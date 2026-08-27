using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class RemoteWindowMediaAttachment :
    IRemoteWindowMediaSink,
    IAsyncDisposable
{
    public static TimeSpan DefaultHandshakeTimeout { get; } = TimeSpan.FromSeconds(2);

    public static TimeSpan MaximumHandshakeTimeout { get; } = TimeSpan.FromSeconds(10);

    private readonly SecureRemoteWindowMediaChannel channel;

    internal RemoteWindowMediaAttachment(
        RemoteWindowMediaRouteBinding binding,
        SecureRemoteWindowMediaChannel channel)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
    }

    public RemoteWindowMediaRouteBinding Binding { get; }

    public static ValueTask<RemoteWindowMediaAttachment> ConnectAsync(
        Stream stream,
        RemoteWindowMediaRouteBinding binding,
        SecureFrameSession ownedMediaSession,
        CancellationToken cancellationToken = default) => ConnectAsync(
            stream,
            binding,
            ownedMediaSession,
            DefaultHandshakeTimeout,
            TimeProvider.System,
            initiatorNonce: null,
            cancellationToken);

    public static ValueTask<RemoteWindowMediaAttachment> ConnectAsync(
        Stream stream,
        RemoteWindowMediaRouteBinding binding,
        SecureFrameSession ownedMediaSession,
        TimeSpan handshakeTimeout,
        CancellationToken cancellationToken = default) => ConnectAsync(
            stream,
            binding,
            ownedMediaSession,
            handshakeTimeout,
            TimeProvider.System,
            initiatorNonce: null,
            cancellationToken);

    public ValueTask<RemoteWindowMediaFrame> ReceiveAsync(
        CancellationToken cancellationToken = default) =>
        channel.ReceiveAsync(cancellationToken);

    public ValueTask SendAsync(
        RemoteWindowMediaFrame frame,
        CancellationToken cancellationToken = default) =>
        channel.SendAsync(frame, cancellationToken);

    public ValueTask DisposeAsync() => channel.DisposeAsync();

    public override string ToString() =>
        $"{nameof(RemoteWindowMediaAttachment)} {{ ProtocolVersion = {Binding.ProtocolVersion} }}";

    internal static async ValueTask<RemoteWindowMediaAttachment> ConnectAsync(
        Stream stream,
        RemoteWindowMediaRouteBinding binding,
        SecureFrameSession ownedMediaSession,
        TimeSpan handshakeTimeout,
        TimeProvider timeProvider,
        byte[]? initiatorNonce,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(ownedMediaSession);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ValidateHandshakeTimeout(handshakeTimeout);
        byte[] nonce = initiatorNonce?.ToArray()
            ?? RandomNumberGenerator.GetBytes(
                RemoteWindowMediaAttachmentCodec.NonceBytes);
        byte[]? request = null;
        byte[]? encodedAcknowledgement = null;
        bool transferredToChannel = false;
        using var deadline = new CancellationTokenSource(
            handshakeTimeout,
            timeProvider);
        using CancellationTokenSource operation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadline.Token);
        try
        {
            RemoteWindowMediaAttachmentCodec.ValidateNonce(
                nonce,
                nameof(initiatorNonce));
            request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                binding,
                nonce,
                ownedMediaSession);
            await RemoteWindowMediaAttachmentWire.WriteAsync(
                stream,
                request,
                operation.Token).ConfigureAwait(false);
            encodedAcknowledgement =
                await RemoteWindowMediaAttachmentWire.ReadAsync(
                    stream,
                    RemoteWindowMediaAttachmentCodec.AcknowledgementEnvelopeBytes,
                    operation.Token).ConfigureAwait(false);
            RemoteWindowMediaAttachmentAcknowledgement acknowledgement =
                RemoteWindowMediaAttachmentCodec.DecodeAcknowledgement(
                    encodedAcknowledgement,
                    ownedMediaSession);
            byte[] acknowledgedInitiatorNonce =
                acknowledgement.ExportInitiatorNonce();
            byte[] responderNonce = acknowledgement.ExportResponderNonce();
            try
            {
                if (acknowledgement.Binding != binding
                    || !CryptographicOperations.FixedTimeEquals(
                        nonce,
                        acknowledgedInitiatorNonce)
                    || CryptographicOperations.FixedTimeEquals(
                        nonce,
                        responderNonce))
                {
                    throw new InvalidDataException(
                        "The Remote Window media attachment acknowledgement binding is invalid.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(acknowledgedInitiatorNonce);
                CryptographicOperations.ZeroMemory(responderNonce);
            }

            operation.Token.ThrowIfCancellationRequested();
            var channel = new SecureRemoteWindowMediaChannel(
                stream,
                ownedMediaSession,
                binding.SessionId,
                binding.ActivityId);
            transferredToChannel = true;
            return new RemoteWindowMediaAttachment(binding, channel);
        }
        catch (Exception failure)
        {
            Exception finalFailure = deadline.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested
                    ? new TimeoutException(
                        "The Remote Window media attachment handshake timed out.",
                        failure)
                    : failure;
            Exception? cleanupFailure = CleanupFailedOwnership(
                stream,
                transferredToChannel ? null : ownedMediaSession);
            if (cleanupFailure is not null)
            {
                finalFailure = new AggregateException(
                    "The Remote Window media attachment and cleanup both failed.",
                    finalFailure,
                    cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(finalFailure).Throw();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
            if (request is not null)
            {
                CryptographicOperations.ZeroMemory(request);
            }

            if (encodedAcknowledgement is not null)
            {
                CryptographicOperations.ZeroMemory(encodedAcknowledgement);
            }
        }
    }

    internal static void ValidateHandshakeTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > MaximumHandshakeTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                $"A Remote Window media attachment timeout must be positive and at most {MaximumHandshakeTimeout}.");
        }
    }

    internal static Exception? CleanupFailedOwnership(
        Stream stream,
        SecureFrameSession? mediaSession)
    {
        Exception? failure = null;
        try
        {
            stream.Dispose();
        }
        catch (Exception cleanupFailure)
        {
            failure = cleanupFailure;
        }

        if (mediaSession is not null)
        {
            try
            {
                mediaSession.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                failure = failure is null
                    ? cleanupFailure
                    : new AggregateException(
                    "Remote Window media attachment resource cleanup failed.",
                    failure,
                    cleanupFailure);
            }
        }

        return failure;
    }
}

public sealed class RemoteWindowMediaRouteRegistry : IAsyncDisposable
{
    public const int DefaultMaximumRoutes = 32;
    public const int MaximumRoutesLimit = 128;
    public const int MaximumRememberedNonces = 512;
    public const int MaximumRememberedRouteIds = 512;
    public static TimeSpan DefaultRouteLifetime { get; } = TimeSpan.FromSeconds(30);
    public static TimeSpan MaximumRouteLifetime { get; } = TimeSpan.FromMinutes(2);

    private readonly Lock gate = new();
    private readonly TaskCompletionSource disposalCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<RemoteWindowMediaRouteId, DateTimeOffset>
        consumedRouteIds = [];
    private readonly int maximumRoutes;
    private readonly HashSet<RouteEntry> ownedRoutes = [];
    private readonly RemoteWindowMediaNonceReplayCache rememberedNonces;
    private readonly Dictionary<RemoteWindowMediaRouteId, RouteEntry> routes = [];
    private readonly TimeProvider timeProvider;
    private Exception? completedCleanupFailure;
    private bool disposed;

    public RemoteWindowMediaRouteRegistry(
        int maximumRoutes = DefaultMaximumRoutes,
        TimeProvider? timeProvider = null)
    {
        if (maximumRoutes is < 1 or > MaximumRoutesLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRoutes));
        }

        this.maximumRoutes = maximumRoutes;
        rememberedNonces = new RemoteWindowMediaNonceReplayCache(
            MaximumRememberedNonces,
            MaximumRouteLifetime);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int Count
    {
        get
        {
            lock (gate)
            {
                return routes.Count;
            }
        }
    }

    public RemoteWindowMediaRouteRegistration RegisterOwnedRoute(
        RemoteWindowMediaRouteBinding binding,
        SecureFrameSession ownedMediaSession,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(ownedMediaSession);
        TimeSpan effectiveLifetime = lifetime ?? DefaultRouteLifetime;
        RouteEntry? entry = null;
        bool admitted = false;
        try
        {
            if (effectiveLifetime <= TimeSpan.Zero
                || effectiveLifetime > MaximumRouteLifetime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(lifetime),
                    $"A Remote Window media route lifetime must be positive and at most {MaximumRouteLifetime}.");
            }

            if (!binding.RouteId.MatchesSession(ownedMediaSession))
            {
                throw new InvalidOperationException(
                    "The Remote Window media route does not match the owned media session.");
            }

            entry = new RouteEntry(
                binding,
                ownedMediaSession,
                checked(timeProvider.GetUtcNow() + effectiveLifetime));
            entry.ExpiryTimer = timeProvider.CreateTimer(
                static state =>
                {
                    var callback = (ExpiryCallback)state!;
                    callback.Registry.Expire(callback.Entry);
                },
                new ExpiryCallback(this, entry),
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                DateTimeOffset now = timeProvider.GetUtcNow();
                PruneConsumedRouteIds(now);
                if (ownedRoutes.Count >= maximumRoutes)
                {
                    throw new InvalidOperationException(
                        "The Remote Window media route registry is at capacity.");
                }

                if (ownedRoutes.Any(route =>
                        route.Binding.RouteId.Equals(binding.RouteId))
                    || consumedRouteIds.ContainsKey(binding.RouteId))
                {
                    throw new InvalidOperationException(
                        "The Remote Window media route identifier has already been consumed.");
                }

                if (consumedRouteIds.Count >= MaximumRememberedRouteIds)
                {
                    throw new InvalidOperationException(
                        "The Remote Window media route history is at capacity.");
                }

                if (!routes.TryAdd(binding.RouteId, entry))
                {
                    throw new InvalidOperationException(
                        "A Remote Window media route is already registered for this control connection.");
                }

                consumedRouteIds.Add(
                    binding.RouteId,
                    checked(now + MaximumRouteLifetime));
                ownedRoutes.Add(entry);

                try
                {
                    if (!entry.ExpiryTimer.Change(
                        effectiveLifetime,
                        Timeout.InfiniteTimeSpan))
                    {
                        throw new InvalidOperationException(
                            "The Remote Window media route expiry could not be armed.");
                    }

                    admitted = true;
                }
                catch
                {
                    RollBackAdmission(entry);
                    throw;
                }
            }

            return new RemoteWindowMediaRouteRegistration(this, entry);
        }
        catch (Exception failure)
        {
            lock (gate)
            {
                if (admitted
                    && entry is not null)
                {
                    RemoveIfCurrent(entry);
                }
                else if (entry is not null)
                {
                    RollBackAdmission(entry);
                }

                if (entry is not null)
                {
                    entry.State = RouteState.Revoked;
                    entry.CleanupStarted = true;
                    entry.MediaSession = null;
                }
            }

            Exception? cleanupFailure = DisposeExpiryTimer(entry);
            try
            {
                ownedMediaSession.Dispose();
            }
            catch (Exception sessionCleanupFailure)
            {
                cleanupFailure = CombineFailures(
                    cleanupFailure,
                    sessionCleanupFailure,
                    "Remote Window media route cleanup failed.");
            }

            // No registration escaped admission, so cleanup failures are
            // propagated below instead of retained on an unreachable task.
            if (entry is not null)
            {
                lock (gate)
                {
                    ownedRoutes.Remove(entry);
                    entry.CleanupCompletion.TrySetResult();
                }
            }

            if (cleanupFailure is not null)
            {
                failure = new AggregateException(
                    "Remote Window media route admission and cleanup both failed.",
                    failure,
                    cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    public ValueTask<RemoteWindowMediaAttachment> AcceptAsync(
        Stream stream,
        CancellationToken cancellationToken = default) => AcceptAsync(
            stream,
            initialEnvelope: null,
            RemoteWindowMediaAttachment.DefaultHandshakeTimeout,
            responderNonce: null,
            cancellationToken);

    public ValueTask<RemoteWindowMediaAttachment> AcceptAsync(
        Stream stream,
        TimeSpan handshakeTimeout,
        CancellationToken cancellationToken = default) => AcceptAsync(
            stream,
            initialEnvelope: null,
            handshakeTimeout,
            responderNonce: null,
            cancellationToken);

    public ValueTask DisposeAsync()
    {
        RouteEntry[]? active = null;
        Exception? priorCleanupFailure = null;
        lock (gate)
        {
            if (!disposed)
            {
                disposed = true;
                active = ownedRoutes.ToArray();
                priorCleanupFailure = completedCleanupFailure;
                completedCleanupFailure = null;
                routes.Clear();
                rememberedNonces.Clear();
                consumedRouteIds.Clear();
            }
        }

        if (active is not null)
        {
            _ = CompleteRegistryDisposalAsync(active, priorCleanupFailure);
        }

        return new ValueTask(disposalCompletion.Task);
    }

    private async Task CompleteRegistryDisposalAsync(
        RouteEntry[] active,
        Exception? priorCleanupFailure)
    {
        List<RouteEntry> cleanupStarts = [];
        Task[] cleanupTasks = new Task[active.Length];
        lock (gate)
        {
            for (var index = 0; index < active.Length; index++)
            {
                RouteEntry entry = active[index];
                RemoveIfCurrent(entry);
                if (!entry.CleanupStarted)
                {
                    entry.CleanupStarted = true;
                    entry.State = RouteState.Revoked;
                    cleanupStarts.Add(entry);
                }

                cleanupTasks[index] = entry.CleanupCompletion.Task;
            }
        }

        foreach (RouteEntry entry in cleanupStarts)
        {
            _ = CompleteRevocationAsync(entry);
        }

        List<Exception>? failures = priorCleanupFailure is null
            ? null
            : [priorCleanupFailure];
        foreach (Task cleanup in cleanupTasks)
        {
            try
            {
                await cleanup.ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }

        if (failures is not null)
        {
            disposalCompletion.TrySetException(new AggregateException(
                "One or more Remote Window media routes failed to close.",
                failures));
        }
        else
        {
            disposalCompletion.TrySetResult();
        }
    }

    internal ValueTask<RemoteWindowMediaAttachment> AcceptAsync(
        Stream stream,
        ReadOnlyMemory<byte> initialEnvelope,
        TimeSpan handshakeTimeout,
        CancellationToken cancellationToken = default) => AcceptAsync(
            stream,
            initialEnvelope,
            handshakeTimeout,
            responderNonce: null,
            cancellationToken);

    internal bool IsAttached(RouteEntry entry)
    {
        lock (gate)
        {
            return entry.State == RouteState.Attached;
        }
    }

    internal ValueTask RevokeAsync(RouteEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        bool startCleanup;
        lock (gate)
        {
            RemoveIfCurrent(entry);
            startCleanup = !entry.CleanupStarted;
            if (startCleanup)
            {
                entry.CleanupStarted = true;
                entry.State = RouteState.Revoked;
            }
        }

        if (startCleanup)
        {
            _ = CompleteRevocationAsync(entry);
        }

        return new ValueTask(entry.CleanupCompletion.Task);
    }

    private async ValueTask<RemoteWindowMediaAttachment> AcceptAsync(
        Stream stream,
        ReadOnlyMemory<byte>? initialEnvelope,
        TimeSpan handshakeTimeout,
        byte[]? responderNonce,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        RemoteWindowMediaAttachment.ValidateHandshakeTimeout(handshakeTimeout);
        byte[]? ownedEnvelope = null;
        RouteEntry? entry = null;
        SecureFrameSession? mediaSession = null;
        bool transferredToChannel = false;
        RemoteWindowMediaAttachment? createdAttachment = null;
        Exception? settlementCleanupFailure = null;
        Exception? cleanupFailureToComplete = null;
        bool completeCleanupAfterSettlement = false;
        using var deadline = new CancellationTokenSource(
            handshakeTimeout,
            timeProvider);
        using CancellationTokenSource operation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadline.Token);
        try
        {
            if (initialEnvelope.HasValue
                && initialEnvelope.Value.Length
                    != RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes)
            {
                throw new InvalidDataException(
                    "The Remote Window media attachment pre-read envelope length is invalid.");
            }

            ownedEnvelope = initialEnvelope?.ToArray()
                ?? await RemoteWindowMediaAttachmentWire.ReadAsync(
                    stream,
                    RemoteWindowMediaAttachmentCodec.RequestEnvelopeBytes,
                    operation.Token).ConfigureAwait(false);
            if (!RemoteWindowMediaAttachmentCodec.TryReadRouteLocator(
                    ownedEnvelope,
                    out RemoteWindowMediaRouteId routeId))
            {
                throw new InvalidDataException(
                    "The Remote Window media attachment locator is invalid.");
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (!routes.TryGetValue(routeId, out entry))
                {
                    throw new InvalidDataException(
                        "The Remote Window media attachment route is not current.");
                }

                if (entry.State != RouteState.Pending)
                {
                    entry = null;
                    throw new InvalidDataException(
                        "The Remote Window media attachment route is already in use.");
                }

                entry.State = RouteState.Validating;
                entry.AcceptStarted = true;
                entry.CandidateStream = stream;
                mediaSession = entry.MediaSession
                    ?? throw new InvalidOperationException(
                        "The Remote Window media route has no pending media session.");
            }

            RemoteWindowMediaAttachmentPrefix prefix =
                RemoteWindowMediaAttachmentCodec.DecodeRequestPrefix(
                    ownedEnvelope);
            if (!prefix.RouteId.Equals(entry.Binding.RouteId)
                || prefix.ProtocolVersion != entry.Binding.ProtocolVersion
                || now >= entry.ExpiresAt)
            {
                throw new InvalidDataException(
                    "The Remote Window media attachment route is not current.");
            }

            RemoteWindowMediaAttachmentRequest request =
                RemoteWindowMediaAttachmentCodec.DecodeRequest(
                    ownedEnvelope,
                    mediaSession);
            byte[] initiatorNonce = request.ExportInitiatorNonce();
            byte[] acknowledgementNonce = responderNonce?.ToArray()
                ?? RandomNumberGenerator.GetBytes(
                    RemoteWindowMediaAttachmentCodec.NonceBytes);
            try
            {
                if (request.Binding != entry.Binding)
                {
                    throw new InvalidDataException(
                        "The Remote Window media attachment binding is not current.");
                }

                RemoteWindowMediaAttachmentCodec.ValidateNonce(
                    acknowledgementNonce,
                    nameof(responderNonce));
                if (CryptographicOperations.FixedTimeEquals(
                    initiatorNonce,
                    acknowledgementNonce))
                {
                    throw new InvalidDataException(
                        "The Remote Window media attachment nonces must be independent.");
                }

                lock (gate)
                {
                    now = timeProvider.GetUtcNow();
                    if (entry.State != RouteState.Validating
                        || now >= entry.ExpiresAt
                        || !rememberedNonces.TryRemember(
                            initiatorNonce,
                            now))
                    {
                        throw new InvalidDataException(
                            "The Remote Window media attachment nonce or route is not current.");
                    }
                }

                byte[] acknowledgement =
                    RemoteWindowMediaAttachmentCodec.EncodeAcknowledgement(
                        entry.Binding,
                        initiatorNonce,
                        acknowledgementNonce,
                        mediaSession);
                try
                {
                    await RemoteWindowMediaAttachmentWire.WriteAsync(
                        stream,
                        acknowledgement,
                        operation.Token).ConfigureAwait(false);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(acknowledgement);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(initiatorNonce);
                CryptographicOperations.ZeroMemory(acknowledgementNonce);
            }

            var channel = new SecureRemoteWindowMediaChannel(
                stream,
                mediaSession,
                entry.Binding.SessionId,
                entry.Binding.ActivityId);
            createdAttachment = new RemoteWindowMediaAttachment(
                entry.Binding,
                channel);
            lock (gate)
            {
                if (entry.State != RouteState.Validating)
                {
                    throw new IOException(
                        "The Remote Window media route was revoked during attachment.");
                }

                entry.State = RouteState.Attached;
                entry.MediaSession = null;
                entry.CandidateStream = null;
                entry.Attachment = createdAttachment;
                transferredToChannel = true;
            }

            settlementCleanupFailure = DisposeExpiryTimer(entry);
            if (settlementCleanupFailure is not null)
            {
                throw settlementCleanupFailure;
            }

            return createdAttachment;
        }
        catch (Exception failure)
        {
            Exception finalFailure = deadline.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested
                    ? new TimeoutException(
                        "The Remote Window media attachment handshake timed out.",
                        failure)
                    : failure;
            Exception? additionalCleanupFailure = DisposeExpiryTimer(entry);
            if (createdAttachment is not null)
            {
                try
                {
                    await createdAttachment.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception attachmentCleanupFailure)
                {
                    additionalCleanupFailure = CombineFailures(
                        additionalCleanupFailure,
                        attachmentCleanupFailure,
                        "Remote Window media attachment cleanup failed.");
                }
            }
            else
            {
                Exception? ownershipCleanup =
                    RemoteWindowMediaAttachment.CleanupFailedOwnership(
                        stream,
                        transferredToChannel ? null : mediaSession);
                if (ownershipCleanup is not null)
                {
                    additionalCleanupFailure = CombineFailures(
                        additionalCleanupFailure,
                        ownershipCleanup,
                        "Remote Window media attachment cleanup failed.");
                }
            }

            Exception? cleanupFailure = settlementCleanupFailure;
            if (additionalCleanupFailure is not null)
            {
                cleanupFailure = CombineFailures(
                    cleanupFailure,
                    additionalCleanupFailure,
                    "Remote Window media attachment cleanup failed.");
            }

            if (cleanupFailure is not null)
            {
                if (ReferenceEquals(failure, settlementCleanupFailure))
                {
                    if (additionalCleanupFailure is not null)
                    {
                        finalFailure = new AggregateException(
                            "The Remote Window media attachment and cleanup both failed.",
                            finalFailure,
                            additionalCleanupFailure);
                    }
                }
                else
                {
                    finalFailure = new AggregateException(
                        "The Remote Window media attachment and cleanup both failed.",
                        finalFailure,
                        cleanupFailure);
                }
            }

            if (entry is not null)
            {
                lock (gate)
                {
                    RemoveIfCurrent(entry);
                    entry.State = RouteState.Revoked;
                    entry.CandidateStream = null;
                    entry.MediaSession = null;
                    entry.Attachment = null;
                    completeCleanupAfterSettlement = !entry.CleanupStarted;
                    entry.CleanupStarted = true;
                    if (!completeCleanupAfterSettlement
                        && cleanupFailure is not null)
                    {
                        entry.AcceptCleanupFailure = CombineFailures(
                            entry.AcceptCleanupFailure,
                            cleanupFailure,
                            "Remote Window media attachment cleanup failed.");
                    }
                }

                cleanupFailureToComplete = cleanupFailure;
            }

            ExceptionDispatchInfo.Capture(finalFailure).Throw();
            throw;
        }
        finally
        {
            if (ownedEnvelope is not null)
            {
                CryptographicOperations.ZeroMemory(ownedEnvelope);
            }

            if (entry is not null && entry.AcceptStarted)
            {
                entry.AcceptSettled.TrySetResult();
            }

            if (completeCleanupAfterSettlement && entry is not null)
            {
                CompleteCleanup(entry, cleanupFailureToComplete);
            }
        }
    }

    private async Task CompleteRevocationAsync(RouteEntry entry)
    {
        await Task.Yield();
        Exception? failure = null;
        try
        {
            failure = DisposeExpiryTimer(entry);
            Stream? candidate;
            RemoteWindowMediaAttachment? attachment = null;
            SecureFrameSession? mediaSession = null;
            bool waitForAccept;
            lock (gate)
            {
                candidate = entry.CandidateStream;
                waitForAccept = entry.AcceptStarted;
                if (!waitForAccept)
                {
                    attachment = entry.Attachment;
                    entry.Attachment = null;
                    mediaSession = entry.MediaSession;
                    entry.MediaSession = null;
                }
            }

            if (candidate is not null)
            {
                try
                {
                    candidate.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    failure = CombineFailures(
                        failure,
                        cleanupFailure,
                        "Remote Window media route cleanup failed.");
                }
            }

            if (waitForAccept)
            {
                await entry.AcceptSettled.Task.ConfigureAwait(false);
                Exception? acceptCleanupFailure;
                lock (gate)
                {
                    acceptCleanupFailure = entry.AcceptCleanupFailure;
                    entry.AcceptCleanupFailure = null;
                    attachment = entry.Attachment;
                    entry.Attachment = null;
                    mediaSession = entry.MediaSession;
                    entry.MediaSession = null;
                }

                if (acceptCleanupFailure is not null)
                {
                    failure = CombineFailures(
                        failure,
                        acceptCleanupFailure,
                        "Remote Window media route cleanup failed.");
                }
            }

            if (attachment is not null)
            {
                try
                {
                    await attachment.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupFailure)
                {
                    failure = CombineFailures(
                        failure,
                        cleanupFailure,
                        "Remote Window media route cleanup failed.");
                }
            }
            else if (mediaSession is not null)
            {
                try
                {
                    mediaSession.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    failure = CombineFailures(
                        failure,
                        cleanupFailure,
                        "Remote Window media route cleanup failed.");
                }
            }
        }
        finally
        {
            CompleteCleanup(entry, failure);
        }
    }

    private void Expire(RouteEntry entry)
    {
        bool shouldRevoke;
        lock (gate)
        {
            shouldRevoke = entry.State is RouteState.Pending or RouteState.Validating
                && timeProvider.GetUtcNow() >= entry.ExpiresAt;
        }

        if (shouldRevoke)
        {
            _ = ObserveExpiryAsync(entry);
        }
    }

    private async Task ObserveExpiryAsync(RouteEntry entry)
    {
        try
        {
            await RevokeAsync(entry).ConfigureAwait(false);
        }
        catch
        {
            // The registration retains the cleanup failure for its owner.
        }
    }

    private bool RemoveIfCurrent(RouteEntry entry)
    {
        bool removed = routes.TryGetValue(
                entry.Binding.RouteId,
                out RouteEntry? current)
            && ReferenceEquals(current, entry)
            && routes.Remove(entry.Binding.RouteId);
        if (removed)
        {
            return true;
        }

        return false;
    }

    private void RollBackAdmission(RouteEntry entry)
    {
        if (routes.TryGetValue(
                entry.Binding.RouteId,
                out RouteEntry? current)
            && ReferenceEquals(current, entry))
        {
            routes.Remove(entry.Binding.RouteId);
            consumedRouteIds.Remove(entry.Binding.RouteId);
        }

        ownedRoutes.Remove(entry);
    }

    private void CompleteCleanup(RouteEntry entry, Exception? failure)
    {
        lock (gate)
        {
            ownedRoutes.Remove(entry);
            if (failure is null)
            {
                entry.CleanupCompletion.TrySetResult();
            }
            else
            {
                if (!disposed && completedCleanupFailure is null)
                {
                    completedCleanupFailure = failure;
                }

                entry.CleanupCompletion.TrySetException(failure);
            }
        }
    }

    private void PruneConsumedRouteIds(DateTimeOffset now)
    {
        foreach (RemoteWindowMediaRouteId expired in consumedRouteIds
            .Where(pair => now >= pair.Value
                && !ownedRoutes.Any(route =>
                    route.Binding.RouteId.Equals(pair.Key)))
            .Select(static pair => pair.Key)
            .ToArray())
        {
            consumedRouteIds.Remove(expired);
        }
    }

    private Exception? DisposeExpiryTimer(RouteEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        ITimer? timer;
        lock (gate)
        {
            timer = entry.ExpiryTimer;
            entry.ExpiryTimer = null;
        }

        if (timer is null)
        {
            return null;
        }

        try
        {
            timer.Dispose();
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }

    private static Exception CombineFailures(
        Exception? first,
        Exception second,
        string message) => first is null
            ? second
            : new AggregateException(message, first, second);

    internal sealed class RouteEntry(
        RemoteWindowMediaRouteBinding binding,
        SecureFrameSession mediaSession,
        DateTimeOffset expiresAt)
    {
        public bool AcceptStarted { get; set; }

        public TaskCompletionSource AcceptSettled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? AcceptCleanupFailure { get; set; }

        public RemoteWindowMediaAttachment? Attachment { get; set; }

        public RemoteWindowMediaRouteBinding Binding { get; } = binding;

        public Stream? CandidateStream { get; set; }

        public TaskCompletionSource CleanupCompletion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CleanupStarted { get; set; }

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public ITimer? ExpiryTimer { get; set; }

        public SecureFrameSession? MediaSession { get; set; } = mediaSession;

        public RouteState State { get; set; } = RouteState.Pending;
    }

    internal enum RouteState
    {
        Pending,
        Validating,
        Attached,
        Revoked,
    }

    private readonly record struct ExpiryCallback(
        RemoteWindowMediaRouteRegistry Registry,
        RouteEntry Entry);
}

internal sealed class RemoteWindowMediaNonceReplayCache
{
    private readonly Dictionary<NonceFingerprint, DateTimeOffset> entries = [];
    private readonly int maximumEntries;
    private readonly TimeSpan retention;

    public RemoteWindowMediaNonceReplayCache(
        int maximumEntries,
        TimeSpan retention)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            retention,
            TimeSpan.Zero);

        this.maximumEntries = maximumEntries;
        this.retention = retention;
    }

    public int Count => entries.Count;

    public void Clear() => entries.Clear();

    public bool TryRemember(ReadOnlySpan<byte> nonce, DateTimeOffset now)
    {
        if (nonce.Length != RemoteWindowMediaAttachmentCodec.NonceBytes)
        {
            throw new ArgumentException(
                $"A nonce must be exactly {RemoteWindowMediaAttachmentCodec.NonceBytes} bytes.",
                nameof(nonce));
        }

        foreach (NonceFingerprint expired in entries
            .Where(pair => now >= pair.Value)
            .Select(static pair => pair.Key)
            .ToArray())
        {
            entries.Remove(expired);
        }

        NonceFingerprint fingerprint = NonceFingerprint.Create(nonce);
        if (entries.ContainsKey(fingerprint) || entries.Count >= maximumEntries)
        {
            return false;
        }

        entries.Add(fingerprint, checked(now + retention));
        return true;
    }

    private readonly record struct NonceFingerprint(
        ulong A,
        ulong B,
        ulong C,
        ulong D)
    {
        public static NonceFingerprint Create(ReadOnlySpan<byte> nonce)
        {
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(nonce, hash);
            return new NonceFingerprint(
                BinaryPrimitives.ReadUInt64BigEndian(hash),
                BinaryPrimitives.ReadUInt64BigEndian(hash[8..]),
                BinaryPrimitives.ReadUInt64BigEndian(hash[16..]),
                BinaryPrimitives.ReadUInt64BigEndian(hash[24..]));
        }
    }
}

public sealed class RemoteWindowMediaRouteRegistration : IAsyncDisposable
{
    private readonly RemoteWindowMediaRouteRegistry.RouteEntry entry;
    private readonly RemoteWindowMediaRouteRegistry registry;

    internal RemoteWindowMediaRouteRegistration(
        RemoteWindowMediaRouteRegistry registry,
        RemoteWindowMediaRouteRegistry.RouteEntry entry)
    {
        this.registry = registry;
        this.entry = entry;
    }

    public RemoteWindowMediaRouteBinding Binding => entry.Binding;

    public bool IsAttached => registry.IsAttached(entry);

    internal Task CleanupCompletion => entry.CleanupCompletion.Task;

    public ValueTask DisposeAsync() => registry.RevokeAsync(entry);

    public override string ToString() =>
        $"{nameof(RemoteWindowMediaRouteRegistration)} {{ IsAttached = {IsAttached} }}";
}

internal static class RemoteWindowMediaAttachmentWire
{
    private const int LengthPrefixBytes = sizeof(int);

    public static async ValueTask WriteAsync(
        Stream stream,
        ReadOnlyMemory<byte> envelope,
        CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[LengthPrefixBytes];
        BinaryPrimitives.WriteInt32BigEndian(prefix, envelope.Length);
        await WriteOwnedAsync(
                stream,
                prefix,
                cancellationToken)
            .ConfigureAwait(false);
        await WriteOwnedAsync(
                stream,
                envelope,
                cancellationToken)
            .ConfigureAwait(false);
        Task flushing = stream.FlushAsync(cancellationToken);
        await WaitAndObserveAsync(
                flushing,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async ValueTask<byte[]> ReadAsync(
        Stream stream,
        int expectedEnvelopeBytes,
        CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[LengthPrefixBytes];
        Task prefixRead = stream.ReadExactlyAsync(prefix, cancellationToken)
            .AsTask();
        await WaitAndObserveAsync(
                prefixRead,
                cancellationToken)
            .ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length != expectedEnvelopeBytes
            || length > RemoteWindowMediaAttachmentCodec.MaximumEnvelopeBytes)
        {
            throw new InvalidDataException(
                "The Remote Window media attachment wire length is invalid.");
        }

        byte[] envelope = GC.AllocateUninitializedArray<byte>(length);
        Task? envelopeRead = null;
        try
        {
            envelopeRead = stream.ReadExactlyAsync(envelope, cancellationToken)
                .AsTask();
            await WaitAndObserveAsync(
                    envelopeRead,
                    cancellationToken)
                .ConfigureAwait(false);
            return envelope;
        }
        catch
        {
            if (envelopeRead is { IsCompleted: false })
            {
                _ = ZeroWhenCompletedAsync(envelopeRead, envelope);
            }
            else
            {
                CryptographicOperations.ZeroMemory(envelope);
            }

            throw;
        }
    }

    private static async ValueTask WriteOwnedAsync(
        Stream stream,
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        byte[] owned = buffer.ToArray();
        Task? writing = null;
        bool zeroDeferred = false;
        try
        {
            writing = stream.WriteAsync(owned, cancellationToken).AsTask();
            await WaitAndObserveAsync(
                    writing,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (writing is { IsCompleted: false })
            {
                zeroDeferred = true;
                _ = ZeroWhenCompletedAsync(writing, owned);
            }

            throw;
        }
        finally
        {
            if (!zeroDeferred)
            {
                CryptographicOperations.ZeroMemory(owned);
            }
        }
    }

    private static async ValueTask WaitAndObserveAsync(
        Task operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _ = ObserveWhenCompletedAsync(operation);

            throw;
        }
    }

    private static async Task ObserveWhenCompletedAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static async Task ZeroWhenCompletedAsync(
        Task operation,
        byte[] buffer)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }
}

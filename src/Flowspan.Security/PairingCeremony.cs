using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;

namespace Flowspan.Security;

public interface IPairingMessageChannel : IAsyncDisposable
{
    public ValueTask<byte[]> ReceiveAsync(
        CancellationToken cancellationToken = default);

    public ValueTask SendAsync(
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default);
}

public interface IPairingDecisionSource
{
    public ValueTask<PairingDecision> DecideAsync(
        PairingConfirmationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PairingDecision
{
    public PairingDecision(
        bool accepted,
        CapabilityGrant capabilitiesGrantedToPeer)
    {
        ArgumentNullException.ThrowIfNull(capabilitiesGrantedToPeer);
        if (!accepted && capabilitiesGrantedToPeer.Capabilities.Count != 0)
        {
            throw new ArgumentException(
                "A rejected pairing decision cannot grant capabilities.",
                nameof(capabilitiesGrantedToPeer));
        }

        Accepted = accepted;
        CapabilitiesGrantedToPeer = capabilitiesGrantedToPeer;
    }

    public bool Accepted { get; }

    public CapabilityGrant CapabilitiesGrantedToPeer { get; }

    public static PairingDecision Reject { get; } = new(
        accepted: false,
        CapabilityGrant.None);
}

public sealed record PairingConfirmationRequest(
    PublicDeviceIdentity PeerIdentity,
    ProtocolVersion ProtocolVersion,
    string ShortAuthenticationString,
    DateTimeOffset ExpiresAt);

public sealed class PairingCeremonyProfile
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(10);

    public PairingCeremonyProfile(
        IEnumerable<ProtocolVersion> supportedVersions,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(supportedVersions);
        ImmutableArray<ProtocolVersion> versions = supportedVersions
            .Distinct()
            .Order()
            .ToImmutableArray();
        if (versions.IsDefaultOrEmpty
            || versions.Length > 16
            || versions.Any(static version => version.Major < 1 || version.Minor < 0))
        {
            throw new ArgumentException(
                "A pairing ceremony must support 1 to 16 initialized protocol versions.",
                nameof(supportedVersions));
        }

        TimeSpan boundedTimeout = timeout ?? DefaultTimeout;
        if (boundedTimeout <= TimeSpan.Zero || boundedTimeout > MaximumTimeout)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        SupportedVersions = versions;
        Timeout = boundedTimeout;
    }

    public ImmutableArray<ProtocolVersion> SupportedVersions { get; }

    public TimeSpan Timeout { get; }
}

public sealed record PairingCeremonyResult(
    bool Succeeded,
    PairingFailure Failure,
    PublicDeviceIdentity? PeerIdentity,
    ProtocolVersion? ProtocolVersion,
    TrustRegistrationResult? TrustRegistration);

public sealed class PairingCeremony
{
    private readonly IPairingDecisionSource decisions;
    private readonly PairingCeremonyProfile profile;
    private readonly TimeProvider timeProvider;
    private readonly ITrustStore trustStore;

    public PairingCeremony(
        PairingCeremonyProfile profile,
        IPairingDecisionSource decisions,
        ITrustStore trustStore,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(trustStore);
        this.profile = profile;
        this.decisions = decisions;
        this.trustStore = trustStore;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ValueTask<PairingCeremonyResult> RunInitiatorAsync(
        IPairingMessageChannel channel,
        DeviceIdentity localIdentity,
        CancellationToken cancellationToken = default) => RunOwnedAsync(
        channel,
        localIdentity,
        SecureSessionRole.Initiator,
        cancellationToken);

    public ValueTask<PairingCeremonyResult> RunResponderAsync(
        IPairingMessageChannel channel,
        DeviceIdentity localIdentity,
        CancellationToken cancellationToken = default) => RunOwnedAsync(
        channel,
        localIdentity,
        SecureSessionRole.Responder,
        cancellationToken);

    private async ValueTask<PairingCeremonyResult> RunCoreAsync(
        IPairingMessageChannel channel,
        DeviceIdentity localIdentity,
        SecureSessionRole localRole,
        CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = startedAt.Add(profile.Timeout);
        using var deadline = new CancellationTokenSource(profile.Timeout, timeProvider);
        using CancellationTokenSource stop =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadline.Token);
        try
        {
            PairingHello localHello = CreateLocalHello(localIdentity, localRole);
            PairingHello peerHello = await ExchangeHelloAsync(
                channel,
                localHello,
                localRole,
                stop.Token).ConfigureAwait(false);
            SecureSessionRole expectedPeerRole = Opposite(localRole);
            if (peerHello.Role != expectedPeerRole
                || peerHello.Party.Identity.DeviceId == localIdentity.DeviceId)
            {
                return Failure(PairingFailure.InvalidMessage, peerHello.Party.Identity);
            }

            ProtocolNegotiationResult negotiation = ProtocolNegotiator.Negotiate(
                localHello.ProtocolVersions,
                peerHello.ProtocolVersions);
            if (!negotiation.Succeeded)
            {
                return Failure(
                    PairingFailure.NoCommonProtocolVersion,
                    peerHello.Party.Identity);
            }

            PairingTranscript transcript = localRole == SecureSessionRole.Initiator
                ? PairingTranscript.Create(
                    localHello.Party,
                    peerHello.Party,
                    negotiation.Version)
                : PairingTranscript.Create(
                    peerHello.Party,
                    localHello.Party,
                    negotiation.Version);
            PairingTranscriptSignature localSignature =
                PairingTranscriptSignature.Create(transcript, localIdentity);
            PairingTranscriptSignature peerSignature;
            if (localRole == SecureSessionRole.Initiator)
            {
                await SendAsync(
                    channel,
                    PairingWireCodec.EncodeTranscriptSignature(localSignature),
                    stop.Token).ConfigureAwait(false);
                peerSignature = await ReceiveTranscriptSignatureAsync(
                    channel,
                    stop.Token).ConfigureAwait(false);
            }
            else
            {
                peerSignature = await ReceiveTranscriptSignatureAsync(
                    channel,
                    stop.Token).ConfigureAwait(false);
                if (!peerSignature.Verify(transcript, peerHello.Party.Identity))
                {
                    return Failure(
                        PairingFailure.InvalidTranscriptSignature,
                        peerHello.Party.Identity,
                        negotiation.Version);
                }

                if (HasIdentityChanged(peerHello.Party.Identity))
                {
                    return Failure(
                        PairingFailure.IdentityChanged,
                        peerHello.Party.Identity,
                        negotiation.Version);
                }

                await SendAsync(
                    channel,
                    PairingWireCodec.EncodeTranscriptSignature(localSignature),
                    stop.Token).ConfigureAwait(false);
            }

            if (localRole == SecureSessionRole.Initiator
                && !peerSignature.Verify(transcript, peerHello.Party.Identity))
            {
                return Failure(
                    PairingFailure.InvalidTranscriptSignature,
                    peerHello.Party.Identity,
                    negotiation.Version);
            }

            if (localRole == SecureSessionRole.Initiator
                && HasIdentityChanged(peerHello.Party.Identity))
            {
                return Failure(
                    PairingFailure.IdentityChanged,
                    peerHello.Party.Identity,
                    negotiation.Version);
            }

            ConfirmationExchange confirmations = await ExchangeConfirmationsAsync(
                channel,
                localIdentity,
                peerHello.Party.Identity,
                transcript,
                negotiation.Version,
                expiresAt,
                stop.Token).ConfigureAwait(false);
            if (confirmations.Failure != PairingFailure.None)
            {
                return Failure(
                    confirmations.Failure,
                    peerHello.Party.Identity,
                    negotiation.Version);
            }

            PairingCompletionProof localCompletion = PairingCompletionProof.Create(
                localIdentity,
                transcript);
            PairingCompletionProof peerCompletion;
            if (localRole == SecureSessionRole.Initiator)
            {
                await SendAsync(
                    channel,
                    PairingWireCodec.EncodeCompletionProof(localCompletion),
                    stop.Token).ConfigureAwait(false);
                peerCompletion = await ReceiveCompletionProofAsync(
                    channel,
                    stop.Token).ConfigureAwait(false);
            }
            else
            {
                peerCompletion = await ReceiveCompletionProofAsync(
                    channel,
                    stop.Token).ConfigureAwait(false);
                if (!peerCompletion.Verify(peerHello.Party.Identity, transcript))
                {
                    return Failure(
                        PairingFailure.InvalidCompletionProof,
                        peerHello.Party.Identity,
                        negotiation.Version);
                }

                await SendAsync(
                    channel,
                    PairingWireCodec.EncodeCompletionProof(localCompletion),
                    stop.Token).ConfigureAwait(false);
            }

            if (localRole == SecureSessionRole.Initiator
                && !peerCompletion.Verify(peerHello.Party.Identity, transcript))
            {
                return Failure(
                    PairingFailure.InvalidCompletionProof,
                    peerHello.Party.Identity,
                    negotiation.Version);
            }

            CapabilityGrant capabilitiesToResponder =
                localRole == SecureSessionRole.Initiator
                    ? confirmations.LocalDecision.CapabilitiesGrantedToPeer
                    : CapabilityGrant.None;
            CapabilityGrant capabilitiesToInitiator =
                localRole == SecureSessionRole.Responder
                    ? confirmations.LocalDecision.CapabilitiesGrantedToPeer
                    : CapabilityGrant.None;
            PairingOutcome outcome = PairingVerifier.EstablishTrust(
                transcript,
                localRole == SecureSessionRole.Initiator
                    ? localSignature.Signature
                    : peerSignature.Signature,
                localRole == SecureSessionRole.Responder
                    ? localSignature.Signature
                    : peerSignature.Signature,
                localRole == SecureSessionRole.Initiator
                    ? confirmations.LocalConfirmation
                    : confirmations.PeerConfirmation,
                localRole == SecureSessionRole.Responder
                    ? confirmations.LocalConfirmation
                    : confirmations.PeerConfirmation,
                capabilitiesToResponder,
                capabilitiesToInitiator,
                timeProvider.GetUtcNow(),
                expiresAt);
            if (!outcome.Succeeded)
            {
                return Failure(
                    outcome.Failure,
                    peerHello.Party.Identity,
                    negotiation.Version);
            }

            TrustRecord localTrust = localRole == SecureSessionRole.Initiator
                ? outcome.InitiatorTrust!
                : outcome.ResponderTrust!;
            TrustRegistrationResult registration = await trustStore.RegisterAsync(
                localTrust,
                stop.Token).ConfigureAwait(false);
            if (registration == TrustRegistrationResult.IdentityChanged)
            {
                return Failure(
                    PairingFailure.IdentityChanged,
                    peerHello.Party.Identity,
                    negotiation.Version);
            }

            return new PairingCeremonyResult(
                true,
                PairingFailure.None,
                peerHello.Party.Identity,
                negotiation.Version,
                registration);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested
            && deadline.IsCancellationRequested)
        {
            return Failure(PairingFailure.Timeout);
        }
        catch (PairingMessageException)
        {
            return Failure(PairingFailure.InvalidMessage);
        }
    }

    private async ValueTask<PairingCeremonyResult> RunOwnedAsync(
        IPairingMessageChannel channel,
        DeviceIdentity localIdentity,
        SecureSessionRole localRole,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(localIdentity);
        PairingCeremonyResult? result = null;
        Exception? failure = null;
        try
        {
            result = await RunCoreAsync(
                channel,
                localIdentity,
                localRole,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception cleanupFailure)
        {
            failure = failure is null
                ? cleanupFailure
                : new AggregateException(
                    "The pairing ceremony and channel cleanup both failed.",
                    failure,
                    cleanupFailure);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result!;
    }

    private PairingHello CreateLocalHello(
        DeviceIdentity localIdentity,
        SecureSessionRole localRole)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(PairingParty.NonceLength);
        try
        {
            return PairingHello.Create(
                localRole,
                new PairingParty(localIdentity.PublicIdentity, nonce),
                profile.SupportedVersions);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    private bool HasIdentityChanged(PublicDeviceIdentity peerIdentity) =>
        trustStore.TryGet(peerIdentity.DeviceId, out TrustRecord? existingTrust)
        && !existingTrust.PeerIdentity.HasSameKey(peerIdentity);

    private async ValueTask<ConfirmationExchange> ExchangeConfirmationsAsync(
        IPairingMessageChannel channel,
        DeviceIdentity localIdentity,
        PublicDeviceIdentity peerIdentity,
        PairingTranscript transcript,
        ProtocolVersion protocolVersion,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource exchangeStop =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<PairingDecision> localDecisionTask = decisions.DecideAsync(
            new PairingConfirmationRequest(
                peerIdentity,
                protocolVersion,
                transcript.ShortAuthenticationString,
                expiresAt),
            exchangeStop.Token).AsTask();
        Task<PairingConfirmation> peerConfirmationTask =
            ReceiveConfirmationAsync(channel, exchangeStop.Token).AsTask();
        try
        {
            Task first = await Task.WhenAny(localDecisionTask, peerConfirmationTask)
                .ConfigureAwait(false);

            PairingConfirmation? peerConfirmation = null;
            if (first == peerConfirmationTask)
            {
                peerConfirmation = await peerConfirmationTask.ConfigureAwait(false);
                if (!peerConfirmation.Verify(peerIdentity, transcript))
                {
                    exchangeStop.Cancel();
                    await ObservePromptCancellationAsync(localDecisionTask)
                        .ConfigureAwait(false);
                    return ConfirmationExchange.Failed(
                        PairingFailure.InvalidConfirmation);
                }

                if (!peerConfirmation.Accepted)
                {
                    exchangeStop.Cancel();
                    await ObservePromptCancellationAsync(localDecisionTask)
                        .ConfigureAwait(false);
                    PairingConfirmation localRejection = PairingConfirmation.Create(
                        localIdentity,
                        transcript,
                        accepted: false);
                    await SendAsync(
                        channel,
                        PairingWireCodec.EncodeConfirmation(localRejection),
                        cancellationToken).ConfigureAwait(false);
                    return ConfirmationExchange.Failed(PairingFailure.Rejected);
                }
            }

            PairingDecision localDecision = await localDecisionTask.ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "The pairing decision source returned null.");
            PairingConfirmation localConfirmation = PairingConfirmation.Create(
                localIdentity,
                transcript,
                localDecision.Accepted);
            await SendAsync(
                channel,
                PairingWireCodec.EncodeConfirmation(localConfirmation),
                cancellationToken).ConfigureAwait(false);

            peerConfirmation ??= await peerConfirmationTask.ConfigureAwait(false);
            if (!peerConfirmation.Verify(peerIdentity, transcript))
            {
                return ConfirmationExchange.Failed(PairingFailure.InvalidConfirmation);
            }

            if (!localDecision.Accepted || !peerConfirmation.Accepted)
            {
                return ConfirmationExchange.Failed(PairingFailure.Rejected);
            }

            return new ConfirmationExchange(
                localDecision,
                localConfirmation,
                peerConfirmation,
                PairingFailure.None);
        }
        catch (Exception primaryFailure)
        {
            exchangeStop.Cancel();
            List<Exception> drainFailures = await DrainConcurrentTasksAsync(
                [localDecisionTask, peerConfirmationTask],
                primaryFailure,
                exchangeStop.Token).ConfigureAwait(false);
            if (drainFailures.Count > 0)
            {
                throw new AggregateException(
                    "The pairing confirmation exchange and concurrent cleanup both failed.",
                    [primaryFailure, .. drainFailures]);
            }

            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
            throw new InvalidOperationException("Unreachable pairing failure path.");
        }
    }

    private static async ValueTask<PairingHello> ExchangeHelloAsync(
        IPairingMessageChannel channel,
        PairingHello localHello,
        SecureSessionRole localRole,
        CancellationToken cancellationToken)
    {
        if (localRole == SecureSessionRole.Initiator)
        {
            await SendAsync(
                channel,
                PairingWireCodec.EncodeHello(localHello),
                cancellationToken).ConfigureAwait(false);
            return await ReceiveHelloAsync(channel, cancellationToken)
                .ConfigureAwait(false);
        }

        PairingHello peerHello = await ReceiveHelloAsync(channel, cancellationToken)
            .ConfigureAwait(false);
        await SendAsync(
            channel,
            PairingWireCodec.EncodeHello(localHello),
            cancellationToken).ConfigureAwait(false);
        return peerHello;
    }

    private static PairingCeremonyResult Failure(
        PairingFailure failure,
        PublicDeviceIdentity? peerIdentity = null,
        ProtocolVersion? protocolVersion = null) => new(
        false,
        failure,
        peerIdentity,
        protocolVersion,
        null);

    private static async ValueTask<List<Exception>> DrainConcurrentTasksAsync(
        IEnumerable<Task> tasks,
        Exception primaryFailure,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (Task task in tasks)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                // Expected after the sibling confirmation operation failed.
            }
            catch (Exception exception) when (ReferenceEquals(exception, primaryFailure))
            {
                // The task that produced the primary failure has now been observed.
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        return failures;
    }

    private static async ValueTask ObservePromptCancellationAsync(
        Task<PairingDecision> prompt)
    {
        try
        {
            await prompt.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Peer rejection or invalid confirmation canceled the local prompt.
        }
    }

    private static SecureSessionRole Opposite(SecureSessionRole role) => role switch
    {
        SecureSessionRole.Initiator => SecureSessionRole.Responder,
        SecureSessionRole.Responder => SecureSessionRole.Initiator,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static ValueTask<PairingConfirmation> ReceiveConfirmationAsync(
        IPairingMessageChannel channel,
        CancellationToken cancellationToken) => ReceiveDecodedAsync(
        channel,
        static message => PairingWireCodec.DecodeConfirmation(message),
        cancellationToken);

    private static ValueTask<PairingCompletionProof> ReceiveCompletionProofAsync(
        IPairingMessageChannel channel,
        CancellationToken cancellationToken) => ReceiveDecodedAsync(
        channel,
        static message => PairingWireCodec.DecodeCompletionProof(message),
        cancellationToken);

    private static ValueTask<PairingHello> ReceiveHelloAsync(
        IPairingMessageChannel channel,
        CancellationToken cancellationToken) => ReceiveDecodedAsync(
        channel,
        static message => PairingWireCodec.DecodeHello(message),
        cancellationToken);

    private static ValueTask<PairingTranscriptSignature>
        ReceiveTranscriptSignatureAsync(
            IPairingMessageChannel channel,
            CancellationToken cancellationToken) => ReceiveDecodedAsync(
            channel,
            static message => PairingWireCodec.DecodeTranscriptSignature(message),
            cancellationToken);

    private static async ValueTask<T> ReceiveDecodedAsync<T>(
        IPairingMessageChannel channel,
        Func<byte[], T> decode,
        CancellationToken cancellationToken)
    {
        try
        {
            byte[] message = await channel.ReceiveAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                return decode(message);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(message);
            }
        }
        catch (InvalidDataException exception)
        {
            throw new PairingMessageException(exception);
        }
    }

    private static async ValueTask SendAsync(
        IPairingMessageChannel channel,
        byte[] message,
        CancellationToken cancellationToken)
    {
        try
        {
            await channel.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(message);
        }
    }

    private sealed record ConfirmationExchange(
        PairingDecision LocalDecision,
        PairingConfirmation LocalConfirmation,
        PairingConfirmation PeerConfirmation,
        PairingFailure Failure)
    {
        public static ConfirmationExchange Failed(PairingFailure failure) => new(
            PairingDecision.Reject,
            null!,
            null!,
            failure);
    }

    private sealed class PairingMessageException(InvalidDataException innerException) :
        Exception("A pairing message is invalid.", innerException);
}

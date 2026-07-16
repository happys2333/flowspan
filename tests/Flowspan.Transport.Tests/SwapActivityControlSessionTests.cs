using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class SwapActivityControlSessionTests
{
    private static readonly ProtocolVersion Version =
        ProtocolFeatures.ActivitySwapMinimumVersion;

    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId LocalId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId PeerId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly SwapReservationToken LocalToken =
        SwapReservationToken.From(
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"));

    private static readonly SwapReservationToken PeerToken =
        SwapReservationToken.From(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));

    [Fact]
    public async Task UnsolicitedSwapResultFaultsSessionClosed()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection);
        Task run = session.RunAsync().AsTask();
        SwapActivitySnapshotQuery query = CreateSnapshotQuery(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()));
        SwapActivitySnapshotResult result = SwapActivitySnapshotResult.Success(
            LocalId,
            query,
            CreateActivity(PeerId, "Remote"));

        connection.Receive(SwapControlMessageCodec.CreateSnapshotResult(
            Version,
            PeerId,
            result,
            Now));

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains("unsolicited Swap snapshot", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExpiredUnknownAbortEnvelopeCannotCreateEndpointTombstone()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var endpoint = new InMemorySwapEndpoint(
            LocalId,
            new InMemoryActivityCatalog());
        var peer = new AuthorizedSwapEndpoint(new FixedClock(Now), endpoint);
        peer.SetPeerGrant(PeerId, CapabilityGrant.Of(Capability.ActivitySwap));
        await using var session = new ActivityControlSession(
            connection,
            new RejectingActivityPeer(LocalId),
            replacePeer: null,
            replaceInventoryPeer: null,
            peer,
            new FixedTimeProvider(Now));
        Task run = session.RunAsync().AsTask();
        CorrelationId correlationId = CorrelationId.From(Guid.NewGuid());
        SwapDecision abort = SwapDecision.Create(
            OperationId.From(Guid.NewGuid()),
            SwapDecisionOutcome.Abort,
            Now.AddSeconds(-31),
            [
                SwapDecisionParticipant.Create(LocalId, LocalToken),
                SwapDecisionParticipant.Create(PeerId, PeerToken),
            ],
            FailureCode.PeerUnavailable);

        connection.Receive(SwapControlMessageCodec.CreateDecision(
            Version,
            PeerId,
            correlationId,
            LocalId,
            abort,
            Now.AddSeconds(-31)));

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains("envelope lifetime", failure.Message, StringComparison.Ordinal);
        Assert.False(endpoint.TryGetDecision(abort.OperationId, out _));
    }

    [Fact]
    public async Task SameCorrelationForgedCrossOperationDecisionResultFaultsClosed()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection);
        Task run = session.RunAsync().AsTask();
        CorrelationId correlationId = CorrelationId.From(Guid.NewGuid());
        SwapDecision expected = CreateDecision(OperationId.From(Guid.NewGuid()));
        ValueTask<SwapDeliveryResult<SwapApplyResult>> applying =
            session.ApplyDecisionAsync(
                LocalId,
                correlationId,
                expected,
                CancellationToken.None);
        _ = await connection.ReadSentAsync();
        SwapDecision forged = CreateDecision(OperationId.From(Guid.NewGuid()));

        connection.Receive(SwapControlMessageCodec.CreateDecisionResult(
            Version,
            PeerId,
            LocalId,
            correlationId,
            forged,
            SwapApplyResult.Success(SwapReservationPhase.Committed),
            Now));

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains(
            "does not match the pending request",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await applying).Status);
    }

    [Fact]
    public async Task PendingSwapReservesCorrelationAcrossHandoffAndReplace()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection);
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        CorrelationId correlationId = CorrelationId.From(Guid.NewGuid());
        SwapActivitySnapshotQuery query = CreateSnapshotQuery(
            OperationId.From(Guid.NewGuid()),
            correlationId);
        ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>> querying =
            session.QueryActivityAsync(LocalId, query, CancellationToken.None);
        _ = await connection.ReadSentAsync();
        ActivityTransferOffer offer = CreateOffer(correlationId);
        ReplaceActivityCommand replace = CreateReplaceCommand(correlationId);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((IActivityChannel)session)
                .SendAsync(LocalId, offer, CancellationToken.None)
                .AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((IReplaceChannel)session)
                .SendAsync(LocalId, replace, CancellationToken.None)
                .AsTask());

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await querying).Status);
    }

    [Fact]
    public async Task SessionLossReducesPendingSnapshotToAcknowledgementLost()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection);
        Task run = session.RunAsync().AsTask();
        SwapActivitySnapshotQuery query = CreateSnapshotQuery(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()));

        ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>> querying =
            session.QueryActivityAsync(LocalId, query, CancellationToken.None);
        _ = await connection.ReadSentAsync();
        session.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await querying).Status);
    }

    [Fact]
    public async Task SessionLossReducesPendingPrepareToAcknowledgementLost()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection);
        Task run = session.RunAsync().AsTask();
        SwapPrepareCommand command = CreatePrepareCommand(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()));

        ValueTask<SwapDeliveryResult<SwapPrepareResult>> preparing =
            session.PrepareAsync(LocalId, command, CancellationToken.None);
        _ = await connection.ReadSentAsync();
        session.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await preparing).Status);
    }

    [Fact]
    public async Task SessionLossReducesPendingDecisionToAcknowledgementLost()
    {
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection);
        Task run = session.RunAsync().AsTask();
        SwapDecision decision = CreateDecision(OperationId.From(Guid.NewGuid()));

        ValueTask<SwapDeliveryResult<SwapApplyResult>> applying =
            session.ApplyDecisionAsync(
                LocalId,
                CorrelationId.From(Guid.NewGuid()),
                decision,
                CancellationToken.None);
        _ = await connection.ReadSentAsync();
        session.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await applying).Status);
    }

    [Fact]
    public async Task SilentPeerSnapshotTimesOutAtOperationDeadlineAndClosesSession()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection, time);
        Task run = session.RunAsync().AsTask();
        SwapActivitySnapshotQuery query = CreateSnapshotQuery(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()));

        ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>> querying =
            session.QueryActivityAsync(LocalId, query, CancellationToken.None);
        _ = await connection.ReadSentAsync();
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(30));

        SwapDeliveryResult<SwapActivitySnapshotResult> result = await querying;
        Assert.Equal(ActivityDeliveryStatus.AcknowledgementLost, result.Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(
            ActivityDeliveryStatus.NotDelivered,
            (await session.QueryActivityAsync(
                LocalId,
                CreateSnapshotQuery(
                    OperationId.From(Guid.NewGuid()),
                    CorrelationId.From(Guid.NewGuid())),
                CancellationToken.None)).Status);
    }

    [Fact]
    public async Task SnapshotResultAfterDeadlineFailsClosedBeforeTimerCallback()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection, time);
        Task run = session.RunAsync().AsTask();
        SwapActivitySnapshotQuery query = CreateSnapshotQuery(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()));
        ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>> querying =
            session.QueryActivityAsync(LocalId, query, CancellationToken.None);
        _ = await connection.ReadSentAsync();
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        time.AdvanceWithoutFiring(TimeSpan.FromSeconds(30));

        connection.Receive(SwapControlMessageCodec.CreateSnapshotResult(
            Version,
            PeerId,
            SwapActivitySnapshotResult.Success(
                LocalId,
                query,
                CreateActivity(PeerId, "Remote")),
            Now.AddSeconds(1)));

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => run.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Contains("after its deadline", failure.Message, StringComparison.Ordinal);
        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await querying).Status);
    }

    [Fact]
    public async Task SilentPeerPrepareTimesOutAtReservationDeadlineAndClosesSession()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection, time);
        Task run = session.RunAsync().AsTask();
        SwapPrepareCommand command = CreatePrepareCommand(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()));

        ValueTask<SwapDeliveryResult<SwapPrepareResult>> preparing =
            session.PrepareAsync(LocalId, command, CancellationToken.None);
        _ = await connection.ReadSentAsync();
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(30));

        SwapDeliveryResult<SwapPrepareResult> result = await preparing;
        Assert.Equal(ActivityDeliveryStatus.AcknowledgementLost, result.Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task SilentPeerDecisionTimesOutAtAcknowledgementWindowAndClosesSession()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new FakeActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection, time);
        Task run = session.RunAsync().AsTask();
        SwapDecision decision = CreateDecision(OperationId.From(Guid.NewGuid()));

        ValueTask<SwapDeliveryResult<SwapApplyResult>> applying =
            session.ApplyDecisionAsync(
                LocalId,
                CorrelationId.From(Guid.NewGuid()),
                decision,
                CancellationToken.None);
        _ = await connection.ReadSentAsync();
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        time.Advance(SwapControlMessageCodec.DecisionAcknowledgementTimeout);

        SwapDeliveryResult<SwapApplyResult> result = await applying;
        Assert.Equal(ActivityDeliveryStatus.AcknowledgementLost, result.Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task BlockedSwapSendTimesOutAtOperationDeadlineAndClosesSession()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new BlockingSendActivityControlConnection(LocalId, PeerId);
        var session = CreateSession(connection, time);
        Task run = session.RunAsync().AsTask();
        SwapActivitySnapshotQuery query = CreateSnapshotQuery(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()));

        ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>> querying =
            session.QueryActivityAsync(LocalId, query, CancellationToken.None);
        await connection.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(30));

        SwapDeliveryResult<SwapActivitySnapshotResult> result = await querying;
        Assert.Equal(ActivityDeliveryStatus.AcknowledgementLost, result.Status);
        await connection.SendCancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task NonCooperativeSwapSendStillReturnsAtOperationDeadline()
    {
        var time = new ManualTimeProvider(Now);
        var connection = new NonCooperativeSendActivityControlConnection(
            LocalId,
            PeerId);
        var session = CreateSession(connection, time);
        Task run = session.RunAsync().AsTask();
        SwapActivitySnapshotQuery query = CreateSnapshotQuery(
            OperationId.From(Guid.NewGuid()),
            CorrelationId.From(Guid.NewGuid()));

        ValueTask<SwapDeliveryResult<SwapActivitySnapshotResult>> querying =
            session.QueryActivityAsync(LocalId, query, CancellationToken.None);
        await connection.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await time.TimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(1));
        time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(
            ActivityDeliveryStatus.AcknowledgementLost,
            (await querying).Status);
        Assert.False(session.SupportsSwap);
        Assert.Equal(
            ActivityDeliveryStatus.NotDelivered,
            (await session.QueryActivityAsync(
                LocalId,
                CreateSnapshotQuery(
                    OperationId.From(Guid.NewGuid()),
                    CorrelationId.From(Guid.NewGuid())),
                CancellationToken.None)).Status);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task AuthenticatedResponseWinsWhenOldSendThenFailsAndClosesSession()
    {
        var connection = new ResponseBeforeSendCompletionConnection(LocalId, PeerId);
        var session = CreateSession(connection);
        using var stop = new CancellationTokenSource();
        Task run = session.RunAsync(stop.Token).AsTask();
        CorrelationId correlationId = CorrelationId.From(Guid.NewGuid());
        SwapActivitySnapshotQuery firstQuery = CreateSnapshotQuery(
            OperationId.From(Guid.NewGuid()),
            correlationId);
        Task<SwapDeliveryResult<SwapActivitySnapshotResult>> first = session
            .QueryActivityAsync(LocalId, firstQuery, CancellationToken.None)
            .AsTask();
        _ = await connection.ReadSentAsync();
        connection.Receive(SwapControlMessageCodec.CreateSnapshotResult(
            Version,
            PeerId,
            SwapActivitySnapshotResult.Success(
                LocalId,
                firstQuery,
                CreateActivity(PeerId, "Remote")),
            Now));
        await connection.ReadyForNextIncoming.Task.WaitAsync(TimeSpan.FromSeconds(1));

        SwapActivitySnapshotQuery secondQuery = CreateSnapshotQuery(
            OperationId.From(Guid.NewGuid()),
            correlationId);
        Task<SwapDeliveryResult<SwapActivitySnapshotResult>> second = session
            .QueryActivityAsync(LocalId, secondQuery, CancellationToken.None)
            .AsTask();
        _ = await connection.ReadSentAsync();
        connection.FailFirstSend.TrySetResult();
        Assert.Equal(ActivityDeliveryStatus.Acknowledged, (await first).Status);
        Assert.Equal(ActivityDeliveryStatus.AcknowledgementLost, (await second).Status);

        Assert.Equal(
            ActivityDeliveryStatus.NotDelivered,
            (await session.PrepareAsync(
                LocalId,
                CreatePrepareCommand(OperationId.From(Guid.NewGuid()), correlationId),
                CancellationToken.None)).Status);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task SendIoFailureWithoutResponseIsAcknowledgementLost()
    {
        var connection = new ResponseBeforeSendCompletionConnection(LocalId, PeerId);
        var session = CreateSession(connection);
        Task run = session.RunAsync().AsTask();
        Task<SwapDeliveryResult<SwapActivitySnapshotResult>> querying = session
            .QueryActivityAsync(
                LocalId,
                CreateSnapshotQuery(
                    OperationId.From(Guid.NewGuid()),
                    CorrelationId.From(Guid.NewGuid())),
                CancellationToken.None)
            .AsTask();
        _ = await connection.ReadSentAsync();

        connection.FailFirstSend.TrySetResult();

        Assert.Equal(ActivityDeliveryStatus.AcknowledgementLost, (await querying).Status);
        Assert.False(session.SupportsSwap);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task ConcurrentCancelAndDisposeRemainIdempotent()
    {
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var connection = new FakeActivityControlConnection(LocalId, PeerId);
            var session = CreateSession(connection);
            Task run = session.RunAsync().AsTask();

            await Task.WhenAll(
                Task.Run(session.Cancel),
                Task.Run(async () => await session.DisposeAsync()));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }
    }

    [Fact]
    public async Task ProtocolOnePointZeroKeepsActivityButDoesNotExposeSwapChannel()
    {
        var version = new ProtocolVersion(1, 0);
        using var sourceIdentity = DeviceIdentity.Generate(LocalId, "Source");
        using var targetIdentity = DeviceIdentity.Generate(PeerId, "Target");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var listenerEndpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.ActivitySwap)),
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                listenerEndpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.ActivitySwap)),
                [version]);
        await using AuthenticatedTcpControlConnection targetConnection = await accepting;
        var localEndpoint = new AuthorizedSwapEndpoint(
            new FixedClock(Now),
            new InMemorySwapEndpoint(LocalId, new InMemoryActivityCatalog()));
        await using var sourceHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(LocalId),
            replacePeer: null,
            replaceInventoryPeer: null,
            localEndpoint,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();

        Assert.True(sourceHandler.IsSwapEndpointAvailable);
        Assert.True(sourceHandler.TryGetChannel(PeerId, out IActivityChannel? activity));
        Assert.NotNull(activity);
        Assert.False(sourceHandler.TryGetSwapChannel(
            PeerId,
            out ISwapEndpointChannel? swap));
        Assert.Null(swap);

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
    }

    [Fact]
    public async Task RealAuthenticatedLoopbackCommitsDurableRemoteSwap()
    {
        using var sourceIdentity = DeviceIdentity.Generate(LocalId, "Source");
        using var targetIdentity = DeviceIdentity.Generate(PeerId, "Target");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var listenerEndpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.ActivitySwap)),
                [Version]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                listenerEndpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.Of(Capability.ActivitySwap)),
                [Version]);
        await using AuthenticatedTcpControlConnection targetConnection = await accepting;

        var sourceCatalog = new InMemoryActivityCatalog();
        var targetCatalog = new InMemoryActivityCatalog();
        ActivityInstance sourceActivity = CreateActivity(LocalId, "Source");
        ActivityInstance targetActivity = CreateActivity(PeerId, "Target");
        Assert.True(sourceCatalog.TryAdd(sourceActivity));
        Assert.True(targetCatalog.TryAdd(targetActivity));
        var sourceEndpoint = new InMemorySwapEndpoint(LocalId, sourceCatalog);
        var endpointStore = new MemorySwapPayloadStore();
        using PersistentSwapEndpointJournal targetJournal =
            await PersistentSwapEndpointJournal.OpenAsync(PeerId, endpointStore);
        using var targetEndpoint = new PersistentSwapEndpoint(
            PeerId,
            targetCatalog,
            targetJournal);
        var sourcePeer = new AuthorizedSwapEndpoint(
            new FixedClock(Now),
            sourceEndpoint);
        var targetPeer = new AuthorizedSwapEndpoint(
            new FixedClock(Now),
            targetEndpoint);
        sourcePeer.SetPeerGrant(
            PeerId,
            CapabilityGrant.Of(Capability.ActivitySwap));
        targetPeer.SetPeerGrant(
            LocalId,
            CapabilityGrant.Of(Capability.ActivitySwap));
        await using var sourceHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(LocalId),
            replacePeer: null,
            replaceInventoryPeer: null,
            sourcePeer,
            new FixedTimeProvider(Now));
        await using var targetHandler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(PeerId),
            replacePeer: null,
            replaceInventoryPeer: null,
            targetPeer,
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task sourceRun = sourceHandler.RunAsync(sourceConnection, stop.Token).AsTask();
        Task targetRun = targetHandler.RunAsync(targetConnection, stop.Token).AsTask();
        Assert.True(sourceHandler.TryGetSwapChannel(
            PeerId,
            out ISwapEndpointChannel? remoteChannel));
        Assert.NotNull(remoteChannel);
        var transactionStore = new MemorySwapPayloadStore();
        using PersistentSwapTransactionJournal transactionJournal =
            await PersistentSwapTransactionJournal.OpenAsync(transactionStore);
        var coordinator = new SwapCoordinator(
            LocalId,
            new FixedClock(Now),
            transactionJournal,
            new DeterministicSwapTokenSource([LocalToken, PeerToken]));
        OperationContext context = OperationContext.Create(
            OperationId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Now.AddSeconds(30));

        SwapCoordinatorResult result = await coordinator.ExecuteAsync(
            context,
            new DirectSwapEndpointChannel(sourceEndpoint),
            sourceActivity.Descriptor.Id,
            remoteChannel,
            targetActivity.Descriptor.Id);

        Assert.Equal(OperationStatus.Committed, result.Status);
        Assert.True(transactionJournal.TryGet(
            context.OperationId,
            out SwapCoordinatorTransaction? transaction));
        Assert.Equal(SwapDecisionOutcome.Commit, transaction.Decision?.Outcome);
        Assert.True(targetJournal.TryGet(
            context.OperationId,
            out SwapEndpointRecord? remoteRecord));
        Assert.Equal(SwapReservationPhase.Committed, remoteRecord.Reservation?.Phase);
        Assert.True(sourceCatalog.TryGet(
            targetActivity.Descriptor.Id,
            out ActivityInstance? onSource));
        Assert.Equal(LocalId, onSource.Placement.DeviceId);
        Assert.True(targetCatalog.TryGet(
            sourceActivity.Descriptor.Id,
            out ActivityInstance? onTarget));
        Assert.Equal(PeerId, onTarget.Placement.DeviceId);

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sourceRun);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => targetRun);
    }

    private static ActivityControlSession CreateSession(
        IActivityControlConnection connection,
        TimeProvider? timeProvider = null) => new(
        connection,
        new RejectingActivityPeer(LocalId),
        replacePeer: null,
        replaceInventoryPeer: null,
        swapPeer: null,
        timeProvider ?? new FixedTimeProvider(Now));

    private static SwapActivitySnapshotQuery CreateSnapshotQuery(
        OperationId operationId,
        CorrelationId correlationId) => SwapActivitySnapshotQuery.Create(
        OperationContext.Create(
            operationId,
            correlationId,
            Now.AddSeconds(30)),
        PeerId,
        CreateActivity(PeerId, "Remote").Descriptor.Id);

    private static SwapPrepareCommand CreatePrepareCommand(
        OperationId operationId,
        CorrelationId correlationId) => new(
        operationId,
        correlationId,
        PeerToken,
        CreateActivity(PeerId, "Remote"),
        CreateActivity(LocalId, "Local"),
        Now.AddSeconds(30));

    private static SwapDecision CreateDecision(OperationId operationId) =>
        SwapDecision.Create(
            operationId,
            SwapDecisionOutcome.Commit,
            Now,
            [
                SwapDecisionParticipant.Create(LocalId, LocalToken),
                SwapDecisionParticipant.Create(PeerId, PeerToken),
            ]);

    private static ActivityInstance CreateActivity(
        DeviceId deviceId,
        string title)
    {
        string activityId = deviceId == LocalId
            ? "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            : "dddddddd-dddd-dddd-dddd-dddddddddddd";
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse(activityId),
            ActivityKind.Parse("workspace.note/v1"),
            deviceId,
            title,
            JsonSerializer.Serialize(new { text = title }));
        return ActivityInstance.Active(
            descriptor,
            ActivityPlacement.On(deviceId, "desktop"),
            revision: 7);
    }

    private static ActivityTransferOffer CreateOffer(CorrelationId correlationId) =>
        ActivityTransferOffer.Create(
            OperationKind.Handoff,
            OperationContext.Create(
                OperationId.From(Guid.NewGuid()),
                correlationId,
                Now.AddSeconds(30)),
            CreateActivity(LocalId, "Local").Descriptor,
            ActivityPlacement.On(PeerId, "desktop"));

    private static ReplaceActivityCommand CreateReplaceCommand(
        CorrelationId correlationId)
    {
        ActivityInstance target = CreateActivity(PeerId, "Remote");
        ActivityInstance incoming = CreateActivity(LocalId, "Local");
        return ReplaceActivityCommand.Create(
            OperationContext.Create(
                OperationId.From(Guid.NewGuid()),
                correlationId,
                Now.AddSeconds(30)),
            target.Descriptor.Id,
            target.Revision,
            target.Descriptor.DescriptorDigest,
            incoming.Descriptor,
            ActivityPlacement.On(PeerId, "desktop"),
            Now.AddMinutes(1));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = utcNow;

        public TaskCompletionSource TimerCreated { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Advance(TimeSpan elapsed)
        {
            List<ManualTimer> candidates;
            DateTimeOffset now;
            lock (gate)
            {
                utcNow = utcNow.Add(elapsed);
                now = utcNow;
                candidates = timers.ToList();
            }

            foreach (ManualTimer timer in candidates.Where(timer => timer.IsDue(now)))
            {
                timer.Fire(now);
            }
        }

        public void AdvanceWithoutFiring(TimeSpan elapsed)
        {
            lock (gate)
            {
                utcNow = utcNow.Add(elapsed);
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (gate)
            {
                timers.Add(timer);
            }

            TimerCreated.TrySetResult();
            return timer;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private DateTimeOffset dueAt = DateTimeOffset.MaxValue;
            private bool disposed;
            private TimeSpan period = Timeout.InfiniteTimeSpan;

            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                lock (owner.gate)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    dueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : owner.utcNow.Add(dueTime);
                    period = newPeriod;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    disposed = true;
                    owner.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    if (disposed || dueAt > now)
                    {
                        return;
                    }

                    dueAt = period == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : now.Add(period);
                }

                callback(state);
            }

            public bool IsDue(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    return !disposed && dueAt <= now;
                }
            }
        }
    }

    private sealed class RejectingActivityPeer(DeviceId deviceId) : IActivityPeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<OperationReceipt>(
                new InvalidOperationException("No inbound transfer was expected."));
    }

    private sealed class MemorySwapPayloadStore :
        ISwapStatePayloadStore,
        ISwapEndpointStatePayloadStore
    {
        private byte[]? payload;

        public ValueTask<byte[]?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(payload?.ToArray());
        }

        public ValueTask SaveAsync(
            ReadOnlyMemory<byte> value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            payload = value.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeActivityControlConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IActivityControlConnection
    {
        private readonly Channel<ControlMessage> incoming =
            Channel.CreateUnbounded<ControlMessage>();
        private readonly Channel<ControlMessage> outgoing =
            Channel.CreateUnbounded<ControlMessage>();

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } = Version;

        public void Receive(ControlMessage message) =>
            incoming.Writer.TryWrite(message);

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            incoming.Reader.ReadAsync(cancellationToken);

        public ValueTask<ControlMessage> ReadSentAsync() => outgoing.Reader.ReadAsync();

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outgoing.Writer.TryWrite(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingSendActivityControlConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IActivityControlConnection
    {
        private readonly Channel<ControlMessage> incoming =
            Channel.CreateUnbounded<ControlMessage>();

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } = Version;

        public TaskCompletionSource SendCancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            incoming.Reader.ReadAsync(cancellationToken);

        public async ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            SendStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SendCancellationObserved.TrySetResult();
                throw;
            }
        }
    }

    private sealed class NonCooperativeSendActivityControlConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IActivityControlConnection
    {
        private readonly Channel<ControlMessage> incoming =
            Channel.CreateUnbounded<ControlMessage>();
        private readonly TaskCompletionSource never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } = Version;

        public TaskCompletionSource SendStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default) =>
            incoming.Reader.ReadAsync(cancellationToken);

        public ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            SendStarted.TrySetResult();
            return new ValueTask(never.Task);
        }
    }

    private sealed class ResponseBeforeSendCompletionConnection(
        DeviceId localDeviceId,
        DeviceId peerDeviceId) : IActivityControlConnection
    {
        private readonly Channel<ControlMessage> incoming =
            Channel.CreateUnbounded<ControlMessage>();
        private readonly Channel<ControlMessage> outgoing =
            Channel.CreateUnbounded<ControlMessage>();
        private int readCount;
        private int sendCount;

        public DeviceId LocalDeviceId { get; } = localDeviceId;

        public DeviceId PeerDeviceId { get; } = peerDeviceId;

        public ProtocolVersion ProtocolVersion { get; } = Version;

        public TaskCompletionSource FailFirstSend { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadyForNextIncoming { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Receive(ControlMessage message) =>
            incoming.Writer.TryWrite(message);

        public async ValueTask<ControlMessage> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref readCount) >= 2)
            {
                ReadyForNextIncoming.TrySetResult();
            }

            return await incoming.Reader.ReadAsync(cancellationToken);
        }

        public ValueTask<ControlMessage> ReadSentAsync() =>
            outgoing.Reader.ReadAsync();

        public async ValueTask SendAsync(
            ControlMessage message,
            CancellationToken cancellationToken = default)
        {
            int current = Interlocked.Increment(ref sendCount);
            outgoing.Writer.TryWrite(message);
            if (current == 1)
            {
                await FailFirstSend.Task.WaitAsync(cancellationToken);
                throw new IOException("Injected first-send failure after response.");
            }
        }
    }
}

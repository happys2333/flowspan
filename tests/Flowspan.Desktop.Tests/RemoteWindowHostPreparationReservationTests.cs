using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class RemoteWindowHostPreparationReservationTests
{
    private const long HostGeneration = 7;

    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        30,
        12,
        0,
        0,
        TimeSpan.Zero);

    private static readonly DeviceId HostDeviceId = DeviceId.Parse(
        "11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId ParticipantDeviceId = DeviceId.Parse(
        "22222222-2222-2222-2222-222222222222");

    private static readonly ActivityId ActivityId = ActivityId.Parse(
        "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task SourceInvalidationBeforeRouteAdmissionPreventsRoute()
    {
        RemoteWindowPreparationRequest request = CreateRequest();
        RemoteWindowHostPreparationEpochBundle epochs =
            RemoteWindowHostPreparationEpochBundle.Create();
        using var reservation = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            epochs);
        Arm(reservation);

        Assert.True(reservation.TryInvalidate(
            HostGeneration,
            RemoteWindowHostPreparationFact.Source,
            epochs.Get(RemoteWindowHostPreparationFact.Source)));

        Assert.False(reservation.TryAdmitRouteSelection(Now));
        RemoteWindowHostPreparationTermination terminal =
            await reservation.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("native_source_stale", terminal.ReasonCode);
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.PreRoute,
            terminal.CleanupScope);
        Assert.False(reservation.Snapshot.RouteMayBeOwned);
        Assert.False(reservation.Snapshot.PrepareSendAdmitted);
    }

    [Fact]
    public async Task InvalidationDuringAdmittedRoutePreventsPrepareSend()
    {
        RemoteWindowPreparationRequest request = CreateRequest();
        RemoteWindowHostPreparationEpochBundle epochs =
            RemoteWindowHostPreparationEpochBundle.Create();
        using var reservation = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            epochs);
        Arm(reservation);
        var routeAdmitted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRoute = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> route = StartDedicated(() =>
        {
            Assert.True(reservation.TryAdmitRouteSelection(Now));
            routeAdmitted.TrySetResult();
            releaseRoute.Task.GetAwaiter().GetResult();
            return reservation.CompleteRouteSelection();
        });
        await routeAdmitted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.True(reservation.TryInvalidate(
                HostGeneration,
                RemoteWindowHostPreparationFact.Source,
                epochs.Get(RemoteWindowHostPreparationFact.Source)));
        }
        finally
        {
            releaseRoute.TrySetResult();
        }

        Assert.False(await route.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(reservation.TryAdmitPrepareSend(request, Now));
        RemoteWindowHostPreparationTermination terminal =
            await reservation.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
            terminal.CleanupScope);
        Assert.True(reservation.Snapshot.RouteMayBeOwned);
        Assert.False(reservation.Snapshot.PrepareSendAdmitted);
    }

    [Fact]
    public async Task PrepareSendAdmissionBeforeInvalidationIsIrreversible()
    {
        RemoteWindowPreparationRequest request = CreateRequest();
        RemoteWindowHostPreparationEpochBundle epochs =
            RemoteWindowHostPreparationEpochBundle.Create();
        using var reservation = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            epochs);
        Arm(reservation);
        Assert.True(reservation.TryAdmitRouteSelection(Now));
        Assert.True(reservation.CompleteRouteSelection());
        var sendAdmitted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSender = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> sending = StartDedicated(() =>
        {
            bool admitted = reservation.TryAdmitPrepareSend(request, Now);
            sendAdmitted.TrySetResult();
            releaseSender.Task.GetAwaiter().GetResult();
            return admitted;
        });
        await sendAdmitted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal(
                RemoteWindowHostPreparationPhase.PrepareSending,
                reservation.Snapshot.Phase);
            Assert.True(reservation.TryInvalidate(
                HostGeneration,
                RemoteWindowHostPreparationFact.Permission,
                epochs.Get(RemoteWindowHostPreparationFact.Permission)));
        }
        finally
        {
            releaseSender.TrySetResult();
        }

        Assert.True(await sending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(
            RemoteWindowHostPreparationPhase.Terminal,
            reservation.Snapshot.Phase);
        Assert.True(reservation.Snapshot.RouteMayBeOwned);
        Assert.True(reservation.Snapshot.PrepareSendAdmitted);
        Assert.False(reservation.TryAdmitPrepareSend(request, Now));
        RemoteWindowHostPreparationTermination terminal =
            await reservation.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("native_permission_denied", terminal.ReasonCode);
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
            terminal.CleanupScope);
    }

    [Fact]
    public async Task RouteSideEffectThenThrowRequiresConnectionConsumption()
    {
        RemoteWindowPreparationRequest request = CreateRequest();
        RemoteWindowHostPreparationEpochBundle epochs =
            RemoteWindowHostPreparationEpochBundle.Create();
        using var reservation = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            epochs);
        Arm(reservation);
        var injected = new IOException("injected route failure");
        var sideEffectOccurred = false;
        Assert.True(reservation.TryAdmitRouteSelection(Now));

        Exception? observed = Record.Exception((Action)(() =>
        {
            sideEffectOccurred = true;
            throw injected;
        }));
        Assert.Same(injected, observed);
        Assert.True(sideEffectOccurred);
        Assert.True(reservation.TryFailRouteSelection());

        RemoteWindowHostPreparationTermination terminal =
            await reservation.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("responder_route_failed", terminal.ReasonCode);
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
            terminal.CleanupScope);
        Assert.True(reservation.Snapshot.RouteMayBeOwned);
        Assert.False(reservation.CompleteRouteSelection());
        Assert.False(reservation.TryAdmitPrepareSend(request, Now));
        Assert.False(reservation.TryFailRouteSelection());
    }

    [Fact]
    public async Task DeadlineEqualityTerminatesBeforePrepareSendAdmission()
    {
        RemoteWindowPreparationRequest request = CreateRequest();
        using var reservation = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            RemoteWindowHostPreparationEpochBundle.Create());
        Arm(reservation);
        Assert.True(reservation.TryAdmitRouteSelection(Now));
        Assert.True(reservation.CompleteRouteSelection());

        Assert.False(reservation.TryAdmitPrepareSend(
            request,
            request.Deadline));

        Assert.True(reservation.Terminal.IsCompleted);
        RemoteWindowHostPreparationTermination terminal =
            await reservation.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("preparation_expired", terminal.ReasonCode);
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
            terminal.CleanupScope);
        Assert.Equal(
            RemoteWindowHostPreparationPhase.Terminal,
            reservation.Snapshot.Phase);
        Assert.False(reservation.Snapshot.PrepareSendAdmitted);
    }

    [Fact]
    public async Task DeadlineEqualityCannotAdmitRouteReadyOrPromotion()
    {
        RemoteWindowPreparationRequest request = CreateRequest();
        using var beforeArm = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            RemoteWindowHostPreparationEpochBundle.Create());

        Assert.False(beforeArm.TryArm(request.Deadline));

        RemoteWindowHostPreparationTermination armTerminal =
            await beforeArm.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("preparation_expired", armTerminal.ReasonCode);
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.PreRoute,
            armTerminal.CleanupScope);
        Assert.False(beforeArm.Snapshot.RouteMayBeOwned);

        using var beforeRoute = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            RemoteWindowHostPreparationEpochBundle.Create());
        Arm(beforeRoute);

        Assert.False(beforeRoute.TryAdmitRouteSelection(request.Deadline));

        RemoteWindowHostPreparationTermination routeTerminal =
            await beforeRoute.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("preparation_expired", routeTerminal.ReasonCode);
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.PreRoute,
            routeTerminal.CleanupScope);
        Assert.False(beforeRoute.Snapshot.RouteMayBeOwned);

        using var beforeReady = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            RemoteWindowHostPreparationEpochBundle.Create());
        Arm(beforeReady);
        Assert.True(beforeReady.TryAdmitRouteSelection(Now));
        Assert.True(beforeReady.CompleteRouteSelection());
        Assert.True(beforeReady.TryAdmitPrepareSend(request, Now));

        Assert.False(beforeReady.TryMatchReady(
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready"),
            request.Deadline));

        RemoteWindowHostPreparationTermination readyTerminal =
            await beforeReady.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("preparation_expired", readyTerminal.ReasonCode);
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
            readyTerminal.CleanupScope);
        Assert.True(beforeReady.Snapshot.PrepareSendAdmitted);

        using var beforePromotion = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            RemoteWindowHostPreparationEpochBundle.Create());
        Arm(beforePromotion);
        Assert.True(beforePromotion.TryAdmitRouteSelection(Now));
        Assert.True(beforePromotion.CompleteRouteSelection());
        Assert.True(beforePromotion.TryAdmitPrepareSend(request, Now));
        Assert.True(beforePromotion.TryMatchReady(
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready"),
            Now));

        Assert.False(beforePromotion.TryPromote(request.Deadline));

        RemoteWindowHostPreparationTermination promotionTerminal =
            await beforePromotion.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("preparation_expired", promotionTerminal.ReasonCode);
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
            promotionTerminal.CleanupScope);
        Assert.True(beforePromotion.Snapshot.PrepareSendAdmitted);
    }

    [Fact]
    public async Task StaleEpochAndHostGenerationCannotInvalidateReplacement()
    {
        RemoteWindowHostPreparationEpochBundle staleEpochs =
            RemoteWindowHostPreparationEpochBundle.Create();
        RemoteWindowHostPreparationEpochBundle replacementEpochs =
            RemoteWindowHostPreparationEpochBundle.Create();
        const long replacementGeneration = HostGeneration + 1;
        using var replacement = new RemoteWindowHostPreparationReservation(
            replacementGeneration,
            CreateRequest(),
            replacementEpochs);
        ArgumentException reused = Assert.Throws<ArgumentException>(() =>
            new RemoteWindowHostPreparationReservation(
                replacementGeneration + 1,
                CreateRequest("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                replacementEpochs));
        Assert.Equal("epochs", reused.ParamName);
        Arm(replacement);

        Assert.Equal(
            replacementGeneration,
            replacement.Snapshot.HostGeneration);
        Assert.False(replacement.TryInvalidate(
            HostGeneration,
            RemoteWindowHostPreparationFact.Source,
            replacementEpochs.Get(RemoteWindowHostPreparationFact.Source)));
        Assert.False(replacement.TryInvalidate(
            replacementGeneration,
            RemoteWindowHostPreparationFact.Source,
            staleEpochs.Get(RemoteWindowHostPreparationFact.Source)));
        Assert.False(replacement.Terminal.IsCompleted);
        Assert.Equal(
            RemoteWindowHostPreparationPhase.Armed,
            replacement.Snapshot.Phase);
        Assert.True(replacement.TryAdmitRouteSelection(Now));

        Assert.True(replacement.TryInvalidate(
            replacementGeneration,
            RemoteWindowHostPreparationFact.Source,
            replacementEpochs.Get(RemoteWindowHostPreparationFact.Source)));
        RemoteWindowHostPreparationTermination terminal =
            await replacement.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
            terminal.CleanupScope);
    }

    [Fact]
    public async Task ConcurrentInvalidationsPublishOneTerminalSignal()
    {
        RemoteWindowHostPreparationEpochBundle epochs =
            RemoteWindowHostPreparationEpochBundle.Create();
        using var reservation = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            CreateRequest(),
            epochs);
        Arm(reservation);
        RemoteWindowHostPreparationFact[] facts =
            Enum.GetValues<RemoteWindowHostPreparationFact>();
        using var start = new Barrier(facts.Length + 1);
        Task<(RemoteWindowHostPreparationFact Fact, bool Won)>[] attempts = facts
            .Select(fact => StartDedicated(() =>
            {
                Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(5)));
                bool won = reservation.TryInvalidate(
                    HostGeneration,
                    fact,
                    epochs.Get(fact));
                return (fact, won);
            }))
            .ToArray();

        Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(5)));
        (RemoteWindowHostPreparationFact Fact, bool Won)[] results =
            await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(5));

        (RemoteWindowHostPreparationFact Fact, bool Won) winner = Assert.Single(
            results,
            static result => result.Won);
        Task<RemoteWindowHostPreparationTermination> terminalTask =
            reservation.Terminal;
        Assert.Same(terminalTask, reservation.Terminal);
        RemoteWindowHostPreparationTermination terminal =
            await terminalTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(winner.Fact, terminal.Fact);
        Assert.Equal(
            ExpectedInvalidationReason(winner.Fact),
            terminal.ReasonCode);
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.PreRoute,
            terminal.CleanupScope);
        Assert.All(
            results.Where(static result => !result.Won),
            static result => Assert.False(result.Won));
    }

    [Fact]
    public async Task ReadyAndPromotionRequireExactPhaseAndBinding()
    {
        RemoteWindowPreparationRequest request = CreateRequest();
        RemoteWindowHostPreparationEpochBundle epochs =
            RemoteWindowHostPreparationEpochBundle.Create();
        var reservation = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            epochs);
        RemoteWindowPreparationResponse ready =
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready");

        Assert.False(reservation.TryMatchReady(ready, Now));
        Assert.False(reservation.TryPromote(Now));
        Assert.Equal(
            RemoteWindowHostPreparationPhase.Collecting,
            reservation.Snapshot.Phase);
        Assert.False(reservation.TryAdmitRouteSelection(Now));
        Arm(reservation);
        Assert.True(reservation.TryAdmitRouteSelection(Now));
        Assert.True(reservation.CompleteRouteSelection());
        Assert.True(reservation.TryAdmitPrepareSend(request, Now));
        Assert.False(reservation.TryPromote(Now));

        Assert.True(reservation.TryMatchReady(ready, Now));
        Assert.Equal(
            RemoteWindowHostPreparationPhase.ReadyMatched,
            reservation.Snapshot.Phase);
        Assert.True(reservation.TryPromote(Now));
        Assert.Equal(
            RemoteWindowHostPreparationPhase.Promoted,
            reservation.Snapshot.Phase);
        Assert.False(reservation.TryPromote(Now));
        Assert.False(reservation.TryInvalidate(
            HostGeneration,
            RemoteWindowHostPreparationFact.Source,
            epochs.Get(RemoteWindowHostPreparationFact.Source)));

        reservation.Dispose();
        Assert.Equal(
            RemoteWindowHostPreparationPhase.Promoted,
            reservation.Snapshot.Phase);
        Assert.False(reservation.Terminal.IsCompleted);

        using var foreignReservation = new RemoteWindowHostPreparationReservation(
            HostGeneration,
            request,
            RemoteWindowHostPreparationEpochBundle.Create());
        Arm(foreignReservation);
        Assert.True(foreignReservation.TryAdmitRouteSelection(Now));
        Assert.True(foreignReservation.CompleteRouteSelection());
        Assert.True(foreignReservation.TryAdmitPrepareSend(request, Now));
        RemoteWindowPreparationResponse foreignReady =
            RemoteWindowPreparationResponse.Create(
                CreateRequest("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready");

        Assert.False(foreignReservation.TryMatchReady(foreignReady, Now));

        Assert.True(foreignReservation.Terminal.IsCompleted);
        RemoteWindowHostPreparationTermination terminal =
            await foreignReservation.Terminal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("remote_window_ready_mismatch", terminal.ReasonCode);
        Assert.Equal(
            RemoteWindowHostPreparationCleanupScope.ConsumeConnection,
            terminal.CleanupScope);
        Assert.False(foreignReservation.TryPromote(Now));
        Assert.Equal(
            RemoteWindowHostPreparationPhase.Terminal,
            foreignReservation.Snapshot.Phase);
    }

    private static RemoteWindowPreparationRequest CreateRequest(
        string correlationId = "cccccccc-cccc-cccc-cccc-cccccccccccc") =>
        RemoteWindowPreparationRequest.Create(
            CorrelationId.Parse(correlationId),
            SessionId,
            ActivityId,
            HostDeviceId,
            ParticipantDeviceId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));

    private static void Arm(
        RemoteWindowHostPreparationReservation reservation) =>
        Assert.True(reservation.TryArm(Now));

    private static string ExpectedInvalidationReason(
        RemoteWindowHostPreparationFact fact) => fact switch
        {
            RemoteWindowHostPreparationFact.Source => "native_source_stale",
            RemoteWindowHostPreparationFact.Permission =>
                "native_permission_denied",
            RemoteWindowHostPreparationFact.Authorization =>
                "mirror_capability_denied",
            RemoteWindowHostPreparationFact.Connection =>
                "authenticated_connection_stale",
            RemoteWindowHostPreparationFact.EmergencyStop =>
                "emergency_stop_readiness_unavailable",
            RemoteWindowHostPreparationFact.Protection =>
                "native_protection_not_safe",
            _ => throw new ArgumentOutOfRangeException(nameof(fact)),
        };

    private static Task<T> StartDedicated<T>(Func<T> operation) =>
        Task.Factory.StartNew(
            operation,
            CancellationToken.None,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
}

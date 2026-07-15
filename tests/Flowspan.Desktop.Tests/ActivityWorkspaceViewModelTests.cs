using System.Collections.Immutable;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;

namespace Flowspan.Desktop.Tests;

public sealed class ActivityWorkspaceViewModelTests
{
    private static readonly DeviceId LocalId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId TargetId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void NoteAndAuthenticatedTargetProduceExplicitSourcePreservingPreview()
    {
        var service = new FakeActivityService();
        using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance);
        viewModel.DraftTitle = "Release plan";
        viewModel.DraftText = "portable note body";

        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);

        Assert.Single(viewModel.Activities);
        Assert.Equal("Release plan", viewModel.SelectedActivity?.Title);
        Assert.True(viewModel.IsPreviewVisible);
        Assert.Equal("SEMANTIC HANDOFF — SOURCE STAYS OPEN", viewModel.PreviewStatus);
        Assert.Contains("workspace.note/v1", viewModel.PreviewDescription);
        Assert.Contains("Peer desk", viewModel.PreviewDescription);
        Assert.Contains("plain-text note", viewModel.DataDisclosure);
        Assert.Equal(
            "REMOTE WINDOW NOT AVAILABLE IN THIS BUILD",
            viewModel.DegradationStatus);
        Assert.Contains("process memory", viewModel.DegradationDescription);
        Assert.True(viewModel.HandoffCommand.CanExecute(null));
    }

    [Fact]
    public void NoteAndAuthenticatedTargetProduceAcknowledgementOrderedMovePreview()
    {
        var service = new FakeActivityService();
        using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Release plan",
            DraftText = "portable note body",
        };
        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);

        Assert.True(viewModel.IsMovePreviewVisible);
        Assert.Equal(
            "SEMANTIC MOVE — SOURCE CLOSES AFTER TARGET ACKNOWLEDGEMENT",
            viewModel.MovePreviewStatus);
        Assert.Contains("resumes", viewModel.MovePreviewDescription);
        Assert.Contains("first", viewModel.MovePreviewDescription);
        Assert.Contains("only after", viewModel.MovePreviewDescription);
        Assert.Contains("remains active", viewModel.MovePreviewDescription);
        Assert.True(viewModel.MoveCommand.CanExecute(null));
    }

    [Fact]
    public async Task CommittedHandoffShowsRedactedReceiptAndNoMisleadingUndo()
    {
        const string canary = "FLOWSPAN_NOTE_SECRET_CANARY";
        var service = new FakeActivityService();
        using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Incident note",
            DraftText = canary,
        };
        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);

        await viewModel.HandoffAsync();

        Assert.Equal("HANDOFF COMMITTED", viewModel.ReceiptStatus);
        Assert.Contains("Peer desk", viewModel.ReceiptSummary);
        Assert.Contains("source remains available", viewModel.ReceiptSummary);
        Assert.NotEmpty(viewModel.ReceiptCorrelationId);
        Assert.Equal("none", viewModel.ReceiptReason);
        Assert.Contains("NO UNDO", viewModel.UndoDescription);
        Assert.DoesNotContain(canary, viewModel.ReceiptStatus, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, viewModel.ReceiptSummary, StringComparison.Ordinal);
        Assert.DoesNotContain(canary, viewModel.ReceiptReason, StringComparison.Ordinal);
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task CommittedMoveClosesSourceOnlyAfterVerifiedTargetReceipt()
    {
        var service = new FakeActivityService();
        using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Incident note",
            DraftText = "portable body",
        };
        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);

        await viewModel.MoveAsync();

        Assert.Equal("MOVE COMMITTED", viewModel.ReceiptStatus);
        Assert.Contains("acknowledged", viewModel.ReceiptSummary);
        Assert.Contains("source closed", viewModel.ReceiptSummary);
        Assert.Contains("NO AUTOMATIC UNDO", viewModel.UndoDescription);
        Assert.Contains("move it back", viewModel.UndoDescription);
        Assert.DoesNotContain(
            "handoff",
            viewModel.UndoDescription,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(service.SourceStillActive);
        Assert.Empty(viewModel.Activities);
        Assert.Null(viewModel.SelectedActivity);
    }

    [Fact]
    public async Task MoveSourceCleanupFailureNamesCommittedDuplicateWarning()
    {
        var service = new FakeActivityService
        {
            Outcome = OperationStatus.CommittedWithWarning,
            Failure = FailureCode.SourceCleanupFailed,
        };
        using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Incident note",
            DraftText = "portable body",
        };
        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);

        await viewModel.MoveAsync();

        Assert.Equal("MOVE COMMITTED WITH WARNING", viewModel.ReceiptStatus);
        Assert.Contains("target committed", viewModel.ReceiptSummary);
        Assert.Contains("source cleanup failed", viewModel.ReceiptSummary);
        Assert.Contains("two active copies", viewModel.ReceiptSummary);
        Assert.Equal("source-cleanup-failed", viewModel.ReceiptReason);
        Assert.True(service.SourceStillActive);
        Assert.Single(viewModel.Activities);
    }

    [Fact]
    public async Task MoveAcknowledgementLossKeepsSourceAndNamesUncertainOutcome()
    {
        var service = new FakeActivityService
        {
            Outcome = OperationStatus.Recovering,
            Failure = FailureCode.AcknowledgementLost,
        };
        using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Incident note",
            DraftText = "portable body",
        };
        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);

        await viewModel.MoveAsync();

        Assert.Equal("MOVE OUTCOME UNCERTAIN", viewModel.ReceiptStatus);
        Assert.Contains("may have accepted", viewModel.ReceiptSummary);
        Assert.Contains("semantic resume", viewModel.ReceiptSummary);
        Assert.DoesNotContain("semantic copy", viewModel.ReceiptSummary);
        Assert.Contains("source remains available", viewModel.ReceiptSummary);
        Assert.Equal("acknowledgement-lost", viewModel.ReceiptReason);
        Assert.True(service.SourceStillActive);
        Assert.Single(viewModel.Activities);
    }

    [Fact]
    public async Task MoveRejectionNamesFailedResumeAndKeepsSource()
    {
        var service = new FakeActivityService
        {
            Outcome = OperationStatus.Rejected,
            Failure = FailureCode.CapabilityDenied,
        };
        using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Incident note",
            DraftText = "portable body",
        };
        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);

        await viewModel.MoveAsync();

        Assert.Equal("MOVE REJECTED", viewModel.ReceiptStatus);
        Assert.Contains("did not accept the semantic resume", viewModel.ReceiptSummary);
        Assert.DoesNotContain("semantic copy", viewModel.ReceiptSummary);
        Assert.Contains("source remains available", viewModel.ReceiptSummary);
        Assert.Equal("capability-denied", viewModel.ReceiptReason);
        Assert.True(service.SourceStillActive);
        Assert.Single(viewModel.Activities);
    }

    [Fact]
    public async Task CapabilityDenialIsNamedWithoutRemovingSource()
    {
        var service = new FakeActivityService
        {
            Outcome = OperationStatus.Rejected,
            Failure = FailureCode.CapabilityDenied,
        };
        using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Plan",
            DraftText = "body",
        };
        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);

        await viewModel.HandoffAsync();

        Assert.Equal("HANDOFF REJECTED", viewModel.ReceiptStatus);
        Assert.Equal("capability-denied", viewModel.ReceiptReason);
        Assert.Contains("source remains available", viewModel.ReceiptSummary);
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task AcknowledgementLossNamesUncertaintyWithoutClaimingRejection()
    {
        var service = new FakeActivityService
        {
            Outcome = OperationStatus.Recovering,
            Failure = FailureCode.AcknowledgementLost,
        };
        using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Plan",
            DraftText = "body",
        };
        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);

        await viewModel.HandoffAsync();

        Assert.Equal("HANDOFF OUTCOME UNCERTAIN", viewModel.ReceiptStatus);
        Assert.Contains("may have accepted", viewModel.ReceiptSummary);
        Assert.Contains("source remains available", viewModel.ReceiptSummary);
        Assert.DoesNotContain("did not accept", viewModel.ReceiptSummary);
        Assert.Equal("acknowledgement-lost", viewModel.ReceiptReason);
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task RuntimeFailureIsSanitizedAndRefreshRemovesDisconnectedTarget()
    {
        const string canary = "SOCKET_SECRET_CANARY";
        var service = new FakeActivityService
        {
            FailureException = new IOException(canary),
        };
        using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Plan",
            DraftText = "body",
        };
        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);

        await viewModel.HandoffAsync();

        Assert.Equal("HANDOFF UNAVAILABLE", viewModel.ReceiptStatus);
        Assert.DoesNotContain(canary, viewModel.ReceiptSummary, StringComparison.Ordinal);
        service.Disconnect();
        Assert.Empty(viewModel.Targets);
        Assert.Null(viewModel.SelectedTarget);
        Assert.False(viewModel.HandoffCommand.CanExecute(null));
    }

    [Fact]
    public async Task ReplaceInventoryBuildsExactPreviewWithoutDestructiveActivation()
    {
        DesktopReplaceTargetSnapshot target = CreateReplaceTarget(
            "33333333-3333-3333-3333-333333333333",
            "Existing target",
            4,
            'A');
        var service = new FakeActivityService
        {
            ReplaceInventoryResult = SuccessfulReplaceInventory(target),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);

        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);

        Assert.True(viewModel.IsReplacePreviewVisible);
        Assert.Equal(
            "REPLACE PREVIEW — CONFIRMATION REQUIRED",
            viewModel.ReplacePreviewStatus);
        Assert.Contains("Incoming note", viewModel.ReplaceIncomingDescription);
        Assert.Contains("workspace.note/v1", viewModel.ReplaceIncomingDescription);
        Assert.Contains("Existing target", viewModel.ReplaceTargetDescription);
        Assert.Contains("Peer desk", viewModel.ReplaceTargetDescription);
        Assert.Contains("revision 4", viewModel.ReplaceTargetDescription);
        Assert.Contains(new string('A', 64), viewModel.ReplaceTargetDescription);
        Assert.False(viewModel.IsDestructiveReplaceAvailable);
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task ReplaceConfirmationIsRevokedWhenExactTargetSelectionChanges()
    {
        var service = new FakeActivityService
        {
            ReplaceInventoryResult = SuccessfulReplaceInventory(
                CreateReplaceTarget(
                    "33333333-3333-3333-3333-333333333333",
                    "Existing target A",
                    4,
                    'A',
                    "left"),
                CreateReplaceTarget(
                    "44444444-4444-4444-4444-444444444444",
                    "Existing target B",
                    7,
                    'B',
                    "right")),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = viewModel.ReplaceTargets[0];

        Assert.True(viewModel.IsReplaceConfirmationAvailable);
        viewModel.HasAcknowledgedReplace = true;

        Assert.True(viewModel.HasAcknowledgedReplace);
        Assert.Equal(
            "PREVIEW CONFIRMED — DESTRUCTIVE REPLACE NOT ACTIVATED",
            viewModel.ReplaceActivationStatus);
        Assert.False(viewModel.IsDestructiveReplaceAvailable);

        viewModel.SelectedReplaceTarget = viewModel.ReplaceTargets[1];

        Assert.False(viewModel.HasAcknowledgedReplace);
        Assert.Equal(
            "CONFIRMATION REQUIRED — REVIEW THE EXACT TARGET SNAPSHOT",
            viewModel.ReplaceActivationStatus);
    }

    [Fact]
    public async Task ReplaceRefreshRejectsChangedRevisionBeforeAnyDestructiveRequest()
    {
        DesktopReplaceTargetSnapshot original = CreateReplaceTarget(
            "33333333-3333-3333-3333-333333333333",
            "Existing target",
            4,
            'A');
        var service = new FakeActivityService
        {
            ReplaceInventoryResult = SuccessfulReplaceInventory(original),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;
        service.ReplaceInventoryResult = SuccessfulReplaceInventory(
            CreateReplaceTarget(
                original.ActivityId.ToString(),
                "Existing target updated",
                5,
                'C'),
            minute: 1);

        await viewModel.RefreshReplaceTargetsAsync();

        DesktopReplaceTargetSnapshot refreshed = Assert.Single(viewModel.ReplaceTargets);
        Assert.Equal(5, refreshed.Revision);
        Assert.Null(viewModel.SelectedReplaceTarget);
        Assert.False(viewModel.HasAcknowledgedReplace);
        Assert.Equal(
            "TARGET CHANGED — REVIEW REFRESHED INVENTORY",
            viewModel.ReplaceInventoryStatus);
        Assert.Contains(
            "revision or descriptor digest changed",
            viewModel.ReplaceInventoryDescription);
        Assert.Contains("No Replace request was sent", viewModel.ReplaceInventoryDescription);
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task ReplaceInventoryIsInvalidatedWhenIncomingSelectionChanges()
    {
        var service = new FakeActivityService
        {
            ReplaceInventoryResult = SuccessfulReplaceInventory(
                CreateReplaceTarget(
                    "33333333-3333-3333-3333-333333333333",
                    "Existing target",
                    4,
                    'A')),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;

        viewModel.SelectedActivity = null;

        Assert.Empty(viewModel.ReplaceTargets);
        Assert.Null(viewModel.SelectedReplaceTarget);
        Assert.False(viewModel.HasAcknowledgedReplace);
        Assert.Equal("REPLACE TARGETS NOT LOADED", viewModel.ReplaceInventoryStatus);
        Assert.False(viewModel.IsReplacePreviewVisible);
    }

    [Fact]
    public async Task ReplaceInventoryFailureIsSanitizedAndRetryable()
    {
        const string canary = "REPLACE_SOCKET_SECRET_CANARY";
        var service = new FakeActivityService
        {
            ReplaceInventoryException = new IOException(canary),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);

        await viewModel.RefreshReplaceTargetsAsync();

        Assert.Equal(
            "REPLACE TARGETS UNAVAILABLE — RETRY",
            viewModel.ReplaceInventoryStatus);
        Assert.Contains(
            "authenticated local connection",
            viewModel.ReplaceInventoryDescription);
        Assert.DoesNotContain(
            canary,
            viewModel.ReplaceInventoryDescription,
            StringComparison.Ordinal);
        Assert.Empty(viewModel.ReplaceTargets);
        Assert.True(service.SourceStillActive);
        Assert.True(viewModel.RefreshReplaceTargetsCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(
        FailureCode.CapabilityDenied,
        "REPLACE TARGETS BLOCKED — REVIEW TRUST")]
    [InlineData(
        FailureCode.ActivityNotFound,
        "INCOMING ACTIVITY CHANGED — SELECT AGAIN")]
    [InlineData(
        FailureCode.AdapterUnavailable,
        "REPLACE UNSUPPORTED FOR THIS ACTIVITY")]
    [InlineData(
        FailureCode.DeadlineExpired,
        "REPLACE TARGET QUERY EXPIRED — RETRY")]
    [InlineData(
        FailureCode.AcknowledgementLost,
        "REPLACE TARGETS UNCONFIRMED — RETRY")]
    public async Task ReplaceInventoryRejectionNamesSafeRecovery(
        FailureCode failureCode,
        string expectedStatus)
    {
        var service = new FakeActivityService
        {
            ReplaceInventoryResult =
                DesktopReplaceTargetInventoryResult.Failed(failureCode),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);

        await viewModel.RefreshReplaceTargetsAsync();

        Assert.Equal(expectedStatus, viewModel.ReplaceInventoryStatus);
        Assert.Contains("No Replace request was sent", viewModel.ReplaceInventoryDescription);
        Assert.Empty(viewModel.ReplaceTargets);
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task LateReplaceInventoryIsDiscardedAfterParticipantChange()
    {
        var pending = new TaskCompletionSource<DesktopReplaceTargetInventoryResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeActivityService
        {
            PendingReplaceInventory = pending,
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);

        Task loading = viewModel.RefreshReplaceTargetsAsync();
        await service.ReplaceInventoryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.SelectedActivity = null;
        pending.SetResult(SuccessfulReplaceInventory(CreateReplaceTarget(
            "33333333-3333-3333-3333-333333333333",
            "Stale target",
            4,
            'A')));

        await loading;

        Assert.Empty(viewModel.ReplaceTargets);
        Assert.Null(viewModel.SelectedReplaceTarget);
        Assert.Equal("REPLACE TARGETS NOT LOADED", viewModel.ReplaceInventoryStatus);
        Assert.False(viewModel.HasAcknowledgedReplace);
    }

    [Fact]
    public async Task ActivityServiceChangeInvalidatesReplaceConfirmation()
    {
        var service = new FakeActivityService
        {
            ReplaceInventoryResult = SuccessfulReplaceInventory(
                CreateReplaceTarget(
                    "33333333-3333-3333-3333-333333333333",
                    "Existing target",
                    4,
                    'A')),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;

        service.SignalChanged();

        Assert.Empty(viewModel.ReplaceTargets);
        Assert.Null(viewModel.SelectedReplaceTarget);
        Assert.False(viewModel.HasAcknowledgedReplace);
        Assert.Equal("REPLACE TARGETS NOT LOADED", viewModel.ReplaceInventoryStatus);
    }

    [Fact]
    public async Task ReplaceInventoryShowsCaptureTimeAndBoundedTruncation()
    {
        var capturedAt = new DateTimeOffset(
            2026,
            7,
            15,
            12,
            34,
            56,
            TimeSpan.Zero);
        ImmutableArray<DesktopReplaceTargetSnapshot> targets = Enumerable
            .Range(1, 64)
            .Select(index => new DesktopReplaceTargetSnapshot(
                TargetId,
                ActivityId.Parse(
                    $"{index:X8}-0000-0000-0000-000000000000"),
                $"Target {index}",
                "workspace.note/v1",
                1,
                new string('A', 64),
                "desktop"))
            .ToImmutableArray();
        var service = new FakeActivityService
        {
            ReplaceInventoryResult = new DesktopReplaceTargetInventoryResult(
                FailureCode.None,
                true,
                capturedAt,
                targets),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);

        await viewModel.RefreshReplaceTargetsAsync();

        Assert.Equal(capturedAt.ToString("O"), viewModel.ReplaceInventoryCapturedAt);
        Assert.Equal(
            "SHOWING FIRST 64 ELIGIBLE TARGETS — INVENTORY TRUNCATED",
            viewModel.ReplaceInventoryCoverage);
        Assert.Equal(64, viewModel.ReplaceTargets.Count);
    }

    [Fact]
    public async Task ExactReplaceRefreshPreservesSelectionButRevokesConfirmation()
    {
        DesktopReplaceTargetSnapshot target = CreateReplaceTarget(
            "33333333-3333-3333-3333-333333333333",
            "Existing target",
            4,
            'A');
        var service = new FakeActivityService
        {
            ReplaceInventoryResult = SuccessfulReplaceInventory(target),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;
        service.ReplaceInventoryResult = SuccessfulReplaceInventory(
            target,
            minute: 1);

        await viewModel.RefreshReplaceTargetsAsync();

        Assert.Equal(target, viewModel.SelectedReplaceTarget);
        Assert.False(viewModel.HasAcknowledgedReplace);
        Assert.Equal(
            "TARGETS REFRESHED — CONFIRM AGAIN",
            viewModel.ReplaceInventoryStatus);
    }

    [Fact]
    public async Task MissingReplaceTargetAfterRefreshRequiresFreshSelection()
    {
        DesktopReplaceTargetSnapshot target = CreateReplaceTarget(
            "33333333-3333-3333-3333-333333333333",
            "Existing target",
            4,
            'A');
        var service = new FakeActivityService
        {
            ReplaceInventoryResult = SuccessfulReplaceInventory(target),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;
        service.ReplaceInventoryResult = SuccessfulReplaceInventory(minute: 1);

        await viewModel.RefreshReplaceTargetsAsync();

        Assert.Empty(viewModel.ReplaceTargets);
        Assert.Null(viewModel.SelectedReplaceTarget);
        Assert.False(viewModel.HasAcknowledgedReplace);
        Assert.Equal(
            "TARGET CHANGED — REVIEW REFRESHED INVENTORY",
            viewModel.ReplaceInventoryStatus);
        Assert.Contains(
            "no longer eligible",
            viewModel.ReplaceInventoryDescription);
    }

    [Fact]
    public async Task ConfiguredDestructiveReplaceStillRequiresExactConfirmation()
    {
        DesktopReplaceTargetSnapshot target = CreateReplaceTarget(
            "33333333-3333-3333-3333-333333333333",
            "Existing target",
            4,
            'A');
        var service = new FakeActivityService
        {
            IsDestructiveReplaceAvailable = true,
            ReplaceInventoryResult = SuccessfulReplaceInventory(target),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);

        await viewModel.RefreshReplaceTargetsAsync();
        Assert.False(viewModel.IsDestructiveReplaceAvailable);
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        Assert.False(viewModel.IsDestructiveReplaceAvailable);

        viewModel.HasAcknowledgedReplace = true;

        Assert.True(viewModel.IsDestructiveReplaceAvailable);
        Assert.Equal(
            "PREVIEW CONFIRMED — DESTRUCTIVE REPLACE READY",
            viewModel.ReplaceActivationStatus);
        await viewModel.RefreshReplaceTargetsAsync();
        Assert.False(viewModel.IsDestructiveReplaceAvailable);
    }

    private static ActivityWorkspaceViewModel CreateReplaceReadyViewModel(
        FakeActivityService service)
    {
        var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Incoming note",
            DraftText = "portable body",
        };
        viewModel.CreateWorkspaceNote();
        viewModel.SelectedTarget = Assert.Single(viewModel.Targets);
        return viewModel;
    }

    private static DesktopReplaceTargetSnapshot CreateReplaceTarget(
        string activityId,
        string title,
        long revision,
        char digestCharacter,
        string placementSlot = "desktop") => new(
            TargetId,
            ActivityId.Parse(activityId),
            title,
            "workspace.note/v1",
            revision,
            new string(digestCharacter, 64),
            placementSlot);

    private static DesktopReplaceTargetInventoryResult SuccessfulReplaceInventory(
        DesktopReplaceTargetSnapshot? target = null,
        DesktopReplaceTargetSnapshot? secondTarget = null,
        int minute = 0) => new(
            FailureCode.None,
            false,
            new DateTimeOffset(2026, 7, 15, 12, minute, 0, TimeSpan.Zero),
            (target, secondTarget) switch
            {
                (null, _) => [],
                (_, null) => [target],
                _ => [target, secondTarget],
            });

    private sealed class FakeActivityService : IDesktopActivityService
    {
        private readonly List<DesktopActivitySnapshot> activities = [];
        private ActivityDescriptor? descriptor;
        private bool connected = true;

        public event Action? Changed;

        public bool IsDestructiveReplaceAvailable { get; set; }

        public OperationStatus Outcome { get; set; } = OperationStatus.Committed;

        public FailureCode Failure { get; set; } = FailureCode.None;

        public Exception? FailureException { get; set; }

        public DesktopReplaceTargetInventoryResult ReplaceInventoryResult { get; set; } =
            DesktopReplaceTargetInventoryResult.Failed(FailureCode.PeerUnavailable);

        public Exception? ReplaceInventoryException { get; set; }

        public TaskCompletionSource<DesktopReplaceTargetInventoryResult>?
            PendingReplaceInventory
        { get; set; }

        public TaskCompletionSource ReplaceInventoryStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool SourceStillActive { get; private set; } = true;

        public DesktopActivitySnapshot CreateWorkspaceNote(
            string title,
            string text,
            ActivitySensitivity sensitivity)
        {
            descriptor = ActivityDescriptor.Create(
                ActivityId.From(Guid.NewGuid()),
                ActivityKind.Parse("workspace.note/v1"),
                LocalId,
                title,
                JsonSerializer.Serialize(new { text }),
                sensitivity);
            var snapshot = new DesktopActivitySnapshot(
                descriptor.Id,
                descriptor.Title,
                descriptor.Kind.Value,
                descriptor.Sensitivity,
                ActivityLifecycle.Active);
            activities.Add(snapshot);
            Changed?.Invoke();
            return snapshot;
        }

        public ImmutableArray<DesktopActivitySnapshot> GetActivities() =>
            activities.ToImmutableArray();

        public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets() => connected
            ? [new DesktopActivityTargetSnapshot(TargetId, "Peer desk")]
            : [];

        public ValueTask<OperationReceipt> HandoffAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(OperationKind.Handoff, cancellationToken);

        public ValueTask<OperationReceipt> MoveAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            CancellationToken cancellationToken = default)
        {
            ValueTask<OperationReceipt> result = ExecuteAsync(
                OperationKind.Move,
                cancellationToken);
            if (result.IsCompletedSuccessfully
                && result.Result.Status == OperationStatus.Committed)
            {
                SourceStillActive = false;
                activities.Clear();
                Changed?.Invoke();
            }

            return result;
        }

        public ValueTask<DesktopReplaceTargetInventoryResult> GetReplaceTargetsAsync(
            ActivityId incomingActivityId,
            DeviceId targetDeviceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReplaceInventoryException is not null)
            {
                return ValueTask.FromException<DesktopReplaceTargetInventoryResult>(
                    ReplaceInventoryException);
            }

            if (PendingReplaceInventory is not null)
            {
                ReplaceInventoryStarted.TrySetResult();
                return new ValueTask<DesktopReplaceTargetInventoryResult>(
                    PendingReplaceInventory.Task);
            }

            return ValueTask.FromResult(ReplaceInventoryResult);
        }

        private ValueTask<OperationReceipt> ExecuteAsync(
            OperationKind kind,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailureException is not null)
            {
                return ValueTask.FromException<OperationReceipt>(FailureException);
            }

            ActivityDescriptor current = descriptor
                ?? throw new InvalidOperationException("No Activity exists.");
            var operationId = OperationId.From(Guid.NewGuid());
            var correlationId = CorrelationId.From(Guid.NewGuid());
            OperationReceipt receipt = Outcome switch
            {
                OperationStatus.Committed => OperationReceipt.Committed(
                    operationId,
                    correlationId,
                    kind,
                    LocalId,
                    TargetId,
                    current,
                    DateTimeOffset.UtcNow),
                OperationStatus.CommittedWithWarning =>
                    OperationReceipt.CommittedWithWarning(
                        operationId,
                        correlationId,
                        kind,
                        LocalId,
                        TargetId,
                        current,
                        DateTimeOffset.UtcNow,
                        Failure),
                OperationStatus.Rejected => OperationReceipt.Rejected(
                    operationId,
                    correlationId,
                    kind,
                    LocalId,
                    TargetId,
                    current,
                    DateTimeOffset.UtcNow,
                    Failure),
                OperationStatus.Recovering => OperationReceipt.Recovering(
                    operationId,
                    correlationId,
                    kind,
                    LocalId,
                    TargetId,
                    current,
                    DateTimeOffset.UtcNow,
                    Failure),
                _ => throw new InvalidOperationException("Unsupported fake outcome."),
            };
            return ValueTask.FromResult(receipt);
        }

        public void Disconnect()
        {
            connected = false;
            Changed?.Invoke();
        }

        public void SignalChanged() => Changed?.Invoke();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

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

    [Fact]
    public async Task ConfirmedReplaceProjectsCommittedReceiptAndCapsule()
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
            ReplaceResultFactory = CreateCommittedDesktopReplaceResult,
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;

        await viewModel.ReplaceAsync();

        Assert.Equal("REPLACE COMMITTED", viewModel.ReplaceOperationStatus);
        Assert.Contains("target recovery", viewModel.ReplaceOperationDescription);
        Assert.Equal("none", viewModel.ReplaceOperationReason);
        Assert.NotEmpty(viewModel.ReplaceOperationId);
        Assert.NotEmpty(viewModel.ReplaceOperationCorrelationId);
        Assert.NotEmpty(viewModel.ReplaceOperationOccurredAt);
        Assert.NotEmpty(viewModel.ReplaceOperationCapsule);
        Assert.Contains("2026-07-15T12:15:00", viewModel.ReplaceOperationUndoExpiry);
        Assert.False(viewModel.HasAcknowledgedReplace);
        Assert.False(viewModel.IsDestructiveReplaceAvailable);
        Assert.Empty(viewModel.ReplaceTargets);
        Assert.True(service.SourceStillActive);
        Assert.Equal(viewModel.SelectedActivity?.ActivityId, service.RequestedReplaceActivityId);
        Assert.Equal(target, service.RequestedReplaceTarget);
    }

    [Fact]
    public async Task CommittedReplaceWithoutVerifiedCapsuleIsNotPresentedAsSuccess()
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
            ReplaceResultFactory = static (incomingId, selected) =>
                CreateCommittedDesktopReplaceResult(incomingId, selected) with
                {
                    UndoCapsule = null,
                },
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;

        await viewModel.ReplaceAsync();

        Assert.Equal(
            "REPLACE RESULT INVALID — INSPECT TARGET RECOVERY",
            viewModel.ReplaceOperationStatus);
        Assert.Contains(
            "no verified undo capsule",
            viewModel.ReplaceOperationDescription);
        Assert.Single(viewModel.ReplaceTargets);
        Assert.False(viewModel.HasAcknowledgedReplace);
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task ReplaceAcknowledgementLossNamesUncertainTargetRecoveryBoundary()
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
            ReplaceResultFactory = static (incomingId, selected) =>
                CreateUnacknowledgedDesktopReplaceResult(
                    ActivityDeliveryStatus.AcknowledgementLost,
                    FailureCode.AcknowledgementLost),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;

        await viewModel.ReplaceAsync();

        Assert.Equal(
            "REPLACE OUTCOME UNCERTAIN — DO NOT RETRY",
            viewModel.ReplaceOperationStatus);
        Assert.Contains("may have committed", viewModel.ReplaceOperationDescription);
        Assert.Contains("target recovery", viewModel.ReplaceOperationDescription);
        Assert.Contains("source remains active", viewModel.ReplaceOperationDescription);
        Assert.Contains("new Operation ID", viewModel.ReplaceOperationDescription);
        Assert.Equal("acknowledgement-lost", viewModel.ReplaceOperationReason);
        Assert.Empty(viewModel.ReplaceOperationCapsule);
        Assert.False(viewModel.HasAcknowledgedReplace);
        Assert.False(viewModel.ReplaceCommand.CanExecute(null));
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task PendingReplaceDisablesDuplicateActivation()
    {
        DesktopReplaceTargetSnapshot target = CreateReplaceTarget(
            "33333333-3333-3333-3333-333333333333",
            "Existing target",
            4,
            'A');
        var pending = new TaskCompletionSource<DesktopReplaceOperationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeActivityService
        {
            IsDestructiveReplaceAvailable = true,
            ReplaceInventoryResult = SuccessfulReplaceInventory(target),
            PendingReplace = pending,
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;

        Task replacing = viewModel.ReplaceAsync();
        await service.ReplaceStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.ReplaceCommand.CanExecute(null));
        Assert.Equal(
            "REPLACE PENDING — DUPLICATE DISABLED",
            viewModel.ReplaceOperationStatus);
        pending.SetResult(CreateUnacknowledgedDesktopReplaceResult(
            ActivityDeliveryStatus.NotDelivered,
            FailureCode.PeerUnavailable));
        await replacing;
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SendTimeStaleReplaceClearsPreviewAndRequiresRefresh()
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
            ReplaceResultFactory = static (incomingId, selected) =>
                CreateUnacknowledgedDesktopReplaceResult(
                    ActivityDeliveryStatus.NotDelivered,
                    FailureCode.RevisionConflict) with
                {
                    OperationId = null,
                    CorrelationId = null,
                },
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;

        await viewModel.ReplaceAsync();

        Assert.Equal(
            "REPLACE NOT SENT — TARGET CHANGED",
            viewModel.ReplaceOperationStatus);
        Assert.Contains("fresh inventory", viewModel.ReplaceOperationDescription);
        Assert.Equal("TARGET CHANGED — REFRESH REQUIRED", viewModel.ReplaceInventoryStatus);
        Assert.Empty(viewModel.ReplaceOperationId);
        Assert.Empty(viewModel.ReplaceOperationCorrelationId);
        Assert.Empty(viewModel.ReplaceTargets);
        Assert.Null(viewModel.SelectedReplaceTarget);
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task ReplaceExceptionIsSanitizedAsUnknownTargetBoundary()
    {
        const string canary = "REPLACE_PRIVATE_EXCEPTION_CANARY";
        DesktopReplaceTargetSnapshot target = CreateReplaceTarget(
            "33333333-3333-3333-3333-333333333333",
            "Existing target",
            4,
            'A');
        var service = new FakeActivityService
        {
            IsDestructiveReplaceAvailable = true,
            ReplaceInventoryResult = SuccessfulReplaceInventory(target),
            ReplaceException = new IOException(canary),
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;

        await viewModel.ReplaceAsync();

        Assert.Equal(
            "REPLACE OUTCOME UNAVAILABLE — INSPECT TARGET RECOVERY",
            viewModel.ReplaceOperationStatus);
        Assert.Contains("may have crossed", viewModel.ReplaceOperationDescription);
        Assert.DoesNotContain(canary, viewModel.ReplaceOperationDescription);
        Assert.False(viewModel.HasAcknowledgedReplace);
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task ExistingTargetRecoveryBoundaryBlocksRetryGuidance()
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
            ReplaceResultFactory = CreateRecoveryBlockedDesktopReplaceResult,
        };
        using ActivityWorkspaceViewModel viewModel =
            CreateReplaceReadyViewModel(service);
        await viewModel.RefreshReplaceTargetsAsync();
        viewModel.SelectedReplaceTarget = Assert.Single(viewModel.ReplaceTargets);
        viewModel.HasAcknowledgedReplace = true;

        await viewModel.ReplaceAsync();

        Assert.Equal(
            "REPLACE BLOCKED BY TARGET RECOVERY — DO NOT RETRY",
            viewModel.ReplaceOperationStatus);
        Assert.Contains("did not mutate", viewModel.ReplaceOperationDescription);
        Assert.Contains("resolve the existing boundary", viewModel.ReplaceOperationDescription);
        Assert.True(service.SourceStillActive);
    }

    [Fact]
    public async Task StartupShowsPayloadFreeTargetLocalReplaceRecoveryAndExactExpiry()
    {
        var service = new FakeActivityService
        {
            ReplaceRecoveryResult = await CreateReplaceRecoveryResultAsync(),
        };
        await using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance);

        await viewModel.InitializeAsync();

        Assert.Equal(
            "TARGET-LOCAL REPLACE HISTORY — NO UNDO ACTION",
            viewModel.ReplaceRecoveryStatus);
        Assert.Equal(
            "1 TARGET-LOCAL REPLACE / UNDO RECORDS",
            viewModel.ReplaceRecoveryCoverage);
        DesktopReplaceRecoveryItem item = Assert.Single(
            viewModel.ReplaceRecoveryItems);
        Assert.Equal("TARGET-LOCAL REPLACE", item.Kind);
        Assert.Equal("COMMITTED", item.State);
        Assert.Equal("none", item.Reason);
        Assert.Contains(LocalId.ToString(), item.Participants, StringComparison.Ordinal);
        Assert.Contains(TargetId.ToString(), item.Participants, StringComparison.Ordinal);
        Assert.Contains("2026-07-15T12:10:00.0000000+00:00", item.Undo);
        Assert.Contains("UNCONSUMED AT SNAPSHOT", item.Undo, StringComparison.Ordinal);
        Assert.Contains("EXACT CURRENT REPLACEMENT NOT PROVEN", item.Undo, StringComparison.Ordinal);
        string visible = string.Join(
            '\n',
            item.Kind,
            item.State,
            item.Reason,
            item.OperationId,
            item.CorrelationId,
            item.Participants,
            item.Activities,
            item.Capsule,
            item.Timestamp,
            item.Undo);
        Assert.DoesNotContain("Original secret title", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("Incoming secret title", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("secret body", visible, StringComparison.Ordinal);
        Assert.False(viewModel.IsDestructiveReplaceAvailable);
    }

    [Fact]
    public async Task AvailableTargetLocalUndoRequiresSelectionAndExactConfirmation()
    {
        var service = new FakeActivityService
        {
            ReplaceRecoveryResult = await CreateReplaceRecoveryResultAsync(
                undoable: true),
        };
        await using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance);
        await viewModel.InitializeAsync();

        DesktopReplaceRecoveryItem item = Assert.Single(
            viewModel.ReplaceRecoveryItems);
        Assert.True(item.CanUndo);
        Assert.False(viewModel.IsTargetLocalUndoAvailable);
        Assert.False(viewModel.HasAcknowledgedTargetLocalUndo);

        viewModel.SelectedReplaceRecoveryItem = item;

        Assert.True(viewModel.IsTargetLocalUndoConfirmationAvailable);
        Assert.Contains(item.Capsule, viewModel.TargetLocalUndoConfirmationDescription);
        Assert.Contains(item.Activities, viewModel.TargetLocalUndoConfirmationDescription);
        Assert.Contains(
            "2026-07-15T12:10:00.0000000+00:00",
            viewModel.TargetLocalUndoConfirmationDescription);
        Assert.DoesNotContain(
            "Original secret title",
            viewModel.TargetLocalUndoConfirmationDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Incoming secret title",
            viewModel.TargetLocalUndoConfirmationDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "secret body",
            viewModel.TargetLocalUndoConfirmationDescription,
            StringComparison.Ordinal);
        Assert.False(viewModel.IsTargetLocalUndoAvailable);

        viewModel.HasAcknowledgedTargetLocalUndo = true;

        Assert.True(viewModel.IsTargetLocalUndoAvailable);
        Assert.Equal(
            "TARGET-LOCAL UNDO CONFIRMED — READY",
            viewModel.TargetLocalUndoStatus);
    }

    [Fact]
    public async Task RecoveryRefreshRevokesTargetLocalUndoSelectionAndConfirmation()
    {
        var service = new FakeActivityService
        {
            ReplaceRecoveryResult =
                await CreateReplaceRecoveryResultAsync(undoable: true),
        };
        await using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance);
        await viewModel.InitializeAsync();
        viewModel.SelectedReplaceRecoveryItem = Assert.Single(
            viewModel.ReplaceRecoveryItems);
        viewModel.HasAcknowledgedTargetLocalUndo = true;
        Assert.True(viewModel.IsTargetLocalUndoAvailable);

        service.SignalChanged();

        Assert.Null(viewModel.SelectedReplaceRecoveryItem);
        Assert.False(viewModel.HasAcknowledgedTargetLocalUndo);
        Assert.False(viewModel.IsTargetLocalUndoAvailable);
        Assert.Equal(
            "TARGET-LOCAL UNDO — SELECT AN AVAILABLE CAPSULE",
            viewModel.TargetLocalUndoStatus);
    }

    [Fact]
    public async Task ConfirmedTargetLocalUndoPresentsCommittedOutcomeAndRefreshesState()
    {
        DesktopReplaceRecoveryResult initial =
            await CreateReplaceRecoveryResultAsync(undoable: true);
        DesktopReplaceRecoveryResult completed =
            await CreateReplaceRecoveryResultAsync(consumed: true);
        var service = new FakeActivityService
        {
            ReplaceRecoveryResult = initial,
            ReplaceRecoveryResultAfterUndo = completed,
            UndoResult = UndoReplaceResult.Committed(
                OperationContext.Create(
                    OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                    CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
                    new DateTimeOffset(2026, 7, 15, 12, 1, 30, TimeSpan.Zero)),
                UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                new DateTimeOffset(2026, 7, 15, 12, 1, 1, TimeSpan.Zero)),
        };
        await using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance);
        await viewModel.InitializeAsync();
        viewModel.SelectedReplaceRecoveryItem = Assert.Single(
            viewModel.ReplaceRecoveryItems);
        viewModel.HasAcknowledgedTargetLocalUndo = true;

        await viewModel.UndoReplaceAsync();

        Assert.Equal(
            UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            service.RequestedUndoCapsuleId);
        Assert.Equal("TARGET-LOCAL UNDO COMMITTED", viewModel.TargetLocalUndoStatus);
        Assert.Equal("none", viewModel.TargetLocalUndoReason);
        Assert.Equal(
            "2026-07-15T12:01:01.0000000+00:00",
            viewModel.TargetLocalUndoOccurredAt);
        Assert.Contains("consumed", viewModel.TargetLocalUndoDescription);
        Assert.False(viewModel.HasAcknowledgedTargetLocalUndo);
        Assert.False(viewModel.IsTargetLocalUndoAvailable);
        Assert.DoesNotContain(
            viewModel.ReplaceRecoveryItems,
            static item => item.CanUndo);
    }

    [Fact]
    public async Task PendingTargetLocalUndoDisablesDuplicateUntilRecordedOutcome()
    {
        UndoCapsuleId capsuleId =
            UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        var pending = new TaskCompletionSource<UndoReplaceResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeActivityService
        {
            ReplaceRecoveryResult =
                await CreateReplaceRecoveryResultAsync(undoable: true),
            ReplaceRecoveryResultAfterUndo =
                await CreateReplaceRecoveryResultAsync(consumed: true),
            PendingUndo = pending,
        };
        await using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance);
        await viewModel.InitializeAsync();
        viewModel.SelectedReplaceRecoveryItem = Assert.Single(
            viewModel.ReplaceRecoveryItems);
        viewModel.HasAcknowledgedTargetLocalUndo = true;

        Task operation = viewModel.UndoReplaceAsync();
        await service.UndoStarted.Task;

        Assert.True(viewModel.IsBusy);
        Assert.Equal(
            "TARGET-LOCAL UNDO PENDING — DO NOT RETRY",
            viewModel.TargetLocalUndoStatus);
        Assert.False(viewModel.IsTargetLocalUndoAvailable);
        Assert.False(viewModel.TargetLocalUndoCommand.CanExecute(null));

        pending.TrySetResult(UndoReplaceResult.Committed(
            OperationContext.Create(
                OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
                new DateTimeOffset(2026, 7, 15, 12, 1, 30, TimeSpan.Zero)),
            capsuleId,
            new DateTimeOffset(2026, 7, 15, 12, 1, 1, TimeSpan.Zero)));
        await operation;

        Assert.False(viewModel.IsBusy);
        Assert.Equal("TARGET-LOCAL UNDO COMMITTED", viewModel.TargetLocalUndoStatus);
        Assert.False(viewModel.TargetLocalUndoCommand.CanExecute(null));
    }

    [Fact]
    public async Task TargetLocalUndoExceptionIsMappedWithoutLeakingDetails()
    {
        const string canary = "UNDO_ADAPTER_EXCEPTION_SECRET_CANARY";
        var service = new FakeActivityService
        {
            ReplaceRecoveryResult =
                await CreateReplaceRecoveryResultAsync(undoable: true),
            UndoException = new IOException(canary),
        };
        await using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance);
        await viewModel.InitializeAsync();
        viewModel.SelectedReplaceRecoveryItem = Assert.Single(
            viewModel.ReplaceRecoveryItems);
        viewModel.HasAcknowledgedTargetLocalUndo = true;

        await viewModel.UndoReplaceAsync();

        Assert.Equal(
            "TARGET-LOCAL UNDO OUTCOME UNAVAILABLE — INSPECT RECOVERY",
            viewModel.TargetLocalUndoStatus);
        Assert.DoesNotContain(
            canary,
            viewModel.TargetLocalUndoStatus,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            canary,
            viewModel.TargetLocalUndoDescription,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            canary,
            viewModel.TargetLocalUndoReason,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        OperationStatus.Rejected,
        FailureCode.UndoCapsuleExpired,
        "TARGET-LOCAL UNDO REJECTED",
        "expired",
        "undo-capsule-expired")]
    [InlineData(
        OperationStatus.Rejected,
        FailureCode.UndoCapsuleConsumed,
        "TARGET-LOCAL UNDO REJECTED",
        "already consumed",
        "undo-capsule-consumed")]
    [InlineData(
        OperationStatus.Rejected,
        FailureCode.RevisionConflict,
        "TARGET-LOCAL UNDO REJECTED",
        "no longer the exact current",
        "revision-conflict")]
    [InlineData(
        OperationStatus.Failed,
        FailureCode.UndoUnavailable,
        "TARGET-LOCAL UNDO FAILED",
        "did not complete",
        "undo-unavailable")]
    [InlineData(
        OperationStatus.Recovering,
        FailureCode.InternalFailure,
        "TARGET-LOCAL UNDO OUTCOME UNCERTAIN — DUPLICATE DISABLED",
        "pending record blocks duplicate",
        "internal-failure")]
    public async Task TargetLocalUndoPresentsEveryRecordedTerminalOutcome(
        OperationStatus status,
        FailureCode failureCode,
        string expectedStatus,
        string expectedDescription,
        string expectedReason)
    {
        UndoCapsuleId capsuleId =
            UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        OperationContext context = OperationContext.Create(
            OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
            new DateTimeOffset(2026, 7, 15, 12, 1, 30, TimeSpan.Zero));
        UndoReplaceResult result = UndoReplaceResult.FromRecordedResult(
            context.OperationId,
            context.CorrelationId,
            capsuleId,
            status,
            failureCode,
            new DateTimeOffset(2026, 7, 15, 12, 1, 1, TimeSpan.Zero));
        var service = new FakeActivityService
        {
            ReplaceRecoveryResult =
                await CreateReplaceRecoveryResultAsync(undoable: true),
            ReplaceRecoveryResultAfterUndo =
                await CreateReplaceRecoveryResultAsync(
                    undoStatus: status,
                    undoFailureCode: failureCode),
            UndoResult = result,
        };
        await using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance);
        await viewModel.InitializeAsync();
        viewModel.SelectedReplaceRecoveryItem = Assert.Single(
            viewModel.ReplaceRecoveryItems);
        viewModel.HasAcknowledgedTargetLocalUndo = true;

        await viewModel.UndoReplaceAsync();

        Assert.Equal(expectedStatus, viewModel.TargetLocalUndoStatus);
        Assert.Contains(expectedDescription, viewModel.TargetLocalUndoDescription);
        Assert.Equal(expectedReason, viewModel.TargetLocalUndoReason);
        Assert.DoesNotContain(
            viewModel.ReplaceRecoveryItems,
            static item => item.CanUndo);
    }

    [Fact]
    public async Task UnavailableReplaceRecoveryDoesNotBlockNonReplaceWorkspace()
    {
        var service = new FakeActivityService();
        await using var viewModel = new ActivityWorkspaceViewModel(
            service,
            InlineDesktopUiDispatcher.Instance)
        {
            DraftTitle = "Still usable",
            DraftText = "Non-Replace work remains available",
        };

        await viewModel.InitializeAsync();

        Assert.Equal(
            "REPLACE RECOVERY STATE UNAVAILABLE — REPLACE LOCKED",
            viewModel.ReplaceRecoveryStatus);
        Assert.Contains(
            "Handoff and Move remain available",
            viewModel.ReplaceRecoveryDescription,
            StringComparison.Ordinal);
        Assert.True(viewModel.IsNoteCreationAvailable);
        viewModel.CreateWorkspaceNote();
        Assert.Single(viewModel.Activities);
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

    private static DesktopReplaceOperationResult CreateCommittedDesktopReplaceResult(
        ActivityId incomingActivityId,
        DesktopReplaceTargetSnapshot selectedTarget)
    {
        var occurredAt = new DateTimeOffset(
            2026,
            7,
            15,
            12,
            0,
            0,
            TimeSpan.Zero);
        OperationContext context = OperationContext.Create(
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"),
            occurredAt.AddSeconds(30));
        ActivityDescriptor incoming = ActivityDescriptor.Create(
            incomingActivityId,
            ActivityKind.Parse("workspace.note/v1"),
            LocalId,
            "Incoming note",
            "{\"text\":\"portable body\"}");
        OperationReceipt receipt = OperationReceipt.Committed(
            context.OperationId,
            context.CorrelationId,
            OperationKind.Replace,
            LocalId,
            TargetId,
            incoming,
            occurredAt);
        var capsule = new UndoCapsuleReference(
            UndoCapsuleId.Parse("66666666-6666-6666-6666-666666666666"),
            context.OperationId,
            context.CorrelationId,
            TargetId,
            selectedTarget.ActivityId,
            selectedTarget.Revision,
            selectedTarget.DescriptorDigest,
            incomingActivityId,
            incoming.DescriptorDigest,
            occurredAt.AddMinutes(15));
        return new DesktopReplaceOperationResult(
            context.OperationId,
            context.CorrelationId,
            ActivityDeliveryStatus.Acknowledged,
            FailureCode.None,
            occurredAt,
            receipt,
            capsule);
    }

    private static DesktopReplaceOperationResult
        CreateUnacknowledgedDesktopReplaceResult(
            ActivityDeliveryStatus deliveryStatus,
            FailureCode failureCode)
    {
        var occurredAt = new DateTimeOffset(
            2026,
            7,
            15,
            12,
            0,
            0,
            TimeSpan.Zero);
        return new DesktopReplaceOperationResult(
            OperationId.Parse("44444444-4444-4444-4444-444444444444"),
            CorrelationId.Parse("55555555-5555-5555-5555-555555555555"),
            deliveryStatus,
            failureCode,
            occurredAt,
            null,
            null);
    }

    private static DesktopReplaceOperationResult
        CreateRecoveryBlockedDesktopReplaceResult(
            ActivityId incomingActivityId,
            DesktopReplaceTargetSnapshot selectedTarget)
    {
        var occurredAt = new DateTimeOffset(
            2026,
            7,
            15,
            12,
            0,
            0,
            TimeSpan.Zero);
        OperationId operationId =
            OperationId.Parse("88888888-8888-8888-8888-888888888888");
        CorrelationId correlationId =
            CorrelationId.Parse("99999999-9999-9999-9999-999999999999");
        ActivityDescriptor incoming = ActivityDescriptor.Create(
            incomingActivityId,
            ActivityKind.Parse("workspace.note/v1"),
            LocalId,
            "Incoming note",
            "{\"text\":\"portable body\"}");
        OperationReceipt receipt = OperationReceipt.Failed(
            operationId,
            correlationId,
            OperationKind.Replace,
            LocalId,
            TargetId,
            incoming,
            occurredAt,
            FailureCode.OperationInProgress);
        return new DesktopReplaceOperationResult(
            operationId,
            correlationId,
            ActivityDeliveryStatus.Acknowledged,
            FailureCode.OperationInProgress,
            occurredAt,
            receipt,
            null);
    }

    private static async Task<DesktopReplaceRecoveryResult>
        CreateReplaceRecoveryResultAsync(
            bool undoable = false,
            bool consumed = false,
            OperationStatus? undoStatus = null,
            FailureCode undoFailureCode = FailureCode.None)
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        using PersistentReplaceStateStore state =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        var original = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ActivityKind.Parse("workspace.note/v1"),
                TargetId,
                "Original secret title",
                "{\"text\":\"secret body one\"}"),
            ActivityPlacement.On(TargetId, "desktop"),
            revision: 4);
        var incoming = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                ActivityKind.Parse("workspace.note/v1"),
                LocalId,
                "Incoming secret title",
                "{\"text\":\"secret body two\"}"),
            ActivityPlacement.On(TargetId, "desktop"),
            revision: 5);
        OperationId operationId =
            OperationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        CorrelationId correlationId =
            CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        UndoCapsule capsule = UndoCapsule.Create(
            UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            OperationContext.Create(
                operationId,
                correlationId,
                new DateTimeOffset(2026, 7, 15, 12, 0, 30, TimeSpan.Zero)),
            LocalId,
            TargetId,
            original,
            incoming,
            new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 15, 12, 10, 0, TimeSpan.Zero));
        Assert.True(await state.TryAddAsync(capsule));
        await state.ExecuteOnceAsync(
            operationId,
            new string('A', 64),
            _ => ValueTask.FromResult(OperationReceipt.Committed(
                operationId,
                correlationId,
                OperationKind.Replace,
                LocalId,
                TargetId,
                incoming.Descriptor,
                new DateTimeOffset(2026, 7, 15, 12, 0, 1, TimeSpan.Zero))),
            CancellationToken.None);
        OperationStatus? recordedUndoStatus = consumed
            ? OperationStatus.Committed
            : undoStatus;
        if (recordedUndoStatus is not null)
        {
            OperationId undoOperationId =
                OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
            Assert.Equal(
                UndoJournalPreparationStatus.Prepared,
                (await state.PrepareUndoAsync(
                    capsule.Id,
                    undoOperationId,
                    new string('B', 64))).Status);
            await state.CompleteUndoAsync(
                undoOperationId,
                UndoReplaceResult.FromRecordedResult(
                    undoOperationId,
                    CorrelationId.Parse(
                        "99999999-9999-9999-9999-999999999999"),
                    capsule.Id,
                    recordedUndoStatus.Value,
                    recordedUndoStatus == OperationStatus.Committed
                        ? FailureCode.None
                        : undoFailureCode,
                    new DateTimeOffset(
                        2026,
                        7,
                        15,
                        12,
                        1,
                        1,
                        TimeSpan.Zero)));
        }
        ReplaceRecoverySnapshot snapshot = state.GetRecoverySnapshot(
            new DateTimeOffset(2026, 7, 15, 12, 1, 0, TimeSpan.Zero));
        return undoable
            ? DesktopReplaceRecoveryResult.Available(snapshot, [capsule.Id])
            : DesktopReplaceRecoveryResult.Available(snapshot);
    }

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

        public DesktopReplaceRecoveryResult ReplaceRecoveryResult { get; set; } =
            DesktopReplaceRecoveryResult.Unavailable;

        public DesktopReplaceRecoveryResult? ReplaceRecoveryResultAfterUndo
        { get; set; }

        public UndoReplaceResult? UndoResult { get; set; }

        public Exception? UndoException { get; set; }

        public TaskCompletionSource<UndoReplaceResult>? PendingUndo { get; set; }

        public TaskCompletionSource UndoStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public UndoCapsuleId? RequestedUndoCapsuleId { get; private set; }

        public Exception? ReplaceInventoryException { get; set; }

        public TaskCompletionSource<DesktopReplaceTargetInventoryResult>?
            PendingReplaceInventory
        { get; set; }

        public TaskCompletionSource ReplaceInventoryStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<ActivityId, DesktopReplaceTargetSnapshot,
            DesktopReplaceOperationResult>? ReplaceResultFactory
        { get; set; }

        public TaskCompletionSource<DesktopReplaceOperationResult>? PendingReplace
        { get; set; }

        public Exception? ReplaceException { get; set; }

        public TaskCompletionSource ReplaceStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ActivityId? RequestedReplaceActivityId { get; private set; }

        public DesktopReplaceTargetSnapshot? RequestedReplaceTarget { get; private set; }

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

        public DesktopReplaceRecoveryResult GetReplaceRecoveryState() =>
            ReplaceRecoveryResult;

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

        public async ValueTask<DesktopReplaceOperationResult> ReplaceAsync(
            ActivityId incomingActivityId,
            DesktopReplaceTargetSnapshot selectedTarget,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedReplaceActivityId = incomingActivityId;
            RequestedReplaceTarget = selectedTarget;
            ReplaceStarted.TrySetResult();
            if (ReplaceException is not null)
            {
                throw ReplaceException;
            }

            if (PendingReplace is not null)
            {
                return await PendingReplace.Task.WaitAsync(cancellationToken);
            }

            return ReplaceResultFactory?.Invoke(incomingActivityId, selectedTarget)
                ?? throw new InvalidOperationException(
                    "No fake Replace result was configured.");
        }

        public async ValueTask<UndoReplaceResult> UndoReplaceAsync(
            UndoCapsuleId capsuleId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedUndoCapsuleId = capsuleId;
            UndoStarted.TrySetResult();
            if (UndoException is not null)
            {
                throw UndoException;
            }

            UndoReplaceResult result = PendingUndo is not null
                ? await PendingUndo.Task.WaitAsync(cancellationToken)
                : UndoResult
                    ?? throw new InvalidOperationException(
                        "No fake undo result was configured.");
            if (ReplaceRecoveryResultAfterUndo is not null)
            {
                ReplaceRecoveryResult = ReplaceRecoveryResultAfterUndo;
            }

            return result;
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

    private sealed class MemoryReplaceStatePayloadStore : IReplaceStatePayloadStore
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
}

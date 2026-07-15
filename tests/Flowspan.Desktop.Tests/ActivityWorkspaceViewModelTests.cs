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

    private sealed class FakeActivityService : IDesktopActivityService
    {
        private readonly List<DesktopActivitySnapshot> activities = [];
        private ActivityDescriptor? descriptor;
        private bool connected = true;

        public event Action? Changed;

        public OperationStatus Outcome { get; set; } = OperationStatus.Committed;

        public FailureCode Failure { get; set; } = FailureCode.None;

        public Exception? FailureException { get; set; }

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
            CancellationToken cancellationToken = default)
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
                    OperationKind.Handoff,
                    LocalId,
                    TargetId,
                    current,
                    DateTimeOffset.UtcNow),
                OperationStatus.Rejected => OperationReceipt.Rejected(
                    operationId,
                    correlationId,
                    OperationKind.Handoff,
                    LocalId,
                    TargetId,
                    current,
                    DateTimeOffset.UtcNow,
                    Failure),
                OperationStatus.Recovering => OperationReceipt.Recovering(
                    operationId,
                    correlationId,
                    OperationKind.Handoff,
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

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

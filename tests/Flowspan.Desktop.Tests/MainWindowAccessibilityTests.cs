using System.Collections.Immutable;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Desktop.Tests;

public sealed class MainWindowAccessibilityTests
{
    // Avalonia 12.1.0 StartNew can publish a session before its dispatcher Task
    // is assigned, making per-test Dispose intermittently throw. The official
    // assembly cache keeps only the session dispatcher process-scoped; the
    // assembly attribute still rebuilds and disposes the app per Dispatch.
    private static HeadlessUnitTestSession HeadlessSession =>
        HeadlessUnitTestSession.GetOrStartForAssembly(
            typeof(MainWindowAccessibilityTests).Assembly);

    // The headless key helpers flush every pending dispatcher job and render
    // tick before the raw key is delivered. Interaction with virtualized item
    // content must therefore start from an already-stable visual tree, or the
    // pending virtualization work can recycle the focused container between
    // Focus() and key delivery, silently dropping the key on a detached
    // control. Mirrors the flush loop in HeadlessWindowExtensions.
    private static void DrainPendingUiJobs()
    {
        for (var i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public async Task ShellDeclaresTextStatesAndSupportsKeyboardDisclosure()
    {
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup());
        await viewModel.InitializeAsync();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();

            ToggleButton toggle = Assert.IsType<ToggleButton>(
                window.FindControl<ToggleButton>("IdentityDetailsToggle"));
            Button emergencyStop = Assert.IsType<Button>(
                window.FindControl<Button>("EmergencyStopButton"));
            TextBlock sharingStatus = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("SharingStatusText"));

            Assert.Equal(
                "Show identity details",
                toggle.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Emergency stop",
                emergencyStop.GetValue(AutomationProperties.NameProperty));
            Assert.Equal("NOT SHARING", sharingStatus.Text);
            Assert.False(emergencyStop.IsEnabled);
            Assert.True(toggle.Focus());

            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(viewModel.IsIdentityDetailsVisible);
            Assert.Equal("Hide identity details", toggle.Content);
            window!.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SemanticHandoffPreviewAndReceiptAreKeyboardOperableAndNamed()
    {
        var activities = new AccessibilityActivityService();
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            activityService: activities);
        await viewModel.InitializeAsync();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            TextBox title = Assert.IsType<TextBox>(
                window.FindControl<TextBox>("WorkspaceNoteTitleTextBox"));
            TextBox body = Assert.IsType<TextBox>(
                window.FindControl<TextBox>("WorkspaceNoteBodyTextBox"));
            Button create = Assert.IsType<Button>(
                window.FindControl<Button>("CreateWorkspaceNoteButton"));
            ListBox activityList = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("ActivityList"));
            ListBox targetList = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("ActivityTargetList"));
            TextBlock preview = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ActivityPreviewStatusText"));
            TextBlock disclosure = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ActivityDataDisclosureText"));
            TextBlock degradation = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ActivityDegradationStatusText"));
            Button handoff = Assert.IsType<Button>(
                window.FindControl<Button>("ActivityHandoffButton"));
            TextBlock receipt = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ActivityReceiptStatusText"));
            TextBlock sharing = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("SharingStatusText"));

            Assert.Equal(
                "Portable note title",
                title.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Portable note body",
                body.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Create portable note Activity",
                create.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Local semantic Activities",
                activityList.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Authenticated semantic Activity targets",
                targetList.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Confirm semantic handoff copy",
                handoff.GetValue(AutomationProperties.NameProperty));
            Assert.False(handoff.IsEnabled);

            viewModel.Activities.DraftTitle = "Release plan";
            viewModel.Activities.DraftText = "bounded note body";
            Assert.True(create.IsEnabled);
            Assert.True(create.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            viewModel.Activities.SelectedTarget = Assert.Single(
                viewModel.Activities.Targets);

            Assert.Single(activityList.Items);
            Assert.Single(targetList.Items);
            Assert.Equal(
                "SEMANTIC HANDOFF — SOURCE STAYS OPEN",
                preview.Text);
            Assert.Contains("plain-text note", disclosure.Text);
            Assert.Equal(
                "REMOTE WINDOW NOT AVAILABLE IN THIS BUILD",
                degradation.Text);
            Assert.True(handoff.IsEnabled);
            Assert.True(handoff.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.Equal("HANDOFF COMMITTED", receipt.Text);
            Assert.Equal(
                "Semantic Activity operation receipt status",
                receipt.GetValue(AutomationProperties.NameProperty));
            Assert.Equal("NOT SHARING", sharing.Text);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SemanticMovePreviewAndCommitAreKeyboardOperableAndNamed()
    {
        var activities = new AccessibilityActivityService();
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            activityService: activities);
        await viewModel.InitializeAsync();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            TextBox title = Assert.IsType<TextBox>(
                window.FindControl<TextBox>("WorkspaceNoteTitleTextBox"));
            TextBox body = Assert.IsType<TextBox>(
                window.FindControl<TextBox>("WorkspaceNoteBodyTextBox"));
            Button create = Assert.IsType<Button>(
                window.FindControl<Button>("CreateWorkspaceNoteButton"));
            TextBlock preview = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ActivityMovePreviewStatusText"));
            Button move = Assert.IsType<Button>(
                window.FindControl<Button>("ActivityMoveButton"));
            TextBlock receipt = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ActivityReceiptStatusText"));

            Assert.Equal(
                "Semantic move preview status",
                preview.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Confirm semantic move after target acknowledgement",
                move.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Resumes the Activity on the selected authenticated target first, then closes the source only after a verified target acknowledgement.",
                move.GetValue(AutomationProperties.HelpTextProperty));
            Assert.Equal("MOVE PREVIEW NOT READY", preview.Text);
            Assert.False(move.IsEnabled);

            title.Text = "Release plan";
            body.Text = "bounded note body";
            Assert.True(create.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            viewModel.Activities.SelectedTarget = Assert.Single(
                viewModel.Activities.Targets);

            Assert.Equal(
                "SEMANTIC MOVE — SOURCE CLOSES AFTER TARGET ACKNOWLEDGEMENT",
                preview.Text);
            Assert.True(move.IsEnabled);
            Assert.True(move.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.Equal("MOVE COMMITTED", receipt.Text);
            Assert.Equal(
                "Semantic Activity operation receipt status",
                receipt.GetValue(AutomationProperties.NameProperty));
            Assert.Empty(viewModel.Activities.Activities);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConfirmedReplaceIsKeyboardOperableNamedAndKeepsSharingOff()
    {
        var activities = new AccessibilityActivityService();
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            activityService: activities);
        await viewModel.InitializeAsync();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            TextBox title = Assert.IsType<TextBox>(
                window.FindControl<TextBox>("WorkspaceNoteTitleTextBox"));
            TextBox body = Assert.IsType<TextBox>(
                window.FindControl<TextBox>("WorkspaceNoteBodyTextBox"));
            Button create = Assert.IsType<Button>(
                window.FindControl<Button>("CreateWorkspaceNoteButton"));
            Button loadTargets = Assert.IsType<Button>(
                window.FindControl<Button>("ReviewReplaceTargetsButton"));
            ListBox replaceTargets = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("ReplaceTargetList"));
            TextBlock incoming = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ReplaceIncomingDescriptionText"));
            TextBlock replaced = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ReplaceTargetDescriptionText"));
            CheckBox confirmation = Assert.IsType<CheckBox>(
                window.FindControl<CheckBox>("ReplaceConfirmationCheckBox"));
            TextBlock activationStatus = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ReplaceActivationStatusText"));
            Button destructiveReplace = Assert.IsType<Button>(
                window.FindControl<Button>("DestructiveReplaceButton"));
            TextBlock replaceResult = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ReplaceOperationStatusText"));
            TextBlock sharing = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("SharingStatusText"));

            Assert.Equal(
                "Load or refresh Replace target inventory",
                loadTargets.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Replace-eligible Activities on selected target device",
                replaceTargets.GetValue(AutomationProperties.NameProperty));
            Assert.False(loadTargets.IsEnabled);
            Assert.False(destructiveReplace.IsEnabled);

            title.Text = "Incoming note";
            body.Text = "portable body";
            Assert.True(create.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            viewModel.Activities.SelectedTarget = Assert.Single(
                viewModel.Activities.Targets);
            Assert.True(loadTargets.IsEnabled);
            Assert.True(loadTargets.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.Equal(2, replaceTargets.ItemCount);
            Control replaceTargetItem = Assert.IsAssignableFrom<Control>(
                replaceTargets.ContainerFromIndex(0));
            Assert.True(replaceTargetItem.Focus());
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

            Assert.NotNull(viewModel.Activities.SelectedReplaceTarget);
            Assert.Contains("Incoming note", incoming.Text);
            Assert.Contains("Existing target", replaced.Text);
            Assert.Contains("revision 4", replaced.Text);
            Assert.Equal(
                "Confirm replacing Existing target on Peer desk with Incoming note",
                confirmation.GetValue(AutomationProperties.NameProperty));
            Assert.True(confirmation.IsEnabled);
            Assert.True(confirmation.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(viewModel.Activities.HasAcknowledgedReplace);
            Assert.Equal(
                "PREVIEW CONFIRMED — DESTRUCTIVE REPLACE READY",
                activationStatus.Text);
            Assert.True(destructiveReplace.IsEnabled);
            Assert.Equal(
                "Replace selected target Activity after exact confirmation",
                destructiveReplace.GetValue(AutomationProperties.NameProperty));
            Assert.True(destructiveReplace.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.Equal("REPLACE COMMITTED", replaceResult.Text);
            Assert.Equal(
                "Destructive Replace operation status",
                replaceResult.GetValue(AutomationProperties.NameProperty));
            Assert.False(destructiveReplace.IsEnabled);
            Assert.Equal("NOT SHARING", sharing.Text);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReplaceRecoveryRecordsWithoutAnEligibleCapsuleRemainReadOnly()
    {
        var activities = new AccessibilityActivityService
        {
            ReplaceRecoveryResult = await CreateReplaceRecoveryResultAsync(),
        };
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            activityService: activities);
        await viewModel.InitializeAsync();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            TextBlock status = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("ReplaceRecoveryStatusText"));
            ListBox records = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("ReplaceRecoveryList"));
            Button destructiveReplace = Assert.IsType<Button>(
                window.FindControl<Button>("DestructiveReplaceButton"));
            TextBlock sharing = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("SharingStatusText"));

            Assert.Equal(
                "Replace recovery status",
                status.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Target-local Replace and undo recovery records",
                records.GetValue(AutomationProperties.NameProperty));
            string[] automationNames = window.GetVisualDescendants()
                .OfType<Control>()
                .Select(control => control.GetValue(AutomationProperties.NameProperty))
                .Where(static name => !string.IsNullOrEmpty(name))
                .ToArray()!;
            Assert.Contains("Replace recovery guidance", automationNames);
            Assert.Contains("Replace recovery record coverage", automationNames);
            Assert.Contains("Replace recovery snapshot time", automationNames);
            Assert.Contains("Replace recovery record state", automationNames);
            Assert.Contains("Replace recovery Operation ID", automationNames);
            Assert.Contains("Replace recovery correlation ID", automationNames);
            Assert.Contains("Replace recovery capsule ID", automationNames);
            Assert.Contains("Replace recovery undo availability", automationNames);
            Assert.Equal(
                "TARGET-LOCAL REPLACE HISTORY — NO UNDO ACTION",
                status.Text);
            Assert.Equal(2, records.ItemCount);
            // Realize both recovery records before keyboard interaction: the
            // Scene panel enlarged the window content, and the unscrolled
            // headless viewport otherwise leaves record 2 unrealized, so
            // directional selection would have no target container.
            records.ScrollIntoView(1);
            DrainPendingUiJobs();
            records.ScrollIntoView(0);
            DrainPendingUiJobs();
            Control first = Assert.IsAssignableFrom<Control>(
                records.ContainerFromIndex(0));
            Assert.True(first.Focus());
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

            Assert.Equal(1, records.SelectedIndex);
            Assert.False(destructiveReplace.IsEnabled);
            Assert.Equal("NOT SHARING", sharing.Text);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TargetLocalUndoIsNamedConfirmedAndActivatedByKeyboard()
    {
        var activities = new AccessibilityActivityService
        {
            ReplaceRecoveryResult = await CreateUndoableReplaceRecoveryResultAsync(),
            UndoResult = UndoReplaceResult.Committed(
                OperationContext.Create(
                    OperationId.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                    CorrelationId.Parse("99999999-9999-9999-9999-999999999999"),
                    new DateTimeOffset(2026, 7, 15, 12, 6, 0, TimeSpan.Zero)),
                UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                new DateTimeOffset(2026, 7, 15, 12, 5, 1, TimeSpan.Zero)),
        };
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            activityService: activities);
        await viewModel.InitializeAsync();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? window = null;

        await session.Dispatch(() =>
        {
            window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            ListBox records = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("ReplaceRecoveryList"));
            CheckBox confirmation = Assert.IsType<CheckBox>(
                window.FindControl<CheckBox>("TargetLocalUndoConfirmationCheckBox"));
            Button undo = Assert.IsType<Button>(
                window.FindControl<Button>("TargetLocalUndoButton"));
            TextBlock status = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("TargetLocalUndoStatusText"));

            Assert.Equal(
                "Target-local undo status",
                status.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Activate the confirmed target-local undo",
                undo.GetValue(AutomationProperties.NameProperty));
            // Realize both recovery records before keyboard interaction; see
            // ReplaceRecoveryRecordsWithoutAnEligibleCapsuleRemainReadOnly.
            records.ScrollIntoView(1);
            DrainPendingUiJobs();
            records.ScrollIntoView(0);
            DrainPendingUiJobs();
            Control first = Assert.IsAssignableFrom<Control>(
                records.ContainerFromIndex(0));
            Assert.True(first.Focus());
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            Assert.Equal(1, records.SelectedIndex);
            Assert.True(confirmation.IsEnabled);
            Assert.True(confirmation.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(viewModel.Activities.HasAcknowledgedTargetLocalUndo);
            Assert.True(undo.IsEnabled);
            Assert.True(undo.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        }, CancellationToken.None);

        await activities.UndoRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.Dispatch(() =>
        {
            MainWindow shownWindow = window
                ?? throw new InvalidOperationException("The undo test window was not shown.");
            Assert.Equal(
                "TARGET-LOCAL UNDO COMMITTED",
                viewModel.Activities.TargetLocalUndoStatus);
            TextBlock sharing = Assert.IsType<TextBlock>(
                shownWindow.FindControl<TextBlock>("SharingStatusText"));
            Assert.Equal("NOT SHARING", sharing.Text);
            shownWindow.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SceneApplyAndCompensationAreKeyboardOperableNamedAndTruthful()
    {
        var scenes = new AccessibilitySceneService();
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            sceneApplyService: scenes);
        await viewModel.InitializeAsync();
        viewModel.Scenes.SelectScene(scenes.Scene, currentGroupRevision: 2);
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? window = null;
        var workflowCompleted = false;

        try
        {
            // The default per-test isolation rebuilds the Avalonia application
            // (locator, KeyboardDevice, FocusManager) for every Dispatch call, so
            // a window created in an earlier Dispatch can never receive keys sent
            // in a later one: its impl captured the old KeyboardDevice while
            // Focus() targets the new. The whole keyboard workflow therefore runs
            // inside one async Dispatch, which keeps a single application alive
            // across awaits by pumping a dispatcher frame.
            await session.Dispatch<int>(async () =>
            {
                window = new MainWindow { DataContext = viewModel };
                MainWindow shownWindow = window;
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show();
                Button preview = Assert.IsType<Button>(
                    window.FindControl<Button>("ScenePreviewButton"));
                TextBlock previewStatus = Assert.IsType<TextBlock>(
                    window.FindControl<TextBlock>("ScenePreviewStatusText"));
                TextBlock expiry = Assert.IsType<TextBlock>(
                    window.FindControl<TextBlock>("ScenePreviewExpiryText"));
                TextBlock staleGroup = Assert.IsType<TextBlock>(
                    window.FindControl<TextBlock>("SceneStaleGroupWarningText"));
                ListBox previewItems = Assert.IsType<ListBox>(
                    window.FindControl<ListBox>("ScenePreviewList"));
                CheckBox applyConfirmation = Assert.IsType<CheckBox>(
                    window.FindControl<CheckBox>("SceneApplyConfirmationCheckBox"));
                Button apply = Assert.IsType<Button>(
                    window.FindControl<Button>("SceneApplyButton"));

                Assert.Equal(
                    "Preview selected Scene without mutation",
                    preview.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Scene preview status",
                    previewStatus.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Scene preview expiry state",
                    expiry.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Scene stale Group warning",
                    staleGroup.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Ordered Scene actions and blockers",
                    previewItems.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Confirm this exact expiring Scene preview",
                    applyConfirmation.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Apply the exact confirmed Scene preview",
                    apply.GetValue(AutomationProperties.NameProperty));
                Assert.True(preview.IsEnabled);
                Assert.False(apply.IsEnabled);
                Assert.True(preview.Focus());

                window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

                await scenes.PreviewRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
                DrainPendingUiJobs();

                Assert.Equal(2, previewItems.ItemCount);
                Assert.Equal(
                    "PREVIEW READY — BLOCKERS PRESENT",
                    viewModel.Scenes.PreviewStatus);
                Assert.Contains(
                    "saved revision 1",
                    viewModel.Scenes.StaleGroupWarning);

                previewItems.ScrollIntoView(1);
                DrainPendingUiJobs();
                Control replaceContainer = Assert.IsAssignableFrom<Control>(
                    previewItems.ContainerFromIndex(1));
                CheckBox replaceConfirmation = replaceContainer
                    .GetVisualDescendants()
                    .OfType<CheckBox>()
                    .Single(control => control
                        .GetValue(AutomationProperties.NameProperty)?
                        .StartsWith("Confirm Scene item", StringComparison.Ordinal)
                        is true);
                replaceConfirmation.BringIntoView();
                DrainPendingUiJobs();

                Assert.StartsWith(
                    "Confirm Scene item 2 replacement of Activity",
                    replaceConfirmation.GetValue(AutomationProperties.NameProperty));
                Assert.True(replaceConfirmation.Focus());
                Assert.True(replaceConfirmation.IsFocused);
                shownWindow.KeyPressQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);
                shownWindow.KeyReleaseQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);
                Assert.True(replaceConfirmation.IsChecked);
                Assert.True(viewModel.Scenes.PreviewItems[1].IsReplaceConfirmed);
                Assert.False(apply.IsEnabled);

                Assert.True(applyConfirmation.Focus());
                shownWindow.KeyPressQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);
                shownWindow.KeyReleaseQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);
                Assert.True(viewModel.Scenes.HasAcknowledgedApply);
                Assert.True(apply.IsEnabled);
                Assert.True(apply.Focus());
                shownWindow.KeyReleaseQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);

                await scenes.ApplyRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
                DrainPendingUiJobs();
                TextBlock resultStatus = Assert.IsType<TextBlock>(
                shownWindow.FindControl<TextBlock>("SceneApplyResultStatusText"));
                ListBox resultItems = Assert.IsType<ListBox>(
                    shownWindow.FindControl<ListBox>("SceneApplyResultList"));
                CheckBox compensationConfirmation = Assert.IsType<CheckBox>(
                    shownWindow.FindControl<CheckBox>(
                        "SceneCompensationConfirmationCheckBox"));
                Button compensate = Assert.IsType<Button>(
                    shownWindow.FindControl<Button>("SceneCompensateButton"));

                Assert.Equal(
                    "Scene Apply overall result",
                    resultStatus.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Ordered Scene Apply item outcomes",
                    resultItems.GetValue(AutomationProperties.NameProperty));
                Assert.Equal("SCENE PARTIALLY COMPLETED", resultStatus.Text);
                Assert.Equal(2, resultItems.ItemCount);
                Assert.Equal(
                    "Confirm explicit reverse-order Scene Replace compensation",
                    compensationConfirmation.GetValue(
                        AutomationProperties.NameProperty));
                Assert.Equal(
                    "Attempt explicit safe Scene compensation",
                    compensate.GetValue(AutomationProperties.NameProperty));
                Assert.False(compensate.IsEnabled);
                Assert.True(compensationConfirmation.Focus());
                shownWindow.KeyPressQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);
                shownWindow.KeyReleaseQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);
                Assert.True(compensate.IsEnabled);
                Assert.True(compensate.Focus());
                shownWindow.KeyReleaseQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);

                await scenes.CompensationRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
                DrainPendingUiJobs();
                TextBlock compensationStatus = Assert.IsType<TextBlock>(
                shownWindow.FindControl<TextBlock>("SceneCompensationStatusText"));
                ListBox compensationItems = Assert.IsType<ListBox>(
                    shownWindow.FindControl<ListBox>("SceneCompensationResultList"));
                TextBlock sharing = Assert.IsType<TextBlock>(
                    shownWindow.FindControl<TextBlock>("SharingStatusText"));

                Assert.Equal(
                    "Scene compensation overall status",
                    compensationStatus.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Reverse-order Scene compensation item outcomes",
                    compensationItems.GetValue(AutomationProperties.NameProperty));
                Assert.Equal("COMPENSATION COMPLETED", compensationStatus.Text);
                Assert.Single(compensationItems.Items);
                Assert.Equal("NOT SHARING", sharing.Text);
                return 0;
            }, CancellationToken.None);
            workflowCompleted = true;
        }
        finally
        {
            try
            {
                await session.Dispatch(() => window?.Close(), CancellationToken.None);
                if (window is not null)
                {
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch when (!workflowCompleted)
            {
                // Keep the original workflow failure as the reported error
                // instead of masking it with a cleanup exception.
            }
        }
    }

    [Fact]
    public async Task SceneRepositoryLifecycleIsKeyboardOperableNamedAndRedacted()
    {
        var repository = new AccessibilitySceneRepositoryService();
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            sceneRepositoryService: repository);
        await viewModel.InitializeAsync();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        MainWindow? window = null;
        var workflowCompleted = false;

        try
        {
            // One async Dispatch keeps a single Avalonia application alive for
            // the whole keyboard workflow; see
            // SceneApplyAndCompensationAreKeyboardOperableNamedAndTruthful.
            await session.Dispatch<int>(async () =>
            {
                window = new MainWindow { DataContext = viewModel };
                MainWindow shownWindow = window;
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show();
                TextBlock status = Assert.IsType<TextBlock>(
                    window.FindControl<TextBlock>("SceneRepositoryStatusText"));
                ListBox list = Assert.IsType<ListBox>(
                    window.FindControl<ListBox>("SceneRepositoryList"));
                Button select = Assert.IsType<Button>(
                    window.FindControl<Button>("SceneRepositorySelectButton"));
                Button beginDelete = Assert.IsType<Button>(
                    window.FindControl<Button>(
                        "SceneRepositoryBeginDeleteButton"));
                Button confirmDelete = Assert.IsType<Button>(
                    window.FindControl<Button>(
                        "SceneRepositoryConfirmDeleteButton"));
                Button export = Assert.IsType<Button>(
                    window.FindControl<Button>("SceneRepositoryExportButton"));

                Assert.Equal(
                    "Scene repository status",
                    status.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Stored Scenes",
                    list.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Select the stored Scene for apply preview",
                    select.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Review stored Scene deletion",
                    beginDelete.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Delete the stored Scene permanently",
                    confirmDelete.GetValue(AutomationProperties.NameProperty));
                Assert.Equal(
                    "Export a redacted record of the selected stored Scene",
                    export.GetValue(AutomationProperties.NameProperty));
                Assert.Equal("2 SCENES STORED", status.Text);
                Assert.Equal(2, list.ItemCount);
                Assert.False(select.IsEnabled);
                Assert.False(beginDelete.IsEnabled);
                Assert.False(export.IsEnabled);

                // Realize both stored Scenes before keyboard interaction; see
                // ReplaceRecoveryRecordsWithoutAnEligibleCapsuleRemainReadOnly.
                list.ScrollIntoView(1);
                DrainPendingUiJobs();
                list.ScrollIntoView(0);
                DrainPendingUiJobs();
                Control first = Assert.IsAssignableFrom<Control>(
                    list.ContainerFromIndex(0));
                Assert.True(first.Focus());
                shownWindow.KeyPressQwerty(
                    PhysicalKey.ArrowDown,
                    RawInputModifiers.None);
                shownWindow.KeyReleaseQwerty(
                    PhysicalKey.ArrowDown,
                    RawInputModifiers.None);
                Assert.Equal(1, list.SelectedIndex);
                SceneRepositoryItemViewModel selected = Assert.IsType<
                    SceneRepositoryItemViewModel>(
                    viewModel.SceneRepository.SelectedScene);
                Assert.Equal("Reading desk", selected.Name);
                DesktopSceneRepositoryPlanItem inspectItem = Assert.Single(
                    viewModel.SceneRepository.InspectItems);
                Assert.Equal("ITEM 1", inspectItem.ItemLabel);

                Assert.True(select.IsEnabled);
                Assert.True(select.Focus());
                shownWindow.KeyReleaseQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);
                Assert.Equal("Reading desk", viewModel.Scenes.SceneName);
                Assert.Equal(
                    "SCENE SELECTED FOR APPLY",
                    viewModel.SceneRepository.LifecycleStatus);

                Assert.True(export.IsEnabled);
                Assert.True(export.Focus());
                shownWindow.KeyReleaseQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);
                await repository.ExportRequested.Task
                    .WaitAsync(TimeSpan.FromSeconds(5));
                DrainPendingUiJobs();
                Assert.Equal(
                    "EXPORT WRITTEN",
                    viewModel.SceneRepository.LifecycleStatus);
                Assert.Contains(
                    SceneRepositoryExport.ExportKind,
                    viewModel.SceneRepository.ExportPreview,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "Reading desk",
                    viewModel.SceneRepository.ExportPreview,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "reading-slot",
                    viewModel.SceneRepository.ExportPreview,
                    StringComparison.Ordinal);

                Assert.True(beginDelete.IsEnabled);
                Assert.False(
                    viewModel.SceneRepository.IsDeleteConfirmationVisible);
                Assert.True(beginDelete.Focus());
                shownWindow.KeyReleaseQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);
                DrainPendingUiJobs();
                Assert.True(
                    viewModel.SceneRepository.IsDeleteConfirmationVisible);
                TextBlock confirmation = Assert.IsType<TextBlock>(
                    shownWindow.FindControl<TextBlock>(
                        "SceneRepositoryDeleteConfirmationText"));
                Assert.Contains(
                    "Reading desk",
                    confirmation.Text,
                    StringComparison.Ordinal);
                Assert.EndsWith(
                    "This action has no undo.",
                    confirmation.Text);
                Assert.True(confirmDelete.IsEnabled);
                Assert.True(confirmDelete.Focus());
                shownWindow.KeyReleaseQwerty(
                    PhysicalKey.Space,
                    RawInputModifiers.None);
                await repository.DeleteRequested.Task
                    .WaitAsync(TimeSpan.FromSeconds(5));
                DrainPendingUiJobs();
                Assert.Equal(
                    "SCENE DELETED",
                    viewModel.SceneRepository.LifecycleStatus);
                Assert.Equal(1, list.ItemCount);
                Assert.Equal(
                    "1 SCENE STORED",
                    viewModel.SceneRepository.RepositoryStatus);
                Assert.False(
                    viewModel.SceneRepository.IsDeleteConfirmationVisible);

                TextBlock sharing = Assert.IsType<TextBlock>(
                    shownWindow.FindControl<TextBlock>("SharingStatusText"));
                Assert.Equal("NOT SHARING", sharing.Text);
                return 0;
            }, CancellationToken.None);
            workflowCompleted = true;
        }
        finally
        {
            try
            {
                await session.Dispatch(() => window?.Close(), CancellationToken.None);
                if (window is not null)
                {
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            catch when (!workflowCompleted)
            {
                // Keep the original workflow failure as the reported error
                // instead of masking it with a cleanup exception.
            }
        }
    }

    [Fact]
    public async Task PairingConfirmationRequiresKeyboardCodeAcknowledgement()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        using var source = new DesktopPairingDecisionSource();
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            source,
            InlineDesktopUiDispatcher.Instance);
        await viewModel.InitializeAsync();
        Task<PairingDecision> pending = source.DecideAsync(
            new PairingConfirmationRequest(
                peer.PublicIdentity,
                new ProtocolVersion(1, 0),
                "123456",
                DateTimeOffset.UtcNow.AddMinutes(1))).AsTask();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            CheckBox confirmation = Assert.IsType<CheckBox>(
                window.FindControl<CheckBox>("PairingCodeConfirmation"));
            Button accept = Assert.IsType<Button>(
                window.FindControl<Button>("AcceptPairingButton"));
            Button reject = Assert.IsType<Button>(
                window.FindControl<Button>("RejectPairingButton"));
            TextBlock code = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("PairingCodeValue"));

            Assert.Equal("123 456", code.Text);
            Assert.Equal(
                "I compared the pairing code on both devices",
                confirmation.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Confirm pairing",
                accept.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Reject pairing",
                reject.GetValue(AutomationProperties.NameProperty));
            Assert.False(accept.IsEnabled);
            Assert.True(confirmation.Focus());

            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(accept.IsEnabled);
            Assert.True(accept.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        PairingDecision decision = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(decision.Accepted);
        Assert.Empty(decision.CapabilitiesGrantedToPeer.Capabilities);
    }

    [Fact]
    public async Task TrustedDeviceCapabilitiesAndRevokeAreKeyboardOperable()
    {
        using DeviceIdentity peer = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Peer desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            peer.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None));
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            trustAuthority: new DesktopTrustAuthority(trustStore));
        await viewModel.InitializeAsync();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            ListBox devices = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("TrustedDeviceList"));
            CheckBox mirrorView = Assert.IsType<CheckBox>(
                window.FindControl<CheckBox>("GrantMirrorViewCheckBox"));
            CheckBox activitySwap = Assert.IsType<CheckBox>(
                window.FindControl<CheckBox>("GrantActivitySwapCheckBox"));
            Button save = Assert.IsType<Button>(
                window.FindControl<Button>("SaveCapabilitiesButton"));
            Button reviewRevoke = Assert.IsType<Button>(
                window.FindControl<Button>("ReviewRevokeButton"));
            Button confirmRevoke = Assert.IsType<Button>(
                window.FindControl<Button>("ConfirmRevokeButton"));
            TextBlock mutationStatus = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("TrustMutationStatusText"));

            Assert.Single(devices.Items);
            Assert.Equal(
                "Allow peer to view mirrored Activities",
                mirrorView.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Allow peer to participate in atomic Activity swaps",
                activitySwap.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Save peer capabilities",
                save.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Review device revocation",
                reviewRevoke.GetValue(AutomationProperties.NameProperty));
            Assert.True(mirrorView.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(save.IsEnabled);
            Assert.True(save.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(trustStore.Allows(peer.DeviceId, Capability.MirrorView));

            Assert.True(reviewRevoke.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.True(confirmRevoke.IsVisible);
            Assert.True(trustStore.TryGet(peer.DeviceId, out _));
            Assert.True(confirmRevoke.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Assert.False(trustStore.TryGet(peer.DeviceId, out _));
            Assert.True(mutationStatus.IsVisible);
            Assert.Equal("DEVICE REVOKED", mutationStatus.Text);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TrustedDeviceSelectionUpdatesCapabilityDraftByKeyboard()
    {
        using DeviceIdentity first = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "First desk");
        using DeviceIdentity second = DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            "Second desk");
        var trustStore = new InMemoryTrustStore();
        trustStore.Register(new TrustRecord(
            first.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.ActivityOffer)));
        trustStore.Register(new TrustRecord(
            second.PublicIdentity,
            DateTimeOffset.UnixEpoch.AddMinutes(1),
            CapabilityGrant.Of(Capability.MirrorDrive)));
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            trustAuthority: new DesktopTrustAuthority(trustStore));
        await viewModel.InitializeAsync();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            ListBox devices = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("TrustedDeviceList"));

            Assert.Equal("First desk", viewModel.TrustedDevices.SelectedDevice?.DisplayName);
            Assert.True(viewModel.TrustedDevices.GrantActivityOffer);
            Assert.False(viewModel.TrustedDevices.GrantMirrorDrive);
            Control firstItem = Assert.IsAssignableFrom<Control>(
                devices.ContainerFromIndex(0));
            Assert.True(firstItem.Focus());
            window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

            Assert.Equal("Second desk", viewModel.TrustedDevices.SelectedDevice?.DisplayName);
            Assert.False(viewModel.TrustedDevices.GrantActivityOffer);
            Assert.True(viewModel.TrustedDevices.GrantMirrorDrive);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task LocalPairingEnableAndCandidateActionsAreKeyboardReachable()
    {
        var runtime = new DesktopLocalPairingRuntime(
            new AccessibilityNetworkFactory());
        await using var viewModel = new WorkspaceShellViewModel(
            new ReadyStartup(),
            trustAuthority: new DesktopTrustAuthority(new InMemoryTrustStore()),
            localPairingRuntime: runtime,
            localNetworkPermissionGuide:
                DesktopLocalNetworkPermissionGuide.ForPlatform(
                    DesktopPlatformFamily.MacOS));
        await viewModel.InitializeAsync();
        HeadlessUnitTestSession session = HeadlessSession;
        var closed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await session.Dispatch(() =>
        {
            var window = new MainWindow { DataContext = viewModel };
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            Button review = Assert.IsType<Button>(
                window.FindControl<Button>("ReviewLocalNetworkAccessButton"));
            Button enable = Assert.IsType<Button>(
                window.FindControl<Button>("EnableLocalPairingButton"));
            CheckBox acknowledgement = Assert.IsType<CheckBox>(
                window.FindControl<CheckBox>("LocalNetworkPermissionAcknowledgement"));
            TextBlock dataExposure = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("LocalNetworkDataExposureText"));
            TextBlock promptExpectation = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("LocalNetworkPromptExpectationText"));
            TextBlock revocation = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("LocalNetworkRevocationText"));
            ListBox candidates = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("LocalPairingCandidateList"));
            ListBox trustedConnections = Assert.IsType<ListBox>(
                window.FindControl<ListBox>("TrustedPeerConnectionList"));
            TextBlock identityWarning = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("TrustedPeerIdentityWarningSummary"));
            Button pair = Assert.IsType<Button>(
                window.FindControl<Button>("PairDiscoveredDeviceButton"));
            Button cancel = Assert.IsType<Button>(
                window.FindControl<Button>("CancelOutboundPairingButton"));
            TextBlock sharing = Assert.IsType<TextBlock>(
                window.FindControl<TextBlock>("SharingStatusText"));

            Assert.Equal(
                "Review local network access",
                review.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Enable local network after permission review",
                enable.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Discovered local pairing candidates",
                candidates.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Trusted peer local connection status",
                trustedConnections.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Pair selected local device",
                pair.GetValue(AutomationProperties.NameProperty));
            Assert.Equal(
                "Cancel outbound pairing",
                cancel.GetValue(AutomationProperties.NameProperty));
            Assert.True(review.IsEnabled);
            Assert.True(review.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.False(viewModel.LocalPairing.IsEnabled);
            Assert.True(viewModel.LocalPairing.IsPermissionReviewVisible);
            Assert.Contains("Activity content", dataExposure.Text);
            Assert.Contains("Local Network", promptExpectation.Text);
            Assert.Contains("System Settings", revocation.Text);
            Assert.Equal(
                "Acknowledge local network exposure",
                acknowledgement.GetValue(AutomationProperties.NameProperty));
            Assert.True(acknowledgement.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(viewModel.LocalPairing.HasAcknowledgedPermissionReview);
            Assert.True(enable.IsEnabled);
            Assert.True(enable.Focus());
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);

            Assert.True(viewModel.LocalPairing.IsEnabled);
            Assert.Single(trustedConnections.Items);
            Assert.True(identityWarning.IsVisible);
            Assert.Contains("IDENTITY CLAIM BLOCKED", identityWarning.Text);
            Assert.Equal(
                "Trusted peer identity warning",
                identityWarning.GetValue(AutomationProperties.NameProperty));
            Assert.Equal("NOT SHARING", sharing.Text);
            window.Close();
        }, CancellationToken.None);
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class ReadyStartup : IDesktopIdentityStartup
    {
        public ValueTask<LocalIdentitySnapshot> InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new LocalIdentitySnapshot(
                "Desk",
                "11111111-1111-1111-1111-111111111111",
                new string('A', 64),
                "Operating-system protected",
                false));
        }

        public void Dispose()
        {
        }
    }

    private static async Task<DesktopReplaceRecoveryResult>
        CreateReplaceRecoveryResultAsync()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        using PersistentReplaceStateStore state =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        ActivityDescriptor descriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Must stay private",
            "{\"text\":\"must stay private\"}");
        for (int index = 1; index <= 2; index++)
        {
            OperationId operationId = OperationId.Parse(
                $"bbbbbbbb-bbbb-bbbb-bbbb-{index:X12}");
            CorrelationId correlationId = CorrelationId.Parse(
                $"cccccccc-cccc-cccc-cccc-{index:X12}");
            await state.ExecuteOnceAsync(
                operationId,
                index.ToString("X64", System.Globalization.CultureInfo.InvariantCulture),
                _ => ValueTask.FromResult(OperationReceipt.Rejected(
                    operationId,
                    correlationId,
                    OperationKind.Replace,
                    DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
                    DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                    descriptor,
                    new DateTimeOffset(2026, 7, 15, 12, index, 0, TimeSpan.Zero),
                    FailureCode.CapabilityDenied)),
                CancellationToken.None);
        }

        return DesktopReplaceRecoveryResult.Available(
            state.GetRecoverySnapshot(
                new DateTimeOffset(2026, 7, 15, 12, 5, 0, TimeSpan.Zero)));
    }

    private static async Task<DesktopReplaceRecoveryResult>
        CreateUndoableReplaceRecoveryResultAsync()
    {
        var payloadStore = new MemoryReplaceStatePayloadStore();
        using PersistentReplaceStateStore state =
            await PersistentReplaceStateStore.OpenAsync(payloadStore);
        DeviceId source =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        DeviceId target =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");
        ActivityInstance original = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ActivityKind.Parse("workspace.note/v1"),
                target,
                "Private original",
                "{\"text\":\"private original\"}"),
            ActivityPlacement.On(target, "desktop"),
            revision: 4);
        ActivityInstance replacement = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                ActivityKind.Parse("workspace.note/v1"),
                source,
                "Private replacement",
                "{\"text\":\"private replacement\"}"),
            ActivityPlacement.On(target, "desktop"),
            revision: 5);
        UndoCapsule capsule = UndoCapsule.Create(
            UndoCapsuleId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            OperationContext.Create(
                OperationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                new DateTimeOffset(2026, 7, 15, 12, 0, 30, TimeSpan.Zero)),
            source,
            target,
            original,
            replacement,
            new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 15, 12, 10, 0, TimeSpan.Zero));
        Assert.True(await state.TryAddAsync(capsule));
        await state.ExecuteOnceAsync(
            capsule.OperationId,
            new string('A', 64),
            _ => ValueTask.FromResult(OperationReceipt.Committed(
                capsule.OperationId,
                capsule.CorrelationId,
                OperationKind.Replace,
                source,
                target,
                replacement.Descriptor,
                new DateTimeOffset(2026, 7, 15, 12, 0, 1, TimeSpan.Zero))),
            CancellationToken.None);
        OperationId rejectedOperationId =
            OperationId.Parse("01010101-0101-0101-0101-010101010101");
        await state.ExecuteOnceAsync(
            rejectedOperationId,
            new string('B', 64),
            _ => ValueTask.FromResult(OperationReceipt.Rejected(
                rejectedOperationId,
                CorrelationId.Parse("02020202-0202-0202-0202-020202020202"),
                OperationKind.Replace,
                source,
                target,
                replacement.Descriptor,
                new DateTimeOffset(2026, 7, 15, 12, 4, 0, TimeSpan.Zero),
                FailureCode.CapabilityDenied)),
            CancellationToken.None);
        return DesktopReplaceRecoveryResult.Available(
            state.GetRecoverySnapshot(
                new DateTimeOffset(2026, 7, 15, 12, 5, 0, TimeSpan.Zero)),
            [capsule.Id]);
    }

    private sealed class AccessibilitySceneService : IDesktopSceneApplyService
    {
        private static readonly DeviceId SourceDevice =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly DeviceId TargetDevice =
            DeviceId.Parse("33333333-3333-3333-3333-333333333333");
        private static readonly ActivityKind Kind =
            ActivityKind.Parse("workspace.note/v1");
        private readonly SceneCompensationResult compensation;
        private readonly SceneApplyPreview preview;
        private readonly SceneApplyResult result;

        public AccessibilitySceneService()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ActivityId blockedActivity = ActivityId.Parse(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            ActivityId incomingActivity = ActivityId.Parse(
                "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            ActivityGroup group = ActivityGroup.Create(
                GroupId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                "Accessibility group",
                [blockedActivity, incomingActivity]);
            SceneActivityPlan blockedPlan = SceneActivityPlan.Place(
                blockedActivity,
                ActivityPlacement.On(TargetDevice, "protected-slot"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.ReplaceWithUndo);
            SceneActivityPlan replacePlan = SceneActivityPlan.Place(
                incomingActivity,
                ActivityPlacement.On(TargetDevice, "focus"),
                SceneSourceDisposition.PreserveSource,
                SceneConflictPolicy.ReplaceWithUndo);
            Scene = ScenePlan.CreateFromGroup(
                SceneId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                "Keyboard Scene",
                group,
                [blockedPlan, replacePlan]);
            SceneSourceSelection blockedSource = CreateSource(
                index: 0,
                blockedActivity,
                "blocked-source");
            SceneSourceSelection replaceSource = CreateSource(
                index: 1,
                incomingActivity,
                "replace-source");
            SceneReplaceTargetSnapshot replaceTarget =
                SceneReplaceTargetSnapshot.Create(
                    ActivityId.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    revision: 9,
                    descriptorDigest: new string('E', 64),
                    Kind,
                    replacePlan.Placement);
            SceneApplyItemPreview blocked =
                SceneApplyItemPreview.BlockedByOccupancy(
                    blockedPlan,
                    blockedSource,
                    SceneSlotOccupancy.Opaque,
                    OperationId.Parse(
                        "10101010-1010-1010-1010-101010101010"),
                    CorrelationId.Parse(
                        "11111111-1111-1111-1111-111111111111"));
            SceneApplyItemPreview replace = SceneApplyItemPreview.Replace(
                replacePlan,
                replaceSource,
                replaceTarget,
                OperationId.Parse("12121212-1212-1212-1212-121212121212"),
                CorrelationId.Parse("13131313-1313-1313-1313-131313131313"));
            preview = SceneApplyPreview.Create(
                Scene,
                OperationId.Parse("14141414-1414-1414-1414-141414141414"),
                CorrelationId.Parse("15151515-1515-1515-1515-151515151515"),
                now,
                now.AddMinutes(5),
                [blocked, replace],
                observedGroupRevision: 2);
            var capsule = new UndoCapsuleReference(
                UndoCapsuleId.Parse("16161616-1616-1616-1616-161616161616"),
                replace.ChildOperationId,
                replace.ChildCorrelationId,
                TargetDevice,
                replaceTarget.ActivityId,
                replaceTarget.Revision,
                replaceTarget.DescriptorDigest,
                incomingActivity,
                replaceSource.DescriptorDigest,
                now.AddMinutes(10));
            SceneApplyItemResult blockedResult =
                SceneApplyItemResult.FromPreviewOnly(blocked, now.AddSeconds(2));
            SceneApplyItemResult replaceResult = SceneApplyItemResult.FromOperation(
                replace,
                OperationReceipt.FromRecordedResult(
                    replace.ChildOperationId,
                    replace.ChildCorrelationId,
                    OperationKind.Replace,
                    OperationStatus.Committed,
                    SourceDevice,
                    TargetDevice,
                    incomingActivity,
                    Kind,
                    replaceSource.DescriptorDigest,
                    now.AddSeconds(2),
                    FailureCode.None),
                capsule);
            result = SceneApplyResult.Create(
                preview,
                now.AddSeconds(1),
                now.AddSeconds(2),
                [blockedResult, replaceResult]);
            OperationContext undoContext = SceneApplyCompensator.CreateStableContext(
                result,
                replaceResult,
                capsule);
            compensation = SceneCompensationResult.Create(
                result.ParentOperationId,
                [
                    SceneCompensationItemResult.FromUndo(
                        replace.Index,
                        TargetDevice,
                        UndoReplaceResult.Committed(
                            undoContext,
                            capsule.Id,
                            now.AddSeconds(3))),
                ]);
        }

        public bool IsSceneApplyReady => true;

        public ScenePlan Scene { get; }

        public TaskCompletionSource PreviewRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ApplyRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource CompensationRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<SceneApplyPreview> PreviewSceneAsync(
            ScenePlan scene,
            IEnumerable<SceneSourceSelection> selectedSources,
            long? observedGroupRevision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreviewRequested.TrySetResult();
            return ValueTask.FromResult(preview);
        }

        public ValueTask<SceneApplyExecutionResult> ApplySceneAsync(
            ScenePlan scene,
            SceneApplyPreview requestedPreview,
            SceneApplyApproval approval,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(preview.Fingerprint, approval.PreviewFingerprint);
            // ImmutableArray<T> equality compares backing-array references; the
            // approval carries equal confirmation records in Scene order inside
            // its own array, so the binding must be compared by value.
            Assert.True(preview.RequiredReplaceConfirmations
                .SequenceEqual(approval.ReplaceConfirmations));
            ApplyRequested.TrySetResult();
            return ValueTask.FromResult(SceneApplyExecutionResult.Accepted(result));
        }

        public ValueTask<SceneCompensationResult> CompensateSceneAsync(
            SceneApplyResult applyResult,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CompensationRequested.TrySetResult();
            return ValueTask.FromResult(compensation);
        }

        private static SceneSourceSelection CreateSource(
            int index,
            ActivityId activityId,
            string slot) => SceneSourceSelection.Create(
            index,
            activityId,
            revision: 7,
            descriptorDigest: new string((char)('A' + index), 64),
            Kind,
            ActivityPlacement.On(SourceDevice, slot));
    }

    private sealed class AccessibilitySceneRepositoryService :
        IDesktopSceneRepositoryService
    {
        private static readonly DateTimeOffset StoredAt =
            new(2026, 7, 26, 9, 0, 0, TimeSpan.Zero);
        private readonly List<SceneRepositoryEntry> entries = [];

        public AccessibilitySceneRepositoryService()
        {
            entries.Add(CreateEntry(
                "aaaaaaa1-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "Focus desk",
                "focus-slot"));
            entries.Add(CreateEntry(
                "bbbbbbb2-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                "Reading desk",
                "reading-slot"));
        }

        public bool IsSceneRepositoryReady => true;

        public TaskCompletionSource DeleteRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ExportRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask InitializeAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<ImmutableArray<SceneRepositoryEntry>> ListScenesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(entries.ToImmutableArray());
        }

        public ValueTask<SceneRepositoryEntry> SaveSceneAsync(
            ScenePlan scene,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The accessibility workflow does not save Scenes.");

        public ValueTask<bool> DeleteSceneAsync(
            SceneId sceneId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool removed = entries.RemoveAll(
                entry => entry.Scene.Id == sceneId) > 0;
            DeleteRequested.TrySetResult();
            return ValueTask.FromResult(removed);
        }

        public ValueTask<DesktopSceneExportResult?> ExportSceneAsync(
            SceneId sceneId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SceneRepositoryEntry? entry = entries.FirstOrDefault(
                candidate => candidate.Scene.Id == sceneId);
            ExportRequested.TrySetResult();
            return ValueTask.FromResult(entry is null
                ? null
                : new DesktopSceneExportResult(
                    $"/exports/scene-export-{sceneId}.json",
                    Encoding.UTF8.GetString(
                        SceneRepositoryExport.EncodeRedacted(
                            entry,
                            StoredAt.AddHours(1)))));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static SceneRepositoryEntry CreateEntry(
            string sceneId,
            string name,
            string slot) => SceneRepositoryEntry.Create(
            ScenePlan.Create(
                SceneId.Parse(sceneId),
                name,
                [
                    SceneActivityPlan.Place(
                        ActivityId.Parse(
                            "cccccccc-cccc-cccc-cccc-cccccccccccc"),
                        ActivityPlacement.On(
                            DeviceId.Parse(
                                "99999999-9999-9999-9999-999999999999"),
                            slot),
                        SceneSourceDisposition.PreserveSource,
                        SceneConflictPolicy.RequireEmpty),
                ]),
            StoredAt);
    }

    private sealed class AccessibilityActivityService : IDesktopActivityService
    {
        private static readonly DeviceId LocalId =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");

        private static readonly DeviceId TargetId =
            DeviceId.Parse("22222222-2222-2222-2222-222222222222");

        private ActivityDescriptor? descriptor;

        public event Action? Changed;

        public bool IsDestructiveReplaceAvailable => true;

        public DesktopReplaceRecoveryResult ReplaceRecoveryResult { get; init; } =
            DesktopReplaceRecoveryResult.Unavailable;

        public UndoReplaceResult? UndoResult { get; init; }

        public TaskCompletionSource<UndoCapsuleId> UndoRequested { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

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
                System.Text.Json.JsonSerializer.Serialize(new { text }),
                sensitivity);
            Changed?.Invoke();
            return CreateSnapshot();
        }

        public ImmutableArray<DesktopActivitySnapshot> GetActivities() =>
            descriptor is null ? [] : [CreateSnapshot()];

        public ImmutableArray<DesktopActivityTargetSnapshot> GetTargets() =>
            [new DesktopActivityTargetSnapshot(TargetId, "Peer desk")];

        public DesktopReplaceRecoveryResult GetReplaceRecoveryState() =>
            ReplaceRecoveryResult;

        public ValueTask<OperationReceipt> HandoffAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActivityDescriptor current = descriptor
                ?? throw new InvalidOperationException("No note exists.");
            return ValueTask.FromResult(OperationReceipt.Committed(
                OperationId.From(Guid.NewGuid()),
                CorrelationId.From(Guid.NewGuid()),
                OperationKind.Handoff,
                LocalId,
                TargetId,
                current,
                DateTimeOffset.UtcNow));
        }

        public ValueTask<OperationReceipt> MoveAsync(
            ActivityId activityId,
            DeviceId targetDeviceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActivityDescriptor current = descriptor
                ?? throw new InvalidOperationException("No note exists.");
            OperationReceipt receipt = OperationReceipt.Committed(
                OperationId.From(Guid.NewGuid()),
                CorrelationId.From(Guid.NewGuid()),
                OperationKind.Move,
                LocalId,
                TargetId,
                current,
                DateTimeOffset.UtcNow);
            descriptor = null;
            Changed?.Invoke();
            return ValueTask.FromResult(receipt);
        }

        public ValueTask<DesktopReplaceTargetInventoryResult> GetReplaceTargetsAsync(
            ActivityId incomingActivityId,
            DeviceId targetDeviceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new DesktopReplaceTargetInventoryResult(
                FailureCode.None,
                false,
                new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
                [
                    new DesktopReplaceTargetSnapshot(
                        TargetId,
                        ActivityId.Parse("33333333-3333-3333-3333-333333333333"),
                        "Other target",
                        "workspace.note/v1",
                        2,
                        new string('B', 64),
                        "desktop"),
                    new DesktopReplaceTargetSnapshot(
                        TargetId,
                        ActivityId.Parse("44444444-4444-4444-4444-444444444444"),
                        "Existing target",
                        "workspace.note/v1",
                        4,
                        new string('A', 64),
                        "desktop"),
                ]));
        }

        public ValueTask<DesktopReplaceOperationResult> ReplaceAsync(
            ActivityId incomingActivityId,
            DesktopReplaceTargetSnapshot selectedTarget,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActivityDescriptor incoming = descriptor
                ?? throw new InvalidOperationException("No note exists.");
            var occurredAt = new DateTimeOffset(
                2026,
                7,
                15,
                12,
                0,
                0,
                TimeSpan.Zero);
            OperationContext context = OperationContext.Create(
                OperationId.Parse("55555555-5555-5555-5555-555555555555"),
                CorrelationId.Parse("66666666-6666-6666-6666-666666666666"),
                occurredAt.AddSeconds(30));
            OperationReceipt receipt = OperationReceipt.Committed(
                context.OperationId,
                context.CorrelationId,
                OperationKind.Replace,
                LocalId,
                TargetId,
                incoming,
                occurredAt);
            var capsule = new UndoCapsuleReference(
                UndoCapsuleId.Parse("77777777-7777-7777-7777-777777777777"),
                context.OperationId,
                context.CorrelationId,
                TargetId,
                selectedTarget.ActivityId,
                selectedTarget.Revision,
                selectedTarget.DescriptorDigest,
                incomingActivityId,
                incoming.DescriptorDigest,
                occurredAt.AddMinutes(15));
            return ValueTask.FromResult(new DesktopReplaceOperationResult(
                context.OperationId,
                context.CorrelationId,
                ActivityDeliveryStatus.Acknowledged,
                FailureCode.None,
                occurredAt,
                receipt,
                capsule));
        }

        public ValueTask<UndoReplaceResult> UndoReplaceAsync(
            UndoCapsuleId capsuleId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UndoRequested.TrySetResult(capsuleId);
            return UndoResult is not null
                ? ValueTask.FromResult(UndoResult)
                : ValueTask.FromException<UndoReplaceResult>(
                    new InvalidOperationException("No accessibility undo result was configured."));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private DesktopActivitySnapshot CreateSnapshot()
        {
            ActivityDescriptor current = descriptor
                ?? throw new InvalidOperationException("No note exists.");
            return new DesktopActivitySnapshot(
                current.Id,
                current.Title,
                current.Kind.Value,
                current.Sensitivity,
                ActivityLifecycle.Active);
        }
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

    private sealed class AccessibilityNetworkFactory :
        IDesktopLocalPairingNetworkFactory
    {
        public ValueTask<IDesktopLocalPairingNetworkSession> StartAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDesktopLocalPairingNetworkSession>(
                new AccessibilityNetworkSession());
    }

    private sealed class AccessibilityNetworkSession :
        IDesktopLocalPairingNetworkSession
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public int ListeningPort => 4747;

        public ImmutableArray<UnverifiedPairingCandidate> GetCandidates() => [];

        public ImmutableArray<DesktopTrustedPeerConnectionSnapshot>
            GetTrustedPeerConnections() =>
        [
            new(
                DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
                "Peer desk",
                "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
                DesktopTrustedPeerConnectionState.AuthenticatedIdle,
                null,
                null,
                "SHA256:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"),
        ];

        public ValueTask<PairingCeremonyResult> PairAsync(
            UnverifiedPairingCandidate candidate,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<PairingCeremonyResult>(
                new NotSupportedException());

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Flowspan.Diagnostics;

namespace Flowspan.Desktop;

public sealed class OperationHistoryItemViewModel
{
    internal OperationHistoryItemViewModel(OperationHistoryEntry entry)
    {
        Entry = entry;
        Label = DesktopText.Format("LocalData_HistoryItemLabelFormat", entry.Sequence);
        Kind = entry.Receipt.Kind.ToString();
        Status = entry.Receipt.Status.ToString();
        FailureCode = entry.Receipt.FailureCode.ToString();
        RecordedAt = entry.RecordedAt.ToString("O");
        OccurredAt = entry.Receipt.OccurredAt.ToUniversalTime().ToString("O");
        AutomationName = DesktopText.Format(
            "LocalData_HistoryItemAutomationNameFormat",
            Kind,
            Status,
            RecordedAt);
    }

    internal OperationHistoryEntry Entry { get; }

    public string AutomationName { get; }
    public string FailureCode { get; }
    public string Kind { get; }
    public string Label { get; }
    public string OccurredAt { get; }
    public string RecordedAt { get; }
    public string Status { get; }
}

public sealed class DiagnosticExportItemViewModel(string fileName)
{
    public string FileName { get; } = fileName;

    public string AutomationName => DesktopText.Format(
        "LocalData_DiagnosticItemAutomationNameFormat",
        FileName);
}

public sealed class LocalDataViewModel :
    INotifyPropertyChanged,
    IAsyncDisposable
{
    private readonly RelayCommand beginClearHistoryCommand;
    private readonly RelayCommand beginDeleteDiagnosticCommand;
    private readonly RelayCommand beginDeleteHistoryCommand;
    private readonly RelayCommand cancelClearHistoryCommand;
    private readonly RelayCommand cancelDeleteDiagnosticCommand;
    private readonly RelayCommand cancelDeleteHistoryCommand;
    private readonly AsyncRelayCommand confirmClearHistoryCommand;
    private readonly AsyncRelayCommand confirmDeleteDiagnosticCommand;
    private readonly AsyncRelayCommand confirmDeleteHistoryCommand;
    private readonly AsyncRelayCommand exportDiagnosticsCommand;
    private readonly AsyncRelayCommand exportHistoryCommand;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private readonly AsyncRelayCommand refreshCommand;
    private readonly IDesktopLocalDataService service;
    private string diagnosticDeleteConfirmation = string.Empty;
    private string diagnosticsExportPath =
        DesktopText.Get("LocalData_DiagnosticsExportPath_None");
    private string diagnosticsPreview =
        DesktopText.Get("LocalData_DiagnosticsPreview_Default");
    private string diagnosticsStatus = DesktopText.Get(
        "LocalData_DiagnosticsStatus_NotLoaded");
    private bool disposed;
    private string historyClearConfirmation = string.Empty;
    private string historyDeleteConfirmation = string.Empty;
    private string historyDescription =
        DesktopText.Get("LocalData_HistoryDescription_Default");
    private string historyExportPath = DesktopText.Get(
        "LocalData_HistoryExportPath_None");
    private string historyExportPreview =
        DesktopText.Get("LocalData_HistoryExportPreview_Default");
    private string historyStatus = DesktopText.Get(
        "LocalData_HistoryStatus_NotLoaded");
    private bool isBusy;
    private bool isClearHistoryVisible;
    private bool isDeleteDiagnosticVisible;
    private bool isDeleteHistoryVisible;
    private bool isHistoryAvailable;
    private DiagnosticExportItemViewModel? selectedDiagnosticExport;
    private OperationHistoryItemViewModel? selectedHistory;

    public LocalDataViewModel(IDesktopLocalDataService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        refreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
        exportHistoryCommand = new AsyncRelayCommand(
            ExportHistoryAsync,
            () => IsHistoryAvailable && !IsBusy);
        exportDiagnosticsCommand = new AsyncRelayCommand(
            ExportDiagnosticsAsync,
            () => IsHistoryAvailable && !IsBusy);
        beginDeleteHistoryCommand = new RelayCommand(
            BeginDeleteHistory,
            () => SelectedHistory is not null && !IsBusy);
        cancelDeleteHistoryCommand = new RelayCommand(
            CancelDeleteHistory,
            () => IsDeleteHistoryVisible && !IsBusy);
        confirmDeleteHistoryCommand = new AsyncRelayCommand(
            ConfirmDeleteHistoryAsync,
            () => SelectedHistory is not null
                && IsDeleteHistoryVisible
                && !IsBusy);
        beginClearHistoryCommand = new RelayCommand(
            BeginClearHistory,
            () => History.Count > 0 && !IsBusy);
        cancelClearHistoryCommand = new RelayCommand(
            CancelClearHistory,
            () => IsClearHistoryVisible && !IsBusy);
        confirmClearHistoryCommand = new AsyncRelayCommand(
            ConfirmClearHistoryAsync,
            () => IsClearHistoryVisible && !IsBusy);
        beginDeleteDiagnosticCommand = new RelayCommand(
            BeginDeleteDiagnostic,
            () => SelectedDiagnosticExport is not null && !IsBusy);
        cancelDeleteDiagnosticCommand = new RelayCommand(
            CancelDeleteDiagnostic,
            () => IsDeleteDiagnosticVisible && !IsBusy);
        confirmDeleteDiagnosticCommand = new AsyncRelayCommand(
            ConfirmDeleteDiagnosticAsync,
            () => SelectedDiagnosticExport is not null
                && IsDeleteDiagnosticVisible
                && !IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DiagnosticExportItemViewModel> DiagnosticExports { get; } = [];

    public ObservableCollection<OperationHistoryItemViewModel> History { get; } = [];

    public ICommand BeginClearHistoryCommand => beginClearHistoryCommand;
    public ICommand BeginDeleteDiagnosticCommand => beginDeleteDiagnosticCommand;
    public ICommand BeginDeleteHistoryCommand => beginDeleteHistoryCommand;
    public ICommand CancelClearHistoryCommand => cancelClearHistoryCommand;
    public ICommand CancelDeleteDiagnosticCommand => cancelDeleteDiagnosticCommand;
    public ICommand CancelDeleteHistoryCommand => cancelDeleteHistoryCommand;
    public ICommand ConfirmClearHistoryCommand => confirmClearHistoryCommand;
    public ICommand ConfirmDeleteDiagnosticCommand =>
        confirmDeleteDiagnosticCommand;
    public ICommand ConfirmDeleteHistoryCommand => confirmDeleteHistoryCommand;
    public ICommand ExportDiagnosticsCommand => exportDiagnosticsCommand;
    public ICommand ExportHistoryCommand => exportHistoryCommand;
    public ICommand RefreshCommand => refreshCommand;

    public string DiagnosticDeleteConfirmation
    {
        get => diagnosticDeleteConfirmation;
        private set => SetProperty(ref diagnosticDeleteConfirmation, value);
    }

    public string DiagnosticsExportPath
    {
        get => diagnosticsExportPath;
        private set => SetProperty(ref diagnosticsExportPath, value);
    }

    public string DiagnosticsPreview
    {
        get => diagnosticsPreview;
        private set => SetProperty(ref diagnosticsPreview, value);
    }

    public string DiagnosticsStatus
    {
        get => diagnosticsStatus;
        private set => SetProperty(ref diagnosticsStatus, value);
    }

    public string HistoryClearConfirmation
    {
        get => historyClearConfirmation;
        private set => SetProperty(ref historyClearConfirmation, value);
    }

    public string HistoryDeleteConfirmation
    {
        get => historyDeleteConfirmation;
        private set => SetProperty(ref historyDeleteConfirmation, value);
    }

    public string HistoryDescription
    {
        get => historyDescription;
        private set => SetProperty(ref historyDescription, value);
    }

    public string HistoryExportPath
    {
        get => historyExportPath;
        private set => SetProperty(ref historyExportPath, value);
    }

    public string HistoryExportPreview
    {
        get => historyExportPreview;
        private set => SetProperty(ref historyExportPreview, value);
    }

    public string HistoryStatus
    {
        get => historyStatus;
        private set => SetProperty(ref historyStatus, value);
    }

    public bool HasSelectedHistory => SelectedHistory is not null;

    public bool IsClearHistoryVisible
    {
        get => isClearHistoryVisible;
        private set
        {
            if (SetProperty(ref isClearHistoryVisible, value))
            {
                NotifyCommandState();
            }
        }
    }

    public bool IsDeleteDiagnosticVisible
    {
        get => isDeleteDiagnosticVisible;
        private set
        {
            if (SetProperty(ref isDeleteDiagnosticVisible, value))
            {
                NotifyCommandState();
            }
        }
    }

    public bool IsDeleteHistoryVisible
    {
        get => isDeleteHistoryVisible;
        private set
        {
            if (SetProperty(ref isDeleteHistoryVisible, value))
            {
                NotifyCommandState();
            }
        }
    }

    public bool IsHistoryAvailable
    {
        get => isHistoryAvailable;
        private set
        {
            if (SetProperty(ref isHistoryAvailable, value))
            {
                NotifyCommandState();
            }
        }
    }

    public DiagnosticExportItemViewModel? SelectedDiagnosticExport
    {
        get => selectedDiagnosticExport;
        set
        {
            if (SetProperty(ref selectedDiagnosticExport, value))
            {
                CancelDeleteDiagnostic();
                NotifyCommandState();
            }
        }
    }

    public OperationHistoryItemViewModel? SelectedHistory
    {
        get => selectedHistory;
        set
        {
            if (SetProperty(ref selectedHistory, value))
            {
                CancelDeleteHistory();
                OnPropertyChanged(nameof(HasSelectedHistory));
                NotifyCommandState();
            }
        }
    }

    public async ValueTask InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try
        {
            await service.InitializeAsync(cancellationToken)
                .ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            ApplyUnavailable();
        }
    }

    public async Task RefreshAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        IsBusy = true;
        try
        {
            ImmutableArray<OperationHistoryEntry> entries =
                await service.GetHistoryAsync(lifetimeCancellation.Token)
                    .ConfigureAwait(true);
            History.Clear();
            foreach (OperationHistoryEntry entry in entries.OrderByDescending(
                static entry => entry.Sequence))
            {
                History.Add(new OperationHistoryItemViewModel(entry));
            }

            SelectedHistory = null;
            IsHistoryAvailable = service.IsReady;
            HistoryStatus = entries.Length switch
            {
                0 => DesktopText.Get("LocalData_HistoryStatus_Empty"),
                1 => DesktopText.Get("LocalData_HistoryStatus_OneRetained"),
                _ => DesktopText.Format(
                    "LocalData_HistoryStatus_MultipleRetainedFormat",
                    entries.Length),
            };
            HistoryDescription = service.IsHistoryWriteDegraded
                ? DesktopText.Get("LocalData_HistoryDescription_Degraded")
                : DesktopText.Get("LocalData_HistoryDescription_Ready");
            DiagnosticsPreview = await service
                .PreviewDiagnosticsAsync(lifetimeCancellation.Token)
                .ConfigureAwait(true);
            DiagnosticsStatus = DesktopText.Get(
                "LocalData_DiagnosticsStatus_Ready");
            ReloadDiagnosticExports();
        }
        catch (OperationCanceledException)
            when (lifetimeCancellation.IsCancellationRequested)
        {
            HistoryStatus = DesktopText.Get(
                "LocalData_HistoryStatus_RefreshCancelled");
        }
        catch (Exception)
        {
            ApplyUnavailable();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
        await service.DisposeAsync().ConfigureAwait(false);
    }

    private bool IsBusy
    {
        get => isBusy;
        set
        {
            if (isBusy != value)
            {
                isBusy = value;
                NotifyCommandState();
            }
        }
    }

    private void ReloadDiagnosticExports()
    {
        DiagnosticExports.Clear();
        foreach (string fileName in service.ListDiagnosticExports())
        {
            DiagnosticExports.Add(new DiagnosticExportItemViewModel(fileName));
        }

        SelectedDiagnosticExport = null;
    }

    private void ApplyUnavailable()
    {
        History.Clear();
        DiagnosticExports.Clear();
        SelectedHistory = null;
        SelectedDiagnosticExport = null;
        IsHistoryAvailable = false;
        HistoryStatus = DesktopText.Get("LocalData_HistoryStatus_Unavailable");
        HistoryDescription =
            DesktopText.Get("LocalData_HistoryDescription_Unavailable");
        DiagnosticsStatus = DesktopText.Get(
            "LocalData_DiagnosticsStatus_Unavailable");
        DiagnosticsPreview =
            DesktopText.Get("LocalData_DiagnosticsPreview_Unavailable");
    }

    public void BeginDeleteHistory()
    {
        if (SelectedHistory is null)
        {
            return;
        }

        HistoryDeleteConfirmation = DesktopText.Format(
            "LocalData_HistoryDeleteConfirmationFormat",
            SelectedHistory.Kind,
            SelectedHistory.RecordedAt);
        IsDeleteHistoryVisible = true;
    }

    public void CancelDeleteHistory()
    {
        HistoryDeleteConfirmation = string.Empty;
        IsDeleteHistoryVisible = false;
    }

    public async Task ConfirmDeleteHistoryAsync()
    {
        OperationHistoryItemViewModel? selected = SelectedHistory;
        if (selected is null || !IsDeleteHistoryVisible)
        {
            return;
        }

        IsBusy = true;
        try
        {
            bool deleted = await service.DeleteHistoryAsync(
                selected.Entry.EntryId,
                lifetimeCancellation.Token).ConfigureAwait(true);
            CancelDeleteHistory();
            HistoryStatus = deleted
                ? DesktopText.Get("LocalData_HistoryStatus_ReceiptDeleted")
                : DesktopText.Get(
                    "LocalData_HistoryStatus_ReceiptNoLongerExists");
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            CancelDeleteHistory();
            HistoryStatus = DesktopText.Get(
                "LocalData_HistoryStatus_DeleteFailed");
            HistoryDescription =
                DesktopText.Get("LocalData_HistoryDescription_MutationFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void BeginClearHistory()
    {
        if (History.Count == 0)
        {
            return;
        }

        HistoryClearConfirmation = DesktopText.Format(
            "LocalData_HistoryClearConfirmationFormat",
            History.Count);
        IsClearHistoryVisible = true;
    }

    public void CancelClearHistory()
    {
        HistoryClearConfirmation = string.Empty;
        IsClearHistoryVisible = false;
    }

    public async Task ConfirmClearHistoryAsync()
    {
        if (!IsClearHistoryVisible)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _ = await service.ClearHistoryAsync(lifetimeCancellation.Token)
                .ConfigureAwait(true);
            CancelClearHistory();
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            CancelClearHistory();
            HistoryStatus = DesktopText.Get(
                "LocalData_HistoryStatus_ClearFailed");
            HistoryDescription =
                DesktopText.Get("LocalData_HistoryDescription_MutationFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportHistoryAsync()
    {
        IsBusy = true;
        try
        {
            DesktopRedactedExportResult exported = await service
                .ExportHistoryAsync(lifetimeCancellation.Token)
                .ConfigureAwait(true);
            HistoryExportPath = exported.FullPath;
            HistoryExportPreview = exported.RedactedContent;
            HistoryStatus = DesktopText.Get(
                "LocalData_HistoryStatus_ExportWritten");
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            HistoryStatus = DesktopText.Get(
                "LocalData_HistoryStatus_ExportFailed");
            HistoryDescription =
                DesktopText.Get("LocalData_HistoryDescription_ExportFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportDiagnosticsAsync()
    {
        IsBusy = true;
        try
        {
            DesktopRedactedExportResult exported = await service
                .ExportDiagnosticsAsync(lifetimeCancellation.Token)
                .ConfigureAwait(true);
            DiagnosticsExportPath = exported.FullPath;
            DiagnosticsPreview = exported.RedactedContent;
            DiagnosticsStatus = DesktopText.Get(
                "LocalData_DiagnosticsStatus_ExportWritten");
            ReloadDiagnosticExports();
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            DiagnosticsStatus = DesktopText.Get(
                "LocalData_DiagnosticsStatus_ExportFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void BeginDeleteDiagnostic()
    {
        if (SelectedDiagnosticExport is null)
        {
            return;
        }

        DiagnosticDeleteConfirmation =
            DesktopText.Get("LocalData_DiagnosticDeleteConfirmation");
        IsDeleteDiagnosticVisible = true;
    }

    public void CancelDeleteDiagnostic()
    {
        DiagnosticDeleteConfirmation = string.Empty;
        IsDeleteDiagnosticVisible = false;
    }

    public async Task ConfirmDeleteDiagnosticAsync()
    {
        DiagnosticExportItemViewModel? selected = SelectedDiagnosticExport;
        if (selected is null || !IsDeleteDiagnosticVisible)
        {
            return;
        }

        IsBusy = true;
        try
        {
            bool deleted = await service.DeleteDiagnosticExportAsync(
                selected.FileName,
                lifetimeCancellation.Token).ConfigureAwait(true);
            CancelDeleteDiagnostic();
            DiagnosticsStatus = deleted
                ? DesktopText.Get(
                    "LocalData_DiagnosticsStatus_BundleDeleted")
                : DesktopText.Get(
                    "LocalData_DiagnosticsStatus_BundleNoLongerExists");
            ReloadDiagnosticExports();
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException)
        {
            CancelDeleteDiagnostic();
            DiagnosticsStatus = DesktopText.Get(
                "LocalData_DiagnosticsStatus_DeleteFailed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyCommandState()
    {
        refreshCommand.NotifyCanExecuteChanged();
        exportHistoryCommand.NotifyCanExecuteChanged();
        exportDiagnosticsCommand.NotifyCanExecuteChanged();
        beginDeleteHistoryCommand.NotifyCanExecuteChanged();
        cancelDeleteHistoryCommand.NotifyCanExecuteChanged();
        confirmDeleteHistoryCommand.NotifyCanExecuteChanged();
        beginClearHistoryCommand.NotifyCanExecuteChanged();
        cancelClearHistoryCommand.NotifyCanExecuteChanged();
        confirmClearHistoryCommand.NotifyCanExecuteChanged();
        beginDeleteDiagnosticCommand.NotifyCanExecuteChanged();
        cancelDeleteDiagnosticCommand.NotifyCanExecuteChanged();
        confirmDeleteDiagnosticCommand.NotifyCanExecuteChanged();
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Flowspan.Desktop;

public sealed partial class MainWindow : Window
{
    private bool closeAfterDisposal;
    private bool disposalStarted;

    public MainWindow() => InitializeComponent();

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is WorkspaceShellViewModel viewModel)
        {
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel
            || closeAfterDisposal
            || DataContext is not IAsyncDisposable asyncDisposable)
        {
            return;
        }

        e.Cancel = true;
        if (disposalStarted)
        {
            return;
        }

        disposalStarted = true;
        try
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(true);
        }
        catch
        {
            // Closing must continue after every resource was asked to stop.
        }

        closeAfterDisposal = true;
        Close();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

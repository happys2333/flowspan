using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Flowspan.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is WorkspaceShellViewModel viewModel)
        {
            await viewModel.InitializeAsync().ConfigureAwait(true);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

using Avalonia.Threading;

namespace Flowspan.Desktop;

public interface IDesktopUiDispatcher
{
    public void Post(Action callback);
}

public sealed class InlineDesktopUiDispatcher : IDesktopUiDispatcher
{
    private InlineDesktopUiDispatcher()
    {
    }

    public static InlineDesktopUiDispatcher Instance { get; } = new();

    public void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        callback();
    }
}

public sealed class AvaloniaDesktopUiDispatcher : IDesktopUiDispatcher
{
    private AvaloniaDesktopUiDispatcher()
    {
    }

    public static AvaloniaDesktopUiDispatcher Instance { get; } = new();

    public void Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (Dispatcher.UIThread.CheckAccess())
        {
            callback();
            return;
        }

        Dispatcher.UIThread.Post(callback, DispatcherPriority.Normal);
    }
}

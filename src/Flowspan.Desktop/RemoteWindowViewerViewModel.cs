using System.Buffers;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Input;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Flowspan.Domain;
using Flowspan.Transport;

namespace Flowspan.Desktop;

public sealed class RemoteWindowViewerViewModel :
    INotifyPropertyChanged,
    IAsyncDisposable,
    IDesktopRemoteWindowReceivePolicy,
    IDesktopRemoteWindowParticipantRendererFactory
{
    private static readonly AsyncLocal<ViewerCallbackScope?> CurrentCallback = new();
    private readonly Lock gate = new();
    private readonly IDesktopUiDispatcher dispatcher;
    private readonly Func<ValueTask>? stopReceiving;
    private readonly RelayCommand enableCommand;
    private readonly AsyncRelayCommand stopCommand;
    private readonly TimeProvider timeProvider;
    private RemoteWindowPreparationRequest? permit;
    private ParticipantRenderer? renderer;
    private WriteableBitmap? bitmap;
    private long generation;
    private bool enabled;
    private bool stopping;
    private bool disposed;
    private Task stopTask = Task.CompletedTask;
    private Exception? notificationFailure;
    private Exception? stopFailure;

    public RemoteWindowViewerViewModel(
        IDesktopUiDispatcher? dispatcher = null,
        Func<ValueTask>? stopReceiving = null,
        TimeProvider? timeProvider = null)
    {
        this.dispatcher = dispatcher ?? AvaloniaDesktopUiDispatcher.Instance;
        this.stopReceiving = stopReceiving;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        enableCommand = new RelayCommand(EnableReceiving, CanEnable);
        stopCommand = new AsyncRelayCommand(
            StopFromCommandAsync,
            CanStop);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal Exception? NotificationFailure => Volatile.Read(ref notificationFailure);

    internal bool IsCallbackActive => HasActiveCallback();

    public ICommand EnableReceivingCommand => enableCommand;

    public ICommand StopReceivingCommand => stopCommand;

    public WriteableBitmap? ImageSource
    {
        get
        {
            lock (gate)
            {
                return bitmap;
            }
        }
    }

    public bool IsViewing => ImageSource is not null;

    public bool IsReceivingEnabled
    {
        get
        {
            lock (gate)
            {
                return enabled;
            }
        }
    }

    public string Status
    {
        get
        {
            lock (gate)
            {
                return stopReceiving is null
                    ? DesktopText.Get("Viewer_Unavailable")
                    : stopping
                        ? DesktopText.Get("Viewer_Stopping")
                        : stopFailure is not null
                            ? DesktopText.Get("Viewer_StopFailed")
                        : bitmap is not null
                            ? DesktopText.Get("Viewer_Viewing")
                            : enabled
                                ? DesktopText.Get("Viewer_Waiting")
                                : DesktopText.Get("Viewer_Disabled");
            }
        }
    }

    public void EnableReceiving()
    {
        lock (gate)
        {
            if (disposed || stopping || enabled || stopReceiving is null
                || stopFailure is not null || renderer is not null || bitmap is not null)
            {
                return;
            }

            generation = checked(generation + 1);
            permit = null;
            stopFailure = null;
            enabled = true;
        }

        PublishChanged();
    }

    internal string? GetRejectionReason(RemoteWindowPreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            if (disposed || !enabled || stopping)
            {
                return "renderer_unavailable";
            }

            if (request.RequestedRole is not MirrorParticipantRole.ViewOnly)
            {
                return "role_unsupported";
            }

            if (renderer is not null
                || (permit is not null
                    && permit != request
                    && timeProvider.GetUtcNow() < permit.Deadline))
            {
                return "renderer_unavailable";
            }

            permit = request;
            return null;
        }
    }

    string? IDesktopRemoteWindowReceivePolicy.GetRejectionReason(
        RemoteWindowPreparationRequest request) => GetRejectionReason(request);

    internal async ValueTask<IDesktopRemoteWindowParticipantRenderer?> PrepareAsync(
        RemoteWindowPreparationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ParticipantRenderer candidate;
        ValueTask readiness;
        lock (gate)
        {
            if (disposed || !enabled || stopping
                || request.RequestedRole is not MirrorParticipantRole.ViewOnly
                || permit != request || renderer is not null
                || timeProvider.GetUtcNow() >= request.Deadline)
            {
                return null;
            }

            candidate = new ParticipantRenderer(this, generation);
            renderer = candidate;
            readiness = candidate.PrepareReadinessAsync(cancellationToken);
        }

        Exception? failure = null;
        try
        {
            await readiness.ConfigureAwait(false);
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsCurrent(candidate) && permit == request
                    && timeProvider.GetUtcNow() < request.Deadline)
                {
                    return candidate;
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            await candidate.DisposeForOwnerAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(FindFatalAllocationFailure(failure) ?? failure).Throw();
        }

        return null;
    }

    ValueTask<IDesktopRemoteWindowParticipantRenderer?>
        IDesktopRemoteWindowParticipantRendererFactory.PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) =>
        PrepareAsync(request, cancellationToken);

    public ValueTask StopReceivingAsync() => StopReceivingCoreAsync(forceJoin: false);

    private ValueTask StopReceivingCoreAsync(bool forceJoin)
    {
        ParticipantRenderer? current;
        TaskCompletionSource completion;
        lock (gate)
        {
            if (stopping)
            {
                return !forceJoin && HasActiveCallback()
                    ? ValueTask.CompletedTask
                    : new ValueTask(stopTask);
            }

            if (!enabled && renderer is null && permit is null)
            {
                return !forceJoin && HasActiveCallback()
                    ? ValueTask.CompletedTask
                    : new ValueTask(stopTask);
            }

            enabled = false;
            stopping = true;
            generation = checked(generation + 1);
            permit = null;
            current = renderer;
            completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            stopTask = completion.Task;
        }

        Exception? activationFailure = null;
        try
        {
            current?.CloseNow();
        }
        catch (Exception exception)
        {
            activationFailure = exception;
        }

        PublishChanged();
        _ = CompleteStopAsync(current, completion, activationFailure);
        return !forceJoin && HasActiveCallback()
            ? ValueTask.CompletedTask
            : new ValueTask(completion.Task);
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            disposed = true;
        }

        return StopReceivingAsync();
    }

    internal ValueTask DisposeForOwnerAsync()
    {
        lock (gate)
        {
            disposed = true;
        }

        return StopReceivingCoreAsync(forceJoin: true);
    }

    private bool CanEnable()
    {
        lock (gate)
        {
            return !disposed && !stopping && !enabled && stopReceiving is not null
                && stopFailure is null && renderer is null && bitmap is null;
        }
    }

    private bool CanStop()
    {
        lock (gate)
        {
            return !disposed && !stopping
                && (enabled || stopFailure is not null || renderer is not null || bitmap is not null);
        }
    }

    private async Task CompleteStopAsync(
        ParticipantRenderer? current,
        TaskCompletionSource completion,
        Exception? activationFailure)
    {
        Exception? failure = activationFailure;
        try
        {
            if (stopReceiving is not null)
            {
                await stopReceiving().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }

        try
        {
            if (current is not null)
            {
                await current.DisposeForOwnerAsync().ConfigureAwait(false);
            }

        }
        catch (Exception exception)
        {
            failure = failure is null
                ? exception
                : new AggregateException(failure, exception);
        }

        try
        {
            if (current is not null || ImageSource is not null)
            {
                await InvokeUiAsync(() => ClearBitmap(current), CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = failure is null ? exception : new AggregateException(failure, exception);
        }

        lock (gate)
        {
            stopping = false;
            stopFailure = failure;
        }

        PublishChanged();
        if (failure is null)
        {
            completion.TrySetResult();
        }
        else
        {
            completion.TrySetException(FindFatalAllocationFailure(failure) ?? failure);
        }
    }

    private async Task StopFromCommandAsync()
    {
        try
        {
            await StopReceivingAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (FindFatalAllocationFailure(exception) is null)
        {
            // The API preserves the failure for its owner; presentation uses
            // the resource-backed stopped-with-failure state instead of text.
        }
    }

    private static OutOfMemoryException? FindFatalAllocationFailure(Exception exception)
    {
        if (exception is OutOfMemoryException fatal)
        {
            return fatal;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (Exception nested in aggregate.InnerExceptions)
            {
                if (FindFatalAllocationFailure(nested) is { } nestedFatal)
                {
                    return nestedFatal;
                }
            }

            return null;
        }

        return exception.InnerException is { } inner
            ? FindFatalAllocationFailure(inner)
            : null;
    }

    private void Present(
        ParticipantRenderer owner,
        DesktopRemoteWindowBgraFrame frame,
        CancellationToken cancellationToken)
    {
        WriteableBitmap? replacement = null;
        WriteableBitmap? previous = null;
        try
        {
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrent(owner))
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                replacement = new WriteableBitmap(
                    new PixelSize(frame.Width, frame.Height),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Opaque);
                using (ILockedFramebuffer target = replacement.Lock())
                {
                    if (!MemoryMarshal.TryGetArray(frame.Pixels, out ArraySegment<byte> pixels)
                        || pixels.Array is null)
                    {
                        throw new InvalidDataException();
                    }

                    for (int row = 0; row < frame.Height; row++)
                    {
                        Marshal.Copy(
                            pixels.Array,
                            pixels.Offset + (row * frame.Stride),
                            target.Address + (row * target.RowBytes),
                            frame.Stride);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrent(owner))
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                previous = bitmap;
                bitmap = replacement;
                replacement = null;
            }

            PublishChanged();
        }
        finally
        {
            ClearAndDispose(previous);
            ClearAndDispose(replacement);
        }
    }

    private void ProbeReadiness(ParticipantRenderer owner, CancellationToken cancellationToken)
    {
        WriteableBitmap? probe = null;
        try
        {
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrent(owner))
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                probe = new WriteableBitmap(
                    new PixelSize(1, 1),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Opaque);
                using ILockedFramebuffer target = probe.Lock();
                Marshal.WriteInt32(target.Address, 0);
            }
        }
        finally
        {
            ClearAndDispose(probe);
        }
    }

    private bool IsCurrent(ParticipantRenderer owner) =>
        !disposed && enabled && !stopping
        && owner.Generation == generation
        && ReferenceEquals(renderer, owner)
        && !owner.IsClosed;

    private void ClearBitmap(ParticipantRenderer? owner)
    {
        WriteableBitmap? previous;
        lock (gate)
        {
            if (owner is not null && !ReferenceEquals(renderer, owner))
            {
                return;
            }

            previous = bitmap;
            bitmap = null;
            renderer = null;
            permit = null;
        }

        try
        {
            PublishChanged();
        }
        finally
        {
            ClearAndDispose(previous);
        }
    }

    private static void ClearAndDispose(WriteableBitmap? value)
    {
        if (value is null)
        {
            return;
        }

        try
        {
            using ILockedFramebuffer target = value.Lock();
            byte[] zeroRow = ArrayPool<byte>.Shared.Rent(target.RowBytes);
            try
            {
                CryptographicOperations.ZeroMemory(zeroRow);
                for (int row = 0; row < target.Size.Height; row++)
                {
                    Marshal.Copy(zeroRow, 0, target.Address + (row * target.RowBytes), target.RowBytes);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(zeroRow, clearArray: true);
            }
        }
        finally
        {
            value.Dispose();
        }
    }

    private async Task InvokeUiAsync(Action action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var work = new UiWork(action, completion, cancellationToken);
        using CancellationTokenRegistration registration =
            cancellationToken.Register(work.CancelBeforeExecution);
        try
        {
            dispatcher.Post(work.Execute);
        }
        catch (Exception exception)
        {
            work.FailBeforeExecution(exception);
        }

        await completion.Task.ConfigureAwait(false);
    }

    private void PublishChanged()
    {
        try
        {
            dispatcher.Post(() =>
            {
                ViewerCallbackScope? previous = CurrentCallback.Value;
                var scope = new ViewerCallbackScope(this, previous);
                CurrentCallback.Value = scope;
                try
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImageSource)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsViewing)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsReceivingEnabled)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                    enableCommand.NotifyCanExecuteChanged();
                    stopCommand.NotifyCanExecuteChanged();
                }
                catch (Exception exception)
                {
                    Interlocked.CompareExchange(ref notificationFailure, exception, null);
                }
                finally
                {
                    scope.Exit();
                    CurrentCallback.Value = previous;
                }
            });
        }
        catch (Exception exception)
        {
            Interlocked.CompareExchange(ref notificationFailure, exception, null);
        }
    }

    private bool HasActiveCallback()
    {
        for (ViewerCallbackScope? current = CurrentCallback.Value;
            current is not null;
            current = current.Previous)
        {
            if (current.IsActive && ReferenceEquals(current.Owner, this))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ViewerCallbackScope(
        RemoteWindowViewerViewModel owner,
        ViewerCallbackScope? previous)
    {
        private int active = 1;

        public RemoteWindowViewerViewModel Owner { get; } = owner;

        public ViewerCallbackScope? Previous { get; } = previous;

        public bool IsActive => Volatile.Read(ref active) != 0;

        public void Exit() => Volatile.Write(ref active, 0);
    }

    private sealed class UiWork(
        Action action,
        TaskCompletionSource completion,
        CancellationToken cancellationToken)
    {
        private int state;

        public void CancelBeforeExecution()
        {
            if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
            {
                completion.TrySetCanceled(cancellationToken);
            }
        }

        public void FailBeforeExecution(Exception exception)
        {
            if (Interlocked.CompareExchange(ref state, 2, 0) == 0)
            {
                completion.TrySetException(exception);
            }
        }

        public void Execute()
        {
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
            {
                return;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                completion.TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }
    }

    private sealed class ParticipantRenderer(
        RemoteWindowViewerViewModel owner,
        long generation) : IDesktopRemoteWindowParticipantRenderer
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly TaskCompletionSource disposal = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock renderGate = new();
        private Task renderTask = Task.CompletedTask;
        private bool closed;
        private bool disposing;

        public long Generation { get; } = generation;

        public bool IsClosed => Volatile.Read(ref closed);

        public ValueTask PrepareReadinessAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource completion;
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(closed, this);
                completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                renderTask = completion.Task;
            }

            _ = CompleteReadinessAsync(completion, cancellationToken);
            return new ValueTask(completion.Task);
        }

        public ValueTask RenderAsync(
            DesktopRemoteWindowBgraFrame frame,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(frame);
            TaskCompletionSource completion;
            lock (renderGate)
            {
                ObjectDisposedException.ThrowIf(closed, this);
                if (!renderTask.IsCompleted)
                {
                    throw new InvalidOperationException();
                }

                completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                renderTask = completion.Task;
            }

            _ = CompleteRenderAsync(frame, completion, cancellationToken);
            return new ValueTask(completion.Task);
        }

        public void CloseNow()
        {
            lock (renderGate)
            {
                if (closed)
                {
                    return;
                }

                closed = true;
                cancellation.Cancel();
            }
        }

        public ValueTask DisposeAsync() => DisposeCore(forceJoin: false);

        public ValueTask DisposeForOwnerAsync() => DisposeCore(forceJoin: true);

        private ValueTask DisposeCore(bool forceJoin)
        {
            Task pending;
            lock (renderGate)
            {
                if (disposing)
                {
                    return !forceJoin && owner.HasActiveCallback()
                        ? ValueTask.CompletedTask
                        : new ValueTask(disposal.Task);
                }

                disposing = true;
                pending = renderTask;
            }

            Exception? activationFailure = null;
            try
            {
                CloseNow();
            }
            catch (Exception exception)
            {
                activationFailure = exception;
            }

            _ = DisposeCoreAsync(pending, activationFailure);
            return !forceJoin && owner.HasActiveCallback()
                ? ValueTask.CompletedTask
                : new ValueTask(disposal.Task);
        }

        private async Task CompleteRenderAsync(
            DesktopRemoteWindowBgraFrame frame,
            TaskCompletionSource completion,
            CancellationToken cancellationToken)
        {
            try
            {
                await RenderCoreAsync(frame, cancellationToken).ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private async Task CompleteReadinessAsync(
            TaskCompletionSource completion,
            CancellationToken cancellationToken)
        {
            try
            {
                using CancellationTokenSource linked =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellation.Token,
                        cancellationToken);
                await owner.InvokeUiAsync(
                    () => owner.ProbeReadiness(this, linked.Token),
                    linked.Token).ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (OperationCanceledException exception)
            {
                completion.TrySetCanceled(exception.CancellationToken);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private async Task RenderCoreAsync(
            DesktopRemoteWindowBgraFrame frame,
            CancellationToken cancellationToken)
        {
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellation.Token,
                    cancellationToken);
            await owner.InvokeUiAsync(
                () => owner.Present(this, frame, linked.Token),
                linked.Token).ConfigureAwait(false);
        }

        private async Task DisposeCoreAsync(Task pending, Exception? activationFailure)
        {
            Exception? failure = activationFailure;
            try
            {
                await pending.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }

            try
            {
                await owner.InvokeUiAsync(
                    () => owner.ClearBitmap(this),
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }

            try
            {
                cancellation.Dispose();
            }
            catch (Exception exception)
            {
                failure = failure is null ? exception : new AggregateException(failure, exception);
            }

            if (failure is null)
            {
                disposal.TrySetResult();
            }
            else
            {
                disposal.TrySetException(FindFatalAllocationFailure(failure) ?? failure);
            }
        }
    }
}

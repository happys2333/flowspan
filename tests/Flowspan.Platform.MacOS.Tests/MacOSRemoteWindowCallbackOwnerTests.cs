using System.Diagnostics.CodeAnalysis;
using Flowspan.Platform.MacOS;

namespace Flowspan.Platform.MacOS.Tests;

public sealed class MacOSRemoteWindowCallbackOwnerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    [Fact]
    [SuppressMessage("Naming", "CA1707", Justification = "Preserves the named prerequisite acceptance scenario.")]
    public async Task SourceLossWithoutSample_ClosesAdmissionAndNotifiesOnce()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        Assert.True(owner.BindStreamOnce(101));
        int unavailable = 0;
        Assert.True(owner.Activate(() => unavailable++));

        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive);
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Active);

        Assert.True(owner.AdmissionClosed);
        Assert.Equal(1, unavailable);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await owner.RetireAsync());
    }

    [Fact]
    public async Task EarlyTerminalWaitsForExactBindingAndNeverReopensAdmission()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        int unavailable = 0;
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive);
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        Assert.True(owner.AdmissionClosed);
        Assert.Equal(0, unavailable);
        Assert.True(owner.BindStreamOnce(101));
        Assert.False(owner.BindStreamOnce(101));
        Assert.True(owner.Activate(() => unavailable++));
        Assert.True(owner.AdmissionClosed);
        Assert.Equal(1, unavailable);
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Active);
        Assert.True(owner.AdmissionClosed);
        Assert.Equal(1, unavailable);
        await owner.RetireAsync();
    }

    [Fact]
    public async Task EarlyTerminalBindingMismatchRetiresWithoutInstallingHandler()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        pool.Dispatch(11, 999, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.False(owner.BindStreamOnce(101));
        int unavailable = 0;
        Assert.False(owner.Activate(() => unavailable++));
        Assert.True(owner.AdmissionClosed);
        Assert.Equal(0, unavailable);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await owner.RetireAsync());
        Assert.False(owner.BindStreamOnce(999));
    }

    [Fact]
    public async Task RetirementClosesAdmissionBeforeJoiningBlockedInvocation()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        Assert.True(owner.BindStreamOnce(101));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        Assert.True(owner.Activate(() =>
        {
            entered.TrySetResult();
            Assert.True(release.Wait(TestTimeout));
        }));
        Task callback = Task.Run(() => pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive));
        Task<MacOSRemoteWindowCallbackRetirement>? retire = null;
        try
        {
            await entered.Task.WaitAsync(TestTimeout);
            retire = owner.RetireAsync().AsTask();
            Assert.True(owner.AdmissionClosed);
            Assert.False(retire.IsCompleted);
        }
        finally
        {
            release.Set();
            await callback.WaitAsync(TestTimeout);
            if (retire is not null)
            {
                Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
                    await retire.WaitAsync(TestTimeout));
            }
        }
    }

    [Fact]
    public async Task PublishedAddressCannotBeOverwrittenAndLateCallbackCannotReachNewOwner()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var first));
        var retired = Assert.IsType<MacOSRemoteWindowCallbackOwner>(first);
        Assert.True(retired.TryPublishBridge(11));
        Assert.True(retired.BindStreamOnce(101));
        int retiredNotifications = 0;
        Assert.True(retired.Activate(() => retiredNotifications++));
        await retired.RetireAsync();
        Assert.True(pool.TryReserve(out var second));
        var current = Assert.IsType<MacOSRemoteWindowCallbackOwner>(second);
        Assert.False(current.TryPublishBridge(11));
        Assert.True(current.TryPublishBridge(12));
        Assert.False(current.TryPublishBridge(13));
        Assert.True(current.BindStreamOnce(101));
        int unavailable = 0;
        Assert.True(current.Activate(() => unavailable++));
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive);
        pool.Dispatch(11, -1, MacOSRemoteWindowDelegateSignal.StoppedWithError);
        Assert.Equal(0, retiredNotifications);
        Assert.Null(retired.Failure);
        Assert.False(current.AdmissionClosed);
        Assert.Equal(0, unavailable);
        pool.Dispatch(12, 101, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.Equal(1, unavailable);
        await current.RetireAsync();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(16)]
    public async Task PublishedRetiredSlotsStillConsumeEntirePoolBudget(int capacity)
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(capacity);
        for (int index = 0; index < capacity; index++)
        {
            Assert.True(pool.TryReserve(out var candidate));
            var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
            Assert.True(owner.TryPublishBridge(index + 1));
            await owner.RetireAsync();
        }

        Assert.False(pool.TryReserve(out var absent));
        Assert.Null(absent);
    }

    [Fact]
    public async Task DirectCallbackRetirementRejectsSelfJoinBeforeWaiting()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        Assert.True(owner.BindStreamOnce(101));
        MacOSRemoteWindowCallbackRetirement? observed = null;
        Assert.True(owner.Activate(() =>
        {
            var selfJoin = owner.RetireAsync();
            if (selfJoin.IsCompletedSuccessfully)
            {
                observed = selfJoin.Result;
            }
        }));
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.SelfJoinRejected, observed);
        Assert.True(owner.AdmissionClosed);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await owner.RetireAsync());
    }

    [Fact]
    public async Task CallbackTaskRunDescendantRejectsSelfJoinBeforeWaiting()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        Assert.True(owner.BindStreamOnce(101));
        MacOSRemoteWindowCallbackRetirement? observed = null;
        Assert.True(owner.Activate(() =>
        {
            observed = Task.Run(() =>
            {
                var selfJoin = owner.RetireAsync();
                return selfJoin.IsCompletedSuccessfully
                    ? (MacOSRemoteWindowCallbackRetirement?)selfJoin.Result
                    : null;
            }).GetAwaiter().GetResult();
        }));
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.SelfJoinRejected, observed);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await owner.RetireAsync());
    }

    [Fact]
    public async Task CapturedExecutionContextAfterCallbackExitCanRetireNormally()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        Assert.True(owner.BindStreamOnce(101));
        var exitObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<MacOSRemoteWindowCallbackRetirement>? descendant = null;
        Assert.True(owner.Activate(() => descendant = Task.Run(async () =>
        {
            await exitObserved.Task;
            return await owner.RetireAsync();
        })));
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive);
        exitObserved.SetResult();
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await Assert.IsAssignableFrom<Task<MacOSRemoteWindowCallbackRetirement>>(descendant).WaitAsync(TestTimeout));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SuppressMessage("Usage", "CA2201", Justification = "Exercises preservation of injected fatal allocation failures.")]
    public async Task HandlerFaultIsContainedAndOriginalNestedFatalRemainsObservable(bool nested)
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        Assert.True(owner.BindStreamOnce(101));
        var fatal = new OutOfMemoryException("injected owner handler failure");
        Exception thrown = nested
            ? new AggregateException(new InvalidOperationException(), new AggregateException(fatal))
            : fatal;
        Assert.True(owner.Activate(() => throw thrown));
        Assert.Null(Record.Exception(() =>
            pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive)));
        Assert.True(owner.AdmissionClosed);
        Assert.Same(fatal, owner.Failure);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await owner.RetireAsync());
    }

    [Fact]
    public async Task TerminalAfterBindingWithWrongIdentityRetiresBeforeActivation()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        Assert.True(owner.BindStreamOnce(101));
        pool.Dispatch(11, 999, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.False(owner.Activate(() => Assert.Fail("Mismatched owner cannot activate.")));
        Assert.True(owner.AdmissionClosed);
        await owner.RetireAsync();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(101, 999)]
    public async Task InvalidSignalOrZeroStreamCannotInvokeHandler(int identity, int signal)
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        Assert.True(owner.BindStreamOnce(101));
        int unavailable = 0;
        Assert.True(owner.Activate(() => unavailable++));
        pool.Dispatch(11, identity, (MacOSRemoteWindowDelegateSignal)signal);
        Assert.Equal(0, unavailable);
        Assert.False(owner.AdmissionClosed);
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.Equal(1, unavailable);
        await owner.RetireAsync();
    }

    [Fact]
    [SuppressMessage("Usage", "CA2201", Justification = "Exercises fatal precedence beyond a former arbitrary traversal cutoff.")]
    public async Task DeeplyNestedFatalRemainsOriginalObservableFailure()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(1);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        Assert.True(owner.TryPublishBridge(11));
        Assert.True(owner.BindStreamOnce(101));
        var fatal = new OutOfMemoryException("injected deep fatal");
        Exception nested = fatal;
        for (int depth = 0; depth < 65; depth++)
        {
            nested = new AggregateException(nested);
        }

        Assert.True(owner.Activate(() => throw nested));
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.Same(fatal, owner.Failure);
        await owner.RetireAsync();
    }

    [Fact]
    public async Task PublishedZeroBindingRetiresPermanentlyWithoutInstallingHandler()
    {
        var pool = new MacOSRemoteWindowCallbackOwnerPool(2);
        Assert.True(pool.TryReserve(out var candidate));
        var owner = Assert.IsType<MacOSRemoteWindowCallbackOwner>(candidate);
        int unavailable = 0;
        Assert.False(owner.BindStreamOnce(0));
        Assert.False(owner.AdmissionClosed);
        Assert.False(owner.Activate(() => unavailable++));
        Assert.True(owner.TryPublishBridge(11));
        Assert.False(owner.BindStreamOnce(0));
        Assert.True(owner.AdmissionClosed);
        Assert.False(owner.BindStreamOnce(101));
        Assert.False(owner.Activate(() => unavailable++));
        pool.Dispatch(11, 101, MacOSRemoteWindowDelegateSignal.Inactive);
        Assert.Equal(0, unavailable);
        Assert.Null(owner.Failure);
        Assert.Equal(MacOSRemoteWindowCallbackRetirement.ManagedInvocationsExited,
            await owner.RetireAsync());
        Assert.False(owner.BindStreamOnce(101));
        Assert.False(owner.TryPublishBridge(12));
    }
}

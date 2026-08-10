using System.Collections.Concurrent;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Desktop.Tests;

public sealed class DesktopPairingDecisionSourceTests
{
    [Fact]
    public async Task DisposeWaitsForActiveCancellationPublication()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var cancellation = new CancellationTokenSource();
        using var releasePublication = new ManualResetEventSlim();
        var publicationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new DesktopPairingDecisionSource();
        source.PromptChanged += OnPromptChanged;
        Task<PairingDecision> decision = source.DecideAsync(
            CreateRequest(peer, "111111"),
            cancellation.Token).AsTask();

        cancellation.Cancel();
        await publicationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task disposing = Task.Run(source.Dispose);
        int returnedBeforePublicationReleased = 0;
        Task observeDisposal = disposing.ContinueWith(
            _ =>
            {
                if (!releasePublication.IsSet)
                {
                    Interlocked.Exchange(
                        ref returnedBeforePublicationReleased,
                        1);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            Assert.False(releasePublication.IsSet);
        }
        finally
        {
            releasePublication.Set();
            await Task.WhenAll(disposing, observeDisposal);
        }

        Assert.Equal(0, Volatile.Read(ref returnedBeforePublicationReleased));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decision);

        void OnPromptChanged(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind == DesktopPairingPromptChangeKind.Canceled)
            {
                publicationEntered.TrySetResult();
                releasePublication.Wait(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task CancellationCoalescingRetainsTheHighestAllocatedSequence()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        using var releaseFirstQueue = new ManualResetEventSlim();
        var firstQueueReached = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var workerCaptured = new TaskCompletionSource<Action>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new ConcurrentQueue<DesktopPairingPromptChangedEventArgs>();
        int firstCancellationPaused = 0;
        using var source = new DesktopPairingDecisionSource(
            publish => workerCaptured.TrySetResult(publish),
            BeforeCancellationChangeQueued);
        source.PromptChanged += (_, eventArgs) => observed.Enqueue(eventArgs);

        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(peer, "111111"),
            firstCancellation.Token).AsTask();
        Task cancelFirst = Task.Run(firstCancellation.Cancel);
        await firstQueueReached.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task<PairingDecision> second = source.DecideAsync(
            CreateRequest(peer, "222222"),
            secondCancellation.Token).AsTask();
        secondCancellation.Cancel();
        Action publish = await workerCaptured.Task.WaitAsync(TimeSpan.FromSeconds(5));

        releaseFirstQueue.Set();
        await cancelFirst.WaitAsync(TimeSpan.FromSeconds(5));
        publish();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

        Assert.Contains(
            observed,
            static change => change.Kind == DesktopPairingPromptChangeKind.Canceled
                && change.Sequence == 4);

        void BeforeCancellationChangeQueued(
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind != DesktopPairingPromptChangeKind.Canceled
                || Interlocked.CompareExchange(
                    ref firstCancellationPaused,
                    1,
                    0) != 0)
            {
                return;
            }

            firstQueueReached.TrySetResult();
            releaseFirstQueue.Wait(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task DelayedCancellationKeepsSequenceAndCoalescesToLatestChange()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var source = new DesktopPairingDecisionSource();
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        using var thirdCancellation = new CancellationTokenSource();
        using var releaseFirstCancellation = new ManualResetEventSlim();
        var firstCancellationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var latestCancellationPublished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new ConcurrentQueue<DesktopPairingPromptChangedEventArgs>();
        long firstCanceledSequence = 0;
        int blockedCancellation = 0;
        source.PromptChanged += BlockFirstCancellation;
        source.PromptChanged += RecordChange;

        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(peer, "111111"),
            firstCancellation.Token).AsTask();
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await firstCancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Task<PairingDecision> second = source.DecideAsync(
            CreateRequest(peer, "222222"),
            secondCancellation.Token).AsTask();
        secondCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Task<PairingDecision> third = source.DecideAsync(
            CreateRequest(peer, "333333"),
            thirdCancellation.Token).AsTask();
        thirdCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);

        releaseFirstCancellation.Set();
        await latestCancellationPublished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(
            [
                (DesktopPairingPromptChangeKind.Opened, 1L),
                (DesktopPairingPromptChangeKind.Opened, 3L),
                (DesktopPairingPromptChangeKind.Opened, 5L),
                (DesktopPairingPromptChangeKind.Canceled, 2L),
                (DesktopPairingPromptChangeKind.Canceled, 6L),
            ],
            observed.Select(static change => (change.Kind, change.Sequence)));

        void BlockFirstCancellation(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            if (eventArgs.Kind != DesktopPairingPromptChangeKind.Canceled
                || Interlocked.CompareExchange(ref blockedCancellation, 1, 0) != 0)
            {
                return;
            }

            Volatile.Write(ref firstCanceledSequence, eventArgs.Sequence);
            firstCancellationEntered.TrySetResult();
            releaseFirstCancellation.Wait(TimeSpan.FromSeconds(10));
        }

        void RecordChange(
            object? sender,
            DesktopPairingPromptChangedEventArgs eventArgs)
        {
            observed.Enqueue(eventArgs);
            if (eventArgs.Kind == DesktopPairingPromptChangeKind.Canceled
                && eventArgs.Sequence > Volatile.Read(ref firstCanceledSequence))
            {
                latestCancellationPublished.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task VerifiedRequestWaitsForAndReturnsExplicitLocalGrant()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var source = new DesktopPairingDecisionSource();
        DateTimeOffset expiry = DateTimeOffset.Parse(
            "2026-07-14T02:00:00+00:00",
            System.Globalization.CultureInfo.InvariantCulture);

        Task<PairingDecision> decision = source.DecideAsync(
            new PairingConfirmationRequest(
                peer.PublicIdentity,
                new ProtocolVersion(1, 0),
                "123456",
                expiry)).AsTask();
        DesktopPairingPrompt prompt = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt);

        Assert.False(decision.IsCompleted);
        Assert.Equal("Peer desk", prompt.PeerDisplayName);
        Assert.Equal(peer.DeviceId.ToString(), prompt.PeerDeviceId);
        Assert.Equal(peer.PublicIdentity.Fingerprint, prompt.PeerFingerprint);
        Assert.Equal("123456", prompt.ShortAuthenticationString);
        Assert.Equal("1.0", prompt.ProtocolVersion);
        Assert.Equal(expiry, prompt.ExpiresAt);

        Assert.True(source.TryAccept(
            prompt.PromptId,
            CapabilityGrant.Of(Capability.ActivityReceive)));

        PairingDecision accepted = await decision;
        Assert.True(accepted.Accepted);
        Assert.True(accepted.CapabilitiesGrantedToPeer.Allows(
            Capability.ActivityReceive));
        Assert.False(accepted.CapabilitiesGrantedToPeer.Allows(
            Capability.ActivityOffer));
        Assert.Null(source.CurrentPrompt);
    }

    [Fact]
    public async Task ConcurrentRequestIsRejectedWithoutReplacingVisiblePeer()
    {
        using DeviceIdentity firstPeer = CreatePeer("First peer");
        using DeviceIdentity secondPeer = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Second peer");
        using var source = new DesktopPairingDecisionSource();

        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(firstPeer, "111111")).AsTask();
        DesktopPairingPrompt firstPrompt = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt);

        PairingDecision second = await source.DecideAsync(
            CreateRequest(secondPeer, "222222"));

        Assert.False(second.Accepted);
        Assert.Equal(firstPrompt.PromptId, source.CurrentPrompt?.PromptId);
        Assert.Equal("First peer", source.CurrentPrompt?.PeerDisplayName);
        Assert.True(source.TryReject(firstPrompt.PromptId));
        Assert.False((await first).Accepted);
    }

    [Fact]
    public async Task CancellationClearsPromptAndStaleCommandCannotResolveNextRequest()
    {
        using DeviceIdentity firstPeer = CreatePeer("First peer");
        using DeviceIdentity nextPeer = DeviceIdentity.Generate(
            DeviceId.Parse("33333333-3333-3333-3333-333333333333"),
            "Next peer");
        using var source = new DesktopPairingDecisionSource();
        using var cancellation = new CancellationTokenSource();
        Task<PairingDecision> first = source.DecideAsync(
            CreateRequest(firstPeer, "111111"),
            cancellation.Token).AsTask();
        Guid stalePromptId = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt).PromptId;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Null(source.CurrentPrompt);

        Task<PairingDecision> next = source.DecideAsync(
            CreateRequest(nextPeer, "222222")).AsTask();
        DesktopPairingPrompt nextPrompt = Assert.IsType<DesktopPairingPrompt>(
            source.CurrentPrompt);
        Assert.False(source.TryAccept(stalePromptId, CapabilityGrant.None));
        Assert.False(next.IsCompleted);
        Assert.True(source.TryReject(nextPrompt.PromptId));
        Assert.False((await next).Accepted);
    }

    [Fact]
    public async Task InvalidAuthenticationStringCannotOpenPrompt()
    {
        using DeviceIdentity peer = CreatePeer("Peer desk");
        using var source = new DesktopPairingDecisionSource();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await source.DecideAsync(new PairingConfirmationRequest(
                peer.PublicIdentity,
                new ProtocolVersion(1, 0),
                "12A456",
                DateTimeOffset.UtcNow.AddMinutes(1))));

        Assert.Null(source.CurrentPrompt);
    }

    private static PairingConfirmationRequest CreateRequest(
        DeviceIdentity peer,
        string code) => new(
        peer.PublicIdentity,
        new ProtocolVersion(1, 0),
        code,
        DateTimeOffset.UtcNow.AddMinutes(1));

    private static DeviceIdentity CreatePeer(string displayName) =>
        DeviceIdentity.Generate(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            displayName);
}

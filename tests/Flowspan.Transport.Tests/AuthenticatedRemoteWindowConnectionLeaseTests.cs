using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedRemoteWindowConnectionLeaseTests
{
    private static readonly DeviceId LocalDeviceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId PeerDeviceId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task RevocationRegistrationKeepsGenerationAliveUntilCallbacksReturn()
    {
        (SecureFrameSession ownedFrames, SecureFrameSession counterpartFrames) =
            CreateSecureSessions();
        using (counterpartFrames)
        await using (var routes = new RemoteWindowMediaRouteRegistry())
        await using (var mediaSession = new AuthenticatedRemoteWindowMediaSession(
            LocalDeviceId,
            PeerDeviceId,
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            routes,
            ownedFrames))
        {
            var generation = new RemoteWindowConnectionGeneration(value: 1);
            Assert.True(generation.TryAcquire(
                new UnusedPreparationChannel(),
                mediaSession,
                out AuthenticatedRemoteWindowConnectionLease? acquired));
            AuthenticatedRemoteWindowConnectionLease lease = Assert.IsType<
                AuthenticatedRemoteWindowConnectionLease>(acquired);
            bool callbackObservedRevocation = false;
            using CancellationTokenRegistration registration =
                lease.RegisterRevocationCallback(() =>
                {
                    lease.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    callbackObservedRevocation = lease.IsRevoked;
                });

            Exception? failure = generation.RevokeAndReleaseOwner();

            Assert.Null(failure);
            Assert.True(callbackObservedRevocation);
            Assert.True(lease.IsRevoked);
        }
    }

    [Fact]
    public void CompletedRevocationRegistrationReleasesOwnerFromCallerContext()
    {
        WeakReference owner = CreateCompletedRevocationRegistration();

        AssertCollected(owner);
    }

    [Fact]
    public async Task ReturnedCallbackContextIsInactiveWhileAnotherCallbackRuns()
    {
        var owner = new object();
        var generation = new RemoteWindowConnectionGeneration(
            value: 1,
            owner);
        var copiedContextReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCopiedContext = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var copiedContextIsActive = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var blockingCallbackStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlockingCallback = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration blocking =
            generation.RegisterRevocationCallback(() =>
            {
                blockingCallbackStarted.TrySetResult();
                releaseBlockingCallback.Task.GetAwaiter().GetResult();
            });
        using CancellationTokenRegistration copying =
            generation.RegisterRevocationCallback(() =>
                _ = ObserveFromCopiedContextAsync());
        Task<Exception?> revoking = Task.Run(
            generation.RevokeAndReleaseOwner);

        try
        {
            await copiedContextReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await blockingCallbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseCopiedContext.TrySetResult();

            Assert.False(await copiedContextIsActive.Task.WaitAsync(
                TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseCopiedContext.TrySetResult();
            releaseBlockingCallback.TrySetResult();
        }

        Assert.Null(await revoking.WaitAsync(TimeSpan.FromSeconds(5)));

        async Task ObserveFromCopiedContextAsync()
        {
            copiedContextReady.TrySetResult();
            await releaseCopiedContext.Task;
            copiedContextIsActive.TrySetResult(
                RemoteWindowConnectionGeneration.IsActiveRevocationCallback(
                    owner));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateCompletedRevocationRegistration()
    {
        var owner = new object();
        var weakOwner = new WeakReference(owner);
        var generation = new RemoteWindowConnectionGeneration(
            value: 1,
            owner);
        using CancellationTokenRegistration registration =
            generation.RegisterRevocationCallback(static () => { });

        Assert.Null(generation.RevokeAndReleaseOwner());
        GC.KeepAlive(owner);
        return weakOwner;
    }

    private static void AssertCollected(WeakReference reference)
    {
        for (int attempt = 0; attempt < 3 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(reference.IsAlive);
    }

    private static (SecureFrameSession Owned, SecureFrameSession Counterpart)
        CreateSecureSessions()
    {
        byte[] secret = SHA256.HashData(BitConverter.GetBytes(0x75));
        byte[] transcriptHash = SHA256.HashData(
            Encoding.ASCII.GetBytes("connection-generation-revocation"));
        using SecureSessionKeyMaterial material =
            SecureSessionKeyMaterial.DeriveRemoteWindowMedia(
                secret,
                transcriptHash);
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(transcriptHash);
        return (
            material.CreateSession(SecureSessionRole.Initiator),
            material.CreateSession(SecureSessionRole.Responder));
    }

    private sealed class UnusedPreparationChannel :
        IRemoteWindowPreparationChannel
    {
        public DeviceId ParticipantDeviceId => PeerDeviceId;

        public ValueTask<RemoteWindowPreparationDeliveryResult> PrepareAsync(
            RemoteWindowPreparationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask PublishAdmissionStateAsync(
            RemoteWindowParticipantState state,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}

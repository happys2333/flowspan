using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class PairingCeremonyTests
{
    private static readonly CapabilityGrant InitiatorGrant =
        CapabilityGrant.Of(Capability.ActivityReceive);
    private static readonly CapabilityGrant ResponderGrant =
        CapabilityGrant.Of(Capability.MirrorView);

    [Fact]
    public async Task DualAcceptancePersistsDirectionalLocalTrust()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var initiatorTrust = new InMemoryTrustStore();
        var responderTrust = new InMemoryTrustStore();
        var initiatorDecision = new RecordingDecisionSource(
            new PairingDecision(accepted: true, InitiatorGrant));
        var responderDecision = new RecordingDecisionSource(
            new PairingDecision(accepted: true, ResponderGrant));
        (InMemoryPairingChannel initiatorChannel, InMemoryPairingChannel responderChannel) =
            InMemoryPairingChannel.CreatePair();
        PairingCeremonyProfile profile = CreateProfile();

        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            profile,
            initiatorDecision,
            initiatorTrust).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();
        Task<PairingCeremonyResult> responding = new PairingCeremony(
            profile,
            responderDecision,
            responderTrust).RunResponderAsync(
                responderChannel,
                responderIdentity).AsTask();
        PairingCeremonyResult[] results = await Task.WhenAll(initiating, responding)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.All(results, static result => Assert.True(result.Succeeded));
        Assert.All(
            results,
            static result => Assert.Equal(PairingFailure.None, result.Failure));
        Assert.All(
            results,
            static result => Assert.Equal(
                TrustRegistrationResult.Added,
                result.TrustRegistration));
        Assert.True(initiatorTrust.TryGet(
            responderIdentity.DeviceId,
            out TrustRecord? trustedResponder));
        Assert.True(trustedResponder.GrantedCapabilities.Allows(
            Capability.ActivityReceive));
        Assert.True(responderTrust.TryGet(
            initiatorIdentity.DeviceId,
            out TrustRecord? trustedInitiator));
        Assert.True(trustedInitiator.GrantedCapabilities.Allows(Capability.MirrorView));

        PairingConfirmationRequest initiatorRequest =
            Assert.Single(initiatorDecision.Requests);
        PairingConfirmationRequest responderRequest =
            Assert.Single(responderDecision.Requests);
        Assert.Equal(
            initiatorRequest.ShortAuthenticationString,
            responderRequest.ShortAuthenticationString);
        Assert.Equal(responderIdentity.DeviceId, initiatorRequest.PeerIdentity.DeviceId);
        Assert.Equal(initiatorIdentity.DeviceId, responderRequest.PeerIdentity.DeviceId);
    }

    [Fact]
    public async Task PeerRejectionCancelsPendingPromptAndPersistsNoTrust()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var initiatorTrust = new InMemoryTrustStore();
        var responderTrust = new InMemoryTrustStore();
        var pendingResponderDecision = new BlockingDecisionSource();
        (InMemoryPairingChannel initiatorChannel, InMemoryPairingChannel responderChannel) =
            InMemoryPairingChannel.CreatePair();

        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            CreateProfile(),
            new RecordingDecisionSource(PairingDecision.Reject),
            initiatorTrust).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();
        Task<PairingCeremonyResult> responding = new PairingCeremony(
            CreateProfile(),
            pendingResponderDecision,
            responderTrust).RunResponderAsync(
                responderChannel,
                responderIdentity).AsTask();
        PairingCeremonyResult[] results = await Task.WhenAll(initiating, responding)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.All(
            results,
            static result => Assert.Equal(PairingFailure.Rejected, result.Failure));
        Assert.True(pendingResponderDecision.CancellationObserved);
        Assert.False(initiatorTrust.TryGet(responderIdentity.DeviceId, out _));
        Assert.False(responderTrust.TryGet(initiatorIdentity.DeviceId, out _));
    }

    [Fact]
    public async Task NoCommonVersionSkipsConfirmationAndTrust()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var initiatorTrust = new InMemoryTrustStore();
        var responderTrust = new InMemoryTrustStore();
        var initiatorDecision = new ThrowingDecisionSource();
        var responderDecision = new ThrowingDecisionSource();
        (InMemoryPairingChannel initiatorChannel, InMemoryPairingChannel responderChannel) =
            InMemoryPairingChannel.CreatePair();

        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            new PairingCeremonyProfile([new ProtocolVersion(1, 0)]),
            initiatorDecision,
            initiatorTrust).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();
        Task<PairingCeremonyResult> responding = new PairingCeremony(
            new PairingCeremonyProfile([new ProtocolVersion(2, 0)]),
            responderDecision,
            responderTrust).RunResponderAsync(
                responderChannel,
                responderIdentity).AsTask();
        PairingCeremonyResult[] results = await Task.WhenAll(initiating, responding)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.All(
            results,
            static result => Assert.Equal(
                PairingFailure.NoCommonProtocolVersion,
                result.Failure));
        Assert.Equal(0, initiatorDecision.CallCount);
        Assert.Equal(0, responderDecision.CallCount);
        Assert.False(initiatorTrust.TryGet(responderIdentity.DeviceId, out _));
        Assert.False(responderTrust.TryGet(initiatorIdentity.DeviceId, out _));
    }

    [Fact]
    public async Task AlteredInitiatorSignatureIsRejectedBeforeResponderPrompt()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity previouslyTrustedIdentity = DeviceIdentity.Generate(
            initiatorIdentity.DeviceId,
            "Laptop");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var initiatorTrust = new InMemoryTrustStore();
        var responderTrust = new InMemoryTrustStore();
        responderTrust.Register(new TrustRecord(
            previouslyTrustedIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None));
        var responderDecision = new ThrowingDecisionSource();
        (InMemoryPairingChannel initiatorChannel, InMemoryPairingChannel responderChannel) =
            InMemoryPairingChannel.CreatePair(
                mutateFirstToSecond: static message =>
                {
                    byte[] mutated = message.ToArray();
                    if (mutated.Length > 5 && mutated[4] == 2)
                    {
                        mutated[^1] ^= 0x80;
                    }

                    return mutated;
                });

        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            CreateProfile(),
            new RecordingDecisionSource(
                new PairingDecision(accepted: true, InitiatorGrant)),
            initiatorTrust).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();
        PairingCeremonyResult responderResult = await new PairingCeremony(
            CreateProfile(),
            responderDecision,
            responderTrust).RunResponderAsync(
                responderChannel,
                responderIdentity);

        Assert.Equal(
            PairingFailure.InvalidTranscriptSignature,
            responderResult.Failure);
        Assert.Equal(0, responderDecision.CallCount);
        await Assert.ThrowsAnyAsync<Exception>(() => initiating);
        Assert.False(initiatorTrust.TryGet(responderIdentity.DeviceId, out _));
        Assert.True(responderTrust.TryGet(
            initiatorIdentity.DeviceId,
            out TrustRecord? preserved));
        Assert.True(preserved.PeerIdentity.HasSameKey(
            previouslyTrustedIdentity.PublicIdentity));
    }

    [Fact]
    public async Task AlteredConfirmationPersistsNoTrust()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var initiatorTrust = new InMemoryTrustStore();
        var responderTrust = new InMemoryTrustStore();
        (InMemoryPairingChannel initiatorChannel, InMemoryPairingChannel responderChannel) =
            InMemoryPairingChannel.CreatePair(
                mutateFirstToSecond: static message =>
                {
                    byte[] mutated = message.ToArray();
                    if (mutated.Length > 5 && mutated[4] == 3)
                    {
                        mutated[^1] ^= 0x40;
                    }

                    return mutated;
                });
        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            CreateProfile(),
            new RecordingDecisionSource(
                new PairingDecision(accepted: true, InitiatorGrant)),
            initiatorTrust).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();

        PairingCeremonyResult responderResult = await new PairingCeremony(
            CreateProfile(),
            new RecordingDecisionSource(
                new PairingDecision(accepted: true, ResponderGrant)),
            responderTrust).RunResponderAsync(
                responderChannel,
                responderIdentity);

        Assert.Equal(PairingFailure.InvalidConfirmation, responderResult.Failure);
        await Assert.ThrowsAnyAsync<Exception>(() => initiating);
        Assert.False(initiatorTrust.TryGet(responderIdentity.DeviceId, out _));
        Assert.False(responderTrust.TryGet(initiatorIdentity.DeviceId, out _));
    }

    [Fact]
    public async Task AlteredCompletionProofPersistsNoTrust()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var initiatorTrust = new InMemoryTrustStore();
        var responderTrust = new InMemoryTrustStore();
        (InMemoryPairingChannel initiatorChannel, InMemoryPairingChannel responderChannel) =
            InMemoryPairingChannel.CreatePair(
                mutateFirstToSecond: static message =>
                {
                    byte[] mutated = message.ToArray();
                    if (mutated.Length > 5 && mutated[4] == 4)
                    {
                        mutated[^1] ^= 0x20;
                    }

                    return mutated;
                });
        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            CreateProfile(),
            new RecordingDecisionSource(
                new PairingDecision(accepted: true, InitiatorGrant)),
            initiatorTrust).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();

        PairingCeremonyResult responderResult = await new PairingCeremony(
            CreateProfile(),
            new RecordingDecisionSource(
                new PairingDecision(accepted: true, ResponderGrant)),
            responderTrust).RunResponderAsync(
                responderChannel,
                responderIdentity);

        Assert.Equal(
            PairingFailure.InvalidCompletionProof,
            responderResult.Failure);
        await Assert.ThrowsAnyAsync<Exception>(() => initiating);
        Assert.False(initiatorTrust.TryGet(responderIdentity.DeviceId, out _));
        Assert.False(responderTrust.TryGet(initiatorIdentity.DeviceId, out _));
    }

    [Fact]
    public async Task ExistingDifferentKeyIsNotOverwritten()
    {
        DeviceId initiatorId =
            DeviceId.Parse("11111111-1111-1111-1111-111111111111");
        using DeviceIdentity initiatorIdentity = DeviceIdentity.Generate(
            initiatorId,
            "Laptop");
        using DeviceIdentity oldInitiatorIdentity = DeviceIdentity.Generate(
            initiatorId,
            "Laptop");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var responderTrust = new InMemoryTrustStore();
        responderTrust.Register(new TrustRecord(
            oldInitiatorIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.None));
        (InMemoryPairingChannel initiatorChannel, InMemoryPairingChannel responderChannel) =
            InMemoryPairingChannel.CreatePair();
        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            CreateProfile(),
            new ThrowingDecisionSource(),
            new InMemoryTrustStore()).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();

        PairingCeremonyResult result = await new PairingCeremony(
            CreateProfile(),
            new ThrowingDecisionSource(),
            responderTrust).RunResponderAsync(
                responderChannel,
                responderIdentity);

        Assert.Equal(PairingFailure.IdentityChanged, result.Failure);
        Assert.True(responderTrust.TryGet(initiatorId, out TrustRecord? preserved));
        Assert.True(preserved.PeerIdentity.HasSameKey(
            oldInitiatorIdentity.PublicIdentity));
        await Assert.ThrowsAnyAsync<Exception>(() => initiating);
    }

    [Fact]
    public async Task WholeCeremonyTimeoutUsesInjectedTimeAndPersistsNoTrust()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        var trust = new InMemoryTrustStore();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var channel = new NeverReceivingChannel();
        var profile = new PairingCeremonyProfile(
            [new ProtocolVersion(1, 0)],
            timeout: TimeSpan.FromMinutes(2));
        Task<PairingCeremonyResult> running = new PairingCeremony(
            profile,
            new ThrowingDecisionSource(),
            trust,
            time).RunInitiatorAsync(channel, identity).AsTask();
        await channel.MessageSent.Task.WaitAsync(TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromMinutes(2));
        PairingCeremonyResult result = await running.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(PairingFailure.Timeout, result.Failure);
        Assert.True(channel.Disposed);
        Assert.False(trust.TryGet(
            DeviceId.Parse("22222222-2222-2222-2222-222222222222"),
            out _));
    }

    [Fact]
    public async Task ConfirmationPromptTimeoutCancelsBothSidesBeforeTrust()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var initiatorTrust = new InMemoryTrustStore();
        var responderTrust = new InMemoryTrustStore();
        var pendingDecision = new BlockingDecisionSource();
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var profile = new PairingCeremonyProfile(
            [new ProtocolVersion(1, 0)],
            timeout: TimeSpan.FromMinutes(2));
        (InMemoryPairingChannel initiatorChannel, InMemoryPairingChannel responderChannel) =
            InMemoryPairingChannel.CreatePair();
        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            profile,
            new RecordingDecisionSource(
                new PairingDecision(accepted: true, InitiatorGrant)),
            initiatorTrust,
            time).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();
        Task<PairingCeremonyResult> responding = new PairingCeremony(
            profile,
            pendingDecision,
            responderTrust,
            time).RunResponderAsync(
                responderChannel,
                responderIdentity).AsTask();
        await pendingDecision.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromMinutes(2));
        PairingCeremonyResult[] results = await Task.WhenAll(initiating, responding)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.All(
            results,
            static result => Assert.Equal(PairingFailure.Timeout, result.Failure));
        Assert.True(pendingDecision.CancellationObserved);
        Assert.False(initiatorTrust.TryGet(responderIdentity.DeviceId, out _));
        Assert.False(responderTrust.TryGet(initiatorIdentity.DeviceId, out _));
    }

    [Fact]
    public async Task TrustPersistenceFailureIsVisibleAndNotReportedAsLocalSuccess()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var initiatorTrust = new FailingRegistrationTrustStore();
        var responderTrust = new InMemoryTrustStore();
        (InMemoryPairingChannel initiatorChannel, InMemoryPairingChannel responderChannel) =
            InMemoryPairingChannel.CreatePair();
        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            CreateProfile(),
            new RecordingDecisionSource(
                new PairingDecision(accepted: true, InitiatorGrant)),
            initiatorTrust).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();
        Task<PairingCeremonyResult> responding = new PairingCeremony(
            CreateProfile(),
            new RecordingDecisionSource(
                new PairingDecision(accepted: true, ResponderGrant)),
            responderTrust).RunResponderAsync(
                responderChannel,
                responderIdentity).AsTask();

        InvalidDataException failure = await Assert.ThrowsAsync<InvalidDataException>(
            () => initiating);
        PairingCeremonyResult responderResult = await responding;

        Assert.Equal("trust save failed", failure.Message);
        Assert.True(responderResult.Succeeded);
        Assert.False(initiatorTrust.TryGet(responderIdentity.DeviceId, out _));
        Assert.True(responderTrust.TryGet(initiatorIdentity.DeviceId, out _));
    }

    [Fact]
    public async Task SameKeyAlreadyTrustedDoesNotSilentlyReplaceCapabilities()
    {
        using DeviceIdentity initiatorIdentity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        using DeviceIdentity responderIdentity = CreateIdentity(
            "22222222-2222-2222-2222-222222222222",
            "Desk");
        var initiatorTrust = new InMemoryTrustStore();
        initiatorTrust.Register(new TrustRecord(
            responderIdentity.PublicIdentity,
            DateTimeOffset.UnixEpoch,
            CapabilityGrant.Of(Capability.MirrorView)));
        var responderTrust = new InMemoryTrustStore();
        (InMemoryPairingChannel initiatorChannel, InMemoryPairingChannel responderChannel) =
            InMemoryPairingChannel.CreatePair();
        Task<PairingCeremonyResult> initiating = new PairingCeremony(
            CreateProfile(),
            new RecordingDecisionSource(
                new PairingDecision(accepted: true, InitiatorGrant)),
            initiatorTrust).RunInitiatorAsync(
                initiatorChannel,
                initiatorIdentity).AsTask();
        Task<PairingCeremonyResult> responding = new PairingCeremony(
            CreateProfile(),
            new RecordingDecisionSource(
                new PairingDecision(accepted: true, ResponderGrant)),
            responderTrust).RunResponderAsync(
                responderChannel,
                responderIdentity).AsTask();

        PairingCeremonyResult initiatorResult = await initiating;
        PairingCeremonyResult responderResult = await responding;

        Assert.True(initiatorResult.Succeeded);
        Assert.Equal(
            TrustRegistrationResult.AlreadyTrusted,
            initiatorResult.TrustRegistration);
        Assert.True(responderResult.Succeeded);
        Assert.True(initiatorTrust.TryGet(
            responderIdentity.DeviceId,
            out TrustRecord? preserved));
        Assert.True(preserved.GrantedCapabilities.Allows(Capability.MirrorView));
        Assert.False(preserved.GrantedCapabilities.Allows(
            Capability.ActivityReceive));
    }

    [Fact]
    public async Task ProtocolAndChannelCleanupFailuresAreBothPreserved()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() =>
            new PairingCeremony(
                CreateProfile(),
                new ThrowingDecisionSource(),
                new InMemoryTrustStore()).RunInitiatorAsync(
                    new ReceiveAndDisposeFailingChannel(),
                    identity).AsTask());

        Assert.Collection(
            failure.InnerExceptions,
            exception => Assert.Equal("receive failed", exception.Message),
            exception => Assert.Equal("dispose failed", exception.Message));
    }

    [Fact]
    public async Task MalformedPeerFrameIsAProtocolResultNotALocalStorageError()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");

        PairingCeremonyResult result = await new PairingCeremony(
            CreateProfile(),
            new ThrowingDecisionSource(),
            new InMemoryTrustStore()).RunInitiatorAsync(
                new MalformedMessageChannel(),
                identity);

        Assert.Equal(PairingFailure.InvalidMessage, result.Failure);
    }

    [Fact]
    public async Task CallerCancellationPropagatesAndDisposesChannel()
    {
        using DeviceIdentity identity = CreateIdentity(
            "11111111-1111-1111-1111-111111111111",
            "Laptop");
        var channel = new NeverReceivingChannel();
        using var cancellation = new CancellationTokenSource();
        Task<PairingCeremonyResult> running = new PairingCeremony(
            CreateProfile(),
            new ThrowingDecisionSource(),
            new InMemoryTrustStore()).RunInitiatorAsync(
                channel,
                identity,
                cancellation.Token).AsTask();
        await channel.MessageSent.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.True(channel.Disposed);
    }

    [Fact]
    public void ProfileEnforcesDefaultAndHardTimeoutLimits()
    {
        var defaultProfile = new PairingCeremonyProfile(
            [new ProtocolVersion(1, 0)]);

        Assert.Equal(PairingCeremonyProfile.DefaultTimeout, defaultProfile.Timeout);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PairingCeremonyProfile(
                [new ProtocolVersion(1, 0)],
                PairingCeremonyProfile.MaximumTimeout + TimeSpan.FromTicks(1)));
    }

    private static DeviceIdentity CreateIdentity(string id, string name) =>
        DeviceIdentity.Generate(DeviceId.Parse(id), name);

    private static PairingCeremonyProfile CreateProfile() => new(
        [new ProtocolVersion(1, 0)],
        timeout: TimeSpan.FromSeconds(5));

    private sealed class BlockingDecisionSource : IPairingDecisionSource
    {
        public bool CancellationObserved { get; private set; }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PairingDecision> DecideAsync(
            PairingConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable pairing prompt.");
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }

    private sealed class FailingRegistrationTrustStore : ITrustStore
    {
        private readonly InMemoryTrustStore inner = new();

        public SecretStoreProtection Protection => inner.Protection;

        public bool Allows(DeviceId peerDeviceId, Capability capability) =>
            inner.Allows(peerDeviceId, capability);

        public ValueTask<TrustRegistrationResult> RegisterAsync(
            TrustRecord trustRecord,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidDataException("trust save failed");
        }

        public ValueTask<bool> RevokeAsync(
            DeviceId peerDeviceId,
            CancellationToken cancellationToken = default) =>
            inner.RevokeAsync(peerDeviceId, cancellationToken);

        public bool TryGet(
            DeviceId peerDeviceId,
            [NotNullWhen(true)] out TrustRecord? trustRecord) =>
            inner.TryGet(peerDeviceId, out trustRecord);

        public ValueTask<bool> TryUpdateCapabilitiesAsync(
            DeviceId peerDeviceId,
            string expectedFingerprint,
            CapabilityGrant capabilities,
            CancellationToken cancellationToken = default) =>
            inner.TryUpdateCapabilitiesAsync(
                peerDeviceId,
                expectedFingerprint,
                capabilities,
                cancellationToken);
    }

    private sealed class RecordingDecisionSource(PairingDecision decision) :
        IPairingDecisionSource
    {
        private readonly ConcurrentQueue<PairingConfirmationRequest> requests = [];

        public IEnumerable<PairingConfirmationRequest> Requests => requests;

        public ValueTask<PairingDecision> DecideAsync(
            PairingConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests.Enqueue(request);
            return ValueTask.FromResult(decision);
        }
    }

    private sealed class ThrowingDecisionSource : IPairingDecisionSource
    {
        public int CallCount { get; private set; }

        public ValueTask<PairingDecision> DecideAsync(
            PairingConfirmationRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("The pairing prompt must not run.");
        }
    }

    private sealed class InMemoryPairingChannel : IPairingMessageChannel
    {
        private readonly ChannelReader<byte[]> inbound;
        private readonly Func<ReadOnlyMemory<byte>, byte[]> mutate;
        private readonly ChannelWriter<byte[]> outbound;
        private int disposed;

        private InMemoryPairingChannel(
            ChannelReader<byte[]> inbound,
            ChannelWriter<byte[]> outbound,
            Func<ReadOnlyMemory<byte>, byte[]> mutate)
        {
            this.inbound = inbound;
            this.outbound = outbound;
            this.mutate = mutate;
        }

        public static (InMemoryPairingChannel First, InMemoryPairingChannel Second)
            CreatePair(
                Func<ReadOnlyMemory<byte>, byte[]>? mutateFirstToSecond = null,
                Func<ReadOnlyMemory<byte>, byte[]>? mutateSecondToFirst = null)
        {
            Channel<byte[]> firstInbound = Channel.CreateUnbounded<byte[]>();
            Channel<byte[]> secondInbound = Channel.CreateUnbounded<byte[]>();
            return (
                new InMemoryPairingChannel(
                    firstInbound.Reader,
                    secondInbound.Writer,
                    mutateFirstToSecond ?? (static message => message.ToArray())),
                new InMemoryPairingChannel(
                    secondInbound.Reader,
                    firstInbound.Writer,
                    mutateSecondToFirst ?? (static message => message.ToArray())));
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                outbound.TryComplete();
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]> ReceiveAsync(
            CancellationToken cancellationToken = default) =>
            inbound.ReadAsync(cancellationToken);

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> message,
            CancellationToken cancellationToken = default) =>
            outbound.WriteAsync(mutate(message), cancellationToken);
    }

    private sealed class NeverReceivingChannel : IPairingMessageChannel
    {
        public bool Disposed { get; private set; }

        public TaskCompletionSource MessageSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public async ValueTask<byte[]> ReceiveAsync(
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable pairing receive.");
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MessageSent.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MalformedMessageChannel : IPairingMessageChannel
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<byte[]> ReceiveAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<byte[]>([0x00]);
        }

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReceiveAndDisposeFailingChannel : IPairingMessageChannel
    {
        public ValueTask DisposeAsync() =>
            ValueTask.FromException(new InvalidOperationException("dispose failed"));

        public ValueTask<byte[]> ReceiveAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<byte[]>(new IOException("receive failed"));

        public ValueTask SendAsync(
            ReadOnlyMemory<byte> message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = utcNow;

        public void Advance(TimeSpan elapsed)
        {
            List<ManualTimer> candidates;
            DateTimeOffset now;
            lock (gate)
            {
                utcNow = utcNow.Add(elapsed);
                now = utcNow;
                candidates = timers.ToList();
            }

            foreach (ManualTimer timer in candidates.Where(timer => timer.IsDue(now)))
            {
                timer.Fire(now);
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            lock (gate)
            {
                timers.Add(timer);
            }

            return timer;
        }

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            private DateTimeOffset dueAt = DateTimeOffset.MaxValue;
            private bool disposed;
            private TimeSpan period = Timeout.InfiniteTimeSpan;

            public bool Change(TimeSpan dueTime, TimeSpan newPeriod)
            {
                lock (owner.gate)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    dueAt = dueTime == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : owner.utcNow.Add(dueTime);
                    period = newPeriod;
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner.gate)
                {
                    disposed = true;
                    owner.timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    if (disposed || dueAt > now)
                    {
                        return;
                    }

                    dueAt = period == Timeout.InfiniteTimeSpan
                        ? DateTimeOffset.MaxValue
                        : now.Add(period);
                }

                callback(state);
            }

            public bool IsDue(DateTimeOffset now)
            {
                lock (owner.gate)
                {
                    return !disposed && dueAt <= now;
                }
            }
        }
    }
}

using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class DnsSdPeerAdvertisementServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PublishesImmediatelyRefreshesAndWithdrawsOnCancellation()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        var publisher = new RecordingPublisher();
        var time = new MutableTimeProvider(Now);
        var delay = new RefreshOnceThenBlockDelay(() =>
            time.UtcNow = Now.AddSeconds(40));
        int nonce = 0;
        var service = new DnsSdPeerAdvertisementService(
            identity,
            4747,
            [new ProtocolVersion(1, 1), new ProtocolVersion(1, 0)],
            publisher,
            delay,
            time,
            offerLifetime: TimeSpan.FromSeconds(80),
            refreshInterval: TimeSpan.FromSeconds(40),
            nextNonce: () => Enumerable.Repeat(
                    checked((byte)++nonce),
                    SignedDiscoveryOffer.NonceLength)
                .ToArray());
        using var cancellation = new CancellationTokenSource();

        Task run = service.RunAsync(cancellation.Token).AsTask();

        Assert.Equal(2, publisher.Offers.Count);
        Assert.Equal(Now, publisher.Offers[0].IssuedAt);
        Assert.Equal(Now.AddSeconds(40), publisher.Offers[1].IssuedAt);
        Assert.Equal<ProtocolVersion>(
            [new ProtocolVersion(1, 0), new ProtocolVersion(1, 1)],
            publisher.Offers[0].ProtocolVersions);
        Assert.NotEqual(
            publisher.Offers[0].OfferDigest,
            publisher.Offers[1].OfferDigest);
        Assert.All(publisher.Offers, offer =>
            Assert.True(offer.Verify(identity.PublicIdentity, offer.IssuedAt)));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.Equal(1, publisher.WithdrawCount);
    }

    [Fact]
    public async Task PublishFailureIsSurfacedAfterWithdrawalAttempt()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        var publisher = new RecordingPublisher
        {
            PublishException = new IOException("publish failed"),
        };
        var service = new DnsSdPeerAdvertisementService(
            identity,
            4747,
            [new ProtocolVersion(1, 0)],
            publisher,
            new NeverAdvertisementDelay(),
            new MutableTimeProvider(Now));

        IOException failure = await Assert.ThrowsAsync<IOException>(() =>
            service.RunAsync().AsTask());

        Assert.Equal("publish failed", failure.Message);
        Assert.Equal(1, publisher.WithdrawCount);
    }

    [Fact]
    public async Task PublishAndWithdrawFailuresAreBothPreserved()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        var publisher = new RecordingPublisher
        {
            PublishException = new IOException("publish failed"),
            WithdrawException = new InvalidOperationException("withdraw failed"),
        };
        var service = new DnsSdPeerAdvertisementService(
            identity,
            4747,
            [new ProtocolVersion(1, 0)],
            publisher,
            new NeverAdvertisementDelay(),
            new MutableTimeProvider(Now));

        AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() =>
            service.RunAsync().AsTask());

        Assert.Collection(
            failure.InnerExceptions,
            exception => Assert.Equal("publish failed", exception.Message),
            exception => Assert.Equal("withdraw failed", exception.Message));
    }

    [Fact]
    public async Task ConcurrentRunIsRejectedAndServiceCanRestartAfterCancellation()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        var publisher = new RecordingPublisher();
        var service = new DnsSdPeerAdvertisementService(
            identity,
            4747,
            [new ProtocolVersion(1, 0)],
            publisher,
            new NeverAdvertisementDelay(),
            new MutableTimeProvider(Now));
        using var firstCancellation = new CancellationTokenSource();
        Task first = service.RunAsync(firstCancellation.Token).AsTask();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RunAsync().AsTask());

        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        using var secondCancellation = new CancellationTokenSource();
        Task second = service.RunAsync(secondCancellation.Token).AsTask();
        secondCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(2, publisher.WithdrawCount);
    }

    [Fact]
    public void ConstructorRejectsUnsafeTimingAndProtocolConfiguration()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(
            DeviceId.Parse("11111111-1111-1111-1111-111111111111"),
            "Laptop");
        var publisher = new RecordingPublisher();
        var delay = new NeverAdvertisementDelay();

        Assert.Throws<ArgumentException>(() => new DnsSdPeerAdvertisementService(
            identity,
            4747,
            [],
            publisher,
            delay));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DnsSdPeerAdvertisementService(
                identity,
                4747,
                [new ProtocolVersion(1, 0)],
                publisher,
                delay,
                offerLifetime: TimeSpan.FromSeconds(30),
                refreshInterval: TimeSpan.FromSeconds(30)));
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class NeverAdvertisementDelay : IDnsSdAdvertisementDelay
    {
        public ValueTask WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default) =>
            new(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));
    }

    private sealed class RecordingPublisher : IDnsSdServicePublisher
    {
        public List<SignedDiscoveryOffer> Offers { get; } = [];

        public Exception? PublishException { get; init; }

        public int WithdrawCount { get; private set; }

        public Exception? WithdrawException { get; init; }

        public void Publish(SignedDiscoveryOffer offer)
        {
            if (PublishException is not null)
            {
                throw PublishException;
            }

            Offers.Add(offer);
        }

        public void Withdraw()
        {
            WithdrawCount++;
            if (WithdrawException is not null)
            {
                throw WithdrawException;
            }
        }
    }

    private sealed class RefreshOnceThenBlockDelay(Action beforeRefresh) :
        IDnsSdAdvertisementDelay
    {
        private int calls;

        public ValueTask WaitAsync(
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                beforeRefresh();
                return ValueTask.CompletedTask;
            }

            return new ValueTask(Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken));
        }
    }
}

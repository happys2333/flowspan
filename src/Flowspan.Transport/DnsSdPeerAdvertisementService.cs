using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Transport;

public interface IDnsSdServicePublisher
{
    public void Publish(SignedDiscoveryOffer offer);

    public void Withdraw();
}

public interface IDnsSdAdvertisementDelay
{
    public ValueTask WaitAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default);
}

public sealed class SystemDnsSdAdvertisementDelay : IDnsSdAdvertisementDelay
{
    public ValueTask WaitAsync(
        TimeSpan delay,
        CancellationToken cancellationToken = default) =>
        new(Task.Delay(delay, cancellationToken));
}

public sealed class DnsSdPeerAdvertisementService
{
    public static readonly TimeSpan DefaultOfferLifetime = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(45);
    private readonly IDnsSdAdvertisementDelay delay;
    private readonly DeviceIdentity identity;
    private readonly Func<byte[]> nextNonce;
    private readonly TimeSpan offerLifetime;
    private readonly ushort port;
    private readonly ImmutableArray<ProtocolVersion> protocolVersions;
    private readonly IDnsSdServicePublisher publisher;
    private readonly TimeSpan refreshInterval;
    private readonly TimeProvider timeProvider;
    private int running;

    public DnsSdPeerAdvertisementService(
        DeviceIdentity identity,
        int port,
        IEnumerable<ProtocolVersion> protocolVersions,
        IDnsSdServicePublisher publisher,
        IDnsSdAdvertisementDelay delay,
        TimeProvider? timeProvider = null,
        TimeSpan? offerLifetime = null,
        TimeSpan? refreshInterval = null,
        Func<byte[]>? nextNonce = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(protocolVersions);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(delay);
        if (port is < 1 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        ImmutableArray<ProtocolVersion> versions = protocolVersions
            .Distinct()
            .Order()
            .ToImmutableArray();
        if (versions.IsDefaultOrEmpty
            || versions.Length > 16
            || versions.Any(static version => version.Major < 1 || version.Minor < 0))
        {
            throw new ArgumentException(
                "A DNS-SD advertisement must contain 1 to 16 initialized protocol versions.",
                nameof(protocolVersions));
        }

        TimeSpan lifetime = offerLifetime ?? DefaultOfferLifetime;
        TimeSpan refresh = refreshInterval ?? DefaultRefreshInterval;
        if (lifetime <= TimeSpan.Zero
            || lifetime > SignedDiscoveryOffer.MaximumLifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offerLifetime),
                "The DNS-SD offer lifetime is outside the signed-offer limit.");
        }

        if (refresh <= TimeSpan.Zero || refresh >= lifetime)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refreshInterval),
                "The DNS-SD refresh interval must be positive and shorter than the offer lifetime.");
        }

        this.identity = identity;
        this.port = checked((ushort)port);
        this.protocolVersions = versions;
        this.publisher = publisher;
        this.delay = delay;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.offerLifetime = lifetime;
        this.refreshInterval = refresh;
        this.nextNonce = nextNonce ?? CreateNonce;
    }

    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "A DNS-SD advertisement service can run only one loop at a time.");
        }

        Exception? runFailure = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] nonce = nextNonce();
                SignedDiscoveryOffer offer = SignedDiscoveryOffer.Create(
                    identity,
                    port,
                    protocolVersions,
                    timeProvider.GetUtcNow(),
                    offerLifetime,
                    nonce);
                publisher.Publish(offer);
                await delay.WaitAsync(refreshInterval, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            runFailure = exception;
        }

        Exception? withdrawFailure = null;
        try
        {
            publisher.Withdraw();
        }
        catch (Exception exception)
        {
            withdrawFailure = exception;
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }

        if (runFailure is not null && withdrawFailure is not null)
        {
            throw new AggregateException(
                "The DNS-SD advertisement loop and withdrawal both failed.",
                runFailure,
                withdrawFailure);
        }

        if (withdrawFailure is not null)
        {
            ExceptionDispatchInfo.Capture(withdrawFailure).Throw();
            throw new UnreachableException();
        }

        ExceptionDispatchInfo.Capture(runFailure
            ?? new InvalidOperationException(
                "The DNS-SD advertisement loop ended without a failure or cancellation."))
            .Throw();
        throw new UnreachableException();
    }

    private static byte[] CreateNonce() =>
        RandomNumberGenerator.GetBytes(SignedDiscoveryOffer.NonceLength);
}

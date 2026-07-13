using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class ReconnectBackoffTests
{
    [Fact]
    public void ExponentialDelayIsBoundedAtConfiguredMaximum()
    {
        var backoff = new ReconnectBackoff(
            TimeSpan.FromMilliseconds(250),
            TimeSpan.FromSeconds(30),
            jitterFraction: 0);

        Assert.Equal(TimeSpan.FromMilliseconds(250), backoff.DelayForAttempt(0, 0.5));
        Assert.Equal(TimeSpan.FromMilliseconds(500), backoff.DelayForAttempt(1, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(1), backoff.DelayForAttempt(2, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(30), backoff.DelayForAttempt(30, 0.5));
        Assert.Equal(TimeSpan.FromSeconds(30), backoff.DelayForAttempt(10_000, 0.5));
    }

    [Fact]
    public void DeterministicJitterStaysWithinConfiguredBounds()
    {
        var backoff = new ReconnectBackoff(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30),
            jitterFraction: 0.2);

        Assert.Equal(TimeSpan.FromMilliseconds(800), backoff.DelayForAttempt(0, 0));
        Assert.Equal(TimeSpan.FromSeconds(1), backoff.DelayForAttempt(0, 0.5));
        Assert.Equal(TimeSpan.FromMilliseconds(1200), backoff.DelayForAttempt(0, 1));
        Assert.Equal(TimeSpan.FromSeconds(30), backoff.DelayForAttempt(30, 1));
    }

    [Fact]
    public void InvalidAttemptsSamplesAndConfigurationAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectBackoff(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            double.NaN));
        var backoff = new ReconnectBackoff(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30));
        Assert.Throws<ArgumentOutOfRangeException>(() => backoff.DelayForAttempt(-1, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => backoff.DelayForAttempt(0, -0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => backoff.DelayForAttempt(0, double.NaN));
    }
}

namespace Flowspan.Transport;

public sealed class ReconnectBackoff
{
    public ReconnectBackoff(
        TimeSpan minimumDelay,
        TimeSpan maximumDelay,
        double jitterFraction = 0.2)
    {
        if (minimumDelay <= TimeSpan.Zero || maximumDelay < minimumDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumDelay),
                "Reconnect delays must be positive and ordered.");
        }

        if (jitterFraction is < 0 or > 0.5 || double.IsNaN(jitterFraction))
        {
            throw new ArgumentOutOfRangeException(
                nameof(jitterFraction),
                "Reconnect jitter must be from 0 to 0.5.");
        }

        MinimumDelay = minimumDelay;
        MaximumDelay = maximumDelay;
        JitterFraction = jitterFraction;
    }

    public TimeSpan MinimumDelay { get; }

    public TimeSpan MaximumDelay { get; }

    public double JitterFraction { get; }

    public TimeSpan DelayForAttempt(int attempt, double jitterSample)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        if (jitterSample is < 0 or > 1 || double.IsNaN(jitterSample))
        {
            throw new ArgumentOutOfRangeException(
                nameof(jitterSample),
                "A jitter sample must be from 0 to 1.");
        }

        double exponent = Math.Min(attempt, 30);
        double baseMilliseconds = Math.Min(
            MaximumDelay.TotalMilliseconds,
            MinimumDelay.TotalMilliseconds * Math.Pow(2, exponent));
        double jitterMultiplier = 1 - JitterFraction + (2 * JitterFraction * jitterSample);
        double jittered = Math.Clamp(
            baseMilliseconds * jitterMultiplier,
            MinimumDelay.TotalMilliseconds * (1 - JitterFraction),
            MaximumDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(jittered);
    }
}

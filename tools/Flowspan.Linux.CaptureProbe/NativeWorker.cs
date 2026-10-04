namespace Flowspan.Linux.CaptureProbe;

internal static class NativeWorker
{
    public static readonly TimeSpan MaximumBudget = TimeSpan.FromSeconds(15);

    public static ProbeResult Run(Func<ProbeResult> operation, TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (budget <= TimeSpan.Zero || budget > MaximumBudget)
        {
            throw new ArgumentOutOfRangeException(nameof(budget));
        }

        ProbeResult? result = null;
        Thread worker = new(() =>
        {
            try
            {
                result = operation();
            }
            catch (Exception)
            {
                result = new(ProbeStatus.Fail, ProbeReason.ManagedFailure, OwnersReleased: false);
            }
        })
        {
            IsBackground = true,
            Name = "Flowspan PipeWire thread-loop ABI probe",
        };
        worker.Start();
        if (!worker.Join(budget))
        {
            // The outer thread has no native owner to dispose. In particular it must
            // not stop/destroy/deinit while the background worker borrows that owner.
            // Program exits nonzero; only process teardown releases a blocked owner.
            return new(ProbeStatus.Fail, ProbeReason.WorkerTimeout, OwnersReleased: false);
        }

        return result ?? new(ProbeStatus.Fail, ProbeReason.ManagedFailure, OwnersReleased: false);
    }
}

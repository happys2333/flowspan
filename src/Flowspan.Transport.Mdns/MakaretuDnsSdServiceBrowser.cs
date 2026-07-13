namespace Flowspan.Transport.Mdns;

public sealed class MakaretuDnsSdServiceBrowser : IDnsSdServiceBrowser
{
    private readonly DnsSdResolutionCache cache = new();
    private readonly Func<IMdnsDiscoveryStack> createStack;
    private readonly Lock gate = new();
    private readonly INetworkChangeSource networkChanges;
    private readonly Lock restartGate = new();
    private StackBinding? binding;
    private IDisposable? networkSubscription;
    private int disposed;
    private int started;

    public MakaretuDnsSdServiceBrowser()
        : this(
            new SystemNetworkChangeSource(),
            static () => new MakaretuMdnsDiscoveryStack())
    {
    }

    internal MakaretuDnsSdServiceBrowser(
        INetworkChangeSource networkChanges,
        Func<IMdnsDiscoveryStack> createStack)
    {
        ArgumentNullException.ThrowIfNull(networkChanges);
        ArgumentNullException.ThrowIfNull(createStack);
        this.networkChanges = networkChanges;
        this.createStack = createStack;
    }

    public event Action<Exception>? Faulted;

    public event Action<DnsSdServiceSnapshot>? ServiceChanged;

    public event Action<string>? ServiceRemoved;

    public void Dispose()
    {
        lock (restartGate)
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            networkSubscription?.Dispose();
            networkSubscription = null;
            StackBinding? removed;
            lock (gate)
            {
                removed = binding;
                binding = null;
                cache.Clear();
            }

            removed?.Dispose();
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0)
        {
            return;
        }

        try
        {
            networkSubscription = networkChanges.Subscribe(OnNetworkChanged);
            Restart(throwOnFailure: true);
        }
        catch
        {
            networkSubscription?.Dispose();
            networkSubscription = null;
            Volatile.Write(ref started, 0);
            throw;
        }
    }

    private void OnInstanceDiscovered(StackBinding source, string instanceName)
    {
        if (!IsCurrent(source))
        {
            return;
        }

        try
        {
            source.Stack.QueryInstance(instanceName);
        }
        catch (Exception exception)
        {
            ReportFault(exception);
        }
    }

    private void OnInstanceRemoved(StackBinding source, string instanceName)
    {
        bool removed;
        lock (gate)
        {
            removed = ReferenceEquals(binding, source)
                && cache.RemoveInstance(instanceName);
        }

        if (removed)
        {
            PublishRemoved(instanceName.TrimEnd('.'));
        }
    }

    private void OnNetworkChanged()
    {
        try
        {
            Restart(throwOnFailure: false);
        }
        catch (Exception exception)
        {
            ReportFault(exception);
        }
    }

    private void OnRecords(
        StackBinding source,
        IReadOnlyList<MdnsResource> records)
    {
        DnsSdCacheUpdate update;
        lock (gate)
        {
            if (!ReferenceEquals(binding, source))
            {
                return;
            }

            update = cache.Observe(records);
        }

        foreach (string hostName in update.HostsToQuery)
        {
            try
            {
                source.Stack.QueryHost(hostName);
            }
            catch (Exception exception)
            {
                ReportFault(exception);
            }
        }

        foreach (DnsSdServiceSnapshot snapshot in update.Snapshots)
        {
            PublishChanged(snapshot);
        }
    }

    private bool IsCurrent(StackBinding source)
    {
        lock (gate)
        {
            return ReferenceEquals(binding, source);
        }
    }

    private void PublishChanged(DnsSdServiceSnapshot snapshot)
    {
        foreach (Action<DnsSdServiceSnapshot> subscriber in
                 ServiceChanged?.GetInvocationList()
                     .Cast<Action<DnsSdServiceSnapshot>>() ?? [])
        {
            try
            {
                subscriber(snapshot);
            }
            catch (Exception exception)
            {
                ReportFault(exception);
            }
        }
    }

    private void PublishRemoved(string instanceName)
    {
        foreach (Action<string> subscriber in
                 ServiceRemoved?.GetInvocationList().Cast<Action<string>>() ?? [])
        {
            try
            {
                subscriber(instanceName);
            }
            catch (Exception exception)
            {
                ReportFault(exception);
            }
        }
    }

    private void ReportFault(Exception exception)
    {
        foreach (Action<Exception> subscriber in
                 Faulted?.GetInvocationList().Cast<Action<Exception>>() ?? [])
        {
            try
            {
                subscriber(exception);
            }
            catch
            {
                // Diagnostics must not tear down the network callback.
            }
        }
    }

    private void Restart(bool throwOnFailure)
    {
        lock (restartGate)
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            StackBinding? oldBinding;
            string[] removedInstances;
            StackBinding newBinding;
            IMdnsDiscoveryStack stack = createStack()
                ?? throw new InvalidOperationException(
                    "The mDNS stack factory returned null.");
            try
            {
                newBinding = new StackBinding(this, stack);
            }
            catch
            {
                stack.Dispose();
                throw;
            }

            lock (gate)
            {
                oldBinding = binding;
                binding = null;
                removedInstances = cache.Clear();
                binding = newBinding;
            }

            oldBinding?.Dispose();
            foreach (string instanceName in removedInstances)
            {
                PublishRemoved(instanceName);
            }

            try
            {
                newBinding.Stack.Start();
                newBinding.Stack.QueryInstances();
            }
            catch (Exception exception)
            {
                lock (gate)
                {
                    if (ReferenceEquals(binding, newBinding))
                    {
                        binding = null;
                    }
                }

                newBinding.Dispose();
                if (throwOnFailure)
                {
                    throw;
                }

                ReportFault(exception);
            }
        }
    }

    private sealed class StackBinding : IDisposable
    {
        private readonly Action<string> instanceDiscovered;
        private readonly Action<string> instanceRemoved;
        private readonly Action<IReadOnlyList<MdnsResource>> recordsReceived;
        private int disposed;

        public StackBinding(
            MakaretuDnsSdServiceBrowser owner,
            IMdnsDiscoveryStack stack)
        {
            Stack = stack;
            instanceDiscovered = instance => owner.OnInstanceDiscovered(this, instance);
            instanceRemoved = instance => owner.OnInstanceRemoved(this, instance);
            recordsReceived = records => owner.OnRecords(this, records);
            stack.InstanceDiscovered += instanceDiscovered;
            stack.InstanceRemoved += instanceRemoved;
            stack.RecordsReceived += recordsReceived;
        }

        public IMdnsDiscoveryStack Stack { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            Stack.InstanceDiscovered -= instanceDiscovered;
            Stack.InstanceRemoved -= instanceRemoved;
            Stack.RecordsReceived -= recordsReceived;
            Stack.Dispose();
        }
    }
}

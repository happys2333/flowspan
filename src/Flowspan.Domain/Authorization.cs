using System.Collections.Frozen;

namespace Flowspan.Domain;

public enum Capability
{
    ActivityOffer,
    ActivityReceive,
    ActivityReplace,
    MirrorView,
    MirrorDrive,
    FileReceive,
    SceneApply,
}

public sealed class CapabilityGrant
{
    private readonly FrozenSet<Capability> capabilities;

    private CapabilityGrant(IEnumerable<Capability> capabilities) =>
        this.capabilities = capabilities.ToFrozenSet();

    public static CapabilityGrant None { get; } = new([]);

    public static CapabilityGrant Of(params Capability[] capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        return new CapabilityGrant(capabilities);
    }

    public IReadOnlySet<Capability> Capabilities => capabilities;

    public bool Allows(Capability capability) => capabilities.Contains(capability);
}

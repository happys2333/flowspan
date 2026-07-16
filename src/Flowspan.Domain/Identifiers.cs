namespace Flowspan.Domain;

public sealed record DeviceId
{
    private DeviceId(Guid value) => Value = value;

    public Guid Value { get; }

    public static DeviceId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("A device ID cannot be empty.", nameof(value))
            : new DeviceId(value);

    public static DeviceId Parse(string value) => From(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}

public sealed record ActivityId
{
    private ActivityId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ActivityId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("An Activity ID cannot be empty.", nameof(value))
            : new ActivityId(value);

    public static ActivityId Parse(string value) => From(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}

public sealed record GroupId
{
    private GroupId(Guid value) => Value = value;

    public Guid Value { get; }

    public static GroupId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("A Group ID cannot be empty.", nameof(value))
            : new GroupId(value);

    public static GroupId Parse(string value) => From(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}

public sealed record SceneId
{
    private SceneId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SceneId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("A Scene ID cannot be empty.", nameof(value))
            : new SceneId(value);

    public static SceneId Parse(string value) => From(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}

public sealed record OperationId
{
    private OperationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static OperationId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("An operation ID cannot be empty.", nameof(value))
            : new OperationId(value);

    public static OperationId Parse(string value) => From(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}

public sealed record CorrelationId
{
    private CorrelationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CorrelationId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("A correlation ID cannot be empty.", nameof(value))
            : new CorrelationId(value);

    public static CorrelationId Parse(string value) => From(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}

public sealed record UndoCapsuleId
{
    private UndoCapsuleId(Guid value) => Value = value;

    public Guid Value { get; }

    public static UndoCapsuleId From(Guid value) =>
        value == Guid.Empty
            ? throw new ArgumentException("An undo capsule ID cannot be empty.", nameof(value))
            : new UndoCapsuleId(value);

    public static UndoCapsuleId Parse(string value) => From(Guid.Parse(value));

    public override string ToString() => Value.ToString("D");
}

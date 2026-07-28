namespace Flowspan.Release;

public sealed record ReleaseTarget
{
    private ReleaseTarget(
        string rid,
        string archiveExtension,
        string rootName,
        string entryPoint,
        bool usesZip,
        bool isMacOS)
    {
        Rid = rid;
        ArchiveExtension = archiveExtension;
        RootName = rootName;
        EntryPoint = entryPoint;
        UsesZip = usesZip;
        IsMacOS = isMacOS;
    }

    public string Rid { get; }

    public string ArchiveExtension { get; }

    public string RootName { get; }

    public string EntryPoint { get; }

    public bool UsesZip { get; }

    public bool IsMacOS { get; }

    public static ReleaseTarget Parse(string rid) => rid switch
    {
        "win-x64" => new(
            rid,
            ".zip",
            "Flowspan",
            "Flowspan/Flowspan.Desktop.exe",
            usesZip: true,
            isMacOS: false),
        "osx-arm64" => new(
            rid,
            ".tar.gz",
            "Flowspan.app",
            "Flowspan.app/Contents/MacOS/Flowspan.Desktop",
            usesZip: false,
            isMacOS: true),
        "linux-x64" => new(
            rid,
            ".tar.gz",
            "flowspan",
            "flowspan/Flowspan.Desktop",
            usesZip: false,
            isMacOS: false),
        _ => throw new ReleaseInputException(
            "The release target is not in the approved RID matrix."),
    };
}

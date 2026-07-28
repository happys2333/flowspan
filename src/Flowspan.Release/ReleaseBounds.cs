namespace Flowspan.Release;

public static class ReleaseBounds
{
    public const int MaximumFileCount = 4096;
    public const int MaximumRelativePathLength = 240;
    public const long MaximumFileBytes = 512L * 1024 * 1024;
    public const long MaximumPackageBytes = 1024L * 1024 * 1024;
    public const int MaximumJsonBytes = 4 * 1024 * 1024;
    public const int MaximumTextLength = 256;
    public const long MinimumSourceDateEpoch = 315532800;
    public const long MaximumSourceDateEpoch = 4102444799;
}

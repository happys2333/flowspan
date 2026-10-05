namespace Flowspan.Platform.MacOS;

// Only the external root and Block runtime effects are replaceable. The staged
// owner, capture helpers and callback handling remain the actual primitive.
internal interface IMacOSRemoteWindowBlockOperations
{
    public nint AllocateRoot(object target);
    public void FreeRoot(nint root);
    public nint CopyBlock(nint block);
    public void ReleaseBlock(nint block);
}

using Flowspan.Domain;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowMediaFrameOwnershipTests
{
    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly ActivityId ActivityId =
        ActivityId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void DisposeClearsOwnedPayloadAndRejectsFurtherAccess()
    {
        byte[] ownedPayload = [0x11, 0x22, 0x33, 0x44];
        var frame = RemoteWindowMediaFrame.TakeOwnership(
            SessionId,
            ActivityId,
            RemoteWindowMediaKind.Video,
            sequence: 1,
            chunkIndex: 0,
            chunkCount: 1,
            ownedPayload);

        frame.Dispose();
        frame.Dispose();

        Assert.Equal(new byte[ownedPayload.Length], ownedPayload);
        Assert.Throws<ObjectDisposedException>(() => frame.PayloadLength);
        Assert.Throws<ObjectDisposedException>(frame.ExportPayload);
        Assert.Throws<ObjectDisposedException>(() => frame.Clone());
        Assert.Throws<ObjectDisposedException>(() =>
            RemoteWindowMediaFrameCodec.Encode(frame));
    }

    [Fact]
    public void FailedOwnershipValidationLeavesPayloadWithCaller()
    {
        byte[] ownedPayload = [0x11, 0x22, 0x33, 0x44];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RemoteWindowMediaFrame.TakeOwnership(
                SessionId,
                ActivityId,
                RemoteWindowMediaKind.Video,
                sequence: 0,
                chunkIndex: 0,
                chunkCount: 1,
                ownedPayload));

        Assert.Equal(new byte[] { 0x11, 0x22, 0x33, 0x44 }, ownedPayload);
    }
}

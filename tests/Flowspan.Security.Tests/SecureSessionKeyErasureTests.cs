using System.Reflection;
using System.Security.Cryptography;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class SecureSessionKeyErasureTests
{
    [Fact]
    public void RotationAndDisposalEraseSupersededDirectionalKeyBuffers()
    {
        byte[] initialKey = Enumerable.Repeat((byte)0x5a, 32).ToArray();
        byte[] sessionIdentifier = Enumerable.Repeat((byte)0xa5, 16).ToArray();
        var protector = new SecureFrameProtector(
            initialKey,
            SecureFrameDirection.InitiatorToResponder,
            sessionIdentifier,
            SecureFrameSession.MaximumFramesPerEpoch,
            SecureFrameSession.MaximumPlaintextBytesPerEpoch);
        CryptographicOperations.ZeroMemory(initialKey);
        CryptographicOperations.ZeroMemory(sessionIdentifier);
        FieldInfo? keyField = typeof(SecureFrameProtector).GetField(
            "key",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(keyField);
        byte[] retiredKey = Assert.IsType<byte[]>(keyField.GetValue(protector));
        Assert.Contains(retiredKey, value => value != 0);

        protector.AdvanceEpoch(nextEpoch: 2);

        Assert.All(retiredKey, value => Assert.Equal<byte>(0, value));
        byte[] activeKey = Assert.IsType<byte[]>(keyField.GetValue(protector));
        Assert.NotSame(retiredKey, activeKey);
        Assert.Contains(activeKey, value => value != 0);

        protector.Dispose();

        Assert.All(activeKey, value => Assert.Equal<byte>(0, value));
    }
}

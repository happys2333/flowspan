using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class DeviceIdentityTests
{
    private static readonly DeviceId Device =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void GeneratedIdentitySignsAndDetectsAlteration()
    {
        using DeviceIdentity identity = DeviceIdentity.Generate(Device, "Laptop");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("pairing transcript"));

        byte[] signature = identity.SignHash(hash);

        Assert.Equal(64, signature.Length);
        Assert.True(identity.PublicIdentity.VerifyHash(hash, signature));
        hash[0] ^= 0x01;
        Assert.False(identity.PublicIdentity.VerifyHash(hash, signature));
        signature[0] ^= 0x01;
        Assert.False(identity.PublicIdentity.VerifyHash(hash, signature));
    }

    [Fact]
    public void Pkcs8RoundTripPreservesPublicIdentity()
    {
        using DeviceIdentity original = DeviceIdentity.Generate(Device, "Laptop");
        byte[] privateKey = original.ExportPkcs8ForSecretStore();
        try
        {
            using DeviceIdentity imported = DeviceIdentity.ImportPkcs8(
                Device,
                "Laptop",
                privateKey);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("test"));

            Assert.Equal(
                original.PublicIdentity.Fingerprint,
                imported.PublicIdentity.Fingerprint);
            Assert.True(imported.PublicIdentity.VerifyHash(hash, imported.SignHash(hash)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateKey);
        }
    }

    [Fact]
    public void NonP256PublicIdentityIsRejected()
    {
        using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        byte[] spki = p384.ExportSubjectPublicKeyInfo();

        Assert.Throws<CryptographicException>(() => new PublicDeviceIdentity(
            Device,
            "Laptop",
            spki));
    }

    [Fact]
    public void DisposedIdentityCannotSignOrExport()
    {
        DeviceIdentity identity = DeviceIdentity.Generate(Device, "Laptop");
        identity.Dispose();
        byte[] hash = new byte[SHA256.HashSizeInBytes];

        Assert.Throws<ObjectDisposedException>(() => identity.SignHash(hash));
        Assert.Throws<ObjectDisposedException>(() => identity.ExportPkcs8ForSecretStore());
    }
}

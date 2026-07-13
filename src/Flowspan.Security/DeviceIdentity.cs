using System.Security.Cryptography;
using System.Text;
using Flowspan.Domain;

namespace Flowspan.Security;

public sealed class PublicDeviceIdentity
{
    private readonly byte[] subjectPublicKeyInfo;

    public PublicDeviceIdentity(
        DeviceId deviceId,
        string displayName,
        ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        DeviceId = deviceId;
        DisplayName = DeviceIdentity.NormalizeDisplayName(displayName);
        if (subjectPublicKeyInfo.IsEmpty || subjectPublicKeyInfo.Length > 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(subjectPublicKeyInfo),
                "An identity SPKI must contain 1 to 1024 bytes.");
        }

        this.subjectPublicKeyInfo = subjectPublicKeyInfo.ToArray();
        ValidatePublicKey(this.subjectPublicKeyInfo);
        Fingerprint = Convert.ToHexString(SHA256.HashData(this.subjectPublicKeyInfo));
    }

    public DeviceId DeviceId { get; }

    public string DisplayName { get; }

    public string Fingerprint { get; }

    public byte[] ExportSubjectPublicKeyInfo() => (byte[])subjectPublicKeyInfo.Clone();

    public bool HasSameKey(PublicDeviceIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return CryptographicOperations.FixedTimeEquals(
            subjectPublicKeyInfo,
            other.subjectPublicKeyInfo);
    }

    public bool VerifyHash(ReadOnlySpan<byte> hash, ReadOnlySpan<byte> signature)
    {
        if (hash.Length != SHA256.HashSizeInBytes || signature.Length != 64)
        {
            return false;
        }

        using ECDsa verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int bytesRead);
        return bytesRead == subjectPublicKeyInfo.Length
            && verifier.VerifyHash(
                hash,
                signature,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static void ValidatePublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        using ECDsa verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int bytesRead);
        ECParameters parameters = verifier.ExportParameters(includePrivateParameters: false);
        if (bytesRead != subjectPublicKeyInfo.Length
            || verifier.KeySize != 256
            || parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
        {
            throw new CryptographicException(
                "The identity key must be exactly one P-256 SPKI value.");
        }
    }
}

public sealed class DeviceIdentity : IDisposable
{
    private readonly ECDsa signingKey;
    private readonly Lock signingGate = new();
    private bool disposed;

    private DeviceIdentity(DeviceId deviceId, string displayName, ECDsa signingKey)
    {
        DeviceId = deviceId;
        DisplayName = NormalizeDisplayName(displayName);
        this.signingKey = signingKey;
        PublicIdentity = new PublicDeviceIdentity(
            deviceId,
            DisplayName,
            signingKey.ExportSubjectPublicKeyInfo());
    }

    public DeviceId DeviceId { get; }

    public string DisplayName { get; }

    public PublicDeviceIdentity PublicIdentity { get; }

    public static DeviceIdentity Generate(DeviceId deviceId, string displayName)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new DeviceIdentity(deviceId, displayName, key);
    }

    public static DeviceIdentity ImportPkcs8(
        DeviceId deviceId,
        string displayName,
        ReadOnlySpan<byte> privateKey)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        if (privateKey.IsEmpty)
        {
            throw new ArgumentException("A PKCS#8 private key is required.", nameof(privateKey));
        }

        ECDsa key = ECDsa.Create();
        try
        {
            key.ImportPkcs8PrivateKey(privateKey, out int bytesRead);
            ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
            if (bytesRead != privateKey.Length
                || key.KeySize != 256
                || parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            {
                throw new CryptographicException(
                    "The identity key must be exactly one P-256 PKCS#8 value.");
            }

            return new DeviceIdentity(deviceId, displayName, key);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public byte[] ExportPkcs8ForSecretStore()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        lock (signingGate)
        {
            return signingKey.ExportPkcs8PrivateKey();
        }
    }

    public byte[] SignHash(ReadOnlySpan<byte> hash)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (hash.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException(
                "An identity signature requires one SHA-256 hash.",
                nameof(hash));
        }

        lock (signingGate)
        {
            return signingKey.SignHash(
                hash,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        lock (signingGate)
        {
            if (!disposed)
            {
                signingKey.Dispose();
                disposed = true;
            }
        }
    }

    internal static string NormalizeDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string normalized = displayName.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.Length > 80 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A device display name must contain 1 to 80 non-control characters.",
                nameof(displayName));
        }

        return normalized;
    }
}

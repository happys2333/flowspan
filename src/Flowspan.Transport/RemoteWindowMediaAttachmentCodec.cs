using System.Buffers.Binary;
using System.Security.Cryptography;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;

namespace Flowspan.Transport;

public sealed class RemoteWindowMediaRouteId : IEquatable<RemoteWindowMediaRouteId>
{
    public const int ByteLength = 16;
    private readonly byte[] value;

    private RemoteWindowMediaRouteId(ReadOnlySpan<byte> value) =>
        this.value = value.ToArray();

    public static RemoteWindowMediaRouteId FromSession(
        SecureFrameSession mediaSession)
    {
        ArgumentNullException.ThrowIfNull(mediaSession);
        byte[] identifier = mediaSession.ExportSessionIdentifier();
        try
        {
            return FromBytes(identifier);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(identifier);
        }
    }

    public bool Equals(RemoteWindowMediaRouteId? other) =>
        other is not null
        && CryptographicOperations.FixedTimeEquals(value, other.value);

    public override bool Equals(object? obj) =>
        obj is RemoteWindowMediaRouteId other && Equals(other);

    public override int GetHashCode() =>
        BinaryPrimitives.ReadInt32BigEndian(value);

    public override string ToString() => nameof(RemoteWindowMediaRouteId);

    internal static RemoteWindowMediaRouteId FromBytes(ReadOnlySpan<byte> value)
    {
        if (value.Length != ByteLength
            || value.IndexOfAnyExcept((byte)0) < 0)
        {
            throw new ArgumentException(
                $"A Remote Window media route ID must contain {ByteLength} nonzero opaque bytes.",
                nameof(value));
        }

        return new RemoteWindowMediaRouteId(value);
    }

    internal void CopyTo(Span<byte> destination) => value.CopyTo(destination);

    internal bool MatchesSession(SecureFrameSession mediaSession)
    {
        byte[] identifier = mediaSession.ExportSessionIdentifier();
        try
        {
            return CryptographicOperations.FixedTimeEquals(value, identifier);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(identifier);
        }
    }
}

public sealed record RemoteWindowMediaRouteBinding
{
    private RemoteWindowMediaRouteBinding(
        ProtocolVersion protocolVersion,
        DeviceId initiatorDeviceId,
        DeviceId responderDeviceId,
        RemoteWindowMediaRouteId routeId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId)
    {
        ProtocolVersion = protocolVersion;
        InitiatorDeviceId = initiatorDeviceId;
        ResponderDeviceId = responderDeviceId;
        RouteId = routeId;
        SessionId = sessionId;
        ActivityId = activityId;
    }

    public ActivityId ActivityId { get; }

    public DeviceId InitiatorDeviceId { get; }

    public ProtocolVersion ProtocolVersion { get; }

    public DeviceId ResponderDeviceId { get; }

    public RemoteWindowMediaRouteId RouteId { get; }

    public RemoteWindowSessionId SessionId { get; }

    public static RemoteWindowMediaRouteBinding Create(
        ProtocolVersion protocolVersion,
        DeviceId initiatorDeviceId,
        DeviceId responderDeviceId,
        RemoteWindowMediaRouteId routeId,
        RemoteWindowSessionId sessionId,
        ActivityId activityId)
    {
        ArgumentNullException.ThrowIfNull(initiatorDeviceId);
        ArgumentNullException.ThrowIfNull(responderDeviceId);
        ArgumentNullException.ThrowIfNull(routeId);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(activityId);
        if (!ProtocolFeatures.SupportsRemoteWindowMediaRoute(protocolVersion))
        {
            throw new ArgumentOutOfRangeException(
                nameof(protocolVersion),
                $"A Remote Window media route requires protocol {ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion} or later.");
        }

        if (initiatorDeviceId == responderDeviceId)
        {
            throw new ArgumentException(
                "A Remote Window media route requires two distinct devices.",
                nameof(responderDeviceId));
        }

        return new RemoteWindowMediaRouteBinding(
            protocolVersion,
            initiatorDeviceId,
            responderDeviceId,
            routeId,
            sessionId,
            activityId);
    }

    public override string ToString() =>
        $"{nameof(RemoteWindowMediaRouteBinding)} {{ ProtocolVersion = {ProtocolVersion} }}";
}

internal sealed class RemoteWindowMediaAttachmentRequest
{
    private readonly byte[] initiatorNonce;

    public RemoteWindowMediaAttachmentRequest(
        RemoteWindowMediaRouteBinding binding,
        ReadOnlySpan<byte> initiatorNonce)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        RemoteWindowMediaAttachmentCodec.ValidateNonce(
            initiatorNonce,
            nameof(initiatorNonce));
        this.initiatorNonce = initiatorNonce.ToArray();
    }

    public RemoteWindowMediaRouteBinding Binding { get; }

    public byte[] ExportInitiatorNonce() => initiatorNonce.ToArray();
}

internal sealed class RemoteWindowMediaAttachmentAcknowledgement
{
    private readonly byte[] initiatorNonce;
    private readonly byte[] responderNonce;

    public RemoteWindowMediaAttachmentAcknowledgement(
        RemoteWindowMediaRouteBinding binding,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        RemoteWindowMediaAttachmentCodec.ValidateNonce(
            initiatorNonce,
            nameof(initiatorNonce));
        RemoteWindowMediaAttachmentCodec.ValidateNonce(
            responderNonce,
            nameof(responderNonce));
        this.initiatorNonce = initiatorNonce.ToArray();
        this.responderNonce = responderNonce.ToArray();
    }

    public RemoteWindowMediaRouteBinding Binding { get; }

    public byte[] ExportInitiatorNonce() => initiatorNonce.ToArray();

    public byte[] ExportResponderNonce() => responderNonce.ToArray();
}

internal static class RemoteWindowMediaAttachmentCodec
{
    public const int NonceBytes = 32;
    public const int RequestEnvelopeBytes = 200;
    public const int AcknowledgementEnvelopeBytes = 232;
    public const int MaximumEnvelopeBytes = AcknowledgementEnvelopeBytes;
    private const int ActivityIdOffset = 80;
    private const int AcknowledgementPlaintextBytes = 160;
    private const int BodyFlagsOffset = 6;
    private const int BodyFormatOffset = 4;
    private const int BodyInitiatorDeviceIdOffset = 16;
    private const int BodyKindOffset = 5;
    private const int BodyProtocolMajorOffset = 8;
    private const int BodyProtocolMinorOffset = 12;
    private const int BodyReservedOffset = 7;
    private const int BodyResponderDeviceIdOffset = 32;
    private const int BodyRouteIdOffset = 48;
    private const int BodySessionIdOffset = 64;
    private const int EncryptedLengthOffset = 32;
    private const int EnvelopeFlagsOffset = 6;
    private const int EnvelopeFormatOffset = 4;
    private const int EnvelopeHeaderBytes = 36;
    private const int EnvelopeKindOffset = 5;
    private const int EnvelopeProtocolMajorOffset = 8;
    private const int EnvelopeProtocolMinorOffset = 12;
    private const int EnvelopeReservedOffset = 7;
    private const int EnvelopeRouteIdOffset = 16;
    private const byte FormatVersion = 1;
    private const int IdentifierBytes = 16;
    private const int InitiatorNonceOffset = 96;
    private const int RequestPlaintextBytes = 128;
    private const int ResponderNonceOffset = 128;
    private static ReadOnlySpan<byte> BodyMagic => "FSMB"u8;
    private static ReadOnlySpan<byte> EnvelopeMagic => "FSM1"u8;

    public static bool HasMagic(ReadOnlySpan<byte> encoded) =>
        encoded.Length >= EnvelopeMagic.Length
        && encoded[..EnvelopeMagic.Length].SequenceEqual(EnvelopeMagic);

    internal static RemoteWindowMediaAttachmentPrefix DecodeRequestPrefix(
        ReadOnlySpan<byte> encoded)
    {
        EnvelopePrefix prefix = DecodeEnvelopePrefix(
            encoded,
            AttachmentMessageKind.Request);
        return new RemoteWindowMediaAttachmentPrefix(
            prefix.ProtocolVersion,
            prefix.RouteId);
    }

    internal static bool TryReadRouteLocator(
        ReadOnlySpan<byte> encoded,
        out RemoteWindowMediaRouteId routeId)
    {
        routeId = null!;
        if (encoded.Length < EnvelopeRouteIdOffset + IdentifierBytes
            || !HasMagic(encoded))
        {
            return false;
        }

        try
        {
            routeId = RemoteWindowMediaRouteId.FromBytes(
                encoded.Slice(EnvelopeRouteIdOffset, IdentifierBytes));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static byte[] EncodeRequest(
        RemoteWindowMediaRouteBinding binding,
        ReadOnlySpan<byte> initiatorNonce,
        SecureFrameSession mediaSession) => Encode(
            binding,
            AttachmentMessageKind.Request,
            initiatorNonce,
            responderNonce: default,
            mediaSession);

    public static byte[] EncodeAcknowledgement(
        RemoteWindowMediaRouteBinding binding,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        SecureFrameSession mediaSession) => Encode(
            binding,
            AttachmentMessageKind.Acknowledgement,
            initiatorNonce,
            responderNonce,
            mediaSession);

    public static RemoteWindowMediaAttachmentRequest DecodeRequest(
        ReadOnlySpan<byte> encoded,
        SecureFrameSession mediaSession)
    {
        DecodedBody body = Decode(
            encoded,
            AttachmentMessageKind.Request,
            mediaSession);
        return new RemoteWindowMediaAttachmentRequest(
            body.Binding,
            body.InitiatorNonce);
    }

    public static RemoteWindowMediaAttachmentAcknowledgement DecodeAcknowledgement(
        ReadOnlySpan<byte> encoded,
        SecureFrameSession mediaSession)
    {
        DecodedBody body = Decode(
            encoded,
            AttachmentMessageKind.Acknowledgement,
            mediaSession);
        return new RemoteWindowMediaAttachmentAcknowledgement(
            body.Binding,
            body.InitiatorNonce,
            body.ResponderNonce);
    }

    internal static void ValidateNonce(ReadOnlySpan<byte> nonce, string parameterName)
    {
        if (nonce.Length != NonceBytes
            || nonce.IndexOfAnyExcept((byte)0) < 0)
        {
            throw new ArgumentException(
                $"A media attachment nonce must contain {NonceBytes} nonzero bytes.",
                parameterName);
        }
    }

    private static byte[] Encode(
        RemoteWindowMediaRouteBinding binding,
        AttachmentMessageKind kind,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        SecureFrameSession mediaSession)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(mediaSession);
        ValidateNonce(initiatorNonce, nameof(initiatorNonce));
        if (kind == AttachmentMessageKind.Acknowledgement)
        {
            ValidateNonce(responderNonce, nameof(responderNonce));
        }
        else if (!responderNonce.IsEmpty)
        {
            throw new ArgumentException(
                "A media attachment request cannot contain a responder nonce.",
                nameof(responderNonce));
        }

        ValidateRouteSession(binding.RouteId, mediaSession);
        int plaintextLength = kind == AttachmentMessageKind.Request
            ? RequestPlaintextBytes
            : AcknowledgementPlaintextBytes;
        byte[] plaintext = GC.AllocateUninitializedArray<byte>(plaintextLength);
        byte[]? encrypted = null;
        try
        {
            WriteBody(
                plaintext,
                binding,
                kind,
                initiatorNonce,
                responderNonce);
            encrypted = mediaSession.Encrypt(plaintext);
            int expectedEnvelopeLength = kind == AttachmentMessageKind.Request
                ? RequestEnvelopeBytes
                : AcknowledgementEnvelopeBytes;
            if (encrypted.Length != expectedEnvelopeLength - EnvelopeHeaderBytes)
            {
                throw new InvalidOperationException(
                    "The secure media attachment frame has an unexpected length.");
            }

            byte[] envelope = GC.AllocateUninitializedArray<byte>(
                expectedEnvelopeLength);
            WriteEnvelopeHeader(envelope, binding, kind, encrypted.Length);
            encrypted.CopyTo(envelope, EnvelopeHeaderBytes);
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (encrypted is not null)
            {
                CryptographicOperations.ZeroMemory(encrypted);
            }
        }
    }

    private static DecodedBody Decode(
        ReadOnlySpan<byte> encoded,
        AttachmentMessageKind expectedKind,
        SecureFrameSession mediaSession)
    {
        ArgumentNullException.ThrowIfNull(mediaSession);
        EnvelopePrefix prefix = DecodeEnvelopePrefix(encoded, expectedKind);
        ValidateRouteSession(prefix.RouteId, mediaSession);
        byte[] plaintext = mediaSession.Decrypt(encoded[EnvelopeHeaderBytes..]);
        try
        {
            return DecodeBody(plaintext, prefix, expectedKind);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static EnvelopePrefix DecodeEnvelopePrefix(
        ReadOnlySpan<byte> encoded,
        AttachmentMessageKind expectedKind)
    {
        int expectedLength = expectedKind == AttachmentMessageKind.Request
            ? RequestEnvelopeBytes
            : AcknowledgementEnvelopeBytes;
        if (encoded.Length != expectedLength)
        {
            throw new InvalidDataException(
                "The Remote Window media attachment envelope length is invalid.");
        }

        if (!HasMagic(encoded))
        {
            throw new InvalidDataException(
                "The Remote Window media attachment magic is invalid.");
        }

        if (encoded[EnvelopeFormatOffset] != FormatVersion
            || encoded[EnvelopeKindOffset] != (byte)expectedKind
            || encoded[EnvelopeFlagsOffset] != 0
            || encoded[EnvelopeReservedOffset] != 0)
        {
            throw new InvalidDataException(
                "The Remote Window media attachment envelope is unsupported.");
        }

        ProtocolVersion protocolVersion = ReadProtocolVersion(
            encoded,
            EnvelopeProtocolMajorOffset,
            EnvelopeProtocolMinorOffset);
        if (!ProtocolFeatures.SupportsRemoteWindowMediaRoute(protocolVersion))
        {
            throw new InvalidDataException(
                $"A Remote Window media attachment requires protocol {ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion} or later.");
        }

        uint encryptedLength = BinaryPrimitives.ReadUInt32BigEndian(
            encoded.Slice(EncryptedLengthOffset, sizeof(uint)));
        if (encryptedLength != encoded.Length - EnvelopeHeaderBytes)
        {
            throw new InvalidDataException(
                "The Remote Window media attachment encrypted length is invalid.");
        }

        try
        {
            return new EnvelopePrefix(
                protocolVersion,
                RemoteWindowMediaRouteId.FromBytes(
                    encoded.Slice(EnvelopeRouteIdOffset, IdentifierBytes)));
        }
        catch (ArgumentException failure)
        {
            throw new InvalidDataException(
                "The Remote Window media attachment route is invalid.",
                failure);
        }
    }

    private static DecodedBody DecodeBody(
        ReadOnlySpan<byte> plaintext,
        EnvelopePrefix prefix,
        AttachmentMessageKind expectedKind)
    {
        int expectedLength = expectedKind == AttachmentMessageKind.Request
            ? RequestPlaintextBytes
            : AcknowledgementPlaintextBytes;
        if (plaintext.Length != expectedLength
            || !plaintext[..BodyMagic.Length].SequenceEqual(BodyMagic)
            || plaintext[BodyFormatOffset] != FormatVersion
            || plaintext[BodyKindOffset] != (byte)expectedKind
            || plaintext[BodyFlagsOffset] != 0
            || plaintext[BodyReservedOffset] != 0)
        {
            throw new InvalidDataException(
                "The protected Remote Window media attachment body is unsupported.");
        }

        ProtocolVersion protocolVersion = ReadProtocolVersion(
            plaintext,
            BodyProtocolMajorOffset,
            BodyProtocolMinorOffset);
        RemoteWindowMediaRouteId routeId;
        try
        {
            routeId = RemoteWindowMediaRouteId.FromBytes(
                plaintext.Slice(BodyRouteIdOffset, IdentifierBytes));
        }
        catch (ArgumentException failure)
        {
            throw new InvalidDataException(
                "The protected Remote Window media attachment route is invalid.",
                failure);
        }

        if (protocolVersion != prefix.ProtocolVersion
            || !routeId.Equals(prefix.RouteId))
        {
            throw new InvalidDataException(
                "The protected Remote Window media attachment does not match its envelope.");
        }

        try
        {
            RemoteWindowMediaRouteBinding binding =
                RemoteWindowMediaRouteBinding.Create(
                    protocolVersion,
                    DeviceId.From(ReadGuid(
                        plaintext.Slice(
                            BodyInitiatorDeviceIdOffset,
                            IdentifierBytes))),
                    DeviceId.From(ReadGuid(
                        plaintext.Slice(
                            BodyResponderDeviceIdOffset,
                            IdentifierBytes))),
                    routeId,
                    RemoteWindowSessionId.From(ReadGuid(
                        plaintext.Slice(BodySessionIdOffset, IdentifierBytes))),
                    ActivityId.From(ReadGuid(
                        plaintext.Slice(ActivityIdOffset, IdentifierBytes))));
            byte[] initiatorNonce = plaintext.Slice(
                    InitiatorNonceOffset,
                    NonceBytes)
                .ToArray();
            byte[] responderNonce = expectedKind ==
                AttachmentMessageKind.Acknowledgement
                    ? plaintext.Slice(ResponderNonceOffset, NonceBytes).ToArray()
                    : [];
            ValidateNonce(initiatorNonce, "initiatorNonce");
            if (expectedKind == AttachmentMessageKind.Acknowledgement)
            {
                ValidateNonce(responderNonce, "responderNonce");
            }

            return new DecodedBody(binding, initiatorNonce, responderNonce);
        }
        catch (ArgumentException failure)
        {
            throw new InvalidDataException(
                "The protected Remote Window media attachment binding is invalid.",
                failure);
        }
    }

    private static void WriteEnvelopeHeader(
        Span<byte> destination,
        RemoteWindowMediaRouteBinding binding,
        AttachmentMessageKind kind,
        int encryptedLength)
    {
        EnvelopeMagic.CopyTo(destination);
        destination[EnvelopeFormatOffset] = FormatVersion;
        destination[EnvelopeKindOffset] = (byte)kind;
        destination[EnvelopeFlagsOffset] = 0;
        destination[EnvelopeReservedOffset] = 0;
        WriteProtocolVersion(
            destination,
            EnvelopeProtocolMajorOffset,
            EnvelopeProtocolMinorOffset,
            binding.ProtocolVersion);
        binding.RouteId.CopyTo(
            destination.Slice(EnvelopeRouteIdOffset, IdentifierBytes));
        BinaryPrimitives.WriteUInt32BigEndian(
            destination.Slice(EncryptedLengthOffset, sizeof(uint)),
            checked((uint)encryptedLength));
    }

    private static void WriteBody(
        Span<byte> destination,
        RemoteWindowMediaRouteBinding binding,
        AttachmentMessageKind kind,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce)
    {
        BodyMagic.CopyTo(destination);
        destination[BodyFormatOffset] = FormatVersion;
        destination[BodyKindOffset] = (byte)kind;
        destination[BodyFlagsOffset] = 0;
        destination[BodyReservedOffset] = 0;
        WriteProtocolVersion(
            destination,
            BodyProtocolMajorOffset,
            BodyProtocolMinorOffset,
            binding.ProtocolVersion);
        WriteGuid(
            destination.Slice(BodyInitiatorDeviceIdOffset, IdentifierBytes),
            binding.InitiatorDeviceId.Value);
        WriteGuid(
            destination.Slice(BodyResponderDeviceIdOffset, IdentifierBytes),
            binding.ResponderDeviceId.Value);
        binding.RouteId.CopyTo(
            destination.Slice(BodyRouteIdOffset, IdentifierBytes));
        WriteGuid(
            destination.Slice(BodySessionIdOffset, IdentifierBytes),
            binding.SessionId.Value);
        WriteGuid(
            destination.Slice(ActivityIdOffset, IdentifierBytes),
            binding.ActivityId.Value);
        initiatorNonce.CopyTo(
            destination.Slice(InitiatorNonceOffset, NonceBytes));
        if (kind == AttachmentMessageKind.Acknowledgement)
        {
            responderNonce.CopyTo(
                destination.Slice(ResponderNonceOffset, NonceBytes));
        }
    }

    private static ProtocolVersion ReadProtocolVersion(
        ReadOnlySpan<byte> source,
        int majorOffset,
        int minorOffset)
    {
        uint major = BinaryPrimitives.ReadUInt32BigEndian(
            source.Slice(majorOffset, sizeof(uint)));
        uint minor = BinaryPrimitives.ReadUInt32BigEndian(
            source.Slice(minorOffset, sizeof(uint)));
        try
        {
            return new ProtocolVersion(checked((int)major), checked((int)minor));
        }
        catch (Exception failure) when (
            failure is ArgumentOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException(
                "The Remote Window media attachment protocol version is invalid.",
                failure);
        }
    }

    private static void WriteProtocolVersion(
        Span<byte> destination,
        int majorOffset,
        int minorOffset,
        ProtocolVersion version)
    {
        BinaryPrimitives.WriteUInt32BigEndian(
            destination.Slice(majorOffset, sizeof(uint)),
            checked((uint)version.Major));
        BinaryPrimitives.WriteUInt32BigEndian(
            destination.Slice(minorOffset, sizeof(uint)),
            checked((uint)version.Minor));
    }

    private static Guid ReadGuid(ReadOnlySpan<byte> source) =>
        new(source, bigEndian: true);

    private static void WriteGuid(Span<byte> destination, Guid value)
    {
        if (!value.TryWriteBytes(destination, bigEndian: true, out int bytesWritten)
            || bytesWritten != IdentifierBytes)
        {
            throw new InvalidOperationException(
                "A Remote Window media attachment identifier could not be encoded.");
        }
    }

    private static void ValidateRouteSession(
        RemoteWindowMediaRouteId routeId,
        SecureFrameSession mediaSession)
    {
        if (!routeId.MatchesSession(mediaSession))
        {
            throw new InvalidOperationException(
                "The Remote Window media route does not match the authenticated media session.");
        }
    }

    private enum AttachmentMessageKind : byte
    {
        Request = 1,
        Acknowledgement = 2,
    }

    private sealed record DecodedBody(
        RemoteWindowMediaRouteBinding Binding,
        byte[] InitiatorNonce,
        byte[] ResponderNonce);

    private sealed record EnvelopePrefix(
        ProtocolVersion ProtocolVersion,
        RemoteWindowMediaRouteId RouteId);
}

internal readonly record struct RemoteWindowMediaAttachmentPrefix(
    ProtocolVersion ProtocolVersion,
    RemoteWindowMediaRouteId RouteId);

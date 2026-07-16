using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Flowspan.Security;

public sealed class SecureSessionKeyUpdate
{
    private SecureSessionKeyUpdate(bool requestPeerUpdate, uint nextEpoch)
    {
        RequestPeerUpdate = requestPeerUpdate;
        NextEpoch = nextEpoch;
    }

    public uint NextEpoch { get; }

    public bool RequestPeerUpdate { get; }

    public static SecureSessionKeyUpdate Create(
        bool requestPeerUpdate,
        uint nextEpoch)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nextEpoch, 2u);
        return new SecureSessionKeyUpdate(requestPeerUpdate, nextEpoch);
    }
}

public static class SecureSessionKeyUpdateCodec
{
    public const int EncodedLength = 10;
    private const byte KeyUpdateKind = 1;
    private const byte RequestPeerUpdateFlag = 1;
    private static ReadOnlySpan<byte> Magic => "FSR1"u8;

    public static byte[] Encode(SecureSessionKeyUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        byte[] encoded = new byte[EncodedLength];
        Magic.CopyTo(encoded);
        encoded[4] = KeyUpdateKind;
        encoded[5] = update.RequestPeerUpdate ? RequestPeerUpdateFlag : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(6), update.NextEpoch);
        return encoded;
    }

    public static SecureSessionKeyUpdate Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length != EncodedLength
            || !encoded[..Magic.Length].SequenceEqual(Magic)
            || encoded[4] != KeyUpdateKind
            || encoded[5] > RequestPeerUpdateFlag)
        {
            throw new InvalidDataException("The secure-session KeyUpdate is malformed.");
        }

        uint nextEpoch = BinaryPrimitives.ReadUInt32BigEndian(encoded[6..]);
        if (nextEpoch < 2)
        {
            throw new InvalidDataException(
                "The secure-session KeyUpdate epoch is invalid.");
        }

        return SecureSessionKeyUpdate.Create(
            encoded[5] == RequestPeerUpdateFlag,
            nextEpoch);
    }

    public static bool IsKeyUpdate(ReadOnlySpan<byte> encoded) =>
        encoded.Length >= Magic.Length
        && encoded[..Magic.Length].SequenceEqual(Magic);
}

public static class SecureSessionEpochKeyDerivation
{
    private const int KeyLength = 32;
    private const int SessionIdentifierLength = 16;
    private static ReadOnlySpan<byte> Context => "FLOWSPAN-REKEY-V1"u8;

    public static byte[] DeriveNextKey(
        ReadOnlySpan<byte> currentKey,
        ReadOnlySpan<byte> sessionIdentifier,
        SecureSessionRole senderRole,
        uint nextEpoch)
    {
        if (currentKey.Length != KeyLength)
        {
            throw new ArgumentException(
                "A secure-session traffic key must contain 32 bytes.",
                nameof(currentKey));
        }

        if (sessionIdentifier.Length != SessionIdentifierLength)
        {
            throw new ArgumentException(
                "A secure-session identifier must contain 16 bytes.",
                nameof(sessionIdentifier));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(nextEpoch, 2u);
        byte direction = senderRole switch
        {
            SecureSessionRole.Initiator => 1,
            SecureSessionRole.Responder => 2,
            _ => throw new ArgumentOutOfRangeException(
                nameof(senderRole),
                senderRole,
                "Unknown secure-session sender role."),
        };
        byte[] info = new byte[Context.Length + 1 + sizeof(uint)];
        Context.CopyTo(info);
        info[Context.Length] = direction;
        BinaryPrimitives.WriteUInt32BigEndian(
            info.AsSpan(Context.Length + 1),
            nextEpoch);
        try
        {
            return HkdfSha256.DeriveKey(
                currentKey,
                sessionIdentifier,
                info,
                KeyLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(info);
        }
    }
}

public readonly record struct SecureSessionPeerKeyUpdateDecision(
    uint NextReceiveEpoch,
    bool SendResponse,
    bool CompletesLocalRequest);

public static class SecureSessionRekeyRules
{
    public static SecureSessionPeerKeyUpdateDecision EvaluatePeerUpdate(
        SecureSessionKeyUpdate update,
        uint localSendEpoch,
        uint localReceiveEpoch,
        uint? pendingLocalEpoch)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (localReceiveEpoch == uint.MaxValue
            || update.NextEpoch != localReceiveEpoch + 1)
        {
            throw new InvalidDataException(
                "The peer KeyUpdate must advance the receive epoch by exactly one.");
        }

        if (pendingLocalEpoch is uint pending
            && pending != update.NextEpoch)
        {
            throw new InvalidDataException(
                "The peer KeyUpdate does not match the pending local epoch.");
        }

        bool completesLocalRequest = pendingLocalEpoch == update.NextEpoch;
        if (!update.RequestPeerUpdate)
        {
            if (!completesLocalRequest)
            {
                throw new InvalidDataException(
                    "The peer sent an unsolicited KeyUpdate response.");
            }

            return new SecureSessionPeerKeyUpdateDecision(
                update.NextEpoch,
                SendResponse: false,
                CompletesLocalRequest: true);
        }

        if (localSendEpoch >= update.NextEpoch)
        {
            return new SecureSessionPeerKeyUpdateDecision(
                update.NextEpoch,
                SendResponse: false,
                completesLocalRequest);
        }

        if (localSendEpoch == uint.MaxValue
            || localSendEpoch + 1 != update.NextEpoch)
        {
            throw new InvalidDataException(
                "The peer requested a KeyUpdate with a send-epoch gap.");
        }

        return new SecureSessionPeerKeyUpdateDecision(
            update.NextEpoch,
            SendResponse: true,
            completesLocalRequest);
    }
}

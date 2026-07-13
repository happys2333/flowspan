namespace Flowspan.Security;

public enum InboundHandshakeProtocol
{
    Pairing,
    AuthenticatedSession,
}

public static class HandshakeProtocolClassifier
{
    private const int EnvelopeBytes = 5;
    private const byte HelloKind = 1;

    public static InboundHandshakeProtocol ClassifyInitialHello(
        ReadOnlySpan<byte> message)
    {
        if (message.Length is < EnvelopeBytes
            or > PairingWireCodec.MaximumMessageBytes
            || message[4] != HelloKind)
        {
            throw new InvalidDataException(
                "The first inbound frame is not a supported handshake hello.");
        }

        if (message[..4].SequenceEqual("FSP1"u8))
        {
            return InboundHandshakeProtocol.Pairing;
        }

        if (message[..4].SequenceEqual("FSH1"u8))
        {
            return InboundHandshakeProtocol.AuthenticatedSession;
        }

        throw new InvalidDataException(
            "The first inbound frame is not a supported handshake hello.");
    }
}

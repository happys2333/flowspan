using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class HandshakeProtocolClassifierTests
{
    [Theory]
    [InlineData("FSP1", InboundHandshakeProtocol.Pairing)]
    [InlineData("FSH1", InboundHandshakeProtocol.AuthenticatedSession)]
    public void CanonicalHelloEnvelopeSelectsExactlyOneProtocol(
        string magic,
        InboundHandshakeProtocol expected)
    {
        byte[] message = [.. System.Text.Encoding.ASCII.GetBytes(magic), 1];

        InboundHandshakeProtocol actual =
            HandshakeProtocolClassifier.ClassifyInitialHello(message);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("46")]
    [InlineData("46535031")]
    [InlineData("4653503102")]
    [InlineData("4653483102")]
    [InlineData("554E4B3101")]
    public void TruncatedWrongKindAndUnknownFamiliesAreRejected(string messageHex)
    {
        byte[] message = Convert.FromHexString(messageHex);

        Assert.Throws<InvalidDataException>(() =>
            HandshakeProtocolClassifier.ClassifyInitialHello(message));
    }

    [Fact]
    public void OversizedInitialFrameIsRejectedBeforeFamilySelection()
    {
        byte[] message = new byte[PairingWireCodec.MaximumMessageBytes + 1];
        "FSP1"u8.CopyTo(message);
        message[4] = 1;

        Assert.Throws<InvalidDataException>(() =>
            HandshakeProtocolClassifier.ClassifyInitialHello(message));
    }

    [Fact]
    public void SeededHostileSelectorsStayInsideTheFormatErrorContract()
    {
        var random = new Random(0x4653_5031);
        for (int index = 0; index < 512; index++)
        {
            byte[] message = new byte[random.Next(0, 65)];
            random.NextBytes(message);

            Exception? failure = Record.Exception(() =>
                HandshakeProtocolClassifier.ClassifyInitialHello(message));

            Assert.IsType<InvalidDataException>(failure);
        }
    }
}

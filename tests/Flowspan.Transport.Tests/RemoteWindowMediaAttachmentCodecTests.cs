using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flowspan.Domain;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowMediaAttachmentCodecTests
{
    private static readonly DeviceId InitiatorDeviceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId ResponderDeviceId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly ActivityId ActivityId =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void RequestAndAcknowledgementRoundTripExactBindings()
    {
        byte[] initiatorNonce = Enumerable.Range(0, 32)
            .Select(static value => checked((byte)value))
            .ToArray();
        byte[] responderNonce = Enumerable.Range(32, 32)
            .Select(static value => checked((byte)value))
            .ToArray();
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSecureSessions();
        using (initiator)
        using (responder)
        {
            RemoteWindowMediaRouteBinding binding = CreateBinding(initiator);
            Assert.Equal(
                binding.RouteId,
                RemoteWindowMediaRouteId.FromSession(responder));
            byte[] encodedRequest = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                binding,
                initiatorNonce,
                initiator);
            RemoteWindowMediaAttachmentRequest request =
                RemoteWindowMediaAttachmentCodec.DecodeRequest(
                    encodedRequest,
                    responder);

            byte[] encodedAcknowledgement =
                RemoteWindowMediaAttachmentCodec.EncodeAcknowledgement(
                    request.Binding,
                    request.ExportInitiatorNonce(),
                    responderNonce,
                    responder);
            RemoteWindowMediaAttachmentAcknowledgement acknowledgement =
                RemoteWindowMediaAttachmentCodec.DecodeAcknowledgement(
                    encodedAcknowledgement,
                    initiator);

            Assert.Equal(binding, request.Binding);
            Assert.Equal(initiatorNonce, request.ExportInitiatorNonce());
            Assert.Equal(binding, acknowledgement.Binding);
            Assert.Equal(
                initiatorNonce,
                acknowledgement.ExportInitiatorNonce());
            Assert.Equal(
                responderNonce,
                acknowledgement.ExportResponderNonce());
            Assert.True(RemoteWindowMediaAttachmentCodec.HasMagic(encodedRequest));
            Assert.True(
                RemoteWindowMediaAttachmentCodec.HasMagic(encodedAcknowledgement));
            Assert.Equal<ulong>(1, initiator.NextSendSequence);
            Assert.Equal<ulong>(1, initiator.NextReceiveSequence);
            Assert.Equal<ulong>(1, responder.NextSendSequence);
            Assert.Equal<ulong>(1, responder.NextReceiveSequence);
        }
    }

    [Fact]
    public void ProtocolOnePointFiveCannotCreateMediaRouteBinding()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSecureSessions();
        using (initiator)
        using (responder)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                RemoteWindowMediaRouteBinding.Create(
                    ProtocolFeatures.RemoteWindowMinimumVersion,
                    InitiatorDeviceId,
                    ResponderDeviceId,
                    RemoteWindowMediaRouteId.FromSession(initiator),
                    SessionId,
                    ActivityId));
        }
    }

    [Theory]
    [InlineData(0, 0x58)]
    [InlineData(4, 0x02)]
    [InlineData(5, 0x02)]
    [InlineData(6, 0x01)]
    [InlineData(7, 0x01)]
    [InlineData(15, 0x05)]
    [InlineData(16, 0xff)]
    [InlineData(35, 0xa5)]
    public void UnsupportedOrMismatchedClearEnvelopeIsRejectedBeforeDecryption(
        int offset,
        byte value)
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSecureSessions();
        using (initiator)
        using (responder)
        {
            byte[] encoded = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                CreateBinding(initiator),
                Enumerable.Repeat((byte)0x11, 32).ToArray(),
                initiator);
            encoded[offset] = value;

            Assert.ThrowsAny<Exception>(() =>
                RemoteWindowMediaAttachmentCodec.DecodeRequest(
                    encoded,
                    responder));
            Assert.Equal<ulong>(0, responder.NextReceiveSequence);
        }
    }

    [Fact]
    public void TruncatedTrailingAndTamperedEnvelopesAreRejected()
    {
        AssertRejected(static encoded => encoded[..^1]);
        AssertRejected(static encoded => [.. encoded, 0x00]);
        AssertRejected(static encoded =>
        {
            encoded[^1] ^= 0x01;
            return encoded;
        });
    }

    [Fact]
    public void RouteAndBindingDiagnosticsOmitAllIdentifiers()
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSecureSessions();
        using (initiator)
        using (responder)
        {
            RemoteWindowMediaRouteBinding binding = CreateBinding(initiator);
            byte[] routeBytes = initiator.ExportSessionIdentifier();
            string routeDiagnostic = binding.RouteId.ToString();
            string bindingDiagnostic = binding.ToString();

            Assert.Equal(nameof(RemoteWindowMediaRouteId), routeDiagnostic);
            Assert.DoesNotContain(
                Convert.ToHexString(routeBytes),
                routeDiagnostic,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                InitiatorDeviceId.ToString(),
                bindingDiagnostic,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                ResponderDeviceId.ToString(),
                bindingDiagnostic,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                SessionId.ToString(),
                bindingDiagnostic,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                ActivityId.ToString(),
                bindingDiagnostic,
                StringComparison.OrdinalIgnoreCase);
            CryptographicOperations.ZeroMemory(routeBytes);
        }
    }

    [Fact]
    public void ProtocolOnePointSixGoldenFixtureIsStableAndDecodable()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "remote-window-media-attachment-v1.6.json");
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = fixture.RootElement;
        byte[] initiatorNonce = Convert.FromHexString(
            root.GetProperty("initiatorNonce").GetString()!);
        byte[] responderNonce = Convert.FromHexString(
            root.GetProperty("responderNonce").GetString()!);
        JsonElement requestFixture = root.GetProperty("request");
        JsonElement acknowledgementFixture = root.GetProperty("acknowledgement");
        byte[] frozenRequest = Convert.FromHexString(
            requestFixture.GetProperty("frameHex").GetString()!);
        byte[] frozenAcknowledgement = Convert.FromHexString(
            acknowledgementFixture.GetProperty("frameHex").GetString()!);
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSecureSessions();
        using (initiator)
        using (responder)
        {
            RemoteWindowMediaRouteBinding binding = CreateBinding(initiator);
            byte[] route = initiator.ExportSessionIdentifier();
            byte[] request = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                binding,
                initiatorNonce,
                initiator);
            _ = RemoteWindowMediaAttachmentCodec.DecodeRequest(request, responder);
            byte[] acknowledgement =
                RemoteWindowMediaAttachmentCodec.EncodeAcknowledgement(
                    binding,
                    initiatorNonce,
                    responderNonce,
                    responder);
            RemoteWindowMediaAttachmentAcknowledgement decoded =
                RemoteWindowMediaAttachmentCodec.DecodeAcknowledgement(
                    frozenAcknowledgement,
                    initiator);

            Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
            Assert.Equal("1.6", root.GetProperty("protocol").GetString());
            Assert.Equal(
                root.GetProperty("routeId").GetString(),
                Convert.ToHexString(route));
            Assert.Equal(frozenRequest, request);
            Assert.Equal(
                requestFixture.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(frozenRequest)));
            Assert.Equal(frozenAcknowledgement, acknowledgement);
            Assert.Equal(
                acknowledgementFixture.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(frozenAcknowledgement)));
            Assert.Equal(binding, decoded.Binding);
            Assert.Equal(initiatorNonce, decoded.ExportInitiatorNonce());
            Assert.Equal(responderNonce, decoded.ExportResponderNonce());
        }
    }

    private static RemoteWindowMediaRouteBinding CreateBinding(
        SecureFrameSession mediaSession) =>
        RemoteWindowMediaRouteBinding.Create(
            ProtocolFeatures.RemoteWindowMediaRouteMinimumVersion,
            InitiatorDeviceId,
            ResponderDeviceId,
            RemoteWindowMediaRouteId.FromSession(mediaSession),
            SessionId,
            ActivityId);

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateSecureSessions()
    {
        byte[] secret = Enumerable.Repeat((byte)0x33, 32).ToArray();
        byte[] transcriptHash = SHA256.HashData(
            Encoding.ASCII.GetBytes("authenticated-media-attachment-fixture"));
        using SecureSessionKeyMaterial material =
            SecureSessionKeyMaterial.DeriveRemoteWindowMedia(
                secret,
                transcriptHash);
        CryptographicOperations.ZeroMemory(secret);
        CryptographicOperations.ZeroMemory(transcriptHash);
        return (
            material.CreateSession(SecureSessionRole.Initiator),
            material.CreateSession(SecureSessionRole.Responder));
    }

    private static void AssertRejected(Func<byte[], byte[]> mutate)
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSecureSessions();
        using (initiator)
        using (responder)
        {
            byte[] encoded = RemoteWindowMediaAttachmentCodec.EncodeRequest(
                CreateBinding(initiator),
                Enumerable.Repeat((byte)0x11, 32).ToArray(),
                initiator);

            Assert.ThrowsAny<Exception>(() =>
                RemoteWindowMediaAttachmentCodec.DecodeRequest(
                    mutate(encoded),
                    responder));
            Assert.Equal<ulong>(0, responder.NextReceiveSequence);
        }
    }
}

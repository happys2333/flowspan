using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class RemoteWindowControlMessageCodecTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 8, 9, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId HostId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId ParticipantId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly ActivityId ActivityId =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly RemoteWindowSessionId SessionId =
        RemoteWindowSessionId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly CorrelationId CorrelationId =
        CorrelationId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void AllFiveProtocolOnePointFiveFramesAndHashesMatchFrozenFixture()
    {
        RemoteWindowAdmissionRequest admission = CreateAdmission();
        RemoteWindowDriverRequest driver = RemoteWindowDriverRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            expectedEpoch: 7,
            TimeSpan.FromSeconds(30),
            Now.AddSeconds(5));
        RemoteWindowInputRequest input = RemoteWindowInputRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            leaseEpoch: 8,
            RemoteInputBatch.Create([RemoteInputEvent.PointerMove(0.25, 0.75)]),
            Now.AddSeconds(2));
        RemoteWindowDisconnectRequest disconnect =
            RemoteWindowDisconnectRequest.Create(
                CorrelationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                lastKnownRevision: 12,
                "participant_closed",
                Now.AddSeconds(5));
        RemoteWindowParticipantState state = RemoteWindowParticipantState.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            RemoteWindowControlAction.Input,
            RemoteWindowControlOutcome.Rejected,
            "secure_input",
            RemoteWindowLifecycle.ProtectionPaused,
            RemoteWindowCaptureState.Paused,
            participantCount: 2,
            MirrorParticipantRole.DriverEligible,
            ParticipantId,
            driverLeaseEpoch: 8,
            Now.AddSeconds(30),
            ProtectionKind.SecureInput,
            revision: 13);
        (string Name, ControlMessage Message)[] messages =
        [
            ("admission", Freeze(
                RemoteWindowControlMessageCodec.CreateAdmission(
                    ProtocolFeatures.RemoteWindowMinimumVersion,
                    ParticipantId,
                    admission,
                    Now),
                "01010101-0101-0101-0101-010101010101")),
            ("driver", Freeze(
                RemoteWindowControlMessageCodec.CreateDriverRequest(
                    ProtocolFeatures.RemoteWindowMinimumVersion,
                    ParticipantId,
                    driver,
                    Now),
                "02020202-0202-0202-0202-020202020202")),
            ("input", Freeze(
                RemoteWindowControlMessageCodec.CreateInputRequest(
                    ProtocolFeatures.RemoteWindowMinimumVersion,
                    ParticipantId,
                    input,
                    Now),
                "03030303-0303-0303-0303-030303030303")),
            ("disconnect", Freeze(
                RemoteWindowControlMessageCodec.CreateDisconnect(
                    ProtocolFeatures.RemoteWindowMinimumVersion,
                    ParticipantId,
                    disconnect,
                    Now),
                "04040404-0404-0404-0404-040404040404")),
            ("state", Freeze(
                RemoteWindowControlMessageCodec.CreateState(
                    ProtocolFeatures.RemoteWindowMinimumVersion,
                    HostId,
                    state,
                    Now),
                "05050505-0505-0505-0505-050505050505")),
        ];
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "remote-window-control-v1.5.json");
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(fixturePath));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("1.5", root.GetProperty("protocol").GetString());
        JsonElement[] fixtures = root.GetProperty("fixtures")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(messages.Length, fixtures.Length);
        for (int index = 0; index < messages.Length; index++)
        {
            (string name, ControlMessage message) = messages[index];
            JsonElement fixture = fixtures[index];
            byte[] frame = ControlMessageCodec.Encode(message);
            Assert.Equal(name, fixture.GetProperty("name").GetString());
            Assert.Equal(
                fixture.GetProperty("frame").GetString(),
                Encoding.UTF8.GetString(frame));
            Assert.Equal(
                fixture.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(frame)));
            Assert.Equal(
                frame,
                ControlMessageCodec.Encode(ControlMessageCodec.Decode(frame)));
        }
    }

    [Fact]
    public void AdmissionRoundTripsEveryAuthenticatedLiveSessionBinding()
    {
        RemoteWindowAdmissionRequest request = RemoteWindowAdmissionRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.DriverEligible,
            Now.AddSeconds(5));

        ControlMessage message = RemoteWindowControlMessageCodec.CreateAdmission(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            ParticipantId,
            request,
            Now);
        RemoteWindowAdmissionRequest decoded =
            RemoteWindowControlMessageCodec.DecodeAdmission(message, HostId);

        Assert.Equal(request, decoded);
        Assert.Equal(ControlMessageType.RemoteWindowAdmission, message.Type);
        Assert.Equal(ParticipantId, message.SenderDeviceId);
        Assert.Equal(CorrelationId, message.CorrelationId);
        Assert.Equal(5_000, message.TimeToLiveMilliseconds);
    }

    [Fact]
    public void DriverRequestRoundTripsExpectedEpochAndBoundedDuration()
    {
        RemoteWindowDriverRequest request = RemoteWindowDriverRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            expectedEpoch: 7,
            TimeSpan.FromSeconds(30),
            Now.AddSeconds(5));

        ControlMessage message = RemoteWindowControlMessageCodec.CreateDriverRequest(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            ParticipantId,
            request,
            Now);
        RemoteWindowDriverRequest decoded =
            RemoteWindowControlMessageCodec.DecodeDriverRequest(message, HostId);

        Assert.Equal(request, decoded);
        Assert.Equal(ControlMessageType.RemoteWindowDriver, message.Type);
        Assert.Equal(7, decoded.ExpectedEpoch);
        Assert.Equal(TimeSpan.FromSeconds(30), decoded.LeaseDuration);
    }

    [Fact]
    public void InputRequestRoundTripsClosedBatchUnderExactLeaseEpoch()
    {
        RemoteInputBatch batch = RemoteInputBatch.Create(
        [
            RemoteInputEvent.HidKeyDown(0x07, 0x04),
            RemoteInputEvent.PointerMove(0.25, 0.75),
            RemoteInputEvent.PointerButtonDown(RemotePointerButton.Primary),
            RemoteInputEvent.Scroll(-120, 240),
        ]);
        RemoteWindowInputRequest request = RemoteWindowInputRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            leaseEpoch: 8,
            batch,
            Now.AddSeconds(2));

        ControlMessage message = RemoteWindowControlMessageCodec.CreateInputRequest(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            ParticipantId,
            request,
            Now);
        RemoteWindowInputRequest decoded =
            RemoteWindowControlMessageCodec.DecodeInputRequest(message, HostId);

        Assert.Equal(request.CorrelationId, decoded.CorrelationId);
        Assert.Equal(request.SessionId, decoded.SessionId);
        Assert.Equal(request.ActivityId, decoded.ActivityId);
        Assert.Equal(request.HostDeviceId, decoded.HostDeviceId);
        Assert.Equal(request.ParticipantDeviceId, decoded.ParticipantDeviceId);
        Assert.Equal(request.LeaseEpoch, decoded.LeaseEpoch);
        Assert.Equal(request.Deadline, decoded.Deadline);
        Assert.Equal(4, decoded.Batch.Events.Count);
        Assert.Equal(RemoteInputEventKind.HidKeyDown, decoded.Batch.Events[0].Kind);
        Assert.Equal(0.25, decoded.Batch.Events[1].NormalizedX);
        Assert.Equal(RemotePointerButton.Primary, decoded.Batch.Events[2].PointerButton);
        Assert.Equal(240, decoded.Batch.Events[3].VerticalScroll);
        Assert.Equal(ControlMessageType.RemoteWindowInput, message.Type);
    }

    [Fact]
    public void DisconnectRoundTripsLastKnownRevisionWithoutPayload()
    {
        RemoteWindowDisconnectRequest request = RemoteWindowDisconnectRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            lastKnownRevision: 12,
            "participant_closed",
            Now.AddSeconds(5));

        ControlMessage message = RemoteWindowControlMessageCodec.CreateDisconnect(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            ParticipantId,
            request,
            Now);
        RemoteWindowDisconnectRequest decoded =
            RemoteWindowControlMessageCodec.DecodeDisconnect(message, HostId);

        Assert.Equal(request, decoded);
        Assert.Equal(ControlMessageType.RemoteWindowDisconnect, message.Type);
        Assert.DoesNotContain("payload", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StateRoundTripsParticipantDriverAndProtectionWithoutInput()
    {
        RemoteWindowParticipantState state = RemoteWindowParticipantState.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            RemoteWindowControlAction.Input,
            RemoteWindowControlOutcome.Rejected,
            "secure_input",
            RemoteWindowLifecycle.ProtectionPaused,
            RemoteWindowCaptureState.Paused,
            participantCount: 2,
            MirrorParticipantRole.DriverEligible,
            ParticipantId,
            driverLeaseEpoch: 8,
            Now.AddSeconds(30),
            ProtectionKind.SecureInput,
            revision: 13);

        ControlMessage message = RemoteWindowControlMessageCodec.CreateState(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            HostId,
            state,
            Now);
        RemoteWindowParticipantState decoded =
            RemoteWindowControlMessageCodec.DecodeState(
                message,
                ParticipantId,
                SessionId,
                ActivityId);

        Assert.Equal(state, decoded);
        Assert.Equal(ControlMessageType.RemoteWindowState, message.Type);
        Assert.Equal(HostId, message.SenderDeviceId);
        Assert.DoesNotContain("events", message.Body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("title", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload", message.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdmissionRejectsUnknownFieldsBeforeControllerWork()
    {
        RemoteWindowAdmissionRequest request = CreateAdmission();
        ControlMessage message = RemoteWindowControlMessageCodec.CreateAdmission(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            ParticipantId,
            request,
            Now);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body["unexpected"] = true;

        ControlMessage hostile = Rebuild(message, body.ToJsonString());

        Assert.Throws<InvalidDataException>(
            () => RemoteWindowControlMessageCodec.DecodeAdmission(hostile, HostId));
    }

    [Fact]
    public void InputRejectsFieldsOutsideItsEventDiscriminant()
    {
        RemoteInputBatch batch = RemoteInputBatch.Create(
            [RemoteInputEvent.HidKeyDown(0x07, 0x04)]);
        RemoteWindowInputRequest request = RemoteWindowInputRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            leaseEpoch: 8,
            batch,
            Now.AddSeconds(2));
        ControlMessage message = RemoteWindowControlMessageCodec.CreateInputRequest(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            ParticipantId,
            request,
            Now);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body["events"]![0]!["x"] = 0.5;

        ControlMessage hostile = Rebuild(message, body.ToJsonString());

        Assert.Throws<InvalidDataException>(
            () => RemoteWindowControlMessageCodec.DecodeInputRequest(hostile, HostId));
    }

    [Fact]
    public void StateRejectsWrongLiveSessionEvenWhenEnvelopeIsAuthenticated()
    {
        RemoteWindowParticipantState state = RemoteWindowParticipantState.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            RemoteWindowControlAction.Admission,
            RemoteWindowControlOutcome.Applied,
            "participant_updated",
            RemoteWindowLifecycle.Active,
            RemoteWindowCaptureState.Capturing,
            participantCount: 2,
            MirrorParticipantRole.ViewOnly,
            HostId,
            driverLeaseEpoch: 1,
            Now.AddSeconds(30),
            ProtectionKind.Safe,
            revision: 2);
        ControlMessage message = RemoteWindowControlMessageCodec.CreateState(
            ProtocolFeatures.RemoteWindowMinimumVersion,
            HostId,
            state,
            Now);

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodeState(
                message,
                ParticipantId,
                RemoteWindowSessionId.From(Guid.NewGuid()),
                ActivityId));
    }

    private static RemoteWindowAdmissionRequest CreateAdmission() =>
        RemoteWindowAdmissionRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.DriverEligible,
            Now.AddSeconds(5));

    private static ControlMessage Rebuild(ControlMessage message, string body) =>
        ControlMessage.Create(
            message.Version,
            message.Type,
            message.MessageId,
            message.CorrelationId,
            message.SenderDeviceId,
            message.SentAt,
            TimeSpan.FromMilliseconds(message.TimeToLiveMilliseconds),
            body);

    private static ControlMessage Freeze(
        ControlMessage message,
        string messageId) => ControlMessage.Create(
            message.Version,
            message.Type,
            Guid.Parse(messageId),
            message.CorrelationId,
            message.SenderDeviceId,
            message.SentAt,
            TimeSpan.FromMilliseconds(message.TimeToLiveMilliseconds),
            message.Body.GetRawText());

}

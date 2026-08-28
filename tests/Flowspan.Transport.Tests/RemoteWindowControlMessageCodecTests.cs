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
    public void AllThreeProtocolOnePointSevenFramesAndHashesMatchFrozenFixture()
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.DriverEligible,
            Now.AddSeconds(5));
        (string Name, ControlMessage Message)[] messages =
        [
            ("prepare", Freeze(
                RemoteWindowControlMessageCodec.CreatePrepare(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    HostId,
                    request,
                    Now),
                "06060606-0606-0606-0606-060606060606")),
            ("ready-success", Freeze(
                RemoteWindowControlMessageCodec.CreateReady(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    ParticipantId,
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Ready,
                        "participant_ready"),
                    Now.AddSeconds(1)),
                "07070707-0707-0707-0707-070707070707")),
            ("ready-rejection", Freeze(
                RemoteWindowControlMessageCodec.CreateReady(
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    ParticipantId,
                    RemoteWindowPreparationResponse.Create(
                        request,
                        RemoteWindowPreparationOutcome.Rejected,
                        "participant_busy"),
                    Now.AddSeconds(2)),
                "08080808-0808-0808-0808-080808080808")),
        ];
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "remote-window-preparation-v1.7.json");
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(fixturePath));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("1.7", root.GetProperty("protocol").GetString());
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

            if (message.Type is ControlMessageType.RemoteWindowPrepare)
            {
                Assert.Equal(
                    request,
                    RemoteWindowControlMessageCodec.DecodePrepare(
                        ControlMessageCodec.Decode(frame),
                        ParticipantId,
                        ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
            }
            else
            {
                RemoteWindowPreparationResponse decoded =
                    RemoteWindowControlMessageCodec.DecodeReady(
                        ControlMessageCodec.Decode(frame),
                        HostId,
                        ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                        request);
                Assert.Equal(
                    index == 1
                        ? RemoteWindowPreparationOutcome.Ready
                        : RemoteWindowPreparationOutcome.Rejected,
                    decoded.Outcome);
            }
        }
    }

    [Fact]
    public void PreparationDigestMatchesFrozenKnownAnswer()
    {
        const string canonical =
            "flowspan.remote-window.prepare.v1\n"
            + "1\n"
            + "7\n"
            + "cccccccc-cccc-cccc-cccc-cccccccccccc\n"
            + "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\n"
            + "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\n"
            + "11111111-1111-1111-1111-111111111111\n"
            + "22222222-2222-2222-2222-222222222222\n"
            + "driver-eligible\n"
            + "1786179605000";
        const string expected =
            "EBE76E7CFB02474A44691A16A4F31497F50A68E75584E085F1266B252BB65700";
        Assert.Equal(
            expected,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));

        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.DriverEligible,
            Now.AddSeconds(5));
        ControlMessage message = RemoteWindowControlMessageCodec.CreatePrepare(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            HostId,
            request,
            Now);

        Assert.Equal(
            expected,
            message.Body.GetProperty("prepareDigest").GetString());
    }

    [Fact]
    public void PreparationRoundTripsHostSelectedLiveBinding()
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.DriverEligible,
            Now.AddSeconds(5));

        ControlMessage message = RemoteWindowControlMessageCodec.CreatePrepare(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            HostId,
            request,
            Now);
        RemoteWindowPreparationRequest decoded =
            RemoteWindowControlMessageCodec.DecodePrepare(
                message,
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);

        Assert.Equal(request, decoded);
        Assert.Equal(ControlMessageType.RemoteWindowPrepare, message.Type);
        Assert.Equal(HostId, message.SenderDeviceId);
        Assert.Equal(CorrelationId, message.CorrelationId);
        Assert.Equal(5_000, message.TimeToLiveMilliseconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparationEnvelopeCanonicalizesSubMillisecondSendTime(bool ready)
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.DriverEligible,
            Now.AddSeconds(5));
        DateTimeOffset expectedSentAt = Now.AddSeconds(ready ? 1 : 0);
        DateTimeOffset observedSentAt = expectedSentAt.AddTicks(3_410);

        ControlMessage message = ready
            ? RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    request,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"),
                observedSentAt)
            : RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                request,
                observedSentAt);

        Assert.Equal(expectedSentAt, message.SentAt);
        Assert.Equal(ready ? 4_000 : 5_000, message.TimeToLiveMilliseconds);
        if (ready)
        {
            Assert.Equal(
                RemoteWindowPreparationOutcome.Ready,
                RemoteWindowControlMessageCodec.DecodeReady(
                    message,
                    HostId,
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                    request).Outcome);
        }
        else
        {
            Assert.Equal(
                request,
                RemoteWindowControlMessageCodec.DecodePrepare(
                    message,
                    ParticipantId,
                    ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
        }
    }

    [Fact]
    public void PreparationRejectsNonCanonicalDigestCase()
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));
        ControlMessage message = RemoteWindowControlMessageCodec.CreatePrepare(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            HostId,
            request,
            Now);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body["prepareDigest"] = body["prepareDigest"]!.GetValue<string>().ToLowerInvariant();

        ControlMessage nonCanonical = Rebuild(message, body.ToJsonString());

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodePrepare(
                nonCanonical,
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Theory]
    [InlineData("00")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public void PreparationRejectsMalformedOrMismatchedDigest(string digest)
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));
        ControlMessage message = RemoteWindowControlMessageCodec.CreatePrepare(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            HostId,
            request,
            Now);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body["prepareDigest"] = digest;

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodePrepare(
                Rebuild(message, body.ToJsonString()),
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Theory]
    [InlineData("sessionId", "dddddddd-dddd-dddd-dddd-dddddddddddd")]
    [InlineData("activityId", "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")]
    [InlineData("hostDeviceId", "33333333-3333-3333-3333-333333333333")]
    [InlineData("participantDeviceId", "44444444-4444-4444-4444-444444444444")]
    [InlineData("requestedRole", "view-only")]
    [InlineData("deadline", "2026-08-08T09:00:04+00:00")]
    public void PreparationRejectsEveryTamperedBindingComponent(
        string property,
        string value)
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.DriverEligible,
            Now.AddSeconds(5));
        ControlMessage message = RemoteWindowControlMessageCodec.CreatePrepare(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            HostId,
            request,
            Now);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body[property] = value;

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodePrepare(
                Rebuild(message, body.ToJsonString()),
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Fact]
    public void PreparationRequiresWholeMillisecondUtcDeadline()
    {
        Assert.Throws<ArgumentException>(() =>
            RemoteWindowPreparationRequest.Create(
                CorrelationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.AddTicks(1)));
        Assert.Throws<ArgumentException>(() =>
            RemoteWindowPreparationRequest.Create(
                CorrelationId,
                SessionId,
                ActivityId,
                HostId,
                ParticipantId,
                MirrorParticipantRole.ViewOnly,
                Now.ToOffset(TimeSpan.FromHours(1)).AddSeconds(5)));
    }

    [Fact]
    public void PreparationDecoderRejectsUnknownAndSubMillisecondFields()
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));
        ControlMessage message = RemoteWindowControlMessageCodec.CreatePrepare(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            HostId,
            request,
            Now);
        JsonObject unknown = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        unknown["unexpected"] = true;
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodePrepare(
                Rebuild(message, unknown.ToJsonString()),
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion));

        JsonObject fractional = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        fractional["deadline"] = "2026-08-08T09:00:05.0000001+00:00";
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodePrepare(
                Rebuild(message, fractional.ToJsonString()),
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Theory]
    [InlineData(false, "2026-08-08T09:00:05Z")]
    [InlineData(false, "2026-08-08T09:00:05.000+00:00")]
    [InlineData(false, "2026-08-08T09:00:05.0000+00:00")]
    [InlineData(true, "2026-08-08T09:00:05Z")]
    [InlineData(true, "2026-08-08T09:00:05.000+00:00")]
    [InlineData(true, "2026-08-08T09:00:05.0000+00:00")]
    public void PreparationDecodersRejectNoncanonicalUtcDeadlineSpellings(
        bool readyMessage,
        string deadline)
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        ControlMessage message = CreatePreparationMessage(readyMessage, request);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body["deadline"] = deadline;

        Assert.Throws<InvalidDataException>(() =>
            DecodePreparationMessage(
                readyMessage,
                Rebuild(message, body.ToJsonString()),
                request));
    }

    [Theory]
    [MemberData(nameof(CanonicalPreparationTimestampCases))]
    public void PreparationFramesUseCanonicalWholeMillisecondUtcSpellings(
        bool readyMessage,
        int milliseconds,
        string fractionalPart)
    {
        DateTimeOffset deadline = Now.AddSeconds(5).AddMilliseconds(milliseconds);
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.DriverEligible,
            deadline);
        DateTimeOffset sentAt = Now
            .AddSeconds(readyMessage ? 1 : 0)
            .AddMilliseconds(milliseconds);
        ControlMessage message = readyMessage
            ? RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    request,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"),
                sentAt)
            : RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                request,
                sentAt);

        Assert.Equal(
            $"2026-08-08T09:00:05{fractionalPart}+00:00",
            message.Body.GetProperty("deadline").GetString());
        byte[] frame = ControlMessageCodec.Encode(message);
        using JsonDocument document = JsonDocument.Parse(frame);
        Assert.Equal(
            $"2026-08-08T09:00:0{(readyMessage ? 1 : 0)}{fractionalPart}+00:00",
            document.RootElement.GetProperty("sentAt").GetString());

        ControlMessage decoded = ControlMessageCodec.Decode(frame);
        _ = DecodePreparationMessage(readyMessage, decoded, request);
    }

    [Theory]
    [InlineData(false, "2026-08-08T09:00:00Z")]
    [InlineData(false, "2026-08-08T09:00:00.000+00:00")]
    [InlineData(false, "2026-08-08T09:00:00.0000+00:00")]
    [InlineData(true, "2026-08-08T09:00:01Z")]
    [InlineData(true, "2026-08-08T09:00:01.000+00:00")]
    [InlineData(true, "2026-08-08T09:00:01.0000+00:00")]
    public void PreparationFrameDecoderRejectsNoncanonicalEnvelopeSentAtSpelling(
        bool readyMessage,
        string sentAt)
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        ControlMessage message = CreatePreparationMessage(readyMessage, request);
        JsonObject envelope = JsonNode.Parse(
            Encoding.UTF8.GetString(ControlMessageCodec.Encode(message)))!.AsObject();
        envelope["sentAt"] = sentAt;

        Assert.Throws<InvalidDataException>(() =>
        {
            ControlMessage decoded = ControlMessageCodec.Decode(
                Encoding.UTF8.GetBytes(envelope.ToJsonString()));
            _ = DecodePreparationMessage(readyMessage, decoded, request);
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PreparationMessageBuilderRejectsNoncanonicalEnvelopeSentAt(
        bool readyMessage,
        bool subMillisecond)
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        ControlMessage message = CreatePreparationMessage(readyMessage, request);
        DateTimeOffset sentAt = subMillisecond
            ? message.SentAt.AddTicks(1)
            : message.SentAt.ToOffset(TimeSpan.FromHours(1));

        Assert.Throws<ArgumentException>(() =>
            RebuildEnvelope(message, sentAt: sentAt));
    }

    [Fact]
    public void PreparationSchemaErrorsDoNotReflectUnknownFieldNames()
    {
        const string canary = "raw-secret-canary";
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));
        ControlMessage prepare = RemoteWindowControlMessageCodec.CreatePrepare(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            HostId,
            request,
            Now);
        JsonObject prepareBody = JsonNode.Parse(prepare.Body.GetRawText())!.AsObject();
        prepareBody[canary] = true;
        InvalidDataException prepareFailure = Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodePrepare(
                Rebuild(prepare, prepareBody.ToJsonString()),
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion));

        ControlMessage ready = RemoteWindowControlMessageCodec.CreateReady(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            ParticipantId,
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready"),
            Now.AddSeconds(1));
        JsonObject readyBody = JsonNode.Parse(ready.Body.GetRawText())!.AsObject();
        readyBody[canary] = true;
        InvalidDataException readyFailure = Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodeReady(
                Rebuild(ready, readyBody.ToJsonString()),
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request));

        Assert.DoesNotContain(
            canary,
            prepareFailure.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            canary,
            readyFailure.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReadyRoundTripsExactPreparationAndBoundedOutcome()
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.DriverEligible,
            Now.AddSeconds(5));
        RemoteWindowPreparationResponse response =
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready");

        ControlMessage message = RemoteWindowControlMessageCodec.CreateReady(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            ParticipantId,
            response,
            Now.AddSeconds(1));
        RemoteWindowPreparationResponse decoded =
            RemoteWindowControlMessageCodec.DecodeReady(
                message,
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request);

        Assert.Equal(response, decoded);
        Assert.Equal(ControlMessageType.RemoteWindowReady, message.Type);
        Assert.Equal(ParticipantId, message.SenderDeviceId);
        Assert.Equal(CorrelationId, message.CorrelationId);
        Assert.Equal(4_000, message.TimeToLiveMilliseconds);
        Assert.Contains("prepareDigest", message.Body.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, "participant_busy")]
    [InlineData(false, "participant_ready")]
    [InlineData(false, "unknown_reason")]
    public void ReadyRejectsInvalidOutcomeReasonPairs(bool ready, string reasonCode)
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));
        ControlMessage message = RemoteWindowControlMessageCodec.CreateReady(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            ParticipantId,
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready"),
            Now.AddSeconds(1));
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body["ready"] = ready;
        body["reasonCode"] = reasonCode;

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodeReady(
                Rebuild(message, body.ToJsonString()),
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request));
    }

    [Fact]
    public void ReadyRejectsDifferentPendingCorrelation()
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));
        ControlMessage message = RemoteWindowControlMessageCodec.CreateReady(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            ParticipantId,
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready"),
            Now.AddSeconds(1));
        ControlMessage wrongCorrelation = ControlMessage.Create(
            message.Version,
            message.Type,
            message.MessageId,
            CorrelationId.From(Guid.NewGuid()),
            message.SenderDeviceId,
            message.SentAt,
            TimeSpan.FromMilliseconds(message.TimeToLiveMilliseconds),
            message.Body.GetRawText());

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodeReady(
                wrongCorrelation,
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request));
    }

    [Fact]
    public void ReadyRejectsDifferentPendingProtocolVersion()
    {
        RemoteWindowPreparationRequest request = RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));
        var newerVersion = new ProtocolVersion(1, 8);
        ControlMessage newerReady = RemoteWindowControlMessageCodec.CreateReady(
            newerVersion,
            ParticipantId,
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready"),
            Now.AddSeconds(1));

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodeReady(
                newerReady,
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request));
    }

    [Theory]
    [InlineData("sessionId", "dddddddd-dddd-dddd-dddd-dddddddddddd")]
    [InlineData("activityId", "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")]
    [InlineData("hostDeviceId", "33333333-3333-3333-3333-333333333333")]
    [InlineData("participantDeviceId", "44444444-4444-4444-4444-444444444444")]
    [InlineData("requestedRole", "view-only")]
    [InlineData("deadline", "2026-08-08T09:00:06+00:00")]
    [InlineData(
        "prepareDigest",
        "0000000000000000000000000000000000000000000000000000000000000000")]
    public void ReadyRejectsEveryTamperedEchoedPreparationField(
        string property,
        string value)
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        ControlMessage message = CreatePreparationMessage(readyMessage: true, request);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body[property] = value;
        int timeToLiveMilliseconds = property == "deadline"
            ? 5_000
            : message.TimeToLiveMilliseconds;

        ControlMessage hostile = RebuildEnvelope(
            message,
            body: body.ToJsonString(),
            timeToLiveMilliseconds: timeToLiveMilliseconds);

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodeReady(
                hostile,
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request));
    }

    [Fact]
    public void ReadyRejectsDigestFromAnotherPreparationCorrelation()
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        RemoteWindowPreparationRequest otherRequest =
            RemoteWindowPreparationRequest.Create(
                CorrelationId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                request.SessionId,
                request.ActivityId,
                request.HostDeviceId,
                request.ParticipantDeviceId,
                request.RequestedRole,
                request.Deadline);
        ControlMessage message = CreatePreparationMessage(readyMessage: true, request);
        ControlMessage otherPrepare = CreatePreparationMessage(
            readyMessage: false,
            otherRequest);
        string otherDigest = otherPrepare.Body
            .GetProperty("prepareDigest")
            .GetString()!;
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        Assert.NotEqual(
            message.Body.GetProperty("prepareDigest").GetString(),
            otherDigest);
        body["prepareDigest"] = otherDigest;

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodeReady(
                Rebuild(message, body.ToJsonString()),
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request));
    }

    [Theory]
    [InlineData("participant_busy")]
    [InlineData("renderer_unavailable")]
    [InlineData("renderer_start_failed")]
    [InlineData("media_unavailable")]
    [InlineData("media_attachment_failed")]
    [InlineData("role_unsupported")]
    [InlineData("preparation_expired")]
    [InlineData("preparation_cancelled")]
    [InlineData("participant_stopping")]
    public void ReadyDecodesEveryAllowlistedRejectionReason(string reasonCode)
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        RemoteWindowPreparationResponse response =
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Rejected,
                reasonCode);
        ControlMessage message = RemoteWindowControlMessageCodec.CreateReady(
            ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
            ParticipantId,
            response,
            Now.AddSeconds(1));

        RemoteWindowPreparationResponse decoded =
            RemoteWindowControlMessageCodec.DecodeReady(
                message,
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request);

        Assert.Equal(response, decoded);
        Assert.Equal(RemoteWindowPreparationOutcome.Rejected, decoded.Outcome);
        Assert.Equal(reasonCode, decoded.ReasonCode);
    }

    [Fact]
    public void PreparationCreatorsRejectWrongDirectionalSender()
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        RemoteWindowPreparationResponse response =
            RemoteWindowPreparationResponse.Create(
                request,
                RemoteWindowPreparationOutcome.Ready,
                "participant_ready");

        Assert.Throws<ArgumentException>(() =>
            RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                request,
                Now));
        Assert.Throws<ArgumentException>(() =>
            RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                response,
                Now.AddSeconds(1)));
    }

    [Fact]
    public void PreparationDecodersRejectWrongAuthenticatedSenderAndPeer()
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        ControlMessage prepare = CreatePreparationMessage(readyMessage: false, request);
        ControlMessage ready = CreatePreparationMessage(readyMessage: true, request);

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodePrepare(
                RebuildEnvelope(prepare, senderDeviceId: ParticipantId),
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodePrepare(
                prepare,
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodeReady(
                RebuildEnvelope(ready, senderDeviceId: HostId),
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request));
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodeReady(
                ready,
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request));
    }

    [Fact]
    public void PreparationDecoderRejectsDifferentNegotiatedVersion()
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        ControlMessage message = RemoteWindowControlMessageCodec.CreatePrepare(
            new ProtocolVersion(1, 8),
            HostId,
            request,
            Now);

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodePrepare(
                message,
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
    }

    [Fact]
    public void PreparationDecodersRejectTheOppositeMessageDirection()
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        ControlMessage prepare = CreatePreparationMessage(readyMessage: false, request);
        ControlMessage ready = CreatePreparationMessage(readyMessage: true, request);

        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodePrepare(
                ready,
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion));
        Assert.Throws<InvalidDataException>(() =>
            RemoteWindowControlMessageCodec.DecodeReady(
                prepare,
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request));
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 1)]
    public void PreparationDecodersRejectAnyEnvelopeTtlDeadlineMismatch(
        bool readyMessage,
        int timeToLiveDeltaMilliseconds)
    {
        RemoteWindowPreparationRequest request = CreatePreparation();
        ControlMessage message = CreatePreparationMessage(readyMessage, request);
        ControlMessage hostile = RebuildEnvelope(
            message,
            timeToLiveMilliseconds:
                message.TimeToLiveMilliseconds + timeToLiveDeltaMilliseconds);

        Assert.Throws<InvalidDataException>(() =>
            DecodePreparationMessage(readyMessage, hostile, request));
    }

    [Theory]
    [MemberData(nameof(RequiredPreparationSchemaCases))]
    public void PreparationSchemasRejectEveryMalformedRequiredFieldWithoutReflectingValues(
        bool readyMessage,
        string field,
        string mutation)
    {
        const string canary = "raw-secret-canary";
        RemoteWindowPreparationRequest request = CreatePreparation();
        ControlMessage message = CreatePreparationMessage(readyMessage, request);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        switch (mutation)
        {
            case "missing":
                Assert.True(body.Remove(field));
                break;
            case "null":
                body[field] = null;
                break;
            case "wrong-type":
                body[field] = new JsonObject { ["value"] = canary };
                break;
            default:
                throw new InvalidOperationException("Unknown schema mutation.");
        }

        ControlMessage hostile = Rebuild(message, body.ToJsonString());
        InvalidDataException failure = Assert.Throws<InvalidDataException>(() =>
            DecodePreparationMessage(readyMessage, hostile, request));

        Assert.DoesNotContain(canary, failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "duplicate")]
    [InlineData(false, "unknown")]
    [InlineData(true, "duplicate")]
    [InlineData(true, "unknown")]
    public void PreparationSchemasRejectDuplicateOrUnknownFieldsWithoutReflectingValues(
        bool readyMessage,
        string mutation)
    {
        const string canary = "raw-secret-canary";
        RemoteWindowPreparationRequest request = CreatePreparation();
        ControlMessage message = CreatePreparationMessage(readyMessage, request);

        InvalidDataException failure;
        if (mutation == "duplicate")
        {
            string body = message.Body.GetRawText();
            string duplicateBody = body.Insert(
                body.Length - 1,
                $",\"sessionId\":\"{canary}\"");
            failure = Assert.Throws<InvalidDataException>(() =>
                Rebuild(message, duplicateBody));
        }
        else
        {
            JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
            body[canary] = canary;
            ControlMessage hostile = Rebuild(message, body.ToJsonString());
            failure = Assert.Throws<InvalidDataException>(() =>
                DecodePreparationMessage(readyMessage, hostile, request));
        }

        Assert.DoesNotContain(canary, failure.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void LegacyReadersAcceptAlternateUtcDeadlineSpelling(int protocolMinor)
    {
        RemoteWindowAdmissionRequest request = CreateAdmission();
        ControlMessage message = RemoteWindowControlMessageCodec.CreateAdmission(
            new ProtocolVersion(1, protocolMinor),
            ParticipantId,
            request,
            Now);
        JsonObject body = JsonNode.Parse(message.Body.GetRawText())!.AsObject();
        body["deadline"] = "2026-08-08T09:00:05Z";

        RemoteWindowAdmissionRequest decoded =
            RemoteWindowControlMessageCodec.DecodeAdmission(
                Rebuild(message, body.ToJsonString()),
                HostId);

        Assert.Equal(request, decoded);
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

    public static TheoryData<bool, string, string> RequiredPreparationSchemaCases()
    {
        string[] prepareFields =
        [
            "activityId",
            "deadline",
            "hostDeviceId",
            "participantDeviceId",
            "prepareDigest",
            "requestedRole",
            "sessionId",
        ];
        string[] readyFields = [.. prepareFields, "ready", "reasonCode"];
        string[] mutations = ["missing", "null", "wrong-type"];
        var cases = new TheoryData<bool, string, string>();
        foreach (bool readyMessage in new[] { false, true })
        {
            foreach (string field in readyMessage ? readyFields : prepareFields)
            {
                foreach (string mutation in mutations)
                {
                    cases.Add(readyMessage, field, mutation);
                }
            }
        }

        return cases;
    }

    public static TheoryData<bool, int, string> CanonicalPreparationTimestampCases()
    {
        (int Milliseconds, string FractionalPart)[] timestamps =
        [
            (0, string.Empty),
            (1, ".001"),
            (10, ".01"),
            (100, ".1"),
            (120, ".12"),
            (123, ".123"),
        ];
        var cases = new TheoryData<bool, int, string>();
        foreach (bool readyMessage in new[] { false, true })
        {
            foreach ((int milliseconds, string fractionalPart) in timestamps)
            {
                cases.Add(readyMessage, milliseconds, fractionalPart);
            }
        }

        return cases;
    }

    private static RemoteWindowPreparationRequest CreatePreparation() =>
        RemoteWindowPreparationRequest.Create(
            CorrelationId,
            SessionId,
            ActivityId,
            HostId,
            ParticipantId,
            MirrorParticipantRole.DriverEligible,
            Now.AddSeconds(5));

    private static ControlMessage CreatePreparationMessage(
        bool readyMessage,
        RemoteWindowPreparationRequest request) => readyMessage
            ? RemoteWindowControlMessageCodec.CreateReady(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                ParticipantId,
                RemoteWindowPreparationResponse.Create(
                    request,
                    RemoteWindowPreparationOutcome.Ready,
                    "participant_ready"),
                Now.AddSeconds(1))
            : RemoteWindowControlMessageCodec.CreatePrepare(
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                HostId,
                request,
                Now);

    private static object DecodePreparationMessage(
        bool readyMessage,
        ControlMessage message,
        RemoteWindowPreparationRequest request) => readyMessage
            ? RemoteWindowControlMessageCodec.DecodeReady(
                message,
                HostId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion,
                request)
            : RemoteWindowControlMessageCodec.DecodePrepare(
                message,
                ParticipantId,
                ProtocolFeatures.RemoteWindowPreparationMinimumVersion);

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

    private static ControlMessage RebuildEnvelope(
        ControlMessage message,
        string? body = null,
        DeviceId? senderDeviceId = null,
        int? timeToLiveMilliseconds = null,
        DateTimeOffset? sentAt = null) => ControlMessage.Create(
            message.Version,
            message.Type,
            message.MessageId,
            message.CorrelationId,
            senderDeviceId ?? message.SenderDeviceId,
            sentAt ?? message.SentAt,
            TimeSpan.FromMilliseconds(
                timeToLiveMilliseconds ?? message.TimeToLiveMilliseconds),
            body ?? message.Body.GetRawText());

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

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Flowspan.Application;
using Flowspan.Domain;
using Flowspan.Platform;
using Flowspan.Protocol;
using Flowspan.Security;
using Flowspan.Transport;

namespace Flowspan.Transport.Tests;

public sealed class AuthenticatedControlSessionDispatcherIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 4, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId SourceId =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId TargetId =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ProtocolOnePointFiveCorrelatesAllOutboundFamiliesOnOneConnection()
    {
        ProtocolVersion version = ProtocolFeatures.RemoteWindowMinimumVersion;
        CapabilityGrant capabilities = CapabilityGrant.Of(
            Capability.ActivityReplace,
            Capability.ActivitySwap,
            Capability.SceneApply,
            Capability.MirrorView);
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Target");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(sourceIdentity.PublicIdentity, Now, capabilities),
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(targetIdentity.PublicIdentity, Now, capabilities),
                [version]);
        await using AuthenticatedTcpControlConnection rawTargetConnection =
            await accepting;
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(SourceId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task sourceRun = handler.RunAsync(sourceConnection, stop.Token).AsTask();

        Assert.True(handler.TryGetReplaceChannel(
            TargetId,
            out IReplaceChannel? replaceChannel));
        Assert.True(handler.TryGetSwapChannel(
            TargetId,
            out ISwapEndpointChannel? swapChannel));
        Assert.True(handler.TryGetSceneSourceLookupChannel(
            TargetId,
            out ISceneSourceLookupChannel? sceneChannel));
        Assert.True(handler.TryGetRemoteWindowChannel(
            TargetId,
            out IRemoteWindowControlChannel? remoteWindowChannel));
        Assert.NotNull(replaceChannel);
        Assert.NotNull(swapChannel);
        Assert.NotNull(sceneChannel);
        Assert.NotNull(remoteWindowChannel);

        ActivityDescriptor targetDescriptor = ActivityDescriptor.Create(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ActivityKind.Parse("workspace.note/v1"),
            TargetId,
            "Target note",
            JsonSerializer.Serialize(new { text = "target" }));
        ActivityDescriptor incomingDescriptor = ActivityDescriptor.Create(
            ActivityId.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ActivityKind.Parse("workspace.note/v1"),
            SourceId,
            "Incoming note",
            JsonSerializer.Serialize(new { text = "incoming" }));
        ReplaceActivityCommand replaceCommand = ReplaceActivityCommand.Create(
            OperationContext.Create(
                OperationId.Parse("30303030-3030-3030-3030-303030303030"),
                CorrelationId.Parse("40404040-4040-4040-4040-404040404040"),
                Now.AddSeconds(30)),
            targetDescriptor.Id,
            expectedTargetRevision: 7,
            targetDescriptor.DescriptorDigest,
            incomingDescriptor,
            ActivityPlacement.On(TargetId, "desktop"),
            Now.AddMinutes(10));
        var replaceResult = new ReplaceOperationResult(
            OperationReceipt.Committed(
                replaceCommand.Context.OperationId,
                replaceCommand.Context.CorrelationId,
                OperationKind.Replace,
                SourceId,
                TargetId,
                incomingDescriptor,
                Now),
            new UndoCapsuleReference(
                UndoCapsuleId.Parse("50505050-5050-5050-5050-505050505050"),
                replaceCommand.Context.OperationId,
                replaceCommand.Context.CorrelationId,
                TargetId,
                replaceCommand.TargetActivityId,
                replaceCommand.ExpectedTargetRevision,
                replaceCommand.ExpectedTargetDescriptorDigest,
                incomingDescriptor.Id,
                incomingDescriptor.DescriptorDigest,
                replaceCommand.UndoExpiresAt));

        ActivityInstance swapActivity = ActivityInstance.Active(
            ActivityDescriptor.Create(
                ActivityId.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                ActivityKind.Parse("workspace.note/v1"),
                TargetId,
                "Swap note",
                JsonSerializer.Serialize(new { text = "swap" })),
            ActivityPlacement.On(TargetId, "secondary"),
            revision: 9);
        SwapActivitySnapshotQuery swapQuery = SwapActivitySnapshotQuery.Create(
            OperationContext.Create(
                OperationId.Parse("60606060-6060-6060-6060-606060606060"),
                CorrelationId.Parse("70707070-7070-7070-7070-707070707070"),
                Now.AddSeconds(30)),
            TargetId,
            swapActivity.Descriptor.Id);
        SwapActivitySnapshotResult swapResult =
            SwapActivitySnapshotResult.Success(SourceId, swapQuery, swapActivity);

        ActivityId sceneActivityId =
            ActivityId.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        SceneSourceLookupQuery sceneQuery = SceneSourceLookupQuery.Create(
            OperationContext.Create(
                OperationId.Parse("80808080-8080-8080-8080-808080808080"),
                CorrelationId.Parse("90909090-9090-9090-9090-909090909090"),
                Now.AddSeconds(30)),
            TargetId,
            sceneActivityId,
            index: 3);
        SceneSourceSelection sceneSelection = SceneSourceSelection.Create(
            index: 3,
            sceneActivityId,
            revision: 11,
            new string('A', 64),
            ActivityKind.Parse("workspace.note/v1"),
            ActivityPlacement.On(TargetId, "scene-slot"));
        SceneSourceLookup sceneResult = SceneSourceLookup.FromObservation(
            index: 3,
            sceneActivityId,
            [sceneSelection],
            isComplete: true);

        RemoteWindowSessionId remoteWindowSessionId =
            RemoteWindowSessionId.Parse("abababab-abab-abab-abab-abababababab");
        ActivityId remoteWindowActivityId =
            ActivityId.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");
        RemoteWindowAdmissionRequest admission = RemoteWindowAdmissionRequest.Create(
            CorrelationId.Parse("efefefef-efef-efef-efef-efefefefefef"),
            remoteWindowSessionId,
            remoteWindowActivityId,
            TargetId,
            SourceId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));
        RemoteWindowParticipantState remoteWindowResult =
            RemoteWindowParticipantState.Create(
                admission.CorrelationId,
                remoteWindowSessionId,
                remoteWindowActivityId,
                TargetId,
                SourceId,
                RemoteWindowControlAction.Admission,
                RemoteWindowControlOutcome.Applied,
                "admitted",
                RemoteWindowLifecycle.Active,
                RemoteWindowCaptureState.Capturing,
                participantCount: 2,
                MirrorParticipantRole.ViewOnly,
                TargetId,
                driverLeaseEpoch: 1,
                Now.AddSeconds(30),
                ProtectionKind.Safe,
                revision: 1);

        Task<ReplaceDeliveryResult> replacing = replaceChannel.SendAsync(
            SourceId,
            replaceCommand,
            CancellationToken.None).AsTask();
        Task<SwapDeliveryResult<SwapActivitySnapshotResult>> swapping =
            swapChannel.QueryActivityAsync(
                SourceId,
                swapQuery,
                CancellationToken.None).AsTask();
        Task<SceneSourceLookupDeliveryResult> locating = sceneChannel.QuerySourceAsync(
            SourceId,
            sceneQuery,
            CancellationToken.None).AsTask();
        Task<RemoteWindowControlDeliveryResult> admitting =
            remoteWindowChannel.AdmitAsync(
                admission,
                CancellationToken.None).AsTask();

        var requests = new List<ControlMessage>(capacity: 4);
        for (int index = 0; index < 4; index++)
        {
            requests.Add(await rawTargetConnection.ReceiveAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5)));
        }

        Assert.Equal(4, requests.Select(static request => request.Type).Distinct().Count());
        Assert.Contains(requests, request =>
            request.Type == ControlMessageType.ActivityReplace
            && request.CorrelationId == replaceCommand.Context.CorrelationId
            && ActivityControlMessageCodec.DecodeReplace(request, TargetId)
                == replaceCommand);
        Assert.Contains(requests, request =>
            request.Type == ControlMessageType.ActivitySwapSnapshot
            && request.CorrelationId == swapQuery.Context.CorrelationId
            && SwapControlMessageCodec.DecodeSnapshotQuery(request, TargetId)
                == swapQuery);
        Assert.Contains(requests, request =>
            request.Type == ControlMessageType.SceneSourceLookup
            && request.CorrelationId == sceneQuery.Context.CorrelationId
            && SceneControlMessageCodec.DecodeSourceLookupQuery(request, TargetId)
                == sceneQuery);
        Assert.Contains(requests, request =>
            request.Type == ControlMessageType.RemoteWindowAdmission
            && request.CorrelationId == admission.CorrelationId
            && RemoteWindowControlMessageCodec.DecodeAdmission(request, TargetId)
                == admission);

        for (int index = requests.Count - 1; index >= 0; index--)
        {
            ControlMessage response = requests[index].Type switch
            {
                ControlMessageType.ActivityReplace =>
                    ActivityControlMessageCodec.CreateReplaceResult(
                        version,
                        TargetId,
                        replaceResult,
                        Now),
                ControlMessageType.ActivitySwapSnapshot =>
                    SwapControlMessageCodec.CreateSnapshotResult(
                        version,
                        TargetId,
                        swapResult,
                        Now),
                ControlMessageType.SceneSourceLookup =>
                    SceneControlMessageCodec.CreateSourceLookupResult(
                        version,
                        TargetId,
                        SourceId,
                        sceneQuery,
                        sceneResult,
                        Now),
                ControlMessageType.RemoteWindowAdmission =>
                    RemoteWindowControlMessageCodec.CreateState(
                        version,
                        TargetId,
                        remoteWindowResult,
                        Now),
                _ => throw new InvalidDataException(
                    "The raw peer received an unexpected control message."),
            };
            Assert.Equal(requests[index].CorrelationId, response.CorrelationId);
            await rawTargetConnection.SendAsync(response, CancellationToken.None);
        }

        ReplaceDeliveryResult deliveredReplace = await replacing.WaitAsync(
            TimeSpan.FromSeconds(5));
        SwapDeliveryResult<SwapActivitySnapshotResult> deliveredSwap =
            await swapping.WaitAsync(TimeSpan.FromSeconds(5));
        SceneSourceLookupDeliveryResult deliveredScene = await locating.WaitAsync(
            TimeSpan.FromSeconds(5));
        RemoteWindowControlDeliveryResult deliveredRemoteWindow =
            await admitting.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(ActivityDeliveryStatus.Acknowledged, deliveredReplace.Status);
        Assert.Equal(replaceResult, deliveredReplace.Result);
        Assert.Equal(ActivityDeliveryStatus.Acknowledged, deliveredSwap.Status);
        Assert.Equal(swapResult, deliveredSwap.Response);
        Assert.Equal(SceneControlDeliveryStatus.Acknowledged, deliveredScene.Status);
        Assert.Equal(sceneResult, deliveredScene.Result);
        Assert.Equal(
            RemoteWindowControlDeliveryStatus.Acknowledged,
            deliveredRemoteWindow.Status);
        Assert.Equal(remoteWindowResult, deliveredRemoteWindow.State);

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sourceRun.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task RegistrationChangeExposesOnlyStartedNegotiatedRoutes()
    {
        ProtocolVersion version = ProtocolFeatures.RemoteWindowMinimumVersion;
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Target");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [version]);
        await using AuthenticatedTcpControlConnection targetConnection =
            await accepting;
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(SourceId),
            new FixedTimeProvider(Now));
        var admissionRequest = RemoteWindowAdmissionRequest.Create(
            CorrelationId.Parse("abababab-abab-abab-abab-abababababab"),
            RemoteWindowSessionId.Parse("bcbcbcbc-bcbc-bcbc-bcbc-bcbcbcbcbcbc"),
            ActivityId.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd"),
            TargetId,
            SourceId,
            MirrorParticipantRole.ViewOnly,
            Now.AddSeconds(5));
        var admissionStarted =
            new TaskCompletionSource<Task<RemoteWindowControlDeliveryResult>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        int notificationCount = 0;
        void StartAdmissionOnFirstChange()
        {
            if (Interlocked.Increment(ref notificationCount) != 1)
            {
                return;
            }

            try
            {
                if (!handler.TryGetRemoteWindowChannel(
                        TargetId,
                        out IRemoteWindowControlChannel? channel)
                    || channel is null)
                {
                    admissionStarted.TrySetException(
                        new InvalidOperationException(
                            "The first registration change exposed no Remote Window route."));
                    return;
                }

                admissionStarted.TrySetResult(
                    channel.AdmitAsync(admissionRequest, CancellationToken.None).AsTask());
            }
            catch (Exception exception)
            {
                admissionStarted.TrySetException(exception);
            }
        }

        handler.Changed += StartAdmissionOnFirstChange;
        using var stop = new CancellationTokenSource();
        Task run = handler.RunAsync(sourceConnection, stop.Token).AsTask();
        try
        {
            Task<RemoteWindowControlDeliveryResult> admission =
                await admissionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(
                admission.IsCompleted,
                "A newly published registration must not return before its route can send.");
            ControlMessage sent = await targetConnection.ReceiveAsync()
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(ControlMessageType.RemoteWindowAdmission, sent.Type);
            Assert.Equal(
                admissionRequest,
                RemoteWindowControlMessageCodec.DecodeAdmission(sent, TargetId));
        }
        finally
        {
            handler.Changed -= StartAdmissionOnFirstChange;
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                run.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task PreCancelledRunNeverPublishesARegistration()
    {
        ProtocolVersion version = ProtocolFeatures.RemoteWindowMinimumVersion;
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Target");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [version]);
        await using AuthenticatedTcpControlConnection targetConnection =
            await accepting;
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(SourceId),
            new FixedTimeProvider(Now));
        int notificationCount = 0;
        handler.Changed += () => Interlocked.Increment(ref notificationCount);
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.RunAsync(sourceConnection, stop.Token).AsTask());

        Assert.Equal(0, Volatile.Read(ref notificationCount));
        Assert.Empty(handler.GetConnectedPeers());
        Assert.False(handler.TryGetChannel(TargetId, out _));
        Assert.False(handler.TryGetRemoteWindowChannel(TargetId, out _));
        Assert.False(handler.TryGetRemoteWindowPreparationChannel(TargetId, out _));
        Assert.False(handler.TryAcquireRemoteWindowConnection(TargetId, out _));
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    public async Task PreparationChannelExposureMatchesNegotiatedMinor(
        int minor,
        bool expected)
    {
        var version = new ProtocolVersion(1, minor);
        using DeviceIdentity sourceIdentity = DeviceIdentity.Generate(SourceId, "Source");
        using DeviceIdentity targetIdentity = DeviceIdentity.Generate(TargetId, "Target");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 1);
        var endpoint = Assert.IsType<IPEndPoint>(listener.LocalEndpoint);
        Task<AuthenticatedTcpControlConnection> accepting =
            AuthenticatedTcpControlConnection.AcceptAsync(
                listener,
                targetIdentity,
                new TrustRecord(
                    sourceIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [version]).AsTask();
        await using AuthenticatedTcpControlConnection sourceConnection =
            await AuthenticatedTcpControlConnection.ConnectAsync(
                endpoint,
                sourceIdentity,
                new TrustRecord(
                    targetIdentity.PublicIdentity,
                    Now,
                    CapabilityGrant.None),
                [version]);
        await using AuthenticatedTcpControlConnection targetConnection =
            await accepting;
        await using var handler = new AuthenticatedActivitySessionHandler(
            new RejectingActivityPeer(SourceId),
            new FixedTimeProvider(Now));
        using var stop = new CancellationTokenSource();
        Task run = handler.RunAsync(sourceConnection, stop.Token).AsTask();

        Assert.Equal(
            expected,
            handler.TryGetRemoteWindowPreparationChannel(
                TargetId,
                out IRemoteWindowPreparationChannel? channel));
        Assert.Equal(expected, channel is not null);

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            run.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private sealed class RejectingActivityPeer(DeviceId deviceId) : IActivityPeer
    {
        public DeviceId DeviceId { get; } = deviceId;

        public ValueTask<OperationReceipt> ReceiveActivityAsync(
            DeviceId senderDeviceId,
            ActivityTransferOffer offer,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<OperationReceipt>(
                new InvalidOperationException("No inbound Activity was expected."));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

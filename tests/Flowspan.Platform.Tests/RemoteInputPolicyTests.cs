using Flowspan.Domain;
using Flowspan.Platform;

namespace Flowspan.Platform.Tests;

public sealed class RemoteInputPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    private static readonly DeviceId Owner =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId Peer =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void FreshSafeStateAndCurrentLeaseAllowInput()
    {
        (RemoteInputPolicy policy, MirrorSession session) = CreateDriverSession();

        RemoteInputDecision decision = policy.Evaluate(
            session,
            Peer,
            session.DriverLease.Epoch,
            SafeAt(Now.AddSeconds(1)),
            Now.AddSeconds(1));

        Assert.Equal(RemoteInputDecision.Allowed, decision);
    }

    [Fact]
    public void UnknownStaleAndSensitiveProtectionFailClosed()
    {
        (RemoteInputPolicy policy, MirrorSession session) = CreateDriverSession();
        DateTimeOffset evaluationTime = Now.AddSeconds(1);

        Assert.Equal(
            RemoteInputDecision.ProtectionStateUnknown,
            policy.Evaluate(
                session,
                Peer,
                session.DriverLease.Epoch,
                new ProtectionSnapshot(ProtectionKind.Unknown, evaluationTime, "fake"),
                evaluationTime));
        Assert.Equal(
            RemoteInputDecision.ProtectionStateStale,
            policy.Evaluate(
                session,
                Peer,
                session.DriverLease.Epoch,
                SafeAt(evaluationTime.Subtract(TimeSpan.FromSeconds(1))),
                evaluationTime));
        Assert.Equal(
            RemoteInputDecision.SensitiveSurface,
            policy.Evaluate(
                session,
                Peer,
                session.DriverLease.Epoch,
                new ProtectionSnapshot(
                    ProtectionKind.SecureInput,
                    evaluationTime,
                    "fake"),
                evaluationTime));
    }

    [Fact]
    public void ViewOnlyAndStaleLeaseCannotInjectInput()
    {
        var emergencyStop = new EmergencyStopLatch();
        var policy = new RemoteInputPolicy(emergencyStop);
        MirrorSession viewOnly = MirrorSession.Start(
                ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Owner,
                Now,
                TimeSpan.FromSeconds(10))
            .AddParticipant(Peer, MirrorParticipantRole.ViewOnly);

        Assert.Equal(
            RemoteInputDecision.ViewOnly,
            policy.Evaluate(
                viewOnly,
                Peer,
                viewOnly.DriverLease.Epoch,
                SafeAt(Now),
                Now));

        (_, MirrorSession driverSession) = CreateDriverSession();
        Assert.Equal(
            RemoteInputDecision.DriverLeaseDenied,
            policy.Evaluate(
                driverSession,
                Peer,
                driverSession.DriverLease.Epoch - 1,
                SafeAt(Now.AddSeconds(1)),
                Now.AddSeconds(1)));
    }

    [Fact]
    public void EmergencyStopOverridesOtherwiseValidInputUntilLocalReset()
    {
        var emergencyStop = new EmergencyStopLatch();
        var policy = new RemoteInputPolicy(emergencyStop);
        MirrorSession session = CreateSession();

        emergencyStop.Activate();
        RemoteInputDecision stopped = policy.Evaluate(
            session,
            Peer,
            session.DriverLease.Epoch,
            SafeAt(Now.AddSeconds(1)),
            Now.AddSeconds(1));
        emergencyStop.ResetAfterLocalConfirmation();
        RemoteInputDecision reset = policy.Evaluate(
            session,
            Peer,
            session.DriverLease.Epoch,
            SafeAt(Now.AddSeconds(1)),
            Now.AddSeconds(1));

        Assert.Equal(RemoteInputDecision.EmergencyStopped, stopped);
        Assert.Equal(RemoteInputDecision.Allowed, reset);
    }

    [Fact]
    public void FutureProtectionObservationBeyondToleranceIsStale()
    {
        (RemoteInputPolicy policy, MirrorSession session) = CreateDriverSession();

        RemoteInputDecision decision = policy.Evaluate(
            session,
            Peer,
            session.DriverLease.Epoch,
            SafeAt(Now.AddSeconds(2)),
            Now.AddSeconds(1));

        Assert.Equal(RemoteInputDecision.ProtectionStateStale, decision);
    }

    private static (RemoteInputPolicy Policy, MirrorSession Session) CreateDriverSession()
    {
        var emergencyStop = new EmergencyStopLatch();
        return (new RemoteInputPolicy(emergencyStop), CreateSession());
    }

    private static MirrorSession CreateSession() => MirrorSession.Start(
            ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Owner,
            Now,
            TimeSpan.FromSeconds(10))
        .AddParticipant(Peer, MirrorParticipantRole.DriverEligible)
        .TransferDriver(Peer, Now.AddSeconds(1), TimeSpan.FromSeconds(10));

    private static ProtectionSnapshot SafeAt(DateTimeOffset observedAt) => new(
        ProtectionKind.Safe,
        observedAt,
        "fake");
}

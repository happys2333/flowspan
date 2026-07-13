using Flowspan.Domain;

namespace Flowspan.Domain.Tests;

public sealed class MirrorSessionTests
{
    private static readonly ActivityId Activity =
        ActivityId.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly DeviceId Owner =
        DeviceId.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DeviceId Peer =
        DeviceId.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset Now =
        new(2026, 7, 13, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OwnerStartsAsVisibleAuthorizedDriver()
    {
        MirrorSession session = Start();

        Assert.True(session.CanView(Owner));
        Assert.True(session.CanInjectInput(Owner, session.DriverLease.Epoch, Now));
        Assert.False(session.CanView(Peer));
    }

    [Fact]
    public void ViewOnlyParticipantCanNeverReceiveOrUseDriverAuthority()
    {
        MirrorSession session = Start().AddParticipant(
            Peer,
            MirrorParticipantRole.ViewOnly);

        Assert.True(session.CanView(Peer));
        Assert.Throws<InvalidOperationException>(() =>
            session.TransferDriver(Peer, Now.AddSeconds(1), TimeSpan.FromSeconds(10)));
        Assert.False(session.CanInjectInput(Peer, session.DriverLease.Epoch, Now));
    }

    [Fact]
    public void TransferInvalidatesPreviousEpochBeforeNewInput()
    {
        MirrorSession initial = Start().AddParticipant(
            Peer,
            MirrorParticipantRole.DriverEligible);
        long ownerEpoch = initial.DriverLease.Epoch;

        MirrorSession transferred = initial.TransferDriver(
            Peer,
            Now.AddSeconds(1),
            TimeSpan.FromSeconds(10));

        Assert.False(transferred.CanInjectInput(Owner, ownerEpoch, Now.AddSeconds(1)));
        Assert.True(transferred.CanInjectInput(
            Peer,
            transferred.DriverLease.Epoch,
            Now.AddSeconds(1)));
    }

    [Fact]
    public void ExpiredPeerLeaseReturnsToOwnerWithHigherEpoch()
    {
        MirrorSession peerDriven = Start()
            .AddParticipant(Peer, MirrorParticipantRole.DriverEligible)
            .TransferDriver(Peer, Now.AddSeconds(1), TimeSpan.FromSeconds(1));
        long peerEpoch = peerDriven.DriverLease.Epoch;

        MirrorSession refreshed = peerDriven.RefreshExpiredLease(
            Now.AddSeconds(2),
            TimeSpan.FromSeconds(10));

        Assert.False(refreshed.CanInjectInput(Peer, peerEpoch, Now.AddSeconds(2)));
        Assert.Equal(Owner, refreshed.DriverLease.HolderDeviceId);
        Assert.True(refreshed.CanInjectInput(
            Owner,
            refreshed.DriverLease.Epoch,
            Now.AddSeconds(2)));
    }

    [Fact]
    public void RemovingCurrentDriverReturnsAuthorityToOwner()
    {
        MirrorSession peerDriven = Start()
            .AddParticipant(Peer, MirrorParticipantRole.DriverEligible)
            .TransferDriver(Peer, Now.AddSeconds(1), TimeSpan.FromSeconds(10));

        MirrorSession removed = peerDriven.RemoveParticipant(
            Peer,
            Now.AddSeconds(2),
            TimeSpan.FromSeconds(10));

        Assert.False(removed.CanView(Peer));
        Assert.Equal(Owner, removed.DriverLease.HolderDeviceId);
        Assert.True(removed.CanInjectInput(
            Owner,
            removed.DriverLease.Epoch,
            Now.AddSeconds(2)));
    }

    [Fact]
    public void EmergencyStopRevokesEveryEpochUntilLocalResume()
    {
        MirrorSession peerDriven = Start()
            .AddParticipant(Peer, MirrorParticipantRole.DriverEligible)
            .TransferDriver(Peer, Now.AddSeconds(1), TimeSpan.FromSeconds(10));
        long peerEpoch = peerDriven.DriverLease.Epoch;

        MirrorSession stopped = peerDriven.EmergencyStop(Now.AddSeconds(2));

        Assert.Equal(MirrorSessionStatus.EmergencyStopped, stopped.Status);
        Assert.Null(stopped.DriverLease.HolderDeviceId);
        Assert.False(stopped.CanView(Owner));
        Assert.False(stopped.CanInjectInput(Peer, peerEpoch, Now.AddSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => stopped.TransferDriver(
            Owner,
            Now.AddSeconds(3),
            TimeSpan.FromSeconds(10)));

        MirrorSession resumed = stopped.ResumeOwner(
            Now.AddSeconds(3),
            TimeSpan.FromSeconds(10));

        Assert.True(resumed.CanInjectInput(
            Owner,
            resumed.DriverLease.Epoch,
            Now.AddSeconds(3)));
    }

    [Fact]
    public void OnlyLatestEpochAuthorizesAcrossManyTransfers()
    {
        MirrorSession session = Start().AddParticipant(
            Peer,
            MirrorParticipantRole.DriverEligible);
        var previousEpochs = new List<(DeviceId DeviceId, long Epoch)>();

        for (int index = 1; index <= 100; index++)
        {
            previousEpochs.Add((
                session.DriverLease.HolderDeviceId!,
                session.DriverLease.Epoch));
            DeviceId next = index % 2 == 0 ? Owner : Peer;
            session = session.TransferDriver(
                next,
                Now.AddSeconds(index),
                TimeSpan.FromSeconds(10));

            foreach ((DeviceId deviceId, long epoch) in previousEpochs)
            {
                Assert.False(session.CanInjectInput(
                    deviceId,
                    epoch,
                    Now.AddSeconds(index)));
            }

            Assert.True(session.CanInjectInput(
                next,
                session.DriverLease.Epoch,
                Now.AddSeconds(index)));
        }
    }

    [Fact]
    public void LeaseRejectsBackwardsTimeAndUnsafeDuration()
    {
        MirrorSession session = Start();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.TransferDriver(
            Owner,
            Now.AddSeconds(-1),
            TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.TransferDriver(
            Owner,
            Now,
            TimeSpan.Zero));
    }

    private static MirrorSession Start() => MirrorSession.Start(
        Activity,
        Owner,
        Now,
        TimeSpan.FromSeconds(10));
}

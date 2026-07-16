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

    private static readonly DeviceId Third =
        DeviceId.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly DeviceId Fourth =
        DeviceId.Parse("44444444-4444-4444-4444-444444444444");

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
    public void CurrentDriverCannotBeDowngradedWithoutLeaseTransition()
    {
        MirrorSession peerDriven = Start()
            .AddParticipant(Peer, MirrorParticipantRole.DriverEligible)
            .TransferDriver(Peer, Now.AddSeconds(1), TimeSpan.FromSeconds(10));
        long peerEpoch = peerDriven.DriverLease.Epoch;

        Assert.Throws<InvalidOperationException>(() => peerDriven.AddParticipant(
            Peer,
            MirrorParticipantRole.ViewOnly));

        Assert.True(peerDriven.CanInjectInput(Peer, peerEpoch, Now.AddSeconds(1)));
    }

    [Fact]
    public void SafeOwnerCannotBecomeViewOnlyWhilePeerDrives()
    {
        MirrorSession peerDriven = Start()
            .AddParticipant(Peer, MirrorParticipantRole.DriverEligible)
            .TransferDriver(Peer, Now.AddSeconds(1), TimeSpan.FromSeconds(10));

        Assert.Throws<InvalidOperationException>(() => peerDriven.AddParticipant(
            Owner,
            MirrorParticipantRole.ViewOnly));
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

    [Fact]
    public void SeededTransitionSequencesNeverReviveOldDriverAuthority()
    {
        DeviceId[] devices = [Owner, Peer, Third, Fourth];
        DeviceId[] peers = [Peer, Third, Fourth];
        for (int seed = 0; seed < 32; seed++)
        {
            var random = new Random(seed);
            DateTimeOffset now = Now;
            MirrorSession session = TransitionWithTrace(
                $"seed={seed}, phase=start",
                Start);
            var expectedParticipants = new Dictionary<DeviceId, MirrorParticipantRole>
            {
                [Owner] = MirrorParticipantRole.DriverEligible,
            };
            MirrorSessionStatus expectedStatus = MirrorSessionStatus.Active;
            DeviceId? expectedHolder = Owner;
            long expectedEpoch = 1;
            DateTimeOffset expectedIssuedAt = Now;
            DateTimeOffset expectedExpiresAt = Now.AddSeconds(10);
            for (int eventIndex = 0; eventIndex < 128; eventIndex++)
            {
                string prefix = $"seed={seed}, event={eventIndex}";
                try
                {
                    string action;
                    if (expectedStatus == MirrorSessionStatus.EmergencyStopped)
                    {
                        action = random.Next(3) switch
                        {
                            0 => VerifyStoppedAddRejected(),
                            1 => VerifyStoppedTransferRejected(),
                            _ => Resume(),
                        };
                    }
                    else
                    {
                        action = random.Next(5) switch
                        {
                            0 => AddOrUpdateParticipant(),
                            1 => RemovePeer(),
                            2 => TransferDriver(),
                            3 => AdvanceAndRefresh(),
                            _ => EmergencyStop(),
                        };
                    }

                    string trace =
                        $"{prefix}, action={action}, expectedStatus={expectedStatus}, expectedEpoch={expectedEpoch}";
                    AssertMirrorInvariant(
                        session,
                        devices,
                        expectedParticipants,
                        expectedStatus,
                        expectedHolder,
                        expectedEpoch,
                        expectedIssuedAt,
                        expectedExpiresAt,
                        now,
                        trace);
                }
                catch (Exception exception) when (!exception.Message.Contains(
                    prefix,
                    StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{prefix}, unexpected generated-case exception.",
                        exception);
                }

                string AddOrUpdateParticipant()
                {
                    DeviceId participant = devices[random.Next(devices.Length)];
                    MirrorParticipantRole role = random.Next(2) == 0
                        ? MirrorParticipantRole.ViewOnly
                        : MirrorParticipantRole.DriverEligible;
                    if ((participant == Owner || expectedHolder == participant)
                        && role == MirrorParticipantRole.ViewOnly)
                    {
                        Exception? failure = Record.Exception(() =>
                            session.AddParticipant(participant, role));
                        Assert.True(
                            failure is InvalidOperationException,
                            $"{prefix}, action=protected-role-change:{participant}:{role}, exception={failure}");
                        return $"protected-role-change-rejected:{participant}:{role}";
                    }

                    session = TransitionWithTrace(
                        $"{prefix}, action=add:{participant}:{role}",
                        () => session.AddParticipant(participant, role));
                    expectedParticipants[participant] = role;
                    return $"add:{participant}:{role}";
                }

                string RemovePeer()
                {
                    DeviceId peer = peers[random.Next(peers.Length)];
                    session = TransitionWithTrace(
                        $"{prefix}, action=remove:{peer}",
                        () => session.RemoveParticipant(
                            peer,
                            now,
                            TimeSpan.FromSeconds(10)));
                    expectedParticipants.Remove(peer);
                    if (expectedHolder == peer)
                    {
                        expectedHolder = Owner;
                        expectedEpoch++;
                        expectedIssuedAt = now;
                        expectedExpiresAt = now.AddSeconds(10);
                    }

                    return $"remove:{peer}";
                }

                string TransferDriver()
                {
                    DeviceId[] eligible = expectedParticipants
                        .Where(static participant =>
                            participant.Value == MirrorParticipantRole.DriverEligible)
                        .Select(static participant => participant.Key)
                        .ToArray();
                    DeviceId next = eligible[random.Next(eligible.Length)];
                    TimeSpan duration = TimeSpan.FromSeconds(random.Next(1, 11));
                    session = TransitionWithTrace(
                        $"{prefix}, action=transfer:{next}, duration={duration}",
                        () => session.TransferDriver(
                            next,
                            now,
                            duration));
                    expectedHolder = next;
                    expectedEpoch++;
                    expectedIssuedAt = now;
                    expectedExpiresAt = now.Add(duration);
                    return $"transfer:{next}";
                }

                string AdvanceAndRefresh()
                {
                    now = now.AddSeconds(random.Next(1, 16));
                    session = TransitionWithTrace(
                        $"{prefix}, action=refresh:{now:O}",
                        () => session.RefreshExpiredLease(
                            now,
                            TimeSpan.FromSeconds(10)));
                    if (now >= expectedExpiresAt)
                    {
                        expectedHolder = Owner;
                        expectedEpoch++;
                        expectedIssuedAt = now;
                        expectedExpiresAt = now.AddSeconds(10);
                    }

                    return $"refresh:{now:O}";
                }

                string EmergencyStop()
                {
                    session = TransitionWithTrace(
                        $"{prefix}, action=emergency-stop",
                        () => session.EmergencyStop(now));
                    expectedStatus = MirrorSessionStatus.EmergencyStopped;
                    expectedHolder = null;
                    expectedEpoch++;
                    expectedIssuedAt = now;
                    expectedExpiresAt = now;
                    return "emergency-stop";
                }

                string VerifyStoppedAddRejected()
                {
                    Exception? failure = Record.Exception(() => session.AddParticipant(
                        peers[random.Next(peers.Length)],
                        MirrorParticipantRole.DriverEligible));
                    Assert.True(
                        failure is InvalidOperationException,
                        $"{prefix}, action=stopped-add, exception={failure}");
                    return "stopped-add-rejected";
                }

                string VerifyStoppedTransferRejected()
                {
                    Exception? failure = Record.Exception(() => session.TransferDriver(
                        Owner,
                        now,
                        TimeSpan.FromSeconds(10)));
                    Assert.True(
                        failure is InvalidOperationException,
                        $"{prefix}, action=stopped-transfer, exception={failure}");
                    return "stopped-transfer-rejected";
                }

                string Resume()
                {
                    session = TransitionWithTrace(
                        $"{prefix}, action=resume-owner",
                        () => session.ResumeOwner(
                            now,
                            TimeSpan.FromSeconds(10)));
                    expectedStatus = MirrorSessionStatus.Active;
                    expectedHolder = Owner;
                    expectedEpoch++;
                    expectedIssuedAt = now;
                    expectedExpiresAt = now.AddSeconds(10);
                    return "resume-owner";
                }
            }
        }
    }

    private static T TransitionWithTrace<T>(string trace, Func<T> transition)
    {
        try
        {
            return transition();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"{trace}, unexpected generated-transition exception.",
                exception);
        }
    }

    private static void AssertMirrorInvariant(
        MirrorSession session,
        IReadOnlyList<DeviceId> devices,
        Dictionary<DeviceId, MirrorParticipantRole> expectedParticipants,
        MirrorSessionStatus expectedStatus,
        DeviceId? expectedHolder,
        long expectedEpoch,
        DateTimeOffset expectedIssuedAt,
        DateTimeOffset expectedExpiresAt,
        DateTimeOffset now,
        string trace)
    {
        Assert.True(session.Status == expectedStatus, trace);
        Assert.True(session.Participants.Count == expectedParticipants.Count, trace);
        foreach ((DeviceId deviceId, MirrorParticipantRole role) in expectedParticipants)
        {
            Assert.True(
                session.Participants.TryGetValue(
                    deviceId,
                    out MirrorParticipantRole actualRole)
                && actualRole == role,
                trace);
        }

        Assert.True(session.DriverLease.HolderDeviceId == expectedHolder, trace);
        Assert.True(session.DriverLease.Epoch == expectedEpoch, trace);
        Assert.True(session.DriverLease.IssuedAt == expectedIssuedAt, trace);
        Assert.True(session.DriverLease.ExpiresAt == expectedExpiresAt, trace);
        Assert.True(
            session.DriverLease.EmergencyStopped
            == (expectedStatus == MirrorSessionStatus.EmergencyStopped),
            trace);

        int authorized = 0;
        foreach (DeviceId device in devices)
        {
            bool expectedView = expectedStatus == MirrorSessionStatus.Active
                && expectedParticipants.ContainsKey(device);
            Assert.True(session.CanView(device) == expectedView, trace);

            bool canInject = session.CanInjectInput(
                device,
                expectedEpoch,
                now);
            bool expectedCanInject = expectedStatus == MirrorSessionStatus.Active
                && expectedHolder == device
                && expectedParticipants.TryGetValue(
                    device,
                    out MirrorParticipantRole role)
                && role == MirrorParticipantRole.DriverEligible
                && now >= expectedIssuedAt
                && now < expectedExpiresAt;
            Assert.True(canInject == expectedCanInject, trace);
            if (canInject)
            {
                authorized++;
            }

            for (long retiredEpoch = 1; retiredEpoch < expectedEpoch; retiredEpoch++)
            {
                Assert.False(
                    session.CanInjectInput(device, retiredEpoch, now),
                    trace);
            }
        }

        Assert.True(authorized is >= 0 and <= 1, trace);
    }

    private static MirrorSession Start() => MirrorSession.Start(
        Activity,
        Owner,
        Now,
        TimeSpan.FromSeconds(10));
}

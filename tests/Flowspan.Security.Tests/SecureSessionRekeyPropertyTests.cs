using System.Security.Cryptography;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class SecureSessionRekeyPropertyTests
{
    [Theory]
    [InlineData(7301)]
    [InlineData(19081)]
    [InlineData(65537)]
    [InlineData(104729)]
    public void SeededTwoPeerTracesConvergeAcrossRepeatedAndCrossedRequests(
        int seed)
    {
        (SecureFrameSession initiator, SecureFrameSession responder) =
            CreateSessions();
        using (initiator)
        using (responder)
        {
            var random = new Random(seed);
            for (uint targetEpoch = 2; targetEpoch <= 65; targetEpoch++)
            {
                if (random.Next(3) == 0)
                {
                    CompleteCrossedRequest(initiator, responder, targetEpoch);
                }
                else if (random.Next(2) == 0)
                {
                    CompleteSingleRequest(initiator, responder, targetEpoch);
                }
                else
                {
                    CompleteSingleRequest(responder, initiator, targetEpoch);
                }

                Assert.Equal(targetEpoch, initiator.SendEpoch);
                Assert.Equal(targetEpoch, initiator.ReceiveEpoch);
                Assert.Equal(targetEpoch, responder.SendEpoch);
                Assert.Equal(targetEpoch, responder.ReceiveEpoch);
            }
        }
    }

    private static void CompleteSingleRequest(
        SecureFrameSession requester,
        SecureFrameSession responder,
        uint targetEpoch)
    {
        byte[] requestFrame = EncryptUpdate(
            requester,
            requestPeerUpdate: true,
            targetEpoch);
        requester.AdvanceSendEpoch(targetEpoch);

        SecureSessionPeerKeyUpdateDecision responderDecision = ReceiveUpdate(
            responder,
            requestFrame,
            pendingLocalEpoch: null);
        Assert.True(responderDecision.SendResponse);
        Assert.False(responderDecision.CompletesLocalRequest);
        responder.AdvanceReceiveEpoch(responderDecision.NextReceiveEpoch);

        byte[] responseFrame = EncryptUpdate(
            responder,
            requestPeerUpdate: false,
            targetEpoch);
        responder.AdvanceSendEpoch(targetEpoch);

        SecureSessionPeerKeyUpdateDecision requesterDecision = ReceiveUpdate(
            requester,
            responseFrame,
            pendingLocalEpoch: targetEpoch);
        Assert.False(requesterDecision.SendResponse);
        Assert.True(requesterDecision.CompletesLocalRequest);
        requester.AdvanceReceiveEpoch(requesterDecision.NextReceiveEpoch);
    }

    private static void CompleteCrossedRequest(
        SecureFrameSession initiator,
        SecureFrameSession responder,
        uint targetEpoch)
    {
        byte[] initiatorFrame = EncryptUpdate(
            initiator,
            requestPeerUpdate: true,
            targetEpoch);
        byte[] responderFrame = EncryptUpdate(
            responder,
            requestPeerUpdate: true,
            targetEpoch);
        initiator.AdvanceSendEpoch(targetEpoch);
        responder.AdvanceSendEpoch(targetEpoch);

        SecureSessionPeerKeyUpdateDecision responderDecision = ReceiveUpdate(
            responder,
            initiatorFrame,
            pendingLocalEpoch: targetEpoch);
        SecureSessionPeerKeyUpdateDecision initiatorDecision = ReceiveUpdate(
            initiator,
            responderFrame,
            pendingLocalEpoch: targetEpoch);

        Assert.False(responderDecision.SendResponse);
        Assert.True(responderDecision.CompletesLocalRequest);
        Assert.False(initiatorDecision.SendResponse);
        Assert.True(initiatorDecision.CompletesLocalRequest);
        responder.AdvanceReceiveEpoch(responderDecision.NextReceiveEpoch);
        initiator.AdvanceReceiveEpoch(initiatorDecision.NextReceiveEpoch);
    }

    private static byte[] EncryptUpdate(
        SecureFrameSession sender,
        bool requestPeerUpdate,
        uint targetEpoch)
    {
        byte[] plaintext = SecureSessionKeyUpdateCodec.Encode(
            SecureSessionKeyUpdate.Create(requestPeerUpdate, targetEpoch));
        try
        {
            return sender.Encrypt(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static SecureSessionPeerKeyUpdateDecision ReceiveUpdate(
        SecureFrameSession receiver,
        byte[] frame,
        uint? pendingLocalEpoch)
    {
        byte[] plaintext = receiver.Decrypt(frame);
        try
        {
            SecureSessionKeyUpdate update =
                SecureSessionKeyUpdateCodec.Decode(plaintext);
            return SecureSessionRekeyRules.EvaluatePeerUpdate(
                update,
                receiver.SendEpoch,
                receiver.ReceiveEpoch,
                pendingLocalEpoch);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(frame);
        }
    }

    private static (SecureFrameSession Initiator, SecureFrameSession Responder)
        CreateSessions()
    {
        byte[] initiatorKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
        byte[] responderKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
        byte[] sessionIdentifier = Enumerable.Repeat((byte)0x33, 16).ToArray();
        try
        {
            return (
                new SecureFrameSession(
                    initiatorKey,
                    SecureFrameDirection.InitiatorToResponder,
                    responderKey,
                    SecureFrameDirection.ResponderToInitiator,
                    sessionIdentifier),
                new SecureFrameSession(
                    responderKey,
                    SecureFrameDirection.ResponderToInitiator,
                    initiatorKey,
                    SecureFrameDirection.InitiatorToResponder,
                    sessionIdentifier));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(initiatorKey);
            CryptographicOperations.ZeroMemory(responderKey);
            CryptographicOperations.ZeroMemory(sessionIdentifier);
        }
    }
}

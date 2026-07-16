using System.Security.Cryptography;
using Flowspan.Security;

namespace Flowspan.Security.Tests;

public sealed class SecureSessionKeyUpdateTests
{
    [Theory]
    [InlineData("00535231010100000002")]
    [InlineData("46535231020100000002")]
    [InlineData("46535231010200000002")]
    [InlineData("465352310101000000")]
    [InlineData("4653523101010000000200")]
    [InlineData("46535231010100000000")]
    [InlineData("46535231010100000001")]
    public void MalformedKeyUpdateEncodingsAreRejected(string encodedHex)
    {
        byte[] encoded = Convert.FromHexString(encodedHex);

        Assert.Throws<InvalidDataException>(() =>
            SecureSessionKeyUpdateCodec.Decode(encoded));
    }

    [Fact]
    public void CanonicalRequestHasFrozenBytesAndHash()
    {
        SecureSessionKeyUpdate update = SecureSessionKeyUpdate.Create(
            requestPeerUpdate: true,
            nextEpoch: 2);

        byte[] encoded = SecureSessionKeyUpdateCodec.Encode(update);
        SecureSessionKeyUpdate decoded = SecureSessionKeyUpdateCodec.Decode(encoded);

        Assert.Equal("46535231010100000002", Convert.ToHexString(encoded));
        Assert.Equal(
            "919E1A6CECA322B61A0F98612E55C0584189AE166CC6685E8FB775FBDAD71F45",
            Convert.ToHexString(SHA256.HashData(encoded)));
        Assert.True(decoded.RequestPeerUpdate);
        Assert.Equal<uint>(2, decoded.NextEpoch);
    }

    [Fact]
    public void NextDirectionalKeyHasFrozenVector()
    {
        byte[] currentKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
        byte[] sessionIdentifier = Enumerable.Repeat((byte)0x22, 16).ToArray();
        byte[] nextKey = SecureSessionEpochKeyDerivation.DeriveNextKey(
            currentKey,
            sessionIdentifier,
            SecureSessionRole.Initiator,
            nextEpoch: 2);
        try
        {
            Assert.Equal(
                "E1CEE8A87F7D1A22645CE8968C7226F68E7A790AF3C2D07DE8C0D80B80902591",
                Convert.ToHexString(nextKey));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(currentKey);
            CryptographicOperations.ZeroMemory(sessionIdentifier);
            CryptographicOperations.ZeroMemory(nextKey);
        }
    }

    [Fact]
    public void SingleInitiatorRequestProducesOneResponseAndConverges()
    {
        SecureSessionKeyUpdate request = SecureSessionKeyUpdate.Create(
            requestPeerUpdate: true,
            nextEpoch: 2);

        SecureSessionPeerKeyUpdateDecision responder =
            SecureSessionRekeyRules.EvaluatePeerUpdate(
                request,
                localSendEpoch: 1,
                localReceiveEpoch: 1,
                pendingLocalEpoch: null);

        Assert.Equal<uint>(2, responder.NextReceiveEpoch);
        Assert.True(responder.SendResponse);
        Assert.False(responder.CompletesLocalRequest);

        SecureSessionKeyUpdate response = SecureSessionKeyUpdate.Create(
            requestPeerUpdate: false,
            nextEpoch: 2);
        SecureSessionPeerKeyUpdateDecision initiator =
            SecureSessionRekeyRules.EvaluatePeerUpdate(
                response,
                localSendEpoch: 2,
                localReceiveEpoch: 1,
                pendingLocalEpoch: 2);

        Assert.Equal<uint>(2, initiator.NextReceiveEpoch);
        Assert.False(initiator.SendResponse);
        Assert.True(initiator.CompletesLocalRequest);
    }

    [Fact]
    public void CrossedRequestsCompleteWithoutASecondRotation()
    {
        SecureSessionKeyUpdate crossedRequest = SecureSessionKeyUpdate.Create(
            requestPeerUpdate: true,
            nextEpoch: 2);

        SecureSessionPeerKeyUpdateDecision first =
            SecureSessionRekeyRules.EvaluatePeerUpdate(
                crossedRequest,
                localSendEpoch: 2,
                localReceiveEpoch: 1,
                pendingLocalEpoch: 2);
        SecureSessionPeerKeyUpdateDecision second =
            SecureSessionRekeyRules.EvaluatePeerUpdate(
                crossedRequest,
                localSendEpoch: 2,
                localReceiveEpoch: 1,
                pendingLocalEpoch: 2);

        Assert.False(first.SendResponse);
        Assert.True(first.CompletesLocalRequest);
        Assert.False(second.SendResponse);
        Assert.True(second.CompletesLocalRequest);
    }

    [Theory]
    [InlineData(false, 2, 1, 1, -1)]
    [InlineData(true, 3, 2, 1, -1)]
    [InlineData(true, 2, 1, 1, 3)]
    [InlineData(true, 3, 1, 2, -1)]
    [InlineData(true, uint.MaxValue, uint.MaxValue, uint.MaxValue, -1)]
    public void InvalidPeerUpdateTransitionsAreRejected(
        bool requestPeerUpdate,
        uint nextEpoch,
        uint localSendEpoch,
        uint localReceiveEpoch,
        int pendingLocalEpoch)
    {
        SecureSessionKeyUpdate update = SecureSessionKeyUpdate.Create(
            requestPeerUpdate,
            nextEpoch);
        uint? pending = pendingLocalEpoch < 0
            ? null
            : checked((uint)pendingLocalEpoch);

        Assert.Throws<InvalidDataException>(() =>
            SecureSessionRekeyRules.EvaluatePeerUpdate(
                update,
                localSendEpoch,
                localReceiveEpoch,
                pending));
    }
}

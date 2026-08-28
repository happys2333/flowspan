using Flowspan.Protocol;

namespace Flowspan.Transport;

public interface IVerifiedPeerConnectionCandidateValidator
{
    public bool IsCurrent(
        VerifiedPeerConnectionCandidate candidate,
        ProtocolVersion protocolVersion);
}

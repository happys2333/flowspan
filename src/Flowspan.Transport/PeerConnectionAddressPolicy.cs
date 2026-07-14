using System.Net;
using System.Net.Sockets;

namespace Flowspan.Transport;

internal static class PeerConnectionAddressPolicy
{
    public static bool IsUsable(IPAddress address)
    {
        if (address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.Broadcast)
            || IPAddress.IsLoopback(address)
            || address.IsIPv6Multicast
            || (address.AddressFamily == AddressFamily.InterNetworkV6
                && address.IsIPv6LinkLocal
                && address.ScopeId == 0))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte first = address.GetAddressBytes()[0];
            return first is < 224 or > 239;
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6;
    }
}

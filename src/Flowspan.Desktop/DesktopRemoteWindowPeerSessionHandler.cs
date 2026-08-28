using Flowspan.Transport;

namespace Flowspan.Desktop;

internal sealed class DesktopRemoteWindowPeerSessionHandler(
    AuthenticatedActivitySessionHandler inner,
    DesktopRemoteWindowPeerEndpointResolver resolver) :
    IAuthenticatedControlSessionHandler
{
    private readonly AuthenticatedActivitySessionHandler inner = inner
        ?? throw new ArgumentNullException(nameof(inner));
    private readonly DesktopRemoteWindowPeerEndpointResolver resolver = resolver
        ?? throw new ArgumentNullException(nameof(resolver));

    public ValueTask RunAsync(
        AuthenticatedTcpControlConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return resolver.TryResolve(
            connection,
            out VerifiedPeerConnectionCandidate? candidate)
            && candidate is not null
                ? inner.RunWithRemoteWindowPeerAsync(
                    connection,
                    candidate,
                    resolver,
                    cancellationToken)
                : inner.RunAsync(connection, cancellationToken);
    }
}

using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates the address a socket is about to connect to, and opens the
/// socket only to an allowed address.
/// </summary>
/// <remarks>
/// <para>
/// The check happening <strong>here</strong> — inside
/// <see cref="SocketsHttpHandler.ConnectCallback"/> — is the whole point.
/// Validating an address first and then calling
/// <c>HttpClient.SendAsync(url)</c> leaves a TOCTOU gap: <c>HttpClient</c>
/// resolves the name a second time, and an attacker can change the answer
/// between the two resolutions (DNS rebinding). The address validated in the
/// connection callback <em>is the very address the socket connects to</em>;
/// there is no further resolution in between.
/// </para>
/// <para>
/// <c>IHttpClientFactory</c> is deliberately not used, for two reasons: it
/// would add <c>Microsoft.Extensions.Http</c> to the dependency graph, and a
/// consumer reconfiguring the named client could silently remove the protection.
/// </para>
/// <para>
/// The <c>Host</c> header is set from, and preserved as, the original name by
/// <c>HttpClient</c>; TLS validation is also performed against the original name.
/// </para>
/// </remarks>
internal sealed class EgressSocketGuard
{
    private readonly Func<EgressAddressPolicy> _policyAccessor;

    /// <summary>Creates a guard that reads its policy from the shared egress options.</summary>
    /// <param name="options">The egress options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public EgressSocketGuard(IOptionsMonitor<TraconEgressOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _policyAccessor = () => new EgressAddressPolicy(
            options.CurrentValue.AllowPrivateNetworkTargets,
            AllowLoopback: false);
    }

    /// <summary>Creates a guard whose policy is read fresh on every connection.</summary>
    /// <param name="policyAccessor">
    /// Returns the policy in force. Called once per connection, so an option
    /// change takes effect without rebuilding the client.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="policyAccessor"/> is <see langword="null"/>.</exception>
    public EgressSocketGuard(Func<EgressAddressPolicy> policyAccessor)
    {
        ArgumentNullException.ThrowIfNull(policyAccessor);

        _policyAccessor = policyAccessor;
    }

    /// <summary>The delegate to hand to <see cref="SocketsHttpHandler.ConnectCallback"/>.</summary>
    /// <param name="context">The connection context the handler supplies.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A stream over the connected socket.</returns>
    /// <exception cref="TraconException">The target resolves to a rejected address.</exception>
    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var verdict = await EgressAddressValidator
            .ResolveAndValidateAsync(context.DnsEndPoint.Host, _policyAccessor(), cancellationToken)
            .ConfigureAwait(false);

        if (!verdict.IsAllowed)
        {
            throw new TraconException(verdict.Reason ?? "The outbound target was rejected.");
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            // Connects to the validated addresses; the name is not resolved again.
            await socket
                .ConnectAsync(verdict.ResolvedAddresses!, context.DnsEndPoint.Port, cancellationToken)
                .ConfigureAwait(false);

            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }

    /// <summary>Validates a target before it is stored, without opening a connection.</summary>
    /// <param name="target">The target address.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The target resolves to a rejected address.</exception>
    /// <remarks>
    /// This is a convenience for callers that already hold a resolved target.
    /// The save-time endpoints use <see cref="EgressAddressValidator.ValidateLiteral"/>
    /// instead, so that a name which does not resolve yet is not rejected.
    /// </remarks>
    public async ValueTask ValidateAsync(Uri target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var verdict = await EgressAddressValidator
            .ResolveAndValidateAsync(target.Host, _policyAccessor(), cancellationToken)
            .ConfigureAwait(false);

        if (!verdict.IsAllowed)
        {
            throw new TraconException(verdict.Reason ?? "The outbound target was rejected.");
        }
    }

    /// <summary>Builds a handler whose every connection passes through this guard.</summary>
    /// <returns>The handler. The caller owns it.</returns>
    /// <remarks>
    /// Every surface that reaches the network builds its client from this
    /// method. A path that built its own <see cref="SocketsHttpHandler"/>
    /// would silently be unprotected.
    /// </remarks>
    public SocketsHttpHandler CreateHandler()
        => new()
        {
            ConnectCallback = ConnectAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),

            // A proxy would defeat the guard silently: the connection callback
            // would see the PROXY's address and judge that, while the real
            // target travels inside the CONNECT request and is never examined.
            // An ambient HTTPS_PROXY environment variable is enough to trigger
            // that, so the default is turned off rather than inherited.
            UseProxy = false,
        };

    /// <summary>Builds an <see cref="HttpClient"/> whose every connection passes through this guard.</summary>
    /// <param name="timeout">
    /// The response timeout to apply. Pass <see cref="Timeout.InfiniteTimeSpan"/>
    /// only when the caller bounds every request itself.
    /// </param>
    /// <returns>The client. The caller owns it and must dispose it.</returns>
    /// <remarks>
    /// The timeout is explicit rather than defaulted, because replacing the
    /// transport of an SDK also replaces whatever timeout that SDK's own client
    /// carried. A caller that inherits an infinite timeout by accident hangs
    /// forever on a server that accepts a connection and never answers.
    /// </remarks>
    public HttpClient CreateHttpClient(TimeSpan timeout)
        => new(CreateHandler(), disposeHandler: true)
        {
            Timeout = timeout,
        };
}

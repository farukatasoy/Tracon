using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tracon.Core.UnitTests.Egress;

/// <summary>
/// A host name is resolved once per connection, the answer is judged, and the socket opens to the very addresses that
/// were judged; a second answer, however different, is never asked for.
/// </summary>
/// <remarks>
/// <para>
/// DNS rebinding answers the question "is this name safe" with an address that is, and the question "where do I
/// connect" with one that is not. The guard closes it by asking once. A real resolver cannot be told to answer
/// differently the second time, so the guard is built with one that can: it counts the questions, and its second answer
/// is an address the policy forbids and nothing could reach.
/// </para>
/// <para>
/// The first answer is the loopback address of a real listener, allowed by the policy these tests use. The connection has
/// to arrive there, and the resolver must have been asked exactly once. A guard that resolved again for the connect would
/// be sent to the second answer, and a guard that judged one answer and connected by name would not reach the listener
/// at all.
/// </para>
/// </remarks>
public sealed class EgressRebindingTests
{
    private static readonly EgressAddressPolicy LoopbackAllowed = new(AllowPrivateNetworkTargets: false, AllowLoopback: true);

    private static readonly EgressAddressPolicy LoopbackDenied = new(AllowPrivateNetworkTargets: false, AllowLoopback: false);

    [Fact]
    public async Task The_socket_opens_to_the_address_that_was_judged_and_the_name_is_asked_for_once()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var asked = 0;

        var resolver = new EgressHostResolver((_, _) => ValueTask.FromResult<IPAddress[]>(
            Interlocked.Increment(ref asked) == 1
                ? [IPAddress.Loopback]

                // What an attacker's name answers once it has been judged: a private address nothing can reach.
                : [IPAddress.Parse("10.255.255.1")]));

        var served = ServeOnceAsync(listener, TestContext.Current.CancellationToken);

        using var client = new EgressSocketGuard(static () => LoopbackAllowed, resolver).CreateHttpClient(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(new Uri($"http://rebind.example:{port}/"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await served).ShouldBe(1, "the connection did not arrive at the address that was judged");
        asked.ShouldBe(1, "the name was resolved more than once for one connection");
    }

    [Fact]
    public async Task A_name_that_resolves_to_one_good_and_one_forbidden_address_is_refused_whole()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        // The documentation range stands in for a public address nothing answers; the loopback one is the target.
        var resolver = new EgressHostResolver(static (_, _) => ValueTask.FromResult<IPAddress[]>(
            [IPAddress.Parse("203.0.113.9"), IPAddress.Loopback]));

        using var client = new EgressSocketGuard(static () => LoopbackDenied, resolver).CreateHttpClient(TimeSpan.FromSeconds(10));

        var exception = await Should.ThrowAsync<HttpRequestException>(
            async () => await client.GetAsync(new Uri($"http://mixed.example:{port}/"), TestContext.Current.CancellationToken));

        (exception.InnerException ?? exception).Message.ShouldContain(nameof(TraconEgressOptions.AllowPrivateNetworkTargets));
        listener.Pending().ShouldBeFalse("a connection reached the loopback listener through a name that also resolved to a public address");
    }

    [Fact]
    public async Task Every_new_connection_asks_again_and_is_judged_on_its_own_answer()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var answer = new[] { IPAddress.Loopback };
        var asked = 0;

        var resolver = new EgressHostResolver((_, _) =>
        {
            Interlocked.Increment(ref asked);

            return ValueTask.FromResult(answer);
        });

        // No connection is kept between the two requests, so the second one has to be judged afresh.
        using var handler = new EgressSocketGuard(static () => LoopbackAllowed, resolver).CreateHandler();

        handler.PooledConnectionLifetime = TimeSpan.Zero;

        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) };

        var first = ServeOnceAsync(listener, TestContext.Current.CancellationToken);

        using (var response = await client.GetAsync(new Uri($"http://flip.example:{port}/"), TestContext.Current.CancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        await first;

        // The name now answers with an address the policy forbids; the next connection is refused on that answer.
        answer = [IPAddress.Parse("10.255.255.1")];

        var exception = await Should.ThrowAsync<HttpRequestException>(
            async () => await client.GetAsync(new Uri($"http://flip.example:{port}/"), TestContext.Current.CancellationToken));

        (exception.InnerException ?? exception).Message.ShouldContain("10.255.255.1");
        asked.ShouldBe(2);
    }

    private static async Task<int> ServeOnceAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = connection.GetStream();
            var buffer = new byte[4096];

            _ = await stream.ReadAsync(buffer, timeout.Token);

            var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
            await stream.WriteAsync(response, timeout.Token);

            return 1;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }
}

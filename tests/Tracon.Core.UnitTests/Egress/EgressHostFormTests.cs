using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Tracon.Core.UnitTests.Egress;

/// <summary>
/// A guarded client never opens a socket to the loopback interface, however the address is spelled, and judges every
/// hop of a redirect.
/// </summary>
/// <remarks>
/// <para>
/// A real listener sits on the loopback interface and the test asks it afterwards whether anything connected. A
/// verdict on a parsed address could be right while the spelling reached the socket another way: the decimal and hex
/// forms, the short form, a trailing dot, the IPv4-mapped IPv6 form, and a user-info prefix that makes a public
/// address look like the host. The listener answers that question; the validator tests cannot.
/// </para>
/// <para>
/// No name here needs the network: every host is a literal or <c>localhost</c>, which the resolver answers from the
/// hosts file.
/// </para>
/// </remarks>
public sealed class EgressHostFormTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("2130706433")]
    [InlineData("0x7f000001")]
    [InlineData("0177.0.0.1")]
    [InlineData("127.1")]
    [InlineData("0.0.0.0")]
    [InlineData("localhost")]
    [InlineData("localhost.")]
    [InlineData("[::1]")]
    [InlineData("[::ffff:127.0.0.1]")]
    [InlineData("[::ffff:7f00:1]")]
    [InlineData("user@127.0.0.1")]
    [InlineData("8.8.8.8@127.0.0.1")]
    [InlineData("１２７.０.０.１")]
    public async Task No_spelling_of_the_loopback_address_reaches_a_listener(string host)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var url = $"http://{host}:{port}/";

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // The runtime refuses to build the request at all, so no socket can open.
            return;
        }

        using var client = new EgressSocketGuard(static () => EgressAddressPolicy.Deny)
            .CreateHttpClient(TimeSpan.FromSeconds(5));

        await Should.ThrowAsync<HttpRequestException>(async () => await client.GetAsync(uri, TestContext.Current.CancellationToken));

        listener.Pending().ShouldBeFalse($"a connection reached the loopback listener through '{url}'");
    }

    [Fact]
    public async Task A_redirect_to_a_private_address_is_refused_at_the_second_hop()
    {
        // Loopback is allowed so the first hop can be served from here; the private range is not, so the hop the server
        // names is the one the guard must stop.
        var policy = new EgressAddressPolicy(AllowPrivateNetworkTargets: false, AllowLoopback: true);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var served = ServeRedirectAsync(listener, "http://10.0.0.5:81/internal", TestContext.Current.CancellationToken);

        using var client = new EgressSocketGuard(() => policy).CreateHttpClient(TimeSpan.FromSeconds(5));

        var exception = await Should.ThrowAsync<HttpRequestException>(
            async () => await client.GetAsync(
                new Uri($"http://127.0.0.1:{port}/start"),
                TestContext.Current.CancellationToken));

        (exception.InnerException ?? exception).Message.ShouldContain(nameof(TraconEgressOptions.AllowPrivateNetworkTargets));
        (await served).ShouldBe(1, "the first hop must have been served exactly once before the redirect was judged");
    }

    private static async Task<int> ServeRedirectAsync(TcpListener listener, string location, CancellationToken cancellationToken)
    {
        var served = 0;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            while (!timeout.IsCancellationRequested)
            {
                using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = connection.GetStream();
                var buffer = new byte[4096];

                _ = await stream.ReadAsync(buffer, timeout.Token);

                var response = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 302 Found\r\nLocation: {location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response, timeout.Token);
                served++;

                // One hop is all the test needs; the second must never arrive.
                return served;
            }
        }
        catch (OperationCanceledException)
        {
            // The listener was never asked: the count tells the test.
        }

        return served;
    }
}

using System.Text;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// The scripted server stops without leaving a request it accepted to fail on its own after the test that started it
/// has passed.
/// </summary>
/// <remarks>
/// A fault in a request handler nobody waits for does not fail a test. The test platform reports it at the end of the
/// run and fails the run with every test green. These hold the handler between its request's arrival and its handling,
/// so the order is set by the test and needs no timing.
/// </remarks>
public sealed class ScriptedMcpServerTests
{
    private const string ListRequest = """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""";

    [Fact]
    public async Task A_request_accepted_but_not_yet_handled_when_the_server_stops_does_not_fault_its_handler()
    {
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ScriptedMcpServer.Start(
            static (_, _, _, _) => Task.FromResult<string?>("{}"),
            dispatch: async handle =>
            {
                accepted.TrySetResult();
                await release.Task;
                await handle();
            });

        using var client = new HttpClient();
        var sending = SendChunkedAsync(client, server.Endpoint);

        await accepted.Task.WaitAsync(TestContext.Current.CancellationToken);

        // The server stops while the request it accepted still waits for its handler, then the handler goes on.
        var stopping = server.DisposeAsync().AsTask();
        release.SetResult();

        await stopping;
        _ = await Record.ExceptionAsync(() => sending);
    }

    [Fact]
    public async Task A_handler_that_fails_makes_the_dispose_fail_instead_of_being_dropped()
    {
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = ScriptedMcpServer.Start((_, _, _, _) =>
        {
            called.TrySetResult();

            throw new InvalidOperationException("the script broke");
        });

        using var client = new HttpClient();
        var sending = SendChunkedAsync(client, server.Endpoint);

        await called.Task.WaitAsync(TestContext.Current.CancellationToken);

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => server.DisposeAsync().AsTask());

        failure.Message.ShouldBe("the script broke");
        _ = await Record.ExceptionAsync(() => sending);
    }

    // A body of unknown length goes chunked, which is how the MCP client sends its messages.
    private static async Task SendChunkedAsync(HttpClient client, Uri endpoint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(ListRequest, Encoding.UTF8, "application/json"),
        };

        request.Headers.TransferEncodingChunked = true;

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}

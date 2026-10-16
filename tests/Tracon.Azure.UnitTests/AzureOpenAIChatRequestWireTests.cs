using Microsoft.Extensions.AI;
using Tracon.Azure.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// What a chat call through the Azure OpenAI client puts on the wire, and what
/// it reads back (Phase 214).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <c>Azure.AI.OpenAI</c> 2.1.0 was built against <c>OpenAI</c> 2.1.0 and
/// runs against the 2.14.0 that central package management forces (K-211). The
/// mismatch is silent at build time: the call that breaks does so at run time
/// with <c>MissingMethodException</c>. The other tests in this project stop at
/// the factory and never send a request, so none of them can see it.
/// </para>
/// <para>
/// Each test sends one real request over a loopback socket. Embeddings are not
/// covered: <c>Tracon.Azure</c> offers no embedding client.
/// </para>
/// </remarks>
public sealed class AzureOpenAIChatRequestWireTests
{
    private const string CompletionBody = """
        {"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"production-gpt",
         "choices":[{"index":0,"message":{"role":"assistant","content":"pong"},"finish_reason":"stop"}],
         "usage":{"prompt_tokens":3,"completion_tokens":1,"total_tokens":4}}
        """;

    [Fact]
    public async Task A_chat_call_goes_to_the_deployment_path_and_reads_the_answer()
    {
        await using var server = StubHttpServer.Respond(200, "OK", CompletionBody);
        using var chatClient = Factory(server).CreateChatClient(TestData.Binding());

        var response = await chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("pong");
        response.Usage.ShouldNotBeNull().TotalTokenCount.ShouldBe(4);

        var head = await server.RequestHead;
        head.ShouldStartWith($"POST /openai/deployments/{TestData.Deployment}/chat/completions?api-version=");
        head.ShouldContain($"api-key: {TestData.ApiKey}");

        var body = await server.RequestBody;
        body.ShouldContain("\"ping\"");
    }

    [Fact]
    public async Task A_streamed_chat_call_reads_the_deltas()
    {
        const string stream =
            "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"production-gpt\"," +
            "\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"po\"},\"finish_reason\":null}]}\n\n" +
            "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"production-gpt\"," +
            "\"choices\":[{\"index\":0,\"delta\":{\"content\":\"ng\"},\"finish_reason\":\"stop\"}]}\n\n" +
            "data: [DONE]\n\n";

        await using var server = StubHttpServer.Respond(200, "OK", stream);
        using var chatClient = Factory(server).CreateChatClient(TestData.Binding());

        var text = new System.Text.StringBuilder();

        await foreach (var update in chatClient.GetStreamingResponseAsync(
            "ping", cancellationToken: TestContext.Current.CancellationToken))
        {
            text.Append(update.Text);
        }

        text.ToString().ShouldBe("pong");
        (await server.RequestBody).ShouldContain("\"stream\":true");
    }

    private static AzureOpenAIChatClientFactory Factory(StubHttpServer server)
        => new(TestData.Options(options => options.Endpoint = server.BaseAddress));
}

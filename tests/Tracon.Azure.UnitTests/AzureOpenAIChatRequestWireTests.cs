using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Tracon.Azure.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// What a chat call through the Azure OpenAI client puts on the wire, and what
/// it reads back (Phase 217).
/// </summary>
/// <remarks>
/// <para>
/// Azure OpenAI is reached through the plain <c>OpenAI</c> client against the
/// resource's <c>/openai/v1/</c> endpoint; the deployment name travels in the
/// request body's <c>model</c> field. The other tests in this project stop at
/// the factory and never send a request, so only these can see a wrong path,
/// header or body.
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
    public async Task A_chat_call_goes_to_the_v1_path_with_the_deployment_in_the_body_and_reads_the_answer()
    {
        await using var server = StubHttpServer.Respond(200, "OK", CompletionBody);
        using var chatClient = Factory(server).CreateChatClient(TestData.Binding());

        var response = await chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldBe("pong");
        response.Usage.ShouldNotBeNull().TotalTokenCount.ShouldBe(4);

        var head = await server.RequestHead;
        head.ShouldStartWith("POST /openai/v1/chat/completions HTTP/1.1");
        head.ShouldContain($"Authorization: Bearer {TestData.ApiKey}");
        head.ShouldNotContain("api-version", Case.Insensitive);

        var body = await server.RequestBody;
        body.ShouldContain("\"ping\"");
        body.ShouldContain($"\"model\":\"{TestData.Deployment}\"");
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

    [Theory]
    [InlineData(null, AzureOpenAIProviderHealthCheck.DefaultAudience)]
    [InlineData("https://cognitiveservices.azure.us/.default", "https://cognitiveservices.azure.us/.default")]
    public async Task An_Entra_credential_sends_a_Bearer_token_for_the_requested_scope(string? audience, string expectedScope)
    {
        // The Entra path refuses a non-HTTPS address (a token must never cross
        // plain HTTP), so this test points at the real https address and swaps
        // the transport for one that records the request.
        var handler = new RecordingHandler(CompletionBody);
        var credential = new FakeTokenCredential("entra-token");
        var client = AzureOpenAIChatClientFactory.CreateClient(
            TestData.Options(o =>
            {
                o.ApiKey = null;
                o.Audience = audience;
                o.CredentialFactory = () => credential;
            }),
            transport: new HttpClientPipelineTransport(new HttpClient(handler)));
        using var chatClient = AzureOpenAIChatClientFactory.FromClient(client).CreateChatClient(TestData.Binding());

        await chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        handler.Request.ShouldNotBeNull();
        handler.Request.RequestUri!.AbsoluteUri.ShouldBe($"{TestData.EndpointText}openai/v1/chat/completions");
        handler.Request.Headers.Authorization.ShouldNotBeNull().ToString().ShouldBe("Bearer entra-token");
        handler.Request.Headers.Contains("api-key").ShouldBeFalse();
        credential.LastScope.ShouldBe(expectedScope);
    }

    [Fact]
    public async Task An_Entra_credential_is_refused_over_plain_HTTP_instead_of_sending_the_token()
    {
        await using var server = StubHttpServer.Respond(200, "OK", CompletionBody);
        using var chatClient = new AzureOpenAIChatClientFactory(TestData.Options(o =>
            {
                o.Endpoint = server.BaseAddress;
                o.ApiKey = null;
                o.CredentialFactory = static () => new FakeTokenCredential("entra-token");
            }))
            .CreateChatClient(TestData.Binding());

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("not permitted for non TLS");
    }

    [Theory]
    [InlineData("")]
    [InlineData("openai/v1")]
    [InlineData("openai/v1/")]
    public async Task The_v1_segment_is_added_once_whatever_form_of_the_address_is_given(string suffix)
    {
        await using var server = StubHttpServer.Respond(200, "OK", CompletionBody);
        var endpoint = new Uri(server.BaseAddress, suffix);
        using var chatClient = new AzureOpenAIChatClientFactory(TestData.Options(o => o.Endpoint = endpoint))
            .CreateChatClient(TestData.Binding());

        await chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        (await server.RequestHead).ShouldStartWith("POST /openai/v1/chat/completions HTTP/1.1");
    }

    [Fact]
    public async Task A_gateway_path_prefix_in_front_of_the_resource_address_is_kept()
    {
        await using var server = StubHttpServer.Respond(200, "OK", CompletionBody);
        var endpoint = new Uri(server.BaseAddress, "azure");
        using var chatClient = new AzureOpenAIChatClientFactory(TestData.Options(o => o.Endpoint = endpoint))
            .CreateChatClient(TestData.Binding());

        await chatClient.GetResponseAsync("ping", cancellationToken: TestContext.Current.CancellationToken);

        (await server.RequestHead).ShouldStartWith("POST /azure/openai/v1/chat/completions HTTP/1.1");
    }

    [Fact]
    public async Task A_cancelled_caller_stops_the_call_instead_of_waiting_for_the_server()
    {
        await using var server = StubHttpServer.NeverRespond();
        using var chatClient = Factory(server).CreateChatClient(TestData.Binding());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(
            () => chatClient.GetResponseAsync("ping", cancellationToken: cancellation.Token));
    }

#pragma warning disable MEAI001
    [Fact]
    public async Task An_image_call_goes_to_the_v1_images_path_with_the_deployment_in_the_body()
    {
        await using var server = StubHttpServer.Respond(200, "OK", """{"created":1,"data":[{"b64_json":"aGk="}]}""");
        var generator = Factory(server).CreateImageGenerator("image-deployment");

        var response = await generator.GenerateAsync(
            new ImageGenerationRequest("a red kite"), cancellationToken: TestContext.Current.CancellationToken);

        response.Contents.OfType<DataContent>().ShouldHaveSingleItem();

        var head = await server.RequestHead;
        head.ShouldStartWith("POST /openai/v1/images/generations HTTP/1.1");
        head.ShouldContain($"Authorization: Bearer {TestData.ApiKey}");
        (await server.RequestBody).ShouldContain("\"model\":\"image-deployment\"");
    }
#pragma warning restore MEAI001

    private static AzureOpenAIChatClientFactory Factory(StubHttpServer server)
        => new(TestData.Options(options => options.Endpoint = server.BaseAddress));

    /// <summary>Answers every request with one canned chat completion and keeps the request.</summary>
    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}

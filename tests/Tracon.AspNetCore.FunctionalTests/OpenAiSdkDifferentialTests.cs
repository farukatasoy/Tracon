#pragma warning disable OPENAI001 // The Responses and Conversations clients are marked for evaluation; they are what consumers of these endpoints use.

using System.ClientModel;
using System.Net;
using System.Text;
using System.Text.Json;
using OpenAI.Chat;
using OpenAI.Conversations;
using OpenAI.Responses;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Drives the OpenAI-compatible endpoints with the official OpenAI .NET SDK, the client the consumers of these
/// endpoints actually run.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint tests that hand-write both sides of the wire prove the server matches what its author expected. The SDK
/// proves it matches what OpenAI's clients expect: a field the SDK requires, an event order it relies on, the shape it
/// reads an error from. A mismatch shows up as the SDK's own deserialization failing, not as a status code.
/// </para>
/// <para>
/// Chat Completions here is server-side tools only (the agent owns its tools), so the tool round trip is a model that
/// calls a tool inside the run and the SDK sees the final text; a client that sends tool history or tool definitions is
/// refused with a <c>400</c> the SDK must surface as an exception.
/// </para>
/// </remarks>
public sealed class OpenAiSdkDifferentialTests
{
    private const string Agent = "kod-agent";

    // --- Chat Completions, buffered ---

    [Fact]
    public async Task A_buffered_chat_completion_deserializes_in_the_SDK_with_text_model_finish_reason_and_usage()
    {
        await using var host = await StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("usage-model").RespondsWith("Hello from Tracon", inputTokens: 11, outputTokens: 7))
            .AddAgent(Definition(Agent, "usage-model")));

        var chat = OpenAiSdkClients.Chat(host, Agent);

        ChatCompletion completion = await chat.CompleteChatAsync([new UserChatMessage("hi")], cancellationToken: TestContext.Current.CancellationToken);

        completion.Content.Count.ShouldBe(1);
        completion.Content[0].Text.ShouldBe("Hello from Tracon");
        completion.Role.ShouldBe(ChatMessageRole.Assistant);
        completion.FinishReason.ShouldBe(ChatFinishReason.Stop);
        completion.Model.ShouldBe(Agent, "the model field of the answer names the agent that ran");
        completion.Id.ShouldStartWith("chatcmpl-");
        completion.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UnixEpoch);
        completion.Usage.InputTokenCount.ShouldBe(11);
        completion.Usage.OutputTokenCount.ShouldBe(7);
        completion.Usage.TotalTokenCount.ShouldBe(18);
        completion.ToolCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_system_and_developer_message_and_a_prior_assistant_turn_are_accepted_from_the_SDK()
    {
        await using var host = await StartAsync(builder => builder.AddAgent(TestData.Definition()));

        var chat = OpenAiSdkClients.Chat(host, Agent);

        ChatCompletion completion = await chat.CompleteChatAsync(
            [
                new SystemChatMessage("be brief"),
                new DeveloperChatMessage("answer in one line"),
                new UserChatMessage("first"),
                new AssistantChatMessage("first answer"),
                new UserChatMessage("second"),
            ],
            cancellationToken: TestContext.Current.CancellationToken);

        completion.Content[0].Text.ShouldBe("Echo: second");
    }

    [Fact]
    public async Task A_server_side_tool_call_completes_inside_the_run_and_the_SDK_sees_only_the_final_text()
    {
        await using var host = await StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("tool-model")
                .CallsTool("lookup_order", new { orderId = "ORD-9" }, inputTokens: 5, outputTokens: 3)
                .EchoesLastToolResult("Result: ", inputTokens: 8, outputTokens: 4))
            .AddTool(
                (Func<string, string>)(orderId => $"{orderId} shipped"),
                name: "lookup_order",
                description: "Looks an order up.")
            .AddAgent(new AgentDefinition
            {
                Name = Agent,
                Instructions = "Look the order up.",
                Model = new ModelBinding { Provider = "tool-model", Model = "tool-1" },
                ToolNames = ["lookup_order"],
            }));

        var chat = OpenAiSdkClients.Chat(host, Agent);

        ChatCompletion completion = await chat.CompleteChatAsync([new UserChatMessage("where is ORD-9?")], cancellationToken: TestContext.Current.CancellationToken);

        completion.FinishReason.ShouldBe(ChatFinishReason.Stop, "the tool turn is not the answer; the SDK must not be told to run a tool");
        completion.ToolCalls.ShouldBeEmpty();
        completion.Content[0].Text.ShouldStartWith("Result: ", Case.Sensitive);
        completion.Content[0].Text.ShouldContain("ORD-9 shipped", Case.Sensitive);
        completion.Usage.InputTokenCount.ShouldBe(13, "both model calls of the run count");
        completion.Usage.OutputTokenCount.ShouldBe(7);
    }

    // --- Chat Completions, streamed ---

    [Fact]
    public async Task A_streamed_chat_completion_reads_in_the_SDK_as_one_role_chunk_text_chunks_and_one_finish()
    {
        await using var host = await StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("stream-model").RespondsWith("streamed answer", inputTokens: 6, outputTokens: 3))
            .AddAgent(Definition(Agent, "stream-model")));

        var chat = OpenAiSdkClients.Chat(host, Agent);
        var updates = new List<StreamingChatCompletionUpdate>();

        await foreach (var update in chat.CompleteChatStreamingAsync([new UserChatMessage("hi")], cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.ShouldNotBeEmpty();
        updates[0].Role.ShouldBe(ChatMessageRole.Assistant, "the first chunk carries the role");
        updates.Select(static u => u.CompletionId).Distinct(StringComparer.Ordinal).Count().ShouldBe(1, "every chunk belongs to one completion");
        updates.All(static u => string.Equals(u.Model, Agent, StringComparison.Ordinal)).ShouldBeTrue();

        string.Concat(updates.SelectMany(static u => u.ContentUpdate).Select(static part => part.Text)).ShouldBe("streamed answer");

        var finishes = updates.Where(static u => u.FinishReason is not null).ToList();
        finishes.ShouldHaveSingleItem().FinishReason.ShouldBe(ChatFinishReason.Stop);

        // The SDK asks for usage on every stream (stream_options.include_usage), and the answer ends with one more
        // chunk after the finish chunk: no content, the usage of the whole run.
        var last = updates[^1];
        last.FinishReason.ShouldBeNull();
        last.ContentUpdate.ShouldBeEmpty();
        last.Usage.ShouldNotBeNull().InputTokenCount.ShouldBe(6);
        last.Usage.OutputTokenCount.ShouldBe(3);
        last.Usage.TotalTokenCount.ShouldBe(9);
        updates[^2].FinishReason.ShouldBe(ChatFinishReason.Stop, "the finish chunk comes right before the usage chunk");
    }

    [Fact]
    public async Task A_streamed_chat_completion_ends_with_the_DONE_marker_and_declares_an_event_stream()
    {
        await using var host = await StartAsync(builder => builder.AddAgent(TestData.Definition()));

        using var request = RawChatRequest("""{"model":"kod-agent","stream":true,"messages":[{"role":"user","content":"hi"}]}""");

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.TrimEnd().ShouldEndWith("data: [DONE]", Case.Sensitive);
        body.Split("data: [DONE]").Length.ShouldBe(2, "the marker appears exactly once");
    }

    [Fact]
    public async Task A_streamed_chat_completion_that_asks_for_usage_gets_a_usage_chunk_the_SDK_can_read()
    {
        await using var host = await StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("usage-stream-model").RespondsWith("counted", inputTokens: 21, outputTokens: 5))
            .AddAgent(Definition(Agent, "usage-stream-model")));

        // The SDK does not expose stream_options, so the request is written by hand; every OpenAI client that wants
        // usage on a stream sends exactly this body, and then reads a last chunk with an empty choices list and usage.
        using var request = RawChatRequest(
            """{"model":"kod-agent","stream":true,"stream_options":{"include_usage":true},"messages":[{"role":"user","content":"hi"}]}""");

        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));

        var usageChunks = frames
            .Where(static frame => !string.Equals(frame.Data, "[DONE]", StringComparison.Ordinal))
            .Select(static frame => JsonDocument.Parse(frame.Data).RootElement)
            .Where(static chunk => chunk.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            .ToList();

        usageChunks.ShouldHaveSingleItem("include_usage asks for one final chunk that carries the usage");
        usageChunks[0].GetProperty("choices").GetArrayLength().ShouldBe(0);
        usageChunks[0].GetProperty("usage").GetProperty("prompt_tokens").GetInt32().ShouldBe(21);
        usageChunks[0].GetProperty("usage").GetProperty("completion_tokens").GetInt32().ShouldBe(5);
    }

    // --- Chat Completions, errors ---

    [Fact]
    public async Task An_unknown_agent_is_a_404_the_SDK_raises_with_the_server_message()
    {
        await using var host = await StartAsync();

        var chat = OpenAiSdkClients.Chat(host, "no-such-agent");

        var failure = await Should.ThrowAsync<ClientResultException>(
            async () => await chat.CompleteChatAsync([new UserChatMessage("hi")], cancellationToken: TestContext.Current.CancellationToken));

        failure.Status.ShouldBe((int)HttpStatusCode.NotFound);
        failure.Message.ShouldContain("no-such-agent", Case.Sensitive);
    }

    [Fact]
    public async Task A_sampling_setting_the_agent_owns_is_a_400_the_SDK_raises_and_names_the_field()
    {
        await using var host = await StartAsync(builder => builder.AddAgent(TestData.Definition()));

        var chat = OpenAiSdkClients.Chat(host, Agent);

        var failure = await Should.ThrowAsync<ClientResultException>(async () => await chat.CompleteChatAsync(
            [new UserChatMessage("hi")],
            new ChatCompletionOptions { Temperature = 0.2f, MaxOutputTokenCount = 50 },
            TestContext.Current.CancellationToken));

        failure.Status.ShouldBe((int)HttpStatusCode.BadRequest);
        failure.Message.ShouldContain("temperature", Case.Sensitive);
    }

    [Fact]
    public async Task Tool_definitions_and_tool_history_from_the_SDK_are_refused_with_a_400()
    {
        await using var host = await StartAsync(builder => builder.AddAgent(TestData.Definition()));

        var chat = OpenAiSdkClients.Chat(host, Agent);

        var withTools = new ChatCompletionOptions();
        withTools.Tools.Add(ChatTool.CreateFunctionTool("get_weather", "Gets the weather.", BinaryData.FromString("""{"type":"object","properties":{}}""")));

        var definitions = await Should.ThrowAsync<ClientResultException>(async () => await chat.CompleteChatAsync(
            [new UserChatMessage("hi")],
            withTools,
            TestContext.Current.CancellationToken));

        definitions.Status.ShouldBe((int)HttpStatusCode.BadRequest);
        definitions.Message.ShouldContain("tools", Case.Sensitive);

        var history = await Should.ThrowAsync<ClientResultException>(async () => await chat.CompleteChatAsync(
            [
                new UserChatMessage("weather?"),
                new AssistantChatMessage([ChatToolCall.CreateFunctionToolCall("call_1", "get_weather", BinaryData.FromString("{}"))]),
                new ToolChatMessage("call_1", "sunny"),
            ],
            cancellationToken: TestContext.Current.CancellationToken));

        history.Status.ShouldBe((int)HttpStatusCode.BadRequest);
        history.Message.ShouldContain("tool", Case.Sensitive);
    }

    [Fact]
    public async Task A_failing_provider_is_an_error_the_SDK_raises_not_a_hung_or_empty_success()
    {
        await using var host = await StartAsync(builder => builder
            .AddModelProvider(new ThrowingModelProvider("throwing-model"))
            .AddAgent(Definition(Agent, "throwing-model")));

        var chat = OpenAiSdkClients.Chat(host, Agent);

        var buffered = await Should.ThrowAsync<ClientResultException>(
            async () => await chat.CompleteChatAsync([new UserChatMessage("hi")], cancellationToken: TestContext.Current.CancellationToken));

        buffered.Status.ShouldBe((int)HttpStatusCode.BadGateway);
        buffered.Message.ShouldNotBeNullOrWhiteSpace();

        // On a stream the 200 is already sent, so the failure is an error frame. The SDK reads a frame it does not
        // know as an empty update and raises nothing; what a caller can rely on is that the stream never claims a
        // finish reason, which is how a truncated answer is told from a finished one.
        var updates = new List<StreamingChatCompletionUpdate>();

        await foreach (var update in chat.CompleteChatStreamingAsync([new UserChatMessage("hi")], cancellationToken: TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.ShouldNotContain(static update => update.FinishReason != null);
        string.Concat(updates.SelectMany(static update => update.ContentUpdate).Select(static part => part.Text)).ShouldBeEmpty();
    }

    // --- Responses ---

    [Fact]
    public async Task A_response_deserializes_in_the_SDK_and_chains_through_previous_response_id()
    {
        await using var host = await StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("responses-model").RespondsWith("first answer", inputTokens: 9, outputTokens: 4).EchoesUserMessage())
            .AddAgent(Definition(Agent, "responses-model")));

        var responses = OpenAiSdkClients.Responses(host);

        ResponseResult first = (await responses.CreateResponseAsync(Agent, "first turn", previousResponseId: null, TestContext.Current.CancellationToken)).Value;

        first.Id.ShouldStartWith("resp_");
        first.Status.ShouldBe(ResponseStatus.Completed);
        first.Model.ShouldBe(Agent);
        first.GetOutputText().ShouldBe("first answer");
        first.Usage.InputTokenCount.ShouldBe(9);
        first.Usage.OutputTokenCount.ShouldBe(4);
        first.Usage.TotalTokenCount.ShouldBe(13);
        first.OutputItems.OfType<MessageResponseItem>().ShouldHaveSingleItem().Role.ShouldBe(MessageRole.Assistant);

        ResponseResult second = (await responses.CreateResponseAsync(Agent, "second turn", first.Id, TestContext.Current.CancellationToken)).Value;

        second.GetOutputText().ShouldBe("Echo: second turn");
        second.Id.ShouldNotBe(first.Id, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_streamed_response_arrives_in_the_SDK_as_created_then_text_deltas_then_completed()
    {
        await using var host = await StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("responses-stream-model").RespondsWith("streamed response"))
            .AddAgent(Definition(Agent, "responses-stream-model")));

        var responses = OpenAiSdkClients.Responses(host);
        var updates = new List<StreamingResponseUpdate>();

        await foreach (var update in responses.CreateResponseStreamingAsync(Agent, "hi", previousResponseId: null, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.ShouldNotBeEmpty();
        updates[0].ShouldBeOfType<StreamingResponseCreatedUpdate>().Response.Model.ShouldBe(Agent, "the created event names the model too");
        updates[^1].ShouldBeOfType<StreamingResponseCompletedUpdate>();

        string.Concat(updates.OfType<StreamingResponseOutputTextDeltaUpdate>().Select(static u => u.Delta)).ShouldBe("streamed response");

        // The sequence numbers are the SDK's ordering guarantee; they never go backwards.
        updates.Select(static u => u.SequenceNumber).ToList().ShouldBe([.. updates.Select(static u => u.SequenceNumber).Order()]);

        var completed = (StreamingResponseCompletedUpdate)updates[^1];
        completed.Response.Status.ShouldBe(ResponseStatus.Completed);
        completed.Response.Model.ShouldBe(Agent);
        completed.Response.GetOutputText().ShouldBe("streamed response");
    }

    [Fact]
    public async Task A_response_for_an_unknown_agent_is_a_404_the_SDK_raises()
    {
        await using var host = await StartAsync();

        var responses = OpenAiSdkClients.Responses(host);

        var failure = await Should.ThrowAsync<ClientResultException>(
            async () => await responses.CreateResponseAsync("no-such-agent", "hi", previousResponseId: null, TestContext.Current.CancellationToken));

        failure.Status.ShouldBe((int)HttpStatusCode.NotFound);
        failure.Message.ShouldContain("no-such-agent", Case.Sensitive);
    }

    [Fact]
    public async Task A_response_that_runs_a_server_side_tool_returns_the_final_text_and_the_tool_item_in_the_SDK_shape()
    {
        await using var host = await StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("responses-tool-model")
                .CallsTool("lookup_order", new { orderId = "ORD-3" })
                .EchoesLastToolResult("Result: "))
            .AddTool(
                (Func<string, string>)(orderId => $"{orderId} packed"),
                name: "lookup_order",
                description: "Looks an order up.")
            .AddAgent(new AgentDefinition
            {
                Name = Agent,
                Instructions = "Look the order up.",
                Model = new ModelBinding { Provider = "responses-tool-model", Model = "tool-1" },
                ToolNames = ["lookup_order"],
            }));

        var responses = OpenAiSdkClients.Responses(host);

        ResponseResult result = (await responses.CreateResponseAsync(Agent, "where is ORD-3?", previousResponseId: null, TestContext.Current.CancellationToken)).Value;

        result.Status.ShouldBe(ResponseStatus.Completed);
        result.GetOutputText().ShouldContain("ORD-3 packed", Case.Sensitive);
    }

    // --- Conversations ---

    [Fact]
    public async Task A_conversation_round_trips_through_the_SDK_create_get_items_delete()
    {
        await using var host = await StartAsync(builder => builder.AddAgent(TestData.Definition()));

        var conversations = OpenAiSdkClients.Conversations(host);
        var responses = OpenAiSdkClients.Responses(host);

        ConversationResource created = (await conversations.CreateConversationAsync(
            new ConversationCreationOptions { Metadata = { ["topic"] = "sdk-differential" } },
            TestContext.Current.CancellationToken)).Value;

        created.Id.ShouldStartWith("conv_");
        created.Object.ShouldBe("conversation");
        created.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UnixEpoch);

        var options = new CreateResponseOptions { Model = Agent, ConversationOptions = new ResponseConversationOptions(created.Id) };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem("hello conversation"));

        ResponseResult turn = (await responses.CreateResponseAsync(options, TestContext.Current.CancellationToken)).Value;
        turn.GetOutputText().ShouldBe("Echo: hello conversation");

        ConversationResource fetched = (await conversations.GetConversationAsync(created.Id, TestContext.Current.CancellationToken)).Value;
        fetched.Id.ShouldBe(created.Id);

        var items = new List<string>();

        await foreach (var page in conversations
            .GetConversationItemsAsync(created.Id, limit: null, order: null, after: null, include: null, options: null)
            .GetRawPagesAsync())
        {
            using var document = JsonDocument.Parse(page.GetRawResponse().Content.ToString());
            foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
            {
                items.Add(item.GetProperty("type").GetString()!);
            }
        }

        items.ShouldNotBeEmpty("the turn left items behind");

        var deleted = await conversations.DeleteConversationAsync(created.Id, TestContext.Current.CancellationToken);
        deleted.GetRawResponse().Status.ShouldBe((int)HttpStatusCode.OK);
    }

    // The SDK cannot send a request without an API key and always sends it as a bearer token, so the host is
    // configured the way the guide tells a consumer to configure it: the key is the endpoint's bearer token.
    private static Task<TraconTestHost> StartAsync(Action<ITraconBuilder>? configureTracon = null)
        => TraconTestHost.StartAsync(
            configureTracon,
            configureEndpoints: static options => options.AuthToken = OpenAiSdkClients.ApiKey);

    private static HttpRequestMessage RawChatRequest(string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", OpenAiSdkClients.ApiKey);

        return request;
    }

    private static AgentDefinition Definition(string name, string provider)
        => new()
        {
            Name = name,
            Instructions = "Reply briefly.",
            Model = new ModelBinding { Provider = provider, Model = provider + "-1" },
        };
}

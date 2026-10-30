using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon.OpenAIShared.Tests;

/// <summary>
/// What the strict decorator hands to the client below it.
/// </summary>
/// <remarks>
/// Linked into <c>Tracon.OpenAI.UnitTests</c> and <c>Tracon.Azure.UnitTests</c>. These tests stop at the
/// options the inner client receives; the bytes on the wire are <see cref="StrictResponseSchemaWireTests"/>.
/// </remarks>
public sealed class OpenAIStrictResponseSchemaChatClientTests
{
    private static readonly ChatResponseFormat Schema = ChatResponseFormat.ForJsonSchema(
        JsonDocument.Parse("""{"type":"object","properties":{"answer":{"type":"string"}}}""").RootElement.Clone(),
        "answer");

    [Fact]
    public async Task A_schema_request_gets_the_strict_flag_on_a_copy_of_the_options()
    {
        var inner = new CapturingChatClient();
        using var client = new OpenAIStrictResponseSchemaChatClient(inner);
        var options = new ChatOptions { ResponseFormat = Schema, Temperature = 0.3f };

        await client.GetResponseAsync("hi", options, TestContext.Current.CancellationToken);

        var sent = inner.Options.ShouldHaveSingleItem()!;
        sent.AdditionalProperties.ShouldNotBeNull()["strict"].ShouldBe(true);
        sent.Temperature.ShouldBe(0.3f);
        sent.ShouldNotBeSameAs(options);
        options.AdditionalProperties.ShouldBeNull("the caller's options are shared by every run of a compiled agent");
    }

    [Fact]
    public async Task A_streamed_request_gets_the_same_treatment()
    {
        var inner = new CapturingChatClient();
        using var client = new OpenAIStrictResponseSchemaChatClient(inner);
        var options = new ChatOptions { ResponseFormat = Schema };

        await foreach (var _ in client.GetStreamingResponseAsync("hi", options, TestContext.Current.CancellationToken))
        {
        }

        inner.Options.ShouldHaveSingleItem()!.AdditionalProperties.ShouldNotBeNull()["strict"].ShouldBe(true);
        options.AdditionalProperties.ShouldBeNull();
    }

    [Fact]
    public async Task A_function_without_its_own_choice_is_declared_not_strict()
    {
        var inner = new CapturingChatClient();
        using var client = new OpenAIStrictResponseSchemaChatClient(inner);
        var function = AIFunctionFactory.Create(static (string city) => city, "lookup", "Looks a city up.");
        var options = new ChatOptions { ResponseFormat = Schema, Tools = [function] };

        await client.GetResponseAsync("hi", options, TestContext.Current.CancellationToken);

        var declared = inner.Options.ShouldHaveSingleItem()!.Tools.ShouldNotBeNull().ShouldHaveSingleItem();
        var declaration = declared.ShouldBeAssignableTo<AIFunctionDeclaration>()!;

        declaration.Name.ShouldBe("lookup");
        declaration.Description.ShouldBe("Looks a city up.");
        declaration.JsonSchema.GetRawText().ShouldBe(function.JsonSchema.GetRawText());
        declaration.AdditionalProperties["strict"].ShouldBe(false);
        declaration.GetService<AIFunction>().ShouldBeSameAs(function);

        options.Tools.ShouldHaveSingleItem().ShouldBeSameAs(function);
        function.AdditionalProperties.ContainsKey("strict").ShouldBeFalse();
    }

    [Fact]
    public async Task A_function_that_chose_for_itself_keeps_its_choice()
    {
        var inner = new CapturingChatClient();
        using var client = new OpenAIStrictResponseSchemaChatClient(inner);
        var chosen = new ChosenFunction();
        var options = new ChatOptions { ResponseFormat = Schema, Tools = [chosen] };

        await client.GetResponseAsync("hi", options, TestContext.Current.CancellationToken);

        inner.Options.ShouldHaveSingleItem()!.Tools.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBeSameAs(chosen);
    }

    [Fact]
    public async Task A_tool_that_is_not_a_function_passes_through()
    {
        var inner = new CapturingChatClient();
        using var client = new OpenAIStrictResponseSchemaChatClient(inner);
        var search = new HostedWebSearchTool();
        var options = new ChatOptions { ResponseFormat = Schema, Tools = [search] };

        await client.GetResponseAsync("hi", options, TestContext.Current.CancellationToken);

        inner.Options.ShouldHaveSingleItem()!.Tools.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBeSameAs(search);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("text")]
    [InlineData("json")]
    public async Task A_request_without_a_response_schema_is_passed_on_untouched(string kind)
    {
        var inner = new CapturingChatClient();
        using var client = new OpenAIStrictResponseSchemaChatClient(inner);
        var options = new ChatOptions
        {
            ResponseFormat = kind switch
            {
                "text" => ChatResponseFormat.Text,
                "json" => ChatResponseFormat.Json,
                _ => null,
            },
            Tools = [AIFunctionFactory.Create(static () => 1, "one")],
        };

        await client.GetResponseAsync("hi", options, TestContext.Current.CancellationToken);

        inner.Options.ShouldHaveSingleItem().ShouldBeSameAs(options);
    }

    [Fact]
    public async Task A_request_without_options_is_passed_on_without_options()
    {
        var inner = new CapturingChatClient();
        using var client = new OpenAIStrictResponseSchemaChatClient(inner);

        await client.GetResponseAsync("hi", options: null, TestContext.Current.CancellationToken);

        inner.Options.ShouldHaveSingleItem().ShouldBeNull();
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_inner_client()
    {
        var inner = new CapturingChatClient();
        using var client = new OpenAIStrictResponseSchemaChatClient(inner);
        using var source = new CancellationTokenSource();

        await client.GetResponseAsync("hi", new ChatOptions { ResponseFormat = Schema }, source.Token);

        inner.Tokens.ShouldHaveSingleItem().ShouldBe(source.Token);
    }

    [Fact]
    public async Task Two_concurrent_requests_on_shared_options_each_get_their_own_copy()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new CapturingChatClient(gate.Task);
        using var client = new OpenAIStrictResponseSchemaChatClient(inner);
        var shared = new ChatOptions
        {
            ResponseFormat = Schema,
            Tools = [AIFunctionFactory.Create(static () => 1, "one")],
        };

        var first = client.GetResponseAsync("a", shared, TestContext.Current.CancellationToken);
        var second = client.GetResponseAsync("b", shared, TestContext.Current.CancellationToken);

        // Both calls are inside the inner client at once, so a decorator that wrote the shared instance
        // would have written it twice by now.
        inner.Options.Count.ShouldBe(2);
        inner.Options[0].ShouldNotBeSameAs(inner.Options[1]);
        inner.Options.ShouldAllBe(static sent => sent!.AdditionalProperties != null && (bool)sent.AdditionalProperties["strict"]!);
        shared.AdditionalProperties.ShouldBeNull();
        shared.Tools.ShouldHaveSingleItem().ShouldBeAssignableTo<AIFunction>();

        gate.SetResult();
        await Task.WhenAll(first, second);
    }

    [Fact]
    public async Task Under_the_function_loop_the_tool_runs_and_both_turns_carry_the_strict_response_format_and_the_non_strict_tool()
    {
        var calls = 0;
        var function = AIFunctionFactory.Create(
            (string city) =>
            {
                calls++;
                return $"sunny in {city}";
            },
            "lookup",
            "Looks a city up.");

        // The decorator sits BELOW the function loop, as the registry builds it: the loop holds the invocable
        // function and resolves the call, the decorator only changes what the provider is shown.
        var inner = new ScriptedToolChatClient();
        using var client = new ChatClientBuilder(new OpenAIStrictResponseSchemaChatClient(inner))
            .UseFunctionInvocation()
            .Build();
        var options = new ChatOptions { ResponseFormat = Schema, Tools = [function] };

        var response = await client.GetResponseAsync("weather?", options, TestContext.Current.CancellationToken);

        calls.ShouldBe(1, "the function behind the wrapper is the one that ran");
        response.Text.ShouldBe("{}");
        inner.Options.Count.ShouldBe(2, "one turn for the tool call, one for the answer");

        foreach (var sent in inner.Options)
        {
            sent!.AdditionalProperties.ShouldNotBeNull()["strict"].ShouldBe(true);
            sent.Tools.ShouldNotBeNull().ShouldHaveSingleItem().ShouldBeAssignableTo<AIFunctionDeclaration>()!
                .AdditionalProperties["strict"].ShouldBe(false);
        }

        options.AdditionalProperties.ShouldBeNull();
        options.Tools.ShouldHaveSingleItem().ShouldBeSameAs(function);
    }

    [Fact]
    public void The_decorator_answers_service_lookups_from_the_client_below_it()
    {
        var metadata = new ChatClientMetadata("inner");
        var inner = new CapturingChatClient { Metadata = metadata };
        using var client = new OpenAIStrictResponseSchemaChatClient(inner);

        client.GetService<ChatClientMetadata>().ShouldBeSameAs(metadata);
    }

    private sealed class CapturingChatClient(Task? gate = null) : IChatClient
    {
        public List<ChatOptions?> Options { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public ChatClientMetadata? Metadata { get; init; }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            Tokens.Add(cancellationToken);

            if (gate is not null)
            {
                await gate;
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            Tokens.Add(cancellationToken);
            await Task.CompletedTask;

            yield return new ChatResponseUpdate(ChatRole.Assistant, "{}");
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(ChatClientMetadata) ? Metadata : null;

        public void Dispose()
        {
        }
    }

    /// <summary>Answers the first turn with a call of <c>lookup</c> and the second with a JSON text.</summary>
    private sealed class ScriptedToolChatClient : IChatClient
    {
        public List<ChatOptions?> Options { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);

            var reply = Options.Count == 1
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "lookup", new Dictionary<string, object?>(StringComparer.Ordinal) { ["city"] = "Oslo" })])
                : new ChatMessage(ChatRole.Assistant, "{}");

            return Task.FromResult(new ChatResponse(reply));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>A function declaration that already says whether it is strict.</summary>
    private sealed class ChosenFunction : AIFunctionDeclaration
    {
        public override string Name => "chosen";

        public override IReadOnlyDictionary<string, object?> AdditionalProperties { get; }
            = new Dictionary<string, object?>(StringComparer.Ordinal) { ["strict"] = true };
    }
}

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// <see cref="RunRecordingAgent"/> hands the inner agent a plain
/// <see cref="AgentRunOptions"/>, never <see cref="TraconRunOptions"/>.
/// The end-to-end proof is
/// <c>MafFunctionMiddlewareTests</c>; these tests pin the copy itself.
/// </summary>
public sealed class RunOptionsHandoffTests
{
    [Fact]
    public void Tracon_options_become_plain_options_with_every_base_property()
    {
        var format = ChatResponseFormat.Json;
        var options = new TraconRunOptions
        {
            RunId = Guid.NewGuid(),
            AllowBackgroundResponses = true,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["key"] = "value" },
            ResponseFormat = format,
        };

        var inner = RunRecordingAgent.ForInnerAgent(options);

        inner.ShouldNotBeNull();
        inner.GetType().ShouldBe(typeof(AgentRunOptions));
        inner.AllowBackgroundResponses.ShouldBe(true);
        inner.ResponseFormat.ShouldBeSameAs(format);
        inner.AdditionalProperties.ShouldNotBeNull();
        inner.AdditionalProperties["key"].ShouldBe("value");

        // A copy, so an inner layer that writes to it cannot change the caller's options.
        inner.AdditionalProperties.ShouldNotBeSameAs(options.AdditionalProperties);
    }

    /// <summary>
    /// A workflow step's model options for the run (a handoff step's tools and instructions) go
    /// inward on the one type the agent reads them from, with the base properties beside them.
    /// </summary>
    [Fact]
    public void A_steps_model_options_go_inward_as_chat_client_run_options()
    {
        var chatOptions = new ChatOptions { Instructions = "hand off when needed" };
        var options = new TraconRunOptions
        {
            RunId = Guid.NewGuid(),
            AllowBackgroundResponses = true,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["key"] = "value" },
            ChatOptions = chatOptions,
        };

        var inner = RunRecordingAgent.ForInnerAgent(options).ShouldBeOfType<ChatClientAgentRunOptions>();

        inner.ChatOptions.ShouldNotBeNull().Instructions.ShouldBe("hand off when needed");
        inner.ChatOptions.ShouldNotBeSameAs(chatOptions);
        inner.ChatClientFactory.ShouldBeNull();
        inner.AllowBackgroundResponses.ShouldBe(true);
        inner.AdditionalProperties.ShouldNotBeNull()["key"].ShouldBe("value");
    }

    [Fact]
    public void Other_options_pass_through_unchanged()
    {
        var plain = new AgentRunOptions();
        var chatClient = new ChatClientAgentRunOptions();

        RunRecordingAgent.ForInnerAgent(null).ShouldBeNull();
        RunRecordingAgent.ForInnerAgent(plain).ShouldBeSameAs(plain);
        RunRecordingAgent.ForInnerAgent(chatClient).ShouldBeSameAs(chatClient);
    }

    /// <summary>
    /// 🚨 The copy names each base property by hand. When Microsoft Agent
    /// Framework adds a property to <see cref="AgentRunOptions"/>, this test
    /// fails: add the property to <c>RunRecordingAgent.ForInnerAgent</c> and
    /// to the list below.
    /// </summary>
    [Fact]
    public void The_copy_knows_every_settable_base_property()
    {
        var settable = typeof(AgentRunOptions)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(static property => property.SetMethod is { IsPublic: true })
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        settable.ShouldBe(
        [
            nameof(AgentRunOptions.AdditionalProperties),
            nameof(AgentRunOptions.AllowBackgroundResponses),
            // A literal, not nameof: the property is MEAI001.
            "ContinuationToken",
            nameof(AgentRunOptions.ResponseFormat),
        ]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Inner_agent_never_sees_Tracon_options(bool recording, bool streaming)
    {
        var inner = new OptionsCapturingAgent();
        var tenant = new FixedTenantContext();
        var agent = new RunRecordingAgent(
            inner,
            new InMemoryRunStore(tenantContext: tenant),
            tenant,
            new TraconRunRecordingOptions { Enabled = recording },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RunRecordingAgent>.Instance);

        var options = new TraconRunOptions { RunId = Guid.NewGuid() };

        if (streaming)
        {
            await foreach (var _ in agent.RunStreamingAsync("hello", options: options))
            {
            }
        }
        else
        {
            await agent.RunAsync("hello", options: options);
        }

        inner.SeenOptionTypes.ShouldBe([typeof(AgentRunOptions)]);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "default";
    }

    private sealed class OptionsCapturingAgent : AIAgent
    {
        public List<Type?> SeenOptionTypes { get; } = [];

        protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
            System.Text.Json.JsonElement serializedState,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override ValueTask<System.Text.Json.JsonElement> SerializeSessionCoreAsync(
            AgentSession session,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        protected override Task<AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            SeenOptionTypes.Add(options?.GetType());
            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            SeenOptionTypes.Add(options?.GetType());
            await Task.CompletedTask;
            yield return new AgentResponseUpdate(ChatRole.Assistant, "done");
        }
    }
}

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

    /// <summary>
    /// A caller's added instructions go inward on the one type the agent reads them from, and the options it passed are
    /// left as they were.
    /// </summary>
    [Fact]
    public void Added_instructions_go_inward_as_chat_client_run_options()
    {
        var options = new TraconRunOptions
        {
            RunId = Guid.NewGuid(),
            AllowBackgroundResponses = true,
            AdditionalProperties = new AdditionalPropertiesDictionary { ["key"] = "value" },
            AdditionalInstructions = "Never mention the price.",
        };

        var inner = RunRecordingAgent.ForInnerAgent(options).ShouldBeOfType<ChatClientAgentRunOptions>();

        inner.ChatOptions.ShouldNotBeNull().Instructions.ShouldBe("Never mention the price.");
        inner.AllowBackgroundResponses.ShouldBe(true);
        inner.AdditionalProperties.ShouldNotBeNull()["key"].ShouldBe("value");
        options.AdditionalInstructions.ShouldBe("Never mention the price.");
    }

    [Fact]
    public void Added_instructions_follow_the_instructions_of_a_workflow_step_and_the_callers_options_are_not_changed()
    {
        var chatOptions = new ChatOptions { Instructions = "hand off when needed" };
        var options = new TraconRunOptions { ChatOptions = chatOptions, AdditionalInstructions = "Answer in French." };

        var inner = RunRecordingAgent.ForInnerAgent(options).ShouldBeOfType<ChatClientAgentRunOptions>();

        inner.ChatOptions.ShouldNotBeNull().Instructions.ShouldBe("hand off when needed\nAnswer in French.");
        chatOptions.Instructions.ShouldBe("hand off when needed", "the options the workflow step built are not edited");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_added_instructions_add_nothing(string? text)
    {
        var inner = RunRecordingAgent.ForInnerAgent(new TraconRunOptions { AdditionalInstructions = text });

        inner.ShouldNotBeNull();
        inner.GetType().ShouldBe(typeof(AgentRunOptions));
    }

    [Fact]
    public void Blank_added_instructions_leave_a_workflow_steps_options_as_they_are()
    {
        var options = new TraconRunOptions
        {
            ChatOptions = new ChatOptions { Instructions = "hand off when needed" },
            AdditionalInstructions = " ",
        };

        RunRecordingAgent.ForInnerAgent(options).ShouldBeOfType<ChatClientAgentRunOptions>()
            .ChatOptions.ShouldNotBeNull().Instructions.ShouldBe("hand off when needed");
    }

    [Fact]
    public void A_copy_of_the_options_keeps_the_added_instructions()
    {
        var options = new TraconRunOptions { RunId = Guid.NewGuid(), AdditionalInstructions = "Be brief." };

        ((TraconRunOptions)options.Clone()).AdditionalInstructions.ShouldBe("Be brief.");
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
        var agent = TestRunRecording.Agent(
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

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Added_instructions_reach_the_inner_agent_on_every_path(bool recording, bool streaming)
    {
        var inner = new OptionsCapturingAgent();
        var tenant = new FixedTenantContext();
        var agent = TestRunRecording.Agent(
            inner,
            new InMemoryRunStore(tenantContext: tenant),
            tenant,
            new TraconRunRecordingOptions { Enabled = recording },
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RunRecordingAgent>.Instance);

        var options = new TraconRunOptions { RunId = Guid.NewGuid(), AdditionalInstructions = "Keep it short." };

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

        inner.SeenOptionTypes.ShouldBe([typeof(ChatClientAgentRunOptions)]);
        inner.SeenInstructions.ShouldBe(["Keep it short."]);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "default";
    }

    private sealed class OptionsCapturingAgent : AIAgent
    {
        public List<Type?> SeenOptionTypes { get; } = [];

        public List<string?> SeenInstructions { get; } = [];

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
            SeenInstructions.Add((options as ChatClientAgentRunOptions)?.ChatOptions?.Instructions);
            return Task.FromResult(new AgentResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session = null,
            AgentRunOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            SeenOptionTypes.Add(options?.GetType());
            SeenInstructions.Add((options as ChatClientAgentRunOptions)?.ChatOptions?.Instructions);
            await Task.CompletedTask;
            yield return new AgentResponseUpdate(ChatRole.Assistant, "done");
        }
    }
}

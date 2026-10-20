using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Extensions;

/// <summary>
/// Model options that come with one run (<see cref="ChatClientAgentRunOptions"/>) follow the
/// extension rule: what the definition owns is refused, an added instruction and a tool
/// declaration pass.
/// </summary>
public sealed class RunChatOptionsRuleTests
{
    /// <summary>A handoff step's options pass: its instructions, its declarations, one call at a time.</summary>
    [Fact]
    public void A_handoff_steps_options_pass()
    {
        var options = new ChatClientAgentRunOptions(new ChatOptions
        {
            Instructions = "Hand off technical questions.",
            Tools = [AIFunctionFactory.CreateDeclaration("handoff_to_1", "Hands off.", AIJsonUtilities.CreateJsonSchema(typeof(string)))],
            AllowMultipleToolCalls = false,
            Seed = 7,
        });

        Should.NotThrow(() => RunChatOptionsRule.Check(options));
    }

    [Fact]
    public void Options_that_are_not_chat_client_run_options_pass()
    {
        Should.NotThrow(() => RunChatOptionsRule.Check(null));
        Should.NotThrow(() => RunChatOptionsRule.Check(new AgentRunOptions()));
        Should.NotThrow(() => RunChatOptionsRule.Check(new TraconRunOptions()));
        Should.NotThrow(() => RunChatOptionsRule.Check(new ChatClientAgentRunOptions(new ChatOptions { Tools = [] })));
    }

    /// <param name="member">The protected member the run sets.</param>
    [Theory]
    [InlineData(nameof(ChatOptions.ModelId))]
    [InlineData(nameof(ChatOptions.Temperature))]
    [InlineData(nameof(ChatOptions.TopP))]
    [InlineData(nameof(ChatOptions.MaxOutputTokens))]
    [InlineData(nameof(ChatOptions.ResponseFormat))]
    [InlineData(nameof(ChatOptions.Reasoning))]
    [InlineData(nameof(ChatOptions.ConversationId))]
    [InlineData(nameof(ChatOptions.RawRepresentationFactory))]
    public void A_member_the_definition_owns_is_refused(string member)
    {
        var chat = member switch
        {
            nameof(ChatOptions.ModelId) => new ChatOptions { ModelId = "other-model" },
            nameof(ChatOptions.Temperature) => new ChatOptions { Temperature = 1.5f },
            nameof(ChatOptions.TopP) => new ChatOptions { TopP = 0.5f },
            nameof(ChatOptions.MaxOutputTokens) => new ChatOptions { MaxOutputTokens = 10 },
            nameof(ChatOptions.ResponseFormat) => new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            nameof(ChatOptions.Reasoning) => new ChatOptions { Reasoning = new ReasoningOptions { Output = ReasoningOutput.Summary } },
            nameof(ChatOptions.ConversationId) => new ChatOptions { ConversationId = "conv-1" },
            _ => new ChatOptions { RawRepresentationFactory = static _ => null },
        };

        var exception = Should.Throw<ArgumentException>(() => RunChatOptionsRule.Check(new ChatClientAgentRunOptions(chat)));

        exception.Message.ShouldContain($"ChatOptions.{member}", Case.Sensitive);
    }

    /// <summary>A function runs in the loop and a hosted tool at the provider; both would skip the tool chain.</summary>
    [Fact]
    public void A_tool_that_runs_is_refused()
    {
        var function = new ChatClientAgentRunOptions(new ChatOptions { Tools = [AIFunctionFactory.Create(static () => "x", "run_me")] });
        var hosted = new ChatClientAgentRunOptions(new ChatOptions { Tools = [new HostedWebSearchTool()] });

        Should.Throw<ArgumentException>(() => RunChatOptionsRule.Check(function)).Message.ShouldContain("a tool that runs");
        Should.Throw<ArgumentException>(() => RunChatOptionsRule.Check(hosted)).Message.ShouldContain("a tool that runs");
    }

    /// <summary>A factory can replace the model client and every ring on it.</summary>
    [Fact]
    public void A_chat_client_factory_is_refused()
    {
        var options = new ChatClientAgentRunOptions { ChatClientFactory = static client => client };

        Should.Throw<ArgumentException>(() => RunChatOptionsRule.Check(options))
            .Message.ShouldContain(nameof(ChatClientAgentRunOptions.ChatClientFactory), Case.Sensitive);
    }
}

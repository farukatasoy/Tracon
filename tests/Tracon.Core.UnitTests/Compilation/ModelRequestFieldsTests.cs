using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// F-598 — what the compiler makes of the request fields of <see cref="ModelBinding"/>: each reaches
/// <see cref="ChatOptions"/> and a value the provider would reject, or the run would ignore, stops the
/// compilation and names the field.
/// </summary>
/// <remarks>
/// The options are read from inside an agent extension, which runs once the options object is fully built
/// and before the agent is made from it, so the test sees exactly what the agent would send.
/// </remarks>
public sealed class ModelRequestFieldsTests
{
    [Fact]
    public void Every_field_the_definition_sets_reaches_the_chat_options()
    {
        var options = Compile(TestData.Binding() with
        {
            Seed = 42,
            TopK = 40,
            StopSequences = ["END", "STOP"],
            FrequencyPenalty = 0.3f,
            PresencePenalty = 0.4f,
            ToolMode = "required:lookup",
            AllowMultipleToolCalls = false,
            ReasoningEffort = "Low",
            ReasoningOutput = "Summary",
        });

        options.Seed.ShouldBe(42);
        options.TopK.ShouldBe(40);
        options.StopSequences.ShouldBe(["END", "STOP"]);
        options.FrequencyPenalty.ShouldBe(0.3f);
        options.PresencePenalty.ShouldBe(0.4f);
        options.ToolMode.ShouldBeOfType<RequiredChatToolMode>().RequiredFunctionName.ShouldBe("lookup");
        options.AllowMultipleToolCalls.ShouldBe(false);
        options.Reasoning.ShouldNotBeNull().Effort.ShouldBe(ReasoningEffort.Low);
        options.Reasoning.Output.ShouldBe(ReasoningOutput.Summary);
    }

    [Fact]
    public void A_definition_that_sets_none_sends_none_of_them()
    {
        var options = Compile(TestData.Binding());

        options.Seed.ShouldBeNull();
        options.TopK.ShouldBeNull();
        options.StopSequences.ShouldBeNull();
        options.FrequencyPenalty.ShouldBeNull();
        options.PresencePenalty.ShouldBeNull();
        options.ToolMode.ShouldBeNull();
        options.AllowMultipleToolCalls.ShouldBeNull();
        options.Reasoning.ShouldBeNull();
    }

    [Fact]
    public void The_reasoning_output_can_stand_without_an_effort_and_the_effort_without_an_output()
    {
        var output = Compile(TestData.Binding() with { ReasoningOutput = "full" }).Reasoning.ShouldNotBeNull();
        var effort = Compile(TestData.Binding() with { ReasoningEffort = "High" }).Reasoning.ShouldNotBeNull();

        output.Effort.ShouldBeNull();
        output.Output.ShouldBe(ReasoningOutput.Full);
        effort.Effort.ShouldBe(ReasoningEffort.High);
        effort.Output.ShouldBeNull();
    }

    /// <param name="text">The tool mode as the definition writes it.</param>
    /// <param name="expected">The mode it means.</param>
    [Theory]
    [InlineData("auto", "auto")]
    [InlineData("AUTO", "auto")]
    [InlineData("none", "none")]
    [InlineData("None", "none")]
    [InlineData("required", "required")]
    [InlineData("Required", "required")]
    [InlineData("required:lookup", "required:lookup")]
    [InlineData(" REQUIRED:lookup ", "required:lookup")]
    public void A_tool_mode_is_read_without_regard_to_case(string text, string expected)
    {
        var mode = Compile(TestData.Binding() with { ToolMode = text }).ToolMode;

        (mode switch
        {
            AutoChatToolMode => "auto",
            NoneChatToolMode => "none",
            RequiredChatToolMode { RequiredFunctionName: null } => "required",
            RequiredChatToolMode required => $"required:{required.RequiredFunctionName}",
            _ => "unknown",
        }).ShouldBe(expected);
    }

    /// <param name="text">The value the definition writes.</param>
    /// <param name="reason">What the error says.</param>
    [Theory]
    [InlineData("sometimes", "the valid values are")]
    [InlineData("required:", "is empty")]
    [InlineData("required:other_tool", "is not one of the definition's ToolNames")]
    [InlineData("requires:lookup", "the valid values are")]
    public void An_invalid_tool_mode_does_not_compile_and_the_error_names_the_value(string text, string reason)
    {
        var exception = Should.Throw<TraconCompilationException>(() => Compile(TestData.Binding() with { ToolMode = text }));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain(nameof(ModelBinding.ToolMode), Case.Sensitive);
        exception.Message.ShouldContain($"'{text}'", Case.Sensitive);
        exception.Message.ShouldContain(reason, Case.Sensitive);
    }

    [Fact]
    public void A_required_tool_the_definition_does_not_list_names_the_listed_tools()
    {
        var exception = Should.Throw<TraconCompilationException>(
            () => Compile(TestData.Binding() with { ToolMode = "required:missing" }, toolNames: ["lookup", "search"]));

        exception.Message.ShouldContain("lookup, search");
    }

    [Fact]
    public void An_unknown_reasoning_output_does_not_compile_and_lists_the_valid_values()
    {
        var exception = Should.Throw<TraconCompilationException>(
            () => Compile(TestData.Binding() with { ReasoningOutput = "everything" }));

        exception.Message.ShouldContain("'everything'", Case.Sensitive);
        exception.Message.ShouldContain("None, Summary, Full", Case.Sensitive);
    }

    /// <param name="topK">A top-k limit that is not greater than zero.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_top_k_that_is_not_positive_does_not_compile(int topK)
    {
        var exception = Should.Throw<TraconCompilationException>(() => Compile(TestData.Binding() with { TopK = topK }));

        exception.Message.ShouldContain(nameof(ModelBinding.TopK), Case.Sensitive);
        exception.Message.ShouldContain("greater than zero", Case.Sensitive);
    }

    [Fact]
    public void A_stop_sequence_without_text_does_not_compile()
    {
        var exception = Should.Throw<TraconCompilationException>(
            () => Compile(TestData.Binding() with { StopSequences = ["END", string.Empty] }));

        exception.Message.ShouldContain(nameof(ModelBinding.StopSequences), Case.Sensitive);
    }

    [Fact]
    public void The_chat_options_hold_their_own_copy_of_the_stop_list()
    {
        var stop = new List<string> { "END" };
        var options = Compile(TestData.Binding() with { StopSequences = stop });

        stop.Add("later");

        options.StopSequences.ShouldBe(["END"]);
    }

    /// <summary>Compiles the definition and returns the chat options an extension sees on the finished options object.</summary>
    private static ChatOptions Compile(ModelBinding model, IReadOnlyList<string>? toolNames = null)
    {
        ChatOptions? seen = null;
        var registry = TestData.Registry(TestData.Tool("lookup"), TestData.Tool("search"));
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            registry,
            agentExtensions: [new DelegateAgentExtension("probe", context => seen = context.ChatAgentOptions!.ChatOptions, null)]);

        _ = compiler.Compile(TestData.Definition() with
        {
            Model = model,
            ToolNames = toolNames ?? ["lookup"],
            ExtensionNames = ["probe"],
        });

        return seen.ShouldNotBeNull();
    }
}

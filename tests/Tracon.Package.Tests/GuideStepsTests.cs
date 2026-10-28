using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// What <see cref="GuideSteps"/> counts as a step. <see cref="FirstAgentPathTests"/> measures the
/// guide with it, so the measure itself needs a test: a parser that silently skipped a kind of
/// step would hold the baseline green while the path grew.
/// </summary>
public sealed class GuideStepsTests
{
    // The shape the guide had before the template took the model name as a flag: five commands
    // and one edit to Program.cs.
    private const string GuideWithACodeEdit = """
        # Guide

        ## Create the host

        ```bash
        dotnet new install Tracon.Templates@1.0.0
        dotnet new tracon-api -n MyAgents
        cd MyAgents
        ```

        ```bash
        # a comment is not a step
        dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "YOUR_API_KEY"
        ```

        ```csharp title="Program.cs (excerpt)"
        const string ModelName = "your-model";
        ```

        ```json
        { "an": "illustration, not a step" }
        ```

        ```bash
        dotnet run \
          --urls http://localhost:5081
        ```

        ## Run it

        ```bash
        curl http://localhost:5081/tracon/api/agents
        ```
        """;

    [Fact]
    public void A_command_and_a_titled_listing_each_count_as_one_step()
    {
        var steps = GuideSteps.Read(GuideWithACodeEdit, "Create the host", "Run it");

        steps.Count.ShouldBe(6);
        steps.Count(step => step.Kind == GuideStepKind.FileEdit).ShouldBe(1);
        steps[^1].Text.ShouldBe("dotnet run --urls http://localhost:5081");
    }

    [Fact]
    public void A_block_outside_the_section_is_not_a_step()
    {
        var steps = GuideSteps.Read(GuideWithACodeEdit, "Create the host", "Run it");

        steps.ShouldNotContain(step => step.Text.StartsWith("curl", StringComparison.Ordinal));
    }

    [Fact]
    public void A_renamed_heading_fails_with_a_message_that_names_it()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => GuideSteps.Read(GuideWithACodeEdit, "Create the project", "Run it"));

        exception.Message.ShouldContain("Create the project");
    }

    [Theory]
    [InlineData("cd MyAgents", new[] { "cd", "MyAgents" })]
    [InlineData("dotnet user-secrets set \"K:A\" \"two words\"", new[] { "dotnet", "user-secrets", "set", "K:A", "two words" })]
    [InlineData("dotnet new tracon-api -n X --model \"\"", new[] { "dotnet", "new", "tracon-api", "-n", "X", "--model", "" })]
    [InlineData("curl -d '{\"message\":\"a b\"}' http://h/p", new[] { "curl", "-d", "{\"message\":\"a b\"}", "http://h/p" })]
    public void Words_split_on_whitespace_outside_quotes(string commandLine, string[] expected)
        => GuideSteps.Words(commandLine).ShouldBe(expected);
}

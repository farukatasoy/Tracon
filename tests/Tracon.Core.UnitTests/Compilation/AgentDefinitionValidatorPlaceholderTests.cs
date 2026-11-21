using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The validation report warns when the instructions write a declared parameter in single braces.
/// </summary>
/// <remarks>
/// A prompt moved from a system that writes <c>{name}</c> compiles, runs and sends the braces to the model; nothing
/// else shows the mistake. The finding is a warning and only for a name the definition declares as a parameter: a
/// JSON example in the same text must stay quiet.
/// </remarks>
public sealed class AgentDefinitionValidatorPlaceholderTests
{
    private static readonly AgentParameter Customer = new() { Name = "customer", Kind = AgentParameterKind.Text, DefaultValue = "a customer" };

    [Fact]
    public async Task A_declared_parameter_written_in_single_braces_is_warned_about_once_and_the_definition_stays_valid()
    {
        var report = await ValidateAsync("Hello {customer}, thank you {customer}.", [Customer]);

        report.Valid.ShouldBeTrue("a warning never stops the definition");

        var warning = report.Messages.ShouldHaveSingleItem();
        warning.Severity.ShouldBe(ValidationSeverity.Warning);
        warning.Code.ShouldBe("single_brace_placeholder");
        warning.Path.ShouldBe("instructions");
        warning.Message.ShouldContain("'{customer}'", Case.Sensitive);
        warning.Message.ShouldContain("'{{customer}}'", Case.Sensitive);
    }

    [Theory]
    [InlineData("Hello {customer}")]
    [InlineData("Hello { customer }")]
    [InlineData("Hello {\tcustomer\t}")]
    public async Task The_warning_reads_the_spacing_the_binder_reads(string instructions)
    {
        var report = await ValidateAsync(instructions, [Customer]);

        report.Messages.ShouldHaveSingleItem().Code.ShouldBe("single_brace_placeholder");
    }

    [Theory]
    [InlineData("Hello {{customer}}")]
    [InlineData("Hello {{ customer }}")]
    [InlineData("Hello {{{customer}}}")]
    [InlineData("Hello {other}")]
    [InlineData("Return {\"customer\": \"text\"}")]
    [InlineData("Hello {customer.name}")]
    [InlineData("Hello {customer and more}")]
    public async Task Nothing_is_said_about_a_correct_placeholder_an_undeclared_name_or_other_text_in_braces(string instructions)
    {
        var report = await ValidateAsync(instructions, [Customer]);

        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_variant_for_a_culture_is_checked_too_and_the_path_names_it()
    {
        var report = await ValidateAsync(
            "Hello {{customer}}",
            [Customer],
            new Dictionary<string, string>(StringComparer.Ordinal) { ["tr"] = "Merhaba {customer}" });

        var warning = report.Messages.ShouldHaveSingleItem();

        warning.Code.ShouldBe("single_brace_placeholder");
        warning.Path.ShouldBe("instructionsByCulture[tr]");
    }

    [Fact]
    public async Task A_definition_without_parameters_is_not_checked()
    {
        var report = await ValidateAsync("Hello {customer}", []);

        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_declared_parameter_that_no_placeholder_uses_is_not_reported()
    {
        var report = await ValidateAsync("Answer briefly.", [Customer]);

        report.Valid.ShouldBeTrue();
        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_declared_names_in_single_braces_are_two_warnings()
    {
        var report = await ValidateAsync(
            "{customer} wrote about {topic}.",
            [Customer, new AgentParameter { Name = "topic", Kind = AgentParameterKind.Text, DefaultValue = "x" }]);

        report.Messages.Select(static message => message.Code).ShouldBe(["single_brace_placeholder", "single_brace_placeholder"]);
    }

    private static async Task<AgentValidationReport> ValidateAsync(
        string instructions,
        IReadOnlyList<AgentParameter> parameters,
        IReadOnlyDictionary<string, string>? byCulture = null)
    {
        var models = TestData.Providers(new FakeModelProvider());
        var tools = TestData.Registry();
        var catalog = new FakeAgentCatalog([]);
        var options = Options.Create(new TraconOptions());
        var skills = new AgentSkillCatalog([], new InMemoryAgentSkillStore(), new SingleTenantContext(options), options);
        var compiler = new AgentDefinitionCompiler(models, tools, new AgentCompilationServices { Skills = skills, CallableAgents = new CallableAgentResolver(catalog) });
        var validator = new AgentDefinitionValidator(models, tools, skills, catalog, compiler, options);

        return await validator.ValidateAsync(
            TestData.Definition() with { Instructions = instructions, InstructionsByCulture = byCulture, Parameters = parameters },
            TestContext.Current.CancellationToken);
    }
}

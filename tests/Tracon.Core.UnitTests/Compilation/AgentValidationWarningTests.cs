using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// F-320 / F-346 — the validation report warns when a binding leaves
/// <see cref="ModelBinding.MaxOutputTokens"/> empty and the provider's default
/// is below the catalog's maximum, for a structured response or high reasoning
/// effort. The warning never stops the definition.
/// </summary>
/// <remarks>
/// The raw client states its default through <see cref="ModelOutputLimit"/>
/// (<c>IChatClient.GetService</c>); the validator reads it from the FULL
/// pipeline the registry builds, so these tests also prove every ring passes
/// the question through.
/// </remarks>
public sealed class AgentValidationWarningTests
{
    private static readonly JsonElement Schema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    [Fact]
    public async Task A_structured_response_without_a_limit_below_the_catalog_maximum_is_warned_about()
    {
        var report = await ValidateAsync(
            limit: new ModelOutputLimit { DefaultMaxOutputTokens = 4096 },
            catalogMaximum: 128_000,
            binding: TestData.Binding() with { ResponseFormat = JsonSchema() });

        report.Valid.ShouldBeTrue("a warning never stops the definition");

        var warning = report.Messages.ShouldHaveSingleItem();
        warning.Severity.ShouldBe(ValidationSeverity.Warning);
        warning.Code.ShouldBe("output_truncation_risk");
        warning.Path.ShouldBe("model.maxOutputTokens");
        warning.Message.ShouldContain("4096");
        warning.Message.ShouldContain("128000");
        warning.Message.ShouldContain("structured");
    }

    [Fact]
    public async Task High_reasoning_effort_without_a_limit_is_warned_about_for_plain_text_too()
    {
        var report = await ValidateAsync(
            limit: new ModelOutputLimit { DefaultMaxOutputTokens = 4096, HighReasoningEffort = true },
            catalogMaximum: 128_000,
            binding: TestData.Binding());

        report.Messages.ShouldHaveSingleItem().Message.ShouldContain("reasoning");
    }

    [Theory]
    [InlineData("explicit limit")]
    [InlineData("plain text, normal effort")]
    [InlineData("default not below the maximum")]
    [InlineData("no catalog maximum")]
    [InlineData("provider states no default")]
    public async Task No_warning_when_any_condition_is_missing(string missing)
    {
        var binding = TestData.Binding() with { ResponseFormat = JsonSchema() };
        ModelOutputLimit? limit = new() { DefaultMaxOutputTokens = 4096 };
        int? catalogMaximum = 128_000;

        switch (missing)
        {
            case "explicit limit":
                binding = binding with { MaxOutputTokens = 64_000 };
                break;
            case "plain text, normal effort":
                binding = binding with { ResponseFormat = null };
                break;
            case "default not below the maximum":
                catalogMaximum = 4096;
                break;
            case "no catalog maximum":
                catalogMaximum = null;
                break;
            default:
                limit = null;
                break;
        }

        var report = await ValidateAsync(limit, catalogMaximum, binding);

        report.Valid.ShouldBeTrue();
        report.Messages.ShouldBeEmpty();
    }

    private static AgentResponseFormat JsonSchema() => new()
    {
        Kind = AgentResponseFormatKind.JsonSchema,
        Schema = Schema,
    };

    private static async Task<AgentValidationReport> ValidateAsync(ModelOutputLimit? limit, int? catalogMaximum, ModelBinding binding)
    {
        var provider = new FakeModelProvider(
            new OutputLimitChatClient(limit),
            models: [new ModelDescriptor { Name = "fake-model", MaxOutputTokens = catalogMaximum, SupportsStructuredOutput = true }]);

        var models = TestData.Providers(provider);
        var tools = TestData.Registry();
        var catalog = new FakeAgentCatalog([]);
        var options = Options.Create(new TraconOptions());
        var skills = new AgentSkillCatalog([], new InMemoryAgentSkillStore(), new SingleTenantContext(options), options);
        var compiler = new AgentDefinitionCompiler(models, tools, skills: skills, callableAgents: new CallableAgentResolver(catalog));
        var validator = new AgentDefinitionValidator(models, tools, skills, catalog, compiler, options);

        return await validator.ValidateAsync(TestData.Definition() with { Model = binding }, TestContext.Current.CancellationToken);
    }

    /// <summary>A raw client that states its default output limit, as a provider's own client does.</summary>
    private sealed class OutputLimitChatClient(ModelOutputLimit? limit) : DelegatingChatClient(new FakeChatClient())
    {
        public override object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(ModelOutputLimit) && limit is not null
                ? limit
                : base.GetService(serviceType, serviceKey);
    }
}

using System.Reflection;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon.Core.UnitTests.Catalog;

/// <summary>
/// The model a factory agent declares carries no request field, and every member of
/// <see cref="ModelBinding"/> is classified.
/// </summary>
/// <remarks>
/// The classification is two lists in production code (AOT: no reflection there); the reflection
/// is here. A new <see cref="ModelBinding"/> member that is in neither list fails
/// <see cref="Every_model_binding_property_is_classified"/>, so it is decided on purpose whether a factory
/// agent can declare it.
/// </remarks>
public sealed class FactoryModelFieldTests
{
    private static readonly ModelBinding Valid = new() { Provider = "openai", Model = "gpt-5.4-mini" };

    public static TheoryData<string, ModelBinding> RequestFieldCases() => new()
    {
        { nameof(ModelBinding.Temperature), Valid with { Temperature = 0.2f } },
        { nameof(ModelBinding.TopP), Valid with { TopP = 0.9f } },
        { nameof(ModelBinding.TopK), Valid with { TopK = 40 } },
        { nameof(ModelBinding.MaxOutputTokens), Valid with { MaxOutputTokens = 256 } },
        { nameof(ModelBinding.Seed), Valid with { Seed = 7 } },
        { nameof(ModelBinding.FrequencyPenalty), Valid with { FrequencyPenalty = 0.1f } },
        { nameof(ModelBinding.PresencePenalty), Valid with { PresencePenalty = 0.1f } },
        { nameof(ModelBinding.StopSequences), Valid with { StopSequences = ["END"] } },
        { nameof(ModelBinding.ToolMode), Valid with { ToolMode = "auto" } },
        { nameof(ModelBinding.AllowMultipleToolCalls), Valid with { AllowMultipleToolCalls = false } },
        { nameof(ModelBinding.ReasoningEffort), Valid with { ReasoningEffort = "Low" } },
        { nameof(ModelBinding.ReasoningOutput), Valid with { ReasoningOutput = "Summary" } },
        { nameof(ModelBinding.ResponseFormat), Valid with { ResponseFormat = new AgentResponseFormat { Kind = AgentResponseFormatKind.Json } } },
    };

    [Theory]
    [MemberData(nameof(RequestFieldCases))]
    public void A_request_field_is_refused_by_name(string field, ModelBinding model)
    {
        var sync = Should.Throw<ArgumentException>(() => Builder().AddAgent("factory", model, static (AgentFactoryContext _) => null!));
        var asynchronous = Should.Throw<ArgumentException>(() => Builder().AddAgent(
            "factory",
            model,
            static (AgentFactoryContext _, CancellationToken _) => ValueTask.FromResult<AIAgent>(null!)));

        foreach (var exception in new[] { sync, asynchronous })
        {
            exception.ParamName.ShouldBe("model");
            exception.Message.ShouldContain($"{nameof(ModelBinding)}.{field}");
            exception.Message.ShouldContain("factory");
            exception.Message.ShouldContain("ChatOptions");
        }
    }

    [Fact]
    public void An_empty_stop_sequence_list_is_not_a_request_field()
    {
        // The binding says nothing is sent when the list is null or empty, so an empty list declares nothing.
        Should.NotThrow(() => Builder().AddAgent(
            "factory",
            Valid with { StopSequences = [] },
            static (AgentFactoryContext _) => null!));
    }

    [Fact]
    public void The_members_a_registry_reads_are_accepted()
    {
        var model = Valid with
        {
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
            Fallbacks = [new ModelFallback { Provider = "anthropic", Model = "claude-haiku-4-5" }],
            ResponseCache = new ResponseCacheSettings { Enabled = true, Lifetime = TimeSpan.FromMinutes(5) },
            ToolLoop = new ToolLoopSettings { MaximumConsecutiveErrors = 2 },
        };

        Should.NotThrow(() => Builder().AddAgent("factory", model, static (AgentFactoryContext _) => null!));
    }

    [Theory]
    [InlineData("", "gpt-5.4-mini")]
    [InlineData("  ", "gpt-5.4-mini")]
    [InlineData("openai", "")]
    [InlineData("openai", " ")]
    public void A_binding_without_a_provider_or_a_model_name_is_refused(string provider, string model)
    {
        var binding = new ModelBinding { Provider = provider, Model = model };

        var exception = Should.Throw<ArgumentException>(() => Builder().AddAgent("factory", binding, static (AgentFactoryContext _) => null!));

        exception.ParamName.ShouldBe("model");
        exception.Message.ShouldContain("provider");
    }

    [Fact]
    public void Every_model_binding_property_is_classified()
    {
        var properties = typeof(ModelBinding)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(static property => property.Name)
            .Where(static name => !string.Equals(name, "EqualityContract", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        var classified = FactoryModelFields.RequestFieldNames
            .Concat(FactoryModelFields.AcceptedFieldNames)
            .Order(StringComparer.Ordinal)
            .ToList();

        classified.ShouldBe(properties, "a ModelBinding member must be a request field or an accepted member, and in one list only.");
        FactoryModelFields.RequestFieldNames.Intersect(FactoryModelFields.AcceptedFieldNames, StringComparer.Ordinal).ShouldBeEmpty();
    }

    [Fact]
    public void Every_request_field_the_compiler_turns_into_a_chat_option_is_a_request_field()
    {
        // The reason the fields are refused: the compiler is the only reader. A binding member the compiler
        // copies into ChatOptions but that is not on the request list would be ignored without a word.
        var compiler = File.ReadAllText(Path.Combine(
            RepositoryRoot, "src", "Tracon.Core", "Compilation", "AgentDefinitionCompiler.ChatOptions.cs"));

        var members = new[]
        {
            nameof(ModelBinding.Temperature),
            nameof(ModelBinding.TopP),
            nameof(ModelBinding.MaxOutputTokens),
            nameof(ModelBinding.Seed),
            nameof(ModelBinding.FrequencyPenalty),
            nameof(ModelBinding.PresencePenalty),
            nameof(ModelBinding.AllowMultipleToolCalls),
        };

        foreach (var member in members)
        {
            compiler.ShouldContain($"model.{member}", customMessage: $"the compiler no longer reads {member}; update the lists");
            FactoryModelFields.RequestFieldNames.Contains(member, StringComparer.Ordinal).ShouldBeTrue($"{member} is not on the request list");
        }
    }

    private static TraconBuilder Builder() => new(new Microsoft.Extensions.DependencyInjection.ServiceCollection());

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Tracon.slnx was not found above the test output directory.");
    }
}

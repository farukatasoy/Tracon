using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// F-598 — a request field the provider's API surface would drop stops the compilation and makes
/// the validation report name the field and the provider (<see cref="ISamplingSupport"/>).
/// </summary>
/// <remarks>
/// The model adapters send only some of these fields and drop the rest without an error. The rule is
/// the one <see cref="SamplingCapabilityTests"/> pins for the catalog flag: one check, read by the
/// compiler and the validator, covering the fallback links.
/// </remarks>
public sealed class SamplingSupportTests
{
    public static TheoryData<string> Fields() => new()
    {
        nameof(ModelBinding.Seed),
        nameof(ModelBinding.TopK),
        nameof(ModelBinding.StopSequences),
        nameof(ModelBinding.FrequencyPenalty),
        nameof(ModelBinding.PresencePenalty),
        nameof(ModelBinding.ToolMode),
        nameof(ModelBinding.AllowMultipleToolCalls),
        nameof(ModelBinding.ReasoningOutput),
    };

    /// <param name="field">The request field the definition sets.</param>
    [Theory]
    [MemberData(nameof(Fields))]
    public void A_field_the_provider_does_not_send_does_not_compile(string field)
    {
        var compiler = Compiler(new FakeModelProvider(supportedSamplingFields: SamplingFields.None));

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition(Setting(field))));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain($"{nameof(ModelBinding)}.{field}", Case.Sensitive);
        exception.Message.ShouldContain("'fake' provider");
    }

    /// <param name="field">The request field the definition sets.</param>
    [Theory]
    [MemberData(nameof(Fields))]
    public void A_field_the_provider_sends_compiles(string field)
    {
        var compiler = Compiler(new FakeModelProvider());

        compiler.Compile(Definition(Setting(field))).ShouldNotBeNull();
    }

    [Fact]
    public void A_field_only_that_fields_flag_allows_does_not_open_the_others()
    {
        var compiler = Compiler(new FakeModelProvider(supportedSamplingFields: SamplingFields.Seed));

        compiler.Compile(Definition(Setting(nameof(ModelBinding.Seed)))).ShouldNotBeNull();

        var exception = Should.Throw<TraconCompilationException>(
            () => compiler.Compile(Definition(Setting(nameof(ModelBinding.TopK)))));

        exception.Message.ShouldContain(nameof(ModelBinding.TopK));
        exception.Message.ShouldNotContain(nameof(ModelBinding.Seed));
    }

    [Fact]
    public void A_provider_that_states_nothing_sends_none_of_the_fields()
    {
        var compiler = Compiler(new UndeclaringProvider());

        compiler.Compile(Definition(new ModelBinding { Provider = UndeclaringProvider.ProviderName, Model = "m" })).ShouldNotBeNull();

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition(
            new ModelBinding { Provider = UndeclaringProvider.ProviderName, Model = "m", Seed = 1 })));

        exception.Message.ShouldContain("'undeclaring' provider");
    }

    [Fact]
    public void A_definition_that_sets_none_compiles_whatever_the_provider_sends()
    {
        var compiler = Compiler(new FakeModelProvider(supportedSamplingFields: SamplingFields.None));

        compiler.Compile(TestData.Definition()).ShouldNotBeNull();
    }

    [Fact]
    public void An_empty_stop_list_sets_nothing_and_needs_no_support()
    {
        var compiler = Compiler(new FakeModelProvider(supportedSamplingFields: SamplingFields.None));

        compiler.Compile(Definition(TestData.Binding() with { StopSequences = [] })).ShouldNotBeNull();
    }

    [Fact]
    public void A_fallback_link_that_does_not_send_a_field_does_not_compile()
    {
        // FallbackChatClient sends a link the same ChatOptions, Seed included.
        var primary = new FakeModelProvider();
        var backup = new FakeModelProvider(name: "backup", supportedSamplingFields: SamplingFields.TopK);
        var compiler = Compiler(primary, backup);

        var definition = Definition(TestData.Binding() with
        {
            Seed = 7,
            Fallbacks = [new ModelFallback { Provider = "backup", Model = "fake-model" }],
        });

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("'backup' provider");
        exception.Message.ShouldNotContain("'fake' provider");
        exception.Message.ShouldContain("Providers that send it: fake");
    }

    [Fact]
    public async Task The_validation_report_names_each_field_with_its_path_and_provider()
    {
        var validator = Validator(new FakeModelProvider(supportedSamplingFields: SamplingFields.Seed));

        var report = await validator.ValidateAsync(
            Definition(TestData.Binding() with { Seed = 7, TopK = 40, ReasoningOutput = "Summary" }),
            TestContext.Current.CancellationToken);

        report.Valid.ShouldBeFalse();
        report.Messages.Select(static message => (message.Code, message.Path, message.Severity)).ShouldBe(
        [
            ("sampling_field_unsupported", "model.topK", ValidationSeverity.Error),
            ("sampling_field_unsupported", "model.reasoningOutput", ValidationSeverity.Error),
        ]);
        report.Messages.ShouldAllBe(static message => message.Message.Contains("'fake' provider", StringComparison.Ordinal));
        report.Messages.ShouldNotContain(static message => message.Code == "compilation_error");
    }

    [Fact]
    public async Task The_validation_report_is_clean_when_the_provider_sends_every_field_set()
    {
        var validator = Validator(new FakeModelProvider());

        var report = await validator.ValidateAsync(
            Definition(TestData.Binding() with { Seed = 7, TopK = 40, ToolMode = "auto", AllowMultipleToolCalls = false }),
            TestContext.Current.CancellationToken);

        report.Valid.ShouldBeTrue();
        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_validation_report_checks_a_fallback_link()
    {
        var validator = Validator(
            new FakeModelProvider(),
            new FakeModelProvider(name: "backup", supportedSamplingFields: SamplingFields.None));

        var report = await validator.ValidateAsync(
            Definition(TestData.Binding() with
            {
                FrequencyPenalty = 0.2f,
                Fallbacks = [new ModelFallback { Provider = "backup", Model = "fake-model" }],
            }),
            TestContext.Current.CancellationToken);

        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("sampling_field_unsupported");
        message.Path.ShouldBe("model.frequencyPenalty");
        message.Message.ShouldContain("'backup' provider");
    }

    [Fact]
    public void The_registry_hands_the_provider_statement_to_the_descriptor()
    {
        var registry = TestData.Providers(
            new FakeModelProvider(supportedSamplingFields: SamplingFields.Seed | SamplingFields.TopK),
            new UndeclaringProvider());

        var descriptors = registry.List().ToDictionary(static descriptor => descriptor.Name, StringComparer.Ordinal);

        descriptors["fake"].SupportedSamplingFields.ShouldBe(SamplingFields.Seed | SamplingFields.TopK);
        descriptors[UndeclaringProvider.ProviderName].SupportedSamplingFields.ShouldBe(SamplingFields.None);
    }

    /// <summary>The binding that sets exactly <paramref name="field"/>.</summary>
    internal static ModelBinding Setting(string field) => field switch
    {
        nameof(ModelBinding.Seed) => TestData.Binding() with { Seed = 7 },
        nameof(ModelBinding.TopK) => TestData.Binding() with { TopK = 40 },
        nameof(ModelBinding.StopSequences) => TestData.Binding() with { StopSequences = ["END"] },
        nameof(ModelBinding.FrequencyPenalty) => TestData.Binding() with { FrequencyPenalty = 0.5f },
        nameof(ModelBinding.PresencePenalty) => TestData.Binding() with { PresencePenalty = 0.5f },
        nameof(ModelBinding.ToolMode) => TestData.Binding() with { ToolMode = "auto" },
        nameof(ModelBinding.AllowMultipleToolCalls) => TestData.Binding() with { AllowMultipleToolCalls = false },
        nameof(ModelBinding.ReasoningOutput) => TestData.Binding() with { ReasoningOutput = "Summary" },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    internal static AgentDefinition Definition(ModelBinding model) => TestData.Definition() with { Model = model };

    internal static AgentDefinitionCompiler Compiler(params IModelProvider[] providers)
        => new(TestData.Providers(providers), TestData.Registry());

    internal static AgentDefinitionValidator Validator(params IModelProvider[] providers)
    {
        var models = TestData.Providers(providers);
        var tools = TestData.Registry();
        var catalog = new FakeAgentCatalog([]);
        var options = Options.Create(new TraconOptions());
        var skills = new AgentSkillCatalog([], new InMemoryAgentSkillStore(), new SingleTenantContext(options), options);
        var compiler = new AgentDefinitionCompiler(models, tools, skills: skills, callableAgents: new CallableAgentResolver(catalog));

        return new AgentDefinitionValidator(models, tools, skills, catalog, compiler, options);
    }

    /// <summary>A provider written against <see cref="IModelProvider"/> alone, as a third party's would be.</summary>
    private sealed class UndeclaringProvider : IModelProvider
    {
        public const string ProviderName = "undeclaring";

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "m" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new FakeChatClient();
    }
}

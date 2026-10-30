using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// <see cref="ModelDescriptor.SupportsSampling"/>: a catalog that says
/// a model rejects <c>temperature</c>/<c>top_p</c> stops compilation and makes
/// the validation report name the field. <see langword="null"/> means unknown
/// and checks nothing.
/// </summary>
public sealed class SamplingCapabilityTests
{
    [Fact]
    public void Temperature_on_a_model_that_rejects_sampling_does_not_compile()
    {
        var compiler = Compiler(supportsSampling: false);

        var exception = Should.Throw<TraconCompilationException>(
            () => compiler.Compile(TestData.Definition() with { Model = TestData.Binding() with { Temperature = 0.2f } }));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("fake/fake-model");
        exception.Message.ShouldContain(nameof(ModelBinding.Temperature));
    }

    [Fact]
    public void TopP_on_a_model_that_rejects_sampling_does_not_compile()
    {
        var compiler = Compiler(supportsSampling: false);

        Should.Throw<TraconCompilationException>(
            () => compiler.Compile(TestData.Definition() with { Model = TestData.Binding() with { TopP = 0.9f } }));
    }

    [Fact]
    public void An_unknown_flag_does_not_reject_sampling()
    {
        // null is "the catalog does not say" — rejecting it would turn every
        // catalog entry written before this flag into a compilation error.
        var compiler = Compiler(supportsSampling: null);

        compiler.Compile(TestData.Definition() with { Model = TestData.Binding() with { Temperature = 0.2f } })
            .ShouldNotBeNull();
    }

    [Fact]
    public void A_model_that_accepts_sampling_compiles_with_temperature()
    {
        var compiler = Compiler(supportsSampling: true);

        compiler.Compile(TestData.Definition() with { Model = TestData.Binding() with { Temperature = 0.2f } })
            .ShouldNotBeNull();
    }

    [Fact]
    public void A_model_that_rejects_sampling_compiles_when_the_definition_sets_none()
    {
        var compiler = Compiler(supportsSampling: false);

        compiler.Compile(TestData.Definition()).ShouldNotBeNull();
    }

    [Fact]
    public void A_fallback_link_that_rejects_sampling_does_not_compile()
    {
        // FallbackChatClient sends a link the same ChatOptions, Temperature included.
        var primary = new FakeModelProvider(models: [new ModelDescriptor { Name = "fake-model", SupportsSampling = true }]);
        var fallback = new FakeModelProvider(
            name: "backup",
            models: [new ModelDescriptor { Name = "backup-model", SupportsSampling = false }]);
        var compiler = new AgentDefinitionCompiler(TestData.Providers(primary, fallback), TestData.Registry());

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with
            {
                Temperature = 0.2f,
                Fallbacks = [new ModelFallback { Provider = "backup", Model = "backup-model" }],
            },
        };

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("backup/backup-model");
        exception.Message.ShouldNotContain("fake/fake-model");
    }

    [Fact]
    public async Task The_validation_report_names_each_sampling_field_as_an_error()
    {
        var validator = Validator(supportsSampling: false);

        var report = await validator.ValidateAsync(
            TestData.Definition() with { Model = TestData.Binding() with { Temperature = 0.2f, TopP = 0.9f } },
            TestContext.Current.CancellationToken);

        report.Valid.ShouldBeFalse();
        report.Messages.Select(static message => (message.Code, message.Path, message.Severity)).ShouldBe(
        [
            ("sampling_unsupported", "model.temperature", ValidationSeverity.Error),
            ("sampling_unsupported", "model.topP", ValidationSeverity.Error),
        ]);
        report.Messages.ShouldNotContain(static message => message.Code == "compilation_error");
    }

    [Fact]
    public async Task The_validation_report_is_clean_when_the_flag_is_unknown()
    {
        var validator = Validator(supportsSampling: null);

        var report = await validator.ValidateAsync(
            TestData.Definition() with { Model = TestData.Binding() with { Temperature = 0.2f } },
            TestContext.Current.CancellationToken);

        report.Valid.ShouldBeTrue();
        report.Messages.ShouldBeEmpty();
    }

    private static AgentDefinitionCompiler Compiler(bool? supportsSampling)
        => new(
            TestData.Providers(new FakeModelProvider(models:
            [
                new ModelDescriptor { Name = "fake-model", SupportsSampling = supportsSampling },
            ])),
            TestData.Registry());

    private static AgentDefinitionValidator Validator(bool? supportsSampling)
    {
        var models = TestData.Providers(new FakeModelProvider(models:
        [
            new ModelDescriptor { Name = "fake-model", SupportsSampling = supportsSampling },
        ]));
        var tools = TestData.Registry();
        var catalog = new FakeAgentCatalog([]);
        var options = Options.Create(new TraconOptions());
        var skills = new AgentSkillCatalog([], new InMemoryAgentSkillStore(), new SingleTenantContext(options), options);
        var compiler = new AgentDefinitionCompiler(models, tools, skills: skills, callableAgents: new CallableAgentResolver(catalog));

        return new AgentDefinitionValidator(models, tools, skills, catalog, compiler, options);
    }
}

using Tracon.OpenAI.UnitTests.Infrastructure;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// Verifies that <see cref="OpenAIModelProvider.GetConfigurationDiagnostic"/> follows
/// K-059: it returns whether the key was resolved, never the key's VALUE.
/// </summary>
public sealed class OpenAIModelProviderConfigurationDiagnosticsTests
{
    [Fact]
    public void Key_given_reports_resolved_with_no_hint()
    {
        var provider = CreateProvider(healthCheckOptions: TestData.Options());

        var diagnostic = provider.GetConfigurationDiagnostic();

        diagnostic.ShouldNotBeNull();
        diagnostic.Key.ShouldBe("Tracon:Providers:OpenAI:ApiKey");
        diagnostic.Resolved.ShouldBeTrue();
        diagnostic.Hint.ShouldBeNull();
    }

    [Fact]
    public void Empty_key_reports_unresolved_with_a_hint_but_never_the_value()
    {
        // ChatClientFactory is built with a VALID key (its constructor rejects an
        // empty one) — this test only verifies the EMPTY key of the separately
        // passed healthCheckOptions, used purely for DIAGNOSTICS; the two are
        // deliberately different objects.
        var provider = CreateProvider(healthCheckOptions: TestData.Options(static o => o.ApiKey = null));

        var diagnostic = provider.GetConfigurationDiagnostic();

        diagnostic.ShouldNotBeNull();
        diagnostic.Resolved.ShouldBeFalse();
        diagnostic.Hint.ShouldNotBeNullOrEmpty();
        diagnostic.Hint.ShouldNotContain(TestData.ApiKey);
    }

    [Fact]
    public void Diagnostic_is_null_when_no_health_check_options_are_given()
    {
        var provider = new OpenAIModelProvider(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = "gpt-4o-mini" }]);

        provider.GetConfigurationDiagnostic().ShouldBeNull();
    }

    [Fact]
    public void UseOpenAICompatible_reports_no_diagnostic_because_it_has_no_fixed_section()
    {
        // configurationSectionKey: null -> instead of misreporting the code-defined,
        // non-fixed key of UseOpenAICompatible(), it reports nothing at all.
        var provider = new OpenAIModelProvider(
            "openrouter",
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = "gpt-4o-mini" }],
            healthCheckOptions: TestData.Options(),
            configurationSectionKey: null);

        provider.GetConfigurationDiagnostic().ShouldBeNull();
    }

    private static OpenAIModelProvider CreateProvider(OpenAIProviderOptions healthCheckOptions)
        => new(
            OpenAIProviderNames.ChatCompletions,
            OpenAIApiSurface.ChatCompletions,
            new OpenAIChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = "gpt-4o-mini" }],
            healthCheckOptions: healthCheckOptions);
}

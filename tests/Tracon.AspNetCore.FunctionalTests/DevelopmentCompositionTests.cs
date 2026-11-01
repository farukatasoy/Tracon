using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A host with scope and build validation of the service provider on, as the framework has them in a <c>Development</c>
/// host, starts with the capabilities that need no outside service registered at once (tenancy, MCP, workflows,
/// scheduling, conversation memory, the voice conversation socket).
/// </summary>
/// <remarks>
/// The default environment of a consumer's machine is <c>Development</c>, where the full builder turns the validation on,
/// so a captive dependency (a singleton that holds
/// a scoped service) or a registration whose dependency is missing shows up on the consumer's first run and nowhere in a
/// test that runs in <c>Production</c>. The control case proves the validation is really on in this host: a singleton
/// that depends on a scoped service stops it.
/// </remarks>
public sealed class DevelopmentCompositionTests
{
    private const string TestKey = "functional-test-key";

    [Fact]
    public async Task The_capabilities_that_need_no_outside_service_pass_scope_and_build_validation_together()
    {
        var provider = new StubVoiceProvider();

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition("kod-agent"))
                .UseTenancy(static options => options.Enabled = true)
                .UseMcp()
                .UseWorkflows()
                .UseScheduling()
                .UseConversationMemory(static options => options.VectorDimensions = 8)
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureServices: services =>
            {
                services.AddSingleton<ISpeechTranscriber>(provider);
                services.AddSingleton<ISpeechSynthesizer>(provider);
            },
            environment: "Development",
            validateServiceProvider: true);

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_typical_consumer_stack_passes_scope_and_build_validation()
    {
        // SQLite for storage, the four model providers with a key each, and the capabilities a consumer usually
        // turns on next: the stack of a first real deployment.
        using var database = new TempSqliteDatabase("development-composition");

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseSqlite(database.ConnectionString)
                .UseOpenAI(TestKey)
                .UseAnthropic(TestKey, static options => options.DefaultModel = "claude-sonnet-5")
                .UseGoogle(TestKey, static options => options.DefaultModel = "gemini-3.6-flash")
                .UseAzureOpenAI(new Uri("https://test-resource.openai.azure.com/"), TestKey, static options => options.DefaultDeployment = "production-gpt")
                .UseTenancy(static options => options.Enabled = true)
                .UseMcp()
                .UseWorkflows()
                .UseScheduling(),
            environment: "Development",
            validateServiceProvider: true);

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/agents", UriKind.Relative));

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_validation_is_really_on_in_a_Development_host()
    {
        var start = async () => await TraconTestHost.StartAsync(
            configureServices: static services =>
            {
                services.AddScoped<ScopedDependency>();
                services.AddSingleton<CaptiveSingleton>();
            },
            environment: "Development",
            validateServiceProvider: true);

        var error = await start.ShouldThrowAsync<AggregateException>();

        error.Message.ShouldContain("Cannot consume scoped service", Case.Sensitive);
    }

    private sealed class ScopedDependency;

    private sealed class CaptiveSingleton(ScopedDependency dependency)
    {
        public ScopedDependency Dependency { get; } = dependency;
    }
}

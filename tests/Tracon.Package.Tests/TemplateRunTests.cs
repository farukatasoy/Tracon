using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The default (in-memory) combination must start up without any setup, and the
/// catalog endpoint must return 200 (an earlier change Completion Criteria).
/// </summary>
public sealed class TemplateRunTests(TemplateFixture fixture)
{
    private const string ProjectName = "Run.Sample";
    private const int Port = 5185;

    [Fact]
    public async Task Default_combination_starts_up_without_setup_and_returns_the_catalog()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(ProjectName, dir.Path, "--persistence memory --ui false");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var assembly = await GeneratedHost.BuildAsync(dir.Path, ProjectName);

        await using var host = await GeneratedHost.StartAsync(assembly, dir.Path, Port);
        using var httpClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port}/") };

        using var response = await httpClient.GetAsync("tracon/api/agents");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, host.Output);

        // The model is bound explicitly and the template carries none: with no provider the agent's
        // model name is empty, and the catalog must still list it.
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"support\"");
    }

    /// <summary>
    /// With a provider key set and no model name, the generated app stops at
    /// startup and names the setting to fill in, and the flag that fills it.
    /// </summary>
    /// <remarks>
    /// A provider cannot serve an empty model, so the first run used to come
    /// back as "The model provider request failed." - the provider's own error
    /// names the model and that text is a foreign SDK message, redacted before it
    /// reaches the client. The consumer was left with an error naming nothing on
    /// their very first run.
    /// <para>
    /// The guard is conditional on a provider being configured, and the test
    /// above is the other half of the contract: with no key the same app still
    /// starts and still answers.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Missing_model_name_stops_startup_once_a_provider_is_configured()
    {
        const string projectName = "Run.NoModel";

        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            projectName,
            dir.Path,
            "--persistence memory --ui false --provider openai");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var assembly = await GeneratedHost.BuildAsync(dir.Path, projectName);

        // A syntactically valid key that is never sent anywhere: startup stops
        // before the first model call.
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{Port + 1}",
            ["Tracon__Providers__OpenAI__ApiKey"] = "sk-not-a-real-key",
        };

        var result = await ProcessRunner.RunAsync(
            "dotnet",
            $"\"{assembly}\"",
            dir.Path,
            TimeSpan.FromMinutes(1),
            environment);

        result.ExitCode.ShouldNotBe(0, result.Combined);
        result.Combined.ShouldContain("Tracon:Providers:OpenAI:DefaultModel");
        result.Combined.ShouldContain("--model");
    }

    /// <summary>
    /// The same app with the model name given at generation time starts with a
    /// provider key set: the name is read from <c>appsettings.json</c>, and no
    /// edit of any file is needed.
    /// </summary>
    [Fact]
    public async Task Model_name_from_the_template_lets_a_configured_provider_start()
    {
        const string projectName = "Run.WithModel";

        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            projectName,
            dir.Path,
            "--persistence memory --ui false --provider openai --model m-1");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var assembly = await GeneratedHost.BuildAsync(dir.Path, projectName);

        await using var host = await GeneratedHost.StartAsync(
            assembly,
            dir.Path,
            Port + 2,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["Tracon__Providers__OpenAI__ApiKey"] = "sk-not-a-real-key" });
        using var httpClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port + 2}/") };

        using var response = await httpClient.GetAsync("tracon/api/agents");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, host.Output);
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"m-1\"");
    }
}

using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The path from <c>dotnet new</c> to the first answer, taken exactly as the guide
/// writes it: every command of <c>first-agent.md</c> up to <c>dotnet run</c> runs, then the
/// first request of its <c>Run it</c> section is sent, and a model provider answers it.
/// </summary>
/// <remarks>
/// <para>
/// The first contact used to ask for a code edit, and no test knew how many steps
/// the guide took. This test reads the commands OUT OF THE GUIDE, so a guide that
/// grows a step, or a command the harness does not know, fails here instead of in a
/// reader's first five minutes. A step the harness cannot run is a failure by
/// design: the message says to update the test and the baseline together.
/// </para>
/// <para>
/// The provider is a local fake <see cref="FakeChatCompletionsServer"/>, reached
/// through the setting the provider already reads
/// (<c>Tracon:Providers:OpenAI:Endpoint</c>). The test asserts the model name the
/// request carried is the one the guide passed to <c>dotnet new --model</c>: the
/// value travelled from a command-line flag, through <c>appsettings.json</c>, to
/// the wire.
/// </para>
/// </remarks>
public sealed class FirstAgentPathTests(TemplateFixture fixture)
{
    /// <summary>
    /// The number of steps the guide takes from install to first run. A RATCHET in
    /// both directions: a longer path fails, and a shorter one fails until this
    /// number is lowered, so the path can only get shorter.
    /// </summary>
    private const int StepBaseline = 5;

    // Distinct from the TemplateRunTests ports (5185-5187): the two classes run in parallel.
    private const int Port = 5190;

    private static readonly string GuidePath = Path.Combine(
        RepoPaths.Root,
        "docs-site",
        "src",
        "content",
        "docs",
        "getting-started",
        "first-agent.md");

    [Fact]
    public async Task The_guide_path_gets_an_answer_from_the_provider()
    {
        var markdown = await File.ReadAllTextAsync(GuidePath);
        var steps = GuideSteps.Read(markdown, "Create the host", "Run it");
        var (runPath, runBody) = FirstRunRequest(markdown);

        steps.Count.ShouldBe(
            StepBaseline,
            $"The guide now takes {steps.Count} steps from install to first run, the baseline is {StepBaseline}. " +
            $"A longer path is a regression. A shorter one is progress: lower {nameof(StepBaseline)}.{Environment.NewLine}" +
            string.Join(Environment.NewLine, steps.Select(step => $"  {step.Kind}: {step.Text}")));

        await using var provider = FakeChatCompletionsServer.Start();
        using var root = new TempDirectory();

        await using var walk = new Walk(fixture, root.Path, provider.BaseAddress);

        foreach (var step in steps)
        {
            await walk.RunAsync(step);
        }

        walk.Model.ShouldNotBeNullOrWhiteSpace("The guide never passes --model, so the agent has no model to ask for.");
        walk.Host.ShouldNotBeNull("The guide never starts the host.");

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port}/"), Timeout = TimeSpan.FromSeconds(60) };

        // The run endpoint streams server-sent events; reading the body to its end
        // waits for the run to finish.
        using var response = await client.PostAsync(runPath, new StringContent(runBody, System.Text.Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, body);
        body.ShouldContain(FakeChatCompletionsServer.Answer, Case.Sensitive, $"The run did not carry the provider's reply.{Environment.NewLine}{walk.Host.Output}");

        provider.RequestedModels.ShouldHaveSingleItem(
            $"One run is one model call.{Environment.NewLine}{walk.Host.Output}")
            .ShouldBe(walk.Model);
        provider.UnexpectedPaths.ShouldBeEmpty("The host called a provider path the fake does not serve.");
    }

    /// <summary>The URL path and the JSON body of the first <c>curl</c> in the guide's <c>Run it</c> section.</summary>
    /// <remarks>
    /// Read from the guide, not written here: a guide whose request names a path or a body the host does
    /// not serve fails this test instead of its reader's first run.
    /// </remarks>
    private static (string Path, string Body) FirstRunRequest(string markdown)
    {
        var curl = GuideSteps.Read(markdown, "Run it", "Look at what happened")
            .First(step => step.Kind == GuideStepKind.Command && step.Text.StartsWith("curl ", StringComparison.Ordinal));
        var words = GuideSteps.Words(curl.Text);

        words.Contains("POST", StringComparer.Ordinal).ShouldBeTrue($"The first request of 'Run it' is not a POST: {curl.Text}");

        var url = words.Single(word => word.StartsWith("http://localhost:5081/", StringComparison.Ordinal));
        var body = words[Array.IndexOf(words, "-d") + 1];

        return (new Uri(url).AbsolutePath.TrimStart('/'), body);
    }

    /// <summary>Executes the steps of the guide, one at a time, in the harness's own temporary directory.</summary>
    private sealed class Walk(TemplateFixture fixture, string root, Uri providerEndpoint) : IAsyncDisposable
    {
        private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal)
        {
            // The one setting the guide does not mention: it points the provider at the fake.
            ["Tracon__Providers__OpenAI__Endpoint"] = providerEndpoint.ToString(),
        };

        private string _directory = root;

        /// <summary>The value the guide passed to <c>--model</c>, or <see langword="null"/> when it passed none.</summary>
        public string? Model { get; private set; }

        /// <summary>The running host, once the guide's last command started it.</summary>
        public ManagedProcess? Host { get; private set; }

        public async Task RunAsync(GuideStep step)
        {
            if (step.Kind != GuideStepKind.Command)
            {
                throw Unknown(step);
            }

            switch (GuideSteps.Words(step.Text))
            {
                case ["dotnet", "new", "install", ..]:
                    // The fixture installed the packed template already; the guide's line is the published form of it.
                    break;

                case ["dotnet", "new", "tracon-api", .. var arguments]:
                    await GenerateAsync(arguments);
                    break;

                case ["cd", var target]:
                    _directory = Path.GetFullPath(Path.Combine(_directory, target));
                    Directory.Exists(_directory).ShouldBeTrue($"The guide changes to '{target}', which the previous step did not create.");
                    break;

                case ["dotnet", "user-secrets", "set", var key, var value]:
                    // user-secrets reach the host as configuration; so do environment variables, with '__' for ':'.
                    _environment[key.Replace(":", "__", StringComparison.Ordinal)] = value;
                    break;

                case ["dotnet", "run", ..]:
                    // `dotnet run --urls` picks the guide's port; the harness listens on its own, so a fixed port cannot collide.
                    var projectName = Path.GetFileNameWithoutExtension(Directory.EnumerateFiles(_directory, "*.csproj").Single());
                    var assembly = await GeneratedHost.BuildAsync(_directory, projectName);
                    Host = await GeneratedHost.StartAsync(assembly, _directory, Port, _environment);
                    break;

                default:
                    throw Unknown(step);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Host is not null)
            {
                await Host.DisposeAsync();
            }
        }

        private static InvalidOperationException Unknown(GuideStep step) => new(
            $"The guide has a step this test cannot run: {step.Kind} '{step.Text}'. " +
            "A new kind of step changes the path a reader takes: teach the test to run it, and update the baseline.");

        private async Task GenerateAsync(string[] arguments)
        {
            var nameIndex = Array.FindIndex(arguments, argument => argument is "-n" or "--name");
            nameIndex.ShouldBeGreaterThanOrEqualTo(0, "The guide runs 'dotnet new tracon-api' without -n; the harness needs the project name.");

            var name = arguments[nameIndex + 1];
            var rest = arguments.Where((_, index) => index != nameIndex && index != nameIndex + 1).ToArray();
            var modelIndex = Array.IndexOf(rest, "--model");

            Model = modelIndex >= 0 ? rest[modelIndex + 1] : null;

            // The guide creates the project under the current directory, named after the project.
            var result = await fixture.NewAsync(
                name,
                Path.Combine(_directory, name),
                string.Join(' ', rest.Select(argument => argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument)));
            result.ExitCode.ShouldBe(0, result.Combined);
        }
    }
}

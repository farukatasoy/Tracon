using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// A consumer that takes Microsoft.Extensions.AI 11.0.0, a major above the 10.11.0 floor Tracon declares.
/// </summary>
/// <remarks>
/// <para>
/// The floor stays at 10.11.0 because 11.0 belongs to .NET 11 and Microsoft Agent Framework 1.28 itself stands on
/// 10.11.0. A stable dependency is a lower bound, so a consumer can still raise it, and the promise Tracon makes
/// about that is the one this test keeps: the packages restore with the 11.0 packages beside them, the host
/// starts, and an agent of the framework runs a turn over a chat client written against the 11.0 interface.
/// A member that moved between the two majors would show here as a <c>MissingMethodException</c> at start or at the
/// turn, not as a restore error.
/// </para>
/// <para>
/// The restore reaches nuget.org for the 11.0 packages, like the mixed-release shapes do.
/// </para>
/// </remarks>
[Collection(RepositoryTreeGate.Name)]
public sealed class UpperBoundConsumerTests(IsolatedPackageCache cache) : IClassFixture<IsolatedPackageCache>
{
    private const string EchoLine = "UPPER-BOUND-TURN-ANSWERED";
    private const string AssemblyLine = "MEAI-ABSTRACTIONS-LOADED: ";

    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public async Task A_consumer_that_references_Microsoft_Extensions_AI_11_restores_starts_and_runs_a_turn()
    {
        using var dir = new TempDirectory();
        await MixedVersionConsumerProject.WriteNuGetConfigAsync(dir.Path);
        await MixedVersionConsumerProject.WriteWebHostAsync(
            dir.Path,
            ("Tracon.AspNetCore", ReleaseArtifactFixture.Version),
            ("Microsoft.Extensions.AI", "11.0.0"),
            ("Microsoft.Extensions.AI.Abstractions", "11.0.0"));

        await File.WriteAllTextAsync(Path.Combine(dir.Path, "Program.cs"), Program);

        var build = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, RestoreTimeout, cache.Environment);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldNotContain("NU1", customMessage: "a restore warning would mean the graph needed a downgrade or a conflict resolution:" + Environment.NewLine + build.Combined);

        var run = await ProcessRunner.RunAsync("dotnet", "run -c Release --no-build", dir.Path, RunTimeout, cache.Environment);

        run.ExitCode.ShouldBe(0, run.Combined);
        run.Combined.ShouldContain(MixedVersionConsumerProject.StartedLine, customMessage: run.Combined);
        run.Combined.ShouldContain(AssemblyLine + "11.", customMessage: "the host did not load the 11.0 assembly it was asked to:" + Environment.NewLine + run.Combined);
        run.Combined.ShouldContain(EchoLine + ": echo: hello", customMessage: run.Combined);
    }

    /// <summary>The host the shape runs: Tracon starts, then an agent answers one turn over an echo chat client.</summary>
    private static readonly string Program = $$"""
        using Microsoft.Agents.AI;
        using Microsoft.Extensions.AI;
        using Tracon;

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddTracon();

        var app = builder.Build();
        app.MapTracon();

        await app.StartAsync();
        Console.WriteLine("{{MixedVersionConsumerProject.StartedLine}}");
        Console.WriteLine("{{AssemblyLine}}" + typeof(IChatClient).Assembly.GetName().Version);

        var agent = new ChatClientAgent(new EchoChatClient(), instructions: "Echo the user.", name: "echo");
        var response = await agent.RunAsync("hello");
        Console.WriteLine("{{EchoLine}}: " + response.Text);

        await app.StopAsync();

        internal sealed class EchoChatClient : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                var last = messages.LastOrDefault(static message => message.Role == ChatRole.User)?.Text ?? string.Empty;

                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "echo: " + last)));
            }

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                var response = await GetResponseAsync(messages, options, cancellationToken);

                foreach (var update in response.ToChatResponseUpdates())
                {
                    yield return update;
                }
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
            }
        }
        """;
}

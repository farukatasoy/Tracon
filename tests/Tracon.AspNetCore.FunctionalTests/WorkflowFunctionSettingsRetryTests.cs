using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A function node that takes settings and a retry policy (F-295): every attempt sees the same settings,
/// and the run still completes.
/// </summary>
/// <remarks>
/// The retry wrapper is built around the handler that already holds the settings. A wrapper built BEFORE
/// the settings were bound would call the handler with whatever it had at registration time - nothing - so this
/// runs the real registration, the real classifier and a real run over HTTP.
/// </remarks>
public sealed class WorkflowFunctionSettingsRetryTests
{
    [Fact]
    public async Task A_retried_node_calls_the_function_again_with_the_settings_it_started_with()
    {
        var attempts = new ConcurrentQueue<TtsSettings>();

        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
                "flaky",
                _ => (_, settings, _, _) =>
                {
                    attempts.Enqueue(settings);

                    return attempts.Count < 3
                        ? throw new TraconProviderUnavailableException("the speech provider is down")
                        : Say(settings.Describe());
                },
                options => options.RetryPolicy = new WorkflowNodeRetryPolicy
                {
                    MaxAttempts = 5,
                    InitialDelay = TimeSpan.FromMilliseconds(1),
                }));

        await SaveOkAsync(host, "narrated", Chain("""{"voice":"nova","loudnessLufs":-20}""", "flaky"));

        var run = await RunAsync(host, "narrated");

        run.Record.Status.ShouldBe(RunStatus.Completed, run.Record.Error?.Message);
        run.Output.ShouldBe("voice=nova;lufs=-20;speed=Normal");

        attempts.Count.ShouldBe(3);
        attempts.Select(static settings => settings.Describe()).Distinct(StringComparer.Ordinal).ShouldBe(["voice=nova;lufs=-20;speed=Normal"]);
        attempts.Distinct().Count().ShouldBe(1, "every attempt must receive the SAME settings object");
    }
}

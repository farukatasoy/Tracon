using Microsoft.Extensions.AI;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Settings are read when a run starts, so two runs that overlap each keep the settings of the
/// definition they started from (F-295).
/// </summary>
public sealed class WorkflowFunctionSettingsConcurrencyTests
{
    [Fact]
    public async Task A_save_made_while_a_run_is_inside_the_function_is_for_the_next_run_only()
    {
        // 8: an executor instance and its settings object belong to one compile, never to two runs.
        var firstInside = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
                "gate",
                _ => async (_, settings, _, cancellationToken) =>
                {
                    if (string.Equals(settings.Voice, "alloy", StringComparison.Ordinal))
                    {
                        firstInside.TrySetResult();
                        await releaseFirst.Task.WaitAsync(cancellationToken);
                    }

                    return [new ChatMessage(ChatRole.User, $"voice={settings.Voice}")];
                }));

        await SaveOkAsync(host, "narrated", Chain("""{"voice":"alloy"}""", "gate"));

        var inFlight = RunAsync(host, "narrated", "one");

        await firstInside.Task.WaitAsync(Patience);

        // Saved while the first run waits INSIDE the function.
        await SaveOkAsync(host, "narrated", Chain("""{"voice":"nova"}""", "gate"));

        var second = await RunAsync(host, "narrated", "two").WaitAsync(Patience);

        releaseFirst.SetResult();

        var first = await inFlight.WaitAsync(Patience);

        first.Record.Status.ShouldBe(RunStatus.Completed, first.Record.Error?.Message);
        second.Record.Status.ShouldBe(RunStatus.Completed, second.Record.Error?.Message);
        first.Output.ShouldBe("voice=alloy");
        second.Output.ShouldBe("voice=nova");
    }

    [Fact]
    public async Task Runs_of_two_workflows_that_share_a_function_do_not_trade_settings()
    {
        var bothInside = new CountdownEvent(2);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await StartAsync(builder => builder
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, TtsSettings>(
                "gate",
                _ => async (_, settings, _, cancellationToken) =>
                {
                    bothInside.Signal();

                    // Both runs are inside the SAME registered function at the same time.
                    await release.Task.WaitAsync(cancellationToken);

                    return [new ChatMessage(ChatRole.User, $"voice={settings.Voice}")];
                }));

        await SaveOkAsync(host, "host-a", Chain("""{"voice":"alloy"}""", "gate"));
        await SaveOkAsync(host, "host-b", Chain("""{"voice":"nova"}""", "gate"));

        var a = RunAsync(host, "host-a");
        var b = RunAsync(host, "host-b");

        await Task.Run(() => bothInside.Wait(Patience)).WaitAsync(Patience);
        bothInside.CurrentCount.ShouldBe(0, "the two runs were never inside the function together");

        release.SetResult();

        (await a.WaitAsync(Patience)).Output.ShouldBe("voice=alloy");
        (await b.WaitAsync(Patience)).Output.ShouldBe("voice=nova");
    }
}

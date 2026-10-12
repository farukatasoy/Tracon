using System.Net.Http.Json;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A resume compiles the CURRENT definition (F-295): it sees the new settings, and an old checkpoint
/// stays usable because the settings are not part of the executor id.
/// </summary>
public sealed class WorkflowFunctionSettingsResumeTests
{
    [Fact]
    public async Task A_resume_after_a_settings_change_runs_with_the_new_settings_and_is_not_refused()
    {
        // 10: a settings change is not a structural change. If the settings leaked into the executor
        // id, every operator edit would strand the runs waiting to be resumed.
        await using var host = await StartAsync();

        await SaveOkAsync(host, "narrated", Chain("""{"voice":"alloy"}"""));

        var first = await RunAsync(host, "narrated");

        first.Output.ShouldBe("voice=alloy;lufs=-16;speed=Normal");

        var checkpoints = await Infrastructure.TraconTestHost.ReadJsonAsync(
            await host.Client.GetAsync($"/tracon/api/workflows/runs/{first.Record.Id}/checkpoints"));
        var earliest = checkpoints.EnumerateArray()
            .Select(static checkpoint => checkpoint.GetProperty("checkpointId").GetString()!)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToList();

        earliest.Count.ShouldBeGreaterThanOrEqualTo(2);

        await SaveOkAsync(host, "narrated", Chain("""{"voice":"nova"}"""));

        // The first checkpoint is from before the function ran, so the function runs again.
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{first.Record.Id}/resume",
            new WorkflowResumeHttpRequest { CheckpointId = await FirstCheckpointAsync(host, first.Record.Id) });

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var resumed = await ReadRunAsync(host, response);

        resumed.Record.Status.ShouldBe(RunStatus.Completed, resumed.Record.Error?.Message);
        resumed.Output.ShouldBe("voice=nova;lufs=-16;speed=Normal");
    }

    private static async Task<string> FirstCheckpointAsync(Infrastructure.TraconTestHost host, Guid runId)
    {
        var listed = await Infrastructure.TraconTestHost.ReadJsonAsync(
            await host.Client.GetAsync($"/tracon/api/workflows/runs/{runId}/checkpoints"));

        // The list is newest-first or oldest-first depending on the store; the one written before the
        // function ran is the one whose parent is null.
        return listed.EnumerateArray()
            .First(static checkpoint => checkpoint.GetProperty("parentCheckpointId").ValueKind == System.Text.Json.JsonValueKind.Null)
            .GetProperty("checkpointId").GetString()!;
    }
}

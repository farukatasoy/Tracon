using System.Globalization;
using Tracon.Capacity;

namespace Tracon.CapacityHost;

/// <summary>Writes the "full database" fixture the sweep compares against an empty one.</summary>
/// <remarks>
/// <para>
/// 🚨 The fixture is written through the PUBLIC store path - the same
/// <see cref="IRunStore"/> a consumer uses - not with hand-written SQL. A
/// hand-written seed would drift from what the product actually writes, and
/// the "empty vs full" comparison would then be a comparison against a shape
/// the product never produces.
/// </para>
/// <para>
/// Seeding time is deliberately outside every measured window: this mode runs
/// as its own process invocation and exits before the host that will be
/// measured is started.
/// </para>
/// </remarks>
public static class CapacitySeeder
{
    /// <summary>How many recorded deltas each seeded run carries.</summary>
    public const int EventsPerRun = 20;

    /// <summary>Writes the fixture and reports what it actually wrote.</summary>
    /// <param name="runs">The store to write through.</param>
    /// <param name="totalRuns">How many completed runs to write, split evenly across the two tenants.</param>
    /// <param name="cancellationToken">Cancels the seed.</param>
    /// <returns>How many runs and events were written.</returns>
    public static async Task<(long Runs, long Events)> SeedAsync(
        IRunStore runs,
        int totalRuns,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentOutOfRangeException.ThrowIfNegative(totalRuns);

        string[] tenants = [CapacityContract.TenantA, CapacityContract.TenantB];
        long writtenRuns = 0;
        long writtenEvents = 0;

        for (var index = 0; index < totalRuns; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tenant = tenants[index % tenants.Length];
            using var scope = AmbientTenantScope.Begin(tenant);

            var runId = Guid.NewGuid();
            var started = DateTimeOffset.UtcNow.AddMinutes(-totalRuns + index);
            var correlation = "seed-" + index.ToString(CultureInfo.InvariantCulture);

            await runs.StartRunAsync(
                new RunStartInfo
                {
                    RunId = runId,
                    AgentName = CapacityContract.AgentName,
                    StartedAt = started,
                    TenantId = tenant,
                    Status = RunStatus.Running,
                    IsStreaming = index % 2 == 0,
                    ModelId = CapacityContract.ModelName,
                    ModelProvider = CapacityContract.ProviderName,
                },
                cancellationToken).ConfigureAwait(false);

            for (var sequence = 0; sequence < EventsPerRun; sequence++)
            {
                await runs.AppendEventAsync(
                    new RunEvent
                    {
                        RunId = runId,
                        Sequence = sequence,
                        Type = RunEventType.MessageDelta,
                        Timestamp = started.AddMilliseconds(sequence),
                        Text = CapacityPayload.AnswerChunks(correlation, 1, 256)[0],
                        TenantId = tenant,
                    },
                    cancellationToken).ConfigureAwait(false);

                writtenEvents++;
            }

            await runs.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = RunStatus.Completed,
                    CompletedAt = started.AddSeconds(2),
                    EventCount = EventsPerRun,
                },
                cancellationToken).ConfigureAwait(false);

            writtenRuns++;
        }

        return (writtenRuns, writtenEvents);
    }
}

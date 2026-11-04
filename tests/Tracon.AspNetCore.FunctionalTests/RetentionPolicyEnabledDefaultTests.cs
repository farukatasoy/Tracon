using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A retention policy saved without <c>enabled</c> is saved paused. A policy that deletes data, and deletes it for
/// good, is not on until the caller says so.
/// </summary>
/// <remarks>
/// The tests go through the endpoint and the queue: the request body is raw JSON (the typed request would write the
/// default itself), and the cleanup job runs against a store that counts what it is asked to delete.
/// </remarks>
public sealed class RetentionPolicyEnabledDefaultTests
{
    private static readonly Uri RunEventsPolicy = new("/tracon/api/retention/run_events", UriKind.Relative);
    private static readonly Uri Preview = new("/tracon/api/retention/preview?target=run_events", UriKind.Relative);
    private static readonly Uri Run = new("/tracon/api/retention/run?target=run_events", UriKind.Relative);
    private static readonly Uri History = new("/tracon/api/retention/history?target=run_events", UriKind.Relative);

    [Fact]
    public async Task A_policy_saved_without_enabled_is_paused_and_the_cleanup_deletes_nothing()
    {
        var store = new CountingRetentionStore();
        await using var host = await StartAsync(store);

        var saved = await SaveAsync(host, """{"maxAgeDays":30,"maxRows":100}""");

        saved.GetProperty("enabled").GetBoolean().ShouldBeFalse("the request did not say the policy is on");
        saved.GetProperty("maxAgeDays").GetInt32().ShouldBe(30, "the limits are kept, so the policy can be switched on");

        var preview = await PreviewAsync(host);

        preview.GetProperty("enabled").GetBoolean().ShouldBeFalse();
        preview.GetProperty("matchingRows").GetInt64().ShouldBe(0, "a paused policy matches nothing, although the store has rows to match");

        await RunCleanupAsync(host);

        store.DeleteCalls.ShouldBe(0, "a paused policy must not reach the delete statement");
        (await ReadHistoryAsync(host)).GetArrayLength().ShouldBe(0, "no cleanup run is recorded for a paused policy");
    }

    [Fact]
    public async Task A_policy_saved_with_enabled_true_is_on_and_the_cleanup_deletes()
    {
        var store = new CountingRetentionStore();
        await using var host = await StartAsync(store);

        var saved = await SaveAsync(host, """{"maxAgeDays":30,"enabled":true}""");

        saved.GetProperty("enabled").GetBoolean().ShouldBeTrue();

        var preview = await PreviewAsync(host);

        preview.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        preview.GetProperty("matchingRows").GetInt64().ShouldBe(CountingRetentionStore.RowsPastTheCutoff);

        await RunCleanupAsync(host);

        store.DeleteCalls.ShouldBeGreaterThan(0);
        (await ReadHistoryAsync(host)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task A_policy_saved_with_enabled_false_stays_paused()
    {
        var store = new CountingRetentionStore();
        await using var host = await StartAsync(store);

        var saved = await SaveAsync(host, """{"maxAgeDays":30,"enabled":false}""");

        saved.GetProperty("enabled").GetBoolean().ShouldBeFalse();

        await RunCleanupAsync(host);

        store.DeleteCalls.ShouldBe(0);
    }

    private static Task<TraconTestHost> StartAsync(CountingRetentionStore store)
        => TraconTestHost.StartAsync(
            configureTracon: static builder => builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20)),
            configureServices: services => services.AddSingleton<IRetentionStore>(store));

    private static async Task<System.Text.Json.JsonElement> SaveAsync(TraconTestHost host, string body)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await host.Client.PutAsync(RunEventsPolicy, content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private static async Task<System.Text.Json.JsonElement> PreviewAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(Preview, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await TraconTestHost.ReadJsonAsync(response))[0];
    }

    private static async Task<System.Text.Json.JsonElement> ReadHistoryAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(History, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private static async Task RunCleanupAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsync(Run, content: null, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var jobId = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("jobId").GetGuid();
        var job = await QueuedWorkflowHarness.WaitForJobAsync(host, jobId);

        job.Status.ShouldBe(JobStatus.Completed);
    }

    private sealed class CountingRetentionStore : IRetentionStore
    {
        public const long RowsPastTheCutoff = 7;

        private int _deleteCalls;

        public int DeleteCalls => Volatile.Read(ref _deleteCalls);

        public ValueTask<long> CountOlderThanAsync(
            string target,
            string? tenantId,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default)
            => new(RowsPastTheCutoff);

        public ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(
            string target,
            string? tenantId,
            DateTimeOffset cutoff,
            int batchSize,
            CancellationToken cancellationToken = default)
            => new((IReadOnlyList<ArchiveRow>)[]);

        public ValueTask<int> DeleteBatchAsync(
            string target,
            string? tenantId,
            DateTimeOffset cutoff,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _deleteCalls);

            return new(0);
        }

        public ValueTask<DateTimeOffset?> FindRowLimitCutoffAsync(
            string target,
            string? tenantId,
            long maxRows,
            CancellationToken cancellationToken = default)
            => new((DateTimeOffset?)null);
    }
}

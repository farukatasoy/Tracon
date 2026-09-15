using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Tests for the data subject export/erasure endpoints (Phase 64).</summary>
public sealed class DataSubjectEndpointTests
{
    private static readonly Uri ExportUri = new("/tracon/api/data-subjects/subject-1/export", UriKind.Relative);
    private static readonly Uri EraseUri = new("/tracon/api/data-subjects/subject-1", UriKind.Relative);

    [Fact]
    public async Task Export_returns_409_when_no_resolver_is_registered()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(ExportUri);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Erase_returns_409_when_no_resolver_is_registered()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.DeleteAsync(EraseUri);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Unknown_subjectId_resolves_to_an_empty_scope_and_erases_nothing()
    {
        // A resolver that has never heard of this subject returns an EMPTY
        // scope (not null, not an error) — the endpoint must still respond
        // 200 with an empty/zeroed result, not throw or leak another subject's rows.
        var store = new RecordingDataSubjectStore();

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton<IDataSubjectResolver>(new EmptyDataSubjectResolver());
                services.AddSingleton<IDataSubjectStore>(store);
            });

        using var response = await host.Client.DeleteAsync(
            new Uri("/tracon/api/data-subjects/unknown-subject?dryRun=false", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("rowsByTarget").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task Erase_without_dryRun_previews_and_deletes_nothing()
    {
        var store = new RecordingDataSubjectStore();

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services => RegisterFakes(services, store));

        using var response = await host.Client.DeleteAsync(EraseUri);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        store.PreviewCalls.ShouldBe(1);
        store.EraseCalls.ShouldBe(0);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Erase_with_dryRun_false_actually_erases()
    {
        var store = new RecordingDataSubjectStore();

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services => RegisterFakes(services, store));

        using var response = await host.Client.DeleteAsync(
            new Uri("/tracon/api/data-subjects/subject-1?dryRun=false", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        store.PreviewCalls.ShouldBe(0);
        store.EraseCalls.ShouldBe(1);

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetProperty("dryRun").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Erase_writes_an_audit_entry_carrying_the_subject_id()
    {
        var store = new RecordingDataSubjectStore();

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services => RegisterFakes(services, store));

        using var erase = await host.Client.DeleteAsync(
            new Uri("/tracon/api/data-subjects/subject-1?dryRun=false", UriKind.Relative));
        erase.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var audit = await host.Client.GetAsync(
            new Uri("/tracon/api/audit?entity=data-subject:subject-1", UriKind.Relative));
        var entries = await TraconTestHost.ReadJsonAsync(audit);

        entries.GetArrayLength().ShouldBe(1);
        entries[0].GetProperty("action").GetString().ShouldBe("data_subject.erase");
    }

    [Fact]
    public async Task Erase_fails_loudly_when_the_audit_write_fails()
    {
        // K-370: the store's beforeCommitAsync callback (which writes the audit
        // entry) is invoked from INSIDE EraseAsync; when IAuditLog itself is
        // unreachable, the erasure must not silently succeed. The in-process
        // test host has no exception-handler middleware, so the unhandled
        // exception surfaces by propagating out of SendAsync rather than as a
        // 500 response — the same way it would surface to a caller of
        // IDataSubjectStore.EraseAsync directly.
        var store = new RecordingDataSubjectStore();

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                RegisterFakes(services, store);
                services.AddSingleton<IAuditLog>(new ThrowingAuditLog());
            });

        // The refusal is the shared fail-closed policy's, so it names the refused
        // operation and keeps the store's own failure as the inner exception. Before
        // phase 171 this one site leaked the store's raw exception instead — the five
        // other fail-closed operations already wrapped it.
        var refusal = await Should.ThrowAsync<TraconException>(async () =>
            await host.Client.DeleteAsync(
                new Uri("/tracon/api/data-subjects/subject-1?dryRun=false", UriKind.Relative)));

        refusal.Message.ShouldBe(
            "The erasure of data subject 'subject-1' was not applied because it could not "
            + "be written to the audit trail.");
        refusal.InnerException.ShouldBeOfType<InvalidOperationException>();

        // The callback ran from inside EraseAsync, so the deletes were rolled back
        // rather than committed: the refusal reached the store, not just the caller.
        store.EraseCalls.ShouldBe(1);
        store.CommittedCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Missing_scope_gets_403()
    {
        await using var host = await TraconTestHost.StartAsync();

        var created = await ApiKeyEndpointTests.CreateKeyAsync(host, "reader", "AgentsRead");

        using var request = new HttpRequestMessage(HttpMethod.Delete, EraseUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextKey);

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static void RegisterFakes(IServiceCollection services, RecordingDataSubjectStore store)
    {
        services.AddSingleton<IDataSubjectResolver>(new FixedDataSubjectResolver());
        services.AddSingleton<IDataSubjectStore>(store);
    }

    private sealed class FixedDataSubjectResolver : IDataSubjectResolver
    {
        public ValueTask<DataSubjectScope> ResolveAsync(
            string subjectId,
            string tenantId,
            CancellationToken cancellationToken = default)
            => new(new DataSubjectScope { SessionIds = ["s1"], RunIds = [], ConversationIds = [] });
    }

    private sealed class EmptyDataSubjectResolver : IDataSubjectResolver
    {
        public ValueTask<DataSubjectScope> ResolveAsync(
            string subjectId,
            string tenantId,
            CancellationToken cancellationToken = default)
            => new(new DataSubjectScope { SessionIds = [], RunIds = [], ConversationIds = [] });
    }

    private sealed class RecordingDataSubjectStore : IDataSubjectStore
    {
        private static readonly IReadOnlyDictionary<string, int> Empty =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public int PreviewCalls { get; private set; }

        public int EraseCalls { get; private set; }

        public ValueTask<IReadOnlyDictionary<string, int>> PreviewAsync(
            string tenantId,
            DataSubjectScope scope,
            CancellationToken cancellationToken = default)
        {
            PreviewCalls++;

            return new ValueTask<IReadOnlyDictionary<string, int>>(Empty);
        }

        public ValueTask<DataSubjectExport> ExportAsync(
            string tenantId,
            DataSubjectScope scope,
            CancellationToken cancellationToken = default)
            => new(new DataSubjectExport { Json = "{}" });

        /// <summary>The number of erasures that got past beforeCommitAsync to their commit.</summary>
        public int CommittedCalls { get; private set; }

        public async ValueTask<IReadOnlyDictionary<string, int>> EraseAsync(
            string tenantId,
            DataSubjectScope scope,
            Func<IReadOnlyDictionary<string, int>, CancellationToken, ValueTask> beforeCommitAsync,
            CancellationToken cancellationToken = default)
        {
            EraseCalls++;

            // Stands in for the real store's transaction: a throwing callback rolls
            // the deletes back, so nothing past this line runs.
            await beforeCommitAsync(Empty, cancellationToken).ConfigureAwait(false);

            CommittedCalls++;

            return Empty;
        }
    }

    private sealed class ThrowingAuditLog : IAuditLog
    {
        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("audit store is unreachable");

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Array.Empty<AuditEntry>());

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by this test");
    }
}

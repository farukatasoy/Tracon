using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The data subject export and erasure answer <c>400 Agent compilation failed</c> when a session of the subject belongs to an
/// agent that cannot be compiled, and apply nothing.
/// </summary>
/// <remarks>
/// <para>
/// Finding the internal conversation of a session restores the session through its agent, so it resolves the agent from the
/// catalog. A tenant provider binding whose configuration key has no value, or an egress policy that no longer allows the
/// provider, makes that resolution throw; the run endpoints, the replay and the session branch answer <c>400</c> for it, and
/// these three answered <c>500</c>.
/// </para>
/// <para>
/// The answer is an error and not a skip: an erasure that skipped the session would delete the rest of the subject's rows
/// and leave the conversation behind, and say <c>200</c>. Nothing is read, nothing is deleted and nothing is written to the
/// audit trail until the agent can be compiled.
/// </para>
/// </remarks>
public sealed class DataSubjectUncompilableAgentTests
{
    private const string Doomed = "doomed-agent";

    private const string Healthy = "healthy-agent";

    [Theory]
    [InlineData("GET", "data-subjects/subject/export")]
    [InlineData("DELETE", "data-subjects/subject")]
    [InlineData("DELETE", "data-subjects/subject?dryRun=false")]
    public async Task A_session_of_an_agent_that_cannot_be_compiled_is_a_400_and_nothing_is_applied(string method, string path)
    {
        var store = new CountingDataSubjectStore();
        await using var host = await StartAsync(store);
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        await SaveSessionAsync(host, "doomed-session", Doomed, tenant);
        await BreakBindingAsync(host, "doomed-provider", tenant);

        using var response = await host.Client.SendAsync(new HttpRequestMessage(
            new HttpMethod(method),
            new Uri($"/tracon/api/{path}", UriKind.Relative)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var problem = await TraconTestHost.ReadJsonAsync(response);
        problem.GetProperty("title").GetString().ShouldBe("Agent compilation failed");
        (problem.GetProperty("detail").GetString() ?? string.Empty).ShouldContain("Tracon:ProviderKeys:missing-on-purpose");

        store.ExportCalls.ShouldBe(0);
        store.PreviewCalls.ShouldBe(0);
        store.EraseCalls.ShouldBe(0);
        (await host.Services.GetRequiredService<IAuditLog>().QueryAsync(new AuditQuery { TenantId = tenant, Action = "data_subject.erase" }))
            .ShouldBeEmpty();
        (await host.Services.GetRequiredService<ISessionStore>().GetAsync("doomed-session")).ShouldNotBeNull();
    }

    /// <summary>The control: the same session on an agent that compiles is exported and erased as before.</summary>
    [Fact]
    public async Task A_session_of_an_agent_that_compiles_is_exported_and_erased()
    {
        var store = new CountingDataSubjectStore();
        await using var host = await StartAsync(store);
        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;

        await SaveSessionAsync(host, "healthy-session", Healthy, tenant);
        await BreakBindingAsync(host, "doomed-provider", tenant);

        using var export = await host.Client.GetAsync(new Uri("/tracon/api/data-subjects/subject/export", UriKind.Relative));
        using var erase = await host.Client.DeleteAsync(new Uri("/tracon/api/data-subjects/subject?dryRun=false", UriKind.Relative));

        export.StatusCode.ShouldBe(HttpStatusCode.OK);
        erase.StatusCode.ShouldBe(HttpStatusCode.OK, await erase.Content.ReadAsStringAsync());
        store.ExportCalls.ShouldBe(1);
        store.EraseCalls.ShouldBe(1);
    }

    private static Task<TraconTestHost> StartAsync(CountingDataSubjectStore store)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(new FakeModelProvider("doomed-provider").ForModel("m", cfg => cfg.EchoesUserMessage()))
                .AddModelProvider(new FakeModelProvider("healthy-provider").ForModel("m", cfg => cfg.EchoesUserMessage()))
                .AddAgent(TestData.Definition(Doomed) with { Model = new ModelBinding { Provider = "doomed-provider", Model = "m" } })
                .AddAgent(TestData.Definition(Healthy) with { Model = new ModelBinding { Provider = "healthy-provider", Model = "m" } }),
            configureServices: services =>
            {
                services.AddSingleton<IDataSubjectResolver>(new SessionsOfTheSubjectResolver());
                services.AddSingleton<IDataSubjectStore>(store);
            });

    private static async Task SaveSessionAsync(TraconTestHost host, string id, string agentName, string tenant)
        => await host.Services.GetRequiredService<ISessionStore>().SaveAsync(new SessionRecord
        {
            Id = id,
            AgentName = agentName,
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
        });

    private static async Task BreakBindingAsync(TraconTestHost host, string provider, string tenant)
        => await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = tenant,
                ProviderName = provider,
                ApiKeyConfigurationName = "Tracon:ProviderKeys:missing-on-purpose",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

    private sealed class SessionsOfTheSubjectResolver : IDataSubjectResolver
    {
        public ValueTask<DataSubjectScope> ResolveAsync(string subjectId, string tenantId, CancellationToken cancellationToken = default)
            => new(new DataSubjectScope { SessionIds = ["doomed-session", "healthy-session"], RunIds = [], ConversationIds = [] });
    }

    private sealed class CountingDataSubjectStore : IDataSubjectStore
    {
        public int ExportCalls { get; private set; }

        public int PreviewCalls { get; private set; }

        public int EraseCalls { get; private set; }

        public ValueTask<DataSubjectExport> ExportAsync(string tenantId, DataSubjectScope scope, CancellationToken cancellationToken = default)
        {
            ExportCalls++;

            return new(new DataSubjectExport { Json = "{}" });
        }

        public ValueTask<IReadOnlyDictionary<string, int>> PreviewAsync(string tenantId, DataSubjectScope scope, CancellationToken cancellationToken = default)
        {
            PreviewCalls++;

            return new(new Dictionary<string, int>(StringComparer.Ordinal));
        }

        public async ValueTask<IReadOnlyDictionary<string, int>> EraseAsync(
            string tenantId,
            DataSubjectScope scope,
            Func<IReadOnlyDictionary<string, int>, CancellationToken, ValueTask> beforeCommitAsync,
            CancellationToken cancellationToken = default)
        {
            EraseCalls++;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            await beforeCommitAsync(counts, cancellationToken);

            return counts;
        }
    }
}

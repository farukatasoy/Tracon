using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

// MAAI001: the session store types are marked "evaluation purposes only"; this
// file pins the bridge's delete against them (Phase 255).
#pragma warning disable MAAI001

/// <summary>
/// Phase 255 (F-746): every path that deletes a session deletes the same things —
/// the session record, the copy of its content that conversation memory keeps,
/// and its attachments.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Three callers share one internal deletion: <c>DELETE /api/sessions/{id}</c>,
/// <c>DELETE /v1/conversations/{id}</c> and the bridge Microsoft Agent Framework 1.26
/// asks for (<c>AgentSessionStore.DeleteSessionAsync</c>). Before this phase the
/// conversation endpoint left the attachments behind: the session was gone, so no
/// later call could reach them.
/// </para>
/// <para>
/// The order is copy, attachments, record. A step that fails leaves the record in
/// place, so a repeat call finds the session and finishes. The other order ends in
/// bytes nobody can name any more.
/// </para>
/// </remarks>
public sealed class SessionDeletionPathTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string TenantAlfa = "tenant-alfa";
    private const string TenantBeta = "tenant-beta";

    private static readonly Uri Conversations = new("/tracon/v1/conversations", UriKind.Relative);
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    public static TheoryData<string> DeleteEndpoints => new()
    {
        "/tracon/v1/conversations/{0}",
        "/tracon/api/sessions/{0}",
    };

    [Fact]
    public async Task Deleting_a_conversation_deletes_the_attachments_of_its_session()
    {
        await using var host = await StartAsync();
        var conversationId = await CreateAndRunAsync(host);
        var attachmentId = await UploadAsync(host, conversationId);

        using (var deleted = await host.Client.DeleteAsync(
            new Uri($"/tracon/v1/conversations/{conversationId}", UriKind.Relative),
            TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await TraconTestHost.ReadJsonAsync(deleted)).GetProperty("deleted").GetBoolean().ShouldBeTrue();
        }

        await AssertAttachmentGoneAsync(host, attachmentId);
    }

    [Fact]
    public async Task Deleting_a_conversation_keeps_the_attachments_of_another_session()
    {
        await using var host = await StartAsync();
        var conversationId = await CreateAndRunAsync(host);
        var other = await UploadAsync(host, "another-session");
        var unattached = await UploadAsync(host, sessionId: null);

        using var deleted = await host.Client.DeleteAsync(
            new Uri($"/tracon/v1/conversations/{conversationId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        deleted.EnsureSuccessStatusCode();
        (await StatusOfAsync(host, other)).ShouldBe(HttpStatusCode.OK);
        (await StatusOfAsync(host, unattached)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_conversation_that_was_never_used_has_nothing_to_delete_and_its_attachments_stay()
    {
        await using var host = await StartAsync();
        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }, TestContext.Current.CancellationToken))
        {
            created.EnsureSuccessStatusCode();
            conversationId = (await TraconTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        var attachmentId = await UploadAsync(host, conversationId);

        using var deleted = await host.Client.DeleteAsync(
            new Uri($"/tracon/v1/conversations/{conversationId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        deleted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(deleted)).GetProperty("deleted").GetBoolean().ShouldBeFalse();
        (await StatusOfAsync(host, attachmentId)).ShouldBe(HttpStatusCode.OK, "no record means no side effect, as on DELETE /api/sessions");
    }

    [Theory]
    [MemberData(nameof(DeleteEndpoints))]
    public async Task A_denied_delete_leaves_the_session_and_its_attachments(string route)
    {
        var handler = new DenyingHandler();

        await using var host = await StartAsync(configureServices: services =>
            services.AddSingleton<IRunAuthorizationHandler>(handler));
        var conversationId = await CreateAndRunAsync(host);
        var attachmentId = await UploadAsync(host, conversationId);

        handler.DenyDelete = true;

        using var response = await host.Client.DeleteAsync(
            new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, route, conversationId), UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeFalse();
        handler.DeleteRequests.ShouldBeGreaterThan(0, "the gate must have been asked");
        await AssertSessionExistsAsync(host, conversationId);
        (await StatusOfAsync(host, attachmentId)).ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [MemberData(nameof(DeleteEndpoints))]
    public async Task A_copy_that_cannot_be_erased_leaves_neither_the_session_nor_its_attachments_deleted(string route)
    {
        var eraser = new FailingEraser();

        await using var host = await StartAsync(configureServices: services =>
            services.AddSingleton<ISubjectContentEraser>(eraser));
        var conversationId = await CreateAndRunAsync(host);
        var attachmentId = await UploadAsync(host, conversationId);
        var uri = new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, route, conversationId), UriKind.Relative);

        eraser.Fail = true;

        using (var failed = await DeleteAllowingServerErrorAsync(host, uri))
        {
            failed?.IsSuccessStatusCode.ShouldNotBe(true);
        }

        await AssertSessionExistsAsync(host, conversationId);
        (await StatusOfAsync(host, attachmentId)).ShouldBe(HttpStatusCode.OK, "a half deletion leaves bytes of a session that is still there");

        eraser.Fail = false;

        using var repeated = await host.Client.DeleteAsync(uri, TestContext.Current.CancellationToken);

        repeated.IsSuccessStatusCode.ShouldBeTrue("a repeat call finds the session and finishes");
        await AssertAttachmentGoneAsync(host, attachmentId);
    }

    [Fact]
    public async Task Deleting_a_session_in_one_tenant_leaves_the_same_id_in_another_tenant()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition())
            .UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        const string sessionId = "shared-id";

        await SeedSessionAsync(host, TenantAlfa, sessionId);
        await SeedSessionAsync(host, TenantBeta, sessionId);
        var alfa = await UploadAsync(host, sessionId, TenantAlfa);
        var beta = await UploadAsync(host, sessionId, TenantBeta);

        using (var deleted = await host.Client.SendAsync(
            Request(HttpMethod.Delete, $"/tracon/api/sessions/{sessionId}", TenantAlfa),
            TestContext.Current.CancellationToken))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await StatusOfAsync(host, alfa, TenantAlfa)).ShouldBe(HttpStatusCode.NotFound);
        (await StatusOfAsync(host, beta, TenantBeta)).ShouldBe(HttpStatusCode.OK, "tenant-beta's attachment is not tenant-alfa's to delete");

        using var betaSession = await host.Client.SendAsync(
            Request(HttpMethod.Get, $"/tracon/api/sessions/{sessionId}", TenantBeta),
            TestContext.Current.CancellationToken);

        betaSession.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_bridge_deletes_the_session_and_its_attachments_and_leaves_the_others()
    {
        await using var host = await StartAsync();
        var (store, agent) = ResolveBridge(host);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new AgentSessionStoreKey("bridge-1");

        await OpenAsync(store, agent, key);

        var attachmentId = await UploadAsync(host, "bridge-1");
        var bystander = await UploadAsync(host, "bridge-2");

        await store.DeleteSessionAsync(agent, key, cancellationToken);

        (await store.GetSessionAsync(agent, key, cancellationToken)).ShouldBeNull();
        await AssertAttachmentGoneAsync(host, attachmentId);
        (await StatusOfAsync(host, bystander)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_bridge_treats_a_session_that_does_not_exist_as_a_no_op()
    {
        // The framework's contract: "a session that does not exist" is not an error.
        await using var host = await StartAsync();
        var (store, agent) = ResolveBridge(host);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new AgentSessionStoreKey("never-opened");
        var attachmentId = await UploadAsync(host, "never-opened");

        await store.DeleteSessionAsync(agent, key, cancellationToken);
        await store.DeleteSessionAsync(agent, key, cancellationToken);

        (await StatusOfAsync(host, attachmentId)).ShouldBe(HttpStatusCode.OK, "no record means no side effect");
    }

    [Fact]
    public async Task The_bridge_deletes_in_the_current_tenant_only()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition())
            .UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));
        var (store, agent) = ResolveBridge(host);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new AgentSessionStoreKey("shared-id");

        using (AmbientTenantScope.Begin(TenantAlfa))
        {
            await OpenAsync(store, agent, key);
        }

        using (AmbientTenantScope.Begin(TenantBeta))
        {
            await OpenAsync(store, agent, key);
        }

        var alfa = await UploadAsync(host, "shared-id", TenantAlfa);
        var beta = await UploadAsync(host, "shared-id", TenantBeta);

        using (AmbientTenantScope.Begin(TenantAlfa))
        {
            await store.DeleteSessionAsync(agent, key, cancellationToken);

            (await store.GetSessionAsync(agent, key, cancellationToken)).ShouldBeNull();
        }

        using (AmbientTenantScope.Begin(TenantBeta))
        {
            (await store.GetSessionAsync(agent, key, cancellationToken)).ShouldNotBeNull("tenant-alfa's delete is not tenant-beta's");
        }

        (await StatusOfAsync(host, alfa, TenantAlfa)).ShouldBe(HttpStatusCode.NotFound);
        (await StatusOfAsync(host, beta, TenantBeta)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_copy_that_cannot_be_erased_leaves_the_bridge_delete_unfinished_until_a_repeat_call()
    {
        var eraser = new FailingEraser();

        await using var host = await StartAsync(configureServices: services =>
            services.AddSingleton<ISubjectContentEraser>(eraser));
        var (store, agent) = ResolveBridge(host);
        var cancellationToken = TestContext.Current.CancellationToken;
        var key = new AgentSessionStoreKey("bridge-1");

        await OpenAsync(store, agent, key);

        var attachmentId = await UploadAsync(host, "bridge-1");

        eraser.Fail = true;

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await store.DeleteSessionAsync(agent, key, cancellationToken));

        (await store.GetSessionAsync(agent, key, cancellationToken)).ShouldNotBeNull();
        (await StatusOfAsync(host, attachmentId)).ShouldBe(HttpStatusCode.OK);

        eraser.Fail = false;

        await store.DeleteSessionAsync(agent, key, cancellationToken);

        (await store.GetSessionAsync(agent, key, cancellationToken)).ShouldBeNull();
        await AssertAttachmentGoneAsync(host, attachmentId);
    }

    [Fact]
    public async Task The_bridge_refuses_an_empty_session_id()
    {
        await using var host = await StartAsync();
        var (store, agent) = ResolveBridge(host);

        await Should.ThrowAsync<ArgumentException>(async () => await store.DeleteSessionAsync(
            agent, new AgentSessionStoreKey(" "), TestContext.Current.CancellationToken));
    }

    private static (TraconAgentSessionStore Store, AIAgent Agent) ResolveBridge(TraconTestHost host)
    {
        var agent = host.Services.GetRequiredService<AgentDefinitionCompiler>().Compile(TestData.Definition());
        var store = new TraconAgentSessionStore(
            host.Services.GetRequiredService<AgentSessionManager>(),
            host.Services.GetRequiredService<SessionDeletion>());

        return (store, agent);
    }

    private static async Task OpenAsync(TraconAgentSessionStore store, AIAgent agent, AgentSessionStoreKey key)
    {
        var session = await store.GetOrCreateSessionAsync(agent, key, TestContext.Current.CancellationToken);

        await store.SaveSessionAsync(agent, key, session, TestContext.Current.CancellationToken);
    }

    private static Task<TraconTestHost> StartAsync(Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: configureServices);

    private static async Task<string> CreateAndRunAsync(TraconTestHost host)
    {
        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }, TestContext.Current.CancellationToken))
        {
            created.EnsureSuccessStatusCode();
            conversationId = (await TraconTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var run = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", conversation = conversationId, input = "hello" },
            TestContext.Current.CancellationToken);

        run.EnsureSuccessStatusCode();

        return conversationId;
    }

    private static async Task SeedSessionAsync(TraconTestHost host, string tenant, string sessionId)
    {
        using var scope = AmbientTenantScope.Begin(tenant);
        var sessions = host.Services.GetRequiredService<ISessionStore>();

        await sessions.SaveAsync(
            new SessionRecord
            {
                Id = sessionId,
                AgentName = "kod-agent",
                TenantId = tenant,
                State = JsonDocument.Parse("{}").RootElement,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task<Guid> UploadAsync(TraconTestHost host, string? sessionId, string? tenant = null)
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01]), "file", "test.png" },
        };

        var uri = sessionId is null
            ? "/tracon/api/attachments"
            : $"/tracon/api/attachments?sessionId={Uri.EscapeDataString(sessionId)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(uri, UriKind.Relative)) { Content = content };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<HttpStatusCode> StatusOfAsync(TraconTestHost host, Guid attachmentId, string? tenant = null)
    {
        using var request = Request(HttpMethod.Get, $"/tracon/api/attachments/{attachmentId}", tenant);
        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }

    private static async Task AssertAttachmentGoneAsync(TraconTestHost host, Guid attachmentId)
        => (await StatusOfAsync(host, attachmentId))
            .ShouldBe(HttpStatusCode.NotFound, "the attachment must go with its session (F-746)");

    private static async Task AssertSessionExistsAsync(TraconTestHost host, string sessionId)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/sessions/{sessionId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>A failing store surfaces as an exception on the test server; the status is then not observable.</summary>
    private static async Task<HttpResponseMessage?> DeleteAllowingServerErrorAsync(TraconTestHost host, Uri uri)
    {
        try
        {
            return await host.Client.DeleteAsync(uri, TestContext.Current.CancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? tenant)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        return request;
    }

    /// <summary>Allows every request until <see cref="DenyDelete"/> is set, then refuses session deletes.</summary>
    private sealed class DenyingHandler : IRunAuthorizationHandler
    {
        private int _deleteRequests;

        public bool DenyDelete { get; set; }

        public int DeleteRequests => Volatile.Read(ref _deleteRequests);

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
            SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Access != SessionAccess.Delete)
            {
                return ValueTask.FromResult(RunAuthorizationResult.Allow());
            }

            Interlocked.Increment(ref _deleteRequests);

            return ValueTask.FromResult(DenyDelete ? RunAuthorizationResult.Deny("no") : RunAuthorizationResult.Allow());
        }
    }

    /// <summary>A store of the host's own that holds a copy of a session's content and fails on demand.</summary>
    private sealed class FailingEraser : ISubjectContentEraser
    {
        public bool Fail { get; set; }

        public string Target => "failing_copy";

        public ValueTask<int> CountAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
            => ValueTask.FromResult(0);

        public ValueTask<int> EraseAsync(string tenantId, IReadOnlyList<string> sessionIds, CancellationToken cancellationToken)
            => Fail
                ? throw new InvalidOperationException("The copy store is down.")
                : ValueTask.FromResult(0);

        public ValueTask WriteExportAsync(
            string tenantId, IReadOnlyList<string> sessionIds, Utf8JsonWriter writer, CancellationToken cancellationToken)
        {
            writer.WriteStartArray(Target);
            writer.WriteEndArray();

            return ValueTask.CompletedTask;
        }
    }
}
#pragma warning restore MAAI001

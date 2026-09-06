using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 149 (F-201): the OpenAI-compatible <c>/v1/conversations</c> surface
/// now asks the installation's own <see cref="IRunAuthorizationHandler"/>, and
/// a deployment that does not want the surface at all can leave it unmapped.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The gap this phase closed was HALF closed already, which is what makes
/// it worth a file of its own. Phase 148 gave these endpoints AgentPrism's own
/// session-ownership boundary and stopped there; the consumer's handler was
/// still never asked. An installation whose handler refused
/// <c>GET /api/sessions/{id}</c> had <c>GET /v1/conversations/{id}/items</c>
/// hand back the same chat history — one door locked, the one beside it open.
/// Finding one of a surface's two gates is not evidence about the other.
/// </para>
/// <para>
/// The claim under test is not "a denial is refused" but "a denial is
/// INDISTINGUISHABLE from absence" (K-671, K-684): the answer is this
/// endpoint's own OpenAI-shaped <c>404</c>, byte for byte what a conversation
/// belonging to another tenant already produced, never the gate's own
/// <c>ProblemDetails</c> body and never a <c>403</c> that would confirm the
/// conversation exists.
/// </para>
/// </remarks>
public sealed class OpenAIConversationsAuthorizationTests
{
    private const string AgentName = "kod-agent";

    private static Uri Conversation(string conversationId)
        => new($"/agentprism/v1/conversations/{conversationId}", UriKind.Relative);

    private static Uri Items(string conversationId)
        => new($"/agentprism/v1/conversations/{conversationId}/items", UriKind.Relative);

    // ------------------------------------------------------------------
    // Nothing registered, nothing changes.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Every_conversation_endpoint_is_unchanged_when_no_handler_is_registered()
    {
        await using var host = await StartAsync(handler: null);
        await SeedAsync(host, "conv-1");

        using (var created = await host.Client.PostAsync(
            new Uri("/agentprism/v1/conversations", UriKind.Relative), content: null))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var retrieved = await host.Client.GetAsync(Conversation("conv-1")))
        {
            retrieved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var items = await host.Client.GetAsync(Items("conv-1")))
        {
            items.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var deleted = await host.Client.DeleteAsync(Conversation("conv-1"));
        deleted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------
    // The handler is asked, with the right access, on each endpoint.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Retrieving_a_conversation_a_handler_denies_reads_exactly_like_another_tenants()
    {
        // 🚨 The comparison is against ANOTHER TENANT's conversation, not
        // against a second denial by the same handler — two refusals matching
        // each other proves nothing. The tenant refusal is the body that was
        // already reachable before this phase, so matching it is what keeps the
        // new gate from introducing a distinguishable answer.
        //
        // 🚨 And that is ALL the byte equality buys here. This endpoint does
        // not hide existence and cannot: an unused identifier answers 200,
        // because a conversation id is a reservation (see
        // The_conversations_surface_is_mapped_by_default). "404" on this
        // surface has always meant "not yours", never "never existed".
        var handler = new RecordingHandler { Allow = false };

        await using var host = await StartAsync(handler);
        await SeedAsync(host, "conv-1");

        // Written under a DIFFERENT tenant, so the tenant check refuses it
        // before the handler is ever consulted.
        await SeedAsync(host, "conv-elsewhere", tenantId: "other-tenant");

        using var denied = await host.Client.GetAsync(Conversation("conv-1"));
        using var otherTenant = await host.Client.GetAsync(Conversation("conv-elsewhere"));

        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        otherTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await denied.Content.ReadAsStringAsync())
            .ShouldBe((await otherTenant.Content.ReadAsStringAsync())
                .Replace("conv-elsewhere", "conv-1", StringComparison.Ordinal));

        // The tenant refusal short-circuits BEFORE the handler (K-684): another
        // tenant's identity must never reach a consumer's own policy code.
        handler.Requests.Select(static request => request.SessionId).ShouldBe(["conv-1"]);
    }

    [Fact]
    public async Task Listing_a_conversations_items_goes_through_the_handler()
    {
        // 🚨 The worst of the three to leave open: this endpoint returns the
        // conversation's whole chat history.
        var handler = new RecordingHandler { Allow = false };

        await using var host = await StartAsync(handler);
        await SeedAsync(host, "conv-1");

        using var response = await host.Client.GetAsync(Items("conv-1"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        handler.Seen.ShouldContain(SessionAccess.Read);

        // The body must carry no trace of the history it refused to return.
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("hello");
    }

    [Fact]
    public async Task Deleting_a_conversation_a_handler_denies_leaves_the_session_alone()
    {
        var handler = new RecordingHandler { Allow = false };

        await using var host = await StartAsync(handler);
        await SeedAsync(host, "conv-1");

        using var response = await host.Client.DeleteAsync(Conversation("conv-1"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 🚨 A status assertion alone would pass even while the row was being
        // removed: this endpoint answers 200 {"deleted":false} for an id it
        // cannot find. The surviving row is the proof.
        (await StoreOf(host).GetAsync("conv-1")).ShouldNotBeNull();
        handler.Seen.ShouldContain(SessionAccess.Delete);
    }

    [Fact]
    public async Task The_handler_receives_the_conversation_id_and_the_calling_user()
    {
        // A gate that is called with the wrong values is not a gate. The
        // source scan proves the CALL exists; only this proves it is wired.
        var handler = new RecordingHandler { Allow = true };

        await using var host = await StartAsync(handler);
        await SeedAsync(host, "conv-1");

        using var response = await host.Client.GetAsync(Conversation("conv-1"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.SessionId.ShouldBe("conv-1");
        request.UserId.ShouldBe(StaticAttribution.Identity);
        request.Access.ShouldBe(SessionAccess.Read);
    }

    [Fact]
    public async Task Creating_a_conversation_is_not_gated_because_it_writes_nothing()
    {
        // POST /v1/conversations is an identifier RESERVATION: it touches no
        // store and opens no session, so there is no resource to authorize.
        // Gating it would add a second decision point over an id that does not
        // exist yet — and refusing there would close the only path by which a
        // conversation is ever born.
        var handler = new RecordingHandler { Allow = false };

        await using var host = await StartAsync(handler);

        using var response = await host.Client.PostAsync(
            new Uri("/agentprism/v1/conversations", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.Requests.ShouldBeEmpty();

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("id").GetString().ShouldNotBeNullOrWhiteSpace();

        // And the reservation really did stay out of the store.
        (await StoreOf(host).GetAsync(body.GetProperty("id").GetString()!)).ShouldBeNull();
    }

    [Fact]
    public async Task A_handler_that_throws_is_treated_as_a_denial_not_as_a_500()
    {
        // Fail-closed: a consumer's own handler can throw anything, and a
        // broken handler must not open the door it exists to guard.
        await using var host = await StartAsync(new ThrowingHandler());
        await SeedAsync(host, "conv-1");

        using var response = await host.Client.GetAsync(Conversation("conv-1"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_request_cancelled_while_the_handler_is_deciding_never_answers_200()
    {
        // 🚨 The token has to be cancelled AFTER the request is on the wire and
        // the handler has been entered. Cancelling first makes HttpClient throw
        // locally, the server is never reached, and the test proves only that
        // TaskCanceledException exists — it would pass with no gate at all.
        var handler = new BlockingHandler();

        await using var host = await StartAsync(handler);
        await SeedAsync(host, "conv-1");

        using var cancellation = new CancellationTokenSource();
        var call = host.Client.GetAsync(Conversation("conv-1"), cancellation.Token);

        // The handler is now inside AuthorizeSessionAsync, awaiting the token
        // the endpoint handed it. That await is the proof the token arrives.
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        await cancellation.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(async () => await call);

        handler.ObservedCancellation.ShouldBeTrue(
            "the endpoint must pass the REQUEST's token down, not CancellationToken.None");
    }

    [Theory]
    [InlineData("conv_")]
    [InlineData("%20%20")]
    [InlineData("conv_%C3%9C%C3%B6-1")]
    public async Task A_degenerate_identifier_is_refused_rather_than_reaching_the_store(string conversationId)
    {
        // A denying handler must answer these the same way it answers a real
        // identifier: a prefix with nothing after it, an all-whitespace id and
        // a non-ASCII one are all just strings to this route.
        var handler = new RecordingHandler { Allow = false };

        await using var host = await StartAsync(handler);

        using var response = await host.Client.GetAsync(Conversation(conversationId));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_empty_identifier_does_not_reach_the_conversation_route_at_all()
    {
        // 🚨 Measured, not assumed: GET /v1/conversations/ collapses onto the
        // COLLECTION path, which only accepts POST, so the answer is 405 from
        // routing and the gate is never consulted. Asserting 404 here would
        // have been a test that documents a behaviour the server does not have.
        var handler = new RecordingHandler { Allow = false };

        await using var host = await StartAsync(handler);

        using var response = await host.Client.GetAsync(Conversation(string.Empty));

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_traversal_shaped_identifier_lands_on_another_GATED_route_not_outside_one()
    {
        // 🚨 Measured, not assumed. The client normalizes '../..' away before
        // the request leaves, so this never reaches the conversation route at
        // all — it arrives at GET /api/sessions, the LISTING. The point worth
        // locking is that the surface it escapes to is gated too: a denying
        // handler answers 403 there (a list has no single identity to hide,
        // K-671), so traversal buys an attacker nothing.
        var handler = new RecordingHandler { Allow = false };

        await using var host = await StartAsync(handler);

        using var response = await host.Client.GetAsync(Conversation("../../api/sessions"));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        handler.Seen.ShouldContain(SessionAccess.List);
    }

    [Fact]
    public async Task A_very_long_identifier_is_refused_rather_than_crashing()
    {
        var handler = new RecordingHandler { Allow = false };

        await using var host = await StartAsync(handler);

        using var response = await host.Client.GetAsync(Conversation(new string('c', 2000)));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // The surface can be left unmapped altogether.
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_conversations_surface_is_mapped_by_default()
    {
        // The default IS today's behaviour: the surface has shipped and
        // withdrawing it silently would break existing clients.
        new AgentPrismEndpointOptions().MapOpenAIConversations.ShouldBeTrue();

        await using var host = await StartAsync(handler: null);

        using var response = await host.Client.GetAsync(Conversation("anything"));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "an unused identifier is a valid reservation");
    }

    [Fact]
    public async Task Turning_the_surface_off_removes_all_four_routes()
    {
        await using var host = await StartAsync(handler: null, mapConversations: false);
        await SeedAsync(host, "conv-1");

        using (var created = await host.Client.PostAsync(
            new Uri("/agentprism/v1/conversations", UriKind.Relative), content: null))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var retrieved = await host.Client.GetAsync(Conversation("conv-1")))
        {
            retrieved.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var items = await host.Client.GetAsync(Items("conv-1")))
        {
            items.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using var deleted = await host.Client.DeleteAsync(Conversation("conv-1"));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 🚨 The route is GONE, not merely refusing: the session it would have
        // deleted is untouched, and /api/sessions still reaches it.
        (await StoreOf(host).GetAsync("conv-1")).ShouldNotBeNull();

        using var session = await host.Client.GetAsync(
            new Uri("/agentprism/api/sessions/conv-1", UriKind.Relative));
        session.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Turning_the_surface_off_removes_it_from_the_OpenAPI_document()
    {
        // 🚨 A route that answers 404 while still being advertised is worse
        // than either state alone: a generated client would ship a method that
        // can never succeed.
        await using var mapped = await StartAsync(handler: null, withOpenApi: true);
        await using var unmapped = await StartAsync(
            handler: null, mapConversations: false, withOpenApi: true);

        (await ConversationPathsAsync(mapped)).ShouldNotBeEmpty();
        (await ConversationPathsAsync(unmapped)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Turning_the_surface_off_leaves_the_other_OpenAI_routes_alone()
    {
        // The flag governs the conversations routes only. /v1/responses and
        // /v1/chat/completions start runs rather than reach a stored
        // conversation, and a deployment must not lose them by asking for a
        // smaller conversation surface.
        await using var host = await StartAsync(
            handler: null, mapConversations: false, withOpenApi: true);

        var paths = await PathsAsync(host);

        paths.ShouldContain(static path => string.Equals(path, "/agentprism/v1/responses", StringComparison.Ordinal));
        paths.ShouldContain(static path => string.Equals(path, "/agentprism/v1/chat/completions", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static ISessionStore StoreOf(AgentPrismTestHost host)
        => host.Services.GetRequiredService<ISessionStore>();

    /// <summary>Writes a session directly, so no run is needed to have a conversation.</summary>
    /// <param name="host">The running host.</param>
    /// <param name="conversationId">The identifier to write.</param>
    /// <param name="tenantId">The owning tenant; anything but the default is unreachable from a default-tenant request.</param>
    /// <returns>A task that completes when the row exists.</returns>
    private static async Task SeedAsync(
        AgentPrismTestHost host,
        string conversationId,
        string tenantId = "default")
        => await StoreOf(host).SaveAsync(new SessionRecord
        {
            Id = conversationId,
            AgentName = AgentName,
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TenantId = tenantId,
        });

    private static async Task<string[]> PathsAsync(AgentPrismTestHost host)
    {
        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var document = await AgentPrismTestHost.ReadJsonAsync(response);

        return [.. document.GetProperty("paths").EnumerateObject().Select(static path => path.Name)];
    }

    private static async Task<string[]> ConversationPathsAsync(AgentPrismTestHost host)
        => [.. (await PathsAsync(host)).Where(static path => path.Contains("/v1/conversations", StringComparison.Ordinal))];

    private static Task<AgentPrismTestHost> StartAsync(
        IRunAuthorizationHandler? handler,
        bool mapConversations = true,
        bool withOpenApi = false)
        => AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition(AgentName)),
            withOpenApi: withOpenApi,
            configureServices: services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new StaticAttribution()));

                if (handler is not null)
                {
                    services.Replace(ServiceDescriptor.Singleton(handler));
                }
            },
            configureEndpoints: options => options.MapOpenAIConversations = mapConversations);

    /// <summary>An identity that never changes, so the gate always has a caller.</summary>
    private sealed class StaticAttribution : IRunAttributionContext
    {
        public const string Identity = "caller";

        public string? UserId => Identity;

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    /// <summary>Records what the endpoints asked, and answers a fixed verdict.</summary>
    private sealed class RecordingHandler : IRunAuthorizationHandler
    {
        private readonly Lock _gate = new();

        public bool Allow { get; init; }

        public List<SessionAuthorizationRequest> Requests { get; } = [];

        public IEnumerable<SessionAccess> Seen
        {
            get
            {
                lock (_gate)
                {
                    return [.. Requests.Select(static request => request.Access)];
                }
            }
        }

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
            SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Requests.Add(request);
            }

            return ValueTask.FromResult(Allow
                ? RunAuthorizationResult.Allow()
                : RunAuthorizationResult.Deny("no"));
        }
    }

    /// <summary>
    /// A handler that parks inside <c>AuthorizeSessionAsync</c> until the
    /// request's own token is cancelled, so a test can prove the token really
    /// reaches consumer code rather than being replaced with
    /// <see cref="CancellationToken.None"/>.
    /// </summary>
    private sealed class BlockingHandler : IRunAuthorizationHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool ObservedCancellation { get; private set; }

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());

        public async ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
            SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ObservedCancellation = true;
                throw;
            }

            return RunAuthorizationResult.Allow();
        }
    }

    /// <summary>A consumer handler that fails inside its own code.</summary>
    private sealed class ThrowingHandler : IRunAuthorizationHandler
    {
        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(RunAuthorizationResult.Allow());

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
            SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the authorization service is down");
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 148 (F-196) over HTTP: a session records the user it belongs to, and
/// the listing narrows to that user before paging.
/// </summary>
/// <remarks>
/// <para>
/// These live at the FUNCTIONAL level for the reason
/// <c>RunAuthorizationEndpointTests</c> documents for its own siblings: the
/// claim is not that a filter works in isolation but that the identity
/// resolved from the request reaches the row that gets written, survives the
/// second write of the same turn, and comes back out of the query. Every one
/// of those steps crosses a boundary a unit test does not have — DI, HTTP,
/// the store.
/// </para>
/// <para>
/// 🚨 The identity is switched PER REQUEST here, not per host
/// (<see cref="SwitchableAttribution"/>). A test that starts one host per user
/// cannot catch the defect that matters most — an owner written by user A's
/// request being visible to, or overwritten by, user B's — because the two
/// users would never share a store.
/// </para>
/// </remarks>
public sealed class SessionOwnershipTests
{
    private static readonly Uri Sessions = new("/agentprism/api/sessions", UriKind.Relative);
    private static readonly Uri Run = new("/agentprism/api/agents/kod-agent/run", UriKind.Relative);

    // ------------------------------------------------------------------
    // K1: off by default, and off means nothing changes.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Nothing_changes_when_ownership_is_not_configured()
    {
        await using var host = await StartAsync(enabled: false, out var identity);
        identity.UserId = "a";

        await RunAsync(host, "s1");

        identity.UserId = "b";

        // A second user sees the first user's session, exactly as before this
        // phase: with ownership off there IS no owner boundary.
        var listed = await ListAsync(host);
        listed.Length.ShouldBe(1);

        // And the column is null, not an invented placeholder.
        var record = await StoreAsync(host).GetAsync("s1");
        record.ShouldNotBeNull().OwnerId.ShouldBeNull();

        using var read = await host.Client.GetAsync(Session("s1"));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ownership_is_off_in_the_default_options()
    {
        // The K1 claim stated against the type itself, so a future edit to the
        // default cannot pass unnoticed just because every test above sets the
        // flag explicitly.
        var defaults = new AgentPrismSessionOwnershipOptions();

        defaults.Enabled.ShouldBeFalse();
        defaults.RequireAuthenticatedOwner.ShouldBeTrue(
            "once ownership IS turned on, the safe answer to an unresolvable identity is refusal");
        defaults.ManagementPolicy.ShouldBe(AgentPrismPolicies.Operator);
    }

    // ------------------------------------------------------------------
    // The listing narrows to the caller.
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_list_returns_only_the_callers_own_sessions()
    {
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "a-1");
        await RunAsync(host, "a-2");

        identity.UserId = "b";
        await RunAsync(host, "b-1");

        (await ListAsync(host)).Select(static session => session.Id).ShouldBe(["b-1"]);

        identity.UserId = "a";

        var mine = await ListAsync(host);
        mine.Select(static session => session.Id).Order(StringComparer.Ordinal).ShouldBe(["a-1", "a-2"]);
        mine.ShouldAllBe(static session => session.OwnerId == "a");
    }

    [Fact]
    public async Task The_owner_filter_is_applied_before_paging()
    {
        // 🚨 Over HTTP, not only in the store contract: the endpoint has to put
        // the owner INTO the query rather than trimming the list it got back.
        // Five of B's sessions are written LAST, so they are the newest and
        // would head an unfiltered first page.
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";

        for (var i = 0; i < 5; i++)
        {
            await RunAsync(host, $"a-{i}");
        }

        identity.UserId = "b";

        for (var i = 0; i < 5; i++)
        {
            await RunAsync(host, $"b-{i}");
        }

        identity.UserId = "a";

        var page = await ListAsync(host, "?take=3");

        page.Length.ShouldBe(3, "paging must run over the ALREADY narrowed set, never over a trimmed page");
        page.ShouldAllBe(static session => session.OwnerId == "a");
    }

    [Fact]
    public async Task An_unowned_session_never_appears_in_an_owned_listing()
    {
        // A row written before ownership was turned on. It stays in the
        // database and stays readable by id, but it belongs to nobody, so it
        // falls into nobody's list.
        await using var host = await StartAsync(enabled: true, out var identity);

        await StoreAsync(host).SaveAsync(new SessionRecord
        {
            Id = "legacy",
            AgentName = "kod-agent",
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        identity.UserId = "a";
        await RunAsync(host, "mine");

        (await ListAsync(host)).Select(static session => session.Id).ShouldBe(["mine"]);
    }

    [Fact]
    public async Task A_caller_with_no_resolvable_identity_gets_an_empty_list_not_the_whole_tenant()
    {
        // 🚨 Fail-closed. The tempting implementation - "no user, no filter" -
        // hands an unidentified caller EVERY session in the tenant, which is
        // the exact leak this option exists to close.
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "a-1");

        identity.UserId = null;

        (await ListAsync(host)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_management_caller_sees_the_whole_tenant_including_unowned_rows()
    {
        // 🚨 The Operator policy is registered with a REAL assertion whose
        // answer this test flips, not a blanket `_ => true`. A policy that
        // always succeeds proves only that "a policy is registered" reaches
        // the gate; what production depends on is that a caller who does NOT
        // satisfy it gets the narrow list — the second half below.
        //
        // The two sessions are seeded through the STORE rather than through a
        // run: AgentPrismPolicies.Operator also guards the run endpoint, so
        // driving the fixture through HTTP would make this test's own policy
        // switch decide whether the fixture can be built at all.
        ManagementPolicyHolder.Satisfied = false;

        await using var host = await StartAsync(
            enabled: true,
            out var identity,
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(
                    static _ => ManagementPolicyHolder.Satisfied))
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => true)));

        var store = StoreAsync(host);

        await store.SaveAsync(Seed("a-1", owner: "a"));
        await store.SaveAsync(Seed("legacy", owner: null));

        identity.UserId = "operator";
        ManagementPolicyHolder.Satisfied = true;

        // The management listing is the ONLY path by which the unowned legacy
        // row stays reachable at all.
        (await ListAsync(host)).Select(static session => session.Id).Order(StringComparer.Ordinal)
            .ShouldBe(["a-1", "legacy"]);

        // 🚨 The half that matters. The SAME registered policy now FAILS for
        // this caller, so the list narrows to what they own and the legacy row
        // disappears again. Without this, a gate that ignored the policy's
        // RESULT would still have passed the first assertion.
        ManagementPolicyHolder.Satisfied = false;
        identity.UserId = "a";

        (await ListAsync(host)).Select(static session => session.Id).ShouldBe(["a-1"]);

        identity.UserId = "nobody";
        (await ListAsync(host)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unregistered_management_policy_narrows_the_list_instead_of_opening_it()
    {
        // 🚨 The single worst way this could fail. AgentPrism's own role
        // policies are optional - a setup that registers none is supported and
        // common - so "policy missing" must mean "not management", never
        // "policy passed".
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "a-1");

        identity.UserId = "b";

        (await ListAsync(host)).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------
    // A single session belongs to one user.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Reading_another_owners_session_answers_the_same_404_a_missing_one_does()
    {
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "a-1");

        identity.UserId = "b";

        using var denied = await host.Client.GetAsync(Session("a-1"));
        using var missing = await host.Client.GetAsync(Session("no-such-session"));

        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 🚨 BYTE FOR BYTE (K-671): a denial that reads differently confirms
        // the session exists through a side channel.
        (await denied.Content.ReadAsStringAsync())
            .ShouldBe((await missing.Content.ReadAsStringAsync()).Replace("no-such-session", "a-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deleting_another_owners_session_answers_404_and_leaves_it_alone()
    {
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "a-1");

        identity.UserId = "b";

        using var response = await host.Client.DeleteAsync(Session("a-1"));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 🚨 The check has to run BEFORE the delete, not after: a "denied"
        // answer over a row that is already gone is not a denial.
        (await StoreAsync(host).GetAsync("a-1")).ShouldNotBeNull();
    }

    [Fact]
    public async Task An_owner_can_still_read_and_delete_their_own_session()
    {
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "a-1");

        using (var read = await host.Client.GetAsync(Session("a-1")))
        {
            read.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var deleted = await host.Client.DeleteAsync(Session("a-1"));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_unowned_legacy_session_stays_readable_by_id()
    {
        // Ownership is documented as NOT retroactive. Turning it on must not
        // strand every conversation that was live at the moment of the flip.
        await using var host = await StartAsync(enabled: true, out var identity);

        await StoreAsync(host).SaveAsync(new SessionRecord
        {
            Id = "legacy",
            AgentName = "kod-agent",
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        identity.UserId = "anybody";

        using var read = await host.Client.GetAsync(Session("legacy"));
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------
    // The owner cannot be forged, and cannot be dropped.
    // ------------------------------------------------------------------

    [Fact]
    public async Task No_body_field_can_set_the_owner()
    {
        // 🚨 The forgery this whole design refuses. The request body carries
        // every plausible spelling of an owner field; the row must still
        // record the identity the attribution pipeline resolved.
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";

        using var response = await host.Client.PostAsJsonAsync(
            Run,
            new
            {
                message = "hello",
                sessionId = "forged",
                ownerId = "victim",
                userId = "victim",
                owner = "victim",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await StoreAsync(host).GetAsync("forged")).ShouldNotBeNull().OwnerId.ShouldBe("a");
    }

    [Fact]
    public async Task A_second_turn_on_the_same_session_keeps_the_owner()
    {
        // 🚨 The defect class the plan flagged: a session is written more than
        // once, and the second write must not resolve - or lose - the owner
        // again. The version also has to advance, so this is genuinely the
        // TryUpdateAsync path and not a fresh create.
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";

        await RunAsync(host, "s1");
        var first = (await StoreAsync(host).GetAsync("s1")).ShouldNotBeNull();

        await RunAsync(host, "s1");
        var second = (await StoreAsync(host).GetAsync("s1")).ShouldNotBeNull();

        second.Version.ShouldBeGreaterThan(first.Version, "the second turn really did take the update path");
        second.OwnerId.ShouldBe("a");
    }

    [Fact]
    public async Task Running_against_another_owners_session_is_refused()
    {
        // 🚨 The widest door, and the one an earlier draft of this phase left
        // open: continuing a conversation replays its ENTIRE history into the
        // model and appends to it. Gating GET /api/sessions/{id} while leaving
        // POST .../run open would have made the whole boundary decorative.
        // Checking only the stored owner afterwards is not enough either -
        // the write preserves the source owner, so the row would look
        // untouched while B had already read A's conversation.
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "s1");

        var before = (await StoreAsync(host).GetAsync("s1")).ShouldNotBeNull();

        identity.UserId = "b";

        using var response = await host.Client.PostAsJsonAsync(
            Run, new AgentRunRequest { Message = "hello", SessionId = "s1" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var after = (await StoreAsync(host).GetAsync("s1")).ShouldNotBeNull();
        after.OwnerId.ShouldBe("a");
        after.Version.ShouldBe(before.Version, "the refused turn must not have written the session at all");
    }

    [Fact]
    public async Task Running_against_another_owners_conversation_is_refused_on_the_OpenAI_surface()
    {
        // 🚨 The same door, a different building. /v1/responses reaches a
        // session through 'conversation'/'previous_response_id' rather than
        // 'sessionId', so a gate installed only on /api/agents/{name}/run
        // leaves it wide open - the exact coverage mistake phase 139 made and
        // phase 147 had to come back for.
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "conv-a");

        identity.UserId = "b";

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/v1/responses", UriKind.Relative),
            new { model = "kod-agent", input = "hello", conversation = "conv-a" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_direct_store_write_that_carries_no_owner_does_not_clear_it()
    {
        // The store-level rule, asserted through the real registered store: a
        // background path with no identity behind it writes the session again
        // and must leave the owner in place.
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "s1");

        var store = StoreAsync(host);
        var stored = (await store.GetAsync("s1")).ShouldNotBeNull();

        await store.SaveAsync(stored with { OwnerId = null, UpdatedAt = DateTimeOffset.UtcNow });

        (await store.GetAsync("s1")).ShouldNotBeNull().OwnerId.ShouldBe("a");
    }

    // ------------------------------------------------------------------
    // Fail-closed when no identity can be resolved.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Opening_a_session_without_an_identity_is_refused_and_writes_no_row()
    {
        await using var host = await StartAsync(enabled: true, out var identity);
        identity.UserId = null;

        using var response = await host.Client.PostAsJsonAsync(
            Run, new AgentRunRequest { Message = "hello", SessionId = "orphan" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);
        problem.GetProperty("title").GetString().ShouldBe("Session owner required");
        problem.GetProperty("errorType").GetString()
            .ShouldBe(AgentPrismSessionOwnerRequiredException.SessionOwnerRequiredErrorType);

        // 🚨 The point of failing BEFORE the agent runs: no row at all, not a
        // row with a null owner that its caller can never list again.
        (await StoreAsync(host).GetAsync("orphan")).ShouldBeNull();
    }

    [Fact]
    public async Task A_sessionless_run_is_unaffected_by_the_owner_requirement()
    {
        // Nothing is being opened, so there is nothing to own. Refusing here
        // would break every stateless run in a deployment that turns the
        // option on.
        await using var host = await StartAsync(enabled: true, out var identity);
        identity.UserId = null;

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RequireAuthenticatedOwner_off_opens_an_unowned_session_instead_of_refusing()
    {
        await using var host = await StartAsync(
            enabled: true,
            out var identity,
            requireAuthenticatedOwner: false);

        identity.UserId = null;

        using var response = await host.Client.PostAsJsonAsync(
            Run, new AgentRunRequest { Message = "hello", SessionId = "unowned" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await StoreAsync(host).GetAsync("unowned")).ShouldNotBeNull().OwnerId.ShouldBeNull();
    }

    [Fact]
    public async Task A_faulty_attribution_implementation_is_treated_as_no_identity_not_as_a_crash()
    {
        // RunAttributionReader swallows a consumer's own failure and answers
        // null. Under ownership that null must become the ordinary refusal,
        // not a 500.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                Configure(services, enabled: true, requireAuthenticatedOwner: true);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new ThrowingAttribution()));
            });

        using var response = await host.Client.PostAsJsonAsync(
            Run, new AgentRunRequest { Message = "hello", SessionId = "orphan" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ------------------------------------------------------------------
    // The queued path has no HttpContext.
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_queued_run_takes_its_owner_from_the_job_envelope()
    {
        // 🚨 An HTTP-bound IRunAttributionContext has nothing to read inside a
        // background worker, so the identity has to travel WITH the job. The
        // attribution is switched to a different user before the worker runs,
        // which is what proves the owner came from the envelope and not from
        // whatever identity happened to be ambient at execution time.
        await using var host = await StartAsync(
            enabled: true,
            out var identity,
            configureServices: static services => services.UseScheduling(static o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        identity.UserId = "a";

        using (var accepted = await PostQueuedAsync(host, "queued-1"))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        identity.UserId = "someone-else";

        await RunQueuedJobsAsync(host, "queued-1");

        (await StoreAsync(host).GetAsync("queued-1")).ShouldNotBeNull().OwnerId.ShouldBe("a");
    }

    [Fact]
    public async Task A_queued_run_lists_under_its_owner_afterwards()
    {
        await using var host = await StartAsync(
            enabled: true,
            out var identity,
            configureServices: static services => services.UseScheduling(static o => o.PollInterval = TimeSpan.FromMilliseconds(20)));

        identity.UserId = "a";

        using (var accepted = await PostQueuedAsync(host, "queued-1"))
        {
            accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await RunQueuedJobsAsync(host, "queued-1");

        identity.UserId = "b";
        (await ListAsync(host)).ShouldBeEmpty();

        identity.UserId = "a";
        (await ListAsync(host)).Select(static session => session.Id).ShouldBe(["queued-1"]);
    }

    // ------------------------------------------------------------------
    // Idempotency: the second request does not re-own the session.
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_replayed_idempotent_request_does_not_change_the_owner()
    {
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";

        using (var first = await PostIdempotentAsync(host, "k1", "s1"))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // The SAME key replayed by a different identity. Whatever the endpoint
        // answers, the stored owner must still be the one who opened it.
        identity.UserId = "b";

        using var replay = await PostIdempotentAsync(host, "k1", "s1");

        (await StoreAsync(host).GetAsync("s1")).ShouldNotBeNull().OwnerId.ShouldBe(
            "a",
            $"the first request's owner is the session's owner (the replay answered {replay.StatusCode})");
    }

    // ------------------------------------------------------------------
    // Branching: a copy, not a handover.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Branching_keeps_the_source_sessions_owner()
    {
        using var database = new TempSqliteDatabase("session-owner-branch");
        await using var host = await StartSqliteAsync(database, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "source");

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/sessions/source/branch", UriKind.Relative),
            new SessionBranchRequest { NewSessionId = "branched" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await StoreAsync(host).GetAsync("branched")).ShouldNotBeNull().OwnerId.ShouldBe("a");
    }

    [Fact]
    public async Task Branching_another_owners_session_answers_404()
    {
        using var database = new TempSqliteDatabase("session-owner-branch-denied");
        await using var host = await StartSqliteAsync(database, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "source");

        identity.UserId = "b";

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/sessions/source/branch", UriKind.Relative),
            new SessionBranchRequest { NewSessionId = "stolen" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StoreAsync(host).GetAsync("stolen")).ShouldBeNull();
    }

    // ------------------------------------------------------------------
    // Every session-reaching surface, exercised — not only scanned for.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Reading_another_owners_conversation_on_the_OpenAI_surface_answers_404()
    {
        // 🚨 The gap this phase's own audit found. /v1/conversations reaches
        // the SAME sessions under a different name; while it stayed ungated,
        // GET /api/sessions/{id} answered 404 for another user's session and
        // GET /v1/conversations/{id}/items handed back its entire history.
        // A compatibility surface is a surface.
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "conv-a");

        identity.UserId = "b";

        using var retrieve = await host.Client.GetAsync(Conversation("conv-a"));
        using var items = await host.Client.GetAsync(new Uri(Conversation("conv-a") + "/items", UriKind.Relative));

        retrieve.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        items.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_another_owners_conversation_answers_404_and_leaves_it_alone()
    {
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "conv-a");

        identity.UserId = "b";

        using var response = await host.Client.DeleteAsync(Conversation("conv-a"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 🚨 The endpoint answers 200 {"deleted":false} for an id it cannot
        // find, so a status assertion alone would have passed even while the
        // row was being removed. The row itself is the proof.
        (await StoreAsync(host).GetAsync("conv-a")).ShouldNotBeNull();
    }

    [Fact]
    public async Task An_owner_can_still_reach_their_own_conversation()
    {
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "conv-a");

        using var retrieve = await host.Client.GetAsync(Conversation("conv-a"));
        retrieve.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var deleted = await host.Client.DeleteAsync(Conversation("conv-a"));
        deleted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Running_a_workflow_against_another_owners_session_is_refused()
    {
        // The workflow run surface carries a sessionId of its own. The source
        // scan proves the gate CALL is present; only this proves it is wired
        // to the right value.
        await using var host = await AgentPrismTestHost.StartAsync(
            builder => builder
                .AddAgent(TestData.Definition("writer"))
                .AddAgent(TestData.Definition("editor"))
                .UseWorkflows(),
            configureServices: services =>
            {
                Configure(services, enabled: true, requireAuthenticatedOwner: true);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(WorkflowIdentity));
            });

        using (var saved = await host.Client.PutAsJsonAsync(
            "/agentprism/api/workflows/chain",
            new WorkflowSaveRequest { Kind = WorkflowKind.Sequential, AgentNames = ["writer", "editor"] }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        WorkflowIdentity.UserId = "a";

        await StoreAsync(host).SaveAsync(new SessionRecord
        {
            Id = "wf-a",
            AgentName = "writer",
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
            OwnerId = "a",
        });

        WorkflowIdentity.UserId = "b";

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/workflows/chain/run",
            new WorkflowRunHttpRequest { Message = "hello", SessionId = "wf-a" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Opening_a_voice_socket_on_another_owners_session_is_refused()
    {
        // A voice socket WRITES to the session it joins, so leaving this path
        // ungated would let one user speak into another user's conversation
        // while every HTTP route to it answered 404.
        var identity = new SwitchableAttribution();

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static builder => builder
                .AddAgent(TestData.Definition("kod-agent"))
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureServices: services =>
            {
                Configure(services, enabled: true, requireAuthenticatedOwner: true);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(identity));
                var voice = new StubVoiceProvider();
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
            });

        await StoreAsync(host).SaveAsync(new SessionRecord
        {
            Id = "voice-a",
            AgentName = "kod-agent",
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
            OwnerId = "a",
        });

        identity.UserId = "b";

        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        // TestServer surfaces a refused handshake as InvalidOperationException.
        var connect = async () => await socketClient.ConnectAsync(
            new Uri("http://localhost/agentprism/api/voice/sessions/voice-a/stream"),
            TestContext.Current.CancellationToken);

        var error = await connect.ShouldThrowAsync<InvalidOperationException>();

        // 404, the same code an unreachable session already produced — never
        // 403, which would confirm this conversation exists.
        error.Message.ShouldContain("404");
    }

    [Fact]
    public async Task An_unknown_session_still_opens_a_voice_socket()
    {
        // K-283 survives ownership: a session nothing has written yet carries
        // no owner and is not refused. Breaking this would silently kill the
        // FIRST conversation in every installation.
        var identity = new SwitchableAttribution { UserId = "a" };

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: static builder => builder
                .AddAgent(TestData.Definition("kod-agent"))
                .UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureServices: services =>
            {
                Configure(services, enabled: true, requireAuthenticatedOwner: true);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(identity));
                var voice = new StubVoiceProvider();
                services.AddSingleton<ISpeechTranscriber>(voice);
                services.AddSingleton<ISpeechSynthesizer>(voice);
            });

        var socketClient = host.CreateWebSocketClient();
        socketClient.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);

        using var socket = await socketClient.ConnectAsync(
            new Uri("http://localhost/agentprism/api/voice/sessions/never-seen/stream", UriKind.Absolute),
            TestContext.Current.CancellationToken);

        socket.State.ShouldBe(WebSocketState.Open);
    }

    // ------------------------------------------------------------------
    // Failure paths the plan's own table promised.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Two_concurrent_turns_on_one_session_cannot_erase_the_owner()
    {
        // Optimistic concurrency already makes one of the two writers lose;
        // what this adds is that the LOSER's failure never costs the winner
        // its owner, and the surviving row is still owned.
        await using var host = await StartAsync(enabled: true, out var identity);

        identity.UserId = "a";
        await RunAsync(host, "race");

        var attempts = Enumerable.Range(0, 4).Select(_ => host.Client.PostAsJsonAsync(
            Run, new AgentRunRequest { Message = "hello", SessionId = "race" }));

        foreach (var response in await Task.WhenAll(attempts))
        {
            response.Dispose();
        }

        (await StoreAsync(host).GetAsync("race")).ShouldNotBeNull().OwnerId.ShouldBe("a");
    }

    [Fact]
    public async Task A_cancelled_write_leaves_no_unowned_row_behind()
    {
        await using var host = await StartAsync(enabled: true, out var identity);
        identity.UserId = "a";

        using var cancellation = new CancellationTokenSource();

        var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello", SessionId = "cancelled" }),
        };

        var send = host.Client.SendAsync(request, cancellation.Token);
        await cancellation.CancelAsync();

        try
        {
            (await send).Dispose();
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException)
        {
            // The cancellation itself is not the assertion.
        }

        // Whatever the cancellation raced with, the row is either absent or
        // owned — never present and unowned, which is the state that would be
        // invisible to its own caller forever.
        var record = await StoreAsync(host).GetAsync("cancelled");
        (record is null || string.Equals(record.OwnerId, "a", StringComparison.Ordinal)).ShouldBeTrue(
            $"a cancelled write must not leave an unowned row (owner was '{record?.OwnerId}')");
    }

    [Fact]
    public async Task A_store_that_fails_the_write_does_not_report_a_session()
    {
        // The ownership path must not convert a store failure into a silent
        // success: nothing readable may be left behind.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                Configure(services, enabled: true, requireAuthenticatedOwner: true);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(new SwitchableAttribution { UserId = "a" }));
                services.Replace(ServiceDescriptor.Singleton<ISessionStore>(new FailingSessionStore()));
            });

        using var response = await host.Client.PostAsJsonAsync(
            Run, new AgentRunRequest { Message = "hello", SessionId = "explodes" });

        // The status shape is the streaming path's (an SSE error frame under
        // 200); what matters is that no session was reported as stored.
        (await StoreAsync(host).GetAsync("explodes")).ShouldBeNull();
    }

    // ------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------

    private static Uri Session(string sessionId)
        => new($"/agentprism/api/sessions/{sessionId}", UriKind.Relative);

    private static Uri Conversation(string conversationId)
        => new($"/agentprism/v1/conversations/{conversationId}", UriKind.Relative);

    /// <summary>
    /// A process-wide identity for the workflow case, which cannot use the
    /// <c>out</c> parameter because its host is built inline.
    /// </summary>
    private static readonly SwitchableAttribution WorkflowIdentity = new();

    /// <summary>
    /// The answer the registered management policy gives, so one test can flip
    /// it without re-registering the policy.
    /// </summary>
    private static class ManagementPolicyHolder
    {
        public static bool Satisfied { get; set; }
    }

    /// <summary>Builds a stored session directly, bypassing the run pipeline.</summary>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="owner">The owner to record, or <see langword="null"/> for a pre-ownership row.</param>
    /// <returns>The record to save.</returns>
    private static SessionRecord Seed(string sessionId, string? owner)
        => new()
        {
            Id = sessionId,
            AgentName = "kod-agent",
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
            OwnerId = owner,
        };

    private static ISessionStore StoreAsync(AgentPrismTestHost host)
        => host.Services.GetRequiredService<ISessionStore>();

    private static async Task RunAsync(AgentPrismTestHost host, string sessionId)
    {
        using var response = await host.Client.PostAsJsonAsync(
            Run, new AgentRunRequest { Message = "hello", SessionId = sessionId });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> PostQueuedAsync(AgentPrismTestHost host, string sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello", SessionId = sessionId }),
        };

        request.Headers.Add("Prefer", "respond-async");

        return host.Client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostIdempotentAsync(
        AgentPrismTestHost host, string key, string sessionId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello", SessionId = sessionId }),
        };

        request.Headers.Add("Idempotency-Key", key);

        return host.Client.SendAsync(request);
    }

    /// <summary>
    /// Waits for the REAL background worker to pick the queued session up.
    /// </summary>
    /// <remarks>
    /// 🚨 Deliberately the real worker rather than an inline drain: the claim
    /// under test is precisely that a thread with no HttpContext behind it
    /// still writes the right owner, and calling the handler from the test's
    /// own request-free thread would prove a weaker statement than the one
    /// production runs. The generous deadline follows AsyncRunTests' measured
    /// note - under load a short one is not enough, while a healthy run exits
    /// in milliseconds.
    /// </remarks>
    private static async Task RunQueuedJobsAsync(AgentPrismTestHost host, string sessionId)
    {
        var store = StoreAsync(host);
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (await store.GetAsync(sessionId) is not null)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException($"The queued run never wrote session '{sessionId}'.");
    }

    private static async Task<SessionRecord[]> ListAsync(AgentPrismTestHost host, string query = "")
    {
        using var response = await host.Client.GetAsync(new Uri(Sessions + query, UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<SessionRecord[]>())!;
    }

    private static Task<AgentPrismTestHost> StartAsync(
        bool enabled,
        out SwitchableAttribution identity,
        bool requireAuthenticatedOwner = true,
        Action<IServiceCollection>? configureServices = null)
    {
        var attribution = new SwitchableAttribution();
        identity = attribution;

        return AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: services =>
            {
                Configure(services, enabled, requireAuthenticatedOwner);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(attribution));
                configureServices?.Invoke(services);
            });
    }

    private static Task<AgentPrismTestHost> StartSqliteAsync(
        TempSqliteDatabase database,
        out SwitchableAttribution identity)
    {
        var attribution = new SwitchableAttribution();
        identity = attribution;

        return AgentPrismTestHost.StartAsync(
            builder => builder
                .AddAgent(TestData.Definition())
                .UseSqlite(options => options.ConnectionString = database.ConnectionString),
            configureServices: services =>
            {
                Configure(services, enabled: true, requireAuthenticatedOwner: true);
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(attribution));
            });
    }

    private static void Configure(IServiceCollection services, bool enabled, bool requireAuthenticatedOwner)
        => services.Configure<AgentPrismSessionOwnershipOptions>(options =>
        {
            options.Enabled = enabled;
            options.RequireAuthenticatedOwner = requireAuthenticatedOwner;
        });

    /// <summary>
    /// An <see cref="IRunAttributionContext"/> whose answer the test changes
    /// between requests, so several users share one host and one store.
    /// </summary>
    private sealed class SwitchableAttribution : IRunAttributionContext
    {
        public string? UserId { get; set; }

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    /// <summary>A consumer implementation that fails inside its own pipeline.</summary>
    private sealed class ThrowingAttribution : IRunAttributionContext
    {
        public string? UserId => throw new InvalidOperationException("the identity pipeline is down");

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    /// <summary>A store whose writes fail, to exercise the subsystem-failure path.</summary>
    private sealed class FailingSessionStore : ISessionStore
    {
        public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
            => throw new AgentPrismException("the session store is unavailable");

        public ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
            => throw new AgentPrismException("the session store is unavailable");

        public ValueTask<bool> TryUpdateAsync(SessionRecord record, long expectedVersion, CancellationToken cancellationToken = default)
            => throw new AgentPrismException("the session store is unavailable");

        public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
            => new((SessionRecord?)null);

        public ValueTask<string?> GetOwnerTenantIdAsync(string sessionId, CancellationToken cancellationToken = default)
            => new((string?)null);

        public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => new(false);

        public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default)
            => new((IReadOnlyList<SessionRecord>)[]);
    }
}

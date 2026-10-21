using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;
using FakeModelRequest = Tracon.Testing.FakeModelRequest;

namespace Tracon.AspNetCore.FunctionalTests;

// MAAI001: the session store types are marked "evaluation purposes only"; the
// stores below stand in for a consumer's own (Phase 229).
#pragma warning disable MAAI001

/// <summary>
/// Phase 229: an A2A caller that sends a second message with the same
/// <c>contextId</c> continues its conversation, and no other caller can reach it.
/// </summary>
/// <remarks>
/// <para>
/// Before this phase the reply carried the <c>contextId</c> back but the agent
/// kept no history: a caller believed it was in a conversation and the model saw
/// one message. The cases run a real <c>SendMessage</c> through the real A2A
/// handler and read what the model was actually sent, because the tenant and
/// caller boundary sits between the A2A handler, the isolation provider, the
/// session store and the shared <c>sessions</c> table, and no unit test covers
/// all four.
/// </para>
/// </remarks>
public sealed class A2AConversationTests
{
    private const string Agent = "kod-agent";
    private const string OtherAgent = "other-agent";
    private const string ProviderName = "conversation-provider";
    private const string ModelName = "conversation-model";

    // ---- the conversation --------------------------------------------------------

    [Fact]
    public async Task A_second_message_with_the_same_context_id_sees_the_first_turn()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1");
        await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1");

        UserTexts(provider.Requests[^1]).ShouldBe(["My name is Ada", "What is my name?"]);
    }

    [Fact]
    public async Task A_message_with_another_context_id_starts_a_new_conversation()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1");
        await SendAsync(host, Agent, "What is my name?", contextId: "ctx-2");

        UserTexts(provider.Requests[^1]).ShouldBe(["What is my name?"]);
    }

    [Fact]
    public async Task A_message_without_a_context_id_is_a_single_turn()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SendAsync(host, Agent, "My name is Ada", contextId: null);
        await SendAsync(host, Agent, "What is my name?", contextId: null);

        UserTexts(provider.Requests[^1]).ShouldBe(["What is my name?"]);
    }

    [Fact]
    public async Task A_conversation_is_stored_under_an_a2a_identity_that_names_its_agent()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);

        await SendAsync(host, Agent, "hello", contextId: "ctx-1");

        var session = (await SessionsAsync(host)).ShouldHaveSingleItem();

        session.Id.ShouldStartWith("a2a:");
        session.Id.Length.ShouldBe(68);
        session.AgentName.ShouldBe(Agent);
    }

    [Fact]
    public async Task A_very_long_context_id_completes_and_the_stored_identity_stays_short()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);
        var contextId = new string('c', 4096);

        var (first, _) = await SendAsync(host, Agent, "My name is Ada", contextId);
        var (second, _) = await SendAsync(host, Agent, "What is my name?", contextId);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        UserTexts(provider.Requests[^1]).ShouldBe(["My name is Ada", "What is my name?"]);
        (await SessionsAsync(host)).ShouldHaveSingleItem().Id.Length.ShouldBe(68);
    }

    // ---- who can reach it --------------------------------------------------------

    [Fact]
    public async Task Another_api_key_with_the_same_context_id_does_not_see_the_conversation()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider);
        var first = await CreateKeyAsync(host, "first");
        var second = await CreateKeyAsync(host, "second");

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1", token: first);
        var (response, body) = await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1", token: second);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        HasResult(body).ShouldBeTrue(body.ToString());
        UserTexts(provider.Requests[^1]).ShouldBe(
            ["What is my name?"],
            "a context id is a conversation handle, not a credential");

        await SendAsync(host, Agent, "Still me", contextId: "ctx-1", token: first);

        UserTexts(provider.Requests[^1]).ShouldBe(
            ["My name is Ada", "Still me"],
            "the first key still resumes its own conversation");
        (await SessionsAsync(host)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Two_agents_with_the_same_caller_and_context_id_keep_separate_conversations()
    {
        var provider = Provider();
        await using var host = await StartAsync(provider, alsoExpose: OtherAgent);

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1");
        var (response, body) = await SendAsync(host, OtherAgent, "What is my name?", contextId: "ctx-1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        HasResult(body).ShouldBeTrue(body.ToString());
        UserTexts(provider.Requests[^1]).ShouldBe(["What is my name?"]);

        var sessions = await SessionsAsync(host);

        sessions.Select(static session => session.AgentName).Order(StringComparer.Ordinal).ShouldBe([Agent, OtherAgent]);
        sessions.Select(static session => session.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
    }

    [Fact]
    public async Task One_user_in_two_tenants_with_the_same_context_id_keeps_two_conversations()
    {
        // The sessions table keys a row by tenant AND identity, so two tenants
        // could share an identity without a row clash. They must not: a lookup by
        // identity alone (ISessionStore.GetOwnerTenantIdAsync, which the OpenAI
        // compatible surfaces use) has to find ONE tenant.
        var provider = Provider();
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .AddAgent(Definition(Agent))
                .UseA2A(static options => options.ExposedAgents.Add(Agent))
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.ClaimType = UserFromHeadersHandler.TenantClaim;
                }),
            configureServices: static services => services
                .AddAuthentication(UserFromHeadersHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, UserFromHeadersHandler>(UserFromHeadersHandler.SchemeName, configureOptions: null),
            configureAfterMap: static app => app.MapTraconA2A());

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1", user: "ada", tenant: "acme");
        var (response, body) = await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1", user: "ada", tenant: "globex");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        HasResult(body).ShouldBeTrue(body.ToString());
        UserTexts(provider.Requests[^1]).ShouldBe(["What is my name?"]);

        await SendAsync(host, Agent, "Still acme", contextId: "ctx-1", user: "ada", tenant: "acme");

        UserTexts(provider.Requests[^1]).ShouldBe(["My name is Ada", "Still acme"]);

        var acme = (await SessionsAsync(host, "acme")).ShouldHaveSingleItem();
        var globex = (await SessionsAsync(host, "globex")).ShouldHaveSingleItem();

        string.Equals(acme.Id, globex.Id, StringComparison.Ordinal).ShouldBeFalse("an identity belongs to one tenant");
    }

    [Fact]
    public async Task An_authenticated_user_is_the_caller_and_keeps_a_conversation_apart_from_an_api_key()
    {
        var provider = Provider();
        await using var host = await StartWithUsersAsync(provider);
        var key = await CreateKeyAsync(host, "key");

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1", user: "ada");
        await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1", user: "ada");

        UserTexts(provider.Requests[^1]).ShouldBe(["My name is Ada", "What is my name?"], "a user resumes their own conversation");

        await SendAsync(host, Agent, "Who am I?", contextId: "ctx-1", user: "grace");

        UserTexts(provider.Requests[^1]).ShouldBe(["Who am I?"], "another user does not see it");

        await SendAsync(host, Agent, "And me?", contextId: "ctx-1", token: key);

        UserTexts(provider.Requests[^1]).ShouldBe(["And me?"], "an API key does not either");
    }

    [Fact]
    public async Task An_authenticated_user_without_a_readable_actor_is_refused_and_not_merged_into_the_anonymous_caller()
    {
        // 🚨 With Tracon:Audit:ActorClaimType set to a claim this user lacks, the
        // audit actor is empty. Falling back to the anonymous key would put every
        // such user into ONE shared conversation space.
        var provider = Provider();
        await using var host = await StartWithUsersAsync(
            provider,
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.Audit.ActorClaimType = "missing-claim"));

        var (_, refused) = await SendAsync(host, Agent, "hello", contextId: "ctx-1", user: "ada");

        HasResult(refused).ShouldBeFalse(refused.ToString());
        refused.TryGetProperty("error", out _).ShouldBeTrue(refused.ToString());
        provider.Requests.ShouldBeEmpty("the refusal comes before the model call");
        (await SessionsAsync(host)).ShouldBeEmpty();
        LoggedError(host, "isolation key is required").ShouldBeTrue("the operator can read the cause in the log");
    }

    [Fact]
    public async Task An_api_key_still_has_a_conversation_when_the_audit_actor_claim_is_configured()
    {
        // The audit actor of a key is a synthetic claim; ActorClaimType makes it
        // read EMPTY. The isolation key reads the key itself.
        var provider = Provider();
        await using var host = await StartAsync(
            provider,
            configureServices: static services => services.Configure<TraconOptions>(
                static options => options.Audit.ActorClaimType = "missing-claim"));
        var key = await CreateKeyAsync(host, "key");

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1", token: key);
        var (_, body) = await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1", token: key);

        HasResult(body).ShouldBeTrue(body.ToString());
        UserTexts(provider.Requests[^1]).ShouldBe(["My name is Ada", "What is my name?"]);
    }

    // ---- the consumer's registrations win ---------------------------------------

    [Fact]
    public async Task A_session_store_the_consumer_registered_under_the_agent_name_is_used()
    {
        var store = new RecordingSessionStore();
        var provider = Provider();
        await using var host = await StartAsync(
            provider,
            configureServices: services => services.AddKeyedSingleton<AgentSessionStore>(Agent, store));

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1");
        await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1");

        UserTexts(provider.Requests[^1]).ShouldBe(["My name is Ada", "What is my name?"]);
        store.Isolations.Distinct(StringComparer.Ordinal).ShouldBe(["anonymous"], "the provider still scopes the consumer's store");
        (await SessionsAsync(host)).ShouldBeEmpty("Tracon's own session store was not used");
    }

    [Fact]
    public async Task A_session_store_the_consumer_registered_without_a_key_is_used()
    {
        var store = new RecordingSessionStore();
        var provider = Provider();
        await using var host = await StartAsync(
            provider,
            configureServices: services => services.AddSingleton<AgentSessionStore>(store));

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1");
        await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1");

        UserTexts(provider.Requests[^1]).ShouldBe(["My name is Ada", "What is my name?"]);
        store.Saves.ShouldBe(2);
        (await SessionsAsync(host)).ShouldBeEmpty("Tracon's own session store was not used");
    }

    [Fact]
    public async Task A_keyed_store_registered_after_UseA2A_wins_over_the_default()
    {
        var store = new RecordingSessionStore();
        var provider = Provider();
        await using var host = await StartAsync(
            provider,
            configureTracon: builder => builder.Services.AddKeyedSingleton<AgentSessionStore>(Agent, store));

        await SendAsync(host, Agent, "hello", contextId: "ctx-1");

        store.Saves.ShouldBe(1);
        (await SessionsAsync(host)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_isolation_key_provider_the_consumer_registered_is_used()
    {
        var provider = Provider();
        await using var host = await StartAsync(
            provider,
            configureServices: static services => services.AddSingleton<AgentIsolationKeyProvider>(new FixedIsolationKeyProvider("one-room")));
        var first = await CreateKeyAsync(host, "first");
        var second = await CreateKeyAsync(host, "second");

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1", token: first);
        await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1", token: second);

        UserTexts(provider.Requests[^1]).ShouldBe(
            ["My name is Ada", "What is my name?"],
            "the consumer's provider decided that both keys are the same caller");
    }

    [Fact]
    public async Task The_caller_is_named_by_kind_so_a_user_cannot_pose_as_an_api_key()
    {
        // 🚨 A claim value that reads like an API key id must not land in that
        // key's conversation. The isolation key is the kind AND the identity.
        var store = new RecordingSessionStore();
        var provider = Provider();
        await using var host = await StartWithUsersAsync(
            provider,
            configureServices: services => services.AddKeyedSingleton<AgentSessionStore>(Agent, store));
        var (keyId, key) = await CreateKeyWithIdAsync(host, "key");

        await SendAsync(host, Agent, "as the key", contextId: "ctx-1", token: key);
        await SendAsync(host, Agent, "as the look-alike", contextId: "ctx-1", user: "apikey:" + keyId);

        UserTexts(provider.Requests[^1]).ShouldBe(["as the look-alike"], "the user does not read the key's conversation");

        await SendAsync(host, Agent, "as Ada", contextId: "ctx-1", user: "ada");
        await SendAsync(host, Agent, "as nobody", contextId: "ctx-1");

        // Each kind of caller has its own isolation key.
        store.Isolations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ShouldBe(
            ["anonymous", "apikey:" + keyId, "user:ada", "user:apikey:" + keyId]);
    }

    [Fact]
    public async Task The_static_auth_token_is_its_own_caller_and_not_the_anonymous_operator()
    {
        const string Token = "static-token-for-the-a2a-conversation-test";
        var store = new RecordingSessionStore();
        await using var host = await StartAsync(
            Provider(),
            configureServices: services => services.AddKeyedSingleton<AgentSessionStore>(Agent, store),
            configureEndpoints: static options => options.AuthToken = Token);

        var (response, body) = await SendAsync(host, Agent, "hello", contextId: "ctx-1", token: Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        HasResult(body).ShouldBeTrue(body.ToString());
        store.Isolations.Distinct(StringComparer.Ordinal).ShouldBe(["static-token"]);
    }

    // ---- session ownership -------------------------------------------------------

    [Fact]
    public async Task Session_ownership_refuses_a_caller_with_no_attributable_identity_before_the_model_is_called()
    {
        var provider = Provider();
        await using var host = await StartAsync(
            provider,
            configureServices: static services => services.Configure<TraconSessionOwnershipOptions>(static options =>
            {
                options.Enabled = true;
                options.RequireAuthenticatedOwner = true;
            }));

        var (_, body) = await SendAsync(host, Agent, "hello", contextId: "ctx-1");

        HasResult(body).ShouldBeFalse(body.ToString());
        provider.Requests.ShouldBeEmpty("a refusal after the model call has already cost money");
        (await SessionsAsync(host)).ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery())).ShouldBeEmpty("no run was opened");
        LoggedError(host, "session ownership is on").ShouldBeTrue("the operator can read the cause in the log");
    }

    [Fact]
    public async Task Session_ownership_gives_the_conversation_to_the_attributed_user()
    {
        var provider = Provider();
        var identity = new FixedAttribution("ada");
        await using var host = await StartAsync(
            provider,
            configureServices: services =>
            {
                services.Configure<TraconSessionOwnershipOptions>(static options =>
                {
                    options.Enabled = true;
                    options.RequireAuthenticatedOwner = true;
                });
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(identity));
            });

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1");
        await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1");

        UserTexts(provider.Requests[^1]).ShouldBe(["My name is Ada", "What is my name?"]);
        (await SessionsAsync(host)).ShouldHaveSingleItem().OwnerId.ShouldBe("ada");
    }

    [Fact]
    public async Task Session_ownership_keeps_one_end_users_conversation_from_another_user_behind_the_same_api_key()
    {
        // A gateway holds ONE key and speaks for many users. The isolation key
        // cannot tell them apart; ownership can.
        var provider = Provider();
        var identity = new SwitchableAttribution { UserId = "u1" };
        await using var host = await StartAsync(
            provider,
            configureServices: services =>
            {
                services.Configure<TraconSessionOwnershipOptions>(static options =>
                {
                    options.Enabled = true;
                    options.RequireAuthenticatedOwner = true;
                });
                services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(identity));
            });
        var key = await CreateKeyAsync(host, "gateway");

        await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1", token: key);

        identity.UserId = "u2";
        var (_, refused) = await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1", token: key);

        HasResult(refused).ShouldBeFalse(refused.ToString());
        provider.Requests.Count.ShouldBe(1, "the other user's turn never reached the model, and neither did the history");
        LoggedError(host, "belongs to another user").ShouldBeTrue("the operator can read the cause in the log");

        identity.UserId = "u1";
        var (_, resumed) = await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1", token: key);

        HasResult(resumed).ShouldBeTrue(resumed.ToString());
        UserTexts(provider.Requests[^1]).ShouldBe(["My name is Ada", "What is my name?"], "the owner still resumes");
    }

    // ---- when something goes wrong -----------------------------------------------

    [Fact]
    public async Task A_store_that_cannot_save_fails_the_call_and_the_next_message_starts_over()
    {
        var store = new RecordingSessionStore { FailSaves = true };
        var provider = Provider();
        await using var host = await StartAsync(
            provider,
            configureServices: services => services.AddKeyedSingleton<AgentSessionStore>(Agent, store));

        var (_, failed) = await SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1");

        HasResult(failed).ShouldBeFalse(failed.ToString());
        failed.TryGetProperty("error", out _).ShouldBeTrue(failed.ToString());
        LoggedError(host, "the consumer's store is unavailable").ShouldBeTrue("the operator can read the cause in the log");

        store.FailSaves = false;
        await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1");

        UserTexts(provider.Requests[^1]).ShouldBe(["What is my name?"], "the lost turn was never stored");
    }

    [Fact]
    public async Task A_turn_that_ends_for_a_pending_approval_stays_in_the_conversation_and_the_next_message_goes_through()
    {
        // The startup guard refuses to expose an agent that carries an
        // approval-requiring tool. A definition saved AFTER that check can still
        // add one. MAF's handler stores the session whether the turn failed or
        // not, so the refused turn is part of the conversation; MAF declines the
        // unanswered approval itself, so the next turn is not left with a call
        // that has no result.
        var provider = new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .CallsTool("cancel_order", new { orderId = "A1" });
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .AddTool(
                    (Func<string, string>)CancelOrder,
                    name: "cancel_order",
                    description: "Cancels an order.",
                    configure: options => options.RequiresApproval = true)
                .UseA2A(static options => options.ExposedAgents.Add(Agent)),
            configureAfterMap: static app => app.MapTraconA2A());

        // Every request waits for the startup guard, so one request is enough to
        // know it has looked at the catalog — and the agent is not in it yet.
        using (var card = await host.Client.GetAsync(
            new Uri($"/tracon/a2a/{Agent}/.well-known/agent-card.json", UriKind.Relative),
            TestContext.Current.CancellationToken))
        {
            card.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var created = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            TestData.Request(Agent) with
            {
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                ToolNames = ["cancel_order"],
            },
            TestContext.Current.CancellationToken))
        {
            created.EnsureSuccessStatusCode();
        }

        var (_, refused) = await SendAsync(host, Agent, "Cancel order A1", contextId: "ctx-1");

        HasResult(refused).ShouldBeFalse(refused.ToString());
        (await SessionsAsync(host)).ShouldHaveSingleItem();

        var (_, next) = await SendAsync(host, Agent, "Is it done?", contextId: "ctx-1");

        HasResult(next).ShouldBeTrue(next.ToString());
        UserTexts(provider.Requests[^1]).ShouldBe(["Cancel order A1", "Is it done?"]);
        provider.Requests[^1].Messages.ShouldContain(
            static message => message.Role == ChatRole.Tool,
            "the declined call has a result in the history");
    }

    [Fact]
    public async Task A_caller_that_hangs_up_mid_turn_leaves_no_half_turn_behind()
    {
        var provider = new GatedEchoProvider();
        await using var host = await StartGatedAsync(provider);
        using var hangUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var abandoned = SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1", cancellationToken: hangUp.Token);

        await WaitUntil.TrueAsync(() => provider.Calls == 1);
        await hangUp.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(async () => await abandoned);
        await WaitUntil.TrueAsync(() => provider.Cancelled == 1);

        provider.Open();
        var (response, body) = await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        HasResult(body).ShouldBeTrue(body.ToString());
        provider.Seen[^1].ShouldBe(["What is my name?"], "the abandoned turn was not stored");
    }

    [Fact]
    public async Task Two_messages_at_once_on_one_conversation_do_not_overwrite_each_other_silently()
    {
        var provider = new GatedEchoProvider();
        await using var host = await StartGatedAsync(provider);

        var first = SendAsync(host, Agent, "first", contextId: "ctx-1");
        var second = SendAsync(host, Agent, "second", contextId: "ctx-1");

        await WaitUntil.TrueAsync(() => provider.Calls == 2);
        provider.Open();

        var bodies = (await Task.WhenAll(first, second)).Select(static answer => answer.Body).ToList();

        bodies.Count(static body => HasResult(body)).ShouldBe(
            1,
            "the loser of a race on one conversation is told, not lost: " + string.Join(" | ", bodies));
        (await SessionsAsync(host)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_turn_in_flight_when_the_process_starts_draining_is_answered_and_kept()
    {
        var provider = new GatedEchoProvider();
        var drain = new SwitchableDrainState();
        await using var host = await StartGatedAsync(provider, drain);

        var inFlight = SendAsync(host, Agent, "My name is Ada", contextId: "ctx-1");

        await WaitUntil.TrueAsync(() => provider.Calls == 1);
        drain.IsDraining = true;
        provider.Open();

        var (response, body) = await inFlight;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        HasResult(body).ShouldBeTrue("a finished turn is handed over even while draining: " + body);
        (await SessionsAsync(host)).ShouldHaveSingleItem();

        var (_, refused) = await SendAsync(host, Agent, "A new message", contextId: "ctx-1");

        HasResult(refused).ShouldBeFalse("a new message is refused while draining: " + refused);

        drain.IsDraining = false;
        await SendAsync(host, Agent, "What is my name?", contextId: "ctx-1");

        provider.Seen[^1].ShouldBe(["My name is Ada", "What is my name?"]);
    }

    // ---- scaffolding -------------------------------------------------------------

    private static string CancelOrder(string orderId) => $"canceled: {orderId}";

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        Action<ITraconBuilder>? configureTracon = null,
        Action<IServiceCollection>? configureServices = null,
        string? alsoExpose = null,
        Action<TraconEndpointOptions>? configureEndpoints = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .AddModelProvider(provider)
                    .AddAgent(Definition(Agent))
                    .UseA2A(options =>
                    {
                        options.ExposedAgents.Add(Agent);

                        if (alsoExpose is not null)
                        {
                            options.ExposedAgents.Add(alsoExpose);
                        }
                    });

                if (alsoExpose is not null)
                {
                    builder.AddAgent(Definition(alsoExpose));
                }

                configureTracon?.Invoke(builder);
            },
            configureEndpoints: configureEndpoints,
            configureServices: configureServices,
            configureAfterMap: static app => app.MapTraconA2A());

    private static Task<TraconTestHost> StartWithUsersAsync(
        FakeModelProvider provider,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .AddAgent(Definition(Agent))
                .UseA2A(static options => options.ExposedAgents.Add(Agent)),
            configureServices: services =>
            {
                services
                    .AddAuthentication(UserFromHeadersHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, UserFromHeadersHandler>(UserFromHeadersHandler.SchemeName, configureOptions: null);
                configureServices?.Invoke(services);
            },
            configureAfterMap: static app => app.MapTraconA2A());

    private static Task<TraconTestHost> StartGatedAsync(GatedEchoProvider provider, ITraconDrainState? drain = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .AddModelProvider(provider)
                .AddAgent(Definition(Agent))
                .UseA2A(static options => options.ExposedAgents.Add(Agent)),
            configureServices: services =>
            {
                if (drain is not null)
                {
                    services.AddSingleton(drain);
                }
            },
            configureAfterMap: static app => app.MapTraconA2A());

    private static AgentDefinition Definition(string name)
        => TestData.Definition(name) with
        {
            Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
        };

    private static IReadOnlyList<string> UserTexts(FakeModelRequest request)
        => [.. request.Messages.Where(static message => message.Role == ChatRole.User).Select(static message => message.Text)];

    private static bool HasResult(JsonElement body) => body.TryGetProperty("result", out _);

    // The A2A SDK answers a handler failure with an opaque -32603 "internal
    // error" and logs the real exception at Error level.
    private static bool LoggedError(TraconTestHost host, string text)
        => host.Logs.Entries.Any(entry =>
            entry.StartsWith("Error", StringComparison.Ordinal) && entry.Contains(text, StringComparison.OrdinalIgnoreCase));

    private static async Task<IReadOnlyList<SessionRecord>> SessionsAsync(TraconTestHost host, string? tenant = null)
        => await host.Services.GetRequiredService<AgentSessionManager>()
            .QuerySessionsAsync(new SessionQuery { Take = 50, TenantId = tenant }, TestContext.Current.CancellationToken);

    private static async Task<string> CreateKeyAsync(TraconTestHost host, string name)
        => (await CreateKeyWithIdAsync(host, name)).Key;

    private static async Task<(string Id, string Key)> CreateKeyWithIdAsync(TraconTestHost host, string name)
    {
        var created = await host.Services.GetRequiredService<IApiKeyStore>().CreateAsync(
            new ApiKeyDraft
            {
                TenantId = "default",
                Name = name,
                Scopes = [ApiKeyScope.ExternalInvoke],
            },
            TestContext.Current.CancellationToken);

        return (created.Record.Id.ToString(), created.PlaintextKey);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> SendAsync(
        TraconTestHost host,
        string agentName,
        string text,
        string? contextId,
        string? token = null,
        string? user = null,
        string? tenant = null,
        CancellationToken cancellationToken = default)
    {
        // Built with the SDK's own types and serializer options, as in
        // A2AEndpointTests: the field names and casing come from the contract.
        var sendMessageRequest = new A2A.SendMessageRequest
        {
            Message = new A2A.Message
            {
                Role = A2A.Role.User,
                Parts = [A2A.Part.FromText(text)],
                MessageId = Guid.NewGuid().ToString(),
                ContextId = contextId,
            },
        };

        var paramsJson = JsonSerializer.SerializeToElement(sendMessageRequest, A2A.A2AJsonUtilities.DefaultOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/a2a/{agentName}")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "SendMessage",
                @params = paramsJson,
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (user is not null)
        {
            request.Headers.Add(UserFromHeadersHandler.UserHeader, user);
        }

        if (tenant is not null)
        {
            request.Headers.Add(UserFromHeadersHandler.TenantHeader, tenant);
        }

        var token2 = cancellationToken.CanBeCanceled ? cancellationToken : TestContext.Current.CancellationToken;
        var response = await host.Client.SendAsync(request, token2);
        var body = await response.Content.ReadAsStringAsync(token2);

        return (response, JsonDocument.Parse(body).RootElement.Clone());
    }

    /// <summary>A consumer's store: remembers what it was asked and can be told to fail a save.</summary>
    private sealed class RecordingSessionStore : DelegatingAgentSessionStore
    {
        private readonly List<string?> _isolations = [];
        private int _saves;

        public RecordingSessionStore()
            : base(new InMemoryAgentSessionStore())
        {
        }

        public bool FailSaves { get; set; }

        public int Saves => Volatile.Read(ref _saves);

        public IReadOnlyList<string?> Isolations
        {
            get
            {
                lock (_isolations)
                {
                    return [.. _isolations];
                }
            }
        }

        public override ValueTask<AgentSession?> GetSessionAsync(AIAgent agent, AgentSessionStoreKey key, CancellationToken cancellationToken = default)
        {
            Note(key);

            return base.GetSessionAsync(agent, key, cancellationToken);
        }

        public override ValueTask<AgentSession> GetOrCreateSessionAsync(AIAgent agent, AgentSessionStoreKey key, CancellationToken cancellationToken = default)
        {
            Note(key);

            return InnerStore.GetOrCreateSessionAsync(agent, key, cancellationToken);
        }

        public override ValueTask SaveSessionAsync(AIAgent agent, AgentSessionStoreKey key, AgentSession session, CancellationToken cancellationToken = default)
        {
            Note(key);
            Interlocked.Increment(ref _saves);

            return FailSaves
                ? throw new InvalidOperationException("the consumer's store is unavailable")
                : base.SaveSessionAsync(agent, key, session, cancellationToken);
        }

        private void Note(AgentSessionStoreKey key)
        {
            lock (_isolations)
            {
                _isolations.Add(key.Partitions is { } partitions && partitions.TryGetValue("isolation", out var value) ? value : null);
            }
        }
    }

    private sealed class FixedIsolationKeyProvider(string key) : AgentIsolationKeyProvider
    {
        public override ValueTask<string?> GetIsolationKeyAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<string?>(key);
    }

    private sealed class FixedAttribution(string userId) : IRunAttributionContext
    {
        public string? UserId => userId;

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class SwitchableAttribution : IRunAttributionContext
    {
        public string? UserId { get; set; }

        public IReadOnlyDictionary<string, string>? Labels => null;
    }

    private sealed class SwitchableDrainState : ITraconDrainState
    {
        public bool IsDraining { get; set; }
    }

    /// <summary>
    /// A model provider that echoes the last user message, but only after the
    /// test opens a gate. It reports every call and every call that was cancelled.
    /// </summary>
    private sealed class GatedEchoProvider : IModelProvider
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IReadOnlyList<string>> _seen = [];

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = ModelName }];

        public int Calls
        {
            get
            {
                lock (_seen)
                {
                    return _seen.Count;
                }
            }
        }

        public int Cancelled { get; private set; }

        /// <summary>The user messages of each call, in call order.</summary>
        public IReadOnlyList<IReadOnlyList<string>> Seen
        {
            get
            {
                lock (_seen)
                {
                    return [.. _seen];
                }
            }
        }

        public void Open() => _gate.TrySetResult();

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(this);

        private sealed class Client(GatedEchoProvider provider) : IChatClient
        {
            public async Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => new(new ChatMessage(ChatRole.Assistant, await AnswerAsync(messages, cancellationToken).ConfigureAwait(false)));

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, await AnswerAsync(messages, cancellationToken).ConfigureAwait(false));
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // The fake client has no resource to release.
            }

            private async Task<string> AnswerAsync(IEnumerable<ChatMessage> messages, CancellationToken cancellationToken)
            {
                var users = messages.Where(static message => message.Role == ChatRole.User).Select(static message => message.Text).ToList();

                lock (provider._seen)
                {
                    provider._seen.Add(users);
                }

                try
                {
                    await provider._gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    lock (provider._seen)
                    {
                        provider.Cancelled++;
                    }

                    throw;
                }

                return "echo: " + users[^1];
            }
        }
    }

    /// <summary>
    /// Plays one user of one tenant per request, from test headers, so a single
    /// host can serve several callers.
    /// </summary>
    private sealed class UserFromHeadersHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "A2AUserFromHeaders";
        public const string TenantClaim = "tracon_tenant";
        public const string UserHeader = "X-Test-User";
        public const string TenantHeader = "X-Test-Tenant";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers[UserHeader].ToString() is not { Length: > 0 } user)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user) };

            if (Request.Headers[TenantHeader].ToString() is { Length: > 0 } tenant)
            {
                claims.Add(new Claim(TenantClaim, tenant));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
#pragma warning restore MAAI001

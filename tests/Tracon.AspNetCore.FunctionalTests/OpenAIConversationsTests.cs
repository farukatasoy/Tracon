using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies OpenAI Conversations API compatibility.
/// </summary>
/// <remarks>
/// Protected contract: the stock OpenAI SDK's documented flow — first
/// <c>conversations.create()</c>, then <c>responses.create()</c> with that id —
/// must work end to end.
/// </remarks>
public sealed class OpenAIConversationsTests
{
    private static readonly Uri Conversations = new("/tracon/v1/conversations", UriKind.Relative);
    private static readonly Uri Responses = new("/tracon/v1/responses", UriKind.Relative);

    [Fact]
    public async Task Conversation_is_created_and_returns_conv_prefixed_id()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Conversations, new { });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("object").GetString().ShouldBe("conversation");
        json.GetProperty("id").GetString().ShouldStartWith("conv_");
        json.GetProperty("created_at").GetInt64().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Conversation_metadata_is_returned()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Conversations,
            new { metadata = new { customer = "acme" } });

        (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("metadata").GetProperty("customer").GetString().ShouldBe("acme");
    }

    [Fact]
    public async Task Created_id_can_be_used_in_a_responses_call()
    {
        // SDK's documented flow: create -> responses.create(conversation=id)
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }))
        {
            created.EnsureSuccessStatusCode();
            conversationId = (await TraconTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", conversation = conversationId, input = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("conversation").GetProperty("id").GetString().ShouldBe(conversationId);
    }

    [Fact]
    public async Task Conversation_items_return_the_chat_history()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "hello");

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/v1/conversations/{conversationId}/items", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("object").GetString().ShouldBe("list");
        json.GetProperty("has_more").GetBoolean().ShouldBeFalse();

        var items = json.GetProperty("data").EnumerateArray().ToList();
        items.Count.ShouldBeGreaterThanOrEqualTo(2);

        // The user message carries input_text, the assistant reply carries output_text.
        var user = items.First(static item => string.Equals(item.GetProperty("role").GetString(), "user", StringComparison.Ordinal));
        user.GetProperty("type").GetString().ShouldBe("message");
        user.GetProperty("content")[0].GetProperty("type").GetString().ShouldBe("input_text");
        user.GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("hello");

        var assistant = items.First(static item => string.Equals(item.GetProperty("role").GetString(), "assistant", StringComparison.Ordinal));
        assistant.GetProperty("content")[0].GetProperty("type").GetString().ShouldBe("output_text");
        assistant.GetProperty("content")[0].GetProperty("text").GetString().ShouldBe("Echo: hello");
    }

    [Fact]
    public async Task Item_list_applies_the_limit()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "hello");

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/v1/conversations/{conversationId}/items?limit=1", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("data").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Not_yet_used_conversation_returns_empty()
    {
        // In Tracon, POST /v1/conversations RESERVES an id; the session is
        // born on the first /v1/responses call. So an unused conversation
        // returns empty, not 404. This is the one behavior difference from
        // real OpenAI.
        await using var host = await TraconTestHost.StartAsync();

        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }))
        {
            conversationId = (await TraconTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var retrieved = await host.Client.GetAsync(
            new Uri($"/tracon/v1/conversations/{conversationId}", UriKind.Relative));

        retrieved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(retrieved)).GetProperty("id").GetString().ShouldBe(conversationId);

        using var items = await host.Client.GetAsync(
            new Uri($"/tracon/v1/conversations/{conversationId}/items", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(items)).GetProperty("data").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Conversation_is_deleted_and_the_session_goes_with_it()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "hello");

        using (var deleted = await host.Client.DeleteAsync(
            new Uri($"/tracon/v1/conversations/{conversationId}", UriKind.Relative)))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.OK);

            var json = await TraconTestHost.ReadJsonAsync(deleted);
            json.GetProperty("object").GetString().ShouldBe("conversation.deleted");
            json.GetProperty("deleted").GetBoolean().ShouldBeTrue();
        }

        // The conversation and the session are the same thing; deletion must
        // also be visible from the management API.
        using var session = await host.Client.GetAsync(
            new Uri($"/tracon/api/sessions/{conversationId}", UriKind.Relative));

        session.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Conversation_and_session_show_the_same_fact()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "hello");

        using var session = await host.Client.GetAsync(
            new Uri($"/tracon/api/sessions/{conversationId}", UriKind.Relative));

        session.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(session))
            .GetProperty("id").GetString().ShouldBe(conversationId);
    }

    [Fact]
    public async Task Tool_calls_appear_as_an_item()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "hello");

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/v1/conversations/{conversationId}/items", UriKind.Relative));

        var types = (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("data")
            .EnumerateArray()
            .Select(static item => item.GetProperty("type").GetString())
            .ToList();

        // The echoing provider does not call a tool; in this scenario there is
        // only a message item. What matters is that the type field is present
        // on every item.
        types.ShouldAllBe(static type => type != null);
        types.ShouldContain(static type => string.Equals(type, "message", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_body_returns_an_OpenAI_shaped_error()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var content = new StringContent("{broken", System.Text.Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync(Conversations, content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("error").GetProperty("type").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Fact]
    public async Task A_body_far_larger_than_any_metadata_is_refused_without_being_parsed()
    {
        await using var host = await TraconTestHost.StartAsync();

        var oversize = "{\"metadata\":{\"k\":\"" + new string('x', 200_000) + "\"}}";

        using var content = new StringContent(oversize, System.Text.Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync(Conversations, content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("error").GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("too large", Case.Sensitive);
    }

    [Fact]
    public async Task Metadata_within_the_limit_is_still_kept()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Conversations, new { metadata = new { topic = "billing" } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("metadata").GetProperty("topic").GetString().ShouldBe("billing");
    }

    /// <summary>Opens a conversation, runs one turn, and returns the conversation id.</summary>
    private static async Task<string> CreateAndRunAsync(TraconTestHost host, string message)
    {
        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }))
        {
            created.EnsureSuccessStatusCode();
            conversationId = (await TraconTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var run = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", conversation = conversationId, input = message });

        run.EnsureSuccessStatusCode();

        return conversationId;
    }
}

/// <summary>
/// A conversation id is local to the calling tenant: another tenant's id behaves
/// like an id nobody has used.
/// </summary>
/// <remarks>
/// <c>ISessionStore.GetAsync</c> filters by the ambient tenant and the sessions
/// table keys a row by tenant AND id, so two tenants can hold the same id. The
/// endpoints therefore never look across tenants: an id held only by another
/// tenant is a reservation like any other, and a lookup that did cross tenants
/// would both answer wrongly (the caller's own session could read as someone
/// else's) and confirm that the id exists elsewhere.
/// </remarks>
public sealed class OpenAIConversationsCrossTenantTests
{
    [Fact]
    public async Task A_responses_call_with_another_tenants_conversation_id_opens_a_fresh_session_in_the_callers_tenant()
    {
        await using var host = await OpenAITenantHost.StartAsync();

        var conversationId = await OpenAITenantHost.CreateAndRunAsync(host, OpenAITenantHost.Alfa, "This is my private chat.");
        var alfaBefore = await OpenAITenantHost.ItemTextsAsync(host, OpenAITenantHost.Alfa, conversationId);

        using var crossTenant = await host.Client.SendAsync(OpenAITenantHost.Request(
            HttpMethod.Post,
            "/tracon/v1/responses",
            OpenAITenantHost.Beta,
            new { model = "kod-agent", conversation = conversationId, input = "I'm a different tenant." }));

        crossTenant.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Tenant-alfa's session is untouched: same history, no extra turn.
        (await OpenAITenantHost.ItemTextsAsync(host, OpenAITenantHost.Alfa, conversationId)).ShouldBe(alfaBefore);

        // Tenant-beta has a session of its own under the same id, and never saw alfa's text.
        var betaItems = await OpenAITenantHost.ItemTextsAsync(host, OpenAITenantHost.Beta, conversationId);

        OpenAITenantHost.HasText(betaItems, "I'm a different tenant.").ShouldBeTrue();
        OpenAITenantHost.HasText(betaItems, "This is my private chat.").ShouldBeFalse();

        (await OpenAITenantHost.SessionIdsAsync(host, OpenAITenantHost.Alfa)).ShouldBe([conversationId]);
        (await OpenAITenantHost.SessionIdsAsync(host, OpenAITenantHost.Beta)).ShouldBe([conversationId]);
    }

    [Fact]
    public async Task The_conversation_GET_endpoint_answers_another_tenants_id_as_a_reservation()
    {
        await using var host = await OpenAITenantHost.StartAsync();

        var conversationId = await OpenAITenantHost.CreateAndRunAsync(host, OpenAITenantHost.Alfa, "Tenant alfa's chat.");

        using var response = await host.Client.SendAsync(OpenAITenantHost.Request(
            HttpMethod.Get, $"/tracon/v1/conversations/{conversationId}", OpenAITenantHost.Beta, body: null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("id").GetString().ShouldBe(conversationId);
    }

    [Fact]
    public async Task The_conversation_delete_endpoint_deletes_nothing_for_another_tenants_id_and_the_owners_session_remains()
    {
        await using var host = await OpenAITenantHost.StartAsync();

        var conversationId = await OpenAITenantHost.CreateAndRunAsync(host, OpenAITenantHost.Alfa, "Tenant alfa's chat.");

        using var deleted = await host.Client.SendAsync(OpenAITenantHost.Request(
            HttpMethod.Delete, $"/tracon/v1/conversations/{conversationId}", OpenAITenantHost.Beta, body: null));

        deleted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(deleted)).GetProperty("deleted").GetBoolean().ShouldBeFalse();

        using var owned = await host.Client.SendAsync(OpenAITenantHost.Request(
            HttpMethod.Get, $"/tracon/api/sessions/{conversationId}", OpenAITenantHost.Alfa, body: null));

        owned.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_item_list_is_empty_for_another_tenants_id()
    {
        await using var host = await OpenAITenantHost.StartAsync();

        var conversationId = await OpenAITenantHost.CreateAndRunAsync(host, OpenAITenantHost.Alfa, "Tenant alfa's private chat.");

        using var response = await host.Client.SendAsync(OpenAITenantHost.Request(
            HttpMethod.Get, $"/tracon/v1/conversations/{conversationId}/items", OpenAITenantHost.Beta, body: null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("data").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task A_previous_response_id_from_another_tenant_starts_a_fresh_session_and_leaves_the_original_alone()
    {
        await using var host = await OpenAITenantHost.StartAsync();

        // Without a conversation the session is stored under the response id.
        using var first = await host.Client.SendAsync(OpenAITenantHost.Request(
            HttpMethod.Post,
            "/tracon/v1/responses",
            OpenAITenantHost.Alfa,
            new { model = "kod-agent", input = "alfa first" }));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var responseId = (await TraconTestHost.ReadJsonAsync(first)).GetProperty("id").GetString()!;
        var alfaBefore = await OpenAITenantHost.ItemTextsAsync(host, OpenAITenantHost.Alfa, responseId);

        OpenAITenantHost.HasText(alfaBefore, "alfa first").ShouldBeTrue();

        using var crossTenant = await host.Client.SendAsync(OpenAITenantHost.Request(
            HttpMethod.Post,
            "/tracon/v1/responses",
            OpenAITenantHost.Beta,
            new { model = "kod-agent", previous_response_id = responseId, input = "beta follow-up" }));

        crossTenant.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Alfa's session is untouched; beta never loaded it, and saved its turn under its own response id.
        (await OpenAITenantHost.ItemTextsAsync(host, OpenAITenantHost.Alfa, responseId)).ShouldBe(alfaBefore);
        (await OpenAITenantHost.ItemTextsAsync(host, OpenAITenantHost.Beta, responseId)).ShouldBeEmpty();

        (await OpenAITenantHost.SessionIdsAsync(host, OpenAITenantHost.Alfa)).ShouldBe([responseId]);

        var betaSessions = await OpenAITenantHost.SessionIdsAsync(host, OpenAITenantHost.Beta);

        betaSessions.Count.ShouldBe(1);
        string.Equals(betaSessions[0], responseId, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task An_id_held_by_another_tenant_and_an_id_nobody_holds_get_the_same_answer()
    {
        // The existence oracle: if the two answers differed, tenant-beta could
        // probe which ids tenant-alfa holds.
        await using var host = await OpenAITenantHost.StartAsync();

        var held = await OpenAITenantHost.CreateAndRunAsync(host, OpenAITenantHost.Alfa, "Tenant alfa's chat.");
        const string Unheld = "conv_never_used_by_anyone";

        // The one field the comparison below masks is the creation time, and it is
        // also where alfa's record would show through. Check it separately on a
        // session alfa holds from an hour ago: beta's answer must be the current
        // time, not the time alfa's session was born.
        const string Old = "conv_alfa_from_an_hour_ago";
        var bornAt = DateTimeOffset.UtcNow.AddHours(-1);

        await host.Services.GetRequiredService<ISessionStore>().SaveAsync(new SessionRecord
        {
            Id = Old,
            AgentName = "kod-agent",
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = bornAt,
            UpdatedAt = bornAt,
            TenantId = OpenAITenantHost.Alfa,
        });

        (await OpenAITenantHost.CreatedAtAsync(host, OpenAITenantHost.Alfa, Old)).ShouldBe(bornAt.ToUnixTimeSeconds());
        (await OpenAITenantHost.CreatedAtAsync(host, OpenAITenantHost.Beta, Old)).ShouldBeGreaterThan(bornAt.ToUnixTimeSeconds() + 3000);

        foreach (var (method, suffix) in new[] { (HttpMethod.Get, string.Empty), (HttpMethod.Get, "/items"), (HttpMethod.Delete, string.Empty) })
        {
            var heldAnswer = await OpenAITenantHost.AnswerAsync(host, method, $"/tracon/v1/conversations/{held}{suffix}", held);
            var unheldAnswer = await OpenAITenantHost.AnswerAsync(host, method, $"/tracon/v1/conversations/{Unheld}{suffix}", Unheld);

            heldAnswer.ShouldBe(unheldAnswer, $"{method} {suffix}");
        }
    }
}

/// <summary>
/// Two tenants may hold the same conversation id; each reaches only its own session.
/// </summary>
public sealed class OpenAIConversationsSharedIdTests
{
    private const string SharedId = "shared-x";

    [Fact]
    public async Task Each_tenant_reads_writes_and_deletes_only_its_own_session_under_a_shared_id()
    {
        await using var host = await OpenAITenantHost.StartAsync();

        await OpenAITenantHost.RunAsync(host, OpenAITenantHost.Alfa, SharedId, "alfa speaking");
        await OpenAITenantHost.RunAsync(host, OpenAITenantHost.Beta, SharedId, "beta speaking");

        foreach (var tenant in new[] { OpenAITenantHost.Alfa, OpenAITenantHost.Beta })
        {
            using var retrieved = await host.Client.SendAsync(OpenAITenantHost.Request(
                HttpMethod.Get, $"/tracon/v1/conversations/{SharedId}", tenant, body: null));

            retrieved.StatusCode.ShouldBe(HttpStatusCode.OK, tenant);
            (await OpenAITenantHost.SessionIdsAsync(host, tenant)).ShouldBe([SharedId], tenant);
        }

        var alfaItems = await OpenAITenantHost.ItemTextsAsync(host, OpenAITenantHost.Alfa, SharedId);
        var betaItems = await OpenAITenantHost.ItemTextsAsync(host, OpenAITenantHost.Beta, SharedId);

        OpenAITenantHost.HasText(alfaItems, "alfa speaking").ShouldBeTrue();
        OpenAITenantHost.HasText(alfaItems, "beta speaking").ShouldBeFalse();
        OpenAITenantHost.HasText(betaItems, "beta speaking").ShouldBeTrue();
        OpenAITenantHost.HasText(betaItems, "alfa speaking").ShouldBeFalse();

        // A second turn by alfa continues alfa's history, not beta's.
        await OpenAITenantHost.RunAsync(host, OpenAITenantHost.Alfa, SharedId, "alfa again");

        var alfaAfter = await OpenAITenantHost.ItemTextsAsync(host, OpenAITenantHost.Alfa, SharedId);

        OpenAITenantHost.HasText(alfaAfter, "alfa speaking").ShouldBeTrue();
        OpenAITenantHost.HasText(alfaAfter, "alfa again").ShouldBeTrue();
        OpenAITenantHost.HasText(alfaAfter, "beta speaking").ShouldBeFalse();

        // Beta deleting its conversation leaves alfa's.
        using var deleted = await host.Client.SendAsync(OpenAITenantHost.Request(
            HttpMethod.Delete, $"/tracon/v1/conversations/{SharedId}", OpenAITenantHost.Beta, body: null));

        deleted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(deleted)).GetProperty("deleted").GetBoolean().ShouldBeTrue();

        (await OpenAITenantHost.SessionIdsAsync(host, OpenAITenantHost.Beta)).ShouldBeEmpty();
        (await OpenAITenantHost.ItemTextsAsync(host, OpenAITenantHost.Alfa, SharedId)).ShouldBe(alfaAfter);
    }
}

/// <summary>
/// A custom store that ignores the tenant cannot make the endpoints serve another tenant's
/// session: the gate refuses the record it hands back.
/// </summary>
public sealed class OpenAIConversationsTenantBlindStoreTests
{
    private const string BlindId = "blind-id";

    [Fact]
    public async Task A_store_that_returns_another_tenants_record_is_refused_on_every_endpoint()
    {
        var store = new TenantBlindSessionStore();

        await store.SaveAsync(new SessionRecord
        {
            Id = BlindId,
            AgentName = "kod-agent",
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TenantId = OpenAITenantHost.Alfa,
        });

        await using var host = await TraconTestHost.StartAsync(
            static builder => builder
                .AddAgent(TestData.Definition())
                .UseTenancy(static options =>
                {
                    options.Enabled = true;
                    options.AllowHeaderResolution = true;
                }),
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<ISessionStore>(store)));

        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, $"/tracon/v1/conversations/{BlindId}", null),
            (HttpMethod.Get, $"/tracon/v1/conversations/{BlindId}/items", null),
            (HttpMethod.Delete, $"/tracon/v1/conversations/{BlindId}", null),
            (HttpMethod.Post, "/tracon/v1/responses", new { model = "kod-agent", conversation = BlindId, input = "hello" }),
        })
        {
            using var response = await host.Client.SendAsync(OpenAITenantHost.Request(method, path, OpenAITenantHost.Beta, body));

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{method} {path}");
        }

        // The record is the other tenant's and stays exactly as it was.
        var record = await store.GetAsync(BlindId);

        record.ShouldNotBeNull().TenantId.ShouldBe(OpenAITenantHost.Alfa);
    }

    /// <summary>A store keyed by the identifier alone, as a custom store written without tenancy in mind would be.</summary>
    private sealed class TenantBlindSessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<string, SessionRecord> _records = new(StringComparer.Ordinal);

        public ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default)
        {
            _records[record.Id] = record;
            return default;
        }

        public ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
            => new(_records.TryAdd(record.Id, record));

        public ValueTask<bool> TryUpdateAsync(SessionRecord record, long expectedVersion, CancellationToken cancellationToken = default)
            => new(_records.TryGetValue(record.Id, out var existing) &&
                   existing.Version == expectedVersion &&
                   _records.TryUpdate(record.Id, record with { Version = expectedVersion + 1 }, existing));

        public ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
            => new(_records.GetValueOrDefault(sessionId));

        public ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => new(_records.TryRemove(sessionId, out _));

        public ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default)
            => new((IReadOnlyList<SessionRecord>)_records.Values.ToList());
    }
}

/// <summary>The two-tenant host and the request helpers the conversation tenant tests share.</summary>
internal static class OpenAITenantHost
{
    public const string Alfa = "tenant-alfa";
    public const string Beta = "tenant-beta";

    private const string TenantHeader = "X-Tracon-Tenant";

    public static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition())
            .UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

    /// <summary>Reserves a conversation id, runs one turn on it, and returns the id.</summary>
    public static async Task<string> CreateAndRunAsync(TraconTestHost host, string tenant, string message)
    {
        using var created = await host.Client.SendAsync(
            Request(HttpMethod.Post, "/tracon/v1/conversations", tenant, new { }));

        created.EnsureSuccessStatusCode();
        var conversationId = (await TraconTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;

        await RunAsync(host, tenant, conversationId, message);

        return conversationId;
    }

    /// <summary>Runs one turn on a conversation id.</summary>
    public static async Task RunAsync(TraconTestHost host, string tenant, string conversationId, string message)
    {
        using var run = await host.Client.SendAsync(Request(
            HttpMethod.Post,
            "/tracon/v1/responses",
            tenant,
            new { model = "kod-agent", conversation = conversationId, input = message }));

        run.EnsureSuccessStatusCode();
    }

    /// <summary>The texts of a conversation's items, oldest first.</summary>
    public static async Task<IReadOnlyList<string>> ItemTextsAsync(TraconTestHost host, string tenant, string conversationId)
    {
        using var response = await host.Client.SendAsync(Request(
            HttpMethod.Get, $"/tracon/v1/conversations/{conversationId}/items", tenant, body: null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, tenant);

        return (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("data")
            .EnumerateArray()
            .Select(static item => item.GetProperty("content")[0].GetProperty("text").GetString()!)
            .ToList();
    }

    /// <summary>The <c>created_at</c> that <c>GET /v1/conversations/{id}</c> reports to a tenant.</summary>
    public static async Task<long> CreatedAtAsync(TraconTestHost host, string tenant, string conversationId)
    {
        using var response = await host.Client.SendAsync(Request(
            HttpMethod.Get, $"/tracon/v1/conversations/{conversationId}", tenant, body: null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, tenant);

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("created_at").GetInt64();
    }

    /// <summary>Whether any of the texts is exactly <paramref name="text"/>.</summary>
    public static bool HasText(IEnumerable<string> texts, string text)
        => texts.Any(candidate => string.Equals(candidate, text, StringComparison.Ordinal));

    /// <summary>The ids of the sessions the management API lists for a tenant.</summary>
    public static async Task<IReadOnlyList<string>> SessionIdsAsync(TraconTestHost host, string tenant)
    {
        using var response = await host.Client.SendAsync(Request(HttpMethod.Get, "/tracon/api/sessions", tenant, body: null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK, tenant);

        return (await TraconTestHost.ReadJsonAsync(response))
            .EnumerateArray()
            .Select(static session => session.GetProperty("id").GetString()!)
            .ToList();
    }

    /// <summary>
    /// What tenant-beta is told: the status and the body with the requested id and the
    /// creation time (the current time, for a reservation) masked.
    /// </summary>
    public static async Task<string> AnswerAsync(TraconTestHost host, HttpMethod method, string path, string id)
    {
        using var response = await host.Client.SendAsync(Request(method, path, Beta, body: null));

        var body = (await response.Content.ReadAsStringAsync())
            .Replace(id, "<id>", StringComparison.Ordinal);

        body = Regex.Replace(
            body, "\"created_at\":\\d+", "\"created_at\":<now>", RegexOptions.None, TimeSpan.FromSeconds(1));

        return $"{(int)response.StatusCode} {body}";
    }

    public static HttpRequestMessage Request(HttpMethod method, string path, string tenant, object? body)
    {
        var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        request.Headers.Add(TenantHeader, tenant);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }
}

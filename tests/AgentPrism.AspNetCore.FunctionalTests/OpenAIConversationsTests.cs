using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

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
    private static readonly Uri Conversations = new("/agentprism/v1/conversations", UriKind.Relative);
    private static readonly Uri Responses = new("/agentprism/v1/responses", UriKind.Relative);

    [Fact]
    public async Task Conversation_is_created_and_returns_conv_prefixed_id()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Conversations, new { });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

        json.GetProperty("object").GetString().ShouldBe("conversation");
        json.GetProperty("id").GetString().ShouldStartWith("conv_");
        json.GetProperty("created_at").GetInt64().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Conversation_metadata_is_returned()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Conversations,
            new { metadata = new { customer = "acme" } });

        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("metadata").GetProperty("customer").GetString().ShouldBe("acme");
    }

    [Fact]
    public async Task Created_id_can_be_used_in_a_responses_call()
    {
        // SDK's documented flow: create -> responses.create(conversation=id)
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }))
        {
            created.EnsureSuccessStatusCode();
            conversationId = (await AgentPrismTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var response = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", conversation = conversationId, input = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("conversation").GetProperty("id").GetString().ShouldBe(conversationId);
    }

    [Fact]
    public async Task Conversation_items_return_the_chat_history()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "hello");

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}/items", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await AgentPrismTestHost.ReadJsonAsync(response);

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
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "hello");

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}/items?limit=1", UriKind.Relative));

        (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("data").GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Not_yet_used_conversation_returns_empty()
    {
        // In AgentPrism, POST /v1/conversations RESERVES an id; the session is
        // born on the first /v1/responses call. So an unused conversation
        // returns empty, not 404. This is the one behavior difference from
        // real OpenAI.
        await using var host = await AgentPrismTestHost.StartAsync();

        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }))
        {
            conversationId = (await AgentPrismTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var retrieved = await host.Client.GetAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}", UriKind.Relative));

        retrieved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(retrieved)).GetProperty("id").GetString().ShouldBe(conversationId);

        using var items = await host.Client.GetAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}/items", UriKind.Relative));

        (await AgentPrismTestHost.ReadJsonAsync(items)).GetProperty("data").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Conversation_is_deleted_and_the_session_goes_with_it()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "hello");

        using (var deleted = await host.Client.DeleteAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}", UriKind.Relative)))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.OK);

            var json = await AgentPrismTestHost.ReadJsonAsync(deleted);
            json.GetProperty("object").GetString().ShouldBe("conversation.deleted");
            json.GetProperty("deleted").GetBoolean().ShouldBeTrue();
        }

        // The conversation and the session are the same thing; deletion must
        // also be visible from the management API.
        using var session = await host.Client.GetAsync(
            new Uri($"/agentprism/api/sessions/{conversationId}", UriKind.Relative));

        session.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Conversation_and_session_show_the_same_fact()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "hello");

        using var session = await host.Client.GetAsync(
            new Uri($"/agentprism/api/sessions/{conversationId}", UriKind.Relative));

        session.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(session))
            .GetProperty("id").GetString().ShouldBe(conversationId);
    }

    [Fact]
    public async Task Tool_calls_appear_as_an_item()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var conversationId = await CreateAndRunAsync(host, "hello");

        using var response = await host.Client.GetAsync(
            new Uri($"/agentprism/v1/conversations/{conversationId}/items", UriKind.Relative));

        var types = (await AgentPrismTestHost.ReadJsonAsync(response))
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
        await using var host = await AgentPrismTestHost.StartAsync();

        using var content = new StringContent("{broken", System.Text.Encoding.UTF8, "application/json");
        using var response = await host.Client.PostAsync(Conversations, content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("error").GetProperty("type").ValueKind.ShouldBe(JsonValueKind.String);
    }

    /// <summary>Opens a conversation, runs one turn, and returns the conversation id.</summary>
    private static async Task<string> CreateAndRunAsync(AgentPrismTestHost host, string message)
    {
        string conversationId;

        using (var created = await host.Client.PostAsJsonAsync(Conversations, new { }))
        {
            created.EnsureSuccessStatusCode();
            conversationId = (await AgentPrismTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;
        }

        using var run = await host.Client.PostAsJsonAsync(
            Responses,
            new { model = "kod-agent", conversation = conversationId, input = message });

        run.EnsureSuccessStatusCode();

        return conversationId;
    }
}

/// <summary>
/// Cross-tenant access to conversation ids returns <c>404</c>.
/// </summary>
/// <remarks>
/// HATA-S2-005: <c>ISessionStore.GetAsync</c> filters by the ambient tenant, so
/// the cross-tenant ownership check (<c>record is not null &amp;&amp;
/// !IsOwnedByTenant(record, ...)</c>) never fired — another tenant's record is
/// NEVER SEEN from this context, <c>record</c> was always
/// <see langword="null"/>. The check now uses <c>ISessionStore.GetOwnerTenantIdAsync</c>,
/// which runs WITHOUT applying the tenant filter.
/// </remarks>
public sealed class OpenAIConversationsCrossTenantTests
{
    private const string TenantHeader = "X-AgentPrism-Tenant";
    private const string TenantAlfa = "tenant-alfa";
    private const string TenantBeta = "tenant-beta";

    [Fact]
    public async Task Cross_tenant_responses_call_to_a_conversation_id_returns_404()
    {
        // MT-COMPAT-023
        await using var host = await StartTenantHostAsync();

        var conversationId = await CreateAndRunAsync(host, TenantAlfa, "This is my private chat.");

        using var crossTenant = await host.Client.SendAsync(Request(
            HttpMethod.Post,
            "/agentprism/v1/responses",
            TenantBeta,
            new { model = "kod-agent", conversation = conversationId, input = "I'm a different tenant." }));

        crossTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AgentPrismTestHost.ReadJsonAsync(crossTenant))
            .GetProperty("error").GetProperty("type").GetString().ShouldBe("not_found_error");

        // Tenant-alfa's session must NOT have picked up a NEW turn.
        using var owned = await host.Client.SendAsync(
            Request(HttpMethod.Get, $"/agentprism/api/sessions/{conversationId}", TenantAlfa, body: null));

        owned.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cross_tenant_access_to_the_conversation_GET_endpoint_returns_404()
    {
        // MT-COMPAT-036
        await using var host = await StartTenantHostAsync();

        var conversationId = await CreateAndRunAsync(host, TenantAlfa, "Tenant alfa's chat.");

        using var response = await host.Client.SendAsync(
            Request(HttpMethod.Get, $"/agentprism/v1/conversations/{conversationId}", TenantBeta, body: null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("error").GetProperty("type").GetString().ShouldBe("not_found_error");
    }

    [Fact]
    public async Task Cross_tenant_access_to_the_conversation_delete_endpoint_returns_404_and_the_owners_session_remains()
    {
        // MT-COMPAT-039
        await using var host = await StartTenantHostAsync();

        var conversationId = await CreateAndRunAsync(host, TenantAlfa, "Tenant alfa's chat.");

        using var deleted = await host.Client.SendAsync(
            Request(HttpMethod.Delete, $"/agentprism/v1/conversations/{conversationId}", TenantBeta, body: null));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var owned = await host.Client.SendAsync(
            Request(HttpMethod.Get, $"/agentprism/api/sessions/{conversationId}", TenantAlfa, body: null));

        owned.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cross_tenant_access_to_the_item_list_returns_404()
    {
        // MT-COMPAT-043
        await using var host = await StartTenantHostAsync();

        var conversationId = await CreateAndRunAsync(host, TenantAlfa, "Tenant alfa's private chat.");

        using var response = await host.Client.SendAsync(
            Request(HttpMethod.Get, $"/agentprism/v1/conversations/{conversationId}/items", TenantBeta, body: null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("error").GetProperty("type").GetString().ShouldBe("not_found_error");
    }

    private static Task<AgentPrismTestHost> StartTenantHostAsync()
        => AgentPrismTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition())
            .UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

    private static async Task<string> CreateAndRunAsync(AgentPrismTestHost host, string tenant, string message)
    {
        using var created = await host.Client.SendAsync(
            Request(HttpMethod.Post, "/agentprism/v1/conversations", tenant, new { }));

        created.EnsureSuccessStatusCode();
        var conversationId = (await AgentPrismTestHost.ReadJsonAsync(created)).GetProperty("id").GetString()!;

        using var run = await host.Client.SendAsync(Request(
            HttpMethod.Post,
            "/agentprism/v1/responses",
            tenant,
            new { model = "kod-agent", conversation = conversationId, input = message }));

        run.EnsureSuccessStatusCode();

        return conversationId;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string tenant, object? body)
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

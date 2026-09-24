using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 6 endpoints: telemetry, tool usage, tenants, MCP servers, and approval rules.
/// </summary>
public sealed class GovernanceEndpointTests
{
    [Fact]
    public async Task Missing_trace_returns_404_with_a_reason()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{Guid.NewGuid()}/trace", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 404 here is not an error, it is the result of SAMPLING; the
        // reason must be written in the response so the operator doesn't
        // hunt for a bug in vain.
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("SuccessSampleRatio");
    }

    [Fact]
    public async Task Tool_usage_starts_empty()
    {
        await using var host = await TraconTestHost.StartAsync();

        var usage = await host.Client.GetFromJsonAsync<List<ToolUsage>>(
            new Uri("/tracon/api/tools/usage", UriKind.Relative));

        usage.ShouldNotBeNull();
        usage.ShouldBeEmpty();
    }

    [Fact]
    public async Task Run_tool_calls_are_listed()
    {
        await using var host = await TraconTestHost.StartAsync();

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "support",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        await runs.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = runId,
            ToolName = "get_order_status",
            Duration = TimeSpan.FromMilliseconds(42),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var invocations = await host.Client.GetFromJsonAsync<List<ToolInvocationRecord>>(
            new Uri($"/tracon/api/runs/{runId}/tools", UriKind.Relative));

        invocations.ShouldNotBeNull();
        invocations.ShouldHaveSingleItem().ToolName.ShouldBe("get_order_status");
    }

    [Fact]
    public async Task Valid_tenant_is_returned()
    {
        await using var host = await TraconTestHost.StartAsync();

        var current = await host.Client.GetFromJsonAsync<CurrentTenantResponse>(
            new Uri("/tracon/api/tenants/current", UriKind.Relative));

        current.ShouldNotBeNull();
        current.TenantId.ShouldBe("default");
    }

    [Fact]
    public async Task Tenant_record_is_written_and_deleted()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/acme", UriKind.Relative),
            new { displayName = "Acme Inc." });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tenants = await host.Client.GetFromJsonAsync<List<TenantDescriptor>>(
            new Uri("/tracon/api/tenants", UriKind.Relative));

        tenants.ShouldNotBeNull();
        tenants.ShouldHaveSingleItem().Slug.ShouldBe("acme");

        using var deleted = await host.Client.DeleteAsync(
            new Uri("/tracon/api/tenants/acme", UriKind.Relative));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Invalid_tenant_key_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/tenants/has%20space", UriKind.Relative),
            new { displayName = "Invalid" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Mcp_server_is_written_and_listed()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/mcp-servers/github", UriKind.Relative),
            new
            {
                endpoint = "https://mcp.example.com/mcp",
                transport = "StreamableHttp",
                authorizationConfigurationKey = "Tracon:McpSecrets:GithubToken",
                enabled = true,
                requiresApproval = true,
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var servers = await host.Client.GetFromJsonAsync<List<McpServerDefinition>>(
            new Uri("/tracon/api/mcp-servers", UriKind.Relative));

        servers.ShouldNotBeNull();

        var server = servers.ShouldHaveSingleItem();

        server.Name.ShouldBe("github");
        server.RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public async Task Mcp_response_carries_no_secret()
    {
        // The server record never carries the authentication VALUE; it
        // only carries the NAME of the configuration key the value will be
        // read from (decision K-059).
        //
        // The test verifies this by sending an 'authorization' field that
        // does NOT exist in the contract: the field does not bind, is not
        // stored, and never comes back in any response.
        const string ValueThatMustNotLeak = "VERY-SECRET-VALUE-TEST";

        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/mcp-servers/github", UriKind.Relative),
            new
            {
                endpoint = "https://mcp.example.com/mcp",
                authorizationConfigurationKey = "Tracon:McpSecrets:GithubToken",
                authorization = ValueThatMustNotLeak,
                headers = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["X-Server"] = "example",
                },
                enabled = true,
                requiresApproval = true,
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await saved.Content.ReadAsStringAsync()).ShouldNotContain(ValueThatMustNotLeak);

        using var listed = await host.Client.GetAsync(
            new Uri("/tracon/api/mcp-servers", UriKind.Relative));

        (await listed.Content.ReadAsStringAsync()).ShouldNotContain(ValueThatMustNotLeak);
    }

    [Fact]
    public async Task Mcp_header_values_are_masked_in_every_response_and_kept_in_the_store()
    {
        // 🚨 A header value is free text an operator typed: nothing stops it
        // from being an API key. The list is open to every Reader and every
        // AgentsRead key, who could then call the MCP server directly - past
        // the approval gate. The mask covers EVERY header, not only the
        // names that look like credentials: a name heuristic misses X-Auth.
        const string ApiKey = "sk-live-very-secret";
        const string PlainValue = "plain-trace-value";

        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/mcp-servers/github", UriKind.Relative),
            new
            {
                endpoint = "https://mcp.example.com/mcp",
                headers = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["X-Session-Id"] = ApiKey,
                    ["X-Trace"] = PlainValue,
                },
                enabled = true,
                requiresApproval = true,
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var listed = await host.Client.GetAsync(new Uri("/tracon/api/mcp-servers", UriKind.Relative));

        foreach (var body in new[] { await saved.Content.ReadAsStringAsync(), await listed.Content.ReadAsStringAsync() })
        {
            body.ShouldNotContain(ApiKey);
            body.ShouldNotContain(PlainValue);
        }

        var server = (await listed.Content.ReadFromJsonAsync<List<McpServerDefinition>>())!.ShouldHaveSingleItem();
        server.Headers.ShouldBe(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Session-Id"] = "***", ["X-Trace"] = "***" },
            ignoreOrder: true);

        // The mask is a RESPONSE rule only: the stored row keeps the value the
        // transport sends to the MCP server.
        var stored = await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "github");
        stored!.Headers["X-Session-Id"].ShouldBe(ApiKey);
    }

    [Fact]
    public async Task Mcp_audit_trail_records_header_names_but_no_values()
    {
        const string ApiKey = "plain-190";

        await using var host = await TraconTestHost.StartAsync();

        // X-Tenant: a name neither the audit secret filter nor the credential
        // header rule recognizes, so its value is stored as sent (phase 190
        // moved credential names out of plain headers; the mask covers the rest).
        foreach (var value in new[] { ApiKey, ApiKey + "-rotated" })
        {
            using var saved = await host.Client.PutAsJsonAsync(
                new Uri("/tracon/api/mcp-servers/apim", UriKind.Relative),
                new
                {
                    endpoint = "https://mcp.example.com/mcp",
                    headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Tenant"] = value },
                    enabled = true,
                    requiresApproval = true,
                });

            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var entries = await host.Services.GetRequiredService<IAuditLog>()
            .QueryAsync(new AuditQuery { Entity = "mcp:apim" });

        entries.Count.ShouldBe(2);

        foreach (var entry in entries)
        {
            $"{entry.Before}{entry.After}".ShouldNotContain(ApiKey);
            entry.After.ShouldNotBeNull().ShouldContain("X-Tenant");
        }
    }

    [Fact]
    public async Task Mcp_save_that_sends_the_mask_back_is_rejected()
    {
        // A client that reads, edits and writes back would otherwise store
        // "***" over the real value and silently break the server's
        // authentication.
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/mcp-servers/github", UriKind.Relative),
            new
            {
                endpoint = "https://mcp.example.com/mcp",
                headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Api-Key"] = "***" },
                enabled = true,
                requiresApproval = true,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString();
        detail.ShouldNotBeNull().ShouldContain("X-Api-Key");
        detail.ShouldContain("real value");

        (await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "github")).ShouldBeNull();
    }

    [Fact]
    public async Task Stdio_address_is_rejected()
    {
        // Local process transport is a security boundary: someone with
        // access to the interface must not be able to run a program on the server.
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/mcp-servers/local", UriKind.Relative),
            new { endpoint = "file:///usr/local/bin/mcp-server", enabled = true, requiresApproval = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await response.Content.ReadAsStringAsync()).ShouldContain("stdio");
    }

    [Fact]
    public async Task Invalid_address_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/mcp-servers/broken", UriKind.Relative),
            new { endpoint = "this-is-not-an-address", enabled = true, requiresApproval = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_returns_501_when_Mcp_not_registered()
    {
        // Tracon.Mcp is an optional package; if it is not registered,
        // the endpoint explicitly says "not implemented" and does not
        // silently appear to succeed.
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsync(
            new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Mcp_server_is_written_and_listed_with_OAuth_fields()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/mcp-servers/github", UriKind.Relative),
            new
            {
                endpoint = "https://mcp.example.com/mcp",
                transport = "StreamableHttp",
                enabled = true,
                requiresApproval = true,
                oauthEnabled = true,
                oauthClientId = "tracon-client",
                oauthClientSecretConfigurationKey = "Tracon:McpSecrets:GithubClientSecret",
                oauthScopes = "repo read:user",
            });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK);

        var servers = await host.Client.GetFromJsonAsync<List<McpServerDefinition>>(
            new Uri("/tracon/api/mcp-servers", UriKind.Relative));

        var server = servers.ShouldHaveSingleItem();

        server.OAuthEnabled.ShouldBeTrue();
        server.OAuthClientId.ShouldBe("tracon-client");
        server.OAuthClientSecretConfigurationKey.ShouldBe("Tracon:McpSecrets:GithubClientSecret");
        server.OAuthScopes.ShouldBe("repo read:user");
        server.OAuthAuthorizationMode.ShouldBe(McpOAuthAuthorizationMode.AuthorizationCode);
    }

    [Fact]
    public async Task Rejected_when_OAuth_client_id_is_missing()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/mcp-servers/github", UriKind.Relative),
            new { endpoint = "https://mcp.example.com/mcp", enabled = true, oauthEnabled = true });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rejected_when_OAuth_and_static_authorization_header_conflict()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/mcp-servers/github", UriKind.Relative),
            new
            {
                endpoint = "https://mcp.example.com/mcp",
                enabled = true,
                oauthEnabled = true,
                oauthClientId = "tracon-client",
                authorizationConfigurationKey = "Tracon:McpSecrets:GithubToken",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Prompt_and_resource_endpoints_return_501_when_Mcp_not_registered()
    {
        // Prompts/Resources/OAuth (Phase 22) also depend on Tracon.Mcp;
        // if not registered, they must show the same explicit "not
        // implemented" behavior.
        await using var host = await TraconTestHost.StartAsync();

        using var prompts = await host.Client.GetAsync(
            new Uri("/tracon/api/mcp-servers/github/prompts", UriKind.Relative));

        prompts.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        using var resources = await host.Client.GetAsync(
            new Uri("/tracon/api/mcp-servers/github/resources", UriKind.Relative));

        resources.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        using var read = await host.Client.GetAsync(
            new Uri("/tracon/api/mcp-servers/github/resources/read?uri=file:///a", UriKind.Relative));

        read.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        using var oauthStart = await host.Client.PostAsync(
            new Uri("/tracon/api/mcp-servers/github/oauth/start", UriKind.Relative),
            content: null);

        oauthStart.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Mcp_oauth_callback_is_accessible_without_a_bearer_token()
    {
        // The callback endpoint is outside the access layers: the browser
        // the provider redirects cannot carry our bearer token. This
        // request has NO Authorization header at all, and the endpoint
        // still returns 200 (with a failure HTML page, because
        // Tracon.Mcp is not registered in this test host).
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/mcp-servers/github/oauth/callback?code=abc&state=unknown", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");

        var body = await response.Content.ReadAsStringAsync();

        body.ShouldContain("failed");
    }

    [Fact]
    public async Task Approval_rules_are_listed_and_revoked()
    {
        await using var host = await TraconTestHost.StartAsync();

        var store = host.Services.GetRequiredService<IToolApprovalRuleStore>();

        var rule = await store.AddAsync(new ToolApprovalRule
        {
            Id = TraconId.NewId(),
            TenantId = "default",
            AgentName = "support",
            ToolName = "cancel_order",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var rules = await host.Client.GetFromJsonAsync<List<ToolApprovalRule>>(
            new Uri("/tracon/api/approvals/rules", UriKind.Relative));

        rules.ShouldNotBeNull();
        rules.ShouldHaveSingleItem().ToolName.ShouldBe("cancel_order");

        using var deleted = await host.Client.DeleteAsync(
            new Uri($"/tracon/api/approvals/rules/{rule.Id}", UriKind.Relative));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Nonexistent_rule_cannot_be_deleted()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.DeleteAsync(
            new Uri($"/tracon/api/approvals/rules/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Conditioned_approval_rule_is_created_and_read_back()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            ApprovalRulesUri,
            new ToolApprovalRuleRequest
            {
                ToolName = "refund_order",
                ArgumentConditions =
                [
                    new ToolArgumentCondition
                    {
                        Path = "amount",
                        Operator = ToolArgumentOperator.LessThanOrEqual,
                        Value = JsonSerializer.SerializeToElement(100),
                    },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var created = await TraconTestHost.ReadJsonAsync(response);
        created.GetProperty("toolName").GetString().ShouldBe("refund_order");

        // Read the RAW JSON text, not the deserialized C# object: a missing
        // [JsonConverter(typeof(JsonStringEnumConverter<...>))] on
        // ToolArgumentOperator would round-trip fine through matching C# types
        // on both ends and only show up as a wire-level number (K-040's
        // recurring trap, docs/hafiza/aspnetcore-json.md).
        created.GetProperty("argumentConditions")[0].GetProperty("operator").GetString()
            .ShouldBe("LessThanOrEqual");

        var rules = await host.Client.GetFromJsonAsync<List<ToolApprovalRule>>(ApprovalRulesUri);

        var rule = rules.ShouldNotBeNull().ShouldHaveSingleItem();
        var condition = rule.ArgumentConditions.ShouldHaveSingleItem();
        condition.Path.ShouldBe("amount");
        condition.Operator.ShouldBe(ToolArgumentOperator.LessThanOrEqual);
        condition.Value.GetDouble().ShouldBe(100);
    }

    [Fact]
    public async Task Same_scope_and_conditions_written_twice_is_a_conflict()
    {
        await using var host = await TraconTestHost.StartAsync();

        var request = new ToolApprovalRuleRequest
        {
            ToolName = "refund_order",
            ArgumentConditions =
            [
                new ToolArgumentCondition
                {
                    Path = "amount",
                    Operator = ToolArgumentOperator.LessThanOrEqual,
                    Value = JsonSerializer.SerializeToElement(100),
                },
            ],
        };

        using var first = await host.Client.PostAsJsonAsync(ApprovalRulesUri, request);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var second = await host.Client.PostAsJsonAsync(ApprovalRulesUri, request);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Numeric_operator_on_a_text_value_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            ApprovalRulesUri,
            new ToolApprovalRuleRequest
            {
                ToolName = "refund_order",
                ArgumentConditions =
                [
                    new ToolArgumentCondition
                    {
                        Path = "tier",
                        Operator = ToolArgumentOperator.GreaterThan,
                        Value = JsonSerializer.SerializeToElement("gold"),
                    },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reader_role_cannot_create_an_approval_rule()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        using var response = await host.Client.PostAsJsonAsync(
            ApprovalRulesUri,
            new ToolApprovalRuleRequest { ToolName = "refund_order" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static readonly Uri ApprovalRulesUri = new("/tracon/api/approvals/rules", UriKind.Relative);

    [Fact]
    public async Task Empty_run_request_is_rejected()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition("echo")));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/echo/run", UriKind.Relative),
            new { });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await response.Content.ReadAsStringAsync()).ShouldContain("approvals");
    }

    [Fact]
    public async Task Sessionless_approval_request_is_rejected()
    {
        // A pending approval request lives in session history; a
        // sessionless request has nothing to match against.
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition("echo")));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/echo/run", UriKind.Relative),
            new
            {
                approvals = new[] { new { requestId = "req-1", approved = true } },
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await response.Content.ReadAsStringAsync()).ShouldContain("sessionId");
    }
}

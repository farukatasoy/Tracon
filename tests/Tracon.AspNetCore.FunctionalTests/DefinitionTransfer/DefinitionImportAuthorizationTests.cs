using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// Who may read a file, who may try one and who may write it, and what the scopes of a key let it carry.
/// </summary>
public sealed class DefinitionImportAuthorizationTests
{
    [Fact]
    public async Task A_reader_exports_an_operator_tries_an_import_and_only_an_admin_writes_it()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        var bundle = TransferHost.NewBundle().WithAgent(TestData.Request("a"));

        using (var export = await host.Client.GetAsync(TransferHost.Api("definitions/export")))
        {
            export.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var (dry, _) = await host.ImportAsync(bundle, dryRun: true);

        using (dry)
        {
            dry.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Writing needs the Admin role, and the answer does not depend on the file being valid.
        var (write, _) = await host.ImportAsync(bundle, dryRun: false);

        using (write)
        {
            write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await host.AgentExistsAsync("a")).ShouldBeFalse();
    }

    [Fact]
    public async Task A_reader_cannot_even_try_an_import()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(TraconPolicies.Reader, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(TraconPolicies.Operator, static policy => policy.RequireAssertion(static _ => false)));

        var (response, _) = await host.ImportAsync(TransferHost.NewBundle(), dryRun: true);

        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task A_key_that_waives_nothing_cannot_import_a_server_that_waives_approval()
    {
        await using var host = await TraconTestHost.StartAsync();

        var bundle = TransferHost.NewBundle()
            .WithMcpServer("quiet", TransferHost.McpDefinition(requiresApproval: false));

        var plain = await ApiKeyEndpointTests.CreateKeyAsync(host, "agents-admin", "AgentsAdmin");

        UseKey(host, plain.PlaintextKey);

        var denied = await host.ImportOkAsync(bundle, dryRun: true);

        denied["valid"]!.GetValue<bool>().ShouldBeFalse();
        TransferHost.Actions(denied, "mcpServer").ShouldBe(["quiet:rejected"]);
        TransferHost.Item(denied, "mcpServer", "quiet")["messages"]![0]!["code"]!.GetValue<string>().ShouldBe("mcp_approval_waiver_denied");

        // The scope that owns approval policy lets the same file through, as it lets the same save through.
        host.Client.DefaultRequestHeaders.Authorization = null;

        var security = await ApiKeyEndpointTests.CreateKeyAsync(host, "security", "AgentsAdmin", "SecurityAdmin");

        UseKey(host, security.PlaintextKey);

        var allowed = await host.ImportOkAsync(bundle, dryRun: true);

        TransferHost.Actions(allowed, "mcpServer").ShouldBe(["quiet:create"]);
    }

    [Fact]
    public async Task A_file_with_workflows_needs_the_workflow_scope_as_well()
    {
        await using var host = await TraconTestHost.StartAsync();

        var bundle = TransferHost.NewBundle()
            .WithAgent(TestData.Request("a"))
            .WithWorkflow("flow", TransferHost.SequentialWorkflow("a"));

        var agentsOnly = await ApiKeyEndpointTests.CreateKeyAsync(host, "agents-admin", "AgentsAdmin");

        UseKey(host, agentsOnly.PlaintextKey);

        var (denied, body) = await host.ImportAsync(bundle, dryRun: true);

        using (denied)
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden, body?.ToJsonString());
        }

        // The same key carries the agents of the file when the workflows are left out.
        var agents = await host.ImportOkAsync(bundle, dryRun: true, types: "agents");

        TransferHost.Actions(agents, "agent").ShouldBe(["a:create"]);
    }

    [Fact]
    public async Task An_export_that_holds_workflows_needs_the_workflow_scope_as_well()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.CreateAgentAsync(TestData.Request("a"));
        await host.CreateWorkflowAsync("flow", TransferHost.SequentialWorkflow("a"));

        var reader = await ApiKeyEndpointTests.CreateKeyAsync(host, "agents-reader", "AgentsRead");

        UseKey(host, reader.PlaintextKey);

        using (var all = await host.Client.GetAsync(TransferHost.Api("definitions/export")))
        {
            all.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using var agents = await host.Client.GetAsync(TransferHost.Api("definitions/export?types=agents"));

        agents.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static void UseKey(TraconTestHost host, string key)
        => host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
}

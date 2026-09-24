using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A save that leaves a header map out keeps the stored one (phase 190).
/// </summary>
/// <remarks>
/// The admin forms send neither <c>headers</c> nor
/// <c>headerConfigurationKeys</c>, and a read masks the plain values, so a
/// form save used to wipe every stored header — including the credential
/// the server needed. <c>{}</c> is the explicit way to clear a map.
/// </remarks>
public sealed class HeaderPreservationTests
{
    private static readonly Uri Mcp = new("/tracon/api/mcp-servers/m1", UriKind.Relative);
    private static readonly Uri Webhook = new("/tracon/api/webhooks/w1", UriKind.Relative);

    /// <summary>The exact body the MCP form sends (<c>mcp.tsx</c>): no header map at all.</summary>
    private static object McpFormBody(string endpoint = "https://mcp.example.com/mcp") => new
    {
        endpoint,
        transport = "StreamableHttp",
        enabled = true,
        requiresApproval = true,
        oauthEnabled = false,
    };

    /// <summary>The exact body the webhook panel sends (<c>webhook-panel.tsx</c>): no header map at all.</summary>
    private static object WebhookFormBody(string url = "https://example.com/hook") => new
    {
        url,
        events = new[] { "run.completed" },
        enabled = true,
    };

    [Fact]
    public async Task A_form_save_keeps_both_mcp_maps()
    {
        await using var host = await TraconTestHost.StartAsync();

        await SaveAsync(host, Mcp, CredentialHeaderSaveTests.McpBody(
            headers: CredentialHeaderSaveTests.Map(("X-Team", "platform")),
            keys: CredentialHeaderSaveTests.Map(("X-API-Key", "Tracon:McpSecrets:SearchKey"))));

        await SaveAsync(host, Mcp, McpFormBody());

        var stored = await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "m1");

        stored.ShouldNotBeNull();
        stored.Headers["X-Team"].ShouldBe("platform");
        stored.HeaderConfigurationKeys["X-API-Key"].ShouldBe("Tracon:McpSecrets:SearchKey");
    }

    [Fact]
    public async Task A_form_save_keeps_both_webhook_maps()
    {
        await using var host = await TraconTestHost.StartAsync();

        await SaveAsync(host, Webhook, CredentialHeaderSaveTests.WebhookBody(
            headers: CredentialHeaderSaveTests.Map(("X-Team", "platform")),
            keys: CredentialHeaderSaveTests.Map(("X-API-Key", "Tracon:WebhookSecrets:OrdersKey"))));

        await SaveAsync(host, Webhook, WebhookFormBody());

        var stored = await host.Services.GetRequiredService<IWebhookStore>().GetSubscriptionAsync("default", "w1");

        stored.ShouldNotBeNull();
        stored.Headers["X-Team"].ShouldBe("platform");
        stored.HeaderConfigurationKeys["X-API-Key"].ShouldBe("Tracon:WebhookSecrets:OrdersKey");
    }

    [Fact]
    public async Task An_empty_map_clears_it()
    {
        await using var host = await TraconTestHost.StartAsync();

        await SaveAsync(host, Mcp, CredentialHeaderSaveTests.McpBody(
            headers: CredentialHeaderSaveTests.Map(("X-Team", "platform")),
            keys: CredentialHeaderSaveTests.Map(("X-API-Key", "Tracon:McpSecrets:SearchKey"))));
        await SaveAsync(host, Webhook, CredentialHeaderSaveTests.WebhookBody(
            headers: CredentialHeaderSaveTests.Map(("X-Team", "platform")),
            keys: CredentialHeaderSaveTests.Map(("X-API-Key", "Tracon:WebhookSecrets:OrdersKey"))));

        await SaveAsync(host, Mcp, CredentialHeaderSaveTests.McpBody(headers: CredentialHeaderSaveTests.Map(), keys: CredentialHeaderSaveTests.Map()));
        await SaveAsync(host, Webhook, CredentialHeaderSaveTests.WebhookBody(headers: CredentialHeaderSaveTests.Map(), keys: CredentialHeaderSaveTests.Map()));

        var server = await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "m1");
        server.ShouldNotBeNull().Headers.ShouldBeEmpty();
        server.HeaderConfigurationKeys.ShouldBeEmpty();

        var subscription = await host.Services.GetRequiredService<IWebhookStore>().GetSubscriptionAsync("default", "w1");
        subscription.ShouldNotBeNull().Headers.ShouldBeEmpty();
        subscription.HeaderConfigurationKeys.ShouldBeEmpty();
    }

    /// <summary>
    /// A row written before phase 190 may carry a credential in plain headers.
    /// Only the headers a request SENDS are judged (phase 190, open question 1), so the
    /// form can still save it.
    /// </summary>
    [Fact]
    public async Task A_stored_plain_credential_does_not_block_a_form_save()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.Services.GetRequiredService<IMcpServerStore>().SaveAsync(new McpServerDefinition
        {
            Id = Guid.Empty,
            TenantId = "default",
            Name = "m1",
            Endpoint = new Uri("https://mcp.example.com/mcp"),
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-API-Key"] = "legacy-plain" },
        });

        await SaveAsync(host, Mcp, McpFormBody());

        (await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "m1"))!
            .Headers["X-API-Key"].ShouldBe("legacy-plain");
    }

    /// <summary>
    /// The migration recipe (production guide): moving a plain credential to a
    /// key needs the plain map in the SAME save, without that header. Sending
    /// only the new map keeps the stored plain header, and the detail tells
    /// the operator what to do.
    /// </summary>
    [Fact]
    public async Task Migrating_a_plain_credential_needs_the_plain_map_in_the_same_save()
    {
        await using var host = await TraconTestHost.StartAsync();

        await host.Services.GetRequiredService<IMcpServerStore>().SaveAsync(new McpServerDefinition
        {
            Id = Guid.Empty,
            TenantId = "default",
            Name = "m1",
            Endpoint = new Uri("https://mcp.example.com/mcp"),
            Headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-API-Key"] = "legacy-plain",
                ["X-Team"] = "t1",
            },
        });

        using (var keysOnly = await host.Client.PutAsJsonAsync(
                   Mcp,
                   CredentialHeaderSaveTests.McpBody(keys: CredentialHeaderSaveTests.Map(("X-API-Key", "Tracon:McpSecrets:SearchKey")))))
        {
            keysOnly.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await CredentialHeaderSaveTests.Detail(keysOnly)).ShouldContain("Remove 'X-API-Key' from headers");
        }

        await SaveAsync(host, Mcp, CredentialHeaderSaveTests.McpBody(
            headers: CredentialHeaderSaveTests.Map(("X-Team", "t1")),
            keys: CredentialHeaderSaveTests.Map(("X-API-Key", "Tracon:McpSecrets:SearchKey"))));

        var migrated = await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "m1");

        migrated.ShouldNotBeNull().Headers.Keys.ShouldBe(["X-Team"]);
        migrated.HeaderConfigurationKeys["X-API-Key"].ShouldBe("Tracon:McpSecrets:SearchKey");
    }

    /// <summary>
    /// The side effect the preservation brings: moving the address in the
    /// form keeps the headers, so they go to the new host. It is documented
    /// and logged by name — never by value.
    /// </summary>
    [Fact]
    public async Task Moving_to_another_host_keeps_the_maps_and_logs_their_names()
    {
        await using var host = await TraconTestHost.StartAsync();

        await SaveAsync(host, Mcp, CredentialHeaderSaveTests.McpBody(
            headers: CredentialHeaderSaveTests.Map(("X-Team", "team-value-190")),
            keys: CredentialHeaderSaveTests.Map(("X-API-Key", "Tracon:McpSecrets:SearchKey"))));
        await SaveAsync(host, Webhook, CredentialHeaderSaveTests.WebhookBody(
            headers: CredentialHeaderSaveTests.Map(("X-Team", "team-value-190")),
            keys: CredentialHeaderSaveTests.Map(("X-API-Key", "Tracon:WebhookSecrets:OrdersKey"))));

        await SaveAsync(host, Mcp, McpFormBody("https://other.example.com/mcp"));
        await SaveAsync(host, Webhook, WebhookFormBody("https://other.example.com/hook"));

        var server = await host.Services.GetRequiredService<IMcpServerStore>().GetAsync("default", "m1");
        server.ShouldNotBeNull().Endpoint.Host.ShouldBe("other.example.com");
        server.HeaderConfigurationKeys["X-API-Key"].ShouldBe("Tracon:McpSecrets:SearchKey");

        var moved = host.Logs.Entries
            .Where(static entry => entry.StartsWith("Warning", StringComparison.Ordinal)
                && entry.Contains("kept its", StringComparison.Ordinal))
            .ToList();

        moved.Count.ShouldBe(2);

        foreach (var entry in moved)
        {
            entry.ShouldContain("X-API-Key");
            entry.ShouldContain("other.example.com");
        }

        host.Logs.AllText.ShouldNotContain("team-value-190");
    }

    private static async Task SaveAsync(TraconTestHost host, Uri uri, object body)
    {
        using var response = await host.Client.PutAsJsonAsync(uri, body);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}

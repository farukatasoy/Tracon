using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The audit trail records header NAMES and configuration key NAMES, and no
/// header value (phase 190, decision 6).
/// </summary>
/// <remarks>
/// <para>
/// The trail is plain text (K-779). <c>X-Tenant</c> is outside both the audit
/// secret filter and the credential header rule, so its value is stored as
/// sent — only the record's own masking keeps it out of the trail.
/// </para>
/// <para>
/// A configuration key map is keyed by credential-looking names
/// (<c>Authorization</c>, <c>X-API-Key</c>). The name-based filter would
/// redact every entry, although none holds a value; the trail would then no
/// longer say which key a header reads (open question 5 = A).
/// </para>
/// </remarks>
public sealed class CredentialHeaderAuditTests
{
    private const string PlainValue = "plain-190";

    [Fact]
    public async Task Mcp_and_webhook_audit_carries_names_and_no_header_value()
    {
        await using var host = await TraconTestHost.StartAsync();

        await SaveAsync(host, "/tracon/api/mcp-servers/m1", CredentialHeaderSaveTests.McpBody(
            headers: CredentialHeaderSaveTests.Map(("X-Tenant", PlainValue)),
            keys: CredentialHeaderSaveTests.Map(("Authorization", "Tracon:McpSecrets:Bearer"), ("X-API-Key", "Tracon:McpSecrets:SearchKey"))));
        await SaveAsync(host, "/tracon/api/webhooks/w1", CredentialHeaderSaveTests.WebhookBody(
            headers: CredentialHeaderSaveTests.Map(("X-Tenant", PlainValue)),
            keys: CredentialHeaderSaveTests.Map(("X-API-Key", "Tracon:WebhookSecrets:OrdersKey"))));

        var mcp = await host.Client.GetStringAsync(new Uri("/tracon/api/audit/mcp:m1", UriKind.Relative));
        var webhook = await host.Client.GetStringAsync(new Uri("/tracon/api/audit/webhook:w1", UriKind.Relative));

        foreach (var trail in new[] { mcp, webhook })
        {
            trail.ShouldNotContain(PlainValue);
            trail.ShouldContain("X-Tenant");
        }

        // The entries are JSON text inside the response, so their quotes are escaped.
        mcp.ShouldContain("Tracon:McpSecrets:Bearer");
        mcp.ShouldContain("Tracon:McpSecrets:SearchKey");
        webhook.ShouldContain("Tracon:WebhookSecrets:OrdersKey");
    }

    private static async Task SaveAsync(TraconTestHost host, string path, object body)
    {
        using var response = await host.Client.PutAsJsonAsync(new Uri(path, UriKind.Relative), body);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}

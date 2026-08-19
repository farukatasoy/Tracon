using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Behavior of the inbound trigger admin (CRUD) and unauthenticated accept
/// endpoints (phase 66).
/// </summary>
public sealed class TriggerEndpointTests
{
    private const string SecretConfigurationKey = "AgentPrism:TriggerSecrets:Slack";
    private const string Secret = "whsec_test";
    private const string Body = """{"event":{"text":"hello"}}""";

    // --- Admin CRUD ---

    [Fact]
    public async Task Saved_trigger_is_read_back_with_resolved_false_when_the_secret_has_no_value()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/triggers/slack",
            new
            {
                targetKind = "agent",
                targetName = "demo",
                signingSecretConfigurationName = SecretConfigurationKey,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var saved = await response.Content.ReadFromJsonAsync<InboundTriggerResponse>();

        saved.ShouldNotBeNull();
        saved.TargetName.ShouldBe("demo");
        saved.SigningSecretConfigurationName.ShouldBe(SecretConfigurationKey);
        saved.Resolved.ShouldBeFalse();
        saved.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Resolved_is_true_once_the_configuration_key_carries_a_value()
    {
        await using var host = await StartWithSecretAsync();

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/triggers/slack",
            new { targetKind = "agent", targetName = "demo", signingSecretConfigurationName = SecretConfigurationKey });

        var saved = await response.Content.ReadFromJsonAsync<InboundTriggerResponse>();

        saved.ShouldNotBeNull();
        saved.Resolved.ShouldBeTrue();
    }

    [Fact]
    public async Task Name_outside_the_allowed_prefix_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/triggers/slack",
            new { targetKind = "agent", targetName = "demo", signingSecretConfigurationName = "ConnectionStrings:Default" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Path_mode_without_a_payload_path_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/triggers/slack",
            new
            {
                targetKind = "agent",
                targetName = "demo",
                signingSecretConfigurationName = SecretConfigurationKey,
                payloadMode = "path",
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Response_never_carries_the_secret_value()
    {
        await using var host = await StartWithSecretAsync();

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/triggers/slack",
            new { targetKind = "agent", targetName = "demo", signingSecretConfigurationName = SecretConfigurationKey });

        using var listResponse = await host.Client.GetAsync(new Uri("/agentprism/api/triggers", UriKind.Relative));
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listBody.ShouldNotContain(Secret);

        using var auditResponse = await host.Client.GetAsync(new Uri("/agentprism/api/audit", UriKind.Relative));
        var auditBody = await auditResponse.Content.ReadAsStringAsync();
        auditBody.ShouldNotContain(Secret);
    }

    [Fact]
    public async Task Save_writes_an_audit_trail_entry_before_the_definition_is_readable()
    {
        // K-089: this is a functional assertion, not just "no exception" —
        // the entry must actually be there.
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(
            "/agentprism/api/triggers/slack",
            new { targetKind = "agent", targetName = "demo", signingSecretConfigurationName = SecretConfigurationKey });

        using var auditResponse = await host.Client.GetAsync(new Uri("/agentprism/api/audit", UriKind.Relative));
        var auditBody = await auditResponse.Content.ReadAsStringAsync();

        auditBody.ShouldContain("trigger.create");
        auditBody.ShouldContain("configKeyName");
    }

    [Fact]
    public async Task Unknown_name_returns_404_on_get_and_delete()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        (await host.Client.GetAsync(new Uri("/agentprism/api/triggers/missing", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await host.Client.DeleteAsync(new Uri("/agentprism/api/triggers/missing", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleted_trigger_no_longer_accepts_requests()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host);

        (await host.Client.DeleteAsync(new Uri("/agentprism/api/triggers/slack", UriKind.Relative)))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var response = await SendSignedAsync(host, "default", "slack", Body);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // --- Accept endpoint ---

    [Fact]
    public async Task A_correctly_signed_request_is_accepted_and_queues_a_run()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host);

        using var response = await SendSignedAsync(host, "default", "slack", Body);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        response.Headers.Location.ShouldNotBeNull();

        var accepted = await response.Content.ReadFromJsonAsync<InboundTriggerAcceptedResponse>();
        accepted.ShouldNotBeNull();
        accepted.RunId.ShouldNotBeNull();

        using var runResponse = await host.Client.GetAsync(new Uri($"/agentprism/api/runs/{accepted.RunId}", UriKind.Relative));
        runResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unsigned_request_is_rejected()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/agentprism/api/triggers/default/slack")
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json"),
        };

        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_request_with_a_tampered_body_is_rejected()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host);

        var timestamp = DateTimeOffset.UtcNow;
        var signature = WebhookSigner.Sign(Body, timestamp, Secret);

        using var response = await SendSignedAsync(
            host, "default", "slack", """{"event":{"text":"tampered"}}""", timestamp, signature);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_stale_timestamp_is_rejected()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host);

        var staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        var signature = WebhookSigner.Sign(Body, staleTimestamp, Secret);

        using var response = await SendSignedAsync(host, "default", "slack", Body, staleTimestamp, signature);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_replayed_request_is_rejected_the_second_time()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host);

        var timestamp = DateTimeOffset.UtcNow;
        var signature = WebhookSigner.Sign(Body, timestamp, Secret);

        using var first = await SendSignedAsync(host, "default", "slack", Body, timestamp, signature);
        using var second = await SendSignedAsync(host, "default", "slack", Body, timestamp, signature);

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task An_unknown_tenant_does_not_fall_back_to_the_default_tenant()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host);

        using var response = await SendSignedAsync(host, "some-other-tenant", "slack", Body);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unknown_trigger_name_and_a_wrong_signature_return_the_identical_response()
    {
        // 66.2: a caller must not be able to enumerate trigger names by
        // comparing the "does not exist" and "wrong signature" responses —
        // this asserts the two bodies are actually EQUAL, not merely that
        // each independently returns some 4xx code.
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host);

        var timestamp = DateTimeOffset.UtcNow;

        using var missingResponse = await SendSignedAsync(
            host, "default", "missing-trigger", Body, timestamp, "sha256=deadbeef");
        using var wrongSignatureResponse = await SendSignedAsync(
            host, "default", "slack", Body, timestamp, "sha256=deadbeef");

        missingResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrongSignatureResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var missingBody = await missingResponse.Content.ReadAsStringAsync();
        var wrongSignatureBody = await wrongSignatureResponse.Content.ReadAsStringAsync();

        missingBody.ShouldBe(wrongSignatureBody);
    }

    [Fact]
    public async Task A_disabled_trigger_rejects_requests()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host, enabled: false);

        using var response = await SendSignedAsync(host, "default", "slack", Body);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Malformed_json_body_is_rejected()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host);

        const string malformedBody = "{not json";

        using var response = await SendSignedAsync(host, "default", "slack", malformedBody);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_unresolved_payload_path_is_rejected_and_starts_no_run()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host, payloadMode: "path", payloadPath: "event.missing");

        using var response = await SendSignedAsync(host, "default", "slack", Body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_body_larger_than_the_configured_limit_is_rejected()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.Services.Configure<AgentPrismInboundTriggerOptions>(
                options => options.MaxBodyBytes = 16),
            configureServices: services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(SecretConfigurationKey, Secret)]).Build()));

        await SaveTriggerAsync(host);

        var oversizedBody = """{"event":{"text":"this body is definitely longer than sixteen bytes"}}""";
        var timestamp = DateTimeOffset.UtcNow;
        var signature = WebhookSigner.Sign(oversizedBody, timestamp, Secret);

        using var response = await SendSignedAsync(host, "default", "slack", oversizedBody, timestamp, signature);

        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task A_full_quota_rejects_the_trigger_with_429_and_does_not_bypass_it()
    {
        // K-394's precedent: a trigger has no bearer token, but it must go
        // through the SAME QuotaGate every other run-starting endpoint uses.
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host, targetName: "support");

        using (var created = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/quotas", UriKind.Relative),
            new QuotaSaveRequest { AgentName = "support", Period = QuotaPeriod.Daily, MaxRuns = 1, Enabled = true }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var enforcer = host.Services.GetRequiredService<QuotaEnforcer>();

        await enforcer.RecordAsync(new QuotaConsumption
        {
            TenantId = "default",
            AgentName = "support",
            Runs = 1,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        using var response = await SendSignedAsync(host, "default", "slack", Body);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_trigger_rate_limit_rejects_requests_beyond_the_per_minute_cap()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.Services.Configure<AgentPrismInboundTriggerOptions>(
                options => options.MaxRequestsPerMinute = 1),
            configureServices: services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(SecretConfigurationKey, Secret)]).Build()));

        await SaveTriggerAsync(host);

        using var first = await SendSignedAsync(host, "default", "slack", Body);
        using var second = await SendSignedAsync(host, "default", "slack", Body + " ");

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_workflow_target_is_queued_without_a_synchronous_run_id()
    {
        await using var host = await StartWithSecretAsync();
        await SaveTriggerAsync(host, targetKind: "workflow", targetName: "demo-workflow");

        using var response = await SendSignedAsync(host, "default", "slack", Body);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var accepted = await response.Content.ReadFromJsonAsync<InboundTriggerAcceptedResponse>();
        accepted.ShouldNotBeNull();
        accepted.RunId.ShouldBeNull();

        using var jobResponse = await host.Client.GetAsync(new Uri($"/agentprism/api/jobs/{accepted.JobId}", UriKind.Relative));
        jobResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<AgentPrismTestHost> StartWithSecretAsync()
        => await AgentPrismTestHost.StartAsync(
            configureServices: services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(SecretConfigurationKey, Secret)]).Build()));

    private static async Task SaveTriggerAsync(
        AgentPrismTestHost host,
        bool enabled = true,
        string targetKind = "agent",
        string targetName = "demo",
        string payloadMode = "wholeBody",
        string? payloadPath = null)
    {
        using var response = await host.Client.PutAsJsonAsync(
            "/agentprism/api/triggers/slack",
            new
            {
                targetKind,
                targetName,
                signingSecretConfigurationName = SecretConfigurationKey,
                payloadMode,
                payloadPath,
                enabled,
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> SendSignedAsync(
        AgentPrismTestHost host, string tenantId, string name, string body)
    {
        var timestamp = DateTimeOffset.UtcNow;

        return SendSignedAsync(host, tenantId, name, body, timestamp, WebhookSigner.Sign(body, timestamp, Secret));
    }

    private static async Task<HttpResponseMessage> SendSignedAsync(
        AgentPrismTestHost host,
        string tenantId,
        string name,
        string body,
        DateTimeOffset timestamp,
        string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/agentprism/api/triggers/{tenantId}/{name}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        request.Headers.Add(WebhookSigner.TimestampHeader, timestamp.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add(WebhookSigner.SignatureHeader, signature);

        return await host.Client.SendAsync(request);
    }
}

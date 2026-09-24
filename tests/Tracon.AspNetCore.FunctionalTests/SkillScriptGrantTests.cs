using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Behavior tests for the script execution grant endpoints.</summary>
public sealed class SkillScriptGrantTests
{
    private static readonly Uri Grants = new("/tracon/api/skill-script-grants", UriKind.Relative);

    [Fact]
    public async Task Grant_cannot_be_given_while_script_execution_is_disabled()
    {
        // Showing that the grant was given and then rejecting execution
        // would be misleading; the endpoint reports the state explicitly with 409.
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Grants, Request());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Grant_is_given_listed_and_revoked()
    {
        await using var host = await StartWithScriptsAsync();

        using (var created = await host.Client.PostAsJsonAsync(Grants, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            var body = await TraconTestHost.ReadJsonAsync(created);
            body.GetProperty("skillName").GetString().ShouldBe("invoice-analysis");
        }

        using (var listed = await host.Client.GetAsync(Grants))
        {
            (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using var revoked = await host.Client.DeleteAsync(
            new Uri("/tracon/api/skill-script-grants/invoice-analysis", UriKind.Relative));
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Grant_expired_in_the_past_is_rejected()
    {
        await using var host = await StartWithScriptsAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Grants,
            Request() with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Script_with_an_extension_that_has_no_interpreter_cannot_be_saved()
    {
        await using var host = await StartWithScriptsAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/skills/invoice-analysis", UriKind.Relative),
            new AgentSkillRequest
            {
                Name = "invoice-analysis",
                Description = "Analyzes invoices.",
                Instructions = "Examine invoices carefully.",
                Scripts =
                [
                    new AgentSkillScriptDefinition
                    {
                        Name = "run",
                        Extension = "exe",
                        Content = "MZ",
                    },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// A stored skill's grant must say which content it approves. Without a hash, or
    /// with one that cannot be a hash, nothing is written - not the grant, not its
    /// audit entry.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDE")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0")]
    [InlineData("G123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    public async Task A_stored_skill_grant_without_a_well_formed_hash_is_refused_and_writes_nothing(string? expected)
    {
        await using var host = await StartWithScriptsAsync();
        await SaveStoredSkillAsync(host);

        using var response = await host.Client.PostAsJsonAsync(
            Grants,
            new { skillName = "invoice-analysis", scriptName = "total", expectedContentHash = expected });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain("expectedContentHash");
        (await GrantCountAsync(host)).ShouldBe(0);
        (await GrantAuditCountAsync(host)).ShouldBe(0);
    }

    [Fact]
    public async Task A_grant_for_a_script_the_stored_skill_does_not_carry_is_404_and_writes_nothing()
    {
        await using var host = await StartWithScriptsAsync();
        await SaveStoredSkillAsync(host);

        using var response = await host.Client.PostAsJsonAsync(
            Grants,
            new { skillName = "invoice-analysis", scriptName = "missing", expectedContentHash = new string('A', 64) });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        (await GrantCountAsync(host)).ShouldBe(0);
        (await GrantAuditCountAsync(host)).ShouldBe(0);
    }

    /// <summary>
    /// A disabled stored skill is still a stored skill: treating it as absent would
    /// grant it without a pin and without the platform check, and the grant would
    /// refuse every script once the skill is enabled again.
    /// </summary>
    [Fact]
    public async Task A_disabled_stored_skill_still_needs_the_content_hash()
    {
        await using var host = await StartWithScriptsAsync();
        await SaveStoredSkillAsync(host, enabled: false);

        using var response = await host.Client.PostAsJsonAsync(Grants, new { skillName = "invoice-analysis", scriptName = "total" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await GrantCountAsync(host)).ShouldBe(0);
    }

    [Fact]
    public async Task A_disabled_stored_skill_still_needs_platform_authority_in_a_multi_tenant_host()
    {
        await using var host = await TraconTestHost.StartAsync(builder => builder
            .UseSkillScripts(options =>
            {
                options.PlatformIsolationAcknowledged = true;
                options.AllowStoredScripts = true;
                options.Interpreters["py"] = "python3";
            })
            .UseTenancy(options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        using (var saved = await SendAsync(host, HttpMethod.Put, "/tracon/api/skills/invoice-analysis", StoredSkill(enabled: false), tenant: "acme"))
        {
            saved.IsSuccessStatusCode.ShouldBeTrue(await saved.Content.ReadAsStringAsync());
        }

        var key = await CreateKeyAsync(host, "acme", "SecurityAdmin", "AgentsRead");
        using var read = await SendAsync(host, HttpMethod.Get, "/tracon/api/skills/invoice-analysis", body: null, key: key);
        var hash = (await TraconTestHost.ReadJsonAsync(read)).GetProperty("scripts")[0].GetProperty("contentHash").GetString();

        using var response = await SendAsync(
            host,
            HttpMethod.Post,
            "/tracon/api/skill-script-grants",
            new { skillName = "invoice-analysis", scriptName = "total", expectedContentHash = hash },
            key: key);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The endpoint reads the skill before it writes the grant; when that read
    /// fails, the grant is not written anyway on the assumption the skill is absent.
    /// </summary>
    [Fact]
    public async Task A_grant_is_not_written_when_the_skill_cannot_be_read()
    {
        var grants = new InMemorySkillScriptGrantStore();

        await using var host = await TraconTestHost.StartAsync(
            builder => builder.UseSkillScripts(options =>
            {
                options.PlatformIsolationAcknowledged = true;
                options.AllowStoredScripts = true;
                options.Interpreters["py"] = "python3";
            }),
            configureServices: services =>
            {
                services.AddSingleton<IAgentSkillStore, ThrowingSkillStore>();
                services.AddSingleton<ISkillScriptGrantStore>(grants);
            });

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await host.Client.PostAsJsonAsync(Grants, new { skillName = "invoice-analysis", scriptName = "total" }));

        (await grants.ListAsync("default")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_revoked_grant_stays_listed_with_its_revocation_time()
    {
        await using var host = await StartWithScriptsAsync();

        using (var created = await host.Client.PostAsJsonAsync(Grants, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using (var revoked = await host.Client.DeleteAsync(new Uri("/tracon/api/skill-script-grants/invoice-analysis", UriKind.Relative)))
        {
            revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var listed = await host.Client.GetAsync(Grants);
        var grant = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().ShouldHaveSingleItem();
        grant.GetProperty("revokedAt").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.String);

        // Revoking again finds no active grant.
        using var again = await host.Client.DeleteAsync(new Uri("/tracon/api/skill-script-grants/invoice-analysis", UriKind.Relative));
        again.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static object StoredSkill(bool enabled = true)
        => new
        {
            name = "invoice-analysis",
            description = "Analyzes invoices.",
            instructions = "Examine invoices carefully.",
            enabled,
            resources = Array.Empty<object>(),
            scripts = new[] { new { name = "total", extension = "py", content = "print(1)", parametersSchema = (string?)null } },
        };

    private static async Task SaveStoredSkillAsync(TraconTestHost host, bool enabled = true)
    {
        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/skills/invoice-analysis", UriKind.Relative),
            StoredSkill(enabled));

        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private static async Task<int> GrantCountAsync(TraconTestHost host)
    {
        using var listed = await host.Client.GetAsync(Grants);

        return (await TraconTestHost.ReadJsonAsync(listed)).GetArrayLength();
    }

    private static async Task<int> GrantAuditCountAsync(TraconTestHost host)
    {
        using var audit = await host.Client.GetAsync(new Uri("/tracon/api/audit?action=script.grant", UriKind.Relative));

        return (await TraconTestHost.ReadJsonAsync(audit)).GetArrayLength();
    }

    private static async Task<HttpResponseMessage> SendAsync(
        TraconTestHost host,
        HttpMethod method,
        string path,
        object? body,
        string? tenant = null,
        string? key = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body),
        };

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        if (key is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        }

        return await host.Client.SendAsync(request);
    }

    private static async Task<string> CreateKeyAsync(TraconTestHost host, string tenant, params string[] scopes)
    {
        using var response = await SendAsync(host, HttpMethod.Post, "/tracon/api/api-keys", new { name = $"{tenant}-grant-key", scopes }, tenant);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<ApiKeyCreationResult>();

        return created.ShouldNotBeNull().PlaintextKey;
    }

    private static Task<TraconTestHost> StartWithScriptsAsync()
        => TraconTestHost.StartAsync(builder => builder.UseSkillScripts(options =>
        {
            options.PlatformIsolationAcknowledged = true;
            options.AllowStoredScripts = true;
            options.Interpreters["py"] = "python3";
        }));

    private sealed class ThrowingSkillStore : IAgentSkillStore
    {
        public ValueTask<IReadOnlyList<AgentSkillDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("skill store unavailable");

        public ValueTask<AgentSkillDefinition?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("skill store unavailable");

        public ValueTask<AgentSkillDefinition> SaveAsync(AgentSkillDefinition skill, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("skill store unavailable");

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("skill store unavailable");
    }

    private static SkillScriptGrantRequest Request()
        => new() { SkillName = "invoice-analysis" };
}

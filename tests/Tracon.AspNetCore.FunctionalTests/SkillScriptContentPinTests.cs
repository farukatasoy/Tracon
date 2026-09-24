using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A script grant authorizes the content it was given for, not a name: a stored
/// script that changes after the grant does not run, whatever authority wrote the
/// change. Proven on the whole path a model call takes - HTTP run, fake model,
/// Microsoft Agent Framework's <c>run_skill_script</c> dispatcher, the runner, a
/// real <c>/bin/bash</c> process.
/// </summary>
/// <remarks>
/// <para>
/// Every host carries a standing, unconditioned approval rule for
/// <c>run_skill_script</c>: the dispatcher's own approval never stops a call, which
/// is the point - an approval, remembered or admin-authored, is not the gate that
/// decides WHICH content runs. The pin is.
/// </para>
/// <para>
/// Each run gets its own agent and fake provider, because a fake provider's script
/// is one queue: the tool call for a second run would otherwise be served inside
/// the first.
/// </para>
/// </remarks>
public sealed class SkillScriptContentPinTests
{
    private const string SkillName = "pin-skill";
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string StaticToken = "skill-pin-static-token-value";

    private static readonly Uri Grants = new("/tracon/api/skill-script-grants", UriKind.Relative);
    private static readonly Uri Skill = new($"/tracon/api/skills/{SkillName}", UriKind.Relative);

    [Fact]
    public async Task A_grant_pins_the_script_content_it_was_given_for()
    {
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash not found.");

        await using var host = await StartAsync(["hello", "hello"]);
        await SaveSkillAsync(host, ("hello", "echo pin-v1"));

        var hash = await ReadScriptHashAsync(host, "hello");
        using (var granted = await GrantAsync(host, new { skillName = SkillName, scriptName = "hello", expectedContentHash = hash }))
        {
            granted.StatusCode.ShouldBe(HttpStatusCode.Created, await granted.Content.ReadAsStringAsync());
            (await TraconTestHost.ReadJsonAsync(granted)).GetProperty("contentHash").GetString().ShouldBe(hash);
        }

        (await RunAsync(host, run: 1)).ShouldContain("pin-v1");

        // AgentsAdmin rewrites the script under the same name; SecurityAdmin
        // approved v1 only.
        await SaveSkillAsync(host, ("hello", "echo pin-v2"));

        (await RunAsync(host, run: 2)).ShouldNotContain("pin-v2");

        // The reason reaches the audit trail; the model only sees that the call failed.
        (await DenialsAsync(host)).ShouldHaveSingleItem().ShouldContain("content changed since the grant");
    }

    [Fact]
    public async Task Skill_wide_grant_refuses_every_script_after_a_script_is_added()
    {
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash not found.");

        await using var host = await StartAsync(["first", "first", "second"]);
        await SaveSkillAsync(host, ("first", "echo set-first"));

        using (var granted = await GrantAsync(host, new { skillName = SkillName, expectedContentHash = await ReadSetHashAsync(host) }))
        {
            granted.StatusCode.ShouldBe(HttpStatusCode.Created, await granted.Content.ReadAsStringAsync());
        }

        (await RunAsync(host, run: 1)).ShouldContain("set-first");

        await SaveSkillAsync(host, ("first", "echo set-first"), ("second", "echo set-second"));

        (await RunAsync(host, run: 2)).ShouldNotContain("set-first");
        (await RunAsync(host, run: 3)).ShouldNotContain("set-second");

        var denials = await DenialsAsync(host);
        denials.Count.ShouldBe(2);
        denials.ShouldAllBe(static denial => denial.Contains("content changed since the grant", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Skill_wide_grant_refuses_the_remaining_script_after_a_script_is_removed()
    {
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash not found.");

        await using var host = await StartAsync(["first", "first"]);
        await SaveSkillAsync(host, ("first", "echo removed-first"), ("second", "echo removed-second"));

        using (var granted = await GrantAsync(host, new { skillName = SkillName, expectedContentHash = await ReadSetHashAsync(host) }))
        {
            granted.StatusCode.ShouldBe(HttpStatusCode.Created, await granted.Content.ReadAsStringAsync());
        }

        (await RunAsync(host, run: 1)).ShouldContain("removed-first");

        await SaveSkillAsync(host, ("first", "echo removed-first"));

        (await RunAsync(host, run: 2)).ShouldNotContain("removed-first");
        (await DenialsAsync(host)).ShouldHaveSingleItem().ShouldContain("content changed since the grant");
    }

    /// <summary>
    /// A grant given while no stored skill carried the name pins nothing, so it
    /// authorizes file scripts only. The same rule closes the shadowing case: a
    /// file skill granted this way and a stored skill created later under its name
    /// resolve the stored one first, and the stored script finds a grant that pins
    /// no content.
    /// </summary>
    [Fact]
    public async Task A_grant_given_before_the_stored_skill_existed_does_not_authorize_it()
    {
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash not found.");

        await using var host = await StartAsync(["hello"]);

        using (var granted = await GrantAsync(host, new { skillName = SkillName, scriptName = "hello" }))
        {
            granted.StatusCode.ShouldBe(HttpStatusCode.Created, await granted.Content.ReadAsStringAsync());
            var grant = await TraconTestHost.ReadJsonAsync(granted);
            (grant.TryGetProperty("contentHash", out var pinned) ? pinned.ValueKind : JsonValueKind.Null).ShouldBe(JsonValueKind.Null);
        }

        await SaveSkillAsync(host, ("hello", "echo created-later"));

        (await RunAsync(host, run: 1)).ShouldNotContain("created-later");
        (await DenialsAsync(host)).ShouldHaveSingleItem().ShouldContain("does not pin the script content");
    }

    [Fact]
    public async Task A_skill_recreated_with_other_content_does_not_run_under_the_old_grant()
    {
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash not found.");

        await using var host = await StartAsync(["hello", "hello"]);
        await SaveSkillAsync(host, ("hello", "echo recreated-v1"));

        using (var granted = await GrantAsync(host, new { skillName = SkillName, scriptName = "hello", expectedContentHash = await ReadScriptHashAsync(host, "hello") }))
        {
            granted.StatusCode.ShouldBe(HttpStatusCode.Created, await granted.Content.ReadAsStringAsync());
        }

        using (var deleted = await host.Client.DeleteAsync(Skill))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await SaveSkillAsync(host, ("hello", "echo recreated-other"));

        (await RunAsync(host, run: 1)).ShouldNotContain("recreated-other");
        (await DenialsAsync(host)).ShouldHaveSingleItem().ShouldContain("content changed since the grant");

        // The pin binds content, not identity: the same content recreated runs.
        using (var deleted = await host.Client.DeleteAsync(Skill))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await SaveSkillAsync(host, ("hello", "echo recreated-v1"));

        (await RunAsync(host, run: 2)).ShouldContain("recreated-v1");
    }

    /// <summary>
    /// The window between reading the content and granting it: another writer
    /// changes the script in between. The grant must pin what the reviewer read or
    /// nothing at all - never what happens to be stored when it lands.
    /// </summary>
    [Fact]
    public async Task A_grant_for_content_that_changed_after_it_was_read_is_refused_and_writes_nothing()
    {
        await using var host = await StartAsync([]);
        await SaveSkillAsync(host, ("hello", "echo read-v1"));

        var read = await ReadScriptHashAsync(host, "hello");

        await SaveSkillAsync(host, ("hello", "echo written-v2"));

        using var granted = await GrantAsync(host, new { skillName = SkillName, scriptName = "hello", expectedContentHash = read });

        granted.StatusCode.ShouldBe(HttpStatusCode.Conflict, await granted.Content.ReadAsStringAsync());
        var current = await ReadScriptHashAsync(host, "hello");
        current.ShouldNotBeNull();

        // A blind retry must not get the current hash for free: that would reopen the window.
        (await granted.Content.ReadAsStringAsync()).ShouldNotContain(current);
        (await ListGrantsAsync(host)).ShouldBe(0);
        (await AuditActionsAsync(host)).Contains("script.grant", StringComparer.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task The_grant_audit_entry_carries_the_pinned_hash()
    {
        await using var host = await StartAsync([]);
        await SaveSkillAsync(host, ("hello", "echo audited"));

        var hash = await ReadScriptHashAsync(host, "hello");
        hash.ShouldNotBeNull();

        using (var granted = await GrantAsync(host, new { skillName = SkillName, scriptName = "hello", expectedContentHash = hash }))
        {
            granted.StatusCode.ShouldBe(HttpStatusCode.Created, await granted.Content.ReadAsStringAsync());
        }

        var entry = (await AuditAsync(host)).Single(entry => string.Equals(entry.GetProperty("action").GetString(), "script.grant", StringComparison.Ordinal));
        entry.GetProperty("after").GetString().ShouldNotBeNull().ShouldContain(hash);
    }

    /// <summary>
    /// The console saves a skill by sending back what it read, hashes and origin
    /// included. The server ignores them and computes its own: a client cannot
    /// choose the hash a grant is compared with, and the console's round trip
    /// still saves.
    /// </summary>
    [Fact]
    public async Task Hashes_and_origin_sent_with_a_skill_are_ignored()
    {
        await using var host = await StartAsync([]);
        await SaveSkillAsync(host, ("hello", "echo round-trip"));

        var computed = await ReadScriptHashAsync(host, "hello");
        var computedSet = await ReadSetHashAsync(host);
        var forged = new string('A', 64);

        using var request = new HttpRequestMessage(HttpMethod.Put, Skill)
        {
            Content = JsonContent.Create(new
            {
                name = SkillName,
                description = "Skill for the pin tests.",
                instructions = "Run the script when asked.",
                enabled = true,
                resources = Array.Empty<object>(),
                scriptSetHash = forged,
                origin = "Code",
                scripts = new[]
                {
                    new { name = "hello", extension = "sh", content = "echo round-trip", parametersSchema = (string?)null, contentHash = forged },
                },
            }),
        };

        using (var saved = await host.Client.SendAsync(request))
        {
            saved.IsSuccessStatusCode.ShouldBeTrue(await saved.Content.ReadAsStringAsync());
        }

        var skill = await ReadSkillAsync(host, key: null, tenant: null, claimsTenant: null);
        skill.GetProperty("scripts")[0].GetProperty("contentHash").GetString().ShouldBe(computed);
        skill.GetProperty("scriptSetHash").GetString().ShouldBe(computedSet);
        skill.GetProperty("origin").GetString().ShouldBe("Database");
    }

    // ---- Multi-tenant host: a stored-script grant needs platform authority ------

    [Fact]
    public async Task A_tenant_bound_key_cannot_grant_a_stored_script_in_a_multi_tenant_host()
    {
        await using var host = await StartAsync([], multiTenant: true);
        await SaveSkillAsync(host, ("hello", "echo tenant-key"), tenant: "acme");
        var key = await CreateKeyAsync(host, "acme", "SecurityAdmin", "AgentsRead");

        var hash = await ReadScriptHashAsync(host, "hello", key);
        using var request = WithKey(GrantRequest(new { skillName = SkillName, scriptName = "hello", expectedContentHash = hash }), key);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        (await ListGrantsAsync(host, "acme")).ShouldBe(0);
    }

    [Fact]
    public async Task A_key_with_platform_authority_grants_a_stored_script_in_a_multi_tenant_host()
    {
        await using var host = await StartAsync([], multiTenant: true);
        await SaveSkillAsync(host, ("hello", "echo platform-key"), tenant: "acme");
        var key = await CreateKeyAsync(host, "acme", "SecurityAdmin", "AgentsRead", "PlatformAdmin");

        var hash = await ReadScriptHashAsync(host, "hello", key);
        using var request = WithKey(GrantRequest(new { skillName = SkillName, scriptName = "hello", expectedContentHash = hash }), key);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task The_static_token_grants_a_stored_script_in_a_multi_tenant_host()
    {
        await using var host = await StartAsync([], multiTenant: true, authToken: StaticToken);
        await SaveSkillAsync(host, ("hello", "echo static-token"), tenant: "acme", key: StaticToken);

        var hash = await ReadScriptHashAsync(host, "hello", StaticToken, tenant: "acme");
        using var request = WithKey(GrantRequest(new { skillName = SkillName, scriptName = "hello", expectedContentHash = hash }), StaticToken);
        request.Headers.Add(TenantHeader, "acme");
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_claims_admin_needs_the_registered_platform_policy_to_grant_a_stored_script()
    {
        await using var denied = await StartWithClaimsAsync(registerPlatformPolicy: false);
        await SaveSkillAsync(denied, ("hello", "echo claims"), claimsTenant: "acme");
        var deniedHash = await ReadScriptHashAsync(denied, "hello", claimsTenant: "acme");

        using (var request = AsClaimsUser(GrantRequest(new { skillName = SkillName, scriptName = "hello", expectedContentHash = deniedHash }), "acme", platform: true))
        using (var response = await denied.Client.SendAsync(request))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        }

        await using var allowed = await StartWithClaimsAsync(registerPlatformPolicy: true);
        await SaveSkillAsync(allowed, ("hello", "echo claims"), claimsTenant: "acme");
        var allowedHash = await ReadScriptHashAsync(allowed, "hello", claimsTenant: "acme");

        using (var request = AsClaimsUser(GrantRequest(new { skillName = SkillName, scriptName = "hello", expectedContentHash = allowedHash }), "acme", platform: true))
        using (var response = await allowed.Client.SendAsync(request))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task A_single_tenant_host_needs_no_platform_authority_for_a_stored_script_grant()
    {
        await using var host = await StartAsync([]);
        await SaveSkillAsync(host, ("hello", "echo single-tenant"));
        var key = await CreateKeyAsync(host, tenant: null, "SecurityAdmin", "AgentsRead");

        var hash = await ReadScriptHashAsync(host, "hello", key);
        using var request = WithKey(GrantRequest(new { skillName = SkillName, scriptName = "hello", expectedContentHash = hash }), key);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    // ---- Code skills: pinned like stored ones, and they win the name --------------

    [Fact]
    public async Task A_code_skill_script_is_read_and_pinned_like_a_stored_one()
    {
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash not found.");

        await using var host = await StartAsync(["hello"], codeSkillScript: "echo from-code");

        using var read = await host.Client.GetAsync(Skill);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        var skill = await TraconTestHost.ReadJsonAsync(read);
        skill.GetProperty("origin").GetString().ShouldBe("Code");

        var hash = skill.GetProperty("scripts")[0].GetProperty("contentHash").GetString();
        using (var granted = await GrantAsync(host, new { skillName = SkillName, scriptName = "hello", expectedContentHash = hash }))
        {
            granted.StatusCode.ShouldBe(HttpStatusCode.Created, await granted.Content.ReadAsStringAsync());
        }

        (await RunAsync(host, run: 1)).ShouldContain("from-code");
    }

    /// <summary>
    /// A stored skill under a code skill's name never runs: the code skill wins.
    /// The API no longer writes one (409), but a row saved before the name was
    /// taken in code still exists; it is seeded through the store here. The
    /// reviewer reads the content that runs (the code one), and a hash of the
    /// shadowed stored row is refused rather than pinned to content that never
    /// executes.
    /// </summary>
    [Fact]
    public async Task A_stored_skill_shadowed_by_a_code_skill_cannot_be_granted()
    {
        await using var host = await StartAsync([], codeSkillScript: "echo from-code");

        using (var save = await host.Client.PutAsJsonAsync(Skill, SkillBody(("hello", "echo shadowed-store"))))
        {
            save.StatusCode.ShouldBe(HttpStatusCode.Conflict, await save.Content.ReadAsStringAsync());
        }

        var shadowed = await host.Services.GetRequiredService<IAgentSkillStore>().SaveAsync(new AgentSkillDefinition
        {
            TenantId = "default",
            Name = SkillName,
            Description = "Stored before the name was taken in code.",
            Instructions = "Run the script when asked.",
            Scripts = [new AgentSkillScriptDefinition { Name = "hello", Extension = "sh", Content = "echo shadowed-store" }],
        });
        var storedHash = shadowed.Scripts[0].ContentHash;

        var runningHash = await ReadScriptHashAsync(host, "hello");
        string.Equals(runningHash, storedHash, StringComparison.Ordinal).ShouldBeFalse("The code skill and the shadowed stored skill carry different content.");

        using var refused = await GrantAsync(host, new { skillName = SkillName, scriptName = "hello", expectedContentHash = storedHash });
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, await refused.Content.ReadAsStringAsync());
        (await ListGrantsAsync(host)).ShouldBe(0);
    }

    // ---- Harness ----------------------------------------------------------------

    private static async Task<TraconTestHost> StartAsync(
        IReadOnlyList<string> runScripts,
        bool multiTenant = false,
        string? authToken = null,
        string? codeSkillScript = null)
    {
        var host = await TraconTestHost.StartAsync(
            builder => Configure(builder, runScripts, multiTenant, codeSkillScript, claimsTenancy: false),
            configureEndpoints: authToken is null ? null : options => options.AuthToken = authToken);

        await AddStandingApprovalRuleAsync(host, authToken, multiTenant ? "acme" : null);

        return host;
    }

    private static async Task<TraconTestHost> StartWithClaimsAsync(bool registerPlatformPolicy)
        => await TraconTestHost.StartAsync(
            builder => Configure(builder, [], multiTenant: true, codeSkillScript: null, claimsTenancy: true),
            configureServices: services =>
            {
                services
                    .AddAuthentication(ClaimsFromHeadersHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, ClaimsFromHeadersHandler>(
                        ClaimsFromHeadersHandler.SchemeName,
                        configureOptions: null);

                var authorization = services.AddAuthorizationBuilder();

                if (registerPlatformPolicy)
                {
                    authorization.AddPolicy(
                        TraconPolicies.PlatformAdmin,
                        static policy => policy.RequireClaim(ClaimsFromHeadersHandler.PlatformClaim, "yes"));
                }
            });

    private static void Configure(
        ITraconBuilder builder,
        IReadOnlyList<string> runScripts,
        bool multiTenant,
        string? codeSkillScript,
        bool claimsTenancy)
    {
        builder.UseSkillScripts(static options =>
        {
            options.PlatformIsolationAcknowledged = true;
            options.AllowStoredScripts = true;
            options.Interpreters["sh"] = "/bin/bash";
        });

        if (multiTenant)
        {
            builder.UseTenancy(options =>
            {
                options.Enabled = true;

                if (claimsTenancy)
                {
                    options.ClaimType = ClaimsFromHeadersHandler.TenantClaim;
                }
                else
                {
                    options.AllowHeaderResolution = true;
                }
            });
        }

        if (codeSkillScript is not null)
        {
            builder.AddSkill(new AgentSkillDefinition
            {
                TenantId = "default",
                Name = SkillName,
                Description = "Code skill for the pin tests.",
                Instructions = "Run the script when asked.",
                Scripts = [new AgentSkillScriptDefinition { Name = "hello", Extension = "sh", Content = codeSkillScript }],
            });
        }

        for (var run = 1; run <= runScripts.Count; run++)
        {
            var provider = $"pin-model-{run}";

            builder
                .AddModelProvider(new Tracon.Testing.FakeModelProvider(provider)
                    .CallsTool("run_skill_script", new { skillName = SkillName, scriptName = runScripts[run - 1] })
                    .EchoesLastToolResult())
                .AddAgent(new AgentDefinition
                {
                    Name = $"pin-agent-{run}",
                    Instructions = "Run the script.",
                    Model = new ModelBinding { Provider = provider, Model = "pin" },
                    SkillNames = [SkillName],
                });
        }
    }

    private static async Task AddStandingApprovalRuleAsync(TraconTestHost host, string? authToken, string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/approvals/rules", UriKind.Relative))
        {
            Content = JsonContent.Create(new { toolName = "run_skill_script" }),
        };

        if (authToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
        }

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private static object SkillBody(params (string Name, string Content)[] scripts)
        => new
        {
            name = SkillName,
            description = "Skill for the pin tests.",
            instructions = "Run the script when asked.",
            enabled = true,
            resources = Array.Empty<object>(),
            scripts = scripts.Select(static script => new { name = script.Name, extension = "sh", content = script.Content, parametersSchema = (string?)null }).ToArray(),
        };

    private static async Task SaveSkillAsync(
        TraconTestHost host,
        (string Name, string Content) script,
        string? tenant = null,
        string? key = null,
        string? claimsTenant = null)
        => await SaveSkillAsync(host, [script], tenant, key, claimsTenant);

    private static Task SaveSkillAsync(TraconTestHost host, (string Name, string Content) first, (string Name, string Content) second)
        => SaveSkillAsync(host, [first, second], tenant: null, key: null, claimsTenant: null);

    private static async Task SaveSkillAsync(
        TraconTestHost host,
        (string Name, string Content)[] scripts,
        string? tenant,
        string? key,
        string? claimsTenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Skill) { Content = JsonContent.Create(SkillBody(scripts)) };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        if (key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        if (claimsTenant is not null)
        {
            AsClaimsUser(request, claimsTenant, platform: true);
        }

        using var response = await host.Client.SendAsync(request);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
    }

    private static async Task<string?> ReadScriptHashAsync(
        TraconTestHost host,
        string scriptName,
        string? key = null,
        string? tenant = null,
        string? claimsTenant = null)
    {
        var skill = await ReadSkillAsync(host, key, tenant, claimsTenant);

        return skill.GetProperty("scripts").EnumerateArray()
            .Single(script => string.Equals(script.GetProperty("name").GetString(), scriptName, StringComparison.Ordinal))
            .TryGetProperty("contentHash", out var hash) ? hash.GetString() : null;
    }

    private static async Task<string?> ReadSetHashAsync(TraconTestHost host)
        => (await ReadSkillAsync(host, key: null, tenant: null, claimsTenant: null))
            .TryGetProperty("scriptSetHash", out var hash) ? hash.GetString() : null;

    private static async Task<JsonElement> ReadSkillAsync(TraconTestHost host, string? key, string? tenant, string? claimsTenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Skill);

        if (key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        if (claimsTenant is not null)
        {
            AsClaimsUser(request, claimsTenant, platform: true);
        }

        using var response = await host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return await TraconTestHost.ReadJsonAsync(response);
    }

    private static HttpRequestMessage GrantRequest(object body)
        => new(HttpMethod.Post, Grants) { Content = JsonContent.Create(body) };

    private static Task<HttpResponseMessage> GrantAsync(TraconTestHost host, object body)
        => host.Client.SendAsync(GrantRequest(body));

    private static async Task<int> ListGrantsAsync(TraconTestHost host, string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Grants);

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);

        return (await TraconTestHost.ReadJsonAsync(response)).GetArrayLength();
    }

    private static async Task<IReadOnlyList<JsonElement>> AuditAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(new Uri("/tracon/api/audit?limit=200", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = await TraconTestHost.ReadJsonAsync(response);
        var items = body.ValueKind == JsonValueKind.Array ? body : body.GetProperty("items");

        return [.. items.EnumerateArray().Select(static item => item.Clone())];
    }

    private static async Task<IReadOnlyList<string>> AuditActionsAsync(TraconTestHost host)
        => [.. (await AuditAsync(host)).Select(static entry => entry.GetProperty("action").GetString() ?? string.Empty)];

    private static async Task<IReadOnlyList<string>> DenialsAsync(TraconTestHost host)
        => [.. (await AuditAsync(host))
            .Where(static entry => string.Equals(entry.GetProperty("action").GetString(), "script.denied", StringComparison.Ordinal))
            .Select(static entry => entry.GetProperty("after").GetString() ?? string.Empty)];

    /// <summary>Runs agent <c>pin-agent-{run}</c> once and returns every SSE data line, joined.</summary>
    private static async Task<string> RunAsync(TraconTestHost host, int run)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/pin-agent-{run}/run", UriKind.Relative),
            new AgentRunRequest { Message = "run the script" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var frames = new List<string>();

        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()))
        {
            frames.Add($"{frame.Event}: {frame.Data}");
        }

        return string.Join(Environment.NewLine, frames);
    }

    private static async Task<string> CreateKeyAsync(TraconTestHost host, string? tenant, params string[] scopes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/api-keys")
        {
            Content = JsonContent.Create(new { name = $"{tenant ?? "default"}-pin-key", scopes }),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<ApiKeyCreationResult>();

        return created.ShouldNotBeNull().PlaintextKey;
    }

    private static HttpRequestMessage WithKey(HttpRequestMessage request, string key)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return request;
    }

    private static HttpRequestMessage AsClaimsUser(HttpRequestMessage request, string tenant, bool platform = false)
    {
        request.Headers.Add(ClaimsFromHeadersHandler.TenantHeader, tenant);

        if (platform)
        {
            request.Headers.Add(ClaimsFromHeadersHandler.PlatformHeader, "yes");
        }

        return request;
    }

    private sealed class ClaimsFromHeadersHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "PinClaimsFromHeaders";
        public const string TenantClaim = "tracon_tenant";
        public const string PlatformClaim = "tracon_platform";
        public const string TenantHeader = "X-Test-Claim-Tenant";
        public const string PlatformHeader = "X-Test-Claim-Platform";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, "pin-claims-user") };

            if (Request.Headers[TenantHeader].ToString() is { Length: > 0 } tenant)
            {
                claims.Add(new Claim(TenantClaim, tenant));
            }

            if (Request.Headers[PlatformHeader].ToString() is { Length: > 0 } platform)
            {
                claims.Add(new Claim(PlatformClaim, platform));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}

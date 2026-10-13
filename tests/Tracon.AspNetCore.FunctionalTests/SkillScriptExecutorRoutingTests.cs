using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A host that registers its own <see cref="ISkillScriptExecutor"/> gets every
/// approved script, stored or on disk, and nothing that a gate refused. Proven on
/// the whole path a model call takes: HTTP run, fake model, Microsoft Agent
/// Framework's <c>run_skill_script</c> dispatcher, the runner, the executor.
/// </summary>
/// <remarks>
/// <para>
/// Each script would create a sentinel file if a local process ran it. The
/// file never appears: that is what proves no local process started, which a
/// recording executor alone cannot show.
/// </para>
/// <para>
/// Every host carries a standing approval rule for <c>run_skill_script</c>, so
/// MAF's own approval never stops a call; the gates under test are Tracon's.
/// Each run gets its own agent and fake provider, because a fake provider's
/// script is one queue.
/// </para>
/// </remarks>
public sealed class SkillScriptExecutorRoutingTests : IDisposable
{
    private const string SkillName = "route-skill";
    private const string DiskSkillName = "disk-skill";
    private const string DiskScriptName = "scripts/route.sh";
    private const string TenantHeader = "X-Tracon-Tenant";

    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("tracon-routing-");

    [Fact]
    public async Task An_approved_stored_script_goes_to_the_registered_executor_and_no_local_process_starts()
    {
        var sentinel = Path.Combine(_scratch.FullName, "stored-ran-locally");
        var content = $"touch '{sentinel}'\necho stored-local";
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(executor, [("route-agent-1", SkillName, "hello")]);
        var saved = await SaveSkillAsync(host, tenant: "default", ("hello", "sh", content));
        await GrantAsync(host, "default", SkillName, saved.ScriptSetHash);

        (await RunAsync(host, "route-agent-1")).ShouldContain("routed-to-executor:hello");

        File.Exists(sentinel).ShouldBeFalse("A local process ran the script although an executor is registered.");

        var request = executor.Requests.ShouldHaveSingleItem();
        request.Origin.ShouldBe(SkillScriptOrigin.Stored);
        request.TenantId.ShouldBe("default");
        request.RunId.ShouldNotBeNull();
        request.SkillName.ShouldBe(SkillName);
        request.ScriptName.ShouldBe("hello");
        request.Extension.ShouldBe("sh");
        request.Interpreter.ShouldBe("/bin/sh");
        request.Content.ShouldBe(content);
        request.ContentSha256.ShouldBe(Sha256(content));
        request.SourcePath.ShouldBeNull();
    }

    /// <summary>
    /// A script under a skill root goes to the executor too, with the text the
    /// runner read and the path it came from.
    /// </summary>
    /// <remarks>
    /// The call starts at Microsoft Agent Framework's own file skill source, built
    /// by the host's container: MAF finds the script, calls Tracon's delegate, and
    /// the delegate reaches the runner and the registered executor. It does not
    /// start at an HTTP run, because an agent cannot name a skill that exists only
    /// on disk today: compilation refuses the name.
    /// </remarks>
    [Fact]
    public async Task An_approved_file_script_goes_to_the_registered_executor_with_its_text()
    {
        var sentinel = Path.Combine(_scratch.FullName, "file-ran-locally");
        var content = $"touch '{sentinel}'\necho file-local\n";
        var root = WriteDiskSkill(content);
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(executor, runs: [], skillRoot: root);
        await GrantAsync(host, "default", DiskSkillName, contentHash: null);

        using var source = host.Services.GetRequiredService<SkillScriptSupport>().CreateFileSource().ShouldNotBeNull();
        var skill = (await source.GetSkillsAsync(null!, CancellationToken.None))
            .ShouldHaveSingleItem()
            .ShouldBeOfType<Microsoft.Agents.AI.AgentFileSkill>();
        var script = (await skill.GetScriptAsync(DiskScriptName, CancellationToken.None)).ShouldNotBeNull();

        var output = await script.RunAsync(skill, arguments: null, host.Services, CancellationToken.None);

        output.ShouldBeOfType<string>().ShouldContain($"routed-to-executor:{DiskScriptName}");
        File.Exists(sentinel).ShouldBeFalse("A local process ran the file script although an executor is registered.");

        var request = executor.Requests.ShouldHaveSingleItem();
        request.Origin.ShouldBe(SkillScriptOrigin.File);
        request.SkillName.ShouldBe(DiskSkillName);
        request.ScriptName.ShouldBe(DiskScriptName);
        request.Content.ShouldBe(content);
        request.ContentSha256.ShouldBe(Sha256(content));
        request.SourcePath.ShouldBe(Path.Combine(root, DiskSkillName, "scripts", "route.sh"));
    }

    public static TheoryData<string, string> ClosedGates() => new()
    {
        { "no-grant", "no valid execution grant" },
        { "stale-pin", "content changed since the grant" },
        { "extension", "not on the interpreter allow-list" },
    };

    [Theory]
    [MemberData(nameof(ClosedGates))]
    public async Task A_closed_gate_never_reaches_the_executor(string gate, string reason)
    {
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(executor, [("route-agent-1", SkillName, "hello")]);

        var extension = string.Equals(gate, "extension", StringComparison.Ordinal) ? "py" : "sh";
        var saved = await SaveSkillAsync(host, tenant: "default", ("hello", extension, "echo gated"));

        if (string.Equals(gate, "stale-pin", StringComparison.Ordinal))
        {
            await GrantAsync(host, "default", SkillName, saved.ScriptSetHash);
            await SaveSkillAsync(host, tenant: "default", ("hello", extension, "echo changed-after-grant"));
        }
        else if (string.Equals(gate, "extension", StringComparison.Ordinal))
        {
            await GrantAsync(host, "default", SkillName, saved.ScriptSetHash);
        }

        (await RunAsync(host, "route-agent-1")).ShouldNotContain("routed-to-executor");

        executor.Requests.ShouldBeEmpty();
        (await DenialsAsync(host)).ShouldHaveSingleItem().ShouldContain(reason);
    }

    /// <summary>
    /// The audit entry is written by the runner before the executor is called. A
    /// run whose entry cannot be written never reaches the executor: an executor
    /// cannot skip the audit trail, and it cannot be handed a run without one.
    /// </summary>
    [Fact]
    public async Task A_run_whose_audit_entry_cannot_be_written_never_reaches_the_executor()
    {
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(
            executor,
            [("route-agent-1", SkillName, "hello")],
            auditLog: static provider => new ScriptRunRefusingAuditLog(new InMemoryAuditLog(provider.GetRequiredService<ITenantContext>())));

        var saved = await SaveSkillAsync(host, tenant: "default", ("hello", "sh", "echo audited"));
        await GrantAsync(host, "default", SkillName, saved.ScriptSetHash);

        (await RunAsync(host, "route-agent-1")).ShouldNotContain("routed-to-executor");

        executor.Requests.ShouldBeEmpty();
    }

    /// <summary>
    /// Two tenants carry the same skill; only one granted it. The executor gets the
    /// granting tenant's run, with that tenant in the request, and never the other.
    /// </summary>
    [Fact]
    public async Task The_request_carries_the_tenant_whose_grant_opened_the_gates()
    {
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(
            executor,
            [("route-agent-1", SkillName, "hello"), ("route-agent-2", SkillName, "hello")],
            multiTenant: true);

        await AddStandingApprovalRuleAsync(host, "acme");
        await AddStandingApprovalRuleAsync(host, "globex");

        var acme = await SaveSkillAsync(host, tenant: "acme", ("hello", "sh", "echo tenant"));
        await SaveSkillAsync(host, tenant: "globex", ("hello", "sh", "echo tenant"));
        await GrantAsync(host, "acme", SkillName, acme.ScriptSetHash);

        (await RunAsync(host, "route-agent-1", tenant: "acme")).ShouldContain("routed-to-executor:hello");
        (await RunAsync(host, "route-agent-2", tenant: "globex")).ShouldNotContain("routed-to-executor");

        executor.Requests.ShouldHaveSingleItem().TenantId.ShouldBe("acme");
    }

    /// <summary>
    /// The request carries no environment value, and neither the request's text
    /// form nor the server log carries the script's content.
    /// </summary>
    [Fact]
    public async Task Neither_the_request_nor_the_log_carries_a_host_secret_or_the_script()
    {
        var variable = $"TRACON_ROUTING_SECRET_{Guid.NewGuid():N}";
        var secret = $"secret-value-{Guid.NewGuid():N}";
        var contentMarker = $"content-marker-{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, secret);

        try
        {
            var executor = new RecordingScriptExecutor();

            await using var host = await StartAsync(executor, [("route-agent-1", SkillName, "hello")]);
            var saved = await SaveSkillAsync(host, tenant: "default", ("hello", "sh", $"# {contentMarker}\necho \"${variable}\""));
            await GrantAsync(host, "default", SkillName, saved.ScriptSetHash);

            (await RunAsync(host, "route-agent-1")).ShouldContain("routed-to-executor:hello");

            var request = executor.Requests.ShouldHaveSingleItem();
            var fields = typeof(SkillScriptExecutionRequest).GetProperties()
                .Select(property => property.GetValue(request)?.ToString() ?? string.Empty);

            fields.ShouldAllBe(field => !field.Contains(secret, StringComparison.Ordinal));
            request.ToString().ShouldNotContain(contentMarker);
            host.Logs.AllText.ShouldNotContain(secret);
            host.Logs.AllText.ShouldNotContain(contentMarker);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void The_request_text_form_leaves_out_the_content_and_the_arguments()
    {
        var request = new SkillScriptExecutionRequest
        {
            TenantId = "acme",
            SkillName = "billing",
            ScriptName = "refund",
            Origin = SkillScriptOrigin.Stored,
            Extension = "py",
            Interpreter = "/usr/bin/python3",
            Content = "print('content-in-the-request')",
            ContentSha256 = new string('A', 64),
            ArgumentsJson = """{"card":"arguments-in-the-request"}""",
            Timeout = TimeSpan.FromSeconds(30),
            MaxOutputBytes = 1024,
        };

        var text = request.ToString();

        text.ShouldContain("billing");
        text.ShouldContain("refund");
        text.ShouldContain(new string('A', 64));
        text.ShouldNotContain("content-in-the-request");
        text.ShouldNotContain("arguments-in-the-request");
    }

    public void Dispose()
    {
        try
        {
            _scratch.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Temp directory cleanup must not change the test's outcome.
        }
    }

    // ---- Harness ----------------------------------------------------------------

    private string WriteDiskSkill(string content)
    {
        var root = Path.Combine(_scratch.FullName, "skills");
        var skill = Path.Combine(root, DiskSkillName);
        Directory.CreateDirectory(Path.Combine(skill, "scripts"));

        File.WriteAllText(
            Path.Combine(skill, "SKILL.md"),
            $"---\nname: {DiskSkillName}\ndescription: Disk skill for the routing tests.\n---\nRun the script when asked.\n");
        File.WriteAllText(Path.Combine(skill, "scripts", "route.sh"), content);

        return root;
    }

    private static async Task<TraconTestHost> StartAsync(
        RecordingScriptExecutor executor,
        IReadOnlyList<(string Agent, string Skill, string Script)> runs,
        bool multiTenant = false,
        string? skillRoot = null,
        Func<IServiceProvider, IAuditLog>? auditLog = null)
    {
        var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton<ISkillScriptExecutor>(executor);

                if (auditLog is not null)
                {
                    services.AddSingleton<IAuditLog>(auditLog);
                }
            },
            configureTracon: builder =>
            {
                builder.UseSkillScripts(options =>
                {
                    options.PlatformIsolationAcknowledged = true;
                    options.AllowStoredScripts = true;
                    options.Interpreters["sh"] = "/bin/sh";

                    if (skillRoot is not null)
                    {
                        options.SkillRoots.Add(skillRoot);
                    }
                });

                if (multiTenant)
                {
                    builder.UseTenancy(static options =>
                    {
                        options.Enabled = true;
                        options.AllowHeaderResolution = true;
                    });
                }

                foreach (var (agent, skill, script) in runs)
                {
                    var provider = $"{agent}-model";

                    builder
                        .AddModelProvider(new Tracon.Testing.FakeModelProvider(provider)
                            .CallsTool("run_skill_script", new { skillName = skill, scriptName = script })
                            .EchoesLastToolResult())
                        .AddAgent(new AgentDefinition
                        {
                            Name = agent,
                            Instructions = "Run the script.",
                            Model = new ModelBinding { Provider = provider, Model = "route" },
                            SkillNames = [skill],
                        });
                }
            });

        if (!multiTenant)
        {
            await AddStandingApprovalRuleAsync(host, tenant: null);
        }

        return host;
    }

    private static async Task AddStandingApprovalRuleAsync(TraconTestHost host, string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/tracon/api/approvals/rules", UriKind.Relative))
        {
            Content = JsonContent.Create(new { toolName = "run_skill_script" }),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Saves the skill through the store, which computes the hashes the grant pins.</summary>
    private static async Task<AgentSkillDefinition> SaveSkillAsync(
        TraconTestHost host,
        string tenant,
        params (string Name, string Extension, string Content)[] scripts)
        => await host.Services.GetRequiredService<IAgentSkillStore>().SaveAsync(new AgentSkillDefinition
        {
            TenantId = tenant,
            Name = SkillName,
            Description = "Skill for the routing tests.",
            Instructions = "Run the script when asked.",
            Scripts = [.. scripts.Select(static script => new AgentSkillScriptDefinition
            {
                Name = script.Name,
                Extension = script.Extension,
                Content = script.Content,
            })],
        });

    /// <summary>A skill-wide grant through the store; the grant path itself is proven elsewhere.</summary>
    private static async Task GrantAsync(TraconTestHost host, string tenant, string skillName, string? contentHash)
        => await host.Services.GetRequiredService<ISkillScriptGrantStore>().GrantAsync(new SkillScriptGrant
        {
            TenantId = tenant,
            SkillName = skillName,
            ContentHash = contentHash,
            GrantedAt = DateTimeOffset.UtcNow,
        });

    private static async Task<IReadOnlyList<string>> DenialsAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(new Uri("/tracon/api/audit?limit=200", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = await TraconTestHost.ReadJsonAsync(response);
        var items = body.ValueKind == JsonValueKind.Array ? body : body.GetProperty("items");

        return [.. items.EnumerateArray()
            .Where(static entry => string.Equals(entry.GetProperty("action").GetString(), "script.denied", StringComparison.Ordinal))
            .Select(static entry => entry.GetProperty("after").GetString() ?? string.Empty)];
    }

    /// <summary>Runs the agent once and returns every SSE frame, joined.</summary>
    private static async Task<string> RunAsync(TraconTestHost host, string agent, string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{agent}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "run the script" }),
        };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await host.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var frames = new List<string>();

        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()))
        {
            frames.Add($"{frame.Event}: {frame.Data}");
        }

        return string.Join(Environment.NewLine, frames);
    }

    private static string Sha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>Records every request and answers without starting anything.</summary>
    private sealed class RecordingScriptExecutor : ISkillScriptExecutor
    {
        public ConcurrentQueue<SkillScriptExecutionRequest> Requests { get; } = new();

        public ValueTask<SkillScriptExecutionResult> ExecuteAsync(
            SkillScriptExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);

            return ValueTask.FromResult(new SkillScriptExecutionResult
            {
                ExitCode = 0,
                StandardOutput = $"routed-to-executor:{request.ScriptName}",
            });
        }
    }

    /// <summary>An audit log whose store refuses exactly the <c>script.run</c> entry.</summary>
    private sealed class ScriptRunRefusingAuditLog(IAuditLog inner) : IAuditLog
    {
        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => string.Equals(entry.Action, "script.run", StringComparison.Ordinal)
                ? throw new InvalidOperationException("audit store unavailable")
                : inner.WriteAsync(entry, cancellationToken);

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => inner.QueryAsync(query, cancellationToken);

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => inner.VerifyChainAsync(query, cancellationToken);
    }
}

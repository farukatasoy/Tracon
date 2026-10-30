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

    /// <summary>
    /// A script is a paid start like any tool call. Once the tree's budget is spent the
    /// runner refuses it before a grant is read or the executor is asked; a budget that is not
    /// spent leaves the same call as it was.
    /// </summary>
    /// <remarks>
    /// The script tool is built by Microsoft Agent Framework and is not a wrapper of Tracon's
    /// tool chain, so the gate is the runner's own check, proven here on the same path
    /// <see cref="An_approved_file_script_goes_to_the_registered_executor_with_its_text"/> uses.
    /// The scope is set in the test body: an ambient value assigned in a helper does not flow
    /// back to its caller.
    /// </remarks>
    [Fact]
    public async Task A_spent_budget_stops_a_file_script_before_the_executor_is_asked()
    {
        var content = "echo budget\n";
        var root = WriteDiskSkill(content);
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(executor, runs: [], skillRoot: root);
        await GrantAsync(host, "default", DiskSkillName, contentHash: null);

        using var source = host.Services.GetRequiredService<SkillScriptSupport>().CreateFileSource().ShouldNotBeNull();
        var skill = (await source.GetSkillsAsync(null!, CancellationToken.None))
            .ShouldHaveSingleItem()
            .ShouldBeOfType<Microsoft.Agents.AI.AgentFileSkill>();
        var script = (await skill.GetScriptAsync(DiskScriptName, CancellationToken.None)).ShouldNotBeNull();

        var spent = new AgentRunBudget { MaxTotalCost = 1m };
        spent.RecordUsage(0, 5m);

        TraconRunContext.SetCurrent(new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), Budget = spent });

        try
        {
            var refused = await Should.ThrowAsync<TraconRunBudgetExceededException>(
                () => script.RunAsync(skill, arguments: null, host.Services, CancellationToken.None));

            refused.Message.ShouldContain("cost budget is exhausted", Case.Sensitive);
            refused.Message.ShouldEndWith("The tool call did not start.", Case.Sensitive);
            executor.Requests.ShouldBeEmpty("the executor was asked to run a script of a spent tree");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        // The control: the same call with room left in the budget runs.
        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            Budget = new AgentRunBudget { MaxTotalCost = 1m },
        });

        try
        {
            var output = await script.RunAsync(skill, arguments: null, host.Services, CancellationToken.None);

            output.ShouldBeOfType<string>().ShouldContain($"routed-to-executor:{DiskScriptName}");
            executor.Requests.ShouldHaveSingleItem();
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    /// <summary>
    /// An agent names a skill that exists only under a skill root, and an HTTP
    /// run reaches its file script: compilation, Microsoft Agent Framework's file
    /// source, the runner and the registered executor.
    /// </summary>
    /// <remarks>
    /// Before, the catalog and the validator looked for the name only in code and in
    /// the store; compilation refused it with "the skill was not found", and no HTTP
    /// run could ever reach a script on disk.
    /// </remarks>
    [Fact]
    public async Task An_agent_that_names_a_disk_skill_runs_its_file_script_from_an_HTTP_run()
    {
        var sentinel = Path.Combine(_scratch.FullName, "disk-run-locally");
        var content = $"touch '{sentinel}'\necho file-local\n";
        var root = WriteDiskSkill(content);
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(executor, [("disk-agent", DiskSkillName, DiskScriptName)], skillRoot: root);
        await GrantAsync(host, "default", DiskSkillName, contentHash: null);

        (await RunAsync(host, "disk-agent")).ShouldContain($"routed-to-executor:{DiskScriptName}");

        File.Exists(sentinel).ShouldBeFalse("A local process ran the file script although an executor is registered.");

        var request = executor.Requests.ShouldHaveSingleItem();
        request.Origin.ShouldBe(SkillScriptOrigin.File);
        request.SkillName.ShouldBe(DiskSkillName);
        request.ScriptName.ShouldBe(DiskScriptName);
        request.Content.ShouldBe(content);
        request.SourcePath.ShouldBe(Path.Combine(root, DiskSkillName, "scripts", "route.sh"));
    }

    [Fact]
    public async Task An_agent_saved_over_HTTP_may_name_a_disk_skill_and_not_a_missing_one()
    {
        var root = WriteDiskSkill("echo saved\n");

        await using var host = await StartAsync(new RecordingScriptExecutor(), runs: [], skillRoot: root);

        using (var saved = await host.Client.PostAsJsonAsync(
                   new Uri("/tracon/api/agents", UriKind.Relative),
                   TestData.Request("disk-named") with { SkillNames = [DiskSkillName] }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        }

        using var refused = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            TestData.Request("missing-named") with { SkillNames = ["nowhere-skill"] });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("nowhere-skill", Case.Sensitive);
    }

    [Fact]
    public async Task An_edit_to_a_disk_skill_reaches_the_next_run_of_an_agent_already_compiled()
    {
        // The compiled agent is cached; the disk skill's content is part of the key, so
        // an edit on disk compiles the agent again instead of serving the old skill list.
        var root = WriteDiskSkill("echo edit\n");
        var provider = new Tracon.Testing.FakeModelProvider("edit-model").RespondsWith("first", "second");

        await using var host = await TraconTestHost.StartAsync(configureTracon: builder => builder
            .UseSkillScripts(options =>
            {
                options.PlatformIsolationAcknowledged = true;
                options.Interpreters["sh"] = "/bin/sh";
                options.SkillRoots.Add(root);
            })
            .AddModelProvider(provider)
            .AddAgent(new AgentDefinition
            {
                Name = "edit-agent",
                Instructions = "Answer.",
                Model = new ModelBinding { Provider = "edit-model", Model = "edit" },
                SkillNames = [DiskSkillName],
            }));

        await RunAsync(host, "edit-agent");

        File.WriteAllText(
            Path.Combine(root, DiskSkillName, "SKILL.md"),
            $"---\nname: {DiskSkillName}\ndescription: Edited on disk after the first run.\n---\nRun the script when asked.\n");

        await RunAsync(host, "edit-agent");

        var requests = provider.Requests;
        requests.Count.ShouldBe(2);
        Prompt(requests[0]).ShouldNotContain("Edited on disk after the first run.", Case.Sensitive);
        Prompt(requests[1]).ShouldContain("Edited on disk after the first run.", Case.Sensitive);

        static string Prompt(Tracon.Testing.FakeModelRequest request)
            => string.Join(
                Environment.NewLine,
                [request.Options?.Instructions ?? string.Empty, .. request.Messages.Select(static message => message.Text)]);
    }

    /// <summary>
    /// The filter that keeps an agent to the skills its definition names
    /// sits above the cache of the skill list. A root with two skills must still
    /// offer only the named one.
    /// </summary>
    [Fact]
    public async Task An_agent_that_names_one_disk_skill_is_not_offered_the_others_under_the_same_root()
    {
        var root = WriteDiskSkill("echo named\n");
        WriteDiskSkillFile(root, "unnamed-skill", "UNNAMED-DISK-SKILL-3e91 is not named by any agent.");
        var provider = new Tracon.Testing.FakeModelProvider("filter-model").RespondsWith("done");

        await using var host = await StartPromptHostAsync(root, provider);

        await RunAsync(host, "prompt-agent");

        var prompt = PromptOf(provider.Requests.ShouldHaveSingleItem());
        prompt.ShouldContain("Disk skill for the routing tests.", Case.Sensitive);
        prompt.ShouldNotContain("UNNAMED-DISK-SKILL-3e91", Case.Sensitive);
    }

    /// <summary>
    /// The database source comes before the disk source. When both carry a
    /// skill of one name, the run is offered the stored one.
    /// </summary>
    [Fact]
    public async Task A_stored_skill_shadows_a_disk_skill_of_the_same_name_in_a_run()
    {
        var root = WriteDiskSkill("echo disk\n");
        var provider = new Tracon.Testing.FakeModelProvider("shadow-model").RespondsWith("done");

        await using var host = await StartPromptHostAsync(root, provider);
        await host.Services.GetRequiredService<IAgentSkillStore>().SaveAsync(new AgentSkillDefinition
        {
            TenantId = "default",
            Name = DiskSkillName,
            Description = "STORED-SHADOW-5b08 wins over the disk skill.",
            Instructions = "Answer.",
        });

        await RunAsync(host, "prompt-agent");

        var prompt = PromptOf(provider.Requests.ShouldHaveSingleItem());
        prompt.ShouldContain("STORED-SHADOW-5b08", Case.Sensitive);
        prompt.ShouldNotContain("Disk skill for the routing tests.", Case.Sensitive);
    }

    /// <summary>
    /// One content limit for every script: a file under a skill root larger than
    /// <c>MaxScriptContentLength</c> is denied before the executor and before the
    /// <c>script.run</c> entry, from MAF's own file source.
    /// </summary>
    [Fact]
    public async Task A_file_script_past_the_content_limit_never_reaches_the_executor()
    {
        var root = WriteDiskSkill($"# {new string('x', 4_096)}\necho too-large\n");
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(executor, runs: [], skillRoot: root, maxScriptContentLength: 1_024);
        await GrantAsync(host, "default", DiskSkillName, contentHash: null);

        using var source = host.Services.GetRequiredService<SkillScriptSupport>().CreateFileSource().ShouldNotBeNull();
        var skill = (await source.GetSkillsAsync(null!, CancellationToken.None))
            .ShouldHaveSingleItem()
            .ShouldBeOfType<Microsoft.Agents.AI.AgentFileSkill>();
        var script = (await skill.GetScriptAsync(DiskScriptName, CancellationToken.None)).ShouldNotBeNull();

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await script.RunAsync(skill, arguments: null, host.Services, CancellationToken.None));

        exception.Message.ShouldContain("exceeds the 1024-byte limit");
        executor.Requests.ShouldBeEmpty();
        (await DenialsAsync(host)).ShouldHaveSingleItem().ShouldContain("exceeds the 1024-byte limit");
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
        ScriptRunRefusingAuditLog? refusing = null;

        await using var host = await StartAsync(
            executor,
            [("route-agent-1", SkillName, "hello")],
            auditLog: provider => refusing = new ScriptRunRefusingAuditLog(new InMemoryAuditLog(provider.GetRequiredService<ITenantContext>())));

        var saved = await SaveSkillAsync(host, tenant: "default", ("hello", "sh", "echo audited"));
        await GrantAsync(host, "default", SkillName, saved.ScriptSetHash);

        (await RunAsync(host, "route-agent-1")).ShouldNotContain("routed-to-executor");

        // The positive control: the run DID reach the audit write and was refused
        // there - not stopped earlier by a gate this test did not mean to close.
        refusing.ShouldNotBeNull().ScriptRunAttempts.ShouldBe(1);
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

            // Every field, serialized whole: a value that only a field's ToString()
            // would hide (a collection, a nested record) is still searched.
            JsonSerializer.Serialize(request).ShouldNotContain(secret);
            request.Content.ShouldContain(contentMarker);
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

    /// <summary>Writes a skill with no script next to the others under the root.</summary>
    private static void WriteDiskSkillFile(string root, string name, string description)
    {
        var skill = Directory.CreateDirectory(Path.Combine(root, name));

        File.WriteAllText(
            Path.Combine(skill.FullName, "SKILL.md"),
            $"---\nname: {name}\ndescription: {description}\n---\nAnswer when asked.\n");
    }

    /// <summary>A host whose one agent names <see cref="DiskSkillName"/> and answers from a script with no tool call.</summary>
    private static Task<TraconTestHost> StartPromptHostAsync(string skillRoot, Tracon.Testing.FakeModelProvider provider)
        => TraconTestHost.StartAsync(configureTracon: builder => builder
            .UseSkillScripts(options =>
            {
                options.PlatformIsolationAcknowledged = true;
                options.Interpreters["sh"] = "/bin/sh";
                options.SkillRoots.Add(skillRoot);
            })
            .AddModelProvider(provider)
            .AddAgent(new AgentDefinition
            {
                Name = "prompt-agent",
                Instructions = "Answer.",
                Model = new ModelBinding { Provider = provider.Name, Model = "prompt" },
                SkillNames = [DiskSkillName],
            }));

    private static string PromptOf(Tracon.Testing.FakeModelRequest request)
        => string.Join(
            Environment.NewLine,
            [request.Options?.Instructions ?? string.Empty, .. request.Messages.Select(static message => message.Text)]);

    private static async Task<TraconTestHost> StartAsync(
        RecordingScriptExecutor executor,
        IReadOnlyList<(string Agent, string Skill, string Script)> runs,
        bool multiTenant = false,
        string? skillRoot = null,
        Func<IServiceProvider, IAuditLog>? auditLog = null,
        int? maxScriptContentLength = null)
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

                    if (maxScriptContentLength is { } limit)
                    {
                        options.MaxScriptContentLength = limit;
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

    /// <summary>An audit log whose store refuses exactly the <c>script.run</c> entry, and counts the attempts.</summary>
    private sealed class ScriptRunRefusingAuditLog(IAuditLog inner) : IAuditLog
    {
        private int _scriptRunAttempts;

        public int ScriptRunAttempts => Volatile.Read(ref _scriptRunAttempts);

        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (string.Equals(entry.Action, "script.run", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _scriptRunAttempts);
                throw new InvalidOperationException("audit store unavailable");
            }

            return inner.WriteAsync(entry, cancellationToken);
        }

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => inner.QueryAsync(query, cancellationToken);

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => inner.VerifyChainAsync(query, cancellationToken);
    }
}

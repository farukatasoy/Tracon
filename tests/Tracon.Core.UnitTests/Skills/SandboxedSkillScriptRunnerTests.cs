using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Skills;

/// <summary>
/// Verifies the script runner's execution gates.
/// </summary>
/// <remarks>
/// These tests measure <strong>denial</strong> behavior more than
/// functionality: a gate silently opening means unauthorized code runs on
/// the server.
/// </remarks>
public sealed class SandboxedSkillScriptRunnerTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task The_script_does_not_run_when_the_feature_is_disabled()
    {
        var log = new InMemoryAuditLog();
        using var runner = CreateRunner(log, configure: options => options.Enabled = false);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(Demo(EchoScript()), "echo", null, CancellationToken.None));

        exception.Message.ShouldContain("disabled");
    }

    [Fact]
    public async Task The_script_is_denied_and_written_to_the_audit_trail_when_there_is_no_grant()
    {
        var log = new InMemoryAuditLog();
        using var runner = CreateRunner(log);

        await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(Demo(EchoScript()), "echo", null, CancellationToken.None));

        var entries = await log.QueryAsync(new AuditQuery { TenantId = "default" });
        var denied = entries.Single(entry => string.Equals(entry.Action, "script.denied", StringComparison.Ordinal));

        // HATA-K-skill-audit-json (2026-08-15): 'After' is written to a real jsonb
        // column; when DenyAsync passed raw (non-JSON) text, the Postgres INSERT
        // rejected it with "22P02 invalid input syntax for type json" and the
        // audit trail entry NEVER got created. The in-memory fake log cannot
        // catch this (it does not validate JSON) -- so it is parsed explicitly here.
        Should.NotThrow(() => JsonDocument.Parse(denied.After!));
    }

    [Fact]
    public async Task An_expired_grant_is_invalid()
    {
        var log = new InMemoryAuditLog();
        var grants = new InMemorySkillScriptGrantStore();
        await grants.GrantAsync(new SkillScriptGrant
        {
            TenantId = "default",
            SkillName = "demo",
            GrantedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        using var runner = CreateRunner(log, grants);

        await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(Demo(EchoScript()), "echo", null, CancellationToken.None));
    }

    [Fact]
    public async Task No_script_runs_with_an_empty_interpreter_list()
    {
        var log = new InMemoryAuditLog();
        using var runner = CreateRunner(log, await GrantAllAsync(Demo(EchoScript())), options => options.Interpreters.Clear());

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(Demo(EchoScript()), "echo", null, CancellationToken.None));

        exception.Message.ShouldContain("interpreter");
    }

    [Fact]
    public async Task The_script_does_not_run_when_the_audit_trail_cannot_be_written()
    {
        // A deliberate exception to Phase 9's "observability does not break
        // functionality" rule: a run whose record cannot be kept must never happen.
        using var runner = CreateRunner(new ThrowingAuditLog(), await GrantAllAsync(Demo(EchoScript())));

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(Demo(EchoScript()), "echo", null, CancellationToken.None));

        exception.Message.ShouldContain("audit trail");
    }

    [Fact]
    public async Task A_stored_script_is_denied_when_disabled()
    {
        using var runner = CreateRunner(
            new InMemoryAuditLog(),
            await GrantAllAsync(Demo(EchoScript())),
            options => options.AllowStoredScripts = false);

        await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(Demo(EchoScript()), "echo", null, CancellationToken.None));
    }

    [Fact]
    public async Task An_oversized_argument_is_rejected()
    {
        using var runner = CreateRunner(new InMemoryAuditLog(), await GrantAllAsync(Demo(EchoScript())), options => options.MaxArgumentBytes = 8);
        var arguments = JsonDocument.Parse("""{"value":"a very long argument text"}""").RootElement;

        await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(Demo(EchoScript()), "echo", arguments, CancellationToken.None));
    }

    [Fact]
    public async Task An_authorized_script_actually_runs_and_is_written_to_the_audit_trail()
    {
        // Phase 11's proof test: with all gates open, the script actually runs
        // in a real OS process and its output is returned to the model.
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash not found.");

        var log = new InMemoryAuditLog();
        var script = new AgentSkillScriptDefinition
        {
            Name = "echo",
            Extension = "sh",
            Content = "echo hello-tracon",
        };
        var skill = Demo(script);

        using var runner = CreateRunner(log, await GrantAllAsync(skill), options =>
        {
            options.Interpreters.Clear();
            options.Interpreters["sh"] = "/bin/bash";
        });

        var output = await runner.RunStoredScriptAsync(skill, "echo", null, CancellationToken.None);

        output?.ToString().ShouldNotBeNull().ShouldContain("hello-tracon");

        var entries = await log.QueryAsync(new AuditQuery { TenantId = "default" });
        entries.ShouldContain(entry => entry.Action == "script.run");
    }

    /// <summary>
    /// A successful run leaves the span carrying its exit code, its duration
    /// and an Ok status.
    /// </summary>
    /// <remarks>
    /// 🚨 The manual run measured six successful executions whose
    /// <c>execute_skill_script</c> span carried only the skill and script
    /// names: no <c>exit_code</c>, no <c>duration_ms</c>, status Unset. The
    /// tags are set right after the process returns, so the question this
    /// test answers is whether they reach the span at all - a span that is
    /// read before it stops would show exactly that shape.
    /// </remarks>
    [Fact]
    public async Task A_successful_run_leaves_its_exit_code_and_duration_on_the_span()
    {
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash not found.");

        var stopped = new ConcurrentQueue<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue,
        };

        ActivitySource.AddActivityListener(listener);

        var spanSkill = Demo(new AgentSkillScriptDefinition { Name = "echo-ok-span", Extension = "sh", Content = "echo hello-tracon" });

        using var runner = CreateRunner(new InMemoryAuditLog(), await GrantAllAsync(spanSkill), options =>
        {
            options.Interpreters.Clear();
            options.Interpreters["sh"] = "/bin/bash";
        });

        await runner.RunStoredScriptAsync(
            spanSkill,
            "echo-ok-span",
            arguments: null,
            CancellationToken.None);

        // The listener sees every span this ActivitySource produces, including
        // the ones other tests in this assembly open in parallel; the script
        // name is what picks out this run's.
        var span = SpanFor(stopped, "echo-ok-span");

        span.GetTagItem(TraconDiagnostics.Tags.ExitCode).ShouldBe(0);
        span.GetTagItem(TraconDiagnostics.Tags.DurationMs).ShouldNotBeNull();
        span.Status.ShouldBe(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// A run a gate stops leaves that on the span too.
    /// </summary>
    /// <remarks>
    /// 🚨 This is the shape the manual run reported: a span carrying the
    /// skill and the script name, no exit code, no duration and status Unset -
    /// a span that reads as though it never finished. A denied run never
    /// reaches the process, so the outcome every other path writes after it
    /// was never written, and the only trace of the denial was an error.type
    /// on MAF's own execute_tool span above, which names the exception type
    /// and not the gate that closed.
    /// </remarks>
    [Fact]
    public async Task A_denied_run_leaves_the_gate_that_stopped_it_on_the_span()
    {
        var stopped = new ConcurrentQueue<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue,
        };

        ActivitySource.AddActivityListener(listener);

        // No grant: the gate this run hits is the one that matters most.
        using var runner = CreateRunner(new InMemoryAuditLog());

        await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(
                Demo(EchoScript() with { Name = "echo-denied-span" }),
                "echo-denied-span",
                null,
                CancellationToken.None));

        var span = SpanFor(stopped, "echo-denied-span");

        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.GetTagItem(TraconDiagnostics.Tags.ScriptDenialReason)
            .ShouldBeOfType<string>()
            .ShouldContain("execution grant");
    }

    /// <summary>
    /// A grant written before content pinning existed (or given for a name no stored
    /// skill carried) pins nothing. Backfilling it would silently approve content
    /// that may have changed, so it refuses every stored script instead.
    /// </summary>
    [Fact]
    public async Task A_grant_that_pins_no_content_refuses_a_stored_script()
    {
        var log = new InMemoryAuditLog();
        var grants = new InMemorySkillScriptGrantStore();
        await grants.GrantAsync(new SkillScriptGrant { TenantId = "default", SkillName = "demo", GrantedAt = DateTimeOffset.UtcNow });
        using var runner = CreateRunner(log, grants);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(Demo(EchoScript()), "echo", null, CancellationToken.None));

        exception.Message.ShouldContain("does not pin the script content");
        (await DenialReasonsAsync(log)).ShouldContain(reason => reason.Contains("does not pin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_script_grant_refuses_the_script_once_its_content_changes()
    {
        var log = new InMemoryAuditLog();
        var original = EchoScript();
        var grants = new InMemorySkillScriptGrantStore();
        await grants.GrantAsync(new SkillScriptGrant
        {
            TenantId = "default",
            SkillName = "demo",
            ScriptName = "echo",
            ContentHash = original.ContentHash,
            GrantedAt = DateTimeOffset.UtcNow,
        });
        using var runner = CreateRunner(log, grants);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(
                Demo(original with { Content = "print('changed')" }),
                "echo",
                null,
                CancellationToken.None));

        exception.Message.ShouldContain("content changed since the grant");
    }

    [Fact]
    public async Task A_skill_wide_grant_refuses_every_script_once_the_set_changes()
    {
        var log = new InMemoryAuditLog();
        var before = Demo(EchoScript());
        using var runner = CreateRunner(log, await GrantAllAsync(before));

        var after = Demo(EchoScript(), EchoScript() with { Name = "added" });

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(after, "echo", null, CancellationToken.None));

        exception.Message.ShouldContain("content changed since the grant");
    }

    /// <summary>
    /// The two refusals are different facts - "nobody pinned anything" and "what
    /// was pinned is not what is here" - and an operator reading the audit trail or
    /// the span has to tell them apart.
    /// </summary>
    [Fact]
    public async Task The_two_pin_refusals_name_different_reasons_on_the_span_and_in_the_audit_trail()
    {
        var stopped = new ConcurrentQueue<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Enqueue,
        };

        ActivitySource.AddActivityListener(listener);

        var log = new InMemoryAuditLog();
        var unpinned = Demo(EchoScript() with { Name = "unpinned-span" });
        var unpinnedGrants = new InMemorySkillScriptGrantStore();
        await unpinnedGrants.GrantAsync(new SkillScriptGrant { TenantId = "default", SkillName = "demo", GrantedAt = DateTimeOffset.UtcNow });

        using (var runner = CreateRunner(log, unpinnedGrants))
        {
            await Should.ThrowAsync<TraconException>(
                async () => await runner.RunStoredScriptAsync(unpinned, "unpinned-span", null, CancellationToken.None));
        }

        var changed = Demo(EchoScript() with { Name = "changed-span" });

        using (var runner = CreateRunner(log, await GrantAllAsync(Demo(EchoScript() with { Name = "changed-span", Content = "print('v1')" }))))
        {
            await Should.ThrowAsync<TraconException>(
                async () => await runner.RunStoredScriptAsync(changed, "changed-span", null, CancellationToken.None));
        }

        var unpinnedReason = SpanFor(stopped, "unpinned-span").GetTagItem(TraconDiagnostics.Tags.ScriptDenialReason).ShouldBeOfType<string>();
        var changedReason = SpanFor(stopped, "changed-span").GetTagItem(TraconDiagnostics.Tags.ScriptDenialReason).ShouldBeOfType<string>();

        unpinnedReason.ShouldContain("does not pin the script content");
        changedReason.ShouldContain("content changed since the grant");
        unpinnedReason.ShouldNotBe(changedReason, StringComparer.Ordinal);

        var denials = await DenialReasonsAsync(log);
        denials.ShouldContain(reason => reason.Contains("does not pin", StringComparison.Ordinal));
        denials.ShouldContain(reason => reason.Contains("content changed since the grant", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_script_name_the_skill_does_not_carry_is_refused()
    {
        var log = new InMemoryAuditLog();
        var skill = Demo(EchoScript());
        using var runner = CreateRunner(log, await GrantAllAsync(skill));

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(skill, "not-in-the-skill", null, CancellationToken.None));

        exception.Message.ShouldContain("has no script named 'not-in-the-skill'");
    }

    /// <summary>
    /// A file script is not pinned: its content is part of the deployment. It runs
    /// under a grant with or without a hash; what it needs is the grant itself.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_file_script_runs_under_a_grant_with_or_without_a_hash(bool hashed)
    {
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash not found.");

        var directory = Directory.CreateTempSubdirectory("tracon-file-script-").FullName;
        _tempDirectories.Add(directory);
        var path = Path.Combine(directory, "hello.sh");
        await File.WriteAllTextAsync(path, "echo from-disk");

        var grants = new InMemorySkillScriptGrantStore();
        await grants.GrantAsync(new SkillScriptGrant
        {
            TenantId = "default",
            SkillName = "disk-skill",
            ContentHash = hashed ? new string('A', 64) : null,
            GrantedAt = DateTimeOffset.UtcNow,
        });

        using var runner = CreateRunner(new InMemoryAuditLog(), grants, options =>
        {
            options.Interpreters.Clear();
            options.Interpreters["sh"] = "/bin/bash";
        });

        var output = await runner.RunFileScriptAsync("disk-skill", "hello", path, null, null, CancellationToken.None);

        output?.ToString().ShouldNotBeNull().ShouldContain("from-disk");
    }

    [Fact]
    public async Task A_file_script_still_needs_a_grant()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-file-script-").FullName;
        _tempDirectories.Add(directory);
        var path = Path.Combine(directory, "hello.py");
        await File.WriteAllTextAsync(path, "print('from-disk')");

        using var runner = CreateRunner(new InMemoryAuditLog());

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunFileScriptAsync("disk-skill", "hello", path, null, null, CancellationToken.None));

        exception.Message.ShouldContain("no valid execution grant");
    }

    private static async Task<IReadOnlyList<string>> DenialReasonsAsync(InMemoryAuditLog log)
        => [.. (await log.QueryAsync(new AuditQuery { TenantId = "default" }))
            .Where(static entry => string.Equals(entry.Action, "script.denied", StringComparison.Ordinal))
            .Select(static entry => entry.After ?? string.Empty)];

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Temp directory cleanup must not change the test's outcome.
            }
        }
    }

    /// <summary>
    /// The one span this test opened, out of everything the listener saw.
    /// </summary>
    /// <remarks>
    /// An ActivityListener is process-wide: it receives the spans every other
    /// test in the assembly opens on the same source while it is attached, so
    /// a single-item assertion here fails for a reason that has nothing to do
    /// with the behavior under test. The same reason makes the collection a
    /// concurrent one: another test's span stops on ITS thread while this one
    /// reads, and a plain list threw "Collection was modified" (measured,
    /// Phase 184).
    /// </remarks>
    private static Activity SpanFor(IEnumerable<Activity> stopped, string scriptName)
        => stopped.Single(activity =>
            string.Equals(activity.OperationName, TraconDiagnostics.SkillScriptActivityName, StringComparison.Ordinal)
            && string.Equals(
                activity.GetTagItem(TraconDiagnostics.Tags.ScriptName) as string,
                scriptName,
                StringComparison.Ordinal));

    private static AgentSkillScriptDefinition EchoScript() => new()
    {
        Name = "echo",
        Extension = "py",
        Content = "print('hello')",
    };

    private static AgentSkillDefinition Demo(params AgentSkillScriptDefinition[] scripts) => new()
    {
        TenantId = "default",
        Name = "demo",
        Description = "Runner test skill.",
        Instructions = "Run the script.",
        Scripts = scripts,
    };

    /// <summary>A skill-wide grant pinned to the skill's current script set.</summary>
    private static async Task<InMemorySkillScriptGrantStore> GrantAllAsync(AgentSkillDefinition skill)
    {
        var grants = new InMemorySkillScriptGrantStore();
        await grants.GrantAsync(new SkillScriptGrant
        {
            TenantId = "default",
            SkillName = skill.Name,
            ContentHash = skill.ScriptSetHash,
            GrantedAt = DateTimeOffset.UtcNow,
        });
        return grants;
    }

    private static SandboxedSkillScriptRunner CreateRunner(
        IAuditLog auditLog,
        ISkillScriptGrantStore? grants = null,
        Action<TraconSkillScriptOptions>? configure = null)
    {
        var options = new TraconOptions();
        options.Skills.Scripts.Enabled = true;
        options.Skills.Scripts.PlatformIsolationAcknowledged = true;
        options.Skills.Scripts.AllowStoredScripts = true;
        options.Skills.Scripts.Interpreters["py"] = "python3";
        configure?.Invoke(options.Skills.Scripts);

        var wrapper = Options.Create(options);
        return new SandboxedSkillScriptRunner(
            wrapper,
            new SingleTenantContext(wrapper),
            grants ?? new InMemorySkillScriptGrantStore(),
            auditLog,
            new NullAuditActorResolver(),
            NullLogger<SandboxedSkillScriptRunner>.Instance);
    }

    private sealed class NullAuditActorResolver : IAuditActorResolver
    {
        public string? Resolve() => null;
    }

    private sealed class ThrowingAuditLog : IAuditLog
    {
        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store unavailable");

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Array.Empty<AuditEntry>());

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by this test");
    }
}

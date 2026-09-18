using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Skills;

/// <summary>
/// Verifies the sandbox's security gates.
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
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));

        exception.Message.ShouldContain("disabled");
    }

    [Fact]
    public async Task The_script_is_denied_and_written_to_the_audit_trail_when_there_is_no_grant()
    {
        var log = new InMemoryAuditLog();
        using var runner = CreateRunner(log);

        await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));

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
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));
    }

    [Fact]
    public async Task No_script_runs_with_an_empty_interpreter_list()
    {
        var log = new InMemoryAuditLog();
        using var runner = CreateRunner(log, await GrantAllAsync(), options => options.Interpreters.Clear());

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));

        exception.Message.ShouldContain("interpreter");
    }

    [Fact]
    public async Task The_script_does_not_run_when_the_audit_trail_cannot_be_written()
    {
        // A deliberate exception to Phase 9's "observability does not break
        // functionality" rule: a run whose record cannot be kept must never happen.
        using var runner = CreateRunner(new ThrowingAuditLog(), await GrantAllAsync());

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));

        exception.Message.ShouldContain("audit trail");
    }

    [Fact]
    public async Task A_stored_script_is_denied_when_disabled()
    {
        using var runner = CreateRunner(
            new InMemoryAuditLog(),
            await GrantAllAsync(),
            options => options.AllowStoredScripts = false);

        await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));
    }

    [Fact]
    public async Task An_oversized_argument_is_rejected()
    {
        using var runner = CreateRunner(new InMemoryAuditLog(), await GrantAllAsync(), options => options.MaxArgumentBytes = 8);
        var arguments = JsonDocument.Parse("""{"value":"a very long argument text"}""").RootElement;

        await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), arguments, CancellationToken.None));
    }

    [Fact]
    public async Task An_authorized_script_actually_runs_and_is_written_to_the_audit_trail()
    {
        // Phase 11's proof test: with all gates open, the script actually runs
        // in a real OS process and its output is returned to the model.
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash not found.");

        var log = new InMemoryAuditLog();
        using var runner = CreateRunner(log, await GrantAllAsync(), options =>
        {
            options.Interpreters.Clear();
            options.Interpreters["sh"] = "/bin/bash";
        });

        var script = new AgentSkillScriptDefinition
        {
            Name = "echo",
            Extension = "sh",
            Content = "echo hello-tracon",
        };

        var output = await runner.RunStoredScriptAsync("demo", script, null, CancellationToken.None);

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

        var stopped = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };

        ActivitySource.AddActivityListener(listener);

        using var runner = CreateRunner(new InMemoryAuditLog(), await GrantAllAsync(), options =>
        {
            options.Interpreters.Clear();
            options.Interpreters["sh"] = "/bin/bash";
        });

        await runner.RunStoredScriptAsync(
            "demo",
            new AgentSkillScriptDefinition { Name = "echo", Extension = "sh", Content = "echo hello-tracon" },
            arguments: null,
            CancellationToken.None);

        var span = stopped.ShouldHaveSingleItem();

        span.OperationName.ShouldBe(TraconDiagnostics.SkillScriptActivityName);
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
        var stopped = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };

        ActivitySource.AddActivityListener(listener);

        // No grant: the gate this run hits is the one that matters most.
        using var runner = CreateRunner(new InMemoryAuditLog());

        await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));

        var span = stopped.ShouldHaveSingleItem();

        span.Status.ShouldBe(ActivityStatusCode.Error);
        span.GetTagItem(TraconDiagnostics.Tags.ScriptDenialReason)
            .ShouldBeOfType<string>()
            .ShouldContain("execution grant");
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Temp directory cleanup must not change the test's outcome.
            }
        }
    }

    private static AgentSkillScriptDefinition EchoScript() => new()
    {
        Name = "echo",
        Extension = "py",
        Content = "print('hello')",
    };

    private static async Task<InMemorySkillScriptGrantStore> GrantAllAsync()
    {
        var grants = new InMemorySkillScriptGrantStore();
        await grants.GrantAsync(new SkillScriptGrant
        {
            TenantId = "default",
            SkillName = "demo",
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

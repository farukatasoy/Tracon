using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Skills;

/// <summary>
/// What the runner does around a registered <see cref="ISkillScriptExecutor"/>:
/// the request it builds, how long it waits, the limits it applies again, and
/// how it records an executor that fails or breaks its contract.
/// </summary>
/// <remarks>
/// The executor here is a fake: these tests prove the runner's side of the seam.
/// That the HTTP run path reaches the registered executor at all is proven one
/// level up, in <c>SkillScriptExecutorRoutingTests</c>.
/// </remarks>
public sealed partial class SandboxedSkillScriptRunnerTests
{
    [Fact]
    public async Task An_approved_stored_script_reaches_the_executor_with_the_content_the_grant_pins()
    {
        var script = new AgentSkillScriptDefinition { Name = "echo", Extension = "py", Content = "print('pinned')" };
        var skill = Demo(script);
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0, "from-executor")));
        var arguments = JsonDocument.Parse("""{"city":"Ankara"}""").RootElement;

        using var runner = CreateExecutorRunner(new InMemoryAuditLog(), await GrantAllAsync(skill), executor);

        var output = await runner.RunStoredScriptAsync(skill, "echo", arguments, CancellationToken.None);

        output.ShouldBeOfType<string>().ShouldContain("from-executor");

        var request = executor.Requests.ShouldHaveSingleItem();
        request.TenantId.ShouldBe("default");
        request.RunId.ShouldBeNull();
        request.SkillName.ShouldBe("demo");
        request.ScriptName.ShouldBe("echo");
        request.Origin.ShouldBe(SkillScriptOrigin.Stored);
        request.Extension.ShouldBe("py");
        request.Interpreter.ShouldBe("python3");
        request.Content.ShouldBe(script.Content);
        request.ContentSha256.ShouldBe(Sha256(script.Content));
        request.SourcePath.ShouldBeNull();
        request.ArgumentsJson.ShouldBe("""{"city":"Ankara"}""");
        request.Timeout.ShouldBe(new TraconSkillScriptOptions().Timeout);
        request.MaxOutputBytes.ShouldBe(new TraconSkillScriptOptions().MaxOutputBytes);
    }

    [Fact]
    public async Task A_call_without_arguments_sends_no_arguments()
    {
        var skill = Demo(EchoScript());
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0)));

        using var runner = CreateExecutorRunner(new InMemoryAuditLog(), await GrantAllAsync(skill), executor);

        await runner.RunStoredScriptAsync(skill, "echo", arguments: null, CancellationToken.None);

        executor.Requests.ShouldHaveSingleItem().ArgumentsJson.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_inside_an_agent_run_carries_its_run_identifier()
    {
        var skill = Demo(EchoScript());
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0)));
        var (store, writer) = await StartRunAsync();
        var runId = EnterRun(writer);

        try
        {
            using var runner = CreateExecutorRunner(new InMemoryAuditLog(), await GrantAllAsync(skill), executor, runStore: store);

            await runner.RunStoredScriptAsync(skill, "echo", null, CancellationToken.None);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        executor.Requests.ShouldHaveSingleItem().RunId.ShouldBe(runId);
    }

    /// <summary>
    /// A file script is read by the runner, after the quota, and travels whole: a
    /// remote executor cannot read the host's path. Its size is not capped by the
    /// stored-script content limit, which applies to what the database stores.
    /// </summary>
    [Fact]
    public async Task A_file_script_reaches_the_executor_whole_with_its_text_and_path()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-file-script-").FullName;
        _tempDirectories.Add(directory);
        var path = Path.Combine(directory, "large.sh");
        var content = "# naïve café line\n" + string.Concat(Enumerable.Repeat("echo 0123456789abcdef\n", 10_000));
        await File.WriteAllTextAsync(path, content);

        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0)));

        using var runner = CreateExecutorRunner(
            new InMemoryAuditLog(),
            await GrantFileSkillAsync("disk-skill"),
            executor,
            options =>
            {
                options.Interpreters["sh"] = "/bin/sh";
                options.MaxScriptContentLength = 1024;
            });

        await runner.RunFileScriptAsync("disk-skill", "large", path, null, null, CancellationToken.None);

        var request = executor.Requests.ShouldHaveSingleItem();
        Encoding.UTF8.GetByteCount(request.Content).ShouldBeGreaterThan(200_000);
        request.Content.ShouldBe(content);
        request.ContentSha256.ShouldBe(Sha256(content));
        request.Origin.ShouldBe(SkillScriptOrigin.File);
        request.SourcePath.ShouldBe(path);
        request.Interpreter.ShouldBe("/bin/sh");
    }

    /// <summary>
    /// The file is read after the audit entry and the quota: a file that vanished
    /// since MAF listed it fails the call, and the executor never sees a request.
    /// </summary>
    [Fact]
    public async Task A_file_script_that_cannot_be_read_fails_the_call_without_reaching_the_executor()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-file-script-").FullName;
        _tempDirectories.Add(directory);
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0)));
        var log = new InMemoryAuditLog();

        using var runner = CreateExecutorRunner(
            log,
            await GrantFileSkillAsync("disk-skill"),
            executor,
            options => options.Interpreters["sh"] = "/bin/sh");

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunFileScriptAsync(
                "disk-skill", "gone", Path.Combine(directory, "gone.sh"), null, null, CancellationToken.None));

        exception.Message.ShouldContain("could not be read");
        executor.Requests.ShouldBeEmpty();
        (await log.QueryAsync(new AuditQuery { TenantId = "default" }))
            .ShouldContain(static entry => string.Equals(entry.Action, "script.run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stored_script_whose_name_is_not_a_plain_file_name_is_denied_before_the_executor()
    {
        var script = new AgentSkillScriptDefinition { Name = "../escape", Extension = "py", Content = "print('x')" };
        var skill = Demo(script);
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0)));
        var log = new InMemoryAuditLog();

        using var runner = CreateExecutorRunner(log, await GrantAllAsync(skill), executor);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunStoredScriptAsync(skill, "../escape", null, CancellationToken.None));

        exception.Message.ShouldContain("not a plain file name");
        executor.Requests.ShouldBeEmpty();
        (await DenialReasonsAsync(log)).ShouldContain(static reason => reason.Contains("not a plain file name", StringComparison.Ordinal));
        (await log.QueryAsync(new AuditQuery { TenantId = "default" }))
            .ShouldNotContain(static entry => string.Equals(entry.Action, "script.run", StringComparison.Ordinal));
    }

    /// <summary>
    /// The executor's own limit is not trusted: the runner cuts each stream to the
    /// byte limit again, never inside a character, and adds the one notice itself.
    /// </summary>
    [Fact]
    public async Task Output_past_the_limit_is_cut_again_and_the_notice_is_added_once()
    {
        var skill = Demo(EchoScript());
        var executor = new DelegateExecutor((_, _) => Task.FromResult(new SkillScriptExecutionResult
        {
            ExitCode = 0,
            StandardOutput = new string('é', 5_000),
            StandardError = string.Concat(Enumerable.Repeat("😀", 1_000)),
            Truncated = false,
        }));

        using var runner = CreateExecutorRunner(
            new InMemoryAuditLog(),
            await GrantAllAsync(skill),
            executor,
            options => options.MaxOutputBytes = 11);

        var text = (await runner.RunStoredScriptAsync(skill, "echo", null, CancellationToken.None)).ShouldBeOfType<string>();

        // 'é' is two bytes, so 11 bytes keep five of them; the emoji is four bytes
        // (a surrogate pair), so 11 bytes keep two whole ones and never a half.
        text.ShouldContain($"stdout:{Environment.NewLine}{new string('é', 5)}{Environment.NewLine}");
        text.ShouldContain($"stderr:{Environment.NewLine}😀😀{Environment.NewLine}");
        text.ShouldEndWith($"[Tracon: output truncated at the 11-byte limit per stream.]{Environment.NewLine}");
        text.Split("output truncated").Length.ShouldBe(2);
    }

    [Fact]
    public async Task An_executor_that_truncated_on_its_own_still_gets_the_notice()
    {
        var skill = Demo(EchoScript());
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0, "short") with { Truncated = true }));

        using var runner = CreateExecutorRunner(new InMemoryAuditLog(), await GrantAllAsync(skill), executor);

        var text = (await runner.RunStoredScriptAsync(skill, "echo", null, CancellationToken.None)).ShouldBeOfType<string>();

        text.ShouldContain("short");
        text.ShouldContain("output truncated");
    }

    /// <summary>
    /// An executor that never returns would hold a quota slot forever. The runner
    /// arms exactly <c>Timeout + 5 s</c>, gives up there, cancels the executor's
    /// token, records a timeout, and frees the slot for the next call.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_executor_past_its_timeout_is_abandoned_at_the_wait_limit_and_frees_the_quota(bool ignoresToken)
    {
        var skill = Demo(EchoScript());
        var clock = new TriggerableTimeProvider();
        var firstCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new ConcurrentQueue<CancellationToken>();
        var calls = 0;

        var executor = new DelegateExecutor(async (_, token) =>
        {
            tokens.Enqueue(token);

            if (Interlocked.Increment(ref calls) > 1)
            {
                secondCalled.TrySetResult();
                return Exited(0, "second-call");
            }

            firstCalled.TrySetResult();

            if (ignoresToken)
            {
                return await new TaskCompletionSource<SkillScriptExecutionResult>().Task;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, token); // delay: simulated
            return Exited(0);
        });

        var (store, writer) = await StartRunAsync();
        var runId = EnterRun(writer);

        try
        {
            using var runner = CreateExecutorRunner(
                new InMemoryAuditLog(),
                await GrantAllAsync(skill),
                executor,
                options =>
                {
                    options.Timeout = TimeSpan.FromSeconds(30);
                    options.MaxConcurrentPerTenant = 1;
                    options.MaxConcurrentTotal = 1;
                },
                timeProvider: clock,
                runStore: store);

            var first = runner.RunStoredScriptAsync(skill, "echo", null, CancellationToken.None);
            await firstCalled.Task.WaitAsync(TimeSpan.FromSeconds(30));

            var second = runner.RunStoredScriptAsync(skill, "echo", null, CancellationToken.None);

            // The slot is held while the first executor call runs.
            (await Task.WhenAny(secondCalled.Task, Task.Delay(TimeSpan.FromMilliseconds(300)))).ShouldNotBe(secondCalled.Task); // delay: negative
            clock.DueTimes.ShouldBe([TimeSpan.FromSeconds(35)]);

            clock.TriggerAll();

            var firstText = (await first.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeOfType<string>();
            firstText.ShouldContain("did not return in time");
            tokens.First().IsCancellationRequested.ShouldBeTrue();

            (await second.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBeOfType<string>().ShouldContain("second-call");

            var records = await store.ListToolInvocationsAsync(runId);
            records.Count.ShouldBe(2);
            records.ShouldContain(static record => string.Equals(record.Error, "Timed out; the script executor did not return in time.", StringComparison.Ordinal));
            records.ShouldContain(static record => record.Error == null);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    /// <summary>
    /// A caller who stops the run is not a timeout: the cancellation propagates,
    /// nothing is recorded as a failed script, and the slot is freed.
    /// </summary>
    [Fact]
    public async Task A_caller_cancellation_propagates_and_is_not_recorded_as_a_timeout()
    {
        var skill = Demo(EchoScript());
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var executor = new DelegateExecutor(async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) > 1)
            {
                return Exited(0, "after-cancel");
            }

            called.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token); // delay: simulated
            return Exited(0);
        });

        var (store, writer) = await StartRunAsync();
        var runId = EnterRun(writer);

        try
        {
            using var runner = CreateExecutorRunner(
                new InMemoryAuditLog(),
                await GrantAllAsync(skill),
                executor,
                options =>
                {
                    options.MaxConcurrentPerTenant = 1;
                    options.MaxConcurrentTotal = 1;
                },
                runStore: store);

            using var cancellation = new CancellationTokenSource();
            var run = runner.RunStoredScriptAsync(skill, "echo", null, cancellation.Token);
            await called.Task.WaitAsync(TimeSpan.FromSeconds(30));

            await cancellation.CancelAsync();

            var thrown = await Should.ThrowAsync<Exception>(async () => await run.WaitAsync(TimeSpan.FromSeconds(30)));
            thrown.ShouldBeAssignableTo<OperationCanceledException>();

            (await store.ListToolInvocationsAsync(runId)).ShouldBeEmpty();

            (await runner.RunStoredScriptAsync(skill, "echo", null, CancellationToken.None))
                .ShouldBeOfType<string>().ShouldContain("after-cancel");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public async Task The_concurrency_quota_applies_to_a_custom_executor()
    {
        var skill = Demo(EchoScript());
        var release = new TaskCompletionSource<SkillScriptExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        var executor = new DelegateExecutor((_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstCalled.TrySetResult();
                return release.Task;
            }

            return Task.FromResult(Exited(0));
        });

        using var runner = CreateExecutorRunner(
            new InMemoryAuditLog(),
            await GrantAllAsync(skill),
            executor,
            options =>
            {
                options.MaxConcurrentPerTenant = 1;
                options.MaxConcurrentTotal = 1;
            });

        var first = runner.RunStoredScriptAsync(skill, "echo", null, CancellationToken.None);
        await firstCalled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var second = runner.RunStoredScriptAsync(skill, "echo", null, CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(300)); // delay: negative
        executor.Requests.Count.ShouldBe(1);

        release.SetResult(Exited(0));

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        executor.Requests.Count.ShouldBe(2);
    }

    public static TheoryData<string, string> BrokenResults() => new()
    {
        { "null", "returned no result" },
        { "timeout-with-exit-code", "reported a timeout together with an exit code" },
        { "neither", "reported neither an exit code nor a timeout" },
        { "null-output", "returned a result with no output text" },
    };

    /// <summary>
    /// A result that cannot be reported honestly fails the call: the span, the
    /// metric, and the tool call record say so, and the model sees a failure.
    /// </summary>
    [Theory]
    [MemberData(nameof(BrokenResults))]
    public async Task An_inconsistent_result_fails_the_call_and_is_recorded(string shape, string expected)
    {
        SkillScriptExecutionResult? broken = shape switch
        {
            "null" => null,
            "timeout-with-exit-code" => new SkillScriptExecutionResult { TimedOut = true, ExitCode = 0 },
            "neither" => new SkillScriptExecutionResult(),
            _ => new SkillScriptExecutionResult { ExitCode = 0, StandardOutput = null! },
        };

        await ShouldFailAndRecordAsync(
            $"broken-{shape}",
            new DelegateExecutor((_, _) => Task.FromResult(broken!)),
            expected,
            hiddenDetail: null);
    }

    public static TheoryData<string> ExecutorFailures() => ["sync-throw", "async-throw", "network"];

    /// <summary>
    /// An executor that throws fails the call. Its message can carry the host's
    /// internals, so it reaches the server log and nothing the tenant reads.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExecutorFailures))]
    public async Task An_executor_that_throws_fails_the_call_without_its_message(string failure)
    {
        const string detail = "internal-endpoint-10.0.0.7";

        DelegateExecutor executor = failure switch
        {
            "sync-throw" => new DelegateExecutor((_, _) => throw new InvalidOperationException(detail)),
            "async-throw" => new DelegateExecutor(async (_, _) =>
            {
                await Task.Yield();
                throw new InvalidOperationException(detail);
            }),
            _ => new DelegateExecutor(async (_, _) =>
            {
                await Task.Yield();
                throw new HttpRequestException(detail);
            }),
        };

        await ShouldFailAndRecordAsync(
            $"throws-{failure}",
            executor,
            string.Equals(failure, "network", StringComparison.Ordinal) ? "HttpRequestException" : "InvalidOperationException",
            hiddenDetail: detail);
    }

    [Fact]
    public void The_output_cap_never_splits_a_character()
    {
        var truncated = false;

        SkillScriptOutput.Cap("abc", 3, ref truncated).ShouldBe("abc");
        truncated.ShouldBeFalse();

        SkillScriptOutput.Cap("😀😀", 7, ref truncated).ShouldBe("😀");
        truncated.ShouldBeTrue();

        var again = false;
        SkillScriptOutput.Cap("éé", 1, ref again).ShouldBeEmpty();
        again.ShouldBeTrue();
    }

    private static async Task ShouldFailAndRecordAsync(
        string scriptName,
        DelegateExecutor executor,
        string expected,
        string? hiddenDetail)
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

        var skill = Demo(EchoScript() with { Name = scriptName });
        var (store, writer) = await StartRunAsync();
        var runId = EnterRun(writer);

        try
        {
            using var runner = CreateExecutorRunner(new InMemoryAuditLog(), await GrantAllAsync(skill), executor, runStore: store);

            var exception = await Should.ThrowAsync<TraconException>(
                async () => await runner.RunStoredScriptAsync(skill, scriptName, null, CancellationToken.None));

            exception.Message.ShouldContain(expected);

            var record = (await store.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();
            record.Error.ShouldNotBeNull().ShouldContain(expected);
            record.Result.ShouldBeNull();

            var span = SpanFor(stopped, scriptName);
            span.Status.ShouldBe(ActivityStatusCode.Error);
            span.StatusDescription.ShouldNotBeNull().ShouldContain(expected);

            if (hiddenDetail is not null)
            {
                exception.Message.ShouldNotContain(hiddenDetail);
                record.Error.ShouldNotContain(hiddenDetail);
                span.StatusDescription.ShouldNotContain(hiddenDetail);
            }
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    private static SkillScriptExecutionResult Exited(int exitCode, string standardOutput = "")
        => new() { ExitCode = exitCode, StandardOutput = standardOutput };

    private static string Sha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>Starts a recorded run and makes it the current one, as an agent run does.</summary>
    /// <remarks>
    /// The run context is an <c>AsyncLocal</c>. Setting it inside this async
    /// method would not flow back to the test, so the run is entered by the
    /// synchronous <see cref="EnterRun"/> in the test's own body. The caller
    /// clears the run context in a <c>finally</c>.
    /// </remarks>
    private static async Task<(InMemoryRunStore Store, RunEventWriter Writer)> StartRunAsync()
    {
        var store = new InMemoryRunStore();
        var writer = new RunEventWriter(store, new TraconRunRecordingOptions(), NullLogger.Instance, TraconId.NewId(), metrics: null);

        await writer.StartAsync(
            new RunStartInfo { RunId = writer.RunId, AgentName = "script-agent", StartedAt = DateTimeOffset.UtcNow },
            query: "run the script");

        return (store, writer);
    }

    /// <summary>Makes a run started by <see cref="StartRunAsync"/> the current one.</summary>
    /// <param name="writer">The run's writer.</param>
    /// <returns>The run identifier.</returns>
    private static Guid EnterRun(RunEventWriter writer)
    {
        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = writer.RunId,
            RootRunId = writer.RunId,
            Writer = writer,
            TenantId = "default",
        });

        return writer.RunId;
    }

    private static async Task<InMemorySkillScriptGrantStore> GrantFileSkillAsync(string skillName)
    {
        var grants = new InMemorySkillScriptGrantStore();
        await grants.GrantAsync(new SkillScriptGrant { TenantId = "default", SkillName = skillName, GrantedAt = DateTimeOffset.UtcNow });
        return grants;
    }

    private static SandboxedSkillScriptRunner CreateExecutorRunner(
        IAuditLog auditLog,
        ISkillScriptGrantStore grants,
        ISkillScriptExecutor executor,
        Action<TraconSkillScriptOptions>? configure = null,
        TimeProvider? timeProvider = null,
        IRunStore? runStore = null)
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
            grants,
            auditLog,
            new NullAuditActorResolver(),
            executor,
            NullLogger<SandboxedSkillScriptRunner>.Instance,
            runStore,
            metrics: null,
            timeProvider);
    }

    /// <summary>An executor whose behavior each test writes inline, and which records every request.</summary>
    private sealed class DelegateExecutor(
        Func<SkillScriptExecutionRequest, CancellationToken, Task<SkillScriptExecutionResult>> body) : ISkillScriptExecutor
    {
        public ConcurrentQueue<SkillScriptExecutionRequest> Requests { get; } = new();

        public ValueTask<SkillScriptExecutionResult> ExecuteAsync(
            SkillScriptExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);
            return new ValueTask<SkillScriptExecutionResult>(body(request, cancellationToken));
        }
    }
}

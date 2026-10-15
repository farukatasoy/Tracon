using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
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
    /// remote executor cannot read the host's path.
    /// </summary>
    [Fact]
    public async Task A_file_script_within_the_content_limit_reaches_the_executor_whole_with_its_text_and_path()
    {
        var path = WriteFileScript("whole.sh", "# naïve café line\n" + string.Concat(Enumerable.Repeat("echo 0123456789abcdef\n", 2_900)));
        var content = await File.ReadAllTextAsync(path);
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0)));

        using var runner = CreateExecutorRunner(
            new InMemoryAuditLog(),
            await GrantFileSkillAsync("disk-skill"),
            executor,
            options => options.Interpreters["sh"] = "/bin/sh");

        await runner.RunFileScriptAsync("disk-skill", "whole", path, null, null, CancellationToken.None);

        var request = executor.Requests.ShouldHaveSingleItem();
        Encoding.UTF8.GetByteCount(request.Content).ShouldBeGreaterThan(60_000);
        request.Content.ShouldBe(content);
        request.ContentSha256.ShouldBe(Sha256(content));
        request.Origin.ShouldBe(SkillScriptOrigin.File);
        request.SourcePath.ShouldBe(path);
        request.Interpreter.ShouldBe("/bin/sh");
    }

    /// <summary>
    /// One content limit for every script: a file script larger than
    /// <c>MaxScriptContentLength</c> is a closed gate, written as a denial before the
    /// <c>script.run</c> entry and never read into memory.
    /// </summary>
    [Fact]
    public async Task A_file_script_past_the_content_limit_is_denied_before_the_audit_entry_and_the_executor()
    {
        var path = WriteFileScript("large.sh", new string('#', 2_048));
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0)));
        var log = new InMemoryAuditLog();

        using var runner = CreateExecutorRunner(
            log,
            await GrantFileSkillAsync("disk-skill"),
            executor,
            options =>
            {
                options.Interpreters["sh"] = "/bin/sh";
                options.MaxScriptContentLength = 1_024;
            });

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunFileScriptAsync("disk-skill", "large", path, null, null, CancellationToken.None));

        exception.Message.ShouldContain("exceeds the 1024-byte limit");
        executor.Requests.ShouldBeEmpty();
        (await DenialReasonsAsync(log)).ShouldContain(static reason => reason.Contains("exceeds the 1024-byte limit", StringComparison.Ordinal));
        (await log.QueryAsync(new AuditQuery { TenantId = "default" }))
            .ShouldNotContain(static entry => string.Equals(entry.Action, "script.run", StringComparison.Ordinal));
    }

    public static TheoryData<string> SpecialFiles() => ["missing", "empty", "dev-zero-link", "fifo"];

    /// <summary>
    /// A file the runner cannot read safely is a closed gate: it is decided from the
    /// file's metadata, without opening it. A FIFO blocks inside <c>open</c> until a
    /// writer appears and <c>/dev/zero</c> never ends; both report a size of zero.
    /// The quota slot is never taken, so the next script of the tenant still runs.
    /// </summary>
    [Theory]
    [MemberData(nameof(SpecialFiles))]
    public async Task A_missing_empty_or_special_file_script_is_denied_without_opening_it(string kind)
    {
        var directory = Directory.CreateTempSubdirectory("tracon-file-script-").FullName;
        _tempDirectories.Add(directory);
        var path = Path.Combine(directory, $"{kind}.sh");

        switch (kind)
        {
            case "empty":
                await File.WriteAllTextAsync(path, string.Empty);
                break;
            case "dev-zero-link":
                Assert.SkipWhen(!File.Exists("/dev/zero"), "/dev/zero not found.");
                File.CreateSymbolicLink(path, "/dev/zero");
                break;
            case "fifo":
                Assert.SkipWhen(!TryMakeFifo(path), "mkfifo is not available.");
                break;
        }

        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0, "after-the-denial")));
        var log = new InMemoryAuditLog();

        using var runner = CreateExecutorRunner(
            log,
            await GrantFileSkillAsync("disk-skill"),
            executor,
            options =>
            {
                options.Interpreters["sh"] = "/bin/sh";
                options.MaxConcurrentPerTenant = 1;
                options.MaxConcurrentTotal = 1;
            });

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner
                .RunFileScriptAsync("disk-skill", kind, path, null, null, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(30)));

        exception.Message.ShouldContain(string.Equals(kind, "missing", StringComparison.Ordinal)
            ? "does not exist"
            : "is empty or is not a regular file");
        executor.Requests.ShouldBeEmpty();
        (await log.QueryAsync(new AuditQuery { TenantId = "default" }))
            .ShouldNotContain(static entry => string.Equals(entry.Action, "script.run", StringComparison.Ordinal));

        var next = WriteFileScript("next.sh", "echo next");
        (await runner.RunFileScriptAsync("disk-skill", "next", next, null, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)))
            .ShouldBeOfType<string>().ShouldContain("after-the-denial");
    }

    /// <summary>
    /// The file passed the gate and became unreadable before the read: the call fails
    /// after its <c>script.run</c> entry, and the executor never sees a request.
    /// </summary>
    [Fact]
    public async Task A_file_script_that_cannot_be_read_fails_the_call_after_the_audit_entry()
    {
        var path = WriteFileScript("locked.sh", "echo locked");
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix file modes only.");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
        }

        Assert.SkipWhen(CanOpen(path), "The test user can read a file with no permissions (root).");

        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0)));
        var log = new InMemoryAuditLog();

        using var runner = CreateExecutorRunner(
            log,
            await GrantFileSkillAsync("disk-skill"),
            executor,
            options => options.Interpreters["sh"] = "/bin/sh");

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await runner.RunFileScriptAsync("disk-skill", "locked", path, null, null, CancellationToken.None));

        exception.Message.ShouldContain("could not be read");
        executor.Requests.ShouldBeEmpty();
        (await log.QueryAsync(new AuditQuery { TenantId = "default" }))
            .ShouldContain(static entry => string.Equals(entry.Action, "script.run", StringComparison.Ordinal));
    }

    /// <summary>
    /// The gate decides from metadata, so a file can still change between the gate
    /// and the read. The read is held to the same wait limit as the executor: a FIFO
    /// that appears after the gate blocks inside <c>open</c>, and the runner gives
    /// up at <c>Timeout + 5 s</c>, fails the call and frees the quota slot.
    /// </summary>
    [Fact]
    public async Task A_file_that_blocks_after_the_gate_is_abandoned_at_the_wait_limit_and_frees_the_quota()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-file-script-").FullName;
        _tempDirectories.Add(directory);
        var path = Path.Combine(directory, "swapped.sh");
        await File.WriteAllTextAsync(path, "echo regular");
        Assert.SkipWhen(!TryMakeFifo(Path.Combine(directory, "probe.fifo")), "mkfifo is not available.");

        // The script.run entry is written between the gate and the read; this log
        // swaps the regular file for a FIFO exactly there.
        var swapped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = new SwappingAuditLog(() =>
        {
            if (!swapped.Task.IsCompleted)
            {
                File.Delete(path);
                TryMakeFifo(path).ShouldBeTrue();
                swapped.TrySetResult();
            }
        });

        var clock = new TriggerableTimeProvider();
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0, "second-file")));

        using var runner = CreateExecutorRunner(
            log,
            await GrantFileSkillAsync("disk-skill"),
            executor,
            options =>
            {
                options.Interpreters["sh"] = "/bin/sh";
                options.Timeout = TimeSpan.FromSeconds(30);
                options.MaxConcurrentPerTenant = 1;
                options.MaxConcurrentTotal = 1;
            },
            timeProvider: clock);

        try
        {
            var first = runner.RunFileScriptAsync("disk-skill", "swapped", path, null, null, CancellationToken.None);
            await swapped.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await WaitForTimerAsync(clock);

            clock.DueTimes.ShouldBe([TimeSpan.FromSeconds(35)]);
            first.IsCompleted.ShouldBeFalse();

            clock.TriggerAll();

            var exception = await Should.ThrowAsync<TraconException>(async () => await first.WaitAsync(TimeSpan.FromSeconds(30)));
            exception.Message.ShouldContain("could not be read in time");
            executor.Requests.ShouldBeEmpty();

            var next = WriteFileScript("next.sh", "echo next");
            (await runner.RunFileScriptAsync("disk-skill", "next", next, null, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)))
                .ShouldBeOfType<string>().ShouldContain("second-file");
        }
        finally
        {
            ReleaseFifoReader(path);
        }
    }

    [Fact]
    public async Task The_bounded_read_stops_one_byte_past_the_limit_on_an_endless_file()
    {
        Assert.SkipWhen(!File.Exists("/dev/zero"), "/dev/zero not found.");

        (await SandboxedSkillScriptRunner.ReadBoundedAsync("/dev/zero", 4_096, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30)))
            .ShouldBeNull();
    }

    /// <summary>
    /// The tool call record is observability: a store that refuses it does not fail
    /// the script, and the run still gets its result.
    /// </summary>
    [Fact]
    public async Task A_tool_call_record_the_store_refuses_does_not_fail_the_run()
    {
        var skill = Demo(EchoScript());
        var executor = new DelegateExecutor((_, _) => Task.FromResult(Exited(0, "recorded-or-not")));
        var store = DispatchProxy.Create<IRunStore, ToolRecordRefusingRunStore>();
        var runId = TraconId.NewId();

        TraconRunContext.SetCurrent(new AgentRunScope { RunId = runId, RootRunId = runId, TenantId = "default" });

        try
        {
            using var runner = CreateExecutorRunner(new InMemoryAuditLog(), await GrantAllAsync(skill), executor, runStore: store);

            (await runner.RunStoredScriptAsync(skill, "echo", null, CancellationToken.None))
                .ShouldBeOfType<string>().ShouldContain("recorded-or-not");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        ((ToolRecordRefusingRunStore)(object)store).Attempts.ShouldBe(1);
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

    /// <summary>
    /// After the caller stops, an executor that fails later still has its failure
    /// observed: the runner dropped the task, and an unobserved fault would surface
    /// as <see cref="TaskScheduler.UnobservedTaskException"/> in the host.
    /// </summary>
    [Fact]
    public async Task A_late_executor_failure_after_a_caller_cancellation_is_observed()
    {
        var marker = $"late-failure-{Guid.NewGuid():N}";
        var unobserved = new ConcurrentQueue<string>();

        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs args)
        {
            if (args.Exception.InnerExceptions.Any(inner => inner.Message.Contains(marker, StringComparison.Ordinal)))
            {
                unobserved.Enqueue(marker);
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;

        try
        {
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var skill = Demo(EchoScript());

            await RunAndCancelAsync(skill, release, failed, called, marker);

            release.SetResult();
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(30));

            await WaitUntil.TrueAsync(
                () =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    return true;
                },
                "the dropped executor task was collected");

            unobserved.ShouldBeEmpty("The runner dropped a faulted executor task without observing it.");
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    /// <summary>
    /// Starts a run whose executor fails only after <paramref name="release"/>, and
    /// cancels it as the caller. A separate method so no local in the test keeps
    /// the executor's task alive.
    /// </summary>
    private static async Task RunAndCancelAsync(
        AgentSkillDefinition skill,
        TaskCompletionSource release,
        TaskCompletionSource failed,
        TaskCompletionSource called,
        string marker)
    {
        var executor = new DelegateExecutor(async (_, _) =>
        {
            called.TrySetResult();
            await release.Task;
            failed.TrySetResult();
            throw new InvalidOperationException(marker);
        });

        using var runner = CreateExecutorRunner(new InMemoryAuditLog(), await GrantAllAsync(skill), executor);
        using var cancellation = new CancellationTokenSource();

        var run = runner.RunStoredScriptAsync(skill, "echo", null, cancellation.Token);
        await called.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancellation.CancelAsync();

        (await Should.ThrowAsync<Exception>(async () => await run.WaitAsync(TimeSpan.FromSeconds(30))))
            .ShouldBeAssignableTo<OperationCanceledException>();
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

    private string WriteFileScript(string name, string content)
    {
        var directory = Directory.CreateTempSubdirectory("tracon-file-script-").FullName;
        _tempDirectories.Add(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static bool TryMakeFifo(string path)
    {
        // A Windows runner has Git for Windows' mkfifo on its PATH. It exits 0, but what
        // it makes is MSYS's emulation, not a file the .NET file APIs see as a FIFO:
        // measured on CI (2026-10-15), the runner reported "does not exist" and a read
        // never blocked. FIFOs are a Unix file type; the cases that need one skip here.
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var mkfifo = Process.Start(new ProcessStartInfo("mkfifo") { ArgumentList = { path }, UseShellExecute = false });

            if (mkfifo is null)
            {
                return false;
            }

            mkfifo.WaitForExit();
            return mkfifo.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool CanOpen(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Opens a FIFO for writing once, so a reader blocked in <c>open</c> returns.</summary>
    private static void ReleaseFifoReader(string path)
    {
        try
        {
            using var writer = new FileStream(path, FileMode.Open, FileAccess.Write);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing is blocked on it.
        }
    }

    private static Task WaitForTimerAsync(TriggerableTimeProvider clock)
        => WaitUntil.TrueAsync(() => clock.ActiveTimerCount > 0, "the runner armed its wait limit");

    /// <summary>An audit log that runs <paramref name="onScriptRun"/> while it writes the <c>script.run</c> entry.</summary>
    private sealed class SwappingAuditLog(Action onScriptRun) : IAuditLog
    {
        private readonly InMemoryAuditLog _inner = new();

        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            if (string.Equals(entry.Action, "script.run", StringComparison.Ordinal))
            {
                onScriptRun();
            }

            return _inner.WriteAsync(entry, cancellationToken);
        }

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => _inner.QueryAsync(query, cancellationToken);

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => _inner.VerifyChainAsync(query, cancellationToken);
    }

    /// <summary>A run store whose only reachable member, the tool call record, always fails.</summary>
    public class ToolRecordRefusingRunStore : DispatchProxy
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (string.Equals(targetMethod?.Name, nameof(IRunStore.RecordToolInvocationAsync), StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _attempts);
                return new ValueTask(Task.FromException(new InvalidOperationException("tool_invocations is unavailable")));
            }

            throw new NotSupportedException($"{targetMethod?.Name} is not used by the script runner.");
        }
    }

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

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Tracon.Testing.Contracts.Scripts;

/// <summary>
/// Behavior tests for an <see cref="ISkillScriptExecutor"/>: what a script
/// receives, what the executor reports back, and how it stops.
/// </summary>
/// <remarks>
/// <para>
/// Derive one class per executor and return it from
/// <see cref="CreateExecutorAsync"/>. Every scenario sends a
/// <see cref="SkillScriptOrigin.Stored"/> request whose content is a POSIX
/// <c>sh</c> script, so a remote executor needs no file on the test machine.
/// An executor for another runtime overrides <see cref="Extension"/>,
/// <see cref="Interpreter"/>, and the script bodies (the <c>virtual</c>
/// members that end in <c>Script</c>); each body's summary says what it must
/// print.
/// </para>
/// <para>
/// The scenarios do not check isolation. Whether a script can read the host's
/// files, network, or process memory depends on where the executor runs it,
/// and a contract run on the test machine cannot measure that.
/// </para>
/// </remarks>
public abstract class SkillScriptExecutorContract : IAsyncLifetime
{
    /// <summary>The text <see cref="FailingScript"/> writes to standard error.</summary>
    protected const string ExpectedStandardError = "contract-standard-error";

    /// <summary>The exit code <see cref="FailingScript"/> ends with.</summary>
    protected const int ExpectedExitCode = 3;

    /// <summary>The skill name every request carries.</summary>
    protected const string ContractSkillName = "contract-skill";

    /// <summary>The executor under test.</summary>
    protected ISkillScriptExecutor Executor { get; private set; } = null!;

    /// <summary>Creates the executor under test.</summary>
    /// <returns>The executor; the same instance serves every call of one test.</returns>
    protected abstract ValueTask<ISkillScriptExecutor> CreateExecutorAsync();

    /// <summary>
    /// A reason to skip every scenario, or <see langword="null"/> to run them.
    /// The built-in executor returns one when the machine has no <c>/bin/sh</c>.
    /// </summary>
    protected virtual string? SkipReason => null;

    /// <summary>The extension every request carries. Defaults to <c>sh</c>.</summary>
    protected virtual string Extension => "sh";

    /// <summary>
    /// The interpreter-allow-list value every request carries. Defaults to
    /// <c>/bin/sh</c>; a remote executor may expect a runtime name.
    /// </summary>
    protected virtual string Interpreter => "/bin/sh";

    /// <summary>Copies standard input to standard output unchanged.</summary>
    protected virtual string ReadStandardInputScript => "cat";

    /// <summary>
    /// Writes <see cref="ExpectedStandardError"/> to standard error and exits
    /// with <see cref="ExpectedExitCode"/>.
    /// </summary>
    protected virtual string FailingScript => $"echo {ExpectedStandardError} >&2\nexit {ExpectedExitCode}";

    /// <summary>
    /// Writes the line <c>0123456789</c> four hundred times to standard output
    /// (4,400 bytes with line feeds).
    /// </summary>
    protected virtual string FloodScript
        => "i=0\nwhile [ \"$i\" -lt 400 ]; do\n  echo 0123456789\n  i=$((i+1))\ndone";

    /// <summary>Runs for at least sixty seconds unless it is stopped.</summary>
    protected virtual string SleepScript => "sleep 60";

    /// <summary>The script with no content at all.</summary>
    protected virtual string EmptyScript => string.Empty;

    /// <summary>
    /// The longest time a stopped script may take to return in the timeout and
    /// cancellation scenarios. It is far below <see cref="SleepScript"/>'s sixty
    /// seconds, so an executor that waits for the script to finish fails.
    /// </summary>
    protected virtual TimeSpan StopTolerance => TimeSpan.FromSeconds(30);

    /// <summary>Prints <paramref name="text"/> and a line break.</summary>
    /// <param name="text">A word of ASCII letters and hyphens.</param>
    /// <returns>The script body.</returns>
    protected virtual string EchoScript(string text) => $"echo {text}";

    /// <summary>
    /// Prints <c>[&lt;TRACON_SKILL_NAME&gt;][&lt;present when TRACON_SKILL_TEMP is set&gt;][&lt;value of hostVariable&gt;]</c>.
    /// </summary>
    /// <param name="hostVariable">The name of a variable set in the test process.</param>
    /// <returns>The script body.</returns>
    protected virtual string EnvironmentScript(string hostVariable)
        => $"printf '[%s][%s][%s]' \"$TRACON_SKILL_NAME\" \"${{TRACON_SKILL_TEMP:+present}}\" \"${hostVariable}\"";

    /// <summary>
    /// Writes a file named <paramref name="marker"/> into <c>TRACON_SKILL_TEMP</c>,
    /// waits a second so that a concurrent call can do the same, and then lists
    /// the directory's file names, one per line.
    /// </summary>
    /// <param name="marker">A word of ASCII letters.</param>
    /// <returns>The script body.</returns>
    protected virtual string TempMarkerScript(string marker)
        => $"echo {marker} > \"$TRACON_SKILL_TEMP/{marker}\"\nsleep 1\nls \"$TRACON_SKILL_TEMP\"";

    /// <summary>Builds a request for one scenario.</summary>
    /// <param name="scriptName">The script name, a plain file name.</param>
    /// <param name="content">The script body.</param>
    /// <param name="argumentsJson">The JSON for standard input, or <see langword="null"/>.</param>
    /// <param name="timeout">The script timeout; thirty seconds when <see langword="null"/>.</param>
    /// <param name="maxOutputBytes">The byte limit for each output stream.</param>
    /// <returns>A <see cref="SkillScriptOrigin.Stored"/> request with its content hash computed.</returns>
    protected virtual SkillScriptExecutionRequest CreateRequest(
        string scriptName,
        string content,
        string? argumentsJson = null,
        TimeSpan? timeout = null,
        int maxOutputBytes = 64 * 1024)
        => new()
        {
            TenantId = "contract-tenant",
            RunId = Guid.NewGuid(),
            SkillName = ContractSkillName,
            ScriptName = scriptName,
            Origin = SkillScriptOrigin.Stored,
            Extension = Extension,
            Interpreter = Interpreter,
            Content = content,
            ContentSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
            ArgumentsJson = argumentsJson,
            Timeout = timeout ?? TimeSpan.FromSeconds(30),
            MaxOutputBytes = maxOutputBytes,
        };

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Executor = await CreateExecutorAsync().ConfigureAwait(false);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return default;
    }

    [Fact]
    public async Task Arguments_arrive_on_standard_input_as_json()
    {
        Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);
        const string arguments = """{"city":"Montréal","count":3}""";

        var result = await Executor
            .ExecuteAsync(CreateRequest("read-input", ReadStandardInputScript, arguments), CancellationToken.None)
            .ConfigureAwait(false);

        result.ExitCode.ShouldBe(0);
        result.TimedOut.ShouldBeFalse();
        result.StandardOutput.Trim().ShouldBe(arguments);
    }

    [Fact]
    public async Task A_call_without_arguments_reads_an_empty_standard_input()
    {
        Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);

        var result = await Executor
            .ExecuteAsync(CreateRequest("read-nothing", ReadStandardInputScript), CancellationToken.None)
            .ConfigureAwait(false);

        result.ExitCode.ShouldBe(0);
        result.StandardOutput.Trim().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_script_runs_and_exits_cleanly()
    {
        Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);

        var result = await Executor
            .ExecuteAsync(CreateRequest("empty", EmptyScript), CancellationToken.None)
            .ConfigureAwait(false);

        result.ExitCode.ShouldBe(0);
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task The_exit_code_and_standard_error_are_reported()
    {
        Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);

        var result = await Executor
            .ExecuteAsync(CreateRequest("failing", FailingScript), CancellationToken.None)
            .ConfigureAwait(false);

        result.ExitCode.ShouldBe(ExpectedExitCode);
        result.TimedOut.ShouldBeFalse();
        result.Succeeded.ShouldBeFalse();
        result.StandardError.ShouldContain(ExpectedStandardError);
    }

    [Fact]
    public async Task Output_past_the_limit_is_cut_and_flagged_without_a_notice()
    {
        Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);
        const int limit = 512;

        var result = await Executor
            .ExecuteAsync(CreateRequest("flood", FloodScript, maxOutputBytes: limit), CancellationToken.None)
            .ConfigureAwait(false);

        result.Truncated.ShouldBeTrue();
        result.StandardOutput.ShouldNotBeEmpty();
        Encoding.UTF8.GetByteCount(result.StandardOutput).ShouldBeLessThanOrEqualTo(limit);

        // What is kept is the start of what the script printed, and nothing else:
        // the caller adds the one truncation notice, and a second one from the
        // executor would show twice.
        var printed = string.Concat(Enumerable.Repeat("0123456789\n", 400));
        printed.StartsWith(result.StandardOutput.ReplaceLineEndings("\n"), StringComparison.Ordinal).ShouldBeTrue(
            "The kept output must be a prefix of what the script printed, with no notice appended.");
    }

    [Fact]
    public async Task A_script_past_its_timeout_is_stopped_and_reported_as_timed_out()
    {
        Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);
        var stopwatch = Stopwatch.StartNew();

        var result = await Executor
            .ExecuteAsync(CreateRequest("sleeper", SleepScript, timeout: TimeSpan.FromSeconds(1)), CancellationToken.None)
            .ConfigureAwait(false);

        stopwatch.Elapsed.ShouldBeLessThan(StopTolerance);
        result.TimedOut.ShouldBeTrue();
        result.ExitCode.ShouldBeNull();
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task A_cancelled_call_throws_OperationCanceledException_and_is_not_a_timeout()
    {
        Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);
        using var cancellation = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();

        var call = Executor
            .ExecuteAsync(CreateRequest("cancelled", SleepScript, timeout: TimeSpan.FromSeconds(60)), cancellation.Token)
            .AsTask();

        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);

        Exception? thrown = null;

        try
        {
            await call.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        stopwatch.Elapsed.ShouldBeLessThan(StopTolerance);
        thrown.ShouldBeAssignableTo<OperationCanceledException>(
            "A caller's cancellation must surface as OperationCanceledException, not as a TimedOut result.");
    }

    [Fact]
    public async Task The_script_sees_its_two_variables_and_not_the_host_environment()
    {
        Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);
        var variable = $"TRACON_CONTRACT_HOST_{Guid.NewGuid():N}";
        var value = $"host-only-{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, value);

        try
        {
            var result = await Executor
                .ExecuteAsync(CreateRequest("environment", EnvironmentScript(variable)), CancellationToken.None)
                .ConfigureAwait(false);

            result.ExitCode.ShouldBe(0);
            result.StandardOutput.ShouldBe($"[{ContractSkillName}][present][]");
            result.StandardOutput.ShouldNotContain(value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task Concurrent_calls_do_not_share_a_temp_directory()
    {
        Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);

        var first = Executor
            .ExecuteAsync(CreateRequest("marker-first", TempMarkerScript("first")), CancellationToken.None)
            .AsTask();
        var second = Executor
            .ExecuteAsync(CreateRequest("marker-second", TempMarkerScript("second")), CancellationToken.None)
            .AsTask();

        var results = await Task.WhenAll(first, second).ConfigureAwait(false);

        results[0].ExitCode.ShouldBe(0);
        results[1].ExitCode.ShouldBe(0);
        results[0].StandardOutput.Trim().ShouldBe("first");
        results[1].StandardOutput.Trim().ShouldBe("second");
    }

    [Fact]
    public async Task A_stored_request_runs_its_content_and_never_a_path()
    {
        Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);
        var decoy = Directory.CreateTempSubdirectory("tracon-contract-");

        try
        {
            var path = Path.Combine(decoy.FullName, $"decoy.{Extension}");
            await File.WriteAllTextAsync(path, EchoScript("from-path")).ConfigureAwait(false);

            var request = CreateRequest("stored", EchoScript("from-content")) with { SourcePath = path };

            var result = await Executor.ExecuteAsync(request, CancellationToken.None).ConfigureAwait(false);

            result.ExitCode.ShouldBe(0);
            result.StandardOutput.ShouldContain("from-content");
            result.StandardOutput.ShouldNotContain("from-path");
        }
        finally
        {
            decoy.Delete(recursive: true);
        }
    }
}

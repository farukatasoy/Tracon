using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The built-in <see cref="ISkillScriptExecutor"/>: runs a skill script in a
/// separate operating-system process on the Tracon host, <strong>outside</strong>
/// Tracon's own process.
/// </summary>
/// <remarks>
/// <para>
/// Tracon never loads script code into its own process. The interpreter is the
/// allow-list value the request carries, environment variables pass through
/// <see cref="TraconSkillScriptOptions.EnvironmentAllowList"/>, duration and output
/// are limited, and the temporary directory given for writing is deleted at the
/// end of the run.
/// </para>
/// <para>
/// <strong>This is not isolation.</strong> The process runs under the host's own
/// operating-system identity and can read what that identity reads.
/// </para>
/// <para>
/// A file script runs in place, from its own directory. A stored script is
/// written to a temporary directory that only its owner can read, run from
/// there, and deleted afterward.
/// </para>
/// <para>
/// Arguments pass through <c>stdin</c>, <em>not</em> the command line: the
/// command line appears in the operating system's process list, and escaping
/// rules differ across platforms.
/// </para>
/// </remarks>
internal sealed class SkillScriptProcessRunner : ISkillScriptExecutor
{
    /// <summary>The environment variable that tells the script process its temporary write directory.</summary>
    internal const string TempDirectoryVariable = "TRACON_SKILL_TEMP";

    /// <summary>The environment variable that tells the script process the skill's name.</summary>
    internal const string SkillNameVariable = "TRACON_SKILL_NAME";

    private readonly IOptions<TraconOptions> _options;

    /// <summary>Creates the built-in executor.</summary>
    /// <param name="options">The Tracon settings; the environment allow-list is read from them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    internal SkillScriptProcessRunner(IOptions<TraconOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
    }

    /// <inheritdoc />
    public async ValueTask<SkillScriptExecutionResult> ExecuteAsync(
        SkillScriptExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The timeout is armed after the interpreter has started; a value the
        // timer refuses would throw THERE and skip the kill. The runner passes a
        // validated option, but this is the boundary that arms it.
        if (!TimerPeriod.IsValid(request.Timeout))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Timeout,
                TimerPeriod.Failure($"{nameof(SkillScriptExecutionRequest)}.{nameof(SkillScriptExecutionRequest.Timeout)}", request.Timeout));
        }

        if (request.MaxOutputBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaxOutputBytes,
                $"{nameof(SkillScriptExecutionRequest)}.{nameof(SkillScriptExecutionRequest.MaxOutputBytes)} must be at least 1.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // A stored script ALWAYS runs from its content, never from a path: the
        // content is what the grant pinned.
        var runInPlace = request.Origin == SkillScriptOrigin.File && request.SourcePath is { Length: > 0 };
        var scratch = runInPlace ? null : Directory.CreateTempSubdirectory("tracon-script-");

        try
        {
            string scriptPath;
            string workingDirectory;

            if (scratch is null)
            {
                scriptPath = request.SourcePath!;
                workingDirectory = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory;
            }
            else
            {
                // 🚨 The file name is validated HERE, at the boundary that writes
                // it. Path.Combine returns the second part unchanged when it is
                // rooted, and it does not resolve "..", so a stored script named
                // "/etc/cron.d/tracon" or "../../x" was written OUTSIDE the
                // scratch directory, executed from there, and left behind - the
                // scratch cleanup only removes the scratch directory. The runner
                // checks the name before the call too, but an executor that
                // trusts its caller is one refactor away from the same hole. The
                // extension is part of the written name, so the whole name is
                // checked as well.
                var fileName = SkillScriptNaming.RequireSafeFileName(
                    $"{SkillScriptNaming.RequireSafeFileName(request.ScriptName)}.{request.Extension}");

                scriptPath = Path.Combine(scratch.FullName, fileName);
                await File.WriteAllTextAsync(scriptPath, request.Content, cancellationToken)
                    .ConfigureAwait(false);
                workingDirectory = scratch.FullName;
            }

            var tempDirectory = Directory.CreateTempSubdirectory("tracon-skill-").FullName;

            try
            {
                return await RunAsync(
                        request,
                        scriptPath,
                        workingDirectory,
                        tempDirectory,
                        _options.Value.Skills.Scripts.EnvironmentAllowList,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }
        finally
        {
            if (scratch is not null)
            {
                TryDeleteDirectory(scratch.FullName);
            }
        }
    }

    private static async Task<SkillScriptExecutionResult> RunAsync(
        SkillScriptExecutionRequest request,
        string scriptPath,
        string workingDirectory,
        string tempDirectory,
        IList<string> environmentAllowList,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(request.Interpreter)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(scriptPath);

        // The environment is FULLY cleared first. ProcessStartInfo.Environment
        // comes filled with the calling process's variables; a connection
        // string or API key would leak from exactly here.
        startInfo.Environment.Clear();

        foreach (var name in environmentAllowList)
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                startInfo.Environment[name] = value;
            }
        }

        startInfo.Environment[TempDirectoryVariable] = tempDirectory;
        startInfo.Environment[SkillNameVariable] = request.SkillName;

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        var stdout = ReadCappedAsync(process.StandardOutput, request.MaxOutputBytes, linkedSource.Token);
        var stderr = ReadCappedAsync(process.StandardError, request.MaxOutputBytes, linkedSource.Token);

        await WriteArgumentsAsync(process, request.ArgumentsJson, linkedSource.Token).ConfigureAwait(false);

        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutSource.IsCancellationRequested;

            // The process TREE is killed: the interpreter may have started
            // its own child processes, and killing only the parent leaves zombies.
            TryKill(process);

            if (!timedOut)
            {
                throw;
            }
        }

        var output = await SafeAwaitAsync(stdout).ConfigureAwait(false);
        var error = await SafeAwaitAsync(stderr).ConfigureAwait(false);

        return new SkillScriptExecutionResult
        {
            ExitCode = timedOut ? null : process.ExitCode,
            StandardOutput = output.Text,
            StandardError = error.Text,
            Truncated = output.Truncated || error.Truncated,
            TimedOut = timedOut,
        };
    }

    private static async Task WriteArgumentsAsync(
        Process process,
        string? argumentsJson,
        CancellationToken cancellationToken)
    {
        try
        {
            if (argumentsJson is { Length: > 0 })
            {
                await process.StandardInput.WriteAsync(argumentsJson.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
            }

            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The script may have exited without reading stdin; this is not an error.
        }
        finally
        {
            // Close() also tries to flush the StreamWriter's internal buffer.
            // If the script exited without ever reading stdin (e.g. a bare
            // 'echo'), the pipe may ALREADY be closed at this point; Close()'s
            // own flush would then throw the same IOException, and since it
            // was outside the try/catch, it would go uncaught.
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The script may have exited without reading stdin; this is not an error.
            }
        }
    }

    /// <summary>Reads a stream to its end and keeps at most <paramref name="maxBytes"/> UTF-8 bytes of it.</summary>
    /// <remarks>
    /// The text carries no truncation notice: the runner adds one notice to the
    /// formatted result, the same for every executor.
    /// </remarks>
    private static async Task<(string Text, bool Truncated)> ReadCappedAsync(
        StreamReader reader,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new char[4096];
        var bytes = 0;
        var truncated = false;

        while (true)
        {
            int read;

            try
            {
                read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            if (truncated)
            {
                // The stream is drained to the end so the process does not block.
                continue;
            }

            var span = buffer.AsSpan(0, read);
            var chunkBytes = Encoding.UTF8.GetByteCount(span);

            if (bytes + chunkBytes <= maxBytes)
            {
                builder.Append(span);
                bytes += chunkBytes;
                continue;
            }

            builder.Append(span[..SkillScriptOutput.FittingLength(span, maxBytes - bytes)]);
            truncated = true;
        }

        return (builder.ToString(), truncated);
    }

    private static async Task<(string Text, bool Truncated)> SafeAwaitAsync(Task<(string Text, bool Truncated)> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return (string.Empty, false);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            // The process may have already exited.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If the temporary directory cannot be deleted, the run has still completed.
        }
    }
}

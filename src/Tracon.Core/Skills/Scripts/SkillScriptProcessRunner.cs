using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Tracon;

/// <summary>
/// Runs a skill script in a separate operating-system process, <strong>outside</strong>
/// Tracon's own process.
/// </summary>
/// <remarks>
/// <para>
/// Tracon never loads script code into its own process. The interpreter
/// is chosen from an allow-list, environment variables are passed through an
/// allow-list, duration and output are limited, and the temporary directory
/// given for writing is deleted at the end of the run.
/// </para>
/// <para>
/// Arguments pass through <c>stdin</c>, <em>not</em> the command line: the
/// command line appears in the operating system's process list, and escaping
/// rules differ across platforms.
/// </para>
/// </remarks>
internal static class SkillScriptProcessRunner
{
    /// <summary>The environment variable that tells the script process its temporary write directory.</summary>
    internal const string TempDirectoryVariable = "TRACON_SKILL_TEMP";

    /// <summary>The environment variable that tells the script process the skill's name.</summary>
    internal const string SkillNameVariable = "TRACON_SKILL_NAME";

    public static async Task<SkillScriptExecutionResult> ExecuteAsync(
        string interpreter,
        string scriptPath,
        string workingDirectory,
        string? argumentsJson,
        TraconSkillScriptOptions options,
        string skillName,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tempDirectory = Directory.CreateTempSubdirectory("tracon-skill-").FullName;
        var startedAt = timeProvider.GetTimestamp();

        try
        {
            return await RunAsync(
                    interpreter,
                    scriptPath,
                    workingDirectory,
                    argumentsJson,
                    options,
                    skillName,
                    tempDirectory,
                    startedAt,
                    timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private static async Task<SkillScriptExecutionResult> RunAsync(
        string interpreter,
        string scriptPath,
        string workingDirectory,
        string? argumentsJson,
        TraconSkillScriptOptions options,
        string skillName,
        string tempDirectory,
        long startedAt,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(interpreter)
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

        foreach (var name in options.EnvironmentAllowList)
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                startInfo.Environment[name] = value;
            }
        }

        startInfo.Environment[TempDirectoryVariable] = tempDirectory;
        startInfo.Environment[SkillNameVariable] = skillName;

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        using var timeoutSource = new CancellationTokenSource(options.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        var stdout = ReadCappedAsync(process.StandardOutput, options.MaxOutputBytes, linkedSource.Token);
        var stderr = ReadCappedAsync(process.StandardError, options.MaxOutputBytes, linkedSource.Token);

        await WriteArgumentsAsync(process, argumentsJson, linkedSource.Token).ConfigureAwait(false);

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
            Duration = timeProvider.GetElapsedTime(startedAt),
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
            // was outside the try/catch, it would go uncaught
            // (HATA-K-skill-pipe, 2026-08-15).
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

            var remaining = maxBytes - bytes;
            var taken = 0;

            while (taken < read && Encoding.UTF8.GetByteCount(span.Slice(taken, 1)) <= remaining)
            {
                remaining -= Encoding.UTF8.GetByteCount(span.Slice(taken, 1));
                taken++;
            }

            builder.Append(span[..taken]);
            builder.Append(
                CultureInfo.InvariantCulture,
                $"\n[Tracon: output truncated at the {maxBytes}-byte limit.]");
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

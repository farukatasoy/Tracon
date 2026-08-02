using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AgentPrism;

/// <summary>
/// Bir skill script'ini AgentPrism surecinin <strong>disinda</strong>, ayri bir
/// isletim sistemi surecinde calistirir.
/// </summary>
/// <remarks>
/// <para>
/// AgentPrism script kodunu hicbir zaman kendi surecine yuklemez. Yorumlayici
/// beyaz listeden secilir, ortam degiskenleri beyaz listeyle aktarilir, sure ve
/// cikti sinirlanir, yazma icin verilen gecici dizin calistirma sonunda silinir.
/// </para>
/// <para>
/// Argumanlar komut satirindan <em>degil</em>, <c>stdin</c> uzerinden gecer:
/// komut satiri isletim sisteminin surec listesinde gorunur ve kacis kurallari
/// platformlar arasinda farklidir.
/// </para>
/// </remarks>
internal static class SkillScriptProcessRunner
{
    /// <summary>Script surecine gecici yazma dizinini bildiren ortam degiskeni.</summary>
    internal const string TempDirectoryVariable = "AGENTPRISM_SKILL_TEMP";

    /// <summary>Script surecine skill adini bildiren ortam degiskeni.</summary>
    internal const string SkillNameVariable = "AGENTPRISM_SKILL_NAME";

    public static async Task<SkillScriptExecutionResult> ExecuteAsync(
        string interpreter,
        string scriptPath,
        string workingDirectory,
        string? argumentsJson,
        AgentPrismSkillScriptOptions options,
        string skillName,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var tempDirectory = Directory.CreateTempSubdirectory("agentprism-skill-").FullName;
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
        AgentPrismSkillScriptOptions options,
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

        // Ortam once TAMAMEN bosaltilir. ProcessStartInfo.Environment cagiran
        // surecin degiskenleriyle dolu gelir; baglanti dizesi ve API anahtari
        // tam olarak buradan sizardi.
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

            // Surec AGACI oldurulur: yorumlayici kendi alt sureclerini
            // baslatmis olabilir ve yalnizca ebeveyni oldurmek zombi birakir.
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
            // Script stdin okumadan cikmis olabilir; bu bir hata degildir.
        }
        finally
        {
            process.StandardInput.Close();
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
                // Akis, surec bloke olmasin diye sonuna kadar bosaltilir.
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
                $"\n[AgentPrism: cikti {maxBytes} bayt sinirinda kirpildi.]");
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
            // Surec zaten bitmis olabilir.
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
            // Gecici dizin silinemezse calistirma yine de tamamlanmistir.
        }
    }
}

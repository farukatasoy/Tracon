using System.Diagnostics;
using System.Text;

namespace AgentPrism.Templates.Tests.Infrastructure;

/// <summary>Bir CLI komutunu alt surec olarak calistirir ve ciktisini toplar.</summary>
internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? RepoPaths.Root,
        };

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdOut.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdErr.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw new TimeoutException(
                $"'{fileName} {arguments}' {timeout} icinde bitmedi.{Environment.NewLine}stdout:{stdOut}{Environment.NewLine}stderr:{stdErr}");
        }

        return new ProcessResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Surec zaten cikmis olabilir - yaris durumu, yok sayilir.
        }
    }
}

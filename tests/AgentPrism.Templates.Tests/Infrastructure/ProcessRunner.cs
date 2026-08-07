using System.Diagnostics;
using System.Text;

namespace AgentPrism.Templates.Tests.Infrastructure;

/// <summary>Bir CLI komutunu alt surec olarak calistirir ve ciktisini toplar.</summary>
/// <remarks>
/// 🚨 <strong>MSBuild dugum yeniden kullanimi burada KAPATILIR ve bu bir
/// performans ayari degil, bir KILITLENME duzeltmesidir.</strong>
/// <para>
/// Olculdu (2026-08-07): <c>dotnet pack</c>/<c>build</c> calisan MSBuild isci
/// dugumlerini <c>nodeReuse:true</c> ile baslatir ve o dugumler komut bittikten
/// sonra da <strong>yasamaya devam eder</strong> (varsayilan olarak ~15 dakika).
/// Dugumler ebeveynin YONLENDIRILMIS stdout/stderr tanitici(handle)larini miras
/// alir; boru hatti bu yuzden hicbir zaman EOF gormez. .NET'in
/// <see cref="Process.WaitForExitAsync"/> cagrisi ise cikis kodunu degil,
/// asenkron okuyucularin BITMESINI de bekler — yani alt surec saniyeler icinde
/// cikmis olsa bile bu metot dugumler olene kadar bloke kalir.
/// </para>
/// <para>
/// Belirti: <c>dotnet test</c> hicbir test calistirmadan on dakikalarca asili
/// kalir, <c>ps</c> ciktisinda tek bir <c>dotnet pack</c> sureci bile gorunmez
/// ve yalnizca oksuz (<c>ppid = 1</c>) <c>MSBuild.dll … /nodeReuse:true</c>
/// dugumleri durur. Dugumler oldurulunce fikstur ANINDA devam eder.
/// </para>
/// </remarks>
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

        // 🚨 Tip sinifinin notuna bakin: bu satir olmadan RunAsync, alt surec
        // cikmis olsa bile kalan MSBuild dugumleri boruyu kapatana kadar (~15 dk)
        // bloke kalir. Komut satiri anahtari (`-nodeReuse:false`) yeterli degildir:
        // `dotnet new` gibi MSBuild'i DOLAYLI cagiran komutlar onu tasiyamaz.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

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

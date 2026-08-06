using System.Diagnostics;
using AgentPrism.Templates.Tests.Infrastructure;

namespace AgentPrism.Templates.Tests;

/// <summary>
/// Varsayilan (bellek ici) birlesim hicbir kurulum olmadan ayaga kalkmali ve
/// katalog ucu 200 donmelidir (37. faz Bitis Olcutleri).
/// </summary>
public sealed class TemplateRunTests(TemplateFixture fixture)
{
    private const string ProjectName = "Calisma.Deneme";
    private const int Port = 5185;

    [Fact]
    public async Task VarsayilanBirlesim_KurulumOlmadanAyagaKalkarVeKatalogDoner()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(ProjectName, dir.Path, "--persistence memory --ui false");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));
        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);

        var dllPath = Directory.EnumerateFiles(dir.Path, $"{ProjectName}.dll", SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Contains(Path.Combine("bin", "Release"), StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"'{ProjectName}.dll' derleme ciktisinda bulunamadi.{Environment.NewLine}{buildResult.Combined}");

        var startInfo = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            WorkingDirectory = dir.Path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{Port}";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Process.Start 'dotnet' icin null dondu.");

        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Port}/") };

            HttpResponseMessage? response = null;
            var deadline = DateTime.UtcNow.AddSeconds(30);

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    response = await httpClient.GetAsync("agentprism/api/agents");
                    break;
                }
                catch (HttpRequestException)
                {
                    if (process.HasExited)
                    {
                        var stdOut = await process.StandardOutput.ReadToEndAsync();
                        var stdErr = await process.StandardError.ReadToEndAsync();
                        throw new InvalidOperationException(
                            $"Uygulama beklenmedik sekilde cikti (kod {process.ExitCode}).{Environment.NewLine}{stdOut}{Environment.NewLine}{stdErr}");
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }
            }

            if (response is null)
            {
                throw new InvalidOperationException("Uygulama 30 saniye icinde istek karsilamadi.");
            }

            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
    }
}

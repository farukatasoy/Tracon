
namespace AgentPrism.Core.UnitTests.Skills;

/// <summary>
/// Sureci gercekten baslatan katmanin sinirlarini dogrular.
/// </summary>
/// <remarks>
/// Testler <c>bash</c> kullanir; bulunmadigi platformlarda atlanir. Script
/// dosyalari gecici bir klasorde uretilir, depoya calistirilabilir dosya
/// eklenmez.
/// </remarks>
public sealed class SkillScriptProcessRunnerTests : IDisposable
{
    private static readonly string? BashPath = FindBash();

    private readonly string _root = Directory.CreateTempSubdirectory("agentprism-tests-").FullName;

    [Fact]
    public async Task Ortam_degiskenleri_surece_sizmaz()
    {
        Assert.SkipWhen(BashPath is null, "bash bulunamadi.");

        // Beyaz listede olmayan bir degisken, calistirma anindaki surecte
        // tanimli olsa bile script'e gecmemelidir.
        Environment.SetEnvironmentVariable("AGENTPRISM_TEST_CONNECTIONSTRING", "gizli-deger");
        try
        {
            var script = WriteScript("env.sh", "echo \"[$AGENTPRISM_TEST_CONNECTIONSTRING]\"");

            var result = await RunAsync(script);

            result.StandardOutput.ShouldNotContain("gizli-deger");
            result.StandardOutput.ShouldContain("[]");
        }
        finally
        {
            Environment.SetEnvironmentVariable("AGENTPRISM_TEST_CONNECTIONSTRING", null);
        }
    }

    [Fact]
    public async Task Zaman_asimi_sureci_oldurur()
    {
        Assert.SkipWhen(BashPath is null, "bash bulunamadi.");
        var script = WriteScript("sleep.sh", "sleep 30");

        var result = await RunAsync(script, options => options.Timeout = TimeSpan.FromMilliseconds(300));

        result.TimedOut.ShouldBeTrue();
        result.Succeeded.ShouldBeFalse();
    }

    [Fact]
    public async Task Cikti_siniri_asilinca_kirpilir()
    {
        Assert.SkipWhen(BashPath is null, "bash bulunamadi.");
        var script = WriteScript("flood.sh", "for i in $(seq 1 5000); do echo 0123456789; done");

        var result = await RunAsync(script, options => options.MaxOutputBytes = 512);

        result.Truncated.ShouldBeTrue();
        result.StandardOutput.Length.ShouldBeLessThanOrEqualTo(1024);
    }

    [Fact]
    public async Task Gecici_klasor_degiskeni_yazilabilir_ve_calisma_sonrasi_silinir()
    {
        Assert.SkipWhen(BashPath is null, "bash bulunamadi.");
        var script = WriteScript(
            "temp.sh",
            $"echo veri > \"${SkillScriptProcessRunner.TempDirectoryVariable}/x.txt\" && echo \"${SkillScriptProcessRunner.TempDirectoryVariable}\"");

        var result = await RunAsync(script);

        result.Succeeded.ShouldBeTrue();
        Directory.Exists(result.StandardOutput.Trim()).ShouldBeFalse();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temizlik basarisizligi test sonucunu degistirmemelidir.
        }
    }

    private static string? FindBash()
    {
        foreach (var candidate in new[] { "/bin/bash", "/usr/bin/bash" })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string WriteScript(string name, string body)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, body + Environment.NewLine);
        return path;
    }

    private Task<SkillScriptExecutionResult> RunAsync(
        string scriptPath,
        Action<AgentPrismSkillScriptOptions>? configure = null)
    {
        var options = new AgentPrismOptions().Skills.Scripts;
        options.Enabled = true;
        options.PlatformIsolationAcknowledged = true;
        options.Interpreters["sh"] = BashPath!;
        configure?.Invoke(options);

        return SkillScriptProcessRunner.ExecuteAsync(
            BashPath!,
            scriptPath,
            _root,
            argumentsJson: null,
            options,
            skillName: "test",
            TimeProvider.System,
            CancellationToken.None);
    }
}

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Skills;

/// <summary>
/// Sandbox'in guvenlik kapilarini dogrular.
/// </summary>
/// <remarks>
/// Bu testler islevsellikten cok <strong>reddetme</strong> davranisini olcer:
/// bir kapinin sessizce acilmasi, sunucuda yetkisiz kod calistirilmasi demektir.
/// </remarks>
public sealed class SandboxedSkillScriptRunnerTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public async Task Ozellik_kapaliyken_script_calismaz()
    {
        var log = new InMemoryAuditLog();
        using var runner = CreateRunner(log, configure: options => options.Enabled = false);

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));

        exception.Message.ShouldContain("kapali");
    }

    [Fact]
    public async Task Izin_yokken_script_reddedilir_ve_denetim_izine_yazilir()
    {
        var log = new InMemoryAuditLog();
        using var runner = CreateRunner(log);

        await Should.ThrowAsync<AgentPrismException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));

        var entries = await log.QueryAsync(new AuditQuery { TenantId = "default" });
        var denied = entries.Single(entry => string.Equals(entry.Action, "script.denied", StringComparison.Ordinal));

        // HATA-K-skill-audit-json (2026-08-15): 'After' gercek bir jsonb
        // sutununa yazilir; DenyAsync ham (JSON olmayan) metni gecirdiginde
        // Postgres INSERT'i "22P02 invalid input syntax for type json" ile
        // reddediyordu ve denetim izi HICBIR ZAMAN olusmuyordu. Bellek ici
        // sahte defter bunu yakalayamaz (JSON gecerliligini denetlemez) - bu
        // yuzden burada acikca parse ediliyor.
        Should.NotThrow(() => JsonDocument.Parse(denied.After!));
    }

    [Fact]
    public async Task Suresi_dolmus_izin_gecersizdir()
    {
        var log = new InMemoryAuditLog();
        var grants = new InMemorySkillScriptGrantStore();
        await grants.GrantAsync(new SkillScriptGrant
        {
            TenantId = "default",
            SkillName = "demo",
            GrantedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        using var runner = CreateRunner(log, grants);

        await Should.ThrowAsync<AgentPrismException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Bos_yorumlayici_listesiyle_hicbir_script_calismaz()
    {
        var log = new InMemoryAuditLog();
        using var runner = CreateRunner(log, await GrantAllAsync(), options => options.Interpreters.Clear());

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));

        exception.Message.ShouldContain("yorumlayici");
    }

    [Fact]
    public async Task Denetim_izi_yazilamazsa_script_calismaz()
    {
        // Faz 9'un "gozlemlenebilirlik islevi bozmaz" kuralinin bilincli
        // istisnasi: kaydi tutulamayan bir calistirma hic yapilmamalidir.
        using var runner = CreateRunner(new ThrowingAuditLog(), await GrantAllAsync());

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));

        exception.Message.ShouldContain("denetim");
    }

    [Fact]
    public async Task Kayitli_script_kapaliyken_reddedilir()
    {
        using var runner = CreateRunner(
            new InMemoryAuditLog(),
            await GrantAllAsync(),
            options => options.AllowStoredScripts = false);

        await Should.ThrowAsync<AgentPrismException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), null, CancellationToken.None));
    }

    [Fact]
    public async Task Cok_buyuk_arguman_reddedilir()
    {
        using var runner = CreateRunner(new InMemoryAuditLog(), await GrantAllAsync(), options => options.MaxArgumentBytes = 8);
        var arguments = JsonDocument.Parse("""{"value":"cok uzun bir arguman metni"}""").RootElement;

        await Should.ThrowAsync<AgentPrismException>(
            async () => await runner.RunStoredScriptAsync("demo", EchoScript(), arguments, CancellationToken.None));
    }

    [Fact]
    public async Task Izinli_script_gercekten_calisir_ve_denetim_izine_yazilir()
    {
        // Faz 11'in kanit testi: tum kapilar acikken script gercek bir isletim
        // sistemi surecinde calisir ve ciktisi modele doner.
        Assert.SkipWhen(!File.Exists("/bin/bash"), "bash bulunamadi.");

        var log = new InMemoryAuditLog();
        using var runner = CreateRunner(log, await GrantAllAsync(), options =>
        {
            options.Interpreters.Clear();
            options.Interpreters["sh"] = "/bin/bash";
        });

        var script = new AgentSkillScriptDefinition
        {
            Name = "echo",
            Extension = "sh",
            Content = "echo merhaba-agentprism",
        };

        var output = await runner.RunStoredScriptAsync("demo", script, null, CancellationToken.None);

        output?.ToString().ShouldNotBeNull().ShouldContain("merhaba-agentprism");

        var entries = await log.QueryAsync(new AuditQuery { TenantId = "default" });
        entries.ShouldContain(entry => entry.Action == "script.run");
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Gecici klasor temizligi testin sonucunu degistirmemelidir.
            }
        }
    }

    private static AgentSkillScriptDefinition EchoScript() => new()
    {
        Name = "echo",
        Extension = "py",
        Content = "print('merhaba')",
    };

    private static async Task<InMemorySkillScriptGrantStore> GrantAllAsync()
    {
        var grants = new InMemorySkillScriptGrantStore();
        await grants.GrantAsync(new SkillScriptGrant
        {
            TenantId = "default",
            SkillName = "demo",
            GrantedAt = DateTimeOffset.UtcNow,
        });
        return grants;
    }

    private static SandboxedSkillScriptRunner CreateRunner(
        IAuditLog auditLog,
        ISkillScriptGrantStore? grants = null,
        Action<AgentPrismSkillScriptOptions>? configure = null)
    {
        var options = new AgentPrismOptions();
        options.Skills.Scripts.Enabled = true;
        options.Skills.Scripts.PlatformIsolationAcknowledged = true;
        options.Skills.Scripts.AllowStoredScripts = true;
        options.Skills.Scripts.Interpreters["py"] = "python3";
        configure?.Invoke(options.Skills.Scripts);

        var wrapper = Options.Create(options);
        return new SandboxedSkillScriptRunner(
            wrapper,
            new SingleTenantContext(wrapper),
            grants ?? new InMemorySkillScriptGrantStore(),
            auditLog,
            new NullAuditActorResolver(),
            NullLogger<SandboxedSkillScriptRunner>.Instance);
    }

    private sealed class NullAuditActorResolver : IAuditActorResolver
    {
        public string? Resolve() => null;
    }

    private sealed class ThrowingAuditLog : IAuditLog
    {
        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Array.Empty<AuditEntry>());
    }
}

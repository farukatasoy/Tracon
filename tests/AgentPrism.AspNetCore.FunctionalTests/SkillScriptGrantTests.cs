using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Script calistirma izni uclarinin davranis testleri.</summary>
public sealed class SkillScriptGrantTests
{
    private static readonly Uri Grants = new("/agentprism/api/skill-script-grants", UriKind.Relative);

    [Fact]
    public async Task Script_calistirma_kapaliyken_izin_verilemez()
    {
        // Izin verildigini gosterip calistirmayi reddetmek yanilticidir; uc
        // durumu 409 ile acikca bildirir.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Grants, Request());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Izin_verilir_listelenir_ve_iptal_edilir()
    {
        await using var host = await StartWithScriptsAsync();

        using (var created = await host.Client.PostAsJsonAsync(Grants, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
            var body = await AgentPrismTestHost.ReadJsonAsync(created);
            body.GetProperty("skillName").GetString().ShouldBe("invoice-analysis");
        }

        using (var listed = await host.Client.GetAsync(Grants))
        {
            (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using var revoked = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/skill-script-grants/invoice-analysis", UriKind.Relative));
        revoked.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Gecmiste_biten_izin_reddedilir()
    {
        await using var host = await StartWithScriptsAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Grants,
            Request() with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Yorumlayicisi_olmayan_uzantili_script_kaydedilemez()
    {
        await using var host = await StartWithScriptsAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/skills/invoice-analysis", UriKind.Relative),
            new AgentSkillRequest
            {
                Name = "invoice-analysis",
                Description = "Faturalari inceler.",
                Instructions = "Faturalari dikkatle incele.",
                Scripts =
                [
                    new AgentSkillScriptDefinition
                    {
                        Name = "run",
                        Extension = "exe",
                        Content = "MZ",
                    },
                ],
            });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static Task<AgentPrismTestHost> StartWithScriptsAsync()
        => AgentPrismTestHost.StartAsync(builder => builder.UseSkillScripts(options =>
        {
            options.PlatformIsolationAcknowledged = true;
            options.AllowStoredScripts = true;
            options.Interpreters["py"] = "python3";
        }));

    private static SkillScriptGrantRequest Request()
        => new() { SkillName = "invoice-analysis" };
}

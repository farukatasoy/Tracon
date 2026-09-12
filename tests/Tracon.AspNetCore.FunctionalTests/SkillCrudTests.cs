using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>Skill yonetim API'sinin bellek ici davranis testleri.</summary>
public sealed class SkillCrudTests
{
    private static readonly Uri Skills = new("/tracon/api/skills", UriKind.Relative);

    [Fact]
    public async Task Skill_olusturulur_guncellenir_ve_silinir()
    {
        await using var host = await TraconTestHost.StartAsync();
        var request = Request();

        using (var created = await host.Client.PutAsJsonAsync(new Uri("/tracon/api/skills/invoice-analysis", UriKind.Relative), request))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using (var updated = await host.Client.PutAsJsonAsync(
                   new Uri("/tracon/api/skills/invoice-analysis", UriKind.Relative),
                   request with { Description = "Guncel aciklama" }))
        {
            updated.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await TraconTestHost.ReadJsonAsync(updated)).GetProperty("version").GetInt32().ShouldBe(2);
        }

        using (var listed = await host.Client.GetAsync(Skills))
        {
            var skills = await TraconTestHost.ReadJsonAsync(listed);
            skills.GetArrayLength().ShouldBe(1);
            skills[0].GetProperty("resources").GetArrayLength().ShouldBe(1);
        }

        using var deleted = await host.Client.DeleteAsync(new Uri("/tracon/api/skills/invoice-analysis", UriKind.Relative));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task MAFin_gecersiz_saydigini_skill_adi_reddedilir()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/skills/Invalid_Name", UriKind.Relative),
            Request() with { Name = "Invalid_Name" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static AgentSkillRequest Request()
        => new()
        {
            Name = "invoice-analysis",
            Description = "Faturalari inceler.",
            Instructions = "Faturalari dikkatle incele.",
            Resources =
            [
                new AgentSkillResourceDefinition
                {
                    Name = "policy.md",
                    Content = "Politika metni.",
                },
            ],
        };
}

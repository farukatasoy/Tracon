using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// A skill defined in code wins its name, so a stored copy would never run -
    /// and the console editor, which reads the resolved skill, would write the
    /// code content over it. The save is refused the way a code agent's is.
    /// </summary>
    [Fact]
    public async Task A_stored_skill_cannot_be_saved_under_a_name_defined_in_code()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddSkill(CodeSkill()));

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/skills/invoice-analysis", UriKind.Relative),
            Request());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain("defined in code");

        var store = host.Services.GetRequiredService<IAgentSkillStore>();
        (await store.GetAsync("default", "invoice-analysis")).ShouldBeNull("the refused save wrote a stored copy");

        using var read = await host.Client.GetAsync(new Uri("/tracon/api/skills/invoice-analysis", UriKind.Relative));
        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("origin").GetString().ShouldBe("Code");
    }

    /// <summary>
    /// A stored copy saved before the name was taken in code never runs; deleting it
    /// is the one change the API still makes under that name. With no stored copy
    /// left, the code skill itself cannot be deleted from here.
    /// </summary>
    [Fact]
    public async Task Deleting_a_name_defined_in_code_removes_only_its_stored_copy()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddSkill(CodeSkill()));

        var store = host.Services.GetRequiredService<IAgentSkillStore>();
        await store.SaveAsync(Request().ToDefinition("default"));

        var skill = new Uri("/tracon/api/skills/invoice-analysis", UriKind.Relative);

        using (var deleted = await host.Client.DeleteAsync(skill))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await store.GetAsync("default", "invoice-analysis")).ShouldBeNull();

        using (var again = await host.Client.DeleteAsync(skill))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Conflict, await again.Content.ReadAsStringAsync());
        }

        using var read = await host.Client.GetAsync(skill);
        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("origin").GetString().ShouldBe("Code");
    }

    private static AgentSkillDefinition CodeSkill()
        => new()
        {
            TenantId = "default",
            Name = "invoice-analysis",
            Description = "Reads invoices in code.",
            Instructions = "Read the invoice.",
        };

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

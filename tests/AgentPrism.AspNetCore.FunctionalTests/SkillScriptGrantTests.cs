using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Behavior tests for the script execution grant endpoints.</summary>
public sealed class SkillScriptGrantTests
{
    private static readonly Uri Grants = new("/agentprism/api/skill-script-grants", UriKind.Relative);

    [Fact]
    public async Task Grant_cannot_be_given_while_script_execution_is_disabled()
    {
        // Showing that the grant was given and then rejecting execution
        // would be misleading; the endpoint reports the state explicitly with 409.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Grants, Request());

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Grant_is_given_listed_and_revoked()
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
    public async Task Grant_expired_in_the_past_is_rejected()
    {
        await using var host = await StartWithScriptsAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Grants,
            Request() with { ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Script_with_an_extension_that_has_no_interpreter_cannot_be_saved()
    {
        await using var host = await StartWithScriptsAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/skills/invoice-analysis", UriKind.Relative),
            new AgentSkillRequest
            {
                Name = "invoice-analysis",
                Description = "Analyzes invoices.",
                Instructions = "Examine invoices carefully.",
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

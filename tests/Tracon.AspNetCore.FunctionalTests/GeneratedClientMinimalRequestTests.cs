using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Generated = Tracon.Client.Generated;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A caller that sets only the properties the document marks required gets a working request through the generated
/// client.
/// </summary>
/// <remarks>
/// The document says a free-form member (<c>providerSettings</c>, <c>metadata</c>, <c>checks</c>, <c>payload</c>) is
/// optional and accepts <c>null</c>, so the generated type is nullable and the client leaves it unset. A member that
/// has a server default carries that default in the generated type. Before that, the first request of a caller that
/// set only the required members failed with a <c>400</c> the server's contract gives for a null, or with an
/// <see cref="InvalidOperationException"/> from the serializer.
/// </remarks>
public sealed class GeneratedClientMinimalRequestTests
{
    [Fact]
    public async Task An_agent_created_with_only_a_name_and_a_model_is_accepted()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        var client = Client(host);

        var created = await client.TraconCreateAgentAsync(
            new Generated.AgentDefinitionRequest
            {
                Name = "minimal-agent",
                Model = new Generated.ModelBinding { Provider = "echo", Model = "echo-1" },
            });

        created.Name.ShouldBe("minimal-agent");
    }

    [Fact]
    public async Task A_skill_saved_with_only_its_required_properties_is_accepted()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();
        var client = Client(host);

        var saved = await client.TraconSaveSkillAsync(
            "minimal-skill",
            new Generated.AgentSkillRequest { Name = "minimal-skill", Description = "A skill.", Instructions = "Do the thing." });

        saved.Name.ShouldBe("minimal-skill");
    }

    [Fact]
    public async Task An_eval_suite_saved_without_checks_is_accepted()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync(configureMoreTracon: static b => b.AddAgent(TestData.Definition()));
        var client = Client(host);

        var saved = await client.TraconSaveEvalSuiteAsync("minimal-suite", new Generated.EvalSuiteSaveRequest { AgentName = "kod-agent" });

        saved.Name.ShouldBe("minimal-suite");
    }

    private static Generated.TraconApiClient Client(TraconTestHost host)
    {
        host.Client.BaseAddress = new Uri("http://localhost/tracon/");

        return new Generated.TraconApiClient(host.Client);
    }
}

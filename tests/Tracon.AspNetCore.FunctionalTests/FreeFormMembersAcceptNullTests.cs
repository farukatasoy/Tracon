using System.Net;
using System.Text;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The free-form members of an agent and a skill (<c>providerSettings</c>, <c>metadata</c>) accept an explicit
/// <c>null</c> and read back as the empty dictionary.
/// </summary>
/// <remarks>
/// <para>
/// The document says these members are optional and nullable, and the generated clients leave them unset, so the
/// first request of a caller that sets only the required members carries <c>null</c>. A body is read with the
/// nullable annotations of the request type respected: a member that does not allow <c>null</c> answers
/// <c>400</c> ("doesn't allow setting null values"). The member's own getter already returned the empty dictionary
/// for <c>null</c>, so the server tolerated the value everywhere except at the door.
/// </para>
/// <para>
/// This is the HTTP level on purpose. A unit test of the property accessor says nothing about the body reader that
/// refuses the value before the accessor runs, and it is also the level an older client (which sends the
/// <c>null</c> today) sees.
/// </para>
/// </remarks>
public sealed class FreeFormMembersAcceptNullTests
{
    private const string AgentBody = """
        {"name":"null-members","model":{"provider":"echo","model":"echo-1","providerSettings":null},"metadata":null}
        """;

    [Fact]
    public async Task An_agent_saved_with_null_provider_settings_and_null_metadata_reads_back_with_empty_ones()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var created = await host.Client.PostAsync(AgentsUri, Json(AgentBody));

        created.IsSuccessStatusCode.ShouldBeTrue(await created.Content.ReadAsStringAsync());

        using var read = await host.Client.GetAsync(new Uri("/tracon/api/agents/null-members", UriKind.Relative));
        var definition = (await TraconTestHost.ReadJsonAsync(read)).GetProperty("definition");

        definition.GetProperty("metadata").ValueKind.ShouldBe(JsonValueKind.Object);
        definition.GetProperty("metadata").EnumerateObject().ShouldBeEmpty();
        definition.GetProperty("model").GetProperty("providerSettings").ValueKind.ShouldBe(JsonValueKind.Object);
        definition.GetProperty("model").GetProperty("providerSettings").EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agent_updated_with_null_provider_settings_and_null_metadata_is_accepted()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var created = await host.Client.PostAsync(
            AgentsUri,
            Json("""{"name":"null-members","model":{"provider":"echo","model":"echo-1","providerSettings":{"echo.note":"kept"}},"metadata":{"owner":"a"}}"""));
        created.IsSuccessStatusCode.ShouldBeTrue(await created.Content.ReadAsStringAsync());

        using var updated = await host.Client.PutAsync(new Uri("/tracon/api/agents/null-members", UriKind.Relative), Json(AgentBody));

        updated.IsSuccessStatusCode.ShouldBeTrue(await updated.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_validation_request_with_null_provider_settings_and_null_metadata_is_answered_with_a_report()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var validated = await host.Client.PostAsync(new Uri("/tracon/api/agents/validate", UriKind.Relative), Json(AgentBody));

        validated.StatusCode.ShouldBe(HttpStatusCode.OK, await validated.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_skill_saved_with_null_metadata_reads_back_with_empty_metadata()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsync(
            new Uri("/tracon/api/skills/null-metadata", UriKind.Relative),
            Json("""{"name":"null-metadata","description":"A skill.","instructions":"Do the thing.","metadata":null}"""));

        saved.IsSuccessStatusCode.ShouldBeTrue(await saved.Content.ReadAsStringAsync());

        using var read = await host.Client.GetAsync(new Uri("/tracon/api/skills/null-metadata", UriKind.Relative));
        var metadata = (await TraconTestHost.ReadJsonAsync(read)).GetProperty("metadata");

        metadata.ValueKind.ShouldBe(JsonValueKind.Object);
        metadata.EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_skill_saved_with_metadata_keeps_it()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var saved = await host.Client.PutAsync(
            new Uri("/tracon/api/skills/kept-metadata", UriKind.Relative),
            Json("""{"name":"kept-metadata","description":"A skill.","instructions":"Do the thing.","metadata":{"owner":"platform"}}"""));

        saved.IsSuccessStatusCode.ShouldBeTrue(await saved.Content.ReadAsStringAsync());

        using var read = await host.Client.GetAsync(new Uri("/tracon/api/skills/kept-metadata", UriKind.Relative));

        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("metadata").GetProperty("owner").GetString().ShouldBe("platform");
    }

    private static Uri AgentsUri { get; } = new("/tracon/api/agents", UriKind.Relative);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}

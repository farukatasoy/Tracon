using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests.DefinitionTransfer;

/// <summary>
/// The file is a long-lived contract: a newer file, a field this version does not know, a body that is empty, wrong
/// or too large are all refused, and none of them is dropped silently.
/// </summary>
public sealed class DefinitionImportEnvelopeTests
{
    [Fact]
    public async Task A_newer_formatVersion_is_refused()
    {
        await using var host = await TraconTestHost.StartAsync();
        var bundle = TransferHost.NewBundle();

        bundle["formatVersion"] = 2;

        var problem = await PostRefusedAsync(host, bundle.ToJsonString(), HttpStatusCode.BadRequest);

        problem.GetProperty("title").GetString().ShouldBe("File format too new");
    }

    [Fact]
    public async Task A_file_of_another_kind_is_refused()
    {
        await using var host = await TraconTestHost.StartAsync();
        var bundle = TransferHost.NewBundle();

        bundle["format"] = "something.else";

        var problem = await PostRefusedAsync(host, bundle.ToJsonString(), HttpStatusCode.BadRequest);

        problem.GetProperty("title").GetString().ShouldBe("Not a definitions file");
    }

    [Fact]
    public async Task A_field_this_version_does_not_know_is_refused_with_its_path()
    {
        await using var host = await TraconTestHost.StartAsync();

        // The top level, and inside the definition of an agent: the reader refuses both and names the field.
        var top = TransferHost.NewBundle();

        top["addedByALaterVersion"] = true;

        (await PostRefusedAsync(host, top.ToJsonString(), HttpStatusCode.BadRequest))
            .GetProperty("detail").GetString()!.ShouldContain("addedByALaterVersion");

        var nested = TransferHost.NewBundle().WithAgent(TestData.Request("a"));

        nested["agents"]![0]!["definition"]!["nonsense"] = 1;

        (await PostRefusedAsync(host, nested.ToJsonString(), HttpStatusCode.BadRequest))
            .GetProperty("detail").GetString()!.ShouldContain("nonsense");
    }

    [Fact]
    public async Task A_null_where_a_list_is_required_is_refused()
    {
        await using var host = await TraconTestHost.StartAsync();
        var bundle = TransferHost.NewBundle();

        bundle["agents"] = null;

        await PostRefusedAsync(host, bundle.ToJsonString(), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_empty_body_and_a_body_that_is_not_JSON_are_refused()
    {
        await using var host = await TraconTestHost.StartAsync();

        await PostRefusedAsync(host, string.Empty, HttpStatusCode.BadRequest);
        await PostRefusedAsync(host, "this is not json", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_body_that_is_not_declared_as_JSON_is_refused()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsync(
            TransferHost.Api("definitions/import"),
            new StringContent(TransferHost.NewBundle().ToJsonString(), Encoding.UTF8, "text/plain"));

        response.StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_before_it_is_read()
    {
        await using var host = await TraconTestHost.StartAsync();
        var bundle = TransferHost.NewBundle().WithAgent(TestData.Request("big", new string('a', 9 * 1024 * 1024)));

        await PostRefusedAsync(host, bundle.ToJsonString(), HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task An_empty_file_is_a_valid_import_with_nothing_in_it()
    {
        await using var host = await TraconTestHost.StartAsync();

        var dry = await host.ImportOkAsync(TransferHost.NewBundle(), dryRun: true);

        dry["valid"]!.GetValue<bool>().ShouldBeTrue();
        dry["items"]!.AsArray().Count.ShouldBe(0);

        var applied = await host.ImportOkAsync(TransferHost.NewBundle(), dryRun: false);

        applied["applied"]!.GetValue<bool>().ShouldBeTrue();
        applied["written"]!.AsArray().Count.ShouldBe(0);
    }

    [Fact]
    public async Task A_kind_that_is_not_named_in_types_is_skipped_and_left_alone()
    {
        await using var host = await TraconTestHost.StartAsync();
        var bundle = TransferHost.NewBundle().WithSkill("only-this").WithAgent(TestData.Request("not-this"));

        var applied = await host.ImportOkAsync(bundle, dryRun: false, types: "skills");

        TransferHost.Actions(applied, "skill").ShouldBe(["only-this:create"]);
        TransferHost.Actions(applied, "agent").ShouldBe(["not-this:skipped"]);
        applied["written"]!.AsArray().Select(entry => entry!.GetValue<string>()).ToArray().ShouldBe(["skill:only-this"]);
        (await host.AgentExistsAsync("not-this")).ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_kind_in_types_is_refused()
    {
        await using var host = await TraconTestHost.StartAsync();

        var (response, _) = await host.ImportAsync(TransferHost.NewBundle(), dryRun: true, types: "agents,bogus");

        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    private static async Task<JsonElement> PostRefusedAsync(TraconTestHost host, string body, HttpStatusCode expected)
    {
        using var response = await host.Client.PostAsync(
            TransferHost.Api("definitions/import"),
            new StringContent(body, Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(expected, text);

        return JsonDocument.Parse(text).RootElement.Clone();
    }
}

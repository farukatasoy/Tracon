using System.Net;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.WorkflowSettingsFixtures;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>GET /api/workflows/functions</c> carries the schema of each function's settings (F-295), for
/// display only: it lets the console tell an operator what a node of that function takes.
/// </summary>
public sealed class WorkflowFunctionCatalogSchemaTests
{
    [Fact]
    public async Task A_function_with_settings_carries_its_schema_and_one_without_carries_null()
    {
        await using var host = await StartAsync(static builder => builder
            .AddWorkflowFunction<string, string>("plain", static _ => static (input, _, _) => new ValueTask<string>(input)));

        using var response = await host.Client.GetAsync("/tracon/api/workflows/functions");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var functions = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();
        var tts = functions.Single(static function => string.Equals(function.GetProperty("name").GetString(), Tts, StringComparison.Ordinal));
        var plain = functions.Single(static function => string.Equals(function.GetProperty("name").GetString(), "plain", StringComparison.Ordinal));

        var schema = tts.GetProperty("settingsSchema");

        schema.GetProperty("type").GetString().ShouldBe("object");
        schema.GetProperty("properties").GetProperty("voice").GetProperty("type").GetString().ShouldBe("string");
        schema.GetProperty("required").EnumerateArray().Select(static name => name.GetString()).ShouldBe(["voice"]);
        schema.GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();

        plain.GetProperty("settingsSchema").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_settings_type_the_exporter_only_half_understands_does_not_break_the_list()
    {
        // 11: a System.Type member is one the exporter describes as "unsupported". The list - every
        // function, not just this one - must still be served.
        await using var host = await StartAsync(static builder => builder
            .AddWorkflowFunction<string, string, OddSettings>("odd", static _ => static (input, _, _, _) => new ValueTask<string>(input)));

        using var response = await host.Client.GetAsync("/tracon/api/workflows/functions");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var names = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()
            .Select(static function => function.GetProperty("name").GetString())
            .ToList();

        names.ShouldBe(["odd", "tts"], ignoreOrder: true);
    }

    internal sealed class OddSettings
    {
        public string Name { get; set; } = string.Empty;

        public Type? Kind { get; set; }
    }
}

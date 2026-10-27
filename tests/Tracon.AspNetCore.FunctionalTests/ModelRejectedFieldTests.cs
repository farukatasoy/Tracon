using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-606 — a model can reject a request field that its provider's API surface sends. The catalog says so
/// (<see cref="ModelDescriptor.RejectedSamplingFields"/>), read from configuration, and the validation endpoint and
/// the save path report it before the first run, as they do for a field the surface drops.
/// </summary>
/// <remarks>
/// <para>
/// Measured with a real call (2026-10-27, OpenAI, <c>gpt-5.4-mini</c>, <c>gpt-5.6-luna</c>, <c>gpt-5.6-terra</c>): the
/// Chat Completions surface sends <c>stop</c>, and each of the three models answers a request that carries it with
/// <c>400 unsupported_parameter</c>. The catalog below is the shape an operator writes for such a model.
/// </para>
/// <para>
/// The catalog goes through the configuration binder of the real <c>UseOpenAI</c> call, so the test proves the whole path
/// from the key to the HTTP answer. The model list is read as raw JSON: a flags enum without its string converter would
/// round-trip through typed objects and only show up as a number on the wire.
/// </para>
/// </remarks>
public sealed class ModelRejectedFieldTests
{
    private const string TestKey = "functional-test-key";

    private static readonly Uri ValidateUri = new("/tracon/api/agents/validate", UriKind.Relative);

    [Fact]
    public async Task A_model_the_catalog_says_rejects_stop_is_reported_before_the_first_run_with_the_field_path()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(ValidateUri, Definition("gpt-no-stop", stop: ["END"]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var report = await TraconTestHost.ReadJsonAsync(response);

        report.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var finding = report.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();

        finding.GetProperty("code").GetString().ShouldBe("sampling_field_rejected_by_model");
        finding.GetProperty("path").GetString().ShouldBe("model.stopSequences");
        finding.GetProperty("severity").GetString().ShouldBe("Error");

        var message = finding.GetProperty("message").GetString().ShouldNotBeNull();

        message.ShouldContain("ModelBinding.StopSequences", Case.Sensitive);
        message.ShouldContain("'gpt-no-stop'", Case.Sensitive);
        message.ShouldContain("'openai' provider", Case.Sensitive);
    }

    [Fact]
    public async Task The_same_definition_on_a_model_the_catalog_does_not_name_is_valid()
    {
        // The regression: a catalog entry that names no field, and a model the catalog does not list, change nothing.
        await using var host = await StartAsync();

        foreach (var model in new[] { "gpt-plain", "gpt-not-in-the-catalog" })
        {
            using var response = await host.Client.PostAsJsonAsync(ValidateUri, Definition(model, stop: ["END"], seed: 7));
            var report = await TraconTestHost.ReadJsonAsync(response);

            report.GetProperty("valid").GetBoolean().ShouldBeTrue(report.ToString());
            report.GetProperty("messages").GetArrayLength().ShouldBe(0);
        }
    }

    [Fact]
    public async Task Saving_a_definition_for_a_rejecting_model_is_a_400_that_names_the_field_and_nothing_is_stored()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(new Uri("/tracon/api/agents", UriKind.Relative), Definition("gpt-no-stop", stop: ["END"]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);
        var detail = problem.GetProperty("detail").GetString().ShouldNotBeNull();

        detail.ShouldContain("ModelBinding.StopSequences", Case.Sensitive);
        detail.ShouldContain("'gpt-no-stop'", Case.Sensitive);

        using var stored = await host.Client.GetAsync(new Uri("/tracon/api/agents/range-probe", UriKind.Relative));

        stored.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_fallback_link_whose_model_rejects_the_field_is_reported_with_the_link_model()
    {
        // The link gets the primary call's stop sequences, so it fails the same way at the first fallback.
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            ValidateUri,
            Definition("gpt-plain", stop: ["END"], fallbacks: [new { provider = "openai", model = "gpt-no-stop" }]));

        var report = await TraconTestHost.ReadJsonAsync(response);
        var finding = report.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();

        finding.GetProperty("code").GetString().ShouldBe("sampling_field_rejected_by_model");
        finding.GetProperty("path").GetString().ShouldBe("model.stopSequences");
        finding.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("'gpt-no-stop'", Case.Sensitive);
    }

    [Fact]
    public async Task A_field_the_model_does_not_reject_stays_valid_next_to_one_it_does()
    {
        // The definition sets two fields and the model rejects one of them: the report names that one and no other.
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(ValidateUri, Definition("gpt-no-stop", stop: ["END"], seed: 7));

        var report = await TraconTestHost.ReadJsonAsync(response);

        report.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var finding = report.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();

        finding.GetProperty("code").GetString().ShouldBe("sampling_field_rejected_by_model");
        finding.GetProperty("path").GetString().ShouldBe("model.stopSequences");
    }

    [Fact]
    public async Task The_model_list_carries_the_rejected_fields_as_names_and_no_limits()
    {
        await using var host = await StartAsync();

        var providers = await host.Client.GetFromJsonAsync<JsonElement>(new Uri("/tracon/api/models", UriKind.Relative));
        var openai = providers.EnumerateArray().Single(static provider => string.Equals(provider.GetProperty("name").GetString(), "openai", StringComparison.Ordinal));
        var models = openai.GetProperty("models").EnumerateArray().ToDictionary(static model => model.GetProperty("name").GetString()!, StringComparer.Ordinal);

        // Names, not numbers (a flags enum without its converter would write 4). A set is a comma-separated list.
        models["gpt-no-stop"].GetProperty("rejectedSamplingFields").ValueKind.ShouldBe(JsonValueKind.String);
        models["gpt-no-stop"].GetProperty("rejectedSamplingFields").GetString().ShouldBe("StopSequences");
        models["gpt-two"].GetProperty("rejectedSamplingFields").GetString().ShouldBe("Seed, StopSequences");
        models["gpt-plain"].GetProperty("rejectedSamplingFields").GetString().ShouldBe("None");

        // The ranges a provider accepts are not part of the model list; the validation report carries them.
        var wire = providers.GetRawText();

        wire.ShouldNotContain("amplingLimits", Case.Sensitive);
        wire.ShouldNotContain("maxStopSequences", Case.Sensitive);
        wire.ShouldNotContain("upportedSamplingFields", Case.Sensitive);
    }

    private static async Task<TraconTestHost> StartAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Providers:OpenAI:ApiKey"] = TestKey,
                ["Tracon:Providers:OpenAI:Models:0:Name"] = "gpt-no-stop",
                ["Tracon:Providers:OpenAI:Models:0:RejectedSamplingFields"] = "StopSequences",
                ["Tracon:Providers:OpenAI:Models:1:Name"] = "gpt-plain",
                ["Tracon:Providers:OpenAI:Models:2:Name"] = "gpt-two",
                ["Tracon:Providers:OpenAI:Models:2:RejectedSamplingFields"] = "seed, StopSequences",
            })
            .Build();

        return await TraconTestHost.StartAsync(
            configureTracon: builder => builder.UseOpenAI(configuration.GetSection("Tracon:Providers:OpenAI")));
    }

    private static Dictionary<string, object?> Definition(
        string model,
        string[]? stop = null,
        long? seed = null,
        object[]? fallbacks = null)
        => new(StringComparer.Ordinal)
        {
            ["name"] = "range-probe",
            ["instructions"] = "Answer briefly.",
            ["model"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["provider"] = "openai",
                ["model"] = model,
                ["stopSequences"] = stop,
                ["seed"] = seed,
                ["fallbacks"] = fallbacks ?? [],
            },
        };
}

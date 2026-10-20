using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-603 — the hosted tool support table of the shipped providers, as the validation endpoint reports it. A kind the
/// provider's API surface would drop is an error that names the tool, its kind and the provider; it is never dropped
/// silently.
/// </summary>
/// <remarks>
/// <para>
/// The table below is what each adapter writes on the wire (<c>HostedToolWireTests</c> pins the OpenAI and Anthropic
/// rows against the request body; Google rests on the real calls of 2026-10-20 and the manual acceptance cases). A row
/// changes when an SDK upgrade changes what its adapter sends.
/// </para>
/// <para>
/// The providers are the real ones, built without a network call, so a row fails if the provider package declares
/// something other than the measured support.
/// </para>
/// </remarks>
public sealed class HostedToolSupportTableTests
{
    private const string TestKey = "functional-test-key";
    private const string CompatibleProvider = "compatible-test";

    private static readonly (string Tool, string Kind)[] Kinds =
    [
        ("web", "WebSearch"),
        ("python", "CodeInterpreter"),
        ("docs", "FileSearch"),
    ];

    // true: the provider's API surface runs the kind. web search, code interpreter, file search.
    private static readonly Dictionary<string, bool[]> Support = new(StringComparer.Ordinal)
    {
        [OpenAIProviderNames.ChatCompletions] = [false, false, false],
        [OpenAIProviderNames.Responses] = [true, true, true],
        [CompatibleProvider] = [false, false, false],
        [CompatibleProvider + "-responses"] = [true, true, true],
        [AzureOpenAIProviderNames.AzureOpenAI] = [false, false, false],
        [AnthropicProviderNames.Anthropic] = [true, true, false],
        [GoogleProviderNames.Google] = [true, true, false],
    };

    public static TheoryData<string, string, string, bool> Table()
    {
        var data = new TheoryData<string, string, string, bool>();

        foreach (var (provider, support) in Support)
        {
            for (var index = 0; index < Kinds.Length; index++)
            {
                data.Add(provider, Kinds[index].Tool, Kinds[index].Kind, support[index]);
            }
        }

        return data;
    }

    /// <param name="provider">The provider name.</param>
    /// <param name="tool">The registered hosted tool the definition selects.</param>
    /// <param name="kind">The kind of that tool.</param>
    /// <param name="supported">Whether the provider's API surface runs it.</param>
    [Theory]
    [MemberData(nameof(Table))]
    public async Task The_validation_report_agrees_with_the_measured_support_table(string provider, string tool, string kind, bool supported)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            Body(provider, tool));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        var messages = body.GetProperty("messages").EnumerateArray().ToList();

        if (supported)
        {
            body.GetProperty("valid").GetBoolean().ShouldBeTrue(body.ToString());
            messages.ShouldBeEmpty();

            return;
        }

        body.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var error = messages.ShouldHaveSingleItem();
        error.GetProperty("code").GetString().ShouldBe("hosted_tool_unsupported");
        error.GetProperty("path").GetString().ShouldBe("toolNames[0]");

        var text = error.GetProperty("message").GetString().ShouldNotBeNull();
        text.ShouldContain($"'{tool}' ({kind})", Case.Sensitive);
        text.ShouldContain($"'{provider}' provider", Case.Sensitive);
    }

    /// <summary>The save path runs the same check: the agent is not stored, and the answer names the tool and the provider.</summary>
    [Fact]
    public async Task Saving_a_code_interpreter_for_chat_completions_is_a_400_that_names_the_tool_and_the_providers_that_run_it()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            Body(OpenAIProviderNames.ChatCompletions, "python"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString().ShouldNotBeNull();

        detail.ShouldContain("'python' (CodeInterpreter)", Case.Sensitive);
        detail.ShouldContain("'openai' provider", Case.Sensitive);
        detail.ShouldContain("anthropic", Case.Sensitive);
        detail.ShouldContain("openai-responses", Case.Sensitive);

        using var stored = await host.Client.GetAsync(new Uri("/tracon/api/agents/tool-probe", UriKind.Relative));

        stored.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>A fallback link receives the primary call's tools, so it is held to the same support.</summary>
    [Fact]
    public async Task A_fallback_link_that_would_drop_a_tool_is_reported_with_the_links_provider()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            new
            {
                name = "tool-probe",
                instructions = "Answer briefly.",
                toolNames = new[] { "docs" },
                model = new
                {
                    provider = OpenAIProviderNames.Responses,
                    model = "gpt-5.4-mini",
                    fallbacks = new[] { new { provider = AnthropicProviderNames.Anthropic, model = "claude-sonnet-5" } },
                },
            });

        var body = await TraconTestHost.ReadJsonAsync(response);
        var message = body.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem().GetProperty("message").GetString().ShouldNotBeNull();

        message.ShouldContain("'anthropic' provider", Case.Sensitive);
        message.ShouldNotContain("'openai-responses' provider", Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(configureTracon: static builder => builder
            .UseOpenAI(TestKey)
            .UseOpenAICompatible(CompatibleProvider, static options =>
            {
                options.Endpoint = new Uri("http://127.0.0.1:9/v1");
                options.EnableResponsesSurface = true;
            })
            .UseAnthropic(TestKey, static options => options.DefaultModel = "claude-sonnet-5")
            .UseGoogle(TestKey, static options => options.DefaultModel = "gemini-3.6-flash")
            .UseAzureOpenAI(new Uri("https://test-resource.openai.azure.com/"), TestKey, static options => options.DefaultDeployment = "production-gpt")
            .AddHostedTool("web", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool())
            .AddHostedTool("python", HostedToolKind.CodeInterpreter, static _ => new HostedCodeInterpreterTool())
            .AddHostedTool("docs", HostedToolKind.FileSearch, static _ => new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent("vs_1")] }));

    private static Dictionary<string, object> Body(string provider, string tool) => new(StringComparer.Ordinal)
    {
        ["name"] = "tool-probe",
        ["instructions"] = "Answer briefly.",
        ["toolNames"] = new[] { tool },
        ["model"] = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["provider"] = provider,
            ["model"] = "gpt-4o-mini",
        },
    };
}

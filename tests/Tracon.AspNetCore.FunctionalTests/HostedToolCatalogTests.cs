using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using TraconTestHost = Tracon.AspNetCore.FunctionalTests.Infrastructure.TraconTestHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A hosted tool beside the function tools, through a real host: one list of tools, one namespace of
/// names, and a validation report that says which provider surface drops a tool before a run finds out.
/// </summary>
/// <remarks>
/// The rules themselves are covered at unit level (<c>HostedToolRegistryTests</c>, <c>HostedToolSupportTests</c>);
/// what only a host shows is the registration at start, the HTTP shapes, and the compiler that was built inside
/// the catalog's own construction chain.
/// </remarks>
public sealed class HostedToolCatalogTests
{
    private const string ProviderName = "hosted-model";
    private static readonly Uri Tools = new("/tracon/api/tools", UriKind.Relative);
    private static readonly Uri Validate = new("/tracon/api/agents/validate", UriKind.Relative);

    [Fact]
    public async Task The_tool_list_holds_function_tools_and_hosted_tools_ordered_by_name()
    {
        await using var host = await StartAsync(new ContentModelProvider([new TextContent("ok")], ProviderName));

        using var response = await host.Client.GetAsync(Tools, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tools = await response.Content.ReadFromJsonAsync<List<ToolDescriptor>>(TestContext.Current.CancellationToken);

        tools.ShouldNotBeNull().Select(static tool => tool.Name).ShouldBe(["docs", "lookup", "python", "search"]);

        var docs = tools.Single(static tool => string.Equals(tool.Name, "docs", StringComparison.Ordinal));
        docs.HostedKind.ShouldBe(HostedToolKind.FileSearch);
        docs.Description.ShouldBe("Searches the product documentation.");
        docs.JsonSchema.ShouldBeNull();
        docs.RunsOnClient.ShouldBeFalse();
        docs.RequiresApproval.ShouldBeFalse();
        docs.Effect.ShouldBe(ToolEffect.External);

        tools.Single(static tool => string.Equals(tool.Name, "lookup", StringComparison.Ordinal)).HostedKind.ShouldBeNull();
    }

    [Fact]
    public async Task The_kind_travels_as_a_name_like_the_other_enums_of_the_descriptor()
    {
        await using var host = await StartAsync(new ContentModelProvider([new TextContent("ok")], ProviderName));

        var json = await host.Client.GetFromJsonAsync<JsonElement>(Tools, TestContext.Current.CancellationToken);
        var search = json.EnumerateArray().Single(static tool => string.Equals(tool.GetProperty("name").GetString(), "search", StringComparison.Ordinal));

        search.GetProperty("hostedKind").GetString().ShouldBe("WebSearch");
    }

    [Fact]
    public async Task Listing_the_tools_runs_no_factory()
    {
        var factories = 0;

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(new ContentModelProvider([new TextContent("ok")], ProviderName))
            .AddHostedTool("search", HostedToolKind.WebSearch, _ =>
            {
                Interlocked.Increment(ref factories);

                return new HostedWebSearchTool();
            }));

        using var response = await host.Client.GetAsync(Tools, TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();
        factories.ShouldBe(0);
    }

    [Fact]
    public async Task Two_hosted_tools_with_one_name_stop_the_host()
    {
        var exception = await Should.ThrowAsync<TraconException>(() => TraconTestHost.StartAsync(builder => builder
            .AddHostedTool("search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool())
            .AddHostedTool("search", HostedToolKind.CodeInterpreter, static _ => new HostedCodeInterpreterTool())));

        exception.Message.ShouldContain("'search'");
    }

    [Fact]
    public async Task A_hosted_tool_named_like_a_function_tool_stops_the_host()
    {
        var exception = await Should.ThrowAsync<TraconException>(() => TraconTestHost.StartAsync(builder => builder
            .AddTool((Func<string>)(static () => "42"), name: "search", description: "Looks something up.")
            .AddHostedTool("search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool())));

        exception.Message.ShouldContain("'search'");
        exception.Message.ShouldContain("hosted tool and as a function tool");
    }

    [Fact]
    public void The_registration_refuses_a_name_the_tool_rules_refuse_and_a_kind_that_is_not_defined()
    {
        var builder = new ServiceCollection().AddTracon();

        Should.Throw<TraconException>(() => builder.AddHostedTool("has space", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool()));
        Should.Throw<ArgumentOutOfRangeException>(() => builder.AddHostedTool("odd", (HostedToolKind)99, static _ => new HostedWebSearchTool()));
        Should.Throw<ArgumentNullException>(() => builder.AddHostedTool("odd", HostedToolKind.WebSearch, null!));
    }

    [Fact]
    public async Task The_validation_report_names_a_tool_the_providers_surface_drops_and_does_not_call_it_unknown()
    {
        // A surface that runs a web search only.
        await using var host = await StartAsync(new ContentModelProvider([new TextContent("ok")], ProviderName, HostedToolKinds.WebSearch));

        using var response = await host.Client.PostAsJsonAsync(
            Validate,
            Definition(["search", "python", "docs", "lookup"]),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var report = await TraconTestHost.ReadJsonAsync(response);
        var messages = report.GetProperty("messages").EnumerateArray().ToArray();

        report.GetProperty("valid").GetBoolean().ShouldBeFalse();
        messages.Select(static message => (message.GetProperty("code").GetString(), message.GetProperty("path").GetString()))
            .ShouldBe([("hosted_tool_unsupported", "toolNames[1]"), ("hosted_tool_unsupported", "toolNames[2]")]);
        var text = messages[0].GetProperty("message").GetString().ShouldNotBeNull();

        text.ShouldContain("'python' (CodeInterpreter)", Case.Sensitive);
        text.ShouldContain($"'{ProviderName}' provider", Case.Sensitive);
    }

    [Fact]
    public async Task The_validation_report_is_clean_when_the_surface_runs_every_kind_selected()
    {
        await using var host = await StartAsync(new ContentModelProvider([new TextContent("ok")], ProviderName));

        using var response = await host.Client.PostAsJsonAsync(Validate, Definition(["search", "python", "docs", "lookup"]), TestContext.Current.CancellationToken);

        var report = await TraconTestHost.ReadJsonAsync(response);

        report.GetProperty("valid").GetBoolean().ShouldBeTrue();
        report.GetProperty("messages").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_validation_report_flags_two_tools_of_one_kind()
    {
        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(new ContentModelProvider([new TextContent("ok")], ProviderName))
            .AddHostedTool("search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool())
            .AddHostedTool("news", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool()));

        using var response = await host.Client.PostAsJsonAsync(Validate, Definition(["search", "news"]), TestContext.Current.CancellationToken);

        var report = await TraconTestHost.ReadJsonAsync(response);
        var message = report.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();

        message.GetProperty("code").GetString().ShouldBe("duplicate_hosted_tool_kind");
        message.GetProperty("path").GetString().ShouldBe("toolNames[1]");
    }

    [Fact]
    public async Task An_agent_whose_surface_drops_its_tool_does_not_compile_and_reaches_no_provider()
    {
        var provider = new ContentModelProvider([new TextContent("ok")], ProviderName, HostedToolKinds.None);

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddHostedTool("search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool())
            .AddAgent(new AgentDefinition
            {
                Name = "hosted-agent",
                Instructions = "Answer.",
                Model = new ModelBinding { Provider = ProviderName, Model = $"{ProviderName}-1" },
                ToolNames = ["search"],
                Origin = AgentDefinitionOrigin.Code,
            }));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/hosted-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "hi" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("'search' (WebSearch)");
        provider.Options.ShouldBeEmpty();
    }

    private static object Definition(string[] toolNames) => new
    {
        name = "hosted-agent",
        instructions = "Answer briefly.",
        toolNames,
        model = new { provider = ProviderName, model = $"{ProviderName}-1" },
    };

    private static Task<TraconTestHost> StartAsync(ContentModelProvider provider)
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(provider)
            .AddTool((Func<string>)(static () => "42"), name: "lookup", description: "Looks something up.")
            .AddHostedTool("search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool())
            .AddHostedTool("python", HostedToolKind.CodeInterpreter, static _ => new HostedCodeInterpreterTool())
            .AddHostedTool(
                "docs",
                HostedToolKind.FileSearch,
                static _ => new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent("vs_1")] },
                description: "Searches the product documentation."));
}

using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.Client.Generated;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The generated .NET client reads the tool list of a host that has a hosted tool.
/// </summary>
/// <remarks>
/// 🚨 Found by the phase audit, not by a test: the document wraps a nullable enum property in
/// <c>oneOf: [null, $ref]</c> and also lists <c>null</c> in the enum itself, and NJsonSchema then wrote an empty class named
/// after the property (<c>public HostedKind? HostedKind</c>) in place of the enum. The client built, the coverage test saw
/// the method, and <c>TraconListToolsAsync</c> threw <c>JsonException</c> the first time a host registered a hosted tool.
/// Eight properties of the older surface (<c>RunComparisonSide.ErrorClass</c> among them) had the same defect, and the
/// repair of the generator pipeline closes them all.
/// <c>ClientCoverageTests</c> proves a method NAME exists; only a value crossing the real HTTP boundary proves it reads.
/// </remarks>
public sealed class HostedToolClientTests
{
    [Fact]
    public async Task The_generated_client_lists_a_hosted_tool_with_its_kind_and_a_function_tool_without_one()
    {
        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddTool((Func<string>)(static () => "42"), name: "lookup", description: "Looks something up.")
            .AddHostedTool("search", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool(), "Searches the web."));

        host.Client.BaseAddress = new Uri(host.Client.BaseAddress!, "tracon/");

        var tools = await new TraconApiClient(host.Client).TraconListToolsAsync(TestContext.Current.CancellationToken);

        var search = tools.Single(static tool => string.Equals(tool.Name, "search", StringComparison.Ordinal));

        search.HostedKind.ShouldBe(Tracon.Client.Generated.HostedToolKind.WebSearch);
        search.Description.ShouldBe("Searches the web.");
        tools.Single(static tool => string.Equals(tool.Name, "lookup", StringComparison.Ordinal)).HostedKind.ShouldBeNull();
    }
}

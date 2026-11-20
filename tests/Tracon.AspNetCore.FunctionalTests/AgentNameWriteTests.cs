using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The management API refuses a name it could not address again, and keeps accepting every name it can.
/// </summary>
/// <remarks>
/// A '/' in a name cannot be routed: the escaped form in the <c>Location</c> header is not decoded back, so a definition
/// saved under it would be created and then never read, updated, deleted or run by name. The store and the catalog
/// accept the name (their own tests say so); only the writer that turns it into an address says no.
/// </remarks>
public sealed class AgentNameWriteTests
{
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);
    private static readonly Uri Validate = new("/tracon/api/agents/validate", UriKind.Relative);

    [Fact]
    public async Task A_name_with_a_slash_is_refused_with_400_and_nothing_is_saved()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Agents, TestData.Request(name: "team/support"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var problem = await TraconTestHost.ReadJsonAsync(response);
        problem.GetProperty("title").GetString().ShouldBe("Agent name invalid");
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("'/'");

        using var listed = await host.Client.GetAsync(Agents);
        (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray()
            .Select(static agent => agent.GetProperty("name").GetString())
            .ShouldNotContain("team/support", StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_dry_run_says_what_the_save_would_say()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Validate, TestData.Request(name: "team/support"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("title").GetString().ShouldBe("Agent name invalid");
    }

    [Theory]
    [InlineData("Customer Support (EN)")]
    [InlineData("what is this?")]
    [InlineData("naïve café")]
    [InlineData("a-b_c.d")]
    public async Task A_name_the_address_can_carry_is_saved_and_read_back_by_the_address_the_save_returned(string name)
    {
        await using var host = await TraconTestHost.StartAsync();

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request(name: name));

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        using var read = await host.Client.GetAsync(created.Headers.Location);

        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(read)).GetProperty("descriptor").GetProperty("name").GetString().ShouldBe(name);
    }
}

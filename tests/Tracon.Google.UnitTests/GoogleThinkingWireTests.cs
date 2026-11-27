using System.Text.Json;
using Microsoft.Extensions.AI;
using Tracon.Google.UnitTests.Infrastructure;
using Tracon.ProviderCore.Tests;

namespace Tracon.Google.UnitTests;

/// <summary>
/// The <c>thinkingConfig</c> the real Google adapter writes for a thinking setting and for the portable reasoning
/// effort, and the rule that a Gemini request carries one control for the depth of thinking and not two.
/// </summary>
/// <remarks>
/// <para>
/// The adapter maps <see cref="ReasoningEffort"/> by itself: <c>None</c> becomes a thinking budget of zero, <c>Low</c>
/// and <c>Medium</c> the thinking levels of those names, and <c>High</c> and <c>ExtraHigh</c> the high level. It fills a
/// field only when the request has none, so a setting written into the request first is kept. A budget and a level
/// together are two controls for one thing, and the service refuses the pair; a setting Tracon writes must therefore be
/// the only one that reaches the wire. The tests read the body the adapter writes against a local server.
/// </para>
/// <para>
/// The refusal itself is the service's and is not measured here: no test reaches Google. What the tests prove is what
/// leaves the process.
/// </para>
/// </remarks>
public sealed class GoogleThinkingWireTests
{
    private const string Candidate = """{"candidates":[{"content":{"role":"model","parts":[{"text":"ok"}]},"finishReason":"STOP"}]}""";

    [Theory]
    [InlineData("minimal", "MINIMAL")]
    [InlineData("low", "LOW")]
    [InlineData("medium", "MEDIUM")]
    [InlineData("HIGH", "HIGH")]
    public async Task The_thinking_level_setting_is_the_only_thinking_field_on_the_wire(string setting, string onTheWire)
    {
        var config = await ThinkingConfigAsync(Level(setting));

        config.GetProperty("thinkingLevel").GetString().ShouldBe(onTheWire);
        config.EnumerateObject().Select(static property => property.Name).ShouldBe(["thinkingLevel"]);
    }

    [Theory]
    [InlineData(ReasoningEffort.None, "thinkingBudget")]
    [InlineData(ReasoningEffort.Low, "thinkingLevel")]
    [InlineData(ReasoningEffort.Medium, "thinkingLevel")]
    [InlineData(ReasoningEffort.High, "thinkingLevel")]
    [InlineData(ReasoningEffort.ExtraHigh, "thinkingLevel")]
    public async Task The_reasoning_effort_alone_is_mapped_by_the_adapter_to_one_thinking_field(ReasoningEffort effort, string field)
    {
        var config = await ThinkingConfigAsync(TestData.Binding(), Effort(effort));

        config.EnumerateObject().Select(static property => property.Name).ShouldBe([field]);
    }

    [Fact]
    public async Task The_reasoning_effort_maps_to_the_level_of_its_name_and_the_two_highest_share_the_high_level()
    {
        var levels = new Dictionary<ReasoningEffort, string?>();

        foreach (var effort in new[] { ReasoningEffort.Low, ReasoningEffort.Medium, ReasoningEffort.High, ReasoningEffort.ExtraHigh })
        {
            levels[effort] = (await ThinkingConfigAsync(TestData.Binding(), Effort(effort))).GetProperty("thinkingLevel").GetString();
        }

        levels.ShouldBe(new Dictionary<ReasoningEffort, string?>
        {
            [ReasoningEffort.Low] = "LOW",
            [ReasoningEffort.Medium] = "MEDIUM",
            [ReasoningEffort.High] = "HIGH",
            [ReasoningEffort.ExtraHigh] = "HIGH",
        });

        (await ThinkingConfigAsync(TestData.Binding(), Effort(ReasoningEffort.None))).GetProperty("thinkingBudget").GetInt32().ShouldBe(0);
    }

    [Theory]
    [InlineData(ReasoningEffort.None)]
    [InlineData(ReasoningEffort.Low)]
    [InlineData(ReasoningEffort.High)]
    [InlineData(ReasoningEffort.ExtraHigh)]
    public async Task The_thinking_level_setting_wins_over_the_reasoning_effort(ReasoningEffort effort)
    {
        var config = await ThinkingConfigAsync(Level("medium"), Effort(effort));

        config.GetProperty("thinkingLevel").GetString().ShouldBe("MEDIUM");
        config.TryGetProperty("thinkingBudget", out _).ShouldBeFalse("a request that carries a level carries no budget");
    }

    [Theory]
    [InlineData(ReasoningEffort.None)]
    [InlineData(ReasoningEffort.Low)]
    [InlineData(ReasoningEffort.High)]
    public async Task The_thinking_budget_setting_wins_over_the_reasoning_effort(ReasoningEffort effort)
    {
        var config = await ThinkingConfigAsync(Budget(512), Effort(effort));

        config.GetProperty("thinkingBudget").GetInt32().ShouldBe(512);
        config.TryGetProperty("thinkingLevel", out _).ShouldBeFalse("a request that carries a budget carries no level");
    }

    [Fact]
    public async Task A_budget_of_zero_from_the_setting_is_not_replaced_by_the_level_of_the_effort()
    {
        var config = await ThinkingConfigAsync(Budget(0), Effort(ReasoningEffort.High));

        config.GetProperty("thinkingBudget").GetInt32().ShouldBe(0);
        config.TryGetProperty("thinkingLevel", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task The_include_thoughts_setting_does_not_stand_in_for_the_depth_so_the_effort_still_reaches_the_wire()
    {
        var config = await ThinkingConfigAsync(
            TestData.Binding(providerSettings: TestData.Settings((GoogleProviderNames.ThinkingIncludeThoughtsSetting, true))),
            Effort(ReasoningEffort.Low));

        config.GetProperty("includeThoughts").GetBoolean().ShouldBeTrue();
        config.GetProperty("thinkingLevel").GetString().ShouldBe("LOW");
    }

    [Fact]
    public async Task The_level_and_the_include_thoughts_settings_travel_together()
    {
        var config = await ThinkingConfigAsync(TestData.Binding(providerSettings: TestData.Settings(
            (GoogleProviderNames.ThinkingLevelSetting, "high"),
            (GoogleProviderNames.ThinkingIncludeThoughtsSetting, true))));

        config.GetProperty("thinkingLevel").GetString().ShouldBe("HIGH");
        config.GetProperty("includeThoughts").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_options_of_the_caller_keep_their_reasoning_effort_after_the_call()
    {
        // A compiled agent shares one options instance across every call; a call that cleared its effort would change the next.
        await using var server = StubHttpServer.Respond(200, "OK", Candidate);
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(Level("low"));
        var options = Effort(ReasoningEffort.High);

        await client.GetResponseAsync("hi", options, TestContext.Current.CancellationToken);

        options.Reasoning.ShouldNotBeNull().Effort.ShouldBe(ReasoningEffort.High);
    }

    [Fact]
    public async Task A_caller_that_brings_its_own_raw_request_keeps_its_effort_and_settings_do_not_reach_it()
    {
        await using var server = StubHttpServer.Respond(200, "OK", Candidate);
        using var factory = new GoogleChatClientFactory(TestData.Options(options => options.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(Level("low"));
        var options = Effort(ReasoningEffort.High);

        options.RawRepresentationFactory = static _ => new global::Google.GenAI.Types.GenerateContentConfig();

        await client.GetResponseAsync("hi", options, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(await server.RequestBody);

        body.RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig").GetProperty("thinkingLevel").GetString()
            .ShouldBe("HIGH", "the consumer took over the request, so the portable effort is mapped as it always was");
    }

    private static ModelBinding Level(string value)
        => TestData.Binding(providerSettings: TestData.Settings((GoogleProviderNames.ThinkingLevelSetting, value)));

    private static ModelBinding Budget(int value)
        => TestData.Binding(providerSettings: TestData.Settings((GoogleProviderNames.ThinkingBudgetTokensSetting, value)));

    private static ChatOptions Effort(ReasoningEffort effort)
        => new() { Reasoning = new ReasoningOptions { Effort = effort } };

    /// <summary>Sends one call through the real adapter and returns the <c>thinkingConfig</c> of the body it wrote.</summary>
    private static async Task<JsonElement> ThinkingConfigAsync(ModelBinding binding, ChatOptions? options = null)
    {
        await using var server = StubHttpServer.Respond(200, "OK", Candidate);
        using var factory = new GoogleChatClientFactory(TestData.Options(o => o.Endpoint = server.BaseAddress));
        using var client = factory.CreateChatClient(binding);

        await client.GetResponseAsync("hi", options, TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(await server.RequestBody);

        return body.RootElement.GetProperty("generationConfig").GetProperty("thinkingConfig").Clone();
    }
}

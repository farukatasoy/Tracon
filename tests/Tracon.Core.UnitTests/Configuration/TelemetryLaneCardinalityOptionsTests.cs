using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Core.UnitTests.Diagnostics;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The limit on telemetry identities reaches both lane pools from configuration, is checked when the host starts, and
/// means zero when it says zero.
/// </summary>
public sealed class TelemetryLaneCardinalityOptionsTests
{
    private const string Key = "Tracon:Observability:MaxTelemetryLaneCardinality";

    private static Dictionary<string, string?> Config(string value)
        => new(StringComparer.Ordinal) { [Key] = value };

    [Fact]
    public void The_default_is_the_budget_the_measurement_chose()
    {
        new TraconOptions().Observability.MaxTelemetryLaneCardinality.ShouldBe(1024);
        TraconObservabilityOptions.DefaultMaxTelemetryLaneCardinality.ShouldBe(1024);
    }

    [Fact]
    public void The_limit_binds_from_configuration()
        => SectionBinding.Bind<TraconOptions>(Config("250")).Observability.MaxTelemetryLaneCardinality.ShouldBe(250);

    [Fact]
    public void Text_that_is_not_a_number_fails_at_startup_and_names_the_key()
    {
        var failure = SectionBinding.FailsAtStartup(Config("many"));

        failure.Message.ShouldContain(Key);
        failure.Message.ShouldContain("'many'");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1000001")]
    public void A_negative_or_absurd_value_fails_at_startup(string value)
    {
        var failure = SectionBinding.FailsAtStartup(Config(value));

        failure.Message.ShouldContain(nameof(TraconObservabilityOptions));
        failure.Message.ShouldContain(nameof(TraconObservabilityOptions.MaxTelemetryLaneCardinality));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("1000000")]
    public void The_edges_of_the_range_are_accepted(string value)
        => SectionBinding.StartsCleanly(Config(value));

    [Fact]
    public async Task The_pool_of_the_model_calls_and_the_pool_of_the_agents_both_read_the_limit_from_the_container()
    {
        using var provider = SectionBinding.Build(Config("2"), compose: null);

        var chat = provider.GetRequiredService<ChatTelemetryLanes>();

        for (var endpoint = 0; endpoint < 6; endpoint++)
        {
            chat.Wrap(new ScriptedPipeline("answer", new ChatClientMetadata("limit-test", new Uri($"https://host-{endpoint}.example.test/"), "m")));
        }

        chat.AdmittedIdentities.ShouldBe(2);
        chat.OverflowedCalls.ShouldBe(4);

        var decorator = provider.GetServices<IAgentDecorator>().OfType<OpenTelemetryAgentDecorator>().Single();
        var descriptor = new AgentDescriptor { Name = "alpha", Origin = AgentDefinitionOrigin.Code, SourceName = "test" };

        for (var endpoint = 0; endpoint < 6; endpoint++)
        {
            var agent = new ChatClientAgent(
                new ScriptedPipeline("answer", new ChatClientMetadata("limit-test", new Uri($"https://host-{endpoint}.example.test/"), "m")),
                new ChatClientAgentOptions { Name = "agent-" + endpoint });

            await decorator.Decorate(agent, descriptor).RunAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        }

        decorator.LaneCount.ShouldBe(3, "two identities the limit allows and the lane of the overflow");
    }

    [Fact]
    public void A_limit_of_zero_keys_no_endpoint_in_either_pool()
    {
        using var provider = SectionBinding.Build(Config("0"), compose: null);

        var chat = provider.GetRequiredService<ChatTelemetryLanes>();

        chat.Wrap(new ScriptedPipeline("answer", new ChatClientMetadata("limit-test", new Uri("https://host.example.test/"), "m")));

        chat.AdmittedIdentities.ShouldBe(0);
        chat.OverflowedCalls.ShouldBe(1);
    }
}

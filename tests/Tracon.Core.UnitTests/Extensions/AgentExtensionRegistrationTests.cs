using Microsoft.Extensions.DependencyInjection;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Extensions;

/// <summary>
/// What <c>AddAgentExtension</c> accepts, what the registry refuses, and that listing the
/// names runs no extension code.
/// </summary>
public sealed class AgentExtensionRegistrationTests
{
    [Fact]
    public void Two_extensions_with_one_name_are_refused_with_the_name_in_the_message()
    {
        var exception = Should.Throw<TraconException>(() => new AgentExtensionRegistry(
            [new DelegateAgentExtension("audit", static _ => { }, null), new DelegateAgentExtension("audit", static _ => { }, "again")]));

        exception.Message.ShouldContain("'audit'");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Audit")]
    [InlineData("-audit")]
    [InlineData("has space")]
    [InlineData("tab\there")]
    public void A_name_outside_the_pattern_is_refused(string name)
    {
        Should.Throw<TraconException>(() => new AgentExtensionRegistry([new DelegateAgentExtension(name, static _ => { }, null)]));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("a")]
    [InlineData("context.policy_v2-eu")]
    [InlineData("0lead")]
    public void A_name_inside_the_pattern_is_accepted(string name)
    {
        var registry = new AgentExtensionRegistry([new DelegateAgentExtension(name, static _ => { }, null)]);

        registry.Extensions.ShouldHaveSingleItem().Name.ShouldBe(name);
    }

    [Fact]
    public void The_list_is_ordered_by_name_and_runs_no_extension()
    {
        var calls = 0;
        var registry = new AgentExtensionRegistry(
        [
            new DelegateAgentExtension("zeta", _ => calls++, "Last."),
            new DelegateAgentExtension("alpha", _ => calls++, null),
            new DelegateAgentExtension("mid", _ => calls++, "Middle."),
        ]);

        registry.Extensions.Select(static extension => extension.Name).ShouldBe(["alpha", "mid", "zeta"]);
        registry.Extensions.Select(static extension => extension.Description).ShouldBe([null, "Middle.", "Last."]);
        calls.ShouldBe(0);
    }

    [Fact]
    public void An_unknown_name_and_a_repeated_name_are_each_reported_with_the_path_of_their_list_item()
    {
        var registry = new AgentExtensionRegistry([new DelegateAgentExtension("known", static _ => { }, null)]);

        var problems = registry.FindProblems(["known", "missing", "known"]);

        problems.Select(static problem => (problem.Code, problem.Path)).ShouldBe(
        [
            ("unknown_extension", "extensionNames[1]"),
            ("duplicate_extension", "extensionNames[2]"),
        ]);
        registry.FindProblems([]).ShouldBeEmpty();
        registry.FindProblems(["known"]).ShouldBeEmpty();
    }

    [Fact]
    public void The_empty_registry_lists_nothing_and_refuses_every_name()
    {
        AgentExtensionRegistry.Empty.Extensions.ShouldBeEmpty();
        AgentExtensionRegistry.Empty.FindProblems(["anything"]).ShouldHaveSingleItem().Code.ShouldBe("unknown_extension");
    }

    [Fact]
    public void Registering_by_type_twice_adds_one_extension()
    {
        var services = new ServiceCollection();
        var builder = services.AddTracon();

        builder.AddAgentExtension<ProbeExtension>();
        builder.AddAgentExtension<ProbeExtension>();

        services.Count(static descriptor => descriptor.ServiceType == typeof(IAgentExtension)).ShouldBe(1);
    }

    [Fact]
    public void The_builder_rejects_an_empty_name_and_a_missing_callback()
    {
        var builder = new ServiceCollection().AddTracon();

        Should.Throw<ArgumentException>(() => builder.AddAgentExtension(" ", static _ => { }));
        Should.Throw<ArgumentNullException>(() => builder.AddAgentExtension("x", null!));
        Should.Throw<ArgumentNullException>(() => builder.AddAgentExtension((IAgentExtension)null!));
    }

    [Fact]
    public void A_definition_without_extension_names_never_touches_the_registry_set()
    {
        var calls = 0;
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            agentExtensions: [new DelegateAgentExtension("probe", _ => calls++, null)]);

        _ = compiler.Compile(TestData.Definition());

        calls.ShouldBe(0);
    }

    private sealed class ProbeExtension : IAgentExtension
    {
        public string Name => "probe-type";

        public string? Description => null;

        public void Configure(AgentExtensionContext context)
        {
        }
    }
}

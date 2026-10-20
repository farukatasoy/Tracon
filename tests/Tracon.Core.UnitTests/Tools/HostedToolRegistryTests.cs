using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// F-603 — what the registry of hosted tools accepts, what it refuses, and what it builds for a
/// definition. The registry is the one rule set the compiler and the validator share.
/// </summary>
public sealed class HostedToolRegistryTests
{
    private static HostedToolContext Context(string tenant = "acme", string provider = "fake")
        => new() { TenantId = tenant, Provider = provider, Services = new ServiceProviderStub() };

    private static HostedToolRegistration Search(string name = "web") => new(name, HostedToolKind.WebSearch, _ => new HostedWebSearchTool(), null);

    private static HostedToolRegistration Interpreter(string name = "py") => new(name, HostedToolKind.CodeInterpreter, _ => new HostedCodeInterpreterTool(), null);

    private static HostedToolRegistration Files(string name = "docs", string store = "vs_1")
        => new(name, HostedToolKind.FileSearch, _ => new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent(store)] }, "Searches the docs.");

    [Fact]
    public void Two_registrations_with_one_name_are_refused()
    {
        var exception = Should.Throw<TraconException>(() => new HostedToolRegistry([Search("same"), Interpreter("same")]));

        exception.Message.ShouldContain("'same'");
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("é")]
    public void A_name_the_tool_naming_rules_refuse_is_refused(string name)
    {
        Should.Throw<TraconException>(() => new HostedToolRegistry([Search(name)]));
    }

    [Fact]
    public void A_kind_that_is_not_defined_is_refused()
    {
        var registration = new HostedToolRegistration("odd", (HostedToolKind)99, _ => new HostedWebSearchTool(), null);

        Should.Throw<TraconException>(() => new HostedToolRegistry([registration])).Message.ShouldContain("'odd'");
    }

    [Fact]
    public void Listing_describes_each_tool_by_kind_and_runs_no_factory()
    {
        var calls = 0;
        var registry = new HostedToolRegistry(
        [
            new HostedToolRegistration("zeta", HostedToolKind.WebSearch, _ => { calls++; return new HostedWebSearchTool(); }, null),
            Files("alpha"),
        ]);

        var descriptors = registry.Descriptors;

        descriptors.Select(static descriptor => descriptor.Name).ShouldBe(["alpha", "zeta"]);
        descriptors[0].HostedKind.ShouldBe(HostedToolKind.FileSearch);
        descriptors[0].Description.ShouldBe("Searches the docs.");
        descriptors[0].JsonSchema.ShouldBeNull();
        descriptors[0].RunsOnClient.ShouldBeFalse();
        descriptors[0].RequiresApproval.ShouldBeFalse();
        descriptors[0].Effect.ShouldBe(ToolEffect.External);
        calls.ShouldBe(0, "listing is data; a factory runs only when an agent that selects the tool is compiled");
    }

    [Fact]
    public void Two_tools_of_one_kind_are_a_problem_at_the_second_one()
    {
        var registry = new HostedToolRegistry([Search("web"), Search("news")]);
        var definition = TestData.Definition(toolNames: ["web", "news"]);

        var problem = registry.FindProblems(definition).ShouldHaveSingleItem();

        problem.Code.ShouldBe("duplicate_hosted_tool_kind");
        problem.Path.ShouldBe("toolNames[1]");
        problem.Message.ShouldContain("'news'");
        problem.Message.ShouldContain("'web'");
    }

    [Fact]
    public void Tools_of_different_kinds_are_not_a_problem()
    {
        var registry = new HostedToolRegistry([Search(), Interpreter(), Files()]);

        registry.FindProblems(TestData.Definition(toolNames: ["web", "py", "docs"])).ShouldBeEmpty();
    }

    [Fact]
    public void A_web_search_beside_the_harness_web_search_is_a_conflict()
    {
        var registry = new HostedToolRegistry([Search()]);
        var definition = TestData.Definition(toolNames: ["web"], harness: new HarnessSettings { EnableWebSearch = true });

        var problem = registry.FindProblems(definition).ShouldHaveSingleItem();

        problem.Code.ShouldBe("hosted_tool_conflict");
        problem.Path.ShouldBe("toolNames[0]");
    }

    [Fact]
    public void A_harness_web_search_beside_another_kind_is_no_conflict()
    {
        var registry = new HostedToolRegistry([Interpreter()]);
        var definition = TestData.Definition(toolNames: ["py"], harness: new HarnessSettings { EnableWebSearch = true });

        registry.FindProblems(definition).ShouldBeEmpty();
    }

    [Fact]
    public void A_tool_mode_that_requires_a_hosted_tool_is_a_problem()
    {
        var registry = new HostedToolRegistry([Search()]);
        var definition = TestData.Definition(toolNames: ["web"]) with
        {
            Model = TestData.Binding() with { ToolMode = "required:web" },
        };

        var problem = registry.FindProblems(definition).ShouldHaveSingleItem();

        problem.Code.ShouldBe("hosted_tool_not_callable");
        problem.Path.ShouldBe("model.toolMode");
    }

    [Fact]
    public void A_definition_that_selects_no_hosted_tool_has_no_problem_and_no_fingerprint()
    {
        var registry = new HostedToolRegistry([Search()]);
        var definition = TestData.Definition(toolNames: ["lookup"]);

        registry.FindProblems(definition).ShouldBeEmpty();
        registry.Fingerprint(definition, Context()).Text.ShouldBeEmpty();
    }

    [Fact]
    public void The_selection_keeps_the_order_and_the_index_of_the_tool_names()
    {
        var registry = new HostedToolRegistry([Search(), Files()]);
        var definition = TestData.Definition(toolNames: ["lookup", "docs", "other", "web"]);

        registry.Selected(definition).Select(static item => (item.Index, item.Registration.Name)).ShouldBe([(1, "docs"), (3, "web")]);
    }

    [Fact]
    public void The_factory_receives_the_tenant_the_provider_and_the_services()
    {
        HostedToolContext? seen = null;
        var registration = new HostedToolRegistration("web", HostedToolKind.WebSearch, context => { seen = context; return new HostedWebSearchTool(); }, null);
        var context = Context("tenant-7", "openai-responses");

        HostedToolRegistry.Create(registration, context, "agent");

        seen.ShouldBeSameAs(context);
    }

    [Theory]
    [InlineData(HostedToolKind.WebSearch, typeof(HostedCodeInterpreterTool))]
    [InlineData(HostedToolKind.CodeInterpreter, typeof(HostedFileSearchTool))]
    [InlineData(HostedToolKind.FileSearch, typeof(HostedWebSearchTool))]
    public void A_factory_that_returns_another_kinds_tool_does_not_compile(HostedToolKind kind, Type returned)
    {
        var registration = new HostedToolRegistration("odd", kind, _ => (AITool)Activator.CreateInstance(returned)!, null);

        var exception = Should.Throw<TraconCompilationException>(() => HostedToolRegistry.Create(registration, Context(), "agent"));

        exception.AgentName.ShouldBe("agent");
        exception.Message.ShouldContain("'odd'");
        exception.Message.ShouldContain(returned.Name);
    }

    [Fact]
    public void A_factory_that_returns_nothing_does_not_compile()
    {
        var registration = new HostedToolRegistration("odd", HostedToolKind.WebSearch, _ => null!, null);

        Should.Throw<TraconCompilationException>(() => HostedToolRegistry.Create(registration, Context(), "agent"))
            .Message.ShouldContain("returned nothing");
    }

    [Fact]
    public void A_factory_that_throws_does_not_compile_and_names_the_tool_and_the_agent()
    {
        var registration = new HostedToolRegistration("odd", HostedToolKind.WebSearch, _ => throw new InvalidOperationException("secret detail"), null);

        var exception = Should.Throw<TraconCompilationException>(() => HostedToolRegistry.Create(registration, Context(), "agent"));

        exception.AgentName.ShouldBe("agent");
        exception.Message.ShouldContain("'odd'");
        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void An_instance_another_tool_returned_is_refused()
    {
        var shared = new HostedWebSearchTool();
        var first = new HostedToolRegistration("first", HostedToolKind.WebSearch, _ => shared, null);
        var second = new HostedToolRegistration("second", HostedToolKind.WebSearch, _ => shared, null);

        HostedToolRegistry.Create(first, Context(), "agent").ShouldBeSameAs(shared);

        Should.Throw<TraconCompilationException>(() => HostedToolRegistry.Create(second, Context(), "agent"))
            .Message.ShouldContain("another hosted tool returned");

        // The same registration may hand out one shared instance every time.
        HostedToolRegistry.Create(first, Context(), "agent").ShouldBeSameAs(shared);
    }

    [Fact]
    public void The_registered_name_is_found_from_the_options_the_model_call_is_given()
    {
        var registry = new HostedToolRegistry([Search("web"), Files("docs")]);
        var tools = registry.Selected(TestData.Definition(toolNames: ["web", "docs"]))
            .Select(item => HostedToolRegistry.Create(item.Registration, Context(), "agent"))
            .ToList();
        var options = new ChatOptions { Tools = [.. tools, TestData.Tool("lookup")] };

        HostedToolIdentity.FindName(options, HostedToolKind.WebSearch).ShouldBe("web");
        HostedToolIdentity.FindName(options, HostedToolKind.FileSearch).ShouldBe("docs");
        HostedToolIdentity.FindName(options, HostedToolKind.CodeInterpreter).ShouldBeNull();
        HostedToolIdentity.FindName(null, HostedToolKind.WebSearch).ShouldBeNull();
    }

    [Fact]
    public void A_tool_the_registry_did_not_build_has_no_registered_name()
    {
        // The harness adds its own web search; its calls are not a registered tool's.
        var options = new ChatOptions { Tools = [new HostedWebSearchTool()] };

        HostedToolIdentity.FindName(options, HostedToolKind.WebSearch).ShouldBeNull();
    }

    [Fact]
    public void The_fingerprint_changes_when_what_the_factory_builds_changes()
    {
        var store = "vs_1";
        var registry = new HostedToolRegistry(
        [
            new HostedToolRegistration(
                "docs",
                HostedToolKind.FileSearch,
                _ => new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent(store)], MaximumResultCount = 5 },
                null),
        ]);
        var definition = TestData.Definition(toolNames: ["docs"]);

        var before = registry.Fingerprint(definition, Context());
        var again = registry.Fingerprint(definition, Context());
        store = "vs_2";
        var after = registry.Fingerprint(definition, Context());

        before.Text.ShouldNotBeEmpty();
        string.Equals(again.Text, before.Text, StringComparison.Ordinal).ShouldBeTrue("the same tool hashes the same");
        string.Equals(after.Text, before.Text, StringComparison.Ordinal).ShouldBeFalse("a tenant that replaces its vector store must reach the next resolve");
    }

    [Fact]
    public void The_fingerprint_reads_the_tenant_the_agent_is_compiled_for()
    {
        var registry = new HostedToolRegistry(
        [
            new HostedToolRegistration(
                "docs",
                HostedToolKind.FileSearch,
                context => new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent($"vs_{context.TenantId}")] },
                null),
        ]);
        var definition = TestData.Definition(toolNames: ["docs"]);

        string.Equals(registry.Fingerprint(definition, Context("a")).Text, registry.Fingerprint(definition, Context("b")).Text, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void A_property_a_provider_sdk_object_carries_makes_the_fingerprint_opaque()
    {
        // The SDK object has no ToString of its own, so its text is its type name: two different filters would hash alike.
        var registry = new HostedToolRegistry(
        [
            new HostedToolRegistration(
                "web",
                HostedToolKind.WebSearch,
                _ => new HostedWebSearchTool(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Filters"] = new SdkFilters() }),
                null),
        ]);

        var fingerprint = registry.Fingerprint(TestData.Definition(toolNames: ["web"]), Context());

        fingerprint.Text.ShouldNotBeEmpty();
        fingerprint.Opaque.ShouldBeTrue();
    }

    [Fact]
    public void A_tool_made_of_plain_values_is_not_opaque_and_each_value_reaches_the_fingerprint()
    {
        var domains = new[] { "a.example" };
        var registry = new HostedToolRegistry(
        [
            new HostedToolRegistration(
                "web",
                HostedToolKind.WebSearch,
                _ => new HostedWebSearchTool(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["SearchContextSize"] = "low",
                    ["Domains"] = domains,
                    ["Limits"] = new Dictionary<string, int>(StringComparer.Ordinal) { ["results"] = 3 },
                }),
                null),
        ]);
        var definition = TestData.Definition(toolNames: ["web"]);

        var before = registry.Fingerprint(definition, Context());
        domains[0] = "b.example";
        var after = registry.Fingerprint(definition, Context());

        before.Opaque.ShouldBeFalse();
        string.Equals(before.Text, after.Text, StringComparison.Ordinal).ShouldBeFalse("a narrowed domain list must reach the next resolve");
    }

    [Fact]
    public void The_bytes_of_a_code_interpreter_input_reach_the_description()
    {
        byte[] first = [1, 2, 3];
        byte[] second = [1, 2, 4];

        var one = HostedToolDescription.Of(new HostedCodeInterpreterTool { Inputs = [new DataContent(first, "text/csv")] });
        var other = HostedToolDescription.Of(new HostedCodeInterpreterTool { Inputs = [new DataContent(second, "text/csv")] });

        one.Opaque.ShouldBeFalse();
        string.Equals(one.Text, other.Text, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void Two_first_compiles_of_one_shared_instance_do_not_race()
    {
        var shared = new HostedWebSearchTool();
        var registration = new HostedToolRegistration("web", HostedToolKind.WebSearch, _ => shared, null);
        var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 64, _ =>
        {
            try
            {
                HostedToolRegistry.Create(registration, Context(), "agent");
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        });

        failures.ShouldBeEmpty();
    }

    private sealed class SdkFilters;

    private sealed class ServiceProviderStub : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}

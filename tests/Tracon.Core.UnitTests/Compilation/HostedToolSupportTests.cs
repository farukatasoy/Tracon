using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// F-603 — a hosted tool the provider's API surface would drop stops the compilation and makes the
/// validation report name the tool and the provider (<see cref="IHostedToolSupport"/>); a tool the
/// provider runs reaches the model call.
/// </summary>
/// <remarks>
/// The sibling of <see cref="SamplingSupportTests"/>: one check, read by the compiler and the validator,
/// covering the fallback links. The table of which provider surface runs which kind is pinned against the
/// wire in the functional tests (<c>HostedToolWireTests</c>); this class pins the rule that reads it.
/// </remarks>
public sealed class HostedToolSupportTests
{
    private static readonly HostedToolRegistration[] Registrations =
    [
        new("web", HostedToolKind.WebSearch, _ => new HostedWebSearchTool(), "Searches the web."),
        new("py", HostedToolKind.CodeInterpreter, _ => new HostedCodeInterpreterTool(), null),
        new("docs", HostedToolKind.FileSearch, _ => new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent("vs_1")] }, null),
    ];

    public static TheoryData<string, HostedToolKinds> Kinds() => new()
    {
        { "web", HostedToolKinds.WebSearch },
        { "py", HostedToolKinds.CodeInterpreter },
        { "docs", HostedToolKinds.FileSearch },
    };

    /// <param name="tool">The registered name the definition selects.</param>
    /// <param name="flag">The kind's flag in a provider's set.</param>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_kind_the_provider_does_not_run_does_not_compile(string tool, HostedToolKinds flag)
    {
        var all = FakeModelProvider.AllHostedTools;
        var compiler = Compiler(new FakeModelProvider(supportedHostedTools: all & ~flag));

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition([tool])));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain($"'{tool}'", Case.Sensitive);
        exception.Message.ShouldContain("'fake' provider");
    }

    /// <param name="tool">The registered name the definition selects.</param>
    /// <param name="flag">The kind's flag in a provider's set.</param>
    [Theory]
    [MemberData(nameof(Kinds))]
    public void A_kind_the_provider_runs_compiles(string tool, HostedToolKinds flag)
    {
        var compiler = Compiler(new FakeModelProvider(supportedHostedTools: flag));

        compiler.Compile(Definition([tool])).ShouldNotBeNull();
    }

    [Fact]
    public void A_provider_that_states_nothing_runs_none_of_the_kinds()
    {
        var compiler = Compiler(new UndeclaringProvider());

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition(["web"], UndeclaringProvider.ProviderName)));

        exception.Message.ShouldContain("'undeclaring' provider");
        exception.Message.ShouldContain("No registered provider runs it");
    }

    [Fact]
    public void A_definition_that_selects_no_hosted_tool_compiles_whatever_the_provider_runs()
    {
        var compiler = Compiler(new FakeModelProvider(supportedHostedTools: HostedToolKinds.None));

        compiler.Compile(TestData.Definition()).ShouldNotBeNull();
    }

    [Fact]
    public void The_message_names_the_providers_that_run_the_kind()
    {
        var compiler = Compiler(
            new FakeModelProvider(supportedHostedTools: HostedToolKinds.None),
            new FakeModelProvider(name: "capable", supportedHostedTools: HostedToolKinds.CodeInterpreter));

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition(["py"])));

        exception.Message.ShouldContain("Providers that run it: capable");
    }

    [Fact]
    public void A_fallback_link_that_does_not_run_a_kind_does_not_compile()
    {
        // FallbackChatClient sends a link the same ChatOptions, tools included.
        var compiler = Compiler(
            new FakeModelProvider(),
            new FakeModelProvider(name: "backup", supportedHostedTools: HostedToolKinds.WebSearch));

        var definition = Definition(["py"]) with
        {
            Model = TestData.Binding() with { Fallbacks = [new ModelFallback { Provider = "backup", Model = "fake-model" }] },
        };

        var exception = Should.Throw<TraconCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("'backup' provider");
        exception.Message.ShouldNotContain("'fake' provider");
    }

    [Fact]
    public async Task The_validation_report_names_each_tool_with_its_path_and_provider()
    {
        var validator = Validator(new FakeModelProvider(supportedHostedTools: HostedToolKinds.WebSearch));

        var report = await validator.ValidateAsync(Definition(["web", "py", "docs"]), TestContext.Current.CancellationToken);

        report.Valid.ShouldBeFalse();
        report.Messages.Select(static message => (message.Code, message.Path, message.Severity)).ShouldBe(
        [
            ("hosted_tool_unsupported", "toolNames[1]", ValidationSeverity.Error),
            ("hosted_tool_unsupported", "toolNames[2]", ValidationSeverity.Error),
        ]);
        report.Messages.ShouldAllBe(static message => message.Message.Contains("'fake' provider", StringComparison.Ordinal));
        report.Messages.ShouldNotContain(static message => message.Code == "unknown_tool");
        report.Messages.ShouldNotContain(static message => message.Code == "compilation_error");
    }

    [Fact]
    public async Task The_validation_report_is_clean_when_the_provider_runs_every_kind_selected()
    {
        var validator = Validator(new FakeModelProvider());

        var report = await validator.ValidateAsync(Definition(["web", "py", "docs"]), TestContext.Current.CancellationToken);

        report.Valid.ShouldBeTrue();
        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_validation_report_checks_a_fallback_link()
    {
        var validator = Validator(
            new FakeModelProvider(),
            new FakeModelProvider(name: "backup", supportedHostedTools: HostedToolKinds.None));

        var report = await validator.ValidateAsync(
            Definition(["docs"]) with
            {
                Model = TestData.Binding() with { Fallbacks = [new ModelFallback { Provider = "backup", Model = "fake-model" }] },
            },
            TestContext.Current.CancellationToken);

        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("hosted_tool_unsupported");
        message.Path.ShouldBe("toolNames[0]");
        message.Message.ShouldContain("'backup' provider");
    }

    [Fact]
    public async Task A_name_no_registry_knows_is_still_an_unknown_tool()
    {
        var validator = Validator(new FakeModelProvider());

        var report = await validator.ValidateAsync(Definition(["web", "nowhere"]), TestContext.Current.CancellationToken);

        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("unknown_tool");
        message.Path.ShouldBe("toolNames[1]");
    }

    [Fact]
    public async Task The_validation_report_carries_the_selection_rules_with_their_paths()
    {
        var validator = Validator(new FakeModelProvider());
        var definition = Definition(["web", "news"]) with
        {
            Harness = new HarnessSettings(),
            Model = TestData.Binding() with { ToolMode = "required:py" },
        };

        var report = await validator.ValidateAsync(definition, TestContext.Current.CancellationToken);

        report.Messages.Select(static message => (message.Code, message.Path)).ShouldBe(
        [
            ("duplicate_hosted_tool_kind", "toolNames[1]"),
            ("hosted_tool_not_callable", "model.toolMode"),
        ]);
    }

    [Fact]
    public void The_registry_hands_the_provider_statement_to_the_descriptor()
    {
        var registry = TestData.Providers(
            new FakeModelProvider(supportedHostedTools: HostedToolKinds.WebSearch | HostedToolKinds.FileSearch),
            new UndeclaringProvider());

        var descriptors = registry.List().ToDictionary(static descriptor => descriptor.Name, StringComparer.Ordinal);

        descriptors["fake"].SupportedHostedTools.ShouldBe(HostedToolKinds.WebSearch | HostedToolKinds.FileSearch);
        descriptors[UndeclaringProvider.ProviderName].SupportedHostedTools.ShouldBe(HostedToolKinds.None);
    }

    [Fact]
    public async Task The_hosted_tool_reaches_the_model_call_beside_the_function_tool()
    {
        var client = new FakeChatClient();
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry(TestData.Tool("lookup")),
            hostedTools: new HostedToolRegistry(Registrations));

        var agent = compiler.Compile(Definition(["web", "lookup", "docs"]));
        await agent.RunAsync("hi", cancellationToken: TestContext.Current.CancellationToken);

        var tools = client.LastOptions.ShouldNotBeNull().Tools.ShouldNotBeNull();

        tools.Count.ShouldBe(3);
        tools[0].ShouldBeOfType<HostedWebSearchTool>();
        tools[1].Name.ShouldBe("lookup");
        tools[2].ShouldBeOfType<HostedFileSearchTool>();
    }

    [Fact]
    public void The_factory_builds_the_tool_for_the_tenant_and_the_provider_of_the_agent()
    {
        HostedToolContext? seen = null;
        var client = new FakeChatClient();
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry(),
            tenantContext: new FixedTenantContext("tenant-9"),
            hostedTools: new HostedToolRegistry(
            [
                new HostedToolRegistration(
                    "web",
                    HostedToolKind.WebSearch,
                    context =>
                    {
                        seen = context;

                        return new HostedWebSearchTool();
                    },
                    null),
            ]));

        compiler.Compile(Definition(["web"])).ShouldNotBeNull();

        seen.ShouldNotBeNull().TenantId.ShouldBe("tenant-9");
        seen.Provider.ShouldBe("fake");
    }

    [Fact]
    public async Task The_cache_key_follows_what_the_factory_builds()
    {
        var store = "vs_1";
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            hostedTools: new HostedToolRegistry(
            [
                new HostedToolRegistration(
                    "docs",
                    HostedToolKind.FileSearch,
                    _ => new HostedFileSearchTool { Inputs = [new HostedVectorStoreContent(store)] },
                    null),
            ]));
        var definition = Definition(["docs"]);

        var before = await compiler.ResolveDependenciesAsync(definition, TestContext.Current.CancellationToken);
        before.BypassCache.ShouldBeFalse();
        store = "vs_2";
        var after = await compiler.ResolveDependenciesAsync(definition, TestContext.Current.CancellationToken);
        var plain = await compiler.ResolveDependenciesAsync(TestData.Definition(), TestContext.Current.CancellationToken);

        before.CacheFingerprint.ShouldNotBeNullOrEmpty();
        string.Equals(before.CacheFingerprint, after.CacheFingerprint, StringComparison.Ordinal)
            .ShouldBeFalse("a tenant that replaces its vector store must reach the next resolve");
        plain.CacheFingerprint.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agent_whose_tool_holds_a_value_the_fingerprint_cannot_compare_is_never_served_from_the_cache()
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            hostedTools: new HostedToolRegistry(
            [
                new HostedToolRegistration(
                    "web",
                    HostedToolKind.WebSearch,
                    _ => new HostedWebSearchTool(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Filters"] = new object() }),
                    null),
            ]));

        var dependencies = await compiler.ResolveDependenciesAsync(Definition(["web"]), TestContext.Current.CancellationToken);

        dependencies.BypassCache.ShouldBeTrue();
    }

    private static AgentDefinition Definition(IReadOnlyList<string> toolNames, string provider = "fake")
        => TestData.Definition(toolNames: toolNames) with { Model = TestData.Binding(provider) };

    private static AgentDefinitionCompiler Compiler(params IModelProvider[] providers)
        => new(TestData.Providers(providers), TestData.Registry(TestData.Tool("lookup")), hostedTools: new HostedToolRegistry(Registrations));

    private static AgentDefinitionValidator Validator(params IModelProvider[] providers)
    {
        var models = TestData.Providers(providers);
        var tools = TestData.Registry(TestData.Tool("lookup"));
        var catalog = new FakeAgentCatalog([]);
        var options = Options.Create(new TraconOptions());
        var skills = new AgentSkillCatalog([], new InMemoryAgentSkillStore(), new SingleTenantContext(options), options);
        var compiler = new AgentDefinitionCompiler(
            models,
            tools,
            skills: skills,
            callableAgents: new CallableAgentResolver(catalog),
            hostedTools: new HostedToolRegistry([.. Registrations, new HostedToolRegistration("news", HostedToolKind.WebSearch, _ => new HostedWebSearchTool(), null)]));

        return new AgentDefinitionValidator(models, tools, skills, catalog, compiler, options);
    }

    /// <summary>A provider written against <see cref="IModelProvider"/> alone, as a third party's would be.</summary>
    private sealed class UndeclaringProvider : IModelProvider
    {
        public const string ProviderName = "undeclaring";

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "m" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new FakeChatClient();
    }
}

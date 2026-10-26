using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// Core part of the F-60 definition validation endpoint: a check that repeats
/// the real compilation path, registers nothing, and calls no model.
/// </summary>
public sealed class AgentDefinitionValidatorTests
{
    [Fact]
    public async Task Valid_definition_produces_no_message()
    {
        var (validator, _, _) = CreateValidator();

        var report = await validator.ValidateAsync(TestData.Definition());

        report.Valid.ShouldBeTrue();
        report.Inconclusive.ShouldBeFalse();
        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unknown_provider_produces_unknown_model()
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition() with { Model = TestData.Binding(provider: "no-such") };
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("unknown_model");
        message.Severity.ShouldBe(ValidationSeverity.Error);
    }

    [Fact]
    public async Task Unknown_tool_produces_unknown_tool()
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition(toolNames: ["no-such"]);
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("unknown_tool");
        message.Path.ShouldBe("toolNames[0]");
    }

    [Fact]
    public async Task Unknown_skill_produces_unknown_skill()
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition() with { SkillNames = ["no-such"] };
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("unknown_skill");
        message.Path.ShouldBe("skillNames[0]");
    }

    [Fact]
    public async Task Skill_settings_on_an_agent_that_names_no_skill_produce_a_warning_that_does_not_stop_the_definition()
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition() with { Skills = new SkillSettings { DisableReadApproval = true } };
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeTrue("a warning never stops the definition");
        var message = report.Messages.ShouldHaveSingleItem();
        message.Severity.ShouldBe(ValidationSeverity.Warning);
        message.Code.ShouldBe(AgentDefinitionValidator.SkillSettingsWithoutSkillsCode);
        message.Path.ShouldBe("skills");
    }

    [Fact]
    public async Task Skill_settings_on_an_agent_that_names_a_skill_produce_no_such_warning()
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition() with
        {
            SkillNames = ["no-such"],
            Skills = new SkillSettings { DisableReadApproval = true },
        };
        var report = await validator.ValidateAsync(definition);

        report.Messages.Select(static message => message.Code).ShouldBe(["unknown_skill"]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Workspace_settings_on_an_agent_whose_workspace_is_off_produce_a_warning_that_does_not_stop_the_definition(bool readOnly, bool disableReadApproval)
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition() with
        {
            Workspace = new WorkspaceSettings { Enabled = false, ReadOnly = readOnly, DisableReadApproval = disableReadApproval },
        };
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeTrue("a warning never stops the definition");
        var message = report.Messages.ShouldHaveSingleItem();
        message.Severity.ShouldBe(ValidationSeverity.Warning);
        message.Code.ShouldBe(AgentDefinitionValidator.WorkspaceSettingsWithoutWorkspaceCode);
        message.Path.ShouldBe("workspace");
    }

    [Fact]
    public async Task A_workspace_that_is_off_with_no_other_setting_is_the_same_as_no_workspace_and_says_nothing()
    {
        var (validator, _, _) = CreateValidator();

        var report = await validator.ValidateAsync(TestData.Definition() with { Workspace = new WorkspaceSettings() });

        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_workspace_that_is_on_says_nothing_about_its_settings()
    {
        var (validator, _, _) = CreateValidator();

        var report = await validator.ValidateAsync(TestData.Definition() with
        {
            Workspace = new WorkspaceSettings { Enabled = true, ReadOnly = true, DisableReadApproval = true },
        });

        report.Messages.Where(static message => message.Code.StartsWith("workspace", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_workspace_on_an_agent_whose_name_holds_a_slash_is_an_error_that_names_the_field()
    {
        var (validator, _, _) = CreateValidator();

        var report = await validator.ValidateAsync(TestData.Definition() with
        {
            Name = "team/drafter",
            Workspace = new WorkspaceSettings { Enabled = true },
        });

        report.Valid.ShouldBeFalse();
        var message = report.Messages.Single(static message => string.Equals(message.Code, AgentDefinitionValidator.WorkspaceAgentNameCode, StringComparison.Ordinal));
        message.Severity.ShouldBe(ValidationSeverity.Error);
        message.Path.ShouldBe("name");
    }

    [Fact]
    public async Task Self_referencing_definition_produces_cycle()
    {
        var descriptors = new[]
        {
            new AgentDescriptor
            {
                Name = "test-agent",
                Origin = AgentDefinitionOrigin.Database,
                SourceName = "database",
            },
        };
        var (validator, _, _) = CreateValidator(descriptors: descriptors);

        var definition = TestData.Definition() with { CallableAgents = ["test-agent"] };
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("cycle");
    }

    [Fact]
    public async Task A_pinned_callable_agent_whose_version_exists_is_valid()
    {
        var store = new InMemoryAgentDefinitionStore(new FixedTenantContext("default"));
        await store.SaveAsync(TestData.Definition("support-agent"));

        var (validator, _, _) = CreateValidator(
            descriptors: [new AgentDescriptor { Name = "support-agent", Origin = AgentDefinitionOrigin.Database, SourceName = "database", Version = 1 }],
            definitions: store);

        var definition = TestData.Definition() with
        {
            CallableAgents = [new CallableAgentReference { Name = "support-agent", Version = 1 }],
        };

        (await validator.ValidateAsync(definition)).Valid.ShouldBeTrue();
    }

    [Fact]
    public async Task A_pin_to_a_version_the_callable_agent_does_not_have_is_reported_at_the_entry()
    {
        var store = new InMemoryAgentDefinitionStore(new FixedTenantContext("default"));
        await store.SaveAsync(TestData.Definition("support-agent"));

        var (validator, _, _) = CreateValidator(
            descriptors: [new AgentDescriptor { Name = "support-agent", Origin = AgentDefinitionOrigin.Database, SourceName = "database", Version = 1 }],
            definitions: store);

        var definition = TestData.Definition() with
        {
            CallableAgents = [new CallableAgentReference { Name = "support-agent", Version = 4 }],
        };
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();

        var message = report.Messages.ShouldHaveSingleItem();

        message.Code.ShouldBe("callable_agent_version_not_found");
        message.Severity.ShouldBe(ValidationSeverity.Error);
        message.Path.ShouldBe("callableAgents[0].version");
    }

    [Fact]
    public async Task A_pin_on_a_callable_agent_defined_in_code_and_a_repeated_name_are_each_reported()
    {
        var (validator, _, _) = CreateValidator(
            descriptors:
            [
                new AgentDescriptor { Name = "coded-agent", Origin = AgentDefinitionOrigin.Code, SourceName = "code" },
                new AgentDescriptor { Name = "other-agent", Origin = AgentDefinitionOrigin.Database, SourceName = "database" },
            ]);

        var definition = TestData.Definition() with
        {
            CallableAgents =
            [
                new CallableAgentReference { Name = "coded-agent", Version = 1 },
                "other-agent",
                "other-agent",
            ],
        };
        var report = await validator.ValidateAsync(definition);

        report.Messages.Select(static message => (message.Code, message.Path)).ShouldBe(
            [("callable_agent_version_invalid", "callableAgents[0].version"), ("callable_agent_duplicate", "callableAgents[2].name")]);
    }

    [Fact]
    public async Task Tool_mode_callable_agent_whose_tool_name_is_taken_produces_a_conflict_naming_both()
    {
        var descriptors = new[]
        {
            new AgentDescriptor { Name = "support-agent", Origin = AgentDefinitionOrigin.Database, SourceName = "database" },
        };
        var (validator, _, _) = CreateValidator(
            registry: TestData.Registry(TestData.Tool("support_agent")),
            descriptors: descriptors);

        var definition = TestData.Definition(toolNames: ["support_agent"]) with
        {
            CallableAgents = ["support-agent"],
            SubAgents = new SubAgentSettings { CallMode = SubAgentCallMode.Tool },
        };
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("sub_agent_tool_name_conflict");
        message.Severity.ShouldBe(ValidationSeverity.Error);
        message.Path.ShouldBe("callableAgents[0].name");
        message.Message.ShouldContain("'support-agent'");
        message.Message.ShouldContain("'support_agent'");
    }

    [Fact]
    public async Task The_same_names_report_no_tool_name_conflict_in_background_mode()
    {
        var descriptors = new[]
        {
            new AgentDescriptor { Name = "support-agent", Origin = AgentDefinitionOrigin.Database, SourceName = "database" },
        };
        var (validator, _, _) = CreateValidator(
            registry: TestData.Registry(TestData.Tool("support_agent")),
            descriptors: descriptors);

        var definition = TestData.Definition(toolNames: ["support_agent"]) with { CallableAgents = ["support-agent"] };
        var report = await validator.ValidateAsync(definition);

        // Only the conflict is asserted: this validator's compiler has no tenant context, so the
        // structural step that follows reports the missing sub-agent resolver for any callable agent.
        report.Messages.Any(static message => string.Equals(message.Code, "sub_agent_tool_name_conflict", StringComparison.Ordinal))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Three_separate_errors_return_three_messages_does_not_stop_at_first()
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition(toolNames: ["no-such-tool"]) with
        {
            Model = TestData.Binding(provider: "no-such-provider"),
            SkillNames = ["no-such-skill"],
        };

        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        report.Messages.Count.ShouldBe(3);
        report.Messages.Select(static message => message.Code)
            .ShouldBe(["unknown_model", "unknown_tool", "unknown_skill"], ignoreOrder: true);
    }

    [Fact]
    public async Task Invalid_provider_setting_produces_invalid_setting()
    {
        var (validator, _, _) = CreateValidator(providers: [new ThrowingModelProvider()]);

        var report = await validator.ValidateAsync(TestData.Definition());

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("invalid_setting");
    }

    [Fact]
    public async Task Unreachable_mcp_server_produces_inconclusive_does_not_drop_valid()
    {
        var refresher = new FakeMcpToolRefresher(hang: true);
        var (validator, _, _) = CreateValidator(
            mcpRefresher: refresher,
            mcpTimeout: TimeSpan.FromMilliseconds(30));

        var report = await validator.ValidateAsync(TestData.Definition(toolNames: ["mcp-tool"]));

        // An unreachable MCP server is not the same as a wrong tool name:
        // Valid is not dropped, only Inconclusive is set.
        report.Valid.ShouldBeTrue();
        report.Inconclusive.ShouldBeTrue();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("mcp_unreachable");
        message.Severity.ShouldBe(ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Actively_refused_unreachable_MCP_server_also_produces_inconclusive()
    {
        // HATA-006 / MT-CORE-006: "connection refused" does NOT time out —
        // McpToolCatalog.RefreshAsync does NOT throw an exception for this, it only
        // returns HadUnreachableServers=true. This used to escape the catch blocks
        // in TryRefreshMcpAsync that only catch exceptions, and was silently
        // counted as "success", so the missing tool fell through to unknown_tool.
        var refresher = new FakeMcpToolRefresher(unreachable: true);
        var (validator, _, _) = CreateValidator(mcpRefresher: refresher);

        var report = await validator.ValidateAsync(TestData.Definition(toolNames: ["mcp-tool"]));

        report.Valid.ShouldBeTrue();
        report.Inconclusive.ShouldBeTrue();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("mcp_unreachable");
        message.Severity.ShouldBe(ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Mcp_refresh_resolving_missing_tool_produces_no_error()
    {
        var registry = new MutableToolRegistry();
        var refresher = new FakeMcpToolRefresher(onRefresh: () => registry.Add(TestData.Tool("mcp-tool")));
        var (validator, _, _) = CreateValidator(registry: registry, mcpRefresher: refresher);

        var report = await validator.ValidateAsync(TestData.Definition(toolNames: ["mcp-tool"]));

        report.Valid.ShouldBeTrue();
        report.Inconclusive.ShouldBeFalse();
        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Validation_calls_no_model_and_does_not_change_catalog()
    {
        var client = new FakeChatClient();
        var descriptors = new[]
        {
            new AgentDescriptor { Name = "other", Origin = AgentDefinitionOrigin.Database, SourceName = "database" },
        };
        var (validator, catalog, _) = CreateValidator(providers: [new FakeModelProvider(client)], descriptors: descriptors);

        var before = await catalog.ListAsync();

        await validator.ValidateAsync(TestData.Definition());

        var after = await catalog.ListAsync();

        client.CallCount.ShouldBe(0);
        after.Count.ShouldBe(before.Count);
    }

    private static (AgentDefinitionValidator Validator, FakeAgentCatalog Catalog, AgentDefinitionCompiler Compiler) CreateValidator(
        IModelProvider[]? providers = null,
        IToolRegistry? registry = null,
        AgentDescriptor[]? descriptors = null,
        IMcpToolRefresher? mcpRefresher = null,
        TimeSpan? mcpTimeout = null,
        IAgentDefinitionStore? definitions = null)
    {
        var models = TestData.Providers(providers ?? [new FakeModelProvider()]);
        var tools = registry ?? TestData.Registry();
        var catalog = new FakeAgentCatalog(descriptors ?? []);
        var options = Options.Create(new TraconOptions
        {
            Validation = new TraconValidationOptions { McpTimeout = mcpTimeout ?? TimeSpan.FromSeconds(5) },
        });
        var tenantContext = new SingleTenantContext(options);
        var skills = new AgentSkillCatalog([], new InMemoryAgentSkillStore(), tenantContext, options);
        var compiler = new AgentDefinitionCompiler(models, tools, skills: skills, callableAgents: new CallableAgentResolver(catalog), tenantContext: tenantContext);

        var validator = new AgentDefinitionValidator(models, tools, skills, catalog, compiler, options, mcpRefresher, definitions);

        return (validator, catalog, compiler);
    }

    /// <summary>Fake provider that throws <see cref="TraconException"/> on every call.</summary>
    private sealed class ThrowingModelProvider : IModelProvider
    {
        public string Name => "fake";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "fake-model" }];

        public IChatClient CreateChatClient(ModelBinding binding)
            => throw new TraconException(
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.ProviderSettings)} contains unrecognized keys: bogus.");
    }

    /// <summary>
    /// Registry that allows adding tools at run time. The real <see cref="ToolRegistry"/>
    /// is fixed at compile time; this fake simulates an MCP tool cache that is
    /// refreshed in the background.
    /// </summary>
    private sealed class MutableToolRegistry : IToolRegistry
    {
        private readonly Dictionary<string, AIFunctionDeclaration> _tools = new(StringComparer.Ordinal);

        public void Add(AIFunction tool) => _tools[tool.Name] = tool;

        public IReadOnlyList<ToolDescriptor> List()
            => [.. _tools.Values.Select(static tool => new ToolDescriptor { Name = tool.Name })];

        public bool TryGet(string name, [NotNullWhen(true)] out AIFunctionDeclaration? tool) => _tools.TryGetValue(name, out tool);
    }

    /// <summary>Fake refresher that controls fresh MCP scan requests.</summary>
    private sealed class FakeMcpToolRefresher(bool hang = false, bool unreachable = false, Action? onRefresh = null) : IMcpToolRefresher
    {
        public async ValueTask<McpRefreshOutcome> RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (hang)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            }

            onRefresh?.Invoke();

            return new McpRefreshOutcome { ToolCount = 1, HadUnreachableServers = unreachable };
        }
    }
}

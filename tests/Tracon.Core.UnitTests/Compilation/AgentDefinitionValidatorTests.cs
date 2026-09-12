using System.Diagnostics.CodeAnalysis;
using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

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

        var definition = TestData.Definition() with { CallableAgentNames = ["test-agent"] };
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("cycle");
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
        TimeSpan? mcpTimeout = null)
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
        var compiler = new AgentDefinitionCompiler(models, tools, skills: skills, callableAgents: new CallableAgentResolver(catalog));

        var validator = new AgentDefinitionValidator(models, tools, skills, catalog, compiler, options, mcpRefresher);

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
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }

            onRefresh?.Invoke();

            return new McpRefreshOutcome { ToolCount = 1, HadUnreachableServers = unreachable };
        }
    }
}

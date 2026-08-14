using System.Diagnostics.CodeAnalysis;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Compilation;

/// <summary>
/// F-60 tanim dogrulama ucunun Core parcasi: gercek derleme yolunu tekrar eden,
/// hicbir sey kaydetmeyen ve hicbir model cagirmayan denetim.
/// </summary>
public sealed class AgentDefinitionValidatorTests
{
    [Fact]
    public async Task Gecerli_tanim_hicbir_mesaj_uretmez()
    {
        var (validator, _, _) = CreateValidator();

        var report = await validator.ValidateAsync(TestData.Definition());

        report.Valid.ShouldBeTrue();
        report.Inconclusive.ShouldBeFalse();
        report.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bilinmeyen_saglayici_unknown_model_uretir()
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition() with { Model = TestData.Binding(provider: "yok-boyle") };
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("unknown_model");
        message.Severity.ShouldBe(ValidationSeverity.Error);
    }

    [Fact]
    public async Task Bilinmeyen_tool_unknown_tool_uretir()
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition(toolNames: ["yok-boyle"]);
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("unknown_tool");
        message.Path.ShouldBe("toolNames[0]");
    }

    [Fact]
    public async Task Bilinmeyen_skill_unknown_skill_uretir()
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition() with { SkillNames = ["yok-boyle"] };
        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("unknown_skill");
        message.Path.ShouldBe("skillNames[0]");
    }

    [Fact]
    public async Task Kendini_cagiran_tanim_cycle_uretir()
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
    public async Task Uc_ayri_hata_uc_mesaj_doner_ilkinde_durmaz()
    {
        var (validator, _, _) = CreateValidator();

        var definition = TestData.Definition(toolNames: ["yok-tool"]) with
        {
            Model = TestData.Binding(provider: "yok-saglayici"),
            SkillNames = ["yok-skill"],
        };

        var report = await validator.ValidateAsync(definition);

        report.Valid.ShouldBeFalse();
        report.Messages.Count.ShouldBe(3);
        report.Messages.Select(static message => message.Code)
            .ShouldBe(["unknown_model", "unknown_tool", "unknown_skill"], ignoreOrder: true);
    }

    [Fact]
    public async Task Gecersiz_saglayici_ayari_invalid_setting_uretir()
    {
        var (validator, _, _) = CreateValidator(providers: [new ThrowingModelProvider()]);

        var report = await validator.ValidateAsync(TestData.Definition());

        report.Valid.ShouldBeFalse();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("invalid_setting");
    }

    [Fact]
    public async Task Ulasilamayan_mcp_sunucusu_inconclusive_uretir_valid_dusurmez()
    {
        var refresher = new FakeMcpToolRefresher(hang: true);
        var (validator, _, _) = CreateValidator(
            mcpRefresher: refresher,
            mcpTimeout: TimeSpan.FromMilliseconds(30));

        var report = await validator.ValidateAsync(TestData.Definition(toolNames: ["mcp-tool"]));

        // MCP sunucusuna ulasilamadi ile tool adi yanlis ayni sey degildir:
        // Valid dusurulmez, yalniz Inconclusive isaretlenir.
        report.Valid.ShouldBeTrue();
        report.Inconclusive.ShouldBeTrue();
        var message = report.Messages.ShouldHaveSingleItem();
        message.Code.ShouldBe("mcp_unreachable");
        message.Severity.ShouldBe(ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Aktif_red_ile_erisilemeyen_MCP_sunucusu_da_inconclusive_uretir()
    {
        // HATA-006 / MT-CORE-006: "connection refused" ZAMAN ASIMINA ugramaz —
        // McpToolCatalog.RefreshAsync bunun icin bir istisna FIRLATMAZ, yalniz
        // HadUnreachableServers=true doner. Eskiden bu, TryRefreshMcpAsync'in
        // yalniz istisna yakalayan catch bloklarindan kacip sessizce
        // "basarili" sayiliyor ve eksik tool unknown_tool'a duşuyordu.
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
    public async Task Mcp_tazeleme_eksik_tool_u_cozerse_hata_uretilmez()
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
    public async Task Dogrulama_hicbir_model_cagirmaz_ve_katalogu_degistirmez()
    {
        var client = new FakeChatClient();
        var descriptors = new[]
        {
            new AgentDescriptor { Name = "diger", Origin = AgentDefinitionOrigin.Database, SourceName = "database" },
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
        var options = Options.Create(new AgentPrismOptions
        {
            Validation = new AgentPrismValidationOptions { McpTimeout = mcpTimeout ?? TimeSpan.FromSeconds(5) },
        });
        var tenantContext = new SingleTenantContext(options);
        var skills = new AgentSkillCatalog([], new InMemoryAgentSkillStore(), tenantContext, options);
        var compiler = new AgentDefinitionCompiler(models, tools, skills: skills, callableAgents: new CallableAgentResolver(catalog));

        var validator = new AgentDefinitionValidator(models, tools, skills, catalog, compiler, options, mcpRefresher);

        return (validator, catalog, compiler);
    }

    /// <summary>Her cagride <see cref="AgentPrismException"/> firlatan sahte saglayici.</summary>
    private sealed class ThrowingModelProvider : IModelProvider
    {
        public string Name => "fake";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "fake-model" }];

        public IChatClient CreateChatClient(ModelBinding binding)
            => throw new AgentPrismException(
                $"{nameof(ModelBinding)}.{nameof(ModelBinding.ProviderSettings)} icinde su anahtarlar taninmiyor: bogus.");
    }

    /// <summary>
    /// Calisma aninda tool eklenebilen defter. Gercek <see cref="ToolRegistry"/>
    /// derlemede sabitlenir; bu sahte, arka planda tazelenen bir MCP tool
    /// onbellegini taklit eder.
    /// </summary>
    private sealed class MutableToolRegistry : IToolRegistry
    {
        private readonly Dictionary<string, AIFunction> _tools = new(StringComparer.Ordinal);

        public void Add(AIFunction tool) => _tools[tool.Name] = tool;

        public IReadOnlyList<ToolDescriptor> List()
            => [.. _tools.Values.Select(static tool => new ToolDescriptor { Name = tool.Name })];

        public bool TryGet(string name, [NotNullWhen(true)] out AIFunction? tool) => _tools.TryGetValue(name, out tool);
    }

    /// <summary>Taze MCP tarama isteklerini denetleyen sahte tazeleyici.</summary>
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

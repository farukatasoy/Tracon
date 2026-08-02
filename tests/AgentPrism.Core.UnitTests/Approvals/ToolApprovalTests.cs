using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Approvals;

/// <summary>
/// Tool onayi: defterin sarmalamasi ve kalici kurallarin uygulanmasi.
/// </summary>
public sealed class ToolApprovalTests
{
    [Fact]
    public void Onay_isteyen_tool_defterde_sarmalanir()
    {
        // Sarmalama BURADA yapilmalidir: defter, "bir agent yalnizca kayitli bir
        // tool'a isaret edebilir" kuralinin zorlandigi tek yerdir. Baska bir kod
        // yolunun sarmalamayi atlamasi mumkun olmamalidir.
        var registry = new ToolRegistry(
        [
            new AgentPrismToolRegistration(Function("safe_tool")),
            new AgentPrismToolRegistration(Function("dangerous_tool"), requiresApproval: true),
        ]);

        registry.TryGet("safe_tool", out var safe).ShouldBeTrue();
        safe.ShouldNotBeOfType<ApprovalRequiredAIFunction>();

        registry.TryGet("dangerous_tool", out var dangerous).ShouldBeTrue();
        dangerous.ShouldBeOfType<ApprovalRequiredAIFunction>();
    }

    [Fact]
    public void Sarmalama_adi_ve_aciklamayi_korur()
    {
        // ApprovalRequiredAIFunction bir DelegatingAIFunction'dir; sarmalama
        // modelin gordugu sozlesmeyi degistirmemelidir.
        var registry = new ToolRegistry(
        [
            new AgentPrismToolRegistration(Function("dangerous_tool"), requiresApproval: true),
        ]);

        registry.TryGet("dangerous_tool", out var tool).ShouldBeTrue();

        tool.Name.ShouldBe("dangerous_tool");

        var descriptor = registry.List().ShouldHaveSingleItem();

        descriptor.RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public void Kaynak_bilgisi_tanima_tasinir()
    {
        var registry = new ToolRegistry(
        [
            new AgentPrismToolRegistration(Function("remote_tool"), requiresApproval: true, source: "github"),
        ]);

        var descriptor = registry.List().ShouldHaveSingleItem();

        string.Equals(descriptor.Source, "github", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Kural_yoksa_otomatik_onay_verilmez()
    {
        var evaluator = CreateEvaluator(new InMemoryToolApprovalRuleStore());

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Arguman_kapsamsiz_kural_her_cagriyi_onaylar()
    {
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("cancel_order", agentName: "support", argumentsHash: null));

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order", ("orderId", "A"))))
            .ShouldBeTrue();
        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order", ("orderId", "B"))))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Arguman_kapsamli_kural_yalnizca_ayni_argumanla_eslesir()
    {
        var store = new InMemoryToolApprovalRuleStore();
        var call = Call("cancel_order", ("orderId", "ORD-1"));

        await store.AddAsync(Rule(
            "cancel_order",
            agentName: "support",
            argumentsHash: ToolApprovalRuleEvaluator.ComputeArgumentsHash(call.Arguments)));

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", call)).ShouldBeTrue();
        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order", ("orderId", "ORD-2"))))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Baska_agentin_kurali_gecmez()
    {
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("cancel_order", agentName: "baska-agent", argumentsHash: null));

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Agent_kapsamsiz_kural_tum_agentlari_kapsar()
    {
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("cancel_order", agentName: null, argumentsHash: null));

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("herhangi-bir-agent", Call("cancel_order"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Baska_kiracinin_kurali_gecmez()
    {
        // Kiraci siniri: bir kiracinin verdigi onay baska bir kiracinin
        // cagrisini calistiramaz.
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("cancel_order", agentName: null, argumentsHash: null) with
        {
            TenantId = "baska-kiraci",
        });

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Depo_hatasi_onay_vermez()
    {
        // Guvenli taraf: kural okunamiyorsa cagri kullaniciya sorulur.
        var evaluator = CreateEvaluator(new ThrowingRuleStore());

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order"))).ShouldBeFalse();
    }

    [Fact]
    public void Arguman_parmak_izi_anahtar_sirasindan_bagimsizdir()
    {
        // Sozluk sirasi calistirmalar arasinda degisebilir; degisirse
        // "bir daha sorma" kurali hicbir zaman eslesmezdi.
        var first = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["b"] = 2, ["a"] = 1 });

        var second = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 });

        string.Equals(first, second, StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public void Farkli_argumanlar_farkli_parmak_izi_uretir()
    {
        var first = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "2" });

        // Ayrac olmasaydi "a=1" + "b=2" ile "a=1b=" + "2" ayni izi uretebilirdi.
        var second = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "1b=2" });

        string.Equals(first, second, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task Ayni_kapsam_ikinci_kez_eklenmez()
    {
        var store = new InMemoryToolApprovalRuleStore();

        var first = await store.AddAsync(Rule("cancel_order", "support", null));
        var second = await store.AddAsync(Rule("cancel_order", "support", null));

        second.Id.ShouldBe(first.Id);
        (await store.ListAsync("default")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Baska_kiracinin_kurali_silinemez()
    {
        var store = new InMemoryToolApprovalRuleStore();
        var rule = await store.AddAsync(Rule("cancel_order", "support", null));

        (await store.DeleteAsync("baska-kiraci", rule.Id)).ShouldBeFalse();
        (await store.DeleteAsync("default", rule.Id)).ShouldBeTrue();
    }

    private static ToolApprovalRuleEvaluator CreateEvaluator(IToolApprovalRuleStore store)
        => new(store, new FixedTenantContext(), NullLogger<ToolApprovalRuleEvaluator>.Instance);

    private static ToolApprovalRule Rule(string toolName, string? agentName, string? argumentsHash)
        => new()
        {
            Id = AgentPrismId.NewId(),
            TenantId = "default",
            AgentName = agentName,
            ToolName = toolName,
            ArgumentsHash = argumentsHash,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static FunctionCallContent Call(string name, params (string Key, object? Value)[] arguments)
        => new(
            Guid.NewGuid().ToString("N"),
            name,
            arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    private static AIFunction Function(string name)
        => AIFunctionFactory.Create(() => "ok", name, $"{name} aciklamasi");

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "default";
    }

    private sealed class ThrowingRuleStore : IToolApprovalRuleStore
    {
        public ValueTask<IReadOnlyList<ToolApprovalRule>> ListAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<ToolApprovalRule> AddAsync(
            ToolApprovalRule rule,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<bool> DeleteAsync(
            string tenantId,
            Guid ruleId,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");
    }
}

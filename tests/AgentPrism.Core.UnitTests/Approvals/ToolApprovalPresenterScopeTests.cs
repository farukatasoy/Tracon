using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Approvals;

/// <summary>
/// <see cref="IToolApprovalPresenter"/> registered as a SINGLETON that resolves a
/// SCOPED dependency through <c>IServiceScopeFactory</c> — the exact shape the
/// interface's own <c>&lt;example&gt;</c> documents, driven through a REAL DI
/// container rather than a hand-built fake (docs/142, section 142.1, K-218).
/// </summary>
/// <remarks>
/// An isolated probe does not prove this: K-218's own original case had a separate
/// console probe where the same call worked, while the real MAF pipeline handed the
/// tool an empty <c>IServiceProvider</c>. This class never touches MAF at all — it
/// exercises exactly what <see cref="ToolApprovalPresenterRunner"/> does, resolving
/// the presenter through ordinary constructor injection from a real
/// <see cref="ServiceProvider"/>, the same as <c>AddAgentPrism()</c> wires it.
/// </remarks>
public sealed class ToolApprovalPresenterScopeTests
{
    [Fact]
    public async Task Presenter_resolves_a_scoped_dependency_through_the_real_container()
    {
        var services = new ServiceCollection();

        services.AddScoped<FakeSkillRepository>();
        services.AddSingleton<IToolApprovalPresenter, SkillApprovalPresenter>();
        services.AddOptions<AgentPrismOptions>();

        await using var provider = services.BuildServiceProvider();

        var runner = new ToolApprovalPresenterRunner(
            provider.GetRequiredService<IToolApprovalPresenter>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismOptions>>(),
            NullLogger<ToolApprovalPresenterRunner>.Instance);

        var request = new ToolApprovalRequestContent(
            "req-1",
            new FunctionCallContent(
                "call-1",
                "delete_skill",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["skillId"] = FakeSkillRepository.KnownSkillId.ToString() }));

        var result = await runner.ResolveAllAsync([request], "tenant-a", "agent-a", CancellationToken.None);

        // A null here is exactly the K-218 failure mode: the scoped repository
        // silently returning nothing because the presenter never got a REAL
        // service provider. It must resolve the real skill name.
        result["req-1"].ShouldNotBeNull();
        result["req-1"]!.EntityName.ShouldBe("Refund Policy");
    }

    [Fact]
    public async Task Unknown_entity_resolves_to_null_without_throwing()
    {
        var services = new ServiceCollection();

        services.AddScoped<FakeSkillRepository>();
        services.AddSingleton<IToolApprovalPresenter, SkillApprovalPresenter>();
        services.AddOptions<AgentPrismOptions>();

        await using var provider = services.BuildServiceProvider();

        var runner = new ToolApprovalPresenterRunner(
            provider.GetRequiredService<IToolApprovalPresenter>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismOptions>>(),
            NullLogger<ToolApprovalPresenterRunner>.Instance);

        var request = new ToolApprovalRequestContent(
            "req-1",
            new FunctionCallContent(
                "call-1",
                "delete_skill",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["skillId"] = Guid.NewGuid().ToString() }));

        var result = await runner.ResolveAllAsync([request], "tenant-a", "agent-a", CancellationToken.None);

        result["req-1"].ShouldBeNull();
    }

    /// <summary>A fake scoped "database" — the shape a real <c>DbContext</c> registration takes.</summary>
    private sealed class FakeSkillRepository
    {
        public static readonly Guid KnownSkillId = Guid.Parse("8f14e45f-ceea-467e-adc9-15476f4f0f47");

        private readonly Dictionary<Guid, string> _skills = new() { [KnownSkillId] = "Refund Policy" };

        public string? FindName(Guid skillId) => _skills.GetValueOrDefault(skillId);
    }

    /// <summary>The exact shape <see cref="IToolApprovalPresenter"/>'s own <c>&lt;example&gt;</c> documents.</summary>
    private sealed class SkillApprovalPresenter(IServiceScopeFactory scopes) : IToolApprovalPresenter
    {
        public ValueTask<ToolApprovalPresentation?> PresentAsync(ToolApprovalContext context, CancellationToken cancellationToken = default)
        {
            if (context.GetString("skillId") is not { } skillIdText || !Guid.TryParse(skillIdText, out var skillId))
            {
                return new ValueTask<ToolApprovalPresentation?>((ToolApprovalPresentation?)null);
            }

            using var scope = scopes.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<FakeSkillRepository>();
            var name = repository.FindName(skillId);

            return new ValueTask<ToolApprovalPresentation?>(name is null
                ? null
                : new ToolApprovalPresentation { EntityType = "skill", EntityId = skillIdText, EntityName = name });
        }
    }
}

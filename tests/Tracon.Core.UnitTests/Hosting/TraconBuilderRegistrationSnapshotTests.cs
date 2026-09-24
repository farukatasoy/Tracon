using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Hosting;

/// <summary>
/// Pins what every <see cref="ITraconBuilder"/> registration method writes to
/// the service collection: the service type, the lifetime and the shape of
/// the descriptor (implementation type, instance or factory).
/// </summary>
/// <remarks>
/// Each method is called TWICE on a fresh chain, so the snapshot also pins
/// whether a repeated call deduplicates (<c>TryAdd*</c>) or appends
/// (<c>Add*</c>). Moving the methods to another type must keep every line
/// identical; a changed line is a behavior change, not a refactoring.
/// </remarks>
public sealed class TraconBuilderRegistrationSnapshotTests
{
    public static TheoryData<string, string> Cases() => new()
    {
        { "Configure", "IConfigureOptions`1|Singleton|instance:ConfigureNamedOptions`1 ; IConfigureOptions`1|Singleton|instance:ConfigureNamedOptions`1" },
        { "AddTool(AIFunction, configure)", "TraconToolRegistration|Singleton|instance:TraconToolRegistration ; TraconToolRegistration|Singleton|instance:TraconToolRegistration" },
        { "AddTool(AIFunction)", "TraconToolRegistration|Singleton|instance:TraconToolRegistration ; TraconToolRegistration|Singleton|instance:TraconToolRegistration" },
        { "AddScopedTool(AIFunction, configure)", "TraconToolRegistration|Singleton|factory ; TraconToolRegistration|Singleton|factory" },
        { "AddScopedTool(AIFunction)", "TraconToolRegistration|Singleton|factory ; TraconToolRegistration|Singleton|factory" },
        { "AddTool(Delegate)", "TraconToolRegistration|Singleton|instance:TraconToolRegistration ; TraconToolRegistration|Singleton|instance:TraconToolRegistration" },
        { "AddToolsFrom<T>", "TraconToolRegistration|Singleton|instance:TraconToolRegistration ; TraconToolRegistration|Singleton|instance:TraconToolRegistration" },
        { "AddToolsFrom(Type)", "TraconToolRegistration|Singleton|instance:TraconToolRegistration ; TraconToolRegistration|Singleton|instance:TraconToolRegistration" },
        { "AddAgent(definition)", "CodeAgentRegistration|Singleton|instance:CodeAgentRegistration ; CodeAgentRegistration|Singleton|instance:CodeAgentRegistration" },
        { "AddSkill", "CodeSkillRegistration|Singleton|instance:CodeSkillRegistration ; CodeSkillRegistration|Singleton|instance:CodeSkillRegistration" },
        { "AddAgent(name, factory)", "CodeAgentRegistration|Singleton|instance:CodeAgentRegistration ; CodeAgentRegistration|Singleton|instance:CodeAgentRegistration" },
        { "AddAgentSource<T>", "IAgentSource|Singleton|type:SnapshotSource" },
        { "AddAgentSource(instance)", "IAgentSource|Singleton|instance:SnapshotSource ; IAgentSource|Singleton|instance:SnapshotSource" },
        { "AddAgentSource(factory)", "IAgentSource|Singleton|factory ; IAgentSource|Singleton|factory" },
        { "AddAgentDecorator<T>", "IAgentDecorator|Singleton|type:SnapshotDecorator" },
        { "AddAgentDecorator(instance)", "IAgentDecorator|Singleton|instance:SnapshotDecorator ; IAgentDecorator|Singleton|instance:SnapshotDecorator" },
        { "AddAgentDecorator(factory)", "IAgentDecorator|Singleton|factory ; IAgentDecorator|Singleton|factory" },
        { "AddRunJudge<T>", "IRunJudge|Singleton|type:SnapshotJudge" },
        { "AddRunJudge(instance)", "IRunJudge|Singleton|instance:SnapshotJudge ; IRunJudge|Singleton|instance:SnapshotJudge" },
        { "AddRunJudge(factory)", "IRunJudge|Singleton|factory ; IRunJudge|Singleton|factory" },
        { "AddModelProvider<T>", "IModelProvider|Singleton|type:SnapshotProvider" },
        { "AddModelProvider(instance)", "IModelProvider|Singleton|instance:SnapshotProvider ; IModelProvider|Singleton|instance:SnapshotProvider" },
        { "AddModelProvider(factory)", "IModelProvider|Singleton|factory ; IModelProvider|Singleton|factory" },
        { "AddEvalCheck", "TraconEvalCheckRegistration|Singleton|instance:TraconEvalCheckRegistration ; TraconEvalCheckRegistration|Singleton|instance:TraconEvalCheckRegistration" },
        { "AddLoopEvaluator", "TraconLoopEvaluatorRegistration|Singleton|instance:TraconLoopEvaluatorRegistration ; TraconLoopEvaluatorRegistration|Singleton|instance:TraconLoopEvaluatorRegistration" },
        { "RequireCustomBinding<T>", "RequiredBindingRegistration|Singleton|instance:RequiredBindingRegistration ; RequiredBindingRegistration|Singleton|instance:RequiredBindingRegistration" },
        { "RequireProductionProfile", "ProductionProfileRegistration|Singleton|instance:ProductionProfileRegistration ; ProductionProfileRegistration|Singleton|instance:ProductionProfileRegistration" },
    };

    [Fact]
    public void Every_registration_method_has_a_case()
    {
        // A new registration method without a case here would change unobserved.
        var methods = typeof(TraconBuilderExtensions)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly);

        Cases().Count.ShouldBe(methods.Length);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Registration_method_rejects_a_null_chain(string method, string expected)
    {
        _ = expected;

        var exception = Should.Throw<ArgumentNullException>(() => Invoke(null!, method));

        exception.ParamName.ShouldBe("builder");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Registration_method_writes_the_pinned_descriptors(string method, string expected)
    {
        var services = new ServiceCollection();
        var builder = services.AddTracon();
        var before = services.Count;

        Invoke(builder, method);
        Invoke(builder, method);

        var actual = string.Join(" ; ", services.Skip(before).Select(Describe));

        actual.ShouldBe(expected, $"'{method}' changed what it registers.");
    }

    private static string Describe(ServiceDescriptor descriptor)
    {
        var shape = descriptor switch
        {
            { ImplementationInstance: { } instance } => "instance:" + instance.GetType().Name,
            { ImplementationType: { } type } => "type:" + type.Name,
            _ => "factory",
        };

        return $"{descriptor.ServiceType.Name}|{descriptor.Lifetime}|{shape}";
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test code; runs on the JIT.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test code; runs on the JIT.")]
    private static void Invoke(ITraconBuilder builder, string method)
    {
        var tool = AIFunctionFactory.Create(() => "ok", "snapshot_tool");

        _ = method switch
        {
            "Configure" => builder.Configure(static _ => { }),
            "AddTool(AIFunction, configure)" => builder.AddTool(tool, static o => o.RequiresApproval = true),
            "AddTool(AIFunction)" => builder.AddTool(tool),
            "AddScopedTool(AIFunction, configure)" => builder.AddScopedTool(tool, static o => o.RequiresApproval = true),
            "AddScopedTool(AIFunction)" => builder.AddScopedTool(tool),
            "AddTool(Delegate)" => builder.AddTool(() => "ok", "snapshot_delegate"),
            "AddToolsFrom<T>" => builder.AddToolsFrom<SnapshotTools>(),
            "AddToolsFrom(Type)" => builder.AddToolsFrom(typeof(SnapshotStaticTools)),
            "AddAgent(definition)" => builder.AddAgent(new AgentDefinition
            {
                Name = "snapshot",
                Instructions = "x",
                Model = new ModelBinding { Provider = "fake", Model = "fake" },
            }),
            "AddSkill" => builder.AddSkill(new AgentSkillDefinition
            {
                TenantId = "default",
                Name = "snapshot-skill",
                Description = "x",
                Instructions = "x",
            }),
            "AddAgent(name, factory)" => builder.AddAgent("snapshot", static _ => null!),
            "AddAgentSource<T>" => builder.AddAgentSource<SnapshotSource>(),
            "AddAgentSource(instance)" => builder.AddAgentSource(new SnapshotSource()),
            "AddAgentSource(factory)" => builder.AddAgentSource(static _ => new SnapshotSource()),
            "AddAgentDecorator<T>" => builder.AddAgentDecorator<SnapshotDecorator>(),
            "AddAgentDecorator(instance)" => builder.AddAgentDecorator(new SnapshotDecorator()),
            "AddAgentDecorator(factory)" => builder.AddAgentDecorator(static _ => new SnapshotDecorator()),
            "AddRunJudge<T>" => builder.AddRunJudge<SnapshotJudge>(),
            "AddRunJudge(instance)" => builder.AddRunJudge(new SnapshotJudge()),
            "AddRunJudge(factory)" => builder.AddRunJudge(static _ => new SnapshotJudge()),
            "AddModelProvider<T>" => builder.AddModelProvider<SnapshotProvider>(),
            "AddModelProvider(instance)" => builder.AddModelProvider(new SnapshotProvider()),
            "AddModelProvider(factory)" => builder.AddModelProvider(static _ => new SnapshotProvider()),
            "AddEvalCheck" => builder.AddEvalCheck("snapshotCheck", FunctionEvaluator.Create("snapshotCheck", static (string _) => true)),
#pragma warning disable MAAI001
            "AddLoopEvaluator" => builder.AddLoopEvaluator(
                "snapshotLoop",
                new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()))),
#pragma warning restore MAAI001
            "RequireCustomBinding<T>" => builder.RequireCustomBinding<ITenantContext>(),
            "RequireProductionProfile" => builder.RequireProductionProfile(),
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "No case for this method."),
        };
    }

    // The Type overload exists for static classes, which cannot be a type argument.
    private static class SnapshotStaticTools
    {
        [TraconTool("snapshot_static", "x")]
        public static string Scanned() => "ok";
    }

    private sealed class SnapshotTools
    {
        [TraconTool("snapshot_scanned", "x")]
        public static string Scanned() => "ok";
    }

    private sealed class SnapshotSource : IAgentSource
    {
        public string Name => "snapshot";

        public int Priority => 0;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<AgentDescriptor>>([]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<AIAgent?>(null);
    }

    private sealed class SnapshotDecorator : IAgentDecorator
    {
        public int Order => 0;

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor) => agent;
    }

    private sealed class SnapshotJudge : IRunJudge
    {
        public string Name => "snapshot";

        public ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The snapshot never runs a judge.");
    }

    private sealed class SnapshotProvider : IModelProvider
    {
        public string Name => "snapshot";

        public IReadOnlyList<ModelDescriptor> Models => [];

        public IChatClient CreateChatClient(ModelBinding binding) =>
            throw new InvalidOperationException($"The snapshot never creates a client for '{Name}'.");

        public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential credential) =>
            throw new InvalidOperationException($"The snapshot never creates a client for '{Name}'.");
    }
}

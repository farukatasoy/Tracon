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
        { "AddHostedTool", "HostedToolRegistration|Singleton|instance:HostedToolRegistration ; HostedToolRegistration|Singleton|instance:HostedToolRegistration" },
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
        { "AddAgentExtension<T>", "IAgentExtension|Singleton|type:SnapshotExtension" },
        { "AddAgentExtension(instance)", "IAgentExtension|Singleton|instance:SnapshotExtension ; IAgentExtension|Singleton|instance:SnapshotExtension" },
        { "AddAgentExtension(name, configure)", "IAgentExtension|Singleton|instance:DelegateAgentExtension ; IAgentExtension|Singleton|instance:DelegateAgentExtension" },
        { "AddRunJudge<T>", "IRunJudge|Singleton|type:SnapshotJudge" },
        { "AddRunJudge(instance)", "IRunJudge|Singleton|instance:SnapshotJudge ; IRunJudge|Singleton|instance:SnapshotJudge" },
        { "AddRunJudge(factory)", "IRunJudge|Singleton|factory ; IRunJudge|Singleton|factory" },
        { "AddModelProvider<T>", "IModelProvider|Singleton|type:SnapshotProvider" },
        { "AddModelProvider(instance)", "IModelProvider|Singleton|instance:SnapshotProvider ; IModelProvider|Singleton|instance:SnapshotProvider" },
        { "AddModelProvider(factory)", "IModelProvider|Singleton|factory ; IModelProvider|Singleton|factory" },
        { "AddEvalCheck", "TraconEvalCheckRegistration|Singleton|instance:TraconEvalCheckRegistration ; TraconEvalCheckRegistration|Singleton|instance:TraconEvalCheckRegistration" },
        { "AddLoopEvaluator(instance)", "TraconLoopEvaluatorRegistration|Singleton|instance:TraconLoopEvaluatorRegistration ; TraconLoopEvaluatorRegistration|Singleton|instance:TraconLoopEvaluatorRegistration" },
        { "AddLoopEvaluator(instance, description)", "TraconLoopEvaluatorRegistration|Singleton|instance:TraconLoopEvaluatorRegistration ; TraconLoopEvaluatorRegistration|Singleton|instance:TraconLoopEvaluatorRegistration" },
        // The factory overloads register the same INSTANCE descriptor: the factory
        // travels inside the registration and runs at the first compilation, not
        // when the container resolves the registration list.
        { "AddLoopEvaluator(factory)", "TraconLoopEvaluatorRegistration|Singleton|instance:TraconLoopEvaluatorRegistration ; TraconLoopEvaluatorRegistration|Singleton|instance:TraconLoopEvaluatorRegistration" },
        { "AddLoopEvaluator(factory, description)", "TraconLoopEvaluatorRegistration|Singleton|instance:TraconLoopEvaluatorRegistration ; TraconLoopEvaluatorRegistration|Singleton|instance:TraconLoopEvaluatorRegistration" },
        { "RequireCustomBinding<T>", "RequiredBindingRegistration|Singleton|instance:RequiredBindingRegistration ; RequiredBindingRegistration|Singleton|instance:RequiredBindingRegistration" },
        { "RequireProductionProfile", "ProductionProfileRegistration|Singleton|instance:ProductionProfileRegistration ; ProductionProfileRegistration|Singleton|instance:ProductionProfileRegistration" },
        // A named speech provider is a keyed factory plus the name record the resolver
        // lists; each call below uses a new name, because a repeated name throws.
        { "AddSpeechSynthesizer(factory)", "ISpeechSynthesizer|Singleton|keyed-factory ; SpeechProviderRegistration|Singleton|instance:SpeechProviderRegistration ; ISpeechSynthesizer|Singleton|keyed-factory ; SpeechProviderRegistration|Singleton|instance:SpeechProviderRegistration" },
        { "AddSpeechTranscriber(factory)", "ISpeechTranscriber|Singleton|keyed-factory ; SpeechProviderRegistration|Singleton|instance:SpeechProviderRegistration ; ISpeechTranscriber|Singleton|keyed-factory ; SpeechProviderRegistration|Singleton|instance:SpeechProviderRegistration" },
        // A bound Microsoft.Extensions.AI client adds the startup check: one record per bound name, and
        // once per host the validator plus the options type whose validation runs it at start.
        { "AddSpeechSynthesizer(binding)", "ISpeechSynthesizer|Singleton|keyed-factory ; SpeechProviderRegistration|Singleton|instance:SpeechProviderRegistration ; SpeechClientBridgeCheck|Singleton|instance:SpeechClientBridgeCheck ; IValidateOptions`1|Singleton|type:SpeechClientBridgeValidator ; IConfigureOptions`1|Transient|factory ; ISpeechSynthesizer|Singleton|keyed-factory ; SpeechProviderRegistration|Singleton|instance:SpeechProviderRegistration ; SpeechClientBridgeCheck|Singleton|instance:SpeechClientBridgeCheck" },
        { "AddSpeechTranscriber(binding)", "ISpeechTranscriber|Singleton|keyed-factory ; SpeechProviderRegistration|Singleton|instance:SpeechProviderRegistration ; SpeechClientBridgeCheck|Singleton|instance:SpeechClientBridgeCheck ; IValidateOptions`1|Singleton|type:SpeechClientBridgeValidator ; IConfigureOptions`1|Transient|factory ; ISpeechTranscriber|Singleton|keyed-factory ; SpeechProviderRegistration|Singleton|instance:SpeechProviderRegistration ; SpeechClientBridgeCheck|Singleton|instance:SpeechClientBridgeCheck" },
    };

    private static int _speechNames;

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
        // A keyed descriptor throws on the unkeyed accessors, so it is matched first.
        var shape = descriptor switch
        {
            { IsKeyedService: true, KeyedImplementationInstance: { } keyedInstance } => "keyed-instance:" + keyedInstance.GetType().Name,
            { IsKeyedService: true, KeyedImplementationType: { } keyedType } => "keyed-type:" + keyedType.Name,
            { IsKeyedService: true } => "keyed-factory",
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
            "AddHostedTool" => builder.AddHostedTool("snapshot_hosted", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool(), "Snapshot hosted tool."),
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
            "AddAgentExtension<T>" => builder.AddAgentExtension<SnapshotExtension>(),
            "AddAgentExtension(instance)" => builder.AddAgentExtension(new SnapshotExtension()),
            "AddAgentExtension(name, configure)" => builder.AddAgentExtension("snapshot-extension", static _ => { }, "Snapshot extension."),
            "AddRunJudge<T>" => builder.AddRunJudge<SnapshotJudge>(),
            "AddRunJudge(instance)" => builder.AddRunJudge(new SnapshotJudge()),
            "AddRunJudge(factory)" => builder.AddRunJudge(static _ => new SnapshotJudge()),
            "AddModelProvider<T>" => builder.AddModelProvider<SnapshotProvider>(),
            "AddModelProvider(instance)" => builder.AddModelProvider(new SnapshotProvider()),
            "AddModelProvider(factory)" => builder.AddModelProvider(static _ => new SnapshotProvider()),
            "AddEvalCheck" => builder.AddEvalCheck("snapshotCheck", FunctionEvaluator.Create("snapshotCheck", static (string _) => true)),
#pragma warning disable MAAI001
            "AddLoopEvaluator(instance)" => builder.AddLoopEvaluator(
                "snapshotLoop",
                new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()))),
            "AddLoopEvaluator(instance, description)" => builder.AddLoopEvaluator(
                "snapshotLoop",
                new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop())),
                "Snapshot criterion."),
            "AddLoopEvaluator(factory)" => builder.AddLoopEvaluator(
                "snapshotLoop",
                static _ => new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()))),
            "AddLoopEvaluator(factory, description)" => builder.AddLoopEvaluator(
                "snapshotLoop",
                static _ => new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop())),
                "Snapshot criterion."),
#pragma warning restore MAAI001
            "RequireCustomBinding<T>" => builder.RequireCustomBinding<ITenantContext>(),
            "RequireProductionProfile" => builder.RequireProductionProfile(),
            "AddSpeechSynthesizer(factory)" => builder.AddSpeechSynthesizer(
                $"snapshot-{Interlocked.Increment(ref _speechNames)}",
                static _ => null!),
            "AddSpeechTranscriber(factory)" => builder.AddSpeechTranscriber(
                $"snapshot-{Interlocked.Increment(ref _speechNames)}",
                static _ => null!),
            "AddSpeechSynthesizer(binding)" => builder.AddSpeechSynthesizer(
                $"snapshot-{Interlocked.Increment(ref _speechNames)}",
                new TextToSpeechClientBinding { ProviderName = "snapshot", MaxCharactersPerRequest = 10 }),
            "AddSpeechTranscriber(binding)" => builder.AddSpeechTranscriber(
                $"snapshot-{Interlocked.Increment(ref _speechNames)}",
                new SpeechToTextClientBinding { ProviderName = "snapshot" }),
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

    private sealed class SnapshotExtension : IAgentExtension
    {
        public string Name => "snapshot-extension";

        public string? Description => null;

        public void Configure(AgentExtensionContext context)
        {
        }
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

using System.Collections;
using System.Reflection;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// Gate: the async compile path builds every model client through the
/// registry's tenant-aware members, never through the tenant-blind synchronous
/// <see cref="IModelProviderRegistry.CreateChatClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// The defect class recurred three times. A fallback link (audit,
/// That decision), the compaction summarizer and the <c>aiJudge</c> loop judge
/// (2026-10-11) were each built with the synchronous member on a path that
/// serves tenants, so the tenant's egress policy and its own key never reached
/// them. Each time the agent's own model was correct, and every test that
/// looked only at that model stayed green.
/// </para>
/// <para>
/// The registry below throws from the synchronous member, and the probes switch
/// on every feature that builds a model client during compilation. A new
/// <see cref="ModelBinding"/> anywhere in <see cref="AgentDefinition"/> fails
/// the second test until a probe sets it — and switches on the feature that
/// builds its client.
/// </para>
/// </remarks>
public sealed class CompilePathTenantAwarenessTests
{
    private const string HarnessProbe = "harness-with-loop-judge-and-summarizer";
    private const string ChatProbe = "chat-with-summarizer";
    private const string UtilityProbe = "chat-summarized-by-the-utility-model";

    // Every ModelBinding an AgentDefinition can carry, as a property path.
    private static readonly string[] ProbedBindingPaths =
    [
        "Compaction.SummarizationModel",
        "Model",
    ];

    private static readonly string[] ProbeNames = [HarnessProbe, ChatProbe, UtilityProbe];

    public static TheoryData<string> Probes => new(ProbeNames);

    [Theory]
    [MemberData(nameof(Probes))]
    public async Task Compiling_for_a_tenant_never_builds_a_tenant_blind_model_client(string probe)
    {
        var registry = new TenantBlindCallRefusingRegistry(TestData.Providers(new FakeModelProvider()));
        var compiler = new AgentDefinitionCompiler(
            registry,
            TestData.Registry(),
            utilityModel: TestData.Binding(model: "utility-model"),
            judgeOptions: new ModelRunJudgeOptions { Model = TestData.Binding(model: "judge-model") });

        var agent = await compiler.CompileAsync(ProbeDefinition(probe), CancellationToken.None);

        agent.ShouldNotBeNull();
    }

    [Fact]
    public void A_probe_sets_every_model_binding_an_agent_definition_can_carry()
    {
        var discovered = new SortedSet<string>(StringComparer.Ordinal);
        CollectBindingPaths(typeof(AgentDefinition), prefix: string.Empty, discovered, visited: []);

        var unknown = discovered.Where(path => !ProbedBindingPaths.Contains(path, StringComparer.Ordinal)).ToList();
        var gone = ProbedBindingPaths.Where(path => !discovered.Contains(path)).ToList();

        unknown.ShouldBeEmpty(
            "AgentDefinition gained a ModelBinding. Set it in a probe below, switch on the feature that builds " +
            "its client, then list its path in ProbedBindingPaths. That client must be built with " +
            "CreateChatClientAsync or CreateSetupChatClientAsync before the agent.");
        gone.ShouldBeEmpty("A probed ModelBinding no longer exists on AgentDefinition; remove its path from ProbedBindingPaths.");

        foreach (var path in ProbedBindingPaths)
        {
            ProbeNames
                .Any(probe => IsSet(ProbeDefinition(probe), path.Split('.'), 0))
                .ShouldBeTrue($"no probe sets '{path}'");
        }
    }

    private static AgentDefinition ProbeDefinition(string probe)
    {
        var model = TestData.Binding() with
        {
            Fallbacks = [new ModelFallback { Provider = "fake", Model = "fallback-model" }],
        };

        var summarizer = new CompactionSettings
        {
            Strategy = CompactionStrategyKind.Pipeline,
            TriggerMessages = 20,
            SummarizationModel = TestData.Binding(model: "summarization-model"),
        };

        var judgedLoop = new HarnessSettings
        {
            Loop = new LoopSettings
            {
                Criteria = [new LoopCriterion { Kind = "aiJudge", JudgeCriteria = ["Is the answer complete?"] }],
            },
        };

        return probe switch
        {
            HarnessProbe => TestData.Definition(harness: judgedLoop) with { Model = model, Compaction = summarizer },

            ChatProbe => TestData.Definition() with { Model = model, Compaction = summarizer },

            UtilityProbe => TestData.Definition() with
            {
                Model = model,
                Compaction = summarizer with { Strategy = CompactionStrategyKind.Summarization, SummarizationModel = null },
            },

            _ => throw new ArgumentOutOfRangeException(nameof(probe), probe, "Unknown probe."),
        };
    }

    private static void CollectBindingPaths(Type type, string prefix, SortedSet<string> paths, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var path = prefix + property.Name;
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (propertyType == typeof(ModelBinding))
            {
                // A binding's own fallbacks are checked by the registry, link by link.
                paths.Add(path);
                continue;
            }

            if (ElementType(propertyType) is { } element && IsTraconType(element))
            {
                CollectBindingPaths(element, path + "[].", paths, visited);
            }
            else if (IsTraconType(propertyType))
            {
                CollectBindingPaths(propertyType, path + ".", paths, visited);
            }
        }

        visited.Remove(type);
    }

    private static bool IsSet(object? value, string[] segments, int index)
    {
        if (value is null)
        {
            return false;
        }

        if (index == segments.Length)
        {
            return true;
        }

        var segment = segments[index];
        var isCollection = segment.EndsWith("[]", StringComparison.Ordinal);
        var next = value.GetType().GetProperty(isCollection ? segment[..^2] : segment)!.GetValue(value);

        return isCollection
            ? next is IEnumerable items && items.Cast<object?>().Any(item => IsSet(item, segments, index + 1))
            : IsSet(next, segments, index + 1);
    }

    private static Type? ElementType(Type type)
        => type == typeof(string)
            ? null
            : type.GetInterfaces()
                .Append(type)
                .FirstOrDefault(static candidate =>
                    candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];

    private static bool IsTraconType(Type type)
        => type.IsClass && type.Assembly == typeof(AgentDefinition).Assembly;

    private sealed class TenantBlindCallRefusingRegistry(IModelProviderRegistry inner) : IModelProviderRegistry
    {
        public IReadOnlyList<ModelProviderDescriptor> List() => inner.List();

        public IChatClient CreateChatClient(ModelBinding binding)
            => throw new InvalidOperationException(
                $"The async compile path built a client for '{binding.Provider}/{binding.Model}' with the " +
                "tenant-blind synchronous CreateChatClient. The tenant's egress policy and its own key never " +
                "reach that client: build it with CreateChatClientAsync or CreateSetupChatClientAsync before " +
                "the agent.");

        public ValueTask<IChatClient> CreateChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => inner.CreateChatClientAsync(binding, cancellationToken);

        public ValueTask<IChatClient> CreateSetupChatClientAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => inner.CreateSetupChatClientAsync(binding, cancellationToken);

        public ValueTask<bool> HasTenantProviderOverrideAsync(ModelBinding binding, CancellationToken cancellationToken = default)
            => inner.HasTenantProviderOverrideAsync(binding, cancellationToken);
    }
}

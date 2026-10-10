using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every dependency of the agent compiler and of the model provider registry
/// is classified by how its state reaches the compiled-agent cache.
/// </summary>
/// <remarks>
/// <para>
/// A compiled agent carries every decision taken while it was compiled, and
/// the cache serves it again only while its key still describes those
/// decisions. "State read while compiling but missing from the key" recurred
/// five times: the tenant (K-380), a tenant credential (K-471, K-874), the
/// version used as a proxy for content (K-811), the shared instructions block
/// and callable sub-agents (K-812), and the tenant egress policy (F-304,
/// 2026-10-11).
/// </para>
/// <para>
/// This gate does not prove a classification right; the test it names does.
/// It makes a NEW dependency fail until someone decides how its state reaches
/// the cache: in the key, by bypassing the cache, or not at all because it is
/// read only at run time or is fixed for the life of the process. A
/// dependency that disappears fails too, so the list cannot go stale.
/// </para>
/// </remarks>
public sealed class CompiledAgentCacheKeyCoverageTests
{
    private static readonly Dictionary<Type, string> KnownDependencies = new()
    {
        // In the key.
        [typeof(ITenantContext)] = "key: the tenant (K-380)",
        [typeof(AgentSkillCatalog)] = "key: skill fingerprint (K-812)",
        [typeof(CallableAgentResolver)] = "key: callable sub-agent fingerprint (K-812, AgentDefinitionCacheInvalidationTests)",
        [typeof(IAgentDefinitionStore)] = "key: shared instructions text (K-812, AgentDefinitionCacheInvalidationTests)",
        [typeof(ITenantEgressPolicyStore)] = "key: egress policy content (F-304, DefinitionStoreAgentSourceEgressPolicyTests)",

        // Bypassed: an agent built with this state is never cached.
        [typeof(ITenantProviderBindingStore)] = "bypass: tenant credential (K-471, K-874, DefinitionStoreAgentSourceTenantCredentialTests)",
        [typeof(TenantProviderCredentialResolver)] = "bypass: read only when a tenant binding exists",

        // Known gap, recorded and open.
        [typeof(IToolRegistry)] = "OPEN F-305: a tenant's MCP tool snapshot is not in the key",

        // Read per call, never baked into the compiled agent.
        [typeof(IServiceProvider)] = "run time: services resolved per call",
        [typeof(ChatHistoryProvider)] = "run time: history read per run",
        [typeof(SkillScriptSupport)] = "run time: script grants checked per call (K-860)",
#pragma warning disable MAAI001 // AgentFileStore is "evaluation purposes only" — see K-020.
        [typeof(AgentFileStore)] = "run time: file memory read per call",
#pragma warning restore MAAI001
        [typeof(IMcpResourceContextProviderFactory)] = "run time: resources resolve their connection per call",
        [typeof(IVectorSearchStore)] = "run time: search_knowledge reads per call",
        [typeof(IEmbeddingGenerator<string, Embedding<float>>)] = "run time: embeddings per call",
        [typeof(ModelProviderCircuitBreaker)] = "run time: circuit state per call",
        [typeof(IAttachmentStore)] = "run time: attachment bytes per call",
        [typeof(ContentGuardPipeline)] = "run time: guards evaluate per call",
        [typeof(ProviderConcurrencyLimiter)] = "run time: limits per call",
        [typeof(IDistributedCache)] = "run time: response cache per call",
        [typeof(IProviderRetryClassifier)] = "run time: classifies each failure",

        // Fixed for the life of the process.
        [typeof(IModelProviderRegistry)] = "process: its own dependencies are classified here",
        [typeof(IEnumerable<IModelProvider>)] = "process: providers registered at startup",
        [typeof(ILoggerFactory)] = "process",
        [typeof(TraconMetrics)] = "process",
        [typeof(ModelBinding)] = "process: utility model from IOptions<TraconOptions>",
        [typeof(int)] = "process: knowledge result limit from IOptions",
        [typeof(TraconAgentGraphOptions)] = "process: from IOptions<TraconOptions>",
        [typeof(TimeProvider)] = "process",
        [typeof(IEnumerable<TraconLoopEvaluatorRegistration>)] = "process: criteria registered in code",
        [typeof(ModelRunJudgeOptions)] = "process: judge binding from IOptions",
    };

    [Fact]
    public void Every_dependency_of_the_compiler_and_the_registry_is_classified()
    {
        var actual = new[] { typeof(AgentDefinitionCompiler), typeof(ModelProviderRegistry) }
            .SelectMany(static type => type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(static constructor => constructor.GetParameters())
            .Select(static parameter => parameter.ParameterType)
            .ToHashSet();

        var unknown = actual
            .Where(type => !KnownDependencies.ContainsKey(type))
            .Select(static type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        var gone = KnownDependencies.Keys
            .Where(type => !actual.Contains(type))
            .Select(static type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        unknown.ShouldBeEmpty(
            "A new dependency reached the agent compiler or the model provider registry. Decide how its " +
            "state reaches the compiled-agent cache: in the key (CompiledAgentCache.CombineFingerprints in " +
            "ResolveDependenciesAsync), by bypassing the cache, or not at all because it is read only at run " +
            "time. Prove the choice with a test, then classify the type here.");
        gone.ShouldBeEmpty("A classified dependency is no longer a constructor parameter; remove it from the list.");
    }
}

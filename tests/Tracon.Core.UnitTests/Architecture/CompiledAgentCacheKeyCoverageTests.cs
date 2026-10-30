using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
/// five times: the tenant, a tenant credential, the
/// version used as a proxy for content, the shared instructions block
/// and callable sub-agents, and the tenant egress policy (
/// 2026-10-11). The sixth, found by the same scan, was the connection an MCP
/// tool object is bound to (2026-10-11).
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
        [typeof(ITenantContext)] = "key: the tenant",
        [typeof(AgentSkillCatalog)] = "key: skill fingerprint",
        [typeof(CallableAgentResolver)] = "key: callable sub-agent fingerprint (AgentDefinitionCacheInvalidationTests)",
        [typeof(IAgentDefinitionStore)] = "key: shared instructions text (AgentDefinitionCacheInvalidationTests)",
        [typeof(ITenantEgressPolicyStore)] = "key: egress policy content (DefinitionStoreAgentSourceEgressPolicyTests)",
        [typeof(IToolRegistry)] = "key: MCP connection binding (McpToolCacheTests)",
        [typeof(HostedToolRegistry)] = "key: what the hosted tool factories build for the tenant (HostedToolRunTests.A_tenant_gets_the_tool_its_own_factory_context_builds_and_a_replaced_store_reaches_the_next_run)",

        // Bypassed: an agent built with this state is never cached.
        [typeof(ITenantProviderBindingStore)] = "bypass: tenant credential (DefinitionStoreAgentSourceTenantCredentialTests)",
        [typeof(TenantProviderCredentialResolver)] = "bypass: read only when a tenant binding exists",

        // Read per call, never baked into the compiled agent.
        [typeof(IServiceProvider)] = "run time: services resolved per call",
        [typeof(ChatHistoryProvider)] = "run time: history read per run",
        [typeof(SkillScriptSupport)] = "run time: script grants checked per call",
#pragma warning disable MAAI001 // AgentFileStore is "evaluation purposes only"
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
        [typeof(IEnumerable<IAgentExtension>)] = "process: extensions registered in code; the names a definition selects are part of the definition, so they enter the key (AgentDefinitionCacheInvalidationTests)",
        [typeof(ModelRunJudgeOptions)] = "process: judge binding from IOptions",
        [typeof(IOptions<TraconTenantProviderOptions>)] = "process: RequireEgressPolicy is read from IOptions, so it is fixed when the host starts (TenantProviderEgressRequiredTests); a value that reloaded would need to enter the key like the policy does",
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

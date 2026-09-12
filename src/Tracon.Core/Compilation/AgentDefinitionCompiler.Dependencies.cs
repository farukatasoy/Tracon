using System.Security.Cryptography;
using System.Text;

namespace Tracon;

/// <summary>
/// Dependency and shared-instruction/callable-agent resolution: the pieces of
/// a definition that determine its compilation cache key.
/// </summary>
public sealed partial class AgentDefinitionCompiler
{
    /// <summary>Resolves the dependencies that determine a definition's compilation cache key.</summary>
    /// <param name="definition">The definition to inspect.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The callable agents, cache fingerprint, and cache-bypass decision.</returns>
    /// <remarks>
    /// Internal: see <see cref="AgentCompilationDependencies"/>'s remarks for why this is
    /// not (yet) part of the public surface.
    /// </remarks>
    internal async ValueTask<AgentCompilationDependencies> ResolveDependenciesAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var skills = await ResolveSkillsAsync(definition, cancellationToken).ConfigureAwait(false);
        var callable = await ResolveCallableAgentsAsync(definition, cancellationToken).ConfigureAwait(false);
        var shared = await ResolveSharedInstructionsAsync(definition, cancellationToken).ConfigureAwait(false);

        return new AgentCompilationDependencies
        {
            CallableAgents = callable,
            CacheFingerprint = CompiledAgentCache.CombineFingerprints(
                CompiledAgentCache.CombineFingerprints(skills.Fingerprint, callable.Fingerprint),
                shared.Fingerprint),
            BypassCache = await UsesTenantProviderOverrideAsync(definition.Model, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>Resolves <see cref="AgentDefinition.SharedInstructionsName"/> against <see cref="_definitionStore"/>.</summary>
    /// <exception cref="TraconCompilationException">
    /// The definition references a shared instructions block but no store is
    /// registered, the named block does not exist, or the named block itself
    /// references another block (a shared instructions block cannot chain).
    /// </exception>
    internal async ValueTask<ResolvedSharedInstructions> ResolveSharedInstructionsAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.SharedInstructionsName is not { Length: > 0 } blockName)
        {
            return ResolvedSharedInstructions.Empty;
        }

        if (_definitionStore is null)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' references shared instructions '{blockName}', but no " +
                "IAgentDefinitionStore is registered.")
            {
                AgentName = definition.Name,
            };
        }

        var block = await _definitionStore.GetAsync(blockName, cancellationToken).ConfigureAwait(false)
            ?? throw new TraconCompilationException(
                $"Agent '{definition.Name}' references shared instructions '{blockName}', but no such " +
                "definition exists.")
            {
                AgentName = definition.Name,
            };

        if (block.SharedInstructionsName is not null)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' references shared instructions '{blockName}', which itself " +
                $"references '{block.SharedInstructionsName}'. A shared instructions block cannot reference " +
                "another block.")
            {
                AgentName = definition.Name,
            };
        }

        return new ResolvedSharedInstructions(block.Instructions ?? string.Empty, $"{blockName}:{block.Version}");
    }

    /// <summary>
    /// Resolves the sub-agents a definition may call and produces the cache fingerprint.
    /// </summary>
    /// <param name="definition">The agent definition being resolved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sub-agent summaries and the fingerprint.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconCompilationException">
    /// The definition wants to call a sub-agent but the feature is not registered.
    /// </exception>
    public async ValueTask<ResolvedCallableAgents> ResolveCallableAgentsAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.CallableAgentNames.Count == 0)
        {
            return ResolvedCallableAgents.Empty;
        }

        if (_callableAgents is null)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' wants to call other agents, but the sub-agent " +
                "resolver is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        var infos = await _callableAgents
            .DescribeAsync(definition.CallableAgentNames, cancellationToken)
            .ConfigureAwait(false);

        return new ResolvedCallableAgents(infos, CreateCallableFingerprint(infos));
    }

    /// <summary>
    /// Produces a cache fingerprint from the sub-agent list.
    /// </summary>
    /// <remarks>
    /// A sub-agent's <em>description</em> is embedded in the instruction text
    /// sent to the model. If the fingerprint did not carry the version, the
    /// calling agent would stay in the cache with the old text when a
    /// sub-agent's description was updated, and the change would never take effect.
    /// </remarks>
    private static string CreateCallableFingerprint(IReadOnlyList<CallableAgentInfo> infos)
    {
        var content = new StringBuilder();

        foreach (var info in infos)
        {
            content.Append('|').Append(info.Name).Append(':').Append(info.Version);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }

    /// <summary>Validates a definition's skills and produces the cache key.</summary>
    /// <param name="definition">The agent definition being validated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Skill fingerprint.</returns>
    internal ValueTask<ResolvedAgentSkills> ResolveSkillsAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.SkillNames.Count == 0)
        {
            return ValueTask.FromResult(ResolvedAgentSkills.Empty);
        }

        if (_skills is null)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' uses skills, but the skill catalog is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        return _skills.ResolveAsync(definition, cancellationToken);
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Dependency and shared-instruction/callable-agent resolution: the pieces of
/// a definition that determine its compilation cache key.
/// </summary>
public sealed partial class AgentDefinitionCompiler
{
    /// <summary>
    /// Produces the fingerprint of a definition's own content: the
    /// <see cref="CompiledAgentCache"/> key component that decides whether a
    /// compiled agent may be reused.
    /// </summary>
    /// <param name="definition">The definition to fingerprint.</param>
    /// <returns>The fingerprint, as an uppercase hex SHA-256 string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <strong>The whole serialized definition is hashed, deliberately.</strong>
    /// Listing the compile-relevant fields by hand would be cheaper, but a field
    /// added to <see cref="AgentDefinition"/> later would not reach the list, and
    /// the failure mode is silent: the cache would serve an agent compiled from
    /// the OLD value of that field. Serializing the record makes a new field part
    /// of the fingerprint the moment it is declared.
    /// </para>
    /// <para>
    /// This replaces <see cref="AgentDefinition.Version"/>, which was a proxy for
    /// content and stopped being one after a delete: the store restarts numbering,
    /// so a recreated name is version <c>1</c> again. Content is the thing that was
    /// always meant; the version only stood in for it.
    /// </para>
    /// </remarks>
    public static string CreateDefinitionFingerprint(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var json = JsonSerializer.SerializeToUtf8Bytes(definition, TraconCoreJsonContext.Default.AgentDefinition);

        return Convert.ToHexString(SHA256.HashData(json));
    }

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

        var text = block.Instructions ?? string.Empty;

        // 🚨 The block's TEXT, not its version. The text is what gets prepended to
        // the referencing agent's instructions at compile time, and a version
        // number stops tracking it the moment the block is deleted and recreated:
        // numbering restarts at 1 and the referencing agent keeps the deleted
        // block's words. Same root cause as the definition fingerprint above.
        var fingerprint = string.Concat(
            blockName,
            ":",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))));

        return new ResolvedSharedInstructions(text, fingerprint);
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
    /// <para>
    /// A sub-agent's <em>description</em> is embedded in the instruction text sent
    /// to the model, so the whole <see cref="CallableAgentInfo"/> that gets baked
    /// into the calling agent is hashed - description included, not just the
    /// version.
    /// </para>
    /// <para>
    /// The version alone was not enough. A sub-agent deleted and recreated under the
    /// same name is version <c>1</c> again, and the caller stayed in the cache
    /// describing the deleted sub-agent to the model.
    /// </para>
    /// </remarks>
    private static string CreateCallableFingerprint(IReadOnlyList<CallableAgentInfo> infos)
    {
        var content = new StringBuilder();

        foreach (var info in infos)
        {
            content.Append('|').Append(info.Name)
                   .Append(':').Append(info.Version)
                   .Append(':').Append(info.Description);
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

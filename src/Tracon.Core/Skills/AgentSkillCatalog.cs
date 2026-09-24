using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Combines code and run-time skill sources with a precedence rule.</summary>
public sealed class AgentSkillCatalog
{
    private readonly Dictionary<string, AgentSkillDefinition> _codeSkills;
    private readonly IAgentSkillStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly IOptions<TraconOptions> _options;

    internal AgentSkillCatalog(
        IEnumerable<CodeSkillRegistration> codeSkills,
        IAgentSkillStore store,
        ITenantContext tenantContext,
        IOptions<TraconOptions> options)
    {
        ArgumentNullException.ThrowIfNull(codeSkills);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);

        _codeSkills = new Dictionary<string, AgentSkillDefinition>(StringComparer.Ordinal);
        foreach (var registration in codeSkills)
        {
            if (!_codeSkills.TryAdd(registration.Definition.Name, registration.Definition))
            {
                throw new TraconException(
                    $"More than one code skill is registered with name '{registration.Definition.Name}'.");
            }
        }

        _store = store;
        _tenantContext = tenantContext;
        _options = options;
    }

    internal string TenantId => _tenantContext.TenantId;

    internal async ValueTask<ResolvedAgentSkills> ResolveAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken)
    {
        if (definition.SkillNames.Count > _options.Value.Skills.MaxSkillsPerAgent)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' can have at most {_options.Value.Skills.MaxSkillsPerAgent} skills.")
            {
                AgentName = definition.Name,
            };
        }

        var skills = new List<AgentSkillDefinition>(definition.SkillNames.Count);
        foreach (var name in definition.SkillNames)
        {
            var skill = await FindAsync(name, cancellationToken).ConfigureAwait(false);
            if (skill is not { })
            {
                throw new TraconCompilationException(
                    $"Agent '{definition.Name}' refers to skill '{name}', but the skill was not found.")
                {
                    AgentName = definition.Name,
                };
            }

            if (skill.Enabled)
            {
                skills.Add(skill);
            }
        }

        return new ResolvedAgentSkills(CreateFingerprint(definition.SkillNames, skills));
    }

    /// <summary>Determines whether a skill is registered in code or in the store.</summary>
    /// <remarks>
    /// Returns <see langword="true"/> for a disabled skill too. Absence and disabled
    /// status are separate conditions, and the validator reports them with different codes.
    /// </remarks>
    internal async ValueTask<bool> ExistsAsync(string name, CancellationToken cancellationToken)
        => await FindAsync(name, cancellationToken).ConfigureAwait(false) is not null;

    internal async ValueTask<IReadOnlyList<AgentSkillDefinition>> GetEnabledAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken)
    {
        var skills = new List<AgentSkillDefinition>(names.Count);
        foreach (var name in names)
        {
            var skill = await FindAsync(name, cancellationToken).ConfigureAwait(false);
            if (skill is { Enabled: true })
            {
                skills.Add(skill);
            }
        }

        return skills;
    }

    /// <summary>Determines whether a skill with this name is registered in code.</summary>
    /// <remarks>
    /// A code skill wins over a stored one with the same name, so a stored skill
    /// under such a name never runs. The save endpoint refuses to write one.
    /// </remarks>
    /// <param name="name">The skill name, compared exactly.</param>
    /// <returns><see langword="true"/> when <c>AddSkill</c> registered the name.</returns>
    internal bool IsDefinedInCode(string name) => _codeSkills.ContainsKey(name);

    /// <summary>
    /// Finds a skill the way the runtime resolves it - code first, then the store -
    /// and reports where it came from in <see cref="AgentSkillDefinition.Origin"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A disabled skill is returned too, unlike <see cref="GetEnabledAsync"/>. The
    /// script grant endpoint relies on that: filtering here would make a disabled
    /// stored skill look absent, and a grant for it would be written with no content
    /// pin and no platform check, and would then refuse every script once the skill
    /// is enabled again.
    /// </para>
    /// <para>
    /// The origin is set by where the definition was found, never taken from what a
    /// store returned: a custom store cannot make a stored skill read as code.
    /// </para>
    /// </remarks>
    /// <param name="name">The skill name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved definition; <see langword="null"/> when neither source has the name.</returns>
    internal async ValueTask<AgentSkillDefinition?> FindWithOriginAsync(string name, CancellationToken cancellationToken)
    {
        if (_codeSkills.TryGetValue(name, out var code))
        {
            return code;
        }

        var stored = await _store.GetAsync(TenantId, name, cancellationToken).ConfigureAwait(false);

        return stored is null ? null : stored with { Origin = AgentDefinitionOrigin.Database };
    }

    private ValueTask<AgentSkillDefinition?> FindAsync(string name, CancellationToken cancellationToken)
        => _codeSkills.TryGetValue(name, out var skill)
            ? ValueTask.FromResult<AgentSkillDefinition?>(skill)
            : _store.GetAsync(TenantId, name, cancellationToken);

    private string CreateFingerprint(IReadOnlyList<string> names, IReadOnlyList<AgentSkillDefinition> enabledSkills)
    {
        var content = new StringBuilder(TenantId);
        foreach (var name in names)
        {
            content.Append('|').Append(name);
            var skill = enabledSkills.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (skill is not null)
            {
                content.Append(':').Append(skill.Version).Append(':').Append(skill.UpdatedAt.UtcTicks);
            }
            else
            {
                content.Append(":disabled");
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
    }
}

internal readonly record struct ResolvedAgentSkills(string Fingerprint)
{
    public static ResolvedAgentSkills Empty { get; } = new(string.Empty);
}

using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Skill source production: database and disk sources, deduplication, and
/// tenant-isolated caching.
/// </summary>
public sealed partial class AgentDefinitionCompiler
{
    private AgentSkillsProvider CreateSkillsProvider(AgentDefinition definition)
        => new(CreateSkillsSource(definition), new AgentSkillsProviderOptions(), _loggerFactory, ownsSource: true);

    private DeduplicatingAgentSkillsSource CreateSkillsSource(AgentDefinition definition)
    {
        if (_skills is null)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' uses skills, but the skill catalog is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        AgentSkillsSource source = new AggregatingAgentSkillsSource(CreateInnerSources(definition));
        source = new FilteringAgentSkillsSource(
            source,
            (skill, _) => definition.SkillNames.Contains(skill.Frontmatter.Name, StringComparer.Ordinal),
            _loggerFactory);
        source = new CachingAgentSkillsSource(
            source,
            new CachingAgentSkillsSourceOptions
            {
                CacheIsolationKeySelector = _ => _skills.TenantId,
            });
        return new DeduplicatingAgentSkillsSource(source, _loggerFactory);
    }

    /// <summary>Combines the database and disk sources.</summary>
    /// <remarks>
    /// The disk source is added only when a root is defined via
    /// <c>UseSkillScripts</c>. Order matters: the database source comes
    /// first, so if a skill with the same name exists,
    /// <c>DeduplicatingAgentSkillsSource</c> keeps the database record and
    /// tenant isolation is not broken.
    /// </remarks>
    private List<AgentSkillsSource> CreateInnerSources(AgentDefinition definition)
    {
        var sources = new List<AgentSkillsSource>(2)
        {
            new AgentPrismSkillsSource(_skills!, definition, _scripts),
        };

        if (_scripts?.CreateFileSource() is { } fileSource)
        {
            sources.Add(fileSource);
        }

        return sources;
    }
}

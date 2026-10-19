using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Skill provider production: database and disk sources, the named-skill filter,
/// deduplication, and tenant-isolated caching, chained by Microsoft Agent
/// Framework's own <see cref="AgentSkillsProviderBuilder"/>.
/// </summary>
public sealed partial class AgentDefinitionCompiler
{
    /// <summary>Builds the provider for the skills a definition names, for a plain agent and a harness agent alike.</summary>
    /// <remarks>
    /// <para>
    /// One chain serves both agent types. The builder orders it: the sources,
    /// then the cache, then the filter, then deduplication. The cache holds the
    /// sources' combined list and the filter runs on every call.
    /// </para>
    /// <para>
    /// The database source is added first, then the disk source, which only exists
    /// when a root is defined via <c>UseSkillScripts</c>. Order matters: if a skill
    /// with the same name exists in both, deduplication keeps the first, the database
    /// record, and tenant isolation is not broken.
    /// </para>
    /// <para>
    /// The cache is keyed by tenant. Without a key Microsoft Agent Framework shares one
    /// list across every caller of the provider, and the database source's list
    /// depends on the tenant.
    /// </para>
    /// </remarks>
    /// <exception cref="TraconCompilationException">The skill catalog is not registered.</exception>
    private AgentSkillsProvider CreateSkillsProvider(AgentDefinition definition)
    {
        if (_skills is null)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' uses skills, but the skill catalog is not registered.")
            {
                AgentName = definition.Name,
            };
        }

        var builder = new AgentSkillsProviderBuilder();

        if (_loggerFactory is not null)
        {
            builder.UseLoggerFactory(_loggerFactory);
        }

        builder.UseSource(new TraconSkillsSource(_skills, definition, _scripts));

        if (_scripts?.CreateFileSource() is { } fileSource)
        {
            builder.UseSource(fileSource);
        }

        return builder
            .UseFilter((skill, _) => definition.SkillNames.Contains(skill.Frontmatter.Name, StringComparer.Ordinal))
            .UseCachingOptions(options => options.CacheIsolationKeySelector = _ => _skills.TenantId)
            .Build();
    }
}

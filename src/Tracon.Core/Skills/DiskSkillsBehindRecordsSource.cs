using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Offers the skills of a disk source except the ones whose name a skill record owns, enabled or not.
/// </summary>
/// <remarks>
/// <para>
/// A skill registered in code or saved in the store owns its name: <see cref="AgentSkillCatalog"/> answers "disabled"
/// for a record that is switched off, and does not look under the skill roots for the same name. The store source
/// offers only the enabled records, and the disk source sits after it, so without this filter a record that is switched
/// off left its name free for the disk skill: the catalog and the validator said "disabled" while the run offered the
/// skill from disk. Switching the record off is the one way a tenant takes a skill of the installation away from its
/// agents, so it has to work on the path a run takes.
/// </para>
/// <para>
/// An enabled record needs no help (the store source comes first and the provider keeps the first of one name), and is
/// dropped here too, so the rule is one: a name with a record is never served from disk.
/// </para>
/// <para>
/// The compiled agent is keyed by a fingerprint that carries the disabled state of each name it uses, so a record
/// switched on or off compiles the agent again and this filter is asked on the new provider.
/// </para>
/// </remarks>
/// <param name="inner">The disk source.</param>
/// <param name="catalog">The catalog that knows the records.</param>
internal sealed class DiskSkillsBehindRecordsSource(AgentSkillsSource inner, AgentSkillCatalog catalog) : AgentSkillsSource
{
    /// <inheritdoc />
    public override async Task<IList<AgentSkill>> GetSkillsAsync(
        AgentSkillsSourceContext context,
        CancellationToken cancellationToken = default)
    {
        var skills = await inner.GetSkillsAsync(context, cancellationToken).ConfigureAwait(false);
        var offered = new List<AgentSkill>(skills.Count);

        foreach (var skill in skills)
        {
            if (!await catalog.HoldsRecordAsync(skill.Frontmatter.Name, cancellationToken).ConfigureAwait(false))
            {
                offered.Add(skill);
            }
        }

        return offered;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

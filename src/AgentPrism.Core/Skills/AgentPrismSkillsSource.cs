using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>AgentPrism skill tanimlarini MAF skill kaynagina cevirir.</summary>
internal sealed class AgentPrismSkillsSource : AgentSkillsSource
{
   private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

   private readonly AgentSkillCatalog _catalog;
   private readonly IReadOnlyList<string> _skillNames;

   public AgentPrismSkillsSource(AgentSkillCatalog catalog, AgentDefinition definition)
   {
      ArgumentNullException.ThrowIfNull(catalog);
      ArgumentNullException.ThrowIfNull(definition);

      _catalog = catalog;
      _skillNames = definition.SkillNames;
   }

   public override async Task<IList<AgentSkill>> GetSkillsAsync(
       AgentSkillsSourceContext context,
       CancellationToken cancellationToken = default)
   {
      var definitions = await _catalog.GetEnabledAsync(_skillNames, cancellationToken).ConfigureAwait(false);
      var skills = new List<AgentSkill>(definitions.Count);

      foreach (var definition in definitions)
      {
         skills.Add(CreateSkill(definition));
      }

      return skills;
   }

   private static AgentInlineSkill CreateSkill(AgentSkillDefinition definition)
   {
      var frontmatter = new AgentSkillFrontmatter(definition.Name, definition.Description, definition.Compatibility)
      {
         License = definition.License,
         AllowedTools = definition.AllowedTools,
         Metadata = new AdditionalPropertiesDictionary(
              definition.Metadata.Select(static pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))),
      };
      var skill = new AgentInlineSkill(
          frontmatter,
          definition.Instructions,
          SerializerOptions,
          static _ => new AIFunctionArguments(StringComparer.Ordinal));

      foreach (var resource in definition.Resources)
      {
         // MAF bu asiri yuklemede comparer sunmaz. Adlar HTTP ve store
         // sinirinda benzersiz oldugu icin burada ek bir karsilastirma yoktur.
#pragma warning disable MA0002
         skill.AddResource(resource.Name, resource.Content, resource.Description ?? string.Empty);
#pragma warning restore MA0002
      }

      return skill;
   }
}

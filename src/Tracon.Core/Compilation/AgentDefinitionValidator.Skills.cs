namespace Tracon;

/// <summary>The skill settings step of <see cref="AgentDefinitionValidator"/>.</summary>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>The code of the warning about skill settings on an agent that names no skill.</summary>
    internal const string SkillSettingsWithoutSkillsCode = "skill_settings_without_skills";

    /// <summary>
    /// Warns when a definition carries <see cref="AgentDefinition.Skills"/> but names no skill, so
    /// the settings change nothing.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="messages">The report's messages.</param>
    /// <remarks>
    /// A warning, not an error: a form that keeps the settings while the author clears the skill list
    /// is not a mistake the compiler needs to stop.
    /// </remarks>
    private static void CheckSkillSettings(AgentDefinition definition, List<ValidationMessage> messages)
    {
        if (definition.Skills is null || definition.SkillNames.Count > 0)
        {
            return;
        }

        messages.Add(new ValidationMessage
        {
            Severity = ValidationSeverity.Warning,
            Code = SkillSettingsWithoutSkillsCode,
            Message = $"Agent '{definition.Name}' sets {nameof(AgentDefinition.Skills)}, but names no skill in " +
                      $"{nameof(AgentDefinition.SkillNames)}, so the settings change nothing.",
            Path = "skills",
        });
    }
}

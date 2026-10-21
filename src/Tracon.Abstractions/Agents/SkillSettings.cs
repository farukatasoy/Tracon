namespace Tracon;

/// <summary>
/// Settings for how an agent uses the skills it names in <c>AgentDefinition.SkillNames</c>.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework wraps all three skill tools for approval by default. This
/// record lets a definition turn the approval off for the two tools that only read
/// text. The third tool, <c>run_skill_script</c>, runs code in a process or in a sandbox
/// and keeps its approval: there is deliberately no member that turns that off. A standing
/// approval rule that an administrator writes for the tool can still approve a call.
/// </para>
/// <para>
/// An agent that is exposed over A2A or MCP, or that runs without a session, has nobody
/// to answer an approval request. It needs <see cref="DisableReadApproval"/>, or the
/// first <c>load_skill</c> call ends the run.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// var definition = new AgentDefinition
/// {
///     Name = "support",
///     Model = new ModelBinding { Provider = "openai", Model = "gpt-5.4-mini" },
///     SkillNames = ["house-style"],
///     Skills = new SkillSettings { DisableReadApproval = true },
/// };
/// </code>
/// </example>
public sealed record SkillSettings
{
    /// <summary>
    /// Gets a value that makes <c>load_skill</c> and <c>read_skill_resource</c> run without
    /// asking for approval. <c>run_skill_script</c> keeps its approval.
    /// </summary>
    /// <remarks>
    /// Skill text reaches the model as instructions. Turn this on only for skills that a
    /// tenant administrator writes, because the text is then a prompt-injection surface that no
    /// person reviews at run time.
    /// </remarks>
    public bool DisableReadApproval { get; init; }
}

namespace Tracon;

/// <summary>
/// Settings of the AG-UI endpoint connected by <c>MapTraconAGUI</c>.
/// </summary>
/// <remarks>
/// Access protection is not set here: the endpoint inherits it from <c>MapTracon</c>.
/// </remarks>
public sealed class TraconAGUIOptions
{
    /// <summary>
    /// Whether the front end may supply the values of the agent's run parameters in <c>forwardedProps.parameters</c>.
    /// Default <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off, an AG-UI request cannot carry a parameter: <c>forwardedProps</c> is ignored, and an agent with a required
    /// parameter cannot be started from AG-UI. On, <c>forwardedProps.parameters</c> is a JSON object whose values are all
    /// strings, and it is checked exactly like the <c>parameters</c> of <c>POST /api/agents/{name}/run</c>: a name the
    /// agent's schema does not declare, a missing required value and a value over <c>MaxParameterValueLength</c> are each a
    /// <c>400</c>. A value that is not a string is a <c>400</c> as well. The values are recorded with the run, so a replay
    /// repeats them. Nothing else is read from <c>forwardedProps</c>: the tenant, the session and the agent are never taken
    /// from it.
    /// </para>
    /// <para>
    /// <strong>A value becomes part of the agent's instructions.</strong> The instructions are the trusted channel to the
    /// model, and the content guard does not read them. A browser that can set the value writes part of them. Turn this on
    /// when the host accepts that for its front end and keeps the schema narrow: declare only the parameters the front
    /// end needs, and keep the value short. A host that can produce the value on its own server should do that instead.
    /// </para>
    /// <para>
    /// Tracon's own log lines and the <c>RUN_ERROR</c> text carry the names of parameters, never their values. An error that
    /// a model provider returns can quote the instructions, and the server log then holds that text.
    /// </para>
    /// </remarks>
    public bool AcceptForwardedParameters { get; set; }
}

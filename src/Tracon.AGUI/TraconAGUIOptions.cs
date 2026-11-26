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

    /// <summary>
    /// The host's own code that decides the parameter values of an AG-UI run, on the server. Default <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set, it replaces <see cref="AcceptForwardedParameters"/>: the endpoint calls it once per request, after the run gates
    /// and before the agent is resolved, with the agent, the thread, the caller and what the front end sent
    /// (<see cref="AGUIParameterRequest"/>). <strong>Only what it returns reaches the run.</strong> The front end's own values
    /// are an input of the resolver and enter the run only if the resolver returns them. A <see langword="null"/> or empty
    /// result runs the agent with its defaults.
    /// </para>
    /// <para>
    /// The values it returns are checked against the agent's schema and <c>MaxParameterValueLength</c> like any others, and are
    /// recorded with the run. A tool of the run reads them from <c>TraconRunContext.Current.Parameters</c> with the source
    /// <see cref="ParameterValueSource.Host"/>: they are the host's, not the browser's, so a tool may decide by them what a
    /// <see cref="ParameterValueSource.Client"/> value could never decide. A run that repeats them later (an approval resumed
    /// from the inbox, a replay) reads them as <see cref="ParameterValueSource.Client"/>: the source is not stored with the
    /// values.
    /// </para>
    /// <para>
    /// <strong>It fails closed.</strong> A resolver that throws ends the request with <c>500</c> and the error type
    /// <c>parameter_resolution_failed</c>, and no run starts. Nothing the front end sent, and no message of the exception, is
    /// in the answer. A resolver reads the tenant from <c>ITenantContext</c> itself, the same as a tool; it receives a
    /// cancellation token that is the request's.
    /// </para>
    /// <para>
    /// A resolver is called for every agent the endpoint serves. It tells them apart by <see cref="AGUIParameterRequest.AgentName"/>,
    /// which is how a host accepts values for one agent and none for another.
    /// </para>
    /// </remarks>
    public Func<AGUIParameterRequest, CancellationToken, ValueTask<IReadOnlyDictionary<string, string>?>>? ResolveParameters { get; set; }

    /// <summary>
    /// The host's own code that decides the sentence an assistant says first in a new conversation, on the server. Default
    /// <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set, a request that carries no new input (no user message, no tool result and no answer to an approval or a request
    /// for input) is an <strong>opening request</strong> and not a mistake. It passes the same gates as a run and resolves the
    /// agent the same way, with the parameters <see cref="ResolveParameters"/> or <see cref="AcceptForwardedParameters"/> decide.
    /// Then, when the thread has no history yet, the resolver is called once with the agent, the thread, the caller and what the
    /// front end sent. A non-blank text it returns is written to the thread's history as an assistant message, with no model
    /// call and no run, and is streamed back as <c>RUN_STARTED</c>, <c>TEXT_MESSAGE_START</c>, <c>TEXT_MESSAGE_CONTENT</c>,
    /// <c>TEXT_MESSAGE_END</c> and <c>RUN_FINISHED</c>. The agent's next turn reads it from the history, so it knows it has already
    /// greeted, and an evaluation of the conversation reads it as the first message.
    /// </para>
    /// <para>
    /// The greeting is said <strong>once</strong>: a thread that has any history, and a thread another request opened first, is
    /// answered with <c>RUN_STARTED</c> and <c>RUN_FINISHED</c> and nothing else, and the resolver is not called. A <see langword="null"/>
    /// or blank text says nothing in the same way. The text is the host's own: it is not screened by a content guard and it
    /// is never taken from the front end.
    /// </para>
    /// <para>
    /// <strong>It fails closed.</strong> A resolver that throws ends the request with <c>500</c> and the error type
    /// <c>greeting_resolution_failed</c>, and nothing is written. Without a resolver an opening request is the <c>400</c> it
    /// always was.
    /// </para>
    /// <example>
    /// <code>
    /// app.MapTraconAGUI("/tracon/agui", options =>
    ///     options.ResolveGreeting = (request, _) =&gt; ValueTask.FromResult&lt;string?&gt;(
    ///         $"Hello {request.User.Identity?.Name}! Are you ready to start?"));
    /// </code>
    /// </example>
    /// </remarks>
    public Func<AGUIParameterRequest, CancellationToken, ValueTask<string?>>? ResolveGreeting { get; set; }
}

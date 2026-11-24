namespace Tracon;

/// <summary>
/// Says where the parameter values of a run came from, which decides whether a tool may take them as given.
/// </summary>
/// <remarks>
/// <para>
/// The values a run is bound with fill the placeholders of the agent's instructions, and a tool of that run can read them
/// (<c>AgentRunScope.Parameters</c>). A value that came from a front end is text the user, or a script in the user's
/// browser, chose; a tool that acts on it as if the server had decided it lets the client decide. The source is read together
/// with the values, never apart from them.
/// </para>
/// <para>
/// The first member is the value of a scope that says nothing: a source that is not known is read as the least trusted one.
/// </para>
/// </remarks>
public enum ParameterValueSource
{
    /// <summary>
    /// The values came in a request, or are kept from one: the body of a run request, the <c>forwardedProps</c> of an AG-UI
    /// request the host takes as they are, and every run that repeats the values of an earlier request (a queued run, an
    /// approval resumed from the inbox, a replay, a continuation, a batch item). The text of whoever sent the request; it
    /// carries no authority, and a tool does not decide who may see or change something by it.
    /// </summary>
    Client = 0,

    /// <summary>
    /// The application that embeds Tracon gave the values through its own API, such as
    /// <c>AgentResolveOptions.Parameters</c>. The code that made the call is the host's own.
    /// </summary>
    Caller = 1,

    /// <summary>
    /// The host's own code produced the values on the server for this request, such as the
    /// <c>TraconAGUIOptions.ResolveParameters</c> callback of the AG-UI endpoint. What the front end sent is an input of that
    /// code and not a value of the run: only what the code returns reaches the run.
    /// </summary>
    Host = 2,
}

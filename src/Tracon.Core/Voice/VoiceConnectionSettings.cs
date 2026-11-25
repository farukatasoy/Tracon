using System.Security.Claims;

namespace Tracon;

/// <summary>
/// What the host's connection resolver (<see cref="VoiceConversationOptions.ResolveConnection"/>) is given to decide how one
/// voice conversation connection runs.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ClientVoiceId"/> is the front end's text and is <strong>not trusted</strong>: it is what the browser put in
/// the <c>voiceId</c> field of its <c>start</c> frame. The resolver decides whether it becomes the voice of the connection.
/// What it returns is the only thing that reaches the run.
/// </para>
/// <para>
/// The connection has passed the endpoint's access protection, the run authorization gate (<c>SessionAccess.Voice</c>), the
/// session ownership check and the tenant's provider checks before the resolver is called, and the agent exists for the
/// tenant, so a connection that is turned away never reaches the host's code.
/// </para>
/// </remarks>
public sealed record VoiceConnectionRequest
{
    /// <summary>Gets the agent the connection talks to, as the <c>start</c> frame named it.</summary>
    public required string AgentName { get; init; }

    /// <summary>Gets the agent session the conversation runs in; it is the session id of the socket address.</summary>
    public required string SessionId { get; init; }

    /// <summary>Gets the tenant the connection belongs to. It is fixed for the life of the connection.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the authenticated caller; an anonymous principal when the host authenticates nobody.</summary>
    public required ClaimsPrincipal User { get; init; }

    /// <summary>
    /// Gets the voice identifier the front end asked for in its <c>start</c> frame; <see langword="null"/> when it asked for
    /// none. Untrusted.
    /// </summary>
    public string? ClientVoiceId { get; init; }
}

/// <summary>What the host decided for one voice conversation connection.</summary>
/// <remarks>
/// <para>
/// Every property is optional. A <see langword="null"/> result, or one that sets nothing, runs the connection with the
/// agent's defaults, the configured voice and no opening.
/// </para>
/// <para>
/// <see cref="Greeting"/> and <see cref="OpeningPrompt"/> are two ways to open the conversation and exclude each other: a
/// result that sets both is refused, because choosing one silently would hide a mistake of the resolver.
/// </para>
/// </remarks>
public sealed record VoiceConnectionSettings
{
    /// <summary>
    /// Gets the parameter values of the agent for every turn of the connection. They are checked against the agent's
    /// schema and <c>MaxParameterValueLength</c> like any others and are recorded with each run.
    /// </summary>
    /// <remarks>
    /// A tool of the run reads them from <c>TraconRunContext.Current.Parameters</c> with the source
    /// <see cref="ParameterValueSource.Host"/>: they are the host's, not the browser's. The front end sends no parameter on
    /// this protocol, so nothing the browser sent can reach them. A name the agent does not declare ends the connection
    /// with an <c>error</c> frame when it starts.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }

    /// <summary>
    /// Gets the identifier of the voice that speaks the answers. When it is <see langword="null"/> the voice of
    /// <see cref="VoiceConversationOptions.VoiceId"/> applies, then the provider's default; the voice the front end asked
    /// for is never used once a resolver is set.
    /// </summary>
    public string? VoiceId { get; init; }

    /// <summary>
    /// Gets a fixed text the assistant says first, right after the <c>ready</c> frame. Nothing runs: no model is called and
    /// no run is recorded.
    /// </summary>
    /// <remarks>
    /// The text is spoken through the same path as an answer (so <see cref="VoiceConversationOptions.MaxSpokenCharactersPerTurn"/>
    /// applies) and is written to the session history as an assistant message, so the agent knows it has already greeted.
    /// It is said only when the session has no history yet: a client that reconnects to a conversation in progress is not
    /// greeted again.
    /// </remarks>
    public string? Greeting { get; init; }

    /// <summary>
    /// Gets a text the agent answers first, right after the <c>ready</c> frame, as if a user had said it: the agent's answer
    /// is the opening.
    /// </summary>
    /// <remarks>
    /// This is an ordinary turn: a run is recorded and costs what a run costs, and the text and the answer enter the
    /// session history (the text as a user message). It is the host's own text, never the front end's. It runs only when
    /// the session has no history yet.
    /// </remarks>
    public string? OpeningPrompt { get; init; }
}

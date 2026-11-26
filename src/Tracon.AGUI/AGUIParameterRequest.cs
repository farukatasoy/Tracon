using System.Security.Claims;

namespace Tracon;

/// <summary>
/// What the host's parameter resolver (<see cref="TraconAGUIOptions.ResolveParameters"/>) is given to decide the parameter values
/// of one AG-UI run, and what its greeting resolver (<see cref="TraconAGUIOptions.ResolveGreeting"/>) is given to decide the
/// first sentence of a conversation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ForwardedParameters"/> is the front end's text and is <strong>not trusted</strong>: it is what the browser put in
/// <c>forwardedProps.parameters</c>, shape-checked (an object whose values are strings) and nothing more. The resolver decides
/// whether any of it, or something it looks up from <see cref="ThreadId"/> and <see cref="User"/>, becomes a value of the run.
/// What it returns is checked against the agent's schema like any other values.
/// </para>
/// <para>
/// The request has passed the endpoint's access protection and the run gates (role, run authorization, session ownership, quota)
/// before the resolver is called, and the agent exists for the tenant, so a request that is turned away never reaches the
/// host's code.
/// </para>
/// </remarks>
/// <param name="AgentName">The agent the request runs.</param>
/// <param name="ThreadId">The AG-UI thread, which is the session the run belongs to.</param>
/// <param name="User">The authenticated caller; an anonymous principal when the host authenticates nobody.</param>
/// <param name="ForwardedParameters">
/// What the front end sent in <c>forwardedProps.parameters</c>, as it sent it; empty when it sent none. Untrusted.
/// </param>
public sealed record AGUIParameterRequest(
    string AgentName,
    string ThreadId,
    ClaimsPrincipal User,
    IReadOnlyDictionary<string, string> ForwardedParameters);

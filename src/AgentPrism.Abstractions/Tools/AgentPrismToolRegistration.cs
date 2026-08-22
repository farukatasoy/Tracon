using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// A single tool registered with dependency injection. AgentPrism builds the
/// tool registry from these registrations.
/// </summary>
/// <remarks>
/// The consumer can also register tools from their own DI modules:
/// <code>
/// services.AddSingleton(new AgentPrismToolRegistration(myFunction));
/// </code>
/// </remarks>
public sealed class AgentPrismToolRegistration
{
    /// <summary>Creates a new tool registration.</summary>
    /// <param name="function">
    /// The tool to register. An <see cref="AIFunction"/> runs on the server;
    /// an <see cref="AIFunctionDeclaration"/> that is not an
    /// <see cref="AIFunction"/> (declaration-only, produced by
    /// <c>AddClientTool</c>) runs on the caller instead.
    /// </param>
    /// <param name="requiresApproval">
    /// Whether explicit approval is required before the call.
    /// <paramref name="function"/> must be an <see cref="AIFunction"/> when
    /// this is <see langword="true"/> — approval defers a server-side call;
    /// a client-side tool has no server-side body to approve.
    /// </param>
    /// <param name="source">
    /// The tool's source. <see langword="null"/> for tools defined in code;
    /// the server name for tools coming from a remote MCP server.
    /// </param>
    /// <param name="effect">The tool's effect class. Defaults to <see cref="ToolEffect.Read"/>.</param>
    /// <param name="requiredPermission">
    /// The permission name a caller must hold to call this tool, or
    /// <see langword="null"/> to declare none.
    /// </param>
    /// <param name="timeout">
    /// The longest duration this tool's call may run, or <see langword="null"/>
    /// to use the installation default.
    /// </param>
    /// <param name="safeToRepeat">See <see cref="SafeToRepeat"/>. Defaults to <see langword="false"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    public AgentPrismToolRegistration(
        AIFunctionDeclaration function,
        bool requiresApproval = false,
        string? source = null,
        ToolEffect effect = ToolEffect.Read,
        string? requiredPermission = null,
        TimeSpan? timeout = null,
        bool safeToRepeat = false)
    {
        ArgumentNullException.ThrowIfNull(function);

        Function = function;
        RequiresApproval = requiresApproval;
        Source = source;
        Effect = effect;
        RequiredPermission = requiredPermission;
        Timeout = timeout;
        SafeToRepeat = safeToRepeat;
    }

    /// <summary>The registered tool.</summary>
    public AIFunctionDeclaration Function { get; }

    /// <summary>Whether explicit approval is required before the call.</summary>
    public bool RequiresApproval { get; }

    /// <summary>The tool's source. <see langword="null"/> for tools defined in code.</summary>
    public string? Source { get; }

    /// <summary>The tool's effect class.</summary>
    public ToolEffect Effect { get; }

    /// <summary>
    /// The permission name a caller must hold to call this tool, or
    /// <see langword="null"/> when the tool declares none.
    /// </summary>
    public string? RequiredPermission { get; }

    /// <summary>
    /// The longest duration this tool's call may run, or <see langword="null"/>
    /// to use the installation default.
    /// </summary>
    public TimeSpan? Timeout { get; }

    /// <summary>
    /// Whether this tool's call may run again when an interrupted run is
    /// continued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default, <see langword="false"/>, is safe for every tool: a call
    /// this repository has no record of finishing is never repeated
    /// automatically. Continuation asks a stronger question than
    /// idempotency — not just "does the same call produce the same
    /// result?" but "may this call's SIDE EFFECT legitimately happen a
    /// second time?" Only a tool whose author can answer yes to that should
    /// set this to <see langword="true"/> (typically by carrying its own
    /// idempotency key).
    /// </para>
    /// <para>
    /// Only consulted for <see cref="ToolEffect.Destructive"/> and
    /// <see cref="ToolEffect.External"/> tools, which are otherwise refused
    /// for a continuation — setting this to <see langword="true"/> is how a
    /// tool author opts a specific irreversible or externally visible call
    /// back in. <see cref="ToolEffect.Read"/> and <see cref="ToolEffect.Write"/>
    /// calls are always safe to continue and never consult this flag.
    /// </para>
    /// </remarks>
    public bool SafeToRepeat { get; }
}

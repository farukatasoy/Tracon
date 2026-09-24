using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// A single tool registered with dependency injection. Tracon builds the
/// tool registry from these registrations.
/// </summary>
/// <remarks>
/// <para>
/// The consumer can also register tools from their own DI modules. Only the
/// tool itself is required; every other setting is an <c>init</c> property
/// that keeps its default when it is not set.
/// </para>
/// <para>
/// A registration is shared by every run, so it cannot change after it is
/// created.
/// </para>
/// <example>
/// <code>
/// builder.Services.AddSingleton(new TraconToolRegistration(refundTool)
/// {
///     RequiresApproval = true,
///     Effect = ToolEffect.Write,
/// });
/// </code>
/// </example>
/// </remarks>
public sealed class TraconToolRegistration
{
    /// <summary>Creates a new tool registration with default settings.</summary>
    /// <param name="function">
    /// The tool to register. An <see cref="AIFunction"/> runs on the server;
    /// an <see cref="AIFunctionDeclaration"/> that is not an
    /// <see cref="AIFunction"/> (declaration-only, produced by
    /// <c>AddClientTool</c>) runs on the caller instead.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The tool name does not follow the tool naming rules.</exception>
    public TraconToolRegistration(AIFunctionDeclaration function)
    {
        ArgumentNullException.ThrowIfNull(function);

        if (!ToolNameRules.IsValid(function.Name))
        {
            throw new TraconException($"Tool name '{function.Name}' is invalid. {ToolNameRules.Description}");
        }

        Function = function;
    }

    /// <summary>The registered tool.</summary>
    public AIFunctionDeclaration Function { get; }

    /// <summary>
    /// Whether explicit approval is required before the call. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Function"/> must be an <see cref="AIFunction"/> when this is
    /// <see langword="true"/>: approval defers a server-side call, and a
    /// client-side tool has no server-side body to approve.
    /// </remarks>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// The tool's source. <see langword="null"/> for tools defined in code;
    /// the server name for tools coming from a remote MCP server.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>The tool's effect class. Defaults to <see cref="ToolEffect.Read"/>.</summary>
    public ToolEffect Effect { get; init; }

    /// <summary>
    /// The permission name a caller must hold to call this tool, or
    /// <see langword="null"/> when the tool declares none.
    /// </summary>
    public string? RequiredPermission { get; init; }

    /// <summary>
    /// The longest duration this tool's call may run, or <see langword="null"/>
    /// to use the installation default.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Whether this tool's call may run again when an interrupted run is
    /// continued. Defaults to <see langword="false"/>.
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
    public bool SafeToRepeat { get; init; }

    /// <summary>
    /// The most bytes (UTF-8) this tool's result may carry, or
    /// <see langword="null"/> to use the installation default. A result over
    /// the limit is trimmed and handed to the model inside an envelope that
    /// states how many bytes were dropped.
    /// </summary>
    /// <remarks>
    /// Bounding the output inside the tool's own body is always better: the
    /// tool knows its data, this only counts bytes. This limit is the last
    /// defence for the day that bound is forgotten.
    /// </remarks>
    public int? MaxOutputBytes { get; init; }
}

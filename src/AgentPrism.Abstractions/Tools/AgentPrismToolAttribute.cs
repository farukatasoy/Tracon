namespace AgentPrism;

/// <summary>
/// Marks a method as a tool. <c>AddToolsFrom&lt;T&gt;()</c> registers only
/// methods carrying this attribute.
/// </summary>
/// <remarks>
/// <para>
/// Marking is an <strong>explicit choice</strong>. Adding a new public method
/// to a class does not automatically expose it to agents. This is the
/// natural continuation of design rule K2 (tools are defined only in code).
/// </para>
/// <para>
/// The description field tells the model <em>when</em> to call the tool; if
/// left empty, the model looks only at the name and parameter schema.
/// </para>
/// <example>
/// <code>
/// internal static class OrderTools
/// {
///     [AgentPrismTool("get_order_status", "Returns an order's shipping status.")]
///     public static string GetOrderStatus(string orderId) =&gt; ...;
///
///     public static void Helper() { }   // not a tool
/// }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AgentPrismToolAttribute : Attribute
{
    /// <summary>Creates a mark that uses the method name as the tool name.</summary>
    public AgentPrismToolAttribute()
    {
    }

    /// <summary>Creates a mark with an explicit tool name.</summary>
    /// <param name="name">The tool name. Used in agent definitions.</param>
    public AgentPrismToolAttribute(string name) => Name = name;

    /// <summary>Creates a mark with a name and a description.</summary>
    /// <param name="name">The tool name. Used in agent definitions.</param>
    /// <param name="description">The description telling the model when to call the tool.</param>
    public AgentPrismToolAttribute(string name, string description)
    {
        Name = name;
        Description = description;
    }

    /// <summary>
    /// The tool name. The method name is used if <see langword="null"/>.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// The description telling the model when to call the tool.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether explicit approval is required before the call.
    /// </summary>
    /// <remarks>
    /// A marked tool is wrapped with <c>ApprovalRequiredAIFunction</c> in the
    /// registry. Microsoft Agent Framework produces an approval request
    /// instead of running the tool; the call waits until the user's decision
    /// arrives. Mark tools that perform irreversible actions (cancellation,
    /// deletion, payment) this way.
    /// </remarks>
    public bool RequiresApproval { get; init; }

    /// <summary>The tool's effect class. Defaults to <see cref="ToolEffect.Read"/>.</summary>
    public ToolEffect Effect { get; init; }

    /// <summary>
    /// The permission name a caller must hold to call this tool, or
    /// <see langword="null"/> to declare none.
    /// </summary>
    /// <remarks>Checked by the registered <c>IToolAuthorizationHandler</c> before every call.</remarks>
    public string? RequiredPermission { get; init; }

    /// <summary>
    /// The longest duration this tool's call may run, in seconds. Zero (the
    /// default) uses the installation default instead of a fixed value.
    /// </summary>
    /// <remarks>
    /// An attribute argument cannot be a <see cref="TimeSpan"/>, so seconds is
    /// the unit; the registry converts it.
    /// </remarks>
    public int TimeoutSeconds { get; init; }
}

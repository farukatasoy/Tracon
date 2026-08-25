namespace AgentPrism;

/// <summary>
/// Marks a method as a tool. <c>AddToolsFrom&lt;T&gt;()</c> registers only
/// methods carrying this attribute.
/// </summary>
/// <remarks>
/// <para>
/// Marking is an <strong>explicit choice</strong>. Adding a new public method
/// to a class does not automatically expose it to agents. This is the
/// natural continuation of the code-only tools rule (tools are defined only in code).
/// </para>
/// <para>
/// The description field tells the model <em>when</em> to call the tool; if
/// left empty, the model looks only at the name and parameter schema.
/// </para>
/// <example>
/// <code>
/// internal class OrderTools
/// {
///     [AgentPrismTool("get_order_status", "Returns an order's shipping status.")]
///     public static string GetOrderStatus(string orderId) =&gt; "shipped";
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

    /// <summary>See <see cref="AgentPrismToolRegistration.SafeToRepeat"/>. Defaults to <see langword="false"/>.</summary>
    public bool SafeToRepeat { get; init; }

    /// <summary>
    /// The maximum UTF-8 byte count for this tool result. Zero uses the installation default.
    /// </summary>
    /// <remarks>
    /// This limit does not apply to <see cref="Microsoft.Extensions.AI.AIContent"/> attachments. They use
    /// the existing attachment contract instead of an inline text result.
    /// </remarks>
    public int MaxOutputBytes { get; init; }

    /// <summary>
    /// The source-generated JSON context for a complex tool result, or
    /// <see langword="null"/> when the result needs no JSON serialization.
    /// </summary>
    /// <remarks>
    /// Set this to a <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
    /// type that declares <c>[JsonSerializable(typeof(TResult))]</c>. The JSON
    /// source generator only sees source written by the tool owner; a context
    /// emitted by another source generator arrives too late for it to process.
    /// </remarks>
    public Type? JsonSerializerContext { get; init; }
}

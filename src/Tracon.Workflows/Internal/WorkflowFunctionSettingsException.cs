namespace Tracon;

/// <summary>
/// A function node's settings do not fit the settings type its function declares.
/// </summary>
/// <remarks>
/// A separate type so the compiler can tell this refusal apart from the other
/// reasons a node fails to build, and add what only it knows: the settings were
/// saved against an earlier shape of the type. The message is user-facing and
/// names the node and the member; it never carries the settings value.
/// </remarks>
internal sealed class WorkflowFunctionSettingsException : TraconException
{
    /// <summary>Creates a new error.</summary>
    /// <param name="message">The user-facing message. Must not carry a settings value.</param>
    public WorkflowFunctionSettingsException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a new error.</summary>
    /// <param name="message">The user-facing message. Must not carry a settings value.</param>
    /// <param name="innerException">The serializer's own error.</param>
    public WorkflowFunctionSettingsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

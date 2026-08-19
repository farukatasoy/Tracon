using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// Classifies the blast radius of a tool call.
/// </summary>
/// <remarks>
/// <para>
/// This is information, not a gate — <see cref="ToolDescriptor.RequiresApproval"/>
/// and <see cref="IToolAuthorizationHandler"/> are the gates. The value is read
/// by the UI (badge color), written to the audit trail, and can be used by a
/// consumer's <see cref="IToolAuthorizationHandler"/> as a scope key.
/// </para>
/// <para>
/// Written <strong>as a name</strong> in JSON, the same convention every
/// other UI-facing enum in this API follows (<see cref="RunEventType"/>,
/// <see cref="RunErrorClass"/>).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ToolEffect>))]
public enum ToolEffect
{
    /// <summary>No side effect.</summary>
    Read = 0,

    /// <summary>Changes persistent data.</summary>
    Write = 1,

    /// <summary>Cannot be undone.</summary>
    Destructive = 2,

    /// <summary>Data leaves the process (an external call, a notification, a payment).</summary>
    External = 3,
}

using System.Text.Json;

namespace Tracon;

/// <summary>
/// The settings of one function node in a workflow graph built in code, passed to
/// <see cref="WorkflowFunctionBinding.GetWorkflowFunction(System.IServiceProvider, string, Action{WorkflowFunctionStepOptions})"/>.
/// </summary>
/// <remarks>
/// A stored workflow carries the same two values in a <see cref="WorkflowNodeReference"/>; a graph built
/// in code has no node reference, so it chooses them here. A callback with properties rather than optional
/// parameters, for the reason <see cref="WorkflowFunctionOptions"/> gives.
/// </remarks>
public sealed class WorkflowFunctionStepOptions
{
    /// <summary>
    /// Gets or sets the longest time one call of the node may take. <see langword="null"/> (the default)
    /// uses the timeout the function was registered with, if any.
    /// </summary>
    /// <remarks>
    /// The same setting as <see cref="WorkflowNodeReference.Timeout"/>; it wins over
    /// <see cref="WorkflowFunctionOptions.Timeout"/>, and its remarks apply: the token the handler was
    /// given is cancelled when the time is up, and each attempt of a retried node has the whole time.
    /// Must be from one millisecond to about 49.7 days.
    /// </remarks>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the node's settings as a JSON object. <see langword="null"/> (the default) is a node
    /// without settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read into the settings type the function declares (<c>AddWorkflowFunction&lt;TInput, TOutput, TSettings&gt;</c>)
    /// with the strict read a stored node's settings get: an unknown field, a missing <c>required</c> member
    /// and a value of the wrong type are refused when the graph is built, naming the node and the member.
    /// A function that declares no settings refuses any.
    /// </para>
    /// <para>
    /// To pass a typed value instead of JSON, call <see cref="SetSettings{TSettings}(TSettings)"/>.
    /// </para>
    /// </remarks>
    public JsonElement? Settings { get; set; }

    /// <summary>Sets <see cref="Settings"/> from a typed value.</summary>
    /// <typeparam name="TSettings">The settings type the function declares.</typeparam>
    /// <param name="settings">The settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> is <see langword="null"/>.</exception>
    /// <exception cref="JsonException">The value cannot be written as JSON.</exception>
    /// <remarks>
    /// The value is written with the options the read uses, so it comes back as the object that was given
    /// and the check is the one a stored node's settings pass. A settings type with a <c>required</c> member
    /// is satisfied by the value, not by a default. Settings are values and never a secret: they appear in
    /// the run's audit trail.
    /// </remarks>
    public void SetSettings<TSettings>(TSettings settings)
        where TSettings : class
    {
        ArgumentNullException.ThrowIfNull(settings);

        Settings = WorkflowFunctionSettings.Serialize(settings);
    }
}

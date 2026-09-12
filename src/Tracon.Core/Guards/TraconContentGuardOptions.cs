namespace Tracon;

/// <summary>Settings for the content inspection pipeline.</summary>
/// <remarks>
/// <para>
/// Read from the <c>Tracon:ContentGuard</c> configuration section.
/// </para>
/// <para>
/// None of the settings in this class <strong>have any effect while no guard
/// is registered</strong>: the inspection wrapper is not added to the pipeline and
/// this object is never read. The the no-surprises rule (no surprises) gate is not a flag, it
/// <em>is</em> the registration itself — the built-in guard is added by an
/// explicit choice, either via <c>AddPatternContentGuard()</c> or by populating
/// the <c>Tracon:ContentGuard:Pattern</c> section.
/// </para>
/// </remarks>
public sealed class TraconContentGuardOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Tracon:ContentGuard";

    /// <summary>
    /// Inspects content sent to the model. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Tool results are covered too: a tool result enters the model on the
    /// <em>second</em> call and is seen because inspection sits at the
    /// <c>IChatClient</c> layer.
    /// </remarks>
    public bool InspectInput { get; set; } = true;

    /// <summary>
    /// Inspects content coming from the model. Default <see langword="true"/>.
    /// </summary>
    public bool InspectOutput { get; set; } = true;

    /// <summary>
    /// While output inspection is on, the streaming response is
    /// <strong>buffered</strong>. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A frame cannot be recalled once it has been sent to the client. A pattern
    /// does not match on a partial piece of text: <c>4539-</c> is seen and passes
    /// through before the card number is complete. Buffering costs the
    /// liveliness of the stream but inspects correctly.
    /// </para>
    /// <para>
    /// Setting this to <see langword="false"/> leaves output inspection
    /// <em>half done</em>: the guard only ever sees each frame on its own.
    /// Silently doing half an inspection is worse than doing none — the user
    /// believes they are protected. That is why the choice is an explicit
    /// setting, not a hidden behavior.
    /// </para>
    /// </remarks>
    public bool BufferStreamingOutput { get; set; } = true;
}

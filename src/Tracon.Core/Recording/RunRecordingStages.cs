namespace Tracon;

/// <summary>
/// The closed set of values written to <see cref="TraconDiagnostics.Tags.RecordingStage"/>,
/// paired with the sentence each one is logged as.
/// </summary>
/// <remarks>
/// The tag value is a contract a dashboard filters on, so it is a constant rather
/// than a sentence assembled at the call site. The human half lives here too, in
/// <see cref="Describe"/>: keeping them apart lets a new stage arrive with a tag and
/// no sentence, which is the failure mode a hand-repeated expression always has.
/// It is <c>internal</c> on purpose — a consumer reads these values off a metric, it
/// never writes them, and a public enum could not gain a member without breaking.
/// </remarks>
internal static class RunRecordingStages
{
    /// <summary>The run record could not be opened.</summary>
    public const string Start = "start";

    /// <summary>A single run event could not be appended.</summary>
    public const string Event = "event";

    /// <summary>A tool-invocation measurement could not be recorded.</summary>
    public const string ToolInvocation = "tool_invocation";

    /// <summary>The run record could not be closed.</summary>
    public const string Completion = "completion";

    /// <summary>A registered event sink threw and was dropped for this run.</summary>
    public const string Sink = "sink";

    /// <summary>The run's input messages could not be saved, so it cannot be replayed.</summary>
    public const string Input = "input";

    /// <summary>The sentence used when a value outside the set reaches <see cref="Describe"/>.</summary>
    /// <remarks>
    /// <see cref="Describe"/> cannot throw: it runs inside the swallow path whose whole
    /// purpose is that recording failures never interrupt a run. It falls back to this
    /// instead, and a test asserts no member of <see cref="All"/> ever reaches it.
    /// </remarks>
    public const string UnknownDescription = "a run record could not be written";

    /// <summary>Gets every stage value, in the order a run encounters them.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Start, Event, ToolInvocation, Completion, Sink, Input];

    /// <summary>Returns the log sentence that belongs to <paramref name="stage"/>.</summary>
    /// <param name="stage">One of the constants on this type.</param>
    /// <returns>The sentence, or <see cref="UnknownDescription"/> for an unknown value.</returns>
    public static string Describe(string stage) => stage switch
    {
        Start => "the run record could not be opened",
        Event => "a run event could not be written",
        ToolInvocation => "a tool invocation could not be recorded",
        Completion => "the run record could not be closed",
        Sink => "an event sink failed",
        Input => "the run input could not be saved",
        _ => UnknownDescription,
    };
}

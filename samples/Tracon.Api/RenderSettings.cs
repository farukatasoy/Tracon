namespace Tracon.Api;

/// <summary>
/// The settings of the <c>render</c> workflow function: how long the pretend render
/// takes.
/// </summary>
/// <remarks>
/// The function stands in for a step that takes minutes (an audio render, a video
/// encode), so a queued run can be stopped halfway through its slow step and its
/// recovery watched: <c>{"seconds":30}</c> on the node.
/// </remarks>
internal sealed record RenderSettings
{
    /// <summary>Gets how many seconds the render takes.</summary>
    public int Seconds { get; init; } = 5;
}

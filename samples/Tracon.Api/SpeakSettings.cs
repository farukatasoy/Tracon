namespace Tracon.Api;

/// <summary>
/// The settings of the <c>speak</c> workflow function: the shape the function's code
/// declares, and an operator fills in per workflow.
/// </summary>
/// <remarks>
/// Names are camelCase in JSON (<c>{"voice":"alloy","loudnessLufs":-14}</c>). An unknown
/// name, a missing <c>voice</c> and a value of the wrong type are refused when the workflow
/// is saved. Never a secret: a setting is stored in the workflow definition and shown to
/// everyone who can read it.
/// </remarks>
internal sealed record SpeakSettings
{
    /// <summary>Gets the name of the voice that reads the text.</summary>
    public required string Voice { get; init; }

    /// <summary>Gets the target loudness, in LUFS.</summary>
    public double LoudnessLufs { get; init; } = -16;

    /// <summary>
    /// Gets the NAME of the configuration key that holds the speech service's credential, or
    /// <see langword="null"/>. The key's name is a setting; the credential never is.
    /// </summary>
    public string? ApiKeyConfigurationKey { get; init; }
}

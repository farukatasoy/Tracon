namespace Tracon;

/// <summary>Settings for the voice tools.</summary>
/// <remarks>
/// The type <strong>cannot</strong> be a <c>record</c>: the compiler-generated
/// <c>ToString</c> writes every property, and a single <c>LogDebug("{Options}", o)</c>
/// call would leak <see cref="ApiKey"/> into the log.
/// <c>SecretLeakTests</c> verifies the type does not define its own <c>ToString</c>.
/// </remarks>
public sealed class VoiceOptions
{
    /// <summary>Default name of the configuration section.</summary>
    public const string SectionName = "Tracon:Voice";

    /// <summary>Default output format.</summary>
    /// <remarks>
    /// MP3 is chosen because the attachment store does type checking from the
    /// <strong>magic byte</strong>. <c>pcm_*</c> and <c>ulaw_*</c> outputs have no header
    /// and are rejected.
    /// </remarks>
    public const string DefaultOutputFormat = "mp3_44100_128";

    /// <summary>Provider name. Currently only <c>elevenlabs</c> is supported.</summary>
    public string Provider { get; set; } = VoiceProviderNames.ElevenLabs;

    /// <summary>
    /// The API key. A <strong>secret</strong>: it lives only in
    /// <c>dotnet user-secrets</c> or an environment variable.
    /// </summary>
    /// <remarks>
    /// The key's <em>value</em> is held here, not its <em>name</em>. The rule forbids
    /// writing secrets to the DATABASE; voice configuration never reaches the
    /// database, and the other four provider packages also carry a plain <c>ApiKey</c>.
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Base address. When empty, the provider's public address is used.
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Voice to use when the request gives no voice id.</summary>
    public string? DefaultVoiceId { get; set; }

    /// <summary>Default synthesis model.</summary>
    public string? SynthesisModelId { get; set; }

    /// <summary>Default transcription model.</summary>
    public string? TranscriptionModelId { get; set; }

    /// <summary>Output format.</summary>
    public string OutputFormat { get; set; } = DefaultOutputFormat;

    /// <summary>
    /// Maximum characters that can be spoken in a single request. Default 5,000.
    /// </summary>
    /// <remarks>
    /// When the limit is exceeded, the tool <strong>returns an error</strong>; it does
    /// not silently truncate the text. Truncation produces a sentence the user never
    /// hears, and the reason becomes invisible.
    /// </remarks>
    public int MaxCharactersPerRequest { get; set; } = 5000;

    /// <summary>How many voice requests can run at once. Default 2.</summary>
    public int MaxConcurrentRequests { get; set; } = 2;

    /// <summary>
    /// Whether voice tools require explicit approval before a call. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The tool causes no irreversible side effect — it produces a file and spends
    /// money. Cost can be a reason on its own; that is why the setting exists, off by default.
    /// </remarks>
    public bool RequireApproval { get; set; }

    /// <summary>Request timeout. 100 seconds when empty.</summary>
    public TimeSpan? Timeout { get; set; }
}

/// <summary>Names of known voice providers.</summary>
public static class VoiceProviderNames
{
    /// <summary>ElevenLabs. STABLE name: stored in usage records.</summary>
    public const string ElevenLabs = "elevenlabs";
}

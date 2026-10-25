namespace Tracon;

/// <summary>
/// The provider names that the <c>UseOpenAI()</c> call registers, and the
/// <see cref="ModelBinding.ProviderSettings"/> keys.
/// </summary>
/// <remarks>
/// These names are <strong>stable</strong>. Agent definitions are stored in the database
/// with these names; changing them breaks the stored definitions.
/// </remarks>
public static class OpenAIProviderNames
{
    /// <summary>Gets the provider that uses the Chat Completions API: <c>openai</c>.</summary>
    public const string ChatCompletions = "openai";

    /// <summary>Gets the provider that uses the Responses API: <c>openai-responses</c>.</summary>
    public const string Responses = "openai-responses";

    /// <summary>The prefix for provider-specific settings: <c>openai</c>.</summary>
    /// <remarks>
    /// One prefix covers both surfaces (<see cref="ChatCompletions"/> and
    /// <see cref="Responses"/>) and every provider that
    /// <c>UseOpenAICompatible()</c> registers, whatever name it is given: they
    /// all build their clients through the same OpenAI client library.
    /// </remarks>
    public const string SettingsPrefix = "openai";

    /// <summary>
    /// The setting key that asks OpenAI to enforce the response schema while the model generates:
    /// <c>openai.strictJsonSchema</c> (boolean).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <strong>off</strong>. With it on, the request carries <c>strict: true</c> on the response
    /// format of an agent whose <see cref="ModelBinding.ResponseFormat"/> is a JSON schema, and OpenAI refuses
    /// to produce a reply that breaks the schema. It applies to the response format only; the agent's tools
    /// stay non-strict.
    /// </para>
    /// <para>
    /// Strict or not, the OpenAI client rewrites the schema before it sends it: every object is closed, every
    /// property becomes required, and the bounds (<c>maxLength</c>, <c>pattern</c>, <c>minimum</c> and the
    /// like) move into the schema's <c>description</c> text. Strict mode therefore enforces the schema that
    /// <c>POST /api/agents/validate</c> reports under <c>responseSchemas</c>, not the one the author wrote. A
    /// property that may stay empty has to allow <c>null</c>: <c>"type": ["string", "null"]</c>.
    /// </para>
    /// <para>
    /// A server registered with <c>UseOpenAICompatible()</c> reads the same key. A server that does not know
    /// strict mode can refuse the request.
    /// </para>
    /// </remarks>
    public const string StrictJsonSchemaSetting = "openai.strictJsonSchema";

    /// <summary>The setting keys this provider supports: <see cref="StrictJsonSchemaSetting"/>.</summary>
    /// <remarks>
    /// <para>
    /// The settings that OpenAI and the compatible servers share (temperature, output limit, reasoning
    /// effort, response format) are fixed fields of <see cref="ModelBinding"/>; only a vendor-only setting
    /// is a key here.
    /// </para>
    /// <para>
    /// Any other key is rejected: <see cref="ModelProviderSettings.Validate(ModelBinding, string, IReadOnlyCollection{string})"/>
    /// names it and lists these. A key is never ignored silently: a setting that does nothing would leave
    /// the author without the behavior they configured.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> SupportedSettings { get; } = [StrictJsonSchemaSetting];
}

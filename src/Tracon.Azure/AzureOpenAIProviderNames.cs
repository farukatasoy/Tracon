namespace Tracon;

/// <summary>
/// The provider name registered by the <c>UseAzureOpenAI()</c> call, and the
/// <see cref="ModelBinding.ProviderSettings"/> keys.
/// </summary>
/// <remarks>
/// <para>
/// These names are <strong>stable</strong>. Agent definitions are stored in the
/// database under these names; changing them breaks stored definitions.
/// </para>
/// <para>
/// The name is <c>azure-openai</c>, not <c>azure</c>: the package may later
/// register another Azure model service, at which point the name <c>azure</c>
/// would no longer say what it refers to.
/// </para>
/// </remarks>
public static class AzureOpenAIProviderNames
{
    /// <summary>The provider that uses the Azure OpenAI v1 Chat Completions endpoint: <c>azure-openai</c>.</summary>
    public const string AzureOpenAI = "azure-openai";

    /// <summary>The prefix for provider-specific settings: <c>azure-openai</c>.</summary>
    public const string SettingsPrefix = "azure-openai";

    /// <summary>
    /// The setting key that asks Azure OpenAI to enforce the response schema while the model generates:
    /// <c>azure-openai.strictJsonSchema</c> (boolean).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <strong>off</strong>. With it on, the request carries <c>strict: true</c> on the response
    /// format of an agent whose <see cref="ModelBinding.ResponseFormat"/> is a JSON schema, and the service
    /// refuses to produce a reply that breaks the schema. It applies to the response format only; the agent's
    /// tools stay non-strict.
    /// </para>
    /// <para>
    /// Strict or not, the client rewrites the schema before it sends it: every object is closed, every
    /// property becomes required, and the bounds (<c>maxLength</c>, <c>pattern</c>, <c>minimum</c> and the
    /// like) move into the schema's <c>description</c> text. Strict mode therefore enforces the schema that
    /// <c>POST /api/agents/validate</c> reports under <c>responseSchemas</c>, not the one the author wrote. A
    /// property that may stay empty has to allow <c>null</c>: <c>"type": ["string", "null"]</c>.
    /// </para>
    /// <para>
    /// Strict mode needs a deployment of a model that supports structured outputs; a deployment that does
    /// not can refuse the request.
    /// </para>
    /// </remarks>
    public const string StrictJsonSchemaSetting = "azure-openai.strictJsonSchema";

    /// <summary>The setting keys this provider supports: <see cref="StrictJsonSchemaSetting"/>.</summary>
    /// <remarks>
    /// <para>
    /// The Azure-only request fields (data sources, and the like) were written through the Azure SDK's own
    /// request extensions, which threw <c>MissingMethodException</c> at run time against the OpenAI SDK
    /// version in use. The provider now talks to the resource's v1 endpoint through the plain OpenAI client,
    /// which has no counterpart for them. Offering a setting that doesn't work is worse than not offering it
    /// at all, so only a setting the plain client sends is a key here.
    /// </para>
    /// <para>
    /// Any other key is rejected: <see cref="ModelProviderSettings.Validate(ModelBinding, string, IReadOnlyCollection{string})"/>
    /// names it and lists these.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> SupportedSettings { get; } = [StrictJsonSchemaSetting];
}

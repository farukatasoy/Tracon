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
    /// <summary>The provider that uses the Azure OpenAI Chat Completions endpoint: <c>azure-openai</c>.</summary>
    public const string AzureOpenAI = "azure-openai";

    /// <summary>The prefix for provider-specific settings: <c>azure-openai</c>.</summary>
    public const string SettingsPrefix = "azure-openai";

    /// <summary>
    /// The setting keys this provider supports. <strong>Empty today.</strong>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list is deliberately empty. The custom fields Azure lets you add to
    /// a chat request (data sources, the <c>max_completion_tokens</c> key) are
    /// written through <c>Azure.AI.OpenAI.Chat.AzureChatExtensions</c>; it was
    /// measured that <strong>all</strong> of these extensions throw
    /// <c>MissingMethodException</c> at run time against the OpenAI SDK version
    /// we use. Offering a setting that doesn't work is worse than not offering
    /// it at all.
    /// </para>
    /// <para>
    /// Because the list is empty, <see cref="ModelProviderSettings.Validate"/>
    /// rejects any key and says "supports no extra settings" in its message.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> SupportedSettings { get; } = [];
}

using Azure.Core;

namespace Tracon;

/// <summary>Tracon's Azure OpenAI provider options.</summary>
/// <remarks>
/// <para>
/// This type is deliberately a <c>class</c>, not a <c>record</c>: the
/// <c>ToString</c> a <c>record</c> generates writes every property, and a
/// single log line would expose the API key.
/// </para>
/// <para>
/// Validation is done by hand inside <see cref="AzureOpenAIProviderOptionsValidator"/>;
/// <c>DataAnnotations</c> relies on reflection and breaks AOT compatibility.
/// </para>
/// </remarks>
public sealed class AzureOpenAIProviderOptions
{
    /// <summary>The full path of the configuration section options are read from.</summary>
    public const string SectionName = "Tracon:Providers:AzureOpenAI";

    /// <summary>
    /// The Azure OpenAI resource's address. Example:
    /// <c>https://my-resource.openai.azure.com/</c>.
    /// </summary>
    /// <remarks>
    /// Required; Azure has no single global address, each resource has its own.
    /// The address is not a secret, but it exposes an organization's topology;
    /// it is <strong>not shown</strong> in error messages or health-check detail.
    /// </remarks>
    public Uri? Endpoint { get; set; }

    /// <summary>The Azure OpenAI resource's API key.</summary>
    /// <remarks>
    /// <para>
    /// <strong>This value is a secret and is not written to a file.</strong> Use
    /// <c>dotnet user-secrets</c>, an environment variable, or a secret manager.
    /// </para>
    /// <para>
    /// When <see cref="CredentialFactory"/> is given, this field is
    /// <strong>not used</strong>. At least one of the two must be filled in.
    /// </para>
    /// </remarks>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The factory that produces a Microsoft Entra credential. When given, this
    /// is used instead of the API key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The factory deliberately comes from the consumer.</strong> The
    /// <c>DefaultAzureCredential</c> type needed for a managed credential lives
    /// in the <c>Azure.Identity</c> package; that package is not small and pulls
    /// in the <c>Microsoft.Identity.Client</c> chain too. <c>Tracon.Azure</c>
    /// does not take it as a dependency; it only binds to the <c>Azure.Core</c>
    /// abstraction and leaves the credential choice to the consumer.
    /// </para>
    /// <example>
    /// <code>
    /// // In the consumer's project: &lt;PackageReference Include="Azure.Identity" /&gt;
    /// var options = new AzureOpenAIProviderOptions
    /// {
    ///     CredentialFactory = static () =&gt; new DefaultAzureCredential(),
    /// };
    /// </code>
    /// </example>
    /// <para>
    /// The factory is called <strong>once</strong> while the client is built.
    /// The returned <see cref="TokenCredential"/> manages token renewal itself.
    /// </para>
    /// </remarks>
    public Func<TokenCredential>? CredentialFactory { get; set; }

    /// <summary>
    /// The <strong>deployment</strong> name used when <see cref="ModelBinding.Model"/>
    /// is left empty.
    /// </summary>
    /// <remarks>
    /// What Azure calls is not a model name, it is a <strong>deployment
    /// name</strong>. The same model can be deployed under different names, and
    /// the person who sets up the resource chooses the deployment name. A
    /// binding's <see cref="ModelBinding.Model"/> is therefore a deployment name
    /// too, and the model catalog lists deployments, not models.
    /// </remarks>
    public string? DefaultDeployment { get; set; }

    /// <summary>
    /// The token scope (audience) requested with the Entra credential.
    /// The Azure public cloud is used when <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Affects only the <see cref="CredentialFactory"/> path; it is not used on
    /// the API key path. Sovereign clouds (Azure Government, Azure China)
    /// require a different scope.
    /// </para>
    /// <para>
    /// Public cloud value: <c>https://cognitiveservices.azure.com/.default</c>.
    /// Azure Government: <c>https://cognitiveservices.azure.us/.default</c>.
    /// </para>
    /// </remarks>
    public string? Audience { get; set; }

    /// <summary>The upper time limit for a single request. The library default is used when <see langword="null"/>.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// The model catalog shown to the UI. Each entry's <c>Name</c> field is the
    /// <strong>deployment name</strong>.
    /// </summary>
    /// <remarks>
    /// Tracon carries no built-in model list; the catalog comes entirely
    /// from here. This list is <em>not a validation list</em>: a deployment name
    /// absent from it can still be used.
    /// </remarks>
    public IList<ModelDescriptor> Models { get; } = [];
}

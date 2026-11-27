using System.Drawing;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Generates images from the host's own code under the tenant's provider rules, with the provider failure masked and the spend
/// measured.
/// </summary>
/// <remarks>
/// <para>
/// A keyed <c>IImageGenerator</c> called directly skips everything Tracon applies to a provider call: the tenant's egress policy,
/// its own credential, the failure mask and the measurement. This service applies them, the same way the <c>generate_image</c> tool
/// and the operator endpoint do. It does not need <c>Tracon:Images:Enabled</c>: that switch offers images to agents and over HTTP,
/// and this call is the host's own.
/// </para>
/// <para>
/// <strong>The measurement.</strong> Inside a run (a step run opened with <see cref="ITraconStepRunner"/>, a workflow function node,
/// or a tool of an agent run) the call's spend is recorded as a non-token item of that run, which is part of the run's
/// <c>nonTokenCost</c>, its budget and the tenant's quota; <see cref="TraconImageResult.UsageRecorded"/> is then
/// <see langword="true"/>. Outside a run nothing is recorded, and the result still carries the measurement.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered by <c>AddTracon()</c> with <c>TryAdd</c>. Call this interface; do not
/// implement or replace it: the tenant checks and the measurement run inside it. To add a provider, register an image generator
/// with a <c>Use*Images</c> call or a keyed <c>IImageGenerator</c>.
/// </para>
/// <para>
/// <strong>Tenant behavior — AMBIENT tenant.</strong> The current run's tenant, or the current <c>ITenantContext</c> outside a run.
/// </para>
/// <para>
/// <strong>Delivery:</strong> no delivery guarantee applies. One call sends one request; a failed call is not repeated by this
/// service, but the provider client's own retries apply, and an image call is billed. The OpenAI and Azure OpenAI clients send a
/// call again after a retryable failure (such as <c>429</c> or a <c>5xx</c> answer) up to three times by default, so one failing
/// call can reach the provider four times. <c>Tracon:Images:Providers:{name}:MaxRetries</c> (<c>TraconImageProviderOptions.MaxRetries</c>) is a ceiling on
/// that count for image calls: <c>0</c> sends the call once. It only lowers the count, and chat calls keep the client's.
/// </para>
/// </remarks>
public interface IImageGenerationService
{
    /// <summary>Generates one or more images.</summary>
    /// <param name="request">The prompt, and optionally the count, size, provider, model and provider settings of this call.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The images as bytes, and what the call spent.</returns>
    /// <exception cref="ArgumentException">The prompt is blank, or the count or the size is not positive.</exception>
    /// <exception cref="TraconException">
    /// No provider or model can be selected, the count exceeds <c>Tracon:Images:MaxImagesPerRequest</c>, the provider is refused for
    /// the tenant (<see cref="TraconTenantProviderRefusedException"/>), a setting in <see cref="TraconImageRequest.ProviderSettings"/>
    /// is not supported by the provider or is of the wrong type, the provider failed (a <c>ProviderInvocationException</c>
    /// from Tracon's core package, with the status and whether a retry can help), the call ran longer than
    /// <c>Tracon:Images:Timeout</c>, or the provider returned no image.
    /// </exception>
    /// <remarks>
    /// An image the provider returns as an address is downloaded through Tracon's outbound-network guard. The measurement is
    /// reported before that download: when the provider has drawn, the money is spent even if the download then fails.
    /// </remarks>
    /// <example>
    /// <code>
    /// app.MapPost("/cover", async (IImageGenerationService images, CancellationToken cancellationToken) =>
    /// {
    ///     var result = await images.GenerateAsync(
    ///         new TraconImageRequest { Prompt = "A lighthouse at dawn", Size = new Size(1024, 1024) },
    ///         cancellationToken);
    ///
    ///     return Results.Bytes(result.Images[0].Data.ToArray(), result.Images[0].MediaType);
    /// });
    /// </code>
    /// </example>
    ValueTask<TraconImageResult> GenerateAsync(TraconImageRequest request, CancellationToken cancellationToken = default);
}

/// <summary>One image call of the host's own code.</summary>
public sealed record TraconImageRequest
{
    /// <summary>Gets what to draw.</summary>
    public required string Prompt { get; init; }

    /// <summary>Gets how many images to ask for. Default 1; at most <c>Tracon:Images:MaxImagesPerRequest</c>.</summary>
    public int Count { get; init; } = 1;

    /// <summary>Gets the size, or <see langword="null"/> for the provider's own. Not every provider takes one.</summary>
    public Size? Size { get; init; }

    /// <summary>
    /// Gets the image provider (<c>openai</c>, <c>azure-openai</c>, <c>google</c>), or <see langword="null"/> for the default
    /// (<c>Tracon:Images:Provider</c>, or the only registered one).
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>Gets the model or Azure deployment, or <see langword="null"/> for the provider's configured model.</summary>
    public string? Model { get; init; }

    /// <summary>
    /// Gets the provider settings of this call, keyed under the provider's prefix (for example <c>openai.quality</c>). Empty by
    /// default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The type is the one of <c>TraconImageProviderOptions.ProviderSettings</c>, and the keys are the same. The call's
    /// settings are laid over the ones registered for the provider: a key in both takes the call's value for this call only, and
    /// the next call starts from the registered ones again. A key the provider does not support, a key of another provider and a
    /// value of the wrong type are refused with a <see cref="TraconException"/> before the provider is called, as nothing is
    /// ignored silently. The message names the key, never the value.
    /// </para>
    /// <para>
    /// Code sets it. The <c>generate_image</c> tool and <c>POST /api/images/generate</c> send none and cannot be given any:
    /// the quality of an image changes what the call costs, and a model or a client must not choose it.
    /// </para>
    /// <para>
    /// <strong>The measured cost does not follow the quality.</strong> A flat <c>PerImage</c> price in <c>Tracon:Pricing:Images</c>
    /// is one price for every quality: a <c>high</c> call and a <c>low</c> call are recorded at the same cost. A host that varies
    /// the quality per call prices a token-billed image model by tokens (<c>OutputCostPerMillionTokens</c>): the token count is the
    /// one the provider reports for that call.
    /// </para>
    /// <para>
    /// A <see langword="with"/> copy of the request shares this dictionary with the original, and record equality compares it by
    /// reference, so two requests with the same settings in two dictionaries are not equal.
    /// </para>
    /// <example>
    /// <code>
    /// var request = new TraconImageRequest
    /// {
    ///     Prompt = "A lighthouse at dawn",
    ///     ProviderSettings = { ["openai.quality"] = JsonSerializer.SerializeToElement("high") },
    /// };
    /// </code>
    /// </example>
    /// </remarks>
    public IDictionary<string, JsonElement> ProviderSettings { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>What one image call returned and spent.</summary>
public sealed record TraconImageResult
{
    /// <summary>Gets the images, as bytes with their media type.</summary>
    public required IReadOnlyList<DataContent> Images { get; init; }

    /// <summary>
    /// Gets the measurement: the provider and model that drew, the unit (images or tokens) and the quantity, and the cost under
    /// <c>Tracon:Pricing:Images</c>, <see langword="null"/> when no price is configured.
    /// </summary>
    public required ToolCallUsage Usage { get; init; }

    /// <summary>
    /// Gets whether the measurement was reported to a run. <see langword="false"/> outside a run, and when the run already holds
    /// the most items it can.
    /// </summary>
    /// <remarks>
    /// A store that cannot write the item does not change it: the failure is logged and the amount still counts toward the run's
    /// quota entry and webhook. A call that ends after its run has closed is still reported, and its amount stays in the run's
    /// items only: the run's total, its budget and its quota entry were written when it closed.
    /// </remarks>
    public required bool UsageRecorded { get; init; }
}

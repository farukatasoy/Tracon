using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Puts the key a provider was set up with out of the failures of an image call.
/// </summary>
/// <remarks>
/// <para>
/// The chat pipeline replaces the key in a failure before anything writes it (<see cref="ProviderFailureTaggingChatClient"/>).
/// An image generator does not go through that pipeline, and the telemetry ring Tracon puts around it logs the exception it
/// sees and copies its message to the span. A provider SDK that puts the provider's body into the exception (the Google SDK
/// does: <c>API key not valid: &lt;the key&gt;</c>) then writes the quoted key into the log and the span.
/// </para>
/// <para>
/// The wrapper sits closest to the provider's generator, inside that ring. A failure the generator itself threw becomes a
/// <see cref="ProviderInvocationException"/>: a sentence Tracon chose, the status, and an inner exception whose text has the
/// key replaced. The ring, the endpoint and the tool above it all see that one. A failure Tracon wrote
/// (<see cref="TraconException"/>) and a cancellation pass through as they are.
/// </para>
/// <para>
/// Only a provider Tracon knows (one the model provider registry holds under that name) is wrapped: its setup keys are what
/// the mask replaces, and the registry is the one place that reads them. A generator a consumer registers under a name of its own is left alone. The speech clients need no wrapper: the
/// OpenAI transport and the Gemini synthesizer both throw a <see cref="TraconException"/> that keeps the service's text out.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
internal static class ProviderFailureMasking
{
    /// <summary>Wraps an image generator.</summary>
    /// <param name="inner">The provider's generator.</param>
    /// <param name="services">The container that holds the model provider registry.</param>
    /// <param name="providerName">The provider the generator belongs to.</param>
    /// <returns>The wrapper, or <paramref name="inner"/> when no provider of that name is registered.</returns>
    internal static IImageGenerator Wrap(IImageGenerator inner, IServiceProvider services, string? providerName)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(services);

        if (string.IsNullOrWhiteSpace(providerName)
            || services.GetService<IModelProviderRegistry>() is not ModelProviderRegistry registry
            || !registry.TrySetupKeyMask(providerName, out var canonicalName, out var mask))
        {
            return inner;
        }

        return new MaskingImageGenerator(inner, canonicalName, mask);
    }

    private sealed class MaskingImageGenerator(IImageGenerator inner, string provider, ProviderSecretMask mask) : DelegatingImageGenerator(inner)
    {
        public override async Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.GenerateAsync(request, options, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested
                && exception is not TraconException
                && ProviderFailureNormalizer.ShouldNormalize(exception))
            {
                throw ProviderInvocationException.UpstreamFailure(exception, provider, mask);
            }
        }
    }
}
#pragma warning restore MEAI001

using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>The host's own image calls: selected, refused or allowed for the tenant, masked, measured and recorded.</summary>
#pragma warning disable MEAI001
internal sealed class ImageGenerationService : IImageGenerationService
{
    private readonly ImageProviderSelection _selection;
    private readonly ImageGeneratorResolver _resolver;
    private readonly ImageAttachmentWriter _contents;
    private readonly ImagePricing _pricing;
    private readonly ITenantContext _tenants;
    private readonly TimeProvider _timeProvider;

    public ImageGenerationService(
        ImageProviderSelection selection,
        ImageGeneratorResolver resolver,
        ImageAttachmentWriter contents,
        ImagePricing pricing,
        ITenantContext tenants,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(pricing);
        ArgumentNullException.ThrowIfNull(tenants);

        _selection = selection;
        _resolver = resolver;
        _contents = contents;
        _pricing = pricing;
        _tenants = tenants;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<TraconImageResult> GenerateAsync(TraconImageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("The image request has no prompt.", nameof(request));
        }

        if (request.Count < 1)
        {
            throw new ArgumentException("The image count must be positive.", nameof(request));
        }

        if (request.Size is { } requested && (requested.Width < 1 || requested.Height < 1))
        {
            throw new ArgumentException("The image size must be positive.", nameof(request));
        }

        var options = _selection.Options;

        if (request.Count > options.MaxImagesPerRequest)
        {
            throw new TraconException(
                $"The image count is {request.Count}; the limit is {options.MaxImagesPerRequest}. " +
                "Request fewer images or raise the `Tracon:Images:MaxImagesPerRequest` setting.");
        }

        // One read: the provider, its model and its settings belong together. The call's own settings are laid over the registered
        // ones and checked here, before the tenant checks and the provider: a refused setting sends nothing anywhere.
        var chosen = _selection.Select(request.Provider, request.Model, request.ProviderSettings);

        // The tenant of the run the call is part of; outside a run, the request's. Its provider checks come before the call.
        var tenantId = TraconRunContext.Current?.TenantId ?? _tenants.TenantId;
        var generator = await _resolver.ResolveForTenantAsync(tenantId, chosen.Provider, cancellationToken).ConfigureAwait(false);

        using var deadline = new CancellationTokenSource(options.Timeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            var response = await generator
                .GenerateAsync(new ImageGenerationRequest(request.Prompt), chosen.ToOptions(request.Count, request.Size), linked.Token)
                .ConfigureAwait(false);

            if (response.Contents.Count == 0)
            {
                throw new TraconException("The image provider returned no image content.");
            }

            var usage = Measure(chosen, request, response);

            // Recorded BEFORE a temporary address is downloaded: the money is spent even when the download then fails.
            var recorded = await NonTokenUsageReporter.ReportAsync(nodeName: null, usage).ConfigureAwait(false);
            var images = await _contents.MaterializeAsync(response.Contents, linked.Token).ConfigureAwait(false);

            return new TraconImageResult { Images = images, Usage = usage, UsageRecorded = recorded };
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw ProviderInvocationException.FromTransport(
                chosen.Provider,
                $"The image generation ran longer than {TraconImageOptions.SectionName}:Timeout.",
                new TimeoutException("The image generation timed out.", exception));
        }
    }

    private ToolCallUsage Measure(ImageSelection chosen, TraconImageRequest request, ImageGenerationResponse response)
    {
        var sizeKey = request.Size is { } size
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{size.Width}x{size.Height}")
            : null;
        var measured = _pricing.Measure(
            chosen.Provider,
            chosen.Model,
            sizeKey,
            response.Contents.Count,
            response.Usage?.OutputTokenCount);

        return new ToolCallUsage
        {
            Unit = measured.Unit,
            Quantity = measured.Quantity,
            Cost = measured.Cost,
            Currency = measured.Currency,
            Provider = chosen.Provider,
            Model = chosen.Model,
        };
    }
}
#pragma warning restore MEAI001

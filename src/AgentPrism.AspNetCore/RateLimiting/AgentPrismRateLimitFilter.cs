using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Applies a fixed-window rate limit to AgentPrism's own endpoint group.
/// </summary>
/// <remarks>
/// <para>
/// AgentPrism <strong>does not write</strong> its own middleware: it builds a thin
/// layer over .NET's <see cref="PartitionedRateLimiter"/> type.
/// <c>System.Threading.RateLimiting</c> comes from the ASP.NET Core shared framework;
/// there is no extra package dependency (measured in 21.1).
/// </para>
/// <para>
/// If the consumer already uses <c>AddRateLimiter()</c>, AgentPrism does not
/// compete with it: this filter applies only to AgentPrism's endpoint group and
/// does not change the consumer's overall limit.
/// </para>
/// </remarks>
internal sealed class AgentPrismRateLimitFilter : IEndpointFilter, IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;
    private readonly IOptionsMonitor<AgentPrismRateLimitOptions> _optionsMonitor;

    /// <summary>Creates a new rate limit filter.</summary>
    /// <param name="optionsMonitor">The rate limit settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> is <see langword="null"/>.</exception>
    public AgentPrismRateLimitFilter(IOptionsMonitor<AgentPrismRateLimitOptions> optionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _optionsMonitor = optionsMonitor;

        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var options = optionsMonitor.CurrentValue;
            var key = ResolvePartitionKey(context, options);

            return RateLimitPartitionExtensions.CreateFixedWindow(key, options);
        });
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var options = _optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            return await next(context).ConfigureAwait(false);
        }

        var httpContext = context.HttpContext;

        using var lease = await _limiter.AcquireAsync(httpContext, permitCount: 1, httpContext.RequestAborted)
            .ConfigureAwait(false);

        if (lease.IsAcquired)
        {
            return await next(context).ConfigureAwait(false);
        }

        // Retry-After is the wait time reported by the limiter; if it reports none,
        // the full window is used. The client should not have to guess when to retry.
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata)
            ? metadata
            : options.Window;

        httpContext.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        return Results.Problem(
            title: "Rate limit exceeded",
            detail: string.Create(
                CultureInfo.InvariantCulture,
                $"This setup allows at most {options.PermitLimit} requests per {options.Window.TotalSeconds:0} seconds. " +
                $"Retry after {retryAfter.TotalSeconds:0} seconds."),
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    /// <inheritdoc />
    public void Dispose() => _limiter.Dispose();

    private static string ResolvePartitionKey(HttpContext context, AgentPrismRateLimitOptions options)
    {
        if (options.Partition == RateLimitPartitionKind.Global)
        {
            return "__global";
        }

        // The tenant context is a singleton and reads the HTTP context; it cannot be
        // resolved outside a request, so it is resolved here.
        var tenants = context.RequestServices.GetService<ITenantContext>();

        return tenants?.TenantId ?? "__default";
    }
}

/// <summary>Helper that keeps fixed-window partition setup in one place.</summary>
internal static class RateLimitPartitionExtensions
{
    /// <summary>Builds a fixed-window partition with the given settings.</summary>
    /// <param name="key">The partition key.</param>
    /// <param name="options">The rate limit settings.</param>
    /// <returns>The partition definition.</returns>
    public static System.Threading.RateLimiting.RateLimitPartition<string> CreateFixedWindow(
        string key,
        AgentPrismRateLimitOptions options)
        => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, options.PermitLimit),
                Window = options.Window > TimeSpan.Zero ? options.Window : TimeSpan.FromMinutes(1),
                QueueLimit = Math.Max(0, options.QueueLimit),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
}

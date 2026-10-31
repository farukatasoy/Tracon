using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Applies a fixed-window rate limit to Tracon's own endpoint group.
/// </summary>
/// <remarks>
/// <para>
/// Where the window is counted follows <see cref="TraconRateLimitOptions.Scope"/>.
/// <see cref="RateLimitScope.Instance"/> (the default) builds a thin layer over .NET's
/// <see cref="PartitionedRateLimiter"/> type: <c>System.Threading.RateLimiting</c> comes from
/// the ASP.NET Core shared framework, so there is no extra package dependency (measured).
/// <see cref="RateLimitScope.Deployment"/> counts in the <see cref="IRateLimitCounterStore"/>
/// through <see cref="DeploymentRateLimiter"/>, and falls back to the same
/// <see cref="PartitionedRateLimiter"/> for the requests during which the store fails.
/// </para>
/// <para>
/// If the consumer already uses <c>AddRateLimiter()</c>, Tracon does not
/// compete with it: this filter applies only to Tracon's endpoint group and
/// does not change the consumer's overall limit.
/// </para>
/// <para>
/// The management API group and the AG-UI group each get a filter, and each filter keeps its
/// own counter: an AG-UI request does not spend the management API's window.
/// </para>
/// </remarks>
internal sealed class TraconRateLimitFilter : IEndpointFilter, IDisposable
{
    private readonly string _group;
    private readonly PartitionedRateLimiter<HttpContext> _limiter;
    private readonly IOptionsMonitor<TraconRateLimitOptions> _optionsMonitor;
    private readonly DeploymentRateLimiter _deployment;

    /// <summary>Creates a new rate limit filter.</summary>
    /// <param name="group">The endpoint group, one of <see cref="RateLimitGroups"/>.</param>
    /// <param name="optionsMonitor">The rate limit settings.</param>
    /// <param name="deployment">The shared counter used when the scope is <see cref="RateLimitScope.Deployment"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public TraconRateLimitFilter(
        string group,
        IOptionsMonitor<TraconRateLimitOptions> optionsMonitor,
        DeploymentRateLimiter deployment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(deployment);

        _group = group;
        _optionsMonitor = optionsMonitor;
        _deployment = deployment;

        _limiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var options = optionsMonitor.CurrentValue;
            var key = ResolveLocalPartitionKey(context, options);

            return RateLimitPartitionExtensions.CreateFixedWindow(key, options);
        });
    }

    /// <summary>
    /// Creates the filter of the management API group (<c>MapTracon</c>).
    /// </summary>
    /// <param name="services">The application services.</param>
    /// <returns>The filter, or <see langword="null"/> when the rate limit settings are not registered.</returns>
    public static TraconRateLimitFilter? ForManagementApi(IServiceProvider services)
        => Create(services, RateLimitGroups.Management);

    /// <summary>
    /// Creates the filter of the AG-UI group (<c>MapTraconAGUI</c>). Its counter is not the management API's.
    /// </summary>
    /// <param name="services">The application services.</param>
    /// <returns>The filter, or <see langword="null"/> when the rate limit settings are not registered.</returns>
    public static TraconRateLimitFilter? ForAgUi(IServiceProvider services)
        => Create(services, RateLimitGroups.AgUi);

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

        if (options.Scope == RateLimitScope.Deployment)
        {
            var decision = await _deployment
                .TryCountAsync(
                    ResolveDeploymentPartitionKey(httpContext, options),
                    FixedWindowDecision.EffectivePermitLimit(options),
                    FixedWindowDecision.EffectiveWindow(options),
                    RateLimiterKind.Endpoint,
                    httpContext.RequestAborted)
                .ConfigureAwait(false);

            // A null decision is a store failure: the local limiter below decides for this request.
            if (decision is { } counted)
            {
                return counted.Allowed
                    ? await next(context).ConfigureAwait(false)
                    : TooManyRequests(httpContext, options, counted.RetryAfter);
            }
        }

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

        return TooManyRequests(httpContext, options, retryAfter);
    }

    /// <inheritdoc />
    public void Dispose() => _limiter.Dispose();

    private static TraconRateLimitFilter? Create(IServiceProvider services, string group)
    {
        if (services.GetService<IOptionsMonitor<TraconRateLimitOptions>>() is not { } options)
        {
            return null;
        }

        var deployment = services.GetRequiredService<DeploymentRateLimiter>();

        // Mapping runs at start: a Deployment scope with a counter that only this process can see is
        // told once, here, not on the first request.
        if (options.CurrentValue.Scope == RateLimitScope.Deployment)
        {
            deployment.WarnWhenProcessLocal();
        }

        return new TraconRateLimitFilter(group, options, deployment);
    }

    private static IResult TooManyRequests(HttpContext httpContext, TraconRateLimitOptions options, TimeSpan retryAfter)
    {
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

    private RateLimitPartitionKey ResolveDeploymentPartitionKey(HttpContext context, TraconRateLimitOptions options)
        => options.Partition == RateLimitPartitionKind.Global
            ? RateLimitPartitionKey.ForGlobal(_group)
            : RateLimitPartitionKey.ForTenant(_group, ResolveTenantId(context));

    private static string ResolveLocalPartitionKey(HttpContext context, TraconRateLimitOptions options)
    {
        if (options.Partition == RateLimitPartitionKind.Global)
        {
            return "__global";
        }

        return context.RequestServices.GetService<ITenantContext>()?.TenantId ?? "__default";
    }

    private static string ResolveTenantId(HttpContext context)
    {
        // The tenant context is a singleton and reads the HTTP context; it cannot be
        // resolved outside a request, so it is resolved here.
        var tenantId = context.RequestServices.GetService<ITenantContext>()?.TenantId;

        return string.IsNullOrWhiteSpace(tenantId) ? "__default" : tenantId;
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
        TraconRateLimitOptions options)
        => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = FixedWindowDecision.EffectivePermitLimit(options),
                Window = FixedWindowDecision.EffectiveWindow(options),
                QueueLimit = Math.Max(0, options.QueueLimit),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            });
}

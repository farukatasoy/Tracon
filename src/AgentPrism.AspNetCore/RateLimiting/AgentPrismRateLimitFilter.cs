using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// AgentPrism'in kendi uc grubuna sabit pencereli bir hiz siniri uygular.
/// </summary>
/// <remarks>
/// <para>
/// AgentPrism kendi middleware'ini <strong>yazmaz</strong>: .NET'in
/// <see cref="PartitionedRateLimiter"/> tipi uzerine ince bir katman kurar.
/// <c>System.Threading.RateLimiting</c> ASP.NET Core paylasilan cercevesinden
/// gelir; ek bir paket bagimliligi yoktur (21.1'in olcumu).
/// </para>
/// <para>
/// Tuketici zaten <c>AddRateLimiter()</c> kullaniyorsa AgentPrism onunla
/// yarismaz: bu filtre yalnizca AgentPrism'in uc grubuna uygulanir ve
/// tuketicinin genel sinirini degistirmez.
/// </para>
/// </remarks>
internal sealed class AgentPrismRateLimitFilter : IEndpointFilter, IDisposable
{
    private readonly PartitionedRateLimiter<HttpContext> _limiter;
    private readonly IOptionsMonitor<AgentPrismRateLimitOptions> _optionsMonitor;

    /// <summary>Yeni bir hiz siniri filtresi olusturur.</summary>
    /// <param name="optionsMonitor">Hiz siniri ayarlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="optionsMonitor"/> <see langword="null"/> ise.</exception>
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

        // Retry-After, sinirlayicinin bildirdigi bekleme suresidir; bildirmiyorsa
        // pencerenin tamami kullanilir. Istemcinin ne zaman tekrar deneyecegini
        // tahmin etmesi gerekmemelidir.
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

        // Kiraci baglami singleton'dir ve HTTP baglamini okur; istek disinda
        // cozulemez, bu yuzden burada cozulur.
        var tenants = context.RequestServices.GetService<ITenantContext>();

        return tenants?.TenantId ?? "__default";
    }
}

/// <summary>Sabit pencereli bolum kurulumunu tek yerde tutan yardimci.</summary>
internal static class RateLimitPartitionExtensions
{
    /// <summary>Verilen ayarlarla sabit pencereli bir bolum kurar.</summary>
    /// <param name="key">Bolum anahtari.</param>
    /// <param name="options">Hiz siniri ayarlari.</param>
    /// <returns>Bolum tanimi.</returns>
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

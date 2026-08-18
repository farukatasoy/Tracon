using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Wires CORS support for the AgentPrism endpoint group, without requiring
/// the consumer to call <c>services.AddCors()</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <c>app.UseCors(...)</c> cannot be used here. It resolves
/// <c>ICorsService</c> from DI (measured: without <c>AddCors()</c>,
/// <c>Build()</c> throws <c>InvalidOperationException</c> at host startup),
/// but <see cref="AgentPrismEndpointRouteBuilderExtensions.MapAgentPrism"/>
/// runs AFTER <c>app.Build()</c>, when the container is sealed and
/// <see cref="AgentPrismEndpointOptions.AllowedOrigins"/> is not yet known
/// (K1 — no separate registration step). This mirrors K-251: an
/// after-<c>Build()</c> extension cannot register new DI services.
/// </para>
/// <para>
/// The fix (measured against a real HTTP round trip, including a preflight
/// request): <see cref="CorsService"/> and <see cref="CorsMiddleware"/>
/// are constructed directly, not through DI. <see cref="CorsService"/>'s
/// public constructor only needs <c>IOptions&lt;CorsOptions&gt;</c> (a
/// throwaway default instance — no named policy is used) and
/// <see cref="ILoggerFactory"/>, which is already resolvable from the
/// SEALED container (reading an existing registration, not adding one).
/// </para>
/// </remarks>
internal static class AgentPrismCorsMiddleware
{
    /// <summary>
    /// Adds the CORS middleware to the pipeline if any origin is allowed.
    /// </summary>
    /// <param name="endpoints">The application's routing builder.</param>
    /// <param name="options">The endpoint options carrying <see cref="AgentPrismEndpointOptions.AllowedOrigins"/>.</param>
    /// <param name="prefix">The normalized path prefix. Only requests under this prefix are affected.</param>
    public static void Map(IEndpointRouteBuilder endpoints, AgentPrismEndpointOptions options, string prefix)
    {
        if (options.AllowedOrigins.Count == 0 || endpoints is not IApplicationBuilder app)
        {
            return;
        }

        var loggerFactory = endpoints.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var corsService = new CorsService(Options.Create(new CorsOptions()), loggerFactory);

        var policy = new CorsPolicyBuilder([.. options.AllowedOrigins])
            .AllowAnyMethod()
            .AllowAnyHeader()
            .Build();

        var policyProvider = new FixedCorsPolicyProvider(policy);

        app.Use(next =>
        {
            var corsMiddleware = new CorsMiddleware(next, corsService, loggerFactory);

            return httpContext => httpContext.Request.Path.StartsWithSegments(prefix, StringComparison.Ordinal)
                ? corsMiddleware.Invoke(httpContext, policyProvider)
                : next(httpContext);
        });
    }

    /// <summary>Always returns the same fixed policy, regardless of the requested policy name.</summary>
    private sealed class FixedCorsPolicyProvider(CorsPolicy policy) : ICorsPolicyProvider
    {
        public Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName) => Task.FromResult<CorsPolicy?>(policy);
    }
}

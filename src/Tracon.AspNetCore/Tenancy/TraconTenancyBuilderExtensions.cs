using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

/// <summary>
/// Extensions that connect multi-tenancy to the HTTP request.
/// </summary>
public static class TraconTenancyBuilderExtensions
{
    /// <summary>
    /// Resolves the tenant from the current HTTP request. If not called, Tracon
    /// runs single-tenant and no extra configuration is needed.
    /// </summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="configure">The resolution settings.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The <strong>claim is preferred</strong> as the tenant source; the header
    /// path must be opened explicitly and, because it can be spoofed, should only
    /// be used inside a trusted network. Details:
    /// <see cref="TraconTenancyOptions"/>.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseOpenAI(apiKey)
    ///        .UsePostgreSql(connectionString)
    ///        .UseTenancy(options =>
    ///        {
    ///            options.Enabled = true;
    ///            options.ClaimType = "tenant_id";
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseTenancy(
        this ITraconBuilder builder,
        Action<TraconTenancyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<TraconTenancyOptions>();
        services.Configure(configure);
        services.AddHttpContextAccessor();

        // Replace, NOT TryAdd: AddTracon() runs earlier in the chain and has
        // already registered SingleTenantContext.
        services.Replace(ServiceDescriptor.Singleton<ITenantContext, HttpTenantContext>());

        // Phase 170: the production profile gate's tenant question is answered
        // in Core by WHICH ITenantContext is bound, which this call has just
        // changed. That answer is wrong for Enabled=false, so the refinement is
        // registered exactly here - the one composition where it has something
        // to add. It is only ever resolved if the application also called
        // RequireProductionProfile.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProductionProfileCheck, TenancyResolutionProfileCheck>());

        return builder;
    }
}

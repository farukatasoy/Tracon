using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>
/// Extensions that connect multi-tenancy to the HTTP request.
/// </summary>
public static class AgentPrismTenancyBuilderExtensions
{
    /// <summary>
    /// Resolves the tenant from the current HTTP request. If not called, AgentPrism
    /// runs single-tenant and no extra configuration is needed.
    /// </summary>
    /// <param name="builder">The AgentPrism configuration chain.</param>
    /// <param name="configure">The resolution settings.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The <strong>claim is preferred</strong> as the tenant source; the header
    /// path must be opened explicitly and, because it can be spoofed, should only
    /// be used inside a trusted network. Details:
    /// <see cref="AgentPrismTenancyOptions"/>.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
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
    public static IAgentPrismBuilder UseTenancy(
        this IAgentPrismBuilder builder,
        Action<AgentPrismTenancyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<AgentPrismTenancyOptions>();
        services.Configure(configure);
        services.AddHttpContextAccessor();

        // Replace, NOT TryAdd: AddAgentPrism() runs earlier in the chain and has
        // already registered SingleTenantContext.
        services.Replace(ServiceDescriptor.Singleton<ITenantContext, HttpTenantContext>());

        return builder;
    }
}

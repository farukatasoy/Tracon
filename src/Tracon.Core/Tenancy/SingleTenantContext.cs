using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The default tenant context for single-tenant deployments. It always returns
/// <see cref="TraconOptions.DefaultTenantId"/>.
/// </summary>
/// <remarks>
/// In multi-tenant scenarios, the consumer registers its <see cref="ITenantContext"/>
/// implementation <em>before</em> calling <c>AddTracon()</c>. Tracon uses
/// <c>TryAdd</c>, so the consumer registration wins.
/// </remarks>
public sealed class SingleTenantContext : ITenantContext
{
    private readonly IOptions<TraconOptions> _options;

    /// <summary>Initializes a new single-tenant context.</summary>
    /// <param name="options">The Tracon options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public SingleTenantContext(IOptions<TraconOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    /// <remarks>
    /// When <see cref="AmbientTenantScope.Current"/> is set, for example while a
    /// scheduled job runs, its value takes precedence over the default.
    /// </remarks>
    public string TenantId => AmbientTenantScope.Current ?? _options.Value.DefaultTenantId;
}

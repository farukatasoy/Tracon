namespace Tracon;

/// <summary>
/// Computes the cost from a model+usage pair. Pricing order: the model
/// catalog, then the <c>Tracon:Pricing</c> configuration.
/// </summary>
/// <remarks>
/// <strong>Tenant behavior — TENANT-INDEPENDENT.</strong> <see cref="Resolve"/>
/// is a pure function of provider, model and usage; the pricing catalog and
/// configuration it reads from are global, not per-tenant, so the same price
/// applies regardless of which tenant's run is being costed.
/// </remarks>
public interface IRunPricingResolver
{
    /// <summary>Resolves the cost.</summary>
    /// <param name="provider">
    /// The provider name. If <see langword="null"/> (example: recomputing a
    /// historical row), the price is resolved by model name alone, using the
    /// first match across providers.
    /// </param>
    /// <param name="model">The model name. Cost does not apply if <see langword="null"/> or empty.</param>
    /// <param name="usage">The token usage. Cost does not apply if <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="null"/> only if <paramref name="model"/> or
    /// <paramref name="usage"/> is missing (a case where cost can never apply
    /// — for example, a code agent with no bound model). When the model is
    /// known, a <see cref="RunCost"/> is always returned even if pricing is
    /// undefined; in that case <see cref="RunCost.Source"/> is
    /// <see cref="PricingSource.Unknown"/> and the cost fields are
    /// <see langword="null"/> — <strong>not zero</strong>.
    /// </returns>
    RunCost? Resolve(string? provider, string? model, RunUsage? usage);
}

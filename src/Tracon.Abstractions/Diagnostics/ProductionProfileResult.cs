namespace Tracon;

/// <summary>The answer one production profile check gives.</summary>
/// <remarks>
/// <para>
/// Built through the three factory methods rather than a constructor, so a
/// permissive answer cannot be written without the two things that make it
/// actionable: what the setting is today, and how to change it. A startup
/// failure that only names the setting sends the reader to the source.
/// </para>
/// <para>
/// <strong>No text on this type may carry a configuration VALUE.</strong> Every
/// field is written into the startup exception and into the log, so a key, a
/// connection string or a token placed here leaves the process. Describe the
/// state ("no content guard is registered"), never the material.
/// </para>
/// <para>
/// Deliberately not a <c>record</c>: the generated <c>ToString</c> prints every
/// field, which turns any log statement that formats the object into a second,
/// unreviewed disclosure path.
/// </para>
/// </remarks>
public sealed class ProductionProfileResult
{
    private ProductionProfileResult(
        ProductionProfileState state,
        string setting,
        string observed,
        string remedy)
    {
        State = state;
        Setting = setting;
        Observed = observed;
        Remedy = remedy;
    }

    /// <summary>Gets what the check found.</summary>
    public ProductionProfileState State { get; }

    /// <summary>
    /// Gets the setting or registration the check inspected, named the way a
    /// consumer would search for it — a configuration key such as
    /// <c>Tracon:RateLimit:Enabled</c>, or a contract such as
    /// <c>IContentGuard</c>.
    /// </summary>
    public string Setting { get; }

    /// <summary>
    /// Gets what the check observed, described rather than quoted. Empty for
    /// <see cref="ProductionProfileState.Satisfied"/>.
    /// </summary>
    public string Observed { get; }

    /// <summary>
    /// Gets how to answer the decision. Empty unless the state is
    /// <see cref="ProductionProfileState.Permissive"/>.
    /// </summary>
    public string Remedy { get; }

    /// <summary>The feature is on; the decision was answered.</summary>
    /// <param name="setting">The setting or registration that was inspected.</param>
    /// <returns>A satisfied result.</returns>
    /// <exception cref="ArgumentException"><paramref name="setting"/> is empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="setting"/> is <see langword="null"/>.</exception>
    public static ProductionProfileResult Satisfied(string setting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);

        return new ProductionProfileResult(ProductionProfileState.Satisfied, setting, string.Empty, string.Empty);
    }

    /// <summary>The setting is still on its permissive default.</summary>
    /// <param name="setting">The setting or registration that was inspected.</param>
    /// <param name="observed">What the setting is today, described without quoting its value.</param>
    /// <param name="remedy">How to answer the decision.</param>
    /// <returns>A permissive result.</returns>
    /// <exception cref="ArgumentException">An argument is empty or white space.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static ProductionProfileResult Permissive(string setting, string observed, string remedy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        ArgumentException.ThrowIfNullOrWhiteSpace(observed);
        ArgumentException.ThrowIfNullOrWhiteSpace(remedy);

        return new ProductionProfileResult(ProductionProfileState.Permissive, setting, observed, remedy);
    }

    /// <summary>The decision does not apply to this composition.</summary>
    /// <param name="setting">The setting or registration that was inspected.</param>
    /// <param name="reason">Why the decision is meaningless here.</param>
    /// <returns>A not-applicable result.</returns>
    /// <exception cref="ArgumentException">An argument is empty or white space.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static ProductionProfileResult NotApplicable(string setting, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ProductionProfileResult(ProductionProfileState.NotApplicable, setting, reason, string.Empty);
    }
}

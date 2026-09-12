namespace Tracon;

/// <summary>
/// The built-in <see cref="IRunAttributionContext"/>. It reports whatever
/// <see cref="AmbientRunAttributionScope"/> carries, and nothing otherwise.
/// </summary>
/// <remarks>
/// <para>
/// An application that registers nothing gets exactly its previous behaviour:
/// both members are <see langword="null"/>, the <c>user_id</c> and
/// <c>labels</c> columns stay NULL, and no other behaviour changes. This is the
/// the no-surprises rule "no surprises" default.
/// </para>
/// <para>
/// The ambient scope is still honoured here so that a queued job, a scheduled
/// run or a direct .NET call can attribute itself without the consumer having
/// to write an implementation first — the same courtesy
/// <c>HttpTenantContext</c> extends to <see cref="AmbientTenantScope"/>.
/// </para>
/// <para>
/// The registration uses <c>TryAdd</c>, so a consumer implementation that binds
/// the interface to a real identity pipeline always wins.
/// </para>
/// </remarks>
public sealed class DefaultRunAttributionContext : IRunAttributionContext
{
    /// <inheritdoc />
    public string? UserId => AmbientRunAttributionScope.CurrentUserId;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string>? Labels => AmbientRunAttributionScope.CurrentLabels;
}

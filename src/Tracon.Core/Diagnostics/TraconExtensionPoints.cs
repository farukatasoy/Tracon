using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// The seven embedding points a host may bind, and what "Tracon's built-in
/// default" means for each one.
/// </summary>
/// <remarks>
/// <para>
/// Single-sourced on purpose. Two callers ask the same question:
/// <see cref="TraconDiagnosticsCollector"/> reports WHICH implementation is
/// bound, and <see cref="RequiredBindingValidator"/> refuses to start the host
/// when a required one is still the built-in default. Writing that judgement
/// twice lets the two answers drift the moment a default type is renamed.
/// </para>
/// <para>
/// This is the list, and it does not grow to cover every contract Tracon
/// registers with <c>TryAdd</c>: an eighth entry is a deliberate decision, not
/// a side effect of adding a replaceable registration.
/// </para>
/// </remarks>
internal static class TraconExtensionPoints
{
    /// <summary>The seven points, in the order the diagnostics report lists them.</summary>
    public static readonly IReadOnlyList<ExtensionPoint> All =
    [
        new(typeof(ITenantContext), typeof(SingleTenantContext)),
        new(typeof(IRunAttributionContext), typeof(DefaultRunAttributionContext)),
        new(typeof(IToolAuthorizationHandler), typeof(AllowAllToolAuthorizationHandler)),
        new(typeof(IRunAuthorizationHandler), typeof(AllowAllRunAuthorizationHandler)),
        new(
            typeof(IRunEventSink),
            BuiltInDefault: null,
            // The closed generic keeps the probe AOT-safe: the non-generic
            // GetServices(Type) overload needs run-time code generation.
            CollectionProbe: static provider => provider.GetServices<IRunEventSink>().Any()),
        new(typeof(IAttachmentStorage), BuiltInDefault: null),
        new(typeof(IToolApprovalPresenter), typeof(NullToolApprovalPresenter)),
    ];

    /// <summary>The contract names, for an error message that lists what is accepted.</summary>
    public static string ContractNames
        => string.Join(", ", All.Select(static point => point.Contract.Name));

    /// <summary>Finds the point a contract describes.</summary>
    /// <param name="contract">The contract type.</param>
    /// <returns>The point, or <see langword="null"/> when the contract is not one of the seven.</returns>
    public static ExtensionPoint? Find(Type contract)
    {
        foreach (var point in All)
        {
            if (point.Contract == contract)
            {
                return point;
            }
        }

        return null;
    }

    /// <summary>
    /// The type Tracon binds for <paramref name="contract"/> when the host
    /// binds nothing.
    /// </summary>
    /// <param name="contract">One of the seven contracts.</param>
    /// <returns>
    /// The built-in default type, or <see langword="null"/> when Tracon
    /// registers nothing at all for that point.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="contract"/> is not one of the seven.</exception>
    public static Type? BuiltInDefaultOf(Type contract)
    {
        var point = Find(contract)
            ?? throw new ArgumentOutOfRangeException(nameof(contract), contract, "Not a Tracon extension point.");

        return point.BuiltInDefault;
    }
}

/// <summary>Describes one embedding point.</summary>
/// <param name="Contract">The contract a host binds.</param>
/// <param name="BuiltInDefault">
/// The type Tracon binds when the host binds nothing, or
/// <see langword="null"/> when Tracon registers NOTHING for this point. For
/// those two points the built-in default is an ABSENCE, not a type, and a type
/// comparison answers a different question than the one being asked.
/// </param>
/// <param name="CollectionProbe">
/// Answers whether a COLLECTION point has any binding, or <see langword="null"/>
/// for a single-instance point. A collection point is bound when the collection
/// is non-empty, which is a different question from "is the resolved type the
/// built-in default" — carrying the probe with the point keeps the two apart
/// instead of leaving a caller to remember which test applies.
/// </param>
internal sealed record ExtensionPoint(
    Type Contract,
    Type? BuiltInDefault,
    Func<IServiceProvider, bool>? CollectionProbe = null);

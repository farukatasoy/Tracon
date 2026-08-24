using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace AgentPrism.Testing.Contracts.Storage;

/// <summary>
/// Verifies that a test assembly derives every contract class this package
/// ships, so a class dropped during a refactor or never picked up for a new
/// provider is caught at build time instead of silently reducing coverage.
/// </summary>
/// <remarks>
/// AgentPrism's own four store implementations (in-memory, PostgreSQL, SQL
/// Server, SQLite) each call <see cref="MissingDerivedTypes"/> from their own
/// test assembly; a third-party implementation can do the same.
/// </remarks>
public static class ContractCoverage
{
    /// <summary>
    /// The contract classes this package ships: every non-generic
    /// <see langword="public abstract"/> type in this assembly whose name
    /// ends in <c>Contract</c>.
    /// </summary>
    /// <returns>The contract types, ordered by name.</returns>
    [RequiresUnreferencedCode("Reflects over every type in this assembly. Build-time test infrastructure only; never called from application code.")]
    public static IReadOnlyList<Type> ContractTypes()
        => [.. typeof(ContractCoverage).Assembly
            .GetTypes()
            .Where(static type => type is { IsPublic: true, IsAbstract: true, IsGenericTypeDefinition: false })
            .Where(static type => type.Name.EndsWith("Contract", StringComparison.Ordinal))
            .OrderBy(static type => type.Name, StringComparer.Ordinal)];

    /// <summary>
    /// The contract classes <paramref name="consumerAssembly"/> declares no
    /// concrete derived type for.
    /// </summary>
    /// <param name="consumerAssembly">
    /// The test assembly to check, typically <c>Assembly.GetExecutingAssembly()</c>
    /// from the caller.
    /// </param>
    /// <param name="except">
    /// Contract type names this assembly intentionally does not cover (for
    /// example, a contract for a store shape this deployment does not offer).
    /// A name that does not match any contract type is itself reported back,
    /// the same way <c>TenantCoverageTests.Coverage_list_carries_no_stale_entries</c>
    /// catches a stale exemption: an exemption for a contract that no longer
    /// exists, or was never spelled correctly, must not silently pass.
    /// </param>
    /// <returns>
    /// The names of the uncovered, non-exempt contract types, ordered by
    /// name, followed by any stale <paramref name="except"/> entry (prefixed
    /// <c>"stale exemption: "</c>); empty when every contract type is either
    /// covered or validly exempted.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="consumerAssembly"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode("Reflects over every type in the consumer assembly. Build-time test infrastructure only; never called from application code.")]
    public static IReadOnlyList<string> MissingDerivedTypes(
        Assembly consumerAssembly,
        IReadOnlyCollection<string>? except = null)
    {
        ArgumentNullException.ThrowIfNull(consumerAssembly);

        var contractTypes = ContractTypes();
        var exempt = except ?? [];

        var concreteTypes = consumerAssembly
            .GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false })
            .ToList();

        var missing = contractTypes
            .Where(contract => !exempt.Contains(contract.Name, StringComparer.Ordinal))
            .Where(contract => !concreteTypes.Any(concrete => concrete.IsSubclassOf(contract)))
            .Select(static contract => contract.Name);

        var staleExemptions = exempt
            .Where(name => !contractTypes.Any(contract => string.Equals(contract.Name, name, StringComparison.Ordinal)))
            .Select(static name => $"stale exemption: {name}");

        return [.. missing.Concat(staleExemptions).OrderBy(static name => name, StringComparer.Ordinal)];
    }
}

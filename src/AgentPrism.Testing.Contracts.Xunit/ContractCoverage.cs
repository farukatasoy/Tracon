using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace AgentPrism.Testing.Contracts;

/// <summary>
/// Verifies that a test assembly derives every contract class this package
/// ships for one contract family, so a class dropped during a refactor or
/// never picked up for a new implementation is caught at build time instead of
/// silently reducing coverage.
/// </summary>
/// <remarks>
/// <para>
/// AgentPrism's own four store implementations (in-memory, PostgreSQL, SQL
/// Server, SQLite) each call <see cref="MissingDerivedTypes"/> from their own
/// test assembly; a third-party implementation can do the same.
/// </para>
/// <para>
/// Every call names a <strong>family</strong> — the namespace the contracts
/// live in, such as <see cref="StorageContracts"/> or
/// <see cref="ProviderContracts"/>. There is deliberately no family-less
/// overload: one used to exist, and adding a second family to this package
/// would have made every existing caller fail with contracts that were never
/// theirs to implement. A store implementer's coverage check must not start
/// failing because AgentPrism published a model-provider contract.
/// </para>
/// </remarks>
public static class ContractCoverage
{
    /// <summary>The namespace holding the store contracts (<c>IRunStore</c> and the rest).</summary>
    public const string StorageContracts = "AgentPrism.Testing.Contracts.Storage";

    /// <summary>The namespace holding the model-provider contracts.</summary>
    public const string ProviderContracts = "AgentPrism.Testing.Contracts.Providers";

    /// <summary>The namespace holding run judge contracts.</summary>
    public const string JudgeContracts = "AgentPrism.Testing.Contracts.Judges";

    /// <summary>
    /// The contract classes this package ships for one family: every
    /// non-generic <see langword="public abstract"/> type in
    /// <paramref name="namespaceScope"/> whose name ends in <c>Contract</c>.
    /// </summary>
    /// <param name="namespaceScope">
    /// The contract family, normally <see cref="StorageContracts"/> or
    /// <see cref="ProviderContracts"/>.
    /// </param>
    /// <returns>The contract types, ordered by name.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="namespaceScope"/> is empty, or names no contract type
    /// at all. A namespace that matches nothing is a typo, not an empty
    /// family: reporting zero contracts would make a coverage check pass
    /// while checking nothing.
    /// </exception>
    [RequiresUnreferencedCode("Reflects over every type in this assembly. Build-time test infrastructure only; never called from application code.")]
    public static IReadOnlyList<Type> ContractTypes(string namespaceScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namespaceScope);

        var types = typeof(ContractCoverage).Assembly
            .GetTypes()
            .Where(static type => type is { IsPublic: true, IsAbstract: true, IsGenericTypeDefinition: false })
            .Where(static type => type.Name.EndsWith("Contract", StringComparison.Ordinal))
            .Where(type => string.Equals(type.Namespace, namespaceScope, StringComparison.Ordinal))
            .OrderBy(static type => type.Name, StringComparer.Ordinal)
            .ToArray();

        return types.Length > 0
            ? types
            : throw new ArgumentException(
                $"No contract type lives in namespace '{namespaceScope}'. Use {nameof(ContractCoverage)}." +
                $"{nameof(StorageContracts)} or {nameof(ContractCoverage)}.{nameof(ProviderContracts)}.",
                nameof(namespaceScope));
    }

    /// <summary>
    /// The contract classes in one family that <paramref name="consumerAssembly"/>
    /// declares no concrete derived type for.
    /// </summary>
    /// <param name="consumerAssembly">
    /// The test assembly to check, typically <c>Assembly.GetExecutingAssembly()</c>
    /// from the caller.
    /// </param>
    /// <param name="namespaceScope">
    /// The contract family to check, normally <see cref="StorageContracts"/>
    /// or <see cref="ProviderContracts"/>. Contracts outside it are ignored,
    /// so a consumer that implements one family is unaffected by the others.
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
    /// <exception cref="ArgumentException"><paramref name="namespaceScope"/> names no contract type.</exception>
    [RequiresUnreferencedCode("Reflects over every type in the consumer assembly. Build-time test infrastructure only; never called from application code.")]
    public static IReadOnlyList<string> MissingDerivedTypes(
        Assembly consumerAssembly,
        string namespaceScope,
        IReadOnlyCollection<string>? except = null)
    {
        ArgumentNullException.ThrowIfNull(consumerAssembly);

        var contractTypes = ContractTypes(namespaceScope);
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

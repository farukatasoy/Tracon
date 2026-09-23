using System.Reflection;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// Gates the part of the three SQL providers that is still copied by hand: the
/// registration body of <c>UsePostgreSql</c>, <c>UseSqlServer</c> and
/// <c>UseSqlite</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The defect class is SILENT. <c>AddTracon()</c> pre-registers an in-memory
/// default for every store, so a <c>ReplaceTraconDefault</c> line forgotten in
/// one provider does not fail anything: the store simply stays in memory and
/// its data is lost with the process. Before this gate no test could see it —
/// the contract tests build every store with <c>new</c> and skip DI, the three
/// <c>ConsumerStoreRegistrationTests</c> resolve two contracts, and
/// <c>AuditCoverageTests</c> builds its container from <c>AddTracon()</c> alone.
/// The same blindness covered the audit decorators: a provider that registers
/// a bare <c>Sql*Store</c> where <c>AddTracon()</c> registers an
/// <c>Auditing*</c> decorator drops that store's audit trail (F-170's class).
/// </para>
/// <para>
/// Five checks, each closing a different miss: the three providers touch the
/// same service types (a line added to one provider and forgotten in another);
/// every persistence contract resolves to the provider's own implementation (a
/// store added to Core and forgotten in all three); every contract keeps the
/// decorator state <c>AddTracon()</c> gives it; a later provider in the chain
/// takes over every contract, as K-183 promises (a <c>TryAdd</c> where
/// <c>ReplaceTraconDefault</c> belongs); and every contract type a provider
/// registers is classified, so a persistence contract not named <c>*Store</c>
/// cannot stay outside the other checks.
/// </para>
/// <para>
/// No check opens a database connection. The connection strings point at port 1
/// (PostgreSQL, SQL Server) and at a SQLite file nobody creates: resolving
/// builds the stores and their data sources, and
/// <see cref="Every_persistence_contract_resolves_to_the_providers_own_implementation"/>
/// asserts that the SQLite file still does not exist afterwards. The test never
/// names an internal provider type — the three providers compile the same
/// linked-source types (K-176), so a type name here would be ambiguous
/// (CS0433); the implementation's assembly is what identifies the provider.
/// </para>
/// </remarks>
public sealed class SqlProviderRegistrationParityTests
{
    private static readonly string SqliteDatabasePath = Path.Combine(
        Path.GetTempPath(),
        $"tracon-registration-parity-{Guid.NewGuid():N}.db");

    private static readonly SqlProvider PostgreSql = new(
        "PostgreSQL",
        typeof(TraconPostgreSqlBuilderExtensions).Assembly,
        typeof(TraconPostgreSqlOptions),
        static builder => builder.UsePostgreSql(static options =>
        {
            options.ConnectionString = "Host=localhost;Port=1;Database=unused;Username=unused;Timeout=1";
            options.AutoApplyMigrations = false;
        }));

    private static readonly SqlProvider SqlServer = new(
        "SQL Server",
        typeof(TraconSqlServerBuilderExtensions).Assembly,
        typeof(TraconSqlServerOptions),
        static builder => builder.UseSqlServer(static options =>
        {
            options.ConnectionString =
                "Server=localhost,1;Database=unused;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=1";
            options.AutoApplyMigrations = false;
        }));

    private static readonly SqlProvider Sqlite = new(
        "SQLite",
        typeof(TraconSqliteBuilderExtensions).Assembly,
        typeof(TraconSqliteOptions),
        static builder => builder.UseSqlite(static options =>
        {
            options.ConnectionString = $"Data Source={SqliteDatabasePath}";
            options.AutoApplyMigrations = false;
        }));

    private static readonly SqlProvider[] Providers = [PostgreSql, SqlServer, Sqlite];

    /// <summary>
    /// Persistence contracts whose name does not match the reflected
    /// <c>Tracon.Abstractions</c> <c>I*Store</c> pattern, each with the reason
    /// it is one.
    /// </summary>
    /// <remarks>
    /// <see cref="Every_contract_type_a_provider_registers_is_classified"/> keeps
    /// this list complete: a contract type a provider registers must be here, in
    /// the reflected <c>I*Store</c> set or in <see cref="NotPersistenceContracts"/>.
    /// </remarks>
    private static readonly Dictionary<Type, string> ContractsNotNamedStore = new()
    {
        [typeof(IAuditLog)] = "the audit trail's rows; AddTracon() registers InMemoryAuditLog",
        [typeof(ChatHistoryProvider)] = "MAF's conversation history, an abstract class outside Tracon.Abstractions",
#pragma warning disable MAAI001 // AgentFileStore is evaluation-only in MAF; the providers register it the same way.
        [typeof(AgentFileStore)] = "MAF's agent file store, an abstract class outside Tracon.Abstractions",
#pragma warning restore MAAI001
        [typeof(IMigrationApplier)] = "`tracon migrate` resolves it with GetRequiredService",
        [typeof(IStatePreflightReader)] = "`tracon state-check` resolves it with GetRequiredService",
        [typeof(ISqlPersistenceDiagnostics)] = "/api/diagnostics and the 503 store-unavailable answer read it",
    };

    /// <summary>
    /// Service types a provider registers from a contract assembly that are NOT
    /// persistence contracts, each with the reason.
    /// </summary>
    /// <remarks>
    /// <see cref="Every_contract_type_a_provider_registers_is_classified"/> reads
    /// this list together with <see cref="PersistenceContracts"/>: a contract
    /// type a provider registers must be in exactly one of them.
    /// </remarks>
    private static readonly Dictionary<Type, string> NotPersistenceContracts = new()
    {
        [typeof(SqlPersistenceRegistrationMarker)] =
            "holds no state: each provider adds one (AddSingleton, not Replace), and /api/diagnostics reports " +
            "their count as RegisteredPersistenceProviders",
    };

    /// <summary>
    /// Contracts that not every provider resolves to its own implementation.
    /// </summary>
    /// <remarks>
    /// <see cref="Every_exemption_still_matches_what_the_providers_register"/>
    /// keeps each entry honest, so an entry cannot outlive its reason.
    /// </remarks>
    private static readonly Dictionary<Type, string> NotEveryProviderImplements = new()
    {
        [typeof(IVectorSearchStore)] =
            "PostgreSQL only (K-344): SQL Server and SQLite register nothing, and PostgreSQL's " +
            "factory returns null while EnableKnowledge is off",
    };

    /// <summary>
    /// Contracts <c>AddTracon()</c> gives no built-in default, so there is no
    /// baseline decorator state; the three providers are compared with each
    /// other instead.
    /// </summary>
    private static readonly Dictionary<Type, string> NoAddTraconDefault = new()
    {
        [typeof(IVoiceSessionStore)] =
            "UseVoiceConversation() and UseLiveVoice() register its in-memory default, not AddTracon()",
        [typeof(IConversationBranchStore)] =
            "no in-memory equivalent: MAF's in-memory history cannot be copied up to a sequence number",
        [typeof(IVectorSearchStore)] = "PostgreSQL only (K-344)",
        [typeof(IMigrationApplier)] = "provider-only seam: there is no database to migrate without a provider",
        [typeof(IStatePreflightReader)] = "provider-only seam: there is no stored state without a provider",
        [typeof(ISqlPersistenceDiagnostics)] = "provider-only seam: GetService null means 'no SQL provider'",
    };

    /// <summary>
    /// Contracts a LATER provider in the chain does not take over, each with
    /// the reason it is tolerated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🚨 A deliberate tolerance, not a design statement.
    /// <c>IConversationBranchStore</c> is the only contract the three providers
    /// register with <c>TryAddSingleton</c>; every other one goes through
    /// <c>ReplaceTraconDefault</c> (K-025). With one provider the two verbs end
    /// the same, because <c>AddTracon()</c> registers no default for it. With two
    /// providers in one chain — a configuration error K-183 warns about — the
    /// FIRST provider keeps the branch store while every other contract follows
    /// the second, so branching writes to one database and sessions live in the
    /// other. Changing the verb is an open decision, not this gate's. The entry
    /// is pinned: once all three providers switch to <c>ReplaceTraconDefault</c>
    /// the second provider takes over and this entry fails until it is removed.
    /// </para>
    /// <para>
    /// Measured limit: a switch in ONE provider only is invisible here.
    /// <c>ReplaceTraconDefault</c> sees the other provider's <c>TryAdd</c>
    /// descriptor as unmarked, treats it as the consumer's and keeps it, so the
    /// first provider still wins in every chain. Seeing that needs Core's
    /// internal default registry, which this project cannot read.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<Type, string> FirstProviderInTheChainKeeps = new()
    {
        [typeof(IConversationBranchStore)] =
            "registered with TryAddSingleton in all three providers, not ReplaceTraconDefault",
    };

    /// <summary>
    /// The three providers add or replace the same set of service types.
    /// </summary>
    /// <remarks>
    /// Measured on the descriptors, not by resolving: a descriptor that
    /// <c>AddTracon()</c> did not add is one the provider added or replaced. The
    /// provider's own options type is the only name allowed to differ.
    /// </remarks>
    [Fact]
    public void Every_provider_registers_the_same_service_types()
    {
        var registered = Providers.ToDictionary(
            static provider => provider,
            static provider => RegisteredServiceTypes(provider));

        var union = registered.Values.SelectMany(static types => types).ToHashSet(StringComparer.Ordinal);
        var exempt = NotEveryProviderImplements.Keys
            .Select(static type => Describe(type, optionsType: null))
            .ToHashSet(StringComparer.Ordinal);

        var drift = registered
            .SelectMany(pair => union
                .Where(type => !exempt.Contains(type) && !pair.Value.Contains(type))
                .Select(type => $"{pair.Key.Name} does not register {type}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        drift.ShouldBeEmpty(
            customMessage: "The three Use* methods no longer register the same service types. A line added to " +
                           "one provider was forgotten in another; add it to all three " +
                           $"(src/Tracon.PostgreSql, src/Tracon.SqlServer, src/Tracon.Sqlite):{Environment.NewLine}" +
                           string.Join(Environment.NewLine, drift));
    }

    /// <summary>
    /// Every persistence contract resolves to the provider's own implementation,
    /// never to <c>AddTracon()</c>'s in-memory default, and resolving them opens
    /// no connection.
    /// </summary>
    [Theory]
    [InlineData("PostgreSQL")]
    [InlineData("SQL Server")]
    [InlineData("SQLite")]
    public async Task Every_persistence_contract_resolves_to_the_providers_own_implementation(string providerName)
    {
        var provider = Providers.Single(candidate => string.Equals(candidate.Name, providerName, StringComparison.Ordinal));

        List<string> wrong;
        await using (var services = Build(provider.Use))
        {
            wrong = PersistenceContracts()
                .Where(static contract => !NotEveryProviderImplements.ContainsKey(contract))
                .Select(contract => (Contract: contract, Implementation: Unwrap(services.GetService(contract))))
                .Where(pair => pair.Implementation?.GetType().Assembly != provider.Assembly)
                .Select(static pair => $"{pair.Contract.Name} -> {DescribeImplementation(pair.Implementation)}")
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        wrong.ShouldBeEmpty(
            customMessage: $"With {provider.Name} registered, these persistence contracts do not come from " +
                           "the provider. AddTracon() pre-registers an " +
                           "in-memory default, so a missing ReplaceTraconDefault line keeps the data in memory " +
                           $"without any error:{Environment.NewLine}" + string.Join(Environment.NewLine, wrong));

        AssertNoSqliteConnectionWasOpened();
    }

    /// <summary>
    /// Every contract is audit-decorated after a provider exactly when it is
    /// after <c>AddTracon()</c> alone.
    /// </summary>
    /// <remarks>
    /// Checked on the resolved service BEFORE it is unwrapped: the check above
    /// unwraps, so a provider that registers a bare <c>Sql*Store</c> where
    /// <c>AddTracon()</c> registers an <c>Auditing*</c> decorator passes it.
    /// </remarks>
    [Fact]
    public async Task Every_contract_keeps_the_audit_decorator_state_AddTracon_gives_it()
    {
        var contracts = PersistenceContracts();
        var baseline = await DecoratorStatesAsync(static _ => { }, contracts);
        var perProvider = new Dictionary<SqlProvider, IReadOnlyDictionary<Type, bool>>();

        foreach (var provider in Providers)
        {
            perProvider[provider] = await DecoratorStatesAsync(provider.Use, contracts);
        }

        var drift = new List<string>();

        foreach (var contract in contracts)
        {
            // A contract without an AddTracon() default has no baseline; the
            // three providers must then agree with each other (PostgreSQL is
            // the arbitrary reference).
            var expected = NoAddTraconDefault.ContainsKey(contract)
                ? perProvider[PostgreSql][contract]
                : baseline[contract];

            drift.AddRange(Providers
                .Where(provider => perProvider[provider][contract] != expected)
                .Select(provider =>
                    $"{provider.Name}: {contract.Name} is {Decorated(perProvider[provider][contract])}, " +
                    $"expected {Decorated(expected)}"));
        }

        drift.ShouldBeEmpty(
            customMessage: "A provider's registration changed a contract's audit decorator. A bare Sql*Store " +
                           "where AddTracon() registers an Auditing* decorator silently drops that store's audit " +
                           $"trail:{Environment.NewLine}" + string.Join(Environment.NewLine, drift));
    }

    /// <summary>
    /// With two providers in one chain, the later one takes over every
    /// contract — the "last registration wins" rule K-183 documents.
    /// </summary>
    /// <remarks>
    /// This is the only check that sees the registration VERB of a contract
    /// <c>AddTracon()</c> has no default for: registered with <c>TryAdd</c>
    /// instead of <c>ReplaceTraconDefault</c>, it resolves the same with one
    /// provider (so the three checks above stay green) but stays with the first
    /// provider in a chain. Measured: <c>IStatePreflightReader</c> moved to
    /// <c>TryAddSingleton</c> in <c>UseSqlite</c> fails only this test. For a
    /// contract that HAS a default, <c>TryAdd</c> is a no-op and the
    /// completeness check already fails. <see cref="FirstProviderInTheChainKeeps"/>
    /// lists the tolerated cases and their limit.
    /// </remarks>
    [Fact]
    public async Task A_later_provider_in_the_chain_takes_over_every_contract()
    {
        (SqlProvider First, SqlProvider Second)[] chains =
        [
            (PostgreSql, SqlServer),
            (SqlServer, Sqlite),
            (Sqlite, PostgreSql),
        ];

        var drift = new List<string>();

        foreach (var (first, second) in chains)
        {
            await using var services = Build(builder =>
            {
                first.Use(builder);
                second.Use(builder);
            });

            foreach (var contract in PersistenceContracts())
            {
                if (NotEveryProviderImplements.ContainsKey(contract))
                {
                    continue;
                }

                var expected = FirstProviderInTheChainKeeps.ContainsKey(contract) ? first : second;
                var implementation = Unwrap(services.GetService(contract));

                if (implementation?.GetType().Assembly != expected.Assembly)
                {
                    drift.Add($"{first.Name} then {second.Name}: {contract.Name} -> " +
                              $"{DescribeImplementation(implementation)}, expected {expected.Name}");
                }
            }
        }

        drift.ShouldBeEmpty(
            customMessage: "A contract does not follow the chain's registration order. A provider registers " +
                           "with ReplaceTraconDefault, not TryAdd (K-025); a deliberate exception belongs in " +
                           $"FirstProviderInTheChainKeeps with its reason:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, drift));
    }

    /// <summary>
    /// The exemption lists stay true: an exempted contract really behaves as
    /// its reason says.
    /// </summary>
    [Fact]
    public async Task Every_exemption_still_matches_what_the_providers_register()
    {
        // K-344: PostgreSQL registers the vector store; the other two do not.
        var vectorStore = Describe(typeof(IVectorSearchStore), optionsType: null);
        RegisteredServiceTypes(PostgreSql).Contains(vectorStore).ShouldBeTrue("PostgreSQL registers IVectorSearchStore");
        RegisteredServiceTypes(SqlServer).Contains(vectorStore).ShouldBeFalse("SQL Server has no vector store");
        RegisteredServiceTypes(Sqlite).Contains(vectorStore).ShouldBeFalse("SQLite has no vector store");

        // A contract without an AddTracon() default must be declared, and a
        // declared one must really have none — this is also what verifies that
        // IVoiceSessionStore comes from the voice builders, not AddTracon().
        await using var baseline = Build(static _ => { });

        var undeclared = PersistenceContracts()
            .Where(contract => baseline.GetService(contract) is null && !NoAddTraconDefault.ContainsKey(contract))
            .Select(static contract => contract.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        var stale = NoAddTraconDefault.Keys
            .Where(contract => baseline.GetService(contract) is not null)
            .Select(static contract => contract.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        undeclared.ShouldBeEmpty(
            customMessage: "AddTracon() registers no default for these contracts. Add each to NoAddTraconDefault " +
                           $"with the reason: {string.Join(", ", undeclared)}");
        stale.ShouldBeEmpty(
            customMessage: "AddTracon() now registers a default for these contracts; remove them from " +
                           $"NoAddTraconDefault: {string.Join(", ", stale)}");
    }

    /// <summary>
    /// Every service type a provider registers from <c>Tracon.Abstractions</c>,
    /// <c>Tracon.Core</c> or MAF is classified: a persistence contract the
    /// checks above cover, or a <see cref="NotPersistenceContracts"/> entry with
    /// its reason.
    /// </summary>
    /// <remarks>
    /// This keeps <see cref="ContractsNotNamedStore"/> honest without a manual
    /// review. A provider that starts to register a new persistence contract
    /// whose name does not end in <c>Store</c> is otherwise invisible to the
    /// completeness, decorator and chain checks: none of them reflect it. This
    /// check fails until someone puts the type in one of the two lists. A stale
    /// entry fails too, so neither list can outlive its reason.
    /// </remarks>
    [Fact]
    public void Every_contract_type_a_provider_registers_is_classified()
    {
        var contractAssemblies = new HashSet<Assembly>
        {
            typeof(IRunStore).Assembly,
            typeof(TraconServiceCollectionExtensions).Assembly,
            typeof(ChatHistoryProvider).Assembly,
#pragma warning disable MAAI001 // AgentFileStore is evaluation-only in MAF; only its assembly is read here.
            typeof(AgentFileStore).Assembly,
#pragma warning restore MAAI001
        };

        var registered = Providers
            .SelectMany(static provider => RegisteredDescriptors(provider))
            .Select(static descriptor => descriptor.ServiceType)
            .Where(type => contractAssemblies.Contains(type.Assembly))
            .ToHashSet();
        var persistence = PersistenceContracts().ToHashSet();

        var unclassified = registered
            .Where(type => !persistence.Contains(type) && !NotPersistenceContracts.ContainsKey(type))
            .Select(static type => Describe(type, optionsType: null))
            .Order(StringComparer.Ordinal)
            .ToList();
        var both = NotPersistenceContracts.Keys
            .Where(persistence.Contains)
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        var stale = ContractsNotNamedStore.Keys
            .Concat(NotPersistenceContracts.Keys)
            .Where(type => !registered.Contains(type))
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        unclassified.ShouldBeEmpty(
            customMessage: "A provider registers these contract types, and no list classifies them. Add a " +
                           "persistence contract to ContractsNotNamedStore, anything else to " +
                           $"NotPersistenceContracts, each with its reason:{Environment.NewLine}" +
                           string.Join(Environment.NewLine, unclassified));
        both.ShouldBeEmpty(
            customMessage: "These types are both a persistence contract and in NotPersistenceContracts; " +
                           $"keep one: {string.Join(", ", both)}");
        stale.ShouldBeEmpty(
            customMessage: "No provider registers these listed types any more; remove their entries: " +
                           string.Join(", ", stale));
    }

    /// <summary>
    /// Every public <c>I*Store</c> interface in <c>Tracon.Abstractions</c>, plus
    /// the persistence contracts not named that way.
    /// </summary>
    private static List<Type> PersistenceContracts()
        => typeof(IRunStore).Assembly
            .GetExportedTypes()
            .Where(static type => type.IsInterface && type.Name.EndsWith("Store", StringComparison.Ordinal))
            .Concat(ContractsNotNamedStore.Keys)
            .Distinct()
            .OrderBy(static type => type.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The service types whose descriptor the provider added or replaced, with
    /// the provider's own options type normalized away.
    /// </summary>
    private static HashSet<string> RegisteredServiceTypes(SqlProvider provider)
        => RegisteredDescriptors(provider)
            .Select(descriptor => Describe(descriptor.ServiceType, provider.OptionsType))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The descriptors the provider added or replaced after <c>AddTracon()</c>.</summary>
    private static List<ServiceDescriptor> RegisteredDescriptors(SqlProvider provider)
    {
        var services = new ServiceCollection();
        var builder = services.AddTracon();
        var before = services.ToHashSet(ReferenceEqualityComparer.Instance);

        provider.Use(builder);

        return services.Where(descriptor => !before.Contains(descriptor)).ToList();
    }

    private static async Task<IReadOnlyDictionary<Type, bool>> DecoratorStatesAsync(
        Action<ITraconBuilder> use,
        IEnumerable<Type> contracts)
    {
        await using var services = Build(use);

        return contracts.ToDictionary(
            static contract => contract,
            contract => services.GetService(contract) is IAuditDecorated);
    }

    private static ServiceProvider Build(Action<ITraconBuilder> use)
    {
        var services = new ServiceCollection();
        use(services.AddTracon());

        return services.BuildServiceProvider();
    }

    private static object? Unwrap(object? service)
        => service is IAuditDecorated decorated ? decorated.AuditedInner : service;

    private static string DescribeImplementation(object? implementation)
        => implementation is null
            ? "nothing registered"
            : $"{implementation.GetType().Name} ({implementation.GetType().Assembly.GetName().Name})";

    private static string Decorated(bool decorated) => decorated ? "audit-decorated" : "not decorated";

    /// <summary>A readable, assembly-free type name; the provider's options type becomes one token.</summary>
    private static string Describe(Type type, Type? optionsType)
    {
        if (type == optionsType)
        {
            return "<provider options>";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definition = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var arguments = type.GetGenericArguments().Select(argument => Describe(argument, optionsType));

        return $"{definition[..definition.IndexOf('`', StringComparison.Ordinal)]}<{string.Join(", ", arguments)}>";
    }

    /// <summary>
    /// SQLite creates its database file on the first open, so a file that still
    /// does not exist proves no store opened a connection while it was resolved.
    /// </summary>
    private static void AssertNoSqliteConnectionWasOpened()
    {
        var opened = File.Exists(SqliteDatabasePath);

        foreach (var file in new[] { SqliteDatabasePath, SqliteDatabasePath + "-wal", SqliteDatabasePath + "-shm" })
        {
            File.Delete(file);
        }

        opened.ShouldBeFalse(
            customMessage: "Resolving the SQLite stores created the database file, so a store opened a " +
                           "connection while it was being built. This gate must stay connection-free.");
    }

    private sealed record SqlProvider(string Name, Assembly Assembly, Type OptionsType, Action<ITraconBuilder> Use);
}

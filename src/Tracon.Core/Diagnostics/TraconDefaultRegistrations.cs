using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

/// <summary>
/// The set of service registrations Tracon added as its OWN built-in defaults.
/// </summary>
/// <remarks>
/// <para>
/// A storage provider (<c>UsePostgreSql</c>, <c>UseSqlServer</c>,
/// <c>UseSqlite</c>) must override Tracon's in-memory default for ~34 store
/// contracts, so it cannot use <c>TryAdd</c>: <c>AddTracon()</c> has already
/// registered them. Plain <see cref="ServiceCollectionDescriptorExtensions.Replace"/>
/// is what it used instead, and that cannot tell three situations apart:
/// </para>
/// <list type="table">
///   <item><term>Tracon's in-memory default</term><description>overwrite</description></item>
///   <item><term>The consumer's own registration</term><description>KEEP — this type exists for this row</description></item>
///   <item><term>No registration at all</term><description>add</description></item>
/// </list>
/// <para>
/// The distinguishing signal is not the implementation TYPE — the defaults are
/// registered through factory delegates, so <c>ImplementationType</c> is
/// <see langword="null"/> for most of them. It is WHICH DESCRIPTOR INSTANCE
/// Tracon added, which is what this type records. A contract the consumer
/// registered before <c>AddTracon()</c> never gets a marked descriptor at all,
/// because <c>TryAdd</c> is a no-op there.
/// </para>
/// <para>
/// The promise this keeps is published: <c>guides/write-your-own-store</c> tells
/// consumers that "<c>TryAdd*</c> means your registration always wins", and
/// <c>samples/Tracon.Embedded</c> binds its own stores before <c>AddTracon()</c>
/// for exactly that reason.
/// </para>
/// </remarks>
internal sealed class TraconDefaultRegistrations(IServiceCollection services)
{
    private readonly HashSet<ServiceDescriptor> _marked =
        new(ReferenceEqualityComparer.Instance);

    private readonly HashSet<Type> _preserved = [];

    private readonly HashSet<Type> _known = [];

    /// <summary>
    /// Whether the registration that WINS for this contract is one the
    /// consuming application made rather than one of Tracon's defaults.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The collection is the same object the provider was built from, so by the
    /// time a hosted service asks, the list is final and its LAST descriptor for
    /// a service type is the one <c>GetService</c> returns. That is why this
    /// answers both registration orders the shipped guide promises: a consumer
    /// who registers before <c>AddTracon()</c> leaves Tracon's <c>TryAdd</c> a
    /// no-op, and one who registers after appends a descriptor that wins on
    /// order. Neither is marked.
    /// </para>
    /// <para>
    /// A contract Tracon knows nothing about answers <see langword="false"/> —
    /// the caller separates "not an extension point" from "still on the
    /// default" before asking.
    /// </para>
    /// </remarks>
    public bool ConsumerOwns(Type contract)
    {
        ServiceDescriptor? winning = null;

        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == contract)
            {
                winning = descriptor;
            }
        }

        return winning is not null && !_marked.Contains(winning);
    }

    /// <summary>Records that Tracon HAS a default for this contract.</summary>
    /// <remarks>
    /// Written even when the default was not added, because a consumer who
    /// registered first makes <c>TryAdd</c> a no-op — and that consumer is
    /// exactly the one who then wants <c>RequireCustomBinding</c> to accept the
    /// contract. Knowing a contract and owning its registration are two
    /// different facts, so they are two different sets.
    /// </remarks>
    public void MarkKnown(Type contract) => _known.Add(contract);

    /// <summary>Whether this contract is one Tracon has a default for.</summary>
    /// <remarks>
    /// This is what makes <c>RequireCustomBinding</c>'s accepted set
    /// self-maintaining: every contract Tracon defaults or a storage provider
    /// overrides passes through these helpers, so no second list can drift away
    /// from the first.
    /// </remarks>
    public bool Knows(Type contract) => _known.Contains(contract);

    /// <summary>Records a descriptor as Tracon's own built-in default.</summary>
    public void Mark(ServiceDescriptor descriptor) => _marked.Add(descriptor);

    /// <summary>Whether this exact descriptor is one Tracon registered as a default.</summary>
    public bool IsDefault(ServiceDescriptor descriptor) => _marked.Contains(descriptor);

    /// <summary>
    /// Records that a storage provider left a consumer's registration in place.
    /// </summary>
    /// <remarks>
    /// The set is read at startup to log one warning per contract. Silence is
    /// what made the old behavior expensive to find: the registration vanished
    /// with no log, no warning and no startup failure.
    /// </remarks>
    public void MarkPreserved(Type contract) => _preserved.Add(contract);

    /// <summary>The contracts a storage provider declined to overwrite.</summary>
    public IReadOnlyCollection<Type> Preserved => _preserved;
}

/// <summary>
/// Registers and overrides Tracon's built-in defaults without ever overwriting
/// a registration the consuming application made itself.
/// </summary>
/// <remarks>
/// Internal on purpose. Writing a STORE is a supported seam and needs nothing
/// from here — an ordinary <c>AddSingleton</c> is the documented step. Writing
/// a STORAGE PROVIDER, which overrides ~34 contracts at once, is not a shipped
/// seam, so these helpers stay out of the public API surface.
/// </remarks>
internal static class TraconDefaultRegistrationExtensions
{
    /// <summary>
    /// The registry for this service collection, created on first use.
    /// </summary>
    /// <remarks>
    /// Held as an <c>ImplementationInstance</c> so it is readable from the
    /// collection itself, before any provider is built: every caller here runs
    /// during composition, not at resolve time.
    /// </remarks>
    public static TraconDefaultRegistrations DefaultRegistrations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var descriptor in services)
        {
            if (descriptor.ImplementationInstance is TraconDefaultRegistrations existing)
            {
                return existing;
            }
        }

        var registry = new TraconDefaultRegistrations(services);
        services.AddSingleton(registry);
        return registry;
    }

    /// <summary>
    /// Adds one of Tracon's built-in defaults and marks it as such.
    /// </summary>
    /// <remarks>
    /// Behaves exactly like <c>TryAdd</c> — a contract the consumer already
    /// registered is left alone — and additionally remembers the descriptor it
    /// added, so a storage provider can later tell it apart from a consumer's.
    /// </remarks>
    public static void TryAddTraconDefault(this IServiceCollection services, ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);

        var registry = services.DefaultRegistrations();
        registry.MarkKnown(descriptor.ServiceType);

        foreach (var existing in services)
        {
            if (existing.ServiceType == descriptor.ServiceType)
            {
                // The consumer registered it first, so nothing is added and
                // nothing is marked - a storage provider will later find an
                // unmarked descriptor and keep it. The contract stays KNOWN,
                // which is what lets that same consumer declare it through
                // RequireCustomBinding.
                return;
            }
        }

        services.Add(descriptor);
        registry.Mark(descriptor);
    }

    /// <summary>
    /// Adds a built-in default built by a factory, and marks it.
    /// </summary>
    public static void TryAddTraconDefault<TService>(
        this IServiceCollection services,
        Func<IServiceProvider, TService> factory)
        where TService : class
        => services.TryAddTraconDefault(ServiceDescriptor.Singleton(factory));

    /// <summary>
    /// Adds a built-in default by implementation type, and marks it.
    /// </summary>
    public static void TryAddTraconDefault<TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
        this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
        => services.TryAddTraconDefault(ServiceDescriptor.Singleton<TService, TImplementation>());

    /// <summary>
    /// Overrides Tracon's built-in default for a contract, but never a
    /// registration the consuming application made itself.
    /// </summary>
    /// <remarks>
    /// When every registration for the contract is unmarked, the consumer owns
    /// the contract and this is a no-op apart from recording the fact for the
    /// startup warning. When the contract is unregistered, the descriptor is
    /// simply added.
    /// </remarks>
    public static void ReplaceTraconDefault(this IServiceCollection services, ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(descriptor);

        var registry = services.DefaultRegistrations();
        registry.MarkKnown(descriptor.ServiceType);
        List<ServiceDescriptor>? registered = null;

        foreach (var existing in services)
        {
            if (existing.ServiceType == descriptor.ServiceType)
            {
                (registered ??= []).Add(existing);
            }
        }

        if (registered is null)
        {
            services.Add(descriptor);
            registry.Mark(descriptor);
            return;
        }

        foreach (var existing in registered)
        {
            if (!registry.IsDefault(existing))
            {
                // The consumer's registration wins - AGENTS.md's rule and the
                // promise guides/write-your-own-store publishes. It is NOT
                // silent: the startup warning names the contract.
                registry.MarkPreserved(descriptor.ServiceType);
                return;
            }
        }

        foreach (var existing in registered)
        {
            services.Remove(existing);
        }

        services.Add(descriptor);
        registry.Mark(descriptor);
    }
}

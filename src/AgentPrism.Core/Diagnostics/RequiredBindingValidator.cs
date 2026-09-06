using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AgentPrism;

/// <summary>
/// One contract a host declared through <c>RequireCustomBinding&lt;T&gt;()</c>.
/// </summary>
/// <param name="Contract">The declared contract.</param>
/// <remarks>
/// Registered as an instance, so <c>TryAdd</c> cannot be used: it would collapse
/// every declaration to the first one, because it deduplicates by implementation
/// TYPE and all of these share one. Idempotence therefore belongs to the reader
/// — <see cref="RequiredBindingValidator"/> works on the distinct set of
/// contracts, so declaring the same contract twice is a no-op rather than an
/// error a composition module could trip over.
/// </remarks>
internal sealed record RequiredBindingRegistration(Type Contract);

/// <summary>
/// Refuses to start the host when a contract the application declared as a
/// required custom binding still resolves to AgentPrism's built-in default.
/// </summary>
/// <remarks>
/// <para>
/// Every extension point is registered with <c>TryAdd</c>, so a host that binds
/// nothing starts silently on the built-in default. That is the right default
/// for a first run and the wrong one for a security profile: a deployment that
/// means to enforce its own rule wants a mis-composed container to be a startup
/// failure, not an authorization decision nobody notices.
/// </para>
/// <para>
/// It runs while the host starts rather than at the first HTTP request, because
/// binding an extension point is a COMPOSITION concern: an embedded host with no
/// HTTP surface at all needs the same guarantee, and it never maps an endpoint.
/// </para>
/// <para>
/// This is a composition gate, not a security proof. It says the host's own
/// implementation is the one bound; it says nothing about whether that
/// implementation decides correctly.
/// </para>
/// </remarks>
internal sealed class RequiredBindingValidator(
    IEnumerable<RequiredBindingRegistration> registrations,
    IServiceScopeFactory scopeFactory) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var required = DistinctContracts();

        if (required.Count == 0)
        {
            // Nothing was declared, so nothing is resolved. An installation that
            // never calls RequireCustomBinding keeps its exact composition order:
            // resolving a service here would build it, and building it earlier
            // than before is a behavior change of its own.
            return Task.CompletedTask;
        }

        // Membership is checked BEFORE anything is resolved, so a contract that
        // is not an extension point is reported as exactly that, instead of as a
        // binding that happens to be missing.
        foreach (var contract in required)
        {
            if (AgentPrismExtensionPoints.Find(contract) is null)
            {
                throw new InvalidOperationException(
                    $"{contract.Name} was declared as a required custom binding, but it is not an AgentPrism " +
                    $"extension point. RequireCustomBinding accepts these seven contracts: " +
                    $"{AgentPrismExtensionPoints.ContractNames}.");
            }
        }

        // The check resolves inside a scope, never from the root provider: a host
        // is free to bind an extension point as a scoped service, and resolving a
        // scoped service from the root is the captive-dependency mistake
        // ValidateScopes exists to reject.
        using var scope = scopeFactory.CreateScope();

        foreach (var contract in required)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Validate(scope.ServiceProvider, AgentPrismExtensionPoints.Find(contract)!);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private List<Type> DistinctContracts()
    {
        var required = new List<Type>();

        foreach (var registration in registrations)
        {
            if (!required.Contains(registration.Contract))
            {
                required.Add(registration.Contract);
            }
        }

        return required;
    }

    private static void Validate(IServiceProvider provider, ExtensionPoint point)
    {
        var contract = point.Contract.Name;

        if (point.CollectionProbe is { } hasBinding)
        {
            // A collection point has no built-in default TYPE; AgentPrism
            // registers nothing, so "the default is bound" means the collection
            // is empty. A type comparison would silently answer a different
            // question here.
            if (!hasBinding(provider))
            {
                throw Missing(contract);
            }

            return;
        }

        var bound = provider.GetService(point.Contract) ?? throw Missing(contract);

        if (point.BuiltInDefault is { } builtIn && bound.GetType() == builtIn)
        {
            throw new InvalidOperationException(
                $"{contract} was declared as a required custom binding, but AgentPrism's built-in default " +
                $"{builtIn.Name} is what resolved. Register your own {contract} on IServiceCollection BEFORE " +
                $"the AddAgentPrism() call. AgentPrism registers {contract} with TryAdd, so a TryAdd " +
                $"registration made after AddAgentPrism() is dropped and the built-in default stays bound.");
        }
    }

    private static InvalidOperationException Missing(string contract)
        => new(
            $"{contract} was declared as a required custom binding, but nothing is registered for it. " +
            $"Register your own {contract} on IServiceCollection before the AddAgentPrism() call.");
}

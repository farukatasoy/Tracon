using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace AgentPrism;

/// <summary>
/// One <c>AddJobHandler</c> call: the key, and the type that serves it.
/// </summary>
/// <param name="HandlerKey">The dispatch key.</param>
/// <param name="HandlerType">The <see cref="IJobHandler"/> implementation, resolved per execution.</param>
/// <param name="BuiltIn">
/// Whether AgentPrism itself registered this handler. Only a built-in
/// registration may use the <see cref="JobHandlerKeys.ReservedPrefix"/>
/// namespace.
/// </param>
internal sealed record JobHandlerRegistration(string HandlerKey, Type HandlerType, bool BuiltIn);

/// <summary>
/// The key-to-type map the background worker dispatches through.
/// </summary>
/// <remarks>
/// Built once, at host start, and read-only afterwards — two workers may look
/// a key up in parallel without any coordination. Because the lookup is an
/// exact ordinal match rather than a scan, the registration ORDER of the
/// handlers is irrelevant: a consumer's handler cannot shadow a built-in one,
/// and a built-in one cannot shadow a consumer's.
/// </remarks>
internal sealed class JobHandlerRegistry
{
    private readonly FrozenDictionary<string, Type> _byKey;

    private JobHandlerRegistry(FrozenDictionary<string, Type> byKey) => _byKey = byKey;

    /// <summary>Every registered key, ordered so the message a validator prints is stable.</summary>
    public IReadOnlyList<string> Keys { get; private init; } = [];

    /// <summary>
    /// Builds the map, rejecting a duplicate key, an invalid key, and a
    /// consumer registration inside the reserved namespace.
    /// </summary>
    /// <param name="registrations">Every registration in the container.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="registrations"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Two registrations share a key, a key is malformed, or a consumer
    /// registration uses the <see cref="JobHandlerKeys.ReservedPrefix"/> namespace.
    /// </exception>
    public static JobHandlerRegistry Create(IEnumerable<JobHandlerRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var byKey = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            if (!JobHandlerKeys.IsValidKey(registration.HandlerKey))
            {
                throw new InvalidOperationException(
                    $"'{registration.HandlerKey}' is not a valid job handler key. A key must be 1-128 " +
                    "characters: lowercase ASCII letters, digits, '.', '_', or '-', starting with a " +
                    $"letter or digit. It was registered for '{registration.HandlerType.FullName}'.");
            }

            if (!registration.BuiltIn && JobHandlerKeys.IsReserved(registration.HandlerKey))
            {
                throw new InvalidOperationException(
                    $"The job handler key '{registration.HandlerKey}' is reserved: the " +
                    $"'{JobHandlerKeys.ReservedPrefix}' namespace belongs to AgentPrism's own handlers. " +
                    $"Register '{registration.HandlerType.FullName}' under a key of your own " +
                    "(for example 'contoso.nightly-report').");
            }

            if (byKey.TryGetValue(registration.HandlerKey, out var existing))
            {
                // 🚨 The SAME type under the SAME key is a no-op, not a clash.
                // A registration is written with a plain Add (a setup-time
                // extension must not inspect the collection -- K-251), so
                // calling AddJobHandler<T>(key) twice, or AddAgentPrism()
                // twice, produces two identical entries. Treating that as a
                // conflict would break the "registration is idempotent"
                // promise AddAgentPrism's own documentation makes. What a key
                // may not do is name TWO DIFFERENT handlers.
                if (existing == registration.HandlerType)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    $"Two job handlers are registered for the key '{registration.HandlerKey}': " +
                    $"'{existing.FullName}' and '{registration.HandlerType.FullName}'. A key " +
                    "identifies exactly one handler; give one of them a different key.");
            }

            byKey.Add(registration.HandlerKey, registration.HandlerType);
        }

        return new JobHandlerRegistry(byKey.ToFrozenDictionary(StringComparer.Ordinal))
        {
            Keys = [.. byKey.Keys.Order(StringComparer.Ordinal)],
        };
    }

    /// <summary>Finds the implementation type registered for <paramref name="handlerKey"/>.</summary>
    /// <param name="handlerKey">The job's handler key.</param>
    /// <param name="handlerType">The implementation type, when one is registered.</param>
    /// <returns><see langword="true"/> if a handler is registered for the key.</returns>
    public bool TryGetHandlerType(string? handlerKey, [NotNullWhen(true)] out Type? handlerType)
    {
        if (handlerKey is null)
        {
            handlerType = null;
            return false;
        }

        return _byKey.TryGetValue(handlerKey, out handlerType);
    }

    /// <summary>Whether a handler is registered for <paramref name="handlerKey"/>.</summary>
    /// <param name="handlerKey">The candidate key.</param>
    /// <returns><see langword="true"/> if the key is registered.</returns>
    public bool IsRegistered(string? handlerKey) => TryGetHandlerType(handlerKey, out _);
}

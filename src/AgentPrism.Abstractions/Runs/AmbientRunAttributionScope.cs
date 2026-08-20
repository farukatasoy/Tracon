namespace AgentPrism;

/// <summary>
/// The ambient pass-through for attributing a run that does not run inside an
/// HTTP request to a user and a set of labels.
/// </summary>
/// <remarks>
/// <para>
/// This is the exact counterpart of <see cref="AmbientTenantScope"/>. An
/// <see cref="IRunAttributionContext"/> implementation is a singleton and
/// normally resolves the user from the current request; a queued job, a
/// scheduled run or a direct .NET API call has no request to resolve from. The
/// class carries the value through the same <see cref="AsyncLocal{T}"/>
/// pattern <c>IHttpContextAccessor</c> uses, and the built-in
/// <c>DefaultRunAttributionContext</c> reads it.
/// </para>
/// <para>
/// An <see cref="AsyncLocal{T}"/> write made inside an <c>async</c> method
/// does NOT flow back to its caller. Open the scope in the body of the method
/// that actually starts the run, and keep it alive across the whole run — on a
/// streaming path that means the scope must still be open before every
/// <c>MoveNextAsync</c>, not only before the first one.
/// </para>
/// <para>
/// Both members of a scope are held in a SINGLE slot, so
/// <see cref="Begin(string?, IReadOnlyDictionary{string, string}?)"/> replaces
/// the user and the labels together and can never be observed half-applied.
/// </para>
/// </remarks>
public static class AmbientRunAttributionScope
{
    private static readonly AsyncLocal<Frame?> Ambient = new();

    /// <summary>Gets the ambient user identity, or <see langword="null"/> when no scope is open.</summary>
    public static string? CurrentUserId => Ambient.Value?.UserId;

    /// <summary>Gets the ambient labels, or <see langword="null"/> when no scope is open.</summary>
    public static IReadOnlyDictionary<string, string>? CurrentLabels => Ambient.Value?.Labels;

    /// <summary>Gets whether a scope is open right now.</summary>
    public static bool IsActive => Ambient.Value is not null;

    /// <summary>
    /// Sets the ambient attribution for the duration of the scope.
    /// </summary>
    /// <param name="userId">
    /// The user identity, or <see langword="null"/> to attribute the run to no
    /// user. Whitespace is treated as <see langword="null"/>.
    /// </param>
    /// <param name="labels">The labels, or <see langword="null"/> when there are none.</param>
    /// <returns>
    /// An object that restores the previous value when
    /// <see cref="IDisposable.Dispose"/> is called. Nested use is safe.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="userId"/> or <paramref name="labels"/> breaks a limit on
    /// <see cref="RunLabels"/>. The scope is REJECTED rather than trimmed:
    /// silently trimming a label set turns a measurement into a false claim.
    /// </exception>
    public static IDisposable Begin(string? userId, IReadOnlyDictionary<string, string>? labels)
    {
        var normalizedUserId = string.IsNullOrWhiteSpace(userId) ? null : userId;

        if (RunLabels.Validate(normalizedUserId, labels) is { } error)
        {
            throw new ArgumentException(error, nameof(labels));
        }

        var previous = Ambient.Value;

        Ambient.Value = new Frame(
            normalizedUserId,
            labels is { Count: > 0 } ? RunLabels.Freeze(labels) : null);

        return new RestoreScope(previous);
    }

    private sealed record Frame(string? UserId, IReadOnlyDictionary<string, string>? Labels);

    private sealed class RestoreScope(Frame? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}

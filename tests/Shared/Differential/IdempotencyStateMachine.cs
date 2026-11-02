using System.Globalization;
using System.Text;
using CsCheck;

namespace Tracon.Tests.Differential;

/// <summary>The keys, tenants and responses one idempotency sequence draws from.</summary>
/// <param name="Tenants">Tenant spellings, some of them twins of others.</param>
/// <param name="Keys">Idempotency keys, some of them twins of others.</param>
/// <param name="Fingerprints">Request fingerprints (hexadecimal, like the SHA-256 the filter computes).</param>
/// <param name="Responses">Responses to store.</param>
internal sealed record IdempotencyUniverse(string[] Tenants, string[] Keys, string[] Fingerprints, IdempotencyResponseSpec[] Responses);

/// <summary>A response a step stores.</summary>
/// <param name="StatusCode">The status code.</param>
/// <param name="ContentType">The content type.</param>
/// <param name="Body">The body.</param>
/// <param name="RunId">The run the response belongs to, if any.</param>
/// <param name="Headers">The stored headers.</param>
internal sealed record IdempotencyResponseSpec(int StatusCode, string ContentType, string Body, Guid? RunId, IReadOnlyList<KeyValuePair<string, string>> Headers);

/// <summary>A generated idempotency sequence.</summary>
/// <param name="Universe">The values the steps draw from.</param>
/// <param name="Steps">The steps.</param>
internal sealed record IdempotencyPlan(IdempotencyUniverse Universe, IReadOnlyList<IdempotencyStep> Steps);

/// <summary>Which reservation a completion or a release claims to hold.</summary>
internal enum ReservedAtHint
{
    /// <summary>No claim: the call changes the record whoever holds it.</summary>
    Anyone,

    /// <summary>The creation time of the reservation the record holds now.</summary>
    Current,

    /// <summary>One tick before it.</summary>
    JustBefore,

    /// <summary>One microsecond after it.</summary>
    JustAfter,

    /// <summary>A time that no reservation has.</summary>
    Unrelated,
}

/// <summary>One call on an idempotency store.</summary>
internal abstract record IdempotencyStep;

/// <summary>A reservation request.</summary>
internal sealed record ReserveStep(int Tenant, int Key, int Fingerprint, DateTimeOffset CreatedAt, TimeSpan? Timeout) : IdempotencyStep;

/// <summary>The recording of a response.</summary>
internal sealed record CompleteStep(int Tenant, int Key, int Response, ReservedAtHint Hint, DateTimeOffset? ReservedAt) : IdempotencyStep;

/// <summary>The release of a reservation.</summary>
internal sealed record ReleaseStep(int Tenant, int Key, ReservedAtHint Hint, DateTimeOffset? ReservedAt) : IdempotencyStep;

/// <summary>
/// The idempotency store state machine: reserve (with and without a takeover timeout), complete and release, with and
/// without the reservation time the caller holds.
/// </summary>
internal sealed class IdempotencyStateMachine : StateMachine<IdempotencyPlan, IdempotencyStep>
{
    // The key and tenant columns hold 200 characters.
    private const int ColumnLength = 200;

    private static readonly Gen<string> Hex = Gen.String[Gen.Char["0123456789abcdef"], 1, 64];

    private static readonly Gen<TimeSpan?> Timeout = Gen.Frequency(
        (3, Gen.Const((TimeSpan?)null)),
        (2, Gen.Const((TimeSpan?)TimeSpan.Zero)),
        (2, Gen.Const((TimeSpan?)TimeSpan.FromMinutes(-5))),
        (6, Gen.Int[1, 60].Select(static minutes => (TimeSpan?)TimeSpan.FromMinutes(minutes))),
        (1, Gen.Const((TimeSpan?)TimeSpan.FromDays(100_000))));

    private static readonly Gen<IdempotencyResponseSpec> Response =
        from status in Gen.Frequency((6, Gen.OneOf(200, 201, 202, 400, 404, 409, 500)), (1, Gen.OneOf(0, 100, 599, int.MaxValue, -1)))
        from contentType in Gen.Frequency((4, Gen.OneOf("application/json", "text/plain", "text/event-stream")), (2, BoundaryValues.Text(100)))
        from body in Gen.Frequency((4, BoundaryValues.Text(60)), (1, Gen.Const(string.Empty)), (1, Gen.Const("{}")))
        from runId in Gen.Frequency((1, Gen.Const((Guid?)null)), (1, Gen.Guid.Select(static id => (Guid?)id)))
        from headers in Gen.Frequency(
            (3, Gen.Const(Array.Empty<KeyValuePair<string, string>>())),
            (2, Gen.OneOf("X-Request-Id", "Content-Language", "ETag", "X-A").SelectMany(name => BoundaryValues.Text(30).Select(value => new[] { KeyValuePair.Create(name, value) }))))
        select new IdempotencyResponseSpec(status, contentType, body, runId, headers);

    /// <inheritdoc />
    public override string Name => "IIdempotencyStore";

    /// <inheritdoc />
    public override Gen<IdempotencyPlan> Plans(int maxSteps, ProviderTraits traits)
    {
        ArgumentNullException.ThrowIfNull(traits);

        var universe =
            from tenants in BoundaryValues.TenantPool(3)
            from keys in BoundaryValues.Pool(4, BoundaryValues.Trimmed(ColumnLength), ColumnLength, traits.TrailingSpaceInsensitiveKeys)
            from fingerprints in Hex.Array[3]
            from responses in Response.Array[3]
            select new IdempotencyUniverse(tenants, keys, fingerprints, responses);

        return universe.SelectMany(
            u => Step(u).List[1, maxSteps],
            static (u, steps) => new IdempotencyPlan(u, steps));
    }

    /// <inheritdoc />
    public override IReadOnlyList<IdempotencyStep> Steps(IdempotencyPlan plan) => plan.Steps;

    /// <inheritdoc />
    public override IStepModel<IdempotencyStep> NewModel(IdempotencyPlan plan, ProviderTraits traits) => new IdempotencyModel(plan.Universe, traits);

    /// <inheritdoc />
    public override IStepDriver<IdempotencyStep> NewDriver(IdempotencyPlan plan, DifferentialStores stores, DifferentialScope scope)
        => new IdempotencyDriver(DifferentialStores.Require(stores.Idempotency), scope, plan.Universe);

    /// <inheritdoc />
    public override string Describe(IdempotencyStep step) => step switch
    {
        ReserveStep s => $"Reserve tenant={s.Tenant} key={s.Key} fingerprint={s.Fingerprint} createdAt={BoundaryValues.Show(s.CreatedAt)} timeout={s.Timeout?.ToString("c", CultureInfo.InvariantCulture) ?? "-"}",
        CompleteStep s => $"Complete tenant={s.Tenant} key={s.Key} response={s.Response} reservedAt={s.Hint}({(s.ReservedAt is { } at ? BoundaryValues.Show(at) : "-")})",
        ReleaseStep s => $"Release tenant={s.Tenant} key={s.Key} reservedAt={s.Hint}({(s.ReservedAt is { } at ? BoundaryValues.Show(at) : "-")})",
        _ => step.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public override string Print(IdempotencyPlan plan)
    {
        var builder = new StringBuilder("\n");
        SessionStateMachine.Pool(builder, "tenants", plan.Universe.Tenants);
        SessionStateMachine.Pool(builder, "keys", plan.Universe.Keys);
        SessionStateMachine.Pool(builder, "fingerprints", plan.Universe.Fingerprints);

        for (var index = 0; index < plan.Universe.Responses.Length; index++)
        {
            builder.Append("  responses[").Append(index).Append("] = ").AppendLine(IdempotencyModel.Render(plan.Universe.Responses[index]));
        }

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            builder.Append("  ").Append(index + 1).Append(". ").AppendLine(Describe(plan.Steps[index]));
        }

        return builder.ToString();
    }

    private static Gen<IdempotencyStep> Step(IdempotencyUniverse u)
    {
        var tenant = Gen.Int[0, u.Tenants.Length - 1];
        var key = Gen.Int[0, u.Keys.Length - 1];
        var fingerprint = Gen.Int[0, u.Fingerprints.Length - 1];
        var response = Gen.Int[0, u.Responses.Length - 1];
        var hint = Gen.Frequency(
            (3, Gen.Const(ReservedAtHint.Anyone)),
            (8, Gen.Const(ReservedAtHint.Current)),
            (1, Gen.Const(ReservedAtHint.JustBefore)),
            (1, Gen.Const(ReservedAtHint.JustAfter)),
            (1, Gen.Const(ReservedAtHint.Unrelated)));

        // Reservation times move forward by whole minutes so that a timeout of a few minutes expires inside a sequence.
        var createdAt = Gen.Frequency(
            (8, Gen.Int[0, 180].Select(static minutes => new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero).AddMinutes(minutes))),
            (2, Gen.Int[0, 180].Select(static minutes => new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero).AddMinutes(minutes).AddTicks(7))),
            (1, Gen.Int[0, 180].Select(static minutes => new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero).AddMinutes(minutes).ToOffset(TimeSpan.FromMinutes(330)))));

        return Gen.Frequency(
            (34, tenant.SelectMany(t => key.SelectMany(k => fingerprint.SelectMany(f => createdAt.SelectMany(c => Timeout.Select(o => (IdempotencyStep)new ReserveStep(t, k, f, c, o))))))),
            (22, tenant.SelectMany(t => key.SelectMany(k => response.SelectMany(r => hint.Select(h => (IdempotencyStep)new CompleteStep(t, k, r, h, null)))))),
            (16, tenant.SelectMany(t => key.SelectMany(k => hint.Select(h => (IdempotencyStep)new ReleaseStep(t, k, h, null))))));
    }
}

/// <summary>The idempotency specification, written from the interface documentation.</summary>
internal sealed class IdempotencyModel(IdempotencyUniverse universe, ProviderTraits traits) : IStepModel<IdempotencyStep>
{
    private readonly Dictionary<(string Tenant, string Key), Entry> _entries = [];

    /// <inheritdoc />
    public IdempotencyStep Concretize(IdempotencyStep step)
    {
        switch (step)
        {
            case CompleteStep s:
                return s with { ReservedAt = ResolveHint(s.Hint, s.Tenant, s.Key) };
            case ReleaseStep s:
                return s with { ReservedAt = ResolveHint(s.Hint, s.Tenant, s.Key) };
            default:
                return step;
        }
    }

    /// <inheritdoc />
    public Observation Apply(IdempotencyStep step)
    {
        try
        {
            return step switch
            {
                ReserveStep s => Reserve(s),
                CompleteStep s => Complete(s),
                ReleaseStep s => Release(s),
                _ => throw new InvalidOperationException(step.GetType().Name),
            };
        }
        catch (ArgumentException)
        {
            return Observation.Rejected();
        }
    }

    internal static string Render(IdempotencyResponseSpec response)
        => $"status={response.StatusCode} type={BoundaryValues.Show(response.ContentType)} run={response.RunId?.ToString("D", CultureInfo.InvariantCulture) ?? "null"} headers=[{string.Join(";", response.Headers.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => BoundaryValues.Show(pair.Key) + "=" + BoundaryValues.Show(pair.Value)))}] body={BoundaryValues.Show(response.Body)}";

    private IdempotencyResponseSpec Stored(IdempotencyResponseSpec response)
        => response with
        {
            ContentType = traits.Text(response.ContentType),
            Body = traits.Text(response.Body),
            Headers = [.. response.Headers.Select(pair => KeyValuePair.Create(traits.Text(pair.Key), traits.Text(pair.Value)))],
        };

    private static string Fold(string tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentException("blank tenant", nameof(tenant));
        }

        return tenant.ToLowerInvariant();
    }

    private DateTimeOffset? ResolveHint(ReservedAtHint hint, int tenant, int key)
    {
        if (hint == ReservedAtHint.Anyone)
        {
            return null;
        }

        var current = _entries.GetValueOrDefault(Address(tenant, key))?.CreatedAt;
        var anchor = current ?? new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

        return hint switch
        {
            ReservedAtHint.Current => anchor,
            ReservedAtHint.JustBefore => anchor.AddTicks(-1),
            ReservedAtHint.JustAfter => anchor.AddTicks(10),
            _ => anchor.AddYears(1).AddTicks(3),
        };
    }

    private (string Tenant, string Key) Address(int tenant, int key)
    {
        try
        {
            return (traits.Text(Fold(universe.Tenants[tenant])), KeyOf(universe.Keys[key]));
        }
        catch (ArgumentException)
        {
            return (string.Empty, string.Empty);
        }
    }

    private string KeyOf(string key)
    {
        var text = traits.Text(key);

        return traits.TrailingSpaceInsensitiveKeys ? text.TrimEnd(' ') : text;
    }

    private bool Holds(Entry entry, DateTimeOffset? reservedAt)
        => reservedAt is not { } at || (entry.State == EntryState.Reserved && traits.Ticks(entry.CreatedAt) == traits.Ticks(at));

    private Observation Reserve(ReserveStep step)
    {
        var address = (traits.Text(Fold(universe.Tenants[step.Tenant])), KeyOf(universe.Keys[step.Key]));
        var fingerprint = universe.Fingerprints[step.Fingerprint];

        if (!_entries.TryGetValue(address, out var existing))
        {
            _entries[address] = new Entry(fingerprint, step.CreatedAt);

            return Observation.Of("Reserved");
        }

        if (existing.State == EntryState.Reserved
            && step.Timeout is { } timeout
            && timeout > TimeSpan.Zero
            && traits.Ticks(step.CreatedAt) - traits.Ticks(existing.CreatedAt) >= timeout.Ticks)
        {
            _entries[address] = new Entry(fingerprint, step.CreatedAt);

            return Observation.Of("Reserved");
        }

        if (existing.State == EntryState.Completed)
        {
            return string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal)
                ? Observation.Of("Completed " + Render(Stored(universe.Responses[existing.Response!.Value])))
                : Observation.Of("FingerprintMismatch");
        }

        return Observation.Of("InProgress");
    }

    private Observation Complete(CompleteStep step)
    {
        var address = (traits.Text(Fold(universe.Tenants[step.Tenant])), KeyOf(universe.Keys[step.Key]));

        if (_entries.TryGetValue(address, out var entry) && Holds(entry, step.ReservedAt))
        {
            entry.State = EntryState.Completed;
            entry.Response = step.Response;
        }

        return Observation.Of("ok");
    }

    private Observation Release(ReleaseStep step)
    {
        var address = (traits.Text(Fold(universe.Tenants[step.Tenant])), KeyOf(universe.Keys[step.Key]));

        if (_entries.TryGetValue(address, out var entry) && Holds(entry, step.ReservedAt))
        {
            _entries.Remove(address);
        }

        return Observation.Of("ok");
    }

    private enum EntryState
    {
        Reserved,
        Completed,
    }

    private sealed class Entry(string fingerprint, DateTimeOffset createdAt)
    {
        public string Fingerprint { get; } = fingerprint;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public EntryState State { get; set; } = EntryState.Reserved;

        public int? Response { get; set; }
    }
}

/// <summary>Applies idempotency steps to a store.</summary>
internal sealed class IdempotencyDriver(IIdempotencyStore store, DifferentialScope scope, IdempotencyUniverse universe)
    : IStepDriver<IdempotencyStep>
{
    /// <inheritdoc />
    public async ValueTask<Observation> ApplyAsync(IdempotencyStep step)
    {
        switch (step)
        {
            case ReserveStep s:
                var reservation = await store.ReserveAsync(new IdempotencyRequest
                {
                    TenantId = scope.Wrap(universe.Tenants[s.Tenant]),
                    Key = universe.Keys[s.Key],
                    Fingerprint = universe.Fingerprints[s.Fingerprint],
                    CreatedAt = s.CreatedAt,
                    ReservationTimeout = s.Timeout,
                });

                return Observation.Of(reservation.State switch
                {
                    IdempotencyState.Reserved => "Reserved",
                    IdempotencyState.InProgress => "InProgress",
                    IdempotencyState.FingerprintMismatch => "FingerprintMismatch",
                    _ => "Completed " + IdempotencyModel.Render(ToSpec(reservation.Response)),
                });
            case CompleteStep s:
                var spec = universe.Responses[s.Response];
                await store.CompleteAsync(
                    scope.Wrap(universe.Tenants[s.Tenant]),
                    universe.Keys[s.Key],
                    new IdempotencyResponse
                    {
                        StatusCode = spec.StatusCode,
                        ContentType = spec.ContentType,
                        Body = spec.Body,
                        RunId = spec.RunId,
                        Headers = spec.Headers.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                    },
                    s.ReservedAt);

                return Observation.Of("ok");
            case ReleaseStep s:
                await store.ReleaseAsync(scope.Wrap(universe.Tenants[s.Tenant]), universe.Keys[s.Key], s.ReservedAt);

                return Observation.Of("ok");
            default:
                throw new InvalidOperationException(step.GetType().Name);
        }
    }

    private static IdempotencyResponseSpec ToSpec(IdempotencyResponse? response)
        => response is null
            ? new IdempotencyResponseSpec(-1, "<no response>", string.Empty, null, [])
            : new IdempotencyResponseSpec(response.StatusCode, response.ContentType, response.Body, response.RunId, [.. response.Headers]);
}

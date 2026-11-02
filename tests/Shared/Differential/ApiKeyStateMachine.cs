using System.Globalization;
using System.Text;
using CsCheck;

namespace Tracon.Tests.Differential;

/// <summary>The tenants, names and times one API key sequence draws from.</summary>
/// <param name="Tenants">Tenant spellings, some of them twins of others.</param>
/// <param name="Names">Key names.</param>
internal sealed record ApiKeyUniverse(string[] Tenants, string[] Names);

/// <summary>A generated API key sequence.</summary>
/// <param name="Universe">The values the steps draw from.</param>
/// <param name="Steps">The steps.</param>
internal sealed record ApiKeyPlan(ApiKeyUniverse Universe, IReadOnlyList<ApiKeyStep> Steps);

/// <summary>One call on an API key store.</summary>
internal abstract record ApiKeyStep;

/// <summary>The creation of a key.</summary>
internal sealed record CreateKeyStep(int Tenant, int Name, ApiKeyScope[] Scopes, DateTimeOffset? ExpiresAt) : ApiKeyStep;

/// <summary>The listing of a tenant's keys.</summary>
internal sealed record ListKeysStep(int Tenant) : ApiKeyStep;

/// <summary>The lookup of a key by its digest; a slot that no key holds looks up a digest no key has.</summary>
internal sealed record FindKeyStep(int Slot) : ApiKeyStep;

/// <summary>The revocation of a key on behalf of a tenant.</summary>
internal sealed record RevokeKeyStep(int Tenant, int Slot) : ApiKeyStep;

/// <summary>The recording of a use.</summary>
internal sealed record TouchKeyStep(int Slot, DateTimeOffset UsedAt) : ApiKeyStep;

/// <summary>The API key store state machine: create, list, find by digest, revoke and touch.</summary>
internal sealed class ApiKeyStateMachine : StateMachine<ApiKeyPlan, ApiKeyStep>
{
    private const int SlotCount = 6;

    private static readonly DateTimeOffset?[] Expiries =
    [
        null,
        null,
        new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(7),
        new DateTimeOffset(2100, 1, 1, 12, 0, 0, TimeSpan.FromHours(14)),
        new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero).AddTicks(9_999_999),
    ];

    /// <inheritdoc />
    public override string Name => "IApiKeyStore";

    /// <inheritdoc />
    public override Gen<ApiKeyPlan> Plans(int maxSteps, ProviderTraits traits)
    {
        ArgumentNullException.ThrowIfNull(traits);

        var universe =
            from tenants in BoundaryValues.TenantPool(3)
            from names in BoundaryValues.Trimmed(200).Array[3]
            select new ApiKeyUniverse(tenants, names);

        return universe.SelectMany(
            u => Step(u).List[1, maxSteps],
            static (u, steps) => new ApiKeyPlan(u, steps));
    }

    /// <inheritdoc />
    public override IReadOnlyList<ApiKeyStep> Steps(ApiKeyPlan plan) => plan.Steps;

    /// <inheritdoc />
    public override IStepModel<ApiKeyStep> NewModel(ApiKeyPlan plan, ProviderTraits traits) => new ApiKeyModel(plan.Universe, traits);

    /// <inheritdoc />
    public override IStepDriver<ApiKeyStep> NewDriver(ApiKeyPlan plan, DifferentialStores stores, DifferentialScope scope)
        => new ApiKeyDriver(DifferentialStores.Require(stores.ApiKeys), scope, plan.Universe);

    /// <inheritdoc />
    public override string Describe(ApiKeyStep step) => step switch
    {
        CreateKeyStep s => $"Create tenant={s.Tenant} name={s.Name} scopes=[{string.Join(",", s.Scopes)}] expires={(s.ExpiresAt is { } at ? BoundaryValues.Show(at) : "-")}",
        ListKeysStep s => $"List tenant={s.Tenant}",
        FindKeyStep s => $"Find slot={s.Slot}",
        RevokeKeyStep s => $"Revoke tenant={s.Tenant} slot={s.Slot}",
        TouchKeyStep s => $"Touch slot={s.Slot} usedAt={BoundaryValues.Show(s.UsedAt)}",
        _ => step.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public override string Print(ApiKeyPlan plan)
    {
        var builder = new StringBuilder("\n");
        SessionStateMachine.Pool(builder, "tenants", plan.Universe.Tenants);
        SessionStateMachine.Pool(builder, "names", plan.Universe.Names);

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            builder.Append("  ").Append(index + 1).Append(". ").AppendLine(Describe(plan.Steps[index]));
        }

        return builder.ToString();
    }

    private static Gen<ApiKeyStep> Step(ApiKeyUniverse u)
    {
        var tenant = Gen.Int[0, u.Tenants.Length - 1];
        var slot = Gen.Int[0, SlotCount - 1];
        var scopes = Gen.Frequency(
            (1, Gen.Const(Array.Empty<ApiKeyScope>())),
            (6, Gen.Enum<ApiKeyScope>().Array[1, 3]),
            (1, Gen.Enum<ApiKeyScope>().SelectMany(scope => Gen.Const(new[] { scope, scope }))));

        return Gen.Frequency(
            (16, tenant.SelectMany(t => Gen.Int[0, u.Names.Length - 1].SelectMany(n => scopes.SelectMany(s => Gen.OneOf(Expiries).Select(e => (ApiKeyStep)new CreateKeyStep(t, n, s, e)))))),
            (10, tenant.Select(static t => (ApiKeyStep)new ListKeysStep(t))),
            (16, slot.Select(static s => (ApiKeyStep)new FindKeyStep(s))),
            (12, tenant.SelectMany(t => slot.Select(s => (ApiKeyStep)new RevokeKeyStep(t, s)))),
            (8, slot.SelectMany(s => BoundaryValues.Instant.Select(at => (ApiKeyStep)new TouchKeyStep(s, at)))));
    }
}

/// <summary>The API key specification, written from the interface documentation.</summary>
internal sealed class ApiKeyModel(ApiKeyUniverse universe, ProviderTraits traits) : IStepModel<ApiKeyStep>
{
    private readonly List<Key> _keys = [];

    /// <inheritdoc />
    public ApiKeyStep Concretize(ApiKeyStep step) => step;

    /// <inheritdoc />
    public Observation Apply(ApiKeyStep step)
    {
        try
        {
            switch (step)
            {
                case CreateKeyStep s:
                    var tenant = traits.Text(Fold(universe.Tenants[s.Tenant]));
                    var key = new Key(_keys.Count, tenant, traits.Text(universe.Names[s.Name]), s.Scopes, s.ExpiresAt is { } at ? traits.Ticks(at) : null);
                    _keys.Add(key);

                    return Observation.Of("created " + (traits.CreateEchoesInputText ? RenderCreated(Fold(universe.Tenants[s.Tenant]), universe.Names[s.Name], key.Scopes) : RenderCreated(key)));
                case ListKeysStep s:
                    var listed = traits.Text(Fold(universe.Tenants[s.Tenant]));

                    return Observation.List("keys", [.. _keys.Where(k => string.Equals(k.Tenant, listed, StringComparison.Ordinal)).Select(Render)]);
                case FindKeyStep s:
                    return Observation.Of(s.Slot < _keys.Count ? Render(_keys[s.Slot]) : "null");
                case RevokeKeyStep s:
                    var revoking = traits.Text(Fold(universe.Tenants[s.Tenant]));

                    if (s.Slot < _keys.Count
                        && string.Equals(_keys[s.Slot].Tenant, revoking, StringComparison.Ordinal)
                        && !_keys[s.Slot].Revoked)
                    {
                        _keys[s.Slot].Revoked = true;

                        return Observation.Of("true");
                    }

                    return Observation.Of("false");
                case TouchKeyStep s:
                    if (s.Slot < _keys.Count)
                    {
                        _keys[s.Slot].LastUsed = traits.Ticks(s.UsedAt);
                    }

                    return Observation.Of("ok");
                default:
                    throw new InvalidOperationException(step.GetType().Name);
            }
        }
        catch (ArgumentException)
        {
            return Observation.Rejected();
        }
    }

    internal static string RenderCreated(string tenant, string name, IEnumerable<ApiKeyScope> scopes)
        => $"tenant={BoundaryValues.Show(tenant)} name={BoundaryValues.Show(name)} scopes=[{string.Join(",", scopes)}]";

    internal static string Render(int slot, string tenant, string name, IEnumerable<ApiKeyScope> scopes, long? expires, bool revoked, long? lastUsed)
        => $"key#{slot} {RenderCreated(tenant, name, scopes)} expires={expires?.ToString(CultureInfo.InvariantCulture) ?? "null"} revoked={(revoked ? "fresh" : "null")} used={lastUsed?.ToString(CultureInfo.InvariantCulture) ?? "null"}";

    private static string Fold(string tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentException("blank tenant", nameof(tenant));
        }

        return tenant.ToLowerInvariant();
    }

    private static string RenderCreated(Key key) => ApiKeyModel.RenderCreated(key.Tenant, key.Name, key.Scopes);

    private static string Render(Key key)
        => Render(key.Slot, key.Tenant, key.Name, key.Scopes, key.Expires, key.Revoked, key.LastUsed);

    private sealed class Key(int slot, string tenant, string name, ApiKeyScope[] scopes, long? expires)
    {
        public int Slot { get; } = slot;

        public string Tenant { get; } = tenant;

        public string Name { get; } = name;

        public ApiKeyScope[] Scopes { get; } = scopes;

        public long? Expires { get; } = expires;

        public bool Revoked { get; set; }

        public long? LastUsed { get; set; }
    }
}

/// <summary>Applies API key steps to a store.</summary>
internal sealed class ApiKeyDriver(IApiKeyStore store, DifferentialScope scope, ApiKeyUniverse universe) : IStepDriver<ApiKeyStep>
{
    private readonly List<(Guid Id, byte[] Hash)> _created = [];

    /// <inheritdoc />
    public async ValueTask<Observation> ApplyAsync(ApiKeyStep step)
    {
        switch (step)
        {
            case CreateKeyStep s:
                var result = await store.CreateAsync(new ApiKeyDraft
                {
                    TenantId = scope.Wrap(universe.Tenants[s.Tenant]),
                    Name = universe.Names[s.Name],
                    Scopes = s.Scopes,
                    ExpiresAt = s.ExpiresAt,
                });
                _created.Add((result.Record.Id, ApiKeyGenerator.ComputeHash(result.PlaintextKey)));

                return Observation.Of("created " + ApiKeyModel.RenderCreated(scope.Unwrap(result.Record.TenantId) ?? "<null>", result.Record.Name, result.Record.Scopes));
            case ListKeysStep s:
                var listed = await store.ListAsync(scope.Wrap(universe.Tenants[s.Tenant]));

                return Observation.List("keys", [.. listed.Select(Render)]);
            case FindKeyStep s:
                var hash = s.Slot < _created.Count ? _created[s.Slot].Hash : ApiKeyGenerator.ComputeHash("no-such-key-" + s.Slot.ToString(CultureInfo.InvariantCulture));
                var found = await store.FindByHashAsync(hash);

                return Observation.Of(found is null ? "null" : Render(found));
            case RevokeKeyStep s:
                var id = s.Slot < _created.Count ? _created[s.Slot].Id : Guid.NewGuid();

                return Observation.Of(await store.RevokeAsync(scope.Wrap(universe.Tenants[s.Tenant]), id) ? "true" : "false");
            case TouchKeyStep s:
                await store.TouchLastUsedAsync(s.Slot < _created.Count ? _created[s.Slot].Id : Guid.NewGuid(), s.UsedAt);

                return Observation.Of("ok");
            default:
                throw new InvalidOperationException(step.GetType().Name);
        }
    }

    private string Render(ApiKeyRecord record)
    {
        var slot = _created.FindIndex(pair => pair.Id == record.Id);

        return ApiKeyModel.Render(
            slot,
            scope.Unwrap(record.TenantId) ?? "<null>",
            record.Name,
            record.Scopes,
            record.ExpiresAt?.UtcTicks,
            record.RevokedAt is not null,
            record.LastUsedAt?.UtcTicks);
    }
}

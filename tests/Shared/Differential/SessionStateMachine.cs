using System.Globalization;
using System.Text;
using System.Text.Json;
using CsCheck;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Tests.Differential;

/// <summary>The keys, tenants and documents one session sequence draws from.</summary>
/// <param name="Ids">Session identifiers, some of them twins of others.</param>
/// <param name="Agents">Agent names, some of them twins of others.</param>
/// <param name="Tenants">Tenant spellings, some of them letter-case or dotted-i twins of others.</param>
/// <param name="Owners">Owner identities.</param>
/// <param name="States">State documents (JSON text).</param>
/// <param name="MafVersions">Producer version labels.</param>
internal sealed record SessionUniverse(
    string[] Ids,
    string[] Agents,
    string[] Tenants,
    string[] Owners,
    string[] States,
    string[] MafVersions)
{
    /// <summary>Resolves a tenant reference: an index into <see cref="Tenants"/>, or a blank value.</summary>
    /// <param name="reference">The index; -1 is the empty string and -2 is white space.</param>
    /// <returns>The spelling.</returns>
    public string Tenant(int reference) => reference switch
    {
        -1 => string.Empty,
        -2 => " \t ",
        _ => Tenants[reference],
    };
}

/// <summary>A generated session sequence.</summary>
/// <param name="Universe">The values the steps draw from.</param>
/// <param name="Steps">The steps.</param>
internal sealed record SessionPlan(SessionUniverse Universe, IReadOnlyList<SessionStep> Steps);

/// <summary>What the expected version of a conditional update is relative to.</summary>
internal enum VersionHint
{
    /// <summary>The version the record holds now.</summary>
    Current,

    /// <summary>One behind the version the record holds now.</summary>
    Stale,

    /// <summary>One ahead of the version the record holds now.</summary>
    Next,

    /// <summary>Zero.</summary>
    Zero,

    /// <summary>Minus one.</summary>
    Negative,

    /// <summary>The largest <see cref="long"/>.</summary>
    Max,
}

/// <summary>The fields of a session write.</summary>
/// <param name="Id">Index into the identifiers.</param>
/// <param name="Agent">Index into the agent names.</param>
/// <param name="State">Index into the state documents.</param>
/// <param name="Created">The creation time the caller supplies.</param>
/// <param name="Updated">The update time the caller supplies.</param>
/// <param name="RecordTenant">The tenant the record carries, or <see langword="null"/> to use the ambient one.</param>
/// <param name="Owner">Index into the owners, or <see langword="null"/>.</param>
/// <param name="Maf">Index into the version labels, or <see langword="null"/>.</param>
internal sealed record SessionWrite(
    int Id,
    int Agent,
    int State,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    int? RecordTenant,
    int? Owner,
    int? Maf);

/// <summary>One call on a session store.</summary>
/// <param name="Tenant">The ambient tenant the call runs under (see <see cref="SessionUniverse.Tenant"/>).</param>
internal abstract record SessionStep(int Tenant);

/// <summary>An unconditional save.</summary>
internal sealed record SaveStep(int Tenant, SessionWrite Write) : SessionStep(Tenant);

/// <summary>A create that succeeds only for a new session.</summary>
internal sealed record TryCreateStep(int Tenant, SessionWrite Write) : SessionStep(Tenant);

/// <summary>A conditional update.</summary>
internal sealed record TryUpdateStep(int Tenant, SessionWrite Write, VersionHint Hint, long Expected) : SessionStep(Tenant);

/// <summary>A read of one session.</summary>
internal sealed record GetStep(int Tenant, int Id) : SessionStep(Tenant);

/// <summary>A delete of one session.</summary>
internal sealed record DeleteStep(int Tenant, int Id) : SessionStep(Tenant);

/// <summary>A listing.</summary>
internal sealed record QueryStep(int Tenant, int? Agent, int? QueryTenant, int? Owner, int Skip, int Take) : SessionStep(Tenant);

/// <summary>
/// The session store state machine: save, create, conditional update, read, delete and list, over a pool of
/// identifiers, agents, tenants and owners that contains twins of one another.
/// </summary>
internal sealed class SessionStateMachine : StateMachine<SessionPlan, SessionStep>
{
    // The identifier column holds 200 characters; the product refuses a longer session identifier before a store sees it.
    private const int SessionIdMaxLength = 200;

    private static readonly int[] Limits = [0, 1, 2, 3, 5, 100, int.MaxValue, -1, int.MinValue];

    /// <inheritdoc />
    public override string Name => "ISessionStore";

    /// <inheritdoc />
    public override Gen<SessionPlan> Plans(int maxSteps, ProviderTraits traits)
    {
        ArgumentNullException.ThrowIfNull(traits);

        var universe =
            from ids in BoundaryValues.Pool(4, BoundaryValues.Trimmed(SessionIdMaxLength), SessionIdMaxLength, traits.TrailingSpaceInsensitiveKeys)
            from agents in BoundaryValues.Pool(3, BoundaryValues.Trimmed(200), 200)
            from tenants in BoundaryValues.TenantPool(3)
            from owners in traits.OwnerFilterIgnoresCase ? BoundaryValues.AsciiIdentifierPool(3, 200) : BoundaryValues.Pool(3, BoundaryValues.Trimmed(200), 200)
            from states in BoundaryValues.JsonDocument.Array[3]
            from versions in BoundaryValues.Text(40).Array[2]
            select new SessionUniverse(ids, agents, tenants, owners, states, versions);

        return universe.SelectMany(
            u => Step(u).List[1, maxSteps],
            static (u, steps) => new SessionPlan(u, steps));
    }

    /// <inheritdoc />
    public override IReadOnlyList<SessionStep> Steps(SessionPlan plan) => plan.Steps;

    /// <inheritdoc />
    public override IStepModel<SessionStep> NewModel(SessionPlan plan, ProviderTraits traits) => new SessionModel(plan.Universe, traits);

    /// <inheritdoc />
    public override IStepDriver<SessionStep> NewDriver(SessionPlan plan, DifferentialStores stores, DifferentialScope scope)
        => new SessionDriver(DifferentialStores.Require(stores.Sessions), stores.Tenant, scope, plan.Universe);

    /// <inheritdoc />
    public override string Describe(SessionStep step) => step switch
    {
        SaveStep s => $"Save   tenant={s.Tenant} {Write(s.Write)}",
        TryCreateStep s => $"TryCreate tenant={s.Tenant} {Write(s.Write)}",
        TryUpdateStep s => $"TryUpdate tenant={s.Tenant} expected={s.Expected} ({s.Hint}) {Write(s.Write)}",
        GetStep s => $"Get tenant={s.Tenant} id={s.Id}",
        DeleteStep s => $"Delete tenant={s.Tenant} id={s.Id}",
        QueryStep s => $"Query tenant={s.Tenant} agent={s.Agent?.ToString(CultureInfo.InvariantCulture) ?? "-"} queryTenant={s.QueryTenant?.ToString(CultureInfo.InvariantCulture) ?? "-"} owner={s.Owner?.ToString(CultureInfo.InvariantCulture) ?? "-"} skip={s.Skip} take={s.Take}",
        _ => step.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public override string Print(SessionPlan plan)
    {
        var builder = new StringBuilder("\n");
        Pool(builder, "ids", plan.Universe.Ids);
        Pool(builder, "agents", plan.Universe.Agents);
        Pool(builder, "tenants", plan.Universe.Tenants);
        Pool(builder, "owners", plan.Universe.Owners);
        Pool(builder, "states", plan.Universe.States);
        Pool(builder, "mafVersions", plan.Universe.MafVersions);

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            builder.Append("  ").Append(index + 1).Append(". ").AppendLine(Describe(plan.Steps[index]));
        }

        return builder.ToString();
    }

    internal static void Pool(StringBuilder builder, string name, IReadOnlyList<string> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            builder.Append("  ").Append(name).Append('[').Append(index).Append("] = ").AppendLine(BoundaryValues.Show(values[index]));
        }
    }

    private static string Write(SessionWrite w)
        => $"id={w.Id} agent={w.Agent} state={w.State} created={BoundaryValues.Show(w.Created)} updated={BoundaryValues.Show(w.Updated)} recordTenant={w.RecordTenant?.ToString(CultureInfo.InvariantCulture) ?? "-"} owner={w.Owner?.ToString(CultureInfo.InvariantCulture) ?? "-"} maf={w.Maf?.ToString(CultureInfo.InvariantCulture) ?? "-"}";

    private static Gen<int> TenantReference(SessionUniverse u)
        => Gen.Frequency(
            (96, Gen.Int[0, u.Tenants.Length - 1]),
            (2, Gen.Const(-1)),
            (2, Gen.Const(-2)));

    private static Gen<SessionStep> Step(SessionUniverse u)
    {
        var tenant = TenantReference(u);
        var id = Gen.Int[0, u.Ids.Length - 1];
        var agent = Gen.Int[0, u.Agents.Length - 1];
        var owner = Gen.Int[0, u.Owners.Length - 1];
        var optionalTenant = Gen.Frequency((7, Gen.Const((int?)null)), (3, TenantReference(u).Select(static value => (int?)value)));
        var optionalOwner = Gen.Frequency((5, Gen.Const((int?)null)), (5, owner.Select(static value => (int?)value)));
        var optionalMaf = Gen.Frequency((5, Gen.Const((int?)null)), (5, Gen.Int[0, u.MafVersions.Length - 1].Select(static value => (int?)value)));
        var limit = Gen.Frequency((3, Gen.OneOf(Limits)), (2, Gen.Int[0, 6]));

        var write =
            from i in id
            from a in agent
            from state in Gen.Int[0, u.States.Length - 1]
            from created in BoundaryValues.PlainInstant
            from updated in BoundaryValues.Instant
            from recordTenant in optionalTenant
            from o in optionalOwner
            from maf in optionalMaf
            select new SessionWrite(i, a, state, created, updated, recordTenant, o, maf);

        var hint = Gen.Frequency(
            (10, Gen.Const(VersionHint.Current)),
            (3, Gen.Const(VersionHint.Stale)),
            (2, Gen.Const(VersionHint.Next)),
            (1, Gen.Const(VersionHint.Zero)),
            (1, Gen.Const(VersionHint.Negative)),
            (1, Gen.Const(VersionHint.Max)));

        var optionalAgent = Gen.Frequency((5, Gen.Const((int?)null)), (5, agent.Select(static value => (int?)value)));
        var optionalQueryTenant = Gen.Frequency((8, Gen.Const((int?)null)), (2, Gen.Int[0, u.Tenants.Length - 1].Select(static value => (int?)value)));

        return Gen.Frequency(
            (22, tenant.SelectMany(t => write.Select(w => (SessionStep)new SaveStep(t, w)))),
            (12, tenant.SelectMany(t => write.Select(w => (SessionStep)new TryCreateStep(t, w)))),
            (14, tenant.SelectMany(t => write.SelectMany(w => hint.Select(h => (SessionStep)new TryUpdateStep(t, w, h, 0))))),
            (16, tenant.SelectMany(t => id.Select(i => (SessionStep)new GetStep(t, i)))),
            (8, tenant.SelectMany(t => id.Select(i => (SessionStep)new DeleteStep(t, i)))),
            (22, tenant.SelectMany(t => optionalAgent.SelectMany(a => optionalQueryTenant.SelectMany(q => optionalOwner.SelectMany(o => limit.SelectMany(skip => limit.Select(take => (SessionStep)new QueryStep(t, a, q, o, skip, take)))))))));
    }
}

/// <summary>The session specification, written from the interface documentation.</summary>
internal sealed class SessionModel(SessionUniverse universe, ProviderTraits traits) : IStepModel<SessionStep>
{
    private readonly Dictionary<(string Tenant, string Key), Row> _rows = [];

    /// <inheritdoc />
    public SessionStep Concretize(SessionStep step)
    {
        if (step is not TryUpdateStep update)
        {
            return step;
        }

        var current = Find(update.Tenant, update.Write.RecordTenant, update.Write.Id)?.Version ?? 1;

        var expected = update.Hint switch
        {
            VersionHint.Current => current,
            VersionHint.Stale => current - 1,
            VersionHint.Next => current + 1,
            VersionHint.Zero => 0,
            VersionHint.Negative => -1,
            _ => long.MaxValue,
        };

        return update with { Expected = expected };
    }

    /// <inheritdoc />
    public Observation Apply(SessionStep step)
    {
        try
        {
            return step switch
            {
                SaveStep s => Save(s),
                TryCreateStep s => TryCreate(s),
                TryUpdateStep s => TryUpdate(s),
                GetStep s => Get(s),
                DeleteStep s => Delete(s),
                QueryStep s => Query(s),
                _ => throw new InvalidOperationException(step.GetType().Name),
            };
        }
        catch (ArgumentException)
        {
            return Observation.Rejected();
        }
    }

    private static string Fold(string tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentException("blank tenant", nameof(tenant));
        }

        return tenant.ToLowerInvariant();
    }

    private string TenantOf(string spelling) => traits.Text(Fold(spelling));

    private string KeyOf(string id)
    {
        var text = traits.Text(id);

        return traits.TrailingSpaceInsensitiveKeys ? text.TrimEnd(' ') : text;
    }

    private string EffectiveTenant(int ambient, int? recordTenant)
        => TenantOf(universe.Tenant(recordTenant ?? ambient));

    private Row? Find(int ambient, int? recordTenant, int id)
    {
        try
        {
            return _rows.GetValueOrDefault((EffectiveTenant(ambient, recordTenant), KeyOf(universe.Ids[id])));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private Row NewRow(SessionWrite w, string tenant, long version, long? created = null, string? owner = null)
        => new(
            traits.Text(universe.Ids[w.Id]),
            tenant,
            traits.Text(universe.Agents[w.Agent]),
            CanonicalJson.Of(universe.States[w.State], traits.Text),
            created ?? traits.Ticks(w.Created),
            traits.Ticks(w.Updated),
            owner ?? (w.Owner is { } index ? traits.Text(universe.Owners[index]) : null),
            w.Maf is { } maf ? traits.Text(universe.MafVersions[maf]) : null,
            version);

    private Observation Save(SaveStep step)
    {
        var tenant = EffectiveTenant(step.Tenant, step.Write.RecordTenant);
        var key = (tenant, KeyOf(universe.Ids[step.Write.Id]));

        _rows[key] = _rows.TryGetValue(key, out var existing)
            ? NewRow(step.Write, tenant, existing.Version + 1, existing.Created, existing.Owner ?? NewRow(step.Write, tenant, 0).Owner) with { Id = existing.Id }
            : NewRow(step.Write, tenant, 1);

        return Observation.Of("saved");
    }

    private Observation TryCreate(TryCreateStep step)
    {
        var tenant = EffectiveTenant(step.Tenant, step.Write.RecordTenant);
        var key = (tenant, KeyOf(universe.Ids[step.Write.Id]));

        if (_rows.ContainsKey(key))
        {
            return Observation.Of("false");
        }

        _rows[key] = NewRow(step.Write, tenant, 1);

        return Observation.Of("true");
    }

    private Observation TryUpdate(TryUpdateStep step)
    {
        var tenant = EffectiveTenant(step.Tenant, step.Write.RecordTenant);
        var key = (tenant, KeyOf(universe.Ids[step.Write.Id]));

        if (!_rows.TryGetValue(key, out var existing) || existing.Version != step.Expected)
        {
            return Observation.Of("false");
        }

        _rows[key] = NewRow(step.Write, tenant, step.Expected + 1, existing.Created, existing.Owner ?? NewRow(step.Write, tenant, 0).Owner) with { Id = existing.Id };

        return Observation.Of("true");
    }

    private Observation Get(GetStep step)
    {
        var tenant = TenantOf(universe.Tenant(step.Tenant));

        return Observation.Of(
            _rows.TryGetValue((tenant, KeyOf(universe.Ids[step.Id])), out var row)
                ? Render(traits.ReadEchoesRequestedKey ? row with { Id = universe.Ids[step.Id] } : row)
                : "null");
    }

    private Observation Delete(DeleteStep step)
    {
        var tenant = TenantOf(universe.Tenant(step.Tenant));

        return Observation.Of(_rows.Remove((tenant, KeyOf(universe.Ids[step.Id]))) ? "true" : "false");
    }

    private Observation Query(QueryStep step)
    {
        var tenant = step.QueryTenant is { } q ? TenantOf(universe.Tenant(q)) : TenantOf(universe.Tenant(step.Tenant));
        var agent = step.Agent is { } a ? traits.Text(universe.Agents[a]) : null;
        var owner = step.Owner is { } o ? traits.Text(universe.Owners[o]) : null;

        var sorted = _rows.Values
            .Where(row => string.Equals(row.Tenant, tenant, StringComparison.Ordinal))
            .Where(row => agent is null || string.Equals(row.Agent, agent, StringComparison.Ordinal))
            .Where(row => owner is null || string.Equals(row.Owner, owner, traits.OwnerFilterIgnoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .OrderByDescending(static row => row.Updated)
            .Select(static row => (Key: row.Updated, Rendered: Render(row)))
            .ToList();

        return TiedPage.Cut("sessions", sorted, Math.Max(step.Skip, 0), Math.Max(step.Take, 0));
    }

    internal static string Render(Row row)
        => $"id={BoundaryValues.Show(row.Id)} tenant={BoundaryValues.Show(row.Tenant)} agent={BoundaryValues.Show(row.Agent)} owner={BoundaryValues.Show(row.Owner)} v={row.Version} created={row.Created} updated={row.Updated} maf={BoundaryValues.Show(row.Maf)} state={row.State}";

    internal sealed record Row(string Id, string Tenant, string Agent, string State, long Created, long Updated, string? Owner, string? Maf, long Version);
}

/// <summary>Applies session steps to a store.</summary>
internal sealed class SessionDriver(ISessionStore store, MutableTenantContext ambient, DifferentialScope scope, SessionUniverse universe)
    : IStepDriver<SessionStep>
{
    /// <inheritdoc />
    public async ValueTask<Observation> ApplyAsync(SessionStep step)
    {
        ambient.TenantId = scope.Wrap(universe.Tenant(step.Tenant));

        switch (step)
        {
            case SaveStep s:
                await store.SaveAsync(Record(s.Write));

                return Observation.Of("saved");
            case TryCreateStep s:
                return Observation.Of(await store.TryCreateAsync(Record(s.Write)) ? "true" : "false");
            case TryUpdateStep s:
                return Observation.Of(await store.TryUpdateAsync(Record(s.Write), s.Expected) ? "true" : "false");
            case GetStep s:
                return Observation.Of(await store.GetAsync(universe.Ids[s.Id]) is { } found ? Render(found) : "null");
            case DeleteStep s:
                return Observation.Of(await store.DeleteAsync(universe.Ids[s.Id]) ? "true" : "false");
            case QueryStep s:
                var query = new SessionQuery
                {
                    AgentName = s.Agent is { } a ? universe.Agents[a] : null,
                    TenantId = s.QueryTenant is { } q ? scope.Wrap(universe.Tenant(q)) : null,
                    OwnerId = s.Owner is { } o ? universe.Owners[o] : null,
                    Skip = s.Skip,
                    Take = s.Take,
                };
                var list = await store.QueryAsync(query);

                return Observation.List("sessions", list.Select(Render).ToList());
            default:
                throw new InvalidOperationException(step.GetType().Name);
        }
    }

    private SessionRecord Record(SessionWrite w)
        => new()
        {
            Id = universe.Ids[w.Id],
            AgentName = universe.Agents[w.Agent],
            State = JsonDocument.Parse(universe.States[w.State]).RootElement.Clone(),
            CreatedAt = w.Created,
            UpdatedAt = w.Updated,
            TenantId = w.RecordTenant is { } t ? scope.Wrap(universe.Tenant(t)) : null,
            OwnerId = w.Owner is { } o ? universe.Owners[o] : null,
            StateMafVersion = w.Maf is { } m ? universe.MafVersions[m] : null,

            // Stores take the version from their own state and the caller's expected version, never from this field.
            Version = 99,
        };

    private string Render(SessionRecord record)
        => SessionModel.Render(new SessionModel.Row(
            record.Id,
            scope.Unwrap(record.TenantId) ?? "<null>",
            record.AgentName,
            CanonicalJson.Of(record.State.GetRawText()),
            record.CreatedAt.UtcTicks,
            record.UpdatedAt.UtcTicks,
            record.OwnerId,
            record.StateMafVersion,
            record.Version));
}

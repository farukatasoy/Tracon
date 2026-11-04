using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using CsCheck;

namespace Tracon.Tests.Differential;

/// <summary>The tenants, names and texts one webhook sequence draws from.</summary>
/// <param name="Tenants">Tenant spellings, some of them twins of others.</param>
/// <param name="Names">Subscription names, some of them twins of others (a name is compared by its characters).</param>
/// <param name="Urls">Endpoint addresses.</param>
/// <param name="Texts">Header values, configuration keys, payloads and errors.</param>
/// <param name="Instants">Times a record is created and updated at.</param>
internal sealed record WebhookUniverse(string[] Tenants, string[] Names, string[] Urls, string[] Texts, DateTimeOffset[] Instants);

/// <summary>A generated webhook sequence.</summary>
/// <param name="Universe">The values the steps draw from.</param>
/// <param name="Steps">The steps.</param>
internal sealed record WebhookPlan(WebhookUniverse Universe, IReadOnlyList<WebhookStep> Steps);

/// <summary>One call on a webhook store.</summary>
internal abstract record WebhookStep;

/// <summary>The saving of a subscription; the identifier is decided when the step is concretized.</summary>
/// <param name="Tenant">The tenant spelling.</param>
/// <param name="Name">The subscription name.</param>
/// <param name="Url">The endpoint address.</param>
/// <param name="Events">The events it names (indexes into the fixed list), in order, without repeats.</param>
/// <param name="SecretKey">The configuration key of the secret (a text), or <see langword="null"/>.</param>
/// <param name="Headers">The header names (indexes into the fixed list) and the value text of each.</param>
/// <param name="HeaderKeys">The header names that read their value from configuration, and the key text of each.</param>
/// <param name="Enabled">Whether the subscription is on.</param>
/// <param name="Failures">The failure counter the subscription carries.</param>
/// <param name="Created">The creation time.</param>
/// <param name="Updated">The update time.</param>
/// <param name="FreshId">Whether a subscription that already holds the name is saved under an identifier of its own.</param>
/// <param name="IdSlot">The identifier slot; -1 until the model decides.</param>
internal sealed record SaveSubscriptionStep(int Tenant, int Name, int Url, int[] Events, int? SecretKey, (int Name, int Value)[] Headers, (int Name, int Value)[] HeaderKeys, bool Enabled, int Failures, int Created, int Updated, bool FreshId, int IdSlot) : WebhookStep;

/// <summary>The read of a subscription by name.</summary>
internal sealed record GetSubscriptionStep(int Tenant, int Name) : WebhookStep;

/// <summary>The listing of a tenant's subscriptions.</summary>
internal sealed record ListSubscriptionsStep(int Tenant) : WebhookStep;

/// <summary>The search for the subscriptions of an event.</summary>
internal sealed record FindForEventStep(int Tenant, int Event) : WebhookStep;

/// <summary>The removal of a subscription.</summary>
internal sealed record DeleteSubscriptionStep(int Tenant, int Name) : WebhookStep;

/// <summary>The record of a delivery outcome on a subscription.</summary>
internal sealed record RecordOutcomeStep(int Slot, bool Succeeded, int Threshold, int Updated) : WebhookStep;

/// <summary>The creation of a delivery; the delivery gets a slot of its own when the step is concretized.</summary>
internal sealed record CreateDeliveryStep(int Subscription, int Tenant, int Event, int Payload, int Created, int DeliverySlot) : WebhookStep;

/// <summary>The read of a delivery.</summary>
internal sealed record GetDeliveryStep(int Delivery) : WebhookStep;

/// <summary>The record of the result of a delivery attempt.</summary>
internal sealed record RecordResultStep(int Delivery, WebhookDeliveryStatus Status, int Attempt, int? Code, int? Error, int Recorded) : WebhookStep;

/// <summary>The listing of deliveries.</summary>
internal sealed record QueryDeliveriesStep(int Tenant, int? Subscription, WebhookDeliveryStatus? Status, int Skip, int Take) : WebhookStep;

/// <summary>
/// The webhook store state machine: subscriptions (upsert by name, event search, removal with their deliveries), the
/// failure counter that disables a subscription, and delivery records with their results.
/// </summary>
internal sealed class WebhookStateMachine : StateMachine<WebhookPlan, WebhookStep>
{
    internal static readonly string[] EventNames = ["run.completed", "run.failed", "Run.Completed", "job.failed", "x"];

    internal static readonly string[] HeaderNames = ["X-Team", "x-env", "Authorization", "Cookie"];

    private const int SubscriptionSlots = 5;

    /// <inheritdoc />
    public override string Name => "IWebhookStore";

    /// <inheritdoc />
    public override Gen<WebhookPlan> Plans(int maxSteps, ProviderTraits traits)
    {
        ArgumentNullException.ThrowIfNull(traits);

        var universe =
            from tenants in BoundaryValues.TenantPool(3)
            from names in BoundaryValues.Pool(3, BoundaryValues.Trimmed(100), 100, traits.TrailingSpaceInsensitiveKeys)
            from urls in BoundaryValues.Text(150).Array[2]
            from texts in BoundaryValues.Text(80).Array[4]
            from instants in BoundaryValues.Instant.Array[3]
            select new WebhookUniverse(tenants, names, urls, texts, instants);

        return universe.SelectMany(
            u => Step(u).List[1, maxSteps],
            static (u, steps) => new WebhookPlan(u, steps));
    }

    /// <inheritdoc />
    public override IReadOnlyList<WebhookStep> Steps(WebhookPlan plan) => plan.Steps;

    /// <inheritdoc />
    public override IReadOnlyList<WebhookStep> RaceSteps(WebhookPlan plan)
    {
        // Two subscriptions exist before the tasks start; the tasks then save, delete, record outcomes and read them.
        var setup = Enumerable.Range(0, 2)
            .Select(index => (WebhookStep)new SaveSubscriptionStep(0, index, 0, [0, 1], null, [], [], true, 0, 0, 0, false, -1))
            .ToList();

        return [.. setup, .. plan.Steps.Where(static step => step is not (CreateDeliveryStep or GetDeliveryStep or RecordResultStep or QueryDeliveriesStep))];
    }

    /// <inheritdoc />
    public override int? RaceSetupLength(WebhookPlan plan) => 2;

    /// <inheritdoc />
    public override IReadOnlyList<WebhookStep> Snapshot(WebhookPlan plan)
        => [.. Enumerable.Range(0, plan.Universe.Tenants.Length).Select(static tenant => (WebhookStep)new ListSubscriptionsStep(tenant))];

    /// <inheritdoc />
    public override IStepModel<WebhookStep> NewModel(WebhookPlan plan, ProviderTraits traits) => new WebhookModel(plan.Universe, traits);

    /// <inheritdoc />
    public override IStepDriver<WebhookStep> NewDriver(WebhookPlan plan, DifferentialStores stores, DifferentialScope scope)
        => new WebhookDriver(DifferentialStores.Require(stores.Webhooks), scope, plan.Universe);

    /// <inheritdoc />
    public override string Describe(WebhookStep step) => step switch
    {
        SaveSubscriptionStep s => $"SaveSubscription tenant={s.Tenant} name={s.Name} url={s.Url} events=[{string.Join(",", s.Events)}] secret={s.SecretKey?.ToString(CultureInfo.InvariantCulture) ?? "-"} headers=[{string.Join(",", s.Headers.Select(static pair => $"{pair.Name}={pair.Value}"))}] headerKeys=[{string.Join(",", s.HeaderKeys.Select(static pair => $"{pair.Name}={pair.Value}"))}] enabled={s.Enabled} failures={s.Failures} created={s.Created} updated={s.Updated} freshId={s.FreshId} id={s.IdSlot}",
        GetSubscriptionStep s => $"GetSubscription tenant={s.Tenant} name={s.Name}",
        ListSubscriptionsStep s => $"ListSubscriptions tenant={s.Tenant}",
        FindForEventStep s => $"FindForEvent tenant={s.Tenant} event={s.Event}",
        DeleteSubscriptionStep s => $"DeleteSubscription tenant={s.Tenant} name={s.Name}",
        RecordOutcomeStep s => $"RecordOutcome subscription={s.Slot} succeeded={s.Succeeded} threshold={s.Threshold} updated={s.Updated}",
        CreateDeliveryStep s => $"CreateDelivery subscription={s.Subscription} tenant={s.Tenant} event={s.Event} payload={s.Payload} created={s.Created} slot={s.DeliverySlot}",
        GetDeliveryStep s => $"GetDelivery delivery={s.Delivery}",
        RecordResultStep s => $"RecordResult delivery={s.Delivery} status={s.Status} attempt={s.Attempt} code={s.Code?.ToString(CultureInfo.InvariantCulture) ?? "-"} error={s.Error?.ToString(CultureInfo.InvariantCulture) ?? "-"} recorded={s.Recorded}",
        QueryDeliveriesStep s => $"QueryDeliveries tenant={s.Tenant} subscription={s.Subscription?.ToString(CultureInfo.InvariantCulture) ?? "-"} status={s.Status?.ToString() ?? "-"} skip={s.Skip} take={s.Take}",
        _ => step.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public override string Print(WebhookPlan plan)
    {
        var builder = new StringBuilder("\n");
        SessionStateMachine.Pool(builder, "tenants", plan.Universe.Tenants);
        SessionStateMachine.Pool(builder, "names", plan.Universe.Names);
        SessionStateMachine.Pool(builder, "urls", plan.Universe.Urls);
        SessionStateMachine.Pool(builder, "texts", plan.Universe.Texts);

        for (var index = 0; index < plan.Universe.Instants.Length; index++)
        {
            builder.Append("  instants[").Append(index).Append("] = ").AppendLine(BoundaryValues.Show(plan.Universe.Instants[index]));
        }

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            builder.Append("  ").Append(index + 1).Append(". ").AppendLine(Describe(plan.Steps[index]));
        }

        return builder.ToString();
    }

    private static Gen<WebhookStep> Step(WebhookUniverse u)
    {
        var tenant = Gen.Int[0, u.Tenants.Length - 1];
        var name = Gen.Int[0, u.Names.Length - 1];
        var slot = Gen.Int[0, SubscriptionSlots - 1];
        var instant = Gen.Int[0, u.Instants.Length - 1];
        var text = Gen.Int[0, u.Texts.Length - 1];
        var events = Gen.Int[0, EventNames.Length - 1].Array[0, 3].Select(static picked => picked.Distinct().ToArray());
        var headers = Gen.Int[0, HeaderNames.Length - 1].Array[0, 3].SelectMany(picked => text.Array[picked.Length].Select(values => picked.Distinct().Select((header, index) => (Name: header, Value: values[index])).ToArray()));
        var save =
            from t in tenant
            from n in name
            from url in Gen.Int[0, u.Urls.Length - 1]
            from e in events
            from secret in Gen.Frequency((2, Gen.Const((int?)null)), (1, text.Select(static x => (int?)x)))
            from h in headers
            from hk in headers
            from enabled in Gen.Frequency((5, Gen.Const(true)), (1, Gen.Const(false)))
            from failures in Gen.Frequency((6, Gen.Const(0)), (1, Gen.Int[1, 3]))
            from created in instant
            from updated in instant
            from fresh in Gen.Frequency((3, Gen.Const(false)), (1, Gen.Const(true)))
            select (WebhookStep)new SaveSubscriptionStep(t, n, url, e, secret, h, hk, enabled, failures, created, updated, fresh, -1);
        var outcome =
            from s in slot
            from ok in Gen.Frequency((2, Gen.Const(true)), (5, Gen.Const(false)))
            from threshold in Gen.Frequency((6, Gen.Int[1, 4]), (1, Gen.Const(int.MaxValue)))
            from updated in instant
            select (WebhookStep)new RecordOutcomeStep(s, ok, threshold, updated);
        var delivery =
            from s in slot
            from t in tenant
            from e in Gen.Int[0, EventNames.Length - 1]
            from payload in text
            from created in instant
            select (WebhookStep)new CreateDeliveryStep(s, t, e, payload, created, -1);
        var result =
            from d in Gen.Int[0, 7]
            from status in Gen.Enum<WebhookDeliveryStatus>()
            from attempt in Gen.Int[0, 6]
            from code in Gen.Frequency((2, Gen.Const((int?)null)), (3, Gen.OneOf(200, 204, 400, 500, 503).Select(static x => (int?)x)))
            from error in Gen.Frequency((2, Gen.Const((int?)null)), (1, text.Select(static x => (int?)x)))
            from recorded in instant
            select (WebhookStep)new RecordResultStep(d, status, attempt, code, error, recorded);
        var query =
            from t in tenant
            from s in Gen.Frequency((3, Gen.Const((int?)null)), (1, slot.Select(static x => (int?)x)))
            from status in Gen.Frequency((3, Gen.Const((WebhookDeliveryStatus?)null)), (1, Gen.Enum<WebhookDeliveryStatus>().Select(static x => (WebhookDeliveryStatus?)x)))
            from skip in Gen.Frequency((6, Gen.Const(0)), (2, Gen.Int[1, 4]), (1, Gen.Int[-3, -1]))
            from take in Gen.Frequency((6, Gen.Const(50)), (2, Gen.Int[0, 4]), (1, Gen.Int[-3, -1]), (1, Gen.Const(int.MaxValue)))
            select (WebhookStep)new QueryDeliveriesStep(t, s, status, skip, take);

        return Gen.Frequency(
            (18, save),
            (8, tenant.SelectMany(t => name.Select(n => (WebhookStep)new GetSubscriptionStep(t, n)))),
            (6, tenant.Select(static t => (WebhookStep)new ListSubscriptionsStep(t))),
            (8, tenant.SelectMany(t => Gen.Int[0, EventNames.Length - 1].Select(e => (WebhookStep)new FindForEventStep(t, e)))),
            (6, tenant.SelectMany(t => name.Select(n => (WebhookStep)new DeleteSubscriptionStep(t, n)))),
            (16, outcome),
            (10, delivery),
            (6, Gen.Int[0, 7].Select(static d => (WebhookStep)new GetDeliveryStep(d))),
            (10, result),
            (8, query));
    }
}

/// <summary>The webhook specification, written from the interface documentation and the behaviour its contract tests pin.</summary>
internal sealed class WebhookModel(WebhookUniverse universe, ProviderTraits traits) : IStepModel<WebhookStep>
{
    private readonly Dictionary<(string Tenant, string Name), Sub> _subs = [];
    private readonly List<Delivery> _deliveries = [];
    private int _nextSub;
    private int _nextDelivery;

    /// <summary>Renders a subscription in the one form both sides use.</summary>
    /// <returns>The text.</returns>
    internal static string RenderSub(int id, string tenant, string name, string url, IEnumerable<string> events, string? secret, IEnumerable<KeyValuePair<string, string>> headers, IEnumerable<KeyValuePair<string, string>> headerKeys, bool enabled, int failures, long created, long updated)
        => $"sub#{id} tenant={BoundaryValues.Show(tenant)} name={BoundaryValues.Show(name)} url={BoundaryValues.Show(url)} events=[{string.Join(",", events.Select(BoundaryValues.Show))}] secret={(secret is null ? "-" : BoundaryValues.Show(secret))} headers={{{Pairs(headers)}}} headerKeys={{{Pairs(headerKeys)}}} enabled={enabled} failures={failures.ToString(CultureInfo.InvariantCulture)} created={created.ToString(CultureInfo.InvariantCulture)} updated={updated.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Renders a delivery in the one form both sides use.</summary>
    /// <returns>The text.</returns>
    internal static string RenderDelivery(int id, int sub, string tenant, string eventType, string payload, WebhookDeliveryStatus status, int attempt, int? code, string? error, long created, long? delivered)
        => $"delivery#{id} sub#{sub} tenant={BoundaryValues.Show(tenant)} event={BoundaryValues.Show(eventType)} payload={BoundaryValues.Show(payload)} status={status} attempt={attempt.ToString(CultureInfo.InvariantCulture)} code={code?.ToString(CultureInfo.InvariantCulture) ?? "-"} error={(error is null ? "-" : BoundaryValues.Show(error))} created={created.ToString(CultureInfo.InvariantCulture)} delivered={delivered?.ToString(CultureInfo.InvariantCulture) ?? "-"}";

    private static string Pairs(IEnumerable<KeyValuePair<string, string>> pairs)
        => string.Join(";", pairs.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => BoundaryValues.Show(pair.Key) + "=" + BoundaryValues.Show(pair.Value)));

    /// <inheritdoc />
    public WebhookStep Concretize(WebhookStep step)
    {
        switch (step)
        {
            case SaveSubscriptionStep save:
                return _subs.TryGetValue((Canonical(save.Tenant), traits.Text(universe.Names[save.Name])), out var held)
                    ? save with { IdSlot = save.FreshId ? _nextSub++ : held.IdSlot }
                    : save with { IdSlot = _nextSub++ };
            case CreateDeliveryStep create:
                // A delivery belongs to a subscription that exists and carries that subscription's tenant: it is created by
                // the delivery job for a subscription it found, so a call that names neither is not one the product makes.
                if (SubOf(create.Subscription) is not { } owner)
                {
                    return new ListSubscriptionsStep(create.Tenant);
                }

                var sameTenant = string.Equals(Canonical(create.Tenant), owner.Tenant, StringComparison.Ordinal);

                return create with { Tenant = sameTenant ? create.Tenant : owner.TenantIndex, DeliverySlot = _nextDelivery++ };
            case GetDeliveryStep get:
                return get with { Delivery = _nextDelivery == 0 ? 0 : get.Delivery % _nextDelivery };
            case RecordResultStep result:
                // A result for one of the deliveries created so far (a delivery that is gone is a no-op for the store).
                return result with { Delivery = _nextDelivery == 0 ? 0 : result.Delivery % _nextDelivery };
            default:
                return step;
        }
    }

    /// <inheritdoc />
    public Observation Apply(WebhookStep step)
    {
        try
        {
            return step switch
            {
                SaveSubscriptionStep s => Save(s),
                GetSubscriptionStep s => Get(s),
                ListSubscriptionsStep s => List(s),
                FindForEventStep s => Find(s),
                DeleteSubscriptionStep s => Delete(s),
                RecordOutcomeStep s => Outcome(s),
                CreateDeliveryStep s => Create(s),
                GetDeliveryStep s => GetDelivery(s),
                RecordResultStep s => Record(s),
                QueryDeliveriesStep s => Query(s),
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

    private string Canonical(int tenant) => traits.Text(Fold(universe.Tenants[tenant]));

    private Sub? SubOf(int slot) => _subs.Values.FirstOrDefault(sub => sub.IdSlot == slot);

    private Observation Save(SaveSubscriptionStep s)
    {
        var tenant = Canonical(s.Tenant);
        var name = traits.Text(universe.Names[s.Name]);
        var created = traits.Ticks(universe.Instants[s.Created]);

        // The name is the key; a save over a subscription replaces everything but its identifier and its creation time.
        if (!_subs.TryGetValue((tenant, name), out var sub))
        {
            sub = new Sub { IdSlot = s.IdSlot, Tenant = tenant, TenantIndex = s.Tenant, Name = name, Created = created };
            _subs[(tenant, name)] = sub;
        }

        sub.Url = traits.Text(universe.Urls[s.Url]);
        sub.Events = [.. s.Events.Select(index => traits.Text(WebhookStateMachine.EventNames[index]))];
        sub.Secret = s.SecretKey is { } key ? traits.Text(universe.Texts[key]) : null;
        sub.Headers = [.. s.Headers.Select(pair => KeyValuePair.Create(traits.Text(WebhookStateMachine.HeaderNames[pair.Name]), traits.Text(universe.Texts[pair.Value])))];
        sub.HeaderKeys = [.. s.HeaderKeys.Select(pair => KeyValuePair.Create(traits.Text(WebhookStateMachine.HeaderNames[pair.Name]), traits.Text(universe.Texts[pair.Value])))];
        sub.Enabled = s.Enabled;
        sub.Failures = s.Failures;
        sub.Updated = traits.Ticks(universe.Instants[s.Updated]);

        return Observation.Of("saved " + Render(sub));
    }

    private Observation Get(GetSubscriptionStep s)
        => Observation.Of(_subs.TryGetValue((Canonical(s.Tenant), traits.Text(universe.Names[s.Name])), out var sub) ? Render(sub) : "null");

    private Observation List(ListSubscriptionsStep s)
    {
        var tenant = Canonical(s.Tenant);

        return Observation.List("subscriptions", [.. _subs.Values.Where(sub => string.Equals(sub.Tenant, tenant, StringComparison.Ordinal)).Select(Render).Order(StringComparer.Ordinal)]);
    }

    private Observation Find(FindForEventStep s)
    {
        var tenant = Canonical(s.Tenant);
        var eventName = traits.Text(WebhookStateMachine.EventNames[s.Event]);

        // An enabled subscription of the tenant whose event list holds the event, compared exactly.
        return Observation.List(
            "subscriptions",
            [.. _subs.Values
                .Where(sub => sub.Enabled && string.Equals(sub.Tenant, tenant, StringComparison.Ordinal) && sub.Events.Contains(eventName, StringComparer.Ordinal))
                .Select(Render)
                .Order(StringComparer.Ordinal)]);
    }

    private Observation Delete(DeleteSubscriptionStep s)
    {
        var key = (Canonical(s.Tenant), traits.Text(universe.Names[s.Name]));

        if (!_subs.Remove(key, out var removed))
        {
            return Observation.Of("false");
        }

        _deliveries.RemoveAll(delivery => delivery.SubSlot == removed.IdSlot);

        return Observation.Of("true");
    }

    private Observation Outcome(RecordOutcomeStep s)
    {
        if (SubOf(s.Slot) is not { } sub)
        {
            return Observation.Of("false");
        }

        sub.Updated = traits.Ticks(universe.Instants[s.Updated]);

        if (s.Succeeded)
        {
            sub.Failures = 0;

            return Observation.Of("false");
        }

        sub.Failures++;

        // True when this call is the one that switched the subscription off.
        var switchedOff = sub.Enabled && sub.Failures >= s.Threshold;

        if (switchedOff)
        {
            sub.Enabled = false;
        }

        return Observation.Of((traits.OutcomeAnswersForASubscriptionThatIsOff ? !sub.Enabled : switchedOff) ? "true" : "false");
    }

    private Observation Create(CreateDeliveryStep s)
    {
        var owner = SubOf(s.Subscription) ?? throw new InvalidOperationException("the step was concretized against a subscription that exists");
        var delivery = new Delivery
        {
            Slot = s.DeliverySlot,
            SubSlot = owner.IdSlot,
            Tenant = Canonical(s.Tenant),
            EventType = traits.Text(WebhookStateMachine.EventNames[s.Event]),
            Payload = traits.Text(universe.Texts[s.Payload]),
            Created = traits.Ticks(universe.Instants[s.Created]),
        };

        _deliveries.Add(delivery);

        // A SQL store builds the record it returns from the call and writes the row separately, so the text and the time
        // of the returned record are the caller's own (the tenant is not: it is returned in the form the row holds).
        return Observation.Of("created " + (traits.CreateEchoesInputText
            ? RenderDelivery(delivery.Slot, delivery.SubSlot, delivery.Tenant, WebhookStateMachine.EventNames[s.Event], universe.Texts[s.Payload], WebhookDeliveryStatus.Pending, 0, null, null, universe.Instants[s.Created].UtcTicks, null)
            : Render(delivery)));
    }

    private Observation GetDelivery(GetDeliveryStep s)
        => Observation.Of(_deliveries.FirstOrDefault(delivery => delivery.Slot == s.Delivery) is { } found ? Render(found) : "null");

    private Observation Record(RecordResultStep s)
    {
        if (_deliveries.FirstOrDefault(delivery => delivery.Slot == s.Delivery) is not { } delivery)
        {
            return Observation.Of("ok");
        }

        // The attempt's outcome replaces the last one. A delivered attempt writes the delivery time; any other attempt
        // leaves it as it was (the interface is silent on a result after a delivered one, which the product never records;
        // the in-memory store and the SQL stores agree, so the model pins that).
        delivery.Status = s.Status;
        delivery.Attempt = s.Attempt;
        delivery.Code = s.Code;
        delivery.Error = s.Error is { } error ? traits.Text(universe.Texts[error]) : null;

        if (s.Status == WebhookDeliveryStatus.Delivered)
        {
            delivery.Delivered = traits.Ticks(universe.Instants[s.Recorded]);
        }

        return Observation.Of("ok");
    }

    private Observation Query(QueryDeliveriesStep s)
    {
        var tenant = Canonical(s.Tenant);
        var subscription = s.Subscription is { } slot ? SubOf(slot)?.IdSlot ?? -1 : (int?)null;
        var matches = _deliveries
            .Where(delivery => string.Equals(delivery.Tenant, tenant, StringComparison.Ordinal))
            .Where(delivery => subscription is not { } sub || delivery.SubSlot == sub)
            .Where(delivery => s.Status is not { } status || delivery.Status == status)
            .OrderByDescending(static delivery => delivery.Created)
            .Select(delivery => (Key: delivery.Created, Rendered: Render(delivery)))
            .ToList();

        // The interface does not say what a take below one returns. All four stores answer one record (they cut the take
        // at one, the way the endpoint does), so the model pins that and says so: it is not read from a document.
        return TiedPage.Cut("deliveries", matches, s.Skip, Math.Max(1, s.Take));
    }

    private static string Render(Sub sub)
        => RenderSub(sub.IdSlot, sub.Tenant, sub.Name, sub.Url, sub.Events, sub.Secret, sub.Headers, sub.HeaderKeys, sub.Enabled, sub.Failures, sub.Created, sub.Updated);

    private static string Render(Delivery delivery)
        => RenderDelivery(delivery.Slot, delivery.SubSlot, delivery.Tenant, delivery.EventType, delivery.Payload, delivery.Status, delivery.Attempt, delivery.Code, delivery.Error, delivery.Created, delivery.Delivered);

    private sealed class Sub
    {
        public required int IdSlot { get; init; }

        public required string Tenant { get; init; }

        public required int TenantIndex { get; init; }

        public required string Name { get; init; }

        public required long Created { get; init; }

        public string Url { get; set; } = string.Empty;

        public IReadOnlyList<string> Events { get; set; } = [];

        public string? Secret { get; set; }

        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; set; } = [];

        public IReadOnlyList<KeyValuePair<string, string>> HeaderKeys { get; set; } = [];

        public bool Enabled { get; set; }

        public int Failures { get; set; }

        public long Updated { get; set; }
    }

    private sealed class Delivery
    {
        public required int Slot { get; init; }

        public required int SubSlot { get; init; }

        public required string Tenant { get; init; }

        public required string EventType { get; init; }

        public required string Payload { get; init; }

        public required long Created { get; init; }

        public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;

        public int Attempt { get; set; }

        public int? Code { get; set; }

        public string? Error { get; set; }

        public long? Delivered { get; set; }
    }
}

/// <summary>Applies webhook steps to a store.</summary>
internal sealed class WebhookDriver(IWebhookStore store, DifferentialScope scope, WebhookUniverse universe) : IStepDriver<WebhookStep>
{
    // Steps of a race run in several tasks: the identifier of a slot is made once, by whichever task needs it first.
    private readonly ConcurrentDictionary<int, Guid> _subscriptionIds = [];
    private readonly ConcurrentDictionary<int, Guid> _deliveryIds = [];

    /// <inheritdoc />
    public async ValueTask<Observation> ApplyAsync(WebhookStep step)
    {
        switch (step)
        {
            case SaveSubscriptionStep s:
                var saved = await store.SaveSubscriptionAsync(new WebhookSubscription
                {
                    Id = SubId(s.IdSlot),
                    TenantId = scope.Wrap(universe.Tenants[s.Tenant]),
                    Name = universe.Names[s.Name],
                    Url = universe.Urls[s.Url],
                    Events = [.. s.Events.Select(static index => WebhookStateMachine.EventNames[index])],
                    SecretConfigurationKey = s.SecretKey is { } key ? universe.Texts[key] : null,
                    Headers = Map(s.Headers),
                    HeaderConfigurationKeys = Map(s.HeaderKeys),
                    Enabled = s.Enabled,
                    ConsecutiveFailures = s.Failures,
                    CreatedAt = universe.Instants[s.Created],
                    UpdatedAt = universe.Instants[s.Updated],
                });

                return Observation.Of("saved " + Render(saved));
            case GetSubscriptionStep s:
                var found = await store.GetSubscriptionAsync(scope.Wrap(universe.Tenants[s.Tenant]), universe.Names[s.Name]);

                return Observation.Of(found is null ? "null" : Render(found));
            case ListSubscriptionsStep s:
                var listed = await store.ListSubscriptionsAsync(scope.Wrap(universe.Tenants[s.Tenant]));

                return Observation.List("subscriptions", [.. listed.Select(Render).Order(StringComparer.Ordinal)]);
            case FindForEventStep s:
                var matches = await store.FindForEventAsync(scope.Wrap(universe.Tenants[s.Tenant]), WebhookStateMachine.EventNames[s.Event]);

                return Observation.List("subscriptions", [.. matches.Select(Render).Order(StringComparer.Ordinal)]);
            case DeleteSubscriptionStep s:
                return Observation.Of(await store.DeleteSubscriptionAsync(scope.Wrap(universe.Tenants[s.Tenant]), universe.Names[s.Name]) ? "true" : "false");
            case RecordOutcomeStep s:
                return Observation.Of(await store.RecordSubscriptionOutcomeAsync(SubId(s.Slot), s.Succeeded, s.Threshold, universe.Instants[s.Updated]) ? "true" : "false");
            case CreateDeliveryStep s:
                var created = await store.CreateDeliveryAsync(new WebhookDelivery
                {
                    Id = DeliveryId(s.DeliverySlot),
                    SubscriptionId = SubId(s.Subscription),
                    TenantId = scope.Wrap(universe.Tenants[s.Tenant]),
                    EventType = WebhookStateMachine.EventNames[s.Event],
                    Payload = universe.Texts[s.Payload],
                    Status = WebhookDeliveryStatus.Pending,
                    CreatedAt = universe.Instants[s.Created],
                });

                return Observation.Of("created " + Render(created));
            case GetDeliveryStep s:
                var delivery = await store.GetDeliveryAsync(DeliveryId(s.Delivery));

                return Observation.Of(delivery is null ? "null" : Render(delivery));
            case RecordResultStep s:
                await store.RecordDeliveryResultAsync(new WebhookDeliveryResult
                {
                    DeliveryId = DeliveryId(s.Delivery),
                    Status = s.Status,
                    Attempt = s.Attempt,
                    ResponseCode = s.Code,
                    Error = s.Error is { } error ? universe.Texts[error] : null,
                    RecordedAt = universe.Instants[s.Recorded],
                });

                return Observation.Of("ok");
            case QueryDeliveriesStep s:
                var page = await store.QueryDeliveriesAsync(new WebhookDeliveryQuery
                {
                    TenantId = scope.Wrap(universe.Tenants[s.Tenant]),
                    SubscriptionId = s.Subscription is { } slot ? SubId(slot) : null,
                    Status = s.Status,
                    Skip = s.Skip,
                    Take = s.Take,
                });

                return Observation.List("deliveries", [.. page.Select(Render)]);
            default:
                throw new InvalidOperationException(step.GetType().Name);
        }
    }

    private static Dictionary<string, string> Map((int Name, int Value)[] pairs, string[] texts)
        => new(pairs.Select(pair => KeyValuePair.Create(WebhookStateMachine.HeaderNames[pair.Name], texts[pair.Value])), StringComparer.OrdinalIgnoreCase);

    private Guid SubId(int slot) => _subscriptionIds.GetOrAdd(slot, static _ => Guid.NewGuid());

    private Guid DeliveryId(int slot) => _deliveryIds.GetOrAdd(slot, static _ => Guid.NewGuid());

    private Dictionary<string, string> Map((int Name, int Value)[] pairs) => Map(pairs, universe.Texts);

    private string Render(WebhookSubscription sub)
        => WebhookModel.RenderSub(
            SlotOf(_subscriptionIds, sub.Id),
            scope.Unwrap(sub.TenantId) ?? "<null>",
            sub.Name,
            sub.Url,
            sub.Events,
            sub.SecretConfigurationKey,
            sub.Headers,
            sub.HeaderConfigurationKeys,
            sub.Enabled,
            sub.ConsecutiveFailures,
            sub.CreatedAt.UtcTicks,
            sub.UpdatedAt.UtcTicks);

    private string Render(WebhookDelivery delivery)
        => WebhookModel.RenderDelivery(
            SlotOf(_deliveryIds, delivery.Id),
            SlotOf(_subscriptionIds, delivery.SubscriptionId),
            scope.Unwrap(delivery.TenantId) ?? "<null>",
            delivery.EventType,
            delivery.Payload,
            delivery.Status,
            delivery.Attempt,
            delivery.ResponseCode,
            delivery.Error,
            delivery.CreatedAt.UtcTicks,
            delivery.DeliveredAt?.UtcTicks);

    private static int SlotOf(ConcurrentDictionary<int, Guid> ids, Guid id)
        => ids.FirstOrDefault(pair => pair.Value == id, new KeyValuePair<int, Guid>(-1, id)).Key;
}

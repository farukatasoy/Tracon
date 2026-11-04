namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IWebhookStore"/> contract.
/// </summary>
/// <remarks>
/// This contract has no <strong>secret field</strong>. The store only carries
/// the name of the configuration key the secret is read from; tests
/// verify this.
/// </remarks>
public abstract class WebhookStoreContract : TenantIsolationContract<IWebhookStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var subscription = await Store.SaveSubscriptionAsync(
            Subscription(name) with { Id = Guid.NewGuid(), TenantId = tenantId }, cancellationToken);

        await Store.CreateDeliveryAsync(Delivery(subscription.Id) with { TenantId = tenantId }, cancellationToken);

        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var subscription = await Store.GetSubscriptionAsync(tenantId, (string)key);

        // Event matching is also locked to the tenant; otherwise one tenant's event
        // would be delivered to another tenant's endpoint.
        var matches = await Store.FindForEventAsync(tenantId, "run.completed");
        matches.Any(item => item.Id == subscription?.Id).ShouldBe(subscription is not null);

        return subscription is not null;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
    {
        var deliveries = await Store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = tenantId }, cancellationToken);
        var subscriptions = await Store.ListSubscriptionsAsync(tenantId, cancellationToken);

        // Delivery history must also respect the same tenant boundary as the subscription.
        deliveries.Count.ShouldBe(subscriptions.Count);

        return subscriptions.Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteSubscriptionAsync(tenantId, (string)key);

    private const string Tenant = "test";

    /// <summary>
    /// The name is the client's: names that differ only in letter case are two records. A store that folds
    /// case merges them, and the second save replaces the first without a sign.
    /// </summary>
    [Fact]
    public Task Names_that_differ_only_in_case_are_two_subscriptions()
        => AssertNamesThatDifferOnlyInCaseAreSeparateRecordsAsync();

    [Fact]
    public async Task Saved_subscription_is_read_back()
    {
        await Store.SaveSubscriptionAsync(Subscription(
            events: ["run.completed", "run.failed"],
            secretKey: "Tracon:WebhookSecrets:orders"));

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");

        loaded.ShouldNotBeNull();
        loaded.Url.ShouldBe("https://example.com/hook");
        loaded.Events.ShouldBe(["run.completed", "run.failed"]);
        loaded.SecretConfigurationKey.ShouldBe("Tracon:WebhookSecrets:orders");
        loaded.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Headers_are_preserved()
    {
        await Store.SaveSubscriptionAsync(Subscription(
            headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Team"] = "platform",
                ["X-Env"] = "prod",
            }));

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");

        loaded.ShouldNotBeNull();
        loaded.Headers.Count.ShouldBe(2);
        loaded.Headers["X-Team"].ShouldBe("platform");
        loaded.Headers["X-Env"].ShouldBe("prod");
    }

    [Fact]
    public async Task Header_configuration_keys_are_preserved()
    {
        // The SAVED record is checked as well as the one read back: a store
        // that returns the written row from its own column list (SQL Server's
        // OUTPUT clause) can drop the field there while a later read has it.
        var saved = await Store.SaveSubscriptionAsync(Subscription() with
        {
            HeaderConfigurationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Api-Key"] = "Tracon:WebhookSecrets:OrdersKey",
                ["Cookie"] = "Tracon:WebhookSecrets:OrdersSession",
            },
        });

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");

        foreach (var subscription in new[] { saved, loaded.ShouldNotBeNull() })
        {
            subscription.HeaderConfigurationKeys.Count.ShouldBe(2);
            subscription.HeaderConfigurationKeys["X-Api-Key"].ShouldBe("Tracon:WebhookSecrets:OrdersKey");
            subscription.HeaderConfigurationKeys["Cookie"].ShouldBe("Tracon:WebhookSecrets:OrdersSession");
        }

        // Event matching reads the same column list as a plain read.
        (await Store.FindForEventAsync(Tenant, "run.completed")).ShouldHaveSingleItem()
            .HeaderConfigurationKeys["X-Api-Key"].ShouldBe("Tracon:WebhookSecrets:OrdersKey");

        // An update replaces the map; an upsert that leaves the column out of
        // its UPDATE list keeps the old names.
        await Store.SaveSubscriptionAsync(loaded with { HeaderConfigurationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) });

        (await Store.GetSubscriptionAsync(Tenant, "orders"))!.HeaderConfigurationKeys.ShouldBeEmpty();
    }

    [Fact]
    public async Task Header_names_that_differ_only_in_case_do_not_fail_the_save()
    {
        // A header name is case-insensitive, but a caller's dictionary may be
        // case-sensitive. A store that folds the names must not throw on the
        // pair: the save answered 500 when it did.
        var saved = await Store.SaveSubscriptionAsync(Subscription(
            headers: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["X-Team"] = "one",
                ["x-team"] = "two",
            }));

        saved.Headers.Count.ShouldBeInRange(1, 2);
        (await Store.GetSubscriptionAsync(Tenant, "orders")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Saving_the_same_name_a_second_time_overwrites_it()
    {
        await Store.SaveSubscriptionAsync(Subscription(url: "https://one.example.com/hook"));
        await Store.SaveSubscriptionAsync(Subscription(url: "https://two.example.com/hook"));

        var all = await Store.ListSubscriptionsAsync(Tenant);

        all.Count.ShouldBe(1);
        all[0].Url.ShouldBe("https://two.example.com/hook");
    }

    /// <summary>
    /// A subscription saved over the one that holds its name keeps the identifier and the creation time of the one it
    /// replaces; everything else is the new one's.
    /// </summary>
    /// <remarks>
    /// The identifier is what the deliveries of the subscription name, and the creation time says when the name first got
    /// a subscription. A store that takes the new record's identifier orphans the history, and one that takes its creation
    /// time moves it forward every time an administrator edits the subscription.
    /// </remarks>
    [Fact]
    public async Task Saving_over_a_subscription_keeps_its_identifier_and_its_creation_time()
    {
        var first = await Store.SaveSubscriptionAsync(Subscription(url: "https://one.example.com/hook"));
        var later = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

        var second = await Store.SaveSubscriptionAsync(Subscription(url: "https://two.example.com/hook") with { CreatedAt = later, UpdatedAt = later });

        second.Id.ShouldBe(first.Id);
        second.CreatedAt.ShouldBe(first.CreatedAt);
        second.UpdatedAt.ShouldBe(later);
        second.Url.ShouldBe("https://two.example.com/hook");

        var read = (await Store.GetSubscriptionAsync(Tenant, "orders")).ShouldNotBeNull();

        read.Id.ShouldBe(first.Id);
        read.CreatedAt.ShouldBe(first.CreatedAt);
    }

    /// <summary>
    /// Writers that save the same name at the same time leave one subscription: the name is unique whoever wins.
    /// </summary>
    /// <remarks>
    /// Two nodes that apply one configuration, or an administrator console that saves twice, race on one name. A store
    /// that looks for the subscription and then adds one lets both writers add.
    /// </remarks>
    [Fact]
    public async Task Writers_that_save_one_name_at_the_same_time_leave_one_subscription()
    {
        const int Writers = 32;

        for (var round = 0; round < 20; round++)
        {
            var name = "racing-" + round;
            var gate = new TaskCompletionSource();
            var writers = Enumerable.Range(0, Writers)
                .Select(index => Task.Run(async () =>
                {
                    await gate.Task;

                    return await Store.SaveSubscriptionAsync(Subscription(name: name, url: "https://example.com/" + index));
                }))
                .ToList();

            gate.SetResult();

            var saved = await Task.WhenAll(writers);

            (await Store.ListSubscriptionsAsync(Tenant)).Count(subscription => string.Equals(subscription.Name, name, StringComparison.Ordinal)).ShouldBe(1, $"round {round}: two subscriptions hold one name");
            saved.Select(static subscription => subscription.Id).Distinct().Count().ShouldBe(1, $"round {round}: the writers were handed different subscriptions");
        }
    }

    [Fact]
    public async Task Subscribers_for_an_event_are_found()
    {
        await Store.SaveSubscriptionAsync(Subscription(name: "a", events: ["run.completed"]));
        await Store.SaveSubscriptionAsync(Subscription(name: "b", events: ["run.failed"]));
        await Store.SaveSubscriptionAsync(
            Subscription(name: "c", events: ["run.completed", "quota.threshold"]));

        var matches = await Store.FindForEventAsync(Tenant, "run.completed");

        matches.Select(subscription => subscription.Name).ShouldBe(["a", "c"], ignoreOrder: true);
    }

    [Fact]
    public async Task Disabled_subscription_does_not_appear_in_event_search()
    {
        await Store.SaveSubscriptionAsync(
            Subscription(name: "a", events: ["run.completed"], enabled: false));

        (await Store.FindForEventAsync(Tenant, "run.completed")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Event_name_matches_exactly()
    {
        await Store.SaveSubscriptionAsync(Subscription(events: ["run.completed"]));

        // Searching for 'run.completed'; 'run.complete' or 'run.completed.v2'
        // must not match.
        (await Store.FindForEventAsync(Tenant, "run.complete")).ShouldBeEmpty();
        (await Store.FindForEventAsync(Tenant, "run.completed.v2")).ShouldBeEmpty();
        (await Store.FindForEventAsync(Tenant, "run.completed")).Count.ShouldBe(1);
    }

    /// <summary>
    /// An event name is matched by its characters: a subscription to <c>run.completed</c> is not found for
    /// <c>Run.Completed</c>.
    /// </summary>
    /// <remarks>
    /// The event names are fixed identifiers the product publishes. A store that compares them by the collation of its
    /// column finds a subscription for a spelling nobody publishes, on one engine and not on the others.
    /// </remarks>
    [Fact]
    public async Task Event_name_matches_by_its_characters_letter_case_included()
    {
        await Store.SaveSubscriptionAsync(Subscription(events: ["run.completed"]));

        (await Store.FindForEventAsync(Tenant, "Run.Completed")).ShouldBeEmpty();
        (await Store.FindForEventAsync(Tenant, "RUN.COMPLETED")).ShouldBeEmpty();
        (await Store.FindForEventAsync(Tenant, "run.completed")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Another_tenants_subscription_is_not_visible()
    {
        await Store.SaveSubscriptionAsync(Subscription());

        (await Store.GetSubscriptionAsync("other", "orders")).ShouldBeNull();
        (await Store.ListSubscriptionsAsync("other")).ShouldBeEmpty();
        (await Store.FindForEventAsync("other", "run.completed")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleted_subscription_is_not_read_back()
    {
        await Store.SaveSubscriptionAsync(Subscription());

        (await Store.DeleteSubscriptionAsync(Tenant, "orders")).ShouldBeTrue();
        (await Store.GetSubscriptionAsync(Tenant, "orders")).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_a_subscription_also_deletes_its_deliveries()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        await Store.CreateDeliveryAsync(Delivery(subscription.Id));

        await Store.DeleteSubscriptionAsync(Tenant, "orders");

        var deliveries = await Store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = Tenant });

        deliveries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Delivery_record_is_read_back()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var delivery = await Store.CreateDeliveryAsync(Delivery(subscription.Id));

        var loaded = await Store.GetDeliveryAsync(delivery.Id);

        loaded.ShouldNotBeNull();
        loaded.EventType.ShouldBe("run.completed");
        loaded.Status.ShouldBe(WebhookDeliveryStatus.Pending);
        loaded.Attempt.ShouldBe(0);
    }

    /// <summary>
    /// The records a call returns, and the ones a later read returns, carry the canonical tenant whichever spelling wrote
    /// them.
    /// </summary>
    /// <remarks>
    /// The caller compares a record's tenant with its own as ordinal text. A store that returns the spelling it was given
    /// makes the answer of the write differ from the answer of the next read.
    /// </remarks>
    [Fact]
    public async Task A_record_written_under_another_spelling_is_returned_and_read_under_the_canonical_tenant()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription() with { TenantId = "TEST" });

        subscription.TenantId.ShouldBe(Tenant);

        var created = await Store.CreateDeliveryAsync(Delivery(subscription.Id) with { TenantId = "Test" });

        created.TenantId.ShouldBe(Tenant);
        (await Store.GetDeliveryAsync(created.Id)).ShouldNotBeNull().TenantId.ShouldBe(Tenant);
        (await Store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = "tEST" })).ShouldHaveSingleItem().TenantId.ShouldBe(Tenant);
        (await Store.GetSubscriptionAsync("TEST", "orders")).ShouldNotBeNull().TenantId.ShouldBe(Tenant);
    }

    [Fact]
    public async Task Successful_delivery_result_is_recorded()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var delivery = await Store.CreateDeliveryAsync(Delivery(subscription.Id));
        var deliveredAt = new DateTimeOffset(2026, 8, 3, 12, 5, 0, TimeSpan.Zero);

        await Store.RecordDeliveryResultAsync(new WebhookDeliveryResult
        {
            DeliveryId = delivery.Id,
            Status = WebhookDeliveryStatus.Delivered,
            Attempt = 1,
            ResponseCode = 204,
            RecordedAt = deliveredAt,
        });

        var loaded = await Store.GetDeliveryAsync(delivery.Id);

        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe(WebhookDeliveryStatus.Delivered);
        loaded.ResponseCode.ShouldBe(204);
        loaded.Attempt.ShouldBe(1);
        loaded.DeliveredAt.ShouldBe(deliveredAt);
    }

    [Fact]
    public async Task Failed_delivery_does_not_record_a_delivery_time()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var delivery = await Store.CreateDeliveryAsync(Delivery(subscription.Id));

        await Store.RecordDeliveryResultAsync(new WebhookDeliveryResult
        {
            DeliveryId = delivery.Id,
            Status = WebhookDeliveryStatus.Failed,
            Attempt = 5,
            ResponseCode = 500,
            Error = "HTTP 500",
            RecordedAt = DateTimeOffset.UtcNow,
        });

        var loaded = await Store.GetDeliveryAsync(delivery.Id);

        loaded.ShouldNotBeNull();
        loaded.Status.ShouldBe(WebhookDeliveryStatus.Failed);
        loaded.Error.ShouldBe("HTTP 500");
        loaded.DeliveredAt.ShouldBeNull();
    }

    [Fact]
    public async Task Deliveries_are_filtered_by_status()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var first = await Store.CreateDeliveryAsync(Delivery(subscription.Id));
        await Store.CreateDeliveryAsync(Delivery(subscription.Id));

        await Store.RecordDeliveryResultAsync(new WebhookDeliveryResult
        {
            DeliveryId = first.Id,
            Status = WebhookDeliveryStatus.Delivered,
            Attempt = 1,
            RecordedAt = DateTimeOffset.UtcNow,
        });

        var delivered = await Store.QueryDeliveriesAsync(new WebhookDeliveryQuery
        {
            TenantId = Tenant,
            Status = WebhookDeliveryStatus.Delivered,
        });

        delivered.Count.ShouldBe(1);
        delivered[0].Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task Failure_counter_counts_every_concurrent_failure()
    {
        // Deliveries of one subscription run on several job slots at once. A counter that is read, raised and
        // written back in separate steps loses the increments of the writers that overlap.
        const int Writers = 4;
        const int PerWriter = 50;

        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var now = DateTimeOffset.UtcNow;

        await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => Task.Run(async () =>
        {
            for (var index = 0; index < PerWriter; index++)
            {
                await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, int.MaxValue, now);
            }
        })));

        (await Store.GetSubscriptionAsync(Tenant, "orders"))!.ConsecutiveFailures.ShouldBe(Writers * PerWriter);
    }

    [Fact]
    public async Task Failure_counter_increments_and_disables_the_subscription_at_the_threshold()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var now = DateTimeOffset.UtcNow;

        // Threshold 3: the first two failures do not disable it.
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 3, now)).ShouldBeFalse();
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 3, now)).ShouldBeFalse();

        (await Store.GetSubscriptionAsync(Tenant, "orders"))!.Enabled.ShouldBeTrue();

        // The third one disables it and reports that.
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 3, now)).ShouldBeTrue();

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");
        loaded.ShouldNotBeNull();
        loaded.Enabled.ShouldBeFalse();
        loaded.ConsecutiveFailures.ShouldBe(3);
    }

    [Fact]
    public async Task Successful_delivery_resets_the_counter()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var now = DateTimeOffset.UtcNow;

        await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 5, now);
        await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 5, now);
        await Store.RecordSubscriptionOutcomeAsync(subscription.Id, true, 5, now);

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");

        loaded.ShouldNotBeNull();
        loaded.ConsecutiveFailures.ShouldBe(0);
        loaded.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Saving_a_subscription_again_writes_the_failure_counter_it_carries()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var now = DateTimeOffset.UtcNow;

        // Threshold 2: the second failure disables the subscription.
        await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 2, now);
        await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 2, now);

        var disabled = await Store.GetSubscriptionAsync(Tenant, "orders");
        disabled.ShouldNotBeNull();
        disabled.Enabled.ShouldBeFalse();
        disabled.ConsecutiveFailures.ShouldBe(2);

        // The management API re-enables a subscription by saving it with the
        // counter at zero. A store that keeps the stored counter undoes that
        // reset without a trace: the next single failure disables it again.
        await Store.SaveSubscriptionAsync(disabled with { Enabled = true, ConsecutiveFailures = 0 });

        var saved = await Store.GetSubscriptionAsync(Tenant, "orders");
        saved.ShouldNotBeNull();
        saved.Enabled.ShouldBeTrue();
        saved.ConsecutiveFailures.ShouldBe(0);

        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 2, now)).ShouldBeFalse();
        (await Store.GetSubscriptionAsync(Tenant, "orders"))!.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task The_created_delivery_is_the_delivery_a_read_reports()
    {
        // The text and the time the row holds, not those of the call: a SQL store writes U+0000 as U+FFFD and
        // PostgreSQL keeps microseconds.
        var subscription = await Store.SaveSubscriptionAsync(Subscription());

        var created = await Store.CreateDeliveryAsync(Delivery(subscription.Id) with
        {
            EventType = "run\0completed",
            Payload = "{\"text\":\"a\\u0000b\"}\0",
            CreatedAt = new DateTimeOffset(2026, 11, 4, 9, 30, 15, TimeSpan.Zero).AddTicks(7),
        });
        var read = (await Store.GetDeliveryAsync(created.Id)).ShouldNotBeNull();

        created.EventType.ShouldBe(read.EventType);
        created.Payload.ShouldBe(read.Payload);
        created.CreatedAt.ShouldBe(read.CreatedAt);
        created.TenantId.ShouldBe(read.TenantId);
        created.Status.ShouldBe(read.Status);
    }

    [Fact]
    public async Task A_failure_recorded_on_a_subscription_that_is_already_off_does_not_report_a_disable()
    {
        // The answer is "did THIS call switch the subscription off". The delivery job logs a warning on true, so a
        // store that answers true for every failure of a subscription that is off logs "was disabled" again and
        // again for a subscription that stopped long ago.
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var now = DateTimeOffset.UtcNow;

        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 2, now)).ShouldBeFalse();
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 2, now)).ShouldBeTrue();

        // Off now: a further failure counts, and reports nothing.
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 2, now)).ShouldBeFalse();
        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 2, now)).ShouldBeFalse();

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");
        loaded.ShouldNotBeNull();
        loaded.Enabled.ShouldBeFalse();
        loaded.ConsecutiveFailures.ShouldBe(4);

        // A subscription an administrator switched off is the same case.
        var switchedOff = await Store.SaveSubscriptionAsync(Subscription("paused", enabled: false));

        (await Store.RecordSubscriptionOutcomeAsync(switchedOff.Id, false, 1, now)).ShouldBeFalse();
        (await Store.RecordSubscriptionOutcomeAsync(switchedOff.Id, false, 100, now)).ShouldBeFalse();
    }

    [Fact]
    public async Task Exactly_one_of_several_concurrent_failures_at_the_threshold_reports_the_disable()
    {
        // Deliveries of one subscription fail on several job slots at once. The call that crosses the threshold
        // answers true; a store that reads "off" after its own update, or reads the old state without a lock, lets
        // two of them (or none) answer true.
        const int Writers = 16;
        const int Threshold = 5;

        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var now = DateTimeOffset.UtcNow;

        var answers = await Task.WhenAll(Enumerable.Range(0, Writers).Select(_ => Task.Run(
            async () => await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, Threshold, now))));

        answers.Count(answer => answer).ShouldBe(1);

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");
        loaded.ShouldNotBeNull();
        loaded.Enabled.ShouldBeFalse();
        loaded.ConsecutiveFailures.ShouldBe(Writers);
    }

    [Fact]
    public async Task A_success_on_an_off_subscription_resets_the_counter_and_reports_nothing()
    {
        // A success clears the counter whatever the state; it does not switch the subscription back on (only the
        // management API does that).
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        var now = DateTimeOffset.UtcNow;

        await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 1, now);

        (await Store.RecordSubscriptionOutcomeAsync(subscription.Id, true, 1, now)).ShouldBeFalse();

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");
        loaded.ShouldNotBeNull();
        loaded.Enabled.ShouldBeFalse();
        loaded.ConsecutiveFailures.ShouldBe(0);
    }

    [Fact]
    public async Task A_canceled_token_stops_a_delivery_write_and_leaves_no_delivery()
    {
        // The subscription is saved first with a live token, so the canceled call is the delivery write itself and not
        // the seed of the contract.
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Store.CreateDeliveryAsync(Delivery(subscription.Id), source.Token));

        (await Store.QueryDeliveriesAsync(new WebhookDeliveryQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_canceled_token_stops_an_outcome_write_and_leaves_the_counter()
    {
        var subscription = await Store.SaveSubscriptionAsync(Subscription());
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Store.RecordSubscriptionOutcomeAsync(subscription.Id, false, 1, DateTimeOffset.UtcNow, source.Token));

        var loaded = await Store.GetSubscriptionAsync(Tenant, "orders");
        loaded.ShouldNotBeNull();
        loaded.Enabled.ShouldBeTrue();
        loaded.ConsecutiveFailures.ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_subscription_reports_nothing()
    {
        var now = DateTimeOffset.UtcNow;

        (await Store.RecordSubscriptionOutcomeAsync(Guid.NewGuid(), false, 1, now)).ShouldBeFalse();
        (await Store.RecordSubscriptionOutcomeAsync(Guid.NewGuid(), true, 1, now)).ShouldBeFalse();
    }

    private static WebhookSubscription Subscription(
        string name = "orders",
        string url = "https://example.com/hook",
        IReadOnlyList<string>? events = null,
        string? secretKey = null,
        IReadOnlyDictionary<string, string>? headers = null,
        bool enabled = true)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Name = name,
            Url = url,
            Events = events ?? ["run.completed"],
            SecretConfigurationKey = secretKey,
            Headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Enabled = enabled,
            CreatedAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.Zero),
        };

    private static WebhookDelivery Delivery(Guid subscriptionId)
        => new()
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            TenantId = Tenant,
            EventType = "run.completed",
            Payload = """{"event":"run.completed"}""",
            Status = WebhookDeliveryStatus.Pending,
            CreatedAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
        };
}

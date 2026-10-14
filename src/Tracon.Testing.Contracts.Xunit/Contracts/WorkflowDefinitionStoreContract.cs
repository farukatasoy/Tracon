using System.Text.Json;

namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IWorkflowDefinitionStore"/> contract.
/// </summary>
/// <remarks>
/// The in-memory store and the SQL stores must pass the same
/// scenarios. In particular, version increment, version history and tenant isolation must
/// behave identically in all of them.
/// </remarks>
public abstract class WorkflowDefinitionStoreContract : TenantIsolationContract<IWorkflowDefinitionStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        await Store.SaveAsync(tenantId, Definition() with { Name = name }, cancellationToken);
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.ListAsync(tenantId, cancellationToken)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key);

    [Fact]
    public async Task Saved_definition_is_read_back()
    {
        var saved = await Store.SaveAsync("tenant-a", Definition());

        saved.Version.ShouldBe(1);
        saved.TenantId.ShouldBe("tenant-a");

        var loaded = await Store.GetAsync("tenant-a", "review");

        loaded.ShouldNotBeNull();
        loaded.Kind.ShouldBe(WorkflowKind.Sequential);
        loaded.AgentNames.ShouldBe(["researcher", "writer", "editor"]);
        loaded.Description.ShouldBe("Three-step review.");
    }

    [Fact]
    public async Task All_fields_survive_the_round_trip()
    {
        // 🚨 The jsonb payload goes through a HAND-WRITTEN DTO. If a new
        // field is added to the definition and the DTO is not updated, the
        // field silently disappears; neither compilation nor any other test
        // catches it. This is the whole reason this test exists.
        var definition = new WorkflowDefinition
        {
            Name = "full",
            DisplayName = "Full Definition",
            Description = "All fields populated.",
            Kind = WorkflowKind.Handoff,
            AgentNames = ["support", "specialist"],
            MaxIterations = 5,
            HandoffInstructions = "Hand off to the specialist for technical questions.",
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = await Store.GetAsync("tenant-a", "full");

        loaded.ShouldNotBeNull();
        loaded.DisplayName.ShouldBe("Full Definition");
        loaded.Description.ShouldBe("All fields populated.");
        loaded.Kind.ShouldBe(WorkflowKind.Handoff);
        loaded.AgentNames.ShouldBe(["support", "specialist"]);
        loaded.MaxIterations.ShouldBe(5);
        loaded.HandoffInstructions.ShouldBe("Hand off to the specialist for technical questions.");

        // Added in phase 16. Because the default is false, a missing DTO
        // field would show up in this test as "returned false" -- true is
        // written deliberately.
        loaded.RequirePlanApproval.ShouldBeFalse();
    }

    [Fact]
    public async Task Node_list_survives_the_round_trip()
    {
        // Added in phase 71. The same hand-written DTO trap the remark above
        // describes: a missing field here would silently vanish on save.
        var definition = new WorkflowDefinition
        {
            Name = "mixed-chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = [],
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "uppercase", Kind = WorkflowNodeKind.Function },
                new WorkflowNodeReference { Name = "editor", Kind = WorkflowNodeKind.Agent },
            ],
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = await Store.GetAsync("tenant-a", "mixed-chain");

        loaded.ShouldNotBeNull();
        loaded.Nodes.Count.ShouldBe(3);
        loaded.Nodes[1].Name.ShouldBe("uppercase");
        loaded.Nodes[1].Kind.ShouldBe(WorkflowNodeKind.Function);
        loaded.AgentNames.ShouldBeEmpty();
    }

    [Fact]
    public async Task Function_node_settings_survive_the_round_trip()
    {
        // Added in phase 195 (K-580): the same hand-written DTO trap. The in-memory store hands
        // the record back as it was given, so only a SQL run can show the settings vanishing.
        // The node list is a nested list inside the jsonb payload, so the property has to be
        // carried by the node record itself - the DTO does not copy node fields one by one.
        var definition = new WorkflowDefinition
        {
            Name = "narrated",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference
                {
                    Name = "tts",
                    Kind = WorkflowNodeKind.Function,
                    Settings = JsonDocument.Parse(
                        """{"voice":"alloy","loudnessLufs":-12.5,"enabled":true,"tags":["a","b"],"nested":{"retries":3},"empty":null}""")
                        .RootElement.Clone(),
                },
                new WorkflowNodeReference { Name = "mix", Kind = WorkflowNodeKind.Function },
            ],
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = (await Store.GetAsync("tenant-a", "narrated"))!;

        loaded.Nodes.Count.ShouldBe(3);

        // A node without settings reads back as null - not as an unset element, which cannot be
        // written as JSON and would fail the whole workflow list.
        loaded.Nodes[0].Settings.ShouldBeNull();
        loaded.Nodes[2].Settings.ShouldBeNull();

        var settings = loaded.Nodes[1].Settings.ShouldNotBeNull();

        settings.GetProperty("voice").GetString().ShouldBe("alloy");
        settings.GetProperty("loudnessLufs").GetDouble().ShouldBe(-12.5);
        settings.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        settings.GetProperty("tags").EnumerateArray().Select(static tag => tag.GetString()).ShouldBe(["a", "b"]);
        settings.GetProperty("nested").GetProperty("retries").GetInt32().ShouldBe(3);
        settings.GetProperty("empty").ValueKind.ShouldBe(JsonValueKind.Null);

        // And the list read, which serializes every definition, must not fail on any of them.
        (await Store.ListAsync("tenant-a")).Single().Nodes[1].Settings.ShouldNotBeNull();
    }

    [Fact]
    public async Task Node_timeouts_and_the_run_timeout_survive_the_round_trip()
    {
        // Added in phase 196 (F-303, F-405): the hand-written DTO trap again. The run
        // timeout is a definition field the DTO copies by hand; the node timeout rides
        // on the node record. The in-memory store hands the record back as it was given,
        // so only a SQL run can show either one vanishing.
        var definition = new WorkflowDefinition
        {
            Name = "podcast",
            Kind = WorkflowKind.Sequential,
            RunTimeout = TimeSpan.FromMinutes(42),
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent, Timeout = TimeSpan.FromMinutes(6) },
                new WorkflowNodeReference { Name = "render", Kind = WorkflowNodeKind.Function, Timeout = TimeSpan.FromMilliseconds(1500) },
                new WorkflowNodeReference { Name = "mix", Kind = WorkflowNodeKind.Function },
            ],
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = (await Store.GetAsync("tenant-a", "podcast"))!;

        loaded.RunTimeout.ShouldBe(TimeSpan.FromMinutes(42));
        loaded.Nodes[0].Timeout.ShouldBe(TimeSpan.FromMinutes(6));
        loaded.Nodes[1].Timeout.ShouldBe(TimeSpan.FromMilliseconds(1500));
        loaded.Nodes[2].Timeout.ShouldBeNull();

        var listed = (await Store.ListAsync("tenant-a")).Single();

        listed.RunTimeout.ShouldBe(TimeSpan.FromMinutes(42));
        listed.Nodes[1].Timeout.ShouldBe(TimeSpan.FromMilliseconds(1500));
    }

    [Fact]
    public async Task The_response_mode_survives_the_round_trip()
    {
        // Added in phase 208: the hand-written DTO trap a third time. A mode the payload
        // dropped would read back as Branch, and a Single-answer workflow would open a
        // branch for every repeated answer with nothing failing.
        await Store.SaveAsync("tenant-a", new WorkflowDefinition
        {
            Name = "approval",
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "manager",
            AgentNames = ["writer"],
            RequirePlanApproval = true,
            ResponseMode = WorkflowResponseMode.SingleAnswer,
        });

        (await Store.GetAsync("tenant-a", "approval"))!.ResponseMode.ShouldBe(WorkflowResponseMode.SingleAnswer);
        (await Store.ListAsync("tenant-a")).Single().ResponseMode.ShouldBe(WorkflowResponseMode.SingleAnswer);
        (await Store.GetVersionAsync("tenant-a", "approval", 1))!.ResponseMode.ShouldBe(WorkflowResponseMode.SingleAnswer);
    }

    [Fact]
    public async Task Saving_again_without_timeouts_clears_them()
    {
        // A save replaces the definition (K-725): a field the request leaves out is
        // cleared, never carried over.
        await Store.SaveAsync("tenant-a", new WorkflowDefinition
        {
            Name = "podcast",
            Kind = WorkflowKind.Sequential,
            RunTimeout = TimeSpan.FromMinutes(42),
            Nodes = [new WorkflowNodeReference { Name = "render", Kind = WorkflowNodeKind.Function, Timeout = TimeSpan.FromSeconds(5) }],
        });

        await Store.SaveAsync("tenant-a", new WorkflowDefinition
        {
            Name = "podcast",
            Kind = WorkflowKind.Sequential,
            Nodes = [new WorkflowNodeReference { Name = "render", Kind = WorkflowNodeKind.Function }],
        });

        var loaded = (await Store.GetAsync("tenant-a", "podcast"))!;

        loaded.RunTimeout.ShouldBeNull();
        loaded.Nodes[0].Timeout.ShouldBeNull();
    }

    [Fact]
    public async Task Saving_again_without_node_settings_clears_them()
    {
        // A save replaces the definition (K-725); the store must not carry the old node's
        // settings over to a node that no longer has any.
        await Store.SaveAsync("tenant-a", new WorkflowDefinition
        {
            Name = "narrated",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference
                {
                    Name = "tts",
                    Kind = WorkflowNodeKind.Function,
                    Settings = JsonDocument.Parse("""{"voice":"alloy"}""").RootElement.Clone(),
                },
            ],
        });

        await Store.SaveAsync("tenant-a", new WorkflowDefinition
        {
            Name = "narrated",
            Kind = WorkflowKind.Sequential,
            Nodes = [new WorkflowNodeReference { Name = "tts", Kind = WorkflowNodeKind.Function }],
        });

        (await Store.GetAsync("tenant-a", "narrated"))!.Nodes[0].Settings.ShouldBeNull();
    }

    [Fact]
    public async Task Parameter_schema_survives_the_round_trip()
    {
        // Added in phase 194 (K-580): the same hand-written DTO trap. The
        // in-memory store hands the record back as it was given, so only a SQL
        // run can show the schema vanishing.
        var definition = Definition() with
        {
            Name = "parameterized",
            Parameters =
            [
                new AgentParameter
                {
                    Name = "tone",
                    Kind = AgentParameterKind.Text,
                    Required = true,
                    DefaultValue = "formal",
                    Description = "The register of the answer.",
                },
                new AgentParameter { Name = "retries", Kind = AgentParameterKind.Number },
                new AgentParameter { Name = "verbose", Kind = AgentParameterKind.Boolean, DefaultValue = "false" },
            ],
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = (await Store.GetAsync("tenant-a", "parameterized"))!;

        loaded.Parameters.Count.ShouldBe(3);
        loaded.Parameters[0].Name.ShouldBe("tone");
        loaded.Parameters[0].Kind.ShouldBe(AgentParameterKind.Text);
        loaded.Parameters[0].Required.ShouldBeTrue();
        loaded.Parameters[0].DefaultValue.ShouldBe("formal");
        loaded.Parameters[0].Description.ShouldBe("The register of the answer.");
        loaded.Parameters[1].Kind.ShouldBe(AgentParameterKind.Number);
        loaded.Parameters[1].Required.ShouldBeFalse();
        loaded.Parameters[1].DefaultValue.ShouldBeNull();
        loaded.Parameters[2].Kind.ShouldBe(AgentParameterKind.Boolean);
        loaded.Parameters[2].DefaultValue.ShouldBe("false");
    }

    [Fact]
    public async Task Magentic_manager_name_is_preserved()
    {
        var definition = Definition() with
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            ManagerAgentName = "manager",
            RequirePlanApproval = true,
        };

        await Store.SaveAsync("tenant-a", definition);

        var loaded = (await Store.GetAsync("tenant-a", "magentic"))!;

        loaded.ManagerAgentName.ShouldBe("manager");

        // Plan approval is part of the jsonb payload; if it were missing from
        // the hand-written DTO, it would silently disappear.
        loaded.RequirePlanApproval.ShouldBeTrue();
    }

    [Fact]
    public async Task Every_save_increments_the_version()
    {
        await Store.SaveAsync("tenant-a", Definition());
        var second = await Store.SaveAsync("tenant-a", Definition() with { Description = "Updated." });

        second.Version.ShouldBe(2);

        var loaded = await Store.GetAsync("tenant-a", "review");

        loaded!.Version.ShouldBe(2);
        loaded.Description.ShouldBe("Updated.");
    }

    [Fact]
    public async Task Incoming_version_value_is_ignored()
    {
        // The store decides the version. Trusting the value sent by the
        // client would let two users write the same version number.
        var saved = await Store.SaveAsync("tenant-a", Definition() with { Version = 99 });

        saved.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Another_tenants_definition_is_not_visible()
    {
        await Store.SaveAsync("tenant-a", Definition());

        (await Store.GetAsync("tenant-b", "review")).ShouldBeNull();
        (await Store.ListAsync("tenant-b")).ShouldBeEmpty();
        (await Store.DeleteAsync("tenant-b", "review")).ShouldBeFalse();

        (await Store.GetAsync("tenant-a", "review")).ShouldNotBeNull();
    }

    [Fact]
    public async Task Same_name_is_independent_across_tenants()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Description = "Tenant A." });
        await Store.SaveAsync("tenant-b", Definition() with { Description = "Tenant B." });

        (await Store.GetAsync("tenant-a", "review"))!.Description.ShouldBe("Tenant A.");
        (await Store.GetAsync("tenant-b", "review"))!.Description.ShouldBe("Tenant B.");
    }

    [Fact]
    public async Task Listing_is_sorted_by_name()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Name = "zeta" });
        await Store.SaveAsync("tenant-a", Definition() with { Name = "alpha" });
        await Store.SaveAsync("tenant-a", Definition() with { Name = "beta" });

        var names = (await Store.ListAsync("tenant-a")).Select(static definition => definition.Name).ToList();

        names.ShouldBe(["alpha", "beta", "zeta"]);
    }

    [Fact]
    public async Task Deleting_a_nonexistent_definition_returns_false()
        => (await Store.DeleteAsync("tenant-a", "missing")).ShouldBeFalse();

    [Fact]
    public async Task Deleted_definition_is_not_read_back()
    {
        await Store.SaveAsync("tenant-a", Definition());

        (await Store.DeleteAsync("tenant-a", "review")).ShouldBeTrue();
        (await Store.GetAsync("tenant-a", "review")).ShouldBeNull();
    }

    // --- Version history (phase 200) ---------------------------------------

    [Fact]
    public async Task Every_save_keeps_its_version_and_the_history_lists_them_newest_first()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Description = "First." });
        await Store.SaveAsync("tenant-a", Definition() with { Description = "Second." });
        var third = await Store.SaveAsync("tenant-a", Definition() with { Description = "Third." });

        third.Version.ShouldBe(3);

        var history = await Store.ListVersionsAsync("tenant-a", "review");

        history.Select(static definition => definition.Version).ShouldBe([3, 2, 1]);
        history.Select(static definition => definition.Description).ShouldBe(["Third.", "Second.", "First."]);

        // A snapshot is a whole definition: the tenant and the time are set, the name is the
        // history's key and not part of the payload.
        history.ShouldAllBe(static definition => definition.Name == "review" && definition.TenantId == "tenant-a");
        history.ShouldAllBe(static definition => definition.UpdatedAt != null);

        var first = await Store.GetVersionAsync("tenant-a", "review", 1);

        first.ShouldNotBeNull();
        first.Version.ShouldBe(1);
        first.Description.ShouldBe("First.");
        first.AgentNames.ShouldBe(["researcher", "writer", "editor"]);

        // The current definition is the newest version.
        (await Store.GetAsync("tenant-a", "review"))!.Version.ShouldBe(3);
    }

    [Fact]
    public async Task A_version_or_a_history_that_does_not_exist_is_empty_not_an_error()
    {
        (await Store.GetVersionAsync("tenant-a", "never-saved", 1)).ShouldBeNull();
        (await Store.ListVersionsAsync("tenant-a", "never-saved")).ShouldBeEmpty();

        await Store.SaveAsync("tenant-a", Definition());

        (await Store.GetVersionAsync("tenant-a", "review", 2)).ShouldBeNull();
        (await Store.GetVersionAsync("tenant-a", "review", 0)).ShouldBeNull();
        (await Store.GetVersionAsync("tenant-a", "review", -1)).ShouldBeNull();
    }

    [Fact]
    public async Task A_rollback_writes_the_old_content_as_a_new_version_and_moves_nothing()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Description = "First." });
        await Store.SaveAsync("tenant-a", Definition() with { Description = "Second.", AgentNames = ["writer"] });

        var rolledBack = await Store.RollbackAsync("tenant-a", "review", 1);

        rolledBack.Version.ShouldBe(3);
        rolledBack.Description.ShouldBe("First.");
        rolledBack.AgentNames.ShouldBe(["researcher", "writer", "editor"]);
        rolledBack.TenantId.ShouldBe("tenant-a");

        var current = (await Store.GetAsync("tenant-a", "review"))!;

        current.Version.ShouldBe(3);
        current.Description.ShouldBe("First.");

        // History is never rewritten: version 2 is still what it was, and the rollback is itself a
        // version (so it can be rolled back in turn).
        (await Store.GetVersionAsync("tenant-a", "review", 2))!.Description.ShouldBe("Second.");
        (await Store.GetVersionAsync("tenant-a", "review", 3))!.Description.ShouldBe("First.");
        (await Store.ListVersionsAsync("tenant-a", "review")).Count.ShouldBe(3);
    }

    [Fact]
    public async Task A_rollback_to_a_version_that_does_not_exist_throws_and_writes_nothing()
    {
        await Store.SaveAsync("tenant-a", Definition());

        await Should.ThrowAsync<TraconException>(async () => await Store.RollbackAsync("tenant-a", "review", 7));
        await Should.ThrowAsync<TraconException>(async () => await Store.RollbackAsync("tenant-a", "never-saved", 1));

        (await Store.GetAsync("tenant-a", "review"))!.Version.ShouldBe(1);
        (await Store.ListVersionsAsync("tenant-a", "review")).Count.ShouldBe(1);
        (await Store.GetAsync("tenant-a", "never-saved")).ShouldBeNull();
    }

    [Fact]
    public async Task Deleting_keeps_the_history_and_the_numbering_continues_after_it()
    {
        // A version number is an identity: a run that recorded version 3 must never be handed another
        // content under that number. An agent's history dies with it and restarts at 1 (the
        // hazard this store avoids); a workflow's does not.
        await Store.SaveAsync("tenant-a", Definition() with { Description = "One." });
        await Store.SaveAsync("tenant-a", Definition() with { Description = "Two." });
        await Store.SaveAsync("tenant-a", Definition() with { Description = "Three." });

        (await Store.DeleteAsync("tenant-a", "review")).ShouldBeTrue();

        (await Store.GetAsync("tenant-a", "review")).ShouldBeNull();
        (await Store.ListAsync("tenant-a")).ShouldBeEmpty();

        // The history outlives the definition.
        (await Store.GetVersionAsync("tenant-a", "review", 3))!.Description.ShouldBe("Three.");
        (await Store.ListVersionsAsync("tenant-a", "review")).Count.ShouldBe(3);

        var again = await Store.SaveAsync("tenant-a", Definition() with { Description = "Four." });

        again.Version.ShouldBe(4);
        (await Store.GetVersionAsync("tenant-a", "review", 3))!.Description.ShouldBe("Three.");
        (await Store.GetVersionAsync("tenant-a", "review", 4))!.Description.ShouldBe("Four.");
        (await Store.ListVersionsAsync("tenant-a", "review")).Select(static definition => definition.Version)
            .ShouldBe([4, 3, 2, 1]);
    }

    [Fact]
    public async Task A_rollback_of_a_deleted_definition_creates_it_again_and_continues_the_numbering()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Description = "One." });
        await Store.SaveAsync("tenant-a", Definition() with { Description = "Two." });
        await Store.DeleteAsync("tenant-a", "review");

        var restored = await Store.RollbackAsync("tenant-a", "review", 1);

        restored.Version.ShouldBe(3);
        restored.Description.ShouldBe("One.");
        (await Store.GetAsync("tenant-a", "review"))!.Version.ShouldBe(3);
    }

    [Fact]
    public async Task Deleting_a_definition_that_does_not_exist_leaves_the_history_alone()
    {
        await Store.SaveAsync("tenant-a", Definition());
        await Store.DeleteAsync("tenant-a", "review");

        (await Store.DeleteAsync("tenant-a", "review")).ShouldBeFalse();
        (await Store.ListVersionsAsync("tenant-a", "review")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_saves_of_one_name_get_distinct_versions_and_each_keeps_its_own_content()
    {
        // The first save of a name goes through the INSERT branch of the upsert, every other through the
        // UPDATE branch: both are in the run.
        const int Writers = 8;

        var saved = await Task.WhenAll(Enumerable.Range(1, Writers).Select(
            index => Task.Run(async () => await Store.SaveAsync("tenant-a", Definition() with { Description = $"Writer {index}." }))));

        saved.Select(static definition => definition.Version).Order().ShouldBe(Enumerable.Range(1, Writers));

        var history = await Store.ListVersionsAsync("tenant-a", "review");

        history.Select(static definition => definition.Version).ShouldBe(Enumerable.Range(1, Writers).Reverse());

        // A number names one content: what each caller was told it wrote is what that version holds.
        foreach (var definition in saved)
        {
            (await Store.GetVersionAsync("tenant-a", "review", definition.Version))!.Description.ShouldBe(definition.Description);
        }

        (await Store.GetAsync("tenant-a", "review"))!.Version.ShouldBe(Writers);
    }

    [Fact]
    public async Task Another_tenant_neither_reads_nor_rolls_back_nor_numbers_with_this_tenants_history()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Description = "A one." });
        await Store.SaveAsync("tenant-a", Definition() with { Description = "A two." });

        (await Store.GetVersionAsync("tenant-b", "review", 1)).ShouldBeNull();
        (await Store.ListVersionsAsync("tenant-b", "review")).ShouldBeEmpty();
        await Should.ThrowAsync<TraconException>(async () => await Store.RollbackAsync("tenant-b", "review", 1));

        // Nothing leaked the other way either: tenant B has no definition, and its first save is version 1.
        (await Store.GetAsync("tenant-b", "review")).ShouldBeNull();

        var first = await Store.SaveAsync("tenant-b", Definition() with { Description = "B one." });

        first.Version.ShouldBe(1);
        (await Store.GetVersionAsync("tenant-a", "review", 1))!.Description.ShouldBe("A one.");
        (await Store.GetVersionAsync("tenant-b", "review", 1))!.Description.ShouldBe("B one.");
        (await Store.ListVersionsAsync("tenant-a", "review")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Every_field_of_a_definition_is_kept_by_the_version_it_was_saved_as()
    {
        // The history is a second copy of the same hand-written payload (K-580 again): a field
        // the current row keeps but the history drops would roll back to a definition with a
        // hole, and only a SQL run can show it.
        var rich = new WorkflowDefinition
        {
            Name = "podcast",
            DisplayName = "Podcast",
            Description = "The narrated hour.",
            Kind = WorkflowKind.Sequential,
            RunTimeout = TimeSpan.FromMinutes(42),
            ResponseMode = WorkflowResponseMode.SingleAnswer,
            Parameters = [new AgentParameter { Name = "tone", Kind = AgentParameterKind.Text, Required = true, DefaultValue = "warm" }],
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent, AgentVersion = 2, Timeout = TimeSpan.FromMinutes(6) },
                new WorkflowNodeReference
                {
                    Name = "tts",
                    Kind = WorkflowNodeKind.Function,
                    Timeout = TimeSpan.FromSeconds(90),
                    Settings = JsonDocument.Parse("""{"voice":"alloy","loudnessLufs":-12.5}""").RootElement.Clone(),
                },
                new WorkflowNodeReference { Name = "editor", Kind = WorkflowNodeKind.Agent },
            ],
        };

        await Store.SaveAsync("tenant-a", rich);
        await Store.SaveAsync("tenant-a", new WorkflowDefinition { Name = "podcast", Kind = WorkflowKind.Sequential, AgentNames = ["writer"] });

        foreach (var restored in new[]
        {
            (await Store.GetVersionAsync("tenant-a", "podcast", 1))!,
            (await Store.ListVersionsAsync("tenant-a", "podcast")).Single(static definition => definition.Version == 1),
            await Store.RollbackAsync("tenant-a", "podcast", 1),
        })
        {
            restored.DisplayName.ShouldBe("Podcast");
            restored.Description.ShouldBe("The narrated hour.");
            restored.RunTimeout.ShouldBe(TimeSpan.FromMinutes(42));
            restored.ResponseMode.ShouldBe(WorkflowResponseMode.SingleAnswer);
            restored.Parameters.Single().DefaultValue.ShouldBe("warm");
            restored.Nodes.Count.ShouldBe(3);
            restored.Nodes[0].AgentVersion.ShouldBe(2);
            restored.Nodes[0].Timeout.ShouldBe(TimeSpan.FromMinutes(6));
            restored.Nodes[1].AgentVersion.ShouldBeNull();
            restored.Nodes[1].Timeout.ShouldBe(TimeSpan.FromSeconds(90));
            restored.Nodes[1].Settings.ShouldNotBeNull().GetProperty("voice").GetString().ShouldBe("alloy");
            restored.Nodes[2].AgentVersion.ShouldBeNull();
            restored.AgentNames.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Agent_version_pins_survive_the_round_trip()
    {
        // Added in phase 200 (K-580): the node record carries the pin, and the in-memory store hands
        // the record back as it was given, so only a SQL run can show the pin vanishing.
        await Store.SaveAsync("tenant-a", new WorkflowDefinition
        {
            Name = "pinned",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent, AgentVersion = 3 },
                new WorkflowNodeReference { Name = "editor", Kind = WorkflowNodeKind.Agent },
            ],
        });

        var loaded = (await Store.GetAsync("tenant-a", "pinned"))!;

        loaded.Nodes[0].AgentVersion.ShouldBe(3);
        loaded.Nodes[1].AgentVersion.ShouldBeNull();

        var listed = (await Store.ListAsync("tenant-a")).Single();

        listed.Nodes[0].AgentVersion.ShouldBe(3);
        listed.Nodes[1].AgentVersion.ShouldBeNull();

        // A save replaces the definition (K-725): a pin the request leaves out is cleared.
        await Store.SaveAsync("tenant-a", new WorkflowDefinition
        {
            Name = "pinned",
            Kind = WorkflowKind.Sequential,
            Nodes = [new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent }],
        });

        (await Store.GetAsync("tenant-a", "pinned"))!.Nodes[0].AgentVersion.ShouldBeNull();
        (await Store.GetVersionAsync("tenant-a", "pinned", 1))!.Nodes[0].AgentVersion.ShouldBe(3);
    }

    [Fact]
    public async Task The_new_members_refuse_a_cancelled_token_and_a_cancelled_rollback_leaves_no_trace()
    {
        await Store.SaveAsync("tenant-a", Definition() with { Description = "One." });
        await Store.SaveAsync("tenant-a", Definition() with { Description = "Two." });

        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Store.GetVersionAsync("tenant-a", "review", 1, source.Token));
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Store.ListVersionsAsync("tenant-a", "review", source.Token));
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Store.RollbackAsync("tenant-a", "review", 1, source.Token));

        // The half that proves the work never ran (see StoreCancellationContract).
        (await Store.GetAsync("tenant-a", "review"))!.Version.ShouldBe(2);
        (await Store.ListVersionsAsync("tenant-a", "review")).Count.ShouldBe(2);
    }

    // The hooks below exist for the SQL providers: a version written in one transaction with the
    // current row can only be shown by making the history write fail. An implementation with no raw
    // access (the in-memory store) leaves them off and the test returns, as AuditLogContract does.

    /// <summary>Whether the backing state of this store can be reached with raw SQL (SQL providers only).</summary>
    protected virtual bool SupportsRawHistory => false;

    /// <summary>Runs raw SQL against the backing store. Only called when <see cref="SupportsRawHistory"/> is <see langword="true"/>.</summary>
    protected virtual ValueTask ExecuteRawAsync(string sql) => throw new NotSupportedException();

    /// <summary>The fully qualified <c>workflow_definition_versions</c> table reference this provider's raw SQL should use.</summary>
    protected virtual string QualifiedWorkflowHistoryTable => throw new NotSupportedException();

    /// <summary>The expression this provider's raw SQL uses for "now" in a timestamp column.</summary>
    protected virtual string RawNowExpression => throw new NotSupportedException();

    [Fact]
    public async Task A_save_whose_history_write_fails_leaves_the_current_definition_as_it_was()
    {
        if (!SupportsRawHistory)
        {
            return;
        }

        await Store.SaveAsync("tenant-a", Definition() with { Description = "Stays." });

        // Occupy the number the next save will take, so the history insert collides.
        await ExecuteRawAsync(
            $"INSERT INTO {QualifiedWorkflowHistoryTable} (tenant_id, name, version, definition, created_at) " +
            $"VALUES ('tenant-a', 'review', 2, '{{}}', {RawNowExpression})");

        await Should.ThrowAsync<Exception>(
            async () => await Store.SaveAsync("tenant-a", Definition() with { Description = "Never lands." }));

        // One transaction: the current row did not move on without a history entry to name it.
        var current = (await Store.GetAsync("tenant-a", "review"))!;

        current.Version.ShouldBe(1);
        current.Description.ShouldBe("Stays.");
    }

    private static WorkflowDefinition Definition()
        => new()
        {
            Name = "review",
            Description = "Three-step review.",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["researcher", "writer", "editor"],
        };
}

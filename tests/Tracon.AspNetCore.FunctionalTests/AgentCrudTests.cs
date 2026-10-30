using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Agent definition lifecycle: create, read, update, versioning,
/// rollback, and delete.
/// </summary>
public sealed class AgentCrudTests
{
    private static readonly Uri Agents = new("/tracon/api/agents", UriKind.Relative);

    [Fact]
    public async Task Definition_is_created_and_appears_in_the_catalog()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request());
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        // 201 without a Location header is a half-kept promise: the client is
        // told a resource was made but not where it is.
        created.Headers.Location?.ToString().ShouldBe("/tracon/api/agents/db-agent");

        using var list = await host.Client.GetAsync(Agents);
        var json = await TraconTestHost.ReadJsonAsync(list);

        json.EnumerateArray()
            .Select(static agent => agent.GetProperty("name").GetString())
            .ShouldContain(static name => name == "db-agent");
    }

    [Fact]
    public async Task Update_produces_a_new_version()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var updated = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/db-agent", UriKind.Relative),
            TestData.Request(instructions: "Now give a long answer."));

        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(updated)).GetProperty("version").GetInt32().ShouldBe(2);

        using var versions = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/db-agent/versions", UriKind.Relative));

        var history = await TraconTestHost.ReadJsonAsync(versions);
        history.GetArrayLength().ShouldBe(2);

        // Newest first. A history served oldest-first still has the right
        // COUNT, so a count-only assertion cannot tell the two apart — and the
        // UI shows whatever comes first as "current".
        history.EnumerateArray()
            .Select(static version => version.GetProperty("version").GetInt32())
            .ToArray()
            .ShouldBe([2, 1]);
    }

    [Fact]
    public async Task Rollback_writes_the_old_content_as_a_new_version()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request(instructions: "first")))
        {
            created.EnsureSuccessStatusCode();
        }

        using (var updated = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/db-agent", UriKind.Relative),
            TestData.Request(instructions: "second")))
        {
            updated.EnsureSuccessStatusCode();
        }

        using var rolledBack = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/db-agent/rollback", UriKind.Relative),
            new AgentRollbackRequest { Version = 1 });

        rolledBack.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(rolledBack);
        json.GetProperty("instructions").GetString().ShouldBe("first");

        // Rollback does NOT DELETE the old version; it writes its content as a new version.
        json.GetProperty("version").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Delete_removes_the_definition()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var deleted = await host.Client.DeleteAsync(
            new Uri("/tracon/api/agents/db-agent", UriKind.Relative));

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var missing = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/db-agent", UriKind.Relative));

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Detail_marks_a_code_agent_as_not_editable()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/kod-agent", UriKind.Relative));

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("isEditable").GetBoolean().ShouldBeFalse();
        json.GetProperty("descriptor").GetProperty("origin").GetString().ShouldBe("Code");
    }

    [Fact]
    public async Task Detail_of_a_declarative_code_agent_exposes_its_in_memory_definition()
    {
        // TestData.Definition() sets Instructions = "Reply briefly." - this proves
        // it reaches the response even though it is never written to the database
        // (IAgentDefinitionStore never sees a code agent's definition at all).
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/kod-agent", UriKind.Relative));

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("definition").GetProperty("instructions").GetString().ShouldBe("Reply briefly.");
        json.GetProperty("factoryInstructions").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Detail_of_a_factory_code_agent_reads_its_instructions_best_effort()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(
            "factory-agent",
            services => services.GetRequiredService<IModelProviderRegistry>()
                .CreateChatClient(TestData.Model())
                .AsAIAgent(instructions: "Reply briefly, factory agent.", name: "factory-agent")));

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/factory-agent", UriKind.Relative));

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("definition").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        json.GetProperty("factoryInstructions").GetString().ShouldBe("Reply briefly, factory agent.");
    }

    [Fact]
    public async Task Detail_of_a_factory_agent_with_no_readable_instructions_stays_null_instead_of_failing()
    {
        // PlainAgent is a bare AIAgent, not a ChatClientAgent - there is no
        // known way to read instructions from it. The detail view must still
        // return 200, not 500: reading instructions is a best-effort extra,
        // never a reason to break the read-only view.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent("plain-agent", static _ => new PlainAgent()));

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/plain-agent", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("definition").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        json.GetProperty("factoryInstructions").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    private sealed class PlainAgent : Microsoft.Agents.AI.AIAgent
    {
        protected override Task<Microsoft.Agents.AI.AgentResponse> RunCoreAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Agents.AI.AgentSession? session = null,
            Microsoft.Agents.AI.AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Never run in this test - only the detail endpoint's factory read is exercised.");

        protected override IAsyncEnumerable<Microsoft.Agents.AI.AgentResponseUpdate> RunCoreStreamingAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Agents.AI.AgentSession? session = null,
            Microsoft.Agents.AI.AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Never run in this test - only the detail endpoint's factory read is exercised.");

        protected override ValueTask<Microsoft.Agents.AI.AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Never run in this test - only the detail endpoint's factory read is exercised.");

        protected override ValueTask<System.Text.Json.JsonElement> SerializeSessionCoreAsync(
            Microsoft.Agents.AI.AgentSession session,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Never run in this test - only the detail endpoint's factory read is exercised.");

        protected override ValueTask<Microsoft.Agents.AI.AgentSession> DeserializeSessionCoreAsync(
            System.Text.Json.JsonElement serializedState,
            System.Text.Json.JsonSerializerOptions? jsonSerializerOptions = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("Never run in this test - only the detail endpoint's factory read is exercised.");
    }

    [Fact]
    public async Task Detail_marks_a_database_agent_as_editable()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/db-agent", UriKind.Relative));

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("isEditable").GetBoolean().ShouldBeTrue();
        json.GetProperty("definition").GetProperty("name").GetString().ShouldBe("db-agent");
    }

    // --- Code agent protection: code wins on a name collision ---

    [Fact]
    public async Task Definition_cannot_be_written_with_the_same_name_as_a_code_agent()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(Agents, TestData.Request(name: "kod-agent"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).ShouldContain("defined in code");
    }

    [Fact]
    public async Task Code_agent_cannot_be_updated()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/kod-agent", UriKind.Relative),
            TestData.Request(name: "kod-agent"));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Code_agent_cannot_be_deleted()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.DeleteAsync(
            new Uri("/tracon/api/agents/kod-agent", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A 409 that deleted the agent anyway would be the worse defect of the
        // two, and the status code alone cannot see it.
        using var still = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/kod-agent", UriKind.Relative));

        still.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Validation ---

    [Fact]
    public async Task Name_in_the_path_must_match_the_name_in_the_body()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/some-name", UriKind.Relative),
            TestData.Request(name: "other-name"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Definition_with_an_empty_name_is_rejected()
    {
        // The name is the route key. An empty one would produce a row that no
        // URL can address, so it is rejected at the door rather than stored.
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(Agents, TestData.Request(name: string.Empty));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Nonexistent_definition_cannot_be_deleted()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.DeleteAsync(
            new Uri("/tracon/api/agents/no-such-agent", UriKind.Relative));

        // A delete that reports success for a row that never existed makes
        // "delete then verify" meaningless for every caller.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nonexistent_definition_cannot_be_updated()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/no-such-agent", UriKind.Relative),
            TestData.Request(name: "no-such-agent"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Same_name_cannot_be_created_a_second_time()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var first = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            first.EnsureSuccessStatusCode();
        }

        using var second = await host.Client.PostAsJsonAsync(Agents, TestData.Request());

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Compaction_and_memory_settings_round_trip()
    {
        await using var host = await TraconTestHost.StartAsync();

        var request = TestData.Request() with
        {
            Compaction = new CompactionSettings
            {
                Strategy = CompactionStrategyKind.SlidingWindow,
                TriggerMessages = 40,
                MinimumPreservedTurns = 3,
            },
            Memory = new MemorySettings { EnableTodo = true, EnableTextSearch = true },
        };

        using (var created = await host.Client.PostAsJsonAsync(Agents, request))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/db-agent", UriKind.Relative));

        var json = await TraconTestHost.ReadJsonAsync(response);
        var definition = json.GetProperty("definition");

        definition.GetProperty("compaction").GetProperty("strategy").GetString().ShouldBe("SlidingWindow");
        definition.GetProperty("compaction").GetProperty("triggerMessages").GetInt32().ShouldBe(40);
        definition.GetProperty("memory").GetProperty("enableTodo").GetBoolean().ShouldBeTrue();
        definition.GetProperty("memory").GetProperty("enableTextSearch").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Skill_settings_round_trip_and_an_update_without_them_clears_them()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.AddSkill(new AgentSkillDefinition
            {
                TenantId = "default",
                Name = "house-style",
                Description = "How every reply is worded.",
                Instructions = "Answer in three words.",
            }));

        var request = TestData.Request() with
        {
            SkillNames = ["house-style"],
            Skills = new SkillSettings { DisableReadApproval = true },
        };

        using (var created = await host.Client.PostAsJsonAsync(Agents, request))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        }

        using (var response = await host.Client.GetAsync(new Uri("/tracon/api/agents/db-agent", UriKind.Relative)))
        {
            var definition = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("definition");

            definition.GetProperty("skills").GetProperty("disableReadApproval").GetBoolean().ShouldBeTrue();
        }

        // The request is the whole definition: an update that leaves the settings out turns the approval back on.
        using (var updated = await host.Client.PutAsJsonAsync(
                   new Uri("/tracon/api/agents/db-agent", UriKind.Relative),
                   request with { Skills = null }))
        {
            updated.EnsureSuccessStatusCode();
        }

        using (var response = await host.Client.GetAsync(new Uri("/tracon/api/agents/db-agent", UriKind.Relative)))
        {
            var definition = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("definition");

            (!definition.TryGetProperty("skills", out var skills) || skills.ValueKind == System.Text.Json.JsonValueKind.Null)
                .ShouldBeTrue(definition.ToString());
        }
    }

    [Fact]
    public async Task Validation_warns_about_skill_settings_on_an_agent_that_names_no_skill()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            TestData.Request() with { Skills = new SkillSettings { DisableReadApproval = true } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var report = await TraconTestHost.ReadJsonAsync(response);

        report.GetProperty("valid").GetBoolean().ShouldBeTrue(report.ToString());
        report.GetProperty("messages").EnumerateArray()
            .Select(static message => message.GetProperty("code").GetString())
            .Contains("skill_settings_without_skills", StringComparer.Ordinal)
            .ShouldBeTrue(report.ToString());
    }

    [Fact]
    public async Task Workspace_settings_round_trip_and_an_update_without_them_clears_them()
    {
        await using var host = await TraconTestHost.StartAsync();

        var request = TestData.Request() with
        {
            Workspace = new WorkspaceSettings { Enabled = true, ReadOnly = true, DisableReadApproval = true },
        };

        using (var created = await host.Client.PostAsJsonAsync(Agents, request))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        }

        using (var response = await host.Client.GetAsync(new Uri("/tracon/api/agents/db-agent", UriKind.Relative)))
        {
            var workspace = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("definition").GetProperty("workspace");

            workspace.GetProperty("enabled").GetBoolean().ShouldBeTrue();
            workspace.GetProperty("readOnly").GetBoolean().ShouldBeTrue();
            workspace.GetProperty("disableReadApproval").GetBoolean().ShouldBeTrue();
        }

        // The request is the whole definition: an update that leaves the settings out gives the agent no workspace.
        using (var updated = await host.Client.PutAsJsonAsync(
                   new Uri("/tracon/api/agents/db-agent", UriKind.Relative),
                   request with { Workspace = null }))
        {
            updated.EnsureSuccessStatusCode();
        }

        using (var response = await host.Client.GetAsync(new Uri("/tracon/api/agents/db-agent", UriKind.Relative)))
        {
            var definition = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("definition");

            (!definition.TryGetProperty("workspace", out var workspace) || workspace.ValueKind == System.Text.Json.JsonValueKind.Null)
                .ShouldBeTrue(definition.ToString());
        }
    }

    [Fact]
    public async Task A_stored_definition_shows_its_workspace_in_the_catalog_descriptor_the_external_check_reads()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(
                   Agents,
                   TestData.Request() with { Workspace = new WorkspaceSettings { Enabled = true } }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        }

        using var response = await host.Client.GetAsync(Agents);
        var listed = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()
            .Single(static agent => string.Equals(agent.GetProperty("name").GetString(), "db-agent", StringComparison.Ordinal));

        listed.GetProperty("workspace").GetProperty("enabled").GetBoolean().ShouldBeTrue(listed.ToString());
    }

    [Fact]
    public async Task Validation_warns_about_workspace_settings_on_an_agent_whose_workspace_is_off()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            TestData.Request() with { Workspace = new WorkspaceSettings { Enabled = false, ReadOnly = true } });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var report = await TraconTestHost.ReadJsonAsync(response);

        report.GetProperty("valid").GetBoolean().ShouldBeTrue(report.ToString());
        report.GetProperty("messages").EnumerateArray()
            .Select(static message => message.GetProperty("code").GetString())
            .Contains("workspace_settings_without_workspace", StringComparer.Ordinal)
            .ShouldBeTrue(report.ToString());
    }

    [Fact]
    public async Task Cannot_roll_back_to_a_nonexistent_version()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/db-agent/rollback", UriKind.Relative),
            new AgentRollbackRequest { Version = 99 });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // --- Version diff ---

    [Fact]
    public async Task Version_diff_returns_the_two_raw_definitions()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request(instructions: "first")))
        {
            created.EnsureSuccessStatusCode();
        }

        using (var updated = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/agents/db-agent", UriKind.Relative),
            TestData.Request(instructions: "second")))
        {
            updated.EnsureSuccessStatusCode();
        }

        using var diff = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/db-agent/versions/1/diff/2", UriKind.Relative));

        diff.StatusCode.ShouldBe(HttpStatusCode.OK);

        var json = await TraconTestHost.ReadJsonAsync(diff);
        json.GetProperty("left").GetProperty("instructions").GetString().ShouldBe("first");
        json.GetProperty("right").GetProperty("instructions").GetString().ShouldBe("second");
    }

    [Fact]
    public async Task Diff_of_a_nonexistent_version_returns_404()
    {
        await using var host = await TraconTestHost.StartAsync();

        using (var created = await host.Client.PostAsJsonAsync(Agents, TestData.Request()))
        {
            created.EnsureSuccessStatusCode();
        }

        using var diff = await host.Client.GetAsync(
            new Uri("/tracon/api/agents/db-agent/versions/1/diff/99", UriKind.Relative));

        diff.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

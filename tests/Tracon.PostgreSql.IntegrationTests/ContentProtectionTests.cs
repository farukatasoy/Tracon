using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Proves at-rest content protection against a real PostgreSQL database: the
/// stored bytes, not just the round trip through the store.
/// </summary>
/// <remarks>
/// Each test builds its own <see cref="SqlStoreContext"/> from an isolated,
/// migrated schema (<see cref="PostgresTestContext"/>) and swaps in a
/// content protector — <see cref="PostgresTestContext"/> itself always uses
/// the no-op default, so protection is layered on top here rather than
/// changed in the shared fixture every other contract test also relies on.
/// </remarks>
#pragma warning disable MAAI001 // AgentFileStore is "evaluation purposes only" — same rationale as the product code.
public sealed class ContentProtectionTests(PostgresFixture fixture)
{
    private static readonly string TestKey = Convert.ToBase64String(Enumerable.Range(0, 32).Select(static i => (byte)i).ToArray());

    [Fact]
    public async Task Session_state_is_encrypted_at_rest_when_protection_is_on()
    {
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);
        var protectingContext = BuildProtectingContext(baseContext, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.SessionState });
        var sessions = new SqlSessionStore(protectingContext, baseContext.TenantContext);

        await sessions.SaveAsync(TestData.Session("s1", TestData.State("""{"turn":"secret-value"}""")));

        var raw = await baseContext.ScalarAsync<string>($"SELECT state FROM {baseContext.SchemaName}.sessions WHERE id = 's1';");
        raw.ShouldNotBeNull();
        raw.ShouldContain("$apEnc");
        raw.ShouldNotContain("secret-value");

        var loaded = await sessions.GetAsync("s1");
        loaded.ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetString().ShouldBe("secret-value");
    }

    [Fact]
    public async Task Session_state_stays_plaintext_when_protection_is_off()
    {
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);
        var sessions = new SqlSessionStore(baseContext.StoreContext, baseContext.TenantContext);

        await sessions.SaveAsync(TestData.Session("s1", TestData.State("""{"turn":"visible-value"}""")));

        var raw = await baseContext.ScalarAsync<string>($"SELECT state FROM {baseContext.SchemaName}.sessions WHERE id = 's1';");
        raw.ShouldNotBeNull();
        raw.ShouldContain("visible-value");
        raw.ShouldNotContain("$apEnc");
    }

    [Fact]
    public async Task A_row_written_before_protection_was_turned_on_stays_readable_after()
    {
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);
        var unprotectedSessions = new SqlSessionStore(baseContext.StoreContext, baseContext.TenantContext);
        await unprotectedSessions.SaveAsync(TestData.Session("legacy", TestData.State("""{"turn":"old-value"}""")));

        var protectingContext = BuildProtectingContext(baseContext, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.SessionState });
        var protectedSessions = new SqlSessionStore(protectingContext, baseContext.TenantContext);

        var loaded = await protectedSessions.GetAsync("legacy");

        loaded.ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetString().ShouldBe("old-value");
    }

    [Fact]
    public async Task A_row_written_while_protection_was_on_stays_readable_after_it_is_turned_off()
    {
        // The mirror of the previous test. Realistic "turned off": the SAME
        // AesGcmContentProtector/Keys stay registered (only Enabled flips to
        // false), because that is what AddContentProtection(...) + Enabled:
        // false looks like in production - not a swapped-out protector.
        // Unprotect looks at the value's own $apEnc tag, never at IsEnabled,
        // so a row written while on must not strand once it is turned off.
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);

        var options = new TraconContentProtectionOptions { Enabled = true, ActiveKeyId = "k1" };
        options.Keys["k1"] = "Keys:k1";
        var configuration = new FakeConfiguration(new Dictionary<string, string>(StringComparer.Ordinal) { ["Keys:k1"] = TestKey });
        var protector = new AesGcmContentProtector(new StaticMonitor(options), configuration);

        var onContext = new SqlStoreContext
        {
            DataSource = baseContext.StoreContext.DataSource,
            Dialect = baseContext.StoreContext.Dialect,
            CommandTimeoutSeconds = baseContext.StoreContext.CommandTimeoutSeconds,
            ProviderName = baseContext.StoreContext.ProviderName,
            ContentProtector = protector,
            ProtectedColumns = new HashSet<ProtectedColumn> { ProtectedColumn.SessionState },
        };
        var protectedSessions = new SqlSessionStore(onContext, baseContext.TenantContext);
        await protectedSessions.SaveAsync(TestData.Session("turned-off", TestData.State("""{"turn":"still-readable"}""")));

        var rawWhileOn = await baseContext.ScalarAsync<string>($"SELECT state FROM {baseContext.SchemaName}.sessions WHERE id = 'turned-off';");
        rawWhileOn.ShouldNotBeNull();
        rawWhileOn.ShouldContain("$apEnc");

        options.Enabled = false;

        var offContext = new SqlStoreContext
        {
            DataSource = baseContext.StoreContext.DataSource,
            Dialect = baseContext.StoreContext.Dialect,
            CommandTimeoutSeconds = baseContext.StoreContext.CommandTimeoutSeconds,
            ProviderName = baseContext.StoreContext.ProviderName,
            ContentProtector = protector,
            ProtectedColumns = System.Collections.Immutable.ImmutableHashSet<ProtectedColumn>.Empty,
        };
        var unprotectedSessions = new SqlSessionStore(offContext, baseContext.TenantContext);
        var loaded = await unprotectedSessions.GetAsync("turned-off");

        loaded.ShouldNotBeNull();
        loaded.State.GetProperty("turn").GetString().ShouldBe("still-readable");
    }

    [Fact]
    public async Task A_column_left_out_of_the_protected_set_stays_plaintext_while_another_column_is_encrypted()
    {
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);

        // Protection is ON overall, but Columns only names SessionState -
        // RunInput must be written as plaintext even though the same
        // protector instance is active.
        var protectingContext = BuildProtectingContext(baseContext, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.SessionState });

        var sessions = new SqlSessionStore(protectingContext, baseContext.TenantContext);
        var runInputs = new SqlRunInputStore(protectingContext);
        var runs = new SqlRunStore(protectingContext, baseContext.TenantContext);

        await sessions.SaveAsync(TestData.Session("s1", TestData.State("""{"turn":"session-secret"}""")));

        var runId = Guid.NewGuid();
        await runs.StartRunAsync(TestData.Run(runId));
        await runInputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = baseContext.TenantContext.TenantId,
            Messages = [new ChatMessage(ChatRole.User, "run-input-not-secret")],
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var rawSession = await baseContext.ScalarAsync<string>($"SELECT state FROM {baseContext.SchemaName}.sessions WHERE id = 's1';");
        var rawRunInput = await baseContext.ScalarAsync<string>($"SELECT messages FROM {baseContext.SchemaName}.run_inputs WHERE run_id = '{runId}';");

        rawSession.ShouldNotBeNull();
        rawSession.ShouldContain("$apEnc");

        rawRunInput.ShouldNotBeNull();
        rawRunInput.ShouldContain("run-input-not-secret");
        rawRunInput.ShouldNotContain("$apEnc");
    }

    [Fact]
    public async Task Run_input_messages_are_encrypted_at_rest()
    {
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);
        var protectingContext = BuildProtectingContext(baseContext, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.RunInput });
        var runInputs = new SqlRunInputStore(protectingContext);
        var runs = new SqlRunStore(protectingContext, baseContext.TenantContext);
        var runId = Guid.NewGuid();

        await runs.StartRunAsync(TestData.Run(runId));
        await runInputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = baseContext.TenantContext.TenantId,
            Messages = [new ChatMessage(ChatRole.User, "credit card 4111 1111 1111 1111")],
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var raw = await baseContext.ScalarAsync<string>($"SELECT messages FROM {baseContext.SchemaName}.run_inputs WHERE run_id = '{runId}';");
        raw.ShouldNotBeNull();
        raw.ShouldContain("$apEnc");
        raw.ShouldNotContain("4111");

        var loaded = await runInputs.GetAsync(baseContext.TenantContext.TenantId, runId);
        loaded.ShouldNotBeNull();
        loaded.Messages.ShouldHaveSingleItem().Text.ShouldBe("credit card 4111 1111 1111 1111");
    }

    [Fact]
    public async Task Run_event_text_and_payload_are_encrypted_at_rest()
    {
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);
        var protectingContext = BuildProtectingContext(baseContext, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.RunEventText, ProtectedColumn.RunEventPayload });
        var runs = new SqlRunStore(protectingContext, baseContext.TenantContext);
        var runId = Guid.NewGuid();

        await runs.StartRunAsync(TestData.Run(runId));
        await runs.AppendEventAsync(new RunEvent
        {
            RunId = runId,
            Sequence = 0,
            Type = RunEventType.MessageDelta,
            Text = "delta with secret-token",
            Payload = "{\"raw\":\"secret-payload\"}",
            Timestamp = DateTimeOffset.UtcNow,
        });

        var rawText = await baseContext.ScalarAsync<string>($"SELECT text FROM {baseContext.SchemaName}.run_events WHERE run_id = '{runId}';");
        var rawPayload = await baseContext.ScalarAsync<string>($"SELECT payload FROM {baseContext.SchemaName}.run_events WHERE run_id = '{runId}';");

        rawText.ShouldNotBeNull();
        rawText.ShouldContain("$apEnc");
        rawText.ShouldNotContain("secret-token");

        rawPayload.ShouldNotBeNull();
        rawPayload.ShouldContain("$apEnc");
        rawPayload.ShouldNotContain("secret-payload");

        var events = await runs.ReadEventsAsync(runId).ToListAsync();
        var loaded = events.ShouldHaveSingleItem();
        loaded.Text.ShouldBe("delta with secret-token");
        loaded.Payload.ShouldBe("{\"raw\":\"secret-payload\"}");
    }

    [Fact]
    public async Task Tool_arguments_and_result_are_encrypted_at_rest()
    {
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);
        var protectingContext = BuildProtectingContext(baseContext, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.ToolArguments, ProtectedColumn.ToolResult });
        var runs = new SqlRunStore(protectingContext, baseContext.TenantContext);
        var runId = Guid.NewGuid();

        await runs.StartRunAsync(TestData.Run(runId));
        await runs.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            ToolName = "lookup_customer",
            Arguments = "{\"ssn\":\"111-22-3333\"}",
            Result = "{\"name\":\"secret-customer-name\"}",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var rawArguments = await baseContext.ScalarAsync<string>($"SELECT arguments FROM {baseContext.SchemaName}.tool_invocations WHERE run_id = '{runId}';");
        var rawResult = await baseContext.ScalarAsync<string>($"SELECT result FROM {baseContext.SchemaName}.tool_invocations WHERE run_id = '{runId}';");

        rawArguments.ShouldNotBeNull();
        rawArguments.ShouldContain("$apEnc");
        rawArguments.ShouldNotContain("111-22-3333");

        rawResult.ShouldNotBeNull();
        rawResult.ShouldContain("$apEnc");
        rawResult.ShouldNotContain("secret-customer-name");

        var stored = (await runs.ListToolInvocationsAsync(runId)).ShouldHaveSingleItem();
        stored.Arguments.ShouldBe("{\"ssn\":\"111-22-3333\"}");
        stored.Result.ShouldBe("{\"name\":\"secret-customer-name\"}");
    }

    [Fact]
    public async Task Agent_file_content_is_encrypted_at_rest_and_search_still_finds_matches()
    {
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);
        var protectingContext = BuildProtectingContext(baseContext, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.AgentFileContent });
        var agentFiles = new SqlAgentFileStore(protectingContext, baseContext.TenantContext);

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            AgentName = "cp-test-agent",
        });

        try
        {
            await agentFiles.WriteAsync("/notes.md", "line one\nsecret-marker line\nline three");

            var raw = await baseContext.ScalarAsync<string>(
                $"SELECT content FROM {baseContext.SchemaName}.agent_files WHERE agent_name = 'cp-test-agent' AND path = '/notes.md';");
            raw.ShouldNotBeNull();
            raw.ShouldContain("$apEnc");
            raw.ShouldNotContain("secret-marker");

            var results = await agentFiles.SearchAsync("/", "secret-marker");
            results.ShouldHaveSingleItem().FileName.ShouldBe("/notes.md");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public async Task Attachment_content_is_encrypted_at_rest_and_the_hash_still_matches_plaintext()
    {
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);
        var protectingContext = BuildProtectingContext(baseContext, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.AttachmentContent });
        var attachments = new SqlAttachmentStore(protectingContext);
        var plaintext = "attachment body with secret-content"u8.ToArray();

        var descriptor = await attachments.SaveAsync(new AttachmentContent
        {
            TenantId = baseContext.TenantContext.TenantId,
            FileName = "note.txt",
            MediaType = "text/plain",
            Data = plaintext,
        });

        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(plaintext));
        descriptor.Sha256.ShouldBe(expectedHash);

        var rawBytes = await baseContext.ScalarAsync<byte[]>(
            $"SELECT content FROM {baseContext.SchemaName}.attachments WHERE id = '{descriptor.Id}';");
        rawBytes.ShouldNotBeNull();
        System.Text.Encoding.UTF8.GetString(rawBytes).ShouldNotContain("secret-content");

        await using var stream = await attachments.OpenReadAsync(baseContext.TenantContext.TenantId, descriptor.Id);
        stream.ShouldNotBeNull();
        using var reader = new StreamReader(stream);
        (await reader.ReadToEndAsync()).ShouldBe("attachment body with secret-content");
    }

    [Fact]
    public async Task Reading_with_an_unconfigured_key_id_throws_naming_the_key_id()
    {
        await using var baseContext = await PostgresTestContext.CreateAsync(fixture);
        var writerContext = BuildProtectingContext(baseContext, "k1", new HashSet<ProtectedColumn> { ProtectedColumn.SessionState });
        var writer = new SqlSessionStore(writerContext, baseContext.TenantContext);
        await writer.SaveAsync(TestData.Session("s1", TestData.State("""{"turn":"x"}""")));

        var readerContext = BuildProtectingContext(baseContext, "k2", new HashSet<ProtectedColumn> { ProtectedColumn.SessionState });
        var reader = new SqlSessionStore(readerContext, baseContext.TenantContext);

        (await Should.ThrowAsync<TraconException>(async () => await reader.GetAsync("s1"))).Message.ShouldContain("k1");
    }

    private static SqlStoreContext BuildProtectingContext(PostgresTestContext baseContext, string activeKeyId, IReadOnlySet<ProtectedColumn> columns)
    {
        var options = new TraconContentProtectionOptions { Enabled = true, ActiveKeyId = activeKeyId };
        options.Keys[activeKeyId] = $"Keys:{activeKeyId}";

        var configuration = new FakeConfiguration(
            new Dictionary<string, string>(StringComparer.Ordinal) { [$"Keys:{activeKeyId}"] = TestKey });

        var protector = new AesGcmContentProtector(new StaticMonitor(options), configuration);

        return new SqlStoreContext
        {
            DataSource = baseContext.StoreContext.DataSource,
            Dialect = baseContext.StoreContext.Dialect,
            CommandTimeoutSeconds = baseContext.StoreContext.CommandTimeoutSeconds,
            ProviderName = baseContext.StoreContext.ProviderName,
            ContentProtector = protector,
            ProtectedColumns = columns,
        };
    }

    private sealed class StaticMonitor(TraconContentProtectionOptions value) : IOptionsMonitor<TraconContentProtectionOptions>
    {
        public TraconContentProtectionOptions CurrentValue => value;

        public TraconContentProtectionOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TraconContentProtectionOptions, string?> listener) => null;
    }

    private sealed class FakeConfiguration(IReadOnlyDictionary<string, string> values) : Microsoft.Extensions.Configuration.IConfiguration
    {
        public string? this[string key]
        {
            get => values.GetValueOrDefault(key);
            set => throw new NotSupportedException();
        }

        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
#pragma warning restore MAAI001

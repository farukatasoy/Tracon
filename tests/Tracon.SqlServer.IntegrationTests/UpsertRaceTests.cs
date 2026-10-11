using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// Concurrent first writes of one key through a two-statement SQL Server upsert
/// leave one row and never surface a unique-key violation (F-313).
/// </summary>
/// <remarks>
/// <para>
/// The SQL Server upsert is <c>UPDATE … WITH (UPDLOCK, SERIALIZABLE)</c> followed by
/// <c>IF @@ROWCOUNT = 0 INSERT</c>. The range lock that keeps a second session out of
/// the INSERT branch lives only as long as the transaction around it. Run in
/// autocommit, each statement is its own transaction: the lock ends with the UPDATE,
/// both sessions see zero rows, and both enter the INSERT branch.
/// </para>
/// <para>
/// 🚨 Regression (2026-10-11): <c>SkillScriptGrantContract</c>'s racing case failed
/// once in a full-solution run with "Violation of UNIQUE KEY constraint
/// 'skill_script_grants_uq'" and passed alone. The contract races one key once; these
/// cases race a FRESH key every round, so every round is a pure first-write race and
/// the window opens on a quiet machine too.
/// </para>
/// </remarks>
public sealed class UpsertRaceTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    private const int Rounds = 20;
    private const int Writers = 12;

    [Fact]
    public async Task Racing_first_grants_of_a_key_leave_one_record()
    {
        await schema.ResetAsync();
        var store = schema.Context.SkillScriptGrants;

        await RaceAsync(round => store.GrantAsync(new SkillScriptGrant
        {
            TenantId = "tenant-a",
            SkillName = "invoice",
            ScriptName = $"script-{round}",
            GrantedBy = "admin@example.com",
            GrantedAt = DateTimeOffset.UtcNow,
            ContentHash = new string('A', 64),
        }).AsTask());

        var grants = await store.ListAsync("tenant-a", TestContext.Current.CancellationToken);
        grants.Count.ShouldBe(Rounds);
    }

    /// <summary>A NULL key column (a grant for the whole skill) races the same way.</summary>
    [Fact]
    public async Task Racing_first_whole_skill_grants_leave_one_record()
    {
        await schema.ResetAsync();
        var store = schema.Context.SkillScriptGrants;

        await RaceAsync(round => store.GrantAsync(new SkillScriptGrant
        {
            TenantId = "tenant-a",
            SkillName = $"skill-{round}",
            ScriptName = null,
            GrantedBy = "admin@example.com",
            GrantedAt = DateTimeOffset.UtcNow,
            ContentHash = new string('A', 64),
        }).AsTask());

        var grants = await store.ListAsync("tenant-a", TestContext.Current.CancellationToken);
        grants.Count.ShouldBe(Rounds);
    }

    /// <summary>Quotas key on a nullable agent name: both the named and the tenant-wide scope.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Racing_first_quota_saves_leave_one_record(bool named)
    {
        await schema.ResetAsync();
        var store = schema.Context.Quotas;
        var periods = Enum.GetValues<QuotaPeriod>();

        await RaceAsync(round => store.SaveAsync(new QuotaDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = $"tenant-{round / periods.Length}",
            AgentName = named ? "writer" : null,
            Period = periods[round % periods.Length],
            MaxRuns = 10,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }).AsTask());
    }

    /// <summary>Approval rules key on four nullable columns.</summary>
    [Fact]
    public async Task Racing_first_approval_rules_leave_one_record()
    {
        await schema.ResetAsync();
        var store = schema.Context.ApprovalRules;

        await RaceAsync(round => store.AddAsync(new ToolApprovalRule
        {
            Id = Guid.Empty,
            TenantId = "tenant-a",
            AgentName = null,
            ToolName = $"tool-{round}",
            CreatedAt = DateTimeOffset.UtcNow,
        }).AsTask());

        var rules = await store.ListAsync("tenant-a", TestContext.Current.CancellationToken);
        rules.Count.ShouldBe(Rounds);
    }

    [Fact]
    public async Task Racing_first_mcp_server_saves_leave_one_record()
    {
        await schema.ResetAsync();
        var store = schema.Context.McpServers;

        await RaceAsync(round => store.SaveAsync(new McpServerDefinition
        {
            Id = Guid.Empty,
            TenantId = "tenant-a",
            Name = $"server-{round}",
            Endpoint = new Uri("https://mcp.example.com/mcp"),
        }).AsTask());
    }

    [Fact]
    public async Task Racing_first_session_saves_leave_one_record()
    {
        await schema.ResetAsync();
        var store = schema.Context.Sessions;

        await RaceAsync(round => store.SaveAsync(new SessionRecord
        {
            Id = $"session-{round}",
            TenantId = "tenant-a",
            AgentName = "writer",
            State = System.Text.Json.JsonDocument.Parse("{}").RootElement,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        }).AsTask());
    }

    [Fact]
    public async Task Racing_first_tenant_saves_leave_one_record()
    {
        await schema.ResetAsync();
        var store = schema.Context.Tenants;

        await RaceAsync(round => store.SaveAsync(new TenantDescriptor
        {
            Id = Guid.Empty,
            Slug = $"tenant-{round}",
            DisplayName = $"Tenant {round}",
        }).AsTask());
    }

    [Fact]
    public async Task Racing_first_egress_policy_saves_leave_one_record()
    {
        await schema.ResetAsync();
        var store = schema.Context.TenantEgressPolicies;

        await RaceAsync(round => store.UpsertAsync($"tenant-{round}", ["openai"]).AsTask());
    }

    /// <summary>A lease race has one winner; the losers get <see langword="false"/>, never an exception.</summary>
    [Fact]
    public async Task Racing_first_lease_acquisitions_have_one_winner()
    {
        await schema.ResetAsync();
        var store = schema.Context.SingletonLeases;
        var winners = 0;

        await RaceAsync(async round =>
        {
            if (await store.TryAcquireAsync($"lease-{round}", $"owner-{Guid.NewGuid():N}", TimeSpan.FromMinutes(1)))
            {
                Interlocked.Increment(ref winners);
            }
        });

        winners.ShouldBe(Rounds);
    }

    /// <summary>
    /// Starts <see cref="Writers"/> writes of one key at once, <see cref="Rounds"/>
    /// times, with a new key each round.
    /// </summary>
    private static async Task RaceAsync(Func<int, Task> write)
    {
        for (var round = 0; round < Rounds; round++)
        {
            var key = round;
            using var start = new ManualResetEventSlim(initialState: false);

            var writers = Enumerable.Range(0, Writers)
                .Select(_ => Task.Run(
                    async () =>
                    {
                        start.Wait(TestContext.Current.CancellationToken);
                        await write(key);
                    },
                    TestContext.Current.CancellationToken))
                .ToArray();

            start.Set();

            await Task.WhenAll(writers);
        }
    }
}

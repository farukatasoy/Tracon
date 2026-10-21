using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent with skills runs where nobody can answer an approval request (A2A, a run with no
/// session) when its definition turns the skill read approval off, and not otherwise (F-626,
/// Phase 231).
/// </summary>
/// <remarks>
/// <para>
/// Microsoft Agent Framework wraps all three skill tools for approval by default. A caller that
/// is not a person cannot answer, so before this phase a skill-using agent was exposed over A2A
/// and failed on its first <c>load_skill</c> call. Each test drives the whole path a model call
/// takes: the host, the catalog, the compiler, Microsoft Agent Framework's tool dispatcher, and
/// the run's approval boundary.
/// </para>
/// <para>
/// Each test starts its own host and uses one run: a fake provider's script is one queue.
/// </para>
/// </remarks>
public sealed class SkillExposureTests
{
    private const string SkillName = "house-style";
    private const string SkillText = "Answer every question in exactly three words.";
    private const string ScriptName = "hello";
    private const string CodeAgent = "skilled-code-agent";
    private const string DatabaseAgent = "skilled-database-agent";
    private const string ProviderName = "skill-provider";
    private const string ModelName = "skill-model";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_code_agent_with_the_read_approval_off_loads_its_skill_over_A2A_and_the_run_completes(bool harness)
    {
        await using var host = await StartAsync(
            ProviderFor("load_skill", new { skillName = SkillName }),
            builder => builder
                .AddAgent(CodeDefinition(new SkillSettings { DisableReadApproval = true }, harness))
                .UseA2A(static options => options.ExposedAgents.Add(CodeAgent)),
            static app => app.MapTraconA2A());

        var (response, body) = await SendA2AMessageAsync(host.Client, CodeAgent);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        body.TryGetProperty("error", out _).ShouldBeFalse(body.ToString());
        body.ToString().ShouldContain(SkillText, Case.Sensitive);

        (await OnlyRunAsync(host, CodeAgent)).Status.ShouldBe(RunStatus.Completed);
    }

    /// <summary>
    /// The same path for an agent defined in the database. The startup guard reads the catalog once, when
    /// the host starts, so the agent is stored by an earlier host over the same database file.
    /// </summary>
    [Fact]
    public async Task A_database_agent_with_the_read_approval_off_loads_its_skill_over_A2A_and_the_run_completes()
    {
        using var database = new TempSqliteDatabase("skill-exposure-open");
        await SeedDatabaseAgentAsync(database, new SkillSettings { DisableReadApproval = true });

        await using var host = await StartAsync(
            ProviderFor("load_skill", new { skillName = SkillName }),
            builder => builder
                .UseSqlite(database.ConnectionString)
                .UseA2A(static options => options.ExposedAgents.Add(DatabaseAgent)),
            static app => app.MapTraconA2A());

        var (response, body) = await SendA2AMessageAsync(host.Client, DatabaseAgent);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        body.TryGetProperty("error", out _).ShouldBeFalse(body.ToString());
        body.ToString().ShouldContain(SkillText, Case.Sensitive);

        (await OnlyRunAsync(host, DatabaseAgent)).Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_database_agent_with_the_read_approval_on_cannot_be_exposed_over_A2A()
    {
        using var database = new TempSqliteDatabase("skill-exposure-refused");
        await SeedDatabaseAgentAsync(database, skills: null);

        await using var host = await StartAsync(
            ProviderFor("load_skill", new { skillName = SkillName }),
            builder => builder
                .UseSqlite(database.ConnectionString)
                .UseA2A(static options => options.ExposedAgents.Add(DatabaseAgent)),
            static app => app.MapTraconA2A());

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => host.Client.GetAsync(
            new Uri($"/tracon/a2a/{DatabaseAgent}/.well-known/agent-card.json", UriKind.Relative)));

        exception.Message.ShouldContain(DatabaseAgent, Case.Sensitive);
        exception.Message.ShouldContain("skills", Case.Sensitive);
        exception.Message.ShouldContain("DisableReadApproval", Case.Sensitive);
    }

    /// <summary>
    /// The descriptor is the only thing the startup guard sees. A source that dropped the setting would
    /// refuse an agent that is safe to expose, and no unit test of the guard would notice.
    /// </summary>
    [Fact]
    public async Task The_catalog_descriptor_of_a_code_agent_and_of_a_database_agent_carries_the_skill_settings()
    {
        await using var host = await StartAsync(
            ProviderFor("load_skill", new { skillName = SkillName }),
            static builder => builder.AddAgent(CodeDefinition(new SkillSettings { DisableReadApproval = true }, harness: false)));

        await CreateDatabaseAgentAsync(host, new SkillSettings { DisableReadApproval = true });

        var catalog = await host.Services.GetRequiredService<IAgentCatalog>().ListAsync();

        catalog.Single(static descriptor => string.Equals(descriptor.Name, CodeAgent, StringComparison.Ordinal))
            .Skills.ShouldNotBeNull().DisableReadApproval.ShouldBeTrue();
        catalog.Single(static descriptor => string.Equals(descriptor.Name, DatabaseAgent, StringComparison.Ordinal))
            .Skills.ShouldNotBeNull().DisableReadApproval.ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_without_a_session_loads_the_skill_when_the_read_approval_is_off()
    {
        await using var host = await StartAsync(
            ProviderFor("load_skill", new { skillName = SkillName }),
            builder => builder.AddAgent(CodeDefinition(new SkillSettings { DisableReadApproval = true }, harness: false)));

        using var response = await RunWithoutSessionAsync(host, CodeAgent);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).ShouldContain(SkillText, Case.Sensitive);
        (await OnlyRunAsync(host, CodeAgent)).Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_run_without_a_session_ends_at_the_first_load_skill_when_the_read_approval_is_on()
    {
        await using var host = await StartAsync(
            ProviderFor("load_skill", new { skillName = SkillName }),
            builder => builder.AddAgent(CodeDefinition(skills: null, harness: false)));

        using var response = await RunWithoutSessionAsync(host, CodeAgent);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("errorType").GetString().ShouldBe("approval_unanswerable");
        (await OnlyRunAsync(host, CodeAgent)).Status.ShouldBe(RunStatus.Failed);
    }

    /// <summary>
    /// Turning the read approval off must not open the script tool. The model asks for a script; the run
    /// stops at the approval and the executor never receives the request.
    /// </summary>
    [Fact]
    public async Task The_script_tool_still_asks_for_approval_when_the_read_approval_is_off()
    {
        var executor = new RecordingScriptExecutor();

        await using var host = await StartAsync(
            ProviderFor("run_skill_script", new { skillName = SkillName, scriptName = ScriptName }),
            builder => builder.AddAgent(CodeDefinition(new SkillSettings { DisableReadApproval = true }, harness: false)),
            configureServices: services => services.AddSingleton<ISkillScriptExecutor>(executor),
            withScript: true);

        using var response = await RunWithoutSessionAsync(host, CodeAgent);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("errorType").GetString().ShouldBe("approval_unanswerable");
        (await OnlyRunAsync(host, CodeAgent)).Status.ShouldBe(RunStatus.Failed);
        executor.Requests.ShouldBeEmpty("the run stopped at the approval, so no script reached the executor");
    }

    private static FakeModelProvider ProviderFor(string toolName, object arguments)
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .CallsTool(toolName, arguments)
            .EchoesLastToolResult();

    private static AgentDefinition CodeDefinition(SkillSettings? skills, bool harness)
        => new()
        {
            Name = CodeAgent,
            Instructions = "Use the skill.",
            Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
            SkillNames = [SkillName],
            Skills = skills,
            Harness = harness ? new HarnessSettings() : null,
            Origin = AgentDefinitionOrigin.Code,
        };

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        Action<ITraconBuilder>? configureTracon = null,
        Action<Microsoft.AspNetCore.Builder.WebApplication>? configureAfterMap = null,
        Action<IServiceCollection>? configureServices = null,
        bool withScript = false)
        => TraconTestHost.StartAsync(
            configureServices: configureServices,
            configureTracon: builder =>
            {
                builder
                    .AddModelProvider(provider)
                    .AddSkill(new AgentSkillDefinition
                    {
                        TenantId = "default",
                        Name = SkillName,
                        Description = "How every reply is worded.",
                        Instructions = SkillText,
                        Scripts = withScript
                            ? [new AgentSkillScriptDefinition { Name = ScriptName, Extension = "sh", Content = "echo hello" }]
                            : [],
                    });

                if (withScript)
                {
                    builder.UseSkillScripts(static options =>
                    {
                        options.PlatformIsolationAcknowledged = true;
                        options.AllowStoredScripts = true;
                        options.Interpreters["sh"] = "/bin/sh";
                    });
                }

                configureTracon?.Invoke(builder);
            },
            configureAfterMap: configureAfterMap);

    /// <summary>Stores the database agent in the file and stops the host that stored it.</summary>
    private static async Task SeedDatabaseAgentAsync(TempSqliteDatabase database, SkillSettings? skills)
    {
        await using var seeding = await StartAsync(
            ProviderFor("load_skill", new { skillName = SkillName }),
            builder => builder.UseSqlite(database.ConnectionString));

        await CreateDatabaseAgentAsync(seeding, skills);
    }

    private static async Task CreateDatabaseAgentAsync(TraconTestHost host, SkillSettings? skills)
    {
        using var created = await host.Client.PostAsJsonAsync(
            "/tracon/api/agents",
            TestData.Request(DatabaseAgent) with
            {
                Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                SkillNames = [SkillName],
                Skills = skills,
            });

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    /// <summary>A buffered run with no session id: nobody can answer an approval request.</summary>
    private static async Task<HttpResponseMessage> RunWithoutSessionAsync(TraconTestHost host, string agentName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/agents/{agentName}/run")
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "Use the skill." }),
        };

        // The idempotency key selects the buffered form, whose refusal is a plain 409.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request);
    }

    private static async Task<(HttpResponseMessage Response, JsonElement Body)> SendA2AMessageAsync(HttpClient client, string agentName)
    {
        // Built with the SDK's own types and serializer options, as in A2AEndpointTests.
        var sendMessage = JsonSerializer.SerializeToElement(
            new A2A.SendMessageRequest
            {
                Message = new A2A.Message
                {
                    Role = A2A.Role.User,
                    Parts = [A2A.Part.FromText("Use the skill.")],
                    MessageId = Guid.NewGuid().ToString(),
                },
            },
            A2A.A2AJsonUtilities.DefaultOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/a2a/{agentName}")
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "SendMessage", @params = sendMessage }),
        };
        request.Headers.Accept.ParseAdd("application/json");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return (response, JsonDocument.Parse(body).RootElement.Clone());
    }

    private static async Task<RunRecord> OnlyRunAsync(TraconTestHost host, string agentName)
        => (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { AgentName = agentName }))
            .ShouldHaveSingleItem();

    /// <summary>Records every request and answers without starting anything.</summary>
    private sealed class RecordingScriptExecutor : ISkillScriptExecutor
    {
        public ConcurrentQueue<SkillScriptExecutionRequest> Requests { get; } = new();

        public ValueTask<SkillScriptExecutionResult> ExecuteAsync(
            SkillScriptExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);

            return ValueTask.FromResult(new SkillScriptExecutionResult { ExitCode = 0, StandardOutput = "ran" });
        }
    }
}

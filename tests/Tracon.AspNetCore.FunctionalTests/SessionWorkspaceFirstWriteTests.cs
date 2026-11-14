using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

// MAAI001: AgentFileStore is marked "evaluation purposes only" - the same rationale as AgentDefinitionCompiler.Memory.cs.
#pragma warning disable MAAI001

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The first file an agent keeps in a session gives the session its record, so a run that ends in a failure leaves nothing
/// behind that a delete, the retention policy or an erasure cannot reach.
/// </summary>
/// <remarks>
/// <para>
/// The record of a session is written when a run ends, and a run that fails writes none. A file the run kept before it failed
/// stayed under a session that did not exist: the delete (<c>404</c>), the retention policy and the erasure of a data subject
/// all begin at the record. The write that needs no approval is the way in: a code policy or a rule the user once kept lets
/// the first run write; every other write stops at an approval, and a run that stops for one has already written its record.
/// </para>
/// <para>
/// The model writes a file and then the upstream fails, as it does in a real outage. Every case runs over SQLite, where the
/// files and the sessions are rows of one database and what is left behind is a count.
/// </para>
/// </remarks>
public sealed class SessionWorkspaceFirstWriteTests
{
    private const string ProviderName = "first-write-model";
    private const string Writer = "writer-model";
    private const string Router = "router-model";

    [Fact]
    public async Task A_run_that_fails_after_it_wrote_a_file_leaves_a_session_the_files_go_with()
    {
        using var database = new TempSqliteDatabase("first-write");
        var provider = new WriteThenOutageProvider();
        await using var host = await StartAsync(database, provider, ownership: null);

        using var failed = await RunAsync(host, "drafter", "session-1");

        await ShouldHaveFailedAsync(failed);
        provider.Calls.ShouldBe(2, "the model asked for the write, and its next call failed");
        (await ReadFileAsync(host, "session-1", "drafter")).ShouldBe("- first", "the write was approved by policy and ran");

        using var session = await host.Client.GetAsync(new Uri("/tracon/api/sessions/session-1", UriKind.Relative));
        session.StatusCode.ShouldBe(HttpStatusCode.OK, "the first file gave the session its record, though the run failed");

        using var deleted = await host.Client.DeleteAsync(new Uri("/tracon/api/sessions/session-1", UriKind.Relative));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var tree = (IAgentFileTree)host.Services.GetRequiredService<AgentFileStore>();
        (await tree.MeasureAsync("default", SessionWorkspacePaths.SessionRoot("default", "session-1"), CancellationToken.None)).Files.ShouldBe(0);
    }

    [Fact]
    public async Task A_sub_agent_that_wrote_a_file_gives_the_session_of_its_root_the_record_though_the_root_fails()
    {
        using var database = new TempSqliteDatabase("first-write-child");
        var provider = new WriteThenOutageProvider();
        await using var host = await StartAsync(database, provider, ownership: null);

        using var failed = await RunAsync(host, "router", "session-1");

        await ShouldHaveFailedAsync(failed);
        (await ReadFileAsync(host, "session-1", "drafter")).ShouldBe("- first", "the sub-agent kept its file under its root's session");

        using var session = await host.Client.GetAsync(new Uri("/tracon/api/sessions/session-1", UriKind.Relative));
        session.StatusCode.ShouldBe(HttpStatusCode.OK, "a sub-agent has no session of its own: the root's is the one that needs the record");

        using var deleted = await host.Client.DeleteAsync(new Uri("/tracon/api/sessions/session-1", UriKind.Relative));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var tree = (IAgentFileTree)host.Services.GetRequiredService<AgentFileStore>();
        (await tree.MeasureAsync("default", SessionWorkspacePaths.SessionRoot("default", "session-1"), CancellationToken.None)).Files.ShouldBe(0);
    }

    [Fact]
    public async Task With_session_ownership_on_the_first_file_gives_the_session_to_its_user_and_another_user_is_refused_before_the_model()
    {
        using var database = new TempSqliteDatabase("first-write-owner");
        var provider = new WriteThenOutageProvider();
        var identity = new SettableAttribution { UserId = "user-1" };
        await using var host = await StartAsync(database, provider, ownership: identity);

        using var failed = await RunAsync(host, "drafter", "session-1");

        await ShouldHaveFailedAsync(failed);

        var record = (await host.Services.GetRequiredService<ISessionStore>().GetAsync("session-1")).ShouldNotBeNull();

        record.OwnerId.ShouldBe("user-1", "the record the first file wrote is claimed by the user who ran, as the run's own save would have");

        var calls = provider.Calls;
        identity.UserId = "user-2";

        using var refused = await RunAsync(host, "drafter", "session-1");

        refused.StatusCode.ShouldBeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        provider.Calls.ShouldBe(calls, "the second user never reached the model, and never saw the first user's files");
        (await ReadFileAsync(host, "session-1", "drafter")).ShouldBe("- first");
    }

    /// <summary>The run endpoint streams: a run that failed is an <c>error</c> frame in a <c>200</c> answer.</summary>
    private static async Task ShouldHaveFailedAsync(HttpResponseMessage response)
        => (await response.Content.ReadAsStringAsync()).ShouldContain("event: error", Case.Sensitive, "the upstream failed after the tool ran, so the run failed");

    private static Task<HttpResponseMessage> RunAsync(TraconTestHost host, string agent, string sessionId)
        => host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/agents/{agent}/run", UriKind.Relative),
            new AgentRunRequest { Message = "work", SessionId = sessionId });

    /// <summary>Reads a file straight from the host's store; the SQL store wants the scope of a run to name the agent.</summary>
    private static async Task<string?> ReadFileAsync(TraconTestHost host, string sessionId, string agent)
    {
        TraconRunContext.SetCurrent(new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), AgentName = agent });

        try
        {
            return await host.Services.GetRequiredService<AgentFileStore>()
                .ReadAsync(SessionWorkspacePaths.AgentRoot("default", sessionId, agent) + "/notes.md");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    private static Task<TraconTestHost> StartAsync(TempSqliteDatabase database, WriteThenOutageProvider provider, SettableAttribution? ownership)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseSqlite(database.ConnectionString);
                builder.AddModelProvider(provider);

                // The write that asks for no approval: the way a first run writes a file before any run has stopped for one.
                builder.AddToolApprovalPolicy("file_access_write", static _ => ToolApprovalPolicyDecision.NotRequired);

                builder.AddAgent(new AgentDefinition
                {
                    Name = "drafter",
                    Instructions = "Work with files.",
                    Model = new ModelBinding { Provider = ProviderName, Model = Writer },
                    Workspace = new WorkspaceSettings { Enabled = true },
                    Origin = AgentDefinitionOrigin.Code,
                });
                builder.AddAgent(new AgentDefinition
                {
                    Name = "router",
                    Instructions = "Delegate.",
                    Model = new ModelBinding { Provider = ProviderName, Model = Router },
                    CallableAgents = ["drafter"],
                    SubAgents = new SubAgentSettings { CallMode = SubAgentCallMode.Tool },
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: services =>
            {
                if (ownership is not null)
                {
                    services.Configure<TraconSessionOwnershipOptions>(static options =>
                    {
                        options.Enabled = true;
                        options.RequireAuthenticatedOwner = true;
                    });
                    services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(ownership));
                }
            });

    /// <summary>
    /// A provider whose model writes <c>notes.md</c> (or, for the router, calls the drafter) on its first call and fails on the
    /// next one, the way an upstream that goes down in the middle of a run does.
    /// </summary>
    private sealed class WriteThenOutageProvider : IModelProvider
    {
        private readonly Lock _gate = new();

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } =
        [
            new ModelDescriptor { Name = Writer },
            new ModelDescriptor { Name = Router },
        ];

        public int Calls { get; private set; }

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(this, binding.Model);

        private sealed class Client(WriteThenOutageProvider provider, string model) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Next(messages))));

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                await Task.Yield();

                yield return new ChatResponseUpdate(ChatRole.Assistant, Next(messages));
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
                // The fake client has no resources to release.
            }

            private List<AIContent> Next(IEnumerable<ChatMessage> messages)
            {
                lock (provider._gate)
                {
                    provider.Calls++;
                }

                var answered = messages.Any(static message => message.Contents.OfType<FunctionResultContent>().Any());

                if (answered)
                {
                    throw new FakeUpstreamOutageException("The provider failed to resolve DNS.");
                }

                return string.Equals(model, Router, StringComparison.Ordinal)
                    ? [new FunctionCallContent(Guid.NewGuid().ToString("N"), "drafter", new Dictionary<string, object?>(StringComparer.Ordinal) { ["query"] = "write the notes" })]
                    : [new FunctionCallContent(
                        Guid.NewGuid().ToString("N"),
                        "file_access_write",
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["fileName"] = "notes.md", ["content"] = "- first" })];
            }
        }
    }
}

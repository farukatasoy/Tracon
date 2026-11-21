using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A turn that is canceled before it finishes writes nothing to the session's history by default. A host can keep it:
/// the streaming endpoints do when <c>Tracon:Sessions:KeepCanceledTurns</c> is on and the model had started to answer,
/// and a host that runs agents from its own code calls <see cref="AgentSessionManager.RecordCanceledTurnAsync"/>.
/// </summary>
/// <remarks>
/// These sit at the functional level because the claim crosses boundaries a unit test does not have: a client that
/// closes a real response, the run that ends canceled, and a history that a later read finds in the store.
/// </remarks>
public sealed class CanceledTurnHistoryTests
{
    private const string AgentName = "cancel-agent";
    private const string SessionId = "cancel-session";
    private const string Partial = "The answer so far";

    [Fact]
    public async Task A_canceled_turn_is_kept_with_its_partial_answer_when_the_host_asked_for_it()
    {
        var model = new StreamThenHangModelProvider("stream-hang", Partial);

        await using var host = await StartAsync(model, keep: true);

        await CancelStreamingTurnAsync(host, model, "what is the tide?", SessionId);

        var kept = await WaitUntil.ValueAsync(
            async () => await ReadAsync(host, SessionId),
            static messages => messages is { Count: > 0 },
            "the canceled turn to be written to the session's history");

        kept!.Select(static message => (message.Role, message.Text)).ShouldBe(
            [
                (ChatRole.User, "what is the tide?"),
                (ChatRole.Assistant, Partial + "\n\n" + AgentSessionManager.CanceledAnswerNote),
            ]);
    }

    [Fact]
    public async Task The_next_turn_of_a_kept_session_sees_the_canceled_turn()
    {
        var model = new StreamThenHangModelProvider("stream-hang", Partial);

        await using var host = await StartAsync(model, keep: true);

        await CancelStreamingTurnAsync(host, model, "remember ALPHA", SessionId);

        await WaitUntil.ValueAsync(
            async () => await ReadAsync(host, SessionId),
            static messages => messages is { Count: > 0 },
            "the canceled turn to be written to the session's history");

        var session = await host.Services.GetRequiredService<AgentSessionManager>().GetOrCreateSessionAsync(
            (await ResolveAsync(host)),
            SessionId,
            TestContext.Current.CancellationToken);

        session.ShouldNotBeNull();

        (await ReadAsync(host, SessionId))!.Select(static message => message.Text)
            .ShouldContain("remember ALPHA", StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_canceled_turn_leaves_nothing_in_the_history_when_the_host_did_not_ask_for_it()
    {
        var model = new StreamThenHangModelProvider("stream-hang", Partial);

        await using var host = await StartAsync(model, keep: false);

        await CancelStreamingTurnAsync(host, model, "forget this", SessionId);
        await WaitForCanceledRunAsync(host, SessionId);

        (await ReadAsync(host, SessionId)).ShouldBeNull("no turn ended, so no session was ever saved");
    }

    [Fact]
    public async Task A_turn_canceled_before_the_model_began_to_answer_writes_nothing_even_when_the_host_asked_for_it()
    {
        var model = new HangingModelProvider("hang-first", respectsCancellation: true);

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(model)
                .AddAgent(Definition("hang-first")),
            configureServices: static services => services.Configure<TraconSessionOptions>(static options => options.KeepCanceledTurns = true));

        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "too early", SessionId = SessionId }),
        };

        var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        await model.Started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        response.Dispose();

        await WaitForCanceledRunAsync(host, SessionId);

        (await ReadAsync(host, SessionId)).ShouldBeNull("the content guards have not necessarily screened a message the model never began to answer");
    }

    [Fact]
    public async Task A_host_that_runs_agents_from_its_own_code_keeps_a_canceled_turn_with_the_manager()
    {
        var model = new StreamThenHangModelProvider("stream-hang", Partial);

        await using var host = await StartAsync(model, keep: false);

        var manager = host.Services.GetRequiredService<AgentSessionManager>();
        var agent = await ResolveAsync(host);
        var session = await manager.GetOrCreateSessionAsync(agent, "embedded-1", TestContext.Current.CancellationToken);

        await manager.RecordCanceledTurnAsync(
            agent,
            session,
            [new ChatMessage(ChatRole.User, "embedded question")],
            partialAnswer: null,
            TestContext.Current.CancellationToken);

        (await ReadAsync(host, "embedded-1"))!.Select(static message => (message.Role, message.Text))
            .ShouldBe([(ChatRole.User, "embedded question")], "no answer had started, so only the question is kept");

        var record = await host.Services.GetRequiredService<ISessionStore>().GetAsync("embedded-1", TestContext.Current.CancellationToken);

        record.ShouldNotBeNull().Version.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task The_option_is_off_by_default_and_binds_from_configuration()
    {
        new TraconSessionOptions().KeepCanceledTurns.ShouldBeFalse();

        var model = new StreamThenHangModelProvider("stream-hang", Partial);

        await using var host = await TraconTestHost.StartAsync(
            builder => builder.AddModelProvider(model).AddAgent(Definition("stream-hang")),
            configureServices: static services => services.AddOptions<TraconSessionOptions>());

        host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<TraconSessionOptions>>()
            .CurrentValue.KeepCanceledTurns.ShouldBeFalse();
    }

    private static readonly Uri RunUri = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    private static AgentDefinition Definition(string provider)
        => new()
        {
            Name = AgentName,
            Instructions = "Give a short answer.",
            Model = new ModelBinding { Provider = provider, Model = string.Equals(provider, "stream-hang", StringComparison.Ordinal) ? "stream-hang-1" : "hanging-1" },
        };

    private static Task<TraconTestHost> StartAsync(StreamThenHangModelProvider model, bool keep)
        => TraconTestHost.StartAsync(
            builder => builder.AddModelProvider(model).AddAgent(Definition("stream-hang")),
            configureServices: services => services.Configure<TraconSessionOptions>(options => options.KeepCanceledTurns = keep));

    private static async Task CancelStreamingTurnAsync(TraconTestHost host, StreamThenHangModelProvider model, string message, string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, RunUri)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = message, SessionId = sessionId }),
        };

        var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        await model.ChunkSent.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // The first piece is in the stream the client reads. Reading it proves the server handled the update before the
        // client goes away; closing the response is the client going away.
        using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken)))
        {
            string? line;

            do
            {
                line = await reader.ReadLineAsync(TestContext.Current.CancellationToken);
            }
            while (line is not null && !line.Contains(Partial, StringComparison.Ordinal));

            line.ShouldNotBeNull("the partial answer reached the client before it left");
        }

        response.Dispose();
    }

    private static async Task WaitForCanceledRunAsync(TraconTestHost host, string sessionId)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        await WaitUntil.TrueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = sessionId }, TestContext.Current.CancellationToken))
                .Any(static run => run.Status == RunStatus.Canceled),
            "the run of the client that left to be recorded as canceled");
    }

    private static async Task<AIAgent> ResolveAsync(TraconTestHost host)
        => (await host.Services.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(AgentName, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

    private static async Task<IReadOnlyList<ChatMessage>?> ReadAsync(TraconTestHost host, string sessionId)
        => await host.Services.GetRequiredService<AgentSessionManager>()
            .ReadHistoryAsync(await ResolveAsync(host), sessionId, cancellationToken: TestContext.Current.CancellationToken);
}

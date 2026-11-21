using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A canceled turn that is kept holds what the user typed, and nothing else. A turn that answers a tool approval sends the
/// answer as a user message too, and that answer belongs to the call of the earlier turn, not to this one.
/// </summary>
/// <remarks>
/// A history that holds the answer without the call and result it answers would hand the next turn an approved call that
/// has already run, which the tool loop can run again.
/// </remarks>
public sealed class CanceledTurnApprovalTests
{
    private const string AgentName = "approval-cancel-agent";
    private const string SessionId = "approval-cancel-session";
    private const string Partial = "Working on it";

    [Fact]
    public async Task A_canceled_turn_that_answered_an_approval_does_not_write_the_answer_into_the_history()
    {
        var executions = 0;
        var model = new ApprovalThenStreamModelProvider("approval-stream", Partial);

        await using var host = await TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(model)
                .AddTool(
                    (Func<string, Task<string>>)(orderId =>
                    {
                        Interlocked.Increment(ref executions);

                        return Task.FromResult($"{orderId} canceled.");
                    }),
                    name: "cancel_order",
                    description: "Cancels an order.",
                    configure: static options => options.RequiresApproval = true)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = "approval-stream", Model = "approval-stream-1" },
                    ToolNames = ["cancel_order"],
                }),
            configureServices: static services => services.Configure<TraconSessionOptions>(static options => options.KeepCanceledTurns = true));

        var runUri = new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

        using var first = await host.Client.PostAsJsonAsync(
            runUri,
            new AgentRunRequest { Message = "cancel order ORD-7", SessionId = SessionId },
            TestContext.Current.CancellationToken);

        var requestId = await ReadApprovalRequestIdAsync(first);

        // The user approves, the tool runs, the model starts to answer, and the client goes away.
        using var answer = new HttpRequestMessage(HttpMethod.Post, runUri)
        {
            Content = JsonContent.Create(new AgentRunRequest
            {
                SessionId = SessionId,
                Approvals = [new ToolApprovalDecision { RequestId = requestId, Approved = true }],
            }),
        };

        var response = await host.Client.SendAsync(answer, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        await model.PartialSent.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

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

        var runs = host.Services.GetRequiredService<IRunStore>();

        await WaitUntil.TrueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId }, TestContext.Current.CancellationToken))
                .Any(static run => run.Status == RunStatus.Canceled),
            "the run of the client that left to be recorded as canceled");

        executions.ShouldBe(1, "the approved tool ran once, in the turn that was canceled");

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
            .ResolveAsync(AgentName, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

        var history = (await host.Services.GetRequiredService<AgentSessionManager>()
            .ReadHistoryAsync(agent, SessionId, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

        history
            .Where(static message => message.Role == ChatRole.User)
            .SelectMany(static message => message.Contents)
            .OfType<ToolApprovalResponseContent>()
            .ShouldBeEmpty("an approval answer written without the call and result it answers is an orphan");

        history.SelectMany(static message => message.Contents).OfType<TextContent>()
            .Select(static content => content.Text)
            .ShouldNotContain(Partial + "\n\n" + AgentSessionManager.CanceledAnswerNote, StringComparer.Ordinal, "a turn with nothing typed in it is not kept");
    }

    private static async Task<string> ReadApprovalRequestIdAsync(HttpResponseMessage response)
    {
        string? id = null;

        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken)))
        {
            if (!string.Equals(frame.Event, "approvals", StringComparison.Ordinal))
            {
                continue;
            }

            id = JsonDocument.Parse(frame.Data).RootElement.EnumerateArray()
                .Select(static request => request.GetProperty("requestId").GetString())
                .FirstOrDefault();
            break;
        }

        id.ShouldNotBeNullOrEmpty("The run did not return a pending approval request to answer.");

        return id!;
    }

    /// <summary>A model that asks for a tool, and after the tool's result streams a piece of text and waits.</summary>
    private sealed class ApprovalThenStreamModelProvider(string name, string partial) : IModelProvider
    {
        private readonly TaskCompletionSource _partialSent = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "approval-stream-1" }];

        public Task PartialSent => _partialSent.Task;

        public IChatClient CreateChatClient(ModelBinding binding) => new Client(_partialSent, partial);

        private sealed class Client(TaskCompletionSource partialSent, string partial) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException("This provider only streams.");

            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
                IEnumerable<ChatMessage> messages,
                ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                var sawResult = messages.SelectMany(static message => message.Contents).OfType<FunctionResultContent>().Any();

                if (!sawResult)
                {
                    yield return new ChatResponseUpdate(
                        ChatRole.Assistant,
                        [new FunctionCallContent("call_cancel_1", "cancel_order", new Dictionary<string, object?>(StringComparer.Ordinal) { ["orderId"] = "ORD-7" })]);

                    yield break;
                }

                yield return new ChatResponseUpdate(ChatRole.Assistant, partial);

                partialSent.TrySetResult();

                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;

            public void Dispose()
            {
            }
        }
    }
}

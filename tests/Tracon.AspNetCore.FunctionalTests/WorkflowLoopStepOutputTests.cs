using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What the step after a harness-loop agent receives in a workflow: the final
/// iteration's answer, never the earlier drafts or the loop's feedback.
/// </summary>
/// <remarks>
/// A workflow runs its agent steps through the streaming path, and a streaming
/// loop yields every iteration. The next node is a caller that reads the answer,
/// not a person watching it form, so a draft that reaches it is a draft the run
/// acts on: a text-to-speech node would speak the rejected version.
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only"
#pragma warning disable MAAI001
public sealed class WorkflowLoopStepOutputTests
{
    private const string WriterProvider = "writing";
    private const string WriterModel = "writer-model";
    private const string WriterName = "writer";
    private const string ListenerProvider = "listening";
    private const string ListenerModel = "listener-model";
    private const string ListenerName = "listener";
    private const string Draft = "first draft";
    private const string Final = "final answer ALL DONE";

    [Fact]
    public async Task A_function_node_after_a_loop_agent_receives_only_the_final_iteration()
    {
        List<AIChatMessage>? received = null;
        var writer = Provider(WriterProvider, WriterModel, Draft, Final);

        await using var host = await StartAsync(
            writer,
            listener: null,
            configure: builder => builder
                .UseWorkflows()
                .AddWorkflowFunction<List<AIChatMessage>, List<AIChatMessage>>(
                    "capture",
                    _ => (messages, _, _) =>
                    {
                        received = messages;

                        return new ValueTask<List<AIChatMessage>>(messages);
                    }));

        await SaveAsync(host, new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = WriterName, Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "capture", Kind = WorkflowNodeKind.Function },
            ],
        });

        await RunAsync(host);

        writer.Requests.Count.ShouldBe(2, "the loop ran a second iteration after the draft");
        Texts(received.ShouldNotBeNull()).ShouldBe([Final]);
    }

    [Fact]
    public async Task The_next_agent_of_a_sequential_chain_does_not_see_a_loop_agents_drafts()
    {
        var writer = Provider(WriterProvider, WriterModel, Draft, Final);
        var listener = Provider(ListenerProvider, ListenerModel, "heard it");

        await using var host = await StartAsync(
            writer,
            listener,
            configure: builder => builder.UseWorkflows());

        await SaveAsync(host, new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            AgentNames = [WriterName, ListenerName],
        });

        await RunAsync(host);

        writer.Requests.Count.ShouldBe(2, "the loop ran a second iteration after the draft");

        var heard = Texts(listener.Requests.ShouldHaveSingleItem().Messages);
        heard.ShouldContain(static text => text.Contains(Final, StringComparison.Ordinal));
        heard.ShouldNotContain(static text => text.Contains(Draft, StringComparison.Ordinal));
    }

    private static List<string> Texts(IEnumerable<AIChatMessage> messages)
        => [.. messages.Select(static message => message.Text).Where(static text => !string.IsNullOrWhiteSpace(text))];

    private static async Task SaveAsync(TraconTestHost host, WorkflowSaveRequest request)
    {
        using var saved = await host.Client.PutAsJsonAsync("/tracon/api/workflows/episode", request);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    private static async Task RunAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            "/tracon/api/workflows/episode/run",
            new WorkflowRunHttpRequest { Message = "write the episode" });

        _ = await response.Content.ReadAsStringAsync();
    }

    private static FakeModelProvider Provider(string name, string model, params string[] responses)
    {
        var provider = new FakeModelProvider(name);
        provider.ForModel(model, cfg => cfg.RespondsWith(responses));

        return provider;
    }

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider writer,
        FakeModelProvider? listener,
        Action<ITraconBuilder> configure)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(writer);
                configure(builder);

                builder.AddAgent(new AgentDefinition
                {
                    Name = WriterName,
                    Description = "Writes until the draft is done.",
                    Instructions = "Write.",
                    Model = new ModelBinding { Provider = WriterProvider, Model = WriterModel },
                    Harness = new HarnessSettings
                    {
                        Loop = new LoopSettings
                        {
                            Criteria = [new LoopCriterion { Kind = "completionMarker", Marker = "ALL DONE" }],
                            MaxIterations = 3,
                        },
                    },
                    Origin = AgentDefinitionOrigin.Code,
                });

                if (listener is not null)
                {
                    builder.AddModelProvider(listener);
                    builder.AddAgent(new AgentDefinition
                    {
                        Name = ListenerName,
                        Description = "Reads what the writer handed over.",
                        Instructions = "Listen.",
                        Model = new ModelBinding { Provider = ListenerProvider, Model = ListenerModel },
                        Origin = AgentDefinitionOrigin.Code,
                    });
                }
            });
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// The package's Tracon.Testing.TraconTestHost and this project's own host share a name,
// so the package is imported one type at a time.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A code-defined approval policy decides for every call of its tool, not only for a tool that was flagged
/// <c>RequiresApproval</c>.
/// </summary>
/// <remarks>
/// The policy used to be the input of the framework's automatic approval rules, and those rules are asked only about calls that
/// are already waiting for approval. A tool registered without the flag never waited, so a policy that said
/// <see cref="ToolApprovalPolicyDecision.Required"/> for it changed nothing and the body ran. The tests drive a whole run through
/// the host: the boundary is the model loop, the framework's approval decorator and the inbox, which no unit test crosses.
/// </remarks>
public sealed class ToolApprovalPolicyRunTests
{
    private const string AgentName = "policy-agent";
    private const string ToolName = "refund_order";
    private const string Model = "policy-model";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly Uri PendingApprovals = new("/tracon/api/approvals/pending", UriKind.Relative);
    private static readonly Uri Tools = new("/tracon/api/tools", UriKind.Relative);

    [Fact]
    public async Task A_tool_without_the_flag_waits_for_approval_when_its_policy_says_Required()
    {
        var calls = new CallCounter();

        await using var host = await StartAsync(calls, static _ => ToolApprovalPolicyDecision.Required);

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        // The body is the thing the policy exists to hold back.
        calls.Value.ShouldBe(0);

        using var pending = await host.Client.GetAsync(PendingApprovals);
        var request = (await TraconTestHost.ReadJsonAsync(pending)).EnumerateArray().ShouldHaveSingleItem();

        request.GetProperty("toolName").GetString().ShouldBe(ToolName);
    }

    [Theory]
    [InlineData(ToolApprovalPolicyDecision.NotRequired)]
    [InlineData(ToolApprovalPolicyDecision.Undecided)]
    public async Task A_tool_without_the_flag_runs_without_asking_when_its_policy_does_not_say_Required(ToolApprovalPolicyDecision decision)
    {
        var calls = new CallCounter();

        await using var host = await StartAsync(calls, _ => decision);

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");

        calls.Value.ShouldBe(1);

        using var pending = await host.Client.GetAsync(PendingApprovals);
        (await TraconTestHost.ReadJsonAsync(pending)).EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_policy_that_throws_makes_a_tool_without_the_flag_wait_for_approval()
    {
        var calls = new CallCounter();

        await using var host = await StartAsync(calls, static _ => throw new InvalidOperationException("The policy is broken."));

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

        // A broken policy does not release a tool from approval.
        calls.Value.ShouldBe(0);
    }

    [Fact]
    public async Task A_tool_without_the_flag_and_without_a_policy_runs_as_before()
    {
        var calls = new CallCounter();

        await using var host = await StartAsync(calls, policy: null);

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");

        calls.Value.ShouldBe(1);
    }

    [Fact]
    public async Task A_flagged_tool_keeps_its_rule_that_a_NotRequired_policy_lets_it_run()
    {
        var calls = new CallCounter();

        await using var host = await StartAsync(calls, static _ => ToolApprovalPolicyDecision.NotRequired, flagged: true);

        var runId = await QueueAsync(host);

        (await host.WaitForRunStatusAsync(runId, "Completed")).ShouldBe("Completed");

        calls.Value.ShouldBe(1);
    }

    [Fact]
    public async Task A_tool_with_a_policy_is_listed_as_asking_for_approval_and_one_without_is_not()
    {
        var calls = new CallCounter();

        await using var host = await StartAsync(
            calls,
            static _ => ToolApprovalPolicyDecision.NotRequired,
            configure: tracon => tracon.AddTool(
                (Func<string>)(static () => "ok"),
                name: "plain_tool",
                description: "A tool with no policy."));

        using var response = await host.Client.GetAsync(Tools);
        var tools = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();

        // Fail-closed: the descriptor is read by the surfaces that cannot ask a person (MCP, A2A, replay).
        tools.Single(tool => string.Equals(tool.GetProperty("name").GetString(), ToolName, StringComparison.Ordinal))
            .GetProperty("requiresApproval").GetBoolean().ShouldBeTrue();
        tools.Single(tool => string.Equals(tool.GetProperty("name").GetString(), "plain_tool", StringComparison.Ordinal))
            .GetProperty("requiresApproval").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_policy_for_a_hosted_tool_stops_the_host_and_names_the_tool()
    {
        var exception = await Should.ThrowAsync<TraconException>(() => TraconTestHost.StartAsync(builder => builder
            .AddHostedTool("web_lookup", HostedToolKind.WebSearch, static _ => new HostedWebSearchTool())
            .AddToolApprovalPolicy("web_lookup", static _ => ToolApprovalPolicyDecision.Required)));

        exception.Message.ShouldContain("'web_lookup'");
        exception.Message.ShouldContain("hosted tool");
    }

    [Fact]
    public async Task A_policy_for_a_name_no_registered_tool_carries_does_not_stop_the_host()
    {
        // A tool of an MCP server of one tenant, or a tool the run adds, is not in the registry when the host starts.
        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddToolApprovalPolicy("a_tool_that_appears_later", static _ => ToolApprovalPolicyDecision.Required));

        using var response = await host.Client.GetAsync(Tools);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<TraconTestHost> StartAsync(
        CallCounter calls,
        Func<ToolApprovalContext, ToolApprovalPolicyDecision>? policy,
        bool flagged = false,
        Action<ITraconBuilder>? configure = null)
        => TraconTestHost.StartAsync(tracon =>
        {
            tracon
                .AddModelProvider(new FakeModelProvider(Model)
                    .CallsTool(ToolName, new { orderId = "ORD-7" })
                    .EchoesLastToolResult())
                .AddTool(
                    (Func<string, string>)(orderId =>
                    {
                        calls.Increment();

                        return $"{orderId} refunded.";
                    }),
                    name: ToolName,
                    description: "Refunds an order.",
                    configure: options => options.RequiresApproval = flagged)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = Model, Model = "policy-1" },
                    ToolNames = [ToolName],
                });

            if (policy is not null)
            {
                tracon.AddToolApprovalPolicy(ToolName, policy);
            }

            configure?.Invoke(tracon);
            tracon.UseScheduling(options => options.PollInterval = TimeSpan.FromMilliseconds(20));
        });

    private static async Task<Guid> QueueAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "refund the order", SessionId = "policy-session" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    /// <summary>Counts how often the tool's body ran; one per test, so parallel tests do not share it.</summary>
    private sealed class CallCounter
    {
        private int _value;

        public int Value => Volatile.Read(ref _value);

        public void Increment() => Interlocked.Increment(ref _value);
    }
}

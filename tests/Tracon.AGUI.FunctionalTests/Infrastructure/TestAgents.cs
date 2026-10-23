using System.Runtime.CompilerServices;
using System.Text.Json;
using Tracon.Testing;

namespace Tracon.AGUI.FunctionalTests.Infrastructure;

/// <summary>The agents the tests run, and the model scripts behind them.</summary>
internal static class TestAgents
{
    public const string Echo = "echo-agent";
    public const string ClientTool = "client-tool-agent";
    public const string Approval = "approval-agent";

    public const string OpenPage = "open_page";
    public const string CancelOrder = "cancel_order";

    /// <summary>The schema the CODE declares for <see cref="OpenPage"/>; a request's own schema must never replace it.</summary>
    public const string OpenPageSchema = """{"type":"object","properties":{"url":{"type":"string"}},"required":["url"]}""";

    public static AgentDefinition Definition(string name, string provider, params string[] toolNames)
        => new()
        {
            Name = name,
            Instructions = "Answer briefly.",
            Model = new ModelBinding { Provider = provider, Model = provider + "-1" },
            ToolNames = toolNames,
        };

    /// <summary>An agent that echoes the user's message, on the host's default model.</summary>
    public static void AddEcho(ITraconBuilder builder)
        => builder.AddAgent(Definition(Echo, "echo"));

    /// <summary>An agent with a client-side tool: the model calls it, the front end runs it, the result is echoed.</summary>
    public static FakeModelProvider AddClientTool(ITraconBuilder builder, string agentName = ClientTool)
    {
        var model = new FakeModelProvider("client-model").CallsTool(OpenPage, new { url = "https://example.test" }).EchoesLastToolResult("Opened: ");

        builder
            .AddModelProvider(model)
            .AddClientTool(OpenPage, "Opens a page in the user's browser.", JsonDocument.Parse(OpenPageSchema).RootElement.Clone())
            .AddAgent(Definition(agentName, "client-model", OpenPage));

        return model;
    }

    /// <summary>An agent whose tool waits for approval; <paramref name="executions"/> counts how often the tool really ran.</summary>
    public static FakeModelProvider AddApprovalTool(
        ITraconBuilder builder,
        StrongBox<int> executions,
        string agentName = Approval,
        bool callsTwice = false)
    {
        var model = new FakeModelProvider("approval-model").CallsTool(CancelOrder, new { orderId = "ORD-7" });

        if (callsTwice)
        {
            // The script is one queue for the whole provider: the second run of a test needs its own call, and the turn
            // between them needs an answer that is not a call.
            model.RespondsWith("first order handled").CallsTool(CancelOrder, new { orderId = "ORD-8" });
        }

        model.EchoesLastToolResult();

        builder
            .AddModelProvider(model)
            .AddTool(
                (Func<string, string>)(orderId =>
                {
                    Interlocked.Increment(ref executions.Value);

                    return $"{orderId} canceled.";
                }),
                name: CancelOrder,
                description: "Cancels an order.",
                configure: options => options.RequiresApproval = true)
            .AddAgent(Definition(agentName, "approval-model", CancelOrder));

        return model;
    }
}

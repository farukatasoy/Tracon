using System.Text.Json;
using AGUI.Abstractions;

namespace Tracon.AGUI.FunctionalTests.Infrastructure;

/// <summary>Builds AG-UI requests the way a front end does.</summary>
internal static class AGUIInputs
{
    public const string EmptySchema = """{"type":"object","properties":{}}""";

    public static RunAgentInput User(string thread, string text, string run = "run-1", params AGUITool[] tools)
        => new()
        {
            ThreadId = thread,
            RunId = run,
            Messages = [UserMessage("u-" + run, text)],
            Tools = [.. tools],
            Context = [],
        };

    public static AGUIUserMessage UserMessage(string id, string text) => new() { Id = id, Content = new AGUIContent(text) };

    public static AGUIAssistantMessage AssistantMessage(string id, string text, params (string Id, string Name)[] calls)
        => new()
        {
            Id = id,
            Content = text,
            ToolCalls = [.. calls.Select(static call => new AGUIToolCall
            {
                Id = call.Id,
                Type = "function",
                Function = new AGUIToolCallFunction { Name = call.Name, Arguments = "{}" },
            })],
        };

    public static AGUIToolMessage ToolMessage(string id, string callId, string content, string? error = null)
        => new() { Id = id, ToolCallId = callId, Content = new AGUIContent(content), Error = error };

    public static AGUITool Tool(string name, string schema = EmptySchema)
        => new() { Name = name, Description = "declared by the front end", Parameters = JsonDocument.Parse(schema).RootElement.Clone() };

    public static RunAgentInput With(string thread, string run, IEnumerable<AGUIMessage> messages, IEnumerable<AGUITool>? tools = null, IEnumerable<AGUIResume>? resume = null)
        => new()
        {
            ThreadId = thread,
            RunId = run,
            Messages = [.. messages],
            Tools = [.. tools ?? []],
            Context = [],
            Resume = resume is null ? null : [.. resume],
        };

    public static AGUIResume Resolved(string interruptId, object payload)
        => new()
        {
            InterruptId = interruptId,
            Status = ResumeStatus.Resolved,
            Payload = JsonSerializer.SerializeToElement(payload),
        };

    public static AGUIResume Cancelled(string interruptId)
        => new() { InterruptId = interruptId, Status = ResumeStatus.Cancelled };
}

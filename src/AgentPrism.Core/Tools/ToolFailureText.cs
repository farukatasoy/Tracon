namespace AgentPrism;

/// <summary>Prevents arbitrary tool exception messages from entering persistent or streamed output.</summary>
internal static class ToolFailureText
{
    internal static string? Get(Exception? exception) => exception switch
    {
        null => null,
        AgentPrismException => exception.Message,
        _ => $"Tool failed with {exception.GetType().Name}.",
    };
}

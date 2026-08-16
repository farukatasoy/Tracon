namespace AgentPrism.Testing.Internal;

/// <summary>An ordered response queue for a model, and the fallback used once it is drained.</summary>
/// <remarks>
/// The queue is consumed <strong>once</strong>: each call pops the next step in
/// line. Once the queue is empty, every call returns the fallback — it never
/// reads from the queue again. This is a lasting, predictable behavior for the
/// provider's lifetime, unlike the real provider fakes (RoutingModelProvider,
/// ScriptedModelProvider) that try to infer "which tool was already called" by
/// scanning message history — here the state lives in the provider ITSELF.
/// </remarks>
internal sealed class FakeModelScript
{
    private readonly Queue<FakeStep> _queue = new();

    /// <summary>Text produced once the queue is drained. Defaults to a fixed response.</summary>
    public string FallbackText { get; set; } = "fake response";

    /// <summary>The kind of response produced once the queue is drained.</summary>
    public FakeFallbackKind Fallback { get; set; } = FakeFallbackKind.StaticText;

    /// <summary>Prefix prepended to the text for <see cref="FakeFallbackKind.EchoLastToolResult"/>.</summary>
    public string FallbackPrefix { get; set; } = string.Empty;

    /// <summary>Token usage reported alongside the fallback response. Empty in most scenarios.</summary>
    public FakeUsage? FallbackUsage { get; set; }

    /// <summary>Enqueues a step.</summary>
    public void Enqueue(FakeStep step) => _queue.Enqueue(step);

    /// <summary>Pops the next step; <see langword="null"/> if the queue is empty.</summary>
    public FakeStep? Dequeue() => _queue.TryDequeue(out var step) ? step : null;
}

/// <summary>The kind of response produced once the queue is drained.</summary>
internal enum FakeFallbackKind
{
    /// <summary><see cref="FakeModelScript.FallbackText"/> is returned as-is.</summary>
    StaticText,

    /// <summary>The last incoming user message is echoed.</summary>
    EchoUserMessage,

    /// <summary>The last tool result in history is returned (with <see cref="FakeModelScript.FallbackPrefix"/> if set).</summary>
    EchoLastToolResult,
}

/// <summary>A single step in the queue: either plain text or a tool call.</summary>
internal sealed record FakeStep
{
    public string? Text { get; init; }

    public string? ToolName { get; init; }

    public object? ToolArguments { get; init; }

    /// <summary>Token usage reported as soon as this step is returned. Empty in most steps.</summary>
    public FakeUsage? Usage { get; init; }
}

/// <summary>Token usage reported alongside a step.</summary>
internal sealed record FakeUsage(int InputTokens, int OutputTokens);

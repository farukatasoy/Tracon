using AgentPrism.Testing.Internal;
using Microsoft.Extensions.AI;

namespace AgentPrism.Testing;

/// <summary>
/// Configurable, non-networked fake model provider.
/// </summary>
/// <remarks>
/// <para>
/// Five fluent methods (<see cref="RespondsWith(string[])"/>, <see cref="EchoesUserMessage"/>,
/// <see cref="CallsTool"/>, <see cref="ForModel"/>, <see cref="WithModel"/>) cover
/// all the fake provider behavior that is today duplicated across five separate
/// files in AgentPrism's test suites.
/// </para>
/// <para>
/// Each model has its own <strong>ordered response queue</strong> (selected with
/// <see cref="ForModel"/>; the default queue is used when none is selected). A
/// call pops the next step in the queue; once the queue is drained, every
/// subsequent call returns either the echo set with <see cref="EchoesUserMessage"/>
/// or a fixed response. This behavior is lasting for the provider's lifetime —
/// it does not infer "which tool was already called" by scanning message history.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var provider = new FakeModelProvider().
/// CallsTool("get_order_status", new { orderId = "ORD-7" }).
/// EchoesUserMessage();
/// </code>
/// </example>
public sealed class FakeModelProvider : IModelProvider, IDisposable
{
    private readonly Dictionary<string, FakeModelScript> _scripts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ModelDescriptor> _models = [];
    private readonly List<FakeModelRequest> _requests = [];
    private readonly Lock _gate = new();
    private readonly FakeModelScript _default = new();

    private FakeModelScript _current;

    /// <summary>Creates a new fake provider.</summary>
    /// <param name="name">Provider name.</param>
    public FakeModelProvider(string name = "fake")
    {
        Name = name;
        _current = _default;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IReadOnlyList<ModelDescriptor> Models => _models.Count > 0
        ? _models
        : [new ModelDescriptor { Name = "fake-model", ContextWindowTokens = 8_192, MaxOutputTokens = 1_024 }];

    /// <summary>Requests that reached this provider; the newest is last.</summary>
    public IReadOnlyList<FakeModelRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    /// <summary>Registers a model on this provider.</summary>
    /// <param name="descriptor">Model descriptor.</param>
    /// <returns>The chain, for continued configuration.</returns>
    public FakeModelProvider WithModel(ModelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        _models.Add(descriptor);

        return this;
    }

    /// <summary>
    /// Enqueues responses to return in order. Once the queue is drained, a fixed
    /// response is returned unless <see cref="EchoesUserMessage"/> was set.
    /// </summary>
    /// <param name="responses">Texts to return in order.</param>
    /// <returns>The chain, for continued configuration.</returns>
    public FakeModelProvider RespondsWith(params string[] responses)
    {
        ArgumentNullException.ThrowIfNull(responses);

        foreach (var response in responses)
        {
            _current.Enqueue(new FakeStep { Text = response });
        }

        return this;
    }

    /// <summary>
    /// Enqueues a single text response that reports a specific token usage.
    /// </summary>
    /// <param name="response">Text to return.</param>
    /// <param name="inputTokens">Input token count to report.</param>
    /// <param name="outputTokens">Output token count to report.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <remarks>For scenarios that test cost/usage metrics end to end.</remarks>
    public FakeModelProvider RespondsWith(string response, int inputTokens, int outputTokens)
    {
        ArgumentNullException.ThrowIfNull(response);

        _current.Enqueue(new FakeStep
        {
            Text = response,
            Usage = new FakeUsage(inputTokens, outputTokens),
        });

        return this;
    }

    /// <summary>
    /// Starts echoing the last incoming user message once the queue is drained.
    /// </summary>
    /// <returns>The chain, for continued configuration.</returns>
    public FakeModelProvider EchoesUserMessage()
    {
        _current.Fallback = FakeFallbackKind.EchoUserMessage;

        return this;
    }

    /// <summary>
    /// Starts echoing the LAST tool result in history once the queue is drained.
    /// </summary>
    /// <param name="prefix">Text prepended to the result.</param>
    /// <param name="inputTokens">If given, the input token count reported with every response.</param>
    /// <param name="outputTokens">If given, the output token count reported with every response.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <remarks>
    /// For the last step of a tool chain (set up with <see cref="CallsTool"/>),
    /// where the final response must genuinely depend on the result the tool
    /// returned — e.g. a hand-off result to a sub-agent.
    /// </remarks>
    public FakeModelProvider EchoesLastToolResult(string prefix = "", int? inputTokens = null, int? outputTokens = null)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        _current.Fallback = FakeFallbackKind.EchoLastToolResult;
        _current.FallbackPrefix = prefix;
        _current.FallbackUsage = inputTokens is { } input && outputTokens is { } output
            ? new FakeUsage(input, output)
            : null;

        return this;
    }

    /// <summary>Enqueues a tool call.</summary>
    /// <param name="toolName">Name of the tool to call.</param>
    /// <param name="arguments">
    /// Call arguments. If not an <see cref="IDictionary{TKey, TValue}"/>, its
    /// properties are read through reflection (works for anonymous types).
    /// </param>
    /// <returns>The chain, for continued configuration.</returns>
    public FakeModelProvider CallsTool(string toolName, object? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        _current.Enqueue(new FakeStep { ToolName = toolName, ToolArguments = arguments });

        return this;
    }

    /// <summary>
    /// Enqueues a SINGLE turn that calls every given tool at once.
    /// </summary>
    /// <param name="calls">The tool name and arguments for each call.</param>
    /// <returns>The chain, for continued configuration.</returns>
    /// <remarks>
    /// <see cref="CallsTool"/> enqueues one call per turn and can never
    /// produce more than one <c>FunctionCallContent</c> in the same response;
    /// exercising a REAL concurrent tool-call loop
    /// (<c>FunctionInvokingChatClient.AllowConcurrentInvocation</c>) needs
    /// several independent calls to arrive together, in one turn.
    /// </remarks>
    public FakeModelProvider CallsTools(params (string ToolName, object? Arguments)[] calls)
    {
        ArgumentNullException.ThrowIfNull(calls);

        if (calls.Length == 0)
        {
            throw new ArgumentException("At least one call is required.", nameof(calls));
        }

        _current.Enqueue(new FakeStep { ToolCalls = calls });

        return this;
    }

    /// <summary>
    /// Defines a separate response queue for a specific model name. Calls to
    /// <see cref="RespondsWith(string[])"/>/<see cref="EchoesUserMessage"/>/<see cref="CallsTool"/>
    /// inside the <paramref name="configure"/> body affect only that model's queue.
    /// </summary>
    /// <param name="modelId">Model name.</param>
    /// <param name="configure">Configuration that fills this model's queue.</param>
    /// <returns>The chain, for continued configuration.</returns>
    public FakeModelProvider ForModel(string modelId, Action<FakeModelProvider> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(configure);

        if (!_scripts.TryGetValue(modelId, out var script))
        {
            script = new FakeModelScript();
            _scripts[modelId] = script;

            if (!_models.Exists(model => string.Equals(model.Name, modelId, StringComparison.OrdinalIgnoreCase)))
            {
                _models.Add(new ModelDescriptor { Name = modelId });
            }
        }

        var previous = _current;
        _current = script;

        try
        {
            configure(this);
        }
        finally
        {
            _current = previous;
        }

        return this;
    }

    /// <inheritdoc />
    public IChatClient CreateChatClient(ModelBinding binding, ModelProviderCredential? credential = null)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var script = _scripts.TryGetValue(binding.Model, out var forModel) ? forModel : _default;

        // 🚨 Returns the RAW client. The tool-call loop (UseFunctionInvocation)
        // and telemetry moved to ModelProviderRegistry in Phase 48; every
        // IModelProvider now returns a raw client and the pipeline wraps it.
        // Wrapping here too would nest the pipeline inside itself.
        return new FakeChatClient(script, Record);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The fake provider has no resource to release.
    }

    private void Record(FakeModelRequest request)
    {
        lock (_gate)
        {
            _requests.Add(request);
        }
    }
}

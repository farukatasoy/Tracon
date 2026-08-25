using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Provides the built-in, model-backed <see cref="IRunJudge"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// The judge's own call goes through a <see cref="RunRecordingAgent"/> wrapper
/// and is recorded as <see cref="RunKind.Eval"/>, like every case run from
/// <c>EvalJobHandler</c>. This computes cost automatically and excludes the row
/// from <c>RunStatistics</c>, so the evaluated agent's cost does not grow and no
/// new column is needed. The recording decorator deliberately disables quota
/// enforcement with <c>quotaEnforcer: null</c> and event publishing with
/// <c>webhookPublisher: null</c>. A judge is a control-plane cost, does not use
/// the user's quota, and does not publish its own <c>run.completed</c> noise.
/// </para>
/// <para>
/// Structured output uses the
/// <c>ChatResponseFormat.ForJsonSchema(JsonElement, ...)</c> overload. Its
/// manually written schema does not use reflection and preserves the AOT stance
/// of <c>AgentPrism.Core</c>.
/// </para>
/// </remarks>
internal sealed class ModelRunJudge(
    IModelProviderRegistry modelProviders,
    IOptionsMonitor<ModelRunJudgeOptions> optionsMonitor,
    IRunStore runStore,
    ITenantContext tenantContext,
    IOptions<AgentPrismOptions> agentPrismOptions,
    ILoggerFactory loggerFactory,
    AgentPrismMetrics? metrics = null,
    RunTraceCollector? traceCollector = null,
    TimeProvider? timeProvider = null,
    IRunPricingResolver? pricingResolver = null,
    IRunErrorClassifier? errorClassifier = null,
    IRunInputStore? runInputStore = null,
    IServiceProvider? services = null) : IRunJudge
{
    private static readonly JsonElement JudgmentSchema = BuildSchema();

    /// <inheritdoc />
    public string Name => "model";

    /// <inheritdoc />
    public async ValueTask<RunJudgment> JudgeAsync(
        RunJudgeContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var options = optionsMonitor.CurrentValue;

        var chatOptions = new ChatOptions
        {
            Instructions = BuildInstructions(options),
            ModelId = options.Model.Model,
            Temperature = options.Model.Temperature,
            TopP = options.Model.TopP,
            MaxOutputTokens = options.Model.MaxOutputTokens,
            ResponseFormat = ChatResponseFormat.ForJsonSchema(
                JudgmentSchema,
                schemaName: "run_judgment",
                schemaDescription: "The quality score for a run."),
        };

        var judgeAgentId = $"judge:{Name}";

        var chatClient = await modelProviders
            .CreateSetupChatClientAsync(options.Model, cancellationToken)
            .ConfigureAwait(false);

        var innerAgent = chatClient.AsAIAgent(
            new ChatClientAgentOptions
            {
                Id = judgeAgentId,
                Name = judgeAgentId,
                ChatOptions = chatOptions,
            },
            loggerFactory,
            services);

        // This wrapper records the judge's own run. Quota enforcement and webhook
        // publishing are deliberately not supplied; see the class documentation.
        var recordingAgent = new RunRecordingAgent(
            innerAgent: innerAgent,
            runStore: runStore,
            tenantContext: tenantContext,
            options: agentPrismOptions.Value.RunRecording,
            logger: loggerFactory.CreateLogger<RunRecordingAgent>(),
            metrics: metrics,
            traceCollector: traceCollector,
            modelId: options.Model.Model,
            modelProvider: options.Model.Provider,
            timeProvider: timeProvider,
            graphOptions: agentPrismOptions.Value.AgentGraph,
            agentVersion: null,
            includeAgentVersionTag: false,
            pricingResolver: pricingResolver,
            quotaEnforcer: null,
            webhookPublisher: null,
            cancellationRegistry: null,
            errorClassifier: errorClassifier,
            runInputStore: runInputStore);

        var response = await recordingAgent
            .RunAsync(
                BuildTranscript(context),
                session: null,
                options: new AgentPrismRunOptions { RunId = AgentPrismId.NewId(), Kind = RunKind.Eval },
                cancellationToken)
            .ConfigureAwait(false);

        var usage = response.Usage is { } responseUsage
            ? new RunUsage
            {
                InputTokens = responseUsage.InputTokenCount,
                OutputTokens = responseUsage.OutputTokenCount,
                TotalTokens = responseUsage.TotalTokenCount,

                // The judge is a normal model call and hits the same prompt cache:
                // without these the judge's own cost is resolved as if every input
                // token were fresh.
                CachedInputTokens = UsageBreakdown.CachedInputTokens(responseUsage),
                ReasoningTokens = UsageBreakdown.ReasoningTokens(responseUsage),
                AudioInputTokens = UsageBreakdown.AudioInputTokens(responseUsage),
                AudioOutputTokens = UsageBreakdown.AudioOutputTokens(responseUsage),
            }
            : null;

        // 🚨 This is the judge's own cost and is also emitted with the
        // `agentprism.judge.cost` tag. RunRecordingAgent.CompleteAsync has already
        // written it to the general `agentprism.run.cost` counter for the judge's
        // own row. Resolving it again through the same inexpensive in-memory lookup
        // does not inflate the evaluated agent's summary; it adds a judge-specific metric.
        var judgeCost = usage is not null ? pricingResolver?.Resolve(options.Model.Provider, options.Model.Model, usage) : null;

        if (judgeCost is { Source: not PricingSource.Unknown } cost)
        {
            metrics?.RecordJudgeCost(
                Name,
                options.Model.Model,
                tenantContext.TenantId,
                cost.Total() ?? 0m,
                cost.Currency ?? "unknown");
        }

        var (score, reason) = ParseJudgment(response.Text);

        return new RunJudgment { Score = score, Reason = reason };
    }

    private static string BuildInstructions(ModelRunJudgeOptions options)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "You are a judge who evaluates the quality of an AI assistant response. " +
            "The input and output of a run are provided below.");

        if (options.Criteria.Count > 0)
        {
            builder.AppendLine("Evaluation criteria:");

            foreach (var criterion in options.Criteria)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {criterion}");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.Instructions))
        {
            builder.AppendLine(options.Instructions);
        }

        builder.AppendLine(
            "Return only an object that matches the supplied JSON schema. 'score' is an integer from 0 to 100 " +
            "(use null if you cannot decide), and 'reason' is a short rationale.");

        return builder.ToString();
    }

    private static string BuildTranscript(RunJudgeContext context)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Input");

        foreach (var message in context.Input)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"{message.Role}: {message.Text}");
        }

        builder.AppendLine();
        builder.AppendLine("## Output");
        builder.AppendLine(context.Output);

        if (context.ToolNames.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"## Called tools: {string.Join(", ", context.ToolNames)}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Parses the judge JSON response.
    /// </summary>
    /// <remarks>
    /// A response that cannot be parsed does not throw. It returns a
    /// <see langword="null"/> score, and <see cref="OnlineEvalJobHandler"/> writes
    /// no score. It does not silently write <c>0</c>, which would confuse no
    /// measurement with a zero measurement.
    /// </remarks>
    private static (int? Score, string? Reason) ParseJudgment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, "The judge returned an empty response.");
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            int? score = root.TryGetProperty("score", out var scoreElement) &&
                scoreElement.ValueKind == JsonValueKind.Number
                ? scoreElement.GetInt32()
                : null;

            string? reason = root.TryGetProperty("reason", out var reasonElement) &&
                reasonElement.ValueKind == JsonValueKind.String
                ? reasonElement.GetString()
                : null;

            if (score is { } value)
            {
                score = Math.Clamp(value, 0, 100);
            }

            return (score, reason);
        }
        catch (JsonException exception)
        {
            return (null, $"The judge response could not be parsed: {exception.Message}");
        }
    }

    private static JsonElement BuildSchema()
    {
        const string schemaJson = """
            {
              "type": "object",
              "properties": {
                "score": { "type": ["integer", "null"], "minimum": 0, "maximum": 100 },
                "reason": { "type": ["string", "null"] }
              },
              "required": ["score", "reason"],
              "additionalProperties": false
            }
            """;

        using var document = JsonDocument.Parse(schemaJson);
        return document.RootElement.Clone();
    }
}

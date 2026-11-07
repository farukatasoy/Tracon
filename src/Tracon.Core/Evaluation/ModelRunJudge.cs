using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Provides the built-in, model-backed <see cref="IRunJudge"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// The judge's own call goes through a <see cref="RunRecordingAgent"/> wrapper
/// and is recorded as <see cref="RunKind.Eval"/>, like every case run from
/// <c>EvalJobHandler</c>. This computes cost automatically and excludes the row
/// from <c>RunStatistics</c>, so the evaluated agent's cost does not grow and no
/// new column is needed. The recorder is built from
/// <see cref="RunRecordingServices.ForControlPlaneRun"/>, which leaves out quota
/// enforcement and event publishing (and the sampler, the sinks and the
/// guard). A judge is a control-plane cost, does not use
/// the user's quota, and does not publish its own <c>run.completed</c> noise.
/// </para>
/// <para>
/// Structured output uses the
/// <c>ChatResponseFormat.ForJsonSchema(JsonElement, ...)</c> overload. Its
/// manually written schema does not use reflection and preserves the AOT stance
/// of <c>Tracon.Core</c>.
/// </para>
/// </remarks>
internal sealed class ModelRunJudge(
    IModelProviderRegistry modelProviders,
    IOptionsMonitor<ModelRunJudgeOptions> optionsMonitor,
    RunRecordingServices recording,
    ILoggerFactory loggerFactory,
    IServiceProvider? services = null) : IRunJudge
{
    private static readonly JsonElement JudgmentSchema = BuildSchema();

    // The judge's own run is not the user's: no quota, no webhook, no cancellation from outside, no sampling.
    private readonly RunRecordingServices _recording = recording.ForControlPlaneRun();

    /// <inheritdoc />
    public string Name => "model";

    /// <summary>
    /// Stops a judge whose binding sets a sampling field its provider does not send.
    /// </summary>
    /// <remarks>
    /// The judge writes its own request options and never passes through the agent compiler, so the
    /// provider's statement of what its API surface sends (<see cref="ISamplingSupport"/>) is asked here.
    /// Without it a judge on a provider that drops the temperature would run on the provider's default and
    /// nothing would say so; the scores would simply get noisier. Only the two fields the judge sends are checked.
    /// </remarks>
    /// <exception cref="TraconException">The binding sets <see cref="ModelBinding.Temperature"/> or <see cref="ModelBinding.TopP"/> and the provider does not send it.</exception>
    private void RefuseSamplingFieldsTheProviderDrops(ModelBinding model)
    {
        var dropped = SamplingSupportCheck.Find(modelProviders, model)
            .Where(static finding => finding.Field is SamplingFields.Temperature or SamplingFields.TopP)
            .ToList();

        if (dropped.Count > 0)
        {
            throw new TraconException(
                "The model judge cannot use its configured model. " +
                string.Join(" ", dropped.Select(static finding => finding.Describe())));
        }
    }

    /// <inheritdoc />
    public async ValueTask<RunJudgment> JudgeAsync(
        RunJudgeContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var options = optionsMonitor.CurrentValue;

        RefuseSamplingFieldsTheProviderDrops(options.Model);

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
            innerAgent,
            _recording,
            new RecordedAgentIdentity(options.Model.Model, options.Model.Provider, Version: null),
            loggerFactory.CreateLogger<RunRecordingAgent>());

        var response = await recordingAgent
            .RunAsync(
                BuildTranscript(context),
                session: null,
                options: new TraconRunOptions { RunId = TraconId.NewId(), Kind = RunKind.Eval },
                cancellationToken)
            .ConfigureAwait(false);

        // The judge is a normal model call and hits the same prompt cache: the whole breakdown
        // travels with the usage, or the judge's own cost is resolved as if every input token
        // were fresh.
        var usage = response.Usage is { } responseUsage ? UsageBreakdown.ToRunUsage(responseUsage) : null;

        // 🚨 This is the judge's own cost and is also emitted with the
        // `tracon.judge.cost` tag. RunRecordingAgent.CompleteAsync has already
        // written it to the general `tracon.run.cost` counter for the judge's
        // own row. Resolving it again through the same inexpensive in-memory lookup
        // does not inflate the evaluated agent's summary; it adds a judge-specific metric.
        var judgeCost = usage is not null ? _recording.PricingResolver?.Resolve(options.Model.Provider, options.Model.Model, usage) : null;

        if (judgeCost is { Source: not PricingSource.Unknown } cost)
        {
            _recording.Metrics?.RecordJudgeCost(
                Name,
                options.Model.Model,
                _recording.TenantContext.TenantId,
                cost.Total() ?? 0m,
                cost.Currency ?? "unknown");
        }

        var (score, reason) = ParseJudgment(response.Text, loggerFactory.CreateLogger<ModelRunJudge>());

        // The built-in judge reports ONE overall verdict, and it carries the
        // judge's own name. That makes it the headline score: it is the one
        // that feeds the online-evaluation window and the judge-score
        // histogram. No decision means an EMPTY judgment, never a zero.
        return score is { } decided
            ? new RunJudgment
            {
                Scores =
                [
                    new JudgeScore
                    {
                        Name = Name,
                        Kind = RunScoreKind.Numeric,
                        Value = decided,
                        Comment = reason,
                    },
                ],
            }
            : new RunJudgment();
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
    private static (int? Score, string? Reason) ParseJudgment(string text, ILogger logger)
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
            var correlationId = SafeErrorText.NewCorrelationId();

            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The judge response could not be parsed. (ref: {CorrelationId})", correlationId);
            }

            return (null, $"The judge response could not be parsed: {SafeErrorText.ForPersistence(exception, correlationId)}");
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

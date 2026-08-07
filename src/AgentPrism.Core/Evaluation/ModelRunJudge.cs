using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Yerlesik, model cagiran <see cref="IRunJudge"/> uygulamasi — Faz 49.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Yargicin KENDI cagrisi de bir <see cref="RunRecordingAgent"/> sarmalayicisi
/// uzerinden yapilir ve <see cref="RunKind.Eval"/> ile kaydedilir — tipki
/// <c>EvalJobHandler</c>'in her vakayi calistirdigi desen gibi. Boylece maliyet
/// otomatik hesaplanir ve <c>runs</c> tablosuna dusen bu satir zaten
/// <c>RunStatistics</c>'ten HARIC TUTULUR (bkz. <c>InMemoryRunStore.cs</c>,
/// <c>SqlRunStore.cs</c>) — olculen agent'in maliyeti sismez, yeni bir sutun
/// gerekmez. Kayit dekoratoru kotayi (<c>quotaEnforcer: null</c>) VE olay
/// yayinini (<c>webhookPublisher: null</c>) bilerek DEVRE DISI kurar: yargic
/// bir kontrol duzlemi maliyetidir, kullanicinin kotasini tuketmez ve kendi
/// <c>run.completed</c> gurultusunu yaymaz.
/// </para>
/// <para>
/// Yapilandirilmis cikti (Faz 38) <c>ChatResponseFormat.ForJsonSchema(JsonElement, ...)</c>
/// asiri yuklemesiyle istenir — elle yazilmis sema, yansimaya dayanmaz ve
/// <c>AgentPrism.Core</c>'un AOT duruşunu bozmaz.
/// </para>
/// </remarks>
public sealed class ModelRunJudge(
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
                schemaDescription: "Bir calistirmanin kalite puani."),
        };

        var judgeAgentId = $"judge:{Name}";

        var chatClient = modelProviders.CreateChatClient(options.Model);

        var innerAgent = chatClient.AsAIAgent(
            new ChatClientAgentOptions
            {
                Id = judgeAgentId,
                Name = judgeAgentId,
                ChatOptions = chatOptions,
            },
            loggerFactory,
            services);

        // Yargicin KENDI calistirmasini kayit altina alan sarmalayici. Kota ve
        // webhook BILEREK gecirilmez (yukaridaki sinif belgesi).
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
            }
            : null;

        // 🚨 Bu, yargicin KENDI maliyetidir ve `agentprism.judge.cost` etiketiyle
        // AYRICA yayilir. `RunRecordingAgent.CompleteAsync` zaten ayni maliyeti
        // genel `agentprism.run.cost` sayacina yazdi (judge'in KENDI `runs` satiri
        // icin) — burada ikinci kez hesaplamak (ayni pricingResolver, ucuz bir
        // bellek ici arama) puanlanan agent'in ozetini SISMEZ, yalnizca yargica
        // ozgu bir gosterge ekler.
        var judgeCost = usage is not null ? pricingResolver?.Resolve(options.Model.Provider, options.Model.Model, usage) : null;

        if (judgeCost is { Source: not PricingSource.Unknown } cost)
        {
            metrics?.RecordJudgeCost(
                Name,
                options.Model.Model,
                tenantContext.TenantId,
                (cost.InputCost ?? 0m) + (cost.OutputCost ?? 0m),
                cost.Currency ?? "unknown");
        }

        var (score, reason) = ParseJudgment(response.Text);

        return new RunJudgment { Score = score, Reason = reason, JudgeUsage = usage };
    }

    private static string BuildInstructions(ModelRunJudgeOptions options)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "Bir yapay zeka asistaninin ürettiği yanıtın kalitesini degerlendiren bir yargicsin. " +
            "Asagida bir calistirmanin girdisi ve ciktisi verilecek.");

        if (options.Criteria.Count > 0)
        {
            builder.AppendLine("Degerlendirme olcutleri:");

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
            "Yalnizca verilen JSON semasina uyan bir nesne dondur: 'score' 0-100 arasi bir " +
            "tamsayidir (karar veremiyorsan null birak), 'reason' kisa bir gerekcedir.");

        return builder.ToString();
    }

    private static string BuildTranscript(RunJudgeContext context)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Girdi");

        foreach (var message in context.Input)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"{message.Role}: {message.Text}");
        }

        builder.AppendLine();
        builder.AppendLine("## Cikti");
        builder.AppendLine(context.Output);

        if (context.ToolNames.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine(CultureInfo.InvariantCulture, $"## Cagrilan tool'lar: {string.Join(", ", context.ToolNames)}");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Yargicin JSON yanitini ayristirir.
    /// </summary>
    /// <remarks>
    /// Ayristirilamayan bir yanit istisna FIRLATMAZ: <see langword="null"/> puan
    /// dondurur ve <see cref="OnlineEvalJobHandler"/> hicbir puan yazmaz — sessiz
    /// bir <c>0</c> yazilmaz, olcum yoklugu ile sifir olcum karistirilmaz.
    /// </remarks>
    private static (int? Score, string? Reason) ParseJudgment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, "Yargic bos yanit dondurdu.");
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
            return (null, $"Yargic yaniti ayristirilamadi: {exception.Message}");
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

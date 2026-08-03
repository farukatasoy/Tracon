using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Microsoft Agent Framework'un dis istek nesnesini, olay akisina yazilabilen ve
/// oradan geri okunabilen bir ozete cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Bekleyen istekler icin <strong>ayri bir tablo acilmadi</strong>. Istek bir
/// <see cref="RunEventType.WorkflowRequest"/> olayinin yukunde yasar; olay akisi
/// zaten append-only, kiraci filtreli ve sayfalanabilir. Ikinci bir kayit hatti
/// ayni bilgiyi iki yerde tutar ve zamanla ayrisirdi.
/// </para>
/// <para>
/// Ozet, istegi <em>yeniden kurmaya</em> degil <em>gostermeye</em> yeter. Yanit
/// verirken gercek <see cref="ExternalRequest"/> nesnesi kontrol noktasindan
/// sürdürulen yurutmede yeniden yayinlanir ve kimlik uzerinden eslestirilir -
/// olculdu (Faz 16): sürdürme ayni <c>RequestId</c> ile ayni istegi tekrar
/// verir.
/// </para>
/// </remarks>
internal static class WorkflowRequestDescriptor
{
    private static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Istegi olay yukune cevirir.</summary>
    /// <param name="request">Microsoft Agent Framework istegi.</param>
    /// <returns>JSON ozet.</returns>
    public static string Describe(ExternalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshot = new Snapshot
        {
            RequestId = request.RequestId,
            PortId = request.PortInfo.PortId,
            RequestType = request.PortInfo.RequestType.TypeName,
            ResponseType = request.PortInfo.ResponseType.TypeName,
            Prompt = Prompt(request),
            Form = Form(request),
        };

        return JsonSerializer.Serialize(snapshot, Options);
    }

    /// <summary>
    /// Bir <see cref="RunEventType.WorkflowRequest"/> olayini bekleyen istege cevirir.
    /// </summary>
    /// <param name="runEvent">Olay.</param>
    /// <returns>Bekleyen istek; yuk okunamiyorsa <see langword="null"/>.</returns>
    /// <remarks>
    /// Yuk okunamazsa olay <strong>dusurulur</strong>. Sebep: yuk kayit
    /// ayarlarina gore kirpilmis olabilir (<c>MaxPayloadLength</c>) ve yarim bir
    /// JSON'dan uydurulmus bir istek, kullaniciya cevaplanamayacak bir kart
    /// gosterirdi.
    /// </remarks>
    public static WorkflowPendingRequest? Parse(RunEvent runEvent)
    {
        ArgumentNullException.ThrowIfNull(runEvent);

        if (runEvent.Payload is not { Length: > 0 } payload)
        {
            return null;
        }

        Snapshot? snapshot;

        try
        {
            snapshot = JsonSerializer.Deserialize<Snapshot>(payload, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (snapshot is null || string.IsNullOrEmpty(snapshot.RequestId))
        {
            return null;
        }

        return new WorkflowPendingRequest
        {
            RunId = runEvent.RunId,
            RequestId = snapshot.RequestId,
            PortId = snapshot.PortId ?? string.Empty,
            RequestType = snapshot.RequestType,
            ResponseType = snapshot.ResponseType,
            Prompt = snapshot.Prompt,
            Form = snapshot.Form,
            RequestedAt = runEvent.Timestamp,
        };
    }

    /// <summary>Portun yanit tipine gore arayuzun soracagi bicimi secer.</summary>
    /// <param name="request">Microsoft Agent Framework istegi.</param>
    /// <returns>Girdi bicimi.</returns>
    public static WorkflowRequestForm Form(ExternalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsDataOfType<MagenticPlanReviewRequest>())
        {
            return WorkflowRequestForm.PlanReview;
        }

        // Bildirimsel workflow'lar dahil, mesaj listesiyle cevaplanan her istek
        // bu arayuzden gecer; metin kutusu dogru sorudur.
        if (request.TryGetDataAs<IExternalRequestEnvelope>(out _))
        {
            return WorkflowRequestForm.Text;
        }

        if (request.PortInfo.ResponseType.IsMatch<bool>())
        {
            return WorkflowRequestForm.Boolean;
        }

        return request.PortInfo.ResponseType.IsMatch<string>()
            ? WorkflowRequestForm.Text
            : WorkflowRequestForm.Json;
    }

    /// <summary>Kullaniciya gosterilecek istek metnini cikarir.</summary>
    private static string? Prompt(ExternalRequest request)
    {
        if (request.TryGetDataAs<MagenticPlanReviewRequest>(out var review))
        {
            var plan = review.Plan.Text;

            return review.IsStalled
                ? $"Yurutme tikandi ve plan yeniden kuruldu.{Environment.NewLine}{plan}"
                : plan;
        }

        if (request.TryGetDataAs<IExternalRequestEnvelope>(out var envelope))
        {
            return envelope.GetInnerRequestContent() is TextContent text ? text.Text : null;
        }

        if (request.TryGetDataAs<string>(out var value))
        {
            return value;
        }

        if (request.TryGetDataAs<ChatMessage>(out var message))
        {
            return message.Text;
        }

        // Bilinmeyen bir yuku ToString() ile dokmek olay tablosunu sisirirdi;
        // arayuz tip adiyla da anlamli bir kart cizebilir.
        return request.PortInfo.RequestType.TypeName;
    }

    /// <summary>Olay yukunun kablo bicimi.</summary>
    private sealed record Snapshot
    {
        public string? RequestId { get; init; }

        public string? PortId { get; init; }

        public string? RequestType { get; init; }

        public string? ResponseType { get; init; }

        public string? Prompt { get; init; }

        public WorkflowRequestForm Form { get; init; }
    }
}

using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Kullanicinin verdigi yaniti, portun bekledigi tipte bir
/// <see cref="ExternalResponse"/> nesnesine cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Cevrim <strong>yanit tipine gore</strong> yapilir ve cevrilemeyen bir yanit
/// <see cref="AgentPrismException"/> ile reddedilir. Yanlis tipte bir yaniti
/// sessizce kabul etmek, yurutmeyi kullanicinin anlayamayacagi bir noktada -
/// bir executor'un icinde, bir cevrim hatasi olarak - bozardi.
/// </para>
/// <para>
/// Yanit nesnesi <em>istegin kendisinden</em> uretilir
/// (<c>ExternalRequest.CreateResponse</c>). Boylece port bilgisi ve istek
/// kimligi elle tasinmaz; ikisinin kaymasi mumkun olmaz.
/// </para>
/// </remarks>
internal static class WorkflowResponseFactory
{
    /// <summary>Yaniti kurar.</summary>
    /// <param name="request">Kontrol noktasindan yeniden yayinlanan istek.</param>
    /// <param name="answer">Kullanicinin verdigi yanit.</param>
    /// <returns>Yurutmeye gonderilecek yanit.</returns>
    /// <exception cref="AgentPrismException">Yanit, portun bekledigi tipe cevrilemiyorsa.</exception>
    public static ExternalResponse Create(ExternalRequest request, WorkflowAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(answer);

        // Plan onayi kendi yanit tipini kendisi uretir: MagenticPlanReviewResponse
        // yalnizca ChatMessage listesi tasir ve elle kurmak MAF'in ic bicimini
        // tekrarlamak olurdu.
        if (request.TryGetDataAs<MagenticPlanReviewRequest>(out var review))
        {
            if (answer.Approved is not false)
            {
                return request.CreateResponse(review.Approve());
            }

            if (answer.Text is not { Length: > 0 } revision)
            {
                throw new AgentPrismException(
                    "Plan reddedildi ancak duzeltme metni verilmedi. Yonetici agent'in plani neye " +
                    "gore yeniden kuracagini bilmesi icin 'text' alani zorunludur.");
            }

            return request.CreateResponse(review.Revise(revision));
        }

        // Mesajla cevaplanan istekler (bildirimsel workflow'larin kullanici
        // girdisi dahil) kendi zarflarini tasir.
        if (request.TryGetDataAs<IExternalRequestEnvelope>(out var envelope))
        {
            var text = Require(answer.Text, request, "metin");

            return request.CreateResponse(envelope.CreateResponse([new ChatMessage(ChatRole.User, text)]));
        }

        var responseType = request.PortInfo.ResponseType;

        if (responseType.IsMatch<bool>())
        {
            return request.CreateResponse(
                answer.Approved ?? throw Missing(request, "'approved' alani (evet/hayir)"));
        }

        if (responseType.IsMatch<string>())
        {
            return request.CreateResponse(Require(answer.Text, request, "metin"));
        }

        return request.CreateResponse(Deserialize(request, answer));
    }

    private static object Deserialize(ExternalRequest request, WorkflowAnswer answer)
    {
        if (answer.Json is not { Length: > 0 } json)
        {
            throw Missing(request, $"'{request.PortInfo.ResponseType.TypeName}' tipinde bir 'json' govdesi");
        }

        // TypeId.ToString() "Ad, DerlemeAdi, Version=..." uretir; Type.GetType
        // tam olarak bu bicimi bekler.
        var target = Type.GetType(request.PortInfo.ResponseType.ToString(), throwOnError: false)
                     ?? throw new AgentPrismException(
                         $"'{request.PortInfo.ResponseType.TypeName}' yanit tipi bu surecte cozulemedi. " +
                         "Tipi tanimlayan derleme yuklu degil; workflow'u tanimlayan paketin " +
                         "uygulamaya referansli oldugundan emin olun.");

        try
        {
            return JsonSerializer.Deserialize(json, target, JsonSerializerOptions.Web)
                   ?? throw new AgentPrismException(
                       $"Yanit govdesi '{request.PortInfo.ResponseType.TypeName}' tipine cevrildiginde bos kaldi.");
        }
        catch (JsonException exception)
        {
            throw new AgentPrismException(
                $"Yanit govdesi '{request.PortInfo.ResponseType.TypeName}' tipine cevrilemedi: {exception.Message}",
                exception);
        }
    }

    private static string Require(string? value, ExternalRequest request, string what)
        => value is { Length: > 0 } text ? text : throw Missing(request, what);

    private static AgentPrismException Missing(ExternalRequest request, string what)
        => new($"'{request.PortInfo.PortId}' portu {what} bekliyor ancak yanitta bu deger yok.");
}

/// <summary>Kullanicinin bekleyen bir istege verdigi ham yanit.</summary>
/// <param name="RequestId">Yanitlanan istegin kimligi.</param>
/// <param name="Approved">Evet/hayir yaniti.</param>
/// <param name="Text">Metin yaniti.</param>
/// <param name="Json">Serbest JSON yanit.</param>
internal sealed record WorkflowAnswer(string RequestId, bool? Approved, string? Text, string? Json);

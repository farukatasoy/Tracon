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
                    "The plan was rejected but no revision text was given. The 'text' field is " +
                    "required so the manager agent knows what to rebuild the plan against.");
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
                         $"Response type '{request.PortInfo.ResponseType.TypeName}' could not be resolved " +
                         "in this process. The assembly defining the type is not loaded; make sure the " +
                         "package defining the workflow is referenced by the application.");

        try
        {
            return JsonSerializer.Deserialize(json, target, JsonSerializerOptions.Web)
                   ?? throw new AgentPrismException(
                       $"The response body was empty after conversion to type '{request.PortInfo.ResponseType.TypeName}'.");
        }
        catch (JsonException exception)
        {
            throw new AgentPrismException(
                $"The response body could not be converted to type '{request.PortInfo.ResponseType.TypeName}': {exception.Message}",
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

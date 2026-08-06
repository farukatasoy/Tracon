using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace AgentPrism;

/// <summary>
/// OpenAI uyumlu uclarin ortak yardimcilari: hata bicimi, agent secimi ve
/// guvenilmez oturum kimliklerinin sahiplik denetimi.
/// </summary>
internal static class OpenAICompatSupport
{
    /// <summary>
    /// <c>metadata</c> icinde agent adinin arandigi anahtar. Microsoft'un DevUI
    /// uygulamasinin konvansiyonudur.
    /// </summary>
    public const string EntityIdKey = "entity_id";

    /// <summary>
    /// Istek govdesinden agent adini cikarir.
    /// </summary>
    /// <param name="body">Ham istek govdesi.</param>
    /// <returns>Agent adi; bulunamazsa <see langword="null"/>.</returns>
    /// <remarks>
    /// <para>
    /// Once <c>model</c> alanina bakilir: bu, stok OpenAI SDK'larinin ek alan
    /// yazmadan calismasini saglar
    /// (<c>client.responses.create(model="support", ...)</c>).
    /// </para>
    /// <para>
    /// Bulunamazsa <c>metadata.entity_id</c> denenir; DevUI ile uyum icin.
    /// </para>
    /// </remarks>
    public static string? ReadAgentName(JsonElement body)
    {
        if (body.ValueKind is JsonValueKind.Object &&
            body.TryGetProperty("metadata", out var metadata) &&
            metadata.ValueKind is JsonValueKind.Object &&
            metadata.TryGetProperty(EntityIdKey, out var entityId) &&
            entityId.ValueKind is JsonValueKind.String &&
            entityId.GetString() is { Length: > 0 } fromMetadata)
        {
            return fromMetadata;
        }

        if (body.ValueKind is JsonValueKind.Object &&
            body.TryGetProperty("model", out var model) &&
            model.ValueKind is JsonValueKind.String &&
            model.GetString() is { Length: > 0 } fromModel)
        {
            return fromModel;
        }

        return null;
    }

    /// <summary>Govdedeki <c>stream</c> bayragini okur.</summary>
    /// <param name="body">Ham istek govdesi.</param>
    /// <returns>Akis istendiyse <see langword="true"/>.</returns>
    public static bool ReadStreamFlag(JsonElement body)
        => body.ValueKind is JsonValueKind.Object &&
           body.TryGetProperty("stream", out var stream) &&
           stream.ValueKind is JsonValueKind.True;

    /// <summary>
    /// OpenAI bicimli bir hata yaniti uretir.
    /// </summary>
    /// <param name="statusCode">HTTP durum kodu.</param>
    /// <param name="message">Kullaniciya gosterilecek aciklama.</param>
    /// <param name="type">OpenAI hata turu.</param>
    /// <returns>Yazilabilir sonuc.</returns>
    /// <remarks>
    /// Bu uclar <c>ProblemDetails</c> <strong>kullanmaz</strong>. OpenAI SDK'lari
    /// hata govdesini <c>{"error":{"message":...}}</c> bicimiyle cozumler;
    /// <c>ProblemDetails</c> dondurmek istemcide anlamsiz bir hata uretirdi.
    /// Yonetim API'si (<c>/api/*</c>) ise <c>ProblemDetails</c> kullanmaya devam eder.
    /// </remarks>
    public static IResult Error(int statusCode, string message, string type = "invalid_request_error")
        => Results.Json(
            new OpenAIErrorEnvelope(new OpenAIErrorBody(message, type)),
            JsonOptions,
            contentType: "application/json",
            statusCode: statusCode);

    /// <summary>
    /// Guvenilmez bir oturum kimliginin gecerli kiraciya ait olup olmadigini denetler.
    /// </summary>
    /// <param name="store">Oturum deposu.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="sessionId">Istemciden gelen kimlik.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Kimlik baska bir kiraciya aitse <see langword="false"/>. Kayit yoksa
    /// <see langword="true"/> doner: kimlik henuz kullanilmamistir ve yeni bir
    /// oturum acilacaktir.
    /// </returns>
    /// <remarks>
    /// <c>conversation</c> ve <c>previous_response_id</c> istemciden gelir ve
    /// <strong>guvenilmez</strong> kabul edilir. Denetim burada, HTTP katmaninda
    /// yapilir; depoya birakilmaz. Bellek ici depo kiraci filtresi uygulamaz,
    /// dolayisiyla depoya guvenmek kurulumdan kuruluma degisen bir garanti olurdu.
    /// </remarks>
    public static async ValueTask<bool> IsOwnedByTenantAsync(
        ISessionStore store,
        ITenantContext tenantContext,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var record = await store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        return record?.TenantId is null ||
               string.Equals(record.TenantId, tenantContext.TenantId, StringComparison.Ordinal);
    }

    /// <summary>Unix saniyesi cinsinden simdiki zaman.</summary>
    /// <returns>Epoch saniyesi.</returns>
    public static long UnixNow() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>OpenAI uyumlu ciktilar icin serilestirme ayarlari.</summary>
    /// <remarks>
    /// OpenAI kablo bicimi <c>snake_case</c> kullanir; ozellik adlari bu yuzden
    /// <c>JsonPropertyName</c> ile acikca yazilir ve varsayilan adlandirma
    /// politikasina birakilmaz.
    /// </remarks>
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Sayisal kimlik ureten yardimci.</summary>
    /// <param name="prefix">Kimlik oneki.</param>
    /// <returns>Benzersiz kimlik.</returns>
    public static string CreateId(string prefix)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}{Guid.NewGuid():N}");

    /// <summary>OpenAI uyumlu hata govdesinin dis zarfi. Yaniti sema ustverisine baglamak icin internal.</summary>
    internal sealed record OpenAIErrorEnvelope(OpenAIErrorBody Error);

    /// <summary>OpenAI uyumlu hata govdesi.</summary>
    internal sealed record OpenAIErrorBody(string Message, string Type);
}

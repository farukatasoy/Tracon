namespace AgentPrism.Core.UnitTests.Runs;

/// <summary>
/// <see cref="DefaultRunErrorClassifier"/>'in kural eslesmelerini ve
/// eski/yeni <c>error_type</c> bicimlerinin ayni sinifa dustugunu dogrular.
/// </summary>
public sealed class DefaultRunErrorClassifierTests
{
    private readonly DefaultRunErrorClassifier _classifier = new();

    [Theory]
    [InlineData("content_filtered", "yanit filtrelendi", RunErrorClass.ContentFiltered)]
    [InlineData("compilation_failed", "agent derlenemedi", RunErrorClass.CompilationFailed)]
    [InlineData("provider_unavailable", "devre kesici acik", RunErrorClass.ProviderUnavailable)]
    [InlineData("System.Net.Http.HttpRequestException", "connection reset", RunErrorClass.ProviderError)]
    // Gercek bir OpenAI 404 yanitiyla olculdu (samples/AgentPrism.Api): resmi
    // SDK HttpRequestException degil ClientResultException firlatir.
    [InlineData("System.ClientModel.ClientResultException", "HTTP 404 (invalid_request_error: model_not_found)\n\nThe model `gpt-x` does not exist or you do not have access to it.", RunErrorClass.ProviderError)]
    [InlineData("System.Exception", "sunucu HTTP 503 dondurdu", RunErrorClass.ProviderError)]
    [InlineData("System.Exception", "Sunucu 429 Too Many Requests dondurdu", RunErrorClass.RateLimited)]
    [InlineData("System.Exception", "rate limit exceeded, retry later", RunErrorClass.RateLimited)]
    [InlineData("System.Exception", "Bu kiraci icin tanimli kota asildi.", RunErrorClass.QuotaExceeded)]
    [InlineData("System.Exception", "Tool 'refund_order' calisirken hata olustu", RunErrorClass.ToolError)]
    [InlineData("System.TimeoutException", "islem zaman asimina ugradi", RunErrorClass.Timeout)]
    [InlineData("System.Exception", "the operation has timed out", RunErrorClass.Timeout)]
    [InlineData("System.OperationCanceledException", "iptal edildi", RunErrorClass.Canceled)]
    [InlineData("System.Threading.Tasks.TaskCanceledException", "iptal edildi", RunErrorClass.Canceled)]
    public void Her_sinif_en_az_bir_ornekle_dogru_kovaya_duser(string type, string message, RunErrorClass expected)
    {
        var result = _classifier.Classify(new RunError { Type = type, Message = message });

        result.Class.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Uygulamaya.Ozgu.BeklenmedikBirTip", "hicbir kurala uymuyor")]
    [InlineData("System.Exception", "genel bir hata mesaji, hicbir anahtar kelime tasimiyor")]
    public void Taninmayan_hata_tahmin_edilmez_unknown_olur(string type, string message)
    {
        var result = _classifier.Classify(new RunError { Type = type, Message = message });

        result.Class.ShouldBe(RunErrorClass.Unknown);
    }

    [Theory]
    [InlineData("compilation_failed", "AgentPrism.AgentPrismCompilationException", RunErrorClass.CompilationFailed)]
    [InlineData("provider_unavailable", "AgentPrism.AgentPrismProviderUnavailableException", RunErrorClass.ProviderUnavailable)]
    public void Eski_ve_yeni_error_type_bicimleri_ayni_sinifa_eslenir(string stableType, string legacyType, RunErrorClass expected)
    {
        var stable = _classifier.Classify(new RunError { Type = stableType, Message = "hata" });
        var legacy = _classifier.Classify(new RunError { Type = legacyType, Message = "hata" });

        stable.Class.ShouldBe(expected);
        legacy.Class.ShouldBe(expected);
    }

    [Fact]
    public void Ayni_hata_her_zaman_ayni_parmak_izini_uretir()
    {
        var first = _classifier.Classify(new RunError { Type = "content_filtered", Message = "yanit filtrelendi" });
        var second = _classifier.Classify(new RunError { Type = "content_filtered", Message = "yanit filtrelendi" });

        first.Fingerprint.ShouldBe(second.Fingerprint);
    }
}

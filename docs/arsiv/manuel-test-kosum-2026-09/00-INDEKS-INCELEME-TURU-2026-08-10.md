# 00-INDEKS § 8 — 2026-08-10 inceleme turu (taşınan kayıt)

> Tarihli koşum notu. `docs/manuel-test/00-INDEKS.md` § 8'den taşındı (Faz 199,
> manuel test bütçesi — F-441 deseni): içerik **silinmedi**, olduğu gibi aşağıdadır.
> Bu kayıt bir kusur listesi değildir; tur öncesi incelemenin tarihsel özetidir.

> ## 🔧 2026-08-10 — Koşum başlamadan önce inceleme turu (bu bölümün tamamı okundu)
>
> Kullanıcı, üretim bitip koşum başlamadan önce bu bölümdeki ~40 notun tek tek
> gözden geçirilip **şu an alınabilecek her aksiyonun** alınmasını istedi. Aşağıda
> ne yapıldığı özetlenir; her not kendi orijinal metninde durur (tarihsel kayıt
> bozulmadı), yalnız bu özet eklendi. Ayrıntılı gerekçe ve kanıt: konuşma kaydı;
> kod tarafı gerekçeler ilgili commit mesajlarında.
>
> **Kodlandı (kusur, doğrudan düzeltildi):**
> 1. 🚨🚨 **KRİTİK — `CompiledAgentCache` kiracı boyutu taşımıyordu** (bu turda
>    NOTLARDA HİÇ yazılı değildi, kod okuması sırasında ayrıca bulundu). İki
>    farklı kiracının aynı adda+sürümde+bağımlılık parmak izinde bir tanımı
>    olması (ör. ikisi de `"support"`, ikisi de sürüm 1) onbelleğin BİRİNCİ
>    kiracının derlenmiş agent'ını (talimatlar, tool bağlamaları, `search_knowledge`'ın
>    bindirdiği kiracı kimliği dahil) İKİNCİ kiracıya döndürmesine yol açıyordu —
>    kiracı yalıtımının tam anlamıyla kırılması. `TenantId` artık onbellek
>    anahtarının bir parçası (`CompiledAgentCache.cs`, `DefinitionStoreAgentSource.cs`,
>    `CodeAgentSource.cs`).
> 2. 🚨 **KRİTİK ŞÜPHE doğrulandı ve KISMEN düzeltildi — dosya belleği/metin
>    araması kiracılar arası sızdırıyordu.** Paylaşılan `AgentFileStore`
>    (`TryAddSingleton`) artık her kiracı için `TenantPrefixingAgentFileStore`
>    ile sarmalanıyor (`AgentDefinitionCompiler.RequireFileStore`) — kiracılar
>    arası sızıntı KAPANDI. Aynı kiracı içindeki ajan/oturum sınırı **hâlâ
>    açık** — bkz. `docs/ADAYLAR.md` F-105 (yetenek adayı, mekanizma
>    MAF'ın `TextSearchProvider` callback'inin oturum bilgisi taşımaması).
> 3. **`HttpTenantContext.AllowedTenants` beyaz listesi gerçekten atlanıyordu**
>    (bu turda notlarda "ölçüldü, KOŞULMADI" diye kaydedilmişti — kod okumasıyla
>    doğrulandı, deterministik bir mantık hatası, koşum gerektirmiyordu).
>    `TraconEndpointFilter`'a `CheckTenancyWhitelist` eklendi: beyaz listede
>    olmayan bir aday artık 403 ile reddedilir, varsayılan kiracıya sessizce
>    düşmez.
> 4. **`--no-build` ile paketlenen `Tracon.Core` kaynak üretecini
>    TAŞIMIYORDU** (Kritik olarak işaretlenmişti). `Tracon.Core.csproj`
>    artık `@(Analyzer)` yerine Generators projesinin `GetTargetPath` hedefini
>    kullanıyor — `dotnet pack --no-build` sonrası doğrulandı
>    (`analyzers/dotnet/cs/Tracon.Generators.dll` artık var).
> 5. **SSE `error` çerçevesi boşluğu (K-296)** — `AgentEndpoints.ExecuteStreamingAsync`,
>    `OpenAIResponsesEndpoints.ResponsesStream`, `OpenAIChatCompletionsEndpoints.ChatCompletionsStream`
>    üçünde de dar `catch` filtresi kaldırıldı; artık `OperationCanceledException`
>    dışındaki HER istisna bir `error` çerçevesine dönüşüyor. Bu, Anthropic'in
>    gerçek istisna hiyerarşisini, `RegexMatchTimeoutException`'ı (guard notu,
>    aşağıda) ve gelecekteki benzer sürprizleri kapsıyor.
> 6. **`/v1/conversations/{id}/items`'ta `has_more` sabit `false` idi** —
>    artık kırpma gerçekleştiğinde `true` döner.
> 7. **`GET /api/workflows/{name}`, kod-tanımlı bir workflow için düz 404
>    döndürüyordu** — artık `runner` üzerinden var olduğu tespit edilirse
>    "düzenlenebilir tanımı yok" diye ayırt edici bir 404 mesajı döner (tam
>    birleştirme mimari olarak mümkün değil — kod-tanımlı workflow'lar hiç
>    `WorkflowDefinition` taşımıyor, bkz. `CodeWorkflowRegistration`).
> 8. **`POST /api/voice/speak`, `MaxCharactersPerRequest`'i denetlemiyordu**
>    (yalnız `speak` tool'u denetliyordu). `ISpeechSynthesizer`'a
>    `MaxCharactersPerRequest` eklendi; HTTP ucu artık aynı sınırı uygular.
> 9. **Şablon hâlâ `AddToolsFrom` (yansıma) öğretiyordu** — `AddGeneratedTools()`'a
>    geçirildi (`src/Tracon.Templates/content/Tracon.Starter/Program.cs`).
>    İzlek A ile UÇTAN UCA doğrulandı (`dotnet pack` → yerel feed → `dotnet new`
>    → `dotnet build`, sıfır uyarı).
> 10. **`Tracon:Ui:AllowRemoteAccess` ölü config anahtarıydı** —
>     `samples/Tracon.Api/Program.cs`'in `MapTracon` çağrısına bağlandı.
> 11. **AOT bayrağı `AGENTS.md` ile çelişiyordu** — dört değil sekiz paket
>     (Anthropic, Google, Azure, Voice eklendi) olacak şekilde düzeltildi.
> 12. **`/api/meta`'nın "gerçek çıktı" örneği Faz 4'ten kalma, güncel değildi**
>     (`docs/arsiv/fazlar/04-HTTP-API.md`) — `jobStore`/`jobWorkerEnabled`/`roles` alanlarını
>     içerecek şekilde güncellendi, tarihli bir uyarı notuyla.
> 13. **`docs/arsiv/fazlar/36-SAKLAMA-HACIM-SINIRI.md`'nin K-260 notu artık yanlıştı**
>     (`MaxRows`'un kiracı filtresi taşımadığı iddiası) — kod ondan ileri
>     gitmiş; sayfaya "ARTIK ESKİMİŞ" uyarısı ve güncel davranış eklendi.
> 14. 🐛 **Yan bulgu (notlarda hiç yoktu): `TemplateFixture.ResolveMetaPackageVersion()`
>     dosya sistemi sırasına güveniyordu** — `artifacts/package/release/`'de
>     birikmiş eski (bozuk, `--no-build`'den kalma) `.nupkg` dosyaları varsa
>     rastgele biri seçilebiliyordu; şablon testleri bu yüzden aralıklı
>     CS1061 ile düşüyordu. En son yazılan dosyayı seçecek şekilde düzeltildi;
>     eski `artifacts/package/release/*.nupkg` temizlendi (git-ignored, yeniden
>     üretilebilir).
>
> **Doğrudan doğrulandı, kod DEĞİŞMEDİ (not zaten doğruydu veya konu kapsam
> dışıydı):** mssql/server çelişkisi, EchoModelProvider konumu, migration
> sayısı farkı, Faz 31 kapsam boşluğu, 08/20 tablo satırları — bunların hepsi
> ilgili notta zaten "ÇÖZÜLDÜ" işaretliydi, bu turda yeniden dokunulmadı.
>
> **Yetenek adayına dönüştürüldü (kusur değil — `docs/hafiza`'daki "kusur
> kodla, yetenek planla" ayrımı):** `docs/ADAYLAR.md`'ye üç yeni
> kalem eklendi —
> **F-103** (API anahtarı kapsam taksonomisinin genişletilmesi — bu bölümde
> `RequireApiKeyScope` eksikliği DOKUZ ayrı notta tekrar tekrar bulunmuştu:
> Workflow, Scheduling, Eval/Experiment, Governance/MCP kaydı, Knowledge,
> Approval, Retention, Quota; hepsi TEK, zaten bilinen ve K-360'ta kayıtlı bir
> kararın — "tam taksonomi bilinçli ertelendi" — aynı sonucu),
> **F-104** (örnek uygulama rol politikalarını hiç kaydetmiyor, `RequireRole`
> her yerde no-op — Skill/Approval notlarında ayrı ayrı bulunmuştu, tek kök
> neden), **F-105** (dosya belleği/metin aramasının kiracı-İÇİ ajan/oturum
> sınırı — madde 2'nin tamamlanmamış kısmı).
>
> **Hâlâ açık, kasıtlı olarak dokunulmadı (koşum aşamasında doğrulanacak veya
> ayrı bir kod incelemesi ister — bu tur yalnız NOTLARI taradı, yeni bir tam
> kod taraması yapmadı):** `NpgsqlDataSourceFactory` ölü kod şüphesi,
> `RunStatistics` altı sayaç toplamı, `QuotaDecision.CostFellBackToTokens` /
> `RunErrorClass.Canceled` ölü kod şüpheleri, `RunErrorClass` Anthropic/Google'da
> `Unknown`'a düşmesi, skill sayısı sınırının save/run asimetrisi,
> `WorkflowSaveRequest.Kind` zorunlu olmaması, `pending_approvals`/`quota_usage`
> retention hedefi eksikliği, `PatternContentGuard`'ın boş bölümde bile
> kaydolması, `RunEvent.Payload` JSON olmaması, `TraconDiagnosticsReport`
> plan sapması (Faz 33 doc), `TraconTestHost`'un auth middleware eklemediği
> — bunların hiçbiri kiracı yalıtımı veya veri kaybı sınıfında değil (Orta/Düşük
> önem), bu yüzden bu turun bütçesi içinde ele alınmadı. Hız sınırı ve
> webhook/olay yayını için hâlâ hiçbir senaryo dosyası yok — bu bir kod
> boşluğu değil, **üretim** boşluğudur (bkz. §7'nin dosya listesi); sonraki
> bir üretim oturumu kapatmalı.
>
> **Doğrulama:** Dört kapı bu turda çalıştırıldı — `dotnet build` (0 uyarı, 0
> hata, arayüz dahil), `dotnet test` (dokunulan HER alan %100 geçti;
> SqlServer container'ının bu makinede hiç başlamaması — K-186/K-317 — ve
> Faz 56'nın kanarya metotlarının kiracı-kapsama sözleşme testine hiç
> eklenmemiş olması ÖNCEDEN VAR olan, bu turla ilgisiz iki bulguydu, dokunulmadı),
> `dotnet pack --no-build` (üreteç DOĞRULANDI), `dotnet format --verify-no-changes`
> (0 fark).


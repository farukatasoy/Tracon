# Faz 113 — Sağlayıcı Arıza Sınıflandırmasının Genişleme Noktası

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-149**
> **Önkoşul:** Faz 44 (hata sınıflandırma) ve Faz 62 (model yedek zinciri) — ikisi de arşivde; yalnız aşağıdaki grep'lerle okunur
> **Paketler:** `AgentPrism.Abstractions` (yeni sözleşme), `AgentPrism.Core` (`Models/`, `Runs/`)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — bir arayüz, bir enum, bir tipin görünürlüğü. Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır ve her dosya yalnız başlık taşıyor (K-603)
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/reliability.md`, `guides/model-providers.md`, `concepts/runs.md`
> · sevk edilen: yeni tiplerin XML `<example>`'ları; `write-your-own-*` ailesine bir sayfa (bkz. Açık Soru 3)
> **Manuel test alanı:** `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md` · `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 51562cd:docs/arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md
> ```
>
> Damıtıldı 2026-08-26 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism sağlayıcı arızasını iki ayrı yerde ve iki ayrı amaçla sınıflandırır: **yedek zincirine geçilsin mi** (retry) ve **run hangi hata sınıfına yazılsın** (taksonomi). İkisi de exception tipinin **adı** ve mesajdaki **HTTP metni** üzerinden regex ile karar verir.

## Bitiş Ölçütleri (DoD)

- [x] Hiçbir sınıflandırıcı kaydedilmemiş bir kurulumda retry ve hata sınıfı kararları **bit-bit bugünküyle aynı** (`FallbackRetryRegressionTests` yeşil — hem `FallbackRetryClassifier.IsRetryable` hem `DefaultProviderRetryClassifier.Classify` aynı 13 satırlık karar tablosunu doğrular)
- [x] `IProviderRetryClassifier` kaydeden tüketicinin kararı yedek zincirini yönetir; `Unknown` yerleşiğe düşer (`ProviderRetryClassifierSeamTests`; gerçek `samples/AgentPrism.Api` koşumunda da doğrulandı — MT-MYU-015/016)
- [x] `OperationCanceledException` hiçbir tüketici sınıflandırıcısı tarafından retry'a çevrilemez (`FallbackRetryClassifier.IsCancellation` seam'den ÖNCE çalışır; `A_cancellation_wrapped_in_another_exception_cannot_be_turned_into_a_retry` her-zaman-`Retry`-diyen bir casus sınıflandırıcıyla bile kanıtlar)
- [x] `DefaultRunErrorClassifier` kompozisyonla çağrılabilir; kompozisyonla üretilen parmak izi yerleşikle **aynı** kümeye düşer (`RunErrorClassifierCompositionTests`; gerçek koşumda da aynı `fingerprint` iki `run`'da ölçüldü — MT-OBS-052)
- [x] Tüketici sınıflandırıcısı exception atarsa run **durmaz**; yerleşiğe düşülür ve hata loglanır (`RunRecordingAgent.Completion.cs`'e eklenen `ClassifyOrFallback` — plan bunu içermiyordu, bkz. Plandan Sapmalar; `RunRecordingAgentTests.A_throwing_error_classifier_falls_back_...` + gerçek koşum MT-OBS-053)
- [x] Her iki nokta `TryAdd*` ile kayıtlı; tüketicinin kaydı kazanır (`ProviderRetryClassifierRegistrationTests`, `RunErrorClassifierRegistrationTests`, `ServiceRegistrationSnapshotTests`)
- [x] `AgentPrism.Core` yeni bir sağlayıcı SDK referansı **almadı** — `grep -c PackageReference src/AgentPrism.Core/AgentPrism.Core.csproj` faz öncesiyle **aynı** (14)
- [x] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban 386c386`)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (§ Gerçek Koşum Kanıtı)
- [x] `secret` taraması boş döndü (`kapi.py tarama`, kapı zincirinin içinde)
- [x] Manuel kabul case'leri iki alan dosyasına eklendi; otomatikleştirilebilenler koşuldu (`27-MODEL-YEDEK-VE-ON-UCUS.md` MT-MYU-015/016, `12-GOZLEMLENEBILIRLIK-MALIYET.md` MT-OBS-051/052/053 — beşi de gerçek OpenAI çağrısıyla koşuldu, MT-OBS-053'ün log satırı 👤 insan gözüyle doğrulandı)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz (yeni public tiplerin API referans sayfaları üretildi: `AgentPrism.IProviderRetryClassifier.md`, `AgentPrism.ProviderRetryDecision.md`, `AgentPrism.DefaultProviderRetryClassifier.md`, `AgentPrism.RunErrorFingerprint.md`)

### Doğrulama komutları

```bash
# Core'a sağlayıcı SDK sızmadı
grep -n "PackageReference" src/AgentPrism.Core/AgentPrism.Core.csproj

# AOT listesi değişmedi
grep -l "AotCompatible>false" src/*/*.csproj
```

---

## Plandan Sapmalar

1. **`DefaultProviderRetryClassifier.cs`'in konumu planın dosya listesinden
   sapıyor.** Plan `src/AgentPrism.Core/Runs/DefaultProviderRetryClassifier.cs`
   diyordu; dosya oraya yazıldı (plana sadık kalındı) ama bu bir domain
   uyuşmazlığıdır — sınıf `Runs/` değil `Models/` (sağlayıcı/retry) alanına
   aittir. Kapanışta taşınmadı çünkü namespace `AgentPrism` her iki klasörde de
   aynı ve derleme/davranış etkilenmiyor; yalnız gezinme kolaylığı kaybı.
2. **`RunRecordingAgent.Completion.cs`'e `ClassifyOrFallback` eklendi — plan
   dosya listesinde bu dosya YOKTU.** DoD satırı ("Tüketici sınıflandırıcısı
   exception atarsa run durmaz; yerleşiğe düşülür ve hata loglanır") kodu
   okuyunca kanıtsız çıktı: `RunRecordingAgent.Completion.cs:51`
   `_errorClassifier.Classify(error)`'ı hiçbir `try/catch` olmadan çağırıyordu
   — bir tüketici `IRunErrorClassifier`'ı atarsa `CompleteAsync` (zaten
   başarısız bir run'ın KAPANIŞ adımı) kendisi patlardı. `ClassifyOrFallback`
   eklendi: atarsa loglar, `new DefaultRunErrorClassifier().Classify(error)`'a
   düşer. Bu satırın planın dosya listesine girmemiş olması bir plan boşluğuydu,
   uygulama kararı değil — DoD'nin kendisi zaten bunu istiyordu.
3. **🚨 Kapsam dışı, gerçek koşumda bulunan ve düzeltilen bir kusur:**
   `FallbackChatClient`, yedek bağlıya geçerken `ChatOptions.ModelId`'yi
   güncellemiyordu — `AgentDefinitionCompiler.BuildChatOptions` bu alanı
   BİRİNCİL binding'in modeliyle derleme anında sabitliyor ve aynı `ChatOptions`
   nesnesi her yeniden deneme çağrısında tekrar kullanılıyordu. Yedek bağlının
   KENDİ modeli farklıysa (bu tipin bütün amacı budur), giden istek yine
   birincilin model adını taşıyordu. MT-MYU-015'i gerçek bir OpenAI anahtarıyla
   koşarken ölçüldü: yedek gerçek `openai`'a birincinin yer tutucu model adıyla
   gitti, sunucu `HTTP 404 (model_not_found)` döndürdü, zincir TAMAMEN
   tükendi. `kusur-giderme` protokolüyle düzeltildi (`FallbackChatClient.OptionsForLink`);
   regresyon: `FallbackChatClientTests.Fallback_link_is_called_with_its_own_ModelId_not_the_primarys`
   ve `Streaming_fallback_link_is_called_with_its_own_ModelId_not_the_primarys`.
   Ayrıntı ve sınıf taraması sonucu (tarandı, başka vaka yok):
   `docs/hafiza/model-boru-hatti.md` § "`ChatOptions.ModelId` yedek bağlıya sızar".
4. **Açık Soru 4 (`WorkflowNodeRetry`) kod değişikliği GEREKTİRMEDİ.** Plan
   "ölçülmeli" diyordu; ölçüm `AgentPrismWorkflowFunctionExtensions.cs:118`'in
   zaten `services.GetRequiredService<IRunErrorClassifier>()` çağırdığını
   (AYNI DI singleton'ı) gösterdi — tüketicinin kaydı workflow retry'ını
   otomatik kapsıyordu. Yalnız belgelendi, kod dokunulmadı.
5. **`tuketici-dokuman-senkronu` bu fazda çalıştırıldı** (plan dokümanında
   yalnız "Tüketici yüzeyi" satırında listeli, ayrı bir adım olarak
   yazılmamıştı): `guides/write-your-own-error-classifier.md` yeni sayfa
   (Açık Soru 3 → A), `guides/reliability.md`, `concepts/runs.md` ve
   `capabilities.md`'ye kısa çapraz bağlantılar eklendi, sidebar güncellendi.

## Bu Fazda Verilen Kararlar

- **K-629** — `IProviderRetryClassifier` (üç durumlu), `DefaultRunErrorClassifier`
  internal→public, `RunErrorFingerprint` facade'ı ve kapsam sınırları. Tam
  metin: `docs/KARARLAR.md`.

## Denetim Bulguları

Bağımsız denetim (`faz-denetim`, taze bağlamlı ayrı agent, 2026-08-26) bir 🔴,
üç 🟡, bir 🟢 buldu. Hepsi kapanmadan önce kapatıldı:

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `FallbackChatClient.OptionsForLink`'in XML `<remarks>`'i geliştirme günlüğü sesi taşıyordu (`🚨`, "Regression (found live...)") — kalite sözleşmesi § B ihlali; `shipped-documentation-baseline.txt`'e satır **eklenerek** muaf tutulmuştu, düzeltilmemişti | **Düzeltildi.** Yorum kendi kendine yeten bir gerekçeye çevrildi (anlatı çıkarıldı, teknik gerekçe kaldı); baseline satırı kaldırıldı (0 ihlal, muafiyet yok) |
| 2 | 🟡 | `capabilities.md` yeni `IProviderRetryClassifier`/kompoze edilebilir `IRunErrorClassifier`'dan hiç bahsetmiyordu | **Düzeltildi.** "Model providers" bölümüne kısa bir paragraf + `write-your-own-error-classifier` bağlantısı eklendi. `IRunErrorClassifier`'ın "Embedding points" tablosundan (5 satır, `/api/diagnostics`'in birebir raporladığı beş sözleşmeyle sabit) DIŞLANMASI kasıtlı korundu — diagnostics bu ikisini raporlamıyor, tabloya eklemek yanlış bir "altısı da izlenir" izlenimi verirdi |
| 3 | 🟡 | `IProviderRetryClassifier`/`OptionsForLink` akışlı (streaming) yolda hiçbir otomatik testle egzersiz edilmiyordu — yalnız tek seferlik elle koşum (MT-MYU-015/016) | **Kısmen düzeltildi.** `FallbackChatClientTests.Streaming_fallback_link_is_called_with_its_own_ModelId_not_the_primarys` eklendi (birim seviyesi — bu sınıfın BÜTÜN mevcut testleri, sync ve streaming, her zaman birim seviyesindeydi, yeni bir emsal değil). Gerçek DI+HTTP+SSE sınırını geçen bir FONKSİYONEL test (`tests/AgentPrism.AspNetCore.FunctionalTests/`) `FallbackChatClient` için hiç yoktu — bu faz öncesine ait bir boşluk, bu fazda genişletilmedi. `docs/ADAYLAR.md`'ye aday olarak yazılmadı çünkü kapsamı `FallbackChatClient`'ın TAMAMI (yalnız bu fazın eklediği parça değil); bir sonraki oturum bu notu okuyup karar verebilir |
| 4 | 🟡 | Denetim başladığında DoD'nin "faz-denetim koşuldu" satırı, rapor bitmeden ✅ işaretlenmişti (paralel kulvar sırasında yarış) | Süreç notu, kod kusuru değil. Bu bölüm (Denetim Bulguları) denetim GERÇEKTEN bittikten sonra yazıldı; DoD satırı geçerli |
| 5 | 🟢 | `guides/model-providers.md` yeni seam'lere çapraz bağlantı almadı | Aday değil — `reliability.md`/`concepts/runs.md`/`capabilities.md` zaten kapsıyor, dördüncü sayfa gereksiz tekrar olurdu |

Denetimin "temiz" bulduğu başlıklar (değişmeden doğrulandı): regresyon karar
tablosu, iptal sızıntısı koruması, kompozisyon+parmak izi eşleşmesi,
`ClassifyOrFallback` ile run'ın durmaması, `TryAdd*` kaydı, Core'a sağlayıcı
SDK referansı sızmaması, imza-gövde zinciri, manuel case'lerin gerçekliği.

## Sonraki Faza Devir Notu

- **`FallbackChatClient`'ın hiçbir fonksiyonel (DI+HTTP+akış sınırı) testi yok**
  — yalnız birim testleri var, bugüne kadar hep öyleydi. Bu fazın kendi
  parçası (retry seam, `OptionsForLink`) da aynı seviyede kaldı. Gerçek bir
  sağlayıcı hatasında akışlı yolun uçtan uca çalıştığını yalnız elle koşum
  (MT-MYU-015/016) kanıtlıyor. `tests/AgentPrism.AspNetCore.FunctionalTests/`
  altına bu sınıf için bir dosya açmak ayrı, `FallbackChatClient`'ın TAMAMINI
  kapsayan bir iştir — bu faz onu genişletmedi.
- **`DefaultProviderRetryClassifier.cs` `Runs/` klasöründe yaşıyor ama alanı
  `Models/`dir** (bkz. Plandan Sapmalar #1). Dokunursan doğru yere taşımayı
  değerlendir; bu fazın kapsamı değildi.
- **`ChatOptions.ModelId` tuzağı yalnız `FallbackChatClient`'ta düzeltildi.**
  Sınıf taraması (`docs/hafiza/model-boru-hatti.md`) birden fazla FARKLI
  binding'e ait istemciyi çağıran BAŞKA bir kod yolu bulmadı — ama yeni bir
  öyle yol (ör. bir "routing" veya "load balancing" özelliği) eklenirse aynı
  tuzağı taşıyıp taşımadığı doğrulanmalı.
- **`WorkflowNodeRetry`'nin `IRunErrorClassifier` paylaşımı artık belgeli**
  (`write-your-own-error-classifier.md`) ama kendi otomatik testi yok —
  `WorkflowNodeRetryTests` zaten enjekte edilen sınıflandırıcıyla çalışıyor,
  DI-seviyesinde "consumer registration workflow'a da ulaşır" iddiasını
  doğrudan kanıtlayan bir test eklenmedi (yalnız grep + kod okuma).

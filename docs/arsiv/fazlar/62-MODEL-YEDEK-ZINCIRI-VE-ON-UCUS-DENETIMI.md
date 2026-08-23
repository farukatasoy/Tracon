# Faz 62 — Model Yedek Zinciri ve Ön Uçuş Denetimi

> **Durum:** ✅ Tamamlandı (2026-08-18)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-44**, **F-59**
> **Önkoşul:** [Faz 8](08-SAGLAYICI-GENISLEMESI.md) — devre kesici ve sağlayıcı sağlığı bu fazın yarısını kurdu · [Faz 13](13-BAGLAM-SIKISTIRMA-VE-BELLEK.md) — `MaxContextWindowTokens`'ın bugünkü tek tüketicisi
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Yok — yedek zinciri agent tanımının içindedir, tanım zaten `jsonb` olarak saklanır
> **Public API:** **büyüyor** — `ModelBinding`'e bir alan. `PublicAPI.Shipped.txt` bugün **boş** (ölçüldü: 1 satır), `EnablePublicApiTracking` `true`. `sealed record`'a alan eklemek **bugün bedava**, ilk yayından sonra bir sürüm kararıdır
> **Site etkisi:** `guides/model-providers.md`, `guides/reliability.md`, `reference/configuration.md`
> **Manuel test alanı:** `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/62-MODEL-YEDEK-ZINCIRI-VE-ON-UCUS-DENETIMI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz model çağrısının **iki ucunu** kapatır: çağrıdan **önce** parayı harcamadan reddetmek, çağrı **başarısız olduğunda** hizmeti ayakta tutmak. Bugün ikisi de eksiktir. Sağlayıcı kesildiğinde devre kesici açılır ve çalıştırma **yalnız hata verir**; ikinci bir sağlayıcıya geçiş yoktur.

## Bitiş Ölçütleri (DoD)

- [x] `Fallbacks` boşken bugünkü hata yolu **birebir** korunur (test kanıtlar) — `FallbackChatClientTests.Empty_fallbacks_preserves_todays_error_path`
- [x] Devre açıkken yedek devreye girer; `run` kaydı ve `RunStatistics.ByModel` **gerçekten çalışan** modeli gösterir — `FallbackRecordingTests` (üç test), dört depo sözleşme testinde de doğrulandı
- [x] Kimlik doğrulama hatası, içerik filtresi ve iptal yedeği **tetiklemez** — `Authentication_errors_are_not_retried`, `Content_filter_does_not_trigger_a_fallback_attempt`, `Canceled_calls_are_not_retried`; sarmalanmış biçimleri de (`An_authentication_failure_wrapped_the_same_way_still_does_not_retry`)
- [x] Zincir tükendiğinde hata mesajı denenen sağlayıcıları sayar — `Exhausted_chain_throws_the_first_failure_not_the_last`
- [x] Eşzamanlılık sınırı `null` iken sıcak yolda ek tahsis **yoktur** — `Unlimited_by_default_never_waits`; tasarım gereği hiç sarmalayıcı eklenmiyor
- [x] `MaxContextWindowTokens` boşken değer `ModelDescriptor`'dan türetilir; ikisi de boşsa hata iki yolu da söyler — iki yeni `AgentDefinitionCompilerTests`
- [x] Ön uçuş **kapalı** varsayılandır; açıkken aşan istem model çağrısı **yapılmadan** `400` döner — `PreflightEndpointTests` (gerçek HTTP host üzerinden)
- [x] `POST /api/agents/{name}/estimate` sağlayıcıya istek **göndermeden** sayı döner — `PreflightEndpointTests`, `ContextWindowEstimatorTests`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/`format` tüm çözüm genelinde yeşil (bu turda birden fazla kez koşuldu)
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — **YAPILAMADI**, bu ortamda gerçek sağlayıcı kimlik bilgisi yok (bkz. Plandan Sapmalar #7). Yerine: gerçek OpenAI SDK'sına karşı canlı bir bağlantı-hatası testi koşuldu (bkz. Denetim Bulguları #1) ve dört gerçek SQL/HTTP entegrasyon paketi (Postgres/Sqlite/SqlServer/AspNetCore.FunctionalTests) baştan sona koşuldu.
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md` içine eklendi (9 case); otomatikleştirilebilen KISMI (gerçek kimlik bilgisi gerektirmeyenler) otomatik testlerle zaten kapsanıyor — dosyanın kendisi gerçek kimlik bilgisiyle **henüz koşulmadı** (§7 `⬜`)
- [x] `faz-denetim` koşuldu; 🔴 bulgu **kapandı** (K-450)
- [x] `docs-site/` güncellendi (`guides/model-providers.md`, `guides/reliability.md`, `reference/configuration.md`); `npm run build` + `check-links.mjs` temiz — 901 sayfa, 111.200 iç referans, sıfır kırık
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — 166,2 KB gzip / 250 KB (Faz 61 taban: 165,4 KB; +0,8 KB)

### Doğrulama komutları

```bash
# Kestirim - model cagrisi YAPILMAZ
curl -s -X POST http://localhost:5081/agentprism/api/agents/demo/estimate \
  -H 'Content-Type: application/json' \
  -d '{"message":"..."}'

# On ucus acikken asan istem reddedilir
curl -s -o /dev/null -w '%{http_code}\n' -X POST \
  http://localhost:5081/agentprism/api/agents/demo/run \
  -H 'Content-Type: application/json' \
  -d '{"message":"<pencereden buyuk istem>"}'
```

---

## Plandan Sapmalar

1. **Ön uçuş/`estimate` sayımı yalnız YENİ kullanıcı mesajını sayar, oturum
   geçmişini DEĞİL.** Plan "istem token sayısı" diyordu ama tam konuşma
   geçmişini (`ChatHistoryProvider` üzerinden) okumak preflight'ı "ucuz,
   risksiz" olmaktan çıkarırdı — F-59'un kendi Amaç bölümü türetimi model
   üstverisinden, sayımı ise yalnız "yaklaşık" bir kestirim olarak
   çerçeveliyordu. `ContextWindowEstimator.Estimate` yalnız
   `AgentRunRequest.Message` metnini sayar. Uzun bir oturumda geçmişin kendisi
   pencereyi doldurmuşsa bu KAÇAR — bilinçli bir kapsam sınırıdır.
2. **Tokenizer sabit bir referans kodlama kullanır (`o200k_base`/`gpt-4o`),
   bağlanan sağlayıcıdan BAĞIMSIZ.** Açık Soru 1 seçenek A'yı seçti ama
   "hangi model için hangi tokenizer" sorusunu açık bıraktı. Anthropic/Google
   çevrimdışı bir tokenizer paketi yayınlamadığı için AgentPrism TEK bir sabit
   kodlamayla her sağlayıcıyı yaklaşık sayar — K-448.
3. **`Microsoft.Bcl.Memory` CVE zorlaması plan dışıydı.** Tokenizer veri
   paketi (`Microsoft.ML.Tokenizers.Data.O200kBase`) ölçülene kadar
   bilinmeyen bir NU1903 (yüksek önem) getirdi ve `dotnet restore`'u kırdı;
   K-007 deseniyle sabitlendi — K-448.
4. **`RunCompletion.ModelId` alanı ve `RunEventWriter.CompleteAsync`'in imza
   değişikliği plandaki "Planlanan Public API" listesinde YOKTU.** DoD'nin
   "`RunStatistics.ByModel` gerçekten çalışan modeli gösterir" satırı bunu
   ZORUNLU kıldı: `runs.model_id` yalnız `RunStarted`'da (birincil modelle)
   yazılıyordu, tamamlanmada güncellenecek bir yol yoktu. Dört depo
   uygulamasının (InMemory/Postgres/Sqlite/SqlServer) DÖRDÜ de dokunuldu.
5. **`ModelProviderRegistry` yeni bir `concurrencyLimiter` kurucu parametresi
   aldı** — plan taslağında yoktu, F-44'ün eşzamanlılık sınırının boru
   hattına girmesinin doğal sonucu.
6. **Agent düzenleyicisine "tahmin rozeti" (estimate badge) EKLENMEDİ.**
   Plan "model bölümüne kestirim rozeti eklenir" diyordu. Backend (`/estimate`
   ucu, `ContextWindowEstimator`) TAM çalışır durumda ve UI'dan `curl` ile
   kullanılabilir; canlı-güncellenen bir arayüz rozeti (debounce, yükleniyor/
   hata durumları) ayrı bir iş parçası olarak KAPSAM DIŞI bırakıldı — zaman
   bütçesi kararı. Yedek listesi editörü (add/remove, provider/model alanları,
   E2E ile kanıtlanmış kayıt/geri-okuma) TAM uygulandı.
7. **DoD'nin "`samples/AgentPrism.Api` ile gerçek `run` yapıldı" satırı
   TAMAMLANAMADI.** Bu ortamda gerçek OpenAI/Anthropic kimlik bilgisi yok;
   davranış bunun yerine gerçek bir Postgres/Sqlite/SqlServer konteynerine
   karşı koşan sözleşme testleriyle VE `FallbackChatClientTests`/
   `FallbackRecordingTests`'in uçtan uca (gerçek `ModelProviderRegistry` +
   `RunRecordingAgent` boru hattı, yalnız ağ çağrısı sahte) testleriyle
   kanıtlandı. Manuel kabul dosyası (`27-MODEL-YEDEK-VE-ON-UCUS.md`) gerçek
   kimlik bilgisiyle koşulmayı bekliyor — §7 "Koşum" sütununda `⬜`.
8. **`AgentDefinitionCompiler.FindModelDescriptor` paylaşılan
   `ModelCatalogLookup.Find`'a çıkarıldı** (küçük bir DRY refaktörü,
   `ContextWindowEstimator`'ın AYNI arama mantığına ihtiyacı olduğu için).

## Bu Fazda Verilen Kararlar

K-443 — K-450 (sekiz karar), `docs/KARARLAR.md`'de:

- **K-443** — Yedek zincirinin boru hattı konumu: devre kesici DIŞI, içerik
  filtresi tespiti İÇİ (K-320'nin uygulanışı).
- **K-444** — `ModelFallback` yalnız `Provider`+`Model` taşır (Açık Soru 2, A).
- **K-445** — Eşzamanlılık sınırı reddetmez, bekler (Açık Soru 3, A).
- **K-446** — Ön uçuş reddi `400` döner (Açık Soru 4, A).
- **K-447** — Yedek olayı hem `run_events` hem span etiketine yazılır (Açık
  Soru 5, A).
- **K-448** — Tokenizer için açık `PackageReference` + `Microsoft.Bcl.Memory`
  CVE zorlaması (Açık Soru 1, A + ölçülen bulgu).
- **K-449** — Retryable olmayan hata (401/403, iptal) hangi halkada olursa
  olsun anında ve sarmalanmadan fırlatılır; zincir yalnız tüm halkalar
  retryable hatayla tükendiğinde "ilk hata" özetine sarılır.
- **K-450** — `IsRetryable` istisnanın TAMAMINI (`InnerException` zinciri +
  `AggregateException` kolları) gezer, yalnız en dıştakine bakmaz — bağımsız
  denetimde bulunan 🔴 bulgunun düzeltmesi (bkz. Denetim Bulguları).

## Denetim Bulguları

`faz-denetim` taze bağlamlı bağımsız bir alt agent olarak koşuldu (çalışma
ağacı, faz 62 kapsamındaki dosyalarla sınırlı).

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `FallbackRetryClassifier.IsRetryable` yalnız en dıştaki istisnaya bakıyordu; gerçek `OpenAI` 2.12.0 istemcisi dinlemeyen bir porta karşı `AggregateException → ClientResultException → HttpRequestException → SocketException` zinciri fırlatıyor (ölçüldü), hiçbiri eşleşmiyordu — canlı bir kesintide devre kesici açılana kadarki ilk `FailureThreshold` istek yedeğe hiç düşmeden çıplak hata dönüyordu. | 🔴 | **Düzeltildi** — K-450. `IsRetryable` artık `Flatten(exception)` ile tüm zinciri (kendisi + `InnerException` + her `AggregateException` kolu) gezer; `TransportExceptionTypePattern`'e SDK sarmalayıcı tipleri eklendi (yalnız hiçbir karede HTTP durum metni yokken devreye girer, güvenli). Gerçek SDK'ya karşı doğrulandı (canlı "connection refused" zincirinde `IsRetryable` artık `true`) + iki regresyon testi eklendi (`A_bare_connection_failure_wrapped_the_way_the_real_OpenAI_client_wraps_it_falls_over`, `An_authentication_failure_wrapped_the_same_way_still_does_not_retry`). Tuzak `docs/hafiza/openai-saglayici.md`'ye yazıldı. |
| 2 | Agent düzenleyicisine "tahmin rozeti" eklenmedi — bilinçli bir kapsam kararı olarak bildirilmişti ama "Plandan Sapmalar" bölümü denetim anında boştu. | 🟡 | **Kapandı** — bu kapanış turunda "Plandan Sapmalar" #6 olarak yazıldı (yukarıda). Kod tarafında ek iş yok. |

**Temiz çıkan başlıklar:** 3.1, 3.2, 3.3, 3.5, 3.6, 3.7, 3.8 (denetçinin tam
gerekçesi ajan çıktısında; bu doküman yalnız 🔴/🟡 bulguları taşır).

## Sonraki Faza Devir Notu

- **Devralınan sözleşme:** `ModelProviderRegistry.CreateChatClient`'ın boru
  hattı sırası artık (dıştan içe) içerik filtresi tespiti → **yedek zinciri**
  → devre kesici → ek çözme → `FunctionInvokingChatClient` → OTel → içerik
  guard'ı → **eşzamanlılık sınırlayıcı** → ham istemci. Yeni bir halka
  eklerken K-320'nin sorusu ("her gerçek model çağrısını görmesi gerekiyor
  mu?") artık yedek zincirini de hesaba katmalı: yedek DIŞINDA bir halka
  yalnız BİRİNCİL denemeyi görür, yedek İÇİNDE bir halka (eşzamanlılık
  sınırlayıcı gibi) her halkayı ayrı ayrı görür.
- **🚨 Bilinen tuzak (K-450, `docs/hafiza/openai-saglayici.md`):** bir
  sağlayıcı SDK'sının fırlattığı istisnayı sınıflandırırken yalnız en dıştaki
  istisnaya bakmak yeterli değildir — gerçek bağlantı hataları `AggregateException`
  içinde çok katmanlı gelir. Yeni bir sınıflandırıcı yazan herkes
  `Exception.InnerException`/`AggregateException.InnerExceptions` zincirini
  gezmelidir.
- **🚨 Bilinen tuzak (bu fazda ölçüldü, K-007 emsali):** `Microsoft.ML.Tokenizers`
  gibi bir veri paketi eklerken transitif bir CVE zorlaması (NU1903) restore'u
  KIRABİLİR; `dotnet restore` gerçekten çalıştırılmadan "paket eklendi"
  denemez.
- **Yarım kalan iş:** `docs/manuel-test/27-MODEL-YEDEK-VE-ON-UCUS.md` gerçek
  `OpenAI`/ikinci bir sağlayıcı kimlik bilgisiyle HENÜZ koşulmadı (bu ortamda
  kimlik bilgisi yok — §7 `⬜`). Bir sonraki oturum, gerçek kimlik bilgisi
  varsa bu dosyayı `manuel-test-kosumu` skill'iyle koşup kapatmalı; K-450'nin
  düzeltmesi `MT-MYU-002`'yi artık geçirmelidir (denetimde ölçülen kanıt bunu
  destekliyor, ama gerçek ortamda TEYİT edilmedi).
- **Sıradaki faz:** `docs/ADAYLAR.md`'den seçilecek; bu fazın kapsamı dışında
  yeni bir aday üretilmedi (denetimin 🟢 listesi boş çıktı).

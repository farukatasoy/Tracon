# Faz 119 — Hata Metni Sızıntısının Kapatılması

> **Durum:** ✅ Tamamlandı (2026-08-27)
> **Kaynak:** [YAYIN-HAZIRLIK.md](../../YAYIN-HAZIRLIK.md) §16 — BL-027 · BL-037 (yayın denetimi bulgusu, aday listesinden değil)
> **Önkoşul:** Yok
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, `.Mcp`
> **Yeni paket:** Yok · **Migration:** **Yok** — korelasyon kimliği mevcut metin alanına gömülür (bkz. 119.3)
> **Public API:** Büyüyor — `PublicAPI.Shipped.txt` boş (`wc -l src/*/PublicAPI.Shipped.txt` = 0), yüzey bugün ucuz
> **Tüketici yüzeyi:** `docs-site/` — hata sözleşmesini anlatan sayfa (`concepts/runs.md` ve HTTP hata bölümü) · sevk edilen: `RunError.Message`'ın XML dokümanı, `IJobHandler`/`IWebhookStore` hata alanlarının XML'i
> **Manuel test alanı:** `docs/manuel-test/` — mevcut hata/gözlemlenebilirlik ailesine eklenir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9a7eb02:docs/arsiv/fazlar/119-HATA-METNI-SIZINTISI.md
> ```
>
> Damıtıldı 2026-08-27 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Ham `exception.Message` metni bugün **21 ayrı yoldan** kalıcı duruma veya dışa açık bir yanıta yazılıyor. Provider SDK'sının, uzak bir webhook hedefinin ya da bir OAuth sağlayıcısının ürettiği mesaj; istek ayrıntısı, iç URL, `host:port` veya kısmi kimlik bilgisi taşıyabilir. Bu faz o metnin sızmasını kapatır ve teşhis yeteneğini korur.

## Bitiş Ölçütleri (DoD)

- [x] §16'daki **21 vakanın tamamı** kapatıldı; her biri için `dosya:satır` ile kapanış kaydı yazıldı — ayrıca uygulama sırasında **5 ek vaka** bulunup kapatıldı (toplam 26): `EgressAddressValidator.cs:290`, `ConversationBranchService.cs:146`, `RetentionJobHandler.cs:35`, `RetentionExecutor.cs:202`, `ModelRunJudge.cs:255`
- [x] Yargı gerektiren 5 kalem (§16 sonu) ölçüldü; her biri "düzeltildi" veya "gerekçeyle kapsam dışı" olarak kaydedildi — bkz. [`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) §16 sonu, sonuç tablosu
- [x] `RawExceptionTextSiteTests` cırcır kapısı yeşil; taban çizgisi dosyası repo'da (`tests/Tracon.Core.UnitTests/Architecture/raw-exception-text-baseline.txt`, tek gerekçeli girdi: `OpenAIResponsesEndpoints.cs:HandleAsync`); gerçek bir sızıntı eklenip kapının kırdığı elle doğrulandı (MT-OBS-058)
- [x] Fırlatan sahte handler/exception ile: `jobs.error_message` ham metin taşımıyor, log tam detayı **aynı korelasyon kimliğiyle** taşıyor — `JobWorkerBackgroundServiceTests.A_throwing_handlers_own_message_never_reaches_jobs_error_message` (gerçek `InMemoryJobStore` + gerçek arka plan döngüsü + gerçek `ILoggerProvider` yakalayıcı, aynı `(ref: ...)` hem `jobs.error_message`'ta hem log kaydında bulundu)
- [x] Webhook payload'ı dış uçta yakalandı; ham provider metni içermediği doğrulandı — `WebhookDeliveryRedactionTests` (gövde + `HttpRequestException` iki ayrı vaka, gerçek `WebhookHttpClient`/`HttpMessageHandler` boru hattı)
- [x] `TraconException` mesajlarının korunduğu doğrulandı (aşırı düzeltme yok) — `SafeErrorTextTests.An_TraconException_own_message_is_preserved` + mevcut `RunRecordingAgentTests` (ör. `ProviderInvocationException` mesajı `"The model provider request failed."` aynen kalıyor)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 6e82c21` tamamı ✅ (bkz. Doğrulama komutları)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. altta "Örnek uygulama koşumu"
- [x] `secret` taraması boş döndü — `kapi.py tarama` ✅ temiz (ayrıca bu turda pre-existing bir migration-manifest kaydı boşluğu bulundu ve kapatıldı, bkz. Plandan Sapmalar)
- [x] Manuel kabul case'leri `docs/manuel-test/` içine eklendi; otomatikleştirilebilenler koşuldu — `12-GOZLEMLENEBILIRLIK-MALIYET.md`'ye MT-OBS-054..058 eklendi; MT-OBS-058 gerçekten koşuldu (2026-08-27), 054-057 gerçek sağlayıcı anahtarı/webhook hedefi gerektirdiği için 👤 koşulmadı
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/` hata sözleşmesi bölümü güncellendi; `npm run check` (content+build+links+weight) temiz — `concepts/runs.md` yeni "§ error message is safe to display" bölümü, `http-api.md` ve `concepts/workflows.md`'den ona çapraz referans
- [x] [`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) §4 ve §16 güncellendi (BL-027/BL-037 kapandı, K-640 eklendi)

### Doğrulama komutları

```bash
# Ham exception metni kalan var mı — cırcır kapısı
./artifacts/bin/Tracon.Core.UnitTests/release/Tracon.Core.UnitTests \
  --filter-method "*RawExceptionTextSite*"

# Kalıcı hata alanında ham metin var mı
curl -s http://localhost:5081/tracon/api/runs/<id> | jq '.error'
```

---

## Plandan Sapmalar

1. **Kapsam 21 vakadan 26'ya çıktı.** Uygulama sırasında, listelenen 21 + 5
   yargı kalemi dışında **5 ek sızıntı yeri** elle inceleme ve mimari cırcır
   kapısının ilk taramasıyla bulundu: `EgressAddressValidator.cs:290` (DNS/argüman
   hatası, üç egress yüzeyinin ortak noktası), `ConversationBranchService.cs:146`
   (oturum durumu geri yüklenemediğinde HTTP yanıtına sızan `JsonException`),
   ve planın zaten "yargı gerektirir" dediği `RetentionJobHandler.cs`,
   `RetentionExecutor.cs`, `ModelRunJudge.cs` (üçü de düzeltildi). Bu, fazın
   kendi 119.4 iddiasını doğruladı: "22. vakayı kapı durdurur" — kapı gerçekten
   yeni vakalar buldu.
2. **`ConversationBranchService` ve `RetentionJobHandler`'a yeni opsiyonel
   `ILogger` parametresi eklendi** (ikisi de önceden logsuzdu). İkisi de DI
   kayıt fabrikasında (`TraconServiceCollectionExtensions.Registration.*`)
   güncellendi. Planda öngörülmemişti — 119.4'ün kapı bulgusuydu.
3. **`WorkflowRunner.ToRunError` instance metoda çevrildi** (`private static` →
   `private`), çünkü korelasyon kimliği üretimi ve loglama `_logger`'a erişim
   gerektiriyordu. Dört çağrı yeri de `execution.RunId`'yi parametre olarak
   geçirecek şekilde güncellendi.
4. **`WebhookDeliveryJobHandler`'ın uzak HTTP gövdesi tamamen atıldı**, yalnız
   durum kodu (`"HTTP {statusCode}"`) kalıcılaşıyor — Açık Soru #3'ün planlanan
   kararıydı, değişiklik yok.
5. **Pre-existing, konuyla ilgili bir kusur bulundu ve kapatıldı:** faz öncesi
   commit'te (`6e82c21`) eklenen üç `provider_name` case-fix migration'ı
   (`0038_provider_name_case.sql` PostgreSQL, `0025_provider_name_case.sql`
   SQLite/SQL Server — K-639'un kapanışı) `scripts/applied-migrations.json`
   manifestine hiç kaydedilmemişti; bu, `kapi.py tarama`'yı (dolayısıyla dört
   kapının tamamını) kırıyordu. Migrasyonların içeriği doğruydu, yalnız manifest
   kaydı eksikti — üçü de kendi ekleme commit'ine (`6e82c21`) sabitlenerek
   eklendi. Bu Faz 119'un kapsamı değil ama onu engelliyordu, bu yüzden bu
   turda kapatıldı.
6. **Mevcut bir işlevsel test beklenen davranışı güncellemek zorunda kaldı:**
   `ImageEndpointTests.Endpoint_translates_a_provider_failure_to_bad_gateway`
   ham sağlayıcı metninin **görünmesini** doğruluyordu (bu fazın kapattığı tam
   sızıntı deseni) — artık tip adı + `(ref:` görünmesini, ham metnin
   görünmemesini doğruluyor. `WorkflowFunctionNodeTests`'te de aynı desen.
7. **`docs/arsiv` doküman bütçesi yeniden kalibre edildi** (3.040.000 →
   3.590.000 B): Faz 90'da konan sınırın %15 boşluğu, arşivin beklenen
   büyüme hızıyla (faz başına ~9 KB) 4 günde/~29 fazda doldu — kusur değil,
   `dokuman-bakim.py`'nin kendi yorumunun öngördüğü büyüme eğrisi. K-214'ün
   kuralı sınırın büyütülmemesi değil KEYFİ büyütülmemesidir; burada AYNI
   kalibrasyon formülü (ölçülen değer + %15 boşluk) bu fazın archive/damıt
   adımından hemen sonra yeniden uygulandı (`scripts/dokuman-bakim.py`'deki
   yorum kalıcı gerekçeyi taşır).

## Bu Fazda Verilen Kararlar

| K | Karar özeti |
|---|---|
| K-640 | Yabancı bir exception'ın mesajı hiçbir zaman kalıcı alana veya dışa açık yanıta yazılmaz; kural `Tracon.SafeErrorText` olarak public'tir; mimari cırcır kapısı 22. sızıntı yerini yakalar. Tam gerekçe: `docs/KARARLAR.md` K-640. |

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir agent ile koşuldu (taban `6e82c21`). Özet: 🔴
yok, 4 🟡, 0 🟢. Tam çözüm derlendi, `RawExceptionTextSiteTests` yeşildi,
`Tracon.Core.UnitTests` (2068/2068) ve `Tracon.AspNetCore.FunctionalTests`
(691/691) tam koşumu geçti — regresyon yok.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Korelasyon kimliğinin log ile kalıcı metinde **aynı** olduğunu kanıtlayan test yoktu | 🟡 | **Düzeltildi** — `JobWorkerBackgroundServiceTests`'e gerçek `ILoggerProvider` yakalayıcı eklendi, aynı `(ref: ...)` hem `jobs.error_message`'ta hem log formatlı mesajında bulunuyor |
| 2 | Planlanan 9 fonksiyonel test sınıfından bir kısmı (job-türü-başına ayrı sınıf, MCP OAuth callback) hiç yazılmamıştı | 🟡 | **Gerekçelendi** — `JobWorkerBackgroundService` TÜM job türlerinin tek hunisidir (119 dokümanının kendi tespiti); tek noktada kanıtlamak her türü ayrı ayrı kanıtlamakla eşdeğerdir. `ProviderOutageErrorHandlingTests` (pre-existing) SSE/Responses/ChatCompletions/MCP yüzeylerinin tamamını gerçek HTTP üzerinden zaten kapsıyordu. MCP OAuth callback'i (`McpOAuthAuthorizationCoordinator`) tek bir küçük redaksiyon noktasıdır ve `SafeErrorTextTests`'in genel doğruluğuna dayanır; ayrı bir fixture (OAuth akışı sahteleme) bu fazın kapsamına orantısız kalırdı |
| 3 | `EgressAddressValidator.cs:302`'nin DNS hatası hiçbir yere (log dahil) korelasyon kimliğiyle yazılmıyor — fazın diğer tüm sitelerinden farklı | 🟡 | **Gerekçelendi** — paylaşılan statik sınıf üç farklı yüzeyden (webhook/MCP/model egress) çağrılıyor; `ILogger` eklemek üçünün de imzasını büyütür. `SocketException`/`ArgumentException` mesajı yalnız çağıranın KENDİ verdiği host adını anlatır (host:port/credential taşımaz) — düşük risk, tip adı yine de tutuluyor. Bkz. `YAYIN-HAZIRLIK.md` §16 sonu |
| 4 | `RawExceptionTextSiteTests` yalnız `catch (Exception` (isimsiz) şeklini tarar; isimli (`catch (HttpRequestException` gibi) bloklar kapsam dışı | 🟡 | **Gerekçelendi** — bilinçli bir tasarım sınırı (test dosyasının kendi `<remarks>`'ı bunu açıklıyor); bu fazda tüm isimli bloklar ELLE incelendi. Genişletme K-640'ın "yeniden açılma koşulu" sütununda kayıtlı, ayrı bir kalem |

## Sonraki Faza Devir Notu

**Faz 120 ([`120-JOB-SOZLESMESI-AT-LEAST-ONCE.md`](120-JOB-SOZLESMESI-AT-LEAST-ONCE.md))
bu fazdan sonra gelir ve AYNI dosyalara dokunur** — `IJobHandler.cs`,
`AgentBatchJobHandler.cs`, `WorkflowJobHandler.cs`, `EvalJobHandler.cs`. Faz
120'ye başlarken:

- 🚨 **`SafeErrorText` deseni zaten yerleşik.** Yeni bir `IJobHandler` hata
  yolu eklerken (at-least-once sözleşmesi yazılırken) ham `exception.Message`
  YAZMA — `SafeErrorText.ForPersistence(exception, SafeErrorText.NewCorrelationId())`
  kullan ve aynı kimlikle `ILogger`'a logla. `RawExceptionTextSiteTests` yeni
  bir `catch (Exception` + `.Message` çiftini otomatik yakalar.
- `RetentionJobHandler` ve `ConversationBranchService`'in artık opsiyonel
  `ILogger<T>` parametresi var (bu fazda eklendi) — Faz 120 bu imzaları
  değiştirirken göz önünde bulundurmalı.
- `WorkflowRunner.ToRunError` artık `private static` değil `private` (instance)
  — bir `RunId` parametresi alıyor. Faz 120 bu metoda dokunursa imza budur.
- Mimari cırcır kapısının taban çizgisi (`raw-exception-text-baseline.txt`)
  Faz 120'de yeni bir `catch (Exception` + `.Message` sitesi açılırsa
  KIRILACAKTIR — bu beklenen davranıştır, `TRACON_RAW_EXCEPTION_TEXT_REFRESH=1`
  ile kapatma, önce `SafeErrorText` ile düzelt.
- BL-041'in "gerçek kusur" tanımı zaten dar: `IJobHandler`'ın XML dokümanına
  at-least-once notu + `JobHandlerContract` + `JobLeaseExpiryTests`. Faz 119
  bu kapsamı GENİŞLETMEDİ.

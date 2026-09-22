# Faz 181 — Provider Ortak Katmanı

> **Durum:** ✅ Tamamlandı (2026-09-22)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı; mimari seçim: shared-source, paket yok)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-258**
> **Önkoşul:** Yok
> **Paketler:** `Tracon.OpenAI`, `.Anthropic`, `.Google`, `.Azure` (+ yeni shared-source ağacı)
> **Yeni paket:** **Yok** — `src/Tracon.Providers.Shared/` bir paket değildir; `Tracon.Sql.Shared` emsali ([Tracon.SqlServer.csproj:52](../../../src/Tracon.SqlServer/Tracon.SqlServer.csproj)) gibi `<Compile Include>` ile derlemeye kopyalanır
> **Migration:** Yok
> **Public API:** Büyümüyor — kural: ortak kod `internal` yardımcı + kompozisyon; public hiyerarşi değişmez
> **Tüketici yüzeyi:** Plan: yok. Gerçekleşen: dört plan dışı kusur düzeltmesi tüketiciye görünür (bkz. Plandan Sapmalar) → `CHANGELOG.md` `[Unreleased]` · `guides/model-providers.md` sorun giderme girdisi
> **Manuel test alanı:** `docs/manuel-test/05-SAGLAYICI-OPENAI.md` · `06-SAGLAYICI-DIGER.md` — mevcut case'ler regresyon görevi görür

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 3c83ae75:docs/arsiv/fazlar/181-PROVIDER-ORTAK-KATMANI.md
> ```
>
> Damıtıldı 2026-09-22 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Dört sağlayıcı paketi aynı gövdeyi elle kopyalıyor. K-646 bu kopyanın bedelini ölçtü: bir BYOK-cache kusuru **dört yerde ayrı ayrı** düzeltildi. K-483 sınıfı (elle tekrarlanan ifadeye terim eklemek) burada dört kat geçerli. Bu faz ortak gövdeyi `Tracon.Sql.Shared` emsalindeki gibi tek shared-source ağacına indirir; davranış birebir korunur, public yüzey değişmez.

## Bitiş Ölçütleri (DoD)

- [x] `sed`+`diff` ölçümü tekrarlanır ve üç dosya çiftinde farklı-satır sayısı yalnız gerçek sağlayıcı farkını içerir (auth · endpoint · ayrıştırma); ölçüm kapanışa yazılır — **Süreç Ölçümü**'nde; kalan farklı kod satırlarının hepsi sağlayıcı farkıdır (SDK seçenekleri, auth başlığı, endpoint, gövde biçimi, Azure'un deployment log metni)
- [x] Dört paketin `PublicAPI.Unshipped.txt` dosyalarında net satır değişimi 0 — `git diff --stat ed861301 -- 'src/*/PublicAPI.*'` boş; `Tracon.Core`'da da 0 (cache logger'ı `internal init`)
- [x] Mevcut sözleşme, credential ve secret-leak testleri **değiştirilmeden** yeşil — test dosyalarında yalnız ekleme var (denetçi `--numstat` ile doğruladı); OpenAI 126→169 · Anthropic 80→121 · Google 84→125 · Azure 76→120, hepsi yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban ed861301` (sonuç: Süreç Ölçümü altındaki not)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — **Örnek Uygulama Koşumu**
- [x] `secret` taraması boş döndü — `kapi.py tarama`: `✅ temiz (17 işaretli sentetik credential atlandı)`
- [x] Manuel kabul: sağlayıcı smoke case'leri koşuldu, sonuç belgede — **Örnek Uygulama Koşumu**
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 🔴 yok, üç 🟡 kapandı

### Doğrulama komutları

```bash
for p in OpenAI Anthropic Google Azure; do git diff --stat src/Tracon.$p/PublicAPI.Unshipped.txt; done
python3 scripts/kapi.py test --proje Tracon.Anthropic.UnitTests --sinif "*Contract*"
```

---

## Plandan Sapmalar

| Plan | Gerçek | Gerekçe |
|---|---|---|
| 181.2: "public sınıflar internal tabandan türeyemez (CS0060), bu yüzden kompozisyon" | **İddia yanlış** — `*ModelProvider` ve `*ProviderHealthCheck` zaten `internal`; kalıtım mümkündü. Kompozisyon **yine de** korundu | Ölçüldü: `grep "internal sealed class .*ModelProvider"`. Kompozisyon kabukların tipini, tabanını ve kurucu imzalarını değiştirmedi; mevcut testler bu imzalara bağlı. Kararın gerekçesi yanlıştı, kararın kendisi değil |
| 181.2: `internal static class ModelProviderCore` | `ModelProviderCore<TFactory>` (örnek durumu: katalog kümesi, iki BYOK önbelleği, guard) + statik `ModelProviderCore` (durumsuz kurallar) | Önbellekler sağlayıcı örneği başınadır; statik sınıf onları tutamaz. İkiye bölmek çağıranın statik çağrıda fabrika tipini yazmasını önler |
| Plan: shared-source'ta XML doc yazımı tanımsız | `///` kullanıldı; önce ölçüldü | F-76/K-352 sınıfı riski (dört derlemede aynı `<member>` id'si → `/openapi/v1.json` 500). **Ölçüldü:** id'ler dört XML'de ortak ama `XmlCommentGenerator` yalnız public üye emit ediyor; `OpenApiDocumentTests` ve örnek uygulamada `/openapi/v1.json` 200. Kural README'de: ağaçtaki her tip `internal` |
| "Davranış birebir korunur" | **Dört bilinçli davranış farkı** — hepsi `CHANGELOG.md` `[Unreleased]`'da | (1) Azure `BindModels` adsız deployment'ı atlıyor, `Endpoint` okuması göreli değeri `UriKind.Absolute` ile düşürüyordu; diğer üç kopya HATA-S3-002/003/004'te düzeltilmişti — ortak kod Azure'a da uyguladı, iki düşen test önce yazıldı. (2) Google sağlık detayı `Response is not valid JSON.` → `The response is not valid JSON.` (dördü aynı). (3) Katalog dışı log metni üç sağlayıcıda ortaklaştı (`option` → `setting`). (4) `UseOpenAICompatible()` sağlayıcısının log'u `Tracon:Providers:OpenAI:Models`'i gösteriyordu — o sağlayıcı o bölümü okumaz; artık `OpenAIProviderOptions.Models` der (taban üzerinde worktree'de kırmızı kanıtlandı) |
| Planda yok | **Plan dışı kusur (kusur-giderme):** istisna atan bir sağlık denetimi `/api/models/health`'i tüm sağlayıcılar için düşürüyordu | Azure `TokenCredential` istisnası `CheckHealthAsync`'ten kaçıyor, `ModelProviderHealthCache.GetAllAsync` yakalamıyordu. İki katmanda kapandı (K-848). Düşen iki fonksiyonel test önce yazıldı ve kırmızı görüldü. **Sınıf taraması:** sağlayıcı başına toplayan başka tek yer `TraconDiagnosticsCollector` — önbellekten `TryPeek` okur, ağ çağrısı yapmaz; `VoiceEndpoints` tek sağlayıcıyı denetler, liste değildir. Başka vaka yok |
| Planda yok | `scripts/kapi.py`: `src/Tracon.Providers.Shared/` ve `tests/Shared/Providers/` dört provider test projesini seçer | Eşleme olmasa paket haritasında olmayan yol tam koşuma düşerdi; `tests/Shared/` de öyle. `kapi_test.py`'ye iki test |
| Açık Soru 1 | **A** uygulandı: `ProviderRegistrationCore` = options + validator kaydı, "zaten kayıtlı mı" + yapılandırma okuyucuları + `BindModels` | B (fabrika kurulumu) OpenAI'nin iki sağlayıcı kaydı yüzünden simetrik değil — ölçüm önerisini doğruladı |
| Açık Soru 2 | **B** uygulandı: katalog dosyalarına dokunulmadı | — |

## Bu Fazda Verilen Kararlar

- **K-848** — istisna atan `IModelProviderHealthCheck` listeyi düşürmez; `detail` yalnız tip adı, istisna Warning log'a (public API değişmeden: `internal init` logger)
- **K-849** — provider ortak gövdesi shared-source, paket yok, her tip `internal` (kullanıcı kararı: plan onayı)

Yerel tercihler (defter dışı): Azure kendi deployment log metnini tutar (MODEL/DEPLOYMENT uyarısı sağlayıcı farkıdır) · paylaşılan testler dört test projesine bağlanır, her derlemenin kopyası ayrı koşar.

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, PostgreSQL, gerçek anahtarlar (2026-09-22):

| Case / komut | Gerçek çıktı |
|---|---|
| `MT-OAI-070` · `MT-PROV-060` — `GET /api/models/health?refresh=true` | `anthropic Healthy (12 model)` · `google Healthy (50)` · `openai Healthy (7)` · `openai-responses Healthy (7)` · `openrouter Healthy (200 — üst sınır)`; üç auth biçimi paylaşılan gövdeden geçti |
| F-76 sınıfı — `GET /openapi/v1.json` | `200` (dört provider XML'i ortak `internal` id taşırken) |
| Gerçek `run` — `claude-support` · `gemini-support` · `support` | üçü `Completed`; 751 · 226 · 352 token; SSE `run` → `update`… → `done` |
| `MT-PROV-021` — katalog dışı `claude-sonnet-4-6` | run tamamlandı; log: `Model 'claude-sonnet-4-6' is not in the 'anthropic' catalog; the request is sent anyway. Use the Tracon:Providers:Anthropic:Models setting to add the model to the catalog.` |
| `MT-OAI-021` sınıfı — katalog dışı `openrouter` modeli | log: `... Use the OpenAIProviderOptions.Models setting ...` (düzeltmeden önce `Tracon:Providers:OpenAI:Models` derdi) |
| `MT-OAI-092` · `MT-PROV-071` — `GET /api/diagnostics` | `configuration`: üç `Tracon:Providers:*:ApiKey` `resolved: true`, `hint: null`, değer yok; `openrouter` için satır yok (K-249) |
| Azure ailesi (`MT-PROV-080…088`) | ⏭ — makinede Azure kimliği yok (2026-09-16 turundaki durum); davranış `AzureOpenAIProviderHealthCheckWireTests` + iki fonksiyonel testle kanıtlı |

Denemede açılan iki agent silindi (`204`).

### Site senkronu gerekçesi (`--site-denetle`)

Değişen sayfa: `guides/model-providers.md` (Azure `Credential error` sorun giderme girdisi) + üretilen `llms-full.txt`. Karşılanmayan iki kural bilinçli:

- **`cekirdek-kavram` → `concepts/`**: tetikleyen `ModelProviderHealthCache.cs`; değişen şey sağlayıcı sağlık listesinin hata davranışıdır, bir kavram değil. Tüketici metni `guides/model-providers.md`'de ve `CHANGELOG.md`'de; HTTP ucu açıklaması ("status Unknown… detail does NOT include…") hâlâ doğru.
- **`paket-tanimi` → `packages.md`**: dört provider `.csproj`'u yalnız `<Compile Include>` satırı kazandı; paket kimliği, bağımlılık ve açıklama değişmedi.

Bu kapanışta kuralın iki yanlış pozitifi düzeltildi (`dokuman_bakim_test.py` +2): `*.Shared` README'si paket README'si sayılmaz; `model-saglayici` kuralı `Providers.Shared`'ı kapsar ve `guides/model-providers.md` ile de karşılanır.

## Süreç Ölçümü

**Kopya ölçümü** (`sed` ad normalizasyonu, yorum ve boş satır hariç **kod satırı**; `toplam / farklı`):

| Dosya çifti (Anthropic ↔ …) | Önce | Sonra |
|---|---|---|
| `*ModelProvider.cs` ↔ Google · Azure · OpenAI | 115+115/12 · 115+117/32 · 115+118/23 | 77+76/5 · 77+88/25 · 77+83/24 |
| `*ProviderExtensions.cs` ↔ Google · Azure · OpenAI | 127+123/8 · 127+130/31 · 127+139/46 | 87+83/8 · 87+86/23 · 87+99/46 |
| `*ProviderHealthCheck.cs` ↔ Google · Azure · OpenAI | 119+124/41 · 119+138/51 · 119+121/14 | 31+34/19 · 31+52/53 · 31+30/9 |

On iki dosyanın kod satırı **1.486 → 826**; ortak ağaç 294 kod satırı. Kalan ortak satırlar arayüz imzaları, kurucu argüman denetimleri ve çekirdeğe çağrılardır — kabuğun kendisi. Kalan **farklı** satırların hepsi sağlayıcı farkıdır (SDK seçenekleri, auth başlığı, endpoint, gövde biçimi, Azure'un deployment metni, OpenAI'nin iki yüzeyi). Yorum dahil ham ölçüm (planın yöntemi): `ModelProvider` A↔G 199+199/40 → 138+140/34 · `Extensions` 216+212/60 → 157+153/46 · `HealthCheck` 186+190/82 → 75+77/62; kalan ham fark çoğunlukla public XML doc metninin sağlayıcıya göre farklı yazımıdır.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 |
| Düzeltme turu sayısı | 1 (denetim sonrası üç 🟡) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 — `ShippedDocumentationSelfContainmentTests` ilk koşumda `///` içindeki faz/K referanslarını yakaladı, kapanıştan önce düzeltildi |
| Faz kapandıktan sonra bulunan kusur | — |

## Denetim Bulguları

Bağımsız denetçi (`faz-denetcisi`, taze bağlam): **🔴 yok**, üç 🟡, iki 🟢. 🔴 olmadığı için kullanıcı triyajı gerekmedi.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Yeni iki `catch` istisnayı log'lamıyordu; eskiden arka plan servisi Warning + stack trace yazıyordu (gözlemlenebilirlik gerilemesi) | **Düzeltildi.** `ProviderHealthCheckCore` sağlayıcının kendi logger'ını alır; `ModelProviderHealthCache` `internal init` logger ile DI fabrikasından bağlanır (public API değişmedi). Testler: `Credential_failure_reaches_the_log_with_its_exception` (×4 derleme) · `ModelProviderHealthCacheThrowingCheckTests` (gerçek DI kaydıyla) |
| 2 | 🟡 | Tüketiciye görünen davranış değişiklikleri sürüm notunda yoktu; başlık "Tüketici yüzeyi: Yok" diyordu | **Düzeltildi.** `CHANGELOG.md` `[Unreleased]` Fixed/Changed; başlık satırı düzeltildi; sapma tablosu |
| 3 | 🟡 | Manuel case metni log'un "dört sağlayıcıda ortak" olduğunu söylüyordu; Azure kendi metnini tutuyor | **Düzeltildi.** `MT-OAI-021` · `MT-PROV-021` |
| 4 | 🟢 | `ic-dongu` paylaşımlı kaynak değişince HTTP seviyesindeki tek kanıtı (`Tracon.AspNetCore.FunctionalTests`) seçmiyor | **Devredildi → F-263** |
| 5 | 🟢 | Azure kimlik hatası için kimlik gerektirmeyen manuel case yazılabilir | **Devredildi → F-264** |

Denetçinin doğruladıkları: mevcut test dosyalarında yalnız ekleme · `PublicAPI` net 0 · ortak ağaçta `#if`, SDK `using`'i ve public tip yok · dört wire testi karşı sağlayıcının başlığının **gitmediğini** de gösteriyor · `Every_ModelDescriptor_property_is_bound_from_configuration` K-483 sınıfına yansıma korumasıdır (uygulayan oturum bir alanı silerek kırmızı gördü).

## Sonraki Faza Devir Notu

**Provider paketlerinde bir kural dört pakette aynıysa `src/Tracon.Providers.Shared/`'a yazılır.** Kabuklarda yalnız sağlayıcı farkı kalır. Ağacın kuralları `README.md`'dedir; üçü tuzaktır:

- 🚨 **Ağaçtaki her tip `internal` kalır.** Public bir tip dört derlemede aynı adla var olur: iki provider'a bağlı tüketicide `CS0433`, dört `PublicAPI` satırı. XML doc id'leri dört dosyada ortaktır; bu **yalnız** `internal` olduğu için zararsızdır (OpenAPI üreteci public üye emit eder — ölçüldü).
- 🚨 **Sevk edilen `///` metnine faz/K/HATA numarası yazılmaz** — `ShippedDocumentationSelfContainmentTests` yakalar. Kayıt referansı `//` yorumunda kalır (ağaçtaki `// Record:` satırları).
- 🚨 **Kopyalardan birini düzelten iş diğerlerini `diff`'lemelidir.** Bu faz Azure'da üç kopyanın aldığı ama Azure'un almadığı iki düzeltme buldu. Hâlâ kopya olan yerler: `*ProviderOptionsValidator.cs` (dört adet; üçü göreli endpoint'i mesajda yazıyor, Azure yazmıyor — topoloji gerekçesi Azure'a özgü mü, ölçülmedi) ve `*ModelCatalog.cs` (Açık Soru 2 = B).

**Faz 182'ye:** provider public yüzeyi değişmedi; envanter için ayrıntı `docs/182-*.md` "Bu Faza Başlarken" 5. maddesinde.

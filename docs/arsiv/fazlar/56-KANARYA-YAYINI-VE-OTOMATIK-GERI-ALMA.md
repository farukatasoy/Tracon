# Faz 56 — Kanarya Yayını ve Otomatik Geri Alma

> **Durum:** ✅ Tamamlandı (2026-08-09)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-74**
> **Önkoşul:** [Faz 19](19-SURUM-KARSILASTIRMA-VE-AB.md) (deney altyapısı) · [Faz 31](31-GERI-BILDIRIM-VE-PUANLAMA.md) · [Faz 44](44-HATA-SINIFLANDIRMA.md) · [Faz 49](49-CEVRIMICI-DEGERLENDIRME.md) — üçü de **tamamlandı**
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.AspNetCore`, `Tracon.Sql.Shared`, `Tracon.UI`
> **Yeni paket:** Yok · **Migration:** gerekli — `experiments` tablosuna eşik alanları, numara uygulama anında alınır
> **Public API:** büyüyor — `Experiment`'a alanlar + bir ayar sınıfı. 🚨 `Experiment` bir `sealed record`; Faz 7'den **sonra** alan eklemek sürüm kararı olurdu

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 19 A/B deneyini verdi ama **kimse sonuca göre karar vermiyor**. Varyant başına hata oranı hesaplanıyor; bu sayıyı okuyup deneyi durduran hiçbir kod yok. Trafik oranı sabittir ve hata oranı patlarsa deney kendiliğinden durmaz. - **F-74** — deneye eşik kuralı (hata oranı > X veya puan < Y ise durdur), kademeli trafik artırma, otomatik geri alma ve denetim kaydı.

## Kapatılan Açık Sorular

| # | Soru | Seçenekler | Öneri | Gerçekleşen karar |
|---|---|---|---|---|
| 1 | Eşik mutlak mı göreceli mı? | A: `ErrorRate > X` · B: `ErrorRate - kontrol > X` | **B** | **B**, önerildiği gibi. `CanaryEvaluator` `canaryRate - controlRate > maxDelta` hesaplar. |
| 2 | Puan kaynağı hangisi? | A: Faz 49'un çevrimiçi eval puanı · B: Faz 31'in kullanıcı geri bildirimi · C: İkisi de | **A** | **A**, önerildiği gibi. `ExperimentVariantResult.AverageScore` yalnız `RunScoreKind.Numeric` (Faz 49) puanlarını sayar. |
| 3 | Kademeli artırma bu fazda mı? | A: Evet · B: Ayrı kalem | **A** | **A**, önerildiği gibi. `RampSteps` boşken hiç çalışmaz (ölçüldü, `CanaryRampTests` benzeri testler). |
| 4 | Geri alma sonrası yeniden başlatma otomatik mi? | A: Elle · B: Belirli süre sonra otomatik | **A** | **A**, önerildiği gibi. Otomatik yeniden başlatma yazılmadı. |
| 5 | Değerlendirme penceresi Faz 49'unkiyle aynı mı? | A: Aynı ayar · B: Ayrı ayar | **B** | **Sapma — C: hiçbir pencere yok.** K-376: `ExperimentVariantResult` deney başından beri biriken tüm-zamanlı veriyi taşır; kanarya kolunun "geçmişi" olmadığı için Faz 49'un pencere kavramı yapısal olarak uygulanamaz oldu. Gerekçe K-376'da. |

Ek olarak, planın "Planlanan Public API" kod örneğinde yoktu ama düz yazısında geçen
`RampSteps` alanı `RampRequiresSampleSize` ile karıştırılmıştı (K-375) — bu ikinci
alan hiç açılmadı, `MinSampleSize` yeniden kullanıldı.

---

## Bitiş Ölçütleri (DoD)

- [x] Kanarya kolu kontrolden eşik kadar kötüyse deney **otomatik durur** ve ağırlıklar kontrole döner — `CanaryEvaluationServiceTests.Kanarya_kontrolden_esik_kadar_kotuyse_otomatik_geri_alinir`
- [x] Kontrol de kötüyse kanarya **durdurulmaz** (göreceli karşılaştırma çalışıyor) — `CanaryEvaluatorTests.Kontrol_de_kotuyse_gorece_karsilastirma_geri_almaz`
- [x] `MinSampleSize`'a ulaşılmadan **hiçbir** karar verilmez — `CanaryEvaluatorTests.MinSampleSizeya_ulasilmadiysa_karar_verilmez`, `CanaryEvaluatorTests.Kanarya_kolu_hic_calismamissa_karar_verilmez`
- [x] `AutoRollbackEnabled = false` (varsayılan) iken hiçbir deney durdurulmaz — `CanaryEvaluationServiceTests.AutoRollbackEnabled_kapaliyken_hicbir_deney_taranmaz_ve_durdurulmaz` (`ListRunningWithCanaryAsync` sıfır kez çağrılıyor, **hiç** sorgu atılmıyor)
- [x] Her otomatik karar `audit_log`'da görünür; yazılamazsa karar uygulanmaz — `CanaryEvaluationServiceTests.Kanarya_kontrolden_esik_kadar_kotuyse_otomatik_geri_alinir` (audit kaydı doğrulanıyor) + `Denetim_izi_yazilamazsa_geri_alma_uygulanmaz` (K-089); gerçek host'ta `GET /api/audit?action=experiment.canary_policy` ile de doğrulandı (bkz. aşağı)
- [x] Kademeli artırmada var olan oturumlar kolunu **değiştirmez** (ölçüldü) — `CanaryEvaluationServiceTests.Kademeli_artirma_...` + `ExperimentAssignmentResolverTests.Kanarya_agirligi_buyudukce_...`. 🚨 Bu iddia ilk yazılışta **yanlıştı** — bkz. K-374 ve Plandan Sapmalar
- [x] Değerlendirme iki örnekli kurulumda yalnız birinde koşar — `CanaryEvaluationServiceTests.Iki_ornekte_degerlendirme_yalniz_birinde_kosar`
- [x] Değerlendirici `SchemaReadyGate`'i bekler (K-354) — `CanaryEvaluationService.ExecuteAsync`, `ApprovalExpirationService` ile birebir aynı desen
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/`format --verify-no-changes` hepsi temiz (2026-08-09)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. aşağı, gerçek `gpt-5.4-mini` çağrılarıyla
- [x] `secret` taraması boş döndü (bu fazın dokunduğu dosyalarda; iki ön-var eşleşme `MsSqlBuilder.DefaultPassword` kod-örneğidir, Faz 56'dan bağımsız)
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı **ölçüldü** ve yazıldı — `javascript: 164.4 KB gzipped (budget 250 KB)`, `embedded: 141.5 KB brotli`; `i18n.test.ts` (identicalOnPurpose listesi dahil) yeşil

### Doğrulama komutları

```bash
# Kanarya kurali tanimla
curl -s -X PUT http://localhost:5080/tracon/api/experiments/yeni-talimat/canary \
  -H "Content-Type: application/json" \
  -d '{"canaryVariant":"treatment","maxErrorRateDelta":0.10,"minSampleSize":20,"rampSteps":[5,25,50,100]}'

# Deneyin durumu ve geri alma nedeni
curl -s http://localhost:5080/tracon/api/experiments/yeni-talimat | jq '.status, .rollbackReason'

# Denetim izinde karar (ucta bir zarf YOKTUR, duz dizi doner)
curl -s "http://localhost:5080/tracon/api/audit?action=experiment.auto_rollback" | jq '.[0]'
```

**Gerçek koşum (2026-08-09, `samples/Tracon.Api`, bellek içi depolar, gerçek `openai`/`gpt-5.4-mini` çağrıları):**

```
PUT /api/experiments/demo-canary/canary  -> 200, canary.canaryVariant = "canary"
GET /api/experiments/demo-canary/canary  -> 200, evaluation.decision = "InsufficientData"
                                             (henuz calistirma yok)
POST /api/experiments/demo-canary/start  -> 200, status = "Running"
# 13 gercek run (7 control, 6 canary), farkli X-Session-Id ile
GET /api/experiments/demo-canary/results -> control: totalRuns=7 errorRate=0
                                             canary:  totalRuns=6 errorRate=0
GET /api/experiments/demo-canary/canary  -> evaluation.decision = "Healthy",
                                             reason = "Kanarya kontrolden esik kadar kotu degil."
GET /api/audit?action=experiment.canary_policy -> 2 kayit (Set + guncelleme),
                                             before/after tam Experiment govdesi tasiyor
```

Otomatik geri alma ve kademeli artırma yolları gerçek bir hata/geniş örneklem üretmenin
maliyeti (canlı model çağrısı) yüzünden bu koşumda **tetiklenmedi** — o yollar
`CanaryEvaluationServiceTests` içinde sentetik veriyle uçtan uca (StartRunAsync →
CompleteRunAsync → gerçek arka plan servisi → gerçek SQL/bellek içi depo) zaten
kanıtlanmıştır; bu koşumun amacı yalnızca gerçek HTTP/DI/serileştirme zincirinde
kablo hatası olmadığını göstermekti.

---

## Plandan Sapmalar

1. **İki kollu kısıt eklendi** (K-373): plan "kalan kollar kontrol sayılır" diyordu (çoğul); uygulama kanaryayı yalnız İKİ kollu deneylerde tanımlanabilir kıldı. Gerekçe K-373'te — N kollu bir deneyde kontrol kolları arasındaki sınırın oturum kararlılığı genel halde kanıtlanamaz.
2. **`RampRequiresSampleSize` açılmadı** (K-375): planın 56.4 düz yazısı bahsediyordu ama "Planlanan Public API" kod örneği hiç içermiyordu — plan kendi içinde tutarsızdı. Kademeli artırma da `CanaryPolicy.MinSampleSize`'ı aynen kullanır.
3. **Ayrı bir `EvaluationWindow` açılmadı** (K-376, Açık Soru 5'in "B" önerisinin aksine): kanarya kararı `ExperimentVariantResult`'ın deney başından beri biriken tüm-zamanlı verisine dayanır. Gerekçe: bir kanarya kolunun "geçmişi" yoktur, ilk çalıştırmasından bugüne her şey zaten güncel veridir; Faz 49'un penceresi sürekli çalışan bir agent için anlamlıdır, kanarya için değil.
4. **`ExperimentVariantResult.AverageScore` eklendi** (K-377, plan dosya listesinde yoktu): `CanaryPolicy.MinScore` kararının kanıtı olmadan test edilemezdi; `SelectExperimentResults` `run_scores`'a iki aşamalı bir CTE ile genişletildi (Postgres/SQL Server/SQLite üçünde de).
5. **`ExperimentAssignmentResolver.SelectVariant` değiştirildi** (K-374, plan bunu öngörmüyordu): kova aralığı hesaplaması artık kanarya kolunu HER ZAMAN ilk sıraya alır. Bu değişiklik olmadan 56.4'ün "var olan oturumlar kolunu değiştirmez" iddiası **yanlıştı** — kendi yazdığım testler bunu kırmızı olarak yakaladı, ayrıntı için K-374 ve `docs/hafiza/cekirdek-calistirma.md`.
6. **Otomatik yeniden başlatma sorusu (Açık Soru 4) planın önerdiği gibi "A: elle" olarak bırakıldı** — sapma değil, plan zaten böyle öneriyordu; burada teyit için not edilir.
7. **Gerçek koşumda otomatik geri alma/kademeli artırma TETİKLENMEDİ** (bkz. DoD doğrulama notu): canlı bir OpenAI çağrısıyla gerçek bir hata veya 20+ örneklik trafik üretmenin maliyeti bu koşum için haklı görülmedi; o yollar `CanaryEvaluationServiceTests` içinde sentetik veriyle gerçek arka plan servisiyle uçtan uca kanıtlanmıştır.

## Bu Fazda Verilen Kararlar

K-373 ile K-379 arası `docs/KARARLAR.md`'ye eklendi:

- **K-373** — Kanarya yalnız iki kollu deneylerde tanımlanabilir.
- **K-374** — 🚨 `SelectVariant` kanarya kolunu fiziksel sıradan bağımsız her zaman ilk sıraya alır (bir gerçek kusuru düzeltir).
- **K-375** — `RampRequiresSampleSize` açılmadı, `MinSampleSize` yeniden kullanılır.
- **K-376** — Ayrı bir `EvaluationWindow` açılmadı.
- **K-377** — `ExperimentVariantResult.AverageScore` eklendi; iki aşamalı ortalama CTE'si.
- **K-378** — Ramp adımı denetlenmez, yalnız geri alma K-089 emsaliyle denetlenir.
- **K-379** — `SetCanaryPolicyAsync` `SaveAsync`'in Draft kısıtından muaf.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**

- `CanaryPolicy`/`CanaryEvaluation`/`CanaryDecisionKind` (Abstractions) — `Experiment.Canary` ve `Experiment.RollbackReason` `sealed record`'un parçası; Faz 7'den sonra alan eklemek sürüm kararı olur (aynı uyarı hâlâ geçerli, bu fazda kullanılmadı).
- `IExperimentStore`'un 4 yeni metodu (`ListRunningWithCanaryAsync`, `SetCanaryPolicyAsync`, `AdvanceCanaryRampAsync`, `RollbackCanaryAsync`) — yeni bir `IExperimentStore` uygulaması (bugün yok) bunları da uygulamalıdır.
- `PUT /api/experiments/{name}/canary` **yalnız iki kollu deneylerde** 200 döner (K-373) — üç veya daha fazla kollu bir deneyde her zaman 400.
- `ExperimentAssignmentResolver.SelectVariant`'ın kanarya-ankraj davranışı (K-374): bir `Experiment.Canary` tanımlıysa kova hesaplaması `Variants` sırasından bağımsızdır. Bu genel bir kural DEĞİLDİR — yalnız kanarya kolu için geçerlidir; sıradan (kanaryasız) A/B deneylerinde davranış Faz 19'daki gibi kalır.

**Bilinen tuzaklar (🚨):**

- Bir kolun ağırlığı **zamanla değişebiliyorsa** (rampa gibi), o kolun kova aralığı listedeki fiziksel konumundan bağımsız, sabit bir uca ankorlanmalıdır — aksi halde ağırlık değişimi listedeki DİĞER kolların da sınırını kaydırır ve var olan oturumlar sessizce kol değiştirir. Bu fazda gerçek bir kusur olarak yaşandı (K-374); yeni bir "zamanla ağırlığı değişen kol" özelliği yazan biri aynı tuzağa düşebilir.
- `CanaryPolicy.RampInterval`in "son ne zaman değişti" ölçütü `Experiment.UpdatedAt`'tir — yeni bir alan açılmadı. `SetCanaryPolicyAsync` de `UpdatedAt`'i günceller; bir kanarya kuralını deney Running iken elle düzenlemek bu yüzden bir sonraki ramp adımını `RampInterval` kadar geciktirir. Bu kabul edilebilir bir yan etkidir, ama şaşırtıcı olabilir.
- `ExperimentVariantResult.AverageScore` yalnız `RunScoreKind.Numeric` (Faz 49 online eval) puanlarını sayar; Faz 31'in insan puanları (`Binary`/`Stars`) dahil DEĞİLDİR (Açık Soru 2'nin "A" kararı — plan böyle öneriyordu).
- Kanarya kararı `ExperimentResultsQuery`'nin AYNI sonuç setini kullanır; bir deneyin `results` ucu ile `canary.evaluation.canary`/`canary.evaluation.control` alanları HER ZAMAN tutarlıdır (aynı sorgu, aynı an) — ayrı bir önbellek YOKTUR.
- SQL Server sözleşme testleri bu makinede (Apple Silicon) çalıştırılamadı (`mcr.microsoft.com/mssql/server` Rosetta hatası veriyor, bkz. `docs/hafiza/sql-server-yerel-test.md`) — SQL Server'a özgü T-SQL, Faz 44'ün kanıtlanmış `COL_LENGTH`/`EXEC`-sarmalı desenine BİREBİR uyularak yazıldı ama gerçek bir SQL Server'da DOĞRULANMADI. Postgres ve SQLite'ta 27/27 sözleşme testi geçti.

**Sıradaki faz:** Bu faz `docs/arsiv/UCUNCU-FAZ-YOL-HARITASI.md`'nin dördüncü dalgasının son planlı kalemiydi (53–56). Sıradaki kalem için `docs/ADAYLAR.md`'ye ve README'nin yol haritası tablosuna bakın.

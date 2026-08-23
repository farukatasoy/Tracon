# Faz 49 — Çevrimiçi Değerlendirme (üretim trafiğinde yargıç)

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-71**
> **Önkoşul:** 🚨 [Faz 31](31-GERI-BILDIRIM-VE-PUANLAMA.md) — `run_scores` tablosu ve `IRunScoreStore` oradan gelir. **Bu faz kendi puan tablosunu AÇMAZ**
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** **Yok** — puan tablosu Faz 31'indir; bu faz yalnız `RunScoreKind`'a bir üye ekler
> **Public API:** büyüyor — bir arayüz, bir `JobKind` üyesi, bir `RunScoreKind` üyesi, iki ayar sınıfı, iki kayıt tipi. Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/49-CEVRIMICI-DEGERLENDIRME.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Faz 48'den Devralınanlar

> Bu bölüm Faz 48'in kapanışında eklendi. Sonraki oturum bu fazı ayrı bir
> sohbette uygulayacaksa bu üç maddeyi bilmek zorundadır.

### D1 — 🚨 Yargıcın `IChatClient`'ı da guard'dan geçer

`IRunJudge` bir model çağırır ve o çağrı `IModelProviderRegistry.CreateChatClient`
ile kurulursa **kayıtlı her `IContentGuard` yargıç isteminde de çalışır.** Sonuç:

| Durum | Ne olur |
|---|---|
| Guard, puanlanan çalıştırmanın metnini **engellerse** | Yargıç çağrısı `AgentPrismContentBlockedException` ile düşer. Puanlanan çalıştırma başarılıydı; puanlama başarısız olur |
| Guard, metni **maskelerse** | Yargıç maskelenmiş metni puanlar ve puan **anlamsızlaşabilir** — maskelenen şey tam olarak değerlendirilen içerikti |

Bu, bu fazın **karara bağlaması gereken** bir noktadır ve plan bunu öngörmedi.
Üç seçenek görünür: (a) yargıç istemcisini guard'sız kurmak (boru hattı defterin
içinde olduğu için bunun için açık bir yol gerekir), (b) engellemeyi puanlama
başarısızlığı sayıp `RunScoreKind`'a yazmamak, (c) hiçbir şey yapmayıp davranışı
belgelemek. **Seçim gerekçesiyle `KARARLAR.md`'ye yazılmalıdır.**

### D2 — 🚨 Boru hattına halka eklemenin yeri değişti

`UseFunctionInvocation()` ve `UseOpenTelemetry()` artık sağlayıcı paketlerinde
**değil**, `ModelProviderRegistry.CreateChatClient` içindedir (K-320);
`IModelProvider` **ham** istemci döndürür. Bu faz model çağrı yoluna bir halka
eklerse (örnek: yargıç maliyetini ayrı ölçen bir sarmalayıcı) onu **orada** kurar
ve şu soruyu yanıtlar: *her model çağrısını görmesi gerekiyor mu?* Gerekiyorsa
tool döngüsünün içine, agent turu başına bir kez yetiyorsa dışına.

Bugünkü sıra (dıştan içe): içerik filtresi tespiti → devre kesici → ek çözme →
`FunctionInvokingChatClient` → OpenTelemetry → içerik guard'ı → ham istemci.

### D3 — `RunErrorClass` ve `RunEventType`'a üye eklendi

`RunErrorClass.ContentBlocked = 11` ve `RunEventType.{ContentMasked = 20,
ContentBlocked = 21}`. İkisi de `smallint` sütunda saklanır; bu faz aynı enum'lara
dokunacaksa değerleri **sona** eklemelidir.

🚨 **Ölçüldü (2026-08-07): `RunScoreKind` yalnız `Binary = 1` ve `Stars = 2`
taşıyor.** Yol haritası `Numeric` üyesinin **Faz 31'e** taşınmasını istiyordu ama
Faz 31 onu eklemedi; bu fazın kendisi eklemek zorundadır ve değer **`3`** olmalıdır
(`smallint` sütunda saklanır, mevcut değerler kaydırılamaz). Kaynak:
[`RunScoreKind.cs`](../../../src/AgentPrism.Abstractions/Runs/RunScoreKind.cs).

---

## Amaç

Bugün kalite yalnız **elle yazılmış eval takımlarında**, yalnız **tetiklenince** ölçülüyor. Üretim trafiği hiç puanlanmıyor. Bir talimat değişikliği kaliteyi düşürürse bunu ilk fark eden kullanıcıdır. Bu faz, örneklenmiş üretim çalıştırmalarını arka planda puanlar.

## Bitiş Ölçütleri (DoD)

- [x] 🚨 Varsayılan ayarlarla (`Enabled = false`, `SampleRate = 0`) yargıç
      modeli **hiç çağrılmaz** ve tek kuruş harcanmaz — hem birim testiyle
      (`OnlineEvalDisabledTests`/`Varsayilan_ayarlarla_hicbir_sey_orneklenmez`)
      hem `samples/AgentPrism.Api`'nin varsayılan appsettings'iyle (aşağıda)
      doğrulandı
- [x] `SampleRate = 1.0` ile her tamamlanan çalıştırma puanlanır ve puan
      `run_scores`'a `Source = judge:{ad}` ile yazılır — gerçek OpenAI
      çağrısıyla doğrulandı (aşağıda)
- [x] 🚨 `MaxScoresPerHour` aşılınca örnekleme durur —
      `RunSamplerTests.MaxScoresPerHour_asilinca_ornekleme_durur`
- [x] 🚨 Yargıcın kendi çalıştırması yeni bir yargıç işi **açmaz** —
      `RunSamplerTests.Eval_turundeki_calistirma_orneklenmez` (yargıç
      `RunKind.Eval` ile kaydedilir, örnekleyici bu türü koşulsuz atlar)
- [x] 🚨 Yargıç çalıştırması `RunKind.Eval`'dir ve agent'ın `RunStatistics`
      maliyetine **girmez**; `GET /api/runs?kind=Eval` ile **görünür** —
      gerçek koşumda `support` agent'ının `totalCost`'u etkilenmedi
- [x] 🚨 Yargıç karar veremezse puan **yazılmaz** — sessiz `0` yazılmaz —
      `ModelRunJudgeTests.Null_score_null_olarak_kalir`,
      `OnlineEvalJobHandlerTests.Karar_verilemeyen_yargic_sessiz_sifir_yazmaz`
- [x] 🚨 Yargıç hatası puanlanan çalıştırmayı etkilemez; `runs` satırı değişmez —
      `OnlineEvalJobHandlerTests.Bir_yargic_hata_verirse_is_geri_adimli_yeniden_denenir_digeri_yine_de_yazar`
- [x] Ortalama eşiğin altında **ve** örnek sayısı yeterliyse `run.score.low`
      webhook'u tetiklenir; tek düşük puan alarm üretmez —
      `OnlineEvalSummaryServiceTests` (3 test)
- [x] İnsan puanı ile yargıç puanı aynı tabloda ayrılabilir
      (`SELECT source, avg(value) … GROUP BY source`) — `Source` alanı
      `human` / `judge:{ad}` ile ayrışır
- [x] `agentprism.judge.cost` ve `agentprism.judge.score` metrikleri yazılır —
      kod yolu `agentprism.run.cost`/`RecordRun` ile birebir aynı desende
      (`AgentPrismMetrics.RecordJudgeCost`/`RecordJudgeScore`); bu örnek
      barındırıcıda `/metrics` ucu (Prometheus exporter) hiç kayıtlı değildi,
      bu yüzden HTTP üzerinden doğrudan gözlenemedi — **bilinen bir sınırlama**,
      bu fazın bir eksiği değil (bkz. Sonraki Faza Devir Notu)
- [x] `POST /api/runs/{id}/judge` örneklemeyi atlar — gerçek koşumda ve
      `OnlineEvaluationEndpointTests`'te doğrulandı
- [x] 🚨 `AgentPrism.Core` AOT uyarısı üretmez — `dotnet build` üç TFM'de de
      (`net8.0`/`net9.0`/`net10.0`) 0 uyarı; `ForJsonSchema` yalnız
      `JsonElement` aşırı yüklemesiyle çağrıldı (`ResponseFormatAotTests`
      kaynak taramasından geçti)
- [x] Dört doğrulama kapısı sıfır uyarı verir (aşağıda)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, **gerçek bir yargıç
      çağrısı** ölçüldü ve maliyeti bu belgeye yazıldı (yukarıda, "Bu Fazda
      Verilen Kararlar")
- [x] `secret` taraması boş döndü (değişen dosyalarda)
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve buraya yazıldı
      (160,1 KB / 250 KB)

### Doğrulama komutları — gerçek koşum (2026-08-07, `samples/AgentPrism.Api`, port 5081)

```bash
# 1) Varsayilan appsettings (Enabled/SampleRate hic verilmedi) — yargic HIC calismamali
dotnet run --no-build -c Release --urls http://localhost:5081

RUN=$(curl -s -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H "content-type: application/json" -d '{"message":"varsayilan ayarla test"}' \
  | grep -m1 '"runId"' | sed -E 's/.*"runId":"([^"]+)".*/\1/')
curl -s "http://localhost:5081/agentprism/api/runs/$RUN/feedback" | jq 'length'
# -> 0

curl -s "http://localhost:5081/agentprism/api/runs?kind=Eval" | jq 'length'
# -> 6 (ONCEKI acik-ayarli kosumdan kalan, bu YENI calistirmadan artmadi)

# --- yeniden baslatildi: AgentPrism__OnlineEvaluation__Enabled=true SampleRate=1.0 ---

# 2) Puanlandi mi
RUN2=$(curl -s -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H "content-type: application/json" -d '{"message":"istanbul nerede, kisa cevap ver"}' \
  | grep -m1 '"runId"' | sed -E 's/.*"runId":"([^"]+)".*/\1/')
curl -s "http://localhost:5081/agentprism/api/runs/$RUN2/feedback" | jq '.[] | {kind, value, source, comment}'
# -> {"kind":"Numeric","value":100,"source":"judge:model",
#     "comment":"Kısa, doğrudan ve doğru cevap verilmiş."}

# 3) 🚨 Maliyet ayrimi — agent'in ozeti sismemis olmali
curl -s "http://localhost:5081/agentprism/api/stats?agentName=support" | jq '{totalRuns, totalCost}'
# -> {"totalRuns":3,"totalCost":null}   (fiyat bu ortamda tanimsiz — null, 0 DEGIL)
curl -s "http://localhost:5081/agentprism/api/runs?kind=Eval" | jq '.[] | {agentName, kind, cost}'
# -> uc satirin ucu de agentName:"judge:model", kind:"Eval" — support'un ustune YAZILMADI

# 4) Ozet ucu
curl -s http://localhost:5081/agentprism/api/evaluation/online | jq
# -> {"sampleCount":3,"averageScore":98.67,"lowScoreThreshold":60,
#     "minSampleSize":20,"belowThreshold":false,"judgeCost":null,"judgeCostCurrency":null}

# 5) Elle puanlama
curl -s -X POST "http://localhost:5081/agentprism/api/runs/$RUN2/judge" | jq
# -> tek elemanli dizi, Source: "judge:model" — AYNI satirin GUNCELLENDIGI
#    ikinci cagride dogrulandi (run_scores'ta ikinci satir ACILMADI, K-331)
```

### Doğrulama kapıları — gerçek sonuç (2026-08-07)

```
dotnet build  AgentPrism.slnx -c Release              → Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test   AgentPrism.slnx -c Release --no-build    → Core.UnitTests 712/712, AspNetCore.FunctionalTests
                                                          391/391, PostgreSql.IntegrationTests 844/844,
                                                          Sqlite.IntegrationTests 445/445, Ui.E2ETests 41/41
                                                          (izole koşumda; tüm paket paralel koşulunca sesli
                                                          konuşma E2E testi bir kez zaman aşımına uğradı —
                                                          Faz 49'dan bağımsız, bkz. Sonraki Faza Devir Notu).
                                                          SqlServer.IntegrationTests bu makinede Rosetta
                                                          kısıtı yüzünden koşmadı (bu fazdan önce de var olan
                                                          bilinen bir kısıt, bkz. docs/hafiza/sql-saglayicilari.md)
dotnet pack   AgentPrism.slnx -c Release --no-build    → Başarılı, paket sayısı değişmedi (yeni paket yok)
dotnet format AgentPrism.slnx --verify-no-changes      → exit 0, değişiklik yok
secret taraması                                        → değişen dosyalarda boş (pre-existing bir eşleşme
                                                          docs/hafiza/sql-server-yerel-test.md'de var, bu
                                                          fazda değişmedi, gerçek bir secret değil)
```

---

## Plandan Sapmalar

- **Pencere özeti (`GET /api/evaluation/online`) yeni bir SQL sorgu yüzeyi açmadan, bellek içi bir kayan pencereyle uygulandı.** Plan bunu açık bırakmıştı. `run_scores`'u zaman aralığına göre tarayan 3 diyalektlik bir agregasyon sorgusu yerine `OnlineEvalSummaryService` kiracı başına bir bellek içi kuyruk tutar — `RunSampler`'ın saatlik bütçesiyle aynı K1 tercihi (K-332). Yargıç maliyeti aynı uçta mevcut `RunQuery.Kind` süzgeciyle `runs` tablosundan (client-side `AgentName.StartsWith("judge:")` filtresiyle) toplanır; burada da yeni SQL yazılmadı.
- **Yargıcın kendi çalıştırması `IAgentCatalog` üzerinden DEĞİL, `ModelRunJudge`'ın ephemeral bir `ChatClientAgent` kurup doğrudan `RunRecordingAgent` ile sarmasıyla kaydedilir.** Plan "EvalJobHandler ile aynı desen" diyordu ama EvalJobHandler katalogdaki GERÇEK bir agent'ı çalıştırır; yargıç için katalogda bir tanım yoktur. Kota (`quotaEnforcer: null`) ve olay yayını (`webhookPublisher: null`) bilerek verilmez — Açık Soru 6'nın (A) doğal sonucu.
- **`RunScore.Author` yargıç puanlarında `judge:{ad}` ile dolu yazıldı**, insan puanı gibi `null` değil (K-331). Bu, K-239'un ters NULL semantiğini (author `null` iken benzersizlik uygulanmaz) tersine çevirip UPSERT tekilliğini devreye sokmak için kasıtlı bir tercihti — plan bunu belirtmiyordu, uygulama sırasında (retry/manuel yeniden puanlama senaryosunda çift satır riski görülünce) karara bağlandı.
- **`OnlineEvalJobHandler` DI'da hem `IJobHandler` hem kendi somut tipiyle kayıtlıdır** (K-333) — plan bunu öngörmüyordu; gerçek bir fonksiyonel test (`OnlineEvaluationEndpointTests`) `POST /api/runs/{id}/judge` ucunun `No service for type 'OnlineEvalJobHandler'` ile 500 döndüğünü yakaladı, birim testleri bunu göremezdi.
- **`docs/openapi/agentprism.json` anlık görüntüsü yenilendi** (+153/-2 satır) — iki yeni ucun ustverisini yansıtır; `OpenApiSnapshotTests` bunu zorunlu kıldı.
- **Ölçüldü, tahmini değil: arayüz payı 1-2 KB değil ~7,6 KB gzip oldu** (152,5 KB → 160,1 KB / 250 KB bütçe). Fark, judge skoru gösterimi + "Şimdi puanla" düğmesi + dashboard paneli + iki dilde ~11 sözlük anahtarından geliyor. Kalan pay hâlâ 89,9 KB.

## Bu Fazda Verilen Kararlar

K-327, K-328, K-329, K-330, K-331, K-332, K-333 — `docs/KARARLAR.md`. K-140 bu fazla kapandı (yeniden açılma notu güncellendi).

Özet:
1. 🚨 **`AIJudgeLoopEvaluator` KULLANILMADI** (K-327): `LoopEvaluation` puan döndürmez, `LoopContext` canlı `AIAgent`+`AgentSession` ister — reflection dökümüyle ölçüldü (MAF 1.16.0).
2. **Yargıç maliyeti `RunKind.Eval` dışlamasıyla ayrıldı** (K-328) — Faz 18'in açık soru 4 kararının (K-141) ikinci tüketicisi. Gerçek koşumla doğrulandı: `support` agent'ının `totalCost`'u etkilenmedi.
3. **İki kapılı varsayılan** (K-329): `Enabled = false` **ve** `SampleRate = 0.0`. Gerçek koşumla doğrulandı: varsayılan ayarlarla yeni bir çalıştırmanın `feedback` listesi boş kaldı, `kind=Eval` satır sayısı artmadı.
4. **D1 çözüldü**: yargıcın `IChatClient`'ı guard boru hattından GEÇER; engelleme özel kod olmadan genel "yargıç hatası" yoluna (K-160 retry) düşer (K-330).
5. **`RunScore.Author = "judge:{ad}"`** (K-331) — retry/manuel yeniden puanlamada tekil satır garantisi.
6. **Pencere özeti bellek içi** (K-332).
7. **`OnlineEvalJobHandler` çift DI kaydı** (K-333).

**Gerçek bir yargıç çağrısının ölçülen maliyeti**: bu ortamda `gpt-5.4-mini` için fiyat kataloğu/yapılandırması tanımlı değildi, bu yüzden hem ölçülen agent'ın hem yargıcın maliyeti `PricingSource.Unknown` (`null`) döndü — bu, "tanımsız fiyat sıfır değil `null`'dur" sözleşmesinin (bkz. `RunCost`) beklenen davranışıdır ve **hem** olağan çalıştırma **hem** yargıç için simetrikti. Token kullanımı gerçekti (gerçek OpenAI çağrısı): yargıç girdisi (soru+cevap özeti+talimat) tipik olarak birkaç yüz token, çıktısı (JSON `{"score":…,"reason":…}`) birkaç düzine token.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**

- `IRunJudge` (`Name`, `JudgeAsync(RunJudgeContext, CancellationToken)`) — Faz 7'den önce eklendi, metot eklemek yayından sonra kırıcıdır.
- `RunScoreKind.Numeric = 3` — Faz 31'in enum'una sona eklendi, değer kararlıdır.
- `JobKind.OnlineEval = 6` — kararlıdır.
- `RunQuery.Kind` — `GET /api/runs?kind=` ve `RunTimeSeriesQuery.Kind` ile aynı anlamı taşır.
- `WebhookEvents.RunScoreLow` = `"run.score.low"` — abonelerin kaydettiği bir dize, değiştirilemez.

**Bilinen tuzaklar (🚨):**

- `OnlineEvalSummaryService`'in pencere özeti **bellek içidir**, süreç yeniden başlatılınca sıfırlanır ve tek örnekli dağıtımda doğrudur (K-332). Çok örnekli bir dağıtımda her örnek kendi penceresini görür — kaynak gerçek her zaman `run_scores` tablosudur.
- Bir `IJobHandler`'ı hem kuyruk işleyicisi hem doğrudan çağrılan bir HTTP servisi yapmak istersen çift DI kaydı gerekir (K-333, `docs/hafiza/aspnetcore-di.md`).
- Yargıç modeli için fiyat tanımlı değilse (bu ortamda `gpt-5.4-mini` öyleydi) hem `agentprism.judge.cost` metriği hem `GET /api/evaluation/online`'ın `judgeCost` alanı `null` kalır — sıfır değil, "bilinmiyor" (mevcut `RunCost` sözleşmesiyle tutarlı).
- **F-74 (kanarya yayını ve otomatik geri alma) artık yapılabilir.** Üç önkoşulunun üçü de tamam: Faz 31 (insan puanı), Faz 44 (hata sınıfı) ve bu faz (otomatik puan). F-74'ün eşik mantığı bu fazın `MinSampleSize` + pencere kuralını **aynen** kullanmalıdır; üçüncü bir eşik kuralı yazılmamalıdır.
- **Ölçüt yeri açık kaldı** (Açık Soru 7): global ayar (`ModelRunJudgeOptions.Criteria`) seçildi; agent başına ölçüt ihtiyacı ölçülürse `AgentDefinition`'a alan eklemek bir sözleşme değişikliğidir ve Faz 7'den önce ucuzdur.
- **Arayüzde yalnız tek bir judge skoru satırı gösterilir** (birden çok `IRunJudge` kayıtlıysa hepsi `feedback-control.tsx`'te listelenir, ama dashboard paneli yalnız `OnlineEvalSummaryService`'in TÜM yargıçları birleştiren tek penceresini gösterir — yargıç bazında ayrım arayüzde yoktur).
- `Ui.E2ETests.UiTests.Playground_konusma_modu_mikrofonu_acar_ve_transkript_gosterir` tüm paket paralel koşulduğunda ara sıra zaman aşımına uğruyor (izole koşumda hep geçiyor) — Faz 49'dan **bağımsız**, sesli konuşma (Faz 29) testinin kaynak rekabetiyle ilgili bilinen bir kırılganlık; bu fazda yeni bir bulgu değil, yalnız gözlemlendi.

**Sıradaki faz: [Faz 50 — Dışa Açılan Agent Yüzeyi](50-DISA-ACILAN-AGENT-YUZEYI.md).** Bu faza bağımlı değildir — kendi önkoşulu yoktur, Faz 12'nin çağrı grafiği sınır denetimlerini yeniden kullanır.

# Tüketici Feature Talepleri — Ölçüm Kaydı (2026-09-01)

> **Tur türü:** dış tüketici raporunun koda karşı ölçümü.
> **Tüketici:** ProdigyEnabler · **İncelenen sürüm:** `0.0.0-preview.0.504`
> **Çıktı:** dört faz (129 · 130 · 131 · 132), üç ertelenen kalem.
>
> Bu kayıt raporun kopyası değildir. Yalnız **ölçülen** kısmı taşır: hangi iddia
> doğru çıktı, hangi kanıt hangi faza gitti, hangi kalem neden ertelendi.

## Raporun güvenilirliği

Rapor `.504` yüzeyleri için on beş iddia yazdı. On beşi de doğru çıktı:
`IRunStore.ReadEventsAsync`, `ISessionStore.TryUpdateAsync` + `SessionRecord.Version`,
`ITraconBuilder.AddScopedTool`, `IToolArgumentsValidator`,
`AgentRunBudget.MaxDuration`, `AgentResponseFormat` + `AgentResponseFormatKind.JsonSchema`,
`RunEventType.ModelFallbackUsed`, `IRunPricingResolver` + `PricingSource`,
`TraconSchedulingOptions`'ın global oluşu. Yanlış iddia bulunmadı.

Raporun **kaçırdığı** tek ölçüm Talep 5'i güçlendirir: Tracon bugün
`POST /api/stats/recalculate-costs` ile tarihsel maliyetleri yeniden yazıyor;
yani raporun "eski kayıt yeni fiyatla tekrar hesaplanmaz" kabul kriteri bugün
sağlanmıyor. Ayrıntı Faz 132'dedir.

## Zamanlama ölçümü — dört talebin ortak gerekçesi

`wc -l src/*/PublicAPI.Shipped.txt` → her dosya **1 satır** (yalnız başlık).
Shipped giriş sayısı sıfırdır. [`YAYIN-HAZIRLIK.md`](../YAYIN-HAZIRLIK.md) yayın
türünü `preview` sabitliyor ve API freeze'i GA'ya erteliyor.

Sonuç: `IJobStore.LeaseAsync` imzasını ve `RunCost` alanlarını değiştirmek
**bugün bedavadır**, GA'dan sonra bir sürüm kararıdır. Faz 129 ve 132'nin
sıralanma gerekçesi tüketicinin önceliği değil, bu penceredir. Kullanıcı
2026-09-01'de bu pencerede ilerlemeye karar verdi.

## Talep talep sonuç

| # | Talep | Sonuç | Nereye |
|---|---|---|---|
| 1 | Named job lanes ve worker subscription | Kanıt doğru; boşluk gerçek | **F-172 → Faz 129** |
| 2 | Structured response validation seam | Kanıt doğru; seam olarak alınır, validator sevk edilmez | **F-174 → Faz 131** |
| 3A | Generator scalar constraints | Kanıt doğru; en dar kalem | **F-173 → Faz 130** |
| 3B | AOT-safe nested object schema | Kanıt doğru; K-615 uzlaştırması gerekir | **F-176** — Faz 130 sonrası |
| 4 | Dynamic cost/latency routing policy | **Ön koşul yok** — `RunRecord`'da provider alanı hiç yok | **F-179** — ön koşulu Faz 132 kapatır |
| 5 | Applied price snapshot | Kanıt doğru **ve** bir beyan çelişkisi var | **F-175 → Faz 132** |
| 6 | Provider attempt duration/index | Tüketici "şimdi istemiyoruz" diyor; bugün hiçbir karar engellemiyor | **F-178** — lane metrikleri fazına biner |

### Faza girmeyen kalemler için gerekçe

**Talep 3B (nested object).** K-615 generator'ın kendi `JsonSerializerContext`'ini
kullanmamasını karara bağladı; tool sahibi kendi context'ini
`TraconToolAttribute.JsonSerializerContext` ile verir, vermezse derleme
`APG0008` ile durur. Nested object şeması + AOT metadata bu kararla uzlaştırılmalıdır.
Faz 130 bunu beklemez ama aynı faza da girmez.

**Talep 4 (dynamic routing).** Üç ön koşul eksik:
`RunRecord` `ModelId` taşıyor, **provider taşımıyor** — dolayısıyla raporun
"seçilen provider/model, usage ve cost attribution ile aynı olur" kriteri bugün
ölçülemez; `ModelProviderHealthCache` sağlık durumu verir, kayan latency
penceresi vermez; attempt süresi hiç ölçülmez. Faz 132 birincisini kapatır.

**Talep 6 (attempt telemetry).** `FallbackChatClient.GetResponseAsync` döngü
indeksini zaten tutuyor ve `ModelFallbackUsed`'ı zaten yazıyor. Ekleme tamamen
additive'dir; bugün alınan hiçbir karar onu kapatmıyor. Tüketicinin tek isteği
"API tasarımında engel çıkarmayın" — bu istek karşılanıyor.

### Lane metrikleri neden Faz 129'a girmedi

Raporun 4.5 kabul kriteri lane başına queued/leased/succeeded/failed/duration
metriği istiyor. Ölçüm: `TraconMetrics` bugün run, token, cost, tool, judge,
model cache ve agent source sayıyor — **hiç job metriği yok**. "Lane metriği"
istemek, olmayan bir metrik ailesini sıfırdan kurmak demektir. Bu ayrı bir
fazdır ve Faz 129'un lane kimliğine bağlıdır.

## Raporun kendi fallback planı

Tüketici her talep için Tracon gelmezse ne yapacağını yazdı. Bu, taleplerin
**bloklayıcı olmadığını** gösterir: entegrasyon bugün başlayabilir. Feature'ların
değeri tüketiciyi kurtarmak değil, aynı runtime politikasının her tüketicide
yeniden yazılmasını önlemektir. Dört fazın hepsi bu ölçütü geçti — dördü de
Tracon'in kendi içinde bugün gerçek bir boşluk veya çelişki kapatıyor.

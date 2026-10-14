# Faz 205 — Kök Kapandıktan Sonra Biten Harcamanın Kotası

> **Durum:** 📋 Planlandı (2026-10-14)
> **Plan onayı:** onaylanmadı — engelleyici dört tasarım sorusu 2026-10-14'te kullanıcıyla cevaplandı (§205.1 A, F-482 dahil, webhook belgelenir, eşik bildirimi çocuğun akışı); plan metni onay bekler, uygulama onaydan önce başlamaz
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-418**, **F-482**
> **Genellik:** Alt agent çağıran ya da tool'u zaman aşımına düşebilen her tüketici, kiracı kotası açıksa etkilenir. İş tüketici kodunda kalamaz: kota sayacını yalnız Tracon yazar. Kalem bir sözleşme ihlalidir (kiracı kotası bir yönetişim sınırıdır, site "consumption is counted" der), bu yüzden § *Tüketici geri bildirimi kuralı*'ndan muaftır. İkinci kanıt ilk ilkedir: K-913 kendi bilinen sınırını yazar ve bir model talimatı ("görevi başlat, bekleme") harcamayı kotadan gizler
> **Karşı görüş:** "Arka plan alt run'ı `ChildDeadline` (2 dk) ile sınırlıdır; kaçak küçük ve nadirdir." Cevap: sınır run başınadır, toplam değildir. Bir kök en çok `MaxTotalRuns` (25) çocuk başlatır ve her biri 2 dakikaya kadar harcar; aynı istek tekrarlanınca kaçak sınırsız büyür. Geç biten tool'un harcaması (F-482) gerçek paradır ve bugün hiçbir sayaca girmez. Mekanizma ikisini de aynı kuralla kapatır
> **Önkoşul:** Yok — K-913 (Faz dışı, F-383) ve Faz 199 (K-972…K-974) kapalı
> **Paketler:** `Tracon.Abstractions` (yalnız `internal` üye), `Tracon.Core`, `Tracon.Workflows`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — taşıyıcı `internal`'dır (§205.2). Ölçüldü (2026-10-14): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır, her biri yalnız `#nullable enable`; bir sapma public üye gerektirirse GA'dan (K-603) önce hâlâ ucuzdur
> **Tüketici yüzeyi:** site: `concepts/governance.md` (kota bölümü: "counted once at the tree's root" cümlesi ve eşik bildirimi), `concepts/agents.md` (arka plan alt agent'ı, yalnız bir paragraf varsa), `guides/background-work.md`'ye dokunulmaz · sevk edilen: `CHANGELOG.md` (davranış değişikliği), `capabilities.md` kota satırı
> **Manuel test alanı:** `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` (kota case'leri, `MT-RET-*`)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-913\|K-972\|K-973\|K-974\|K-976\|K-219" docs/KARARLAR.md
   ```
   **K-913** (kök agent kotaya ve webhook'a ağaç toplamını yazar — bu faz kota yarısını değiştirir),
   **K-972** (para kararı yalnız `RunSpend.Total`'dan; yalnız `Tracon:Pricing:Currency` toplanır),
   **K-973** (webhook yükünün `Cost`/`NonTokenCost` anlamı), **K-974** (geç gelen kalem yalnız detaya —
   bu faz kota yarısını değiştirir, snapshot kuralı kalır), **K-976** (karışık para biriminde toplam yok)
3. Önceki faz devri: `awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/199-FONKSIYON-DUGUMU-MALIYETI.md`
   — geç tool ölçümünün (`LateToolCompletionRecorder`) ve `NonTokenCostAccumulator.Seal()`'ın sözleşmesi
4. Alan hafızası:
   [`hafiza/olcum-kota-ve-secenekler.md`](hafiza/olcum-kota-ve-secenekler.md) (kota, toplama ifadesi, K-483 sınıfı) ·
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) (`AsyncLocal`/scope kuralı; arka plan görevi ortamı göremez)
5. Gerektiğinde: [`MIMARI.md`](MIMARI.md) — agent çağrı grafiği ve kota bölümü (`grep -n "kota\|quota" docs/MIMARI.md`)

---

## Amaç

Kiracı kotası bir çağrı ağacının harcamasını eksiksiz saymalıdır. Bugün harcama
yalnız kökün kapanış anında okunur: o anda hâlâ koşan arka plan alt run'ı ve
zaman aşımından sonra biten tool'un tutarı hiçbir sayaca girmez. Bu faz kuralı
tersine çevirir: **her harcama, kesinleştiği anda, onu yapan run tarafından,
ağacın kök kimliğiyle kotaya yazılır.** Kiracı, kotası olan operatör ve faturayı
kotadan okuyan tüketici kazanır.

- **F-418** — Kök kapandıktan sonra biten arka plan alt run'ının tüketimi kotaya girer.
- **F-482** — Kapanıştan sonra bildirilen tool harcaması kotaya girer (toplam snapshot'ı ve webhook değişmez).

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`RunRecordingAgent.Completion.cs:162-169`](../src/Tracon.Core/Recording/RunRecordingAgent.Completion.cs) | Yalnız `Depth == 0` run'ı ağacı okur ve kotaya yazar; çocuk hiç yazmaz (`:200` webhook için aynı kapı) |
| [`RunRecordingAgent.Notifications.cs:57-160`](../src/Tracon.Core/Recording/RunRecordingAgent.Notifications.cs) | Ağaç toplamı kökün satırı kapanmadan depodan **bir kez** okunur; o anda koşan çocuğun satırında usage yoktur |
| [`AgentDefinitionCompiler.Agents.cs:119-124`](../src/Tracon.Core/Compilation/AgentDefinitionCompiler.Agents.cs) | Alt agent'lar MAF `BackgroundAgentsProvider` ile sunulur; `start_task` görevi başlatıp hemen döner |
| `grep -rn "ReleaseSessionAsync" src/` → boş | Tracon görevleri kök bitince iptal etmez; görev oturumla yaşar ve sonraki turda da sonuçlanabilir. Kökü bekletmek ya da çocuğu iptal etmek bu yüzden çözüm değildir |
| [`TraconOptions.cs:190`, `:229`](../src/Tracon.Core/TraconOptions.cs) | `MaxTotalRuns = 25`, `ChildDeadline = 2 dk`: çocuk başına sınır var, toplamda yok |
| [`WorkflowRunner.cs:1551`, `:1737-1781`](../src/Tracon.Workflows/Internal/WorkflowRunner.cs) | Workflow kökü satırını kapattıktan **sonra** ağacı okur ve yazar; adımın başlattığı arka plan görevi aynı açıktadır |
| [`LateToolCompletionRecorder.cs:128`](../src/Tracon.Core/Recording/LateToolCompletionRecorder.cs) | Geç biten tool'un ölçümü yalnız çağrının `tool_invocations` satırına yazılır (K-974); kotaya gitmez |
| [`ChildAgentInvoker.cs:477-499`](../src/Tracon.Core/Graph/ChildAgentInvoker.cs) | Çocuk ağacın `AgentRunBudget` örneğini **aynı referansla** alır; kökün adını ve kiracısını almaz |
| [`concepts/governance.md:494-497`](../docs-site/src/content/docs/concepts/governance.md) | Site sınırı yazar: "A child run that is still running in the background when its root ends is not counted." |

> Kanıtlar 2026-10-14 tarihinde doğrulandı.

---

## 205.1 — Kural: harcama kesinleştiği anda yazılır

Seçilen tasarım A'dır (kullanıcı kararı, 2026-10-14). Kota girdisi artık ağaç başına
bir tane değildir; harcayan her olay kendi girdisini yazar.

| Kim | Ne zaman | Girdi |
|---|---|---|
| Kök agent run'ı | Kendi kapanışında | Kendi token'ı ve tutarı (`RunSpend.Total`), `Runs = 1` |
| Alt agent run'ı (her derinlik) | Kendi kapanışında, kök kapanmış olsa da | Kendi token'ı ve tutarı, `Runs = 0` |
| Workflow kökü | Kendi kapanışında | Kendi düğümlerinin token dışı tutarı, `Runs = 1`; token yok (satırın modeli yok) |
| Geç biten tool (F-482) | Ölçüm geç geldiği anda | Yalnız tutar, `Runs = 0`, token yok |

Her girdi **ağacın kök kimliğiyle** yazılır: kiracı ve ad kökünkidir (agent kökünde
agent adı, workflow kökünde workflow adı). Bugün yalnız kök yazdığı için sayaçlar
kökün adı altındadır; ağaç sayacı bu yüzden aynı kalır, yalnız eksiksiz olur. Çocuk
kendi adıyla hiç sayaç açmaz (`AgentTreeQuotaTests` bunu bugün de ölçer).

```mermaid
sequenceDiagram
    participant K as Kök run
    participant C as Arka plan alt run'ı
    participant Q as Kota sayacı
    K->>C: start_task (hemen döner)
    K->>Q: kendi girdisi (Runs=1)
    K-->>K: kapanır, webhook = kapanış anındaki ağaç
    C->>Q: kendi girdisi (Runs=0, kökün adıyla)
    C-->>C: kapanır
```

Kararın dört sonucu:

- **Çift sayım yoktur.** Her run yalnız kendini yazar; kök artık ağacı kotaya yazmaz.
  `ReadTreeConsumptionAsync` yalnız webhook için kalır (§205.3).
- **Sayaç daha erken güncellenir.** Çocuğun harcaması kök kapanmadan sayaca girer.
  Kota yine run başlamadan denetlenir; sayaç yaklaşık kalır (site uyarısı geçerli).
- **Para birimi kuralı her girdide aynıdır.** Girdinin tutarı `RunSpend.Total`'dır (K-972):
  `Tracon:Pricing:Currency` dışındaki tutar sayılmaz, token sayılır. Bugünkü F-419 testinin
  beklentisi (`AgentTreeQuotaTests.A_tree_priced_in_two_currencies…`: token 20, tutar 0.016)
  korunur — başka para birimli çocuk token'ıyla girer, tutarıyla girmez.
- **Yazım sayısı artar.** Ağaç başına en çok `MaxTotalRuns + 1` kota yazımı olur; her biri
  bugünkü tek yazımla aynı işlemdir (`QuotaEnforcer.RecordAsync` → `IQuotaStore.AddUsageAsync`).
  Maliyet model çağrısının yanında küçüktür; ölçülmedi, kapanışta ölçülmeli.

## 205.2 — Kök kimliği çocuğa nasıl ulaşır

Arka plan görevi kökün ortam kapsamını (`AsyncLocal`) güvenle göremez: görev kök
kapandıktan sonra sürer. Kimlik bu yüzden **ağacın paylaşılan nesnesinde** taşınır.
`AgentRunBudget` her çocuğa aynı referansla geçer (`ChildAgentInvoker.cs:489`) ve iki
kök de onu kendisi kurar (`RunRecordingAgent.Lifecycle.cs`'te `CreateBudget`,
`WorkflowRunner.cs:610`). Plan şunu önerir:

- `AgentRunBudget`'a `internal` bir **ağaç hesabı** üyesi: kiracı, kök adı, kök run kimliği,
  kökün kullanıcısı. Kök onu bütçeyi kurarken doldurur. `Tracon.Abstractions` bu üyeyi
  `Tracon.Core` ve `Tracon.Workflows`'a açar (ikisi de `InternalsVisibleTo` listesindedir,
  ölçüldü). Public yüzey büyümez.
- Çocuk kendi kapanışında girdisini bu hesapla yazar; ortam kiracısına bakmaz.
- MCP (`CatalogToolCallHandler.cs:103`) ve A2A (`ExternalAgentProxy.cs:187`) kendi
  bütçesini kurar ve oradaki run köktür; hesap o kökte doldurulur.

> **Doğrulanmadı — uygulama ölçmeli:** her kök yolunun (eşzamanlı, akışlı, kuyruklu run,
> devam, onay devamı, replay, eval case, MCP, A2A, workflow) bir bütçe kurduğu ve `Budget`'ın
> hiçbir kökte `null` kalmadığı. `null` kalan yol hesap taşımaz; o yolun çocuğu bugünkü gibi
> yazmamalı ve bu bir test ile sabitlenmeli.

## 205.3 — Webhook kapanış anının görüntüsüdür (belgelenir)

Kullanıcı kararı (2026-10-14): `run.completed` yükü değişmez. Kök kapanırken ağacı okur
(K-913) ve yükte o anki toplamı taşır. Kökten sonra biten çocuk ve geç gelen tool tutarı
yüke girmez; kesin toplam `GET /api/runs/{id}` (ağaç alanları) ve kota sayacıdır. Yeni
webhook olayı açılmaz (K1). Site bu sınırı açıkça yazar; bugünkü "not counted" cümlesi
kaldırılır.

## 205.4 — Eşik bildirimi eşiği geçen run'ın akışına yazılır

Kullanıcı kararı (2026-10-14): çocuğun girdisi bir eşiği geçerse
`tracon.quota.threshold` bildirimi **çocuğun kendi** olay akışına yazılır
(`PublishThresholdToRunStream` açıksa) ve `quota.threshold` webhook'u o run'ı
adlandırır. Kökün akışı kapanmış olabilir; kapanmış akışa yazılmaz. Geç gelen tool
tutarının girdisi bildirimi hiçbir akışa yazmaz (run kapanmıştır); yalnız webhook gider.
Sitenin "Only the root run of a call tree ever carries the notice" cümlesi değişir.

## 205.5 — Geç biten tool (F-482)

`LateToolCompletionRecorder` geç ölçümü çağrının satırına yazdıktan sonra, ölçümün
tutarı varsa (`ToolCallUsage.Cost`) ağaç hesabıyla bir kota girdisi yazar: `Runs = 0`,
token yok, tutar `RunSpend` kuralıyla. Run'ın snapshot sütunları değişmez (K-974'ün
snapshot kuralı kalır); değişen yalnız kotadır. Yazım fire-and-forget yoludur ve
"gözlemlenebilirlik işlevselliği bozmaz" kuralına uyar: depo hatası loglanır.

---

## Planlanan Public API

> Public yüzey büyümez. Aşağıdakiler `internal` taslaktır; gerçek adlar kapanışta yazılır.

```csharp
// Tracon.Abstractions (internal; Core ve Workflows görür)
internal sealed record TreeAccount(string TenantId, string RootName, Guid RootRunId, string? UserId);

public sealed class AgentRunBudget   // bugün sealed, partial değil — üye aynı dosyaya eklenir
{
    internal TreeAccount? Account { get; set; }   // kök kurarken doldurur, bir kez
}

// Tracon.Core (internal) — tek yazım yolu; kök, çocuk, workflow ve geç tool onu çağırır
internal static class TreeQuota
{
    internal static ValueTask<IReadOnlyList<QuotaThresholdCrossing>> RecordAsync(
        IQuotaEnforcer enforcer, TreeAccount account, Guid runId, long tokens, decimal? amount, int runs,
        DateTimeOffset occurredAt, CancellationToken cancellationToken);
}
```

🚨 Tek yazım yolu bir kapıdır, süs değildir: dört çağıran `QuotaConsumption`'ı elle kurarsa
K-483 sınıfı (elle tekrarlanan toplama ifadesine terim eklemek) geri gelir.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Runs/
├── AgentRunBudget.cs                     (internal Account)
└── TreeAccount.cs                        (yeni, internal)
src/Tracon.Core/
├── Quotas/TreeQuota.cs                   (yeni, internal — tek yazım yolu)
├── Recording/RunRecordingAgent.Completion.cs      (her derinlik kendini yazar)
├── Recording/RunRecordingAgent.Notifications.cs   (RecordQuotaAsync kendi tüketimi; ağaç okuması yalnız webhook)
├── Recording/RunRecordingAgent.Lifecycle.cs       (kök hesabı doldurur)
└── Recording/LateToolCompletionRecorder.cs        (geç tutarın girdisi)
src/Tracon.Workflows/Internal/WorkflowRunner.cs    (kendi düğüm tutarı, Runs=1; hesap kurulumu)
src/Tracon.Testing.Contracts.Xunit/Contracts/QuotaStoreContract.cs  (Runs=0 girdisi)
tests/Tracon.AspNetCore.FunctionalTests/BackgroundChildQuotaTests.cs (yeni)
docs-site/src/content/docs/concepts/governance.md
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Bekleme yapmayan kökün çocuğu kök kapandıktan sonra biter ve sayaca girmez (F-418 repro: `start_task`, kapı açılmadan cevap; çocuğun modeli kapıyla bekletilir) | Fonksiyonel, akışlı **ve** akışsız | `BackgroundChildQuotaTests` — önce kırmızı (sayaç 10, beklenen 20) |
| Kök ağacı yazmaya devam eder ve çocuk da yazar: çift sayım | Fonksiyonel | `AgentTreeQuotaTests` (mevcut: 20 token, 0.058) yeşil kalır; `BackgroundChildQuotaTests` tam toplamı ölçer |
| Workflow adımının başlattığı arka plan görevi workflow bitince sayılmaz | Fonksiyonel | `BackgroundChildQuotaTests` workflow case'i — önce kırmızı |
| Çocuk girdisi ortam kiracısına yazar (arka plan iş parçacığında ortam başka kiracıdır) | Fonksiyonel, iki kiracı | `BackgroundChildQuotaTests` iki kiracılı case |
| Başka para birimli çocuğun tutarı sayaca eklenir | Fonksiyonel | `AgentTreeQuotaTests.A_tree_priced_in_two_currencies…` (mevcut, beklenti korunur) |
| Çocuk kendi adıyla sayaç açar | Fonksiyonel | `AgentTreeQuotaTests` mevcut `CountersAsync(host, "arastirmaci")` boş kalır |
| `Runs = 0` girdisi bir depoda run sayısını artırır ya da reddedilir | Sözleşme (bellek + üç SQL) | `QuotaStoreContract` yeni case |
| Eşzamanlı biten çok çocuk sayaçta artış kaybeder | Sözleşme | `QuotaStoreContract.Concurrent_increments_lose_no_increment` (mevcut) — kapsamı yeter, ölçülmeli |
| Çocuğun eşik bildirimi köke yazılmaya çalışılır (kapanmış akış) ya da hiç yazılmaz | Fonksiyonel | `BackgroundChildQuotaTests` eşik case'i (`PublishThresholdToRunStream` açık) |
| Geç biten tool tutarı kotaya girmez (F-482 repro) | Fonksiyonel | `LateToolCompletionTests` yeni case — önce kırmızı |
| Geç tutar run snapshot'ını değiştirir (K-974 kırılır) | Fonksiyonel | Aynı case snapshot'ın değişmediğini ölçer |
| Kota deposu çocuğun girdisinde hata verir | Fonksiyonel | Çocuk run `Completed` kalır, uyarı loglanır (`RecordingLoggerProvider`) |
| Çocuk iptal edilir ya da `ChildDeadline`'a çarpar | Fonksiyonel | O ana kadar kaydedilen usage girer; bugünkü davranışla aynı, test sabitler |
| `Budget` `null` kalan bir kök yolu | Birim/fonksiyonel | §205.2 ölçümünün testi — her kök yolu hesap taşır |
| Kuyruklu run'ın ikinci denemesi önceki denemenin çocuğunu yeniden sayar | Fonksiyonel | Her çocuk ayrı run'dır ve bir kez yazar; kökün kendi girdisi deneme başına kendi harcamasıdır — `QueuedRunAttributionTests` emsaliyle bir case |

Beş soru: **iptal** (çocuk iptali, test var) · **eşzamanlılık** (eşzamanlı çocuklar,
sözleşme) · **boş/aşırı girdi** (usage'sız çocuk girdisi yazmaz ya da `0` yazar —
karar Açık Soru 2) · **başka kiracı** (iki kiracılı case) · **alt sistem hatası**
(kota deposu hatası).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnek uygulama; günlük token kotası açık; yönlendirici agent'ın talimatı "görevi başlat, sonucu bekleme" | Yönlendiriciyi çalıştır; HTTP yanıtı gelince `GET /api/quotas/usage` oku; çocuk bitince tekrar oku | İlk okuma kökün token'ını, ikinci okuma kök + çocuğu gösterir; `runs` 1 |
| 2 | Aynı; `run.completed` webhook alıcısı | Aynı run | Webhook yükü kapanış anındaki ağacı taşır (çocuk dahil değil); fark site sınırıyla uyumlu |
| 3 | `PublishThresholdToRunStream` açık; eşik çocuğun girdisiyle geçilecek kadar düşük | Aynı run; çocuğun olay akışını oku | Bildirim çocuğun akışındadır; kökün akışında yoktur |
| 4 | Zaman aşımı kısa, tutar bildiren yavaş tool | Run'ı çalıştır; tool bitince sayacı oku | Sayaç tool tutarı kadar artar; run'ın `nonTokenCost`'u değişmez |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Taşıyıcı nerede? | A: `AgentRunBudget` `internal` üyesi · B: `AgentRunScope`'a yeni public alan + `TraconRunOptions` | A — bütçe zaten ağaç başına tek referanstır ve iki kök onu kurar; public yüzey büyümez |
| 2 | Hiç usage'ı ve tutarı olmayan çocuk girdi yazar mı? | A: yazmaz · B: `0` yazar | A — boş yazım depo turudur; eşik değişmez |
| 3 | Geç tool girdisinin `RunId`'si hangisi? | A: tool'u çağıran run · B: kök | A — eşik webhook'u tutarı harcayan run'ı adlandırır (§205.4 ile tutarlı) |
| 4 | `ReadTreeConsumptionAsync`'in kota yolundan çıkması `StartedRuns` kapısını gereksiz kılar mı? | A: yalnız webhook için kalır · B: kaldırılır | A — webhook kararı (§205.3) okumayı korur |

---

## Bitiş Ölçütleri (DoD)

- [ ] Bekleme yapmayan kökün çocuğu bitince kökün sayacı kök + çocuğu taşır; `runs` 1 (`BackgroundChildQuotaTests`, önce kırmızı)
- [ ] Workflow adımının arka plan görevi workflow bitince sayaca girer (önce kırmızı)
- [ ] Geç biten tool'un tutarı sayaca girer, run snapshot'ı değişmez (önce kırmızı)
- [ ] `AgentTreeQuotaTests`, `NonTokenSpendAgentTreeTests`, `NonTokenSpendTests` değiştirilmeden yeşil
- [ ] `QuotaStoreContract` `Runs = 0` case'i dört depoda yeşil
- [ ] K-913 ve K-974'ün kota yarısını değiştiren karar `KARARLAR.md`'ye yazıldı (kategori: güvenlik, public-api)
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (`concepts/governance.md` iki cümle, `capabilities.md`); `npm run check` temiz

### Doğrulama komutları

```bash
# Arka plan çocuğunun kotası
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*BackgroundChildQuotaTests" "*AgentTreeQuotaTests" "*LateToolCompletionTests"

# Örnek uygulamada sayaç (çocuk bitmeden ve bittikten sonra)
curl -s http://localhost:5081/tracon/api/quotas/usage -H "Authorization: Bearer $TOKEN"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Kök hâlâ ağacı kotaya yazar ve çocuk da yazar: çift sayım | Kota yolu yalnız `TreeQuota.RecordAsync`'ten geçer; `AgentTreeQuotaTests` tam toplamı ölçer (çift sayım 40 token verir) |
| Arka plan görevi ortam kiracısını okur | Hesap bütçede taşınır; iki kiracılı test |
| Elle kurulan `QuotaConsumption` (K-483 sınıfı) | Tek yazım yolu; kapanışta `grep -rn "new QuotaConsumption" src/` yalnız o yolu göstermeli |
| Workflow kökü satırı kapattıktan sonra okuyordu; sıra değişince webhook yükü kayar | Webhook okuması değişmez (§205.3); yalnız kota yolu değişir |
| Yazım sayısının artması kota deposunu yükler | Ağaç başına en çok 26 yazım; kapanışta örnek uygulamada ölçülür ve yazılır |
| Site ve K-913 metni eski kuralı anlatır | `faz-tamamlama` Adım 7; `governance.md:494-497` iki cümle değişir |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Fazın kararları burada yazılır. Dört kategoriden birine
> giren karar (AGENTS.md) ayrıca `docs/KARARLAR.md`'ye K-NNN ve kategori
> etiketiyle girer ve numarası burada anılır; girmeyen karar yalnız burada
> kalır (`faz-tamamlama` Adım 8). Plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

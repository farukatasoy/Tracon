# Faz 128 — Run Ağacı Süre Bütçesi

> **Durum:** 📋 Planlandı (2026-08-31)
> **Kaynak:** [kesif/2026-08-31-tuketici-raporu-faz-adaylari.md](kesif/2026-08-31-tuketici-raporu-faz-adaylari.md) · **T-7**
> **Önkoşul:** [Faz 114](arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md) (çalıştırma-içi bütçe tavanı, `RunBudgetChatClient`) — arşivde; **damıtılmış**, tam metin `git show 3fbdc7d:docs/arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md`
> **Paketler:** `AgentPrism.Abstractions` (`Runs/AgentRunBudget.cs`), `AgentPrism.Core` (`Models/RunBudgetChatClient.cs`, `AgentPrismOptions.cs`)
> **Yeni paket:** Yok · **Migration:** Yok — tavan yapılandırmadan gelir
> **Public API:** Büyüyor — mevcut iki tipe birer alan. Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır (K-603)
> **Tüketici yüzeyi:** `docs-site/src/content/docs/concepts/governance.md`, `guides/production.md`, `capabilities.md` · sevk edilen: `AgentRunBudget` ve `AgentPrismAgentGraphOptions` XML dokümanları
> **Manuel test alanı:** `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula.

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**:
   ```bash
   grep -n "K-627\|K-630\|K-603" docs/KARARLAR.md
   ```
   **K-630** (çalıştırma-içi bütçe kesmesi yeni bir `RunErrorClass` üyesi **açmadan** `QuotaExceeded`'a eşlenir — 🚨 bu faz de aynı eşlemeyi kullanır) · **K-627** (`RunErrorClass` değeri `9` kalıcı olarak emekli) · **K-603**
3. Alan hafızası:
   [`hafiza/olcum-kota-ve-secenekler.md`](hafiza/olcum-kota-ve-secenekler.md) (bütçe ve kota tuzakları; K-483'ün vakası burada) ·
   [`hafiza/model-boru-hatti.md`](hafiza/model-boru-hatti.md) (halka konumu)
4. Gerektiğinde: [`MIMARI-GUVENLIK.md`](MIMARI-GUVENLIK.md) — kota ve bütçe bölümü

---

## Amaç

`AgentRunBudget` token, maliyet, çocuk run sayısı ve derinlik taşır; **süre
taşımaz.** İterasyon tavanı bir iterasyonun ne kadar süreceğini sınırlamaz ve
tool timeout'u yalnız **tek bir çağrıyı** sınırlar. Uzun tool zinciri olan bir
run saatlerce sürebilir.

- **T-7** — Run ağacına bir süre boyutu; kesme noktası mevcut desene uyar.

### Karşı görüş ölçüldü — kalem ayakta

Aday metni dürüst bir itiraz taşıyordu: *"Faz 114 zaten çalıştırma-içi tavanı
koydu ve gerekçesi 'maliyet sayılmıyordu' idi. Süre, maliyetin dolaylı bir
vekilidir — ayrı bir boyut mu, yoksa gürültü mü?"*

Ölçüm itirazı **çürüttü**. Süre, maliyetin vekili değildir; ikisinin ayrıldığı
somut bir yol var:

| Senaryo | Token/maliyet tavanı ne yapar | Süre tavanı ne yapar |
|---|---|---|
| Her turda küçük bir model çağrısı, ama her tool 90 saniye bekliyor | **Hiçbir şey** — token az, maliyet düşük | Keser |
| Yavaş bir sağlayıcı, aynı token hacmi | Hiçbir şey | Keser |
| Kısa ama pahalı tek tur | Keser | Hiçbir şey |

Ve **kuyruğa alınmış run'da hiçbir dış zaman sınırı yoktur**:
`JobWorkerBackgroundService.cs:227` işi koşarken kirayı **sürekli yeniler**
(`StartLeaseRenewal`). HTTP isteğinin doğal zaman aşımı orada yoktur. Yani
dayanıklı çalıştırma yolunda bugün run'ı zamanla sınırlayan **hiçbir şey**
yoktur.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentRunBudget.cs:36-80`](../src/AgentPrism.Abstractions/Runs/AgentRunBudget.cs) | Dört boyut: `MaxTotalTokens`, `MaxTotalCost`, `MaxTotalRuns`, `MaxDepth`. Süre **yok** |
| [`AgentPrismOptions.cs:141-178`](../src/AgentPrism.Core/AgentPrismOptions.cs) | `AgentPrismAgentGraphOptions` aynı dört boyutu taşıyor |
| [`RunBudgetChatClient.cs:83-97`](../src/AgentPrism.Core/Models/RunBudgetChatClient.cs) | Kesme noktası **hazır**: `ThrowIfExhausted` her gerçek model çağrısından önce koşuyor |
| [`TimeoutAIFunction.cs:23-28`](../src/AgentPrism.Core/Tools/TimeoutAIFunction.cs) | Tool timeout yalnız **tek çağrıyı** sınırlar ve gövdeyi zorla durduramaz — dokümante edilmiş sınır |
| [`JobWorkerBackgroundService.cs:227`](../src/AgentPrism.Core/Scheduling/JobWorkerBackgroundService.cs) | Kuyruktaki iş koşarken kira **yenilenir**; süre üst sınırı yoktur |

> Kanıtlar 2026-08-31 tarihinde `8105c00` üzerinde doğrulandı.

---

## 128.1 — Beşinci boyut: `MaxDuration`

`AgentRunBudget` ağaç boyunca paylaşılan **tek** nesnedir (bilinçli olarak
`class`, `record` değil). Süre de o nesneye aittir: kök run başlar, bütün
çocuklar aynı son tarihi paylaşır.

```csharp
public sealed class AgentRunBudget
{
    /// <summary>The wall-clock time the whole tree may take. Null means no limit.</summary>
    public TimeSpan? MaxDuration { get; init; }

    /// <summary>The instant the tree's time runs out. Null when MaxDuration is null.</summary>
    public DateTimeOffset? Deadline { get; }
}
```

**`TimeSpan` yapılandırmadan gelir, `Deadline` ondan türetilir.** İkisi ayrı
alan olarak yapılandırılabilseydi tutarsız bir çift üretilebilirdi. Son tarih
bütçe nesnesi kurulurken **bir kez** hesaplanır; ağacın her dalı aynı anı görür.

🚨 **`TimeProvider` kullanılır, `DateTimeOffset.UtcNow` değil.** Süre bir
testin doğrulayabileceği tek şeydir ve gerçek saate bağlanan bir tavan ya
kırılgan bir testle ya da hiç testle sınanır. `RunReconciliationService` ve
`RunContinuationJobHandler` bu repo'da zaten `TimeProvider` alıyor.

## 128.2 — Kesme noktası: iki model turu arasında

Bu kural Faz 114'ün kararıdır ve değiştirilmez:

> *"The cutoff always lands between two model turns, never inside one — the
> decorator only ever refuses a call it has not yet made."*

Yani `MaxDuration`, `RunBudgetChatClient.ThrowIfExhausted`'ın içine **bir
kontrol daha** olarak girer. Akan bir yanıtın ortasında kesme yapılmaz; yarım
bir model mesajı bırakmak, bütçeyi zorlamaktan daha kötüdür.

```mermaid
flowchart TD
    A[Tool döngüsü bir sonraki model çağrısını yapacak] --> B{IsExhausted}
    B -- token/maliyet doldu --> D[AgentPrismRunBudgetExceededException]
    B -- son tarih geçti --> D
    B -- hayır --> C[Gerçek model çağrısı]
    C --> A
```

Sonuç: **bir tool 90 dakika sürerse o tool kesilmez.** Kesme, o tool bittikten
sonra yapılacak ilk model çağrısında olur. Bu bir eksiklik değil, aynı
tasarımın devamıdır ve **dokümanda açıkça yazılır** — bir tüketici
`MaxDuration`'ı "sert bir zaman aşımı" sanmamalıdır.

`TryReserveRun` (çocuk run başlatma) da son tarihi kontrol eder: süresi dolmuş
bir ağaçta yeni çocuk run başlamaz.

## 128.3 — Hata sınıflandırması

K-630 aynen uygulanır: kesme **yeni bir `RunErrorClass` üyesi açmaz**, mevcut
`QuotaExceeded`'a eşlenir. `AgentPrismRunBudgetExceededException` yeniden
kullanılır; yalnız mesajı hangi boyutun dolduğunu söyler.

🚨 `AgentRunBudget.DescribeModelCallExhaustion()` bugün token ve maliyeti
anlatan **elle yazılmış** bir ifadedir. Ona üçüncü bir terim eklemek K-483'ün
tam sınıfıdır: mesajı üreten yer ile `IsExhausted`'ı hesaplayan yer aynı
listeyi görmelidir. Uygulayan oturum iki ifadeyi **tek** kaynaktan türetsin.

---

## Planlanan Public API

```csharp
// AgentPrism.Abstractions
public sealed class AgentRunBudget
{
    // … mevcut dört boyut …
    public TimeSpan? MaxDuration { get; init; }
    public DateTimeOffset? Deadline { get; }
}

// AgentPrism.Core
public sealed class AgentPrismAgentGraphOptions
{
    // … mevcut dört alan …
    /// <summary>Zero means no time limit. Default: zero (K1 — no surprise).</summary>
    public TimeSpan MaxDuration { get; set; }
}
```

**Varsayılan sıfırdır** — yani bugünkü davranış birebir korunur. Diğer dört
boyutun bir kısmı varsayılan bir tavan taşıyor (`MaxTotalTokens = 200_000`);
süre taşımaz, çünkü makul bir varsayılan süre yoktur: bir eval koşumu ile bir
sohbet turu aynı ölçekte değildir.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Runs/AgentRunBudget.cs   (değişir)
src/AgentPrism.Core/
├── AgentPrismOptions.cs                             (değişir — AgentGraph.MaxDuration + CreateBudget)
├── Models/RunBudgetChatClient.cs                    (değişir — son tarih kontrolü)
└── AgentPrismServiceCollectionExtensions.Binding.*.cs  (değişir — yapılandırma bağlama)

tests/AgentPrism.Core.UnitTests/Runs/RunDurationBudgetTests.cs   (yeni)
tests/AgentPrism.AspNetCore.FunctionalTests/RunDeadlineTests.cs  (yeni)

docs-site/src/content/docs/concepts/governance.md    (değişir)
docs-site/src/content/docs/guides/production.md      (değişir)
docs-site/src/content/docs/capabilities.md           (değişir)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Son tarih geçti, bir sonraki model çağrısı yine de yapılıyor | Birim | `RunDurationBudgetTests` (sahte `TimeProvider`) |
| Kesme bir model turunun **ortasında** oluyor (yarım mesaj) | Fonksiyonel | `RunDeadlineTests` — son olay `RunFailed` olmalı, yarım mesaj değil |
| Çocuk run'lar kökten farklı bir son tarih görüyor | Birim | `RunDurationBudgetTests` |
| `MaxDuration` ayarlı değilken (sıfır) davranış değişiyor | Birim | `RunDurationBudgetTests` — regresyon |
| Hata `QuotaExceeded` yerine yeni bir sınıfa düşüyor | Birim | `RunDurationBudgetTests` — K-630 |
| Mesaj hangi boyutun dolduğunu söylemiyor | Birim | `RunDurationBudgetTests` |
| `DescribeModelCallExhaustion` ile `IsExhausted` farklı boyut listesi görüyor | Birim | `RunDurationBudgetTests` — 🚨 K-483 sınıfı |
| Kuyruktaki (dayanıklı) run'da son tarih uygulanmıyor | **Fonksiyonel** | `RunDeadlineTests` — sınır: kuyruk |
| Bağlam sıkıştırmanın özetleme çağrısı son tarihi görmüyor | Birim | `RunDurationBudgetTests` — aynı boru hattından geçer, görmelidir |
| İptal ile son tarih birbirine karışıyor (iptal `Canceled`, son tarih `QuotaExceeded` olmalı) | Fonksiyonel | `RunDeadlineTests` |
| Gerçek saate bağlanıp test kırılgan oluyor | — | `TimeProvider` zorunlu; DoD'de ayrı satır |
| Başka kiracının run'ı etkileniyor | — | Bütçe run ağacına özeldir, kiracı sınırı geçmez; gerekçe budur ve kiracı testi **gereksizdir** |

Beş soru: **iptal** → ayrı satır · **eşzamanlılık** → son tarih değişmez bir
değerdir, `Interlocked` gerekmez (diğer dört boyuttan farkı budur ve yazılır) ·
**boş/aşırı girdi** → sıfır ve negatif `TimeSpan` ile `TimeSpan.MaxValue` ·
**başka kiracı** → yukarıda gerekçelendi · **alt sistem hatası** → alt sistem yok.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `AgentGraph.MaxDuration = 00:00:05`, her çağrısı 3 saniye süren bir tool | Çok turlu bir `run` | Run kesilir; `runs.error_class` = `QuotaExceeded`; kesme tool ortasında değil, tool bittikten sonraki model çağrısında |
| 2 | Aynı ayar, kuyruğa alınmış (`202 Accepted`) run | Aynı senaryo | Aynı davranış — kira yenilense de run kesilir |
| 3 | `MaxDuration` ayarlı değil | Uzun bir run | Davranış Faz 127 ile birebir aynı |
| 4 | `MaxDuration` dolmuşken kullanıcı ayrıca iptal eder | Cancel ucu | Hata sınıfı `Canceled`, `QuotaExceeded` değil |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `MaxDuration` çalışma isteğiyle (`AgentPrismRunOptions`) run başına geçilebilmeli mi? | A: Yalnız yapılandırma · B: İstek başına da | **A** — istek başına süre, çağıranın kendi son tarihini sunucuya dayatmasıdır; bugün ölçülmüş bir ihtiyaç yok. B istenirse ayrı kalem |
| 2 | Son tarih yaklaşırken bir uyarı olayı yazılmalı mı? | A: Hayır · B: Evet, varsayılan kapalı | **A** — olay hacmi artar; kesmenin kendisi zaten bir olaydır |
| 3 | Tool timeout'u kalan süreye göre kısaltılmalı mı? | A: Hayır · B: Evet | **A** — iki mekanizmayı birbirine bağlamak, birini değiştirmeyi diğerini bozmak hâline getirir. Ancak ölçülürse Açık Soru olarak kapanışta yazılır |

---

## Bitiş Ölçütleri (DoD)

- [ ] `MaxDuration` dolduğunda bir sonraki model çağrısı **yapılmaz**; run `QuotaExceeded` ile kapanır
- [ ] Kesme her zaman iki model turu arasındadır; kesilen run'ın son olayı yarım bir model mesajı **değildir** — gerçek koşumda ölçüldü ve çıktı belgeye yazıldı
- [ ] Çocuk run'lar kökle **aynı** son tarihi görür
- [ ] Kuyruğa alınmış run'da da son tarih uygulanır — `RunDeadlineTests`
- [ ] `MaxDuration` ayarlı değilken (sıfır) davranış bugünküyle **aynıdır**
- [ ] Kesme mesajı hangi boyutun dolduğunu söyler; mesaj ile `IsExhausted` **tek** kaynaktan türer (K-483)
- [ ] Süre `TimeProvider` üzerinden okunur; hiçbir test gerçek saate bağlı değildir
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` içine eklendi
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# Süre tavanı gerçekten kesiyor mu
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests --filter-method "*RunDurationBudget*"

# Kuyruktaki run'da (sınır: kuyruk)
./artifacts/bin/AgentPrism.AspNetCore.FunctionalTests/release/AgentPrism.AspNetCore.FunctionalTests --filter-method "*RunDeadline*"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Tüketici `MaxDuration`'ı sert zaman aşımı sanır ve uzun bir tool'un kesileceğini bekler | Sınır `concepts/governance.md`'de **açıkça** yazılır; tool timeout'u ile farkı aynı paragrafta anlatılır |
| Mesaj ifadesi ile `IsExhausted` ifadesi ayrışır | DoD'de ayrı satır; K-483'ün vakası hafızada yazılı |
| Testler gerçek saate bağlanıp kırılganlaşır | `TimeProvider` DoD'de ayrı satır |
| Varsayılan bir süre konulur ve var olan uzun run'lar sessizce kesilir | Varsayılan **sıfır**; K1'in gereği ve DoD'de regresyon satırı var |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.

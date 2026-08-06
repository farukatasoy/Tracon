# Faz 49 — Çevrimiçi Değerlendirme (üretim trafiğinde yargıç)

> **Durum:** 📋 Planlandı (2026-08-06)
> **Kaynak:** [UCUNCU-FAZ-ADAYLARI.md](UCUNCU-FAZ-ADAYLARI.md) · **F-71**
> **Önkoşul:** 🚨 [Faz 31](31-GERI-BILDIRIM-VE-PUANLAMA.md) — `run_scores` tablosu ve `IRunScoreStore` oradan gelir. **Bu faz kendi puan tablosunu AÇMAZ**
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** **Yok** — puan tablosu Faz 31'indir; bu faz yalnız `RunScoreKind`'a bir üye ekler
> **Public API:** büyüyor — bir arayüz, bir `JobKind` üyesi, bir `RunScoreKind` üyesi, iki ayar sınıfı, iki kayıt tipi. Faz 7'den önce ucuz

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-139\|K-140\|K-151\|K-160\|K-162\|K-232" docs/KARARLAR.md
   ```
   🚨 **K-140** (AI yargıç Faz 18 kapsamı dışında bırakıldı; **yeniden açılma
   koşulu tam olarak bu fazdır** — "kullanıcı model tabanlı puanlama isterse
   maliyet uyarısı ve model seçimi ile ayrı bir faz açılır"),
   **K-139** (MAF'ın `LocalEvaluator`'ı kullanılır; ikinci bir eval çerçevesi
   yazılmaz), **K-151** (iki ayrı alan deseni — yargıç maliyeti ayrı
   hesaplanmalıdır), **K-160** (kuyruk geri adımlı bekleme), **K-162**
   (kota devam eden çalıştırmayı kesmez), **K-232** (yanıtlar çevrilmez).
3. [`31-GERI-BILDIRIM-VE-PUANLAMA.md`](31-GERI-BILDIRIM-VE-PUANLAMA.md) —
   **planlanan public API bölümünün tamamı** ve devir notu:
   ```bash
   awk '/## Planlanan Public API/,/## Planlanan Dosya/' docs/31-GERI-BILDIRIM-VE-PUANLAMA.md
   awk '/## Sonraki Faza Devir Notu/,0' docs/31-GERI-BILDIRIM-VE-PUANLAMA.md
   ```
   🚨 Bu fazın tüm çıktısı o fazın `run_scores` tablosuna yazılır. Sözleşme
   oradan devralınır ve **değiştirilmez**.
4. [`18-DEGERLENDIRME.md`](18-DEGERLENDIRME.md) — yalnız "İki ayrı kavram"
   bölümü ve devir notu. Eval işi, `EvalCheckRegistry` ve `LocalEvaluator`
   deseni oradan gelir.
5. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/maf-api.md`](hafiza/maf-api.md) (**ana kaynak** — MAF eval ve
   yargıç tipleri),
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md)
   (`RunKind`, maliyet kaydı, örnekleme),
   [`hafiza/frontend.md`](hafiza/frontend.md) (sözlük, bundle)
6. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) — değerlendirme ve iş kuyruğu bölümleri

---

## Amaç

Bugün kalite yalnız **elle yazılmış eval takımlarında**, yalnız **tetiklenince**
ölçülüyor. Üretim trafiği hiç puanlanmıyor. Bir talimat değişikliği kaliteyi
düşürürse bunu ilk fark eden kullanıcıdır.

Bu faz, örneklenmiş üretim çalıştırmalarını arka planda puanlar. Puan Faz 31'in
`run_scores` tablosuna yazılır — insan puanıyla **aynı tabloya**, farklı bir
kaynakla. Böylece iki sinyal karşılaştırılabilir ve yargıç kalibre edilebilir.

- **F-71** — örnekleme, arka plan puanlama işi, yargıç genişleme noktası,
  ayrı maliyet hesabı ve eşik aşımında Faz 21'in webhook'u.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`EvalJobHandler.cs:30`](../src/AgentPrism.Core/Evaluation/EvalJobHandler.cs) | Eval yalnız `JobKind.Eval` ile, yalnız bir **takım** üzerinde koşar. Girdi bir `EvalSuite`'tir; üretim çalıştırması değil |
| [`JobKind.cs`](../src/AgentPrism.Abstractions/Scheduling/JobKind.cs) | Beş üye. Üretim trafiğini puanlayan bir iş türü **yok** |
| [`EvalCheckRegistry.cs`](../src/AgentPrism.Core/Evaluation/EvalCheckRegistry.cs) | Altı yerleşik denetim: `nonEmpty`, `containsExpected`, `keywords`, `toolCalled`, `toolCallsPresent`, `hasImageContent`. **Hiçbiri model çağırmaz** — K-140'ın bilinçli sonucu |
| [`EvalJobHandler.cs:125`](../src/AgentPrism.Core/Evaluation/EvalJobHandler.cs) | `new LocalEvaluator([.. checks])` — MAF'ın değerlendiricisi kullanılıyor (K-139) |
| [`MAF-GENISLEME-NOKTALARI.md:315`](MAF-GENISLEME-NOKTALARI.md) | `AIJudgeLoopEvaluator · LoopAgent → planlanmadi (eval'den AYRI kavram)` |
| [`InMemoryRunStore.cs:333`](../src/AgentPrism.Core/Storage/InMemoryRunStore.cs) | 🚨 **`RunKind.Eval` çalıştırmaları `RunStatistics`'ten ZATEN HARİÇ TUTULUYOR.** "Eval vaka çalıştırmaları sentetik test çağrılarıdır, gerçek trafik değildir" |
| [`SqlRunStore.cs:214`](../src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs) | Aynı dışlama SQL tarafında da var: `DbHelpers.Add(command, "kind_eval", (short)RunKind.Eval)` |

> Kanıtlar 2026-08-06 tarihinde doğrulandı.

### 🚨 MAF imzaları ölçüldü — `AIJudgeLoopEvaluator` bu iş için uygun DEĞİL

Aday listesi "`AIJudgeLoopEvaluator` ve `LoopAgent` **var**; imzaları
doğrulanmadı" diyordu. Ölçüldü (MAF 1.16.0):

```
AIJudgeLoopEvaluator : LoopEvaluator  [sealed]
    ctor(IChatClient judgeClient, AIJudgeLoopEvaluatorOptions options)
    virtual ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken ct)

AIJudgeLoopEvaluatorOptions
    prop IEnumerable<String> Criteria
    prop String Instructions
    prop String FeedbackMessageTemplate

LoopContext  [sealed]
    ctor(AIAgent agent, AgentSession session, IReadOnlyList<ChatMessage> initialMessages,
         AgentResponse lastResponse, AgentRunOptions runOptions)

LoopEvaluation  [sealed]
    static LoopEvaluation Continue(String feedback)
    static LoopEvaluation Stop()
    prop String  Feedback       { get; }
    prop Boolean ShouldReinvoke { get; }
```

**Tipler var, ama işi yapmıyorlar.** İki ölçülmüş sebep:

| Sebep | Ayrıntı |
|---|---|
| 🚨 **`LoopEvaluation` bir PUAN döndürmez** | Yalnız `ShouldReinvoke` (bool) ve `Feedback` (metin) taşır. Çevrimiçi değerlendirmenin ürünü bir **sayıdır**; bu tip onu üretemez |
| 🚨 **`LoopContext` CANLI bir çalıştırma ister** | Kurucusu `AIAgent` **ve** `AgentSession` istiyor. Bitmiş bir çalıştırmayı puanlamak için sentetik bir agent ve oturum kurmak gerekirdi — çalışan bir şeyi taklit etmek |

`LoopAgent` ise bambaşka bir yetenektir: bir çalıştırmayı **yeterli olana kadar
tekrarlar**. Bu bir ölçüm değil, bir kontrol akışıdır. K-140 bunu zaten
söylemişti ve ölçüm onu doğruluyor.

**Sonuç:** Bu faz `AIJudgeLoopEvaluator` kullanmaz. Yargıç, MAF'ın
`LocalEvaluator`/`EvalCheck` deseninin **model çağıran** bir kardeşi olarak
yazılır ve K-139 korunur: ikinci bir eval **çerçevesi** yazılmaz, var olan
denetim yuvasına model çağıran bir denetim eklenir.

---

## 49.1 — Akış

```mermaid
flowchart TD
    A["calistirma tamamlandi<br/>RunStatus.Completed"] --> B{"orneklendi mi?<br/>SampleRate"}
    B -->|hayir| Z["hicbir sey olmaz"]
    B -->|evet| C["JobKind.OnlineEval kuyruga"]
    C --> D["OnlineEvalJobHandler"]
    D --> E["run_inputs + cikti okunur"]
    E --> F["IRunJudge.JudgeAsync"]
    F --> G["run_scores<br/>Source = judge:{ad}"]
    G --> H{"puan esigin altinda mi?"}
    H -->|evet| I["Faz 21 webhook<br/>run.score.low"]
    H -->|hayir| Z2["bitti"]

    classDef yeni fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    classDef para fill:#7a4a1f,stroke:#3d2510,color:#ffffff
    class C,D,F,G yeni
    class F para
```

Turuncu kutu **para harcar**. Bu fazın her tasarım kararı o kutunun etrafında
döner.

## 49.2 — Örnekleme: varsayılan sıfır

🚨 **Yargıç her puanlamada bir model çağrısı yapar ve gerçek para harcar.**
Bu, K1'in en sert uygulanması gereken yerdir.

| Ayar | Varsayılan | Gerekçe |
|---|---|---|
| `Enabled` | `false` | Faz 48 ile aynı düz K1 okuması |
| `SampleRate` | `0.0` | 🚨 Açılsa bile **hiçbir şey puanlanmaz** — oran ayrıca verilmelidir |
| `MaxScoresPerHour` | `100` | Örnekleme oranı yanlış hesaplansa bile bir üst sınır vardır |

**Neden iki kapı?** Çünkü `SampleRate` bir **orandır** ve trafik patlarsa
mutlak maliyet de patlar. Saatlik tavan, oranın hesap hatasına karşı ikinci
savunmadır. Aynı iki katmanlı desen `jobs_schedule_scheduled_uq` kısıtında
(K-138) ve kota + hız sınırı ayrımında (K-158) zaten var.

Örnekleme **deterministiktir**: `runId`'nin özetinden türetilir. Aynı
çalıştırma iki kez değerlendirilmez ve yeniden deneme yeni bir zar atmaz.

### Neler örneklenmez

| Dışlanan | Neden |
|---|---|
| `RunKind.Eval` çalıştırmaları | Sentetik trafik. Zaten `RunStatistics`'ten de hariç |
| Başarısız çalıştırmalar | Hata sınıflandırmanın işidir ([Faz 44](44-HATA-SINIFLANDIRMA.md)); yargıç bir hatayı puanlayamaz |
| Alt agent çalıştırmaları (`Depth > 0`) | Kök çalıştırma zaten puanlanır; ağacın her düğümünü puanlamak maliyeti derinlikle çarpar |
| 🚨 **Yargıcın kendi çalıştırmaları** | Sonsuz döngü. Yargıç `RunKind.Eval` ile kaydedilir ve o tür zaten dışlanır |

## 49.3 — Yargıç maliyeti nasıl ayrılır

K-151'in kuralı: "iki ayrı alan". Ama bu faz **yeni bir alan açmaz** — mekanizma
zaten var ve ölçüldü.

🚨 **Yargıç çalıştırması `RunKind.Eval` ile kaydedilir.** İki depo uygulaması da
o türü `RunStatistics`'ten **zaten** dışlıyor
([`InMemoryRunStore.cs:333`](../src/AgentPrism.Core/Storage/InMemoryRunStore.cs),
[`SqlRunStore.cs:214`](../src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs)).
Bu, Faz 18'in açık soru 4'ünde alınmış bir karardır ve bu fazın ihtiyacını
kelimesi kelimesine karşılar:

> "Eval vaka çalıştırmaları sentetik test çağrılarıdır, gerçek trafik değildir;
> özeti kirletmemesi için hariç tutulur."

Sonuç: agent'ın maliyeti **şişmez**, hiçbir yeni sütun gerekmez ve
`EvalJobHandler` ile aynı desen kullanılır.

🚨 **Ama maliyet görünmez olmamalıdır.** Dışlanmak "yok sayılmak" değildir.
Yargıç maliyeti iki yerde görünür:

| Yer | Nasıl |
|---|---|
| `GET /api/runs?kind=Eval` | Yargıç çalıştırmaları listelenebilir olmalıdır — `RunQuery`'ye bir `Kind` süzgeci eklenir |
| `agentprism.judge.cost` metriği | [Faz 35](35-MALIYET-VE-KOTA-METRIKLERI.md)'in enstrüman ailesine katılır |

## 49.4 — Yargıç genişleme noktası

```csharp
public interface IRunJudge
{
    string Name { get; }
    ValueTask<RunJudgment> JudgeAsync(RunJudgeContext context, CancellationToken ct = default);
}
```

Yerleşik uygulama `ModelRunJudge`'dır: bir `IChatClient` ile ölçüt listesini
sorar ve **yapılandırılmış çıktı** ister.

🚨 **Yapılandırılmış çıktı [Faz 38](38-YAPILANDIRILMIS-CIKTI.md)'in işidir ve o
faz bu fazdan önce bitmelidir — ya da yargıç kendi ayrıştırmasını yazar.**
Faz 38'in ölçümü ilgilidir: `ChatResponseFormat.ForJsonSchema`'nın üç aşırı
yüklemesinden **ikisi yansımaya dayanır**; AOT duruşu için yalnız `JsonElement`
alan kullanılır. Yargıç `AgentPrism.Core` içindedir ve **AOT uyumlu kalmalıdır**.

Bağımlılık zorunlu değildir: Faz 38 bitmemişse yargıç düz metin ister ve tek
bir sayıyı ayrıştırır. Bu bir **kırılganlıktır** ve Açık Soru 2'dedir.

### Puan biçimi

Faz 31'in `RunScore.Value` alanı `int`'tir ve `RunScoreKind` iki değer taşır:
`Binary = 1`, `Stars = 2`. Yargıç puanı ikisine de tam oturmaz.

```csharp
public enum RunScoreKind
{
    Binary = 1,
    Stars = 2,
    Numeric = 3,   // YENI — 0..100 tamsayi yuzde
}
```

🚨 **Bu, Faz 31'in public sözleşmesine bir değer eklemektir.** Faz 31 henüz
kodlanmadı; iki faz aynı turda ise **`Numeric` üyesi baştan Faz 31'e konmalıdır**
ve bu faz onu yalnız kullanır. Devir notu bunu taşır.

`Source` alanı `judge:{name}` biçiminde yazılır — insan puanı `human` yazar.
Böylece iki sinyal aynı tabloda ayrılabilir ve kalibrasyon sorgulanabilir:

```sql
SELECT s.source, avg(s.value) FROM agentprism.run_scores s GROUP BY s.source;
```

## 49.5 — Eşik ve webhook

Puan bir eşiğin altındaysa Faz 21'in webhook'u tetiklenir: `run.score.low`.

🚨 **Tek bir düşük puan bir olay değildir.** Model gürültülüdür ve tek örnek
üzerinden alarm üretmek nöbetçi mühendisi eğitir: bildirimler yok sayılmaya
başlar.

| Kural | Değer |
|---|---|
| Asgari örnek sayısı | `MinSampleSize`, varsayılan `20` |
| Pencere | `EvaluationWindow`, varsayılan 1 saat |
| Eşik | `LowScoreThreshold`, varsayılan `60` (0–100 ölçeğinde) |

Alarm, **pencere içindeki ortalama** eşiğin altındaysa ve örnek sayısı asgariyi
aştıysa üretilir. Aynı desen [Faz 44](44-HATA-SINIFLANDIRMA.md)'ün eşik
mantığında ve aday listesindeki F-74'te (kanarya) tekrar edecektir; üçünün
**aynı** kuralı kullanması önemlidir.

## 49.6 — Yargıç hata verirse

`IRunJudge` bir model çağırır ve model çağrıları başarısız olur. Kural nettir:

🚨 **Yargıcın başarısızlığı puanlanan çalıştırmayı ETKİLEMEZ.** Çalıştırma çoktan
bitmiştir; yargıç arka plandadır. Bu, "gözlemlenebilirlik işlevselliği bozmaz"
kuralının doğrudan uygulamasıdır.

| Durum | Davranış |
|---|---|
| Yargıç modeli hata verir | İş kuyruğunun geri adımlı beklemesiyle yeniden denenir (K-160) |
| `MaxAttempts` aşılır | İş `Failed`; puan yazılmaz. Çalıştırma **etkilenmez** |
| Yargıç çıktısı ayrıştırılamaz | Puan yazılmaz; hata loglanır. Sessiz bir `0` **yazılmaz** — sıfır bir ölçümdür, ölçüm yokluğu değildir |
| Devre kesici açık | İş yeniden denenir; sağlayıcı kapalıyken yargıç ısrar etmez |

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions/Evaluation/IRunJudge.cs (YENI)

/// <summary>
/// Tamamlanmis bir uretim calistirmasini puanlayan genisleme noktasi.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Bu arayuz MAF'in <c>AIJudgeLoopEvaluator</c>'ini SARMALAMAZ. Olculdu
/// (MAF 1.16.0): <c>LoopEvaluation</c> bir PUAN dondurmez (yalnizca
/// <c>ShouldReinvoke</c> ve <c>Feedback</c>) ve <c>LoopContext</c> canli bir
/// <c>AIAgent</c> + <c>AgentSession</c> ister. Bitmis bir calistirmayi
/// puanlamak icin uygun degildir.
/// </para>
/// <para>K4: kayit <c>TryAddEnumerable</c> ile; tuketicinin kaydi kazanir.</para>
/// </remarks>
public interface IRunJudge
{
    /// <summary>Yargicin adi. <c>RunScore.Source</c> alanina <c>judge:{Name}</c> yazilir.</summary>
    string Name { get; }

    /// <summary>Calistirmayi puanlar.</summary>
    ValueTask<RunJudgment> JudgeAsync(
        RunJudgeContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Yargicin gordugu baglam.</summary>
public sealed record RunJudgeContext
{
    public required Guid RunId { get; init; }
    public required string TenantId { get; init; }
    public required string AgentName { get; init; }

    /// <summary>
    /// Calistirmanin girdisi. 🚨 <c>run_inputs</c>'tan okunur
    /// ([Faz 47](47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md)); kayit yoksa
    /// calistirma orneklenmez.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Input { get; init; }

    /// <summary>Calistirmanin cikti metni.</summary>
    public required string Output { get; init; }

    /// <summary>Cagrilan tool adlari. Bazi olcutler bunu ister.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];
}

/// <summary>Yargicin karari.</summary>
public sealed record RunJudgment
{
    /// <summary>Puan, 0–100. Yargic karar veremediyse <see langword="null"/>.</summary>
    /// <remarks>
    /// 🚨 Karar verilemedigi durumda <c>0</c> DEGIL <see langword="null"/>
    /// dondurulur. Sifir bir olcumdur; olcum yoklugu degildir.
    /// </remarks>
    public int? Score { get; init; }

    /// <summary>Kisa gerekce. <c>RunScore.Comment</c> alanina yazilir.</summary>
    public string? Reason { get; init; }

    /// <summary>Yargicin kendi model kullanimi. Maliyet raporuna girer.</summary>
    public RunUsage? JudgeUsage { get; init; }
}
```

```csharp
// AgentPrism.Abstractions/Runs/RunScoreKind.cs — Faz 31'in enum'una SONA bir uye
public enum RunScoreKind
{
    Binary = 1,
    Stars = 2,

    /// <summary>0–100 arasi tamsayi puan. Model tabanli yargic bunu uretir.</summary>
    Numeric = 3,
}

// AgentPrism.Abstractions/Scheduling/JobKind.cs — SONA bir uye
public enum JobKind
{
    // ... mevcut uyeler degismez
    /// <summary>Orneklenmis bir uretim calistirmasini puanlar (Faz 49).</summary>
    OnlineEval = 6,
}

// AgentPrism.Abstractions/Runs/RunQuery.cs — mevcut record'a bir alan
public sealed record RunQuery
{
    // ... mevcut alanlar
    /// <summary>Yalnizca bu turdeki calistirmalari getirir. Yargic calistirmalari <see cref="RunKind.Eval"/>'dir.</summary>
    public RunKind? Kind { get; init; }
}
```

```csharp
// AgentPrism.Core — ayarlar

/// <summary>Cevrimici degerlendirme ayarlari.</summary>
public sealed class OnlineEvaluationOptions
{
    /// <summary>🚨 Varsayilan KAPALI. Yargic para harcar (K1).</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Orneklenecek calistirma orani, 0.0–1.0.
    /// 🚨 Varsayilan <c>0.0</c>: <see cref="Enabled"/> acilsa bile oran
    /// verilmedikce hicbir sey puanlanmaz.
    /// </summary>
    public double SampleRate { get; set; }

    /// <summary>
    /// Saatte en fazla kac puanlama yapilir. Orneklemenin ikinci savunmasi.
    /// </summary>
    public int MaxScoresPerHour { get; set; } = 100;

    /// <summary>Yalnizca bu agent'lar puanlanir. Bos ise hepsi.</summary>
    public IList<string> AgentNames { get; } = [];

    /// <summary>Dusuk puan esigi (0–100).</summary>
    public int LowScoreThreshold { get; set; } = 60;

    /// <summary>🚨 Alarm icin gereken asgari ornek sayisi. Tek ornek alarm uretmez.</summary>
    public int MinSampleSize { get; set; } = 20;

    /// <summary>Ortalama hesabinin penceresi.</summary>
    public TimeSpan EvaluationWindow { get; set; } = TimeSpan.FromHours(1);
}

/// <summary>Yerlesik model tabanli yargicin ayarlari.</summary>
public sealed class ModelRunJudgeOptions
{
    /// <summary>
    /// Yargicin modeli. 🚨 Olculen agent'in modelinden AYRI secilir; ucuz bir
    /// model yeterlidir ve maliyeti dusurur.
    /// </summary>
    public required ModelBinding Model { get; set; }

    /// <summary>Puanlama olcutleri. Istemin govdesini olusturur.</summary>
    public IList<string> Criteria { get; } = [];

    /// <summary>Yargica verilen ek talimat.</summary>
    public string? Instructions { get; set; }
}
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/runs/{runId:guid}/feedback` | Reader | **Faz 31'in ucu.** Yargıç puanı da burada görünür (`source = judge:…`) |
| `GET` | `/api/evaluation/online` | Reader | Pencere içindeki ortalama puan, örnek sayısı, yargıç maliyeti |
| `POST` | `/api/runs/{runId:guid}/judge` | Operator | 🚨 Bir çalıştırmayı **elle** puanlatır. Örneklemeyi atlar; kalibrasyon ve hata ayıklama içindir |

Yeni bir puan **yazma** ucu yoktur — Faz 31'in `POST /api/runs/{runId}/feedback`
ucu insan puanı içindir; yargıç puanını arka plan işi yazar.

### Arayüz payı

| Ekran | İş |
|---|---|
| `run-detail.tsx` | Yargıç puanı insan puanının yanında; kaynak etiketi (`human` / `judge:…`) |
| `dashboard.tsx` | Pencere ortalaması ve örnek sayısı — Faz 31'in göstergesinin yanında |

Yeni bağımlılık **yok**. Bugünkü kullanım (2026-08-06):
**151,3 KB gzip / 250 KB**, kalan pay **98,7 KB**. Bu fazın payı **tahminî
1–2 KB gzip**'tir; gerçek değer uygulama anında `postbuild.mjs` çıktısından
okunur ve buraya yazılır.

Sözlük anahtarları `en.ts` **ve** `tr.ts` (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Evaluation/
├── IRunJudge.cs                    (YENI)
├── RunJudgeContext.cs              (YENI)
└── RunJudgment.cs                  (YENI)

src/AgentPrism.Abstractions/
├── Runs/RunScoreKind.cs            (Faz 31'in enum'una SONA bir uye)
├── Runs/RunSupportTypes.cs         (RunQuery'ye Kind suzgeci)
└── Scheduling/JobKind.cs           (SONA bir uye)

src/AgentPrism.Core/Evaluation/
├── ModelRunJudge.cs                (YENI — yerlesik yargic)
├── ModelRunJudgeOptions.cs         (YENI)
├── OnlineEvaluationOptions.cs      (YENI)
├── OnlineEvalJobHandler.cs         (YENI — JobKind.OnlineEval)
├── RunSampler.cs                   (YENI — deterministik ornekleme + saatlik tavan)
└── OnlineEvalSummaryService.cs     (YENI — pencere ortalamasi, esik, webhook)

src/AgentPrism.Core/Recording/
└── RunRecordingAgent.cs            (tamamlanan calistirmayi orneklemeye bildirir)

src/AgentPrism.Core/Diagnostics/
└── AgentPrismMetrics.cs            (agentprism.judge.cost, agentprism.judge.score)

src/AgentPrism.Sql.Shared/Stores/
└── SqlRunStore.cs                  (RunQuery.Kind suzgeci)

src/AgentPrism.PostgreSql/Internal/PostgresQueries.cs   (kind suzgeci)
src/AgentPrism.SqlServer/Internal/SqlServerQueries.cs   (ayni)
src/AgentPrism.Sqlite/Internal/SqliteQueries.cs         (ayni)

src/AgentPrism.AspNetCore/Endpoints/
└── EvalEndpoints.cs                (online ozet + elle puanlama)

src/AgentPrism.UI/frontend/src/
├── screens/run-detail.tsx
├── screens/dashboard.tsx
└── locales/{en,tr}.ts
```

**Migration yok.** `run_scores` Faz 31'in tablosudur ve `source` sütunu zaten
plandadır. `RunScoreKind` ve `JobKind` yalnız enum değeri ekler.

---

## Testler

| Test sınıfı | Neyi doğrular |
|---|---|
| `OnlineEvalDisabledTests` | 🚨 Varsayılan ayarlarla **hiçbir** çalıştırma puanlanmaz; yargıç modeli **hiç çağrılmaz** |
| `OnlineEvalSampleRateZeroTests` | 🚨 `Enabled = true` ama `SampleRate = 0` → yine hiçbir şey puanlanmaz |
| `OnlineEvalSamplingTests` | `SampleRate = 1.0` ile her tamamlanan çalıştırma bir iş açar |
| `OnlineEvalDeterministicSamplingTests` | Aynı `runId` iki kez değerlendirilmez; yeniden deneme yeni zar atmaz |
| `OnlineEvalHourlyCapTests` | 🚨 `MaxScoresPerHour` aşılınca örnekleme durur — oran ne olursa olsun |
| `OnlineEvalExclusionTests` | 🚨 `RunKind.Eval`, `Failed` ve `Depth > 0` çalıştırmaları **örneklenmez** |
| `OnlineEvalNoRecursionTests` | 🚨 Yargıcın kendi çalıştırması yeni bir yargıç işi **açmaz** |
| `OnlineEvalScoreWriteTests` | Puan `run_scores`'a `Kind = Numeric`, `Source = judge:{ad}` ile yazılır |
| `OnlineEvalCostSeparationTests` | 🚨 Yargıç çalıştırması `RunKind.Eval`'dir ve `RunStatistics`'e **girmez**; agent'ın maliyeti şişmez |
| `OnlineEvalCostVisibleTests` | Yargıç maliyeti `GET /api/runs?kind=Eval` ile **görünür**; `agentprism.judge.cost` metriği yazılır |
| `OnlineEvalNullScoreTests` | 🚨 Yargıç karar veremezse `null` döner ve **hiçbir puan yazılmaz** — sessiz `0` yazılmaz |
| `OnlineEvalJudgeFailureTests` | 🚨 Yargıç hatası puanlanan çalıştırmayı **etkilemez**; `runs` satırı değişmez |
| `OnlineEvalRetryTests` | Yargıç hatası geri adımlı beklemeyle yeniden denenir (K-160) |
| `OnlineEvalThresholdTests` | Ortalama eşiğin altında **ve** örnek sayısı yeterliyse webhook tetiklenir |
| `OnlineEvalMinSampleTests` | 🚨 Bir düşük puan alarm **üretmez**; `MinSampleSize` altında webhook yok |
| `OnlineEvalMissingInputTests` | `run_inputs` kaydı yoksa çalıştırma örneklenmez ve hata üretmez |
| `OnlineEvalTenantTests` | Örnekleme ve puan kiracı sınırını korur |
| `OnlineEvalManualJudgeTests` | `POST /api/runs/{id}/judge` örneklemeyi atlar ve puan yazar |
| `OnlineEvalAotTests` | 🚨 `AgentPrism.Core` AOT uyarısı üretmez — yargıcın JSON ayrıştırması yansımaya dayanmaz |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Girdi nereden okunur? | A: **`run_inputs`** ([Faz 47](47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md)) · B: `conversation_items` · C: yeni bir kayıt | **A.** Faz 47 tam olarak bu tabloyu açıyor ve ikinci bir girdi kaydı açmak iki yerde bakım demektir. Faz 47 bitmemişse yalnız oturumlu çalıştırmalar örneklenebilir ve bu sınır belgeye yazılır |
| 2 | Yargıç yapılandırılmış çıktı mı istesin? | A: **Faz 38 varsa evet** · B: her zaman düz metin ayrıştır | **A.** Düz metin ayrıştırma kırılgandır ve model biçimi değiştirdiğinde sessizce `null` üretir. Faz 38 bitmemişse B kullanılır ve ayrıştırma **başarısızlığı loglanır** |
| 3 | `RunScoreKind.Numeric` Faz 31'e mi eklensin? | A: **Faz 31'e** · B: bu fazda | **A.** İki faz aynı turdadır; enum'a iki kez dokunmak `PublicAPI.Unshipped.txt` disiplinini gereksiz karmaşıklaştırır. Faz 31 önce biterse üyeyi baştan taşır |
| 4 | Yargıç modeli ölçülen agent'ınkiyle aynı mı olsun? | A: **ayrı, ucuz bir model** · B: aynı model | **A.** Aynı model kendi çıktısını puanlarken sistematik olarak yanlıdır ve maliyeti ikiye katlar. Ayrı model **zorunlu ayar** olur |
| 5 | Örnekleme nerede tetiklenir? | A: **`RunRecordingAgent` tamamlanma noktasında** · B: ayrı bir arka plan tarayıcı | **A.** Çalıştırma zaten orada kapanıyor; ikinci bir tarayıcı `runs` tablosunu düzenli sorgular ve gereksiz yük üretir. 🚨 Ama örnekleme kararı **çalıştırmayı yavaşlatmamalıdır** — kuyruğa yazma `fire-and-forget` olmalı ve hatası yutulmalıdır |
| 6 | Yargıç kotayı tüketsin mi? | A: **hayır, ayrı sayaç** · B: evet | **A.** Kota tüketicinin agent kullanımını sınırlar; yargıç bir **kontrol düzlemi** maliyetidir. Kotaya yazmak kullanıcının kotasını görünmez biçimde yer. Ama maliyet **görünür** olmalıdır — `agentprism.judge.cost` bu yüzden zorunludur |
| 7 | Ölçütler agent tanımında mı yaşasın? | A: **global ayarda** · B: agent tanımında | **A** bir taslaktır. Agent başına ölçüt daha değerlidir ama `AgentDefinition`'a alan eklemek bir sözleşme değişikliğidir; ihtiyaç **ölçülmeden** yapılmaz |
| 8 | Puan 0–100 mü 0–1 mi? | A: **0–100 tamsayı** · B: 0–1 ondalık | **A.** Faz 31'in `RunScore.Value` alanı `int`'tir; ondalık için tipi değiştirmek o fazın sözleşmesini kırar |

---

## Bitiş Ölçütleri (DoD)

- [ ] 🚨 Varsayılan ayarlarla (`Enabled = false`, `SampleRate = 0`) yargıç
      modeli **hiç çağrılmaz** ve tek kuruş harcanmaz
- [ ] `SampleRate = 1.0` ile her tamamlanan çalıştırma puanlanır ve puan
      `run_scores`'a `Source = judge:{ad}` ile yazılır
- [ ] 🚨 `MaxScoresPerHour` aşılınca örnekleme durur
- [ ] 🚨 Yargıcın kendi çalıştırması yeni bir yargıç işi **açmaz**
- [ ] 🚨 Yargıç çalıştırması `RunKind.Eval`'dir ve agent'ın `RunStatistics`
      maliyetine **girmez**; `GET /api/runs?kind=Eval` ile **görünür**
- [ ] 🚨 Yargıç karar veremezse puan **yazılmaz** — sessiz `0` yazılmaz
- [ ] 🚨 Yargıç hatası puanlanan çalıştırmayı etkilemez; `runs` satırı değişmez
- [ ] Ortalama eşiğin altında **ve** örnek sayısı yeterliyse `run.score.low`
      webhook'u tetiklenir; tek düşük puan alarm üretmez
- [ ] İnsan puanı ile yargıç puanı aynı tabloda ayrılabilir
      (`SELECT source, avg(value) … GROUP BY source`)
- [ ] `agentprism.judge.cost` ve `agentprism.judge.score` metrikleri yazılır
- [ ] `POST /api/runs/{id}/judge` örneklemeyi atlar
- [ ] 🚨 `AgentPrism.Core` AOT uyarısı üretmez
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, **gerçek bir yargıç
      çağrısı** ölçüldü ve maliyeti bu belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve buraya yazıldı

### Doğrulama komutları

```bash
# 1) Varsayilan — yargic HIC calismamali
RUN=$(curl -s -X POST http://localhost:5081/agentprism/api/agents/asistan/run \
  -H "content-type: application/json" -d '{"message":"merhaba"}' | jq -r '.runId')
sleep 3
curl -s "http://localhost:5081/agentprism/api/runs/$RUN/feedback" | jq 'length'
#    beklenen: 0

psql -tA "$AGENTPRISM_CONN" -c \
  "SELECT count(*) FROM agentprism.runs WHERE kind = 2;"   -- RunKind.Eval
#    beklenen: 0

# --- ornek uygulamada acilir: Enabled = true, SampleRate = 1.0 ---

# 2) Puanlandi mi
RUN2=$(curl -s -X POST http://localhost:5081/agentprism/api/agents/asistan/run \
  -H "content-type: application/json" -d '{"message":"istanbul nerede"}' | jq -r '.runId')
sleep 10
curl -s "http://localhost:5081/agentprism/api/runs/$RUN2/feedback" \
  | jq '.[] | {kind, value, source, comment}'

# 3) 🚨 Maliyet ayrimi — agent'in ozeti sismemis olmali
curl -s "http://localhost:5081/agentprism/api/runs/statistics?agentName=asistan" \
  | jq '{totalRuns, totalCost}'
curl -s "http://localhost:5081/agentprism/api/runs?kind=Eval" \
  | jq '[.[] | .cost.amount] | add'
#    ikinci deger BIRINCIYE dahil OLMAMALI

# 4) 🚨 Ozyineleme yok — yargic calistirmasi yeni yargic isi acmamali
psql -tA "$AGENTPRISM_CONN" -c \
  "SELECT count(*) FROM agentprism.jobs WHERE kind = 6;"   -- JobKind.OnlineEval
#    calistirma sayisi kadar olmali, iki kati DEGIL

# 5) Kaynak ayrimi
psql "$AGENTPRISM_CONN" -c \
  "SELECT source, count(*), round(avg(value),1) FROM agentprism.run_scores GROUP BY source;"

# 6) Saatlik tavan
curl -s http://localhost:5081/agentprism/api/evaluation/online | jq

# 7) Elle puanlama
curl -s -X POST "http://localhost:5081/agentprism/api/runs/$RUN/judge" | jq

# 8) Metrikler
curl -s http://localhost:5081/metrics | grep -E "judge_cost|judge_score"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 **Yargıç para harcar ve fatura sessizce büyür** | İki kapı: `Enabled = false` **ve** `SampleRate = 0`. Üçüncü savunma `MaxScoresPerHour`. Maliyet `agentprism.judge.cost` ile görünür |
| 🚨 Yargıç kendi çalıştırmasını puanlar → sonsuz döngü | Yargıç `RunKind.Eval` ile kaydedilir; o tür örneklemeden **hariçtir**. Ayrı bir test doğrular |
| Yargıç maliyeti agent'ın maliyetine karışır | `RunKind.Eval` dışlaması **zaten var** (K-151 deseni); test bunu doğrular |
| Maliyet dışlanınca **görünmez** olur | `RunQuery.Kind` süzgeci ve ayrı metrik zorunludur |
| 🚨 Tek düşük puan alarm üretir ve bildirimler yok sayılmaya başlar | `MinSampleSize` ve pencere ortalaması zorunludur |
| Yargıç hatası çalıştırmayı etkiler | Yargıç arka plandadır; çalıştırma çoktan bitmiştir. Ayrı bir test doğrular |
| Ayrıştırılamayan çıktıda sessiz `0` yazılır | `Score` `int?`'tir; `null` puanı **yazmaz** |
| Örnekleme çalıştırmayı yavaşlatır | Kuyruğa yazma `fire-and-forget`; hatası yutulur (Açık Soru 5) |
| Yargıç modeli ölçülen modelle aynı olursa yanlı puan verir | Yargıç modeli **zorunlu ayrı ayardır** |
| `run_inputs` yoksa yargıç çıplak çalışır | Girdisi olmayan çalıştırma **örneklenmez**; sessiz yarım puanlama yapılmaz |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.
>
> **Not:** Üç karar **mutlaka** kayda geçmelidir:
> 1. 🚨 **`AIJudgeLoopEvaluator` KULLANILMADI** ve ölçülen gerekçe:
>    `LoopEvaluation` puan döndürmez, `LoopContext` canlı agent+oturum ister.
>    K-140'ın yeniden açılma koşulu bu fazdır; kapanışta o karar
>    **güncellenmelidir**.
> 2. **Yargıç maliyeti `RunKind.Eval` dışlamasıyla ayrılır** — yeni sütun
>    açılmadı. Faz 18'in açık soru 4 kararının ikinci tüketicisi olduğu
>    yazılmalıdır.
> 3. **İki kapılı varsayılan** (`Enabled = false` **ve** `SampleRate = 0`) ve
>    gerekçesi.
>
> Gerçek bir yargıç çağrısının ölçülen maliyeti de buraya yazılır.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
>
> **Not:** Üç devir bilgisi zorunludur:
> 1. 🚨 **Aday listesindeki F-74 (kanarya yayını ve otomatik geri alma) artık
>    yapılabilir.** Üç önkoşulunun üçü de tamamlanmış olur: Faz 31 (insan
>    puanı), Faz 44 (hata sınıfı) ve bu faz (otomatik puan). F-74'ün eşik
>    mantığı bu fazın `MinSampleSize` + pencere kuralını **aynen** kullanmalıdır;
>    üçüncü bir eşik kuralı yazılmamalıdır.
> 2. **`IRunJudge` yeni bir arayüzdür** ve Faz 7'den önce eklendi. Metot
>    eklemek yayından sonra kırıcıdır.
> 3. **Ölçüt yeri açık kaldı** (Açık Soru 7): global ayar seçildi; agent başına
>    ölçüt ihtiyacı ölçülürse `AgentDefinition`'a alan eklemek bir sözleşme
>    değişikliğidir ve Faz 7'den önce ucuzdur.

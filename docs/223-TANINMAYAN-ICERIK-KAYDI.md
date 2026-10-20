# Faz 223 — Tanınmayan MAF İçeriği ve Workflow Olayının Kaydı

> **Durum:** ✅ Tamamlandı (2026-10-20)
> **Kademe:** tam — geri dönüşü pahalı karar: iki yeni ayrılmış olay tipi adı `run_events`'e kalıcı yazılır ve istemci onlara göre süzer; ad sonradan değişirse kayıtlı veri eski adla kalır. Güvenlik sınırı: yeni olayların yükü `RecordToolPayloads` kuralına bağlanır ve `secret` taşıyabilecek alan (URI sorgusu) yazılmaz (K-059)
> **Plan onayı:** Bakımcı, 2026-10-19 (sohbette: Dalga 2 sırası; açık sorular uygulama başında sorulur)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-589** — MAF kapsama matrisi G-04 / C-10 ([`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §7.0, §8), İ3 "tanınmayan kaybolmaz"
> **Genellik:** run kaydına bakan her tüketici; en çok hosted tool (web search, code interpreter, image generation, hosted MCP) kullanan, sağlayıcı hata içeriği alan ve kendi `WorkflowEvent` türevini yayan kod workflow'u yazan tüketici. İş tüketici kodunda kalamaz: içerik ve olay `internal` kayıt yolunda düşer
> **Karşı görüş:** "Bilinmeyeni kaydetmek gürültü üretir; tanınan her tipe kendi olayı verilmelidir." Cevap: tanınan tipe olay vermek her MAF sürümünde iş ister ve arada kayıp sürer. Bu faz kaybı kapatır; tip başına zengin olay (atıf, hosted tool ücreti) sonraki kalemlerdir (C-12). Gürültü sınırlıdır: yük yalnız üstveri taşır, gövde (bayt, metin) yazılmaz
> **Önkoşul:** Yok
> **Paketler:** `Tracon.Abstractions` (iki sabit), `Tracon.Core` (içerik yolu, yük kuralı), `Tracon.Workflows` (olay yolu)
> **Yeni paket:** Yok · **Migration:** Yok — `RunEventType.Custom` (29) ve `custom_type` sütunu vardır
> **Public API:** büyüyor — iki `const` (`RunEventCustomTypes`); `1.0.0` GA'dan (K-603) önce ucuz. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya 0 satır (2026-10-19)
> **Tüketici yüzeyi:** site: `concepts/runs.md` (olay listesi), `concepts/workflows.md` (tanınmayan olay), `capabilities.md` (kayıt satırı), `http-api/` (üretilir) · sevk edilen: `RunEventCustomTypes` XML dokümanı, `CHANGELOG.md` `Added`
> **Manuel test alanı:** Yok — davranış kodla tekrarlanır (sahte sağlayıcı hosted tool içeriği üretir)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-919 —\|K-991 —\|K-059 —" docs/KARARLAR.md
   ```
   **K-919** (`tracon.output.truncated` ayrılmış tipi; yükü metin taşımaz) · **K-991** (canlı akış ayrılmış bildirimleri kapalı listeyle verir; listeye tip eklemek sözleşme kararıdır) · **K-059** (`secret` veritabanına yazılmaz)
3. Önceki fazdan devir — Yok
4. Alan hafızası:
   [`hafiza/run-olay-sozlesmesi.md`](hafiza/run-olay-sozlesmesi.md) (`RunEventType` sözleşmesi, terminal durum switch'leri) ·
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) (`RunRecordingAgent` zinciri)
5. MEAI içerik tipleri — bu doküman 2026-10-19'da ölçtü (§223.1). Uygulama başlarken `maf-api-kesfi '*Content'` ile yeniden dök

---

## Amaç

Tracon tanımadığı MAF içeriğini ve workflow olayını bugün sessizce düşürür. Hosted web search çağrısı, sağlayıcının `ErrorContent`'i, üretilen görselin `DataContent`'i ve tüketicinin kendi workflow olayı run kaydında görünmez. Faz bitince her biri ayrılmış bir `Custom` olayı olarak kaydedilir; yük yalnız üstveri taşır ve `RecordToolPayloads` kuralına uyar.

- **F-589** — `tracon.content` (agent içeriği) ve `tracon.workflow.event` (workflow olayı) ayrılmış tipleri

Kapsam dışı (matris C-10'da kalır): atıf (`TextContent.Annotations`), mesaj kaynağı, workflow OTel, embedding/ses span'leri, hosted tool ücreti (C-12).

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`RunRecordingAgent.Persistence.cs:215-240`](../src/Tracon.Core/Recording/RunRecordingAgent.Persistence.cs) | `switch` dört tipi tanır (`TextContent`, `TextReasoningContent`, `FunctionCallContent`, `FunctionResultContent`); geri kalanı `default: break` ile düşer |
| aynı yer, `case TextContent … when _options.RecordMessageDeltas` | 🚨 Bayrak kapalıyken `TextContent` de `default`'a düşer. "Tanınmayan" dalı eklenince metin yanlışlıkla tanınmayan sayılır |
| [`WorkflowEventMapper.cs:103`](../src/Tracon.Workflows/Internal/WorkflowEventMapper.cs) · [`WorkflowRunner.cs:1485-1493`](../src/Tracon.Workflows/Internal/WorkflowRunner.cs) | Tanınmayan olay `Unknown` döner; koşucu yalnız `Warning` loglar ve atlar |
| [`RunEventWriter.cs:299-304`](../src/Tracon.Core/Recording/RunEventWriter.cs) | Ayrılmış `Custom` olayın yükü `RecordToolPayloads` kapalıyken de yazılır (`allowReserved && Custom`). Yeni tipler bu muafiyeti almamalı |
| [`RunEventCustomTypes.cs`](../src/Tracon.Abstractions/Runs/RunEventCustomTypes.cs) | Ayrılmış önek `tracon.`; bugün iki tip: `quota.threshold`, `output.truncated` |

> Kanıtlar 2026-10-19 tarihinde doğrulandı.

---

## 223.1 — `tracon.content`

MEAI 10.11'in 22 içerik tipi ölçüldü (decompile, 2026-10-19). Sınıflandırma:

| Tip | Bugün | Bu fazda |
|---|---|---|
| `TextContent` · `TextReasoningContent` | bayrağa bağlı olay | **tanınır** — bayrak kapalıysa hiçbir olay yazılmaz (bugünkü gibi) |
| `FunctionCallContent` · `FunctionResultContent` | tool olayı | değişmez |
| `UsageContent` | kullanım hesabı ayrı yoldan | **tanınır**, olay yok |
| `ToolApprovalRequestContent` · `ToolApprovalResponseContent` | onay yolu ayrı | **tanınır**, olay yok |
| `WebSearchToolCallContent` · `CodeInterpreterToolCallContent` · `ImageGenerationToolCallContent` · `McpServerToolCallContent` ve dört `…ToolResultContent` | düşer | `tracon.content`, yük `{ "type": "hosted_tool_call" \| "hosted_tool_result", "tool": "web_search" \| …, "callId" }` |
| `ErrorContent` | düşer | `tracon.content`, yük `{ "type": "error", "errorCode", "message" }` |
| `DataContent` | düşer | `tracon.content`, yük `{ "type": "data", "mediaType", "length" }` — bayt yazılmaz |
| `UriContent` | düşer | `tracon.content`, yük `{ "type": "uri", "mediaType" }` — URI yazılmaz (Açık Soru 1) |
| `HostedFileContent` · `HostedVectorStoreContent` | düşer | `tracon.content`, yük `{ "type": "hosted_file" \| "hosted_vector_store", "id" }` |
| diğer her `AIContent` (gelecek MEAI/MAF tipleri dahil) | düşer | `tracon.content`, yük `{ "type": "unrecognized", "clrType": "<tam ad>" }` |

- `Text` her zaman kısa bir özettir (`"web_search call"`, `"error: <code>"`); kullanıcı verisi taşımaz.
- Yük `Utf8JsonWriter` ile elle yazılır; `AIContent`'in polimorfik serileştirmesi kullanılmaz (AOT ve bilinmeyen tür).
- Yük **muaf değildir**: `RecordToolPayloads` kapalıyken `null` olur. Yazıcının muafiyet koşulu açık bir listeye çevrilir (`QuotaThreshold`, `OutputTruncated`); yeni tip listeye girmez.
- Akışlı yolda aynı içerik birden çok güncellemede gelirse (ör. akan `ErrorContent`) olay her parça için değil, içerik nesnesi başına yazılır — uygulama başında sahte sağlayıcıyla ölçülür.

## 223.2 — `tracon.workflow.event`

- `WorkflowRunner.cs:1485`'teki dal `Warning` + `continue` yerine olay yazar: `Text` = olayın CLR tip adı; yük `{ "eventType": "<tam ad>", "executorId": …? , "data": <DescribeOutput deseni> }`. `DescribeOutput`'un kuralı korunur: tanınmayan veri için yalnız tip adı yazılır, `ToString()` değil (`WorkflowEventMapper.cs:107-114`).
- `Warning` log'u `Debug`'a iner: olay artık kayıttadır. Yük muaf değildir.
- Canlı workflow akışının bu olayı çerçeve olarak verip vermediği uygulama başında ölçülür; ajan akışının kapalı listesine (K-991) **eklenmez**.

---

## Planlanan Public API

```csharp
// Tracon.Abstractions
public static partial class RunEventCustomTypes
{
    public const string Content = ReservedPrefix + "content";
    public const string WorkflowEvent = ReservedPrefix + "workflow.event";
}
```

Bugün eklemek bedava / sonradan eklemek kırıcı değil (sabit eklemek ekleyicidir); ama ad sonradan değişirse `run_events`'teki kayıtlar eski adla kalır — kademe tetiği budur.

### HTTP `endpoint`'leri

Yeni uç yok. `GET /api/runs/{id}/events` yeni olayları mevcut `Custom` biçimiyle döner.

### Arayüz payı

Yok. Konsol `Custom` olayını bugün genel biçimde gösterir — uygulama başında ölçülür; gösteremiyorsa ayrı kalem.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Runs/RunEventCustomTypes.cs          (iki sabit)
src/Tracon.Core/Recording/RunRecordingAgent.Persistence.cs   (switch: tanınan/tanınmayan ayrımı)
src/Tracon.Core/Recording/ContentEventPayload.cs             (internal; elle JSON)
src/Tracon.Core/Recording/RunEventWriter.cs                  (muafiyet listesi)
src/Tracon.Workflows/Internal/WorkflowRunner.cs              (Unknown dalı)
src/Tracon.Workflows/Internal/WorkflowEventMapper.cs         (taslak üretimi)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Hosted tool / hata / veri içeriği gerçek run'da kayda girmez | Fonksiyonel (HTTP run, sahte sağlayıcı içeriği) | `UnrecognizedContentRecordingTests` |
| `RecordMessageDeltas` kapalıyken metin `tracon.content` olarak yazılır | Birim | `ContentEventClassificationTests` |
| `UsageContent` veya onay içeriği çift kayda girer | Birim | `ContentEventClassificationTests` |
| Yük `RecordToolPayloads` kapalıyken yazılır | Birim (`RunEventWriter`) + Fonksiyonel | `ReservedPayloadPolicyTests` |
| `DataContent` baytı ya da URI sorgusu yüke girer (`secret` sızıntısı) | Birim | `ContentEventPayloadTests` |
| Bilinmeyen gelecek tip yazıcıyı çökertir | Birim (özel `AIContent` türevi) | `ContentEventPayloadTests` |
| Tüketicinin `WorkflowEvent` türevi kayda girmez | Fonksiyonel (kod workflow'u) | `UnknownWorkflowEventRecordingTests` |
| Olay yazımı başarısızken run düşer | Fonksiyonel | `UnrecognizedContentRecordingTests` (store hatası) |
| Mevcut `quota.threshold` / `output.truncated` muafiyeti kaybolur | Birim | `ReservedPayloadPolicyTests` |
| Tüketici `tracon.content` tipinde olay yazabilir | Birim | mevcut ayrılmış önek testi genişler |

Beş soru: iptal — yeni yol yazıcının mevcut iptal kuralını kullanır · eşzamanlılık — akışlı yol tek sıralı yazıcıdan geçer · boş/aşırı girdi — büyük `DataContent` yalnız uzunluk yazar; yük `MaxPayloadLength` ile kesilir · başka kiracı — yazıcı kiracı damgasını bugünkü gibi basar · alt sistem hatası — "Gözlemlenebilirlik işlevselliği bozmaz": yazım hatası loglanır, run sürer.

---

## Manuel Kabul Case'leri

Kabul case'i yok — davranış sahte sağlayıcı ile kodla tekrarlanır. Gerçek sağlayıcının hosted web search içeriğini DoD'deki örnek uygulama run'ı gösterir.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `UriContent` için URI yazılır mı? | A: yazılmaz, yalnız `mediaType` · B: sorgusuz URI (şema + host + yol) | A — imzalı URL'nin yolu da kimlik taşıyabilir (K-059 sınıfı); ihtiyaç ölçülürse B ayrı kalem |
| 2 | `ErrorContent.Message` yazılır mı? | A: yükte, `RecordToolPayloads`'a bağlı · B: hiç | A — sağlayıcı hata metni teşhisin kendisidir; yük kuralı onu zaten kapatır |
| 3 | Hosted tool çağrısı `tracon.content` mı, yeni `RunEventType` mı? | A: `tracon.content` · B: `HostedToolCall` enum üyesi | A — enum üyesi her istemcinin switch'ini büyütür (`run-olay-sozlesmesi.md`); ücret ve zengin olay C-12'nin kararıdır |

---

## Bitiş Ölçütleri (DoD)

- [x] Sahte sağlayıcı web search çağrısı, `ErrorContent` ve `DataContent` ürettiğinde `GET /api/runs/{id}/events` üç `tracon.content` olayı döner; `RecordToolPayloads` kapalıyken yükleri `null`'dır (`UnrecognizedContentRecordingTests`, tamponlu ve akışlı yol)
- [x] `RecordMessageDeltas` kapalı run'da `tracon.content` olayı yoktur (`ContentEventClassificationTests`, `UnrecognizedContentRecordingTests`)
- [x] Kod workflow'unun kendi olayı `tracon.workflow.event` olarak kayıttadır; `Warning` log'u kalmaz (`UnknownWorkflowEventRecordingTests`, `UnknownWorkflowEventHttpTests`)
- [x] `quota.threshold` ve `output.truncated` yükü `RecordToolPayloads` kapalıyken yazılmaya devam eder (`ReservedPayloadPolicyTests`; `output.truncated` ayrıca HTTP'de)
- [x] Dört doğrulama kapısı sıfır uyarı verir (`kapi.py kapanis --taban 5e8941d3`: çıkış 0; tam test 576 sn)
- `samples/Tracon.Api` ile **gerçek sağlayıcıda** hosted web search koşumu yapılmadı: gerçek bir OpenAI anahtarı (parayı ve veriyi dışarı götürür) ve web search açık bir harness agent'ı ister; örnek uygulamanın tek harness agent'ı `DisableWebSearch = true`'dur ve `faz-tamamlama` Adım 2'nin yalıtım komutu anahtarı boşaltır. Yerine örnek uygulama yalıtılmış (boş bağlantı dizeleri, geçici SQLite, `Npgsql` log satırı 0) ayağa kaldırıldı; `researcher` (echo) run'ı `run.started` → 7 × `message.delta` → `run.completed` ile `Completed` bitti. Hosted içerik üretmediği için yeni olay orada görünmez; yeni olayın kanıtı gerçek DI host'lu fonksiyonel testlerdedir
- [x] `secret` taraması boş döndü (`kapi.py tarama`, `kapanis` içinde)
- [x] Kabul case'i yok; davranış otomatik testte
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (tek 🔴 kullanıcı kararıyla gürültü, bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` (içerik · derleme · bağlantı · ağırlık) temiz

### Doğrulama komutları

```bash
curl -s http://localhost:5081/tracon/api/runs/<runId>/events | jq '.[] | select(.customType | startswith("tracon."))'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Akışlı yolda aynı içerik çok kez yazılır | Uygulama başında ölçüm; içerik başına tekilleştirme |
| Yük kuralı değişimi mevcut iki tipi bozar | Muafiyet listesi testi |
| Konsol `Custom` olayını okunaksız gösterir | Uygulama başında ölçüm; gerekiyorsa ayrı kalem |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Plan ile gerçek arasındaki fark **gizlenmez**. Kapanışta
> kapanış commit mesajına taşınır.

1. **`AlwaysApproveToolApprovalResponseContent` tanınır, olay yok.** Plan yalnız MEAI'nin iki onay tipini adlandırdı. MAF'ın bu tipi `AIContent`'ten türer (`ToolApprovalResponseContent`'ten değil), tanınmasaydı her "her zaman onayla" yanıtı `unrecognized` yazılırdı. MEAI 10.11'in `InputRequestContent`/`InputResponseContent` taban sınıfları da var; **bilerek** tanınmadı — gelecekteki başka bir alt sınıf `unrecognized` olarak görünsün.
2. **Serbest metin yük içinde kesilir, yükü yazıcının sınırına sığdırır** (`ContentEventPayload.BuildFitting`): `ErrorContent.Message` ve workflow olayının metin verisi, en çok 2048 karakter ve tüm yük `MaxPayloadLength`'e sığana dek (kaçışlı JSON uzunluğu, ASCII dışı karakter 6 karakter tutar); kesim surrogate çiftini bölmez. Plan yalnız `MaxPayloadLength` kesimini söylüyordu; yazıcı yükü şekline bakmadan keser ve JSON'u bozar. Her dize `Sanitize` ile geçer (yalnız duran surrogate → U+FFFD; ölçüldü: `Utf8JsonWriter` fırlatmaz, sessizce U+FFFD yazar — denetçinin "run düşer" repro'su çıkmadı, ama kesimde bölünen emoji bozuluyordu ve düzeltildi).
3. **Ayrılmış `Custom` yük muafiyeti açık listeye döndü ve `RunEventType` önsözü düzeldi.** Plan listeyi yazıcıda söylüyordu; "`ReservedPrefix` altındaki her `Custom` muaftır" cümlesi hem tip önsözünde hem `Custom` üyesinde yazılıydı ve artık yanlıştı. İkisi `QuotaThreshold` ve `OutputTruncated`'ı adlandırır.
4. **`WorkflowEventMapping.Unknown` kaldırıldı.** `Map` bilinmeyen olay için `IsKnown: false` ile **taslağı da** döndürür; koşucu `IsKnown` ile `AppendReservedAsync` / `AppendAsync` arasında seçer. `Warning` log'u `Debug`'a indi (CA1873 için `IsEnabled` kapısıyla).
5. **Kod workflow'u testi iki seviyede**: gerçek koşucu + gerçek function node (`Workflows.UnitTests`) ve gerçek DI host + HTTP (`UnknownWorkflowEventHttpTests`). Plandaki "kod workflow'u" bir `WorkflowBuilder` grafiği idi; function node aynı `IWorkflowContext.AddEventAsync` yolunu koşar.
6. **`WorkflowTestHost` iki ayar kazandı** (`TraconOptions`, `RunnerLogger`): `RecordToolPayloads` kapalı ve log seviyesi testleri için.
7. **K-991 listesine tip eklenmedi** (plan böyle dedi). Canlı akış testi bunu `The_live_stream_does_not_carry_the_content_events` ile sabitler.
8. **Plan "uygulama başında ölçülür" dediği üç şeyin sonucu:** (i) akışlı yolda tekrar eden içerik — **tekilleştirme yok**: `DataContent`/`ErrorContent`/hosted tool içeriğinin kararlı kimliği yoktur, her güncellemedeki her içerik nesnesi bir olaydır; gerçek bir sağlayıcıda parça parça gelen görüntü (image generation) birden çok olay üretebilir, **ölçülmedi** (gerçek sağlayıcı koşumu yapılmadı). (ii) Canlı workflow akışı olayı `event` çerçevesi olarak **verir** (`UnknownWorkflowEventHttpTests`); ajan akışı vermez. (iii) Konsol `Custom` olayını genel kartla gösterir (`run-detail.tsx:666`, etiket `customType`); kod okundu, görsel olarak ölçülmedi.
9. **`executorId` yalnız `ExecutorEvent` türevlerinde yazılır.** Bir function node'un `AddEventAsync` ile verdiği düz `WorkflowEvent` türevinde anahtar yoktur; site ve XML bunu söyler.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez.

- Açık sorular (kullanıcı kararı, 2026-10-20, üçü de planın önerisi): `UriContent` adresi yazılmaz (1-A); `ErrorContent.Message` yükte yazılır, `RecordToolPayloads`'a bağlı (2-A); hosted tool çağrısı `tracon.content` olur, yeni enum üyesi açılmaz (3-A). → `K-1034`.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 9 (Plandan Sapmalar) |
| Düzeltme turu sayısı | 1 (denetim sonrası) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 1 / 0 |
| Fazın ürettiği regresyon | 0 |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı. Her satır: bulgu · seviye (🔴/🟡/🟢) · sonuç
> (düzeltildi / gerekçelendi / F-NN olarak devredildi). 🟢 kapanışta
> `ADAYLAR.md`'ye gider.

Denetçi: `faz-denetcisi` (taze bağlam, salt-okunur), 2026-10-20.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Workflow yolunda tanınmayan olayın tarifi `try/catch` dışında; 2048'de kesilen emoji `Utf8JsonWriter`'ı fırlatıp run'ı düşürür | 🔴 (öneri: araştırılacak) | **Gürültü** (kullanıcı kararı, 2026-10-20). Repro koşuldu: `Utf8JsonWriter` fırlatmaz, U+FFFD yazar; run düşmez. Gerçek kusur başkaydı — kesim emojiyi bölüp bozuyordu: kesim artık çifti bölmez, her dize `Sanitize`'dan geçer; testler mutasyonla kırmızıya döner. Kullanıcı verisinin kendi getter'ının fırlatması `DescribeOutput` ile aynı sınıf, bu fazın dışında |
| 2 | Sabit 2048 cap yazıcının `MaxPayloadLength` sınırını bilmiyor; ASCII dışı metin kaçışta 6 katına çıkar, JSON kesilip bozulur | 🟡 | Düzeltildi: `BuildFitting` yükü sınıra sığdırır (iki yolda `maxPayloadLength` geçer); testler 1024/8192 sınırı ve Çince/Kiril/`"` metniyle |
| 3a | `executorId` dokümanı davranışla örtüşmüyor (yalnız `ExecutorEvent` türevinde) | 🟡 | Düzeltildi: XML, site ve sapma 9 "`ExecutorEvent` türevinde" der |
| 3b | "Metadata only" iddiası `message`/`data` metniyle çelişiyor | 🟡 | Düzeltildi: XML, site, CHANGELOG ve K-1034 başlığı |
| 3c | Canlı akış cümlesi `tracon.workflow.event` için yanıltıcı | 🟡 | Düzeltildi: workflow akışı olayı `event` çerçevesiyle verir; `The_workflow_run_stream_sends_the_event_as_an_event_frame` |
| 4 | "Uygulama başında ölçülür" üç ölçümün sonucu Plandan Sapmalar'da yok | 🟡 | Düzeltildi: sapma 8 (tekilleştirme yok · ölçülmedi, canlı akış verir, konsol genel kart · görsel ölçülmedi) |
| 5 | `Text` sağlayıcı denetimli `ErrorCode`/`MediaType` taşır | 🟢 | F-592 |
| 6 | K-640 cırcırı `ErrorContent.Message` yolunu görmez; muafiyet listesi ↔ önsöz kapısı tip adı saymaz | 🟢 | F-593 |
| 7 | `docs/MAF-KAPSAMA.md` hâlâ `default: break` / `_ => Unknown` yazıyor | 🟢 | Düzeltildi (Adım 7): üç satır ve G-04/C-10 güncellendi |

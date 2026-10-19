# Faz 223 — Tanınmayan MAF İçeriği ve Workflow Olayının Kaydı

> **Durum:** 📋 Planlandı (2026-10-19)
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

- [ ] Sahte sağlayıcı web search çağrısı, `ErrorContent` ve `DataContent` ürettiğinde `GET /api/runs/{id}/events` üç `tracon.content` olayı döner; `RecordToolPayloads` kapalıyken yükleri `null`'dır
- [ ] `RecordMessageDeltas` kapalı run'da `tracon.content` olayı yoktur
- [ ] Kod workflow'unun kendi olayı `tracon.workflow.event` olarak kayıttadır; `Warning` log'u kalmaz
- [ ] `quota.threshold` ve `output.truncated` yükü `RecordToolPayloads` kapalıyken yazılmaya devam eder
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı (hosted web search açık harness agent'ı), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Kabul case'i yok; davranış otomatik testte
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

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

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı. Her satır: bulgu · seviye (🔴/🟡/🟢) · sonuç
> (düzeltildi / gerekçelendi / F-NN olarak devredildi). 🟢 kapanışta
> `ADAYLAR.md`'ye gider.

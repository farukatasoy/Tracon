# Keşif Turu — 2026-08-18 · Tüketici raporu kaynaklı kalemler

> Bu bir **koşum kaydıdır**, spec değildir. Sıcak yolda değildir ve baştan sona
> okunmaz. Onaylanan kalemlerin tam metni [`ADAYLAR.md`](../ADAYLAR.md)
> içinde yaşar; bu dosya yalnız oraya işaret eder.

**Tetikleyen:** Harici bir proje (ProdigyEnabler, ABP 10.5 / .NET 10) `docs-site/`
sitesinin 91 sayfasını tarayıp bir uygulanabilirlik raporu üretti. Kullanıcı
raporun bulgularının ölçülmesini ve aday kalemlere dönüştürülmesini istedi.

**Zemin:** Faz 61 kapalı · Faz 62–66 planlandı · Faz 7 (yayın) ⏸ · en büyük
numara F-109

**Ekosistem taraması:** 2026-08-18 · web erişimi **var**

**Kaynak rapor:** `agentprism-uygulanabilirlik-analizi-2026-08-17.md` — bu
repo'da değildir, tüketici projesinin `claudedocs/` dizinindedir.

---

## 1. Ölçülen zemin (Aşama 0)

| Kaynak | Bulgu |
|---|---|
| [`YOL-HARITASI.md`](../YOL-HARITASI.md) | 67 kalem · Faz 62–66 📋 planlandı · **Faz 7 ⏸ — hiçbir paket yayınlanmadı** |
| Faz 61 devir notu | Yarım kalan: F-109 (replay). Yeni kalem bırakmamış |
| Ekosistem boşluk tablosu | On üç satırın on ikisi plana girdi; kalan iki satır F-34, F-72 |
| [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](../arsiv/KARARLAR-INDEKS-REDDEDILEN.md) | 24 kalem tarandı — **on kalemin hiçbiri listede yok** |
| [`ADAYLAR.md`](../ADAYLAR.md) grep | `pgvector` · `cache` · `reasoning` · `timeout` · `purpose` · `SignalR` · `fatura` · `yerelleştir` → **eşleşme yok**. Onu da yeni kalemdir |
| `git ls-files` | Turun başında çalışma alanı temiz |

🚨 **Turun en önemli zemin bulgusu:** Faz 7 yayınlanmadı, her paketin
`PublicAPI.Shipped.txt` dosyası boştur. Public yüzeyi büyüten kalem **bugün
bedavadır**. On kalemin altısı public sözleşmeye dokunur.

---

## 2. Ham fikir listesi (Aşama 2)

Bu turda ham fikir üretilmedi. Kaynak rapor on adet aday üretmişti (§8.1–8.10)
ve beş açık soru sormuştu (§11.1–11.5); tur bunları **elemeye değil ölçmeye**
harcandı. Eleme ölçümün sonucudur.

| # | Rapor kalemi | Ölçüm sonucu | Sonuç |
|---|---|---|---|
| 1 | §8.1 Kiracı sağlayıcı anahtarı (BYOK) | **Zaten planda** — [Faz 65](../65-KIRACI-SAGLAYICI-ANAHTARLARI.md) 📋 | ❌ elendi — kalem yok |
| 2 | §8.2 Tool düzeyinde yetkilendirme | Yok — ölçüldü | ✅ F-113 |
| 3 | §8.3 AI olmayan workflow düğümü | Yok — ölçüldü | ✅ F-116 |
| 4 | §8.4 Faturalandırma çıktısı | Yok, ama **kütüphane sınırının dışı** | ❌ elendi → *Bilerek Önerilmeyenler* |
| 5 | §8.5 Kademeli bütçe politikası | **İddia yanlış** — eşik uyarısı kodda (F-100, 2026-08-18 kapandı). Eksik olan eşik başına *aksiyon* | ⏸ ertelendi — bkz. §6 |
| 6 | §8.6 Olay hedefi / SignalR köprüsü | Yok — ölçüldü | ✅ F-115 |
| 7 | §8.7 Talimatta çok dillilik | Yok — ölçüldü | ✅ F-117 |
| 8 | §8.8 Harici hosted agent yönetimi | Satıcı entegrasyonu | ❌ elendi → *Bilerek Önerilmeyenler* |
| 9 | §8.9 Kiracı bazlı sağlayıcı allowlist'i | Yok — ölçüldü | ✅ F-119 |
| 10 | §8.10 Tool yürütme timeout'u | Yok — ölçüldü | ✅ F-114 |
| 11 | §11.1 `pgvector` koşulsuz zorunlu | **Doğrulandı** | ✅ F-110 |
| 12 | §11.2 Cache token muhasebesi | **Doğrulandı ve büyüdü** | ✅ F-112 |
| 13 | §11.3 Reasoning delta | Yok — ölçüldü | ✅ F-115 içinde |
| 14 | §11.5 ElevenLabs `/with-timestamps` | Yok — ölçüldü | ✅ F-118 |
| 15 | *(turda eklendi)* Çalıştırma kimliği — `UserId` ve etiket yok | Yok — ölçüldü | ✅ F-111 |

**Kullanıcının elemesi:** Ölçüm sunuldu, kullanıcı on kalemin tamamını onayladı
("ADAYLAR.md'ye işleyelim bu on kalemi").

🚨 **15 numaralı kalem raporda yoktur.** Rapor kendi panel tasarımında
`kiracı × kullanıcı × agent × purpose × model` kırılımı istiyordu (M4.2) ama
bunu bir AgentPrism boşluğu olarak yazmamıştı. Ölçüm sırasında çıktı.

---

## 3. Ekosistem taraması (Aşama 3.2)

| Kaynak | Bakılan tarih | Ne bulundu | AgentPrism'e etkisi |
|---|---|---|---|
| `Microsoft.Extensions.AI.Abstractions` 10.8.3 (repo'nun sabitlediği sürüm) | 2026-08-18 | `UsageDetails` on üye taşıyor: `CachedInputTokenCount`, `ReasoningTokenCount`, `InputAudioTokenCount`, `InputTextTokenCount`, `OutputAudioTokenCount`, `OutputTextTokenCount`, `AdditionalCounts` | 🚨 F-112'nin hazırlığı **yüksek** — veri tipli olarak zaten geliyor, biz atıyoruz |
| [LiteLLM Tool Permission Guardrail](https://docs.litellm.ai/docs/proxy/guardrails/tool_permission) · [LiteLLM MCP Permission Management](https://docs.litellm.ai/docs/mcp_control) | 2026-08-18 | Tool başına izin/ret kuralı, sağlayıcıdan bağımsız; MCP tool'ları anahtar/takım/organizasyon bazında kısıtlanıyor | F-113 için "X'te standart, .NET'te yok" |
| [Portkey MCP Gateway](https://portkey.ai/features/mcp) | 2026-08-18 | Takım seviyesinde tool izni, kimlik bilgisi gateway'den çıkmıyor | F-113 |
| [Langfuse token & cost tracking](https://langfuse.com/docs/observability/features/token-and-cost-tracking) · [trace best practices](https://langfuse.com/docs/observability/best-practices) | 2026-08-18 | Her trace'te `user_id` + `session_id` + değişmez `tags`; kırılım bu boyutlarda | F-111 |
| [Braintrust cost attribution](https://www.braintrust.dev/articles/how-to-track-llm-costs-2026) | 2026-08-18 | Harcamayı **kullanıcı, özellik, model** bazında kıran özel etiketler | F-111 |

**Doğrulanmayanlar:** F-110, F-114, F-116, F-117, F-118 için ekosistem taraması
yapılmadı — beşi de kendi kodumuzun ölçümüne dayanır, "başkası yapmış mı"
sorusu kalemin gerekçesini değiştirmez.

---

## 4. Derinleşen kalemler (Aşama 3)

Onunun da tam metni [`ADAYLAR.md`](../ADAYLAR.md) içindedir. Burada yalnız
kanıt satırı ve eleyici sınır kontrolü durur.

| F-NN | Kanıt seviyesi ve yeri | Eleyici sınır | Public yüzey |
|---|---|---|---|
| F-110 | **Ölçüldü** — `Migrations/0024_vector.sql:17` + `MigrationDescriptor.Discover` gömülü tüm `.sql`'i sırayla koşar | Temiz | Migration seti — 🚨 yayından sonra **imkânsız** |
| F-111 | **Ölçüldü** — `RunRecord.cs` yalnız `TenantId`/`SessionId`/`AgentName`; `grep -rn "UserId" src` → **0 sonuç** | Temiz | `RunRecord` · `RunStartInfo` · `RunStatistics*` — en pahalı |
| F-112 | **Ölçüldü** — `RunRecordingAgent.cs:1056` üç sayacı alır, gerisini atar; `RunSupportTypes.cs:6` üç alan | Temiz | `RunUsage` · `RunCost` |
| F-113 | **Ölçüldü** — `ToolDescriptor.cs` izin/etki alanı yok; `IToolAuthoriz*` → 0 sonuç | K2 **zorlamıyor** — izin kontrolü tool *tanımlamaz*, çalıştırmayı kısıtlar | `ToolDescriptor` · attribute |
| F-114 | **Ölçüldü** — timeout yalnız `AgentPrismOptions.cs:71` (MCP) ve `:210` (skill script) | Temiz | `ToolDescriptor` — F-113 ile aynı record |
| F-115 | **Ölçüldü** — `RunEventWriter.cs:22` yalnız `IRunStore`'a yazar; `RunEventType.cs` 0–21, `ReasoningDelta` yok | K3 temiz — MAF tipi sarmalanmıyor | Yeni arayüz + enum'a **ekleme** (konvansiyon izin veriyor) |
| F-116 | **Ölçüldü** — `WorkflowGraph.cs:94` `WorkflowNodeKind`: `Agent`/`Orchestration`/`RequestPort`/`Output` | 🚨 **K2 sınırında** — bkz. aşağı | `WorkflowDefinition` · enum ekleme |
| F-117 | **Ölçüldü** — `AgentDefinition.cs:33` tek `string?` | Temiz | `AgentDefinition` |
| F-118 | **Ölçüldü** — `ElevenLabsSpeechClient.cs:318` yalnız `text-to-speech` ve `/stream` | Temiz | `AgentPrism.Voice` sözleşmesi |
| F-119 | **Ölçüldü** — allowlist yalnız webhook URL'i ve skill script'te; sağlayıcı için yok | Temiz | Yapılandırma + doğrulama |

### 🚨 F-116'nın K2 sınırı — planlamadan önce karara bağlanmalı

K2 der ki: **tool yalnızca kodda tanımlanır**. Bir "kod fonksiyonu" workflow
düğümü, fonksiyonun **kendisi kodda kayıtlıysa** K2'yi ihlal etmez — grafik
yalnız kayıtlı bir düğüme *işaret eder*, tıpkı `AgentDefinition.ToolNames`'in
kayıtlı bir tool'a işaret etmesi gibi.

İhlal, düğümün gövdesi arayüzden veya veritabanından geldiğinde başlar.
`faz-planlama` bu sınırı ilk adımda yazmalıdır. Aksi hâlde faz sessizce K2'yi
deler.

---

## 5. Üç kanalın çıktısı (Aşama 1)

### Kanal 1 — yeni aday

🚨 **Onu da aynı gün plana dönüştü** (kullanıcı kararı). Gövdeleri
[`../arsiv/PLANA-DONUSEN-ADAYLAR.md`](../arsiv/PLANA-DONUSEN-ADAYLAR.md)'e
taşındı; [`../ADAYLAR.md`](../ADAYLAR.md)'de tek satırlık iz kaldı.

| F-NN | Başlık | Faz |
|---|---|---|
| F-110 | `pgvector`'ün isteğe bağlı olması | [Faz 67](../67-ISTEGE-BAGLI-MIGRATION-SETI.md) |
| F-111 | Çalıştırma kimliği ve maliyet kırılım boyutları | [Faz 68](../68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) |
| F-112 | Cache ve reasoning token kırılımı | [Faz 68](../68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) |
| F-113 | Tool düzeyinde yetkilendirme ve etki sınıfı | [Faz 69](../69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) |
| F-114 | Tool yürütme timeout'u | [Faz 69](../69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) |
| F-115 | Çalıştırma olayı hedefi ve `ReasoningDelta` | [Faz 70](../70-CALISTIRMA-OLAYI-HEDEFI.md) |
| F-116 | Workflow kod düğümü | [Faz 71](../71-WORKFLOW-KOD-DUGUMU.md) |
| F-117 | Talimatta çok dillilik | [Faz 72](../72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) |
| F-118 | Zaman damgalı konuşma sentezi | [Faz 72](../72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) |
| F-119 | Kiracı bazlı sağlayıcı allowlist'i | [Faz 65](../65-KIRACI-SAGLAYICI-ANAHTARLARI.md)'e katıldı |

**Gruplama gerekçesi (kullanıcı kararı):** F-113+F-114 aynı `ToolDescriptor`
kaydına, F-111+F-112 aynı `runs` tablosuna dokunur — ayrı planlamak aynı yere
iki kırıcı değişiklik gönderirdi. F-117+F-118 ise **zayıf oldukları için**
birleşti; ortak altyapıları yoktur ve bu, `faz-planlama` Adım 0'ın bilinçli bir
istisnasıdır (Faz 72 dokümanı bunu yazıyor).

### Kanal 2 — kusur

| Bulgu | Kanıt | Kullanıcıya söylendi mi | `kusur-giderme` |
|---|---|---|---|
| `docs-site/src/content/docs/http-api/` altında 247 senkronizasyon kopyası (`<ad> 3.md`) | `ls \| grep -c " [0-9]\.md"` → 247 | ✅ | **Gerekmiyor.** Dizin `.gitignore:100` ile hariç ve **üretilen** bir dizindir; `git ls-files` → 0 izlenen kopya. `npm run generate` temizler |

Bu turda gerçek bir kusur bulunmadı.

### Kanal 3 — yeniden açılması önerilen karar

| K-NNN | Kararın gerekçesi | Neyin değiştiği | Karar |
|---|---|---|---|
| **K-232** — sunucu yanıtları çevrilmez | Paket uluslararası yayınlanır; aynı hata metni günlükte, testte ve destek kaydında aynı olmalı | 🚨 **Hiçbir şey.** Rapor bunu bir boşluk sandı (§4.9); kararın "yeniden açılma koşulu" (bir tüketicinin `Accept-Language` talebi) **gerçekleşmedi** — rapor talep etmiyor, gözlem olarak yazıyor | Yeniden açılmadı |

---

## 6. Reddedilenler

| Fikir | Ret gerekçesi | Kalıcı mı | Nereye |
|---|---|---|---|
| §8.4 Faturalandırma çıktısı (dönem, kur, fatura satırı, dönem kapatma) | Maliyet **hesaplanıyor** ve `RunCost` para birimi taşıyor. Dönem, kur dönüşümü, mark-up ve fatura satırı bir **iş katmanıdır**; muhasebe sistemine göre değişir ve kütüphane sınırının dışındadır. F-111 kırılım boyutlarını verince toplama katmanı tüketicide ucuzlar | ✅ Kalıcı | *Bilerek Önerilmeyenler* |
| §8.8 Harici hosted agent yönetimi (ElevenLabs ConvAI agent'ı) | Tek bir satıcının kontrol panelini sarmalamak. MCP client uzak tool'u, A2A uzak agent'ı zaten konuşuyor. Satıcı başına yüzey bakım borcudur | ✅ Kalıcı | *Bilerek Önerilmeyenler* |
| §8.5 Eşik başına aksiyon (kıs / agent'ı devre dışı bırak) | Uyarı **zaten var** (F-100 ölçümle kapandı). Aksiyon kısmı gerçek bir boşluk ama **kanıtı yok**: bugün 429 dönen bir kotanın yetmediğini gösteren bir koşum yok. Kanıtsız kalem aday dosyasına girmez | ❌ Zamanlama | Yalnız bu notta |

---

## 6.5 — Planlama sırasında düzelen kanıt

| Kalem | Aday listesindeki ifade | Ölçülen gerçek |
|---|---|---|
| F-115 | "düşünme akışı `MessageDelta`'ya karışır" | 🚨 **Daha kötü.** `RunRecordingAgent.WriteContentsAsync` `switch`'i üç tipi tanır; `TextReasoningContent` `default: break` dalına düşer ve **hiç kaydedilmez** |
| F-110 | `0024_vector.sql:17` | Satır **16** — `grep -n` ile düzeltildi |
| F-116 | "MAF executor kavramına sahip, ölçülmeli" | ✅ Ölçüldü: `Microsoft.Agents.AI.Workflows` 1.16.0 `FunctionExecutor<TInput,TOutput>` taşıyor; kurucu imzası Faz 71'e yazıldı |
| F-119 | "allowlist yalnız webhook ve skill script'te" | Kesinleşti: `grep` **üç** sonuç verir, üçü de `EnvironmentAllowList` (skill script). Webhook ayrı bir mekanizma (`WebhookUrlValidator`) |

---

## 7. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap |
|---|---|
| On kalem aday dosyasına mı işlensin, yoksa biri doğrudan faza mı dönüşsün? | Aday dosyasına işlensin |
| On kalem kaç faza bölünsün? | **6 faz — gruplu** (F-113+F-114, F-111+F-112, F-117+F-118 birleşti; F-119 Faz 65'e katıldı) |
| F-117 ve F-118 zayıf kalemler — ne yapalım? | **Tek fazda birleştir** |
| F-119 Faz 65'e katılsın mı? | **Evet** |

**Üçü de soruldu ve cevaplandı** (yukarıdaki tablo). Faz sırası için açık
kalan tek soru, faz dokümanlarının kendi *Açık Sorular* bölümlerine yazıldı:

- **Faz 69 mu Faz 63 mü önce?** İkisi de tool çağrısının önüne kapı koyar ama
  **onay ile izin farklı şeylerdir**. Faz 69'un Açık Soru 1'i 69'u önce
  öneriyor — o zaman Faz 63 etki sınıfını kapsam anahtarı olarak kullanabilir.
- **Faz 67 Faz 7'den önce olmalı.** Tek "sonradan imkânsız" kalem odur.

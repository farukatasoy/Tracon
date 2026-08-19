# Faz 72 — Çok Dilli Talimat ve Zaman Damgalı Sentez

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-117**, **F-118**
> **Önkoşul:** [Faz 19](19-SURUM-KARSILASTIRMA-VE-AB.md) — agent sürümleme ve diff · [Faz 28](28-SES-TOOLLARI.md) — ses tool'ları ve ElevenLabs istemcisi
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Voice`, `AgentPrism.Sql.Shared`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** **YOK** — plan yanlıştı, bkz. Plandan Sapmalar ve K-499
> **Public API:** **büyüdü** — `AgentDefinition.InstructionsByCulture`, `AgentDefinitionRequest.InstructionsByCulture`, `AgentRunRequest.Culture`, `IAgentSource`/`IAgentCatalog`/`IVersionedAgentSource` imzalarına `culture`, `CompiledAgentCache`'e culture'lı aşırı yükler, `InstructionCultureResolver` (yeni tip), `SpeechRequest.IncludeTimestamps`, `SpeechAudio.Alignment`, `SpeechAlignment` (yeni tip), `SpeakRequest.IncludeTimestamps`, `SpeakResponse.Alignment`. `PublicAPI.Shipped.txt` hâlâ boş — bedavaydı.
> **Site etkisi:** `concepts/agents.md`, `guides/voice.md` güncellendi. `reference/configuration.md`'ye dokunulmadı — gerekçe: bu faz `AgentPrismOptions`/`VoiceOptions`'a yeni bir yapılandırma anahtarı eklemedi (`culture`/`includeTimestamps` istek başına alan, config değil)
> **Manuel test alanı:** [`docs/manuel-test/19-COK-MODLULUK-VE-SES.md`](manuel-test/19-COK-MODLULUK-VE-SES.md) (MT-MM-091..094) · [`docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md`](manuel-test/02-CEKIRDEK-VE-KATALOG.md) (MT-CORE-075..081)

---

## ⚠️ Bu faz iki zayıf kalemi taşır

Aday listesinde ikisinin de **karşı görüş** satırı "tek başına faz olmamalı"
diyordu: F-117 yalnız 1 ve 5 numaralı merceklerden, F-118 yalnız 1 numaralı
mercekten iyi görünüyor. Kullanıcı kararıyla tek fazda birleştirildiler
(2026-08-18).

🚨 **Ortak yanları zayıftır** — ikisi de tüketiciye dönük küçük ergonomi
işidir, ortak bir altyapı paylaşmazlar. Bu, `faz-planlama` Adım 0'ın "iki kalem
ancak aynı altyapıyı paylaşıyorsa birleşir" kuralının bilinçli bir istisnasıdır
ve bedeli **bulanık DoD** riskidir. Karşı önlem: bitiş ölçütleri iki ayrı blok
hâlinde yazıldı ve biri diğerini bekleyemez. İki kalem bağımsız olarak
uygulanabilir ve bağımsız olarak iptal edilebilir.

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-228\|K-232\|K-178\|K-032" docs/KARARLAR.md
   ```
   **K-228** (arayüz sözlüğü iki dilli; eksik anahtar derleme hatası),
   **K-232** (🚨 **sunucu yanıtları çevrilmez** — bu faz onu ihlal etmiyor,
   §72.1'e bak), **K-178** (migration numaraları), **K-032** (yerleşik liste yok).
3. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/ses-ve-konusma.md`](hafiza/ses-ve-konusma.md) (ses tool'ları) ·
   [`hafiza/frontend.md`](hafiza/frontend.md) (sözlük, bundle)

---

## Amaç

İki bağımsız ergonomi boşluğu:

- **F-117** — agent talimatı tek dillidir. İki dilde çalışan bir üründe iki ayrı
  agent tanımı gerekir; sürüm geçmişleri, eval kümeleri ve deneyleri ayrışır.
- **F-118** — konuşma sentezi kelime düzeyinde zaman damgası döndüremez.
  Altyazı, vurgulama ve transcript senkronu yapılamaz.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentDefinition.cs:33`](../src/AgentPrism.Abstractions/Agents/AgentDefinition.cs) | `public string? Instructions` — tek metin, kültür kavramı yok |
| [`ElevenLabsSpeechClient.cs:318-319`](../src/AgentPrism.Voice/Internal/ElevenLabsSpeechClient.cs) | Yalnız `v1/text-to-speech/{voiceId}` ve `/stream` çağrılıyor |
| [`SpeechContracts.cs:111-142`](../src/AgentPrism.Abstractions/Voice/SpeechContracts.cs) | `SpeakRequest` üç alan; `SpeakResponse` ek, karakter, maliyet — **hizalama verisi yok** |

> Kanıtlar 2026-08-18 tarihinde doğrulandı.

---

## 72.1 — F-117: kültür anahtarlı talimat

### 🚨 K-232 ihlal edilmiyor

K-232 **sunucu yanıtlarının** çevrilmediğini söyler: `ProblemDetails`, doğrulama
mesajı, sağlayıcı hatası İngilizce kalır. Bu faz onları değiştirmez.

Agent talimatı bir sunucu yanıtı **değildir** — modele giden **içeriktir** ve
tüketicinin yazdığı veridir. `locales/tr.ts`'in meşru bir sözlük olması (K-228)
ile aynı ayrımdır. Plan bu farkı yazar ki sonraki oturum karışıklığa düşmesin.

### Tasarım

`AgentDefinition` bir kültür sözlüğü alır. Tek metin alanı **korunur** ve
varsayılan olur:

```
Instructions            → varsayılan metin (bugünkü davranış)
InstructionsByCulture   → kültür anahtarı → metin
```

Çözümleme sırası: istenen kültür → kültürün ana dili (`tr-TR` → `tr`) →
`Instructions`. Eşleşme yoksa varsayılan kullanılır ve **hata verilmez**.

Kültür nereden gelir: `AgentRunRequest` üzerinde açık bir alan. HTTP
`Accept-Language` başlığı **kullanılmaz** — K-232'nin çizgisiyle tutarlıdır ve
bir tarayıcı başlığının modele giden içeriği sessizce değiştirmesi sürpriz olur
(K1).

### 🚨 Sürümleme ve eval sorusu — bu fazın asıl kararı

[Faz 19](19-SURUM-KARSILASTIRMA-VE-AB.md) sürümü ve diff'i **tek** bir talimat
metni üzerinden kurdu. [Faz 18](18-DEGERLENDIRME.md) eval'i de öyle. Kültür
eklenince şu soru doğar: **kültür sürümün içinde mi dışında mı?**

| Seçenek | Sonuç |
|---|---|
| **A — kültür sürümün İÇİNDE** | Bir sürüm tüm dilleri taşır. Bir dili düzeltmek yeni sürüm açar ve **tüm dilleri** etkiler. Diff çok dilli olur. Eval seti dil seçer |
| **B — dil başına ayrı sürüm hattı** | Diller bağımsız ilerler. Ama "agent'ın aktif sürümü" tek bir şey olmaktan çıkar; deney (`experiment`) ve kanarya ağırlıkları dil başına ayrışır |

**A önerilir.** B, [Faz 19](19-SURUM-KARSILASTIRMA-VE-AB.md) ve
[Faz 56](56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md)'nın kurduğu "agent'ın tek
aktif sürümü vardır" sözleşmesini kırar ve o kırılma bu fazın kapsamından
büyüktür. A'nın bedeli kabul edilebilir: bir dili düzeltmek bir sürüm açar,
zaten olması gereken budur.

---

## 72.2 — F-118: zaman damgalı sentez

ElevenLabs zaman damgası döndüren bir uç sunar. `SpeakRequest` isteğe bağlı bir
bayrak alır; `SpeakResponse` hizalama verisi taşır.

🚨 **Uç adı ve yanıt şeması bu planda YAZILMADI.** Bugünkü kod yalnız
`v1/text-to-speech/{voiceId}` ve `/stream` çağırıyor; zaman damgalı ucun tam
yolu ve yanıt gövdesi **doğrulanmadı**. Uygulayan oturum ilk iş olarak
sağlayıcının güncel API dokümanını okur ve şemayı plana **ölçerek** yazar.
Tahmin edilmiş bir şema sessizce yanlış koda dönüşür.

**Üç kural:**

1. **Varsayılan kapalı.** Zaman damgası ek yük ve muhtemelen farklı bir uç
   demektir; isteyen açar (K1).
2. **Akışlı sentez ayrı ele alınır.** Akışta hizalama verisi farklı gelir veya
   hiç gelmez — iki yol ayrı test edilir ve desteklenmeyen yol **açıkça** öyle
   der.
3. **Maliyet muhasebesi değişmez.** Karakter sayımı ve fiyat bugünkü gibi
   kalır; hizalama ek bir birim değildir.

### Kapsam dışı

| Dışarıda | Neden |
|---|---|
| Altyazı biçimi üretimi (SRT, VTT) | Biçim dönüştürme tüketicinin işidir; hizalama verisi ham hâliyle yeter |
| Transkripsiyon tarafında zaman damgası | `ISpeechTranscriber` ayrı bir sözleşme; ölçülmemiş ihtiyaç |

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions — F-117
public sealed record AgentDefinition
{
    public string? Instructions { get; init; }

    /// <summary>Culture-keyed instructions. Falls back to Instructions when no match.</summary>
    public IReadOnlyDictionary<string, string>? InstructionsByCulture { get; init; }
}

// AgentPrism.Abstractions — F-118
public sealed record SpeakRequest
{
    /// <summary>Requests word-level alignment. Default false.</summary>
    public bool IncludeTimestamps { get; init; }
}

public sealed record SpeechAlignment
{
    public required string Text { get; init; }
    public required TimeSpan Start { get; init; }
    public required TimeSpan End { get; init; }
}

public sealed record SpeakResponse
{
    /// <summary>Word-level alignment. Null when not requested or unsupported.</summary>
    public IReadOnlyList<SpeechAlignment>? Alignment { get; init; }
}
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `POST` | `/api/agents/{name}/run` | — | Gövdeye isteğe bağlı `culture` alanı |
| `POST` | `/api/voice/speak` | Operator | Gövdeye `includeTimestamps`, yanıta `alignment` |

### Arayüz payı

Agent editöründe dil sekmesi. Yeni bağımlılık yok. Bugünkü bundle 146.104 B
brotli; fazın payı kapanışta ölçülür. Yeni metin `en.ts` **ve** `tr.ts` (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/
├── Agents/AgentDefinition.cs           (değişir — InstructionsByCulture)
└── Voice/SpeechContracts.cs            (değişir — IncludeTimestamps, Alignment)

src/AgentPrism.Core/Compilation/        (değişir — kültür çözümlemesi)
src/AgentPrism.Voice/
├── Internal/ElevenLabsSpeechClient.cs  (değişir — zaman damgalı uç)
└── Tools/SpeakTool.cs                  (değişir — şema alanı)

src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Migrations/NNNN_agent_instructions_culture.sql (YENİ ×3)
src/AgentPrism.AspNetCore/              (sözleşme alanları)
src/AgentPrism.UI/                      (dil sekmesi + locales)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Kültür eşleşmezse hata verilir (varsayılana düşmez) | Birim | `InstructionCultureResolutionTests` |
| `tr-TR` → `tr` geri düşüşü çalışmaz | Birim | aynı sınıf |
| Kültür sözlüğü boşken bugünkü davranış değişir | Birim | aynı sınıf |
| Sürüm diff'i çok dilli metni gösteremez | Fonksiyonel | `AgentVersionDiffCultureTests` |
| Eval seti hangi dili koştuğunu bilmez | Fonksiyonel | `EvalCultureTests` |
| Deney/kanarya ağırlıkları dil başına ayrışır | Fonksiyonel | `ExperimentCultureTests` — seçenek A ile **ayrışmamalı** |
| `Accept-Language` sessizce talimatı değiştirir | Fonksiyonel (HTTP) | `CultureHeaderIgnoredTests` |
| Zaman damgası istenmeyen çağrıda ek yük getirir | Fonksiyonel | `SpeakTimestampOptOutTests` |
| Akışlı sentezde hizalama sessizce boş döner | Fonksiyonel | `SpeakStreamingAlignmentTests` — **açık davranış** |
| Sağlayıcı hizalamayı desteklemezse patlar | Fonksiyonel | `SpeakAlignmentUnsupportedTests` — `null` döner, hata değil |
| Karakter sayımı ve maliyet hizalamayla değişir | Birim | `VoicePricingTests` |
| Üç sağlayıcıda talimat sözlüğü sütunu ayrışır | Sözleşme | `tests/Shared/Contracts/` |

**Beş soru:** iptal — sentez iptali mevcut davranış · eşzamanlılık — aynı
agent'ın farklı kültürlerle paralel `run`'ları; `CompiledAgentCache` anahtarı
**kültürü taşımalı** 🚨 · boş/aşırı girdi — boş kültür anahtarı, tanımsız
kültür · başka kiracı — sözleşme testi · alt sistem hatası — sağlayıcı
hizalamayı desteklemez.

🚨 **`CompiledAgentCache` anahtarı en kolay kaçırılacak yerdir.** Kültür
anahtara girmezse ilk `run` hangi dilde derlendiyse sonraki tüm `run`'lar o
dilde çalışır. K-380 anahtarın kiracıyı zaten taşıdığını kaydediyor; aynı
listeye kültür girmelidir.

---

## Manuel Kabul Case'leri

### F-117

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Kültür sözlüğü boş agent | `run` | Bugünkü davranış birebir |
| 2 | `en` ve `tr` talimatlı agent | `culture: "tr"` ile `run` | Yanıt Türkçe talimata göre |
| 3 | Aynı agent | `culture: "tr-TR"` ile `run` | `tr` girdisi kullanılır |
| 4 | Aynı agent | `culture: "de"` ile `run` | Varsayılan `Instructions`; **hata yok** |
| 5 | Aynı agent | `Accept-Language: tr` başlığı, gövdede kültür yok | Varsayılan kullanılır; başlık **yok sayılır** |
| 6 | Aynı agent | Önce `tr`, sonra `en` ile arka arkaya `run` | İkinci `run` İngilizce — önbellek yanlış dili tutmuyor |
| 7 | Arayüz | Agent editörünü aç | 👤 Dil sekmesi; sürüm diff'i iki dili de gösterir |

### F-118

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 8 | ElevenLabs anahtarı | `includeTimestamps` **olmadan** sentez | Bugünkü yanıt birebir; `alignment` `null` |
| 9 | Aynı | `includeTimestamps: true` | 👤 Hizalama listesi gelir; süreler ses uzunluğuyla tutarlı |
| 10 | Aynı | Akışlı sentezde `includeTimestamps: true` | 👤 Davranış **ölçülür ve belgelenir**: destekleniyor veya açıkça desteklenmiyor |
| 11 | Aynı | 8 ve 9'un maliyetlerini karşılaştır | Karakter sayımı ve fiyat **aynı** |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kültür sürümün içinde mi dışında mı? | A: içinde · B: dil başına sürüm hattı | **A** — §72.1'de gerekçeli. B, Faz 19 ve 56'nın "tek aktif sürüm" sözleşmesini kırar |
| 2 | Talimat sözlüğü ayrı tabloya mı `jsonb` sütuna mı? | A: `jsonb` sütun · B: ayrı tablo | **A** — K-027 düz sözlük için `jsonb` diyor; dil sayısı küçüktür |
| 3 | Kültür `AgentSession` boyunca sabit mi? | A: her `run` kendi kültürünü taşır · B: oturuma yazılır | **A** — B oturum sözleşmesini büyütür; ölçülmemiş ihtiyaç |
| 4 | Zaman damgalı ucun şeması? | 🚨 **Doğrulanmadı** | Uygulama ilk adımda sağlayıcı dokümanını okur ve şemayı ölçerek yazar. Tahmin **edilmez** |
| 5 | F-117 ile F-118 birlikte mi kapanmalı? | A: bağımsız · B: birlikte | **A** — ortak altyapı yok; biri gecikirse diğeri beklemez |

---

## Bitiş Ölçütleri (DoD)

### F-117

- [x] Kültür sözlüğü boşken hiçbir davranış değişmez — `InstructionCultureResolutionTests`,
      MT-CORE-075 (gerçek `samples/AgentPrism.Api` koşumu)
- [x] `tr-TR` → `tr` → varsayılan geri düşüş zinciri çalışır; eşleşmeyen kültür
      **hata vermez** — `InstructionCultureResolutionTests`, MT-CORE-076..078
      (gerçek Anthropic Claude'a karşı koşuldu, bkz. Doğrulama komutları)
- [x] `CompiledAgentCache` anahtarı kültürü taşır — `CompiledAgentCacheTests.Recompiles_when_the_culture_changes`,
      `CultureInstructionEndpointTests.Back_to_back_runs...`, MT-CORE-080
- [x] `Accept-Language` başlığı talimatı **değiştirmez** — `CultureInstructionEndpointTests.Accept_Language_header_is_ignored`,
      MT-CORE-079
- [x] Sürüm diff'i çok dilli metni gösterir — `agent-detail.tsx`'te `cultureUnion` ile
      per-culture `DiffView` bölümü; manuel doğrulama MT-CORE-081 (👤 gerekir)
- [x] Üç SQL sağlayıcısı + bellek içi sözleşme koşumları geçer — `AgentDefinitionStoreContract.SaveAsync_round_trips_all_definition_fields`,
      gerçek Postgres (1118/1118), SqlServer (558/558), Sqlite (572/572) koşuldu

### F-118

- [x] `includeTimestamps` verilmediğinde bugünkü yanıt birebir aynı —
      `ElevenLabsSpeechClientTests.Without_IncludeTimestamps_alignment_is_null_and_the_plain_path_is_used`,
      `VoiceEndpointTests.IncludeTimestamps_false_returns_no_alignment`, MT-MM-091
- [x] `includeTimestamps: true` hizalama listesi döner; çıktı belgeye yazıldı —
      MT-MM-092, gerçek ElevenLabs'a karşı koşuldu (bkz. Doğrulama komutları:
      "Hello world" → 11 karakterlik hizalama, artan `start`/`end`)
- [x] Sağlayıcı desteklemiyorsa `null` döner — hata değil —
      `ElevenLabsSpeechClientTests.Missing_alignment_in_a_timestamped_response_yields_null_not_an_error`,
      `Mismatched_alignment_array_lengths_yield_null_rather_than_throwing`
- [x] Akışlı yol davranışı ölçüldü ve belgelendi — **desteklenmiyor, açıkça
      reddediliyor**: `ElevenLabsSpeechClientTests.Streaming_synthesis_rejects_IncludeTimestamps_explicitly`,
      K-502, MT-MM-093
- [x] Maliyet muhasebesi değişmedi — `VoiceEndpointTests.IncludeTimestamps_true_returns_the_alignment_and_the_same_cost_accounting`,
      MT-MM-094 (gerçek ElevenLabs'a karşı: `characters`/`cost` iki çağrıda birebir aynı)

### Ortak

- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/`format`
      tüm çözümde 0 uyarı/0 hata (bu kapanıştan hemen önce yeniden koşuldu)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı —
      aşağıdaki Doğrulama komutları bölümü gerçek çıktı taşır
- [x] `secret` taraması boş döndü — bu fazın eklediği hiçbir dosyada eşleşme yok
      (taramanın gösterdiği tüm satırlar önceki fazlardan kalma, yerel dev
      şifreleri/örnek metinler)
- [x] Manuel kabul case'leri iki alan dosyasına eklendi; otomatikleştirilebilenler
      koşuldu — MT-CORE-075..081, MT-MM-091..094; HTTP ile koşulabilenler
      (075-080, 091-094) gerçek sunucuya karşı çalıştırıldı, 081 ve MT-MM-093'ün
      insan-gerekli/otomatik parçaları işaretlendi
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 1 🔴 bulundu
      (`SourceLanguageTests` ihlali, test verisindeki Türkçe örnek metin) ve
      düzeltildi; 2 🟡 kararlara yazıldı (K-503, K-504); 3 🟢 ADAYLAR.md'ye
      (F-123) veya kapsam dışı bırakıldı
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz —
      `concepts/agents.md`, `guides/voice.md`
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı —
      `i18n.test.ts` geçti; javascript 173.5 KB gzip / embedded 148.8 KB brotli
      (budget 250 KB), Faz 68 sonrası ölçülen 151.3 KB'den **düşük**
      (araya giren fazların küçültmesi bu fazın payını maskeliyor; ayrı
      ölçülemedi)

### Doğrulama komutları — gerçek çıktı (2026-08-19, `samples/AgentPrism.Api`, gerçek Anthropic + ElevenLabs)

```bash
# Kültür çözümlemesi — agent instructionsByCulture={"tr": "..."} ile oluşturuldu
curl -s -X POST "$APU/api/agents/faz72-polyglot/run" -H "$APB" -H 'content-type: application/json' \
  -H 'Idempotency-Key: demo' -d '{"message":"how are you?","culture":"tr"}' | jq -r '.response.messages[0].contents[0].text'
# → "İyiyim, teşekkür ederim, sen nasılsın?"

curl -s -X POST "$APU/api/agents/faz72-polyglot/run" -H "$APB" -H 'content-type: application/json' \
  -H 'Idempotency-Key: demo2' -d '{"message":"how are you?","culture":"de"}' | jq -r '.response.messages[0].contents[0].text'
# → "I'm doing well, thank you for asking!"  (de eşleşmiyor, varsayılana düşer — hata YOK)

# Hizalama — gerçek ElevenLabs
curl -s -X POST "$APU/api/voice/speak" -H "$APB" -H 'content-type: application/json' \
  -d '{"text":"Hello world","voiceId":"hpp4J3VqNfWAUOO0d1Us","includeTimestamps":true}' | jq '.alignment | length'
# → 11  ("Hello world" = 11 karakter)
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| ⚠️ İki zayıf kalem tek fazda → DoD bulanıklaşır | DoD iki ayrı blok; Açık Soru 5 bağımsızlığı sabitler; biri iptal edilirse diğeri kapanabilir |
| 🚨 `CompiledAgentCache` kültürü taşımaz → yanlış dil önbelleğe girer | Manuel Case 6 ve `faz-uygulama` Adım 4 imza-gövde listesi |
| Sürüm/eval/deney sözleşmesi kırılır | Açık Soru 1 plan anında A ile kapatıldı; kapanışta karar defterine yazılır |
| Zaman damgalı uç şeması tahmin edilir | Açık Soru 4 uygulamayı ölçmeye zorlar; plan şema **yazmıyor** |
| K-232 ihlal edildi sanılır | §72.1 farkı yazıyor: talimat içeriktir, sunucu yanıtı değil |
| Sağlayıcı hizalamayı kaldırırsa özellik ölür | Sözleşme `null` döner; tek satıcıya bağlılık kapanış notuna yazılır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **Migration seti yazılmadı (K-499).** Plan "üç migration seti gerekli" diyordu.
   Uygulama başlamadan önce ölçüldü: `agent_definitions.definition` zaten TÜM
   `AgentDefinition` içeriğini `jsonb` blob'unda taşıyor (`AgentDefinitionPayload`,
   K-027'nin polimorfik-olmayan istisnası); `InstructionsByCulture` yeni bir
   sütun değil, mevcut blob'un yeni bir alanıdır. Bu, `faz-uygulama` Adım 1'in
   ("planın yapısal iddiasını kabul etmeden ölç") tam bir örneğidir.
2. **`SpeechAlignment` KARAKTER bazlıdır, planın taslağı "word-level"
   varsayıyordu (K-501).** Plan bunu bilerek "doğrulanmadı" işaretlemişti
   (Açık Soru 4). ElevenLabs'ın gerçek OpenAPI şeması (`api.elevenlabs.io/openapi.json`,
   doğrulandı) `CharacterAlignmentResponseModel` döner — karakter granülerliği.
   `SpeechAlignment.Character` alan adı `Text` değil `Character`'dır.
3. **`Tools/SpeakTool.cs`'e dokunulmadı (K-504).** Plan dosya listesi bunu
   "değişir" diye işaretlemişti. Model-çağrılabilir `speak` tool'unun şeması
   `includeTimestamps` almaz; hizalama yalnız operatör HTTP yolunda
   (`POST /api/voice/speak`) — planın kendi HTTP uç tablosu zaten yalnız o
   ucu adlandırıyordu.
4. **Kültür yalnız KÖK agent'ın derlenmesinde kullanılır (K-503, denetimde
   bulundu).** `CallableAgentResolver`/`ChildAgentInvoker`, `EvalJobHandler`,
   `RunReplayService` her zaman `culture: null` çözümler. Plan bu sınırı
   adlandırmadı; ADAYLAR.md'ye F-123 olarak yazıldı.
5. **`IAgentSource`/`IAgentCatalog`/`IVersionedAgentSource` imzaları
   değiştirildi, yeni aşırı yükleme eklenmedi.** `culture` parametresi mevcut
   `ResolveAsync`/`ResolveVersionAsync` metotlarına eklendi (yeni bir aşırı
   yükleme değil) — pakette hiçbir şey henüz yayınlanmadığı için (`PublicAPI.Shipped.txt`
   boş) kırıcı değişiklik bedavaydı; ~20 üretim çağrı yeri ve ~9 test dosyası
   mekanik olarak güncellendi (imza değişikliği derleyiciyi zorladı).

## Bu Fazda Verilen Kararlar

K-499, K-500, K-501, K-502, K-503, K-504 — bkz. `docs/KARARLAR.md`. Ayrıca
kapsam dışı archival: K-422 ve K-389'un tam gerekçesi `docs/arsiv/KARARLAR-GECMISI.md`'ye
taşındı (`KARARLAR.md` bütçesini bu fazın altı yeni kararı aştırdığı için —
içerik silinmedi, taşındı).

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions — F-117
public sealed record AgentDefinition
{
    // ... mevcut alanlar ...
    public IReadOnlyDictionary<string, string>? InstructionsByCulture { get; init; }
}

public interface IAgentSource
{
    ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default);
}

public interface IAgentCatalog
{
    ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken);
    ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default);
}

public interface IVersionedAgentSource : IAgentSource
{
    ValueTask<AIAgent?> ResolveVersionAsync(string agentName, int version, string? culture = null, CancellationToken cancellationToken = default);
}

public static class InstructionCultureResolver
{
    public static string? Resolve(AgentDefinition definition, string? culture);
}

public sealed class CompiledAgentCache
{
    // Yeni aşırı yükler — culture parametresi son (factory'den önce)
    public AIAgent GetOrAdd(string tenantId, string name, int version, string dependencyFingerprint, string culture, Func<AIAgent> factory);
    public ValueTask<AIAgent> GetOrAddAsync(string tenantId, string name, int version, string dependencyFingerprint, string culture, Func<ValueTask<AIAgent>> factory);
}

// AgentPrism.AspNetCore — F-117
public sealed record AgentRunRequest
{
    // ... mevcut alanlar ...
    public string? Culture { get; init; }
}

public sealed record AgentDefinitionRequest
{
    // ... mevcut alanlar ...
    public IReadOnlyDictionary<string, string>? InstructionsByCulture { get; init; }
}

// AgentPrism.Abstractions — F-118
public sealed record SpeechRequest
{
    // ... mevcut alanlar ...
    public bool IncludeTimestamps { get; init; }
}

public sealed class SpeechAudio
{
    // ... mevcut alanlar ...
    public IReadOnlyList<SpeechAlignment>? Alignment { get; init; }
}

public sealed record SpeechAlignment
{
    public required string Character { get; init; }   // KARAKTER, kelime DEĞİL — bkz. Plandan Sapmalar #2
    public required TimeSpan Start { get; init; }
    public required TimeSpan End { get; init; }
}

// AgentPrism.AspNetCore — F-118
public sealed record SpeakRequest
{
    // ... mevcut alanlar ...
    public bool IncludeTimestamps { get; init; }
}

public sealed record SpeakResponse
{
    // ... mevcut alanlar ...
    public IReadOnlyList<SpeechAlignment>? Alignment { get; init; }
}
```

### HTTP `endpoint`'leri (gerçekleşen — plandakiyle aynı)

| Metot | Yol | Gövde/yanıt eklentisi |
|---|---|---|
| `POST` | `/api/agents/{name}/run` | Gövdeye `culture` |
| `PUT`/`POST` | `/api/agents` (`AgentDefinitionRequest`) | Gövdeye `instructionsByCulture` |
| `POST` | `/api/voice/speak` | Gövdeye `includeTimestamps`, yanıta `alignment` |

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/
├── Agents/AgentDefinition.cs               (değişti — InstructionsByCulture)
├── Agents/IAgentSource.cs                  (değişti — culture parametresi)
├── Agents/IAgentCatalog.cs                 (değişti — culture parametresi, iki metot)
├── Agents/IVersionedAgentSource.cs         (değişti — culture parametresi)
└── Voice/SpeechModels.cs                   (değişti — IncludeTimestamps, Alignment, SpeechAlignment YENİ)
    Voice/SpeechContracts.cs                (değişti — SpeakRequest.IncludeTimestamps, SpeakResponse.Alignment)

src/AgentPrism.Core/
├── Compilation/InstructionCultureResolver.cs   (YENİ)
├── Compilation/CompiledAgentCache.cs           (değişti — Culture anahtar bileşeni)
├── Compilation/AgentDefinitionCompiler.cs      (değişti — culture Compile/CompileAsync'e eklendi)
├── Compilation/AgentDefinitionValidator.cs     (değişti — çağrı yeri düzeltmesi)
├── Catalog/CodeAgentSource.cs                  (değişti)
├── Catalog/DefinitionStoreAgentSource.cs       (değişti)
├── Catalog/CompositeAgentCatalog.cs            (değişti)
├── Graph/CallableAgentResolver.cs              (değişti — çağrı yeri, culture: null)
├── Replay/RunReplayService.cs                  (değişti — çağrı yeri, culture: null)
├── Evaluation/EvalJobHandler.cs                (değişti — çağrı yeri, culture: null)
├── Scheduling/Agent{Batch,Run}JobHandler.cs    (değişti — çağrı yeri, culture: null)
├── Approvals/ApprovalResumeJobHandler.cs       (değişti — çağrı yeri, culture: null)
├── Sessions/ConversationBranchService.cs       (değişti — çağrı yeri, culture: null)
├── Privacy/SessionConversationResolver.cs      (değişti — çağrı yeri, culture: null)
└── Voice/VoiceConversationDriver.cs            (değişti — çağrı yeri, culture: null)

src/AgentPrism.Sql.Shared/Internal/AgentDefinitionPayload.cs   (değişti — InstructionsByCulture, migration YOK)

src/AgentPrism.AspNetCore/
├── Contracts/AgentContracts.cs                 (değişti — AgentRunRequest.Culture, AgentDefinitionRequest.InstructionsByCulture)
├── Endpoints/AgentEndpoints.cs                 (değişti — request.Culture iki run yoluna geçirildi)
├── Endpoints/VoiceEndpoints.cs                 (değişti — IncludeTimestamps/Alignment geçirildi)
├── A2A/ExternalAgentProxy.cs                   (değişti — çağrı yeri)
├── Internal/ChatHistoryReader.cs               (değişti — çağrı yeri)
├── McpServer/CatalogToolCallHandler.cs         (değişti — çağrı yeri)
└── OpenAICompat/OpenAI{ChatCompletions,Responses}Endpoints.cs   (değişti — çağrı yeri)

src/AgentPrism.Voice/Internal/
├── ElevenLabsJson.cs                (değişti — ElevenLabsAudioWithTimestampsResponse, ElevenLabsCharacterAlignment YENİ DTO'lar)
└── ElevenLabsSpeechClient.cs        (değişti — SynthesizeWithTimestampsAsync, ReadTimestampedAudioAsync, ToAlignment,
                                       SynthesizeStreamingAsync artık IncludeTimestamps'i reddediyor)

src/AgentPrism.UI/frontend/src/
├── screens/agent-editor.tsx         (değişti — Instructions by culture bölümü)
├── screens/agent-detail.tsx         (değişti — sürüm diff'inde per-culture bölüm)
├── lib/types.ts                     (değişti — instructionsByCulture alanları)
└── locales/{en,tr}.ts               (değişti)

tests/
├── AgentPrism.Core.UnitTests/Compilation/InstructionCultureResolutionTests.cs   (YENİ)
├── AgentPrism.Core.UnitTests/Compilation/CompiledAgentCacheTests.cs             (değişti — iki yeni test)
├── AgentPrism.Core.UnitTests/{Catalog,Diagnostics,Evaluation,Scheduling,Graph,Fakes}/*  (değişti — fake imza güncellemesi)
├── AgentPrism.Voice.UnitTests/ElevenLabsSpeechClientTests.cs                    (değişti — 8 yeni test)
├── AgentPrism.AspNetCore.FunctionalTests/CultureInstructionEndpointTests.cs     (YENİ)
├── AgentPrism.AspNetCore.FunctionalTests/VoiceEndpointTests.cs                  (değişti — 2 yeni test)
├── AgentPrism.Workflows.UnitTests/Fakes/WorkflowTestHost.cs                     (değişti — fake imza güncellemesi)
├── Shared/Contracts/AgentDefinitionStoreContract.cs                            (değişti — round-trip testine InstructionsByCulture eklendi)
└── AgentPrism.PostgreSql.IntegrationTests/*, AgentPrism.AspNetCore.FunctionalTests/AgentDelegationTests.cs  (değişti — çağrı yeri)

docs-site/src/content/docs/
├── concepts/agents.md               (değişti — Culture-keyed instructions bölümü)
└── guides/voice.md                  (değişti — Character-level timing bölümü)

docs/manuel-test/
├── 02-CEKIRDEK-VE-KATALOG.md        (değişti — MT-CORE-075..081)
└── 19-COK-MODLULUK-VE-SES.md        (değişti — MT-MM-091..094)
```

## Denetim Bulguları

Bağımsız denetim taze bağlamlı bir alt agent ile koşuldu (`.agents/skills/faz-denetim/SKILL.md`).

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `SourceLanguageTests` ihlali: test verisindeki örnek talimat metni gerçek Türkçe sözcükler taşıyordu (`tests/**` K-408 kapsamında) | **Düzeltildi** — `CultureInstructionEndpointTests.cs` ve `AgentDefinitionStoreContract.cs`'teki örnek metinler İngilizce köklü, dil-nötr placeholder'a çevrildi (`"Answer using the tr/fr culture text."`); `dotnet test` yeniden koşuldu, 998/998 |
| 2 | 🟡 | `CallableAgentResolver` her zaman `culture: null` gönderiyor — alt agent ebeveynin kültürünü görmez | **Gerekçelendi** — K-503 olarak karar defterine yazıldı; F-123 olarak ADAYLAR.md'ye eklendi |
| 3 | 🟡 | Planlanan dosya listesi `Tools/SpeakTool.cs`'in değişeceğini söylüyordu, dokunulmadı | **Gerekçelendi** — K-504 olarak karar defterine yazıldı, Plandan Sapmalar #3 |
| 4 | 🟢 | `EvalJobHandler` kültüre özgü talimatı test edemiyor | ADAYLAR.md F-123'e dahil edildi |
| 5 | 🟢 | `RunReplayService` orijinal `run`'ın kültürünü saklamadığı için yeniden oynatma kültürü koruyamıyor | ADAYLAR.md F-123'e dahil edildi |
| 6 | 🟢 | `agent-editor.tsx`'te kültür satırları `key={index}` kullanıyor | Devredilmedi — kontrollü input deseni (mevcut `fallbacks` listesiyle aynı desen) satır silindiğinde değer kaymasını önlüyor; gerçek bir kusur değil |

🔴 kapandıktan sonra dört kapı (`build`/`test`/`pack`/`format`) yeniden koşuldu — hepsi 0 uyarı/0 hata.

## Sonraki Faza Devir Notu

- **Kültür yalnız kök agent'ta çalışır (K-503/F-123).** Faz 73 (tüketici agent
  desteği) veya sonrası çok dilli bir alt-agent zinciri isterse önce `run`
  kaydına kültür alanı eklemek gerekir — bugün hiçbir yerde saklanmıyor.
- **`InstructionCultureResolver.Resolve` saf bir statik fonksiyondur** —
  `AgentDefinitionCompiler`'ın DIŞINDA, bağımsız test edilebilir. Yeni bir
  kültür kaynağı (ör. oturum bazlı varsayılan) eklenirse buraya dokunulur.
  `CompiledAgentCache`'in culture'ı anahtara EKLEMESİ gerektiğini unutma —
  yeni bir istek-bazlı boyut eklerken bu deseni tekrarla (bkz. K-380'in aynı
  dersi tenant için verdiği).
- **`SpeechAlignment` karakter bazlıdır.** Bir tüketici kelime/cümle
  granülerliği isterse (altyazı üretimi gibi) bu AgentPrism'in işi değil —
  `docs/72`'nin kapsam dışı tablosu ve `docs-site/guides/voice.md` bunu açıkça
  söylüyor. Yanlışlıkla "AgentPrism SRT üretsin" gibi bir işe girişilmesin.
- **Akışlı zaman damgalı sentez YAZILMADI (K-502).** ElevenLabs'ın
  `/stream/with-timestamps` ucu ayrı bir JSON-parça protokolü konuşur; bugünkü
  `ISpeechSynthesizer.SynthesizeStreamingAsync` imzası bunu taşıyamaz. Gerçek
  bir ihtiyaç ölçülürse yeni bir arayüz üyesi gerekir (mevcut üye
  kırılmadan) — `docs/hafiza/ses-ve-konusma.md`'de şema notu var.
- **`docs/KARARLAR.md` bütçesi bu fazda aşıldı ve iki eski karar (K-389,
  K-422) arşive taşındı.** Bir sonraki faz altı+ yeni karar eklerse aynı
  duruma düşer — `python3 scripts/dokuman-bakim.py` erken koşulmalı, bütçe
  aşımı fazın SONUNDA sürpriz olmasın.

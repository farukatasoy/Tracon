# Faz 72 — Çok Dilli Talimat ve Zaman Damgalı Sentez

> **Durum:** 📋 Planlandı (2026-08-18)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-117**, **F-118**
> **Önkoşul:** [Faz 19](19-SURUM-KARSILASTIRMA-VE-AB.md) — agent sürümleme ve diff · [Faz 28](28-SES-TOOLLARI.md) — ses tool'ları ve ElevenLabs istemcisi
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Voice`, `AgentPrism.Sql.Shared`, `AgentPrism.{PostgreSql,SqlServer,Sqlite}`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli — üç set** (talimat sözlüğü). Numara uygulama anında alınır (K-178)
> **Public API:** **büyüyor** — `AgentDefinition` ve `SpeakRequest`/`SpeakResponse` alan alır. `PublicAPI.Shipped.txt` bugün **boş** — şimdi bedava
> **Site etkisi:** `concepts/agents.md`, `guides/voice.md`, `reference/configuration.md`
> **Manuel test alanı:** [`docs/manuel-test/19-COK-MODLULUK-VE-SES.md`](manuel-test/19-COK-MODLULUK-VE-SES.md) · [`docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md`](manuel-test/02-CEKIRDEK-VE-KATALOG.md)

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

- [ ] Kültür sözlüğü boşken hiçbir davranış değişmez
- [ ] `tr-TR` → `tr` → varsayılan geri düşüş zinciri çalışır; eşleşmeyen kültür
      **hata vermez**
- [ ] `CompiledAgentCache` anahtarı kültürü taşır (Manuel Case 6 kanıtıyla)
- [ ] `Accept-Language` başlığı talimatı **değiştirmez**
- [ ] Sürüm diff'i çok dilli metni gösterir
- [ ] Üç SQL sağlayıcısı + bellek içi sözleşme koşumları geçer

### F-118

- [ ] `includeTimestamps` verilmediğinde bugünkü yanıt birebir aynı
- [ ] `includeTimestamps: true` hizalama listesi döner; çıktı belgeye yazıldı
- [ ] Sağlayıcı desteklemiyorsa `null` döner — hata değil
- [ ] Akışlı yol davranışı ölçüldü ve belgelendi
- [ ] Maliyet muhasebesi değişmedi

### Ortak

- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri iki alan dosyasına eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
# Kültür çözümlemesi
curl -s -X POST http://localhost:5081/agentprism/api/agents/greeter/run \
  -H 'content-type: application/json' -d '{"message":"merhaba","culture":"tr"}' | jq -r '.text'

# Hizalama
curl -s -X POST http://localhost:5081/agentprism/api/voice/speak \
  -H 'content-type: application/json' -d '{"text":"merhaba dunya","includeTimestamps":true}' \
  | jq '.alignment | length'
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

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.

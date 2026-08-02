# Faz 26 — Anthropic (Claude) ve Google Gemini Sağlayıcıları

> **Durum:** 📋 Planlandı
> **Kaynak:** [BEYIN-FIRTINASI.md](BEYIN-FIRTINASI.md) · **F-01**, **F-02**
> **Önkoşul:** [Faz 8](08-SAGLAYICI-GENISLEMESI.md) — sağlık denetimi ve devre kesici hazır olmalı
> **Paketler:** **`AgentPrism.Anthropic` (YENİ)**, **`AgentPrism.Google` (YENİ)**
> **Migration:** Yok

---

## Bu Faza Başlarken

1. [`03-SAGLAYICI-VE-DERLEYICI.md`](03-SAGLAYICI-VE-DERLEYICI.md) — **şablon budur**
2. [`08-SAGLAYICI-GENISLEMESI.md`](08-SAGLAYICI-GENISLEMESI.md) — `IModelProviderHealthCheck`, devre kesici
3. [`KARARLAR.md`](KARARLAR.md) — **K-032** (model kataloğu yapılandırmadan), **K-035** (ayar sınıfı `record` olamaz), **K-007** (bağımlılık), **K-006** (AOT)
4. Bu doküman

---

## Amaç

Faz 8'den sonra Claude ve Gemini'ye OpenRouter üzerinden zaten erişilebilir.
Bu faz **birinci sınıf** desteği getirir: sağlayıcıya özgü yetenekler
(prompt caching, `thinking` blokları, uzun bağlam, güvenlik filtresi yanıtları)
ancak doğrudan SDK ile kullanılabilir.

Bu yüzden faz sıranın sonlarındadır: **acil değildir**, ama tamamlayıcıdır.

---

## 26.1 — ⚠️ İlk İş: SDK'ları Ölçün

Bu fazın tüm maliyeti tek bir soruya bağlıdır: **paket hazır bir `IChatClient`
veriyor mu?**

```bash
dotnet package search "Anthropic.SDK" --exact-match --format json
dotnet package search "Google_GenerativeAI" --exact-match --format json

# Paketin ici — IChatClient adaptoru var mi?
APIDUMP_PACKAGES="Anthropic.SDK@5.10.0" APIDUMP_PREFIXES="Anthropic" \
  .agents/skills/maf-api-kesfi/scripts/dump-api.sh '*ChatClient*'
```

Bilinen durum (2026-08-02 itibarıyla, **doğrulanmalı**):

| Paket | Son sürüm | Tür |
|-------|-----------|-----|
| `Anthropic.SDK` | 5.10.0 | Topluluk |
| `Google_GenerativeAI` | 3.6.7 | Topluluk |

İki sonuç, iki farklı maliyet:

| Durum | İş |
|-------|-----|
| SDK `IChatClient` adaptörü **sunuyor** | `AgentPrism.OpenAI` ile birebir aynı şekil: ~6 dosya, 1–2 gün |
| **Sunmuyor** | `IChatClient` uygulamasını **biz** yazarız: mesaj eşlemesi, akış, tool çağrısı, kullanım sayaçları. Belirgin biçimde büyük iş |

> 🚨 **Topluluk paketi bağımlılığı bir karardır.** Yayınlanan bir NuGet paketi,
> tüketiciye o bağımlılığı geçirir. Paketin bakım durumu, lisansı ve sürüm
> politikası uygulamadan önce değerlendirilir ve karar defterine yazılır.
> Alternatif: `Microsoft.Extensions.AI` resmî bir sağlayıcı yayınladıysa **o
> tercih edilir**.

---

## 26.2 — Paket Şekli (`AgentPrism.OpenAI` şablonu)

Her iki paket de aynı altı parçadan oluşur:

```
src/AgentPrism.Anthropic/
├── AnthropicProviderOptions.cs          // class, record DEGIL (K-035)
├── AnthropicProviderOptionsValidator.cs // elle yazilmis (AOT)
├── AnthropicProviderNames.cs
├── AnthropicChatClientFactory.cs        // istemci kurulumu + boru hatti
├── AnthropicModelCatalog.cs             // YAPILANDIRMADAN (K-032)
├── AnthropicModelProvider.cs            // IModelProvider + IModelProviderHealthCheck
├── AnthropicProviderExtensions.cs       // UseAnthropic(...)
└── README.md
```

```csharp
builder.AddAgentPrism()
       .UseAnthropic(apiKey, o => { o.DefaultModel = "claude-…"; })
       .UseGemini(apiKey, o => { o.DefaultModel = "gemini-…"; });
```

Boru hattı `AgentPrism.OpenAI` ile aynıdır ve **atlanmaz**:

```csharp
chatClient.AsBuilder()
          .UseFunctionInvocation()      // tool dongusu
          .UseOpenTelemetry("AgentPrism")  // Faz 6 span'leri
          .Build();
```

Faz 8'in devre kesici dekoratörü zaten `IChatClient` düzeyindedir; bu paketler
onu **bedava** alır ve yeniden yazmaz.

---

## 26.3 — Sağlayıcıya Özgü Ayarlar

`ModelBinding` bugün beş alan taşıyor: `Provider`, `Model`, `Temperature`,
`MaxOutputTokens`, `TopP`, `ReasoningEffort`. Anthropic'in prompt caching'i veya
Gemini'nin güvenlik eşikleri buraya sığmaz.

İki seçenek:

| Yol | Değerlendirme |
|-----|---------------|
| `ModelBinding`'e sağlayıcıya özgü alanlar eklemek | Sözleşme kirlenir; `AgentPrism.Abstractions` bir satıcının kavramını taşır |
| `ModelBinding.ProviderSettings` — `IReadOnlyDictionary<string, JsonElement>` | Sözleşme temiz kalır; sağlayıcı kendi anahtarlarını okur |

**Öneri: ikinci yol.** Bilinmeyen anahtar **sessizce yok sayılmaz** — K-034'ün
`ReasoningEffort` için verdiği kararın aynısı: sağlayıcı tanımadığı bir anahtar
görürse derleme hatası verir ve geçerli anahtarları listeler.

```csharp
public sealed record ModelBinding
{
    // ...mevcut uyeler
    public IReadOnlyDictionary<string, JsonElement> ProviderSettings { get; init; }
        = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}
```

Örnekler: `anthropic.promptCaching = true`, `anthropic.thinking.budgetTokens = 8000`,
`google.safety.harassment = "BLOCK_ONLY_HIGH"`.

---

## 26.4 — Bilinen Davranış Farkları

Bunlar test edilir ve README'ye yazılır. "Aynı çalışır" demek yanlış olur.

| Konu | Anthropic | Gemini |
|------|-----------|--------|
| Tool çağrı biçimi | `tool_use` / `tool_result` blokları | `functionCall` / `functionResponse` parçaları |
| Sistem mesajı | Ayrı `system` alanı, mesaj listesinde **değil** | `systemInstruction` alanı |
| Akışta kullanım | `message_delta` içinde artımlı | Sonda toplu gelebilir |
| Güvenlik filtresi | `stop_reason` | `finishReason: SAFETY` + `promptFeedback` — **yanıt boş gelebilir** |
| Çok modluluk | Görsel destekli (Faz 14 ile) | Görsel + ses + video |
| Boş yanıt | Ender | Güvenlik filtresinde **sık** |

🚨 **Güvenlik filtresi boş yanıt üretebilir.** `AgentResponse.Text` boş
geldiğinde AgentPrism bunu "başarılı ama boş" değil, **açık bir hata** olarak
kaydetmelidir: `RunRecord.ErrorType = "content_filtered"`. Sessiz boş yanıt,
hata ayıklaması en zor durumdur.

---

## Testler

| Proje | Yeni test |
|-------|-----------|
| `AgentPrism.Anthropic.UnitTests` (**yeni**) | Kayıt; katalog yapılandırmadan; `ProviderSettings` eşlemesi; bilinmeyen anahtar hatası; boru hattı üyeleri (`GetService`); sır sızmayan `ToString` (K-035) |
| `AgentPrism.Google.UnitTests` (**yeni**) | Aynısı + güvenlik filtresi yanıtının hata olarak kaydı |
| `AgentPrism.AspNetCore.FunctionalTests` | Üç sağlayıcının aynı anda kayıtlı olması; `/api/models` çıktısı |

**Gerçek model kanıtı zorunludur:** her iki sağlayıcıyla bir tool çağrısı
içeren çalıştırma yapılır. Tool çağrı eşlemesi burada kırılır — birim testi
bunu yakalamaz.

---

## Bu Fazda Verilecek Kararlar

1. **Hangi SDK kullanıldı ve neden** (topluluk paketi bağımlılığı gerekçesi).
2. **`ModelBinding.ProviderSettings` sözleşmeye eklendi** — sağlayıcı kavramları
   soyutlamaya sızmasın diye.
3. **Bilinmeyen sağlayıcı ayarı derleme hatasıdır** (K-034 deseni).
4. **Güvenlik filtresi boş yanıtı hata olarak kaydedilir.**
5. **AOT durumu ölçümle** — SDK uyumlu değilse paket bayrağı `false`.

---

## Açık Sorular

1. **İki paket mi, tek `AgentPrism.Providers` paketi mi?** Tek paket iki SDK
   bağımlılığını birden getirir — K-001'e aykırıdır. Öneri: **iki ayrı paket**.
2. **Paket adı `AgentPrism.Google` mü `AgentPrism.Gemini` mi?** Vertex AI de
   eklenebilir. Öneri: **`AgentPrism.Google`**.
3. **Topluluk SDK'sı yerine ham HTTP istemcisi yazılsın mı?** Bağımlılık
   olmaz ama bakım yükü bize geçer. Öneri: **SDK**, bakımı durursa yeniden
   değerlendirilir.
4. **Prompt caching varsayılan açık mı?** Maliyeti düşürür ama davranışı
   değiştirir. Öneri: **kapalı**, `ProviderSettings` ile açılır.

---

## Bitiş Ölçütleri (DoD)

- [ ] İki paket üretiliyor; `dotnet pack` sayısı doğru
- [ ] Her iki sağlayıcıyla gerçek çalıştırma ve **tool çağrısı** çalışıyor
      (gerçek çıktı dokümanda)
- [ ] Akışlı çalıştırmada token kullanımı doğru toplanıyor
- [ ] Sağlık denetimi ve devre kesici üç sağlayıcıda da çalışıyor
- [ ] Güvenlik filtresi yanıtı hata olarak kaydediliyor
- [ ] `ProviderSettings` ile en az bir sağlayıcıya özgü ayar uçtan uca çalışıyor
- [ ] Paket kontrol listesi tamam; sır taraması boş
- [ ] Dört doğrulama kapısı sıfır uyarı

---

## Riskler

| Risk | Önlem |
|------|-------|
| Topluluk SDK'sı bakımsız kalır | Bağımlılık tek pakette izole; ham HTTP'ye geçiş yolu açık |
| `IChatClient` adaptörü yoksa iş büyür | İlk adım ölçümdür; kapsam ona göre belirlenir |
| Tool eşlemesi sessizce bozulur | Gerçek model testi zorunlu |
| SDK AOT uyumsuz | Paket bazlı bayrak |
| `ProviderSettings` çöp kutusuna döner | Bilinmeyen anahtar hata verir; her sağlayıcı desteklediği anahtarları README'de listeler |

---

## Sonraki Faza Devir Notu

- Faz 27 (Azure) aynı şablonu kullanır; `ProviderSettings` mekanizması orada da
  gerekli olacaktır.
- Faz 20'nin fiyat çözümlemesi bu sağlayıcıların model kataloglarında da
  çalışmalıdır — fiyat yine **yapılandırmadan** gelir (K-032).

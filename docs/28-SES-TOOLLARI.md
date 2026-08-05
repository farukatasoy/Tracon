# Faz 28 — Ses Tool'ları (ElevenLabs)

> **Durum:** 📋 Planlandı
> **Kaynak:** [BEYIN-FIRTINASI.md](BEYIN-FIRTINASI.md) · **F-13** (1/2)
> **Önkoşul:** [Faz 14](14-COK-MODLULUK.md) — ses çıktısı `attachments` deposunu kullanır
> **Sonraki:** [Faz 29](29-KONUSMA-KATMANI.md) — gerçek zamanlı konuşma katmanı
> **Paketler:** **`AgentPrism.Voice` (YENİ)**
> **Migration:** Yok

---

## Bu Faza Başlarken

1. [`14-COK-MODLULUK.md`](14-COK-MODLULUK.md) — `IAttachmentStore`, tür beyaz listesi
2. [`KARARLAR.md`](KARARLAR.md) — **K-012** (tool'lar yalnız kodda), **K-009** (sırlar), **K-007** (bağımlılık), **K-035** (ayar sınıfı `record` olamaz), **K-211** (yeni SDK'da çalışma anı ölçümü)
3. [`03-SAGLAYICI-VE-DERLEYICI.md`](03-SAGLAYICI-VE-DERLEYICI.md) — tool kaydı ve derleyici
4. Bu doküman

---

## Faz 27'den Devralınan (yeni paket kalıbı)

`AgentPrism.Voice` bu depodaki **onuncu** pakettir. Kalıp oturdu; en kısa örnek
`src/AgentPrism.Azure`'dur (dokuz dosya, dekoratörü yok).

### Yeni paket kontrol listesi — hepsi gerekli

| Adım | Atlanırsa |
|---|---|
| `src/AgentPrism.Voice/README.md` | build `AGENTPRISM0001` ile kırılır |
| `PublicAPI.Shipped.txt` + `PublicAPI.Unshipped.txt` (içerik: `#nullable enable`) | analyzer hatası |
| `AgentPrism.slnx` içine `<Project Path=… />` (paket **ve** test projesi) | proje hiç derlenmez |
| `DependencyDirectionTests.AllowedReferences` içine `["AgentPrism.Voice"] = ["AgentPrism.Core"]` | test kırılır |
| `Directory.Packages.props` içine SDK sürümü, **gerekçe yorumuyla** | merkezî sürüm yönetimi hata verir |
| `samples/AgentPrism.Api` içine `ProjectReference` + `appsettings.json` şeması | örnek fazı gösteremez |

Meta pakete **eklenmez** (K-185 · K-209 · Faz 27 deseni): sağlayıcı paketleri
tüketicinin bağımlılık grafiğine zorla girmez.

### 🚨 Faz 27'de bedel ödeten tuzak

**Derleme yeşilliği bir SDK'nın çalıştığını kanıtlamaz.** `Azure.AI.OpenAI` 2.1.0,
`OpenAI` 2.1.0'a karşı derlenmişti; merkezî paket yönetimi `OpenAI` 2.12.0'ı zorladı.
NuGet çakışmayı **sessizce** çözdü, `dotnet build` **sıfır uyarı** verdi, ve ilgili
uzantı metotlarının tamamı çalışma anında `MissingMethodException` attı.

Bir SDK, **başka bir SDK'nın tipini** genişletiyorsa (uzantı metodu, `partial`,
alt sınıf), o yolu gerçekten çağırarak ölçün. `Directory.Packages.props`'a yeni
bir satır eklemeden önce:

```bash
dotnet list <proje> package --include-transitive   # agirlik
# + kucuk bir konsol projesinde gercek cagri
```

Ses SDK'ları bu riski taşır: çoğu `System.Net.Http` ve `System.Text.Json`
üzerinde kendi katmanını kurar.

### Sağlık denetimi ve sır sızıntısı kalıbı

Dört sağlayıcı paketinde de aynıdır ve kopyalanabilir
(`AzureOpenAIProviderHealthCheck` en kısa örnektir):

- SDK yerine doğrudan `HttpClient` — hata detayının sır ve adres taşımadığı
  denetlenebilir olsun diye
- `HttpRequestException.Message` **kullanılmaz**; `exception.HttpRequestError`
  adres taşımayan bir kategori adıdır
- `internal static` yardımcılar (adres birleştirme, gövde ayrıştırma) ağ çağrısı
  olmadan test edilir
- `SecretLeakTests` sekiz çıktıda anahtarı arar; ayar sınıfının kendi `ToString`'ini
  tanımlamadığını da doğrular (K-035)

---

## Amaç

Kullanıcı cevabı **konuştuğunu duysun** — ama henüz gerçek zamanlı bir konuşma
kurmadan. Bu faz iki tool ekler ve `AgentPrism.Voice` paketinin temelini atar:

- **Metinden sese** (TTS): agent bir metni seslendirir, ses `attachments`
  tablosuna yazılır
- **Sesten metne** (STT): yüklenen bir ses dosyası metne çevrilir

Kullanıcı nihai hedefin **konuşma katmanı** olduğunu belirtti (K-065). Bu faz o
hedefin ilk adımıdır: sağlayıcı soyutlaması, kimlik doğrulama, ses depolama ve
maliyet burada çözülür; Faz 29 yalnız **gerçek zamanlılık** sorununu ele alır.

---

## 28.1 — Soyutlama Önce, Sağlayıcı Sonra

ElevenLabs bir tercihtir, bir bağımlılık değil. Paket iki sözleşme tanımlar:

```csharp
namespace AgentPrism;

public interface ISpeechSynthesizer
{
    string ProviderName { get; }
    ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(SpeechRequest request, CancellationToken ct = default);
    ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken ct = default);
}

public interface ISpeechTranscriber
{
    string ProviderName { get; }
    ValueTask<SpeechTranscript> TranscribeAsync(Stream audio, string mediaType,
                                                SpeechTranscriptionOptions? options = null,
                                                CancellationToken ct = default);
}

public sealed record SpeechRequest
{
    public required string Text { get; init; }
    public string? VoiceId { get; init; }
    public string? ModelId { get; init; }
    public string OutputFormat { get; init; } = "audio/mpeg";
    public float? Speed { get; init; }
}

public sealed record SpeechAudio
{
    public required ReadOnlyMemory<byte> Data { get; init; }
    public required string MediaType { get; init; }
    public TimeSpan? Duration { get; init; }
    public int? CharactersBilled { get; init; }     // maliyet icin
}
```

`AgentPrism.Voice` içinde ElevenLabs uygulaması bulunur; başka bir sağlayıcı
isteyen kendi uygulamasını kaydeder (K4).

### Bağımlılık kararı

`ElevenLabs-DotNet` (3.7.2) mevcuttur. Alternatif: ham `HttpClient` ile üç uç
(`/v1/text-to-speech`, `/v1/speech-to-text`, `/v1/voices`).

**Öneri: ham `HttpClient`.** Gerekçe: kullanılan yüzey üç uçtan ibarettir, JSON
sözleşmesi basittir ve `System.Text.Json` kaynak üreteci ile AOT uyumlu kalır.
Bir topluluk SDK'sı bakım ve sürüm riskini tüketiciye taşır (K-007).

Karar uygulama oturumunda ölçümle kesinleşir: SDK'nın yüzeyi gerçekten büyük bir
kolaylık sağlıyorsa alınabilir; gerekçe yazılır.

---

## 28.2 — Tool'lar

K2 korunur: tool'lar **kodda** tanımlıdır. Kullanıcı arayüzden yalnız **seçer**.

```csharp
builder.AddAgentPrism()
       .UseVoice(o =>
       {
           o.Provider = "elevenlabs";
           o.ApiKeyConfigurationKey = "ElevenLabs:ApiKey";   // ANAHTAR ADI, deger degil
           o.DefaultVoiceId = "…";
           o.MaxCharactersPerRequest = 5000;
       });
```

Kaydedilen tool'lar:

| Tool | Ne yapar | Onay |
|------|----------|------|
| `speak` | Metni sese çevirir, `attachments`'a yazar, kimlik döner | **Gerekli değil** (varsayılan), ayarla açılabilir |
| `transcribe` | Bir ek kimliğinin sesini metne çevirir | Gerekli değil |
| `list_voices` | Kullanılabilir sesleri listeler | Gerekli değil |

Neden onay gerekmiyor: tool geri alınamaz bir dış etki yaratmaz — dosya üretir
ve ücret harcar. Ücret bir gerekçe olabilir; bu yüzden
`VoiceOptions.RequireApproval` ile açılabilir ve varsayılan **kapalıdır**.

### Ses çıktısı nereye yazılır — beyin fırtınasının açık sorusu

**Cevap: `attachments` tablosuna** (Faz 14). Yeni bir depo açılmaz.

- `media_type` = `audio/mpeg` — Faz 14'ün beyaz listesinde zaten var
- `session_id` ve `run_id` doldurulur; oturum silinince ses de silinir
- Tool sonucu modele **ekin kimliğini** döndürür, ham sesi değil.
  Ham ses tool sonucuna konursa bağlam penceresi base64 ile dolar
- Arayüz transcript'te `<audio>` etiketiyle çalar — kütüphane gerekmez

---

## 28.3 — Maliyet ve Sınırlar

Ses ücretlendirmesi karakter (TTS) veya süre (STT) bazlıdır; token değil.
Faz 20'nin maliyet modeli **token varsayar**.

Bu fazda yapılacak:

- `SpeechAudio.CharactersBilled` ve transkript süresi `tool_invocations`
  kaydına yazılır
- Fiyat yine **yapılandırmadan** gelir (K-032):
  `AgentPrism:Pricing:Voice:elevenlabs:PerMillionCharacters`
- Toplam maliyet raporunda ses ayrı bir kalem olarak görünür — token maliyetiyle
  **toplanmaz**, ayrı gösterilir. İki farklı birim toplanamaz

Sınırlar: istek başına karakter sınırı (varsayılan 5.000), ses dosyası boyut
sınırı (Faz 14'ün 20 MB'ı), eşzamanlı istek sınırı (varsayılan 2).

---

## 28.4 — Arayüz

- Playground'da her asistan mesajının yanında **"seslendir"** düğmesi
  (tool'u elle tetikler)
- Üretilen ses transcript'te satır içi çalar
- Agent düzenleyicisinde ses tool'ları seçilebilir; varsayılan ses seçimi
- Settings'te ses sağlayıcısı durumu ve kalan kota (sağlayıcı bildiriyorsa)

Bütçe hedefi: **+4 KB gzip'ten az**. Ses çalma `<audio>` ile; oynatıcı
kütüphanesi **alınmaz**.

---

## Testler

| Proje | Yeni test |
|-------|-----------|
| `AgentPrism.Voice.UnitTests` (**yeni**) | Sözleşme eşlemesi; karakter sınırı; sahte `HttpMessageHandler` ile istek biçimi; API anahtarının **loglanmaması** ve `ToString`'de görünmemesi (K-035) |
| `AgentPrism.PostgreSql.IntegrationTests` | Ses ekinin `attachments`'a yazılması, `audio/mpeg` türü |
| `AgentPrism.AspNetCore.FunctionalTests` | Tool'un uçtan uca çalışması (sahte sağlayıcı ile); ek indirme başlıkları |
| `AgentPrism.Ui.E2ETests` | Seslendir düğmesi ve `<audio>` öğesinin görünmesi |

**Gerçek sağlayıcı kanıtı:** gerçek bir API anahtarıyla bir cümle seslendirilir;
üretilen ekin boyutu, süresi ve maliyet kaydı dokümana yazılır.

---

## Bu Fazda Verilecek Kararlar

1. **`ISpeechSynthesizer` / `ISpeechTranscriber` soyutlamaları önce gelir;
   ElevenLabs bir uygulamadır.**
2. **Ham `HttpClient` mi SDK mı** — ölçümle karara bağlanır.
3. **Ses `attachments` tablosunda yaşar**; yeni depo açılmaz.
4. **Tool sonucu ek kimliği döndürür**, ham ses değil.
5. **Ses maliyeti token maliyetiyle toplanmaz** — birimler farklıdır.
6. **API anahtarı yapılandırma anahtarı adıyla verilir** (K-009 / K-059 deseni).

---

## Açık Sorular

1. **STT için hangi sağlayıcı?** ElevenLabs STT sunar; OpenAI `whisper` de bir
   seçenek ve mevcut sağlayıcı üzerinden gider. Öneri: **ikisi de desteklensin**,
   `ISpeechTranscriber` uygulaması seçilebilir olsun.
2. **`speak` tool'u onay istesin mi?** Ücret harcar. Öneri: **hayır**
   (varsayılan), ayarla açılabilir.
3. **Ses otomatik üretilsin mi (her yanıt için)?** Maliyeti sessizce büyütür.
   Öneri: **hayır** — kullanıcı düğmeye basar veya agent tool'u çağırır.
4. **Paket adı `AgentPrism.Voice` mi `AgentPrism.ElevenLabs` mi?** Faz 29 aynı
   paketi genişletecek. Öneri: **`AgentPrism.Voice`**.

---

## Bitiş Ölçütleri (DoD)

- [ ] `AgentPrism.Voice` paketi üretiliyor
- [ ] `speak` tool'u gerçek sağlayıcıyla çalışıyor; ses `attachments`'a yazılıyor
- [ ] Arayüzde ses çalınıyor
- [ ] `transcribe` yüklenen bir ses dosyasını metne çeviriyor
- [ ] Karakter/süre kaydı `tool_invocations`'a yazılıyor
- [ ] API anahtarı hiçbir yerde (log, yanıt, veritabanı) görünmüyor
- [ ] Paket kontrol listesi tamam; sır taraması boş
- [ ] Dört doğrulama kapısı sıfır uyarı

---

## Riskler

| Risk | Önlem |
|------|-------|
| Ses maliyeti fark edilmeden büyür | Karakter sınırı; otomatik seslendirme yok; maliyet ayrı kalem |
| Ses ekleri veritabanını şişirir | Faz 14 boyut sınırı + Faz 25 saklama politikası |
| Sağlayıcı kesintisi | Faz 8'in devre kesici deseni burada da uygulanabilir |
| Topluluk SDK riski | Ham `HttpClient` önerisi |
| Ham ses bağlama girer | Tool sonucu yalnız kimlik döndürür |

---

## Sonraki Faza Devir Notu

- **Faz 29 bu paketi genişletir.** `ISpeechSynthesizer.SynthesizeStreamingAsync`
  bu fazda tanımlanır ama yalnız Faz 29'da gerçekten kullanılır — imzayı şimdi
  doğru koymak, sonra kırıcı değişiklik yapmaktan iyidir.
- Faz 29 düşük gecikme için **akışlı** sentez ve **artımlı** transkripsiyon
  isteyecektir; sözleşme buna hazır olmalıdır.

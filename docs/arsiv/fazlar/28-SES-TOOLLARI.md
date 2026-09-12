# Faz 28 — Ses Tool'ları (ElevenLabs)

> **Durum:** ✅ Tamamlandı (2026-08-05)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-13** (1/2)
> **Önkoşul:** [Faz 14](14-COK-MODLULUK.md) — ses çıktısı `attachments` deposunu kullanır
> **Sonraki:** [Faz 29](29-KONUSMA-KATMANI.md) — gerçek zamanlı konuşma katmanı
> **Paketler:** **`Tracon.Voice` (YENİ)**
> **Migration:** 0015 (Postgres) / 0003 (SqlServer, Sqlite) — planda "yok" deniyordu, **gerekli çıktı**

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/28-SES-TOOLLARI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç ve Sonuç

Kullanıcı cevabı **konuştuğunu duysun** — ama henüz gerçek zamanlı bir konuşma kurmadan. Nihai hedef konuşma katmanıdır (K-065); bu faz sağlayıcı soyutlamasını, kimlik doğrulamayı, ses depolamayı ve maliyeti çözer. Faz 29 yalnız **gerçek zamanlılık** sorununu ele alacaktır.

## Bitiş Ölçütleri (DoD)

Elle doğrulama: `samples/Tracon.Api`, 2026-08-05. Model çağrıları **gerçek
OpenAI**'a gitti; ses sağlayıcısı ElevenLabs'in veri düzlemi sözleşmesini birebir
taklit eden yerel bir uçtu (Faz 27'nin deseni).

| Ölçüt | Durum | Kanıt |
|---|---|---|
| `Tracon.Voice` paketi üretiliyor | ✅ | **15 paket**; 1 doğrudan bağımlılık, **0 NuGet** |
| `speak` gerçek bir çalıştırmada çalışıyor | ✅ | aşağıdaki çıktı, 3 |
| Ses `attachments`'a yazılıyor, `session_id` **dolu** | ✅ | çıktı 4 — G1 |
| `transcribe` ses ekini metne çeviriyor | ✅ | çıktı 5 |
| Ölçüm `tool_invocations`'a yazılıyor | ✅ | çıktı 3 ve 5 — `usage` alanı |
| Ses maliyeti token maliyetiyle toplanmıyor | ✅ | ayrı sütunlar, ayrı birim |
| Arayüzde ses çalınıyor (token açıkken de) | ✅ | E2E `…ses_ogesi_calar`, `blob:` |
| Bundle bütçesi | ✅ | +0,6 KB gzip |
| Paket AOT uyumlu, sıfır uyarı | ✅ | `IsAotCompatible=true` altında derleniyor |
| API anahtarı hiçbir yerde görünmüyor | ✅ | 7 API çıktısı + günlük → **0 kez** |
| Paket kontrol listesi tamam | ✅ | README + slnx + `DependencyDirectionTests` |
| Dört doğrulama kapısı sıfır uyarı | ✅ | build / test / pack / format → 0 uyarı |

> ⚠️ **Gerçek ElevenLabs aboneliğiyle doğrulama yapılmadı.** Sözleşme (yol,
> `xi-api-key`, `output_format`, multipart STT, `/v2/voices`) taklit uçla uçtan
> uca doğrulandı. Gerçek hesapta kalan **iki** belirsizlik:
> (1) faturalanan karakteri hangi yanıt başlığının taşıdığı — bulunamazsa ölçüm
> `Estimated` olur ve maliyet yine hesaplanır;
> (2) gerçek MP3 çıktısının ilk baytları — maske tabanlı tanıma altı geçerli
> başlığı kapsıyor, ama ölçülmedi.
>
> ⚠️ **SQL Server sözleşme testleri bu makinede koşmadı.** `mssql/server`
> konteyneri Apple Silicon üzerinde başlamıyor — Faz 23'ten beri bilinen ortam
> sınırı, bu fazın değişikliğiyle ilgisi yoktur. Migration `0003_tool_usage.sql`
> yazıldı ama **çalıştırılmadı**.
>
> ⚠️ E2E paketi tam çözüm koşusunda bir kez 1 test düşürdü; yalıtılmış koşuda
> iki kez 30/30 geçti. Yük altında zamanlama kaynaklı görünüyor.

### Gerçek çıktı (2026-08-05)

```
# 1) Ses saglayicisi sagligi — ucret uretmez
$ curl .../api/voice/health
  {"providerName":"elevenlabs","isHealthy":true,"latency":"00:00:00.030","voiceCount":2}

# 2) Tool defteri
$ curl .../api/tools
  list_voices  onay=False  sema=var
  speak        onay=False  sema=var
  transcribe   onay=False  sema=var

# 3) GERCEK CALISTIRMA — OpenAI modeli speak tool'unu cagirdi
  toolName   : speak
  arguments  : text=Tracon ses testi tamamlandi.
  result     : Ses uretildi. attachmentId=019fd236-a208-…, tur=audio/mpeg, boyut=2062 bayt
  duration   : 00:00:00.0309
  usage      : {unit: characters, quantity: 37, cost: 0.00407, currency: USD, isEstimated: false}
               # 110.0 * 37 / 1e6 = 0.00407  ← yapilandirmadan gelen fiyat

# 4) Ek kaydi — G1
  tur=audio/mpeg  boyut=2062
  oturum=conv_019fd2369a237fc28c5e0b2cf16be7de     # <- DOLU
  calistirma=019fd236-9a43-7be1-a70b-5c7cc489cf8e
  yazan=sesli-asistan

# 5) transcribe
  result : [dil=tr] merhaba bu bir deneme kaydidir
  usage  : {unit: seconds, quantity: 4.25, cost: 0.000425, currency: USD, isEstimated: false}

# 6) Saglayici ucuna ULASAN istekler
  POST /v1/text-to-speech/ses-tr-1?output_format=mp3_44100_128
       xi-api-key=SAHTE-SES-ANAHTARI-xyz789   Authorization=None   content-type=application/json
       govde={"text":"Tracon ses testi tamamlandi.","model_id":"eleven_multilingual_v2"}
  GET  /v2/voices
       xi-api-key=SAHTE-SES-ANAHTARI-xyz789

# 7) Sir taramasi
  /api/voice/health · /api/voice/voices · /api/meta · /api/tools ·
  /api/agents · /api/runs · /api/attachments     -> anahtar 0 kez
  uygulama gunlugu                               -> anahtar 0 kez
```

---

## Kullanım

```csharp
builder.AddTracon()
       .UseVoice(configuration.GetSection(VoiceOptions.SectionName))
       .AddAgent(new AgentDefinition
       {
           Name = "sesli-asistan",
           Instructions = "Kullanici isterse cevabini seslendir.",
           Model = new ModelBinding { Provider = "openai", Model = "gpt-5.4-mini" },
           ToolNames = ["speak", "transcribe", "list_voices"],
       });
```

```bash
dotnet user-secrets set "Tracon:Voice:ApiKey" "..."
```

---

## Bu Fazda Verilen Kararlar

Karar defterine yazıldı (`docs/KARARLAR.md`, **K-215 – K-221**):

1. **K-215** — Ses sözleşmeleri `Tracon.Abstractions`'ta; ElevenLabs bir uygulamadır.
2. **K-216** — Ham `HttpClient`; `Tracon.Voice` hiçbir NuGet paketi almaz.
3. **K-217** — `AgentRunScope.SessionId` eklendi; oturumsuz ek saklama tarafından silinir.
4. **K-218** — 🚨 Tool bağımlılıkları **kurulum anında** alınır; `AIFunctionArguments.Services` MAF boru hattında boştur.
5. **K-219** — `tool_invocations` beş ölçüm sütunu taşır; ses maliyeti token maliyetiyle toplanmaz.
6. **K-220** — `POST /api/voice/speak` operatör eylemidir ve `tool_invocations`'a yazmaz.
7. **K-221** — Ses API anahtarı düz `ApiKey`'dir; K-059 yalnız veritabanı içindir.

---

## Sonraki Faza Devir Notu

- 🚨 **`AIFunctionArguments.Services` bu depoda kullanılamaz** (K-218). Yeni bir
  tool yazarken bağımlılığı kurulum anında alın. Aynı sebeple
  **`AddToolsFrom` ile kaydedilen ÖRNEK METOT tool'ları da çalışmaz** —
  `ToolMethodScanner.CreateFunction` taşıyıcıyı `arguments.Services`'ten çözer ve
  `EmptyServiceProvider` alır. Depoda hiç örnek-metot tool'u yok, bu yüzden hiç
  görülmedi. Düzeltmek ayrı bir iştir: ya tarayıcıya bir `IServiceProvider`
  verilmeli ya da hata mesajı bu durumu açıkça söylemelidir.
- **`SynthesizeStreamingAsync` uygulandı ama üretimde kullanılmıyor.** Faz 29
  gerçek zamanlı yolda kullanacaktır. 🚨 Akan ses **eke yazılamaz**: PCM/µ-law
  başlıksızdır ve parçalar tek başına geçerli dosya değildir.
- **Artımlı transkripsiyon ayrı bir arayüz olmalıdır**
  (`IStreamingSpeechTranscriber`). `ISpeechTranscriber`'a üye eklemek tüketici
  uygulamalarını kırar.
- **STT yanıtındaki `words[]` okunmuyor.** Kelime zamanlaması Faz 29'un ihtiyacı.
- **Ses kotası Faz 21'in mekanizmasına bağlanmadı.** `QuotaEnforcer` token ve
  çalıştırma sayar; ses dakikası bir birim değildir. Faz 29'da ses sürekli akar
  ve `MaxCharactersPerRequest` orada koruma sağlamaz — soru orada açılmalıdır.
- **Operatör yolunun ölçümü kalıcı değildir** (K-220). Kalıcılık isteniyorsa
  `tool_invocations.run_id`'nin nullable yapılması veya operatör eylemleri için
  ayrı bir tablo gerekir; ikisi de bu fazın kapsamı dışındaydı.
- **`DependencyDirectionTests.AllowedReferences` hâlâ `Tracon.SqlServer`,
  `Tracon.Sqlite` ve `Tracon.Sql.Shared` paketlerini içermiyor**
  (Faz 23/24'ten kalan boşluk; Faz 26 ve 27'de de açıktı). Bu fazda
  `Tracon.Voice` eklendi.

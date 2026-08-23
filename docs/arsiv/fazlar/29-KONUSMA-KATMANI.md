# Faz 29 — Konuşma Katmanı (Gerçek Zamanlı Ses)

> **Durum:** ✅ Tamamlandı (2026-08-05)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-13** (2/2) · Kullanıcı kararı **K-065**
> **Önkoşul:** [Faz 28](28-SES-TOOLLARI.md) — sağlayıcı soyutlamaları oradan gelir
> **Sonraki:** [Faz 30](30-ARAYUZ-CILASI.md) — arayüz cilası ve i18n
> **Paketler:** `.Abstractions`, `.Core`, `.AspNetCore`, `.Sql.Shared` + üç SQL sağlayıcısı, `.UI`
> **Migration:** 0016 (Postgres) / 0004 (SqlServer, Sqlite)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/29-KONUSMA-KATMANI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç ve Sonuç

Kullanıcı **konuşsun ve konuşulan cevabı duysun** — ama AgentPrism'in kontrol düzlemi vaatleri (çalıştırma kaydı, span, maliyet, onay, kiracı, kota) askıya alınmadan. **Sonuç:** Tarayıcıdan konuşulup sesli yanıt alınıyor. Her konuşma turu **normal bir `runs` satırı** üretiyor; token sayımı, span'ler ve tool onayı aynen çalışıyor.

## ⚠️ Bu Faz Barındırma Modelini Değiştirir

Bugüne kadar AgentPrism **istek/yanıt** çalıştı: HTTP gelir, SSE ile akar, biter.
Gerçek zamanlı ses bunu değiştirir:

| Konu | Faz 28'e kadar | Bu fazdan sonra |
|------|-------|-----------------|
| Bağlantı | Kısa ömürlü HTTP | Dakikalarca açık WebSocket |
| Durum | İstek başına | Bağlantı boyunca sunucuda |
| Ölçekleme | Herhangi bir örnek | Bağlantı **bir örneğe bağlı** (yapışkan oturum) |
| Ters vekil | Standart | WebSocket geçişi ve zaman aşımı ayarı gerekir |

Bu yüzden yetenek **isteğe bağlıdır**: `UseVoiceConversation()` çağrılmazsa
`VoiceConversationDriver` kaydedilmez, WebSocket ucu **hiç bağlanmaz** ve
`UseWebSockets()` ara yazılımı da kurulmaz.

> 🚨 Uç, "var ama kapalı" anlamına gelen **501 dönmez**; adres gerçekten
> yoktur ve istek **404** alır. Faz 28'in `/api/voice/*` uçları 501 döner çünkü
> onlar her zaman bağlanır — buradaki uç bağlanmaz. Ayrımı test koruyor:
> `UseVoiceConversation_cagrilmadiysa_HICBIR_uc_acilmaz`.

---

## Bitiş Ölçütleri (DoD)

Elle doğrulama: `samples/AgentPrism.Api`, 2026-08-05. Model çağrıları **gerçek
OpenAI**'a gitti; ses sağlayıcısı ElevenLabs'in veri düzlemi sözleşmesini birebir
taklit eden yerel bir uçtu (Faz 27/28'in deseni).

| Ölçüt | Durum | Kanıt |
|---|---|---|
| Tarayıcıdan konuşulup sesli yanıt alınıyor | ✅ | E2E `…konusma_modu_mikrofonu_acar…` + aşağıdaki çıktı 1 |
| Her tur normal bir `runs` satırı üretiyor; span ve maliyet görünüyor | ✅ | çıktı 2 — `Completed`, 413 token |
| Kesinti çalışıyor; kesilen yanıt geçmişe yazılıyor | ✅ | çıktı 3 — `Canceled` + `[Yanit … kesildi.]` |
| Kimlik doğrulama katmanları WebSocket'te de geçerli | ✅ | `Token_alt_protokolde_kabul_edilir`, `Token_SORGU_DIZESINDE_kabul_EDILMEZ` |
| Bağlantı ve süre sınırları uygulanıyor | ✅ | `Es_zamanli_baglanti_siniri_uygulanir`; süre sınırı `end_reason` ile kayda yazılır |
| Ses varsayılan saklanmıyor; açıldığında `attachments`'a yazılıyor | ✅ | `Ses_varsayilan_olarak_SAKLANMAZ`, `PersistAudio_acikken_…` |
| Gecikme ölçüldü ve dokümana yazıldı | ✅ | çıktı 1 |
| `UseVoiceConversation()` çağrılmadığında hiçbir uç açılmıyor | ✅ | `…HICBIR_uc_acilmaz` → **404** |
| Bundle ölçüldü; dört doğrulama kapısı sıfır uyarı | ✅ | +2,4 KB gzip; build/test/pack/format → 0 uyarı, 15 paket |

> ⚠️ **Gerçek ElevenLabs aboneliğiyle doğrulama yapılmadı.** Faz 28'in iki
> belirsizliği açık kalmaya devam ediyor.
>
> ⚠️ **Ölçülen gecikme sağlayıcı gecikmesini İÇERMEZ.** Çözüm ve sentez yerel bir
> taklit uca gitti (~0 ms); ölçülen süre gerçek modelin süresidir. Gerçek bir
> sağlayıcıda çözüm ve sentez için birkaç yüz milisaniye daha eklenir.
>
> ⚠️ **SQL Server sözleşme testleri bu makinede koşmadı.** `mssql/server`
> konteyneri Apple Silicon üzerinde hazır olmuyor (`TimeoutException`) — Faz
> 23'ten beri bilinen ortam sınırı. Migration `0004_voice_sessions.sql` yazıldı
> ama **çalıştırılmadı**.

### Gerçek çıktı (2026-08-05)

```
# 1) UCTAN UCA BIR TUR — gercek OpenAI modeli
baglandi: ws://127.0.0.1:5080/agentprism/api/voice/sessions/conv_76e1…/stream
kabul edilen alt protokol: agentprism.voice.v1
ready         agent=sesli-asistan persistAudio=False
transcript    "siparisim nerede" (3 ms)
runStarted    019fd302-3a65-76bb-9537-62b27d328f8b (10 ms)
audioStart    audio/mpeg (1008 ms)
done          tur=1 kesildi=False (1034 ms)
yanit         : Sipariş durumunu kontrol edebilmem için sipariş numaranı yaz.
ses           : 414 bayt

GECIKME (commit anindan itibaren, saglayici gecikmesi HARIC)
  transkript          :       3 ms
  calistirma basladi  :      10 ms
  ilk metin (altyazi) :     934 ms
  ILK SES             :    1008 ms
  tur bitti           :    1034 ms

# 2) CALISTIRMA KAYDI — ses calistirma yolunu DEGISTIRMEDI
  status      : Completed        isStreaming : true
  agentName   : sesli-asistan    modelId     : gpt-5.4-mini
  sessionId   : conv_defd2758…   eventCount  : 18
  usage       : {input: 394, output: 19, total: 413}

# 3) KESINTI (barge-in)
  cancel        gonderildi (893 ms)
  done          tur=1 kesildi=True (910 ms)
  calistirma    status = Canceled
  oturum gecmisi:
    role=assistant  text="Sipariş\n\n[Yanit kullanici tarafindan kesildi.]"

# 4) KONUSMA KAYDI — ses ICERMEZ
  turns=1  inputSeconds=1.6  outputChars=61  endReason=Client
  # 1.6 = SAGLAYICININ bildirdigi sure. Saglayici bildirmediginde ham PCM'de
  # bayt sayisindan hesaplanan 1.0 kullanildi (olculdu, iki kosu).

# 5) SAGLAYICI UCUNA ULASAN istekler
  POST /v1/speech-to-text                       xi-api-key=…  multipart=32.379 bayt
  POST /v1/text-to-speech/ses-tr-1/stream?output_format=mp3_44100_128
       govde={"text":"Sipariş durumunu kontrol edebilmem için sipariş numaranı yaz.",
              "model_id":"eleven_multilingual_v2"}

# 6) SIR TARAMASI
  8 API ucu + uygulama gunlugu  -> anahtar 0 kez
```

---

## Kullanım

```csharp
builder.AddAgentPrism()
       .UseVoice(configuration.GetSection(VoiceOptions.SectionName))

       // ⚠️ BARINDIRMA MODELINI DEGISTIRIR: WebSocket dakikalarca acik kalir ve
       // BIR sunucu ornegine baglanir. Cagrilmazsa hicbir WebSocket ucu acilmaz.
       .UseVoiceConversation(configuration.GetSection(VoiceConversationOptions.SectionName));
```

```
GET {prefix}/api/voice/sessions/{sessionId}/stream
    Sec-WebSocket-Protocol: agentprism.voice.v1, agentprism.token.<token>
```

Ters vekil arkasında: WebSocket geçişine izin verin ve boşta zaman aşımını
`MaxConnectionDuration` üzerinde tutun. Çok örnekli dağıtımda **yapışkan oturum**
gerekir.

---

## Bu Fazda Verilen Kararlar

Karar defterine yazıldı (`docs/KARARLAR.md`, **K-222 – K-227**):

1. **K-222** — Seçenek A (boru hattı) + konuşma katmanı `Core`'da, `Voice`'ta değil.
2. **K-223** — `MapAgentPrism` `UseWebSockets()`'i koşullu olarak kendisi kurar.
3. **K-224** — WebSocket token'ı **alt protokolde** taşınır; sorgu dizesinde kabul edilmez.
4. **K-225** — `PersistAudio` yalnız **agent'ın ürettiği sesi** saklar.
5. **K-226** — Artımlı transkript yok; tek atımlı çözüm (kullanıcı kararı).
6. **K-227** — Ses dakikası bir kota birimi değildir (kullanıcı kararı).

---

## Sonraki Faza Devir Notu

- 🚨 **`UseWebSockets()` artık `MapAgentPrism` içinde koşullu olarak çağrılıyor**
  (K-223). WebSocket kullanan başka bir yetenek eklenirse aynı koşula bağlanmalı;
  ara yazılımı koşulsuz kurmak, yeteneği kullanmayan tüketicinin boru hattına
  dokunmak olur.
- 🚨 **Konuşma modunun metinleri Faz 30'da çevrilmelidir** ve ses seçimi dile
  bağlıdır: Türkçe ses ≠ İngilizce ses. `VoiceConversationOptions.VoiceId` tek
  bir sestir; dil başına ses eşlemesi Faz 30'un işidir.
- **`ISpeechTranscriber` hâlâ tek atımlıdır.** Artımlı transkript isteniyorsa
  ayrı bir arayüz (`IStreamingSpeechTranscriber`) gerekir; mevcut arayüze üye
  eklemek tüketici uygulamalarını kırar (K-226).
- **`AttachmentTypeGuard` EBML (WebM) imzasını tanımaz.** Kullanıcının sesini
  saklamak isteyen bir gelecek faz önce bunu eklemeli ve `audio/*` beyaz
  listesinin video WebM'i de kabul edeceğini kabul etmelidir (S3).
- **`VoiceConnectionLimiter` süreç içidir.** Çok örnekli dağıtımda toplam sınır
  örnek sayısıyla çarpılır — hız sınırıyla (K-158) aynı ödünleşme.
- **`voice_sessions` saklama hedefine eklendi** ama örnek uygulamada bir politika
  tanımlı değil; Faz 25'in varsayılan yapılandırması bu hedefi kapsamıyor.
- **`DependencyDirectionTests.AllowedReferences` hâlâ `AgentPrism.SqlServer`,
  `AgentPrism.Sqlite` ve `AgentPrism.Sql.Shared` paketlerini içermiyor**
  (Faz 23/24'ten kalan boşluk; Faz 26, 27 ve 28'de de açıktı).

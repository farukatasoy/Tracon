# Ses ve Konusma Tuzaklari

> Ses tool'lari (`AgentPrism.Voice`), gercek zamanli konusma katmani (`Core/Voice/`),
> token disi olcum.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.
>
> `docs/hafiza/kod-haritasi.md`'den tasindi (2026-08-15, Faz 58.0): sicak yol
> butcesi. Icerik degismedi.

## Nerede yasiyor

- **Ses `AgentPrism.Voice/` altinda, olcum Core'da** (2026-08-05, Faz 28):
  sozlesmeler `Abstractions/Voice/` (HTTP katmani gorsun diye, K-215);
  `Internal/ElevenLabsSpeechClient.cs` uc HTTP ucunu ham `HttpClient` ile
  konusur ve eszamanlilik sayacini tutar; `Tools/VoiceToolBase.cs` 🚨
  bagimliligi KURULUM aninda alir (K-218). Token disi olcum:
  `Core/Tools/AgentPrismToolUsage.cs` (public bildirim) →
  `Core/Recording/ToolUsageAccumulator.cs` (cagri kimligine gore) →
  `ToolInvocationTracker.OnResult`. Uclar
  `AspNetCore/Endpoints/VoiceEndpoints.cs`.
- **Konusma katmani `Core/Voice/` altinda, `AgentPrism.Voice`'ta DEGIL**
  (2026-08-05, Faz 29): boru hatti yalniz `ISpeechTranscriber`/
  `ISpeechSynthesizer` kullanir, saglayiciya dokunmaz (K-222).
  `VoiceConversationDriver` (alma dongusu + tur gorevi + gonderme kilidi),
  `VoiceConversationStateMachine` (saf), `VoiceUtteranceBuffer` (🚨 PCM'e 44
  baytlik WAV basligi yazar), `VoiceSpeechSegmenter` (cumle bolucu),
  `VoiceConnectionLimiter` (kiraci CAS sayaci). Uc
  `AspNetCore/Voice/VoiceConversationEndpoint.cs`; sozlesmeler
  `Abstractions/Voice/ConversationContracts.cs`. Arayuz:
  `UI/frontend/src/lib/voice.ts` + `components/voice-panel.tsx`.

## Tuzaklar

- **🚨 Konusma WebSocket'i token'i `Sec-WebSocket-Protocol` alt protokolunde
  alir** (`agentprism.token.<token>`) ve sabit zamanda kendisi dogrular (K-224).
  Tarayici bir el sikismaya `Authorization` basligi ekleyemez; sorgu dizesi
  sunucu ve ters vekil gunluklerine yazilacagi icin **kabul edilmez**.
- **🚨 Oturum deposu davranisini degistirmek ses ucunu sessizce bozdu** (K-283,
  Faz 41): ses ucunun "baskasinin oturumu" reddi bellek ici oturum deposunun
  kiraci-agnostik davranisina dayaniyordu; depo kiraciyla sinirlaninca red
  etkisiz kaldi. Birim degil **fonksiyonel** testler yakaladi.
- **ElevenLabs'ın zaman damgalı uçları KARAKTER bazlıdır, kelime bazlı DEĞİL**
  (2026-08-19, Faz 72, K-501): `POST /v1/text-to-speech/{voiceId}/with-timestamps`
  ve `.../stream/with-timestamps` — ikisi de doğrulandı
  (`api.elevenlabs.io/openapi.json`, `AudioWithTimestampsResponseModel`/
  `StreamingAudioChunkWithTimestampsResponseModel`). Yanıt `audio_base64` +
  `alignment`/`normalized_alignment`; her ikisi de `CharacterAlignmentResponseModel`
  — `characters`/`character_start_times_seconds`/`character_end_times_seconds`
  PARALEL dizileri. `alignment` (orijinal metin) kullanılır, `normalized_alignment`
  (sağlayıcının normalize ettiği metin — sayı/kısaltma açılımı) DEĞİL: ikincisi
  çağıranın gönderdiği ham metinle hizalanmaz. Yeni bir sağlayıcı entegre
  edilirken "word-level" varsayılmaz — her sağlayıcının kendi granülerliği
  ölçülür.
- **🚨 `ISpeechSynthesizer.SynthesizeStreamingAsync` hizalama TAŞIYAMAZ —
  `IncludeTimestamps` bu yolda istisna fırlatır, sessizce yok saymaz**
  (2026-08-19, Faz 72, K-502): dönüş tipi `IAsyncEnumerable<ReadOnlyMemory<byte>>`,
  yalnız ham ses baytı. ElevenLabs'ın akışlı zaman damgalı ucu (`/stream/with-timestamps`)
  AYRI bir JSON-parça protokolü konuşur (her parça `audio_base64` + `alignment`
  taşıyan bir JSON nesnesi) — bugünkü arayüz bunu hiç modelleyemez. Akışta
  hizalama gerçek bir ihtiyaç olursa `ISpeechSynthesizer`'a yeni bir üye
  eklemek gerekir (mevcut üye kırılmadan); mevcut `SynthesizeStreamingAsync`'i
  "bazen JSON bazen ham bayt" döndürecek şekilde değiştirmek K1'i ihlal eder.
- **🚨 Sunucu durumu degistirip SESSIZCE donerse duplex istemci asilir**
  (2026-09-03, F-180 kapanisi, K-660): `commit` ses gelmeden ulastiginda
  `VoiceConversationDriver.Commit` `FinishTurn(counted:false)` cagirip
  **hicbir cerceve gondermeden** donuyordu. Istemci ise gonder'e basildigi anda
  kendini `'thinking'`e alip kaydediciyi durduruyor — yani panel kalici olarak
  asiliyor ve mikrofon bir daha acilmiyordu. Ders: **istemcinin kendini bekleme
  durumuna soktugu her istekte sunucunun bir cikis cercevesi borcu vardir.**
  Sunucunun kendi state machine'inin dogru olmasi yetmez.
  - Sinif taramasi ayni kusurun **ikinci** vakasini buldu ve o uretimde daha
    olasidir: transcriber bos metin dondugunde (`ProcessTurnAsync`, gurultulu
    oda) istemci bos `transcript` cercevesini yok sayar ve `return` sondaki
    `done`'i atlar. Ikisi de `idle` cercevesiyle kapatildi.
  - **Mevcut test bunu goremiyordu** (`Commit_without_audio_does_NOT_produce_a_turn`):
    yalnizca SUNUCUNUN dinlemeye dondugunu olcuyordu (sonraki tur calisiyor mu).
    Duplex bir protokolde "sunucu durumu dogru" ile "istemci kurtulabilir" AYRI
    iddialardir; ikincisi cerceveyi beklemeden olculemez.
  - `idle` bilerek `done`'dan ayri bir cercevedir: `done` var olan bir turu
    kapatir ve o turun numarasini/`cancelled` bayragini tasir — istemci ikisini
    ayni sayarsa ONCEKI turun kaydini yeniden yazar.
  - 🚨 Yalnizca tam paket kosumunda dusuyordu (izole 57/57 yesil), bu yuzden
    "yalitim cakismasi" sanildi. Yuk yalnizca pencereyi genisletiyordu; gercek
    sebep koddaydi. **Tek basina gecip pakette dusen test otomatik olarak
    kirilgan degildir** — kod yolu okunmadan siniflandirilmaz.
- **🚨 ElevenLabs'in `/v2/voices` ucu `page_size` verilmezse VARSAYILAN 10 ses
  doner** (2026-09-03, Faz 138, K-669'un yaninda olculdu, olcum kaynagi:
  `api.elevenlabs.io/openapi.json` — `GET /v2/voices` parametre listesi):
  `ElevenLabsSpeechClient.ListVoicesAsync` sorgu dizesi hic eklemeden
  `BuildUri("v2/voices")` cagiriyordu; kod `MaxReportedVoices = 500` sinirini
  varsayiyordu ama gercekte saglayici HER ZAMAN yalniz ilk 10'u donuyordu —
  premade katalog bile bunu asar. Cozum `page_size=100` + `has_more`/
  `next_page_token` takibi (`ReadVoicesPageAsync` + `MapVoices` ayrimi).
  **`VoiceResponseModel`'in `labels` sozlugu `language` anahtari TASIMAZ** —
  dil ayri, coklu-model `verified_languages` dizisinde durur
  (`VerifiedVoiceLanguageResponseModel.language`, zorunlu alan). Yeni bir
  saglayici entegre edilirken saglayicinin GERCEK OpenAPI/semasi
  (varsa) once cekilip grep'lenmeli — dokumantasyon prosasi ("filtering,
  based on the voice's 'language' label" gibi) semanin kendisiyle CELISEBILIR.
- **🚨 Ses ucuna bir yetkilendirme kapısı eklerken K-283 KORUNMALIDIR** (Faz 147,
  K-687): var olmayan bir oturum reddedilmez — handler yine de sorulur ama
  varsayılan cevap soketi AÇAR. Kapıyı "oturum yoksa reddet" olarak yazmak her
  kurulumdaki İLK konuşmayı sessizce bozar ve başka hiçbir test bunu görmez;
  `VoiceAuthorizationTests.An_unknown_session_still_opens_when_the_handler_allows_it`
  bunu kilitler. Ret `404`'tür (`401` değil — kimlik doğrulaması başarılıydı;
  `403` değil — oturumun varlığını doğrulardı) ve gövdeyi
  `WriteSessionNotFoundAsync` tek yerden yazar: `CheckSessionAsync`'in kendi
  `404` gövdesi bilerek ATILIR, çünkü metni bu ucunkinden farklıdır.
- **TestServer'da reddedilen bir WebSocket el sıkışmasının GÖVDESİ okunamaz**
  (Faz 147): `WebSocketClient.ConnectAsync` `InvalidOperationException` atar ve
  yanıt gövdesi kaybolur. Düz bir `GET` ile gövdeyi okumaya çalışmak da işe
  yaramaz — `IsWebSocketRequest` kontrolü kapıdan ÖNCE çalışır ve `400` döner.
  Test edilebilir olan şey istisnanın mesajındaki durum kodudur
  (`error.Message.ShouldContain("404")`); gövde birebirliği ise yapısal olarak,
  tek bir yardımcıyı paylaşarak sağlanır.


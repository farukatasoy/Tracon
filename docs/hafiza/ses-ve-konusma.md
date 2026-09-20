# Ses ve Konusma Tuzaklari

> Ses tool'lari (`Tracon.Voice`), gercek zamanli konusma katmani (`Core/Voice/`),
> token disi olcum.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.
>
> `docs/hafiza/kod-haritasi.md`'den tasindi (2026-08-15, Faz 58.0): sicak yol
> butcesi. Icerik degismedi.

## Nerede yasiyor

- **Ses `Tracon.Voice/` altinda, olcum Core'da** (2026-08-05, Faz 28):
  sozlesmeler `Abstractions/Voice/` (HTTP katmani gorsun diye, K-215);
  `Internal/ElevenLabsSpeechClient.cs` uc HTTP ucunu ham `HttpClient` ile
  konusur ve eszamanlilik sayacini tutar; `Tools/VoiceToolBase.cs` 🚨
  bagimliligi KURULUM aninda alir (K-218). Token disi olcum:
  `Core/Tools/TraconToolUsage.cs` (public bildirim) →
  `Core/Recording/ToolUsageAccumulator.cs` (cagri kimligine gore) →
  `ToolInvocationTracker.OnResult`. Uclar
  `AspNetCore/Endpoints/VoiceEndpoints.cs`.
- **Konusma katmani `Core/Voice/` altinda, `Tracon.Voice`'ta DEGIL**
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
  alir** (`tracon.token.<token>`) ve sabit zamanda kendisi dogrular (K-224).
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


## Saglayici barindirmali canli ses (Faz 161)

- **Canli katman `Core/Voice/` altinda, Secenek A'dan TAMAMEN ayri** (2026-09-11,
  Faz 161): `LiveVoiceSessionHost` (sideband pump + delegation sozlugu + kapanis
  sirasi), `LiveVoiceSessionLauncher` (limit → agent coz → yarat → attach → kayit
  sirasinin TEK yeri), `LiveVoiceSessionRegistry` (kimlik → host, TTL supurgesi),
  `LiveVoiceDelegationRunner` (delegation → `run`), `LiveTranscriptLedger` (saf,
  agsiz), `LiveVoiceAppendBudget`, `VoiceDurationPricing`. Saglayici
  `OpenAI/Live/`. Uclar `AspNetCore/Voice/LiveVoiceEndpoints.cs`, ortak kapilar
  `VoiceEndpointGates.cs`. `VoiceConversationDriver`'a DOKUNULMADI — yalniz
  `VoiceHistoryWriter` cikarildi.

### Olculen GPT-Live protokolu (2026-09-11, gercek oturum)

🚨 **Bu satirlar dokumantasyondan degil, gercek bir oturumun ham dokumunden
alindi.** Saglayicinin kendi hata mesaji kabul ettigi olay listesini veriyor —
yeni bir olay eklemeden once bilerek gecersiz bir `type` gonderip listeyi
tazele.

- **Yalniz `webrtc` transport'u var.** `websocket`/`ws` → `400 "Only the webrtc
  transport is supported."` Sunucu tarafli bir medya koprusu **yapilamaz**;
  Tracon SDP araciligi yapar.
- **`/v1/live/client_secrets` → 404.** Ephemeral token ucu YOK ve gerekmiyor:
  oturumu Tracon kendi anahtariyla yaratir.
- **Append alani `content`, `text` DEGIL** ve `delegation_id` **uc kanalda da
  zorunlu** (`session.thinking|commentary|instructions.append`).
- **Transcript'te `is_final` YOK** — yalniz `start_ms`/`end_ms` tasiyan delta.
  🚨 Defter bu yuzden `TimeProvider` KULLANMAZ: zamani saglayici veriyor ve
  delegation kesimi (`offset_ms`) o zamana gore yapiliyor; yerel saat kayar.
- **`session.delegation.created`**: `offset_ms` + `delegation:{id, type, target}`.
  Kimlik `delegation.id`'dedir ve `item_` onekini tasir — `deleg_` DEGIL.
  `offset_ms`, delegation'dan onceki SON transcript segmentinin **baslangicidir**;
  kesim `StartMs <= offsetMs` ile o segmenti dahil eder.
- **`session.usage.updated` VAR** ve `usage.seconds` tasir; `session.closed` de
  ayni alani tasir. Faturalanan sure budur (K-745).
- **Append tavani 500 TOKEN**: `"Context append text must not exceed 500 tokens."`
  Karakter donusumu tek yerde (`OpenAILiveOptions.ConservativeCharactersPerToken`).
- **`session.interrupt` YOK.** Sideband konusmayi kesemez.
- 🚨 **Sideband sesi AYNALIYOR** (`session.input_audio.append` ve
  `session.output_audio.delta`, base64). Faz 161 bu frame'leri yok sayar.

### Tuzaklar

- **🚨 Sentetik mikrofonla test ederken ses dosyasinin SONUNA sessizlik ekle.**
  Chromium `--use-file-for-fake-audio-capture` dosyayi DONGUDE calar; sessizlik
  yoksa kullanici hic susmaz, saglayicinin VAD'i tur sonunu hic gormez ve model
  **hic sira alamaz**. Ilk denemede 117 transcript delta geldi, tek delegation
  gelmedi. 14-20 sn sessizlik yeterli.
- **🚨 Giden WebSocket egress politikasini elde cagirmak zorundadir** (K-751).
  `ClientWebSocket`'in `ConnectCallback`'i yoktur. Ayrica: politika reddi REST
  yolunda `HttpRequestException` **icine sarili** gelir — yalniz
  `TraconException` yakalayan bir uc sirali bir reddi yakalanmamis 500
  olarak kacirir (gercek kosumda bulundu).
- **🚨 Sideband pump'i HTTP isteginin disinda kosar.** Ambient kiraci ve
  attribution pump'in KENDI govdesinde, ilk olay islenmeden once acilir; her
  delegation gorevi onlari devralir. Acilmazsa delegation `run`'lari **varsayilan
  kiraciya** duser ve tek kiracili her test yesil kalir.
- **Giden soket `TestServer`'dan taklit EDILEMEZ.** `FakeGptLiveServer` gercek
  bir `WebApplication`'dir (`http://127.0.0.1:0` + `UseWebSockets()`).
  Loopback'i egress politikasi varsayilan olarak reddeder — testler
  `AllowPrivateNetworkTargets = true` kurar, `LiveVoiceEgressTests` ise reddin
  kendisini olcer.
- **`LiveTranscriptLedger` is parcacigi guvenli DEGIL.** Pump yazarken delegation
  gorevi `Cut` cagirir. `MaxConcurrentDelegations` yukseltilecekse once kilit.
- **🚨 Dis `CloseAsync` `_lifetime`'i pump bitmeden iptal edemez** (2026-09-19,
  Windows CI): sideband'de alinmis ama pump'in henuz ledger'a yazmadigi transcript
  frame'leri vardir. Once delegation'lari durdur, sideband'i kapat, pump'i bekle;
  sonra `FlushHistoryAsync` cagir. Tersi sira session history'yi sessizce eksik
  yazar.
  - **Bu "duzeltme" kendisi de eksikti** (2026-09-20, `ubuntu-latest` CI; 8x
    paralel + CPU yukunde tekrar uretildi, izole 15/15 yesil, yuklu ~4-7/120
    kirmizi): `DisposeSidebandAsync` `_socket.CloseAsync(...)` cagiriyordu.
    Bu, yalniz GONDERMEZ — kendi ICINDE de okur (peer'in close frame'ini
    bekler) — ve pump'in kendi askidaki `ReceiveAsync` cagrisiyla rakip bir
    okuma yaratir. Olculdu: `CloseAsync` istisnasiz donuyordu ama ledger
    flush aninda **0** kayit tasiyordu — kendi ic okumasi transcript'i pump
    hic gormeden tuketmisti. Duzeltme (`OpenAILiveSideband`): (1) `CloseAsync`
    yerine yalniz-gonderen `CloseOutputAsync`; (2) pump'in `while` kosulu
    `_socket.State == Open` OLAMAZ — `CloseOutputAsync` state'i ANINDA
    `CloseSent`'e cevirir ve kosul bunu gorup HENUZ OKUNMAMIS veriyi
    beklemeden cikar; kosul yalniz cancellation olmali, bitise `ReceiveAsync`
    kendisi karar verir; (3) `Dispose` pump'in kendi `finally`'sinde
    tetiklenen bir sinyali sinirli sureyle (10sn) bekler, SONRA soketi kapatir
    — pump henuz hic ZAMANLANMAMIS olsa bile (bir "baslamadiysa bekleme"
    kisayolu boş pencere birakti; kosulsuz bekleme, dongu bitmisse
    maliyetsizdir). Ders: arka plan pump'i okurken kapatan taraf iki yonlu
    kapatma cagirmaz, yalniz gonderir ve dongunun kendi kendine bitmesini
    sinirli surede bekler — `ILiveVoiceSideband`'in XML dokumanina eklendi.

- **🚨 Sentetik bir ses akışı WebRTC'de çalışır, `MediaRecorder`'da ÇALIŞMAZ**
  (2026-09-19, manuel kapanış §5 turları 8-9): canlı ses (`live-test.html`,
  `/api/voice/live/sessions`) parçayı WebRTC'ye doğrudan verir ve
  `MediaStreamAudioDestinationNode` tabanlı sentetik bir akış sorunsuz taşınır
  — sağlayıcı sentezlenmiş konuşmayı doğru transkript etti. Konuşma paneli
  (`UseVoiceConversation`) ise `MediaRecorder` kullanır ve aynı akıştan **hiç
  veri üretmez**: WebSocket'in `send` çağrısı sarıldığında giden çerçeve `2`
  (yalnız `start` + `commit`), ses parçası `0` çıktı. ∴ konuşma panelinin
  içerik isteyen case'leri gerçek mikrofon ister; içerik istemeyenleri
  (rozet, i18n/tema, `idle` çerçevesi) sentetikle ölçülebilir.
- **Canlı ses oturumunu tarayıcısız sürmenin yordamı** (aynı vaka): mikrofon
  `getUserMedia` **oturum açılmadan önce** kendi `MediaStreamAudioDestinationNode`'a
  bağlanır (parça oturum boyunca aynı kalmalı), sonra sorunun sesi sunucunun
  kendi TTS'iyle üretilip (`POST /api/voice/speak` → ek → `decodeAudioData`)
  o düğüme **çalınır**. ⚠️ `replaceTrack` kullanma — oturum `Abandoned` olur;
  parçayı değiştirmek değil **içine çalmak** gerekir.
- **🚨 Canlı ses ucunda token `Authorization` başlığıyla GİTMEZ** (aynı vaka,
  `MT-SEC-162`): WebSocket subprotocol'ü kullanılır —
  `Sec-WebSocket-Protocol: tracon.voice.v1, tracon.token.<token>`
  (`VoiceConversationProtocol.cs:23,34`). Yanlış taşımayla gelen `401`
  yetkilendirme reddi **değildir**; bunu ayırt etmeyen bir ölçüm case'i yanlış
  sebeple "geçti" sanır.
- **Kuyruklu bir onay `sessionId` ister** (aynı vaka): `Prefer: respond-async`
  ile onay isteyen bir run `sessionId` verilmezse `Failed` olur ve kalıcı bir
  `pending_approvals` satırı hiç oluşmaz. Senkron koşumda onay akışın içinde
  taşınır ve tabloya **yazılmaz** — `/api/approvals/pending` boş görünür.

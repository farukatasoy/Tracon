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
- **ElevenLabs zaman damgası KARAKTER bazlıdır** (Faz 72, K-501): `alignment` okunur, `normalized_alignment` değil; tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `ISpeechSynthesizer.SynthesizeStreamingAsync` hizalama TAŞIYAMAZ —
  `IncludeTimestamps` bu yolda istisna fırlatır, sessizce yok saymaz**
  (2026-08-19, Faz 72, K-502): dönüş tipi `IAsyncEnumerable<ReadOnlyMemory<byte>>`,
  yalnız ham ses baytı. ElevenLabs'ın akışlı zaman damgalı ucu (`/stream/with-timestamps`)
  AYRI bir JSON-parça protokolü konuşur (her parça `audio_base64` + `alignment`
  taşıyan bir JSON nesnesi) — bugünkü arayüz bunu hiç modelleyemez. Akışta
  hizalama gerçek bir ihtiyaç olursa `ISpeechSynthesizer`'a yeni bir üye
  eklemek gerekir (mevcut üye kırılmadan); mevcut `SynthesizeStreamingAsync`'i
  "bazen JSON bazen ham bayt" döndürecek şekilde değiştirmek K1'i ihlal eder.
- **🚨 Duplex protokolde istemcinin bekleme durumuna girdiği her istekte sunucunun bir çıkış çerçevesi borcu vardır** (F-180, K-660): ses gelmeden `commit` ve boş transkript `idle` ile kapanır; "sunucu durumu doğru" ile "istemci kurtulabilir" ayrı iddialardır; tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 ElevenLabs `/v2/voices` `page_size` verilmezse 10 ses döner; `labels` `language` taşımaz** (Faz 138, K-669) — sağlayıcının OpenAPI şemasını önce çek; tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
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
- **🚨 `UseVoice` tüketicinin sağlayıcısını yalnız ÇAĞRI ANINDA görür** (F-336): ElevenLabs kuralları yalnız yerleşik istemci bir sözleşmeye hizmet ederken uygulanır (`VoiceProviderBinding`, `TryAdd`'den önce koleksiyon taraması; doğrulayıcıda çözümleme döngü kurar). Fiyat, `speak` limiti ve sağlık bağlı uygulamanın `ProviderName`/limitini okur. Test anahtarsız, gerçek `UseVoice` + `IStartupValidator` ile (`CustomVoiceProviderTests`).


## Saglayici barindirmali canli ses (Faz 161)

- **🚨 Canlı ses de bir sağlayıcı çağrısıdır: kiracı politikası ve kiracı anahtarı
  ONA da uygulanır** (2026-10-11, F-306, K-878). Oturum kiracıya bağlıydı ama
  `OpenAILiveProvider` süreç geneli anahtarı kullanıyor, politikayı hiç okumuyordu
  — model yolundaki K-466/K-874 kuralı ses yoluna hiç taşınmamıştı. Denetim
  `LiveVoiceSessionLauncher`'da, oturum satırı/slot/sağlayıcı çağrısından ÖNCE;
  kural `TenantProviderAccess`'te tek yerde (registry ve bağlama ucu da onu
  çağırır). Sağlayıcıya anahtar İKİ çağrıda gider (`CreateSessionAsync` +
  `AttachSessionAsync`); opt-in etmeyen (`SupportsTenantCredentials` varsayılan
  `false`) sağlayıcı ve uç nokta override'lı bağlama `403` — ikisi de launcher'da, kayıttan önce. 🚨 Politika testi kontrol case'inde
  agent'ın KENDİ model sağlayıcısını (`echo`) da izinliye koymalı: canlı ses
  delegasyonu agent'ı derler ve politika o modeli de yargılar (yoksa `400`).
  TTS/STT yolu Faz 198'de kapandı (K-930, aşağıda).
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
- **🚨 Dış `CloseAsync` `_lifetime`'i pump bitmeden iptal edemez** (2026-09-19; F-257'nin transkript vakasının kök nedeni) — kapanış sırası `CloseCoreAsync`'tedir; tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

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

## Adlı konuşma sağlayıcısı ve kiracı kapısı (Faz 198)

- **🚨 Konuşma çağrısının kiracı kapısı `ISpeechProviderResolver`'dadır** (2026-10-13, K-930). Tool, HTTP ve fonksiyon düğümü her çağrıda çözer; konuşma soketi bağlantı açılırken BİR KEZ çözer ve açık soket sonradan kaydedilen politikayı görmez (F-446). Sağlık kontrolü varsayılan örneği doğrudan çağırır (platform). Egress kayıt adına değil `ProviderName`'e uygulanır.
- **🚨 Sarmalayıcı yalnız kiracının anahtarı varsa döner.** `is ElevenLabsSpeechClient` ya da bir yetenek arayüzü (`is ISpeechDialogueSynthesizer`) denetimi bağlamalı kiracıda düşer; çağıran `ProviderName` okur; diyalog yeteneği `GetDialogueSynthesizerAsync` ile alınır (K-952). Fiyat `audio.ModelId ?? request.ModelId` ile.
- **🚨 Kuyruklu test "run kapsamı önce" sırasını ayırt ETMEZ:** iş işçisi `AmbientTenantScope` açar, iki kaynak aynı kiracıyı verir (mutasyonla ölçüldü). Sırayı `ITenantContext`'ten farklı bir `TraconRunContext` kiracısıyla sına (`Tracon.Voice.UnitTests.SpeechTenantPolicyTests`).
- **Belirteci gözlemlemeyen depo iptalden sonra normal döner:** çözümleyici kontrollerden SONRA da `ThrowIfCancellationRequested` der; yoksa sağlayıcıyı iptal edilmiş çağırana verir (`A_cancelled_resolution_never_hands_out_the_provider`).

## Diyalog sentezi ve sağlayıcı ayarları (Faz 203)

- **🚨 Diyalog yeteneği DI'da DEĞİL, çözümleyicidedir** (K-952): `GetService<ISpeechDialogueSynthesizer>()` `null` döner; kayıt kapıyı atlatırdı. Çözümleyici yeteneği kayıtlı örnekte okur, sarmalayıcıda değil.
- **🚨 ElevenLabs diyalog servisi bilinmeyen `settings` alanını hatasız kabul eder** (`settings.style` → `200`, ölçüldü): sessiz düşen ayarı sağlayıcı yakalamaz, istemci reddeder (K-953). Aynı `seed` farklı bayt döndürdü — bayt karşılaştırmasıyla ayar etkisi ölçülemez. Yalnız etiket taşıyan tur (`[pause]`) `400 input_text_empty`; bu yüzden her turun en az bir segmenti olmalı.
- **🚨 Run kaydı `TraconException` olmayan istisnayı maskeler** (`ArgumentException failed. (ref: …)`): operatörün göreceği ret `TraconException` olmalı (K-955).
- **"Ağ çağrısı yok" iddiası canlı uçta sayılamaz:** örneği `Tracon__Voice__Endpoint=http://127.0.0.1:<port>/` ile istek kaydeden yerel bir sahte uca yönelt (Faz 203 case 2–6).

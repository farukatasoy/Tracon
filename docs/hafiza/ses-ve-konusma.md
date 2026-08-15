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

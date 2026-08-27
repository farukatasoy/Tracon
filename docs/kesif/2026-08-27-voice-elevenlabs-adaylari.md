# Keşif Turu — 2026-08-27 · Voice ve ElevenLabs

> Bu bir koşum kaydıdır, spec değildir. `ADAYLAR.md` kullanıcı onayı beklediği
> için değişmedi. Kod, faz planı ve implementation üretilmedi.

**Tetikleyen:** Voice için resmî ElevenLabs ve codebase kanıtlı feature keşfi.  
**Zemin:** Faz 118 kapalı · sıralanabilir aday yok · en büyük ID F-169.  
**Tarama:** 2026-08-27 · yalnız resmî ElevenLabs kaynakları.

## 1. Ölçülen zemin

| Yüzey | Repo gerçeği |
|---|---|
| Public speech contract | `ISpeechSynthesizer`: batch TTS, raw-byte streaming TTS, voice listesi. `ISpeechTranscriber`: batch STT. `SpeechRequest`: text/voice/model/format/language/timestamp. `SpeechTranscript`: text/language/probability/duration (`SpeechContracts.cs:3-70`, `SpeechModels.cs:3-159`). |
| Adapter | ElevenLabs TTS, HTTP streaming, timestamped TTS, STT ve voice listesi. Ham `HttpClient`, source-generated JSON, dış NuGet yok, AOT açık (`ElevenLabsSpeechClient.cs:75-337`, `ElevenLabsJson.cs:88-103`). |
| MAF sınırı | Voice turn: client audio → batch STT → `AIAgent.RunStreamingAsync` → cümle bazlı TTS. Normal run yolu tool approval, quota, tenant, run, span ve cost contract'larını korur (`VoiceConversationDriver.cs:512-693`). MAF tipleri sarmalanmaz. |
| Güvenlik | Tenant handshake'te sabitlenir; connection limit tenant bazlıdır. Browser token'ı WebSocket subprotocol'ündedir. Kullanıcı audio'su saklanmaz; agent audio'su default kapalıdır (K-224/K-225). |
| Boşluklar | Her cümle ayrı TTS HTTP request'idir. Streaming yalnız byte döndürür; media/alignment/usage/request ID kaybolur. STT rich/realtime verisi modellenmez. Provider error body ve telemetry header'ları atılır. Tek global key/voice catalog için tenant voice ACL yoktur. Live model/format global config'e bağlıdır. |

Karar taraması: K-215 geçerli; contract core'da, ElevenLabs implementation'da.
K-216 geçerli; resmî .NET SDK yok. K-218 geçerli. K-224/K-225 yeni privacy
kanıtıyla güçlendi. K-227 ve K-501/K-502 geçerli. K-226'nın reopen eşiği resmî
Realtime STT contract'ıyla doküman düzeyinde karşılandı. K-222, Speech Engine
nedeniyle yalnız benchmark için yeniden ölçülebilir. `ADAYLAR.md`, reddedilen
ledger ve Faz 116–118 taramasında çakışan Voice kalemi bulunmadı. Voice manuel
ailesinde 61 applicable case'in 59'u geçti; iki case bilinçli atlandı.

## 2. Resmî kanıt kataloğu

| Kod | Resmî ElevenLabs kaynağı | Doğrulanan nokta |
|---|---|---|
| E1 | [TTS create](https://elevenlabs.io/docs/api-reference/text-to-speech/convert/) | voice settings, dictionary, seed, stitching, `enable_logging` |
| E2 | [Realtime TTS](https://elevenlabs.io/docs/eleven-api/guides/how-to/websockets/realtime-tts), [latency](https://elevenlabs.io/docs/eleven-api/guides/how-to/best-practices/latency-optimization) | incremental text WebSocket, buffering; incremental LLM için WS |
| E3 | [Request stitching](https://elevenlabs.io/docs/eleven-api/guides/how-to/text-to-speech/request-stitching) | completed request ID, iki saat sınırı; ZRM ile uyumsuz |
| E4 | [Timestamped TTS](https://elevenlabs.io/docs/api-reference/text-to-speech/convert-with-timestamps) | character alignment |
| E5 | [Batch STT](https://elevenlabs.io/docs/api-reference/speech-to-text/convert), [overview](https://elevenlabs.io/docs/overview/capabilities/speech-to-text) | word/char timestamp, diarization, multichannel, keyterm, redaction, webhook, long file |
| E6 | [Realtime STT](https://elevenlabs.io/docs/api-reference/speech-to-text/v-1-speech-to-text-realtime) | partial/committed event, VAD/manual commit, keyterm/entity, ZRM, single-use token |
| E7 | [Models/concurrency](https://elevenlabs.io/docs/overview/models) | Flash/Scribe latency konumu, current/max concurrency header |
| E8 | [Errors](https://elevenlabs.io/docs/eleven-api/resources/errors), [API introduction](https://elevenlabs.io/docs/api-reference/introduction) | typed error/code/request ID; rate/concurrency ayrımı; request/trace/cost header |
| E9 | [Usage analytics](https://elevenlabs.io/docs/overview/administration/usage-analytics), [OTel traces](https://elevenlabs.io/docs/eleven-agents/customization/opentelemetry-traces) | TTFB/latency/concurrency; ElevenAgents trace export yüzeyi |
| E10 | [ZRM](https://elevenlabs.io/docs/eleven-api/resources/zero-retention-mode), [API keys](https://elevenlabs.io/docs/overview/administration/workspaces/api-keys), [security](https://elevenlabs.io/docs/eleven-api/guides/how-to/best-practices/security), [residency](https://elevenlabs.io/docs/overview/administration/data-residency) | provider default retention; explicit no-log; scoped/service key, IP ve voice permission |
| E11 | [Pronunciation dictionaries](https://elevenlabs.io/docs/eleven-api/guides/how-to/text-to-speech/pronunciation-dictionaries) | IPA/CMU, locator/version ve model sınırları |
| E12 | [Speech Engine](https://elevenlabs.io/docs/overview/capabilities/speech-engine), [upstream](https://elevenlabs.io/docs/api-reference/speech-engine/speech-engine-upstream), [2026-05-25 changelog](https://elevenlabs.io/docs/changelog/2026/5/25) | provider STT/TTS/turn-taking + custom agent WebSocket/JWT/interruption |
| E13 | [Webhooks](https://elevenlabs.io/docs/eleven-api/resources/webhooks) | HMAC, idempotency, opt-in retry, queue cap ve auto-disable |
| E14 | [Libraries](https://elevenlabs.io/docs/eleven-api/resources/libraries/), [breaking policy](https://elevenlabs.io/docs/eleven-api/resources/breaking-changes-policy) | resmî REST SDK yalnız Python/JS; .NET third-party; additive JSON field breaking değil |
| E15 | [Cloning](https://elevenlabs.io/docs/eleven-api/concepts/voice-cloning), [IVC](https://elevenlabs.io/docs/eleven-api/guides/how-to/voices/instant-voice-cloning), [Voice Design](https://elevenlabs.io/docs/eleven-creative/voices/voice-design) | long-lived provider voice asset authoring |
| E16 | [Dubbing](https://elevenlabs.io/docs/overview/capabilities/dubbing), [create API](https://elevenlabs.io/docs/api-reference/legacy/dubbing/create) | async media localization workflow |
| E17 | [Audio isolation](https://elevenlabs.io/docs/api-reference/audio-isolation/stream), [resource sharing](https://elevenlabs.io/docs/overview/administration/workspaces/sharing-resources) | isolation operation; workspace resource rolleri |

## 3. Yirmi ham fikir ve eleme

| # | Fikir | Sonuç |
|---:|---|---|
| 1 | Realtime incremental STT | C1: K-226 reopen |
| 2 | Incremental text-input TTS session | A1 |
| 3 | Typed streaming audio event | B1 |
| 4 | Retention/ZRM intent | A2 |
| 5 | Typed provider failure/correlation | B2 |
| 6 | Voice OTel/concurrency telemetry | A3; #5'e bağlı |
| 7–8 | Rich transcript + keyterm/no-verbatim profile | A4'te birleşti |
| 9 | Pronunciation profile | A5 |
| 10 | Async long-form STT job | A7; ölçüm bekliyor |
| 11 | Tenant voice policy | B3 |
| 12 | Capability validation | A6 |
| 13 | Voice removal webhook cache invalidation | Elendi; A6 olmadan bağımsız değer düşük |
| 14 | Speech Engine bridge | C2; yalnız benchmark |
| 15 | Default audio isolation | Elendi; privacy/latency/cost |
| 16–17 | Voice cloning/design | Elendi; creator asset domain'i |
| 18 | Dubbing | Elendi; media localization domain'i |
| 19 | ElevenAgents adapter | Elendi; MAF control plane'ı duplicate eder |
| 20 | .NET SDK migration | Elendi; resmî SDK doğrulanmadı |

## 4. Sekiz mercek özeti

Sıra: değer · stratejik uyum · codebase boşluğu · provider bağımsızlığı · efor ·
public API riski · security/privacy · test/ops.

| Kalem | Değer | Uyum | Boşluk | Bağımsız | Efor | API | Sec | Test/Ops |
|---|---|---|---|---|---|---|---|---|
| C1 Realtime STT | yüksek | yüksek | ölçüldü | yüksek | L | yüksek | yüksek | orta-yüksek |
| A1 Incremental TTS | yüksek | yüksek | ölçüldü | yüksek | L | yüksek | orta | yüksek |
| A2 Retention | yüksek | yüksek | ölçüldü | yüksek | M | orta | yüksek | yüksek |
| A3 Telemetry | yüksek | yüksek | ölçüldü | yüksek | M | düşük-orta | düşük | yüksek |
| A4 Rich transcript | orta-yüksek | yüksek | ölçüldü | yüksek | M | orta | orta-yüksek | yüksek |
| A5 Pronunciation | orta-yüksek | yüksek | ölçüldü | orta-yüksek | M | orta | orta | yüksek |
| A6 Capability | orta | yüksek | ölçüldü | yüksek | M | orta | düşük | yüksek |
| A7 Async STT | orta | orta | ölçüldü | orta | XL | yüksek | yüksek | orta |
| C2 Speech Engine | yüksek | koşullu | ölçüldü | düşük | XL | yüksek | yüksek | yüksek |

## 5. Üç kanal

Alan kısaltmaları: **P** problem/hedef · **K** kanıt · **İ** önerilen kapsam ·
**D** dışarıda · **S** core/adapter sınırı · **Alt** alternatif · **T** trade-off ·
**API** public etki · **Sec** security/privacy/tenant · **ERÖ** efor/risk/öncelik ·
**Y** yargı.

### A. Yeni feature adayları

#### A1 · Incremental text-input TTS session

**P:** LLM delta'larını tek TTS session'ına beslemek; bugün her cümle ayrı HTTP
request'i açar. **K:** `VoiceConversationDriver.cs:602-693`; E2/E3. **İ:** Eski
interface'i bozmayan incremental synthesis session; Core capability seçer, segment
fallback kalır. **D:** Eleven v3 dialogue, hosted agent, browser-provider bağlantısı.
**S:** Core text delta/audio event bilir; buffer schedule/auth/model adapter'dadır.
**Alt:** HTTP stream veya stitching; true incremental değildir. **T:** Latency ve
continuity kazanır; socket/reconnect/backpressure state'i gelir. **API:** Yeni
interface ve event tipleri. **Sec:** Key server'da; socket tenant/session scoped;
local/provider concurrency ölçülür. **ERÖ:** L/yüksek/P0. **Y:** Kabul; önce wire
probe gerekir.

#### A2 · Explicit retention intent ve ZRM enforcement

**P:** Local audio saklanmasa da provider retention sürer; enterprise intent yoktur.
**K:** `SpeechModels.cs:3-42,133-141`, client `enable_logging` göndermez; E1/E3/E6/E10.
**İ:** `ProviderDefault`/`RequireNoRetention`; silent downgrade yok; tenant policy
request'ten sıkı olabilir. **D:** HIPAA/BAA iddiası, dashboard/support verisi.
**S:** Core intent/fail-closed; adapter parametre ve warning doğrular. **Alt:** Global
provider option; request/tenant override taşımaz. **T:** Compliance açılır; stitching
ve history ile çatışır. **API:** Additive enum/policy. **Sec:** Audio/text loglanmaz;
tenant gevşetemez. **ERÖ:** M/yüksek/P0. **Y:** Kabul; entitlement runtime'da fail
edebilir.

#### A3 · Voice provider OTel ve concurrency telemetry

**P:** TTFB, latency, request/trace ID, concurrency ve rejection görünmez. **K:**
Client'ta `ActivitySource`/`ILogger` yok; yalnız character cost okunur; E7–E9.
**İ:** Provider-neutral operation/model/outcome/duration/TTFB/usage/concurrency tags;
safe provider IDs; session/run correlation. **D:** Provider dashboard kopyası,
payload/audio/text. **S:** Core conventions; header mapping adapter'da. **Alt:** Log
only; correlation/metric zayıf. **T:** Ops değeri yüksek; cardinality ve secret riski
yönetilir. **API:** Çoğu internal; optional diagnostics yüzeyi additive. **Sec:** ID
endpoint'e çıkmaz; tenant tag low-cardinality. **ERÖ:** M/orta/P0. **Y:** Kabul;
B2 ile birlikte.

#### A4 · Rich transcript ve vocabulary profile

**P:** Speaker/word timing/channel/event kaybolur; domain terms ve no-verbatim
seçilemez. **K:** `SpeechTranscript` dört alan; STT form model/language dışında dar;
E5/E6. **İ:** Additive word segment/speaker/channel/event; logical vocabulary ve
privacy mode; basit text yolu aynı. **D:** Meeting UI, subtitle editor, provider entity
taksonomisini core'a kopyalamak. **S:** Ortak transcript semantiği core'da; keyterm,
entity ve model limitleri adapter'da. **Alt:** Provider JSON escape hatch; AOT ve
portability bozulur. **T:** Downstream değer artar; büyük response/memory ve PII artar.
**API:** Additive immutable records/options. **Sec:** Redaction varsayılan değildir;
tenant policy ve telemetry scrub gerekir. **ERÖ:** M/orta/P1. **Y:** Kabul; live path
değil, tool/operator/downstream için.

#### A5 · Logical pronunciation profile

**P:** Ürün/kişi/teknik adları tutarlı söylenmez. **K:** `SpeechRequest` alanı ve
TTS body mapping'i yok; E1/E11. **İ:** Request/agent config logical profile taşır;
adapter en çok üç locator/version'a çözer. **D:** PLS editor, IPA üretimi, dictionary
CRUD/share UI. **S:** Core logical ad/resolver; provider asset/version adapter'da.
**Alt:** Prompt/fonetik preprocessing transcript'i bozar. **T:** Runtime maliyeti
düşük; asset version lifecycle gelir. **API:** Additive profile reference/resolver.
**Sec:** Mapping tenant-scoped. **ERÖ:** M/orta/P1. **Y:** Kabul; indirection gelecekteki
provider ve tenant ayrımını korur.

#### A6 · Capability snapshot ve fail-fast validation

**P:** Feature/model/entitlement çatışması ancak provider 4xx'iyle görülür. **K:**
Synthesizer yalnız max character bildirir; transcriber capability taşımaz; E1/E2/E5–E7.
**İ:** Coarse batch/stream/incremental TTS, batch/realtime STT, alignment, diarization,
vocabulary, no-retention snapshot; numeric limit optional. **D:** Tüm model kataloğunu
hard-code etmek. **S:** Semantics core'da; model/hesap matrisi adapter'da. **Alt:**
Exception-driven veya docs-only. **T:** Erken hata; capability sürüm bakımı. **API:**
Yeni snapshot contract; conservative default. **Sec:** Entitlement cache account/tenant
scope'lu olmalı. **ERÖ:** M/orta/P1. **Y:** Kabul; A1/A2/A4/A5 foundation'ı.

#### A7 · Async long-form transcription job — beklet

**P:** Uzun media tek request'te timeout/memory baskısı yapar. **K:** `TranscribeTool`
tek çağrıda bekler; E5/E13. **İ:** Submission/status/result seam, dar HMAC webhook,
run/job correlation. **D:** Dubbing, transcoding, generic webhook platformu. **S:**
Job lifecycle core; signature/task ID adapter/AspNetCore'da. **Alt:** Consumer background
job'u; bugün en güvenli yol. **T:** Büyük dosya açılır; SSRF, duplicate, durable state ve
üç SQL maliyeti. **API:** Büyük seam/persistence. **Sec:** Opaque local tenant correlation,
URL allowlist. **ERÖ:** XL/yüksek/P2. **Y:** Şimdilik `ADAYLAR.md`ye alma; kullanıcı
talebi ve store reuse kanıtı bekle.

### B. Olası kusurlar veya eksik contract'lar

#### B1 · Streaming metadata kaybı

**P/K:** Raw byte contract media/alignment/usage/request ID taşımaz; Core media type'ı
ikinci kez ister ve mismatch silent decode üretebilir (`SpeechContracts.cs:32-52`,
`VoiceConversationOptions.cs:88-99`; E4/E8). **İ/S:** Eski method'u koruyan provider-neutral
typed audio session/event; provider frame mapping adapter'da. **D:** Eski method'u
JSON/byte union yapmak. **Alt:** Synthesizer media property; diğer metadata'yı çözmez.
**T/API:** Foundation değeri ve public API riski yüksek. **Sec:** URL/key/body event'e
girmez. **ERÖ:** M/yüksek/P0. **Y:** Eksik contract; A1'den önce tasarım kapısı.

#### B2 · Provider failure semantiği kaybı

**P/K:** Client body'yi tamamen atar; rate/concurrency, credit/scope/conflict ve request
ID kaybolur (`ElevenLabsSpeechClient.cs:628-660`; E8). **İ:** Allowlist type/code/request
ID parser ve typed failure/retry classification. **D:** Raw body logu ve blind retry.
**S:** Ortak failure sınıfı core'da; provider code mapping adapter'da. **Alt:** Status-only
telemetry, consumer control flow'a yetmez. **T/API:** Teşhis artar; exception contract'ı
kalıcıdır. **Sec:** Secret-leak test; provider ID tenant endpoint'ine çıkmaz. **ERÖ:**
S–M/orta/P0. **Y:** Eksik feature contract; promised behavior kusuru henüz kanıtlanmadı.

#### B3 · Voice resource tenant isolation yok

**P/K:** Singleton client/global key/tüm voice listesi ve arbitrary voice ID vardır;
connection tenancy voice authorization değildir (`VoiceBuilderExtensions.cs`,
`VoiceOptions.cs:23-52`; E10/E17). **İ:** Tenant-aware logical voice catalog/allowlist;
policy varsa fail-closed. **D:** Workspace RBAC kopyası, key provisioning, secret DB.
**S:** Logical policy core'da; provider voice ID/account adapter resolver'da. **Alt:**
Tenant başına host/key; güvenli fakat pahalı. **T/API:** Gerçek security sınırı; DI/cache
zorlaşır. **Sec:** Unauthorized/not-found aynı cevap; tenant-scoped cache. **ERÖ:**
L/yüksek/P0–P1. **Y:** Multi-tenant production öncesi kabul; host-wide credential
assumption'ı docs'ta explicit değil.

#### B4 · Live synthesis config'i global config'e bağlı

**P/K:** Driver yalnız text/voice ID geçirir; live ve tool TTS aynı model/formatı paylaşır
(`VoiceConversationDriver.cs:673-676`; E7). **İ:** `LowLatency`/`Balanced`/`HighQuality`
logical conversation profile, language/format ve A6 validation. **D:** Model listesini
core'a hard-code etmek. **S:** Intent core'da; model ID adapter'da. **Alt:** Global modeli
Flash yapmak tool quality'yi de değiştirir. **T/API:** Use-case ayrılır; mapping tartışmalı.
**Sec:** Tenant expensive-model policy. **ERÖ:** S–M/orta/P1. **Y:** Eksik config contract;
resolver kapsamı büyüyebilir.

### C. Yeniden açılması gereken kararlar

#### C1 · K-226 — incremental transcript yok

**P/K:** Resmî Realtime STT artık partial/final, VAD/manual commit, timestamp, language
ve token contract'ı taşır; E6/E7. **İ:** Kararı tasarım keşfine aç; ayrı
`IStreamingSpeechTranscriber`, batch interface değişmez. **D:** Browser-provider bypass,
mandatory provider VAD. **S:** Partial/commit semantics core'da; protocol/token adapter'da.
**Alt:** Client VAD + batch STT fallback. **T/API:** Caption/barge-in latency kazanır;
ordering/reconnect/usage/privacy state'i gelir. **Sec:** Server-side socket; client token
önerilmez. **ERÖ:** L/yüksek/P0. **Y:** Reopen; gerçek abonelik wire probe olmadan plan yok.

#### C2 · K-222 — provider realtime proxy ek modu

**P/K:** Speech Engine provider media/turn-taking ile custom MAF brain'i artık ayırabilir;
E12. **İ:** Kararı tersine çevirme; Option A default, Option B yalnız POC/benchmark.
**D:** Hosted ElevenAgents LLM/tools ve MAF run bypass. **S:** Her transcript yine
`AIAgent.RunStreamingAsync`; media protocol ElevenLabs-specific. **Alt:** C1+A1 ile
AgentPrism-managed realtime. **T/API:** Daha düşük latency potansiyeli; lock-in, inbound
WS/JWT, double history ve correlation riski. **Sec:** Tek kullanımlık durable tenant/session
↔ conversation binding gerekir. **ERÖ:** XL/yüksek/P1. **Y:** Yalnız karar/benchmark;
latency, parity, correlation ve failover kanıtlanmadan feature değildir.

## 6. Sıralama, bağımlılıklar ve retler

1. **C1 Realtime STT** — utterance tamamlanmadan agent işini başlatma seam'ini açar.
2. **A1 Incremental TTS** — cümle başına HTTP yerine LLM delta→audio session sağlar.
3. **A2 Retention/ZRM** — local retention ile provider retention farkını kapatır.
4. **B2+A3 Failure/telemetry** — rate, concurrency, credit ve correlation'ı işletilebilir yapar.
5. **B3 Tenant voice policy** — voice resource'u gerçek authorization sınırına bağlar.

C1 en yüksek leverage'a sahiptir. İlk seri batch STT beklemesini kaldırır. Partial
caption ve erken commit sağlar. Seam ElevenLabs'e değil streaming STT semantiğine
aittir ve başka provider'lara açıktır.

Bağımlılıklar: B1→A1; B2→A3; A6→A1/A2/A4/A5 validation; C1 production için
A3'ten yararlanır. C2, C1/A1'in provider-specific alternatifidir. A7, HMAC ve
durable tenant correlation ölçümüne bağlıdır.

**Etkileyici fakat yapılmamalı:** ElevenAgents adapter MAF control plane'ını duplicate
eder. Cloning/PVC ve Voice Design consent/ownership/asset authoring domain'idir.
Dubbing media localization ürünüdür. Default audio isolation ikinci biometric hop,
latency ve cost ekler. Raw JSON bag AOT/validation/portability'yi bozar. Third-party
.NET SDK doğrulanmış kazanım sağlamaz. Blind retry paid generation'ı duplicate edebilir.

## 7. Doğrulanamayanlar ve kapanış

- Projenin ElevenLabs hesabında Realtime STT/Speech Engine entitlement'ı doğrulanamadı.
- Realtime event ordering, reconnect/resume ve usage header'ları wire probe görmedi.
- Speech Engine token/`conversation_id` için local tenant/session metadata alanı bulunamadı.
- Speech Engine'in Option A'ya karşı end-to-end latency kazancı ölçülmedi.
- Resmî .NET REST/Speech Engine SDK doğrulanamadı; E14 .NET'i third-party gösteriyor.
- Retry'ın aynı generation için ücret/idempotency garantisi doğrulanamadı.

Her kalan kalem codebase veya resmî kaynak kanıtı ve karşı görüş taşır. Kanal ayrımı
korundu. `ADAYLAR.md` kullanıcı onayını bekler.

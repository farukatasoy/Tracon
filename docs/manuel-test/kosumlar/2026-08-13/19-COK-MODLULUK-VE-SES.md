# 19 — Çok Modluluk: Ek, Ses Tool'ları ve Gerçek Zamanlı Konuşma (`MM`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../19-COK-MODLULUK-VE-SES.md`](../../19-COK-MODLULUK-VE-SES.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/19-COK-MODLULUK-VE-SES.md
> ```

---

## Temiz geçen case'ler (49)

| Case | Durum | Başlık |
|---|---|---|
| MT-MM-001 | ☑ | Geçerli bir PNG yüklenir, sihirli bayttan doğru tür çıkarılır |
| MT-MM-002 | ☑ | İstemcinin bildirdiği yanlış `Content-Type` sihirli bayt tarafından GEÇERSİZ kılınır |
| MT-MM-003 | ☑ | Yürütülebilir/tanınmayan içerik reddedilir |
| MT-MM-004 | ☑ | Boş dosya reddedilir |
| MT-MM-005 | ☑ | `AgentPrismAttachmentOptions.MaxBytes` (varsayılan 20 MB) aşımı reddedilir |
| MT-MM-006 | ☑ | MP3 çerçeve senkronu BİT MASKESİYLE tanınır — beş geçerli varyant kabul, iki `reserved` varyant ret |
| MT-MM-010 | ☑ | İndirme doğru başlıklarla ve bayt-bayt eşleşmeyle döner |
| MT-MM-011 | ☑ | Listeleme `sessionId` ile filtreler; `skip`/`take` sınırlanır |
| MT-MM-012 | ☑ | Silme sonrası indirme VE ikinci silme `404` döner |
| MT-MM-014 | ☑ | Oturum silinince ekleri de gider (kaskad, yabancı anahtar OLMADAN) |
| MT-MM-020 | ☑ | Bir ek + mesajla çalıştırma; modele giden GERÇEK içerik `DataContent`'tir |
| MT-MM-021 | ☑ | Var olmayan bir ekle çalıştırma → `400`, akış hiç BAŞLAMAZ |
| MT-MM-023 | ☑ | `message` BOŞ ama `attachmentIds` doluysa istek GEÇERLİDİR |
| MT-MM-026 | ☑ | `/v1/responses` gömülü `data:` URI'yi eğe çevirir |
| MT-MM-027 | ☑ | `/v1/chat/completions` görsel girdiyi KABUL ETMEZ |
| MT-MM-028 | ☑ | Beyaz listede olmayan bir `data:` türü reddedilir |
| MT-MM-032 | ☑ | `pcm_*`/`ulaw_*`/`alaw_*` çıktı biçimleri AÇILIŞTA reddedilir |
| MT-MM-033 | ☑ | Tanınmayan sağlayıcı adı AÇILIŞTA reddedilir |
| MT-MM-034 | ☑ | `MaxCharactersPerRequest`/`MaxConcurrentRequests` sıfır veya negatif AÇILIŞTA reddedilir |
| MT-MM-035 | ☑ | Hatalı bir API anahtarıyla hata mesajı anahtarı SIZDIRMAZ |
| MT-MM-038 | ☑ | `GET /api/voice/voices` gerçek ses listesini döner |
| MT-MM-039 | ☑ | `GET /api/voice/health` ücret ÜRETMEDEN sağlığı ölçer |
| MT-MM-040 | ☑ | `POST /api/voice/speak` metni seslendirir, ek üretir, yanıtta karakter/maliyet döner |
| MT-MM-041 | ☑ | `POST /api/voice/speak` boş metinle `400` döner |
| MT-MM-043 | ☑ | `GET /api/voice/sessions` konuşma katmanı kapalıyken BOŞ liste döner, `501` DEĞİL |
| MT-MM-048 | ☑ | `transcribe` kayıtlı bir ses ekini metne çevirir |
| MT-MM-049 | ☑ | `transcribe` — ses OLMAYAN bir ekle çağrılırsa hata döner |
| MT-MM-050 | ☑ | `list_voices` ücret ÜRETMEDEN sesleri listeler |
| MT-MM-053 | ☑ | `speak` tool çağrısı `tool_invocations`'a `usage_unit=characters` ile yazılır |
| MT-MM-054 | ☑ | `transcribe` tool çağrısı `usage_unit=seconds` ile yazılır |
| MT-MM-055 | ☑ | `POST /api/voice/speak` (operatör yolu) `tool_invocations`'a HİÇ satır YAZMAZ |
| MT-MM-059 | ☑ | Python WebSocket istemcisini kur |
| MT-MM-062 | ☑ | `UseVoiceConversation()` açıksa ama sağlayıcı yoksa `501`; hiç çağrılmadıysa `404` |
| MT-MM-064 | ☑ | Token doğruysa alt protokolde KABUL edilir, `ready` çerçevesi gelir |
| MT-MM-065 | ☑ | Token YANLIŞSA el sıkışma REDDEDİLİR |
| MT-MM-071 | ☑ | Her tur normal bir `runs` satırı üretir — çalıştırma yolu DEĞİŞMEZ |
| MT-MM-072 | ☑ | İkinci `start` REDDEDİLİR — agent/oturum/kiracı bağlantı boyunca sabittir |
| MT-MM-073 | ☑ | Ses göndermeden `commit` — tur ÜRETİLMEZ, dinlemeye geri döner |
| MT-MM-074 | ☑ | Bilinmeyen `inputFormat` → `error` çerçevesi |
| MT-MM-075 | ☑ | Olmayan bir agent adıyla `start` → `error` çerçevesi, agent adını içerir |
| MT-MM-079 | ☑ | `MaxConcurrentConnectionsPerTenant` sınırı — sınır soket YÜKSELTİLMEDEN önce ayrılır |
| MT-MM-080 | ☑ | Ses VARSAYILAN olarak SAKLANMAZ |
| MT-MM-081 | ☑ | `PersistAudio` açıkken YALNIZ agent'ın sesi eke yazılır — kullanıcının sesi HİÇ saklanmaz |
| MT-MM-082 | ☑ | `voice_sessions` kaydı yazılır — ses İÇERMEZ, `turns`/`endReason` doğrudur |
| MT-MM-083 | ☑ | Ham PCM çözüme WAV başlığıyla gider — 44 baytlık RIFF başlığı eklenir |
| MT-MM-086 | ☑ | Canlı transkript ve altyazı, gerçek bir turda arayüzde akar |
| MT-MM-087 | ☑ | Agent konuşurken "Interrupt" düğmesi görünür; basılınca kesinti gerçekleşir |
| MT-MM-088 | ☑ | `persistAudio` açıkken görünür bir rozet belirir |
| MT-MM-090 | ☑ | i18n/tema hızlı geçiş kontrolü — konuşma paneli metinleri |

## Ayrıntı taşıyan case'ler (12)

## MT-MM-013 — Başka kiracının ekine erişilemez — "yok" gibi yanıtlanır

**Gerçek sonuç**
İlk deneme (yanlış `X-Tenant-Id` başlığıyla, çok kiracılık kapalı) `HTTP: 200`
döndü — sapma değil, doküman kusuruydu: gerçek başlık adı `X-AgentPrism-Tenant`
(`HttpTenantContext.cs:50`), `X-Tenant-Id` sunucu tarafından hiç okunmuyor ve
sessizce yok sayılıyor. Girilecek veri düzeltildi (yukarıda not edildi),
çok kiracılık `AgentPrism:Tenancy:Enabled`/`AllowHeaderResolution` ile açılıp
doğru başlıkla tekrar koşuldu: `HTTP: 404` — beklenen davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-022 — Başka kiracının eki çalıştırmada kullanılamaz

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek bulunamadi"` (doğru başlık `X-AgentPrism-Tenant` ile,
bkz. MT-MM-013 doküman düzeltmesi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-031 — `AgentPrism:Voice:ApiKey` yoksa `/api/voice/*` `501`, konuşma ucu `404` döner

**Gerçek sonuç**
Birinci istek beklendiği gibi `HTTP: 501`. İkinci istek (`$APB` ile, dokümanın
kendi komutuyla) `HTTP: 404` DEĞİL, `HTTP: 401` `{"title":"Kimlik dogrulanamadi",
"detail":"Gecerli bir 'Authorization: Bearer <token>' basligi gerekiyor."}`
döndü — GEÇERLİ bir bearer token verilmesine rağmen.

Kök neden: `UseVoiceConversation()` çağrılmadığı için `/api/voice/sessions/{id}/stream`
gerçekten kayıtlı değil (kod beklendiği gibi çalışıyor), istek
`api/`-önekli yollar için `UiEndpoints.ServeAsync`'in yakalayıcı (`{**path}`)
rotasına düşüyor (`UiEndpoints.cs:44-46,66-74`) ve orada `NotFound` (404)
üretiliyor — AMA yalnız `Authorization` başlığı BOŞSA. Bu grup
`AgentPrismEndpointFilter(options, requireBearerToken: false)` ile korunuyor
(`AgentPrismEndpointRouteBuilderExtensions.cs:294`); `requireBearerToken: false`
olunca `_authToken` `null` olarak ayarlanıyor (`AgentPrismEndpointFilter.cs:57`).
Başlık BOŞ değilse filtre statik `AuthToken`'ı HİÇ karşılaştırmıyor
(`_authToken is {Length: >0}` `false` olduğu için `93. satır` atlanıyor),
doğrudan `IApiKeyStore` üzerinden bir API anahtarı arıyor
(`AgentPrismEndpointFilter.cs:100-131`); statik bearer token kayıtlı bir API
anahtarı OLMADIĞI için arama boş dönüyor ve `134. satır`daki genel
`Unauthorized()` tetikleniyor — mesaj "gecerli bir token gerekiyor" der ama
tam olarak geçerli olan statik token zaten sağlanmıştı. Doğrulama: aynı
başlıkla kayıtlı bir rotaya (`/api/voice/health`) istek atıldığında `501`
düzgün dönüyor (bearer token orada normal şekilde denetleniyor); sorun yalnız
eşlenmemiş `api/*` yollarında ortaya çıkıyor — rastgele bir yol da
(`/api/totally-made-up-path-xyz`) aynı `401`i veriyor, yalnız bu uca özgü
değil. **Kusur — HATA-S1-014, Önem: Orta** (bkz. şerit sonuç dosyası).
`/api/voice/sessions` (§8 doğrulaması, aynı ön koşulda) beklendiği gibi
`HTTP: 200`, `[]` döndü — MT-MM-043 bu adımla birleştirildi ve GEÇTİ.

**Ön koşulu geri aldım:** `AgentPrism__Voice__ApiKey` gerçek ElevenLabs
anahtarıyla ayarlanıp uygulama yeniden başlatıldı (bkz. koşum notu).

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-014 düzeltmesiyle (K-395) yeniden koşuldu: doğru statik token artık 404, yanlış token 401. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MM-042 — `POST /api/voice/speak`, `MaxCharactersPerRequest` sınırını artık YEREL OLARAK denetler (düzeltildi)

**Gerçek sonuç**
`HTTP: 400`, `title:"Metin cok uzun"`, detay `Metin 6000 karakter; sinir 5000. ...`
— fix bekleneni yaptı, istek sağlayıcıya gitmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-046 — `speak` gerçek bir çalıştırmada çağrılır, ek `session_id`'si DOLUDUR (G1)

**Gerçek sonuç**
İlk denemede `speak` tool çağrısı `Error: Function failed.` ile başarısız
oldu — sunucu logunda kök neden: `AgentPrismException: Ses uretilemedi:
HTTP 400.` Sebep bu ortama özgüydü: paylaşılan makine-geneli `user-secrets`
deposundaki `AgentPrism:Voice:DefaultVoiceId` değeri (başka/eski bir
ElevenLabs anahtarına ait, bu şeridin env değişkeni bunu hiç override
etmemişti) bu anahtarın hesabında GEÇERSİZ bir ses kimliği taşıyordu.
`AgentPrism__Voice__DefaultVoiceId` ortam değişkeni MT-MM-038'de doğrulanmış
gerçek bir kimlikle (`hpp4J3VqNfWAUOO0d1Us`) override edilip yeniden
başlatıldıktan sonra: akış tamamlandı, `speak` çağrıldı, sonuç
`"Ses uretildi. attachmentId=019ffa0e-cebd-7274-9547-8cdb2a2ede54, ..."`
(ham ses yok). SQL sorgusu (düzeltilmiş, `tool_name` sütunu olmadan — bkz.
not) tek satır döndü: `session_id='manuel-mm-speak-2'` DOLU, `run_id` DOLU.

> **Doküman düzeltmesi:** Doğrulama sorgusundaki `tool_name` sütunu
> `attachments` tablosunda YOK (`\d attachments` doğrulandı — sütunlar:
> id/tenant_id/session_id/run_id/file_name/media_type/byte_size/sha256/
> content/external_uri/created_by/created_at). Sorgudan çıkarıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-047 — `speak` — `MaxCharactersPerRequest` aşımı tool İÇİNDE hata döner, metin KIRPILMAZ

**Gerçek sonuç**
İki denemede de model `speak` tool'unu çağırdı AMA metni 6000 karaktere
TAMAMLAMADI (1096, sonra daha direktif bir istemle 1207 karakterde kesti) —
tool başarıyla ses üretti, sınır hiç tetiklenmedi. Kök neden: `sesli-asistan`
fixture'ının model ayarı `maxOutputTokens:1024` (`GET /api/agents` çıktısı).
6000 karakterlik bir fonksiyon çağrısı argümanı tek bir tamamlamada
1024 çıktı token'ına asla sığmaz — model kaç kez denenirse denensin bu
sınıra token bütçesinden ÖNCE ulaşamaz. Bu, doğrulanabilir bir yapısal
kısıt (fixture ayarı), model isteksizliği değil.

`⏭ ATLA — model tool'u istenen uzunlukta (6000 kr) hiçbir zaman tetikleyemez;
sebep `sesli-asistan` fixture'ının `maxOutputTokens=1024` sınırı`. HTTP ucu
tarafında AYNI sınır MT-MM-042'de doğrudan (model araya girmeden) zaten
doğrulandı — kapsanan davranış orada kanıtlandı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-MM-063 — WebSocket olmayan bir isteğe `400` döner

**Gerçek sonuç**
Dokümandaki komut (statik `Authorization: Bearer` başlığıyla, `-H "$APB"`)
`HTTP 401` ("Kimlik dogrulanamadi") döndürdü, `400` DEĞİL — `HATA-S1-014`
ile AYNI kök nedene çarpıyor: `voiceGroup` de `requireBearerToken: false`
ile kurulu (`AgentPrismEndpointRouteBuilderExtensions.cs:253` — WebSocket
el sıkışması sırasında tarayıcı `Authorization` başlığı ekleyemediği için
bilinçli tasarım, token yerine WS alt protokolüyle taşınır), bu yüzden
BOŞ OLMAYAN bir `Authorization` başlığı statik token ile hiç
karşılaştırılmadan doğrudan `IApiKeyStore`'da aranıyor, bulunamayınca genel
`401` dönüyor — endpoint'in kendi "WebSocket yukseltmesi gerekiyor" `400`
mantığına hiç ulaşılamıyor. Başlıksız istekte (`Authorization` hiç
verilmeden) beklenen `400` DOĞRU şekilde alındı — ölçüldü ayrıca kanıt
olarak. Bu, önceki oturumun `HATA-S1-014`'ünün (eşlenmemiş `api/*` yolları)
kapsamının MAPLI uçları da (voice conversation grubu) kapsadığını gösteriyor
— aynı kusur, ikinci bir yüzey. Yeni numara açılmadı, `HATA-S1-014`'ün
notuna eklendi.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-014 düzeltmesiyle (K-395) yeniden koşuldu: 400 (WebSocket yukseltmesi gerekiyor) artık dogru token ile de aliniyor. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MM-066 — 🚨 Token SORGU DİZESİNDE gönderilirse KABUL EDİLMEZ

**Gerçek sonuç**
`BAGLANTI REDDEDILDI: server rejected WebSocket connection: HTTP 401` —
sorgu dizesindeki token tamamen yok sayıldı, alt protokolde
`agentprism.token.*` girdisi olmayınca bağlantı reddedildi. Beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-067 — Başka kiracının oturumuna bağlanmak o kaydı GÖRMEZ; kendi kiracısında taze bir oturum açılır

**Gerçek sonuç**
İlk denemede `AgentPrism__Tenancy__AllowHeaderResolution` bu şeridin
ortamında AÇIK DEĞİLDİ — `X-AgentPrism-Tenant: kiraci-alfa` başlığı hiç
okunmadı, hem ön koşul POST'u hem WebSocket bağlantısı aynı `default`
kiracısına, aynı oturum kimliğine yazdı (kirlenme: 6 mesaj tek oturumda
karıştı, `paylasilan-oturum-id` artık `default` kiracısında bu kirli
durumda duruyor — zararsız, başka case ona bağlı değil). Düzeltme: sunucu
`AgentPrism__Tenancy__Enabled=true` + `AgentPrism__Tenancy__AllowHeaderResolution=true`
ile yeniden başlatıldı (S1-3'ün `23` dosyasında uyguladığı aynı desen) ve
case TEMİZ bir oturum kimliğiyle (`paylasilan-oturum-id-2`) tekrarlandı.
İkinci denemede: `kiraci-alfa` oturumu doğru tenant'ta oluştu
(`tenantId:"kiraci-alfa"`, 2 mesaj). WebSocket `default` kiracısıyla
BAŞARIYLA bağlandı (`ready` çerçevesi geldi) — kendi TAZE oturumunu açtı.
`kiraci-alfa` oturumu WebSocket turu SONRASINDA da `2` mesajda sabit kaldı
— hiç değişmedi. Doküman iddiası doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 11 — Gerçek Zamanlı Konuşma: Protokol Akışı ve Durum Makinesi (Faz 29)

---

## MT-MM-070 — Uçtan uca bir tur: `start → ready → commit → transcript → runStarted → text → audioStart → audioEnd → done`

**Gerçek sonuç**
Sıra tam beklendiği gibi: `ready` → `transcript` (`final:true`,
`text:"[tone]"`) → `runStarted` (`runId: 019ffa21-f876-7f7a-8960-cc93840f40a4`)
→ `text` deltaları → `audioStart` (`audio/mpeg`) → ikili ses çerçeveleri →
`audioEnd` → `done` (`cancelled:false, turn:1`). Sapma: model burada TEK
değil İKİ ayrı `text`/`audioStart`/`audioEnd` döngüsü üretti (agent iki
ayrı `speak` tool çağrısı yaptı — "Seslendirme ister misin? İ" ve
"steren metni gönder." biçiminde bölünmüş bir yanıt). Bu, doğrulanması
istenen SIRAYI bozmuyor (döngü kendi içinde ready→...→done akışına uyuyor,
yalnız `text`/`audioStart`/`audioEnd` üçlüsü tekrarlanıyor) — kusur değil,
gerçek modelin serbest kararı (sinüs tonu anlamsız girdi olduğu için model
davranışı öngörülemez, MT-MM-070/071'in amacı protokol sırasını doğrulamak,
model içeriğini değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-078 — Kesinti (`cancel`) çalıştırmayı `Canceled` yapar; yarım yanıt geçmişe "kesildi" notuyla yazılır

**Gerçek sonuç**
İlk yarı doğrulandı: `audioStart` gelir gelmez `cancel` gönderildi, `done`
çerçevesi `cancelled: true, turn: 1` taşıdı, `GET /api/sessions/...`'in son
asistan mesajı `[Yanit kullanici tarafindan kesildi.]` dizgisini içeriyordu.
**SQL/API doğrulaması BAŞARISIZ oldu** → `HATA-S1-015` (Yüksek, yeni). `GET
/api/runs/<runId>` 15+ saniye sonra bile `status: "Running"`,
`completedAt: null`, `eventCount: 0`, `usage: null` döndürdü — run KALICI
OLARAK "Running" durumunda asılı kaldı, `Canceled`'a HİÇ geçmedi. Kök neden
kod okumasıyla bulundu: `RunRecordingAgent.RunCoreStreamingAsync`
(`src/AgentPrism.Core/Recording/RunRecordingAgent.cs:255-370`) `CompleteAsync`
çağrısını (hem `Completed` yolu satır 366 hem `Canceled` yakalayıcısı satır
323-327) yalnız İKİ yerde tetikler: (a) `enumerator.MoveNextAsync()`
`OperationCanceledException` fırlatırsa (satır 304-333'teki iç try/catch),
(b) döngü doğal olarak biterse (satır 353'ten SONRA, 355-358'deki
`finally`'nin dışında, satır 360-370). Ses turunda kesinti tam bu ikisinin
ARASINDA oluyor: `VoiceConversationDriver.RespondAsync`
(`src/AgentPrism.Core/Voice/VoiceConversationDriver.cs:606-632`) her
`update` alındıktan SONRA (RunRecordingAgent `yield return` ile kontrolü
DRIVER'a devrettikten sonra) `SpeakAsync` (ElevenLabs TTS ağ çağrısı,
satır 629) çağırıyor — kesinti tam bu TTS çağrısı SÜRERKEN geliyor
(`audioStart` zaten gönderilmiş, ses parçaları akıyor). `_turnCancellation.Cancel()`
bu noktada `SpeakAsync`'i (driver kodu, RunRecordingAgent'ın DIŞINDA) iptal
ediyor; `RespondAsync`'in `await foreach` döngüsü bir istisnayla çıkıyor,
bu da RunRecordingAgent'ın `updates` numaralandırıcısını ERKEN
`DisposeAsync()` ile kapatıyor — C#'ın async-iterator kuralına göre bu yalnız
298-358 arasındaki `finally` bloğunu (numaralandırıcının kendi
`DisposeAsync`'i) çalıştırır, 360+ satırındaki (döngüden SONRAKİ) `CompleteAsync`
çağrısına HİÇ ULAŞILMAZ — ne `Completed` ne `Canceled` yazılır, run
sonsuza dek `Running` kalır. Etki: yalnız durum yanlış değil — gerçek
OpenAI (kısmi metin akışı) ve ElevenLabs (üretilen ses parçaları) maliyeti
GERÇEKTEN oluştu ama `usage`/`cost`/`quota_usage` HİÇ kaydedilmedi (sessiz
veri kaybı). Bu, kesintiyi TETİKLEYEN her akan (`streaming`) çalıştırma
için genel bir risktir (yalnız ses'e özgü olmayabilir) — döngü gövdesinde
bir `yield return` SONRASI, bir sonraki `MoveNextAsync`'ten ÖNCE herhangi
bir istisna/iptal tüketiciyi (`consumer`) erken `DisposeAsync`'e
zorlarsa aynı sessiz kayıp oluşur.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-015 düzeltmesiyle (K-398) yeniden koşuldu: run artık status:Canceled ile tamamlaniyor. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MM-089 — Güvenli bağlam yoksa panel açılmaz, açık bir mesaj gösterilir

**Gerçek sonuç**
Doğrulandı: `samples/AgentPrism.Api/appsettings.json:31` içinde
`"AllowRemoteAccess": false` — bu şeritte hiç açılmadı. Doküman kendi
belirttiği ⏭ ATLA yoluna göre işaretlendi; geçici olarak açıp tekrar
denemek §2.1'in "kod değiştirilmez" kapsamı DIŞINDA (yalnız config, kod
değil) ama zaman bütçesi + fiziksel eylem gerektiren diğer §13 case'leri
(086-088, 090) zaten Beklemede olduğundan bu oturumda AllowRemoteAccess
açılıp tekrar denenmedi — sonraki fiziksel eylem turunda diğerleriyle
birlikte ele alınmalı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

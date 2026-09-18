# 08 — OpenAI Uyumlu Uçlar (`COMPAT`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../08-OPENAI-UYUMLU-UCLAR.md`](../../08-OPENAI-UYUMLU-UCLAR.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz B — sıradaki aile: `13 · 19 · 04 · 18 · 10 · 08`) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `f721b229` donuk |
| **Case sayısı** | 50 (MT-COMPAT-001..050) |
| **Port** | 5081 |
| **Depo** | `mt_s1` PostgreSQL şeması |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): `launch2.py` başlatıcı
script ile ortam değişkeni kullanıldı. Tenancy (`Enabled`+
`AllowHeaderResolution`) bu ailenin kendi §9/kiracı case'leri için açık
başlatıldı (dosya 18'den beri zaten açık kalan bir bayrak).

🚨 **Sıra notu:** `10-ARAYUZ-AGENT-PLAYGROUND.md` (Playwright-ağırlıklı)
YERİNE bu aile (`08`) öne alındı — Playwright tarayıcısı bu turun TAMAMI
boyunca başka bir şeritçe meşguldü, saf CLI olan bu aileye geçildi. `10`
tarayıcı boşalınca koşulacak.

---

## Devir notu

**Aile KAPANDI: MT-COMPAT-001..050 koşuldu, 50/50 Geçti — sıfır açık
kalem, sıfır yeni kusur.** Bu turun en temiz ailesi: hiçbir spec
düzeltmesi gerekmedi, `azure-support`'un bu ortamda kayıtlı olmaması
(bilinen kısıt) dışında hiçbir ortam engeli çıkmadı.

**Tek ortam ikamesi:** `azure-support` agent'ı bu ortamda hiç kayıtlı
değil (Azure kimliği yok). MT-COMPAT-027/028 (sağlayıcı hatası akışsız/
akışlı) `manuel-bozuk-model` (önceki bir aileden kalma, salt-okunan
gerçek bir "kırık sağlayıcı" fixture'ı) ile aynı sözleşmeyi doğruladı.

**En değerli tek bulgu bir kusur DEĞİL:** MT-COMPAT-029, HTTP yanıtının
`status:"completed"` gösterdiği ama run kaydının `AwaitingApproval`
olduğu gerçek bir tutarsızlığı ölçtü — ama kaynak (`OpenAIResponsesEndpoints
.cs:339-383`) bu TAM senaryoyu önceden, MAF'ın gerçek OpenAI Responses
API davranışıyla karşılaştırarak belgelemiş: kasıtlı ve doğru. Onay
bekleyen bir çağrı yalnız yönetim onay API'siyle (`POST
/api/approvals/{id}/decide`) yanıtlanabilir, compat uçlarından DEĞİL.

**Python SDK (§4, MT-COMPAT-045-047):** resmi `openai` paketi
(v3.0.0) `responses`/`conversations`/`chat.completions` istemcilerinin
hiçbirinde istisna fırlatmadı — Tracon'in compat yüzeyi gerçek bir SDK'ya
karşı tam uyumlu.

**Sıradaki ailenin işi:** `10-ARAYUZ-AGENT-PLAYGROUND.md` — Playwright'ın
boşaldığını kontrol et; hâlâ meşgulse `13`/`19`/`04`/`18`'in ertelenen
UI/kod-donması case'lerini (bkz. o dosyaların fiziksel eylem tabloları)
toplu olarak değerlendirmeyi düşün.

---

# 1 — `POST /v1/chat/completions` (durumsuz)

## MT-COMPAT-001 — `model` alanından agent seçilir, mutlu yol

**Gerçek sonuç**
`HTTP 200`, `object:"chat.completion"`, `id` `chatcmpl-` önekli,
`choices[0].message.role:"assistant"`, `finish_reason:"stop"`, `usage`
dolu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-002 — `metadata.entity_id` ile agent seçilir

**Gerçek sonuç**
`model:"gorunmez-model-adi"` (geçersiz) + `metadata.entity_id:"support"`
→ `HTTP 200`, yanıt doldu — `entity_id` önceliği doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-003 — Ne `model` ne `entity_id` verilirse `400`

**Gerçek sonuç**
`HTTP 400`, `{"error":{"message":"No agent selected...","type":"invalid_request_error"}}`
— OpenAI zarfı, `ProblemDetails` değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-004 — Bilinmeyen agent `404` + `model_not_found`

**Gerçek sonuç**
`HTTP 404`, `error.type:"model_not_found"`, mesaj agent adını içeriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-005 — `messages` boş dizi ise `400`

**Gerçek sonuç**
`HTTP 400`, `"'messages' cannot be empty."`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-006 — Bozuk JSON gövdesi `400`

**Gerçek sonuç**
`HTTP 400`, mesaj `"Body could not be parsed:"` ile başlıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-007 — Parçalı içerik (`content` dizi biçimi) birleştirilir

**Gerçek sonuç**
İki parça (`"ORD-1001 "` + `"siparisim nerede?"`) birleşti, model
`ORD-1001` içeren gerçek bir sipariş durumu yanıtı verdi (tool
tetiklendi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-008 — `developer` rolü `system`'e eşlenir

**Gerçek sonuç**
`developer` talimatı ("yalnız 'tamam' yaz") izlendi, yanıt tam `"tamam"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-009 — Tool çağrısı tel biçiminde görünmez, ama çalıştırma kaydına düşer

**Gerçek sonuç**
Yanıt gövdesinde `tool_calls`/`function_call` alanı YOK, yalnız düz metin
(`ORD-1001` içeriyor). `GET /api/runs` en üst kaydı `status:Completed,
eventCount:6`; `GET /api/runs/{id}/events` içinde `get_order_status`
geçiyor — tool çağrısı kaybolmadı, yalnız tel biçiminden dışlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-010 — `usage` alanları `snake_case` döner

**Gerçek sonuç**
`['prompt_tokens', 'completion_tokens', 'total_tokens']` — tam eşleşme.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-011 — Akışlı yanıt: ilk çerçeve `role` deltası, biten işaret `[DONE]`

**Gerçek sonuç**
`content-type: text/event-stream`. İlk çerçeve `"delta":{"role":"assistant"}`
(spec `"content":null` de bekliyordu — .NET'in null alanları serileştirmeden
atlaması, işlevsel olarak eşdeğer, kusur değil). Sonraki çerçeveler
`"content":"..."` parçaları. Son iki çerçeve `finish_reason:"stop"` ve düz
`data: [DONE]`. Her çerçevede `object:"chat.completion.chunk"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-012 — Akış durumsuzdur: hiçbir oturum/konuşma oluşmaz

**Gerçek sonuç**
Oturum sayısı çağrılardan önce/sonra **4/4** — değişmedi (hem akışsız hem
akışlı çağrı `sessions` tablosuna hiç dokunmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-013 — `Idempotency-Key` tekrarı önbellekten döner

**Gerçek sonuç**
İlk yanıtta `Idempotency-Replayed` yok, ikincide `true`, iki gövde
BİREBİR aynı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-014 — Aynı `Idempotency-Key`, farklı gövde → `422`

**Gerçek sonuç**
`HTTP 422`, `title:"Idempotency-Key used for a different request"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-015 — `Idempotency-Key` + `stream: true` → `400`

**Gerçek sonuç**
`HTTP 400`, `title:"Idempotency-Key not supported on streaming requests"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-016 — Aynı tel biçimi gerçek bir Anthropic agent'ıyla da çalışır

**Gerçek sonuç**
`claude-support` ile `HTTP 200`, MT-COMPAT-001 ile birebir aynı şema
(`object:"chat.completion"`, `choices[0].message`, `usage`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — `POST /v1/responses` (oturum destekli)

## MT-COMPAT-017 — `model` alanından agent seçilir, mutlu yol

**Gerçek sonuç**
`HTTP 200`, gerçek OpenAI Responses şeması: `id` `resp_` önekli,
`object` yok ama `status:"completed"`, `output[0]` mesajı doğru.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-018 — `metadata.entity_id` ile agent seçilir

**Gerçek sonuç**
`model` verilmeden `metadata.entity_id:"support"` → `HTTP 200`,
`status:"completed"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-019 — Agent seçilmezse `400` + bilinen agent listesi

**Gerçek sonuç**
`HTTP 400`, mesaj `"Kayitli agent'lar"` yerine İngilizce `"Registered
agents:"` ile 22 agent adını listeliyor (`support` dahil) — Chat
Completions'tan (MT-COMPAT-003, liste YOK) farkı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-020 — Bilinmeyen agent `404` + bilinen agent listesi

**Gerçek sonuç**
`HTTP 404`, `error.type:"model_not_found"`, mesaj hem agent adını hem
`"Registered agents:"` listesini içeriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-021 — `conversation` alanı oturumu o kimlikle saklar (K-043)

**Gerçek sonuç**
Yanıt `id` (`resp_...`) `manuel-conv-021`'den farklı. `GET
/api/sessions/manuel-conv-021` → `200`, `agentName:"support"` — ikinci
bir depo açılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-022 — `previous_response_id` geçmişi zincirler

**Gerçek sonuç**
İkinci tur "Az önce sorduğunuz sipariş numarası: **ORD-1001**." — ilk
turun geçmişini gördü. `GET /api/sessions/$RID1` → `200`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-023 — Konuşma kimliğine çapraz kiracı erişimi `404` döner

**Gerçek sonuç**
`kiraci-beta` ile aynı `conversation` kimliğine erişim → `HTTP 404`,
`error.type:"not_found_error"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-024 — Akışlı yanıt OpenAI olay adlarını kullanır, `[DONE]` YOKTUR

**Gerçek sonuç**
İlk olay `response.created`, son olay `response.completed`. `[DONE]`
sıfır eşleşme. Ara olaylar `response.in_progress`,
`response.output_item.added/done`, `response.content_part.added/done`,
`response.output_text.delta/done` — spec'in kod okumasıyla ölçtüğü
listeyle birebir örtüşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-027 — Akışsız yolda sağlayıcı hatası `502` döner — `azure-support` yerine ikame

**Gerçek sonuç — ortam ikamesi (gerekçe aşağıda).** `azure-support`
agent'ı bu ortamda hiç KAYITLI DEĞİL (`404` — Azure kimliği yok, bilinen
kısıt, `00-INDEKS.md`). Aynı "bilinçli kırık sağlayıcı" senaryosunu
üreten, önceki bir aileden kalma gerçek bir fixture (`manuel-bozuk-model`,
`provider:openai, model:"gpt-olmayan-model-xyz"`) kullanıldı — salt
okundu, değiştirilmedi. Sonuç: `HTTP 502`, `error.type:"upstream_error"`,
gövde OpenAI zarfı (`ProblemDetails` değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-028 — Akışlı yolda sağlayıcı hatası `event: error` çerçevesi üretir (düzeltildi) — aynı ikame

**Gerçek sonuç**
Aynı `manuel-bozuk-model` ikamesiyle: başlıklar `200`/`text/event-stream`
gönderildi, bağlantı çerçevesiz KAPANMADI — `event: error` çerçevesi
geldi (`"message":"The model provider request failed."`). Çapraz
doğrulama: `GET /api/runs?agentName=manuel-bozuk-model&take=1` →
`status:"Failed"`. K-296'nın düzeltmesi hâlâ geçerli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-025 — Gömülü `data:` URI bir eke çevrilir, sohbet geçmişine gömülmez

**Gerçek sonuç**
`/api/attachments?sessionId=manuel-conv-025` → tam 1 kayıt,
`mediaType:"image/png"`. `GET /api/sessions/manuel-conv-025` geçmişinde
ham base64 dizgisi YOK, yalnız `attachments/` referans URI'si VAR.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-026 — Tanınmayan ikili tür `400` ile reddedilir

**Gerçek sonuç**
`HTTP 400`, `"File type not recognized. Supported types: application/pdf,
audio/*, image/gif, image/jpeg, image/png, image/webp, text/plain."`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-029 — Onay gerektiren bir tool çağrısı `/v1/responses` üzerinden nasıl görünür

**Gerçek sonuç — gözlenen davranış tam kaydedildi, kod düzeyinde ZATEN
gerekçeli (kusur DEĞİL).** HTTP yanıtı `status:"completed"`,
`output[0]` bir `function_call` ögesi (`name:"cancel_order",
arguments:{"orderId":"ORD-1001"}, status:"completed"`) taşıyor —
tool GERÇEKTEN çalışmış gibi görünüyor. Ama `GET /api/runs?...` aynı
çalıştırmayı `status:"AwaitingApproval"` gösteriyor — HTTP yanıtı ile run
kaydı GERÇEKTEN TUTARSIZ. Kaynak (`OpenAIResponsesEndpoints.cs:339-383`)
bu tam senaryoyu ÖNCEDEN, uzun bir yorumla belgeliyor: MAF'ın
`OpenAIResponses.WriteResponse`'u onay bekleyen bir çağrıyı SESSİZCE
düşürüyordu (çıktı boş array + `status:completed` — çağrının VARLIĞINDAN
bile haberdar olunmuyordu); düzeltme, öğeyi gerçek OpenAI'nin
`function_call` şemasıyla BAYT-BAYTA aynı şekilde enjekte ediyor — bu,
MAF'ın NORMAL (onaysız) bir tool çağrısı için ürettiği AYNI şekil, ve
gerçek OpenAI Responses API'si de bekleyen bir çağrıyı `"requires_action"`
DEĞİL tam olarak böyle temsil ediyor. **Çağrı bu UÇTAN asla
yanıtlanamaz** — yönetim onay API'si (`POST /api/approvals/{id}/decide`)
kullanılmalı. Bu, kodun kendi belgelediği, kasıtlı ve doğru bir
tasarımdır — HATA açılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-030 — `Idempotency-Key` + `stream: true` `/v1/responses`'ta da `400`

**Gerçek sonuç**
`HTTP 400`, `title:"Idempotency-Key not supported on streaming requests"`
— MT-COMPAT-015 ile aynı filtre.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — `/v1/conversations*`

## MT-COMPAT-031 — Boş gövdeyle oluşturma: `conv_` önekli kimlik döner

**Gerçek sonuç**
`id:"conv_01a0addcd68671afb7bcbbbff7aa569c"` — `conv_` + 32 hex,
`object:"conversation"`. `metadata` alanı yanıtta hiç yok (null olduğu
için .NET serileştirmede atlanmış — MT-COMPAT-011 ile aynı zararsız fark).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-032 — Metadata ile oluşturma: yalnız metin alanları geri döner

**Gerçek sonuç**
`metadata:{"kaynak":"manuel-test"}` — `sayi_alani`/`bool_alani` sessizce
düştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-033 — Bozuk gövde sessizce yutulmaz, `400` döner

**Gerçek sonuç**
`HTTP 400`, `"Body could not be parsed:"` ile başlıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-034 — Kullanılmamış konuşma `GET`'i `200` boş döner, `404` DEĞİL

**Gerçek sonuç**
`HTTP 200`, `object:"conversation"`, `created_at` şimdiki zamana yakın.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-035 — Kullanılmış konuşmanın `created_at`'i oturum oluşturma zamanını yansıtır

**Gerçek sonuç**
`created_at` oturumun gerçek oluşturulma anına yakın bir Unix damgası.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-036 — Konuşma `GET`'ine çapraz kiracı erişimi `404` döner

**Gerçek sonuç**
`kiraci-beta` ile `HTTP 404`, `error.type:"not_found_error"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-037 — Silme, altındaki oturumu da siler

**Gerçek sonuç**
`DELETE` → `{"id":"manuel-conv-037","object":"conversation.deleted","deleted":true}`.
Ardından `GET /api/sessions/manuel-conv-037` → `404`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-038 — Kullanılmamış bir konuşmayı silmek hata değil, `deleted: false` döner

**Gerçek sonuç**
`HTTP 200`, `deleted: false`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-039 — Çapraz kiracı silme `404` döner, başka kiracının oturumunu silmez

**Gerçek sonuç**
`kiraci-beta` silme denemesi → `404`. `kiraci-alfa`'nın oturumu hâlâ var
(`GET` → `200`, mesajlar bozulmamış) — başarısız silme girişimi hiçbir
etki bırakmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-040 — Öge listesi mesaj/tool çağrısı/tool sonucu sırasını korur

**Gerçek sonuç**
Sıra tam: `message` → `function_call` (`get_order_status`) →
`function_call_output` (aynı `call_id`) → `message`. Toplam 4 öge.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-041 — `limit` parametresi kırpar, `has_more` doğru döner (düzeltildi)

**Gerçek sonuç**
`limit=2` → tam 2 öge, `has_more:true` (gerçek toplam 4, fix'in
regresyonu YOK).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-042 — Kullanılmamış konuşmada öge listesi boş dizi döner

**Gerçek sonuç**
`HTTP 200`, `{"object":"list","data":[],"has_more":false}`
(`first_id`/`last_id` null olduğu için yanıtta hiç yok — zararsız fark).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-043 — Öge listesine çapraz kiracı erişimi `404` döner

**Gerçek sonuç**
`kiraci-beta` → `HTTP 404`, `error.type:"not_found_error"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-044 — `POST /v1/conversations/{id}/items` desteklenmez (koşumda düzeltildi)

**Gerçek sonuç**
`HTTP 405` (404 değil), `Allow: GET, HEAD` başlığı, gövde
`application/problem+json` bir `ProblemDetails` — spec'in kendi
düzeltilmiş beklentisiyle birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Resmi `openai` Python SDK ile uçtan uca

## MT-COMPAT-045 — `responses.create` + `responses.stream` + `previous_response_id` zinciri

**Gerçek sonuç**
İlk yanıt `ORD-1001` içeriyor. Akış `response.created`'dan
`response.completed`'a kesintisiz 12 olay yazdırdı, istisna yok.
Zincirlenmiş yanıt `"Az önce **ORD-1001** sipariş numarasını sordunuz."`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-046 — `conversations.create/retrieve/items.list/delete` zinciri

**Gerçek sonuç**
`conv.id` `conv_` ile başlıyor. `items.data` 2 `message` içeriyor (en az
1 şartı fazlasıyla karşılandı). `deleted.deleted = True`. Hiçbir adımda
SDK istisnası yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-047 — `chat.completions.create` (akışlı/akışsız) + `NotFoundError` yakalama

**Gerçek sonuç**
İlk yanıt `ORD-1001` içeriyor. Akış kesintisiz metin yazdı. Bilinmeyen
agent'ta `NotFoundError` yakalandı, `status_code == 404`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Kimlik doğrulama ve hata güvenliği

## MT-COMPAT-048 — `Authorization` başlığı eksikse `401`

**Gerçek sonuç**
`HTTP 401`, `"Authentication failed"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-049 — Yanlış bearer token `401`

**Gerçek sonuç**
`HTTP 401` — `TraconEndpointFilter` compat uçlarına da uygulanıyor, ayrı
bir kimlik doğrulama yolu yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-050 — Provider hatası Responses/Chat Completions'ta ham metin sızdırmaz (Faz 103)

**Gerçek sonuç — otomatik test kanıtı.**
`ProviderOutageErrorHandlingTests`'in `*_never_exposes_a_secret_like_provider_message`
ailesinden 4 test spec'in kendi notunda "Otomatikleştirildi" diye
işaretli; dosya 19'un MT-MM-108'inde bu paketin TAMAMI
(`Tracon.AspNetCore.FunctionalTests`, 1077/1077) bu turda zaten koşuldu
ve geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

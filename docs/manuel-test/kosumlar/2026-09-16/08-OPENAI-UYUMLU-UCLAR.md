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

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 19630654:docs/manuel-test/kosumlar/2026-09-16/08-OPENAI-UYUMLU-UCLAR.md
> ```

---

## Temiz geçen case'ler (45)

| Case | Durum | Başlık |
|---|---|---|
| MT-COMPAT-001 | ☑ | `model` alanından agent seçilir, mutlu yol |
| MT-COMPAT-002 | ☑ | `metadata.entity_id` ile agent seçilir |
| MT-COMPAT-003 | ☑ | Ne `model` ne `entity_id` verilirse `400` |
| MT-COMPAT-004 | ☑ | Bilinmeyen agent `404` + `model_not_found` |
| MT-COMPAT-005 | ☑ | `messages` boş dizi ise `400` |
| MT-COMPAT-006 | ☑ | Bozuk JSON gövdesi `400` |
| MT-COMPAT-007 | ☑ | Parçalı içerik (`content` dizi biçimi) birleştirilir |
| MT-COMPAT-008 | ☑ | `developer` rolü `system`'e eşlenir |
| MT-COMPAT-009 | ☑ | Tool çağrısı tel biçiminde görünmez, ama çalıştırma kaydına düşer |
| MT-COMPAT-010 | ☑ | `usage` alanları `snake_case` döner |
| MT-COMPAT-012 | ☑ | Akış durumsuzdur: hiçbir oturum/konuşma oluşmaz |
| MT-COMPAT-013 | ☑ | `Idempotency-Key` tekrarı önbellekten döner |
| MT-COMPAT-014 | ☑ | Aynı `Idempotency-Key`, farklı gövde → `422` |
| MT-COMPAT-015 | ☑ | `Idempotency-Key` + `stream: true` → `400` |
| MT-COMPAT-016 | ☑ | Aynı tel biçimi gerçek bir Anthropic agent'ıyla da çalışır |
| MT-COMPAT-017 | ☑ | `model` alanından agent seçilir, mutlu yol |
| MT-COMPAT-018 | ☑ | `metadata.entity_id` ile agent seçilir |
| MT-COMPAT-019 | ☑ | Agent seçilmezse `400` + bilinen agent listesi |
| MT-COMPAT-020 | ☑ | Bilinmeyen agent `404` + bilinen agent listesi |
| MT-COMPAT-021 | ☑ | `conversation` alanı oturumu o kimlikle saklar (K-043) |
| MT-COMPAT-022 | ☑ | `previous_response_id` geçmişi zincirler |
| MT-COMPAT-023 | ☑ | Konuşma kimliğine çapraz kiracı erişimi `404` döner |
| MT-COMPAT-024 | ☑ | Akışlı yanıt OpenAI olay adlarını kullanır, `[DONE]` YOKTUR |
| MT-COMPAT-027 | ☑ | Akışsız yolda sağlayıcı hatası `502` döner — `azure-support` yerine ikame |
| MT-COMPAT-025 | ☑ | Gömülü `data:` URI bir eke çevrilir, sohbet geçmişine gömülmez |
| MT-COMPAT-026 | ☑ | Tanınmayan ikili tür `400` ile reddedilir |
| MT-COMPAT-030 | ☑ | `Idempotency-Key` + `stream: true` `/v1/responses`'ta da `400` |
| MT-COMPAT-031 | ☑ | Boş gövdeyle oluşturma: `conv_` önekli kimlik döner |
| MT-COMPAT-032 | ☑ | Metadata ile oluşturma: yalnız metin alanları geri döner |
| MT-COMPAT-033 | ☑ | Bozuk gövde sessizce yutulmaz, `400` döner |
| MT-COMPAT-034 | ☑ | Kullanılmamış konuşma `GET`'i `200` boş döner, `404` DEĞİL |
| MT-COMPAT-035 | ☑ | Kullanılmış konuşmanın `created_at`'i oturum oluşturma zamanını yansıtır |
| MT-COMPAT-036 | ☑ | Konuşma `GET`'ine çapraz kiracı erişimi `404` döner |
| MT-COMPAT-037 | ☑ | Silme, altındaki oturumu da siler |
| MT-COMPAT-038 | ☑ | Kullanılmamış bir konuşmayı silmek hata değil, `deleted: false` döner |
| MT-COMPAT-039 | ☑ | Çapraz kiracı silme `404` döner, başka kiracının oturumunu silmez |
| MT-COMPAT-040 | ☑ | Öge listesi mesaj/tool çağrısı/tool sonucu sırasını korur |
| MT-COMPAT-042 | ☑ | Kullanılmamış konuşmada öge listesi boş dizi döner |
| MT-COMPAT-043 | ☑ | Öge listesine çapraz kiracı erişimi `404` döner |
| MT-COMPAT-045 | ☑ | `responses.create` + `responses.stream` + `previous_response_id` zinciri |
| MT-COMPAT-046 | ☑ | `conversations.create/retrieve/items.list/delete` zinciri |
| MT-COMPAT-047 | ☑ | `chat.completions.create` (akışlı/akışsız) + `NotFoundError` yakalama |
| MT-COMPAT-048 | ☑ | `Authorization` başlığı eksikse `401` |
| MT-COMPAT-049 | ☑ | Yanlış bearer token `401` |
| MT-COMPAT-050 | ☑ | Provider hatası Responses/Chat Completions'ta ham metin sızdırmaz (Faz 103) |

## Ayrıntı taşıyan case'ler (5)

## MT-COMPAT-011 — Akışlı yanıt: ilk çerçeve `role` deltası, biten işaret `[DONE]`

**Gerçek sonuç**
`content-type: text/event-stream`. İlk çerçeve `"delta":{"role":"assistant"}`
(spec `"content":null` de bekliyordu — .NET'in null alanları serileştirmeden
atlaması, işlevsel olarak eşdeğer, kusur değil). Sonraki çerçeveler
`"content":"..."` parçaları. Son iki çerçeve `finish_reason:"stop"` ve düz
`data: [DONE]`. Her çerçevede `object:"chat.completion.chunk"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-028 — Akışlı yolda sağlayıcı hatası `event: error` çerçevesi üretir (düzeltildi) — aynı ikame

**Gerçek sonuç**
Aynı `manuel-bozuk-model` ikamesiyle: başlıklar `200`/`text/event-stream`
gönderildi, bağlantı çerçevesiz KAPANMADI — `event: error` çerçevesi
geldi (`"message":"The model provider request failed."`). Çapraz
doğrulama: `GET /api/runs?agentName=manuel-bozuk-model&take=1` →
`status:"Failed"`. K-296'nın düzeltmesi hâlâ geçerli.

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

## MT-COMPAT-041 — `limit` parametresi kırpar, `has_more` doğru döner (düzeltildi)

**Gerçek sonuç**
`limit=2` → tam 2 öge, `has_more:true` (gerçek toplam 4, fix'in
regresyonu YOK).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-COMPAT-044 — `POST /v1/conversations/{id}/items` desteklenmez (koşumda düzeltildi)

**Gerçek sonuç**
`HTTP 405` (404 değil), `Allow: GET, HEAD` başlığı, gövde
`application/problem+json` bir `ProblemDetails` — spec'in kendi
düzeltilmiş beklentisiyle birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Resmi `openai` Python SDK ile uçtan uca

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

Aile açılıyor — bu ilk devir notu.

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

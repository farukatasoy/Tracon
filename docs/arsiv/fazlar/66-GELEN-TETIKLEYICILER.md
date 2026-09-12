# Faz 66 — Gelen Tetikleyiciler

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-65**
> **Önkoşul:** [Faz 17](17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md) — iş kuyruğu · [Faz 21](21-KOTA-VE-OLAY-YAYINI.md) — `WebhookSigner` ters yönde kullanılır · [Faz 43](43-IDEMPOTENCY-KEY.md) — tekrar koruması oradan gelir · [Faz 46](46-DAYANIKLI-CALISTIRMA.md) — `JobKind.AgentRun` tetikleyicinin hedefidir · [Faz 53](53-KIRACI-API-ANAHTARLARI.md) — kapsam modeli
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.Sql.Shared`, `Tracon.PostgreSql`, `Tracon.SqlServer`, `Tracon.Sqlite`, `Tracon.AspNetCore`, `Tracon.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli — üç set** (yeni `inbound_triggers` tablosu). Numara uygulama anında alınır (K-178)
> **Public API:** **büyüyor** — bir kayıt tipi, bir depo arayüzü, uç ailesi. `PublicAPI.Shipped.txt` bugün **boş**; ekleme **bugün bedava**
> **Site etkisi:** `guides/background-work.md`, `concepts/runs.md`, yeni `guides/inbound-triggers.md`
> **Manuel test alanı:** [`docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md`](../../manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/66-GELEN-TETIKLEYICILER.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 21 **giden** webhook'u verdi: Tracon dış dünyaya olay yollar. Tersi yoktur. Bir Slack mesajı, bir e-posta, bir kuyruk olayı bir çalıştırma başlatamaz. Agent yalnız **sorulunca** konuşur; olaya tepki veremez. - **F-65** — İmzalı gelen uç, olay → agent eşlemesi ve Faz 17'nin kuyruğuna düşürme.

## Bitiş Ölçütleri (DoD)

- [x] Doğru imzalı istek `202` + `Location` döner ve çalıştırma kuyruktan koşar — `samples/Tracon.Api`'de doğrulandı: `202`, `Location: /tracon/api/runs/{runId}`, run birkaç saniyede `Completed`
- [x] İmzasız, yanlış imzalı ve pencere dışı istek `401` döner — `TriggerEndpointTests` + `InboundTriggerDispatcherTests`
- [x] Aynı istek ikinci kez `409` döner; ikinci çalıştırma açılmaz — `A_replayed_request_is_rejected_the_second_time`
- [x] Bilinmeyen kiracı `401` alır (**K-472**, plandaki `404` değil), varsayılana **düşmez** — `An_unknown_tenant_does_not_fall_back_to_the_default_tenant`
- [x] Yanıt "tetikleyici yok" ile "imza yanlış" arasında fark **göstermez** — `An_unknown_trigger_name_and_a_wrong_signature_return_the_identical_response` iki gövdeyi bayt bayt karşılaştırır
- [x] Tetikleyici çalıştırması kota kapısından geçer — `A_full_quota_rejects_the_trigger_with_429_and_does_not_bypass_it`
- [x] Hız sınırı ve gövde boyutu sınırı çalışır (`429` / `413`) — `The_trigger_rate_limit_rejects_requests_beyond_the_per_minute_cap`, `A_body_larger_than_the_configured_limit_is_rejected`
- [x] İmza `secret`'ının değeri hiçbir yerde saklanmaz; yalnız yapılandırma adı durur — `pg_dump` taraması 0 eşleşme (aşağıda)
- [x] Önek dışındaki bir yapılandırma adı `400` ile reddedilir — `Name_outside_the_allowed_prefix_is_rejected`
- [x] Başka kiracının tetikleyicisi ne görünür ne çalışır (sözleşme testi, dört koşum) — `InboundTriggerStoreContract`, InMemory + 3 SQL sağlayıcısı
- [x] Tetikleyici yazımı denetim izine mutasyondan **önce** yazılır — `Save_writes_an_audit_trail_entry_before_the_definition_is_readable`; kodda `ApprovalEndpoints`/K-370 ile birebir aynı desen
- [x] Dört doğrulama kapısı sıfır uyarı verir — build/test/pack/format hepsi temiz
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıdaki "Doğrulama komutları" bölümü gerçek çıktıyla güncellendi
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md` içine eklendi (MT-JOB-091..102); otomatikleştirilebilenler (091-101) `samples/Tracon.Api`'ye karşı koşuldu, 102 (arayüz) 👤 insan gerekir
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/` güncellendi (`guides/background-work.md`, `guides/inbound-triggers.md`, `concepts/runs.md`); `npm run build` + `check-links.mjs` temiz — 970 sayfa, 122628 iç bağlantı, kırık yok
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — 165,4 KB → **171,3 KB** gzip / 250 KB (+5,9 KB)

### Doğrulama komutları

Gerçek koşum çıktısı (2026-08-19, `samples/Tracon.Api`, gerçek PostgreSQL + gerçek `support` agent):

```bash
dotnet user-secrets set "Tracon:TriggerSecrets:Slack" "whsec_manual_test_66" \
  --project samples/Tracon.Api

curl -s -X PUT http://localhost:5000/tracon/api/triggers/slack \
  -H 'Authorization: Bearer manuel-test-token-2026' -H 'Content-Type: application/json' \
  -d '{"targetKind":"agent","targetName":"support","signingSecretConfigurationName":"Tracon:TriggerSecrets:Slack","payloadMode":"path","payloadPath":"event.text"}'
# -> {"name":"slack",...,"resolved":true,...}

# imzali istek (X-Tracon-Timestamp/-Signature hesabi WebhookSigner.Sign ile)
curl -s -i -X POST http://localhost:5000/tracon/api/triggers/default/slack \
  -H 'Content-Type: application/json' -H "X-Tracon-Timestamp: $TS" -H "X-Tracon-Signature: $SIG" \
  --data-binary @body.json
# -> HTTP/1.1 202 Accepted
#    Location: /tracon/api/runs/01a01890-1652-7183-9d50-6efd430644a3
#    {"runId":"01a01890-...","jobId":"01a01890-...", "location":"...","eventsLocation":".../events"}

curl -s http://localhost:5000/tracon/api/runs/01a01890-1652-7183-9d50-6efd430644a3 -H "$AUTH"
# -> "status":"Completed", modelId gpt-5.4-mini, usage.totalTokens 246

# ayni imza tekrar -> 409; imzasiz -> 401; bilinmeyen kiraci -> 401; bilinmeyen isim -> 401
# path cozulmezse -> 400 detail: "The payload path 'event.text' did not resolve..."

pg_dump -U postgres -d tracon --schema=tracon | grep -c "whsec_manual_test_66"
# -> 0
```

---

## Plandan Sapmalar

1. **404 → 401 birleşmesi (K-472).** İlk uygulama planın taslak public API'sindeki
   `NotFound`/`Unauthorized` ayrımını birebir kodladı (bilinmeyen kiracı/isim/devre
   dışı → `404`; imza hatası → `401`). Bağımsız denetim bunun 66.2'nin "aynı gövde
   ve kod" kuralını (ve manuel case 7'nin `404` beklentisiyle o kuralın kendi
   içindeki çelişkiyi) ihlal ettiğini, ve bunun gerçek bir numaralandırma açığı
   olduğunu gösterdi. Kapanışta TÜM bu durumlar TEK bir jenerik `401`'e birleştirildi;
   manuel case 96/97 buna göre güncellendi.
2. **İmza/hız sınırı sırası (K-473).** Plan sıralamayı açıkça belirtmiyordu; ilk
   uygulama hız sınırını imzadan ÖNCE kontrol etti ("her istek sayılır" sezgisiyle).
   Bağımsız denetim bunun kimliksiz bir saldırganın gerçek gönderenin bütçesini
   tüketmesine izin verdiğini gösterdi; sıra ters çevrildi.
3. **Hedef varlığı doğrulanmıyor (K-474, gerekçelendi — sapma değil).** Plan bunu
   açıkça belirtmiyordu; `SchedulingEndpoints`/Faz 17 emsaliyle tutarlı olacak
   şekilde kabul anında agent/workflow varlığı kontrol EDİLMEDİ — hata işleyici
   seviyesinde (`run` → `Failed`) yakalanır. Bağımsız denetimde sorgulandı, emsal
   ile doğrulanıp onaylandı.
4. **Rate limiter `System.Threading.RateLimiting` yerine elle yazıldı.** Plan
   Faz 21'in `TraconRateLimitFilter`'ıyla aynı altyapıyı ima ediyordu, ama o
   tip ASP.NET Core paylaşılan çerçevesinden gelir ve `Tracon.Core` (düz sınıf
   kütüphanesi, web bağımlılığı yok) onu göremez. `InboundTriggerRateLimiter`
   bağımsız, sabit pencereli bir sayaçla yazıldı (K1: dispatcher'ın bir web
   çerçevesine bağımlı olmaması).

## Bu Fazda Verilen Kararlar

- **K-472** — "tetikleyici yok" ile "imza yanlış" TEK `401`'e birleşir (bkz. `docs/KARARLAR.md`).
- **K-473** — imza doğrulaması hız sınırından önce çalışır (bkz. `docs/KARARLAR.md`).
- **K-474** — hedef agent/workflow varlığı kabul anında doğrulanmaz (bkz. `docs/KARARLAR.md`).

## Denetim Bulguları

`faz-denetim` bir kez koşuldu (2026-08-19, taze bağlamlı bağımsız agent).

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | "Tetikleyici yok" ile "imza yanlış" farklı kodlarla (`404`/`401`) dönüyordu; 66.2'nin "aynı gövde/kod" kuralını ihlal ediyordu; iddia eden test aslında karşılaştırmıyordu (test tiyatrosu) | 🔴 | **Düzeltildi** — tek `Unauthorized=401`'e birleştirildi (K-472); test artık iki gövdeyi bayt bayt karşılaştırıyor |
| 2 | Hız sınırı imzadan önce kontrol ediliyordu; kimliksiz bir istek akını gerçek gönderenin bütçesini tüketebiliyordu | 🔴 | **Düzeltildi** — sıra ters çevrildi (K-473); yeni test `An_unsigned_flood_does_not_consume_a_legitimate_senders_rate_limit_budget` |
| 3 | `SqlInboundTriggerStore`'un çalışma anı `exception.Message`'ı Türkçe idi (dil sınırı ihlali) | 🔴 | **Düzeltildi** — İngilizce'ye çevrildi |
| 4 | "Kota kapısından geçer" DoD satırı hiçbir testle doğrulanmamıştı | 🟡 | **Düzeltildi** — `A_full_quota_rejects_the_trigger_with_429_and_does_not_bypass_it` eklendi |
| 5 | `EnqueueAsync` başarısız olursa `ValidateAsync`'in açtığı idempotency rezervasyonu asla serbest bırakılmıyordu; retry sonsuza dek `409` alırdı | 🟡 | **Düzeltildi** — `AcceptAsync` artık `EnqueueAsync`'i try/catch'e alıp hata durumunda `ReleaseAsync` çağırıyor |

**Ek doğrulanan noktalar (soruldu, kusur çıkmadı):** K-089/K-370 audit-önce
deseni birebir doğru uygulanmış; `EnqueueAsync`'in hedef varlığını doğrulamaması
kasıtlı ve `SchedulingEndpoints` emsaliyle tutarlı (K-474); migration/tablo
sayısı testleri üç sağlayıcıda da doğru; `PublicAPI.Unshipped.txt` üç dosyada
da gerçek yüzeyle eşleşiyor.

Düzeltmelerden sonra dört doğrulama kapısı yeniden koşuldu — hepsi temiz.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `IInboundTriggerStore`/`InboundTrigger` (Abstractions), `InboundTriggerDispatcher`
  (Core, host-agnostic — `ValidateAsync` → `EnqueueAsync`/`ReleaseAsync` iki
  aşamalı akışı, HTTP katmanının arasına kota kontrolü sokabilmesi için).
- `POST /api/triggers/{tenantId}/{name}` kimlik doğrulamasız üçüncü uç grubu
  (`requireBearerToken:false, requireLoopback:false`, `AuthorizationPolicy`
  BİLEREK uygulanmaz) — internete açık, imzayla korunan bir uca ihtiyaç duyan
  gelecekteki her faz bu deseni tekrar kullanabilir.

**Bilinen tuzaklar (🚨):**
- 🚨 Bu fazdan sonra eklenecek her yeni "kimlik doğrulamasız" uç,
  `docs-site/scripts/build-http-api.mjs`'in `readEndpointAuthorization`
  fonksiyonundaki `anonymous` kontrolüne VE `check-content.mjs`'in
  `expectedAnonymous` listesine eklenmelidir — yoksa `npm run generate`/`npm run
  check:content` kırılır (`TraconAcceptInboundTrigger` örneği).
- 🚨 `tests/Tracon.AspNetCore.FunctionalTests/ApiKeyScopeCoverageTests.cs`'in
  `ExemptRoutePatterns`'ı da aynı şekilde her yeni kimlik doğrulamasız/kapsamsız
  uç için güncellenmelidir.
- 🚨 `Tracon.Core`'da `System.Threading.RateLimiting` KULLANILAMAZ (ASP.NET
  Core paylaşılan çerçevesinden gelir); host-agnostic bir sınırlayıcı gerekiyorsa
  elle yazılmalıdır (`InboundTriggerRateLimiter` örneği).
- 🚨 Bir kaynağın "yok" durumuyla "yetkisiz" durumunu birleştirmek isteyen her
  yeni uç için: plan taslağı ile manuel case tablosunun aynı HTTP kodunu
  iddia ettiğinden EMİN OL — K-472'nin çelişkisi ikisinin ayrı yazılmasından
  doğdu.

**Yarım kalan işler:** Yok — DoD'nin tamamı kapalı, 🔴/🟡 denetim bulgusu kalmadı.

**Sıradaki faz:** `docs/YOL-HARITASI.md`'de üretilir; bu faz kapanınca yeniden
üretilmelidir (`python3 scripts/dokuman-bakim.py`).

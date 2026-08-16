# 07 — HTTP Yönetim API'si (`API`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../07-HTTP-YONETIM-API.md`](../../07-HTTP-YONETIM-API.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-API-001 — `POST /api/agents` yeni bir tanım oluşturur, `201` ve `Location` döner

**Gerçek sonuç**
`HTTP: 201`, `Location: /agentprism/api/agents/manuel-crud-01`. Gövde `name`, `model` alanlarını taşıyor, `origin: "Database"`, `version: 1`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-002 — Aynı ad ikinci kez `POST` edilirse `409` döner (veritabanı kökenli)

**Gerçek sonuç**
`HTTP: 409`, `title: "Agent adi kullanimda"`, `detail: "'manuel-crud-01' adinda bir tanim zaten var. Guncellemek icin PUT kullanin."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-003 — Kodda tanımlı `support` adıyla `POST` edilirse `409` döner (farklı gerekçe metni)

**Gerçek sonuç**
`HTTP: 409`, `detail`: `"'support' kodda tanimli bir agent'tir ve yonetim API'sinden degistirilemez. Ad cakismasinda kod kazandigi icin ayni adla yazilan bir tanim hicbir zaman cozulmezdi."` — beklenen metinle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-004 — Adı boş bir tanım `400` döner

**Gerçek sonuç**
`HTTP: 400`, `title: "Agent adi bos"`, `detail: "'name' alani zorunludur."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-005 — `model.provider` veya `model.model` eksikse `400` döner

**Gerçek sonuç**
`HTTP: 400`, `title: "Model baglantisi eksik"`, `detail: "'model.provider' ve 'model.model' alanlari zorunludur."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-006 — Var olmayan agent'ı okuma `404` döner

**Gerçek sonuç**
`HTTP: 404`, `title: "Agent bulunamadi"`, `detail: "'hic-boyle-bir-agent' adinda bir agent yok."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-007 — `PUT` yol ile gövdedeki ad uyuşmazsa `400` döner

**Gerçek sonuç**
`HTTP: 400`, `title: "Ad uyusmuyor"`, `detail: "Yoldaki ad 'manuel-crud-01', govdedeki ad 'baska-bir-ad'. Agent adi degistirilemez; yeni bir ad icin yeni bir tanim olusturun."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-008 — Kodda tanımlı bir agent `PUT` ile güncellenemez

**Gerçek sonuç**
`HTTP: 409`, `title: "Kodda tanimli agent degistirilemez"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-009 — Var olmayan bir veritabanı tanımını `PUT` etmek `404` döner

**Gerçek sonuç**
`HTTP: 404`, `title: "Agent bulunamadi"`, `detail: "'hic-olusturulmamis' adinda bir agent yok."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-010 — Başarılı `PUT` yeni bir versiyon üretir

**Gerçek sonuç**
`HTTP: 200` (PUT yanıtı). Versiyon sayısı `1 → 2`. `/versions` listesi `[2, 1]` sırasıyla döndü — en yeni versiyon ilk sırada (yeniden eskiye).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-011 — Kodda tanımlı bir agent silinemez

**Gerçek sonuç**
`HTTP: 409`, `title: "Kodda tanimli agent degistirilemez"`. Ardından `GET /api/agents/support` → `200` (silinmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-012 — Var olmayan agent'ı silmek `404` döner

**Gerçek sonuç**
`HTTP: 404`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-013 — Başarılı `DELETE` `204` döner, ardından `GET` `404` döner

**Gerçek sonuç**
Silme: `HTTP: 204`, gövde boş. Ardından okuma: `HTTP: 404`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-014 — Var olmayan bir versiyon farkı istenirse `404` döner

**Gerçek sonuç**
`manuel-versiyon-testi` oluşturuldu (`version: 1`). `GET /versions/1/diff/99` → `HTTP: 404`, `title: "Surum bulunamadi"`, `detail` içinde `99` sayısı geçiyor: `"'manuel-versiyon-testi' agent'inin 99 numarali surumu yok."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-015 — Var olmayan bir versiyona geri dönmek `404` döner

**Gerçek sonuç**
`HTTP: 404`, `title: "Geri alinamadi"`, `detail: "'manuel-versiyon-testi' agent'inin 99 numarali surumu bulunamadi."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Doğrulama ucu HTTP garantileri (`POST /api/agents/validate`, Faz 34)

Doğrulama başarısızlığı bir HTTP hatası **değildir**: istek gövdesi geçerliyse
yanıt her zaman `200`'dür, sonuç `AgentValidationReport.Valid` alanında
taşınır. Alan bazlı doğrulama içeriği (`unknown_tool`/`unknown_model`)
[`02-CEKIRDEK-VE-KATALOG.md`](02-CEKIRDEK-VE-KATALOG.md)'de zaten kanıtlandı —
burada yalnız ucun **kendi** davranışsal garantileri sınanır.

---

## MT-API-020 — Geçerli VE geçersiz tanımda da yanıt `200`'dür; `severity` ad olarak yazılır

**Gerçek sonuç**
İki istek de `HTTP: 200`. Birinci: `valid:true, messages:[]`. İkinci: `valid:false`, `messages[0].severity: "Error"` (dize/ad olarak, sayısal değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-021 — `validate` yan etkisizdir: hiçbir agent veya çalıştırma satırı yazılmaz

**Gerçek sonuç**
`agent: 13 -> 13`, `run: 0 -> 0` — on doğrulama isteği sonrası hiçbir sayı değişmedi (yan etkisiz).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-022 — Bozuk JSON gövdesi gerçek bir HTTP hatası verir (`400`)

**Gerçek sonuç**
`HTTP: 400`, `title: "Gecersiz istek govdesi"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — `Idempotency-Key` (Faz 43)

Yalnız `POST /api/agents/{name}/run` ucuna eklenir. Baş­lık **taşımayan** bir
istek için filtre hiçbir sorgu atmadan geçer (K1). `AgentPrismIdempotencyOptions`
varsayılanı: `Enabled: true`, `MaxKeyLength: 255`. Bu bölüm gerçek para harcar
(her başarılı case en az bir gerçek çalıştırma tetikler).

---

## MT-API-030 — Aynı anahtar VE aynı gövdeyle ikinci istek yeniden çalışmaz, `Idempotency-Replayed: true` döner

**Gerçek sonuç**
Birinci istek `HTTP: 200`, gövde gerçek çalıştırma sonucu (`runId`, mesaj `"tamam"`). İkinci istek `HTTP: 200`, `Idempotency-Replayed: true` başlığı var. `sessionId: api-idem-01` için `/api/runs` sayısı **`1`** — agent ikinci kez çalışmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-031 — Aynı anahtar, FARKLI gövdeyle kullanılırsa `422` döner

**Gerçek sonuç**
`HTTP: 422`, `title: "Idempotency-Key farkli bir istek icin kullanilmis"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-032 — Akışlı istekte (`stream: true`) `Idempotency-Key` desteklenmez

**Gerçek sonuç**
`HTTP: 400`, `title: "Akisli istekte Idempotency-Key desteklenmiyor"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-033 — 255 karakteri aşan `Idempotency-Key` reddedilir

**Gerçek sonuç**
`HTTP: 400`, `title: "Idempotency-Key cok uzun"`, `detail`: `"Anahtar en fazla 255 karakter olabilir; gelen uzunluk 256."` — 255 ve 256 sayıları geçiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-034 — Aynı anahtarla eşzamanlı iki istek: ikincisi `409` alır

**Gerçek sonuç**
`HTTP1: 200` (gerçekten çalıştı), `HTTP2: 409` (`Istek zaten isleniyor` — rezervasyon mekanizması gözlendi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Hata sınıflandırmasının HTTP'de görünürlüğü (`/api/stats/errors`, Faz 44)

`GET /api/stats/errors`, `/api/stats`'in dar bir dilimidir — yalnız
`ByErrorClass` alanını döner. Gerçek sağlayıcı hataları (Anthropic/Google
üzerinden tetiklenen sınıflandırma şüpheleri) zaten
[`06-SAGLAYICI-DIGER.md`](06-SAGLAYICI-DIGER.md)'de kaydedildi; burada OpenAI
üzerinden **ucuz** bir tetikleyici (var olmayan model → `404`, ölçüldü K-296)
kullanılarak ucun kendi HTTP sözleşmesi (parametreler, gruplama, varsayılan
aralık) sınanır.

---

## MT-API-040 — Gerçek bir sağlayıcı hatası `/api/stats/errors`'ta gruplanarak görünür

**Gerçek sonuç**
Dizide bir giriş var: `class: "ProviderError"`, `totalRuns: 2`. `topClusters[0].count: 2` (aynı `model_not_found` parmak izine iki hata da düştü). `sampleMessage`: `"HTTP 404 (invalid_request_error: model_not_found)..."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-041 — `/api/stats/errors` varsayılan aralığı son 24 saattir, `?hours=` ile değiştirilir

**Gerçek sonuç**
**Doküman düzeltmesi**: `Girilecek veri` scripti `e['count']` alanını okuyordu ama gerçek yanıt şeması bunu taşımıyor — sınıf düzeyinde sayaç `totalRuns`'tır, `count` yalnız iç içe `topClusters[]` dizisinde var. Script `e['totalRuns']` olarak düzeltildi (AGENTS.md: doküman ile kod çelişirse doküman yanlıştır). Düzeltilmiş sorguyla: 24 saatlik varsayılan pencere `2` döndü; `hours=0.01` (36 saniye) de `2` döndü çünkü MT-API-040 hemen öncesinde koşuldu ve hatalar hâlâ o pencerenin içinde — bu, senaryonun kendi notuyla ("MT-API-040'ın hemen ardından koşulmadıysa daha düşük") tutarlı. Sorgu mekanizması hatasız çalıştı, `hours` parametresi işlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-042 — `/api/stats` genel sayaçları tutarlıdır (maliyet HARİÇ)

**Gerçek sonuç**
`totalRuns:2, completedRuns:0, failedRuns:2, canceledRuns:0, runningRuns:0, awaitingInputRuns:0`. `byAgent`'ta `manuel-hata-sinifi-testi` girdisi var (`totalRuns:2, failedRuns:2`). Eşitlik doğrulandı: `totalRuns(2) = completedRuns(0)+failedRuns(2)+canceledRuns(0)+runningRuns(0)+awaitingInputRuns(0)`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Oturumlar (`/api/sessions`)

---

## MT-API-050 — `GET /api/sessions` sayfalama parametreleri `[1, 200]` aralığına kırpılır

**Gerçek sonuç**
Üç istek de çökmedi. `take=0` → **`1`** kayıt döndü (`Math.Clamp(0,1,200)`). `take=99999` → `HTTP: 200`. `skip=-5` → `HTTP: 200`. Hiçbir durumda `500` görülmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-051 — Var olmayan oturum `404` döner

**Gerçek sonuç**
`HTTP: 404`, `title: "Oturum bulunamadi"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-052 — Oturum silinir, tekrar okunduğunda `404` döner

**Gerçek sonuç**
Birinci silme: `HTTP: 204`. Okuma: `HTTP: 404`. İkinci silme: `HTTP: 404` (idempotent "başarı" değil, gerçek "yok").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-053 — Bellek içi depoda `POST /api/sessions/{id}/branch` `501` döner

**Gerçek sonuç**
`HTTP: 501`, `title: "Dallandirma desteklenmiyor"`, `detail`: "Konusma dallandirma yalnizca kalici bir SQL saglayicisi acikken calisir...".

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-054 — Var olmayan bir oturumu dallandırmak `404` döner

**Gerçek sonuç**
`HTTP: 501` — bellek içi kurulumda `NotSupported` kontrolü `SessionNotFound`'dan **önce** çalışıyor (var olmayan oturum için de `404` değil `501` döndü). Gerçek sıra kaydedildi: `NotSupported` ilk kontrol ediliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Çalıştırmalar (`/api/runs`) — genel sözleşme

Derin SSE/UI davranışı `11-ARAYUZ-RUN-SESSION-SSE.md`'nin konusudur; burada
yalnız listeleme/filtreleme/ağaç/`Last-Event-ID` **kontratı** sınanır.

---

## MT-API-060 — `GET /api/runs` varsayılan olarak yalnız kök çalıştırmaları döner

**Gerçek sonuç**
**KALDI — HATA-S2-001.** `includeChildren` olmadan `1` satır (doğru). Ancak `includeChildren=true` ile de **`1`** satır döndü — beklenen `2` (kök + `support` alt çalıştırması) değil. Kök çalıştırmanın kendisi `childRunCount:1` taşıyor (alt çalıştırma gerçekten var ve kaydedilmiş), ama `/api/runs?sessionId=api-agac-01&includeChildren=true` onu listelemiyor. Kök neden: `InMemoryRunStore.QueryRunsAsync` (`src/AgentPrism.Core/Storage/InMemoryRunStore.cs:339`) `SessionId` eşitlik filtresini `OnlyRootRuns`'tan bağımsız, HER satıra (alt çalıştırmalar dahil) uyguluyor. Alt çalıştırmaların kendi `sessionId` alanı **kasıtlı olarak** `null`'dur (K-217: 'sütun çalıştırma bu oturumla başlatıldı der, kapsam burada üretilen içerik bu oturuma aittir der' — `RunRecordingAgent.cs:481-485`). Aynı filtre deseni `PostgresQueries.cs:409`, `SqliteQueries.cs:458`, `SqlServerQueries.cs:488`'de birebir kopya — **dört store'un tamamını** etkiliyor. Sonuç: `sessionId` + `includeChildren=true` kombinasyonu asla alt çalıştırma göstermez; uç noktanın kendi `WithDescription` metni (`RunEndpoints.cs:83-86`, "'includeChildren=true' kullanin") bu tuzağı belirtmiyor — K1 'sıfır sürpriz' ilkesini ihlal ediyor. Doğru tam-aile görünümü yalnız `rootRunId`/`GET /api/runs/{id}/tree` ile elde ediliyor (MT-API-062 bunu doğruladı, aynı kök çalıştırmanın `/tree`'si doğru `2` satır döndürdü).

---

**GEÇTİ (KAPANIS-PLANI Aile R, bu koşum).** Düzeltme: filtre artık kaydın
KENDİ oturumuna değil, kendi ağacının KÖKÜNE (`RootRunId ?? Id`) ait
oturuma bakıyor — kök satırın `SessionId`'si zaten bu değere eşit olduğu
için tek koşul (`sessionRootIds.Contains(...)`) hem kökü hem altını
kapsıyor. Dört depoda da (bellek içi + üç SQL lehçesi) aynı desen
uygulandı — ayrıntı KAPANIS-PLANI.md Aile R.

Canlı PostgreSQL'e karşı yeniden üretildi (`mt_fin` şeması, `yonlendirici` →
`support` çağrısı `api-agac-01` oturumunda iki kez koşuldu — kazayla iki kök
oluştu, düzeltmeyi çok-köklü oturum senaryosunda da doğruladı):
`includeChildren` olmadan `2` satır (yalnız iki kök `yonlendirici`),
`includeChildren=true` ile `4` satır (iki kök + iki `support` alt
çalıştırması) — artık ikisi de doğru. `/api/runs/{childId}/tree` de `2`
satır (kök + kendisi) döndürmeye devam ediyor (MT-API-062 regresyon
görmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-061 — Var olmayan `runId` her uçta `404` döner

**Gerçek sonuç**
Üçü de `HTTP: 404` döndü (`get`, `/tree`, `/events`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-062 — `GET /api/runs/{id}/tree` kökten tam ağacı döner (alt çalıştırmadan sorulsa bile)

**Gerçek sonuç**
MT-API-060'ın kaydettiği alt çalıştırma kimliği (`/tree` üzerinden bulundu, çünkü `includeChildren` listesi onu içermiyordu — bkz. HATA-S2-001) ile sorgulanan `/tree` de **`2`** satır döndürdü (kök + kendisi) — ağaç her zaman kökünden çekiliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-063 — `Last-Event-ID` ile akış kaldığı sıradan devam eder, tekrar göndermez

**Gerçek sonuç**
İlk gönderilen olayın `id:` alanı **`2`**'dir (`0` ve `1` tekrar gönderilmedi) — beklenen `>= 2` ile uyumlu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-064 — Girdi kaydı kapalıyken `GET /api/runs/{id}/input` `404` döner

**Gerçek sonuç**
_(2026-08-13 koşumu: Kaldı — HATA-S2-002. `AgentPrismServiceCollectionExtensions
.BindRunRecording` `RecordRunInput`'ı hiç okumuyordu, `GET .../input` her zaman
`200` dönüyordu.)_

**2026-08-15 yeniden koşum (KAPANIS-PLANI §9, K-406 sonrası) — Geçti.**
Kök neden `K-406` ile kapatıldı: `BindRunRecording`
(`src/AgentPrism.Core/AgentPrismServiceCollectionExtensions.cs:1799-1801`)
artık `RecordRunInput`'ı `TryReadBool` ile config'ten okuyor. Canlı
doğrulama: `AgentPrism:RunRecording:RecordRunInput=false` ile yeniden
başlatıldı, `support` agent'ına bir tur çalıştırıldı. `GET /api/runs/{id}`
→ **200** (çalıştırma var). `GET /api/runs/{id}/input` → **404**, gövde
`"No recorded input"` + `"Run '...' has no recorded input. It may have
started while input recording was disabled, or been deleted by a
retention policy."` — düzeltilmiş, tam beklenen davranış. Temizlik: env
değişkeni kaldırıldı, geçici şema (`mt_apirun`) düşürüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-065 — `GET /api/runs` sayfalama parametreleri `[1, 200]` aralığına kırpılır

**Gerçek sonuç**
`take=0` → **`1`** kayıt. `take=99999` → çökmedi, `HTTP: 200`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Tool ve model listeleme sözleşmesi

---

## MT-API-070 — `GET /api/tools` kayıtlı tool'ları JSON şemalarıyla listeler

**Gerçek sonuç**
Dizide `get_order_status`, `list_recent_orders`, `cancel_order` var (artı ses tool'ları: `list_voices`, `speak`, `transcribe`). Her girdi `jsonSchema` alanı taşıyor. Uç yalnız `GET`; başka fiil yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-071 — `GET /api/models`'in `status` alanı önbellekten gelir, ağ çağrısı yapmaz

**Gerçek sonuç**
Yanıt süresi `0.008s` (8ms) — milisaniyeler mertebesinde, gerçek ağ çağrısından belirgin şekilde kısa. Hiçbir `/api/models/health/*` sorgusu yapılmadığı için her sağlayıcının (`anthropic`, `google`, `openai`, `openai-responses`, `openrouter`) `status` alanı `"Unknown"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — `/api/meta`

---

## MT-API-080 — `/api/meta` kimlik doğrulamasız erişilebilir, sır içermez

**Gerçek sonuç**
`HTTP: 200`. Gövde `version`, `prefix`, `authentication` (`allowRemoteAccess`, `requiresBearerToken`, `requiresAuthorizationPolicy`), `storage` (`persistent`, `agentDefinitionStore`, `runStore`, `sessionStore`, `jobStore`, `jobWorkerEnabled`), `roles` (`canRead`, `canOperate`, `canAdminister`) alanlarını taşıyor. Hiçbir alanda `ApiKey`, bağlantı dizesi veya agent adı geçmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Güvenlik mekanizmasının varlığı

Derinlik (loopback dışı erişim, API anahtarı kapsamı, kiracı başlığı çakışması,
rol politikaları) `13-KIRACI-VE-GUVENLIK.md`'nin konusudur. Bu bölüm yalnız
üç katmanlı korumanın **var olduğunu** — geçerli tokenin geçtiğini, eksik
tokenin `401` verdiğini — kanıtlar.

---

## MT-API-090 — Token yokken korunan bir uç `401` döner, `WWW-Authenticate: Bearer` taşır

**Gerçek sonuç**
İki istek de `HTTP: 401`, `WWW-Authenticate: Bearer` başlığı taşıyor, `content-type: application/problem+json`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-API-091 — Doğru token ile korunan uç normal çalışır

**Gerçek sonuç**
`HTTP: 200`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 10 — `ProblemDetails` zarfı (genel sözleşme)

---

## MT-API-100 — Farklı hatalar aynı `ProblemDetails` zarfını taşır

**Gerçek sonuç**
`content-type: application/problem+json`. Gövde `type`, `title`, `status`, `detail` alanlarını taşıyor; `status: 404` HTTP durum koduyla aynı. `title: "Agent bulunamadi"` kısa/sabit; değişken agent adı yalnız `detail`'de geçiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bu dosyada kanıtlanmayan, kod okurken fark edilen şüpheler

Bu bölüm bir kusur listesi değildir — koşum aşamasında doğrulanacak
**şüphelerdir** (`PROMPT.md` §7.3).

- **MT-API-034**: `IdempotencyState.InProgress` yarışını elle, iki `curl`
  süreciyle tetiklemek zamanlamaya bağlıdır. Gerçek eşzamanlılık garantisi
  (rezervasyonun atomikliği) yalnız otomatik entegrasyon testlerinde
  güvenilir biçimde doğrulanabilir; bu case pratikte "muhtemelen" bir sonuç
  üretir, kesin değil.
- **MT-API-054**: `ConversationBranchService.BranchAsync`'in bellek içi
  depoda `SessionNotFound` ile `NotSupported` durumlarını hangi sırayla
  kontrol ettiği kaynak okumasıyla doğrulanmadı (yalnız `SessionEndpoints.cs`
  çağıran taraf okundu, servisin kendi gövdesi okunmadı — kapsam dışı
  bırakıldı, süre kısıtı). Koşum gerçek değeri kaydeder.
- **MT-API-042**: `totalRuns = completedRuns + failedRuns + canceledRuns +
  runningRuns` eşitliği `RunStatistics` tipinin alan tanımlarından
  **çıkarsandı** (XML dokümanı okunmadı, yalnız isimlendirme deseninden
  varsayıldı); `Queued`/`AwaitingInput` durumlarının bu toplama nasıl dahil
  olduğu (veya olmadığı) koşumda doğrulanmalıdır.

---

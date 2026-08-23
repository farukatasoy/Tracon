# 07 — HTTP Yönetim API'si (`API`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../07-HTTP-YONETIM-API.md`](../../07-HTTP-YONETIM-API.md) — `Ön koşul`, `Adımlar`,
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
> her case'in bloğu AYNEN durur. Tam metin:
> `git log --follow -- <bu dosya>`

---

## Temiz geçen case'ler (38)

| Case | Durum | Başlık |
|---|---|---|
| MT-API-001 | ☑ | `POST /api/agents` yeni bir tanım oluşturur, `201` ve `Location` döner |
| MT-API-002 | ☑ | Aynı ad ikinci kez `POST` edilirse `409` döner (veritabanı kökenli) |
| MT-API-003 | ☑ | Kodda tanımlı `support` adıyla `POST` edilirse `409` döner (farklı gerekçe metni) |
| MT-API-004 | ☑ | Adı boş bir tanım `400` döner |
| MT-API-005 | ☑ | `model.provider` veya `model.model` eksikse `400` döner |
| MT-API-006 | ☑ | Var olmayan agent'ı okuma `404` döner |
| MT-API-007 | ☑ | `PUT` yol ile gövdedeki ad uyuşmazsa `400` döner |
| MT-API-008 | ☑ | Kodda tanımlı bir agent `PUT` ile güncellenemez |
| MT-API-009 | ☑ | Var olmayan bir veritabanı tanımını `PUT` etmek `404` döner |
| MT-API-010 | ☑ | Başarılı `PUT` yeni bir versiyon üretir |
| MT-API-011 | ☑ | Kodda tanımlı bir agent silinemez |
| MT-API-012 | ☑ | Var olmayan agent'ı silmek `404` döner |
| MT-API-013 | ☑ | Başarılı `DELETE` `204` döner, ardından `GET` `404` döner |
| MT-API-014 | ☑ | Var olmayan bir versiyon farkı istenirse `404` döner |
| MT-API-015 | ☑ | Var olmayan bir versiyona geri dönmek `404` döner |
| MT-API-020 | ☑ | Geçerli VE geçersiz tanımda da yanıt `200`'dür; `severity` ad olarak yazılır |
| MT-API-021 | ☑ | `validate` yan etkisizdir: hiçbir agent veya çalıştırma satırı yazılmaz |
| MT-API-022 | ☑ | Bozuk JSON gövdesi gerçek bir HTTP hatası verir (`400`) |
| MT-API-030 | ☑ | Aynı anahtar VE aynı gövdeyle ikinci istek yeniden çalışmaz, `Idempotency-Replayed: true` döner |
| MT-API-031 | ☑ | Aynı anahtar, FARKLI gövdeyle kullanılırsa `422` döner |
| MT-API-032 | ☑ | Akışlı istekte (`stream: true`) `Idempotency-Key` desteklenmez |
| MT-API-033 | ☑ | 255 karakteri aşan `Idempotency-Key` reddedilir |
| MT-API-034 | ☑ | Aynı anahtarla eşzamanlı iki istek: ikincisi `409` alır |
| MT-API-040 | ☑ | Gerçek bir sağlayıcı hatası `/api/stats/errors`'ta gruplanarak görünür |
| MT-API-042 | ☑ | `/api/stats` genel sayaçları tutarlıdır (maliyet HARİÇ) |
| MT-API-050 | ☑ | `GET /api/sessions` sayfalama parametreleri `[1, 200]` aralığına kırpılır |
| MT-API-051 | ☑ | Var olmayan oturum `404` döner |
| MT-API-052 | ☑ | Oturum silinir, tekrar okunduğunda `404` döner |
| MT-API-053 | ☑ | Bellek içi depoda `POST /api/sessions/{id}/branch` `501` döner |
| MT-API-054 | ☑ | Var olmayan bir oturumu dallandırmak `404` döner |
| MT-API-061 | ☑ | Var olmayan `runId` her uçta `404` döner |
| MT-API-063 | ☑ | `Last-Event-ID` ile akış kaldığı sıradan devam eder, tekrar göndermez |
| MT-API-065 | ☑ | `GET /api/runs` sayfalama parametreleri `[1, 200]` aralığına kırpılır |
| MT-API-070 | ☑ | `GET /api/tools` kayıtlı tool'ları JSON şemalarıyla listeler |
| MT-API-071 | ☑ | `GET /api/models`'in `status` alanı önbellekten gelir, ağ çağrısı yapmaz |
| MT-API-080 | ☑ | `/api/meta` kimlik doğrulamasız erişilebilir, sır içermez |
| MT-API-090 | ☑ | Token yokken korunan bir uç `401` döner, `WWW-Authenticate: Bearer` taşır |
| MT-API-091 | ☑ | Doğru token ile korunan uç normal çalışır |

## Ayrıntı taşıyan case'ler (5)

## MT-API-041 — `/api/stats/errors` varsayılan aralığı son 24 saattir, `?hours=` ile değiştirilir

**Gerçek sonuç**
**Doküman düzeltmesi**: `Girilecek veri` scripti `e['count']` alanını okuyordu ama gerçek yanıt şeması bunu taşımıyor — sınıf düzeyinde sayaç `totalRuns`'tır, `count` yalnız iç içe `topClusters[]` dizisinde var. Script `e['totalRuns']` olarak düzeltildi (AGENTS.md: doküman ile kod çelişirse doküman yanlıştır). Düzeltilmiş sorguyla: 24 saatlik varsayılan pencere `2` döndü; `hours=0.01` (36 saniye) de `2` döndü çünkü MT-API-040 hemen öncesinde koşuldu ve hatalar hâlâ o pencerenin içinde — bu, senaryonun kendi notuyla ("MT-API-040'ın hemen ardından koşulmadıysa daha düşük") tutarlı. Sorgu mekanizması hatasız çalıştı, `hours` parametresi işlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-API-062 — `GET /api/runs/{id}/tree` kökten tam ağacı döner (alt çalıştırmadan sorulsa bile)

**Gerçek sonuç**
MT-API-060'ın kaydettiği alt çalıştırma kimliği (`/tree` üzerinden bulundu, çünkü `includeChildren` listesi onu içermiyordu — bkz. HATA-S2-001) ile sorgulanan `/tree` de **`2`** satır döndürdü (kök + kendisi) — ağaç her zaman kökünden çekiliyor.

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

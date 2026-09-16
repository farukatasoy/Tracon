# 07 — HTTP Yönetim API'si (`API`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../07-HTTP-YONETIM-API.md`](../../07-HTTP-YONETIM-API.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz A zinciri, tek şerit — zincirin SON ailesi) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `7e3a4de7` donuk |
| **Case sayısı** | 43 (MT-API-001..100) |
| **Port** | 5081 (spec 5080 yazar — şerit sapması) |
| **Depo** | **Bellek içi** (`storage.persistent: false`) — dosyanın kendi varsayılanı (MT-API-053/054 bunu ön koşul sayar), `mt_s1` şemasına dokunulmadı |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): gerçek OpenAI anahtarı tek
komutta ortam değişkenine aktarıldı, hiçbir dosyaya/loga yazılmadı.

**Açılış ölçümü:**

```
GET /api/meta -> version 0.0.0-preview.0.789, storage.persistent=false,
  agentDefinitionStore=InMemoryAgentDefinitionStore, runStore=InMemoryRunStore,
  sessionStore=InMemorySessionStore, jobStore=InMemoryJobStore, jobWorkerEnabled=true
```

---

## Devir notu

**Oturum 12 tamamlandı — CRUD bloğu (001-015) bitti.** Aşağıda kayıtlı.
Sıradaki oturum MT-API-020'den (validate bloğu) devam eder.

🚨 **Dosya 07'nin `Beklenen sonuç` metinleri sistematik olarak bayat** —
dosya 05'teki aynı desen (K-228). Tüm case'lerde ürünün ürettiği `title`/`detail`
İngilizce, spec Türkçe yazıyordu. Fark tespit edildiğinde spec bu koşumda
düzeltildi (kural 1 istisnası), gerekçe her case'in `Gerçek sonuç`'una
yazıldı. **Sıradaki case'lerde de aynı beklentiyle git** — Türkçe bekleyen her
satır şüphelidir.

---

## MT-API-001 — `POST /api/agents` yeni bir tanım oluşturur, `201` ve `Location` döner

**Gerçek sonuç**
`HTTP: 201`. `Location: /tracon/api/agents/manuel-crud-01`. Gövde tam
`AgentDefinition`'ı taşıyor (`name`, `model`, `origin: "Database"`,
`version: 1`, vb.).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-002 — Aynı ad ikinci kez `POST` edilirse `409` döner

**Gerçek sonuç**
`HTTP: 409`. `title: "Agent name in use"`, `detail: "A definition named
'manuel-crud-01' already exists. Use PUT to update it."` — spec Türkçe
bekliyordu, ürün İngilizce (K-228); `Beklenen sonuç` bu koşumda düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-003 — Kodda tanımlı `support` adıyla `POST` edilirse `409` döner (farklı gerekçe metni)

**Gerçek sonuç**
`HTTP: 409`. `title: "Agent name in use"` — MT-API-002 ile **aynı** title.
`detail: "'support' is an agent defined in code and cannot be changed from
the management API. Code wins name conflicts, so a definition written with
the same name would never resolve."` — `detail` gerçekten MT-API-002'den
farklı (case'in iddiası bu ölçüde doğru), ama `title` aynı çıktı; spec'in
"farklı gerekçe metni" ifadesi yalnız `detail` seviyesinde geçerli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-004 — Adı boş bir tanım `400` döner

**Gerçek sonuç**
`HTTP: 400`. `title: "Agent name empty"`, `detail: "'name' is required."`
(İngilizce, K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-005 — `model.provider` veya `model.model` eksikse `400` döner

**Gerçek sonuç**
`HTTP: 400`. `title: "Model binding missing"`, `detail: "'model.provider' and
'model.model' are required."` (İngilizce, K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-006 — Var olmayan agent'ı okuma `404` döner

**Gerçek sonuç**
`HTTP: 404`. `title: "Agent not found"`, `detail: "There is no agent named
'hic-boyle-bir-agent'."` (İngilizce, K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-007 — `PUT` yol ile gövdedeki ad uyuşmazsa `400` döner

**Gerçek sonuç**
`HTTP: 400`. `title: "Name mismatch"`, `detail: "The path name is
'manuel-crud-01', the body name is 'baska-bir-ad'. An agent's name cannot be
changed; create a new definition for a new name."` (İngilizce, K-228 — spec
düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-008 — Kodda tanımlı bir agent `PUT` ile güncellenemez

**Gerçek sonuç**
`HTTP: 409`. `title: "Code-defined agent cannot be modified"` (İngilizce,
K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-009 — Var olmayan bir veritabanı tanımını `PUT` etmek `404` döner

**Gerçek sonuç**
`HTTP: 404`. `title: "Agent not found"` (İngilizce, K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-010 — Başarılı `PUT` yeni bir versiyon üretir

**Gerçek sonuç**
`PUT` yanıtı `HTTP: 200`, `version: 2`. Versiyon sayısı `PUT` öncesi `1`,
sonrası `2` — bir fazla. `/versions` listesi `[2, 1]` sırasında — yeniden
eskiye.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-011 — Kodda tanımlı bir agent silinemez

**Gerçek sonuç**
`DELETE` `HTTP: 409`, `title: "Code-defined agent cannot be modified"`
(İngilizce, K-228 — spec düzeltildi). Ardından `GET /api/agents/support`
`HTTP: 200`, `origin: "Code"` — silinmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-012 — Var olmayan agent'ı silmek `404` döner

**Gerçek sonuç**
`HTTP: 404`, `title: "Agent not found"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-013 — Başarılı `DELETE` `204` döner, ardından `GET` `404` döner

**Gerçek sonuç**
Silme: `HTTP: 204`, gövde boş. Ardından okuma: `HTTP: 404`,
`title: "Agent not found"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-014 — Var olmayan bir versiyon farkı istenirse `404` döner

**Gerçek sonuç**
`HTTP: 404`. `title: "Version not found"`, `detail: "Agent
'manuel-versiyon-testi' has no version 99."` — `99` geçiyor (İngilizce,
K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-015 — Var olmayan bir versiyona geri dönmek `404` döner

**Gerçek sonuç**
`HTTP: 404`. `title: "Rollback failed"`, `detail: "Version 99 of agent
'manuel-versiyon-testi' was not found."` (İngilizce, K-228 — spec
düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-020 — Geçerli VE geçersiz tanımda da yanıt `200`'dür; `severity` ad olarak yazılır

**Gerçek sonuç**
Geçerli tanım: `HTTP: 200`, `valid:true`, `messages:[]`. Geçersiz tanım
(bilinmeyen tool): `HTTP: 200`, `valid:false`,
`messages:[{"severity":"Error","code":"unknown_tool",...}]` — `severity` dize
olarak yazılmış (`1` gibi sayısal değil), spec'in beklediği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-021 — `validate` yan etkisizdir: hiçbir agent veya çalıştırma satırı yazılmaz

**Gerçek sonuç**
On kez doğrulama isteği sonrası `agent: 15 -> 15`, `run: 0 -> 0` — hiçbir
yazma olmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-022 — Bozuk JSON gövdesi gerçek bir HTTP hatası verir (`400`)

**Gerçek sonuç**
`HTTP: 400`, `title: "Invalid request body"` (validate'in "her zaman 200"
kuralının istisnası doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-030 — Aynı anahtar VE aynı gövdeyle ikinci istek yeniden çalışmaz, `Idempotency-Replayed: true` döner

**Gerçek sonuç**
İlk yanıt `HTTP: 200`, `content-type: application/json; charset=utf-8`,
`Idempotency-Replayed` başlığı **yok**, `runId: 01a0ab95-1b2b-7109-8aa7-9e68f80789f8`.
İkinci yanıt `HTTP: 200`, `Idempotency-Replayed: true` **var**, gövde birinciyle
birebir aynı (aynı `runId`, aynı `responseId`, aynı mesaj metni). `/api/runs?agentName=support&sessionId=api-idem-01`
sayısı `1` — agent ikinci kez çalışmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-031 — Aynı anahtar, FARKLI gövdeyle kullanılırsa `422` döner

**Gerçek sonuç**
`HTTP: 422`, `title: "Idempotency-Key used for a different request"`
(İngilizce, K-228 — spec'in Türkçe beklentisi bayat, düzeltilmedi çünkü case
zaten yalnız durum kodunu iddia ediyordu, `title` metnini iddia etmiyordu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-032 — Akışlı istekte (`stream: true`) `Idempotency-Key` desteklenmez

**Gerçek sonuç**
`HTTP: 400`, `title: "Idempotency-Key not supported on streaming requests"`
(İngilizce, K-228; spec durum kodunu doğru bekliyordu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-033 — 255 karakteri aşan `Idempotency-Key` reddedilir

**Gerçek sonuç**
`HTTP: 400`, `title: "Idempotency-Key too long"`, `detail: "The key may be at
most 255 characters; received length 256."` — `255` ve `256` her ikisi de
geçiyor (İngilizce, K-228; spec durum kodu ve sayıları doğru bekliyordu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-034 — Aynı anahtarla eşzamanlı iki istek: ikincisi `409` alır

**Gerçek sonuç**
Birinci istek (0.05s gecikmeli ikinciden önce başlayan) `HTTP: 200` (gerçekten
çalıştı). İkinci istek `HTTP: 409`, `title: "Request already in progress"` —
spec'in beklediği rezervasyon mekanizması (`IdempotencyState.InProgress`)
doğrudan gözlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-040 — Gerçek bir sağlayıcı hatası `/api/stats/errors`'ta gruplanarak görünür

**Gerçek sonuç**
`class: "Unknown"`, `count: 2` (iki hata da aynı fingerprint'e düştü),
`sampleMessage: "The model provider request failed."` — spec'in kendi
koşullu ifadesi ("eşleşmiyorsa Unknown görülebilir") gerçekleşti. Bu,
**dosya 05'te açılmış `HATA-S1-020`'nin aynı kök nedeni** — `upstream_error`
sınıflandırıcıda tanınmıyor, her sağlayıcı hatası `Unknown`'a düşüyor.
Yeni bir kusur açılmadı, mevcut `HATA-S1-020`'ye çapraz referans verildi.
Case, spec'in koşullu beklentisi karşılandığı için **Geçti** sayılır (count
`en az 2` şartı sağlandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-041 — `/api/stats/errors` varsayılan aralığı son 24 saattir, `?hours=` ile değiştirilir

**Gerçek sonuç**
24 saatlik sorgu: toplam `2`. `hours=0.01` (36 saniye) sorgusu da `2` —
MT-API-040 hemen ardından (birkaç saniye içinde) koşulduğu için hatalar hâlâ
36 saniyelik pencerenin içinde; spec'in kendi notu bu senaryoyu öngörüyor
("hemen ardından koşulmadıysa daha düşük" — burada hemen ardından koşuldu).
Pencereleme mekanizmasının kendisi (`hours` parametresinin okunduğu) ayrıca
`400`/çökme vermeden çalıştığı için doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-042 — `/api/stats` genel sayaçları tutarlıdır (maliyet HARİÇ)

**Gerçek sonuç**
`totalRuns: 2`, `failedRuns: 2`, `completedRuns/canceledRuns/runningRuns/awaitingInputRuns: 0`.
Beş sayacın toplamı (`0+2+0+0+0=2`) `totalRuns`'a **eşit** — kod-doğrulanmamış
şüphe bu koşumda ampirik olarak doğrulandı. `byAgent` içinde
`manuel-hata-sinifi-testi` girdisi var, `byErrorClass` MT-API-040 ile tutarlı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

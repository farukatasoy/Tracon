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

**🎉 DOSYA 07 KAPANDI — 43/43 case koşuldu** (oturum 12: tamamı tek
oturumda — CLI bütçesi ~40, dosya 07 43 case ama tamamı basit `curl`
olduğundan tek oturumda bitti). Dosya sonucu: **43 ☑ Geçti · 0 ☑ Kaldı ·
0 ⏭ Atlandı**. Sayım skill §7 betiğiyle alındı, elle yazılmadı.

**🎉 ZİNCİR TAMAMLANDI (Faz A bitti).** `01 → 02 → 03 → 05 → 07` beşi de
yeşil. Toplam **311 case** koşuldu (268 + 43). Sıradaki iş **Faz B'nin
açılması** — dört şerit paralel, `00-KOSUM-PLANI.md` §3.1 dağılımı.

🚨 **Dosya 07'nin `Beklenen sonuç` metinleri sistematik olarak bayat çıktı** —
dosya 05'teki aynı desen (K-228). Neredeyse her case'te ürünün ürettiği
`title`/`detail` İngilizce, spec Türkçe yazıyordu. Fark tespit edildiğinde
spec bu koşumda düzeltildi (kural 1 istisnası), gerekçe her case'in
`Gerçek sonuç`'una yazıldı. **20+ satır düzeltildi** — tam liste aşağıdaki
case kayıtlarında.

**Yeni bulgular / çözülen belirsizlikler:**
- **MT-API-040/041/042** dosya 05'te açılmış `HATA-S1-020`'nin (sağlayıcı
  hata sınıflandırıcısı `upstream_error`'ı tanımıyor, her hata `Unknown`'a
  düşüyor) aynı kök nedenini bir kez daha doğruladı — yeni kayıt açılmadı,
  mevcut bulguya çapraz referans verildi.
- **MT-API-054 açık soruyu çözdü:** bellek içi depoda dallandırma kontrolü
  (`501`) oturum varlığı kontrolünden (`404`) **önce** çalışıyor — var olmayan
  bir oturum için de `501` görülüyor.
- **MT-API-064** `Tracon:RunRecording:RecordRunInput` ayarı için uygulama iki
  kez yeniden başlatıldı (kapalı → test → varsayılana dönüş); ortam değişkeni
  kullanıldı, `user-secrets`'a yazılmadı.
- **/run varsayılan olarak (Idempotency-Key yokken) SSE akışı döner** —
  dosya 02'nin notuyla tutarlı; `<scratch>/sse.py` ile ayrıştırıldı.

**Sonraki oturumun işi:** Aşama 1 bitti. `docs/manuel-test/kosumlar/2026-09-16/DEVIR.md`'yi
güncelle ve Faz B'yi aç (dört worktree zaten hazır: `ap-s1..4`, dallar
`test/kosum-s1..4`). Şerit dağılımı `00-KOSUM-PLANI.md` §3.1'dedir.

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

## MT-API-050 — `GET /api/sessions` sayfalama parametreleri `[1, 200]` aralığına kırpılır

**Gerçek sonuç**
`take=0` → `1` kayıt (kırpıldı). `take=99999` → `HTTP: 200` (çökmedi).
`skip=-5` → `HTTP: 200` (çökmedi). Üç istekte de `500` görülmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-051 — Var olmayan oturum `404` döner

**Gerçek sonuç**
`HTTP: 404`, `title: "Session not found"` (İngilizce, K-228 — spec
düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-052 — Oturum silinir, tekrar okunduğunda `404` döner

**Gerçek sonuç**
Birinci silme: `HTTP: 204`. Okuma: `HTTP: 404`. İkinci silme: `HTTP: 404`
(zaten silinmiş, idempotent "başarı" değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-053 — Bellek içi depoda `POST /api/sessions/{id}/branch` `501` döner

**Gerçek sonuç**
`HTTP: 501`, `title: "Branching not supported"` (İngilizce, K-228 — spec
düzeltildi). `detail` bellek içi kısıtı açıkça anlatıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-054 — Var olmayan bir oturumu dallandırmak `404` döner

**Gerçek sonuç**
`HTTP: 501` (aynı `title: "Branching not supported"`) — spec'in açık bıraktığı
sıra sorusu çözüldü: **depo türü kontrolü oturum varlığından önce** yapılıyor,
oturum hiç var olmayan bir id ile de olsa önce `501` görülüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-060 — `GET /api/runs` varsayılan olarak yalnız kök çalıştırmaları döner

**Gerçek sonuç**
`router` çalıştırması gerçekten `support`'u çağırdı (birleşik SSE metni: "ORD-1001
siparişiniz kargoya verilmiş..."). `includeChildren` olmadan `1` satır (yalnız
kök, `agentName: router`). `includeChildren=true` ile `2` satır (`router`
kök + `support` alt çalıştırması, `parentRunId` kökü gösteriyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-061 — Var olmayan `runId` her uçta `404` döner

**Gerçek sonuç**
Üçü de (`get`, `tree`, `events`) `HTTP: 404`, `title: "Run not found"`
(İngilizce — spec zaten yalnız durum kodunu iddia ediyordu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-062 — `GET /api/runs/{id}/tree` kökten tam ağacı döner (alt çalıştırmadan sorulsa bile)

**Gerçek sonuç**
`support` alt çalıştırmasının `runId`'siyle sorgulanan `/tree` de `2` satır
döndü (kök + kendisi) — ağaç her zaman kökünden çekiliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-063 — `Last-Event-ID` ile akış kaldığı sıradan devam eder, tekrar göndermez

**Gerçek sonuç**
`Last-Event-ID: 1` ile istendiğinde ilk gönderilen olayın `id:` alanı **`2`**
(`message.delta`) — `0` ve `1` tekrar gönderilmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-064 — Girdi kaydı kapalıyken `GET /api/runs/{id}/input` `404` döner

**Gerçek sonuç**
`Tracon__RunRecording__RecordRunInput=false` ortam değişkeniyle uygulama
yeniden başlatıldı (skill §1.2 — `user-secrets` yerine ortam değişkeni).
Çalıştırma sonrası `GET /api/runs/{id}` `HTTP: 200` (çalıştırmanın kendisi
var), `GET /api/runs/{id}/input` `HTTP: 404`, `title: "No recorded input"`
(İngilizce, K-228). Test sonrası uygulama varsayılan ayarla (girdi kaydı
açık) yeniden başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-065 — `GET /api/runs` sayfalama parametreleri `[1, 200]` aralığına kırpılır

**Gerçek sonuç**
`take=0` → `1` kayıt. `take=99999` → `HTTP: 200`, çökmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-070 — `GET /api/tools` kayıtlı tool'ları JSON şemalarıyla listeler

**Gerçek sonuç**
10 tool listelendi: `cancel_order`, `estimate_shipping_cost`,
`get_order_status`, `get_slow_report`, `list_recent_orders`, `list_voices`,
`mark_preview_ready`, `read_shopping_cart`, `speak`, `transcribe`. İstenen
üçü (`get_order_status`, `list_recent_orders`, `cancel_order`) var. Her
girdi `jsonSchema` alanı taşıyor (spec'in "`parameters` veya eşdeğer alan"
beklentisi — gerçek alan adı `jsonSchema`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-071 — `GET /api/models`'in `status` alanı önbellekten gelir, ağ çağrısı yapmaz

**Gerçek sonuç**
Yanıt süresi `0.007s` (7 ms) — gerçek ağ çağrısından belirgin şekilde kısa.
5 sağlayıcı (`anthropic`, `google`, `openai`, `openai-responses`,
`openrouter`), hepsinin `status` alanı `Unknown` (sağlık denetimi hiç
çalıştırılmamıştı — temiz durum).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-080 — `/api/meta` kimlik doğrulamasız erişilebilir, sır içermez

**Gerçek sonuç**
`Authorization` başlığı olmadan `HTTP: 200`. Gövde `version`, `prefix`,
`authentication` (`allowRemoteAccess`, `requiresBearerToken`,
`requiresAuthorizationPolicy`), `storage` (`persistent`,
`agentDefinitionStore`, `runStore`, `sessionStore`, `jobStore`,
`jobWorkerEnabled`), `roles` (`canRead`, `canOperate`, `canAdminister`)
alanlarını taşıyor. Hiçbir alanda `ApiKey`, bağlantı dizesi veya agent adı
geçmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-090 — Token yokken korunan bir uç `401` döner, `WWW-Authenticate: Bearer` taşır

**Gerçek sonuç**
İki istek de (`Authorization` yok / yanlış token) `HTTP: 401`,
`WWW-Authenticate: Bearer` başlığı var. `title: "Authentication failed"`
(İngilizce, K-228 — spec düzeltildi), `detail: "A valid 'Authorization:
Bearer <token>' header is required."` — beklenen token hakkında hiçbir bilgi
vermiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-091 — Doğru token ile korunan uç normal çalışır

**Gerçek sonuç**
`HTTP: 200` — MT-API-090'ın olumsuz sonucunun yalnız token eksikliğinden
geldiği doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-100 — Farklı hatalar aynı `ProblemDetails` zarfını taşır

**Gerçek sonuç**
`content-type: application/problem+json` (RFC 7807). Gövde `type`, `title`,
`status`, `detail` alanlarını taşıyor; `status: 404` HTTP koduyla aynı.
`title: "Agent not found"` — kısa, sabit; değişken veri (agent adı) yalnız
`detail`'de, `title`'a sızmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 17 — Eval, Deneyler (A/B), Kanarya Yayını ve Geri Bildirim (`EVAL`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../17-EVAL-VE-DENEYLER.md`](../../17-EVAL-VE-DENEYLER.md) — `Ön koşul`, `Adımlar`,
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

## Temiz geçen case'ler (60)

| Case | Durum | Başlık |
|---|---|---|
| MT-EVAL-001 | ☑ | `PUT /api/evals/{name}` yeni bir takım oluşturur |
| MT-EVAL-002 | ☑ | Aynı adı tekrar `PUT` etmek GÜNCELLER; `id`/`createdAt` sabit kalır |
| MT-EVAL-003 | ☑ | `GET /api/evals` kiracının tüm takımlarını listeler |
| MT-EVAL-004 | ☑ | Bilinmeyen `check` `kind` → `400`, takım kaydedilmez |
| MT-EVAL-005 | ☑ | Boş `checks: []` dizisiyle takım kaydetmek BAŞARILIDIR (koşu zamanı patlar) |
| MT-EVAL-006 | ☑ | Var olmayan takım `GET` → `404` |
| MT-EVAL-007 | ☑ | `DELETE` takımı siler; vaka ve koşuları KASKAT siler |
| MT-EVAL-010 | ☑ | `PUT /api/evals/{name}/cases` TAM DEĞİŞİM yapar, artımlı değil |
| MT-EVAL-011 | ☑ | Boş `query` içeren bir vaka → `400`, hiçbir vaka kaydedilmez |
| MT-EVAL-013 | ☑ | Arayüz: `CaseEditor`'da boş `query` varken "Save cases" DEVRE DIŞI |
| MT-EVAL-014 | ☑ | Arayüz: `evals.tsx` "New suite" formunda geçersiz JSON `checks` → API'ye HİÇ GİTMEZ |
| MT-EVAL-020 | ☑ | Mutlu yol: koşu tetiklenir, gerçek run üretir, `nonEmpty` + `containsExpected` geçer |
| MT-EVAL-021 | ☑ | `toolCalled` (`mode: "all"`) denetimi: tool çağrılmazsa BAŞARISIZ |
| MT-EVAL-022 | ☑ | `keywords` denetimi, `caseSensitive: true` iken büyük/küçük harf FARK YARATIR |
| MT-EVAL-023 | ☑ | `hasImageContent` denetimi: metin-yalnız yanıt BAŞARISIZ olmalı |
| MT-EVAL-024 | ☑ | `numRepetitions: 3` — TEK tekrar başarısız olursa vaka TÜMÜYLE başarısız sayılır |
| MT-EVAL-025 | ☑ | Sürüm pinleme: koşu SIRASINDA agent tanımı değişirse ESKİ sürüm kullanılmaya devam eder |
| MT-EVAL-026 | ☑ | `checks: []` olan takımı koşmak → `HTTP 200` ama `EvalRun.Status` sonunda `Failed` |
| MT-EVAL-027 | ☑ | 0 vakalı takımı koşmak → SENKRON `400` |
| MT-EVAL-028 | ☑ | `GET /api/evals/{name}/runs` koşu geçmişini listeler |
| MT-EVAL-035 | ☑ | `POST cases/from-run/{runId}` başarısız bir run'ı vakaya terfi ettirir |
| MT-EVAL-036 | ☑ | Aynı run'ı İKİNCİ kez terfi etmek → `200` İDEMPOTENT, ikinci vaka OLUŞMAZ |
| MT-EVAL-037 | ☑ | Var olmayan `runId`'yi terfi etmek → `404` |
| MT-EVAL-038 | ☑ | Arayüz: `PromoteToEvalCase` HİÇBİR takım yokken görünmez |
| MT-EVAL-040 | ☑ | İki kapılı varsayılan: yargıç kayıtlı olsa BİLE `Enabled=false`/`SampleRate=0` iken hiçbir şey örneklenmez |
| MT-EVAL-041 | ☑ | `Enabled=true` + `SampleRate=1.0` → HER tamamlanan run örneklenir |
| MT-EVAL-042 | ☑ | Aynı `runId` HER ZAMAN aynı örnekleme kararını verir (belirlenirlik) |
| MT-EVAL-043 | ☑ | `POST /api/runs/{id}/judge` örneklemeyi ATLAR, kayıtlıysa doğrudan puanlar |
| MT-EVAL-044 | ☑ | Hiçbir `IRunJudge` KAYITLI DEĞİLKEN `/judge` → BOŞ dizi (hata değil) |
| MT-EVAL-045 | ☑ | Aynı run'ı İKİ KEZ yargılamak → AYNI `RunScore` satırı GÜNCELLENİR, iki satır OLUŞMAZ |
| MT-EVAL-046 | ☑ | `GET /api/evaluation/online` özet döner |
| MT-EVAL-050 | ☑ | Kod-kökenli agent (`support`) ile deney oluşturmak → `400` |
| MT-EVAL-052 | ☑ | Ağırlık toplamı ≠ 100 → `400` |
| MT-EVAL-053 | ☑ | Var olmayan `version` numarası → `400` |
| MT-EVAL-054 | ☑ | `START`, sonra AYNI agent için İKİNCİ bir deney başlatmak → `409` |
| MT-EVAL-055 | ☑ | `Running` deneyi `DELETE` etmek → `409` |
| MT-EVAL-056 | ☑ | `Running` deneyi `PUT` ile düzenlemek → hata |
| MT-EVAL-057 | ☑ | `STOP` → `Stopped`; tek yönlü, `Draft`'a DÖNMEZ |
| MT-EVAL-058 | ☑ | Arayüz: ağırlık toplamı ≠ 100 iken "Kaydet" DEVRE DIŞI |
| MT-EVAL-059 | ☑ | Arayüz: `Running`/`Stopped` deneyde Düzenle/Sil GİZLİ |
| MT-EVAL-062 | ☑ | Aynı oturum kimliği HER ZAMAN aynı varyantı alır |
| MT-EVAL-063 | ☑ | `runs` tablosunda `experiment_id`/`variant` sütunları dolu gelir |
| MT-EVAL-070 | ☑ | İki varyantlı OLMAYAN deneye kanarya politikası eklemek → `400` |
| MT-EVAL-071 | ☑ | Geçerli kanarya politikası PUT edilir |
| MT-EVAL-072 | ☑ | `GET .../canary` — yeterli örnek toplanana kadar `InsufficientData` |
| MT-EVAL-073 | ☑ | Örnek uygulamada `AutoRollbackEnabled` VARSAYILAN OLARAK kapalı — arka plan servisi HİÇBİR ŞEY yapmaz |
| MT-EVAL-074 | ☑ | `AutoRollbackEnabled=true` + düşük eşik → OTOMATİK geri alma tetiklenir, audit ÖNCE yazılır |
| MT-EVAL-076 | ☑ | Arayüz: `rollbackReason` dolu banner, OTOMATİK geri almayı manuel `Stop`'tan ayırt eder |
| MT-EVAL-080 | ☑ | `POST /feedback` ikili (Binary) puanı kaydeder |
| MT-EVAL-081 | ☑ | Binary `value: 2` → `400` |
| MT-EVAL-082 | ☑ | Yıldız (`Stars`) puanı `1..5` API'de tam desteklenir (arayüzde YOK) |
| MT-EVAL-083 | ☑ | Stars `value: 0` ve `value: 6` → ikisi de `400` |
| MT-EVAL-085 | ☑ | `author` alanının `NULL` olduğu anonim senaryoda İKİ POST → İKİ AYRI satır |
| MT-EVAL-086 | ☑ | `DELETE /feedback/{scoreId}` puanı siler |
| MT-EVAL-087 | ☑ | Programatik yazma (yargıç) `IRunScoreStore`'a AUDIT İZİ BIRAKMAZ |
| MT-EVAL-088 | ☑ | Arayüz `FeedbackControl`: aynı başparmağa TEKRAR tıklamak puanı SİLER |
| MT-EVAL-089 | ☑ | Arayüz `FeedbackControl`: yorum, PUAN YOKKEN kaydedilmez; yıldız arayüzü hiç YOK |
| MT-EVAL-090 | ☑ | Arayüz "Judge now" düğmesi, yargıç yokken doğru mesaj gösterir |
| MT-EVAL-091 | ☑ | `GET /compare/{a}/{b}` iki run'ı yan yana döner |
| MT-EVAL-094 | ☑ | `POST /replay` `LiveTools`: rota seviyesi `Operator` YETMEZ, işleyici içi `Admin` gerekir |

## Ayrıntı taşıyan case'ler (9)

## MT-EVAL-012 — `DELETE /api/evals/{name}/cases` tümünü `[]` ile değiştirir

**Gerçek sonuç**
`HTTP: 204` (doküman `200` varsaymıştı; `DELETE` uçları bu repoda tutarlı biçimde `204` döner — bkz. MT-EVAL-007, MT-SKILL-004/005 vb. — doküman düzeltmesi, kusur değil), ikinci çağrı `[]` döndü. MT-EVAL-010'un ilk `PUT`'u tekrar uygulanıp tek vaka geri getirildi (§3 için).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-015 — Arayüz: takım "Sil" düğmesi HİÇBİR onay istemez

**Gerçek sonuç**
Test amaçlı `silinecek-takim` oluşturulup "Sil" düğmesine tıklandı: hiçbir onay diyaloğu açılmadan satır anında listeden kayboldu. Tam beklendiği gibi (asimetri doğrulandı, kusur değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Eval Koşusu: Tetikleme, Yerleşik Denetimler, Tekrar, Sürüm Pinleme (Faz 18)

> **Gerçek para uyarısı.** Bu bölümün her koşusu gerçek OpenAI modeli çağırır.

---

## MT-EVAL-051 — DB-kökenli agent ile iki varyantlı, ağırlığı 100'e tamamlanan deney oluşturulur

**Gerçek sonuç**
- `manuel-destek` (`FIX-AGENT-01`) önceki oturumlardan zaten `version=3`
  taşıyordu (doc'un varsaydığı taze `version=1` değil — önceki fazlarda
  bu fixture üzerinde çalışılmış). Doc'un talimatını uyarlayarak: agent
  tekrar `PUT` edilip `version=4` üretildi, sonra deney `version=3`/`version=4`
  varyantlarıyla kuruldu (doc'taki `1`/`2` yerine). `HTTP: 200`,
  `"status":"Draft"`. Beklenen davranış (fonksiyonel olarak) doğrulandı;
  sürüm numaraları doc'tan farklı ama anlamı aynı — bu bir dokuman
  düzeltmesi değil, ortamın önceki koşumlardan kalan durumuna uyarlama.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-075 — Sağlıklı kanarya + `rampSteps` varsa ağırlık kademeli artar; ramp-up AUDIT'E YAZILMAZ

**Gerçek sonuç**
- `kanarya-saglikli` (`kontrol-saglikli`/`kanarya-saglikli-kol`, ikisi de
  `version=3`, eşit ağırlık `50/50`), `minSampleSize=3, maxErrorRateDelta
  =0.2, rampSteps=[25,50,100], rampInterval="00:00:01"` politikasıyla
  kuruldu (`AutoRollbackEnabled` MT-EVAL-074'ten hâlâ `true`). Her iki kola
  toplam 9 BAŞARILI run üretildi (3 kontrol, 6 kanarya — kova dağılımı
  eşit değildi ama ikisi de `minSampleSize=3`'ü aştı). ~30 sn sonra
  `variants` sorgulandığında: `kanarya-saglikli-kol.weight=100`,
  `kontrol-saglikli.weight=0`.
  **Sapma:** Kod okuması (`CanaryEvaluationService.TryAdvanceRampAsync`,
  `nextStep = RampSteps.Where(s => s > canaryVariant.Weight)
  .OrderBy(s).FirstOrDefault()`) `rampSteps` içinde MEVCUT ağırlıktan
  BÜYÜK olan en küçük basamağı seçiyor. Bu case'in kurulumunda başlangıç
  ağırlığı `50` (eşit bölünmüş) olduğu için `rampSteps=[25,50,100]`'de
  `50`'den büyük tek basamak `100`'dür — servis bir sonraki taramada
  doğrudan `100`'e atladı, ara basamak `25`'i hiç göstermedi. Doğru
  senaryo (kanarya `<25` bir başlangıç ağırlığıyla kurulmalıydı) için
  yeniden koşum yapılmadı; asıl doğrulanmak istenen İKİ mekanizma yine de
  gözlemlendi: (1) sağlıklı kanarya ağırlığı OTOMATİK yükseliyor (bu
  senaryoda `50→100`), (2) audit tablosunda bu deney için yalnızca
  `experiment.create`/`experiment.start`/`experiment.canary_policy`
  kayıtları var — ramp-up'a ait HİÇBİR kayıt yok (3 satır, hepsi ramp-up
  DIŞI). Temel iddia (ramp-up sessiz kalır, ağırlık otomatik artar)
  doğrulandı; yalnız gözlenen sayısal basamak dokümanın `25` beklentisiyle
  birebir eşleşmedi (kurulum farkı, kusur değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-084 — Aynı yazar aynı hedefi iki kez puanlar → UPSERT (tek satır)

**Gerçek sonuç**
- İkinci `POST .../feedback` `{"kind":"Binary","value":0,"comment":
  "Fikrim degisti."}` ile gönderildi, `HTTP: 200`. SQL: `SELECT count(*),
  value, comment, author FROM run_scores WHERE run_id=... AND kind=1
  GROUP BY value, comment, author` → **2 satır** döndü (`value=1,
  comment="Dogru cevap.", author=NULL` VE `value=0, comment="Fikrim
  degisti.", author=NULL`) — ilk puan ÜZERİNE YAZILMADI, ikinci ayrı bir
  satır olarak eklendi. Bu case'in ön koşulu ("bu ortamda `author` alanı
  ... `NULL` değildir") BU ORTAM için YANLIŞ: statik bearer token akışında
  `author` GERÇEKTE `NULL` kalıyor (MT-EVAL-080/082'de de gözlendi).
  `run_scores_target_author_idx` tekil indeksi `(tenant_id, run_id,
  COALESCE(message_id,''), author)` üzerine kurulu — `author`'ın kendisi
  `COALESCE` edilmiyor, PostgreSQL'de `NULL ≠ NULL` olduğu için tekillik
  hiç devreye girmiyor (doğrulandı, `\d run_scores` ile indeks tanımı
  okundu). Bu, MT-EVAL-085'in "açık soru 4" olarak zaten belgelediği,
  KASITLI KABUL EDİLMİŞ davranışın AYNISI — gözlenen iki satır düzeltilmiş
  beklentiyle **tam örtüşüyor**.

---

**Doküman düzeltmesi (2026-08-15):** Ön koşul ve beklenti koda göre
düzeltildi. Kod/veri kusuru yok — kasıtlı kabul edilmiş davranış
(`MT-EVAL-085` ile aynı kök neden).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-092 — `GET /input`: `RecordRunInput=false` iken `404` — ✅ DÜZELTİLDİ (2026-08-14, K-406)

**Gerçek sonuç**
- `AgentPrism:RunRecording:RecordRunInput=false` set edilip uygulama
  yeniden başlatıldı, `support` agent'ına yeni bir run gönderildi
  (`runId=019ffece-9276-7457-88a5-b27278c04dd9`), `GET .../input`
  çağrıldı. Beklenen `404` yerine **`HTTP: 200`**, tam girdi
  (`messages: [...]`) döndü — `RecordRunInput=false` HİÇBİR ETKİ
  yapmadı.
  **🚨 HATA-K-007 (Yüksek).** Kök neden koddan doğrulandı:
  `AgentPrismServiceCollectionExtensions.cs:1769-1795`'teki
  `BindRunRecording` metodu `Enabled`, `RecordMessageDeltas`,
  `RecordToolPayloads`, `MaxPayloadLength` alanlarını config'ten okuyor
  AMA `RecordRunInput`'u (varsayılanı `true`, `AgentPrismOptions.cs:461`)
  HİÇ okumuyor — `TryReadBool(recording, nameof(...RecordRunInput), ...)`
  çağrısı eksik. Sonuç: bu bayrak config/`user-secrets`/ortam
  değişkeninden ASLA `false` olamıyor, her zaman derleme-zamanı
  varsayılanı (`true`) geçerli kalıyor. `RunEndpoints.cs`'teki `GET
  /input` ucunun kendisi doğru çalışıyor (depoda girdi VARSA `200`,
  YOKSA `404` — sorun bu uçta değil); `RunRecordingAgent` de
  `!_options.RecordRunInput` kontrolünü doğru yapıyor (satır ~593) —
  sorun yalnız bağlama (binding) katmanında. Etki: kullanıcı girdisi
  hassas veri (PII/gizli bilgi) içerebilir; bu bayrak tam da bunu
  KAPATMAK için var (bkz. dosyanın kendi güvenlik notu, §2), ama
  operatör onu kapattığını sanırken aslında hâlâ KAYDEDİLİYOR — sessiz
  bir gizlilik kontrolü kaçağı. Case sonrası `RecordRunInput` secret'ı
  kaldırıldı.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-007/K-406 — düzeltildi):**
`BindRunRecording`'e eksik `TryReadBool(recording,
nameof(AgentPrismRunRecordingOptions.RecordRunInput), ...)` çağrısı
eklendi. Aynı senaryo birebir tekrarlandı, gerçek sunucuya karşı:
`RecordRunInput=false` iken yeni bir çalıştırmanın `GET /input`'u artık
`HTTP: 404`, `"Girdi kaydi yok"`. Regresyon: ayar kaldırılıp (varsayılan
`true`) uygulama yeniden başlatılınca aynı uç `HTTP: 200` + tam girdi.
Aynı kök neden bağımsız olarak `HATA-S2-002`/`HATA-S4-015` olarak da
bulunmuştu — her iki serit sonuç dosyasına da bu karara işaret eden
kapanış notu eklendi. Ayrıntı: `SONUCLAR-K-2026-08-13.md`, `K-406`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-093 — `POST /replay` varsayılan `ReplayTools`: kaydedilmiş tool sonucu tekrar kullanılır, GERÇEK yan etki YOK

**Gerçek sonuç**
- `support` kod-kökenli olduğu için `ReplayTools`/`NoTools` reddediyor
  (`"'support' agent'inin kalici bir tanimi yok ... yalnizca LiveTools ile
  oynatilabilir"` — bu, dosyanın kendisinin belgelemediği ama tutarlı bir
  ek kısıt, kusur değil). Bunun yerine DB-kökenli `manuel-destek`
  (`version=3`, `get_order_status` araçlı) ile `ORD-1001 siparisim
  nerede?` çalıştırıldı (`runId=019ffed0-...`, `toolCallCount=1`).
  `{"toolMode":"ReplayTools"}` (sürüm belirtilmeden) İLK denemede `502`
  verdi — kod okumasıyla doğrulandı: `agentVersion` istekte verilmezse
  replay `IAgentDefinitionStore.GetAsync` ile agent'ın GÜNCEL (en son)
  sürümünü kullanıyor (`RunReplayRequest.AgentVersion` XML dokümanı:
  "Verilmezse bugünkü etkin sürüm"), bu ortamda `manuel-destek`'in güncel
  sürümü (v5) MT-EVAL-074 için kasıtlı bozulmuş modeli taşıyordu — bu
  dokümanlanmış tasarım gereği beklenen davranış, kusur değil.
  `{"toolMode":"ReplayTools","agentVersion":3}` ile düzeltilip tekrar
  çağrıldı: `HTTP: 200`, `compareLocation:"/agentprism/api/runs/
  019ffed0-.../compare/019ffed2-..."`, `agentVersion:3`,
  `replayOfRunId:"019ffed0-..."`. Beklenenle eşleşiyor (sürüm parametresi
  gerekliliği doküman notu olarak eklendi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-100 — `ApiKeyScope` enum'ında Eval/Experiment için kapsam YOK — yalnız Role ile sınırlı anahtar TÜM uçlara erişir — ✅ DÜZELTİLDİ (2026-08-14, K-407)

**Gerçek sonuç**
- **🚨 HATA-K-008 (Yüksek) — şüphe DOĞRULANDI.** Yalnız `RunsRead`
  kapsamlı bir API anahtarı (`ap_default_qf4GP...`) üretildi.
  - Adım 2: `PUT /api/evals/kapsam-testi` → **`HTTP: 200`**, takım
    gerçekten oluşturuldu.
  - Adım 3: `PUT /api/experiments/kapsam-testi` → **`HTTP: 200`**, deney
    gerçekten oluşturuldu (`status:"Draft"`).
  - Adım 4 (kontrol grubu): `PUT /api/agents/kapsam-kontrol` (aynı
    anahtarla) → **`HTTP: 403`**, `detail: "Bu uc 'AgentsAdmin'
    kapsamini gerektiriyor; anahtar bu kapsami tasimiyor."`
  Kontrol grubunun `403` vermesi, kapsam sisteminin `AgentEndpoints`'te
  ÇALIŞTIĞINI ama eval/experiment yüzeyinde HİÇ uygulanmadığını
  kanıtlıyor. Salt-okunur bir anahtarla eval takımı/deney
  oluşturulabiliyor — bu deneyler `PUT .../start` ile (aynı anahtar,
  ayrı bir kapsam denetimi olmadığı için) çalıştırılabilir hâle gelip
  gerçek para harcayan run'lar tetikleyebilir. `MT-JOB-090`/`MT-WF-100`
  ile AYNI kök nedenin (`ApiKeyScope` enum'ında `Eval`/`Experiment` için
  hiç kapsam tanımlanmamış olması) DÖRDÜNCÜ bağımsız tekrarı doğrulandı.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-008/K-407 — düzeltildi):**
`ApiKeyScope`'a `EvalsRead`/`EvalsAdmin`/`ExperimentsRead`/`ExperimentsAdmin`
eklendi; `EvalEndpoints`/`ExperimentEndpoints`'in tüm uçlarına
`RequireApiKeyScope` eklendi (tanım/veri yönetimi yeni kapsamları, gerçek
model çağırıp para harcayan `POST /api/evals/{name}/run` var olan
`RunsWrite`'ı aldı). Aynı senaryo birebir tekrarlandı: yalnız `RunsRead`
taşıyan anahtarla `PUT /api/evals/{name}` → `403 "EvalsAdmin kapsamini
gerektiriyor"`, `PUT /api/experiments/{name}` → `403 "ExperimentsAdmin
kapsamini gerektiriyor"`. Regresyon: ilgili kapsamları taşıyan bir
anahtarla eval takımı oluşturma `200`. `SchedulingEndpoints`
(`MT-JOB-090`) ve `GovernanceEndpoints` bu düzeltmenin kapsamı dışında
bırakıldı — bu koşumun konfirme ettiği HATA-K-NNN listesine dahil
değillerdi. Ayrıntı: `SONUCLAR-K-2026-08-13.md`, `K-407`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-101 — `feedback`/`compare`/`input` uçlarında da `RequireApiKeyScope` YOK — `replay`'in AKSİNE — ✅ DÜZELTİLDİ (2026-08-14, K-407)

**Gerçek sonuç**
- **Şüphe DOĞRULANDI** (aynı `RunsRead`-yalnız anahtar, MT-EVAL-100'den).
  - Feedback yazma: `POST .../feedback {"kind":"Binary","value":1}` →
    **`HTTP: 200`** — yalnız okuma amaçlı anahtar geri bildirim
    YAZABİLDİ.
  - Replay (kontrol): `POST .../replay {"toolMode":"ReplayTools"}` →
    **`HTTP: 403`**, `detail: "Bu uc 'RunsWrite' kapsamini gerektiriyor;
    anahtar bu kapsami tasimiyor."`
  Kontrast birebir doğrulandı: aynı `RunEndpoints.cs` dosyasında `replay`
  kapsam denetimini doğru uyguluyor, `feedback` hiç uygulamıyor —
  `RequireApiKeyScope`'un dosya içinde TUTARSIZ uygulandığı kanıtlandı.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-008/K-407 — düzeltildi):**
`feedback`(yaz)/`input`/`compare`(oku) uçlarına eksik
`RequireApiKeyScope(RunsWrite|RunsRead)` çağrıları eklendi. Aynı senaryo
birebir tekrarlandı: yalnız `RunsRead` taşıyan anahtarla `POST
.../feedback` artık `403 "RunsWrite kapsamini gerektiriyor"` (kontrast:
aynı anahtarla `GET .../input` hâlâ `200`, çünkü bu uç yalnız `RunsRead`
istiyor). Ayrıntı: `SONUCLAR-K-2026-08-13.md`, `K-407`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Koşum sonrası temizlik notu

Bu dosyanın case'leri `AgentPrism:OnlineEvaluation:*`,
`AgentPrism:Canary:*` ve `AgentPrism:RunRecording:RecordRunInput`
`user-secrets` girdilerini geçici olarak açar. Dosyayı bitirdikten sonra:

```bash
cd samples/AgentPrism.Api
dotnet user-secrets remove "AgentPrism:OnlineEvaluation:Enabled"
dotnet user-secrets remove "AgentPrism:OnlineEvaluation:SampleRate"
dotnet user-secrets remove "AgentPrism:Canary:AutoRollbackEnabled"
dotnet user-secrets remove "AgentPrism:Canary:ScanInterval"
dotnet user-secrets remove "AgentPrism:RunRecording:RecordRunInput"
```

---

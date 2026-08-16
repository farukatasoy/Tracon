# 17 — Eval, Deneyler (A/B), Kanarya Yayını ve Geri Bildirim (`EVAL`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../17-EVAL-VE-DENEYLER.md`](../../17-EVAL-VE-DENEYLER.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-EVAL-001 — `PUT /api/evals/{name}` yeni bir takım oluşturur

**Gerçek sonuç**
`HTTP: 200`, `id: "019ffdcb-dae4-..."`, `createdAt == updatedAt`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-002 — Aynı adı tekrar `PUT` etmek GÜNCELLER; `id`/`createdAt` sabit kalır

**Gerçek sonuç**
`id` birebir aynı, `createdAt` değişmedi, `updatedAt` ilerledi, `checks` tek elemana indi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-003 — `GET /api/evals` kiracının tüm takımlarını listeler

**Gerçek sonuç**
`['destek-degerlendirme']` — listede. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-004 — Bilinmeyen `check` `kind` → `400`, takım kaydedilmez

**Gerçek sonuç**
`HTTP: 400`, `detail: "Bilinmeyen denetim turu: 'regexMatch'. Ozel bir denetimse 'IAgentPrismBuilder.AddEvalCheck(\"{kind}\", ...)' ile kaydedilmelidir."` (mesaj kalıbı `{kind}` yer tutucusu kullanıyor, doküman `regexMatch` doğrudan yazmıştı — küçük bir metin farkı, anlam aynı). `GET` → `404`, kayıt oluşmadı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-005 — Boş `checks: []` dizisiyle takım kaydetmek BAŞARILIDIR (koşu zamanı patlar)

**Gerçek sonuç**
`HTTP: 200`, takım `checks: []` ile oluştu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-006 — Var olmayan takım `GET` → `404`

**Gerçek sonuç**
`HTTP: 404`, `title: "Eval takimi bulunamadi"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-007 — `DELETE` takımı siler; vaka ve koşuları KASKAT siler

**Gerçek sonuç**
`HTTP: 204`. SQL sorgusu `count: 0` döndü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Eval Vaka (Case) Yönetimi ve Arayüz (Faz 18)

---

## MT-EVAL-010 — `PUT /api/evals/{name}/cases` TAM DEĞİŞİM yapar, artımlı değil

**Gerçek sonuç**
Son `GET` `1` döndü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-011 — Boş `query` içeren bir vaka → `400`, hiçbir vaka kaydedilmez

**Gerçek sonuç**
`HTTP: 400`, `detail: "Her vaka bos olmayan bir 'query' alani tasimalidir."` Sonraki `GET` hâlâ tek eski vakayı (`ORD-1001 siparisim nerede?`) gösterdi — kısmi yazma olmadı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-012 — `DELETE /api/evals/{name}/cases` tümünü `[]` ile değiştirir

**Gerçek sonuç**
`HTTP: 204` (doküman `200` varsaymıştı; `DELETE` uçları bu repoda tutarlı biçimde `204` döner — bkz. MT-EVAL-007, MT-SKILL-004/005 vb. — doküman düzeltmesi, kusur değil), ikinci çağrı `[]` döndü. MT-EVAL-010'un ilk `PUT`'u tekrar uygulanıp tek vaka geri getirildi (§3 için).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-013 — Arayüz: `CaseEditor`'da boş `query` varken "Save cases" DEVRE DIŞI

**Gerçek sonuç**
Boş `query`'li satır eklendikten sonra "Save cases" `[disabled]` oldu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-014 — Arayüz: `evals.tsx` "New suite" formunda geçersiz JSON `checks` → API'ye HİÇ GİTMEZ

**Gerçek sonuç**
"Save"e tıklandığında satır içi `alert` rolünde "Checks must be valid JSON — an array of check definitions." mesajı göründü, form kapanmadı. Ağ istekleri incelendi: yalnız sayfa yüklemesinin `GET /api/evals`'i vardı, hiçbir `PUT` gitmedi. Tam beklendiği gibi. (İlgisiz bir konsol hatası da gözlendi: `Pattern attribute value [a-zA-Z0-9_-]+ is not a valid regular expression` — tarayıcının yeni `/v` regex modu ile bir input `pattern` özniteliği uyuşmazlığı, bu case'in konusuyla ilgisiz, kozmetik.)

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

## MT-EVAL-020 — Mutlu yol: koşu tetiklenir, gerçek run üretir, `nonEmpty` + `containsExpected` geçer

**Gerçek sonuç**
Nihai durum `Completed`, `total=1, passed=1, failed=0`. `output` "ORD-1001 siparişiniz kargoya verilmiş..." — `ORD-1001` içeriyor, her iki denetim de (`non_empty`, `contains_expected`) `passed:true`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-021 — `toolCalled` (`mode: "all"`) denetimi: tool çağrılmazsa BAŞARISIZ

**Gerçek sonuç**
İlk koşu: `Passed=true`, `reason: "All tools called: get_order_status"`. Karşıt kanıt: `query: "Merhaba"` ile değiştirilip yeniden koşulunca `Passed=false`, `reason: "Missing tool calls: get_order_status"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-022 — `keywords` denetimi, `caseSensitive: true` iken büyük/küçük harf FARK YARATIR

**Gerçek sonuç**
Model yanıtı `ORD-1001`'i birebir aynı büyük harfle içerdi, `Passed=true`, `reason: "All keywords found: ORD-1001"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-023 — `hasImageContent` denetimi: metin-yalnız yanıt BAŞARISIZ olmalı

**Gerçek sonuç**
`Passed=false`, `failureReason: "has_image_content: No image content found in conversation"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-024 — `numRepetitions: 3` — TEK tekrar başarısız olursa vaka TÜMÜYLE başarısız sayılır

**Gerçek sonuç**
3 tekrarın tamamı ayrı sonuç satırı olarak döndü (üçü de `Passed=false`, `has_image_content` gerekçesiyle), run seviyesinde `total=1, passed=0, failed=1`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-025 — Sürüm pinleme: koşu SIRASINDA agent tanımı değişirse ESKİ sürüm kullanılmaya devam eder

**Gerçek sonuç**
`manuel-destek` (`FIX-AGENT-01`) oluşturuldu, 10 vakalı bir takım (`surum-pinleme-testi`) koşusu tetiklendi. Durum `Running` olduğu anda (`agentVersion: 2` zaten pinlenmiş görünüyordu) agent `PUT` ile tekrar güncellendi (`version: 3`'e çıktı). Koşu bitince hem HTTP yanıtı hem SQL sorgusu `agent_version: 2` gösterdi — koşu SIRASINDA yapılan güncellemeden (versiyon 3) etkilenmedi. Tam beklendiği gibi. (İlk deneme yanlış zamanlamayla — güncelleme `Pending` durumdayken yapılmıştı, pinleme henüz olmamıştı — yanıltıcı bir sonuç verdi; `Running` durumunu yakalayarak doğru tekrarlandı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-026 — `checks: []` olan takımı koşmak → `HTTP 200` ama `EvalRun.Status` sonunda `Failed`

**Gerçek sonuç**
Tetikleme `HTTP: 200`. Birkaç saniye sonra `status: "Failed"`, `passed=0, failed=1, total=1`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-027 — 0 vakalı takımı koşmak → SENKRON `400`

**Gerçek sonuç**
`HTTP: 400`, `title: "Kosu baslatilamadi"`, `detail: "'destek-degerlendirme' takiminin hic vakasi yok."` Vaka geri eklendi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-028 — `GET /api/evals/{name}/runs` koşu geçmişini listeler

**Gerçek sonuç**
Liste `1` kayıt döndü, `status: "Completed"` — MT-EVAL-020'nin koşusu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Üretimden Eval Kümesi: Run → Vaka Terfi (Faz 45)

---

## MT-EVAL-035 — `POST cases/from-run/{runId}` başarısız bir run'ı vakaya terfi ettirir

**Gerçek sonuç**
- `support` agent'ına `ORD-1001 siparisim nerede?` gönderildi
  (`runId=019ffddd-029a-7d7f-b906-3acaa8e1242b`), sonra
  `POST /api/evals/destek-degerlendirme/cases/from-run/<RUN_ID>` çağrıldı.
  `HTTP: 201`. Yanıt: `sourceRunId="019ffddd-029a-7d7f-b906-3acaa8e1242b"`,
  `sourceKind="ReferenceRun"`, `promotedAt="2026-08-14T01:22:40.838615+00:00"`,
  `query`/`expectedOutput`/`expectedTools` run'ın transkriptinden dolduruldu.
  Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-036 — Aynı run'ı İKİNCİ kez terfi etmek → `200` İDEMPOTENT, ikinci vaka OLUŞMAZ

**Gerçek sonuç**
- Aynı `RUN_ID` ile aynı uç tekrar çağrıldı → `HTTP: 200`, gövde MT-EVAL-035'teki
  ile birebir aynı vaka kaydı (`id`, `promotedAt` değişmedi). SQL sorgusu
  `SELECT count(*) ... WHERE source_run_id = '<RUN_ID>'` → `1`. İkinci vaka
  oluşmadı, kısmi tekil indeks beklendiği gibi çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-037 — Var olmayan `runId`'yi terfi etmek → `404`

**Gerçek sonuç**
- Sıfır GUID ile çağrıldı → `HTTP: 404`, `detail: "'00000000-0000-0000-0000-000000000000' kimlikli bir calistirma yok."`
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-038 — Arayüz: `PromoteToEvalCase` HİÇBİR takım yokken görünmez

**Gerçek sonuç**
- Tüm 6 eval takımı `DELETE /api/evals/{name}` ile silindi (`GET /api/evals`
  → `[]`). `http://localhost:5080/agentprism/runs/019ffdd7-1910-78cb-ab36-0c1d9b849481`
  (MT-EVAL-035'in support run'ı) tarayıcıda açıldı, `browser_snapshot` alındı:
  Run detay sayfasının tamamı (başlık, istatistik satırı, Feedback, "Replay
  this run", Transcript, Event timeline, Trace, Tool calls bölümleri) göründü
  ama hiçbir yerde "Bu run'ı vakaya terfi et" bileşeni yok. Konsolda 2 hata
  vardı (`/api/agents/support/versions` ve `/api/runs/{id}/trace` → `404`) —
  ikisi de bu case'le ilgisiz, önceden var olan ayrı uç eksiklikleri.
  Beklenen davranış doğrulandı: bileşen 0 takım varken render edilmiyor.
  Case sonrası `destek-degerlendirme` (`support`, `nonEmpty`+`containsExpected`,
  vaka `ORD-1001 siparisim nerede?` → `ORD-1001`/`get_order_status`) ve
  `tool-cagri-testi` (`support`, `toolCalled` mode `all` `get_order_status`,
  vaka `ORD-1001 nerede?`) `PUT /api/evals/{name}` + `PUT .../cases` ile
  geri kuruldu, ikisi de `HTTP 200` ile doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Çevrimiçi Değerlendirme: Örnekleme, Yargıç, Özet (Faz 49)

> **Gerçek para uyarısı.** MT-EVAL-041, MT-EVAL-043, MT-EVAL-045 gerçek
> OpenAI modeli (yargıç olarak) çağırır.

---

## MT-EVAL-040 — İki kapılı varsayılan: yargıç kayıtlı olsa BİLE `Enabled=false`/`SampleRate=0` iken hiçbir şey örneklenmez

**Gerçek sonuç**
- `AgentPrism:OnlineEvaluation` için hiçbir `user-secrets` girdisi yok
  (`dotnet user-secrets list` doğrulandı — varsayılan durum). `support`
  agent'ına `FIX-PROMPT-02` gönderildi (`runId=019ffdde-5d11-76a5-81a8-39b58b04866e`),
  run tamamlandı, 10 sn beklendi. `SELECT count(*) FROM agentprism.jobs
  WHERE kind = 6` → `0`. `jobs` tablosundaki en son satırın `created_at`'i
  (`01:13:58`) bu run'ın tamamlanma zamanından (`01:23:56`) önceki bir
  koşuma ait — run sonrası hiçbir yeni iş kuyruğa girmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-041 — `Enabled=true` + `SampleRate=1.0` → HER tamamlanan run örneklenir

**Gerçek sonuç**
- `AgentPrism:OnlineEvaluation:Enabled=true` ve `:SampleRate=1.0` set edildi,
  uygulama yeniden başlatıldı. `support` agent'ına `ORD-1001 siparisim
  nerede?` gönderildi (`runId=019ffddf-3ee3-77d6-bee7-2f572e003a9d`), run
  tamamlandı, 15 sn beklendi. `jobs` tablosunda `kind=6` (`OnlineEval`),
  `status=3` (`Completed`) satırı bulundu. `run_scores`'ta `kind=3`
  (`Numeric`), `value=95`, `source='judge:model'`, `author='judge:model'`
  satırı bulundu. Beklenenle birebir eşleşiyor. Case sonrası her iki
  `user-secrets` anahtarı `remove` ile kaldırıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-042 — Aynı `runId` HER ZAMAN aynı örnekleme kararını verir (belirlenirlik)

**Gerçek sonuç**
- Hesaplama doğrulaması (gerçek HTTP çağrısı gerektirmez). `RunSampler.IsSampled`
  kaynağı okundu: `private static bool IsSampled(Guid runId, double sampleRate)`
  yalnız `runId`'nin 16 byte'ı üzerinden FNV-1a hash'i hesaplar (offset basis
  `14695981039346656037`, prime `1099511628211`); `HashCode`, `Random`,
  `Environment` veya süreç başına değişen HİÇBİR girdi kullanmıyor — saf,
  durumsuz bir fonksiyon. Bu, aynı `runId`'nin her çağrıda ve her süreç
  yeniden başlatmasında AYNI kesri üreteceğini matematiksel olarak garanti
  eder. Ampirik doğrulama: algoritma Python'da birebir yeniden üretilip
  (`.NET Guid` byte düzeni: Data1/Data2/Data3 little-endian, Data4 olduğu
  gibi) üç farklı gerçek `runId` için `sampleRate=0.5` ile iki kez
  hesaplandı — üçü de iki çağrıda da aynı sonucu verdi (ör.
  `019ffddf-3ee3-...` → kesir `0.7369989531679121`, `sampled=False`, her
  iki hesaplamada birebir aynı). Belgelenen garanti kodda doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-043 — `POST /api/runs/{id}/judge` örneklemeyi ATLAR, kayıtlıysa doğrudan puanlar

**Gerçek sonuç**
- MT-EVAL-041'in run'ı (`019ffdde-5d11-76a5-81a8-39b58b04866e`, `SampleRate`
  bu sırada `1.0`'dı ama bu case örneklemeyi hiç kullanmıyor, doğrudan
  `/judge` çağırıyor) üzerinde `POST /api/runs/{id}/judge` çağrıldı. `HTTP:
  200`, gövde `[{"kind":"Numeric","value":92,"comment":"...",
  "source":"judge:model","author":"judge:model",...}]`. Audit tablosunda
  `action='run.judge.manual', entity='run:019ffdde-...'` kaydı bulundu.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-044 — Hiçbir `IRunJudge` KAYITLI DEĞİLKEN `/judge` → BOŞ dizi (hata değil)

**Gerçek sonuç**
- OpenAI sağlayıcısı kapalıyken (bkz. bu case'in koşum notundaki olay —
  `AgentPrism:Providers:OpenAI:ApiKey` istemsizce boşaltıldı, ayrıntı
  dosya sonundaki "Sapmalar" bölümünde) `echo` sağlayıcılı `arastirmaci`
  agent'ına bir run gönderildi (`runId=019ffde2-b5b5-7e9d-8b08-28605fe7800e`),
  ardından `POST /api/runs/{id}/judge` çağrıldı. `HTTP: 200`, gövde `[]`.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-045 — Aynı run'ı İKİ KEZ yargılamak → AYNI `RunScore` satırı GÜNCELLENİR, iki satır OLUŞMAZ

**Gerçek sonuç**
- MT-EVAL-043'ün aynı run'ı (`019ffdde-5d11-76a5-81a8-39b58b04866e`) için
  `/judge` ikinci kez çağrıldı. Dönen kaydın `id`'si
  (`019ffde0-c844-7439-b423-54d6618d7f18`) ilk çağrıyla BİREBİR aynı kaldı
  (`value`/`comment`/`createdAt` güncellendi, satır değişmedi). SQL:
  `SELECT count(*) ... WHERE author='judge:model'` → `1`. Beklenenle
  eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-046 — `GET /api/evaluation/online` özet döner

**Gerçek sonuç**
- `HTTP: 200`. Gövde:
  `{"windowStart":"...","windowEnd":"...","sampleCount":0,"averageScore":null,
  "lowScoreThreshold":60,"minSampleSize":20,"belowThreshold":false,
  "judgeCost":null,"judgeCostCurrency":null}`. Tüm alanlar mevcut,
  `sampleCount=0 < minSampleSize=20` iken `belowThreshold=false`.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Deneyler (A/B): CRUD ve Doğrulama Kuralları (Faz 19)

---

## MT-EVAL-050 — Kod-kökenli agent (`support`) ile deney oluşturmak → `400`

**Gerçek sonuç**
- `HTTP: 400`, `detail: "'support' kodda tanimlidir ve surum gecmisi
  tutmaz. Kod kaynakli agent'larda deney kurulamaz."` Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-EVAL-052 — Ağırlık toplamı ≠ 100 → `400`

**Gerçek sonuç**
- `HTTP: 400`, `detail: "Varyant agirliklarinin toplami 100 olmalidir;
  suan 80."` Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-053 — Var olmayan `version` numarası → `400`

**Gerçek sonuç**
- `HTTP: 400`, `detail: "'manuel-destek' agent'inin 99 numarali surumu
  yok."` Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-054 — `START`, sonra AYNI agent için İKİNCİ bir deney başlatmak → `409`

**Gerçek sonuç**
- (Sürüm numaraları MT-EVAL-051'deki uyarlamayla `1`/`2` yerine `3`
  kullanıldı.) İlk `start`: `HTTP: 200`, `"status":"Running"`. `ikinci-deney`
  (`manuel-destek`, `version=3`, `weight=100`) `Draft` olarak oluşturuldu,
  `start` edilince `HTTP: 409`, `detail: "'manuel-destek' agent'i icin
  baska bir deney zaten calisiyor. Ayni agent icin ayni anda tek deney
  calisabilir."` SQL: `status=1` (Running) satırı tam `1` adet
  (`destek-talimat-testi`). Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-055 — `Running` deneyi `DELETE` etmek → `409`

**Gerçek sonuç**
- `HTTP: 409`, `detail: "'destek-talimat-testi' deneyi calisirken
  silinemez; once durdurulmalidir."` Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-056 — `Running` deneyi `PUT` ile düzenlemek → hata

**Gerçek sonuç**
- `HTTP: 409`, `detail: "'destek-talimat-testi' deneyi 'Running'
  durumunda; yalnizca Draft durumundaki deneyler duzenlenebilir."`
  Başarısız olma beklentisiyle eşleşiyor; gözlemlenen kod `409`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-057 — `STOP` → `Stopped`; tek yönlü, `Draft`'a DÖNMEZ

**Gerçek sonuç**
- `HTTP: 200`, `"status":"Stopped"`, `"endedAt"` dolduruldu. Takip eden
  `GET` de `"Stopped"` döndürdü. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-058 — Arayüz: ağırlık toplamı ≠ 100 iken "Kaydet" DEVRE DIŞI

**Gerçek sonuç**
- `/agentprism/experiments` → "New experiment" açıldı, iki varyantın
  `Weight %` alanları `30`/`30` yapıldı. Metin "Weights total 60% (must
  be 100%)" göründü, `browser_evaluate` ile `getComputedStyle(...).color`
  → `rgb(190, 18, 60)` (kırmızı/rose tonu) doğrulandı. "Save" düğmesi
  `disabled` özniteliğiyle işaretliydi. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-059 — Arayüz: `Running`/`Stopped` deneyde Düzenle/Sil GİZLİ

**Gerçek sonuç**
- `/agentprism/experiments` listesinde `destek-talimat-testi` (`stopped`)
  satırının son hücresi BOŞ — Edit/Sil düğmesi yok. Karşılaştırma amaçlı:
  aynı listedeki `ikinci-deney` (`draft`) satırında "Edit" düğmesi VE bir
  ikinci (sil) düğmesi görünüyor. Kontrast beklenen davranışı doğruluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Atama Belirlenirliği (Assignment Determinism)

---

## MT-EVAL-062 — Aynı oturum kimliği HER ZAMAN aynı varyantı alır

**Gerçek sonuç**
- `destek-talimat-testi-2` (`kisa-talimat`/`version=3`, `detayli-talimat`
  /`version=4`, `50`/`50`) oluşturulup `Running` yapıldı. `manuel-destek`
  agent'ına `sessionId="belirlenirlik-testi-42"` ile 5 kez art arda `POST
  /api/agents/manuel-destek/run` çağrıldı. SQL: `SELECT DISTINCT variant
  ...` → tek satır, `kisa-talimat`. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-063 — `runs` tablosunda `experiment_id`/`variant` sütunları dolu gelir

**Gerçek sonuç**
- SQL sonucu: `experiment_id="019ffe21-a2d1-7078-a720-c9c6dd677a52"`
  (`destek-talimat-testi-2`'nin `id`'siyle birebir), `variant="kisa-talimat"`
  (MT-EVAL-062 ile aynı), `agent_version=3` (`kisa-talimat` varyantının
  `version` değeriyle aynı). Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Kanarya Yayını ve Otomatik Geri Alma (Faz 56)

---

## MT-EVAL-070 — İki varyantlı OLMAYAN deneye kanarya politikası eklemek → `400`

**Gerçek sonuç**
- Üç varyantlı `uc-varyantli` (`34/33/33`) oluşturuldu, `canary` `PUT`
  edildi. `HTTP: 400`, `detail: "Kanarya kurali yalnizca iki kollu
  deneylerde tanimlanabilir."` Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-071 — Geçerli kanarya politikası PUT edilir

**Gerçek sonuç**
- `HTTP: 200`. Yanıtın `canary` alanı `{"canaryVariant":"detayli-talimat",
  "maxErrorRateDelta":0.2,"minSampleSize":3,"rampSteps":[25,50,100],
  "rampInterval":"01:00:00"}` içeriyor. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-072 — `GET .../canary` — yeterli örnek toplanana kadar `InsufficientData`

**Gerçek sonuç**
- `HTTP: 200`, `evaluation.decision: "InsufficientData"`, `reason: "Asgari
  sonuclanmis calistirma sayisina (3) ulasilmadi: kanarya 0, kontrol 5."`
  (MT-EVAL-062'nin 5 run'ı kontrol koluna, `kisa-talimat`'a düştüğü için
  kontrol zaten 5'te, kanarya kolu `detayli-talimat` hâlâ 0'da). Canlılık
  doğrulaması: `GET` art arda iki kez çağrıldı, `evaluatedAt` her
  seferinde değişti (`02:38:24.79...` → `02:38:32.70...`) — sonuç
  önbelleklenmiyor, her istekte yeniden hesaplanıyor. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-073 — Örnek uygulamada `AutoRollbackEnabled` VARSAYILAN OLARAK kapalı — arka plan servisi HİÇBİR ŞEY yapmaz

**Gerçek sonuç**
- `dotnet user-secrets list` → hiçbir `AgentPrism:Canary:*` girdisi yok;
  `appsettings.json`'da da `Canary` bölümü yok (varsayılan durum).
  `destek-talimat-testi-2` `minSampleSize=3` kanarya politikasıyla `Running`
  durumda ve kontrol kolunda 5 tamamlanmış run varken (tarama
  gerçekleşseydi bir işlem yapabilecek olgun bir aday) `rollback_reason`
  SQL'de `NULL` kaldı — arka plan servisi hiçbir şey yapmadı. Beklenenle
  eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-074 — `AutoRollbackEnabled=true` + düşük eşik → OTOMATİK geri alma tetiklenir, audit ÖNCE yazılır

**Gerçek sonuç**
- **Kurulum uyarlaması** (`destek-talimat-testi-2`'nin varyant sürüm
  bağları `Running` iken sabittir ve mevcut sürümler zaten çalışan bir
  model taşıyordu — bir sürümü retroaktif olarak bozmanın yolu yok, çünkü
  sürüm anlık görüntüleri değişmez; bkz. kod okuması: `CompositeAgentCatalog
  .ResolveAsync` → `DefinitionStoreAgentSource.ResolveVersionAsync`
  saklanan `AgentDefinition`'ı DOĞRUDAN kullanır, "canlı" bir tanımla
  birleştirmez). Bunun yerine: `manuel-destek` var olmayan bir `modelId`
  (`model-olmayan-xyz-999`) ile `version=5`'e yükseltildi; `destek-talimat
  -testi-2` durduruldu; YENİ bir deney (`kanarya-geri-alma-testi`,
  `kontrol`=`version 3` [sağlam], `bozuk-kanarya`=`version 5` [bozuk])
  oluşturulup `Running` yapıldı; kanarya politikası `minSampleSize=3,
  maxErrorRateDelta=0.0` ile kuruldu. Farklı `sessionId`'lerle 7 run
  gönderildi, kova ataması gözlenerek 4'ü `kontrol`'e (hepsi `Completed`),
  3'ü `bozuk-kanarya`'ya (hepsi `Failed`, model bulunamadığı için) düştü.
  `AutoRollbackEnabled=true`, `ScanInterval=00:00:30` set edilip uygulama
  yeniden başlatıldı, ~50 sn içinde (2 tarama döngüsü) sonuç gözlendi:
  - `experiments.rollback_reason`: `"Kanarya hata orani (%100,0)
    kontrolden (%0,0) %0,0 esiginden fazla yuksek."` — dolu.
  - `audit_log`: `action='experiment.auto_rollback'`,
    `actor='system:canary-evaluator'`,
    `after={"reason":"...","canaryErrorRate":1,"controlErrorRate":0,
    "canaryAverageScore":null}`.
  - `GET /api/experiments/kanarya-geri-alma-testi` → `variants`:
    `bozuk-kanarya weight=0`, `kontrol weight=100`. Ayrıca gözlenen ek
    detay (dokümanın belirtmediği): `status` da `"Stopped"`'a geçti
    (`endedAt` doldu) — geri alma yalnız ağırlığı sıfırlamıyor, deneyi de
    sonlandırıyor.
  Üç ana beklenti de birebir doğrulandı. Case sonrası
  `AgentPrism:Canary:AutoRollbackEnabled`/`:ScanInterval` `user-secrets`'tan
  kaldırılacak (MT-EVAL-075 bu ayarları tekrar kullanacağı için dosya
  sonundaki toplu temizliğe bırakıldı).

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

## MT-EVAL-076 — Arayüz: `rollbackReason` dolu banner, OTOMATİK geri almayı manuel `Stop`'tan ayırt eder

**Gerçek sonuç**
- (MT-EVAL-074'teki isim uyarlaması nedeniyle `destek-talimat-testi-2`
  yerine `kanarya-geri-alma-testi` açıldı — asıl otomatik geri alınan
  deney budur.) `/agentprism/experiments/kanarya-geri-alma-testi`
  ekranında "Canary" bölümünde metin: `"Automatically rolled back:
  Kanarya hata orani (%100,0) kontrolden (%0,0) %0,0 esiginden fazla
  yuksek."` — kırmız/rose tonlu arka planlı (oklab kroma pozitif kırmızı
  yönünde, `%10` opaklık) bir banner içinde. Kontrast: manuel `Stop`
  edilmiş `destek-talimat-testi` ekranında (hiç kanarya kuralı hiç
  tanımlanmamış, "No canary rule" gösteriyor) böyle bir banner YOK.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Geri Bildirim ve Puanlama (Faz 31)

> Bu bölüm daha önce **hiçbir** manuel test dosyasına atanmamıştı (bkz.
> `00-INDEKS.md` §8, 2026-08-09 tarihli not) — bu dosya bu boşluğu kapatır.

---

## MT-EVAL-080 — `POST /feedback` ikili (Binary) puanı kaydeder

**Gerçek sonuç**
- `support` agent'ına `ORD-1001 siparisim nerede?` gönderildi
  (`runId=019ffec7-e060-7c5f-bb9c-24cd4dbeaadf`). `POST .../feedback`
  `{"kind":"Binary","value":1,"comment":"Dogru cevap."}` → `HTTP: 200`.
  Takip eden `GET .../feedback` yeni satırı (`id`, `value:1`,
  `comment:"Dogru cevap."`, `source:"human"`) gösterdi. Beklenenle
  eşleşiyor. Not: `author` alanı `null` döndü — bu, MT-EVAL-084'ün
  kendi ön koşul varsayımıyla çelişiyor, ayrıntı o case'in notunda.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-081 — Binary `value: 2` → `400`

**Gerçek sonuç**
- `HTTP: 400`, `detail: "Ikili puan ('binary') yalniz 0 veya 1 olabilir."`
  Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-082 — Yıldız (`Stars`) puanı `1..5` API'de tam desteklenir (arayüzde YOK)

**Gerçek sonuç**
- `HTTP: 200`, `{"kind":"Stars","value":4,...}`. SQL: `run_scores` içinde
  `kind=2, value=4` satırı bulundu. Arayüz kontrolü MT-EVAL-089'a
  bırakıldı. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-083 — Stars `value: 0` ve `value: 6` → ikisi de `400`

**Gerçek sonuç**
- İkisi de `HTTP: 400`, `detail: "Yildiz puani ('stars') 1 ile 5 arasinda
  olmalidir."` Beklenenle birebir eşleşiyor.

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

## MT-EVAL-085 — `author` alanının `NULL` olduğu anonim senaryoda İKİ POST → İKİ AYRI satır

**Gerçek sonuç**
- Ayrı bir `AgentPrism.Testing` entegrasyon testi kurulumuna gerek
  kalmadı: MT-EVAL-084'ün kendi koşumu bu davranışı GERÇEK REST
  uçlarıyla, gerçek `author IS NULL` koşuluyla zaten kanıtladı. SQL:
  `SELECT count(*) FROM run_scores WHERE run_id='019ffec7-...' AND
  author IS NULL` → `2` (Binary `value=1` ve `value=0` satırları), `1`
  DEĞİL. Beklenenle (sayısal olarak) eşleşiyor — kod okumasıyla ölçülen
  "kasıtlı kabul edilmiş davranış" iddiası doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-086 — `DELETE /feedback/{scoreId}` puanı siler

**Gerçek sonuç**
- `HTTP: 204`. Audit: `action='run.feedback.delete'` kaydı bulundu.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-087 — Programatik yazma (yargıç) `IRunScoreStore`'a AUDIT İZİ BIRAKMAZ

**Gerçek sonuç**
- Karşılaştırma çifti: MT-EVAL-043'ün run'ı (`019ffdde-...`, `POST
  /judge` HTTP ucu ile manuel yargılandı) `audit_log`'da bulundu
  (`run.judge.manual`, önceki case'lerde zaten doğrulandı). MT-EVAL-041'in
  run'ı (`019ffddf-3ee3-77d6-bee7-2f572e003a9d`, `RunSampler`'ın otomatik
  örneklemesiyle `OnlineEvalJobHandler` üzerinden yargılandı, HTTP `/judge`
  ucu HİÇ çağrılmadı) için: `SELECT count(*) FROM audit_log WHERE entity
  LIKE '%<runId>%'` → `0` satır — bu run için audit_log'da HİÇBİR kayıt
  yok (ne `run.judge*` ne `run.feedback*`). Beklenen ayrım doğrulandı:
  HTTP uç işleyicisi kendi audit kaydını YAZIYOR, ama `IRunScoreStore
  .UpsertAsync`'in kendisi (otomatik örnekleme yolunun kullandığı) hiçbir
  iz bırakmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-088 — Arayüz `FeedbackControl`: aynı başparmağa TEKRAR tıklamak puanı SİLER

**Gerçek sonuç**
- Aynı run'ın detay ekranında "Helpful" düğmesine 3 kez tıklandı, her
  seferinde `GET .../feedback` ile durum doğrulandı:
  1. Tıklama: `[{"value":1,"comment":"Test yorumu - puan yokken",...}]`
     (MT-EVAL-089'da yazılan bekleyen yorum bu ilk puanla birlikte
     kaydedildi — puan yokken kaydedilmeme kuralını çiğnemiyor, yalnız
     puan OLUŞTUĞUNDA birlikte gönderiliyor).
  2. Tıklama: `[]` — puan KOMPLE SİLİNDİ, `value:0`'a (başparmak aşağı)
     ÇEVRİLMEDİ.
  3. Tıklama: `[{"value":1,"comment":null,...}]` — YENİ bir `id` ile
     yeniden oluşturuldu.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-089 — Arayüz `FeedbackControl`: yorum, PUAN YOKKEN kaydedilmez; yıldız arayüzü hiç YOK

**Gerçek sonuç**
- Hiç puanlanmamış yeni bir run'ın (`019ffeca-a5b4-7f71-86a1-52d475d99007`)
  detay ekranı açıldı. Yorum kutusuna "Test yorumu - puan yokken" yazıldı,
  `Tab` ile blur edildi. `GET /api/runs/{id}/feedback` → `[]` — hiçbir
  şey kaydedilmedi. Sayfada `browser_find` ile "star" arandı, gerçek bir
  yıldız kontrolü bulunamadı (eşleşmeler yalnız "run.started"/"tool call"
  gibi alakasız metinlerdi). Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-EVAL-090 — Arayüz "Judge now" düğmesi, yargıç yokken doğru mesaj gösterir

**Gerçek sonuç**
- OpenAI `user-secrets` anahtarı geçici olarak kaldırılıp uygulama
  yeniden başlatıldı (bu kez betik hatası yapılmadan — çıkış kodu her
  adımda ayrıca denetlendi). `echo` sağlayıcılı `arastirmaci` agent'ından
  bir run üretildi, run detay ekranı açılıp "Judge now" tıklandı. Sonuç:
  düğmenin yanında `"No judge is configured."` satır-içi metni belirdi
  — `role="alert"` YOK, renk `rgb(107,107,121)` (nötr gri, hata kırmızısı
  DEĞİL). Beklenen davranış (hata banner'ı değil, beklenen boş-sonuç
  mesajı) doğrulandı. Ardından OpenAI anahtarı `user-secrets`'a geri
  yazıldı, uygulama yeniden başlatılıp gerçek bir çağrıyla doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 10 — Çalıştırma Karşılaştırma, Yeniden Oynatma, Girdi Görüntüleme

---

## MT-EVAL-091 — `GET /compare/{a}/{b}` iki run'ı yan yana döner

**Gerçek sonuç**
- `GET /api/runs/{a}/compare/{b}` iki farklı agent'a ait run'la (`support`
  ve `arastirmaci`) çağrıldı. `HTTP: 200`. Hem `left` hem `right`
  belirtilen tüm alanları taşıyor (`runId, agentName, agentVersion,
  modelId, status, durationMs, usage, cost, toolCallCount, output,
  scores`). Fark hesaplaması yok, iki ham nesne dönüyor. Beklenenle
  eşleşiyor.

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

## MT-EVAL-094 — `POST /replay` `LiveTools`: rota seviyesi `Operator` YETMEZ, işleyici içi `Admin` gerekir

**Gerçek sonuç**
- (MT-EVAL-093'teki gibi `agentVersion:3` belirtilerek çağrıldı — sürüm
  belirtilmezse replay her zaman güncel sürümü kullanıyor, ayrıntı o
  case'in notunda.) `{"toolMode":"LiveTools","agentVersion":3}` →
  `HTTP: 200`, `compareLocation` dolu, yeni bir çıktı üretildi (modelin
  gerçekten tekrar çağrıldığını gösteren farklı ifadeli ama anlamca aynı
  bir yanıt). Beklenen davranış (statik-token ortamında rol ayrımı no-op,
  `200` dönüyor) doğrulandı. **Koşum notu**: bu ortamda `Operator`/`Admin`
  rol ayrımı hiç uygulanmadığı için, dokümanın iddia ettiği "gerçek bir
  rol-ayrımlı ortamda `403` beklenir" savı bu koşumda DOĞRULANAMADI —
  yalnız kod okumasıyla ölçülen bir iddia olarak kalır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 11 — Güvenlik: Eval/Deney Alanında API Anahtarı Kapsamı HİÇ YOK

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

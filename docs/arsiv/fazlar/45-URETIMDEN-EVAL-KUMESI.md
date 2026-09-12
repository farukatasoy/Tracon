# Faz 45 — Üretimden Değerlendirme Veri Kümesi Toplama

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-53**
> **Önkoşul:** [Faz 31](31-GERI-BILDIRIM-VE-PUANLAMA.md) — puan bazlı terfi `run_scores` tablosunu ister. Durum bazlı terfi Faz 31 olmadan da çalışır ([45.1](#451--faz-31-ne-kadar-önkoşul))
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli** — `eval_cases`'e üç sütun, üç set (K-178). Gerekçe [45.5](#455--terfi-eden-vakanın-kökeni-izlenir)
> **Public API:** büyüyor — `IEvalStore`'a bir metot, bir kayıt tipi. 🚨 Faz 7'den önce ucuz, sonra **kırıcı**

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/45-URETIMDEN-EVAL-KUMESI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 18 eval altyapısını **verdi**: `eval_suites`, `eval_cases`, `eval_runs` ve `eval_case_results` tabloları çalışıyor, MAF'ın `LocalEvaluator`'ı bağlı (K-139). Eksik olan şey altyapı değil, **veri**dir. Bugün bir eval vakası yazmanın tek yolu arayüzden elle doldurmaktır (K-143). Elle yazılan kümeler bayatlar: kimse ürünün üç ay önceki hatalarını hatırlamaz.

## Bitiş Ölçütleri (DoD)

- [x] 🚨 **Başarısız** bir çalıştırma terfi edilir ve oluşan vakanın
      `expectedOutput` alanı **boştur** — hata teste dönüşmez.
      `RunToCasePromotionEndpointTests.Basarisiz_calistirma_terfi_edilir_ve_expectedOutput_bostur`
      (kasıtlı olarak fırlatan bir `IChatClient` ile gerçek bir HTTP çalıştırması)
- [x] Başarılı bir çalıştırma referans olarak terfi edilir; `expectedOutput` ve
      `expectedTools` doğru dolar.
      `RunToCasePromotionEndpointTests.Basarili_calistirma_referans_olarak_terfi_edilir_ve_denetim_kaydi_yazilir`
      + `samples/Tracon.Api` ile canlı doğrulandı (bkz. Doğrulama komutları çıktısı)
- [x] Aynı çalıştırma ikinci kez terfi edilirse `200` döner ve **ikinci vaka
      oluşmaz**. `RunToCasePromotionEndpointTests.Ayni_calistirma_ikinci_kez_terfi_edilirse_200_doner_ve_ikinci_vaka_olusmaz`
      + `AddCaseAsync_ayni_source_run_id_ikinci_kez_mevcut_vakayi_doner` (`EvalStoreContract`)
- [x] Çok turlu çalıştırma `409` ile reddedilir ve mesaj sebebi yazar.
      `RunToCasePromotionEndpointTests.Cok_turlu_calistirma_409_ile_reddedilir`
- [x] Başka kiracının çalıştırması `404` döner.
      `RunToCasePromotionEndpointTests.Baska_kiracinin_calistirmasi_terfi_edilemez_AYNI_404_doner`
- [x] İki eş zamanlı terfi iki farklı `seq` üretir; benzersizlik kısıtı
      ihlal edilmez. `EvalStoreContract.AddCaseAsync_es_zamanli_terfiler_farkli_seq_uretir`
      (8 eşzamanlı `AddCaseAsync`), bellek içi + PostgreSQL + SQLite'ta geçti
- [x] Terfi edilen vaka gerçek bir eval koşusunda kullanılır ve sonuç üretir.
      `samples/Tracon.Api` ile canlı doğrulandı: `passed: 1, failed: 0`
      (bkz. Doğrulama komutları çıktısı)
- [x] `source_run_id` yabancı anahtar **taşımaz**; kaynak çalıştırma silinse
      bile vaka okunabilir kalır. Migration `0022_eval_case_source.sql` (ve
      SQL Server/SQLite eşleri) — `source_run_id` sütununda `REFERENCES` yok
- [x] Terfi `audit_log`'a yazılır. `eval.case.promoted` eylemi;
      `RunToCasePromotionEndpointTests` + canlı doğrulama
- [x] Sözleşme testleri bellek içi + üç SQL sağlayıcısında geçer. Bellek içi +
      PostgreSQL + SQLite ölçüldü (831/831, 429/429). 🚨 **SQL Server bu
      ortamda koşulamadı** — bkz. Sonraki Faza Devir Notu madde 6
- [x] Migration üç sette de uygulandı (K-178). PostgreSQL `0022`,
      SQL Server `0010`, SQLite `0010`
- [x] Dört doğrulama kapısı sıfır uyarı verir. `build`/`test`/`pack`/`format`
      dördü de yeşil (SQL Server testleri hariç, ortam kısıtı)
- [x] `samples/Tracon.Api` ile gerçek `run` → terfi → eval koşusu zinciri
      **uçtan uca** çalıştı; çıktı bu belgeye yazıldı (bkz. aşağı)
- [x] `secret` taraması boş döndü
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı **ölçüldü**: 157,0 KB gzip / 250 KB
      bütçe (önceki taban 151,3 KB — bu faz +5,7 KB ekledi)

### Gerçek sunucuyla doğrulama (samples/Tracon.Api, port 5091, SQLite + echo sağlayıcı)

```
$ curl -N -X POST .../agents/support/run -d '{"message":"12345 numarali siparisimin durumu ne?"}'
# runId: 019fdb7e-ed90-76a7-8d31-4201bdff565d, sessionId: null (oturumsuz)

$ curl .../runs/$RUN_ID/events | grep RunStarted
data: {...,"type":"RunStarted",...,"text":"12345 numarali siparisimin durumu ne?",...}

$ curl -X PUT .../evals/uretim-regresyon -d '{"agentName":"support","checks":[{"kind":"nonEmpty"}]}'
# 200, suiteId: 019fdb7f-5177-7620-881d-a9ef50abc251

$ curl -X POST .../evals/uretim-regresyon/cases/from-run/$RUN_ID
# HTTP/1.1 201 Created
# {"query":"12345 numarali siparisimin durumu ne?","expectedOutput":"Echo: 12345 ...",
#  "sourceKind":"ReferenceRun","sourceRunId":"019fdb7e-..."}

$ curl -X POST .../evals/uretim-regresyon/cases/from-run/$RUN_ID   # ikinci kez
# HTTP/1.1 200 OK — ayni id, vaka sayisi 1'de kaldi

$ curl .../audit?action=eval.case.promoted
# [{"action":"eval.case.promoted","entity":"eval_case:019fdb80-...", ...}]  — TEK kayit

$ curl -X POST .../evals/uretim-regresyon/run
# ... 3 sn sonra:
$ curl .../evals/runs/$EVAL_RUN_ID
# {"run":{"status":"Completed","total":1,"passed":1,"failed":0},
#  "results":[{"passed":true,"output":"Echo: 12345 numarali siparisimin durumu ne?",
#              "scores":[{"name":"non_empty","passed":true}]}]}
```

### Doğrulama komutları

```bash
# Basarisiz bir calistirma uret ve kimligini al
RUN_ID=$(curl -s -X POST http://localhost:5081/tracon/api/agents/kirik/run \
  -H 'content-type: application/json' \
  -d '{"messages":[{"role":"user","text":"merhaba"}]}' | jq -r '.runId')

# Takim olustur
curl -s -X PUT http://localhost:5081/tracon/api/evals/regresyon \
  -H 'content-type: application/json' \
  -d '{"agentName":"asistan","description":"uretimden toplanan"}' | jq

# Terfi et — 201 gelmeli
curl -s -X POST \
  "http://localhost:5081/tracon/api/evals/regresyon/cases/from-run/$RUN_ID" \
  -i | head -10

# 🚨 expectedOutput BOS olmali
curl -s http://localhost:5081/tracon/api/evals/regresyon/cases \
  | jq '.[] | {seq, query, expectedOutput, sourceRunId, sourceKind}'

# Ikinci terfi — 200 gelmeli, vaka sayisi ARTMAMALI
curl -s -X POST \
  "http://localhost:5081/tracon/api/evals/regresyon/cases/from-run/$RUN_ID" \
  -i | head -3
curl -s http://localhost:5081/tracon/api/evals/regresyon/cases | jq 'length'

# Denetim kaydi yazildi mi
curl -s "http://localhost:5081/tracon/api/audit?action=eval.case.promoted" | jq

# Terfi eden vaka gercek bir kosuda kullaniliyor mu
curl -s -X POST http://localhost:5081/tracon/api/evals/regresyon/run | jq
```

---

## Plandan Sapmalar

🚨 **En büyük sapma: planın "`query`, `run_events`'teki ilk kullanıcı mesajından
okunur" iddiası yanlıştı.** Ölçüldü (bölüm 45.4'ün kendisi bunu "muhtemelen"
diye yazmıştı, doğrulanmamıştı): `run_events` kullanıcının girdi metnini hiçbir
zaman taşımıyordu — yalnız model çıktısı (`MessageDelta`/`MessageCompleted`),
tool olayları ve durum geçişleri vardı. Uygulama bu yüzden iki turda ilerledi:

1. **İlk tur — oturum tabanlı okuma.** Çekirdek kayıt hattına (`RunRecordingAgent`)
   dokunmamak için `query`, çalıştırmanın oturumundan `ChatHistoryProvider`
   üzerinden okunacak şekilde tasarlandı (`/api/sessions/{id}`'nin kullandığı
   kanıtlanmış yol). Gerçek bir HTTP testiyle ölçüldü: `AgentEndpoints.AgentRunStream`
   `sessions.SaveSessionAsync(...)`'i YALNIZ başarı yolunda çağırıyor —
   başarısız bir çalıştırmanın oturumu hiç kaydedilmiyor. Bu, planın 🚨 işaretli
   **en önemli** DoD maddesini ("başarısız çalıştırma terfi edilir, `expectedOutput`
   boştur") yapısal olarak imkânsız kılıyordu.
2. **İkinci tur — `run_events`'e query eklendi (kullanıcı kararı, K-300).**
   Kullanıcıya iki seçenek sunuldu: kapsamı resmen daralt (yalnız başarılı+
   oturumlu terfi) veya çekirdek kayıt hattına dokunarak sorunu kökten çöz.
   Kullanıcı ikincisini seçti. `RunEventWriter.StartAsync` artık bir
   `string? query` parametresi alır ve `RunStarted` olayının `Text` alanına
   yazar; `RunRecordingAgent.RunCoreAsync`/`RunCoreStreamingAsync` bunu
   `messages`'ten (ilk `ChatRole.User` mesajı) SENKRON çıkarır — MEMORY.md'nin
   dört kez tekrarlanan `AsyncLocal`/`Activity.Current` tuzağına GİRMEZ, çünkü
   hiçbir `AsyncLocal` ataması yoktur, yalnız düz veri aktarımıdır.

Bu değişiklik, planın öngörmediği ama daha İYİ bir sonuç verdi: terfi artık
oturumsuz (sessionId'siz) çalıştırmalarda da çalışır — plan bunu hiç
düşünmemişti (bkz. K-300, K-301).

**İkinci sapma:** çok turluluk tespiti tam sohbet geçmişi okumadan yapıldı
(K-301) — plan bunun nasıl yapılacağını açık bırakmıştı (Açık Soru 3).
`query` artık `run_events`'ten geldiği için tam transkript gerekmiyor; yalnız
"bu `sessionId`'de bu çalıştırmadan ÖNCE başka bir çalıştırma var mı" sorusu
`IRunStore.QueryRunsAsync` ile cevaplanıyor.

**Üçüncü sapma:** planlanan `AddCaseAsync(Guid, EvalCaseDraft, CancellationToken) -> EvalCase`
imzası `EvalCaseAddResult { EvalCase Case, bool Created }` döndürecek şekilde
değişti — çağıranın (uç) 200 mü 201 mi döneceğini bilmesi gerekiyordu ve
taslak imza bunu ayırt edemiyordu.

Faz 31 bitmişti (Faz 44'te tamamlandı), bu yüzden puan bazlı terfi (`NegativeScore`)
ertelenmeden uygulandı. "Olumsuz puan" tanımı plan tarafından bırakılmıştı;
K-303 ile karara bağlandı: `Binary` için `Value == 0`, `Stars` için `Value <= 2`.

Açık Soru 2 **ölçüldü**: `EvalChecks.ContainsExpected`, `ExpectedOutput` `null`
iken FIRLATMAZ — sessizce `Passed = false` döner. Boş `expectedOutput` taşıyan
bir vaka (Failed/NegativeScore), takımı `containsExpected` denetimi
kullanıyorsa eval koşusunda her zaman "kaldı" görünür — bu doğru semantik
kabul edildi (vaka insan tarafından tamamlanmayı bekliyor demektir), B seçeneği
onaylandı. Regresyon testi: `EvalCheckRegistryTests.ContainsExpected_bos_ExpectedOutput_ile_daima_basarisiz_olur`.

## Bu Fazda Verilen Kararlar

- **K-300** — Terfi sorgusu `run_events`'teki `RunStarted.Text`'ten okunur (kullanıcı kararı)
- **K-301** — Çok turluluk tam geçmiş değil, "önceki çalıştırma var mı" sorgusuyla belirlenir
- **K-302** — `AddCaseAsync`'in eşzamanlılığı düz `INSERT` + `IsUniqueViolation` yakalama + yeniden deneme ile çözülür (`ON CONFLICT`/`MERGE` yok)
- **K-303** — "Olumsuz puan" eşiği: `Binary Value == 0` veya `Stars Value <= 2`

Tam gerekçeler `docs/KARARLAR.md`'de.

## Sonraki Faza Devir Notu

1. 🚨 **Aday listesindeki F-58 (GDPR silme) terfi eden kopyayı `eval_cases.source_run_id`
   üzerinden bulmalıdır.** Sütun yabancı anahtar TAŞIMAZ (K-178'in append-only
   deseni) — F-58 bunu `eval_cases.source_run_id = <silinen run>` eşleşmesiyle
   arayacak, JOIN ile değil.
2. **Aday listesindeki F-71 (çevrimiçi değerlendirme) aynı kaynağı kullanır**:
   örneklenmiş üretim çalıştırmaları. `RunToCasePromoter`'ın terfi mantığı
   (özellikle `ExtractOutputText` — `MessageCompleted`/`MessageDelta` birleştirme)
   doğrudan yeniden kullanılabilir; F-71 kendi puanlama akışını (`AIJudgeLoopEvaluator`
   veya benzeri) ayrıca yazmalıdır.
3. **Çok turlu terfi bu fazda `409` ile kapatıldı.** `EvalCase` sözleşmesinin
   çok turluluğu nasıl taşıyacağı (tüm geçmişi `context`'e mi yazacak, yoksa
   yeni bir alan mı açacak) yeni bir aday kalemidir.
4. 🚨 **`RunEventType.RunStarted.Text` artık kullanıcının girdi metnini taşıyor
   (K-300) — bu, çekirdek kayıt hattına eklenen YENİ bir davranıştır.** Bir
   sonraki `RunEventWriter`/`RunRecordingAgent` değişikliğinde bu alanın hâlâ
   doğru doldurulduğunu (özellikle akışlı/akışsız her iki dalda da) doğrulayın.
   Workflow çalıştırmaları (`RunKind.Workflow`) kapsam dışı bırakıldı — `Text`
   hep `null`'dır (`WorkflowRunner.cs`).
5. 🚨 **Keşfedilen, faz kapsamı DIŞINDA bir hata:** `PUT /api/evals/{name}`
   isteğinde `checks` alanı gövdeden atlanırsa (varsayılan `JsonElement` →
   `ValueKind = Undefined`) uç `500` döner (K-166'nın aynı deseni,
   `JsonElementConverter.Write` `InvalidOperationException` fırlatır).
   `samples/Tracon.Api` ile gerçek bir çağrıda ölçüldü, düzeltilmedi
   (Faz 18'in ucu, Faz 45'in kapsamı dışında). `EvalSuiteSaveRequest.Checks`'e
   bir varsayılan (`= default` yerine boş dizi) veya doğrulama eklenmeli.
6. **SQL Server entegrasyon testleri bu oturumda koşulamadı** — `docs/hafiza/sql-saglayicilari.md`'de
   zaten belgeli bir kısıt (Apple Silicon + Docker Desktop Rosetta emülasyonu,
   `mssql/server` yalnız `linux/amd64`). Kod PostgreSQL ve SQLite'ta ölçüldü;
   SQL Server sorgu metni ikisiyle birebir aynı desendedir ama gerçek bir
   SQL Server'a karşı hiç çalıştırılmadı — bir sonraki oturum (veya CI) bunu
   doğrulamalıdır.

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
>
> **Not:** Üç devir bilgisi zorunludur:
> 1. 🚨 Aday listesindeki **F-58** (GDPR silme) terfi eden kopyayı
>    `eval_cases.source_run_id` üzerinden bulmalıdır. Devir notu bunu açıkça
>    yazmalıdır.
> 2. Aday listesindeki **F-71** (çevrimiçi değerlendirme) aynı kaynağı
>    kullanır: örneklenmiş üretim çalıştırmaları. Devir notu, terfi mantığının
>    yeniden kullanılıp kullanılamayacağını değerlendirmelidir.
> 3. **Çok turlu terfi** bu fazda `409` ile kapatıldı. `EvalCase`
>    sözleşmesinin çok turluluğu nasıl taşıyacağı yeni bir aday kalemidir ve
>    Faz 7'den önce karara bağlanması ucuzdur.

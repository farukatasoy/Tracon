# Faz 194 — Workflow ve Kuyruk Yollarında Parametre Değerleri

> **Durum:** ✅ Tamamlandı (2026-10-12)
> **Plan onayı:** Bakımcı, 2026-10-12 (engelleyici kararlar sohbette alındı: kalem F-294; tam kapsam; workflow kendi şemasını bildirir; değer `run_inputs`'ta yeni sütunda; kuyruk yükünde alan; batch'te öğe başına nesne)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-294**
> **Önkoşul:** Yok — F-330 (K-890) ve [Faz 192](192-DONGU-TUKENME-POLITIKASI.md) kapandı. [Faz 193](../../193-NET8-NET9-DUSURME.md) konu bakımından bağımsızdır; sıra serbesttir
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.UI` · üretilen: `Tracon.Client`, `@tracon/client`
> **Yeni paket:** Yok · **Migration:** gerekli — `run_inputs.parameters` sütunu, üç sağlayıcıda; numara uygulama anında alınır (K-178)
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable` satırı
> **Tüketici yüzeyi:** site: `concepts/workflows.md`, `concepts/agents.md`, `concepts/runs.md`, `guides/background-work.md`, `capabilities.md` (`api/` ve `http-api/` üretilir) · sevk edilen: yeni okuma uzantısının XML `<example>`'ı, `src/Tracon.Workflows/README.md`, `CHANGELOG.md` `[Unreleased]` (batch öğe yorumunun değişikliği **adıyla**)
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md` · `docs/manuel-test/16-IS-KUYRUGU-VE-ZAMANLAMA.md` · `docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 197efd31:docs/arsiv/fazlar/194-WORKFLOW-VE-KUYRUK-PARAMETRELERI.md
> ```
>
> Damıtıldı 2026-10-12 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir agent run'ı bugün yalnız senkron `/run` yolunda parametre değeri taşır. Workflow run'ı, kuyruklu agent run'ı, batch öğesi, replay ve iki devam yolu değer taşımaz; F-330'dan beri bu yollar varsayılanı bağlar ya da düşer.

## Bitiş Ölçütleri (DoD)

- [x] `POST /tracon/api/workflows/{ad}/run` `parameters` ile koşar; adımın sistem istemi değeri taşır (test 1) — ✅ `WorkflowParameterBindingTests`; örnek uygulamada `WorkflowOutput` iki adımın talimatında değeri taşıdı
- [x] Geçersiz değer akış başlamadan agent `/run` ile aynı `400` gövdesini döner (test 2) — ✅ `WorkflowRunParameterGateTests`; örnek uygulamada `HTTP 400`, `missingParameters:["tone"]`
- [x] Eksik zorunlu katılımcı parametresinde run sıfır sağlayıcı çağrısıyla `Failed` biter (test 4) — ✅ `WorkflowParticipantCoverageTests` (`run` `Failed`, sıfır model çağrısı)
- [x] Resume ve respond kaynak değeri kullanır; kaynak `NULL` + parametreli tanım reddedilir (test 9-10) — ✅ `WorkflowParameterResumeTests` · `WorkflowParameterRespondTests`; kayıtsız kaynak reddedilir
- [x] Fonksiyon düğümü `context.GetRunParameters()` ile değeri okur (test 17) — ✅ `WorkflowFunctionParameterTests`; örnek uygulamada `[shimmer] Echo […]`
- [x] Kuyruklu agent run'ı `parameters` ve `documents` kabul eder; worker değeri kullanır (test 19-20) — ✅ `QueuedRunParameterTests`; örnek uygulamada worker değeri ve belgeyi kullandı
- [x] Yapılı batch öğesi öğe başına değer taşır; tek geçersiz öğe yalnız kendini düşürür (test 24-25) — ✅ `AgentBatchParameterTests` · `WorkflowBatchParameterTests` · `BatchItemInputTests`; örnek uygulamada bozuk öğe `Failed`, diğerleri `Completed`
- [x] Replay, çökme sonrası devam ve onay-devamı kayıtlı değeri kullanır (test 26-27) — ✅ `RunReplayParameterTests` · `RunContinuationTests` · `ApprovalResumeParameterTests`
- [x] `run_inputs.parameters` üç sağlayıcıda migration ile gelir; `NULL`/`{}` ayrımı sözleşme testinde (test 12) — ✅ `RunInputStoreContract`: SQLite 903, PostgreSQL 958 (+1 atlanmış, bu fazla ilgisiz), SQL Server 886 test yeşil
- [x] Retention `AwaitingInput` run'ın girdisini tutar (test 11) — ✅ `RetentionDataPlaneTests` (PostgreSQL) · `RetentionMaxRowsDataPlaneTests` (SQLite) · `RunInputRetentionTests` (SQL Server); mutasyon denetimi kırıyor
- [x] Değer log'da, span'de ve `run_events`'te düz görünmez (test 31) — ✅ `ParameterValueLeakTests`
- [x] Derleme maliyeti ölçüldü ve Açık Soru 7 karara bağlandı — ✅ 0,064 ms/çağrı (katalog önbellek isabeti 0,077 ms); Açık Soru 7 → B, K-897
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` — ✅ `python3 scripts/kapi.py kapanis --taban 12bf0b4c` — sonucu aşağıdaki "Kapanış kapıları" bölümünde
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (parametreli workflow + kuyruklu agent), çıktı belgeye yazıldı — ✅ bkz. *Örnek Uygulama Koşumu*
- [x] `secret` taraması boş döndü — ✅ `kapanis` ilk komutu `kapi.py tarama` (kapanış koşumunda boş döndü)
- [x] Manuel kabul case'leri üç alan dosyasına eklendi; otomatikleştirilebilenler koşuldu — ✅ 10 case: `MT-WF-127…131`, `MT-JOB-136…139`, `MT-CORE-141…142`; `136`, `137` örnek uygulamada koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — ✅ 1 🔴 gerçek, kapandı (bkz. *Denetim Bulguları*); kapıların hepsi yeniden koşuldu
- [x] `docs-site/` güncellendi (beş sayfa; `concepts/agents.md`'deki "replay ve devam reddedilir" paragrafı dahil); `npm run build` + `check-links.mjs` temiz — ✅ 13 site sayfası; `npm run check` yeşil (içerik, bağlantı, ağırlık, SEO, agent map)
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — ✅ `workflowDetail.parameters` iki dilde; bundle 197,7 KB gzip (bütçe 250 KB; payı 0,3 KB)
- [x] `CHANGELOG.md` `[Unreleased]` batch öğe yorumunu, kuyruk yükünün yeni alanlarını ve K-890 değişikliğini adıyla anar — ✅ `[Unreleased]` Added/Changed: batch öğe kuralı, kuyruk yükü alanları, K-890 değişikliği, webhook zarfı, judge
- [x] Kapanışta iki yeni aday açıldı: `jobs.payload` içerik koruması; batch öğe türü işareti (yalnız çakışma ölçülürse) — ✅ F-374 (`jobs.payload` koruması) ve F-375 (batch öğe türü işareti); denetimden F-376…F-381

### Doğrulama komutları

```bash
# Parametreli workflow run'ı (örnek uygulama, Operator rolü)
curl -sN -X POST http://localhost:5081/tracon/api/workflows/podcast/run \
  -H 'Content-Type: application/json' \
  -d '{"message":"Episode 12","parameters":{"tone":"formal","voice":"alloy"}}'

# Eksik zorunlu değer — akış başlamadan 400
curl -s -X POST http://localhost:5081/tracon/api/workflows/podcast/run \
  -H 'Content-Type: application/json' -d '{"message":"Episode 12"}'

# Kuyruklu agent run'ı — parametre + belge
curl -s -X POST http://localhost:5081/tracon/api/agents/writer/run \
  -H 'Prefer: respond-async' -H 'Content-Type: application/json' \
  -d '{"message":"Draft","parameters":{"tone":"formal"},"documents":[{"name":"brief.txt","content":"…"}]}'

# Kayıtlı değer (PostgreSQL; korumalı sütun şifreli görünmeli)
psql "$TRACON_PG" -c "SELECT run_id, parameters IS NOT NULL FROM tracon.run_inputs ORDER BY created_at DESC LIMIT 3;"
```

Yollar ve adlar örnek uygulamada uygulama anında kurulur; komutlar kapanışta
gerçek çıktıyla yeniden yazılır.

---

## Plandan Sapmalar

Numara `#N` ile verilir; sonraki fazlar bu numarayla atıf yapabilir.

| # | Plan | Gerçek | Neden |
|---|---|---|---|
| 1 | §194.1: kayıtta `DefaultValue`'nun `Kind`'a uygunluğu; §194.3: tür uyuşmazlığı adımı düşürür | **Yazılmadı.** `Kind` hiçbir yerde doğrulanmaz | `AgentParameterValidator.ValidateValues` agent şemasında da `Kind`'a bakmıyordu; site `Kind`'ı konsol etiketi diye anlatıyor. Workflow'a daha sıkı kural koymak iki şemayı tutarsız yapardı (K-897) |
| 2 | §194.3: derlenen agent run başına `(runId, agentName)` anahtarıyla tutulur | **Not yok.** Her çağrıda derlenir | Ölçüldü: derleme + dekorasyon **0,064 ms/çağrı**; katalog önbellek isabeti 0,077 ms. Sağlayıcı çağrısı saniyeler sürer. Açık Soru 7 → B |
| 3 | §194.5: workflow satırı `messages` = başlangıç mesajı | **`Messages = []`** yazar | Workflow motoru girdiyi içerik koruyucudan geçirmez; ham mesajı `run_inputs`'a yazmak koruyucuyu atlardı. Hiçbir tüketici workflow mesajını geri okumaz (K-898) |
| 4 | Plan yeni iç tip saymadı | `ParameterizedAgentFactory`, `ParameterValueSet`, `BatchItemInput` | Beş çağıran (`/run`, replay, devam, onay-devamı, workflow adımı) aynı derle + dekoratör sırasını ister. Bu sırayı beş yerde yazmak K-581'i beş kez bozma şansıdır |
| 5 | §194.9: replay `NULL` iken bugünkü ret | Workflow run'ının replay'i **400** döner ve resume'a yönlendirir | Workflow satırı mesaj taşımadığı için replay'i anlamsızdı; sessiz boş koşum yerine açık ret |
| 6 | §194.6: workflow resume/respond yeniden doğrular | Yapıldı; ek olarak `RecordRunInput=false` iken ret mesajı **ayarı adlandırır** (Açık Soru 5-A) | Operatör neyi açacağını mesajdan bilir |
| 7 | Kuyruk işleyicisi değeri yalnız varsa bağlar | Değer yokken de `UnboundParameterGuard` varsa bağlar | K-890: değersiz zorunlu parametre yine düşmeli, sessizce açılmamalı |
| 8 | `RunReplayService` yalnız değer ekler | Katalog yolunda dekoratörlü fabrika yeniden kurulur | Parametreli derleme önbelleği atlar; dekoratör zinciri yeniden uygulanmazsa bütçe ve onay düşerdi (K-581) |
| 9 | Retention: "satırı tutar" | Satır sınırı ifadesi `CASE WHEN EXISTS … THEN NULL` ile `AwaitingInput` satırını **saymaz** | K-888 deseni: yoksa sınır tutulan satırı silmeye çalışır. Mutasyon denetimi yapıldı: düzeltme kaldırılınca retention testi kırılıyor |
| 10 | §194.10: konsol yalnız gösterir | Run düğmesi zorunlu değer eksikken **kapalıdır** | `400`'ü kullanıcıya atmak yerine alan boş kalmasın |
| 11 | Örnek uygulama gerçek sağlayıcıyla koşar | Yeni `EchoModelProvider` (`echo-instructions-1`) ve yalnız Echo kipinde kayıtlı `podcast-writer`, `podcast-editor` | Anahtarsız koşum talimatı yanıta yazar; değerin modele ulaştığı gözle görülür |
| 12 | Plan `K-896…` rezerve etmedi | `K-897`, `K-898`, `K-899` | `K-896` F-332'ye aitti. Manuel case'lerde ilk yazılan numara düzeltildi |
| 13 | Plan `GET /api/runs/{id}/input` için bir şey demedi | Workflow satırı için artık **200 + boş mesaj listesi** döner | Satır var, mesaj yok. 404 "kayıt yok" demek olurdu ve yanlıştır (K-898) |

Açık Sorular: 1-A (`GetRunParameters()` run dışında `InvalidOperationException`) ·
2-A (yeni `ProtectedColumn.RunInputParameters = 10`) · 3-A (yükseltme anında
bekleyen parametreli onay-devamı reddedilir) · 4-A (bilinmeyen batch üst alanı
öğeyi düşürür) · 5-A (kayıt kapalıyken parametreli workflow resume edilmez) ·
6-A (kuyruk belgesinde boyut sınırı yok, eşzamanlı yolla aynı) · 7 (ölçüldü, B).

## Bu Fazda Verilen Kararlar

Üç karar `docs/KARARLAR.md`'ye girdi:

- **K-897** *(public-api)* — workflow parametre şeması, kapsama kuralı, `GetRunParameters()`, `Kind` doğrulanmaz
- **K-898** *(kalıcı-veri, public-api, güvenlik)* — `run_inputs.parameters`, `NULL` ≠ `{}`, `ProtectedColumn.RunInputParameters`, retention istisnası, K-890'ın replay/devam maddesinin değişmesi
- **K-899** *(güvenlik, public-api)* — kuyruk yükü ve yapılı batch öğesi, `jobs.payload` korumasız (kullanıcı kararı)

Faz içinde kalan yerel tercihler:

- Alt agent çağrısına değer geçirilmez: yalnız workflow'un **doğrudan** adımı alır. Adımın kendi alt agent'ı varsayılanla bağlanır.
- Magentic yöneticisi ve katılımcıları aynı süzmeyi alır: bağlama `ChildAgentInvoker` seviyesindedir, desene bağlı değildir.
- Fonksiyon düğümü sözlüğü salt okunurdur ve nihai settir (varsayılanlar uygulanmış).

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, **Echo** sağlayıcıyla (`echo-instructions-1`, talimatı yanıta yazar) ve
SQLite çalışma veritabanıyla koştu. Çıktılar gerçektir; `runId` değerleri kısaltılmadan
korundu.

| Komut | Çıktı |
|---|---|
| `PUT /api/workflows/podcast` (`parameters`: `tone` zorunlu, `voice` varsayılan `alloy`) | `{"name":"podcast","version":1,"parameters":["tone","voice"]}` |
| `POST /api/workflows/podcast/run` `{"message":"Episode 12","parameters":{"tone":"formal","voice":"nova"}}` | `WorkflowOutput`: `Echo [instructions: Write the script in a formal tone.]: Episode 12` ve `Echo [instructions: Edit for a formal tone, voice nova.]: …` — iki adım da kendi talimatında değeri taşır |
| Aynı çağrı, `parameters` yok | `HTTP 400` · `{"title":"Invalid run parameters","missingParameters":["tone"],"unknownParameters":[],"tooLongParameters":[]}` |
| `POST /api/agents/podcast-writer/run` `Prefer: respond-async`, `parameters` + `documents` | kuyruğa girer, worker değeri ve belgeyi kullanır; zorunlu değer yoksa kabulde `400` |
| `POST /api/runs/{id}/replay` gövdesiz | `Echo [instructions: Edit for a dry tone, voice alloy.]: Episode 14` — kayıtlı değer |
| Aynı, `{"parameters":{"voice":"onyx"}}` | `… voice onyx.]` — birleşik değer |
| Aynı, `{"parameters":{"vice":"x"}}` | `Replay not supported` · `Unknown parameter 'vice'` |
| Agent batch: düz öğe · yapılı öğe (`tone`+`voice`) · bozuk öğe (`tonne`) · düz öğe | `Completed` · `Completed` (`formal … nova`) · `Failed` (`Unknown parameter 'tonne'`) · `Completed` |
| Zamanlama kaydı, `parameters` string olan öğe | `HTTP 400` |
| `podcast-narrated` (fonksiyon düğümü `GetRunParameters()["voice"]`) | `[shimmer] Echo [instructions: Write the script in a calm tone.]: Episode 15` |

Varsayılan dışı yapılandırma: `Tracon__Providers__OpenAI__ApiKey=` (boş) ve `ConnectionStrings` SQLite'a
çevrildi; gerçek sağlayıcı anahtarı ve PostgreSQL bağlantısı bu koşumda kullanılmadı.
Denetim sonrası webhook zarfı (`MT-JOB-139`) örnek uygulamada **koşulmadı**; imza `secret`'ı
gerektirir. Kanıtı `InboundTriggerDispatcherTests` (dispatcher + handler çifti) verir.

## Kapanış Kapıları

`python3 scripts/kapi.py kapanis --taban 12bf0b4c` (denetim düzeltmelerinden **sonra**): **10/10 adım yeşil**,
çıkış 0. İlk koşum `SourceLanguageTests`'te düştü (yeni test girdimdeki `ü`); girdi değişti,
baz çizgisi büyütülmedi, kapı baştan koşuldu.

| Adım | Süre |
|---|---|
| `kapi.py tarama` (`secret` taraması) · `dokuman-bakim.py --denetle` · script testleri · agent map · `denetim-paketi.py` | 5,97 · 1,67 · 7,24 · 0,18 · 0,53 sn |
| `dotnet build Tracon.slnx -c Release` | 88,94 sn · 0 uyarı, 0 hata |
| `dotnet test` (`-maxcpucount:2`) | 474,17 sn · hiçbir proje `Failed` değil |
| `dotnet pack` · `dotnet format --verify-no-changes` · `docs-site` `npm run check` | 7,97 · 117,88 · 38,84 sn |

Başlıca test sayıları (net10.0): Core.UnitTests 3466 · AspNetCore.FunctionalTests 1493 ·
PostgreSql.IntegrationTests 959 · Sqlite.IntegrationTests 903 · SqlServer.IntegrationTests 886 ·
Ui.E2ETests 89 · Workflows.UnitTests 167 · Generators.UnitTests 302.

**Yayın provası** (sevk edilen `RunInputStoreContract` ve `WorkflowDefinitionStoreContract` genişledi):
`python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.4` → çıkış 0; altı exact-version paketlenmiş
sample testi, Native AOT smoke ve net8.0 tüketici smoke geçti. Hiçbir sample bu iki store'u uygulamıyor.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan tek commit'te kaldı (`12bf0b4c`); sapmalar "Plandan Sapmalar"da |
| Düzeltme turu sayısı | 6 — mimari ve ratchet kapıları (2: servis kaydı/yüzey/SQL metni; kaynak dili/sevk edilen doküman) · migration bütünlük manifesti (1) · site kapıları: ekran görüntüsü damgası, anasayfa pin'leri, agent map (1) · bağımsız denetim sonrası (1) · kapanış koşumunda kaynak dili: denetim testimdeki `ü` (1) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 2 — `🔴1` (webhook gövdesi yapılı batch öğesi sayılıyordu, bu fazın ayrıştırıcısı getirdi) ve `🟡4` (parametreli workflow'un boş mesajlı satırı judge'ı kör puanlamaya açtı). İkisini de bağımsız denetim yakaladı, kapanıştan önce düzeltildi |
| Faz kapandıktan sonra bulunan kusur | 0 — kapanış anı; sonraki `kusur-giderme` koşumunda güncellenir |

## Denetim Bulguları

Denetçi: `faz-denetcisi` (taze bağlam, salt-okunur), aralık `12bf0b4c..84b02b3f`. Triyaj
`faz-tamamlama` Adım 5.1: her bulgunun kanıtı kodda **elle** doğrulandı.

| # | Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🔴 | Workflow hedefli inbound trigger'ın webhook gövdesi `WorkflowJobHandler`'da yapılı batch öğesi olarak okunuyordu; gönderen `parameters` ile workflow'un değerlerini seçebiliyordu, fazladan üyeli gövde de `Failed` oluyordu | **Gerçek** — `InboundTriggerDispatcher` düz metin yazıyor, handler `BatchItemInput.Parse` ile okuyor | **Düzeltildi.** `BatchItemInput.Envelope`: dispatcher metni `{"message": …}` ile sarar. Testler: `InboundTriggerDispatcherTests` (dispatcher+handler çifti, iki gövde), `BatchItemInputTests` (zarf gidiş-dönüş, beş girdi). Mutasyon denetimi: zarf kaldırılınca iki durum da kırılıyor. `MT-JOB-139`, K-899 metni, CHANGELOG |
| 2 | 🟡 | `run_inputs` yazımı düşerse HITL run kalıcı cevaplanamaz; ret mesajı yanlış nedeni söyler; test cevabı denemiyor | **Gerçek** (dar) | **Kısmen düzeltildi.** Mesaj artık depo hatasını adlandırır; test cevabı dener. Yeniden yazma (kurtarma yolu) **devredildi**: yazım best-effort kalır (K-898), log uyarısı sonucu adlandırır. F-381 |
| 3 | 🟡 | Kiracı yalıtımı testi kiracıya özgü **saklı** agent tanımını sınamıyor | **Gerçek** (kapsam açığı; kodda kusur yok) | **Düzeltildi.** `WorkflowParameterTenantIsolationTests.Two_tenants_with_a_STORED_agent_of_the_same_name_keep_their_own_instructions` — iki kiracı, aynı adlı saklı agent, farklı talimat, eşzamanlı run; karışma yok |
| 4 | 🟡 | Parametreli workflow'un `run_inputs` satırı boş mesajlıdır; online eval ve `/judge` "girdi yok" korumasını atlayıp kör puanlıyordu | **Gerçek** — workflow kökü `MessageDelta` yazar | **Düzeltildi.** `OnlineEvalJobHandler`: `Messages.Count == 0` kayıt yok sayılır. Test `A_run_inputs_record_with_no_messages_is_judged_like_a_missing_one`; mutasyon denetimi kırılıyor. CHANGELOG |
| 5 | 🟢 | Paylaşılan boş sözlükler `Dictionary` tabanlı | Gerçek, düşük | Devredildi: F-376 |
| 6 | 🟢 | `ReplayTools` + parametreli tanım testi yok | Gerçek, düşük | Devredildi: F-377 |
| 7 | 🟢 | Replay ret mesajı kayıt dışı giriş yollarını (OpenAI uyumlu, MCP, A2A, voice) anmıyor; test adı 404'ü "ayarı adlandırır" diye gösteriyor | Gerçek | **Düzeltildi** (mesaj eklendi, test yeniden adlandırıldı) |
| 8 | 🟢 | Agent batch `anyItemCarriesValues` yalnız `Pending` öğeleri sayar | Gerçek, uç durum | Devredildi: F-378 |
| 9 | 🟢 | `InMemoryRunInputStore` 1000 run'da keser | Gerçek, reddedici | Devredildi: F-379 |
| 10 | 🟢 | Kayıtlı schedule'larda `{"message":…, başka alan}` öğeleri yükseltmeden sonra tek tek `Failed` olur | Gerçek | **Düzeltildi** (belge): CHANGELOG sonucu adlandırır |
| 11 | 🟢 | Workflow run ucunun `WithDescription` metni parametre kapısını anmıyor | Gerçek, yalnız metin | Devredildi: F-380 (OpenAPI + istemci yeniden üretimi ister) |

Denetçinin "planı aşan kararlar" notu: `Kind` doğrulanmaması, workflow satırının mesaj saklamaması
ve `WorkflowDescriptor.Parameters` makul bulundu. **Açık Soru 7'nin ölçümü** repoda yeniden
üretilebilir bir artefakta bağlı değil, yalnız K-897'deki sayıdır; ölçüm tek seferlik bir test
koşumuydu ve kapsam dışı bırakıldı.

Plan test adlarından ayrı sınıf olarak **yazılmayanlar** (içerikleri başka sınıflardadır):
`WorkflowParameterConcurrencyTests` (→ `WorkflowParameterTenantIsolationTests`),
`WorkflowRunnerParameterValidationTests` (→ `WorkflowParameterRespondTests` içinde),
`QueuedRunDocumentTests` (→ `QueuedRunParameterTests`), `WorkflowRunInputStoreFailureTests`
(→ `WorkflowParameterRespondTests.A_failing_input_store_does_not_stop_the_run`).

## Sonraki Faza Devir Notu

**Sıradaki faz: [Faz 195](195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md).** Önkoşulu (bu faz) karşılandı;
başlayabilir.

### Devralınan sözleşmeler

- `WorkflowDefinition.Parameters` / `WorkflowSaveRequest.Parameters` (`IReadOnlyList<AgentParameter>`) **vardır**; editör onu **korur**, düzenlemez. Faz 195 `workflow-editor.tsx`'te `Draft.parameters`'ı **silmemelidir**: `full-replace-editors.test.tsx` `Complete<WorkflowSaveRequest>` alanı zorunlu kılar.
- `WorkflowDefinitionPayload` (`Tracon.Sql.Shared`) `Parameters`'ı elle taşır (K-580). Faz 195'in `Settings` alanı aynı sınıfa girer; bellek içi depo bunu maskeler, `WorkflowDefinitionStoreContract` tam alan gidiş-dönüşünü kanıtlar.
- `context.GetRunParameters()` fonksiyon düğümünde nihai değer setini verir ve run dışında `InvalidOperationException` atar. Faz 195'in `AddWorkflowFunction<TInput, TOutput, TSettings>` aşırı yüklemesi aynı bağlamı kullanır.
- `WorkflowDescriptor.Parameters` konsol detay ekranına gelir; Faz 195 düğüm listesi kurarken bu alanı bozmaz.
- Konsol bundle tabanı: **197,7 KB gzip** (bütçe 250 KB).

### 🚨 Bilinen tuzaklar

- **Parametreli derleme tek yerdedir:** `ParameterizedAgentFactory`. Önbelleği ve katalog dekoratör zincirini atlar; yeni bir çağıran kendi derlemesini yazarsa bütçe, onay ve kayıt dekoratörleri **sessizce** düşer.
- **`NULL` ≠ `{}`** (`run_inputs.parameters`): okuyucu ikisini aynı saymaz, yoksa parametreli tanım varsayılanla sessizce sürer (K-898).
- **Dış kaynaklı metin zarflanır** (`BatchItemInput.Envelope`): `job_items.input` tür taşımaz, `message` string üyeli her JSON yapılı sayılır. Dışarıdan gelen metinden öğe yazan her yeni yol aynısını yapar (denetim 🔴1, K-899).
- **`jobs.payload` ve `job_items.input` korumasızdır** (K-899): parametre değeri ve belge metni orada düz durur. F-374.
- **Workflow satırı mesaj saklamaz** (`Messages = []`); tüketiciler boş mesajlı satırı "girdi yok" sayar (online eval, `/judge`). Yeni bir `run_inputs` tüketicisi aynı kuralı uygular.
- **`dotnet build`'siz E2E:** `kapi.py test` derlemez; ekran görüntüsü ve E2E için önce UI açık tam `dotnet build Tracon.slnx -c Release` gerekir.
- **Anasayfa kırpımı pin'i:** `approvals*` ve `run-detail*` yeniden üretilirse `CAPTURE_PINS` güncellenir ve kırpım gözle doğrulanır (bu fazda yapıldı).

### Doküman bütçeleri (dar)

`docs/ADAYLAR.md` %2, `docs/manuel-test/*.md` %2, `hafiza/workflows.md` %2, `hafiza/cekirdek-calistirma.md` %0
boş. Faz 195 bunlara ekleme yapmadan **önce** içerik taşır.

### Açık işler

- `F-374…F-381` adayları ([ADAYLAR.md](../../ADAYLAR.md)): `jobs.payload` koruması, batch öğe türü işareti, paylaşılan boş sözlükler, `ReplayTools` testi, batch kira devri, bellek içi depo sınırı, OpenAPI açıklaması, HITL kayıt kurtarma.
- Site **yayınlanmadı** (`./scripts/site-deploy.sh` çalıştırılmadı; Adım 10 dışarıya dönük iştir, istenmedi). Kapı ve derleme yeşil.

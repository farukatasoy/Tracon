# Faz 196 — Uzun Adımların Süre Sınırı ve İptali

> **Durum:** ✅ Tamamlandı (2026-10-13)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-B, 2-A, 3-A, 4-A, 5-A, 6-A, 7-A, 8-A, 9-A, 10-B, 11-B, 12-B, 13-A). Faz 203 ile paralel geliştirilir
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-303**, **F-323**, **F-405**
> **Önkoşul:** [Faz 195](195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md) — aynı kayıt yüzeyi ve editör satırı; 2026-10-12'de kapandı (`55272673`)
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.Sql.Shared`, SQL sağlayıcıları, `.AspNetCore`, `.Testing.Contracts.Xunit`, `.UI` · üretilen istemciler
> **Yeni paket:** Yok · **Migration:** gerekli (Açık Soru 5) — `jobs` süre sütunu, üç sağlayıcı; numara uygulamada alınır
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/workflows.md`, `guides/write-your-own-job-handler.md`, `guides/background-work.md`, `reference/configuration.md`, `ui.md`, `capabilities.md` · sevk edilen: `IJobHandler`/`RunTimeout` XML'i, `<example>`, `Tracon.AgentMap.md`, `Tracon.Workflows/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md`, `16-IS-KUYRUGU-VE-ZAMANLAMA.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show bc010120:docs/arsiv/fazlar/196-UZUN-ADIM-SURE-VE-IPTAL.md
> ```
>
> Damıtıldı 2026-10-13 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün fonksiyon düğümünü yalnız kurulum geneli `RunTimeout` keser; job'un süre sınırı yoktur; iptal çalışan koda token olarak ulaşmaz. Faz ortak altyapıyı kurar: adım süresi, token'a ulaşan iptal, ayırt edilebilir hata.

## Bitiş Ölçütleri (DoD)

- [x] Aşım run'ı `Failed` + `workflow_function_timeout` ile bitirir; gövde token'ı iptal olur (test 1-4)
- [x] Çağıran iptali ve `RunTimeout` düğüm aşımı yazmaz; aralık dışı süre `400` (test 5, 9)
- [x] İptal handler token'ını bir `PollInterval` içinde düşürür; aşım `tracon.job.max-duration-exceeded` yazar (test 14-15)
- [x] İki alan dört depoda round-trip; harita bağlanır; `TimeoutSiteTests` güncel (test 12, 19-20, 22)
- [x] Agent düğümü süresi `ChildDeadline`'ı geçersiz kılar ve resume'u bozmaz; tanım `RunTimeout`'u tavanla sınırlıdır (test 24-28)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 9088e92e`, üç faz (196, 202, 203) `main`'e birleştirildikten sonra `c2592ccd` üzerinde tek koşum, çıkış 0; sevk edilen sözleşme değiştiği için `yayin --kuru --surum 1.0.0-preview.4` ilk koşumda çıkış 0 (§ *Kapanış kapısı ve yayın provası*)
- [x] `samples/Tracon.Api` ile gerçek `run` (case 1, 6, 7, 9, 11), çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri iki alan dosyasına eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz
- [x] `en.ts`/`tr.ts` eksiksiz; bundle payı yazıldı; `CHANGELOG.md` yeni aşırı yüklemeyi anar

### Kapanış kapısı ve yayın provası (`main`, 2026-10-13)

Faz 196, 202 ve 203 ayrı worktree dallarında paralel geliştirildi ve `main`'e birleştirildi (`bc3c7d11` → 203, `fac7fbd6` → 196, `c2592ccd` → 202). Uygulama dalları `kapanis` ve `yayin` koşmadı (orkestratör kararı, makine paylaşılıyordu); ikisi birleşik `main`'de bir kez koştu. Fazın tabanı `9088e92e`'dir.

| Kapı | Commit | Sonuç |
|---|---|---|
| `python3 scripts/kapi.py kapanis --taban 9088e92e` | `c2592ccd` | ✅ çıkış 0 — `tarama` · `--denetle` · script testleri · agent map · denetim paketi · `dotnet build` 89 sn · tam `dotnet test` 506 sn · `dotnet pack` · `dotnet format --verify-no-changes` 114 sn · `npm run check` (1078 sayfa, kırık bağlantı yok, SEO 0 hata). Performans kapısı tetiklenmedi: diff üç sıcak yola ve `bench/`'e dokunmaz |
| `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.4` | `c2592ccd` | ✅ ilk koşumda çıkış 0 — kırıcı liste 3 tip, 0 TFM düşüşü, 3 paket (hepsi `Unreleased` notunda) · 20 paket · npm dry-run · altı paketlenmiş örnek (`FileRunStore` 106, `CustomModelProvider` 38 + 1 atlandı, `CustomRunJudge` 11, `CustomAgentSource` 15, `CustomTool` 18, `CustomJobHandler` 10) · Native AOT smoke · net8.0 tüketici smoke |

Önceki prova çıktısı (Faz 197/198, `dcc1a88f`) silinmedi: `artifacts/package/yayin/1.0.0-preview.4-onceki-dcc1a88f`.

### Doğrulama komutları

Örnek uygulama 2026-10-13'te koşuldu: `190beb9d` derlemesi, port 5081,
PostgreSQL şeması `faz196`, `Development`, `Tracon__Demo__SlowModel__Seconds=20`.
İstek yolları `/tracon/api/...` altındadır; gövdeler `MT-WF-142`…`147` ve
`MT-JOB-146`…`148` içindedir.

```bash
dotnet artifacts/bin/Tracon.Api/release/Tracon.Api.dll --urls http://127.0.0.1:5081   # + case'e göre --Tracon:... argümanları
curl -sX PUT  $APU/api/workflows/bounded -H 'Content-Type: application/json' -d '{...}'
curl -sX POST $APU/api/workflows/bounded/run -H 'Content-Type: application/json' -d '{"message":"hello"}'
curl -s       $APU/api/runs/{runId}
curl -sX POST $APU/api/schedules/episode-nightly/trigger        # gövdesiz; boş JSON gövde + JSON content-type 400 döner (eski davranış)
curl -sX POST $APU/api/jobs/{jobId}/cancel
```

| Case | Gerçek çıktı |
|---|---|
| 1 | `HTTP 200 in 2.3 s` · run `Failed` · `workflow_function_timeout` / `Timeout` · "Function node 'slow' did not return within 2 s and was cancelled…" · katalog: `slow`, `deaf` → `"00:00:02"` |
| 2 | Düğüm `00:00:05`: `5.1 s`, `Failed`, "…within 5 s…" |
| 3 | Fonksiyon düğümünde `00:00:00` → `400` "Node 'slow' in workflow 'bad' has a 'timeout' of 00:00:00. A function node's timeout must be from 1 to 4294967294 milliseconds…" · agent düğümünde `00:00:05` → `200` |
| 5 | `deaf`: `2.2 s`, `Failed`; ~4 sn sonra log: "Function node deaf of workflow run 01a13615-fea6-… returned 00:00:06.0025155 after it started, past its timeout of 00:00:02; its result is discarded" |
| 6 | İptal `16:29:49.25` → `204`; iş hemen `Cancelled`; run `01a13619-408c…` `Canceled`, `completedAt 16:29:57.03` (iptalden 7,8 sn sonra; render 60 sn sürerdi); öğe `Pending` |
| 7 | Harita `tracon.workflow=00:00:05`: iş `Failed`, `attempt 1`, "tracon.job.max-duration-exceeded (ref: 7262b7b0)"; run 5,0 sn sonra `Canceled`, `job_max_duration_exceeded`; öğe `Failed`. Düzeltmeden önce: `attempt 2`, "All of the job's items failed." (kusur, `b93ed7da`) |
| 8 | Harita `00:00:00`: süreç dinlemeden çıktı (134); "TraconSchedulingOptions.MaxDurationByHandlerKey['tracon.workflow'] must be between 1 and 4294967294 milliseconds. Actual value: 00:00:00." |
| 9 | `podcast` (agent düğümü `00:00:05`, model 20 sn): `5.3 s`, `Failed`, `child_run_timeout` / `Timeout`, "Agent 'slow-writer' did not respond in time (limit: 00:00:05, set by the 'timeout' of node 'slow-writer' in workflow 'podcast')…" · `podcast-plain`: `20.2 s`, `Completed` |
| 10 | `runTimeout 00:30:00`, tavan yok → `400` "…above the installation's run timeout (00:10:00). A definition may only shorten 'Tracon:Workflows:RunTimeout' unless the host sets 'Tracon:Workflows:MaxRunTimeout'…" |
| 11 | Ölçekli (`RunTimeout=00:00:10`, `MaxRunTimeout=00:01:00`, 15 sn'lik zincir): `00:02:00` → `400` "…above the host's ceiling 'Tracon:Workflows:MaxRunTimeout' (00:01:00)." · `00:00:30` → `200`, run `15.2 s` `Completed` · kontrol (süresiz): `10.1 s` `Canceled`, `TimeoutException`, "…Raise the 'Tracon:Workflows:RunTimeout' value…" · tanım `00:00:05`: `5.0 s` `Canceled`, "…after 00:00:05, the run timeout its definition sets…" |

Case 4 👤 görseldir; koşulmadı (Vitest karşılığı: `workflow-editor.test.tsx`
"timeouts"; Aşama 2'de E2E `WorkflowTests` konsoldan süre girip run'ı ölçer).
**Arayüz payı:** gzip bundle 205,5 KB, taban 204,1 KB → **+1,4 KB**.

Aşama 2 yeniden koşumu (`135c7fde` derlemesi, aynı ortam): case 7 — iş `Failed`,
`attempt 1`, "tracon.job.max-duration-exceeded (ref: a3fe3931)", run 5,0 sn sonra
`Canceled` + `job_max_duration_exceeded`, öğe `Failed`; case 6 — iptal
`18:25:54.84`, run `Canceled` `18:26:02.76` (7,9 sn), iş `Cancelled`, öğe `Pending`.

---

## Plandan Sapmalar

> Plan ile gerçek arasındaki fark **gizlenmez**. Aşama 1 (kod + örnek uygulama)
> sonunda yazıldı; Aşama 2'de bakımcı kararları ve denetim bulgularıyla
> güncellendi. "Karar N" bakımcının 2026-10-13 kararlarıdır.

| # | Plan | Gerçek | Gerekçe |
|---|---|---|---|
| 1 | §196.2 Adım 0: MAF `FunctionExecutor` run token'ını geçiriyor mu? | **Geçiriyor.** Prob testi: `RunTimeout`'ta gövdenin token'ı 2 sn içinde iptal oldu. `kusur-giderme` gerekmedi; prob silindi | Ölçüm |
| 2 | §196.5: tanım slot beklemesinden önce çözülmemişse CTS sonra kurulur, slot beklemesi kurulum değerine sayılır | Tanım çözümü ve derleme **slot beklemesinden önceye** alındı; tanımın `RunTimeout`'u slot beklemesini de sayar; derlenemeyen tanım slot almadan düşer. **Onaylandı (karar 2, K-960)** | Geri dönüş yolu tanımın süresini kuyrukta yok sayardı |
| 3 | §196.5 + Açık Soru 10-B: `agentNames` biçimi süre taşımaz; konsolda süre alanı agent düğümünde de görünür | Aşama 1: süreli agent-only zincir `nodes` yazılıyordu ve sapma K-901'i yanlış alıntıladı (K-901: "fonksiyon varsa `nodes`, yoksa `agentNames`"). **Aşama 2 (karar 1, denetim 🟡4, K-959):** alan yalnız fonksiyon adımlı zincirde görünür; süre biçimi değiştirmez; `agentNames` biçimine düşen süreli adım (son fonksiyon kaldırıldı ya da HTTP ile yazılmış `nodes`) uyarıyla kaydı tutar, `chainBody` istek kurmaz | Biçim değişimi bekleyen run'ı "graph's structure differs" ile resume edilemez yapar ve agent adımının canlı akışını keser. Süre isteyen operatör fonksiyon adımı ekler ya da `nodes`'u HTTP ile yazar |
| 4 | Açık Soru 7-A: aşım denemesi başarısızdır, deneme kaldıkça yeniden kiralanır | Genel handler'da öyle. Workflow handler'larında 13-A önce gelir (**karar 4, K-963**): kuyruklu run işi hemen düşer; batch durdurulan öğeyi `Failed` yazar, deneme başarısız olur; bekleyen öğe kalmadıysa iş kodla hemen düşer. Aşama 2: aynı anda tamamlanan son öğede iş tamamlanır (🟡2); run'ı başlamamış öğe bekler kalır (🟡1) | Örnek uygulama case 7: tek öğeli batch kodsuz "All of the job's items failed." ile bitiyordu (`b93ed7da`) |
| 5 | §196.3: depoda okunan geçersiz `MaxDuration` için kural yok | Aşama 1: işçi loglar, işi sınırsız koşar. **Aşama 2 (🟡3):** loglar ve anahtarın haritadaki sınırına düşer; harita yoksa sınırsız | Bozuk satır operatörün sınırını kapatmamalı |
| 6 | §196.3: runner job sınırını nasıl öğrenir — yazılmadı | İç `JobAttemptSignals` + ambient `AmbientJobAttempt` (işçinin `ExecuteWithHandlerAsync` gövdesinde açılır); `AmbientWriteSiteTests` yeni işaretçi aldı; `JobContext.Signals` iç özelliktir | Run hata tipini yalnız sebebi bilen yazabilir; public yüzey büyümez |
| 7 | §196.5: kayıtta tavan `400` | `Tracon.Core` içinde iç `IWorkflowRunTimeoutPolicy`; `UseWorkflows` kaydeder; `PUT` onu opsiyonel `[FromServices]` ile alır | `Tracon.AspNetCore` `Tracon.Workflows`'a bağımlı değil; motor kayıtlı değilken yalnız `TimerPeriod` denetlenir |
| 8 | §196.5: tavan yalnız kayıtta | `MaxRunTimeout` açılışta `Enabled` kapalıyken de doğrulanır (`TimerPeriod`, `≥ RunTimeout`); tavan sonradan düşerse kayıtlı daha uzun değer tavana kesilir, uyarı loglanır. **Onaylandı (karar 3, K-960)** | Ayar tavanı emsali |
| 9 | Açık Soru 4: CS0121 ölçülür | `builder.AddWorkflowFunction<TIn,TOut>(name, factory, null)` **CS0121** verir (konumsal `null` hem `string? description`'a hem `Action<WorkflowFunctionOptions>`'a uyar); adlı argüman çalışır; RS0026/RS0027 tetiklenmedi. `CHANGELOG` "Source break" yazar | `1.0.0` öncesi kabul (K-958) |
| 10 | §196.1: konsolda saniye alanı | Saniye (`90`, `2.5`) veya `hh:mm:ss`; dokunulmayan değer sunucudaki yazımıyla döner (`storedTimeout`, K-918); okunamayan metin kaydı tutar | Tam değiştirme kaydı (K-725) değeri sessiz silmemeli |
| 11 | Test 9: "agent düğümünde süre" kötü girdi | F-405 ile geçerli girdi; test ve case 3 agent düğümünü `200` + üst sınırı (`MaxAgentNodeTimeout` = `TimerPeriod` − 30 sn) ile sınar | Plan satırı F-405'ten önceydi |
| 12 | Test 24: `waitTimeout` türetmesi | Sert kesim (süre + 30 sn) invoker alanından yansımayla okunur | 30 sn beklenmez |
| 13 | §196.5: aşım bugünkü davranış | `ChildAgentInvoker` opsiyonel `deadlineSetting` alır; ret metni ayarı adlandırır ("set by the 'timeout' of node …") | Operatör hangi ayarı değiştireceğini bilmeli |
| 14 | Planlanan dosya listesi | Örnek uygulamaya `SlowModelProvider` (`Tracon:Demo:SlowModel:Seconds`), `slow` ve `deaf` fonksiyonları | Case 9 gerçek model olmadan ölçülebilsin |
| 15 | Manuel case 11: 10 dk / 1 sa / 15 dk | Ölçekli: 10 sn / 1 dk / 15 sn | Aynı kural, kısa koşum |
| 16 | Migration numarası uygulamada | PostgreSQL `0057`, SQL Server `0045`, SQLite `0044` (`job_max_duration`) | — |
| 17 | Test 23 (E2E) | Aşama 1'de koşulmadı. Aşama 2: `WorkflowTests.A_step_timeout_typed_in_the_console_is_saved_on_its_node_and_stops_the_run` yazıldı; tam koşumda yeşil | — |
| 18 | Job satırları sahte saatle (`ManualTimeProvider`) | Core'un `ManualTimeProvider`'ı zamanlayıcı sürmez; Aşama 1 job testleri gerçek saat + kısa süre kullandı. **Aşama 2 (🟡6):** due-time `ManualClock` `tests/Shared/Time/`'a taşındı (Core + Workflows, K-411 bağlantı), periyodik zamanlayıcı aldı; iptalin tam bir `PollInterval`'da ve sürenin tam sınırda düştüğü işçinin kendi saatinde ölçülür. Kalan: diğer job satırları gerçek saatle, sınırları hata sınırıdır | İşçinin iş arama döngüsü `TimeProvider` almaz (`new PeriodicTimer(PollInterval)`): tamamen sahte saatle işçi iş kiralamaz |
| 19 | §196.2: geç bitiş logu | Çağıran iptali veya run süresiyle beklenmeyen gövdenin geç bitişi artık "past its timeout" demez (🟢4) | Yanıltıcı log |
| 20 | — | Aşama 1'in iki `<example>` bloğu örnek kapısında derlenmedi (JSON parçası `language="json"` taşımıyordu; örnek tanımsız `IAudioRenderer`'a dayanıyordu). Aşama 1 `ic-dongu` yalnız derlemişti; Aşama 2'nin tam koşumu yakaladı (`f3ba7ced`) | Fazın ürettiği regresyon |
| 21 | `capabilities.md` iki satır | Birleşik dalda ajan haritası bütçeyi 38 B aştı (11 302 / 11 264); iki satır tek "Time limits" satırı oldu (11 213 B) | `build-agent-map.mjs` bütçesi |
| 22 | — | Yeni public istisna `TraconWorkflowFunctionTimeoutException` yüzey envanterinde `kanıtsız` çıktı; site sayfası sabitini adlandırır (`concepts/workflows.md`) | `public-yuzey-envanteri.py` |

## Bu Fazda Verilen Kararlar

Karar defterine girenler (hepsi kullanıcı kararı; ayrıntı satırlarında):

| K | Özet | Kategori |
|---|---|---|
| K-958 | Fonksiyon düğümü süresi: kayıt + düğüm, deneme başına, `workflow_function_timeout`; `CS0121` kabul | `public-api` |
| K-959 | Agent düğümü süresi yalnız `nodes` zincirinde; konsol biçimi çevirmez (karar 1) | `public-api` |
| K-960 | Tanım `RunTimeout`'u + `MaxRunTimeout` tavanı; slot beklemesi sayılır (karar 2, 3) | `public-api`, `geri-dönüşü-pahalı` |
| K-961 | Job süresi alanı, sütunu, haritası; deneme başına; bozuk değer haritaya düşer | `public-api`, `kalıcı-veri` |
| K-962 | İptal token'a bir `PollInterval` içinde ulaşır; sebep kaynaktan | `public-api` |
| K-963 | Workflow işinde süre aşımı işin sonudur; 13-A > 7-A; hazırlık penceresi (karar 4) | `geri-dönüşü-pahalı`, `kalıcı-veri` |

Kategori dışı (K açılmadı):

- İç seam'ler public değil: `JobAttemptSignals`, `AmbientJobAttempt`, `JobContext.Signals`, `IWorkflowRunTimeoutPolicy` (Sapma 6, 7).
- `ManualClock` paylaşılan test aparatıdır, iki test projesine bağlanır (K-411 deseni).
- Örnek uygulamanın `SlowModelProvider`'ı yalnız `Tracon:Demo:SlowModel:Seconds` verilince kaydolur.
- Batch handler'da sınır yolu `ItemRun.Started`'a bağlıdır (K-963'ün uygulaması).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 2 — plan aşamasında F-405 kapsamı eklendi, Faz 197 devri Açık Soru 13'ü ekledi; uygulamada bir karar değişti (K-959, karar 1) |
| Düzeltme turu sayısı | 1 — Aşama 2: denetimin 6 🟡'ı ve 🟢4 |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 1 — iki `<example>` bloğu örnek kapısında derlenmedi (Sapma 20, `f3ba7ced`) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi — faz henüz kapanmadı |

## Denetim Bulguları

`faz-denetim` (bağımsız denetçi, 2026-10-13): 🔴 yok, 6 🟡, 4 🟢. Hepsi gerçek bulgu.

| # | Bulgu | Triyaj | Sonuç |
|---|---|---|---|
| 🟡1 | Hazırlık penceresinde sınır (K-926): devam satırı sonsuza dek `Queued`; başlamamış öğe run kimliksiz `Failed` | gerçek | Düzeltildi `135c7fde`: sınır yolu `ItemRun.Started`'a bağlı; iki düşen test, mutasyonla kırmızı |
| 🟡2 | Son öğe sınırla aynı anda tamamlanınca iş yine kodla `Failed` | gerçek | Düzeltildi `135c7fde`; düşen test |
| 🟡3 | Depodaki aralık dışı `MaxDuration` haritaya düşmüyor | gerçek | Düzeltildi `135c7fde`; `JobRecord` XML'i; düşen test |
| 🟡4 | Sapma 3'ün K-901 alıntısı yanlış; biçim değişimi resume'u ve akışı bozar | gerçek | Karar 1 ile değişti `dedbc039` (K-959); Vitest, E2E, MT-WF-144 |
| 🟡5 | `Stopped` plan adımının handler dalları testsiz | gerçek | İki fonksiyonel test, son kiralama gerçek işçiden `135c7fde`; mutasyonla kırmızı |
| 🟡6 | DoD "bir `PollInterval` içinde" ölçülmüyor | gerçek | `ManualClock` testleri `135c7fde`; kalan sapma Sapma 18 |
| 🟢1 | `TimeoutSiteTests` `Task.Delay(TimeSpan, TimeProvider, …)` sitelerini görmez | gerçek | Devredildi: F-449 |
| 🟢2 | `WorkflowAgentCache` süre başına girdi açar, tahliye yok | gerçek | Devredildi: F-450 |
| 🟢3 | `JobStoreContract` `int` ms sınırını aşan süreyi sınamıyor | gerçek | Devredildi: F-451 |
| 🟢4 | Çağıran iptalinde geç bitiş "past its timeout" diye loglanıyor | gerçek | Düzeltildi `f3ba7ced`; düşen test |

## Sonraki Faza Devir Notu

**Sözleşmeler.** Fonksiyon düğümü `retry(timeout(handler))` sarmalayıcısında koşar
(`Workflows/Internal/WorkflowNodeTimeout.cs`, `TraconWorkflowFunctionExtensions.NodeWrapper`).
Hata kimlikleri: `workflow_function_timeout`
(`TraconWorkflowFunctionTimeoutException.WorkflowFunctionTimeoutErrorType`),
`job_max_duration_exceeded` (`JobAttemptSignals.MaxDurationExceededRunErrorType`, iç),
`tracon.job.max-duration-exceeded` (`JobErrorCodes.MaxDurationExceeded`). Job token'ının
sebebi `JobContext.Signals.Reason`'dan okunur (iç).

**Tuzaklar.**
- 🚨 Workflow job handler'ına yeni bir hazırlık adımı eklenirse `ItemRun.Started`'ın ÖNÜNDE kalır; sınır yalnız başlamış run'ı bitirir (K-963, K-926).
- 🚨 Run'ı `job_max_duration_exceeded` dışında bir tiple kapatan değişiklik kurtarmayı süreyi yeniden deneme başına yapar (`WorkflowRecovery.PlanAsync` `Stopped`).
- 🚨 `TimeoutSiteTests` ortaktır (Faz 202 dahil): yeni `CancelAfter`/argümanlı CTS sitesi bir satır ister; `Task.Delay(süre, saat, …)` siteleri kapsam dışıdır (F-449).
- 🚨 Ajan haritası 11 213 / 11 264 B: yeni yetenek satırı başka bir satırı kısaltmayı gerektirir.
- 🚨 `docs/manuel-test/*.md` bütçesi birleşik dalda 2 550 487 / 2 556 000 B (~5,5 KB boş): sonraki fazın case'i için önce tarihli koşum notları `arsiv/manuel-test-kosum-2026-09/`'a taşınır (F-441 deseni).
- Zamanı soran testte `tests/Shared/Time/ManualClock.cs` kullan; iş işçisinin iş arama döngüsü gerçek saattir.

**Devirler.** Faz 199 (aynı sarmalama noktası; geç gövdenin maliyeti) ve Faz 200
(`RunTimeout`/`Timeout` tanımın parçası; süre şekil değişikliği değildir) "Bu Faza
Başlarken"e birer madde aldı. Faz 202 başka bir oturumda uygulanıyor; ortak dosya
`TimeoutSiteTests` ve `tests/Shared/Time/ManualClock.cs` bu notta.

**Yarım kalan.** `kapi.py kapanis` ve yayın provası birleşik `main`'de yeşil koştu (§ *Kapanış kapısı ve yayın provası*). `site-deploy.sh` koşulmadı: yayın bakımcı onayını bekler (üç fazın site değişikliği birlikte yayınlanır).

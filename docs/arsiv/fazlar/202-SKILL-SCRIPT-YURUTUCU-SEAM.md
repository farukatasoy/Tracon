# Faz 202 — Skill Script Yürütmesini Harici Sandbox'a Yönlendiren Seam

> **Durum:** ✅ Tamamlandı (2026-10-13)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-A, 5-A; 1–3 güvenlik kararıdır). Faz 196 ve Faz 203 ile paralel geliştirilir
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-345**
> **Önkoşul:** Yok — işlevsel bağımlılık yok; Faz 196 ile ortak dosya `TimeoutSiteTests.cs`
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/tools.md`, yeni `guides/write-your-own-script-executor.md` (+ `src/sidebar.mjs`), `reference/{threat-model,security-policy}.md`, `getting-started/security.md`, `capabilities.md` · sevk edilen: XML `<example>`, `SECURITY.md`, iki paket README'si, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/14-SKILL-VE-SCRIPT.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show a6ad7b6f:docs/arsiv/fazlar/202-SKILL-SCRIPT-YURUTUCU-SEAM.md
> ```
>
> Damıtıldı 2026-10-13 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Skill script'i bugün beş kapıdan geçer ve Tracon'un makinesinde, Tracon'un OS kimliğiyle başlar (R8, K-862). Yürütme tek bir `internal static` çağrıdır; host süreci kendi ortamına (container, mikro-VM, uzak servis) taşıyamaz. Bu faz çağrıyı public bir seam'e çevirir: `ISkillScriptExecutor`.

## Bitiş Ölçütleri (DoD)

- [x] Kayıtlı yürütücüyle stored ve disk script'i yerel süreç başlatmadan yürütücüye gider (test 1, 4) — `SkillScriptExecutorRoutingTests` (stored: HTTP → MAF → runner, nöbetçi dosya; disk: MAF'ın dosya kaynağından, Sapma 1); örnek uygulamada `MT-SKILL-078`
- [x] Kapı kapalıyken veya denetim yazılamazken yürütücü çağrılmaz (test 2-3) — `A_closed_gate_never_reaches_the_executor` (grant yok · bayat pin · liste dışı uzantı), `A_run_whose_audit_entry_cannot_be_written_never_reaches_the_executor`; örnek uygulamada `MT-SKILL-079`
- [x] Varsayılanla mevcut script testleri davranış iddiası değişmeden yeşil — `SandboxedSkillScriptRunnerTests`, `SkillScriptProcessRunnerTests` (yalnız kurulum satırı), `SkillScriptContentPinTests`, `SkillScriptGrantTests`, E2E `SkillTests` (UI'lı derleme, 4/4); örnek uygulamada yerel yürütücü nöbetçi dosyayı yarattı
- [x] Süreye uymayan yürütücü `Timeout + tolerans` içinde `TimedOut` kaydı üretir (test 6) — "kayıt" `ToolInvocationRecord.Error` = `Timed out; the script executor did not return in time.` metnidir (`ToolInvocationRecord.TimedOut` alanı `TraconToolTimeoutException`'a aittir, yazılmaz); `An_executor_past_its_timeout_is_abandoned_at_the_wait_limit_and_frees_the_quota` (kurulan gecikme tam 35 sn); örnek uygulamada `MT-SKILL-080` (kayıt süresi 7,009 sn; düzeltmeden sonra 7,002 sn)
- [x] Yerel yürütücü sözleşmeyi geçer; `RequireCustomBinding` yerel varsayılanda açılışı durdurur — `SkillScriptProcessRunnerContractTests` 10/10 (net8 · net9 · net10), `SkillScriptExecutorRegistrationTests`; örnek uygulamada `MT-SKILL-082`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 9088e92e`, üç faz (196, 202, 203) `main`'e birleştirildikten sonra `c2592ccd` üzerinde tek koşum, çıkış 0; sevk edilen sözleşme değiştiği için `yayin --kuru --surum 1.0.0-preview.4` ilk koşumda çıkış 0 (§ *Kapanış kapısı ve yayın provası*)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1, 4), çıktı belgeye yazıldı — § *Örnek uygulama koşumu*; denetim düzeltmesinden sonra `078`/`080` yeniden koşuldu
- [x] `secret` taraması boş döndü — `kapi.py tarama`: temiz (17 işaretli sentetik credential atlandı)
- [x] Manuel kabul case'leri `docs/manuel-test/14-SKILL-VE-SCRIPT.md` içine eklendi; otomatikleştirilebilenler koşuldu — `MT-SKILL-078`…`083` (§ 9); `078`…`082` örnek uygulamada koşuldu; `078`…`080`, `082`, `083` ➜ CI (`083`'ün HTTP yolu yok, F-481)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 🔴 yok; 2 🟡 + 4 🟢 düzeltildi (§ *Denetim Bulguları*)
- [x] `docs-site/` güncellendi; `cd docs-site && npm run check` temiz; `CHANGELOG.md` yeni tipleri anar — § *Tüketici yüzeyi*

### Kapanış kapısı ve yayın provası (`main`, 2026-10-13)

Faz 196, 202 ve 203 ayrı worktree dallarında paralel geliştirildi ve `main`'e birleştirildi (`bc3c7d11` → 203, `fac7fbd6` → 196, `c2592ccd` → 202). Uygulama dalları `kapanis` ve `yayin` koşmadı (orkestratör kararı, makine paylaşılıyordu); ikisi birleşik `main`'de bir kez koştu. Fazın kendi tabanı `12cfee4b`'dir; kapanış tabanı `9088e92e` onu kapsar.

| Kapı | Commit | Sonuç |
|---|---|---|
| `python3 scripts/kapi.py kapanis --taban 9088e92e` | `c2592ccd` | ✅ çıkış 0 — `tarama` · `--denetle` · script testleri · agent map · denetim paketi · `dotnet build` 89 sn · tam `dotnet test` 506 sn · `dotnet pack` · `dotnet format --verify-no-changes` 114 sn · `npm run check` (1078 sayfa, kırık bağlantı yok, SEO 0 hata). Performans kapısı tetiklenmedi: diff üç sıcak yola ve `bench/`'e dokunmaz |
| `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.4` | `c2592ccd` | ✅ ilk koşumda çıkış 0 — kırıcı liste 3 tip, 0 TFM düşüşü, 3 paket (hepsi `Unreleased` notunda) · 20 paket · npm dry-run · altı paketlenmiş örnek (`FileRunStore` 106, `CustomModelProvider` 38 + 1 atlandı, `CustomRunJudge` 11, `CustomAgentSource` 15, `CustomTool` 18, `CustomJobHandler` 10) · Native AOT smoke · net8.0 tüketici smoke |

Önceki prova çıktısı (Faz 197/198, `dcc1a88f`) silinmedi: `artifacts/package/yayin/1.0.0-preview.4-onceki-dcc1a88f`.

### Örnek uygulama koşumu — gerçek çıktı (2026-10-13, port 5083)

`dotnet run --no-build -c Release --no-launch-profile`, `ASPNETCORE_ENVIRONMENT=Development`
(`user-secrets` yüklendi; OpenAI `gpt-5.4-mini`), PostgreSQL `ap-pg`, şema
`Tracon__PostgreSql__SchemaName=faz202`, token `Tracon__Ui__AuthToken` ortamla
ezildi. Geçici kod `docs/manuel-test/14-SKILL-VE-SCRIPT.md` § 9'un ortak ön
koşuludur; commit edilmedi, koşum sonunda geri alındı. `run_skill_script` ve
`load_skill` için kalıcı onay kuralı; `yurutucu-skill` (`merhaba` = `touch
/tmp/tracon-faz202-yerel && echo yerel-surec`, `asili`, `ortam`) üç pinli grant'la.

| Case | Kontrol | Gerçek çıktı |
|---|---|---|
| `MT-SKILL-078` | `FAZ202_SCRIPTS=executor`, "Run the merhaba script." | run `01a1366d-0b3e-…` `Completed`; tool sonucu `exit_code: 0` / `faz202-executor:merhaba:FDBA1ED41483`; log: `FAZ202-EXECUTOR received SkillScriptExecutionRequest { TenantId = default, RunId = 01a1366d-0b3e-…, SkillName = yurutucu-skill, ScriptName = merhaba, Origin = Stored, Extension = sh, Interpreter = /bin/sh, ContentSha256 = FDBA1ED4…1B36E47, SourcePath = , Timeout = 00:00:30, MaxOutputBytes = 262144 }`; bağımsız `sha256("touch /tmp/tracon-faz202-yerel && echo yerel-surec")` aynı; log'da `touch /tmp` yok; `/tmp/tracon-faz202-yerel` yok; denetimde `script.run yurutucu-skill/merhaba` |
| `MT-SKILL-079` | `merhaba` grant'ı iptal (`204`), aynı istek | modele `Error: Function failed.`; yürütücü log satırı sayısı 1'de kaldı; `script.denied yurutucu-skill/merhaba "There is no valid execution grant for this script."` |
| `MT-SKILL-080` | `Tracon__Skills__Scripts__Timeout=00:00:02`, kota 1/1, `asili` ve `merhaba` 1 sn arayla eşzamanlı | `asili` kaydı `duration 00:00:07.009`, `error "Timed out; the script executor did not return in time."`, SSE `did not return in time`; log `Skill script yurutucu-skill/asili did not return within its timeout of 00:00:02 plus 00:00:05; …`; `merhaba` kaydı `asili`'den 9 ms sonra (`18:02:42.853` → `.862`), `exit_code: 0` — kota bırakılınca hemen koştu; iki run `Completed`, toplam 11 sn |
| `MT-SKILL-081` | `FAZ202_EXECUTOR=docker`, `TRACON_FAZ202_HOST_MARKER=host-only-value`, `ortam` | `docker run --rm -i --network none alpine:3.20`; 350 ms; `/proc/1/environ` ve `env`: yalnız `PATH`, `HOSTNAME=7bc52dc81baf`, `HOME=/root`, `PWD`, `SHLVL`, `TRACON_SKILL_NAME=yurutucu-skill`, `TRACON_SKILL_TEMP=/tmp`; `host-only-value` ne çıktıda ne log'da |
| `MT-SKILL-082` | `FAZ202_SCRIPTS=require-missing` | açılış çıkış kodu `134`: `System.InvalidOperationException: ISkillScriptExecutor was declared as a required custom binding, but the registration that resolves is Tracon's own. …` |
| Varsayılan yürütücü | `FAZ202_SCRIPTS=default`, `merhaba` (grant yeniden) | `exit_code: 0` / `yerel-surec`, 103 ms; `/tmp/tracon-faz202-yerel` oluştu — yerel süreç önceki gibi |

Temizlik: üç grant iptal, agent ve skill silindi, iki onay kuralı silindi,
nöbetçi dosya silindi; 5083 boş. `faz202` şeması `ap-pg`'de duruyor.

**Denetim düzeltmesinden sonra (`bcb15ad5`, aynı kurulum):** bekleme sınırı artık
okumadan önce kuruluyor; stored yol etkilenmemeli. `asili` + `merhaba` eşzamanlı,
`Timeout=00:00:02`: `asili` kaydı `00:00:07.002`, aynı hata metni; `merhaba` 8 ms
sonra (`18:49:54.337` → `.345`) `exit_code: 0` / `faz202-executor:merhaba:FDBA1ED41483`;
yürütücü iki istek aldı, nöbetçi dosya yok. Disk boyut/tür kapısı örnek uygulamada
koşulamaz: disk skill'i HTTP'den çağrılamıyor (F-481); `MT-SKILL-083` ➜ CI.

### İç döngü (`kapi.py ic-dongu`, 2026-10-13)

Proje haritasında `Tracon.Testing.Contracts.Xunit` yoktur; seçim tam koşuma
genişledi ve UI'sız derlendi (F-356). Sonuç ve teşhis:

| Bulgu | Sebep | Durum |
|---|---|---|
| `TestDelayClassificationTests` (3 TFM) | yeni testlerde dört `Task.Delay` etiketsizdi | `// delay: simulated` / `negative` eklendi; izole geçti |
| `SourceLanguageTests` (3 TFM) | `İstanbul` ve `ünicode` örnek metni; sonra geçici örnek uygulama kodu (`Faz 202`) | örnek metin değişti; geçici kod geri alındı; tam `Tracon.Core.UnitTests` net10 3745/3745 |
| `Tracon.Ui.E2ETests` (tümü) | UI'sız derleme (F-356) | UI'lı derlemeyle `SkillTests` 4/4 |
| `Tracon.SqlServer.IntegrationTests` 4 test | paylaşılan makinede 35 dk'lık koşum, kaynak çekişmesi | hepsi izole geçti; faz SQL'e dokunmadı |

Geri kalan projeler yeşil. Düzeltmeden sonra hedefli koşumlar: `Tracon.Core.UnitTests`
net10 tamamı 3745/3745; net8 ve net9 script/mimari sınıfları 109/109;
`Tracon.AspNetCore.FunctionalTests` script, kayıt ve DI sınıfları 67/67;
`Tracon.Generators.UnitTests` 311/311 (XML örneği derlenir).

**İkinci koşum (denetim düzeltmesi, commit'ten önce):** seçim haritadan geldi (tam
koşum yok). Çıkış `0`: `Tracon.AspNetCore.FunctionalTests` 1783/1783 ·
`Tracon.Core.UnitTests` 3754/3754 (net8 · net9 · net10) · `Tracon.Generators.UnitTests`
311/311 · `Tracon.Sql.Shared.UnitTests` 52/52 (üç TFM).

### Tüketici yüzeyi (`tuketici-dokuman-senkronu`, 2026-10-13)

Envanter: **site** — `guides/write-your-own-script-executor.md` (yeni, `src/sidebar.mjs`),
`concepts/tools.md`, `reference/{threat-model,security-policy}.md`,
`getting-started/security.md`, `capabilities.md`, `packages.md`; **sevk edilen** — XML
(`ISkillScriptExecutor`, istek, sonuç, köken, `TraconSkillScriptOptions`,
`UseSkillScripts`, `RequireCustomBinding`), `SECURITY.md`, `Tracon.Abstractions` ve
`Tracon.Testing.Contracts.Xunit` README'leri, `CHANGELOG.md` (`Added` + üç `Changed`);
**üretilen** — `Tracon.AgentMap.md`, `llms.txt`, `llms-full.txt`. Kapılar:

| Kapı | Çıktı |
|---|---|
| `ShippedDocumentationSelfContainmentTests`, `CapabilityExampleTests`, `SourceLanguageTests` | 6/6 |
| `Tracon.Package.Tests` `LocalReferenceTests` | 10/10 |
| `build-agent-map.mjs --check` | `Agent map: up to date and within budget.` |
| `npm run check` | `Content: 57 manual pages and 1070 total pages passed.` · `Links: 178293 internal reference(s) across 1070 pages and llms.txt, none broken.` · `SEO: … 0 hata` · `Weight: 1070 pages under 59000 B gzip.` |
| `dokuman-bakim.py --site-denetle --taban 12cfee4b` | üç kural (`buildtransitive`, `cekirdek-kavram`, `paket-readme`) ✅ |

### `main` ile birleşme (Faz 196 + 203, 2026-10-13)

Kararlar `K-962`…`K-969`'dan `K-964`…`K-971`'e kaydırıldı (main'in sonu K-963). Yüzey
tabanı `Tracon.Abstractions` 421 + 4 = 425 (gerçek sayımla `PublicSurfaceBaselineTests`
yeşil). `docs/manuel-test/*.md` birleşik ağaçta 3,4 KB aştı; F-441 deseniyle
`14-SKILL-VE-SCRIPT.md`'nin tarihli notları (`041`, `025`, `054`, `060`/`061`) ve § 9'un
geçici kod bloğu `arsiv/manuel-test-kosum-2026-09/`'a taşındı ya da kısaltıldı; ağaç
2 555 604 / 2 556 000 B. Birleşik dalda: build 0 uyarı · `Tracon.Core.UnitTests`
3798/3798 (net8 · net9 · net10) · `Tracon.AspNetCore.FunctionalTests` 1822/1822 ·
`Tracon.Generators.UnitTests` 317/317 · `Tracon.Testing.Contracts.Xunit.UnitTests` 14/14 ·
`Tracon.Sql.Shared.UnitTests` 52/52 · `npm run check` (1078 sayfa, kırık bağlantı yok) ·
`--denetle` çıkış 0 · `--site-denetle` ✅ · `kapi.py tarama` temiz.

---

## Plandan Sapmalar

Uygulama oturumunda yazıldı (faz-uygulama Adım 1). Kapanış oturumu gözden geçirir.

1. **Disk script'i HTTP run'dan hiç çalışmıyor — kusur F-481.** Plan test 1 ve 4'ü
   "HTTP → MAF → runner" seviyesinde istiyordu. Ölçüldü: agent `SkillNames` içinde
   yalnız diskte duran bir skill'i adlandırınca run `400` döner (*"refers to skill
   'disk-skill', but the skill was not found"*). `AgentSkillCatalog.ResolveAsync` ve
   `AgentDefinitionValidator.CheckSkillsAsync` adı yalnız kodda ve `store`'da arar.
   Faz öncesinden beri böyledir; fazın alanı değil (katalog/derleme), kararı açık
   (disk tarama ve önbellek parmak izi). `docs/ADAYLAR.md` kusur tablosuna 🟡 yazıldı.
   Disk yönlendirmesi MAF'ın kendi dosya kaynağından (`SkillScriptSupport.CreateFileSource`
   → `AgentFileSkill.GetScriptAsync` → `AgentFileSkillScript.RunAsync`) başlayan,
   host container'ını kullanan fonksiyonel testle kanıtlandı. Ölçüm yan bulgusu: MAF
   dosya script'inin adı göreli yoldur (`scripts/route.sh`), uzantıyı da taşır;
   istek XML'i bunu yazar.
2. **Stored script adı yürütücüden önce bir KAPIDIR.** 202.4 "runner'da da doğrulanır"
   diyordu, yer söylemiyordu. Kontrol argüman kapılarının yanına, denetimden önce
   girdi: düz dosya adı olmayan ad `script.denied` yazar ve yürütücüye ulaşmaz. Önceden
   aynı ad `script.run` yazdıktan sonra yazma sınırında `TraconException` atıyordu.
   Faz 11 devri kapı 4'ü zaten "script adının düz bir dosya adı olduğu" diye anlatır;
   davranış şimdi o cümleye uyar. Yerel yürütücü yazma sınırında yine doğrular
   (uzantıyla birleşik adı da).
3. **Modele dönen zaman aşımı metni değişti (bakımcı onayladı, K-970).** `The script timed out and the process
   tree was terminated.` → `The script timed out and was stopped.` Eski cümle harici
   yürütücü için doğru değildi. Bekleme sınırında runner'ın kendi ürettiği sonuç ayrı
   metin taşır: `The script timed out. The script executor did not return in time, so
   the script may still be running.`; `tool_invocations.error` = `Timed out; the script
   executor did not return in time.` `ToolInvocationRecord.TimedOut` önceki gibi
   yazılmaz (alan `TraconToolTimeoutException`'a aittir).
4. **Kesme işaretinin metni ve yeri (bakımcı onayladı, K-970).** Plan "biçimli metnin sonuna" diyordu; metin de
   değişti: `[Tracon: output truncated at the N-byte limit per stream.]`. Yerel yürütücü
   artık işaret eklemez; sözleşme testi bunu zorlar (`Output_past_the_limit_is_cut_and_flagged_without_a_notice`).
   Runner'ın kesmesi UTF-8 karakter ortasında ve vekil çiftinde bölmez (`SkillScriptOutput`).
5. **Disk script'i `MaxScriptContentLength` ile sınırlı ve açılmadan denetlenir —
   bakımcı kararı (2026-10-13, Aşama 1'in "sınır yok" yorumunu değiştirdi; K-968).**
   Plan hata modu 12 "büyük disk script'i" diyordu, davranış söylemiyordu. Aşama 1
   sınır koymamıştı; bağımsız denetim (🟡2) iki gerilemeyi gösterdi: skill root'taki
   FIFO `open(2)`'de çağıranın iş parçacığını kota slotu tutulurken bloklıyordu (testte
   ölçüldü: test iş parçacığı dakikalarca asılı kaldı), `/dev/zero`'ya bağlantı sonsuz
   okunuyordu. Tasarım: (a) **kapı** — `CheckFileScript` bağlantıyı son hedefine çözer,
   yok / `Length == 0` / sınır aşımını dosyayı açmadan reddeder. FIFO, aygıt ve `/proc`
   girdisi .NET'te `Attributes=Normal`, `Length=0` verir (ölçüldü; tür açmadan
   okunamaz), boş script'in çalışacak bir şeyi yoktur; ikisi tek kuralda birleşti
   (`The script file is empty or is not a regular file.`). (b) **okuma** — `ReadBoundedAsync`
   havuz iş parçacığında açar, yürütücüyle aynı `Timeout + 5 sn` bekleme sınırı altında
   bekler (bekleme sınırının CTS'si artık okumadan önce kurulur) ve sınır+1 baytta durur.
   (c) **denetim sırası (K-969)** — kapı retleri `script.denied`, `script.run` ve kota slotu
   yok; kapıdan sonra çıkan hata (`could not be read`, `could not be read in time`, sınır
   aşımı) `script.run`'dan sonra tool hatasıdır: K-089 "denenen çalıştırma yazılır, kapalı
   kapı ret yazar" ile tutarlı. Bilinen sınır: kapıdan sonra FIFO'ya dönen dosyada
   açılış iş parçacığı yazar gelene kadar bloklu kalır; slot ve çağıran serbesttir.
6. **Yürütücü istisnasının mesajı modele ve kayda gitmez (bakımcı onayladı, K-970).** Plan "istisna span, metrik
   ve `tool_invocations`'a hata yazar" diyordu. Yazılan metin yalnız istisna tipidir
   (`The script executor failed (HttpRequestException).`); mesaj yalnız sunucu
   log'una gider. Harici yürütücünün mesajı host iç bilgisini taşıyabilir.
7. **Sözleşme ailesinin public yüzeyi plandan büyük.** Plan yalnız
   `CreateExecutorAsync()` gösteriyordu. "Gövdeler ezilir" kuralı için script gövdeleri
   (`ReadStandardInputScript`, `FailingScript`, `FloodScript`, `SleepScript`,
   `EmptyScript`, `EchoScript`, `EnvironmentScript`, `TempMarkerScript`), `Extension`,
   `Interpreter`, `SkipReason`, `StopTolerance` ve `CreateRequest` `virtual` oldu.
   Senaryo sayısı 10'dur: plandaki sekize "boş script" (hata modu 12) ve "stored istek
   `SourcePath` taşısa da içeriğini koşar" eklendi.
8. **Bekleme sınırı ikinci bir zamanlayıcıdır; seçenek tavanı 5 sn indi.**
   `TraconSkillScriptOptions.Timeout` artık en çok `~49,7 gün − 5 sn`'dir; validator
   toplamı çıkarmayla hesaplar (`TimeSpan.MaxValue + 5 sn` taşmaz). `TimeoutSiteTests`'e
   `SandboxedSkillScriptRunner.cs` satırı eklendi; mevcut `SkillScriptProcessRunner.cs`
   satırı değişmedi (paylaşılan dosya). Yerel yürütücü süreç başlamadan önce
   `request.Timeout` ve `MaxOutputBytes`'ı da doğrular (sözleşme doğrudan çağırabilir).
9. **XML `<example>` yalnız bir tip bildirimidir.** `ExampleCompilationTests` her
   örneği derler; tip ile kayıt satırını karıştıran blok derlenmez. Örnek kendi
   içinde derlenen bir HTTP runner yürütücüsüdür; kayıt cümlesi remarks'ta düz yazıdır.
10. **Test altyapısı:** `TriggerableTimeProvider` kurulan zamanlayıcıların süresini
    kaydeder (`DueTimes`) — test hangi gecikmenin kurulduğunu ölçer, yalnız birinin
    kurulduğunu değil.
11. **Önce düşen test, kodla birlikte yazıldı; kırmızı mutasyonla gösterildi.** Kod
    önce yazıldı. Her yeni davranış için kod geçici olarak bozuldu ve test kırmızı
    görüldü: runner'ın yürütücüyü atlaması (6 fonksiyonel test), yeniden kesmenin
    kalkması, tutarlılık denetiminin kalkması (3 birim), bekleme sınırının kalkması,
    validator satırının kalkması. Denetim yazımını yutan ve ad kapısını kapatan iki
    mutasyonun koşumunu güvenlik sınıflandırıcısı reddetti; o iki davranışı mevcut
    birim testi `The_script_does_not_run_when_the_audit_trail_cannot_be_written`, yeni
    fonksiyonel test `A_run_whose_audit_entry_cannot_be_written_never_reaches_the_executor`
    (Aşama 2'de pozitif kontrol: `script.run` denemesi tam 1) ve yeni birim testi
    `A_stored_script_whose_name_is_not_a_plain_file_name_is_denied_before_the_executor`
    yeşil kanıtlar; kırmızı hâlleri ölçülmedi. Aşama 2'nin testleri koddan ÖNCE yazıldı ve
    eski kodda kırmızı görüldü: boyut kapısı ve geç yürütücü hatası (ikisi birim) düştü,
    FIFO senaryosu test iş parçacığını asılı bıraktı (süreç öldürüldü).
12. **`ReadBoundedAsync` ve `CheckFileScript` `internal static` ve doğrudan test edilir.**
    Kapıdan sonra dosyanın değişmesi deterministik kurulamaz; test `script.run` yazımında
    dosyayı FIFO ile değiştiren bir denetim log'u kullanır (`SwappingAuditLog`) — denetim
    kapı ile okuma arasındaki tek adımdır.

## Bu Fazda Verilen Kararlar

`docs/KARARLAR.md` § 2 — `K-964`…`K-971` (main ile birleşmede `K-962`…`K-969`dan kaydırıldı):

| K | Karar | Kategori |
|---|---|---|
| K-964 | Yürütme `ISkillScriptExecutor` seam'inden geçer; kapılar, pin, denetim, kota, bekleme sınırı, çıktı sınırı runner'da; yerel süreç işaretli varsayılan; sonuç `Abstractions`'a taşındı, `Duration`'sız | public-api, güvenlik |
| K-965 | Harici yürütücüde de `PlatformIsolationAcknowledged`; K-861 değişmez (Açık Soru 1-A, 2-A) 👤 | güvenlik |
| K-966 | Disk script'i de yürütücüye; kardeş dosya taşınmaz; kota uygulanır; tolerans sabit 5 sn (Açık Soru 3-A, 4-A, 5-A) 👤 | güvenlik |
| K-967 | R8 varsayılan yürütücüye daralır; B7 adı ve kapsam satırı değişmez | güvenlik, public-api |
| K-968 | `MaxScriptContentLength` disk script'ine de; boş/özel dosya açılmadan ret; sınırlı ve bekleme sınırlı okuma 👤 | güvenlik |
| K-969 | Ad ve disk boyut/tür kapısı denetimden önce `script.denied`; kapı sonrası okuma hatası `script.run`'dan sonra | güvenlik |
| K-970 | Zaman aşımı metni, runner'ın tek kesme işareti, yürütücü istisnasında yalnız tip 👤 | public-api, güvenlik |
| K-971 | İstek ortam değeri taşımaz; iki değişken; `ContentSha256` ≠ grant pini; `ToString` içeriği basmaz | public-api, güvenlik |

Kategori dışı yerel tercihler: test altyapısı `TriggerableTimeProvider.DueTimes`;
XML örneği tip bildirimi (Sapma 9); `SkillScriptOutput` tek kesme yardımcısı.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 (disk script'i boyut sınırı, bakımcı kararı 2026-10-13; Sapma 5) |
| Düzeltme turu sayısı | 1 (bağımsız denetim → Aşama 2) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 1 (Aşama 1'in disk okuması: FIFO kota slotunu tutarak bloklanıyordu, `/dev/zero` sonsuz okunuyordu; merge'den önce denetimde bulundu ve kapandı) |
| Faz kapandıktan sonra bulunan kusur | 0 — faz kapanmadı (ölçülmedi) |

## Denetim Bulguları

Bağımsız denetim (2026-10-13): 🔴 yok, 2 🟡, 4 🟢. Triyaj ve sonuç:

| # | Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🟡 | `ISkillScriptExecutor` XML'i kotanın sırasını yanlış anlatıyordu ("after the call … quota"); kapı listesinde stored ad kapısı yoktu | gerçek | Düzeltildi: slot çağrıdan önce alınır ve çağrı dönene ya da bekleme sınırına kadar tutulur; liste ad ve disk boyut kapısını sayar. Site (`concepts/tools.md`, yeni rehber) aynı sırayı anlatır |
| 2 | 🟡 | Disk script'i okuması boyutsuz ve süresiz, kota slotu tutulurken; FIFO slotu kilitler, `/dev/zero` sonsuz okunur (gerileme) | gerçek | Düzeltildi (K-968, K-969, Sapma 5): açmadan meta veri kapısı + bekleme sınırı altında sınırlı okuma; testler önce kırmızı görüldü |
| 3 | 🟢 | `A_run_whose_audit_entry_cannot_be_written_never_reaches_the_executor` pozitif kontrol taşımıyordu | gerçek | Düzeltildi: `ScriptRunAttempts.ShouldBe(1)` |
| 4 | 🟢 | Ortam sızıntısı testi istek alanlarını `ToString()` ile tarıyordu | gerçek | Düzeltildi: istek `JsonSerializer` ile bütün seri hâle getirilip taranır |
| 5 | 🟢 | `tool_invocations` store'u hata verince run'ın sürdüğünü kanıtlayan test yoktu (faz öncesi) | gerçek | Düzeltildi: `A_tool_call_record_the_store_refuses_does_not_fail_the_run` |
| 6 | 🟢 | Çağıran iptalinde geç kalan yürütücü görevi gözlenmiyordu | gerçek | Düzeltildi: `ObserveLateOutcome` iptal yolunda da; `A_late_executor_failure_after_a_caller_cancellation_is_observed` eski kodda kırmızıydı |

Ayrıca denetim iki metin hatası buldu: Sapma 11 bir birim testini "yeni fonksiyonel"
diye anıyordu (düzeltildi); DoD 4'teki "TimedOut kaydı"nın
`ToolInvocationRecord.Error` metni olduğu yazıldı.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler.** `ISkillScriptExecutor` (singleton · beklenen kiracı ·
teslim garantisi yok; iptal işbirliğine dayanır, Tracon yalıtımı doğrulamaz) ve
`SkillScriptExecutorContract` (10 senaryo, POSIX `sh`). Sıra runner'dadır: kapılar
(ad ve disk boyut/tür dahil) → `script.run` → kota slotu → disk okuma → yürütücü;
okuma ve yürütücü tek `Timeout + 5 sn` bekleme sınırı altında. Çıktı sınırını ve tek
kesme işaretini runner uygular; süreyi runner ölçer; tutarsız/`null` sonuç ve istisna
çağrıyı düşürür (kayda yalnız tip).

**Açık uçlar.**

- **F-481** (🟡, ADAYLAR kusur tablosu): yalnız diskte duran skill'i agent
  adlandıramaz; `SkillRoots` script'leri HTTP run'dan çalışmaz. Kapsam dışında bırakıldı
  (bakımcı). Disk testleri bugün MAF'ın dosya kaynağından başlar; düzeltme HTTP biçimini
  ekler.
- **F-169** (Hyperlight/CodeAct yürütücüsü) eşiği değişmedi (en yeni 0.7.0,
  2026-10-12); seam artık böyle bir yürütücüyü paket olarak takmaya izin verir.
- `kapi.py` proje haritasında `Tracon.Testing.Contracts.Xunit` yok: bu pakete dokunan
  değişiklik `ic-dongu`'yu tam koşuma (ve UI'sız E2E'ye, F-356) genişletir.

🚨 **Harici yürütücü yazanlar için tuzaklar:**

- Dosya script'inin `ScriptName`'i göreli yoldur (`scripts/x.sh`); dosya adı değildir.
- Çağıran iptali `OperationCanceledException`'dır, `TimedOut` değil; token'ı yok sayan
  yürütücü Tracon bekleme sınırında bırakıldıktan sonra da koşabilir.
- Çıktıya işaret ekleme; `Truncated` yaz. Mesajı gizli bilgi taşıyan istisna atma —
  yalnız tipi kayda gider ama sunucu log'una gider.
- Kullanıcının yazabildiği dizinden dosya okuyacaksan FIFO/aygıtı açmadan ele
  (`docs/hafiza/secenek-baglama-ve-gizlilik.md`).

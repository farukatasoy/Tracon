# Test Kosum Tuzaklari — `dotnet test`, MSBuild, Paralellik

> Paketi KOSARKEN karsilasilan tuzaklar: `dotnet test` davranisi, MSBuild alt
> sureci, paralel sinif deadlock'u, kusur/kirilgan test ayrimi. Test YAZARKEN
> karsilasilanlar icin: [`test-altyapisi.md`](test-altyapisi.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 90'da ayrildi: `test-altyapisi.md` 15.751/16.000 B'ye ulasmisti (%1
> bosluk). 400 B madde tavani OLCULDU ve REDDEDILDI -- en buyuk madde (1.296 B)
> kesilse kok sebep kalir, COZUM (`MSBUILDDISABLENODEREUSE=1`, 8 dk -> 18,5 sn)
> giderdi. K-214 merdiveni: gercek bolunme.

## Alt surec ve MSBuild

- **🚨 Yonlendirilmis bir alt surecte MSBuild DUGUM YENIDEN KULLANIMI `WaitForExitAsync`'i ~15 DAKIKA bloke eder** (2026-08-07, Faz 47): `dotnet test AgentPrism.slnx` hicbir test kosmadan on dakikalarca asili kaldi. Kok sebep `AgentPrism.Package.Tests` fikstürüdür (Faz 95'e kadar `AgentPrism.Templates.Tests` adını taşıyordu): `ProcessRunner` `dotnet pack`/`build`'i `RedirectStandardOutput`/`Error` ile calistirir; `dotnet pack` MSBuild isci dugumlerini `nodeReuse:true` ile baslatir ve o dugumler komut bittikten sonra da yasar (varsayilan ~15 dk). Dugumler ebeveynin yonlendirilmis boru taniticilarini MIRAS ALIR, boru hicbir zaman EOF gormez ve .NET'in `Process.WaitForExitAsync` cagrisi cikis kodunu degil **asenkron okuyucularin bitmesini** de bekledigi icin alt surec saniyeler once cikmis olsa bile bloke kalir. **Belirti**: `ps` ciktisinda tek bir `dotnet pack` sureci yoktur, yalnizca oksuz (`ppid = 1`) `MSBuild.dll … /nodeReuse:true` dugumleri durur; dugumler `pkill` ile oldurulunce fikstür ANINDA devam eder (olculdu). **Cozum**: `ProcessRunner` her alt surece `MSBUILDDISABLENODEREUSE=1` verir. Komut satiri anahtari (`-nodeReuse:false`) yetmez — `dotnet new` gibi MSBuild'i DOLAYLI cagiran komutlar onu tasiyamaz. Olcum: 8 dk+ (asili) → **18,5 sn**. **Kural**: MSBuild cagiran her alt sureci yonlendirirken bu degisken verilir.

## 🚨 `sed -i.bak` + `mv .bak dosya` ESKI mtime'i geri getirir, `dotnet build` DERLEMEZ (Faz 94)

Bir gate'in gercekten bir kusuru YAKALADIGINI dogrulamak icin sahte bir kusur
enjekte edip test kosmak (bu depoda standart pratik) su sirayla YANLIS sonuc
verebilir: `sed -i.bak 's/X/Y/' dosya.cs` → build+test (kusur yakalanir, DOGRU)
→ `mv dosya.cs.bak dosya.cs` (geri al) → build (yesil, ama **YANLIS**: `mv`
hedefin mtime'ini KAYNAK dosyanin (`.bak`, sahte-kusurdan ONCEKI) mtime'iyla
degistirir; bu bazen sahte-kusurlu derlemenin CIKTI dosyasindan daha ESKI
kalir). `dotnet build` artimli derleme icin mtime karsilastirir, "kaynak
DLL'den eski" gorunce **YENIDEN DERLEMEZ** ve bir onceki (SAHTE KUSURLU)
derlemeyi sessizce kullanmaya devam eder — sonraki `dotnet test` calistirmasi
YESIL doner ama gercekte hala BOZUK derlemeyi test etmektedir. Bu depoda
`AGENTPRISM_SQL_SNAPSHOT_REFRESH=1` ile checked-in bir taban cizgisi dosyasi
BU SEKILDE bir kez BOZULMUS (sahte terim iceren cikti taban cizgisine
yazilmis) ve fark edilene kadar 4 test sahte SUCCESS/FAILURE dongusu
uretti. **Kural**: bir kaynak dosyayi geri aldiktan (`mv`, `git checkout`,
`cp`) hemen sonra `touch <dosya>` calistir, SONRA derle — ya da direkt
`--no-incremental` kullan. Refresh/generate gibi CIKTI-YAZAN bir komutu
supheli bir derlemeden HEMEN sonra calistirmadan once bu adimi atlama.

## 🚨 `dotnet test --filter` SESSIZCE YUTULUR (Faz 77)

- **`--filter` MTP'de YOKTUR ve hata da vermez — tum paketi kosar.** 2026-08-20'de
  olculdu: `dotnet test tests/AgentPrism.Core.UnitTests -c Release --no-build
  --filter CapabilityExampleTests` **1004 testin tamamini** kosar ve yesil doner.
  Daralttigini sanirsin; kosum suresi seni yanilmaz cunku paket zaten hizlidir.
  Tehlike yesil bir yanlistir: bir kapiyi "kostum" diye isaretlersin ama aslinda
  hangi testin gectigini bilmezsin.
  **Dogru bicim derlenmis ikiliyi DOGRUDAN cagirmaktir:**
  `./artifacts/bin/<Proje>/release/<Proje> --filter-class "*Ad*" "*Ad2*"`
  (birden cok desen bosluk ile ayrilir). Secenekler: `--filter-class`,
  `--filter-method`, `--filter-namespace`, `--filter-uid` ve `--filter-not-*`.
  Olcum: 1004 test → **15 test / ~2 sn**.
  🚨 **Bayat komut dokumanlarda duruyor:** `docs/73`, `docs/74` ve `docs/75`
  `dotnet test --filter <Ad>` yazar. Oradan kopyalama; uc dosya da kapanmis
  kayittir ve geriye donuk duzeltilmez.

## Kusur mu, kirilgan test mi — ayirmadan rapor etme

- **🚨 Bir test tam pakette duser, tek basina gecerse "bayat" demeden ONCE
  paketi BIR KEZ DAHA kostur.** 2026-08-08'de yasandi: PostgreSQL paketinde
  `EvalStoreContract.AddCaseAsync_es_zamanli_terfiler_farkli_seq_uretir` dustu
  (`SqlEvalStore.AddCaseAsync` — "5 denemede sira numarasi atanamadi"), tek
  basina gecti, ikinci tam kosumda **870/870** yesil geldi. Yuk altinda
  kirilgan bir test; degisiklikle ilgisi yoktu. Aday listesine **F-102** olarak
  yazildi — sessiz birakilmadi.
  **Ikinci vaka (2026-08-24, Faz 95 kapanisi):** `AgentPrism.Workflows.UnitTests.WorkflowHumanInTheLoopTests.A_rejection_response_also_flows_through_execution`
  tam kosumda dustu (`Sequence contains no matching element`), tek basina VE
  proje tek basina (102/102) gecti. Faz 95 `src/AgentPrism.Workflows`'a hic
  dokunmadi — nedensellik dislaniyor. Ucuncu tam kosum 20/20 proje yesil
  donuncu dogrulandi. F-102'nin sinifi `Ui.E2ETests`'e ozgu degil, tam
  cozum kosumunda HERHANGI bir projede gorulebilir.
- **🚨 `Templates.Tests` GLOBAL `~/.templateengine/packages.json` dosyasina
  yazar — paralel kosumda birbirini kilitler** (2026-08-16, Faz 58). Belirti:
  `Most_minimal_combination_compiles_with_zero_warnings` `ExitCode 70` ile
  duser, mesaj: *"Failed to retrieve template packages from provider 'Global
  Settings'. Details: The process cannot access the file
  '/Users/<kullanici>/.templateengine/packages.json' because it is being used
  by another process"* + `Sequence contains no matching element`. Bu bir URUN
  KUSURU DEGILDIR: `dotnet new` sablon deposu kullanici genelindedir, test
  basina yalitilmaz. **Ayirt etme**: tek basina kostur —
  `dotnet test tests/AgentPrism.Package.Tests -c Release --no-build`; 10/10
  gecerse kilit cakismasidir. Ayni kosumda iki kez ust uste duserse gercek
  kusurdur. Faz 58'de tam paket bir kez 3695/3695 yesil, ikinci kez bu tek
  testte dustu, izole kosumda 10/10 gecti.
- **🚨 `AgentPrism.Ui.E2ETests` tam kosarken (55 test) zamanlama yarisi altinda
  kirilgan tekil testler cikabilir — F-102'nin Playwright hali** (2026-08-19,
  Faz 65 kapanisi). `Runs_button_on_session_page_navigates_to_filtered_list`
  tam kosumda 3 denemeden 2'sinde dustu (`tbody tr` sayisi dugme etiketiyle
  eslesmeden okundu), izolasyonda 3/3 gecti. Kok sebep tarayici/`Docker`
  kaynak cekismesi, urun kusuru degil. **Ayirt etme**: ayni desen — tek basina
  kostur, gecerse yuk altinda kirilganlik, ikinci tam kosumda da duserse
  gercek kusur. Aday: **F-122**.
  **Ikinci vaka (2026-08-20, Faz 77): `Eval_suite_is_created_case_added_and_run_passes`** —
  ayni desen, bu kez `fill` sirasinda *"element was detached from the DOM"* (`UiTests.cs:1113`).
  Uc adim da kosuldu: tam kosumda dustu, izolasyonda 1/1, ikinci tam kosumda 56/56 gecti.
  Aday: **F-130**. Iki vaka ayni sinif -- `Ui.E2ETests` tam kosumda kaynak cekismesine acik.
  **Ucuncu vaka (2026-08-23, Faz 90 kapanisi): AYNI test** (`Eval_suite_is_created_case_added_and_run_passes`). Uc adim yine kosuldu: tam kosumda 4956/4957 (bu tek test dustu), izolasyonda 1/1 (7,7 sn), ikinci tam kosum cikis kodu 0. Faz 90 C#'a, TypeScript'e ve frontend'e HIC dokunmadi -- nedensellik da dislaniyor. Uc vaka artik F-130'u bir **kirilganlik sinifi** olarak sabitliyor: `Ui.E2ETests` tam kosumda yalitilmali.

- **🚨 `dotnet test ... | grep ... | head -N` KOSUMU ERKEN KESER.** `head` N
  satiri alinca boruyu kapatir, `dotnet test` SIGPIPE alir ve kalan test
  projeleri **hic kosmaz**; kabuk yine de `exit 0` doner ve kosum basarili
  GORUNUR. 2026-08-08'de yasandi: 16 projeden yalniz 9'u kostu. Tam paketi
  **dosyaya yaz**, sonra dosyayi filtrele.

- **🚨 Tüketici testleri GLOBAL NuGet önbelleğine takılır — değişiklik görünmez olur** (2026-08-19, Faz 73): MinVer sürümü git yüksekliğinden türediği için iki commit arasındaki her `dotnet pack` **aynı** sürüm dizesini üretir (`0.0.0-preview.0.271`). NuGet bir sürümü global paket klasörüne BİR KEZ açar ve sonra hep onu kullanır; yeniden paketlenen `.nupkg` hiç açılmaz. Belirti: kodda yaptığın değişiklik `TemplateFixture` tabanlı testlerde **hiç görünmez** ve teşhis yanlış yere gider (Faz 73'te bir analyzer değişikliği üç koşum boyunca yok sanıldı). Çözüm fixture'a girdi: `TemplateFixture.ClearGlobalPackageCache` paketlenen sürümün `~/.nuget/packages/agentprism*/<sürüm>` dizinlerini siler. **Depo dışında elle bir tüketici denerken aynı dizini sen de sil.**

## 🚨 Paralel test SINIFLARI migration deadlock'u uretir (Faz 76)

Olculdu: tam surunun dort kosumunun **ikisinde** SQL Server entegrasyon testleri
20–32 test dusurdu — hepsi `SqlServerSchemaFixture.InitializeAsync` icinde
`0017_approval_conditions` deadlock'u. Hata bir migration adi soyler, yarisi degil.

Sebep tasarimdadir: her sozlesme testi SINIFI kendi semasini kurar
(`NewSchemaName()`) ve tum migration setini uygular; xunit siniflari paralel
kosturur. Desen uc saglayicida da vardi, yalniz SQL Server'in DDL kilitleri
cekismeyi gorunur yapiyor. Cozum: uc `*TestContext.CreateAsync` yolunda
`SemaphoreSlim(1, 1)` — sema kurulumu olculen sey degildir. **Kasitli**
eszamanlilik testleri etkilenmez; onlar `Migrations.ApplyAsync()`'i dogrudan
cagirir.

- **Izole yesil, tam kosum kirmizi** ise once paralellige ve paylasilan
  altyapiya bak (tek proje kosumu 558/558 geciyordu).
- **"failed: 0" ama toplam sayi dusmusse kosum eksiktir.**
  `grep -cE "Test run summary:"` ile proje sayisini da say — beklenen **16**.

## Faz 91 taban ölçümü — 2026-08-23

Ölçümler macOS/Apple Silicon üzerinde, temizleme sonrası ve tüm MSBuild alt
süreçlerinde `MSBUILDDISABLENODEREUSE=1` ile alındı. Süreler wall-clock'tur.

| Kapı | Taban sonucu |
|---|---:|
| `dotnet build` — frontend açık, soğuk | 58,60 s · 0 warning · 0 error |
| `dotnet build` — frontend açık, sıcak | 6,05 s |
| `dotnet build` — frontend kapalı, soğuk | 52,88 s |
| `dotnet build` — frontend kapalı, sıcak | 6,09 s |
| Tam `dotnet test AgentPrism.slnx --no-build` | 164,43 s · exit 1 |
| `dotnet pack --no-build` | 6,62 s · exit 0 |
| `dotnet format --verify-no-changes --no-restore` | 76,47 s · exit 0; workspace yükleme uyarısı yazdı |
| `docs-site` `npm run check` | 9,93 s · exit 1; yerel Astro koşumu Node 20.19.4'ü, Astro'nun istediği `>=22.12.0` yerine gördü |
| Kapanış kapıları toplamı (yukarıdaki beş ayrı wall-clock koşum) | 316,05 s |

Tam testin 19 proje özeti:

| Proje | Süre | Sonuç |
|---|---:|---|
| `Anthropic.UnitTests` | 0,244 s | 44/44 |
| `Azure.UnitTests` | 0,222 s | 52/52 |
| `Client.UnitTests` | 0,149 s | 3/3 |
| `Google.UnitTests` | 0,369 s | 49/49 |
| `Mcp.UnitTests` | 0,745 s | 30/30 |
| `OpenAI.UnitTests` | 0,569 s | 87/87 |
| `Embedded.Tests` | 5,717 s | 3/3 |
| `Core.UnitTests` | 6,499 s | 1801/1801 |
| `Cli.FunctionalTests` | 8,575 s | 15/15 |
| `Generators.UnitTests` | 6,742 s | 156/156 |
| `Voice.UnitTests` | 0,764 s | 43/43 |
| `Workflows.UnitTests` | 3,504 s | 102/102 |
| `Testing.UnitTests` | 12,152 s | 32/32 |
| `Ui.E2ETests` | 29,811 s | 0/57 — yerel koşum altyapısı kırmızı |
| `Sqlite.IntegrationTests` | 65,127 s | 591/592 — bir test kırmızı |
| `PostgreSql.IntegrationTests` | 94,461 s | 638/638 |
| `Templates.Tests` | 105,001 s | 32/32 |
| `AspNetCore.FunctionalTests` | 155,219 s | 647/647 |
| `SqlServer.IntegrationTests` | 156,919 s | 574/574 |

Bu tablo ürün kusuru iddiası değildir. `Ui.E2ETests` için F-130 kırılganlık sınıfı
zaten üç kez kanıtlanmıştır. SQLite kırmızı yolu Faz 91 değişiklikleriyle
nedensel olarak ilişkili değildir; Faz 91 sonu aynı test ikinci kez izole ve
tam koşumla ayrıştırılmalıdır.

## Faz 91 sonrası ölçümü — 2026-08-23

Aynı makinede, aynı `MSBUILDDISABLENODEREUSE=1` koşuluyla tekrarlandı. Temiz
build için `dotnet clean` sonrası ölçüm alındı. Bu kez Docker ve Playwright
altyapısı hazırdı; tam test paketi 19 projenin tamamında yeşil döndü.

| Kapı | Sonuç |
|---|---:|
| `dotnet build` — frontend açık, soğuk | 49,66 s · 0 warning · 0 error |
| `dotnet build` — frontend açık, sıcak | 6,09 s · `npm run build` koşmadı |
| `dotnet build` — frontend kapalı, soğuk | 48,58 s · 0 warning · 0 error |
| `dotnet build` — frontend kapalı, sıcak | 6,50 s · 0 warning · 0 error |
| Tam `dotnet test AgentPrism.slnx --no-build` | 173,13 s · exit 0 |
| `dotnet pack --no-build` | 5,50 s · exit 0 |
| `dotnet format --verify-no-changes --no-restore` | 76,33 s · exit 0; workspace yükleme uyarısı yazdı |
| `docs-site` `npm run check` — default PATH | 9,77 s · exit 1; Node 20.19.4 < Astro gereksinimi `>=22.12.0` |
| `docs-site` `npm run check` — CI uyumlu Node 22.23.2 | 23,36 s · exit 0; dört alt kapı yeşil |
| Kapanış kapıları toplamı (soğuk frontend-açık build + test + pack + format + geçen Node 22 site) | 327,98 s |

Tam testin 19 proje özeti:

| Proje | Süre | Sonuç |
|---|---:|---|
| `Anthropic.UnitTests` | 0,239 s | 44/44 |
| `Azure.UnitTests` | 0,290 s | 52/52 |
| `Client.UnitTests` | 0,193 s | 3/3 |
| `Google.UnitTests` | 0,428 s | 49/49 |
| `Mcp.UnitTests` | 0,859 s | 30/30 |
| `OpenAI.UnitTests` | 0,475 s | 87/87 |
| `Embedded.Tests` | 7,950 s | 3/3 |
| `Core.UnitTests` | 7,450 s | 1801/1801 |
| `Cli.FunctionalTests` | 13,885 s | 15/15 |
| `Generators.UnitTests` | 12,663 s | 156/156 |
| `Voice.UnitTests` | 2,631 s | 43/43 |
| `Workflows.UnitTests` | 4,504 s | 102/102 |
| `Testing.UnitTests` | 14,913 s | 32/32 |
| `Ui.E2ETests` | 133,156 s | 57/57 |
| `Sqlite.IntegrationTests` | 75,739 s | 592/592 |
| `PostgreSql.IntegrationTests` | 104,481 s | 638/638 |
| `Templates.Tests` | 115,698 s | 32/32 |
| `AspNetCore.FunctionalTests` | 164,438 s | 647/647 |
| `SqlServer.IntegrationTests` | 166,444 s | 574/574 |

Karşılaştırma: frontend-açık soğuk build 8,94 s hızlandı; sıcak build iki
koşumda da `npm run build` çalıştırmadı. Test süresi 8,70 s arttı; bu nedenle
tam testte iyileşme iddia edilmez. Default PATH ile site kapısı kırmızı olsa da
CI uyumlu Node 22.23.2 ile dört alt kapının tamamı yeşildir. Geçen kapanış
toplamı 327,98 s'dir; baseline'daki site ölçümü Node 20 yüzünden yarıda kaldığı
için bu toplamlarla doğrudan hız yüzdesi çıkarılmaz.

### Faz 91 kapanış tekrarında F-130 ayrıştırması — 2026-08-23

Kapanış kapılarının ikinci tekrarında tam suite `AgentPrism.Ui.E2ETests`
56/57 ile kırmızı oldu: önce `Pending_request_card_can_be_answered`, ayrı
UI koşumunda `Runs_button_on_session_page_navigates_to_filtered_list`.
İki case de derlenmiş test ikilisinde `--filter-method` ile izole 1/1 geçti.
Bu, ürün kusuru değil; tam Playwright suite'inin kaynak/zamanlama çekişmesine
duyarlı F-130 sınıfının yeni kanıtıdır. İlk tam kapanış koşumu 57/57 ve 19
proje ile yeşildi. Kapanış raporu tekrarlanabilirliği korumak için son kırmızı
çıkış kodunu saklamaz; izole koşum ayrıştırma adımıdır.

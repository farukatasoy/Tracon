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
  **Ucuncu vaka (2026-08-23, Faz 90 kapanisi): AYNI test** (`Eval_suite_is_created_case_added_and_run_passes`). Uc adim yine kosuldu: tam kosumda 4956/4957 (bu tek test dustu), izolasyonda 1/1 (7,7 sn), ikinci tam kosum cikis kodu 0. **Kapanış (2026-08-26):** kök yarış bulundu: ilk `GET /cases` pending iken kullanıcı `Add case`e basabiliyor, sonra gelen boş yanıt yeni satırı siliyordu. UI ilk yükleme bitene kadar `Add case` ve `Save cases`i kapatır; frontend regression testi önce kırmızı, düzeltmeden sonra yeşildir. F-130 açık kayıt değildir.
- **Kaynak nedenselliğini ölçme yolu değişmedi.** Bir tam koşum kırılması faz
  değişikliğinden şüphe ettiriyorsa `git stash push -u` → tabanı derle → tam
  koşum → `git stash pop` uygula. Worktree kullanma; extension sample'ları yerel
  NuGet feed'ini ister ve `artifacts/package/release` worktree'de yoktur.

- **🚨 `dotnet test ... | grep ... | head -N` KOSUMU ERKEN KESER.** `head` N
  satiri alinca boruyu kapatir, `dotnet test` SIGPIPE alir ve kalan test
  projeleri **hic kosmaz**; kabuk yine de `exit 0` doner ve kosum basarili
  GORUNUR. 2026-08-08'de yasandi: 16 projeden yalniz 9'u kostu. Tam paketi
  **dosyaya yaz**, sonra dosyayi filtrele.

- **🚨 `| tail -200` erken KESMEZ ama alfabetik olarak ONCE gelen projelerin
  sonucunu GORUNMEZ kilar** (2026-09-01, Faz 130). `AgentPrism.slnx`'teki
  projeler alfabetik kosar; `AgentPrism.Core.UnitTests` ve
  `AgentPrism.Sql.Shared.UnitTests` `Ui.E2ETests`/`Voice.UnitTests`'ten CIDDI
  ONCE biter. `dotnet test AgentPrism.slnx ... | tail -200` komple kosumu
  BEKLER (SIGPIPE yok, yukaridaki tuzaktan farkli) ama yalniz SON 200 satiri
  saklar — erken projelerdeki gercek KIRMIZI satirlar sessizce disaridadir,
  koşum "temiz" GORUNUR. Faz 130'da tam bu sekilde iki bagimsiz kusur
  (`PlaywrightLocatorTests`, `SqlTextSnapshotTests` — ikisi de Faz 129'un
  kapanisinda atlanmis bayat taban cizgisi) ilk `tail -200`'lu kosumda
  gorulmedi, ikinci kosumda (tam log DOSYAYA yazilinca) ortaya cikti. **Kural**:
  `kapi.py kapanis` gibi uzun bir kapiyi HER ZAMAN tam log dosyasina yaz
  (`> log.txt 2>&1`), `tail`'i yalniz o dosyayi SONRADAN okurken kullan —
  komutun kendisine asla `| tail` ekleme.

- **🚨 Tüketici testleri GLOBAL NuGet önbelleğine takılır — değişiklik görünmez olur** (2026-08-19, Faz 73): MinVer sürümü git yüksekliğinden türediği için iki commit arasındaki her `dotnet pack` **aynı** sürüm dizesini üretir (`0.0.0-preview.0.271`). NuGet bir sürümü global paket klasörüne BİR KEZ açar ve sonra hep onu kullanır; yeniden paketlenen `.nupkg` hiç açılmaz. Belirti: kodda yaptığın değişiklik `TemplateFixture` tabanlı testlerde **hiç görünmez** ve teşhis yanlış yere gider (Faz 73'te bir analyzer değişikliği üç koşum boyunca yok sanıldı). Çözüm fixture'a girdi: `TemplateFixture.ClearGlobalPackageCache` paketlenen sürümün `~/.nuget/packages/agentprism*/<sürüm>` dizinlerini siler. **Depo dışında elle bir tüketici denerken aynı dizini sen de sil.**

## 🚨 Paralel test SINIFLARI migration deadlock'u uretir (Faz 76)

- **🚨 Ayni sinifin YENI belirtisi: cekisme test GOVDESINDE degil FIXTURE ACILISINDA patlar** (2026-09-06, Faz 148): SQL Server tam kosumda 13/655 dustu, hepsi `SqlServerSchemaFixture.InitializeAsync` icinde `Migration '0001_initial' … Execution Timeout Expired`. Dusen testlerin fazla hic ilgisi yoktu — yeni migration'i sanik sanmak icin her sebep vardi. Ayirt eden iki sey: `kapi.py`'nin kendi izole kosumu 13/13 gecti, paket tek basina 655/655 verdi. `mssql/server` burada amd64 emulasyonundadir (K-386), fixture acilisi zaten yavastir. **Kural**: migration ekledigin fazda SQL Server dusuyorsa once paketi TEK BASINA kosur — `0001_initial`'in timeout'u seninkiyle ilgili degildir.

- **🚨 Full solution test run'inda test PROJELERI sinirsiz paralel kosmaz** (2026-08-25, Faz 100 sonrasi): `dotnet test AgentPrism.slnx` 24 test executable'i ayni anda baslatinca Docker container'lari, Playwright, functional host'lar ve `AgentPrism.Package.Tests` icindeki `dotnet pack` ayni CPU/RAM butcesine saldirir. Belirti urun hatasi degildir: `SourceLanguageTests` 5 sn Regex timeout'u ve CLI'nin 10 sn HTTP timeout'u yalniz tam run'da duser; ikisi de izolasyonda saniyeler icinde gecer. **Uc** worker bile Package build, PostgreSQL ve functional host'lari birlikte dakikalara iterdi. Cozum: gate ve CI tam run'lari **`-maxcpucount:1`** ile kosar. Bu, toplam wall-clock suresini artirir; ancak Docker ve package testi ayni anda makineyi doyurmadigi icin kaynak cekismesinden uzayan tekil testleri ve sahte timeout'lari kaldirir.

- **🚨 `dotnet new sln` varsayilan uzantisi SDK'ya gore degisir** (2026-08-28):
  Yerel SDK `.sln`, CI SDK'si `.slnx` uretebilir. Sonraki `dotnet sln` veya
  `dotnet build` komutunda sabit bir dosya adi kullanan test bu nedenle yalniz
  bir ortamda kirilir. Test fixture'i formati acikca secmelidir:
  `dotnet new sln --format slnx`; sonraki komutlar ayni `.slnx` adini kullanir.

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

## 🚨 `HttpListener`'ı iki kez kapatmak BAŞKA testin portunu çalar

`Stop()` ve `Close()` ikisi de uç nokta yöneticisinin prefix-kaldırma yolundan
geçer ve o yol **çıkarken portu bağlayabilir** — gözlenen yığın
`Close → RemoveListener → RemovePrefixInternal → GetEPListener` ile
`Address already in use` verdi. İkisini birden çağırmak, o anda boş port
arayan her testle yarışır.

Kusurun okunması zordur: hata **`Dispose` içinde**, testin kendi iddiaları
geçtikten sonra düşer ve **tek başına koşan test hep yeşildir**. Kırılganlık
gibi görünür; değildir. Ölçüm: tam projede 2/2 düştü, izole 2/2 geçti,
düzeltmeden sonra tam projede 3/3 geçti.

Kural: bir dinleyiciyi **tam olarak bir kez** yık. Sınıf taraması (2026-08-31):
`HttpListener` ve "port 0'ı rezerve et, bırak, sonra bağlan" deseni depoda
**tek dosyadadır** (`ImageAttachmentWriterTests`); başka vaka yok.

## 🚨 Bir global sahte `TimeProvider` kaydı arka plan servisini de dondurur (Faz 128)

`AgentPrismTestHost.StartAsync`'te `services.AddSingleton<TimeProvider>(fakeClock)`
yalnız test edilen KODU değil, **host'un kendi arka plan servislerini** de
etkiler — `JobWorkerBackgroundService`'in poll/kira-yenileme döngüsü AYNI
`TimeProvider`'ı okur. Elle ilerleyen bir sahte saat (`ManualTimeProvider`,
yalnız `Advance()` çağrılınca ilerler) o döngüyü **sonsuza kadar dondurur** —
ölçüldü: `UseScheduling` ile kuyruğa alınan bir `run` 30 saniye boyunca
`Queued`'dan hiç çıkmadı, test zaman aşımına uğradı. Senkron (job worker'sız)
bir senaryo sahte saatle sorunsuz çalışır; kuyruklu/arka-plan bir senaryoyu
test ederken ya **gerçek saat + kısa gerçek süre** kullan (örnek: kısa bir
`MaxDuration` + `Task.Delay` ile gerçekten geciken bir tool), ya da yalnız
SENKRON yolu sahte saatle test et. Vaka: `RunDeadlineTests.Deadline_is_enforced_on_the_queued_durable_run_path_too`.

## Kapanış kapısı taban ölçümleri

Faz 91 taban/sonrası wall-clock ve proje-başına sonuç tabloları
[`test-kosum-olcumleri.md`](test-kosum-olcumleri.md)'ye taşındı (Faz 101 —
bu dosya bütçeyi aştı). Aktif tuzak değil, tarihsel ölçüm kaydıdır.

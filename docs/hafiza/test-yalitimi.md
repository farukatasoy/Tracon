# Test Yalitimi ve Tam Kosum Kirilganligi

> Bir test TEK BASINA gecip TAM kosumda dustugunde buraya bak. Test
> **yazimi** tuzaklari [`test-altyapisi.md`](test-altyapisi.md), kosumun
> ASILMASI/eksik kalmasi [`test-kosum-tuzaklari.md`](test-kosum-tuzaklari.md).
>
> **Bu sinif SEKIZ kez kayda gecti** — tamami
> [`test-yalitimi-vakalari.md`](test-yalitimi-vakalari.md)'dedir (sinif Faz
> 103'te acildi; Faz 82 · 130 · 134 · 141 ayri testlerle tekrarladi, 2026-09-04
> surec denetiminin taban kosumu ve 2026-09-12 AOT `publish` vakasi eklendi).
> Her seferinde ayirt etme ELLE yapildi.
> `python3 scripts/kapi.py kapanis` artik dusen testi otomatik izole tekrar
> kosar ve hukmu basar (`isolate_failed_tests`) — cikis kodunu DEGISTIRMEZ,
> yalnizca bir sonraki adimi soyler. Ayrim: 2026-09-04 surec denetimi
> ([kesif](../kesif/2026-09-04-surec-denetimi.md)).

## Vakalar

Olculmus vakalarin tamami ayri bir defterdedir:
[`test-yalitimi-vakalari.md`](test-yalitimi-vakalari.md). Bir testi veya deseni
daha once gorup gormedigini oradan arar, bastan sona okumazsin:
`grep -n '<TestAdi>' docs/hafiza/test-yalitimi-vakalari.md`.

## Kusur mu, kirilgan test mi — ayirmadan rapor etme

> Faz 156'da `test-kosum-tuzaklari.md`'den taşındı: konu bu dosyanın "tek
> başına geçip TAM koşumda düşen test" temasıyla örtüşüyordu.

- **🚨 Bir test tam pakette duser, tek basina gecerse "bayat" demeden ONCE
  paketi BIR KEZ DAHA kostur.** 2026-08-08'de yasandi: PostgreSQL paketinde
  `EvalStoreContract.AddCaseAsync_es_zamanli_terfiler_farkli_seq_uretir` dustu
  (`SqlEvalStore.AddCaseAsync` — "5 denemede sira numarasi atanamadi"), tek
  basina gecti, ikinci tam kosumda **870/870** yesil geldi. Yuk altinda
  kirilgan bir test; degisiklikle ilgisi yoktu. Aday listesine **F-102** olarak
  yazildi — sessiz birakilmadi.
  **Ikinci vaka (2026-08-24, Faz 95 kapanisi):** `Tracon.Workflows.UnitTests.WorkflowHumanInTheLoopTests.A_rejection_response_also_flows_through_execution`
  tam kosumda dustu (`Sequence contains no matching element`), tek basina VE
  proje tek basina (102/102) gecti. Faz 95 `src/Tracon.Workflows`'a hic
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
  `dotnet test tests/Tracon.Package.Tests -c Release --no-build`; 10/10
  gecerse kilit cakismasidir. Ayni kosumda iki kez ust uste duserse gercek
  kusurdur. Faz 58'de tam paket bir kez 3695/3695 yesil, ikinci kez bu tek
  testte dustu, izole kosumda 10/10 gecti.
- **🚨 `Tracon.Ui.E2ETests` tam kosarken (55 test) zamanlama yarisi altinda
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
- **`ObjectToolAotPackageTests` — ayni desenin UCUNCU vakasi, bu kez Native AOT
  `publish`'inde** (2026-09-12). `…publishes_under_Native_AOT_without_a_trim_warning_and_runs`
  ayni degisiklik kumesiyle uc tam kosumda: GECTI · DUSTU · GECTI (21/21 proje,
  7040 test, 0 dusen), izole de gecti. AOT `publish` tam bir native derlemedir ve
  `-maxcpucount:1` altinda bile Docker/Playwright ile ayni butceyi paylasir.
  Ayirt etme yukaridakiyle ayni.
  Aday: **F-130**. Iki vaka ayni sinif -- `Ui.E2ETests` tam kosumda kaynak cekismesine acik.
  **Ucuncu vaka (2026-08-23, Faz 90 kapanisi): AYNI test** (`Eval_suite_is_created_case_added_and_run_passes`). Uc adim yine kosuldu: tam kosumda 4956/4957 (bu tek test dustu), izolasyonda 1/1 (7,7 sn), ikinci tam kosum cikis kodu 0. **Kapanış (2026-08-26):** kök yarış bulundu: ilk `GET /cases` pending iken kullanıcı `Add case`e basabiliyor, sonra gelen boş yanıt yeni satırı siliyordu. UI ilk yükleme bitene kadar `Add case` ve `Save cases`i kapatır; frontend regression testi önce kırmızı, düzeltmeden sonra yeşildir. F-130 açık kayıt değildir.
- **🚨 "Yük altında kırılgan" hükmü, testin GEÇME sebebi bir zamanlama eşiğinin ALTINDA kalmaksa YANLIŞTIR** (2026-09-09, K-743). `ModelHealthSingletonTests.Health_check_runs_on_only_one_instance` bu dosyada Faz 103'ten beri kırılgan
  listesindeydi. Değildi: `LeaseDuration=1 sn` ile `SingletonGuard`'ın yenileme aralığı kiranın süresine EŞİTTİ, kira t≈1 sn'de
  sahibi çalışırken düşüyor ve ikinci örnek devralıyordu. Test yalnız 500 ms'lik penceresi o ana ULAŞMADIĞI için geçiyordu; CI'da
  yük altında pencere taşınca (test süresi 1,24 sn) gerçek kusur ortaya çıktı. **Ayırt etme yöntemi**: testi tekrar koşmak yetmez —
  testin geçmesini sağlayan zamanlama varsayımını bul ve onu BÜYÜT. Pencere 1500 ms yapılınca 3/3 kırmızı, aynı sayılarla
  (`providerA=16, providerB=9`): deterministik, yani kırılgan değil. Düzeltme ürün tarafındaydı (validator kısa kirayı reddediyor);
  test artık saatten bağımsız (kira 5 dk, süresi testin içinde dolamaz). Gerçek zamanlı pencere kullanan bir test yazarken kural:
  **pencere uzarsa iddia hâlâ doğru kalmalı**; yalnız pencere kısa olduğu için doğruysa o test bir kusuru saklıyordur.
  Sınıf taraması aynı kurgudan üç tane daha buldu — `McpDiscoverySingletonTests` (birebir aynı: 1 sn kira + 500 ms pencere + XOR),
  `CanaryEvaluationServiceTests`, `RunReconciliationTests` — dördü de 5 dk'lık kiraya geçti. Bir XOR iddiası kurulurken sorulacak soru:
  **bu iddiayı yanlışlayabilecek bir zamanlayıcı var mı, ve testin süresi ona ulaşabilir mi?**
- **Kaynak nedenselliğini ölçme yolu değişmedi.** Bir tam koşum kırılması faz
  değişikliğinden şüphe ettiriyorsa `git stash push -u` → tabanı derle → tam
  koşum → `git stash pop` uygula. Worktree kullanma; extension sample'ları yerel
  NuGet feed'ini ister ve `artifacts/package/release` worktree'de yoktur.

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

`TraconTestHost.StartAsync`'te `services.AddSingleton<TimeProvider>(fakeClock)`
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

- **Process testinde bekleme MUTLAK SÜRE değil KOŞUL olmalıdır** (Faz 157). Hazırlık için process'in stdout'a yazdığı bir satır (`HARNESS-READY`), devralma için veritabanının KENDİ `lease_until` değeri beklenir; `Task.Delay(sabit)` yüklü bir ajanda kırılgandır. `ManagedProcess.DisposeAsync` her yolda ağacı öldürür — düşen bir test öksüz worker bırakmaz.

## Playwright E2E'de yuk kaynakli gezinme zaman asimi (Faz 161)

`Tracon.Ui.E2ETests` tam kosumda **her seferinde baska bir test** 30 sn'lik
`GotoAsync` zaman asimiyla dusebiliyor (olculdu 2026-09-11: once
`Tools_screen_shows_call_count`, sonra `Skill_created_from_UI_is_listed`,
ucuncu kosum 58/58 yesil). Ucu de izole gecti.

🚨 "Tek basina geciyor" TEK BASINA yeterli DEGIL. Uc kanit birlikte arandi:
degisiklik o yuzeye hic dokunmuyor (`git status | grep Tracon.UI` bos) ·
dusen test **degisiyor** (kod kusuru ayni testi dusurur) · E2E host'u yeni
yetenegi hic kaydetmiyor (`grep -rn "UseLiveVoice" tests/...E2ETests/` bos).
Biri tutmuyorsa kod yolu okunur.

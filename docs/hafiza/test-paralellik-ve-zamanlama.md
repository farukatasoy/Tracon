# Test Paralelligi ve Zamanlama Tuzaklari

> Tam kosumda paralel test siniflari/projeleri, migration fixture'lari, kaynak
> cekismesi ve scheduler zamanlamasinin urettiği yanlis negatifler. `dotnet test`
> komutu ve MSBuild tuzaklari icin: [`test-kosum-tuzaklari.md`](test-kosum-tuzaklari.md).
> Izole/tam kosum ayrimi ve vaka kayitlari icin: [`test-yalitimi.md`](test-yalitimi.md).
>
> 2026-09-19'da `test-kosum-tuzaklari.md` 16.377/16.000 B'ye ulasti. Kalici
> konu siniri paralellik ve zamanlama oldugu icin bu bolum ayrildi; 400 B madde
> tavaniyla metni kesmek kok sebep veya cozumu kaybettirirdi.

## 🚨 Paralel test SINIFLARI migration deadlock'u uretir (Faz 76)

- **🚨 Ayni sinifin YENI belirtisi: cekisme test GOVDESINDE degil FIXTURE ACILISINDA patlar** (2026-09-06, Faz 148): SQL Server tam kosumda 13/655 dustu, hepsi `SqlServerSchemaFixture.InitializeAsync` icinde `Migration '0001_initial' … Execution Timeout Expired`. Dusen testlerin fazla hic ilgisi yoktu — yeni migration'i sanik sanmak icin her sebep vardi. Ayirt eden iki sey: `kapi.py`'nin kendi izole kosumu 13/13 gecti, paket tek basina 655/655 verdi. `mssql/server` burada amd64 emulasyonundadir (K-386), fixture acilisi zaten yavastir. **Kural**: migration ekledigin fazda SQL Server dusuyorsa once paketi TEK BASINA kosur — `0001_initial`'in timeout'u seninkiyle ilgili degildir.

- **🚨 Full solution test run'inda test PROJELERI sinirsiz paralel kosmaz** (2026-08-25, Faz 100 sonrasi): `dotnet test Tracon.slnx` 24 test executable'i ayni anda baslatinca Docker container'lari, Playwright, functional host'lar ve `Tracon.Package.Tests` icindeki `dotnet pack` ayni CPU/RAM butcesine saldirir. Belirti urun hatasi degildir: `SourceLanguageTests` 5 sn Regex timeout'u ve CLI'nin 10 sn HTTP timeout'u yalniz tam run'da duser; ikisi de izolasyonda saniyeler icinde gecer. **Uc** worker bile Package build, PostgreSQL ve functional host'lari birlikte dakikalara iterdi. Cozum: gate ve CI tam run'lari **`-maxcpucount:1`** ile kosar. Bu, toplam wall-clock suresini artirir; ancak Docker ve package testi ayni anda makineyi doyurmadigi icin kaynak cekismesinden uzayan tekil testleri ve sahte timeout'lari kaldirir.

- **🚨 Timeout testi gercek saatin callback'ini dar bir pencerede beklemez** (2026-09-19, Ubuntu CI): drain testi 150 ms timeout'un ardindan iki saniye icinde donmesini bekliyordu; yüklü CI scheduler timer callback'ini geciktirince test, urun sozlesmesi yerine makinenin anlik kapasitesini ölçtü. Timeout davranışı `TimeProvider` alan kodda kontrol edilebilir bir test clock ile ve timer'i testin tetiklemesiyle kanıtlanır. Gercek zaman yalnız entegrasyon veya kabul seviyesinde ölçülür.

- **🚨 Bir node handler'inin baslamasi, event stream'inin o node'a ulastigini KANITLAMAZ** (2026-09-19, Ubuntu CI): MAF handler'i calistirip testin `TaskCompletionSource`unu tamamladiktan sonra `ExecutorInvoked` olayini yayimlayabilir. Test o arada deadline'i tetiklerse, run baslamis gorunur ama onceki node'un olaylari akimdan okunmadan kesilir. Timeout testi hem handler'in basladigini hem de hedef node'un `ExecutorInvoked` olayinin stream'e yazildigini bekler; sonra saati tetikler.

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

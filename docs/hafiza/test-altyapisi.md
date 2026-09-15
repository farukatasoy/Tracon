# Test Altyapisi Tuzaklari

> xunit.v3/MTP, Shouldly, Testcontainers, Playwright.
>
> Paketi KOSARKEN cikan tuzaklar (`dotnet test`, MSBuild, paralellik) AYRI
> dosyadadir: [`test-kosum-tuzaklari.md`](test-kosum-tuzaklari.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.
>
> 2026-08-01…03 arasindaki on bes madde (xunit.v3/VSTest ayrimi, Testcontainers
> 4.13 ctor'u, Shouldly/Meziantou cakismalari, erken Playwright locator tuzaklari)
> butce yuzunden [`../arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md)'ye
> tasindi (Faz 76). Kurallar hala gecerli — bir tuzak ararken oraya da grep at.

## Circir testi ve uretilen dosya okuma (Faz 74)

- **🚨 Statik ozellik baslaticilari BEYAN SIRASINDA kosar** (2026-08-19, olculdu): `CapabilityEntryPoints`'te `RepositoryRoot` toplayicilardan SONRA beyan edilmisti; toplayicilar kostugunda deger hala `null`'di ve `TypeInitializationException` verdi. Bir toplayicinin ihtiyac duydugu deger ya EN BASTA beyan edilir ya da `Lazy<T>` ile ertelenir — `Lazy` alani da consumers'tan **once** beyan edilmelidir, cunku alan baslaticisi da sirayla kosar.
- **🚨 Derlenmis XML dokumanini okuyan kapi, derlenmemis cozumde SESSIZCE YESIL gecer**: XML yoksa "hic kapsanmayan uye yok" sonucu cikar. Kapi once "her giris noktasi icin bir dokumanli uye bulundu mu?" diye sormali ve bulunamayanlari "once cozumu derle" mesajiyla dusurmelidir.
- **Test derlemesinin kendi dizini yapilandirmayi soyler**: `UseArtifactsOutput` altinda `AppContext.BaseDirectory` son segmenti `release` veya `debug`'dir. Paket XML'i `artifacts/bin/<Paket>/<yapilandirma>_net10.0/<Paket>.xml` altindadir; **dizin adi ile dosya adi eslesmelidir**, yoksa bagimliligin baska bir paketin ciktisina kopyalanan XML'i ikinci kez okunur.
- **Ornek metnini denetleyen iddia yabanci uyeler icin ACIK bir liste ister**: `Add*`/`Use*`/`Map*` cagrilarinin Tracon'e ait olup olmadigi ad sekline bakarak ayirt edilemez (`AddSingleton`, `AddHealthChecks`, `MapHealthChecks` Microsoft'undur). Liste yalnizca **Microsoft** uyelerini tasir; oraya bir Tracon uyesi eklemek incelemede tam olarak yanlis iddia olarak gorunur.
- **Kaynak dili kapisi test dosyalarini da tarar**: yol adinda Turkce harf kullanan bir test (`"Sipariş Servisi"`) `SourceLanguageTests`'i kizartir. Unicode yolu denemek icin Turkce olmayan bir harf sec (`Café`, `Órder`).

## Kume karsilastirmasi (Faz 78)

- **🚨 `HashSet.ShouldBe(...)` SIRALI esitlik denetler.** MSBuild proje sirasini
  garanti etmez; iki projeli bir cozumde `["First", "Second"]` bekleyen bir
  kume karsilastirmasi `["Second", "First"]` gordu ve dustu. Cozum
  `Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()` +
  liste karsilastirmasi. **Ikinci tuzak:** `ShouldContain("dize")` bir
  `HashSet<string>` uzerinde LINQ `Contains`'e baglanir ve `MA0002` ile
  **DERLEMEYI KIRAR** — karsilastirici ister; predicate asiri yuklemesi
  (`ShouldContain(x => ...)`) veya siralanmis liste kullan.
- **🚨 Bir derleme hatasi test kosumunu SESSIZCE eskitir.** `dotnet build … |
  grep error` ile hatayi gorup yine de test binary'sini kosmak **onceki**
  surumu olcer ve yesil gorunur. Faz 78'de bu iki kez oldu (mutasyon denetimi ve
  `MA0002`). Kosumdan once derlemenin gercekten yesil oldugunu dogrula.
- **🚨 Art arda kosan Docker tabanli paketlerde `Testcontainers` teardown'i
  yarisa girer** (Faz 81): her test `Passed` VE `Test Assembly Cleanup Failure`
  ile ikiletir (`Passed: N, Failed: N, Total: 2N`); gercek assertion asla
  KIRMIZI degildir. Ayirt etme: izole kostur (`dotnet test tests/<Paket>`),
  gecerse kaynak cekismesi. Vaka:
  [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

## Sevk edilen xunit.v3 test taban sınıfları: `xunit.v3` DEĞİL `xunit.v3.extensibility.core` (Faz 98)

Bir paket `[Fact]`/`[Theory]` taşıyan ABSTRACT taban sınıflar sevk ediyorsa
(kendisi çalıştırılabilir bir test PROJESİ değil, tüketicinin test projesinin
referans vereceği bir KÜTÜPHANE) `xunit.v3` paketi YANLIŞ seçimdir: onun
`buildTransitive` özellikleri `<OutputType>Exe</OutputType>` dayatır (MTP'nin
giriş noktası) ve kütüphane derlemesini kırar. `xunit.v3.extensibility.core`
AYNI `xunit.v3.core.dll`'i, bu zorunluluk olmadan verir — paketin kendi
açıklaması "test yazarları xunit.v3'ü kullanmalı" der, ama bu paket bir test
yazarı değil, test yazarının projesinin referans vereceği kütüphanedir.
`Shouldly` normal PackageReference kalır (private değil) — tüketicinin
kalıtılan `[Fact]` gövdeleri Shouldly çağırır. Ayrıca: bu paket `src/`
altındaysa `tests/Directory.Build.props`'un `<Using Include="Xunit"/>` /
`<Using Include="Shouldly"/>` satırlarını MİRAS ALMAZ — kendi csproj'unda
tekrarlanmalı — ve `.editorconfig`'in `[tests/**/*.cs]` bölümü (CA1707 alt
çizgi serbestliği gibi) de kapsamaz; proje yoluna özel yeni bir bölüm gerekir.

## `Barrier` ile eszamanlilik testi: senkron govde sessizce SIRALI calisir (Faz 81)

- **🚨 `AllowConcurrentInvocation = true` + senkron (`Func<string>`) bir tool
  govdesi = SESSIZCE sirali calisma.** Olculdu: `FunctionInvocationProcessor.ProcessFunctionCallsAsync`
  (MEAI) esiklemeyi `Task.WhenAll(...)` ile yapar, ama bu cagri ONCE LINQ
  `select`'i MATERYALIZE eder — govde `await` ETMEDEN (senkron) bloklayan bir
  cagriya (`Barrier.SignalAndWait`) girerse, `Task.WhenAll` ikinci govdeyi
  BASLATAMAZ: birinci govde donene kadar ikinci cagri hic KURULMAZ. Uc govde x
  5 sn zaman asimi = 15 sn — sessizce, hatasiz, ama HICBIR zaman gercekten
  cakismadan. **Cozum**: govde `Task.Run(() => { barrier.SignalAndWait(...); return sonuc; })`
  ile sarilir (senkron blokaji GERCEK bir arka plan thread'ine tasir) veya
  gercekten `async`/`await Task.Yield()` iceren bir govde yazilir. Barrier
  boyutu da DENIED/atlanan cagrilari SAYMAZ — bir cagri yetkilendirme
  reddiyle govdesine hic girmiyorsa `Barrier` katilimci sayisi o cagriyi
  DISLAR, aksi halde kalan govdeler suresiz bekler (zaman asimina kadar).

## Sevk edilen sozlesme paketi (Faz 98 · 99)


- **🚨 "Hepsini bul" reflection kapisi **ilk gunden** aile/kapsam parametresi
  almalidir; kapsamsiz asiri yukleme BIRAKILMAZ ve eslesmeyen kapsam
  `ArgumentException` atmalidir** (K-610) — aksi halde yazim hatasi "hicbir
  sozlesme yok, demek ki hepsi kapsanmis" diyen yesil bir kapi uretir. Faz 98/99
  vakasi: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `xunit.v3.extensibility.core` `Assert` TASIMAZ.** `Assert.Skip` /
  `Assert.SkipWhen` `xunit.v3.assert` paketindedir ve o paket sevk edilen
  sozlesme paketinin cozulmus grafiginde YOKTUR (olculdu, `project.assets.json`).
  Kosullu atlama icin xunit v3'un `[Fact(SkipUnless = nameof(X))]` alternatifi
  de ise yaramaz: `X` **public static** olmali, dolayisiyla ornek duzeyinde
  sanal bir uyeyi (turetilmis sinifin `override` ettigi bir ozelligi) OKUYAMAZ.
  Cozum atlamak degil, **ayri bir opt-in sozlesme sinifi** yazmaktir; turetmek
  niyet beyanidir ve sessizce gecen senaryo kalmaz (K-611).
- **Bir sozlesme senaryosunun disi var mi — ihlali KASTEN uretip kirmiziyi gor.**
  Faz 99'da ham-istemci kurali icin bu yapildi: ihlal once **derlenmedi bile**
  (yalniz `Tracon.Abstractions`'a bagli bir saglayici `AsBuilder()`'a
  erisemez — o tip `Microsoft.Extensions.AI`'dedir). Bu kendi basina bir
  bulgudur: en olasi hatayi yapmak yapisal olarak zordur. Paket bilerek
  eklendiginde senaryo uc turetilmis sinifta birden dustu.
- **Yeni bir `samples/*.Tests` projesi `tests/**` gevsemelerini ALMAZ.**
  `.editorconfig`'in `[tests/**/*.cs]` bolumu path'e bakar; `samples/` altindaki
  bir test projesi CA1707 (snake_case test adi) ve xUnit1051'e takilir.
  `[samples/*.Tests/**/*.cs]` glob'u **eslesmedi** (denendi); bastirmayi projenin
  kendi `<NoWarn>`'una gerekcesiyle yazmak hem calisiyor hem de yanindaki ornek
  UYGULAMALARI gevsetmiyor.
- **🚨 Derleyici-üretimi closure adina (`Method.Name`) DAYANMA** (2026-08-26,
  Faz 105): numaralama PARTIAL CLASS genelindedir — ilgisiz bir private metot
  eklemek sonraki her closure'in numarasini kaydirir ve DI anlik goruntu testi
  davranis degismeden kirilir. Ayirt etmek gerekiyorsa `ServiceProvider` kurup
  gercek `.GetType()`'i resolve et. Olcum:
  [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Satir-bazli bir tarayicida "imzanin tamami tek satirda" varsayimini ASLA
  yapma** (2026-08-27, Faz 119): cok satirli bir yapiyi sessizce yanlis kapsar —
  cokmez, olmasi gerekenden FAZLA eslesir. Gercek repo stilinde cok kosullu
  `when`/`if` neredeyse her zaman cok satirlidir. Tarayicinin kendi regresyon
  testi tek-satir ornek kullanirsa bunu yakalamaz; yakalayan adim gercek `src/`
  agacinda kosup cikan siteleri tek tek okumaktir. Vaka:
  [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Yeni bir XML `<example>`'a illüstratif bir tip adı eklemek `Tracon.
  Generators.UnitTests`'i kırar, hedef paketin kendi test projesini DEĞİL**
  (Faz 122): `tests/Tracon.Generators.UnitTests/Examples/ExampleCompilationTests`
  her `<example>` bloğunu gerçekten DERLER; `ExamplePrelude.cs` "sen yazmış
  gibi davran" tipleri (`GitAgentSource`, `ResponseQualityJudge`, vb.) stub
  olarak tanımlar. Yeni bir örnek yeni bir illüstratif ad kullanıyorsa
  (`AuditingAgentDecorator` gibi) o ada `ExamplePrelude.cs`'e bir stub eklenmeli
  — eklenmezse `CS0246` yalnız TAM çözüm koşumunda (`dotnet test Tracon.slnx`
  veya `kapi.py kapanis`) görünür, `Core.UnitTests` gibi hedef paketin kendi
  testini koşmak bunu YAKALAMAZ.
- **`FakeModelProvider.EchoesUserMessage()` ham mesajı DEĞİL, `"Echo: {mesaj}"`
  önekli metni döner** (2026-09-01, Faz 131). Geçerli JSON test etmek isteyen
  bir test bu yüzden echo modunu kullanamaz — `"Echo: {...}"` sözdizimsel olarak
  geçersiz JSON'dur. `RespondsWith(sabitMetin)` ile sabit bir yanıt kuyruklamak
  gerekir (`FakeModelProvider("...").RespondsWith(...)`).

## Sahte `null` bagimlilik ile gercek NO-OP uygulamasi AYNI SEY DEGILDIR (Faz 156)

🚨 **Fark tam olarak hata yolundadir ve testi olcmek istedigi seyden koparir.**

`SqlStoreContext.ContentProtector` test altyapisinda `null` birakiliyordu;
`ProtectedValue.Read` `?.` ile kisa devre yapip ham zarfi donduruyordu. Uretimde
`AddTracon()` bir `NullContentProtector` **kaydeder** ve onun `Unprotect`'i
sifreli bir zarf gorunce **FIRLATIR**.

Sonuc: Faz 156'nin ön kontrolu iki yesil test ve dort yesil kapiyla `EXIT=134`
verdi — yigin iziyle. Kusuru yalniz `samples/Tracon.Api` kosumu gosterdi.

**Kural:** bir "bos/varsayilan" bagimliligi test ederken **DI'in gercekten
kaydettigini** kullan. `null` birakmak yalnizca daha az kod degildir; farkli bir
kod yolu secer. Bu depodaki hazir yardimci:
`tests/Tracon.Sqlite.IntegrationTests/Infrastructure/ProtectingStoreContext.Keyless`
— uretimin kaydettigi protector'i verir, `null` degil.

Ayni sinifin diger yuzu: bir istisna atan no-op'u `try/catch` ile sarmak
yetmez, testin o yolu GERCEKTEN gormesi gerekir. Duzeltmenin kaniti iki testin
duzeltme olmadan kirmizi oldugunun olculmesidir.

## Yalitim ve tam kosum kirilganligi

Tek basina gecip tam kosumda dusen test AYRI dosyadadir:
[`test-yalitimi.md`](test-yalitimi.md) (2026-09-04'te butce ve konu icin ayrildi).

- **🚨 VARLIK taramasi kapsam DEGILDIR — cagri sayisini say** (2026-09-06,
  F-204). `RunAuthorizationCoverageTests` her (dosya, marker) cifti icin
  `IsMatch` soruyordu; bir dosya ise marker basina COK cagri tasir. Olculdu:
  `RunEndpoints.cs` **12** `CheckRunResourceAsync` cagrisi tasiyor —
  on birinin silinmesi kapiyi yesil birakirdi; `OpenAIConversationsEndpoints.cs`
  uc tane tasiyor, ki o dosya Faz 147 ve 149'un IKISININ de geri donmek
  zorunda kaldigi dosyadir. Sayim TAM esitliktir, taban degil: taban olsaydi
  bir ekleme bir silmeyi oder ve net sifirda kapi sessiz gecerdi. Bir guvenlik
  sinirini degistirmek bilincli olmalidir — sayiyi elle guncellemek o onaydir.
  **Ders: bir "hâlâ cagriliyor mu" kapisi yazarken once o cagrinin dosyada
  KAC KEZ gectigini olc; bir'den buyukse `IsMatch` yanlis aractir.**

- **🚨 Bir tarayici/kapi testi HER ZAMAN iki yonlu yazilir** (2026-09-06, F-206):
  eslesmesi gerekeni eslestirdigi KADAR, eslesmemesi gerekeni eslestirmedigi de
  kanitlanir. Cikis kodunu kirmayan bir dedektorde bu daha da onemlidir —
  gurultu sessizce normallesir. `\bShould\b` vakasi:
  [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **Sozlesme testleri `tests/Shared/` altindadir** ve saglayici basina bir entegrasyon test projesine derlenir (`Tracon.StoreContracts` ad alani). Yeni bir saglayici eklerken sozlesme testi YAZILMAZ; yalnizca kosucu sinif turetilir. SQLite bu iddianin DORDUNCU kanitidir (K-194).  
  *(2026-09-07'de `sql-saglayicilari.md`'den butce icin tasindi.)*
- **Ayrı process başlatan altyapı `tests/Shared/Infrastructure/` altındadır ve LINK'lenir, kopyalanmaz** (Faz 157): `ProcessRunner` (`RunAsync` = bitmesini bekle, `StartAsync` = uzun ömürlü + `ManagedProcess` ile `SIGKILL`), `RepoRoot`, `WorkerProcessHost`, `HarnessExecutionLog`. Tüketen proje `<Compile Include="../Shared/Infrastructure/..." Link="..."/>` ile bağlar; ikinci kopya K-411 sınıfıdır. MSBuild `nodeReuse` deadlock düzeltmesi tek bir `CreateStartInfo` gövdesindedir; iki giriş noktası onu paylaşır.

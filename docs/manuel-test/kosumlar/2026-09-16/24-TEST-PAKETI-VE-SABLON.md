# 24 — Test Paketi ve Proje Şablonu — koşum kaydı (2026-09-16, ap-s2)

> **Devir notu (oturum 15, ap-s2):** Bölüm 1–5 kısmi bitti — MT-TEST-001..013,
> 020..030, 040..045, 050..055, 060..064 = **41/41 Geçti, 0 Kaldı**. Sırada
> Bölüm 5'in devamı: `MT-TEST-070..094` (~25 case) — `python3 scripts/kapi.py
> test --proje Tracon.Package.Tests` ve benzeri tam test-suite koşumları
> içerdiği için daha ağır (dakikalar sürebilir). Ayrı bir oturumda devam
> edilmeli, oturum bütçesi (~40 CLI case) aşıldı. Ortam: şerit `ap-s2`, port
> 5082 (uygulama bu ailede çoğunlukla kullanılmıyor — yalnız MT-TEST-007
> 5081'i geçici kullandı, iş bitince kapatıldı). Global şablon kaydı bu
> oturumda `ap-s3`'ün eski yolundan `ap-s2`'nin kendi yoluna taşındı (bkz.
> aşağıdaki not) — sıradaki oturum `dotnet new list tracon-api` ile tekrar
> kontrol etmeli, paralel şeritler `dotnet new install` çalıştırırsa
> değişebilir. `~/tracon-manuel/{test-paketi,net8-deneme,aot-deneme,
> depo-disi-tuketici}` ayakta bırakıldı (bir kısmı Bölüm 5'in devamında
> tekrar kullanılabilir, tur sonunda topluca silinecek — §8).
>
> **Bu ailede bulunan tek desen: dokuz case'te (005, 008, 043, 050, 051, 052,
> 053, 060, 062, 063) beklenen sonuç metni/öncülü bayat.** Beşi dil sınırı
> (K-228, Türkçe metin yerine gerçek kod İngilizce). Biri (060) case'in kendi
> `grep`inin scope'suz olması. Biri (008) case'in kendi script'inin iki farklı
> proje adı kullanması. İkisi (062, 063) daha ciddi: 062 çok yakın tarihli bir
> karar tersine dönmesi (**K-780**, 2026-09-15, K-270'i kaldırdı —
> `Tracon.Testing` artık `net8.0`'dan da kullanılabiliyor, case'in tam
> tersini iddia ediyordu), 063 ise mekanizma yanlış anlaşılmış (paket
> AOT-analiz dışı bırakılıyor, uyarı ÜRETMİYOR). **Hiçbiri ürün kusuru
> değil.** Dosya 05'in emsaline uyularak (kural 1.1: "doküman ile kod
> çelişirse doküman yanlıştır") **spec dosyasının kendisi** de bu oturumda
> düzeltildi (`docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md`, commit
> `f3c9383e`), yalnız bu kayıt dosyasına not düşülmedi. Ayrı `HATA-S2-*`
> kaydı açılmadı.

**⚠️ Paylaşılan durum notu (kural 1.3 kapsamı dışı sapma):** Oturum
başlangıcında `dotnet new list tracon-api` şablonun `ap-s3`'ün worktree
yolundan (`/Users/farukatasoy/Desktop/projects/ap-s3/src/Tracon.Templates`)
kurulu olduğunu gösterdi — bu dosyanın hiçbir case'i ap-s3'e ait değil, muhtemelen
eski bir kalıntı (ap-s3'ün sıradaki ailesi 21-DAYANIKLILIK, şablona dokunmuyor).
MT-TEST-001'in ön koşulu ("şablon kurulu değil") ile çakıştığı için önce o kayıt
`dotnet new uninstall <ap-s3 yolu>` ile kaldırıldı, sonra `ap-s2`'nin kendi
yolundan kuruldu. Ürün kusuru değil, tur aparatı sapması.

---

## Bölüm 1 — Şablon (`dotnet new tracon-api`, Faz 37)

### MT-TEST-001 — Şablon paketten kurulur ve `dotnet new list`'te görünür

**Gerçek sonuç**
Önce paylaşılan global kayıttan `ap-s3` yolundaki kurulum kaldırıldı (yukarıdaki
not), sonra `dotnet new install ./src/Tracon.Templates` (ap-s2 worktree'sinden)
çalıştırıldı. Çıktı `Tracon control plane (ASP.NET Core)` adını ve `tracon-api`
kısa adını listeledi. `template.json:15` içinde `identity: "Tracon.Api.CSharp"`
doğrulandı (güncel `dotnet` SDK 10.0.100 `dotnet new install` çıktısında ayrı bir
"identity" satırı basmıyor, yalnız isim/kısa ad/dil tablosu — bu SDK'nın kendi
çıktı biçimi, kusur değil). `dotnet new list tracon-api` beklenen adı ve `[C#]`
dilini gösterdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-002 — En yalın birleşim (`memory`+`openai`+`ui:false`) sıfır uyarıyla derlenir

**Gerçek sonuç**
`dotnet new tracon-api -n Yalin.Deneme --persistence memory --provider openai --ui false`
çıkış kodu 0. `dotnet build -c Release`: `Build succeeded. 0 Warning(s) 0 Error(s)`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-003 — En dolu birleşim (`sqlserver`+`azure`+`ui:true`) sıfır uyarıyla derlenir

**Gerçek sonuç**
`dotnet new` çıkış kodu 0, `dotnet build -c Release`: `0 Warning(s) 0 Error(s)`.
Üretilen `.csproj` `<PackageReference Include="Tracon.SqlServer" .../>` ve
`<PackageReference Include="Tracon.Azure" .../>` satırlarını taşıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-004 — Üretilen `appsettings.json` yalnız boş placeholder taşır, hiçbir dosyada `secret` görünümlü değer yok

**Gerçek sonuç**
`secret` deseni taraması "temiz" yazdı. `appsettings.json`:
`Tracon.SqlServer.ConnectionString`, `Tracon.Providers.AzureOpenAI.Endpoint` ve
`.ApiKey` alanlarının hepsi `""`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-005 — Üretilen `Program.cs` hiçbir sağlayıcı için sabit bir model adı taşımaz

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, ürün kusuru DEĞİL):** Beklenen
sonuç metni Türkçe `MODEL_ADINI_BURAYA_YAZIN` placeholder'ı arıyordu; gerçek
placeholder İngilizce `WRITE_MODEL_NAME_HERE` — kod tabanının dil sınırı
kuralına göre (`AGENTS.md`: "pakete giren ve çalışma anında çalışan her şey
İngilizce'dir", K-228) şablon içeriği İngilizce'ye çevrilmiş, spec metni
bayat kalmış. Dört sağlayıcının (`openai`, `anthropic`, `google`, `azure`)
her birinde `WRITE_MODEL_NAME_HERE` tam olarak 1 kez geçiyor; bilinen model
öneki taraması (`gpt-|claude-|gemini-|o1-|o3-|text-embedding-`) dördünde de
"temiz". Davranışsal iddia (sabit model adı yok) doğrulandı, yalnız beklenen
metindeki placeholder dizesi düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-006 — `-n` ile yeniden adlandırma: `Tracon.Starter` dizesi hiçbir dosyada/dosya adında kalmaz

**Gerçek sonuç**
`Benim.Agent.csproj` var, `Tracon.Starter.csproj` yok. `Tracon.Starter` dize
taraması "temiz". `dotnet build -c Release`: `0 Warning(s) 0 Error(s)`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-007 — Varsayılan (`memory`) birleşim kurulumsuz `dotnet run` ile ayağa kalkar

**Gerçek sonuç**
Port 5081 boştu (ap-s1 Faz B işini bitirip uygulamasını durdurmuştu).
`dotnet new tracon-api -n Calisma.Deneme` (varsayılan seçenekler) + arka planda
`dotnet run -c Release`, 10 saniye bekleme. `GET /tracon/api/agents` → `200 OK`,
gövde tek bir agent: `"name":"support"`, `"displayName":"Support Assistant"`.
Hiçbir bağlantı hatası yok. İş bitince süreç durduruldu, port 5081 boşaltıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-008 — `postgres`/`openai` seçiminde ayrı bir paket referansı EKLENMEZ (zaten meta pakette)

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası):** Beklenen sonuç "iki `.csproj`
birebir aynıdır (diff boş)" diyor, ama case'in kendi girilecek veri bloğu iki
projeyi FARKLI adlarla üretiyor (`Meta.Kontrol` / `Meta.Kontrol2`) — bu yüzden
`RootNamespace` doğal olarak farklı, ayrıca her `dotnet new` çağrısı rastgele
bir `UserSecretsId` GUID'i üretir (persistence seçimiyle ilgisiz). Case'in asıl
iddiasını izole etmek için aynı adla iki proje üretildi
(`Meta.Kontrol` / `Meta.Kontrol` başka dizinde): `<ItemGroup>` blokları
`postgres` ile ve onsuz **birebir aynı** — yalnızca
`<PackageReference Include="Tracon" Version="..." />`. Davranışsal iddia
doğrulandı; "birebir aynıdır" ifadesi `ItemGroup`/`PackageReference` bloğuyla
sınırlandırılmalı, RootNamespace/UserSecretsId hariç.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-009 — `--skip-restore` restore adımını atlar

**Gerçek sonuç**
`--skip-restore` verilmeden üretilen projede `obj/` var (`project.assets.json`
dahil restore çıktıları). `--skip-restore true` ile üretilende `obj/` hiç
oluşmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-010 — `--TraconVersion` belirli bir sürüme sabitler

**Gerçek sonuç**
`grep "PackageReference Include=\"Tracon\""` → `Version="0.0.0-preview.0.789"`
(tam olarak `$SURUM`, `*-*` değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-011 — Tanınmayan bir `--persistence` değeri reddedilir

**Gerçek sonuç**
Çıkış kodu 127 (sıfırdan farklı). Hata mesajı: `'mysql' is not a valid value
for --persistence. The possible values are: memory, postgres, sqlite,
sqlserver`. Hedef dizin (`$TMP/g`) hiç oluşmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-012 — `-h` çıktısında üç bayrak görünür, `TraconVersion` gizlidir

**Gerçek sonuç**
`dotnet new tracon-api -h` çıktısı `--persistence`, `--provider` (`-pr`), `-ui`,
`--skip-restore` (`-sr`) dördünü de kısa açıklamalarıyla listeledi.
`--TraconVersion` çıktıda hiç görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-013 — Şablon paketi derlenmez; üretilen proje `Tracon.Templates`'e hiç referans vermez

**Gerçek sonuç**
`unzip -l ~/tracon-local-feed/Tracon.Templates.*.nupkg | grep -i "\.dll"` boş
döndü → "dll yok — beklenen". Üretilen projede `Tracon.Templates` dize
taraması "temiz".

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bölüm 2 — `FakeModelProvider` (Faz 39)

> Ortak konsol projesi `~/tracon-manuel/test-paketi` bu oturumda kuruldu
> (`dotnet new console` + `dotnet add package Tracon.Testing --version
> 0.0.0-preview.0.789`). `dotnet nuget list source` repo kökünden çalıştırılınca
> yalnız `nuget.org` gösteriyor (repo'nun kendi `NuGet.Config`'i `<clear/>` ile
> kaynakları sabitliyor — bilinçli, K-anlamında kusur değil); proje repo
> **dışında** olduğu için global config'teki `tracon-local` kaynağı görüyor ve
> paket oradan çözülüyor — doğrulandı (`~/.nuget/packages/tracon.testing/
> 0.0.0-preview.0.789` önbellekte).
>
> Spec'in `Girilecek veri` bloklarının bir kısmında (`MT-TEST-020`, `-022` .. `-026`,
> `-029`) `using Tracon;` eksik — `ModelBinding` tipi `Tracon` ad alanında,
> derlemek için eklenmesi gerekti. Küçük bir eksiklik, davranışsal iddiayı
> etkilemiyor; not olarak düşülüyor, ayrı `HATA` açılmadı.

### MT-TEST-020 — Varsayılan kurulum sabit `"fake response"` döner; katalog tek bir `fake-model` girdisi taşır

**Gerçek sonuç**
`Models.Count: 1`, `Model adi: fake-model`, `Yanit: fake response` — beklenenle
birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-021 — `EchoesUserMessage()` son kullanıcı mesajını `Echo: ` öneki ile yankılar

**Gerçek sonuç**
`Echo: ORD-7 nerede` ve `Echo: ikinci soru` — beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-022 — `RespondsWith(...)` yanıtları sırayla tüketir

**Gerçek sonuç**
İlk çağrı `ilk yanit`, ikinci çağrı `ikinci yanit`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-023 — Kuyruk tükendikten sonra `EchoesUserMessage()` fallback'i devreye girer

**Gerçek sonuç**
`1: ilk` (kuyruktan), `2: Echo: sonraki mesaj`, `3: Echo: ucuncu mesaj`
(fallback, güncel mesajı yankılıyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-024 — Fallback tanımlanmamışsa kuyruk tükendikten sonra sabit `"fake response"` tekrar tekrar döner

**Gerçek sonuç**
`1: tek yanit`, `2: fake response`, `3: fake response`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-025 — `CallsTool(...)` bir `FunctionCallContent` üretir; anonim tip argümanları yansımayla sözlüğe kopyalanır

**Gerçek sonuç**
`Tool: get_order_status`, `orderId: ORD-7`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-026 — `ForModel(...)` farklı modeller için bağımsız kuyruk tutar

**Gerçek sonuç**
`router: router yaniti`, `researcher: researcher yaniti`, `Models.Count: 2`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-027 — `EchoesLastToolResult(prefix)` gerçek tool-çağrı boru hattı üzerinden son tool sonucunu yankılar

**Gerçek sonuç**
`ModelProviderRegistry` üzerinden: `Sonuc: hazirlaniyor (ORD-7)`. Aynı
`FakeModelProvider`ı doğrudan (deftersiz) çağırınca yanıt yalnızca
`FunctionCallContent` taşıdı (tool çalıştırılmadı) — beklenenle birebir, ham
istemcide tool döngüsü kurulmuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-028 — `RespondsWith(text, inputTokens, outputTokens)` bildirilen kullanım gerçek boru hattında `RunRecord.Usage`'a yansır

**Gerçek sonuç**
`InputTokens: 42`, `OutputTokens: 17` — birebir bildirilen değerler.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-029 — `Requests` listesi gönderilen mesaj geçmişini ve `ChatOptions.Tools`'u kaydeder

**Gerçek sonuç**
`Requests.Count: 2`, `Ilk istek son mesaj: birinci`, `Ilk istek Tools.Count: 1`,
`Ikinci istek Options: null`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-030 — `Models` kataloğunda olmayan bir model adı agent kaydını ENGELLEMEZ

**Gerçek sonuç**
Hiçbir istisna fırlamadı, `run.ShouldHaveCompleted()` geçti,
"Basarili — katalogda olmayan model kaydi engellemedi." yazdırıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bölüm 3 — `TraconTestHost` (Faz 39)

### MT-TEST-040 — `StartAsync()` hiçbir yapılandırma olmadan ayağa kalkar, `/tracon/api/meta` `200` döner

**Gerçek sonuç**
`Durum: 200`. Gövde `"version":"0.0.0-preview.0.789"` ve `"prefix":"/tracon"`
içeriyor (ayrıca `authentication`/`storage`/`roles` alanları da var —
beklenenin bir üst kümesi, çelişki değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-041 — Özel `Prefix` yalnız o önekten yanıt verir, varsayılan önek artık yanıt vermez

**Gerçek sonuç**
`/panel: 200`, `/tracon: 404`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-042 — `DisposeAsync()` sonrası `Client` kullanılırsa `ObjectDisposedException` fırlatılır

**Gerçek sonuç**
`Beklenen: ObjectDisposedException firlatildi`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-043 — `RunAsync` var olmayan bir agent adıyla çağrılırsa `TraconAssertionException` fırlatılır

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, ürün kusuru DEĞİL):** Beklenen
sonuç Türkçe `'olmayan-agent' calistirilamadi` ile başlayan bir mesaj
bekliyordu; gerçek mesaj İngilizce (K-228 dil sınırı — çalışma anında çalışan
her şey İngilizce): `Failed to run 'olmayan-agent'. Expected a successful
status code, found '404': {"type":"...","title":"Agent not found","status":404,
"detail":"There is no agent named 'olmayan-agent'."}`. Davranışsal iddia
(`TraconAssertionException` fırlatılır, mesaj `404` durum kodunu içerir)
doğrulandı; yalnız beklenen metnin dili düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-044 — 🚨 K-218 tuzağının ampirik kanıtı: `AIFunctionArguments.Services`'ten çözülen bağımlılık `null` gelir

**Gerçek sonuç**
`Tool sonucu: "SERVICE COZULEMEDI"` — beklenenle birebir, K-218 tuzağı ampirik
olarak yeniden doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-045 — `ConfigureServices`, `AddTracon()` çağrısından ÖNCE çalışır

**Gerçek sonuç**
`Sira: ConfigureServices -> ConfigureTracon`, `Deger: ozel-deger`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bölüm 4 — `RunAssertions` (Faz 39)

> 🚨 **Doküman deseni (kural 1.1 istisnası, ürün kusuru DEĞİL, dosya genelinde
> tekrar eden tek desen):** Bu bölümün beş case'i (050–053, ayrıca 043) Türkçe
> düşen-yol mesajları bekliyor (`"Calistirmanin durumu ... beklenirdi ama ...
> bulundu."` vb.); gerçek `RunAssertions`/`TraconAssertionException` mesajları
> İngilizce (K-228 dil sınırı — çalışma anında çalışan her şey İngilizce).
> Davranışsal iddialar (doğru alan adları, doğru sayılar, doğru hata tipi)
> hepsinde doğrulandı; yalnız beklenen metnin dili bayat. Tek tek not
> düşülüyor, HATA açılmıyor — aynı kök nedenin (spec Türkçe yazılmış, kod
> İngilizce'ye geçmiş) beşinci tekrarı.

### MT-TEST-050 — `ShouldHaveCompleted()` geçer; başarısız bir çalıştırmada beklenen/bulunan durumu yazan mesajla düşer

**Gerçek sonuç**
`GECEN YOL: basarili.` yazdırıldı. `DUSEN YOL mesaji: Expected the run status
to be 'Failed' but found 'Completed'.` — beklenen/bulunan durum ikisi de
mesajda var, yalnız dil İngilizce (yukarıdaki not).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-051 — `ShouldHaveFailedWith(errorType)` kararlı bir hata tipini doğrular

**Gerçek sonuç**
`GECEN YOL: dogru hata tipi.` `DUSEN YOL mesaji: Expected the error type to be
'baska_bir_tip' but found 'content_blocked'.` — `content_blocked` (spec'in
kendi uyarısıyla tutarlı, `content_filtered` DEĞİL) doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-052 — `ShouldHaveCalledTool(name, times:)` sayı uyuşmazsa beklenen/bulunan sayıyı yazan mesajla düşer

**Gerçek sonuç**
`GECEN YOL: tam olarak 2 cagri.` `DUSEN YOL mesaji: Expected tool
'get_order_status' to be called 5 time(s) but it was called 2 time(s).`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-053 — `ShouldNotHaveCalledTool(name)` çağrılmış bir tool için düşer

**Gerçek sonuç**
`GECEN YOL: cancel_order hic cagrilmadi.` `DUSEN YOL mesaji: Expected tool
'get_order_status' to never be called but it was called 1 time(s).`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-054 — 🚨 `ShouldHaveOutputContaining` SSE (`/run`) yolunda `MessageDelta` parçalarını birleştirir

**Gerçek sonuç**
`GECEN: cikti iceriyor.` `MessageCompleted sayisi: 0`,
`MessageDelta sayisi: 1` — beklenenle birebir; bu, Faz 39'un kendi geçmiş
hatasının regresyon koruması, hâlâ doğru çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-055 — Zincirleme iddialar art arda çalışır

**Gerçek sonuç**
`Zincir basariyla tamamlandi -- hicbir asamada istisna atilmadi.` — README'nin
hızlı başlangıç örneği birebir çalıştırıldı, üç zincirlenmiş iddia da geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bölüm 5 — Paketleme / sözleşme (Faz 39, 95, 98, 99, 143)

### MT-TEST-060 — Paketlenmiş `.nuspec` hiçbir test çerçevesi bağımlılığı taşımaz

**Gerçek sonuç**
`<dependencies>` üç grup taşıyor (`net8.0`, `net9.0`, `net10.0`), üçü de yalnız
`Tracon.AspNetCore`, `Tracon.Core`, `Microsoft.AspNetCore.TestHost`. 🚨 **Spec
notu (ürün kusuru DEĞİL):** case'in kendi `grep` komutu tüm `.nuspec` dosyasını
tarıyor; paketin `<description>` alanı bilinçli olarak "Binds to no test
framework (xunit, NUnit, MSTest)" yazdığı için scope'suz tarama yanlış pozitif
üretiyor. Taramayı yalnız `<dependencies>` bloğuna scope edince "temiz" —
davranışsal iddia (gerçek bağımlılık yok) doğru.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-061 — Meta paket (`Tracon`) `Tracon.Testing`'e referans VERMEZ

**Gerçek sonuç**
`.nuspec` taraması "temiz". Kaynak `.csproj` taraması `0`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-062 — `Tracon.Testing` yalnız `net10.0` hedefler — `net8.0` projeden kullanılamaz

**Gerçek sonuç**
🚨🚨 **Bu case artık koda göre TERSİNE dönmüş (kural 1.1 istisnası, ürün kusuru
DEĞİL — bilinçli, çok yakın tarihli bir karar):** `docs/KARARLAR.md` satır 827,
**K-780 (2026-09-15)** — bu tur açılmadan yalnız 1 gün önce — K-270'i
("Tracon.Testing yalnız net10.0 hedefler") **kaldırdı**: "`Tracon.Testing`
çalışma paketleriyle AYNI matrisi hedefler (`net8.0;net9.0;net10.0`)". Kaynakta
doğrulandı: `Tracon.Testing.csproj`, `TraconAotCompatible=false` dışında TFM'i
daraltmıyor; `Microsoft.AspNetCore.TestHost` her TFM için ayrı sürümle
(`VersionOverride`) çözülüyor. Ampirik doğrulama: `dotnet new console
--framework net8.0` artık **SDK'nın kendi şablonunda** reddediliyor (SDK
10.0.100'ün console şablonu yalnız net9.0/net10.0 sunuyor — ayrı, ilgisiz bir
SDK kısıtı), bu yüzden `net8.0` hedefli bir `.csproj` elle yazıldı. `dotnet add
package Tracon.Testing` **başarıyla** eklendi ("Package 'Tracon.Testing' is
compatible with all the specified frameworks"), `dotnet build -c Release`:
`0 Warning(s) 0 Error(s)`. Beklenen sonuç tamamen tersine çevrildi ve
düzeltildi: paket net8.0'dan **kullanılabilir**, NU1202 **alınmaz**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-063 — AOT publish denemesi trim/AOT analiz uyarısı üretir (koşumda ölçülecek)

**Gerçek sonuç**
`PublishAot=true` + `FakeModelProvider().CallsTool(...)` çağıran bir `Program.cs`
ile `dotnet publish -c Release -r osx-arm64 --self-contained`: başarılı biter,
**sıfır** `IL[0-9]+`/`NETSDK1210` uyarısı üretir. Kök nedeni kaynakta
doğrulandı: `Tracon.Testing.csproj`'daki `<TraconAotCompatible>false</...>`,
`Directory.Build.targets:16-17`'de yalnız `TraconAotCompatible=true` iken
`IsAotCompatible=true` atıyor — `false` için `IsAotCompatible` hiç
atanmıyor (varsayılan `false`). Sonuç: paket AOT-uyumlu **olarak
işaretlenmediği için** derleyici onun genel yüzeyini
`RequiresDynamicCode`/`RequiresUnreferencedCode` ile doğrulamıyor ve tüketici
tarafında da bu API'lere yönelik bir uyarı üretmiyor — "uyarı üretir" beklentisi
mekanizmayı ters anlıyordu: paket AOT-güvenli OLMADIĞINI ilan ederek analizin
**dışında** kalıyor, "uyarı üreterek işaretlemiyor". Case'in kendi notuna göre
("sıfır uyarı çıkması ... not düşülmelidir") bu gözlem kayda geçirildi — kod
yorumu bayat değil, mekanizma farklı işliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-064 — Depo dışı tüketici: gerçek model çağırmadan uçtan uca bir agent testi

**Gerçek sonuç**
`~/tracon-manuel/depo-disi-tuketici` (repo dışı) içinde MT-TEST-055'in kodu
birebir çalıştırıldı. Ortamda hiçbir sağlayıcı anahtarı tanımlı değilken
(`env | grep -iE "openai|anthropic|..."` boş — yalnız alakasız
`CLAUDE_CODE_EXECPATH` eşleşti) `Zincir basariyla tamamlandi -- hicbir asamada
istisna atilmadi.` yazdırıldı — `FakeModelProvider` hiç ağa çıkmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bölüm 5 (devamı) — Paketleme/sözleşme

> **Devir notu (oturum 15, ap-s2 devam — dosya 24 ŞİMDİ TAM BİTTİ):**
> `MT-TEST-070..094` koştu, 24/24 çalıştırılabilir case Geçti, 0 Kaldı.
> İki case (`077`, `083`) 👤 fiziksel/tarayıcı eylemi — §"Fiziksel eylem
> listesi"ne düştü. **Dosya 24: 66 case'in 64'ü koşuldu (hepsi Geçti), 2'si
> fiziksel eylem bekliyor.** Sıradaki aile: `35-TYPESCRIPT-ISTEMCISI.md`
> (11 case). Paketleme durumu: ilk `dotnet pack Tracon.src.slnf` bu oturumda
> yanlışlıkla `kill -9` ile kesildi (aslında donmamış, yalnız tüm çözümü
> paketlemek ~4 dakika sürüyor) — orphan süreç kendi başına tamamlandı,
> `artifacts/package/release/` şimdi sürüm `0.0.0-preview.0.839` (20 paket)
> taşıyor. Eski `0.819` sürümü (önceki bir oturumdan kalıntı) silindi —
> `kapi.py yayin --kuru` bunu "stale packages" olarak doğru şekilde
> yakaladı (ürün kusuru değil, tur aparatı kalıntısı).

### MT-TEST-070 — Meta paket tüketicisi gerçek bir `run` koşturur; üretilmiş tool çalışır (Faz 95, madde 10)

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, tooling — ürün kusuru DEĞİL):**
Case'in kendi komutu (`--sinif ConsumerRunTests`, joker karaktersiz) **hiçbir
test eşleştirmedi** (`Zero tests ran`) — `docs/hafiza/test-kosum-tuzaklari.md`
zaten belgeliyor: `--filter-class` joker karakter gerektirir (`"*Ad*"`), çıplak
sınıf adı FQN'e (`Tracon.Package.Tests.ConsumerRunTests`) eşleşmez. Düzeltilmiş
komut: `python3 scripts/kapi.py test --proje Tracon.Package.Tests --sinif
'*ConsumerRunTests*'`. Bununla 1/1 Geçti (15.7s — ilk koşumda `dotnet pack`
soğuk önbellekle ~4 dakika sürdüğü için oturumda bu süreç yanlışlıkla donmuş
sanılıp `kill -9` ile kesildi; orphan süreç kendi başına tamamlandı, ürün
kusuru değil, bkz. yukarıdaki devir notu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-071 — Analyzer paketleme hedefi devre dışı bırakılırsa kapı KIRILIR (Faz 95, sahte kusur enjeksiyonu)

**Gerçek sonuç**
Üç adım da birebir beklenen gibi: (1) `Condition="'$(TargetFramework)' ==
'net10.0'"` → `'net99.0'` enjeksiyonu sonrası `ConsumerRunTests` **kırıldı**:
`error CS1061: 'ITraconBuilder' does not contain a definition for
'AddGeneratedTools'`. (2) Geri alma + `touch` sonrası `git diff` temiz. (3)
Test tekrar **Geçti** (1/1, 41.7s).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-072 — Grafiğe yeni bir geçişli paket girerse `TransitiveDependencyTests` KIRILIR (Faz 95, madde 22)

**Gerçek sonuç**
Üç adım da birebir beklenen gibi: (1) `Humanizer.Core` enjeksiyonu sonrası
yalnız `Tracon.Google` şekli kırıldı (`Tracon` ve `Tracon.Core` yeşil kaldı, 2
succeeded/1 failed), hata mesajı `Added: [Humanizer.Core]`. (2) Geri alma +
`touch` sonrası `git diff` temiz. (3) Üç şekil de tekrar **Geçti** (3/3, 2 dk).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-073 — `Tracon.Testing.Contracts.Xunit` yalnız `Tracon.Abstractions`'ı geçişli olarak indirir

**Gerçek sonuç**
`dotnet list package --include-transitive` çıktısında yalnız iki `Tracon.*`
satırı: `Tracon.Testing.Contracts.Xunit 0.0.0-preview.0.839` ve
`Tracon.Abstractions 0.0.0-preview.0.839`. `Tracon.Core` hiç yok. (Sürüm
`0.839`, spec'in bayat kaydettiği `0.360` değil — beklenen; bu turun kendi
`dotnet pack` çıktısı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-074 — 15 metodu `NotSupportedException` fırlatan bir `IRunStore`, `RunStoreContract`'ı türetir; derlenir, suite koşar ve KIRMIZI olur

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, ürün kusuru DEĞİL — suite
zamanla büyümüş):** MT-TEST-073'ün probe projesine `xunit.v3` +
`UseMicrosoftTestingPlatformRunner`/`TestingPlatformDotnetTestSupport`
eklenmesi gerekti (case metni bunu atlıyor, `tests/Directory.Build.props`'un
MTP kurulumunu tekrarlamak gerekti — repo kuralı, kusur değil).
`IRunStore`'un tüm 15 metodunu `NotSupportedException` ile uygulayan bir sınıf
`dotnet build -c Release`: **`0 Error(s)`**. Suite koştu: **99 case** (spec'in
kaydettiği `88` değil — `RunStoreContract` zamanla büyümüş, aynı büyüme deseni
MT-TEST-075/084'te de görüldü), **97 Kırmızı** (hepsi
`System.NotSupportedException` ile), **0 Yeşil**, **2 Atlandı**
(`Experiment_results_average_a_run_level_score_written_with_an_empty_message_id`
ve `..._leave_a_message_level_score_out_of_the_average` — ikisi de opsiyonel
`IRunScoreStore`'a bağlı, `CreateScoreStoreAsync()` override edilmediği için
atlanıyor, `IRunStore`'un 15 zorunlu metoduna dahil değil, kusur değil).
Davranışsal iddia ("derleme hatası değil çalışma zamanı hatası") tam
doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-075 — Örnek store (`Tracon.Samples.FileRunStore`) yalnız NuGet paketleriyle restore edilir ve sözleşme suite'i yeşildir

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, kusur değil):** `dotnet build
samples/Tracon.Samples.FileRunStore.Tests -c Release`: `0 Warning(s), 0
Error(s)`. Suite: **99/99 Geçti** (spec'in kaydettiği `88` değil — aynı büyüme
MT-TEST-074'te açıklandı, aynı `RunStoreContract` tabanı). (Bu case MT-TEST-086
sırasında da aynı komutla koşuldu, sonuç aynı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-076 — Yayın provası **20** paket görür; `Tracon.Testing.Contracts.Xunit` kimlik kümesindedir

**Gerçek sonuç**
🚨 **Paylaşılan durum notu (ürün kusuru DEĞİL):** İlk koşum, önceki bir
oturumdan kalma izlenmeyen bir dosya (`bench/baseline 2.json`) yüzünden
"Çalışma ağacı temiz değil" ile reddetti — dosya `/tmp`'e geçici taşınıp
koşum sonrası **aynı yere geri kondu**. İkinci koşum da `artifacts/package/
release/` içindeki **eski** `0.819` sürüm paketlerini (bu turun daha önceki
bir `dotnet pack`'inden kalıntı, gitignore'lu bir build çıktısı) "stale
packages" diye **doğru şekilde reddetti** — bu paketler silindi (yalnız
`artifacts/`, kod değil), üçüncü koşum: **✅ 20 paket, sürüm
'0.0.0-preview.0.839'**, `Tracon.Testing.Contracts.Xunit` listede, aynı sürüm
hattında. `icon.png` paketin içinde doğrulandı (`unzip -l`). npm paketi de
`--dry-run` ile başarıyla yayınlandı, 6 örnek + Native AOT smoke testi geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-078 — Örnek sağlayıcı yalnız NuGet paketleriyle derlenir

**Gerçek sonuç**
`grep -c ProjectReference` → `0`. `dotnet build -c Release`: `0 Warning(s), 0
Error(s)`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-079 — `ModelProviderContract` örnek sağlayıcıya karşı yeşil geçer, hiçbir senaryo atlanmaz

**Gerçek sonuç**
`dotnet test -c Release`: **38/38 Geçti, skipped: 0**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-080 — Ham istemci kuralı ihlal edilince suite KIRMIZI olur ve nedenini söyler

**Gerçek sonuç**
Üç adım da birebir beklenen gibi. (1) `CreateChatClient(ModelBinding)`'in
dönüş satırı `.AsBuilder().UseFunctionInvocation().Build()` ile sarmalandı —
`Microsoft.Extensions.AI` paket referansı **olmadan** `error CS1061:
'ContosoChatClient' does not contain a definition for 'AsBuilder'` ile
derlenmedi (tam beklenen: ihlali yapmak için bilerek referans eklemek
gerekiyor). (2) Referans + `using` eklenince derlendi (`0 Error(s)`); test
suite koşulunca **her üç** türetilmiş sınıfta (`ContosoModelProviderCredential
Tests`, `..SettingsTests`, `..ContractTests`) `Create_chat_client_returns_a_
raw_client_that_builds_no_tool_call_loop` **düştü** (3 failed/35 passed),
mesaj: `"IModelProvider.CreateChatClient must return a RAW client. ... Building
the loop here nests two FunctionInvokingChatClient instances and hides the
tool-result turn from the content guard."` — beklenen içeriğin (ortak boru
hattı + content guard'dan gizleme) birebir aynısı. (3) `.cs` ve `.csproj` geri
alındı + `touch`, `git diff` temiz, suite tekrar **38/38 Geçti**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-081 — Yeni sözleşme ailesi depolama kapsam kapılarını kırmaz

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (küçük, kusur değil):** Metin "PostgreSQL ve SQL
Server aynı kod yolunu koşar" diyor ama komut `Tracon.Sqlite.
IntegrationTests`'i çağırıyor — tutarsızlık, muhtemelen kopyala-yapıştır
kalıntısı. Çalıştırılan iki komut da geçti:
`Tracon.Core.UnitTests --filter-method "*ContractCoverage*"` → 5/5,
`Tracon.Sqlite.IntegrationTests --filter-method "*ContractCoverage*"` → 1/1.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-082 — Boru hattı sahipliği regresyonu davranış üzerinden ölçer, tip adı saymaz

**Gerçek sonuç**
`--filter-method "*PipelineOwnership*"` → **3/3 Geçti**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-084 — Worker kapanışı uçuştaki job slotunu bırakmadan dönmez

**Gerçek sonuç**
🚨 **Doküman düzeltmesi (kural 1.1 istisnası, kusur değil — sınıf büyümüş):**
Beklenen sonuç "Bir test geçer" diyor; kaynakta (`JobWorkerBackgroundService
Tests.cs`) şu an **4** `[Fact]` var
(`StopAsync_waits_for_a_leased_job_to_release_its_concurrency_slot`,
`A_full_lane_does_not_block_the_default_lanes_job`,
`A_worker_scoped_to_one_lane_never_leases_another_lane`,
`A_throwing_handlers_own_message_never_reaches_jobs_error_message`).
`kapi.py test --sinif '*JobWorkerBackgroundServiceTests*'` → **4/4 Geçti**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-085 — Uygulanmış PostgreSQL migration Git tabanındaki baytla aynıdır

**Gerçek sonuç**
`python3 scripts/kapi.py tarama` → `Tarama: ✅ temiz (6 işaretli sentetik
credential atlandı)` (bu komutun docstring'i migration-integrity taramasını
kapsadığını doğruluyor — `0032_tenant_provider_bindings.sql`/`0037_run_
continuation.sql` dahil). `python3 -m unittest scripts.kapi_test -v` →
**64/64 OK** (çıktıdaki `❌`/`⚠️` satırları test fixture'larının KENDİ
simüle ettiği hata senaryoları, gerçek arıza değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-086 — Beş sözleşme ailesi (Storage/Providers/Judges/AgentSources/Tools) gerçek çıkarımı zorunlu kılar (Faz 103)

**Gerçek sonuç**
`dotnet list src/Tracon.Testing.Contracts.Xunit package --include-transitive`
→ hiçbir `Tracon.*` paketi listelenmiyor (proje referansı, paket değil) —
"Tracon.Core görünmez" doğrulandı. `Tracon.Core.UnitTests` tam suite: **2805/
2805 Geçti, 0 Atlandı**. Beş `samples/Tracon.Samples.*.Tests` projesi
(FileRunStore, CustomModelProvider, CustomRunJudge, CustomAgentSource,
CustomTool — `CustomJobHandler` hariç, o beş sözleşme ailesine ait değil)
derlendi ve koşuldu: 99+38+11+15+18 = **181/181 Geçti, 0 Atlandı**. Judges
ailesi iki tarafta da doğrulandı: `Tracon.Core.UnitTests.Evaluation.
ModelRunJudgeTests` (built-in) ve `Tracon.Samples.CustomRunJudge.Tests.
ResponseQualityJudgeContractTests` (sample).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-087 — `[Description]` taşıyan bir tool parametresi üretilen şemada `description` alanı taşır (Faz 125)

**Gerçek sonuç**
🚨 **Doküman notu:** Case portu `5080` (varsayılan) veriyor; ap-s2 şeridinin
kendi portu (`5082`) kullanıldı, uygulama zaten ayaktaydı. `GET /tracon/api/
tools`: `get_order_status.jsonSchema` → `"orderId":{"description":"The order
number.","type":"string"}`. `cancel_order`, `estimate_shipping_cost`,
`get_slow_report`, `list_recent_orders` — hepsinin kendi parametre(ler)inde
`description` var.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-088 — Açıklaması olmayan bir tool parametresi TRC0009 uyarısı üretir; derleme başarılı biter (Faz 125)

**Gerçek sonuç**
`[Description]` silindikten sonra `dotnet build -c Release`: birebir beklenen
`warning TRC0009: Parameter 'orderId' of tool 'get_order_status' has no
description...`, `0 Error(s)`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-089 — Nesne parametreli bir tool metodu TRC0003 hatası üretir; mesaj iç içe nesnenin ifade edilemediğini ve kaçış yolunu adıyla söyler (Faz 125)

**Gerçek sonuç**
🚨🚨 **Bu case'in önermesi artık koda göre YANLIŞ (kural 1.1 istisnası, ürün
kusuru DEĞİL — generator'a sonradan eklenmiş bir yetenek):**
`ToolDiagnostics.cs`'teki `TRC0003` mesajının KENDİSİ artık şunu söylüyor:
"...and a supported object - a public record or class with a single public
constructor, up to 3 nested object levels deep (see TRC0011, TRC0012)."
Yani nested object parametreleri **artık desteklidir** (3 seviyeye kadar).
Case'in kendi repro'su (`OrderFilter(string Status, int MinAmount)` — tam da
"public record, tek public constructor") bu yüzden TRC0003 **VERMEDİ**;
bunun yerine `error TRC0011: ... which is not declared with
[JsonSerializable(typeof(...))] on the JsonSerializerContext...` verdi —
build yine `Build FAILED` ile bitti (`0` çıkış kodu değil), ama farklı bir
kod/mesajla. Gerçek `TRC0003`'ü ampirik olarak doğrulamak için genuine
desteklenmeyen bir tip (`Dictionary<string,string>`) denendi ve birebir
case'in ORİJİNAL mesaj biçimiyle eşleşti (`"is not supported by the
generator. Supported types: ..."`) — ama "nested object'i hiç ifade edemez"
kısmı artık mesajda YOK, tam tersi anlatılıyor. Davranışsal iddia ("nesne
parametresi derleme hatası verir") kısmen doğru (TRC0011 de bir hata), ama
case'in kod örneği ve beklenen hata kodu/mesajı artık yanlış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-090 — `ToolArgumentValidationContract` doğru bir validator'da yeşil geçer (Faz 143)

**Gerçek sonuç**
`--filter-method "*ToolArgumentValidationContractTests*"` → **7/7 Geçti**
(spec'in 2026-09-04 kaydıyla birebir aynı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-091 — 🚨 Aynı suite, her şeyi kabul eden bir validator'da KIRMIZI olur — test tiyatrosu yok (Faz 143)

**Gerçek sonuç**
`--filter-method "*ToolContractSelfProofTests*"` → **11/11 Geçti** (spec'in
2026-09-04 kaydıyla birebir aynı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-092 — Aynı tohum aynı argümanları üretir; tohumlu üreteç kırılgan değildir (Faz 143)

**Gerçek sonuç**
İki ardışık koşum, ikisi de **13/13 Geçti** (spec'in 2026-09-04 kaydıyla
birebir aynı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-093 — Nested object parametreli bir tool, sözleşmeyi açıkça atlar — sessizce değil (Faz 143)

**Gerçek sonuç**
`--filter-method "*Required_nested_object*"` → **1/1 Geçti** (spec'in
2026-09-04 kaydıyla birebir aynı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-TEST-094 — `Tracon.Testing.Contracts.Xunit` üretime sızmaz; iki yeni sözleşme yalnız `Tracon.Abstractions`'a bağımlıdır (Faz 143)

**Gerçek sonuç**
`--filter-method "*DependencyDirectionTests*"` → **5/5 Geçti** (spec'in
2026-09-04 kaydıyla birebir aynı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Fiziksel eylem listesi

| Case | Neden | Kullanıcıdan istenen |
|---|---|---|
| MT-TEST-077 | Yayınlanan siteyi tarayıcıda gözle inceleme (👤) | `https://tracon.dev/guides/write-your-own-store/` sayfasını aç; `Tracon.Testing.Contracts.Xunit` kurulumunu, `RunStoreContract` türetme örneğini ve altı davranış eksenini (idempotency, üç kiracı modu, thread safety, null/bulunamadı, olay sırası, yinelenen `Sequence`) anlattığını, kod örneğinin derlenebilir gerçek imzalar kullandığını doğrula. |
| MT-TEST-083 | Yayınlanan siteyi tarayıcıda gözle inceleme (👤) | `https://tracon.dev/api/tracon.imodelprovider/` ve `https://tracon.dev/guides/model-providers/` sayfalarını aç; sağlayıcı sözleşmesinin sekiz maddesinin (singleton ömrü, eşzamanlılık/thread safety, ham istemci, registry'ye ait ortak halkalar, dispose sahipliği, credential fabrikası yan etkisiz, `OrdinalIgnoreCase` ad karşılaştırması, kataloğun izin listesi olmaması) ve yetenek bayrağı notunun (yalnız `SupportsStructuredOutput` zorlanır) açıkça yazılı olduğunu doğrula. |

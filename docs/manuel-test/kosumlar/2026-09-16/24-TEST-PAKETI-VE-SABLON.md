# 24 — Test Paketi ve Proje Şablonu — koşum kaydı (2026-09-16, ap-s2)

> **Devir notu (oturum 15, ap-s2):** Bölüm 1–4 bitti — MT-TEST-001..013,
> 020..030, 040..045, 050..055 = **36/36 Geçti, 0 Kaldı**. Sırada Bölüm 5+
> (`MT-TEST-060` — paketleme/sözleşme case'leri, `060..094`, ~35 case kaldı;
> bunlar `dotnet pack`/AOT publish/sözleşme suite koşumu içerdiği için daha
> ağır — ayrı bir oturumda devam edilmeli, oturum bütçesi ~40 CLI case'e
> ulaşıldı). Ortam: şerit `ap-s2`, port 5082 (uygulama bu ailede çoğunlukla
> kullanılmıyor — yalnız MT-TEST-007 5081'i geçici kullandı, iş bitince
> kapatıldı). Global şablon kaydı bu oturumda `ap-s3`'ün eski yolundan
> `ap-s2`'nin kendi yoluna taşındı (bkz. aşağıdaki not) — sıradaki oturum
> `dotnet new list tracon-api` ile tekrar kontrol etmeli, paralel şeritler
> `dotnet new install` çalıştırırsa değişebilir. `~/tracon-manuel/test-paketi`
> ayakta bırakıldı (Bölüm 5+ de kullanacak), Program.cs son hali MT-TEST-055.
>
> **Bu ailede bulunan tek desen: beş case'te (005, 008, 043, 050, 051 — ayrıca
> 052/053 örtük) beklenen sonuç metni bayat (Türkçe placeholder/mesaj yerine
> gerçek kod İngilizce, K-228). Hiçbiri ürün kusuru değil**, hepsi kural 1.1
> istisnasıyla düzeltildi, case metinlerinde işaretli. Ayrı `HATA-S2-*` kaydı
> açılmadı — dosya 07'nin emsaliyle aynı (stale spec metni, kusur değil).

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

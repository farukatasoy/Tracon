# Değerlendirme Raporu — Faz 0-52 Geriye Dönük İnceleme

> ## ✅ KAPATILDI (2026-08-08) — bu rapor işlendi
>
> Aşağıdaki metin **denetim anındaki** durumu anlatır ve tarihsel değeri için
> korunmuştur. Yapılanlar:
>
> | Rapor maddesi | Sonuç |
> |---|---|
> | §2.1 F-76 OpenAPI 500 | **Düzeltildi.** 🚨 Kapsam iddiası **yanlış çıktı**: hata yalnız `ProjectReference` ile derleyeni etkiliyor, NuGet paketi tüketicisini **etkilemiyor** (uçtan uca ölçüldü). Karar **K-352** |
> | §2.2 `.UseMcp()` bağlama | **Düzeltildi** — `UseMcp(IConfiguration, …)` aşırı yüklemesi. Karar **K-353** |
> | §2.3 `BackgroundService` yarışı | **Düzeltildi** — `SchemaReadyGate`, sıra bağımsız. Karar **K-354** |
> | §2.4 Alt yazmalarda kiracı | **Düzeltildi** — beklenen kiracı alanları + `[JsonIgnore]`. Karar **K-355** |
> | §3 Faz 7 | **Beklemede** (kullanıcı kararı). Eşik kararı **yazılmadı**; yalnız durum kaydedildi |
> | §5 F-56 / F-36 / F-69 / F-74 | **Plana dönüştü:** [Faz 53](fazlar/53-KIRACI-API-ANAHTARLARI.md) · [Faz 54](fazlar/54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md) · [Faz 55](fazlar/55-ASENKRON-ONAY-KUTUSU.md) · [Faz 56](fazlar/56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md). **Kod yazılmadı** |
> | §1 README test sayısı | **Düzeltildi** — "310 test" → ölçülen **3355 test, 16 proje** |
> | §4 + §6 kapsam-dışı işler | **Numaralandırıldı:** F-90…F-102 ([aday listesi](../ADAYLAR.md)) |
>
> 🚨 **Bu raporun tek yanlış iddiası §2.1'in kapsamıdır** ("her çok-sağlayıcılı
> tüketici kırık"). Ölçüm bunu yanlışladı; ayrıntı K-352'dedir.
>
> Güncel durum için bu rapor değil [`README.md`](../../README.md),
> [`UCUNCU-FAZ-YOL-HARITASI.md`](UCUNCU-FAZ-YOL-HARITASI.md) ve
> [`ADAYLAR.md`](../ADAYLAR.md) okunur.

---

> **Tek seferlik denetim belgesi.** Doküman bütçesi sistemine (`scripts/dokuman-bakim.py`)
> dahil değildir ve sıcak okuma yoluna (`AGENTS.md`, `MEMORY.md`) eklenmemiştir.
> İçerik `docs/ADAYLAR.md`, `docs/arsiv/UCUNCU-FAZ-YOL-HARITASI.md` ve
> `docs/KARARLAR-INDEKS.md`'nin **2026-08-08** tarihli durumundan derlenmiştir;
> bu üç dosya birincil kaynaktır ve bayatladığında bu rapor değil onlar geçerlidir.
>
> **Yöntem:** Kod tabanında `TODO`/`FIXME` taraması (sıfır sonuç — proje
> disiplini gereği açık iş kalemleri koda değil dokümana yazılıyor), `git log`/
> `git status`, ve mevcut karar defterinin/aday listesinin tam okunması.
> Aşağıdaki hiçbir madde tahmin değildir; her biri bir dosya+satır veya karar
> numarasıyla izlenebilir.

---

## 1. Genel Durum

- **52 fazın tamamı tamamlandı** (Faz 7 hariç). Son commit `d168eea "faz 52"`,
  çalışma ağacı temiz.
- Kod tabanında **sıfır** `TODO`/`FIXME`/`NotImplementedException` (proje
  kuralı: "Start it = Finish it"). Açık iş kalemlerinin tamamı `docs/ADAYLAR.md`
  içinde yaşıyor — bu rapor onu okunabilir hâle getirir, yerine geçmez.
- **20 aday kalem** (F-numaralı) hâlâ plana dönüşmedi; bunlara ek olarak
  planlama ve uygulama sırasında bulunan **9 numaralandırılmamış** kapsam-dışı
  iş var (bkz. §4).
- **Doküman kayması bulundu:** [`README.md:311`](../../README.md#L311) `dotnet test`
  komutunu "# 310 test" notuyla belgeliyor. Depoda bugün 16 test projesi var
  (`AgentPrism.Generators.UnitTests` dahil) ve toplam `[Fact]`/`[Theory]`
  işaretli metot sayısı **~3.900**'ün üzerinde — sayı en az bir büyüklük
  mertebesi bayatlamış. Küçük ama gerçek bir düzeltme.

---

## 2. Bilinen Kusurlar (Aday Değil, Ölçülmüş Hata)

Aşağıdakiler "yeni özellik" değil, **bugün var olan ve kanıtlanmış** davranış
kusurlarıdır. Faz kapsamı dışında bırakıldıkları için hâlâ açıktır.

### 2.1 🔴 OpenAPI + çoklu SQL sağlayıcı → `/openapi/v1.json` 500 verir

**Kanıt (F-76, ölçüldü 2026-08-06):** `AgentPrism.Sql.Shared` üç ayrı derlemeye
(`AgentPrism.SqlServer`, `AgentPrism.Sqlite`, `AgentPrism.PostgreSql`)
`LinkBase` ile bağlanıyor (K-185). İki veya daha fazla SQL sağlayıcısı aynı
projede referanslandığında (`samples/AgentPrism.Api` dahil — SqlServer +
Sqlite birlikte), `Microsoft.AspNetCore.OpenApi`'nin XML doküman toplayıcısı
aynı tam nitelikli tip adını (`T:AgentPrism.MigrationRunner` vb.) iki kez
görüyor ve `ArgumentException` fırlatıyor. Sonuç: `AddOpenApi()` +
`MapOpenApi()` kullanan **her** çok-sağlayıcılı tüketici için `/openapi/v1.json`
kırık.
**Neden görünmedi:** `AgentPrism.AspNetCore.FunctionalTests` hiçbir SQL
sağlayıcısına referans vermiyor; hata yalnız gerçek çok-sağlayıcı kurulumunda
çıkıyor.
**Üç çözüm yolu belirlenmiş, hiçbiri seçilmemiş:** (a) çakışan tiplerin XML
doküman üretimini tek derlemede bırakacak yapılandırma, (b) `AgentPrism.Sql.Shared`'ı
gerçek bir paket yapmak (K-185'i yeniden açar), (c) yukarı akış
(`dotnet/aspnetcore`) hatası olarak bildirmek.
**Öneri:** Faz 40'ın "belge = gerçek uygulama" iddiasını doğrulayan bu hata,
Faz 7 (yayın) öncesi kapatılmalı — aksi hâlde yayınlanan ilk sürüm, dokümante
edilen bir özelliği (OpenAPI) yaygın bir kurulumda (2+ SQL sağlayıcı) kırık
bırakır.

### 2.2 🟡 `.UseMcp()` yapılandırma bağlama sessizce çalışmıyor

**Kanıt (Faz 42 sırasında ölçüldü):** `AgentPrismMcpOptions` yalnız kod
tarafında `configure` delegesiyle set edilebiliyor; `IConfiguration.Bind`
hiç çağrılmıyor. `AgentPrism:Mcp:RefreshInterval` gibi bir ortam değişkeni
**hiçbir hata vermeden hiçbir şey yapmıyor**. Sessiz konfigürasyon kaybı,
container/K8s tabanlı dağıtımlarda (env-var-first) tipik bir tuzaktır.
**Öneri:** `IConfiguration` bağlama eklenmeli veya en azından açılışta bir
tanı (`ConfigurationDiagnostic`, Faz 33'ün mekanizması hazır) üretilmeli.

### 2.3 🟡 `BackgroundService` başlatma sırası migration ile yarışabiliyor

**Kanıt (Faz 42 sırasında ölçüldü):** `MigrationHostedService.StartAsync`
migration'ları tam bekliyor, ama `BackgroundService` taban sınıfı
`ExecuteAsync`'i beklemeden dönüyor. Kayıt sırası `.UseMcp()` → `.UseSqlite()`
ise `McpDiscoveryService`'in ilk SQL denemesi migration bitmeden çalışabiliyor
("no such table"). Kendiliğinden düzeliyor (bir sonraki turda) ama gözlenebilir
bir hata/uyarı üretiyor — özellikle ilk açılış sağlık kontrollerinde yanlış
alarm.
**Öneri:** Hosted service sırası garanti edilmeli veya ilk tur gecikmeli
başlatılmalı; ikisi de ayrı bir karar gerektiriyor.

### 2.4 🟡 Alt yazma yolları kiracı süzgeci taşımıyor

**Kanıt (Faz 41 devir notu, K-280):** `IRunStore.AppendEventAsync`,
`CompleteRunAsync`, `UpdateRunCostAsync`, `RecordToolInvocationAsync`
kiracı filtresi almıyor. Ambient kiracı ile süzme denendi ve **geri alındı**
(`RunStartInfo.TenantId` ambient kiracıyı bilerek ezdiği için meşru yazmaları
düşürüyordu). Bugün ulaşılabilir bir sızıntı **yok** (UUID v7 kimlikler,
okuma tarafı süzülü) ama düzeltme public API'yi büyütecek (`RunEvent`/
`RunCompletion`/`ToolInvocationRecord`'a birer alan) ve bu yüzden kasıtlı
olarak ayrı bırakıldı.
**Öneri:** Faz 7'den (yayın) önce karara bağlanırsa bedava; sonra
`PublicAPI.Shipped.txt` disiplinine girer.

---

## 3. Faz 7 — Yayın Hazırlığı Hâlâ Bekliyor 🔴

`docs/arsiv/fazlar/07-SAGLAMLASTIRMA-VE-YAYIN.md`'de tanımlanan sekiz alt başlığın **hiçbiri**
uygulanmadı (K-068, kullanıcı kararı — zamanlama kasıtlı ertelendi, unutulmuş
değil):

| Alt başlık | Durum |
|---|---|
| 7.1 Public API dondurma (`EnablePublicApiTracking`) | `false` — hiç açılmadı |
| 7.2 Paket doğrulama | Yapılmadı |
| 7.3 Paket ikonu | Yapılmadı |
| 7.4 Depo adresi düzeltmesi | Yapılmadı |
| 7.5 Test kapsamı raporu | Yapılmadı |
| 7.6 Performans ölçümü / yük testi | Yapılmadı — `BenchmarkDotNet` projesi depoda yok (bkz. F-67) |
| 7.7 Dokümantasyon (dış tüketici gözüyle) | Yapılmadı |
| 7.8 Yayın zinciri (CI/CD → NuGet) | Yapılmadı |

**Neden bu raporun en önemli maddesi:** `EnablePublicApiTracking = false`
olduğu için 26 fazın (31-52) her biri public yüzeyi serbestçe büyüttü.
Üç kalem **var olan bir arayüze metot ekledi** — bu, yayından **sonra**
yapılsaydı kırıcı bir sürüm kararı olurdu, öncesinde bedava oldu:

- Faz 36 → `IRetentionStore`
- Faz 45 → `IEvalStore`
- Faz 52 → `IAgentPrismBuilder` (`AddGeneratedTools()`)

Bu üç örnek, **Faz 7'nin ne kadar süre daha ertelenebileceğinin** doğal bir
üst sınırını gösteriyor: her yeni faz, dondurma yapılmadığı sürece "bedava"
kırıcı değişiklik yapma özgürlüğünü kullanmaya devam edecek. Kalan 20 aday
kalemden en az yedisi (F-30/32/40/44/50/56/59/61/68/75, bkz. aday listesinin
"Faz 7 Hatırlatması" bölümü) benzer şekilde public yüzeyi büyütüyor. **Öneri:**
Faz 7'yi tetikleyecek somut bir eşik belirlenmeli (örn. "kalan öncelikli
kümedeki N kalem bitince" veya bir takvim tarihi) — aksi hâlde erteleme
süresiz uzar ve dondurma maliyeti her turda katlanarak artar.

---

## 4. Kapsam Dışı Bırakılan, Henüz Numaralandırılmamış İşler

Planlama ve uygulama sırasında **9 iş** bilinçli olarak aday listesine
yazılmadı (numara almadı) ama unutulmadı; devir notlarında yaşıyor. Bunları
resmi bir F-numarasıyla listeye eklemek, sonraki bir planlama turunun bunları
yeniden keşfetmesini önler:

| İş | Kaynak faz | Özet |
|---|---|---|
| PostgreSQL RLS derinlemesine savunma | Faz 41 | Sözleşme testi seçildi, RLS iptal edilmedi — SQLite'ta karşılığı yok |
| MCP OAuth token'ının örnekler arası paylaşımı | Faz 42 | K-059 ile çatışıyor (`secret` veritabanına yazılmaz); ayrı karar gerekir |
| Paylaşılan (dağıtık) hız sınırı | Faz 42 | K-158 bilerek bellekte tuttu |
| Akışlı yanıtta idempotency | Faz 43 | Faz 46'nın `202 Accepted` sözleşmesiyle kapandı sayılabilir |
| TypeScript istemci paketi + npm yayını | Faz 40 | İkinci dağıtım kanalı; ayrı yayın hattı ister |
| Çok turlu eval vakası terfisi | Faz 45 | `EvalCase` sözleşmesini değiştirir; Faz 7'den önce karara bağlanması ucuz |
| Tur bazlı kontrol noktası (F-68 Okuma B) | Faz 46 | MAF agent düzeyinde kanca **yok** (ölçüldü); AgentPrism yazarsa K3 zorlanır |
| Azure AI Content Safety adaptörü | Faz 48 | Ağırlık sorun değil (4 paket); erteleme gerekçesi doğrulanamazlık (K-212 emsali) |
| `IVectorSearchStore` SQL Server/SQLite uygulaması | Faz 51 | Yerel `VECTOR` tipi ve `sqlite-vec` ölçülmedi |

Ayrıca **Faz 48'in kodu yazılırken** üç yeni kalem doğdu (F-87, F-88, F-89 —
zaten numaralı, aday listesinde duruyor):

- **F-87** 🔴 Kayıtlardaki hassas verinin redaksiyonu — guard yalnız model
  sınırında çalışıyor; `run_events`/`run_inputs` ham içeriği kalıcı saklıyor.
  Faz 45'in eval terfisi ve Faz 47'nin yeniden oynatması **ham girdiye
  bağımlı** — bu çatışma önce karara bağlanmalı.
- **F-88** Guard kararının transcript'te gösterilmesi — arayüz yeni olay
  tiplerini henüz göstermiyor.
- **F-89** Kiracı bazlı guard kuralları — `ContentGuardContext.TenantId`
  taşınıyor ama yerleşik guard tek kural kümesi kullanıyor.

---

## 5. Öncelik Kümeleri (Kalan 20 Aday Kalem)

Aday listesi kalemleri üç kümeye ayırıyor. Aşağıda her kümenin **neden
aciliyetli** olduğu özetlenmiştir; ayrıntı için `docs/ADAYLAR.md`.

### 5.1 Kimlik ve çok kiracılılık — 🔴 en acil küme

Zinciri **F-56** açıyor ve Faz 50 onu acil hâle getirdi:

- **F-56** Kiracı bazlı API anahtarları — bugün gelen kimlik doğrulama **tek
  statik token**. Faz 50 bir dış yüzey (MCP sunucusu, A2A) açtı ve bu yüzeyi
  hâlâ o tek token koruyor. `AllowRemoteAccess` geçici bir savunma.
- **F-40** BYOK (kiracı başına sağlayıcı anahtarı) — bugün tüm kiracılar aynı
  faturayı paylaşıyor.
- **F-64** Gömülebilir sohbet bileşeni — F-56 olmadan güvenle gömülemez
  (son kullanıcı tarayıcısına yönetim token'ı konulamaz).
- **F-65** Gelen tetikleyiciler — F-56'dan sonra doğal.

**Neden en acil:** Dış yüzey (Faz 50) zaten canlı; kimlik zayıflığı bugün
**kullanılabilir durumda**, teorik değil.

### 5.2 İşletim boşlukları

Altısı da planlanmış bir fazın doğrudan devamı:

- **F-36** 🔴 Öksüz çalıştırma uzlaştırması — Faz 46 öksüz `Running` satırı
  **üretiyor** ve bunu düzelten kod henüz yok. Gösterge paneli yanlış sayıyor.
- **F-69** 🔴 Asenkron onay kutusu — kuyrukta koşan bir çalıştırma onay
  isterse **kimse cevap veremez** (kuyrukta istemci yok). Faz 46 bunu bir
  kolaylıktan eksiğe çevirdi.
- **F-74** Kanarya yayını ve otomatik geri alma — üç önkoşulun (F-52, F-55,
  F-71) üçü de artık planlı; listedeki **en hazır** kalem.
- **F-44** Model yedek zinciri ve yönlendirme — devre kesici açılınca
  çalıştırma bugün yalnız hata veriyor, başka sağlayıcıya geçmiyor.
- **F-59** Ön uçuş bütçe denetimi — `ModelDescriptor.ContextWindowTokens`
  tanımlı ama **hiç okunmuyor**; aşım hatası sağlayıcıdan geliyor, önceden
  yakalanmıyor.
- **F-45** Yanıt önbelleği — deterministik iş yüklerinde tekrar eden
  sorgular tekrar ödeniyor.

### 5.3 Uyum ve veri hakları — kurumsal satış kapısı

- **F-72** ⏸ ACS uyumu — ölçüldü, native bağımlılık riski nedeniyle
  ertelendi (GA olduğunda yeniden açılabilir).
- **F-75** Denetim izi hash zinciri — `audit_log` ruhen append-only ama
  teknik olarak değişmez değil.
- **F-58** GDPR silme/ihracat — F-75 ile **birlikte** tasarlanmalı (silme
  hakkı ↔ değişmezlik çatışması, `KARARLAR.md`'ye yazılacak türden bir karar).
- **F-41** İçerik şifreleme (at-rest) — sohbet geçmişi ve ekler açık metin.
- **F-61** Argüman düzeyinde tool politikası — onay kuralları bugün tool
  düzeyinde ("hep sor" ya da "hiç sorma"), koşul (`100 TL altı otomatik`)
  desteklenmiyor; bu, onay yorgunluğu üretiyor.

### 5.4 Bağımsız kalemler (istenen sırada yapılabilir)

- **F-34** Talimat şablonlama ve paylaşılan prompt kütüphanesi
- **F-48** GitOps (tanım dışa/içe aktarım)
- **F-50** Tipli yönetim istemcisi ve CLI (`dotnet agentprism`)
- **F-51** .NET Aspire entegrasyonu
- **F-67** Performans regresyon kapısı (`BenchmarkDotNet`)

---

## 6. Ek Öneriler

Bunlar mevcut aday listesinde **yer almayan**, bu incelemede ortaya çıkan
yeni fikirlerdir. Kod tabanında doğrulanmamıştır — uygulamadan önce
`maf-api-kesfi`/kod incelemesiyle teyit edilmelidir. Sırasıyla en somuttan
en spekülatife:

1. **Bütçe eşiği uyarısı (proaktif kota alarmı).** Faz 35 kota ölçerlerini
   (OTel gauge) verdi, Faz 21 giden webhook'u verdi — ama ikisini bağlayan
   "eşik aşılınca webhook tetikle" mantığı yok. Bugün bir operatör kota
   aşımını yalnız gösterge panelinde **görerek** fark ediyor. Düşük maliyetli,
   var olan iki altyapıyı birleştiren bir kalem.
2. **RAG belge tazeliği takibi.** Faz 51 vektör aramayı getirdi ama
   gömülerin ne zaman bayatladığını izleyen veya kaynak belge değiştiğinde
   yeniden gömmeyi tetikleyen bir mekanizma yok. `document_embeddings`
   tablosuna bir `source_updated_at`/`last_indexed_at` karşılaştırması ve
   isteğe bağlı bir "yeniden indeksle" ucu eklenebilir.
3. **Playground'da agent'sız tool deneme modu.** Tools ekranı bugün kayıt
   listesini gösteriyor; bir tool'u agent zincirine girmeden **tek başına**
   çağırıp çıktısını görebilme (parametre formuyla) doğrulanmalı — yoksa
   düşük maliyetli bir DX eklentisi olur. **Doğrulanmamış**: bu yetenek
   bugün var olabilir, önce kod incelemesi gerekir.
4. **A2A/MCP dış çağrının iç çağrı grafiğinde görünürlüğü.** Faz 12 iç
   çağrı grafiğini, Faz 50 dış yüzeyi verdi. Dışarıdan gelen bir A2A/MCP
   çağrısının hangi dış kimlikle geldiği bugün `ChildRunApproval`/
   `ExternalAgentProxy` üzerinden kısmen izlenebiliyor olabilir — arayüzde
   "bu çalıştırma dışarıdan mı tetiklendi, kimden" sorusuna doğrudan yanıt
   veren bir görünüm olup olmadığı **doğrulanmalı**.

---

## 7. Özet Tablo — Ne Yapılmalı, Neden

| Öncelik | Kalem | Tip | Gerekçe |
|---|---|---|---|
| 🔴 1 | F-76 OpenAPI 500 hatası | Kusur | Yayınlanmış bir özellik (Faz 40) çok-sağlayıcılı kurulumda kırık |
| 🔴 2 | F-56 kiracı API anahtarları | Güvenlik | Faz 50'nin dış yüzeyi bugün tek statik token'la korunuyor |
| 🔴 3 | F-36 öksüz çalıştırma uzlaştırması | Kusur | Faz 46 bu kusuru zaten üretiyor |
| 🔴 4 | F-69 asenkron onay kutusu | Eksik | Kuyruklu çalıştırmada onay akışı işlemez durumda |
| 🟡 5 | Faz 7 tetikleme eşiği kararı | Süreç | Her yeni faz public yüzeyi "bedava" büyütüyor; pencere kapanmadan karar verilmeli |
| 🟡 6 | F-74 kanarya + otomatik geri alma | Yetenek | Üç önkoşulu da tamam, en hazır kalem |
| 🟢 7 | README test sayısı düzeltmesi | Doküman | Küçük ama gerçek bir kayma |
| 🟢 8 | §4'teki 9 kapsam-dışı iş + §6 önerileri | Backlog | Sonraki planlama turunun kaynağı |

---

## Kaynaklar

- [`docs/arsiv/UCUNCU-FAZ-YOL-HARITASI.md`](UCUNCU-FAZ-YOL-HARITASI.md) — 26 kalemin
  plana dönüşüm gerekçesi, ölçülen kanıtlar, Faz 7 etki tablosu
- [`docs/ADAYLAR.md`](../ADAYLAR.md) — kalan 20 kalemin
  tam açıklaması (sorun, kapsam, değer, mercek, hazırlık, maliyet, risk,
  bağımlılık, ekosistem karşılaştırması)
- [`docs/KARARLAR-INDEKS.md`](../KARARLAR-INDEKS.md) · [`docs/arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](KARARLAR-INDEKS-REDDEDILEN.md) —
  alınmış ve reddedilmiş kararlar
- [`docs/arsiv/fazlar/07-SAGLAMLASTIRMA-VE-YAYIN.md`](fazlar/07-SAGLAMLASTIRMA-VE-YAYIN.md) — Faz 7
  kapsamı

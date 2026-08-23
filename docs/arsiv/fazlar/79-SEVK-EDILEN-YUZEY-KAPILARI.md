# Faz 79 — Sevk Edilen Yüzey Kapıları

> **Durum:** ✅ Tamamlandı (2026-08-21)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-125**, **F-136** (Dalga 13, Küme A'nın C# kapı yarısı)
> **Önkoşul:** Yok. [Faz 78](78-YETENEK-HARITASI-ERISIMI.md) `APG0402`'yi ekledi; bu faz onu da kapsar
> **Paketler:** `AgentPrism.Generators` (yalnız tanı metinleri) · test projesi `AgentPrism.Generators.UnitTests`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. İki kalem de test ve doküman kapısıdır
> **Tüketici yüzeyi:** `docs-site/` → `troubleshooting.md` (beş yeni tanı bölümü)
> · sevk edilen: iki XML `<example>` bloğu **yeniden yazılır** (`AgentPrismToolAttribute.cs`, `AgentPrismMcpServerBuilderExtensions.cs`)
> **Manuel test alanı:** [`docs/manuel-test/31-DOKUMAN-DOGRULUGU.md`](../../manuel-test/31-DOKUMAN-DOGRULUGU.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/79-SEVK-EDILEN-YUZEY-KAPILARI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism bugün üç şeyi sevk ediyor ve üçü de yanlış olabildiği hâlde **hiçbir kapı kızarmıyor**: XML `<example>` blokları (tüketicinin kopyaladığı kod), `APG` tanı kodları (tüketicinin derlemesinde gördüğü mesaj) ve bunların doküman karşılıkları. Bu faz iki kapıyı kurar.

## Bitiş Ölçütleri (DoD)

- [x] `ExampleCompilationTests` **49** bloğun tamamını derler/doğrular; blok sayısı iddiası testte yazılıdır — **not:** 49'un 45'i C# (Roslyn derlemesi), 4'ü `<code language="json">` (config parçası, `JsonDocument.Parse` ile doğrulanır). Plan "49 blok Roslyn ile derlenir" diyordu; ölçüm bunu düzeltti (Plandan Sapmalar #2)
- [x] İki elipsis bloğu yeniden yazılmıştır ve derlenir; **muafiyet tabanı açılmamıştır** — **not:** gerçekte değişen ikinci dosya plandaki gibi `AgentPrismMcpServerBuilderExtensions.cs` değil, `AzureOpenAIProviderOptions.cs`'dir (Plandan Sapmalar #3)
- [x] `<example>` taşıyan **15 paketin tamamı** test projesinden referanslıdır; referans kümesi ile bulunan paket kümesi testte karşılaştırılır
- [x] Sıfır blok bulunursa test düşer (boş küme tuzağı kapatıldı) — bağımsız sayım çapraz kontrolüyle (`CountRawExampleTags`), ablasyonla doğrulandı (bir `ProjectReference` kaldırılıp testin gerçekten kırmızı olduğu görüldü, sonra geri eklendi)
- [x] `DiagnosticIntegrityTests` **14** `APG` kodunun tamamını `troubleshooting.md`'de bulur; ters yön (ölü satır) de denetlenir — her iki yön de ad hoc kırmızı/yeşil ile canlı doğrulandı
- [x] `APG0002`…`APG0006` `troubleshooting.md`'de anlatılmıştır — İngilizce, her biri "ne oldu / neden / düzeltme"
- [x] Referans eklemenin **derleme süresine etkisi ölçüldü** ve dokümana yazıldı — Açık Soru 2
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [~] `samples/AgentPrism.Api` ile gerçek `run` yapılmadı — **gerekçe:** bu faz hiçbir runtime/HTTP yüzeyine dokunmuyor (bkz. "Planlanan Public API": public yüzey, HTTP endpoint'i ve arayüz payı üçü de "Yok"). İki kalem de derleme-anı/doküman kapısıdır; sample'da gösterilecek yeni bir çalışma-zamanı davranışı yok. Bunun yerine dokuz paketin **gerçek** unit/functional test paketleri (Anthropic, Azure, Google, Mcp, OpenAI, Testing, Voice, Workflows, AspNetCore.FunctionalTests — toplam ~1000+ test) yeniden koşuldu ve hepsi geçti; bu, "gerçek entegrasyon" ihtiyacının regresyon açısından karşılığıdır
- [x] `secret` taraması boş döndü (yalnız bu fazın dokunduğu dosyalarda; repodaki önceden var olan yerel test `Password=`/`sk-` literalleri bu fazdan bağımsızdır)
- [x] Manuel kabul case'leri `docs/manuel-test/31-DOKUMAN-DOGRULUGU.md` içine eklendi (`MT-DDG-025`…`MT-DDG-029`); 25, 26, 28 canlı koşuldu ve kırmızı olduğu görüldü, sonra geri alındı. 27 ve 29 mekanizma olarak aynı kod yolunu kullanır (25/26/28 ile doğrulanmıştır), ayrıca koşulmadı
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (iki 🟡 kapandı, bkz. Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` (`check:content` + `build` + `check:links` + `check:weight`) dördü de temiz

### Doğrulama komutları

```bash
# Kapı gerçekten 49 blok görüyor mu (test çıktısındaki sayı iddiası)
dotnet test tests/AgentPrism.Generators.UnitTests -c Release

# Descriptor sayısı ile dokümandaki kod sayısı eşit mi
grep -rhoE 'APG[0-9]{4}' src/AgentPrism.Generators/ | sort -u | wc -l
grep -ohE 'APG[0-9]{4}' docs-site/src/content/docs/troubleshooting.md | sort -u | wc -l

# Muafiyet tabanı hâlâ boş mu (yalnız yorum satırları)
grep -cv '^#' tests/AgentPrism.Core.UnitTests/Architecture/capability-example-baseline.txt
```

---

## Plandan Sapmalar

1. 🚨 **§79.1'in "yalnız iki yer tutucu (`app`, `agentPrism`) yeter" iddiası ölçülmeden
   yazılmıştı ve yanlıştı.** Adım 1 (`faz-uygulama`) gereği kod yazmadan önce
   ölçüldü: 49 bloğun serbest tanımlayıcılarını çıkaran küçük bir Python taraması,
   `builder` (`IHostApplicationBuilder`) adının **36/45** C# blokta bağlanmamış
   hâlde geçtiğini gösterdi — plan bunu hiç saymamıştı. Gerçek prelüd on iki
   değişken (`builder`, `app`, `agentPrism`, `apiKey`, `connectionString`,
   `configuration`, `refundTool`, `endpoint`, `audio`, `request`, `price`, `args`)
   ve beş test-yalnız saplama tip (`OnPremiseModelProvider`, `IOrderGateway`/
   `OrderGateway`, `NightlyReportJobHandler`, `CustomerNameGuard`, paylaşılan
   `OrderTools`) taşıyor — `ExamplePrelude.cs`. Her biri ölçümle geldi: derleme
   önce çalıştırıldı, gerçek `CSxxxx` hatası okundu, sonra placeholder eklendi
   (Adım 3 — "sözleşmeyi önce düşen testle sabitle").
2. **49 bloğun tamamı Roslyn ile derlenmiyor — 45'i C#, 4'ü JSON.** Plan
   "49 blok Roslyn ile derlenir" diyordu; ölçüm dört bloğun `<code
   language="json">` taşıdığını gösterdi (`ModelBinding.cs`,
   `Anthropic/Azure/OpenAIModelCatalog.cs` — `appsettings.json` parçaları, tam
   belge değil). `ExampleBlock.Language` alanı eklendi; JSON bloklar
   `{ + içerik + }` sarılıp `JsonDocument.Parse` ile doğrulanıyor, Roslyn'e hiç
   girmiyor. DoD'nin "49 bloğun tamamı derlenir" cümlesi bu ayrımla okunmalı.
3. **İkinci elipsis bloğu plandaki dosya değil, farklı bir dosyaydı.** §79.1
   `AgentPrismMcpServerBuilderExtensions.cs`'i "derlenmeyen elipsis bloğu"
   olarak adlandırıyordu; ölçüm gösterdi ki oradaki `// ...` geçerli bir C#
   yorumudur ve blok prelüd placeholder'ları eklenince zaten derleniyordu —
   hiç dokunulmadı. Gerçekte ikinci düzeltme gereken blok
   `AzureOpenAIProviderOptions.cs`'ti: `options.CredentialFactory = ...;`
   bildirilmemiş bir `options` adına atıfta bulunuyordu (`CS0103`). Çözüm
   örneği kendi kendine yeterli hâle getirmek oldu:
   `var options = new AzureOpenAIProviderOptions { CredentialFactory = ... };`.
4. **`OrderTools` örneği `static` sınıf olarak yazılamaz; `AgentPrismToolAttribute.cs`
   düzeltildi.** `AgentPrismToolAttribute.cs`'in örneği `internal static class
   OrderTools` gösteriyordu (elipsis düzeltmesiyle birlikte); `IAgentPrismBuilder.cs`'in
   AYRI bir örneği aynı adı `.AddToolsFrom<OrderTools>()` ile generic tip
   argümanı yapıyordu. C#, statik bir sınıfı generic tip argümanı olarak KABUL
   ETMEZ (`CS0718`) — iki örnek birlikte kopyalanan bir tüketicinin derlemesi
   gerçekten kırılırdı. `static` kaldırıldı; tool metodu (`GetOrderStatus`)
   APG0007 gereği `static` kalmaya devam ediyor, yalnız konteyner sınıf değil.
   Karar defterine yazıldı: K-546.
5. **Bir NuGet paketi test-yalnız eklendi: `Azure.Identity` 1.21.0.**
   `AzureOpenAIProviderOptions.CredentialFactory`'nin örneği `DefaultAzureCredential`
   adlandırıyor — gerçek bir tip, `AgentPrism.Azure`'un KASITLI OLARAK bağımlı
   olmadığı bir pakette (bkz. o sınıfın kendi XML dokümanı). Kapı bu örneğin
   gerçekten derlendiğini kanıtlamak zorunda olduğu için paket test projesine
   eklendi; `AgentPrism.Azure`'un kendi bağımlılık grafiği değişmedi (doğrulandı:
   `src/AgentPrism.Azure.csproj` dokunulmadı). Karar defterine yazıldı: K-547.
6. **`samples/AgentPrism.Api` ile gerçek `run` yapılmadı** — bu faz hiçbir
   runtime veya HTTP yüzeyine dokunmuyor (planın kendi "Planlanan Public API"
   bölümü zaten üçünü de "Yok" diye işaretliyordu). Bunun yerine dokuz paketin
   gerçek unit/functional test paketleri (~1000+ test) yeniden koşuldu; hiçbiri
   kırılmadı. DoD'de `[~]` ile işaretlendi, gerekçesiyle.
7. **İntegrasyon testleri (Postgres/SqlServer Testcontainers) ve Playwright
   E2E paketi bu oturumda koşulmadı** — kasıtlı: hiçbir `src/` çalışma-zamanı
   davranışı değişmedi (yalnız test-yalnız kod, XML doküman yorumu, ve bir
   nesne-başlatıcı söz dizimi değişikliği — davranışsız). Risk düşük görüldü;
   denetçi bu kapsam kararını sorgulamadı.

## Bu Fazda Verilen Kararlar

| **K-546 — `AgentPrismToolAttribute.cs`'in `OrderTools` örneği artık `static` DEĞİL; tool metodu yine `static`** | 2026-08-21 | Ölçüldü (F-125 uygulanırken): `IAgentPrismBuilder.cs`'in AYRI bir örneği `.AddToolsFrom<OrderTools>()` çağırıyor ve C# statik bir sınıfı generic tip argümanı olarak kabul etmiyor (`CS0718`) — iki örneği birlikte kopyalayan bir tüketici gerçekten derleyemezdi, bunu `ExampleCompilationTests` yakaladı. `ToolMethodScanner` yalnız metodun `IsStatic` olduğuna bakıyor, konteyner sınıfın statik olmasını istemiyor (APG0007 de yalnız metottan bahsediyor). `internal static class` → `internal class`; `[AgentPrismTool]` işaretli metot `public static` kaldı. | `AddToolsFrom<T>()` bir gün `Type` parametresi yerine gerçek bir generic kısıtlama YAZARSA (bugün öyle değil, yalnız `typeof(T)` kullanıyor) yeniden değerlendirilir |
| **K-547 — `Azure.Identity` yalnızca test projesine (`AgentPrism.Generators.UnitTests`) `PackageReference` olarak eklendi; `AgentPrism.Azure`'un bağımlılık grafiği DEĞİŞMEDİ** | 2026-08-21 | `AzureOpenAIProviderOptions.CredentialFactory`'nin sevk edilen `<example>`'ı `DefaultAzureCredential`'ı (o pakette) adlandırıyor — bilerek: `AgentPrism.Azure` o bağımlılığı ALMIYOR (yorum: "Azure.Identity does not appear... credential type is left to the consumer"). F-125'in derleme kapısı örneği GERÇEKTEN derlemek zorunda, bu yüzden gerçek tipe ihtiyaç duydu. `Directory.Packages.props`'ta `Label="Test"` grubuna eklendi (`Label="Saglayicilar"` değil — ilk taslak yanlış grup altına koymuştu, bağımsız denetim 🟢 olarak işaretledi, taşındı). | Bir tüketicinin dikte ettiği bir yönetilen kimlik senaryosu `AgentPrism.Azure`'un kendisine gerçek bir `Azure.Identity` bağımlılığı eklemeyi gerektirirse |

## Denetim Bulguları

`faz-denetim` skill'i taze bağlamlı bir `general-purpose` agent ile koşuldu
(git diff HEAD, 13 dosya). Sonuç: **🔴 yok.**

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | §79.1'in anlatısı, hangi iki dosyanın gerçekte düzeltildiğini yanlış anlatıyor (`AgentPrismMcpServerBuilderExtensions.cs` yerine `AzureOpenAIProviderOptions.cs`) | 🟡 | Düzeltildi — Plandan Sapmalar #3 |
| 2 | §79.2'nin istediği derleme-süresi ölçümü kapanıştan önce dokümana yazılmamıştı | 🟡 | Düzeltildi — Açık Soru 2 ve DoD satırı güncellendi |
| 3 | `Directory.Packages.props`'ta `Azure.Identity`, `Label="Test"` yerine `Label="Saglayicilar"` (üretim sağlayıcı grubu) altına eklenmişti | 🟢 | Düzeltildi (taşındı) — davranışı etkilemiyordu, yalnız organizasyon |
| 4 | `docs-site/scripts/check-weight.mjs`'in kod-içi yorumundaki "en ağır sayfa" ölçümü bu fazın eklemesinden sonra bayatladı (yorum 49 365 B diyor, gerçek 50 885 B) | 🟢 | Devredilmedi — bu fazın dokunmadığı bir dosyadaki yorum satırı, fonksiyonel etkisi yok (kapı dinamik ölçer). `docs/ADAYLAR.md`'ye F-NN açacak kadar değerli görülmedi |

## Sonraki Faza Devir Notu

1. **Devralınan sözleşme: `ExampleCompilationTests` kaynağı `src/**/*.cs`'ten
   DOĞRUDAN okur, build edilmiş XML'den DEĞİL** — `CapabilityExampleTests`'in
   tersi bir tercih, bilerek: bu kapı `dotnet build` çalışmadan da (temiz
   klonda) anlamlı, ve derlenmiş XML'in bayatlama riskini (bkz.
   `hafiza/dokumantasyon.md`'deki `--skip-docfx` tuzağı) hiç taşımıyor. Yeni bir
   `<example>` ekleyen bir sonraki faz bunu bilmeli: kaynağı değiştirmek yeter,
   `dotnet build` koşmak GEREKMEZ.
2. 🚨 **Yeni bir `<example>` bloğu ekleyen her faz, blokta geçen serbest
   tanımlayıcıyı `ExamplePrelude.Placeholders`'a bakmadan VARSAYMASIN.** Bu
   fazın kendi kör noktası tam bu oldu (Plandan Sapmalar #1). Yeni bir blok
   `CS0103`/`CS0246` ile düşerse iki seçenek var: (a) isim 2+ blokta tekrar
   ediyorsa `ExamplePrelude.cs`'e yeni bir kalıcı placeholder/saplama tip ekle,
   (b) tek bir bloğa özgüyse örneği kendi kendine yeterli hâle getir (aynı
   `AzureOpenAIProviderOptions.cs` düzeltmesinin deseni).
3. **Devralınan sözleşme: `troubleshooting.md`'deki her `APG` kodu artık
   canlı bir kapı taşıyor** (`DiagnosticIntegrityTests.Every_diagnostic_is_explained_on_the_troubleshooting_page`
   ve tersi). Yeni bir `APG` tanısı ekleyen bir sonraki faz — `Directory.Build.props`'un
   `RS2000` (`AnalyzerReleases.Unshipped.md`) kapısına EK OLARAK — bu sayfaya
   bir `###` bölüm de eklemek zorunda, yoksa test kırmızı kalır.
4. **`AgentPrism.Generators.UnitTests` artık 15 paketin tamamına referans
   veriyor** (önceden 4'tü). Yeni bir paket `<example>` taşımaya başlarsa
   `Every_package_carrying_an_example_is_referenced_by_this_project` testi
   otomatik kırmızı olur ve hangi paketin eksik olduğunu adlandırır —
   `AgentPrism.slnx`'e eklenen bir sonraki paket bunu unutmamalı, kapı zaten
   hatırlatıyor.
5. **Yarım kalan iş yok.** DoD'nin tamamı işaretlendi (bir satır `[~]` —
   gerekçeli, uygulanamaz). Faz `F-125` ve `F-136`'nın ikisini de kapattı.
6. **Sıradaki faz: Faz 80 (`80-DOKUMAN-KAPILARININ-DOGRULUGU.md`)** —
   `docs/YOL-HARITASI.md`'de bir sonraki "📋 Planlandı" kalemdir.

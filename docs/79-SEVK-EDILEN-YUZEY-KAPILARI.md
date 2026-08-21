# Faz 79 — Sevk Edilen Yüzey Kapıları

> **Durum:** ✅ Tamamlandı (2026-08-21)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-125**, **F-136** (Dalga 13, Küme A'nın C# kapı yarısı)
> **Önkoşul:** Yok. [Faz 78](78-YETENEK-HARITASI-ERISIMI.md) `APG0402`'yi ekledi; bu faz onu da kapsar
> **Paketler:** `AgentPrism.Generators` (yalnız tanı metinleri) · test projesi `AgentPrism.Generators.UnitTests`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. İki kalem de test ve doküman kapısıdır
> **Tüketici yüzeyi:** `docs-site/` → `troubleshooting.md` (beş yeni tanı bölümü)
> · sevk edilen: iki XML `<example>` bloğu **yeniden yazılır** (`AgentPrismToolAttribute.cs`, `AgentPrismMcpServerBuilderExtensions.cs`)
> **Manuel test alanı:** [`docs/manuel-test/31-DOKUMAN-DOGRULUGU.md`](manuel-test/31-DOKUMAN-DOGRULUGU.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu üçünü grep'le:
   ```bash
   grep -n "K-421\|K-522\|K-538" docs/KARARLAR.md
   ```
   **K-421** (public API takibi açık ama hiçbir şey sevk edilmedi), **K-522**
   (tüketici doküman standardı `tuketici-dokuman-senkronu` skill'ine taşındı),
   **K-538** (`APG0402` yalnız yerel referans dosyası gerçekten yazılırken öter)
3. [`78-YETENEK-HARITASI-ERISIMI.md`](78-YETENEK-HARITASI-ERISIMI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/78-YETENEK-HARITASI-ERISIMI.md
   ```
   Faz 78 `APG0402`'yi ve yetenek haritası bütçesini bıraktı; bu faz o tanının
   dokümante edilme sözleşmesini devralır.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/analyzer-yazimi.md`](hafiza/analyzer-yazimi.md) (tanı descriptor'ları ve
   `AnalyzerTestHelper`) · [`hafiza/dokumantasyon.md`](hafiza/dokumantasyon.md)
   (içerik kapısı yazarken, `check-content.mjs`, site yayın hattı)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MAF-GENISLEME-NOKTALARI.md`](MAF-GENISLEME-NOKTALARI.md) — yalnız `<example>`
   bloklarının hangi giriş noktalarını anlattığını görmek için

---

## Amaç

AgentPrism bugün üç şeyi sevk ediyor ve üçü de yanlış olabildiği hâlde
**hiçbir kapı kızarmıyor**: XML `<example>` blokları (tüketicinin kopyaladığı
kod), `APG` tanı kodları (tüketicinin derlemesinde gördüğü mesaj) ve bunların
doküman karşılıkları. Bu faz iki kapıyı kurar. Kazanan, paketi ilk kez kuran
geliştirici ve tüketicinin kod agent'ıdır: kopyaladıkları örnek derlenir, aldıkları
tanı kodunun bir karşılığı bulunur.

- **F-125** — 49 `<example>` bloğunun tamamı Roslyn ile derlenir; derlenmeyen blok
  derlemeyi kırar
- **F-136** — 14 `APG` tanı kodunun tamamı `troubleshooting.md`'de geçer; geçmeyen
  kod derlemeyi kırar

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`CapabilityExampleTests.cs`](../tests/AgentPrism.Core.UnitTests/Architecture/CapabilityExampleTests.cs) | Dört kapı var (`Every_registration_entry_point_shows_a_worked_example`, `The_reader_sees_every_entry_point_including_the_generic_ones`, `No_example_teaches_a_registration_that_does_not_exist`, `Every_example_calls_the_member_it_documents`) — **dördü de ad denetimi**. Hiçbiri derlemiyor |
| [`AnalyzerTestHelper.cs:82`](../tests/AgentPrism.Generators.UnitTests/AnalyzerTestHelper.cs#L82) | `ShouldCompileCleanly(string source)` **zaten var**. F-125'in ihtiyacı olan ilkel hazır; kurulacak şey blok çıkarma ve prelüd |
| [`AnalyzerTestHelper.cs:97`](../tests/AgentPrism.Generators.UnitTests/AnalyzerTestHelper.cs#L97) | `BuildReferences()` `TRUSTED_PLATFORM_ASSEMBLIES` okur — derleme yalnız **test projesinin kendi referanslarını** görür |
| [`ToolDiagnostics.cs`](../src/AgentPrism.Generators/ToolDiagnostics.cs) · [`UsageDiagnostics.cs`](../src/AgentPrism.Generators/UsageDiagnostics.cs) | Toplam **14** descriptor: `APG0001`…`APG0007` ve `APG0101`, `APG0102`, `APG0201`, `APG0301`, `APG0302`, `APG0401`, `APG0402` |
| [`DiagnosticIntegrityTests.cs:128`](../tests/AgentPrism.Generators.UnitTests/DiagnosticIntegrityTests.cs#L128) | `Every_help_link_resolves_to_a_section_of_the_capability_map` her kodun **yardım bağlantısını** `capabilities.md` başlığına bağlıyor — ama kodun kendisinin bir sayfada **anlatıldığını** hiçbir kapı istemiyor |
| `docs-site/.../troubleshooting.md` | **9** kod geçiyor. **Beşi yok:** `APG0002` · `APG0003` · `APG0004` · `APG0005` · `APG0006` |
| `capability-example-baseline.txt` | 4 satırın **dördü de yorum** → sıfır muafiyet. Dosya kendi sözünü yazıyor: *"born empty and that is the intended state"* |

> Kanıtlar **2026-08-21** tarihinde yeniden doğrulandı. Aday kaydına göre **üç
> düzeltme** yapıldı ve bunlar plana işlendi:
>
> 1. Kayıt *"`guides/coding-agents.md` ve `troubleshooting.md` birlikte 9 kod
>    taşıyor"* diyordu. Ölçüm: **her biri ayrı ayrı** aynı dokuzu taşıyor.
> 2. Kayıt elipsis blok sayısını 2 diyordu ve **doğru** — ama dosya seviyesinde
>    grep 10 dosya gösterir; sayım **blok** seviyesinde yapılmalıdır.
> 3. Kayıt kapının maliyetini yazmamıştı. Ölçüldü: 49 blok **15 pakete** yayılmış,
>    test projesi bugün yalnız **dördünü** görüyor (§79.2).

---

## 79.1 — `<example>` derleme kapısı (F-125)

Kapı `AgentPrism.Generators.UnitTests` içinde yaşar. Gerekçe ölçüldü: Roslyn
(`Microsoft.CodeAnalysis.CSharp`) ve `ShouldCompileCleanly` orada; `Core.UnitTests`
ikisini de taşımıyor. F-136 da aynı projeye düştüğü için iki kalem tek yerde
buluşur.

```mermaid
flowchart LR
    A["src/**/*.cs<br/>XML yorumları"] --> B["Blok çıkarıcı<br/>&lt;example&gt;…&lt;/example&gt;"]
    B --> C["Prelüd birleştirici<br/>using + yer tutucu"]
    C --> D["AnalyzerTestHelper<br/>ShouldCompileCleanly"]
    D -->|"CS hatası"| E["❌ derleme kırılır"]
    D -->|"temiz"| F["✅ kapı geçer"]
```

**Prelüd yalnız iki yer tutucu ister** (ölçüldü): `app` **6** blokta,
`agentPrism` **1** blokta. `options`, `o`, `context` ve `services` blok içinde
lambda parametresi olarak bağlanır; prelüd istemez.

Blokların çoğu bir `using` bloğu taşımaz, çünkü XML örneği bağlamı varsayar.
Prelüd bu yüzden sabit bir `using` kümesi ve iki yer tutucu bildirimi ekler.
Prelüdün kendisi **testte** yaşar, sevk edilen XML'de değil.

### İki elipsis bloğu yeniden yazılır

`AgentPrismToolAttribute.cs` ve `AgentPrismMcpServerBuilderExtensions.cs`
bloklarında `...` vardır ve derlenmez. Muafiyet tabanı **açılmaz** (kullanıcı
kararı 👤): var olan `capability-example-baseline.txt` kendi sözünü *"yalnız
küçülür, boş doğdu"* diye yazıyor; ikinci bir muafiyet listesi o sözü bozar.
İki blok derlenebilir hâle getirilir — tüketici de böylece kopyalanabilir bir
örnek görür.

## 79.2 — On bir proje referansı (F-125'in gerçek maliyeti)

49 blok **15 pakete** yayılmıştır:

| Paket | Blok | Paket | Blok |
|---|---|---|---|
| `AgentPrism.Core` | 19 | `AgentPrism.Testing` | 1 |
| `AgentPrism.AspNetCore` | 7 | `AgentPrism.SqlServer` | 1 |
| `AgentPrism.Workflows` | 4 | `AgentPrism.UI` | 1 |
| `AgentPrism.Azure` | 3 | `AgentPrism.PostgreSql` | 1 |
| `AgentPrism.OpenAI` | 3 | `AgentPrism.Sqlite` | 1 |
| `AgentPrism.Anthropic` | 2 | `AgentPrism.Google` | 1 |
| `AgentPrism.Mcp` | 2 | `AgentPrism.Voice` | 1 |
| `AgentPrism.Abstractions` | 2 | | |

Test projesi bugün **dördünü** görüyor: `Core`, `AspNetCore`, `OpenAI`,
`Generators` (`Abstractions` `Core` üzerinden geçişli gelir). Kalan **11**
paket `ProjectReference` olarak eklenir: `Workflows` · `Azure` · `Anthropic` ·
`Mcp` · `Testing` · `SqlServer` · `UI` · `PostgreSql` · `Sqlite` · `Google` ·
`Voice`.

Bu kullanıcı kararıdır 👤. Ölçülen gerekçe: `DependencyDirectionTests` yalnız
`src/` katmanlarını kısıtlar, test projesini değil — yani mimari bir ihlal yoktur.
Alternatifler reddedildi: yeni bir test projesi 17. projeyi ve paket kontrol
listesini getirirdi; `artifacts/bin`'den DLL yolu ile referans vermek derleme
sırasını garanti etmez ve çıktı dizin düzenine bağlanırdı.

🚨 **Uygulayan oturum için tuzak:** referans eklemek derleme süresini büyütür ve
`AgentPrism.UI` referansı arayüz derlemesini test projesinin önkoşulu yapar.
Ölçüm gerekir: referanslar eklendikten sonra `dotnet build` süresi yazılır. Süre
kabul edilemez çıkarsa çözüm referansı düşürmek değil, `AgentPrism.UI`'nin tek
bloğunu taşımaktır — karar o anda alınır ve kapanışta yazılır.

## 79.3 — `APG` tanı kodu doküman kapısı (F-136)

Kapı `DiagnosticIntegrityTests`'in kardeşidir ve aynı dosyada yaşar. Var olan
`Every_help_link_resolves_to_a_section_of_the_capability_map` **yardım
bağlantısını** kontrol eder; yeni kapı **kodun kendisinin anlatıldığını**
kontrol eder.

**Zorunlu ev `troubleshooting.md`'dir** (kullanıcı kararı 👤). Gerekçe: bir kod
gören tüketicinin gittiği sayfa orasıdır ve zaten dokuzu taşır.
`guides/coding-agents.md` bir **seçki** olarak özgür kalır — agent'a yönelik bir
sayfanın her kodu tekrarlaması gerekmez.

### Beş kod yazılacak

| Kod | Başlık |
|---|---|
| `APG0002` | Invalid tool name |
| `APG0003` | Unsupported parameter type |
| `APG0004` | A generic method cannot be a tool |
| `APG0005` | No marked tool method |
| `APG0006` | Tool description missing |

Her biri `troubleshooting.md`'de bir bölüm alır: ne olduğu, neden ötüğü,
düzeltmenin ne olduğu. Metin **İngilizce**dir (dil sınırı).

---

## Planlanan Public API

**Yok — bu faz public yüzeyi büyütmez.** İki kalem de test ve doküman kapısıdır.

Doğrulandı (2026-08-21): `wc -l src/*/PublicAPI.Shipped.txt` → 16 satır / 16
dosya, yani her dosya yalnız `#nullable enable` taşıyor. Hiçbir şey sevk
edilmemiştir; yine de bu faz yüzeye dokunmadığı için o gerçeğin bir maliyeti yok.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok — arayüz kaynağına dokunulmaz. `AgentPrism.UI` yalnız **referans** olarak
eklenir (tek bir `<example>` bloğu için); bundle üretilmez, `wwwroot` değişmez.

---

## Planlanan Dosya Listesi

```
tests/AgentPrism.Generators.UnitTests/
├── AgentPrism.Generators.UnitTests.csproj   (11 ProjectReference eklenir)
├── Examples/
│   ├── ExampleBlock.cs                      (blok + kaynak dosya + satır)
│   ├── ExampleExtractor.cs                  (XML'den <example> çıkarır)
│   ├── ExamplePrelude.cs                    (using kümesi + app/agentPrism)
│   └── ExampleCompilationTests.cs           (F-125 kapısı)
└── DiagnosticIntegrityTests.cs              (F-136 kapısı eklenir)

src/AgentPrism.Abstractions/Tools/
└── AgentPrismToolAttribute.cs               (elipsis bloğu yeniden yazılır)

src/AgentPrism.AspNetCore/McpServer/
└── AgentPrismMcpServerBuilderExtensions.cs  (elipsis bloğu yeniden yazılır)

docs-site/src/content/docs/
└── troubleshooting.md                       (beş yeni tanı bölümü)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Bir `<example>` bloğu var olmayan bir API adlandırır (`CS1061`) | Birim (Roslyn derleme) | `ExampleCompilationTests` |
| Bir blok var olan ada **yanlış biçimde** atar (`CS0200`, salt okunur özellik) | Birim (Roslyn derleme) | `ExampleCompilationTests` |
| Çıkarıcı bir bloğu **sessizce atlar** ve kapı 49 yerine 30 blok görür | Birim | `ExampleCompilationTests` — sayı iddiası: çıkarılan blok sayısı beklenen sayıya eşit olmalı |
| Prelüd bir yer tutucuyu bağlamaz; blok `CS0103` ile düşer | Birim | `ExampleCompilationTests` |
| 🚨 Kapı **hiçbir şey derlemez** ve yine de yeşil kalır (boş küme tuzağı) | Birim | `ExampleCompilationTests` — sıfır blok bulunursa test **düşer** |
| Yeni bir paket eklenir, `<example>` taşır ama referans eklenmez → blokları sessizce kapsam dışı kalır | Birim | `ExampleCompilationTests` — bulunan paket kümesi referans kümesiyle karşılaştırılır; fark **düşürür** |
| Yeni bir `APG` descriptor eklenir, dokümana yazılmaz | Birim | `DiagnosticIntegrityTests` (yeni `Theory`) |
| Bir kod dokümanda geçer ama descriptor silinmiştir (ölü satır) | Birim | `DiagnosticIntegrityTests` — ters yön de denetlenir |

Beş soru, bu fazın kod yolları için:

| Soru | Cevap |
|---|---|
| İptal | Yok — kapılar senkron test kodudur, `CancellationToken` yalnız `TestContext.Current` üzerinden geçer |
| Eşzamanlılık | Yok — dosya okuma ve Roslyn derleme, paylaşılan durum yok |
| Boş/aşırı girdi | **Var ve kritik:** sıfır blok bulunması sessiz bir geçiş üretir. Tabloda kendi satırı var |
| Başka kiracının kaydı | Yok — kiracı kavramı bu fazda yok |
| Alt sistem hatası | Kaynak dosya okunamazsa test düşer; bu doğru davranıştır (kapı kapı olmaktan çıkmamalı) |

Sözleşme testi **gerekmez** — hiçbir davranış DI, HTTP, kiracı, akış veya depo
sınırını geçmiyor. İkisi de derleme anı kapısıdır.

---

## Manuel Kabul Case'leri

> Kapanışta [`docs/manuel-test/31-DOKUMAN-DOGRULUGU.md`](manuel-test/31-DOKUMAN-DOGRULUGU.md)
> içine eklenecek case'lerin taslağı.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Temiz çalışma kopyası | Bir `<example>` bloğuna var olmayan bir üye ekle (`options.NoSuchThing = 1;`), `dotnet test tests/AgentPrism.Generators.UnitTests -c Release` | Test **düşer** ve mesaj dosya adını + `CS1061`'i adlandırır |
| 2 | Temiz çalışma kopyası | `ToolDiagnostics.cs`'e `APG0008` adında yeni bir descriptor ekle, `dotnet test tests/AgentPrism.Generators.UnitTests -c Release` | Test **düşer**: `APG0008` `troubleshooting.md`'de geçmiyor |
| 3 | Temiz çalışma kopyası | `troubleshooting.md`'den `APG0003` bölümünü sil, testi koş | Test **düşer** |
| 4 | Temiz çalışma kopyası | Tüm `<example>` bloklarını geçici olarak sil, testi koş | Test **düşer** — "sıfır blok bulundu" (kapının kendisi çalışıyor mu) |
| 5 | 👤 insan gerekir | `docs-site`'ta `npm run build`, `troubleshooting` sayfasını tarayıcıda aç | Beş yeni bölüm görünür; başlık slug'ları yardım bağlantılarıyla çakışmıyor |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Prelüd tek bir sabit `using` kümesi mi, blok başına çıkarım mı? | A: tek sabit küme (tüm paketlerin ana namespace'leri) · B: blokta geçen tipe göre çıkarım | **A seçildi** — ama plan `only two placeholder`'ı ölçmeden varsaymıştı; gerçek küme çok daha büyük çıktı, bkz. Plandan Sapmalar #1 |
| 2 | `AgentPrism.UI` referansı derleme süresini kabul edilemez büyütürse? | A: o tek bloğu başka pakete taşı · B: UI'yi kapsam dışı bırak ve baseline'a yaz | **Ölçüldü, sorun çıkmadı**: test projesi için hot incremental build ~7 sn, temiz (obj/bin silinmiş) build ~8.4 sn — CLAUDE.md'nin "~5 sn" taban çizgisiyle aynı mertebede. A/B seçimi gerekmedi |
| 3 | Beş tanı bölümü `troubleshooting.md`'nin neresine girer? | A: var olan `APG` bölümünün altına sırayla · B: yeni bir "Generator diagnostics" alt başlığı | **A** — sayfa zaten dokuz kodu sıralı taşıyor; ikinci bir grup okuyucuyu böler |
| 4 | Kapı `docs-site/dist/` çıktısını mı, kaynak `.md`'yi mi okur? | A: kaynak `.md` · B: üretilen `dist/` | **A** — `dist/` derleme çıktısıdır ve temiz klonda yoktur; `check-content.mjs` tuzağı aynısıdır (`hafiza/dokumantasyon.md`) |

---

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

## Riskler

| Risk | Önlem |
|------|-------|
| 11 proje referansı derleme süresini gözle görülür büyütür | Süre **ölçülür** ve dokümana yazılır. Açık Soru 2 kabul edilemez durumu karara bağlar |
| Blok çıkarıcı XML kaçışlarını (`&lt;`, `&amp;`) çözmezse blok yanlış derlenir | Çıkarıcı `System.Xml` ile çözer, elle `Replace` ile değil. Test: kaçış taşıyan bir blok |
| Prelüd blok içindeki adla çakışır (`app` iki kez bildirilir) | Prelüd yer tutucuyu **yalnız blokta bağlı değilse** ekler; test: her iki durum |
| 🚨 Kapı yazılır ama hiçbir şey bulmaz ve yeşil kalır | Sıfır blok testi DoD'dedir. Bu repoda aynı sınıf tuzak `check-content.mjs`'te yaşandı (`hafiza/dokumantasyon.md`) |
| `troubleshooting.md` bütçeyi aşar | Beş bölüm kısa tutulur; aşarsa içerik **silinmez**, sayfa bölünür (`dokuman-bakim.py` kararı) |
| `AgentPrism.UI` referansı E2E/frontend derlemesini test projesine bağlar ve `-p:AgentPrismFrontendEnabled=false` ile iç döngü kırılır | Bayraklı koşum **ölçülür**. Kırılırsa Açık Soru 2'nin A seçeneği uygulanır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

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

## Gerçekleşen Public API

**Yok** — plan doğruydu. `wc -l src/*/PublicAPI.Shipped.txt` hâlâ 16 satır/16
dosya (yalnız `#nullable enable`); `PublicAPI.Unshipped.txt` dosyaları dokunulmadı.
Tek görünürlük değişikliği `AnalyzerTestHelper.References`'in `private` →
`internal` olmasıdır (test projesi içi, tüketiciye gitmez).

## Dosya Listesi (gerçekleşen)

```
tests/AgentPrism.Generators.UnitTests/
├── AgentPrism.Generators.UnitTests.csproj   (11 ProjectReference + Azure.Identity PackageReference)
├── AnalyzerTestHelper.cs                    (References: private → internal)
├── DiagnosticIntegrityTests.cs              (F-136: 2 yeni test + ApgCodePattern + TroubleshootingPath)
└── Examples/
    ├── ExampleBlock.cs                      (record: yol, satır, kod, dil)
    ├── ExampleExtractor.cs                  (src/**/*.cs'ten doğrudan okur, XElement.Parse ile decode eder)
    ├── ExamplePrelude.cs                    (12 placeholder + 5 saplama tip + tip-bildirimi bloğu için özel dal)
    └── ExampleCompilationTests.cs           (F-125: C# → Roslyn, JSON → JsonDocument.Parse)

src/AgentPrism.Abstractions/Tools/
└── AgentPrismToolAttribute.cs               (elipsis düzeltildi; OrderTools artık static değil)

src/AgentPrism.Azure/
└── AzureOpenAIProviderOptions.cs            (örnek kendi kendine yeterli: `var options = new ...`)

docs-site/src/content/docs/
└── troubleshooting.md                       (APG0002-APG0006, 5 yeni ### bölüm)

Directory.Packages.props                     (Azure.Identity 1.21.0, Label="Test")
docs/manuel-test/31-DOKUMAN-DOGRULUGU.md      (MT-DDG-025..029)
```

Planın öngördüğü `AgentPrismMcpServerBuilderExtensions.cs` değişikliği
**gerçekleşmedi** (Plandan Sapmalar #3) — o dosya diff'te yok.

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

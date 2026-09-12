# 24 — Test Paketi ve Proje Şablonu (`TEST`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../24-TEST-PAKETI-VE-SABLON.md`](../../24-TEST-PAKETI-VE-SABLON.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/24-TEST-PAKETI-VE-SABLON.md
> ```

---

## Temiz geçen case'ler (35)

| Case | Durum | Başlık |
|---|---|---|
| MT-TEST-001 | ☑ | Şablon paketten kurulur ve `dotnet new list`'te görünür |
| MT-TEST-002 | ☑ | En yalın birleşim (`memory`+`openai`+`ui:false`) sıfır uyarıyla derlenir |
| MT-TEST-003 | ☑ | En dolu birleşim (`sqlserver`+`azure`+`ui:true`) sıfır uyarıyla derlenir |
| MT-TEST-004 | ☑ | Üretilen `appsettings.json` yalnız boş placeholder taşır, hiçbir dosyada `secret` görünümlü değer yok |
| MT-TEST-005 | ☑ | Üretilen `Program.cs` hiçbir sağlayıcı için sabit bir model adı taşımaz |
| MT-TEST-006 | ☑ | `-n` ile yeniden adlandırma: `Tracon.Starter` dizesi hiçbir dosyada/dosya adında kalmaz |
| MT-TEST-007 | ☑ | Varsayılan (`memory`) birleşim kurulumsuz `dotnet run` ile ayağa kalkar |
| MT-TEST-009 | ☑ | `--skip-restore` restore adımını atlar |
| MT-TEST-010 | ☑ | `--TraconVersion` belirli bir sürüme sabitler |
| MT-TEST-011 | ☑ | Tanınmayan bir `--persistence` değeri reddedilir |
| MT-TEST-012 | ☑ | `-h` çıktısında üç bayrak görünür, `TraconVersion` gizlidir |
| MT-TEST-013 | ☑ | Şablon paketi derlenmez; üretilen proje `Tracon.Templates`'e hiç referans vermez |
| MT-TEST-021 | ☑ | `EchoesUserMessage()` son kullanıcı mesajını `Echo: ` öneki ile yankılar |
| MT-TEST-022 | ☑ | `RespondsWith(...)` yanıtları sırayla tüketir |
| MT-TEST-023 | ☑ | Kuyruk tükendikten sonra `EchoesUserMessage()` fallback'i devreye girer |
| MT-TEST-024 | ☑ | Fallback tanımlanmamışsa kuyruk tükendikten sonra sabit `"fake response"` tekrar tekrar döner |
| MT-TEST-025 | ☑ | `CallsTool(...)` bir `FunctionCallContent` üretir; anonim tip argümanları yansımayla sözlüğe kopyalanır |
| MT-TEST-026 | ☑ | `ForModel(...)` farklı modeller için bağımsız kuyruk tutar |
| MT-TEST-027 | ☑ | `EchoesLastToolResult(prefix)` gerçek tool-çağrı boru hattı üzerinden son tool sonucunu yankılar |
| MT-TEST-028 | ☑ | `RespondsWith(text, inputTokens, outputTokens)` bildirilen kullanım gerçek boru hattında `RunRecord.Usage`'a yansır |
| MT-TEST-029 | ☑ | `Requests` listesi gönderilen mesaj geçmişini ve `ChatOptions.Tools`'u kaydeder |
| MT-TEST-030 | ☑ | `Models` kataloğunda olmayan bir model adı agent kaydını ENGELLEMEZ |
| MT-TEST-040 | ☑ | `StartAsync()` hiçbir yapılandırma olmadan ayağa kalkar, `/tracon/api/meta` `200` döner |
| MT-TEST-041 | ☑ | Özel `Prefix` yalnız o önekten yanıt verir, varsayılan önek artık yanıt vermez |
| MT-TEST-042 | ☑ | `DisposeAsync()` sonrası `Client` kullanılırsa `ObjectDisposedException` fırlatılır |
| MT-TEST-043 | ☑ | `RunAsync` var olmayan bir agent adıyla çağrılırsa `TraconAssertionException` fırlatılır |
| MT-TEST-045 | ☑ | `ConfigureServices`, `AddTracon()` çağrısından ÖNCE çalışır |
| MT-TEST-050 | ☑ | `ShouldHaveCompleted()` geçer; başarısız bir çalıştırmada beklenen/bulunan durumu yazan mesajla düşer |
| MT-TEST-051 | ☑ | `ShouldHaveFailedWith(errorType)` kararlı bir hata tipini doğrular |
| MT-TEST-052 | ☑ | `ShouldHaveCalledTool(name, times:)` sayı uyuşmazsa beklenen/bulunan sayıyı yazan mesajla düşer |
| MT-TEST-053 | ☑ | `ShouldNotHaveCalledTool(name)` çağrılmış bir tool için düşer |
| MT-TEST-055 | ☑ | Zincirleme iddialar art arda çalışır |
| MT-TEST-060 | ☑ | Paketlenmiş `.nuspec` hiçbir test çerçevesi bağımlılığı taşımaz |
| MT-TEST-061 | ☑ | Meta paket (`Tracon`) `Tracon.Testing`'e referans VERMEZ |
| MT-TEST-064 | ☑ | Depo dışı tüketici: gerçek model çağırmadan uçtan uca bir agent testi |

## Ayrıntı taşıyan case'ler (6)

## MT-TEST-008 — `postgres`/`openai` seçiminde ayrı bir paket referansı EKLENMEZ (zaten meta pakette)

**Gerçek sonuç**
- **Doküman düzeltmesi**: "`diff` boş döner" iddiası, case'in KENDİ
  girilecek-veri adımlarıyla (`-n Meta.Kontrol` VS `-n Meta.Kontrol2`,
  İKİ FARKLI proje adı) çelişiyor — farklı proje adı `RootNamespace`'i VE
  rastgele üretilen `UserSecretsId` GUID'ini kaçınılmaz olarak
  değiştiriyor, `diff` bu iki satırda fark gösteriyor (doğrulandı).
  Asıl doğrulanmak istenen özdeş iddia bu değil: her iki `.csproj`
  dosyası da yalnız TEK bir `<PackageReference Include="Tracon"
  Version="0.0.0-preview.0.107" />` satırı taşıyor, `postgres` seçmek
  EK bir `PackageReference` satırı EKLEMİYOR — bu, doğrulanmak istenen
  gerçek iddia, ve doğru. Kod kusuru değil, doküman ifadesi düzeltmeli
  ("`diff` yalnız `PackageReference` satırlarında boş döner" olmalı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-020 — Varsayılan kurulum sabit `"fake response"` döner; katalog tek bir `fake-model` girdisi taşır

**Gerçek sonuç**
- **Doküman düzeltmesi**: verilen kod aynen yapıştırılınca `CS0246:
  'ModelBinding' bulunamadı` ile derlenmedi — `ModelBinding` tipi
  `Tracon` ad alanındadır, doküman yalnız `using Tracon.Testing;`
  yazmış, `using Tracon;` eksik. `using Tracon;` eklenince: çıktı
  `Models.Count: 1`, `Model adi: fake-model`, `Yanit: fake response` —
  beklenenle birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-044 — 🚨 K-218 tuzağının ampirik kanıtı: `AIFunctionArguments.Services`'ten çözülen bağımlılık `null` gelir

**Gerçek sonuç**
- Düzeltilmiş script çalıştırıldı: `Tool sonucu: SERVICE COZULEMEDI` —
  düzeltilmiş beklentiyle **tam örtüşüyor**. Kod okumasıyla da doğrulandı:
  MAF, `AIFunctionArguments.Services`'i asla gerçek `null` göndermez —
  daima `Microsoft.Extensions.AI.EmptyServiceProvider`ın (boş ama `null`
  OLMAYAN) bir örneğini gönderir (bkz. `ToolMethodScanner.cs:22,93`,
  `VoiceToolBase.cs:22`, `ToolRegistrationTests.cs:102-105`'teki tutarlı
  yorumlar). Doküman case'inin ESKİ `args.Services is null` denetimi bu
  yüzden HER ZAMAN `false` dönüyordu — yanlış koşulu sınıyordu; script
  düzeltildi. K-218'in ASIL iddiası (gerçek DI kayıtları `Services`
  üzerinden ÇÖZÜLEMEZ) doğrulandı: `Services is null: False;
  GetService(MyRegisteredService): NULL`. `Directory
  .Packages.props`'ta MAF/`Microsoft.Extensions.AI` sürümleri K-218
  yazıldığından beri değişmedi, `AgentDefinitionCompiler.cs:945`'teki
  `AsAIAgent(options, _loggerFactory, _services)` çağrısı da hiç
  değişmedi (git log doğrulandı) — üretim boru hattında hiçbir şey
  değişmedi, yalnızca doküman örneğinin ESKİ denetim koşulu (`is null` vs
  `GetService(...) is null`) yanlıştı. K-218'in kendisi hâlâ tam olarak
  geçerli.

---

**Doküman düzeltmesi (2026-08-15):** Script ve beklenti koda göre
düzeltildi. Üretim değişikliği yok — K-218'in iddiası doğrulanmış durumda
kalıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-054 — 🚨 `ShouldHaveOutputContaining` SSE (`/run`) yolunda `MessageDelta` parçalarını birleştirir

**Gerçek sonuç**
- Çıktı: `GECEN: cikti iceriyor.`, `MessageCompleted sayisi: 0`,
  `MessageDelta sayisi: 1`. Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-062 — `Tracon.Testing` yalnız `net10.0` hedefler — `net8.0` projeden kullanılamaz

**Gerçek sonuç**
- **Ortam uyarlaması**: kurulu SDK'nın `dotnet new console --framework`
  seçenekleri yalnız `net9.0`/`net10.0` sunuyor, `net8.0` artık desteklenen
  bir seçenek DEĞİL (SDK sürümüyle ilgili, kod kusuru değil) — bunun yerine
  `net9.0` kullanıldı; paketin `TargetFrameworks`'ü yalnız `net10.0`
  olduğu için `net9.0` de aynı derecede uyumsuz, iddia geçerliliğini
  korur. `dotnet add package Tracon.Testing` → `error NU1202: Package
  Tracon.Testing 0.0.0-preview.0.107 is not compatible with net9.0
  (.NETCoreApp,Version=v9.0). ... supports: net10.0`. `.csproj` kontrol
  edildi: `PackageReference` satırı EKLENMEDİ (CLI restore-zamanı
  uyumsuzluğu algılayıp değişikliği geri aldı). Sonraki `dotnet build`
  (paket asla eklenmediği için) beklendiği gibi başarılı — bu, dokümanın
  "paket eklenmeye ZORLANIRSA" koşuluyla çelişmiyor, zorlanmadı.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-063 — AOT publish denemesi trim/AOT analiz uyarısı üretir (koşumda ölçülecek)

**Gerçek sonuç**
- Yeni bağımsız bir konsol projesi (`~/tracon-manuel/aot-deneme`),
  `Tracon.Testing` eklendi, `.csproj`'a elle `<PublishAot>true</PublishAot>`
  eklendi. İlk denemede `Program.cs` yalnız `new FakeModelProvider()` ve
  `.Models.Count` kullanıyordu — `CallsTool`'un kendisi hiç çağrılmadığı
  için anlamlı olmayabilir diye, `Program.cs` doküman kodunun `.CallsTool
  ("get_order_status", new { orderId = "ORD-7" })` çağrısını (yansımalı
  `ToArguments` yolunu GERÇEKTEN tetikleyen) içerecek şekilde
  güncellendi. `dotnet publish -c Release -r osx-arm64 --self-contained`
  → **`0` uyarı** (`grep -E "warning IL[0-9]+|NETSDK1210"` boş döndü,
  hem minimal hem `CallsTool`'lu denemede). Yayımlanan AOT ikilisi
  doğrudan çalıştırıldı — ÇÖKMEDİ, `Metin: ` (boş, ayrı bir konu —
  `CallsTool` sonrası `EchoesUserMessage`/`RespondsWith` fallback'i
  olmadan ham istemci tool sonucunu metne çevirmiyor, MT-TEST-027'de
  zaten gözlenen davranış) yazdırdı. **Ölçülen sonuç, dokümanın kendi
  öngördüğü alternatif senaryodur**: sıfır uyarı çıktı — kod yorumu
  (`Tracon.Testing.csproj:21`) muhtemelen güncelliğini yitirmiş ya
  da trimmer, `IsAotCompatible`/`IsTrimmable` işaretlenmemiş bir
  paketin İÇİNİ derinlemesine analiz etmiyor (yalnız işaretli 8 paket
  derin analiz ediliyor — bkz. `MEMORY.md`'nin "sekiz paket uyumludur"
  notu), bu yüzden `Tracon.Testing`'in kendi reflection kullanımı
  hiç taranmıyor olabilir. Bu koşumda ne pozitif ne negatif "kusur"
  olarak işaretlenmiyor — dokümanın kendi talimatı gereği yalnız ölçüm
  kaydediliyor ve not düşülüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

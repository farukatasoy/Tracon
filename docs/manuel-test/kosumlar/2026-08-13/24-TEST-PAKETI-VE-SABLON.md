# 24 — Test Paketi ve Proje Şablonu (`TEST`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../24-TEST-PAKETI-VE-SABLON.md`](../../24-TEST-PAKETI-VE-SABLON.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-TEST-001 — Şablon paketten kurulur ve `dotnet new list`'te görünür

**Gerçek sonuç**
- Yerel NuGet feed'i `~/agentprism-local-feed` yeni bir `dotnet pack` ile
  tazelendi (`MSBUILDDISABLENODEREUSE=1`, sürüm `0.0.0-preview.0.107` —
  önceki feed içeriği eski, `0.78`'e kadardı). Şablon önce
  `dotnet new uninstall` ile temizlendi (zaten kurulu değildi, çıkış kodu
  `103` ile doğrulandı), sonra `dotnet new install ./src/AgentPrism.Templates`
  ile kuruldu. Çıktı: `"AgentPrism control plane (ASP.NET Core)" installed`,
  tablo `Short Name: agentprism-api`, `Language: [C#]`. `dotnet new list
  agentprism-api` aynı satırı tekrar gösterdi. Beklenenle eşleşiyor
  (kimlik `AgentPrism.Api.CSharp` CLI çıktısında ayrıca görünmüyor —
  `dotnet new list` varsayılan olarak yalnız görünen adı/kısa adı/dili
  gösteriyor, kimlik `template.json`'da doğrudan doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-002 — En yalın birleşim (`memory`+`openai`+`ui:false`) sıfır uyarıyla derlenir

**Gerçek sonuç**
- `dotnet new` çıkış kodu `0`. `dotnet build -c Release`: `Build succeeded.
  0 Warning(s), 0 Error(s)`. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-003 — En dolu birleşim (`sqlserver`+`azure`+`ui:true`) sıfır uyarıyla derlenir

**Gerçek sonuç**
- `dotnet new` çıkış kodu `0`. `dotnet build -c Release`: `Build succeeded.
  0 Warning(s), 0 Error(s)`. `.csproj` içinde `<PackageReference
  Include="AgentPrism.SqlServer" .../>` ve `<PackageReference
  Include="AgentPrism.Azure" .../>` bulundu. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-004 — Üretilen `appsettings.json` yalnız boş placeholder taşır, hiçbir dosyada `secret` görünümlü değer yok

**Gerçek sonuç**
- Tarama **"temiz"** yazdı. `appsettings.json` içeriği:
  `SqlServer.ConnectionString: ""`, `Providers.AzureOpenAI.Endpoint: ""`,
  `Providers.AzureOpenAI.ApiKey: ""` — üçü de boş dize. Beklenenle
  birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-005 — Üretilen `Program.cs` hiçbir sağlayıcı için sabit bir model adı taşımaz

**Gerçek sonuç**
- Dört sağlayıcının (`openai`, `anthropic`, `google`, `azure`) hepsi için
  `grep -c MODEL_ADINI_BURAYA_YAZIN` → `1`, bilinen model öneki taraması
  → `"temiz"`. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-006 — `-n` ile yeniden adlandırma: `AgentPrism.Starter` dizesi hiçbir dosyada/dosya adında kalmaz

**Gerçek sonuç**
- `Benim.Agent.csproj` var, `AgentPrism.Starter` taraması "temiz". `dotnet
  build -c Release`: `Build succeeded. 0 Warning(s), 0 Error(s)`.
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-007 — Varsayılan (`memory`) birleşim kurulumsuz `dotnet run` ile ayağa kalkar

**Gerçek sonuç**
- Hiçbir `user-secrets` ayarlanmadan `dotnet run -c Release` başlatıldı,
  bağlantı hatası olmadan ayağa kalktı. `GET /agentprism/api/agents` →
  `HTTP/1.1 200 OK`, gövde `[{"name":"support","displayName":"Destek
  Asistani",...}]` — tek agent. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-008 — `postgres`/`openai` seçiminde ayrı bir paket referansı EKLENMEZ (zaten meta pakette)

**Gerçek sonuç**
- **Doküman düzeltmesi**: "`diff` boş döner" iddiası, case'in KENDİ
  girilecek-veri adımlarıyla (`-n Meta.Kontrol` VS `-n Meta.Kontrol2`,
  İKİ FARKLI proje adı) çelişiyor — farklı proje adı `RootNamespace`'i VE
  rastgele üretilen `UserSecretsId` GUID'ini kaçınılmaz olarak
  değiştiriyor, `diff` bu iki satırda fark gösteriyor (doğrulandı).
  Asıl doğrulanmak istenen özdeş iddia bu değil: her iki `.csproj`
  dosyası da yalnız TEK bir `<PackageReference Include="AgentPrism"
  Version="0.0.0-preview.0.107" />` satırı taşıyor, `postgres` seçmek
  EK bir `PackageReference` satırı EKLEMİYOR — bu, doğrulanmak istenen
  gerçek iddia, ve doğru. Kod kusuru değil, doküman ifadesi düzeltmeli
  ("`diff` yalnız `PackageReference` satırlarında boş döner" olmalı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-009 — `--skip-restore` restore adımını atlar

**Gerçek sonuç**
- İlk projede `obj/` dolu (`project.assets.json` dahil restore çıktıları).
  İkinci projede (`--skip-restore true`) `obj/` klasörü **hiç yok** (`ls`:
  "No such file or directory"). Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-010 — `--AgentPrismVersion` belirli bir sürüme sabitler

**Gerçek sonuç**
- `<PackageReference Include="AgentPrism" Version="0.0.0-preview.0.107" />`
  — tam olarak `$SURUM` değeri, `*-*` değil. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-011 — Tanınmayan bir `--persistence` değeri reddedilir

**Gerçek sonuç**
- Çıkış kodu `127` (sıfırdan farklı). Hata mesajı: `'mysql' is not a
  valid value for --persistence. The possible values are: memory,
  postgres, sqlite, sqlserver` — dört seçenek de listelendi. `$TMP/g`
  dizini hiç oluşturulmadı ("No such file or directory"). Beklenenle
  eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-012 — `-h` çıktısında üç bayrak görünür, `AgentPrismVersion` gizlidir

**Gerçek sonuç**
- Çıktıda dört bayrağın hepsi (`-p/--persistence`, `-pr/--provider`, `-ui`,
  `-sr/--skip-restore`), kısa açıklamalar ve varsayılan değerlerle
  listelendi. `--AgentPrismVersion` hiçbir yerde görünmedi. Beklenenle
  eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-013 — Şablon paketi derlenmez; üretilen proje `AgentPrism.Templates`'e hiç referans vermez

**Gerçek sonuç**
- `unzip -l` taraması: hiçbir `.dll` yok ("dll yok -- beklenen").
  Üretilen projede `AgentPrism.Templates` dizesi taraması "temiz".
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — `FakeModelProvider` davranışları (Faz 39a)

> Aşağıdaki her case, "Koşmadan önce" adım 4'te kurulan
> `~/agentprism-manuel/test-paketi` konsol projesinin `Program.cs`'ini
> **tamamen** değiştirir. Metin eşleşmesi burada izlek C kuralı gereği
> serbesttir — `FakeModelProvider` deterministiktir.

---

## MT-TEST-020 — Varsayılan kurulum sabit `"fake response"` döner; katalog tek bir `fake-model` girdisi taşır

**Gerçek sonuç**
- **Doküman düzeltmesi**: verilen kod aynen yapıştırılınca `CS0246:
  'ModelBinding' bulunamadı` ile derlenmedi — `ModelBinding` tipi
  `AgentPrism` ad alanındadır, doküman yalnız `using AgentPrism.Testing;`
  yazmış, `using AgentPrism;` eksik. `using AgentPrism;` eklenince: çıktı
  `Models.Count: 1`, `Model adi: fake-model`, `Yanit: fake response` —
  beklenenle birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-021 — `EchoesUserMessage()` son kullanıcı mesajını `Echo: ` öneki ile yankılar

**Gerçek sonuç**
- Çıktı: `Echo: ORD-7 nerede` / `Echo: ikinci soru`. Beklenenle birebir
  eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-022 — `RespondsWith(...)` yanıtları sırayla tüketir

**Gerçek sonuç**
- (Aynı `using AgentPrism;` eksikliği MT-TEST-020'de kaydedildi, burada
  da tekrar eklendi.) Çıktı: `ilk yanit` / `ikinci yanit`. Beklenenle
  birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-023 — Kuyruk tükendikten sonra `EchoesUserMessage()` fallback'i devreye girer

**Gerçek sonuç**
- Çıktı: `1: ilk`, `2: Echo: sonraki mesaj`, `3: Echo: ucuncu mesaj`.
  Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-024 — Fallback tanımlanmamışsa kuyruk tükendikten sonra sabit `"fake response"` tekrar tekrar döner

**Gerçek sonuç**
- Çıktı: `1: tek yanit`, `2: fake response`, `3: fake response`.
  Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-025 — `CallsTool(...)` bir `FunctionCallContent` üretir; anonim tip argümanları yansımayla sözlüğe kopyalanır

**Gerçek sonuç**
- Çıktı: `Tool: get_order_status`, `orderId: ORD-7`. Beklenenle birebir
  eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-026 — `ForModel(...)` farklı modeller için bağımsız kuyruk tutar

**Gerçek sonuç**
- Çıktı: `router: router yaniti`, `researcher: researcher yaniti`,
  `Models.Count: 2`. Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-027 — `EchoesLastToolResult(prefix)` gerçek tool-çağrı boru hattı üzerinden son tool sonucunu yankılar

**Gerçek sonuç**
- `ModelProviderRegistry` üzerinden: çıktı tam olarak `Sonuc: hazirlaniyor
  (ORD-7)`. Doğrudan `FakeModelProvider.CreateChatClient(binding)` ile
  (defter olmadan) aynı `ChatOptions` verilince: `Contents:
  FunctionCallContent` (tek içerik türü), `Text: ''` (boş) — tool hiç
  çalıştırılmadı. Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-028 — `RespondsWith(text, inputTokens, outputTokens)` bildirilen kullanım gerçek boru hattında `RunRecord.Usage`'a yansır

**Gerçek sonuç**
- Çıktı: `InputTokens: 42`, `OutputTokens: 17`. Beklenenle birebir
  eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-029 — `Requests` listesi gönderilen mesaj geçmişini ve `ChatOptions.Tools`'u kaydeder

**Gerçek sonuç**
- Çıktı: `Requests.Count: 2`, `Ilk istek son mesaj: birinci`, `Ilk istek
  Tools.Count: 1`, `Ikinci istek Options: null`. Beklenenle birebir
  eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-030 — `Models` kataloğunda olmayan bir model adı agent kaydını ENGELLEMEZ

**Gerçek sonuç**
- Hiçbir istisna fırlatılmadı, `run.ShouldHaveCompleted()` geçti, "Basarili
  -- katalogda olmayan model kaydi engellemedi." yazdırıldı. Beklenenle
  eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — `AgentPrismTestHost` (Faz 39b)

---

## MT-TEST-040 — `StartAsync()` hiçbir yapılandırma olmadan ayağa kalkar, `/agentprism/api/meta` `200` döner

**Gerçek sonuç**
- `Durum: 200`. Gövde `"version":"0.0.0-preview.0.107","prefix":
  "/agentprism",...` içeriyor (ayrıca `authentication`/`storage`/`roles`
  alt nesneleri de var, dokümanın belirttiği asgari şart karşılandı).
  Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-041 — Özel `Prefix` yalnız o önekten yanıt verir, varsayılan önek artık yanıt vermez

**Gerçek sonuç**
- `/panel: 200`, `/agentprism: 404`. Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-042 — `DisposeAsync()` sonrası `Client` kullanılırsa `ObjectDisposedException` fırlatılır

**Gerçek sonuç**
- Çıktı: `Beklenen: ObjectDisposedException firlatildi`. Beklenenle
  birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-043 — `RunAsync` var olmayan bir agent adıyla çağrılırsa `AgentPrismAssertionException` fırlatılır

**Gerçek sonuç**
- Yakalandı: `'olmayan-agent' calistirilamadi. Beklenen durum kodu
  basarili, bulunan '404': {...,"title":"Agent bulunamadi","status":404,
  "detail":"'olmayan-agent' adinda bir agent yok."}`. Beklenenle
  eşleşiyor.

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

## MT-TEST-045 — `ConfigureServices`, `AddAgentPrism()` çağrısından ÖNCE çalışır

**Gerçek sonuç**
- Çıktı: `Sira: ConfigureServices -> ConfigureAgentPrism`, `Deger:
  ozel-deger`. Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — `RunAssertions` — geçen ve düşen yol (Faz 39c)

---

## MT-TEST-050 — `ShouldHaveCompleted()` geçer; başarısız bir çalıştırmada beklenen/bulunan durumu yazan mesajla düşer

**Gerçek sonuç**
- Çıktı: `GECEN YOL: basarili.` sonra `DUSEN YOL mesaji: Calistirmanin
  durumu 'Failed' olmasi beklenirdi ama 'Completed' bulundu.` Beklenenle
  birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-051 — `ShouldHaveFailedWith(errorType)` kararlı bir hata tipini doğrular

**Gerçek sonuç**
- Çıktı: `GECEN YOL: dogru hata tipi.` sonra `DUSEN YOL mesaji: Hata
  tipinin 'baska_bir_tip' olmasi beklenirdi ama 'content_blocked'
  bulundu.` Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-052 — `ShouldHaveCalledTool(name, times:)` sayı uyuşmazsa beklenen/bulunan sayıyı yazan mesajla düşer

**Gerçek sonuç**
- Çıktı: `GECEN YOL: tam olarak 2 cagri.` sonra `DUSEN YOL mesaji:
  'get_order_status' tool'unun 5 kez cagrilmasi beklenirdi ama 2 kez
  cagrildi.` Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-053 — `ShouldNotHaveCalledTool(name)` çağrılmış bir tool için düşer

**Gerçek sonuç**
- Çıktı: `GECEN YOL: cancel_order hic cagrilmadi.` sonra `DUSEN YOL
  mesaji: 'get_order_status' tool'unun hic cagrilmamasi beklenirdi ama
  1 kez cagrildi.` Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-054 — 🚨 `ShouldHaveOutputContaining` SSE (`/run`) yolunda `MessageDelta` parçalarını birleştirir

**Gerçek sonuç**
- Çıktı: `GECEN: cikti iceriyor.`, `MessageCompleted sayisi: 0`,
  `MessageDelta sayisi: 1`. Beklenenle birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-055 — Zincirleme iddialar art arda çalışır

**Gerçek sonuç**
- README'deki örnek birebir çalıştırıldı, hiçbir aşamada istisna
  atılmadı. Çıktı: `Zincir basariyla tamamlandi -- hicbir asamada istisna
  atilmadi.` Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Paket kalitesi ve sınırlar (K-007/K-008/K-270)

---

## MT-TEST-060 — Paketlenmiş `.nuspec` hiçbir test çerçevesi bağımlılığı taşımaz

**Gerçek sonuç**
- `<dependencies>` bloğu tam olarak üç bağımlılık listeliyor:
  `AgentPrism.AspNetCore`, `AgentPrism.Core`, `Microsoft.AspNetCore.TestHost`
  (sürüm `0.0.0-preview.0.107`/`10.0.10`). Test çerçevesi taraması bu blok
  içinde "temiz". (Not: `.nuspec`'in `<description>` alanı — bağımlılık
  bloğunun DIŞINDA — paketin "xunit, NUnit, MSTest'e bağlı değildir"
  şeklindeki kendi açıklamasında bu isimleri metin olarak geçiriyor; bu
  bir bağımlılık değil, tarama doğru şekilde yalnız `<dependencies>`
  bloğuna uygulandı.) Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-061 — Meta paket (`AgentPrism`) `AgentPrism.Testing`'e referans VERMEZ

**Gerçek sonuç**
- `.nuspec` taraması "temiz". Kaynak `.csproj` taraması `0`. Beklenenle
  birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-062 — `AgentPrism.Testing` yalnız `net10.0` hedefler — `net8.0` projeden kullanılamaz

**Gerçek sonuç**
- **Ortam uyarlaması**: kurulu SDK'nın `dotnet new console --framework`
  seçenekleri yalnız `net9.0`/`net10.0` sunuyor, `net8.0` artık desteklenen
  bir seçenek DEĞİL (SDK sürümüyle ilgili, kod kusuru değil) — bunun yerine
  `net9.0` kullanıldı; paketin `TargetFrameworks`'ü yalnız `net10.0`
  olduğu için `net9.0` de aynı derecede uyumsuz, iddia geçerliliğini
  korur. `dotnet add package AgentPrism.Testing` → `error NU1202: Package
  AgentPrism.Testing 0.0.0-preview.0.107 is not compatible with net9.0
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
- Yeni bağımsız bir konsol projesi (`~/agentprism-manuel/aot-deneme`),
  `AgentPrism.Testing` eklendi, `.csproj`'a elle `<PublishAot>true</PublishAot>`
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
  (`AgentPrism.Testing.csproj:21`) muhtemelen güncelliğini yitirmiş ya
  da trimmer, `IsAotCompatible`/`IsTrimmable` işaretlenmemiş bir
  paketin İÇİNİ derinlemesine analiz etmiyor (yalnız işaretli 8 paket
  derin analiz ediliyor — bkz. `MEMORY.md`'nin "sekiz paket uyumludur"
  notu), bu yüzden `AgentPrism.Testing`'in kendi reflection kullanımı
  hiç taranmıyor olabilir. Bu koşumda ne pozitif ne negatif "kusur"
  olarak işaretlenmiyor — dokümanın kendi talimatı gereği yalnız ölçüm
  kaydediliyor ve not düşülüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TEST-064 — Depo dışı tüketici: gerçek model çağırmadan uçtan uca bir agent testi

**Gerçek sonuç**
- Tamamen yeni, depo dışı bir dizinde (`~/agentprism-manuel/depo-disi
  -tuketici`) sıfırdan proje oluşturuldu, `AgentPrism.Testing` eklendi,
  MT-TEST-055'in kodu birebir yapıştırıldı. `dotnet run -c Release` →
  `Zincir basariyla tamamlandi -- hicbir asamada istisna atilmadi.`
  Ağ trafiği iddiası kod okumasıyla da doğrulandı: `FakeChatClient.cs`
  içinde `HttpClient` veya herhangi bir `Http.` kullanımı YOK — sağlayıcı
  yapısal olarak ağa çıkamaz. Hiçbir OpenAI/Anthropic/vb. `secret`
  ayarlanmadan (bu proje `samples/AgentPrism.Api`'nin `user-secrets`
  deposuna hiç dokunmuyor) program başarıyla bitti. Beklenenle eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

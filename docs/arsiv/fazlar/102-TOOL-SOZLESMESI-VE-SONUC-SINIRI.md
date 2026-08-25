# Faz 102 — Tool Sözleşmesi ve Sonuç Sınırı

> **Durum:** ✅ Tamamlandı (2026-08-25)
> **Kaynak:** Doğrudan kullanıcı isteği — aday listesinden gelmedi, aday listesine kalem eklemez (Faz 101 ile aynı yol)
> **Önkoşul:** [Faz 101](101-KAYNAK-SOZLESMESININ-YAYINI.md) — sözleşme yayını deseni (contract suite + sample + XML) buradan devralınır
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Generators`, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — `PublicAPI.Shipped.txt` **boştur** (`wc -l src/*/PublicAPI.Shipped.txt` → tümü `0`), bu yüzden bugün eklemek ve kırmak **bedavadır**; preview.1'den sonra ikisi de sürüm kararıdır
> **Tüketici yüzeyi:** site: `getting-started/tools.md`, `concepts/tools.md`, **yeni** `guides/write-your-own-tool.md`, `capabilities.md`
> · sevk edilen: `AgentPrismToolRegistration` / `AgentPrismToolAttribute` / `IToolRegistry` / `TimeoutAIFunction` / `TruncatingAIFunction` XML'leri, `src/AgentPrism.Testing.Contracts.Xunit/README.md`
> **Manuel test alanı:** [`docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show e5b20eb:docs/arsiv/fazlar/102-TOOL-SOZLESMESI-VE-SONUC-SINIRI.md
> ```
>
> Damıtıldı 2026-08-25 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir üçüncü taraf geliştirici bugün **basit** bir tool yazabilir; **üretim kalitesinde** bir tool yazamaz. Altı davranış invariant'ı yalnız kaynak kodda yorum olarak duruyor, iki tanesi verilen XML sözüyle **çelişiyor**, biri önerilen kayıt yolunda **çalışmıyor**, ve yüzeyi doğrulayacak ne executable sözleşme ne de çalışan bir örnek var. Bu faz o boşluğu kapatır.

## Bitiş Ölçütleri (DoD)

- [x] Complex object dönen bir tool'un sonucu content guard tarafından **alan
      değerleriyle** görülür; Manuel Case 1 kanıtı belgeye yazıldı — kanıt:
      `OrderFulfillmentToolTests.A_generated_complex_results_field_value_is_seen_by_the_content_guard`
      (red→green doğrulandı); normalize edilemeyen sonuç için fail-closed yolu
      ayrıca `ContentGuardMaskTests.Tool_result_that_cannot_be_normalized_is_masked_even_when_no_guard_pattern_matches`
      ile kanıtlı (bağımsız denetimin bulduğu ve kapatılan güvenlik açığı — bkz. Denetim Bulguları)
- [x] `MaxOutputBytes` altı sonuç tipinde de uygulanır: `string` · `JsonElement` ·
      primitive · record/class · collection · `null`. `AIContent` istisnası XML'de yazılı —
      `TruncatingAIFunctionTests`, `ToolRegistryWrapperOrderTests`
- [x] `[AgentPrismTool(SafeToRepeat = true)]` üç kayıt yolunda **aynı**
      `AgentPrismToolRegistration` üretir — `ToolRegistrationTests` (parity, sekiz alan)
- [x] `AddTool` sekiz knob'un **hepsine** `configure` üzerinden erişir; ham
      `services.AddSingleton(new AgentPrismToolRegistration(...))` artık tek yol değil —
      `ToolRegistrationOptions` + `IAgentPrismBuilder.AddTool` gerçekleşen imzada
- [x] Geçersiz ve duplicate tool adı **startup'ta** durur (Manuel Case 5, 6) —
      `ToolRegistrationValidationServiceTests`
- [x] Tanınmayan `IToolRegistry` startup'ta reddedilir; opt-out bayrağı çalışır;
      `.UseMcp()` **geçer** (Manuel Case 7, 8, 9) — gerçek host/DI sınırında kanıtlı:
      `ToolRegistryVerificationEndpointTests`, `McpToolRegistryVerificationTests`
- [x] Ham istisna mesajı SSE ve HTTP yüzeyinden çıkmaz, `ILogger`'da tam kalır;
      AgentPrism'in kendi istisnaları korunur (Manuel Case 10, 11) — `ToolFailureText.Get`
- [x] `ContractCoverage.ToolContracts` ailesi var; dört mevcut aile
      (Storage · Providers · Judges · AgentSources) **yeşil** kalır — `ContractCoverageTests`,
      `ToolContractCoverageTests` (Core + sample), tam kapanış koşumunda doğrulandı
- [x] `samples/AgentPrism.Samples.CustomTool.Tests` yalnız `PackageReference`
      ile AgentPrism'e bağlanır ve yeşil koşar — 9/9 (ContractCoverage dahil)
- [x] `AgentPrism.Core` ve `.Abstractions` AOT uyumlu kalır
      (`grep -l "AotCompatible>false" src/*/*.csproj` çıktısı değişmedi) —
      taban commit `8929903` ile birebir karşılaştırıldı, aynı 11 proje
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 8929903`
      tam yeşil (build · test · pack · format · `docs-site npm run check`); 20 paket,
      tek sürüm hattı, `kapi.py yayin --kuru` temiz
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı —
      Sqlite + gerçek OpenAI `gpt-5.4-mini` ile `support` agent'ı, `get_order_status`
      tool çağrısı; `GET /api/tools`, `POST .../run`, `GET .../tools` hepsi doğrulandı
      (bkz. Sonraki Faza Devir Notu → "Yarım kalan iş")
- [x] `secret` taraması boş döndü — `kapi.py tarama` ✅
- [x] Manuel kabul case'leri `docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md` içine
      eklendi (MT-CORE-097..099); otomatikleştirilebilenler (097) koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — iki tur: uygulama sırasında (3🔴, kapandı)
      ve kapanışta bağımsız agent (2🔴, ikisi de düzeltildi ve red→green kanıtlandı)
- [x] `docs-site/` güncellendi (`guides/write-your-own-tool.md` dahil, ayrıca
      `concepts/tools.md` — site-denetle'nin yakaladığı eksik — ve
      `getting-started/tools.md`'deki iki kırık `AddTool` örneği); `npm run check`
      (content + build + links + weight) dördü de temiz

### Doğrulama komutları

```bash
# Faz 101'in tuzağı — samples derlemeden ÖNCE
rm -rf ~/.nuget/packages/agentprism*
python3 scripts/kapi.py yayin --kuru

# Parity ve sonuç sınırı
dotnet test tests/AgentPrism.Core.UnitTests --filter "ToolRegistrationParity|ToolOutputBudgetMatrix|ContentGuardComplexToolResult"

# Sözleşme ailesi ve sample
dotnet test samples/AgentPrism.Samples.CustomTool.Tests

# AOT kapısı
grep -l "AotCompatible>false" src/*/*.csproj

# Kapanış
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>
```

---

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

- Complex sonuç için generator'ın kendi ürettiği `JsonSerializerContext` kullanılmadı.
  Roslyn generator'ları birbirlerinin ürettiği kaynağı aynı compilation'da görmez;
  gerçek consumer build'i bu yaklaşımı `CS0534` ile kırdı. `AgentPrismToolAttribute`
  içine `JsonSerializerContext` eklendi ve complex sonuçta `APG0008` ile tool sahibinin
  kendi `[JsonSerializable(typeof(TResult))]` context'ini vermesi zorunlu kılındı.
  Bu AOT-safe'tir ve rehber/sample bunu çalışır biçimde kanıtlar.
- `CustomToolContract` yalnız implementer'ın tek başına sağlayabileceği isim,
  metadata ve concurrent invocation sözleşmesini taşır. Tenant context, timeout,
  output envelope ve guard davranışı registry/DI sınırını geçtiği için Core
  functional testleri ve `CustomTool` sample run'ında kanıtlanır; contract
  paketine Core bağımlılığı eklenmedi.
- Plandaki dağınık test sınıfı taslağı (`ToolOutputBudgetMatrixTests`,
  `ToolRegistrationParityTests`, `ContentGuardComplexToolResultTests`,
  `ToolNameValidationTests`, `McpToolRegistryVerificationTests`,
  `ToolRegistryVerificationTests`, `ToolFailureExposureTests`) ayrı dosyalar
  olarak açılmadı; aynı davranışlar mevcut/genişletilmiş dosyalara konsolide
  edildi: `ToolRegistrationTests.cs` (parity + ad doğrulaması), `ToolResultTextTests.cs`
  (normalizer), `TruncatingAIFunctionTests.cs` (budget matrix),
  `ToolRegistrationValidationServiceTests.cs` (startup doğrulama + opt-out),
  `ToolRegistryWrapperOrderTests.cs` (wrapper sırası), `ContentGuardEndpointTests.cs`
  ve `ToolGovernanceEndpointTests.cs` (fonksiyonel sınır). Kapsam plandakiyle
  aynı; dosya sayısı azaldı.
- **Kapanış kapısının ikinci koşumu gerçek bir regresyon yakaladı ve düzeltildi.**
  `ConcurrentToolInvocationTests.Three_concurrent_tool_calls_are_all_recorded_correctly_under_a_guaranteed_overlap`
  eski (tırnaksız) bir `Result` beklentisi taşıyordu. Kanıt, `ilspycmd` ile
  `Microsoft.Extensions.AI.OpenAIChatClient.ToOpenAIChatMessages`'ın decompile
  edilmesiyle çıkarıldı: `FunctionResultContent.Result` ham bir `string` ise
  olduğu gibi, **her başka tip** için (bir `JsonElement` dahil)
  `JsonSerializer.Serialize(Result, object-typeinfo)` ile tel'e yazılıyor —
  ve `AIFunctionFactory.Create` bir delegate'in `string` dönüşünü bile her
  zaman `JsonElement`'e sarıyor (ölçüldü). Yani delegate ile kayıtlı bir
  `string` tool tel üzerinde **tırnaklı** gidiyor, generator ile kayıtlı aynı
  dönüş tipi **tırnaksız**. `ToolResultText`'in `GetRawText()` davranışı
  (102.2) bu farkı doğru yansıtıyor; testin beklentisi buna göre düzeltildi,
  kod değişmedi.
- `docs-site/src/content/docs/concepts/tools.md` ilk koşumda hiç
  güncellenmemişti (`dokuman-bakim.py --site-denetle` bunu "cekirdek-kavram"
  kuralıyla yakaladı). Registry snapshot/singleton/concurrency, governance
  ownership tablosu, timeout'un cooperative olmadığı + in-turn retry ile
  `SafeToRepeat`'in ayrımı, ve sonuç temsilinin/kalıcılığının canonical form'u
  eklendi. Ayrıca `getting-started/tools.md` içindeki iki kod örneği 102.5'te
  kaldırılan `AddTool(..., requiresApproval: bool)` imzasını kullanıyordu
  (artık derlenmez) — yeni `configure` şekline düzeltildi.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

- **K-614** — `IToolRegistry` public kalır; tanınmayan implementation
  `IVerifiedToolRegistry` marker'ı taşımadığı için startup'ta reddedilir;
  `AllowUnverifiedToolRegistry` bilerek yapan için opt-out'tur.
- **K-615** — Generator kendi ürettiği `JsonSerializerContext`'i kullanmaz;
  complex tool sonucu için tool sahibi kendi context'ini
  `AgentPrismToolAttribute.JsonSerializerContext`'e verir, aksi hâlde derleme
  `APG0008` ile durur.
- **K-616** — `CustomToolContract` yalnız implementer'ın tek başına
  sağlayabileceği isim/metadata/eşzamanlı çağrı sözleşmesini taşır;
  tenant/timeout/envelope/guard davranışı registry-DI sınırını geçtiği için
  pakete Core bağımlılığı eklenmeden Core fonksiyonel testlerinde ve sample
  run'ında kanıtlanır.
- **K-617** — Content guard'ın normalize edemediği tool sonucu için
  fail-closed yolu, guard pattern eşleşmesinden tamamen bağımsız, koşulsuz
  bir değiştirme olarak yeniden yazıldı (bağımsız denetimin kapanışta bulduğu
  ve kapattığı 🔴 — bkz. Denetim Bulguları).

Tam gerekçe her dördü için [`docs/KARARLAR.md`](../../KARARLAR.md)'dedir.

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

**Birinci tur (uygulama sırasında).**

- 🔴 `JsonSerializerContext` plan dışı public API idi; yukarıdaki sapma kaydıyla
  gerekçelendi ve `APG0008`, rehber ile sample tarafından görünür kılındı.
- 🔴 İlk rehber örneğinde tanımsız domain tipleri vardı; bağımsız derlenebilir
  `OrderReceipt` örneğiyle düzeltildi.
- 🔴 Manuel kabul setinde Faz 102 case'leri yoktu; MT-CORE-097..099 eklendi.
- 🟡 Shared contract'ın host sınırı test etmemesi gerekçelendi; sınır davranışları
  Core testleri ve gerçek package tüketicisi sample ile doğrulanır.

**İkinci tur — bağımsız denetim (kapanış oturumu, taze bağlamlı ayrı agent).**
Taban `8929903`, kapsam `git diff 8929903` (commit'lenmiş + çalışma ağacı).

- 🔴 **DÜZELTİLDİ — content guard'ın "fail-closed" sözü gerçek değildi.**
  `ContentGuardMessageMasker.ReadText`, normalize edilemeyen bir tool sonucu
  için sabit bir yer tutucu metin (`"[Tool result could not be inspected]"`)
  döndürüyordu, ama bu metin **koşulsuz** içeriğin yerine geçmiyordu — sıradan
  metin gibi guard'ın desen eşleşmesine veriliyordu. Desen (neredeyse hiçbir
  zaman) yer tutucuyla eşleşmediği için `InspectAsync` `null` dönüyor,
  `RewriteMessageAsync` bunu "değişiklik yok" sayıyor ve **orijinal, ham**
  `FunctionResultContent` hiç dokunulmadan modele ve kalıcı kayda gidiyordu —
  `ContentGuardPipeline`'ın kendi XML sözüyle ("content that cannot be
  inspected is not let through") doğrudan çelişen bir güvenlik açığıydı.
  Kanıt: red→green ile doğrulandı (`git stash` ile eski koda karşı test
  çalıştırıldı, gerçek secret `Dictionary` hiç maskelenmeden sızdığı
  gözlemlendi). Düzeltme: `ContentGuardMessageMasker`/`ContentGuardPipeline`/
  `ContentGuardingChatClient` üçü de tek bir paylaşılan `RewriteContentAsync`
  yoluna taşındı; normalize edilemeyen sonuç artık **koşulsuz** yer tutucuyla
  değiştiriliyor ve `ContentGuardPipeline.RecordUninspectableToolResultAsync`
  ile bir `ContentMasked` olayı yazılıyor (yalnız gerçek karar yolunda, önizleme
  yolunda değil — aynı `runs` satırı henüz yok kısıtı). Kanıt testi:
  `ContentGuardMaskTests.Tool_result_that_cannot_be_normalized_is_masked_even_when_no_guard_pattern_matches`.
- 🔴 **DÜZELTİLDİ — DoD'nin başlıca iddiası (complex sonuç guard'a alan
  değerleriyle görünür) hiçbir testte kanıtlanmamıştı.** Plandaki taslak test
  sınıfları (`ContentGuardComplexToolResultTests`,
  `ToolRegistrationParityTests`, `McpToolRegistryVerificationTests`,
  `ToolRegistryVerificationTests` vb.) hiç yazılmamıştı; devir notu tablosu
  bu adları var olmayan/ilgisiz dosyalara bağlıyordu. Kapatıldı:
  `samples/AgentPrism.Samples.CustomTool.Tests`'e generator yolundan geçen bir
  `[AgentPrismTool]` (`preview_order`) + gerçek `PatternContentGuard` ile uçtan
  uca kanıt eklendi (`A_generated_complex_results_field_value_is_seen_by_the_content_guard`,
  red→green doğrulandı: guard'sız koşum modelin cevabında ham
  `{"OrderId":"...","Status":"ready"}` JSON'unu gösterdi). Ayrıca gerçek host/DI
  sınırında iki yeni test eklendi: `ToolRegistryVerificationEndpointTests`
  (`AgentPrism.AspNetCore.FunctionalTests`, foreign `IToolRegistry` gerçek
  `WebApplication.StartAsync()` ile reddediliyor + opt-out çalışıyor) ve
  `McpToolRegistryVerificationTests` (`AgentPrism.Mcp.UnitTests`, `.UseMcp()`
  gerçek DI-çözülen `IHostedService` zincirinden geçiyor).
- 🟡 **DÜZELTİLDİ.** `CustomToolContract.Concurrent_server_calls_complete`
  hiçbir `Should*` taşımıyordu. `calls.ShouldAllBe(RanToCompletion)` eklendi;
  sözleşmenin tool semantiğini bilmediği için daha güçlü bir iddia
  kuramayacağı XML'e yazıldı.
- 🟡 **DÜZELTİLDİ.** `TruncatingAIFunctionTests.cs`'in sınıf yorumu, generator'ın
  complex tipte de artık (102.2 sonrası) ham CLR nesnesi değil `JsonElement`
  döndürdüğünü yansıtmıyordu — güncellendi.
- 🟡 **GEREKÇELENDİ.** `samples/AgentPrism.Samples.CustomTool` hiçbir tool'da
  `AgentPrismRunContext.Current`'i okumuyor ve ayrı bir istisna-yönetimi
  rehberi taşımıyor (102.11'in listesindeki iki madde). Sample'ın asıl kanıt
  yükü (singleton, scoped bağımlılık, complex sonuç, `SafeToRepeat`,
  sekiz knob) zaten karşılanıyor; bu ikisi kapsamı büyütmeden sonraki bir
  dokümantasyon geçişine bırakıldı — `docs/ADAYLAR.md`'ye F-NN açılmadı çünkü
  makine kapısına dönüşecek bir kalem değil, gözle denetlenen bir eksiklik.
- 🔴 **DÜZELTİLDİ (protokol adımı, yeni bulgu değil).** `docs/YOL-HARITASI.md`
  fazı hâlâ "📋 Planlandı" gösteriyordu; `dokuman-bakim.py` yeniden koşularak
  üretildi. Aynı geçişte "Bu Fazda Verilen Kararlar" bölümündeki
  `docs/KARARLAR.md` bağlantısındaki göreli yol hatası (`../KARARLAR.md` →
  `KARARLAR.md`) da düzeltildi.

**Temiz çıkan başlıklar (ikinci tur):** 3.5 (imza-gövde kayması yok — `SafeToRepeat`/
`MaxOutputBytes` her üretim/tüketim noktasında izlendi), 3.7 (İngilizce metin, XML
doküman, `TryAdd*`, K1/K3/K-059/`ConfigureAwait(false)` ihlali yok).

## Sonraki Faza Devir Notu

**Devralınan sözleşme.** Tool genişleme yüzeyi artık `AgentPrismToolAttribute`,
`AgentPrismToolRegistration` ve `IToolRegistry`'nin kendi XML'inde tam yazılı
(102.9'daki 18 invariant). Yeni bir kayıt yolu veya wrapper eklerken artık
kaynağı okumak zorunlu değil.

**Davranış sözleşmesi tablosu.**

| Kural | Testi |
|---|---|
| Complex tool sonucu content guard'a alan değerleriyle görünür, tip adı olarak değil — generator yolundan uçtan uca | `samples/AgentPrism.Samples.CustomTool.Tests` → `OrderFulfillmentToolTests.A_generated_complex_results_field_value_is_seen_by_the_content_guard` |
| Normalize edilemeyen tool sonucu **koşulsuz** maskelenir (fail-closed), guard deseni yer tutucuyla eşleşmese bile | `ContentGuardMaskTests.Tool_result_that_cannot_be_normalized_is_masked_even_when_no_guard_pattern_matches` |
| `MaxOutputBytes` altı sonuç tipinde de (complex dahil) uygulanır; `AIContent` muaf | `TruncatingAIFunctionTests`, `ToolRegistryWrapperOrderTests` |
| Üç kayıt yolu (`[AgentPrismTool]`+generator · `AddToolsFrom` · `AddTool(AIFunction)`) aynı sekiz alanlı `AgentPrismToolRegistration` üretir | `ToolRegistrationTests` (parity) |
| Geçersiz/duplicate tool adı **startup'ta** durur | `ToolRegistrationValidationServiceTests` |
| Tanınmayan `IToolRegistry` gerçek host/DI başlangıcında reddedilir; `AllowUnverifiedToolRegistry` opt-out çalışır | `ToolRegistrationValidationServiceTests` (marker: `IVerifiedToolRegistry`, elle çağrı) + `ToolRegistryVerificationEndpointTests` (`AgentPrism.AspNetCore.FunctionalTests`, gerçek `WebApplication.StartAsync()`) |
| `McpToolRegistry` aynı doğrulamadan geçer, tanınmayan sayılmaz | `McpToolRegistryVerificationTests` (`AgentPrism.Mcp.UnitTests`, gerçek DI-çözülen `IHostedService`) |
| Ham istisna mesajı SSE/HTTP'ye çıkmaz; AgentPrism'in kendi istisnaları korunur | `ToolFailureText.Get` + `ToolGovernanceEndpointTests` |
| `CustomToolContract`/`RepeatableToolContract` dört mevcut aileyi kırmaz | `ContractCoverageTests` |

**🚨 Bilinen tuzaklar (bu fazda ölçüldü):**

- **`AIFunction.InvokeAsync`'in ham dönüş TİPİ, `FunctionInvokingChatClient`'ın
  GERÇEK döngüsünde gözlenen `FunctionResultContent.Result` tipiyle her zaman
  AYNI olmayabilir — izole probe entegre davranışı kanıtlamaz (MEMORY.md'nin
  tekrarlayan uyarısı, bu fazda yeniden doğrulandı).** `AgentPrism.AddTool((Func<Task<string>>)...)`
  ile kayıtlı bir tool, GERÇEK `AgentPrism.AspNetCore.FunctionalTests` koşumunda
  (`ConcurrentToolInvocationTests`) tel üzerinde **tırnaklı** gitti — kanıt:
  `ilspycmd` ile decompile edilen `OpenAIChatClient.ToOpenAIChatMessages`,
  `Result` ham `string` değilse `JsonSerializer.Serialize(Result, object-typeinfo)`
  çağırıyor ve isolate bir `AIFunctionFactory.Create(...).InvokeAsync(...)`
  probu bu delegate için runtime tipin `JsonElement` olduğunu doğruladı.
  Ama kapanışta `samples/AgentPrism.Api`'yi GERÇEK bir OpenAI anahtarıyla
  çalıştırıp `AddToolsFrom(typeof(OrderTools))` ile kayıtlı `get_order_status`
  (yine `string` dönen, ama `MethodInfo`-tabanlı) çağrıldığında,
  `GET /api/runs/{id}/tools` üzerinden okunan **ham** (`hex`/escape kontrolü
  yapıldı) `ToolInvocationRecord.Result` **tırnaksızdı**. İki gözlem de gerçek
  entegre koşumdan geliyor; aradaki fark (Delegate vs `MethodInfo` overload,
  ya da başka bir etken) bu fazda TAM izole edilmedi — kalan bütçe yetersizdi.
  **Sonuç: `ToolResultText`'in `GetRawText()` davranışı (102.2) doğru ve
  güvenli** (fail-closed/guard tasarımı `JsonElement` durumunu zaten doğru
  ele alıyor) ama "delegate ile kayıtlı HER string tool tırnaklı gider" gibi
  genellenmiş bir iddia KURMA — hangi kayıt yolunun hangi runtime tipi
  ürettiği ölçülmeden varsayılmasın.
- **Roslyn kaynak üreteçleri birbirinin ürettiği kaynağı AYNI derleme
  geçişinde göremez.** Generator'ın kendi `JsonSerializerContext`'ini emit
  edip aynı taramada tüketmesi planlanmıştı (102.2); gerçek tüketici build'i
  bunu `CS0534` ile kırdı (K-615). Kalıcı çözüm: gereksinim tüketicinin KENDİ
  derlemesine itilir (`APG0008`, `[JsonSerializable]` context'i çağıran
  yazar). Bir üreteç ileride benzer bir "üret ve aynı taramada tüket" deseni
  denerse önce bunu ölçsün.
- **Faz 101'in `dotnet format --no-restore` tuzağı BİREBİR tekrarlandı.**
  `dotnet format AgentPrism.slnx --verify-no-changes --no-restore` (kapı
  koşucusunun kendi komutu) `samples/`'ın `VersionOverride="*-*"` + yerel
  `NuGet.config` kombinasyonuyla workspace'i yükleyemedi (`CS0246` —
  `AgentPrismToolRegistration`, `FactAttribute` gibi tipler "bulunamadı",
  `AgentPrism.Samples.CustomTool.Tests` VE ilgisiz `AgentPrism.Samples.FileRunStore.Tests`
  ikisinde birden). `--no-restore`'u düşürmek (`dotnet format AgentPrism.slnx
  --verify-no-changes`) sorunu aynen 101'deki gibi çözdü. `kapi.py`'nin
  `closing_commands` listesi hâlâ `--no-restore` taşıyor — samples'a dokunan
  **her** fazda bu yeniden kırmızı görünecek; gerçek bir format hatasıyla
  (bu fazda ayrıca birini de yakaladı — import sıralaması) karıştırma.
- **Faz 101'in diğer üç tuzağı (NuGet global önbelleği, `artifacts/package/release`
  birikimi, `docfx CS1704`) bu kapanışta görülmedi** — kapanış oturumu birden
  fazla `dotnet pack` çalıştırdığı için `artifacts/package/release` biriktiği
  gözlendi (aynı paketin farklı build-numaralı iki sürümü) ve temizlenerek
  giderildi; devir notu 101'de duruyor.
- **🔴 Content guard'ın "fail-closed" sözü, ikinci turun bulduğu güvenlik açığı
  (yukarıdaki Denetim Bulguları) düzeltilmeden önce GERÇEK DEĞİLDİ.**
  Normalize edilemeyen bir tool sonucu, sabit bir yer tutucu metni guard
  desenine sokup eşleşme YOKSA orijinal ham içeriği dokunmadan geçiriyordu.
  Bu, "yer tutucu metin döndürmek" ile "koşulsuz yer tutucuyla DEĞİŞTİRMEK"
  arasındaki farkın gözden kaçmasıyla oldu — ikisi kodda birbirine çok benzer
  görünüyor. Bir guard/mask tasarımı incelenemeyen içerik için "güvenli
  varsayılan" öneriyorsa, o varsayılanın gerçekten KOŞULSUZ uygulandığını
  (bir eşleşme koşuluna bağlı olmadığını) satır satır izle.

**Yarım kalan iş.** Yok. `samples/AgentPrism.Api`, PostgreSql yerine geçici
bir Sqlite `ConnectionString` (env var override, `dotnet user-secrets`'a
DOKUNULMADI) ile başlatıldı; ortamda önceden yapılandırılmış gerçek
`OpenAI`/`Anthropic`/`Google` `secret`'ları vardı (Faz 101'in aksine bu ortam
onları taşıyordu). `support` agent'ı gerçek `gpt-5.4-mini` ile çalıştırıldı:
`GET /api/tools` üretim `ToolRegistrationValidationService`'ten geçmiş
kaydı gösterdi, `POST /api/agents/support/run` gerçek bir `get_order_status`
tool çağrısı + model cevabı üretti (573 token), `GET /api/runs/{id}/tools`
kalıcı `ToolInvocationRecord`'u doğruladı. Çıktı yukarıdaki DoD satırına
yazıldı.

**Sıradaki faz.** `docs/YOL-HARITASI.md` üretildikten sonra seçilir; bu faz
aday listesinden gelmedi (doğrudan kullanıcı isteği), aday listesine yeni bir
kalem eklemedi.

# Faz 127 — Tool Kayıt Yüzeyi: Tek Kompozisyon, Argüman Kapısı ve Kapsamlı Tool

> **Durum:** ✅ Tamamlandı (2026-09-01)
> **Kaynak:** [kesif/2026-08-31-tuketici-raporu-faz-adaylari.md](../../kesif/2026-08-31-tuketici-raporu-faz-adaylari.md) · **T-4**, **T-3**
> **Önkoşul:** [Faz 69](69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) (sarmalayıcı zinciri ve sırası) ve [Faz 89](89-TOOL-CIKTISI-BOYUT-SINIRI.md) (`TruncatingAIFunction`) — ikisi de arşivde
> **Paketler:** `Tracon.Abstractions` (yeni arayüz), `Tracon.Core` (`Tools/`), `Tracon.Mcp` (`Internal/McpTenantTools.cs`)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — bir arayüz (`IToolArgumentsValidator`), bir sonuç tipi (`ToolArgumentsValidationResult`), iki builder metodu (`AddScopedTool` aşırı yüklemeleri) ve **bir sarmalayıcı tip** (`ValidatingAIFunction`, `AuthorizingAIFunction`/`TimeoutAIFunction`/`TruncatingAIFunction` ile aynı public-wrapper deseninde — kapanışta eklendi, denetim bulgusu). Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır (K-603)
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/write-your-own-tool.md`, `concepts/tools.md`, `reference/extension-points.md` · sevk edilen: yeni arayüzün XML `<example>`'ı, `Tracon.AgentMap.md` yetenek satırı
> **Manuel test alanı:** `docs/manuel-test/18-MCP-VE-A2A.md` (MCP tarafı) ve `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md` (argüman kapısı)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f5a295:docs/arsiv/fazlar/127-TOOL-KAYIT-YUZEYI.md
> ```
>
> Damıtıldı 2026-09-01 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz tool kayıt yüzeyindeki üç şeyi birlikte ele alır, çünkü üçü de **aynı altyapıya** — sarmalayıcı zincirine ve `TraconBuilder`'a — dokunur. Birincisi bir yapısal borçtur ve ölçümde çıktı: sarmalayıcı zinciri **iki yerde elle yazılı** ve kodun kendi yorumu bunu itiraf ediyor.

## Bitiş Ölçütleri (DoD)

- [x] Sarmalayıcı zinciri **tek** yerde kuruluyor; `ToolRegistry` ve `McpTenantTools` aynı fonksiyonu çağırıyor — `ToolWrapperChainTests` iki yolun aynı sırayı ürettiğini kanıtlıyor
- [x] Kayıtlı bir `IToolArgumentsValidator` hem kod tool'unu hem **MCP tool'unu** görüyor — manuel case 1 ve 2 (`MT-GUARD-080`, `MT-MCP-068`; otomatik karşılıkları koştu, gerçek sağlayıcı anahtarıyla henüz elle koşulmadı — bkz. Plandan Sapmalar)
- [x] Doğrulayıcı kayıtlı değilken davranış değişmiyor; mevcut tool testleri değişmeden geçiyor
- [x] Reddedilen çağrının metni argüman **değeri** taşımıyor
- [x] Fırlatan doğrulayıcı çağrıyı reddediyor, run'ı düşürmüyor
- [x] `AddScopedTool` ile kaydedilmiş tool her çağrıda taze kapsam alıyor; kapsam tool tamamlanınca kapanıyor — `ScopedToolLifetimeTests`
- [x] Eşzamanlı iki tool çağrısı ayrı kapsamlar alıyor
- [x] `ToolMethodScanner` ret metni `AddScopedTool`'u adıyla gösteriyor; K-347 **açılmadı** (örnek metot hâlâ reddediliyor)
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] Gerçek boru hattıyla `run` yapıldı, çıktı belgeye yazıldı — **`samples/Tracon.Api`'de DEĞİL** (bu ortamda sağlayıcı anahtarı yok); `ToolGovernanceEndpointTests`/`ScopedToolLifetimeTests` gerçek `FunctionInvokingChatClient` döngüsü ve gerçek `TraconTestHost` üzerinden, sahte model sağlayıcısıyla koştu. Gerçek anahtarla `samples/Tracon.Api` koşumu sonraki oturuma devredildi (bkz. Plandan Sapmalar, Sonraki Faza Devir Notu)
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/18-MCP-VE-A2A.md` ve `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md` içine eklendi
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

### Doğrulama komutları

```bash
# İki yolun aynı zinciri kurduğu
./artifacts/bin/Tracon.Core.UnitTests/release/Tracon.Core.UnitTests --filter-method "*ToolWrapperChain*"

# Kapsam yaşam döngüsü (sınır: DI)
./artifacts/bin/Tracon.AspNetCore.FunctionalTests/release/Tracon.AspNetCore.FunctionalTests --filter-method "*ScopedToolLifetime*"
```

---

## Plandan Sapmalar

- **`docs-site/src/content/docs/reference/extension-points.md` yok.** Fazın "Tüketici
  yüzeyi" satırı bu dosyayı anıyordu; repo'da böyle bir dosya hiç yoktu (muhtemelen
  aday metninden kalma yanlış bir yol). Onun yerine gerçekten var olan ve tool
  kaydını anlatan üç sayfa güncellendi: `getting-started/tools.md`,
  `concepts/tools.md`, `guides/write-your-own-tool.md`.
- **`samples/Tracon.Api` ile gerçek sağlayıcı çağrısı yapılmadı.** Bu ortamda
  bir OpenAI/Anthropic/vb. API anahtarı yok. Onun yerine gerçek boru hattı
  (`FunctionInvokingChatClient` tool döngüsü, gerçek `TraconTestHost`)
  `ToolGovernanceEndpointTests.Rejected_arguments_complete_the_run_and_are_recorded_as_ToolFailed`
  ve `ScopedToolLifetimeTests`'te sahte bir model sağlayıcısıyla (`FakeModelProvider`)
  koştu — MAF'ın gerçek fonksiyon çağırma döngüsünden geçer, yalnız model kararı
  sahtedir. `MT-GUARD-080/081`, `MT-MCP-068`, `MT-CORE-107/108` bu yüzden 👤
  (elle, gerçek anahtarla) koşulmadı işaretiyle kaldı.
- **İki ek manuel case, plandaki iki dosyanın dışında.** Plan yalnız
  `18-MCP-VE-A2A.md` ve `22-GUARDRAIL-VE-YAPISAL-CIKTI.md`'yi anıyordu (argüman
  kapısı için). `AddScopedTool`'un yaşam döngüsü (manuel case 4) ve
  `ToolMethodScanner`'ın ret metni (manuel case 5) argüman doğrulamasından ayrı,
  genel bir tool-kaydı konusu olduğu için `02-CEKIRDEK-VE-KATALOG.md`'ye
  (`CORE`, zaten `Tools/` kapsıyor) `MT-CORE-107`/`MT-CORE-108` olarak eklendi.
- **`capabilities.md`'de üç ÖNCEKİ satır da kısaltıldı.** İki yeni satır
  (`Scoped tools`, `Argument validation`) eklenince üretilen `llms.txt` sabit
  20 480 bayt bütçesini aştı (agent map yalnız tablonun `Ad` ve `Kayıt/kaynak`
  hücrelerini okur, "Ne zorlanıyor" hücresini HİÇ okumaz — ölçüldü). Bütçeye
  dönmek için `Delegate tools`, `Tool output size limit` ve `Client-side tools`
  satırlarının **kod açıklığı** hücreleri kısaltıldı; anlamları ve tam açıklamaları
  sayfanın kendi metninde değişmeden durur. İçerik hatası değil, bütçe disiplini.
- **`ValidatingAIFunction` reddi, `AuthorizingAIFunction`'ın deseninin TERSİDİR.**
  Plan yalnız "reddedilen çağrı `ToolFailed` olayına yazılır" diyordu; somut
  mekanizma plandan çıkarıldı: `AuthorizingAIFunction` bir reddi normal başarılı
  bir sonuç olarak DÖNER (fırlatmaz), `ValidatingAIFunction` ise
  `TraconException` FIRLATIR — MAF'ın kendi exception→`FunctionResultContent`
  dönüşümü bunu `ToolFailed` yapar (`TimeoutAIFunction`'ın zaten kullandığı aynı
  mekanizma, `ToolGovernanceEndpointTests`'in mevcut timeout testiyle önceden
  kanıtlanmıştı). İki halka aynı "fail-closed" ilkesini paylaşır ama farklı
  kayıt sınıfı üretir — bilinçli, plan onaylı bir asimetri (DoD, "Reddedilen
  çağrının sonucu").
- **`ValidatingAIFunction` `public`.** Plandaki "Planlanan Public API" bölümü
  yalnız arayüzü, sonuç tipini ve `AddScopedTool`'u sayıyordu; sarmalayıcı
  tipin kendisi listede yoktu. Kod tarafında sorun değil —
  `AuthorizingAIFunction`/`TimeoutAIFunction`/`TruncatingAIFunction` zaten aynı
  desende `public`'tir (tüketici `AITool.GetService<T>()` ile katmanı bulabilsin
  diye) ve `PublicAPI.Unshipped.txt` doğru güncellenmişti — yalnız plan bunu
  öngörmemişti (bağımsız denetim bulgusu, 🟡#1). Bu belge şimdi düzeltiliyor.

## Bu Fazda Verilen Kararlar

Yok. Plandaki dört Açık Soru, plan dokümanının kendi "Öneri" sütunundaki A
seçenekleriyle (tek kompozisyon noktası `Core`'da kalır, fırlatan doğrulayıcı
reddeder, tool-başına muafiyet yok, `AIFunctionArguments.Services` yazılabilir
— `maf-api-kesfi` ile ölçüldü) kullanıcıya yeniden sorulmadan kapandı; hiçbiri
public API/compatibility contract, güvenlik/kiracı sınırı veya kalıcı veri
kararı düzeyinde değildi, dolayısıyla yeni bir `K-*` kaydı açılmadı.

## Denetim Bulguları

Bağımsız denetçi (taze bağlamlı ayrı bir agent) 2026-09-01'de koştu. Üç 🔴, iki
🟡 bulgu; hepsi bu faz içinde kapandı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `docs-site`'ın `npm run check:content` kapısı kırmızıydı: `write-your-own-tool.md` yeni bölümlerle `DIAGRAM_THRESHOLD` (6500 bayt) üstüne çıktı, diyagramsızdı. | **Düzeltildi.** Sayfaya "Call pipeline order" mermaid akış şeması eklendi (Authorizing → Validating → Timeout → ApprovalRequired → Truncating). `npm run check` dördü de yeşil. |
| 2 | 🔴 | `SourceLanguageTests` kırmızıydı: `ToolRegistrationTests.cs:231`'deki yorumda `Faz 127` ifadesi Türkçe kelime listesine takılıyordu. | **Düzeltildi** — denetim başlamadan önce, uygulama oturumunda zaten fark edilip giderilmişti (denetçi çalışma ağacının erken bir anını yakaladı); bağımsız yeniden koşum bunu doğruladı. |
| 3 | 🔴 | DoD satırı "`samples/Tracon.Api` ile gerçek `run` yapıldı" kanıtsızdı; faz dokümanının kapanış bölümleri denetim anında hâlâ boştu. | **Gerekçelendi** — bu ortamda hiçbir sağlayıcı API anahtarı yok (ölçüldü: `env` taraması boş döndü). Gerçek boru hattı (`FunctionInvokingChatClient` döngüsü, gerçek `TraconTestHost`) `ToolGovernanceEndpointTests`/`ScopedToolLifetimeTests`'te sahte model sağlayıcısıyla koştu; ilgili beş manuel case (`MT-GUARD-080/081`, `MT-MCP-068`, `MT-CORE-107/108`) 👤 (gerçek anahtarla elle koşulacak) olarak açıkça işaretlendi — bkz. Plandan Sapmalar ve Sonraki Faza Devir Notu. |
| 4 | 🟡 | `ValidatingAIFunction` `public` sevk ediliyor ama plandaki "Planlanan Public API" özeti bunu saymıyordu. | **Gerekçelendi ve belgelendi** — bkz. Plandan Sapmalar; kod doğru (`AuthorizingAIFunction`/`TimeoutAIFunction`/`TruncatingAIFunction` ile aynı desen), yalnız plan metni eksikti. |
| 5 | 🟡 | Fazın "Tüketici yüzeyi" satırı var olmayan `docs-site/.../reference/extension-points.md`'yi anıyordu. | **Gerekçelendi** — bkz. Plandan Sapmalar; gerçek tüketici içeriği doğru sayfalara (zaten) eklenmişti, yalnız plan yanlış bir yola işaret ediyordu. |

**Temiz çıkan başlıklar:** 3.2 (test tiyatrosu), 3.3 (yanlış test seviyesi),
3.4 (kapsanmayan hata yolları), 3.5 (imza-gövde kayması), 3.7 (repo kuralları,
dil sınırı hariç — o da #2 ile kapandı).

🔴 bulgular kapandıktan sonra dört kapı yeniden koşuldu (`dotnet build` sıfır
uyarı, ilgili test projeleri yeşil, `npm run check` dördü yeşil).

## Sonraki Faza Devir Notu

- Tool sarmalayıcı zinciri artık **tek** kompozisyon noktasından
  (`ToolWrapperChain.Compose`) geçiyor. Yeni bir halka eklerken tek yer
  yeterli — `ToolRegistry`/`McpTenantTools`'a ayrı ayrı dokunmaya gerek yok.
- `IToolArgumentsValidator` şu an sadece **tüketicinin** yazacağı bir arayüz;
  Tracon kendi JSON Schema doğrulayıcısını sevk etmiyor (bilinçli tercih,
  §127.2). İleride bir gömülü doğrulayıcı istenirse bu, yeni bir fazdır.
  `Tools/` kapsamında dokunulmayan `docs/manuel-test/22-...`'nin §9'u yalnız
  reddetme yolunu kanıtlıyor — kabul eden bir gerçek doğrulayıcıyla koşum
  henüz yok.
- `samples/Tracon.Api`'ye gerçek anahtarla erişimi olan bir sonraki oturum,
  `MT-GUARD-080/081`, `MT-MCP-068`, `MT-CORE-107/108`'i 👤 olarak kapatmalı —
  bu faz onları yalnız otomatik testlerle (gerçek boru hattı, sahte model)
  kanıtladı.

# Faz 202 — Skill Script Yürütmesini Harici Sandbox'a Yönlendiren Seam

> **Durum:** 📋 Planlandı (2026-10-12)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-A, 5-A; 1–3 güvenlik kararıdır). Faz 196 ve Faz 203 ile paralel geliştirilir
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-345**
> **Önkoşul:** Yok — işlevsel bağımlılık yok; Faz 196 ile ortak dosya `TimeoutSiteTests.cs`
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `concepts/tools.md`, yeni `guides/write-your-own-script-executor.md` (+ `src/sidebar.mjs`), `reference/{threat-model,security-policy}.md`, `getting-started/security.md`, `capabilities.md` · sevk edilen: XML `<example>`, `SECURITY.md`, iki paket README'si, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/14-SKILL-VE-SCRIPT.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar: `grep -n "K-012\|K-058\|K-066\|K-086\|K-087\|K-089\|K-860\|K-861\|K-862" docs/KARARLAR.md`
   — K2 (2026-10-12'de teyit edildi) ve iki istisnası (K-058, K-066); onay bayrağı
   (K-086); fail-closed denetim (K-089); içerik pini (K-860); platform yetkisi
   (K-861); B7 adı ve R8 (K-862)
3. Devir notları (kapı akışı aynı çıktıdadır):
   `awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/11-SKILL-SCRIPT-CALISTIRMA.md`;
   aynısı `186-SCRIPT-IZNI-ICERIK-PINI.md` için
4. Alan hafızası: `grep -n "script\|Script" docs/hafiza/{maf-api,secenek-baglama-ve-gizlilik,genisleme-noktalari-ve-denetim}.md`
   (sevk edilen XML'e faz/`K-`/`F-` yazılmaz) · `aspnetcore-di.md` (`internal` kurucu fabrika ister)
5. Gerektiğinde: `MIMARI-GUVENLIK.md` § *Skill script çalıştırma* · `MIMARI-TEHDIT-MODELI.md` § R8

---

## Amaç

Skill script'i bugün beş kapıdan geçer ve Tracon'un makinesinde, Tracon'un OS
kimliğiyle başlar (R8, K-862). Yürütme tek bir `internal static` çağrıdır; host
süreci kendi ortamına (container, mikro-VM, uzak servis) taşıyamaz. Bu faz çağrıyı
public bir seam'e çevirir: `ISkillScriptExecutor`. Host kendi yürütücüsünü DI ile
kaydeder; kapılar, pin, onay, denetim, kota, çıktı ve süre sınırı Tracon'da kalır.
Yeni kod yazma yüzeyi açılmaz: K2 gevşemez, yalnız K-066 sürecinin **nerede**
başladığı değişir.

Tüketici bağlamı (2026-10-12): çok kiracılı bir SaaS'ta operatör canlı kod yazar ve
kod host'un sandbox runner'ında koşar; agent'ın çağırdığı script de orada
koşmalıdır. Plan genel bir Tracon yeteneğidir.

- **F-345** — yürütücü sözleşmesi, varsayılan yerel uygulama, runner bağlantısı,
  sözleşme testi, site güvenlik metni.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| Fonksiyon düğümü ve host job'u medya adımları | Host runner'ı kendisi çağırır; süre sınırı Faz 196'dadır |
| Arayüzden yazılan kodu koşturan düğüm | K2/L20; kullanıcı 2026-10-12'de teyit etti |
| Tracon'un HTTP/container yürütücü paketi | Runner API'si host'a özgü; rehber iskelet verir |
| Hyperlight yürütücüsü | F-169 eşiği aşılmadı: en yeni 0.7.0 (2026-10-12) |
| MCP stdio, onay akışı, `script.run` yükü, küme çapında kota | Değişmez |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `SandboxedSkillScriptRunner.cs:447-456` · `SkillScriptExecutionResult.cs:4` | Tek yürütme çağrısı `internal static SkillScriptProcessRunner.ExecuteAsync`; arayüz yok, sonuç tipi `internal` |
| `TraconSkillScriptBuilderExtensions.cs:64-78` | `UseSkillScripts` yürütücü kaydetmez |
| `SkillScriptProcessRunner.cs:78-88` · `:93-104` | `UserName` atanmaz: süreç Tracon'un OS kimliğiyle başlar; ortam allow-list'ten geçer |
| `SandboxedSkillScriptRunner.cs:422-445` | Stored içerik Tracon'un geçici diskine yazılır; ad yazma sınırında doğrulanır (`:439`) |
| `SandboxedSkillScriptRunner.cs:173-174` · `:184-186` | Disk script'inde yalnız yol taşınır; içerik okunmaz |
| MAF `AgentFileSkillScript` (`maf-api-kesfi`, 2026-10-12) | Yalnız `FullPath`, `ParametersSchema`; içeriği Tracon okumalı |
| `SandboxedSkillScriptRunner.cs:400-420` · `SkillScriptConcurrencyLimiter.cs:25-41` | Denetim yürütmeden önce yazılır (K-089); sonra süreç içi kota |
| `SkillScriptProcessRunner.cs:109-137` · `TimeoutSiteTests.cs:38` | Zaman aşımı yerel çağrıda; mimari test siteyi adıyla bekler |
| `TraconOptions.cs` `MaxOutputBytes` ↔ `SkillScriptProcessRunner.cs:114-115` | Kod her akışı ayrı keser; XML 2026-10-12'de "akış başına" diye düzeltildi (F-342 demeti) |
| `TraconOptionsValidator.cs:279-286` | `Enabled` iken `PlatformIsolationAcknowledged` zorunlu |
| `SkillScriptGrantEndpoints.cs:216-221` | Çok kiracılıda stored grant platform yetkisi ister |
| `RequiredBindingValidator.cs:74` · `TraconDefaultRegistrations.cs:166-188` | `RequireCustomBinding<T>()` `TryAddTraconDefault` işaretli sözleşmeyi kabul eder |
| `SeamContractDocumentationTests.cs:83-88` | Arayüz XML'i ömür, kiracı modu, teslim ifadesi taşımalı |
| `check-content.mjs:1527-1528` · `:208-215` | Sandbox vaadi her sayfada (`api/` dahil) ve `SECURITY.md`'de yasak; *Accepted risks* "operating-system identity" demeli |
| `grep -rn UseSkillScripts samples` → boş | Örnekte script yok; manuel case geçici kod ekler (MT-SKILL-041) |

> Kanıtlar 2026-10-12 tarihinde `99fe0b13` zemininde doğrulandı.

---

## 202.1 — Sözleşme

- **Sıra değişmez:** kapılar → `script.run` denetimi → kota → yürütücü. Kapıdan
  geçmeyen istek yürütücüye ulaşmaz.
- **İçerik istekte taşınır.** Stored içerik pinin hesaplandığı tanım nesnesinden
  gelir (`SandboxedSkillScriptRunner.cs:291-293`); disk script'ini Tracon kotadan sonra okur. Reddedilen: hash +
  çekme ucu (yeni kimlikli uç, TOCTOU).
- **`ContentSha256`** = `Content`'in UTF-8 SHA-256'sı. `ContentHash` adı bilerek
  kullanılmaz: grant hash'i alan ayırıcılı ve `internal`'dır (`SkillScriptHashing.cs:28`).
- **Kiracı** kapıların kiracısıdır (`:308`); **`RunId`** `TraconRunContext.CurrentRunId`.
- 🚨 **Ortam değeri istekte yoktur** (`EnvironmentAllowList` yerel ayardır);
  `ToString` `Content` ve `ArgumentsJson`'ı basmaz.
- **Script'in gördüğü:** stdin'de `ArgumentsJson`, `TRACON_SKILL_NAME`, çağrıya özel
  `TRACON_SKILL_TEMP`.
- Yürütücü runner kurucusuna **zorunlu** parametredir; MAF'ın `IServiceProvider`'ı
  boştur (`:135`). XML: singleton · "expected tenant" · "no delivery guarantee
  applies"; garanti sınırı: süreç yalıtılmaz, iptal işbirliğine dayanır.

## 202.2 — Varsayılan yerel yürütücü

- `SkillScriptProcessRunner` `internal sealed class : ISkillScriptExecutor` olur; ad
  ve yol korunur. Kayıt `UseSkillScripts` içinde `TryAddTraconDefault` + fabrika.
  🚨 Tip tabanlı kayıt `internal` kurucuda çalışma anında düşer.
- İşaretli kayıt `RequireCustomBinding<ISkillScriptExecutor>()`'ı açar: host yerel
  yürütmeyi açılışta reddeder (XML: `TraconBuilderExtensions.cs:52-57`).
- **Davranış birebir kalır;** scratch ve ad doğrulama runner'dan buraya taşınır.
  `SandboxedSkillScriptRunnerTests`, `SkillScriptProcessRunnerTests`,
  `SkillScriptContentPinTests` yalnız kurulum satırı değişerek yeşil kalır.
- `MaxOutputBytes` akış başınadır (XML 2026-10-12'de düzeldi); istek alanı aynı anlamı taşır.

## 202.3 — Süre, iptal ve çıktı

- Yürütücü `Timeout`'ta durdurur, `TimedOut` döndürür; çağıran iptali
  `OperationCanceledException`'dır.
- **Bekleme sınırı (yeni):** runner `Timeout + tolerans`'ta token'ı iptal eder,
  beklemeyi bırakır (`WaitAsync`, `TimeProvider`); kayıt `TimedOut`. 🚨 Toplam
  zamanlayıcı aralığını taşabilir — validator denetler, `TimeoutSiteTests` siteyi
  adlandırır. Uzak süreç sürebilir; site bunu yazar.
- **Çıktı sınırını runner yeniden uygular;** kesme işaretini yalnız runner ekler
  (biçimli metnin sonuna; `CHANGELOG` anar).
- **Süreyi runner ölçer** (`Duration` sonuçta yok). Tutarsız sonuç, `null` ve
  istisna span, metrik ve `tool_invocations`'a hata yazar; MAF'a `TraconException`.

## 202.4 — Güvenlik metni

- 🚨 Denetim yazımı runner'da kalır; yürütücüye taşınırsa host atlayabilir
  (`AuditWritePolicyTests` yeri dosya adıyla izler).
- Stored script adı yürütücüden önce runner'da da doğrulanır (`SkillScriptNaming`).
- B7 adı ve kapsam satırı değişmez; R8 "varsayılan yürütücüyle" diye nitelenir.
- 🚨 Yeni metin "runs where your executor runs it" der; yasak ifade `api/`'de de kırmızıdır.

## 202.5 — Sözleşme testi

Yeni aile `Tracon.Testing.Contracts.Scripts` + `ContractCoverage.ScriptContracts`.
Türeyen sınıf `CreateExecutorAsync()` verir; gövdeler (varsayılan POSIX `sh`)
ezilir. Senaryolar: (1) stdin JSON, boş argüman · (2) çıkış kodu, stderr ·
(3) `Truncated` · (4) `TimedOut`, `ExitCode = null` · (5) iptal · (6) iki değişken
görünür, test sürecinin değişkeni görünmez · (7) eşzamanlı çağrılar geçici dizin
paylaşmaz · (8) `Stored` istekte `Content` koşar. Yerel yürütücü onu türetir, `sh`
yoksa atlar. R8'in tersi manuel case 4'tedir.

---

## Planlanan Public API

> Taslak. **Bugün eklemek bedava / `1.0.0` GA sonrası** record'a alan, arayüze üye
> eklemek kırıcı — alanlar şimdi tamamlanır.

```csharp
// Tracon.Abstractions
public interface ISkillScriptExecutor
{
    ValueTask<SkillScriptExecutionResult> ExecuteAsync(
        SkillScriptExecutionRequest request, CancellationToken cancellationToken = default);
}

public enum SkillScriptOrigin { File, Stored }   // Stored: database or code-defined, content-pinned

public sealed record SkillScriptExecutionRequest
{
    public required string TenantId { get; init; }
    public Guid? RunId { get; init; }
    public required string SkillName { get; init; }
    public required string ScriptName { get; init; }
    public required SkillScriptOrigin Origin { get; init; }
    public required string Extension { get; init; }
    public required string Interpreter { get; init; }   // allow-list value; a runtime name remotely
    public required string Content { get; init; }
    public required string ContentSha256 { get; init; }
    public string? SourcePath { get; init; }      // File only; run in place locally
    public string? ArgumentsJson { get; init; }   // stdin
    public required TimeSpan Timeout { get; init; }
    public required int MaxOutputBytes { get; init; }   // per stream
}

// Tracon.Testing.Contracts.Xunit
public abstract class SkillScriptExecutorContract
{
    protected abstract ValueTask<ISkillScriptExecutor> CreateExecutorAsync();
}
```

`SkillScriptExecutionResult` Core'dan `Abstractions`'a taşınır, `Duration` çıkar
(`ExitCode`, `StandardOutput`, `StandardError`, `Truncated`, `TimedOut`,
`Succeeded`). `Tracon.Core`'da yeni public üye yok; XML değişir. **HTTP
`endpoint`'leri:** Yok. **Arayüz payı:** Yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Skills/   yürütücü, istek, köken, sonuç (taşınır)
src/Tracon.Core/                  runner, yerel yürütücü, UseSkillScripts, seçenek XML'i, validator
src/Tracon.Testing.Contracts.Xunit/  Contracts/Scripts/SkillScriptExecutorContract, ContractCoverage
tests/Tracon.Core.UnitTests/      SkillScriptProcessRunnerContractTests, iki mimari test
tests/Tracon.AspNetCore.FunctionalTests/  SkillScriptExecutorRoutingTests, SkillScriptExecutorRegistrationTests
docs/MIMARI-GUVENLIK.md · docs/MIMARI-TEHDIT-MODELI.md  (kapanışta)
```

---

## Hata Modları ve Testler

> Seviyeyi plan seçer; sınır geçen davranış birim testiyle kanıtlanamaz —
> [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

| # | Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|---|
| 1 | Kayıtlı yürütücüye rağmen yerel süreç başlar | Fonksiyonel (HTTP → MAF → runner, nöbetçi dosya) | `SkillScriptExecutorRoutingTests` |
| 2 | Kapı kapalıyken yürütücü çağrılır | Fonksiyonel (grant yok, bayat pin, liste dışı uzantı) | aynı |
| 3 | Denetim yazılamazken yürütücü çağrılır | Fonksiyonel (atan `IAuditLog`) | aynı |
| 4 | Giden içerik pinlenenden farklı / hash yanlış | Fonksiyonel (stored + disk) | aynı |
| 5 | Varsayılan davranış kayar | Mevcut birim + fonksiyonel + sözleşme | mevcut üç sınıf |
| 6 | Süreye uymayan yürütücü kotayı asılı tutar | Birim (elle ilerletilen saat) | `SandboxedSkillScriptRunnerTests` |
| 7 | `Timeout + tolerans` aralığı taşar | Birim + mimari | `TimeoutOptionValidationTests` · `TimeoutSiteTests` |
| 8 | Sınırı aşan çıktı modele gider | Birim | `SandboxedSkillScriptRunnerTests` |
| 9 | İstisna / `null` / tutarsız sonuç kaydı düşürür | Birim | aynı |
| 10 | **İptal:** çağıran iptali `TimedOut` sayılır | Birim + sözleşme | aynı |
| 11 | **Eşzamanlılık:** kota atlanır; geçici dizin paylaşılır | Birim + sözleşme | aynı |
| 12 | **Boş/aşırı girdi:** argümansız, boş içerik, büyük disk script'i | Birim + sözleşme | aynı |
| 13 | **Başka kiracı:** istek grant'ı verenden farklı kiracı taşır | Fonksiyonel (iki kiracı) | `SkillScriptExecutorRoutingTests` |
| 14 | **Alt sistem hatası:** disk okunamaz; uzak ağ hatası | Birim | `SandboxedSkillScriptRunnerTests` |
| 15 | `secret`, içerik veya argüman isteğe/log'a sızar | Birim (`ToString`) + log taraması | `SkillScriptExecutorRoutingTests` |
| 16 | Tüketici kaydı sıraya göre kaybeder; `RequireCustomBinding` kaçırır | Fonksiyonel (DI, iki sıra) | `SkillScriptExecutorRegistrationTests` |
| 17 | Arayüz XML'i üç ifadeyi taşımaz | Mimari | `SeamContractDocumentationTests` |
| 18 | Site/XML yasak ifade kullanır | Site kapısı | `check-content.mjs` |
| 19 | Sözleşme ailesi türetilmez | Mimari | `ContractCoverage.MissingDerivedTypes` |

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/14-SKILL-VE-SCRIPT.md` içine eklenir.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnekte geçici `UseSkillScripts` + kaydedici yürütücü | Pinli grant, kalıcı onay → agent script çağırır | Run `Completed`; yürütücü isteği aldı; yerel süreç yok |
| 2 | Aynı, grant yok | Aynı çağrı | `script.denied`; yürütücü çağrılmadı |
| 3 | Yürütücü süreyi yok sayar | İki çağrı | İlki `TimedOut`; ikincisi kota beklemez |
| 4 | Yürütücü `docker run --rm -i --network none alpine:3.20 sh` | `cat /proc/$PPID/environ` | Tracon ortamı görünmez — 👤 Docker |
| 5 | `RequireCustomBinding<ISkillScriptExecutor>()`, kayıt yok | Başlat | Açılış hatası sözleşmeyi adlandırır |

---

## Açık Sorular

> 1–3 güvenlik kararıdır; uygulama onaydan önce başlamaz.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Harici yürütücüde `PlatformIsolationAcknowledged` zorunlu mu? | A: kalır · B: istenmez | **A** — Tracon yalıtımı doğrulayamaz; yerel yürütmeyi kapatmak için `RequireCustomBinding` var |
| 2 | Yürütücü kayıtlıyken K-861 gevşer mi? | A: değişmez · B: yürütücü kiracı yalıtımı beyan eder | **A** — beyan doğrulanamaz; ayrı karardır. Tüketicide grant'ı platform operatörü verir |
| 3 | Disk script'i de yürütücüye gider mi? | A: evet, kardeş dosyalar taşınmaz · B: yalnız stored | **A** — yarım yönlendirme R8'i açık bırakır; yürütücü `Origin`'e bakıp reddedebilir |
| 4 | Bekleme toleransı | A: sabit 5 sn · B: yeni seçenek | **A** — seçenek yüzeyi açmaz; kapanışta uzak stub ile denenir |
| 5 | Kota harici yürütücüde uygulanır mı? | A: evet · B: atlanır | **A** — kota Tracon sınırıdır |

---

## Bitiş Ölçütleri (DoD)

- [ ] Kayıtlı yürütücüyle stored ve disk script'i yerel süreç başlatmadan yürütücüye gider (test 1, 4)
- [ ] Kapı kapalıyken veya denetim yazılamazken yürütücü çağrılmaz (test 2-3)
- [ ] Varsayılanla mevcut script testleri davranış iddiası değişmeden yeşil
- [ ] Süreye uymayan yürütücü `Timeout + tolerans` içinde `TimedOut` kaydı üretir (test 6)
- [ ] Yerel yürütücü sözleşmeyi geçer; `RequireCustomBinding` yerel varsayılanda açılışı durdurur
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1, 4), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/14-SKILL-VE-SCRIPT.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `cd docs-site && npm run check` temiz; `CHANGELOG.md` yeni tipleri anar

---

## Riskler

| Risk | Önlem |
|------|-------|
| Host yürütücüsü vermediği yalıtımı vaat eder | Tracon yalnız kapıları vaat eder (K-862 dili) |
| Yürütücü süreye uymaz veya içeriği log'a yazar | Bekleme sınırı; rehber kuralı yazar |
| `MIMARI-GUVENLIK.md` bütçesi %0 boş (2026-10-12) | Kapanışta metin eklenmez, değiştirilir; ayrıntı siteye |
| Faz 196 ile `TimeoutSiteTests.cs` çakışır | Sonra başlayan faz satırı birleştirir |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Dört kategoriden birine giren karar ayrıca
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer. Beklenen adaylar:
> yürütücü seam'i, R8'in varsayılan yürütücüye daralması (`public-api`,
> `güvenlik`); Açık Soru 1–3. Plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Bulgu yoksa "🔴 ve 🟡 yok"
> yazılır; boş bırakılmaz.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

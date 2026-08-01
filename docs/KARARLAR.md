# KARARLAR.md — Canlı Karar Defteri

> Bu dosya, projede **bilinçli olarak alınmış ve gerekçelendirilmiş kararların** kaydıdır. Amacı: gelecekteki bir oturumun (insan veya AI) daha önce kanıtla reddedilmiş bir işi yeniden önermesini veya kapatılmış bir tartışmayı yeniden açmasını engellemek.
>
> **Kullanım kuralı:** Buradaki bir kalemi öneri olarak gündeme getirmeden önce "yeniden açılma koşulu" sütununa bak. Koşul gerçekleşmediyse önerme. Koşul gerçekleştiyse, bu dosyayı güncelleyerek kalemi yeniden aç.

---

## 1. Kanıt Olmadan Yeniden Açılmayacak İşler

Kaynak: Faz 0 altyapı çalışması (2026-08-01). Ayrıntı: [`00-ALTYAPI.md`](00-ALTYAPI.md), [`MIMARI.md`](MIMARI.md).

| Karar | Tarih | Gerekçe | Yeniden açılma koşulu |
|---|---|---|---|
| **Arayüz Blazor ile yazılmadı** (WebAssembly veya Server) *(kullanıcı kararı)* | 2026-08-01 | WebAssembly ilk yükleme boyutu (~2 MB+) "lite arayüz" hedefiyle çelişiyor. Blazor Server kalıcı SignalR bağlantısı ister; bir NuGet paketi olarak tüketicinin barındırma modelini kısıtlar. React+Vite çıktısı gömülü statik varlık olarak sıfır tüketici bağımlılığı üretir. | Blazor WASM AOT çıktısı gzip <300 KB'ye inerse yeniden ölçülür |
| **EF Core kullanılmadı** *(kullanıcı kararı)* | 2026-08-01 | Kütüphane, tüketiciye EF Core sürüm kısıtı dayatmamalı; EF Core sürüm çakışması .NET'te en sık bağımlılık sorunlarından biri. Ayrıca EF migration'larını tüketicinin projesinden çalıştırmak gerekir — kütüphane kendi şemasını kendi yönetemez. Ham Npgsql + gömülü SQL bu iki sorunu da ortadan kaldırır. | AgentPrism bir kütüphane olmaktan çıkıp bağımsız bir servise dönüşürse |
| **`netstandard2.0` ve `net472` hedeflenmedi** | 2026-08-01 | AgentPrism'in çalışma yeri modern ASP.NET Core. Bu iki hedef `IAsyncEnumerable`, `System.Text.Json` kaynak üreteçleri ve minimal API yapılarında ciddi ek yük getirir; karşılığında AgentPrism senaryosunda kullanıcı üretmez. | .NET Framework üzerinde somut bir kullanıcı talebi gelirse |
| **Tek paket (monolitik) paketleme yapılmadı** *(kullanıcı kararı)* | 2026-08-01 | PostgreSQL kullanmayan tüketici `Npgsql`'i çekmemeli. Modüler yapıda `AgentPrism.SqlServer` veya `AgentPrism.Anthropic` eklemek breaking change olmaz. | — |
| **Geçişli sabitleme (`CentralPackageTransitivePinningEnabled`) açılmadı** | 2026-08-01 | Ölçüldü: bayrak açıkken `AgentPrism.PostgreSql` üretilen `.nuspec` içinde **13** doğrudan bağımlılık bildiriyordu (`OpenAI`, `OpenTelemetry.Api` gibi hiç referans verilmemişler dahil). Kapalıyken **2**. Uygulamalarda doğru, kütüphanelerde tüketicinin bağımlılık grafiğini kirletir. | Bir CVE nedeniyle geçişli sürüm zorlanması gerekirse — o zaman bilinçli tek bir `PackageReference` eklenir, bayrak yine açılmaz |
| **Arayüzden tool kodu yazma özelliği eklenmedi** | 2026-08-01 | Arayüze erişen herkes sunucuda kod çalıştırabilirdi. Arayüz yalnız kodda kayıtlı tool'lardan seçim yaptırır. Bu sınır güvenlik sınırıdır. | Yeniden açılmaz. MCP tool'ları (Faz 6) ayrı policy + zorunlu onay + denetim izi ile bilinçli istisnadır. |
| **MAF tipleri sarmalanmadı** | 2026-08-01 | `AIAgent`, `AgentSession`, `ChatMessage`, `AIFunction` doğrudan kullanılır. Paralel tip hiyerarşisi her MAF sürümünde bakım borcu üretir ve tüketiciyi MAF ekosisteminden koparır. AgentPrism bir kontrol düzlemidir, soyutlama katmanı değil. | MAF kırıcı bir API değişikliği yapar ve sarmalama tek çare kalırsa |
| **Trim/AOT analyzer'ları kök seviyede açılmadı** | 2026-08-01 | Denendi ve örnek uygulamayı kırdı: `app.MapGet(pattern, delegate)` `IL2026` + `IL3050` üretiyor, `TreatWarningsAsErrors` build'i kırıyor. Minimal API yönlendirmesi doğası gereği reflection kullanır. | ASP.NET Core minimal API tam AOT uyumlu hale gelirse |
| **Çalıştırma kaydı MAF middleware'i olarak yazılmadı** | 2026-08-01 | MAF middleware zinciri agent'a özgüdür ve `HarnessAgent` kendi iç dekoratörlerini ekler. Dış `DelegatingAIAgent` sarmalayıcısı harness dahil her agent tipinde aynı çalışır. | MAF agent tipinden bağımsız global bir middleware noktası sunarsa |

---

## 2. Kalıcı Mimari/Altyapı Kararları

| Karar | Tarih | Gerekçe | Yeniden açılma koşulu |
|---|---|---|---|
| **K-001 — Modüler paket ailesi + meta paket** *(kullanıcı kararı)* | 2026-08-01 | 7 paket: `Abstractions`, `Core`, `PostgreSql`, `OpenAI`, `AspNetCore`, `UI`, `AgentPrism` (meta). Bağımlılık grafiği tek yönlü ve döngüsüz. | — |
| **K-002 — Arayüz React 19 + TypeScript + Vite, assembly'ye gömülü** *(kullanıcı kararı)* | 2026-08-01 | Vite çıktısı `EmbeddedResource` olur, middleware ile sunulur. Tüketici projede sıfır JS bağımlılığı. gzip bütçesi 250 KB, CI kapısı. | — |
| **K-003 — Hibrit agent tanımı: kod + çalışma anı veritabanı** *(kullanıcı kararı)* | 2026-08-01 | `AddAIAgent` ile kodda tanımlananlar katalogda görünür; arayüzden tanımlananlar PostgreSQL'de saklanır ve çalışma anında derlenir. Ad çakışmasında **kod kazanır** — kod derleme zamanında doğrulanmıştır. | — |
| **K-004 — Veri erişimi: Npgsql + elden yazılmış SQL + gömülü migration runner** *(kullanıcı kararı)* | 2026-08-01 | Bkz. bölüm 1, "EF Core kullanılmadı". Migration'lar gömülü `.sql`, `pg_advisory_lock` ile korumalı, checksum doğrulamalı. | — |
| **K-005 — Hedef framework `net8.0;net9.0;net10.0`** *(kullanıcı kararı)* | 2026-08-01 | `Microsoft.Agents.AI` ve `Npgsql 10.0.3` dahil tüm bağımlılıkların üçünü de desteklediği doğrulandı. Tek `net10.0` erişimi gereksiz daraltırdı — üretimdeki projelerin büyük kısmı `net8.0` LTS'te. | — |
| **K-006 — Trim/AOT uyumu katman bazlı** | 2026-08-01 | `Abstractions`, `Core`, `PostgreSql`, `OpenAI` → AOT uyumlu. `AspNetCore`, `UI`, meta → değil. `src/Directory.Build.props` içindeki `AgentPrismAotCompatible` özelliği ile. Veremeyeceğimiz bir vaadi vermiyoruz. | — |
| **K-007 — Geçişli sabitleme kapalı** | 2026-08-01 | Bkz. bölüm 1. | — |
| **K-008 — Ön sürüm MAF bağımlılığı yalnız `AgentPrism.AspNetCore` içinde** | 2026-08-01 | `Microsoft.Agents.AI.Hosting` preview, `.Hosting.OpenAI` alpha. `Abstractions`, `Core`, `PostgreSql`, `OpenAI` yalnız GA paketlere bağlı. Sonuç: MAF GA'ya geçtiğinde tek pakette sürüm güncellemesi yeterli. AgentPrism o ana kadar `1.0.0-preview.N` yayınlanır. | Her iki paket GA olduğunda `1.0.0` yayınlanır |
| **K-009 — Sırlar `dotnet user-secrets` ile** *(kullanıcı kararı)* | 2026-08-01 | `appsettings.json` yalnız boş placeholder ve şema taşır. Bağlantı dizesi ve API anahtarı repoya hiç girmez. | — |
| **K-010 — Arayüz erişimi üç katmanlı** *(kullanıcı kararı)* | 2026-08-01 | 1) loopback varsayılan, 2) opsiyonel bearer token (sabit zamanlı karşılaştırma), 3) ASP.NET Core authorization policy kancası. `/api/meta` kimlik doğrulaması olmadan erişilir — arayüzün kimlik yöntemini öğrenmesi için; hassas veri içermez. | — |
| **K-011 — Klasör düzeni `src/` + `samples/` + `tests/`** *(kullanıcı kararı)* | 2026-08-01 | NuGet paket repolarının standart düzeni. Yeni paket eklemek düzeni bozmaz. | — |
| **K-012 — Tool'lar yalnız kodda tanımlanır** | 2026-08-01 | Bkz. bölüm 1. Güvenlik sınırı. | — |
| **K-013 — Ayrı `agentprism` PostgreSQL şeması** | 2026-08-01 | Tüketicinin `public` şemasına hiç dokunulmaz. Tablo adı çakışması, migration çakışması ve yanlışlıkla veri silme riski ortadan kalkar. | — |
| **K-014 — `run_events` append-only** | 2026-08-01 | Çalıştırma olayları güncellenmez, yalnız eklenir. `(run_id, seq)` birincil anahtar. Canlı akış (SSE) ve geçmişe dönük replay aynı kod yolundan geçer. | — |
| **K-015 — Birincil anahtarlar `uuid` v7** | 2026-08-01 | Zaman sıralı UUID. Rastgele UUID'nin B-tree index parçalanmasını önler, `bigserial`'ın merkezî sıra darboğazını getirmez. | — |
| **K-016 — Public API takibi Faz 7'ye ertelendi** | 2026-08-01 | Analyzer Faz 0'da kuruldu, `EnablePublicApiTracking=false` ile susturuldu. Faz 1–6 boyunca API yüzeyi hızla değişecek; her değişikliği kaydettirmek yayınlanmamış bir pakette hiçbir koruma sağlamaz. | — |
| **K-017 — Sürümleme MinVer ile git etiketinden** | 2026-08-01 | Elle sürüm düzenlemesi yok. Etiket yokken `0.0.0-preview.0`. | — |
| **K-018 — Bellek içi store'lar birinci sınıf implementasyon** | 2026-08-01 | `AddAgentPrism()` tek başına, veritabanı olmadan çalışır. Bu bir test yardımcısı değil, desteklenen bir moddur; sınırları (süreç ömrü, tek düğüm) dokümante edilir ve `/api/meta` aktif store tipini bildirir. | — |

---

## 3. Yeni Karar Ekleme Şablonu

Bir iş "kanıt yok → yapma" ile kapatıldığında veya kalıcı bir mimari tercih yapıldığında ilgili tabloya satır ekle:

```
| <karar — ne yapılmadı/yapıldı> | YYYY-AA-GG | <gerekçe — hangi kanıt/ölçüm/kısıt> | <hangi koşul gerçekleşirse yeniden açılır> |
```

Kurallar:
- Gerekçesiz karar ekleme — "istemedik" yeterli değil; neden istenmediği yazılmalı.
- Bir kalem yeniden açıldığında satırı silme; sonuna "**(yeniden açıldı: YYYY-AA-GG, sebep)**" ekle ve işi normal akışta planla.
- Kullanıcı kararlarını `(kullanıcı kararı)` etiketiyle işaretle — bunlar teknik kanıtla değil, ancak kullanıcıyla konuşularak değişir.

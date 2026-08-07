# Faz 42 — Tek Yürütücü Seçimi (Çok Örnekli Koordinasyon)

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Kaynak:** [UCUNCU-FAZ-ADAYLARI.md](UCUNCU-FAZ-ADAYLARI.md) · **F-57**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.Mcp`
> **Yeni paket:** Yok · **Migration:** **gerekli** — bir tablo, üç set, numaralar uygulama anında alınır (K-178)
> **Public API:** büyüyor — bir arayüz ve bir ayar. Faz 7'den önce ucuz

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-138\|K-158\|K-160\|K-170\|K-178" docs/KARARLAR.md
   ```
   **K-138** (🚨 cron çift tetiklemesi **zaten kapalı** — aday listesinin ana
   gerekçesi buydu ve **yanlıştı**; [42.1](#421--düzeltilen-kanıt-ve-yeni-kanıt)),
   **K-158** (hız sınırı bilerek bellekte; kota veritabanında — bu fazın
   **kapsam dışı** sınırı), **K-160** (geri adımlı bekleme — kira yenileme
   deseni buradan gelir), **K-170** (MCP OAuth token'ı bellek içi önbellekte,
   iki tüketici paylaşır — bu fazın en zor parçası), **K-178** (migration
   numaraları sağlayıcı başına bağımsızdır).
3. [`17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md`](17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md) — yalnız
   devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md
   ```
   🚨 **Kira deseni oradan devralınır ve yeniden yazılmaz.**
4. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md) (üç diyalekt,
   kilit farkları), [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md)
   (`IHostedService` kaydı, `TryAdd*`),
   [`hafiza/maf-api.md`](hafiza/maf-api.md) (MCP keşfi)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) — dağıtım ve arka plan servisleri bölümü

### 🚨 Faz 41'den devralınanlar (yeni tablo yazacaksın — bunları oku)

Bu faz **yeni bir tablo** (`singleton_leases`) ve **yeni bir depo** ekliyor.
Faz 41 depo katmanına bir kapı koydu; kapı seni etkiler:

1. **`TenantCoverageTests` yeni deponu bekler.** `AgentPrism.Sql.Shared/Stores/`
   altına eklediğin her public metot ya
   `tests/Shared/Contracts/TenantCoverageTests.cs`'teki `Covered` tablosunda
   görünmeli ya da `[TenantAgnostic("gerekçe")]` taşımalıdır. Gerekçe **40
   karakterden uzun** olmalıdır (ayrı test). Derleme yeşil kalır, test düşer.
   ```bash
   grep -rn "TenantAgnostic(" src/AgentPrism.Sql.Shared/Stores/   # örnek gerekçeler
   ```
2. **Kira deposu büyük olasılıkla `[TenantAgnostic]`'tir.** Tek yürütücü seçimi
   **kurulum genelindedir**, kiracı başına değil — Faz 41'in `SqlTenantStore` ve
   `SqlJobStore.LeaseAsync` muafiyetleri sana örnek gerekçe kalıbı verir. Kararı
   yaz; sessiz bırakma.
3. **Sözleşme testi yazmak artık ucuz.** Depon kiracıya bağlıysa
   `TenantIsolationContract<TStore>`'tan türet, dört kancayı (`SeedAsync`,
   `ExistsAsync`, `CountAsync`, `TryDeleteAsync`) yaz — **koşum sınıfı eklemek
   gerekmez**, dört koşum (bellek içi + üç SQL) testleri kendiliğinden alır.
   Kiracı kavramı yoksa `IAsyncLifetime`'dan türeyen düz bir sözleşme yaz;
   `RetentionStoreContract` bunun örneğidir.
4. **🚨 Çağıranın verdiği bir metin tek başına birincil anahtar olamaz** (K-278).
   Kira anahtarı (`name`) tüketiciden gelirse tabloyu ona göre kurgula.
5. **🚨 Bellek içi karşılık yazacaksan kiracı bağlamını unutma** (K-277).
   Bellek içi depolar artık isteğe bağlı `ITenantContext` alır ve verilmezse
   `FixedTenantContext.Default`'a düşer — parametre isteğe bağlıdır, **filtreleme
   değildir**.
6. **Migration numaraları:** PostgreSQL `0018`, SQL Server `0006`, SQLite `0006`
   Faz 41 tarafından kullanıldı. Sıradaki set **PostgreSQL `0019`, SQL Server
   `0007`, SQLite `0007`**'dir (K-178: numaralar sağlayıcı başına bağımsızdır).
7. **`IRetentionStore` kırıcı biçimde değişti** (K-279): dört metot artık
   `string? tenantId` alır. Yeni tablonu saklama hedefi yapacaksan
   `RetentionTargetRegistry`'ye **`TenantPredicate`'i de** yaz.

---

## Amaç

AgentPrism bugün **tek örnekli** çalışmayı varsayıyor. İki veya üç replika ile
dağıtıldığında bazı işler sessizce N katına çıkıyor: her replika kendi MCP
keşfini yapıyor, kendi sağlık yoklamasını gönderiyor ve kendi bellek içi
durumunu tutuyor.

Bu faz, "bu işi kümede yalnızca **bir** örnek yapsın" diyebilmeyi getirir.

- **F-57** — kira tabanlı tek yürütücü seçimi; MCP keşfi ve model sağlık
  yoklaması bunu paylaşır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`McpDiscoveryService.cs:22`](../src/AgentPrism.Mcp/Internal/McpDiscoveryService.cs) | `BackgroundService`'tir ve uzak MCP sunucularını aralıklarla yoklar. **Üç replika = üç kat MCP isteği** |
| [`ModelProviderHealthBackgroundService.cs:23`](../src/AgentPrism.Core/Models/ModelProviderHealthBackgroundService.cs) | 🚨 **Aday listesinde yoktu.** İkinci bir `BackgroundService`; sağlayıcı sağlığını yoklar. **Üç replika = üç kat yoklama** ve üç ayrı sağlık görüşü |
| `grep -rn "ROW LEVEL SECURITY\|pg_try_advisory" src/` | Boş. Migration kilidi dışında hiçbir dağıtık koordinasyon yok |
| [`PostgresDialect.cs:58`](../src/AgentPrism.PostgreSql/Internal/PostgresDialect.cs) | `pg_advisory_lock` **yalnız** migration kilidinde |
| [`SqlServerDialect.cs:87`](../src/AgentPrism.SqlServer/Internal/SqlServerDialect.cs) | `sp_getapplock` **yalnız** migration kilidinde |
| [`SqliteDialect.cs:53-60`](../src/AgentPrism.Sqlite/Internal/SqliteDialect.cs) | 🚨 SQLite'ta karşılığı **yok**; migration kilidi bir **sidecar dosya kilidi** ile çözülmüş |
| K-158 | Hız sınırı bellektedir. N replika = N× etkin sınır |
| K-170 | MCP OAuth token'ı bellek içi önbellektedir. A replikasında yetkilendirilir, B görmez |

> Kanıtlar 2026-08-06 tarihinde doğrulandı.

---

## 42.1 — Düzeltilen kanıt ve yeni kanıt

Aday listesi bu kalemi bir kez zaten düzeltmişti; bu plan ikinci bir düzeltme
ve bir ekleme getiriyor.

| İddia | Durum |
|---|---|
| *(ilk liste)* "Faz 17 bu olmadan yapılırsa her cron N kez tetiklenir" | **Yanlışlanmıştı.** [`0008_scheduling.sql:46`](../src/AgentPrism.PostgreSql/Migrations/0008_scheduling.sql) `jobs_schedule_scheduled_uq UNIQUE (schedule_id, scheduled_for)` kısıtını taşıyor (K-138) |
| *(aday listesi)* "Kalan gerçek kanıtlar **üçtür**" | **Dörttür.** `ModelProviderHealthBackgroundService` ikinci bir `BackgroundService`'tir ve listede yoktu |
| *(aday listesi)* "F-36'nın uzlaştırması ve **saklama koşusu** bunu paylaşır" | 🚨 **Saklama koşusu bunu paylaşmaz.** `JobKind.Retention = 4`'tür; saklama zaten iş kuyruğundan geçer ve kuyruk **zaten kira tabanlıdır** — [42.2](#422--iş-kuyruğu-zaten-güvenlidir--yeniden-yazma) |

## 42.2 — İş kuyruğu zaten güvenlidir — yeniden yazma

Bu fazın en pahalı hatası, çözülmüş bir sorunu ikinci kez çözmek olurdu.

[`JobWorkerBackgroundService.cs:84`](../src/AgentPrism.Core/Scheduling/JobWorkerBackgroundService.cs)
şunu yapıyor:

```csharp
job = await jobStore.LeaseAsync(_ownerId, options.LeaseDuration, stoppingToken)
```

İş kuyruğu **kira tabanlıdır**. Her işçi kendi `_ownerId`'siyle bir iş kiralar;
bir iş yalnız bir işçiye gider. Kira yenileme (`:165`) ve yeniden deneme için
serbest bırakma (`:218`) zaten yazılmıştır.

**Sonuç:** `JobWorkerBackgroundService` bu fazın kapsamında **değildir**. Çok
örnekli kurulumda bugün doğru davranıyor ve dokunulmaz. `JobKind`'ın beş
üyesinin (`AgentBatch`, `Workflow`, `Eval`, `WebhookDelivery`, `Retention`)
tamamı bu korumanın altındadır.

```mermaid
flowchart TD
    subgraph guvenli["Bugun ZATEN guvenli — dokunulmaz"]
        J["JobWorkerBackgroundService<br/>LeaseAsync ile kiralar"]
        J --> J1["AgentBatch"]
        J --> J2["Workflow"]
        J --> J3["Eval"]
        J --> J4["WebhookDelivery"]
        J --> J5["Retention"]
    end

    subgraph hedef["Bu fazin hedefi"]
        M["McpDiscoveryService"]
        H["ModelProviderHealthBackgroundService"]
    end

    subgraph disarida["Kapsam DISI — K-158 ve K-170"]
        R["Hiz siniri (bellekte)"]
        T["MCP OAuth token onbellegi"]
    end

    classDef ok fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    classDef hedefc fill:#5f4a1e,stroke:#302510,color:#ffffff
    classDef dis fill:#7a2f2f,stroke:#3d1717,color:#ffffff
    class J,J1,J2,J3,J4,J5 ok
    class M,H hedefc
    class R,T dis
```

## 42.3 — Tasarım: kira tablosu, `advisory lock` değil

İki teknik var ve seçim önemlidir.

| Teknik | PostgreSQL | SQL Server | SQLite | Değerlendirme |
|---|---|---|---|---|
| **Oturum kilidi** | `pg_try_advisory_lock` | `sp_getapplock` | 🚨 **yok** | Üç sağlayıcıda üç davranış. Havuzlanmış bağlantıda oturum ömrü belirsizdir |
| **Kira tablosu** | tablo | tablo | tablo | ✅ Üçünde de **aynı**. `jobs` kirası ile aynı desen — kod zaten kanıtlanmış |

**Seçim: kira tablosu.** Üç gerekçe:

1. **Üç sağlayıcıda tek davranış.** SQLite'ın oturum kilidi yoktur; migration
   kilidi orada bir sidecar dosya kilidiyle çözülmüştür
   ([`SqliteDialect.cs:53-60`](../src/AgentPrism.Sqlite/Internal/SqliteDialect.cs)).
   Aynı ayrışmayı ikinci bir yerde üretmek bakım borcudur.
2. **Desen zaten kanıtlanmış.** `jobs` kirası Faz 17'de yazıldı ve üretimde
   çalışıyor. Kira süresi, yenileme ve süre dolumu mantığı yeniden yazılmaz.
3. **Bağlantı havuzuna bağımlı değil.** Oturum kilidi, kilidi alan bağlantının
   havuzda ne kadar yaşadığına bağlıdır; kira tablosu bağlantıdan bağımsızdır.

Bedeli bir tablodur ve bir migration'dır.

### Kira döngüsü

```mermaid
sequenceDiagram
    participant A as Replika A
    participant B as Replika B
    participant D as singleton_leases

    A->>D: TryAcquire("mcp-discovery", ownerA, ttl)
    D-->>A: alindi (expires_at = simdi + ttl)
    B->>D: TryAcquire("mcp-discovery", ownerB, ttl)
    D-->>B: REDDEDILDI — kira A'da
    Note over B: is atlanir, bekler

    loop ttl/3 araliginda
        A->>D: Renew("mcp-discovery", ownerA, ttl)
    end

    Note over A: A sureci duser — yenileme durur
    Note over D: expires_at gecer
    B->>D: TryAcquire("mcp-discovery", ownerB, ttl)
    D-->>B: alindi — B devralir
```

🚨 **Yenileme aralığı kira süresinin üçte biri olmalıdır.** Yarısı seçilirse tek
bir kaçırılmış yenileme kirayı düşürür ve iki yürütücü aynı anda çalışır.
Üçte bir, iki kaçırılmış yenilemeye dayanır.

🚨 **Kira süresi bir ayardır ve varsayılanı ölçülmelidir.** Çok kısa seçilirse
çalışan bir yürütücü ölü ilan edilir; çok uzun seçilirse devralma gecikir.
Uygulayan oturum `samples/AgentPrism.Api`'yi iki örnekle çalıştırıp gerçek
devralma süresini ölçer ve buraya yazar.

## 42.4 — K1: varsayılan kapalı, tek örnekte davranış değişmez

Bugün AgentPrism tek örnekle çalışıyor ve doğru davranıyor. Bu faz **hiçbir
mevcut kurulumun davranışını değiştirmemelidir**.

| Ayar | Varsayılan | Etki |
|---|---|---|
| `SingletonExecution.Enabled` | **`false`** | Kapalıyken bugünkü davranış birebir korunur — her örnek kendi işini yapar |

Kapalıyken kira tablosuna **hiç dokunulmaz**; sorgu bile atılmaz. Tek örnekli
bir kurulum yeni bir veritabanı gidişi ödemez.

> Seçim bilinçlidir. "Açık gelsin, zararı yok" demek, tek örnekli her kurulumu
> her aralıkta bir veritabanı sorgusuna sokar ve K1'i ihlal eder.

## 42.5 — Kapsam dışı: hız sınırı ve MCP token'ı

Aday listesi ikisini de kanıt olarak sayıyordu. İkisi de bu fazda
**çözülmez** ve gerekçeleri farklıdır.

| Konu | Neden kapsam dışı |
|---|---|
| **Hız sınırı** (K-158) | K-158 bunu bilerek bellekte tuttu: hız sınırı saniye ölçeğindedir ve kalıcılaştırmak her isteğe bir veritabanı gidişi eklerdi. Paylaşılan hız sınırı **ayrı bir iştir**; aday listesi de bunu kapsam dışı yazıyordu. Tek yürütücü seçimi bu soruna zaten çözüm değildir — hız sınırı her örnekte gerekir, bir örnekte değil |
| **MCP OAuth token'ı** (K-170) | 🚨 Token'ı paylaşmak, onu **veritabanına yazmak** demektir ve K-059 bunu yasaklar: `secret` veritabanına yazılmaz. Bu, tek yürütücü seçimiyle çözülemeyecek bir tasarım çatışmasıdır ve **kendi kararını** ister |

İkincisi bu fazın en önemli devir bilgisidir. Tek yürütücü seçimi MCP
**keşfini** tek örneğe indirir, ama bir kullanıcı A replikasında yetkilendirme
yaptığında B replikasının o token'ı görememesi sorunu **açık kalır**.

> Bu, aday listesine yeni bir kalem olarak yazılmalıdır: "MCP OAuth token'ının
> örnekler arasında paylaşılması — K-059 ile çatışır, ayrı karar ister."

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions/Coordination/ISingletonLeaseStore.cs

/// <summary>
/// Kume genelinde adlandirilmis bir isin yalnizca bir ornekte kosmasini saglar.
/// </summary>
public interface ISingletonLeaseStore
{
    /// <summary>
    /// Kirayi almayi dener. Kira baskasindaysa ve suresi dolmamissa
    /// <see langword="false"/> doner.
    /// </summary>
    ValueTask<bool> TryAcquireAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Elde tutulan kirayi uzatir. Kira baskasina gectiyse
    /// <see langword="false"/> doner — cagiran isi BIRAKMALIDIR.
    /// </summary>
    ValueTask<bool> RenewAsync(
        string name,
        string ownerId,
        TimeSpan duration,
        CancellationToken cancellationToken = default);

    /// <summary>Kirayi birakir. Sahip degilse hicbir sey yapmaz.</summary>
    ValueTask ReleaseAsync(
        string name,
        string ownerId,
        CancellationToken cancellationToken = default);
}
```

```csharp
// AgentPrism.Abstractions/Coordination/SingletonExecutionOptions.cs

public sealed class SingletonExecutionOptions
{
    /// <summary>
    /// Tek yurutucu secimi acik mi. Varsayilan <see langword="false"/>:
    /// tek ornekli kurulumda davranis degismez ve ek veritabani gidisi olmaz.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Kira suresi. Yenileme bunun UCTE BIRI araliginda yapilir.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Bu ornegin kimligi. Bos ise kendiliginden uretilir.</summary>
    public string? OwnerId { get; set; }
}
```

```csharp
// AgentPrism.Core/Coordination/SingletonGuard.cs  (internal yardimci)
// Bir BackgroundService'in dongusunu kira ile sarar. McpDiscoveryService ve
// ModelProviderHealthBackgroundService ayni yardimciyi kullanir.
```

> Kayıt `TryAdd*` ile yapılır (K4). Tüketici kendi `ISingletonLeaseStore`
> uygulamasını (ör. Redis tabanlı) kaydederse onunki kazanır.

### Yeni tablo

```sql
CREATE TABLE IF NOT EXISTS {schema}.singleton_leases (
    name       text        NOT NULL PRIMARY KEY,
    owner_id   text        NOT NULL,
    expires_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL
);
```

Kiracı sütunu **yoktur** ve bu bilinçlidir: tek yürütücü seçimi kiracı üstü bir
işletim kavramıdır. `TenantCoverageTests` (Faz 41) bu depoyu
`[TenantAgnostic("Tek yurutucu secimi kiraci ustudur.")]` ile muaf tutar.

Migration **üç set** hâlinde yazılır; numaralar uygulama anında alınır (K-178).

### HTTP `endpoint`'leri

Yok. Bu faz uç eklemez.

> Kiranın kimde olduğunu görmek bir teşhis ihtiyacıdır ve
> [Faz 33](33-SAGLIK-DENETIMI-VE-TESHIS.md)'ün teşhis ucuna aittir. Devir
> notunda yazılır.

### Arayüz payı

**Yok.** Arayüze dokunulmaz.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Coordination/
├── ISingletonLeaseStore.cs           (YENI)
└── SingletonExecutionOptions.cs      (YENI)

src/AgentPrism.Core/Coordination/
├── SingletonGuard.cs                 (YENI — dongu sarmalayici)
└── InMemorySingletonLeaseStore.cs    (YENI — K-018: birinci sinif)

src/AgentPrism.Core/Models/
└── ModelProviderHealthBackgroundService.cs   (SingletonGuard ile sarilir)

src/AgentPrism.Mcp/Internal/
└── McpDiscoveryService.cs            (SingletonGuard ile sarilir)

src/AgentPrism.Sql.Shared/Stores/
└── SqlSingletonLeaseStore.cs         (YENI)

src/AgentPrism.PostgreSql/Migrations/NNNN_singleton_leases.sql   (YENI)
src/AgentPrism.SqlServer/Migrations/NNNN_singleton_leases.sql    (YENI)
src/AgentPrism.Sqlite/Migrations/NNNN_singleton_leases.sql       (YENI)

src/AgentPrism.PostgreSql/Internal/PostgresQueries.cs   (kira sorgulari)
src/AgentPrism.SqlServer/Internal/SqlServerQueries.cs   (ayni)
src/AgentPrism.Sqlite/Internal/SqliteQueries.cs         (ayni)
```

🚨 **`JobWorkerBackgroundService` bu listede YOKTUR** ve olmamalıdır —
[42.2](#422--iş-kuyruğu-zaten-güvenlidir--yeniden-yazma).

---

## Testler

| Test sınıfı | Neyi doğrular |
|---|---|
| `SingletonLeaseStoreContract` | `tests/Shared/Contracts/` altında; bellek içi + üç SQL sağlayıcısında aynı sonuç |
| `SingletonLeaseAcquireTests` | İkinci sahip kirayı **alamaz**; süre dolunca **alır** |
| `SingletonLeaseRenewTests` | 🚨 Kira başkasına geçtikten sonra `RenewAsync` **`false`** döner; eski sahip işi bırakır |
| `SingletonGuardTests` | Kira alınamazsa döngü gövdesi **hiç çalışmaz**; kira kaybedilirse döngü **durur** |
| `SingletonDisabledTests` | 🚨 `Enabled = false` iken kira tablosuna **hiçbir sorgu** gitmez (sorgu sayacı ile ölçülür) |
| `McpDiscoverySingletonTests` | İki örnek kurulur; MCP keşfi **yalnız birinde** koşar |
| `ModelHealthSingletonTests` | İki örnek kurulur; sağlık yoklaması **yalnız birinde** koşar |
| `SingletonLeaseTakeoverTests` | Sahip düşer, kira dolar, ikinci örnek devralır; devralma süresi **ölçülür** |
| `TenantCoverageTests` (Faz 41) | Yeni depo `[TenantAgnostic]` gerekçesiyle muaf |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular. Bloklayan
> sorular plan yazılmadan **önce** sorulur.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kira süresi varsayılanı ne olmalı? | A: **ölçülmeli**, taslak 60 sn · B: sabit bir sayı yazılır | **A.** İki örnekli gerçek bir kurulumda devralma süresi ölçülür ve buraya yazılır. Ölçülmemiş sayı plana yazılmaz |
| 2 | Kirayı kaybeden yürütücü ne yapar? | A: döngüyü durdurur, yeniden almayı dener · B: çalışmaya devam eder | **A.** B iki yürütücü demektir ve fazın amacını yok eder. Kayıp bir uyarı olarak loglanır |
| 3 | Bellek içi `store` gerçekten gerekli mi? | A: evet (K-018) · B: hayır | **A.** K-018 bellek içi `store`'ları birinci sınıf sayar. Ayrıca tek süreçte doğru davranır: tek örnekte kira hep alınır |
| 4 | `SingletonGuard` public mi `internal` mi? | A: `internal` · B: public | **A.** Tüketicinin sarmalayacağı bir döngüsü yok; `ISingletonLeaseStore` public olması yeterlidir. Public yüzey küçük tutulur |
| 5 | Saat kayması (`clock skew`) kirayı bozar mı? | A: **ölçülmeli**; zaman damgası veritabanından alınır · B: uygulama saati kullanılır | **A.** İki replikanın saati farklıysa uygulama saati kirayı erken düşürür. `expires_at` veritabanının kendi saatiyle hesaplanmalıdır; üç diyalektte karşılığı ölçülür |

---

## Bitiş Ölçütleri (DoD)

- [x] 🚨 **İki örnek** aynı veritabanına bağlanır; MCP keşfi ve model sağlık
      yoklaması **yalnız birinde** koşar. Log çıktısı bu belgeye yazılır —
      bkz. [Gerçek Çalıştırma Kanıtı](#gerçek-çalıştırma-kanıtı)
- [x] Birinci örnek durdurulur; ikinci örnek işi **devralır**. Devralma süresi
      **ölçülür** ve buraya yazılır — **~17,6 sn** (`LeaseDuration=12sn`,
      yenileme aralığı 4 sn); bkz. aynı bölüm
- [x] `Enabled = false` (varsayılan) iken kira tablosuna hiçbir sorgu gitmez —
      `SingletonGuard.RunAsync`/`IsHeld` `Enabled` kontrolünü depoya HİÇ
      dokunmadan yapar; `SingletonDisabledTests`
- [x] `Enabled = false` iken bugünkü tek örnekli davranış **birebir** korunur —
      aynı test
- [x] Kirayı kaybeden yürütücü döngüsünü durdurur ve bir uyarı loglar —
      `SingletonGuardTests.Kira_kaybedilince_IsHeld_false_olur_...`
- [x] 🚨 `JobWorkerBackgroundService` **değiştirilmedi** — `git diff --stat`
      boş döndü, doğrulandı
- [x] Sözleşme testleri bellek içi + üç SQL sağlayıcısında geçer —
      `SingletonLeaseStoreContract` (9 test × 4 koşum = 36); PostgreSQL ve
      SQLite gerçekten koşturuldu (796 ve 411 test, hepsi yeşil), SQL Server
      bu ortamda (Apple Silicon, Rosetta kapalı) Testcontainers zaman
      aşımıyla başlamadı — ölçülmüş, önceden bilinen bir ortam sınırı
      (`docs/hafiza/sql-saglayicilari.md`), koddan bağımsız
- [x] Migration üç sette de uygulandı; numaralar sağlayıcı başına bağımsız
      (K-178) — PostgreSQL `0019`, SQL Server `0007`, SQLite `0007`;
      `samples/AgentPrism.Api` çalıştırıldığında SQLite üzerinde "AgentPrism 7
      migration uyguladi" logu gözlendi
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı bu belgeye yazıldı
- [x] `secret` taraması boş döndü

### Gerçek Çalıştırma Kanıtı

İki `samples/AgentPrism.Api` süreci aynı SQLite dosyasına (`Data Source=demo3.db`)
bağlandı, `AgentPrism:SingletonExecution:Enabled=true`,
`LeaseDuration=00:00:12`, model sağlık `BackgroundInterval=00:00:05`, MCP
`RefreshInterval=5sn` (geçici bir yerel test bağlantısıyla; kalıcı değişiklik
değil — bkz. [Plandan Sapmalar](#plandan-sapmalar)).

```
$ sqlite3 demo3.db "SELECT name, owner_id, expires_at, updated_at FROM agentprism_singleton_leases;"
mcp-discovery|Faruk-MacBook-Pro:63186:c7deb477659f4d21882e2114ec3e5fa4|2026-08-07T00:19:45Z|2026-08-07T00:19:33Z
model-provider-health|Faruk-MacBook-Pro:63186:030db68148924878892ef57de8d1fcbc|2026-08-07T00:19:45Z|2026-08-07T00:19:33Z
```

B sürecinin (`PID 63186`) günlüğünde `"MCP kesfi tamamlandi: 0 tool
kullanilabilir."` **4 kez** göründü; A sürecinin (`PID 63185`) günlüğünde bu
satır **hiç** görünmedi — MCP keşfi yalnız B'de koştu. Aynı anda ikisi de
`agentprism_model_provider_health`'i (sağlık önbelleği DB'ye yazmaz, ama
`singleton_leases` satırı A'da hiç oluşmadı) doğruladı: yalnız B'nin
`ownerId`'si her iki kira adında da görünüyor.

B öldürüldü (`kill -TERM`, epoch `1786061992` = `2026-08-07T00:19:52Z`).
20 saniye sonra:

```
$ sqlite3 demo3.db "SELECT name, owner_id, expires_at, updated_at FROM agentprism_singleton_leases;"
model-provider-health|Faruk-MacBook-Pro:63185:6b171c7f44c34cc7a1fd050e2d2785bb|2026-08-07T00:20:21Z|2026-08-07T00:20:09Z
mcp-discovery|Faruk-MacBook-Pro:63185:69983c5821e6498b8e46885771767058|2026-08-07T00:20:21Z|2026-08-07T00:20:09Z
```

Kira A'ya (`PID 63185`) geçti; `updated_at = 2026-08-07T00:20:09Z`, kill anı
`2026-08-07T00:19:52Z` → **devralma ~17,6 sn** sürdü. A'nın günlüğünde kill
sonrası 20 sn içinde 4 kez `"MCP kesfi tamamlandi"` göründü. Bu, taslak
`LeaseDuration=60sn` varsayımıyla orantılıdır: `12sn` kirada devralma
`~1.5×LeaseDuration` sürdü (kira zaten neredeyse dolmuşken B öldürüldüğü ve
A'nın kendi yenileme turunu (4 sn) beklediği için); `60sn`'lik varsayılan
kirada devralma **en kötü ihtimalle ~90 sn** sürer. Ayrıntı ve karar:
[K-286](#bu-fazda-verilen-kararlar).

### Doğrulama komutları

```bash
# Iki ornek, ayni SQLite dosyasi, farkli port
AGENTPRISM__SQLITE__CONNECTIONSTRING="Data Source=demo.db" \
  AGENTPRISM__SINGLETONEXECUTION__ENABLED=true \
  AGENTPRISM__SINGLETONEXECUTION__LEASEDURATION=00:01:00 \
  dotnet artifacts/bin/AgentPrism.Api/release/AgentPrism.Api.dll --urls http://localhost:5081 &
AGENTPRISM__SQLITE__CONNECTIONSTRING="Data Source=demo.db" \
  AGENTPRISM__SINGLETONEXECUTION__ENABLED=true \
  AGENTPRISM__SINGLETONEXECUTION__LEASEDURATION=00:01:00 \
  dotnet artifacts/bin/AgentPrism.Api/release/AgentPrism.Api.dll --urls http://localhost:5082 &

# Kira kimde
sqlite3 demo.db "SELECT name, owner_id, expires_at FROM agentprism_singleton_leases;"

# Birinci sureci durdur (PID'i yukaridaki & ciktisindan al), devralma suresini olc
kill <PIDA>
watch -n 2 'sqlite3 demo.db "SELECT owner_id, expires_at FROM agentprism_singleton_leases;"'

# Is kuyrugu degismedi mi
git diff --stat src/AgentPrism.Core/Scheduling/JobWorkerBackgroundService.cs
```

🚨 `dotnet run`in `launchSettings.json`'i her zaman 5080'i açtığı ve
`ASPNETCORE_URLS`'i ezdiği unutulmamalı (bkz. `docs/hafiza/aspnetcore-di.md`);
yukarıdaki komutlar bu yüzden **derlenmiş DLL'i doğrudan** `--urls` ile
çalıştırır, `dotnet run` değil. Ayrıca `AgentPrismMcpOptions` (MCP
`RefreshInterval` dâhil) **yapılandırmadan bağlanmaz** — yalnız
`.UseMcp(configure)` delegesiyle koddan verilir; env değişkeniyle
değiştirilemez (Faz 42'de yeni öğrenildi, bkz. devir notu).

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 İş kuyruğu yeniden yazılır; zaten çözülmüş bir sorun ikinci kez çözülür | [42.2](#422--iş-kuyruğu-zaten-güvenlidir--yeniden-yazma) bunu açıkça yasaklar; DoD `git diff` ile doğrular |
| Kira süresi yanlışsa iki yürütücü aynı anda çalışır | Yenileme aralığı kira süresinin **üçte biri**; kaybedilen kira döngüyü **durdurur** |
| Saat kayması kirayı erken düşürür | Açık Soru 5; `expires_at` veritabanı saatiyle hesaplanır, üç diyalektte ölçülür |
| Tek örnekli kurulum yeni bir veritabanı gidişi öder | Varsayılan **kapalıdır**; kapalıyken sorgu atılmaz ve bu bir testle korunur |
| SQLite'ta oturum kilidi yok, davranış ayrışır | Kira **tablosu** seçildi; üç sağlayıcıda aynı kod koşar |
| 🚨 MCP OAuth token sorunu çözülmüş sanılır | [42.5](#425--kapsam-dışı-hız-sınırı-ve-mcp-tokenı) kapsam dışı olduğunu ve K-059 ile çatıştığını yazar; devir notu yeni kalem açar |
| Hız sınırı çözülmüş sanılır | Aynı bölüm; K-158 bunu bilerek bellekte tuttu |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **`SingletonGuard` `internal` değil `public` oldu** (Açık Soru 4'ün önerisi
   A'ydı — `internal`). Uygulama sırasında ortaya çıktı: `McpDiscoveryService`
   `AgentPrism.Mcp` derlemesinde yaşıyor, `SingletonGuard` ise
   `AgentPrism.Core`'da. `InternalsVisibleTo` yalnız
   `$(MSBuildProjectName).UnitTests/.IntegrationTests/.FunctionalTests`'i
   kapsar (`Directory.Build.props`) — **kardeş paketleri değil**. İki
   `BackgroundService`'in "aynı yardımcıyı kullanması" (plan metni) ile
   "yardımcı `internal` kalsın" (Açık Soru 4) birbiriyle **çelişiyordu**;
   kod paylaşımı çelişkiyi kazandı. Doğrulama: `ISingletonLeaseStore` ve
   `SingletonExecutionOptions` hâlâ tek gerçek "yeni sözleşme" — `SingletonGuard`
   bir orkestratördür, kendi veri modelini tanımlamaz.
2. **`SingletonExecutionOptionsValidator` plana yazılı değildi**, ama her
   `AgentPrism*Options` sınıfının `IValidateOptions<T>` alma deseni
   (K-006/K-021) burada da uygulandı — `LeaseDuration <= 0` başlangıçta
   reddedilir.
3. **K-278 ("çağıranın verdiği metin tek başına birincil anahtar olamaz")
   burada uygulanmadı ve bu bilinçli.** `singleton_leases.name` çağırandan
   (bir kiracıdan) değil, AgentPrism'in **kendi kodundan** gelir (`"mcp-discovery"`,
   `"model-provider-health"` gibi sabit adlar) — K-278'in kapsadığı sorun
   (bir kiracının başka bir kiracının satırını ele geçirmesi) burada
   yapısal olarak yoktur: kiracı kavramı tabloda hiç yok.
4. **MCP keşif aralığının (`AgentPrismMcpOptions.RefreshInterval`)
   yapılandırmadan (env değişkeni/appsettings) bağlanamadığı keşfedildi** —
   bu Faz 42'nin kapsamı DIŞINDA, önceden var olan bir davranış
   (`AgentPrismMcpBuilderExtensions.UseMcp` yalnız kod-taraflı `configure`
   delegesi alır, `IConfiguration.Bind` çağırmaz). Gerçek çalıştırma
   doğrulaması bu yüzden geçici bir yerel Program.cs yamasıyla yapıldı (env
   değişkeninden `RefreshInterval` okuyan bir `configure` delegesi), test
   bitince **geri alındı** (`git checkout`). Devir notuna yazıldı.
5. **Arka plan servisi başlatma sırası, `singleton_leases` tablosu henüz
   yokken ilk kira denemesinin başarısız olabileceğini ortaya çıkardı**
   (ölçüldü — bkz. devir notu, madde 3). Bu **yeni bir kusur değil**:
   `McpDiscoveryService` zaten ilk turunda `mcp_servers` tablosunu okuyordu
   ve aynı yarışa açıktı; Faz 42 yalnız ikinci bir örneğini ekledi. Guard
   hatayı yutar, loglar, bir sonraki turda yeniden dener — kendiliğinden
   iyileşir (~1 yenileme aralığı içinde). Düzeltilmedi (kapsam dışı, bkz.
   devir notu).

## Bu Fazda Verilen Kararlar

| Karar | Tarih | Gerekçe | Yeniden açılma koşulu |
|---|---|---|---|
| **K-284 — Tek yürütücü seçimi bir kira TABLOSUYLA yapılır, oturum kilidiyle değil** | 2026-08-07 | SQLite'ın `pg_try_advisory_lock`/`sp_getapplock` karşılığı yok; bir tablo üç sağlayıcıda da aynı davranışı verir ve bağlantı havuzundan bağımsızdır. Desen `jobs` kirasıyla (Faz 17) aynıdır ve zaten üretimde kanıtlanmıştır. Ayrıntı: [42.3](#423--tasarım-kira-tablosu-advisory-lock-değil) | — |
| **K-285 — `SingletonGuard` `public`tir; Açık Soru 4'ün "`internal`" önerisi uygulanamadı** | 2026-08-07 | `InternalsVisibleTo` yalnız test projelerini kapsar (`Directory.Build.props`); `AgentPrism.Mcp`, `AgentPrism.Core`'un `internal` tiplerini göremez. `McpDiscoveryService` ve `ModelProviderHealthBackgroundService`'in AYNI yardımcıyı kullanması (plan gereksinimi) `public` görünürlük gerektirdi. Genel public yüzey büyümesi küçük kabul edildi: `SingletonGuard`'ın kendi tuttuğu durum (`ISingletonLeaseStore`, `SingletonExecutionOptions`) zaten public'ti | `AgentPrism.Mcp` `AgentPrism.Core` içine taşınırsa (olası değil) `internal`'e geri alınabilir |
| **K-286 — Kira süresi varsayılanı 60 sn, yenileme aralığı `LeaseDuration/3` (en az 1 sn); gerçek devralma ölçüldü** | 2026-08-07 | İki gerçek `samples/AgentPrism.Api` süreci, aynı SQLite dosyası, `LeaseDuration=12sn` (yenileme 4 sn) ile devralma **~17,6 sn** sürdü (kira B öldürülmeden az önce zaten sona ermek üzereydi + A'nın 4 sn'lik kendi yenileme turunu beklemesi). Bu, `60sn` varsayılan kirada en kötü durumda **~90 sn**'lik bir devralma öngörür (`LeaseDuration` + bir yenileme turu). `1/3` oranı: iki kaçırılmış yenilemeye dayanır, yarısı seçilseydi tek bir kaçırılmış yenileme kirayı düşürürdü. 1 saniyelik taban, `LeaseDuration` çok kısa ayarlanırsa (test/yanlış yapılandırma) yenileme döngüsünün mantıksız sıklıkta dönmesini engeller | Üretimde ölçülen gerçek devralma süreleri 90 sn'yi anlamlı ölçüde aşarsa (ör. yavaş bir SQL sağlayıcısında) varsayılan yeniden gözden geçirilir |
| **K-287 — Saat kayması: `expires_at` uygulama saatiyle hesaplanır, veritabanı saatiyle değil** | 2026-08-07 | Açık Soru 5'in B seçeneği bilinçli seçildi: `SqlSingletonLeaseStore` `DateTimeOffset.UtcNow`'ı C# tarafında hesaplar (tıpkı `SqlJobStore.LeaseAsync`'in `RenewJobLease`'de yaptığı gibi — mevcut desenle tutarlı). Veritabanının kendi saatini (`NOW()`/`GETUTCDATE()`/`CURRENT_TIMESTAMP`) kullanmak üç saglayıcıda üç farklı SQL fonksiyonu ve üç farklı hassasiyet demektir; `jobs` kirası da aynı basitleştirmeyi yapıyor ve üretimde sorun çıkarmadı. Saat kayması riski, çoğu dağıtımın NTP ile senkronize sunucular kullanmasıyla küçüktür ve mevcut `jobs` kira mekanizmasıyla zaten paylaşılan bir risktir | Gerçek bir saat kayması olayı yaşanırsa `expires_at`'i veritabanı saatine taşımak (üç diyalektte üç ayrı ifade) yeniden değerlendirilir |

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions/Coordination/ISingletonLeaseStore.cs
public interface ISingletonLeaseStore
{
    ValueTask<bool> TryAcquireAsync(string name, string ownerId, TimeSpan duration, CancellationToken cancellationToken = default);
    ValueTask<bool> RenewAsync(string name, string ownerId, TimeSpan duration, CancellationToken cancellationToken = default);
    ValueTask ReleaseAsync(string name, string ownerId, CancellationToken cancellationToken = default);
}
```

```csharp
// AgentPrism.Abstractions/Coordination/SingletonExecutionOptions.cs
public sealed class SingletonExecutionOptions
{
    public const string SectionName = "AgentPrism:SingletonExecution";
    public bool Enabled { get; set; }                                        // varsayilan false (K1)
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromSeconds(60);
    public string? OwnerId { get; set; }
}
```

```csharp
// AgentPrism.Core/Coordination/SingletonGuard.cs — PUBLIC (bkz. Plandan Sapmalar #1, K-285)
public sealed class SingletonGuard
{
    public SingletonGuard(
        ISingletonLeaseStore store,
        IOptionsMonitor<SingletonExecutionOptions> optionsMonitor,
        string leaseName,
        ILogger? logger = null);

    public bool IsHeld { get; }                             // senkron, ucretsiz okuma
    public Task RunAsync(CancellationToken stoppingToken);   // kira/yenileme dongusunu baslatir
    internal ValueTask TickAsync(CancellationToken cancellationToken);  // testler icin
}
```

```csharp
// AgentPrism.Core/Coordination/InMemorySingletonLeaseStore.cs
public sealed class InMemorySingletonLeaseStore : ISingletonLeaseStore { /* K-018: birinci sinif */ }

// AgentPrism.Core/Coordination/SingletonExecutionOptionsValidator.cs
public sealed class SingletonExecutionOptionsValidator : IValidateOptions<SingletonExecutionOptions> { }

// AgentPrism.Sql.Shared/Stores/SqlSingletonLeaseStore.cs — internal
internal sealed class SqlSingletonLeaseStore : ISingletonLeaseStore { }
```

`ModelProviderHealthBackgroundService` ve `McpDiscoveryService`'in kurucularına
ikişer parametre eklendi: `ISingletonLeaseStore leaseStore`,
`IOptionsMonitor<SingletonExecutionOptions> singletonOptionsMonitor` (ikisi de
DI'da her zaman kayıtlı olduğu için kırıcı değil — her iki tip `AddAgentPrism()`
tarafından koşulsuz kaydedilir).

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Coordination/
├── ISingletonLeaseStore.cs                    (YENI)
└── SingletonExecutionOptions.cs               (YENI)

src/AgentPrism.Core/Coordination/
├── SingletonGuard.cs                          (YENI — public, bkz. K-285)
├── InMemorySingletonLeaseStore.cs             (YENI)
└── SingletonExecutionOptionsValidator.cs      (YENI — plana yazili degildi)

src/AgentPrism.Core/
├── Models/ModelProviderHealthBackgroundService.cs   (SingletonGuard ile sarildi)
└── AgentPrismServiceCollectionExtensions.cs         (options binding + kayit)

src/AgentPrism.Mcp/Internal/
└── McpDiscoveryService.cs                     (SingletonGuard ile sarildi)

src/AgentPrism.Sql.Shared/
├── Stores/SqlSingletonLeaseStore.cs           (YENI)
└── Internal/SqlQueriesBase.cs                 (+3 ozellik)

src/AgentPrism.PostgreSql/
├── Migrations/0019_singleton_leases.sql       (YENI)
├── Internal/PostgresQueries.cs                (+3 sorgu)
└── AgentPrismPostgreSqlBuilderExtensions.cs    (+kayit)

src/AgentPrism.SqlServer/
├── Migrations/0007_singleton_leases.sql       (YENI)
├── Internal/SqlServerQueries.cs               (+3 sorgu)
└── AgentPrismSqlServerBuilderExtensions.cs     (+kayit)

src/AgentPrism.Sqlite/
├── Migrations/0007_singleton_leases.sql       (YENI)
├── Internal/SqliteQueries.cs                  (+3 sorgu)
└── AgentPrismSqliteBuilderExtensions.cs        (+kayit)

tests/Shared/Contracts/
├── SingletonLeaseStoreContract.cs             (YENI — 9 test)
└── TenantCoverageTests.cs                     (+1 satir: SqlSingletonLeaseStore)

tests/AgentPrism.PostgreSql.IntegrationTests/
├── Contracts/InMemoryStoreContractTests.cs    (+koşum)
├── Contracts/PostgresStoreContractTests.cs    (+koşum)
├── Infrastructure/PostgresTestContext.cs      (+SingletonLeases ozelligi)
└── MigrationTests.cs                          (tablo sayisi 40 -> 41)

tests/AgentPrism.SqlServer.IntegrationTests/    (ayni uc dosya deseni)
tests/AgentPrism.Sqlite.IntegrationTests/       (ayni uc dosya deseni)

tests/AgentPrism.Core.UnitTests/Coordination/
├── SingletonGuardTests.cs                     (YENI — 6 test)
├── SingletonLeaseTakeoverTests.cs             (YENI — 2 test, gercek zaman)
└── ModelHealthSingletonTests.cs               (YENI — 1 test, gercek ModelProviderHealthBackgroundService x2)

tests/AgentPrism.Mcp.UnitTests/
└── McpDiscoverySingletonTests.cs              (YENI — 1 test, gercek McpDiscoveryService x2)
```

Plandaki dosya listesiyle fark: `SingletonExecutionOptionsValidator.cs` eklendi
(plana yazılı değildi, K-006/K-021 deseni gereği). `JobWorkerBackgroundService.cs`
plandaki gibi **dokunulmadı** — `git diff --stat` boş döndü.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler**

- `ISingletonLeaseStore` (Abstractions/Coordination): `TryAcquireAsync`/
  `RenewAsync`/`ReleaseAsync`. Yeni bir arka plan işini tek örneğe indirmek
  isteyen kod, kendi `SingletonGuard` örneğini kurar (bkz. `ModelProviderHealthBackgroundService`
  veya `McpDiscoveryService`'in kurucusu — desen ikisinde de aynı) ve
  benzersiz bir kira adı seçer.
- `SingletonGuard` **public**'tir (`AgentPrism.Core`); herhangi bir paket
  onu kullanabilir. `RunAsync(stoppingToken)` bir arka plan görevi olarak
  başlatılır (await edilmez), `IsHeld` her iş turunda senkron okunur.

**Bilinen tuzaklar (🚨)**

1. **`AgentPrismMcpOptions` (dolayısıyla `RefreshInterval`)
   `IConfiguration`'dan bağlanmaz** — yalnız `.UseMcp(o => ...)` delegesiyle
   koddan verilir. `AGENTPRISM__MCP__REFRESHINTERVAL` gibi bir ortam
   değişkeni **sessizce hiçbir şey yapmaz**. Bu Faz 42'den önce de böyleydi;
   ilk kez burada ölçülerek doğrulandı. Mcp modülüne dokunan bir faz bunu
   düzeltmeyi (config-bound hâle getirmeyi) değerlendirmelidir.
2. **Yenileme aralığının bir alt sınırı vardır (`SingletonGuard.RunAsync`
   içinde 1 saniye)** — `LeaseDuration` çok kısa (< 3 sn) ayarlanırsa
   yenileme `LeaseDuration/3`'ten DAHA SEYREK olur ve kira, sahibi hâlâ
   çalışırken bile süresi dolmuş görünebilir (başka bir örnek onu haksız
   yere devralabilir). Üretimde `LeaseDuration` en az birkaç saniye
   olmalıdır; varsayılan (60 sn) bu sınırın çok üzerindedir.
3. **🚨 Arka plan servisi başlatma sırası bir yarış açık bırakır.**
   `McpDiscoveryService` (bir `BackgroundService`) `.UseMcp()` çağrısında,
   `MigrationHostedService` (düz bir `IHostedService`) ise `.UseSqlite()`/
   `.UsePostgreSql()`/`.UseSqlServer()` çağrısında kaydedilir. Genel Host,
   `IHostedService.StartAsync`'i KAYIT SIRASINA göre çağırır; `.UseMcp()`
   Program.cs'te genelde `.UseSqlite()`'tan ÖNCE çağrılır. `BackgroundService.StartAsync`
   `ExecuteAsync`'i bekletmeden döner — bu yüzden `McpDiscoveryService`'in
   ilk `SingletonGuard` denemesi, `MigrationHostedService.StartAsync`
   migration'ları tamamlamadan ÖNCE çalışabilir ve `"no such table:
   ...singleton_leases"` (veya `mcp_servers` için de aynı desen) uyarısıyla
   başarısız olabilir. **Yeni bir kusur değildir** — `McpDiscoveryService`
   zaten ilk turunda `mcp_servers` tablosunu okuyordu ve aynı yarışa
   açıktı; Faz 42 yalnız ikinci bir örneğini (kira tablosu) ekledi.
   `SingletonGuard.TickAsync` hatayı yutar, loglar, bir sonraki yenileme
   turunda (birkaç saniye içinde) kendiliğinden düzelir — üretim
   etkisi geçicidir ama gözlemlenebilir bir uyarı satırı üretir. Kalıcı
   çözüm (hosted service kayıt sırasını garanti etmek veya `.UseMcp()`'in
   kendi ilk turunu geciktirmesi) bu fazın kapsamı dışıdır; aday listesine
   yazılmalıdır.

**Yarım kalan işler / açık uçlar — aday listesine yazılacak dört kalem**

1. **MCP OAuth token'ının örnekler arasında paylaşılması** — K-059 ile
   çatışır ([42.5](#425--kapsam-dışı-hız-sınırı-ve-mcp-tokenı)).
2. **Paylaşılan hız sınırı** — K-158 bunu bilerek bellekte tuttu.
3. **Kira durumunun teşhis ucunda gösterilmesi** —
   [Faz 33](33-SAGLIK-DENETIMI-VE-TESHIS.md)'e aittir.
4. **`AgentPrismMcpOptions`'ı `IConfiguration`'a bağlamak** — yukarıdaki
   tuzak 1; şu an yalnız kod-taraflı `configure` delegesiyle ayarlanabiliyor.

Ayrıca aday listesindeki **F-36** (öksüz çalıştırma uzlaştırması) bu fazın
`ISingletonLeaseStore`'unu doğrudan kullanır. Uzlaştırıcı kendi kira adını
seçmelidir (ör. `"orphan-run-reconciliation"`); `SingletonGuard`'ın kurucu
imzası (`store, optionsMonitor, leaseName, logger`) doğrudan yeniden
kullanılabilir.

**Sıradaki faz:** [Faz 43 — Idempotency Key](43-IDEMPOTENCY-KEY.md).

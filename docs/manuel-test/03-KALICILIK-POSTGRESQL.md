# 03 — Kalıcılık: PostgreSQL (`PG`)

> **Alan kodu:** `PG` · **Faz:** 2 (ayrıca 51: pgvector)
> **Kaynak:** `src/AgentPrism.PostgreSql` (`AgentPrismPostgreSqlBuilderExtensions.cs` ·
> `AgentPrismPostgreSqlOptions.cs` · `AgentPrismPostgreSqlOptionsValidator.cs` ·
> `Internal/NpgsqlDataSourceFactory.cs` · `Internal/PostgresDialect.cs` ·
> `Internal/PostgresQueries.cs` · `Stores/PgVectorSearchStore.cs` · `Migrations/*.sql`)
>
> Migration çalıştırma motoru (`MigrationRunner`, `MigrationHostedService`) ve depo
> uygulamaları (`SqlAgentDefinitionStore` vb.) `src/AgentPrism.Sql.Shared` içinde
> yaşar ve SQLite/SQL Server ile ORTAKTIR; bu dosya onları yalnız PostgreSQL
> sağlayıcısı üzerinden, PostgreSQL'e özgü SQL metni ve diyalekt davranışıyla
> (advisory lock, `jsonb`, `pgvector`) sınar. Sağlayıcılar arası karşılaştırma
> [`04-KALICILIK-DIGER.md`](04-KALICILIK-DIGER.md)'dedir.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

`UsePostgreSql()` çağrıldığında AgentPrism'in bellek içi varsayılanlarının yerini
alan zincir. Bağlantı ve şema ayarları doğrulanır, tek bir `NpgsqlDataSource`
kurulur, açılışta gömülü SQL migration'ları bir öneri kilidiyle korunarak
uygulanır, yirmi civarı depo `Replace` ile (ikisi `TryAdd` ile) değiştirilir ve
bazıları denetim izi dekoratörüyle sarılır. `pgvector` tabanlı vektör deposu
yalnız bu paketin uygulamasıdır.

```mermaid
flowchart TD
    A["UsePostgreSql()"] --> B["AgentPrismPostgreSqlOptions + Validator"]
    B --> C["NpgsqlDataSource"]
    C --> D["SqlStoreContext (PostgresDialect)"]
    D --> E["MigrationHostedService"]
    E -->|"pg_advisory_lock"| F["MigrationRunner: 28 migration"]
    F --> G["SchemaReadyGate acilir"]
    D --> H["Depo kayitlari: Replace / TryAdd"]
    H --> I["Denetim izi dekoratorleri"]
    D --> J["PgVectorSearchStore (pgvector)"]
    D --> K["ISqlPersistenceDiagnostics"]
    K --> L["/health, /agentprism/api/diagnostics"]
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| SQLite/SQL Server davranışı, migration sayısı karşılaştırması, bellek içi izlek ("`UsePostgreSql()` çağrılmazsa hiçbir şey kırılmaz") | [`04-KALICILIK-DIGER.md`](04-KALICILIK-DIGER.md) |
| Bilgi tabanı yükleme/parçalama, `search_knowledge` tool akışı, RAG anlamsal kalitesi | [`20-BELLEK-RAG-BAGLAM.md`](20-BELLEK-RAG-BAGLAM.md) |
| Kiracı/rol/API anahtarı HTTP güvenlik sınırları | [`13-KIRACI-VE-GUVENLIK.md`](13-KIRACI-VE-GUVENLIK.md) |
| Saklama, arşivleme politikaları, kota | [`23-SAKLAMA-ARSIV-KOTA.md`](23-SAKLAMA-ARSIV-KOTA.md) |
| AOT publish smoke testi | [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md) (`MT-PKG-062`) |
| Agent tanımı derleme/katalog genel davranışı, HTTP durum kodları | [`02-CEKIRDEK-VE-KATALOG.md`](02-CEKIRDEK-VE-KATALOG.md) |
| Teşhis/sağlık ucunun sağlayıcı bağımsız sözleşmesi | [`25-SAGLIK-TESHIS-OPENAPI.md`](25-SAGLIK-TESHIS-OPENAPI.md) |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. PostgreSQL container'ı (`ap-pg`, `pgvector/pgvector:pg18`) çalışır durumdadır.
3. `AgentPrism:PostgreSql:ConnectionString` `dotnet user-secrets` içinde tanımlıdır
   (bkz. `00-INDEKS.md` §2.4). Bazı case'ler bu değeri **geçici olarak** değiştirir;
   her case kendi temizlik adımını taşır.
4. `AgentPrism:Ui:AuthToken` `manuel-test-token-2026`'dır.
5. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080`

Kısaltmalar — bu dosyadaki her `curl`/`psql` şunları kullanır:

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
export PG="docker exec -i ap-pg psql -U postgres -d agentprism"
```

> **İzlek A notu.** Bu dosyadaki konsol uygulamaları [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md)
> `MT-PKG-070`'in kurduğu yerel NuGet feed'ini (`~/agentprism-local-feed`) kullanır.
> `AgentPrism.PostgreSql` bağımsız bir konsol uygulamasından **model çağırmadan**
> `IVectorSearchStore` gibi arayüzlere erişmeyi sağlar — Npgsql bağlantısı gerçektir
> ama hiçbir LLM sağlayıcısı gerekmez.

---

# 1 — Bağlantı, ayarlar ve doğrulama

Bu bölüm `AgentPrismPostgreSqlOptions`, `AgentPrismPostgreSqlOptionsValidator` ve
`NpgsqlDataSourceFactory`'yi sınar. Doğrulama açılışta (`ValidateOnStart`) çalışır;
geçersiz bir ayar uygulamanın **hiç başlamamasına** yol açar — çalışma anında
sessizce yok sayılmaz.

### MT-PG-001 — Boş bağlantı dizesiyle başlatma reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-006 |

**Ön koşul**
- PostgreSQL container'ı çalışıyor.

**Adımlar**
1. Bağlantı dizesini boş bir değere ayarla.
2. Uygulamayı başlat.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" ""
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı **reddeder** (`ValidateOnStart`); konsolda `OptionsValidationException`
  görünür ve mesaj `AgentPrismPostgreSqlOptions.ConnectionString bos olamaz` metnini taşır.
- Süreç sıfırdan farklı bir çıkış koduyla sonlanır; `/health` hiçbir zaman yanıt vermez.

**Gerçek sonuç**
> **Kaldı — ama kök neden ürün kusuru değil, case'in İzlek B ile test edilemez olması.**
> `samples/AgentPrism.Api/Program.cs:639` şu korumayı taşır:
> `else if (!string.IsNullOrWhiteSpace(postgreSql["ConnectionString"])) { agentPrism.UsePostgreSql(postgreSql); }`.
> Bağlantı dizesi boş bırakıldığında bu koşul `false` döner ve `UsePostgreSql()`
> **hiç çağrılmaz** — validator'a hiçbir zaman ulaşılmaz. Uygulama normal
> başladı (`Now listening on: http://localhost:5080`), `OptionsValidationException`
> ATILMADI. `GET /agentprism/api/diagnostics`: `"persistenceProvider": "InMemory"`,
> `"registeredPersistenceProviders": 0`. `/health` **200 Degraded** döndü (kalıcılık
> değil, model sağlayıcı sağlığı nedeniyle — bkz. `25-SAGLIK-TESHIS-OPENAPI.md`).
>
> Ayrı bir yardımcı harness ile (bu case'in adımı DEĞİL, doğrulama amaçlı,
> `src/AgentPrism.PostgreSql`'e `ProjectReference` veren bir konsol) doğrudan
> `UsePostgreSql(...)` çağrıldığında validator'ın TAM DA belgelenen gibi
> çalıştığı doğrulandı:
> - `UsePostgreSql("")` (string aşırı yüklemesi) çağrı ANINDA
>   `ArgumentException: The value cannot be an empty string or composed
>   entirely of whitespace. (Parameter 'connectionString')` fırlatır — belgelenenden
>   daha erken/sert bir hata.
> - `UsePostgreSql(IConfiguration)` (örnek uygulamanın kullandığı aşırı yükleme)
>   çağrının kendisinde atmaz, ama `IOptions<AgentPrismPostgreSqlOptions>.Value`
>   erişiminde TAM OLARAK belgelenen mesajı taşıyan
>   `Microsoft.Extensions.Options.OptionsValidationException` fırlatır:
>   `"AgentPrismPostgreSqlOptions.ConnectionString bos olamaz. ..."`.
>
> **Sonuç:** `AgentPrismPostgreSqlOptionsValidator` doğru çalışıyor. Kusur,
> bu case'in İzlek B (örnek uygulama) üzerinden yazılmış olmasında — örnek
> uygulamanın kasıtlı "bağlantı dizesi yoksa bellek içi çalış" davranışı
> (`Program.cs:622` yorumu) bu senaryoyu YAPISAL OLARAK erişilemez kılıyor.
> Öneri: case'in adımları İzlek A/C'ye (doğrudan `UsePostgreSql()` çağıran bir
> konsol) taşınmalı; İzlek B için ayrı ve doğru bir case ("boş bağlantı dizesiyle
> örnek uygulama bellek içi depoya sessizce düşer") eklenmeli.

**Durum:** ☐ Beklemede · ☐ Geçti · ☒ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism"` — uygulandı.

---

### MT-PG-002 — Şema adı `public` olamaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-013 |

**Ön koşul**
- PostgreSQL container'ı çalışıyor, bağlantı dizesi geçerli.

**Adımlar**
1. Şema adını `public` olarak ayarla.
2. Uygulamayı başlat.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:PostgreSql:SchemaName" "public"
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Hata mesajı `'public' olamaz` ve `K-013` ifadelerini taşır.
- Tüketicinin `public` şeması **hiçbir şekilde** değiştirilmez.

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';
-- Beklenen: denemeden ONCEKI ile AYNI sayi (AgentPrism public'e hicbir tablo yazmadi).
```

**Gerçek sonuç**
> Beklendiği gibi. Uygulama `Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
> AgentPrismPostgreSqlOptions.SchemaName 'public' olamaz. AgentPrism tuketicinin
> public semasina dokunmaz. Gerekce: docs/KARARLAR.md, karar K-013.` ile
> başlamadan sonlandı (`Now listening` satırı hiç görünmedi). Doğrulama sorgusu
> `count = 0` döndü — denemeden önceki durumla aynı.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:PostgreSql:SchemaName" "agentprism"` — uygulandı.

---

### MT-PG-003 — Geçersiz şema adı biçimleri ve enjeksiyon denemesi reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-029, K-179 |

Negatif senaryo. Şema adı yapılandırmadan gelir ve SQL metnine doğrudan
yerleştirilir (parametre olarak gönderilemez); bu yüzden tanımlayıcı kuralları
katıdır: yalnız küçük harf, rakam, alt çizgi.

**Ön koşul**
- PostgreSQL container'ı çalışıyor.

**Adımlar**
1. Büyük harf içeren bir şema adı dene.
2. Boşluk içeren bir şema adı dene.
3. SQL enjeksiyonu deneyen bir şema adı dene.

**Girilecek veri**
```bash
# 1) Buyuk harf
dotnet user-secrets set "AgentPrism:PostgreSql:SchemaName" "Agentprism"
cd samples/AgentPrism.Api && dotnet run
# Ctrl+C ile durdur

# 2) Bosluk
dotnet user-secrets set "AgentPrism:PostgreSql:SchemaName" "agent prism"
dotnet run
# Ctrl+C ile durdur

# 3) Enjeksiyon denemesi
dotnet user-secrets set "AgentPrism:PostgreSql:SchemaName" "agentprism; DROP SCHEMA public CASCADE;--"
dotnet run
# Ctrl+C ile durdur
```

**Beklenen sonuç**
- Üçü de uygulamanın başlamasını reddeder; hata mesajı aynı biçimdedir
  (`gecerli bir tirnaksiz PostgreSQL tanimlayicisi degil`).
- 3. denemede `DROP SCHEMA` **hiçbir zaman çalıştırılmaz** — doğrulama, adın SQL
  metnine yerleştirilmesinden ÖNCE gerçekleşir.

**Doğrulama sorgusu**
```sql
SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'public';
-- Beklenen: bir satir doner (public hala var).
```

**Gerçek sonuç**
> Beklendiği gibi, üçü de AYNI biçimde reddetti (mesaj kalıbı sabit,
> yalnız "Gelen deger" değişiyor):
> `AgentPrismPostgreSqlOptions.SchemaName gecerli bir tirnaksiz PostgreSQL
> tanimlayicisi degil. Kucuk harf veya alt cizgi ile baslamali; kucuk harf,
> rakam ve alt cizgi icermeli; en cok 63 karakter olmalidir. Gelen deger:
> '<deger>'.` — `Agentprism`, `agent prism`, ve enjeksiyon dizesinin tamamı
> (`agentprism; DROP SCHEMA public CASCADE;--`) sırayla bu kalıba düştü.
> Doğrulama sorgusu bir satır döndü — `public` şeması hâlâ var, `DROP SCHEMA`
> hiç çalışmadı.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:PostgreSql:SchemaName" "agentprism"` — uygulandı.

---

### MT-PG-004 — `CommandTimeoutSeconds` sınırları

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Sınır senaryosu.

**Ön koşul**
- PostgreSQL container'ı çalışıyor, diğer ayarlar geçerli.

**Adımlar**
1. `-1` dene (aralık dışı, reddedilir).
2. `3601` dene (aralık dışı, reddedilir).
3. `0` dene (sınırsız anlamına gelir, kabul edilir).
4. `3600` dene (üst sınır, kabul edilir).

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:PostgreSql:CommandTimeoutSeconds" "-1"
dotnet run   # reddedilir, Ctrl+C

dotnet user-secrets set "AgentPrism:PostgreSql:CommandTimeoutSeconds" "3601"
dotnet run   # reddedilir, Ctrl+C

dotnet user-secrets set "AgentPrism:PostgreSql:CommandTimeoutSeconds" "0"
dotnet run   # baslar, Ctrl+C

dotnet user-secrets set "AgentPrism:PostgreSql:CommandTimeoutSeconds" "3600"
dotnet run   # baslar, Ctrl+C
```

**Beklenen sonuç**
- 1. ve 2. denemede hata mesajı `0 ile 3600 arasinda olmalidir` ifadesini ve
  gönderilen değeri taşır.
- 3. ve 4. deneme başarıyla başlar.

**Gerçek sonuç**
> Beklendiği gibi, dört değer de. `-1` → `AgentPrismPostgreSqlOptions.CommandTimeoutSeconds
> 0 ile 3600 arasinda olmalidir. Gelen deger: -1.` ile reddedildi. `3601` → aynı
> kalıp, `Gelen deger: 3601.` ile reddedildi. `0` ve `3600` ikisi de `Now
> listening on: http://localhost:5080` ile normal başladı.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets remove "AgentPrism:PostgreSql:CommandTimeoutSeconds"` (varsayılan 30'a döner) — uygulandı.

---

### MT-PG-005 — Üç `UsePostgreSql` aşırı yüklemesi aynı sonucu üretir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-021 |

`UsePostgreSql(string)`, `UsePostgreSql(IConfiguration)` ve
`UsePostgreSql(Action<Options>)` üç ayrı yoldan aynı `AgentPrismPostgreSqlOptions`'a
varmalıdır. `IConfiguration` yolu yansıma KULLANMAZ — elle yazılmış bir `Bind()`
metodudur (K-021); yeni bir alan eklenip bu metoda eklenmezse sessizce kaybolur.

**Ön koşul**
- [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md) `MT-PKG-070` geçti
  (yerel feed hazır).

**Adımlar**
1. Konsol projesi kur.
2. Üç overload'ı sırayla dene, her birinde çözülen `ConnectionString` ve
   `SchemaName` değerlerini yazdır.

**Girilecek veri**
```bash
rm -rf ~/agentprism-manuel/pg-ayarlar && mkdir -p ~/agentprism-manuel/pg-ayarlar
cd ~/agentprism-manuel/pg-ayarlar
dotnet new console -o . --force
cp ~/agentprism-manuel/uretec/nuget.config .
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.PostgreSql.*.nupkg | sed 's#.*AgentPrism.PostgreSql\.##;s#\.nupkg##')
dotnet add package AgentPrism.PostgreSql --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

void Yazdir(string etiket, IServiceCollection services)
{
    var options = services.BuildServiceProvider()
        .GetRequiredService<IOptions<AgentPrismPostgreSqlOptions>>().Value;
    Console.WriteLine($"{etiket}: ConnectionString='{options.ConnectionString}' SchemaName='{options.SchemaName}'");
}

var conn = "Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism";

var s1 = new ServiceCollection();
s1.AddAgentPrism().UsePostgreSql(conn);
Yazdir("string", s1);

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AgentPrism:PostgreSql:ConnectionString"] = conn,
        ["AgentPrism:PostgreSql:SchemaName"] = "agentprism",
    })
    .Build();
var s2 = new ServiceCollection();
s2.AddAgentPrism().UsePostgreSql(config.GetSection(AgentPrismPostgreSqlOptions.SectionName));
Yazdir("IConfiguration", s2);

var s3 = new ServiceCollection();
s3.AddAgentPrism().UsePostgreSql(o =>
{
    o.ConnectionString = conn;
    o.SchemaName = "agentprism";
});
Yazdir("Action<Options>", s3);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Üç satır da AYNI `ConnectionString` ve `SchemaName` değerlerini gösterir.

**Gerçek sonuç**
> Beklendiği gibi (paketlenmiş `AgentPrism.PostgreSql` 0.0.0-preview.0.64,
> yerel feed üzerinden). Üç satır da birebir aynı:
> `ConnectionString='Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism'
> SchemaName='agentprism'`.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-006 — Bağlantı dizesi hiç verilmezse hangi denetim önce tetiklenir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Sınır senaryosu / şüphe kaydı. Kod tabanında bağlantı dizesi boşluğunu kontrol
eden **iki** ayrı yer vardır: `AgentPrismPostgreSqlOptionsValidator` (her
`IOptions<T>.Value` erişiminde tetiklenir) ve `NpgsqlDataSourceFactory.Create`
(kendi içinde ayrı bir `AgentPrismException` fırlatır). Normal DI akışında
`NpgsqlDataSource` fabrikası `IOptions<T>.Value`'yu **kendisi** okuduğu için
Validator'ın önce tetiklenmesi beklenir; bu case hangisinin GERÇEKTEN göründüğünü
kaydeder.

**Ön koşul**
- [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md) `MT-PKG-070` geçti.

**Adımlar**
1. Bağlantı dizesi verilmeden `UsePostgreSql` çağır.
2. `NpgsqlDataSource`'u çözümlemeyi dene.

**Girilecek veri**
```bash
rm -rf ~/agentprism-manuel/pg-baglantisiz && mkdir -p ~/agentprism-manuel/pg-baglantisiz
cd ~/agentprism-manuel/pg-baglantisiz
dotnet new console -o . --force
cp ~/agentprism-manuel/uretec/nuget.config .
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.PostgreSql.*.nupkg | sed 's#.*AgentPrism.PostgreSql\.##;s#\.nupkg##')
dotnet add package AgentPrism.PostgreSql --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

var services = new ServiceCollection();
services.AddAgentPrism().UsePostgreSql(_ => { });

var provider = services.BuildServiceProvider();

try
{
    provider.GetRequiredService<NpgsqlDataSource>();
    Console.WriteLine("🚨 istisna ATILMADI");
}
catch (Exception ex)
{
    Console.WriteLine($"istisna tipi: {ex.GetType().FullName}");
    Console.WriteLine($"mesaj: {ex.Message}");
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Bir istisna fırlar; `🚨 istisna ATILMADI` görünmez.
- İstisnanın tipi kaydedilir (`Microsoft.Extensions.Options.OptionsValidationException`
  mi yoksa `AgentPrism.AgentPrismException` mi). `AgentPrismPostgreSqlOptionsValidator`'ın
  DI akışında Factory'nin kendi kontrolünden ÖNCE tetiklenip tetiklenmediği burada netleşir;
  ikisi de kullanıcı için anlaşılır bir mesaj taşımalıdır.

**Gerçek sonuç**
> `00-INDEKS.md` §8'deki şüphe DOĞRULANDI: `Microsoft.Extensions.Options.OptionsValidationException`
> fırlıyor — `AgentPrism.AgentPrismException` DEĞİL. Mesaj:
> `AgentPrismPostgreSqlOptions.ConnectionString bos olamaz. Baglanti dizesini
> "UsePostgreSql(...)" cagrisinda verin veya 'AgentPrism:PostgreSql:ConnectionString'
> ayarini "dotnet user-secrets" icinde tanimlayin.` `Validator`, `NpgsqlDataSourceFactory.Create`
> içindeki kendi boş-dize kontrolünden önce tetikleniyor; o kontrol normal DI
> akışında gerçekten ulaşılamaz durumda (şüphe kaydındaki tahmin gibi).

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-007 — Yanlış host ile başlatma migration adımında çöker (fail-fast)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Negatif senaryo. `MigrationHostedService`'in kod yorumu açıktır: "Hata uygulamayi
baslatmaz" — şema hazır değilken sessizce çalışan bir AgentPrism veri kaybeder;
bu yüzden migration hatası yutulmaz, süreç kapanır.

**Ön koşul**
- Hiçbir şey dinlemeyen bir port biliniyor (örnek: `1`).

**Adımlar**
1. Bağlantı dizesini geçersiz bir porta ayarla.
2. Uygulamayı başlat ve bekle.
3. `/health`'e erişmeyi dene.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" \
  "Host=localhost;Port=1;Database=agentprism;Username=postgres;Password=agentprism;Timeout=5"
cd samples/AgentPrism.Api && dotnet run
```
```bash
# Baska bir terminalde, uygulama hala "baslarken":
curl -s -m 3 -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"
```

**Beklenen sonuç**
- `dotnet run` süreci bir `NpgsqlException`/`SocketException` zincirini konsola
  yazar ve **sonlanır** (host asla "Now listening on" satırını yazmaz).
- `/health` isteği bağlantı reddi veya zaman aşımıyla başarısız olur — Kestrel
  hiç dinlemeye başlamamıştır.

**Gerçek sonuç**
> Beklendiği gibi. `Npgsql.NpgsqlException: Failed to connect to 127.0.0.1:1
> ---> System.Net.Sockets.SocketException (61): Connection refused` zinciri
> `MigrationRunner.ApplyAsync` → `MigrationHostedService.StartAsync` üzerinden
> fırladı, süreç ~4 saniyede sonlandı, `Now listening` HİÇ yazılmadı. `/health`
> isteği `HTTP: 000` (bağlantı kurulamadı) döndü — Kestrel hiç dinlemedi.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism"` — uygulandı.

---

### MT-PG-008 — `secret` hiçbir zaman veritabanına veya dosyaya yazılmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-059 |

Negatif senaryo. `AgentPrismPostgreSqlOptions.ConnectionString`'in XML dokümanı
"bir sırdır ve dosyaya yazılmaz" der; bu case iddiayı doğrular.

**Ön koşul**
- Uygulama normal çalışıyor, en az birkaç agent kaydedilmiş (önceki case'lerden).

**Adımlar**
1. `appsettings*.json` dosyalarını bağlantı dizesi için tara.
2. `agent_definitions` tablosunu bağlantı dizesi alt dizgisi için tara.

**Girilecek veri**
```bash
grep -rn "Password=agentprism\|Host=localhost;Port=55432" samples/AgentPrism.Api/appsettings*.json
```
```sql
SELECT count(*) FROM agentprism.agent_definitions WHERE definition::text ILIKE '%Password=%';
SELECT count(*) FROM agentprism.audit_log WHERE before::text ILIKE '%Password=%' OR after::text ILIKE '%Password=%';
```

**Beklenen sonuç**
- `grep` sıfır satır döner — `appsettings*.json` boş yer tutucu taşır, gerçek
  değer yalnız `dotnet user-secrets` içindedir.
- Her iki SQL sorgusu da **0** döner.

**Gerçek sonuç**
> Beklendiği gibi (bu case önceki case'lerden agent kaydı birikince koşuldu —
> `manuel-denetim` dahil). `grep` sıfır satır döndü (exit code 1). Her iki SQL
> sorgusu da `count = 0`.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Migration'lar

`MigrationRunner`/`MigrationHostedService` `AgentPrism.Sql.Shared` içinde
yaşar ama davranışları PostgreSQL diyalektinin (`pg_advisory_lock`, 28 gömülü
`.sql` dosyası) üzerinden gözlenir. Kapsam kararı (bkz. `PROMPT.md` §3): migration
üç yoldan test edilir — boş DB, yeniden çalıştırma (idempotent), var olan şema
üzerine.

### MT-PG-020 — Boş DB'de 28 migration sırayla uygulanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

**Ön koşul**
- Reset yordamı uygulanmış (şema düşürülmüş).

**Adımlar**
1. Uygulamayı başlat.
2. Açılış logunu oku.
3. `__migrations` tablosunu sorgula.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```
```sql
SELECT count(*) FROM agentprism.__migrations;
SELECT id, name FROM agentprism.__migrations ORDER BY id;
```

**Beklenen sonuç**
- Açılış logu `AgentPrism 28 migration uyguladi. Sema: agentprism.` satırını taşır.
- `count(*)` **28** döner.
- `id` sütunu 1'den 28'e **boşluksuz** sıralıdır; son satır
  `0028_experiment_canary`'dir.

**Gerçek sonuç**
> Beklendiği gibi. Log `AgentPrism 28 migration uyguladi. Sema: agentprism.`
> satırını taşıdı. `count(*) = 28`. `id` 1'den 28'e boşluksuz sıralı, son satır
> `0028_experiment_canary`.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-021 — Yeniden başlatma migration'ları tekrar uygulamaz (idempotent)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

**Ön koşul**
- MT-PG-020 geçti; şema DÜŞÜRÜLMEDİ.

**Adımlar**
1. Uygulamayı durdur (`Ctrl+C`).
2. Tekrar başlat.
3. Açılış logunu oku.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- `"... migration uyguladi."` satırı **görünmez** (uygulanan migration sayısı
  0'dır; kod bu durumda log basmaz).
- `__migrations` hâlâ **28** satır taşır.
- Uygulama normal başlar, hiçbir hata görünmez.

**Gerçek sonuç**
> Beklendiği gibi. `"... migration uyguladi."` satırı görünmedi (0 eşleşme),
> `__migrations` hâlâ 28 satır, uygulama normal dinlemeye geçti, hata yok.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-022 — Var olan (kısmi) şema üzerine devam

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Sınır senaryosu. İlk beş migration ELLE uygulanır ve `__migrations` defterine
doğru checksum'la kaydedilir — bir önceki AgentPrism sürümünün yarım bıraktığı
bir dağıtımı simüler. Uygulama geri kalan 23'ünü uygulamalıdır.

**Ön koşul**
- Reset yordamı uygulanmış, uygulama HENÜZ başlatılmamış.

**Adımlar**
1. İlk beş migration dosyasını elle uygula.
2. Checksum'larını hesaplayıp `__migrations`'a elle yaz.
3. Uygulamayı başlat.
4. Açılış logunu ve `__migrations` tablosunu oku.

**Girilecek veri**
```bash
$PG -c "CREATE SCHEMA IF NOT EXISTS agentprism;"

for f in src/AgentPrism.PostgreSql/Migrations/0001_initial.sql \
         src/AgentPrism.PostgreSql/Migrations/0002_observability.sql \
         src/AgentPrism.PostgreSql/Migrations/0003_agent_skills.sql \
         src/AgentPrism.PostgreSql/Migrations/0004_skill_scripts.sql \
         src/AgentPrism.PostgreSql/Migrations/0005_agent_call_graph.sql; do
  sed 's/{schema}/agentprism/g' "$f" | $PG
done

$PG -c "CREATE TABLE IF NOT EXISTS agentprism.__migrations (
  id integer NOT NULL PRIMARY KEY, name text NOT NULL,
  checksum text NOT NULL, applied_at timestamptz NOT NULL);"

for f in src/AgentPrism.PostgreSql/Migrations/0001_initial.sql \
         src/AgentPrism.PostgreSql/Migrations/0002_observability.sql \
         src/AgentPrism.PostgreSql/Migrations/0003_agent_skills.sql \
         src/AgentPrism.PostgreSql/Migrations/0004_skill_scripts.sql \
         src/AgentPrism.PostgreSql/Migrations/0005_agent_call_graph.sql; do
  id=$(basename "$f" | cut -c1-4 | sed 's/^0*//')
  name=$(basename "$f" .sql)
  checksum=$(python3 -c "
import hashlib
data = open('$f', 'rb').read().replace(b'\r\n', b'\n')
print(hashlib.sha256(data).hexdigest().upper())
")
  $PG -c "INSERT INTO agentprism.__migrations (id, name, checksum, applied_at) VALUES ($id, '$name', '$checksum', now());"
done

cd samples/AgentPrism.Api && dotnet run
```
```sql
SELECT id, name, applied_at FROM agentprism.__migrations ORDER BY id LIMIT 7;
```

**Beklenen sonuç**
- Açılış logu `AgentPrism 23 migration uyguladi.` yazar (28 − 5).
- Hiçbir checksum uyuşmazlığı hatası oluşmaz.
- `__migrations`'ta 28 satır vardır; id 1–5'in `applied_at` değeri elle yazılan
  zaman, id 6–28'inki uygulamanın az önceki açılış zamanıdır.

**Gerçek sonuç**
> Beklendiği gibi. Log `AgentPrism 23 migration uyguladi. Sema: agentprism.`
> yazdı (28 − 5). Checksum uyuşmazlığı hatası oluşmadı. `count(*) = 28`; id
> 1–5'in `applied_at` değeri (`20:04:33.2xx`–`.7xx`, elle yazılan) id 6–28'inkinden
> (`20:04:45.8xx`, uygulamanın açılış anı) FARKLI — elle yazılan beşi dokunulmadan
> kaldı.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-023 — Uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Negatif senaryo. Uygulanmış bir migration dosyasının içeriği (temsili olarak)
değişmiş gibi simüle edilir — `__migrations.checksum` elle bozulur.

**Ön koşul**
- MT-PG-020 veya MT-PG-021 geçti (28 migration uygulanmış, uygulama DURDURULMUŞ).

**Adımlar**
1. `__migrations` tablosunda `id=1`'in checksum'ını boz.
2. Uygulamayı başlat.
3. Doğru checksum'ı hesapla (temizlik için).

**Girilecek veri**
```bash
$PG -c "UPDATE agentprism.__migrations SET checksum = 'BOZUK0000000000000000000000000000000000000000000000000000000' WHERE id = 1;"
cd samples/AgentPrism.Api && dotnet run
```
```bash
python3 -c "
import hashlib
data = open('src/AgentPrism.PostgreSql/Migrations/0001_initial.sql','rb').read().replace(b'\r\n', b'\n')
print(hashlib.sha256(data).hexdigest().upper())
"
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Konsolda `AgentPrismException` görünür; mesaj `'0001_initial' migration'i
  veritabaninda uygulanmis ancak dosyanin icerigi degismis` ifadesini taşır.
- Mesaj hem veritabanındaki (bozuk) hem dosyadaki (doğru) checksum'ı gösterir.

**Gerçek sonuç**
> Beklendiği gibi. `AgentPrism.AgentPrismException: '0001_initial' migration'i
> veritabaninda uygulanmis ancak dosyanin icerigi degismis. Veritabanindaki
> ozet: BOZUK00...0000, dosyanin ozeti: FDC95ECB...66F1D. Uygulanmis bir
> migration duzenlenmez; degisiklik icin yeni bir migration dosyasi ekleyin.`
> ile başlamadı, `Now listening` görünmedi. Mesaj iki özeti de gösterdi.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** Checksum `FDC95ECB390F5C465A071751607C6917E022AC4ED684A386413B4FA62F166F1D`
> değerine geri yazıldı — uygulandı.

---

### MT-PG-024 — İki eşzamanlı örnek çakışmadan migration uygular

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Sınır senaryosu. `pg_advisory_lock` oturum kapsamlıdır; iki örnek aynı anda
başlarsa biri kilidi alır, diğeri bekler.

**Ön koşul**
- Reset yordamı uygulanmış, temiz (boş) DB.

**Adımlar**
1. İki terminalde uygulamayı EŞ ZAMANLI başlat (biri farklı portta).
2. İkisinin de loglarını oku.
3. `__migrations` satır sayısını kontrol et.

**Girilecek veri**
```bash
# Terminal 1
cd samples/AgentPrism.Api && dotnet run

# Terminal 2 (mumkun oldugunca ayni anda)
cd samples/AgentPrism.Api && dotnet run --urls http://localhost:5090
```
```sql
SELECT count(*) FROM agentprism.__migrations;
```

**Beklenen sonuç**
- Yalnız BİR terminalin logu `AgentPrism 28 migration uyguladi.` yazar; diğeri
  0 migration uygular (log satırı görünmez) çünkü kilidi aldığında migration'lar
  zaten bitmiştir.
- Hiçbir terminalde checksum hatası veya çökme olmaz.
- `count(*)` tam olarak **28** döner (56 değil — birincil anahtar çakışması yoktur).

**Gerçek sonuç**
> Beklendiği gibi. İki `dotnet` süreci (5080 ve 5090) neredeyse eşzamanlı
> başlatıldı. Yalnız 5090 örneği `AgentPrism 28 migration uyguladi.` yazdı;
> 5080 örneği hiç migration log satırı yazmadan doğrudan `Now listening`'e
> geçti (kilidi aldığında migration'lar zaten bitmişti). İkisi de hatasız
> dinlemeye başladı. `count(*) = 28` — 56 değil.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** İki süreç de `kill -9` ile durduruldu — uygulandı.

---

### MT-PG-025 — `AutoApplyMigrations=false` migration uygulamaz, sorumluluk operatöre kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-354 |

Negatif/sınır senaryosu.

**Ön koşul**
- Reset yordamı uygulanmış, temiz (boş) şema.

**Adımlar**
1. `AutoApplyMigrations`'ı kapat.
2. Uygulamayı başlat.
3. `/health`'i çağır.
4. Bir agent kaydetmeyi dene.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:PostgreSql:AutoApplyMigrations" "false"
cd samples/AgentPrism.Api && dotnet run
```
```bash
curl -s -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"

curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"manuel-migrationsiz","model":{"provider":"echo","model":"echo-1"}}' \
  -w "\nHTTP: %{http_code}\n"
```

**Beklenen sonuç**
- Uygulama başlar (çökmez); log `migration'lari otomatik uygulanmiyor` satırını
  taşır.
- `/health` **Unhealthy** döner (bekleyen migration var).
- Agent kaydı isteği veritabanı hatasıyla (tablo yok) başarısız olur; HTTP kodu
  5xx'tir ama uygulama çökmez, sonraki istekler de aynı şekilde anlaşılır hata
  döner.

**Gerçek sonuç**
> **Kaldı — gerçek, iki bağımsız denemede TUTARLI biçimde tekrar üretildi.**
> Log beklenen `AgentPrism migration'lari otomatik uygulanmiyor (AutoApplyMigrations
> kapali). Semanin guncel olmasi cagiranin sorumlulugundadir.` satırını taşıdı
> ve `Now listening on: http://localhost:5080` bile yazdı — ama birkaç saniye
> içinde uygulama KENDİ KENDİNİ KAPATTI:
> ```
> crit: AgentPrism.A2AApprovalGuardFilter[0]
>       A2A disa acik yuzey denetimi basarisiz oldu; uygulama durduruluyor.
>       Npgsql.PostgresException (0x80004005): 42P01: relation "agentprism.agent_definitions" does not exist
> info: Microsoft.Hosting.Lifetime[0]
>       Application is shutting down...
> ```
> İki bağımsız denemede de (temiz şema, `AutoApplyMigrations=false` sabit)
> aynı çöküş oluştu — rastgele bir yarış değil, tutarlı bir davranış.
> `/health` ve agent kaydı isteklerine ULAŞILAMADI (`HTTP: 000`) çünkü süreç
> çökme sürecindeydi.
>
> **Kök neden** (`src/AgentPrism.AspNetCore/A2A/A2AApprovalGuardFilter.cs`,
> `src/AgentPrism.Abstractions/Diagnostics/SchemaReadyGate.cs`): örnek uygulama
> `UseA2A(o => o.ExposedAgents.Add("ozetleyici"))` çağırıyor (`Program.cs:99`,
> VARSAYILAN yapılandırma). `SchemaReadyGate.MarkReady()`, `AutoApplyMigrations`
> kapalıyken de `MigrationHostedService` tarafından BİLEREK hemen çağrılıyor
> (kod yorumu: "o durumda semanin hazir olmasi tuketicinin sorumlulugundadir").
> Kapı açılır açılmaz `A2AApprovalGuardFilter`'ın arka plan denetimi
> `IAgentCatalog.ListAsync()` çağırıyor, bu da var olmayan
> `agentprism.agent_definitions` tablosuna çarpıyor, `catch (Exception)` bloğu
> bunu `LogCritical` + `lifetime.StopApplication()` ile karşılıyor. Aynı desen
> `McpApprovalGuardFilter`'da da var (kod yorumu: "AYNI gerekce ve AYNI
> tasarim") — A2A kapalı olsa MCP'nin de aynı şekilde çökertmesi beklenir.
>
> **Etki:** K-354'ün belgelediği sözleşme ("uygulama başlar, sorumluluk
> operatöre kalır") A2A/MCP açıkken (örnek uygulamanın VARSAYILANI) TAMAMEN
> geçersiz — uygulama Degraded modda hizmet vermek yerine kendini kapatıyor.
> Gerçek bir dağıtımda (ör. K8s: API pod, migration Job'undan önce ayağa
> kalkarsa) bu, `AutoApplyMigrations=false` seçmenin amacını tam tersine
> çeviren bir crash-loop üretir. Belgede **Yüksek** işaretli; gözlenen etkinin
> (dokümante edilen sözleşmenin A2A/MCP açıkken TAMAMEN işlevsiz olması,
> Degraded değil TAM KESİNTİ) **Kritik**'e yükseltilmesi önerilir.
>
> ---
>
> **2026-08-14 yeniden koşum — Geçti.** Kusur `2caa423` (S1-8 dalgası) ile
> kapanmış: `ExternalSurfaceGuard.ListCatalogWithRetryAsync` katalog sorgusunu
> artık üstel geri çekilmeyle yeniden dener ve hiçbir koşulda
> `StopApplication` çağırmaz; log satırı bunu açıkça söylüyor ("bu arada
> uygulamanin geri kalani calisir durumda kalir"). Bu case'in dört beklentisi
> de ampirik olarak doğrulandı (temiz `mt_fin` şeması,
> `AutoApplyMigrations=false`):
> - `migration'lari otomatik uygulanmiyor` log satırı: **1 kez** yazıldı.
> - `Application is shutting down`: **0 kez** — uygulama ayakta kaldı.
> - `/health`: **Unhealthy**, `HTTP 503`.
> - Agent kaydı: `HTTP 500` (tablo yok). Peş peşe ikinci bir kayıt isteği de
>   aynı `500`'ü verdi ve `/health` hâlâ yanıt verdi — çökme yok.
>
> Not: gövdede `provider:"echo"` kullanılırsa `400 Tanim gecersiz` alınır
> (gerçek sağlayıcı anahtarları kayıtlıyken `echo` kayıtlı değildir);
> veritabanına ulaşan yolu görmek için kayıtlı bir sağlayıcı (`openai`)
> kullanıldı.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets remove "AgentPrism:PostgreSql:AutoApplyMigrations"`, reset yordamı — uygulandı.

---

### MT-PG-026 — Şema adı değiştirildiğinde bağımsız bir migration seti oluşur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-013 |

**Ön koşul**
- MT-PG-020 geçti (`agentprism` şeması 28 migration'lı).

**Adımlar**
1. Şema adını `agentprism_ikinci` olarak ayarla.
2. Uygulamayı başlat.
3. Her iki şemayı da sorgula.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:PostgreSql:SchemaName" "agentprism_ikinci"
cd samples/AgentPrism.Api && dotnet run
```
```sql
SELECT schema_name FROM information_schema.schemata WHERE schema_name IN ('agentprism', 'agentprism_ikinci');
SELECT count(*) FROM agentprism.__migrations;
SELECT count(*) FROM agentprism_ikinci.__migrations;
```

**Beklenen sonuç**
- Açılış logu yeni şema için `AgentPrism 28 migration uyguladi. Sema:
  agentprism_ikinci.` yazar.
- Her iki şema da mevcuttur; her ikisinin de `__migrations`'ı **28** satır
  taşır — birbirinden BAĞIMSIZDIR.
- Orijinal `agentprism` şemasındaki veriler (varsa) dokunulmadan kalır.

**Gerçek sonuç**
> Beklendiği gibi. Log `AgentPrism 28 migration uyguladi. Sema:
> agentprism_ikinci.` yazdı. `information_schema.schemata` her iki şemayı da
> listeledi; her ikisinin `__migrations`'ı `count(*) = 28`.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:**
> ```bash
> dotnet user-secrets set "AgentPrism:PostgreSql:SchemaName" "agentprism"
> $PG -c "DROP SCHEMA IF EXISTS agentprism_ikinci CASCADE;"
> ```

---

### MT-PG-027 — `Dimensions` değişikliği uygulanmış `vector` sütununun boyutunu DEĞİŞTİRMEZ

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

Sınır senaryosu. `0024_vector.sql`'in `{dimension}` yer tutucusu bir sema
yer tutucusu GİBİ davranmaz: checksum ham (değiştirilmemiş) metin üzerinden
hesaplanır, bu yüzden `Dimensions` ayarını değiştirip yeniden başlatmak checksum
hatası VERMEZ — ama var olan sütunun boyutunu da değiştirmez. Kod bunu 🚨 ile
işaretler; bu case operasyonel tuzağı doğrular.

**Ön koşul**
- MT-PG-020 geçti (`0024` migration'ı 1536 boyutla uygulanmış).

**Adımlar**
1. Bilgi tabanı boyutunu `3`'e ayarla.
2. Uygulamayı başlat.
3. Sütun boyutunu sorgula.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:Knowledge:Dimensions" "3"
cd samples/AgentPrism.Api && dotnet run
```
```sql
SELECT atttypmod FROM pg_attribute
WHERE attrelid = 'agentprism.document_embeddings'::regclass AND attname = 'embedding';
```

**Beklenen sonuç**
- Uygulama normal başlar, checksum uyuşmazlığı hatası **OLUŞMAZ**.
- `atttypmod` hâlâ **1536**'dır (pgvector bu sütunda boyutu doğrudan taşır) —
  `3` DEĞİLDİR. Ayar sessizce hiçbir şey yapmamıştır.

**Gerçek sonuç**
> Beklendiği gibi. Uygulama normal başladı, checksum uyuşmazlığı hatası
> oluşmadı. `atttypmod = 1536` — `Dimensions=3` ayarı sessizce hiçbir şey
> yapmadı.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets remove "AgentPrism:Knowledge:Dimensions"`

---

# 3 — Kayıt deseni: `Replace` / `TryAdd`, denetim izi, çoklu sağlayıcı

`UsePostgreSql()` yirmiden fazla depoyu `Replace` ile üzerine yazar (K-025);
yalnız `IConversationBranchStore` ve `IVectorSearchStore` `TryAddSingleton` ile
kaydedilir. Beş yazma yapan depo (agent tanımı, oturum, deney, tool onay kuralı,
MCP sunucusu, kiracı) `Auditing*` dekoratörüyle sarılır; yürütmenin yan ürünü
olan depolar (run, iş kuyruğu, kota, webhook, ses oturumu, run skoru vb.)
sarılmaz.

### MT-PG-030 — Yönetimsel yazmalar denetim izine düşer, yürütme yan ürünleri düşmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2, 9 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor, PostgreSQL açık.

**Adımlar**
1. Agent tanımı kaydet.
2. Agent'ı çalıştır (tool çağırmayan bir istekle — `echo` yeterli).
3. `audit_log` tablosunu tara.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-denetim", "instructions": "Kisa yanit ver.",
  "model": { "provider": "echo", "model": "echo-1" }
}'

curl -s -X POST "$APU/api/agents/manuel-denetim/run" -H "$APB" \
  -H "content-type: application/json" -d '{"message":"Merhaba"}' > /dev/null
```
```sql
SELECT entity, action FROM agentprism.audit_log ORDER BY created_at DESC LIMIT 5;
SELECT count(*) FROM agentprism.audit_log WHERE entity ILIKE '%run%' OR entity ILIKE '%tool_invocation%';
```

**Beklenen sonuç**
- `agent_definitions` (veya eşdeğer) varlığı için EN AZ bir audit kaydı vardır.
- İkinci sorgu **0** döner — `runs`/`tool_invocations` için audit_log'da hiçbir
  satır yoktur; `IRunStore` denetim izi dekoratörüyle SARILMAMIŞTIR.

**Gerçek sonuç**
> Beklendiği gibi. `agent:manuel-denetim` / `agent.create` için tek audit
> kaydı var. `runs`/`tool_invocations` için `count(*) = 0`.
>
> **Yan not (ortam, kusur değil):** `echo` sağlayıcısı yalnız OpenAI anahtarı
> BOŞ olduğunda kayıtlı oluyor (`samples/AgentPrism.Api/Program.cs:128,149` —
> `openAiEnabled = !string.IsNullOrWhiteSpace(...)`). Bu makinenin
> `user-secrets`'ında gerçek bir OpenAI anahtarı zaten tanımlıydı; ilk deneme
> bu yüzden `'echo' adinda bir model saglayicisi kayitli degil` ile 400 döndü.
> Bu dosyanın geri kalanı (İzlek B, `echo` kullanan tüm case'ler: 030/060/061)
> için OpenAI anahtarını GEÇİCİ olarak kaldırdım; MT-PG-047'ye gelindiğinde
> geri ekleyip o case sonrası yine kaldıracağım. `dotnet user-secrets` dışında
> hiçbir yere yazılmadı.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-031 — `IConversationBranchStore` `TryAdd` önceliği

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

Sınır senaryosu. `UsePostgreSql()`'in kaynak kodundaki yorum açıktır: bu iki
depo `TryAddSingleton` ile kaydedilir — bir tüketici KENDİ uygulamasını
`UsePostgreSql()`'DEN ÖNCE kaydederse, tüketicininki kazanır.

**Ön koşul**
- [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md) `MT-PKG-070` geçti.

**Adımlar**
1. Sahte bir `IConversationBranchStore` uygulaması yaz.
2. `UsePostgreSql()`'DEN ÖNCE kaydet.
3. Çözümlenen tipi kontrol et.

**Girilecek veri**
```bash
rm -rf ~/agentprism-manuel/tryadd && mkdir -p ~/agentprism-manuel/tryadd
cd ~/agentprism-manuel/tryadd
dotnet new console -o . --force
cp ~/agentprism-manuel/uretec/nuget.config .
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.PostgreSql.*.nupkg | sed 's#.*AgentPrism.PostgreSql\.##;s#\.nupkg##')
dotnet add package AgentPrism.PostgreSql --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var services = new ServiceCollection();
services.AddAgentPrism();

// Tuketici KENDI uygulamasini UsePostgreSql()'DEN ONCE kaydeder.
services.TryAddSingleton<IConversationBranchStore, SahteDalStore>();

services.UsePostgreSql("Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism");

var resolved = services.BuildServiceProvider().GetRequiredService<IConversationBranchStore>();
Console.WriteLine("Cozumlenen tip: " + resolved.GetType().FullName);

internal sealed class SahteDalStore : IConversationBranchStore
{
    public ValueTask<ConversationBranchInfo> CreateBranchAsync(string tenantId, Guid parentConversationId, long? upToSequence, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("sahte uygulama - sadece kayit onceligi test eder");
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Çıktı `SahteDalStore` tipini gösterir — `SqlConversationBranchStore` DEĞİLDİR.
  (`ConversationBranchInfo`/imza koşumda derleme hatası verirse gerçek arayüz
  imzası `maf-api-kesfi` benzeri bir reflection ile önce doğrulanır ve metot
  gövdesi ona göre düzeltilir; bu case'in amacı önceliği kanıtlamaktır, imza
  ayrıntısı değil.)

**Gerçek sonuç**
> Beklendiği gibi — çıktı `Cozumlenen tip: SahteDalStore`. Doc'un anlattığı
> gibi, script iki noktada düzeltme gerektirdi (imza sürüklenmesi, kanıtlanan
> davranışı etkilemiyor): gerçek imza `BranchAsync(...)`/`ConversationBranch?`
> (`CreateBranchAsync`/`ConversationBranchInfo` DEĞİL), ve `UsePostgreSql(...)`
> `IServiceCollection` üzerinde değil `IAgentPrismBuilder` üzerinde bir
> extension — `var builder = services.AddAgentPrism();` yakalanıp
> `builder.UsePostgreSql(...)` çağrılması gerekti (`builder.Services` alttaki
> AYNI `IServiceCollection`'ı taşıyor, bu yüzden `TryAddSingleton` `services`
> üzerinden önce çağrılabiliyor).

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-032 — `IVectorSearchStore` aynı `TryAdd` önceliğine uyar

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | K4 |

**Ön koşul**
- MT-PG-031 projesi hazır (aynı desen, farklı arayüz).

**Adımlar**
1. Sahte bir `IVectorSearchStore` uygulaması `UsePostgreSql()`'DEN ÖNCE kaydet.
2. Çözümlenen tipi kontrol et.

**Girilecek veri**
```bash
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var services = new ServiceCollection();
services.AddAgentPrism();
services.TryAddSingleton<IVectorSearchStore, SahteVektorStore>();
services.UsePostgreSql("Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism");

var resolved = services.BuildServiceProvider().GetRequiredService<IVectorSearchStore>();
Console.WriteLine("Cozumlenen tip: " + resolved.GetType().FullName);

internal sealed class SahteVektorStore : IVectorSearchStore
{
    public int Dimensions => 1;
    public ValueTask UpsertAsync(string tenantId, string collection, string sourceId, IReadOnlyList<VectorChunk> chunks, CancellationToken cancellationToken = default) => default;
    public ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default) => new([]);
    public ValueTask<int> DeleteSourceAsync(string tenantId, string collection, string sourceId, CancellationToken cancellationToken = default) => new(0);
    public ValueTask<IReadOnlyList<string>> ListSourcesAsync(string tenantId, string collection, CancellationToken cancellationToken = default) => new(Array.Empty<string>());
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Çıktı `SahteVektorStore` tipini gösterir — `PgVectorSearchStore` DEĞİLDİR.

**Gerçek sonuç**
> Beklendiği gibi — `Cozumlenen tip: SahteVektorStore`. `IVectorSearchStore`
> imzası doc'la birebir eşleşti; tek düzeltme MT-PG-031'deki gibi
> `UsePostgreSql`'in `builder` üzerinden çağrılması oldu.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-033 — Diğer depolar `Replace` ile kayıtlıdır: tüketici önce kaydetse de PostgreSQL kazanır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-025 |

MT-PG-031/032'nin TERSİ: `IRunStore` gibi diğer yirmi civarı depo `Replace` ile
kaydedilir — kayıt sırası ÖNEMLİ DEĞİLDİR, `UsePostgreSql()` her zaman kazanır.

**Ön koşul**
- [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md) `MT-PKG-070` geçti.

**Adımlar**
1. Sahte bir `IRunStore` uygulaması `UsePostgreSql()`'DEN ÖNCE kaydet.
2. Çözümlenen tipi kontrol et.

**Girilecek veri**
```bash
rm -rf ~/agentprism-manuel/replace && mkdir -p ~/agentprism-manuel/replace
cd ~/agentprism-manuel/replace
dotnet new console -o . --force
cp ~/agentprism-manuel/uretec/nuget.config .
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.PostgreSql.*.nupkg | sed 's#.*AgentPrism.PostgreSql\.##;s#\.nupkg##')
dotnet add package AgentPrism.PostgreSql --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var services = new ServiceCollection();
services.AddAgentPrism();

// Tuketici IRunStore'u ONCE, TryAdd ile kaydeder.
services.TryAddSingleton<IRunStore, SahteRunStore>();

services.UsePostgreSql("Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism");

var resolved = services.BuildServiceProvider().GetRequiredService<IRunStore>();
Console.WriteLine("Cozumlenen tip: " + resolved.GetType().FullName);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Çıktı `SqlRunStore` tipini gösterir (`AgentPrism` ad alanında) —
  `SahteRunStore` **DEĞİLDİR**. `Replace` deseni `TryAdd`'in aksine önceki kaydı
  bilerek ezer; MT-PG-031/032'deki davranışla TAM TERSİDİR.

**Gerçek sonuç**
> Beklendiği gibi — `Cozumlenen tip: AgentPrism.SqlRunStore`, `SahteRunStore`
> DEĞİL. `IRunStore`'un 14 metotlu tam imzası doğrulanıp `throw new
> NotImplementedException()` gövdeleriyle derlendi (dosya:
> `src/AgentPrism.Abstractions/Runs/IRunStore.cs`); doc'un öngördüğü gibi
> yalnız derlenmesi yeterliydi, hiçbiri çalışmadı.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> Not: `SahteRunStore` sınıfının gövdesi koşumda `IRunStore`'un gerçek imzasına
> göre yazılır (`maf-api-kesfi` benzeri bir reflection ile önce doğrulanır); bu
> case'in amacı yalnız kazananın TİPİNİ göstermektir, metotların çalışması
> gerekmez (derlenmesi yeterlidir, hatta `throw new NotImplementedException()`
> gövdeleriyle de amaç sağlanır).

---

### MT-PG-034 — Aynı zincirde iki kalıcılık sağlayıcısı kayıtlıysa uyarı loglanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 23 |
| **İlgili karar** | K-183 |

Negatif senaryo. **Bu case örnek uygulamada geçici bir kod değişikliği
gerektirir** — test bitince adım 4'te geri alınır.

**Ön koşul**
- MT-PG-020 geçti, uygulama durdurulmuş.

**Adımlar**
1. `samples/AgentPrism.Api/Program.cs`'de `agentPrism.UsePostgreSql(postgreSql);`
   satırından (yaklaşık 641. satır) hemen SONRA şu satırı geçici olarak ekle:
   `agentPrism.UseSqlite(o => o.ConnectionString = "Data Source=manuel-test-ikinci.db");`
2. Uygulamayı başlat, açılış logunu oku.
3. `/api/diagnostics` ve `/health` ucunu çağır.
4. Değişikliği geri al.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | python3 -m json.tool | grep -E "persistenceProvider|registeredPersistenceProviders"
curl -s -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"
```
```bash
# Temizlik (adim 4):
git checkout -- samples/AgentPrism.Api/Program.cs
rm -f samples/AgentPrism.Api/manuel-test-ikinci.db
```

**Beklenen sonuç**
- Açılış logu `AgentPrism'de birden fazla kalicilik saglayicisi kayitli:
  PostgreSQL, SQLite.` (ya da çağrı sırasına göre ters) uyarısını taşır.
- `/api/diagnostics`: `registeredPersistenceProviders` = **2**;
  `persistenceProvider` = **son çağrılan** sağlayıcı (`SQLite` — `UseSqlite`
  `UsePostgreSql`'den SONRA eklendiği için `Replace` deseni gereği o kazanır).
- `/health` **Degraded** döner (`registeredPersistenceProviders: 2` verisiyle).

**Gerçek sonuç**
> **Kaldı — uyarı satırı doğru çıktı ama uygulama sonra çöktü; iki bağımsız
> denemede TUTARLI.** Log beklenen uyarıyı verdi: `AgentPrism'de birden fazla
> kalicilik saglayicisi kayitli: PostgreSQL, SQLite. Son kayit kazanir ve su an
> SQLite kullaniliyor. Yalnizca birini cagirin.` Ama birkaç saniye sonra AYNI
> `MT-PG-025` kusuruyla (`A2AApprovalGuardFilter`/`McpApprovalGuardFilter`)
> çöktü — `SQLite Error 1: 'no such table: agentprism_agent_definitions'` ile
> `crit` + `Application is shutting down...`. `Now listening` yazıldıktan hemen
> sonra süreç öldüğü için `/api/diagnostics` ve `/health` isteklerine hiç
> ULAŞILAMADI (`HTTP: 000`).
>
> **Kök neden — MT-PG-025'ten FARKLI bir tetikleyici, AYNI temel kusur:**
> burada `AutoApplyMigrations` açık; PostgreSQL şeması önceki case'lerden
> zaten TAM güncel olduğu için o sağlayıcının `MigrationHostedService`'i
> saniyeler içinde biter ve `SchemaReadyGate.MarkReady()`'yi HEMEN çağırır.
> Ama kazanan depo uygulamaları (`Replace` deseniyle) SQLite'a ait ve SQLite'ın
> KENDİ migration'ı (15 tablo, `agentprism_` şeması, sıfırdan) henüz
> BİTMEMİŞTİR — `SchemaReadyGate` tek, PAYLAŞILAN bir kapı, hangi sağlayıcının
> "gerçekten kazanan" olduğunu bilmiyor. Kapı, EN HIZLI biten sağlayıcı
> (burada zaten migrasyonlu PostgreSQL) tarafından açılıyor, ama guard filter'ın
> sorguladığı depo SQLite'a ait — tablo henüz yok. Aynı `A2AApprovalGuardFilter`/
> `McpApprovalGuardFilter` deseni (bkz. `MT-PG-025`) bunu `catch (Exception)` →
> `StopApplication()` ile karşılıyor.
>
> Bu, `MT-PG-025` ile AYNI temel tasarım kusurunun (guard filter'ların
> `SchemaReadyGate` açılmasını "sorguladığım tablo var" garantisi sanması)
> İKİNCİ, bağımsız bir tetikleyicisi. Çoklu sağlayıcı yanlış yapılandırması
> zaten "Kritik" işaretli bir negatif senaryo; gözlenen sonuç (Degraded yanıt
> yerine TAM çökme) doğrudan bu önem derecesini doğruluyor.
>
> ---
>
> **2026-08-14 yeniden koşum — Geçti.** İki düzeltme birlikte kapattı:
> 1. `2caa423` (S1-8) guard filter'ın `StopApplication` yolunu kaldırdı —
>    `MT-PG-025`'e bakın.
> 2. Asıl yarış bu koşumda düzeltildi: `MigrationHostedService.StartAsync`
>    **kaybeden** sağlayıcının dalında `_schemaReadyGate.MarkReady()`
>    çağırıyordu. Kapı paylaşılan TEK bir sinyaldir; kaybeden onu anında
>    açınca kazananın migration'ı daha bitmeden bekleyen arka plan servisleri
>    boş şemaya sorgu atıyordu. Kaybeden artık kapıyı **açmıyor**; kazanan her
>    yolda (`AutoApplyMigrations` kapalı olsa bile) `MarkReady` çağırdığı için
>    kapı asla açılmadan kalmaz.
>
> Ampirik (geçici `UseSqlite` satırı `UsePostgreSql`'den SONRA, PostgreSQL
> şeması zaten güncel):
> - Uyarı satırı birebir beklendiği gibi: `AgentPrism'de birden fazla kalicilik
>   saglayicisi kayitli: PostgreSQL, SQLite. Son kayit kazanir ve su an SQLite
>   kullaniliyor. Yalnizca birini cagirin.`
> - `registeredPersistenceProviders`: **2** · `persistenceProvider`: **SQLite**
>   (son çağrılan kazanır) · `/health`: **Degraded**, `HTTP 200`.
> - `no such table` hatası: **0** · `Application is shutting down`: **0**.
>
> Geçici kod değişikliği adım 4'e göre geri alındı (`git diff` boş).

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-035 — Sağlayıcı çağrı sırası değişirse kazanan değişir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-025 |

MT-PG-034'ün devamı: sıra tersine çevrilir.

**Ön koşul**
- MT-PG-034'ün geçici kodu HENÜZ geri alınmadı (veya yeniden uygulanır), ama bu
  kez `UseSqlite(...)` satırı `agentPrism.UsePostgreSql(postgreSql);`'DEN ÖNCE
  eklenir.

**Adımlar**
1. Sırayı değiştir: önce `UseSqlite`, sonra `UsePostgreSql`.
2. Uygulamayı başlat.
3. `/api/diagnostics`'i çağır.
4. Değişikliği geri al.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | python3 -m json.tool | grep persistenceProvider
```
```bash
# Temizlik:
git checkout -- samples/AgentPrism.Api/Program.cs
rm -f samples/AgentPrism.Api/manuel-test-ikinci.db
```

**Beklenen sonuç**
- `persistenceProvider` artık **PostgreSQL**'dir (son çağrı yine kazanır, bu kez
  PostgreSQL sondadır). `registeredPersistenceProviders` yine **2**'dir.

**Gerçek sonuç**
> Beklendiği gibi — bu kez ÇÖKMEDEN. `AgentPrism'de birden fazla kalicilik
> saglayicisi kayitli: SQLite, PostgreSQL. Son kayit kazanir ve su an
> PostgreSQL kullaniliyor.` uyarısı, `persistenceProvider: "PostgreSQL"`,
> `registeredPersistenceProviders: 2`, `/health` → `Degraded` (200).
>
> **MT-PG-034'ün kök nedenini doğrulayan kontrast:** burada uygulama
> ÇÖKMEDİ çünkü kazanan sağlayıcı (PostgreSQL) önceki case'lerden zaten TAM
> migrasyonlu — `SchemaReadyGate` hangi sağlayıcı tarafından açılırsa açılsın,
> sorgulanan tablolar zaten vardı. MT-PG-034'te kazanan SQLite'tı ve SQLite'ın
> KENDİ migrasyonu henüz bitmemişken kapı (muhtemelen daha hızlı biten
> PostgreSQL tarafından) açılmıştı — bu yüzden orada çöktü, burada çökmedi.
> Yani çökme, "hangi sağlayıcı kazanıyor" değil "kapıyı açan sağlayıcının
> migrasyonu, KAZANANIN tablolarını garanti etmiyor" sorunudur — bkz. MT-PG-034.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — `pgvector` / `IVectorSearchStore` depolama katmanı

`PgVectorSearchStore` `IVectorSearchStore`'un **tek** somut uygulamasıdır (K4).
Bu bölüm yükleme/parçalama kalitesini DEĞİL, depolama sözleşmesini kanıtlar:
boyut zorunluluğu, kiracı yalıtımı, upsert-üzerine-yazma, kosinüs sıralaması.
Konsol uygulamaları `IVectorSearchStore`'u DI'dan çözerek gerçek embedding
üretmeden (OpenAI anahtarı gerekmeden) deterministik vektörlerle çalışır.

### MT-PG-040 — `pgvector` eklentisi ve HNSW indeksi migration sonrası kuruludur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
- MT-PG-020 geçti (`0024_vector.sql` uygulanmış).

**Adımlar**
1. Eklenti kurulumunu doğrula.
2. İndeksleri listele.

**Girilecek veri**
```sql
SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';
SELECT indexname FROM pg_indexes WHERE schemaname = 'agentprism' AND tablename = 'document_embeddings' ORDER BY indexname;
SELECT indexdef FROM pg_indexes WHERE indexname = 'document_embeddings_hnsw_idx';
```

**Beklenen sonuç**
- `vector` eklentisi kuruludur (bir satır döner).
- İndeks listesi `document_embeddings_hnsw_idx`,
  `document_embeddings_tenant_collection_idx`,
  `document_embeddings_tenant_collection_source_idx`,
  `document_embeddings_tenant_created_idx` adlarını taşır (artı birincil
  anahtar/benzersizlik kısıtı indeksleri).
- `document_embeddings_hnsw_idx` tanımı `USING hnsw` VE `vector_cosine_ops`
  ifadelerini içerir.

**Gerçek sonuç**
> Beklendiği gibi. `vector` eklentisi kurulu (`extversion 0.8.6`). İndeks
> listesi dörtünü de taşıyor, artı `document_embeddings_pkey` ve
> `document_embeddings_uq`. `document_embeddings_hnsw_idx` tanımı:
> `CREATE INDEX ... USING hnsw (embedding vector_cosine_ops)`.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-041 — Embedding uzunluğu depo boyutuyla eşleşmezse `UpsertAsync` reddedilir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- MT-PG-020 geçti. [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md)
  `MT-PKG-070` geçti.

**Adımlar**
1. Konsol projesi kur, `IVectorSearchStore`'u çözümle.
2. `Dimensions` değerini yazdır.
3. Yanlış uzunlukta bir embedding ile `UpsertAsync` çağır.

**Girilecek veri**
```bash
rm -rf ~/agentprism-manuel/vektor && mkdir -p ~/agentprism-manuel/vektor
cd ~/agentprism-manuel/vektor
dotnet new console -o . --force
cp ~/agentprism-manuel/uretec/nuget.config .
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.PostgreSql.*.nupkg | sed 's#.*AgentPrism.PostgreSql\.##;s#\.nupkg##')
dotnet add package AgentPrism.PostgreSql --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddAgentPrism().UsePostgreSql("Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism");

var provider = services.BuildServiceProvider();
var store = provider.GetRequiredService<IVectorSearchStore>();

Console.WriteLine("Dimensions: " + store.Dimensions);

try
{
    await store.UpsertAsync("kiraci-alfa", "manuel-koleksiyon", "kaynak-kisa", new[]
    {
        new VectorChunk { Index = 0, Content = "test", Embedding = new float[10] },
    });
    Console.WriteLine("🚨 istisna ATILMADI");
}
catch (ArgumentException ex)
{
    Console.WriteLine("beklenen istisna: " + ex.Message);
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `Dimensions: 1536` yazar (varsayılan).
- İkinci çıktı `beklenen istisna:` ile başlar ve `10` ile `1536` sayılarını
  taşır.
- `🚨 istisna ATILMADI` satırı görünmez.

**Gerçek sonuç**
> Beklendiği gibi. `Dimensions: 1536`. İkinci satır: `beklenen istisna: Parca 0
> gomu uzunlugu (10) depo boyutuyla (1536) eslesmiyor. (Parameter 'chunks')`.
> `ISTISNA ATILMADI` görünmedi.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-042 — Aynı kaynak yeniden yazılırsa eski parçalar silinir (upsert-üzerine-yazma)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
- MT-PG-041 projesi hazır.

**Adımlar**
1. Aynı `sourceId` ile iki parça yaz.
2. Aynı `sourceId`'yi TEK parçayla yeniden yaz.
3. Kaynak sayısını ve arama sonucunu oku.

**Girilecek veri**
```bash
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

float[] Axis(int index) { var v = new float[1536]; v[index] = 1f; return v; }

var services = new ServiceCollection();
services.AddAgentPrism().UsePostgreSql("Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism");
var store = services.BuildServiceProvider().GetRequiredService<IVectorSearchStore>();

await store.UpsertAsync("kiraci-alfa", "manuel-koleksiyon", "manuel-kaynak", new[]
{
    new VectorChunk { Index = 0, Content = "ilk surum, parca 0", Embedding = Axis(0) },
    new VectorChunk { Index = 1, Content = "ilk surum, parca 1", Embedding = Axis(1) },
});

await store.UpsertAsync("kiraci-alfa", "manuel-koleksiyon", "manuel-kaynak", new[]
{
    new VectorChunk { Index = 0, Content = "ikinci surum, tek parca", Embedding = Axis(2) },
});

var hits = await store.SearchAsync(new VectorSearchRequest
{
    TenantId = "kiraci-alfa", Collection = "manuel-koleksiyon", QueryEmbedding = Axis(2), Top = 10,
});

Console.WriteLine("arama sonuc sayisi (ikinci yazimdan sonra): " + hits.Count);
foreach (var hit in hits)
{
    Console.WriteLine($"  {hit.SourceId} parca={hit.ChunkIndex} icerik='{hit.Content}' mesafe={hit.Distance:F4}");
}
EOF

dotnet run -c Release
```
```sql
SELECT source_id, chunk_index, content FROM agentprism.document_embeddings
WHERE tenant_id = 'kiraci-alfa' AND collection = 'manuel-koleksiyon';
```

**Beklenen sonuç**
- `arama sonuc sayisi (ikinci yazimdan sonra): 1` — ikinci yazımdan sonra tek
  parça vardır; eski iki parça SİLİNMİŞTİR.
- `content` alanı `ikinci surum, tek parca` gösterir; `ilk surum` metni HİÇBİR
  YERDE görünmez.

**Gerçek sonuç**
> Beklendiği gibi. `arama sonuc sayisi (ikinci yazimdan sonra): 1`, tek satır
> `manuel-kaynak parca=0 icerik='ikinci surum, tek parca' mesafe=0,0000`. SQL
> sorgusu da tek satır döndü, aynı içerik. `ilk surum` metni hiçbir yerde yok.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-043 — Arama kosinüs mesafesine göre artan sıralı döner

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

Beklenen sonuç model metnine değil, **cebirsel bir olguya** bağlanır: birim
eksen vektörleri arası kosinüs mesafesi hesaplanabilir ve değişmezdir.

**Ön koşul**
- MT-PG-041 projesi hazır.

**Adımlar**
1. Üç dik birim vektör yaz (`Axis(0)`, `Axis(1)`, `Axis(2)`).
2. `Axis(0)` sorgusuyla ara.

**Girilecek veri**
```bash
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

float[] Axis(int index) { var v = new float[1536]; v[index] = 1f; return v; }

var services = new ServiceCollection();
services.AddAgentPrism().UsePostgreSql("Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism");
var store = services.BuildServiceProvider().GetRequiredService<IVectorSearchStore>();

await store.UpsertAsync("kiraci-alfa", "siralama-testi", "eksen-0", new[] { new VectorChunk { Index = 0, Content = "eksen 0", Embedding = Axis(0) } });
await store.UpsertAsync("kiraci-alfa", "siralama-testi", "eksen-1", new[] { new VectorChunk { Index = 0, Content = "eksen 1", Embedding = Axis(1) } });
await store.UpsertAsync("kiraci-alfa", "siralama-testi", "eksen-2", new[] { new VectorChunk { Index = 0, Content = "eksen 2", Embedding = Axis(2) } });

var hits = await store.SearchAsync(new VectorSearchRequest
{
    TenantId = "kiraci-alfa", Collection = "siralama-testi", QueryEmbedding = Axis(0), Top = 10,
});

foreach (var hit in hits)
{
    Console.WriteLine($"{hit.SourceId} mesafe={hit.Distance:F4}");
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- İlk satır `eksen-0 mesafe=0.0000`'dir (sorgu vektörüyle aynı yön → kosinüs
  mesafesi 0).
- Sonraki iki satır `eksen-1` ve `eksen-2`'dir, ikisi de `mesafe=1.0000`
  (dik vektörler → kosinüs mesafesi 1); aralarındaki sıra ÖNEMLİ DEĞİLDİR ama
  ikisi de `eksen-0`'dan SONRA gelmelidir.
- Liste **artan mesafe** sırasındadır.

**Gerçek sonuç**
> Beklendiği gibi. `eksen-0 mesafe=0,0000`, ardından `eksen-1 mesafe=1,0000`
> ve `eksen-2 mesafe=1,0000` — artan sırada.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-044 — İki kiracı aynı koleksiyon/kaynak kimliğini paylaşsa da birbirini görmez

| | |
|---|---|
| **İzlek** | A |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

Negatif/güvenlik senaryosu. `tenant_id` yalıtımı SQL `WHERE` filtresiyle
uygulanır (birincil anahtar veya benzersizlik kısıtı DEĞİL).

**Ön koşul**
- MT-PG-041 projesi hazır.

**Adımlar**
1. `kiraci-alfa` ve `kiraci-beta` AYNI koleksiyon/kaynak kimliğine farklı içerik
   yazsın.
2. `kiraci-beta` olarak ara.

**Girilecek veri**
```bash
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

float[] Axis(int index) { var v = new float[1536]; v[index] = 1f; return v; }

var services = new ServiceCollection();
services.AddAgentPrism().UsePostgreSql("Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism");
var store = services.BuildServiceProvider().GetRequiredService<IVectorSearchStore>();

await store.UpsertAsync("kiraci-alfa", "paylasimli-koleksiyon", "ortak-kaynak", new[] { new VectorChunk { Index = 0, Content = "ALFA'nin gizli belgesi", Embedding = Axis(0) } });
await store.UpsertAsync("kiraci-beta", "paylasimli-koleksiyon", "ortak-kaynak", new[] { new VectorChunk { Index = 0, Content = "BETA'nin gizli belgesi", Embedding = Axis(0) } });

var betaGorur = await store.SearchAsync(new VectorSearchRequest
{
    TenantId = "kiraci-beta", Collection = "paylasimli-koleksiyon", QueryEmbedding = Axis(0), Top = 10,
});

Console.WriteLine("kiraci-beta " + betaGorur.Count + " sonuc goruyor:");
foreach (var hit in betaGorur) Console.WriteLine("  " + hit.Content);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `kiraci-beta` yalnız 1 sonuç görür, içeriği `BETA'nin gizli belgesi`'dir.
- `ALFA'nin gizli belgesi` metni ASLA görünmez.

**Gerçek sonuç**
> Beklendiği gibi. `kiraci-beta 1 sonuc goruyor: BETA'nin gizli belgesi`.
> `ALFA'nin gizli belgesi` hiç görünmedi — kiracı yalıtımı korunuyor.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-045 — Kaynak silindiğinde tüm parçaları kaybolur

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
- MT-PG-041 projesi hazır.

**Adımlar**
1. Üç parçalı bir kaynak yaz.
2. Kaynağı sil.
3. Kalan kaynak sayısını oku.

**Girilecek veri**
```bash
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

float[] Axis(int index) { var v = new float[1536]; v[index] = 1f; return v; }

var services = new ServiceCollection();
services.AddAgentPrism().UsePostgreSql("Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism");
var store = services.BuildServiceProvider().GetRequiredService<IVectorSearchStore>();

await store.UpsertAsync("kiraci-alfa", "silme-testi", "silinecek-kaynak", new[]
{
    new VectorChunk { Index = 0, Content = "parca 0", Embedding = Axis(0) },
    new VectorChunk { Index = 1, Content = "parca 1", Embedding = Axis(1) },
    new VectorChunk { Index = 2, Content = "parca 2", Embedding = Axis(2) },
});

var silinen = await store.DeleteSourceAsync("kiraci-alfa", "silme-testi", "silinecek-kaynak");
var kalanKaynaklar = await store.ListSourcesAsync("kiraci-alfa", "silme-testi");

Console.WriteLine("silinen parca sayisi: " + silinen);
Console.WriteLine("kalan kaynak sayisi: " + kalanKaynaklar.Count);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `silinen parca sayisi: 3`
- `kalan kaynak sayisi: 0`

**Gerçek sonuç**
> Beklendiği gibi. `silinen parca sayisi: 3`, `kalan kaynak sayisi: 0`.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-046 — `MaxDistance` filtresi uzak sonuçları eler

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
- MT-PG-041 projesi hazır.

**Adımlar**
1. Biri sorguya yakın, biri dik iki kayıt yaz.
2. `MaxDistance` OLMADAN ve `MaxDistance=0.5` İLE ara, sonuç sayılarını
   karşılaştır.

**Girilecek veri**
```bash
cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

float[] Axis(int index) { var v = new float[1536]; v[index] = 1f; return v; }

var services = new ServiceCollection();
services.AddAgentPrism().UsePostgreSql("Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism");
var store = services.BuildServiceProvider().GetRequiredService<IVectorSearchStore>();

await store.UpsertAsync("kiraci-alfa", "mesafe-testi", "yakin", new[] { new VectorChunk { Index = 0, Content = "yakin", Embedding = Axis(0) } });
await store.UpsertAsync("kiraci-alfa", "mesafe-testi", "uzak", new[] { new VectorChunk { Index = 0, Content = "uzak", Embedding = Axis(1) } });

var filtresiz = await store.SearchAsync(new VectorSearchRequest
{
    TenantId = "kiraci-alfa", Collection = "mesafe-testi", QueryEmbedding = Axis(0), Top = 10,
});
var filtreli = await store.SearchAsync(new VectorSearchRequest
{
    TenantId = "kiraci-alfa", Collection = "mesafe-testi", QueryEmbedding = Axis(0), Top = 10, MaxDistance = 0.5,
});

Console.WriteLine("filtresiz: " + filtresiz.Count);
Console.WriteLine("filtreli (<=0.5): " + filtreli.Count);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `filtresiz: 2`
- `filtreli (<=0.5): 1` — yalnız `yakin` (mesafe 0.0) kalır; `uzak`
  (mesafe 1.0) elenir.

**Gerçek sonuç**
> Beklendiği gibi. `filtresiz: 2`, `filtreli (<=0.5): 1`.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-047 — Koleksiyon adı geçersiz karakter taşıyorsa HTTP ucu 400 döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

Negatif senaryo. `KnowledgeIngestionService.RequireValidCollectionName` yalnız
harf, rakam, alt çizgi, tire kabul eder.

**Ön koşul**
- Örnek uygulama OpenAI anahtarıyla çalışıyor (`bilgi-asistani` agent'ı ve
  embedding üretici kayıtlı).

**Adımlar**
1. Koleksiyon adında `/` içeren bir istek gönder.
2. Geçerli bir koleksiyon adıyla ama uyuşmayan boyutlu bir embedding gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/gecersiz%2Fkoleksiyon/documents" \
  -H "$APB" -H "content-type: application/json" \
  -d '{"sourceId":"test","chunks":[{"index":0,"content":"test","embedding":[0.1]}]}'

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/knowledge/gecerli-koleksiyon_1/documents" \
  -H "$APB" -H "content-type: application/json" \
  -d '{"sourceId":"test","chunks":[{"index":0,"content":"test","embedding":[0.1]}]}'
```

**Beklenen sonuç**
- 1. istek **400** döner; hata mesajı geçersiz koleksiyon adını taşır.
- 2. istek de **400** döner ama FARKLI bir mesajla (`gomu uzunlugu` ifadesini
  taşır — embedding uzunluğu 1, depo boyutu 1536) — bu, iki doğrulamanın
  BAĞIMSIZ çalıştığını kanıtlar.

**Gerçek sonuç**
> Beklendiği gibi. 1. istek **400**: `'gecersiz%2Fkoleksiyon' gecerli bir
> koleksiyon adi degil. Yalniz harf, rakam, alt cizgi ve tire icerebilir.` 2.
> istek de **400** ama FARKLI mesajla: `Parca 0 gomu uzunlugu (1) depo
> boyutuyla (1536) eslesmiyor.` — iki doğrulama bağımsız.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Teşhis ve sağlık entegrasyonu (PostgreSQL'e özgü)

`MigrationRunner` `ISqlPersistenceDiagnostics`'i uygular; `/health` ve
`/agentprism/api/diagnostics` bu sözleşme üzerinden PostgreSQL bağlantı ve
migration durumunu okur. Genel sözleşme (sağlayıcı bağımsız alanlar, model
sağlayıcı devre kesici durumu) [`25-SAGLIK-TESHIS-OPENAPI.md`](25-SAGLIK-TESHIS-OPENAPI.md)'dedir;
burada yalnız PostgreSQL'in ürettiği veriler sınanır.

### MT-PG-050 — `/health` PostgreSQL erişilemezken Unhealthy döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

**Ön koşul**
- Uygulama normal çalışıyor (migration'lar tamam).

**Adımlar**
1. Container'ı durdur.
2. `/health`'i çağır.
3. Container'ı yeniden başlat, kısa süre bekle.
4. `/health`'i tekrar çağır.

**Girilecek veri**
```bash
docker stop ap-pg
curl -s -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"

docker start ap-pg
sleep 3
curl -s -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"
```

**Beklenen sonuç**
- Container durdurulmuşken `/health` **503** döner; gövde `Unhealthy` durumunu
  ve `Kalicilik veritabanina erisilemiyor` benzeri bir mesajı taşır.
- Container yeniden başladıktan sonra (uygulama YENİDEN BAŞLATILMADAN) `/health`
  tekrar **200** (`Healthy`) döner — Npgsql havuzu kendiliğinden toparlanır.

**Gerçek sonuç**
> **Kısmen Kaldı.** PostgreSQL'e özgü davranış TAM beklendiği gibi: container
> durdurulmuşken `/health` **503 Unhealthy** döndü; container yeniden
> başlayıp 3 saniye beklendikten sonra (uygulama YENİDEN BAŞLATILMADAN)
> `/health` **200**'e döndü — Npgsql havuzu kendiliğinden toparlandı, bu
> case'in asıl kanıtladığı şey budur.
>
> Ama gövde `Healthy` DEĞİL, `Degraded` gösterdi. Kök neden: `AgentPrismHealthCheck`
> (`src/AgentPrism.AspNetCore/Health/AgentPrismHealthCheck.cs:70-77`) `Healthy`
> için `report.ModelProviders.Any(status == Healthy)` şartını arıyor — ve
> `echo` sağlayıcısı (bu dosyanın tamamında ağa çıkmamak için kullanılan tek
> sağlayıcı) `ModelProviders` listesinde HİÇ YER ALMIYOR (yalnız
> openai/openai-responses/openrouter/anthropic/google izleniyor). Yani
> `echo`-only bir kurulumda `/health` YAPISAL OLARAK asla düz `Healthy`
> döndüremez — en iyi ihtimalle `Degraded` durur. Bu, PostgreSQL'in DEĞİL, bu
> dosyanın kendi sınır tablosunun "model sağlayıcı devre kesici durumu
> `25-SAGLIK-TESHIS-OPENAPI.md`'dedir, kapsam dışı" dediği bir alanın
> (§ "Bu dosya nerede biter") case metnine sızmasıdır — case'in beklenen
> sonucu kendi belirlediği sınırın dışına taşmış. PostgreSQL'e özgü kısım
> (`canConnect`, `migrationsUpToDate`) doğrulandı; `Healthy` etiketi doğrulanamadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☒ Kaldı · ☐ Atlandı

---

### MT-PG-051 — `/agentprism/api/diagnostics` bekleyen migration'ları listeler, hiçbir `secret` taşımaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | K-059 |

**Ön koşul**
- Boş şema, `AutoApplyMigrations=false`.

**Adımlar**
1. Şemayı düşür, otomatik migration'ı kapat, uygulamayı başlat.
2. Teşhis ucunu çağır.

**Girilecek veri**
```bash
$PG -c "DROP SCHEMA IF EXISTS agentprism CASCADE;"
dotnet user-secrets set "AgentPrism:PostgreSql:AutoApplyMigrations" "false"
cd samples/AgentPrism.Api && dotnet run
```
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `migrationsUpToDate: false`.
- `pendingMigrations` **28** öğe taşır (`0001_initial`'dan
  `0028_experiment_canary`'e).
- Gövdenin hiçbir yerinde bağlantı dizesi, parola veya `Password=` alt dizgisi
  geçmez.

**Gerçek sonuç**
> _(2026-08-13 koşumu: Kaldı — `MT-PG-025` uygulamayı çökertiyordu, istek hiç
> gönderilemedi.)_
>
> **2026-08-14 yeniden koşum — Geçti.** İki ayrı düzeltme gerekti:
> 1. Çökme `2caa423` (S1-8) ile zaten kapanmıştı: `ExternalSurfaceGuard`
>    `ListCatalogWithRetryAsync` ile katalog sorgusunu üstel geri çekilmeyle
>    yeniden dener, uygulamayı durdurmaz. Ampirik: `shutdown` satırı **0**,
>    `Now listening` yazıldı, süreç ayakta.
> 2. Ama uç yine de **HTTP 500** dönüyordu — `AgentPrismDiagnosticsCollector`
>    `_agentCatalog.ListAsync`'i korumasız çağırıyor ve hata, YUKARIDA
>    toplanmış migration bilgisini de çöpe atıyordu. Teşhis ucunun birincil
>    kullanım anı tam da budur. Düzeltildi: katalog sorgusu artık `try/catch`
>    içinde, `AgentCount` `int?` oldu ve okunamayınca `null` gelir.
>
> Gözlenen gövde: `persistenceProvider: PostgreSQL`, `canConnect: true`,
> `migrationsUpToDate: false`, `pendingMigrations` **28 kalem**
> (`0001_initial` … `0028_experiment_canary`), `agentCount: null`,
> `toolCount: 6`. `Password=`/bağlantı dizesi/`sk-` taraması: **0 eşleşme**.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `dotnet user-secrets remove "AgentPrism:PostgreSql:AutoApplyMigrations"`, reset yordamı — uygulandı.

---

### MT-PG-052 — Teşhis ucu migration UYGULAMAZ (salt okunur)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

Sınır senaryosu.

**Ön koşul**
- MT-PG-051 durumunda (28 bekleyen migration, `AutoApplyMigrations=false`).

**Adımlar**
1. Teşhis ucunu ÜÇ kez art arda çağır.
2. `__migrations` tablosunun hâlâ oluşturulmadığını doğrula.

**Girilecek veri**
```bash
for i in 1 2 3; do
  curl -s "$APU/api/diagnostics" -H "$APB" \
    | python3 -c "import sys,json; d=json.load(sys.stdin); print(len(d['pendingMigrations']))"
done
```
```sql
SELECT to_regclass('agentprism.__migrations');
```

**Beklenen sonuç**
- Üç çağrının üçü de **28** yazdırır (değişmez).
- `to_regclass` **NULL** döner — `__migrations` tablosu hâlâ oluşturulmamıştır;
  teşhis ucu şemaya hiçbir şey YAZMAZ.

**Gerçek sonuç**
> _(2026-08-13 koşumu: Kaldı — ön koşul `MT-PG-025` yüzünden kurulamıyordu.)_
>
> **2026-08-14 yeniden koşum — Geçti.** Ön koşul artık kurulabiliyor
> (`MT-PG-051`'e bakın). Üç art arda çağrının üçü de **28** yazdırdı; sayı
> değişmedi. `SELECT to_regclass('mt_fin.__migrations')` **NULL** döndü —
> `__migrations` tablosu oluşturulmadı, teşhis ucu şemaya hiçbir şey yazmıyor.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-053 — Migration tamamsa ve bir model sağlayıcı sağlıklıysa `/health` Healthy döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 33 |
| **İlgili karar** | — |

**Ön koşul**
- Reset yordamı uygulanmış, `AutoApplyMigrations` kaldırılmış (varsayılan
  `true`), uygulama normal başlatılmış (28 migration otomatik uygulanır), en az
  bir model sağlayıcı (`echo` yeterli) kayıtlı.

**Adımlar**
1. `/health`'i çağır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"
```

**Beklenen sonuç**
- **200**, gövde `Healthy` durumunu gösterir.

**Gerçek sonuç**
> **Kaldı.** **200** döndü ama gövde `Degraded`, `Healthy` DEĞİL. Kök neden
> `MT-PG-050`'de belgelendi: `echo` sağlayıcısı `AgentPrismHealthCheck`'in
> izlediği `ModelProviders` listesinde hiç yer almıyor, bu yüzden "en az bir
> model sağlayıcısı sağlıklı" koşulu `echo`-only bir kurulumda YAPISAL OLARAK
> hiçbir zaman sağlanamıyor. Doc'un "echo yeterli" varsayımı bu case için
> YANLIŞ — plan `Healthy` sonucu her koşumda değişmez biçimde `Degraded`'e
> düşer, kod/veri kusuru değil, case'in ön koşul varsayımı hatalı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☒ Kaldı · ☐ Atlandı

---

# 6 — Sınır durumları: yük ve bağlantı kesintisi

### MT-PG-060 — 20 eşzamanlı yazma isteği veri bozulmadan tamamlanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Hafif yük senaryosu (bkz. `PROMPT.md` §3 Kapsam kararları).

**Ön koşul**
- Uygulama normal çalışıyor.

**Adımlar**
1. 20 agent kaydını EŞ ZAMANLI gönder.
2. Kaydedilen sayıyı doğrula.

**Girilecek veri**
```bash
for i in $(seq 1 20); do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
    -H "content-type: application/json" -d "{
    \"name\": \"manuel-esz-$i\", \"model\": { \"provider\": \"echo\", \"model\": \"echo-1\" }
  }" &
done
wait
```
```sql
SELECT count(*) FROM agentprism.agent_definitions WHERE name LIKE 'manuel-esz-%';
```

**Beklenen sonuç**
- 20 HTTP kodunun tümü 2xx'tir.
- SQL sorgusu **20** döner — hiçbir istek kaybolmaz.
- Uygulama loglarında bağlantı havuzu tükenmesi hatası (`Npgsql...TimeoutException`
  veya benzeri) görünmez.

**Gerçek sonuç**
> Beklendiği gibi. 20 isteğin 20'si de **201** döndü. `count(*) = 20`. Loglarda
> `TimeoutException`/pool tükenmesi hatası yok.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PG-061 — PostgreSQL koşum sırasında durursa çalışan bir istek anlaşılır hatayla başarısız olur, uygulama çökmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Negatif/sınır senaryosu. `docker stop`/`docker start` bir "kopan bağlantı"
simülasyonudur.

**Ön koşul**
- MT-PG-060 geçti (`manuel-esz-1` agent'ı var).

**Adımlar**
1. Container'ı durdur.
2. Var olan bir agent'ı çalıştırmayı dene.
3. Container'ı yeniden başlat, kısa süre bekle.
4. Aynı isteği tekrarla.

**Girilecek veri**
```bash
docker stop ap-pg
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/manuel-esz-1/run" -H "$APB" \
  -H "content-type: application/json" -d '{"message":"merhaba"}'

docker start ap-pg
sleep 3
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/manuel-esz-1/run" -H "$APB" \
  -H "content-type: application/json" -d '{"message":"merhaba"}'
```

**Beklenen sonuç**
- Container durdurulmuşken istek 5xx döner; `dotnet run` süreci ÇÖKMEZ (terminal
  hâlâ çalışıyor).
- Container yeniden başladıktan sonra AYNI istek başarıyla tamamlanır — Npgsql
  havuzu kendiliğinden yeniden bağlanır, uygulamanın yeniden başlatılması
  GEREKMEZ.

**Gerçek sonuç**
> Beklendiği gibi. Container durdurulmuşken istek **500** döndü (`ProblemDetails`
> gövdesi, `traceId` taşıyor), süreç ÇÖKMEDİ. Container yeniden başlayıp 3
> saniye beklendikten sonra AYNI istek **200** ile SSE akışını tamamladı
> (`Echo: merhaba`) — uygulama yeniden başlatılmadan Npgsql havuzu kendiliğinden
> toparlandı.

**Durum:** ☐ Beklemede · ☒ Geçti · ☐ Kaldı · ☐ Atlandı

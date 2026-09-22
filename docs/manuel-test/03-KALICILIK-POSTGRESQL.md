# 03 — Kalıcılık: PostgreSQL (`PG`)

> **Alan kodu:** `PG` · **Faz:** 2 (ayrıca 51: pgvector)
> **Kaynak:** `src/Tracon.PostgreSql` (`TraconPostgreSqlBuilderExtensions.cs` ·
> `TraconPostgreSqlOptions.cs` · `TraconPostgreSqlOptionsValidator.cs` ·
> `Internal/NpgsqlDataSourceFactory.cs` · `Internal/PostgresDialect.cs` ·
> `Internal/PostgresQueries.cs` · `Stores/PgVectorSearchStore.cs` · `Migrations/*.sql`)
>
> Migration çalıştırma motoru (`MigrationRunner`, `MigrationHostedService`) ve depo
> uygulamaları (`SqlAgentDefinitionStore` vb.) `src/Tracon.Sql.Shared` içinde
> yaşar ve SQLite/SQL Server ile ORTAKTIR; bu dosya onları yalnız PostgreSQL
> sağlayıcısı üzerinden, PostgreSQL'e özgü SQL metni ve diyalekt davranışıyla
> (advisory lock, `jsonb`, `pgvector`) sınar. Sağlayıcılar arası karşılaştırma
> [`04-KALICILIK-DIGER.md`](04-KALICILIK-DIGER.md)'dedir.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** son tur (2026-09-16):
> [`../arsiv/manuel-test-kosum-2026-09/03-KALICILIK-POSTGRESQL.md`](../arsiv/manuel-test-kosum-2026-09/03-KALICILIK-POSTGRESQL.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır. 2026-08-13 turunun kaydı silindi (K-847);
> tam metin: git show 64c8a103:docs/manuel-test/kosumlar/2026-08-13/03-KALICILIK-POSTGRESQL.md

---

## Bu dosya neyi kanıtlar

`UsePostgreSql()` çağrıldığında Tracon'in bellek içi varsayılanlarının yerini
alan zincir. Bağlantı ve şema ayarları doğrulanır, tek bir `NpgsqlDataSource`
kurulur, açılışta gömülü SQL migration'ları bir öneri kilidiyle korunarak
uygulanır, yirmi civarı depo `Replace` ile (ikisi `TryAdd` ile) değiştirilir ve
bazıları denetim izi dekoratörüyle sarılır. `pgvector` tabanlı vektör deposu
yalnız bu paketin uygulamasıdır.

```mermaid
flowchart TD
    A["UsePostgreSql()"] --> B["TraconPostgreSqlOptions + Validator"]
    B --> C["NpgsqlDataSource"]
    C --> D["SqlStoreContext (PostgresDialect)"]
    D --> E["MigrationHostedService"]
    E -->|"pg_advisory_lock"| F["MigrationRunner: 32 cekirdek + istege bagli knowledge"]
    F --> G["SchemaReadyGate acilir"]
    D --> H["Depo kayitlari: Replace / TryAdd"]
    H --> I["Denetim izi dekoratorleri"]
    D --> J["PgVectorSearchStore (pgvector)"]
    D --> K["ISqlPersistenceDiagnostics"]
    K --> L["/health, /tracon/api/diagnostics"]
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
3. `Tracon:PostgreSql:ConnectionString` `dotnet user-secrets` içinde tanımlıdır
   (bkz. `00-INDEKS.md` §2.4). Bazı case'ler bu değeri **geçici olarak** değiştirir;
   her case kendi temizlik adımını taşır.
4. `Tracon:Ui:AuthToken` `manuel-test-token-2026`'dır.
5. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080`

Kısaltmalar — bu dosyadaki her `curl`/`psql` şunları kullanır:

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
export PG="docker exec -i ap-pg psql -U postgres -d tracon"
```

> **İzlek A notu.** Bu dosyadaki konsol uygulamaları [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md)
> `MT-PKG-070`'in kurduğu yerel NuGet feed'ini (`~/tracon-local-feed`) kullanır.
> `Tracon.PostgreSql` bağımsız bir konsol uygulamasından **model çağırmadan**
> `IVectorSearchStore` gibi arayüzlere erişmeyi sağlar — Npgsql bağlantısı gerçektir
> ama hiçbir LLM sağlayıcısı gerekmez.

---

# 1 — Bağlantı, ayarlar ve doğrulama

Bu bölüm `TraconPostgreSqlOptions`, `TraconPostgreSqlOptionsValidator` ve
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
dotnet user-secrets set "Tracon:PostgreSql:ConnectionString" ""
cd samples/Tracon.Api && dotnet run
```

**Beklenen sonuç**
> **Düzeltildi (2026-08-15, KAPANIS-PLANI §8) — İzlek B ile bu senaryo yapısal
> olarak erişilemez, beklenti koda göre düzeltildi:**
> `samples/Tracon.Api/Program.cs:639` boş bağlantı dizesinde
> `UsePostgreSql()`'i hiç çağırmaz — validator'a hiçbir zaman ulaşılmaz.
> Örnek uygulama sessizce InMemory'e düşer, `/health` **200 Degraded** döner
> (kalıcılık nedeniyle değil, model sağlayıcı sağlığı nedeniyle). Bu, İzlek B
> için **doğru** ve kasıtlı davranıştır (`Program.cs:622` yorumu).
> `TraconPostgreSqlOptionsValidator`'ın kendisi doğru çalışır — bu yalnız
> İzlek A/C (doğrudan `UsePostgreSql()` çağıran bir harness) üzerinden
> gözlemlenebilir: `UsePostgreSql(string)` çağrı anında `ArgumentException`,
> `UsePostgreSql(IConfiguration)` ilk `IOptions.Value` erişiminde
> `TraconPostgreSqlOptions.ConnectionString bos olamaz` mesajıyla
> `OptionsValidationException` fırlatır. Bu case'in adımları İzlek A/C'ye
> taşınmalıdır; İzlek B için ayrı bir case ("boş bağlantı dizesiyle örnek
> uygulama bellek içi depoya sessizce düşer, `/health` 200 Degraded döner")
> eklenmelidir.

~~Eski beklenti (yanlış öncül — İzlek B'de validator'a hiç ulaşılmadığını
gözden kaçırıyordu): Uygulama başlamayı reddeder (`ValidateOnStart`);
konsolda `OptionsValidationException` görünür ve mesaj
`TraconPostgreSqlOptions.ConnectionString bos olamaz` metnini taşır.
Süreç sıfırdan farklı bir çıkış koduyla sonlanır; `/health` hiçbir zaman
yanıt vermez.~~

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
dotnet user-secrets set "Tracon:PostgreSql:SchemaName" "public"
cd samples/Tracon.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Hata mesajı `cannot be 'public'` ve `decision K-013` ifadelerini taşır.
  (Metin İngilizce'dir — K-228 dil sınırı: pakete giren her şey İngilizce.)
- Tüketicinin `public` şeması **hiçbir şekilde** değiştirilmez.

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';
-- Beklenen: denemeden ONCEKI ile AYNI sayi (Tracon public'e hicbir tablo yazmadi).
```

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
dotnet user-secrets set "Tracon:PostgreSql:SchemaName" "Tracon"
cd samples/Tracon.Api && dotnet run
# Ctrl+C ile durdur

# 2) Bosluk
dotnet user-secrets set "Tracon:PostgreSql:SchemaName" "agent prism"
dotnet run
# Ctrl+C ile durdur

# 3) Enjeksiyon denemesi
dotnet user-secrets set "Tracon:PostgreSql:SchemaName" "tracon; DROP SCHEMA public CASCADE;--"
dotnet run
# Ctrl+C ile durdur
```

**Beklenen sonuç**
- Üçü de uygulamanın başlamasını reddeder; hata mesajı aynı biçimdedir
  (`is not a valid unquoted PostgreSQL identifier`) ve reddedilen değeri
  `Actual value: '...'` olarak yazar. (Metin İngilizce'dir — K-228.)
- 3. denemede `DROP SCHEMA` **hiçbir zaman çalıştırılmaz** — doğrulama, adın SQL
  metnine yerleştirilmesinden ÖNCE gerçekleşir.

**Doğrulama sorgusu**
```sql
SELECT schema_name FROM information_schema.schemata WHERE schema_name = 'public';
-- Beklenen: bir satir doner (public hala var).
```

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
dotnet user-secrets set "Tracon:PostgreSql:CommandTimeoutSeconds" "-1"
dotnet run   # reddedilir, Ctrl+C

dotnet user-secrets set "Tracon:PostgreSql:CommandTimeoutSeconds" "3601"
dotnet run   # reddedilir, Ctrl+C

dotnet user-secrets set "Tracon:PostgreSql:CommandTimeoutSeconds" "0"
dotnet run   # baslar, Ctrl+C

dotnet user-secrets set "Tracon:PostgreSql:CommandTimeoutSeconds" "3600"
dotnet run   # baslar, Ctrl+C
```

**Beklenen sonuç**
- 1. ve 2. denemede hata mesajı `must be between 0 and 3600` ifadesini ve
  gönderilen değeri (`Actual value: ...`) taşır. (Metin İngilizce'dir — K-228.)
- 3. ve 4. deneme başarıyla başlar.

---

### MT-PG-005 — Üç `UsePostgreSql` aşırı yüklemesi aynı sonucu üretir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-021 |

`UsePostgreSql(string)`, `UsePostgreSql(IConfiguration)` ve
`UsePostgreSql(Action<Options>)` üç ayrı yoldan aynı `TraconPostgreSqlOptions`'a
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
rm -rf ~/tracon-manuel/pg-ayarlar && mkdir -p ~/tracon-manuel/pg-ayarlar
cd ~/tracon-manuel/pg-ayarlar
dotnet new console -o . --force
cp ~/tracon-manuel/uretec/nuget.config .
SURUM=$(ls ~/tracon-local-feed/Tracon.PostgreSql.*.nupkg | sed 's#.*Tracon.PostgreSql\.##;s#\.nupkg##')
dotnet add package Tracon.PostgreSql --version "$SURUM"
# ConfigurationBuilder icin GEREKLI: Tracon yalnizca IConfiguration
# abstraction'ini getirir, implementation paketini KASITLI olarak getirmez.
dotnet add package Microsoft.Extensions.Configuration

cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

void Yazdir(string etiket, IServiceCollection services)
{
    var options = services.BuildServiceProvider()
        .GetRequiredService<IOptions<TraconPostgreSqlOptions>>().Value;
    Console.WriteLine($"{etiket}: ConnectionString='{options.ConnectionString}' SchemaName='{options.SchemaName}'");
}

var conn = "Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon";

var s1 = new ServiceCollection();
s1.AddTracon().UsePostgreSql(conn);
Yazdir("string", s1);

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Tracon:PostgreSql:ConnectionString"] = conn,
        ["Tracon:PostgreSql:SchemaName"] = "tracon",
    })
    .Build();
var s2 = new ServiceCollection();
s2.AddTracon().UsePostgreSql(config.GetSection(TraconPostgreSqlOptions.SectionName));
Yazdir("IConfiguration", s2);

var s3 = new ServiceCollection();
s3.AddTracon().UsePostgreSql(o =>
{
    o.ConnectionString = conn;
    o.SchemaName = "tracon";
});
Yazdir("Action<Options>", s3);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Üç satır da AYNI `ConnectionString` ve `SchemaName` değerlerini gösterir.

---

### MT-PG-006 — Bağlantı dizesi hiç verilmezse hangi denetim önce tetiklenir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Sınır senaryosu / şüphe kaydı. Kod tabanında bağlantı dizesi boşluğunu kontrol
eden **iki** ayrı yer vardır: `TraconPostgreSqlOptionsValidator` (her
`IOptions<T>.Value` erişiminde tetiklenir) ve `NpgsqlDataSourceFactory.Create`
(kendi içinde ayrı bir `TraconException` fırlatır). Normal DI akışında
`NpgsqlDataSource` fabrikası `IOptions<T>.Value`'yu **kendisi** okuduğu için
Validator'ın önce tetiklenmesi beklenir; bu case hangisinin GERÇEKTEN göründüğünü
kaydeder.

**Ön koşul**
- [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md) `MT-PKG-070` geçti.

**Adımlar**
1. Bağlantı dizesi verilmeden `UsePostgreSql` çağır.
2. `IOptions<TraconPostgreSqlOptions>.Value`'yu ve bir depoyu çözümlemeyi dene.

> 🚨 `NpgsqlDataSource`'u çözümleme — Faz 110'dan beri public bir DI servisi
> **değildir** ve `InvalidOperationException: No service for type` verir; bu
> case'in sorusunu hiç sormadan düşersin.

**Girilecek veri**
```bash
rm -rf ~/tracon-manuel/pg-baglantisiz && mkdir -p ~/tracon-manuel/pg-baglantisiz
cd ~/tracon-manuel/pg-baglantisiz
dotnet new console -o . --force
cp ~/tracon-manuel/uretec/nuget.config .
SURUM=$(ls ~/tracon-local-feed/Tracon.PostgreSql.*.nupkg | sed 's#.*Tracon.PostgreSql\.##;s#\.nupkg##')
dotnet add package Tracon.PostgreSql --version "$SURUM"

cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var services = new ServiceCollection();
services.AddTracon().UsePostgreSql(_ => { });
var provider = services.BuildServiceProvider();

void Dene(string etiket, Action f)
{
    try { f(); Console.WriteLine($"{etiket}: 🚨 istisna ATILMADI"); }
    catch (Exception ex) { Console.WriteLine($"{etiket}: {ex.GetType().FullName}\n  mesaj: {ex.Message}"); }
}

Dene("IOptions<TraconPostgreSqlOptions>.Value",
    () => _ = provider.GetRequiredService<IOptions<TraconPostgreSqlOptions>>().Value);
Dene("IAgentDefinitionStore (depo, gercek tuketici yolu)",
    () => _ = provider.GetRequiredService<IAgentDefinitionStore>());
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Bir istisna fırlar; `🚨 istisna ATILMADI` görünmez.
- İstisnanın tipi kaydedilir (`Microsoft.Extensions.Options.OptionsValidationException`
  mi yoksa `Tracon.TraconException` mi). `TraconPostgreSqlOptionsValidator`'ın
  DI akışında Factory'nin kendi kontrolünden ÖNCE tetiklenip tetiklenmediği burada netleşir;
  ikisi de kullanıcı için anlaşılır bir mesaj taşımalıdır.

---

### MT-PG-007 — Yanlış host ile başlatma migration adımında çöker (fail-fast)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Negatif senaryo. `MigrationHostedService`'in kod yorumu açıktır: "Hata uygulamayi
baslatmaz" — şema hazır değilken sessizce çalışan bir Tracon veri kaybeder;
bu yüzden migration hatası yutulmaz, süreç kapanır.

**Ön koşul**
- Hiçbir şey dinlemeyen bir port biliniyor (örnek: `1`).

**Adımlar**
1. Bağlantı dizesini geçersiz bir porta ayarla.
2. Uygulamayı başlat ve bekle.
3. `/health`'e erişmeyi dene.

**Girilecek veri**
```bash
dotnet user-secrets set "Tracon:PostgreSql:ConnectionString" \
  "Host=localhost;Port=1;Database=tracon;Username=postgres;Password=tracon;Timeout=5"
cd samples/Tracon.Api && dotnet run
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

---

### MT-PG-008 — `secret` hiçbir zaman veritabanına veya dosyaya yazılmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-059 |

Negatif senaryo. `TraconPostgreSqlOptions.ConnectionString`'in XML dokümanı
"bir sırdır ve dosyaya yazılmaz" der; bu case iddiayı doğrular.

**Ön koşul**
- Uygulama normal çalışıyor, en az birkaç agent kaydedilmiş (önceki case'lerden).

**Adımlar**
1. `appsettings*.json` dosyalarını bağlantı dizesi için tara.
2. `agent_definitions` tablosunu bağlantı dizesi alt dizgisi için tara.

**Girilecek veri**
```bash
grep -rn "Password=tracon\|Host=localhost;Port=55432" samples/Tracon.Api/appsettings*.json
```
```sql
SELECT count(*) FROM tracon.agent_definitions WHERE definition::text ILIKE '%Password=%';
SELECT count(*) FROM tracon.audit_log WHERE before::text ILIKE '%Password=%' OR after::text ILIKE '%Password=%';
```

**Beklenen sonuç**
- `grep` sıfır satır döner — `appsettings*.json` boş yer tutucu taşır, gerçek
  değer yalnız `dotnet user-secrets` içindedir.
- Her iki SQL sorgusu da **0** döner.

### MT-PG-020 — Boş DB'de çekirdek + knowledge seti sırayla uygulanır

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
cd samples/Tracon.Api && dotnet run
```
```sql
SELECT count(*) FROM tracon.__migrations;
SELECT set_name, count(*) FROM tracon.__migrations GROUP BY set_name ORDER BY set_name;
SELECT set_name, id, name FROM tracon.__migrations ORDER BY set_name, id;
```

**Beklenen sonuç**
- Açılış logu `Tracon applied 51 migration(s). Schema: tracon.` satırını taşır
  (örnek uygulama `EnableKnowledge: true` taşır, Faz 67 — bkz. MT-PG-062 knowledge
  KAPALIYKEN davranışı ayrıca sınar). Metin İngilizce'dir — K-228.
- `count(*)` **51** döner; `set_name` grubu **50** (`core`) ve **1** (`knowledge`) döner.
- `core` seti `id 1`'den `id 51`'e sıralıdır ama **id 24 boştur** (0024_vector
  çekirdekten `knowledge` setine taşındı, Faz 67, K-475 — sayı geri
  dönüştürülmez); son satır `0051_run_score_evaluator_version`'dır, toplam 50 satır.

> 🚨 **Bu sayılar migration eklendikçe artar.** Sabit sayıyı değil, yapıyı
> doğrula: `core` = `Migrations/*.sql` dosya sayısı, `knowledge` =
> `MigrationsKnowledge/*.sql` dosya sayısı, id 24 boş, birincil anahtar
> `(set_name, id)`. Sayılar 2026-09-16'da ölçülmüştür.
  `knowledge` seti tek başına `id 1`, `0001_vector`'dir — `core`'un `id 1`'i
  (`0001_initial`) ile **çakışmaz**: birincil anahtar `(set_name, id)`'dir.

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
cd samples/Tracon.Api && dotnet run
```

**Beklenen sonuç**
- `"... migration uyguladi."` satırı **görünmez** (uygulanan migration sayısı
  0'dır; kod bu durumda log basmaz).
- `__migrations` hâlâ MT-PG-020'nin ölçtüğü satır sayısını taşır (2026-09-16: **51** = 50 `core` + 1 `knowledge`).
- Uygulama normal başlar, hiçbir hata görünmez.

---

### MT-PG-022 — Var olan (kısmi) şema üzerine devam

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | — |

Sınır senaryosu. İlk beş migration ELLE uygulanır ve `__migrations` defterine
(Faz 67 ÖNCESİ şekliyle — `id` tek başına birincil anahtar, `set_name` sütunu
YOK) doğru checksum'la kaydedilir — hem bir önceki Tracon sürümünün yarım
bıraktığı bir dağıtımı, hem de Faz 67 öncesi bir veritabanının şema
yükseltmesini (K-475) aynı anda simüler. Uygulama geri kalan çekirdek
migration'ları VE knowledge setini uygulamalı, `set_name` sütununu geriye dönük
eklemeli, birincil anahtarı `(set_name, id)`'ye genişletmelidir.

**Ön koşul**
- Reset yordamı uygulanmış, uygulama HENÜZ başlatılmamış.

**Adımlar**
1. İlk beş migration dosyasını elle uygula.
2. Checksum'larını hesaplayıp `__migrations`'a elle yaz.
3. Uygulamayı başlat.
4. Açılış logunu ve `__migrations` tablosunu oku.

**Girilecek veri**
```bash
$PG -c "CREATE SCHEMA IF NOT EXISTS tracon;"

for f in src/Tracon.PostgreSql/Migrations/0001_initial.sql \
         src/Tracon.PostgreSql/Migrations/0002_observability.sql \
         src/Tracon.PostgreSql/Migrations/0003_agent_skills.sql \
         src/Tracon.PostgreSql/Migrations/0004_skill_scripts.sql \
         src/Tracon.PostgreSql/Migrations/0005_agent_call_graph.sql; do
  sed 's/{schema}/tracon/g' "$f" | $PG
done

$PG -c "CREATE TABLE IF NOT EXISTS tracon.__migrations (
  id integer NOT NULL PRIMARY KEY, name text NOT NULL,
  checksum text NOT NULL, applied_at timestamptz NOT NULL);"

for f in src/Tracon.PostgreSql/Migrations/0001_initial.sql \
         src/Tracon.PostgreSql/Migrations/0002_observability.sql \
         src/Tracon.PostgreSql/Migrations/0003_agent_skills.sql \
         src/Tracon.PostgreSql/Migrations/0004_skill_scripts.sql \
         src/Tracon.PostgreSql/Migrations/0005_agent_call_graph.sql; do
  id=$(basename "$f" | cut -c1-4 | sed 's/^0*//')
  name=$(basename "$f" .sql)
  checksum=$(python3 -c "
import hashlib
data = open('$f', 'rb').read().replace(b'\r\n', b'\n')
print(hashlib.sha256(data).hexdigest().upper())
")
  $PG -c "INSERT INTO tracon.__migrations (id, name, checksum, applied_at) VALUES ($id, '$name', '$checksum', now());"
done

cd samples/Tracon.Api && dotnet run
```
```sql
SELECT set_name, id, name, applied_at FROM tracon.__migrations ORDER BY set_name, id LIMIT 8;
```

**Beklenen sonuç**
- Açılış logu `Tracon applied 46 migration(s).` yazar (50 çekirdek − 5 elle
  uygulanmış + 1 knowledge). Sayı migration eklendikçe artar; formülü doğrula.
- Hiçbir checksum uyuşmazlığı hatası oluşmaz.
- `__migrations`'ta **51** satır vardır (50 `core` + 1 `knowledge`); id 1–5'in
  `set_name` değeri `core`'a **geriye dönük dolmuştur** ve `applied_at` elle
  yazılan zamandır, kalan çekirdek satırların `applied_at`'i uygulamanın az
  önceki açılış zamanıdır.

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
- MT-PG-020 veya MT-PG-021 geçti (33 migration uygulanmış, uygulama DURDURULMUŞ).

**Adımlar**
1. `__migrations` tablosunda `core` setinin `id=1`'inin (`0001_initial`)
   checksum'ını boz.
2. Uygulamayı başlat.
3. Doğru checksum'ı hesapla (temizlik için).

**Girilecek veri**
```bash
$PG -c "UPDATE tracon.__migrations SET checksum = 'BOZUK0000000000000000000000000000000000000000000000000000000' WHERE set_name = 'core' AND id = 1;"
cd samples/Tracon.Api && dotnet run
```
```bash
python3 -c "
import hashlib
data = open('src/Tracon.PostgreSql/Migrations/0001_initial.sql','rb').read().replace(b'\r\n', b'\n')
print(hashlib.sha256(data).hexdigest().upper())
"
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Konsolda `TraconException` görünür; mesaj `Migration '0001_initial' has been
  applied to the database but the file's content has changed` ifadesini taşır.
  (Metin İngilizce'dir — K-228.)
- Mesaj hem veritabanındaki (bozuk) hem dosyadaki (doğru) checksum'ı gösterir.

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
cd samples/Tracon.Api && dotnet run

# Terminal 2 (mumkun oldugunca ayni anda)
cd samples/Tracon.Api && dotnet run --urls http://localhost:5090
```
```sql
SELECT count(*) FROM tracon.__migrations;
```

**Beklenen sonuç**
- Yalnız BİR terminalin logu `Tracon applied 51 migration(s).` yazar; diğeri
  0 migration uygular (log satırı görünmez) çünkü kilidi aldığında migration'lar
  zaten bitmiştir. Hangisinin kazandığı belirsizdir — ikisi de olabilir.
- Hiçbir terminalde checksum hatası veya çökme olmaz.
- `count(*)` tam olarak **51** döner (102 değil — birincil anahtar çakışması yoktur).

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
dotnet user-secrets set "Tracon:PostgreSql:AutoApplyMigrations" "false"
cd samples/Tracon.Api && dotnet run
```
```bash
curl -s -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"

# 🚨 `echo` KULLANMA: gercek bir saglayici anahtari kayitliyken EchoModelProvider
# kaydedilmez ve istek 400'de durur, veritabani yoluna hic ulasmaz.
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"manuel-migrationsiz","model":{"provider":"openai","model":"gpt-4o-mini"}}' \
  -w "\nHTTP: %{http_code}\n"
```

**Beklenen sonuç**
- Uygulama başlar (çökmez); log `migrations are not applied automatically`
  satırını taşır. (Metin İngilizce'dir — K-228.)
- `/health` **Unhealthy** döner (bekleyen migration var).
- Agent kaydı isteği **`503`** + `application/problem+json` döner; uygulama
  çökmez ve sonraki istekler de aynı yanıtı verir. `title` alanı
  `Database schema is not current`, `detail` alanı bekleyen migration **sayısını**
  ve iki çıkış yolunu (`tracon migrate` · `AutoApplyMigrations`) söyler, ve
  yeniden denemenin yardım etmeyeceğini yazar (kalıcı durum).
- 🚨 Yanıt **şema adını ve SQL metnini taşımaz** — onlar yalnız günlüğe gider.
  Bu bir güvenlik sınırıdır (`HATA-S1-015` kapanışı, K-813).

---

### MT-PG-026 — Şema adı değiştirildiğinde bağımsız bir migration seti oluşur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 2 |
| **İlgili karar** | K-013 |

**Ön koşul**
- MT-PG-020 geçti (`tracon` şeması tam migration setiyle kurulu).

**Adımlar**
1. Şema adını `tracon_ikinci` olarak ayarla.
2. Uygulamayı başlat.
3. Her iki şemayı da sorgula.

**Girilecek veri**
```bash
dotnet user-secrets set "Tracon:PostgreSql:SchemaName" "tracon_ikinci"
cd samples/Tracon.Api && dotnet run
```
```sql
SELECT schema_name FROM information_schema.schemata WHERE schema_name IN ('tracon', 'tracon_ikinci');
SELECT count(*) FROM tracon.__migrations;
SELECT count(*) FROM tracon_ikinci.__migrations;
```

**Beklenen sonuç**
- Açılış logu yeni şema için `Tracon applied 51 migration(s). Schema:
  tracon_ikinci.` yazar. (Metin İngilizce'dir — K-228; sayı MT-PG-020'ninkiyle aynıdır.)
- Her iki şema da mevcuttur; her ikisinin de `__migrations`'ı **aynı** satır
  sayısını taşır — birbirinden BAĞIMSIZDIR.
- Orijinal `tracon` şemasındaki veriler (varsa) dokunulmadan kalır.

---

### MT-PG-027 — `Dimensions` değişikliği uygulanmış `vector` sütununun boyutunu DEĞİŞTİRMEZ

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

Sınır senaryosu. Knowledge setinin `0001_vector.sql`'indeki `{dimension}` yer
tutucusu bir sema yer tutucusu GİBİ davranmaz: checksum ham (değiştirilmemiş)
metin üzerinden hesaplanır, bu yüzden `Dimensions` ayarını değiştirip yeniden
başlatmak checksum hatası VERMEZ — ama var olan sütunun boyutunu da
değiştirmez. Kod bunu 🚨 ile işaretler; bu case operasyonel tuzağı doğrular.

**Ön koşul**
- MT-PG-020 geçti (knowledge setinin `0001_vector` migration'ı 1536 boyutla uygulanmış).

**Adımlar**
1. Bilgi tabanı boyutunu `3`'e ayarla.
2. Uygulamayı başlat.
3. Sütun boyutunu sorgula.

**Girilecek veri**
```bash
dotnet user-secrets set "Tracon:Knowledge:Dimensions" "3"
cd samples/Tracon.Api && dotnet run
```
```sql
SELECT atttypmod FROM pg_attribute
WHERE attrelid = 'tracon.document_embeddings'::regclass AND attname = 'embedding';
```

**Beklenen sonuç**
- Uygulama normal başlar, checksum uyuşmazlığı hatası **OLUŞMAZ**.
- `atttypmod` hâlâ **1536**'dır (pgvector bu sütunda boyutu doğrudan taşır) —
  `3` DEĞİLDİR. Ayar sessizce hiçbir şey yapmamıştır.

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
2. Agent'ı çalıştır (tool çağırmayan bir istekle).

> 🚨 Gerçek bir sağlayıcı anahtarı kayıtlıyken `echo` sağlayıcısı kayıtlı
> **değildir** ve istek 400'de durur. Model adını örnek uygulamanın
> varsayılanından al (`Tracon:Providers:OpenAI:DefaultModel`, bugün
> `gpt-5.4-mini`) — rastgele bir OpenAI modeli projenin erişimi dışında olabilir.
3. `audit_log` tablosunu tara.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-denetim", "instructions": "Kisa yanit ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'

curl -s -X POST "$APU/api/agents/manuel-denetim/run" -H "$APB" \
  -H "content-type: application/json" -d '{"message":"Merhaba"}' > /dev/null
```
```sql
SELECT entity, action FROM tracon.audit_log ORDER BY created_at DESC LIMIT 5;
SELECT count(*) FROM tracon.audit_log WHERE entity ILIKE '%run%' OR entity ILIKE '%tool_invocation%';
```

**Beklenen sonuç**
- `agent_definitions` (veya eşdeğer) varlığı için EN AZ bir audit kaydı vardır.
- İkinci sorgu **0** döner — `runs`/`tool_invocations` için audit_log'da hiçbir
  satır yoktur; `IRunStore` denetim izi dekoratörüyle SARILMAMIŞTIR.

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
rm -rf ~/tracon-manuel/tryadd && mkdir -p ~/tracon-manuel/tryadd
cd ~/tracon-manuel/tryadd
dotnet new console -o . --force
cp ~/tracon-manuel/uretec/nuget.config .
SURUM=$(ls ~/tracon-local-feed/Tracon.PostgreSql.*.nupkg | sed 's#.*Tracon.PostgreSql\.##;s#\.nupkg##')
dotnet add package Tracon.PostgreSql --version "$SURUM"

cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var services = new ServiceCollection();
// 🚨 UsePostgreSql ITraconBuilder uzerindedir, IServiceCollection uzerinde DEGIL.
var tracon = services.AddTracon();

// Tuketici KENDI uygulamasini UsePostgreSql()'DEN ONCE kaydeder.
services.TryAddSingleton<IConversationBranchStore, SahteDalStore>();

tracon.UsePostgreSql("Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon");

var resolved = services.BuildServiceProvider().GetRequiredService<IConversationBranchStore>();
Console.WriteLine("Cozumlenen tip: " + resolved.GetType().FullName);

internal sealed class SahteDalStore : IConversationBranchStore
{
    public ValueTask<ConversationBranch?> BranchAsync(string tenantId, Guid parentConversationId, long? upToSequence, CancellationToken cancellationToken = default)
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
using Tracon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var services = new ServiceCollection();
var tracon = services.AddTracon();
services.TryAddSingleton<IVectorSearchStore, SahteVektorStore>();
tracon.UsePostgreSql("Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon");

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
rm -rf ~/tracon-manuel/replace && mkdir -p ~/tracon-manuel/replace
cd ~/tracon-manuel/replace
dotnet new console -o . --force
cp ~/tracon-manuel/uretec/nuget.config .
SURUM=$(ls ~/tracon-local-feed/Tracon.PostgreSql.*.nupkg | sed 's#.*Tracon.PostgreSql\.##;s#\.nupkg##')
dotnet add package Tracon.PostgreSql --version "$SURUM"

cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var services = new ServiceCollection();
var tracon = services.AddTracon();

// Tuketici IRunStore'u ONCE, TryAdd ile kaydeder. FABRIKA kaydi kullaniliyor:
// IRunStore'un 14 uyesini elle yazmak bu case'in olctugu onceligi degistirmez.
services.TryAddSingleton<IRunStore>(_ => throw new NotImplementedException("TUKETICININ kaydi cozuldu"));

tracon.UsePostgreSql("Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon");

try
{
    var resolved = services.BuildServiceProvider().GetRequiredService<IRunStore>();
    Console.WriteLine("Cozumlenen tip: " + resolved.GetType().FullName);
}
catch (NotImplementedException ex)
{
    Console.WriteLine("🚨 TUKETICININ kaydi kazandi: " + ex.Message);
}
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- Çıktı `SqlRunStore` tipini gösterir (`Tracon` ad alanında) —
  `SahteRunStore` **DEĞİLDİR**. `Replace` deseni `TryAdd`'in aksine önceki kaydı
  bilerek ezer; MT-PG-031/032'deki davranışla TAM TERSİDİR.

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
1. `samples/Tracon.Api/Program.cs`'de `tracon.UsePostgreSql(postgreSql);`
   satırından (yaklaşık 641. satır) hemen SONRA şu satırı geçici olarak ekle:
   `tracon.UseSqlite(o => o.ConnectionString = "Data Source=manuel-test-ikinci.db");`
2. Uygulamayı başlat, açılış logunu oku.
3. `/api/diagnostics` ve `/health` ucunu çağır.
4. Değişikliği geri al.

**Girilecek veri**
```bash
cd samples/Tracon.Api && dotnet run
```
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | python3 -m json.tool | grep -E "persistenceProvider|registeredPersistenceProviders"
curl -s -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"
```
```bash
# Temizlik (adim 4):
git checkout -- samples/Tracon.Api/Program.cs
rm -f samples/Tracon.Api/manuel-test-ikinci.db
```

**Beklenen sonuç**
- Açılış logu `Tracon'de birden fazla kalicilik saglayicisi kayitli:
  PostgreSQL, SQLite.` (ya da çağrı sırasına göre ters) uyarısını taşır.
- `/api/diagnostics`: `registeredPersistenceProviders` = **2**;
  `persistenceProvider` = **son çağrılan** sağlayıcı (`SQLite` — `UseSqlite`
  `UsePostgreSql`'den SONRA eklendiği için `Replace` deseni gereği o kazanır).
- `/health` **Degraded** döner (`registeredPersistenceProviders: 2` verisiyle).

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
  kez `UseSqlite(...)` satırı `tracon.UsePostgreSql(postgreSql);`'DEN ÖNCE
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
git checkout -- samples/Tracon.Api/Program.cs
rm -f samples/Tracon.Api/manuel-test-ikinci.db
```

**Beklenen sonuç**
- `persistenceProvider` artık **PostgreSQL**'dir (son çağrı yine kazanır, bu kez
  PostgreSQL sondadır). `registeredPersistenceProviders` yine **2**'dir.

### MT-PG-040 — `pgvector` eklentisi ve HNSW indeksi migration sonrası kuruludur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 51 |
| **İlgili karar** | — |

**Ön koşul**
- MT-PG-020 geçti (knowledge setinin `0001_vector.sql`'i uygulanmış).

**Adımlar**
1. Eklenti kurulumunu doğrula.
2. İndeksleri listele.

**Girilecek veri**
```sql
SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';
SELECT indexname FROM pg_indexes WHERE schemaname = 'tracon' AND tablename = 'document_embeddings' ORDER BY indexname;
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
rm -rf ~/tracon-manuel/vektor && mkdir -p ~/tracon-manuel/vektor
cd ~/tracon-manuel/vektor
dotnet new console -o . --force
cp ~/tracon-manuel/uretec/nuget.config .
SURUM=$(ls ~/tracon-local-feed/Tracon.PostgreSql.*.nupkg | sed 's#.*Tracon.PostgreSql\.##;s#\.nupkg##')
dotnet add package Tracon.PostgreSql --version "$SURUM"

cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
// 🚨 EnableKnowledge KAPALIYKEN IVectorSearchStore fabrikasi null doner ve
// GetRequiredService "No service for type ... has been registered" atar.
services.AddTracon().UsePostgreSql(o =>
{
    o.ConnectionString = "Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon";
    o.EnableKnowledge = true;
});

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
using Tracon;
using Microsoft.Extensions.DependencyInjection;

float[] Axis(int index) { var v = new float[1536]; v[index] = 1f; return v; }

var services = new ServiceCollection();
services.AddTracon().UsePostgreSql(o =>
{
    o.ConnectionString = "Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon";
    o.EnableKnowledge = true;   // 🚨 bkz. MT-PG-041
});
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
SELECT source_id, chunk_index, content FROM tracon.document_embeddings
WHERE tenant_id = 'kiraci-alfa' AND collection = 'manuel-koleksiyon';
```

**Beklenen sonuç**
- `arama sonuc sayisi (ikinci yazimdan sonra): 1` — ikinci yazımdan sonra tek
  parça vardır; eski iki parça SİLİNMİŞTİR.
- `content` alanı `ikinci surum, tek parca` gösterir; `ilk surum` metni HİÇBİR
  YERDE görünmez.

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
using Tracon;
using Microsoft.Extensions.DependencyInjection;

float[] Axis(int index) { var v = new float[1536]; v[index] = 1f; return v; }

var services = new ServiceCollection();
services.AddTracon().UsePostgreSql(o =>
{
    o.ConnectionString = "Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon";
    o.EnableKnowledge = true;   // 🚨 bkz. MT-PG-041
});
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
using Tracon;
using Microsoft.Extensions.DependencyInjection;

float[] Axis(int index) { var v = new float[1536]; v[index] = 1f; return v; }

var services = new ServiceCollection();
services.AddTracon().UsePostgreSql(o =>
{
    o.ConnectionString = "Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon";
    o.EnableKnowledge = true;   // 🚨 bkz. MT-PG-041
});
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
using Tracon;
using Microsoft.Extensions.DependencyInjection;

float[] Axis(int index) { var v = new float[1536]; v[index] = 1f; return v; }

var services = new ServiceCollection();
services.AddTracon().UsePostgreSql(o =>
{
    o.ConnectionString = "Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon";
    o.EnableKnowledge = true;   // 🚨 bkz. MT-PG-041
});
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
using Tracon;
using Microsoft.Extensions.DependencyInjection;

float[] Axis(int index) { var v = new float[1536]; v[index] = 1f; return v; }

var services = new ServiceCollection();
services.AddTracon().UsePostgreSql(o =>
{
    o.ConnectionString = "Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon";
    o.EnableKnowledge = true;   // 🚨 bkz. MT-PG-041
});
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
- Örnek uygulama OpenAI anahtarıyla çalışıyor (`knowledge-assistant` agent'ı ve
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
- 2. istek de **400** döner ama FARKLI bir mesajla (`embedding length`
  ifadesini taşır — embedding uzunluğu 1, depo boyutu 1536) — bu, iki
  doğrulamanın BAĞIMSIZ çalıştığını kanıtlar. (Metin İngilizce'dir — K-228.)

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
- Veritabanı erişilemezken `/health` **503** döner ve gövde `Unhealthy` yazar.
  `canConnect` ilk kontroldür ve model sağlayıcı mantığından **önce** kısa devre
  yapar (`TraconHealthCheck.cs:46-49`), bu yüzden bu yarı sağlayıcı kurulumundan
  bağımsızdır.
- `/tracon/api/diagnostics` aynı anda `canConnect: false` taşır. Mesajın
  (`The persistence database is unreachable.`) kendisi `/health` gövdesine
  **girmez**: örnek uygulama `MapHealthChecks`'i ResponseWriter'sız çağırır
  (`Program.cs:913`) ve varsayılan yazıcı yalnız durum metnini yazar.
- Veritabanı geri geldikten sonra, **uygulama yeniden başlatılmadan**,
  `canConnect` yeniden `true` olur — Npgsql havuzu kendiliğinden toparlanır.
- Toparlanma sonrası **genel** durum sağlayıcı sağlık önbelleğine bağlıdır:
  önbellek ısıtılmamışsa `Degraded`, ısıtılmışsa `Healthy` (bkz. MT-PG-053).
  Bu case'in ölçtüğü şey genel etiket değil, **`canConnect`'in `true`'ya
  dönmesidir**; kalıcılık kapsamı budur.

> **🚨 Şerit kuralı — container durdurulmaz.** "Girilecek veri" bloğundaki
> `docker stop/start ap-pg` adımları paylaşılan kaynağa dokunur ve koşulmaz
> (`manuel-test-kosumu` §1.3). Erişilemezlik **şerit-yerel bir TCP
> yönlendiriciyle** taklit edilir: uygulama `Port=554<şerit+80>` ile açılır,
> yönlendirici o portu `55432`'ye aktarır, yönlendirici öldürülünce veritabanı
> uygulamanın gözünde tam olarak `docker stop` kadar erişilemez olur. Dört
> şerit paralel koşarken de güvenlidir.

> **Düzeltildi (2026-09-16):** 2026-08-15 düzeltmesi beklentinin **tamamını**
> üstü çizili bırakmış ve case'i bu dosyanın kapsamı dışına atmıştı. Ölçüm
> bunun fazla kapsadığını gösterdi — `echo` gerekçesi yalnız **kurtarma
> yarısını** etkiler; case'in başlığı olan "erişilemezken Unhealthy" iddiası
> `CanConnect` kısa devresi sayesinde sağlayıcıdan bağımsızdır ve doğrulandı.
> Ayrıca 2026-09-16 şeridi `echo`-only **değildi** (üç gerçek anahtar kayıtlı)
> ve yine `Healthy` gelmedi; gerçek neden sağlayıcıların `Unknown` durmasıdır.

---

### MT-PG-051 — `/tracon/api/diagnostics` bekleyen migration'ları listeler, hiçbir `secret` taşımaz

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
$PG -c "DROP SCHEMA IF EXISTS tracon CASCADE;"
dotnet user-secrets set "Tracon:PostgreSql:AutoApplyMigrations" "false"
cd samples/Tracon.Api && dotnet run
```
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `migrationsUpToDate: false`.
- `pendingMigrations` şemanın **tamamını** listeler: ilk öğe `0001_initial`,
  son öğe `knowledge:0001_vector`. 2026-09-16'da **51** öğedir (50 çekirdek +
  1 knowledge).
  > **🚨 Sabit sayıya güvenme.** Bu sayı her migration eklendiğinde artar
  > (2026-08'de 28, 2026-09'da 51). Doğrulama **yapıya** bakar: liste boş
  > değil, `__migrations` içindeki uygulanmış sayı + bekleyen sayı =
  > kod tarafındaki toplam migration sayısı.
- Gövdenin hiçbir yerinde bağlantı dizesi, parola veya `Password=` alt dizgisi
  geçmez.

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
- MT-PG-051 durumunda (şemanın tamamı bekliyor, `AutoApplyMigrations=false`).

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
SELECT to_regclass('tracon.__migrations');
```

**Beklenen sonuç**
- Üç çağrının üçü de **aynı** sayıyı yazdırır (değişmez). Sayının kendisi
  MT-PG-051'inkiyle aynıdır; sabit bir değere değil **değişmezliğine** bakılır.
- `to_regclass` **NULL** döner — `__migrations` tablosu hâlâ oluşturulmamıştır;
  teşhis ucu şemaya hiçbir şey YAZMAZ. Şemanın kendisi de açılmamış olmalıdır
  (`information_schema.schemata` → 0 satır).

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
  `true`), uygulama normal başlatılmış (şemanın tamamı otomatik uygulanır).
- 🚨 **İzlenen** bir model sağlayıcı kayıtlı: `openai` · `openai-responses` ·
  `openrouter` · `anthropic` · `google`. **`echo` YETMEZ** — izlenen
  `ModelProviders` listesinde yer almaz.
- 🚨 **Sağlayıcı sağlık önbelleği ısıtılmış olmalıdır**: en az bir kez
  `GET /tracon/api/models/health` çağrılmış olmalı. Teşhis koleksiyonu önbelleği
  yalnız **okur**, prob tetiklemez (`TraconDiagnosticsCollector.cs:144`) ve
  sohbet çağrısı bu önbelleğe yazmaz.

**Adımlar**
1. `/health`'i çağır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "http://localhost:5080/health"
```

**Beklenen sonuç**
- Ön koşullar sağlandığında `/health` → **200**, gövde **`Healthy`**.
- Önbellek ısıtılmadan önce aynı kurulum **200 `Degraded`** döner
  ("No model provider has been confirmed healthy yet") ve sağlayıcılar
  `status:"Unknown"` görünür. Bu bir kusur değil, ön koşulun sağlanmamış
  hâlidir — ama kalıcı yüzü `HATA-S1-016`'dır.

> **Düzeltildi (2026-09-16):** 2026-08-15 düzeltmesi "`/health` YAPISAL OLARAK
> asla düz `Healthy` dönemez" diyordu. Ölçüm bunu **çürüttü** — `Healthy`
> alındı (HTTP 200). O düzeltmenin doğru olan tek kısmı `echo` öncülüdür;
> yanlış olan, bunu tüm kurulumlara genellemesidir. Sorun yapısal değil, ön
> koşuldadır: izlenen bir sağlayıcı **ve** ısıtılmış sağlık önbelleği gerekir.
> Ön koşul buna göre düzeltildi.

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
SELECT count(*) FROM tracon.agent_definitions WHERE name LIKE 'manuel-esz-%';
```

**Beklenen sonuç**
- 20 HTTP kodunun tümü 2xx'tir.
- SQL sorgusu **20** döner — hiçbir istek kaybolmaz.
- Uygulama loglarında bağlantı havuzu tükenmesi hatası (`Npgsql...TimeoutException`
  veya benzeri) görünmez.

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

---

## İsteğe bağlı `knowledge` migration seti (Faz 67)

`0024_vector.sql`, `MigrationsKnowledge/0001_vector.sql`'a taşındı ve yalnız
`Tracon:PostgreSql:EnableKnowledge = true` iken uygulanır (K1, varsayılan
kapalı). Karar: K-475/K-476/K-477. Aşağıdaki beş case, faz dokümanının
([`67-ISTEGE-BAGLI-MIGRATION-SETI.md`](../arsiv/fazlar/67-ISTEGE-BAGLI-MIGRATION-SETI.md))
manuel kabul tablosunun karşılığıdır; MT-PG-062/063/064 kapanışta **gerçek**
konteynerlere karşı koşuldu (kanıt aşağıda).

### MT-PG-062 — `pgvector` kurulu olmayan PostgreSQL'de knowledge kapalıyken uygulama sorunsuz açılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 67 |
| **İlgili karar** | K-475, K-476 |

**Ön koşul**
- `pgvector` uzantısı **kurulu olmayan** düz bir `postgres` imajı (`pgvector/pgvector` DEĞİL).
- `EnableKnowledge` verilmez (varsayılan `false`).

**Adımlar**
1. Uygulamayı başlat.
2. Açılış logunu oku.
3. `/api/diagnostics`'i çağır.
4. `\dx` ile kurulu uzantıları listele.

**Girilecek veri**
```bash
docker run -d --name ap-pg-plain -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=tracon -p 55433:5432 postgres:18-alpine
cd samples/Tracon.Api && dotnet run -- \
  --Tracon:PostgreSql:ConnectionString="Host=localhost;Port=55433;Database=tracon;Username=postgres;Password=postgres" \
  --Tracon:PostgreSql:SchemaName=tracon_case1 \
  --Tracon:PostgreSql:EnableKnowledge=false
```
```bash
curl -s http://localhost:5099/tracon/api/diagnostics -H "Authorization: Bearer $TOKEN"
docker exec ap-pg-plain psql -U postgres -d tracon -c "\dx"
```

**Beklenen sonuç**
- Açılış logu çekirdek setin **tamamını** uygular (2026-09-16'da **50**
  migration); hiçbiri düşmez.
  > **🚨 Sabit sayıya güvenme** — `__migrations` içindeki `set_name='core'`
  > satır sayısı kod tarafındaki çekirdek migration sayısına eşit olmalıdır.
  > `set_name='knowledge'` satırı **hiç** olmamalıdır.
- `/api/diagnostics`: `"canConnect": true`, `"migrationsUpToDate": true`,
  `"pendingMigrations": []`.
- `\dx` yalnız `plpgsql` listeler — `vector` **yoktur**.
- `/api/tools` çıktısında `search_knowledge` YOKTUR (K1: kayıt hiç olmaz).
  Listedeki diğer tool'ların sayısı örnek uygulamayla birlikte değişir
  (2026-08'de 7, 2026-09-16'da 10); doğrulama `search_knowledge`'ın
  **yokluğuna** bakar, toplam sayıya değil.

> **⚠️ Bilinen log gürültüsü (2026-09-16, `HATA-S1-017`).** "Hiçbir hata
> yoktur" maddesi bugün **harfiyen** karşılanmıyor: taze bir şemada açılış
> logunun 7. satırı `fail: Tracon.CompositeAgentCatalog` + tam yığın izi
> taşır (`42P01 ... agent_definitions does not exist`). Bu **beklenen** bir
> yoldur — `TraconA2AExtensions.cs:94` uç nokta kurulumu sırasında kataloğu
> listeler, migration'lar henüz koşmamıştır ve istisna kasıtlı olarak yutulur.
> İşlevsel bir kusur değildir; kayıt kapanışta değerlendirilecektir.

**Gerçek koşum kanıtı (2026-08-19, kapanış)**: yukarıdaki adımlar `postgres:18-alpine`
konteynerine karşı BİREBİR çalıştırıldı. Sonuç: `__migrations` 32 satır (hepsi
`set_name='core'`), `\dx` yalnız `plpgsql`, `/api/diagnostics` `canConnect: true`
/ `pendingMigrations: []`, `/api/tools` `search_knowledge`'ı DEĞİL yalnız
`cancel_order`/`get_order_status`/`list_recent_orders`/`list_voices`/
`read_shopping_cart`/`speak`/`transcribe`'ı listeledi.

---

### MT-PG-063 — Aynı ortamda `EnableKnowledge = true` açık ve okunur bir başlangıç hatası verir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 67 |
| **İlgili karar** | K-475 |

Negatif senaryo. `pgvector` sunucuda YÜKLÜ değilken `CREATE EXTENSION vector`
PostgreSQL'in kendi hatasını (`0A000`) verir; `MigrationRunner` bunu
`TraconException`'a sarar.

**Ön koşul**
- MT-PG-062'nin konteyneri (`pgvector` yok).

**Adımlar**
1. `EnableKnowledge = true` ile başlat.
2. Konsol çıktısını oku.

**Girilecek veri**
```bash
cd samples/Tracon.Api && dotnet run -- \
  --Tracon:PostgreSql:ConnectionString="Host=localhost;Port=55433;Database=tracon;Username=postgres;Password=postgres" \
  --Tracon:PostgreSql:SchemaName=tracon_case2 \
  --Tracon:PostgreSql:EnableKnowledge=true
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder (fail-fast, mevcut MT-PG-007 emsali).
- Konsolun İLK satırı **okunur** bir mesaj taşır:
  `Tracon.TraconException: Migration '0001_vector' could not be applied: extension "vector" is not available (SQLSTATE 0A000).`
- Bu istek hiçbir HTTP istemcisine ULAŞMAZ (süreç HTTP dinlemeye başlamadan çöker) —
  "`DbException` yığın izi kullanıcıya gitmez" burada "hiçbir kullanıcı isteği
  hiç işlenmez" anlamına gelir; ayrıntılı .NET yığın izi yalnızca operatörün
  KONSOLUNDA görünür (K-354'ün "hata yutulmaz" ilkesiyle tutarlı).
- Çekirdek migration'ların hiçbiri GERİ ALINMAZ: `__migrations` içinde
  `set_name='core'` setinin **tamamı** uygulanmış ve kalıcı kalır, yalnız
  `0001_vector` düşer. `set_name='knowledge'` hiç satır yazmaz ve
  `document_embeddings` tablosu oluşmaz.

**Gerçek koşum kanıtı (2026-08-19, kapanış)**: aynı konteynere karşı çalıştırıldı.
Gerçek hata: `ERROR: 0A000: extension "vector" is not available` /
`HINT: The extension must first be installed on the system where PostgreSQL is
running.`; uygulamanın fırlattığı üst seviye istisna tam olarak yukarıdaki
metni taşıdı. `tracon_case2.__migrations` sorgulandığında `core` setinin
**32** satırının TAMAMININ başarıyla uygulandığı, yalnız `knowledge` setinin
hiç satır yazmadığı doğrulandı.

---

### MT-PG-064 — `pgvector` kurulu PostgreSQL'de `EnableKnowledge = true` gerçek bir belge yükleme + arama turu tamamlar

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 67 (ayrıca 51) |
| **İlgili karar** | K-476 |

**Ön koşul**
- `ap-pg` (`pgvector/pgvector:pg18`) çalışıyor.
- `EnableKnowledge = true`.
- `Tracon:Providers:OpenAI:ApiKey` `dotnet user-secrets`'ta tanımlı
  (gerçek gömü üretimi için).

**Adımlar**
1. Uygulamayı başlat.
2. Bir belge yükle (`POST /api/knowledge/{collection}/documents`).
3. Aynı koleksiyonda anlamsal arama yap (`POST /api/knowledge/{collection}/search`).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/knowledge/faz67-test/documents" -H "$APB" -H "content-type: application/json" \
  -d '{"sourceId":"doc-1","text":"Tracon Faz 67, PostgreSQL migration setlerini istege bagli hale getirir."}'
curl -s -X POST "$APU/api/knowledge/faz67-test/search" -H "$APB" -H "content-type: application/json" \
  -d '{"query":"migration set nedir","maxResults":3}'
```

**Beklenen sonuç**
- Yükleme `200` ve `{"sourceId":"doc-1","chunkCount":1}` döner.
- `document_embeddings` tablosu şemada VARDIR.
- Arama yüklenen parçayı döndürür (`distance` alanı ile sıralı).

**Gerçek koşum kanıtı (2026-08-19, kapanış)**: gerçek `ap-pg` konteynerine ve
gerçek bir OpenAI gömü çağrısına karşı çalıştırıldı. Yükleme `{"sourceId":"doc-1","chunkCount":1}`
döndü; arama `[{"sourceId":"doc-1","chunkIndex":0,"content":"...","distance":0.693...}]`
ile yüklenen içeriği BİREBİR döndürdü.

---

### MT-PG-065 — Case 062'nin veritabanı sonradan `EnableKnowledge = true` ile yeniden başlatılınca yalnız knowledge seti uygulanır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 67 |
| **İlgili karar** | K-475, K-477 |

**Ön koşul**
- MT-PG-062'nin şeması (çekirdek setin tamamı uygulanmış, `knowledge` seti
  **hiç** uygulanmamış), `pgvector` bu kez KURULU bir sunucuya taşınmış —
  veya aynı ön koşul doğrudan `ap-pg` üzerinde ayrı bir şemada kurulabilir.

**Adımlar**
1. `EnableKnowledge = true` ile yeniden başlat.
2. Açılış logunu oku.

**Beklenen sonuç**
- Yalnız `0001_vector` uygulanır; çekirdek set zaten uygulanmıştı ve YENİDEN
  uygulanmaz. İkinci açılış logunda hiçbir çekirdek DDL'i görünmez.
- `__migrations` içinde `set_name='core'` sayısı **değişmez**,
  `set_name='knowledge'` **1** olur; toplam çekirdek + 1'e çıkar
  (2026-09-16'da 50 + 1 = 51).

Otomatik eşdeğeri (gerçek `ap-pg` konteynerine karşı, kapanışta koşuldu, yeşil):
`OptionalMigrationSetTests.Enabling_knowledge_later_applies_only_the_new_set`.

---

### MT-PG-066 — SQL Server ve SQLite bu fazdan etkilenmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 67 |
| **İlgili karar** | — |

SQL Server ve SQLite hiçbir zaman bir vektör migration'ı taşımadı (bkz. faz
dokümanının kanıt tablosu); bu fazın tek gözlemlenebilir etkisi `__migrations`
defterinin `set_name` sütunu kazanmasıdır — sözleşme testleri davranış
değişikliği olmadan geçmelidir.

**Adımlar**
1. Tam sözleşme test koşumunu çalıştır (`Tracon.SqlServer.IntegrationTests`,
   `Tracon.Sqlite.IntegrationTests`).

**Beklenen sonuç**
- 👤 Fark yok — iki sağlayıcıda da davranış AYNI kalır.

**Gerçek koşum kanıtı (2026-08-19, kapanış)**: `Tracon.SqlServer.IntegrationTests`
540/540, `Tracon.Sqlite.IntegrationTests` 554/554 (ledger yükseltme testi
dahil) — ikisi de gerçek konteynerlere karşı yeşil.

---

### MT-PG-067 — SQL tek kaynak: 117 sorgunun taşınması üretilen SQL'i değiştirmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 94 |
| **İlgili karar** | K-483 |

`SqlQueriesBase` artık üç dialektte özdeş olan 115 sorguyu (plan 117 diyordu;
çözümlenmiş SQL metnine göre ölçüldüğünde 4'ü SQL Server'a özgü hizalama
boşluğu yüzünden aslında farklıydı, 2'si ise fazladan özdeş çıktı — bkz. faz
dokümanının "Plandan Sapmalar" bölümü) tek yerde kurar; maliyet/token toplama
ifadeleri `CostTotal`/`TreeSum` ile üretilir. Kapı: `SqlTextSnapshotTests`
(`tests/Tracon.Sql.Shared.UnitTests`) faz başında alınan taban çizgisiyle
üç dialektin ÇÖZÜMLENMİŞ (şema adı yerleşmiş) SQL metnini birebir karşılaştırır
— Docker gerekmez.

**Adımlar**
1. `dotnet test tests/Tracon.Sql.Shared.UnitTests -c Release` çalıştır.
2. `SqlQueriesBase.CostAddends`'e sahte bir dördüncü terim ekle, aynı komutu
   tekrar çalıştır, sonra geri al.
3. `Tracon.PostgreSql.IntegrationTests`, `Tracon.SqlServer.IntegrationTests`
   ve `Tracon.Sqlite.IntegrationTests` sözleşme setlerinin tamamını gerçek
   sunuculara karşı çalıştır.

**Beklenen sonuç**
- Adım 1: setin **tamamı** yeşil, sıfır başarısızlık (üç dialektin
  metin/boşluk-kapısı + `RunOrdinals` ve `CostAddends` çapraz doğrulamaları).
  Test sayısı fazlarla birlikte artar — 2026-08'de 8, 2026-09-16'da 22;
  doğrulama sabit sayıya değil sıfır başarısızlığa bakar.

> **🚨 Adım 2 bir koşum turunda KOŞULAMAZ.** `SqlQueriesBase.CostAddends`'i
> değiştirmek `src/` altında bir kod değişikliğidir; `manuel-test-kosumu`
> §1.1 bunu yasaklar ve turun bütünlük kapısı donuk ağaçların `git diff`'inin
> boş kalmasına dayanır. Bu adım ya kapanış modunda (Aşama 2) ya da repo
> dışında atılabilir bir kopyada koşulur.
- Adım 2: `CostAddendsCrossCheckTests` düşer (`RunCost.Total()`'ın karşılığı
  yok) — geri alınca yeniden yeşil.
- Adım 3: davranış AYNI kalır; hiçbir maliyet/token/kimlik alanı değişmez.

**Gerçek koşum kanıtı (2026-08-24, kapanış)**: `Tracon.Sql.Shared.UnitTests`
8/8; `Tracon.PostgreSql.IntegrationTests` 638/638;
`Tracon.SqlServer.IntegrationTests` 574/574;
`Tracon.Sqlite.IntegrationTests` 592/592 — dördü de gerçek sunuculara/dosyaya
karşı, refactor öncesi ve sonrası aynı sayılarla yeşil. Ayrıca `samples/Tracon.Api`
gerçek `ap-pg` konteynerine (taze `tracon_p94` şeması, 37 migration) karşı
çalıştırıldı: `phase94-echo` adlı bir agent tanımı oluşturuldu (maliyetsiz
`echo` sağlayıcısı — gerçek bir para harcayan sağlayıcıya dokunulmadı), bir
`run` yapıldı, `GET /tracon/api/runs/{id}` çağrıldı — kayıt `RunOrdinals`
üzerinden doğru okundu (`status: Completed`, `eventCount: 11`, akış yanıtı
tam metniyle geldi). `usage`/`cost` bu koşumda `null` döndü çünkü örnek
uygulamanın `EchoModelProvider`'ı yalnız AKIŞLI yolda kullanım bildirmiyor —
bu bir Faz 94 kusuru değil, örnek uygulamanın bilinen sınırıdır; DOLU
usage/cost yolu zaten `RunStoreContract`'ın 382 test'iyle üç gerçek
veritabanına karşı kanıtlanmıştır. `GET /tracon/api/stats?agentName=phase94-echo`
de çağrıldı — `SelectRunStatistics` (`CostTotal`/`CountWhereAnyNotNull`
kullanan sorgu) gerçek PostgreSQL'e karşı çalıştı ve `totalCost: null` (doğru
— hiçbir koşumun maliyeti yok) döndürdü. Doğrulama şeması temizlendi
(`DROP SCHEMA tracon_p94 CASCADE`).

---

### MT-PG-068 — Dış `NpgsqlDataSource`: tek havuz, iki tüketici

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 110 |
| **İlgili karar** | — |

`UsePostgreSql(o => o.DataSource = ...)`: tüketicinin kendi kurduğu bir
`NpgsqlDataSource`, hem host'un EF Core `DbContext`'ine hem Tracon'e
verilir. `ConnectionString` bu durumda **istenmez**.

**Adımlar**
1. `samples/Tracon.Embedded`'i `Tracon:PostgreSql:ConnectionString`
   ile başlat (örneğin kendi README'sindeki adımlar).
2. `POST /tickets` ile bir `run` başlat.
3. Host'un kendi tablosunu (`Tickets`) ve Tracon'in `tracon.runs`
   tablosunu aynı veritabanında `psql` ile oku.

**Beklenen sonuç**
- Başlangıç logunda tek satır: `Tracon applied N migration(s)`.
- `Tickets.RunId` == `tracon.runs.id`.
- `GET /tracon/api/runs/{id}` aynı kaydı döndürür.

**Gerçek koşum kanıtı (2026-08-26, kapanış)**: `samples/Tracon.Embedded`
yerel bir `postgres:18-alpine` konteynerine karşı gerçek connection string ile
başlatıldı. `POST /tickets` (`tenantId=acme`) bir `run` üretti
(`01a03dd5-c07f-7dac-a5c3-e76a05cb85a3`); `psql` ile doğrudan sorgulandı:
`public."Tickets"` satırının `run_id`'si `tracon.runs.id` ile birebir
eşleşti, `tracon.runs` satırı `status=1` (`Completed`), `tenant_id='acme'`.
`GET /tracon/api/runs/{id}` (`X-Host-Tenant: acme` ile) aynı kaydı 200
döndürdü. Konteyner sonda silindi.

### MT-PG-069 — `DataSource` ve `ConnectionString` birlikte verilirse başlangıç hatası

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 110 |
| **İlgili karar** | — |

Açık Soru 1'in kararı: sessiz öncelik yok, ikisi birden verilirse
`OptionsValidationException` — hangi iki alanın çeliştiğini adıyla söyler.

**Adımlar**
1. `UsePostgreSql(o => { o.DataSource = ds; o.ConnectionString = "..."; })` ile
   bir `ServiceProvider` kur.
2. `IOptions<TraconPostgreSqlOptions>.Value`'yu oku.

**Beklenen sonuç**
- `OptionsValidationException` fırlar; mesaj hem `DataSource` hem
  `ConnectionString` adını taşır.

**Gerçek koşum kanıtı (2026-08-26, kapanış)**: `ExternalDataSourceTests
.DataSource_and_ConnectionString_together_is_rejected` (`Tracon.PostgreSql
.IntegrationTests`, gerçek PostgreSQL konteynerine karşı) — yeşil. Aynı kural
`Tracon.SqlServer.IntegrationTests` ve `Tracon.Sqlite
.IntegrationTests`'in kendi `ExternalDataSourceTests` sınıflarında da
doğrulandı.

### MT-PG-070 — Dış data source host kapanışında dispose edilmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 110 |
| **İlgili karar** | — |

Tracon'in kurmadığı bir data source'u dispose etmesi, host kapanışında
tüketicinin kendi `DbContext`'ini sessizce öldürür — bu fazın en somut kusur
riski.

**Adımlar**
1. Dış bir `NpgsqlDataSource` ile `UsePostgreSql(o => o.DataSource = ds)`
   kur, bir `run` yap (migration'lar gerçekten uygulansın).
2. `ServiceProvider`'ı dispose et.
3. Aynı `ds` üzerinden yeni bir komut çalıştır.

**Beklenen sonuç**
- Adım 3'teki komut normal çalışır — `ObjectDisposedException` **yok**.
- Tracon'in **kendi** kurduğu data source (yalnız `ConnectionString`) için
  ise adım 3 `ObjectDisposedException` fırlatır — sahiplik doğru yönde çalışıyor.

**Gerçek koşum kanıtı (2026-08-26, kapanış)**: `ExternalDataSourceTests
.External_data_source_is_not_disposed_when_the_host_stops` ve
`.Own_data_source_is_disposed_when_the_host_stops` (`Tracon.PostgreSql
.IntegrationTests`, gerçek PostgreSQL) — ikisi de yeşil. Sahiplik mantığının
kendisi (`SqlStoreContext.Dispose()`/`DisposeAsync()`'in `OwnsDataSource`'a
koşullu olması) ayrıca bir spy data source ile izole test edildi:
`SqlStoreContextDisposalTests`, 5/5 yeşil, Docker gerektirmez.

### MT-PG-071 — Havuz paylaşımı ölçümü: aynı connection string, iki `NpgsqlDataSource`, tek havuz DEĞİL

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 110 |
| **İlgili karar** | — |

110.1'in ölçümü: `embedding.md`'nin eski iddiası ("aynı connection string ile
iki taraf tek Npgsql havuzunu paylaşır") **yanlıştı**. Npgsql havuzu
connection string'e değil, `NpgsqlDataSource` **örneğine** aittir.

**Adımlar**
1. Aynı connection string ile iki ayrı `NpgsqlDataSource` kur.
2. Her birinden eşzamanlı 5 bağlantı aç, sunucu tarafında `pg_sleep` ile açık
   tut.
3. 👤 `psql` ile `pg_stat_activity`'de o `application_name`'e sahip backend
   sayısını say.

**Beklenen sonuç**
- Backend sayısı **10**'a yakın (2×5), **5** değil — iki data source havuzu
  paylaşmıyor.

**Gerçek koşum kanıtı (2026-08-26, kapanış)**: `ConnectionPoolSharingTests
.Two_data_sources_built_from_the_same_connection_string_do_not_share_a_pool`
(`Tracon.PostgreSql.IntegrationTests`, gerçek PostgreSQL konteynerine
karşı) — ölçülen backend sayısı tam **10** (`concurrentConnectionsPerSource ×
2`), iddia edilen **5** değil. Sonuç `embedding.md`'ye ve fazın "Plandan
Sapmalar" bölümüne yazıldı. 👤 adımı otomatik test `pg_stat_activity`'yi
doğrudan sorgulayarak yürüttüğü için insan koşumu bu kapanışta atlandı;
istenirse aynı sorgu elle tekrarlanabilir.

## İsteğe bağlı `views` migration seti (Faz 111)

`{schema}.runs_v1` — sürümlü, salt-okunur okuma sözleşmesi görünümü. Yalnız
`Tracon:PostgreSql:EnableReadViews = true` iken kurulur (varsayılan
kapalı). Aşağıdaki case'ler faz dokümanının
([`111-OKUMA-SOZLESMESI-GORUNUMLERI.md`](../arsiv/fazlar/111-OKUMA-SOZLESMESI-GORUNUMLERI.md))
manuel kabul tablosunun karşılığıdır; tamamı kapanışta **gerçek** bir
PostgreSQL konteynerine karşı otomatik koştu (kanıt aşağıda).

### MT-PG-072 — `runs_v1` görünümü 111.2 sütun tablosunu birebir karşılar; korunan sütun taşımaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 111 |
| **İlgili karar** | — |

**Ön koşul**
- PostgreSQL, `EnableReadViews = true` ile migrate edildi.

**Adımlar**
1. `SELECT * FROM tracon.runs_v1 LIMIT 5;`
2. `\d+ tracon.runs_v1` ile sütun listesini kontrol et.

**Beklenen sonuç**
- Görünüm 111.2 tablosunun **tamamını kapsar**: `run_id`, `tenant_id`,
  `agent_name`, `session_id`, `status`, `status_name`, `started_at`,
  `completed_at`, `is_streaming`, beş token sütunu, üç ham maliyet sütunu,
  `total_cost`, `cost_currency`, `error_type`. Hiçbiri eksik olamaz.
  > **🚨 "Birebir" ARAMA — ek sütun kusur değildir.** `0001_read_views.sql:4-6`
  > sözleşmeyi yazıyor: yayımlanmış bir görünüm sütun **kaybetmez**, yeniden
  > adlandırmaz, tip daraltmaz; ama **sütun eklemek serbesttir** ve sürüm
  > yükseltmesi gerektirmez (kırıcı değişiklik `runs_v2` olarak çıkar).
  > 2026-09-16'da görünüm 24 sütun taşıyordu — 111.2'nin 20'si artı dört
  > fiyatlandırma sütunu (`model_provider`, üç `*_price_per_mtok`).
  > Doğrulama **kapsamaya** ve korunan sütun yokluğuna bakar, sayıya değil.
- Hiçbir içerik sütunu (`state`, `item`, `messages`, `text`, `payload`,
  `arguments`, `result`, `content`) yoktur.

**Gerçek koşum kanıtı (2026-08-26, kapanış)**: `ReadViewColumnSetTests`
(`Tracon.Sql.Shared.UnitTests`, veritabanı açmadan, gömülü SQL metni
üzerinden, üç sağlayıcıda) — sütun kümesi kapısı ve korunan sütun kapısı 6/6
yeşil. Canlı doğrulama: `ReadViewContractTests` (`Tracon.PostgreSql
.IntegrationTests`, gerçek PostgreSQL konteynerine karşı) 4/4.

### MT-PG-073 — `total_cost` store'un raporladığı toplamla birebir eşleşir; tanımsız fiyat `NULL` kalır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 111 |
| **İlgili karar** | — |

**Ön koşul**
- Maliyetli bir `run` ve fiyatı tanımsız bir `run` koşuldu.

**Adımlar**
1. Maliyetli koşu için görünümdeki `total_cost` ile
   `IRunStore.GetStatisticsAsync` toplamını karşılaştır.
2. Fiyatı tanımsız koşu için `total_cost`'u oku.

**Beklenen sonuç**
- Adım 1: iki değer birebir aynı.
- Adım 2: `total_cost` **`NULL`**, `0` değil.

**Gerçek koşum kanıtı (2026-08-26, kapanış)**: `ReadViewContractTests
.Total_cost_in_the_view_matches_the_stores_own_statistics` ve
`.Total_cost_is_null_not_zero_when_pricing_is_undefined`
(`Tracon.PostgreSql.IntegrationTests`, gerçek konteyner) — ikisi de yeşil.
Toplam terim kapısı (`ReadViewCostTermTests`, bağlantısız): `runs`'un üç
`*_cost` sütunu da `total_cost` ifadesinde bulunuyor; K-483 sınıfının
otomatik taraması.

### MT-PG-074 — Görünüm kiracı filtrelemez; iki kiracının satırı da görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 111 |
| **İlgili karar** | — |

**Ön koşul**
- İki farklı kiracının birer koşusu var.

**Adımlar**
1. Görünümü `tenant_id` filtresi **vermeden** sorgula.

**Beklenen sonuç**
- İki kiracının satırı da döner — bu **beklenen** davranıştır (111.1); görünüm
  bir güvenlik sınırı değildir, uyarı `reference/read-views.md`'de yazılıdır.

**Gerçek koşum kanıtı (2026-08-26, kapanış)**: `ReadViewContractTests
.View_carries_every_tenants_rows_unfiltered` (gerçek konteyner) — iki
kiracının satırı da filtresiz sorguda döndü.

### MT-PG-075 — `EnableReadViews` varsayılan kapalı; açık değilken görünüm hiç kurulmaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 111 |
| **İlgili karar** | — |

**Ön koşul**
- `EnableReadViews` verilmez (varsayılan `false`).

**Adımlar**
1. Uygulamayı başlat, migration'ları uygula.
2. `SELECT to_regclass('tracon.runs_v1');` çalıştır.

**Beklenen sonuç**
- Sonuç `NULL` — görünüm hiç kurulmadı. `__migrations`'ta `set_name='views'`
  satırı yoktur.

**Gerçek koşum kanıtı (2026-08-26, kapanış)**: repodaki ~40 paylaşılan
sözleşme test sınıfı `enableReadViews` parametresini varsayılan `false` ile
kullanır (`PostgresTestContext.Create`) ve tamamı yeşil kaldı — açık istek
olmadan `views` seti hiçbir zaman devreye girmedi.

# 03 — Kalıcılık: PostgreSQL (`PG`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../03-KALICILIK-POSTGRESQL.md`](../../03-KALICILIK-POSTGRESQL.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz A zinciri, tek şerit) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `7e3a4de7` donuk |
| **Case sayısı** | 50 (MT-PG-001..075) |
| **Port** | 5081 (spec 5080 yazar — şerit sapması) |
| **Şema** | `mt_s1` (spec `tracon` yazar — şerit sapması, skill §1.3) |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): bu dosyanın neredeyse her
case'i `dotnet user-secrets set` yazar. Hepsi şeridin **ortam değişkenine**
çevrildi (`Tracon__PostgreSql__*`). Ortam değişkeni `user-secrets`'ı ezer;
boş değer "kayıtlı değil" demektir. Depo makine genelinde tektir ve yazılmaz.

**Açılış ölçümü (reset sonrası):**

```
DROP SCHEMA mt_s1 CASCADE -> 49 nesne dustu
information_schema.schemata -> public, agentprism (mt_s1 YOK)
public tablo sayisi: 0   (MT-PG-002 / 003 taban cizgisi)
```

---

## Devir notu

**🎉 DOSYA 03 KAPANDI — 50/50 case koşuldu.** Oturum 9'da kalan 20 case bitti:
`050-053` (4) ve `060-075` (16). Dosya sonucu: **48 ☑ Geçti · 1 ☑ Kaldı
(MT-PG-068) · 1 ☐ Beklemede (MT-PG-067)**.

- **Sonraki oturumun işi:** zincirin dördüncü ailesi —
  [`05-SAGLAYICI-OPENAI.md`](../../05-SAGLAYICI-OPENAI.md), 40 case, 2 oturum.
  Bu dosyaya dönme.
- **Bozuk ön koşul:** yok.

🚨 **MT-PG-050 kullanıcı kararı UYGULANDI ve yöntem işe yaradı.** Container'a
hiç dokunulmadı; erişilemezlik **şerit-yerel bir TCP yönlendiriciyle** taklit
edildi (`<scratch>/pgproxy.py`, `55481 → 55432`). Yöntem `docker network
disconnect`'ten üstün çıktı çünkü tamamen şerit-yereldir: `ap-pg` boyunca
`Up` kaldı ve Faz B'de dört şerit paralel koşarken de güvenlidir. **MT-PG-061
aynı yöntemle koşuldu** ve kurtarmayı PID ile kanıtladı — aynı süreç hem düşen
hem toparlanan isteği karşıladı. Sonraki oturumlar container durdurmak isteyen
her case için bu tarifi kullanmalıdır.

### Oturum 9'un bulguları

| Bulgu | Önem | Kısaca |
|---|---|---|
| `HATA-S1-019` | **Yüksek** | `UsePostgreSql` tüketicinin store kaydını `Replace` ile sessizce eziyor — `AGENTS.md`'nin "`TryAdd*` ile kaydet" kuralının ihlali. 117 çağrı, 34 arayüz. `Tracon.Embedded`'in belgelenmiş akışını kırıyor (MT-PG-068 bu yüzden **Kaldı**) |
| `HATA-S1-018` | Orta | Sevk edilen iki hata mesajı karışık dilde (`ne 'Input' ne 'Output'`); dil kapısı iki harfli kelimeleri bilinçli dışladığı için **yapısal olarak** göremiyor |
| `HATA-S1-017` | Düşük | Taze şemaya karşı her açılış `Error` seviyesinde yığın izi basıyor; yutma bir katman geç yapılıyor |
| `HATA-S1-016` | Düşük–Orta | `/health` kendi başına hiçbir zaman `Healthy`'ye ulaşmaz — `/api/models/health` çağrılmadıkça sonsuza dek `Degraded` |
| `HATA-S1-015` | Orta | (oturum 8) Bekleyen migration varken yazma ucu opak `500` dönüyor. **Oturum 9'da ikinci ampirik örnek eklendi** (MT-PG-061): erişilemez veritabanı da aynı opak `500`'e düşüyor |

### Oturum 9'un bıraktığı kalemler

🚨 **MT-PG-067 adım 2 kullanıcı kararı bekliyor.** Case
`SqlQueriesBase.CostAddends`'e sahte bir terim eklemeyi istiyor — `src/`
altında kod değişikliği, kural 1 yasaklıyor. Adım 1 ve 3 yeşil koşuldu.
Seçenekler bekleyen kalem tablosundadır.

🚨 **`HATA-S1-019`'un ilk kapanış sorusu ölçülmeli:**
`RequireCustomBinding<ITenantStore>()` çağrılsaydı başlangıçta patlar mıydı,
yoksa o da mı sessiz kalırdı? Cevap kusurun örnekte mi yoksa koruma
mekanizmasında mı olduğunu belirler.

### Oturum 9'un ortam notları

🚨 **Fiyat yapılandırma yolu:** `Tracon:Pricing:{saglayici}:{model}:Input` /
`:Output`. **`Providers` segmenti YOKTUR** ve yaprak anahtar C# özellik adı
(`InputCostPerMillionTokens`) **değildir**. Yanlış anahtar başlangıçta
`OptionsValidationException` ile reddedilir.

🚨 **Kiracı başlığı iki bayrak ister:** `X-Tracon-Tenant` yalnız
`Tracon:Tenancy:Enabled=true` **ve** `AllowHeaderResolution=true` iken okunur;
ikisi de varsayılan kapalı. Kapalıyken başlık **sessizce** yok sayılır ve her
istek `DefaultTenantId`'ye düşer.

🚨 **Nokta/tire taşıyan yapılandırma anahtarı ortam değişkeni olamaz** —
zsh `export Tracon__Pricing__...gpt-5.4-mini...` adını reddeder. Komut satırı
yapılandırması kullan: `dotnet run ... -- "--Tracon:Pricing:openai:gpt-5.4-mini:Input=0.25"`.

🚨 **`timeout` macOS'ta yoktur** (çıkış 127). Fail-fast bekleyen case'lerde
süreci arka planda koşup çıkış kodunu dosyaya yaz.

⚠️ **Sağlık yoklama döngüsüne gecikme koy.** Gecikmesiz bir `for` döngüsü 60
denemeyi bir saniyede tüketir ve uygulama açılmadan "kapalı" der.

### Oturum 9'un bıraktığı şerit-yerel kaynaklar (tur sonunda silinecek)

| Kaynak | Ne | Durum |
|---|---|---|
| `ap-pg-plain-s1` | `pgvector`'süz `postgres:18-alpine`, port 55433 (MT-PG-062/063) | **silindi** |
| `mt_s1_k` şeması | MT-PG-065 iki aşamalı knowledge seti | duruyor |
| `mt_s1_v` şeması | MT-PG-072/073/074 okuma görünümleri + 6 `run` | duruyor |
| `tracon_embedded_s1` veritabanı | MT-PG-068 `Tracon.Embedded` | duruyor |
| `<scratch>/pgproxy.py` | TCP yönlendirici tarifi | **tarif devir notunda korundu** |

Çalışan süreç bırakılmadı; `ap-pg` ve `ap-mssql` boyunca dokunulmadan `Up` kaldı.

### Oturum 8'in bulgusu

| Bulgu | Önem | Kısaca |
|---|---|---|
| `HATA-S1-015` | Orta | Bekleyen migration varken yazma ucu opak `500` dönüyor; uygulama 51 bekleyen migration'ı biliyor (`/health` 503, `diagnostics`) ama yanıta taşımıyor |

### Ortam — sonraki oturumun bilmesi gerekenler

🚨 **Bu dosyanın neredeyse her case'i `dotnet user-secrets set` yazar.** Hepsi
şeridin ortam değişkenine çevrildi (skill §1.2). Depo yazılmadı.

🚨 **Model adı: `gpt-5.4-mini`.** Örnek uygulamanın varsayılanı budur
(`Program.cs:390-395`). Rastgele bir OpenAI modeli seçme — `gpt-4o-mini`
**HTTP 403 `model_not_found`** verdi (`Project proj_0iwMbkX0... does not have
access`). Anahtar geçerli, erişim dar.

🚨 **İzlek A'da `EnableKnowledge = true` şart.** `IVectorSearchStore` fabrikası
kapalıyken `null` döner ve `GetRequiredService` "No service for type ... has been
registered" atar. Örnek uygulama açık taşıdığı için İzlek B'de görünmez.
Altı vektör case'inin spec kodu düzeltildi.

🚨 **`UsePostgreSql` `ITraconBuilder` üzerindedir**, `IServiceCollection`
üzerinde değil — `AddTracon()`'un dönüşü yakalanmalı (`CS1929`).

🚨 **`NpgsqlDataSource` public bir DI servisi DEĞİLDİR** (Faz 110).

**Bırakılan aparat** (tur sonunda silinecek): `~/tracon-manuel/pg-ayarlar` ·
`pg-baglantisiz` · `tryadd` · `replace` · `vektor` · `ikili-saglayici`.
Çalışan süreç bırakılmadı.

### Spec düzeltmeleri (skill §1.1 istisnası — hepsi doküman kusuru)

| Case | Ne düzeltildi |
|---|---|
| 002 · 003 · 004 · 020 · 023 · 024 · 025 · 026 · 047 | Beklenen mesaj metni Türkçe yazılmıştı; sevk edilen metin İngilizce (K-228) |
| 020 · 021 · 022 · 024 · 026 | Sabit migration sayısı **33/28** bayattı → **51/46**; sayıya değil **yapıya** bakan doğrulama notu eklendi |
| 005 | `Microsoft.Extensions.Configuration` paketi eksikti (Tracon implementation paketini kasıtlı getirmez) |
| 006 | `NpgsqlDataSource` çözümlüyordu — Faz 110'dan beri DI servisi değil; gerçek tüketici yoluna çevrildi |
| 025 · 030 | `provider:"echo"` gerçek anahtar varken kayıtlı değil; model adı düzeltildi |
| 031 · 032 · 033 | `services.UsePostgreSql` → `tracon.UsePostgreSql` (`ITraconBuilder`); `BranchAsync` gerçek imzası; tanımsız `SahteRunStore` fabrikaya çevrildi |
| 041–046 | `EnableKnowledge = true` eklendi |

**`00-INDEKS.md` §4 düzeltildi:** "32 çekirdek + 1 = 33" → "50 çekirdek + 1 = 51",
artı sabit sayıya güvenmeme uyarısı. Önceki oturumun bu aileye bıraktığı açık
kalem **kapandı**. (§3.2 tool tablosu hâlâ bayat — o dosya 02'nin konusu.)

---

## MT-PG-001 — Boş bağlantı dizesiyle başlatma reddedilir

**Gerçek sonuç**
Spec'in 2026-08-15'te düzeltilmiş beklentisi **birebir doğrulandı**. Boş
`Tracon__PostgreSql__ConnectionString` ile uygulama **başladı**:

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5081
```

`GET /health` → **200 `Degraded`** (model sağlayıcı nedeniyle, kalıcılık
nedeniyle değil). `GET /tracon/api/diagnostics` bunu açıkça söylüyor:

```json
"persistenceProvider": "InMemory",
"registeredPersistenceProviders": 0,
"canConnect": true,
"migrationsUpToDate": true
```

`information_schema.schemata` içinde `mt_s1` **yok** (0 satır) — validator'a hiç
ulaşılmadığı için hiçbir şema açılmadı. `samples/Tracon.Api/Program.cs:866`
`UsePostgreSql`'i yalnız bağlantı dizesi doluyken çağırır; İzlek B'de
`TraconPostgreSqlOptionsValidator` yapısal olarak erişilemez.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-002 — Şema adı `public` olamaz

**Gerçek sonuç**
Uygulama başlamayı **reddetti**, süreç `EXIT=134` ile sonlandı; `Now listening on`
satırı hiç yazılmadı.

```
Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
TraconPostgreSqlOptions.SchemaName cannot be 'public'. Tracon never touches the
consumer's public schema. Rationale: docs/KARARLAR.md, decision K-013.
   at Tracon.TraconPostgreSqlBuilderExtensions...UsePostgreSql...
      src/Tracon.PostgreSql/TraconPostgreSqlBuilderExtensions.cs:line 110
```

Doğrulama sorgusu: `information_schema.tables WHERE table_schema='public'` →
**0**, denemeden önceki sayıyla aynı. Tüketicinin `public` şemasına hiçbir tablo
yazılmadı.

📝 **Spec düzeltmesi (doküman kusuru, skill §1.1 istisnası).** `Beklenen sonuç`
`'public' olamaz` ve `K-013` **Türkçe** metnini arıyordu; sevk edilen mesaj
İngilizce'dir (`cannot be 'public'` · `decision K-013`) ve K-228 dil sınırı gereği
öyle olmalıdır — `TraconPostgreSqlOptionsValidator.cs:47-50`. Beklenti koda göre
düzeltildi. Kusur ürün değil, spec metnidir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-003 — Geçersiz şema adı biçimleri ve enjeksiyon denemesi reddedilir

**Gerçek sonuç**
Üç deneme de uygulamanın başlamasını **reddetti** (`EXIT=134`, `Now listening on`
sıfır kez). Hata mesajı üçünde de aynı biçimde, reddedilen değeri de yazıyor:

```
'Tracon'                                -> ...is not a valid unquoted PostgreSQL identifier...
                                           Actual value: 'Tracon'.
'agent prism'                           -> ... Actual value: 'agent prism'.
'tracon; DROP SCHEMA public CASCADE;--' -> ... Actual value: 'tracon; DROP SCHEMA public CASCADE;--'.
```

3. denemede `DROP SCHEMA` **çalıştırılmadı**; doğrulama sorgusu `public` şemasını
bir satır olarak geri verdi. `TraconPostgreSqlOptionsValidator.cs:38-43`
`SqlIdentifier.IsValidUnquoted` çağrısını ad SQL metnine yerleştirilmeden **önce**
yapıyor — enjeksiyon dizesi hiçbir zaman bir komuta girmiyor.

📝 **Spec düzeltmesi (doküman kusuru).** Beklenen mesaj Türkçe yazılmıştı
(`gecerli bir tirnaksiz PostgreSQL tanimlayicisi degil`); sevk edilen metin
İngilizce'dir (K-228). Beklenti koda göre düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-004 — `CommandTimeoutSeconds` sınırları

**Gerçek sonuç**
Dört değer de beklendiği gibi ayrıştı.

| Değer | Sonuç | Kanıt |
|---|---|---|
| `-1` | reddedildi | `...CommandTimeoutSeconds must be between 0 and 3600. Actual value: -1.` · `EXIT=134` |
| `3601` | reddedildi | `... Actual value: 3601.` · `EXIT=134` |
| `0` | **başladı** | `Now listening on: http://localhost:5081` · `/health` 200 `Degraded` |
| `3600` | **başladı** | `Now listening on: http://localhost:5081` · `/health` 200 `Degraded` |

Reddedilen iki mesaj da gönderilen değeri `Actual value:` ile geri veriyor —
`TraconPostgreSqlOptionsValidator.cs:54-58`. `0` değerinde uygulama şemayı kurup
migration'ları uyguladı (`CREATE TABLE IF NOT EXISTS mt_s1.__migrations`), yani
"sınırsız" anlamı sessiz bir devre dışı bırakma değil.

📝 **Spec düzeltmesi (doküman kusuru).** Beklenen mesaj Türkçe yazılmıştı
(`0 ile 3600 arasinda olmalidir`); sevk edilen metin İngilizce'dir (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-005 — Üç `UsePostgreSql` aşırı yüklemesi aynı sonucu üretir

**Gerçek sonuç**
Üç satır da birebir aynı:

```
string:          ConnectionString='Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon' SchemaName='tracon'
IConfiguration:  ConnectionString='Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon' SchemaName='tracon'
Action<Options>: ConnectionString='Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon' SchemaName='tracon'
```

`UsePostgreSql(string)` şema adını vermeden `tracon` varsayılanına düşüyor ve
diğer iki yolun açıkça verdiği değerle eşleşiyor.

**K-021 riski ölçüldü — bugün temiz.** Case'in uyardığı sessiz kayıp (elle
yazılmış `Bind()` bir alanı atlarsa) bugün yok.
`TraconPostgreSqlBuilderExtensions.cs:437-471` altı ayarın **altısını** da
bağlıyor: `ConnectionString` · `SchemaName` · `AutoApplyMigrations` ·
`CommandTimeoutSeconds` · `EnableKnowledge` · `EnableReadViews`.
`TraconPostgreSqlOptions`'ın yedinci üyesi `DataSource` bir `DbDataSource`
nesnesidir ve yapılandırma metninden gelemez — dışlanması doğrudur.

📝 **Spec düzeltmesi (doküman kusuru).** Case'in konsol kodu `ConfigurationBuilder`
kullanıyor ama `Girilecek veri` bloğu yalnız `Tracon.PostgreSql` paketini
ekliyordu; derleme `CS0246: ConfigurationBuilder` ile düştü.
**Bu bir ürün kusuru değildir** — `Tracon.PostgreSql` `IConfiguration`
*abstraction*'ını getirir, *implementation* paketini getirmez; tüketicinin
bağımlılık grafiğini kirletmemek kasıtlıdır (`AGENTS.md`). Harness'e
`Microsoft.Extensions.Configuration` eklendi ve spec'in adımına da yazıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-006 — Bağlantı dizesi hiç verilmezse hangi denetim önce tetiklenir

**Gerçek sonuç**
Case'in sorduğu soru **cevaplandı: Validator önce tetikleniyor.** Factory'nin
kendi `TraconException`'ına hiçbir yoldan ulaşılmıyor.

```
IOptions<TraconPostgreSqlOptions>.Value:  Microsoft.Extensions.Options.OptionsValidationException
  mesaj: TraconPostgreSqlOptions.ConnectionString cannot be empty. Give it in the
         `UsePostgreSql(...)` call, define the 'Tracon:PostgreSql:ConnectionString'
         setting in `dotnet user-secrets`, or set DataSource instead.

IAgentDefinitionStore (gercek tuketici yolu):  ayni istisna, ayni mesaj
```

Mesaj kullanıcı için anlaşılır ve **üç çıkış yolunu birden** sayıyor. Gerekçe
`TraconPostgreSqlBuilderExtensions.cs:108-115`: veri kaynağı `SqlStoreContext`
fabrikasının **içinde** çözülüyor ve o fabrikanın ilk satırı
`IOptions<TraconPostgreSqlOptions>.Value` — Validator kaçınılmaz olarak önce
koşuyor.

🚨 **Spec'in `Girilecek veri` bloğu bayattı (doküman kusuru, ürün kusuru değil).**
Kod `provider.GetRequiredService<NpgsqlDataSource>()` çağırıyordu; bugün alınan
yanıt şu:

```
istisna tipi: System.InvalidOperationException
mesaj: No service for type 'Npgsql.NpgsqlDataSource' has been registered.
```

**Faz 110'dan beri `NpgsqlDataSource` bilerek public bir DI servisi DEĞİLDİR**
(`TraconPostgreSqlBuilderExtensions.cs:100-107`): eskiden iki yönlü bir yarıştı —
ya tüketicinin kendi kaydı sessizce kazanıyor ve Tracon'in `ConnectionString`'i
ölü ayara dönüyordu, ya da Tracon'in örneği public bir Npgsql tipi altında
tüketicinin container'ına sızıyordu. Tek giriş yolu artık
`TraconPostgreSqlOptions.DataSource` alanıdır. Case bu yüzden yanlış tipi
çözümlüyordu ve soruyu hiç sormadan düşüyordu. Adımlar tüketicinin gerçekten
gördüğü yola (`IOptions<...>.Value` ve bir depo) çevrildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-007 — Yanlış host ile başlatma migration adımında çöker (fail-fast)

**Gerçek sonuç**
Fail-fast **doğrulandı.** `Port=1` ile süreç `EXIT=134` ile sonlandı ve
`Now listening on` satırını **hiç yazmadı** (0 eşleşme).

```
fail: Microsoft.Extensions.Hosting.Internal.Host[11]
      Npgsql.NpgsqlException (0x80004005): Failed to connect to 127.0.0.1:1
       ---> System.Net.Sockets.SocketException (61): Connection refused
```

Paralel terminaldeki `curl -m 3 http://localhost:5081/health` → **HTTP 000**
(bağlantı reddi). Kestrel hiç dinlemeye başlamadı, yani şema hazır değilken
sessizce çalışan bir Tracon örneği ortaya çıkmıyor.

⚠️ Aynı zincir başlangıçta bir kez de `fail: Tracon.CompositeAgentCatalog[0]`
altında loglanıyor — katalog kalıcılığa erişmeyi deniyor ve düşüyor. Süreç yine
de kapandığı için davranış doğru; yalnız ilk görünen hata satırı "katalog"
diyor, "migration" demiyor. Kusur değil, gürültü notu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-008 — `secret` hiçbir zaman veritabanına veya dosyaya yazılmaz

**Gerçek sonuç**
K-059 iddiası doğrulandı. Uygulama normal çalışırken (14 agent kayıtlı,
51 migration uygulanmış):

| Tarama | Sonuç |
|---|---|
| `grep -rn "Password=tracon\|Host=localhost;Port=55432" samples/Tracon.Api/appsettings*.json` | **0 satır** (çıkış kodu 1) |
| `mt_s1.agent_definitions WHERE definition::text ILIKE '%Password=%'` | **0** |
| `mt_s1.audit_log WHERE before/after ILIKE '%Password=%'` | **0** |

**Spec'in iki sorgusundan fazlasını koştum.** Case `Kritik` olduğu için şemadaki
`text` · `jsonb` · `varchar` tipli **221 sütunun tamamı** üretilen bir `UNION ALL`
sorgusuyla hem `%Password=%` hem `%sk-%` (sağlayıcı anahtarı deseni) için
tarandı — **sıfır** eşleşme. Bağlantı dizesi de sağlayıcı anahtarı da veritabanına
hiçbir sütundan sızmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-020 — Boş DB'de çekirdek + knowledge seti sırayla uygulanır

**Gerçek sonuç**
Şema düşürülüp uygulama başlatıldı. Açılış logu:

```
Tracon applied 51 migration(s). Schema: mt_s1.
```

| Ölçüm | Sonuç |
|---|---|
| `count(*) FROM mt_s1.__migrations` | **51** |
| `set_name` grubu | `core` **50** · `knowledge` **1** |
| `core` id 24 | **yok** (23 → 25 atlıyor, K-475 numara geri dönüştürülmez) |
| `core` son satır | id **51**, `0051_run_score_evaluator_version` |
| `knowledge` tek satır | id **1**, `0001_vector` — `core` id 1 (`0001_initial`) ile çakışmıyor |
| birincil anahtar | `PRIMARY KEY (set_name, id)` |

Kaynak ölçümü doğruluyor: `src/Tracon.PostgreSql/Migrations/*.sql` **50** dosya,
`src/Tracon.PostgreSql/MigrationsKnowledge/*.sql` **1** dosya.

🚨 **Spec düzeltmesi — bu, önceki oturumun açık bıraktığı bayatlığın kaynağıydı.**
Beklenti **33** migration (32 `core` + 1 `knowledge`) diyordu; bugün **51**
(50 + 1). Faz 67'den sonra eklenen 18 çekirdek migration sayıyı büyütmüş, spec
güncellenmemişti. Yapısal iddiaların **hiçbiri** bozulmadı — yalnız sabit sayılar
bayattı. Beklenti koda göre düzeltildi ve **sayıya değil yapıya bakan** bir
doğrulama notu eklendi, böylece bir sonraki migration bu case'i yeniden
bayatlatmaz. Log metni de Türkçe yazılmıştı (`Tracon 33 migration uyguladi`);
sevk edilen metin İngilizce'dir (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-021 — Yeniden başlatma migration'ları tekrar uygulamaz (idempotent)

**Gerçek sonuç**
Şema düşürülmeden uygulama durduruldu ve yeniden başlatıldı.

| Ölçüm | Sonuç |
|---|---|
| `applied N migration(s)` satırı | **0 eşleşme** — hiç basılmadı |
| `fail:` / `Unhandled exception` | **0** |
| `Now listening on` | var, normal açılış |
| `__migrations` satır sayısı | **51** (50 `core` + 1 `knowledge`), değişmedi |

İdempotanlık doğrulandı: ikinci açılış hiçbir migration uygulamadı ve defter
birebir aynı kaldı.

📝 **Spec düzeltmesi.** Beklenti sabit **33** satır diyordu; MT-PG-020'nin
ölçümüne (**51**) bağlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-022 — Var olan (kısmi) şema üzerine devam

**Gerçek sonuç**
İlk beş migration elle uygulandı ve defter **Faz 67 öncesi şekliyle** kuruldu
(`id` tek başına PK, `set_name` sütunu **yok** — doğrulandı: 0 sütun, 20 tablo).
Uygulama başlatıldı:

```
Tracon applied 46 migration(s). Schema: mt_s1.
```

46 = 50 çekirdek − 5 elle uygulanmış + 1 knowledge. Formül tutuyor.
`Unhandled exception` **0**, checksum uyuşmazlığı **yok** — elle hesaplanan beş
checksum motorun beklediğiyle birebir eşleşti.

Şema yükseltmesi (K-475) de doğrulandı:

| İddia | Ölçüm |
|---|---|
| Toplam satır | **51** (`core` 50 + `knowledge` 1) |
| id 1–5 `set_name` | **`core`'a geriye dönük doldu** |
| id 1–5 `applied_at` | `17:38:30.97` – `17:38:31.34` — **elle yazılan zaman korundu** |
| id 6+ `applied_at` | `17:38:42.10`+ — uygulamanın az önceki açılış zamanı |
| birincil anahtar | `PRIMARY KEY (set_name, id)` — **genişletildi** |

Yani motor hem yarım kalmış bir dağıtımı hem de Faz 67 öncesi bir veritabanını
veri kaybetmeden devralıyor.

📝 **Spec düzeltmesi.** Beklenti `28 migration` ve `33` satır diyordu; bugünkü
ölçüm **46** ve **51**. Yalnız sayılar bayattı, yapısal iddiaların hepsi tuttu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-023 — Uygulanmış migration'ın checksum'ı bozulursa başlama reddedilir

**Gerçek sonuç**
`core`/`id=1` checksum'ı bozuldu. Uygulama başlamayı **reddetti** — `EXIT=134`,
`Now listening on` **0 eşleşme**.

```
Tracon.TraconException: Migration '0001_initial' has been applied to the database
but the file's content has changed. Checksum in the database:
BOZUK0000000000000000000000000000000000000000000000000000000, checksum of the file:
16659329B8A6525B3A20133B832C973728F78FA23D564F21C780169BB7B1B12F.
An applied migration is never edited; add a new migration file for the change.
```

Mesaj **her iki checksum'ı da** gösteriyor ve ne yapılacağını söylüyor
("add a new migration file"). Temizlik: dosyadan hesaplanan doğru checksum
(`16659329...B12F`, mesajın gösterdiğiyle birebir aynı) geri yazıldı.

📝 **Spec düzeltmesi.** Beklenen mesaj Türkçe yazılmıştı; sevk edilen metin
İngilizce'dir (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-024 — İki eşzamanlı örnek çakışmadan migration uygular

**Gerçek sonuç**
Temiz şemada iki örnek eş zamanlı başlatıldı (5081 ve 5181).

| Örnek | `Now listening on` | migration satırı |
|---|---|---|
| A (5081) | 1 | **yok** — 0 migration uyguladı |
| B (5181) | 1 | `Tracon applied 51 migration(s). Schema: mt_s1.` |

`Unhandled exception`: ikisinde de **0**. Defter **51** satır (50 `core` +
1 `knowledge`) — 102 değil, yani birincil anahtar çakışması yok.

Kilidin izi A'nın logunda görünüyor: `SELECT pg_advisory_lock($1)` **228 ms**
sürdü (B migration'ları uygularken bekledi), ardından `pg_advisory_unlock($1)`
0 ms. A kilidi aldığında uygulanacak bir şey kalmamıştı.

⚠️ Bu koşumda kilidi **B** aldı, spec'in ima ettiği gibi "ilk başlatılan"
değil. Yarış belirsizdir ve olması gereken budur; spec'e not düşüldü.

**Şerit sapması:** ikinci örnek 5181'de koşuldu. 5082–5084 diğer şeritlerin,
5090 kapanış doğrulama sunucusunundur.

📝 **Spec düzeltmesi.** Sayılar 33/66 → **51/102**; log metni İngilizce (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-025 — `AutoApplyMigrations=false` migration uygulamaz, sorumluluk operatöre kalır

**Gerçek sonuç**
İlk üç iddia tuttu, dördüncüsü **tutmadı**.

| İddia | Sonuç |
|---|---|
| Uygulama başlar, çökmez | ☑ `Now listening on: http://localhost:5081` |
| Log "otomatik uygulanmıyor" der | ☑ `Tracon migrations are not applied automatically (AutoApplyMigrations is off). Keeping the schema current is the caller's responsibility.` |
| `/health` Unhealthy | ☑ **503 `Unhealthy`** |
| Agent kaydı 5xx, uygulama çökmez, **anlaşılır** hata | ☑ 5xx · ☑ çökmedi · 🚨 **anlaşılır değil** |

Agent kaydı iki kez denendi, ikisi de **HTTP 500** ve ikisi de aynı opak gövde:

```json
{"title":"An error occurred while processing your request.","status":500,
 "traceId":"00-8dbc36c93c03b534391d1ee5e074a56b-..."}
```

Uygulama ayakta kaldı, ikinci istek de aynı biçimde döndü. Bulgu
`HATA-S1-015` olarak açıldı (aşağıda). **Kullanıcı kararı (2026-09-16):**
"anlaşılır hata" istemciye giden yanıttır; opak 500 bunu karşılamaz.

**Yan doğrulama — dosya 02'nin taşıdığı tuzak yeniden üretildi.** `provider:"echo"`
ile istek **400** döndü: `No model provider named 'echo' is registered. Registered
providers: anthropic, google, openai, openai-responses, openrouter.` Gerçek OpenAI
anahtarı ortamda olduğu için `EchoModelProvider` kaydedilmiyor. Case veritabanı
yolunu ölçtüğünden istek `openai` sağlayıcısıyla tekrarlandı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

### HATA-S1-015 — Bekleyen migration varken yazma ucu opak 500 döner

| | |
|---|---|
| **Case** | MT-PG-025 |
| **Önem** | Orta |
| **Sınıf** | teşhis edilebilirlik — uygulama nedeni biliyor, istemciye söylemiyor |

**Belirti.** `AutoApplyMigrations=false` ile açılmış ve migration'ları koşulmamış
bir kurulumda `POST /tracon/api/agents` şunu döner:

```
HTTP 500
{"title":"An error occurred while processing your request.","status":500,"traceId":"..."}
```

**Kök neden.** Yazma yolunda şema hazırlık kontrolü yok; `Npgsql.PostgresException
42P01: relation "mt_s1.agent_definitions" does not exist` genel ASP.NET Core
işleyicisine kadar çıkıyor ve orada ayrıntısız `ProblemDetails`'e dönüşüyor.
`src/Tracon.AspNetCore/` altında `PostgresException`/`DbException` için özel bir
eşleme yok.

**Neden düzeltilebilir.** Uygulama cevabı **zaten biliyor**: aynı anda
`/health` → `503 Unhealthy`, `/tracon/api/diagnostics` → `migrationsUpToDate:
false`, `pendingMigrations: 51`. Yani eksik olan bilgi değil, o bilginin yazma
yanıtına taşınması.

**Kapsam dışı olan.** `SchemaReadyGate`'in bu durumda **açılması** kusur değildir
ve öyle kalmalıdır — `MigrationHostedService.cs:102-112` gerekçeyi yazıyor:
şema tüketicinin sorumluluğundadır ve arka plan servislerini sonsuza dek bekletmenin
faydası yoktur.

**Öneri (kapanışta değerlendirilecek).** Şema güncel değilken yazma uçları
`503` + `"schema is not current; N migrations pending"` taşıyan bir
`ProblemDetails` dönsün. Şema adı ve ham SQL metni **sızdırılmamalı** — bugünkü
opaklığın savunulabilir tarafı budur.

**Sınıf taraması notu.** Aynı desen tüm SQL depo yazmalarını etkiler, yalnız
`agent_definitions`'ı değil; düzeltme tek uca değil ortak yola konmalıdır.

**İkinci ampirik örnek (2026-09-16, MT-PG-061).** Sınıf taraması notu
doğrulandı. Veritabanı **erişilemez** olduğunda (bekleyen migration değil)
`POST /api/agents/manuel-esz-1/run` de aynı opak gövdeyi döndü:

```
run HTTP: 500
{"title":"An error occurred while processing your request.","status":500,"traceId":"..."}
```

Yani iki ayrı tetikleyici (`42P01 relation does not exist` · bağlantı
kurulamıyor) ve iki ayrı yol (yazma · çalıştırma) **tek** opak yanıta
düşüyor. Düzeltme ortak yola konmalıdır; ayrıca bu iki durum istemci için
farklıdır — biri kalıcı (şema eksik), diğeri **geçici** (yeniden denenebilir).
Bugünkü yanıt ikisini ayırt etmiyor.

## MT-PG-026 — Şema adı değiştirildiğinde bağımsız bir migration seti oluşur

**Gerçek sonuç**
`mt_s1` tam setle kuruldu ve içine bir iz bırakıldı (bir agent, `HTTP 201`).
Sonra yalnız şema adı `mt_s1_ikinci` yapılıp uygulama yeniden başlatıldı:

```
Tracon applied 51 migration(s). Schema: mt_s1.          <- ilk acilis
Tracon applied 51 migration(s). Schema: mt_s1_ikinci.   <- ikinci acilis
```

| Ölçüm | `mt_s1` | `mt_s1_ikinci` |
|---|---|---|
| şema var mı | ☑ | ☑ |
| `__migrations` | **51** | **51** |
| `agent_definitions` | **1** (iz duruyor) | **0** (yepyeni) |

İkinci şema sıfırdan ve tam kuruldu; birincinin verisine dokunulmadı. İki defter
birbirinden bağımsız. Temizlik: `DROP SCHEMA mt_s1_ikinci CASCADE`.

📝 **Spec düzeltmesi.** Ön koşul ve beklenti **28** migration diyordu; sabit sayı
MT-PG-020'nin ölçümüne bağlandı. Log metni İngilizce (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-027 — `Dimensions` değişikliği uygulanmış `vector` sütununun boyutunu DEĞİŞTİRMEZ

**Gerçek sonuç**
`Tracon__Knowledge__Dimensions=3` ile yeniden başlatıldı. Operasyonel tuzak
**birebir doğrulandı**:

| İddia | Ölçüm |
|---|---|
| Uygulama normal başlar | ☑ `Now listening on` |
| Checksum uyuşmazlığı **oluşmaz** | ☑ `content has changed` → 0 eşleşme |
| Yeni migration uygulanmaz | ☑ `applied N migration(s)` satırı yok |
| `atttypmod` hâlâ **1536** | ☑ `1536` · `vector(1536)` — `3` değil |

`{dimension}` yer tutucusu bir şema yer tutucusu gibi davranmıyor: checksum ham
metin üzerinden hesaplandığı için ayar değişikliği migration'ı "değişmiş"
göstermiyor, ama uygulanmış sütuna da dokunmuyor. Ayar **sessizce** hiçbir şey
yapıyor.

⚠️ **"Sessizce" kelimesi ölçüldü ve tam anlamıyla doğru:** koşumda `warn:`
seviyesinde **0** satır var ve log'da `dimension` geçen hiçbir uyarı yok.
Operatör 1536'lık bir sütunla 3 boyutlu embedding üretmeye devam eder ve bunu
ancak ilk `UpsertAsync` reddinde (MT-PG-041) öğrenir. Bu, case'in beklediği
davranıştır ve bugün **kusur sayılmadı**; açılışta "yapılandırılan boyut (3)
uygulanmış sütunla (1536) uyuşmuyor" uyarısı bir yetenek adayıdır — oturum
sonunda `ADAYLAR.md` önerisi olarak taşınır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-030 — Yönetimsel yazmalar denetim izine düşer, yürütme yan ürünleri düşmez

**Gerçek sonuç**
Ayrım **temiz**. Agent kaydedildi (`HTTP 201`), modeli güncellendi (`HTTP 200`) ve
çalıştırıldı (SSE: `1 run · 7 update · 1 done`).

`audit_log`'un tamamı:

| entity | action | adet |
|---|---|---|
| `agent:manuel-denetim` | `agent.create` | 1 |
| `agent:manuel-denetim` | `agent.update` | 1 |
| `agent:mt-pg-026-iz` | `agent.create` | 1 |

- Agent varlığı için denetim kaydı **var** (3 satır, hepsi yönetimsel yazma).
- `entity ILIKE '%run%' OR entity ILIKE '%tool_invocation%'` → **0**.
- Aynı anda `mt_s1.runs` **2** satır taşıyor.

Yani çalışmalar kaydediliyor ama denetim izine girmiyor: `IRunStore` denetim izi
dekoratörüyle sarılmamış.

🚨 **Spec düzeltmesi — iki ayrı tuzak.** (1) Case `provider:"echo"` diyordu;
gerçek anahtar varken `echo` kayıtlı değil (dosya 02'nin taşıdığı kural).
(2) `echo` yerine rastgele seçtiğim `gpt-4o-mini` ile `run` **HTTP 403
`model_not_found`** verdi: `Project proj_0iwMbkX0... does not have access to model
gpt-4o-mini`. Anahtar geçerli, erişim yok. Örnek uygulamanın varsayılanı
`gpt-5.4-mini`'dir (`Program.cs:390-395`) ve onunla çalışma sorunsuz tamamlandı.
Spec'e ikisi de not düşüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-031 — `IConversationBranchStore` `TryAdd` önceliği

**Gerçek sonuç**
```
Cozumlenen tip: SahteDalStore
```

Tüketicinin `UsePostgreSql()`'den **önce** kaydettiği uygulama kazandı;
`SqlConversationBranchStore` onu ezmedi. `TryAddSingleton` önceliği doğrulandı.

📝 **Spec düzeltmesi — iki derleme hatası (case bunu zaten öngörüyordu).**
1. `services.UsePostgreSql(...)` derlenmiyor: `CS1929` — genişletme metodu
   `ITraconBuilder` üzerindedir, `IServiceCollection` üzerinde değil.
   `AddTracon()`'un döndürdüğü builder yakalanmalı.
2. Arayüzün gerçek üyesi `CreateBranchAsync` / `ConversationBranchInfo` değil,
   `BranchAsync` / `ValueTask<ConversationBranch?>`
   (`src/Tracon.Abstractions/Sessions/SessionBranch.cs:92`). Case'in kendi notu
   bu durumu öngörüyordu; gövde gerçek imzaya göre düzeltildi.

İkisi de spec'e yazıldı (MT-PG-032'nin kodu aynı `ITraconBuilder` hatasını
taşıyordu, o da düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-032 — `IVectorSearchStore` aynı `TryAdd` önceliğine uyar

**Gerçek sonuç**
```
Cozumlenen tip: SahteVektorStore
```

`PgVectorSearchStore` tüketicinin kaydını ezmedi — K4 önceliği MT-PG-031'le aynı
biçimde tutuyor. Bu case'deki `IVectorSearchStore` imzası (beş üye) spec'te
**doğru** yazılmıştı, gövde hiç değiştirilmedi; yalnız MT-PG-031 ile ortak olan
`ITraconBuilder` düzeltmesi uygulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-033 — Diğer depolar `Replace` ile kayıtlıdır: tüketici önce kaydetse de PostgreSQL kazanır

**Gerçek sonuç**
```
Cozumlenen tip: Tracon.SqlRunStore
```

Tüketici `IRunStore`'u `UsePostgreSql()`'den **önce** kaydetmiş olmasına rağmen
PostgreSQL kazandı. MT-PG-031/032'nin tam tersi davranış — `Replace` önceki kaydı
bilerek eziyor (K-025). Tüketicinin kaydı çözülseydi
`🚨 TUKETICININ kaydi kazandi` basılacaktı; basılmadı.

📝 **Spec düzeltmesi.** `Girilecek veri` bloğu `SahteRunStore` tipini kullanıyordu
ama **hiçbir yerde tanımlamıyordu** — kod olduğu gibi derlenmez. `IRunStore` 14
üye taşır; hepsini elle yazmak case'in ölçtüğü şeyi değiştirmeyeceği için
tüketicinin kaydı bir **fabrika** olarak yazıldı
(`TryAddSingleton<IRunStore>(_ => throw ...)`). Ayrıca MT-PG-031'deki
`ITraconBuilder` düzeltmesi burada da gerekliydi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-034 — Aynı zincirde iki kalıcılık sağlayıcısı kayıtlıysa uyarı loglanır

**Gerçek sonuç**
Üç iddia da tuttu.

```
warn: Tracon.MigrationHostedService[0]
      Tracon has more than one persistence provider registered: PostgreSQL, SQLite.
      The last registration wins and SQLite is currently in use. Call only one.
```

| İddia | Ölçüm |
|---|---|
| Uyarı loglanır | ☑ (iki kez — her sağlayıcının hosted service'i bir kez basıyor) |
| `registeredPersistenceProviders` | ☑ **2** |
| `persistenceProvider` | ☑ **`SQLite`** — son çağrılan kazandı |
| `/health` | ☑ **200 `Degraded`** |

Uyarı yalnız "iki tane var" demiyor; **hangisinin kullanımda olduğunu** ve ne
yapılacağını da söylüyor.

🚨 **Kod donuk olduğu için repo dışında koşuldu.** Case
`samples/Tracon.Api/Program.cs`'e geçici satır eklemeyi istiyor; tur boyunca
`samples/` değişmez (kural 1). Dosya 02'nin kurduğu desen izlendi: repo dışında
`~/tracon-manuel/ikili-saglayici` (port 5086), yayınlanmış paketlerle
(`Tracon.AspNetCore` · `Tracon.PostgreSql` · `Tracon.Sqlite` 0.0.0-preview.0.789)
kurulu bir tüketici host'u. Sıra `SIRA` ortam değişkeniyle seçiliyor, böylece
MT-PG-035 kod değiştirmeden koşulabiliyor. `git status` repo'da boş kaldı.

⚠️ Host minimal olduğu için `/health`'i kendi haritalaması gerekti
(`AddHealthChecks().AddTraconHealthChecks()` + `MapHealthChecks("/health")`,
örnek uygulamanın `Program.cs:904,913` satırlarının aynısı). İlk denemede 404
alındı; bu harness eksiğiydi, ürün kusuru değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-035 — Sağlayıcı çağrı sırası değişirse kazanan değişir

**Gerçek sonuç**
Sıra tersine çevrildi (`UseSqlite` → `UsePostgreSql`) ve kazanan değişti:

```
warn: Tracon has more than one persistence provider registered: SQLite, PostgreSQL.
      The last registration wins and PostgreSQL is currently in use. Call only one.
```

| Ölçüm | MT-PG-034 (pg önce) | MT-PG-035 (sqlite önce) |
|---|---|---|
| `persistenceProvider` | `SQLite` | **`PostgreSQL`** |
| `registeredPersistenceProviders` | 2 | **2** |

Uyarı metninin kendisi de sırayı yansıtıyor (`SQLite, PostgreSQL`) ve kullanımdaki
sağlayıcıyı doğru adlandırıyor. K-025 `Replace` deseni doğrulandı: **son çağrı
kazanır**, kayıt sırası tek belirleyici.

Aynı repo dışı host kullanıldı; sıra `SIRA=sqlite-once` ortam değişkeniyle
seçildi, hiçbir kaynak dosyası değiştirilmedi. Temizlik: host durduruldu,
`manuel-test-ikinci.db*` silindi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-040 — `pgvector` eklentisi ve HNSW indeksi migration sonrası kuruludur

**Gerçek sonuç**
Üçü de tuttu.

```
extname | extversion
vector  | 0.8.6
```

`mt_s1.document_embeddings` indeksleri — beklenen dördü de var, artı iki kısıt
indeksi:

| indeks | beklenen mi |
|---|---|
| `document_embeddings_hnsw_idx` | ☑ |
| `document_embeddings_tenant_collection_idx` | ☑ |
| `document_embeddings_tenant_collection_source_idx` | ☑ |
| `document_embeddings_tenant_created_idx` | ☑ |
| `document_embeddings_pkey` | birincil anahtar (beklenti "artı ... kısıtı" der) |
| `document_embeddings_uq` | benzersizlik kısıtı (aynı şekilde) |

HNSW tanımı her iki ifadeyi de taşıyor:

```sql
CREATE INDEX document_embeddings_hnsw_idx ON mt_s1.document_embeddings
  USING hnsw (embedding vector_cosine_ops)
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-041 — Embedding uzunluğu depo boyutuyla eşleşmezse `UpsertAsync` reddedilir

**Gerçek sonuç**
```
Dimensions: 1536
beklenen istisna: Chunk 0 embedding length (10) does not match the store dimensions (1536). (Parameter 'chunks')
```

Üç iddia da tuttu: `Dimensions` **1536**, istisna mesajı hem **10** hem **1536**
sayısını taşıyor ve hangi parçanın suçlu olduğunu da söylüyor (`Chunk 0`),
`🚨 istisna ATILMADI` satırı **görünmedi**.

🚨 **Spec düzeltmesi — bu dosyanın altı vektör case'ini birden etkiliyordu.**
Kod `UsePostgreSql(<baglanti dizesi>)` çağırıyordu ve ilk koşumda şu hatayla
düştü:

```
System.InvalidOperationException: No service for type 'Tracon.IVectorSearchStore' has been registered.
```

Sebep: `TraconPostgreSqlBuilderExtensions.cs:386-393` `IVectorSearchStore`'u
`TryAddSingleton` ile kaydediyor **ama fabrikası `EnableKnowledge` kapalıyken
`null!` dönüyor** (Faz 67, kasıtlı: "not available" görünsün ki agent derlemesi
`document_embeddings does not exist` yerine açık bir hata versin). Örnek uygulama
`EnableKnowledge: true` taşıdığı için İzlek B'de bu görünmüyor; İzlek A'da
tüketicinin kendisi açmalı. Altı case'in (`041 · 042 · 043 · 044 · 045 · 046`)
kod bloğuna `EnableKnowledge = true` eklendi.

**MT-PG-032 bundan etkilenmez ve geçerli kalır** — descriptor `EnableKnowledge`
ne olursa olsun kaydedildiği için tüketicinin önceki kaydı yine de onu bloklar;
ölçüm zaten `SahteVektorStore` döndürmüştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-042 — Aynı kaynak yeniden yazılırsa eski parçalar silinir (upsert-üzerine-yazma)

**Gerçek sonuç**
```
arama sonuc sayisi (ikinci yazimdan sonra): 1
  manuel-kaynak parca=0 icerik='ikinci surum, tek parca' mesafe=0,0000
```

Veritabanı doğrulaması aynı şeyi söylüyor — `mt_s1.document_embeddings` bu
kiracı/koleksiyon için **tek satır** taşıyor:

```
 source_id     | chunk_index | content
 manuel-kaynak |           0 | ikinci surum, tek parca
```

İlk yazımın iki parçası (`ilk surum, parca 0` · `parca 1`) **silinmiş**;
`ilk surum` metni ne aramada ne tabloda görünüyor. Sorgu vektörü tam eşleştiği
için mesafe `0,0000`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-043 — Arama kosinüs mesafesine göre artan sıralı döner

**Gerçek sonuç**
```
eksen-0 mesafe=0,0000
eksen-1 mesafe=1,0000
eksen-2 mesafe=1,0000
```

Cebirsel beklenti birebir tuttu: sorgu vektörüyle aynı yöndeki `eksen-0` mesafe
**0**'da ve **ilk** sırada; ona dik olan iki vektör mesafe **1**'de ve
**sonrasında**. Liste artan mesafe sırasında.

⚠️ Ondalık ayırıcı virgül (`0,0000`) — koşan makinenin kültürü `tr-TR`. Bu
`Console.WriteLine` biçimlendirmesidir, depodan gelen değer değil; beklentiyi
etkilemez.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-044 — İki kiracı aynı koleksiyon/kaynak kimliğini paylaşsa da birbirini görmez

**Gerçek sonuç**
```
kiraci-beta 1 sonuc goruyor:
  BETA'nin gizli belgesi
```

`kiraci-beta` **tek** sonuç gördü ve o kendi belgesiydi; `ALFA'nin gizli belgesi`
metni hiç görünmedi — üstelik iki kayıt **aynı** koleksiyon ve **aynı** kaynak
kimliğini paylaşıyor.

Yalıtımın nasıl uygulandığı da doğrulandı: tabloda **iki satır da duruyor**,
yani birincil anahtar veya benzersizlik kısıtı ikinci yazımı reddetmedi —
ayrım okuma anında `WHERE tenant_id` ile yapılıyor:

```
 tenant_id   | source_id    | content
 kiraci-alfa | ortak-kaynak | ALFA'nin gizli belgesi
 kiraci-beta | ortak-kaynak | BETA'nin gizli belgesi
```

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-045 — Kaynak silindiğinde tüm parçaları kaybolur

**Gerçek sonuç**
```
silinen parca sayisi: 3
kalan kaynak sayisi: 0
```

`DeleteSourceAsync` üç parçanın üçünü de sildi ve sayıyı doğru raporladı.
Veritabanı doğrulaması: `collection='silme-testi'` için **0** satır kaldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-046 — `MaxDistance` filtresi uzak sonuçları eler

**Gerçek sonuç**
```
filtresiz: 2
filtreli (<=0.5): 1
  kalan: yakin mesafe=0,0000
```

Filtresiz arama iki kaydı da getirdi; `MaxDistance = 0.5` ile yalnız `yakin`
(mesafe **0,0000**) kaldı, dik olan `uzak` (mesafe 1,0) elendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-047 — Koleksiyon adı geçersiz karakter taşıyorsa HTTP ucu 400 döner

**Gerçek sonuç**
İki istek de **400** döndü ve mesajları **farklı** — iki doğrulama bağımsız
çalışıyor.

```
1) POST /api/knowledge/gecersiz%2Fkoleksiyon/documents   -> HTTP 400
   "'gecersiz%2Fkoleksiyon' is not a valid collection name.
    It may only contain letters, digits, underscores, and hyphens."

2) POST /api/knowledge/gecerli-koleksiyon_1/documents    -> HTTP 400
   "Chunk 0 embedding length (1) does not match the store dimension (1536)."
```

Birinci mesaj reddedilen adı ve kuralı birlikte veriyor. İkincisi ad
doğrulamasını geçip boyut doğrulamasına takılıyor, yani ikisi ayrı kapılar.

⚠️ Birinci mesaj adı **URL-kodlu haliyle** (`gecersiz%2Fkoleksiyon`) yazıyor,
çözülmüş haliyle (`gecersiz/koleksiyon`) değil. Ad yine de geçersiz olduğu için
sonuç doğru; kullanıcıya gösterilen metin bir tık ham. Kusur sayılmadı.

📝 **Spec düzeltmesi.** Beklenen mesaj Türkçe yazılmıştı (`gomu uzunlugu`); sevk
edilen metin İngilizce'dir (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-050 — `/health` PostgreSQL erişilemezken Unhealthy döner

**Gerçek sonuç**

**Sapma — container durdurulmadı** (şerit kuralı 3 · kullanıcı kararı
2026-09-16). Erişilemezlik, şerit-yerel bir TCP yönlendiriciyle taklit edildi:
uygulama `Host=localhost;Port=55481` ile açıldı, yönlendirici `55481 → 55432`
(`ap-pg`) aktarıyor. Yönlendiriciyi öldürmek uygulamanın gözünde veritabanını
**tam olarak** `docker stop` kadar erişilemez yapar; `ap-pg` ise hiç dokunulmadan
`Up 4 hours` kaldı. Yöntem şerit-yereldir, Faz B'de dört şerit paralel koşarken
de güvenlidir. Aparat: `<scratch>/pgproxy.py`.

Taban çizgisi (yönlendirici ayakta): `canConnect:true`, `migrationsUpToDate:true`,
`/health` → **200 `Degraded`**.

Adım 1–2 — ağ kesik:

```
=== yonlendirici olduruldu — ap-pg container'ina DOKUNULMADI ===
ap-pg	Up 4 hours
/health -> Unhealthy      HTTP: 503
diagnostics -> {'persistenceProvider': 'PostgreSQL',
                'canConnect': False, 'migrationsUpToDate': False}
```

Adım 3–4 — ağ geri, **uygulama yeniden başlatılmadı**:

```
/health -> Degraded       HTTP: 200
diagnostics -> {'persistenceProvider': 'PostgreSQL',
                'canConnect': True, 'migrationsUpToDate': True}
```

Npgsql havuzu kendiliğinden toparlandı; süreç yeniden başlatılmadı.

📝 **Spec düzeltmesi — 2026-08-15 düzeltmesi fazla kapsıyordu.**
O düzeltme case'in beklentisinin **tamamını** üstü çizili bıraktı ve case'i bu
dosyanın kapsamı dışına attı. Ölçüm bunun yarısının yanlış olduğunu gösteriyor:

`TraconHealthCheck.cs:46-49`'da `CanConnect` **ilk** kontroldür ve model
sağlayıcı mantığından **önce** kısa devre yapar:

```csharp
if (!report.CanConnect)
{
    return HealthCheckResult.Unhealthy("The persistence database is unreachable.", data: data);
}
```

∴ "PostgreSQL erişilemezken `/health` Unhealthy döner" iddiası — case'in
**başlığı** ve kalıcılık kapsamının tam merkezi — `echo` öncülünden tamamen
bağımsızdır ve **doğrulandı** (503). Yalnız **kurtarma yarısı** (adım 3–4'ün
düz `Healthy` beklentisi) `echo` gerekçesinden etkilenir.

Üstelik bu şeritte kurulum `echo`-only **değildir**: gerçek OpenAI · Anthropic ·
Google anahtarları kayıtlı (`configuration[].resolved: true`). Buna rağmen
`Healthy` gelmiyor, ama 2026-08-15'in yazdığı nedenden değil — beş sağlayıcının
beşi de `status:"Unknown"` duruyor, çünkü sağlayıcı sağlığı **ancak gerçek bir
çağrıdan sonra** onaylanır. `TraconHealthCheck.cs:73-80` son dalı buna düşürür:

```csharp
: HealthCheckResult.Degraded("No model provider has been confirmed healthy yet.", data: data);
```

Yani `Degraded` sonucu doğru, gerekçesi yanlış kaydedilmişti.

⚠️ Eski beklentinin ikinci yarısı da koda göre yanlıştı: "gövde ... bir mesaj
taşır" diyordu. `samples/Tracon.Api/Program.cs:913` `app.MapHealthChecks("/health")`
çağrısını **ResponseWriter'sız** yapar; ASP.NET'in varsayılan yazıcısı yalnız
durum metnini (`Unhealthy`) yazar, `description` alanını hiç yazmaz. Mesajın
makine-okunur karşılığı `/tracon/api/diagnostics` içindeki `canConnect:false`
alanıdır. Ayrıca metin İngilizce'dir (K-228), spec Türkçe yazmıştı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-051 — `/tracon/api/diagnostics` bekleyen migration'ları listeler, hiçbir `secret` taşımaz

**Gerçek sonuç**
`mt_s1` düşürüldü (49 nesne), `Tracon__PostgreSql__AutoApplyMigrations=false`
ile açıldı (ortam değişkeni — `user-secrets` yazılmadı, skill §1.2).

```
/health -> HTTP 503
migrationsUpToDate: False
canConnect: True
pendingMigrations sayisi: 51
ilk: 0001_initial
son: knowledge:0001_vector
```

`canConnect:true` ama `migrationsUpToDate:false` — `TraconHealthCheck.cs:51-56`
ikinci dalı, 503'ün nedeni bekleyen migration, erişim değil.

**`secret` taraması temiz** (K-059). Gövde 2857 bayt; yasaklı alt dizgilerin
hepsi **0**:

```
Password= -> 0    password -> 0    Host= -> 0     Username= -> 0
tracon;   -> 0    55432    -> 0    sk-   -> 0     manuel-test-token -> 0
ApiKey    -> 3
```

`ApiKey`'in üç geçişi de yalnız **anahtarın adıdır**, değeri değil — K-059'un
öngördüğü biçim:

```json
{"key": "Tracon:Providers:OpenAI:ApiKey", "resolved": true, "hint": null}
```

Ortamdaki gerçek OpenAI anahtarı gövdede birebir arandı — **geçmiyor**.

📝 **Spec düzeltmesi.** Beklenen sayı **28** (`0001_initial` → `0028_experiment_canary`)
yazıyordu; bugün **51**'dir (50 çekirdek + 1 knowledge) ve son öğe
`knowledge:0001_vector`'dür. Aynı bayatlık `020-026` blokunda oturum 8'de
düzeltilmişti; `051` ile `052` atlanmış. Sabit sayıya değil **yapıya** bakan
doğrulama notu eklendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-052 — Teşhis ucu migration UYGULAMAZ (salt okunur)

**Gerçek sonuç**
Üç çağrının üçü de aynı sayıyı verdi — değişmedi:

```
51
51
51
```

Şemaya hiçbir şey yazılmadı:

```
to_regclass('mt_s1.__migrations') -> NULL (bos)
information_schema.schemata  mt_s1 -> 0
information_schema.tables    mt_s1 -> 0
```

Spec yalnız `__migrations` tablosunun yokluğunu istiyordu; ölçüm bundan
**daha güçlü** çıktı — `mt_s1` şemasının kendisi hiç açılmadı. Teşhis ucu
`DROP SCHEMA`'dan sonraki durumu okuyor ve hiçbir DDL çalıştırmıyor.

📝 **Spec düzeltmesi.** MT-PG-051 ile aynı bayatlık: beklenen sayı **28**
yazıyordu → **51**. Ön koşul satırı da ("28 bekleyen migration") güncellendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-053 — Migration tamamsa ve bir model sağlayıcı sağlıklıysa `/health` Healthy döner

**Gerçek sonuç**
Reset uygulandı, `AutoApplyMigrations` varsayılana (`true`) döndü, uygulama
normal başlatıldı. **51 migration otomatik uygulandı**, `migrationsUpToDate:true`,
`canConnect:true`.

İlk ölçüm — `/health` → **200 `Degraded`**, beş sağlayıcının beşi `Unknown`:

```
[('openai','Unknown'), ('openai-responses','Unknown'), ('openrouter','Unknown'),
 ('anthropic','Unknown'), ('google','Unknown')]
```

**Gerçek bir OpenAI çağrısı bunu değiştirmedi.** `POST /api/agents/support/run`
(`gpt-5.4-mini`) başarıyla tamamlandı — gerçek `chatcmpl-EOoIjX95...` kimliği,
token sayımı, `"finishReason": "stop"` — ama sağlayıcı durumu `Unknown` kaldı ve
`/health` `Degraded` kaldı.

`/api/models/health` çağrıldıktan **sonra** ise:

```
/api/models/health -> [('anthropic','Healthy'), ('google','Healthy'),
                       ('openai','Healthy'), ('openai-responses','Healthy'),
                       ('openrouter','Healthy')]
diagnostics        -> hepsi Healthy
/health            -> Healthy        HTTP: 200
```

∴ Case'in iddiası **doğrulandı**: migration tam + bir sağlayıcı sağlıklı ⇒
`/health` `Healthy`.

**Mekanizma** (kaynak okundu): `TraconDiagnosticsCollector.cs:144` sağlayıcı
durumunu yalnız `_healthCache.TryPeek` ile **okur**, hiçbir prob tetiklemez —
sınıf dokümanı bunu açıkça yazıyor (`TraconDiagnosticsCollector.cs:8-14`,
"Has no side effects"). Önbelleği dolduran tek yol
`ModelProviderHealthCache.GetAllAsync/GetAsync`'tir, yani `/api/models/health`
ucudur. Sohbet çağrısı bu önbelleğe **yazmaz**; yalnız devre kesici katmanı
canlı okunur (`ModelProviderHealthCache.cs:158-171`).

📝 **Spec düzeltmesi — 2026-08-15 düzeltmesi merkez iddiasında yanlış.**
O düzeltme "`/health` YAPISAL OLARAK asla düz `Healthy` dönemez" diyordu.
Ölçüm bunu **çürüttü**: `Healthy` alındı. Doğru olan kısım yalnız `echo`
öncülüdür — `echo` izlenen `ModelProviders` listesinde yer almadığı için
`echo`-only bir kurulum gerçekten `Healthy` olamaz. Ama bu "yapısal
imkânsızlık" değil, **ön koşul hatasıdır**: case'in "(`echo` yeterli)" ön
koşulu yanlıştı, sonucu değil. Ön koşul izlenen bir sağlayıcı isteyecek
biçimde düzeltildi ve önbellek ısıtma adımı eklendi.

🚨 **`HATA-S1-016` — `/health` kendi başına hiçbir zaman `Healthy`'ye ulaşmaz.**
Ayrıntı aşağıdaki hata kaydında.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

### HATA-S1-016 — `/health` kendi başına hiçbir zaman `Healthy`'ye ulaşmaz

| | |
|---|---|
| **Case** | MT-PG-053 (case **geçti**; bulgu case'in kenarından çıktı) |
| **Önem** | Düşük–Orta |
| **Sınıf** | teşhis edilebilirlik — sağlıklı kurulum kendini `Degraded` raporluyor |

**Belirti.** Her şeyi doğru kurulmuş bir uygulama — PostgreSQL erişilebilir, 51
migration uygulanmış, üç gerçek sağlayıcı anahtarı çözülmüş — `/health` ucunda
süresiz **`Degraded`** raporlar:

```
/health -> Degraded   HTTP 200
"No model provider has been confirmed healthy yet."
```

Gerçek bir sohbet çağrısı bunu **değiştirmez**. `POST /api/agents/support/run`
başarıyla tamamlandıktan sonra bile beş sağlayıcının beşi `Unknown` kalır.
Durum yalnız `/api/models/health` çağrıldıktan sonra `Healthy`'ye döner.

**Kök neden.** İki tasarım kararının kesişimi:

1. `TraconDiagnosticsCollector.cs:144` sağlayıcı durumunu yalnız `TryPeek` ile
   okur ve prob tetiklemez. Bu **bilinçlidir** ve dokümante edilmiştir
   (`TraconDiagnosticsCollector.cs:8-14`, "Has no side effects") — sağlık ucunun
   her çağrıda sağlayıcıya ağ isteği atmaması doğru bir karardır.
2. `ModelProviderHealthCache` yalnız `GetAllAsync`/`GetAsync` ile dolar
   (`ModelProviderHealthCache.cs:133-152`), yani yalnız `/api/models/health`
   ucuyla. Sohbet yolu bu önbelleğe yazmaz.

∴ Hiç kimse `/api/models/health`'i çağırmazsa önbellek sonsuza dek boş kalır ve
`TraconHealthCheck.cs:73-80` son dalı hep `Degraded` döndürür.

**Tüketici etkisi.** Bir orchestrator (Kubernetes, ECS) `/health`'i yoklar.
`Degraded` HTTP **200** döndüğü için readiness kapısı düşmez — bu yüzden önem
`Kritik` değildir. Ama gösterge kalıcı olarak yanlıştır: sağlıklı bir kurulum
hiçbir zaman yeşil görünmez ve operatör gerçek bir bozulmayı gürültüden ayıramaz.

**Kapsam dışı olan.** Sohbet çağrısının önbelleğe yazması **istenmeyebilir** —
devre kesici katmanı zaten canlı okunuyor ve başarılı bir çağrı tek bir modeli
kanıtlar, sağlayıcının tamamını değil.

**Öneri (kapanışta değerlendirilecek).** Üç seçenek var ve seçim bir yetenek
kararıdır, kusur düzeltmesi değil:

- Başlangıçta bir kez arka planda sağlayıcı probu koş (soğuk başlangıç maliyeti).
- `TraconHealthCheck` önbellek boşken `Degraded` yerine ayrı bir durum/mesaj
  taşısın — "not yet probed" ile "probed and unhealthy" bugün ayırt edilemiyor.
- Davranışı olduğu gibi bırak ve `docs-site/`'ta belgele: `/health`'in
  `Healthy` raporlaması için `/api/models/health`'in periyodik çağrılması gerekir.

🚨 Üçünün de yeni davranış istediğine dikkat: bu bulgu muhtemelen `ADAYLAR.md`'ye
F-NN olarak girer, Aşama 2'de kodlanacak bir kusur olarak değil. Karar Aşama
2'de verilir (skill §6).

## MT-PG-060 — 20 eşzamanlı yazma isteği veri bozulmadan tamamlanır

**Gerçek sonuç**
20 isteğin 20'si de **201** döndü, veritabanında **20** satır var:

```
=== HTTP kodlari (sayim) ===
  20 201
=== veritabani ===
 kayitli
      20
=== benzersizlik ===
 tekil_ad | satir
       20 |    20
```

Kayıp yok, çift kayıt yok — 20 ad da tekil. Uygulama istekler boyunca ayakta
kaldı (`/health` → 200).

**Havuz tükenmesi izi yok.** `TimeoutException|exhaust|connection pool` taraması
5 satır getirdi, hepsi **migration SQL'inin yorum satırları** (`-- not depend on
the connection pool...`), çalışma anı hatası değil. Gerçek bir
`Npgsql...TimeoutException` kaydı yok.

📝 **Sapma — `provider:"echo"` kullanılamadı.** Spec `echo` yazıyor; gerçek
sağlayıcı anahtarları kayıtlıyken `echo` kayıtlanmaz ve `400 Tanım geçersiz`
verir (dosya 02'den taşınan ortam kuralı). `openai`/`gpt-5.4-mini` kullanıldı.
Case yazma yolunu ölçüyor, model çağrısı yapılmıyor — ölçülen davranış değişmez.

📝 **Sapma — şema `mt_s1`.** Spec `tracon.agent_definitions` yazar; şerit
izolasyonu gereği `mt_s1.agent_definitions` sorgulandı (skill §1.3).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-061 — PostgreSQL koşum sırasında durursa çalışan bir istek anlaşılır hatayla başarısız olur, uygulama çökmez

**Gerçek sonuç**

**Sapma — container durdurulmadı** (MT-PG-050 ile aynı yöntem): uygulama
`Port=55481` üzerinden şerit-yerel yönlendiriciyle bağlandı, yönlendirici
öldürülüp geri getirildi. `ap-pg` boyunca `Up 4 hours` kaldı.

Taban çizgisi — ağ açıkken aynı istek **200**.

Adım 1–2 — ağ kesik:

```
ap-pg	Up 4 hours          <- container'a DOKUNULMADI
run HTTP: 500
{"title":"An error occurred while processing your request.","status":500,...}
surec ayakta: EVET
/health HTTP: 503
```

Adım 3–4 — ağ geri, **uygulama yeniden başlatılmadı**:

```
run HTTP: 200
/health HTTP: 200
pgrep -f "Tracon.Api --urls" -> 71876   (kesintiden ÖNCEKİ ile aynı PID)
```

PID değişmedi — süreç hiç yeniden başlatılmadı, Npgsql havuzu kendiliğinden
yeniden bağlandı. Beklenen sonucun iki maddesi de doğrulandı.

⚠️ **Case başlığının "anlaşılır hata" iddiası karşılanmıyor.** Yanıt
`traceId` dışında hiçbir şey söylemeyen opak bir `500`; ne veritabanı
erişilemezliğinden ne de geçici olduğundan söz ediyor. İstemci bunu kalıcı bir
uygulama hatasından ayıramaz ve yeniden denenebilir olduğunu anlayamaz.

Bu **yeni bir bulgu değildir** — `HATA-S1-015` ile aynı kök neden ve aynı sınıf:
`src/Tracon.AspNetCore/` altında `DbException`/`PostgresException` için özel
eşleme yok, istisna genel ASP.NET Core işleyicisine kadar çıkıyor. O kaydın
"sınıf taraması notu" bu vakayı zaten öngörüyordu ("aynı desen tüm SQL depo
yazmalarını etkiler"). Bu case onun **ikinci ampirik örneğidir**: 025 yazma
yolunu, 061 okuma/çalıştırma yolunu gösteriyor. `HATA-S1-015` kaydına eklendi.

Beklenen sonucun yazılı iki maddesi karşılandığı için durum **Geçti**;
opaklık ayrı bir kayıtta izleniyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-062 — `pgvector` kurulu olmayan PostgreSQL'de knowledge kapalıyken uygulama sorunsuz açılır

**Gerçek sonuç**
Şerit-yerel düz konteyner açıldı: `ap-pg-plain-s1` (`postgres:18-alpine`, port
**55433**). Paylaşılan `ap-pg`'ye dokunulmadı. Ön koşul doğrulandı — `vector`
uzantısı yalnız kurulu değil, **hiç kullanılabilir değil**:

```
pg_available_extensions WHERE name='vector' -> 0
```

Uygulama `EnableKnowledge=false` ve `SchemaName=tracon_case1_s1` ile açıldı.

```
=== uygulanan migration ===
 set_name | count
 core     |    50        <- knowledge seti YOK

=== diagnostics ===
{'canConnect': True, 'migrationsUpToDate': True, 'pendingMigrations': []}

=== \dx ===
 plpgsql | 1.0 | pg_catalog | PL/pgSQL procedural language
(1 row)                       <- vector YOK

=== /api/tools ===
tool sayisi: 10
['cancel_order', 'estimate_shipping_cost', 'get_order_status', 'get_slow_report',
 'list_recent_orders', 'list_voices', 'mark_preview_ready', 'read_shopping_cart',
 'speak', 'transcribe']
search_knowledge var mi: False
```

Dört beklentinin dördü de karşılandı — K1 tutuyor: knowledge kapalıyken tool
**hiç kayıtlanmıyor**, gizlenmiyor.

📝 **Spec düzeltmesi — iki bayat sayı.** Beklenen "`Tracon 32 migration uyguladi.`"
yazıyordu → bugün **50** çekirdek migration. 2026-08-19 kanıt bloğundaki "32
satır" da aynı şekilde bayat; kanıt bloğu **tarihiyle birlikte** korundu (o
tarihte doğruydu), beklenen sonuç yapıya bakacak biçimde düzeltildi.
Ayrıca 2026-08-19 tool listesi 7 tool sayıyordu → bugün **10**
(`estimate_shipping_cost` · `get_slow_report` · `mark_preview_ready` eklenmiş).
Bu, `00-INDEKS.md` §3.2'nin bilinen bayatlığının aynı kökü.

🚨 **`HATA-S1-017` — taze şemada açılış logu `Error` seviyesinde yığın izi
basıyor.** Beklenen sonucun "hiçbir hata yoktur" maddesi **harfiyen**
karşılanmıyor. Ayrıntı aşağıdaki hata kaydında. Case'in ölçtüğü dört yapısal
iddia karşılandığı için durum **Geçti**; log gürültüsü ayrı izleniyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

### HATA-S1-017 — Taze şemada her açılış `Error` seviyesinde yığın izi basar

| | |
|---|---|
| **Case** | MT-PG-062 (ayrıca MT-PG-053 · 051 — her taze açılışta) |
| **Önem** | Düşük |
| **Sınıf** | teşhis edilebilirlik — beklenen bir yol hata gibi loglanıyor |

**Belirti.** Boş bir şemaya karşı açılan her uygulamada, **ilk** veritabanı
sorgusu başarısız olur ve tam yığın iziyle `fail:` seviyesinde loglanır:

```
fail: Tracon.CompositeAgentCatalog[0]
      Agent source 'database' failed during list.
      Npgsql.PostgresException (0x80004005): 42P01:
        relation "tracon_case1_s1.agent_definitions" does not exist
         at Tracon.SqlAgentDefinitionStore.ListAsync(...) SqlAgentDefinitionStore.cs:line 99
         at Tracon.DefinitionStoreAgentSource.ListAsync(...) DefinitionStoreAgentSource.cs:line 52
         at Tracon.CompositeAgentCatalog.ListAsync(...) CompositeAgentCatalog.cs:line 56
```

Sıra ölçüldü: bu kayıt logun **7. satırıdır**; migration çalıştırıcısının
`pg_advisory_lock` → `CREATE SCHEMA` adımları **daha sonra** gelir (satır 39+).
`mt_s1` şemasının taze açılışında da birebir aynı (aynı satır numarası).

**Kök neden.** `TraconA2AExtensions.cs:94` uç noktaları kurarken kataloğu
**senkron** listeler:

```csharp
var descriptors = catalog.ListAsync().AsTask().GetAwaiter().GetResult();
```

Bu, uygulama **kurulum** anındadır; `IHostedService` sırası — dolayısıyla
`MigrationHostedService` — henüz koşmamıştır.

**Bu bilinçlidir ve işlevsel olarak doğrudur.** Hemen üstündeki yorum
(`TraconA2AExtensions.cs:85-90`) durumu birebir öngörüyor ("no such table
against an empty database"), geniş `catch` **kasıtlıdır** ve geri düşüş
güvenlidir — agent kartı yalnız `Description`/`Version` ile süslenir, güvenlik
kontrolü yoktur.

**Kusur olan kısım: yutma bir katman geç yapılıyor.** İstisnayı A2A yakalayıp
yutuyor, ama `CompositeAgentCatalog.cs:56-65` onu **daha önce**
`RecordSourceFailure(..., LogLevel.Error)` ile loglamış oluyor. Yani "beklenen"
olduğu bilinen bir yol, operatörün konsoluna bozuk kurulum görüntüsü veriyor.
Tracon bir kütüphanedir; tüketicinin ilk çalıştırma deneyimi budur.

**Öneri (kapanışta değerlendirilecek).** `CompositeAgentCatalog.ListAsync`
çağıranın beklenen-hata toleransını bilmiyor; seçenekler:

- A2A kurulum yolu `SchemaReadyGate.IsReady` **`false`** iken kataloğu hiç
  listelemesin — süslemeyi atlayıp `agentName`'e düşsün. En küçük değişiklik.
- `CompositeAgentCatalog.ListAsync` bir "sessiz dene" aşırı yüklemesi alsın;
  kurulum yolu onu çağırsın.

**Sınıf taraması notu.** Aynı desen kurulum anında depoya giden **her** yolu
etkiler. `McpDiscoveryService.cs:72` ve `A2AApprovalGuardFilter` bunu doğru
yapıyor — `SchemaReadyGate.WaitAsync` bekliyorlar. Kurulum anındaki senkron
liste bu korumanın **dışında** kalan tek yol olabilir; kapanışta
`GetAwaiter().GetResult()` çağrıları taranmalı.

## MT-PG-063 — Aynı ortamda `EnableKnowledge = true` açık ve okunur bir başlangıç hatası verir

**Gerçek sonuç**
Aynı konteyner (`ap-pg-plain-s1`, `pgvector` yok), `SchemaName=tracon_case2_s1`,
`EnableKnowledge=true`.

**1. Uygulama başlamayı reddetti.** Çıkış kodu **134** (SIGABRT — işlenmemiş
istisna). `Now listening` sayısı **0**: süreç HTTP dinlemeye hiç başlamadı,
∴ hiçbir kullanıcı isteği işlenmedi.

**2. Mesaj spec'te yazdığı gibi, birebir.** İşlenmemiş istisna bloğunun **ilk**
satırı:

```
Unhandled exception. Tracon.TraconException: Migration '0001_vector' could not be applied: extension "vector" is not available (SQLSTATE 0A000).
 ---> Npgsql.PostgresException (0x80004005): 0A000: extension "vector" is not available
```

Ayrıntılı .NET yığın izi yalnız konsolda; hiçbir HTTP istemcisine ulaşmadı
(K-354 "hata yutulmaz" ilkesiyle tutarlı).

**3. Çekirdek migration'lar geri ALINMADI:**

```
 set_name | count
 core     |    50        <- hepsi uygulanmis ve KALICI
to_regclass('tracon_case2_s1.document_embeddings') -> NULL
```

`knowledge` seti **hiç satır yazmadı** ve `document_embeddings` tablosu
oluşmadı; yalnız `0001_vector` düştü. Set izolasyonu tutuyor (K-475/K-476).

📝 **Spec düzeltmesi.** Beklenen ve 2026-08-19 kanıt bloğu "çekirdek 32'si"
diyor → bugün **50**. Beklenen sonuç yapıya bakacak biçimde düzeltildi; kanıt
bloğu tarihiyle korundu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-064 — `pgvector` kurulu PostgreSQL'de `EnableKnowledge = true` gerçek bir belge yükleme + arama turu tamamlar

**Gerçek sonuç**
Paylaşılan `ap-pg` (`pgvector/pgvector:pg18`), şema `mt_s1`,
`EnableKnowledge=true`. Gerçek OpenAI gömü çağrısı yapıldı.

```
to_regclass('mt_s1.document_embeddings') -> mt_s1.document_embeddings

1) POST /api/knowledge/faz67-test/documents   -> HTTP 200
   {"sourceId":"doc-1","chunkCount":1}

2) POST /api/knowledge/faz67-test/search      -> HTTP 200
   [{"sourceId":"doc-1","chunkIndex":0,
     "content":"Tracon Faz 67 makes the PostgreSQL migration sets optional.",
     "distance":0.6148895159019339,"metadata":null}]
```

Arama yüklenen içeriği **birebir** döndürdü ve `distance` alanı taşıyor.
Veritabanı doğrulaması gömünün gerçek olduğunu gösteriyor:

```
 source_id | chunk_index | icerik                                   | boyut
 doc-1     |           0 | Tracon Faz 67 makes the PostgreSQL migr.. |  1536
```

`vector_dims = 1536` — sahte/sıfır vektör değil, gerçek OpenAI gömüsü.
Üç beklentinin üçü de karşılandı.

📝 **Sapma — belge metni İngilizce yazıldı.** Spec Türkçe bir cümle taşıyor.
Sevk edilen yüzey ve gömü modeli için dil farkı ölçülen davranışı değiştirmez;
İngilizce metin K-228 ile de tutarlıdır. Sorgu da İngilizce sorularak anlamsal
eşleşme korundu.

📝 **Sapma — koleksiyon `ap-pg`/`mt_s1` üzerinde.** Spec `ap-pg` diyor, şerit
şeması `mt_s1` (skill §1.3).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-065 — Case 062'nin veritabanı sonradan `EnableKnowledge = true` ile yeniden başlatılınca yalnız knowledge seti uygulanır

**Gerçek sonuç**
Spec'in izin verdiği ikinci yol seçildi: senaryo doğrudan `ap-pg` üzerinde,
ayrı bir şemada (`mt_s1_k`) tekrarlandı. `tracon_case1_s1` düz konteynerde
duruyor ve orada `pgvector` yok; şema taşımak yerine aynı ön koşul `ap-pg`'de
sıfırdan kuruldu.

**1. aşama** — `EnableKnowledge=false`, taze şema:

```
 set_name | count
 core     |    50
to_regclass('mt_s1_k.document_embeddings') -> NULL
```

**2. aşama** — aynı şema, `EnableKnowledge=true` ile yeniden başlatıldı:

```
 set_name  | count
 core      |    50        <- DEGISMEDI
 knowledge |     1        <- yalnizca bu eklendi
 toplam    |    51

CREATE EXTENSION IF NOT EXISTS vector           -> bu acilista 1 kez
CREATE TABLE IF NOT EXISTS mt_s1_k.tenants      -> bu acilista 0 kez
to_regclass('mt_s1_k.document_embeddings') -> mt_s1_k.document_embeddings
```

İkinci açılış logunda **hiçbir çekirdek DDL'i yok** — 50 çekirdek migration
yeniden uygulanmadı, yalnız `0001_vector` koşuldu. Beklentinin iki maddesi de
doğrulandı (K-475/K-477: setler bağımsız ilerliyor).

📝 **Spec düzeltmesi.** Ön koşul ve beklenen sonuç "32 çekirdek" / "`__migrations`
toplam **33**" yazıyordu → bugün **50** ve **51**. Sabit sayı yerine yapıya
bakan ifadeye çevrildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-066 — SQL Server ve SQLite bu fazdan etkilenmez

**Gerçek sonuç**
İki sözleşme seti de gerçek hedeflere karşı koşuldu (`ap-mssql` container'ı ·
yerel SQLite dosyası), donuk `7e3a4de7` ikilisiyle:

```
Tracon.Sqlite.IntegrationTests     -> Passed!  825/825, failed 0, skipped 0  (40s)
Tracon.SqlServer.IntegrationTests  -> Passed!  806/806, failed 0, skipped 0  (1m 33s)
```

İki sağlayıcıda da davranış aynı; vektör migration'ı hiçbirine sızmadı.

📝 **Not — kanıt bloğundaki sayılar büyümüş, bayat değil.** 2026-08-19 kaydı
554 (SQLite) ve 540 (SQL Server) diyor; bugün 825 ve 806. Bu bir bayatlık
değil, aradaki fazlarda eklenen testlerdir. Case'in iddiası sayıya değil
**farksızlığa** bakar: iki set de sıfır başarısızlıkla yeşil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-067 — SQL tek kaynak: 117 sorgunun taşınması üretilen SQL'i değiştirmez

**Gerçek sonuç**

**Adım 1 — yeşil.**

```
Tracon.Sql.Shared.UnitTests -> Passed!  22/22, failed 0, skipped 0  (203ms)
```

**Adım 2 — KOŞULAMADI.** Case `SqlQueriesBase.CostAddends`'e sahte bir dördüncü
terim eklemeyi istiyor. Bu `src/` altında bir kod değişikliğidir ve turun
**değişmez kuralı 1**'i ihlal eder (skill §1.1): `src/` · `samples/` · `tests/`
tur boyunca donuktur ve bütünlük kapısı `git diff 7e3a4de7..HEAD -- src samples
tests`'in **boş** dönmesidir. Geri alınan bir değişiklik bile o kapıyı bu
oturum boyunca kirletir. Karar kullanıcıya bırakıldı — aşağıdaki bekleyen
kalem tablosuna yazıldı.

**Adım 3 — üçü de yeşil**, gerçek sunuculara karşı:

```
Tracon.PostgreSql.IntegrationTests -> Passed!  888/889, failed 0, skipped 1  (49s)
Tracon.SqlServer.IntegrationTests  -> Passed!  806/806, failed 0, skipped 0  (1m 33s)
Tracon.Sqlite.IntegrationTests     -> Passed!  825/825, failed 0, skipped 0  (40s)
```

Hiçbir maliyet/token/kimlik alanı değişmedi; davranış aynı.

📝 `Tracon.PostgreSql.IntegrationTests` içindeki **1 atlanan** test
araştırıldı ve **temiz çıktı** — sessiz bir atlama değil, opt-in bir kapı:

```
skipped ...Load.BoundedSqlLoadTests.Bounded_run_recording_load_produces_a_report
        Set TRACON_LOAD=1 to run the bounded load report.
```

Yük raporu bilinçli olarak varsayılan koşumun dışında tutulmuş ve atlama
gerekçesini kendi mesajında söylüyor. Kusur değil.

📝 **Spec düzeltmesi.** Adım 1 beklentisi "sekiz test de yeşil" diyor → bugün
**22**. Sabit sayı yerine "hepsi yeşil, sıfır başarısızlık" ifadesine çevrildi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

> Adım 1 ve 3 yeşil; case yalnız adım 2 nedeniyle **Beklemede**. Adım 2'nin
> kararı verilince yeniden koşulacak tek adım odur.

## MT-PG-072 — `runs_v1` görünümü 111.2 sütun tablosunu birebir karşılar; korunan sütun taşımaz

**Gerçek sonuç**
Ayrı bir şema (`mt_s1_v`) `EnableReadViews=true` ile kuruldu. Üç set de
uygulandı ve görünüm gerçekten oluştu:

```
 set_name  | count            to_regclass('mt_s1_v.runs_v1')
 core      |    50            -> mt_s1_v.runs_v1
 knowledge |     1
 views     |     1
```

Sütun kümesi `information_schema.columns`'tan ölçüldü — **24 sütun**:

```
111.2'den EKSIK: yok
111.2'ye EK   : ['model_provider', 'input_price_per_mtok',
                 'output_price_per_mtok', 'cached_input_price_per_mtok']
korunan/icerik sutunu: YOK
```

**İki iddianın ikisi de tutuyor:**

1. 111.2'nin istediği **20 sütunun 20'si de** var, hiçbiri eksik değil.
2. Yasaklı içerik sütunlarının (`state` · `item` · `messages` · `text` ·
   `payload` · `arguments` · `result` · `content`) **hiçbiri** yok.

📝 **Spec düzeltmesi — "birebir" kelimesi sözleşmeye aykırı.** Görünüm 111.2
listesinden **dört fazla** sütun taşıyor. Bu bir sapma değil; migration
dosyasının kendi başlığı (`0001_read_views.sql:4-6`) kuralı açıkça yazıyor:

```sql
-- * A published view never loses a column, renames a column, or narrows a
--   column's type. Adding a column is free. A breaking change ships as a
--   NEW view (runs_v2), the old one keeps working for at least one major
--   version.
```

∴ Sütun **eklemek serbesttir** ve sürüm yükseltmesi gerektirmez. Dört ek
sütunun dördü de fiyatlandırma metadata'sıdır (`model_provider` + üç
`*_price_per_mtok`), içerik değil — korunan sütun kuralı bozulmuyor. Beklenen
sonuç "birebir eşleşir" yerine **"111.2'nin tamamını kapsar, hiçbir korunan
sütun taşımaz"** biçimine çevrildi; doğru değişmez budur.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-075 — `EnableReadViews` varsayılan kapalı; açık değilken görünüm hiç kurulmaz

**Gerçek sonuç**
`EnableReadViews` hiç verilmeyen iki şemada da görünüm yok:

```
 mt_s1_gorunum | mt_s1_k_gorunum
               |                  <- ikisi de NULL

 set_name  | count            views_satiri
 core      |    50            -> 0
 knowledge |     1
```

`to_regclass` iki şemada da `NULL`, `__migrations`'ta `set_name='views'`
satırı **yok**. Karşıt kanıt aynı oturumda ölçüldü: `EnableReadViews=true`
verilen `mt_s1_v` şemasında hem görünüm hem `views` satırı oluştu (MT-PG-072).
Varsayılan gerçekten kapalı ve açık istek olmadan set devreye girmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-073 — `total_cost` store'un raporladığı toplamla birebir eşleşir; tanımsız fiyat `NULL` kalır

**Gerçek sonuç**
`mt_s1_v` şemasında üç gerçek `run` üretildi: ikisi fiyat **tanımsızken**,
biri `gpt-5.4-mini` için fiyat tanımlandıktan sonra.

```
 input_tokens | output_tokens |  input_cost  | output_cost  |  total_cost  | cost_currency
          343 |             4 |              |              |              |
          343 |             4 |              |              |              |
          343 |             4 | 0.0000857500 | 0.0000080000 | 0.0000937500 | USD
```

**Adım 2 — `NULL`, `0` değil.** Fiyatı tanımsız iki koşunun `total_cost`'u
boş (`NULL`) döndü; `psql` bunu boş hücre olarak yazar ve `sum()` onları
yok sayar. `0` yazılsaydı toplam bozulurdu.

**Adım 1 — iki değer birebir aynı.**

```
GET /tracon/api/stats  ->  "totalCost": 9.375e-05
                           "currency": "USD"
                           "runsWithUnknownPricing": 2
SELECT sum(total_cost)
  FROM mt_s1_v.runs_v1  ->  0.0000937500
```

`9.375e-05 == 0.00009375` — görünüm ile store'un kendi istatistiği birebir
eşleşiyor. `runsWithUnknownPricing: 2` de görünümdeki iki `NULL` satırla
tutuyor, yani iki taraf aynı koşuları aynı biçimde sınıflandırıyor.

Aritmetik de bağımsız doğrulandı: `343/1e6 × 0.25 = 0.00008575` (`input_cost`),
`4/1e6 × 2.00 = 0.000008` (`output_cost`), toplam `0.00009375`.

📝 **Ortam notu — fiyat yapılandırma yolu.** Fiyat anahtarı
`Tracon:Pricing:{saglayici}:{model}:Input` / `:Output`'tur. **`Providers`
segmenti YOKTUR** — `BindPricing` sağlayıcıyı `Tracon:Pricing`'in doğrudan
çocuğu olarak okur (`TraconServiceCollectionExtensions.Binding.Models.cs:28`),
`Currency`/`Voice`/`Images`'i atlar. Yaprak anahtar C# özellik adı
(`InputCostPerMillionTokens`) **değil**, kısa `Input`/`Output`'tur. Yanlış
anahtar sessizce düşmez; başlangıçta `OptionsValidationException` ile reddedilir
(K-034, MT-CORE-065) — bu oturumda ampirik olarak tetiklendi ve doğru çalıştı.

🚨 **`HATA-S1-018`** — o reddin mesajı karışık dilde. Aşağıdaki kayda bak.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-074 — Görünüm kiracı filtrelemez; iki kiracının satırı da görünür

**Gerçek sonuç**
`Tracon:Tenancy:Enabled=true` ve `AllowHeaderResolution=true` ile iki ayrı
kiracıdan birer `run` üretildi (`X-Tracon-Tenant`). Görünüm **filtresiz**
sorgulandı:

```
  tenant_id  | run_sayisi
 default     |          3
 kiraci-alfa |          1
 kiraci-beta |          1
```

Üç kiracının da satırı döndü. Beklendiği gibi (111.1): görünüm bir güvenlik
sınırı **değildir**, `tenant_id` filtrelenmeden taşınır ve filtreleme
tüketicinin kendi sorgusunun işidir.

📝 **Ortam notu — kiracı başlığı iki bayrak ister.** `X-Tracon-Tenant` yalnız
`Tracon:Tenancy:Enabled=true` **ve** `AllowHeaderResolution=true` iken okunur;
ikisi de varsayılan **kapalıdır** (`TraconTenancyOptions.cs:42,57` — "a header
can be spoofed"). Bayraklar kapalıyken başlık sessizce yok sayılır ve her istek
`DefaultTenantId`'ye düşer. Bu oturumda ilk denemede tam olarak bu oldu: iki
istek de `default` kiracısına yazıldı. Sonraki oturumlar için tuzak.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

### HATA-S1-018 — Sevk edilen iki hata mesajı karışık dilde; dil kapısı yapısal olarak göremiyor

| | |
|---|---|
| **Case** | MT-PG-073 (ortam kurarken tetiklendi) |
| **Önem** | Orta |
| **Sınıf** | dil sınırı (K-228) — kapının kör noktası |

**Belirti.** Yanlış bir fiyat anahtarıyla açılan uygulama şu mesajla düşüyor:

```
Unhandled exception. Microsoft.Extensions.Options.OptionsValidationException:
TraconPricingOptions: 'Providers:openai' ne 'Input' ne 'Output' contains neither value. Check the key name.
```

`ne 'Input' ne 'Output'` Türkçe "ne … ne …" bağlacıdır ve İngilizce bir cümlenin
ortasına girmiş. Cümle İngilizce okunduğunda anlamsızdır; üstelik "ne … ne …"
ile "contains neither value" **aynı olumsuzlamayı iki kez** söylüyor.

**Kaynak.** `src/Tracon.Core/TraconOptionsValidator.cs` — iki ayrı yer:

```
292:  $"{nameof(TraconPricingOptions)}: '{providerName}:{modelName}' ne 'Input' ne 'Output' " +
293:  "contains neither value. Check the key name.");

313:  $"'{nameof(VoicePriceOverride.PerMillionCharacters)}' ne '{nameof(VoicePriceOverride.PerMinute)}' " +
314:  "contains neither value. Check the key name.");
```

İkisi de **sevk edilen çalışma anı metnidir** (`src/`, `Tracon.Core` paketi) ve
tüketicinin konsoluna çıkar. `AGENTS.md`: "pakete giren ve çalışma anında
çalışan her şey İngilizce'dir" (K-228).

**Niçin kapı kaçırdı.** `SourceLanguageTests` iki şey tarar: Türkçe'ye özgü
harfler (`[çğıöşüÇĞİÖŞÜ]`) ve bir Türkçe kelime listesi. `ne` **ikisine de**
takılmaz:

- Türkçe'ye özgü harf taşımıyor — `n` ve `e` ASCII.
- Kelime listesinde yok, çünkü listenin kendi notu iki harfli kelimeleri
  **bilinçli olarak** dışlıyor (`SourceLanguageTests.cs:136-142`):
  > "Words that are also English words, or that appear inside identifiers, are
  > deliberately absent: … and **every two-letter word**."

Dışlama gerekçesi sağlamdır (yanlış pozitif seli), ama sonucu şudur: **iki
harfli bir Türkçe bağlaç kapıdan yapısal olarak geçer.** Bu tek bir kaçak
değil, kapının tanımlı bir kör noktasıdır.

**Önerilen düzeltme (kapanışta).** İki katman:

1. İki mesajı düzelt. Doğru İngilizce: `'{provider}:{model}' contains neither
   an 'Input' nor an 'Output' value. Check the key name.` — hem bağlaç
   İngilizce olur hem çifte olumsuzlama kalkar.
2. Kapının kör noktasını kapat. Tüm iki harfli kelimeleri listeye almak
   yanlış pozitif üretir; **hedefli** bir kural daha ucuz: `ne 'X' ne` ya da
   `\bne\b` deseni **yalnız tırnaklı bir terimin iki yanında** aranırsa
   `ne` bağlacı yakalanır, İngilizce `ne` geçişleri (pratikte yok) etkilenmez.

**Sınıf taraması — KOŞULDU (2026-09-16).** `src/` altındaki tüm `.cs`
dosyalarında string literal taşıyan satırlar dört iki-harfli Türkçe bağlaç için
tarandı. Sonuç:

```
ne ... ne : 3 satir  (TraconOptionsValidator.cs:292 · 312 · 313)
ya ... ya : 0
ki        : 0
mi/mu     : 0
```

**Sınıf dardır ve tamamen ölçülmüştür:** sızıntı tek dosyada, iki mesajda
(üç satırda) toplanıyor; başka hiçbir iki-harfli bağlaç sevk edilen metne
geçmemiş. 312. satır 292'nin `Voice` karşılığıdır ve aynı cümleyi tekrarlar.
∴ kapanışta üç satır düzeltilince sınıf kapanır; ayrıca kapıya hedefli kural
eklenirse yeniden açılması engellenir.

## MT-PG-069 — `DataSource` ve `ConnectionString` birlikte verilirse başlangıç hatası

**Gerçek sonuç**
İzlek C. Case bir `ServiceProvider`'ı kod içinde kurmayı ister; koşum turunda
kod yazılamaz (kural 1), bu yüzden spec'in kendi işaret ettiği otomatik
karşılığı **bu oturumda** donuk `7e3a4de7` ikilisiyle ve gerçek `ap-pg`
konteynerine karşı koşuldu:

```
Tracon.PostgreSql.IntegrationTests.ExternalDataSourceTests
  .DataSource_and_ConnectionString_together_is_rejected     -> mevcut, GEÇTİ

Paket sonucu: Passed!  888/889, failed 0, skipped 1  (49s)
```

Testin adı `--list-tests` ile doğrulandı (varlığı teyit edildi) ve paket
**sıfır başarısızlıkla** bitti, yani bu test geçenler arasındadır. Tek atlanan
test bu değil (aşağıya bak).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-070 — Dış data source host kapanışında dispose edilmez

**Gerçek sonuç**
İzlek C, MT-PG-069 ile aynı yöntem. Sahipliğin **iki yönü** de ayrı testlerle
kapsanıyor ve ikisi de bu oturumda geçti:

```
ExternalDataSourceTests.External_data_source_is_not_disposed_when_the_host_stops -> GEÇTİ
ExternalDataSourceTests.Own_data_source_is_disposed_when_the_host_stops          -> GEÇTİ
```

Beklenen sonucun iki maddesi bunlara birebir karşılık geliyor: dış data source
kapanıştan sağ çıkıyor, Tracon'in **kendi** kurduğu data source ise dispose
ediliyor. Sahiplik doğru yönde çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-071 — Havuz paylaşımı ölçümü: aynı connection string, iki `NpgsqlDataSource`, tek havuz DEĞİL

**Gerçek sonuç**
İzlek C. 👤 işaretli `psql` adımını otomatik test `pg_stat_activity`'yi
doğrudan sorgulayarak yürütüyor:

```
ConnectionPoolSharingTests
  .Two_data_sources_built_from_the_same_connection_string_do_not_share_a_pool -> GEÇTİ
```

Test gerçek `ap-pg` konteynerine karşı koştu ve geçti; ∴ ölçülen backend sayısı
beklenen `2 × concurrentConnectionsPerSource` değerindedir, `embedding.md`'nin
çürütülmüş eski iddiası olan tek havuz değil. Npgsql havuzu connection
string'e değil `NpgsqlDataSource` **örneğine** aittir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-PG-068 — Dış `NpgsqlDataSource`: tek havuz, iki tüketici

**Gerçek sonuç**
`samples/Tracon.Embedded` şerit-yerel bir veritabanına (`tracon_embedded_s1`,
`ap-pg` üzerinde) ve port **5091**'e karşı koşuldu. Örnek kendi
`NpgsqlDataSource`'unu kurup `tracon.UsePostgreSql(o => o.DataSource = ds)`
ile veriyor (`Program.cs:104`) — case'in tarif ettiği yol.

**Üç beklentinin ikisi karşılandı, biri DÜŞTÜ.**

**1. ✅ Tek satırlık migration logu:**

```
Tracon applied 50 migration(s). Schema: tracon.
```

**2. ✅ `Tickets.RunId` == `tracon.runs.id`** — tek havuz, iki tüketici, aynı
veritabanı:

```
POST /tickets -> HTTP 201
{"id":"e0cb6c40-...","tenantId":"acme","runId":"01a0ab77-39a1-7532-9394-20abbc68d363"}

public."Tickets"    Id=e0cb6c40-...  TenantId=acme  RunId=01a0ab77-39a1-7532-9394-20abbc68d363
tracon.runs         id=01a0ab77-39a1-7532-9394-20abbc68d363  tenant_id=acme  status=1

information_schema.schemata -> public, tracon   (ayni veritabaninda iki sema)
```

Host'un EF Core şeması ile Tracon'in store şeması aynı veritabanında, aynı
data source üzerinden yaşıyor. Fazın asıl iddiası bu ve **tutuyor**.

**3. ☑ KALDI — `GET /tracon/api/runs/{id}` kaydı döndürmedi:**

```
GET /tracon/api/runs/01a0ab77-...  -H "X-Host-Tenant: acme"
-> HTTP 404  {"title":"Run not found","detail":"There is no run with id '01a0ab77-...'."}

GET /tracon/api/runs               -H "X-Host-Tenant: acme"
-> HTTP 403  {"title":"Run not authorized",
              "detail":"The registered IRunAuthorizationHandler denied this request."}
```

Kayıt veritabanında **duruyor** ve `tenant_id`'si `acme`. Liste ucunun 403'ü
nedeni açık ediyor: örneğin kendi `EmbeddedRunAuthorizationHandler`'ı `acme`'yi
tanımıyor. Tekil uçta 404 dönmesi kasıtlıdır ve doğrudur — reddedilen bir
kaynak, var olmayan bir kaynakla **aynı** yanıtı verir, aksi hâlde ret kaynağın
varlığını doğrulardı (`EmbeddedRunAuthorizationHandler.cs:50-53`).

**Kök neden — kiracı dizini boş:**

```
GET /tracon/api/tenants -> [{"slug":"default", ...}]     <- YALNIZ default
```

`EmbeddedTenantStore` yapıcısında `acme` ve `globex`'i **seed eder**
(`EmbeddedTenantStore.cs:20-23`) ve örnek onu `AddTracon()`'dan **önce**
kaydeder (`Program.cs:51`). Buna rağmen çalışan `ITenantStore` örneğin
kendisininki değil, Tracon'in SQL store'udur. Yetkilendirici de o boş dizini
okuduğu için `acme`'yi tanımıyor.

🚨 **`HATA-S1-019`** — kök neden bir sözleşme ihlalidir, aşağıdaki kayda bak.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

### HATA-S1-019 — `UsePostgreSql` tüketicinin store kaydını `Replace` ile eziyor

| | |
|---|---|
| **Case** | MT-PG-068 |
| **Önem** | **Yüksek** |
| **Sınıf** | paket sözleşmesi — `TryAdd` kuralının ihlali |

**İhlal edilen kural.** `AGENTS.md`, bu repo'nun paket ailesi için dört temel
kalite eşiğinden birini şöyle yazıyor:

> **`TryAdd*` ile kaydet; tüketicinin kaydı her zaman kazanmalı.**

**Belirti.** Tüketici `ITenantStore`'unu `AddTracon()`'dan **önce**
`AddSingleton` ile kaydetse bile, `UsePostgreSql()` onu sessizce eziyor. Hiçbir
uyarı, hiçbir log, hiçbir başlangıç hatası yok — kayıt sessizce kayboluyor.

**Kök neden.** `TraconPostgreSqlBuilderExtensions.cs:350`:

```csharp
services.Replace(ServiceDescriptor.Singleton<ITenantStore, AuditingTenantStore>(
    static provider => new AuditingTenantStore(
        ActivatorUtilities.CreateInstance<SqlTenantStore>(provider), ...)));
```

`Replace`, `TryAdd` **değil**. `Replace` var olan kaydı koşulsuz ezer ve
Tracon'in kendi bellek-içi varsayılanı ile tüketicinin bilinçli özel kaydını
**ayırt edemez**.

**Sınıf taraması — KOŞULDU (2026-09-16).** Tek bir arayüz değil, sistematik:

```
TraconPostgreSqlBuilderExtensions.cs : 39 Replace
TraconSqlServerBuilderExtensions.cs  : 39 Replace
TraconSqliteBuilderExtensions.cs     : 39 Replace
                                       --- toplam 117
```

PostgreSQL'de ezilen **34 store arayüzü**:

```
IAgentDefinitionStore IAgentSkillStore IApiKeyStore IAttachmentStore IAuditLog
IDataSubjectStore IEvalStore IExperimentStore IIdempotencyStore
IInboundTriggerStore IJobScheduleStore IJobStore IMcpServerStore
IMigrationApplier IPendingApprovalStore IQuotaStore IRetentionPolicyStore
IRetentionStore IRunInputStore IRunScoreStore IRunStore ISessionStore
ISingletonLeaseStore ISkillScriptGrantStore ISqlPersistenceDiagnostics
IStatePreflightReader ITenantEgressPolicyStore ITenantProviderBindingStore
ITenantStore IToolApprovalRuleStore ITraceStore IVoiceSessionStore
IWebhookStore IWorkflowDefinitionStore
```

**`Replace` niçin var — meşru ihtiyaç.** `TryAdd` burada **çalışmaz**:
`AddTracon()` bellek-içi varsayılanları zaten kaydetmiştir, `UsePostgreSql()`
onları geçersiz kılmak **zorundadır**. Yani kusur `Replace` kullanmak değil,
`Replace`'in üç durumu tek sayması:

| Var olan kayıt | Doğru davranış | Bugünkü davranış |
|---|---|---|
| Tracon'in bellek-içi varsayılanı | ez | ezer ✅ |
| Tüketicinin `AddTracon()` **öncesi** özel kaydı | **koru** | ezer ❌ |
| Kayıt yok | ekle | ekler ✅ |

**Niçin bu case'te görünür oldu.** `Tracon.Embedded` bu sözleşmeye açıkça
güveniyor. `Program.cs:12` şunu yazıyor: "*BEFORE AddTracon() so the host's own
registration wins*". `ITenantContext` için bu **çalışıyor** (Tracon onu `TryAdd`
ile kaydeder ve örnek ayrıca `RequireCustomBinding<ITenantContext>()` ile
koruyor, `Program.cs:70`). `ITenantStore` için **çalışmıyor** ve koruma da yok
— bu yüzden kayıp sessiz.

∴ Örneğin README'sindeki belgelenmiş akış bugün kırık: `POST /tickets` 201
dönüyor ama devamındaki `GET /tracon/api/runs/{id}` 200 yerine 404 veriyor.

**Öneri (kapanışta değerlendirilecek).**

- En doğrusu: bellek-içi varsayılanlar bir işaretleyici (`ImplementationType`
  kontrolü ya da bir `TraconDefaultRegistrationMarker`) taşısın; `Replace`
  yalnız **işaretli** kaydı ezsin, işaretsiz olanı bıraksın.
- En azından: ezilen bir tüketici kaydı **sessiz kalmasın** — başlangıçta
  uyarı loglansın ya da `RequireCustomBinding<T>()` bu yolu da kapsasın.

🚨 `RequireCustomBinding<T>()`'in bu durumu yakalayıp yakalamadığı ayrıca
ölçülmeli: örnek onu yalnız `ITenantContext` için çağırıyor. `ITenantStore`
için de çağrılsaydı başlangıçta patlar mıydı, yoksa o da mı sessiz kalırdı?
Kapanışın ilk sorusu bu olmalı — cevabı "patlardı" ise kusur yalnız örnekte,
"sessiz kalırdı" ise koruma mekanizmasının kendisinde.

---

## Koşulamayan case — kullanıcı kararı bekliyor

| Case | Neden | Kullanıcıdan istenen |
|---|---|---|
| `MT-PG-067` adım 2 | `SqlQueriesBase.CostAddends`'e sahte bir terim eklemeyi ister. Bu `src/` altında kod değişikliğidir; turun değişmez kuralı 1 yasaklar ve bütünlük kapısı donuk ağaçların `git diff`'inin boş kalmasına dayanır | Üç seçenekten biri (aşağıda) |

**Seçenekler:**

1. **Aşama 2'ye ertele.** Kapanış modunda kod zaten değişiyor; adım 2 orada
   doğal olarak koşulur. En ucuz ve tura en az müdahale eden yol.
2. **Repo dışı atılabilir bir kopyada koş.** `MT-PKG-034/035` emsali
   (`~/tracon-manuel/ohost`). Burada tüketici host'u değil Tracon'in **kendi
   kaynağı** değiştirileceği için tam bir kaynak kopyası ve ek bir derleme
   gerekir — birkaç dakikalık maliyet, donuk ağaçlara dokunmaz.
3. **Kalıcı olarak kapsam dışı bırak.** Adım 2 bir **kapıyı** sınıyor
   (`CostAddendsCrossCheckTests` düşmeli); bunu doğrulamanın yeri belki de
   manuel kabul seti değil, kapının kendi birim testidir.

**Öneri: 1.** Adım 1 ve 3 zaten yeşil koşuldu ve fazın asıl iddiasını
(üretilen SQL değişmedi) kanıtlıyor. Adım 2 yalnız kapının kendisini sınar ve
kapanış modunda sıfır ek maliyetle koşulabilir.

✅ **KULLANICI KARARI (2026-09-16): Seçenek 1 — Aşama 2'ye ertelendi.**
Case `☐ Beklemede` kalır ve **Aşama 2'nin ilk işlerinden biri** olarak
koşulur: kapanış modunda `src/` zaten değişebilir durumdadır, o yüzden
`SqlQueriesBase.CostAddends`'e sahte dördüncü terim eklenir,
`dotnet test tests/Tracon.Sql.Shared.UnitTests` koşulur
(`CostAddendsCrossCheckTests` **düşmelidir**), terim geri alınır ve set
yeniden yeşil doğrulanır. Ancak ondan sonra `☑ Geçti` işaretlenir.

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

**Oturum 8 bitti. `MT-PG-001..047` koşuldu — 30 case: 29 ☑ Geçti · 1 ☑ Kaldı.**
Dört blok kapandı: `001-008` · `020-027` · `030-035` · `040-047`.

- **Sonraki oturumun işi:** `050-053` bloğu (4 case), sonra `060-075` (16 case).
  Toplam **20 case** kaldı.
- **Bozuk ön koşul:** yok. `mt_s1` tam şemayla ayakta (51 migration, 2 agent,
  2 run, birkaç `document_embeddings` satırı).

🚨 **MT-PG-050 için kullanıcı kararı hazır (2026-09-16).** Case `ap-pg`
container'ını durdurmayı istiyor; şerit kuralı 3 bunu yasaklıyor. **Karar:
container'a DOKUNMA** — erişilemezlik bağlantı dizesini geçersiz bir porta
çevirerek ya da `docker network disconnect` ile taklit edilecek. Aynı davranış
ölçülür, paylaşılan kaynak bozulmaz, Faz B açıldığında da güvenli kalır.

### Bu oturumun bulgusu

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

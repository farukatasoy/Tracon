# Faz 110 — Tüketici Bağlantı Düzlemi

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-26-ef-core-uyum-olcumu.md`](../../kesif/2026-08-26-ef-core-uyum-olcumu.md) — kalem **P1 · P2 · P3 · P5 · P6**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** Yok. Teknik zorunluluk yoktur; [Faz 109](109-FRONTEND-MODULLERI-VE-EKRAN-TESTLERI.md) kapandıktan sonra sıraya girer
> **Paketler:** `AgentPrism.PostgreSql`, `.SqlServer`, `.Sqlite`, `.Sql.Shared` · `samples/AgentPrism.Embedded`
> **Yeni paket:** NuGet paketi **yok**. Sample-only bağımlılık: `Npgsql.EntityFrameworkCore.PostgreSQL` — yalnız `samples/`, sevk edilen hiçbir pakete girmez · **Migration:** Yok
> **Public API:** **Büyüyor** — üç `Options` tipine birer `DbDataSource?` alanı. Bugün ucuz: `wc -l src/*/PublicAPI.Shipped.txt` = **17 satır** (19 paketin tamamı yalnız başlık taşıyor, K-603). Faz 7 sonrası aynı alanı eklemek kırıcı olurdu
> **Tüketici yüzeyi:** Site — yeni sayfa `docs-site/src/content/docs/guides/ef-core.md` · değişen: `guides/embedding.md`, `guides/production.md`, `packages.md` · Sevk edilen: `src/AgentPrism.PostgreSql/README.md`, `.SqlServer/README.md`, `.Sqlite/README.md` ve yeni alanın XML `<example>` bloğu
> **Manuel test alanı:** [`manuel-test/03-KALICILIK-POSTGRESQL.md`](../../manuel-test/03-KALICILIK-POSTGRESQL.md) · [`manuel-test/04-KALICILIK-DIGER.md`](../../manuel-test/04-KALICILIK-DIGER.md) — plan `26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md`'yi işaret ediyordu, o dosya istemci tool'ları/gömülebilir sohbet widget'ına özgü ve bu fazla ilgisiz; SQL Server/SQLite'ın simetrik `DataSource` alanı `04`'e girdi (bkz. "Plandan Sapmalar")

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show e8ce06e:docs/arsiv/fazlar/110-TUKETICI-BAGLANTI-DUZLEMI.md
> ```
>
> Damıtıldı 2026-08-26 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

EF Core kullanan bir uygulama AgentPrism'i gömünce **iki bağlantı düzlemi** doğar. Bugün bu düzlemler arasındaki temas ne bir sözleşmedir ne belgelidir: PostgreSQL'de kazara çalışır, diğer iki sağlayıcıda hiç yoktur, ve sitedeki kapasite iddiası mekanizmayla çelişir. Faz bu temas yüzeyini açık, simetrik ve ölçülmüş bir sözleşmeye bağlar.

## Bitiş Ölçütleri (DoD)

- [x] `UsePostgreSql(o => o.DataSource = ds)` ile kurulan uygulama `run` yapar ve `ConnectionString` **istemez** — `ExternalDataSourceTests.DataSource_alone_does_not_require_a_connection_string` + `Compiled_agent_runs_against_an_external_data_source` (gerçek PostgreSQL)
- [x] Aynı davranış `UseSqlServer` ve `UseSqlite` için de doğrudur — her ikisinin kendi `ExternalDataSourceTests`'i, gerçek sunucu/dosyaya karşı
- [x] Host kapanışında dış data source dispose **edilmez**; case 4 kanıtlar — `External_data_source_is_not_disposed_when_the_host_stops` (üç sağlayıcı) + `samples/AgentPrism.Embedded`'in canlı `SIGTERM` koşumu (bkz. `MT-PG-070`)
- [x] `NpgsqlDataSource` artık public DI servisi olarak kaydedilmez; bunu doğrulayan test yeşildir — `The_data_source_is_not_registered_as_a_public_DI_service` (üç sağlayıcı; denetimin 🔴 bulgusu üzerine eklendi, bkz. "Denetim Bulguları")
- [x] `ConnectionPoolSharingTests` ölçümü koşuldu ve sonucu "Plandan Sapmalar" bölümüne yazıldı
- [x] `embedding.md` havuz cümlesi ölçümle uyumludur
- [x] `guides/ef-core.md` yayında; `npm run build` + `check-links.mjs` temiz
- [x] `samples/AgentPrism.Embedded` hem veritabanısız hem PostgreSQL'li yolda koşar — veritabanısız: `AgentPrism.Embedded.Tests` 3/3; PostgreSQL'li: canlı konteynere karşı `POST /tickets` + `GET /agentprism/api/runs/{id}` (bkz. `MT-PG-068`)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 678ba30`
- [x] `samples/AgentPrism.Embedded` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — plan `samples/AgentPrism.Api`yı işaret ediyordu; bu fazın kendi örneği `AgentPrism.Embedded` olduğu için kanıt oradan alındı (bkz. "Plandan Sapmalar")
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` → ✅ temiz
- [x] Manuel kabul case'leri `docs/manuel-test/03-KALICILIK-POSTGRESQL.md` ve `04-KALICILIK-DIGER.md` içine eklendi (plan `26-...`yi işaret ediyordu, bkz. "Plandan Sapmalar"); otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — ilk turda 1×🔴 + 2×🟡 çıktı, üçü de kapatıldı (bkz. "Denetim Bulguları")

### Doğrulama komutları

```bash
# Dış data source ile kurulan örnek gerçekten koşuyor mu
curl -s http://localhost:5081/agentprism/api/runs | head -c 400

# Public DI kaydı gerçekten kalktı mı
grep -rn "TryAddSingleton(static provider => NpgsqlDataSourceFactory" src/

# Havuz ölçümü
./artifacts/bin/AgentPrism.PostgreSql.IntegrationTests/release/AgentPrism.PostgreSql.IntegrationTests \
  --filter-method "*ConnectionPoolSharing*"
```

---

## Plandan Sapmalar

**110.1 ölçüm sonucu: "Backend sayısı ≈ 2N" — iddia YANLIŞTI.**
`ConnectionPoolSharingTests.Two_data_sources_built_from_the_same_connection_string_do_not_share_a_pool`
aynı connection string'le kurulan iki `NpgsqlDataSource`'tan 5'er eşzamanlı
bağlantı tuttu; `pg_stat_activity` tam **10** backend gördü (5 değil, ölçülen
kesin sayı — `ShouldBe`, `ShouldBeGreaterThan` değil, geçici olarak sıkı
assert ile doğrulandı). Mekanizma: Npgsql 7.0'dan beri havuz `NpgsqlDataSource`
**örneğine** aittir, connection string'e değil — iki ayrı örnek, aynı string
olsa bile, iki ayrı havuz açar. `embedding.md`'nin "aynı connection string iki
tarafı tek havuzda buluşturur" cümlesi düzeltildi; 110.2 tek havuzu GERÇEKTEN
mümkün kılıyor (planın öngördüğü ikinci satır).

**Manuel test alanı referansı yanlıştı.** Plan `26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md`
işaret ediyordu; o dosya istemci tool'ları/gömülebilir sohbet widget'ına özgü ve
bu fazla hiçbir ilgisi yok. Doğru sağlayıcı-simetri dosyası
`04-KALICILIK-DIGER.md` (SQL Server/SQLite) idi — case'ler oraya ve
`03-KALICILIK-POSTGRESQL.md`'ye eklendi.

**PostgreSQL için `DataSource`'un kabul ettiği tip planın öngördüğünden dar
tutuldu.** Plan yalnız "`DbDataSource`, `NpgsqlDataSource` değil" diyordu (public
imza için — bu aynen korundu). Uygulamada `NpgsqlDataSourceFactory.Resolve`
RUNTIME'da `options.DataSource`'un gerçekten bir `NpgsqlDataSource` olmasını
zorunlu kılıyor (aksi hâlde `AgentPrismException`, açık mesajla). Gerekçe: (1)
PostgreSQL için üretim kalitesinde tek `DbDataSource` uygulaması zaten Npgsql'in
kendisi — plan da "SQL Server/SQLite dürüst not"unda bunun SQL Server için
henüz hiç var olmadığını, SQLite için de var olmadığını zaten kabul ediyordu;
(2) `PgVectorSearchStore` somut `NpgsqlDataSource` tipini gerektiriyor ve tek
kaynaktan (`SqlStoreContext.DataSource`) beslenmesi gerekiyordu — aksi hâlde
`EnableKnowledge` açıkken İKİ ayrı data source (dolayısıyla iki havuz) kurulurdu,
bu fazın kendi "tek havuz" iddiasıyla çelişirdi. SQL Server ve SQLite için
kısıtlama YOK — `Options.DataSource` orada gerçekten herhangi bir `DbDataSource`
kabul eder (kendi `SqlServerDataSource`/`SqliteDataSource` adaptörleri dahil);
testler bunu doğrudan kanıtlar.

**SQL Server/SQLite'ta "kendi kurduğu data source'u dispose eder" iddiası
FONKSİYONEL olarak doğrulanamadı — tasarım yine de doğru.** Ölçüldü: ne
`SqlServerDataSource` ne `SqliteDataSource`, `DbDataSource.Dispose()`'u
override ETMİYOR (temel sınıfın no-op'u kalıyor) — sürücünün kendi havuzu bu
ince adaptöre değil `Microsoft.Data.SqlClient`/`Microsoft.Data.Sqlite`'ın
kendisine ait. `Own_data_source_is_disposed_when_the_host_stops` bu yüzden
`Own_data_source_is_marked_as_owned` olarak yeniden yazıldı — `OwnsDataSource`
bayrağının doğru değeri taşıdığını kanıtlar, ama dispose'un GÖZLENEBİLİR bir
etkisi yoktur (bu iki sağlayıcıda). PostgreSQL tarafında `NpgsqlDataSource`
gerçekten bir kaynak sahibidir; oradaki fonksiyonel test (`ObjectDisposedException`)
aynen planlandığı gibi çalıştı. Sahiplik mantığının KENDİSİ (`SqlStoreContext
.Dispose()`/`DisposeAsync()`) ayrıca bir spy `DbDataSource` ile sağlayıcıdan
bağımsız, izole kanıtlandı (`SqlStoreContextDisposalTests`, Docker gerekmez) —
bu üçü de kapsıyor, çünkü kod paylaşılan kaynaktır (K-176).

## Bu Fazda Verilen Kararlar

- **K-625** — Üç SQL sağlayıcısına `Options.DataSource` alanı eklendi;
  AgentPrism kendi kurduğu data source'u artık public bir DI servisi olarak
  kaydetmez; `DataSource` ve `ConnectionString` birlikte verilirse başlangıç
  hatası. Tam gerekçe: `docs/KARARLAR.md`.

## Denetim Bulguları

`faz-denetim` skill'i taze bağlamlı bağımsız bir agent ile koşuldu (kod
yazmadan yalnız `git status`/`git diff` + gerçek `dotnet build`/`dotnet test`
ile doğrulama). İlk tur 1×🔴 + 2×🟡 buldu; üçü de aynı fazda kapatıldı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | DoD'nin istediği "`NpgsqlDataSource` artık public DI servisi değil" iddiasını doğrulayan hiçbir test yoktu — yalnız elle çalıştırılan bir `grep` vardı | **Düzeltildi.** Üç sağlayıcıya `The_data_source_is_not_registered_as_a_public_DI_service` testi eklendi (`provider.GetService<NpgsqlDataSource>()`/`SqlServerDataSource`/`SqliteDataSource` `null` döner), gerçek sunucu/dosyaya karşı yeşil |
| 2 | 🟡 | DoD'nin 13 satırı hâlâ `- [ ]` işaretsizdi, doküman başlığı "✅ Tamamlandı" diyordu | **Düzeltildi.** Her satır kanıtına göre `[x]` işaretlendi |
| 3 | 🟡 | `samples/AgentPrism.Embedded`'in kurduğu `NpgsqlDataSource` hiçbir yerde dispose edilmiyordu — fazın kendi sözleşmesi ("caller keeps ownership and disposes it when the host shuts down") referans örnekte uygulanmıyordu | **Düzeltildi.** `app.Lifetime.ApplicationStopping.Register(() => dataSource.Dispose())` eklendi; canlı bir PostgreSQL konteynerine karşı `SIGTERM` ile doğrulandı — kapanış logunda hata yok |

🔴 ve 🟡 bulgu kalmadı. Temiz çıkan başlıklar (ilk tur): 3.2 (test tiyatrosu
yok), 3.3 (test seviyeleri doğru), 3.5 (imza-gövde kayması yok), 3.6 (plan
dışı public API yok), 3.7 (repo kuralları), 3.8 (ürün yüzeyi/site sözleşmesi).

## Sonraki Faza Devir Notu

- **Faz 111** (Okuma Sözleşmesi Görünümleri, P4) bu fazdan bağımsızdır ama aynı
  `AgentPrism.Sql.Shared`/üç sağlayıcı katmanına dokunacaktır — `SqlStoreContext`
  artık `IDisposable`/`IAsyncDisposable`; yeni bir alan eklerken bu iki metodu
  unutma.
- **SQL Server'ın `DbDataSource` durumu yeniden ölçülmeli** bir sonraki
  `Microsoft.Data.SqlClient` sürüm yükseltmesinde (bugün 7.0.2, ölçülen: yok).
  Ölçüm sonucu ne olursa olsun `AgentPrismSqlServerOptions.DataSource` alanı
  zaten var — yalnız dokümanın "henüz yok" cümlesi değişir.
- **`samples/AgentPrism.Embedded`'in EF Core yolu `Database.EnsureCreatedAsync()`
  kullanır, gerçek `dotnet ef migrations` DEĞİL** — örnek basitliği için
  bilinçli bir tercih (`production.md`'nin CI adımı gerçek `dotnet ef database
  update`'i belgeler, ama örnek kod içinde migration scaffolding'i taşımaz).
  Bir sonraki oturum bunu "eksik" sanmasın.

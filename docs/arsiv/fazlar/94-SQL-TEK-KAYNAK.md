# Faz 94 — SQL Tek Kaynak

> **Durum:** ✅ Tamamlandı (2026-08-24)
> **Kaynak:** [`arsiv/kesif/2026-08-23-yapisal-sorun-envanteri.md`](../kesif/2026-08-23-yapisal-sorun-envanteri.md) kalem **8**. Bu faz bir `F-NN` adayından gelmez.
> **Önkoşul:** [Faz 93](93-KUSUR-SINIFI-KAPILARI.md) — zorunlu değil, ama sıra kullanıcı tarafından böyle seçildi: önce kusur sınıfı kapıları, sonra bu faz. Faz 93 bu fazın kapsamındaki C# tarafını (K-483'ün `RunCost.Total()` ikizi) etkilemez
> **Paketler:** `AgentPrism.Sql.Shared` (bağlı kaynak, K-176), `AgentPrism.PostgreSql`, `AgentPrism.SqlServer`, `AgentPrism.Sqlite`
> **Yeni paket:** Yok · **Migration:** **Yok** — bu faz şemaya dokunmaz, yalnız SQL **metninin** nerede yaşadığını değiştirir
> **Public API:** Büyümüyor. Ölçüldü 2026-08-23: `PublicAPI.Unshipped.txt` 8.079 satır, `Shipped.txt` boş. `SqlQueriesBase`, `SqlDialect` ve `Sql*Store` tiplerinin tamamı `internal`'dır
> **Tüketici yüzeyi:** **Yok.** `tuketici-dokuman-senkronu` Adım 0 tablosundaki hiçbir yol tutmuyor: public üye değişmiyor, HTTP ucu değişmiyor, ekran yok, yeni paket yok, sevk edilen metin yok. Üretilen SQL **birebir aynı kalır** — davranış değişmez, yalnız metnin kaynağı tek yere iner. Skill koşmaz; gerekçe budur.
> `dokuman-bakim.py --site-denetle` bu fazda `kalicilik`/`paket-tanimi`
> kurallarını TETİKLER çünkü üç sağlayıcının `.csproj` dosyaları değişti — ama
> tek değişiklik, test-yalnız bir `InternalsVisibleTo` girdisi ve onu
> açıklayan bir yorumdur (bkz. Dosya Listesi). Ne kalıcılık davranışı ne paket
> tanımı (bağımlılık, sürüm, açıklama) değişti; `getting-started/persistence.md`
> ve `packages.md` güncellenmeyecek. `--site-gerekce-yazildi` ile geçildi.
> **Manuel test alanı:** [`docs/manuel-test/03-KALICILIK-POSTGRESQL.md`](../../manuel-test/03-KALICILIK-POSTGRESQL.md) ve [`docs/manuel-test/04-KALICILIK-DIGER.md`](../../manuel-test/04-KALICILIK-DIGER.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 4a9e1b9:docs/arsiv/fazlar/94-SQL-TEK-KAYNAK.md
> ```
>
> Damıtıldı 2026-08-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz, üç SQL dialect'inde **elle tekrarlanan** metni tek kaynağa indirir. İki eksende çalışır ve ikisi de ölçülmüştür. - **Eksen 1 — özdeş sorgular.** 199 ortak sorgudan **117'si** (%59) üç dialect'te aynıdır; bugün hiçbiri paylaşılmıyor. - **Eksen 2 — tekrarlanan ifadeler.** Maliyet toplama ifadesi **18 yerde** elle yazılıdır (dialect başına 6).

## Bitiş Ölçütleri (DoD)

- [x] Faz başında üç dialect'in 199 sorgusu için anlık görüntü taban çizgisi üretildi (`tests/AgentPrism.Sql.Shared.UnitTests/Baselines/sql-text-baseline.*.txt`, 200 satır/dosya = 199 sorgu + `Schema`); commit henüz yapılmadı (kullanıcı istemedikçe commit edilmiyor, `AGENTS.md` kuralı)
- [x] Faz sonunda `SqlTextSnapshotTests` üç sağlayıcıda da **sıfır fark** verir — 8/8 test, defalarca koşuldu
- [x] Özdeş sorgular `SqlQueriesBase`'e taşındı; taşınan sorgu dialect dosyalarında **kalmadı** (ölçüm: 115 sorgu — plan 117 diyordu, bkz. Plandan Sapmalar #1 — Postgres 2282→1476, SQL Server 2477→1710, SQLite 2241→1468 satır; taban 717→1685)
- [x] Maliyet toplama ifadesi tek kaynaktan üretilir; `CostAddends` listesine sahte bir terim eklemek üç dialect'in metnini birden değiştirir — GERÇEKTEN denendi (`CostAddendsCrossCheckTests` düştü, mesaj doğru), geri alındı
- [x] Token ağaç toplamları tek kaynaktan üretilir (`TreeSum`); cache toplamının `COALESCE(…, 0)` **almadığı** `coalesceToZero: false` parametresiyle korunur — `RunStoreContract`'ın mevcut testleri (3 gerçek veritabanı) davranışı doğruluyor
- [x] `runs` sütun sırası tek listede (`RunColumnOrder`, 53 kalem); `SqlRunStore`'un runs okuyucusu adlandırılmış sabit (`RunOrdinals.X`) kullanır, sabit ordinal **kalmadı** — **kısmi**: sıra listesi SQL metnini üretmez, yalnız ordinal sırasını belgeler (bkz. Plandan Sapmalar #3, bilinçli kapsam daraltması)
- [x] `SqlQueryCompletenessTests` üç sağlayıcıda koşar; SQLite'ın `UpgradeMigrationsTable` muafiyeti gerekçesiyle listede — GERÇEKTEN denendi (`UpsertMcpServer`'ı `string.Empty` yapıp koşuldu, düştü, geri alındı)
- [x] Sözleşme testleri (`tests/Shared/Contracts/`) üç sağlayıcıda da yeşil — `AgentPrism.PostgreSql.IntegrationTests` 638/638, `AgentPrism.SqlServer.IntegrationTests` 574/574, `AgentPrism.Sqlite.IntegrationTests` 592/592 (hepsi gerçek veritabanına/dosyaya karşı, bu oturumda 94.2/94.3/94.4/94.4.3'ün her adımından sonra tekrar koşuldu)
- [x] SQLite tablo nitelendirmesi **noktasız** kalır — `sql-text-baseline.sqlite.txt` elle kontrol edildi (`agentprism_mcp_servers`, nokta yok); `AgentPrism.Sqlite.IntegrationTests` yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 7f8ee9b` TAM koşuldu (tarama, dokuman-bakim, scripts unittest, agent-map, denetim-paketi, `dotnet build`, `dotnet test AgentPrism.slnx` [tüm proje ağacı], `dotnet pack`, `dotnet format --verify-no-changes`, `docs-site npm run check`) — hepsi ✅, çıkış kodu 0
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — PostgreSQL (`ap-pg`, taze `agentprism_p94` şeması) ile, bkz. `docs/manuel-test/03-KALICILIK-POSTGRESQL.md` MT-PG-067
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` kapanış koşumunun parçasıydı, ✅
- [x] Manuel kabul case'leri `docs/manuel-test/03-KALICILIK-POSTGRESQL.md` (MT-PG-067) ve `04-KALICILIK-DIGER.md` (MT-SQL-074, MT-SQL-075) içine eklendi; otomatikleştirilebilenlerin TAMAMI gerçekten koşuldu (bu sahte-hata-enjeksiyonları dahil)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. "Denetim Bulguları"
- [x] `docs/hafiza/sql-saglayicilari.md` güncellendi: yeni sorgu **nereye** yazılır (özdeşse tabana, farklıysa dialect'e) ve dördüncü maliyet terimi **nereden** eklenir (`CostAddends`)

### Doğrulama komutları

```bash
# Üç dosyanın satır sayısı (faz öncesi 2477 / 2282 / 2241)
wc -l src/AgentPrism.SqlServer/Internal/SqlServerQueries.cs \
      src/AgentPrism.PostgreSql/Internal/PostgresQueries.cs \
      src/AgentPrism.Sqlite/Internal/SqliteQueries.cs

# Maliyet ifadesi kaç yerde elle yazılı (faz öncesi 6+6+6)
grep -c "COALESCE(SUM(input_cost), 0)" \
  src/AgentPrism.PostgreSql/Internal/PostgresQueries.cs \
  src/AgentPrism.SqlServer/Internal/SqlServerQueries.cs \
  src/AgentPrism.Sqlite/Internal/SqliteQueries.cs

# Sabit ordinal kaldı mı (runs okuyucusunda)
grep -n "reader, [0-9]\+\|IsDBNull([0-9]\+)" src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs | sed -n '1,40p'

# Sözleşme testleri — üç sağlayıcı
dotnet test tests/AgentPrism.PostgreSql.IntegrationTests -c Release
dotnet test tests/AgentPrism.SqlServer.IntegrationTests -c Release
dotnet test tests/AgentPrism.Sqlite.IntegrationTests -c Release
```

---

## Plandan Sapmalar

1. **117 değil 115 sorgu özdeş çıktı — ölçüm yöntemi düzeltildi.** İlk ölçüm
   (94.3 Adım 1) RAW C# KAYNAĞINI karşılaştırıyordu (yalnız boşluk/yorum
   normalize edilerek). Bu yöntem 4 sorguyu (`InsertApiKey`, `TouchRunHeartbeat`,
   `UpdateRunCompletion`, `UpdateRunCost`) yanlışlıkla özdeş saydı — SQL
   Server'ın `UPDATE`/`SET`/`WHERE` hizalama boşluğu ÇÖZÜMLENMİŞ metinde gerçek
   bir fark yaratıyordu (`   SET` vs `SET`). Ters yönde 2 sorgu (`SelectAttachment`,
   `SelectToolApprovalRules`) kaynakta farklı göründü ama çözümlenmiş metni
   özdeşti. Ölçüm, üç `*Queries` tipini örnekleyip ÇÖZÜMLENMİŞ metni
   karşılaştıracak şekilde yeniden yapıldı (bu artık `SqlTextSnapshotTests`'in
   kendisi) — 94.6'nın "davranış değil metin" ilkesiyle tutarlı, çünkü taşınan
   metin BİREBİR aynı kalmalıdır, "eşdeğer" değil.
2. **Paylaşılan sütun listeleri (`mcpServerColumns` gibi) plan dokümanında hiç
   geçmiyordu ama taşımayı bloke etti.** 15 sorgu `const string XColumns`
   yerel değişkenlerine (üç dialektte de aynı değerde) atıfta bulunuyordu; bu
   değişkenler HEM taşınan HEM kalan sorgular tarafından paylaşılıyordu.
   Çözüm: bu 15 liste `protected const string` olarak tabana taşındı
   (`McpServerColumns` gibi, ilk harf büyütülerek); kalan dialekt sorguları
   inherited üye olarak görmeye devam ediyor, hiçbir "farklı" sorgunun metni
   değişmedi.
3. **94.4.3 (`RunColumnOrder`) planlanandan DAR kapsamda uygulandı — bilinçli
   karar, Açık Soru 5'in cevabı.** Plan, `RunColumnOrder`'ın (isteğe bağlı
   olarak) üç dialektin `runColumns` SQL METNİNİ üretmesini hayal ediyordu.
   Ölçüldü: bir `Tree` kalemi PostgreSQL/SQL Server'da bir `LEFT JOIN LATERAL`
   alias'ından (`tree.foo`), SQLite'ta TAMAMEN AYRI bir ilişkili alt sorgudan
   gelir — bu, isim listesi düzeyinde birleştirilemeyen, gerçek bir SQL YAPISI
   farkıdır (ORM yasağıyla aynı gerekçe: L16/L29). Uygulanan: `RunColumnOrder`
   yalnızca SIRA sözleşmesinin belgeli kaydı; `RunOrdinals` adlandırılmış
   sabitleri bu sıradan TÜRETİLMEMİŞ (elle yazılı, `RunOrdinalsCrossCheckTests`
   ile ona karşı DOĞRULANMIŞ). Sonuç: yeni bir `runs` sütunu hâlâ DÖRT yer
   ister (üç `runColumns` metni + `RunColumnOrder`), ama bir uyuşmazlık artık
   derleme zamanında yakalanır, çalışma anında değil. DoD'un ilgili satırı bu
   yüzden **kısmen** karşılanmıştır; aşağıda işaretlenmiştir.
4. **`TokenAddends` eklenmedi.** Plan, `CostAddends`'e paralel bir
   `TokenAddends` listesi öneriyordu. Uygulama sırasında hiçbir çağrı yerinin
   böyle bir listeyi TÜKETMEDİĞİ görüldü — her `TreeSum` çağrısı zaten kendi
   sütun adını taşıyor, ayrı bir liste hiçbir tekrarı azaltmıyordu (YAGNI).
   Atlandı; gerekçe koda değil yalnız buraya yazıldı çünkü genişleme
   noktası değil, kullanılmayan bir sabit olurdu.
5. **`SqlQueryCompletenessTests` ve `SqlTextSnapshotTests` planın öngördüğü
   üç `*.IntegrationTests` projesi yerine YENİ, Docker'sız bir projede
   (`tests/AgentPrism.Sql.Shared.UnitTests`) toplandı — Açık Soru 3'ün ölçülen
   cevabı.** Ölçüldü: `AgentPrism.PostgreSql.IntegrationTests` gibi bir
   projeye eklenen bir test bile `[assembly: AssemblyFixture(typeof(PostgresFixture))]`
   yüzünden TÜM montaj için bir Docker konteyneri başlatır — testin kendisi
   hiç bağlantı açmasa da. Bu, 94.6'nın "anlık görüntü Docker istemez"
   şartını ihlal ederdi. Çözüm B seçildi: üç sağlayıcı projesine
   `AgentPrism.Sql.Shared.UnitTests`'e özel bir `InternalsVisibleTo` eklendi
   (K-247 sorun yaratmadı — her tip kendi somut sınıf adıyla, birleştirme
   olmadan yansıtılıyor).

## Bu Fazda Verilen Kararlar

Yok. Public API büyümedi, migration yok, kiracı/güvenlik sınırı değişmedi;
`AGENTS.md`'nin kuralı gereği (yalnız public API/uyumluluk, güvenlik, kiracı
sınırı veya kalıcı veri kararı `KARARLAR.md`'ye girer) yeni bir `K-NNN` kaydı
açılmadı. Bu fazın yerel implementasyon tercihleri (`Table()`/`QualifyTable`
devri, `RunOrdinals` tasarımı) bu dokümanda ve kod yorumlarında kalır.

## Denetim Bulguları

Bağımsız denetçi (`faz-denetim`, taze bağlamlı ayrı agent) çalışma ağacına
karşı koşuldu. İlk çağrı `isolation: worktree` ile yapıldı ve YANLIŞ sonuç
verdi — worktree izolasyonu yalnız COMMIT EDİLMİŞ durumu kopyalar, bu fazın
tüm işi commit edilmemiş çalışma ağacında durduğu için denetçi "Faz 94'ün hiç
kodu yok" diye YANLIŞ bir 🔴 üretti. İkinci çağrı izolasyonsuz, doğrudan
çalışma ağacına karşı yapıldı ve geçerli sonucu verdi.

**🔴 ve 🟡 yok.**

Denetçi 8/8 testi bizzat çalıştırdı, `no-docker.slnf`'i derledi, `BuildSharedQueries()`'in
tam 115 özelliği atadığını ve hiçbirinin dialekt dosyalarında ikinci kez
atanmadığını, `RunColumnOrder`↔`RunOrdinals` sayısal eşleşmesini, `Schema`
atamasının `BuildSharedQueries()`'den önce yapıldığını ve `SqliteDialect.QualifyTable`
ezmesinin gerçekten silindiğini bağımsız olarak doğruladı.

**🟢 (aday listesine devredildi):**

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | Planın "Planlanan Public API" bölümü bir `TokenAddends string[]` alanı listeliyordu; gerçekleşen kodda yok (`TreeSum` sütun bazlı çalışıyor, birleşik liste gerekmedi) | Gerekçelendi — bkz. Plandan Sapmalar #4; bu doküman zaten düzeltildi |
| 2 | `runColumns`'daki ağaç MALİYET sütunları (`tree.cost_input` vb.) `TreeSum(column, alias, false)` ile ifade edilebilirdi ama elle yazılı kaldı (94.4.2 kapsamı yalnız token'dı) | `docs/ADAYLAR.md` F-147 olarak devredildi |

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `SqlQueriesBase.BuildSharedQueries()` 115 sorguyu kurar; yeni bir sorgu üç
  dialektte de ÇÖZÜMLENMİŞ metin düzeyinde özdeşse oraya yazılır (kaynak
  metnine değil — bkz. Sapma 1). Paylaşılan bir sütun listesi gerekiyorsa
  `protected const string` olarak tabana konur.
- `RunOrdinals`/`RunColumnOrder` çifti `SqlRunStore`'un `runs` okuyucusu için
  geçerli; diğer okuyucular (`ReadOrphanedRun`, tool/olay/istatistik — 176
  toplam ordinal okumasının geri kalanı) hâlâ çıplak ordinal kullanır. Onlara
  dokunmak bu fazın kapsamı DIŞINDAYDI.
- `tests/AgentPrism.Sql.Shared.UnitTests` Docker'sız SQL doğruluk testleri
  için kalıcı bir yerdir; yeni bir dialekt-bağımsız SQL kontrolü oraya eklenir.

**Bilinen tuzaklar (🚨):**
- 🚨 `sed -i.bak` + `mv .bak dosya` ile bir sahte-kusur enjeksiyonunu geri
  alırken `dotnet build` mtime yüzünden YENİDEN DERLEMEYEBİLİR ve bir önceki
  (sahte kusurlu) derlemeyi sessizce test edebilirsin. Vaka ve kural:
  `docs/hafiza/test-kosum-tuzaklari.md`.
- 🚨 Bu makinedeki `ap-pg` konteynerinin `agentprism` şeması ESKİ bir migration
  checksum'ı taşıyor (`0032_tenant_provider_bindings` — muhtemelen Faz 57/58
  civarındaki bir doküman/dil düzeni geçişinde metin değişti, checksum
  kaydedildikten SONRA). Bu fazdan BAĞIMSIZ bir ortam durumu; migration
  dosyasına bu oturumda dokunulmadı (`git diff` boş). Etkilenen konteynerde
  yeni bir `run` denemeden önce farklı bir şema adı kullan veya konteyneri
  tazele.

**Yarım kalan iş:** 94.4.3 kapsamı Sapma 3'te açıklandığı gibi bilinçli
olarak daraltıldı (SQL metin üretimi değil, yalnız ordinal sırası tek kaynağa
indi). Bu bir eksik değil, ölçülmüş bir tasarım kararıdır — DoD tablosunda
ilgili satır bu gerekçeyle işaretlenmiştir.

**Sıradaki faz:** Yok — `docs/ADAYLAR.md`'den yeni bir `F-NN` seçilmeli.

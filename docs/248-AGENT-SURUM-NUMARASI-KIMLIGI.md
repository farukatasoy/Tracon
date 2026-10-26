# Faz 248 — Agent Sürüm Numarası Silmede Yeniden Kullanılmaz

> **Durum:** 📋 Planlandı (2026-10-26)
> **Kademe:** tam — migration (üç sağlayıcı, yeni tablo) ve kalıcı veri; `IAgentDefinitionStore` numaralama kuralı yayınlanan sözleşme testine girer (tüketicinin kendi deposu yeni case'i geçmelidir); güvenlik: eski bir sürüm sabiti bugün silip yeniden oluşturulan agent'ın başka içeriğini sessizce koşturabilir
> **Plan onayı:** Bakımcı, 2026-10-26 (sohbette alınan karar: sayaç tablosu — geçmiş silmede bugünkü gibi silinir, sayaç kalır; eski döneme işaret eden sabit kapalı düşer)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-409**
> **Genellik:** Veritabanı agent'ını silip aynı adla yeniden oluşturan ve bir sürüm sabitine dayanan her kurulum: workflow düğüm sabiti (K-989), onay devamı (K-895 a), loop reviewer sabiti (`LoopCriterion.ReviewerAgentVersion`), deney kolları (`0010_experiments.sql:33`: `{"name","version","weight"}`) ve Faz 249/250'nin yeni sabitleri. İkinci kanıt ilk ilkedir: sürüm numarası bir kimliktir; K-987 workflow geçmişinde aynı kuralı koydu
> **Karşı görüş:** "Silip yeniden oluşturmak nadirdir; içerik özeti (K-811) önbelleği zaten korur." Cevap: K-811 yalnız derleme önbelleğini korur; sabitler numarayla çözülür. Yeniden oluşturulan agent üç kayıt sonra eski `v3`'e ulaşır ve eski sabit yeni içeriği koşturur — onay devamı başka bir talimatla sürer. Sayaç tablosu küçük bir migration'dır ve silme sözleşmesini değiştirmez
> **Önkoşul:** Yok. [Faz 249](249-RUN-PARAMETRESIYLE-AGENT-SURUMU.md) ve [Faz 250](250-ALT-AGENT-SURUM-SABITI.md) bu faza dayanır: yeni sabitler güvenli bir kimlik ister
> **Paketler:** `Tracon.PostgreSql`, `Tracon.SqlServer`, `Tracon.Sqlite` (migration + SQL metni), `Tracon.Sql.Shared` (`SqlAgentDefinitionStore`), `Tracon.Core` (`InMemoryAgentDefinitionStore`), `Tracon.Abstractions` (`IAgentDefinitionStore` XML), `Tracon.Testing.Contracts.Xunit` (`AgentDefinitionStoreContract`)
> **Yeni paket:** Yok · **Migration:** gerekli — üç sağlayıcıda yeni tablo + geriye dönük doldurma; numara uygulama anında alınır (bugün son: PostgreSQL `0064`, SQL Server `0052`, SQLite `0051`)
> **Public API:** Büyümüyor — tip yüzeyi aynı. Değişen sözleşmedir: `SaveAsync` silinmiş bir adın numarasını sürdürür. `wc -l src/*/PublicAPI.Shipped.txt`: 18 dosya × 1 satır (2026-10-26)
> **Tüketici yüzeyi:** site `concepts/agents.md` (sürümler ve silme), `guides/testing.md` (sözleşme testi listesi, varsa) · sevk edilen: `IAgentDefinitionStore.SaveAsync`/`DeleteAsync` XML, `Tracon.Testing.Contracts.Xunit` yeni case, `CHANGELOG.md` `[Unreleased]` → *Changed* (kendi `IAgentDefinitionStore`'unu yazan tüketici yeni case'i geçmeli) ve *Fixed* (eski sabit yeni içeriği koşturabiliyordu)
> **Manuel test alanı:** `docs/manuel-test/07-HTTP-YONETIM-API.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-987 —\|K-988 —\|K-989 —\|K-895 —\|K-811 —\|K-178 —" docs/KARARLAR.md
   ```
   **K-987** (workflow geçmişinde numara kimliktir, silmede kalır — bu fazın
   emsali, ama burada geçmiş değil yalnız sayaç kalır), **K-988** (workflow kök
   satırı sürümü damgalar), **K-989** (düğüm agent sabiti), **K-895** (onay
   devamı kaynak run'ın sürümünü koşar), **K-811** (derleme önbelleği içerik
   özetini kullanır, silip yeniden oluşturma yüzünden), **K-178** (migration
   numaraları sağlayıcı başına bağımsızdır)
3. Önceki fazdan devir: yok
4. Alan hafızası:
   [`hafiza/sql-migration.md`](hafiza/sql-migration.md) (migration bütünlüğü, kilit) ·
   [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md) (paylaşılan SQL katmanı) ·
   [`hafiza/sqlite.md`](hafiza/sqlite.md) (`NOT NULL` + `DEFAULT` üç sağlayıcıda aynı) ·
   [`hafiza/agent-derleme-ve-onbellek.md`](hafiza/agent-derleme-ve-onbellek.md) (`:100` run kaydındaki `AgentVersion` sabitlenebilir sürüm demek değildir)
5. Gerektiğinde: [`MIMARI.md`](MIMARI.md) § veri modeli (agent tabloları)

---

## Amaç

Bir veritabanı agent'ı silinip aynı adla yeniden oluşturulunca sürüm numarası
1'den başlar. Eski numaraya işaret eden her sabit — workflow düğümü, onay devamı,
reviewer, deney kolu — yeni dönemin aynı numaralı sürümüne ulaşınca **başka
içeriği** sessizce koşturur.

- **F-409 (numara kimliği yarısı)** — sürüm numarası ad başına hiç yeniden
  kullanılmaz. Silme bugünkü gibi tanımı ve geçmişini siler; ad başına bir sayaç
  satırı kalır. Yeniden oluşturulan agent numaralamayı sürdürür. Eski döneme
  işaret eden sabit "sürüm yok" diye kapalı düşer.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`PostgresQueries.cs:68-76`](../src/Tracon.PostgreSql/Internal/PostgresQueries.cs) | `INSERT` dalı `version` olarak `1` yazar; `ON CONFLICT` dalı `+1` yazar. SQLite `SqliteQueries.cs:129-137`, SQL Server `SqlServerQueries.cs:152-165` aynıdır |
| [`0001_initial.sql:49-57`](../src/Tracon.PostgreSql/Migrations/0001_initial.sql) | `agent_definition_versions.agent_id` → `agent_definitions (id) ON DELETE CASCADE`; geçmiş silmeyle gider (SQL Server `0001:78-87`, SQLite `0001:70-78`) |
| [`SqlAgentDefinitionStore.cs:193`](../src/Tracon.Sql.Shared/Stores/SqlAgentDefinitionStore.cs) | Kayıt zaten `ExecuteInTransactionAsync` içindedir — sayaç aynı işleme girebilir |
| [`InMemoryAgentDefinitionStore.cs:121,131-136`](../src/Tracon.Core/Storage/InMemoryAgentDefinitionStore.cs) | `history.Count == 0 ? 1 : last + 1`; `DeleteAsync` bütün geçmişi `TryRemove` eder |
| [`IAgentDefinitionStore.cs:59-63`](../src/Tracon.Abstractions/Agents/IAgentDefinitionStore.cs) | "Deletes the definition and every one of its versions" — numaralama hakkında söz yok |
| [`AgentDefinitionStoreContract.cs:123`](../src/Tracon.Testing.Contracts.Xunit/Contracts/AgentDefinitionStoreContract.cs) | `DeleteAsync_removes_all_versions` yayınlanan sözleşmede; numara sürekliliği case'i yok |
| [`WorkflowAgentVersionPinTests.cs:209`](../tests/Tracon.AspNetCore.FunctionalTests/WorkflowAgentVersionPinTests.cs) | Agent'ı silip yeniden oluşturur; numara yeniden başlar. Test yalnız `v3` henüz olmadığı için düşer — üç kayıt sonra sabit başka içeriği koşturur |
| [`SourceRunAgentResolver.cs:46-52`](../src/Tracon.Core/Catalog/SourceRunAgentResolver.cs) | Onay devamı kaynak run'ın numarasını `ResolveAsync(agentName, version)` ile çözer |

> Kanıtlar 2026-10-26 tarihinde doğrulandı.

---

## 248.1 — Sayaç tablosu

Yeni tablo (ad uygulamada; öneri `agent_definition_version_counters`):
`tenant_id`, `name`, `last_version`, `updated_at`; birincil anahtar
`(tenant_id, name)`. `agent_definitions`'a yabancı anahtar **yoktur** — satır
silmeden sonra yaşamak için vardır.

- Satır yalnız ad ve sayı taşır; agent içeriği taşımaz. Veri sahibi silmesinin
  hedefi değildir (kişisel veri yok); `DataSubjectCoverageTests` tabloyu muaf
  olarak gerekçesiyle adlandırır.
- Kiracı kapıları tabloyu görür: `tenant_id` her sorguda (`TenantCoverageTests`
  ve kiracı yalıtım sözleşmesi). Uygulamada kapıları bul:
  `grep -rln "agent_definition_versions" tests/`.

## 248.2 — Kayıt

Kayıt işlemi (`ExecuteInTransactionAsync`) sayacı da yazar:

1. Sayaç satırını kilitleyerek oku (sağlayıcıya özgü: PostgreSQL `INSERT … ON
   CONFLICT … RETURNING`, SQL Server `UPDLOCK`, SQLite tek yazar).
2. Yeni numara = `sayaç + 1` (satır yoksa `1`).
3. `agent_definitions.version` ve geçmiş satırı bu numarayı alır; sayaç yeni
   numaraya yazılır.

`RollbackAsync` de yeni bir sürüm üretir ve aynı yoldan geçer — uygulamada ölçülür.
SQL metni üç sağlayıcıda değişir; `Tracon.Sql.Shared.UnitTests` SQL metin tabanları
(`Baselines/sql-text-baseline.*.txt`) yenilenir.

## 248.3 — Silme

`DeleteAsync` bugünkü gibi tanımı ve geçmişi siler (cascade). Sayaç satırı kalır.
XML: "The next save under the same name continues the numbering; a version
number is never reused for a name." Bu bir sözleşme cümlesidir ve yayınlanan
sözleşme testine girer (248.5).

## 248.4 — Migration

Üç sağlayıcıda yeni tablo + geriye dönük doldurma: mevcut her
`agent_definitions` satırı için `(tenant_id, name, version)` sayaç olarak yazılır.
SQLite'ta tablo yeniden kurulmaz (geçmiş tablosuna dokunulmaz).
Zaten silinmiş ve yeniden oluşturulmuş agent'ların eski dönemi geri getirilemez —
migration yalnız bugünkü en yüksek numarayı korur.

## 248.5 — Sözleşme

`AgentDefinitionStoreContract`'a yeni case (öneri adı
`SaveAsync_after_DeleteAsync_continues_the_numbering`): kaydet ×3 → sil → kaydet →
sürüm `4`; `GetVersionAsync(name, 1)` `null`. Bellek içi depo ve üç SQL sağlayıcısında
koşar. Bellek içi depo silmede sayacı tutar.

## 248.6 — Eski sabitler kapalı düşer

Yeni numaralama ile eski döneme işaret eden sabit "sürüm yok" alır. Bugünkü
davranış zaten budur (K-989, K-1007: çalıştırmadan önce adıyla düşer); değişen,
düşmenin artık **her zaman** olmasıdır. `WorkflowAgentVersionPinTests` yeni bir
case alır: `v2`'ye sabitle → sil → yeniden oluştur (yeni `v4`) → run sabit adıyla
düşer.

---

## Planlanan Public API

Tip yüzeyi değişmez. XML değişir:

```csharp
// Tracon.Abstractions — IAgentDefinitionStore
/// SaveAsync: "…The version is one more than the highest version this name ever had,
///  including versions of a definition that was deleted."
/// DeleteAsync: "…The numbering is kept: the next save under this name continues it."
```

Yayınlanan `Tracon.Testing.Contracts.Xunit` yeni bir case kazanır (248.5).

### HTTP `endpoint`'leri

Değişmez. `DELETE /api/agents/{name}` ve `POST /api/agents` davranışı yalnız dönen
sürüm numarasında farklıdır.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.PostgreSql/Migrations/00NN_agent_version_counters.sql   (numara uygulamada)
src/Tracon.SqlServer/Migrations/00NN_agent_version_counters.sql
src/Tracon.Sqlite/Migrations/00NN_agent_version_counters.sql
src/Tracon.{PostgreSql,SqlServer,Sqlite}/Internal/*Queries.cs
src/Tracon.Sql.Shared/Internal/SqlQueriesBase.cs
src/Tracon.Sql.Shared/Stores/SqlAgentDefinitionStore.cs
src/Tracon.Core/Storage/InMemoryAgentDefinitionStore.cs
src/Tracon.Abstractions/Agents/IAgentDefinitionStore.cs            (XML)
src/Tracon.Testing.Contracts.Xunit/Contracts/AgentDefinitionStoreContract.cs
tests/Tracon.Sql.Shared.UnitTests/Baselines/sql-text-baseline.*.txt
tests/Tracon.Sql.Shared.UnitTests/DataSubjectCoverageTests.cs       (muafiyet)
tests/Tracon.AspNetCore.FunctionalTests/WorkflowAgentVersionPinTests.cs
docs-site/src/content/docs/concepts/agents.md
CHANGELOG.md
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Sil + yeniden oluştur → sürüm `1` (**önce kırmızı**) | Sözleşme (bellek içi + üç SQL) | `AgentDefinitionStoreContract` yeni case |
| Eski sabit yeni dönemin içeriğini koşturur | Fonksiyonel | `WorkflowAgentVersionPinTests` yeni case |
| Onay devamı eski döneme ait run'ı yeni içerikle sürdürür | Fonksiyonel | onay devamı testi (`SourceRunAgentResolver` yolu; sınıf uygulamada bulunur) |
| Eşzamanlı iki kayıt aynı numarayı alır | Sözleşme | aynı sözleşmede eşzamanlı kayıt case'i (üç SQL sağlayıcısında) |
| Bir kiracının sayacı ötekini etkiler | Sözleşme (kiracı yalıtımı) | `AgentDefinitionStoreContract` iki kiracılı case + `TenantCoverageTests` |
| Migration mevcut agent'ın numarasını sıfırlar | Entegrasyon (her sağlayıcı) | migration testi: `v5` olan agent → migration → kayıt `v6` |
| `RollbackAsync` sayacı atlar | Sözleşme | mevcut rollback case'i + sayaç iddiası |
| Veri sahibi kapsam kapısı yeni tabloyu adlandırmaz | Birim (mimari) | `DataSubjectCoverageTests` |
| SQL metin tabanı bayatlar | Birim | `Tracon.Sql.Shared.UnitTests` taban testleri |

Beş soru: **iptal** — kayıt işlemi iptalde geri alınır (sayaç dahil) ·
**eşzamanlılık** — sayaç satırı kilidi (tablo) · **boş/aşırı girdi** — ad
uzunluğu bugünkü `agent_definitions.name` sınırını izler · **başka kiracı** —
tablo · **alt sistem hatası** — sayaç yazımı düşerse kayıt da düşer (aynı işlem).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 🤖 | `samples/Tracon.Api` + PostgreSQL | `POST /tracon/api/agents` ×2 → `DELETE /tracon/api/agents/x` → `POST` | Son yanıt `version: 3`; `GET …/versions` yalnız `3`'ü listeler |

Case `docs/manuel-test/07-HTTP-YONETIM-API.md`'ye eklenir.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Sayaç satırı kiracı silindiğinde (varsa kiracı silme yolu) gider mi | A: kiracının bütün satırlarıyla gider · B: kalır | A — kiracı yoksa ad alanı da yoktur; uygulamada kiracı silme yolu ölçülür |
| 2 | Migration'dan önce silinip yeniden oluşturulmuş agent'lar için uyarı | A: sürüm notunda bir cümle · B: yok | A |

---

## Kapsam Dışı

- Alt agent sabiti — [Faz 250](250-ALT-AGENT-SURUM-SABITI.md).
- Skill sürüm sabiti ve skill geçmişi — K-082 (kullanıcı kararı) gereği F-408'e bağlı; skill sayacı da silmede yeniden başlar ama geçmiş tutulmadığı için sabit yoktur (F-698).
- Geçmişi silmeden sonra saklamak (K-987 eşi) — reddedildi (kullanıcı kararı, 2026-10-26): silinen agent'ın içeriği kalırdı ve `DeleteAsync` sözleşmesi değişirdi.

---

## Bitiş Ölçütleri (DoD)

- [ ] Sil + yeniden oluştur numaralamayı sürdürür; sözleşme case'i bellek içi depoda ve üç SQL sağlayıcısında yeşil
- [ ] Eski döneme işaret eden sabit run'ı adıyla düşürür (fonksiyonel)
- [ ] Migration üç sağlayıcıda mevcut numarayı korur (entegrasyon)
- [ ] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`)
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1 + sabitli bir run), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] `🤖` case `docs/manuel-test/07-HTTP-YONETIM-API.md` içinde ve koşuldu; gerisi otomatik testte
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz; `CHANGELOG.md` *Changed* + *Fixed* satırları yazıldı
- [ ] Karar `docs/KARARLAR.md`'ye yazıldı (`kalıcı-veri`; reddedilen alternatif: geçmişi silmede saklamak)

### Doğrulama komutları

```bash
python3 scripts/kapi.py test --proje Tracon.Sqlite.IntegrationTests --sinif "*AgentDefinitionStore*"
curl -s -X DELETE http://localhost:5081/tracon/api/agents/x && curl -s -X POST http://localhost:5081/tracon/api/agents -d @agent.json | jq .version
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Tüketicinin kendi `IAgentDefinitionStore`'u yeni sözleşme case'ini geçemez | `CHANGELOG` *Changed*; XML kuralı açık yazar; `rc.1` öncesi serbest |
| Sayaç yazımı kayıt işlemini yavaşlatır | Tek satırlık birincil anahtar yazımı; aynı işlem |
| SQL Server upsert'i iki oturumda aynı numarayı verir | `UpsertRequiresTransaction` deseni + eşzamanlı sözleşme case'i |
| Migration sırası: sayaç tablosu doldurulmadan kayıt gelir | Migration açılışta, kayıttan önce koşar (göç kilidi); doldurma aynı migration'dadır |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Plan ile gerçek arasındaki fark **gizlenmez**. Kapanışta
> kapanış commit mesajına taşınır.

- **Public API büyüdü (plan: "Büyümüyor").** Sözleşme sınıfındaki her `[Fact]` public bir yöntemdir:
  `Tracon.Testing.Contracts.Xunit/PublicAPI.Unshipped.txt` yedi satır aldı (`RS0016` ilk derlemede kırdı).
  Tip yüzeyi gerçekten değişmedi; sevk edilen sözleşme yedi case büyüdü.
- **`DataSubjectCoverageTests` muafiyeti yazılmadı (plan: tabloyu muaf adlandırır).** Kapı yalnız
  `session_id`/`run_id`/`conversation_id` sütunlu tabloyu ister; sayaç tablosunda öyle sütun yok, kapı onu
  zaten görmez. Gereksiz muafiyet kapıyı kirletirdi.
- **`TenantCoverageTests`'e dokunulmadı.** Hiçbir yeni `Sql*Store` public metodu yok; kiracı yalıtımı
  `One_tenants_delete_does_not_move_another_tenants_numbering` sözleşme case'iyle kanıtlandı.
- **Eşzamanlı kayıt case'i sil → kaydet sınırını da geçer** (plan: yalnız eşzamanlı kayıt). İlk kayıt INSERT dalına,
  gerisi UPDATE dalına gider; silmeden sonraki dalın da sınanması için case silmeden sonra koşar.
- **248.6 yeni pin case'i** `v2`'ye sabitle → sil → yeniden oluştur + güncelle: yeni dönem `v3`/`v4` alır, `v2`
  hiçbir şeye işaret etmez (plan "yeni `v4`" dedi). Fark ölçülebilir olan şudur: eski davranışta yeni dönemin
  ikinci kaydı `v2` olurdu ve sabit onun metnini koşardı. Onay devamı case'i de eklendi (planın tablosu istiyordu):
  eski depoda koşu `Completed` olup yeni talimatı çalıştırıyordu.
- **Bellek içi depo:** `DeleteAsync` sözlük girdisini silmez, girdinin sürümlerini boşaltır; sayaç aynı nesnede
  durur. Bu aynı zamanda eski bir yarışı kapatır (silinen listeye yazan eşzamanlı kayıt kaybolurdu). `RollbackAsync`
  boş girdide "agent bulunamadı" der.
- **Açık Soru 1 (kiracı silinince sayaç gider mi):** `ITenantStore.DeleteAsync` "kiracının verisi silinmez" der ve
  kiracı verisini silen yol yok; sayaç, kiracının öteki satırlarıyla kalır. Öneri A'nın gerekçesi ("kiracı yoksa ad
  alanı da yoktur") bu yüzden geçersizdi.
- **Açık Soru 2 (sürüm notu):** A — `CHANGELOG` *Changed* ve site `persistence.md` bir cümle taşır.
- **Sayaç sorgusu agent satırının sürümünü de okur (plan: yalnız `sayaç + 1`).** İlk uygulama planı izledi ve
  denetim 🔴 buldu: göçten sonra ayakta kalan **eski** bir düğüm agent satırını ve geçmiş satırını yazar, sayaca
  dokunmaz; sonraki kayıt sayaçtan eski numarayı alır, geçmişin `UNIQUE (agent_id, version)` anahtarına çarpar ve o
  adın **her** kaydı kalıcı olarak düşerdi. İki sürümün aynı anda ayakta kaldığı pencere desteklenen biçimdir
  (`guides/production.md` § *Two versions are up during a rolling upgrade*); "göç kilidi bunu dışlar" yanlıştı,
  kilit yalnız göç koşucularını sıralar. Yeni kural: numara = `max(sayaç, agent satırının sürümü) + 1` (PostgreSQL
  `GREATEST`, SQLite `MAX`, SQL Server `IIF`; sayaç satırı yoksa yalnız satırın sürümü + 1). SQLite'ta eski sorguya
  karşı kırmızı görüldü.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez.

- **K-1100** (`kalıcı-veri, public-api`): numara ad başına bir kez verilir; sayaç tablosu silmede kalır, geçmiş silinir.
- Kategori dışı (kapanış commit mesajına): numara `max(sayaç, satır) + 1` hesaplanır (rolling upgrade toleransı);
  bellek içi depo girdisini silmez, sürümlerini boşaltır; sayaç tablosu için `DataSubjectCoverageTests` muafiyeti yazılmaz.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı. Her satır: bulgu · seviye (🔴/🟡/🟢) · sonuç
> (düzeltildi / gerekçelendi / F-NN olarak devredildi). 🟢 kapanışta
> `ADAYLAR.md`'ye gider.

| Bulgu | Seviye | Sonuç |
|---|---|---|
| Eski düğümün agent satırını sayaca dokunmadan yazması o adın her kaydını kalıcı düşürür | 🔴 | düzeltildi — sayaç sorgusu satırın sürümünü okur; üç sağlayıcıda `A_save_continues_from_the_agent_row_…` (SQLite'ta eski sorguya karşı kırmızı) |
| Sapmalardaki "bilinen sınır" anlatımı yanlıştı ve kalıcılığı gizliyordu | 🔴 | düzeltildi — madde yeniden yazıldı |
| Public XML ve yorumlar "numaralama yeniden başlar" diyordu (`IWorkflowDefinitionStore`, `AgentDefinitionCompiler`, `CompiledAgentCache`, üç test yorumu) | 🟡 | düzeltildi |
| Eşzamanlı kayıt case'i sayacın INSERT dalını sınamıyordu | 🟡 | düzeltildi — `Concurrent_first_saves_of_a_new_name_get_distinct_versions` |
| "Sayaç + satır + geçmiş tek işlemde" iddiasını kanıtlayan test yoktu | 🟡 | düzeltildi — üç sağlayıcıda `A_save_whose_history_write_fails_leaves_the_counter_where_it_was` |
| "Backfill yeniden koşulur ve sayacı düşürmez" testi düşürmeyi sınamıyordu | 🟢 | düzeltildi — sayaç 9'a çekilir, yeniden koşuda 9 kalır |
| Aynı adlı iki kiracı tohumlanmıyordu | 🟢 | düzeltildi — `alpha` iki kiracıda |
| Onay devamı case'i nedeni doğrulamıyordu | 🟢 | düzeltildi — iletisi sabitlendi |
| `MT-API-016` adımlarında `psql` komutu yoktu | 🟢 | düzeltildi |
| `DeleteAsync_of_a_name_that_never_existed_…` ayırt edici değil | 🟢 | gerekçelendi — "etkisizlik" doğası gereği ayırt edilemez; yalnız kırılmayı ve `1` ile başlamayı sabitler |
| `DELETE` ucunun OpenAPI açıklaması numaralamadan söz etmez | 🟢 | F-705 olarak devredildi (üç üretilmiş dosya yenilenir) |

# Faz 179 — Kiracı Kimliğinin Normalleştirilmesi

> **Durum:** ✅ Tamamlandı (2026-09-19)
> **Plan onayı:** farukatasoy, 2026-09-19 (K1 · KG-034)
> **Kaynak:** [YAYIN-HAZIRLIK.md](../../YAYIN-HAZIRLIK.md) §4 "🔴 1" · §13.0 **A-1**
> **Önkoşul:** Yok — K-639'un kapattığı kardeş vaka (`provider_name`) zaten sevk edildi ve şablon odur
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.AspNetCore`, `Tracon.Sql.Shared`, `Tracon.PostgreSql`, `Tracon.SqlServer`, `Tracon.Sqlite`, `Tracon.Testing.Contracts.Xunit`
> **Yeni paket:** Yok · **Migration:** Gerekli — üç sağlayıcı için birer **guard** script'i; numaralar uygulama anında alınır
> **Public API:** Büyüyor (bir statik metot) — `wc -l src/*/PublicAPI.Shipped.txt` ölçüldü: **boş olmayan 0 satır**, yani Faz 7'den önce ucuz
> **Tüketici yüzeyi:** `docs-site/` — kiracı kimliği biçim kuralı bugün **hiçbir sayfada yok** (ölçüldü); `concepts/` altında bir kural bölümü açılır · sevk edilen: `AmbientTenantScope`, `ITenantContext`, `TraconTenancyOptions.AllowedTenants` XML'leri
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show a1d504d7:docs/arsiv/fazlar/179-KIRACI-KIMLIGI-NORMALLESTIRME.md
> ```
>
> Damıtıldı 2026-09-19 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'un **birincil yalıtım anahtarı** olan kiracı kimliği hiçbir yerde normalleştirilmiyor. Bu yüzden `acme` ile `Acme`'nin aynı kiracı mı ayrı kiracı mı olduğu **hangi veritabanını kullandığına** göre değişiyor: SQL Server'ın varsayılan collation'ı ikisini birleştirirken PostgreSQL ve SQLite ayırıyor.

## Bitiş Ölçütleri (DoD)

- [x] `X-Tracon-Tenant: Acme` ile yazılan `run`, `acme` ile listelenir — **canlı ölçüldü**, çıktı aşağıda. (Başlık adı planda `X-Tenant-Id` yazılmıştı; sevk edilen ad `X-Tracon-Tenant`'tır)
- [x] `AllowedTenants: ["acme"]` iken `ACME` **200**, `other` **403** döner — **canlı ölçüldü**, çıktı aşağıda
- [x] `TenantIsolationContract`'ın harf-kayması senaryosu **dört koşumda** yeşil — sapma 6 gereği taban yerine iki fail-open sözleşmesinde
- [x] Guard temiz veritabanında geçer, kanonik olmayan satırda **durur** — **üç sağlayıcının üçünde de** test var: SQL Server case'i CI collation altında (denetim 🔴 #1), SQLite, **ve PostgreSQL** (`TenantIdCaseGuardTests` ×3). PG'nin kendi katalog sorgusu da mutasyonla kanıtlandı
- [x] `TenantParameterChokePointTests` yeşil; `Tracon.Sql.Shared` içinde normalleştirmeyen `tenant_id` parametresi **0** (tek adlandırılmış istisna: `SqlQueriesBase.cs:133` sütun tanımlayıcısı — sapma 5)
- [x] `grep -rn "Ordinal" src/ | grep -ci tenant` **yeniden ölçüldü: 118 satır.** Tamamı kanonik-kanonik karşılaştırmadır: ezici çoğunluğu K-839'un kanonik girdi bekleyen bellek içi ailesi, kalanı `ChildAgentInvoker`'ın iki tarafı da runtime'dan gelen `scope.TenantId`/`_tenantContext.TenantId` karşılaştırması ve sözlük anahtarı sıralamaları
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü (`kapi.py tarama` ✅)
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi (**MT-SEC-194…198**); otomatikleştirilen karşılığı `TenantIdCaseTests` koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (ikisi de kapandı — Denetim Bulguları)
- [x] `docs-site/` kiracı kimliği biçim ve harf duyarlılığı kuralını **yayımlıyor** (`concepts/governance.md` "What a tenant identifier may be"); `npm run check` temiz
- [x] Egress fail-open kalemi `docs/ADAYLAR.md`'ye yazıldı (**F-254**)

### Canlı koşum kanıtı (2026-09-19, `samples/Tracon.Api`, gerçek PostgreSQL)

Kapanış iddiası kaynaktan değil **çalışan üründen** alındı. Üç ölçüm:

**1 · Harf kayması aynı kiracıya çözülür.** `X-Tracon-Tenant: Acme` ile bir
`run` yazıldı, `acme` ile listelendi:

```
POST /tracon/api/agents/support/run   X-Tracon-Tenant: Acme   -> HTTP 200
  runId 01a0b9d7-a35e-7370-b51a-2b57e3a95541
GET  /tracon/api/runs                 X-Tracon-Tenant: acme   -> HTTP 200
  { "id": "01a0b9d7-…", "tenantId": "acme", "status": "Completed", "agentName": "support" }
GET  /tracon/api/tenants/current      X-Tracon-Tenant: Acme   -> {"tenantId":"acme"}
```

Kiracı **reddedilmedi, normalleştirildi** — ve depoya kanonik hâliyle indi.

**2 · Allowlist iki tarafta da harf duyarsızdır, ama genişlemez.**
`AllowedTenants = ["acme"]` ile:

```
ACME  -> 200 {"tenantId":"acme"}      other -> 403 "Tenant rejected"
Acme  -> 200 {"tenantId":"acme"}      OTHER -> 403 "Tenant rejected"
acme  -> 200 {"tenantId":"acme"}
```

Fold, reddin kendisini zayıflatmıyor (K-382 yüzeyi).

**3 · Guard gerçek bir PostgreSQL'de koştu.** Başlangıçta katalogdan **34
tabloyu** okudu ve tek `UNION ALL` ile hepsini denetledi — elle yazılmış tablo
listesi yok, K-838'in iddia ettiği davranış bu:

```sql
SELECT 'agent_definitions' AS offending_table WHERE EXISTS (
  SELECT 1 FROM agentprism.agent_definitions
   WHERE tenant_id IS NOT NULL AND tenant_id <> lower(tenant_id))
UNION ALL SELECT 'agent_files' … (34 tablo)
```

### Doğrulama komutları

```bash
# Harf kayması artık aynı kiracıdır
curl -s -H "X-Tenant-Id: Acme" -X POST http://localhost:5081/tracon/api/agents/echo/run -d '{"input":"hi"}'
curl -s -H "X-Tenant-Id: acme" http://localhost:5081/tracon/api/runs

# Tıkaç noktası kapısı: normalleştirmeyen parametre kalmadı
grep -rn '"tenant_id"' src/Tracon.Sql.Shared/ | grep -v AddTenant
```

---

## Plandan Sapmalar

**1. Guard üç `.sql` migration'ı değil, tek bir C# adımı oldu.** Plan
`NNNN_tenant_id_case_guard.sql` ×3 öngörüyordu; Açık Soru 1 zaten "SQLite'ta
nasıl durdururuz" diye soruyordu ve cevabı B çıktı. SQLite'ta `RAISE` yalnız
trigger içinde çalışır ve dinamik SQL yoktur — 34 tabloyu dolaşan bir guard
orada SQL ile ifade edilemez. Üç prosedürel lehçe yazmak aynı kuralı üç kez
kopyalar ve SQL Server'ın `COLLATE` tuzağını üç yere dağıtırdı. Sonuç:
`TenantIdCaseGuard` + `SqlDialect.TenantIdTableCatalogSql` /
`NonCanonicalTenantIdPredicate` / `QualifyCatalogTable`, `MigrationRunner`
içinden. Tablo listesi katalogdan okunur (Açık Soru 2 → A). **Yan etki:** guard
tek seferlik değil, her migration koşumunda çalışır — kalıcı bir koruma, ama
başlangıçta bir katalog sorgusu + bir `UNION ALL` maliyeti (K-838).

**2. Bir değil iki public üye eklendi.** `Normalize`'ın yanına
`NormalizeOrNull` geldi: nullable kiracı alan sorgu tipleri (`AuditQuery`,
`JobQuery`) için gerekliydi ve store yazan tüketicinin de işine yarar.

**3. Bellek içi store'ların tamamı normalleştirilmedi — kullanıcı kararı.**
Ölçüm plandan sonra yapıldı: bellek içi ailede **82** metot kiracı parametresi
alıyor ve **116** satır `Ordinal` karşılaştırıyor. Kullanıcıya üç seçenek
sunuldu; "yalnız 11 HTTP ucu + sözleşme" seçildi. Kapsama alınan iki istisna:
`InMemoryTenantEgressPolicyStore` ve `InMemoryTenantProviderBindingStore` —
ikisinde de ıskalama **fail-open**'dır. Kalan 30 bellek içi store kanonik
girdi bekler; `ITenantContext` XML'i bunu yazar (K-839).

**4. Ölçüm planı GÜÇLENDİRDİ — tetikleyici spekülatif değilmiş.**
`PUT /api/tenants/{tenantId}/egress` kiracı kimliğini **doğrudan URL'den**
store'a yazıyor. `Acme` ile kaydedilen politika, runtime `acme` çözdüğü için
bulunamıyor ve `ModelProviderRegistry` satır yokken **hiçbir kısıt
uygulamıyor**. Planın "fail-open" iddiasının gerçek yolu budur; bu yüzden
route'tan kiracı alan 11 uç normalleştirmeye dahil edildi ve
`TenantIdCaseTests` bunu ayrı bir case olarak ölçüyor.

**6. Harf-kayması senaryosu paylaşılan `TenantIsolationContract<T>` tabanına
DEĞİL, iki türemiş sözleşmeye eklendi.** Plan §179.5 "sözleşme dört koşumda
birden çalıştığı için bu tek senaryo üç motorun ayrışmadığını tek seferde
kanıtlar" diyordu. Gerçekleşemedi: taban sözleşme bellek içi store'lar üzerinde
de koşuyor ve sapma 3 gereği onların 30'u normalleştirmiyor — senaryoyu tabana
koymak o 30 koşumu kırardı. Senaryo, ıskalaması **fail-open** olan iki store'a
kondu (`TenantEgressPolicyStoreContract`, `TenantProviderBindingStoreContract`)
ve her ikisi de dört koşumda birden geçiyor. Depolama sınırının kalanının
güvencesi `TenantParameterChokePointTests`'in metin kapısı + SQL Server ve
SQLite guard testleridir. Bağımsız denetimin 🟡 #9'u budur; kapsanmayan
store'lar için sözleşme senaryosu **F-256** olarak aday listesine yazıldı.

**5. `TenantParameterChokePointTests` bir yanlış pozitif buldu ve kapsam
daraltıldı.** `SqlQueriesBase.cs:133`'teki `new("tenant_id", RunColumnSource.Own)`
bir sütun tanımlayıcısıdır, parametre bağlama değil. Kapı bugünkü bağlama
şekillerine daraltılmadı (o hâlde yarınki şekli kaçırırdı); tek bir adlandırılmış
istisna eklendi.

**7. PostgreSQL guard testi kapanışta eklendi — denetimin 🔴 #1'i ile aynı sınıf.**
Kod donduktan sonra ölçüldü: guard'ın üç sağlayıcısından **ikisinin** testi
vardı. `PostgresDialect` kendi `TenantIdTableCatalogSql`'ini override ediyor ve
o override (diğer ikisinden farklı olarak `t.table_type = 'BASE TABLE'` filtresi
taşır) **hiçbir testten geçmiyordu** — tam olarak denetimin SQL Server predicate'i
için bulduğu şekil: katalog sorgusu boş dönerse guard temiz rapor verir ve hata
**sessizdir**. `tests/Tracon.PostgreSql.IntegrationTests/TenantIdCaseGuardTests.cs`
eklendi (2 case) ve mutasyonla kanıtlandı: katalog sorgusunun sütun adı
bozulunca test **kırmızı yanıyor** (2/2 → 1/2). SQLite case'leri bunu
karşılayamıyordu; onlar `pragma_table_info` üzerinden başka bir override'ı
koşuyor.

**8. Sevk edilen kiracı başlığının adı `X-Tracon-Tenant`.** Plan boyunca
`X-Tenant-Id` yazılmıştı; bu plan metninin hatasıydı, ürünün değil. Manuel
case'ler (MT-SEC-194…198) doğru adı taşıyor.

## Bu Fazda Verilen Kararlar

| Karar | Ne |
|---|---|
| **K-836** | Kiracı kimliği karşılaştırıcı değil DEĞER normalleştirilerek case-duyarsız olur; kural `AmbientTenantScope.Normalize` olarak public |
| **K-837** | Kanonik olmayan `tenant_id` taşıyan veritabanında migration DURUR; satırlar katlanmaz 👤 |
| **K-838** | Guard üç `.sql` yerine tek bir C# adımıdır ve tablo listesini KATALOGDAN okur |
| **K-839** | Bellek içi store'ların yalnız ikisi (fail-open olanlar) normalleştirir; kalanı kanonik girdi bekler 👤 |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan yazıldıktan sonra revize edilmedi; altı sapma uygulama sırasında doğdu ve yukarıda kayıtlı |
| Düzeltme turu sayısı | 2 (biri denetim öncesi kapı düzeltmeleri, biri denetim bulguları) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 2 / 0 / 0 — ikisi de gerçekti ve ikisi de düzeltildi |
| Fazın ürettiği regresyon | 0 ölçülen. Kapılar üç kez kızardı ve üçü de fazın kendi eksiğiydi: Turkish karakteri sevk edilen metinde, 🚨 sevk edilen XML'de, `public-surface-baseline` bir eksik |
| Faz kapandıktan sonra bulunan kusur | 1 — kapanış turunun bekleyen-kalem tahsisleri F-251'i ikinci kez kullandı ve tahsis sayacını güncellemedi; 2026-09-22'de bulundu, F-230 emsaliyle çözüldü (ADAYLAR § F-ID tahsis kuralı) |

## Denetim Bulguları

`faz-denetim` taze bağlamlı `faz-denetcisi` ile koşuldu (2026-09-19). Denetçi
ağaca yazmadı. **🔴 2 · 🟡 8 · 🟢 2.** İkisi de gerçek çıktı; on iki bulgunun
onu düzeltildi, ikisi gerekçelendi. Kayıt: [KG-035](../../YAYIN-HAZIRLIK.md).

### 🔴 — ikisi de kapandı

| # | Bulgu | Triyaj | Sonuç |
|---|---|---|---|
| 1 | 🚨 SQL Server'ın `COLLATE Latin1_General_BIN2` predicate'i **hiçbir testten geçmiyordu**. Guard'ın tek testi SQLite'tı ve orada temel yüklem zaten doğru çalıştığı için override silinse tek bir test bile kırılmıyordu — bu, fazın kendi risk tablosunun **birinci satırıydı** | **gerçek** | İhlal yolu artık gerçek SQL Server üzerinde ölçülüyor (`Tracon.SqlServer.IntegrationTests/TenantIdCaseGuardTests.cs`) ve **mutasyonla kanıtlandı**: `COLLATE` override'ı kaldırılınca test kırmızı yanıyor |
| 2 | Guard, `MigrationRunner` içindeki **tek sarmalanmamış bootstrap adımıydı** — hata yolu diğer adımların sözleşmesini izlemiyordu | **gerçek** | Adım diğerleriyle aynı sarmalamaya alındı |

### 🟡 · 🟢 — onu düzeltildi, ikisi gerekçelendi

Düzeltilen dört kalem ayrıca **mutasyonla** kanıtlandı: kiracı normalleştirme
(altı vakanın beşi), tıkaç noktası kapısı, arka plan servisi OCE davranışı ve
onay parmak izi çakışması (üç vakanın üçü).

Gerekçelendirilen iki kalem aday listesine yazıldı — kapsam dışı bırakma
bilinçlidir, gizlenmemiştir:

| Bulgu | Neden kapsam dışı | Nereye gitti |
|---|---|---|
| 🟡 #9 — harf-kayması sözleşme senaryosu paylaşılan tabana değil iki türemiş sözleşmeye kondu | Taban sözleşme bellek içi store'lar üzerinde de koşuyor; K-839 gereği onların 30'u normalleştirmiyor, senaryoyu tabana koymak o 30 koşumu kırardı (sapma 6) | [**F-256**](../../ADAYLAR.md) |
| Egress politikası satırı **yokken** hiçbir kısıt uygulanmaması | Fazın kapattığı şey tetikleyiciydi (harf kayması artık satırı ıskalamıyor); `policy is null ⇒ kısıt yok` ürünün belgelenmiş varsayılanıdır ve fail-closed yapmak ayrı bir ürün kararıdır (KG-034) | [**F-254**](../../ADAYLAR.md) |

## Sonraki Faza Devir Notu

**Bu faz bir kusur sınıfını kapattı, bir tanesini açık bıraktı.** Kiracı kimliği
artık **değer** düzeyinde kanonik (K-836) ve kural public
(`AmbientTenantScope.Normalize` / `NormalizeOrNull`). Karşılaştırıcı hiçbir
yerde değişmedi — `Ordinal` kalır, çünkü iki tarafı da kanonik olan bir
karşılaştırma doğrudur. Bu deseni bozan bir sonraki faz iki kimlik alanını iki
ayrı kurala bağlar.

**Devralınan üç sınır — hepsi ölçülmüş, hiçbiri varsayım değil:**

1. **Bellek içi ailenin 30 store'u normalleştirmez** (K-839 👤). Kanonik girdi
   beklerler ve `ITenantContext` XML'i bunu yazar. Kapsama alınan iki istisna
   (`InMemoryTenantEgressPolicyStore`, `InMemoryTenantProviderBindingStore`)
   ıskalaması **fail-open** olduğu için alındı. Bu aileye dokunan faz, önce
   sapma 3'ün ölçümünü okusun: **82** metot kiracı parametresi alıyor, **116**
   satır `Ordinal` karşılaştırıyor.
2. **Guard her migration koşumunda çalışır**, tek seferlik değil (K-838).
   Maliyeti başlangıçta bir katalog sorgusu + bir `UNION ALL`. Migration
   ekleyen faz bunu bilerek ödesin; tablo listesi katalogdan okunduğu için
   yeni tablo **otomatik** kapsama girer — elle liste güncellemesi yoktur.
3. **`TenantParameterChokePointTests` bir metin kapısıdır.** `Tracon.Sql.Shared`
   içinde normalleştirmeyen `tenant_id` parametresini yakalar ve tek bir
   adlandırılmış istisnası vardır (`SqlQueriesBase.cs:133` — sütun tanımlayıcısı,
   parametre bağlama değil). Kapı bugünkü bağlama şekillerine daraltılmadı;
   yeni bir bağlama şekli eklemek isteyen faz kapıyı **genişletir**, istisna
   listesini değil.

**Yayın hattına devir:** A-1 kapandı, `preview.1` öncesi 🔴 kalmadı. Bu fazın
açtığı kalemler yayın turunun sırasındadır: **A-26** (zaman aşımına uğrayan
workflow run'ı sebep taşımıyor — `WorkflowRunTimeoutTests` boşluğu şimdiden
kilitliyor, kapandığı gün test kırmızı döner), **A-27** (`KARARLAR.md` bütçenin
tam sınırında), **F-254**, **F-255**, **F-256**.

# Faz 108 — Bellek İçi Run Store Ayrıştırma

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`arsiv/kesif/2026-08-23-yapisal-sorun-envanteri.md`](../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 107](107-RUN-KAYIT-AKISI-AYRISTIRMA.md) — runtime writer sabitlendikten sonra onun varsayılan store'u ayrıştırılır
> **Paketler:** `AgentPrism.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `InMemoryRunStore` internal kalır; `IRunStore` sözleşmesi değişmez
> **Tüketici yüzeyi:** Yok. Store davranışı ve public sözleşme değişmez
> **Manuel test alanı:** [`manuel-test/02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md) · [`manuel-test/23-SAKLAMA-ARSIV-KOTA.md`](../../manuel-test/23-SAKLAMA-ARSIV-KOTA.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show fd91a9e:docs/arsiv/fazlar/108-BELLEK-ICI-RUN-STORE-AYRISTIRMA.md
> ```
>
> Damıtıldı 2026-08-26 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`InMemoryRunStore`, run yaşam döngüsünü, event log'unu, query/filtering'i, tree toplamlarını, istatistikleri, experiment sonuçlarını, time series ve tool usage hesaplarını 1.432 satırda taşır. Faz aynı internal sınıfı `partial` dosyalara böler. State ownership ve lock sınırları değişmez.

## Bitiş Ölçütleri (DoD)

- [x] State alanları tek owner'da kalır; yeni alt-store veya state kopyası yoktur — beş `Dictionary`/`Queue` alanı `InMemoryRunStore.cs`'te tek başına kaldı
- [x] Lifecycle, events, queries, statistics ve analytics ayrı sorumluluk dosyalarındadır
- [x] `RunStoreContract` ve `TenantIsolationContract` dört sağlayıcı koşumunda yeşildir — bellek içi 1970/1970, PostgreSQL 88/88, SQL Server 88/88, SQLite 88/88
- [x] F-148 davranışı ve aday kaydı değişmeden kalır — `AppendEventAsync`'in doğrusal `Sequence` taraması karakter düzeyinde taşındı, dokunulmadı
- [x] Public API dosyalarında fark yoktur — `PublicAPI.Shipped/Unshipped.txt` diff'i boş (tip zaten `internal`)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `scripts/kapi.py kapanis --taban 9dc53b9` yeşil
- [x] `samples/AgentPrism.Embedded` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. MT-CORE-106
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri ilgili ailelere eklendi ve otomatik olanlar koşuldu — MT-CORE-106 (`02-CEKIRDEK-VE-KATALOG.md`), MT-RET-044 (`23-SAKLAMA-ARSIV-KOTA.md`)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — iki 🟡 bulgu, ikisi de bu kapanışta kapatıldı

## Plandan Sapmalar

Plana birebir uyuldu; sorumluluk eksenleri planın 108.2 bulletlarıyla aynen
eşleşti. İki küçük yerleşim kararı, plan düz yazıyla anlattığı için burada
netleştirilir:

- `EnsureExpectedTenant` planın hiçbir bulletine açıkça girmiyordu (üç dosyada
  — `Lifecycle`, `Events` — çağrılıyor). Ana state dosyasında (`InMemoryRunStore.cs`)
  bırakıldı: `_runs`'a doğrudan erişen, tek bir sorumluluk eksenine ait
  olmayan paylaşılan bir helper.
- `IsOwnedByCurrentTenant`/`TryGetOwnedRun` de üç dosyada kullanılıyor
  (`GetRunAsync`, `ListToolInvocationsAsync`, `ReadEventsAsync`). "Query,
  tenant ownership ve tree toplamları" ekseni bunları açıkça adlandırdığı için
  `Queries.cs`'e kondu; `Events.cs` onları partial class üyesi olarak çağırır.

DoD'nin "gerçek `run`" satırı `samples/AgentPrism.Api` yerine
`samples/AgentPrism.Embedded` ile koşuldu: `AgentPrism.Api`'nin manuel test
kurulumu PostgreSQL ister (`manuel-test/02-CEKIRDEK-VE-KATALOG.md` "Koşmadan
önce"), oysa bu fazın konusu tam olarak **bellek içi** store'dur —
`AgentPrism.Embedded` bağlantı dizesi istemeden `InMemoryRunStore`'u
doğrudan ayağa kaldırır (`persistenceProvider: InMemory`).

Ayrıca plandaki dosya listesinde olmayan bir dosya eklendi:
`tests/AgentPrism.Core.UnitTests/Storage/InMemoryRunStoreStructureTests.cs`
(planın kendi "Planlanan Dosya Listesi"nde zaten adı geçiyordu, içeriği
belirtilmemişti). Denetim bu dosyanın varlığını doğrudan istemedi; hata
modları tablosunun "Trim run'ı siler ama event/tool/heartbeat kalır" ve
"Score store hata verir veya iptal olur" satırlarının karşılıksız olduğu
görülünce eklendi.

`scripts/dokuman-bakim.py --site-denetle` `cekirdek-kavram` kuralını tetikledi
(`src/AgentPrism.Core/Storage/` altındaki dosya değişikliği `concepts/`
sayfasını ister) — `--site-gerekce-yazildi` ile geçildi. Gerekçe: kural yol
tabanlı bir sezgidir, `InMemoryRunStore`'un davranışı veya sözleşmesi
değişmedi, yalnız dosya organizasyonu değişti; `concepts/`'te anlatılan hiçbir
kavram (store'un ne yaptığı, `MaxRuns`, tenant yalıtımı) bu fazla değişmedi.

## Bu Fazda Verilen Kararlar

Yok. Faz saf bir kod taşıma işiydi; public API/compatibility contract,
güvenlik/kiracı sınırı veya kalıcı veri kararı gerektiren bir seçim yapılmadı.

## Denetim Bulguları

Taze bağlamlı bir `general-purpose` agent `faz-denetim` skill'ini uyguladı
(2026-08-26). Yöntem: `git diff 9dc53b9` satır satır okundu, orijinal dosyayla
karşılaştırıldı; bağımsız olarak `dotnet build`, `dotnet test
AgentPrism.Core.UnitTests` (1970/1970) ve tam `scripts/kapi.py kapanis
--taban 9dc53b9 --site-atla` (tarama, dokuman-bakim, unittest, agent-map,
denetim-paketi, build, tam test paketi, pack, `dotnet format` — hepsi ✅)
koştu.

**🔴 yok.**

**🟡 (ikisi de bu kapanışta kapatıldı):**

1. DoD satırı "`samples/AgentPrism.Api` ile gerçek `run` yapıldı" karşılıksızdı
   — denetim, uygulayan oturumun sample koşumunu henüz belgelemediği anda
   koştuğu için bunu yakaladı. **Kapatıldı:** `samples/AgentPrism.Embedded`
   ile gerçek koşum yapıldı (bkz. Plandan Sapmalar — neden `Api` değil
   `Embedded`), MT-CORE-106 olarak belgelendi.
2. DoD satırı "Manuel kabul case'leri ilgili ailelere eklendi" karşılıksızdı.
   **Kapatıldı:** MT-CORE-106 (`02-CEKIRDEK-VE-KATALOG.md`) ve MT-RET-044
   (`23-SAKLAMA-ARSIV-KOTA.md`) eklendi.

**🟢 yok.**

**Temiz çıkan başlıklar:** 3.1 (kalan tüm DoD satırları), 3.2, 3.3, 3.4, 3.5
(imza değişikliği yok), 3.6 (yeni public API yok), 3.7, 3.8.

## Sonraki Faza Devir Notu

- Aynı satır-satır-diff denetim yöntemi (Faz 107, 108) üçüncü kez işe yaradı;
  büyük bir `partial` taşımasını doğrulamanın ucuz ve güvenilir yolu budur:
  `git show <taban>:<yol>` çıktısını `sed`'le blok blok kes, yeni dosyanın
  gövdesini (class bildirimi ile kapanış `}` arası) çıkar, `diff` al — fark
  yalnız eklenen `{`/`<inheritdoc />`/boş satırlarsa taşıma birebirdir.
- `InMemoryRunStore` artık `partial`; yeni bir sorumluluk ekseni (örn. F-148
  performans optimizasyonu) eklenirse mevcut beş dosyadan hangisine ait
  olduğuna bakılmalı, altıncı bir dosya açmadan önce.
- `samples/AgentPrism.Embedded`, bağlantı dizesi istemeyen bellek-içi
  senaryolar için `samples/AgentPrism.Api`'den daha uygun bir manuel test
  yüzeyidir — `AgentPrism.Api`'nin kendi manuel test dosyası PostgreSQL
  şart koşar.

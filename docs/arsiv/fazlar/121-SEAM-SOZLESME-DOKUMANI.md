# Faz 121 — Seam Sözleşme Dokümanı ve Küçülen Taban Çizgisi

> **Durum:** ✅ Tamamlandı (2026-08-28)
> **Kaynak:** [YAYIN-HAZIRLIK.md](../../YAYIN-HAZIRLIK.md) §13 kulvar 3 — BL-024 · BL-026 · BL-028 · BL-029 · BL-035 · BL-038 · BL-042 · BL-043 · BL-046 · BL-048 · BL-050
> **Önkoşul:** Yok. [Faz 120](120-JOB-SOZLESMESI-AT-LEAST-ONCE.md) aynı işin tek arayüzde yapılmış hâlidir; deseni oradan al
> **Paketler:** `Tracon.Abstractions` (birincil), `Tracon.Workflows`, `Tracon.Core` (yalnız yorum/karşılaştırma)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — bu faz yalnız XML dokümanı yazar. `PublicAPI.Unshipped.txt` dosyaları **değişmemelidir**; değişirse imza kaymıştır ve bu bir hatadır
> **Tüketici yüzeyi:** `docs-site/src/content/docs/api/*` **üretilir** (DocFX, XML'den) — bu fazın çıktısı doğrudan oraya basılır. El yazısı sayfa: `docs-site/src/content/docs/extend/` altındaki seam rehberleri gözden geçirilir · sevk edilen: `Tracon.Abstractions` paket XML dokümanı
> **Manuel test alanı:** Yok — bu faz çalışma anı davranışı değiştirmez. Kapı testtir, manuel case üretmez

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show d198431:docs/arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md
> ```
>
> Damıtıldı 2026-08-28 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon'in extension seam'lerinin **tek bir sözleşme standardı yoktur.** Üçüncü taraf bir implementasyon yazan geliştirici, arayüzün DI lifetime'ını, kiracı modunu, teslim garantisini ve iptal semantiğini bugün **kaynağı okuyarak** öğrenmek zorundadır — paketlenmiş tüketicinin ise kaynağı yoktur.

## Bitiş Ölçütleri (DoD)

- [x] `SeamContractDocumentationTests` var; taban çizgisi üretildi ve **gözden geçirildi** — 78 arayüz tarandı, ilk koşum 227 satır (220 debt) üretti; 3 regresyon testiyle (boş/dolu arayüz, üye-seviyesi doküman, K-642 parçalı-ifade tuzağı) doğrulandı
- [x] Kapının her boyutta kasıtlı bozmayla kırmızı verdiği **ölçüldü ve çıktısı belgeye yazıldı** (K-642 tuzağı) — dimension 1-3: `A_split_phrase_does_not_satisfy_the_scan` regresyon testiyle; dimension 4: üç TheoryData satırı (BL-026/028/042) yazılmadan önce ayrı ayrı RED verdiği ilk koşumda ölçüldü (bkz. Denetim Bulguları)
- [x] Taban çizgisi ters yönde de kilitli: belgelenen bir arayüz düşmezse test kırmızı verir — `Seam_contract_baseline_matches_the_tracked_debt_ledger` bunu 46 satırlık shrink turunda gerçek olarak ölçtü (baseline 222→174 satır)
- [x] 121.3 tablosundaki her kayıt için ilgili boyut dolduruldu ve arayüz taban çizgisinden düştü — BL-024/028/029/035/038/042/043/046/026(belge) tam, BL-048/050 kısmen (yalnız dimension 1/2, kulvar 1/2 dışı kalan contract-test/registration-API bilinçli kapsam dışı)
- [x] `git diff --stat -- 'src/*/PublicAPI.Unshipped.txt'` — **`Tracon.Abstractions`/`Tracon.Core` boş** (imza değişmedi); `Tracon.Testing.Contracts.Xunit` **+3 satır** (bilinçli sapma, BL-046 gerçek kusur düzeltmesinin `AuditLogContract`'a eklediği 3 yeni `[Fact]` — bkz. Plandan Sapmalar, K-644)
- [x] `IRunCancellationRegistry`'nin cooperative-only sınırı fonksiyonel testle ölçüldü — `RunCancellationRegistryTests.TryCancel_does_not_stop_a_run_body_that_never_reads_its_token`: token'ı hiç okumayan bir "run body" `TryCancel` sonrası da "spend" saymaya devam ediyor
- [x] `AuditLogContract` `null` tenant vakasını dört koşumda birden ölçüyor — `InMemory` (21/21), `PostgreSQL` (21/21), `SqlServer` (21/21), `Sqlite` (21/21), hepsi ayrı ayrı yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 2887b9b` tamamı ✅ (tarama, dokuman-bakim, build, `dotnet test` tüm solution 2887+ test, `dotnet pack`, `dotnet format --verify-no-changes`, docs-site build+check:links+check:weight)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. altındaki "Örnek Uygulama Koşumu"
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` ✅ temiz
- [x] Manuel kabul case'i **üretilmedi**; gerekçe: bu faz yalnız XML doküman + bir mevcut kusuru düzeltir (BL-046); kusurun kendisi `curl` ile `samples/Tracon.Api` üzerinde uçtan uca ölçüldü (audit trail yazma/okuma), ayrı bir manuel case gerektirecek yeni bir kullanıcı-görünür akış yok
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 0× 🔴, 1× 🟡 (bu turda kapandı, aşağıya bakınız), 1× 🟢 (`docs/ADAYLAR.md`'ye devredildi)
- [x] `docs-site/` yeniden derlendi (**`--skip-docfx` KULLANILMADAN**); üretilen `api/` sayfaları yeni XML metnini taşıyor; `check-links.mjs` temiz — spot-check: `Tracon.IRunCancellationRegistry.md` "cooperative" metnini, `Tracon.IAuditLog.md` "AMBIENT" metnini taşıyor; 150091 link, 0 kırık
- [x] `YAYIN-HAZIRLIK.md`'de kapanan blocker kayıtları güncellendi — BL-024/028/029/035/038/042/043/046 kapandı, BL-048/050 kısmen, BL-026 belge kısmı kapandı, KG-017 + K-643/K-644 eklendi

### Doğrulama komutları

```bash
# Kapinin gercekten olctugu: taban cizgisi disindan bir arayuzu boz, kirmizi bekle
./artifacts/bin/Tracon.Core.UnitTests/release/Tracon.Core.UnitTests \
  --filter-class "*SeamContractDocumentationTests*"

# Imza kaymadi mi
git diff --stat -- 'src/*/PublicAPI.Unshipped.txt'

# Uretilen API sayfasi yeni metni tasiyor mu (ornek)
grep -n "singleton" docs-site/src/content/docs/api/Tracon.IMcpPromptClient.md
```

---

## Plandan Sapmalar

1. **Dimension detection scope genişletildi: yalnız arayüz başlığı değil, TÜM üye dokümanları.** İlk `SeamContractDocumentationTests` sürümü yalnız `public interface I...`'nin hemen üstündeki `<summary>`/`<remarks>` bloğunu tarıyordu. Bu, referans örnek `IJobHandler`'ı yanlışlıkla "delivery guarantee belgesiz" işaretledi — "at-least-once" cümlesi `ExecuteAsync`'in KENDİ `<remarks>`'inde duruyor, başlıkta değil. `InterfaceDocSurface` metoduna genişletildi: başlık + gövdedeki her `///` satırı birleştirilip aranıyor. K-643'e ve `docs/hafiza/dokumantasyon.md`'ye yazıldı.
2. **Dimension 4 (guarantee limit) genel taramaya DAHİL EDİLMEDİ — ayrı TheoryData mekanizması kullanıldı.** Plan 121.2'nin akış şeması dört boyutu tek bir "cevaplanmış mı" sorusu gibi çiziyordu. Ölçüldüğünde dimension 1-3'ün sabit bir kelime dağarcığı var (`singleton`/`scoped`/`transient` vb.) ama dimension 4'ün (neyin garanti EDİLMEDİĞİ) yok — açık uçlu, arayüze özgü prosa. Genel bir tarama ya anlamsızca gevşer ya yanlış-pozitif üretir. Bunun yerine zaten kanıtlanmış `OrderingContractDocumentationTests` deseni (K-642) yeniden kullanıldı: üç TheoryData satırı (BL-026/028/042), her biri kendi zorunlu ifadeleriyle. K-643'e yazıldı.
3. **BL-046 yalnız doküman değil, gerçek bir kusur olarak kapatıldı — planın "yalnız XML dokümanı yazar" sınırını aştı.** `IAuditLog`'un null-tenant "ambient fallback" sözleşmesini yazarken `InMemoryAuditLog`'un (Tracon'in KENDİ referans implementasyonu) null/boş tenant'ta GERÇEKTEN tüm kiracıları taradığı ölçüldü — dokümante edilen niyet hiçbir implementasyonda gerçek davranış değildi. `SqlAuditLog` ise sessizce boş sonuç döndürüyordu. İkisi de `SqlRunStore`'un zaten kullandığı `ITenantContext` fallback desenine hizalandı (yeni desen icat edilmedi). Bu, kullanıcının "konuyla alakalı bug/defect'leri de çöz" talimatının doğrudan kapsamına giriyordu — BL-046'nın kendisi bu kusuru tarif ediyordu. K-644'e yazıldı.
4. **Public API planı ihlal edildi: `Tracon.Testing.Contracts.Xunit`'in `PublicAPI.Unshipped.txt`'si 3 satır büyüdü.** Madde 3'ün doğal sonucu — `AuditLogContract` (public, paketlenmiş) üç yeni `[Fact]` aldı. `Tracon.Abstractions`/`Tracon.Core` sıfır kaldı (imza değişmedi, yalnız XML doküman). `faz-denetim` bunu 🟡 olarak işaretledi ve burada gerekçelendirilmesini istedi — Açık Soru 2'nin "A: bu fazda" kararı zaten büyümeyi öngörmüştü, yalnız DoD'nin literal `PublicAPI.Unshipped.txt` **boş** komutuyla çelişkisi plan metnine yazılmamıştı.
5. **BL-050'nin gerçek üye sayısı 11 değil 12 çıktı.** Plan tablosu "Küme I'nın 11 arayüzü" diyordu; §15'in kendi küme listesi (`IRunJudge` hariç) 12 üye sayıyor (`IConversationBranchStore` unutulmuş). Tutarsızlık kaynağı belirsiz — küçük, kararı etkilemiyor; 12'nin hepsi dokümante edildi.
6. **`site-denetle`'nin `cekirdek-kavram` kuralı gerekçeyle geçildi, sayfa güncellenmedi.** `IAgentDefinitionStore.cs` (`Agents/` klasörü) değiştiği için tetiklendi, ama değişiklik yalnız MEVCUT, değişmemiş bir davranışın (AMBIENT tenant, `ITenantContext`'ten) XML doküman derinliği — `docs-site/concepts/agents.md` kavram seviyesinde yeni bir şey yok, sayfaya zorlama eklemek doldurma olurdu. `kalicilik` kuralı (`SqlAuditLog.cs`) ise GERÇEKTEN güncellendi: `getting-started/persistence.md`'ye null-tenant AMBIENT fallback notu eklendi (BL-046, K-644) — bu genuinely yeni ve üçüncü taraf implementer'ı ilgilendiren bir sözleşme.

## Bu Fazda Verilen Kararlar

- **K-643** — Seam sözleşme standardının dört boyutu iki farklı mekanizmayla kilitlenir: ilk üçü (sabit kelime dağarcığı) `SeamContractDocumentationTests`'in küçülen taban çizgisiyle, dördüncüsü (açık uçlu prosa) `OrderingContractDocumentationTests` deseniyle ayrı TheoryData satırlarıyla.
- **K-644** — `IAuditLog`'un null-tenant sözleşmesi (`AuditQuery.TenantId`/`AuditChainQuery.TenantId == null` → çağıranın AMBIENT tenant'ına düşer) `ITenantContext` enjeksiyonuyla gerçek davranışa dönüştürüldü; `InMemoryAuditLog`/`SqlAuditLog` `SqlRunStore`'un zaten kullandığı desene hizalandı.

Tam gerekçe: `docs/KARARLAR.md` — grep `K-643\|K-644`.

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir agent olarak çalışma ağacına karşı koştu
(2026-08-28). **🔴 yok.**

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Plan "Public API büyümez" diyor ama `Tracon.Testing.Contracts.Xunit`'in `PublicAPI.Unshipped.txt`'si 3 satır büyüdü; DoD'nin literal `git diff --stat` komutu bunu boş bulmaz | 🟡 | **Kapandı.** Meşru bir sapma (Açık Soru 2, K-644) — Plandan Sapmalar #3/#4 ve DoD listesi açıkça yazıldı, sessizce geçilmedi |
| 2 | `IAgentSkillStore`'un (ve olası başka store'ların) hiçbir `Auditing*` decorator'ı yok — skill kaydı/silme audit trail'e hiç yazmıyor; kardeşleri (`IAgentDefinitionStore`, `ISkillScriptGrantStore`) yazıyor | 🟢 | **Aday listesine devredildi** — bu fazın kapsamı yalnız XML doküman standardı, audit coverage boşluğu ayrı bir runtime-davranış eklentisi. `docs/ADAYLAR.md`'ye F-NN olarak eklenmesi önerilir |

**Temiz çıkan başlıklar:** 3.1 (DoD kanıtla doğrulandı), 3.2 (test tiyatrosu yok
— `SeamContractDocumentationTests`'in 3 regresyon testi ve `AuditLogContract`'ın
3 yeni testi gerçek davranış ölçüyor), 3.3 (contract testleri 4 backend'de de
doğru bağlı), 3.4 (kiracı sızıntısı üç testle kapatıldı), 3.5 (imza-gövde
kayması yok — `SqlAuditLog`'un yeni parametresi tüm çağıranlarda güncellendi),
3.7 (İngilizce, XML doküman, `TryAdd`, AOT tutarlı), 3.8 (docs-site gerçekten
yeniden derlenmiş, manuel case gerekçesi doğru).

## Sonraki Faza Devir Notu

- **35× 🟡 hattının kulvar 3'ü kapandı (KG-017).** BL-024/028/029/035/038/042/043/046
  tam kapandı; BL-048/050 kısmen (yalnız dimension 1/2 — contract test ve
  registration API kulvar 1/2'de kalır); BL-026 belge kısmı kapandı, muhasebe
  hassasiyeti riski değişmedi (iş kaybı yok, halen ölçülü).
- **`seam-contract-baseline.txt` 174 satır bilinen borç taşıyor** (78 arayüzün
  yaklaşık 58'i bu fazda dokunulmadı). Kulvar 3'ün geri kalanı veya
  başka bir seam turu bu dosyayı küçültmeye devam eder — dosyanın kendisi
  neyin eksik olduğunu `<arayüz>:<boyut>` biçiminde tam olarak listeliyor,
  yeniden analiz gerekmez.
- **🚨 Bir seam-metin kapısı yazarken arayüzün TÜM üye dokümanını tara, yalnız başlığı değil.** `IJobHandler` gibi delivery-guarantee cümlesi bir METOT'un `<remarks>`'inde durabilir; yalnız interface-level `<summary>`/`<remarks>`'i okuyan bir tarama bu tür arayüzleri yanlışlıkla eksik işaretler. Bkz. `docs/hafiza/dokumantasyon.md`.
- **🚨 DTO yorumundaki "boşsa X'e düşer" cümlesini, implementasyonu okumadan doğru kabul etme.** `AuditQuery.TenantId`'nin "the tenant of the caller is used when null" cümlesi üç yıldır hiçbir implementasyonda gerçek değildi. Bkz. `docs/hafiza/aspnetcore-di.md`.
- **Yeni aday (kulvar 3 dışı, `faz-denetim`'in bulduğu):** `IAgentSkillStore` (ve muhtemelen komşu store'lar) hiçbir `Auditing*` decorator'ı almıyor — skill CRUD'u audit trail'e hiç yazmıyor. `docs/ADAYLAR.md`'ye eklenmesi kullanıcı kararına bağlı.
- **Bu fazdan sonra planlanmış bir F-122 yok** — `docs/YOL-HARITASI.md`'de 121 son kalemdir. Sıradaki iş kullanıcı kararına bağlı: kulvar 2 (kayıt API'si) için yeni bir `aday-kesfi`/`faz-planlama` turu, yoksa doğrudan `nuget-danismani`'nin yayın kararı turu — `YAYIN-HAZIRLIK.md` §13 ve KG-017 bu kararın girdisidir.

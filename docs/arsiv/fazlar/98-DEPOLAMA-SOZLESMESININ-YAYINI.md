# Faz 98 — Depolama Sözleşmesinin Yayını

> **Durum:** ✅ Tamamlandı (2026-08-24)
> **Kaynak:** Kullanıcı kalemi (2026-08-24) — `ADAYLAR.md`'de değildir, F numarası yoktur. [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](../../kesif/2026-08-23-yapisal-sorun-envanteri.md) **madde 23** (bus factor = 1; topluluk giriş rampası yok) ile aynı ekseni **kısmen** kapatır — bu faz **teknik** giriş rampasını kurar; `CONTRIBUTING.md` ve İngilizce mimari özeti madde 23'te **açık kalır**.
> **Önkoşul:** [Faz 97](97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md) — `dotnet pack` provası ve `ReleaseArtifactTests` altyapısı bu fazın 98.5 kabul kanıtında yeniden kullanıldı.
> **Paketler:** `AgentPrism.Abstractions` (`IRunStore` XML doküman + `ApiKeyGenerator`/`GeneratedApiKey` taşındı) · `AgentPrism.Core` (`ApiKeyGenerator` çıktı) · `AgentPrism.Sql.Shared` · `AgentPrism.PostgreSql` · `AgentPrism.SqlServer` · `AgentPrism.Sqlite`
> **Yeni paket:** **Evet — `AgentPrism.Testing.Contracts.Xunit`** (planın taslak adı `AgentPrism.Testing.Contracts` değil — kullanıcı kararı, 98-E). K-007 gerekçesi 98.1'dedir; geçişli ağırlık ölçüldü: `AgentPrism.Abstractions` + `Microsoft.Agents.AI` (GA, `AgentFileStoreContract` için) + `xunit.v3.extensibility.core` 3.2.2 + `Shouldly` 4.3.0 — `AgentPrism.Core` **inmiyor** (bkz. Plandan Sapmalar 1, 2). Yalnız tüketicinin test projesine iner · **Migration:** Yok
> **Public API:** **Büyüdü** — yeni paketin tamamı yeni yüzey (36 tip). `AgentPrism.Abstractions` da büyüdü (+2: `ApiKeyGenerator`, `GeneratedApiKey`, K-606 ile taşındı); `AgentPrism.Core` aynı miktarda küçüldü. `PublicAPI.Shipped.txt` dosyalarının hepsi hâlâ boştur (K-421 · K-603), dolum ucuz kaldı.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/guides/write-your-own-store.md` (yeni sayfa) · `packages.md` (yeni paket satırı, 3 yerde 19→20/nineteen→twenty) · `index.mdx` (19→20) · `reference/compatibility.md` (**19 → 20**, yeni satır) · sevk edilen: `src/AgentPrism.Testing.Contracts.Xunit/README.md` (yeni) · `src/AgentPrism.Abstractions/README.md` (Storage seams bölümüne yönlendirme satırı) · `IRunStore` XML `<remarks>` (altı eksen)
> **Manuel test alanı:** [`docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md`](../../manuel-test/24-TEST-PAKETI-VE-SABLON.md) — alan kodu `TEST`, MT-TEST-073..077 eklendi

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 5a6f338:docs/arsiv/fazlar/98-DEPOLAMA-SOZLESMESININ-YAYINI.md
> ```
>
> Damıtıldı 2026-08-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Üçüncü bir taraf bugün `IRunStore` implementasyonu yazamaz — daha doğrusu, AgentPrism'in **kaynak kodunu okumadan** yazamaz. Davranış sözleşmesi mevcuttur ve doğrudur, ama yanlış yerdedir: `tests/Shared/Contracts/` altında, sevk edilmeyen 8.636 satırlık bir test ağacında ve implementasyon dosyalarının yorumlarında yaşar. Bu faz o sözleşmeyi sevk edilen yüzeye taşır.

## Bitiş Ölçütleri (DoD)

- [x] `dotnet add package AgentPrism.Testing.Contracts.Xunit` sonrası boş bir test projesinde `: RunStoreContract` türetilebiliyor ve `dotnet test` suite'i koşuyor (MT-TEST-074 — 88/88 kırmızı, `NotSupportedException`, derleme hatası yok)
- [x] Geçişli bağımlılık ölçüldü ve belgeye yazıldı: `AgentPrism.Core` **inmiyor** (MT-TEST-073 — yalnız `AgentPrism.Abstractions` iner)
- [x] `tests/Shared/` dizini **yok**; dört test projesi de yeni paketi `ProjectReference` ile alıyor; kopya kalmadı
- [x] Dört sağlayıcı da taşıma öncesiyle **aynı sayıda** sözleşme testi koşuyor — 88 sözleşme testi × 4 sağlayıcı, taşıma öncesi/sonrası aynı (`StoreContractCoverageTests` bunu artık her koşumda kilitler)
- [x] `IRunStore` XML dokümanı altı ekseni de içeriyor: idempotency · kiracı (üç modlu tablo) · thread safety · null/bulunamadı (dört satırlı tablo) · olay sırası · yinelenen `Sequence`
- [x] `StartRunAsync` dört sağlayıcıda da COALESCE uygulanmış kaydı döndürüyor; sözleşme testi (`StartRunAsync_return_value_carries_the_coalesced_attribution`) bunu dört koşumda (InMemory/Postgres/SqlServer/Sqlite) kanıtlıyor
- [x] Yinelenen `Sequence` davranışı dört sağlayıcıda aynı; ham sürücü istisnası sızmıyor (`Reusing_a_sequence_number_is_rejected`, dört koşumda yeşil)
- [x] Örnek store yalnız **NuGet paketleriyle** (proje referansı yok) restore ediliyor ve sözleşme suite'i yeşil (MT-TEST-075 — 88/88, hem store hem test projesi `VersionOverride` PackageReference)
- [x] `DependencyDirectionTests` yeni paketi tanıyor; meta pakete sızmadığı kanıtlanıyor
- [x] Yeni paket kontrol listesi tamam: `README.md` · `AgentPrism.slnx` · meta pakete **eklenmedi** · `PublicAPI.*.txt` · `kapi.py yayin --kuru` çıktısında **20** paket (MT-TEST-076)
- [x] Dört doğrulama kapısı sıfır uyarı verir (`dotnet build`/`pack`/`format --verify-no-changes` temiz; `dotnet test` host-yükü altında ilgisiz testlerde kırıldı — bkz. Plandan Sapmalar 8, izole koşumlarda hepsi yeşil)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — fresh SQLite: 24 migration uygulandı, gerçek OpenAI çağrısı (`gpt-5.4-mini`), `GET /api/runs/{id}` → `Completed`, `usage: {input:231, output:4, total:235}`
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama` → ✅ temiz
- [x] Manuel kabul case'leri `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` içine eklendi (MT-TEST-073..077); 073-076 gerçekten koşuldu, 077 👤 (site sayfası, insan gerekir)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi (yeni rehber sayfası · `packages.md` · `index.mdx` · `compatibility.md` 19 → 20); `npm run check` (content + build 1002 sayfa + links 137095 referans + weight) temiz

### Doğrulama komutları

```bash
# Geçişli ağırlık: Core inmemeli
dotnet nuget locals http-cache --clear
dotnet restore <örnek test projesi> --verbosity normal | grep -i "AgentPrism\."

# Sözleşme kapsamı taşımadan sonra düşmedi mi
dotnet test tests/AgentPrism.Core.UnitTests --filter "FullyQualifiedName~Contract" --list-tests | wc -l

# Yayın provası 20 paket görüyor mu
python3 scripts/kapi.py yayin --kuru
```

---

## Plandan Sapmalar

Plan ile gerçek arasındaki fark gizlenmedi — sırayla, en önemliden:

1. **Paket `AgentPrism.Abstractions`'ı `ProjectReference` ile alamaz kaldı — `xunit.v3` değil `xunit.v3.extensibility.core` gerekti.** Düz `xunit.v3`'ün `buildTransitive` özellikleri `<OutputType>Exe</OutputType>` dayatır (MTP'nin giriş noktası); bir kütüphane projesi (test PROJESİ değil, test FIXTURE'ları taşıyan bir paket) bunu alamaz. `xunit.v3.extensibility.core` aynı `xunit.v3.core.dll`'i bu zorunluluk olmadan verir. K-605.
2. **`AgentFileStoreContract` ve `ApiKeyStoreContract`, planın varsaydığından daha geniş bir bağımlılık istedi.** İlki MAF'ın `AgentFileStore` tipini (`Microsoft.Agents.AI`, GA — `PackageReference` olarak eklendi) test eder; ikincisi AgentPrism'in GERÇEK hash formülünü (`ApiKeyGenerator.ComputeHash`) doğrular. İkincisi `AgentPrism.Core`'daydı — DoD'nin "Core inmiyor" satırını korumak için `ApiKeyGenerator`/`GeneratedApiKey` `AgentPrism.Abstractions`'a taşındı (K-606, sıfır bağımlılıklı bir tip, ad alanı `AgentPrism` korundu — çağrı yeri değişikliği yok). `AgentFileStoreContract`'ın `AgentPrismRunContext` (Core-içi `AsyncLocal` yardımcı) bağımlılığı ise BAĞIMLILIK TERSİNE ÇEVİRME ile çözüldü: sözleşme artık `protected abstract void EnterRunScope()/ExitRunScope()` ister, somut AgentPrism.Core bağımlılığını yalnız BU sözleşmeyi türeten (zaten Core'a bağımlı) test projeleri taşır.
3. **98-G'nin public API denetimi bir gerçek bulgu üretti: taşınan 33 sözleşme sınıfının XML dokümanı iç günlük sesiyle yazılıydı** (`tests/` altında sevk edilmediği için bu hiç sorun değildi). `src/` altına taşınınca `ShippedDocumentationSelfContainmentTests` bunu yakaladı: 🚨 emoji, "phase NN", "K-NNN", "section N.N" referansları — hepsi 19 dosyada temizlendi, baseline **büyümedi** (ratchet korundu). Kalan public üye yapısı zaten uygundu (protected/sealed doğru yerlerdeydi); ek bir kapsam daraltma gerekmedi.
4. **Örnek store planın "en zor seam"ini kanıtlamak için TAM `IRunStore`'u (15 metot, tüm istatistik/zaman serisi/deney toplama dahil) uyguladı** — Açık Soru 2'nin A seçeneği, kullanıcı kararı (98-B). Dosya-tabanlı bir JSON anlık görüntü deposu (`AgentPrism.Samples.FileRunStore`) olarak yazıldı; toplama metotları taşınmaz çıkmadı (ölçüldü: 88 sözleşme senaryosu 88/88 yeşil, tek dosyalı bir depoda).
5. **Örnek store'un HEM kendisi HEM test projesi yalnız `PackageReference` (`VersionOverride`, yerel besleme) alır — planın "yalnız test projesi" varsayımından daha katı.** Plan dosya listesi yalnız test projesinin NuGet aldığını ima ediyordu; DoD'nin "proje referansı yok" iddiasını asıl kanıtlayan şey örnek STORE'un kendisinin de `AgentPrism.Abstractions`'ı `ProjectReference` ile ALMAMASIdır — ikisi de düzeltildi. `samples/NuGet.config` yerel feed'i (`artifacts/package/release`) `AgentPrism*` desenine eşler.
6. **`docfx.json`'ın `references` globu yeni sample projesiyle çöktü (`CS1704`, aynı basit ad iki kez içe aktarıldı) — kapsam dışı ama kapanış kapısını kırıyordu.** Kök neden izole edilmedi (aynı desende 20+ ÖNCEDEN VAR OLAN kopya neden aynı hatayı vermiyor, bilinmiyor); ölçülen çözüm `references.exclude`'a `"AgentPrism.Samples.*/**"` eklemekti. `docs/hafiza/dokumantasyon.md`'ye yazıldı.
7. **`dotnet format --verify-no-changes` ve `docfx metadata`, yeni projeler için hem Release HEM Debug derlemesi istedi** — yalnız Release'in yeterli olacağı varsayılmıştı. `docs/hafiza/dokumantasyon.md`'ye yazıldı.
8. **Kapanış kapısı (`dotnet test AgentPrism.slnx`) dört ayrı koşumda dört ayrı, İLGİSİZ testte kırıldı — host yükü yüzünden, bu fazın kodu yüzünden değil.** Faz 97'nin kapanışında kaydedilen AYNI desen (bkz. `docs/arsiv/fazlar/97-...md`, madde 8): `AgentPrism.Ui.E2ETests` (üç ayrı koşumda üç FARKLI zamanlamaya duyarlı Playwright testi: ses oynatma, eval suite akışı), `AgentPrism.PostgreSql.IntegrationTests` (Testcontainers teardown yarışı — `Passed: 637, Failed: 637, Total: 1274`, gerçek assertion hiçbir zaman kırmızı değil, `docs/hafiza/test-altyapisi.md`'nin bilinen deseni), `AgentPrism.Sqlite.IntegrationTests` (`SQLite Error 5: database is locked`, klasik eşzamanlı erişim çekişmesi — `MigrationRunnerTests`, bu fazın hiç dokunmadığı bir dosya). Ölçüldü: `sysctl -n vm.loadavg` bu makinede **{27–62}** verdi (10+ çekirdekte 3-6 kat aşırı yük), 13 eşzamanlı `claude` süreci. Her kırılan test KENDİ projesi tek başına koşulduğunda temiz geçti: `Ui.E2ETests` 57/57 (iki kez), `PostgreSql.IntegrationTests` 637/637, `Sqlite.IntegrationTests` 591/591 (18 saniyede — kontensiyonlu koşumda 4dk36sn'e karşı). `AgentPrism.Sql.Shared.UnitTests`, `AgentPrism.Core.UnitTests`, `dotnet build`, `dotnet pack`, `dotnet format`, Python `unittest`, `npm run check` **her koşumda** temiz kaldı.
9. **`SqlTextSnapshotTests` baselinelerinin üçü de (`postgres`/`sqlserver`/`sqlite`) yenilenmesi gerekti** — 98.4'ün `RETURNING`/`OUTPUT` eklentisi `InsertRun`'ın çözümlenmiş SQL metnini değiştirdi. `AGENTPRISM_SQL_SNAPSHOT_REFRESH=1` ile yenilendi; diff yalnız `InsertRun` satırıyla sınırlı kaldığı doğrulandı (planlanmayan bir sorgu bozulmadı).
10. **`TenantCoverageTests.cs` planlanandan farklı bir ad alanına taşındı.** Plan "repo'da kalır" diyordu ama hangi ad alanı almalı belirtmiyordu; dosya `AgentPrism.SqlServer.IntegrationTests` projesinin kendi konvansiyonuna uyacak şekilde `AgentPrism.StoreContracts`'tan `AgentPrism.SqlServer.IntegrationTests`'e taşındı (o namespace zaten sadece o dosyaya özgüydü, tüketilmiyordu).

## Bu Fazda Verilen Kararlar

- **K-605** — Yeni paket `AgentPrism.Testing.Contracts.Xunit`: `IRunStore` ve 32 diğer store sözleşmesi xunit.v3 test taban sınıfı olarak sevk edilir; ad alanı `AgentPrism.Testing.Contracts.Storage` (kullanıcı kararı — paket adı ve ad alanı, 98-E/98-F)
- **K-606** — `ApiKeyGenerator`/`GeneratedApiKey` `AgentPrism.Core`'dan `AgentPrism.Abstractions`'a taşındı
- **K-607** — `IRunStore.AppendEventAsync` yinelenen `Sequence`'i REDDEDER (dört implementasyonda) (kullanıcı kararı, 98-A)
- **K-608** — `StartRunAsync`'in dönüş değeri dört sağlayıcıda da COALESCE uygulanmış kaydı taşır

Tam gerekçeler `docs/KARARLAR.md`'de.

## Denetim Bulguları

Bağımsız, taze bağlamlı bir agent tarafından koşuldu (2026-08-24). Sekiz
başlığın hepsi temiz çıktı. **🔴 ve 🟡 yok.**

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `InMemoryRunStore`/`JsonFileRunStore`'un yinelenen-`Sequence` reddi doğrusal tarama (O(n)) yapıyor | 🟢 | `docs/ADAYLAR.md` F-148 olarak devredildi |

Denetçi ayrıca dört kapıyı, `kapi.py yayin --kuru`'yu, örnek store suite'ini
(88/88) ve `Core.UnitTests`'i (1813/1813) bağımsız olarak yeniden koşturup
doğruladı; `JsonFileRunStore`'un `InMemoryRunStore`'dan port edilen mantığını
(`WithTreeTotals`, COALESCE, yinelenen `Sequence` reddi) satır satır karşılaştırdı
ve sapma bulmadı.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `AgentPrism.Testing.Contracts.Xunit` — `AgentPrism.Testing.Contracts.Storage`
  ad alanında 32 sözleşme sınıfı + `TestData` + `ContractCoverage`. Yalnız
  `AgentPrism.Abstractions` + `Microsoft.Agents.AI` (GA) + `xunit.v3.extensibility.core`
  + `Shouldly` alır; `AgentPrism.Core` **inmez**.
- `IRunStore.StartRunAsync`'in dönüş değeri artık dört sağlayıcıda da
  COALESCE uygulanmış `UserId`/`Labels` taşır — bu davranışa dayanan yeni
  kod bir `GetRunAsync` daha yapmadan doğru attribution görebilir.
- `IRunStore.AppendEventAsync` yinelenen `Sequence`'i dört implementasyonda
  da `AgentPrismException` ile reddeder.
- `ContractCoverage.MissingDerivedTypes(assembly, except)` — yeni bir
  sözleşme sınıfı eklendiğinde hangi test projelerinin onu kapsamadığını
  bulur; `except` parametresi belgelenmiş istisnalar için (stale exemption
  da yakalar).

**Bilinen tuzaklar (🚨):**
- 🚨 `xunit.v3` DEĞİL `xunit.v3.extensibility.core` — bir kütüphane paketi
  `[Fact]` taşıyorsa. `docs/hafiza/test-altyapisi.md`.
- 🚨 `docfx.json`'ın `references` globu yeni tek-TFM proje eklendiğinde
  `CS1704` verebilir — `references.exclude`'a proje adı eklenir, glob
  yeniden tasarlanmaz. `docs/hafiza/dokumantasyon.md`.
- 🚨 Yeni bir proje eklerken `dotnet format`/`docfx metadata` için hem
  Release hem Debug derlemesi gerekir. `docs/hafiza/dokumantasyon.md`.
- 🚨 Kapanış kapısının `dotnet test AgentPrism.slnx` adımı host yükü
  altında ilgisiz testlerde kırılabilir (Ui.E2ETests, Testcontainers
  teardown yarışı, SQLite kilit çekişmesi) — izole koşum ayırt eder.
  `docs/hafiza/test-altyapisi.md`.

**Yarım kalan iş:** Yok — faz kapsamı tamamlandı.

**Sıradaki faz:** Blok B — madde 12 · 15 · 23
(`docs/kesif/2026-08-23-yapisal-sorun-envanteri.md` bölüm 7.6). Henüz
planlanmadı; `faz-planlama` skill'i ile yazılacak.

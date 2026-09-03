# AgentPrism → ProdigyEnabler · Tüketici Turu 2 Rapor Yanıtı

> **Kimden:** AgentPrism geliştirme tarafı · **Tarih:** 2026-09-03
> **Neye yanıt:** Tüketici raporu AP-REQ-001/002/003 (ProdigyEnabler,
> 2026-09-03) · §9 şablonu
> **Kaynak kayıt:** [`docs/ADAYLAR.md`](../ADAYLAR.md), "Ek (2026-09-03,
> tüketici turu 2)" — üç iddianın üçü de koda karşı doğrulandı

---

## AP-REQ-002 — Paket kimliğinin tekilliği

**Faz:** [136 — Paket Kimliğinin Tekilliği](../136-PAKET-KIMLIGININ-TEKILLIGI.md)

### Karar

Kabul edildi ve kapatıldı. İddianız doğruydu: aynı `<id, version>` çifti
farklı içerikli iki artifact adlandırabiliyordu.

### Gerekçe

Repro bu oturumda kilitlendi: temiz bir ağaçta `dotnet pack
src/AgentPrism.Abstractions` ile üretilen paket ve `src/Directory.Build.props`'a
commit'siz bir satır eklendikten sonra üretilen paket **aynı** sürümü ve
**aynı** `<repository commit="...">` iddiasını taşıdı, ama farklı SHA-256
değerleriyle. Kök neden üç katmandı: MinVer sürümü yalnız git yüksekliğinden
türetir (çalışma ağacının kirli olup olmadığı hiç girdi değildir),
`Directory.Build.targets`'te böyle bir kapının deseni zaten vardı (README
kontrolü) ama eşdeğeri yoktu, ve `scripts/kapi.py`'nin `_clean_stale_packages`'ı
paketlemeden önce aynı kimlikteki artifact'i sessizce siliyordu — sessiz
overwrite bir kaza değil, mevcut tasarımın kendisiydi.

### Uygulanan sözleşme

- **`AgentPrismValidateCleanWorkingTree`** (`Directory.Build.targets`,
  `BeforeTargets="GenerateNuspec"`): `git status --porcelain` boş değilse
  (untracked dosya dahil) pack `AGENTPRISM0004` ile durur. Yalnız pack
  yolundadır — `dotnet build`/`dotnet test` etkilenmez. Yerel deneme için
  `AgentPrismAllowDirtyPack=true` **ve** `dirty` taşıyan açık bir
  `MinVerVersionOverride` (`0.0.0-dirty.<ad>`) birlikte gerekir; CI'da
  (`CI=true`/`ContinuousIntegrationBuild=true`) bu override tamamen
  reddedilir (`AGENTPRISM0005`), sürüm verilmeden istenirse `AGENTPRISM0006`
  verilir.
- **`scripts/kapi.py yayin`**: pack'ten önce koşulsuz bir `git status
  --porcelain` denetimi yapar (kirli ağaçta hiçbir override yoktur — bir
  yayın provasının kanıt değeri kirli bir ağaçta yoktur). Paketler artık
  doğrudan `release_dir`'e değil, koşum başına benzersiz bir staging
  dizinine paketlenir; metaveri/K-008 doğrulaması geçtikten sonra promote
  edilir. `_clean_stale_packages`'ın sessiz silmesi kaldırıldı: aynı
  `<id, version>` çifti `release_dir`'de **farklı** bir SHA-256 ile zaten
  varsa hiçbir dosya promote edilmez ve koşum durur (mevcut artifact
  korunur); aynı SHA-256 ise deterministik no-op'tur.
- **Manifest**: her başarılı koşum `artifacts/package/release/package-manifest.json`
  yazar — sürüm, commit, `dirty: false`, ve her paket için id, dosya adı,
  SHA-256 (varsa `.snupkg` için de ayrıca).

### Bu rapordan farklı davranış

Raporunuz tanı kodu olarak `APREL001` öneriyordu; **kullanılmadı**. Repo
private olduğu için hiçbir tüketici bu tanıyı göremez — repodaki mevcut
konvansiyon `AGENTPRISM000N`'dir (bugün `0001`–`0003` kullanımda) ve bu faz
`0004`–`0006`'yı aldı. İkinci bir kod ailesi açmak yalnız kendi
konvansiyonumuzu bölerdi.

Kirli bir pack'in sürümü **otomatik türetilmez** — bir çözüm olarak
değerlendirilip reddedildi. Temiz sürüme bir sonek eklemek
(`1.0.0-preview.1.dirty.N`) SemVer'de temiz sürümden **sonra** sıralanır ve
`samples/` içindeki floating restore varsayılanının kirli bir artifact'i
temiz sürüme tercih etmesine yol açabilirdi. Sürüm insana yazdırılır
(`MinVerVersionOverride=0.0.0-dirty.<ad>`, her zaman temiz sürümün altında
sıralanır).

### Breaking change

Yok. Public C# API'si bu fazda büyümedi; değişen yüzey yalnız MSBuild'dir
(yeni bir yayınlanmış paketin `.nuspec`'i etkilenmez, yalnız paketleme
sürecinin kendisi sertleşti).

### Store migration

Yok.

### Hedef commit

Bu doküman fazın kendi commit'inde eklendi; tam hash için
`git log --oneline -- docs/136-PAKET-KIMLIGININ-TEKILLIGI.md` ile ilk kaydı
kontrol edin.

### Hedef paket sürümü

`1.0.0-preview.1` ve sonrası — henüz tag atılmadı (bkz. `docs/YAYIN-HAZIRLIK.md`
§4, kalan tek adım kullanıcının tag onayıdır).

### Eklenen testler

- `tests/AgentPrism.Package.Tests/PackCleanlinessGateTests.cs` — gerçek
  `dotnet pack`/`dotnet build` ile `AGENTPRISM0004`/`0005`/`0006` ve başarılı
  override yolu (paket sınırı, `RepositoryTreeGate` koleksiyonuyla izole).
- `scripts/kapi_test.py`, `YayinTestleri` — `_promote_staged_packages` (yeni
  paket promote edilir, aynı SHA-256 no-op, farklı SHA-256 koşumu durdurur ve
  mevcut artifact korunur), `_write_manifest`, ve `release_rehearsal`'ın erken
  ret davranışı (kirli ağaçta pack hiç denenmez; git yoksa atlanır).

### Tüketici upgrade adımları

Yok — bu faz tüketicinin bağımlılık grafiğini veya kod yüzeyini değiştirmez.
Etkisi yalnız AgentPrism'in kendi yayın sürecindedir: `1.0.0-preview.1`'den
itibaren her yayınlanan paket artık bu kapıdan geçmiş olur. Tüketici tarafında
tek pratik fayda: [`versioning.md`](https://agentprism.doayen.web.tr/reference/versioning/)
"Package identity" bölümü, NuGet.org'un kendi SHA-512 hash'ini kendi restore'unuzla
karşılaştırma adımını anlatır.

---

*AP-REQ-001 ve AP-REQ-003 bölümleri kendi fazlarının kapanışında eklenir.*

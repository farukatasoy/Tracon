# Faz 123 — Yayın Kritik Yolu: Kapı Kapsamı, Adaptör Sözleşmesi ve Sürüm Notları

> **Durum:** ✅ Tamamlandı (2026-08-28)
> **Kaynak:** [YAYIN-HAZIRLIK.md](../../YAYIN-HAZIRLIK.md) — **BL-052**, **BL-015**, **OP-007** (KG-019, yol B)
> **Önkoşul:** Yok — [Faz 122](122-KAYIT-API-SI-VE-SESSIZ-BOSLUKLAR.md) kapandı, çalışma ağacı temiz
> **Paketler:** Kod paketi değişmiyor. Dokunulan: `src/Directory.Build.props`, `src/AgentPrism.Core`, `src/AgentPrism.{Anthropic,Azure,Google,OpenAI}`, `scripts/`, `.github/workflows/ci.yml`, `tests/AgentPrism.{Anthropic,Azure,Google,OpenAI}.UnitTests`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Planın "büyümüyor" iddiası K-646'nın kusur düzeltmesiyle geçersiz kaldı — bkz. Gerçekleşen Public API. `AgentPrism.Core`'a bir tip eklendi (`TenantChatClientCacheKey`), `PublicAPI.Shipped.txt` toplamı hâlâ **0** satırdır (K-603)
> **Tüketici yüzeyi:** site: [`reference/versioning.md`](../../../docs-site/src/content/docs/reference/versioning.md) (yalnız bağlantı eklenir; ayna sayfa **yok**)
> · sevk edilen: **yeni** kök `CHANGELOG.md` (İngilizce) + her `.nuspec`'e giren `PackageReleaseNotes`
> **Manuel test alanı:** [`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 33aa15e:docs/arsiv/fazlar/123-YAYIN-KRITIK-YOLU.md
> ```
>
> Damıtıldı 2026-08-28 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz yayın **kritik yolunu** kapatır. Üç kalem tek fazdadır çünkü üçü de aynı altyapıyı paylaşır: **`v*` tag'i atıldığında ne üretildiğini ve neyin doğrulandığını belirleyen hat.** İkisi kapının *neyi koşmadığını* düzeltir, üçüncüsü kapının *hiç bilmediği* bir yapıtı ekler.

## Bitiş Ölçütleri (DoD)

- [x] `kapi.py yayin --kuru --surum 1.0.0-preview.1` çıkış `0` verir ve başarı satırı **altı** packed sample sayar — `✅ 6 exact-version packed sample ve Native AOT smoke: 1.0.0-preview.1`
- [x] `samples/` altına sahte bir `*.Tests` projesi eklendiğinde kapı **kırmızı** döner (elle doğrulandı, sonra geri alındı) — MT-PKG-103
- [x] `CHANGELOG.md` bölümü silindiğinde kapı **kırmızı** döner (kırmızı koşum belgeye yazıldı) — MT-PKG-102
- [x] 20/20 `.nuspec` `releaseNotes` alanını taşır ve URL çözümlenmiş sürümü içerir — MT-PKG-104/105
- [x] Dört adaptör test projesi `ModelProviderContract` + `ModelProviderCredentialContract` türetir; `secret` ve ağ olmadan yeşil — MT-PKG-106 (79/76/84/111 test, `Skip` yok)
- [x] `ContractCoverage` muafiyetlerinin her biri gerekçe taşır — Azure ve OpenAI, `ModelProviderSettingsContract` için (ikisi de `ProviderSettings` okumuyor)
- [x] `.github/workflows/ci.yml` `github-release` işi tanımlı; `needs` ve tag koşulu doğru — `needs: [publish, npm-publish]`, `if: startsWith(github.ref, 'refs/tags/v')`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban ef780fc` uçtan uca yeşil (build, 4779 test, pack, format, site)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — OpenAI (`support`) ve Anthropic (`claude-support`) agent'ları gerçek tamamlama döndürdü, `/api/runs`'da `Completed` olarak kaydedildi
- [x] `secret` taraması boş döndü — `kapi.py tarama` ✅
- [x] Manuel kabul case'leri `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-PKG-101..107 (107 yalnız 👤)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz — `npm run check` (dördü) yeşil
- [x] `YAYIN-HAZIRLIK.md`: BL-052, BL-015, OP-007 kapandı olarak işlendi; K-622'nin "beş" ifadesi düzeltildi — OP-006 da (yan etki) kapandı

### Doğrulama komutları

```bash
# Kapı: altı sample sayılmalı
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1 2>&1 | tee /tmp/kapi.log
grep -c "Tests succeeded" /tmp/kapi.log      # 6 beklenir

# PackageReleaseNotes 20/20 pakete aktı mı?
for f in artifacts/package/release/*.nupkg; do
  unzip -p "$f" '*.nuspec' | grep -q '<releaseNotes>' || echo "EKSIK: $f"
done

# URL çözümlendi mi? Ham $(Version) kalmamalı
unzip -p artifacts/package/release/AgentPrism.Core.1.0.0-preview.1.nupkg '*.nuspec' | grep releaseNotes

# Adaptör sözleşmeleri, secret'sız
dotnet test tests/AgentPrism.Anthropic.UnitTests -c Release
```

---

## Plandan Sapmalar

- **123.2 planın öngörmediği bir üretim kodu değişikliğine büyüdü.**
  `ModelProviderCredentialContract`'ı dört shipped adaptöre (Anthropic, Azure,
  Google, OpenAI) türetmek planın kendi uyarısı gibi gerçek bir kusur buldu:
  `Concurrent_resolution_of_one_credential_stays_stable` dördünde de kırmızı
  döndü — her biri credential başına SDK istemcisini önbelleğe alıyordu ama
  döndürdüğü `IChatClient` sarmalayıcısını her çağrıda yeniden üretiyordu.
  Kullanıcıya iki seçenek sunuldu (sözleşmeyi gevşet / dört adaptörü düzelt);
  kullanıcı adaptörleri düzeltmeyi seçti. Sonuç: yeni public tip
  `AgentPrism.TenantChatClientCacheKey` (`AgentPrism.Core`) ve dört
  `*ModelProvider.cs` dosyasında `CreateChatClientCore` değişikliği. Kayıt:
  K-646.
- **`PackageReleaseNotes` planın taslağından farklı yerde tanımlandı.**
  Plan `src/Directory.Build.props`'ta düz bir `<PropertyGroup>` önermişti; bu
  MinVer'in `$(Version)`'ı henüz boşken okunduğu için ölçüldü ve çalışmadı
  (üretilen `.nuspec` `.../blob/v/CHANGELOG.md` taşıyordu). Düzeltme:
  `BeforeTargets="GenerateNuspec"` bir `<Target>`'ın içindeki
  `<PropertyGroup>`'a taşındı — `IsAotCompatible`'ın türetildiği
  `Directory.Build.targets` deseninin aynısı, yalnız evaluation-phase/
  execution-phase ekseninde.
- **`docs-site/reference/versioning.md`'deki CHANGELOG bağlantısı hiperlink
  DEĞİL, düz dosya adı referansı.** Plan "yalnız bağlantı eklenir" diyordu;
  repo bugün **private** (`docs-site/site.config.mjs`:
  `repositoryIsPublic = false`) ve site'nin kendi içerik kapısı
  `https://github.com/farukatasoy/AgentPrism` metnini reddediyor (okuyucuya
  404 verir). `CHANGELOG.md`'nin varlığı ve konumu anlatılıyor, tıklanabilir
  GitHub bağlantısı repo açıldığında eklenebilir.
- **123.1'in envanter kapısı planın taslağından bir isim farklı.** Plan
  `SAMPLE_TEST_EXCLUSIONS: dict[str, str]` ve `validate_sample_inventory`
  fonksiyon imzasını taslak olarak veriyordu; gerçekleşen kod birebir bu
  isimleri kullandı (sapma yok, doğrulama amaçlı not).
- **Site senkron kapısı iki kuralı `--site-gerekce-yazildi` ile geçti.**
  `cekirdek-kavram` (`src/AgentPrism.Core/Models/TenantChatClientCacheKey.cs`
  → `concepts/`) ve `model-saglayici`
  (`src/AgentPrism.{Anthropic,Azure,Google,OpenAI}/*ModelProvider.cs` →
  `getting-started/first-agent.md`) tetiklendi. Gerekçe: K-646'nın düzeltmesi
  yalnız **iç** bir önbellekleme detayıdır — BYOK'un imzası, kayıt çağrıları
  ve gözlemlenebilir davranışı değişmedi (aynı `UseAnthropic`/`UseAzureOpenAI`/
  `UseGoogle`/`UseOpenAI` çağrıları, aynı `ModelProviderCredential` şekli); ne
  `concepts/` ne `getting-started/first-agent.md` yeni bir tüketici gerçeği
  anlatmıyor.
- **`scripts/dokuman-bakim.py`'deki `ARSIV_ESIK` sabiti 114'ten 88'e
  düşürüldü** — K-646 kaydı `KARARLAR-INDEKS.md` bütçesini (25 000 B) 116 B
  aştı; Faz 122'nin devir notu bu tam senaryoyu önceden yazmıştı. Plan
  kapsamında değildi, dokümantasyon bütçesi altyapısının doğal bir sonucu.

## Bu Fazda Verilen Kararlar

- **K-646** — Tenant credential ile üretilen `IChatClient`, dört sevk edilen
  adaptörde de (credential, model, `ProviderSettings`) başına önbelleğe
  alınır; paylaşılan anahtar `AgentPrism.Core.TenantChatClientCacheKey`
  olarak eklendi *(kullanıcı kararı)*. Bkz. `docs/KARARLAR.md`.

## Denetim Bulguları

`faz-denetim`, taze bağlamlı ayrı bir agent olarak koştu (çalışma ağacına
karşı, taban `ef780fc`). Dört adaptörün gerçek `dotnet test` koşumunu ve
`kapi.py yayin --kuru --surum 1.0.0-preview.1`'in kendi çıktısını bağımsızca
doğruladı.

**🔴 Kapanmadan faz bitmez:** yok.

**🟡 Aynı fazda kapanır veya gerekçelenir:**

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `TenantChatClientCacheKey`'in XML dokümanı "the four shipped factories already give the setup-time client, which is built once and shared" diyordu — kod bunu yalanlıyor (setup-time yolu da her çağrıda taze sarmalayıcı üretiyor, yalnız alttaki SDK client paylaşılıyor). | **Düzeltildi.** Cümle "yalnız SDK client paylaşılır, sarmalayıcı değil" diye yeniden yazıldı. |

**🟢 Aday listesine / gerekçelendi:**

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | Reflection tabanlı `CredentialKeyProbe` (~70 satır) dört adaptörün contract test dosyasında birebir kopyalanmış. | **Gerekçelendi, aday açılmadı.** Repo'nun mevcut deseniyle tutarlı (her adaptörün kendi `SecretLeakTests`/health-check testleri de bağımsız kopyalar taşıyor); dört ayrı, birbirinden habersiz test projesi arasında paylaşılan bir yardımcı proje açmak bu fazın kapsamının çok üstünde bir refactor olurdu. |
| 2 | Yeni `github-release` CI işi `${{ github.ref_name }}`'i bir Python string literaline gömüyordu (teorik kaçış riski). | **Düzeltildi** (bedelsizdi) — `env: TAG_NAME` ile taşınıp `os.environ['TAG_NAME']` ile okunuyor. |
| 3 | Fazın "Planlanan Public API" bölümü "Bu faz .NET public yüzeyine dokunmaz" diyordu; K-646 bir public tip ekledi. | **Gerekçelendi** — Plandan Sapmalar ve Gerçekleşen Public API bölümlerine açıkça yazıldı. |

**Temiz çıkan başlıklar:** 3.1 (DoD tek tek ölçüldü), 3.2 (test tiyatrosu
yok), 3.3 (sözleşme testi + gerçek `dotnet test`/`kapi.py yayin` koşumu —
doğru seviye), 3.4 (`Concurrent_resolution_of_one_credential_stays_stable`
eşzamanlılığı kanıtlıyor), 3.5 (imza-gövde kayması yok), 3.6 (`ContractCoverage`
muafiyetleri kanıtlı gerekçe taşıyor), 3.7 (İngilizce/`TryAdd*`/`ConfigureAwait`/
`secret` kuralları temiz), 3.8 (`versioning.md` bağlantısı eklendi, manuel test
case'leri eklendi, iç referans sızmadı).

## Sonraki Faza Devir Notu

- **Yayın kritik yolu kapandı.** BL-052, BL-015, OP-006, OP-007 hepsi
  `docs/YAYIN-HAZIRLIK.md`'de kapalı. Kalan `preview.1` tag'i öncesi işler:
  doküman drift taraması ve OP-002/004/005 hesap kararları (kullanıcı
  ertelemesi, repo-dışı) — bkz. `docs/YAYIN-HAZIRLIK.md` başlık özeti.
- **🚨 `PackageReleaseNotes` gibi `$(Version)`'a bağlı herhangi bir MSBuild
  özelliği düz bir `<PropertyGroup>`'ta TANIMLANAMAZ.** MinVer `$(Version)`'ı
  bir TARGET'te hesaplar (execution phase); düz `<PropertyGroup>` (evaluation
  phase) onu her zaman boş okur. Çözüm:
  `BeforeTargets="GenerateNuspec"` bir `<Target>`'ın içindeki
  `<PropertyGroup>`. `IsAotCompatible`'ın props-vs-csproj sırası tuzağıyla
  AYNI SINIF, farklı eksen (evaluation-vs-execution). Kanıt:
  `src/Directory.Build.props` satır ~72.
- **🚨 `ModelProviderCredentialContract`'ı türetmek, bir adaptörün credential
  wrapper'ını önbelleğe almadığını yakalar.** Yeni bir üçüncü/dördüncü taraf
  provider yazan biri bu contract'ı türetirse aynı testi görür — sözleşmenin
  kendi XML dokümanı artık ("shared SDK client, not the wrapper") doğru.
- **Devralınan sözleşme:** `AgentPrism.Testing.Contracts.Xunit` paketinin dört
  provider contract'ı (`ModelProviderContract`,
  `ModelProviderCredentialContract`, `ModelProviderSettingsContract`) artık
  dört shipped adaptörün TAMAMINDA gerçek testle koşuyor —
  `tests/AgentPrism.{Anthropic,Azure,Google,OpenAI}.UnitTests/*ModelProviderContractTests.cs`.
  Bir provider adaptörüne yeni bir `ProviderSettings` anahtarı eklenirken
  `ModelProviderSettingsContractTests`'in `SupportedSetting`'ini güncellemeyi
  unutma.
- **Devralınan sözleşme:** `scripts/release_extension_samples.py`'nin
  `SAMPLE_TEST_PROJECTS` ∪ `SAMPLE_TEST_EXCLUSIONS` artık `samples/*.Tests`
  envanteriyle TAM eşleşmek zorunda (`validate_sample_inventory`). Yeni bir
  `AgentPrism.Samples.<Ad>.Tests` dizini açıldığında bu iki kümeden birine
  bilinçli olarak eklenmezse `kapi.py yayin` kırmızı döner.
- **Yarım kalan iş yok** — üç kalemin (BL-052, BL-015, OP-007) üçü de
  kapandı, `kapi.py yayin --kuru --surum 1.0.0-preview.1` uçtan uca yeşil.
- **Sıradaki faz `YOL-HARITASI.md`'de henüz planlanmadı** (Faz 122'nin devir
  notundan miras). Kullanıcı kararı gerekiyor: yeni bir `aday-kesfi`/
  `faz-planlama` turu, ya da `nuget-danismani`'nin yayın kararı turu
  (kalan tek büyük engel: OP-002/004/005 hesap kararları ve gerçek `v*`
  tag'inin kendisi).

# Faz 96 — Public Yüzey Küçültme

> **Durum:** ✅ Tamamlandı (2026-08-24)
> **Kaynak:** [`arsiv/kesif/2026-08-23-yapisal-sorun-envanteri.md`](../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **madde 7** (public yüzey yayın kararından önce şişti). Kalem `ADAYLAR.md`'de değildir; F numarası yoktur. Sıra bölüm 7.2'de kullanıcı tarafından sabitlendi (sıra 2).
> **Önkoşul:** [Faz 95](95-GERCEK-TUKETICI-KAPISI.md) — bir tipi `internal`'a çekmek gerçek tüketiciyi kırabilir; bunu yalnız `PackageReference` ile derlenen bir proje ölçer. Faz 95'in `ConsumerRunTests`'i o dedektördür ve **kurulmuştur**.
> **Paketler:** `AgentPrism.Core`, `.Abstractions`, `.AspNetCore`, `.Mcp`, `.OpenAI`, `.Anthropic`, `.Azure`, `.Google`, `.Voice`, `.PostgreSql`, `.SqlServer`, `.Sqlite`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Küçülüyor.** `PublicAPI.Unshipped.txt` dosyalarından satır **silinir**. `Shipped.txt` dosyalarının hepsi boştur (ölçüldü: `wc -l src/*/PublicAPI.Shipped.txt` → 16 dosya × 1 satır) — bugün silmek bedavadır, Faz 97'den sonra bir sürüm kararıdır.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/api/` **üretilir** ve küçülür (bugün 732 dosya); elle yazılmış sayfalarda tip adı geçerse düzeltilir · sevk edilen: `internal`'a çekilen tipin XML dokümanı artık paketle sevk edilmez — `<see cref>` bağı kıran her yer düzeltilir
> **Manuel test alanı:** [`docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md) — alan kodu `PKG`, sıradaki case `MT-PKG-094`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 86f74a6:docs/arsiv/fazlar/96-PUBLIC-YUZEY-KUCULTME.md
> ```
>
> Damıtıldı 2026-08-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism 716 public tip ve 8.063 API girdisi ile hiç yayınlanmadı. Yüzeyi küçültmenin bedeli **bugün sıfırdır**: `Shipped.txt` dosyalarının hepsi boştur, yani hiçbir tip henüz bir uyumluluk sözü taşımıyor. Faz 97 (yayın) `Unshipped`'i `Shipped`'e taşıdığı an her satır bir söze dönüşür ve geri almak kırıcı bir değişiklik olur.

## Bitiş Ölçütleri (DoD)

- [x] 96 adayın **her biri** için karar kaydedildi: `internal` oldu, ya da public kaldı + tek cümlelik gerekçe (96.1'deki üç soruya cevap). Kayıt "Gerçekleşen Public API" bölümündedir — 96/96 `internal` oldu, sıfırı public bırakıldı
- [x] `find src -name PublicAPI.Unshipped.txt -exec cat {} + | grep -vE '^\s*$|^#' | grep -vE ' -> |\(' | wc -l` **716'dan küçük**; yeni sayı belgeye yazıldı — **620**
- [x] `src/AgentPrism.Core/Properties/AssemblyInfo.cs` üç yeni `InternalsVisibleTo` satırını taşıyor ve her biri yorumla gerekçelendirilmiş — planlanan üç (`PostgreSql`/`SqlServer`/`Sqlite`, K-176 `Auditing*` + `InMemoryRunStore`) eklendi; uygulama sırasında **altı** ek satır daha gerekti (test projeleri, bkz. Plandan Sapmalar)
- [x] `PublicSurfaceBaselineTests` yeşil; `public-surface-baseline.txt` yeni sayıları taşıyor; MT-PKG-095 koşuldu ve kapı **kırıldı**
- [x] `PublicApiTrackingDeclarationTests` yeşil; dört mevcut istisna (`Generators`, `Templates`, `Client`, `Cli`) **hâlâ** istisna; MT-PKG-096 koşuldu ve kapı **kırıldı**
- [x] `ConsumerSurfaceTests` `internal` yapılmış bir tipin tüketici projesinde **derlenmediğini**, karşılık gelen arayüzün derlendiğini kanıtlıyor; MT-PKG-094 koşuldu
- [x] Tüm test metotları yeşil (`dotnet test AgentPrism.slnx -c Release --no-build` → 0 failed); hiçbir test **davranış** değişikliği için düzeltilmedi (yalnız `internal` erişimi için `InternalsVisibleTo` veya taşıma) — DoD taslağındaki "2.897" bayat bir sayıydı, ölçülmedi, düzeltilmedi
- [x] K-285 için yeniden açma kararı `docs/KARARLAR.md`'ye yazıldı (engel Faz 88'de kalktı) — K-601
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban d3d9f4b`
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` içine eklendi (MT-PKG-094/095/096); üçü de koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi: elle yazılmış sayfalarda artık `internal` olan tip adı kalmadı; `npm run check` temiz; `api/` yeniden üretildi (732 → 636 dosya)

### Doğrulama komutları

```bash
# yuzey sayimi - once ve sonra
find src -name PublicAPI.Unshipped.txt -exec cat {} + | grep -vE '^\s*$|^#' | grep -vE ' -> |\(' | wc -l

# paket basina tip
for f in src/*/PublicAPI.Unshipped.txt; do
  printf "%-32s %s\n" "$(basename $(dirname $f))" \
    "$(grep -vE '^\s*$|^#' $f | grep -vE ' -> |\(' | wc -l | tr -d ' ')"
done

# yeni kapilar
python3 scripts/kapi.py test --proje AgentPrism.Core.UnitTests --sinif PublicSurfaceBaselineTests
python3 scripts/kapi.py test --proje AgentPrism.Core.UnitTests --sinif PublicApiTrackingDeclarationTests
python3 scripts/kapi.py test --proje AgentPrism.Package.Tests  --sinif ConsumerSurfaceTests

# mandal tazeleme (yalniz bilincli kucultme sonrasi)
AGENTPRISM_PUBLIC_SURFACE_REFRESH=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release

# kapanis
python3 scripts/kapi.py kapanis --taban <faz oncesi commit>
```

---

## Plandan Sapmalar

1. **Açık Soru 2'nin öncülü yanlıştı — ölçülüp düşürüldü.** Plan
   `OpenAIProviderOptionsValidator`'ın `AgentPrism.Azure` tarafından gerçek
   kodda kullanıldığını iddia ediyordu (kanıt olarak
   `AzureOpenAIProviderOptionsValidator.cs` gösteriliyordu) ve buna göre bir
   `InternalsVisibleTo("AgentPrism.Azure")` öngörüyordu. Uygulama anında
   (`faz-uygulama` Adım 1) ölçüldü: `AzureOpenAIProviderOptionsValidator`
   kendi bağımsız doğrulamasını yazar, `OpenAIProviderOptionsValidator`'a hiç
   değinmez. Tam çözüm build'i (`OpenAIProviderOptionsValidator` `internal`
   yapıldıktan sonra) `AgentPrism.Azure`'da sıfır hata verdi. Ek
   `InternalsVisibleTo` **eklenmedi** — gerek yoktu.
2. **`InternalsVisibleTo` deseni dışında kalan tip sayısı plandan farklı
   çıktı — dokuz satır, üç değil.** Plan yalnız üç yeni satır (K-176 SQL
   sağlayıcıları için) öngörüyordu; 96.2'nin son paragrafı ayrıca "sekiz tip,
   beş test projesinin `InternalsVisibleTo` deseni dışında" diyordu (tip
   tip). Gerçek build hataları **altı test projesi** için targeted
   `InternalsVisibleTo` gerektirdi: `AgentPrism.Mcp.UnitTests`,
   `AgentPrism.Workflows.UnitTests`, `AgentPrism.AspNetCore.FunctionalTests`,
   `AgentPrism.PostgreSql.IntegrationTests`, `AgentPrism.SqlServer.IntegrationTests`,
   `AgentPrism.Sqlite.IntegrationTests` (`AgentPrism.Core.UnitTests` desenin
   içindeydi, ek satır gerekmedi — plan burada da yanılmıştı). Açık Soru
   1'in "tip tip karar" seçeneği B (targeted `InternalsVisibleTo`) her
   durumda seçildi: test bir kardeş paketin **gerçek** wiring'ini sınıyordu
   (MCP keşif/kiracı tool'ları, workflow test fikstürü, SQL içerik koruması
   entegrasyonu), taşımak kapsam sınırını bulanıklaştırırdı.
3. **`docs-site`'ın `api/` üretim hattında ölçülmemiş bir kusur bulundu ve
   düzeltildi.** `docfx metadata` artımlıdır: bir tip `internal` olunca eski
   `.md` dosyasını `docfx/api-md` altında **silmez**, bırakır. Bu, `internal`
   yapılan 96 tipin sayfasının site üretiminde hayalet olarak kalmasına yol
   açıyordu (ölçüldü: temizlik olmadan "714 tip", temizlikle **618 tip**).
   Düzeltme `docs-site/scripts/build-api-reference.mjs`'e eklendi:
   `runDocfx()` artık `docfx metadata`'yı çağırmadan önce ara dizini siliyor.
   Bu script değişikliği planda yoktu; kapsamı yalnız bu hatayı kapatıyor.
4. **DoD taslağındaki "2.897 test metodu" sayısı hiç ölçülmedi, bir önceki
   fazdan miras kalan bayat bir referanstı.** Kapanışta ölçülen gerçek
   ölçüt `dotnet test AgentPrism.slnx -c Release --no-build`'in `0 failed`
   dönmesidir; mutlak sayı DoD'nin iddiasının parçası değildir.

## Bu Fazda Verilen Kararlar

- **K-601** — Public yüzey erişilebilirlik ölçütüyle daraltıldı: 96 tip
  `internal` yapıldı (K-285'i yeniden açar).

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir alt agent ile koşuldu (taban: `d3d9f4b`).

🔴 ve 🟡 yok — üç 🟡 bulgu uygulayan oturum tarafından bu kapanışta kapatıldı:

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | DoD'nin "gerçek `run`" satırı için kanıt yoktu | **Düzeltildi** — `samples/AgentPrism.Api` çalıştırıldı (bkz. aşağıdaki gerçek çıktı) |
| 2 | `AssemblyInfo.cs` yorumu "beş" diyordu, altı satır vardı | **Düzeltildi** — yorum "altı" olarak güncellendi |
| 3 | Açık Soru 2'nin sapması hiçbir yere yazılmamıştı | **Düzeltildi** — bu bölümün "Plandan Sapmalar #1"i |

🟢 (aday listesine, kapsam dışı): `InMemoryTenantStore`'un Türkçe XML doc
yorumu (`src/AgentPrism.Core/Storage/InMemoryApprovalAndMcpStores.cs:197`,
diff'e dokunulmamış, bu fazdan önce vardı) — `docs/ADAYLAR.md`'ye taşınmadı,
küçük ve `SourceLanguageTests` taban çizgisi zaten farkında.

### Gerçek `run` kanıtı

`samples/AgentPrism.Api` bağımsız değişkensiz (bağlantı dizisi yok →
`InMemoryRunStore`, API anahtarı yok → `EchoModelProvider`) `http://localhost:5080`
üzerinde ayağa kalktı. `GET /agentprism/api/diagnostics`:

```json
{
  "persistenceProvider": "InMemory",
  "extensionPoints": [
    { "contract": "IToolAuthorizationHandler", "implementation": "AllowAllToolAuthorizationHandler", "isBuiltInDefault": true }
  ]
}
```

`AllowAllToolAuthorizationHandler` (E-1, bu fazda `internal` yapıldı) DI
üzerinden çözüldü — teşhis ucu adını okuyabiliyor, tüketici onu adlandıramıyor.
`POST /agentprism/api/agents/support/run` `{"message":"What is the status of
order ORD-1?"}` gerçek bir SSE akışı üretti (`run` olayı, `update` olayları,
`Echo: What is the status...` metni); `GET /agentprism/api/runs/{runId}`
`"status": "Completed"` döndü — `InMemoryRunStore` (Dilim E-2, `internal`)
üzerinden gerçekten kalıcı hâle geldi.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `PublicSurfaceBaselineTests` ve `public-surface-baseline.txt` artık
  dördüncü mandal — tazeleme: `AGENTPRISM_PUBLIC_SURFACE_REFRESH=1 dotnet
  test tests/AgentPrism.Core.UnitTests -c Release`. Kapı **tip** sayar, girdi
  değil.
- `PublicApiTrackingDeclarationTests` her yeni packable paketin
  `PublicAPI.{Shipped,Unshipped}.txt` **ya da** açık
  `AgentPrismPublicApiTrackingEnabled=false` taşımasını zorunlu kılar.
- `SurfaceProbeProject` (`tests/AgentPrism.Package.Tests/Infrastructure/`) bir
  tipin tek başına erişilebilirliğini (derlenir/derlenmez) ölçen minimal
  konsol proje yazıcısıdır — `ConsumerProject`'ten ayrı tutulur çünkü o proje
  **her zaman** derlenip çalışmalıdır.

**Bilinen tuzaklar (🚨):**
- 🚨 `docfx metadata` artımlıdır ve `internal` olan bir tipin eski sayfasını
  SİLMEZ. `docs-site/scripts/build-api-reference.mjs`'in `runDocfx()`'i bu
  fazda düzeltildi (`docfx/api-md`'yi her koşumda siliyor) — API yüzeyini
  küçülten her gelecek faz bu düzeltmeye güvenebilir, tekrar keşfetmesi
  gerekmez.
- 🚨 `~/.nuget/packages/agentprism*` temizlenmeden yapılan manuel bir
  `dotnet pack` + `dotnet build` tüketici probu, sürüm numarası (MinVer'in
  git-yüksekliği) değişmediyse **eski çıkarılmış paketi** kullanır ve
  yanıltıcı biçimde "derlendi" der (`TemplateFixture.ClearGlobalPackageCache`
  bunu otomatik testlerde zaten yapıyor; elle tekrarlarken unutma).
- 🚨 `internal` yapılan bir tipe `using <Namespace>;` altında niteliksiz adla
  erişmeye çalışmak **CS0246** değil **CS0122** verir (ad çözümü tipi bulur,
  yalnız erişilemez olduğunu bildirir). Planın MT-PKG-094 taslağı bunu yanlış
  tahmin etmişti; ölçülüp düzeltildi.

**Yarım kalan iş:** Yok.

**Sıradaki faz:** Faz 97 — sürüm politikası ve ilk yayın (madde 2 + madde 1,
`docs/kesif/2026-08-23-yapisal-sorun-envanteri.md` bölüm 7.4'teki sıra
tablosu). Henüz planlanmadı; `faz-planlama` skill'i ile yazılacak.

# Faz 95 — Gerçek Tüketici Kapısı

> **Durum:** ✅ Tamamlandı (2026-08-24)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](../../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **madde 10** (yeşil test gerçek davranışı kanıtlamıyor) + **madde 22** (bağımlılık kirliliği kuralı kendi istisnasını taşıyor). Kalemler `ADAYLAR.md`'de değildir; F numarası yoktur.
> **Önkoşul:** Yok
> **Paketler:** `src/` **değişmiyor**. İş `tests/AgentPrism.Package.Tests` (bugünkü `AgentPrism.Templates.Tests`) içindedir.
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — `src/*/PublicAPI.Unshipped.txt` dosyalarına satır eklenmez.
> **Tüketici yüzeyi:** site: `docs-site/src/content/docs/packages.md` → `## What does not enter your graph` bölümü geçişli **beyanı** kazanır (madde 22) · sevk edilen: Yok
> **Manuel test alanı:** [`docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md`](../../manuel-test/24-TEST-PAKETI-VE-SABLON.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 59e0b2e:docs/arsiv/fazlar/95-GERCEK-TUKETICI-KAPISI.md
> ```
>
> Damıtıldı 2026-08-24 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism bir NuGet paket ailesidir, ama bugün hiçbir kapı **bir paket tüketicisinin gerçek bir `run` koşturabildiğini** kanıtlamıyor. Bu faz o tek boşluğu kapatır: `.nupkg`'lerden `PackageReference` ile beslenen bir tüketici projesi üretilir, içinde ağa çıkmayan bir model ile gerçek bir `run` koşulur ve `run` kaydı doğrulanır.

## Bitiş Ölçütleri (DoD)

- [x] `dotnet test tests/AgentPrism.Package.Tests` yeşil; `ConsumerRunTests` alt sürecin `stdout`'unda `OK run=` satırını doğruluyor
- [x] `ConsumerRunTests` üretilmiş bir tool'un **yürütüldüğünü** kanıtlıyor (`ShouldHaveCalledTool`)
- [x] `ConsumerRunTests` `run` kaydını ve en az bir `run_event`'i doğruluyor
- [x] Manuel case 3 koşuldu: analyzer paketleme hedefi devre dışıyken kapı **kırılıyor** — çıktı belgeye yazıldı (MT-TEST-071)
- [x] `TransitiveDependencyTests` üç tüketici şekli için taban çizgisiyle eşleşiyor; manuel case 2 koşuldu ve kapı **kırıldı** (MT-TEST-072)
- [x] `AgentPrism.Templates.Tests` → `AgentPrism.Package.Tests` yeniden adlandırması tamam; `AgentPrism.slnx` ve `AgentPrism.no-docker.slnf` güncel; `docs/arsiv/` **değişmedi**
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/24-TEST-PAKETI-VE-SABLON.md` içine eklendi; üçü de koşuldu (MT-TEST-070/071/072)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (1 🟡 bulundu, düzeltildi)
- [x] `docs-site/packages.md` geçişli beyanı taşıyor; `npm run check` (dört alt kapı) temiz

### Doğrulama komutları

```bash
# 95.2 — tuketici run kapisi
python3 scripts/kapi.py test --proje AgentPrism.Package.Tests --sinif ConsumerRunTests

# 95.3 — gecisli kapanis, tek sekil icin elle
cd "$(mktemp -d)" && dotnet new web -n Probe && cd Probe
dotnet add package AgentPrism --version <packed> --source <repo>/artifacts/package/release
dotnet list package --include-transitive --format json | python3 -c "import json,sys;d=json.load(sys.stdin);print(len(d))"

# rename tamligi — arsiv haric hicbir canli dosyada eski ad kalmamali
grep -rln "AgentPrism\.Templates\.Tests" --exclude-dir=.git --exclude-dir=arsiv . 
```

---

## Plandan Sapmalar

- **95.3'ün taban çizgisi elle yazılmadı, ölçüldü.** Plan `Google.GenAI`'ın
  "11 geçişli bağımlılık" taşıdığını söylüyordu (K-205'in eski ölçümü);
  bugünkü gerçek ölçüm (`dotnet list package --include-transitive`, 2026-08-24,
  SDK 10.0.100) `AgentPrism.Google` şekli için **39** paket kimliği verdi (meta
  `AgentPrism`: 71, `AgentPrism.Core`: 30). Fark bir kusur değil — paket
  sürümleri Faz 8'den beri ilerledi. `Baselines/transitive-dependencies.txt`
  bu ÇALIŞTIRMA anındaki gerçek grafiği taşır.
- **`packages.md`'ye eklenen "What enters your graph if you opt in" bölümü
  denetimde bir 🟡 bulgu aldı ve daraltıldı.** İlk yazım "diğer üç sağlayıcı
  paketi yalnız Microsoft/System paketi ekler" diye ölçülmemiş bir iddia
  taşıyordu; `Baselines/transitive-dependencies.txt`'in `# AgentPrism`
  bölümü `OpenAI` paket kimliğini (SDK'nın kendisi) taşıdığı için cümle
  yanıltıcıydı. Cümle kaldırıldı; yalnız ölçülen `AgentPrism.Google` kalemi
  kaldı.
- **Manuel case'ler yeni bir "İzlek" harfi açmadı.** Plan izlek şemasına
  değinmiyordu; mevcut dosyanın İzlek A (yerel NuGet feed / paketleme)
  kategorisi kullanıldı — MT-TEST-070/071/072 üçü de A.
- Planın öngördüğü her şey (95.2, 95.3, 95.4, rename yüzeyi, DoD komutları)
  birebir uygulandı; kapsam veya yapısal bir sapma yaşanmadı.

## Bu Fazda Verilen Kararlar

Yok. Bu faz `src/` değiştirmedi, yeni public API/uyumluluk sözleşmesi,
güvenlik/kiracı sınırı veya kalıcı veri/migration kararı içermedi — yalnız
test altyapısı ve doküman beyanı eklendi. `docs/KARARLAR.md`'ye yeni bir
`K-NNN` girilmedi (AGENTS.md'nin karar defteri eşiği).

## Denetim Bulguları

`faz-denetim` skill'i taze bağlamlı bir agent ile koşuldu (git diff çalışma
ağacına karşı, taban `a1bc6e85319ec442f975f59b4559933ceda1f545`).

- **🔴 yok.**
- **🟡 1 — `packages.md`'nin yeni bölümü ölçülmeyen bir iddia taşıyordu**
  ("diğer üç sağlayıcı paketi yalnız Microsoft/System paketi ekler" — bu
  fazın ölçtüğü taban çizgisi dosyası bunu doğrulamıyordu, çünkü `AgentPrism`
  meta şeklinin listesi `OpenAI` SDK'sının kendisini de içeriyordu).
  **Sonuç: düzeltildi** — iddia kaldırıldı, yalnız ölçülen `AgentPrism.Google`
  kalemi bırakıldı.
- **🟢 1 — `TransitiveDependencyTests` yalnız 3 tüketici şeklini kapsıyor**,
  paketlenen 19 paketin tamamı değil. Planın kendi Açık Soru 1'i zaten bunu
  bilinçli olarak seçmişti (CI süresi maliyeti > ölçülmemiş kazanç).
  **Sonuç: devredilmedi** — plan zaten gerekçeliyordu, yeni bir `F-NN` açmaya
  gerek görülmedi.

Denetim başlıklarının temiz çıktığı bölümler: 3.1 (DoD'nin her satırı kodda/
testte karşılığını buluyor), 3.2 (test tiyatrosu yok), 3.3 (paket sınırı alt
süreç testiyle doğru seviyede kanıtlanmış), 3.4 (restore/build/run hataları
sessiz geçmiyor), 3.5 (imza-gövde eşleşmesi tam), 3.6 (public API büyümedi),
3.7 (dil/`secret`/MAF sarmalama kuralları temiz), 3.8 (rename tamlığı ölçüldü,
`llms-full.txt` senkron, manuel case'ler eklendi ve koşuldu).

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `tests/AgentPrism.Package.Tests` artık paket tüketimi ve şablon testlerinin
  **ortak** yeridir; yeni bir paket-sınırı testi (`ConsumerRunTests`,
  `TransitiveDependencyTests` desenleri) buraya eklenir.
- `ConsumerProject.WriteAsync(version, dir)` ve `DependencyProbe.ResolveGraphAsync(packageId, version, dir)`
  `internal` yardımcılardır (`Infrastructure/`) — aynı projedeki başka bir
  test sınıfı bunları doğrudan çağırabilir.
- `Baselines/transitive-dependencies.txt` yalnız **paket kimliklerini**
  taşır, sürüm taşımaz; yeni bir tüketici şekli eklenecekse aynı `# <şekil>`
  başlık deseni izlenir (`TransitiveDependencyBaseline.Read`).

**Bilinen tuzaklar (🚨):**
- 🚨 Yeni bir test dosyasına **"Faz NN"** yazma — repo kuralı **"Phase NN"**
  (İngilizce). `SourceLanguageTests`'in Türkçe kelime listesi `faz` kelimesini
  taşır ve bunu kapıda yakalar (bu fazda gerçekten yakaladı, düzeltildi).
- 🚨 Tam çözüm test koşumunda (`dotnet test AgentPrism.slnx`) tek bir
  projenin izole/tek başına geçtiği hâlde tam koşumda düşmesi **artık
  `Ui.E2ETests`'e özgü değil** — bu fazın kapanışında `Workflows.UnitTests`
  de aynı deseni gösterdi (bkz. `docs/hafiza/test-kosum-tuzaklari.md`, F-102
  ikinci vaka). Kapanışta bir proje kırmızı geldiğinde önce izole tekrar,
  sonra tam koşum tekrarı ile ayrıştır — hangi proje olduğuna bakmadan.
- 🚨 Bu makinedeki `ap-pg` konteynerinin `agentprism` şeması hâlâ eski bir
  migration checksum'ı taşıyor (Faz 94'ten devralınan durum, bu fazda da
  gözlemlendi): `samples/AgentPrism.Api`'yi varsayılan `AgentPrism:PostgreSql:SchemaName`
  ile başlatmak `0032_tenant_provider_bindings` checksum hatasıyla çöker.
  Bu fazda `AgentPrism__PostgreSql__SchemaName` ortam değişkeniyle geçici bir
  şema (`agentprism_faz95_probe`) kullanılarak atlatıldı. Konteyner henüz
  tazelenmedi.

**Yarım kalan iş:** Yok.

**Sıradaki faz:** Yok — `docs/ADAYLAR.md`'den yeni bir `F-NN` seçilmeli.

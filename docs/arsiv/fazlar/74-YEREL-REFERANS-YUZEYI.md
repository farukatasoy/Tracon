# Faz 74 — Yerel Referans Yüzeyi

> **Durum:** ✅ Tamamlandı (2026-08-20)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-121** — kapsamı ölçümle değişti; gerekçe [§74.0](#740--f-121-neden-küçüldü)
> **Önkoşul:** [Faz 73](73-TUKETICI-AGENT-DESTEGI.md) — `buildTransitive` borusu, harita üreteci ve `CapabilityCoverageTests` cırcırı oradan devralınır · [Faz 40](40-OPENAPI-YAYINI.md) — `OpenApiSnapshotTests` belgeyi çalışan host'a bağlar, bu yüzden belgeyi paketlemek kayma üretmez
> **Paketler:** `Tracon.Core` (target), `Tracon.AspNetCore` (yeni `buildTransitive`), on bir paket (`<example>` yazımı) · `docs-site/`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Büyümüyor.** `<example>` eklemek imza değiştirmez; MSBuild özelliği ve paket içeriği public API yüzeyi değildir. Ölçüldü: `wc -l src/*/PublicAPI.Shipped.txt` = 16 satır (16 paket × 1 boş satır)
> **Site etkisi:** `capabilities.md` (bir satır) · `build-agent-map.mjs` "Where to look" bölümü · üretilen `Tracon.AgentMap.md`, `llms.txt`, `llms-full.txt` **revizyonu değişir**
> **Manuel test alanı:** `docs/manuel-test/30-YEREL-REFERANS.md` (29 numarayı Faz 73 aldı) — **19 case**

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/74-YEREL-REFERANS-YUZEYI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 73 tüketicinin kod agent'ına **haritayı** verdi: hangi yetenek var, hangi çağrı onu açar. Harita 7 763 bayttır ve bunu tam olarak bilerek yapar — 39 giriş noktasını **adlandırır**, hiçbirini **anlatmaz**.

## API documentation, one file per referenced package

- Tracon.Core: /Users/…/tracon.core/0.0.0-preview.0.271/lib/net10.0/Tracon.Core.xml
- Tracon.Abstractions: /Users/…/Tracon.Abstractions.xml

## HTTP API document

- /Users/…/tracon.aspnetcore/0.0.0-preview.0.271/buildTransitive/tracon.json

## How to read them

grep -A8 'AddToolApprovalPolicy' <api-doc>        # one member, with its example
grep -o 'name="[MTP]:Tracon[^"]*Tenant[^"]*"' <api-doc>   # find the exact name first
```

Son bölüm bir reçetedir ve dosyanın en değerli yeridir. Ölçüm gösterdi ki iki
sorgu ancak **ikinci** denemede cevaplandı; sebep yanlış ad tahminiydi. Önce
adı bulan `grep`, sonra üyeyi okuyan `grep` — bu sırayı dosya kendisi öğretir.

🚨 **`WriteLinesToFile` öğe listesi `;` üzerinden bölünür.** Satırlar öğe olarak
kurulur; içinde `;` geçen bir yol satırı ikiye böler. Ölçülmedi, ama yol
üretimi `$([MSBuild]::Escape(…))` ile korunmalıdır.

### `.gitignore`

Dosya makineye özgüdür ve **işlenmemelidir**. Şablon
([`Tracon.Starter/.gitignore`](../../../src/Tracon.Templates/content/Tracon.Starter/.gitignore))
bir satır kazanır. Var olan projelerde bunu tüketici yapar; dosyanın ilk satırı
bunu söyler. **Tüketicinin `.gitignore`'una Tracon yazmaz** — başkasının
dosyasını değiştirmek K1'in ihlalidir.

---

## Bitiş Ölçütleri (DoD)

- [x] Özellik kapalıyken `dotnet build` referans dosyası **yazmaz** — gerçek paket üzerinde (`Property_unset_writes_no_file`, MT-YRF-001)
- [x] Özellik açıkken dosya oluşur; **içindeki her yol diskte var** — `LocalReferenceTests` her yolu `File.Exists` ile doğrular ve dosyanın `<?xml` ile başladığını da okur
- [x] Yalnız `Tracon.Core` referanslayan projede OpenAPI satırı **yok**; `Tracon.AspNetCore` eklenince **var** — ölçüldü: Worker 2 satır/HTTP yok, Web 9 satır/HTTP var
- [x] `tracon.json` `Tracon.AspNetCore` paketinde; paket boyutu ölçüldü: **1 034 455 → 1 100 931 B (+66 476 B, %6,4)**
- [x] İkinci build dosyaya dokunmaz — üç ardışık `-t:Rebuild` sonrası iki projenin de `mtime`'ı değişmedi (`A_second_build_leaves_the_file_untouched`, MT-YRF-008)
- [x] ~~Salt-okunur depo kökünde build başarılı~~ → **Yazılamayan dosya build'i kırmaz** (Sapma 4). Otomatik: `A_write_that_cannot_succeed_only_warns`. Elle, gerçek CI şekliyle: `warning MSB3491`, **`exit=0`**, dosya yok (MT-YRF-010)
- [x] 39 giriş noktasının **39'u** `<example>` taşır; `capability-example-baseline.txt` **boş** (yalnız 4 yorum satırı)
- [x] `CapabilityExampleTests`'in **iki yönde de** kızardığı gösterildi: `AddSkill`'in örneği silindi → `+ AddSkill: … carries no <example>`; örneksiz `UseSomething()` eklendi → **iki kapı birden** kızardı
- [x] Kapının generic üyeleri de okuduğu gösterildi — 39/39. 🚨 Plan `` `1 `` diyordu; ek **çift** ters tırnaktır ve etkilenen üye **dört**tür (`AddToolsFrom` dahil)
- [x] Çözüm derlenmemişken kapı **düşer**: `artifacts/bin/Tracon.Voice/release_net10.0` silindi → `No XML documentation was found … Build the solution first`
- [x] Harita revizyonu değişti (`e4c7b05b` → `5e144649`); `build-agent-map.mjs --check` "up to date and within budget"; harita **7 940 B** ≤ 10 240
- [x] Eski `AGENTS.md` taşıyan tüketici derlemesinde `APG0401` çıktı ve sil-derle ile **0**'a düştü — çıktı MT-YRF-011'de
- [x] Dört doğrulama kapısı sıfır uyarı verir: `build` 0/0 · `test` **4 407 test, 0 başarısız** · `pack` exit 0 · `format --verify-no-changes` exit 0
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı: `/openapi/v1.json` **127 path / 252 şema**, `/api/agents` kimliksiz `401` + `ProblemDetails`. Paketlenen belgeyle fark **dört isteğe bağlı uç**tur (A2A ×2, `api/diagnostics`, ses akışı) — belge canlı yüzeyin alt kümesidir, sapma değil
- [x] `secret` taraması boş döndü — çıkan beş satırın hepsi Faz 51/06'dan kalan test ve doküman sabitleri
- [x] Manuel kabul case'leri `docs/manuel-test/30-YEREL-REFERANS.md` içine eklendi (**19 case**); otomatikleştirilebilenler koşuldu — 1, 2, 3, 4, 5, 6, 8, 11, 12, 18, 19 gerçek tüketici üzerinde elle koşuldu
- [x] `faz-denetim` koşuldu; **üç 🔴 bulgu** çıktı ve üçü de kapandı; 🔴 kalmadı
- [x] `docs-site/` güncellendi (`capabilities.md`, `troubleshooting.md`); `check:content` 999 sayfa temiz, `npm run build` 1000 sayfa, `check-links.mjs` **126 755 bağlantı, kırık yok**

### Doğrulama komutları

```bash
# Kapali iken dosya olusmaz (dosya PROJENIN yaninda aranir)
dotnet build /tmp/tuketici/src/Consumer/Consumer.csproj \
  && test ! -f /tmp/tuketici/src/Consumer/Tracon.LocalReference.md && echo OK

# Acik iken olusur ve yollari gercek
dotnet build /tmp/tuketici/src/Consumer/Consumer.csproj -p:TraconWriteAgentsFile=true
LR=/tmp/tuketici/src/Consumer/Tracon.LocalReference.md
grep -o '/.*\.xml' $LR | xargs -I{} test -f {} && echo "yollar gercek"

# Isaret ettigi korpus gercekten cevap veriyor
grep -A 12 "AddToolApprovalPolicy" $(grep -m1 -o '/.*Tracon\.Core\.xml' $LR)

# OpenAPI paketlendi mi
unzip -l artifacts/package/release/Tracon.AspNetCore.*.nupkg | grep tracon.json

# Kapinin gercekten yakaladigi gosterilir
python3 scripts/kapi.py test --proje Tracon.Core.UnitTests --sinif "*CapabilityExampleTests*"
```

---

## Plandan Sapmalar

| # | Plan ne diyordu | Ne yapıldı | Gerekçe |
|---|---|---|---|
| 1 | Referans dosyası **`AGENTS.md` ile aynı dizine** (git köküne) yazılır ([§74.2](#742--yerel-referans-dosyası)) | Dosya **projenin yanına** yazılır (`$(MSBuildProjectDirectory)`) | 🚨 **Denetim bulgusu 3, ölçüldü.** Bir çözümde `src/Web` (meta paket) ve `src/Worker` (yalnız `Tracon.Core`) varken tek paylaşılan dosya iki cevabı birden taşıyamıyor: son derlenen proje kazanıyor, Web **HTTP belgesini kaybediyor** ve içerik derlemeden derlemeye değişiyor. İki DoD satırı ("ikinci build dosyaya dokunmaz", "yalnız Core referanslayanda OpenAPI satırı yok") bu düzende **yanlış** oluyordu. Önce birleştirme (merge) denendi ve **düşürüldü**: MSBuild'de öğe dönüşümü içinde string fonksiyonu yazılamıyor (dönüşümün ayracı da tek tırnak), ve daha önemlisi projeler **paralel** derlendiği için okuma-yazma yarışını birleştirme de çözmüyor. Proje başına dosya **yapısal olarak** doğrudur: yarış yok, birleştirme yok, `WriteOnlyWhenDifferent` dediğini yapıyor. Ölçüldü: Web 9 satır + HTTP bölümü, Worker 2 satır ve HTTP bölümü **yok**; üç ardışık derlemede iki dosyanın da `mtime`'ı değişmedi |
| 2 | Dosya `Installed version: <sürüm>` başlığı taşır | Ayrı sürüm başlığı **yok**; sürüm her yolun içindedir | Denetim bulgusu 5: `@(...->'%(NuGetPackageVersion)'->Distinct())` iki farklı sürümü `;` ile birleştiriyor ve `Include` onu **iki satıra bölüyordu**. Ölçüldü: `Core 272` + `Sqlite 271` → dosyada iki ayrı sürüm satırı. Tek bir başlık iki sürümü dürüst anlatamaz; yol zaten sürümü taşıyor (`/tracon.core/0.0.0-preview.0.272/lib/...`) |
| 3 | 🚨 `WriteLinesToFile` öğe listesi `;` üzerinden bölünür; yol üretimi `$([MSBuild]::Escape(…))` ile korunmalıdır ([§74.2](#742--yerel-referans-dosyası)) | Koruma **yazılmadı** | Plan bunu "ölçülmedi" diye işaretlemişti; **ölçüldü ve iddia düştü**: öğe dönüşümü (`@(X->'…')`) kaynak öğe başına tam **bir** çıktı öğesi üretir, değeri `;` içerse bile (`a%3Bb.dll` tek öğe kaldı). Buna karşılık **ölçülmemiş gerçek bir tuzak** çıktı: MSBuild `Include` değerinin **baştaki boşluğunu kırpar**, bu yüzden girintili markdown kod bloğu üretilemiyor — reçete çitli (```` ``` ````) bloğa çevrildi. Sapma 2'de görüldüğü gibi `;` bölünmesi **özellik enterpolasyonlu** `Include` için gerçektir, dönüşüm için değil |
| 4 | `LocalReferenceTests` "salt-okunur depo kökü" case'i taşır | Case **taşınabilir** biçime çevrildi: dosyanın yerine bir **dizin** konur | Konum proje dizinine taşınınca "salt-okunur kök" anlamını yitirdi — proje dizini `bin/`/`obj/` için zaten yazılabilir olmalıdır, salt-okunur yapılınca **derlemenin kendisi** kırılır (`MSB3021`, Tracon ile ilgisiz). Ölçülen garanti aynı kaldı ve `chmod` semantiğine bağlı olmaktan çıktı. Gerçek CI şekli (`UseArtifactsOutput` ile çıktı başka yere, kaynak ağacı salt-okunur) **elle doğrulandı**: `warning MSB3491`, `exit=0`, dosya yok — `MT-YRF-010` |
| 5 | Örnek kapısı iki iddia taşır (örnek var mı · var olmayan API öğretiyor mu) | **Dört** iddia: örnek var mı · 39 adın 39'u okundu mu (generic dahil) · var olmayan `Add*`/`Use*`/`Map*` öğretiyor mu · örnek **kendi üyesini** çağırıyor mu | Dördüncü iddia 27 elle yazılmış örneğin davet ettiği kopyala-yapıştır kusurunu kapatır. Üçüncü iddia `ForeignRegistrationMembers` listesi ister — ad şeklinden Tracon üyesi olup olmadığı anlaşılamıyor (`AddSingleton`, `AddHealthChecks`, `MapHealthChecks` Microsoft'undur). Liste **yalnız Microsoft üyelerini** taşır; oraya bir Tracon adı eklemek incelemede tam olarak yanlış iddia olarak görünür |
| 6 | 27 örnek yazılır | 27 örnek yazıldı; **ikisi hatalıydı ve denetim yakaladı** | `Configure` örneği `options.DefaultTimeout` yazıyordu — o üye `TraconToolOptions`'ta, `TraconOptions`'ta **değil** (`CS1061`). `UseA2A` örneği `o.ExposedAgents = ["support"]` yazıyordu — property salt-okunurdur (`CS0200`). İkisi de düzeltildi ve **40 giriş noktası örneğinin tamamı gerçek paketle derlendi** (aşağıda) |
| 7 | Hafıza notları `docs/hafiza/build-ve-analyzer.md`'ye eklenir | Dosya bütçeyi aştı (17 367 B > 16 000); **`docs/hafiza/paketleme-ve-dagitim.md` açıldı** *(kullanıcı kararı)* | Sınır anlamlıdır: "nasıl derlenir/analiz edilir" `build-ve-analyzer.md`'de (11 238 B), "nasıl paketlenir ve tüketiciye nasıl ulaşır" yeni dosyada (6 829 B). `MEMORY.md` bir yönlendirme satırı kazandı |

## Bu Fazda Verilen Kararlar

K-510 · K-511 · K-512 · K-513 — `docs/KARARLAR.md`.

## API documentation, one file per referenced package

- Tracon.Abstractions: /Users/…/tracon.abstractions/0.0.0-preview.0.272/lib/net10.0/Tracon.Abstractions.xml
- Tracon.AspNetCore: /Users/…/tracon.aspnetcore/0.0.0-preview.0.272/lib/net10.0/Tracon.AspNetCore.xml
…

## HTTP API document

- /Users/…/tracon.aspnetcore/0.0.0-preview.0.272/buildTransitive/tracon.json

## How to read them
…
```

### HTTP `endpoint`'leri · Arayüz payı

Yok — bu faz çalışma anına ve arayüze dokunmadı.

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir denetçiyle koşuldu (2026-08-20). Denetçi dört
kapıyı bağımsız koştu, paket içeriğini `unzip` ile açtı, target davranışını üç
ayrı tüketici deposu kurarak ölçtü ve **27 örneğin tamamını derleyiciye verdi**.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `Configure` örneği var olmayan `TraconOptions.DefaultTimeout` üyesini öğretiyor | **Düzeltildi** → `options.Tools.DefaultTimeout`. 40 örneğin tamamı derlendi |
| 2 | 🔴 | `UseA2A` örneği salt-okunur `ExposedAgents`'a atama yapıyor (`CS0200`) | **Düzeltildi** → `o.ExposedAgents.Add("support")` |
| 3 | 🔴 | Farklı paket kümesi taşıyan iki projede tek dosya yarışıyor; iki DoD satırı yanlış | **Düzeltildi** → dosya proje başına yazılır (Sapma 1). `Two_projects_with_different_references_each_get_their_own_answer` bunu kanıtlar |
| 4 | 🟡 | Kapının ikinci iddiası yalnız `Add\|Use\|Map` çağrılarını okuyor; 🔴 1 ve 2 tam bu delikten geçti. Öneri: örnekleri **derleyen** bir kapı | **Gerekçelendi + kısmen kapandı.** Metin denetimi bu iki kusuru yapısal olarak yakalayamaz: `DefaultTimeout` ve `ExposedAgents` **gerçek** API adlarıdır, yalnız yanlış tipin üzerinde kullanılmışlardır. Yakalayan tek şey derlemedir. Bu fazda 40 giriş noktası örneği elle bir doğrulama projesine çıkarılıp **gerçek paketle derlendi** (aşağıda) ve dördüncü iddia (örnek kendi üyesini çağırır) eklendi. Kalıcı bir derleme kapısı **yeni bir yetenektir**, kusur değil → `ADAYLAR.md` **F-125** |
| 5 | 🟡 | İki farklı sürümde `Installed version:` satırı ikiye bölünüyor | **Düzeltildi** → sürüm başlığı kaldırıldı (Sapma 2) |
| 6 | 🟡 | Üretilen dosya "her üye bir örnek taşır" diyor; gerçek 1838 üyede 19 | **Düzeltildi** → metin "her **kayıt giriş noktası**" diyor |
| 7 | 🟡 | Doküman naif eşleştirmenin **3** generic üyeyi atlayacağını yazıyor; gerçek **4** | **Düzeltildi** → [§74.4](#744--kapı-capabilityexampletests) tuzak tablosu; arite ekinin **çift** ters tırnak olduğu da eklendi |
| 8 | 🟢 | `ProjectReference` ile derleyen depoda dosya oluşmadığını kanıtlayan test yok | **Devredildi.** Bu depo `buildTransitive/`'i kendi üzerinde hiç yüklemez (`ProjectReference` bu varlıkları taşımaz), dolayısıyla davranış burada gözlemlenemez. Mekanizma dolaylı olarak kanıtlı: aynı filtre çerçeve referanslarını da eler ve `LocalReferenceTests` yazılan her yolun diskte olduğunu iddia eder |
| 9 | 🟢 | `MT-YRF-005` adım 2 iki değer basıyor | **Düzeltildi** → `echo $?` kaldırıldı |

**Denetçinin temiz bulduğu başlıklar:** 3.3 (test seviyesi) · 3.5 (imza-gövde) ·
3.6 (plan dışı public API yok) · 3.7 (repo kuralları: İngilizce sınırı, varsayılan
kapalı, `secret` taraması boş).

**Denetçinin notu:** doküman adımları denetimle paralel koştu; `MEMORY.md` ve üç
`docs/hafiza/` dosyası denetim kapsamı dışında kaldı. Bunlar yalnız
dokümantasyondur ve kod yolu taşımaz.

### 40 örneğin derlenmesi (denetim bulgusu 4'ün kapanış kanıtı)

Derlenmiş XML dokümanlarından 39 giriş noktasına ait **40** `<example><code>`
bloğu programatik olarak çıkarıldı (transkripsiyon hatası olmasın diye elle
kopyalanmadı), her biri bir metot gövdesine kondu ve yer tutucular
(`OrderTools`, `IOrderGateway`, `refundTool`, `OnPremiseModelProvider`,
`NightlyReportJobHandler`, `CustomerNameGuard`, `BuildTriageGraph`) ayrı bir
dosyada tanımlandı. Paketlenmiş `Tracon` + yedi sağlayıcı/depo paketi
referanslandı.

```
uretilen ornek: 40   kapsanan giris noktasi: 39/39
dotnet build -c Release  ->  Build succeeded.
```

## Sonraki Faza Devir Notu

1. 🚨 **Tüketiciye yazılan bir dosya PROJE başına yazılır, depo köküne değil.**
   Ölçüldü (bu faz): bir çözümdeki iki proje farklı paket kümesi referanslar ve
   tek bir paylaşılan dosya iki cevabı birden taşıyamaz. Birleştirme de çözmez —
   projeler paralel derlenir ve okuma-yazma yarışır. `AGENTS.md` istisnadır
   çünkü **hiç ezilmez** ve içeriği projeye göre değişmez.
2. 🚨 **MSBuild `Include` değerinin baştaki boşluğu kırpılır.** Girintili
   markdown kod bloğu üretilemez; çitli blok kullan. Sondaki `%0A` bir satırdan
   sonra bos satır üretir ve korunur.
3. 🚨 **Öğe dönüşümü (`@(X->'…')`) `;` üzerinden bölünmez, özellik
   enterpolasyonu bölünür.** Dönüşüm kaynak öğe başına tam bir öğe üretir; ama
   `Include="… $(Prop) …"` içindeki `;` satırı ikiye böler. Ayrıca dönüşüm
   ifadesi içinde string fonksiyonu **yazılamaz** — dönüşümün ayracı da tek
   tırnaktır.
4. 🚨 **Örneğin doğruluğunu yalnız derleme kanıtlar.** Metin denetimi
   `options.DefaultTimeout` (yanlış tipte gerçek ad) ve `ExposedAgents = [...]`
   (salt-okunur property) hatalarını yapısal olarak göremez. Bu fazda örnekler
   elle derlendi; kalıcı kapı **F-125**'tir.
5. **`CapabilityEntryPoints` iki kapının ortak kaynağıdır.** Yeni bir giriş
   noktası eklendiğinde iki kapı birden kızarır: haritaya bir satır
   (`capabilities.md` + `node docs-site/scripts/build-agent-map.mjs`) ve üyeye
   bir `<example>` gerekir.
6. **Statik başlatıcılar beyan sırasında koşar.** `CapabilityEntryPoints`'te
   `RepositoryRoot` `Lazy<T>` ile ve **en başta** beyan edilmiştir; sırayı
   bozmak `TypeInitializationException` verir.
7. **Paketlenmiş `tracon.json` her zaman canlı belgenin bir ALT KÜMESİDİR.**
   Ölçüldü: `samples/Tracon.Api` 127 path sunuyor, belge 123 taşıyor; fark
   dört isteğe bağlı uçtur (A2A ×2, `api/diagnostics`, ses akışı). Bu bir sapma
   değildir — belge `TraconTestHost`'un her zaman açık yüzeyini anlatır ve
   `OpenApiSnapshotTests` onu oraya sabitler.

# Faz 78 — Yetenek Haritası Erişimi

> **Durum:** ✅ Tamamlandı (2026-08-21)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-135**
> **Önkoşul:** [Faz 73](73-TUKETICI-AGENT-DESTEGI.md) — haritayı ve `APG01xx`–`APG0401` ailesini kurar · [Faz 74](74-YEREL-REFERANS-YUZEYI.md) — bu fazın genişlettiği `Tracon.LocalReference.md`'yi kurar
> **Paketler:** `Tracon.Core` (targets), `Tracon.Generators` (analyzer)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — değişiklik MSBuild target'ı, `internal` bir tanı tanımı ve bir Node üretecidir; hiçbir C# public üye eklenmez
> **Tüketici yüzeyi:** site: [`guides/coding-agents.md`](../../../docs-site/src/content/docs/guides/coding-agents.md) (§"`AGENTS.md` — the capability map" yanlış tavsiye veriyor), `capabilities.md` (tanı tablosu satırı)
> · sevk edilen: `Tracon.LocalReference.md` gövdesi (targets içinde), `Tracon.AgentMap.md` alt bölümü, `APG0402` tanı metni, `llms.txt`
> **Manuel test alanı:** [`docs/manuel-test/30-YEREL-REFERANS.md`](../../manuel-test/30-YEREL-REFERANS.md) — case'ler oraya eklenir

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/78-YETENEK-HARITASI-ERISIMI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon yetenek haritasını **`AGENTS.md` olarak** teslim ediyor. O dosya adı tüketicinindir; bir kütüphane onu ancak **boş repo'da** kazanabilir — yani yalnız `dotnet new tracon-api` durumunda.

## Capability map — read this first

- <$(TraconAgentMapFile) degeri>

Every capability Tracon ships, with the call that turns it on. Read it
before you write agent, run, tool, or evaluation code by hand.
```

Bölüm `Condition="Exists('$(TraconAgentMapFile)')"` ile yazılır. Gerekçe:
`ProjectReference` ile derleyen bir repo (Tracon'in kendisi) zaten hiçbir
satır üretmez, ama koşul yolun var olmadığı her durumu da kapatır — ölü bir yol
yazmak, hiç yazmamaktan kötüdür.

🚨 **`Overwrite="true"` + `WriteOnlyWhenDifferent="true"` semantiği korunur.**
Yeni bölüm dosyayı büyütür; ilk build'de içerik değişeceği için dosya bir kez
yeniden yazılır. Bu beklenen davranıştır, bir kusur değildir.

## Bitiş Ölçütleri (DoD)

- [x] Temiz bir tüketici projesinde `dotnet build` sonrası `Tracon.LocalReference.md` "Capability map" bölümüyle **başlar**; yazılan yol diskte vardır ve harita işaretini taşır — `LocalReferenceTests.The_first_section_is_the_capability_map_and_the_path_it_names_is_real`
- [x] `AGENTS.md`'si `Tracon.LocalReference.md`'den söz etmeyen bir depoda `dotnet build` `APG0402` üretir; tek satır işaretçi eklenince uyarı kaybolur — `TemplateAgentsFileTests.Instructions_that_never_name_the_local_reference_are_reported`. 🚨 **Denetim bunu daralttı:** yalnız yerel referans dosyası gerçekten yazılırken (Sapma 5)
- [x] `TraconUsageDiagnostics=false` `APG0402` dahil **yedi** kodun tamamını susturur — plan "altı" diyordu, aile yedi koda çıktı
- [x] Üretilmiş (işaretli) `AGENTS.md` taşıyan depoda `APG0402` **ötmez** — `UsageAnalyzerTests.APG0402_is_silent_for_a_generated_agents_file`
- [x] Dört doğrulama kapısı sıfır uyarı verir. 🚨 `dotnet build` yeşilken `dotnet format` bir `IDE1006` verdi (`_` öneki eksik alan) — dördü de koşmanın sebebi tam olarak budur
- [x] `samples/Tracon.Api` derlenir ve **yeni uyarı üretmez** — çözüm derlemesi 0 uyarı
- [x] `secret` taraması bu fazın dosyalarında boş döndü (eşleşenler `docs/arsiv/` ve `.agents/` içindeki yerel Docker kapsayıcı parolalarıdır, faz öncesinden)
- [x] Manuel kabul case'leri eklendi: `MT-YRF-020`…`027` (8 case, 19 → 27). `MT-YRF-026` ve `027` `👤 insan gerekir` işaretlidir (planın 9 ve 10 numaralı case'leri)
- [x] `faz-denetim` koşuldu; bir 🔴 bulundu ve **kapatıldı**, altı 🟡 kapandı, iki 🟢 devredildi
- [x] `docs-site/` güncellendi; `npm run check` (dört kapı: `check:content` · `build` · `check:links` · `check:weight`) temiz. 🚨 `capabilities.md` `APG0402` **satırı almadı** — o dosyada kod-kod tanı tablosu yok (Sapma 1)
- [x] `build-agent-map.mjs --check` temiz — `up to date and within budget`
- [x] `grep -c '^- \[' docs-site/public/llms.txt` → **38**; 38 bağın tamamı `check-links.mjs`'den geçiyor (`130991 internal reference(s) … none broken`) — kapının `llms.txt`'i görmesi için genişletilmesi gerekti (Sapma 4)
- [x] Sevk edilen harita iki satırı da taşır; boyut uyarısı **üretilir** (`about 400 KB`), elle yazılmaz (Sapma 6)
- [x] Harita ≤ 10 240 B (**8 391**), `llms.txt` ≤ **20 480** B (**16 617**) — plan 16 384 öngörmüştü, ölçüm bütçeyi değiştirdi (Sapma 3)
- [x] `title`/`description` silinince üreteç `exit=1` ile düşer ve sayfayı adıyla söyler: `concepts/governance.md: the page index needs both 'title' and 'description' … this page has no description.`

### Doğrulama komutları

```bash
# Harita yolu yerel referansa girdi mi, ve yol yasiyor mu
grep -A 2 "Capability map" <tuketici-proje>/Tracon.LocalReference.md
head -1 "$(grep -m1 -o '/.*Tracon\.AgentMap\.md' <tuketici-proje>/Tracon.LocalReference.md)"

# APG0402 oter mi
dotnet build <tuketici>.slnx 2>&1 | grep APG0402

# Aile tamamen susuyor mu
dotnet build <tuketici>.slnx -p:TraconUsageDiagnostics=false 2>&1 | grep -c APG0

# Indeks eksiksiz mi ve iki butce de tutuyor mu
node docs-site/scripts/build-agent-map.mjs --check
grep -c '^- \[' docs-site/public/llms.txt                                  # 38
wc -c src/Tracon.Core/buildTransitive/Tracon.AgentMap.md           # <= 10240
wc -c docs-site/public/llms.txt                                            # <= 16384

# llms-full satiri iki kopyada da var mi
grep -c "llms-full.txt" src/Tracon.Core/buildTransitive/Tracon.AgentMap.md docs-site/public/llms.txt
```

---

## Plandan Sapmalar

Planın **altı** iddiası ölçümle düştü. Hiçbiri hedefi değiştirmedi; hepsi
**nasıl**'ı değiştirdi.

### 1 — `capabilities.md`'de kod-kod tanı tablosu YOK

Plan 78.3 şöyle diyordu: *"`capabilities.md`'nin tanı tablosuna `APG0402` satırı
girer — bu dosya sevk edilen haritanın tek kaynağıdır (K-505), yani satır
eklenmezse harita kendi tanısından habersiz kalır."*

Ölçüm: o dosyanın "Coding-agent support" bölümü **yetenek** tablosudur; tek bir
`Usage diagnostics` satırı aileyi bütün olarak anlatır ve hiçbir `APG` kodu
adlandırmaz. Kod-kod tablolar başka yerdedir:
`guides/coding-agents.md` ve `troubleshooting.md`.

Sonuç: `APG0402` satırı **o iki tabloya** girdi. `capabilities.md`'de yalnız
aile tarifi genişletildi (`… and instructions that leave the map unreachable`).
Kod-kod bir satır eklemek yeni bir desen açardı ve sevk edilen haritayı her tanı
için bir satır büyütürdü — `APG0101`…`APG0401` için de hiç yapılmamıştı.

🚨 Bunun bıraktığı boşluk kaydedildi: yeni bir `APG` kodunun dokümana girdiğini
ölçen **hiçbir kapı yok** (`ADAYLAR.md` **F-136**).

### 2 — `markerLength` elle yazılmış dosyada sıfır DEĞİL

Plan 78.2: *"Elle yazılmış bir dosyada işaret olmadığı için `markerLength`
sıfırdır; konum sıfır uzunlukta bir `TextSpan` olur. Uygulama bunu doğrular —
sıfır uzunluklu span'ın IDE'de gösterilebildiği ölçülmelidir."*

Ölçüm: `ReadRevision` `markerLength`'i işaret aramasından **önce** atıyor
(`markerLength = line.Length`), yani elle yazılmış bir dosyada da ilk satırın
uzunluğudur. Konum gerçek bir span'dır ve `APG0401`'inkiyle aynıdır; sıfır
uzunluk yalnız **boş** bir `AGENTS.md`'de oluşur. İkisi de test edildi
(`APG0402_reports_instructions_that_never_name_the_local_reference` span
uzunluğunu, `APG0402_reports_an_empty_agents_file` sıfırı ölçer).

### 3 — İndeks planın öngördüğünün **%37 üstünde**; `llms.txt` bütçesi 20 480 B

Plan: indeks **5 841 B**, `llms.txt` **14 097 B**, bütçe **16 384 B**
("ölçülene %15 boşluk").

Ölçüm: indeks **8 002 B** (ortalama satır 211 B, planın saydığı 153 B değil),
`llms.txt` **16 617 B** — yani planın önerdiği bütçeyi **doğduğu gün aşıyordu**.
Aynı kalibrasyon kuralı gerçek ölçüme uygulandı: 16 617 × 1,15 ≈ 19 110 →
**20 480 B** (20 KiB).

🚨 Denetim bunu bir **tavan yükseltmesi** olarak niteledi ve haklıdır: eski
`verifyBudget` `llms`'i de 10 240'a karşı denetliyordu. Karar **K-537** olarak
yazıldı (K-214 gereği ölçümle).

Harita bütçesi (10 240) **düşürülmedi** ve harita 8 391 B'de kaldı.

### 4 — `check-links.mjs` `llms.txt`'i göremiyordu

DoD *"her bağ `check-links.mjs`'den geçer"* diyordu. Ölçüm: o betik yalnız
`dist/**/*.html` dosyalarını tarıyor; `llms.txt` düz metindir ve `public/`'ten
`dist/`'e kopyalanır — yani **hiç denetlenmiyordu**. Betik genişletildi
(`dist/llms.txt` içindeki markdown bağ satırları aynı çözücüden geçer) ve
kırmızı olduğu **görüldü**: bozuk tek bir bağ `exit=1` üretiyor.

### 5 — 🚨 `APG0402` opt-in'e BAĞLANDI (denetim 🔴 #1, kullanıcı kararı)

Planın dört tetikleme koşulunun hiçbiri "yerel referans dosyası gerçekten
yazılıyor mu" değildi. Denetim bunun sonucunu ölçtü: **hiçbir özellik açmamış**
bir tüketicide `AdditionalFiles` yine akıyor (targets'taki `ItemGroup` opt-in'e
bağlı değil, bu bilerek böyle), yani `APG0402` ötüyordu — ve önerdiği satır
**hiç yazılmayan** bir dosyayı adlandırıyordu. Ölü işaretçi, işaretçisizlikten
kötüdür.

İki seçenek kullanıcıya soruldu; **opt-in'e bağlama** seçildi. Mekanizma
`CompilerVisibleProperty` ile `TraconWriteLocalReference`'ı analyzer'a
akıtmaktır (`build_property.` öneki). Kazanılan: paketi kurmak hâlâ **hiçbir
uyarı üretmez** — targets'ın kendi sözü ve K1 korunur. Bedeli: hiç opt-in
yapmamış bir depo bir dürtme almaz; site bu yüzden reçeteyi **iki adımlı** yazar
(önce özellik, sonra satır).

### 6 — `llms-full.txt` boyutu elle yazılmadı, ÜRETİLİYOR

Plan sevk edilen haritaya `(401 KB — prefer one page above)` diye sabit bir metin
koyuyordu. Sabit sayı sessizce bayatlar. Bunun yerine boyut üretim anında
ölçülüyor ve **100 KB'a yuvarlanıyor** (`about 400 KB`). Yuvarlama bilerek
kabadır: harita gövdesinin SHA-256'sı revizyon işaretidir (K-507), ve her prose
düzenlemesinde değişen bir sayı **kurulu her tüketicinin** haritasını bayat
gösterirdi.

---

## Bu Fazda Verilen Kararlar

| Karar | Nerede |
|---|---|
| **K-537** — `llms.txt` haritadan AYRI bütçelenir (20 480 B); ikisinin ekonomisi farklıdır | `docs/KARARLAR.md` |
| **K-538** — `APG0402` yalnız yerel referans dosyası YAZILIRKEN öter; opt-in yapmamış tüketici hiçbir uyarı almaz | `docs/KARARLAR.md` |

---

## Denetim Bulguları

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `APG0402` opt-in yapmamış tüketicide de ötüyor ve önerdiği satır **ölü** bir dosyayı adlandırıyor | **Düzeltildi** — `CompilerVisibleProperty` kapısı (Sapma 5, K-538). İki fonksiyonel + iki birim testi eklendi; mutasyonla kırmızı olduğu görüldü |
| 2 | 🟡 | `Every_project_of_a_solution_reports_the_missing_pointer` satır sayıyordu; tek proje de eşiği geçiyordu | **Düzeltildi** — ayrık proje adları sayılıyor. 🚨 İlk düzeltme `HashSet.ShouldBe` ile yazıldı ve **sıralamaya takıldı** (denetim listesi 3.2'nin tam kendisi); `Distinct().Order()` ile deterministik hâle getirildi |
| 3 | 🟡 | `llms.txt` tavanı DoD'nin yazdığı sayı değil ve bu bir tavan **yükseltmesidir** | **Düzeltildi** — DoD düzeltildi, K-537 ölçümle yazıldı |
| 4 | 🟡 | Site kapıları son içeriğe karşı koşulmamıştı (`dist/` bayattı) | **Düzeltildi** — `npm run check` tam koştu; en ağır sayfa 49 873 B, harita 8 391 B |
| 5 | 🟡 | Kalite sözleşmesi yerel referans dosyasını "üç bölüm" diye anlatıyordu | **Düzeltildi** — dört bölüm, ilki harita |
| 6 | 🟡 | Planın 9/10 numaralı manuel case'leri hiçbir yere yazılmamıştı | **Düzeltildi** — `MT-YRF-026` ve `027`, `👤 insan gerekir` işaretiyle |
| 7 | 🟡 | Manuel test indeksi ve başlıkları bayat (19 case, Faz 74, eski diyagram) | **Düzeltildi** — 27 case, Faz 74 · 78, diyagram `0402` taşıyor |
| 8 | 🟢 | Yeni bir `APG` kodunun dokümana girdiğini ölçen kapı yok | **Devredildi** — `ADAYLAR.md` **F-136** |

**Denetimin doğruladığı temiz başlıklar:** 3.5 (imza-gövde kayması), 3.6 (plan
dışı public API), 3.7 (repo kuralları — sevk edilen metinde iç referans yok).

---

## Tüketici Yüzeyi Envanteri

> `tuketici-dokuman-senkronu` Adım 1. Faz üç kovanın **üçüne birden** dokundu.

| Kova | Ne değişti | Üretilen mi |
|---|---|---|
| `docs-site/` | `guides/coding-agents.md` (yanlış tavsiye kalktı, iki adımlı reçete, diyagram, `llms.txt` anlatısı) · `troubleshooting.md` (`APG0402` satırı + bölümü) · `capabilities.md` (aile tarifi + anlatı) | Elle |
| Sevk edilen metin | `Tracon.Core.targets` (harita bölümü · `NoWarn` · `CompilerVisibleProperty`) · `APG0402` tanı metni · `AnalyzerReleases.Unshipped.md` | Elle |
| Yerel referans ve harita | `Tracon.LocalReference.md` gövdesi **dört bölüm** oldu · `Tracon.AgentMap.md` iki "Where to look" satırı kazandı · `llms.txt` 38 satırlık indeks kazandı | Üretilir, commit edilir |

Dokunulmayanlar: `api/` · `http-api/` · `public/openapi/` · `ui.md` · ekran
görüntüleri · paket `README.md`'leri · `sidebar.mjs` (yeni sayfa yok).

### Kapı çıktıları

| Kapı | Sonuç |
|---|---|
| `ShippedDocumentationSelfContainmentTests` · `CapabilityExampleTests` · `SourceLanguageTests` | **6/6** — iki taban çizgisi de **büyümedi** |
| `LocalReferenceTests` | **10/10** |
| `build-agent-map.mjs --check` | `Agent map: up to date and within budget.` |
| `npm run check` (içerik · derleme · bağlantı · ağırlık) | `Content: 39 manual pages and 1009 total pages passed.` · `Links: 130991 internal reference(s) across 1010 pages and llms.txt, none broken.` · `Weight: 1010 pages under 57000 B gzip. Heaviest: troubleshooting/index.html at 49873 B.` |
| `dokuman-bakim.py --site-denetle --taban 6b94fb0` | `✅ Site 3 sayfada değişti` |
| `dotnet build` · `dotnet test` · `dotnet pack` · `dotnet format` | 0 uyarı · **16/16 proje geçti** · 0 uyarı · `exit 0` |

🚨 Hiçbir muafiyet listesi ve hiçbir taban çizgisi büyümedi. `check-content.mjs`
yalnız **iddia kazandı** (iki yeni kapı), eşik kaybetmedi.

---

## Sonraki Faza Devir Notu

1. 🚨 **Bir tanının önerdiği düzeltmenin GERÇEKTEN uygulanabilir olduğunu ölç.**
   Bu fazın tek 🔴'ı buydu: `APG0402` doğru koşulda ötüyordu, mesajı doğruydu,
   testleri geçiyordu — ve önerdiği satır **var olmayan** bir dosyayı
   adlandırıyordu. Soru şudur: *bu tanının dediğini harfiyen yapan bir tüketici
   ne elde eder?* Yeni bir tanı yazarken bu soruyu **her yapılandırmada** sor,
   yalnız mutlu yolda değil.
2. 🚨 **`AdditionalFiles` opt-in'e bağlı değildir; tanının kendisi bağlanmalıdır.**
   `Tracon.Core.targets:78` iki dosyayı **her zaman** analyzer'a akıtır (bu
   bilerek: bir kez açıp kapatan tüketicinin dosyası hâlâ bayatlayabilir). Yani
   bir tanı "tüketici bunu açtı mı" bilgisine ihtiyaç duyuyorsa onu
   `CompilerVisibleProperty` ile **ayrıca** almalıdır. Desen:
   `<CompilerVisibleProperty Include="X" />` → `build_property.X`.
3. 🚨 **`HashSet.ShouldBe` SIRALI eşitlik denetler.** MSBuild proje sırasını
   garanti etmez; küme karşılaştırması bu yüzden kırılgandır. `Distinct(...)` +
   `Order(...)` + liste karşılaştırması kullan, ya da her öğe için ayrı
   `ShouldContain(predicate)`. `ShouldContain("dize")` bir `HashSet<string>`
   üzerinde `MA0002` ile **derlemeyi kırar** — karşılaştırıcı ister.
4. **Devralınan sözleşme: `Tracon.LocalReference.md` dört bölümlüdür** ve
   ilki `## Capability map - read this first`. Yeni bir bölüm eklenirse sıra
   soruların sırasını korumalıdır: ne var → nasıl çağrılır → HTTP yüzeyi → nasıl
   okunur. `LocalReferenceTests` ilk bölümü **adıyla** sabitler.
5. **Devralınan sözleşme: `llms.txt` = harita + sayfa indeksi**, ve indeks
   `handWrittenPages()` dolaşımından üretilir — `llms-full.txt` ile **aynı**
   liste. Yeni bir elle yazılan sayfa ikisine de otomatik girer; `title` veya
   `description` eksikse üreteç **düşer**. Site kök `index.mdx`'i bilerek
   dışarıdadır (splash sayfası bir soru cevaplamaz).
6. **İki bütçe artık ayrı:** harita 10 240 B (8 391 dolu), `llms.txt` 20 480 B
   (16 617 dolu). Harita bütçesi ~22 yetenek satırı daha kaldırır; `llms.txt`
   ~18 sayfa daha. İkisi de üreteçte, tek yerde (`budgets`).
7. **Yarım kalan iş yok.** DoD'nin tamamı işaretlendi. İki kalem devredildi:
   **F-136** (`APG` kodu için doküman kapısı) ve **F-137** (aşağıya bak).
8. 🚨 **F-137 — `Shell_opens_and_asks_for_token_when_required` çalışma kopyasına
   göre düşüyor ve Faz 78 ile İLGİSİ YOK.** Kanıt `git stash`'tir: temiz `HEAD`'de,
   aynı kopyada **yine düşüyor**. Ölçüm matrisi dört satırdır ve okunmadan
   tekrarlanmamalıdır:

   | Koşum | Sonuç |
   |---|---|
   | `Ui.E2ETests` tek başına, bu çalışma kopyası | **5/5 düştü** |
   | Yalnız o test, izole, aynı kopya | 1/1 geçti |
   | `/private/tmp` altında `git worktree`, aynı `HEAD`, tam set | 2/2 geçti |
   | `dotnet test Tracon.slnx` içinde | 1 düştü, 1 geçti |

   Yani **deterministik değil**, ama tek başına koşan sette bu yolda neredeyse
   her zaman düşüyor. Fark diff değil, **yol/ortam** kaynaklıdır. Bir sonraki
   oturum `kusur-giderme` ile kök sebebi arasın; şüpheliler kalıcı tarayıcı
   profili, `localStorage` ve yol izinleridir. **Bu faz onu düzeltmedi ve
   gizlemedi.**

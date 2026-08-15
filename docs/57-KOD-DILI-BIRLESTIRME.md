# Faz 57 — Kod Dili Birleştirme (İngilizce)

> **Durum:** 📋 Planlandı (2026-08-15)
> **Kaynak:** Kullanıcı isteği (2026-08-15). Aday listesinde karşılığı **yoktur** —
> bu bir yetenek değil, paket kalitesi işidir.
> **Önkoşul:** Adım 0 (doküman bütçesi rahatlatması) — bu fazın **kapanışı** ona bağlı;
> `faz-tamamlama` `MEMORY.md`'ye yazar ve orada 1 bayt boşluk vardır.
> **Paketler:** Tümü (17 yayınlanan + `Generators`) · `tests/` · `samples/`
> **Yeni paket:** Yok · **Migration:** Yeni migration yok; **var olan 61 dosyanın
> checksum'ı değişir** (bkz. 57.4)
> **Public API:** **Değişmiyor.** İmzalar zaten İngilizce; yalnız XML doküman
> *içeriği* ve string *değerleri* değişir

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-232\|K-228\|K-068\|K-178" docs/KARARLAR.md
   ```
   **K-232** (sunucu yanıtları çevrilmez; API sözleşmesi tek dillidir — bu fazın
   dayanağı), **K-228** (`tr.ts` tip güvenli sözlüktür; eksik anahtar derleme
   hatasıdır — bu fazda **dokunulmaz**), **K-068** (Faz 7 ertelendi — bu faz onu
   beklemez), **K-178** (migration numaraları sağlayıcı başına bağımsızdır).
3. Önceki faz devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md
   ```
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/build-ve-analyzer.md`](hafiza/build-ve-analyzer.md) (XML doküman
   üretimi, `TreatWarningsAsErrors`, CS1591),
   [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md) (migration runner
   ve checksum davranışı)
5. Gerektiğinde: [`MIMARI.md`](MIMARI.md) bölüm 9 (sürüm politikası)

---

## Amaç

AgentPrism'in kodu Türkçe yorumlanmış, İngilizce adlandırılmış bir kütüphanedir.
Bu, geliştirme boyunca doğru tercihti. Ama `GenerateDocumentationFile` açık
olduğu için **XML doküman `.xml` dosyası olarak NuGet paketine giriyor**;
paketi kuran bir geliştirici IntelliSense'te Türkçe açıklama görüyor. İmza
İngilizce, açıklama Türkçe — bu, paketin en görünür kalite kusurudur.

Bu faz tek bir sınır kuralı kurar ve kalıcı kılar:

> **Pakete giren veya çalışma anında çalışan her şey İngilizce'dir.
> Geliştirme aparatı (`docs/`, `.agents/skills/`, doküman bakım script'i)
> Türkçe kalır.**

Bu bir çeviri işi **değildir**. Bir yorum kodun bugünkü davranışını yanlış
anlatıyorsa İngilizce'ye de yanlış geçmemelidir; kod okunur ve açıklama
yeniden yazılır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`Directory.Build.props:35`](../Directory.Build.props) | `<GenerateDocumentationFile>true</GenerateDocumentationFile>` — kök seviyede; 8.837 satır Türkçe XML doküman pakete girer |
| [`Abstractions/Agents/IAgentCatalog.cs:5-8`](../src/AgentPrism.Abstractions/Agents/IAgentCatalog.cs) | `/// Tum agent kaynaklarini tek bir gorunumde birlestiren katalog.` — en merkezî arayüzün özeti Türkçe |
| [`Abstractions/AgentPrismId.cs:68`](../src/AgentPrism.Abstractions/AgentPrismId.cs) | `throw new ArgumentException("Kimlik bir UUID surum 7 degeri degil.", ...)` — tüketiciye giden hata metni Türkçe |
| [`Templates/.../template.json:12`](../src/AgentPrism.Templates/content/AgentPrism.Starter/.template.config/template.json) | `"description": "Calisan bir AgentPrism kontrol duzlemi..."` — `dotnet new agentprism-api --help` çıktısı Türkçe |
| [`UI/frontend/src/screens/run-detail.tsx:90-92`](../src/AgentPrism.UI/frontend/src/screens/run-detail.tsx) | Frontend'deki tek gerçek Türkçe yorum bloğu; dosyanın üst satırları zaten İngilizce |
| [`Core.UnitTests/Architecture/ProblemDetailsLanguageTests.cs:18`](../tests/AgentPrism.Core.UnitTests/Architecture/ProblemDetailsLanguageTests.cs) | Testin kendi XML dokümanı `"kod yorumlari (ki proje konvansiyonu geregi Turkce kalir)"` diyor — **bu faz o konvansiyonu tersine çevirir**, dokümanı da güncellenmelidir |

> Kanıtlar 2026-08-15 tarihinde doğrulandı.

### Ölçülen kapsam

| # | Kategori | Satır | Dosya | Düzeltme tipi |
|---|---|---:|---:|---|
| 1 | **`src/` XML doküman** | **8.837** | 590 | Anlam — pakete girer |
| 2 | `src/` + `samples/` yorum | 2.212 | 221 | Anlam |
| 3 | `tests/` yorum + XML doküman | 2.529 | ~300 | Anlam |
| 4 | Türkçe test metot adı | **1.494 metot** | 258 | Yarı-mekanik |
| 5 | `src/` string (exception 55, log 33, ProblemDetails 19, diğer 800) | 905 | 180 | Anlam |
| 6 | `tests/` string (test verisi) | 747 | 176 | Çoğu mekanik |
| 7 | Migration `.sql` yorumu | 609 | 61 | Mekanik + checksum |
| 8 | Proje dosyaları (`.csproj`/`.props`/`.editorconfig`/`.json`) | 395 | 44 | Anlam |
| 9 | Frontend TS/TSX | 21 | 17 | Mekanik |
| | **Toplam** | **~17.700** | **~1.030** | |

**Kapsam dışı — bilerek:**

| Ne | Satır | Neden |
|---|---:|---|
| `src/AgentPrism.UI/frontend/src/locales/tr.ts` | 760 | Meşru çeviri sözlüğü (K-228) |
| `locales/en.ts:90` `'shell.language.tr': 'Türkçe'` | 1 | Dil seçicisinin kendi etiketi |
| `.agents/skills/**` | 422 | Geliştirme aparatı — `docs/` ile aynı sınıf |
| `scripts/dokuman-bakim.py` | 129 | Türkçe doküman üretir; İngilizce yazmak anlamsız |
| `docs/**` | ~102.000 | Karar: depo dokümanları Türkçe kalır |

**✅ Public API yüzeyi zaten temiz.** 4.014 `public`/`protected` bildirimi
tarandı; Türkçe tip veya üye adı **sıfır**. (`Ch**arac**ters` kalıbı sekiz
yanlış pozitif üretir — `CharactersBilled`, `MaxCharactersPerRequest`,
`PerMillionCharacters`.) **Bu faz kırıcı değişiklik içermez**; sürüm kararı
gerekmez ve Faz 7'yi beklemez.

---

## 57.1 — Cırcır kapısı (önce yazılır)

İş çok oturuma yayılır. Kapı olmadan ilk oturumun temizlediği dosyaya ikinci
oturum Türkçe yorum geri yazar. Bu yüzden **kapı ilk iş yazılır**, temizlik
sonra gelir.

Repo'da tam olarak bu desenin bir örneği zaten var:
[`ProblemDetailsLanguageTests.cs`](../tests/AgentPrism.Core.UnitTests/Architecture/ProblemDetailsLanguageTests.cs).
`src/**/*.cs` dosyalarını diskten okur, ASCII-Türkçe sözcük kalıbı arar,
ihlalleri listeleyip düşer. Yeni test bunun **genelleştirilmiş hâlidir** ve
aynı klasörde yaşar.

```mermaid
flowchart LR
    A["src/ tests/ samples/<br/>kaynak dosyaları"] --> B["ASCII-Türkçe<br/>sözlük taraması"]
    B --> C{"Bulgu taban<br/>çizgisinde mi?"}
    C -->|Evet| D["Geç — bilinen borç"]
    C -->|Hayır| E["DÜŞ — yeni Türkçe eklendi"]
```

**Neden `AgentPrism.Core.UnitTests/Architecture/`:**

| Aday konum | Neden değil |
|---|---|
| `tests/Shared/Contracts/` | Yalnız üç SQL entegrasyon projesine bağlıdır → **Docker gerektirir**; ayrıca assembly üzerinden reflection yapar, diskten kaynak okumaz |
| `AspNetCore.FunctionalTests/` | Host kaldırır; dil taraması için gereksiz ağır |
| **`Core.UnitTests/Architecture/`** | ✅ Docker'sız, her koşumda çalışır, `DependencyDirectionTests` + `ProblemDetailsLanguageTests` ile aynı desen ve aynı `FindRepositoryRoot` yardımcısı |

**Taban çizgisi (baseline):** `tests/AgentPrism.Core.UnitTests/Architecture/source-language-baseline.txt`.
Her satır bir dosya yolu ve o dosyada **beklenen** ihlal sayısıdır. Sayı artarsa
test düşer; azalırsa taban çizgisi güncellenmelidir (test bunu da söyler).
Yenileme, `OpenApiSnapshotTests`'in emsalini izler:

```bash
AGENTPRISM_SOURCE_LANGUAGE_REFRESH=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release
```

57.6 sonunda taban çizgisi boşalır. Boş dosya bırakılır — silinmez; varlığı
kuralın kalıcı olduğunu söyler.

**Sözlük.** `ProblemDetailsLanguageTests`'in bugünkü kalıbı 19 token taşır ve
yalnız `title:` literalinin gövdesinde arar. Yeni test tüm satırda arar, bu
yüzden sözlük genişler ve **yanlış pozitif riski doğar**. Bilinen tuzaklar:

| Kalıp | Neden yanlış pozitif |
|---|---|
| `Ch**arac**ters` | `arac` (araç) |
| `p**ad**ding`, `thre**ad**` | `ad` |
| `v**ali**date` | `ali` |
| `s**onuc**` yok ama `**son**` | İngilizce `season`, `person` |

Bu yüzden sözlük **kelime sınırıyla** (`\b...\b`) eşleşir ve İngilizce beyaz
listesi taşır. Beyaz liste testin içinde, ayrı dosyada değil — okunabilirlik
için.

> 🚨 Bu test yeni yazılırken `ProblemDetailsLanguageTests` **silinmez**.
> O test dar ve kesin bir kuralı (K-232) zorlar; yeni test geniş ve taban
> çizgili. İkisi farklı işler yapar. Yalnız eski testin XML dokümanındaki
> "kod yorumlari ... Turkce kalir" cümlesi düzeltilir.

---

## 57.2 — `Abstractions` + `Core`

590 public tipin **462'si** bu iki pakettedir (Abstractions 282, Core 180).
XML doküman kütlesinin de en büyük parçası buradadır. Tüketiciye görünen
değerin çoğu bu tek geçişte kazanılır — bu yüzden ilk içerik geçişi budur.

Yöntem, dosya dosya:

1. Tipin **kodunu** oku. Yorumun bugünkü davranışı doğru anlattığını doğrula.
2. Anlatmıyorsa **kodu** kaynak al, yorumu değil. Sapmayı kapanışta
   "Plandan Sapmalar"a yaz — bayat yorum bir bulgudur, sessizce düzeltilmez.
3. `<summary>` tek cümle, etken çatı, üçüncü tekil (".NET XML doküman
   geleneği": *"Gets the ..."*, *"Returns the ..."*).
4. `<param>`/`<returns>`/`<exception>` varsa korunur; yoksa **eklenmez** —
   kapsam büyümesi olur.
5. 🚨 içeren yorumlar kazanılmış bilgidir (`HATA-S2-001` gibi vaka
   referansları). **Bilgi kaybedilmez**; İngilizce'ye taşınır, atılmaz.

`AgentPrism.Abstractions` ve `AgentPrism.Core` AOT uyumludur; bu faz kod
üretmediği için AOT durumu değişmez.

---

## 57.3 — `AspNetCore` + `UI` + `Workflows` + `Mcp`

`AgentPrism.AspNetCore` çalışma anı string yoğunluğu en yüksek pakettir (305).
ProblemDetails, log ve doğrulama metinleri buradadır.

**Arayüz payı: yok.** Frontend'de yalnız 21 satır vardır ve hiçbiri ekran metni
değildir; `en.ts`/`tr.ts` anahtar kümesi **değişmez**, bundle ölçüsü değişmez.
Tek gerçek Türkçe blok `screens/run-detail.tsx:90-92`.

---

## 57.4 — Sağlayıcılar, depolama ve migration SQL

OpenAI, Anthropic, Google, Azure, Voice + PostgreSql, SqlServer, Sqlite,
Sql.Shared. Dört sağlayıcının sağlık denetiminde kopyalanmış kalıp vardır
(`Detail = "Yanit gecerli JSON degil."`) — birlikte değişir.

### 🚨 Migration checksum'ı — bu fazın tek gerçek tehlikesi

`MigrationDescriptor.ComputeChecksum` migration dosyasının **ham metninin
tamamının** SHA-256'sını alır; yorumlar hash'e dahildir. Satır sonu
normalize edilir, başka hiçbir şey ayıklanmaz.
`MigrationRunner.VerifyChecksum` uyuşmazlıkta `AgentPrismException` fırlatır ve
mesajı şunu söyler: *"Uygulanmis bir migration duzenlenmez."*

61 dosyanın **tamamı** migration'dır (PostgreSql 29, SqlServer 16, Sqlite 16);
`Migrations/` dışında hiç `.sql` yoktur.

**Karar (kullanıcı, 2026-08-15):** yorumlar temizlenir. Gerekçe: paket henüz
NuGet'e yayınlanmadı (K-068) ve uygulanmış migration taşıyan bir dağıtım yok.

Yordam:

1. 61 dosyanın yorumları İngilizce yazılır → 61 checksum değişir
2. **Dev veritabanları düşürülür ve yeniden kurulur.** Aksi hâlde uygulama
   açılışta fırlatır
3. `AGENTS.md`'ye kural eklenir: yeni migration yorumları İngilizce yazılır
4. Kapanışta bu bir K-kararı olur (numara kapanışta alınır)

> 🚨 **Testler bu kusuru yakalayamaz.** Testcontainers her koşumda sıfır bir
> veritabanı kurar; checksum uyuşmazlığı yalnız **var olan** bir veritabanında
> görülür. Dört kapı yeşilken uygulama açılışta çökebilir. Doğrulama yalnız
> `samples/AgentPrism.Api`'yi faz **öncesinden kalan** bir veritabanına karşı
> çalıştırarak yapılır. Bu, `MEMORY.md`'nin "birim testi yetmez" tuzağının
> birebir örneğidir.

---

## 57.5 — Proje dosyaları, şablon, örnek

395 satır; hacim küçük ama biri doğrudan kullanıcıya görünür:
`src/AgentPrism.Templates/content/AgentPrism.Starter/.template.config/template.json`
— `dotnet new agentprism-api --help` çıktısı bugün Türkçe. Üç seçeneğin
(`--persistence`, `--provider`, `--ui`) açıklamaları da buradadır.

`samples/AgentPrism.Api/Program.cs` başındaki ~201 satırlık faz-faz yorum bloğu
**Faz 59'un tutorial hammaddesidir**. İngilizce yazılırken doğrudan doküman
sitesine taşınabilecek biçimde düzenlenir — kavram başına bir paragraf, faz
numarası referansı yerine yetenek adı.

---

## 57.6 — `tests/`

2.529 satır yorum/XML doküman, **1.494 Türkçe test metodu** (2.024'ün %73'ü),
747 test verisi string'i.

Test adları hiçbir yerden referans edilmez — yalnız `[Fact]`/`[Theory]`
altındaki metot adlarıdır. Yeniden adlandırma bu yüzden **risksizdir**. Ama ad
testin spesifikasyonudur; `sed` ile çevrilmez.

İki dosyada metot adında Türkçe **karakter** vardır; derleyici kabul eder ama
düzeltilir:

- `tests/AgentPrism.AspNetCore.FunctionalTests/OnlineEvaluationEndpointTests.cs:72`
  → `Elle_puanlama_sonrasi_ozet_orneği_gosterir` (`ğ`)
- `tests/AgentPrism.Azure.UnitTests/AzureOpenAIProviderHealthCheckTests.cs:71`
  → `Adres_tanimsizsa_denetim_cagri_yapmadan_saglıksiz_doner` (`ı`)

### Senkron zorunluluğu

`src/` hata mesajı değişince onu assert eden test kırılır. **Ölçüldü: yedi
test.** Kaynak ve test **aynı commit'te** değişir.

| Dosya | Assert |
|---|---|
| `Core.UnitTests/Graph/ChildAgentInvokerTests.cs:28` | `"calistirma kaydi kapali"` |
| `Core.UnitTests/Graph/ChildAgentInvokerTests.cs:58` | `"alt calistirma siniri doldu"` |
| `Core.UnitTests/Graph/ChildAgentInvokerTests.cs:74` | `"kiracisindan cikamaz"` |
| `Core.UnitTests/Models/ModelProviderSettingsTests.cs:52` | `"ait degil"` |
| `Core.UnitTests/Models/ModelProviderRegistryTests.cs:53` | `"hic saglayici kayitli degil"` |
| `Azure.UnitTests/AzureOpenAIChatClientFactoryTests.cs:158` | `"ait degil"` |
| +1 (`Message`/`Detail`/`Title` eşitlik assert'i) | — |

---

## Planlanan Public API

**Değişiklik yok.** Bu faz hiçbir tip, üye veya imza eklemez, kaldırmaz veya
yeniden adlandırmaz. Yalnız XML doküman *içeriği* ve string *değerleri* değişir.

`PublicAPI.Unshipped.txt` dosyaları boş kalır; `EnablePublicApiTracking`
`false` olmaya devam eder (K-068).

### Arayüz payı

Yok. Ekran metni eklenmez, `en.ts`/`tr.ts` anahtar kümesi değişmez, bundle
ölçüsü değişmez.

---

## Planlanan Dosya Listesi

**Yeni dosya — iki tane:**

```
tests/AgentPrism.Core.UnitTests/Architecture/
├── SourceLanguageTests.cs              (yeni — cırcır kapısı)
└── source-language-baseline.txt        (yeni — 57.6'da boşalır)
```

**Değişen:** `src/**` (597 `.cs` + 61 `.sql` + 44 proje dosyası),
`tests/**` (342 `.cs`), `samples/**` (4 `.cs` + 1 `.json`),
`src/AgentPrism.UI/frontend/src/**` (17 dosya), `AGENTS.md` (yeni dil kuralı),
`ProblemDetailsLanguageTests.cs` (XML dokümanındaki bayat cümle).

---

## Testler

| Test sınıfı | Neyi doğrular |
|---|---|
| `SourceLanguageTests` | `src/`, `tests/`, `samples/` içinde taban çizgisinde olmayan Türkçe token yok |
| `ProblemDetailsLanguageTests` | (var olan) K-232 — `title:` literalleri İngilizce |
| `MigrationRunnerTests` (×3 sağlayıcı) | (var olan) checksum doğrulaması; **yeni veritabanında** koşar, uyuşmazlığı göremez |

Yeni sözleşme testi gerekmez — bu faz davranış değiştirmez.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Cırcır sözlüğü `tests/` içinde de tam mı uygulansın? | A: `src/` katı, `tests/` gevşek · B: üçü de katı | **B** — 57.6 zaten `tests/`'i temizliyor; iki eşik tutmak taban çizgisini karmaşıklaştırır |
| 2 | XML doküman `<example>` sayısı (bugün 590 tipte 21) bu fazda artırılsın mı? | A: hayır, Faz 59'a bırak · B: 57.2'de en çok kullanılan tiplere ekle | **A** — kapsam büyümesi; Faz 59 hangi tiplerin belgeleneceğini zaten seçecek |
| 3 | Alt geçişler tek PR mi, altı PR mi? | A: geçiş başına bir commit · B: tek büyük commit | **A** — geri alınabilirlik; ayrıca taban çizgisi her commit'te küçülür ve ilerleme ölçülebilir olur |

---

## Bitiş Ölçütleri (DoD)

- [ ] `source-language-baseline.txt` **boş**; `SourceLanguageTests` yeşil
- [ ] `grep -rln '[çğıöşüÇĞİÖŞÜ]' src/ tests/ samples/` yalnız
      `locales/tr.ts` ve `locales/en.ts` döndürür
- [ ] Üretilen XML doküman gözle denetlendi:
      `artifacts/bin/AgentPrism.Abstractions/release/AgentPrism.Abstractions.xml`
- [ ] `dotnet pack` çıktısındaki `.nupkg` açıldı; `lib/net8.0/*.xml` İngilizce
- [ ] `dotnet new agentprism-api --help` çıktısı İngilizce
- [ ] 🚨 **Faz öncesinden kalan** bir PostgreSQL veritabanına karşı
      `samples/AgentPrism.Api` çalıştırıldı; migration checksum yordamı
      (düşür + yeniden kur) belgeye yazıldı
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] `find src -name "* 2.*" -not -path "*/node_modules/*"` boş
- [ ] `AGENTS.md` dil sınırı kuralını taşıyor

### Doğrulama komutları

```bash
# Cırcır kapısı
dotnet test tests/AgentPrism.Core.UnitTests -c Release --no-build

# Pakete giren XML doküman gerçekten İngilizce mi
unzip -p artifacts/package/release/AgentPrism.Abstractions.*.nupkg \
  lib/net8.0/AgentPrism.Abstractions.xml | head -40

# Şablon açıklaması
dotnet new agentprism-api --help

# Türkçe karakter kalıntısı
grep -rln '[çğıöşüÇĞİÖŞÜ]' src/ tests/ samples/ --exclude-dir=node_modules
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Migration checksum'ı var olan veritabanını kırar; **testler görmez** | Faz öncesinden kalan bir veritabanına karşı örnek uygulama çalıştırılır (DoD maddesi) |
| Cırcır sözlüğü yanlış pozitif üretir (`Characters`, `padding`, `thread`) | Kelime sınırlı eşleşme + testin içinde İngilizce beyaz liste |
| Bayat Türkçe yorum İngilizce'ye **bayat olarak** taşınır | Yorum değil **kod** kaynak alınır; sapma "Plandan Sapmalar"a yazılır |
| Kazanılmış bilgi (🚨 vaka notları, `HATA-*` referansları) çeviride kaybolur | Bilgi taşınır, atılmaz; 🚨 işareti korunur |
| İş çok oturuma yayılırken erken temizlenen dosyaya Türkçe geri gelir | 57.1 kapısı **önce** yazılır |
| `src/` mesajı değişince test kırılır | Yedi test ölçüldü; kaynak ve test aynı commit'te değişir |
| Faz kapanışı `MEMORY.md` bütçesine takılır (1 bayt boşluk) | Adım 0 önkoşuldur; bu fazdan **önce** yapılır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir. Bayat bulunan Türkçe yorumlar da buraya yazılır.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.
> Beklenen kalemler: dil sınırı kuralı, migration yorumu politikası, cırcır kapısı.

## Gerçekleşen Public API

> Kapanışta doldurulur. Bu fazda değişiklik beklenmiyor — beklenti doğrulanır.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz (**Faz 58 — Doküman Düzeni**).

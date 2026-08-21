# Tüketici Dokümanı — Kalite Sözleşmesi

> Bu dosya **neyin doğru sayıldığını** söyler. Protokol
> [`../SKILL.md`](../SKILL.md) içindedir.
>
> Standart Faz 73–76'da ölçülerek kuruldu ve buraya **ayrıştırıldı** (K-522).
> `docs/74-*.md`, `docs/75-*.md` ve `docs/76-*.md` birikimli faz dokümanlarıdır;
> bu dosya varken okunmalarına gerek yoktur.

Her bölüm iki şey ayırır: **kapının denetlediği** kalem ve **gözle** denetlenen
kalem. Kapı yeşil olduğu için sözleşme sağlanmış sayılmaz.

---

## A · Sayfa sözleşmesi

Okur bir sayfayı açtığında ne bulacağını bilmelidir. Her elle yazılan sayfa
dört parçadan oluşur:

| Konum | Ne | Denetim |
|---|---|---|
| Frontmatter | `title` ve `description` | Kapı — `description` **70–180 karakter** |
| İlk paragraf | Bu sayfanın cevapladığı **soru** | Göz |
| Gövde | İşin kendisi | Göz |
| Son bölüm | **`## Read next`** — 1–3 bağlantı | Kapı |

**`## Read next` kuralları.** Başlık tam olarak budur; `Related`, `Next`,
`Related reference` ve `See also` **yasaktır** — üçü de geçmişte sevk edildi ve
okuru üç farklı kapanışla karşıladı. En fazla üç bağlantı olur: bir okur üçten
fazlası arasında karar veremez. Her bağlantı **neden** okunacağını söyleyen bir
cümle taşır.

**Kenar çubuğu.** `docs-site/src/sidebar.mjs` **elle** yazılır; Starlight'ın
`autogenerate` özelliği kullanılmaz. Yeni bir sayfa kenar çubuğuna elle eklenir,
yoksa kapı kırılır. Aynı dosya üç tüketiciye hizmet eder — gezinme, sayfa başına
`og:image` ve içerik kapısı — bu yüzden yeni bir **bölüm** eklerken `sectionImages`
haritası da büyür.

🚨 **Kurulum komutu.** `dotnet add package AgentPrism…` satırı `--prerelease`
veya `--version` taşımalıdır. 1.0 çıkmadı; bayrak olmadan komut çalışmaz.

---

## B · Kendi kendine yeterlik — sevk edilen metnin kuralı

> Pakete giren bir dokümantasyon metni, **yalnız tüketicinin elindeki şeylere**
> gönderme yapar.

**Tüketicinin elinde olanlar:** paketin kendi tipleri, public üyeleri,
yapılandırma anahtarları, HTTP uçları, MSBuild özellikleri ve
`https://agentprism.doayen.web.tr` adresi.

**Elinde olmayanlar:** `docs/KARARLAR.md`, faz numaraları, `K-NNN`, `F-NN`,
`K1`–`K4`, `MT-*`, `docs/NN-*.md`.

### Dönüşüm kuralı — referans değil gerekçe

Satır **silinmez**; taşıdığı bilgi kendi kendine yeten bir cümleye çevrilir.
İçerik korunur, adres düşer.

| Bugün | Yarın |
|---|---|
| `AgentPrism carries no built-in model list (decision K-032).` | `AgentPrism carries no built-in model list: model names change faster than a NuGet release.` |
| `the same rule as K-103, applied a second time` | `the same rule that governs the first approval, applied a second time` |
| `A scope does not replace role policies, it narrows them (docs/arsiv/fazlar/53-KIRACI-API-ANAHTARLARI.md, section 53.3).` | `A scope does not replace role policies, it narrows them.` |
| `The outcome of a data subject erasure request (phase 64).` | `The outcome of a data subject erasure request.` |
| `Details: docs/arsiv/fazlar/27-AZURE-FOUNDRY.md` | `Details: https://agentprism.doayen.web.tr/guides/model-providers/` |

Üç desen vardır ve iş bu üçe indirgenir:

1. **Parantez içi faz veya karar etiketi** — silinir. Cümle zaten tamdır.
2. **Gerekçe taşıyan referans** — gerekçe yerine yazılır. Bilgi **kazanılır**.
3. **Türkçe doküman adresi** — sitedeki karşılık sayfaya çevrilir; karşılığı
   yoksa **silinir**. Adres uydurulmaz.

🚨 Bir `docs/` adresi site adresine çevrilirken hedefin **var olduğu doğrulanır**.
`check-links.mjs` yalnız site içi bağlantıları görür, XML dokümanını görmez.
Kırık bağlantı üretmek, bağlantı olmamasından kötüdür.

### Kapsam

| Yüzey | Dahil mi | Neden |
|---|---|---|
| `src/**/*.cs` içindeki `///` satırları | ✅ | `.xml` olarak pakete girer |
| `src/*/README.md` | ✅ | `PackageReadmeFile` — nuget.org sayfası |
| `docs/openapi/agentprism.json` | ✅ | `AgentPrism.AspNetCore` paketine girer |
| Kök `README.md` | ✅ | GitHub karşılama sayfası; okuru tüketicidir |
| `src/**` içindeki `//` ve `/* */` yorumları | ❌ | Pakete girmez; bakımcının kaydıdır ve `docs/` referansı orada **değerlidir** |
| `tests/**`, `samples/**` | ❌ | Pakete girmez |
| `docs/**`, `.agents/skills/**`, `scripts/**` | ❌ | Geliştirme aparatı |

🚨 **Sınır `///` ile `//` arasındadır ve bilinçlidir.** Bir uygulama yorumunun
"K-320 bu konumu ölçtü" demesi **doğru** davranıştır; sonraki bakımcı o kaydı
okur. Aynı cümle `<summary>` içine girerse tüketiciye gider.

🚨 **Satır bazlı tarama yetmez.** XML yorumu kaynak genişliğinde sarılır ve
`(phase 65)` rutin olarak iki satıra bölünür; satır bazlı bir regex iki yarıyı
da göremez. `///` bloğu birleştirilir, öyle eşleştirilir. Ölçüldü: üç referans
bu delikten geçti.

🚨 **Sözleşme tiplerinde `<see cref>` kullanılmaz** — paketlenen OpenAPI onu tam
CLR imzası olarak basar. İki şekli vardır ve ilk düzeltme birini kaçırdı:
`string? X.Y` (nullable) ve `string X.Y` (düz); ikinci ölçüm **17 vaka daha**
buldu. Sınır yalnız OpenAPI'nin seri hâle getirdiği tiplerdir; iç tiplerde
`<see cref>` IDE gezinmesi için değerlidir.

**Kapı:** `ShippedDocumentationSelfContainmentTests` · `SourceLanguageTests`
(taban çizgisi **yalnız küçülür**).

---

## C · Diyagram kuralı

> Bir sayfa bir **akış**, bir **karar** veya bir **katman** anlatıyorsa, onu bir
> diyagram anlatmalıdır. Bir **liste** anlatıyorsa tablo yeterlidir.

- Her diyagram **Mermaid**'dir (repo kuralı; ASCII kutu çizimi yasaktır).
- Her Mermaid diyagramı `accTitle:` **ve** `accDescr:` taşır.
- Eşiği aşan her anlatı sayfası ya bir figür taşır (`mermaid`, `<img>`, `![]`)
  ya `DIAGRAM_EXEMPT` listesinde **gerekçesiyle** durur.
- Muafiyet listesindeki bir sayfa figür kazanırsa kapı kızarır: muafiyet düşer.

Muaf olanlar tablo sayfalarıdır — `troubleshooting.md` (aramayla okunan semptom
kataloğu), `reference/configuration.md`, `reference/compatibility.md`,
`reference/glossary.md`, `packages.md`. Beşinin de gerekçesi listenin içinde
yazılıdır.

**Kapı:** `check-content.mjs` — boyut ve içerik taraması.

---

## D · Erişilebilirlik ve ağırlık

| Kural | Denetim |
|---|---|
| Her `<img>` beş öznitelik taşır: `alt`, `width`, `height`, `loading`, `decoding` | Kapı |
| Renk token'ları **yalnız** `site.css`'in `:root` bloklarında tanımlanır | Kapı |
| Her token çifti **iki temada da** WCAG AA geçer | Kapı — kontrast hesabı |
| Her kenar çubuğu bölümünün bir `og:image`'ı var ve dosya diskte | Kapı |
| Sayfa ağırlığı gzip tavanının altında | Kapı |

Bir renk yalnız bir `@media` veya bileşen kuralı içinde tanımlanırsa kapı onu
göremez ve tema değişiminde kaybolur.

---

## E · Yerel referans ve agent haritası

**`AgentPrism.LocalReference.md`** tüketicinin diskinde, **her build'de** üretilir
ve projenin yanında durur. Makineye özgüdür; tüketici onu `.gitignore`'una
ekler. AgentPrism **tüketicinin `.gitignore`'unu değiştirmez** — başkasının
dosyasını değiştirmek sıfır sürpriz kuralının ihlalidir.

İçinde dört bölüm vardır ve **ilki** `## Capability map - read this first`:
paketin taşıdığı `AgentPrism.AgentMap.md`'nin mutlak yolu. Sonra referanslanan her
AgentPrism paketinin XML doküman yolu, `AgentPrism.AspNetCore` varsa paketlenmiş
`agentprism.json` yolu, ve bir `## How to read them` `grep` reçetesi. Sıra
soruların sırasını kodlar: önce **ne var**, sonra **nasıl çağrılır**. Reçete **önce adı bulan**, sonra üyeyi
okuyan sırayı öğretir; ölçüm bu sıranın gerektiğini gösterdi — iki sorgu ancak
ikinci denemede cevaplandı ve sebep yanlış ad tahminiydi.

Dosya elle yazılmaz. **Dolaylı** bayatlar:

| Bayatlatan | Sonuç | Kapı |
|---|---|---|
| Eksik `<summary>` | Korpusta boşluk; `grep` cevapsız döner | `dotnet build` (XML dokümanı zorunlu) |
| Giriş noktasında `<example>` yok | Reçetenin sözü tutulmaz | `CapabilityExampleTests` |
| Dosyanın adı veya konumu değişti | Agent haritasının `## Where to look` bölümü bayatlar | Göz |

**`AgentPrism.AgentMap.md`** nupkg'de sevk edilir ve **tek kaynağı**
`docs-site/src/content/docs/capabilities.md`'dir. `llms.txt` ve `llms-full.txt`
aynı kaynaktan üretilir. Üçü de **commit edilir**, çünkü `dotnet pack` haritayı
okur ve Node zinciri `dotnet build`'e bağlanamaz. Bu takas drift kapısını zorunlu
kılar: `build-agent-map.mjs --check`.

🚨 **Yeni paket iki ayrı iş ister.** XML satırını **otomatik** kazanır
(`@(ReferencePath)` süzgeci). `capabilities.md`'nin `## Packages` satırını
**elle** kazanır. İkincisi unutulursa sevk edilen harita sessizce bayat kalır —
bugün hiçbir kapı bunu yakalamıyor.

🚨 **Harita bütçesi aşımda kırpmaz, kırılır.** Bütçe 10 240 B.

---

## F · Ölçülmüş taban çizgileri — yalnız iyileşir

Her satır bir **cırcırdır**: değeri bu yönde değişebilir, tersine değişemez.
Tersine değişim `faz-denetim`'de 🔴 sayılır.

| Taban | Ölçüm (2026-08-20) | Yön |
|---|---|---|
| `DIAGRAM_EXEMPT` | 5 kalem | yalnız küçülür |
| `CLOSING_EXEMPT` | 1 kalem (`index.mdx`) | yalnız küçülür |
| En ağır sayfa | 49 376 B gzip · tavan 57 000 B | yalnız düşer |
| Kontrast — metin | 5,47:1 | yalnız yükselir |
| Kontrast — metin dışı | 3,46:1 | yalnız yükselir |
| Agent haritası | 8 062 B · bütçe 10 240 B | bütçe ölçümle değişir |
| `SourceLanguageTests` taban çizgisi | — | yalnız küçülür |

**Bir tavanı yükseltmek bir karardır.** Gerekçesi ölçümdür, tercih değil; karar
`docs/KARARLAR.md`'ye yazılır.

**Muafiyet listesi bir kaçış kapısı değildir.** İçinde duran her sayfa için
**neden** yazılır ve liste sayfayla birlikte temizlenir — artık var olmayan bir
sayfayı adlandıran liste kapıyı kızartır.

# Faz 135 — Üretilen Şemanın Nesne Grafı

> **Durum:** ✅ Tamamlandı (2026-09-02)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) — **F-176**
> **Önkoşul:** [Faz 130](130-URETILEN-SEMANIN-KISITLARI.md) — kısıt üretim yolu ve `ParameterConstraints` bu fazın üstüne oturur
> **Paketler:** `Tracon.Generators` (tek paket)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyümüyor — üretilen şema metni ve derleme anı davranışı değişir. İki yeni analyzer diagnostic kodu eklenir (`APG0011`, `APG0012`)
> **Tüketici yüzeyi:** `docs-site/`: `guides/write-your-own-tool.md`, `concepts/tools.md`, `capabilities.md` (diagnostic bağlantısının hedef bölümü) · sevk edilen: `APG0003` metni, yeni `APG0011`/`APG0012` metinleri, `AnalyzerReleases.Unshipped.md`
> **Manuel test alanı:** [`docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md) — 🚨 Faz 133'ün **133.0** kalibrasyonu uygulanmamışsa case yazılamaz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 696c436:docs/arsiv/fazlar/135-URETILEN-SEMANIN-NESNE-GRAFI.md
> ```
>
> Damıtıldı 2026-09-02 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Generator bugün skalerleri ve onların dizilerini ifade edebiliyor, kısıtlarını da Faz 130'dan beri yazıyor. Ama **nesne** ifade edemiyor. Gerçek bir business tool'unun girdisi çoğu zaman bir nesnedir: bir değerlendirme rubric'i, bir boyut listesi, bir sahne tanımı.

## Bitiş Ölçütleri (DoD)

- [x] Nesne ve nesne dizisi parametreleri doğru JSON Schema düğümü üretir (case 1–2)
- [x] Faz 130'un kısıtları ve `[Description]` nesne üyelerinde de çalışır (case 3)
- [x] `[JsonSerializable]` eksikse `APG0011` ile derleme **durur** ve eksik tipin adı yazılır (case 4)
- [x] Derinlik aşımı ve cycle `APG0012` üretir; generator asılmaz (case 5–6)
- [x] `APG0003` metni güncellendi; "never expresses a nested object" iddiası kalktı
- [x] Aynı girdi iki derlemede **bit düzeyinde aynı** şema üretir
- [x] `IncrementalGeneratorCacheTests` yeşil — model değer eşitliği korundu
- [x] 🚨 Dış tüketici projesinde `PublishAot` derlemesi **uyarısız** ve tool çalışma anında çağrılabiliyor (case 8) — `ObjectToolAotPackageTests`, `osx-arm64`, 2026-09-02
- [x] `AnalyzerReleases.Unshipped.md` iki yeni kodu taşıyor; `DiagnosticIntegrityTests` yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban b411d5b`, 2026-09-02
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (case 7) — bkz. MT-CORE-121, gerçek OpenAI (`gpt-5.4-mini`) çağrısı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md` içine eklendi (MT-CORE-115..122); otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. "Denetim Bulguları"
- [x] `docs-site/` güncellendi (`write-your-own-tool.md`, `concepts/tools.md`, `capabilities.md`, `troubleshooting.md`); `npm run check` (dört kapı) temiz
- [x] 🚨 `dotnet test Tracon.slnx` TAM log dosyasından teyit edildi — `| tail` ile **değil**; `kapi-kapanis-4.log`, 2026-09-02

### Doğrulama komutları

```bash
# Üretilen şemayı gör
dotnet build samples/... -p:EmitCompilerGeneratedFiles=true
grep -r '"additionalProperties"' obj/**/generated/

# AOT koşumu (asıl kanıt)
dotnet publish <dış tüketici projesi> -r <rid> -p:PublishAot=true
```

---

## Plandan Sapmalar

1. **135.1'in ikinci nesne şekli (parametresiz kurucu + `init`/`set` property'leri)
   uygulanmadı.** Yalnız "tek public parametreli kurucu" şekli (positional
   record'un doğal biçimi) desteklendi — tüm manuel kabul case'leri ve ölçülen
   gerçek talep bu şekli kullanıyor. İkinci şekil için "required mi, optional mi"
   sorusunun ölçülmüş bir sinyali yok (kurucu parametresinde
   `HasExplicitDefaultValue` var, property'de eşdeğeri yok). Kod bunu
   `ParameterTypeValidator.TryGetSinglePublicParameterizedConstructor`'ın XML
   dokümanında açıkça kapsam daraltması olarak işaretliyor.
2. **Üye taşımayan bir nesne (`{}` üretmesi gereken) desteklenmiyor — bu, planın
   "Hata Modları ve Testler" bölümündeki "boş/aşırı girdi" satırıyla çelişir.**
   Bağımsız denetim bunu 🟡 bulgu olarak işaretledi (aşağıya bakın). Kod
   bilinçli olarak DÜZELTİLMEDİ: yalnız public parametresiz kurucusu olan bir
   tip zaten sapma #1'in ikinci şekliyle ÇAKIŞIYOR — "üye yok" ile "henüz
   desteklenmeyen property tabanlı şekil" arasında Roslyn sembolünden GÜVENLİ
   bir ayrım yok. `Parameters.Length: > 0` şartını gevşetmek, gerçek property'leri
   olan bir tipi SESSİZCE boş şemaya (`{}`) düşürme riski taşırdı — mevcut
   davranıştan (APG0003 ile red) daha kötü bir kusur sınıfı. Karar: ikisi de
   `APG0003` ile reddedilir; "Kapsam dışı - bilerek" tablosuna bu kalem
   eklenmeliydi, plan metni burada kendiyle çelişiyordu.
3. **Nesne/nesne-dizisi şeklindeki bir parametrede (veya üyede) HERHANGİ bir
   `DataAnnotations` kısıt attribute'u her zaman `APG0010` ile "uygulanmaz"
   sayılır** — plan bunu açıkça belirtmiyordu, ama Faz 130'un "sessizce yanlış
   şema üretme" ilkesiyle tutarlı, en muhafazakar yorum bu oldu (nesne
   dizisinde `minItems`/`maxItems` için ayrı bir destek eklenmedi).
4. **Tüketici doküman sözleşmesi:** `JsonSerializerContext`'e camelCase gibi
   varsayılan-dışı bir `PropertyNamingPolicy` konması, şema ile bağlamanın
   FARKLI JSON anahtarları beklemesine yol açabilir (bağlama context'in
   `JsonSerializerOptions`'ını kullanıyor, şema üyenin ham C# adını yazıyor) —
   bağımsız denetimin 🟡 bulgusu. Ölçülmeden bir çözüm yazmak yerine
   `guides/write-your-own-tool.md`'ye açık bir uyarı eklendi: context'in
   adlandırma politikası varsayılanda kalmalı. Açık Soru 2 hâlâ kapanmadı.
5. **`samples/Tracon.Api`'ye planın öngördüğünden fazlası eklendi:**
   `estimate_shipping_cost` demo tool'u ve `ShippingAddress` record'u kalıcı
   olarak eklendi, `support` agent'ının `ToolNames`'i genişletildi. Gerekçe:
   manuel case 7'yi gerçek bir OpenAI çağrısıyla kanıtlamak için gerçek bir
   tool gerekiyordu; kalıcı bırakmak (geçici bir test-only tool yerine) diğer
   fazların örnek-uygulama zenginleştirme konvansiyonuyla tutarlı.
6. **`Tracon.Package.Tests`'e planın dosya listesinde adı geçmeyen iki yeni
   dosya eklendi** (`ObjectToolAotConsumerProject.cs`,
   `ObjectToolAotPackageTests.cs`) — planın Riskler tablosunun kendisi bu
   koşumu zorunlu kılıyordu ("DoD'de ayrı bir `PublishAot` satırı;
   `Tracon.Package.Tests` derleyip **koşar**"), yalnız dosya adları
   önceden yazılmamıştı.

## Bu Fazda Verilen Kararlar

- **K-655** — K-615 aynen genişler: bir tool'un OBJECT parametre grafındaki
  her tip de `JsonSerializerContext`'e `[JsonSerializable]` ile eklenmek
  zorundadır, yalnız complex SONUÇ tipi değil (kullanıcı kararı, 2026-09-02).
  Tam gerekçe `docs/KARARLAR.md`'de.

## Denetim Bulguları

Bağımsız denetim (`faz-denetim`, taze bağlamlı ayrı agent) 2026-09-02'de
çalışma ağacına karşı koştu; sekiz başlığın tamamı değerlendirildi.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `docs-site/troubleshooting.md`'ye iç karar-defteri referansı (`K-615`, iki yerde) sızmıştı — tüketici doküman sözleşmesi bunu yasaklar | 🔴 | **Düzeltildi** — her iki cümle de `K-615` adı geçmeden yeniden yazıldı |
| 2 | Nesne ÜYESİ üzerindeki uyumsuz bir kısıt attribute'u (`[Range]` bir `string` üyede gibi) hiçbir zaman `APG0010` üretmiyordu — `ObjectGraphState.UnsupportedConstraintAttributes` dolduruluyor ama `TryCreate`'in döndürdüğü `unsupportedConstraintAttributes`'a hiç birleştirilmiyordu | 🔴 | **Düzeltildi** — `CombineUnsupportedConstraintAttributes` eklendi (üst düzey + üye seviyesi birleşimi, üye adı mesajda görünür); regresyon testi: `ToolDiagnosticTests.A_Range_attribute_on_a_string_object_member_is_reported_and_omitted_from_the_schema` |
| 3 | Riskler tablosu naming-policy uyuşmazlığını "test aynı adı iki uçtan karşılaştırır" diye mitig ediyordu; böyle bir test yoktu — bir tüketici context'ine camelCase koyarsa üye sessizce boş kalabilir | 🟡 | **Gerekçelendi** — Açık Soru 2 zaten "ölçülmeden karar verilmez" diyordu; ölçülmüş talep yok. `guides/write-your-own-tool.md`'ye açık bir 🚨 uyarı eklendi (yukarı bakın) |
| 4 | Plan'ın hata modları tablosu üye taşımayan bir nesnenin `{}` ürettiğini vaat ediyordu; kod bunu reddediyor (`APG0003`) | 🟡 | **Gerekçelendi** — bkz. "Plandan Sapmalar" #2; kod DEĞİŞTİRİLMEDİ, çünkü düzeltme sapma #1'deki henüz-desteklenmeyen ikinci şekille çakışıp gerçek property'leri sessizce düşürme riski taşırdı |
| 5 | Dizi üzerinden kendine referans veren bir cycle'ı (`record Node(IReadOnlyList<Node> Children)`) doğrudan hedefleyen bir test yok | 🟢 | Aday listesine alınmadı — mekanizma zaten paylaşılan `TryCreateObjectType`/`ObjectGraphState` yolunu kullanıyor (kod okunarak doğrulandı), yalnız ek kapsama testi. Küçük bir F-NN açmaya değecek kadar büyük bulunmadı |

**Temiz çıkan başlıklar:** 3.1 (derinlik/cycle algoritması satır satır izlendi),
3.3 (AOT/trim seviyesi doğru — `Tracon.Package.Tests`), 3.5 (imza-gövde
kayması yok), 3.6 (plan dışı public API yok), APG0011'in graf hatası
durumunda yanlışlıkla tetiklenmediği doğrulandı.

🔴 bulgular kapandıktan sonra dört kapı yeniden koşuldu (`tests/Tracon.Generators.UnitTests`
269/269, `tests/Tracon.Core.UnitTests` 2285/2285, `tests/Tracon.Package.Tests`
ilgili testler yeşil).

## Sonraki Faza Devir Notu

- **Property tabanlı ikinci nesne şekli (parametresiz kurucu + `init`/`set`
  property'leri) hâlâ desteklenmiyor.** Ölçülmüş bir talep çıkarsa, "required"
  belirleme sinyali (nullable annotation mı, `required` C# anahtar sözcüğü mü)
  önce ölçülmeli — bu fazın kendi tecrübesi: kurucu-parametresi yolunun
  `HasExplicitDefaultValue`'su kadar net bir sinyal property'de yok.
- **`JsonSerializerContext`'in adlandırma politikası (camelCase vb.) hâlâ
  ölçülmedi (Açık Soru 2).** Şema ile bağlamanın aynı JSON anahtarını görmesi
  gerektiği doğrulanmadı; bir sonraki tur bunu ya ölçüp uygular ya da kapsam
  dışı bırakmayı resmileştirir (`docs/ADAYLAR.md`'ye taşınabilir).
- **Derinlik sayacı (`depth`) ile cycle path'i (`ObjectGraphState.Path`)
  kasıtlı olarak AYRI tutuldu** (bkz. `docs/hafiza/analyzer-yazimi.md`) —
  yeni bir üye kaynağı (ör. property tabanlı ikinci şekil) eklenirse bu ikisi
  senkron kalmalı, `path.Count`'u "derinlik" olarak yeniden kullanma.
- `docs/manuel-test/00-INDEKS.md`'nin `CORE` satırı Faz 130'dan beri
  güncellenmemişti (faz listesi ve case sayısı); bu kapanışta düzeltildi —
  bir sonraki fazın kapanışı bu satırı GÜNCEL TUTMALI (case sayısı artışını
  o anki toplam üzerinden hesapla, önceki fazın bıraktığı sayıyı değil).

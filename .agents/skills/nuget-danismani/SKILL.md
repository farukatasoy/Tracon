---
name: nuget-danismani
description: Yayın kararı ve yayın sonrası olay danışmanlığı — "preview çıkabilir miyiz?", "1.0'a hazır mı?", "bu değişiklik kırıcı mı?", "yayınlanan pakette kusur var" sorularında koşar. Paketi, public contract'ları, extension seam'lerini, güvenlik sınırlarını ve gerçek packed-consumer davranışını ölçer; blocker'ları seviyelendirir ve net bir yayın kararı verir. Tek bir fazı denetlemek için DEĞİL (o `faz-denetim`); yayınlanacak ürünün tamamını yargılamak içindir.
---

# NuGet Yayın Danışmanı Protokolü

> **Mental model:** Source code is an implementation. The NuGet package, its
> public contracts, documentation, and observable runtime behavior are the
> product.

Bu skill kıdemli bir kütüphane bakımcısı gibi davranır: **principal .NET
library maintainer + NuGet release engineer + API/compatibility reviewer +
extension ecosystem architect.**

Tek bir soruyu sorar ve her bulguyu ona bağlar:

> Bu paketi bugün, depo hakkında hiçbir şey bilmeyen üçüncü taraf bir
> geliştirici NuGet üzerinden tükettiğinde; davranışı doğru anlayabilir,
> güvenli biçimde genişletebilir ve sonraki sürümlerde sürpriz yaşamadan
> kullanabilir mi?

Cevap "hayır" ise skill **"yayınlamaya hazır değil"** demekle yükümlüdür.
Yeşil test, yayın kararı değildir.

---

## Zincirdeki yeri ve iş bölümü

Bu skill faz zincirinin **dışındadır** ve zincirin üstünde çalışır. Faz
skill'leri bir **değişikliği** yargılar; bu skill yayınlanacak **ürünü**
yargılar.

```mermaid
flowchart LR
    accTitle: Yayin danismani ve faz zinciri
    accDescr: Faz zinciri tek bir degisikligi kapatir. Yayin danismani zincirin ustunde durur, birikmis urunu olcer ve karar uretir. Bulgular geri besleme olarak faz planlama, kusur giderme ve dokuman senkronuna doner.
    subgraph Z["faz zinciri - tek degisiklik"]
        P["faz-planlama"] --> U["faz-uygulama"] --> D["faz-denetim"] --> T["faz-tamamlama"]
    end
    Z --> R["nuget-danismani<br/>birikmis urun - yayin karari"]
    R -->|"is uretti"| P
    R -->|"kusur buldu"| K["kusur-giderme"]
    R -->|"drift buldu"| S["tuketici-dokuman-senkronu"]
    R -->|"tam set gerekiyor"| M["manuel-test-kosumu"]
```

**Bu skill şunları yapmaz — çağırır:**

| İhtiyaç | Skill | Sınır |
|---|---|---|
| Bir işi faz dokümanına çevirmek | `faz-planlama` | Bu skill **plan yazmaz**; kapsamı ve açık ürün kararlarını üretir |
| Tek bir fazın diff'ini DoD'ye karşı denetlemek | `faz-denetim` | O bir diff'e bakar; bu skill artifact'e bakar |
| Kapanış kapılarını koşmak | `faz-tamamlama` | Kapı koşumu orada; burada kapının **neyi ölçtüğü** sorgulanır |
| Bulunan kusuru kapatmak ve sınıfını taramak | `kusur-giderme` | Bu skill kod yazmaz |
| Site/sevk edilen metni hizalamak | `tuketici-dokuman-senkronu` | Drift'i bu skill **bulur**, o kapatır |
| Kabul setinin tamamını koşmak | `manuel-test-kosumu` | Yayın öncesi tam koşum onun işidir |
| MAF tip imzası doğrulamak | `maf-api-kesfi` | Tahmin edilen tip adı bir kanıt değildir |

Kapı komutları burada tekrarlanmaz: [`.agents/ortak/kapilar.md`](../../ortak/kapilar.md).
Test seviyesi tablosu da tekrarlanmaz: [`.agents/ortak/test-seviyeleri.md`](../../ortak/test-seviyeleri.md).

---

## Adım 0 — Hangi moddasın?

Üç mod vardır. Yanlış mod, gereksiz ölçüm demektir.

| Kullanıcının sorusu | Mod | Nereye git |
|---|---|---|
| "preview çıkabilir miyiz?" · "1.0'a hazır mı?" · "yayınlayalım mı?" | **Yayın kararı** | Adım 1 → 8, tam sıra |
| "bu API doğru mu?" · "burayı kırıcı mı yapar?" · "bu seam nasıl olmalı?" | **Nokta danışmanlığı** | Yalnız ilgili merceği koş (Adım 4), sonra Adım 7 |
| "yayınlanan pakette kusur var" · "tüketici restore edemiyor" | **Yayın sonrası** | Aşağıdaki "Yayın sonrası" bölümü |

Yayın kararı modunda **önce hedefi sabitle**: `preview` · `rc` · `stable` ·
`patch/minor/major`. Hedef sürüm türü, aynı bulgunun seviyesini değiştirir —
preview'da 🟡 olan bir yüzey hatası stable'da 🔴'dır.

---

## Adım 1 — Kanıt sırası: görüş öncesi ölçüm

**Hiçbir önemli kararı yalnız kaynak kodu okuyarak verme.** Bu depoda kaynak
okumasının yanlış cevap verdiği ölçülmüş vakalar vardır: görünmez ayırıcı
karakteri (`U+001F`) hem güvenlik denetçisi hem kapanış oturumu "yok" sandı ve
**yanlış bir 🔴 bulgu** üretildi; gerçeği çalışma anı probu verdi (K-525).

Kanıt merdiveni — yukarıdan aşağı **güç artar**, maliyet de artar:

| Seviye | Kanıt | Neyi kanıtlar |
|---|---|---|
| 1 | Kaynak okuması, `grep` | Bir şeyin **var olduğunu** |
| 2 | XML doküman, `docs-site/`, README | Ne **vaat edildiğini** |
| 3 | Birim testi | İzole davranışı |
| 4 | Fonksiyonel test, contract testi | Sınır davranışını |
| 5 | `.nupkg` içeriği (`unzip -l`, `.nuspec`) | **Paketlenen** yüzeyi |
| 6 | İzole `NUGET_PACKAGES` + exact `PackageReference` ile dış tüketici | Tüketicinin gerçekten göreceğini |
| 7 | O tüketicinin **gerçek run**'ı, gerekiyorsa Native AOT publish | Çalışma anı sözleşmesini |

**Kural:** bir davranış hakkında "çalışıyor" demek için en az **5. seviye**,
bir extension seam sözleşmesi için **6. seviye**, bir güvenlik veya AOT iddiası
için **7. seviye** kanıt iste.

İzole prob ile entegre boru hattını **karıştırma**. Bu depoda ayrı bir konsol
probunda çalışan bir tool çağrısı, MAF boru hattında çalışmıyordu — MAF
`EmptyServiceProvider` geçiriyordu (K-218).

Komutlar: [`resources/kanit-komutlari.md`](resources/kanit-komutlari.md).

---

## Adım 2 — Kaynak ağacı yeşil olması artifact'ın doğru olduğunu göstermez

Yayın değerlendirmesi **paketten** yapılır, ağaçtan değil:

1. temiz `pack` (exact sürüm zorlanmış)
2. yerel feed
3. **boş/izole** `NUGET_PACKAGES`
4. depo **dışında** tüketici çözümü
5. yalnız `PackageReference` — `ProjectReference` yok, source checkout yok
6. exact sürüm — floating (`*-*`) yok
7. build → test → **gerçek run**

Floating sürüm bu depoda ölçülmüş bir tuzaktır: artımlı `dotnet pack`
değişmemiş projeyi yeniden üretmez, "en son yazılan" dosya **bayat** kalır ve
bir önceki koşumun paketi seçilir (Faz 97, `_clean_stale_packages`). Aynı sınıf
global NuGet cache'inde de olur — izole `NUGET_PACKAGES` kullanmayan bir
tüketici testi hiçbir şey kanıtlamaz.

Bu depoda hazır kapılar vardır; **önce onları tercih et**:

```bash
python3 scripts/kapi.py yayin --kuru --surum <hedef sürüm>
```

Tek komut yediyi birden yapar: sürümü zorlar · paketler · bayat `.nupkg`'leri
siler · kimlik kümesini ve metaveriyi doğrular · `npm publish --dry-run` koşar ·
**beş extension sample'ını exact sürüm ve izole `NUGET_PACKAGES` ile** çalıştırır
(`scripts/release_extension_samples.py`, `kapi.py` içinden çağrılır — tek başına
çalıştırılabilir bir script değildir) · Native AOT smoke publish eder ve
çalıştırır. **Ağa hiçbir şey yazmaz, `git tag` atmaz.**

> 🚨 **Kapının çıktısına körü körüne güvenme.** Komutun **neyi doğruladığını**
> oku. Faz 97'nin denetimi `kapi.py yayin`'in yalnız **eksik** paketi
> yakaladığını, **fazla** paketi ve TFM başına XML dokümanı doğrulamadığını
> buldu — ikisi de o komutun kendi metninin verdiği sözdü. Bir kapı yeşilse
> sorulacak soru "ne koştu?" değil, **"ne koşmadı?"**dır.

---

## Adım 3 — Extension seam matrisi

Üçüncü tarafın **genişleteceği** her yüzeyi çıkar ve hepsini **aynı** matrise
koy. Bu depodaki seam'ler bugün: storage · model provider · run judge · agent
source · custom tool · middleware/decorator · policy/guard · serialization ·
transport (MCP · A2A · OpenAI-uyumlu uçlar).

Matrisin 22 sütunu ve her sütunun sorusu:
[`resources/seam-matrisi.md`](resources/seam-matrisi.md).

Matrisi doldurduktan sonra **satırları değil sütunları** oku. Aynı kavram iki
seam'de farklı davranıyorsa üç ihtimal vardır ve **hangisi olduğunu söylemek
zorundasın**:

| Ayrım | Ne yapılır |
|---|---|
| Bilinçli tasarım | Gerekçesi dokümante edilmiş mi? Değilse 🟢 |
| Eksik dokümantasyon | 🟢 veya 🟡 — davranış doğru, keşfedilebilir değil |
| **Contract inconsistency** | 🔴 veya 🟡 — tüketici bir seam'den öğrendiğini diğerine taşır ve yanılır |

Üçüncüsü en pahalısıdır: tüketici tutarlılık **varsayar**, dokümantasyon
okumaz.

---

## Adım 4 — Yedi mercek

Her mercek bağımsız koşar. Nokta danışmanlığı modunda yalnız ilgili olanı koş.

### 4.1 Public API freeze
`Shipped` baseline'a girmemiş bir API'yi **sırf yazılmış olduğu için koruma.**
Bu depoda `PublicAPI.Shipped.txt` preview hattı boyunca **boştur** (K-603) —
yani bugün yüzeyi küçültmek ucuzdur, GA'dan sonra kırıcıdır. YAGNI uygula.

Ara: kullanılmayan public tip · yalnız implementation detayı olan public
helper · gelecekte büyüyecek public `enum` · yanlış arayüze konmuş capability ·
`null` parametresiyle iki anlam taşıyan API · convenience için açılmış
implementation wrapper · concrete Core tipine gereksiz bağımlılık · registration
API'si olmayan extension point · anlamı belirsiz duplicate registration ·
uygulanmayan public options alanı · XML sözü ile çelişen runtime.

Emsal: Faz 96 yaprak olup başka public imzada geçmeyen **96 tipi** `internal`
yaptı (K-601). Faz 103 kullanılmayan `AgentPrismJudgeException`'ı kaldırdı.

### 4.2 SemVer ve compatibility
Her değişikliği sınıflandır: `patch-safe` · `minor/additive` · `source-breaking`
· `binary-breaking` · `behavioral-breaking` · `wire/protocol-breaking` ·
`serialization-breaking` · `database/migration-breaking` · `AOT/trimming-breaking`.

Preview'da bile **tüketici maliyetini** yaz. Stable sonrası yapılamayacakları
ayrıca işaretle. Migration'lar immutable'dır — uygulanmış bir migration
düzenlenmez, yenisi eklenir.

### 4.3 Güvenlik sınırı — uçtan uca izle
"Endpoint'te maskeleniyor" bir kanıt **değildir**. Ham veriyi kaynağından
başlayıp **her** ara katmandan geçir: runtime boru hattı · fallback · retry ·
circuit breaker · `ILogger` · `store` · HTTP · SSE · MCP · OpenAI-uyumlu uçlar
· kalıcı `run`/`tool`/`error` kayıtları.

Ara: `exception` mesajı sızıntısı · `secret`/API anahtarı · connection string ·
kiracı yalıtımı · BYOK credential semantiği · global credential'a sessiz düşme ·
egress policy · content guard · tool sonucu normalizasyonu · yetkilendirme ·
onay · timeout · çıktı sınırı.

Normalizasyon **public boundary'ye yakın** ama classifier/fallback bilgisini
kaybetmeyecek yerde olmalıdır. `secret` veritabanına da yazılmaz (K-059).

### 4.4 Capability tasarımı
Bir implementasyon her capability'yi desteklemiyorsa seçenekleri **karşılaştır**:
nullable parametre · boolean property · marker interface · opt-in interface ·
capability object · registration metadata.

**Optional bir capability sessiz fallback ile geçilmez.** Kiracı BYOK
credential'ı verilmişken provider bunu desteklemiyorsa setup/global
credential'a düşmek bir güvenlik kusurudur; `fail-closed` + stable bir hata
kodu doğru davranıştır (Faz 103, `provider_credential_unsupported`).

Capability sözleşmesi dört şeyi birden olmalıdır: **discoverable · testable ·
documented · runtime-enforced.**

### 4.5 Timeout ≠ cancellation
Her async sınırda sor: caller cancellation nedir? · internal timeout var mı? ·
cooperative mi, gerçek **wait cutoff** mı? · gövde token'ı yok sayarsa ne olur?
· timeout sonrası task yaşamaya devam ediyor mu? · geç tamamlanma/fault
**observe** ediliyor mu? · `OperationCanceledException`'ın kaynağı (caller mı
host mu timeout mu) ayırt ediliyor mu? · retry edilebilir mi?

Bir options alanının adı `Timeout` ise **sonsuz beklemeyi gerçekten kestiğini
ölç.** Linked `CancellationToken` üretmek timeout garantisi değildir.

### 4.6 Exception taxonomy ve public hata sözleşmesi
Her seam için: hangi exception tipi public olabilir? · yabancı exception ne
olur? · ham `Exception.Message` dışarı çıkar mı? · stable `ErrorType`/kod var
mı? · `InnerException` korunuyor mu? · log tam detayı taşıyor mu? ·
HTTP/SSE/MCP kullanıcıya **generic güvenli** mesaj mı veriyor? · framework'ün
kendi bilinen exception alt tipleri korunuyor mu? · kullanılmayan public
exception tipi var mı?

Public hata sözleşmesi ile diagnostics birbirinden ayrıdır. İkisini aynı
string'ten beslemek sızıntı üretir.

### 4.7 Serialization / canonical representation
Tool, plugin veya sonuç taşıyan sistemlerde her tipin **çalışma anı temsilini
ölç**: `string` · `JsonElement` · primitive · `enum` · collection · record/class
· `null` · binary/`AIContent`/protokole özgü değerler.

Content guard, truncation, persistence ve wire çıktısı **aynı canonical
representation**'dan beslenmelidir; farklı beslenirlerse guard'ın gördüğü ile
tüketicinin gördüğü ayrışır.

AOT'u bozan reflection serializer **varsayılan çözüm değildir**: kaynak üretimli
`JsonSerializerContext`/`JsonTypeInfo` yollarını değerlendir. Normalize
edilemeyen hassas veri için `fail-open` değil **`fail-closed`** seç.

---

## Adım 5 — Kanıtın kalitesi: test tiyatrosu ve sample

Bir contract sınıfının **var olması** kanıt değildir. Testin iddia ettiği
davranışı gerçekten kanıtladığını sorgula. Bilinen sahte testler:

- `x.ShouldBe(x)` — kendini doğrulayan iddia
- `Task.WhenAll` var ama **gerçek overlap yok** (barrier/gecikme yok)
- cancellation testi yalnız **önceden iptal edilmiş** token kullanıyor
- timeout testi gövdede token'ı gerçekten **yok saymıyor**
- mutation testi hiçbir şeyi mutate etmiyor
- disposal testi gerçek dispose **sahipliğini** gözlemlemiyor
- kiracı testi iki kiracıyı gerçekten **çakıştırmıyor**
- contract'ın hiç consumer'ı yok — `[Fact]` var ama concrete implementation türetilmiyor
- `Skip` ile sessizce hiç koşmayan optional contract
- yalnız fake davranışı ölçüp gerçek boru hattı sınırını atlayan senaryo

Sağlam bir contract ailesi şunları taşır: reusable public contract paketi ·
ad alanı/aile yalıtımı · opt-in capability contract'ları · en az bir built-in
implementation + bir **dış** sample consumer · `red→green` doğrulama kaydı ·
deterministik concurrency barrier · **in-flight** cancellation · dönüşten sonra
mutation · exact çıktı iddiası.

**Sample bir kalite kapısıdır, örnek kod değildir.** İyi sample: yalnız
`PackageReference` (exact sürüm) · `ProjectReference` yok · public registration
API'sini kullanır · gerçek tüketicinin yazacağı kodu gösterir · contract
suite'ini koşar · mümkünse uçtan uca gerçek `run` yapar · DI/scoped bağımlılık
kullanımını gösterir · cancellation ve kiracı davranışını doğru kullanır ·
rehber sayfasıyla senkrondur. Derlenen 20 satırlık implementation sample
değildir — sample bir **consumer acceptance test**'idir.

Bu depoda kapı hazırdır: `scripts/release_extension_samples.py` wildcard
`VersionOverride`'ı ve `src/AgentPrism` `ProjectReference`'ını **reddeder**.

---

## Adım 6 — Doküman = sözleşme

XML dokümanı, paket `README`'si, `docs-site/` ve sample **aynı** davranışı
anlatmalıdır. Drift ara:

- doküman runtime'dan **daha güçlü** garanti veriyor mu? (en tehlikelisi)
- runtime dokümandan daha güçlü mü? (keşfedilemeyen yetenek)
- paket `README`'si eski paket/aile sayısını taşıyor mu?
- `reference/compatibility.md` ve `reference/versioning.md` güncel mi?
- paket `Description`'ı bayat mı?
- üretilen API dokümanı kaynak XML ile uyuşuyor mu?
- doküman "timeout" derken runtime yalnız cancellation bütçesi mi uyguluyor?
- doküman "tüm sonuç tipleri" derken implementation yalnız `string`/`JsonElement` mi işliyor?
- doküman extension point açıkmış gibi anlatırken public registration API yok mu?

**Kaynak kod doğru diye yanlış doküman önemsiz değildir.** Yanlış doküman
release blocker olabilir — tüketicinin gördüğü tek sözleşme odur. Kapatma işi
`tuketici-dokuman-senkronu`'nundur; standart onun
[`resources/kalite-sozlesmesi.md`](../tuketici-dokuman-senkronu/resources/kalite-sozlesmesi.md)
dosyasındadır.

---

## Adım 7 — Seviyelendir ve "gerçekten blocker mı?" diye sor

Her önemli bulgu yedi soruyu geçer. Geçemeyen bulgu bir seviye **düşer**:

1. Gerçek bir tüketici bunu **bugün** yaşayabilir mi?
2. Paket artifact'i üzerinden **yeniden üretilebilir** mi?
3. Güvenlik veya veri bütünlüğü etkisi var mı?
4. Preview'dan sonra düzeltmek **kırıcı** olur mu?
5. Bir contract testiyle **kalıcı olarak** kilitlenebilir mi?
6. Yalnız doküman kusuru mu, yoksa runtime sözleşmesi mi?
7. Teorik bir ihtimal mi, yoksa **ölçüldü** mü?

Teorik ile ölçülmüşü asla aynı tabloya koyma.

| Seviye | Anlamı |
|---|---|
| 🔴 **Preview blocker** | Yanlış/çelişkili public API · güvenlik sınırı açığı · sonradan kırılacak wire contract · dokümanla ciddi çelişen runtime · yanlış credential/kiracı semantiği · veri bozulma riski · yanlış extension contract |
| 🟡 **1.0 blocker** | Executable contract eksikliği · sample eksikliği · ergonomi · önemli test boşluğu · uzun vadeli tutarsızlık · olgunlaşmamış public extension yüzeyi |
| 🟢 **Doküman / cila** | Davranış doğru, keşfedilebilirlik eksik · README drift · rehber eksikliği · metaveri ifadesi |
| ⚪ **Sonraya** | Gerçek ihtiyacı kanıtlanmamış optimizasyon · speculative API · benchmark'sız mikro-optimizasyon · yeni framework/test adaptörü |

**Seviyeyi mekanik verme.** Her satır kendi gerekçesini taşır. Doküman
kusurunu runtime blocker gibi, runtime blocker'ı doküman kusuru gibi
sınıflandırmak bu tablonun tek gerçek başarısızlık biçimidir.

---

## Adım 8 — Karar ver

Yayın kararı modunda skill **karar vermek zorundadır**. Üç sonuç vardır:

| | Anlamı |
|---|---|
| ✅ **Yayınlanabilir** | 🔴 yok; kalan işler sonraki sürüme sığar |
| ⚠️ **Teknik olarak yayınlanabilir, önerilmez** | 🔴 yok ama 🟡'ler birlikte tüketici deneyimini bozar; gerekçesini yaz |
| ❌ **Yayınlanmamalı** | En az bir 🔴 açık |

Karar **hiçbir zaman** yalnız testlerin yeşil olmasına dayanmaz. Kararla
birlikte **en küçük güvenli yayın kapsamını** öner: hangi paketler, hangi sürüm
türü, hangi işler bu sürümden **çıkarılabilir**.

---

## Danışmanlık tarzı

Bu skill rapor yazmakla yetinmez; **karar verdirir.** Birden fazla makul
seçenek varsa:

1. Problemi bir cümlede tanımla
2. Seçenekleri **A / B / C** olarak ver
3. Her seçenek için beş maliyeti yaz: **API maliyeti · compatibility etkisi ·
   güvenlik etkisi · implementation karmaşıklığı · yayın sonrası geri dönüş
   maliyeti**
4. **Net bir öneri yap** ve işaretle: `Öneri: B.`

Kaynak kod veya ölçüm ile çözülebilen soruyu **kendin çöz**. Kullanıcıya yalnız
gerçek ürün/semantik kararlarını sor, karar grupları hâlinde ve net
seçeneklerle. On beş açık soru bir danışmanlık değildir.

**Faz planı yazma.** "Ne yapalım?" sorusuna işi ve kapsamı söyle. Kullanıcı
açıkça "faz planı yaz" ya da "faz planı için prompt ver" derse `faz-planlama`
için net girdi üret — ve o girdiden **önce** çözülmesi gereken ürün kararlarını
sor.

---

## Yayın sonrası mod

Yayın yapıldıktan sonra bu skill şu konularda danışmanlık verir: paket sağlığı ·
dependency drift · tüketici restore sorunları · SemVer etkisi · API
compatibility · deprecation ve migration stratejisi · patch hotfix ölçütü ·
CVE/güvenlik yanıtı · transitive dependency olayı · bozuk paket metaverisi ·
sembol/Source Link doğrulaması · paket deprecation/yank · release note ·
sonraki preview/RC/stable planı · downstream regresyonlar · issue ve destek
geri bildiriminden risk çıkarma.

Yayın sonrası bir kusur bulunduğunda **yedi adım**:

1. Sorun **source**'ta mı **artifact**'te mi? (İkisi farklı olabilir — ölç)
2. Kaç sürüm etkilendi?
3. Tüketicinin uygulayabileceği bir workaround var mı?
4. `patch` mi `minor` mü `major` mü gerekiyor?
5. Public contract değişiyorsa SemVer etkisini açıkla
6. Regresyon testini **iste** — hangi seviyede olduğunu söyle
7. Doküman ve release note etkisini yaz

Düzeltmenin kendisi `kusur-giderme`'nin işidir; o skill tek vakayı değil
**sınıfı** kapatır ve bu ayrım burada da geçerlidir.

---

## Çıktı biçimi

Büyük bir incelemede bu iskeleti kullan. Küçük bir soruda yalnız ilgili
başlıkları kullan — boş başlık yazma.

```markdown
## Sonuç
<Tek paragraf. Net karar: ✅ / ⚠️ / ❌ ve tek cümlelik gerekçe.>

## Ölçülen kanıtlar
<Gerçek komut, dosya:satır, test adı, paket, tüketici koşumu. Ölçülmeyen iddia yazma.>

## Blocker'lar
| # | Bulgu | Seviye | Neden | Preview sonrası maliyet |

## Contract matrisi
<Yalnız gerekliyse. Seam'ler satır, sözleşme boyutları sütun.>

## Public API kararları
| Yüzey | Karar | Gerekçe |
| ... | tut / değiştir / kaldır / internal yap / capability'ye ayır | ... |

## Test boşlukları
<Gerçek davranışı ölçmeyen veya hiç consumer'ı olmayan contract'lar.>

## Paket/yayın kanıtı
<pack · izole restore · exact sürüm · dış tüketici · AOT.>

## Doküman drift
<XML / README / docs-site / sample çelişkileri.>

## Önerilen sonraki iş
<Faz planı DEĞİL. Yalnız iş ve kapsam. Gerekiyorsa açık ürün soruları.>
```

---

## Yasaklar

- **"Tüm testler yeşil → yayına hazır."** Yeşil test bir girdidir, karar değil.
- Kaynak ağacı testini NuGet tüketici testi sanmak.
- `ProjectReference` ile sample doğrulamak.
- Global NuGet cache'i fark etmeden floating preview sürümü test etmek.
- XML dokümanını runtime gerçeğinden bağımsız yazmak veya doğru saymak.
- Public API'yi **simetri olsun diye** büyütmek; her problemi yeni bir arayüzle çözmek.
- YAGNI'yi ihlal eden speculative facade.
- `CancellationToken` var diye timeout var saymak.
- `IReadOnlyList` gördü diye altındaki verinin immutable olduğunu varsaymak.
- `Task.WhenAll` gördü diye concurrency kanıtlandı saymak.
- Bilinmeyen üçüncü taraf exception mesajını public HTTP/SSE'ye taşımak.
- `AotCompatible` property'si var diye reflection kullanımını görmezden gelmek.
- Doküman kusurunu runtime blocker; runtime blocker'ı doküman kusuru saymak.
- Internal implementation detayını gereksizce public sözleşme yapmak.
- Mevcut davranışı, preview'dan **önce**, "backward compatibility" gerekçesiyle yanlış biçimde dondurmak.
- Kod yazmak. Bu skill ölçer, yargılar ve yönlendirir; düzeltme `kusur-giderme`'nindir.

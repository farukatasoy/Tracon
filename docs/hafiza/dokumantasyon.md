# Dokumantasyon Tuzaklari

> **Kardeş dosya:** geliştirme defterinin (`KARARLAR.md`, `docs/arsiv/fazlar/`)
> BAKIMI ayrı bir dosyadadır: [`defter-bakimi.md`](defter-bakimi.md). Bu dosya
> **sevk edilen** ve kullanıcıya dönük dokümanın tuzaklarını taşır.

> Sevk edilen dokumantasyonun DOGRULUGU (paketlenen XML, paket README'leri,
> paketlenen OpenAPI belgesi, metin kapisi yazma tuzaklari).
>
> Site YAYIN hatti ve Starlight temasi AYRI dosyadadir:
> [`site-yayin-ve-tema.md`](site-yayin-ve-tema.md). Site UREtim betikleri
> (`build-agent-map.mjs`, `docfx`) ve onlarin kapi davranisi da AYRI dosyadadir:
> [`site-uretim-kapilari.md`](site-uretim-kapilari.md) (Faz 122'de ayrildi).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Sinir: **nasil paketlenir** sorusu
> [`paketleme-ve-dagitim.md`](paketleme-ve-dagitim.md)'dedir; **pakete giren
> metnin dogrulugu** buradadir. İçerik kapısı yazımı, sunum kapıları, önek
> karşılaştırma ayırıcısı ve mermaid tema tuzakları (site kapı BETİKLERİNİN
> kendi bakımı) [`site-uretim-kapilari.md`](site-uretim-kapilari.md)'ye
> Faz 156'da taşındı.

## Sevk edilen metnin kurali (Faz 75, K-514)

- **Pakete giren bir metin yalniz tuketicinin elindeki seylere gonderme yapar.**
  Yasak: faz numarasi, `K-NNN`, `F-NN`, `K1`–`K4`, `MT-*`/`HATA-*`,
  `section N.N`, `open question N`, `docs/NN-*.md`. Serbest: paketin kendi
  tipleri, yapilandirma anahtarlari, HTTP yollari, MSBuild ozellikleri ve
  `tracon.dev`.
- **Kural sesi de kapsar**: 🚨/⚠️, `Rationale:` acilisi, `Measured (2026-…)`.
  Site ureteci bunlari zaten siliyordu — yani proje bu sesi tuketiciye uygun
  bulmuyordu, yalniz paket tarafinda zorlamiyordu.
- **Kapsam disi ve bilerek**: `//` uygulama yorumlari. Bakimcinin okudugu bir
  yorumun "K-320 bu konumu olctu" demesi DOGRU davranistir; ayni cumle `///`
  icine girerse `.xml` olarak tuketiciye gider.
- Kapi: `ShippedDocumentationSelfContainmentTests` — `SourceLanguageTests`
  mekanigi, taban cizgisi BOS.

## 🚨 Satir bazli tarama XML yorumunda YETMEZ (K-515)

XML yorumu kaynak genisliginde sarilir, bu yuzden `(phase 65)` rutin olarak iki
satira bolunur: bir satir `(phase` ile biter, sonraki `65)` ile baslar. Satir
satir tarayan bir regex **iki yariyi da goremez**. Olculdu (Faz 75): uc referans
bu delikten gecti ve yalnizca site ureteci blogu birlestirdiginde yakalandi.
Kural: `///` blogunu birlestir, oyle esles, sonra eslesmeyi kapsadigi satirlara
dagit.

## 🚨 Metin onarim zinciri kusuru GIZLER, cozmez (K-516)

`build-api-reference.mjs` ve `build-http-api.mjs` toplam ~110 satirlik bir
`replace` zinciri tasiyordu. Zincir sessizce bozuyordu:
`"Request to promote a run to a case,."`, `"Example: `[...]`. for the format."`,
`POST.../trigger`. Kaynak temizlenince zincir **olculerek** kaldirildi: 689
uretilen sayfanin yalniz biri degisti. Yeni desen: **onarma, hata ver.**

Ayni sinifin ikinci tuzagi: `\s+([,.;:])` kurali bir ELIPSI bozar
(`POST .../trigger` → `POST.../trigger`). Noktalama sikistiran her kural
`(?!\.)` ile korunmalidir.

## 🚨 `<see cref>` paketlenen OpenAPI'de TAM IMZA olur (K-517)

ASP.NET Core'un XML dokuman ureteci `<see cref="X"/>`'i cumlenin ortasina
`string? ClientToolResult.ErrorMessage` olarak basar. Olculdu: 26 yer. Site
kopyasi bunu bir suzgecle siliyordu, paketlenen kopya silmiyordu. OpenAPI'nin
seri hale getirdigi sozlesme tiplerinde `<c>UyeAdi</c>` yaz; ic tiplerde
`<see cref>` IDE gezinmesi icin kalir.

**Tekrar (Faz 132, bagimsiz denetimde bulundu):** Kural yalniz `<summary>`'yi
kapsar — OpenAPI `description` alaninin kaynagi odur. Ayni `<see cref>`
`<remarks>` icinde SORUNSUZDUR (docfx site sayfasinda duzgun link uretir,
OpenAPI'ye hic girmez); kurali `<remarks>`'a da uygulamak fazla istir. Yeni
bir kayit tipine alan eklerken `<summary>`'sini "komsu uyeye referans veriyor
mu" diye bir kez kontrol et.

## Ekran goruntusu ureteci

- `TRACON_UI_SCREENSHOTS=1 dotnet test tests/Tracon.Ui.E2ETests -c Release`
  ile uretilir; liste `DocumentationScreenshotTests.Screens` icindedir ve her
  girdi bir **landmark** tasir — render olmayan ekran testi dusurur, spinner
  fotografi uretmez.
- **🚨 Bos ekran ogretmez.** `SeedCatalogAsync` bir skill, bir schedule,
  tetiklenmis bir job, bir trigger ve bir MCP sunucusu kurar; iki `run` tek bir
  `sessionId` paylasir. Tohumlama iki sozlesmeyi de ortaya cikardi:
  `JobScheduleSaveRequest.Payload` atanmazsa uc **500** doner (`JsonElement`
  `Undefined` tuzagi) ve tetikleyici imzalama anahtari
  `Tracon:TriggerSecrets:` onekini ZORUNLU tutar.

## Sevk edilen XML dokumani kendi kapisina takilir (Faz 99)

`ShippedDocumentationSelfContainmentTests` yalniz `docs/` yollarini ve faz/karar
numaralarini degil, **sesi** de denetler: alarm emojisi (🚨), `Rationale:`
acilisi ve `Measured (…)` bloklari sevk edilen `///` XML'inde YASAKTIR (site
ureticileri bunlari zaten kirpiyor; paket kirpilmamis metni sevk ettigi icin
kural kaynakta zorlanir). Faz 99'da `IModelProvider`'in yeni `<remarks>`'ina
konan tek bir 🚨 kapiyi kirmiziya dondurdu. Implementation yorumu (`//`) kapsam
DISINDADIR — maintainer icin "K-320 bu konumu olctu" yazmak serbesttir, ayni
cumle `<summary>` icinde degildir.

## 🚨 Sayısal sıralama knob'unun YÖNÜ dokümanda ters yazılır

`IAgentDecorator.Order`'ın `<summary>`'si yıllarca gerçek davranışın tersini
söyledi ("a lower value wraps *inside*", oysa düşük değer **dışta** sarar).
Üç şey bunu görünmez yaptı:

- **Davranış testi bir cümleyi göremez.** `Priority`'nin davranışı
  `CompositeAgentCatalogTests` ile zaten test ediliyordu; public cümlesi yine
  de belirsizdi. Bu sınıfın kapısı kaynak **metnini** okumak zorundadır.
- **Arayüz kendi kendisiyle çelişiyordu.** Yanlış kuraldan hemen sonraki örnek
  cümle ("Run recording uses 0, which makes it the outermost") doğruydu; el
  yazısı `docs-site/concepts/runs.md` diyagramı da doğruydu. Tek bir cümle
  yanlıştı ve çevresindeki her şey doğru olduğu için kimse şüphelenmedi.
- **"Higher priority" İKİ anlama gelir.** `IAgentCatalog.ListAsync` "the source
  with the higher priority wins" diyordu; `AgentSourcePriority.Database = 100`
  yanında bu **yüksek sayı** gibi okunur, oysa düşük sayı kazanır. Sıralama
  dokümanında "higher/lower priority" yazma — **"lower value"** yaz.

Kapı: `OrderingContractDocumentationTests` (`tests/Tracon.Core.UnitTests/
Architecture/`). Yeni bir sayısal sıralama/öncelik yüzeyi eklersen `Contracts()`
tablosuna satır ekle (K-642).

**Kapıyı yazarken tuzak:** ilk sürümü test tiyatrosuydu. `"lower"` ve
`"value wins"` ayrı ayrı arandığı için, kasıtlı bozma (`lower` → `higher`)
YEŞİL geçti — aynı özetin ilerisindeki ikinci bir "lower" kontrolü kurtarıyordu.
Metin kapısı **bağlı tek ifade** aramalıdır, parça parça değil; ve her vakada
ayrı ayrı kırmızı verdiğini ölç.

## 🚨 Bir arayüzün "kendi dokümanı" başlığıyla sınırlı değildir (Faz 121, K-643)

`SeamContractDocumentationTests` ilk sürümü yalnız `public interface I...`
satırının HEMEN ÜSTÜNDEKİ `<summary>`/`<remarks>` bloğunu okuyordu. Bu,
`IJobHandler`'ı (K-641'in referans örneği) yanlışlıkla "delivery guarantee
belgesiz" işaretledi — çünkü "at-least-once" cümlesi arayüz başlığında değil,
`ExecuteAsync`'in KENDİ `<remarks>`'inde duruyor. Bir metin kapısı "arayüzün
kendi XML dokümanı" derken, arayüzün İÇİNDEKİ her üyenin doküman bloğunu da
kapsamalı — yalnız başlığı değil. Düzeltme: tarama artık başlık doc'u +
gövdedeki her `///` satırını birleştirip arıyor (`InterfaceDocSurface`).
Regresyon testi: `A_dimension_answered_on_a_MEMBERs_doc_counts_as_answered`.

## 🚨 Site içeriği repo private iken kendi GitHub URL'ine bağlanamaz (Faz 123)

`check-content.mjs` `repositoryIsPublic=false` (`site.config.mjs`) iken
`repositoryUrl` metnini (link veya düz metin, ikisi de) her sayfada reddeder
— okuyucuya bugün `404` dönerdi. Elle yazılan bir sayfa `CHANGELOG.md`'ye
"bağlantı ekle" gibi bir plan kararını uygularken bunu yakaladı. Repo private
kaldığı sürece dosya adını düz metinle (`` `CHANGELOG.md` ``) anlat, GitHub
URL'i yazma; repo açıldığında `repositoryIsPublic=true` olur ve gerçek
bağlantı eklenebilir.

## 🚨 Uretilen referans bir KESIF yuzeyi degildir (2026-09-03)

`quota.threshold` sevk edilmisti ve calisiyordu; anlati onu hic anlatmiyordu.
Tuketici ozelligi bulamadi ve **var olani yeniden onerdi**. Ciplak varsayilan
satiri davranisi anlatmaz; uretilen referansa adini **zaten bilen** bakar.
Kapi: `sevk_edilen_olay_anlatisi()`.

Ayni tur: **oznesiz cumle.** "A channel that reaches capacity drops the event"
tuketicinin KENDI kanalini tarif ediyordu, Tracon'inki gibi okundu —
Tracon'in kanali yoktur. Sorumlulugu anlatan cumle oznesini yazsin.

## 🚨 Bir çalışma anı şartını ÜYE ÜYE anlatmak, şartı üyeye özgü gösterir (2026-09-07, F-212)

`RunEventWriter`, `TraconRunRecordingOptions.RecordToolPayloads` kapalıyken
her olayın `Payload` alanını `null` bırakır. `RunEventType`'ın **32 üyesinin
~18'i** payload iddiası taşıyordu ve bu şartı yalnız **3'ü** anıyordu. Tüketici
kuralı üyeden üyeye öğreniyor, anmayan üyede kuralın geçerli olmadığını
sanıyordu.

Ters yönü daha tehlikeliydi: `WorkflowRequest`'in payload'ı yazıcıda **bilerek
her zaman** yazılır (bekleyen insan isteği yalnız oradan okunur) ama üye
dokümanı bu muafiyeti hiç söylemiyordu — genel kuralı uygulayan okuyucu yanlış
sonuca varırdı.

**Kural: bir çalışma anı şartı bir kez, TİP düzeyinde yazılır; üye yalnız KENDİ
muafiyetini anlatmak için o şarttan söz edebilir.** Tekrar, kuralı üyeye özgü
gösterir.

**Kapı:** `RunEventPayloadSuppressionTests` (üç test). Değerli olan ikincisidir:
yazıcının koşulundaki `RunEventType.X` kümesi ile tip düzeyi `<remarks>`'ta
adı geçen küme **eşit** olmalıdır — dördüncü bir muafiyet eklemek, doküman onu
adlandırana kadar kapıyı kırar. Yazıcıya `RunCompleted` muafiyeti eklenerek
kırmızı olduğu ölçüldü. Üçüncü test tekrarı yasaklar; yazıldığı gün üç ihlal
buldu (`ToolOutputTruncated`, `StructuredResponseRejected`, `Custom`).

Bu, `RunEventPayloadContractTests`'in kendi belgelenmiş sınırının
(*"İngilizceyi JSON'a karşı makineyle denetleyemeyiz"*) **denetlenebilir**
yarısıdır: kümeyi karşılaştırmak İngilizceyi okumak değildir.
- **🚨 Kapı sapmayı YAKALAR; sapmayı ÜRETEN protokol düzeltilmezse sınıf kapanmaz** (2026-09-08): `00-INDEKS.md` 1488 case yazıyordu, gerçek 1597'ydi — 36 ailenin **17'si** bayat. Sebep tek bir cümleydi: `faz-tamamlama` Adım 3 indeksi yalnız "alan dosyası yoksa" güncelletiyordu, yani mevcut bir aileye case eklemek sayacı hiç güncellemiyordu. Faz 157 bir aile dosyasına 170 satır ekledi ve indekse dokunmadı. **İki düzeltme birlikte gerekir:** kapı (`manuel_test_sayim_kaymasi`) unutulanı yakalar, protokol adımı unutulmayı önler. Yalnız kapı eklemek her fazda bir kırmızı üretir ve insanları onu susturmaya eğitir.
- **🚨 Bir kapı İLK koşumunda kırmızı yanarsa, önce KAPIYI doğrula** (2026-09-08, `bagimlilik_surum_damgasi` yazılırken): kapı `IVectorSearchStore.cs:16`'daki `10.8.0` damgasını `Microsoft.Extensions.AI` pini `10.9.0` ile karşılaştırıp sapma bildirdi. Sapma **yoktu** — damga `Microsoft.Extensions.VectorData` hakkındaydı ve repo o paketi hiç almıyor; hatalı olan kapının eşleme kaydıydı. Kod "düzeltilseydi" doğru bir cümle yanlışla değiştirilecekti. Kural: yeni kapının ilk bulgusu bir kanıttır, bir emir değil. Pinlenmeyen paket için kayıt `None` taşır — bu bir atlama değil, yazılı karardır.
- **🚨 Üretilen dosyayı `Edit`'ten korumak ÜRETECİ kilitlemez — ama `ask` bir kilit de değildir** (Faz 167, `claude 2.1.269`): `Edit(<yol>)` deny'ı `Edit` + `Write` **ve** `rm <yol>` Bash komutunu kapsar; `dokuman-bakim.py` **Python ile** yazdığı için üretim modu kuralı hiç tetiklemez (`MT-GDK-029`). Ama `ask` (`docs/arsiv/fazlar/*.md`) oturumun izin moduna tabidir: auto mode'da sınıflandırıcı **sessizce onaylar**, aynı oturumda `deny` sertçe durur. Her iddia kontrol koşumuyla ayırt edildi — kural yokken aynı `rm` dosyayı sildi. Ölçümler ve sınırlar: K-761 · K-763. Doküman kuralını araca taşırken sorulacak soru "kural kondu mu" değil, **"hangi yazma yolunu gerçekten kapatıyor"**.


## 🚨 Makine okuyucusu için HTML, kod bloğunu SESSİZCE bozar (2026-09-14, GEO denetimi)

Expressive Code her kod SATIRINI kendi `<div class="ec-line">`'ına yazar ve aralarına
newline **koymaz**. Etiketleri düşüren bir metin dönüşümü şunu üretir:

```
var builder = WebApplication.CreateBuilder(args);builder.AddTracon().UseUI();
```

Derlenmeyen C#. Tablolarda aynı kusur sütun sınırında olur: satır ayrımı korunur,
sütun ayrımı kaybolur. Bunu hiçbir kapı göremezdi çünkü **render doğru**; bozulan
yalnız düz metne indirgeme.

Çözüm sayfa sayfa değil merkezî: `src/pages/[...slug].md.ts` her içerik sayfasının
markdown kaynağını `<adres>index.md` olarak yayınlar (1.136 dosya). Kapı
`scripts/site-seo-denetle.py` içindedir ve **varlığı yetmez** diye yazılmıştır:
kopyanın gövdesindeki `> Page:` satırı sayfanın canonical'ına eşit olmalıdır. İlk
koşumda açılış sayfası `/index/index.md` altına, var olmayan `/index/` adresiyle
yazıldı — sebebi loader'ın `concepts/index.md`'den `index`'i düşürüp site kökünde
düşürmemesi. Site içinden bakan hiçbir kontrol bunu göremezdi.

İki yan koşul, ikisi de `deploy/nginx.conf`'ta:

- Stok `mime.types`'ta `.md` **yoktur**. `location ~ \.md$ { default_type text/markdown; }`
  kullan — server seviyesinde `types { }` bloğu **miras alınan tüm haritayı siler**
  ve `text/css` ile `image/png`'yi de götürür.
- `charset_types`'a `text/markdown` ekle, ama `text/html` **yazma**: o zaten örtük ve
  listelemek `nginx -t`'de uyarı verir.

## 🚨 robots.txt'te isimli grup, `*` grubunu TAMAMEN geçersizler (2026-09-14)

RFC 9309: bir crawler yalnız **en özgül** eşleşen grubu okur, kalanını yok sayar.
`User-agent: *` altına yazılan `Disallow: /pagefind/fragment/`, `User-agent: GPTBot`
grubu eklendiği anda GPTBot için **etkisiz** olur. Yol kısıtları her gruba tekrar
yazılır (`src/pages/robots.txt.ts`).

İkinci tuzak: kısıtı `Allow: /`'dan **önce** yaz. Uyumlu parser en uzun eşleşmeyi
alır ve sıra önemsizdir, ama Python'un kendi `urllib.robotparser`'ı ilk eşleşmeyi
alır — `Allow: /` başta iken pagefind indeksini taranabilir raporladı. Sıralama
bedava; kuralı iki okumada da doğru kılar.

Politikanın kendisi `site.config.mjs`'deki `crawlerPolicy`'dedir — karar veri,
endpoint yalnız render eder. Ölçülmüş iki gerçek orada kayıtlı: Google-Extended ve
Applebot-Extended kapatmak Search/AI Overviews ve Siri kapsamını **etkilemez**
(ikisinin de kendi dokümanı); CCBot kapatmanın etkisi en geniştir.

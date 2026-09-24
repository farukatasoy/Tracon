# Dokumantasyon Tuzaklari

> **Kardeş dosya:** geliştirme defterinin (`KARARLAR.md`, `docs/arsiv/fazlar/`)
> BAKIMI ayrı bir dosyadadır: [`defter-bakimi.md`](defter-bakimi.md). Bu dosya
> **sevk edilen** ve kullanıcıya dönük dokümanın tuzaklarını taşır.

> Sevk edilen dokumantasyonun DOGRULUGU (paketlenen XML, paket README'leri,
> paketlenen OpenAPI belgesi, metin kapisi yazma tuzaklari).
>
> Site METNININ yazim tuzaklari (markdown ayristiricisi, makine okuyucusu)
> AYRI dosyadadir: [`site-icerik-yazimi.md`](site-icerik-yazimi.md).
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

**Duzeltme tabani tazelemek DEGILDIR, cumleyi yeniden yazmaktir.** Kapinin
tabani (`shipped-documentation-baseline.txt`) yalniz **kucululur**; bir dosya
icin sayiyi buyutmek borcu kalicilastirir. 2026-09 kapanisinda bu kapi **uc ayri
ailede** (D · I · J) kirmiziya dondu — her seferinde yeni yazilan bir `///`
blogunda bir 🚨 ya da bir `K-NNN` referansi vardi, ve her seferinde cozum tek bir
cumlenin yeniden yazilmasiydi. Yeni bir `///` blogu yazdiktan sonra
`grep -n "🚨\|K-[0-9]" <dosya> | grep "///"` kos.

## 🚨 Sayısal sıralama knob'unun YÖNÜ dokümanda ters yazılır

`IAgentDecorator.Order`'ın `<summary>`'si yıllarca gerçek davranışın tersini
söyledi ("a lower value wraps *inside*", oysa düşük değer **dışta** sarar).
Üç şey bunu görünmez yaptı:

- **Davranış testi bir cümleyi göremez** — bu sınıfın kapısı kaynak **metnini**
  okumak zorundadır.
- **"Higher priority" İKİ anlama gelir.** Sıralama dokümanında "higher/lower
  priority" yazma — **"lower value"** yaz. Vakalar:
  [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).

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

## 🚨 Uretilen referans bir KESIF yuzeyi degildir (2026-09-03)

`quota.threshold` sevk edilmisti ve calisiyordu; anlati onu hic anlatmiyordu.
Tuketici ozelligi bulamadi ve **var olani yeniden onerdi**. Ciplak varsayilan
satiri davranisi anlatmaz; uretilen referansa adini **zaten bilen** bakar.
Kapi: `sevk_edilen_olay_anlatisi()`.

Ayni tur: **oznesiz cumle.** "A channel that reaches capacity drops the event"
tuketicinin KENDI kanalini tarif ediyordu, Tracon'inki gibi okundu —
Tracon'in kanali yoktur. Sorumlulugu anlatan cumle oznesini yazsin.

## 🚨 Bir çalışma anı şartını ÜYE ÜYE anlatmak, şartı üyeye özgü gösterir (2026-09-07, F-212)

**Kural: bir çalışma anı şartı bir kez, TİP düzeyinde yazılır; üye yalnız KENDİ
muafiyetini anlatmak için o şarttan söz edebilir.** Tekrar, kuralı üyeye özgü
gösterir — ve muafiyeti yazmayan üye, genel kuralı uygulayan okuyucuyu yanlış
sonuca götürür.

**Kapı:** `RunEventPayloadSuppressionTests` (üç test). Değerli olan ikincisidir:
yazıcının koşulundaki `RunEventType.X` kümesi ile tip düzeyi `<remarks>`'ta adı
geçen küme **eşit** olmalıdır — dördüncü bir muafiyet eklemek, doküman onu
adlandırana kadar kapıyı kırar. Kümeyi karşılaştırmak İngilizceyi okumak
değildir; bu, `RunEventPayloadContractTests`'in belgelenmiş sınırının
denetlenebilir yarısıdır. Ölçüm:
[`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Kapı sapmayı YAKALAR; sapmayı ÜRETEN protokol düzeltilmezse sınıf kapanmaz** (2026-09-08): `00-INDEKS.md` 1488 case yazıyordu, gerçek 1597'ydi — 36 ailenin **17'si** bayat. Sebep tek bir cümleydi: `faz-tamamlama` Adım 3 indeksi yalnız "alan dosyası yoksa" güncelletiyordu, yani mevcut bir aileye case eklemek sayacı hiç güncellemiyordu. Faz 157 bir aile dosyasına 170 satır ekledi ve indekse dokunmadı. **İki düzeltme birlikte gerekir:** kapı (`manuel_test_sayim_kaymasi`) unutulanı yakalar, protokol adımı unutulmayı önler. Yalnız kapı eklemek her fazda bir kırmızı üretir ve insanları onu susturmaya eğitir.
- **🚨 Bir kapı İLK koşumunda kırmızı yanarsa, önce KAPIYI doğrula** (2026-09-08, `bagimlilik_surum_damgasi` yazılırken): kapı `IVectorSearchStore.cs:16`'daki `10.8.0` damgasını `Microsoft.Extensions.AI` pini `10.9.0` ile karşılaştırıp sapma bildirdi. Sapma **yoktu** — damga `Microsoft.Extensions.VectorData` hakkındaydı ve repo o paketi hiç almıyor; hatalı olan kapının eşleme kaydıydı. Kod "düzeltilseydi" doğru bir cümle yanlışla değiştirilecekti. Kural: yeni kapının ilk bulgusu bir kanıttır, bir emir değil. Pinlenmeyen paket için kayıt `None` taşır — bu bir atlama değil, yazılı karardır.
- **🚨 Üretilen dosyayı `Edit`'ten korumak ÜRETECİ kilitlemez — ama `ask` bir kilit de değildir** (Faz 167, `claude 2.1.269`): `Edit(<yol>)` deny'ı `Edit` + `Write` **ve** `rm <yol>` Bash komutunu kapsar; `dokuman-bakim.py` **Python ile** yazdığı için üretim modu kuralı hiç tetiklemez (`MT-GDK-029`). Ama `ask` (`docs/arsiv/fazlar/*.md`) oturumun izin moduna tabidir: auto mode'da sınıflandırıcı **sessizce onaylar**, aynı oturumda `deny` sertçe durur. Her iddia kontrol koşumuyla ayırt edildi — kural yokken aynı `rm` dosyayı sildi. Ölçümler ve sınırlar: K-761 · K-763. Doküman kuralını araca taşırken sorulacak soru "kural kondu mu" değil, **"hangi yazma yolunu gerçekten kapatıyor"**.

## 🚨 Internal tipin XML dokumani da sevk edilir (2026-09-22, Faz 182)

Derleyici `GenerateDocumentationFile` ciktisina internal tiplerin dokumanini da
yazar (olculdu: `T:Tracon.FreeFormJson` `Tracon.Abstractions.xml` icinde). Bir
tip `internal`'a cekilince uc sey bayatlar ve hicbiri derlemeyi kirmaz:

- tipin kendi dokumanindaki "This class is **public** because ..." cumleleri
  (uc vaka: `CanaryEvaluator`, `ChildRunApproval`, `ChatHistoryState`);
- public bir tipin `<see cref>`'i — ayni derlemede sessizce derlenir, docfx onu
  baglantisiz koda cevirir (site kirilmaz ama okuyucu gorunmeyen bir tipe gider);
- public bir seam'in tuketiciye o tipi kullanmasini soyleyen cumlesi
  (`IMigrationApplier`, `IStatePreflightReader`).

Tarama: daraltilan her ad icin `git grep -n 'cref="<Ad>' -- src` ve
`git grep -n '<c><Ad>' -- src`; public tipte olanlar duz metne cevrilir.
- **🚨 Bir `§N` atfı, başlığı silinince sessizce bayatlar** (2026-09-24, Faz 186). `14-SKILL-VE-SCRIPT.md`'nin bölüm başlıkları ve §6'nın ortak kurulumu Faz 58'de silindi (`946a37fb`, 459 satır); case'ler Faz 186'ya kadar `§6'nın 1-5. adımları`'na atıf yapmaya devam etti ve kurulum hiçbir yerde yoktu. Hiçbir kapı `§` atfını çözmez: bir bölüm silinirken ya da taşınırken aynı dosyada `grep -n "§N"` koşulur.

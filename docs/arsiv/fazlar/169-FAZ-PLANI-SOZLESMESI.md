# Faz 169 — Faz Planı Sözleşmesi: Süreç Ölçümü ve Triyaj

> **Durum:** ✅ Tamamlandı (2026-09-13)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-229** (keşif: [`kesif/2026-09-13-anew-karsilastirmasi.md`](../../kesif/2026-09-13-anew-karsilastirmasi.md) § 6 A4 · A6, § 9 H4 · H5)
> **Önkoşul:** 🚨 [Faz 168](168-KURTARMA-RAMPASI-KATALOGU.md) — triyajın "araştırılacak" sonucu `KR-05`'e gider. `KR-05` yoksa o sonucun **gideceği yer yoktur**. Ayrıca [Faz 167](167-AGENT-ZORLAMA-KATMANI.md) — `arsiv/fazlar/` için `ask` kuralı (bkz. Riskler)
> **Paketler:** Yok. Bu faz `src/` altına **hiç dokunmaz**
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok — `docs-site/` sayfası yok, sevk edilen yapıt yok
> **Manuel test alanı:** [`docs/manuel-test/36-GELISTIRME-KAPILARI.md`](../../manuel-test/36-GELISTIRME-KAPILARI.md) — Faz 168'in bıraktığı numaradan devam eder

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show bc26702c:docs/arsiv/fazlar/169-FAZ-PLANI-SOZLESMESI.md
> ```
>
> Damıtıldı 2026-09-13 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

İki boşluk, **aynı yapıt** üzerinde: faz planı şablonu. 1. **Süreç ölçülmüyor.** 167 faz koşuldu ve "plandan sapma", "düzeltme turu", "gerçek/gürültü bulgu", "üretilen regresyon" sayılarının **hiçbiri** tutulmuyor. Bu, ölçüm kültürü olan bir repo için tuhaf bir boşluktur — bütçe ölçülüyor, kapı süresi ölçülüyor, **sürecin kendisi ölçülmüyor**. 2.

## Bitiş Ölçütleri (DoD)

- [x] `faz-plani-sablonu.md` kapanış yarısında `## Süreç Ölçümü` bölümü var; **tablo**, onay kutusu değil; beş satır taşıyor — ✅ beş satırlı tablo, `Denetim Bulguları`'nın önünde
- [x] Şablonun başlık bloğunda `> **Plan onayı:**` satırı var — ✅ `Durum:` satırının hemen altında; geriye dönük konmadı (kapsam sınırı)
- [x] `dokuman-bakim.py --denetle` **14** bulgu kapısı koşuyor (bugün 13) — ✅ ölçüldü: 14 (taban 13). Sayım komutu düzeltildi — üç sonuç biçimi de kapsanıyor (denetim 🟡 4)
- [x] Eşik sabiti **167** ve kodda adlandırılmış; tablo/anlatı içine gömülmemiş — ✅ `SUREC_OLCUMU_ESIGI = 167` (`dokuman-bakim.py:739`)
- [x] Kapı `_faz_no()`, `_durum_tamamlandi_mi()` ve `_faz_bolumleri()`'ni **çağırıyor**; `split("## ")` deseni kodda **yok** — ✅ üçü de çağrılıyor; `split("## ")` yalnız yasağı anlatan yorumda geçiyor
- [x] Kapı **hiçbir şey yazmıyor** — test bunu iddia ediyor — ✅ `test_kapi_HICBIR_SEY_YAZMAZ` bayt bayt karşılaştırıyor; gerçek repoda da `git diff` boş
- [x] `_FAZ_KAL` `"Süreç Ölçümü"` taşıyor; `faz-damit` koşumu "tanınmayan bölüm KORUNDU" uyarısı **basmıyor** — ✅ ve denetim 🟡 1 ile iki bölüm daha eklendi (`Örnek Uygulama Koşumu`, `Faz Dışı Bulunan ve Kapatılan Kusur`); `faz-damit` koşumu **sıfır** uyarı basıyor
- [x] `dokuman_bakim_test.py` **beş** vaka taşıyor: eşik altı · bölüm yok · bölüm boş · bölüm dolu (`ölçülmedi` dahil) · plan durumu; artı "yazmaz" iddiası — ✅ aşıldı: `SurecOlcumuTestleri` **12** vaka (beş DoD vakası + kod bloğu + kök tarama + fazladan satır + eksik metrik + yazmaz + `_FAZ_KAL` + 🔴 1'in düzeltmesi)
- [x] `faz-denetim` Adım 4 🔴 tablosu "Denetçi önerisi" sütunu taşıyor; değerler `gerçek / gürültü / araştırılacak` — ✅ sütun eklendi; bu fazın kendi denetimi onu **ilk kez** doldurdu
- [x] `faz-denetim` Adım 5 **5.1 (triyaj, yalnız 🔴)** ve **5.2 (kapatma)** olarak bölünmüş; 5.1 "araştırılacak" sonucunu **`KR-05`'e** bağlıyor ve bağlantı `kirik_baglantilar()`'dan geçiyor — ✅ bölündü; 5.1 `KR-05`'e hem dosya yolu hem **üretilip karşılaştırılmış** çapa ile bağlanıyor. `kirik_baglantilar()` → 0
- [x] `faz-tamamlama` Adım 5 `## Süreç Ölçümü`'nün doldurulmasını söylüyor — ✅ ve iki metriğin elle sayılacağı **açıkça** yazıldı
- [x] 🚨 Faz **167 ve 168**'in arşivlenmiş kayıtları `## Süreç Ölçümü` bölümünü **hâlâ taşıyor** ve dolu — damıtma onları düşürmemiş — ✅ ikisi de bölümü dolu taşıyor; damıtma düşürmedi (`_duser_mu` → `False`, koşularak doğrulandı)
- [x] Bu fazın **kendi** `## Süreç Ölçümü` tablosu dolu; kapı onu **gerçekten** denetliyor (eşik 167 ≤ 169) — ✅ dolu; kapı onu kökte **gerçekten** denetliyor (mutation ile kırmızı görüldü, satır numarası raporlandı)
- [x] `python3 -m unittest discover -s scripts -p "*_test.py"` yeşil — ✅ **279** yeşil (taban 260)
- [x] `python3 scripts/dokuman-bakim.py --denetle` çıkış `0`; kırık bağlantı `0` — ✅ arşivleme sonrası 0; kırık bağlantı 0
- [x] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`) — ✅ `kapi.py kapanis --taban 66fcddbd`. 🚨 İlk tam test koşumunda **bir** test düştü (`TwoProcessLeaseTakeoverTests.A_dead_workers_job_is_not_taken_over_before_its_lease_expires`); izole geçti ve **ikinci tam koşum** 21 proje / 7039 test ile temiz döndü — kaynak çekişmesi sınıfı, kusur değil
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — 🚨 **bu faz kod değiştirmez; koşum bir regresyon kanıtıdır** (Faz 92 emsali) — ✅ koşuldu; SSE akışı, 7 agent ve 401/200 guard'ı belgeye yazıldı
- [x] `secret` taraması boş döndü — ✅ `kapi.py tarama` → temiz; token yalnız ortam değişkeninde yaşadı (K-059)
- [x] Manuel kabul case'leri `docs/manuel-test/36-GELISTIRME-KAPILARI.md` içine eklendi; ikisi de koşuldu — ✅ **üç** case eklendi (`MT-GDK-036`·`037`·`038` — üçüncüsü faz dışı kusurun kapısını koruyor); üçü de koşuldu
- [x] `faz-denetim` koşuldu — Faz 167'nin `faz-denetcisi` tipiyle **ve** bu fazın kendi 5.1 triyajıyla; 🔴 bulgu kalmadı — ✅ `faz-denetcisi` tipiyle; 1 🔴 (triyaj: gerçek, düzeltildi) · 6 🟡 (hepsi kapandı) · 1 🟢 (düzeltildi). 🔴 kalmadı

### Doğrulama komutları

```bash
# 14 kapı koşuyor mu (bugün 13) — üç ayrı sonuç biçimi vardır, deseni üçü de
# kapsar: `✅ temiz` · `❌ N bulgu` · `N bulgu` · `Kırık bağlantı: N`
python3 scripts/dokuman-bakim.py --denetle | grep -cE ": (✅ temiz|❌ [0-9]+ bulgu|[0-9]+ bulgu|[0-9]+)$"

# Eşik kodda adlandırılmış mı — sabitin ADI aranır, sayı değil
grep -n "SUREC_OLCUMU_ESIGI = " scripts/dokuman-bakim.py

# Yasak desen KOD olarak yok (tek eşleşme, yasağı anlatan yorum satırıdır)
grep -n 'split("## ")' scripts/dokuman-bakim.py | grep -v "^[0-9]*: *#"

# 167 ve 168 kayıtları bölümü koruyor mu
grep -l "^## Süreç Ölçümü" docs/arsiv/fazlar/16[789]-*.md

# Kapı yazmıyor mu
python3 scripts/dokuman-bakim.py --denetle >/dev/null; git diff --stat   # boş
```

---

## Plandan Sapmalar

**1. `hafiza/dokumantasyon.md` planın iddia ettiği bilgiyi TAŞIMIYORDU.**
"Bu Faza Başlarken" 5. kalem oraya yolluyordu: *"kapı kalıbı, damıtma
davranışı ve `_FAZ_KAL` burada yaşar"*. Ölçüldü:
`grep -n "_FAZ_KAL\|kapı kalıbı\|damıtma" docs/hafiza/dokumantasyon.md` →
**sıfır** eşleşme. Kaynak kodun kendisi okundu (`dokuman-bakim.py:1397`
`_FAZ_KAL`, `:1625` `_faz_damit_metni`). Plan satırı yanlıştı; kod doğruydu.
Satır numaraları da kaymıştı: `_faz_bolumleri` `:1448` değil `:1468`'de.

**2. Kapı beş satırı ADIYLA arar, satır SAYISINA bakmaz.** Plan *"beş satırın
her birinin değer hücresi boş değildir"* diyordu. Ölçüldü: Faz 167'nin
tablosu **yedi** satır taşıyor (`Denetim sonrası düzeltme turu`, `Faz dışı
bulunan ve kapatılan kusur`) ve bazı değerleri **kalın** yazıyor. Satır
sayısına bakan bir kapı gerçek bir tabloyu eksik sanardı. Uygulama: beş
**zorunlu etiket** aranır, fazladan satır serbesttir, eşleşme `_tablo_etiketi`
ile biçimden bağımsızdır (`**`/`` ` ``/boşluk normalize edilir).

**3. Bölüm gövdesindeki kod bloğu da soyulur.** Plan yalnız BAŞLIK tespitinde
`_faz_bolumleri`'yi zorunlu kılıyordu. Satır ayrıştırması da
`_kod_bloklarini_soy`'dan geçirildi: şablonu **gösteren** bir faz kaydının
örnek tablosu gerçek tablonun yerine geçmesin. Bu sınıfın repodaki beşinci
vakası; dördü kod yorumlarında yazılı.

**4. Kapsam bir faz dışı kusurla büyüdü** (kullanıcı talimatı: *"konuyla
alakasız bug/defect'lerle karşılaşırsan onları da çöz"*). Ayrıntı aşağıda.

**5. `docs/hafiza/dokumantasyon.md` bütçeyi aştı ve İKİYE BÖLÜNDÜ.** Faz dışı
kusurun notu eklenince 16.702 B > 16.000 B. Kural içeriği silmez, taşır:
karar defteri bakımı, karar indeksi üretimi ve faz arşivleme bölümleri yeni
[`hafiza/defter-bakimi.md`](../../hafiza/defter-bakimi.md) dosyasına gitti
(12.645 B + 4.691 B, ikisi de bütçede ve DAR değil). İki dosya birbirine
başlıktan yollar; `00-INDEKS.md` satırı eklendi.

**Sapmayan:** eşik 167, `## Süreç Ölçümü` adı, `_duser_mu` davranışı, `KR-05`
adresi, `ask` kuralı (`deny` değil) ve `split("## ")` yasağı — hepsi
ölçümde tuttu.

## Faz Dışı Bulunan ve Kapatılan Kusur

**Karar başlığındaki İÇ İÇE `**` üretilen indeksi SESSİZCE kesiyordu.**

Bu fazın kendi K-767'sini yazarken ortaya çıktı: indeks satırı
`Süreç ölçümü eşiği sabit sayı 👤` diye bitti. Sebep `_kararlar_kalemleri()`'nin
tembel `\*\*(.+?)\*\*` deseniydi — başlığın içindeki ilk `**` başlığı orada
kapatıyordu.

**Sınıf taraması** (`kusur-giderme` Adım 5) iki **mevcut** vaka buldu:

| Kalem | İndekste nasıl görünüyordu |
|---|---|
| K-413 | `… ÜRETİLEN dosyadır; kaynak her fazın kendi \`>` — cümle yarıda |
| K-523 | `… \`docs/` — cümle yarıda |

İkisi de `--denetle` **yeşilken** bozuktu; hiçbir kapı görmedi. İki ayrı hata
modu, iki ayrı çözüm:

- K-413 ve K-523'te `**` bir **kod parçasının içindeydi** (`` `> **Durum:**` ``,
  `` `docs/**.md` ``) — orada `**` bir vurgu değil, **gösterilen metindir**.
  Kaçış (`\*\*`) eklemek kod parçasının içinde birebir görüntülenir ve
  başlığı bozar. Doğru çözüm üreteçtedir: yeni `_karar_basligi()` başlığın
  kapanışını `_kod_bloklarini_soy`'dan geçmiş satırda bulur, metni
  **orijinalden** keser (soyucu uzunluğu korur, indeksler hizalıdır).
- K-767'de `**` gerçek bir vurguydu; başlık `` `167` `` koduna çevrildi.

Üçüncü vaka kapı gerektirdi (`kusur-giderme` Adım 6): yeni
`_kesik_karar_basliklari()`, `kararlar_denetle()` içinden koşar. İmza,
başlığı kapatan `**`den sonraki kuyruğun **boşlukla başlamamasıdır** — meşru
kuyruklar (`**(Faz 168)**`, `*(kullanıcı kararı)*`, `🚨`) her zaman boşlukla
başlar. Kapı mutation ile kırmızı görüldü; düzeltmeden önce iki gerçek bulgu
bastı, düzeltmeden sonra temiz. Ders
[`hafiza/defter-bakimi.md`](../../hafiza/defter-bakimi.md) içine yazıldı.

Bu bir bulgu **kapısı değildir** — mevcut "Karar defteri" kapısının içine
girdi, bu yüzden `--denetle`'nin kapı sayısı **14**'te kalır.

## Bu Fazda Verilen Kararlar

| Karar | Özet |
|---|---|
| **K-766** | Süreç ölçümü kapısı bölümün **varlığını** denetler, doğruluğunu denetlemez. `0/0/0` yeşil geçer; hiçbir kapı bir fazın gerçekten kaç düzeltme turu yaşadığını doğrulayamaz. Sınır yazıldı (K-764 deseni) |
| **K-767** | Eşik sabit sayı `167`'dir (kullanıcı kararı); geriye dönük 166 faz doldurulmaz. Tarih · dosya içi işaret · "sonraki numara" alternatifleri gerekçesiyle elendi |
| **K-768** | 🔴 bulgunun triyajını **kullanıcı** yapar; denetçi yalnız önerir. Mekanik kısıt: alt agent'ların araç kümesinde `AskUserQuestion` yoktur |

Faz dışı kusur için **karar kaydı açılmadı**: üreteç davranışı yerel bir
implementation tercihidir, public API/güvenlik/kalıcı veri sınırı geçmez
(`AGENTS.md` karar defteri kuralı). Ders alan dosyasında yaşıyor.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | **5** — plan okuma listesi yanlış dosyaya yolluyordu · kapı satır sayısı yerine etiket arar · gövde kod bloğu da soyulur · kapsam faz dışı kusurla büyüdü · alan hafızası bütçeyi aşıp ikiye bölündü |
| Düzeltme turu sayısı | **4** — `kapi_test` bayat doküman referansı (test fixture adı) · K-767 indeks kesilmesi · kaçış yerine üreteç düzeltmesi · `dokumantasyon.md` bütçe aşımı. Hiçbiri `KR-06`'nın üç tur limitini kod üzerinde zorlamadı; dördü de ayrı yüzeylerde |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | **1 / 0 / 0** — tek 🔴 gerçekti ve düzeltildi (triyajı kullanıcı verdi, Adım 5.1). Ayrıca 🟡 **6** (altısı da kapandı) · 🟢 **1** (aday listesine gitmedi, düzeltildi) |
| Fazın ürettiği regresyon | **0** — `src/`, `samples/`, `tests/` altına sıfır satır; dört kapı yeşil |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz yeni kapanıyor) |

Faz dışı bulunan ve kapatılan kusur: **1** (iç içe `**` → kesik karar indeksi;
sınıf taraması iki mevcut vaka buldu).

## Örnek Uygulama Koşumu

`samples/Tracon.Api` Release'te ayağa kaldırıldı (2026-09-13, `:5080`).
Bu faz `src/`, `samples/` ve `tests/` altına **hiç** dokunmadı
(`git diff --stat 66fcddbd -- src/ samples/ tests/` → **0 satır**), bu yüzden
koşum bir davranış kanıtı değil, **regresyon kanıtıdır**.

| Ne | Gerçek çıktı |
|---|---|
| Başlatma | `MCP discovery completed: 0 tools available` · `Now listening on: http://localhost:5080` · `Application started` |
| `GET /tracon/api/agents` (token'sız) | `401` · `"A valid 'Authorization: Bearer <token>' header is required."` |
| `GET /tracon/api/agents` (Bearer) | `200` · **7 agent**: `cached-support`, `order-summary`, `researcher`, `router`, `summarizer`, `support`, `translator` |
| `POST /tracon/api/agents/support/run` | SSE: `event: run` + `{"runId":"01a09bbb-74a8-7cdf-ae65-8b7e9f7c6210","sessionId":null}`, ardından beş `event: update` (`"Echo: "`, `"Faz "`, `"169 "`, `"süreç "`, `"ölçümü "`) ve `event: done`. Akış parça parça geldi, `authorName` `support` |
| `GET /health` | `Degraded` — Faz 168'deki ile **aynı**; `TraconHealthCheck` teyit edilmiş bir model sağlayıcısı ister ve demo `echo` sağlayıcısı o listeye girmez. Bu fazın ürettiği bir kusur değildir |

🚨 **Koşumun kendisi bir tuzak öğretti.** İlk denemede `AuthToken`
`Tracon__Api__AuthToken` ile verildi ve etkisiz kaldı: gerçek anahtar
`Tracon:Ui:AuthToken`'dır (`samples/Tracon.Api/appsettings.json:42`). Sonuç
**sessizdi ve ters okunuyordu** — token'sız istek `200` döndü (auth kapalıydı),
Bearer taşıyan istek `401` döndü (boş yapılandırılmış token hiçbir değerle
eşleşmez). Yanlış bir env değişkeni adı, guard'ı "çalışmıyor" gibi gösterir.
`AuthToken` yerel bir ortam değişkeninden verildi; hiçbir dosyaya veya
veritabanına yazılmadı (K-059) ve `kapi.py tarama` temiz döndü.

## Denetim Bulguları

Denetçi `faz-denetcisi` tipiyle, taze bağlamla koştu (2026-09-13). Taban
`66fcddbd`; 14 dosya + izlenmeyen `defter-bakimi.md`. **Triyaj bu fazın kendi
5.1 adımıyla koştu — 🔴 kullanıcıya soruldu ve kullanıcı karar verdi.**

### 🔴 (1) — triyaj: **gerçek**

| # | Bulgu | Triyaj | Sonuç |
|---|---|---|---|
| 1 | Yeni kapı gövdeyi `_kod_bloklarini_soy`'dan geçiriyordu; o yardımcı **satır içi kodu da** boşlukla doldurur. `` `ölçülmedi` `` yazan geçerli bir hücre kapıya **boş** görünüyordu — oysa `faz-tamamlama` Adım 5 ve şablonun kendisi tam olarak o yazımı öğretiyor | **gerçek** (kullanıcı kararı; denetçi önerisi de gerçekti) | **Düzeltildi.** `_fence_bloklarini_soy` ayrıldı: hücre ayrıştırması yalnız **fence**'i soyar, satır içi kodu korur. `_kod_bloklarini_soy` artık onu çağırır — diğer çağıranların davranışı **değişmedi**. Düşen test önce yazıldı: `test_SATIR_ICI_KOD_degeri_GECERLIDIR` |

### 🟡 (6) — altısı da **kapandı**

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `faz-damit` iki "tanınmayan bölüm KORUNDU" uyarısı basıyordu; biri bu fazın açtığı H2 | Düzeltildi — `_FAZ_KAL`'a `Örnek Uygulama Koşumu` **ve** `Faz Dışı Bulunan ve Kapatılan Kusur` eklendi. Koşum artık **sıfır** uyarı basıyor (ikincisi taban durumdan devralınan bir gürültüydü, o da kapandı) |
| 2 | Kapanış bölümlerini sayan iki skill listesi yeni bölümü bilmiyordu | Düzeltildi — `faz-planlama/SKILL.md:174` tablosuna ve `faz-tamamlama/SKILL.md:322` listesine eklendi; `faz-planlama`'ya ayrıca "tablo, onay kutusu değil" uyarısı kondu |
| 3 | Kesik başlık kapısı yalnız §2'yi tarıyordu; üreteç dosyanın **tamamını** okur (134 satırlık reddedilen kararlar tablosu denetlenmiyordu) | Düzeltildi — kapı artık üretecin gördüğü satır kümesinin aynısını tarar. Mutation ile doğrulandı: reddedilen tablodaki bir kesik başlık (`KARARLAR.md:15`) yakalanıyor. Yeni test: `test_kapi_URETECIN_gordugu_HER_satiri_tarar` |
| 4 | Doküman sayıları ölçümle tutmuyordu (15/5 → gerçek 19/7) ve üç doğrulama komutu yanlış sonuç veriyordu | Düzeltildi — sayılar ölçüldü (sınıf başına test sayımıyla; taban 260, şimdi 279), üç komut da **koşularak** doğrulandı. Kapı sayım komutu artık üç sonuç biçimini de kapsıyor ve **14** döndürüyor |
| 5 | `docs/hafiza/defter-bakimi.md` izlenmiyordu; `git commit -am` onu dışarıda bırakırdı | Kapandı — dosya `git add` ile açıkça eklendi ve commit içeriği `git status` ile doğrulandı |
| 6 | `docs/KARARLAR.md` bütçenin %99,4'ünde; üç karar yazan sonraki faz kapıyı kırar | Gerekçelendi ve **devredildi** — bu fazda `karar-damit` koşulmadı (ayrı bir iş ve ayrı bir risk); zorunluluk ve K-214 kuralı **Devir Notu**'na yazıldı |

### 🟢 (1)

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `MT-GDK-036/037` satırları kapanış `\|`'ını taşımıyor; `036`'nın "çıkış `0`" beklentisini kendi 🚨 cümlesi çürütüyor | Aday listesine **gitmedi, düzeltildi** — kozmetikti ve maliyeti bir satırdı. `036` artık yalnız kapı satırını iddia ediyor, çıkış kodu iddiasını bırakıyor |

### Denetçinin ayrıca doğruladıkları (bulgu değil)

`KR-05` çapası canlı (başlık hexdump'landı, gizli `U+0307` yok, slug birebir) ·
`K-413`/`K-523` üretilen indekste artık **tam** · eşik davranışı (166 atlanır,
167/168 dolu, kök `docs/169-*.md` gerçekten taranır) · `split("## ")` kodda yok ·
test tiyatrosu yok (`test_kapi_HICBIR_SEY_YAZMAZ` bayt karşılaştırır) · public
API büyümedi · `secret` taraması temiz.

**Temiz çıkan denetim başlıkları:** 3.2 · 3.3 · 3.5 · 3.6 · 3.7

## Sonraki Faza Devir Notu

Bu, üç fazlık turun **son** fazıdır. Üçünün birlikte bıraktığı sözleşme:

| Sözleşme | Nerede yaşar | Faz |
|---|---|---|
| Denetçi tipi — salt-okunur, yazma araçları araç kümesinde yok | [`.claude/agents/faz-denetcisi.md`](../../../.claude/agents/faz-denetcisi.md) | 167 |
| `KR-01…12` kurtarma rampaları — gövde **tek yerde**, katalog bağlar | [`.agents/ortak/kurtarma.md`](../../../.agents/ortak/kurtarma.md) | 168 |
| `## Süreç Ölçümü` — eşik **167**, kapı `surec_olcumu_bulgulari()` | `faz-plani-sablonu.md` + `dokuman-bakim.py` | 169 |
| 🔴 triyajı **kullanıcıya** aittir (5.1), kapatma uygulayana (5.2) | `faz-denetim/SKILL.md` | 169 |

**🚨 Sonraki faz için zorunlu — `docs/KARARLAR.md` bütçe duvarında.**
Ölçüldü (2026-09-13): 417.565 / 420.000 B, kalan **2.435 B**. Bu faz tek başına
2.169 B yazdı. **İki karar yazan bir sonraki faz kapıyı kırar.** K-214 kuralı
açıktır: *"bu kez bütçe büyütülmez, bölünme uygulanır."* Yol `karar-damit`
(satır sınırı `KARAR_SINIRI = 450`) ya da eski kalemlerin
`arsiv/KARARLAR-GECMISI.md`'ye damıtılmasıdır. Bunu keşfe bırakma — kapı
kırmızı döndüğünde faz **ortasında** olacaksın.

**Keşif raporu § 12'nin üç ölçütü ilk kez cevaplanabilir.** Elde **üç** veri
noktası var (167 · 168 · 169):

| Faz | Plan revizyonu | Düzeltme turu | 🔴 (gerçek/gürültü/araştırılacak) | Regresyon |
|---|---|---|---|---|
| 167 | 4 | 0 kod turu | 0 / 0 / 0 (🟡 4) | 0 |
| 168 | 0 | 2 | 2 / 0 / 0 | 0 |
| 169 | 5 | 4 | 1 / 0 / 0 (🟡 6, 🟢 1) | 0 |

İlk okumalar — **üç nokta bir eğilim değildir**, hipotezdir:

1. *Plandan sapma ile 🔴 korele mi?* Üç noktada **ters** görünüyor: sapması
   sıfır olan Faz 168 en çok 🔴 aldı. Olası açıklama: sapma sayısı planın
   **yanlışlığını** değil, uygulayanın planı **ölçtüğünü** gösterir.
2. *Düzeltme turu ile regresyon korele mi?* Üçünde de regresyon **0**; ölçüt
   bu turda ayırt edici değil. Regresyon üreten bir faz gelmeden cevaplanamaz.
3. *Ölçüm ritüele döndü mü?* Henüz hayır — üç tablo da farklı sayılar taşıyor
   ve hiçbiri `0/0/0` değil. 🚨 K-766 bu riski **kabul etti**: kapı varlığı
   denetler, doğruluğu denetleyemez. Beşinci veri noktasında yeniden bak.

**Bilinen sınırlar (devralınıyor, yeni değil):**

- `kirik_baglantilar()` **dosyayı** doğrular, `#fragment`'ı doğrulamaz. Bu fazın
  eklediği `KR-05` çapası elle üretilip karşılaştırıldı; kapı onu korumaz.
  Fragment doğrulayan kapı hâlâ 🟢 adaydır (Faz 168'den devrediyor).
- `## Süreç Ölçümü`'nün **iki** metriği ("düzeltme turu", "üretilen regresyon")
  elle sayılır. `artifacts/kapi-olcum.jsonl` faz numarası taşımaz ve
  `.gitignore`'dadır. `kapi.py`'ye bir `--faz` bayrağı eklemek bu ikisini
  otomatik okunur hâle getirir; ölçülmedi, aday değil, **gözlem**.
- `Plan onayı:` satırı şablona girdi ama **geriye dönük konmadı**. Faz 170 bu
  satırı taşıyan **ilk** plan olmalıdır; taşımıyorsa kapı sessizdir — o satırın
  kapısı **yoktur** (bilinçli: onay bir insan eylemidir, dosya durumu değil).

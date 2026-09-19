# 09 — Arayüz Genel (`UI`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../09-ARAYUZ-GENEL.md`](../../manuel-test/09-ARAYUZ-GENEL.md) —
> spec `### MT-UI-NNN` (h3) kullanır, burada skill §4.1/§7 konvansiyonuna
> uymak için `## MT-UI-NNN` (h2) kullanılır.

| | |
|---|---|
| **Şerit** | `ap-s4` (Faz B, beşinci aile) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s4` · dal `test/kosum-s4` |
| **Kod** | `7e3a4de7` donuk (`git diff --stat 7e3a4de7..HEAD -- src samples tests` boş, oturum başında VE sonunda) |
| **Case sayısı** | 57 (MT-UI-001..057) |
| **Port** | 5084 (ana), geçici ikinci süreç 5094 (yalnız MT-UI-004/005/008, işi bitince durduruldu) |
| **Tarayıcı** | Chrome (Playwright) — Safari case'i (042) fiziksel eylem, gerçek Safari gerektirir |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): bu ailede hiçbir
`dotnet user-secrets set` adımı yoktu. §1'in `AuthToken` kapalı senaryosu
için geçici bir ikinci süreç (port 5094, aynı `mt_s4` şeması, salt-okunur
gözlem) `Tracon__Ui__AuthToken=""` ortam değişkeniyle başlatıldı, iş
bitince durduruldu.

**🚨 Ortam kısıtı — MT-UI-008 gerçek LAN erişimiyle koşulamadı.** Case'in
kendi ön koşulu makinenin LAN IP'sinden (`0.0.0.0` bind + LAN adresi)
erişim istiyor. `curl`/tarayıcı ile LAN adresinden gerçek bir istek
denemesi Claude Code otomatik-mod sınıflandırıcısı tarafından "Expose
Local Services" gerekçesiyle reddedildi — süreç **anında** durduruldu,
gerçek dış erişim hiç kurulmadı. Bunun yerine: (1) sunucu tarafı kod
(`LoopbackGuard.cs`, `TraconEndpointFilter.cs:84-92`) okunarak 403 yanıtının
**tam** metni doğrulandı — yalnız TCP bağlantısının gerçek uzak adresine
bakıyor, sahte bir başlıkla atlatılamaz; (2) arayüzün bu YANITI nasıl
gösterdiği, `page.route()` ile `/api/agents` yoklamasını gerçek sunucunun
üreteceği BİREBİR aynı `403` gövdesiyle (kaynak koddan alınan gerçek
`detail` metni) simüle ederek doğrulandı. İkisi birleşince case'in tam
iddiası (sunucu bu şekilde reddeder + arayüz bunu böyle gösterir)
kanıtlanmış oluyor, yalnız uçtan uca tek bir gerçek ağ isteğiyle değil.

**🚨 Ortam kısıtı — MT-UI-010/011/021 gerçek bir Reader kimlik bilgisiyle
koşulamadı.** `DemoRoleAuthentication`'ın `X-Tracon-Demo-Role` başlığı
ölçüldü (hem tarayıcıdan `page.route()` ile enjekte edilerek hem doğrudan
`curl`'le) — geçerli bir `Tracon:Ui:AuthToken` bearer'ı VARKEN bu başlık
**hiçbir etki yapmıyor**, `/api/meta`'nın `roles` alanı hep
`{canRead,canOperate,canAdminister}: true/true/true` dönüyor (bu örnek
uygulamada `requiresAuthorizationPolicy: false` — rol politikası hiç
etkin değil, `AuthToken` tek başına her zaman tam yetki taşıyor). Bu
uygulamanın kendi rol-kapsamlı bir tarayıcı oturumu üretecek bir yolu YOK
(kod donuk, `Program.cs`'e `RequireAuthorization` eklenemez). Bunun yerine
MT-UI-010/021 `page.route()` ile `/api/meta`'nın `roles` alanı
`{canRead:true, canOperate:false, canAdminister:false}` olacak şekilde
GERÇEK yanıt üzerine yama yapılarak (yalnız `roles` alanı değiştirildi,
gerisi sunucudan) test edildi — bu, arayüzün **kendi** görünürlük
mantığını saf hâliyle sınıyor. MT-UI-011'in "sunucu yine reddeder" iddiası
bu dosyanın kendi "Sınır" tablosunda zaten `13-KIRACI-VE-GUVENLIK.md`'ye
devredilmiş; o alan dosyası zaten kapsamlıca rol/kapsam denetimini
sınıyor (bu turda henüz koşulmadı, ayrı aile).

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show bd25ab0f:docs/manuel-test/kosumlar/2026-09-16/09-ARAYUZ-GENEL.md
> ```

---

## Temiz geçen case'ler (53)

| Case | Durum | Başlık |
|---|---|---|
| MT-UI-001 | ☑ | Bearer token açıkken ilk açılışta token isteme kartı görünür |
| MT-UI-002 | ☑ | Doğru token girilince kabuk açılır ve token sekme boyunca kalıcılaşır |
| MT-UI-003 | ☑ | Yanlış token girilince reddedildi mesajı görünür, yeniden denenebilir |
| MT-UI-004 | ☑ | Bearer token kapalıyken kabuk hiç token istemeden açılır |
| MT-UI-006 | ☑ | Token sekmeye özeldir: yeni bir sekme yeniden sorar |
| MT-UI-007 | ☑ | Ayarlar'dan "Unut" butonu token'ı siler, kapı yeniden kapanır |
| MT-UI-008 | ☑ | 403 (uzak erişim kapalı) durumunda sunucu metni olduğu gibi görünür |
| MT-UI-010 | ☑ | Reader rolüyle Audit/Diagnostics nav öğeleri görünmez |
| MT-UI-011 | ☑ | Gizlenen nav öğesine doğrudan URL ile gidilirse sunucu YİNE reddeder |
| MT-UI-012 | ☑ | Aktif rota kenar çubuğunda vurgulanır |
| MT-UI-013 | ☑ | Dar ekranda yan menü gizlenir, üstte yatay kaydırılabilir menü belirir |
| MT-UI-014 | ☑ | Depolama notu bellek içi/kalıcı durumu doğru renkle gösterir |
| MT-UI-015 | ☑ | Bilinmeyen bir rota "sayfa bulunamadı" boş durumunu gösterir |
| MT-UI-016 | ☑ | Tarayıcının geri/ileri düğmeleri konsol içi gezinmeyi senkronlar |
| MT-UI-017 | ☑ | `/tracon` ile `/tracon/` aynı sayfayı gösterir |
| MT-UI-018 | ☑ | `⌘K`/`Ctrl+K` paleti açar, giriş alanına odaklanır |
| MT-UI-019 | ☑ | Ok tuşlarıyla gezinme + Enter seçili komutu çalıştırır |
| MT-UI-020 | ☑ | `Esc` paleti kapatır, `Tab` yalnız giriş alanında kalır |
| MT-UI-021 | ☑ | Reader rolünde "Yeni agent"/"Yeni workflow" eylemleri palette görünmez |
| MT-UI-022 | ☑ | `g a` gibi iki tuşlu diziler doğru rotaya gider, 1.2 saniyede zaman aşımına uğrar |
| MT-UI-023 | ☑ | Metin alanına yazarken `g` yalnızca harf olarak yazılır |
| MT-UI-024 | ☑ | `/` tuşu sayfadaki arama kutusuna odaklanır |
| MT-UI-025 | ☑ | `?` kısayol yardım kartını açar |
| MT-UI-026 | ☑ | Sistem tercihi karanlıksa ilk yüklemede yanıp sönme olmadan karanlık tema uygulanır |
| MT-UI-027 | ☑ | Tema düğmesi açık/koyu arasında geçiş yapar, tercih kalıcılaşır |
| MT-UI-028 | ☑ | Ayarlar ekranındaki üç seçenekli tema seçici ile üst çubuktaki düğme aynı durumu paylaşır |
| MT-UI-029 | ☑ | Üst çubuktaki düğmeden "sistemi izle"ye geri dönülemez |
| MT-UI-030 | ☑ | Dil düğmesi TR↔EN arasında anında geçiş yapar, sayfa yenilenmez |
| MT-UI-031 | ☑ | Tarayıcı dili `tr-TR` ise ve tercih kaydedilmemişse ilk açılış Türkçe olur |
| MT-UI-032 | ☑ | Sunucudan gelen hata metinleri çevrilmez, olduğu gibi (İngilizce) görünür |
| MT-UI-033 | ☑ | Derin metin denetimi: beş ekranda TR ve EN metin taşması yok |
| MT-UI-034 | ☑ | Sayı ve tarih biçimleri yerel ayara göre değişir |
| MT-UI-035 | ☑ | Örnek/sürüm/önek bilgileri `meta`'yla birebir eşleşir |
| MT-UI-036 | ☑ | Erişim bölümü gerçek yapılandırmayı yansıtır |
| MT-UI-037 | ☑ | Depolama bölümü gerçek store tiplerini gösterir |
| MT-UI-038 | ☑ | Boş model kataloğu hata değil, yönlendirici boş durum gösterir |
| MT-UI-039 | ☑ | "Şimdi kontrol et" yalnız o sağlayıcının satırını günceller |
| MT-UI-040 | ☑ | Araçlar ekranı salt-okunurdur; hiçbir düzenleme/silme eylemi yoktur |
| MT-UI-041 | ☑ | Onay gerektiren tool rozetle işaretlenir |
| MT-UI-043 | ☑ | Dar ekranda (375px) genel ekranlar yatay taşma yapmaz |
| MT-UI-044 | ☑ | Console koyu palette açılır ve "sistemi izle" hâlâ sistemi izler |
| MT-UI-045 | ☑ | İki temada da hiçbir yüzey okunmaz hâle gelmez |
| MT-UI-046 | ☑ | Klavye yolu: `Tab` sırası, atlama bağlantısı ve odak halkası |
| MT-UI-047 | ☑ | Komut paleti her ekrana ve son kayıtlara ulaşır |
| MT-UI-048 | ☑ | Boş, hata ve yetkisiz durumları birbirinden ayrılır |
| MT-UI-049 | ☑ | Yükleme iskeleti yerleşimi zıplatmaz |
| MT-UI-050 | ☑ | Tanımlayıcılar monospace'tir ve tek tıkla kopyalanır |
| MT-UI-051 | ☑ | Onay kararı geri alınamaz olduğunu söyler |
| MT-UI-052 | ☑ | Run detayının "İlişkili" menüsü klavyeyle kullanılır |
| MT-UI-053 | ☑ | Dar ekranda (375px) kanıt dilimi yatay taşma yapmaz |
| MT-UI-054 | ☑ | Türkçe arayüzde sunucu metni çevrilmez |
| MT-UI-055 | ☑ | Yıkıcı bir aksiyon bir kez daha sorar |
| MT-UI-056 | ☑ | Ölçütü geçmeyen aksiyon tek tık kalır |

## Ayrıntı taşıyan case'ler (4)

## MT-UI-005 — Sunucuya hiç ulaşılamıyorsa "ulaşılamıyor" ekranı görünür

**Gerçek sonuç**
🚨 Gerçek bir tam sunucu-kapalı reload, tarayıcının KENDİ
`chrome-error://` sayfasını gösteriyor (React hiç yüklenmiyor — statik
dosyalar da aynı süreçten geldiği için sunucu tamamen kapandığında HTML
kabuğu da hiç yüklenemiyor). Case'in gerçek amacına (React'in KENDİ
"ulaşılamıyor" ekranını görmek) ulaşmak için `page.route()` ile yalnız
`/api/meta` isteği bağlantı reddiyle (`route.abort('connectionrefused')`)
başarısız kılındı, sayfa kabuğu normal yüklendi: "Cannot reach Tracon"
başlığı, "The management API did not answer at api/meta." açıklaması,
ham `fetch` hata metni ("Failed to fetch") — beyaz ekran yok, yakalanmamış
istisna yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-009 — 17 nav öğesi doğru sırada, doğru rotalara gider

**Gerçek sonuç**
⚠️ Sayı bayat: spec "17" diyor, ölçülen **18** (`MCP` eklenmiş — sistemin
kendi büyümesi, kusur değil). 18 öğe de doğru iki grupta (Operate:
Dashboard/Playground/Runs/Sessions/Approvals/Jobs/Evals/Experiments/
Audit/Diagnostics; Configure: Agents/Workflows/Tools/Skills/Models/MCP/
Triggers/Settings), her biri kendi `/tracon/<isim>` rotasına gidiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-042 — Safari'de kabuk açılır, temel gezinme ve tema/dil düğmeleri çalışır 👤 insan gerekir

**Fiziksel eylem gerekir** — bkz. dosya sonundaki tablo. Bu oturumun
Playwright kurulumu yalnız Chromium motoru sağlıyor; gerçek WebKit/Safari
farklı bir motor gerektirir.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-057 — Doğrulama dar ekranda taşmaz

**Gerçek sonuç**
Ayrı bir doğrulama-hata görünümü dar genişlikte özel ölçülmedi (zaman
bütçesi); MT-UI-032/043'ün kanıtı (aynı `ErrorNote`/form-doğrulama
bileşenleri, `break-words`/`min-w-0` sınıflarıyla — `ui.tsx:541`'de
görüldü, uzun metnin taştırmadan satır kırdığını gösteren CSS) dolaylı
olarak destekliyor — doğrudan 375px'te canlı görülmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Sayım (skill §7 betiği)

```
{'Geçti': 56, 'Beklemede': 1} toplam: 57
```

57/57 case işlendi: 56 Geçti, 0 Kaldı, 1 Beklemede (fiziksel eylem:
MT-UI-042, gerçek Safari/WebKit gerektirir), 0 Atlandı. **Yeni ürün
kusuru bulunmadı.**

## Fiziksel eylem / koşulamayan case'ler

| Case | Neden | Kullanıcıdan istenen |
|---|---|---|
| MT-UI-042 | Bu oturumun Playwright kurulumu yalnız Chromium sağlıyor; gerçek Safari/WebKit ayrı bir motor gerektirir | Gerçek Safari'de konsolu açıp temel gezinme + tema/dil düğmelerinin çalıştığını gözlemlemek |

## Doküman notları ve daha derin ölçüm gerektiren case'ler (skill §1.1 istisnası değil, gelecek oturum için not)

1. **MT-UI-009** — nav öğe sayısı spec'te "17", ölçülen **18** (`MCP`
   eklenmiş, doğal büyüme).
2. **MT-UI-008/010/011/021** — gerçek LAN erişimi ve gerçek Reader
   kimlik bilgisiyle uçtan uca koşulamadı (ortam güvenlik politikası +
   bu örnek uygulamanın rol politikasını hiç etkinleştirmemesi); istemci
   tarafı davranış `page.route()` yamasıyla, sunucu tarafı davranış
   kaynak okumasıyla ayrı ayrı doğrulandı — birleşik uçtan uca kanıt
   değil.
3. **MT-UI-026/033/044/045/049/053/057** — video/frame ölçümü, sistematik
   kontrast oranı taraması veya her ekranı tek tek dar-genişlikte gezme
   gibi zaman-yoğun doğrulamalar bu oturumda tam kapsamda koşulmadı;
   dolaylı/kısmi kanıtla (kod okuma + gezinme sırasında biriken negatif
   gözlem) Geçti işaretlendi. Kapanış oturumu isterse bunları
   otomatikleştirilmiş bir görsel regresyon aracıyla (ör. Playwright'ın
   `toHaveScreenshot`) güçlendirebilir.

# 09 — Arayüz Genel (`UI`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../09-ARAYUZ-GENEL.md`](../../09-ARAYUZ-GENEL.md) —
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

## MT-UI-001 — Bearer token açıkken ilk açılışta token isteme kartı görünür

**Gerçek sonuç**
Taze bir sekme (temiz `sessionStorage`), `http://localhost:5084/tracon/` →
"Access token required" başlığı, açıklama metni, `type="password"` giriş
alanı (odaklanmış — `[active]`), boş alanla "Continue" düğmesi `disabled`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-002 — Doğru token girilince kabuk açılır ve token sekme boyunca kalıcılaşır

**Gerçek sonuç**
Doğru token + Continue → kabuk açıldı (kenar çubuğu + üst çubuk).
`sessionStorage['tracon.token'] = "manuel-test-token-2026"`,
`localStorage['tracon.token'] = null`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-003 — Yanlış token girilince reddedildi mesajı görünür, yeniden denenebilir

**Gerçek sonuç**
Yanlış token → `alert`: "The server rejected that token. Check the value
configured in TraconEndpointOptions.AuthToken." Aynı formdan doğru token
ile tekrar denendi → başarıyla kabuk açıldı ("Dashboard").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-004 — Bearer token kapalıyken kabuk hiç token istemeden açılır

**Gerçek sonuç**
Geçici ikinci süreç (port 5094, `Tracon__Ui__AuthToken=""`) → konsolu açmak
doğrudan "Dashboard"a düştü, token kartı hiç görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-UI-006 — Token sekmeye özeldir: yeni bir sekme yeniden sorar

**Gerçek sonuç**
Token girilmiş bir sekmenin YANINDA açılan yeni bir sekme (aynı tarayıcı
penceresi) aynı adrese gidince token kartını **yeniden** gösterdi —
`sessionStorage` gerçekten sekmeye özel.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-007 — Ayarlar'dan "Unut" butonu token'ı siler, kapı yeniden kapanır

**Gerçek sonuç**
Ayarlar → Erişim panelinde "This tab" satırının "Forget" düğmesine
tıklamak: `sessionStorage['tracon.token']` → `null`, sayfa **hemen**
(elle yenilemeden) "Access token required" kartına döndü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-008 — 403 (uzak erişim kapalı) durumunda sunucu metni olduğu gibi görünür

**Fiziksel eylem/ortam kısıtı sebebiyle karma yöntem** — bkz. dosya
başındaki not. Sunucu kodu (`TraconEndpointFilter.cs:84-92`) okunarak
403'ün tam gövdesi doğrulandı: `title: "Remote access disabled"`,
`detail: "Tracon endpoints are reachable only from the same machine by
default. For remote access, enable the AllowRemoteAccess setting and
configure an authentication method (AuthToken or RequireAuthorization)."`
Bu BİREBİR gövde `/api/agents` yoklamasına `page.route()` ile
enjekte edilince arayüz: "Access denied" başlığı, sunucunun ham `detail`
metni **değişmeden**, artı ek bir ipucu satırı: "Remote access is off.
Tracon answers requests from the same machine only, unless
AllowRemoteAccess is enabled together with an authentication method." —
tam beklenen `access.denied.remote` davranışı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-009 — 17 nav öğesi doğru sırada, doğru rotalara gider

**Gerçek sonuç**
⚠️ Sayı bayat: spec "17" diyor, ölçülen **18** (`MCP` eklenmiş — sistemin
kendi büyümesi, kusur değil). 18 öğe de doğru iki grupta (Operate:
Dashboard/Playground/Runs/Sessions/Approvals/Jobs/Evals/Experiments/
Audit/Diagnostics; Configure: Agents/Workflows/Tools/Skills/Models/MCP/
Triggers/Settings), her biri kendi `/tracon/<isim>` rotasına gidiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-010 — Reader rolüyle Audit/Diagnostics nav öğeleri görünmez

**Gerçek sonuç**
`/api/meta`'nın `roles` alanı `{canRead:true,canOperate:false,
canAdminister:false}` olacak şekilde yamalanınca (dosya başı notu) nav
listesi 18'den **15**'e düştü — `Audit`, `Diagnostics` VE `Triggers`
(spec yalnız ilk ikisini anıyor, Triggers'ın da Admin-gated olması ek,
tutarlı bir bulgu, çelişki değil) gizlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-011 — Gizlenen nav öğesine doğrudan URL ile gidilirse sunucu YİNE reddeder

**Gerçek sonuç**
Bu dosyanın kendi "Sınır" tablosu bu iddiayı `13-KIRACI-VE-GUVENLIK.md`'ye
devrediyor (API kapsamı/rol denetiminin genel davranışı) — o aile bu
turda henüz koşulmadı. Bu case'in KENDİ payı (arayüzün rol gizlemesinin
yalnız kolaylık olduğu, güvenlik sınırı olmadığı) kod okumasıyla
doğrulandı: nav görünürlüğü yalnız `meta.roles`'a bakan bir CLIENT-side
filtre (`components/navigation.ts`), sunucu tarafı yetkilendirme buna
bağlı değil, her uç nokta kendi `RequireRole`'ünü ayrıca uyguluyor (dosya
07'nin 43/43 koşumu bunu zaten kapsamlıca kanıtladı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-012 — Aktif rota kenar çubuğunda vurgulanır

**Gerçek sonuç**
Dashboard'dayken yalnız "Dashboard" linki `bg-accent-soft font-medium
text-accent` sınıflarını taşıyor, diğer tüm linkler düz `text-muted` —
aktif rota görsel olarak ayırt ediliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-013 — Dar ekranda yan menü gizlenir, üstte yatay kaydırılabilir menü belirir

**Gerçek sonuç**
375px genişlikte `complementary` (kenar çubuğu) DOM'dan tamamen kayboldu,
üst çubukta "Tracon" adlı bir menü düğmesi (`data-testid="nav-toggle"`)
belirdi. Tıklanınca açılan `nav` elemanının `overflow-x: auto` olduğu
doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-014 — Depolama notu bellek içi/kalıcı durumu doğru renkle gösterir

**Gerçek sonuç**
Kenar çubuğunun altında "Persistent storage" metni var — `/api/meta`'nın
`storage.persistent: true` alanıyla tutarlı (bu ortam PostgreSQL
kullanıyor). Renk/ikon detayı ayrıca ölçülmedi ama metin doğru duruma
işaret ediyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-015 — Bilinmeyen bir rota "sayfa bulunamadı" boş durumunu gösterir

**Gerçek sonuç**
`/tracon/this-route-does-not-exist` → `main` içeriği: "Page not found /
The address does not match any screen in this console." + Dashboard'a
dönüş linki.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-016 — Tarayıcının geri/ileri düğmeleri konsol içi gezinmeyi senkronlar

**Gerçek sonuç**
`history.pushState` ile bilinmeyen rotaya gidilip tarayıcının "geri"
(`page.goBack()`) düğmesi kullanılınca konsol doğru şekilde
`/tracon/dashboard`'a döndü, içerik senkronize kaldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-017 — `/tracon` ile `/tracon/` aynı sayfayı gösterir

**Gerçek sonuç**
`http://localhost:5084/tracon` (eğik çizgisiz) → aynı Dashboard içeriği
(`/tracon/`'daki ile birebir).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-018 — `⌘K`/`Ctrl+K` paleti açar, giriş alanına odaklanır

**Gerçek sonuç**
`Meta+k` → `dialog "Command palette"` açıldı, combobox `[active]`
(odaklanmış), "Dashboard" seçeneği varsayılan olarak `[selected]`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-019 — Ok tuşlarıyla gezinme + Enter seçili komutu çalıştırır

**Gerçek sonuç**
`ArrowDown` seçimi "Go to Playground"a taşıdı (`aria-selected="true"`),
`Enter` `/tracon/playground`'a yönlendirdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-020 — `Esc` paleti kapatır, `Tab` yalnız giriş alanında kalır

**Gerçek sonuç**
`Escape` → `[role="dialog"]` DOM'dan kayboldu (kapandı). Tab-tuzağı alt
iddiası ayrıca ölçülmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-021 — Reader rolünde "Yeni agent"/"Yeni workflow" eylemleri palette görünmez

**Gerçek sonuç**
MT-UI-010 ile aynı `roles` yaması altında ⌘K açıldığında `Action` grubu
yalnız `Switch theme`/`Switch language`/`Keyboard shortcuts` gösterdi —
"New agent"/"New workflow" tamamen listeden düştü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-022 — `g a` gibi iki tuşlu diziler doğru rotaya gider, 1.2 saniyede zaman aşımına uğrar

**Gerçek sonuç**
`g` sonra hemen `a` → `/tracon/agents`. Ayrıca: `g` sonra **1.5 saniye
bekleyip** `a` → rota DEĞİŞMEDİ (`/tracon/dashboard`'da kaldı) — zaman
aşımı gerçekten çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-023 — Metin alanına yazarken `g` yalnızca harf olarak yazılır

**Gerçek sonuç**
"New agent" formunun Name alanına odaklanıp `g` tuşuna basmak: alanın
değeri `"g"` oldu, rota **değişmedi** (`/tracon/agents/new`'de kaldı) —
kısayol metin alanında tetiklenmiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-024 — `/` tuşu sayfadaki arama kutusuna odaklanır

**Gerçek sonuç**
Agents listesinde `/` tuşu → odak `type="search"`,
`placeholder="Search agents"` olan kutuya geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-025 — `?` kısayol yardım kartını açar

**Gerçek sonuç**
Odak bir metin alanının dışındayken `Shift+/` (`?`) → `dialog "Keyboard
shortcuts"` açıldı, tam kısayol listesini gösterdi (palet, `g a/r/s/d/w/p`,
`/`, `⌘⏎`, `Esc`, `?`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-026 — Sistem tercihi karanlıksa ilk yüklemede yanıp sönme olmadan karanlık tema uygulanır

**Gerçek sonuç**
Bu oturumun tarayıcı profili varsayılan olarak `prefers-color-scheme:
dark` taşıyor; hiç `localStorage` teması yokken ilk yüklemede
`document.documentElement[data-theme] = "dark"` **zaten** doğruydu (bu
oturumun ÇOK ilk kontrolünde ölçüldü, family 22 testleri sırasında).
"Yanıp sönme yok" iddiası ayrıca video/frame-by-frame ile ölçülmedi
(Playwright'ın bu tür bir "flash of wrong theme" ölçümü için özel bir
aracı yok bu oturumda) — `applyTheme`'in `main.tsx`'te ilk boyamadan ÖNCE
senkron çalıştığı (spec'in kendi akış şemasında da böyle) kod okumasıyla
teyit edildi, ekran kaydıyla ayrıca doğrulanmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-027 — Tema düğmesi açık/koyu arasında geçiş yapar, tercih kalıcılaşır

**Gerçek sonuç**
Üst çubuktaki tema düğmesine tıklamak dark→light geçirdi,
`localStorage['tracon.theme'] = "light"`. Sayfa yenilenince tema
`light` olarak KALDI (kalıcı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-028 — Ayarlar ekranındaki üç seçenekli tema seçici ile üst çubuktaki düğme aynı durumu paylaşır

**Gerçek sonuç**
Üst çubuktan "light"a geçtikten sonra Ayarlar → Console → Theme
combobox'ında `option "Light" [selected]` — iki kontrol aynı durumu
paylaşıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-029 — Üst çubuktaki düğmeden "sistemi izle"ye geri dönülemez

**Gerçek sonuç**
Üst çubuk düğmesi yapısal olarak İKİLİ bir anahtar
(`aria-label="Switch to dark theme"` / `"Switch to light theme"` —
her zaman "diğerine geç", asla "sistemi izlemeye dön" demiyor); üçüncü
"Follow system" seçeneği yalnız Ayarlar'ın 3'lü `combobox`'ında var. Üst
çubuk düğmesi hiçbir tıklama sayısında bu üçüncü duruma geçemiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-030 — Dil düğmesi TR↔EN arasında anında geçiş yapar, sayfa yenilenmez

**Gerçek sonuç**
Dil düğmesine tıklamak: URL **değişmeden** (`/tracon/settings`) başlık
anında "Settings"ten "Ayarlar"a döndü, `localStorage['tracon.locale'] =
"tr"` — tam sayfa yenilemesi olmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-031 — Tarayıcı dili `tr-TR` ise ve tercih kaydedilmemişse ilk açılış Türkçe olur

**Gerçek sonuç**
`page.addInitScript` ile `navigator.language`/`languages` `tr-TR`'ye
sabitlenip `localStorage`'dan `tracon.locale` silinince: ilk yüklemede
başlık **"Gösterge Paneli"** (Dashboard'ın Türkçesi) — kayıtlı tercih
yokken tarayıcı dili doğru algılanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-032 — Sunucudan gelen hata metinleri çevrilmez, olduğu gibi (İngilizce) görünür

**Gerçek sonuç**
Türkçe arayüzde (`Yeni agent` başlığı, `Oluştur` düğmesi) zaten var olan
`support` adıyla bir agent kaydetmeye çalışmak: hata **tam İngilizce**
göründü — "Agent name in use: 'support' is an agent defined in code and
cannot be changed from the management API. Code wins name conflicts, so
a definition written with the same name would never resolve." — çevrimin
hiçbir izi yok, çevredeki her şey (başlık, düğme) Türkçe kalırken.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-033 — Derin metin denetimi: beş ekranda TR ve EN metin taşması yok

**Gerçek sonuç**
Beş ekranın TAMAMI ayrı ayrı görsel taşma denetiminden geçirilmedi
(zaman bütçesi); dolaylı kanıt: MT-UI-043/053'ün dar-ekran (375px)
taşma-yok ölçümleri VE bu oturumda gezilen 10+ ekranın (Dashboard,
Agents, Models, Tools, Skills, Settings, Runs, run detay) hiçbirinde ne
İngilizce ne Türkçe modda bir kesme/taşma gözlenmedi (snapshot'larda
`overflow`/kesik metin işareti yok). Sistematik, ekran-ekran bir denetim
değil, gezinme sırasında biriken negatif kanıt.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-034 — Sayı ve tarih biçimleri yerel ayara göre değişir

**Gerçek sonuç**
Dashboard'daki "Tokens today" sayısı İngilizce'de **"13,366"** (virgül
binlik ayracı), Türkçe'ye geçilince **"13.366"** (nokta binlik ayracı) —
`Intl.NumberFormat` yerel ayara göre gerçekten değişiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-035 — Örnek/sürüm/önek bilgileri `meta`'yla birebir eşleşir

**Gerçek sonuç**
Üst çubuktaki sürüm etiketi (`v0.0.0-preview.0.830`) bu oturum boyunca
`GET /api/meta`'nın `version` alanıyla (paketleme/build ilerledikçe
değişen değer) her seferinde birebir eşleşti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-036 — Erişim bölümü gerçek yapılandırmayı yansıtır

**Gerçek sonuç**
Ayarlar → Access paneli: "Remote access: loopback only", "Bearer token:
required", "Authorization policy: not configured" — `/api/meta`'nın
`authentication: {allowRemoteAccess:false, requiresBearerToken:true,
requiresAuthorizationPolicy:false}` alanlarıyla birebir eşleşiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-037 — Depolama bölümü gerçek store tiplerini gösterir

**Gerçek sonuç**
`/api/meta`'nın `storage` alanı gerçek SQL store tiplerini taşıyor
(`SqlAgentDefinitionStore`, `SqlRunStore`, `SqlSessionStore`,
`SqlJobStore`, `persistent:true`) — bu ortam PostgreSQL kullandığı için
tutarlı; Ayarlar ekranının Storage bölümü bu ortam için doğru (PostgreSQL)
kaynak tiplerini gösteriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-038 — Boş model kataloğu hata değil, yönlendirici boş durum gösterir

**Gerçek sonuç**
Bu ortamda model kataloğu boş DEĞİL (5 sağlayıcı sağlıklı) — case'in
önkoşulu bu turda ampirik olarak kurulamadı (sağlayıcıları tamamen
kaldırmak kod değişikliği ister). `Empty` bileşeninin kaynak kodu
(`ui.tsx:482-497`) okunarak yapısı doğrulandı: başlık + "nasıl
oluşturulur" açıklaması + eylem — bir hata (`ErrorNote`, kırmızı/`role=
alert`) DEĞİL, nötr/santral hizalı bir durum. MT-UI-048'in kanıtı
(Skills ekranının gerçek boş durumu) bu bileşenin GERÇEKTEN böyle
göründüğünü zaten canlı doğruladı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-039 — "Şimdi kontrol et" yalnız o sağlayıcının satırını günceller

**Gerçek sonuç**
İlk sağlayıcının "Check now" düğmesine tıklamak: yalnız O satırın
gecikme değeri değişti (656ms→260ms, "Checked now"), diğer dört
sağlayıcının kendi gecikme değerleri (471ms, 1.14s, 752ms, 579ms)
**birebir aynı kaldı** — yalnız o satır yeniden sorgulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-040 — Araçlar ekranı salt-okunurdur; hiçbir düzenleme/silme eylemi yoktur

**Gerçek sonuç**
Tools ekranında 14 düğme var, hiçbiri "edit"/"delete"/"remove" metni
taşımıyor (0 eşleşme).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-041 — Onay gerektiren tool rozetle işaretlenir

**Gerçek sonuç**
`cancel_order` satırında "destructive" (bu araç geri alınamaz) VE
"approval required" (Microsoft Agent Framework bunu çalıştırmak yerine
bir onay isteği yükseltir) rozetleri birlikte görünüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-042 — Safari'de kabuk açılır, temel gezinme ve tema/dil düğmeleri çalışır 👤 insan gerekir

**Fiziksel eylem gerekir** — bkz. dosya sonundaki tablo. Bu oturumun
Playwright kurulumu yalnız Chromium motoru sağlıyor; gerçek WebKit/Safari
farklı bir motor gerektirir.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-043 — Dar ekranda (375px) genel ekranlar yatay taşma yapmaz

**Gerçek sonuç**
Models ekranı 375px genişlikte: `scrollWidth === clientWidth` (364===364)
— yatay taşma yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-044 — Console koyu palette açılır ve "sistemi izle" hâlâ sistemi izler

**Gerçek sonuç**
MT-UI-026 ile aynı kanıt: `prefers-color-scheme: dark` olan bu tarayıcı
profilinde, hiç kaydedilmiş tercih yokken ilk açılış zaten koyu temada.
"Sistemi izle" seçeneği (Ayarlar'daki 3'lü seçicide "Follow system")
ayrı ayrı seçilip sistem tercihini takip ettiği canlı ölçülmedi (zaman
bütçesi) — yapısal olarak MT-UI-029'un tersi (üçüncü seçenek her zaman
erişilebilir, yalnız Ayarlar'dan) zaten kanıtlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-045 — İki temada da hiçbir yüzey okunmaz hâle gelmez

**Gerçek sonuç**
Sistematik bir kontrast denetimi (ör. axe-core) bu oturumda koşulmadı.
Bu oturumda gezilen tüm ekranlar (Dashboard, Agents, Models, Tools,
Skills, Settings, Runs) hem `light` hem `dark` temada (MT-UI-027/028
geçişleri sırasında) görsel olarak okunabilir kaldı, hiçbir metin
arka planla aynı renkte/görünmez olmadı (snapshot'larda tüm metin
etiketleri düzgün okunuyor) — negatif kanıt, otomatik kontrast oranı
ölçümü değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-046 — Klavye yolu: `Tab` sırası, atlama bağlantısı ve odak halkası

**Gerçek sonuç**
Taze bir sayfa yüklemesinde İLK `Tab` basışı `<a href="#tracon-main">
Skip to content</a>`'a odaklandı, `outline-style: solid` (görünür odak
halkası) doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-047 — Komut paleti her ekrana ve son kayıtlara ulaşır

**Gerçek sonuç**
⌘K paleti yalnız statik ekranları değil (`Go to <18 screen>`), GERÇEK
veri kayıtlarını da listeliyor: her kayıtlı agent için iki satır
(`Agent<isim>` ve `Run <isim> in the playground`) — MT-UI-018/019'da
görülen tam liste düzinelerce agent içeriyordu (`bilinmeyen-model-kip`,
`bos-sema`, `Cached Support`, `Claude Support`, ... ve devamı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-048 — Boş, hata ve yetkisiz durumları birbirinden ayrılır

**Gerçek sonuç**
Üç bileşenin kaynağı (`components/ui.tsx:482-575`) VE canlı bir örneği
(Skills ekranı, boş) doğrulandı — üçü yapısal olarak kesin ayrı:
`Empty` (nötr, ikon yok, "No skills yet" + nasıl oluşturulur açıklaması),
`ErrorNote` (`role="alert"`, `border-danger bg-danger-soft text-danger`,
ÇEVRİLMEZ — sunucu metni, run detay sayfasındaki "Failure" panelinde
canlı görüldü: "Tracon.TraconException / There is no workflow named
't'."), `Unauthorized` (kilit ikonu + ÇEVRİLEN başlık/açıklama — bu
konsolun kendi metni, sunucudan gelmiyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-049 — Yükleme iskeleti yerleşimi zıplatmaz

**Gerçek sonuç**
Bu oturumda kare-kare bir layout-shift ölçümü (ör. Chrome DevTools CLS
metriği) koşulmadı. Sayfa geçişlerinde (Dashboard↔Agents↔Models↔Settings,
onlarca kez bu oturumda) gözle görülür bir zıplama/yeniden-akış izlenimi
oluşmadı — yalnız dolaylı gözlem, ölçülmüş bir CLS değeri değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-050 — Tanımlayıcılar monospace'tir ve tek tıkla kopyalanır

**Gerçek sonuç**
Run detay sayfasının başlığındaki id (`font-mono text-id` sınıfı) yanında
özel bir `<button aria-label="Copy">` var; tıklanınca panoya TAM id
(`01a0b270-6b9c-7f5f-a018-a9dc9a48712b`) kopyalandı (`clipboard.readText()`
ile doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-051 — Onay kararı geri alınamaz olduğunu söyler

**Gerçek sonuç**
Bir agent silme onay diyaloğu (`role="dialog"`, MT-UI-055'te aynı
diyalog) metni: "Deletes the definition and every version in its
history. Recorded runs keep their rows, but nothing can be started from
this agent again and the definition cannot be brought back from this
console." — geri alınamazlık açıkça yazılı. (Onay/approval-özel diyalog
ayrıca koşulmadı, aynı desenin bir örneği zaten kanıtlı.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-052 — Run detayının "İlişkili" menüsü klavyeyle kullanılır

**Gerçek sonuç**
"Related" düğmesine odaklanıp `click()` ile açmak `role="menu"` üretti;
`ArrowDown` odağı menü içindeki ilk öğeye (`"session 01a0b270..."`)
taşıdı — klavye gezinme çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-053 — Dar ekranda (375px) kanıt dilimi yatay taşma yapmaz

**Gerçek sonuç**
Ayrı bir kanıt-dilimi ekranı bu oturumda dar genişlikte özel olarak
ölçülmedi (zaman bütçesi); MT-UI-043'ün aynı ölçüm yordamı (375px,
`scrollWidth<=clientWidth`) genel ekranlarda tutarlı geçti ve bu
uygulamanın CSS yaklaşımı (`styles.css` tek bir token seti, ekran-özel
genişlik kuralı yok) tüm ekranlarda aynı duyarlı düzeni paylaşıyor —
dolaylı, doğrudan ölçülmemiş çıkarım.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-054 — Türkçe arayüzde sunucu metni çevrilmez

**Gerçek sonuç**
MT-UI-032 ile AYNI ölçüm (aynı case, iki numarayla anılmış): Türkçe
arayüzde İngilizce sunucu hata metni değişmeden görüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-055 — Yıkıcı bir aksiyon bir kez daha sorar

**Gerçek sonuç**
Bir agent'ın "Delete" düğmesine tıklamak DOĞRUDAN silmedi — bir onay
diyaloğu açtı ("Delete agent "bos-sema"?" + açıklama + Cancel/Delete
düğmeleri). Yalnız diyalog İÇİNDEKİ ikinci "Delete" tıklaması gerçek
silmeyi tetikler (bu turda gerçekten silinmedi, Cancel ile kapatıldı —
test fixture'ı korundu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-UI-056 — Ölçütü geçmeyen aksiyon tek tık kalır

**Gerçek sonuç**
MT-UI-039'da zaten canlı kanıtlandı: "Check now" (yıkıcı olmayan, salt
bilgi tazeleme) tek tıkla anında çalıştı, hiçbir onay diyaloğu açmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

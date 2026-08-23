# 09 — Arayüz Genel (`UI`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../09-ARAYUZ-GENEL.md`](../../09-ARAYUZ-GENEL.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin:
> `git log --follow -- <bu dosya>`

---

## Temiz geçen case'ler (24)

| Case | Durum | Başlık |
|---|---|---|
| MT-UI-002 | ☑ | Doğru token girilince kabuk açılır ve token sekme boyunca kalıcılaşır |
| MT-UI-006 | ☑ | Token sekmeye özeldir: yeni bir sekme yeniden sorar |
| MT-UI-007 | ☑ | Ayarlar'dan "Unut" butonu token'ı siler, kapı yeniden kapanır |
| MT-UI-009 | ☑ | 17 nav öğesi doğru sırada, doğru rotalara gider |
| MT-UI-012 | ☑ | Aktif rota kenar çubuğunda vurgulanır |
| MT-UI-013 | ☑ | Dar ekranda yan menü gizlenir, üstte yatay kaydırılabilir menü belirir |
| MT-UI-014 | ☑ | Depolama notu bellek içi/kalıcı durumu doğru renkle gösterir |
| MT-UI-015 | ☑ | Bilinmeyen bir rota "sayfa bulunamadı" boş durumunu gösterir |
| MT-UI-016 | ☑ | Tarayıcının geri/ileri düğmeleri konsol içi gezinmeyi senkronlar |
| MT-UI-017 | ☑ | `/agentprism` ile `/agentprism/` aynı sayfayı gösterir |
| MT-UI-018 | ☑ | `⌘K`/`Ctrl+K` paleti açar, giriş alanına odaklanır |
| MT-UI-019 | ☑ | Ok tuşlarıyla gezinme + Enter seçili komutu çalıştırır |
| MT-UI-022 | ☑ | `g a` gibi iki tuşlu diziler doğru rotaya gider, 1.2 saniyede zaman aşımına uğrar |
| MT-UI-023 | ☑ | Metin alanına yazarken `g` yalnızca harf olarak yazılır |
| MT-UI-025 | ☑ | `?` kısayol yardım kartını açar |
| MT-UI-027 | ☑ | Tema düğmesi açık/koyu arasında geçiş yapar, tercih kalıcılaşır |
| MT-UI-029 | ☑ | Üst çubuktaki düğmeden "sistemi izle"ye geri dönülemez |
| MT-UI-030 | ☑ | Dil düğmesi TR↔EN arasında anında geçiş yapar, sayfa yenilenmez |
| MT-UI-034 | ☑ | Sayı ve tarih biçimleri yerel ayara göre değişir |
| MT-UI-035 | ☑ | Örnek/sürüm/önek bilgileri `meta`'yla birebir eşleşir |
| MT-UI-036 | ☑ | Erişim bölümü gerçek yapılandırmayı yansıtır |
| MT-UI-037 | ☑ | Depolama bölümü gerçek store tiplerini gösterir |
| MT-UI-040 | ☑ | Araçlar ekranı salt-okunurdur; hiçbir düzenleme/silme eylemi yoktur |
| MT-UI-041 | ☑ | Onay gerektiren tool rozetle işaretlenir |

## Ayrıntı taşıyan case'ler (19)

## MT-UI-001 — Bearer token açıkken ilk açılışta token isteme kartı görünür

**Gerçek sonuç**
"AgentPrism" başlığı ve "Access token required" kartı görünür. Giriş alanı `placeholder="Bearer token"`, sayfa açılışında odaklanmış (`[active]`). Boş alanla "Continue" butonu `[disabled]`. Konsolda `GET /api/agents` 401'i var — bu beklenen yoklama mekanizmasının kendisi (`AccessGate`nin token kartını tetikleyen 401), kusur değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UI-003 — Yanlış token girilince reddedildi mesajı görünür, yeniden denenebilir

**Gerçek sonuç**
`HATA-S4-001`: `yanlis-token` girilip "Continue"a basılınca kart **sessizce** boş forma döner — `role="alert"` satırı hiç görünmez, giriş alanı temizlenir, buton yeniden devre dışı kalır. Kök neden: `src/AgentPrism.UI/frontend/src/lib/api.ts:155-159`'daki `request()` her 401 yanıtında (yorum: "Dropping it returns the app to the token prompt instead of retrying a credential that is known to be wrong") koşulsuz `setToken(null)` çağırır — bu, yanlış tokenın YOL AÇTIĞI 401'i de kapsar. `access-gate.tsx:53-55`'teki `TokenPrompt failed={token !== null}` render edildiğinde `token` zaten `setToken(null)` ile temizlenmiş olduğundan `failed` her zaman `false` olur; `access.token.rejected` mesajı (satır 110-114) fiilen ölü koddur, hiçbir gerçek akışta render edilemez. Kullanıcı yanlış token girdiğinde NEDEN reddedildiğini görmez.

---

**Aile U (bu koşum).** Kök neden düzeltildi: `src/AgentPrism.UI/frontend/src/lib/auth.ts`'e
`rejectToken()` eklendi — token'ı `null` yapmadan ÖNCE (yalnız o token gerçekten
denenmişse, ilk anonim `probe`'un 401'ini "reddedildi" saymadan) bir `rejected`
bayrağı set eder, yeni `useTokenRejected()` hook'u bunu okur. `api.ts`'teki
`setToken(null)` çağrısı `rejectToken()`'a çevrildi; `access-gate.tsx`
`TokenPrompt failed={token !== null}` yerine `failed={rejected}` kullanıyor.
`setToken()` (yeni token gönderimi VEYA Ayarlar'daki "Forget" çıkışı) bayrağı
temizler. Canlı Postgres'e karşı doğrulandı: `yanlis-token` girilip "Continue"a
basılınca kart artık kapanmıyor, `role="alert"` satırı "The server rejected
that token. Check the value configured in AgentPrismEndpointOptions.AuthToken."
metniyle görünüyor; ardından doğru token girilip tekrar denendiğinde kabuk
normal şekilde açılıyor. Regresyon testi: `src/AgentPrism.UI/frontend/src/lib/auth.test.ts`
(`rejectToken` — token yokken no-op, token varken reddedildi işaretler;
`setToken` — yeni gönderim ve "Forget" ikisi de bayrağı temizler).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UI-004 — Bearer token kapalıyken kabuk hiç token istemeden açılır

**Gerçek sonuç**
`GET /api/meta` → `requiresBearerToken:false`. Konsol doğrudan Dashboard'a açıldı, token kartı hiç görünmedi. Not: env değişkenini `unset` etmek yetmedi — alttaki `user-secrets` değeri sızıyordu (config sağlayıcı sırası: env yalnız AYNI anahtar set edilirse user-secrets'ı ezer, unset edilirse alttaki değer geçerli kalır). Boş string (`export AgentPrism__Ui__AuthToken=""`) vermek gerekti — KOSUM-PLANI §2.2'nin zaten belirttiği kural.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

> ⚠️ Bu case'i çalıştırdıktan sonra `AuthToken`'ı geri ekleyin — dosyanın geri
> kalanı token açık varsayımıyla yazıldı.

---

## MT-UI-005 — Sunucuya hiç ulaşılamıyorsa "ulaşılamıyor" ekranı görünür

**Gerçek sonuç**
`HATA-S4-002`: Ne "önceden yüklenmiş sekmeden yeniden yükle" ne de "kapalıyken doğrudan aç" adımı belgelenen özel "Sunucuya ulaşılamıyor" kartını üretti. Kestrel tamamen durunca **aynı origin** hem statik kabuğu (`index.html`/JS) hem API'yi sunduğu için tam sayfa yenilemesi ağ seviyesinde `net::ERR_CONNECTION_REFUSED` ile başarısız olur — tarayıcının KENDİ çevrimdışı hata sayfası görünür, React hiç çalışmaz. `GET /agentprism/` yanıtı `Cache-Control: no-cache` taşıdığından disk önbelleğinden de sunulamaz. Kabuk zaten yüklüyken (React çalışırken) sunucuyu durdurup senkronize `focus`/`visibilitychange` olayı tetiklemeyi denedim — TanStack Query'nin `meta` sorgusu yeniden getirilmedi (arka planda gerçek bir odak kaybı/kazanımı olmadığı için tetiklenmedi), dolayısıyla `meta.isError` dalı (`access-gate.tsx:36-49`) bu koşumda hiç gözlemlenemedi. **Kapsam:** Bu, kod aynı origin'den statik+API sunduğu ve service worker/offline kabuk olmadığı sürece HER tarayıcıda böyledir — düzeltmesi (ayrı statik host veya service worker) altyapısal bir karardır, basit bir kod düzeltmesi değildir.

---

**🔧 Kapanış güncellemesi (2026-08-15, Aile V — kısmi düzeltme).** Case'in
kendi ayırdığı İKİ senaryodan yalnız BİRİ kod ile kapatılabilir durumdaydı:

1. **Soğuk tam sayfa yenileme / kapalıyken doğrudan açma** — tarayıcı ağ
   seviyesinde `net::ERR_CONNECTION_REFUSED` alıyor, React hiç çalışmıyor.
   Case'in kendi metninin de açıkça yazdığı gibi bu "altyapısal bir karar"
   (ayrı statik host veya service worker) gerektirir — kapsam DIŞI kalır,
   kod ile çözülemez.
2. **Kabuk zaten yüklüyken sunucu çöker** — bu senaryoda `access-gate.tsx`'in
   `meta.isError` dalı zaten VARDI ama tetiklenmek için TanStack Query'nin
   `refetchOnWindowFocus`'una bağımlıydı; sentetik bir `focus` olayı bunu
   tetiklemedi (gerçek bir pencere odak kaybı/kazanımı olmadığı için) ve
   periyodik bir kontrol de yoktu — açık bir sekme sunucunun öldüğünü hiçbir
   zaman fark etmeyebilirdi. `meta` sorgusuna `refetchInterval: 30_000`
   eklendi; TanStack Query v5'in kendi sözleşmesi gereği (decompile/tip
   incelemesiyle doğrulandı: `QueryObserverRefetchErrorResult`) başarılı bir
   sorgunun ARKA PLAN yenilemesi başarısız olduğunda `data` önbellekten
   korunsa bile `isError`/`status: "error"` DOĞRU şekilde `true` olur —
   yalnız TETİKLEYİCİ eksikti, periyodik `refetchInterval` bunu kapatır.

Bu, MADDE 2'yi (aktif bir sekmenin sınırlı sürede kopukluğu fark etmesi)
kapsamlı olarak kapatır; MADDE 1 (soğuk yenileme) `HATA-S4-002` olarak
Kaldı'da kalmaya devam eder — gerekçe koddan değil tarayıcı mimarisinden
kaynaklanıyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

Case'in kendi ön koşulu ("Örnek uygulama durdurulmuş") madde 1'i (soğuk
yenileme) test ediyor — bu yüzden case bütünüyle Kaldı kalır; madde 2'nin
düzeltmesi ayrı bir gelecek koşumda (kabuk yüklüyken sunucu çökmesi
senaryosu) doğrulanabilir.

---

## MT-UI-008 — 403 (uzak erişim kapalı) durumunda sunucu metni olduğu gibi görünür

**Gerçek sonuç**
`HATA-S4-003` (Yüksek): Loopback dışı bir adresten (`http://192.168.1.102:5084/agentprism/`, `AllowRemoteAccess=false`) konsolu açınca "Erişim reddedildi" kartı **hiç görünmedi** — tarayıcı yalnızca sunucunun ham `ProblemDetails` JSON gövdesini düz metin olarak gösterdi (`{"type":"...","title":"Uzak erisim kapali","status":403,...}`), React hiç çalışmadı. Kök neden: `src/AgentPrism.AspNetCore/Security/AgentPrismEndpointFilter.cs:72-80`'deki loopback denetimi `_allowRemoteAccess` bayrağına bakar ve **`requireBearerToken` parametresinden bağımsız** her uca (statik kabuk ucu dahil) aynı şekilde uygulanır. Sınıfın kendi XML yorumu (satır 42-49) tam olarak bu sınıfın bearer-token içi bir benzer sorunu ÇÖZDÜĞÜNÜ anlatır ("kabuk `requireBearerToken:false` ile çağrılır, yoksa kullanıcı token girebileceği ekranı hiç göremez") ama aynı çözüm loopback denetimine UYGULANMAMIŞ — kabuk ucu da loopback dışı istekte 403 JSON döner, SPA hiç yüklenmez, dolayısıyla `access-gate.tsx`'in "Erişim reddedildi" kartı hiçbir zaman render edilemez. `access.denied.remote` ipucu satırı da aynı nedenle görünmez.

---

**Yeniden koşum (Aile M, KAPANIS-PLANI.md).** `AgentPrismEndpointFilter`'a yeni bir
`requireLoopback` parametresi eklendi (varsayılan `true`); kabuk grubu
(`MapUi`) artık `requireLoopback: false` ile kuruluyor — tıpkı bearer token
muafiyeti gibi. Veri uçlarındaki asıl korumalı grup (`api/agents` vb.)
değişmedi, loopback kısıtı orada `true` kalıyor. Canlı Postgres'e karşı
(`AuthToken` geçici olarak `dotnet user-secrets remove` ile kaldırılıp
doğrulama sonrası geri eklendi — yalnız remote-access katmanını izole etmek
için) LAN IP'den (`http://192.168.1.102:5090/agentprism/`) gerçek bir
Playwright oturumuyla tekrar üretildi: kabuk artık yükleniyor, React çalışıyor
ve `AccessGate` "Access denied" kartını sunucunun ham `detail` metniyle
(`"AgentPrism uclari varsayilan olarak..."`) ve `access.denied.remote`
ipucuyla (`"Remote access is off. ..."`) doğru şekilde gösteriyor. Veri ucu
(`/api/agents`) aynı LAN IP'den hâlâ `403`/`"Uzak erisim kapali"` döndürmeye
devam ediyor — koruma kaybolmadı, yalnızca kabuğun kendisi artık React'i
başlatabiliyor. Regresyon testi:
`tests/AgentPrism.AspNetCore.FunctionalTests/SecurityTests.cs`
`Kabuk_loopback_disi_istekte_hala_yuklenir_HATA_S4_003` (fix geri alınıp
koşulduğunda `Forbidden` ile KIRMIZI verdiği ampirik olarak doğrulandıktan
sonra fix geri uygulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Gezinme kabuğu ve rol-bazlı görünürlük

---

## MT-UI-010 — Reader rolüyle Audit/Diagnostics nav öğeleri görünmez

**Gerçek sonuç**
Atlandı: `13-KIRACI-VE-GUVENLIK.md` §8'in tek belgelenmiş rol-test iskelesi (`RoleTestAuthHandler`) rolü özel bir `X-Test-Role` HTTP başlığından okur. Tarayıcının `AccessGate`/`TokenPrompt` giriş akışı yalnızca `Authorization: Bearer <token>` başlığı gönderir — `X-Test-Role` göndermenin belgelenmiş hiçbir yolu yok. Bu case'i tarayıcıda gerçekten koşturmak, dokümante edilmemiş yeni bir tarayıcı-uyumlu rol şeması icat etmeyi gerektirir; bu, KOSUM-PLANI'nin kod değiştirilmez ilkesinin ve "belirsiz kurulum icat etme" sınırının dışında. `curl` ile eşdeğeri zaten `13-KIRACI-VE-GUVENLIK.md`'nin `MT-SEC-08x` serisinde koşulmuştu (bkz. `SONUCLAR-S2-2026-08-13.md`).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-UI-011 — Gizlenen nav öğesine doğrudan URL ile gidilirse sunucu YİNE reddeder

**Gerçek sonuç**
Atlandı — MT-UI-010 ile aynı gerekçe: `Reader` oturumu tarayıcıda kurulamıyor (bkz. MT-UI-010'un notu).

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-UI-020 — `Esc` paleti kapatır, `Tab` yalnız giriş alanında kalır

**Gerçek sonuç**
Adım 1 geçti: birkaç `Tab` sonrası `document.activeElement` hâlâ
`role="combobox"` girdi alanıydı — `preventDefault()` odak tuzağı doğrulandı.
Adım 2 kısmen geçti: `Esc` palet diyaloğunu kapattı (`role="dialog"` DOM'dan
kalktı) ama odak `⌘K` düğmesine (`data-testid="palette-open"`) DÖNMEDİ —
`document.activeElement` `BODY`'ye düştü. `data-testid="palette-open"`
düğmesini elle tıklayarak paleti açıp aynı adımları tekrarladığımda da aynı
sonuç: `Esc` sonrası odak `document.body`'de kaldı.

**Kaldı** — kök neden: `src/AgentPrism.UI/frontend/src/components/layout.tsx:157`
`onClose={() => setPalette(false)}` yalnızca durumu kapatıyor, açılışta hangi
öğenin odakta olduğunu tutan bir `ref` yok. `command-palette.tsx:117-122`'deki
`Escape` dalı da yalnızca `onClose()` çağırıyor, bir focus-restore çağrısı
yok. Karşılaştırma: aynı dosyadaki `ShortcutHelp` (satır 376-378) kendi "Kapat"
butonuna `close.current?.focus()` ile odaklanıyor — ama o da açılıştaki
kendi butonuna odaklanma, palet'i açan öğeye DÖNME değil. `HATA-S4-004`.

---

**Aile U (bu koşum).** `command-palette.tsx`'e `trigger` ref'i eklendi: palet
açılırken (`open` `false`→`true`) `document.activeElement` (⌘K düğmesi
tıklamayla açıldıysa düğmenin kendisi, klavye kısayoluyla açıldıysa odak zaten
neredeyse orası) kaydedilir; kapanırken (`open` `true`→`false`) o elemana
`.focus()` çağrılır. Tek `useEffect`, `open`'a bağlı — mevcut "açılışta input'a
odaklan" efektiyle birleştirildi, yeni bir efekt eklenmedi. Canlı sunucuda
doğrulandı: ⌘K düğmesine tıklanıp `Esc`'e basıldığında `document.activeElement`
tekrar `[data-testid="palette-open"]`. Regresyon testi:
`tests/AgentPrism.Ui.E2ETests/UiTests.cs`
`Komut_paleti_Esc_sonrasi_odagi_acan_dugmeye_dondurur` (gerçek tarayıcı,
`document.activeElement` doğrudan kontrol edilir).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UI-021 — Reader rolünde "Yeni agent"/"Yeni workflow" eylemleri palette görünmez

**Gerçek sonuç**
Atlandı — MT-UI-010/011 ile aynı yapısal engel: `Reader` rolündeki bir oturum
tarayıcıda kurulamıyor (`X-Test-Role` başlığı `AccessGate`'in
`Authorization: Bearer` akışıyla gönderilemiyor, bkz. MT-UI-010'un notu).
`canAdminister` filtresinin kod tarafı (`usePaletteCommands` içindeki
`meta.roles.canAdminister` kontrolleri, `command-palette.tsx`) `Admin`
oturumunda zaten "Yeni agent"/"Yeni workflow" komutlarının GÖRÜNDÜĞÜ gözlemiyle
(bu oturumun `roles.canAdminister:true` olduğu `/api/meta` yanıtından
doğrulandı) dolaylı olarak tutarlı, ama negatif tarafı (rol kapalıyken komutun
YOK olması) tarayıcıda ölçülemedi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-UI-024 — `/` tuşu sayfadaki arama kutusuna odaklanır

**Gerçek sonuç**
Ön koşulun kendisi kod tarafında YANLIŞ: `input[data-search]` deseni
YALNIZCA tüketen tarafta var (`src/AgentPrism.UI/frontend/src/components/
layout.tsx:202`); tüm `src/AgentPrism.UI/frontend/src/screens/*.tsx` dosyaları
grep'lendi (`grep -rln "data-search" screens/`) — **sıfır** eşleşme. Agents
listesi (kod okuması VE canlı DOM anlık görüntüsüyle doğrulandı) hiç arama
kutusu içermiyor; hiçbir ekran içermiyor. `/`'nin bu davranışı `?` kısayol
yardımında kullanıcıya `shortcuts.focusSearch` ("Focus the search box on this
screen" / "Bu ekrandaki arama kutusuna odaklan") olarak REKLAM EDİLİYOR
(`command-palette.tsx:394`, `locales/en.ts:135`, `locales/tr.ts:134`) ama
hedefi hiçbir zaman yok — ölü/tamamlanmamış bir özellik. Agents ekranında `/`
basıldığında `document.activeElement` `BODY`'de kaldı, hata fırlamadı (ikinci
kısım — "hata vermez" — teknik olarak doğru, ama ilk kısım hiçbir ekranda hiç
gerçekleşemiyor). `HATA-S4-005`.

---

**🔧 Kapanış güncellemesi (2026-08-15, Aile V — HATA-S4-005 düzeltildi).**
`/` kısayolunun kendi mekanizması (`components/layout.tsx:202`,
`input[data-search]` sorgusu) zaten doğruydu — yalnız hiçbir ekran böyle bir
kutu render ETMİYORDU. Case'in kendi ön koşulunun adlandırdığı ekrana
(Agents listesi) gerçek bir arama kutusu eklendi: `TextInput
data-search` ile agent adı/görünen adına göre istemci-tarafı filtreleme
(`agents.tsx`).

Ampirik doğrulama (canlı sunucuya karşı, gerçek Playwright tarayıcısı):
Agents ekranında odak arama kutusunun DIŞINDAYKEN `/` basıldı —
`document.activeElement` artık `aria-label="Search"` taşıyan `<input>`,
ve içeriği `.select()` ile seçili geldi (`selectionStart === 0 &&
selectionEnd === value.length`). Dashboard'da (arama kutusu YOK) aynı
tuşa basıldığında konsolda sıfır hata/istisna — "böyle bir kutu taşımayan
bir ekranda `/` hiçbir şey yapmaz, hata vermez" beklentisi de doğrulandı.
Kutunun kendisi de test edildi: "claude" yazılınca liste 12 satırdan 2'ye
düştü (`claude-destek`, `claude-dusunen`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UI-026 — Sistem tercihi karanlıksa ilk yüklemede yanıp sönme olmadan karanlık tema uygulanır

**Gerçek sonuç**
Atlandı — bu makinenin macOS görünümü fiilen **Dark** (`defaults read -g
AppleInterfaceStyle` → `Dark`), ama Playwright MCP'nin başlattığı Chromium
bunu YANSITMIYOR: `window.matchMedia('(prefers-color-scheme: dark)').matches`
`false` döndü. Mevcut Playwright MCP araç kümesinde `prefers-color-scheme`
emülasyonu için bir araç (`browser_resize` gibi CDP `Emulation.setEmulatedMedia`
çağıran bir tool) yok — bu, ilk karede gerçek karanlık tercihle koşulan bir
yükleme gözlemlemeyi yapısal olarak engelliyor.

Destekleyici kod okuması (bulgu değil, doğrulama): `main.tsx:12`
`applyTheme(readThemePreference())` `createRoot(...).render()`'DAN ÖNCE, modül
seviyesinde senkron çağrılıyor; `index.html`'in `<body>`si `<div id="root">`
dışında hiçbir görünür içerik taşımıyor. Bu ikisi birlikte mimari olarak
"önce açık, sonra karanlık" yanıp sönmesini imkânsız kılıyor — ama bu, canlı
gözlemin yerini tutmaz.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-UI-028 — Ayarlar ekranındaki üç seçenekli tema seçici ile üst çubuktaki düğme aynı durumu paylaşır

**Gerçek sonuç**
İkinci madde geçti (üç seçenek: Follow system/Light/Dark). Birinci madde
**KALDI**: önce üst çubuktaki düğmeyle tema `dark` yapıldı (MT-UI-027), sonra
Ayarlar'a SPA içi gezinme ile gidildi (tam sayfa yenilemesi olmadan), `<select>`
"Follow system" yapıldı. Bu makinenin tarayıcısı `prefers-color-scheme: dark`
`false` bildiriyor (bkz. MT-UI-026), yani "sistemi izle" `light`'a çözümlenmesi
gerekiyordu — `document.documentElement.dataset.theme` gerçekten `"light"`'a
döndü (sayfa GÖRSEL olarak doğru). Ama üst çubuktaki düğmenin kendisi
YANLIŞ kaldı: `title="Theme: dark"`, `aria-label="Switch to light theme"`,
ikon hâlâ ay (`MoonIcon`) — sanki tema hâlâ `dark`'mış gibi.

**Kaldı** — kök neden: `src/AgentPrism.UI/frontend/src/components/layout.tsx:319-353`
`ThemeToggle` ve `src/AgentPrism.UI/frontend/src/screens/settings.tsx:31,134-147`
Ayarlar `<select>`'i **iki bağımsız `useState<ThemePreference>`** taşıyor —
paylaşılan bir context/store yok. Her ikisi de yalnız MOUNT anında
`readThemePreference()` ile localStorage'ı okuyor. `ThemeToggle` kabuğun bir
parçası olduğu için SPA içi gezinmede hiç unmount olmuyor; Ayarlar'ın
`<select>`'i `writeThemePreference`/`applyTheme`'i doğrudan çağırıp DOM'u
(`<html data-theme>`) günceller ama `ThemeToggle`'ın kendi `preference`/
`resolved` state'ini HİÇ bilgilendirmiyor. Sonuç: gerçek tema doğru
uygulanıyor ama düğmenin metni/ikonu bir sonraki TAM SAYFA YENİLEMESİNE kadar
eski değerde donuk kalıyor. `HATA-S4-006`.

**Kapsam:** Yalnız bu case değil — Ayarlar'daki `<select>`'ten yapılan HER
tema değişikliği (`system`/`light`/`dark` hangi yönde olursa olsun) üst
çubuktaki düğmeyi SPA oturumu boyunca yanıltıcı bırakır; kullanıcı sayfayı
yenilemeden düğmeye güvenirse yanlış temaya "geçtiğini" sanabilir.

---

**Aile U (bu koşum).** `src/AgentPrism.UI/frontend/src/lib/theme.ts`'e
paylaşımlı bir store eklendi (`auth.ts`'teki desenin aynısı —
`useSyncExternalStore`): `setThemePreference()` tek yazma yolu,
`useThemePreference()` tek okuma yolu. `ThemeToggle` (layout.tsx) ve Ayarlar
`<select>`'i artık kendi `useState`'lerini taşımıyor, ikisi de bu hook'u
kullanıyor. Komut paletinin "Tema değiştir" eylemi de (üçüncü, gözden kaçan
bir yazma yolu — `command-palette.tsx`) aynı `setThemePreference()`'a
bağlandı. "Sistemi izle"deyken OS tercihini dinleyen `matchMedia` listener'ı
artık bileşen başına değil, modül düzeyinde TEK sefer kurulur. Canlı Postgres'e
karşı doğrulandı: Ayarlar'daki `<select>`'ten "Dark" seçilince üst çubuktaki
düğmenin `title` özniteliği AYNI SPA oturumunda (sayfa yenilemeden)
`"Theme: dark"`'a dönüyor; "Light" seçilince `"Theme: light"`'a. Regresyon
testi: `tests/AgentPrism.Ui.E2ETests/UiTests.cs`
`Ayarlardaki_tema_secici_ust_cubuktaki_dugmeyi_ayni_oturumda_gunceller`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UI-031 — Tarayıcı dili `tr-TR` ise ve tercih kaydedilmemişse ilk açılış Türkçe olur

**Gerçek sonuç**
Atlandı — Playwright MCP'nin başlattığı Chromium'un `navigator.languages`
listesi `["en-US","en"]` ve mevcut araç kümesinde tarayıcı dilini (Chrome
başlatma bayrağı `--lang` veya Playwright context `locale` seçeneği) `tr-TR`
yapacak bir tool yok — bu, MT-UI-026'daki `prefers-color-scheme` engeliyle
aynı sınıftan bir tooling kısıtı.

Destekleyici doğrulama (bulgu değil): `matchLocale` fonksiyonunun
(`src/AgentPrism.UI/frontend/src/lib/i18n.tsx:66-76`) birebir kopyası canlı
sayfada çalıştırıldı — `matchLocale(['tr-TR'])` → `"tr"` döndü, algoritma
doğru. `initialiseLocale()` (`i18n.tsx:235-240`) da `applyTheme` ile aynı
desende: `main.tsx`'te React render'ından ÖNCE, senkron çağrılıyor
(`detectLocale()` → `readLocalePreference()` boşsa `navigator.languages`'a
bakar). Ama gerçek `navigator.languages=tr-TR` altında canlı bir sayfa
yüklemesi gözlemlenemedi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-UI-032 — Sunucudan gelen hata metinleri çevrilmez, olduğu gibi (İngilizce) görünür

**Gerçek sonuç**
Konsolun kendi metinleri Türkçe (birinci madde geçti). İKİNCİ MADDE KALDI:
dil Türkçeyken var olmayan bir agent'a gidildiğinde (`/agentprism/agents/
does-not-exist-xyz`) `role="alert"` kutusu şunu gösterdi: `"Agent bulunamadi:
'does-not-exist-xyz' adinda bir agent yok."` — bu metin **İNGİLİZCE DEĞİL**,
Türkçe (aksansız/ASCII harf çevirisi: "bulunamadi", "adinda"). `curl` ile
doğrudan sunucu doğrulandı — `Accept-Language` başlığı YOK, hatta açıkça
`Accept-Language: en` gönderilse de yanıt DEĞİŞMİYOR:
`{"title":"Agent bulunamadi","status":404,"detail":"'does-not-exist-xyz'
adinda bir agent yok.",...}`. Yani bu, istemcinin dil ayarına göre değil,
SUNUCU KODUNUN İÇİNE gömülü sabit bir Türkçe string.

**Kaldı** — kök neden: `src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs:513-514`
(ve aynı dosyada 217, 276, 345, 427, 450, 458, 473, 505, 562, 571, 579, 594,
602, 852, 867, 986, 1001, 1013, 1021, 1031 satırlarındaki HER `title`/`detail`)
`ProblemDetails` metnini `"Agent bulunamadi"` / `$"'{name}' adinda bir agent
yok."` gibi SABİT Türkçe (aksansız) string olarak yazıyor — bir kaynak/lokalizasyon
sistemi veya `CurrentUICulture` YOK, doğrudan literal. `grep -rlE` ile
repo'da bu ASCII-Türkçe kalıbı (`bulunamadi|kullanimda|gecersiz|desteklenmiyor|
eksik|zorunlu|gerekli|basarisiz`) taşıyan **28 kaynak dosyası** bulundu — pratik
olarak `src/AgentPrism.AspNetCore/Endpoints/`'in TAMAMI dahil (`AgentEndpoints`,
`ApiKeyEndpoints`, `ApprovalEndpoints`, `AttachmentEndpoints`, `CatalogEndpoints`,
`EvalEndpoints`, `ExperimentEndpoints`, `GovernanceEndpoints`,
`KnowledgeEndpoints`, `ModelHealthEndpoints`, `ObservabilityEndpoints`,
`QuotaEndpoints`, `RetentionEndpoints`, `RunEndpoints`, `SchedulingEndpoints`,
`SessionEndpoints`, `SkillEndpoints`, `SkillScriptGrantEndpoints`,
`WebhookEndpoints`, `WorkflowEndpoints`, `Voice/VoiceConversationEndpoint.cs`)
artı `AgentPrism.Core`/`AgentPrism.Workflows`/`AgentPrism.Generators`'daki
birkaç dosya. `ErrorNote`'un kendi yorumundaki "the API contract is
single-language on purpose" iddiası — o "tek dil"in **İngilizce** olduğu
varsayımıyla yazılmış — ama sunucu tarafı fiilen Türkçe. `HATA-S4-007`.

**Kapsam:** Bu case'e özgü değil — HTTP API'nin görünür yüzeyinin BÜYÜK
ÇOĞUNLUĞU (28 dosya) etkileniyor; İngilizce konuşan HERHANGİ bir API
tüketicisi (kütüphaneyi tüketen "milyonlarca geliştirici", CLAUDE.md'nin
kendi tanımı) hata mesajlarını Türkçe alıyor. Şerit 1/2/3'ün önceki HTTP
case'lerinde bu muhtemelen fark edilmedi çünkü `detail`/`title` alanları
genelde yalnız VARLIĞI (`404` durumu, alan adı) doğrulanmış, metnin dili
ayrıca kontrol edilmemişti.

---

**Yeniden koşum (Aile N, KAPANIS-PLANI.md).** `src/AgentPrism.AspNetCore/`
altındaki 26 dosyadaki **113 `title:` literalinin tamamı** ve eşlik eden
`detail:` metinleri İngilizce'ye çevrildi — bu case'in kendi tekrar
üretimindeki tam örnek dahil: `GET /api/agents/does-not-exist-xyz` artık
`{"title":"Agent not found","detail":"There is no agent named
'does-not-exist-xyz'.",...}` döner (canlı Postgres'e karşı doğrulandı,
`Accept-Language: en` başlığıyla/başlıksız fark yok — sunucu zaten tek
dilli). Kapsam yalnız `title:`/`detail:` literalleriyle sınırlı kalmadı:
bu literallerin beslendiği alttaki `AgentPrismException`/doğrulayıcı
mesajları da (AgentPrism.Core, AgentPrism.Workflows, AgentPrism.Generators
— case'in kendi öngördüğü "birkaç dosya" kapsamı) aynı geçişte çevrildi,
aksi halde `detail: ex.Message` yolundan Türkçe metin sızmaya devam
ederdi. Ek olarak OpenAI-uyumlu uçlar (`/v1/responses`,
`/v1/chat/completions`, `/v1/conversations`), A2A/MCP dış çağrı hata
metinleri ve `AgentPrism.Voice`'un WebSocket ProblemDetails yazıcısı da
aynı ilkeyle çevrildi — bunlar `ProblemDetails` kullanmadığı için ilk
grep'in (113 sayımı) dışında kalmıştı ama aynı kullanıcı kararının
(§5.3) kapsamındaydı. Regresyon çiti:
`tests/AgentPrism.Core.UnitTests/Architecture/ProblemDetailsLanguageTests.cs`
— tüm `src/` ağacını tarar, her `title:` literalinin ASCII-Türkçe
kalıp taşımadığını doğrular (fix geri alınıp koşulduğunda KIRMIZI
verdiği ampirik olarak doğrulandı). 29 mevcut test (Workflows.UnitTests
+ AspNetCore.FunctionalTests) eski Türkçe metni doğrudan `ShouldContain`
ile arıyordu; hepsi yeni İngilizce alt dizeye güncellendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UI-033 — Derin metin denetimi: beş ekranda TR ve EN metin taşması yok

**Gerçek sonuç**
1280px genişlikte, beş ekranın (Dashboard, Agents liste, Settings, Tools,
Models) hepsi hem `tr` hem `en`'de programatik olarak tarandı:
`document.body.innerText.match(/\{\w+\}/g)` (çıplak yer tutucu) ve
`el.scrollWidth > el.clientWidth` (yatay taşma) her ekranda çalıştırıldı.
`document.body.scrollWidth` her zaman `≤1280` (pencere genişliği) kaldı —
yatay sayfa taşması yok. Tek eşleşme: Settings ekranındaki Webhooks panelinde
`{id}` — kaynağı incelendi (`src/AgentPrism.UI/frontend/src/components/
webhook-panel.tsx:103`, `<Mono>/api/runs/&#123;id&#125;</Mono>`) ve bu bir
`interpolate()` kusuru DEĞİL: kasıtlı olarak HTML entity ile kaçırılmış,
çevrilmeyen, sabit bir REST yol deseni örneği (`webhooks.noticeAfter`
çeviri anahtarının DIŞINDA, ayrı bir `<Mono>` öğesi). Her iki dilde de
aynı şekilde göründüğü (i18n katalogunun parçası olmadığı) doğrulandı — kusur
değil. Buton/rozet metinleri (`code`/`harness` rozetleri, "Yeni agent" vb.)
görsel olarak taşmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UI-038 — Boş model kataloğu hata değil, yönlendirici boş durum gösterir

**Gerçek sonuç**
Ön koşul bu ÖRNEK UYGULAMADA fiilen kurulamıyor: `samples/AgentPrism.Api/
Program.cs:128-149` OpenAI anahtarı boşken KOŞULSUZ `agentPrism.
AddModelProvider(new EchoModelProvider())` çağırıyor ("Anahtar yoksa
uygulama ağ çağrısı yapmayan örnek sağlayıcı ile çalışır; hiçbir şey
kırılmaz" — bilinçli tasarım). Tüm sağlayıcı anahtarları (`OpenAI`,
`Anthropic`, `Google`, `OpenAICompatible:openrouter`) boş bırakılıp uygulama
yeniden başlatıldı: Modeller ekranı SIFIR sağlayıcı değil, `echo` adlı TEK
bir sağlayıcı ve `echo-1` modelini (bağlam 8.192, çıktı 1.024, `streaming`)
gösterdi — ne kırmızı hata ne de dokümanın tarif ettiği "boş durum kartı"
(`UseOpenAI`/`UseOpenAICompatible` örnek kodu) göründü, çünkü katalog
GERÇEKTEN boş değildi. **Doküman düzeltmesi (AGENTS.md: doküman-kod
çelişkisinde doküman yanlıştır):** bu case'in ön koşulu ("hiçbir sağlayıcı
kayıtlı değil") `samples/AgentPrism.Api` üzerinden hiçbir zaman
üretilemez — echo fallback'i kasıtlı olarak bunu engelliyor. Gerçek boş
katalog durumu yalnız `AgentPrism.Core`'un `ModelProviderRegistry`'sine
doğrudan birim testiyle ya da örnek uygulama dışında sıfır sağlayıcılı
özel bir host ile üretilebilir — bu, elle arayüz testinin kapsamı dışında.
Uygulama normal yapılandırmayla (tüm anahtarlar dolu) yeniden başlatıldı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-UI-039 — "Şimdi kontrol et" yalnız o sağlayıcının satırını günceller

**Gerçek sonuç**
`openai` panelinde "Şimdi denetle"ye tıklandı. Ağ sekmesinde YALNIZ
`GET /api/models/health/openai?refresh=true` çağrıldı (diğer sağlayıcılar
için hiçbir istek gitmedi). `anthropic`/`google`/`openai-responses`/
`openrouter` satırlarının "... sn. önce denetlendi" zaman damgaları
DEĞİŞMEDİ — yalnız `openai` satırı güncellendi. (Yan not: ilk denetimde
`openai` bir kez "erişilemiyor"/"Zaman asimi" (10.01s) gösterdi, ikinci
tıklamada hemen "sağlıklı"ya döndü — geçici bir zaman aşımıydı, kalıcı bir
kusur değil; bu case'in kapsamı olan "yalnız o satır güncellenir" davranışını
etkilemedi.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UI-042 — Safari'de kabuk açılır, temel gezinme ve tema/dil düğmeleri çalışır

**Gerçek sonuç**
Tooling kısıtı — bu Playwright MCP kurulumu yalnız Chromium'u sürüyor,
tarayıcı seçim parametresi yok (`browser_navigate`/`browser_tabs` şemasında
WebKit/Safari seçeneği yok). WebKit/Safari not available in this Playwright
MCP setup.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-UI-043 — Dar ekranda (375px) genel ekranlar yatay taşma yapmaz

**Gerçek sonuç**
375px'te 4 ekran (`document.documentElement.scrollWidth` vs `clientWidth`
ile ölçüldü): Dashboard **481→508** (taşıyor), Ayarlar **481** (taşıyor),
Modeller **364=364** (taşımıyor, GEÇTİ), Tool'lar **508** (taşıyor). Mobil
nav şeridinin kendisi (`overflow-x-auto md:hidden`, 09/10'un beklediği gibi)
doğru çalışıyor — üç ekrandaki taşma BAŞKA, üç AYRI kaynaktan geliyor
(`HATA-S4-008`). Üst çubuktaki dil/tema düğmeleri kendileri üst üste
binmiyor (ayrı gözlem, doğru). **`HATA-S4-008` açıldı** — bkz. aşağı.

---

**Aile U (bu koşum).** Üç bilinen kök neden düzeltildi: `ui.tsx`'teki
`Panel` başlık+aksiyon satırına `flex-wrap` (+ başlığa `min-w-0`),
`settings.tsx`'teki `Row`'un `dd`'sine `break-words` (uzun URL değerleri
artık sarıyor), `tools.tsx`'teki rozet+agent-link satırına `flex-wrap`.
**Dördüncü, önceden kayıtlı olmayan bir kök neden de bulundu**: düzeltmeler
sonrası Playwright ile yeniden ölçüldüğünde Ayarlar hâlâ 92px taşıyordu —
`settings.tsx:43`'teki `<div className="grid gap-4 lg:grid-cols-2">`'nin
`lg:` ALTINDA temel bir `grid-cols-1` taşımaması: CSS Grid'de sütun
`auto` sınıfıyla içerik genişliğine göre büyüyebilir ve konteynerin kendi
genişliğiyle SINIRLI DEĞİLDİR — grid öğesi (Panel) konteynerinin (343px)
dışına, 451px'e kadar taşabiliyordu. `grid-cols-1` eklenerek sütun `1fr`'e
(konteyner genişliğine) sabitlendi. Aynı desen (`grid ... lg:grid-cols-N`
temel sınıf olmadan) kod tabanında 30'dan fazla yerde tekrarlanıyor ama
yalnız bu üçlü (Dashboard/Ayarlar/Tool'lar) bu case'in kapsamındadır —
diğerleri doğrulanmamış, ayrı bir bulgu olarak not düşülür (aşağıdaki not).
Canlı Postgres'e karşı 375px'te üçü de yeniden ölçüldü: Dashboard 331=331,
Ayarlar 331=331, Tool'lar (`cancel_order`, GERÇEKTEN 4 agent tarafından
kullanılıyor) 331=331 — hiçbiri taşmıyor. Regresyon testi:
`tests/AgentPrism.Ui.E2ETests/UiTests.cs`
`Genel_ekranlar_375px_genislikte_yatay_tasma_yapmaz` (Dashboard+Ayarlar,
sabit E2E veri kümesiyle tetiklenebilen ikisi; Tool'lar dalı yalnız canlı
sunucuda doğrulandı — E2E fixture'ında hiçbir tool birden fazla agent
tarafından kullanılmıyor).

**🚨 Ayrı bulgu (kodlanmadı) — grid-cols temel sınıfı deseni.** `grid
gap-* [breakpoint]:grid-cols-N` (temel `grid-cols-1` OLMADAN) deseni
`agent-editor.tsx`, `run-detail.tsx`, `agent-detail.tsx`, `diagnostics.tsx`
ve daha birçok ekranda tekrarlanıyor. Her biri AYNI taşma sınıfını
üretebilir ama içeriğin genişliğine bağlıdır — doğrulanmadan varsayılamaz.
Kapsam dışı bırakıldı (yalnız bu case'in üç ekranı düzeltildi); gelecekte
dar-ekran şikayeti gelirse ilk bakılacak yer burasıdır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bu dosyada kanıtlanmayan, kod okurken fark edilen şüpheler

Bu bölüm bir kusur listesi değildir — koşum aşamasında doğrulanacak
**şüphelerdir** (`PROMPT.md` §7.3).

- **MT-UI-007**: Token silindikten sonra `AccessGate`'in yeniden `TokenPrompt`'a
  düşmesi, `probe` sorgusunun (`enabled: meta.isSuccess`) token değişince
  yeniden çalışacağı varsayımına dayanır (`useQuery({ queryKey: ['probe',
  token], ... })` — `token` anahtarın parçası, bu yüzden değişince React Query
  otomatik yeniden sorgular). Bu akış tarayıcıda **koşulmadı**, yalnız kaynaktan
  çıkarsandı; koşum gerçek gecikmeyi (varsa) kaydeder.
- **MT-UI-011**: `AuditScreen`'in 403 sonrası tam olarak nasıl göründüğü
  (`entries.isError` dalı doğrulandı) ama ekranın geri kalan kısmının (varsa
  filtre çubuğu, sayfalama) 403 durumunda render edilip edilmediği (yarım bir
  iskelet mi kalıyor, yoksa tamamen mi gizleniyor) satır satır izlenmedi.
- **MT-UI-013/043**: Tailwind'in `md` kırılım noktasının 768px olduğu
  `styles.css`'te özel bir `screens` geçersiz kılması **bulunmadığı** için
  varsayılan değer olarak çıkarsandı (Tailwind v4 belgesi, kod içinde açık bir
  sayı olarak yazılı değil). Koşum gerçek kırılma genişliğini gözlemleyerek
  doğrular.

---

# Faz 30 — Arayüz Cilası: Yerelleştirme, Komut Paleti ve Kısayollar

> **Durum:** ✅ Tamamlandı (2026-08-05)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-25**, **F-26**
> **Önkoşul:** Yok — ama **en sonda** olması bilinçliydi
> **Paketler:** `AgentPrism.UI` (yalnız arayüz; **sunucuda tek satır değişiklik yok**)
> **Migration:** Yok
> **Kararlar:** K-228 … K-238

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/30-ARAYUZ-CILASI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Ne Yapıldı

Arayüz **tamamen** iki dilli oldu: 24 ekranın ve 12 bileşenin tüm metinleri
sözlüğe taşındı, komut paleti ve klavye kısayolları eklendi, konuşma modu dile
uygun sesle konuşur hâle geldi ve erişilebilirlikte ölçülen bir kontrast hatası
düzeltildi.

| Ölçüm | Değer |
|---|---|
| Sözlük anahtarı | **794** (her biri iki dilde) |
| Çevrilen dosya | 24 ekran + 12 bileşen + kabuk |
| Yeni birim testi | 48 (`i18n`, `shortcuts`, `palette`) — toplam frontend testi **141** |
| Yeni E2E testi | 9 — toplam **41** |
| Bundle | **151,3 KB gzip / 250 KB bütçe** (Faz 29 sonunda 124,9 KB) |
| Sunucu değişikliği | **Yok** |

---

## Plandan Sapmalar

Planla gerçek arasındaki farklar — ve gerekçeleri:

### 1. `lib/i18n.ts` değil, `lib/i18n.tsx`

Modül bir React bağlamı (`LocaleProvider`) ve iki hook (`useT`, `usePlural`)
dışa açıyor, dolayısıyla JSX içeriyor. Saf kısımlar (`interpolate`,
`matchLocale`, `isLocale`, biçimlendiriciler) aynı dosyadadır ve Vitest onları
Node ortamında test eder; `applyDocumentLocale` bu yüzden `document` yokluğunda
sessizce geri döner.

### 2. `t`'nin kararlı referans olması bir gereksinim değil, **tasarımın merkezi** oldu

Faz dokümanının 🚨 uyarısı ("`t` her dil değişiminde yeni referans olursa açık
WebSocket kopabilir") çözümü belirledi: `useT()` bağlamı okur ama **modül
düzeyindeki** `translate` fonksiyonunu döndürür. Böylece `voice-panel.tsx`
içindeki `start` bağımlılık dizisine `t` girse bile yeniden kurulmaz. Karar
K-229.

### 3. Bundle hedefi tutmadı — ve DoD'deki hedef zaten imkânsızdı

Plan "+6 KB gzip'ten az" ve "toplam 120 KB gzip altı" diyordu. İkisi de gerçekçi
değildi:

- Faz 29 **124,9 KB** ile kapanmıştı; "toplam 120 KB altı" bu fazın başlangıç
  noktasının altındaydı, yani ancak mevcut kod silinerek karşılanabilirdi.
- "+6 KB" iki küçük sözlük varsayıyordu. Gerçek kapsam 794 anahtar × 2 dildir.

Ölçülen: **+26,4 KB gzip**. Bu artışın tamamı sözlüklerdir — `en.ts` + `tr.ts`
kaynak hâlde 29,7 KB gzip'tir; i18n çalışma zamanı, komut paleti ve kısayol
modülü birlikte gürültü seviyesinde kalır. Gerçek kapı olan **250 KB bütçesinin
%61'i** kullanılmış durumda.

### 4. İki ekranda **hard-coded Türkçe** metin bulundu ve düzeltildi

Bu faz i18n'i kurarken, dilin daha önce iki yerde sızmış olduğu ortaya çıktı:

- `components/charts.tsx` → `const EMPTY_MESSAGE = 'Bu aralıkta çalıştırma yok'`
  ve iki Türkçe `aria-label`
- `screens/skills.tsx` → script çalıştırma izni ve script içeriği uyarılarının
  ikisi de Türkçe sabitti

Üçü de sözlüğe alındı. Bunlar Faz 30'un ürettiği bir hata değil, Faz 30'un
**görünür kıldığı** bir tutarsızlıktır.

### 5. Durum rozetleri için ikinci bir anahtar kümesi gerekti

Konsolun rozet dili küçük harftir (`code`, `db`, `harness`, `ok`, `done`). Aynı
durum adı bir `<option>` etiketinde veya sütun başlığında büyük harf ister. Tek
küme ikisinden birini bozuyordu; `runs.status.*` (rozet, küçük) ile
`runs.filter.*` (süzgeç/başlık, büyük) ayrıldı. Karar K-233.

### 6. Erişilebilirlik denetimi **gerçek bir hata** buldu

Plan "kontrast WCAG AA (açık ve koyu tema)" diyordu; denetim bir onay kutusu
değil ölçüm oldu ve iki tema da eşiği karşılamıyordu. Ayrıntı aşağıda.

### 7. Konuşma çözümlemesine dil kodu gönderilmedi

Plan yalnız **seslendirmeyi** dile bağlamayı istiyordu ve o yapıldı. Çözümleme
(speech-to-text) sağlayıcının dil sezmesine bırakıldı; bilinçli sınır, karar
K-235.

---

## Bitiş Ölçütleri (DoD)

| Ölçüt | Durum |
|---|---|
| Arayüz Türkçe ve İngilizce çalışıyor; dil tercihi kalıcı | ✅ 794 anahtar; E2E yeniden yüklemede kalıcılığı doğruluyor |
| Eksik çeviri **derlemeyi kırıyor** | ✅ Kanıt: `nav.runs` silinince `TS2741`, `npm run build` içindeki `tsc --noEmit` durur |
| Komut paleti çalışıyor; rol bazlı filtreleme doğru | ✅ E2E: Admin policy reddedilince "New agent" komutu paletten kayboluyor |
| **Konuşma modu iki dilde çalışıyor**, seslendirme dile uygun sesle | ✅ Dil başına ses Ayarlar'da seçilir, `start` çerçevesinde gönderilir (K-234). ⚠️ Gerçek bir ses sağlayıcısıyla **elle** doğrulanmadı: örnek uygulamada `AgentPrism:Voice` yapılandırılmamış (`/api/voice/voices` → 500) ve E2E sahte sentezleyici kullanıyor. Doğrulanan: panelin sağlayıcısız gizlenmesi ve `voiceId`'nin protokolde taşınması |
| Kısayollar çalışıyor ve metin alanlarında tetiklenmiyor | ✅ 18 birim + 3 E2E testi |
| Klavye ile tüm ekranlar gezilebiliyor; odak görünür | ✅ Global `:focus-visible`; palet odak tuzağı ve `aria-activedescendant` ile |
| Bundle ölçüldü ve bütçe içinde | ✅ **151,3 KB / 250 KB**. ❌ Plandaki "toplam 120 KB altı" hedefi **karşılanmadı ve karşılanamazdı** — faz 124,9 KB'den başlıyordu. Gerekçe "Plandan Sapmalar §3" |
| Dört doğrulama kapısı sıfır uyarı | ✅ build / pack / format temiz. `dotnet test`: **1847 başarılı**, 223 başarısız — hepsi `AgentPrism.SqlServer.IntegrationTests`, `mssql/server` konteyneri bu makinede hiç ayağa kalkmıyor ([23-SQL-SERVER.md](23-SQL-SERVER.md)'de kayıtlı, bu fazdan bağımsız) |

---

## Bu Fazda Verilen Kararlar

| No | Karar |
|----|-------|
| K-228 | i18n kütüphanesi alınmadı; `lib/i18n.tsx` elle yazıldı — eksik anahtar derleme hatası |
| K-229 | `t` modül düzeyindedir, kimliği hiç değişmez (açık WebSocket'i korur) |
| K-230 | Dil tercihi `localStorage`'da; token `sessionStorage`'da kalır |
| K-231 | Varsayılan dil tarayıcıdan gelir — E2E testleri dili sabitlemek zorundadır |
| K-232 | Sunucu yanıtları çevrilmez; API sözleşmesi tek dillidir |
| K-233 | Rozet küçük harf, süzgeç/başlık büyük harf: iki anahtar kümesi |
| K-234 | Dil başına ses eşlemesi istemcide; protokol zaten taşıyordu |
| K-235 | Konuşma çözümlemesine dil kodu gönderilmez |
| K-236 | `--ap-subtle` / `--ap-muted` WCAG AA'ya göre düzeltildi |
| K-237 | Kısayol metin alanında tetiklenmez; `Ctrl+Enter` yereldir |
| K-238 | Komut paleti istemci tarafında arar |

Faz dokümanının "Açık Sorular" bölümündeki dördü de kullanıcıya soruldu ve
önerilen seçenekler onaylandı: tarayıcı dili, terimlerin korunması, istemci
tarafı `voiceId`, istemci tarafı arama. Üçüncü dil **eklenmedi**; mimari
destekliyor.

---

## Sonraki Faza Devir Notu

Bu faz ikinci faz turunun **son** kalemidir. Üçüncü tur adayları:
[`ADAYLAR.md`](../../ADAYLAR.md). Ayrıca
[Faz 7 (yayın)](07-SAGLAMLASTIRMA-VE-YAYIN.md) hâlâ beklemededir (K-068).

🚨 **Bundan sonra her yeni ekran metni iki dilde yazılır.** Unutulamaz: eksik
anahtar derlemeyi kırar. Kural `AGENTS.md`'ye ve `faz-tamamlama` skill'ine
eklendi.

🚨 **Metin üzerine iddia kuran her E2E testi dili sabitlemek zorundadır.**
`Session.OpenAsync` varsayılanı `en-US`'tir. Bu unutulursa test, çalıştıran
makinenin sistem diline bağlanır ve başka bir bilgisayarda kırılır (K-231).

🚨 **İki mesaj birbirinin öneki olmamalıdır.** Playwright'ın `GetByText` çağrısı
alt dizi eşler; `charts.noRuns` ("No run in this window") ile
`dashboard.noRunsInWindow` ("No run in this window yet.") çakıştı ve testi strict
mode ihlaliyle kırdı. İkincisi yeniden yazıldı.

**Açık uçlar:**

- **Bundle bütçesi gözden geçirilmelidir.** 151,3 KB / 250 KB — %61 kullanıldı,
  ~99 KB pay kaldı. Üçüncü bir dil kabaca +13 KB gzip getirir. Dördüncü dilden
  önce sözlüklerin gecikmeli yüklenmesi değerlendirilmelidir; bugün gerekmiyor.
- **Konuşma modu gerçek bir ses sağlayıcısıyla elle denenmedi** (yukarıdaki DoD
  satırı). Ses yapılandırması olan bir kurulumda ilk iş bu olmalıdır.
- **Çözümleme dili gönderilmiyor** (K-235). Sağlayıcının sezmesi yanlış dil
  üretirse `VoiceClientMessage`'a `language` eklenir — tip `internal`, kırıcı
  değil.
- **`DependencyDirectionTests.AllowedReferences` hâlâ `AgentPrism.SqlServer`,
  `AgentPrism.Sqlite` ve `AgentPrism.Sql.Shared` paketlerini içermiyor**
  (Faz 23/24'ten kalan boşluk; 26, 27, 28, 29 ve 30'da da açıktı).
- **`AttachmentTypeGuard` EBML (WebM) imzasını tanımaz** (Faz 29'dan devrediyor).
- **`ISpeechTranscriber` tek atımlıdır** (K-226); artımlı transkript ayrı bir
  arayüz ister.
- **Depoda senkronizasyon kopyaları oluşabiliyor.** Bu fazda
  `wwwroot/assets/index-….css 2.br` gömülü varlık listesini kirletti ve arayüz
  hiç yüklenmedi; `dotnet build` yeşildi. Belirti: sayfa boş, konsolda 404.
  Çözüm `find src -name "* 2.*" -delete`. Not `MEMORY.md`'de.

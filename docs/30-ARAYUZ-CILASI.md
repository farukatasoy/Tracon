# Faz 30 — Arayüz Cilası: Yerelleştirme, Komut Paleti ve Kısayollar

> **Durum:** ✅ Tamamlandı (2026-08-05)
> **Kaynak:** [BEYIN-FIRTINASI.md](BEYIN-FIRTINASI.md) · **F-25**, **F-26**
> **Önkoşul:** Yok — ama **en sonda** olması bilinçliydi
> **Paketler:** `AgentPrism.UI` (yalnız arayüz; **sunucuda tek satır değişiklik yok**)
> **Migration:** Yok
> **Kararlar:** K-228 … K-238

---

## Bu Faza Başlarken

1. [`05-AGENTPRISM-UI.md`](05-AGENTPRISM-UI.md) — arayüz mimarisi, `lib/` yapısı, bundle zinciri
2. [`29-KONUSMA-KATMANI.md`](29-KONUSMA-KATMANI.md) — yalnız **"Sonraki Faza Devir Notu"** bölümü
3. [`KARARLAR.md`](KARARLAR.md) — **"Arayüz i18n altyapısı kurulmadı"** (bölüm 1, kapatıldı);
   **K-045** (kütüphane yerine elle yazma), **K-002** (bundle bütçesi), **K-047** (token depolama)
4. [`docs/hafiza/frontend.md`](hafiza/frontend.md) — arayüze dokunuyorsunuz
5. Bu doküman

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

## 30.1 — i18n (F-25)

### Kütüphane alınmadı (K-228)

`lib/i18n.tsx` ~150 satırdır ve bir kütüphanenin veremeyeceği bir güvence verir:

```ts
// locales/en.ts — `as const` YOK: tip anahtarları tutar, cümleleri değil.
export const en = { 'nav.runs': 'Runs', /* … */ };
export type Messages = typeof en;

// locales/tr.ts
export const tr: Messages = { 'nav.runs': 'Çalıştırmalar', /* … */ };
```

Eksik anahtar **derleme hatasıdır**. `nav.runs` bilerek silindiğinde:

```
src/locales/tr.ts(17,14): error TS2741: Property ''nav.runs'' is missing in type
'{ … 788 more … }' but required in type '{ … 789 more … }'.
```

`npm run build` sırası `tsc --noEmit` → `vitest run` → `vite build` olduğu için
bu kapı `dotnet build`'in içindedir.

### Gerçekleşen public yüzey

```ts
// lib/i18n.tsx
export const LOCALES: readonly ['en', 'tr'];
export type Locale = 'en' | 'tr';
export type MessageKey = keyof Messages;
export type PluralKey = /* yalnız _one ve _other'ı BİRLİKTE olan taban anahtarlar */;

export function translate(key: MessageKey, params?: MessageParams): string;
export function plural(key: PluralKey, n: number, params?: MessageParams): string;
export function interpolate(template: string, params?: MessageParams): string;

export function isLocale(value: string | null | undefined): value is Locale;
export function matchLocale(tags: readonly string[]): Locale | null;
export function detectLocale(): Locale;
export function readLocalePreference(): Locale | null;
export function writeLocalePreference(locale: Locale): void;
export function activeLocale(): Locale;
export function initialiseLocale(): Locale;      // main.tsx, ilk render'dan önce
export function applyDocumentLocale(locale: Locale): void;

export function formatNumber(value: number, options?: Intl.NumberFormatOptions): string;
export function formatDateTime(value: Date, options?: Intl.DateTimeFormatOptions): string;
export function formatRelative(value: number, unit: Intl.RelativeTimeFormatUnit): string;

export function LocaleProvider(props: { children: ReactNode }): ReactNode;
export function useLocale(): { locale: Locale; setLocale(next: Locale): void };
export function useT(): typeof translate;        // 🚨 KARARLI referans (K-229)
export function usePlural(): typeof plural;
```

`PluralKey` bir taban anahtarı ancak `_one` **ve** `_other` sürümlerinin ikisi de
varsa kabul eder; yarısı yazılmış bir çoğul `plural()` çağrısında derleme hatası
verir.

### Biçimlendirme `Intl` ile

`lib/format.ts` artık dile duyarlıdır: `relativeTime` → `Intl.RelativeTimeFormat`,
`absoluteTime` → `Intl.DateTimeFormat`, `count`/`percent`/`money` →
`Intl.NumberFormat`. Türkçe `1.234,5` yazar, İngilizce `1,234.5`; yanlış
biçimlendirilmiş bir sayı yalnız çirkin değil, **yanlış okunur**.

SI birim simgeleri (`ms`, `s`, `m`) çevrilmez — `duration()` her dilde aynıdır.

### Kapsam

| Çevrildi | Çevrilmedi |
|----------|-----------|
| 24 ekranın ve 12 bileşenin tüm metinleri | Agent, tool, model, workflow adları (kimlik) |
| Doğrulama ve hata **başlıkları** | Sunucudan gelen hata **metinleri** (K-232) |
| Tarih, sayı, yüzde, para biçimi | Yapılandırma anahtarı adları (`AgentPrism:…`) |
| `aria-label`, `title`, `placeholder` | MCP prompt'una eklenen başlık (modele giden metin) |
| Konuşma modunun tüm durumları | SI birim simgeleri, enum tel değerleri |

### Terim politikası *(kullanıcı kararı)*

Depo dokümanlarının kuralı arayüze de uygulandı: `agent`, `tool`, `skill`,
`workflow`, `token`, `prompt`, `MCP`, `OAuth` İngilizce kalır ve Türkçe eklerini
kesme işaretiyle alır (`agent'lar`, `tool'lar`). `run` → **çalıştırma**,
`session` → **oturum** — dokümanların zaten kullandığı karşılıklar.

Bir birim testi bunu korur: İngilizce ile birebir aynı kalan 24 anahtarın listesi
testte açıkça yazılıdır, yani unutulmuş bir çeviri o listeye eklenmeden geçemez.

---

## 30.2 — Komut Paleti ve Kısayollar (F-26)

### Komut paleti

`Ctrl/Cmd + K`. Kaynaklar: gezinme, agent'lar (aç / Playground'da çalıştır),
workflow'lar, son 50 çalıştırma (kimliğin ilk karakterleriyle), eylemler
(yeni agent, yeni workflow, temayı değiştir, dili değiştir, kısayol yardımı).

- Bulanık eşleme `lib/palette.ts` içinde ~40 satır saf mantıktır: alt dizi
  puanlaması, bitişik eşleşmeye ve kelime başına bonus, kısa etikete öncelik,
  eşitlikte bildirim sırası korunur (yazarken liste zıplamaz)
- Katalog sorguları **yalnız palet açıkken** koşar (K-238)
- Komutlar **role göre** süzülür: Reader "Yeni agent" görmez
- Erişilebilirlik: `role="dialog"` + `aria-modal`, `role="combobox"` girdi,
  `role="listbox"`/`option` liste, `aria-activedescendant` ile klavye seçimi,
  `Esc` ile kapanma, Tab girdide kilitli (odak tuzağı)

### Kısayollar

| Kısayol | Eylem | Metin alanında |
|---------|-------|----------------|
| `Ctrl/Cmd + K` | Komut paleti | ✅ çalışır |
| `Esc` | Açık katmanı kapat | ✅ çalışır |
| `g` sonra `a` / `r` / `s` / `d` / `w` / `p` | Agents / Runs / Sessions / Dashboard / Workflows / Playground | ❌ |
| `/` | Bu ekrandaki arama kutusuna odaklan | ❌ |
| `?` | Kısayol yardımı | ❌ |
| `Ctrl/Cmd + Enter` | Playground'da promptu gönder | **yerel** — `textarea` üzerinde |

`lib/shortcuts.ts` saf mantıktır ve 18 birim testiyle korunur. Sıra öneki
(`g`) 1,2 saniyede zaman aşımına uğrar ve **metin alanında hiç kurulmaz** —
kurulsaydı sonraki harfi yutardı (K-237).

---

## 30.3 — Erişilebilirlik Denetimi

Denetim bir onay kutusu değil ölçüm oldu ve **gerçek bir hata buldu**:

| Değişken | Eski | Ölçülen | Yeni | Ölçülen |
|---|---|---|---|---|
| `--ap-subtle` (açık) | `#8e8e9d` | 3,23:1 ❌ | `#6b6b79` | 5,24:1 ✅ |
| `--ap-subtle` (koyu) | `#6e6e80` | 3,79:1 ❌ | `#85859a` | 5,25:1 ✅ |
| `--ap-muted` (açık) | `#61616f` | 6,09:1 ✅ | `#55555f` | 7,37:1 ✅ |

`subtle` ve `muted` 11–12 px'te kullanılıyor — WCAG için **normal boy** metin,
yani eşik 4,5:1. Eski değerler her iki temada da altındaydı. `muted` zaten
geçiyordu ama `subtle` koyulaşınca aradaki görsel basamak kaybolacağı için o da
koyulaştırıldı. Ölçüm `raised` zeminde de yapıldı (alt metin iki zeminde de
yaşıyor); `styles.css` içinde 🚨 notu ve eşikler yazılıdır.

Diğerleri:

- Odak halkası zaten global (`:focus-visible`, `outline: 2px solid accent`) —
  değişiklik gerekmedi
- `aria-live="polite"` eklendi: Playground turu (`aria-busy` ile), çalıştırma
  dökümü, workflow çıktısı, konuşma durumu ve konuşma dökümü, `Loading`
  (`role="status"`)
- `ErrorNote` → `role="alert"`
- Grafiklerin metin alternatifi vardı; `aria-label`'ları da çevrildi
- `<html lang>` dil değişiminde güncellenir ve ilk boyamadan **önce** yazılır
- Gezinme `<nav aria-label>` aldı (mobil ve masaüstü iki kopya vardı)

---

## 30.4 — Konuşma Modu Dile Bağlandı

🚨 Türkçe bir yanıtı İngilizce bir sesle seslendirmek sonucu anlaşılmaz kılar.
Ama sağlayıcı bir sesin **hangi dili konuştuğunu bildirmez**: `VoiceDescriptor`
yalnız `VoiceId`, `Name`, `Category` taşır. Eşleme türetilemez, kurulur.

Seçilen yol (K-234) — **sunucuda değişiklik yok**:

1. Ayarlar → **Sesler** paneli, `GET /api/voice/voices` ile dolar ve dil başına
   bir ses seçtirir. Ses sağlayıcısı yapılandırılmamışsa panel kendini gizler
   (örnek uygulamada uç 500 döner; panel `retry: false` ile sessizce yok olur).
2. Seçim `localStorage`'da `agentprism.voice.<dil>` anahtarında durur.
3. `voice-panel.tsx` `start` çerçevesinde `voiceId` gönderir.
   `VoiceClientMessage.VoiceId` bunu Faz 29'dan beri kabul ediyordu.

Çözümleme dil kodu almaz; sağlayıcı dili sezer (K-235).

---

## Gerçekleşen Dosyalar

**Yeni**

```
src/AgentPrism.UI/frontend/src/
├── lib/
│   ├── i18n.tsx              # ~280 satır: sözlük, t, plural, Intl, provider, hooklar
│   ├── i18n.test.ts          # 16 test — biri iki sözlüğü karşılıklı denetler
│   ├── shortcuts.ts          # ~150 satır saf kısayol eşleme
│   ├── shortcuts.test.ts     # 18 test
│   ├── palette.ts            # ~90 satır bulanık eşleme
│   └── palette.test.ts       # 14 test
├── locales/
│   ├── en.ts                 # 794 anahtar; `Messages` tipinin kaynağı
│   └── tr.ts                 # `tr: Messages` — eksik anahtar derleme hatası
└── components/
    └── command-palette.tsx   # CommandPalette + ShortcutHelp
```

**Değişen** — kabuk (`main`, `app`, `layout`, `access-gate`, `ui`, `icons`,
`format`, `styles.css`, `voice`), 24 ekranın tamamı, 11 bileşen,
`tests/AgentPrism.Ui.E2ETests/UiTests.cs`.

---

## Testler

| Proje | Yeni | Neyi doğrular |
|-------|------|---------------|
| Vitest `i18n.test.ts` | 16 | Yer tutucu değiştirme, bilinmeyen yer tutucunun **görünür kalması**, `tr-TR` → `tr` eşlemesi; iki sözlüğün aynı anahtar kümesi ve **aynı yer tutucuları** taşıması; her `_one`'ın bir `_other` kardeşi olması; İngilizce ile birebir aynı kalan 24 anahtarın açık listede olması |
| Vitest `shortcuts.test.ts` | 18 | Ctrl/Cmd birleşmesi, sıra zaman aşımı, başarısız sıradan sonra önekin **kalmaması**, metin alanında plain-harf kısayolun ve sıra önekinin **tetiklenmemesi**, `insideText` bağlamalarının yine ateşlemesi |
| Vitest `palette.test.ts` | 14 | Alt dizi eşleme ve reddi, bitişik/kelime başı bonusu, boş sorguda bildirim sırası, gizli anahtar kelimeyle eşleşme, kimlik önekiyle çalıştırma bulma |
| Vitest `format.test.ts` | (güncellendi) | `Intl` çıktıları: `12 sec. ago`, `4 min. ago`, `3 days ago`; saat kaymasında `now` |
| **TypeScript** | — | `tr` sözlüğünde eksik anahtar → `TS2741` (kanıt yukarıda) |
| E2E `UiTests.cs` | 9 | Tarayıcı diline göre açılış (`tr-TR` → Türkçe + `lang="tr"`), İngilizce tarayıcıda İngilizce kalma, dil değişimi + yeniden yüklemede kalıcılık + Ayarlar seçicisinin uyumu, Türkçe arayüzde sunucu hatasının **çevrilmeden** gösterilmesi, palet ile gezinme, `Esc` ile kapanma, rol bazlı komut süzgeci, `g a` kısayolu, metin alanında kısayolun tetiklenmemesi, `?` yardımı |

Frontend **141** test (10 dosya), E2E **41** test — hepsi yeşil.

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
[`UCUNCU-FAZ-ADAYLARI.md`](UCUNCU-FAZ-ADAYLARI.md). Ayrıca
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

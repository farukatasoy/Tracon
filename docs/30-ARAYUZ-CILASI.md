# Faz 30 — Arayüz Cilası: Yerelleştirme, Komut Paleti ve Kısayollar

> **Durum:** 📋 Planlandı
> **Kaynak:** [BEYIN-FIRTINASI.md](BEYIN-FIRTINASI.md) · **F-25**, **F-26**
> **Önkoşul:** Yok — ama **en sonda** olması bilinçlidir
> **Paketler:** `AgentPrism.UI`
> **Migration:** Yok

---

## Bu Faza Başlarken

1. [`05-AGENTPRISM-UI.md`](05-AGENTPRISM-UI.md) — arayüz mimarisi, `lib/` yapısı, bundle zinciri
2. [`29-KONUSMA-KATMANI.md`](29-KONUSMA-KATMANI.md) — yalnız **"Sonraki Faza Devir Notu"** bölümü
3. [`KARARLAR.md`](KARARLAR.md) — **"Arayüz i18n altyapısı kurulmadı; dil İngilizce"** (bölüm 1) — bu kararın **yeniden açılma koşulu bu fazdır**; **K-045** (kütüphane yerine elle yazma), **K-002** (bundle bütçesi)
4. [`docs/hafiza/frontend.md`](hafiza/frontend.md) — arayüze dokunuyorsunuz
5. Bu doküman

---

## Faz 29'dan Devralınanlar

Bu fazın **çevireceği en yeni ekran** konuşma modudur (Faz 29). Bilinmesi
gerekenler:

| Konu | Devralınan durum |
|---|---|
| Bundle | **124,9 KB gzip / 250 KB bütçe.** Faz 29 +2,4 KB ekledi; i18n sözlükleri buraya girer |
| Yeni bileşen | `components/voice-panel.tsx` — mikrofon, ölçer, altyazı, "Send now", "Interrupt", kayıt rozeti. **Tüm metinleri İngilizce sabittir** |
| Yeni saf modül | `lib/voice.ts` — `microphoneSupport()` kullanıcıya **gösterilen** bir gerekçe metni döndürür; o metin de çevrilmelidir |
| Yeni ekran metni | Playground'daki mikrofon düğmesi ve `voice-unsupported` uyarısı |

🚨 **Ses seçimi dile bağlıdır.** Türkçe bir yanıt İngilizce bir sesle
seslendirilirse sonuç anlaşılmaz olur. `VoiceConversationOptions.VoiceId`
bugün **tek bir ses** taşır; dil başına ses eşlemesi bu fazın işidir. İki yol
var ve seçim bu fazda yapılmalıdır:

1. `VoiceId`'yi bir sözlüğe çevirmek (`Dictionary<string, string>` dil→ses) —
   sunucu tarafı değişikliği, `VoiceConversationOptions` kırıcı olmayan bir
   genişleme
2. İstemcinin `start` mesajında `voiceId` göndermesi — protokol **zaten
   destekliyor** (`VoiceClientMessage.VoiceId`), sunucu değişikliği gerekmez;
   sesi arayüz seçer (`GET /api/voice/voices` ile listelenir)

**Öneri: 2.** Sunucuya dil bilgisi taşımaz ve mevcut protokolü kullanır.

🚨 **`t(...)` çağrısı `voice-panel.tsx` içinde dikkatli kullanılmalıdır.** Panel
`useCallback` bağımlılık dizileri taşır; `t` fonksiyonu her dil değişiminde yeni
bir referans olursa `start`/`stop` yeniden kurulur ve **açık bir WebSocket
bağlantısı kopabilir**. `t`'yi kararlı bir referans olarak dışa açın veya metni
render sırasında çözün.

---

## Neden En Sonda

i18n'in maliyeti kurulum değil, **bakımdır**: her yeni ekran metni iki dilde
yazılmak zorundadır. Faz 8–29 arasında yaklaşık on yeni ekran ve yüzlerce yeni
metin gelir. i18n'i başa koymak, her fazı yavaşlatırdı.

Karar defterindeki kalem şöyle diyordu:

> *"Somut bir çok dilli talep gelirse `lib/` altına sözlük + hook eklenir."*

Kullanıcı bu talebi verdi. Karar **yeniden açılır**, satır silinmez; sonuna
"(yeniden açıldı: …)" notu eklenir.

---

## 30.1 — i18n (F-25)

### Kütüphane alınmaz

`react-i18next` + `i18next` ~15–25 KB gzip'tir ve çoğul kuralları, ad alanları,
gecikmeli yükleme gibi ihtiyacımız olmayan yetenekler taşır.

Gereken şey ~80 satırdır:

```typescript
// lib/i18n.ts
type Locale = "en" | "tr";
type Messages = Record<string, string>;

export function t(key: string, params?: Record<string, string | number>): string;
export function useLocale(): { locale: Locale; setLocale(next: Locale): void };
```

- Sözlükler `locales/en.ts` ve `locales/tr.ts` — düz nesne, **tip güvenli**:
  `tr` sözlüğü `typeof en` tipinde olur, eksik anahtar **derleme hatasıdır**
- Çoğul: `t("runs.count", { n })` için basit `n === 1 ? tekil : çoğul` seçimi.
  Türkçe ve İngilizce için yeterlidir; daha karmaşık diller gerekirse yeniden
  değerlendirilir
- Tarih ve sayı biçimlendirmesi `Intl.DateTimeFormat` / `Intl.NumberFormat` ile —
  tarayıcıda vardır, paket gerekmez
- Dil seçimi: `navigator.language` → `localStorage` tercihi → varsayılan `en`

> **Neden `localStorage`, token `sessionStorage`'dayken (K-047)?** Dil bir sır
> değildir; kullanıcı tercihi sekmeler arası ve kalıcı olmalıdır.

### Kapsam

| Çevrilir | Çevrilmez |
|----------|-----------|
| Arayüz metinleri, düğmeler, başlıklar, boş durum mesajları | Agent adları, tool adları, model adları |
| Doğrulama ve hata **başlıkları** | Sunucudan gelen hata **detayları** |
| Tarih/sayı biçimi | JSON yükleri, günlükler |

🚨 **Sunucu yanıtları çevrilmez.** `ProblemDetails` metinleri İngilizce kalır;
paket NuGet.org'a uluslararası yayınlanır ve API sözleşmesi tek dildir. Arayüz,
bildiği hata kodları için kendi çevirisini gösterir; bilmediğini olduğu gibi
yazar.

### Bütçe

Hedef: **+6 KB gzip'ten az** (iki sözleşme sözlüğü + altyapı). Ölçüm DoD'ye
yazılır. Sözlükler büyürse üçüncü bir dil eklenmeden önce gecikmeli yükleme
değerlendirilir.

---

## 30.2 — Komut Paleti ve Kısayollar (F-26)

Konsol deneyimini hızlandırır, maliyeti düşüktür.

### Komut paleti

`Ctrl/Cmd + K` ile açılır. Kaynaklar:

| Kaynak | Örnek |
|--------|-------|
| Gezinme | "Runs", "Agents", "Dashboard" |
| Agent'lar | Ada göre arama → agent detayına git |
| Çalıştırmalar | Kimliğin ilk 8 karakteri ile doğrudan gitme |
| Eylemler | "Yeni agent", "Playground'da çalıştır", "Temayı değiştir", "Dili değiştir" |

- Bulanık (fuzzy) eşleme ~40 satır; kütüphane alınmaz
- Sonuçlar **yetkiye göre** filtrelenir (Faz 9): Reader "Yeni agent" görmez
- Erişilebilirlik: `role="dialog"`, odak tuzağı, `Esc` ile kapanma, ok tuşlarıyla
  gezinme, `aria-activedescendant`

### Kısayollar

| Kısayol | Eylem |
|---------|-------|
| `Ctrl/Cmd + K` | Komut paleti |
| `g` sonra `a` / `r` / `s` / `d` | Agents / Runs / Sessions / Dashboard |
| `/` | Geçerli listede aramaya odaklan |
| `Ctrl/Cmd + Enter` | Playground'da çalıştır |
| `Esc` | Açık katmanı kapat |
| `?` | Kısayol yardımı |

Kurallar:

- Bir metin alanına yazarken kısayol **tetiklenmez** (`input`, `textarea`,
  `contenteditable` denetimi)
- Tarayıcının kendi kısayolları ezilmez
- Tümü tek bir `lib/shortcuts.ts` içinde toplanır ve Vitest ile test edilir

---

## 30.3 — Ek Erişilebilirlik Denetimi

Bu faz arayüze son kez toplu dokunuştur; birikmiş küçük eksikler burada kapanır:

- Tüm etkileşimli öğelerde klavye erişimi ve görünür odak halkası
- Renk kontrastı WCAG AA (açık ve koyu tema)
- SSE ile akan içerik için `aria-live="polite"`
- Grafiklerde (Faz 20) metin alternatifi
- `lang` niteliği seçilen dile göre güncellenir

---

## Testler

| Proje | Yeni test |
|-------|-----------|
| Frontend (Vitest) | `t()` yer tutucu değiştirme; eksik anahtar davranışı; çoğul; kısayol ayrıştırma; metin alanında kısayol **tetiklenmemesi**; bulanık eşleme sıralaması |
| TypeScript derlemesi | `tr` sözlüğünde eksik anahtar **derlemeyi kırar** (tip düzeyinde kanıt) |
| `AgentPrism.Ui.E2ETests` | Dil değişimi ve kalıcılığı; komut paleti ile gezinme; `g a` kısayolu; rol bazlı komut filtresi |

---

## Bu Fazda Verilecek Kararlar

1. **i18n kararı yeniden açıldı** — karar defterindeki satır silinmez, not eklenir.
2. **i18n kütüphanesi alınmadı** — elle sözlük + hook (K-045 gerekçesi).
3. **Sözlükler tip güvenlidir** — eksik çeviri derleme hatasıdır.
4. **Sunucu yanıtları çevrilmez** — API sözleşmesi tek dildir.
5. **Dil tercihi `localStorage`'da** — sır değildir (K-047 ile çelişmez).

---

## Açık Sorular

1. **Varsayılan dil ne olsun?** `navigator.language` Türkçe ise Türkçe açmak
   doğal; ama ekran görüntüleri ve destek İngilizce. Öneri: **tarayıcı dilini
   kullan**, ilk açılışta dil değiştirme ipucu göster.
2. **Türkçe teknik terimler çevrilsin mi?** ("run" → "çalıştırma", "tool" →
   "araç"?) Öneri: **arayüz metni çevrilir, teknik terimler
   korunur** — `tool`, `agent`, `token` olduğu gibi kalır; depo dokümanlarının
   kuralı budur.
3. **Üçüncü bir dil beklensin mi?** Öneri: mimari destekler ama **eklenmez**;
   talep gelirse eklenir.
4. **Komut paleti sunucu tarafı arama yapsın mı?** Büyük kataloglarda istemci
   tarafı arama yetmez. Öneri: **v1 istemci tarafı**, katalog 200 kaydı aşarsa
   sunucu araması eklenir.

---

## Bitiş Ölçütleri (DoD)

- [ ] Arayüz Türkçe ve İngilizce çalışıyor; dil tercihi kalıcı
- [ ] Eksik çeviri **derlemeyi kırıyor** (kanıt: bilerek eksik bırakılan
      anahtarla derleme hatası)
- [ ] Komut paleti çalışıyor; rol bazlı filtreleme doğru
- [ ] **Konuşma modu iki dilde çalışıyor** ve seslendirme dile uygun sesle yapılıyor
- [ ] Kısayollar çalışıyor ve metin alanlarında tetiklenmiyor
- [ ] Klavye ile tüm ekranlar gezilebiliyor; odak görünür
- [ ] Bundle ölçüldü ve bütçe içinde (hedef: toplam **120 KB gzip altı**)
- [ ] Dört doğrulama kapısı sıfır uyarı

---

## Riskler

| Risk | Önlem |
|------|-------|
| Çeviri bakımı yükü | Tip güvenli sözlük; eksik anahtar derlemede yakalanır |
| Bundle büyümesi | Kütüphane yok; ölçüm DoD'de |
| Kısayol çakışması | Metin alanı denetimi; tarayıcı kısayolları ezilmez |
| Çeviri kalitesi | Türkçe metinler ASD-STE100 sadeliğinde yazılır; teknik terim korunur |
| Sonraki fazlar çeviriyi unutur | Eksik anahtar derlemeyi kırdığı için **unutulamaz** |

---

## Sonraki Faza Devir Notu

- Bu faz ikinci faz planının **son** kalemidir. Bundan sonrası ya
  [Faz 7 (yayın)](07-SAGLAMLASTIRMA-VE-YAYIN.md) ya da yeni bir beyin fırtınası
  turudur.
- i18n açıldıktan sonra **yeni her ekran iki dilde yazılır**. Bu kural
  `AGENTS.md`'ye ve `faz-tamamlama` skill'ine eklenmelidir.
- Bundle bütçesi bu noktada gözden geçirilmelidir: 250 KB sınırının ne kadarı
  kullanıldı, kalan pay yeni ekranlar için yeterli mi?

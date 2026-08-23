---
name: faz-planlama
description: Bir aday yeteneği (F-NN) uygulanabilir bir faz dokümanına (docs/NN-*.md) dönüştürür — kanıt doğrulama, kapsam sınırları, açık soruların sorulması, devir teslim kalitesinde plan yazımı ve yol haritasına kaydetme. AgentPrism'de fazlar ayrı oturumlarda uygulanır; plan dokümanı tek başına yeterli değilse o oturum yanlış kod yazar. Aday listesi hazırlamak için değil, seçilmiş bir adayı plana çevirmek için kullanılır.
---

# Faz Planlama Protokolü

Bu skill, **seçilmiş bir aday yetenek** (`docs/ADAYLAR.md` içindeki
bir F-NN kalemi) uygulanabilir bir faz dokümanına dönüştürülürken çalıştırılır.

Yerini bilin — zincir beş halkadır:

```
aday listesi (F-NN) → [faz-planlama] → docs/NN-*.md → [faz-baslangic] → [faz-uygulama] → [faz-denetim] → [faz-tamamlama]
```

Amaç tek şeydir: **plan dokümanı, onu hiç görmemiş bir oturumda tek başına
yeterli olsun.** AgentPrism fazları ayrı sohbetlerde uygulanır. Plan eksikse o
oturum ya yanlış kod yazar ya durup sorar; ikisi de pahalıdır.

**Bu skill kod yazmaz.** Tek çıktı bir dokümandır.

---

## Adım 0 — Girdiyi netleştir

Kullanıcıdan üç şey gelmeli. Gelmediyse **sor**, varsayma:

| Ne | Neden gerekir |
|---|---|
| Hangi F-NN kalem(ler)i? | Bir faz bir veya iki kalemi kapsar. Üç kalem bir faz değil, bir turdur |
| Faz numarası | Sıradaki boş numara. `ls docs/[0-9][0-9]-*.md \| tail -1` |
| Bu tur bir yol haritasına mı bağlanacak? | Bağlanacaksa yol haritası dosyası da güncellenir |

İki kalem tek fazda ancak **aynı altyapıyı** paylaşıyorsa birleşir. Faz 21
(kota + webhook) böyle yapıldı; ikisi de "AgentPrism'i dış dünyayla sözleşmeye
bağlar" işiydi. Ortak yanı olmayan iki kalemi tek faza koymak DoD'yi bulanık
yapar.

### Reddedilmiş işi yeniden önerme

Yazmaya başlamadan **önce** karar defterine bak. Kapatılmış tartışma yeniden
açılmaz:

```bash
grep -n "<konu>" docs/arsiv/KARARLAR-INDEKS-REDDEDILEN.md docs/KARARLAR-INDEKS.md
grep -n "K-0NN" docs/KARARLAR.md      # bulduğun kalemin tam gerekçesi
```

`KARARLAR-INDEKS-REDDEDILEN.md` (K-214'ün "yeniden açılma koşulu" gereği Faz
32'de `KARARLAR-INDEKS.md`'den ayrıldı) bir kontrol listesidir. Planladığın iş
oradaysa ya plandan çıkar ya da **kararı neyin değiştirdiğini** kanıtla yaz.

---

## Adım 1 — 🚨 Kanıtı yeniden doğrula

**Bu adım atlanmaz ve en çok bedel ödeten adımdır.**

Aday listesindeki kod kanıtları yazıldıkları gün doğruydu. Repo ilerledikçe
satır numaraları kayar, delik kapanır, iddia yanlışlanır.

**Ölçüldü (2026-08-06).** 2026-08-05 tarihli aday listesinin üç iddiası bir gün
sonra yanlış çıktı:

| İddia | Gerçek |
|---|---|
| "F-43 en acil kalem" | **Tamamlanmıştı.** `ModelBinding.ProviderSettings` sözleşmedeydi (K-208) |
| "`RunStatus.Canceled`'ı hiçbir kod yazmıyor" | **Üç yer yazıyordu** (`RunRecordingAgent.cs:186`, `:251`, `WorkflowRunner.cs:463`) |
| "Cron bu olmadan N kez tetiklenir" | **Zaten kapalıydı** — `0008_scheduling.sql:46` benzersiz kısıtı (K-138) |

Bu üçü doğrulanmasaydı, bir faz **var olmayan bir deliği** kapatmak için
planlanacaktı.

Her kanıt için üç soru:

1. **Dosya ve satır hâlâ orada mı?** `grep -n "<sembol>" <dosya>`
2. **İddia hâlâ doğru mu?** "Hiç kullanılmıyor" diyorsa `grep -rn` ile ara —
   yazan bir yer bulursan iddia düşmüştür.
3. **Karar defteri bu deliği kapatmış mı?** `grep -n "<konu>" docs/KARARLAR-INDEKS.md`

Doğrulanmayan kanıt plana **yazılmaz**. Kanıtı düşen kalem ya kapsam dışına
çıkar ya gerekçesi yeniden yazılır — ikisi de dokümanda açıkça belirtilir.

---

## Adım 2 — Kapsam sınırlarını uygula

Bu sınırlar tartışmaya açık değildir. Bir plan bunlardan birini ihlal ediyorsa
plan yanlıştır.

### Değişmez tasarım kuralları

| Kural | Planda ne demek |
|---|---|
| **K1 — sıfır sürpriz** | Her yeni genişleme noktası **varsayılan kapalı** gelir. Plan bunu açıkça yazar |
| **K2 — tool'lar yalnız kodda** | Arayüzden çalıştırılabilir kod/ifade tanımlatan hiçbir tasarım kabul edilmez. Şablon dili bile yalnız **değer yerleştirme** olabilir |
| **K3 — MAF sarmalanmaz** | `AIAgent`, `AgentSession`, `ChatMessage`, `AIFunction` doğrudan kullanılır. Paralel tip hiyerarşisi kurulmaz |
| **K4 — her nokta değiştirilebilir** | Kayıt `TryAdd*` ile. Tüketicinin kaydı kazanır |

### Maliyet sınırları — plan bunları **rakamla** yazar

| Konu | Kural |
|---|---|
| **Yeni NuGet paketi** | K-007 gerekçesi ister. Geçişli ağırlığı **say** (Faz 27: 37 paket → ertelendi, K-212) |
| **Yeni AgentPrism paketi** | `faz-tamamlama`'daki paket kontrol listesi: README, slnx, meta paket, `DependencyDirectionTests` |
| **Ön sürüm MAF paketi** | Yalnız `AgentPrism.AspNetCore` içinde (K-008). Başka yere koyacaksan bu bir karardır |
| **Yeni tablo** | **Üç migration seti** — PostgreSQL + SqlServer + Sqlite. Numaralar sağlayıcı başına bağımsızdır (K-178) |
| **Migration numarası** | Plan numara **rezerve etmez**. Uygulama anında bir sonraki boş numara alınır |
| **Arayüz işi** | Bundle payını **gzip KB olarak** yaz. Bütçe 250 KB; bugünkü kullanımı ölç: `ls -l src/AgentPrism.UI/wwwroot/assets/` |
| **Yeni ekran metni** | `locales/en.ts` + `tr.ts`. Eksik anahtar **derleme hatasıdır** (K-228) |
| **Sunucu yanıtı** | Çevrilmez; API sözleşmesi tek dillidir (K-232) |
| **Kullanıcıya dönük yüzey** | Plan başlığındaki **Tüketici yüzeyi** satırı iki eksende yazılır: hangi `docs-site/` sayfası değişecek **ve** hangi sevk edilen yapıt (XML `<example>`, paket `README.md`'si, `capabilities.md` satırı). `api/` ve `http-api/` üretilir — orada iş XML dokümanı ve `.WithTags`/`.Produces` üstverisidir. Sözleşme: [`tuketici-dokuman-senkronu`](../tuketici-dokuman-senkronu/SKILL.md) |
| **`secret`** | Dosyaya **ve veritabanına** yazılmaz. Kayıtta yalnız yapılandırma anahtarının **adı** durur (K-059) |
| **AOT** | `Abstractions`, `Core`, `PostgreSql`, `OpenAI` AOT uyumlu kalır. Yansımaya dayanan tasarım bu paketlere giremez |
| **Public API** | `EnablePublicApiTracking` **`true`**'dur (K-421) ama `PublicAPI.Shipped.txt` dosyaları **boştur** — yüzeyi büyüten kalem Faz 7'den **önce** hâlâ ucuzdur, sonra bir sürüm kararıdır. Plan bunu bir cümleyle söyler ve iddiayı `wc -l src/*/PublicAPI.Shipped.txt` ile **ölçer** |

### Sözleşme değişikliği kırıcı mıdır?

`ModelBinding` gibi `sealed record` public tiplere alan eklemek Faz 7'den sonra
kırıcıdır. Plan, dokunduğu her public tip için şunu yazar: **"bugün eklemek
bedava / sonradan eklemek kırıcı"**.

---

## Adım 3 — Belirsizlikleri kullanıcıya sor — yazmadan **önce**

Bir tasarım kararı iki farklı okunabiliyorsa **varsayma**. İkisini de tarif et,
maliyet ve riski ayrı yaz, seçimi kullanıcıya bırak.

Tipik ikilikler:

- Hafif mi ağır mı? (kuyrukta baştan `run` **vs** kontrol noktasından devam)
- Test kapısı mı veritabanı kapısı mı? (sözleşme testi **vs** PostgreSQL RLS)
- Yeni tablo mu mevcut tabloya sütun mu?
- Yeni paket mi mevcut pakete ek mi?
- Genişleme noktası mı yerleşik uygulama mı?

Cevabı olmayan ama **planı bloklamayan** soru "Açık Sorular" bölümüne yazılır ve
faz uygulanırken karara bağlanır. Planı bloklayan soru **şimdi** sorulur.

> Soru sorarken seçenekleri kısa tut ve bir tanesini öner. "Ne yapalım?" değil,
> "A mı B mi, ben A öneriyorum çünkü…" diye sor.

---

## Adım 4 — MAF tipi kullanacaksan imzayı doğrula

Plan bir MAF, `Microsoft.Extensions.AI` veya `ModelContextProtocol` tipine
dayanıyorsa imzayı **tahmin etme**. `maf-api-kesfi` skill'ini çalıştır.

Doğrulayamadığın her iddia plana şu biçimde yazılır:

> **Doğrulanmadı — `maf-api-kesfi` ile ölçülmeli.**

Bu cümle bir eksiklik değil, bir devir teslim bilgisidir. Uygulayan oturum onu
ilk iş olarak ölçer. Tahmin edilmiş bir imza ise sessizce yanlış koda dönüşür —
plan `AgentRunResponse` demişti, gerçek tip `AgentResponse` çıktı.

---

## Adım 5 — Dokümanı yaz

Şablon: [`resources/faz-plani-sablonu.md`](resources/faz-plani-sablonu.md).

Dosya adı: `docs/NN-BUYUK-HARFLI-AD.md` — Türkçe, tire ile ayrılmış, kısa.
Örnek: `docs/31-CALISTIRMA-IPTALI-VE-UZLASTIRMA.md`.

Şablonun iki tür bölümü vardır ve **karıştırılmaz**:

| Bölüm | Kim doldurur |
|---|---|
| Başlık · Bu Faza Başlarken · Amaç · Tasarım · Planlanan Public API · Planlanan Dosya Listesi · Hata Modları ve Testler · Manuel Kabul Case'leri · DoD · Riskler · Açık Sorular | **Bu skill** (plan anı) |
| Plandan Sapmalar · Bu Fazda Verilen Kararlar · Gerçekleşen Public API · Dosya Listesi (gerçekleşen) · Denetim Bulguları · Sonraki Faza Devir Notu | **`faz-tamamlama`** (kapanış anı) |

İkinci gruptaki başlıkları **boş yer tutucu olarak** bırak. Kapanışta
doldurulacaklarını yazan bir satır koy. Böylece kapanış adımı unutulmaz.

### Yazım kuralları

- Türkçe, ASD-STE100. Kısa cümle, tek fikir, aktif çatı.
- Her diyagram **Mermaid**. ASCII kutu çizimi yasak — istisnası dizin ağacıdır.
- Ölçmediğin süreyi, yüzdeyi, oranı yazma. Ölçmediysen "ölçülmeli" yaz.
- Kod kanıtı `dosya:satır` biçiminde ve **Adım 1'de doğrulanmış** olmalıdır.
- Pazarlama dili yok.
- 🚨 işaretini yalnız **gerçek tuzak** için kullan; enflasyon işareti öldürür.

### 🚨 Testi hata modundan türet, mutlu yoldan değil

Plan "Testler" tablosu yerine bir **hata modu tablosu** yazar: ne bozulabilir ×
hangi **seviyede** yakalanır. Seviyeyi plan seçer, uygulayan oturum değil —
çünkü yanlış seviye seçimi bu repoda kusurların ana kaynağıdır.

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| Kota sayacı eşzamanlı `run`'da kayar | Fonksiyonel | `QuotaConcurrencyTests` |
| Başka kiracının kotası görünür | Sözleşme (`TenantIsolationContract`) | dört koşumda birden |
| `store` yazamazsa `run` durur | Fonksiyonel | `QuotaStoreFailureTests` |

Seviye kuralı: bir davranış **sınır** geçiyorsa (DI · HTTP · kiracı · akış ·
depo · paket) birim testi onu kanıtlamaz. Ayrıntı: `faz-uygulama` Adım 2.

Her yeni kod yolu için beş soru sorulur ve cevabı tabloya girer: iptal ·
eşzamanlılık · boş/aşırı girdi · başka kiracı · alt sistem hatası.

### DoD gerçekten ölçülebilir olmalı

"Kota çalışıyor" bir bitiş ölçütü değildir. Bunun yerine:

- [ ] `POST /api/agents/x/run` kota dolduğunda `429` ve `Retry-After` döner
- [ ] Dört doğrulama kapısı sıfır uyarı
- [ ] `samples/AgentPrism.Api` üzerinde gerçek çıktı alındı (komut + beklenen yanıt)

Her faz DoD'sinde şu **beş** satır her zaman bulunur:

1. Dört doğrulama kapısı sıfır uyarı
2. `samples/AgentPrism.Api` ile gerçek `run` — çıktı belgeye yazıldı
3. `secret` taraması boş döndü
4. Fazın manuel kabul case'leri `docs/manuel-test/<alan>` içine eklendi ve otomatikleştirilebilenler koşuldu
5. `faz-denetim` koşuldu; 🔴 bulgu kalmadı

Gerekçe: birim testleri Faz 6, 12, 15, 16, 18, 20, 21 ve 28'de gerçek hataları
**kaçırdı**; hepsi yalnız örnek uygulamada ortaya çıktı.

---

## Adım 6 — Kaydet

Plan dokümanı tek başına yetmez; yol haritasına bağlanmalıdır. Faz durumu **iki
yerde tekrarlanmaz**.

| Dosya | Ne yapılır |
|---|---|
| `docs/NN-*.md` — `> **Durum:**` satırı | `📋 Planlandı (YYYY-AA-GG)`. `docs/YOL-HARITASI.md` bundan **üretilir**; elle satır ekleme (K-413) |
| Turun yol haritası dosyası | Sıra tablosuna satır: kalem, neden burada, yeni paket, migration |
| `docs/ADAYLAR.md` | Plana dönüşen kalemin **bölümünü sil**; hangi faza gittiğini tek satırla yaz |
| `AGENTS.md` | Yalnız "sıradaki faz" satırı değiştiyse. Tam liste **yalnız üretilen `docs/YOL-HARITASI.md`'dedir** |

Kalemi aday listesinde bırakma. İki yerde tutmak kayma üretir — belge zaten bu
kuralı yazıyor.

Sonra bakım komutunu çalıştır:

```bash
python3 scripts/dokuman-bakim.py
```

Çıkış kodu 0 olmalıdır. Bütçe aşıldıysa içerik **silinmez**; birikimli kısım
`docs/arsiv/` veya `docs/hafiza/` altına taşınır.

---

## Adım 7 — Kapanış kontrolü

Üç soruya dürüst cevap ver:

> 1. Bu repo'yu hiç görmemiş bir oturum, `faz-baslangic`'in sabit okuma kümesiyle
>    (`AGENTS.md` + `MEMORY.md` + bu doküman) fazı doğru başlatabilir mi?
> 2. Planın dayandığı her kod kanıtı **bugün** doğrulandı mı?
> 3. Planı bloklayan her belirsizlik kullanıcıya soruldu mu?

1'e "hayır" ise eksik bilgiyi **fazın kendi dokümanına** yaz, sıcak dokümana
değil.

---

## Yazılmayacaklar

Plan dokümanına **girmez**:

- **K-NNN numarası rezervasyonu.** Numarayı kararı gerçekten veren faz alır.
- **Uydurma ölçüm.** "%40 hızlanır", "2 saat sürer" — ölçülmediyse yazılmaz.
- **Doğrulanmamış MAF imzası.** "Ölçülmeli" yaz, tahmin etme.
- **Gerçekleşen sonuçlar.** Plan anında hiçbir şey gerçekleşmemiştir.
- **Kod.** Bu skill kod yazmaz; imza taslağı yazar.
- **Birikimli anlatı.** "Faz N sonunda…" cümleleri `docs/arsiv/`'e aittir.
- **Aday listesinin tekrarı.** Kalem plana taşındıysa listeden silinir.

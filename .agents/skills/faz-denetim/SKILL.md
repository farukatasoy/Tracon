---
name: faz-denetim
description: Fazın kodu bittiğinde, kapanıştan önce çalıştırılan bağımsız denetim — taze bağlamlı bir denetçi yalnız DoD ve git diff'e bakarak DoD ihlallerini, test tiyatrosunu ve kapsanmayan hata yollarını arar. AgentPrism'de kodu yazan agent kendi işini yargılıyordu; bu skill o kör noktayı kapatır. Denetçi kod yazmaz, yalnız bulgu üretir.
---

# Faz Denetim Protokolü

Zincirdeki yeri:

```
faz-baslangic → faz-uygulama → [faz-denetim] → faz-tamamlama
```

Amaç tek şeydir: **kodu yazan gözün göremediğini görmek.**

Bir agent kendi kodunu, kendi testleriyle, kendi DoD yorumuna göre yargıladığında
aynı varsayım iki kez kullanılır. Bu repoda bunun bedeli ölçüldü:

| Vaka | Ne oldu |
|---|---|
| Faz 6, 12, 15, 16, 18, 20, 21, 28 | Gerçek kusurlar **testlerden geçti**; yalnız örnek uygulamada çıktı (K-166, K-167) |
| Faz 20 | `Cost = cost` yazılmadı; **1068 test** yakalamadı |
| Faz 48 (K-320) | Planın yapısal iddiası yanlıştı; kod derlendi, testten geçti, yanlış konumdaydı |
| Faz 57 (K-411) | Senkronizasyon kopyaları commit edildi; `main` derlenmedi |

Dördü de **taze bir gözün diff'e bakmasıyla** yakalanabilirdi.

---

## Adım 1 — Denetçiyi çağır

Denetimi **uygulayan oturum başlatır**, kod bittikten ve örnek uygulama
koşulduktan sonra, `faz-tamamlama`'nın doküman adımlarından **önce**.

Denetçi **taze bağlamlı ayrı bir agent**tır. Kararın gerekçesi: uygulayan
oturumun bağlamında fazın her tasarım tercihi zaten haklı görünür.

Çağrı biçimi (Claude Code'da `Agent` aracı, `general-purpose` tipi):

```
Sen AgentPrism'in faz denetçisisin. .agents/skills/faz-denetim/SKILL.md
dosyasını oku ve olduğu gibi uygula.

Faz dokümanı: docs/<NN>-<AD>.md
Denetlenecek değişiklik: <git diff aralığı veya "çalışma ağacı">

Kod YAZMA. Yalnız bulgu üret ve Adım 4'teki biçimde raporla.
```

> Alt agent mekanizması olmayan bir ortamda denetim **ayrı bir sohbette** koşar.
> Aynı oturumda "şimdi denetçi gibi düşün" demek bu skill'i uygulamak
> **değildir** — bağlam taze olmadığı için kör nokta korunur.

---

## Adım 2 — Denetçinin göreceği ve görmeyeceği şeyler

Denetçi şunları okur:

1. Bu dosya
2. Fazın dokümanı — özellikle **Bitiş Ölçütleri (DoD)** ve **Planlanan Public API**
3. Değişikliğin tamamı:
   ```bash
   git diff --stat <taban>...HEAD
   git diff <taban>...HEAD
   ```
4. Gerektiği kadar kaynak kod ve test

Denetçi şunları **okumaz**: `KARARLAR.md`'nin tamamı, `docs/arsiv/`, geçmiş faz
dokümanları. Denetim ucuz olmalıdır; pahalı olursa atlanır.

### 🚨 Dokümanın iddiasına güvenme

Faz dokümanı "✅ yapıldı" diyorsa bu bir **iddiadır**, kanıt değil. Her DoD
satırı için kanıtı **kodda veya testte** bul. Bulamıyorsan bulgu yaz.

---

## Adım 3 — Denetim listesi

Sekiz başlık. Her biri için bulgu ya vardır ya "temiz" yazılır.

### 3.1 DoD ihlali
Her DoD satırını tek tek al. Karşılığı olan kod veya test hangisi? Satır
ölçülebilir değilse ("kota çalışıyor") bu **kendisi** bir bulgudur.

### 3.2 Test tiyatrosu
- Hiçbir şey iddia etmeyen test (`Should` çağrısı yok, yalnız "patlamadı")
- Taklidin kendisini doğrulayan test (mock'a yazılan değeri mock'tan okumak)
- Test edilen davranışı taklitle **devre dışı bırakan** kurulum
- `ShouldBe` yerine sıralamaya bağlı küme karşılaştırması (`HashSet.ShouldBe` **sıralı** eşitlik denetler)

### 3.3 Yanlış test seviyesi
Bir sınırı (DI · HTTP · kiracı · akış · depo · paket) geçen davranış yalnız
birim testiyle mi kanıtlanmış? `faz-uygulama` Adım 2 tablosuna göre bak.

### 3.4 Kapsanmayan hata yolları
Beş soru, her yeni kod yolu için: iptal · eşzamanlılık · boş/aşırı girdi ·
**başka kiracının** kaydı · alt sistem hatası (`store` yazamıyor).

Gözlemlenebilirlik özel kural taşır: `run` kaydı `store`'u hata verirse `run`
**devam etmelidir**. Bunu doğrulayan test var mı?

### 3.5 İmza-gövde kayması
Diff bir imzaya alan/parametre eklediyse, o alanı **yazan** her kod yolu var mı?

```bash
grep -rn "new <Kayıt>\b" src/      # her üretim noktası
grep -rn "<Metot>(" src/           # her çağıran
```

### 3.6 Plan dışı public API
Diff public yüzeyi büyütüyorsa, fazın "Planlanan Public API" bölümünde var mı?
Yoksa gerekçe faz dokümanına yazılmış mı? `EnablePublicApiTracking` açıktır;
derleyici plan dışı public API'yi **uyarır**.

### 3.7 Repo kuralları
- Kod, yorum, XML doküman, `exception`/log metni **İngilizce** mi?
- Her public üye XML dokümanına sahip mi?
- `TryAdd*` ile mi kaydedilmiş? Tüketicinin kaydı kazanıyor mu?
- Yeni genişleme noktası **varsayılan kapalı** mı? (K1)
- MAF tipi sarmalanmış mı? (K3 — sarmalanmamalı)
- `secret` dosyaya veya veritabanına yazılıyor mu? (K-059)
- `ValueTask` dönen arayüzlerde `ConfigureAwait(false)` var mı?

### 3.8 Ürün yüzeyi
- Yeni ekran metni `en.ts` **ve** `tr.ts`'de mi, gerçekten çevrilmiş mi?
- Yeni HTTP ucu `.WithTags`/`.Produces` üstverisi taşıyor mu? (OpenAPI ve site oradan üretilir)
- Kullanıcıya dönük davranış değiştiyse `docs-site/` güncellenmiş mi?
- Fazın manuel kabul case'leri `docs/manuel-test/` içine eklenmiş mi?

**Tüketici doküman sözleşmesi.** Standart
[`tuketici-dokuman-senkronu/resources/kalite-sozlesmesi.md`](../tuketici-dokuman-senkronu/resources/kalite-sozlesmesi.md)
içindedir; denetçi onu okur ve şunları arar:

- 🔴 **Bir muafiyet listesi veya taban çizgisi büyüdü mü?** `DIAGRAM_EXEMPT`,
  `CLOSING_EXEMPT`, `SourceLanguageTests` taban çizgisi ve kontrast tabanları
  **yalnız iyileşir**. Büyüten bir değişiklik gerekçeli olsa bile 🔴'dır —
  gerekçe kararı `docs/KARARLAR.md`'ye taşır, denetimi kapatmaz.
- 🔴 **Ağırlık tavanı veya kontrast tabanı ölçümsüz mü değiştirildi?** Sayı
  uydurulmaz; tavanı yükseltmek ölçüm ister.
- Yeni elle yazılan sayfa sözleşmeyi taşıyor mu: `## Read next` (1–3 bağlantı),
  `description` 70–180 karakter, kenar çubuğundan erişilebilir?
- Sevk edilen metinde iç referans kaldı mı: `K-NNN`, `F-NN`, faz numarası,
  `MT-*`, `docs/NN-*.md`? Sınır `///` ile `//` arasındadır — uygulama yorumunda
  referans **meşrudur**.
- Yeni giriş noktası (`Add*`, `Use*`, `Map*`) `<example>` taşıyor mu? Yerel
  referans dosyasının reçetesi bu sözü verir.
- Yeni yetenek veya yeni paket `capabilities.md`'ye girdi mi? Sevk edilen agent
  haritasının **tek kaynağı** odur ve bugün hiçbir kapı bu boşluğu yakalamıyor.
- Kapı seti tam koştu mu? `npm run build` **yetmez** — `npm run check` dördünü
  (`check:content` · `build` · `check:links` · `check:weight`) koşar.

---

## Adım 4 — Bulguları üç seviyede raporla

Her bulgu **tek bir cümlelik iddia** + **dosya:satır** + **nasıl kırılır**
taşır. Gerekçesiz bulgu yazma; "daha iyi olurdu" bir bulgu değildir.

```markdown
## Denetim — Faz <NN> (<YYYY-AA-GG>)

### 🔴 Kapanmadan faz bitmez
| # | Bulgu | Kanıt | Nasıl kırılır |
|---|---|---|---|
| 1 | <tek cümle> | `dosya.cs:120` | <somut girdi → yanlış çıktı> |

### 🟡 Aynı fazda kapanır veya gerekçelenir
| # | Bulgu | Kanıt | Öneri |
|---|---|---|---|

### 🟢 Aday listesine
| # | Bulgu | Neden şimdi değil |
|---|---|---|

**Temiz çıkan başlıklar:** 3.1, 3.5, 3.7
```

Seviye tanımları:

| Seviye | Anlamı | Sonuç |
|---|---|---|
| 🔴 | DoD ihlali, kusur, repo kuralı ihlali, güvenlik sınırı | Kapanmadan faz **bitmez** |
| 🟡 | Test boşluğu, yanlış seviye, eksik hata yolu | Aynı fazda kapanır **veya** gerekçesi faz dokümanına yazılır |
| 🟢 | İyileştirme, kapsam dışı gözlem | `docs/ADAYLAR.md`'ye F-NN olarak |

Bulgu yoksa bunu açıkça yaz: **"🔴 ve 🟡 yok."** Sessiz rapor, denetim
yapılmadığından ayırt edilemez.

---

## Adım 5 — Uygulayan oturum bulguları kapatır

Denetçi **kod yazmaz**. Rolleri ayırmanın sebebi: düzelten göz yeniden yazan
göz olur ve denetim bağımsızlığı biter.

Uygulayan oturum her bulgu için üç şeyden birini yapar:

1. **Düzeltir** — ve düzeltmeyi kanıtlayan testi ekler
2. **Gerekçeler** — faz dokümanına, neden bulgunun geçerli olmadığını yazar
3. **Devreder** — yalnız 🟢 için; aday listesine F-NN olarak

Sonuç faz dokümanının **"Denetim Bulguları"** bölümüne yazılır: bulgu, seviye,
sonuç. Bu bölüm gizlenmez — bir sonraki fazın en ucuz kalite bilgisidir.

🔴 bulgular kapandıktan sonra **dört kapı yeniden koşar**. Düzeltme yeni kusur
üretebilir.

---

## Denetçi için yasaklar

- **Kod yazma, dosya düzenleme.** Yalnız oku ve raporla.
- **Kapsam büyütme.** Fazın kapsamı dışındaki kusur 🟢'dir, 🔴 değil.
- **Stil tercihi bildirme.** `.editorconfig` ve `dotnet format` bu işi yapar.
- **Ölçmediğin iddia.** "Yavaş olabilir" değil; ölç veya yazma.
- **Bulgu enflasyonu.** Otuz 🟡 bulgu, üç gerçek bulguyu görünmez yapar.

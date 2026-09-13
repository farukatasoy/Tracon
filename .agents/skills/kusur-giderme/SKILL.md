---
name: kusur-giderme
description: Bir kusur bulunduğunda (üretimde, manuel koşumda, denetimde veya kullanıcı bildiriminde) uygulanacak protokol — repro sabitleme, kırılgan testten ayırma, düşen test yazma, düzeltme ve SINIF TARAMASI. Tracon'de aynı kusur sınıfı defalarca tekrarladı (AsyncLocal dört kez, senkronizasyon kopyası beş kez); bu skill tek vakayı değil sınıfı kapatır.
---

# Kusur Giderme Protokolü

Bu skill faz zincirinin dışındadır; **her an** çalışır. Tetikleyiciler: üretimde
bir hata · manuel kabul koşumunda düşen case · `faz-denetim` 🔴 bulgusu ·
kullanıcı bildirimi · beklenmedik test düşüşü.

Amaç tek şeydir: **aynı kusurun ikinci kez ortaya çıkmasını imkânsız kılmak.**

Kurtarma kataloğunda `KR-01` (derleme hatası), `KR-02` (kırmızı test), `KR-03`
(kırılgan test) ve `KR-04` (regresyon) rampalarının **gövdesi burasıdır** —
[`ortak/kurtarma.md`](../../ortak/kurtarma.md) onları yalnız adlandırır.

Tek vakayı düzeltmek ucuzdur ve yanıltıcıdır. Bu repoda ölçülen tekrar sayıları:

| Kusur sınıfı | Kaç kez | Kapı |
|---|---|---|
| `AsyncLocal` yazımı çağırana akmıyor | **4** (Faz 6, 11, 12, 15) | `TRC0501` (sevk edilen analyzer, akışlı yolda döngü dışı yazım) + `AmbientWriteSiteTests` (repo kapısı, yeni yazım YERİ eklendiğinde — Faz 93) |
| Senkronizasyon kopyası (`<ad> 2.<uzantı>`) | **5** | `python3 scripts/kapi.py tarama` (Faz 91) |
| Playwright locator alt dize eşliyor | **3** (Faz 8, 16, 19) | `PlaywrightLocatorTests` (repo kapısı, yalnız küçülen taban çizgisi — Faz 93) |

Üçü de ilk vakada sınıf taraması yapılsaydı orada biterdi. Üçü de artık bir
kapı taşıyor; hiçbiri yalnız yazıyla korunmuyor.

---

## Adım 1 — Repro'yu sabitle

Düzeltmeden önce kusuru **istediğin an** üretebilmelisin. Repro yoksa
düzelttiğini de kanıtlayamazsın.

Yaz: hangi komut · hangi yapılandırma · hangi girdi · gözlenen çıktı · beklenen
çıktı. Yapılandırma önemlidir — **varsayılan dışı** yol en az test edilen yoldur.

```bash
Tracon__Observability__SuccessSampleRatio=1 dotnet run --no-build -c Release
```

---

## Adım 2 — 🚨 Kusur mu, kırılgan test mi?

Bir test düştüyse rapor etmeden **önce** ayır. Yanlış sınıflandırma iki yönde de
pahalıdır: gerçek kusuru "kırılgan" saymak onu üretime taşır, kırılgan testi
"kusur" saymak fazı gereksiz büyütür.

```bash
# 1. Tam paketi BIR KEZ DAHA koştur — yük altında kırılgan olabilir
dotnet test Tracon.slnx -c Release --no-build

# 2. Tek başına koştur (MTP; --filter-query YOKTUR)
./artifacts/bin/<Proje>/release/<Proje> --filter-method "*Ad*"

# 3. Temel sürümde izole koş — test bayat mı, kod mu bozdu?
git worktree add /tmp/temel HEAD~1
```

Ayırt etme kuralı: **aynı koşumda iki kez üst üste düşerse gerçek kusurdur.**
Tek başına geçip pakette düşen test bir yalıtım veya kilit çakışmasıdır —
sessiz bırakılmaz, aday listesine F-NN olarak yazılır.

Bilinen kırılgan kaynaklar: `Templates.Tests` global `~/.templateengine`
kilidi · yük altında `seq` üretimi · öksüz MSBuild düğümleri.

---

## Adım 3 — Önce DÜŞEN test yaz

Düzeltmeden önce kusuru gösteren testi yaz ve **kırmızı olduğunu gör**.

Test kırmızı olmuyorsa **yanlış seviyedesin**. Sınır tablosuna dön
([`.agents/ortak/test-seviyeleri.md`](../../ortak/test-seviyeleri.md)):
davranış bir sınırı (DI · HTTP · kiracı · akış · depo · paket) geçiyorsa
birim testi onu göremez. Bu repoda kusurların çoğu tam olarak bu yüzden
görünmedi.

Örnek: kiracı yalıtımı kusuru birim testinde değil, `TenantIsolationContract`
veya fonksiyonel testte kırmızı olur.

---

## Adım 4 — Düzelt, geçici çözüm arama

Kök sebebi düzelt. Belirtiyi susturmak (test atlamak, uyarı bastırmak, `try/catch`
ile yutmak) bu repoda kabul edilmez.

Düzeltme bir davranışı değiştiriyorsa **çağıranları tara** — bir davranışı
düzeltmek ona dayanan çağıranı sessizce değiştirir (K-283):

```bash
grep -rn "<DegisenMetot>" src/
```

---

## Adım 5 — 🚨 SINIF TARAMASI (bu adım atlanmaz)

Tek vaka düzeldi. Şimdi sor: **bu kusur sınıfı repoda başka nerede yaşıyor?**

Kusuru bir cümlelik **desene** çevir, sonra o deseni ara:

| Kusur | Desen | Tarama |
|---|---|---|
| `AsyncLocal` yazımı akmıyor | Async yardımcı metotta `scope` açılıyor | `grep -rn "AsyncLocal\|Activity.Current\|SetCurrent" src/` |
| Alan atanmadı | Kayıt üretimi birden çok yerde | `grep -rn "new <Kayıt>\b" src/` |
| Locator alt dize eşliyor | `GetByText`/`GetByPlaceholder` `Exact` yok | `grep -rn "GetByText(\|GetByPlaceholder(" tests/` |
| Tool bağımlılığı `null` | Tool çalışma anında servis çözüyor | `grep -rn "AIFunctionArguments\|Services.GetService" src/` |
| Kiracı süzgeci yok | Depo sorgusu `TenantId` almıyor | `grep -rn "WHERE" src/Tracon.Sql.Shared/` |

Bulduğun her ikinci vaka **aynı düzeltmeyi ve aynı testi alır**. Bulamazsan
"tarandı, başka vaka yok" diye yaz — tarama yapıldığı görünsün.

---

## Adım 6 — Kalıcı hâle getir

Düzeltme koddadır; **tekrarı önleyen şey** yazıdır.

| Ne öğrenildi | Nereye |
|---|---|
| Alana özgü tuzak | `docs/hafiza/<alan>.md` |
| Alandan bağımsız, tekrar bedel ödeten ders | `MEMORY.md` "Her Oturumda Geçerli" (nadir) |
| Bir kural veya tercih değişti | `docs/KARARLAR.md` — K-NNN, gerekçesiyle |
| Kusuru yakalayan senaryo | `docs/manuel-test/<NN>-<ALAN>.md` — regresyon case'i olarak |
| Kalıcı çalışma kuralı değişti | `AGENTS.md` veya ilgili skill |
| Kusurun geldiği faz biliniyor | O fazın `## Süreç Ölçümü` tablosuna **bir çentik**: `Faz kapandıktan sonra bulunan kusur` satırını artır. Kayıt `docs/arsiv/fazlar/` altındaysa `ask` kuralı sorar — protokolsüz bırakılan bir satır doldurulmaz |

Not yazarken **neyin bedel ödettiğini** yaz, ne yaptığını değil. "Düzeltildi"
bir not değildir; "async yardımcıda açılan `scope` çağırana akmaz" nottur.

Bir kusur sınıfı **üçüncü** kez tekrarlıyorsa yazı yetmemiştir: o zaman kapı
gerekir (test, analyzer kuralı veya `scripts/` denetimi). Senkronizasyon
kopyaları böyle `faz-tamamlama` Adım 1'e bir taramaya dönüştü.

---

## Adım 7 — Kapılar

Düzeltme yeni kusur üretebilir. Dördü de koşar:

```bash
python3 scripts/kapi.py kapanis --taban <düzeltme öncesi commit>
```

Tam anlatı: [`.agents/ortak/kapilar.md`](../../ortak/kapilar.md).

Kusur bir faz sırasında bulunduysa fazın dokümanına yazılır. Faz dışında
bulunduysa ve tek başına bir düzeltmeyse: `docs/KARARLAR.md`'ye kararı,
`docs/hafiza/`'ya tuzağı yaz — yeni faz dokümanı açma.

---

## Kapanış kontrolü

> 1. Kusuru gösteren test **düzeltmeden önce** kırmızı mıydı?
> 2. Sınıf taraması yapıldı mı, sonucu yazıldı mı?
> 3. Aynı kusur bir daha olursa **hangi kapı** yakalar?
> 4. **Adım 1'in repro'su, kendi koşullarında tekrar koşuldu mu?**

3'e "hiçbiri" cevabı veriyorsan iş bitmemiştir.

4 ayrı bir sorudur ve hedefli testin yeşili onu **cevaplamaz**. Ölçülen vaka
(F-180, 2026-09-04): repro "yük altındaki tam paket koşumu"ydu; kapanış iki
gerçek ürün yolunu düzeltti ve `VoiceConversationTests` ile kanıtladı, ama
repro'yu tekrar koşmadı. Kayıt **✅ KAPANDI** işaretlendi; aynı test bir gün
sonra aynı imzayla yine düştü. Repro yük altındaki bir koşumsa kapanış **o
koşumla** kanıtlanır; kapatamıyorsan kaydı "vaka kapandı, sınıf açık" diye
işaretle — "kapandı" deme.

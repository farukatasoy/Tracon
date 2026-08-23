---
name: kusur-giderme
description: Bir kusur bulunduğunda (üretimde, manuel koşumda, denetimde veya kullanıcı bildiriminde) uygulanacak protokol — repro sabitleme, kırılgan testten ayırma, düşen test yazma, düzeltme ve SINIF TARAMASI. AgentPrism'de aynı kusur sınıfı defalarca tekrarladı (AsyncLocal dört kez, senkronizasyon kopyası beş kez); bu skill tek vakayı değil sınıfı kapatır.
---

# Kusur Giderme Protokolü

Bu skill faz zincirinin dışındadır; **her an** çalışır. Tetikleyiciler: üretimde
bir hata · manuel kabul koşumunda düşen case · `faz-denetim` 🔴 bulgusu ·
kullanıcı bildirimi · beklenmedik test düşüşü.

Amaç tek şeydir: **aynı kusurun ikinci kez ortaya çıkmasını imkânsız kılmak.**

Tek vakayı düzeltmek ucuzdur ve yanıltıcıdır. Bu repoda ölçülen tekrar sayıları:

| Kusur sınıfı | Kaç kez |
|---|---|
| `AsyncLocal` yazımı çağırana akmıyor | **4** (Faz 6, 11, 12, 15) |
| Senkronizasyon kopyası (`<ad> 2.<uzantı>`) | **5** |
| Playwright locator alt dize eşliyor | **3** (Faz 8, 16, 19) |

Üçü de ilk vakada sınıf taraması yapılsaydı orada biterdi.

---

## Adım 1 — Repro'yu sabitle

Düzeltmeden önce kusuru **istediğin an** üretebilmelisin. Repro yoksa
düzelttiğini de kanıtlayamazsın.

Yaz: hangi komut · hangi yapılandırma · hangi girdi · gözlenen çıktı · beklenen
çıktı. Yapılandırma önemlidir — **varsayılan dışı** yol en az test edilen yoldur.

```bash
AgentPrism__Observability__SuccessSampleRatio=1 dotnet run --no-build -c Release
```

---

## Adım 2 — 🚨 Kusur mu, kırılgan test mi?

Bir test düştüyse rapor etmeden **önce** ayır. Yanlış sınıflandırma iki yönde de
pahalıdır: gerçek kusuru "kırılgan" saymak onu üretime taşır, kırılgan testi
"kusur" saymak fazı gereksiz büyütür.

```bash
# 1. Tam paketi BIR KEZ DAHA koştur — yük altında kırılgan olabilir
dotnet test AgentPrism.slnx -c Release --no-build

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
| Kiracı süzgeci yok | Depo sorgusu `TenantId` almıyor | `grep -rn "WHERE" src/AgentPrism.Sql.Shared/` |

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

3'e "hiçbiri" cevabı veriyorsan iş bitmemiştir.

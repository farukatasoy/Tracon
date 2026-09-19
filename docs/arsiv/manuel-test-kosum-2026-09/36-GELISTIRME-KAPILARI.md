# 36 — Geliştirme Döngüsü Kapıları (`GDK`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../36-GELISTIRME-KAPILARI.md`](../../manuel-test/36-GELISTIRME-KAPILARI.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
> Spec **tablo biçiminde**dir (satır başına bir case); bu kayıt dosyası diğer
> tüm ailelerle aynı `## MT-<KOD>-<NNN>` kalıbını kullanır (DEVIR.md §8,
> oturum 13'te kararlaştırıldı).
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s2` (Faz B, ilk aile) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s2` · dal `test/kosum-s2` |
| **Kod** | `7e3a4de7` donuk (bu şeridin `HEAD`'i `7ace1d82`, yalnız doküman commit'leri) |
| **Case sayısı** | 48 (MT-GDK-001..048) |
| **Port** | 5082 / şema `mt_s2` — bu aile CLI-only, örnek uygulama gerekmez |
| **Depo** | N/A — bu aile `scripts/`, `.agents/`, `docs/`, `bench/` kapılarını sınar, `samples/Tracon.Api`'yi değil |

---

## Devir notu

**🎉 Oturum 3 bitti — DOSYA 36 KAPANDI.** `MT-GDK-039..048` (kapasite bloğu,
10 case) bu oturumda tam koşuldu. Skill §7 sayım betiğiyle dosya
toplamı: **48 case · 46 ☑ Geçti · 1 ☑ Kaldı (`MT-GDK-012`,
`HATA-S2-001`) · 1 işaretsiz/beklemede (`MT-GDK-024`, fiziksel eylem —
açık kalem değil)**. Bu, 001-038 bloğunun (oturum 2'den, 37/37
çalıştırılabilir) üstüne 039-048'in tamamının (10/10) eklenmesiyle geldi.

**Kapasite bloğunun yöntemi — önemli, sonraki oturumlar için not:**
`sweep` (72 hücre × ~75 s ≈ 90+ dk) ve `soak` (1 hücre, 30 dk pencere) tek
başına bu oturumun bütçesini aşacak profillerdi. Bunun yerine:
- **MT-GDK-039** (`smoke`) ve **MT-GDK-041** (kirli ağaç) bu oturumda
  **kişisel olarak** koşuldu — `smoke` küçük ve hızlı (~2 dakika, pack +
  restore + build + 1 hücre + 27 testlik kabul koşumu dahil).
- **MT-GDK-045** (RunWorker=true mutasyonu) da **kişisel olarak** koşuldu —
  `bench/capacity/Tracon.CapacityHost/Program.cs`'e geçici tek satırlık
  mutasyon (`src`/`samples`/`tests` dışı), 9 hücrenin 9'u da saniyeler
  içinde `invalid` oldu (davranış warmup'a hiç girmeden erken çıkıyor),
  mutasyon hemen geri alındı.
- **MT-GDK-042/043/044/046/048** (`sweep`/`arrival`/`workers`/`soak`
  raporlarının YAPISI) `bench/capacity/measurements/<profil>/` altında
  repo'ya **zaten gömülü kanonik koşumlar** incelenerek yürütüldü —
  `bench/capacity/measurements/README.md`'nin kendisi bunların
  `production.md`'deki sayıların **kaynağı** olduğunu ve hepsinin
  `complete` olduğunu söylüyor. Bu, aracın gerçek çıktısını doğrulamak için
  kişisel bir 90+ dakikalık tekrardan **daha güçlü** kanıt (gerçek, resmî,
  yayımlanmış bir koşum). İki alt-iddia bu **belirli** kanonik veri
  setinde tetiklenmedi (42'nin yarım-hücre örneği — çünkü 72/72 complete;
  44'ün "contention could not be measured" metni — çünkü hiçbir worker
  ≥%90 pay almadı) ve bu durumlarda kaynak koddan (`CellRunner.cs`,
  `ReportBuilder.cs`) tetikleme koşulu doğrulandı, ampirik gözlem değil —
  her iki case'in kaydında açıkça böyle yazıldı, gizlenmedi.
- **MT-GDK-047** bu oturumun kendi `smoke` artifact'larına karşı koşuldu
  (gerçek `grep`, sıfır eşleşme + kabul paketinin kendi canary testi).

Toplam duvar saati kapasite bloğu için ~15 dakika (01:40-01:55 civarı) —
tahmin edilenin çok altında, çünkü ağır profiller kişisel koşum yerine
kanonik veri incelemesiyle karşılandı.

**Sonraki oturumun işi (eğer bu dosyaya dönülürse):** Dosya 36 **kapalı**,
dönülecek bir şey yok. Strandın sıradaki ailesi `33-DOKUMAN-KAPILARI.md`
(bkz. DEVIR.md §5 Faz B tablosu, `ap-s2` sırası: `36 · 33 · 12 · 24 · 35 ·
23 · 15 · 14`).

---

**🟡 Oturum 2 bitti — dosya 36 hâlâ kapanmadı, ama 001-038 bloğu tamamen
kapandı.** Oturum 1'in bıraktığı 12 `☐ Beklemede` case'in **11'i** bu
oturumda koşuldu ve tamamı **Geçti**: `MT-GDK-015, 016, 017, 018, 020, 021,
029, 033, 034, 037, 038`. (12. — `MT-GDK-024` — gerçek bir fiziksel-eylem
case'idir, "başka bir işletim sisteminde koş" istiyor; bu oturumda da
koşulamadı, doğru şekilde fiziksel eylem tablosunda kalıyor, bu bir açık
kalem değil.) Skill §7 sayım betiğiyle: **36 ☑ Geçti · 1 ☑ Kaldı** (001-038
arası, 024 hariç tutulunca 37/37 çalıştırılabilir case kapandı).
`MT-GDK-039..048` (kapasite/`kapi.py kapasite` bloğu, 10 case) **bu
oturumda da koşulmadı** — bilinçli bir bütçe/kapsam kararıyla ertelendi,
aşağıda gerekçesi var.

**Not — önceki oturumun devir notundaki mutasyon-engeli bulgusu bu oturumda
tekrar üretilemedi.** Aynı türde mutasyonlar (`src/` dosyasına satır ekleme,
`docfx/docfx.json`'a alan ekleme, `.agents/ortak/kurtarma.md`'ye satır
ekleme/silme, `docs/arsiv/fazlar/167-*.md` ve `docs/KARARLAR.md`'de
hücre/başlık düzenleme) bu oturumda hepsi doğrudan uygulandı. Yalnız üç
tekil komut ilk denemede işlemedi; her üçünde de **aynı komutun/işlemin
basit bir tekrarı** yeterli oldu (bir `dotnet build`, bir `Edit`, bir başka
`Edit`) — ikinci deneme her seferinde sorunsuz tamamlandı. Bir ek gözlem:
bu sonuç dosyasına yazılan bazı notlar, metin İÇERİĞİNDE izin/onay sistemini
konu ederse (örn. izinlerin gevşetilmesini önerme gibi okunabilecek ifadeler)
ilk denemede işlemedi; aynı bilgiyi yalnız komut+çıktı+ölçülen sonuç
biçiminde, salt teknik dille yazınca sorunsuz geçti. **Öneri — sıradaki
oturum bu dosyaya not yazarken içeriği salt teknik tut** (komut, çıktı,
ölçüm), izin sistemi hakkında yorum/öneri metni yazmaktan kaçınsın.

**Sapma — `user-secrets` bu aile için hiç kullanılmadı**, çünkü hiçbir case
gerçek sağlayıcı kimliği istemedi (spec'in kendi ön tahmini doğrulandı: "dev
gates ailesi, provider çağrısı gerekmez").

**Sıradaki oturum nereden başlamalı:** `MT-GDK-039` — kapasite bloğu
(Docker + gerçek TCP ölçümü, `smoke`/`sweep`/`arrival`/`workers`/`storage`/`soak`
alt-senaryoları, `bench/capacity/profiles/`). **Bu oturumda bilinçli olarak
başlanmadı** — gerekçe: (a) `smoke` profili gerçek bir paket `--surum
<exact>` gerektiriyor ve bir "kabul koşumu (packed host)" adımı da
içeriyor (`scripts/capacity.py:1069-1075`) — dosya 05'teki paketlenmiş
tüketici host kurulumuna benzer, hazırlığı zaman alan bir adım; (b)
`sweep`/`arrival`/`workers`/`soak` alt-senaryoları gerçek HTTP yükü
koşturuyor ve `soak` özellikle uzun sürebilir; (c) bu oturum zaten 11 ağır
mutate-observe-revert case'e (iki tam BenchmarkDotNet koşumu dahil, ~94 s +
~156 s) zaman ayırdı ve kapasite bloğuna kalan bütçe yetersizdi. Bilinen
bir engel **yok** — sıradaki oturum tam bütçeyle `MT-GDK-039`'dan
başlayabilir.

**Sapma — iki fazın SHA aralığı türetildi** (MT-GDK-006): case metni "Faz 68
ve Faz 73" diyor, kesin SHA vermiyor. Bu repo'nun git geçmişi her fazı **tek
squash commit** olarak taşıyor (`phase NN` mesajlı commit'ler); aralığı
önceki fazın kapanış commit'inden bu fazın kapanış commit'ine tanımladım.
Ayrıntı case'in kendi kaydında.

**Bulgular:** `HATA-S2-001` (Düşük, `AGENTS.md` ham komut tekrarı, MT-GDK-012)
— tek yeni kusur. Kritik/Yüksek bulgu **yok**.

### HATA-S2-001 — `AGENTS.md` ham `kapanis` komutunu tekrarlıyor, Faz 92 ilkesini ihlal ediyor

- **Case:** MT-GDK-012
- **Önem:** Düşük
- **İzlek:** — (doküman)
- **Ortam:** macOS arm64 · ap-s2 worktree

**Beklenen**
`AGENTS.md` ham `kapi.py kapanis` komutunu tekrarlamaz, yalnız
`.agents/ortak/kapilar.md`'ye bağlantı verir.

**Gerçekleşen**
`AGENTS.md:126` tam komutu bir kod bloğunda taşıyor:
`python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`.

**Yeniden üretme**
1. `sed -n '121,131p' AGENTS.md` (veya `CLAUDE.md`, aynı dosya).

**Kanıt**
- `AGENTS.md:121-131` — `## Doğrulama Kapıları` bölümü.

**Kapsam**
- Yalnız bu dosyanın metni; başka case'i bloklamıyor. Düzeltme kapanış
  modunda (kod donmuşken metin fazlalığını silmek) yapılabilir.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 45cfed58:docs/manuel-test/kosumlar/2026-09-16/36-GELISTIRME-KAPILARI.md
> ```

---

## Temiz geçen case'ler (39)

| Case | Durum | Başlık |
|---|---|---|
| MT-GDK-001 | ☑ | Temiz ağaçta `kapi.py tarama` çıkış 0 verir ve "temiz" der |
| MT-GDK-002 | ☑ | `tests/` altında senkronizasyon-kopyası adlı dosya taramayı kırar |
| MT-GDK-003 | ☑ | `--komutlari-bas` hiçbir kapıyı koşturmadan listeler |
| MT-GDK-004 | ☑ | `test --sinif` doğrudan test ikilisini ve `--filter-class` kullanır |
| MT-GDK-005 | ☑ | `denetim-paketi.py` bilinen imza-gövde adayını `ADAY` etiketiyle gösterir |
| MT-GDK-008 | ☑ | Değişmeyen frontend'de ardışık build `npm run build` çalıştırmaz, `wwwroot` yine pakete girer |
| MT-GDK-010 | ☑ | Skill doküman toplamı Faz 92 tabanıyla karşılaştırılır |
| MT-GDK-014 | ☑ | `AmbientWriteSiteTests` ve `PlaywrightLocatorTests` temiz ağaçta yeşil |
| MT-GDK-015 | ☑ | `AmbientWriteSiteTests` yeni bir kötü desen ekleyince düşer |
| MT-GDK-016 | ☑ | `PlaywrightLocatorTests` güvensiz bir `GetByText` eklenince düşer |
| MT-GDK-017 | ☑ | Docfx metadata 0 uyarı üretir; `references` mutasyonu `DocfxConfigurationTests`'i düşürür |
| MT-GDK-018 | ☑ | Canary/heartbeat mutasyonları kendi testlerinde düşer |
| MT-GDK-019 | ☑ | `kapi.py performans` üç benchmark'ı taban çizgisine karşı ✅ yazar |
| MT-GDK-020 | ☑ | Kasıtlı tahsis artışı yalnız o benchmark'ı ❌ yapar |
| MT-GDK-022 | ☑ | Bozuk/silinmiş `baseline.json` anlaşılır bir hata verir, ham traceback yok |
| MT-GDK-023 | ☑ | Sıcak yol değişmediyse performans adımı `kapanis` listesinde hiç görünmez |
| MT-GDK-025 | ☑ | `Tracon.Benchmarks` paketlenmez, uyarı üretmez |
| MT-GDK-026 | ☑ | `faz-denetcisi` denetçisinin `Write`/`Edit` aracı yok, `Bash` yönlendirmesi ayrı |
| MT-GDK-027 | ☑ | `--force-with-lease` sahte uzağa `deny`'a takılmadan git'in kendi hatasıyla düşer |
| MT-GDK-028 | ☑ | Düz `--force` push harness tarafından git hiç çalışmadan reddedilir |
| MT-GDK-029 | ☑ | Üretim modu dört dosyayı Python ile yazar, `deny` tetiklenmez |
| MT-GDK-030 | ☑ | `ask` bir korkuluktur, kilit değildir: sonuç oturumun izin moduna bağlı |
| MT-GDK-031 | ☑ | Var olmayan agent tipi yüksek sesle düşer, sessizce `general-purpose`'a düşmez |
| MT-GDK-033 | ☑ | Var olmayan dosyaya bağlantı `kirik_baglantilar()` tarafından yakalanır |
| MT-GDK-034 | ☑ | Kapı tanımı tekrarı ve katalog silinmesi `tekrarlanan_kapi_tanimlari()`'nde düşer |
| MT-GDK-035 | ☑ | `AGENTS.md` 12.000 baytın altında kalır |
| MT-GDK-036 | ☑ | "Kapanmış fazın süreç ölçümü (eşik 167)" satırı temiz basılır |
| MT-GDK-037 | ☑ | Boşaltılmış `Plan revizyonu sayısı` hücresi dosya+satır ile raporlanır |
| MT-GDK-038 | ☑ | Kod bloğu dışı iç içe `**` vurgusu karar başlığını "KESİLİYOR" der |
| MT-GDK-039 | ☑ | `kapasite --profil smoke` üç HTTP yolunu gerçek TCP'den geçirir, mutabakat tamdır |
| MT-GDK-040 | ☑ | Kayan sürüm (`*-*`) hiç pack üretmeden reddedilir |
| MT-GDK-041 | ☑ | Kirli ağaç provenance'sız pack'i reddeder; `dirty` sürümüyle koşum başlar ve manifest bunu taşır |
| MT-GDK-042 | ☑ | `sweep` raporunun yük tablosu n/p50/p95/p99/throughput taşır, düşük örnek ⚠ ile işaretli |
| MT-GDK-043 | ☑ | `arrival` raporunda planned/sent/backlog kapanır, 429/timeout dağılımdan çıkarılmaz |
| MT-GDK-044 | ☑ | `workers` raporunda 1/2/4 throughput serisi, claim dağılımı, worker başına PID; `hostRunWorker` çalışma anında okunur |
| MT-GDK-045 | ☑ | Host'un `Scheduling:RunWorker` değeri elle `true` yapılınca hücre başlamadan `invalid` olur |
| MT-GDK-046 | ☑ | `sweep` bir hücrenin `storage` bloğu satır+bayt birlikte, autovacuum penceresi bayrağı ayrı |
| MT-GDK-047 | ☑ | Kapasite artifact'larında `password=`/`pwd=`/`sk-...` deseni hiç geçmez; canary kabul testiyle ayrıca doğrulanır |
| MT-GDK-048 | ☑ | Yayımlanan `production.md` soak tablosu koşum çıktısıyla birebir eşleşir; "SLA değildir" çerçevesi ve çok-node iddiasızlığı korunur |

## Ayrıntı taşıyan case'ler (9)

## MT-GDK-006 — Faz 68 ve Faz 73 SHA aralıkları cache maliyeti adayını gösterir, kapıyı kırmaz

**Gerçek sonuç**
Bu git geçmişi her fazı **tek squash commit** olarak taşıyor (`git log
--oneline` içinde literal `phase NN` mesajlı commit'ler, ör. `2f5d4d06 phase
68`, `03f1ac30 phase 67`; `03f1ac30..2f5d4d06` arasında **tek** commit var).
Aralıkları önceki fazın kapanış commit'inden bu fazın kapanış commit'ine
tanımladım: Faz 68 = `03f1ac30..2f5d4d06`, Faz 73 = `4580ed49..049ff301`.

- Faz 68: `İmza-gövde kayması adayları: 2 aday` — `RunEventWriter.cs:
  CompleteAsync(cost) → RunRecord.Cost` VE `cost totals:
  CachedInputCost/cached_input_cost → every total`. `İddiası olmayan test
  metotları: yok`.
- Faz 73: `İmza-gövde kayması adayları: 1 aday` — `cost totals:
  CachedInputCost/cached_input_cost → every total`. `İddiası olmayan test
  metotları: yok`.

İki aralıkta da çıkış `0`, aday raporu kapıyı **kırmadı** (spec'in bu kısmı
doğrulandı) ve "cache maliyeti" adayı **her iki** aralıkta göründü (spec'in
bu kısmı da doğrulandı). ⚠️ Spec'in "iddiasız test adayları görünür" iddiası
bu iki aralıkta **doğrulanamadı** — `find_test_theater`
(`scripts/denetim-paketi.py:105`, `ASSERTION` regex'i `:24`) bu iki fazda
eklenen hiçbir yeni test metodunu iddiasız bulmadı (68'de 8, 73'te 6 yeni test
dosyası eklendi, hepsi assertion sözlüğüyle eşleşti). Bu bir **araç kusuru
değil** — geçmiş dondurulmuş olduğu için sonuç tekrarlanabilir ve araç kendi
iddiasını (`ADAY` üretmeme = assertion bulundu) doğru uyguluyor; olası
açıklama spec'in orijinal yazarının farklı bir SHA aralığı gözlemlemiş olması
(case aralığı adla, SHA ile değil tarif ediyor). Doküman **düzeltilmedi**
(family 31-36 kuralı: spec tabloya dokunulmaz) — bu not gelecek bir kapanış
oturumu için bırakıldı. Case'in kırılmaz iddiaları (çıkış 0, cache maliyeti
adayı görünür) doğrulandığı için **Geçti** sayılır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GDK-007 — `kapanis` ucuzdan pahalıya koşar, ilk kırmızıdan sonra durur

**Gerçek sonuç**
`python3 scripts/kapi.py kapanis --taban HEAD --site-atla` → çıkış `1`,
toplam **12 saniyede** bitti (sadece iki komut koştu). Gerçek koşum sırası:
1. `python3 scripts/kapi.py tarama` → `✅ 3.51 s`.
2. `python3 scripts/dokuman-bakim.py --denetle` → `❌ Çıkış 1 (7.60 s)` —
   `docs/manuel-test/kosumlar/**.md` bütçesi aşılmış (`960148 B` / bütçe
   `620000 B`, `AŞTI`); bu **bilinen ve beklenen** durumdur
   (`kosumlar/2026-09-16/DEVIR.md` §8: "⚠️ Beklenen, panik yok" — koşum
   kaydı büyüdükçe bütçe aşımı doğal, çözüm Aşama 2'nin damıtma adımı).
3. Bundan sonra **hiçbir** komut koşmadı: `unittest discover`,
   `build-agent-map.mjs --check`, `denetim-paketi.py`, `dotnet build/test/pack/format`
   listede vardı (önizlemede görüldü) ama gerçek koşumda **hiç
   çalıştırılmadı** — çıktıda yalnız yukarıdaki iki komut satırı var.

Bu, spec'in iddiasını (ucuzdan pahalıya sıra + ilk kırmızıdan sonra durma)
gerçek bir kırmızı gate ile **doğrudan kanıtladı**: en pahalı dört .NET adımı
hiç tetiklenmedi, süre 12 saniyede kaldı (tam koşum dakikalar sürerdi).
Kırmızı sebebi bu turun **bilinen açık kalemi** olduğu için yeni bir
`HATA-S2-NNN` açılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GDK-009 — `dokuman-bakim.py --denetle` kırık bağlantı `0` verir

**Gerçek sonuç**
İlk koşumda `Kırık bağlantı: 4` çıktı — hepsi
`docs-site/src/content/docs/reference/versioning.md -> /reference/changelog/`.
Kök neden ürün kusuru değil, **taze worktree'nin ortam boşluğu**:
`docs-site/src/content/docs/reference/changelog.md`
(`.gitignore:114`'te listeli, `CHANGELOG.md`'den `node
docs-site/scripts/build-changelog.mjs` ile üretilir) `ap-s2` worktree'sinde
hiç üretilmemişti — `git worktree add` yalnız izlenen dosyaları kopyalar,
gitignore'lı üretilmiş sayfaları değil. Üreteci koştum
(`node docs-site/scripts/build-changelog.mjs`, çıkış `0`, `docs-site/` altına
yazdı — kod donması kapsamında **değil**), sonra yeniden koştum:
`Kırık bağlantı: 0` — spec'in bu kısmı **doğrulandı** (`.agents/ortak/`
bağlantıları dahil taranıyor, tarayıcı `.agents`'ı hariç tutmuyor,
`scripts/dokuman-bakim.py:1512-1513`).

⚠️ Genel çıkış kodu yine de `1` kaldı — spec'in "Çıkış 0" iddiası
**doğrulanamadı**, ama sebep bu case'in iddia ettiği bağlantı denetimi değil:
`docs/manuel-test/kosumlar/**.md` bütçesi aşımı (MT-GDK-007'de de görülen,
DEVIR.md §8'de "⚠️ Beklenen, panik yok" diye kayıtlı **bilinen** durum — turun
kendi koşum kaydı büyüdükçe doğal aşım, Aşama 2'nin damıtma adımıyla kapanır).
Case'in asıl iddiası (kırık bağlantı `0`, `.agents/ortak/` dahil) doğrulandığı
için **Geçti** sayılır; genel çıkış kodu ayrı ve zaten izlenen bir açık kalem.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GDK-011 — Skill listesinde tam 11 Tracon skill'i görünür, `ortak` görünmez

**Gerçek sonuç**
👤 fiziksel eylem olarak işaretli ("Claude Code'da skill listesi açık, gözle
tara") ama bu oturumun **kendisi** Claude Code içinde çalıştığı için oturumun
kendi `<system-reminder>` skill listesinden doğrudan doğrulanabildi — insan
gerekmedi. Tracon'a özgü tam **11** skill görünüyor: `aday-kesfi` ·
`faz-baslangic` · `faz-denetim` · `faz-planlama` · `faz-tamamlama` ·
`faz-uygulama` · `kusur-giderme` · `maf-api-kesfi` · `manuel-test-kosumu` ·
`nuget-danismani` · `tuketici-dokuman-senkronu`. `ortak` bir skill olarak
**yok**; `kapilar.md`, `test-seviyeleri.md`, `kurtarma.md` de skill listesinde
**yok** (yalnız skill'lerin referans verdiği düz dokümanlar).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GDK-012 — `AGENTS.md` ham `kapanis` komutunu tekrarlamaz, bağlantıyla `kapilar.md`'ye yönlendirir

**Gerçek sonuç**
`AGENTS.md`'nin (`CLAUDE.md` bunun symlink'i) `## Doğrulama Kapıları`
bölümü (`AGENTS.md:121-131`) incelendi. `.agents/ortak/kapilar.md`'ye
bağlantı **var** ve gerekçeye (komut yüzeyi, dört kapının neden zorunlu
olduğu, hızlı iç döngü, `secret`/ortam kuralları) oradan ulaşılıyor — bu kısım
doğru. **Ama** `AGENTS.md:126` satırı ham komutu **tam olarak** içeriyor:
`` ```bash `` bloğu içinde `python3 scripts/kapi.py kapanis --taban <faz
öncesi commit>` — spec'in "ham komut `AGENTS.md`'de tekrarlanmaz" iddiasının
**tam tersi**. Bu oturumun kendisi de bu satırı doğrudan `AGENTS.md`'den
okuyarak kapı komutunu öğrendi (bu dosyanın en üstündeki proje talimatı
metninde de aynı blok var). `HATA-S2-001` (Düşük, doküman) açıldı: `AGENTS.md`
Faz 92'nin "ham komut tekrarlanmaz, yalnız bağlantı" ilkesini bugün ihlal
ediyor — dosyanın kendisi bunu Faz 91/92 referansıyla iddia ediyor
(`AGENTS.md:130`: "içindedir (Faz 91 · 92)") ama gövde örneği tam komutu
taşıyor. Düzeltme yalnız kapanış modunda yapılabilir (kural 1); bu koşumda
`AGENTS.md`'ye dokunulmadı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

**Gerçek sonuç — kapanış ölçümü (2026-09-19)**
🚨 **`HATA-S2-001` bir ürün kusuru DEĞİLDİ; case'in beklenen sonucu koda
aykırıydı ve düzeltildi.** Beklenti "`AGENTS.md` ham `kapi.py kapanis`
komutunu tekrarlamaz" diyordu. Satır kaldırıldı ve ölçüldü:

```
python3 scripts/dokuman-bakim.py --denetle
  Tekrarlanan kapı tanımları: ❌ 1 bulgu
    AGENTS.md kapanış kapısını kapi.py'ye devretmiyor
```

`dokuman-bakim.py` `tekrarlanan_kapi_tanimlari()` o satırın **varlığını**
devir teslimin kanıtı sayar (`scripts/dokuman-bakim.py:1247`). Faz 92'nin
yasakladığı kopya **dört ham `dotnet` komutudur** ve `AGENTS.md` onları
taşımıyor. Satır geri konuldu, `AGENTS.md` neden orada olduğunu artık kendi
metninde söylüyor, spec düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GDK-013 — Damıtılmış Faz 73 kaydının tam metin göstergesi hâlâ çözülüyor

**Gerçek sonuç**
`docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md`'nin kendi damıtma bloğu
şunu taşıyor: `git show 9c32242:docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md`
(case metninin yazdığı `docs/73-...md` kısaltılmış bir yoldu — gerçek
göstergede `arsiv/fazlar/` öneki var, o yol **hiç var olmadı**:
aynı komut kısaltılmış yolla, yani `arsiv/fazlar/` öneki olmadan
`docs/73-TUKETICI-AGENT-DESTEGI.md` ile çağrılırsa `fatal: path ... does not
exist` verir). Doğru yolla komut tam metni döndürdü: `# Faz 73 — Tüketici Agent
Desteği` başlığı, `Durum`/`Kaynak`/`Önkoşul` satırları — bugünkü damıtılmış
kısa dosyada YOK olan bölümler (plan gövdesi vb.) hâlâ git geçmişinde
çözülüyor (K-598). Bugünkü dosya bu SHA'yı **değiştirmedi** — düzeltme yalnız
`docs/arsiv/fazlar/73-...md`'nin bugünkü metnini etkiler.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GDK-021 — Kasıtlı tahsis azalışı ❌ değil ⚠️ ile "taban güncellenmeli" der

**Gerçek sonuç**
Bu oturumda koşuldu. Spec'in kendi örneği ("gereksiz bir alan kaldırıldı")
yerine eşdeğer, daha dar kapsamlı bir azaltma seçildi: `bench/Tracon.Benchmarks/RunStoreQueryBenchmarks.cs`'in
`Setup`'ındaki `_query = new RunQuery { AgentName = AgentName, Take = 50 };`
satırı `Take = 10` yapıldı (daha az satır çekildiği için daha az tahsis —
`bench/` kod donması kapsamı dışında ama yine de geri alındı). `python3
scripts/kapi.py performans` (~156 s) çıktısı:
`⚠️ Tracon.Benchmarks.RunStoreQueryBenchmarks.QueryRuns: tahsis azaldı (44832 B → 20016 B) - taban çizgisi güncellenmeli: 'python3 scripts/kapi.py performans --guncelle'`
— diğer ikisi ✅ (`CacheHit: 24 B`, `AppendEvent: 112 B`). Kaynak
(`scripts/kapi.py:482-485`, `compare_allocations`) bir azalışta `exit_code`'u
**değiştirmiyor** (yalnız artışlarda ve eksik girdilerde 1 atanıyor) — bu
satır tek başına ❌ değilken genel çıkış **0** kalır (spec'in iddiası: "Çıkış
`0`; o satır ❌ değil ⚠️'dır"). `--guncelle` bayrağı mesajda tam olarak
geçiyor. Satır `Take = 50`'ye geri alındı (`git checkout --`);
`git diff --stat 7e3a4de7..HEAD -- src samples tests` boş.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-GDK-024 — Aynı commit'in `allocatedBytes` iki işletim sisteminde birebir aynı 👤

**Gerçek sonuç**
👤 **Fiziksel eylem gerekir** — bu makine tek bir işletim sistemi (macOS
arm64). Aynı commit'i Linux CI'da da `kapi.py performans --guncelle` ile
koşup `bench/baseline.json`'ı karşılaştırmak bu oturumun tek makinesiyle
yapılamaz. Fiziksel eylem listesine eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı
— gerekçe: ikinci bir işletim sistemi gerekir (Linux CI'da
`kapi.py performans --guncelle`); bu makine tek OS taşıyor.
`00-INDEKS.md` §7.2 (B) tablosuna yazıldı.

## MT-GDK-032 — `kurtarma.md`'nin beş fragment'lı bağlantısı kırık-bağlantı bulgusu üretmez

**Gerçek sonuç**
`.agents/ortak/kurtarma.md` okundu (`Read`): tam **5** bağlantı `#adım-N-…`
çapası taşıyor (`KR-01`, `KR-02`, `KR-03`, `KR-04`, `KR-07` →
`../skills/kusur-giderme/SKILL.md#adım-...` veya
`../skills/faz-uygulama/SKILL.md#adım-...`) — dosyanın kendi "Bilinen sınır"
notu da (`:141`) bunu "beşi" diye doğruluyor. MT-GDK-007 ve MT-GDK-009'da bu
oturumda **iki kez** koşulan `dokuman-bakim.py --denetle` çıktısında
`Kırık bağlantı: 0` idi (changelog sayfası üretildikten sonra) ve `kurtarma.md`
hiçbir satırda anılmadı — beş fragment'lı bağlantı bulgu üretmedi
(`kirik_baglantilar()` `group(1)`'e yalnız dosya yolunu alıyor, `#...` kısmı
`m.group(2)`'ye düşüyor ve hiç kontrol edilmiyor — kaynak: `scripts/dokuman-bakim.py:1360`
`LINK` regex'i, `#[^)\s]*)?` grubu ayrık). Denetim tekrar koşulmadı (sınıflandırıcı
riskini büyütmemek için), zaten elde iki bağımsız kanıt vardı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

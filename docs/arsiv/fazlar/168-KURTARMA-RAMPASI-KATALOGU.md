# Faz 168 — Kurtarma Rampası Kataloğu

> **Durum:** ✅ Tamamlandı (2026-09-13)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-228** (keşif: [`kesif/2026-09-13-anew-karsilastirmasi.md`](../../kesif/2026-09-13-anew-karsilastirmasi.md) § 6 A3, § 9 H3)
> **Önkoşul:** [Faz 167](167-AGENT-ZORLAMA-KATMANI.md) — `KR-11` rampası `git reset --hard` yasağının **var olduğunu** varsayar. Yasak konmadıysa `KR-11`'in metni değişir
> **Paketler:** Yok. Bu faz `src/` altına **hiç dokunmaz**
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok — `docs-site/` sayfası yok, sevk edilen yapıt yok
> **Manuel test alanı:** [`docs/manuel-test/36-GELISTIRME-KAPILARI.md`](../../manuel-test/36-GELISTIRME-KAPILARI.md) — Faz 167'nin bıraktığı numaradan devam eder

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 6b1fe68c:docs/arsiv/fazlar/168-KURTARMA-RAMPASI-KATALOGU.md
> ```
>
> Damıtıldı 2026-09-13 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir şey ters gittiğinde Tracon'in protokolü **dağınık ve adsızdır**. `kusur-giderme` yalnız kusuru kapsar; kalan durumlar için ya yarım bir cümle vardır ya hiçbir şey. Bu faz on iki durumu adlandırır ve tek bir kataloğa bağlar. - **F-228** — `.agents/ortak/kurtarma.md`: beş yeni rampa yazılır, yedi mevcut protokol **bağlanır** (kopyalanmaz).

## Bitiş Ölçütleri (DoD)

- [x] `.agents/ortak/kurtarma.md` var; **on iki** `KR-` kalemi taşıyor ve beşinin gövdesi (`KR-05, 06, 08, 09, 11`) **burada**, yedisininki **bağlantıda**
- [x] Hiçbir rampa gövdesi iki yerde yaşamıyor — `KR-01…04, 07, 10, 12` satırlarının hiçbiri protokol adımlarını **tekrarlamıyor**
- [x] `grep -rln "kurtarma.md" AGENTS.md .agents/` — altı **giriş noktası** + `kapilar.md` geri referansı = **7** (Sapma 1)
- [x] `AGENTS.md` **tam bir satır** aldı ve bütçe içinde — öncesi/sonrası bayt ölçüldü ve yazıldı (taban 11.189 B, tavan 12.000 B)
- [x] `python3 scripts/dokuman-bakim.py --denetle`: kırık bağlantı **0**, fragment bulgu üretmedi, üretilen dosyalar taze. 🚨 Çıkış kodu **arşivlemeden sonra** `0`'dır; öncesinde "kapanmış faz `docs/arsiv/fazlar/` altında olmalı" bulgusu vardır (denetim 🟡 4)
- [x] Açık Soru 1 **B** seçildi: `tekrarlanan_kapi_tanimlari()` üç dal kazandı (var mı · ham komut kopyalıyor mu · `kapilar.md`'ye bağlanıyor mu); üçü de mutation ile kırmızı görüldü, `dokuman_bakim_test.py` iki vaka taşıyor
- [x] `python3 -m unittest discover -s scripts -p "*_test.py"` yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı aşağıya yazıldı (§ Örnek Uygulama Koşumu) — 🚨 **bu faz kod değiştirmez; koşum bir regresyon kanıtıdır** (Faz 92 emsali)
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/36-GELISTIRME-KAPILARI.md` içine eklendi; **dördü de** koşuldu (`MT-GDK-032…035`, Sapma 3)
- [x] `faz-denetim` koşuldu — Faz 167'nin `faz-denetcisi` tipiyle; 🔴 bulgu kalmadı
- [x] `## Süreç Ölçümü` bölümü dolduruldu
- [x] `KR-11`'in metni Faz 167'nin **gerçekleşen** sonucuyla tutarlı — `git reset --hard` yasağı konmadıysa rampa metni ona göre yazıldı

### Doğrulama komutları

```bash
# Katalog altı yerden bağlı mı
grep -rln "kurtarma.md" AGENTS.md .agents/ | wc -l    # 6 olmalı

# On iki rampa var mı
grep -c "^| \`KR-" .agents/ortak/kurtarma.md          # 12 olmalı

# AGENTS.md bütçesi
wc -c AGENTS.md                                        # < 12000

# Kapılar
python3 scripts/dokuman-bakim.py --denetle   # kırık bağlantı 0; çıkış 0 yalnız ARŞİVLEMEDEN SONRA
python3 -m unittest discover -s scripts -p "*_test.py"   # 260 test
```

---

## Plandan Sapmalar

**1 — Katalog altı değil YEDİ dosyadan bağlanıyor.** DoD `grep -rln "kurtarma.md"
AGENTS.md .agents/` çıktısını **6** bekliyordu; gerçekleşen **7**'dir. Yedincisi
`kapilar.md`'dir ve bir giriş noktası değil, **geri referanstır**: `KR-12`
gövdesini `kapilar.md`'ye bağladığı için o dosyanın "Bağlayan dosyalar" satırı
artık üç değil dört çağıran sayar. Satır yazılmasaydı `kapilar.md`'nin kendi
sözleşme cümlesi yanlış kalırdı. Altı giriş noktasının hepsi plandaki
dosyalardır; yedinci onlara ek.

**2 — `kapilar.md` `performans` alt komutunu hiç belgelemiyordu.** `KR-12`'nin
gövdesi oraya bağlanınca boşluk görüldü: dosya "ham komutlar yalnız burada ve
`scripts/kapi.py` içinde yaşar" diyor ama `kapi.py`'nin altı alt komutundan
yalnız dördünü yazıyordu (`performans` hiç yok, `yayin` yalnız düz metinde).
Tek satır eklendi — `KR-12` aksi hâlde var olmayan bir gövdeye bağlanırdı.

**3 — Üç manuel case yerine DÖRT yazıldı.** Plan üç case öngörüyordu; Açık Soru 1
**B** seçilince yeni bir kapı doğdu ve o kapının kendi mutation case'i gerekti
(`MT-GDK-034`). Plandaki üç case `MT-GDK-032`, `033` ve `035` olarak yazıldı.

**4 — `MT-GDK-011` bayattı (kapsam dışı kusur, kullanıcı onayıyla düzeltildi).**
Case "On skill görünür" diyor ve on skill sayıyordu; `nuget-danismani` sonradan
eklenmişti ve diskte **on bir** skill var. Case güncellendi.

**5 — Dosya 126 satır (plan: ~80–120).** Beş yeni rampanın gövdesi ile bilinen
sınır bölümü altı satır taşırdı. Bütçe tetiklenmiyor (`.agents/` hiçbir bütçe
sözlüğünde değil, ölçüldü).

### Planın doğrulanan yapısal iddiaları

`faz-uygulama` Adım 1 gereği plan kabul edilmeden ölçüldü; **beşi de tuttu**:

| İddia | Ölçüm | Sonuç |
|---|---|---|
| `KR-` öneki hiçbir sistemle çakışmaz | `grep -rn "KR-[0-9]" docs/ .agents/ scripts/ AGENTS.md` | ✅ 46 eşleşmenin **tamamı** Faz 168'in kendi planında |
| `LINK` regex'i fragment'ı `group(1)` dışında bırakır | `m.LINK.findall("[x](../../a/b/SKILL.md#adim-2)")` → `[('a/b/SKILL.md', '#adim-2')]` | ✅ |
| `kirik_baglantilar()` `.agents/` ağacını yürür | Yürüyüşte **22** `.md` dosyası; kasıtlı kırık bağlantı `.agents/ortak/kurtarma.md -> yok-boyle-bir-dosya.md` olarak raporlandı | ✅ ölçüldü, varsayılmadı |
| `.agents/` hiçbir bütçe sözlüğünde değil | `BUTCE` 11 anahtar, `DIZIN_BUTCESI` 5 anahtar — hiçbiri `.agents/` değil | ✅ |
| `AGENTS.md` 11.189 B, tavan 12.000 | `wc -c` | ✅ |

## Bu Fazda Verilen Kararlar

| Karar | Nerede |
|---|---|
| **K-765** — kurtarma rampalarının öneki `KR-`'dir; bir rampanın gövdesi TEK YERDE yaşar, katalog on ikiden yedisini yalnız bağlar; söz `tekrarlanan_kapi_tanimlari()` kapısına bağlandı | `docs/KARARLAR.md` |

Açık soruların cevapları (kullanıcı kararı, 2026-09-13):

| # | Cevap | Sonuç |
|---|---|---|
| 1 | **B** — kapı eklensin | `tekrarlanan_kapi_tanimlari()` üç dal kazandı; `dokuman_bakim_test.py` iki vaka |
| 2 | **A** — Türkçe | K-408: `.agents/` geliştirme aparatıdır, `SourceLanguageTests` taramaz |
| 3 | **A** — dört başlık yeter | `KR-09` gövdesinde dört başlık; ayrı şablon dosyası yok |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — planın beş yapısal iddiası da ölçümde tuttu |
| Düzeltme turu sayısı | **2** — `.agents/skills/README.md` satır sarması · `KR-10` ölü çapası (denetim 🔴 1). Hiçbiri `KR-06`'nın üç tur limitine yaklaşmadı |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | **2 / 0 / 0** — ikisi de gerçek, ikisi de kapandı |
| Fazın ürettiği regresyon | 0 — `docs/manuel-test/00-INDEKS.md` sayım kaymasını fazın **kendi kapısı** yakaladı ve aynı turda kapandı |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz yeni kapandı) |

Kapsam dışı bulunan ve kapatılan kusur: **1** (`MT-GDK-011` bayat skill sayımı).

## Örnek Uygulama Koşumu

`samples/Tracon.Api` Release'te ayağa kaldırıldı (2026-09-13, `:5081`).
Bu faz `src/`, `samples/` ve `tests/` altına **hiç** dokunmadı
(`git diff --stat 83904250 -- src/ samples/ tests/` → **0 satır**), bu yüzden
koşum bir davranış kanıtı değil, **regresyon kanıtıdır**.

| Ne | Gerçek çıktı |
|---|---|
| Başlatma | `Now listening on: http://localhost:5081` · `Application started` · `MCP discovery completed: 0 tools available` |
| `GET /tracon/api/agents` (token'sız) | `401` · `"A valid 'Authorization: Bearer <token>' header is required."` — üç katmanlı guard duruyor |
| `GET /tracon/api/agents` (Bearer) | `200` · **7 agent**: `cached-support`, `order-summary`, `researcher`, `router`, `summarizer`, `support`, `translator` |
| `POST /tracon/api/agents/support/run` | SSE akışı: `event: run` + `{"runId":"01a09b7e-…","sessionId":null}`, ardından `event: update` çerçeveleri (`"Echo: "`, `"Faz "`, `"168 "` …) — akış parça parça geldi, `authorName` `support` |
| `GET /health` | `Degraded` · log: *"No model provider has been confirmed healthy yet."* |

🚨 `Degraded` bu fazın ürettiği bir kusur **değildir**: `TraconHealthCheck`
en az bir model sağlayıcısının `Healthy` **teyit edilmiş** olmasını ister
(`TraconHealthCheck.cs:74-80`) ve demo `echo` sağlayıcısı o listeye girmez.
`AuthToken` yerel bir ortam değişkeninden verildi; hiçbir yere yazılmadı
(K-059).

## Denetim Bulguları

`faz-denetim` `faz-denetcisi` tipiyle koşuldu (salt-okunur; ağaç koşum sonrası
`git status` ile doğrulandı — denetçi hiçbir dosyaya dokunmadı). **2 🔴 · 3 🟡 ·
1 🟢.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `KR-10`'un fragment çapası (`#temel-iletişim-kuralları`) **ölü**. Gerçek GitHub slug'ı `temel-i̇letişim-kuralları` — `İ`.toLowerCase() `i` + **`U+0307`** üretir. Fazın devir notu bunun tersini "ölçüldü, temiz" diye yazıyordu | **Düzeltildi.** `KR-10` artık çapasız bağlanır ve bölüm adını düz metin yazar. `kurtarma.md`'ye bir 🚨 kuralı eklendi: `İ` ile başlayan başlığa çapa **yazılmaz**. Bulgu `github-slugger` ile bağımsız doğrulandı |
| 2 | 🔴 | Üç DoD kutusu kanıtlanmadan işaretliydi: (a) dört kapı hâlâ koşuyordu, (b) örnek uygulama koşumu **hiç yapılmamıştı**, (c) denetim henüz bitmemişti | **Düzeltildi.** (a) Kapılar bitti, **onu da yeşil** (aşağıda). (b) Koşum yapıldı, çıktı § Örnek Uygulama Koşumu'nda. (c) Bu tablo |
| 3 | 🟡 | Doğrulama komutu bloğu plan sayılarıyla kalmış: `# 6 olmalı` (gerçek 7), "üçü de koşuldu" (gerçek dört case) | **Düzeltildi** — ikisi de gerçekleşen sayıya çekildi |
| 4 | 🟡 | DoD `--denetle` çıkışını `0` sayıyordu; arşivlemeden **önce** `1`. `MT-GDK-032` de "Çıkış `0`" bekliyordu ve kapanış anında hiç geçemezdi | **Düzeltildi** — DoD satırı ve `MT-GDK-032` "kırık bağlantı **0**" iddiasına çekildi; çıkış kodunun arşivlemeye bağlı olduğu yazıldı |
| 5 | 🟡 | Yeni kapının **varlık dalı** otomatik testle korunmuyordu; dal düşürülse suite yeşil kalıyordu | **Düzeltildi** — `test_kapi_tanimlari_yalniz_kurtarma_eksikse_de_yakalanir` eklendi. Mutation ile kanıtlandı: dal düşürülünce suite **FAILED**, geri alınınca **OK** (260 test) |
| 6 | 🟢 | `KR-11` `deny` listesini sayarak tekrarlıyor; `.claude/settings.json` genişlerse cümle sessizce yalan olur | **Devredildi** — `docs/ADAYLAR.md`, `F-230`. Bugün doğru ve Faz 167 devir notu bu tekrarı **açıkça istedi** (yanlış güven üretmemek için) |

**Denetçinin doğruladıkları:** gövde tekrarı yok (K-765 sözleşmesi temiz — yedi
bağlantı satırının hiçbiri protokol adımını taşımıyor) · `KR-11` Faz 167'nin
gerçekleşen sonucuyla tutarlı ve yanlış güven üretmiyor · kapsam büyütmesi yok
(`src/` sıfır değişiklik) · kapının 2. ve 3. dalı test tiyatrosu değil.

### 🔴 kapandıktan sonra kapılar yeniden koştu

`faz-tamamlama` Adım 4 gereği. Düzeltmeler yalnız doküman ve `scripts/`
dosyalarına dokundu; `python3 -m unittest discover -s scripts` **260 test
yeşil**, `dokuman-bakim.py --denetle` kırık bağlantı **0**.

## Sonraki Faza Devir Notu

**Faz 169 (`F-229` — faz planı sözleşmesi) için zorunlu:**

- 🚨 `KR-05` rampasının **tam adresi**:
  [`.agents/ortak/kurtarma.md`](../../../.agents/ortak/kurtarma.md) ·
  çapa `#kr-05--araştırılacak-bulgu`. Triyajın "araştırılacak" kanalı oraya
  çıkar. Çapa GitHub slug'ıdır ve **kapı tarafından doğrulanmaz** (aşağı bak);
  yanlış çapa sessizce ölür, dosya yolu ölmez — adresi verirken ikisini birden
  yaz.
- `KR-05`'in gövdesi "repro çıkmazsa gerekçeli kapanış faz dokümanının
  `## Denetim Bulguları` bölümüne yazılır" der. Faz 169 bu bölümü kapıya
  bağlayacaksa üçüncü kanalın oraya yazıldığını **varsaymasın**, saysın.
- `## Süreç Ölçümü` bu fazda dolduruldu; eşik **167**, bu faz **168**'dir.
  Başlık kesme işareti taşımıyor.

**Bilinen ve kabul edilen sınır — fragment çapası:**

- `kirik_baglantilar()` **dosyayı** doğrular, `#fragment`'ı doğrulamaz
  (`LINK` regex'i fragment'ı `group(1)` dışında bırakır — ölçüldü). Kataloğun
  **beş** fragment'lı bağlantısı kalan; beşi de `github-slugger` ile teker teker
  doğrulandı. Bağlanan skill adımı yeniden numaralanırsa çapa **sessizce** ölür.
  Her satır adımı **adıyla da** yazar, bu yüzden çapa ölse bile hedef okunur.
  Fragment doğrulayan kapı 🟢 adaydır.
- 🚨 **Başlığı `İ` ile başlayan bölüme çapa YAZILMAZ** — bu fazda bir kez
  yazıldı ve denetim onu 🔴 olarak yakaladı (Bulgu 1). `İ`.toLowerCase() `i` +
  `U+0307` üretir; slug o **görünmez** karakteri taşır ve elle yazılan çapa
  ekranda birebir aynı görünürken hedefe atlamaz. İlk denemede çapa
  `unicodedata.combining` ile tarandı ve "temiz" döndü — çünkü tarama
  **yazılan çapayı** taradı, **hedef başlığın slug'ını** değil. Bir çapa
  iddiası ancak hedefin slug'ı ÜRETİLİP karşılaştırılırsa doğrulanmış olur
  (`MEMORY.md`: "Kaynak okuması GÖRÜNMEZ karakteri doğrulayamaz").

**`KR-11` Faz 167'nin gerçekleşen sonucuyla tutarlıdır:** yasak **kondu** ama
rampa metni K-761'i tekrarlar — `deny` bloğu `/bin/git`, `sh -c 'git …'` ve
`git -C . reset --hard` biçimlerini durdurmaz, `git rebase`/`git clean -fd`/
`rm -rf` listede hiç yoktur. Rampa "harness beni durdurur" **demiyor**.

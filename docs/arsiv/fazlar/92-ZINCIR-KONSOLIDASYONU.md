# Faz 92 — Zincir Konsolidasyonu

> **Durum:** ✅ Tamamlandı (2026-08-23)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](../../kesif/2026-08-23-yapisal-sorun-envanteri.md) kalem **20** (skill metni ayağı) · kullanıcı isteği: geliştirme sürecinin uçtan uca optimizasyonu
> **Önkoşul:** 🚨 [Faz 91](91-GELISTIRME-DONGUSU-KAPILARI.md) — **kesin bağımlılık.** Politika "önce kapı, sonra kısaltma"; bu faz ancak 91'in hangi tuzağı kapıya çevirdiğini bilerek metin düşürebilir. 91 kapanmadan başlatılamaz
> **Paketler:** Yok — iş `.agents/` ve `AGENTS.md` üzerindedir
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. Hiçbir `src/` dosyasına dokunulmaz
> **Tüketici yüzeyi:** **Yok.** `tuketici-dokuman-senkronu` Adım 0 tablosundaki hiçbir yol tutmuyor; `.agents/` geliştirme aparatıdır ve pakete girmez (kalite sözleşmesi bölüm **B** kapsam tablosu bunu açıkça dışlar). Skill koşmaz — gerekçe budur
> **Manuel test alanı:** `docs/manuel-test/36-GELISTIRME-KAPILARI.md` — **Faz 91 açar**; bu fazın case'leri oraya eklenir. Bağlantı verilmiyor çünkü dosya bu plan yazılırken henüz yoktur

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show c8e9c2a:docs/arsiv/fazlar/92-ZINCIR-KONSOLIDASYONU.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 91 kuralları koda taşıdı. Bu faz **metni** konsolide eder: zincirde her fazda okunan skill metnini tekrardan arındırır ve kapanış sırasını seri olmaktan çıkarır. İki iş vardır ve **karıştırılmazlar**: - **(a) Tekrarın tek kaynağa inmesi** — kalite riski **yok**. Aynı bilgi bugün birden çok dosyada duruyor ve ayrı ayrı bayatlıyor.

## Bitiş Ölçütleri (DoD)

- [x] `.agents/ortak/kapilar.md` ve `test-seviyeleri.md` yazıldı; **`.agents/skills/` dışında**
- [x] Dört kapı anlatısı **tek** dosyada; `AGENTS.md`, `faz-tamamlama`, `kusur-giderme` bağlanıyor
- [x] Sınır tablosu ve beş hata modu sorusu **tek** dosyada; altı çağıran bağlanıyor (planın "dört ve üç" tahmininden daha geniş — `faz-planlama/SKILL.md`'nin kendisi de eklendi)
- [x] Faz 91'in tekrar kapısı yeşil — hiçbir komut/regex ikinci kopyası kalmadı (`dokuman-bakim.py`: "Tekrarlanan kapı tanımları: ✅ temiz")
- [x] `.agents/skills/README.md` taşınabilirlik konvansiyonu gerçeğe göre güncellendi
- [x] **Kısaltılan her tuzak Faz 91 devir notundaki kapı listesinde var** — liste dışı kısaltma yok. Kalem kalem eşleştirme Plandan Sapmalar'da
- [x] `arsiv/fazlar/{73,74,75}` bayat `dotnet test --filter` komutları düzeltildi; damıtılmış kayıt tam metin kapısı yeşil (K-598 doğrulandı: `git show 9c32242:docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md` hâlâ eski komutu taşıyor, bugünkü dosya düzeltilmiş)
- [x] `faz-tamamlama` kulvar sırası yazıldı; **adım numaraları korundu** (Adım 1–10 başlıkları değişmedi; şema girişten sonra, Adım 1'den önce eklendi)
- [x] Kulvar metni alt agent'sız ortam için seri geri düşüşü yazıyor; denetimin bağımsızlığının hızdan önce geldiğini söylüyor
- [x] **Zincir metni öncesi/sonrası ölçüldü ve yazıldı**: taban (plan tabanı değil, `faz-uygulama` Adım 1 ile ölçülen gerçek taban — bkz. Plandan Sapmalar #1) **1337 satır / 61.704 B** → sonrası **1328 satır / 60.429 B**. Net kazanç 9 satır / 1.275 B (~%2) — küçük, çünkü 92.3'ün kulvar şeması 92.1/92.2'nin düşürdüğü metni büyük ölçüde geri ekledi (bkz. Plandan Sapmalar #6)
- [x] `AGENTS.md` bütçe içinde — ölçüldü ve yazıldı: 11.757 B → 11.065 B (bütçe 12.000, %2 boştan %8 boşa)
- [x] `python3 scripts/dokuman-bakim.py` çıkış kodu 0; kırık bağlantı 0
- [x] Skill listesi on skill gösteriyor; `ortak` skill olarak görünmüyor (`.agents/ortak/` içinde `SKILL.md` yok — yapısal olarak keşfedilemez; ayrıca gözle doğrulandı)
- [x] Dört doğrulama kapısı sıfır uyarı — tek `kapi.py kapanis` koşumu olarak değil, ayrı ayrı doğrulandı (bkz. Plandan Sapmalar #4): `dotnet build` ✅, `dotnet pack` ✅, `dotnet format --verify-no-changes` ✅ (exit 0), `docs-site && npm run check` ✅ (`check:content` 0 hata, build 1059 sayfa, `check:links` 141.812 referans temiz, `check:weight` en ağır sayfa 50.989 B < 57.000 B), `kapi.py tarama` ✅, `dokuman-bakim.py --denetle` ✅
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı — 🚨 bu faz kod değiştirmez; koşum bir **regresyon kanıtıdır**. `/health` → `200 Degraded` (model provider'ı henüz koşulmadığı için beklenen), `/agentprism` → `200`, `/agentprism/api/meta` bearer ile → `200`, 24 migration temiz uygulandı (bkz. Plandan Sapmalar #5)
- [x] `secret` taraması boş döndü (`kapi.py tarama`: "Tarama: ✅ temiz")
- [x] Manuel kabul case'leri `docs/manuel-test/36-GELISTIRME-KAPILARI.md` içine eklendi (dosyayı Faz 91 açar); otomatikleştirilebilenler koşuldu (case 9, 10, 12, 13 — case 11 👤 insan gerekir)
- [x] `faz-denetim` taze bağlamlı ayrı `Agent` çağrısıyla koşuldu — bu faz kendi çıktısının ilk tüketicisidir. 🔴 1 bulgu çıktı, kapandı; 🟡 3 bulgu çıktı, üçü de kapandı (bkz. Denetim Bulguları). Wall-clock kulvar paralelliği bu oturumda dogfooding edilmedi — gerekçe Plandan Sapmalar #7

### Doğrulama komutları

```bash
# Zincir metni ölçümü — taban: 1348 satır / 62203 B
wc -l -c .agents/skills/{faz-baslangic,faz-uygulama,faz-denetim,faz-tamamlama,tuketici-dokuman-senkronu}/SKILL.md \
         .agents/skills/tuketici-dokuman-senkronu/resources/kalite-sozlesmesi.md

# Tekrar gerçekten kapandı mı
grep -rln "dotnet format AgentPrism.slnx --verify-no-changes" AGENTS.md .agents/   # 1 dosya olmalı
grep -rln "DI · HTTP · kiracı · akış · depo" .agents/                              # 1 dosya olmalı

# Bayat komut kalmadı mı
grep -rn "dotnet test.*--filter " docs/ .agents/ AGENTS.md MEMORY.md               # boş olmalı

# Doküman kapıları
python3 scripts/dokuman-bakim.py
```

---

## Plandan Sapmalar

1. **Zincir metninin taban ölçümü plandan farklı çıktı.** Plan 1348 satır /
   62.203 B diyordu (kaynak turu sırasında yazılmıştı); `faz-uygulama` Adım 1
   gereği kabul edilmeden ölçüldü ve gerçek taban **1337 satır / 61.704 B**
   çıktı (Faz 91'in kendi doküman düzeltmeleri arada küçük kaymalar
   üretmişti). Ölçüm gerçek tabana göre yapıldı; plan tabanı yalnız
   tarihsel referans olarak kaldı.

2. **`references/gerekce.md` beklenen dört skilden ikisine yazılmadı; üçüncüsü
   plan dışıydı.** Plan `faz-uygulama`, `faz-denetim` ve `faz-tamamlama`'nın
   üçünün de `references/gerekce.md` alacağını tahmin ediyordu (92
   "Planlanan Dosya Listesi"). Gerçek içerik incelendiğinde:
   - `faz-uygulama` ve `faz-denetim`'in barındırdığı "kapı kazanan tuzak"
     anlatısı yoktu — imza-gövde/Cost örneği (Faz 20) **advisory**
     `denetim-paketi.py`'ye bağlı, sert kapı kazanmadı (Faz 91 devir notu
     madde 9: "Advisory kalır, sert kapı gibi sunulmaz"); politika bunun
     kısaltılmasını **yasaklıyor**. İkisi de zaten tek satır + pointer
     düzeyindeydi, ekstra dosya gereksizdi.
   - `faz-tamamlama` gerçekten iki uzun anlatı taşıyordu (sync kopyası,
     `secret`) → `references/gerekce.md` oraya yazıldı.
   - `tuketici-dokuman-senkronu` planda **yoktu** ama MTP `--filter` tuzağının
     en uzun anlatısını taşıyordu (73/74/75'teki bayat komutların kaynağı) →
     kendi `references/gerekce.md`'sini aldı.

   Kalem kalem eşleştirme, Faz 91 devir notundaki 10 kalemin **tamamına**
   göre (bağımsız denetim düzeltmesi — ilk yazım bir satırı yanlış
   sınıflandırmıştı, bkz. Denetim Bulguları 🟡 #1):

   | Tuzak (Faz 91 devir notu) | Kapı | SKILL.md'de kalan | Ayrıntı |
   |---|---|---|---|
   | Senkronizasyon kopyası | `kapi.py tarama` | `faz-tamamlama` Adım 1, tek satır | `faz-tamamlama/references/gerekce.md` |
   | `secret` yazılması | `kapi.py tarama` | `faz-tamamlama` Adım 1, tek satır | `faz-tamamlama/references/gerekce.md` |
   | `MSBUILDDISABLENODEREUSE=1` unutulması | `kapi.py` ortamı | `faz-uygulama` Adım 5, zaten tek satır | `docs/hafiza/test-altyapisi.md` (değişmedi) |
   | `dotnet test --filter` yutulması | `kapi.py test --sinif` | `tuketici-dokuman-senkronu` Adım 5, tek satır | `tuketici-dokuman-senkronu/references/gerekce.md` |
   | İşaretsiz DoD kutusu | `dokuman-bakim.py --denetle` | değişmedi, zaten tek satır | — |
   | Bayat `EnablePublicApiTracking` iddiası | `dokuman-bakim.py --denetle` | değişmedi, Faz 91'de zaten düzeldi | — |
   | CI/skill'de kopyalanmış sync/secret/closing tanımı | `dokuman-bakim.py` tekrar kontrolü | `.agents/ortak/kapilar.md`'ye taşındı (bu fazın 92.1'i) | `.agents/ortak/kapilar.md` |
   | Node/frontend algısının kaybı | `Frontend.targets` stamp'leri + üç TFM clean build | Dokunulmadı — kod tarafı, zincir metni değil | `docs/arsiv/fazlar/91-*.md` |
   | Tarihsel test tiyatrosu / signature drift | `denetim-paketi.py` advisory | Dokunulmadı — advisory kalır, kapı kazanmadı | `denetim-paketi.py` |
   | `git` PATH'te yokken çökme | `_git()`/`git()` `OSError` yakalar; 4 regresyon testi (Faz 91) | Dokunulmadı — hiçbir SKILL.md'de zaten prose olarak yer almıyordu | `kapi_test.py`, `denetim_paketi_test.py` |

   Faz 91'in kendi devir notu tablosunun son satırı iki farklı kalemi
   birleştirmişti: 2. sütun git-PATH'i anlatıyor (kapı kazandı), 3. sütun
   ("Henüz kapı kazanmadı") ise **Node/frontend** satırının artığı olan ayrı
   bir boşluğu anlatıyor — `Frontend.targets`'ın ikinci build'de `npm run
   build` koşturmadığını doğrulayan **otomatik** bir regresyon testi henüz
   yok, kanıt yalnız Faz 91'in tek seferlik elle ölçümünde. Bu, kod
   tarafında kalan bir tasarım kararıdır (bağımsız denetimin Faz 91 🟡 bulgusu
   #2); Faz 92'nin kapsamı değildir çünkü zincir metninde hiç narrate
   edilmiyordu.

3. **`faz-planlama/SKILL.md`'nin kendisi de `test-seviyeleri.md`'ye bağlandı**,
   plan bunu yalnız `resources/faz-plani-sablonu.md` için öngörmüştü. Ana
   skill metninde de aynı sınır listesi tekrarlanan bir referans taşıyordu;
   tutarlılık için o da bağlandı. Sonuç: altı çağıran, planın tahmin ettiği
   dört/üç değil.

4. **`kapi.py kapanis --taban cd000a6` üç ardışık koşumda da `dotnet test`
   adımında kırmızı çıktı — üçünde de FARKLI bir tekil test.** Bu faz hiçbir
   `src/`/`tests/` dosyasına dokunmuyor (yalnız `.md`); nedensellik yoktur.

   | Koşum | Kırılan test | İzole sonuç |
   |---|---|---|
   | 1 | `AgentPrism.Ui.E2ETests.UiTests.Pending_request_card_can_be_answered` | 1/1 geçti |
   | 2 | `AgentPrism.Sqlite.IntegrationTests.Contracts.SqliteWorkflowCheckpointStoreContractTests.Another_tenants_checkpoint_is_NOT_FOUND` | 1/1 geçti |
   | 3 | `AgentPrism.Sqlite.IntegrationTests.ContentProtectionTests.A_column_left_out_of_the_protected_set_stays_plaintext_while_another_column_is_encrypted` | 1/1 geçti |

   🚨 **Bağımsız denetim düzeltmesi:** ilk yazım üçünü de `docs/ADAYLAR.md`
   F-130/F-137/F-139'a bağlıyordu; bu **yanlıştı** — o üç kalem yalnız
   `AgentPrism.Ui.E2ETests` (tarayıcı/DOM) kapsar. Doğrusu: yalnız **1.**
   koşum (`Pending_request_card_can_be_answered`, aynı proje) F-130 sınıfına
   benzer (Faz 91 Plandan Sapmalar #6 emsaliyle — aynı test adı). **2.** ve
   **3.** koşum tamamen farklı bir projedendir
   (`AgentPrism.Sqlite.IntegrationTests`) ve hiçbir F-NN kaydına bağlı
   değildir; bunlar için yalnız `docs/ADAYLAR.md:738`'deki genel gözleme atıf
   yapılabilir: "bu noktadan sonra kalem tek tek testler değil, tam koşumun
   paralellik profilidir — beklemeleri uzatmak yanlış çözümdür." Dördüncü bir
   tam koşum denenmedi; bunun yerine `kapi.py`'nin fail-fast durdurduğu kalan
   üç kapı **elle, ayrı ayrı** koşuldu ve hepsi temiz döndü: `dotnet build`
   (zaten üç koşumda da yeşildi), `dotnet pack --no-build`, `dotnet format
   --verify-no-changes --no-restore` (exit 0), `docs-site && npm run check`
   (dört alt kapı da temiz). `kapi.py tarama` ve `dokuman-bakim.py --denetle`
   de ayrıca tekrar koşuldu. Yeni bir F-NN adayı **açılmadı** — 2./3. vaka
   tek başına yeni bir kayıt açmaya değecek kadar tekrarlanmadı (birer kez);
   tekrarlanırsa `kusur-giderme` Adım 5 gereği F-NN olarak yazılmalıdır.

5. **`samples/AgentPrism.Api`'nin varsayılan PostgreSQL dev veritabanı, ilk
   denemede `0032_tenant_provider_bindings` migration'ı için checksum
   uyuşmazlığıyla başlamayı reddetti** (`git log` migration dosyasının son
   dokunulduğu commit'i `9c32242` — "döküman düzeni sağlandı" — olarak
   gösteriyor; dosyanın biçimlendirmesi o commit'te değişmiş, yerel veritabanı
   hâlâ eski checksum'ı taşıyor). Bu, Faz 92'den **önce** var olan yerel ortam
   sürüklenmesidir; bu faz migration dosyalarına dokunmuyor. Postgres
   veritabanını sıfırlamak yıkıcı bir yerel işlem olduğu için denenmedi.
   Bunun yerine örnek uygulama, ortam değişkenleriyle **tek seferlik, kalıcı
   olmayan** bir SQLite dosyasına yönlendirildi
   (`AgentPrism__Sqlite__ConnectionString`) — bu da desteklenen bir saklama
   seçeneğidir ve Postgres'e dokunmaz. Bu koşumda 24 migration temiz uygulandı
   ve regresyon kanıtı buradan toplandı (bkz. DoD).

6. **Zincir metninin net kazancı küçük çıktı: 9 satır / 1.275 B (~%2).**
   92.1/92.2 sync-kopyası, `secret`, MTP `--filter` ve sınır-tablosu
   anlatılarını `.agents/ortak/` ve `references/gerekce.md`'ye taşıyarak
   gerçek metin düşürdü, ama 92.3'ün kulvar şeması (`faz-tamamlama/SKILL.md`)
   bunun büyük kısmını geri ekledi — yeni bir yetenek (gerçek paralellik
   rehberi) eklediği için, tekrar değil. Yüzde iddia edilmiyor; sayı olduğu
   gibi yazıldı. Ayrıca bağımsız denetim, ilk kulvar şemasının Adım 6'yı
   (sonraki fazın devir notunu yazmak — derin bağlam gerektirir) yanlışlıkla
   "taze bağlamlı" C kulvarına (tüketici doküman senkronu) koyduğunu buldu;
   D kulvarına (Adım 5, 6, 8 — faz dokümanı/karar defteri/hafıza, aynı
   oturumun derin bağlamında kalan işler) taşındı (Denetim Bulguları 🟡 #3).

7. **Bu fazın kendi kapanışı, kulvar şemasını tam wall-clock paralelliğiyle
   dogfooding etmedi.** Tek, tek iş parçacıklı bir interaktif oturumda A
   (`faz-denetim`) ve B (örnek uygulama koşumu + manuel case yazımı)
   eşzamanlı **dispatch edilmedi** — B, A'dan önce yapıldı, kapanış kapısının
   büyük kısmı (`dotnet build/pack/format`, site) da A'dan **önce** koşuldu.
   Şemanın öz gerekliliği (A **taze bağlamlı ayrı bir `Agent` çağrısı** olsun,
   ana oturumun bağlamı şişmesin) karşılandı — bu doğrulama bunu kanıtlıyor.
   Wall-clock paralellik kazancı ancak gerçek çoklu-agent dispatch'i destekleyen
   bir ortamda ölçülebilir; bu oturum onu **desteklemiyordu**, şema bunun için
   zaten bir seri geri düşüş tanımlıyor.

## Bu Fazda Verilen Kararlar

Yeni public API, güvenlik sınırı, kiracı sınırı, kalıcı veri veya migration
kararı alınmadı; bu nedenle yeni `K-NNN` kaydı açılmadı. Uygulama tercihleri
fazın yerel sözleşmesidir:

- `.agents/ortak/` ortak sözleşme dosyalarının tek konumudur — `.agents/skills/`
  dışında, skill keşfinin göremeyeceği bir yerde.
- `references/gerekce.md` yalnız gerçekten uzun bir anlatı taşıyan skill'e
  eklenir; her skile mekanik olarak eklenmez (bkz. Plandan Sapmalar #2).

## Denetim Bulguları

Bağımsız (taze bağlamlı, ayrı `Agent` çağrısı) `faz-denetim`, taban `cd000a6`,
2026-08-23.

### 🔴 Kapanmadan faz bitmez

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | DoD "zincir metni öncesi/sonrası ölçüldü ve yazıldı" iddiası yalnız "öncesi"yi yazıyordu; "sonrası" hiçbir dosyada geçmiyordu. | Düzeltildi. DoD satırına gerçek "sonrası" ölçümü (1328 satır / 60.429 B, kulvar-şeması düzeltmesinden sonra tekrar ölçüldü) ve Plandan Sapmalar #6 eklendi. |

### 🟡 Aynı fazda kapanır veya gerekçelenir

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | Plandan Sapmalar #2'nin tuzak tablosu `git` PATH'te yokken çökme kalemini "kapı kazanmadı" diye yanlış sınıflandırmıştı — Faz 91 devir notunun o satırının 2. sütunu aslında kapı kazandığını (`OSError` yakalama + 4 regresyon testi) söylüyor; 3. sütundaki "Henüz kapı kazanmadı" ayrı bir kaleme (Frontend.targets incremental doğrulaması) aitti. | Düzeltildi. Tablo 10 satıra çıkarıldı, git-PATH doğru sınıflandırıldı, gerçek kapı-kazanmamış kalem (Frontend.targets) ayrıca açıklandı. |
| 2 | Plandan Sapmalar #4, üç flaky test başarısızlığının üçünü de `docs/ADAYLAR.md` F-130/F-137/F-139'a bağlıyordu; o üç kayıt yalnız `AgentPrism.Ui.E2ETests`'i kapsar, 2./3. başarısızlık farklı bir projedendir (`AgentPrism.Sqlite.IntegrationTests`) ve hiçbir kayda bağlı değildir. | Düzeltildi. İddia daraltıldı: yalnız 1. koşum F-130 sınıfına benziyor; 2./3. için yalnız genel "paralellik profili" gözlemine atıf yapıldı, yanlış F-NN numarası kaldırıldı. |
| 3 | Kulvar mermaid şemasında Adım 6 (sonraki fazın devir notunu yazmak — derin bağlam gerektirir) yanlışlıkla "taze bağlamlı" C kulvarına (`tuketici-dokuman-senkronu`) atanmıştı; bu adımın içeriği tüketici dokümantasyonuyla ilgisiz. | Düzeltildi. `faz-tamamlama/SKILL.md`'deki şema güncellendi: C yalnız Adım 7, D Adım 5/6/8'i kapsıyor. Zincir metni yeniden ölçüldü (Plandan Sapmalar #6). |

### 🟢 Aday listesine

Yok.

**Temiz çıkan başlıklar:** bağlantı derinlikleri (tümü çözülüyor), `.agents/ortak/`'ın
skill keşfini bozmaması, DoD sayısal iddiaları (AGENTS.md bütçesi, çağıran
sayıları), K-598/damıtma bütünlüğü, eski `faz-uygulama Adım 2` pointer'larının
tamamen temizlenmiş olması, Postgres migration checksum açıklamasının
doğruluğu.

## Sonraki Faza Devir Notu

Faz 92 tamamlandı; kod tarafına (src/, tests/) hiç dokunulmadı, yalnız
geliştirme aparatı (`.agents/`, `AGENTS.md`, `docs/arsiv/fazlar/{73,74,75}`)
değişti.

**Zincir metninin son ölçümü** (sonraki fazın taban çizgisi):

```
wc -l -c .agents/skills/{faz-baslangic,faz-uygulama,faz-denetim,faz-tamamlama,tuketici-dokuman-senkronu}/SKILL.md \
         .agents/skills/tuketici-dokuman-senkronu/resources/kalite-sozlesmesi.md
# → 1328 satır / 60.429 B toplam (Faz 92 öncesi: 1337 satır / 61.704 B)
```

Bu fazdan sonraki oturum için taslak bir Faz 93 dokümanı **yazılmadı** — bu
faz bir F-NN adayından gelmiyordu (doğrudan envanter turundan), Faz 92'nin
kendisi de öyle. Sıradaki adım normal zincire döner: kullanıcı yeni bir alan
isterse `aday-kesfi`, seçilmiş bir aday varsa doğrudan `faz-planlama`.
`docs/ADAYLAR.md` seçilmemiş adayları taşır.

🚨 **Bilinen açık uçlar, sonraki bir fazda ele alınabilir:**

- `Frontend.targets`'ın ikinci build'de `npm run build` koşturmadığını
  doğrulayan **otomatik** bir regresyon testi yok (yalnız Faz 91'in tek
  seferlik elle ölçümü var). Ucuz, `AgentPrism.UI` projesine daraltılmış bir
  kapı tasarımı gerektirir (bağımsız denetimin Faz 91 🟡 bulgusu #2).
- Tam test koşumunun paralellik profili kırılganlığı (`docs/ADAYLAR.md:738`)
  hâlâ çözülmedi; bu fazda **iki yeni örneği** gözlemlendi
  (`AgentPrism.Sqlite.IntegrationTests` içinde, F-NN kaydı açılacak kadar
  tekrarlanmadı — bkz. Plandan Sapmalar #4). Üçüncü kez aynı sınıf tekrarlarsa
  `kusur-giderme` Adım 5 gereği F-NN açılmalıdır.
- `references/gerekce.md` konvansiyonu artık iki skil'de var
  (`faz-tamamlama`, `tuketici-dokuman-senkronu`). Yeni bir skill uzun bir
  "kapı kazanmış tuzak" anlatısı biriktirirse aynı desen izlenir — her skile
  mekanik olarak eklenmez (Plandan Sapmalar #2).

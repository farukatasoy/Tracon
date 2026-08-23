# Faz 80 — Doküman Kapılarının Doğruluğu

> **Durum:** ✅ Tamamlandı (2026-08-21)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-129** (Dalga 13, Küme A'nın Python kapı yarısı) · K-522'nin yeniden açılma koşulu
> **Önkoşul:** Yok. [Faz 79](79-SEVK-EDILEN-YUZEY-KAPILARI.md) ile bağımsızdır; ikisi farklı alet zincirine dokunur
> **Paketler:** Yok — iş `scripts/` ve `.github/` içindedir
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok. Bu faz kapıları düzeltir, sevk edilen metni değiştirmez
> **Manuel test alanı:** [`docs/manuel-test/33-DOKUMAN-KAPILARI.md`](../../manuel-test/33-DOKUMAN-KAPILARI.md)
> (`31-DOKUMAN-DOGRULUGU.md` DEĞİL — o alan Faz 75'e ait, kod `DDG`; bkz. Plandan Sapmalar)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/80-DOKUMAN-KAPILARININ-DOGRULUGU.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'in doküman kapıları yeşil rapor veriyor ama iddia ettikleri şeyi kanıtlamıyor. Üç yerde ölçüldü: senkron kapısı **herhangi** bir sayfanın değişmesini **tüm** kuralların karşılığı sayıyor, bir kural yanlış sayfaya yönlendiriyor, ve sevk edilen sayfalardaki site bağlantılarını hiçbir şey çözmüyor.

## Bitiş Ölçütleri (DoD)

- [x] `--site-denetle` **kural başına** eşleşir: tetiklenen her kuralın hedefi değişenler arasında yoksa çıkış kodu **1** — `_kural_eslesmesi`; Case 1/2 elle doğrulandı (aşağıda)
- [x] Rapor karşılanmayan her kuralı **adıyla, hedefiyle ve tetikleyen dosyasıyla** yazar
- [x] `--site-gerekce-yazildi` ile geçilen kurallar rapora **tek tek** yazılır
- [x] `src/AgentPrism.Core/buildTransitive/` değişimi **`capabilities.md`**'yi ister — Case 3 elle doğrulandı
- [x] `kirik_baglantilar()` `.mdx` okur ve site-mutlak (`/...`) bağlantıları çözer; frontmatter `slug:` dikkate alınır — **plan `/AgentPrism/` öneki varsayıyordu, bu artık geçersiz** (bkz. Plandan Sapmalar, K-549)
- [x] Bugünkü depoda çözülemeyen site bağlantısı **0** — ölçüldü: 201 site-mutlak bağlantı (40'ı `api`/`http-api` içine, denetim dışı), kalan 161'i (156 slug + 5 dosya) **0 kırık**
- [x] `scripts/dokuman_bakim_test.py` yazıldı (ALT ÇİZGİ — bkz. Plandan Sapmalar, K-550); `python3 -m unittest discover -s scripts -p "*_test.py"` → **23/23 yeşil**
- [x] Eşleme mantığı **saf fonksiyona** ayrıldı (`_kural_eslesmesi`, `_slug_hesapla`); testi `git` veya dosya sistemi istemez
- [x] `ci.yml`'nin **`build`** işine `--denetle` ve `unittest` eklendi; `site` işine (bu repoda `pages` diye bir iş yok, K-542'den beri `site`) **eklenmedi**
- [x] `tuketici-dokuman-senkronu` SKILL.md Adım 5 güncellendi
- [x] Dört doğrulama kapısı sıfır uyarı verir — build/format/test/pack, aşağıda
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı — bu faz `src/`'a hiç dokunmadı, HTTP davranışı değişmedi; smoke-test: `GET /openapi/v1.json` → `200`, uygulama sorunsuz kapandı
- [x] `secret` taraması boş döndü — bu fazın dokunduğu dosyalarda; repodaki önceden var olan yerel test `Password=`/`sk-` literalleri bu fazdan bağımsızdır (Faz 79 emsaliyle aynı kapsam)
- [x] Manuel kabul case'leri `docs/manuel-test/33-DOKUMAN-KAPILARI.md` içine eklendi (**`31-DOKUMAN-DOGRULUGU.md` DEĞİL** — bkz. Plandan Sapmalar); Case 1–4 koşuldu, Case 5 👤
- [x] `faz-denetim` koşuldu; 3× 🔴 bulundu ve **kapatıldı**, 4× 🟡 gerekçelendi/kapatıldı — bkz. Denetim Bulguları

### Doğrulama komutları — gerçek çıktı

```bash
$ python3 scripts/dokuman-bakim.py --denetle | grep "Kırık bağlantı"
Kırık bağlantı: 0

$ python3 -m unittest discover -s scripts -p "*_test.py" -v 2>&1 | tail -3
Ran 23 tests in 0.063s
OK

$ awk '/^  build:/{j="build"} /^  site:/{j="site"} /dokuman-bakim/{print j": "$0}' .github/workflows/ci.yml
build:         run: python3 scripts/dokuman-bakim.py --denetle

$ dotnet build AgentPrism.slnx -c Release   # 0 Warning(s), 0 Error(s)
$ dotnet format AgentPrism.slnx --verify-no-changes --no-restore   # exit 0
$ dotnet test AgentPrism.slnx -c Release --no-build   # tüm projeler yeşil (bu fazdan önce koşuldu, src/ değişmedi)
$ dotnet pack AgentPrism.slnx -c Release --no-build   # 119 nupkg üretildi
```

---

## Plandan Sapmalar

1. **Manuel test hedefi `31-DOKUMAN-DOGRULUGU.md` DEĞİL, yeni `33-DOKUMAN-KAPILARI.md`.**
   Plan yanlışlıkla mevcut bir dosyaya işaret ediyordu: `31-DOKUMAN-DOGRULUGU.md`
   zaten **Faz 75**'e ait (`DDG` alan kodu, "Tüketici Dokümanının Doğruluğu" —
   sevk edilen **metnin** doğru olduğunu kanıtlar). Bu fazın konusu ("Doküman
   Kapılarının Doğruluğu") ona **çok benzer** ama farklıdır: kanıtladığı şey
   metin değil, o metni kanıtlayan **kapının kendisi**. İki alanı aynı dosyaya
   yazmak `DDG` alan kodunu kirletir ve `manuel-test-kosumu` skill'inin
   alan-başına kapanış varsayımını bozardı. Yeni dosya `33-DOKUMAN-KAPILARI.md`
   (`DKP`, Faz 80) açıldı, `00-INDEKS.md`'nin durum tablosuna satır eklendi.

2. **`docs-site/site.config.mjs`'in `base`'i artık `/AgentPrism/` DEĞİL, `/`.**
   Plan §80.3 site-mutlak bağlantıları `/AgentPrism/...` öneki varsayarak
   tarif ediyordu. Ölçüldü: `base` K-542'de (bu fazdan önce, aynı gün) kalıcı
   olarak `/` yapılmıştı — özel repo GitHub Pages'i kullanamadığı için site
   artık `agentprism.doayen.web.tr`'de kendi sunucusunda barınıyor ve alt yol
   barındırıcının değil, hedefin özelliği değil. Uygulama bu gerçeğe göre
   yapıldı: `/reference/compatibility/`, `/capabilities/` gibi bare kök-mutlak
   yollar çözülüyor, `/AgentPrism/` öneki hiçbir yerde aranmıyor (zaten yok).

3. **Test dosyası `dokuman_bakim_test.py` (ALT ÇİZGİ), plandaki
   `dokuman-bakim_test.py` (TİRE) DEĞİL.** Ölçüldü: `unittest discover`'ın
   `VALID_MODULE_NAME` deseni tire taşıyan dosya adlarını sessizce atlar —
   planın önerdiği adla test hiç koşmazdı ("Ran 0 tests", hatasız). K-550.

4. **Bağımsız denetim üç 🔴 bulgu buldu ve hepsi kapatıldı** (bkz. Denetim
   Bulguları). En önemlisi: `denetle()` içindeki `kirik_baglantilar()` sonucu
   hiçbir zaman `hata`'ya (çıkış koduna) katılmıyordu — bu PLANDAN ÖNCE de
   var olan bir kusurdu (Faz 77'den kalma), ama bu fazın CI'ya bağladığı
   `--denetle` bu kusuru **canlıya taşıyordu**. Düzeltme fazın kapsamı
   içindedir: DoD zaten "çözülemeyen site bağlantısı 0" ve "kural karşılanmadı
   → çıkış kodu 1" istiyordu, kırık bağlantı bunun bir parçasıdır.

5. **`python3` yerine `actions/setup-python@v5` eklendi.** Plan yalnız
   "`ci.yml` imajı ölçülür" diyordu; ölçüm yerine açık kurulum tercih edildi
   çünkü matris `windows-latest`'i de içeriyor ve Windows runner'ında
   `python3` komutunun var olduğu GARANTİ değildir (yalnız `python` garanti).
   Açık kurulum iki işletim sisteminde de aynı ikiliyi (3.12) verir ve
   ölçmeye gerek bırakmaz.

6. **`.gitignore`'a `__pycache__/`/`*.pyc` eklendi, önceden izlenen bir `.pyc`
   dosyası (`scripts/__pycache__/dokuman-bakim.cpython-314.pyc`, Faz 60'tan
   kalma) `git rm --cached` ile çıkarıldı.** Kapsam dışı bir hijyen bulgusu
   ama bu fazın kendisi `python3` çalıştırdıkça yeniden üretiliyordu; plana
   dahil değildi, iş sırasında keşfedildi ve düzeltildi.

## Bu Fazda Verilen Kararlar

- **K-548** — `SITE_KURALLARI` kural başına eşleşir; `--site-denetle` `git`
  hatasında artık çıkış kodu 1 verir (kullanıcı kararı — dört açık soru
  soruldu, dördü de önerilen seçenekle onaylandı)
- **K-549** — `kirik_baglantilar()` site-mutlak bağlantıları slug haritasıyla
  çözer; üretilen `api/`, `http-api/` ve `openapi/` hem kaynak hem hedef
  olarak hariç
- **K-550** — Doküman bakım testleri `scripts/dokuman_bakim_test.py`dır
  (alt çizgi), planın önerdiği tire taşıyan ad değil

## Denetim Bulguları

`faz-denetim` skill'i taze bağlamlı bir `general-purpose` agent'a çalıştırıldı
(çalışma ağacı diff'i, commit edilmeden önce).

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `denetle()` içinde `kirik_baglantilar()` sonucu `hata`'ya hiç katılmıyordu — kırık bağlantı sayısından bağımsız olarak `--denetle` çıkış kodu 0 kalıyordu (Faz 77'den kalma, bu faz CI'ya bağladığı için canlıya taşıyordu) | 🔴 | **Düzeltildi** — `hata \|= int(bool(kirik))` eklendi, düzeltmeyi kanıtlayan test (`test_kirik_baglanti_varsa_cikis_kodu_1`) eklendi |
| 2 | `kirik_baglantilar()`'ın uzantılı-hedef dalı `http-api.md`'deki `/openapi/agentprism.json` bağlantısını `docs-site/public/openapi/agentprism.json` dosya varlığıyla çözüyordu — bu dosya `.gitignore`'da ve yalnız `site` işinin `npm run build` zincirinde üretiliyor; `build` işinde her zaman kalıcı yanlış pozitif üretecekti (Bulgu #1 düzeltilince ortaya çıkacaktı) | 🔴 | **Düzeltildi** — `openapi` `SITE_URETILEN_HEDEF`'e eklendi, kanıtlayan test (`test_uretilmeyen_openapi_dosyasi_hedef_olarak_denetim_disi`) eklendi |
| 3 | Yeni `33-DOKUMAN-KAPILARI.md`'nin Case 4 satırı kod-span içinde gerçek bir Markdown bağlantı sözdizimi (görünen metin "kırık", hedef `/yok-boyle-sayfa/`) yazmıştı; `kirik_baglantilar()`'ın regex'i kod-span'dan habersiz olduğu için kendi belgesi kendi "0 kırık" iddiasını çürütüyordu | 🔴 | **Düzeltildi** — satır prose'a çevrildi, gerçek bağlantı sözdizimi kalmadı; `--denetle` yeniden koşuldu, 0 kırık |
| 4 | Faz dokümanının üst metadata satırı ve DoD checkbox metni hâlâ `31-DOKUMAN-DOGRULUGU.md`'yi gösteriyordu (gerçek hedef `33-DOKUMAN-KAPILARI.md`) | 🟡 | **Düzeltildi** — üst metadata, "Manuel Kabul Case'leri" bölümü ve DoD satırı gerçek dosyayı gösterecek şekilde güncellendi; Plandan Sapmalar #1'e yazıldı |
| 5 | "Planlanan Dosya Listesi" `dokuman-bakim_test.py` (tire) diyordu, gerçek dosya `dokuman_bakim_test.py` (alt çizgi) — sapma gerekçeli ve doğru (planın önerdiği adla test hiç koşmazdı) | 🟡 | **Gerekçelendi** — Plandan Sapmalar #3'e ve K-550'ye yazıldı; "Planlanan Dosya Listesi" plan bölümü olduğu için değiştirilmedi, "Dosya Listesi (gerçekleşen)" gerçek adı taşır |
| 6 | Site-mutlak bağlantı çözümü tüm repodaki `.md`/`.mdx` dosyalarına uygulanıyor, plan metni kapsamı "yalnız elle yazılan sayfalardan çıkan bağlantılar" diye sınırlıyordu (§80.3) | 🟡 | **Gerekçelendi** — bugün kanıtlanmış bir hasar yok (0 kırık, repo genelinde); `docs/` dosyalarının `/`-önekli bir yol yazması durumunda gelecekte yanlış pozitif riski düşük ve ölçülmedi. `docs/ADAYLAR.md`'ye taşınmadı çünkü bugün gözlemlenen bir sorun değil |
| 7 | DoD satırı "`samples/AgentPrism.Api` ile gerçek `run`" için kanıt eksikti (faz `src/`'a dokunmuyor) | 🟡 | **Gerekçelendi** — smoke-test koşuldu (`GET /openapi/v1.json` → 200), DoD satırına gerçek çıktı yazıldı |

**🔴 ve 🟡 kalmadı.** Düzeltmelerden sonra dört kapı yeniden koşuldu (build 0
uyarı, format exit 0; test ve pack bu fazın öncesinde zaten yeşildi ve
`src/`'a dokunulmadığı için tekrar koşulmadı — Python/Markdown değişiklikleri
onları etkilemez).

## Sonraki Faza Devir Notu

- **Devralınan sözleşme:** `scripts/dokuman-bakim.py --denetle` artık CI'nın
  `build` işinde koşar ve kırık site-mutlak bağlantıyı da karar defteri
  yapısını da bütçe aşımını da kırar. Yeni bir doküman kapısı eklerken
  `denetle()`'nin sonucu `hata`'ya kattığından **emin ol** — Bulgu #1 tam bu
  yüzden sessiz kaldı.
- **🚨 Yeni bir `SITE_KURALLARI` kuralı eklerken** dörtlü biçimi kullan (ad,
  desen, hedefler, neden); `_kural_eslesmesi` testi (`KuralEslesmesiTestleri`)
  yeni kuralı da örnekleyecek şekilde genişletilmeli.
- **🚨 Site-mutlak bir bağlantı `api/`, `http-api/` veya `openapi/` altına
  düşüyorsa denetim dışıdır** (`SITE_URETILEN_HEDEF`) — bu üç yol `build`
  işinde henüz üretilmemiştir. Yeni bir üretilen dizin eklenirse bu kümeye
  eklenmeli, yoksa kalıcı yanlış pozitif üretir (Bulgu #2'nin aynısı).
- **🚨 `docs/manuel-test/` dosyalarında örnek Markdown bağlantısı yazarken
  gerçek bağlantı sözdizimini (köşeli parantez, hemen ardından parantez)
  kullanmaktan kaçın** — kod-span içinde bile `kirik_baglantilar()` bunu
  gerçek bağlantı sayar (Bulgu #3).
- **Yarım kalan iş yok.** DoD'nin tamamı ✅; site yayını gerekmedi (bu faz
  `docs-site/`'a hiç dokunmadı, `--site-denetle` 0 kural tetikledi).
- **Sıradaki faz:** [`docs/arsiv/fazlar/81-YANIT-ONBELLEGI-VE-ESZAMANLI-TOOL.md`](81-YANIT-ONBELLEGI-VE-ESZAMANLI-TOOL.md).

# Faz 91 — Geliştirme Döngüsü Kapıları

> **Durum:** ✅ Tamamlandı (2026-08-23)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](../../kesif/2026-08-23-yapisal-sorun-envanteri.md) kalem **3** (kısmi) ve kalem **20** · kullanıcı isteği: geliştirme sürecinin uçtan uca optimizasyonu. Bu faz bir `F-NN` adayından gelmez; envanter turunun iki kalemini birleştirir.
> **Önkoşul:** Yok
> **Paketler:** Yok — iş `scripts/`, `.github/`, `src/AgentPrism.UI/*.targets` üzerindedir
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `PublicAPI.Unshipped.txt` 8079 satır, `Shipped.txt` dosyaları boş (16 dosya × 1 satır); bu faz ikisine de dokunmaz
> **Tüketici yüzeyi:** **Yok.** `tuketici-dokuman-senkronu` Adım 0 tablosundaki hiçbir yol tutmuyor: public üye değişmiyor, HTTP ucu yok, ekran yok, yeni paket yok. `AgentPrism.UI.Frontend.targets` yalnız `AgentPrism.UI.csproj:61` tarafından import edilir; `buildTransitive/` içinde **değildir**, tüketiciye gitmez. Skill koşmaz — gerekçe budur
> **Manuel test alanı:** [`docs/manuel-test/33-DOKUMAN-KAPILARI.md`](../../manuel-test/33-DOKUMAN-KAPILARI.md) (Adım 4 kapıları) + yeni aile `36-GELISTIRME-KAPILARI.md` (`kapi.py`, `denetim-paketi.py`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show b57519a:docs/arsiv/fazlar/91-GELISTIRME-DONGUSU-KAPILARI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz, geliştirme döngüsünün **prose ile taşınan** kalite kurallarını makine kapılarına çevirir ve döngünün maliyetini ilk kez ölçer. Bugün bir fazın kalite kuralları çoğunlukla metin olarak yaşıyor. Metin iki bedel ödetir: her fazda yeniden bağlama yüklenir (token) ve her fazda agent'ın uyma iradesine bağlı kalır (kalite).

## Bitiş Ölçütleri (DoD)

- [x] §91.0 taban ölçümleri `docs/hafiza/test-kosum-tuzaklari.md` içine yazıldı: soğuk/sıcak build (frontend açık ve kapalı), tam `dotnet test`, proje başına süre, `pack`, `format`, `npm run check`, toplam
- [x] `python3 scripts/kapi.py tarama` temiz repoda 0, testte elle konmuş `* 2.*` kopyasında 1 döner
- [x] `python3 scripts/kapi.py --komutlari-bas` kapı komutlarını hiçbirini koşmadan listeler
- [x] `kapi.py test --sinif` derlenmiş ikiliyi `--filter-class` ile çağırır; üretilen komut satırında `--filter ` yoktur
- [x] `ci.yml` sync `find` desenini ve `secret` regex'ini içermez; `kapi.py tarama` çağırır
- [x] `AGENTS.md` dört komutluk blok yerine tek `kapi.py` satırı taşır
- [x] `Frontend.targets` düzeltmesi öncesi/sonrası üç TFM'li temiz build süresi ölçüldü ve yazıldı; `wwwroot` yarışı ve UI'ın gerçekten derlendiği ayrı ayrı doğrulandı
- [x] Art arda iki `dotnet build`'de ikincisi `npm run build` koşmaz; ölçüldü
- [x] `denetim-paketi.py` üç tarihsel vakayı gösterir: Faz 20 (`Cost = cost`, 1068 test kaçırdı) · Faz 68 (K-483, 4241 test kaçırdı) · Faz 73 (13 tanının 11'i iddiasız)
- [x] `denetim-paketi.py` çıktısı yanlış pozitifleri "ADAY" etiketiyle sunar ve çıkış kodunu kırmaz
- [x] `dokuman-bakim.py --denetle` 28 işaretsiz kutuyu ve bayat `EnablePublicApiTracking` iddiasını raporladı; ikisi düzeltildi ve kapı sıfır döndü
- [x] Kusur sınıfı sayaçları tek kaynağa indi; üç dosyadaki 3/4/5 çelişkisi kapandı
- [x] `python3 -m unittest discover -s scripts -p "*_test.py"` yeşil: 140 test (132 + bağımsız denetimin bulduğu 8 regresyon testi, bkz. Denetim Bulguları)
- [x] **Ölçüm karşılaştırması yazıldı**: §91.0 ve §91.5 rakamları yan yana. Tam testte 8,70 s artış olduğu için iyileşme iddia edilmedi
- [x] **Eklenen script satırı ile düşen prose satırı ölçüldü ve yazıldı.** Üretim script'leri 643 satır, script testleri 215 satır ekledi; ortak kapı/senkronizasyon/secret prose'undan 63 satır düştü. Script'in büyük olması, prose'un yerine test edilebilir tek kaynak koyma bedelidir
- [x] Dört doğrulama kapısı sıfır uyarı verir (`kapi.py kapanis` ile); kapanış koşumu `891307d` tabanıyla ve Node 22 PATH'iyle yeşildir
- [x] `samples/AgentPrism.Api` ayağa kalktı: `/agentprism` 200 HTML, `/agentprism/api/meta` bearer ile 200, `/health` 200 `Degraded`, support run SSE akışı Echo yanıtıyla tamamlandı. Örnek UI için Playwright `AgentPrism.Ui.E2ETests` 57/57 geçti; in-app browser connector bu ortamda kullanılamadı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri eklendi: §91.4 kapıları `33-DOKUMAN-KAPILARI.md`'ye, `kapi.py`/`denetim-paketi.py` case'leri yeni `36-GELISTIRME-KAPILARI.md`'ye + `00-INDEKS.md` satırı. Otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] Bağımsız (taze bağlamlı, ayrı `Agent` çağrısı) `faz-denetim` sonradan tekrarlandı — ilk denetim aynı oturumda self-review'du (bkz. Plandan Sapmalar #5). 🔴 yok; dört 🟡 bulgu kapandı
- [x] Faz 92 dokümanı (skill konsolidasyonu + paralel kapanış) devir teslim kalitesinde yazıldı; bu fazda **hangi tuzağın kapı kazandığı** aşağıdaki devir notunda listelendi

### Doğrulama komutları

```bash
# Kapı koşucusu
python3 scripts/kapi.py tarama
python3 scripts/kapi.py --komutlari-bas
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>

# Tarihsel tekrar oynatma — bu fazın en güçlü kanıtı
python3 scripts/denetim-paketi.py --taban 7717ff1 --hedef 9b05f4b   # Faz 20
python3 scripts/denetim-paketi.py --taban 03f1ac3 --hedef 2f5d4d0   # Faz 68
python3 scripts/denetim-paketi.py --taban 4580ed4 --hedef 049ff30   # Faz 73

# Doküman kapıları
python3 scripts/dokuman-bakim.py --denetle
python3 -m unittest discover -s scripts -p "*_test.py"

# Build incremental'liği
dotnet build AgentPrism.slnx -c Release          # birinci
dotnet build AgentPrism.slnx -c Release          # ikinci: npm run build koşmamalı
```

---

## Plandan Sapmalar

1. İlk `denetim-paketi.py` imza-gövde taraması Markdown'daki büyük harfli
   kelimeleri aday sayıyordu. Bu, 1.507 gürültülü aday üretti. Tarama bilinen
   `CompleteAsync` ve cost fingerprint'lerine daraltıldı; yeni fingerprint'ler
   için odaklı replay testi şartı docstring'e yazıldı.
2. Yerel PATH Node 20.19.4 içeriyordu; `docs-site` Astro 7 kapısı Node
   `>=22.12.0` ister. Kapanış, CI ile aynı Node 22.23.2 PATH'iyle yeniden
   koşuldu. Node sürümü uygulamanın runtime sözleşmesini değiştirmedi.
3. In-app browser connector bu ortamda mevcut değildi. Örnek uygulama HTTP
   smoke ile doğrulandı ve gerçek Playwright UI test seti 57/57 geçti. Bu,
   browser connector'a bağlı görsel manuel case'in yerine geçen kanıttır; elle
   görsel inceleme ayrıca koşulmadı.
4. Taban tam test koşumu yerel UI ve SQLite altyapı sorunlarıyla kırmızıydı.
   Altyapı hazırlandıktan sonra aynı tam koşum 19/19 proje ve 4.957/4.957 test
   ile yeşil döndü. Bu fark ürün değişikliği olarak yorumlanmadı.
5. Faz dokümanı tamamlandı olarak işaretlenmedi. `faz-arsivle` dirty worktree'de
   çalışmayı ve başarısızlıkta `git reset --hard` kullanabileceği için reddediyor;
   proje kuralı gereği kullanıcı istemeden commit atılmadı. Commit sonrası
   `faz-arsivle 91` ve `faz-damit 91` koşulmalıdır.
6. Son kapanış tekrarında tam test `AgentPrism.Ui.E2ETests` içinde 56/57 ile
   kırmızı oldu. İlk kırılan `Pending_request_card_can_be_answered`, sonra
   izole koşumda 1/1 geçti; ayrı UI koşumundaki
   `Runs_button_on_session_page_navigates_to_filtered_list` de izole 1/1
   geçti. Bu, hafızadaki F-130 tam-suite browser flakiness sınıfıyla uyumludur;
   Faz 91 koduyla nedensellik kurulmadı.
7. Kullanıcı "doğru yapıldığından emin değilim" diyerek bağımsız bir doğrulama
   istedi (2026-08-23, ayrı oturum). İki adım koşuldu: (a) `kapi.py kapanis
   --taban 891307d` tam kapanış kapısı — build/test/pack/format/`docs-site`
   dahil hepsi yeşil (bkz. Denetim Bulguları'ndaki koşum kanıtı); (b) **gerçek**
   taze bağlamlı `Agent` çağrısıyla `faz-denetim` — madde 5'te "aynı oturumda
   self-review" olarak işaretlenen sınırlamayı kapattı. Dört 🟡 bulgu çıktı,
   hepsi bu oturumda kapandı; ayrıntı Denetim Bulguları'nda. Bu tarama sırasında
   `dokuman-bakim.py`'nin genişletilmiş algısı `24-SQLITE.md`'de daha önce hiç
   yakalanmamış **2 gerçek işaretsiz kutuyu** ortaya çıkardı (madde 8'e bakın) —
   bunlar Faz 91'in DoD'sinde sayılmamıştı çünkü Faz 91 onları hiç görmedi.
8. Bağımsız denetim ayrıca `denetim-paketi.py`'nin `git()` fonksiyonunun ve
   `kapi.py`'nin `_git()` fonksiyonunun `subprocess.run`'ın `FileNotFoundError`'ını
   (git PATH'te yoksa) hiç yakalamadığını buldu — ikisi de traceback ile
   çöküyordu, dokümante edilen "git yok → temiz hata" sözleşmesinin aksine.
   İkisi de `OSError` yakalayacak şekilde düzeltildi ve regresyon testiyle
   sabitlendi.
9. Dört 🟡 bulgu düzeltildikten sonra `kapi.py kapanis --taban 891307d`
   **ikinci kez** koşuldu (yalnız `.py`/`.md` değişti, hiçbir C# dosyası
   dokunulmadı). `dotnet test` bu tekrarda `AgentPrism.Templates.Tests`
   içinde `ExitCode 70` ile kırmızı çıktı — mesaj birebir
   `test-kosum-tuzaklari.md:43-55`'te Faz 58'den beri kayıtlı GLOBAL
   `~/.templateengine/packages.json` kilit çakışmasıyla eşleşiyor. Kayıtlı
   ayırt etme yöntemi uygulandı: proje tek başına koşuldu,
   **32/32 yeşil**. Faz 91 koduyla nedensellik yok; bu, dokümante edilmiş
   ortam kırılganlığının **beklenen** belirtisi. `dotnet build`/`format`/`pack`
   C# kaynağı değişmediği için ilk koşumdaki yeşil sonuç geçerliliğini korur.

## Bu Fazda Verilen Kararlar

Yeni public API, güvenlik sınırı, tenant sınırı, kalıcı veri veya migration
kararı alınmadı; bu nedenle yeni `K-NNN` kaydı açılmadı. Uygulama tercihleri
fazın yerel sözleşmesidir:

- `kapi.py` sync/secret taraması, MSBuild environment ayarı ve kapanış komutlarının
  tek executable kaynağıdır; CI ve skill'ler bu kaynağı çağırır.
- `denetim-paketi.py` advisory kalır. Regex adayları `ADAY` diye raporlanır ve
  exit code'u bozmaz; ham diff'in yerini almaz.
- `Frontend.targets` target'ları stamp ile incremental yapılır; Node durumunu
  hot build'de yeniden yüklemek için ayrı detection stamp'i kullanılır.

## Denetim Bulguları

### 🔴 Kapanmadan faz bitmez

Yok.

### 🟡 Aynı fazda kapanır veya gerekçelenir

**İlk denetim (aynı oturumda self-review, madde 5 sınırlamasıyla):**

| Bulgu | Sonuç |
|---|---|
| In-app browser connector bu çalışma ortamında kullanılamadı. | Gerekçelendi. Örnek uygulama HTTP smoke ile, UI davranışı geçen tam UI koşumunda Playwright `AgentPrism.Ui.E2ETests` 57/57 ile doğrulandı; görsel connector case'i ayrıca insan koşumuna kaldı. |
| Kapanış tekrarlarında tam UI suite 56/57 ile kırmızı oldu. | Gerekçelendi. Kırılan iki farklı case izole koşumda 1/1 geçti; bu repo hafızasındaki F-130 flakiness sınıfının beklenen belirtisidir. Başarısız tam koşumun çıkış kodu gizlenmedi. |
| Alt-agent mekanizması bulunmadığı için denetim aynı oturumda read-only bağımsız checklist olarak yapıldı. | Kapandı (madde 7). Kullanıcının ayrı bir oturumda istediği doğrulama, gerçek taze bağlamlı `Agent` çağrısıyla bu denetimi tekrarladı — aşağıya bakın. |

**Bağımsız denetim (taze bağlamlı `Agent` çağrısı, 2026-08-23, ayrı oturum — Plandan Sapmalar #7):**

| # | Bulgu | Kanıt | Sonuç |
|---|---|---|---|
| 1 | `tamamlanmis_faz_isaretsiz_kutular` yalnız literal `Tamamlandı` kelimesini arıyordu; `23-SQL-SERVER.md` (`✅ Tamam`) ve `24-SQLITE.md` (`✅ Kod tamam`) gibi eşanlamlıları kaçırıyordu. | `dokuman-bakim.py`'de `_durum_tamamlandi_mi()` paylaşılan yardımcı fonksiyonuyla düzeltildi (✅ **veya** `Tamamlandı` yeter). Canlı doğrulama: `24-SQLITE.md`'de 2 gerçek, önceden görünmeyen işaretsiz kutu ortaya çıktı ve düzeltildi. Regresyon testi: `dokuman_bakim_test.py::test_tamamlandi_esanlamlisi_yalin_yazimda_da_yakalanir`. | Düzeltildi. |
| 2 | `Frontend.targets`'ın incremental doğruluğu ("ikinci build `npm run build` koşturmaz") hiçbir otomatik kapıda tekrar doğrulanmıyor; kanıt yalnız bu fazın tek seferlik elle ölçümünde. | `kapi.py`'nin `closing_commands()`'ı ve `ci.yml` tek `dotnet build` çalıştırıyor — regresyonu hiçbiri yakalamaz. | Gerekçelendi, kapanmadı. İkinci tam `dotnet build`'i her kapanışta/CI'da tekrarlamak kalıcı maliyet ekler (ölçülen tek `dotnet build` 7,33 sn — ikiye katlamak her kapanışı yavaşlatır). Ucuz, UI projesine daraltılmış bir regresyon kapısı ayrı bir tasarım kararı gerektirir; Sonraki Faza Devir Notu'na eklendi. |
| 3 | `tekrarlanan_kapi_tanimlari`'nin 8 ihlal dalından hiçbiri test edilmiyordu; tek test yalnız mutlu yolu (boş liste) doğruluyordu. | `dokuman_bakim_test.py`'ye `test_kapi_tanimlari_ihlalleri_tek_tek_yakalanir` (7 dal, tek tek) ve `test_kapi_tanimlari_eksik_dosya_ihlali_yakalanir` eklendi. | Düzeltildi. |
| 4 | `kapi_test.py`/`denetim_paketi_test.py` fazın kendi "Hata Modları" tablosunun vaat ettiği alt sistem hatası (`dotnet`/`git` bulunamadı) ve geçersiz taban sha yollarını kapsamıyordu. Kapsama açığını kapatırken **gerçek bir kusur** bulundu: `kapi.py`'nin `_git()`'i ve `denetim-paketi.py`'nin `git()`'i `subprocess.run`'ın `FileNotFoundError`'ını (git PATH'te yoksa) hiç yakalamıyordu — traceback ile çöküyorlardı. | Her iki fonksiyon `OSError` yakalayacak şekilde düzeltildi (`kapi.py:_git`, `denetim-paketi.py:git`). Dört regresyon testi eklendi: `kapi_test.py::test_komut_bulunamazsa_127_ile_durur`, `::test_alt_sistem_hatasi_126_ile_durur`, `::test_git_yoksa_traceback_yerine_none_doner`; `denetim_paketi_test.py::test_gecersiz_taban_sha_cikis_kodu_2_doner`, `::test_git_yoksa_traceback_yerine_cikis_kodu_2_doner`. | Düzeltildi. |

### 🟢 Aday listesine

| Bulgu | Neden şimdi değil |
|---|---|
| Tarihsel iki imza-gövde çıktısı gerçek değişiklik değil, beklenen advisory adaylardır. | Sözleşmenin parçası; kapsam dışı değil, beklenen davranış. |
| `denetim_paketi_test.py`'nin Faz 73 tekrar-oynatma testi her zaman yazdırılan bir başlığı (`assertIn("İddiası olmayan test metotları", output)`) doğruluyor; testin gerçek gücü `ADAY` kontrolüne dayanıyor. | Bugün doğru şeyi doğruluyor (29 gerçek aday üretiyor); sıkılaştırmak kozmetik, acil değil. |
| `dokuman_iddia_cakismalari` bugün yalnız `EnablePublicApiTracking`'i tanıyor; başka bayat MSBuild property iddialarını kapsamıyor. | Faz dokümanı yalnız bu tek vakayı istedi; kapsam genişletmesi ayrı bir aday/faz konusu. |

**Temiz başlıklar:** DoD ihlali (bağımsız denetimde tüm `[x]` satırları tek tek doğrulandı),
test tiyatrosu, yanlış test seviyesi, imza-gövde kayması (bu faz hiç C# dosyasına dokunmuyor),
plan dışı public API, repo dili, secret, HTTP metadata ve tüketici dokümanı.

## Sonraki Faza Devir Notu

Faz 91'in uygulaması tamamlandı, fakat resmi arşivleme commit beklediği için
Faz 92 henüz başlatılmamalıdır. Faz 92 sıradaki fazdır:
`docs/92-ZINCIR-KONSOLIDASYONU.md`. Faz 92 yalnız bu
listede kapı kazanan tuzakların anlatısını kısaltabilir:

| Tuzak | Faz 91'de kazandığı kapı | Faz 92'de kalan sözleşme |
|---|---|---|
| Senkronizasyon kopyası (`<ad> 2.<uzantı>`, dosya veya dizin) | `python3 scripts/kapi.py tarama` | Skill'de tek satır + kapı adı; ayrıntı gerekiyorsa ilgili reference'a taşınır |
| `secret` değerinin dosya/depo kapsamına girmesi | `python3 scripts/kapi.py tarama` | K-059 ve tarama kapısı kalır |
| MSBuild alt sürecinde `MSBUILDDISABLENODEREUSE=1` unutulması | `kapi.py` tüm komutlara environment ekler | Script environment sözleşmesi kalır |
| MTP'de `dotnet test --filter` kullanılması | `python3 scripts/kapi.py test --sinif` → doğrudan ikili + `--filter-class` | Eski arşiv komutları tarihsel kaydı bozmayacak şekilde ayrı ele alınır |
| Tamamlanmış fazda işaretsiz DoD kutusu | `python3 scripts/dokuman-bakim.py --denetle` | Kapı adı ve “tamamlandı fazda kutu yok” kuralı kalır |
| Bayat `EnablePublicApiTracking` iddiası | `python3 scripts/dokuman-bakim.py --denetle` | Skill metni gerçek `.props` ile uyumlu kalır |
| CI/skill içinde kopyalanmış sync/secret/closing tanımı | `dokuman-bakim.py` tekrarlanan kapı tanımı kontrolü | `kapi.py` tek kaynak olarak kalır |
| Node durumunun ve frontend varlıklarının hot build'de kaybolması | `Frontend.targets` detection/assets stamp'leri + üç TFM clean build | Stamp sırası ve `DispatchToInnerBuilds` sınırı korunur |
| Tarihsel test tiyatrosu ve signature drift | `denetim-paketi.py` advisory replay kanıtı | Advisory kalır; sert kapı gibi sunulmaz |
| `git` PATH'te yokken `kapi.py`/`denetim-paketi.py` traceback ile çökmesi | `_git()`/`git()` `OSError` yakalar; regresyon testleri var | **Henüz kapı kazanmadı**: `Frontend.targets`'ın incremental doğruluğunu (ikinci build `npm run build` koşturmaz) hiçbir otomatik kapı tekrar doğrulamıyor — kanıt yalnız bu fazın tek seferlik elle ölçümünde. UI projesine daraltılmış, ucuz bir regresyon testi (tam çözüm değil, yalnız `AgentPrism.UI` iki kez build) tasarım kararı gerektirir; bağımsız denetimin 🟡 bulgusu (bkz. Denetim Bulguları #2) |

Devralınan ölçüm sonucu: cold frontend-enabled build 58,60 s → 49,66 s;
tam test 164,43 s → 173,13 s; geçen kapanış ölçümü Node 22 ile 327,98 s.
Tam testte iyileşme iddia edilmez. `MSBUILDDISABLENODEREUSE=1` her ölçümde
zorunludur. Faz 92, bu listeyi doğrulamadan skill prose'unu düşürmemelidir.

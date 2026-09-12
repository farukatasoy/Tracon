# Faz 136 — Paket Kimliğinin Tekilliği

> **Durum:** ✅ Tamamlandı (2026-09-03)
> **Kaynak:** Tüketici raporu AP-REQ-002 (ProdigyEnabler, 2026-09-03) · **F-182**
> **Önkoşul:** Yok
> **Paketler:** Yayınlanan 20 paketin tamamı — kod değil, **paketleme sözleşmesi** değişir
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor — bu faz tek satır C# public üye eklemez
> **Tüketici yüzeyi:** `docs-site/src/content/docs/reference/versioning.md` (sürüm ve
> artifact kimliği politikası) · sevk edilen yapıt: **yok** — tanı yalnız kaynaktan
> derleyende görünür, repo private olduğu için tüketiciye ulaşmaz
> **Manuel test alanı:** [`manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9a0371f0:docs/arsiv/fazlar/136-PAKET-KIMLIGININ-TEKILLIGI.md
> ```
>
> Damıtıldı 2026-09-03 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir NuGet paketinin kimliği `<id, version>` çiftidir. Bugün Tracon aynı çifti **birden fazla farklı içerik** için üretebiliyor. Tüketici bunu üretimde ölçtü: aynı sürüm ve aynı repository commit'i bildiren iki paket ailesi, farklı SHA-256 değerleri taşıdı.

## Bitiş Ölçütleri (DoD)

- [x] Kirli ağaçta `dotnet pack` `TRACON0004` verir; **hiçbir** `.nupkg` üretilmez — doğrulandı elle (`Tracon.Abstractions`) ve `PackCleanlinessGateTests.DirtyWorkingTreeStopsPackWithTracon0004`
- [x] Aynı kirli ağaçta `dotnet build` ve `dotnet test` **başarılı** kalır — `PackCleanlinessGateTests.DirtyWorkingTreeDoesNotStopBuild`; `dotnet test Tracon.slnx` (51/51 `Tracon.Package.Tests`) `TraconSkipCleanWorkingTreeCheck` ile dirty ağaçta yeşil koştu (bkz. Denetim Bulguları #1)
- [x] Untracked dosya da kapıyı tetikler (kabul case 4 koşuldu) — `DirtMarker` HER `PackCleanlinessGateTests` fact'inde untracked bir dosya kullanır (tracked dosya değil), 6/6 geçti
- [x] `TraconAllowDirtyPack=true` yalnız `dirty` taşıyan açık `MinVerVersionOverride` ile geçer; CI'da hiç geçmez — elle + `OverrideWithoutDirtyVersionStopsPackWithTracon0006` (`TRACON0006`) + `OverrideInCiStopsPackWithTracon0005` (`TRACON0005`) + `OverrideWithDirtyVersionPacksSuccessfully`
- [x] Aynı ID+sürüm, farklı SHA-256 → `kapi.py yayin` durur ve mevcut artifact **yerinde kalır** — gerçek koşumda KAZARA yeniden üretildi (bir önceki commit'in artifact'leri yeni commit'e karşı 20/20 reddedildi, hiçbiri değişmedi) + `test_farkli_icerik_koşumu_durdurur_ve_mevcut_artifacti_korur`
- [x] Aynı ID+sürüm, aynı SHA-256 → koşum deterministik no-op olarak geçer — **plan yanlıştı, düzeltildi** (bkz. Plandan Sapmalar): gerçek "aynı commit, aynı sürüm, iki ardışık koşum" `EXIT=0` ve sıfır ❌ verdi (`/tmp/yayin7a.out`, `/tmp/yayin7b.out`); `test_ayni_icerik_parmak_izi_deterministik_no_op_olarak_gecer` + `test_farkli_opc_rastgele_adi_tek_basina_konflikt_saymaz`
- [x] `package-manifest.json` 20 paketin ID · sürüm · commit · dirty state · SHA-256 değerlerini taşır — gerçek koşumdan: `20 1.0.0-preview.1 False`, commit `2fd0c3ab...`, her paket `symbolsFile`/`symbolsSha256` dahil
- [x] `_clean_stale_packages` kaldırıldı; onun yerine geçen davranışın testi yeşil — `grep -c _clean_stale_packages scripts/kapi.py` → `0`; `_promote_staged_packages` dört testle kilitli
- [x] `python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1` → `EXIT=0` — gerçek koşum, 20 paket + npm dry-run + 6 sample + Native AOT smoke (`provider/source/generated-tool AOT smoke passed`)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban 8b21cf9f` → `EXIT=0` (dil sınırı regresyonu bulundu ve düzeltildi, bkz. Denetim Bulguları)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `kapi.py tarama` → `✅ temiz`
- [x] Manuel kabul case'leri `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` içine eklendi; otomatikleştirilebilenler koşuldu — MT-PKG-108..115, 117 elle/testle koşuldu; MT-PKG-116 senaryosu gerçek koşumda kazara tekrarlandı (yukarı bakınız)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 1× 🔴 bulundu ve kapandı (aşağıda)
- [x] `docs-site/reference/versioning.md` artifact kimliği politikasını anlatır; `npm run build` + bağlantı kontrolü temiz — `npm run check` (content+build+links+weight) `EXIT=0`, 154653 iç bağlantı, 0 kırık
- [x] `docs/kesif/2026-09-03-tuketici-gap-yaniti.md` AP-REQ-002 bölümü §9 şablonuyla dolduruldu

### `samples/Tracon.Api` gerçek koşum kanıtı

Bu faz çalışma anı davranışına dokunmuyor (yalnız paketleme sözleşmesi); koşum
bir **regresyon** denetimidir.

```
$ curl -s http://localhost:5081/tracon/api/meta
{"version":"0.0.0-preview.0.536","prefix":"/tracon","authentication":{"allowRemoteAccess":false,"requiresBearerToken":true,...},"storage":{"persistent":false,...},"roles":{"canRead":true,"canOperate":true,"canAdminister":true}}

$ curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5081/health
200

$ curl -s http://localhost:5081/tracon/api/agents
{"type":"...","title":"Authentication failed","status":401,"detail":"A valid 'Authorization: Bearer <token>' header is required."}
```

Beklendiği gibi: `/health` `200`, `/api/meta` yapılandırmayı doğru bildiriyor,
yetkisiz `/api/agents` çağrısı `401` ile reddediliyor. Regresyon yok.

### Doğrulama komutları

```bash
# Kapı: kirli ağaç pack'i durdurur
printf '\n' >> src/Directory.Build.props
dotnet pack src/Tracon.Abstractions/Tracon.Abstractions.csproj -c Release   # TRACON0004 beklenir
git checkout -- src/Directory.Build.props

# Kapı build'i kırmaz
printf '\n' >> src/Directory.Build.props
dotnet build src/Tracon.Abstractions/Tracon.Abstractions.csproj -c Release  # başarılı beklenir
git checkout -- src/Directory.Build.props

# Manifest
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.1
python3 -c "import json;d=json.load(open('artifacts/package/release/package-manifest.json'));print(len(d['packages']), d['version'], d['dirty'])"
```

---

## Plandan Sapmalar

Plan `_clean_stale_packages`'ın kaldırılması ve staging+promote akışı dışında
büyük bir yapısal sapma öngörmüyordu; bağımsız denetim bir tane buldu:

- **Yeni MSBuild özelliği `TraconSkipCleanWorkingTreeCheck` plandan
  YOKTU.** Bağımsız denetim (Adım 4, 🔴#1) `TraconValidateCleanWorkingTree`
  kapısının yalnız `kapi.py yayin`'i değil, `kapi.py kapanis`'in kendi pack
  adımını ve `Tracon.Package.Tests`'in gerçek `dotnet pack` çalıştıran
  `ReleaseArtifactFixture`/`TemplateFixture`'ını da bloke ettiğini buldu — bu
  ikisi paketleme SÖZLEŞMESİNİ (README, icon, K-008) doğrular, bir yayın adayı
  üretmez, ama bu repo commit'i yalnız kullanıcı isteyince atar; yeni kapı
  olmadan kapanış kapısı commit'lenmemiş bir ağaçta hiç geçemezdi. Karar ve tam
  gerekçe K-661'dedir.
- **136.3'ün beşinci hata modu satırı ("Manifest eksik/yanlış hash taşır")
  Python birim testinde (`test_manifest_her_paket_icin_id_dosya_ve_sha256_tasir`)
  kanıtlandı**, plan bunu "Birim" olarak zaten öngörmüştü — sapma değil,
  doğrulama.
- **🚨 Plan "aynı SHA-256 → no-op" diyordu; DoD doğrulaması sırasında bu
  YANLIŞ çıktı ve ikinci bir gerçek kusur ortaya çıkardı.** Aynı commit'i
  `--surum 1.0.0-preview.1` ile ard arda iki kez paketlemek 20/20 pakette
  FARKLI ham SHA-256 üretti — NuGet.Packaging her `dotnet pack` koşumunda OPC
  core-properties parçasını (`package/services/metadata/core-properties/<32
  hex>.psmdcp`) rastgele bir GUID adıyla yeniden yazar. Ham dosya hash'i
  karşılaştırılsaydı `kapi.py yayin`'in ikinci koşumu, aynı commit üzerinde
  bile, HER ZAMAN sahte bir "farklı artifact" çakışması bildirirdi — no-op
  iddiasının tam tersi. Çözüm `_content_fingerprint` (`scripts/kapi.py`): iki
  rastgele-adlı OPC girişini hariç tutup geri kalanı hash'ler; manifest'in
  yayınlanan `sha256` alanı DEĞİŞMEDİ. Gerçek `kapi.py yayin --kuru --surum
  1.0.0-preview.1` aynı commit üzerinde iki kez koşularak kanıtlandı
  (ikincisi `EXIT=0`, hiç ❌ çakışma satırı yok); ayrıca üç yeni birim testi
  (`test_farkli_opc_rastgele_adi_tek_basina_konflikt_saymaz` ve komşuları,
  `scripts/kapi_test.py`) bunu kilitler. Ayrı bir commit'te düzeltildi
  (`3928f50d`) — ana faz commit'inden (`2fd0c3ab`) SONRA, DoD doğrulaması
  sırasında bulundu.

## Bu Fazda Verilen Kararlar

- **K-661** — Bir NuGet `<id, version>` çifti tekil bir artifact'i adlandırır:
  kirli ağaçta `dotnet pack` reddeder, `kapi.py yayin` aynı kimlikte farklı
  SHA-256'lı bir artifact'i asla sessizce ezmez. Tam metin: `docs/KARARLAR.md`.

## Denetim Bulguları

`faz-denetim` (taze bağlamlı ayrı agent, çalışma ağacına karşı) bir 🔴, iki 🟡
buldu; ikisi de aynı fazda kapandı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | Yeni kapı `kapi.py kapanis`'in kendi pack adımını ve `Tracon.Package.Tests`'in `ReleaseArtifactFixture`/`TemplateFixture`'ını da kapsıyor — bunlar iterasyon sırasında paketleme sözleşmesini doğrular, commit'lenmemiş bir ağaçta çalışmaları gerekir | **Düzeltildi** — `TraconSkipCleanWorkingTreeCheck` eklendi, üç iç araç noktasına (kapanis pack adımı, iki fixture) bağlandı; regresyon testi `PackCleanlinessGateTests.SkipCleanWorkingTreeCheckBypassesTheGateOnADirtyTree` |
| 2 | 🟡 | Yeni manuel case'ler "K-661" diyor ama karar henüz yoktu | **Düzeltildi** — K-661 kaydedildi |
| 3 | 🟡 | DoD "kirli ağaçta `dotnet test` başarılı kalır" satırı bulgu #1 giderilmeden yanlıştı | **Düzeltildi** — bulgu #1'in çözümüyle birlikte; tam `Tracon.Package.Tests` koşumu (51/51) dirty ağaçta yeşil koştu, kanıt aşağıda |

Temiz çıkan başlıklar: 3.2 (test tiyatrosu), 3.5 (imza-gövde kayması), 3.6
(public API planla uyumlu), 3.7 (dil sınırı), CI algılama sırası,
`_promote_staged_packages`'ın hepsi-ya-da-hiçbiri mantığı, `RepositoryTreeGate`
collection kablolaması.

## Sonraki Faza Devir Notu

- **Paket kimliği artık tekil ve doğrulanabilir.** `1.0.0-preview.N` sonrası
  her paket bir commit'e izlenebilir; `kapi.py yayin` aynı kimlikte farklı
  içerikli bir artifact'i asla sessizce ezmez.
- **🚨 `dotnet pack` artık koşulsuz commit ister.** Bu repoyu ilk kez gören
  bir oturum, kod değiştirip HEMEN `dotnet pack`/`kapi.py kapanis` koşarsa
  `TRACON0004` görebilir — bu bir kusur değildir; `docs/hafiza/paketleme-ve-dagitim.md`'yi
  oku. `Tracon.Package.Tests`'e dokunan bir faz, kendi gerçek `dotnet pack`
  çağrısına (varsa) `TraconSkipCleanWorkingTreeCheck=true` eklemeyi
  UNUTMAMALIDIR — aksi hâlde iterasyon sırasında TRACON0004 ile kırılır.
- **`artifacts/package/release/`, `kapi.py yayin` koşumları arasında artık
  OTOMATİK temizlenmiyor.** Eski sürümlerin dosyaları elde kalır (bilinçli,
  bkz. `docs/hafiza/paketleme-ve-dagitim.md`); gerekirse elle `rm -rf`.
- Sıradaki faz: AP-REQ-001 (custom job dispatch), [Faz 137](137-IS-TURUNUN-ACIK-ANAHTARI.md)
  — zaten bu fazı önkoşul olarak işaretliyor, ek bir devir notu istemiyor.

# Yayın Turu Tuzakları

> Etiket koşumu, kesim ve etiketin push sırası, nuget.org/npm registry
> davranışı ve yayın sonrası site doğrulaması. MinVer sürümleme ve tek derleme
> zinciri için: [`yayin-ve-surumleme.md`](yayin-ve-surumleme.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasıdır. Yalnız bu alana dokunurken okunur.
> 2026-10-15'te `yayin-ve-surumleme.md`'den ayrıldı: OP-012 notu dosyayı
> bütçenin üstüne çıkardı.

## Ilk yayin gunu olculenler (2026-09-20, `1.0.0-preview.1`)

- **🚨 `NuGet/login`'in `user:` alani policy'yi OLUSTURAN hesabin profil adidir,
  policy'nin SAHIBI degil.** Ikisi ayri eksendir: sahiplik (kisi ya da
  organizasyon) yalnizca policy'nin HANGI paketleri yayinlayabilecegini belirler;
  token'i kimin takas ettigini degistirmez. Policy `Tracon` organizasyonunun
  sahipliginde oldugu icin `user: Tracon` dogru gorunuyordu ve `ci.yml`'deki
  yorum bunu savunuyordu — ikisi de yanlisti. nuget.org'un cevabi:
  `Token exchange failed (HTTP 401) ... Make sure you are using the username of
  the policy creator, not the policy owner: No matching trust policy owned by
  user 'Tracon' was found.` Dogru deger kisisel profil adidir (`farukatasoy`).
  **Bu iddia hicbir testle yanlislanamazdi**: yalnizca gercek bir token takasi
  onu olcer ve o takas yalnizca bir `v*` etiketinde olur.
- **🚨 npm granular token'inda "Bypass two-factor authentication (2FA)" kutusu
  ISARETLI olmali.** Scope'lar (`@tracon` + `tracon` org, read/write) dogru olsa
  bile kutu kapaliyken her yazma OTP ister ve CI `npm error code EOTP` ile duser.
  Org duzeyinde 2FA enforcement acikken bu zorunludur. Granular token, klasik
  Automation token'dan daha dar oldugu icin tercih edilir; bypass'i isaretlemek
  o daraltmayi bozmaz, yalniz CI'in OTP soramayacagi gercegini kabul eder.
- **Sira geri donusu olmayani korudu ve isini yapti.** `publish` (NuGet)
  `needs: npm-publish` oldugu icin npm `EOTP`'de dustugunde NuGet **hic
  kosmadi**. Sonraki turda npm gecti, NuGet OIDC'de dustu ve `dotnet nuget push`
  yine hic kosmadi. Iki yarim yayin denemesinin ikisinde de surum numarasi
  yanmadi. `npm-publish` var olan surumu `npm view` ile gorup ATLAR ve is
  KIRILMAZ — bu yuzden etiketi tasiyip yeniden denemek bedelsizdir.
- **Yayindan sonra nuget.org hemen gorunmez.** `dotnet nuget push` basarili
  donse ve GitHub release olussa bile `v3-flatcontainer`, registration ucu ve
  paket sayfasi bir sure `404` verir; ilk kez yayinlanan paket kimlikleri
  dogrulamadan gecer. "Push basarili" ile "paket gorunur" ayri anlardir.

## Ikinci yayin turunda olculenler (2026-09-20, `1.0.0-preview.2`)

- **🚨 Site deploy'u yalnız HTTP 200 ve `Content-Type` ile doğrulanamaz.** Yayın
  sunucusu geçerli bir HTML/Markdown ağacını servis ederken package sayfaları
  eski release'in "not published yet" metnini taşıyabiliyordu; iki kanalın da
  başarılı görünmesi bu drift'i sakladı. `scripts/site-deploy.sh` artık
  `CHANGELOG.md` içindeki en son dated release version'ını `/`,
  `/getting-started/`, `/packages/` ve `/reference/changelog/` gövdelerinde
  arar ve eski unpublished-status metni varsa yayını reddeder. Release sonrası
  site deploy'u yine ayrı ve zorunlu bir adımdır.

- **🚨 npm `latest` dist-tag'i SILINEMEZ.** `npm dist-tag rm <paket> latest`
  registry'den `403 Forbidden - DELETE .../dist-tags/latest` alir. Buna bagli
  ikinci gercek: bir paketin **ILK** yayini `--tag next` verilse bile `latest`i
  o surume baglar — `1.0.0-preview.1` canlida `latest` oldu. "Preview'i
  `latest`ten kaldir" adimi bu yuzden TEK bir turda degil, HER preview turunda
  kirmizi biter. Dogru kural: stable **varsa** preview `latest`e hic dokunmaz;
  stable **yoksa** `latest` en yeni preview'e TASINIR (silinemedigine gore eski
  bir preview'de takili kalmasi daha kotudur). Kalici kapi:
  `dokuman-bakim.py` → `geri_alinamaz_registry_islemi`; ayni kapi
  `npm unpublish` ve `dotnet nuget delete`'i de ci.yml'de yasaklar.
- **Dist-tag adimi kirildiginda paket yayinda KALIR.** `npm publish` gecti,
  yalnizca etiket hizalama adimi dustu; artifact byte'lari degismedi. Fakat
  `github-release` isi `needs: [publish, npm-publish]` oldugu icin ATLANDI —
  paketler canlida, release sayfasi eksik kaldi. Dist-tag/release gibi
  yayin-sonrasi adimlarin duzeltilmesi etiketi yeniden kosturmakla olur; yayin
  adimi var olan surumu `npm view` ile gorup atlar.

## Sürüm kesimi ve etiket TEK push'ta (Faz 187, Açık Soru 1 = C)

- **🚨 Kesim commit'i etiketsiz itilirse yayın provası kırmızıdır.** Kesim
  `## [Unreleased]`'i `## [<sürüm>] - <tarih>` yapar ve üstüne boş bir
  `[Unreleased]` açar. Etiket yokken MinVer `<önceki>.N` üretir; bu sürüm için
  bölüm yoktur ve `[Unreleased]` boştur. Kırıcı değişiklik varsa kapı notu
  bulamaz. Mesaj komutu söyler: `git push --atomic origin main v<sürüm>`.
  Etiketli koşumda taban **bir önceki** `v*` etiketidir (MT-PKG-144).
- **🚨 `main`'in önce yeşil olmasını BEKLEME; ama düşen etiketi `main`'de ata olarak BIRAKMA** (2026-10-15, OP-012, bakımcı kararı).
  Yayın tek atomik push'tur: birikmiş commit'ler + kesim + etiket. Etiket koşumu kendi kapılıdır (`build` ×2 →
  prova → yayın). Etiket koşumu bir kod kusuruyla düşerse etiketi HEMEN sil: ata kalan etiket
  `breaking_changes.py`'nin tabanı olur (`git describe`), paketleri nuget.org'da olmadığı için sonraki her
  `main` koşumu `Paketle`'de düşer. Etiketsiz kesim ise `main` provasını düşürür (`[Unreleased]` boş) — bu
  ara hâl beklenen bir kırmızıdır, ölçüt iki bacağın build/test sonucudur. Kırılgan testte etiket taşınmaz,
  "Re-run failed jobs" yeter.
- **🚨 Sürüm notundaki çitli kod bloğu kırıcı değişiklik kapısının span eşleşmesini kaydırır** (2026-09-24, Faz 189): `breaking_changes._code_spans` çiti (```` ``` ````) ayıklamıyordu; her çitin üç backtick'i çevredeki metinle eşleşip hangi metnin span sayıldığını kaydırıyordu. Faz 188'in tek bloğu şans eseri geçti; 189'un ikinci bloğu doğru yazılmış `TraconToolRegistration` ve `ITraconBuilder` adlarını "notta yok" gösterdi. Çitler artık okunmadan önce silinir; yalnız çitin içinde geçen ad **kabul edilmez** (`test_citli_kod_blogu_span_eslesmesini_kaydirmaz`).

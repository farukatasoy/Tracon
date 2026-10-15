# Faz 198 — Adlı Konuşma Sağlayıcısı ve Kiracı Bağlaması

> **Durum:** ✅ Tamamlandı (2026-10-13)
> **Plan onayı:** Bakımcı, 2026-10-13 (Açık Sorular'ın önerileri kabul edildi: 1-A, 2-A, 3-A, 4-A, 5-A)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-298**
> **Önkoşul:** Yok (F-336 kapandı). Örnek, Faz 195 düğüm ayarını; o kapanmazsa run parametresini (Faz 194) kullanır · [Faz 203](203-COK-KONUSMACILI-DIYALOG-SENTEZI.md) (diyalog yeteneği, F-296) bu fazdan **sonra** gelir ve § 198.6'nın bıraktığı yere `ISpeechDialogueSynthesizer`'ı ekler (2026-10-13, yeniden tartıldı)
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Voice`, `.AspNetCore`, `.UI` · üretilen istemciler
> **Yeni paket:** Yok · **Migration:** Yok (Açık Soru 1-A; 1-B'de üç sağlayıcıda gerekir)
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable`
> **Tüketici yüzeyi:** site: `guides/voice.md`, `guides/model-providers.md` § BYOK, `getting-started/security.md`, `ui.md`, `capabilities.md` · sevk edilen: XML `<example>`, `src/Tracon.Voice/README.md`, `CHANGELOG.md`
> **Manuel test alanı:** `docs/manuel-test/19-COK-MODLULUK-VE-SES.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show af306afe:docs/arsiv/fazlar/198-ADLI-KONUSMA-SAGLAYICISI.md
> ```
>
> Damıtıldı 2026-10-13 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon süreçte tek konuşma sağlayıcısı tanır: adsız kayıt, süreç geneli anahtar. Kiracı egress politikası ve kiracı anahtarı model çağrısına (K-467) ve canlı sese (K-878) uygulanır, TTS/STT'ye uygulanmaz.

## Bitiş Ölçütleri (DoD)

- [x] İki adlı + adsız sentezleyici aynı süreçte; `/speak` `provider` ile doğru örneği çağırır (test 1) — `NamedSpeechProviderTests` (her örneğin çağrı sayacı); örnek uygulama `tone`
- [x] Yedi yolda ret sağlayıcıyı çağırmaz; konuşma el sıkışmadan önce `403` döner (test 5, 10) — üç tool `Tracon.Voice.UnitTests.SpeechTenantPolicyTests` (×3 teori), `/speak`, `/voices`, düğüm `SpeechTenantPolicyTests`, soket `VoiceConversationTenantPolicyTests`; kapı kapatılınca 20 fonksiyonel + 6 tool testi kırmızı (mutasyon)
- [x] Desteklemeyen sağlayıcıda bağlamalı kiracı reddedilir; destekleyende anahtar ulaşır, sızmaz (test 6-7) — `SpeechTenantCredentialTests` (6; 24 paralel istek), `SecretLeakTests` (iki proje)
- [x] Test 11, 12, 17 yeşil (ambient kiracı, model fiyatı, konsol listesi) — `SpeechResolverTenantTests` (sınırı: Plandan Sapmalar #16), `VoicePricingTests` (iki proje), Vitest `tenant-provider-panel.test.tsx` + E2E `A_speech_provider_allowed_in_the_console_unblocks_speech_for_the_tenant`
- [x] Dört doğrulama kapısı sıfır uyarı verir — uygulama dalında `python3 scripts/kapi.py kapanis --taban 1bc581ec` çıkış 0 (`b06865fd`): tarama 5,9 sn · `--denetle` 1,8 sn · betik testleri 7,6 sn · ajan haritası 0,1 sn · denetim paketi 0,3 sn · build 101,4 sn · test 1053,4 sn (38 koşum, 20 386 test, 20 367 geçti, 19 atlandı) · pack 47,7 sn · format 334,2 sn · site 87,2 sn. İlk koşum test adımında kırmızıydı (etiketsiz `Task.Delay`, § Süreç Ölçümü). Denetim düzeltmeleri hedefli testlerle doğrulandı. Birleştirmeden sonra tam kapı Faz 197'nin birleştirilmiş dalında (`41a5aae0`, Faz 198'in tamamını taşır) baştan sona tek koşumda çıkış 0 verdi (1219 sn; ayrıntı Faz 197 § *Kapanış kapısı*). `main`'de `yayin --kuru --surum 1.0.0-preview.4` ilk koşumda `Tracon.Client: TraconApiClient`'ı kırıcı buldu: `TraconVoiceListAsync` `provider`'ı iptal token'ından önce aldı, konumsal çağıran derlenmez; `CHANGELOG` `[Unreleased] → Changed` imzayı adıyla anar (`ea3b767d`). Üçüncü koşum çıkış 0 (`a63d21d1`): kırıcı liste 3 tip, 20 paket, altı paketlenmiş örnek, AOT ve net8.0 smoke
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (adlı sentezleyicili fonksiyon düğümü), çıktı belgeye yazıldı — aşağıdaki tablo
- [x] `secret` taraması boş döndü — `kapi.py tarama`: temiz (17 işaretli sentetik değer atlandı)
- [x] Manuel kabul case'leri `docs/manuel-test/19-COK-MODLULUK-VE-SES.md` içine eklendi; otomatikleştirilebilenler koşuldu — `MT-MM-126`…`131`; `126`, `127`, `129` ve `131` adım 1 örnek uygulamada; `128` 👤 görsel, `130` ➜ CI
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 🔴 yok; 🟡 #1 düzeltildi, 🟢 #1 F-445 (§ Denetim Bulguları)
- [x] `docs-site/` güncellendi; `npm run check` temiz (build + içerik + bağlantı + ağırlık)
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — **+350 B** brotli (aşağıda)
- [x] `CHANGELOG.md` `[Unreleased]` davranış değişikliğini ve yeni API'yi anar — Security, Added, Changed

### Örnek uygulama koşumu — gerçek çıktı (2026-10-13, port 5198)

`ASPNETCORE_ENVIRONMENT=Production`, üç bağlantı dizesi boş (bellek içi depo; paralel
oturumla paylaşılan veritabanına yazılmadı), `Tracon__Ui__AuthToken=` boş. ElevenLabs
anahtarı yüklenmedi: `UseVoice()` kapalı, adsız varsayılan sentezleyici yok, yalnız
adlı `tone` kayıtlı. Agent'lar Echo kipinde.

| Kontrol | Gerçek çıktı |
|---|---|
| `GET /api/voice/providers` | `[{"name":"tone","kind":"Synthesizer","providerName":"tone","supportsTenantCredentials":false}]` |
| `POST /api/voice/speak` `{"text":"Merhaba","provider":"tone"}` | `200`; ek `speech-20261013-065408-5567710.wav`, `audio/wav`, `5804` B; `characters 7`, `isEstimated false`, `cost null` (fiyat tanımsız) |
| Aynısı `provider` olmadan | `501` "Voice provider not configured" (varsayılan yok) |
| Aynısı `"provider":"cloud"` | `400` "No speech synthesizer is registered under the name 'cloud'. Registered synthesizers: tone." |
| `GET /api/voice/voices?provider=tone` | `200`; `low` / `mid` / `high` |
| `PUT /api/tenants/default/egress` `["echo"]`, sonra `/speak` `tone` | `403` "Tenant 'default' is not allowed to call speech provider 'tone'. Allowed providers: echo."; ek sayısı `1` → `1` (sağlayıcı çağrılmadı); `/voices?provider=tone` `403` |
| `narrated-tone` workflow (`support` agent + `synthesize` düğümü `{"provider":"tone","voice":"high"}`), politika `["echo"]` | run `Failed`; hata `Tracon.TenantProviderRefusedException`, `class: Unknown` (geçici değil), mesaj yukarıdakiyle aynı; yeni ek yok |
| Politika `["echo","tone"]`, aynı run | `Completed`; `WorkflowOutput` "Echo: Kargom nerede? … [spoken by tone: attachment 01a1340a-98c4-…, 17324 bytes]"; ek `speech.wav` `audio/wav` `17324` B run'a ve oturuma bağlı (3 kelime × 2880 örnek × 2 B + 44 B başlık) |
| `PUT /api/tenants/default/providers/tone` değersiz anahtar adıyla, `/speak` `tone` | `403` "… pointing at configuration key 'Tracon:ProviderKeys:Tone', but that key has no value … the call does NOT fall back to the global key." |
| Günlük | `fail: Tracon.WorkflowRunner … node synthesize failed. (ref: cb39036a)` — yalnız ret run'ı; başka hata yok |

Bundle payı (worktree tabanı `1bc581ec`, `npx vite build && node scripts/postbuild.mjs`):
`index-DLWGvGp3.js.br` 170 990 B → `index-DfMBSjQB.js.br` 171 340 B (**+350 B** brotli);
JS gzip toplamı 203,7 KB → 204,1 KB (bütçe 250 KB).

### Doğrulama komutları

```bash
curl -s http://localhost:5081/tracon/api/voice/providers
```

---

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.
> Uygulama oturumunda yazıldı (2026-10-13); yapısal iddialar `1bc581ec` üzerinde ölçüldü.

| # | Plan | Gerçek | Neden |
|---|---|---|---|
| 1 | `ListVoicesAsync(credential, ct)` | `ListVoicesWithCredentialAsync(ModelProviderCredential?, CancellationToken = default)` | Var olan `ListVoicesAsync(ct = default)` opsiyonel parametre taşıyor: aynı adlı ikinci aşırı yükleme opsiyonelli `RS0026`, opsiyonelsiz `RS0027` verir. K-878 emsali (`AttachSessionAsync`) gibi ayrı ad |
| 2 | Kanıt tablosu satırları (`e59ee892`) | Konum ve sıra iddialarının hepsi tuttu; satır kaymaları: `TraconWorkflowContextExtensions.cs:52` → `:53`; `WorkflowRunner` yolu `src/Tracon.Workflows/Internal/`, kapsam `:612`/`:630`; `ModelProviderRegistry.cs:330-334` → `:320-336` | Faz 201/204 arada geldi; ses dosyalarına dokunmadılar |
| 3 | Kapsam dışı: "Görsel yolunun egress'i (F-384)" | F-384 bu fazdan önce kapandı (K-912). Speech ret yolu onun `TenantProviderRefusedException`'ını (internal) yeniden kullanır; uçlar `403`'ü ondan çıkarır | Ret tipi uçta `403` ile sağlayıcı hatasının `502`'sini ayırır; ikinci bir ret tipi kopya olurdu. Görsel ret metni "model provider" der; değiştirilmedi |
| 4 | "`DescribeDenial` tür alır" | İç enum `TenantProviderCallKind { Model, Speech }` ve yeni aşırı yükleme; eski imza model metnini aynen üretir | `SecondaryModelEgressTests`, `EgressPolicyCacheTests` ve `ImageTenantPolicyTests` değişmeden yeşil |
| 5 | "Bilinmeyen ad → `TraconException`" | İç `SpeechProviderNotFoundException : TraconException`; uç adsız istekte `501`, adlıda `400` verir. Geçersiz biçimli ad (boş, 64+, karakter dışı) da `400`'dür ve mesaj girdiyi tekrar etmez | Public API büyümez; function node'da çağıran yine `TraconException` görür |
| 6 | "keyed singleton + iç ad kaydı" | Anahtar çıplak ad değil, iç `SpeechProviderRegistration` kaydıdır | Tüketicinin aynı dizeyle yaptığı keyed kayıt çakışmaz ve adlı örnek `GetKeyedService` ile kapı atlanarak çözülemez. `TraconBuilderRegistrationSnapshotTests.Describe` keyed tanımlayıcıyı okuyacak biçimde genişledi (keyed tanımlayıcıda `ImplementationInstance` atar) |
| 7 | "Her isteğe kiracı anahtarını koyan sarmalayıcı döner" | Sarmalayıcı yalnız kiracının anahtarı varsa döner; yoksa ham örnek döner. Çağıranın verdiği `Credential` sarmalayıcıda kiracınınkiyle değişir | Bağlamasız kiracıda davranış ve tip aynı kalır; çağıran başka kiracının anahtarını seçemez (`A_caller_supplied_credential_is_replaced_by_the_tenants`) |
| 8 | §198.5 fiyat sırası | `/speak` adsız istekte `ForSynthesis` `null` dönerse `ForCharacters`'a düşer; `VoicePricing` yapılandırılmış modele yalnız varsayılan sağlayıcının `ProviderName`'i için düşer. `VoicePricing` kurucusu `ISpeechTranscriber` alır (internal) | `ForSynthesis` varsayılan üyesi `null` döner: yalnız `ForCharacters` uygulayan tüketici okuyucusu varsayılan sentezleyicide fiyatını kaybetmesin. Adlı sentezleyici varsayılanın fiyatını hiç ödünç almaz (`VoicePricingTests`, iki proje) |
| 9 | Konuşma: "oturum kapısı ile slot arası" | Sürücü `ResolveProvidersAsync` (internal) ile **iki** sağlayıcıyı da (STT + TTS) el sıkışmadan önce çözer; `RunAsync` çözülmüş çifti alır | Ses tanıyıcıya, yanıt metni sentezleyiciye gider; ikisi ayrı sağlayıcı adı taşıyabilir |
| 10 | HTTP: `/voices` `403`/`400` | `/speak` ve `/voices` `.Produces`/`.ProducesProblem` üstverisini açıkça taşır (`400`/`403`/`501`/`502`); OpenAPI'de 132 yol / 170 işlem | Önceden iki uç belgede yalnız `200` taşıyordu; yeni yanıt kodları üretilen istemcilere ancak böyle girer |
| 11 | — | `provider: ""` `400`'dür, varsayılana düşmez | Boş ad belirsizdir; varsayılan `null`/alan yok ile seçilir |
| 12 | Site: `getting-started/security.md` | "The boundaries Tracon enforces" tablosuna "Tenant provider policy" satırı; `check-content.mjs` aynı satırı `reference/threat-model.md`'de de istedi ("fourteen" → "fifteen") | Kural model, konuşma, canlı ses ve görsel sağlayıcısının hepsini kapsıyor; önce hiçbir tabloda yoktu |
| 13 | Site sayfaları | İşlem sayısı sekiz yerde `169` → `170` (iki README, iki paket README'si, `http-api.md`, `capabilities.md`, `getting-started/index.md`, `reference/compatibility.md`, ana sayfa). `packages.md` değişmedi | `--site-denetle`'nin `paket-readme` kuralı `src/Tracon.AspNetCore/README.md` (sayı) ve `src/Tracon.Voice/README.md` değişimi için `packages.md`'yi ister; paket tanımı (`Tracon.Voice`: "speech synthesis, transcription, or live conversation") değişmedi. `--denetle` yeşil |
| 14 | Manuel case'ler: altı satır | `MT-MM-126`…`131`: plan case 1 ve 2 tek case'te (`126`); `131` eklendi (değersiz bağlama + anahtar almayan motor, hata modu 8) | `docs/manuel-test/*.md` ağaç bütçesi 2 556 000 B; ekleme sonrası 2 555 792 B (208 B boş) |
| 15 | `ui.md` ekran görüntüsü | Ekran görüntüleri yeniden üretilmedi; yalnız `.ui-source.sha256` damgası tazelendi | Kiracı paneli `settings.png`'nin görünür alanının altında (üretilen görüntü okundu: yalnız sürüm metni değişiyordu) |
| 16 | Hata modu 11 (`SpeechResolverTenantTests`) | Test kuyruklu iki kiracıyı doğru ayırır, ama "run kapsamı önce" sırasını AYIRT ETMEZ: kiracı yalnız `ITenantContext`'ten okununca da yeşil kaldı (mutasyon) | İş işçisi `AmbientTenantScope` açar; kuyruk yolunda iki kaynak aynı kiracıyı verir. Sıra `Tracon.Voice.UnitTests.SpeechTenantPolicyTests` ile kilitli: run kiracısı `acme`, `ITenantContext` `default`; aynı mutasyonda altı test kırmızı |
| 17 | Test sınıfı adları | Hata modu 14'ün birim kısmı `Core.UnitTests/Voice/SpeechProviderRegistrationTests`; 12 birim `Tracon.Voice.UnitTests/VoicePricingTests`, fonksiyonel `AspNetCore.FunctionalTests/VoicePricingTests`; üç tool'un ret testleri `Tracon.Voice.UnitTests/SpeechTenantPolicyTests` | Fonksiyonel test projesi `Tracon.Voice`'a bilerek referans vermez (soyutlamanın doğru pakette olduğunun kanıtı); tool'lar orada DI bileşimi (`AddTracon().UseVoice()`), kurulum anı sağlayıcısı ve `Services`'siz argümanla koşar |
| 18 | `<example>` | `ExampleCompilationTests` örnekteki tüketici tiplerini ister; `ExamplePrelude`'a `StudioSpeechSynthesizer`, `OnPremiseTranscriber`, `NarrationSettings` taslakları eklendi | Repo deseni (`OnPremiseModelProvider`, `TtsSettings`) |
| 19 | Örnek uygulama | Var olan `speak` düğümü (sahte) değişmedi; yeni `synthesize` düğümü (`SynthesizeSettings { Provider, Voice }`) sesi gerçekten üretir ve ek yazar | Eski düğüm `MT-WF-*` case'lerinin dayandığı çıktıyı üretir |
| 20 | Hata modu 15: "çözümlemede iptal sağlayıcıyı çağırmaz" | Çözümleyici iptali kontrollerden **sonra** da denetler (`c7799430`) | Denetimin ikincil notu testin sağlayıcı-çağrı iddiasını boş buldu; anlamlı hâliyle (belirteci yok sayan depolar) test kırmızı oldu: depo iptalden sonra normal dönünce çözümleyici sağlayıcıyı iptal edilmiş çağırana veriyordu |
| 21 | Sevk edilen metin: "politika her çağrıda okunur", "Tracon örneği doğrudan çağırmaz" | Soket bağlantı başına bir kez denetler; sağlık kontrolü varsayılan örneği doğrudan çağırır — XML, kılavuz, `Tracon.Voice` README ve `CHANGELOG` bunu yazar (`b453dc30`) | Denetim 🟡 #1; kod plana (§198.4 el sıkışma kapısı) uyuyordu, metin fazlasını vaat ediyordu |
| 22 | `MIMARI-GUVENLIK.md` bölümü | BYOK/egress paragrafı "her sağlayıcı çağrısı" olarak düzeltildi; canlı ses, görsel ve konuşma tek cümlede | Dosya bütçesi 23 200 B, boşluk 12 B idi; yeni metin 23 199 B'ye sıkıştırıldı (taşınacak birikimli kısım yoktu) |

## Bu Fazda Verilen Kararlar

Karar defterine giren üç karar (`docs/KARARLAR.md`):

- **K-930** *(güvenlik, public-api; kullanıcı kararı: 2-A, 3-A, 4-A)* — her konuşma çağrısının kiracı kapısı; egress `ProviderName`'e; anahtar yalnız `SupportsTenantCredentials`'a; soket bağlantı başına bir kez.
- **K-931** *(public-api; kullanıcı kararı: 1-A, 5-A)* — adlı kayıt (`AddSpeechSynthesizer`/`AddSpeechTranscriber`), çözümleyici, ad kuralı, çift ad hatası, `GET /api/voice/providers`; motoru çağıran seçer.
- **K-931**'in parçası: `ListVoicesWithCredentialAsync` adı (RS0026/RS0027).
- **K-932** *(public-api)* — fiyat kaynağı: `ModelId` ve `IVoicePricingReader` varsayılan üyeleri.

Kategori dışı yerel tercihler (K-* açılmadı):

- `provider: ""` `400`'dür; varsayılan yalnız alan yokken ya da `null` iken seçilir.
- Sarmalayıcı yalnız kiracının anahtarı varsa döner; çağıranın verdiği `Credential` kiracınınkiyle değişir.
- Ret ve "bulunamadı" iç istisnalardır (`TenantProviderRefusedException`, `SpeechProviderNotFoundException`); uç `403`/`400`/`501`'i onlardan çıkarır.
- Örnek uygulamanın `tone` sentezleyicisi `ProviderName` `"tone"` taşır ve egress'ten muaf değildir (3-A'nın örneği).
- Ekran görüntüleri yeniden üretilmedi, yalnız damga (Plandan Sapmalar #15).

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 (2026-10-13: Önkoşul satırı Faz 203 sırasıyla yeniden tartıldı); uygulama plan metnini değiştirmedi, sapmalar tabloda (22 satır) |
| Düzeltme turu sayısı | 3 — (1) ilk kapanış kapısı kırmızı: etiketsiz `Task.Delay` (`b06865fd`); (2) denetim: 🟡 #1 metin düzeltmesi + ikincil notun açığa çıkardığı iptal kusuru (`c7799430`, `c61925c2`, `b453dc30`); (3) `main`'de yayın provası: sürüm notu `TraconVoiceListAsync`'in kırıcı imzasını anmıyordu (`ea3b767d`) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 1 — `TestDelayClassificationTests` kapısını kıran etiketsiz `Task.Delay` (yeni test; kapanış kapısında yakalandı). Ayrıca yeni kodun kendi kusuru: iptalden sonra sağlayıcıyı vermek (birleştirmeden önce düzeltildi) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı; sonraki `kusur-giderme` koşumu doldurur) |

## Denetim Bulguları

**🔴 yok.**

| Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|
| 🟡 #1 | Sevk edilen metin konuşma soketi için koddan fazlasını vaat ediyordu: "politika her çağrıda okunur, şimdi kaydedilen politika sonraki çağrıyı durdurur" (soket bağlantı başına bir kez okur, `VoiceConversationDriver.cs`) ve "Tracon örneği hiç doğrudan çağırmaz" (sağlık kontrolü doğrudan çağırır, `SynthesizerVoiceHealthCheck.cs:48`). Yerler: `SpeechProviderContracts.cs`, `SpeechContracts.cs`, `guides/voice.md` | Gerçek (metin; kod plana uyuyordu) | **Düzeltildi** — `b453dc30`: XML (çözümleyici, sözleşme, iç çözümleyici), `guides/voice.md`, `Tracon.Voice` README, `CHANGELOG` "bağlantı başına bir kez; açık bağlantı kapanana kadar sağlayıcısını tutar" ve sağlık kontrolünü platform istisnası olarak yazar; `llms*.txt` yeniden üretildi. Tarama: `grep -rn -i "stops the next\|never calls an instance\|every call: a policy"` başka sevk edilen yerde eşleşme bırakmadı (`api/` üretilir) |
| 🟢 #1 | Konuşma soketi ses sağlayıcısını tur başına yeniden çözmez (model tarafı her turda okur, F-310) | — | **Devredildi** — **F-445** (Faz planlama; tur ortası ret davranışı protokol kararı ister) |
| Not | İki boş iddia: iptal testindeki `Studio.Calls.ShouldBe(0)` (test sentezlemiyordu) ve geçersiz ad testindeki `ShouldNotContain("\"\"")` | — | **Düzeltildi** — iptal testi belirteci yok sayan depolarla yeniden yazıldı ve **kırmızı** oldu: çözümleyici iptalden sonra sağlayıcıyı veriyordu; çözümleyici artık kontrollerden sonra da iptali denetler (`c7799430`). Geçersiz ad testi kural metnini ve girdinin yankılanmadığını ölçer (`c61925c2`) |

## Sonraki Faza Devir Notu

**Sıradaki faz:** [Faz 203 — Çok Konuşmacılı Diyalog Sentezi](203-COK-KONUSMACILI-DIYALOG-SENTEZI.md) (F-296). Devir oraya da yazıldı.

**Devralınan sözleşmeler:**
- Motorlar `AddSpeechSynthesizer(name, factory)` / `AddSpeechTranscriber(name, factory)` ile kaydolur. Çağıran `ISpeechProviderResolver.GetSynthesizerAsync(name, ct)` ile alır; `null` adsız varsayılandır.
- Kiracı kapısı çözümleyicidedir. Kiracı `TraconRunContext.Current?.TenantId ?? ITenantContext.TenantId` sırasıyla okunur; egress `ProviderName`'e uygulanır.
- Kiracının anahtarı yalnız `SupportsTenantCredentials` olan motora `SpeechRequest.Credential` ile gider.
- Fiyat `IVoicePricingReader.ForSynthesis(providerName, audio.ModelId ?? request.ModelId, characters)` ile hesaplanır. Motor `SpeechAudio.ModelId`'yi doldurursa doğru fiyatlanır.

**Bilinen tuzaklar:**
- 🚨 Çözümleyici kiracının anahtarı **yoksa ham örneği**, **varsa** `TenantCredentialSpeechSynthesizer` sarmalayıcısını döner. Sarmalayıcı bir arayüzü koşullu uygulayamaz: `is ISpeechDialogueSynthesizer` denetimi bağlamalı kiracıda düşer. İki yol var:
  - yetenek başına bir sarmalayıcı sınıfı (diyalog arayüzünü de uygulayan, kimlik bilgisini ileten);
  - çözümleyicide ayrı bir `GetDialogueSynthesizerAsync`.

  Seçim public API kararıdır. Çağıran tip denetimi yerine `ProviderName` okur.
- 🚨 Adlı motora varsayılanın `SynthesisModelId`/`OutputFormat`'ı verilmez. Diyalog isteği de modelini kendisi taşır.
- 🚨 Diyalog isteğinin tipi de `Credential` taşımalıdır. Varsayılan üye, kimlik bilgisi gelince `NotSupportedException` atmalıdır (`ListVoicesWithCredentialAsync` deseni).
- 🚨 Konuşma soketi sağlayıcıyı bağlantı başına bir kez çözer (F-445). Diyalog soket yoluna girerse aynı sınır geçerlidir.
- Ret testleri için sayaçlı sahte: `tests/Tracon.AspNetCore.FunctionalTests/Infrastructure/CountingSpeechProvider.cs`. Kuyruklu test "run kapsamı önce" sırasını ayırt etmez (`docs/hafiza/ses-ve-konusma.md`).

**Yarım kalan işler:**
- F-441 (manuel test bütçesi, 208 B boş) bir sonraki fazdan önce yapılmalı.
- F-442…F-446 kanal tablolarında.
- `MT-MM-128` 👤 görsel; `MT-MM-130` canlı ElevenLabs anahtarı ister.

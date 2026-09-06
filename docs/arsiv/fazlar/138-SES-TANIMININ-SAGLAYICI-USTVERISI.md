# Faz 138 — Ses Tanımının Sağlayıcı Üstverisi

> **Durum:** ✅ Tamamlandı (2026-09-03)
> **Kaynak:** Tüketici raporu AP-REQ-003 + yanıt dokümanı §5 (ProdigyEnabler, 2026-09-03) · **F-184**
> **Önkoşul:** Yok — [Faz 137](137-IS-TURUNUN-ACIK-ANAHTARI.md) ile bağımsızdır, paralel uygulanabilir
> **Paketler:** `AgentPrism.Abstractions`, `.Voice`, `.AspNetCore`, `.Client`, `.UI`
> **Yeni paket:** Yok · **Migration:** Yok — `VoiceDescriptor` kalıcılaştırılmaz
> **Public API:** **Büyüyor, kırmıyor** — `VoiceDescriptor`'a varsayılanlı bir alan eklenir.
> `PublicAPI.Shipped.txt` boş olduğu için bugün ucuz (`wc -l src/*/PublicAPI.Shipped.txt` ile doğrula)
> **Tüketici yüzeyi:** `docs-site/` — `guides/voice` · sevk edilen: `VoiceDescriptor`
> XML dokümanı, `src/AgentPrism.Voice/README.md` (`list_voices` satırı)
> **Manuel test alanı:** [`manuel-test/19-COK-MODLULUK-VE-SES.md`](../../manuel-test/19-COK-MODLULUK-VE-SES.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 3982ce8e:docs/arsiv/fazlar/138-SES-TANIMININ-SAGLAYICI-USTVERISI.md
> ```
>
> Damıtıldı 2026-09-03 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`VoiceDescriptor` yalnız üç alan taşır: `VoiceId`, `Name`, `Category`. ElevenLabs'ın `labels` alanı hiç parse edilmez. Bu yüzden ses havuzunu niteliklerine göre seçmek isteyen her tüketici ikinci bir sağlayıcı yolu açmak zorunda kalır — AgentPrism'in sağladığı adapter sınırını delerek. Bu ihtiyaç yalnız dış tüketiciye ait değildir.

## Bitiş Ölçütleri (DoD)

- [x] `GET /api/voice/voices` her ses için `attributes` taşır; **gerçek ElevenLabs anahtarı bu ortamda yoktu** — sağlayıcının yayınladığı OpenAPI şeması (`VoiceResponseModel`) doğrudan çekilip incelendi ve eşleme buna göre yazıldı; canlı doğrulama MT-MM-100'e ertelendi
- [x] `labels` yoksa **boş** koleksiyon döner; `null` dönmez (`VoiceAttributeMapperTests`, `A_voice_with_no_labels_yields_an_empty_but_non_null_Attributes_collection`)
- [x] `null`, bozuk ve scalar olmayan label değerleri güvenle atlanır (`VoiceAttributeMapperTests`: null/object/numeric/array değer testleri)
- [x] 32 attribute · 64 karakter key · 256 karakter value sınırları testle uygulanır (`VoiceAttributeMapperTests`)
- [x] Case-insensitive duplicate key tek kanonik değere iner (`Case_different_duplicate_keys_collapse_to_one_canonical_entry`)
- [x] `preview_url`, API key ve ham sağlayıcı gövdesi public modele **taşınmaz** (`SecretLeakTests.Voice_list_never_carries_preview_url_into_the_public_model` — tam alan taraması)
- [x] `list_voices` çıktısı gender bilgisini güvenli biçimde gösterir (`ListVoicesToolTests.Gender_attribute_is_shown_when_reported`)
- [x] Var olan custom `ISpeechSynthesizer` uygulaması **değişmeden derlenir** (`Attributes` varsayılanlı; mevcut tüm üretim noktaları grep'lendi, yalnız `ElevenLabsSpeechClient.MapVoices` dolduruyor)
- [x] 🔴 `ListVoicesTool` içindeki iki Türkçe dizge İngilizce'ye çevrildi
- [x] 🔴 `SourceLanguageTests` kelime listesi bu vakayı **yakalayacak** biçimde genişledi; taban çizgisi büyümedi — kelimeler eklenmeden ÖNCE stash ile ölçüldü (kırmızı, 2 satır), fix sonrası yeşil; aynı turda 5 `AgentPrism.AspNetCore.FunctionalTests` dosyasındaki benzer Türkçe test verisi de temizlendi (yeni yakalanan vaka)
- [x] Üç JSON kaynak üreticisi bağlamı güncellendi (`ElevenLabsJsonContext`, `AgentPrismClientJsonContext.g.cs` — AspNetCore reflection kullanır, üçüncü bir bağlam yok, bkz. Plandan Sapmalar); Native AOT smoke ses yolunda yeşil (`kapi.py yayin --kuru`)
- [x] OpenAPI ve üretilen istemci `attributes` taşır; drift kapısı temiz (`OpenApiSnapshotTests`, `OpenApiResponseSchemaTests` — 23/23 yeşil)
- [x] Dört doğrulama kapısı sıfır uyarı verir (`kapi.py kapanis`)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. "Gerçek Run Kanıtı" bölümü
- [x] `secret` taraması boş döndü (`kapi.py tarama` → ✅ temiz)
- [x] Manuel kabul case'leri `docs/manuel-test/19-COK-MODLULUK-VE-SES.md` içine eklendi (MT-MM-100..107)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (ilk turda 3 🔴 + 2 🟡 + 1 🟢 bulundu, tamamı bu fazda kapatıldı — bkz. Denetim Bulguları)
- [x] `docs-site/guides/voice.md` güncellendi; `npm run build` + bağlantı kontrolü temiz
- [x] `docs/kesif/2026-09-03-tuketici-gap-yaniti.md` AP-REQ-003 bölümü §9 şablonuyla dolduruldu

---

## Plandan Sapmalar

1. **Ölçüm, "Açık Soru 1"in planladığından farklı çıktı.** Plan "labels dili
   taşıyorsa doğrudan eşlenir, taşımıyorsa ayrı alan okunur" diyordu ve iki
   yolu da hazırlamıştı. Gerçek ölçüm (ElevenLabs'in yayınladığı OpenAPI
   şeması, `components.schemas.VoiceResponseModel`) ikinci yolu doğruladı:
   `labels` (`additionalProperties: string`) yalnız `gender`/`accent`/`age`/
   `use_case`/`description` taşıyor, `language` YOK. Dil ayrı bir alanda,
   `verified_languages` (dizi — bir voice birden çok model için doğrulanmış
   olabilir), her öge `VerifiedVoiceLanguageResponseModel.language` (zorunlu).
   Sorgu parametresi dokümantasyonu ("filtering, based on the voice's
   'language' label") şemanın kendisiyle çelişiyordu — karar prosadan değil
   şemadan alındı.
2. **Ölçüldü, planda YOKTU: `/v2/voices` gerçek uç, `/v1/voices` değil.**
   Plan'ın mermaid diyagramı `/v1/voices` yazıyordu; mevcut kod (`grep`)
   `/v2/voices` çağırıyordu ve testler bunu doğruluyordu
   (`Voice_list_is_read_from_the_v2_endpoint_and_sorted_by_name`). Sağlayıcının
   OpenAPI'si `GET /v2/voices`'in `voices` alanının da AYNI
   `VoiceResponseModel` şemasını kullandığını doğruladı (`GetVoicesV2ResponseModel`),
   yani `labels`/`verified_languages` yapısı değişmedi — yalnız planın konum
   iddiası bayattı.
3. **İlgisiz ama aynı dosyada karşılaşılan kusur kapatıldı (F-184 kapsamı
   dışında, kullanıcı talimatı "alakasız bug'ları da çöz" gereğince).**
   `ElevenLabsSpeechClient.ListVoicesAsync` `/v2/voices`'i hiç sorgu dizesi
   eklemeden çağırıyordu; bu uç `page_size` verilmezse VARSAYILAN **10** ses
   döndürür. Kod `MaxReportedVoices = 500` sınırını varsayıyordu ama hiçbir
   hesap gerçekte 10'dan fazla ses görmüyordu. `page_size=100` +
   `has_more`/`next_page_token` takibiyle düzeltildi (`ReadVoicesPageAsync` +
   `MapVoices` ayrımı, 4 yeni test). `docs/hafiza/ses-ve-konusma.md`'ye
   tuzak olarak eklendi.
4. **Bağımsız denetim 3 🔴 buldu, hepsi bu fazda kapatıldı** (ayrıntı aşağıda,
   "Denetim Bulguları"). İkisi de dördüncü doğrulama kapısının (`dotnet test`)
   kendi baseline testleriydi — ilk `faz-uygulama` turunda çalıştırılmamıştı,
   denetimden önce koşulmuş olsaydı erken yakalanırdı. Ders sonraki faza:
   `dotnet test tests/AgentPrism.Core.UnitTests` fazın SON adımı değil,
   `faz-denetim` çağrılmadan ÖNCEKİ adım olmalı.
5. **Frontend'in elle dil eşlemesi (Q4) kaldırılmadı — planın önerisi (A)
   izlendi.** Ölçüm dilin geldiğini gösterdi (ElevenLabs, `verified_languages`
   üzerinden) ama yalnız bu sağlayıcı için ve yalnız sağlayıcı gerçekten
   doğrularsa; tüm sağlayıcılar için garanti değil. Seçici artık üstveriyle
   zenginleşiyor (`voiceOptionMeta`) ama seçimin yerini almıyor.
6. **Gzip etkisi ölçüldü, plan "birkaç yüz bayt" diyordu: gerçek ölçüm +92
   bayt** (`index-*.js`, `gzip -c | wc -c`: 181.626 → 181.718). Bütçenin
   (250 KB) çok altında.

## Bu Fazda Verilen Kararlar

- **K-669** — `VoiceDescriptor.Attributes` sağlayıcı üstverisini typed alanlar
  değil, sınırlı bir `Dictionary<string,string>` olarak taşır. Tam gerekçe:
  `docs/KARARLAR.md`.

## Site Senkronu Gerekçesi (`--site-gerekce-yazildi`)

`dokuman-bakim.py --site-denetle` dört kural tetikledi; dördü de dosya adı
desenine dayalı geniş eşleşmeydi, incelendi ve hedef sayfada gerçek bir
boşluk bırakmadı:

- **`arayuz` → `ui.md`**: `settings.tsx` tetikledi (ses seçicisinin
  `voiceOptionMeta` ile zenginleşmesi). Kozmetik bir etiket eki; `ui.md`
  ekran envanterini listeler, tek bir dropdown'ın metin biçimini değil.
- **`cekirdek-kavram` → `concepts/`**: `PublicAPI.Unshipped.txt` tetikledi
  (yeni `VoiceAttributeNames` tipi). `concepts/*.md` çekirdek soyutlamaları
  (agent, run, tool, workflow) anlatır; ses sağlayıcı üstverisi bunların
  hiçbirinin kavramsal modelini değiştirmedi.
- **`model-saglayici` → `getting-started/first-agent.md`**: `ElevenLabsJson.cs`
  tetikledi — desen dosya yolundaki "provider" kelimesine geniş eşleşiyor;
  `first-agent.md` bugün ses sağlayıcısından hiç bahsetmiyor (`grep` boş) ve
  bu fazın konusu bir LLM model sağlayıcısı değil.
- **`paket-readme` → `packages.md`**: `AgentPrism.Voice/README.md` tetikledi
  (`list_voices` satırının güncellenmesi). `packages.md`'nin `AgentPrism.Voice`
  satırı zaten yüksek seviyeli ("speech synthesis, transcription, or live
  conversation"); tool'un dönüş biçimindeki bir ayrıntı o seviyeye ait değil.

Bu özelliğin gerçek, ayrıntılı tüketici sayfası zaten güncellendi:
[`guides/voice.md`](../../../docs-site/src/content/docs/guides/voice.md)'nin yeni
"Voice attributes" bölümü.

## Gerçek Run Kanıtı

Gerçek bir ElevenLabs anahtarı bu ortamda yoktu; ses-attribute yolunun
GERÇEK sağlayıcıya karşı doğrulanması MT-MM-100/101/102'ye ertelendi (manuel
kabul koşumu). Onun yerine iki kanıt üretildi:

1. **Sağlayıcı şeması doğrudan ölçüldü** — `api.elevenlabs.io/openapi.json`
   indirildi, `VoiceResponseModel`/`VerifiedVoiceLanguageResponseModel`
   şemaları incelendi (bkz. Plandan Sapmalar #1-2). Eşleme tahminle değil bu
   ölçümle yazıldı.
2. **`samples/AgentPrism.Api` gerçek bir `run` ile çalıştırıldı** (in-memory
   depolama, ses sağlayıcısı yapılandırılmadan — voice-opsiyonel yol):
   - `GET /agentprism/api/voice/voices` → `501` (ses hiç yapılandırılmamışken
     beklenen davranış; regresyon yok).
   - `POST /agentprism/api/agents/cached-support/run` gerçek bir SSE koşusu
     üretti: `{"runId":"01a066b2-1994-7804-bbe3-82738f7db0a4", ...}` →
     `"Echo: What is the status of order 42? "` → `done`.
   - Bu, fazın DEĞİŞTİRDİĞİ derleme/DI/HTTP zincirinin (yeni `Attributes`
     alanı, yeniden yapılandırılan `ListVoicesAsync`) gerçek bir host'ta
     regresyonsuz çalıştığını kanıtlar; ses-spesifik davranış hariç.

## Denetim Bulguları

`faz-denetim` (taze bağlamlı ayrı agent) ilk turda **3 🔴, 2 🟡, 1 🟢** buldu.
Tamamı bu fazda kapatıldı:

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `VoiceDescriptor.Attributes`'ın `<summary>`/`<remarks>`'ı `<see cref="VoiceAttributeNames.Gender"/>` gibi MEMBER cref kullanıyordu; sözleşme tipinde bu yasak (`tuketici-dokuman-senkronu` kalite sözleşmesi §B) — NSwag'in OpenAPI/npm/C# istemci açıklamasına tam CLR imzasını ("string VoiceAttributeNames.Gender") bastığı ölçüldü, üçüncü tekrar | **Düzeltildi** — tüm cref'ler `<c>` düz metne çevrildi (`SpeechModels.cs`); OpenAPI/npm/C# istemci yeniden üretildi, açıklama artık düz metin |
| 2 | 🔴 | `ElevenLabsSpeechClient.ListVoicesAsync`'in yeni `<remarks>`'ı 🚨 ile başlıyordu — `///` içinde geliştirme günlüğü sesi yasak (`ShippedDocumentationSelfContainmentTests`); test fiilen kırmızıydı | **Düzeltildi** — 🚨 kaldırıldı, metin düz olguya çevrildi; test yeşil |
| 3 | 🔴 | Yeni public tip `VoiceAttributeNames` `AgentPrism.Abstractions`'ın tip sayısını 363→364 çıkardı ama `public-surface-baseline.txt` güncellenmemişti; `PublicSurfaceBaselineTests` fiilen kırmızıydı | **Düzeltildi** — taban çizgisi `AGENTPRISM_PUBLIC_SURFACE_REFRESH=1` ile 364'e yenilendi (planlı, kasıtlı bir büyüme) |
| 4 | 🟡 | DoD satırı "gerçek run yapıldı, çıktı belgeye yazıldı" — çıktı hiçbir dokümana yazılmamıştı | **Düzeltildi** — bkz. "Gerçek Run Kanıtı" bölümü |
| 5 | 🟡 | `src/AgentPrism.Voice/README.md`'nin `list_voices` satırı bayattı ("List of name + ID") | **Düzeltildi** — `category`/`attributes` yansıtacak biçimde güncellendi |
| 6 | 🟡 | Yeni çok-sayfalı `ListVoicesAsync` döngüsünde ikinci (veya sonraki) sayfanın hata dönmesi hiç test edilmemişti | **Düzeltildi** — `A_failure_on_a_later_page_fails_the_whole_call_rather_than_returning_a_partial_list` testi eklendi |
| 7 | 🟢 | `ListVoicesTool.Description` metni ("Returns a name and id") artık eksikti — araç `category`/`attributes` de döndürüyor | **Düzeltildi** (kozmetik, ADAYLAR'a devredilmeden bu turda giderildi) |

**Temiz çıkan başlıklar** (denetçinin raporu): 3.2 (test tiyatrosu yok), 3.3
(test seviyeleri uygun), 3.5 (imza-gövde kayması yok), 3.6 (plan dışı public
API yok), 3.7 (repo kuralları temiz).

## Sonraki Faza Devir Notu

Üç fazın (136 · 137 · 138) tamamı kapandı. Tüketici yanıt dokümanı
[`docs/kesif/2026-09-03-tuketici-gap-yaniti.md`](../kesif/2026-09-03-tuketici-gap-yaniti.md)
AP-REQ-002 ve AP-REQ-003 bölümleriyle tamamlandı. AP-REQ-001 bölümü Faz 137
kapanışında BOŞ kaldı (o fazın kendi kapanış notu: hedef dosya o an
oluşturulmamıştı) — bu faz onu doldurmadı, kapsamı yalnız AP-REQ-003'tü;
gerekirse ayrı bir kısa iş kalemi olarak `docs/ADAYLAR.md`'ye yazılabilir.

Sonraki oturum için iki not:
- `VoiceDescriptor.Attributes`'ın gerçek ElevenLabs verisiyle ilk canlı
  doğrulaması henüz yapılmadı (MT-MM-100/101/102, gerçek anahtar gerekir) —
  bir sonraki `manuel-test-kosumu` turunda bunlar öncelikli koşulmalı.
- Konsolun elle dil eşlemesi (`settings.tsx`/`voice.ts`) hâlâ duruyor; bu faz
  onu bilerek kaldırmadı (Açık Soru 4, öneri A). Kaldırma kararı, birden çok
  sağlayıcı üzerinde `language` üstverisinin güvenilirliği ayrıca ölçülürse
  ayrı bir iş kalemi olarak değerlendirilebilir.

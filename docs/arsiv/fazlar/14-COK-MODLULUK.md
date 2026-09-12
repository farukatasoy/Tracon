# Faz 14 — Çok Modluluk: Görsel, Ses ve Dosya Girdisi

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-12**
> **Önkoşul:** Yok · Faz 9 önerilir (yükleme yetkisi rol ister)
> **Sonraki bağımlı:** [Faz 28](28-SES-TOOLLARI.md) — ses çıktısı bu fazın deposunu kullanır
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** 0006 (planlanan sırada)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/14-COK-MODLULUK.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Kullanıcı bir görsel, ses dosyası veya belge yükleyip agent'a gönderebilsin. OpenAI ve OpenRouter arayüzlerinin standart yeteneğidir; Tracon bugün yalnız metin taşır. ---

## Bu Fazda Verilen Kararlar

1. **İkili içerik `attachments` tablosunda, mesajda yalnız referans** — geçmiş
   okumasının maliyeti sabit kalır (K-111).
2. **`bytea`, base64 metin değil.**
3. **`IAttachmentStorage` genişleme noktası; bulut SDK bağımlılığı yok** (K-007).
4. **Tür beyaz listesi + sihirli bayt denetimi** — `Content-Type` kanıt değildir (K-113).
5. **Modele gönderimde içerik belleğe çözülür**, sağlayıcıya URL verilmez (K-111).
6. **`attachments.session_id` yabancı anahtar değildir** — denendi, gerçek
   akışta başarısız oldu, geri alındı (K-112).

---

## Açık Sorular — Cevaplandı

1. **Varsayılan boyut sınırı** → **20 MB** (kullanıcı kararı, doküman önerisi
   onaylandı). Ses için ayrı sınır Faz 29'da.
2. **Ekler otomatik silinsin mi?** → **Evet**, ancak `session_id` yabancı
   anahtar OLARAK DEĞİL, uygulama katmanında (K-112). Sahipsiz ekler Faz
   25'in işi olarak kalır.
3. **PDF metne çevrilsin mi?** → **Hayır** (kullanıcı kararı, doküman önerisi
   onaylandı). PDF olduğu gibi gönderilir.
4. **`agent_files` tablosu bu fazda mı?** → **Evet** (K-117), aynı migration
   (0006) içinde. `PostgresAgentFileStore` yazıldı; K-110'un öngördüğü gibi
   `FileMemoryProvider`/`TextSearchProvider` kod değişmeden buraya döndü.

---

## Plandan Sapmalar

- **`IAttachmentStore`'a `DeleteBySessionAsync` eklendi** — plan taslağında
  yoktu. Oturum silme kaskadının (açık soru 2) uygulama katmanında
  yapılabilmesi için gerekli oldu (K-112).
- **`/v1/chat/completions` çok modlu girdi almıyor** — DoD bunu istemiyordu,
  yalnızca `/v1/responses` isteniyordu; bilinçli kapsam kararı (K-116).
- **`.DisableAntiforgery()` eklendi** — plan bundan bahsetmiyordu çünkü minimal
  API'nin `IFormFile` parametresi için otomatik CSRF metadata eklediği
  keşfedilmemişti (K-115).

---

## Bitiş Ölçütleri (DoD)

- [x] Arayüzden görsel yüklenip agent'a gönderiliyor; model yanıt veriyor —
      Playground'a sürükle-bırak/dosya seçici + önizleme eklendi;
      `Tracon.Ui.E2ETests.UiTests.Playground_dosya_yuklenir_onizleme_gorunur_ve_calistirma_devam_eder`
      gerçek bir Chromium'da dosya yükler ve modelin yanıt verdiğini doğrular.
- [x] `conversation_items` içindeki mesaj **küçük** kalıyor — mesaj yalnız
      `UriContent({prefix}/api/attachments/{id})` taşır; ölçüldü:
      `AttachmentRunTests`/`OpenAICompatTests`'te modele giden içerik
      `DataContent`, geçmişe yazılan içerik `UriContent`'tir (K-111).
- [x] Geçmiş yeniden yüklendiğinde ek hâlâ çözülüyor — `AttachmentResolvingChatClient`
      her model çağrısında (yeni tur + geçmiş tur farketmeksizin) referansı çözer.
- [x] Yanlış tür ve büyük dosya reddediliyor — `AttachmentEndpointTests.Bilinmeyen_tur_reddedilir`,
      `Boyut_sinirini_asan_dosya_reddedilir`.
- [x] Başka kiracının ekine erişilemiyor — `AttachmentEndpointTests.Baska_kiracinin_ekine_erisilemez`,
      `AttachmentRunTests.Baska_kiracinin_eki_calistirmada_kullanilamaz`.
- [x] `/v1/responses` OpenAI biçimli görsel girdisi kabul ediyor —
      `OpenAICompatTests.Responses_govdeye_gomulu_data_uri_ege_cevrilir_ve_modele_cozulmus_ulasir`
      (bkz. K-116: `/v1/chat/completions` bilinçli olarak kapsam dışı).
- [x] Dört doğrulama kapısı sıfır uyarı; bundle ölçüldü — `dotnet build/test/pack/format`
      hepsi 0 uyarı/hata; JS bundle 99,1 KB gzip (bütçe 250 KB); bu fazın eklediği
      dosya yükleme UI'ı budget'ı aşmadı.

---

## Sonraki Faza Devir Notu

- Faz 28 (ses tool'ları) üretilen sesi `attachments` tablosuna yazacaktır;
  `audio/*` beyaz listede zaten var (bu fazda temel imza sezgisiyle: WAV/OGG/MP3).
- Faz 25 (saklama) sahipsiz ekleri temizlemekle yükümlüdür.
- `ModelDescriptor`'a yetenek alanı (`SupportsVision` vb.) eklenirse Faz 8'in
  katalog yapısı genişler; K-032 gereği değer **yapılandırmadan** gelir. Bu
  alan eklenene kadar desteklemeyen bir modele ek göndermek sağlayıcı
  hatasıyla sonuçlanır, Tracon önceden engellemez.
- `/v1/chat/completions` görsel/dosya girdisi almaz (K-116). İstenirse
  `AttachmentIngestion.ReplaceEmbeddedDataAsync` zaten paylaşıma hazır;
  yalnız `OpenAIChatCompletionsEndpoints.ReadContent`'in `image_url`/
  `input_file` parçalarını da okuyacak şekilde genişletilmesi gerekir.
- `IAttachmentStore` arayüzüne `DeleteBySessionAsync` eklendi — bir depo
  yazan yeni faz bu üyeyi de uygulamalıdır (contract testinde zorunlu).
- Doğrulanmış tip: `Microsoft.Agents.AI.AgentFileStore`'un tüm üyeleri
  (`ReadAsync` vb.) **nullable** dönüş/parametre taşır ve `CancellationToken`
  dahil her parametre `= default` varsayılanına sahiptir (`MA0061` bunu
  build sırasında zorladı). Yeni bir override yazarken önce
  `maf-api-kesfi` ile doğrulayın, tahmin etmeyin.

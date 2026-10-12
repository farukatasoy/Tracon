# Keşif Turu — 2026-10-11 · Explainer-Video tüketicisi: uyum, kusurlar ve adaylar

> Bu bir **koşum kaydıdır**, spec değildir. Sıcak yolda değildir ve baştan sona
> okunmaz. Onaylanan kalemlerin tam metni
> [`ADAYLAR.md`](../ADAYLAR.md) içinde yaşar; bu dosya
> yalnız oraya işaret eder. Tüketiciye dönük yol haritası ve alternatifler
> [tüketici raporundadır](../arsiv/incelemeler/2026-10-11-explainer-video-tuketici-uyum-raporu.md).

**Tetikleyen:** Podcast tüketicisine benzeyen ikinci bir medya tüketicisi
(Explainer-Video) hattını Tracon üzerine kurmak istiyor. Kullanıcı önce
eksikleri, kusurları ve aday fikirleri tartışmak, sonra alternatifli bir rapor
istedi.
**Zemin:** Faz 192 kapalı · Faz 193 planlandı (2026-11-10'dan önce başlamaz,
K-855) · sıralanabilir kuyrukta 10 aday (F-294…F-303) · en büyük numara F-317.
**Ekosistem taraması:** 2026-10-11 · web erişimi var.
**Yöntem:** Beş Tracon alanı (workflow · agent/harness · job ve sandbox ·
voice/artifact/cost · konsol/tenancy) kodla haritalandı. Her alanın bulgusunu
ayrı bir doğrulayıcı kodla çürütmeye çalıştı. Tüketici reposu ayrıca okundu.
Yedi yüksek etkili iddia elle de okundu. Hiçbir kusur çalıştırılarak yeniden
üretilmedi; her biri `kusur-giderme` ile önce kırmızı testle kanıtlanır.

---

## 0. Tüketici ihtiyacı ve kullanıcı cevapları

Repo: `~/Desktop/Explainer-Video` (tek commit `edb560a`). Remotion 4.0.520 +
React 19 + TypeScript, Node script'leri ve tek bir Python script'i
(`tts_trendyol.py`, VoxCPM2 + torch + MPS).

Hat: 8 bölümlü Türkçe kurumsal makale → LLM (Claude Code skill'i
`content-to-video`, 365 satır) → `src/content/<slug>.ts` (18 sahne tipi;
pilotta 60 sahne, 44 KB) → **insan onayı** (skill "içerik onaylanmadan TTS ve
render başlatma" der) → sahne başına TTS (`say` · Trendyol-TTS · ElevenLabs;
metin hash önbelleği) → ffmpeg montaj ve EBU R128 → whisper.cpp ile yalnız
zamanlama → SRT/VTT → Remotion ile iki render (yakılmış + temiz; yaklaşık
27.200 kare) → mux, chapters → elle QA. Video başına yaklaşık 1 saat.

🚨 LLM çıktısı bugün **kod olarak çalışıyor**: script'ler `.ts` dosyasını
esbuild ile paketleyip `import()` ediyor (`scripts/_content.mjs:7-16`, yedi
giriş noktası).

| # | Soru | Cevap (2026-10-11) |
|---|---|---|
| 1 | Tek kurum iç aracı mı, çok kuruma SaaS mı? | **Çok kurumlu SaaS** (podcast gibi) |
| 2 | Onay nerede, neyi kapsar? | Kiracıların host'unun ekranında onay/ret; revizyon sayısı **n**; içerik hash'i değişirse onay düşer |
| 3 | Kurumsal metin EU bulut TTS'e gidebilir mi? | **Gidebilir** |
| 4 | Trendyol-TTS lisansı ve ses rızası | **Teyitli** |
| 5 | Ürün yönü: A (workflow uçtan uca) mı C (LLM + job) mı? | Varsayılan: **şimdi C**, A hedef |
| 6 | Yakılmış MP4 gerekli mi? | Varsayılan: **temiz MP4 + VTT** (tek render) |
| 7 | Hacim | Varsayılan: 84 video, gece batch, 1 TTS + 2–4 render worker |
| 8 | Kapsam parametresi | Varsayılan: evet, tüm makale |
| 9 | Dil | Varsayılan: yalnız Türkçe; dil şemada parametre |
| 10 | `bg`/`dur` kodla, `art` LLM'de | Varsayılan |
| 11 | Engelleyici kurallar | Varsayılan: render'ı çökertenler ve yapısal kurallar engelleyici; çeşitlilik uyarı |
| 12 | Model | **Başka modeller de olabilir** (kiracı/profil başına) |
| 13 | Kaynak biçimi | Varsayılan: 8 bölümlü Markdown |
| 14 | Remotion lisansı | Varsayılan: Automators + 4.0.x sabit |
| 15 | Artefakt deposu | Varsayılan: nesne deposu; taslak 30 gün, final kalıcı, onay kaydı süresiz |
| 16 | İptal ve yeniden deneme | Varsayılan: iptal ≤ 30 sn; render 1, TTS 2 deneme |
| 17 | Sahne bileşeni ve script'i kim yazar? | **Operatör de yazabilir ve istediği zaman canlı günceller** |
| 18 | Tracon konsolu kime açılır? | **Kiracıların host'una açılır** |
| 19 | Kusur giderme sırası | Onaylandı (§ 5 Kanal 2'deki sıra) |
| 20 | Adayların ADAYLAR'a yazılması | Evet: YA-1, YA-2, YA-8, YA-4, YA-6, YA-7, YA-10; YA-3 ve YA-13 ertelendi; mevcut kalemlerin değişiklikleri işlenir |
| 21 | Kayıt düzeni | Evet: keşif kaydı + `arsiv/incelemeler/` raporu |
| 22 | K-130 ve tek yanıt | K-130 korunur; opt-in tek yanıt F-319'un parçası |

**Cevapların değiştirdiği üç sonuç:**

- **Cevap 17 sandbox cevabını tersine çevirir.** Operatörün yazdığı sahne
  bileşeni (TSX) ve script güvenilmez koddur. Render worker'ı ve script
  runner'ı gerçek bir sandbox'ta koşar (gVisor/nsjail veya iş başına mikro-VM).
  K2 korunur: bu kodu Tracon değil, tüketicinin sandbox'ı çalıştırır (podcast
  turu § 2.3 ile aynı karar).
- **Cevap 17 içerik şemasını canlı yapar.** Yeni sahne tipi yeni JSON Schema,
  yeni talimat ve yeni agent sürümü demektir. Üretim boyunca agent sürümü,
  şema sürümü ve template bundle'ı birlikte sabit kalmalıdır. Bu, F-331'in
  aciliyetini ve F-300'ün kapsamını büyütür.
- **Cevap 1 + 12 + 3 seçimi kiracıya taşır.** Model ve TTS motoru kiracı
  profiline bağlıdır. Yapılandırılmış çıktı birden çok sağlayıcıda sınanır
  (F-320, F-340); ses motoru adla seçilir (F-298 🟡 → 🟠).

---

## 1. Ölçülen zemin (Aşama 0)

| Kaynak | Bulgu |
|---|---|
| Podcast turu (aynı gün) | F-293…F-303 yazıldı; ham #9, #13–#17 ertelendi; #18 K2 ile reddedildi. Bu tur aynı alanların çoğuna ikinci vaka getirir |
| Faz 192 devir notu | F-293 kapandı (loop tükenince run düşer); F-314…F-317 kusur olarak açıldı |
| 2026-09-07 tüketici turu | A17 (genel workflow / dış orchestrator) mimari ret değil; "somut vaka" bekler |
| Reddedilenler | L20 (arayüzden tool kodu), L43 (sürüm label'ı) — ikisi de bu turda açılmaz |
| MAF | Depo 1.20.0'da; `Microsoft.Agents.AI.Workflows` 1.24.0 yayımlı (2026-10-07) |

---

## 2. Tasarım özeti

Tam anlatı, alternatifler ve kabul senaryoları raporda. Burada yalnız dört
sorunun cevabı durur.

- **Harness mı workflow mu?** Hattın tamamı bir **workflow**'dur: bir LLM
  adımı, bir onay kapısı ve n revizyon, sonra uzun deterministik medya
  adımları. LLM adımı harness gerektirmez: `ResponseFormat = JsonSchema` +
  tüketicinin `IStructuredResponseValidator`'ı + `MaxRepairAttempts`,
  oturumsuz ve akışsız run. `Loop` ancak anlamsal bir yargıç gerekirse
  eklenir; bugün yapılandırılmış doğrulamayla birlikte çalışmaz (F-329).
- **Sandbox gerekli mi?** LLM çıktısı için **hayır**, içerik JSON veri olursa
  (Remotion `inputProps` + `calculateMetadata`). Operatör kodu için **evet**
  (cevap 17): render worker'ı ve script runner'ı tüketicinin sandbox'ıdır.
  Remotion Chromium'u `--no-sandbox` ile açar; container tek sınırdır.
- **Python nerede çalışır?** .NET sürecinde ve Tracon skill script runner'ında
  değil (30 sn varsayılan, çağrı başına soğuk süreç, 256 KB çıktı, OS yalıtımı
  yok, K-862). Güvenilir GPU TTS modeli ayrı bir servis olur (CUDA ≥ 12,
  yaklaşık 8 GB VRAM). Operatör script'i sandbox'lı runner'da koşar. Bulut TTS
  seçen kiracıda Python hiç gerekmez.
- **Orkestrasyon:** Şimdi C (Tracon = LLM adımı + job altyapısı; onay ve
  revizyon host'ta), hedef A (Tracon Workflows uçtan uca). A'yı engelleyen
  kusurlar F-325, F-327, F-328; eksik yetenekler F-318, F-319.

---

## 3. Ham fikir listesi (Aşama 2)

| # | Fikir | Kim için | Neden şimdi | Sonuç |
|---|---|---|---|---|
| YA-1 | Workflow `run`/`resume`/`respond` için `Prefer: respond-async` + kuyruk | Nöbetçi mühendis, SaaS host | Saatlik run bağlantıya bağlı | ✅ F-318 |
| YA-2 | HITL karar sözleşmesi: kiracı geneli kutu, opt-in tek yanıt, karar denetimi, süre | Kurumsal platform ekibi | Onay host ekranında, n revizyon | ✅ F-319 |
| YA-2b | Workflow isteği presenter + şemalı, ön dolu yanıt formu | Konsol operatörü | Kart yalnız düz metin gösterir | ⏸ onay host ekranında (cevap 2); F-295 editörüyle birlikte düşünülür |
| YA-3 | Dış işe devret ve bekle düğümü (A17'nin dar dilimi) | MAF kullanan ekip | Uzun adım yuva tutar | ⏸ kullanıcı erteledi (cevap 20) |
| YA-4 | Çöken workflow'u son checkpoint'ten otomatik sürdürme | Nöbetçi mühendis | Elle resume; job baştan koşar | ✅ F-321 |
| YA-5 | Düğüm span/metrik/çıktı özeti + job ilerleme ve sonuç alanı | Nöbetçi mühendis | Saatlik işte yalnız "Running" görünür | ⏸ onaya sunulmadı |
| YA-6 | Kiracı başına workflow eşzamanlılığı, `Queued` satırı, adil kiralama | Kurumsal platform ekibi | SaaS (cevap 1); semafor süreç başına ortak | ✅ F-322 |
| YA-7 | Job süre sınırı + iptalin handler token'ına taşınması | Nöbetçi mühendis | Takılan render tek media slot'unu tutar | ✅ F-323 |
| YA-8 | Kesilen çıktı ayrı sonuç (`OutputTruncated`) + derleme uyarısı | İlk agent'ını kuran geliştirici | Anthropic varsayılanı 4096; 44 KB JSON kesilir | ✅ F-320 |
| YA-9 | Loop ile uyumlu, iterasyon başına yapılandırılmış ölçüt | Kalite kapısı kuran ekip | Doğrulayıcı döngünün dışında | ⏸ F-329 kapanınca |
| YA-10 | Karışık zincirde agent adımının iletim modu + kayıtta tip uyumu | Workflow kuran ekip | Loop bütün taslakları iletir | ✅ F-324 |
| YA-11 | Anthropic güncel API: önbellek TTL 1 saat, talimat sınırında breakpoint, adaptive thinking, örnekleme bayrağı | FinOps | Farklı makaleler önbellek önekini paylaşmaz | ✅ F-346 (ikinci tur cevap 6) |
| YA-12 | OpenAI strict modu + sağlayıcıya giden etkin şemanın görünürlüğü | Çok modelli host | Cevap 12: başka modeller de olabilir | ✅ F-347 (ikinci tur cevap 6); önkoşul F-340 |
| YA-13 | Run'a baytsız artifact referansı (manifest) | Nöbetçi mühendis | İkinci medya tüketicisi | ⏸ kullanıcı erteledi |
| YA-14 | Transkripte kelime zamanı + `SpeechAudio.Duration` | Altyazı üreten ekip | whisper.cpp yalnız zaman için kurulur | ⏸ |
| YA-15 | Eval case'te belge kanalı | Ölçme–iyileştirme | Üretim ve eval farklı mesaj şekli | ⏸ F-343'e bağlı |
| YA-16 | SKILL.md içe aktarma (podcast ham #9'u daraltır) | İlk agent'ını kuran geliştirici | Tek LLM varlığı bir skill | ⏸ değer düşük |
| YA-17 | MCP sunucusu başına tool timeout + progress | MCP kullanan ekip | Global 30 sn | ⏸ bu tüketici job yolunu seçer |
| YA-18 | .NET dışı worker için HTTP job protokolü | Python/Node ekipleri | Kuyruk yalnız .NET worker'la kullanılır | ⏸ gereksiz; A17 |
| YA-19 | Ekleri run'a göre listeleme | Operatör | `runId` filtresi yok | ⏸ `sessionId` filtresi yeter |
| YA-20 | Dağıtık run iptali / uçuştaki job item'ına iptal | Nöbetçi mühendis | Başka instance'taki iptal 409 | ⏸ F-323 ile birlikte değerlendirilir |
| YA-21 | Skill script'ini harici sandbox'a yönlendiren seam (podcast ham #13) | Kurumsal platform ekibi | Cevap 17: ikinci operatör-kodu vakası | ✅ F-345 (ikinci tur cevap 1) |
| YA-22 | Yalnız dağıtım amaçlı handler anahtarı kaydı | Bölünmüş topoloji | API süreci handler tipini kaydetmek zorunda | ❌ zaten var: public `IJobStore.EnqueueAsync` |
| YA-23 | Operatör kodunu Tracon'un çalıştırdığı düğüm | — | Cevap 17 bunu yeniden gündeme getirir | ❌ K2 / L20 (podcast ham #18) |

**Kullanıcının elemesi (2026-10-11):** cevap 20 — yedi kalem ADAYLAR'a, YA-3 ve
YA-13 ertelendi, mevcut kalemlerin değişiklikleri işlenir. Diğer ⏸ kalemler
onaya sunulmadı; yalnız burada durur.

---

## 4. Ekosistem taraması (Aşama 3.2)

| Kaynak | Bakılan tarih | Ne gördük | Tracon'e etkisi |
|---|---|---|---|
| Remotion lisans ve terms (remotion.pro/license, terms v4/v5) | 2026-10-11 | Sunucu tetiklemeli render "Automators": render başına $0.01, aylık $100 alt sınır; v5 terms 5.0 ile yürürlüğe girer; son sürüm 4.0.534 | Tüketici işletme kararı; `licenseKey` worker'a `secret` olarak gider |
| Remotion SSR (docker, linux-dependencies, render-media, calculate-metadata) | 2026-10-11 | Debian tabanı (Alpine yok); `selectComposition`/`renderMedia` + JSON `inputProps`; `cancelSignal`, `onProgress` | İçerik veri olur; iptal ve ilerleme Tracon run'ına bağlanabilir |
| `@remotion/google-fonts` | 2026-10-11 | `loadFont()` render'ı font inene kadar bekletir; yerel alternatif `@remotion/fonts` | Egress'i kapalı sandbox'ta fontlar image'a gömülür |
| VoxCPM2 (HF kartı, GitHub) | 2026-10-11 | Apache-2.0; CUDA ≥ 12; yaklaşık 8 GB VRAM; RTF ~0.30 (RTX 4090, üretici beyanı); sunucu modları Nano-vLLM (üretime hazır değil) ve vLLM-Omni | GPU TTS ayrı servis; .NET sürecine girmez |
| Trendyol-TTS (HF kartı) | 2026-10-11 | MIT etiketi, özel veri seti, "research artifact" | Kullanıcı lisansı teyit etti (cevap 4) |
| ElevenLabs TTS with-timestamps | 2026-10-11 | Karakter hizalaması; `previous_text`/`next_text`, `previous_request_ids` (≤ 3), `seed`, `voice_settings` | F-296'nın istek ayarları dilimi; whisper adımını kaldırır |
| ElevenLabs Forced Alignment | 2026-10-11 | Türkçe destekli; EU endpoint var | Yerel motor sesi için hizalama seçeneği |
| ElevenLabs modeller ve veri yerleşimi | 2026-10-11 | `eleven_v4` (2026-09-28); EU yerleşimi yalnız Enterprise | Kiracı profili kararı |
| Azure Speech (WordBoundary, batch synthesis, bölgeler) | 2026-10-11 | Batch `wordBoundaryEnabled`; tr-TR standart ve MAI-Voice-2 (preview); westeurope/swedencentral | tr-TR kelime sınırı açıkça doğrulanmadı |
| Google Cloud TTS timepoints | 2026-10-11 | Yalnız SSML `<mark>`; otomatik kelime zamanı yok | Google seçen kiracıda ayrı hizalama |
| WhisperX + `mpoyraz/wav2vec2-xls-r-300m-cv7-turkish` | 2026-10-11 | BSD-2 + CC-BY-4.0; Türkçe hizalama modeli | Ticari uyumlu yerel hizalama |
| MCP spec 2026-07-28, Tasks extension; C# SDK 2.2.0 | 2026-10-11 | Tasks resmi extension; stream koparsa istek kaybolur | Tracon `RunBackedMcpTaskStore` `input_required`'ı boş bırakır; onay MCP üzerinden taşınmaz |
| `Microsoft.Agents.AI.Workflows` 1.24.0; `Microsoft.Agents.AI.DurableTask` 1.16.0-preview | 2026-10-11 | RequestPort + checkpoint var; durable yol prerelease ve DTS ister | K-008 gereği çekirdeğe girmez; YA-3 erteleme gerekçesi |

---

## 5. Üç kanalın çıktısı (Aşama 1)

### Kanal 1 — yeni aday

| F-NN | Başlık | Aday dosyasına yazıldı mı |
|---|---|---|
| F-318 | Bağlantıdan bağımsız workflow yürütmesi (`run`/`resume`/`respond`) | ✅ |
| F-319 | Workflow HITL karar sözleşmesi | ✅ |
| F-320 | Kesilen model çıktısı ayrı sonuç | ✅ |
| F-321 | Çöken workflow run'ını checkpoint'ten otomatik sürdürme | ✅ |
| F-322 | Kiracı başına workflow eşzamanlılığı ve görünür kuyruk | ✅ |
| F-323 | Job süre sınırı ve iptalin handler'a ulaşması | ✅ |
| F-324 | Karışık zincirde agent adımının iletim modu ve tip uyumu | ✅ |
| F-345 | Skill script yürütmesini harici sandbox'a yönlendiren public seam | ✅ |
| F-346 | Anthropic güncel API ayarları | ✅ |
| F-347 | OpenAI strict şema modu ve etkin şema görünürlüğü | ✅ |

**Mevcut kalemlere işlenen değişiklik:** F-294 (kuyruklu agent run'ı da
parametre ve belge taşır) · F-295 (operatör canlı günceller; değer artar) ·
F-296 (istek ayarları dilimi diyalogdan önce) · F-298 (🟡 → 🟠; önce F-336) ·
F-299 (çok birim, sağlayıcı/model, konsol) · F-300 (run'a tanım sürümü damgası,
üretim boyunca sürüm sabit) · F-303 (agent düğümü süresi ve workflow başına
`RunTimeout`).

### Kanal 2 — kusur

Hepsi 2026-10-11 tartışmasında kullanıcıya söylendi. `kusur-giderme` henüz
koşulmadı. Onaylanan sıra (cevap 19): F-325 → F-326 → F-328 → F-327 → F-329 →
F-330 → F-331, sonra F-332. ADAYLAR satırları kısadır (aday dosyası bütçesi);
tam kanıt ve repro fikri
[raporun § 12'sindedir](../arsiv/incelemeler/2026-10-11-explainer-video-tuketici-uyum-raporu.md#12-tracon-kusurları-kanıt-tablosu).

**Bütçe notu:** Aday dosyası bu turla 80 KB bütçesinin %1 altına indi. Yer
açmak için F-95, F-178 ve F-179'un gövdeleri
[`arsiv/ERTELENEN-ADAYLAR.md`](../arsiv/ERTELENEN-ADAYLAR.md) § *Tetik bekleyen
gövdeler*'e taşındı; satırları § *Tetik bekleyenler* tablosunda kaldı. İkinci turda aynı tablodaki on altı uzun satır kısaltıldı; özgün metinleri aynı arşiv bölümündedir.

| F-NN | Bulgu (tartışmadaki KG numarası) | Kanıt seviyesi |
|---|---|---|
| F-325 | Workflow SSE keep-alive yok; konsol 30 sn'de keser; slot beklemesinde kapanış çerçevesiz kopar (KG-1, KG-12) | Ölçüldü (kod; elle okundu) |
| F-326 | Job worker'ın kira, deneme ve iptal sözleşmesi (KG-2, KG-3, KG-4) | Ölçüldü (kod; elle okundu) |
| F-327 | Bekleyen insan isteği kalıcı olarak yanıtlanamaz olur (KG-5, KG-9) | Ölçüldü (kod) |
| F-328 | Workflow agent adımında deadline/bütçe reddi içerik olarak akar (KG-6) | Kod okuması; repro gerekli |
| F-329 | Loop + yapılandırılmış doğrulama birleşik metni doğrular (KG-7) | Ölçüldü (kod + F-317 katman probu) |
| F-330 | Parametreli agent kuyruklu/batch/replay yolunda bağlanmadan koşar (KG-11) | Kod okuması; repro gerekli |
| F-331 | Sabit sürümlü çözümleme güncel descriptor'ı kullanır (KG-18) | Ölçüldü (kod) |
| F-332 | Ek ömrü: harici bayt silinmez; workflow eki 7 günde yetim (KG-8, KG-25) | Ölçüldü (kod) |
| F-333 | Belgelenen webhook'lar yayınlanmaz (KG-10) | Ölçüldü (grep) |
| F-334 | `ExecutorFailed` ham istisna mesajı (KG-13) | Ölçüldü (kod + test) |
| F-335 | Konsol başka run'ın isteğini açamaz; K-130 yorumu/testi çelişkili (KG-14) | Ölçüldü (kod) |
| F-336 | Ses katmanı sözleşme kaymaları (KG-15, KG-22, KG-24) | Ölçüldü (kod) |
| F-337 | Workflow kökü atıf taşımaz (KG-16) | Ölçüldü (kod) |
| F-338 | Düğüm retry'ı kalıcı 4xx'i geçici sayar (KG-17) | Ölçüldü (kod) |
| F-339 | Skill'siz harness CWD'den skill keşfeder (KG-19) | Kod + MAF XML; çalıştırılmadı |
| F-340 | OpenAI `ProviderSettings` sessizce yok sayılır (KG-27) | Ölçüldü (kod) |
| F-341 | `respond` ve kayıt doğrulama sözleşmesi (KG-20) | Ölçüldü (kod) |
| F-342 | Belge-kod kaymaları demeti (KG-21, KG-23, KG-30) | Ölçüldü (kod + doküman) |
| F-343 | Eval yolunda bağlam kaybı (KG-26, KG-29) | Ölçüldü (kod) |
| F-344 | Reconciliation workflow kökünü agent gibi sürdürür (KG-28) | Kod okuması |

**Kusur değil, karar:** Yanıtlanmış isteğe ikinci yanıt yeni dal açar (K-130,
kasıtlı). Bu tüketicide ikinci render demektir; opt-in tek yanıt F-319'dadır.
Kod yorumu ve test adı K-130 ile çeliştiği için F-335'e girdi.

### Kanal 3 — yeniden açılması önerilen karar

| Karar | Gerekçesi | Neyin değiştiği | Kullanıcının kararı |
|---|---|---|---|
| A17 (2026-09-07, mimari ret değil) | Genel motor somut vaka bekler | İkinci medya tüketicisi geldi; ama genel motor değil, dar bir "devret ve bekle" dilimi yeter | Yeniden açılmadı; dilim YA-3 olarak ertelendi |
| K-130 | Yanıtlanan run `AwaitingInput` kalır; ikinci yanıt dal açar | Pahalı yan etkili hatta dal ikinci render demektir | Korunur; opt-in tek yanıt F-319 |
| K-128 | Bekleyen istek olay yükünde yaşar | Kiracı geneli kutu olay taramasını pahalı yapabilir | F-319 kapsamında ölçülür; kararın kendi yeniden açılma koşulu |
| L43 | Sürüm label'ı eklenmedi | Canlı şema güncellemesi sürüm sabitlemeyi gerektirir — label değil | Açılmaz; ihtiyaç F-300 + F-331 ile karşılanır |
| K2 / L20 | Arayüzden tool kodu yok | Cevap 17: operatör canlı kod yazar | Korunur; operatör kodu tüketicinin sandbox'ında koşar |
| Podcast ham #13 (ertelenmiş, ret değil) | "Tracon'a yeni kod yüzeyi açmak gerekmiyor" | İkinci tüketici operatör kodunu çalışma anında koşturuyor; seam yeni yazma yüzeyi açmaz, yalnız mevcut stored script'in nerede koştuğunu değiştirir (K-086 istisnası içinde) | Yeniden açıldı (ikinci tur cevap 1) → F-345. Ham #18 reddi korunur |

---

## 6. Reddedilenler ve ertelenenler

| Fikir | Gerekçe | Kalıcı mı | Nereye yazıldı |
|---|---|---|---|
| YA-23 · Operatör kodunu Tracon'un çalıştırdığı düğüm | K2 / L20 | Evet | Ret listesi zaten kapsıyor |
| YA-22 · Dağıtım-yalnız handler kaydı | Public `IJobStore.EnqueueAsync` ihtiyacı karşılar (`IJobStore.cs:20-40`) | Evet | Yalnız burada |
| YA-3 · Devret ve bekle düğümü | Kullanıcı erteledi; C yolu workflow'suz kurulur; MAF durable yolu prerelease | Hayır | Yalnız burada |
| YA-13 · Artifact manifesti | Kullanıcı erteledi; etiket + `Custom` olay + tüketici tablosu yeter | Hayır | Yalnız burada |
| YA-2b, YA-5, YA-9, YA-14…YA-20 | Onaya sunulmadı veya bağımlı | Hayır | Yalnız burada |

---

## 7. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap |
|---|---|
| § 0'daki 22 soru | § 0 tablosu |
| Kusur sırası ve aday yazımı | Cevap 19 ve 20 |

**İkinci tur soruları ve cevapları (2026-10-11):**

| # | Soru | Cevap |
|---|---|---|
| 1 | Podcast ham #13 (harici sandbox seam'i) yeniden değerlendirilsin mi? | Evet → F-345 |
| 2 | Operatör script'i GPU'ya erişir mi? | Örnekle yeniden soruldu → **varsayılan:** operatör kodu yalnız CPU; GPU yalnız host'un TTS servisinde |
| 3 | Operatör yeni içerik kuralı da tanımlar mı? | Örnekle yeniden soruldu → **varsayılan:** kural veri (form/JSON Schema + hazır kural türleri); şema bileşen yayınında sandbox'ta bir kez çıkarılır |
| 4 | Host bütün kiracıları tek konsoldan mı yönetir? | Örnekle yeniden soruldu → **varsayılan:** host kimlik sağlayıcısında "kiracı olarak gir" kurar; Tracon'da değişiklik ve yeni aday yok |
| 5 | LLM ve TTS anahtarlarının sahibi | **Host** (podcast turuyla aynı); F-298'de kiracı BYOK ikincil |
| 6 | OpenAI strict ve Anthropic güncel API aday olsun mu? | Evet → F-347, F-346 |
| 7 | Ret notu sahne düzeyinde mi, içerik düzeyinde mi? | **İçerik düzeyi**; not isteğe bağlı sahne kimliği taşıyabilir |

**Üçüncü tur (2026-10-12) — kullanıcı cevapları ve sonuç:**

| # | Soru veya istek | Cevap ve sonuç |
|---|---|---|
| 1 | Podcast script'ini de operatör mü yazar? | Evet, uygulama ayaktayken canlı günceller; iki hat tek sandbox runner'ını paylaşır |
| 2 | Makale kuyruk yüküne girsin mi? | **Hayır (ikinci yol).** Host job'una yalnız üretim kimliği girer; handler makaleyi okur ve agent'ı `IAgentCatalog` ile süreç içinde çalıştırır |
| 3 | İleride agent onayı nasıl gelir? | Harness `Loop` ölçütü olarak; denetim izi yeni aday F-411 |
| 4 | K2 gevşetilsin mi? | Hayır. Operatör kodu host'un sandbox runner'ında koşar; skill script seam'i Faz 202'ye dönüştü |
| 5 | Kusurlar ve planlar | Açık kusurların hepsi kapandı (F-341…F-344, F-354, F-360, F-364…F-367, F-369…F-372; K-903…K-911). On aday yedi faza dönüştü (Faz 196…202). Kapanışların sınıf taramaları F-386…F-404'ü, plan kapsam dışları F-405…F-411'i üretti |
| 6 | Bu turun dört yeni kusuru (F-382…F-385) | Bakımcı ayrı bir oturumda giderecek; bu turda kodlarına dokunulmadı |

Raporun bu tura göre yeniden yazılmış hâli:
[`arsiv/incelemeler/2026-10-11-explainer-video-tuketici-uyum-raporu.md`](../arsiv/incelemeler/2026-10-11-explainer-video-tuketici-uyum-raporu.md).

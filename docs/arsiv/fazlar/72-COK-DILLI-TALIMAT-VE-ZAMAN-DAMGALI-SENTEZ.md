# Faz 72 — Çok Dilli Talimat ve Zaman Damgalı Sentez

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-117**, **F-118**
> **Önkoşul:** [Faz 19](19-SURUM-KARSILASTIRMA-VE-AB.md) — agent sürümleme ve diff · [Faz 28](28-SES-TOOLLARI.md) — ses tool'ları ve ElevenLabs istemcisi
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.Voice`, `Tracon.Sql.Shared`, `Tracon.AspNetCore`, `Tracon.UI`
> **Yeni paket:** Yok · **Migration:** **YOK** — plan yanlıştı, bkz. Plandan Sapmalar ve K-499
> **Public API:** **büyüdü** — `AgentDefinition.InstructionsByCulture`, `AgentDefinitionRequest.InstructionsByCulture`, `AgentRunRequest.Culture`, `IAgentSource`/`IAgentCatalog`/`IVersionedAgentSource` imzalarına `culture`, `CompiledAgentCache`'e culture'lı aşırı yükler, `InstructionCultureResolver` (yeni tip), `SpeechRequest.IncludeTimestamps`, `SpeechAudio.Alignment`, `SpeechAlignment` (yeni tip), `SpeakRequest.IncludeTimestamps`, `SpeakResponse.Alignment`. `PublicAPI.Shipped.txt` hâlâ boş — bedavaydı.
> **Site etkisi:** `concepts/agents.md`, `guides/voice.md` güncellendi. `reference/configuration.md`'ye dokunulmadı — gerekçe: bu faz `TraconOptions`/`VoiceOptions`'a yeni bir yapılandırma anahtarı eklemedi (`culture`/`includeTimestamps` istek başına alan, config değil)
> **Manuel test alanı:** [`docs/manuel-test/19-COK-MODLULUK-VE-SES.md`](../../manuel-test/19-COK-MODLULUK-VE-SES.md) (MT-MM-091..094) · [`docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md) (MT-CORE-075..081)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## ⚠️ Bu faz iki zayıf kalemi taşır

Aday listesinde ikisinin de **karşı görüş** satırı "tek başına faz olmamalı"
diyordu: F-117 yalnız 1 ve 5 numaralı merceklerden, F-118 yalnız 1 numaralı
mercekten iyi görünüyor. Kullanıcı kararıyla tek fazda birleştirildiler
(2026-08-18).

🚨 **Ortak yanları zayıftır** — ikisi de tüketiciye dönük küçük ergonomi
işidir, ortak bir altyapı paylaşmazlar. Bu, `faz-planlama` Adım 0'ın "iki kalem
ancak aynı altyapıyı paylaşıyorsa birleşir" kuralının bilinçli bir istisnasıdır
ve bedeli **bulanık DoD** riskidir. Karşı önlem: bitiş ölçütleri iki ayrı blok
hâlinde yazıldı ve biri diğerini bekleyemez. İki kalem bağımsız olarak
uygulanabilir ve bağımsız olarak iptal edilebilir.

---

## Amaç

İki bağımsız ergonomi boşluğu: - **F-117** — agent talimatı tek dillidir. İki dilde çalışan bir üründe iki ayrı agent tanımı gerekir; sürüm geçmişleri, eval kümeleri ve deneyleri ayrışır. - **F-118** — konuşma sentezi kelime düzeyinde zaman damgası döndüremez. Altyazı, vurgulama ve transcript senkronu yapılamaz.

## Bitiş Ölçütleri (DoD)

### F-117

- [x] Kültür sözlüğü boşken hiçbir davranış değişmez — `InstructionCultureResolutionTests`,
      MT-CORE-075 (gerçek `samples/Tracon.Api` koşumu)
- [x] `tr-TR` → `tr` → varsayılan geri düşüş zinciri çalışır; eşleşmeyen kültür
      **hata vermez** — `InstructionCultureResolutionTests`, MT-CORE-076..078
      (gerçek Anthropic Claude'a karşı koşuldu, bkz. Doğrulama komutları)
- [x] `CompiledAgentCache` anahtarı kültürü taşır — `CompiledAgentCacheTests.Recompiles_when_the_culture_changes`,
      `CultureInstructionEndpointTests.Back_to_back_runs...`, MT-CORE-080
- [x] `Accept-Language` başlığı talimatı **değiştirmez** — `CultureInstructionEndpointTests.Accept_Language_header_is_ignored`,
      MT-CORE-079
- [x] Sürüm diff'i çok dilli metni gösterir — `agent-detail.tsx`'te `cultureUnion` ile
      per-culture `DiffView` bölümü; manuel doğrulama MT-CORE-081 (👤 gerekir)
- [x] Üç SQL sağlayıcısı + bellek içi sözleşme koşumları geçer — `AgentDefinitionStoreContract.SaveAsync_round_trips_all_definition_fields`,
      gerçek Postgres (1118/1118), SqlServer (558/558), Sqlite (572/572) koşuldu

### F-118

- [x] `includeTimestamps` verilmediğinde bugünkü yanıt birebir aynı —
      `ElevenLabsSpeechClientTests.Without_IncludeTimestamps_alignment_is_null_and_the_plain_path_is_used`,
      `VoiceEndpointTests.IncludeTimestamps_false_returns_no_alignment`, MT-MM-091
- [x] `includeTimestamps: true` hizalama listesi döner; çıktı belgeye yazıldı —
      MT-MM-092, gerçek ElevenLabs'a karşı koşuldu (bkz. Doğrulama komutları:
      "Hello world" → 11 karakterlik hizalama, artan `start`/`end`)
- [x] Sağlayıcı desteklemiyorsa `null` döner — hata değil —
      `ElevenLabsSpeechClientTests.Missing_alignment_in_a_timestamped_response_yields_null_not_an_error`,
      `Mismatched_alignment_array_lengths_yield_null_rather_than_throwing`
- [x] Akışlı yol davranışı ölçüldü ve belgelendi — **desteklenmiyor, açıkça
      reddediliyor**: `ElevenLabsSpeechClientTests.Streaming_synthesis_rejects_IncludeTimestamps_explicitly`,
      K-502, MT-MM-093
- [x] Maliyet muhasebesi değişmedi — `VoiceEndpointTests.IncludeTimestamps_true_returns_the_alignment_and_the_same_cost_accounting`,
      MT-MM-094 (gerçek ElevenLabs'a karşı: `characters`/`cost` iki çağrıda birebir aynı)

### Ortak

- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/`format`
      tüm çözümde 0 uyarı/0 hata (bu kapanıştan hemen önce yeniden koşuldu)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı —
      aşağıdaki Doğrulama komutları bölümü gerçek çıktı taşır
- [x] `secret` taraması boş döndü — bu fazın eklediği hiçbir dosyada eşleşme yok
      (taramanın gösterdiği tüm satırlar önceki fazlardan kalma, yerel dev
      şifreleri/örnek metinler)
- [x] Manuel kabul case'leri iki alan dosyasına eklendi; otomatikleştirilebilenler
      koşuldu — MT-CORE-075..081, MT-MM-091..094; HTTP ile koşulabilenler
      (075-080, 091-094) gerçek sunucuya karşı çalıştırıldı, 081 ve MT-MM-093'ün
      insan-gerekli/otomatik parçaları işaretlendi
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 1 🔴 bulundu
      (`SourceLanguageTests` ihlali, test verisindeki Türkçe örnek metin) ve
      düzeltildi; 2 🟡 kararlara yazıldı (K-503, K-504); 3 🟢 ADAYLAR.md'ye
      (F-123) veya kapsam dışı bırakıldı
- [x] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz —
      `concepts/agents.md`, `guides/voice.md`
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı —
      `i18n.test.ts` geçti; javascript 173.5 KB gzip / embedded 148.8 KB brotli
      (budget 250 KB), Faz 68 sonrası ölçülen 151.3 KB'den **düşük**
      (araya giren fazların küçültmesi bu fazın payını maskeliyor; ayrı
      ölçülemedi)

### Doğrulama komutları — gerçek çıktı (2026-08-19, `samples/Tracon.Api`, gerçek Anthropic + ElevenLabs)

```bash
# Kültür çözümlemesi — agent instructionsByCulture={"tr": "..."} ile oluşturuldu
curl -s -X POST "$APU/api/agents/faz72-polyglot/run" -H "$APB" -H 'content-type: application/json' \
  -H 'Idempotency-Key: demo' -d '{"message":"how are you?","culture":"tr"}' | jq -r '.response.messages[0].contents[0].text'
# → "İyiyim, teşekkür ederim, sen nasılsın?"

curl -s -X POST "$APU/api/agents/faz72-polyglot/run" -H "$APB" -H 'content-type: application/json' \
  -H 'Idempotency-Key: demo2' -d '{"message":"how are you?","culture":"de"}' | jq -r '.response.messages[0].contents[0].text'
# → "I'm doing well, thank you for asking!"  (de eşleşmiyor, varsayılana düşer — hata YOK)

# Hizalama — gerçek ElevenLabs
curl -s -X POST "$APU/api/voice/speak" -H "$APB" -H 'content-type: application/json' \
  -d '{"text":"Hello world","voiceId":"hpp4J3VqNfWAUOO0d1Us","includeTimestamps":true}' | jq '.alignment | length'
# → 11  ("Hello world" = 11 karakter)
```

---

## Plandan Sapmalar

1. **Migration seti yazılmadı (K-499).** Plan "üç migration seti gerekli" diyordu.
   Uygulama başlamadan önce ölçüldü: `agent_definitions.definition` zaten TÜM
   `AgentDefinition` içeriğini `jsonb` blob'unda taşıyor (`AgentDefinitionPayload`,
   K-027'nin polimorfik-olmayan istisnası); `InstructionsByCulture` yeni bir
   sütun değil, mevcut blob'un yeni bir alanıdır. Bu, `faz-uygulama` Adım 1'in
   ("planın yapısal iddiasını kabul etmeden ölç") tam bir örneğidir.
2. **`SpeechAlignment` KARAKTER bazlıdır, planın taslağı "word-level"
   varsayıyordu (K-501).** Plan bunu bilerek "doğrulanmadı" işaretlemişti
   (Açık Soru 4). ElevenLabs'ın gerçek OpenAPI şeması (`api.elevenlabs.io/openapi.json`,
   doğrulandı) `CharacterAlignmentResponseModel` döner — karakter granülerliği.
   `SpeechAlignment.Character` alan adı `Text` değil `Character`'dır.
3. **`Tools/SpeakTool.cs`'e dokunulmadı (K-504).** Plan dosya listesi bunu
   "değişir" diye işaretlemişti. Model-çağrılabilir `speak` tool'unun şeması
   `includeTimestamps` almaz; hizalama yalnız operatör HTTP yolunda
   (`POST /api/voice/speak`) — planın kendi HTTP uç tablosu zaten yalnız o
   ucu adlandırıyordu.
4. **Kültür yalnız KÖK agent'ın derlenmesinde kullanılır (K-503, denetimde
   bulundu).** `CallableAgentResolver`/`ChildAgentInvoker`, `EvalJobHandler`,
   `RunReplayService` her zaman `culture: null` çözümler. Plan bu sınırı
   adlandırmadı; ADAYLAR.md'ye F-123 olarak yazıldı.
5. **`IAgentSource`/`IAgentCatalog`/`IVersionedAgentSource` imzaları
   değiştirildi, yeni aşırı yükleme eklenmedi.** `culture` parametresi mevcut
   `ResolveAsync`/`ResolveVersionAsync` metotlarına eklendi (yeni bir aşırı
   yükleme değil) — pakette hiçbir şey henüz yayınlanmadığı için (`PublicAPI.Shipped.txt`
   boş) kırıcı değişiklik bedavaydı; ~20 üretim çağrı yeri ve ~9 test dosyası
   mekanik olarak güncellendi (imza değişikliği derleyiciyi zorladı).

## Bu Fazda Verilen Kararlar

K-499, K-500, K-501, K-502, K-503, K-504 — bkz. `docs/KARARLAR.md`. Ayrıca
kapsam dışı archival: K-422 ve K-389'un tam gerekçesi `docs/arsiv/KARARLAR-GECMISI.md`'ye
taşındı (`KARARLAR.md` bütçesini bu fazın altı yeni kararı aştırdığı için —
içerik silinmedi, taşındı).

## Denetim Bulguları

Bağımsız denetim taze bağlamlı bir alt agent ile koşuldu (`.agents/skills/faz-denetim/SKILL.md`).

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `SourceLanguageTests` ihlali: test verisindeki örnek talimat metni gerçek Türkçe sözcükler taşıyordu (`tests/**` K-408 kapsamında) | **Düzeltildi** — `CultureInstructionEndpointTests.cs` ve `AgentDefinitionStoreContract.cs`'teki örnek metinler İngilizce köklü, dil-nötr placeholder'a çevrildi (`"Answer using the tr/fr culture text."`); `dotnet test` yeniden koşuldu, 998/998 |
| 2 | 🟡 | `CallableAgentResolver` her zaman `culture: null` gönderiyor — alt agent ebeveynin kültürünü görmez | **Gerekçelendi** — K-503 olarak karar defterine yazıldı; F-123 olarak ADAYLAR.md'ye eklendi |
| 3 | 🟡 | Planlanan dosya listesi `Tools/SpeakTool.cs`'in değişeceğini söylüyordu, dokunulmadı | **Gerekçelendi** — K-504 olarak karar defterine yazıldı, Plandan Sapmalar #3 |
| 4 | 🟢 | `EvalJobHandler` kültüre özgü talimatı test edemiyor | ADAYLAR.md F-123'e dahil edildi |
| 5 | 🟢 | `RunReplayService` orijinal `run`'ın kültürünü saklamadığı için yeniden oynatma kültürü koruyamıyor | ADAYLAR.md F-123'e dahil edildi |
| 6 | 🟢 | `agent-editor.tsx`'te kültür satırları `key={index}` kullanıyor | Devredilmedi — kontrollü input deseni (mevcut `fallbacks` listesiyle aynı desen) satır silindiğinde değer kaymasını önlüyor; gerçek bir kusur değil |

🔴 kapandıktan sonra dört kapı (`build`/`test`/`pack`/`format`) yeniden koşuldu — hepsi 0 uyarı/0 hata.

## Sonraki Faza Devir Notu

- **Kültür yalnız kök agent'ta çalışır (K-503/F-123).** Faz 73 (tüketici agent
  desteği) veya sonrası çok dilli bir alt-agent zinciri isterse önce `run`
  kaydına kültür alanı eklemek gerekir — bugün hiçbir yerde saklanmıyor.
- **`InstructionCultureResolver.Resolve` saf bir statik fonksiyondur** —
  `AgentDefinitionCompiler`'ın DIŞINDA, bağımsız test edilebilir. Yeni bir
  kültür kaynağı (ör. oturum bazlı varsayılan) eklenirse buraya dokunulur.
  `CompiledAgentCache`'in culture'ı anahtara EKLEMESİ gerektiğini unutma —
  yeni bir istek-bazlı boyut eklerken bu deseni tekrarla (bkz. K-380'in aynı
  dersi tenant için verdiği).
- **`SpeechAlignment` karakter bazlıdır.** Bir tüketici kelime/cümle
  granülerliği isterse (altyazı üretimi gibi) bu Tracon'in işi değil —
  `docs/72`'nin kapsam dışı tablosu ve `docs-site/guides/voice.md` bunu açıkça
  söylüyor. Yanlışlıkla "Tracon SRT üretsin" gibi bir işe girişilmesin.
- **Akışlı zaman damgalı sentez YAZILMADI (K-502).** ElevenLabs'ın
  `/stream/with-timestamps` ucu ayrı bir JSON-parça protokolü konuşur; bugünkü
  `ISpeechSynthesizer.SynthesizeStreamingAsync` imzası bunu taşıyamaz. Gerçek
  bir ihtiyaç ölçülürse yeni bir arayüz üyesi gerekir (mevcut üye
  kırılmadan) — `docs/hafiza/ses-ve-konusma.md`'de şema notu var.
- **`docs/KARARLAR.md` bütçesi bu fazda aşıldı ve iki eski karar (K-389,
  K-422) arşive taşındı.** Bir sonraki faz altı+ yeni karar eklerse aynı
  duruma düşer — `python3 scripts/dokuman-bakim.py` erken koşulmalı, bütçe
  aşımı fazın SONUNDA sürpriz olmasın.

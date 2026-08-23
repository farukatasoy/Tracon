# Faz 89 — Tool Çıktısı Boyut Sınırı

> **Durum:** ✅ Tamamlandı (2026-08-23)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-143** — Dalga 14 Küme Ö
> **Önkoşul:** [Faz 69](69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) — `Timeout` alanı ve sarmalayıcı zinciri; yeni alan onun **kardeşidir** ve aynı yerde yaşar · [Faz 13](13-BAGLAM-SIKISTIRMA-VE-BELLEK.md) (compaction — **tamamlayıcıdır, rakip değil**)
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Mcp`
> **Yeni paket:** Yok · **Migration:** Yok — kırpma olayı `RunEventType`'a bir üye ekler ve `run_events.type` bir `smallint`'tir (ölçüldü)
> **Public API:** Büyüyor — 1 tool alanı, 1 kurulum ayarı, 1 olay tipi üyesi, 1 taşınan yardımcı. `PublicAPI.Shipped.txt` toplamı **16 satır** (yalnız başlıklar; ölçüldü 2026-08-21) → Faz 7'den önce eklemek **bedava**
> **Tüketici yüzeyi:** `docs-site/` → `concepts/tools.md`, `guides/context-and-memory.md`, `reference/configuration.md`, `capabilities.md`
> · sevk edilen: yeni alan ve ayarın XML dokümanı, `src/AgentPrism.Core/README.md`. `api/` ve `http-api/` **üretilir**
> **Manuel test alanı:** [`docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`](../../manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md) · [`docs/manuel-test/20-BELLEK-RAG-BAGLAM.md`](../../manuel-test/20-BELLEK-RAG-BAGLAM.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/89-TOOL-CIKTISI-BOYUT-SINIRI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Faz 88'den Devralınan Sözleşmeler

| Sözleşme | Faz 89 etkisi |
|---|---|
| `ToolRegistry.Create(IServiceProvider)` images açıkken `generate_image`ı kayıt listesine ekler | Yeni output sınırı sarmalayıcısı, statik kayıtlarla birlikte bu built-in tool'a da uygulanmalıdır. Ayrı bir kayıt yolu açma. |
| `generate_image` `ToolEffect.External` taşır | Devam koşusunda otomatik tekrar edilmez. Kırpma bu effect veya `SafeToRepeat` kararını değiştiremez. |
| Image attachment yazısı run scope `TenantId`si ile yapılır; URI indirimi bounded ve guard'lıdır | Tool sonucunu kırpmak, attachment kimliği üretildikten sonraki metin yolundadır. Attachment yazma/egress yoluna ikinci bir kopya eklenmez. |
| `IImageGenerator` MEAI001 deneysel yüzeyidir | Faz 89 bunu kullanmaz. `ToolRegistry`teki mevcut dar pragma'yı genişletme. |

🚨 Faz 89 sarmalayıcı zincirini değiştirirken `ToolRegistry`teki conditional
image registration'ı normal `AgentPrismToolRegistration` gibi ele almalıdır.
Kayıt yalnız factory aşamasında eklenir; sarmalayıcı iki ayrı yola bölünürse
Faz 88'in `External`/kayıt/ölçüm sözleşmesi sessizce kayar.

---

## Amaç

Bir tool'un döndürdüğü metin **sınırsızdır** ve doğrudan bağlama girer. [Faz 69](69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) zamanı kısıtladı, **hacmi kısıtlamadı**. `CompactionSettings` bağlamı **sonradan** toparlar — ama toparlanacak token zaten harcanmıştır ve sıkıştırmanın kendisi bir model çağrısıdır.

## Bitiş Ölçütleri (DoD)

- [x] Sınır **konmadığında** çıktı dokunulmadan geçer (K1; sözleşme testi sabitler) — `No_output_limit_anywhere_means_no_truncating_layer_is_installed` (Core + Mcp), gerçek koşuda doğrulandı
- [x] `maxOutputBytes` konduğunda çıktı zarfa sarılır ve `truncated` + `omittedBytes` **her zaman** taşınır — `TruncatingAIFunctionTests`, gerçek koşuda doğrulandı
- [x] Modele giden toplam boyut sınırı **aşmaz** — zarf yükü bütçeye dâhil (`MinimumEnvelopeBytes` kuruculta garanti eder; `Even_the_smallest_accepted_limit_never_produces_an_oversized_envelope`)
- [x] Kırpılan çıktı **geçerli JSON** olarak modele ulaşır (içerik ne olursa olsun) — tırnak/ters bölü ağırlıklı içerik testiyle sabitlendi
- [x] Çok baytlı karakter ortadan kesilmez — CJK testiyle sabitlendi (Türkçe literal `SourceLanguageTests`'i kırdığı için CJK'ye çevrildi)
- [x] 🚨 **MCP** tool'ları da kırpılır — her iki sarmalama zinciri aynı halkaları taşır — `McpTenantToolsTests` üç yeni test
- [x] `McpResourceTrimming` **taşındı**, kopyalanmadı; MCP çağrıları `Core`'a döndü ve var olan MCP testleri geçiyor (30/30)
- [x] `ToolOutputTruncated` olayı `run_events`'e yazılır; migration **gerekmedi** (`smallint`, değer 26)
- [x] Tool alanı kurulum varsayılanını ezer (`Timeout` emsali) — `A_registration_level_output_limit_overrides_the_installation_default`
- [x] Sınır yokken sıcak yolda ek tahsis **yok** — sarmalayıcı hiç kurulmaz (K1 testi `GetService<TruncatingAIFunction>()` `null` döner)
- [x] Dört doğrulama kapısı sıfır uyarı verir — build/test/pack/format, tam koşum + frontend dahil
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (aşağıda)
- [x] `secret` taraması boş döndü (bu fazın dokunduğu dosyalarda; repodaki önceden var olan `Password=`/`sk-` test literalleri kapsam dışı — Faz 79/80/81/87 emsaliyle aynı)
- [x] Manuel kabul case'leri `docs/manuel-test/12-*` ve `18-*` içine eklendi (MT-OBS-048/049, MT-MCP-059); otomatikleştirilebilenler (048/049) gerçek koşuda doğrulandı, 059 👤 insan gerekir işaretiyle
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (üç 🔴 bulundu ve kapandı, ayrıntı aşağıda)
- [x] `docs-site/` güncellendi — *"gövdede kısıtlamak her zaman daha iyidir"* cümlesi `concepts/tools.md`'ye yazıldı
- [x] Arayüze dokunulduysa `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — rozet etiketi literal string'dir (emsal: `ContentMasked`/`ModelFallbackUsed`), yeni çeviri anahtarı gerekmedi; bundle 175,1 KB / 250 KB bütçe

### Doğrulama komutları — gerçek koşum (2026-08-23)

`samples/AgentPrism.Api`, gerçek bir OpenAI anahtarıyla, `AgentPrism__Tools__DefaultMaxOutputBytes=100`
ile başlatıldı. `support` agent'ına `get_order_status`'un doğal çıktısını
100 baytın üstüne çıkaracak uzunlukta bir sipariş numarasıyla soru soruldu.

```bash
# Kirpilan cikti gecerli JSON mu ve zarf 100 baytin altinda mi
curl -s -X POST http://localhost:5081/agentprism/api/agents/support/run \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"message":"What is the status of order ORD-0000000000000000000000000000000000000000000000000000-LONG?"}'
# -> functionResult.result =
#    {"truncated":true,"omittedBytes":57,"content":"Order ORD-00000000000000000000000000000000000000000"}
#    (tam 100 UTF-8 bayt — python3 -c "print(len(s.encode()))" ile ölçüldü)

# Kirpma olayi yazildi mi
curl -s http://localhost:5081/agentprism/api/runs/$RUN_ID/events -H "Authorization: Bearer $TOKEN" \
  | jq '.[] | select(.type=="ToolOutputTruncated")'
# -> {"type":"ToolOutputTruncated","text":"57 byte(s) omitted (limit 100)",
#     "toolName":"get_order_status","toolCallId":"call_6jX9qDFfy53VvPyfJqWHn1B4",
#     "payload":"{\"maxOutputBytes\":100,\"omittedBytes\":57}"}
# -> sequence 2, ToolInvoking (1) ile ToolInvoked (3) arasinda

# Sinir konmadan (K1) ayni tool cagrisi dokunulmadan geciyor mu
# -> ilk kosuda (sinir yok) functionResult.result ham metindi:
#    "Order ORD-1 has shipped. Estimated delivery: 2 days." (52 bayt, zarf yok)

# Iki sarmalama zinciri de halkayi tasiyor mu
grep -n "TruncatingAIFunction" src/AgentPrism.Core/Tools/ToolRegistry.cs \
                               src/AgentPrism.Mcp/Internal/McpTenantTools.cs
```

---

## Plandan Sapmalar

1. **Ölçüm birimi Açık Soru 1'de planlanandan farklı çıktı** — plan `result?.ToString()`
   öneriyordu (ToolInvocationTracker emsali). Bağımsız denetim MEAI'nin
   gerçek tel serileştirmesinin bunu KULLANMADIĞINI kanıtladı; gerçekleşen
   kural yalnız `string` ve `JsonElement`'i ölçer, başka bir ham CLR nesnesi
   dokunulmadan geçer. Ayrıntı: K-594.
2. **`MinimumEnvelopeBytes` planda YOKTU** — bağımsız denetim, `budget <= 0`
   erken çıkışının zarfın gerçekten sığdığını hiç kontrol etmediğini
   (`maxOutputBytes=10` iken zarf 51 bayt) reflection ile kanıtladı. Kurucuya
   57 baytlık bir taban eklendi (K-595); bu, `TruncatingAIFunction`'a yeni bir
   `public static readonly int` alan ekledi — planın "Planlanan Public API"
   bölümünde yoktu, gerekçesi K-595'tedir.
3. **Manuel test dosyası yönlendirmesi değişti** — plan `docs/manuel-test/20-BELLEK-RAG-BAGLAM.md`'yi
   işaret ediyordu; MCP tool kırpması için daha iyi bir alan eşleşmesi olan
   `18-MCP-VE-A2A.md` (`MCP` alan kodu) kullanıldı. `12-*` (OBS) ve `18-*`
   (MCP) case'leri eklendi, `20-*`'ye dokunulmadı.
4. **Arayüz `ui.md` güncellenmedi** — `dokuman-bakim.py --site-denetle` bunu
   `arayuz` kuralı altında tetikledi. `run-detail.tsx`'e eklenen tek şey bir
   rozet etiketidir (`EVENT_STYLE` sabiti); `ui.md` hâlihazırda `ContentMasked`/
   `ModelFallbackUsed` gibi kardeş olay tiplerinin hiçbirini rozet düzeyinde
   belgelemiyor — tutarlılık için aynı kapsam dışı bırakıldı.
   `--site-gerekce-yazildi` ile geçildi.
5. **`[AgentPrismTool]`/`AddTool`'a `MaxOutputBytes` eklenmedi** — `SafeToRepeat`
   (Faz 87) emsaliyle aynı, kasıtlı boşluk: bugün yalnız `AgentPrismToolRegistration`'ın
   DI kurucusundan set edilebilir. Sonraki faza devir notuna yazıldı.

## Bu Fazda Verilen Kararlar

- **K-592** — Tool çıktısı boyut sınırı UTF-8 bayt biriminde ölçülür.
- **K-593** — Zarf `{"truncated","omittedBytes","content"}` alanlarını taşır;
  `Utf8JsonWriter` + `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` ile üretilir.
- **K-594** — Yalnız `string`/`JsonElement` ölçülür; başka ham CLR nesnesi
  dokunulmadan geçer (denetim bulgusu, Açık Soru 1 kapatıldı).
- **K-595** — `maxOutputBytes`, `MinimumEnvelopeBytes`'ın altındaysa kurucu
  `ArgumentOutOfRangeException` atar (denetim bulgusu).
- **K-596** — `McpResourceTrimming` `Core`'a `TextTrimming` adıyla taşındı,
  `public` yapıldı.

Tam gerekçeler: `docs/KARARLAR.md` K-592–K-596.

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı agent, `.agents/skills/faz-denetim`) çalışma
ağacına karşı koştu ve dördü ölçülmüş, biri gerçek reflection çağrısıyla
kanıtlanmış üç 🔴 ve üç 🟡 bulgu üretti:

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Ölçüm `result.ToString()` kullanıyordu; gerçek tel serileştirmesi bunu kullanmıyor (POCO için tip adı döner) | 🔴 | **Düzeltildi** — yalnız `string`/`JsonElement` ölçülür (K-594); `A_raw_non_string_non_JsonElement_result_always_passes_through_untouched` ve `A_JsonElement_result_over_the_limit_is_truncated_via_its_raw_text` testleriyle sabitlendi |
| 2 | `budget <= 0` erken çıkışı zarfın gerçekten sığdığını kontrol etmiyordu; `maxOutputBytes=10` iken zarf 51 bayt üretiyordu | 🔴 | **Düzeltildi** — `MinimumEnvelopeBytes` kurucuda garanti eder (K-595); `Constructor_throws_for_a_positive_limit_too_small_to_ever_hold_an_envelope`, `Even_the_smallest_accepted_limit_never_produces_an_oversized_envelope` |
| 3 | `dotnet test` kırmızıydı — test dosyasındaki Türkçe literal (`çığöşü`) `SourceLanguageTests` taban çizgisini kırıyordu | 🔴 | **Düzeltildi** — CJK örneğe (`你好世界`) çevrildi |
| 4 | Fonksiyonel `ToolTruncationRecordingTests` ("store hatası koşuyu durdurmaz") planlanmış ama yazılmamıştı | 🟡 | **Düzeltildi** — `A_store_failure_while_recording_the_truncation_event_does_not_change_the_returned_result` eklendi |
| 5 | `AgentPrismOptionsValidator`'ın yeni `DefaultMaxOutputBytes` dalı testsizdi | 🟡 | **Düzeltildi** — `AgentPrismToolOptionsValidationTests` eklendi |
| 6 | `docs-site/` bu diff'te hiç dokunulmamıştı | 🟡 | **Düzeltildi** — `tuketici-dokuman-senkronu` uygulandı (dört yüzey güncellendi, dört kapı yeşil) |

🔴 bulgular kapandıktan sonra dört doğrulama kapısı **yeniden koşuldu** ve
yeşil çıktı (tam koşum, frontend dahil).

## Sonraki Faza Devir Notu

- **Devralınan sözleşme — sarmalayıcı zinciri artık DÖRT halka taşır:**
  `Authorizing → Timeout → ApprovalRequired → Truncating → gerçek fonksiyon`,
  hem `ToolRegistry.cs` hem `McpTenantTools.cs`'te. Beşinci bir halka
  eklenirse **her iki** yere elle taşınmalı (K-487'nin devamı).
- 🚨 **`TruncatingAIFunction` yalnız `string`/`JsonElement` sonuçları
  kırpar.** Kod üreticisinin ham CLR nesnesi döndüren bir tool'u (POCO/record)
  bu sınırı hiç görmez — alanın belgelenmiş sınırıdır (K-594), kusur değil.
  Bu kapsamı genişletmek reflection gerektirir ve `AgentPrism.Core`'un AOT
  sözleşmesini bozar; önce tip-güvenli bir serileştirme yolu (kaynak üretilen
  `JsonSerializerContext`) gerekir.
- 🚨 **`TruncatingAIFunction.MinimumEnvelopeBytes` (bugün 57 bayt) bir
  taban, öneri değildir.** Hem `AgentPrismToolRegistration.MaxOutputBytes`
  hem `AgentPrismOptions.Tools.DefaultMaxOutputBytes` bunun altında kurucuda/
  `AgentPrismOptionsValidator`'da reddedilir. Manuel test/demo yazarken bu
  tabanın üstünde bir değer seçilmeli (bkz. MT-OBS-048'in 100 baytlık örneği).
- **`[AgentPrismTool]` ve `AddTool()` hâlâ `MaxOutputBytes` taşımıyor** —
  `SafeToRepeat`'in aynı, kasıtlı boşluğu (Faz 87). Bugün tek yol
  `services.AddSingleton(new AgentPrismToolRegistration(..., maxOutputBytes: N))`.
  Ayrıca `SafeToRepeat` de `AgentPrismGeneratedTools.Create()`'in ürettiği
  kayda hâlâ hiç yazılmıyor — kaynak üreteciye (`ToolCandidate.cs`,
  `SourceWriter.cs`) her iki alanı BİRLİKTE eklemek ayrı bir aday olarak
  değerlendirilmeli.
- **`docs/hafiza/cekirdek-calistirma.md` bütçenin sınırında** (15999/16000,
  Faz 88'den devralındı, bu faz büyütmedi). Bu dosyaya dokunan bir sonraki
  faz muhtemelen bütçeyi aşacak; gerçek bir bölme o zaman ele alınmalı.
- Sıradaki faz `docs/YOL-HARITASI.md`'de üretilir; kapanışta `python3
  scripts/dokuman-bakim.py` ile yeniden üretildi.

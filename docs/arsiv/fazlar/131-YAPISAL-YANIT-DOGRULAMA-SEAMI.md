# Faz 131 — Yapısal Yanıt Doğrulama Seam'i

> **Durum:** ✅ Tamamlandı (2026-09-01)
> **Kaynak:** [kesif/2026-09-01-tuketici-feature-talepleri.md](../../kesif/2026-09-01-tuketici-feature-talepleri.md) — **F-174**
> **Önkoşul:** Yok
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.AspNetCore` (yalnız `RunEventType` yüzeyi), `.UI` (olay etiketi)
> **Yeni paket:** Yok · **Migration:** Yok — yeni `RunEventType` ve `RunErrorClass` değerleri mevcut `smallint` sütunlarına yazılır
> **Public API:** büyüyor — yeni arayüz, options, olay ve hata sınıfı. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya 1 satır; shipped giriş sıfır olduğu için bugün eklemek ucuz
> **Tüketici yüzeyi:** `docs-site/`: `guides/structured-output.md`, `concepts/runs.md` (olay listesi), `capabilities.md`, `reference/configuration.md` · sevk edilen: `IStructuredResponseValidator` XML `<example>`'ı, `en.ts`/`tr.ts` olay etiketi
> **Manuel test alanı:** [`docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md`](../../manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show da91139:docs/arsiv/fazlar/131-YAPISAL-YANIT-DOGRULAMA-SEAMI.md
> ```
>
> Damıtıldı 2026-09-01 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`AgentResponseFormat` bugün yalnız sağlayıcıya bir kısıt gönderir. Dönen yanıtı **hiçbir şey doğrulamaz**. Model boş içerik döndürdüğünde, JSON kesik geldiğinde veya şemaya uymayan bir alan geldiğinde `run` **başarılı** kapanır. Tüketici parse hatasını sonra alır. Böylece Tracon'in `run` sonucu ile gerçek kullanım sonucu ayrışır.

## Bitiş Ölçütleri (DoD)

- [x] `Enabled: false` iken davranış Faz 130 ile **birebir** aynıdır (case 1) — `Disabled_by_default_a_malformed_response_does_not_fail_the_run` (fonksiyonel), `Disabled_option_lets_an_invalid_response_through_unchanged` (birim)
- [x] Geçersiz yanıt `run`'ı `Failed` kapatır; `StructuredResponseInvalid` sınıfı ve `StructuredResponseRejected` olayı yazılır (case 2) — `Enabled_a_malformed_response_fails_the_run_with_the_structured_response_error_class`
- [x] Geçerli yanıt hiçbir olay üretmez (case 3) — `Enabled_a_valid_response_completes_the_run_and_writes_no_event` **ve** gerçek `samples/Tracon.Api` koşumu (aşağıya bak)
- [x] Doğrulayıcı istisnası **geçersiz** sayılır (case 4) — `A_throwing_consumer_validator_rejects_fail_closed`, `A_throwing_validator_rejects_fail_closed_instead_of_propagating`
- [x] Akışta doğrulama son güncellemeden sonra çalışır ve `run` `Failed` kapanır (case 5) — `Streaming_branch_still_closes_the_run_as_failed_after_the_content_already_streamed`, `Streaming_forwards_every_update_before_validating` **ve** gerçek streaming koşumu (geçerli kol)
- [x] Ham model çıktısı hata metnine yazılmaz (case 7) — `The_raw_response_text_never_reaches_the_run_error_message_or_the_rejection_events_own_fields`, `Rejection_writes_a_StructuredResponseRejected_event_on_the_ambient_run_scope`
- [x] Yeni `RunErrorClass` değeri 9'u kullanmaz; `RunErrorClassContractTests` yeşil — değer 14, `Value_nine_stays_retired` yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py ic-dongu`/`tarama` yeşil, tam `dotnet build` (arayüz dahil) 0 uyarı/0 hata
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıya bak
- [x] `secret` taraması boş döndü — `kapi.py tarama` ✅ temiz
- [x] Manuel kabul case'leri `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md` içine eklendi; otomatikleştirilebilenler koşuldu — §10, MT-GUARD-090..096 (geçersiz-yanıt kolu OpenAI'de teknik olarak üretilemez, bkz. Plandan Sapmalar)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/` güncellendi (`guides/structured-output.md`, `concepts/runs.md`, `capabilities.md`, `reference/configuration.md`); `npm run check` (content+build+links+weight) temiz
- [x] `en.ts` ve `tr.ts` eksiksiz (`dashboard.errorClass.StructuredResponseInvalid`); bundle payı ölçüldü ve yazıldı — aşağıya bak

### Doğrulama komutları — gerçek çıktı (2026-09-01, gerçek `gpt-5.4-mini`, `order-summary` demo agent'ı)

```bash
$ curl -s -X POST "$APU/api/agents/order-summary/run" -H "$APB" -H "content-type: application/json" \
  -H "Idempotency-Key: $(uuidgen)" -d '{"message":"Give me a summary for order ORD-1."}' | jq '.response.text'
"{\"orderId\":\"ORD-1\",\"summary\":\"Order ORD-1 has shipped and is estimated to be delivered in 2 days.\"}"

$ curl -s "$APU/api/runs/<id>" -H "$APB" | jq '{status, error}'
{"status": "Completed", "error": null}

$ curl -s "$APU/api/runs/<id>/events" -H "$APB" | grep -i "^event:"
event: run.started
event: tool.invoking
event: tool.invoked
event: message.delta
event: message.completed
event: run.completed
# StructuredResponseRejected yok — geçerli yanıt hiçbir olay üretmedi (case 3, DoD karşılandı)
```

Akışlı (SSE) kol da aynı agent'la gerçek OpenAI'ye karşı koşuldu:
`event: run` → bir dizi `event: update` → `event: done`; `event: error` **hiç
görünmedi**; kapanış `status: "Completed"`.

Geçersiz-yanıt kolu (case 2/4/5) gerçek OpenAI'de üretilemedi (OpenAI'nin
`response_format` modu söz dizimsel geçerliliği API sınırında garanti eder —
bkz. "Plandan Sapmalar"); bu kol `StructuredResponseEndpointTests.cs`'in 8
testiyle gerçek host + gerçek `RunRecordingAgent` zinciri üzerinden, scriptlenebilir
sahte bir sağlayıcıyla kanıtlandı.

### Bundle payı (2026-09-01, tam `dotnet build Tracon.slnx -c Release`)

```
javascript : 176.3 KB gzipped (budget 250 KB)
embedded   : 150.9 KB brotli, from 682.4 KB (widget bütçesi 30 KB, ayrı ölçülür)
```

`llms.txt` 20 477 B (bütçe 20 480 B), `Tracon.AgentMap.md` 9 902 B (bütçe 10 240 B) —
agent map değişmedi (yeni satır eklenmedi, mevcut "Structured output" satırının
yalnız haritaya girmeyen Boundary sütunu genişletildi, bkz. "Plandan Sapmalar").

---

## Plandan Sapmalar

- **"Planlanan Dosya Listesi" `Exceptions/TraconStructuredResponseException.cs`
  dosyasını varsaymıştı — yanlıştı.** Bu repoda TÜM `TraconException` alt
  sınıfları tek dosyada (`src/Tracon.Abstractions/TraconException.cs`)
  yaşıyor, ayrı bir `Exceptions/` klasörü yok. Yeni istisna o dosyanın sonuna
  eklendi, mevcut konvansiyona uyularak.
- **§131.4'ün "Arayüz | Olay etiketi `en.ts` ve `tr.ts`'e girer" iddiası
  `RunEventType` etiketleri için yanlış ölçülmüş.** Kod okuması (Adım 1)
  gösterdi ki `run-detail.tsx`'teki `EVENT_STYLE` haritası (event türü →
  `{label, hue}`) ham bir TypeScript sabiti — `t(...)` üzerinden hiç geçmiyor,
  bu yüzden `ModelFallbackUsed`/`ContentBlocked` gibi mevcut etiketler de
  **her iki dilde aynı İngilizce dizgiyi** gösteriyor (bilinçli tasarım, teknik
  bir olay akışı etiketi). K-228'in "eksik anahtar derleme hatası" kuralı
  `RunErrorClass` için doğrudur (`dashboard.errorClass.*` anahtarları
  `en.ts`/`tr.ts`'te gerçekten var ve `Messages` tipiyle zorlanır) — o ikisi
  eklendi. `StructuredResponseRejected` etiketi `EVENT_STYLE`'a eklendi ama
  `en.ts`/`tr.ts`'e **eklenmedi**, çünkü hiçbir kardeş olay etiketi de orada değil.
- **Doğrulama kapsamı `IStructuredResponseValidator`'ı doğrulayan bir "kiracı"
  testi taşımıyor** (denetim 🟡 #1). Kardeş seam `IToolArgumentsValidator`/
  `ValidatingAIFunction`'ın da bu şekilde bir testi yok — bağlam kiracıyı hiç
  taşımıyor, doğrulayıcı `ITenantContext`'i (varsa) kendisi okur; bu, iki
  seam'in de paylaştığı bilinçli bir tasarım kısıtı, yeni bir gerileme değil.
  Emsal kararla tutarlı bırakıldı.
- **Akışta biriktirilen `List<AgentResponseUpdate>` için yeni bir üst sınır
  seçeneği EKLENMEDİ** (Riskler tablosunun istediği karar, denetim 🟡 #2).
  Gerekçe: bir turun toplam metni zaten `ModelBinding.MaxOutputTokens` ile
  sınırlıdır — bu decorator, `RunRecordingAgent`'ın akışsız yolda zaten
  bellekte tuttuğu `AgentResponse.Text` ile AYNI büyüklük mertebesini akışlı
  yolda bir kez daha tutuyor, yeni bir sınırsız yüzey açmıyor. Yapısal çıktı
  tanım gereği tek, sınırlı bir belgedir (bir liste veya akan bir transkript
  değil). Kanıt yetersiz görülürse ayrı bir aday (`MaxResponseLength` benzeri)
  açılabilir; bu faz bunu bilerek ertelemiştir.
- **`docs-site/ui.md` güncellenmedi** (site senkron denetiminin `arayuz`
  kuralı `--site-gerekce-yazildi` ile geçildi). Değişen tek ekran dosyası
  `run-detail.tsx`'teki `EVENT_STYLE` haritasına bir satır eklemekti — yeni
  bir ekran, yeni bir bileşen veya davranış değişikliği değil. `ui.md`'nin
  run detay ekranını anlatan cümlesi ("the full event stream in order:
  message deltas, tool calls with arguments and results, errors with their
  class") zaten doğru ve genel kalıyor; yeni olay türü bu cümleyle çelişmiyor.
- **Gerçek sağlayıcıyla "geçersiz yanıt" senaryosu canlı koşulamadı.**
  OpenAI'nin `response_format=json_object`/`JsonSchema` modu API sınırında
  **söz dizimsel olarak geçerli** JSON garanti eder — bu ortamda gerçek bir
  OpenAI anahtarı vardı ve `order-summary` demo agent'ı ile geçerli-yanıt kolu
  gerçek çağrıyla koşuldu, ama geçersiz-yanıt kolu teknik olarak üretilemedi.
  Bu, otomatik test paketinin (scriptlenebilir sahte sağlayıcı kullanan)
  neden hem gerekli hem yeterli olduğunun ölçülmüş kanıtıdır — bkz.
  `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md` §10'un başlığındaki not.

## Bu Fazda Verilen Kararlar

Yok. Bu fazda alınan kararların hiçbiri public API/compatibility contract,
güvenlik/kiracı sınırı, kalıcı veri/migration veya geri dönüşü pahalı bir
sistem kararı seviyesine çıkmadı — hepsi yerel implementation tercihi (yukarıdaki
"Plandan Sapmalar"da gerekçeleriyle yazılı). Açık Sorular 1-3 zaten kullanıcı
kararıyla plan aşamasında kapanmıştı.

## Denetim Bulguları

`faz-denetim` skill'i taze bağlamlı bir alt agent olarak koştu (2026-09-01).

| Seviye | Bulgu | Sonuç |
|---|---|---|
| 🔴 | Yok. | — |
| 🟡 #1 | Planın Hata Modları tablosunun öngördüğü kiracı testi yazılmadı. | **Gerekçelendirildi** — kardeş seam `IToolArgumentsValidator`'da da yok; "Plandan Sapmalar"a yazıldı |
| 🟡 #2 | Riskler tablosunun istediği "akış biriktirme üst sınırı" kararı belgelenmemişti. | **Gerekçelendirildi ve belgelendi** — `MaxOutputTokens` zaten sınırlıyor; "Plandan Sapmalar"a yazıldı |
| 🟢 | `StructuredResponseRejected`'in `Payload`'ının `RecordToolPayloads`'a bağımlı olduğu XML'de yazılı değildi. | **Düzeltildi** — `RunEventType.cs`'e tek cümle eklendi |

Denetim ayrıca 2229 (Core.UnitTests) + 725 (AspNetCore.FunctionalTests) testin
ve `kapi.py tarama`'nın (sync kopyası · `secret` · bayat doküman referansı)
yeşil olduğunu bağımsız olarak doğruladı.

## Sonraki Faza Devir Notu

- `IStructuredResponseValidator` artık `IToolArgumentsValidator`'ın **birebir
  ikizi**: aynı no-op varsayılan deseni (`TryAddSingleton` + paylaşılan
  `internal static readonly` örnek), aynı fail-closed kuralı, aynı "tüketici
  kendi `ITenantContext`'ini okur" tenant kuralı. Yeni bir "tüketici doğrulayıcı
  seam'i" eklenecekse bu ikiliyi şablon olarak kullan.
- Bounded repair (geçersiz yanıt için ikinci model çağrısı) bu fazın **bilerek
  kapsam dışı** bıraktığı bir sonraki adımdır (§"Kapsam dışı" tablosu). Açılırsa:
  `AgentRunBudget`'ın token/cost/duration sınırları, `FallbackChatClient`'ın tur
  içi tool defteri, cost attribution ve iptal ile kesişecek — ayrı bir tasarım
  turu gerekir, bu fazın decorator'ına küçük bir ek değil.
- `RunRecordingAgent`'ın akışlı yolunda `TraconRunContext.SetCurrent` HER
  `MoveNextAsync` öncesi yeniden yazılıyor (Faz 12'den beri) — bu sayede en
  içteki decorator (`StructuredResponseValidatingAgent`, Order 30) döngü
  bittikten SONRA çalışan kodunda bile doğru ambient scope'u görüyor. Yeni bir
  içteki decorator eklerken bu garantiye güvenebilirsin, ama ayrı bir konsol
  probuyla DOĞRULAMADAN varsayma (`docs/hafiza/cekirdek-calistirma.md`).
- `Tracon.Testing`'in `FakeModelProvider.EchoesUserMessage()` yanıtı
  **`"Echo: {mesaj}"` önekiyle döner**, ham mesajı değil — bu fazda geçerli
  JSON test etmek isteyen bir test bu yüzden `RespondsWith(sabitMetin)`
  kullanmak zorunda kaldı. Sonraki bir faz "echo" sağlayıcısıyla geçerli
  JSON/yapılandırılmış çıktı test etmeyi planlıyorsa bu öneki hesaba kat.
- Manuel kabul case'lerinin (MT-GUARD-090..096) geçersiz-yanıt kolu bu ortamda
  hâlâ elle koşulmadı (👤 gerekir değil, teknik olarak imkânsız — yukarıdaki
  "Plandan Sapmalar"). `manuel-test-kosumu` tam koşumu sırasında bu case'ler
  yalnızca otomatik karşılıklarıyla kanıtlanmaya devam edecek; bu bir eksiklik
  değil, kalıcı bir sınırdır.

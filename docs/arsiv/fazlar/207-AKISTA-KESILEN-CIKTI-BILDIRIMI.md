# Faz 207 — Akışta Kesilen Çıktı Bildirimi

> **Durum:** ✅ Tamamlandı (2026-10-14)
> **Plan onayı:** Bakımcı, 2026-10-14 — sözleşme kararı sohbette alındı (A: `done`'dan önce `tracon.output.truncated` custom çerçevesi, her zaman açık; chat/completions `finish_reason` düzeltmesi kapsamda) ve Faz 205 ile paralel başlatma onaylandı; plan metni sohbette okunmadı
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-427**
> **Genellik:** İlk ilke. Faz 204 kesilmeyi ayrı bir sonuç yaptı (K-919): kesik düz metin `Completed` biter ve `tracon.output.truncated` olayı yazar. Canlı akış bu olayı taşımaz. Akış okuyan her tüketici (C# ve TS istemcisi, konsol, kendi SSE okuyucusu) yarım yanıtı tam sanabilir. İş tüketici kodunda kalamaz: istemci kesilmeyi ancak `done`'dan sonra ikinci bir istekle (`GET /api/runs/{id}/events`) öğrenir. `/v1/chat/completions` ise ekosistem standardı bir alanı (`finish_reason`) her zaman `"stop"` ile doldurur; OpenAI SDK'sı kullanan tüketicinin başka kanalı yoktur. Kalem bir sözleşme kusurudur, bu yüzden § *Tüketici geri bildirimi kuralı*'ndan muaftır
> **Karşı görüş:** "İstemci `/events`'i okuyabilir. MAF `update` çerçevesinde `finishReason`'ı zaten taşıyor olabilir; yeni çerçeve gereksizdir." Cevap: `/events` ayrı bir sözleşme ve ikinci bir istektir; canlı istemci `done`'da durur. `update.finishReason` MAF'ın model çağrısı başına ham değeridir: tool turunda `tool_calls` olur, harness döngüsünde birden çok kez gelir, Tracon'un kararını (side-channel hariç tutma, `cause`) taşımaz ve MAF tipinin biçimine bağlıdır. Çerçeve, kararı tek yerde (`FinishReasonDetectingChatClient`) verir ve kayıtla bayt bayt aynıdır. Faz `update.finishReason`'ı bu yüzden yalnız belgeler (§207.1), sözleşme yapmaz
> **Önkoşul:** Kod: yok. Faz 205 ile örtüşme: **kodda dosya örtüşmesi yok** — 205'in dosyaları `AgentRunBudget.cs`, `QuotaEnforcer.cs`, `RunRecordingAgent.*.cs`, `LateToolCompletionRecorder.cs`, `NonTokenCostAccumulator.cs`, `WorkflowRunner.cs`, `QuotaStoreContract.cs`'tir; bu faz bunlara dokunmaz. **Semantik örtüşme** §205.4'tedir: çocuğun eşik bildirimi çocuğun kendi akışına taşınır. Bu fazın kota+kesilme sıra testleri 205'in semantiğine göre yazılır (yalnız kökün kendi eşiği kökün akışına gelir). **Doküman örtüşmesi** `concepts/governance.md` ve `capabilities.md`'dedir; bu iki sayfa kapanışta, 205 `main`'e girdikten sonra düzenlenir
> **Paketler:** `Tracon.AspNetCore` · `Tracon.Core` (yalnız bir yüklemin görünürlüğü ve XML remarks) · `Tracon.Abstractions` (yalnız XML remarks)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. Ölçüldü (2026-10-14, bu worktree): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır. Çerçeve yük record'ları `private`'tır (`AgentEndpoints.cs:1853-1860`, `OpenAIChatCompletionsEndpoints.cs:522-558`); yeni yardımcılar `internal` olur. Sözleşme değişikliği HTTP telindedir: yeni bir çerçeve oluşumu ve yeni `finish_reason` değerleri. Bir sapma public üye isterse GA'dan (K-603) önce hâlâ ucuzdur
> **Tüketici yüzeyi:** site: `http-api.md` (§ Streaming), `concepts/runs.md` (§ Two SSE contracts, § Truncated output), `guides/openai-api.md` (§ Compatibility matrix), `guides/model-providers.md` (sorun giderme satırı), `concepts/governance.md` (gerekirse tek cümle, 205'ten sonra) · sevk edilen: `CHANGELOG.md` (iki not), `capabilities.md` satırları (205'ten sonra), `RunEventCustomTypes.OutputTruncated` XML remarks (`api/` sayfası üretilir), Açık Soru 2-A seçilirse uç açıklamasından üretilen dört yapıt (§ Planlanan Public API)
> **Manuel test alanı:** `docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md` (MT-GUARD-111'in akışlı ikizi) · `docs/manuel-test/08-OPENAI-UYUMLU-UCLAR.md` (`finish_reason`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 28e2fa46:docs/arsiv/fazlar/207-AKISTA-KESILEN-CIKTI-BILDIRIMI.md
> ```
>
> Damıtıldı 2026-10-14 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bir kesik düz metin yanıtı, onu canlı okuyan istemciye kesik olduğunu söylemelidir. Bugün söylemez: kesilme yalnız run'ın kayıtlı olaylarında görünür. Bu faz kayıtlı `tracon.output.truncated` olayını ajan akışına, `done`'dan önce bir `custom` çerçevesi olarak aynalar. Kota bildiriminin aynasını kapalı bir listeye genelleştirir.

## Bitiş Ölçütleri (DoD)

- [x] §207.1 ölçüm testi yazıldı; sonuç ve seçilen yol bu dokümana yazıldı (alan var → kapı ve akış kolu ona dayanır)
- [x] Kesik düz metinde `custom` (`tracon.output.truncated`) çerçevesi `done`'dan önce gelir ve `/events` ile bayt bayt aynıdır (`OutputTruncationStreamingTests`, önce kırmızı)
- [x] Ara tool turu kesilmesi çerçeve üretmez (düz metin ikizi, mutasyonla kanıtlı); tool turundan sonra kesik düz metin tam bir çerçeve üretir
- [x] Kota açık + kesilme: iki çerçeve kayıt sırasıyla `done`'dan önce gelir; `QuotaRunNoticeTests`'in mevcut case'leri değişmeden yeşil
- [x] Normal run akış sırasında `ReadEventsAsync` çağırmaz (`The_stream_reads_the_run_events_only_after_an_update_reported_a_cut`: `stop` → 0, `length` → 1)
- [x] Ayna depo okuma hatasında `done` gelir ve uyarı loglanır (`QuotaRunNoticeTests`, önce kırmızı)
- [x] `/v1/chat/completions` iki kolda `"length"` / `"content_filter"` / `"stop"` döner (`OpenAICompatTests`, önce kırmızı); `[DONE]` son satırdır
- [x] `OutputTruncationStreamingTests.cs` yorumu ve `concepts/runs.md` ölçüm sonucuna göre düzeltildi
- [x] `CHANGELOG.md`'ye notlar girdi (`### Added` yeni çerçeve; `### Fixed` chat/completions `finish_reason` ve kota aynası depo hatası, ikisi **Behavior change**)
- [x] §207.6 kalemleri `ADAYLAR.md`'ye aday olarak girdi (**F-503**…**F-506**; `runs.md:160` kayması bu fazda düzeltildi, A2A/MCP aday değil — Sapma 7)
- [x] Dört doğrulama kapısı sıfır uyarı verir — § *Örnek Uygulama Koşumu* altındaki kapanış kapısı satırı
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1-4), çıktı § *Örnek Uygulama Koşumu*'nda
- [x] `secret` taraması boş döndü (`kapi.py tarama`)
- [x] Manuel kabul case'leri eklendi (`MT-GUARD-114`–`115`, `MT-COMPAT-052`–`053`), `00-INDEKS.md` sayımı güncellendi; dördü koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu yok, iki 🟡 düzeltildi
- [x] `docs-site/` güncellendi (altı sayfa, § *Tüketici Yüzeyi Envanteri*); `npm run check` temiz

### Doğrulama komutları

```bash
# Akış, araç turu, kota aynası ve chat/completions
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*OutputTruncationStreamingTests" "*OutputTruncationToolTurnTests" "*QuotaRunNoticeTests" "*OpenAICompatTests" "*SessionlessApprovalTests"

# Örnek uygulamada canlı akış (Idempotency-Key YOK)
curl -N -X POST http://localhost:5081/tracon/api/agents/manuel-kesilen-metin/run \
     -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
     -d '{"message":"How does a library work?"}'

# chat/completions
curl -s http://localhost:5081/tracon/v1/chat/completions -H "Authorization: Bearer $TOKEN" \
     -H 'Content-Type: application/json' \
     -d '{"model":"manuel-kesilen-metin","messages":[{"role":"user","content":"How does a library work?"}]}' \
     | jq '.choices[0].finish_reason'
```

---

## Plandan Sapmalar

1. **Çok iterasyonlu kesilme testi `HarnessLoopTests`'te değil, `OutputTruncationStreamingTests`'te.**
   `HarnessLoopTests`'in kurulumu `FakeModelProvider` alır; o sağlayıcı bitiş sebebi yazamaz.
   Test `FinishReasonModelProvider` + `Harness.Loop` ile yazıldı
   (`A_harness_loop_whose_iterations_are_cut_sends_one_notice_per_recorded_event`). Ölçüm: iki kesik
   iterasyon iki olay yazar ve akışa iki `custom` çerçevesi gelir; ara iterasyonun metni de istemciye akar.
2. **§207.4 akışsız kol ölçüldü.** Tracon dekoratörleri `AgentResponse.FinishReason`'ı korur: düz metin
   agent'ında (`ChatCompletions_reports_the_final_finish_reason`, akışsız satırlar) ve harness `Loop`
   agent'ında (denetim 🟡2). Harness ölçümü iki kolda yeşildir: kesik tek iterasyon `"length"`
   (`ChatCompletions_reports_length_for_a_cut_harness_loop_answer`); ilk iterasyonu kesik, son iterasyonu
   tam döngü `"stop"` (`ChatCompletions_reports_the_last_harness_iteration_not_an_earlier_cut_one`, iki model
   çağrısı). CHANGELOG cümlesi daraltılmadı; testler kilit olarak kalır.
3. **Açık Soru 2 uygulama oturumunda bekletildi, kapanışta A ile uygulandı.** Dört üretilmiş yapıt Faz 205
   ve 208 ile çakışacaktı; bakımcı kapanışta 2-A'yı seçti. Uç açıklaması (`AgentEndpoints.cs`) bir cümle
   kazandı; `docs/openapi/tracon.json`, `TraconApiClient.g.cs` ve `schema.ts` yenilendi (her biri tek
   satır), site kopyası derlemede üretilir. Aynı açıklamadaki `quota.When` boşluk hatası da düzeltildi.
   Açık Soru 1-A (sınır kabul, sitede yazılı) ve 4-A (`### Fixed`) uygulandı; Açık Soru 3 tetiklenmedi.
4. **Depo hatasında kısmi okuma gönderilmez.** Ayna önce olayları okur, sonra yazar. Okuma yarıda düşerse
   hiçbir bildirim gönderilmez, uyarı loglanır, `done` gelir. Böylece istemci kopması (yazma hatası) depo
   hatası sanılmaz.
5. **`RunEventCustomTypes.OutputTruncated` remarks'ı yükün `cause` alanını da anar.** Alan K-985'ten beri
   yüktedir; XML onu anmıyordu.
6. **Responses `incomplete` ölçüldü (§207.6 kod okumasıydı).** Kapanışta geçici bir fonksiyonel probla:
   `Length` ile kesik düz metin `/v1/responses`'ta akışsız `"status":"completed"`, akışta
   `response.completed` döner. Prob silindi; sınır sitede yazılı, iş **F-503**.
7. **§207.6'nın iki kalemi aday olmadı.** `concepts/runs.md:160` kayması yalnız ifadeydi ve bu fazda
   düzeltildi (workflow ve OpenAI akışlarının kendi çerçeve kümeleri ayrı cümleyle yazıldı). A2A
   (`ExternalAgentProxy`) ve MCP (`CatalogToolCallHandler`) akışsızdır; çağıran yalnız metni alır ve run'ın
   kendi olayları bildirimi taşır. Ölçülmüş bir tüketici ihtiyacı yok; aday açılmadı.

## Bu Fazda Verilen Kararlar

- **K-991** (kategori: public-api; kullanıcı kararı): ajan run akışı kayıtlı ayrılmış bildirimleri `done`'dan
  önce `custom` çerçevesi olarak verir; kapalı liste, `/events` ile bayt bayt aynı veri, kapılı depo okuması,
  okuma hatası uyarı + `done`; listeye tip eklemek sözleşme kararıdır. Açık Soru 1-A ve 2-A bu kararın parçasıdır.
- **K-992** (kategori: public-api; kullanıcı kararı): `/v1/chat/completions` `finish_reason` son model
  çağrısının sebebinden eşlenir (`length` · `content_filter` · `stop`), iki kolda, run kaydından bağımsız.
- Kategori dışı (yalnız burada): kesilme yüklemi `FinishReasonDetectingChatClient.IsTruncated` tektir ve
  `internal` olur (ikinci sebep sözlüğü yazılmaz) · bayrak "herhangi bir güncelleme"dir · kısmi okuma
  gönderilmez (Sapma 4) · Açık Soru 4-A (CHANGELOG başlığı).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan değişmedi; sapmalar yukarıda) |
| Düzeltme turu sayısı | 3 (uygulamada `ShippedDocumentationSelfContainmentTests` XML'deki faz/karar atıfları için bir kez kırmızı · denetimin iki 🟡'ı tek turda · kapanış kapısının ilk koşumu ortam kaynaklı `SourceLanguageTests` kırmızısı, F-507) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (mevcut hiçbir test değiştirilmeden kırmızı olmadı; yapılandırılmış ara tur testindeki etkisiz iddia kaldırıldı) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz bugün kapandı) |

## Denetim Bulguları

Bağımsız denetim (`faz-denetcisi`, taze bağlam, 2026-10-14): 🔴 yok, iki 🟡, 🟢 yok.

| Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|
| "Ara tool turu kesilmesi çerçeve üretmez" yalnız **yapılandırılmış** agent'la kanıtlanıyordu (`Streamed_intermediate_tool_turn_at_the_limit_does_not_fail_the_run`). Yapılandırılmış kesilme istisna atar, olay yazmaz; iddia hiç kırmızı olamazdı | 🟡 | gerçek (mutasyonla kanıtlandı) | Düzeltildi. Düz metin ikizi: `OutputTruncationToolTurnTests.Streamed_plain_text_run_whose_intermediate_tool_turn_hit_the_limit_sends_no_notice` (tool turu `Length`, son tur `Stop`; `custom` yok, `/events`'te olay yok, son çerçeve `done`). Mutasyon kanıtı: dedektör "son sebep" yerine "ilk kesilme sebebi kalır" yapıldı (`!IsTruncated(finishReason)` koşulu) → test kırmızı (bir `custom` çerçevesi geldi); mutasyon geri alındı. Yapılandırılmış testteki etkisiz iddia kaldırıldı |
| §207.4 harness agent'ının akışsız yolu ölçülmemişti; CHANGELOG `"length"`'i koşulsuz vaat ediyordu | 🟡 | gerçek (ölçüm eksiği; kod doğru çıktı) | Düzeltildi. Ölçüldü, iki kolda yeşil: `OpenAICompatTests.ChatCompletions_reports_length_for_a_cut_harness_loop_answer` ve `ChatCompletions_reports_the_last_harness_iteration_not_an_earlier_cut_one`. Kod ve CHANGELOG değişmedi (Plandan Sapmalar #2) |

## Örnek Uygulama Koşumu

2026-10-14, `samples/Tracon.Api` (bellek içi depo — paylaşılan veritabanına dokunmamak için bağlantı
dizeleri ortam değişkeniyle boşaltıldı), Anthropic `claude-haiku-4-5-20251001`, `maxOutputTokens: 50`:

- **Case 1 (`MT-GUARD-114`):** akış `run` · 29 `update` · `custom` · `done`; son üç `update`
  `"finishReason": "length"` taşır (Anthropic adaptörü sebebi usage güncellemesine de yazar); `custom` verisi
  `customType: tracon.output.truncated`, yük `{"provider":"anthropic","maxOutputTokens":50,"cause":"output_limit"}`;
  `/events`'teki `custom` verisiyle `cmp` aynı; run `Completed`, `error: null`.
- **Case 2 (`MT-GUARD-115`):** sınırsız agent, kısa soru: `run` · 8 `update` · `done`, `custom` yok.
- **Case 3 (`MT-COMPAT-052`):** akışsız `finish_reason = length` (usage 20/50/70); akışta yalnız son parça
  `"finish_reason":"length"` taşır, ardından `data: [DONE]`.
- **Case 4 (`MT-COMPAT-053`):** iki kolda `"stop"`.
- Ölçüm notu: `update` verisi girintili JSON'dur ve birden çok `data:` satırına yayılır; `custom` tek satırdır.
- **Kapanış kapısı:** `python3 scripts/kapi.py kapanis --taban 42e5479f` — sonucu aşağıdaki satır yazar.

## Tüketici Yüzeyi Envanteri

`tuketici-dokuman-senkronu` Adım 1 (taban `42e5479f`):

1. **`docs-site/` (elle):** `http-api.md` § Streaming (kapalı liste tablosu, depo hatası) · `concepts/runs.md`
   (iki sözleşme tablosu `custom` aldı; workflow ve OpenAI akışlarının kendi kümeleri ayrı cümle — §207.6
   kayması; § Truncated output: canlı `custom` çerçevesi, `update.finishReason`'ın garanti olmadığı) ·
   `guides/openai-api.md` (uyumluluk matrisi satırı + `finish_reason` paragrafı, Responses sınırı) ·
   `guides/model-providers.md` (sorun giderme satırı) · `concepts/governance.md` (kota bildirimi ile kesilme
   sırası, depo hatası; 205'in güncel metnine göre) · `capabilities.md` (Streaming runs satırı).
   **Üretilen:** `http-api/` ve `public/openapi/tracon.json` (uç açıklaması), `api/` (`RunEventCustomTypes`
   remarks), `public/llms-full.txt` (commit edilir).
2. **Sevk edilen metin:** `RunEventCustomTypes.OutputTruncated` XML (canlı akış, `cause`) ·
   `FinishReasonDetectingChatClient` remarks (internal) · `CHANGELOG.md` · paketlenen OpenAPI belgesi ve iki
   üretilmiş istemcinin uç açıklaması. Paket `README.md`'si değişmedi.
3. **Yerel referans / agent haritası:** yeni giriş noktası yok; `capabilities.md` satırı haritaya girmeyen
   tabloda (`build-agent-map.mjs --check`: "up to date and within budget").

Kapılar: (1) sevk edilen metin testleri kapanış kapısının tam test koşumunda · (2) `build-agent-map.mjs --check`
yeşil · (3) `npm run check` yeşil · (4) `dokuman-bakim.py --site-denetle --taban 42e5479f`: 2 kural tetiklendi,
ikisinin hedefi değişti (✅).

## Sonraki Faza Devir Notu

Sıradaki faz yok: seçilmemiş işler `ADAYLAR.md`'dedir. Faz 206 ve 208 paralel dallardadır.

**Devralınan sözleşmeler**

- Ajan run akışı: `run` · `update`… · (`approvals`) · `custom`… · `done`. `custom` yalnız `RunNoticeMirror`'ın
  kapalı listesindeki kayıtlı olaylardır (K-991); veri `/events` ile aynı serileştirmedir (Web varsayılanları).
- `/v1/chat/completions` `finish_reason` = `OpenAICompatSupport.ToChatFinishReason(son sebep)` (K-992).
- Kesilme yüklemi tektir: `FinishReasonDetectingChatClient.IsTruncated`. Yeni bir kesilme sebebi yalnız oraya girer.

**Bilinen tuzaklar**

- 🚨 Yeni ayrılmış bildirim canlı akışa kendiliğinden girmez: listeye **ve** depo okuma kapısına birlikte
  eklenir (kapı bugün yalnız kota seçeneğine ve kesilme sebebine bakar); uç açıklaması ve dört üretilmiş
  yapıt yenilenir.
- 🚨 Kapının öncülü MAF'ın `AgentResponseUpdate.FinishReason` kopyasıdır (MAF 1.20.0). Bir MAF yükseltmesi alanı
  düşürürse `The_last_update_frame_carries_the_model_finish_reason` kırmızı olur; o zaman kapı koşulsuz taramaya
  döner (plan §207.2.4, Açık Soru 3).
- 🚨 `done` yolunda atılan her istisna K-384'ün geniş `catch`'ine düşer ve tamamlanmış run'ı `error` ile bitirir;
  bu yola eklenen gözlemlenebilirlik işi kendi hatasını yakalar.
- "Bildirim yok" iddiası yalnız düz metin agent'ında anlamlıdır; yapılandırılmış kesilme istisna atar.

**Yarım kalan işler**

- **Site yayını (`faz-tamamlama` Adım 10) koşulmadı:** dal `main`'e birleşmedi; yayın birleşmeden sonra
  `./scripts/site-deploy.sh` ile yapılır.
- Adaylar: **F-503** (Responses `incomplete`), **F-504** (workflow SSE kesilmesi), **F-505** (içerikli
  `content_filter` izi), **F-506** (konsol `custom` rozeti), **F-507** (kapanışta ölçülen kapı kırılganlığı:
  `SourceLanguageTests` üretilen site `api/` sayfalarını tarar ve `faz-NNN` worktree'sinde yanlış kırmızı verir;
  `hafiza/site-icerik-yazimi.md`).

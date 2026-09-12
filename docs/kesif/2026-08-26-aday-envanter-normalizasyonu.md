# Aday Envanteri Normalizasyonu — 2026-08-26

> Bu bir **kapanış ve sınıflandırma kaydıdır**. Plan değildir. Yalnız
> [`ADAYLAR.md`](../ADAYLAR.md)'de olmayan kalemlerin neden orada olmadığını
> açıklar.

**Tetikleyen:** Açık görünen F-ID'lerin kusur, karar, ölçüm ve gerçek aday
olarak ayrıştırılması isteği.

**Zemin:** Faz 109 kapalı. F-164 aynı gün kusur kanalında kapandı. Eski
`ADAYLAR.md` içinde açık görünen 43 ID yeniden yargılandı.

**Ekosistem taraması:** Bu tur yeni ekosistem iddiası üretmedi. Sınıflandırma
yalnız repo kodu, kapanmış faz kayıtları ve manuel-test envanteriyle yapıldı.

---

## 1. Ölçülen zemin

| Kaynak | Ölçülen bulgu |
|---|---|
| `rg --files \| rg -i 'benchmark\|bench'` | Benchmark projesi yok; F-67'nin sorunu ayakta. |
| `find docs/manuel-test -maxdepth 1 -name '*.md'` | 37 aile dosyası var. |
| `rg -o '^###? MT-[A-Z0-9-]+' docs/manuel-test` | 1.650 manuel case kimliği var. |
| `AgentDefinitionCompiler.cs:465` | Replay transform'u yalnız `AIFunction` için uygulanıyor; client tool declaration-only kalıyor. |
| `FallbackChatClient.cs:350` ve `DefaultRunErrorClassifier.cs:132` | Failure sınıflandırması iki yerde type-name/message regex'iyle yapılıyor. |
| `OnlineEvalJobHandler.cs:68` ve `IRunScoreStore.cs:12` | Retryable failure ile score upsert mevcut; per-judge durable ownership yok. |
| Faz 103 devir notu | F-154–F-163 preview.1 blocker değildir; çoğu gerçek tüketici veya ölçek ölçümü bekliyor. |

## 2. Normalizasyon sonucu

| Kanal | Sayı | ID'ler | Varış yeri |
|---|---:|---|---|
| Yeni yetenek adayı | 5 | F-67, F-109, F-149, F-152, F-165 | [`ADAYLAR.md`](../ADAYLAR.md) |
| Açık kusur | 0 | — | Bu turda açık kayıt kalmadı. |
| Kapatıldı / yeniden doğrulandı | 5 | F-106, F-130, F-137, F-138, F-139 | Aşağıdaki kapanış kanıtı |
| Karar / uyumluluk eşiği | 6 | F-72, F-90, F-91, F-92, F-95, F-132 | Kullanıcı kararı veya dış değişim bekler |
| Ölçüm bekliyor | 15 | F-51, F-94, F-96, F-97, F-99, F-101, F-123, F-128, F-154, F-156, F-157, F-159, F-160, F-161, F-162 | Aşağıdaki ölçüm tablosu |
| Arşivlendi / birleştirildi | 12 | F-48, F-88, F-89, F-98, F-144, F-145, F-146, F-147, F-148, F-155, F-158, F-163 | Aşağıdaki arşiv tablosu |

Toplam: **43**. F-164 bu sayımdan önce kapatıldığı için bu envantere girmez.

## 3. Kanal 2 — Kapatılan kusur kayıtları

| ID | Kapanış kanıtı | Sonuç |
|---|---|---|
| F-106 | `WorkflowRunner` 2026-08-18'den beri yanıt sonrası `GetStatusAsync()` ile terminal MAF run'ını yeniden açmıyor (K-433, `dc2cbd2`). | Önceki kod düzeltmesi bulunmadan açık görünüyordu; yeni kod işi yok. Gerçek modelli manuel koşum ancak yeniden açma eşiğidir. |
| F-130 | İlk `GET /cases` pending iken `Add case` yeni satırı, geç gelen boş yanıtla siliyordu. Yeni frontend testi önce kırmızıydı; UI artık ilk yükleme bitene kadar `Add case`/`Save cases`i kapatır. | Kapatıldı. Targeted E2E geçti. |
| F-137 | Token submit artık `Dashboard` beklemesinden önce authenticated `GET /api/agents` `200` yanıtını bekliyor. Eski çalışma-yolu belirtisi bu kopyada yeniden oluşmadı. | Kapatıldı. Targeted E2E geçti. Yeniden görülürse ortam kaydıyla ayrı kusur açılır. |
| F-138 | İki bağımsız `WorkflowOutput` assertion'ı (`WorkflowAgentEntryRespondTests`, `WorkflowHumanInTheLoopTests`) 10 tekrarın tamamında geçti; tam workflow unit seti 102/102 geçti. | Kapatıldı; güncel ürün kusuru yok. |
| F-139 | `CreateAgentWithTwoVersionsAsync`, provider catalog gelmeden `Create`e basıyordu. Test artık provider değerini ve enabled `Save` düğmesini bekler. İlk tam UI koşumunda bu eksik doğrudan yeniden üretildi. | Kapatıldı. Targeted E2E geçti. |

Kapanışlar kusuru aday kuyruğuna taşımaz. Aynı belirti yeniden görünürse yeni
bir kusur kaydı açılır ve `kusur-giderme` tekrar koşar.

## 4. Kanal 3 — Karar ve uyumluluk eşikleri

| ID | Neyi değiştirir | Planlanmama nedeni |
|---|---|---|
| F-72 | ACS paketinin beta/native RID bağımlılığı | Paket GA ve taşınabilir olmadan ürün uyumluluk kararı erken. |
| F-90 | SQL RLS ile derin savunma | Sağlayıcılar arası güvenlik sözleşmesi değişir; SQLite karşılığı yok. |
| F-91 | MCP OAuth token'ını kalıcı paylaşma | K-059: `secret` veritabanına yazılmaz. |
| F-92 | Dağıtık hız sınırı | K-158 ve reddedilmiş Redis yaklaşımıyla çakışır. |
| F-95 | Agent düzeyinde checkpoint hook'u | MAF kancası yok; Tracon'in paralel katmanı K3'ü zorlar. |
| F-132 | `RequireHttps` varsayılanı | Mevcut `http` kurulumlarını kırmayan varsayılan kullanıcı/uyumluluk kararı ister. |

## 5. Ölçüm bekleyenler

| ID | Aday olmak için eksik kanıt |
|---|---|
| F-51 | Gerçek Aspire tüketici talebi ve yeni paket/graph maliyeti. |
| F-94 | Çok turlu eval talebi ile `EvalCase` compatibility maliyeti. |
| F-96 | Kuyruklu attachment için URL ownership ve consumer talebi. |
| F-97 | OpenAI `background: true` sözleşmesinin güncel sağlayıcı uyumu ve talep. |
| F-99 | SQL Server `VECTOR` ve SQLite `sqlite-vec` capability/performance ölçümü. |
| F-101 | RAG source değişim modeli ve re-index consumer ihtiyacı. |
| F-123 | Culture'ın eval/replay/child run'a taşınması için persistence ve demand. |
| F-128 | API reference navigation taban çizgisi ve dönüşümün gerçek maliyeti. |
| F-154 | Beş extension seam'inde isim kuralı farkı ve breaking-change matrisi. |
| F-156 | Instance/factory/generic registration için gerçek `ServiceProvider` dispose probu. |
| F-157 | Compiler/cache facade gerektiren bağımsız tüketici senaryosu. |
| F-159 | Tenant-aware cache büyümesi, contention ve throughput eşiği. |
| F-160 | NUnit/MSTest tüketici talebi ve framework-neutral fixture maliyeti. |
| F-161 | Mevcut `Use*` kayıtlarının gerçek ergonomi problemi. |
| F-162 | Agent catalog büyüklüğü, source sayısı ve HTTP pagination eşiği. |

## 6. Arşivlenen veya birleştirilenler

| ID | Sonuç | Gerekçe |
|---|---|---|
| F-48 | Elendi | İçe/dışa aktarım ucu yok; Faz 83 kapsamına bilinçli alınmadı. |
| F-88, F-89, F-98 | Düşürüldü | Guardrail devam kümesi 2026-08-21'de bilinçli elendi. |
| F-144, F-145 | Faz dışı test bakımı | Davranış kusuru değil; ilgili yüzeye dokunan fazın normal regression sorumluluğu. |
| F-146 | Arşiv politikası korunur | Sabit sürüm imzaları git geçmişi ve tam metin denetimiyle geri getirilebilir. |
| F-147 | Yerel refactor | Üç dialektteki doğru SQL metnini değiştirmeyen dar tutarlılık işi. |
| F-148 | Öncelik dışı | In-memory ve öğretim store'unda O(n) tarama için tüketici etkisi ölçülmedi. |
| F-155 | F-149 ile birleşti | Provider failure seam'i ölçülmeden genel exception taxonomy ayrı tasarlanmaz. |
| F-158 | Elendi | Genel internalization/refactor için package-boundary tüketici kanıtı yok. |
| F-163 | F-67 ile birleşti | Load/performance contract'ı aynı benchmark ve CI-gürültü kararını taşır. |

## 7. Kapanış kontrolü

- `ADAYLAR.md` yalnız beş planlanabilir aday taşır.
- Her adayda ölçülmüş repo kanıtı ve gerçek karşı görüş vardır.
- Kusur ve karar kalemleri aday kuyruğundan çıkarıldı.
- Ölçüm bekleyen her ID için adaylaşma eşiği yazıldı.
- Kapanmış veya kopya kalemler bu kayıtta damıtıldı; sıcak aday dosyasından silindi.

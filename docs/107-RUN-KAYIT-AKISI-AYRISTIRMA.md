# Faz 107 — Run Kayıt Akışı Ayrıştırma

> **Durum:** 📋 Planlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 106](arsiv/fazlar/106-AGENT-DERLEYICI-AYRISTIRMA.md) — teknik zorunluluk yoktur; yapısal tur compiler'dan runtime wrapper'a ilerler
> **Paketler:** `AgentPrism.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `RunRecordingAgent` constructor ve davranışı değişmez; `PublicAPI.Shipped.txt` girdisi bugün **0**
> **Tüketici yüzeyi:** Yok. Public imza, XML metni ve observable contract değişmez
> **Manuel test alanı:** [`manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md) · [`manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`](manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız ilgili satırlar:
   ```bash
   grep -n "K-154\|K-157\|K-243\|K-421" docs/KARARLAR.md
   ```
3. Alan hafızası: [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md), [`hafiza/olcum-kota-ve-secenekler.md`](hafiza/olcum-kota-ve-secenekler.md) ve [`hafiza/test-altyapisi.md`](hafiza/test-altyapisi.md)
4. Mimari: [`MIMARI.md`](MIMARI.md) — yalnız run sequence ve `RunRecordingAgent` notları

---

## Amaç

`RunRecordingAgent`, iki yürütme gövdesini, ambient scope/span yaşamını, store yazımını, event sink yayınını, maliyeti, kotayı, online eval sampling'i ve hata dönüşümünü 1.331 satırda toplar. Faz bu sorumlulukları `partial` dosyalara ayırır. Span ve `AsyncLocal` yazım yerlerini değiştirmez.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`RunRecordingAgent.cs:31`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs) | Public wrapper **1.331 satırdır** ve 22 davranış metodu taşır. |
| [`RunRecordingAgent.cs:188`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs) | Akışsız giriş gövdesi scope, span, cancellation ve completion sırasını aynı yerde kurar. |
| [`RunRecordingAgent.cs:297`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs) | Streaming gövde her `MoveNextAsync` öncesi ambient scope'u yeniden yazar; bu yapısal zorunluluktur. |
| [`RunRecordingAgent.cs:945`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs) | Completion tek gövdede store, usage, cost, metrics, quota, webhook ve eval işlerini birleştirir. |

> Kanıtlar 2026-08-26 tarihinde doğrulandı.

## 107.1 — Entry-point gövdeleri yerinde kalır

`RunRecordingAgent` `public sealed partial class` olur. Constructor, `RunCoreAsync` ve `RunCoreStreamingAsync` ana dosyada kalır.

🚨 `Activity` ve `AgentPrismRunContext` başlangıcı async helper'a çıkarılmaz. Streaming döngüsündeki ambient yazım her `MoveNextAsync` öncesinde kalır. `AmbientWriteSiteTests` taban çizgisi yalnız dosya taşıması nedeniyle güncellenir; yazım yeri sayısı artmaz.

> **Faz 106'dan devir:** aynı `partial` ayrıştırma deseni orada denendi ve
> işe yaradı — önce hiç `using`/pragma eklemeden metotları hedef dosyaya taşı,
> sonra `dotnet build` çalıştır. `TreatWarningsAsErrors=true` eksik `using`'i
> `CS0246`, gereksiz pragma'yı `IDE0079` olarak geri verir; ikisi de tahmin
> etmekten daha hızlı ve kesin. `RunRecordingAgent.cs` bugün hiç `MAAI001`
> taşımıyor (kontrol edildi), o yüzden bu fazda pragma sınırı sorunu
> çıkması beklenmez — ama teknik yine de geçerlidir.

## 107.2 — Yaşam döngüsü ve kalıcılık

Private gövdeler şu dosyalara ayrılır:

- run hazırlama, scope üretimi ve cancellation kaydı;
- başlangıç, input, attachment ve tool event yazımı;
- completion, usage/cost birleştirme ve error mapping;
- sink, quota, webhook, metrics ve online evaluation bildirimleri.

Gözlemlenebilirlik store veya sink hatasında run'ı bozamaz. Kök/child ayrımı ve `scope.Depth == 0` koşulları aynı yerde kalır.

## 107.3 — İki yolun simetri kapısı

Akışlı ve akışsız yollar için ortak outcome matrisi eklenir: completed, failed, canceled ve awaiting approval. Her hücre status, event sonu, cancellation cleanup ve span kapanışını doğrular. Bu matris, helper taşımasının yalnız bir yolu değiştirmesini yakalar.

## Planlanan Public API

Yeni public üye yoktur. Constructor parametreleri ve sırası değişmez.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

## Planlanan Dosya Listesi

```text
src/AgentPrism.Core/Recording/
├── RunRecordingAgent.cs
├── RunRecordingAgent.Lifecycle.cs
├── RunRecordingAgent.Persistence.cs
├── RunRecordingAgent.Completion.cs
└── RunRecordingAgent.Notifications.cs
tests/AgentPrism.Core.UnitTests/Recording/
└── RunRecordingAgentOutcomeMatrixTests.cs
```

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Streaming scope yalnız ilk turda yazılır ve iç çağrıda kaybolur | Fonksiyonel | streaming ambient/span regresyon testleri + `AmbientWriteSiteTests` |
| Akışlı ve akışsız yol farklı status/event üretir | Birim / fonksiyonel | `RunRecordingAgentOutcomeMatrixTests` |
| İptal linked CTS yerine gelen token'a bağlanır | Birim | dış iptal ve caller iptali senaryoları |
| Store veya sink hatası gerçek run'ı keser | Fonksiyonel | store/sink failure testleri |
| Child run kota veya webhook'u ikinci kez sayar | Birim | root/child completion testleri |
| Başka kiracının context'i run kaydına sızar | Fonksiyonel | tenant context run testleri |
| Boş usage veya bilinmeyen content tipi completion'ı kırar | Birim | null/unknown content senaryoları |

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Sample + sahte provider | Akışlı ve akışsız aynı prompt'u çalıştır | İki run `Completed`; event sonu ve usage tutarlıdır |
| 2 | Devam eden run | Cancel endpoint'ini çağır | Run `Canceled`; cancellation registry temizlenir |
| 3 | OTel listener | Gerçek run yap | İç model/tool span'leri `agentprism.run` span'inin çocuğudur |

## Açık Sorular

Yok. Yeni lifecycle abstraction kapsam dışıdır.

## Bitiş Ölçütleri (DoD)

- [ ] Entry-point gövdeleri span/scope yazımını kendi gövdelerinde tutar
- [ ] `AmbientWriteSiteTests` yazım yeri sayısının artmadığını kanıtlar
- [ ] Outcome matrisi streaming ve non-streaming dört sonucu kapsar
- [ ] Store/sink failure testleri run davranışının bozulmadığını gösterir
- [ ] `git diff -- 'src/*/PublicAPI.*.txt'` boş döner
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri ilgili ailelere eklendi ve otomatik olanlar koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

## Riskler

| Risk | Önlem |
|---|---|
| Görünüşte mekanik taşıma ambient akışı bozar | Entry-point gövdeleri taşınmaz; APG0501 ve ambient write kapısı koşar |
| Completion sırası değişir | Outcome matrisi ve store/sink failure testleri sıra etkisini ölçer |
| Constructor collaborator nesnesine sarılıp public davranış değişir | Constructor imzası aynen kalır; yeni public collaborator eklenmez |

---

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.

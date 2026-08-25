# Faz 106 — Agent Derleyici Ayrıştırma

> **Durum:** 📋 Planlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 17**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 105](105-DI-BILESEN-KOKU-AYRISTIRMA.md) — teknik zorunluluk yoktur; yapısal tur sırası composition root'tan compiler'a ilerler
> **Paketler:** `AgentPrism.Core`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor. `AgentDefinitionCompiler` imzaları ve davranışı değişmez; `PublicAPI.Shipped.txt` girdisi bugün **0**
> **Tüketici yüzeyi:** Yok. Public imza ve XML metni değişmez; üretilen API reference aynı kalır
> **Manuel test alanı:** [`manuel-test/02-CEKIRDEK-VE-KATALOG.md`](manuel-test/02-CEKIRDEK-VE-KATALOG.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız ilgili satırlar:
   ```bash
   grep -n "K-320\|K-421\|K-581" docs/KARARLAR.md
   ```
3. Alan hafızası: [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) ve [`hafiza/maf-api.md`](hafiza/maf-api.md)
4. Mimari: [`MIMARI.md`](MIMARI.md) — yalnız `AgentDefinitionCompiler.Compile` akışı ve decorator pipeline

---

## Amaç

`AgentDefinitionCompiler`, public compile overload'larını, dependency resolution'ı, chat options üretimini, compaction'ı, memory provider'larını, tool çözümlemeyi ve harness kurulumunu tek 1.617 satırlık sınıfta taşır. Faz sınıfı yeni abstraction ile sarmalamaz. Aynı sınıfı sorumluluk odaklı `partial` dosyalara böler.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentDefinitionCompiler.cs:35`](../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs) | Public compiler sınıfı **1.617 satırdır** ve 39 metot taşır. |
| [`AgentDefinitionCompiler.cs:398`](../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs) | Async dependency resolution ve cache fingerprint aynı dosyadadır. |
| [`AgentDefinitionCompiler.cs:924`](../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs) | Compaction, memory, text/vector search ve file-store kurulumları compiler orchestration ile iç içedir. |
| [`AgentDefinitionCompiler.cs:1407`](../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs) | Harness compilation ve experimental API bastırması aynı büyük gövdede yaşar. |

> Kanıtlar 2026-08-26 tarihinde doğrulandı.

## 106.1 — Facade aynı kalır

`AgentDefinitionCompiler` `public sealed partial class` olur. Constructor ve public `Compile*` overload'ları ana dosyada kalır. Bu dosya yalnız girdi doğrulama, sync/async yol seçimi, cache kararı ve `BuildAgent` yönlendirmesini taşır.

Yeni public interface, compiler wrapper veya paralel definition tipi oluşturulmaz. K3 korunur; MAF tipleri doğrudan kullanılır.

## 106.2 — Sorumluluk dosyaları

Private/internal gövdeler şu eksenlere ayrılır:

- dependency ve shared-instruction/callable-agent çözümleme;
- tool, `ChatOptions`, response format ve model capability kontrolü;
- compaction ve memory provider üretimi;
- chat agent, child agent ve harness üretimi;
- skill source üretimi.

Bu faz yalnız taşıma ve isim netleştirme yapar. K-320 pipeline sırasını, K-581 decorator uygulamasını, cache fingerprint içeriğini veya sync/async davranışını değiştirmez.

## 106.3 — Davranış matrisi

Mevcut testler bir compile-path matrisi altında okunur. Eksik kalan hücreler eklenir:

| Yol | Normal | Shared instructions | BYOK/cache bypass | Culture | Callable agents |
|---|---|---|---|---|---|
| sync `Compile` | mevcut | açık hata | uygulanmaz | null | mevcut |
| async `CompileAsync` | mevcut | çözülür | çözülür | çözülür | çözülür |
| `CompileCachedAsync` | mevcut | fingerprint'e girer | cache bypass | cache key'e girer | fingerprint'e girer |
| parameterized | mevcut | async yol ile aynı | aynı | aynı | aynı |

Matris yeni ürün davranışı eklemez. Refactor sırasında gövde kaymasını yakalar.

## Planlanan Public API

Yeni public üye yoktur. `partial` metadata yüzeyini değiştirmez.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

## Planlanan Dosya Listesi

```text
src/AgentPrism.Core/Compilation/
├── AgentDefinitionCompiler.cs
├── AgentDefinitionCompiler.Dependencies.cs
├── AgentDefinitionCompiler.ChatOptions.cs
├── AgentDefinitionCompiler.Compaction.cs
├── AgentDefinitionCompiler.Memory.cs
├── AgentDefinitionCompiler.Agents.cs
└── AgentDefinitionCompiler.Skills.cs
tests/AgentPrism.Core.UnitTests/Compilation/
└── AgentDefinitionCompilerPathTests.cs
```

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Sync yol async store gerektiren tanımı sessizce eksik derler | Birim | shared-instruction sync/async testleri |
| Cache fingerprint bir dependency'yi düşürür | Birim | `AgentDefinitionCompilerPathTests` + cache testleri |
| Culture veya callable-agent çözümü çağrı zincirinde kaybolur | Birim / fonksiyonel | compiler path matrisi + katalog çözümleme testleri |
| Tool/decorator sırası değişir | Fonksiyonel | gerçek `run` ve decorator pipeline testleri |
| İptal async store çözümlemesine ulaşmaz | Birim | iptal edilmiş token ile dependency resolution |
| Başka kiracının provider veya dosya belleği çözülür | Fonksiyonel | tenant credential ve file-memory izolasyon testleri |
| Opsiyonel memory/embedding alt sistemi yokken hata mesajı bozulur | Birim | mevcut compilation exception testleri |

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Varsayılan sample | `support` agent için gerçek `run` yap | Yanıt ve run kaydı refactor öncesiyle aynıdır |
| 2 | Culture taşıyan definition | `tr-TR` ile compile/run yap | En yakın culture talimatı çözülür |
| 3 | Shared instructions kullanan definition | Async compile ve sync compile yollarını çağır | Async yol çözer; sync yol mevcut açık hatayı verir |

## Açık Sorular

Yok. Yeni collaborator abstraction eklemek kapsam dışıdır.

## Bitiş Ölçütleri (DoD)

- [ ] Ana compiler dosyası public orchestration sınırında kalır; compaction, memory, skills ve agent üretimi ayrı sorumluluk dosyalarındadır
- [ ] Public compile overload'ları ve XML dokümanları birebir kalır
- [ ] Compile-path matrisi sync, async, cached ve parameterized yolları kapsar
- [ ] `git diff -- 'src/*/PublicAPI.*.txt'` boş döner
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri ilgili aileye eklendi ve otomatik olanlar koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

## Riskler

| Risk | Önlem |
|---|---|
| İmza aynı kalır, çağırılan gövde yanlış helper'a gider | Compile-path matrisi her public yolun sonucunu ölçer |
| Private helper'ı yeni service'e çevirmek DI/public yüzeyi büyütür | Yalnız `partial` ayrıştırma yapılır; yeni interface eklenmez |
| Experimental MAF bastırması yanlış dosyaya taşınır | Bastırma en dar kapsamda harness dosyasında kalır; build sıfır warning verir |

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

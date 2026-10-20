# Faz 227 — Eval Vakasında Beklenen Tool Çağrısı ve Argümanı

> **Durum:** 📋 Planlandı (2026-10-20)
> **Kademe:** tam — migration (`eval_cases` yeni sütun, üç sağlayıcı) · public API'den silinen satır (`EvalCase.ExpectedTools` → `ExpectedToolCalls`) · kalıcı veri
> **Plan onayı:** farukatasoy, 2026-10-20 (seçim: `ExpectedToolCalls` ile değiştir)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-605** (yalnız dilim a; çok turlu vaka ve workflow suite'i adayda kalır)
> **Genellik:** Eval kullanan her ekip (ölçme–iyileştirme). Bugün bir vaka "doğru tool doğru argümanla çağrıldı mı" sorusunu soramaz; `ExpectedTools` alanı vardır ama hiçbir kontrol onu okumaz. İş tüketici kodunda kalabilir (özel `IEvalEvaluatorFactory`), ama vaka verisi ve kontrol türü kontrol düzleminindir: konsol, terfi ve sürüm vakayı tanır.
> **Karşı görüş:** "Argüman eşleşmesi kırılgandır; model aynı anlamı farklı yazar." Cevap: eşleşme alt kümedir (beklenen anahtarlar eşleşir, fazlası serbest) ve JSON değeri anlamıyla karşılaştırılır (sayı biçimi, anahtar sırası, boşluk fark etmez). Argümansız beklenti yalnız çağrıyı denetler.
> **Önkoşul:** Yok
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.Testing.Contracts.Xunit`, `.Client`, `.UI`
> **Yeni paket:** Yok · **Migration:** gerekli (üç sağlayıcı) — numara uygulama anında alınır (K-178)
> **Public API:** değişiyor — `1.0.0` GA'dan (K-603) önce ucuz; `Shipped` boş
> **Tüketici yüzeyi:** site: `concepts/evaluation.md` (`expectedTools` paragrafı ve kontrol tablosu) · sevk edilen: `EvalCase` XML doc, `Tracon.Testing.Contracts.Xunit` (`EvalStoreContract`), OpenAPI açıklaması
> **Manuel test alanı:** `docs/manuel-test/17-EVAL-VE-DENEYLER.md` — `🤖`

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız grep:
   ```bash
   grep -n "K-178 —\|K-727 —\|K-1027 —\|K-1031 —" docs/KARARLAR.md
   ```
   **K-178** (migration numarası sağlayıcı başına), **K-727** (Quality kataloğu sevk edilmez), **K-1027** (bilinmeyen istek alanı `400` deseni), **K-1031** (yüke sonradan eklenen liste)
3. Alan hafızası: [`hafiza/sql-migration.md`](hafiza/sql-migration.md), [`hafiza/postgresql.md`](hafiza/postgresql.md) (`jsonb`), [`hafiza/sqlite.md`](hafiza/sqlite.md), [`hafiza/sql-server-tuzaklari.md`](hafiza/sql-server-tuzaklari.md); eval yeri için [`hafiza/altyapi-haritasi.md`](hafiza/altyapi-haritasi.md) yalnız Eval bölümü
4. Emsal migration: `src/Tracon.PostgreSql/Migrations/0036_eval_case_parameters.sql` (Faz 86, `parameters jsonb`)

---

## Amaç

Vaka beklenen tool çağrılarını adı ve isteğe bağlı argümanıyla taşır. Yeni
`toolCallArgsMatch` kontrolü bunu denetler. Eski `ExpectedTools` (yalnız ad, okunmaz)
kalkar; eski satırlar okunurken ada çevrilir.

- **F-605 (a)** — `EvalCase.ExpectedToolCalls`, `toolCallArgsMatch` kontrolü, depo, HTTP, konsol.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`EvalCase.cs:24-33`](../src/Tracon.Abstractions/Evaluation/EvalCase.cs) | `ExpectedTools` yalnız ad taşır ve belgesi "**Not read by any check**" der |
| [`EvaluationContracts.cs:51-52`](../src/Tracon.AspNetCore/Contracts/EvaluationContracts.cs) | OpenAPI açıklaması "Tool names looked for by the `toolCalled` check" der: **yanlış** (alanı hiçbir kontrol okumaz) |
| [`SqlEvalStore.cs:144,212,448`](../src/Tracon.Sql.Shared/Stores/SqlEvalStore.cs) | Sütun virgülle ayrılmış metin (`0009_eval.sql:21`) |
| [`EvalJobHandler.cs:551`](../src/Tracon.Core/Evaluation/EvalJobHandler.cs) | `EvalItem` `ExpectedToolCalls` almaz |
| [`EvalCheckRegistry.cs:29-34`](../src/Tracon.Core/Evaluation/EvalCheckRegistry.cs) | Altı tür var; argüman denetleyen yok |
| [`RunToCasePromoter.cs:124-141`](../src/Tracon.Core/Evaluation/RunToCasePromoter.cs) | Referans run terfisi çağrılan tool **adlarını** yazar |
| MAF `EvalChecks.ToolCallArgsMatch` (decompile + konsol probu, 2026-10-20) | Çağrılan değeri açar: metin → `string`, sayı → `double`, bool → `bool`, nesne/dizi → **ham JSON metni**; karşılaştırma `object.Equals`. Probe: beklenen `"ORD-7"` (string) geçer; beklenen `2` (`int`) ve beklenen `JsonElement` **geçmez**. Nesne argümanı anahtar sırası ve boşluğa bağlıdır |

> Kanıtlar 2026-10-20 tarihinde doğrulandı.

---

## 227.1 — Veri

```csharp
public sealed record EvalExpectedToolCall
{
    public required string Name { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Arguments { get; init; } // null = yalnız çağrı
}
// EvalCase, EvalCaseDraft: ExpectedTools kalkar → IReadOnlyList<EvalExpectedToolCall> ExpectedToolCalls
```

Neden MAF'ın `ExpectedToolCall`'u değil: onun argümanı `IReadOnlyDictionary<string, object>`;
saklanan ve AOT ile serileştirilen vaka `JsonElement` ister (İ1: kullan → türet →
sarmala → yaz; burada tip kalıcı veri sınırını taşımaz). `EvalItem` kurulurken MAF
tipine çevrilir (227.3).

## 227.2 — Depo ve eski satır

- Yeni sütun `expected_tool_calls`: PostgreSQL `jsonb`, SQL Server `nvarchar(max)`,
  SQLite `text` (emsal `parameters`, Faz 86).
- Okuma: yeni sütun doluysa onu okur; boşsa eski `expected_tools` metnini ada çevirir
  (argümansız beklenti). Veri göçü yok; eski sütun yazılmaz, silinmez.
- `EvalStoreContract` gidiş-dönüşü argümanla ve eski satırla sınar (dört koşum).

## 227.3 — Kontrol

- Yeni tür `toolCallArgsMatch` (parametresiz; vakadan okur). Tracon uygular: MAF
  karşılaştırması JSON değerini anlamıyla karşılaştırmaz (kanıt tablosu).
- Anlam (MAF ile aynı): her beklenti için aynı adlı (büyük/küçük harf duyarsız) çağrı
  aranır; `Arguments` null ise çağrı yeter; değilse beklenen her anahtar çağrıda
  bulunur ve değeri **JSON anlamıyla** eşittir (sayı biçimi, nesne anahtar sırası,
  boşluk fark etmez); fazla anahtar serbesttir. Sonuç adı `tool_call_args_match`,
  sebep metni MAF'ınkiyle aynı biçimde.
- `EvalItem.ExpectedToolCalls` MAF tipiyle de doldurulur (değerler MAF'ın açtığı
  biçimde: metin, `double`, `bool`, ham metin). Böylece tüketicinin kendi MAF
  değerlendiricisi de beklentiyi görür.
- Karşılaştırma `net8.0`'da da çalışır (`JsonNode.DeepEquals` ya da kendi yürüyüş;
  Faz 193 `net8.0`'ı düşürene kadar). AOT: yansıma yok.
- Upstream'e bildirim: MAF `ToolCallArgsMatch` tip duyarlılığı (issue faz içinde açılır, bağlantı kapanışta).

## 227.4 — HTTP, terfi, konsol

- Vaka girdisi `expectedToolCalls` alır. Eski `expectedTools` anahtarı `400` alır ve
  yeni alanı adlandırır (K-1027 deseni); yanlış OpenAPI açıklaması düzelir.
- Terfi argümansız beklenti yazar (bugünkü ad listesiyle aynı anlam; Açık Soru 1).
- Konsol `eval-detail.tsx` vaka düzenleyicisi: tool adı + JSON argüman alanı (geçersiz
  JSON kayıtta reddedilir).

---

## Planlanan Public API

```csharp
// Tracon.Abstractions
public sealed record EvalExpectedToolCall { required string Name; IReadOnlyDictionary<string, JsonElement>? Arguments; }
// EvalCase, EvalCaseDraft: - ExpectedTools  + ExpectedToolCalls
```

"Bugün değiştirmek bedava / sonradan kırıcı": `EvalCase` `sealed record`'dur; GA'dan
sonra alan kaldırmak kırıcıdır.

### HTTP `endpoint`'leri

Yeni uç yok. Vaka girdisi ve vaka yanıtı değişir (OpenAPI, `Tracon.Client`, `@tracon/client`).

### Arayüz payı

`screens/eval-detail.tsx` vaka formu. Bundle payı faz başında ölçülür.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Evaluation/EvalCase.cs, EvalCaseDraft.cs, EvalExpectedToolCall.cs (yeni)
src/Tracon.Core/Evaluation/EvalCheckRegistry.cs, EvalJobHandler.cs, RunToCasePromoter.cs, InMemoryEvalStore.cs
src/Tracon.Core/Evaluation/ToolCallArgumentsCheck.cs (yeni)
src/Tracon.Sql.Shared/Stores/SqlEvalStore.cs (+ paylaşılan sorgu metinleri)
src/Tracon.PostgreSql/Migrations/<NNNN>_eval_case_expected_tool_calls.sql
src/Tracon.SqlServer/Migrations/<NNNN>_eval_case_expected_tool_calls.sql
src/Tracon.Sqlite/Migrations/<NNNN>_eval_case_expected_tool_calls.sql
src/Tracon.AspNetCore/Contracts/EvaluationContracts.cs, Endpoints/EvalEndpoints.cs
src/Tracon.Testing.Contracts.Xunit/Contracts/EvalStoreContract.cs
src/Tracon.UI/frontend/src/screens/eval-detail.tsx
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Sayı/nesne argümanı anlamca eşitken eşleşmez | Birim (karşılaştırma tablosu: `2` ↔ `2.0`, anahtar sırası, iç içe dizi) | `ToolCallArgumentsCheckTests` |
| Argüman gidiş-dönüşünde kaybolur ya da eski satır okunamaz | Sözleşme (bellek içi + üç SQL) | `EvalStoreContract` |
| Kontrol uçtan uca çalışmaz (vaka → run → sonuç) | Fonksiyonel (`FakeModelProvider.CallsTool(name, args)`) | `EvalToolCallArgsTests` |
| Eski `expectedTools` sessizce yok sayılır | Fonksiyonel | `EvalCaseLegacyFieldTests` |
| Terfi argüman yazar ya da adı kaybeder | Fonksiyonel | `RunToCasePromoterTests` deseni |
| Migration iki kez koşar ya da sağlayıcı farkı | Entegrasyon (üç SQL) | mevcut migration kapıları |

Beş soru: iptal (değişmez) · eşzamanlılık (vaka yazımı mevcut) · boş/aşırı girdi
(boş liste, `Arguments` boş sözlük, çok büyük JSON — vaka boyut sınırı ölçülmeli) ·
başka kiracı (suite kiracı kapsamlı; sözleşme dört koşumda) · alt sistem hatası
(bozuk JSON sütunu okunursa vaka hatası, liste çökmez — MEMORY "struct alanı" dersi).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 🤖 | Örnek uygulama, gerçek model, `support` agent'ı | Vaka: `{"name":"get_order_status","arguments":{"orderId":"ORD-1001"}}`, suite kontrolü `toolCallArgsMatch`; eval koş | Vaka geçer; argümanı `ORD-9999` yapınca düşer ve sebep `args mismatch` |
| 2 🤖 | Konsol (Playwright) | Vaka formunda argüman JSON'u gir, kaydet, yenile | JSON kalır; geçersiz JSON kayıtta reddedilir |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Referans run terfisi argüman yazsın mı? | A: yalnız ad · B: kaydedilmiş argümanla | A: argüman çoğu zaman o run'a özgüdür; B'yi konsolda elle eklemek kolay |
| 2 | Eski `expected_tools` sütunu ne zaman düşer? | A: bu fazda kalır · B: sonraki migration | A: geri dönüş yolu açık kalır |

---

## Bitiş Ölçütleri (DoD)

- [ ] `toolCallArgsMatch` sayı, metin, bool, iç içe nesne ve dizi argümanını anlamıyla eşler; birim tablosu yeşil
- [ ] Argümanlı vaka dört koşumda gidiş-dönüş yapar; eski satır ada çevrilir
- [ ] Eski `expectedTools` HTTP anahtarı `400` alır
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek eval koşuldu, sonuç belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] `🤖` case'ler `docs/manuel-test/17-EVAL-VE-DENEYLER.md` içinde ve koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run check` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

---

## Riskler

| Risk | Önlem |
|------|-------|
| Üç sağlayıcıda JSON sütun davranışı farklı (SQL Server `nvarchar` ↔ `jsonb`) | Sözleşme testi dört koşumda; emsal `parameters` |
| Tüketicinin özel değerlendiricisi `EvalItem.ExpectedToolCalls`'ın değer biçimine güvenir | MAF'ın açtığı biçimle doldurulur; XML doc yazar |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Çalışırken dolar. Kapanışta kapanış commit mesajına taşınır.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Plan numara rezerve etmez.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı.

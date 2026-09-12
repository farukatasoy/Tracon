# Faz 18 — Değerlendirme (Eval) Altyapısı

> **Durum:** ✅ **Tamamlandı (2026-08-03)**
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-14**
> **Önkoşul:** [Faz 17](17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md) — iş kuyruğu
> **Sonraki bağımlı:** [Faz 19](19-SURUM-KARSILASTIRMA-VE-AB.md) — "v3 v2'den iyi mi?"
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok — bkz. K-139 · **Migration:** 0009 (`0009_eval.sql`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/18-DEGERLENDIRME.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Plandan Sapmalar

1. **Yeni paket hiç gerekmedi (K-139).** 18.2'nin öngördüğü ölçüm yapıldığında
   `EvalItem`/`EvalCheck`/`LocalEvaluator` gibi tiplerin `Microsoft.Extensions.AI.Evaluation`
   değil **`Microsoft.Agents.AI`** ad alanında olduğu görüldü — `Tracon.Core`
   zaten o pakete doğrudan referans veriyor. Destek tipleri (`EvaluationMetric` vb.)
   geçişli olarak geldi. Planlanandan da kolay çıktı.
2. **`LocalEvaluator.DetailedItems` boş döner (K-142, 🚨).** Plan bu alanı okuma
   yolu sanıyordu; gerçek sonuç `AgentEvaluationResults.Items[0].Metrics`'tedir.
   Bir repro programıyla ölçüldü, bkz. karar defteri.
3. **`RunKind.Eval` plandan sonra, doğrulama sırasında eklendi.** Doc'un açık
   soru 4'ü ("eval çalıştırmaları istatistiklere dâhil olsun mu?") bir öneriyle
   kapatılmıştı ama ilk uygulamada kodlanmadı; örnek uygulamada gerçek bir
   çalıştırmayla `/api/stats`'ın kirlendiği görüldü ve düzeltildi (K-141).
   Bu, "birim testleri geçti ama örnek uygulama gerçek hatayı yakaladı" durumuna
   bir örnek daha.
4. **Vaka düzenleyici JSON değil, tekrarlanan alan formu.** `checks` alanı JSON
   metin kutusu olarak kaldı. Bkz. K-143.
5. **`POST .../run` gövdesi `agentVersion` almaz** — yalnız `modelId` ve
   `numRepetitions`. Faz 19'un 19.4 bölümündeki "açık soru 2" (`agentVersion`
   ile belirli bir sürüme karşı eval koşma) bu yüzden **henüz desteklenmiyor**;
   `agent_version` yalnız kayıt amaçlı, koşu anında **otomatik** çözülen
   agent'ın güncel sürümünden okunur. Faz 19 bunu genişletmek isterse
   `EvalRunTriggerRequest`'e alan eklemesi ve `EvalJobHandler`'ın belirli bir
   sürümü derleyip çalıştırması gerekir (bugün yalnız `IAgentCatalog.ResolveAsync`
   ile **güncel** sürüm çözülüyor).

---

## Amaç

Bir agent için test kümesi tanımlamak, düzenli çalıştırmak ve regresyonu görmek. Sürüm geçmişi (`agent_definition_versions`) Faz 1'den beri var; "v3 v2'den daha mı iyi?" sorusu doğal devamıdır ve bugün cevaplanamıyor. ---

## Bu Fazda Verilecek Kararlar

1. **Eval ve LoopEvaluator ayrı kavramlardır**; bu faz yalnız eval'i yapar (K-140).
2. **AI yargıç ilk sürümde yok** — eval ücretsiz kalmalıdır; ihtiyaç somutlaşınca
   eklenir (K-140).
3. **Denetimler bildirimseldir, kod değil** (K2); özel denetim kodda kaydedilir
   (`AddEvalCheck`).
4. **Her vaka kendi `runs` satırını üretir** — hata ayıklanabilirlik. Gerçek
   çalıştırmada doğrulandı: her sonucun `runId`'si gerçek bir `runs` satırına
   çözülüyor.
5. **`agent_version` ve `model_id` kaydedilir** — regresyon takibinin şartı.
   Gerçek çalıştırmada doğrulandı (yukarı bakınız).
6. **Yeni paket gerekmedi** (K-139) — plandaki 18.2 ölçümü bunu doğruladı.
7. **Eval çalıştırmaları `runs` istatistiklerinden hariç tutulur** (K-141) —
   plandaki açık soru 4, uygulama sırasında gerçek bir hatayla doğrulanıp
   kodlandı.

---

## Bitiş Ölçütleri (DoD)

- [x] Suite tanımlanıp çalıştırılıyor; sonuçlar kaydediliyor — bkz. "Gerçek kanıt" #1
- [x] Her vaka için `runs` satırı ve transcript bağlantısı var — her `EvalCaseResult.RunId`
      gerçek bir `runs` satırına çözülüyor (`GET /api/runs/{runId}` ile doğrulandı)
- [x] Aynı suite iki farklı agent sürümünde çalıştırılıp sonuçlar
      karşılaştırılabiliyor (gerçek çıktı dokümanda) — bkz. "Gerçek kanıt" #2
- [x] Tool çağrısı denetimi (`ToolCalledCheck`) gerçek bir çalıştırmada doğru
      sonuç veriyor — bkz. "Gerçek kanıt" #1 (`tool_called_check: passed`)
- [x] Eval çalıştırmaları normal istatistikleri kirletmiyor — bkz. "Gerçek kanıt" #3 (K-141)
- [x] Bağımlılık ölçümü yapıldı ve karar yazıldı — K-139
- [x] Dört doğrulama kapısı sıfır uyarı — build/test/pack/format, 968 .NET + 55 Vitest testi

---

## Sonraki Faza Devir Notu

- **Faz 19 bu fazın çıktısını kullanır:** iki sürümü aynı suite ile ölçüp yan
  yana koymak, A/B'nin çevrimdışı hâlidir — bu fazda **elle** (agent'ı
  güncelleyip suite'i tekrar çalıştırarak) gösterildi, otomatikleştirilmedi.
- 🚨 **`EvalRunTriggerRequest` bir `agentVersion` alanı taşımaz.** Faz 19'un
  19.4 bölümündeki açık soru 2 ("Faz 18'in suite'i bir varyanta karşı
  çalıştırılabilir... `POST /api/evals/{name}/run` gövdesi `agentVersion`
  alabilsin") **bu fazda karşılanmadı**. Bugün `EvalJobHandler` her zaman
  `IAgentCatalog.ResolveAsync` ile agent'ın **güncel** derlenmiş sürümünü
  çalıştırır; belirli bir geçmiş sürümü çalıştırma yolu yoktur (`CompiledAgentCache`
  anahtarı `(name, version)` olsa da, `IAgentCatalog.ResolveAsync` bir sürüm
  parametresi almaz — Faz 19'un 19.3 bölümü zaten bu genişletmeyi planlıyor).
  Faz 19 A/B deneyi eklerken `EvalJobHandler`'ı da güncelleyip belirli bir
  varyantın sürümüne karşı koşabilmesini sağlamalıdır.
- Faz 20 (maliyet) eval çalıştırmalarının maliyetini ayrı bir kalem olarak
  gösterebilir; `RunKind.Eval` bunu HTTP katmanında filtrelemeyi kolaylaştırır.
- Faz 25 (saklama) eski `eval_case_results` kayıtlarını temizlemekle yükümlüdür;
  `eval_runs` özeti korunur.
- 🚨 **`LocalEvaluator.EvaluateAsync(...).DetailedItems` her zaman boştur** —
  yeni bir MAF eval tipi kullanan biri bu tuzağa düşebilir. Gerçek sonuç
  `Items[0].Metrics`'tedir (bkz. K-142, `MEMORY.md`).

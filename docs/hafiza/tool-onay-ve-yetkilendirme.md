# Tool Onayi ve Yetkilendirme Tuzaklari

> Tool cagrisinin yetkilendirme/onay ekseni: sarmalama sirasi, `ApprovalRequiredAIFunction`
> ile MEAI etkilesimi, sunum genisleme noktasi, replay guard'lari. Calistirma
> yolunun KENDISI (RunRecording zinciri, `scope`/span, olay yazimi, iptal) icin:
> [`cekirdek-calistirma.md`](cekirdek-calistirma.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana dokunurken okunur.
> Faz 142'de ayrildi: `cekirdek-calistirma.md` 17.712/16.000 B'ye ulasmisti
> (bütçeyi aştı). Onay/yetkilendirme ekseni calistirma yolundan BAGIMSIZ
> buyuyor -- her tool-onay fazi ona ekliyordu.

- **🚨 MAF tool'a BOS bir servis saglayici gecirir; `AIFunctionArguments.Services` bu repoda KULLANILAMAZ** (Faz 28, K-218): tool bagimliliklarini KURULUM aninda alin, `arguments.Services`'e guvenme. İstisna (Faz 127): `AddScopedTool` bunu ezer, gerçek kapsam açar. Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Tool sarmalama sirasi Authorizing → Timeout → ApprovalRequired → gercek fonksiyon; ret istisna firlatmaz, fail-closed** (2026-08-19, K-487/K-488): `ToolAuthorizationAccumulator` (`ToolUsageAccumulator` deseni) `ToolInvocationRecord.AuthorizationDenied`'i isaretler — K-490.
- **🚨 `AgentDefinitionCompiler`'i katalog ATLANARAK dogrudan cagiran yol, `IAgentDecorator` zincirini (kayit, telemetri, tool onayi) ELLE uygulamalidir** (Faz 86, K-581): `CompiledAgentCache`'i atlayan bir yol katalog metoduna hic girmezse dekorasyon calismaz — run KAYITSIZ gecer. `AgentDecoratorPipeline.Apply` bunu tek yerde toplar. BYOK'un onbellek atlamasi bu tuzagi TASIMAZ.
- **🚨 `RunReplayService.PrepareAsync`'in İKİ hazırlık kolu var; birini kapatıp diğerini unutmak tekrar eden bir kusur sınıfıdır** (Faz 47 → HATA-S4-014, Faz 112 → K-628): kalıcı tanım kolu `definition.ToolNames` okur, katalog/kod-agent kolu (`PrepareFromCatalogAsync`) `descriptor.ToolNames` okur — AYRI kod yollarıdır, biri diğerinin gövdesini paylaşmaz. Faz 47'nin onay-tool guard'ı (`FindApprovalTool`) ilk yazımda yalnız birinci kola girdi; Faz 112'nin istemci-tool guard'ı (`FindClientTool`) bunu bilerek İKİ kola birden ekledi. Replay'e yeni bir "bu agent şu durumda reddedilir" kuralı eklerken: `grep -n "FindApprovalTool\|FindClientTool" src/AgentPrism.Core/Replay/RunReplayService.cs` her ikisinin de tam olarak İKİ çağrı yeri olduğunu göstermeli — biri eksikse kod tanımlı agent delikte kalır.
- **🚨 Onay isteğine ek bilgi (sunum, log, metrik) eklemek isteyen bir
  `AIFunction` sarmalayıcısı `ApprovalRequiredAIFunction`'ın İÇİNE konulamaz —
  `ToolRegistryWrapperOrderTests`'te zaten ÖLÇÜLMÜŞ gerçek şu: MEAI'nin
  function-invoking istemcisi onayı `AITool.GetService(Type)` üzerinden
  `ApprovalRequiredAIFunction`'ı BULARAK, `InvokeAsync`'i hiç çağırmadan
  ayırır** (Faz 142). Sonuç: onayla ilgili yeni bir davranış eklemenin doğru
  yeri tool sarmalayıcı zinciri DEĞİL, `ToolApprovalRequestContent`'in
  `RunRecordingAgent`'ta zaten okunduğu tek nokta (`ChildRunApproval` ailesi).
  Faz 142'nin planı `ApprovingAIFunction` adlı bir sarmalayıcı öngörüyordu;
  bu ölçüm nedeniyle o dosya hiç yazılmadı — bkz. fazın "Plandan Sapmalar"ı.
- **🚨 `AIFunction.JsonSchema`'da `"type"` bir dize DEĞİL bir DİZİ olabilir** (Faz 143,
  Microsoft.Extensions.AI 10.9.0'da ölçüldü): `string?` gibi nullable bir C# parametresi
  `"type":["string","null"]` üretir, `"type":"string"` değil — 9.9.1'de bu davranış yoktu
  (probe önce eski sürümle yazıldı, gerçek pakette skip'e düştü). `TryGetProperty("type",
  out var t) && t.ValueKind == JsonValueKind.String` deseni sessizce hiç eşleşmez ve
  nullable her parametre "desteklenmiyor" sanılır. Çözüm: `type` okuyan her yerde önce
  `ValueKind == Array` dalını da kontrol et, ilk `"null"` olmayan girdiyi kullan
  (`SchemaArgumentGenerator.PrimaryType`). Bir tool şemasını TÜKETEN (üreten değil) her
  kod, MEAI'nin GERÇEK kurulu sürümüyle ölçülmeli — `maf-api-kesfi` dump'ı tip imzasını
  gösterir ama ÜRETİLEN JSON şeklini göstermez, küçük bir probe projesi gerekir.
- **xunit.v3, yalnız `public` sınıfları test olarak keşfeder — `private`/`internal` bir
  sınıf, bir sözleşme temel sınıfından (`[Fact]` miras alarak) türese bile `dotnet test`'in
  normal koşumuna karışmaz** (Faz 143, ampirik ölçüldü: `internal sealed class` + kasten
  kırık `ExpectedResultText` → "Zero tests ran"). Bu, "sözleşme suite'i kasten kırık bir
  implementasyonda KIRMIZI olmalı" kanıtını (`ContractSelfProofTests` deseni) NORMAL yeşil
  koşumu bozmadan yazmanın yoludur: kırık fixture'ı `private sealed class` yap, `[Fact]`
  metodunu doğrudan çağır (`await instance.SomeFact()`), `try/catch` ile "attı mı" sına.

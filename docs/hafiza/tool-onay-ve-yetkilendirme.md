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
- **🚨 `RunReplayService.PrepareAsync`'in İKİ hazırlık kolu var; birini kapatıp diğerini unutmak tekrar eden bir kusur sınıfıdır** (Faz 47 → HATA-S4-014, Faz 112 → K-628): kalıcı tanım kolu `definition.ToolNames` okur, katalog/kod-agent kolu (`PrepareFromCatalogAsync`) `descriptor.ToolNames` okur — AYRI kod yollarıdır, biri diğerinin gövdesini paylaşmaz. Faz 47'nin onay-tool guard'ı (`FindApprovalTool`) ilk yazımda yalnız birinci kola girdi; Faz 112'nin istemci-tool guard'ı (`FindClientTool`) bunu bilerek İKİ kola birden ekledi. Replay'e yeni bir "bu agent şu durumda reddedilir" kuralı eklerken: `grep -n "FindApprovalTool\|FindClientTool" src/Tracon.Core/Replay/RunReplayService.cs` her ikisinin de tam olarak İKİ çağrı yeri olduğunu göstermeli — biri eksikse kod tanımlı agent delikte kalır.
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
- **🚨 Bir yetkilendirme kapısı, ret YANITINI kendisi üretemez — çağırandan
  almalıdır** (Faz 147, K-684): `RunAuthorizationGate.CheckRunResourceAsync`
  reddi `denied` parametresiyle alır. Sebep ölçüldü: reddedilen tekil kaynağın
  gövdesi o UCUN kendi "yok" yanıtıyla birebir aynı olmalıdır ve o metin uçtan
  uca değişir (`"Run not found"` · `"Trace not found"` · `"Attachment not
  found"` · `"Approval request not found"`). Kapının kendi metnini üretmesi bir
  uçta kaçınılmaz olarak ayrışır ve ayrışma bir varlık oracle'ıdır. Aynı sebeple
  handler'ın `Reason`'ı tekil kaynak reddinde YUTULUR.
- **🚨 Kapı sırası iki taraflıdır: kiracı kontrolünden SONRA, durum
  okumasından ÖNCE** (Faz 147): "sonra", başka kiracının kimliğinin tüketicinin
  handler'ına hiç gitmemesini ve var olmayan kaynak için handler'ın hiç
  çağrılmamasını sağlar. "önce" ise `cancel` ve onay kararındaki `409`'un
  reddedilen çağırana kaynağın var olduğunu VE durumunu söylemesini engeller.
  İkisinden biri unutulursa kod derlenir, testten geçer ve yalnız bir sızıntı
  senaryosunda görünür.
- **Yeni bir run başlatan yüzey eklendiğinde `RunAuthorizationCoverageTests`'in
  İKİ listesi vardır** (Faz 147): `ExpectedRunStartingFiles` (altı dosya) ve
  `ExpectedResourceFiles`. Tarama dosya bazındadır ve `RunEndpoints.cs` ikisinde
  de yer alır; oradaki run BAŞLATMA kanıtı `CheckRunResourceAsync ... RunAccess.Start`
  regex'idir, çünkü replay kaynak `run`'ın id'sini taşımak zorundadır ve
  `CheckRunAsync` bunu ifade edemez.

- **🚨 Bir yüzey YARIM kapılı olabilir; bir kapıyı bulmak diğeri hakkında kanıt
  değildir** (Faz 149). `OpenAIConversationsEndpoints` üç `SessionOwnershipGate`
  çağrısı taşıyordu (Faz 148 eklemişti) ve `RunAuthorizationGate` çağrısı
  **sıfırdı**: handler'ı `GET /api/sessions/{id}`'yi reddedecek biçimde kurmuş
  bir tüketicide `GET /v1/conversations/{id}/items` aynı sohbet geçmişini
  veriyordu. Dosya `ExpectedSessionOwnershipFiles`'ta vardı,
  `ExpectedResourceFiles`'ta yoktu — kapsam kapısı **iki ayrı liste**dir ve
  ikisini birden kontrol etmeyen bir denetim yeşil görünür. Yeni bir oturum
  yüzeyi eklerken **ikisine de** ekle.
- **Yetkilendirme kapısı ile SAHİPLİK kapısı ayrı sorular sorar ve ikisi de
  gerekir** (Faz 148 · 149): handler tüketicinin politikasıdır, sahiplik
  Tracon'in kendi verisidir. Sıra: kiracı → handler → sahiplik → gövde.
  Sahiplik kapısı `HttpContext` ister (yönetim politikası istek başına
  değerlendirilir); handler kapısı istemez.
- **Yönetim muafiyeti ASİMETRİKTİR ve öyle kalmalıdır** (Faz 149, K-694):
  `ManagementPolicy` sahipsiz bir satırı **okutur** (`DeniesAsync`), ama o
  satıra `run` başlatmaz (`CheckRunSessionAsync`). Okumak ile konuşmaya eklemek
  farklı eylemlerdir. Sahipliği "tek yardımcıda toplamak" isteyen bir
  sadeleştirme bu asimetriyi siler; `The_management_exemption_does_not_extend_to_starting_a_run`
  onu tutar.
- **🚨 Bir MCP tool'unun sonucu `string` DEĞİL `AIContent`'tir; `ToolResultText`
  onu okuyamadığında BEŞ tüketici birden sessizce bozulur** (2026-09-18,
  `HATA-S1-026` ve kapanışta ölçülen iki kusur daha). `McpClientTool
  .InvokeCoreAsync` tek bloklu sonucu bir `AIContent`, çok bloklu sonucu bir
  `AIContent[]`, yalnız hata/`StructuredContent`/uygulama meta'sı taşıyan
  sonucu bir `JsonElement` döndürür (kaynak okundu, `ModelContextProtocol.Core`
  2.2.0 — ilk ikisi kaydın "protokol sonucu, adaptörün işi" varsayımını
  yanlışlar). `TryGetText` `AIContent` için `false` dönüyordu ve şunlar bunun
  üstüne dallanır: `TruncatingAIFunction` (sınır HİÇ uygulanmadı — 200 bayta
  karşı 8035 bayt ölçüldü; `AIContent[]` ise `AIContent` DEĞİLDİR ve
  `{"error":"tool_result_unsupported"}` ile değiştirildi, sınır hiç
  yapılandırılmamış olsa bile — katman her zaman kuruludur) ·
  `ContentGuardMessageMasker` (her MCP sonucu modele ulaşmadan
  `[Tool result could not be inspected]` oldu) · `ToolInvocationTracker` ve
  `RunRecordingAgent` (`Result`/`Payload` `null`) · `RecordedToolPlayback`
  (replay sonucu kaybetti). **Ders:** ortak bir kanonikleştiriciye yeni bir
  sonuç şekli eklemek tek bir `switch` satırı değildir —
  `grep -rn "ToolResultText" src/` ile TÜKETİCİLERİ say; her biri `false`
  dalında ayrı bir sessiz davranış saklıyor olabilir. K-798 · K-799.
- **`AIJsonUtilities.DefaultOptions` ile serileştirmek `Tracon.Core`'da AOT
  temizdir** (2026-09-18, ölçüldü: sıfır `IL2026`/`IL3050`). `AIContent`
  tipleri MEAI'nin kendi kaynak-üretilmiş context'inden çözülür ve
  `JsonSerializer.Serialize(sonuç, options.GetTypeInfo(typeof(object)))`
  adaptörlerin `FunctionResultContent.Result` için yaptığı **aynı** çağrıdır —
  ölçülen bayt tele giden bayttır. Bu yol için AOT kaçış merdivenine (elle
  yazma → `source generator` → `[RequiresUnreferencedCode]`) gerek yoktur.
- **🚨 Uzak bir tool'un davranışını sınayan fake, uzak tool'un DÖNÜŞ ŞEKLİNİ
  taklit etmelidir** (2026-09-18, K-800): Faz 89 "her iki sarmalama zinciri
  aynı halkaları taşır" sözleşmesini üç testle kapattı ve üçü de
  `AIFunctionFactory.Create(() => new string('a', 10_000))` sarıyordu — `string`
  döndüren bir vekil. Gerçek `McpClientTool` `AIContent` döndürür ve kusur tam
  olarak o ayrımda yaşıyordu: testler yeşil, zincir kurulu, sınır bir faz
  boyunca hiç uygulanmadı. Şekli taklit etmeyen fake yalnız sarmalayıcının
  **kurulduğunu** kanıtlar, davranışını değil. Bugün o üç test `AIContent`
  döndüren bir vekile taşındı ve üstüne gerçek SDK istemcisiyle konuşan bir
  fonksiyonel test (`McpToolResultTruncationTests`) eklendi.

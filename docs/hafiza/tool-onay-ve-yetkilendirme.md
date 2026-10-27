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
- **🚨 Tool bütçe kapısı zincirdedir; zincir dışı kalan kaynak `run_skill_script` ve hosted tool'dur** (2026-10-25, Faz 243, K-1089; arama tool'ları Faz 246'da zincire girdi): `BudgetGatingAIFunction` `ExplainedFailure` içinde, `Authorizing` önündedir; kod, çalışma alanı, MCP tool'u ve iki arama tool'u `Compose`'dan geçer. `run_skill_script` MAF'ın kendi tool'udur: koşulu `SandboxedSkillScriptRunner.ExecuteAsync` girişinde `BudgetGatingAIFunction.Refusal()` sorar (koşul tek yerde). Koşul modelin kapısıyla aynıdır (`IsExhausted`): fonksiyon düğümünün kuralı (maliyet+süre) burada geçmez. Ölçüldü: onay isteği zinciri çağırmadan üretilir ve aynı turdaki onaysız kardeş tool'lar onayla birlikte bekler. Kayıt: zincirdeki araç her çağrıyı en dış katmanda (`CallRecordingAIFunction`) run'ın `ToolCallLedger`'ına not eder; run başarısız ya da iptal bitince satırlar defterden yazılır, yani akışsız run bütçe reddiyle bitse de satırlar kalır (Faz 254, K-1110). Başarılı run'ın satırları mesajlardan gelir ve süreyi defterden alır. `run_skill_script` ve diğer zincir dışı araçlar hâlâ mesajdan okunur.
- **🚨 Tracon'un run'a KENDİSİNİN eklediği tool `BuiltInToolGovernance.Govern`'dan geçer; yeni kaynak `ToolSourceGovernanceTests` listesine girmeden düşer** (2026-10-26, Faz 246, F-688): çalışma alanı, `search_knowledge` ve `search_conversation_memory` registry'de değildir (`/api/tools` listelemez; işleyici yazarı adı sabit metin yazar). `Effect`: `Read/Write/Destructive`, `External`, `External`. Üç ölçüm: (1) MAF `ChatHistoryMemoryProvider` `OnDemand`'da HER çağrıda yeni fonksiyon üretir; sarmalayıcı önbelleksizdir, `GovernAddedTools` önceki araçları referansla ayırır ve `search_conversation_memory` dışı eklenen tool'da çağrıyı düşürür. (2) Yönetişim servisi yoksa derleme düşer (`TraconCompilationException`, "AddTracon"). (3) Replay `toolTransform` yönetilen fonksiyonu bir kez sarar. 🚨 `TraconToolUsage.Report` maliyetsiz satırı `NonTokenCost.UnsummedItemCount`'a SAYAR: toplam boş kalır ama run kaydı `nonTokenCost` taşır.
- **🚨 Onay devam run'ı yeni bir KÖK run'dır ve bütçesi sıfırdan başlar; ilk işi onaylanan çağrıdır, kardeş tool onaylanan betikten ÖNCE koşmaz** (2026-10-27, Faz 254, F-690): yalnız workflow devamı önceki harcamayı devralır (F-531). Onaylı çağrılar önce koşar; kalıcı kuralla onaylı betik de ilk koşar, çağrı sırası ters olsa da. Bu yüzden "devam run'ında betik bütçeye takılır" testi bütçeyi run'ın önüne konan bir adımla (`IAgentDecorator`) harcar. Sıra MEAI sürümüyle değişebilir: teste gömme.
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
- **🚨 `AIFunction.JsonSchema`'da `"type"` bir dize DEĞİL bir DİZİ olabilir** (Faz 143, MEAI 10.9.0): `type` okuyan her yer önce `ValueKind == Array` dalını kontrol eder (`SchemaArgumentGenerator.PrimaryType`). Tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md) (Faz 243 yer açma).
- **xunit.v3 yalnız `public` sınıfları keşfeder** (Faz 143): kasten kırık sözleşme fixture'ı `private sealed class` olur, `[Fact]` doğrudan çağrılır. Tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md) (Faz 243 yer açma).
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

- **🚨 Drain kapısı yetki kapısının listesine binmez; süreç içinde run başlatan
  her yüzey onu kendi gövdesinde sorar** (2026-10-13, F-431). Faz 87 kapıyı
  yalnız ajan run ucuna koydu, workflow girişini "kuyruktan geçer" diye dışarıda
  bıraktı. Sonra workflow uçları süreç içinde akışlı run başlattı: drain
  sırasında `resume` `200` ile yeni run açtı (ölçüldü); OpenAI, MCP, A2A ve ses
  soketi de kapısızdı. Kural: süreç içinde run başlatan yüzey `DrainGate.Check`'i
  (protokol yüzeyinde kendi ret biçimiyle) gövdenin başında sorar; yalnız job
  yazan yüzey (trigger, eval, schedule, onay) sormaz, job bekler. Kapı:
  `RunAuthorizationCoverageTests.Every_known_in_process_run_starting_surface_refuses_while_draining`;
  yeni yüzey listeye elle girer.
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
  İki kapı da `HttpContext` alır: sahiplik yönetim politikasını istek başına
  değerlendirir, ikisi de hatayı istek servislerinden aldığı logger'a yazar.
- **🚨 Fail-closed bir `catch` log yazmazsa ret operatöre görünmez; "kayıtsız"
  ile "attı" aynı dala düşmemeli** (2026-09-23, C12): iki kapı tüketici
  handler'ının istisnasını reddetti, ama log yazmadı; 403 gövdesi olmayan bir
  "denied" kararı bildirdi. `SatisfiesManagementPolicyAsync` kayıtsız policy'yi
  (belgelenmiş geri düşüş) ve atan handler'ı tek `catch`'e koyuyordu. Log
  eklemeden önce ikisini ayır, yoksa her liste isteği Error yazar. Policy'yi
  adla çözmek de tüketicinin provider'ını çağırır: `GetPolicyAsync` `try`
  içinde kalır, yoksa daraltma 500 olur. 404 dalı metin değiştirmez (K-684);
  yalnız 403 "check failed" der. Filtre K-840'tır: eski filtreyle handler'ın
  `HttpClient` timeout'u kaçıyordu; var olan kaynak 500, eksik kaynak 404
  veriyordu (varlık oracle'ı).
- **Yönetim muafiyeti ASİMETRİKTİR ve öyle kalmalıdır** (Faz 149, K-694):
  `ManagementPolicy` sahipsiz bir satırı **okutur** (`DeniesAsync`), ama o
  satıra `run` başlatmaz (`CheckRunSessionAsync`). Okumak ile konuşmaya eklemek
  farklı eylemlerdir. Sahipliği "tek yardımcıda toplamak" isteyen bir
  sadeleştirme bu asimetriyi siler; `The_management_exemption_does_not_extend_to_starting_a_run`
  onu tutar.
- **🚨 Bir MCP tool'unun sonucu `string` DEĞİL `AIContent`'tir; `ToolResultText` onu okuyamadığında BEŞ tüketici birden sessizce bozulur** — tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md) (2026-10-12 bütçe turu).
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

- **🚨 Rolün reddettiği bir panel isteği HİÇ GÖNDERMEMELİDİR — 403'ü çizmek "konsol bozuk" diye okunur** — tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md) (2026-10-12 bütçe turu).
- **Örnek uygulama rol denemesi için hazır bir bayrak taşıyor: `Tracon:Demo:Roles:Enabled`** (2026-09-19, aynı vaka): rolü `X-Tracon-Demo-Role: reader|operator|admin` başlığından okur; rol ölçmek için `Program.cs` değiştirmek gerekmez. Tarayıcıda başlık `context.setExtraHTTPHeaders` ile enjekte edilir, konsolun kendi `fetch`'i göndermez. Tam metin: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md) (Faz 254 turu).

- **🚨 Onay kuralı koşul kümesinin parmak izi TEK yerdedir: `ToolArgumentConditionFingerprint`** (2026-09-22, Faz 182, K-851): `conditions_hash` kalıcı veridir, biçimi DEĞİŞTİRİLEMEZ; iki elle kopya iki yönde ayrışmıştı. Tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md) (Faz 246 yer açma).
- **🚨 Onay kuralı tool'un KAYNAĞINA bağlıdır; damgayı kuralı SUNAN registry verir** (2026-10-11, F-283/F-284, K-876/K-877): kural yazılırken endpoint'in SHA-256'sı damgalanır (`ToolSourceFingerprint.Of`, İKİ yazma yolu), eşleşme kesin eşitliktir; "kaynak yok" ≠ "kaynak şu an görünmüyor". Tam kayıt: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md) (Faz 246 yer açma).
- **🚨 Her kök run `AwaitingApproval` ile kapanabilir; posta kutusu satırını yalnız kancayı veren yol yazar** (2026-10-12, F-370): onay devamının ikinci onayı listede görünmüyordu, çünkü testler zincirin yalnız ilk halkasını sınıyordu. Kök run koşturan her arka plan yolu `QueuedTurn`'ü kullanır (oturum önce, onay sonra).
- **🚨 Cevabı taşıyamayan yüzeyde onay run'ı sonsuza dek bekletir; sınıf altı yerdeydi** (2026-10-14, F-448, K-977; F-389 batch/eval): Chat Completions, oturumsuz yerel run, MCP, A2A, batch, eval. Hepsi `SessionlessApprovalRun.CloseAsFailedAsync` ile `Failed` (`approval_unanswerable`) kapatır; MCP görev eşlemesi bu tipi K-103 reddi gösterir. 🚨 Oturumsuzluk tek başına "cevaplanamaz" demek DEĞİLDİR: geçmişi taşıyan süreç içi tüketici cevaplayabilir — bu yüzden kural `RunRecordingAgent`'ta değil yüzeydedir.
- **🚨 Onay satırı yazımı oturum kaydıyla aynı pencereye girmeliydi; hata modeli ve tool'u yeniden koşturuyordu** (2026-10-14, F-391, K-979): `QueuedTurn.WithinLeaseAsync` ortak; kimlik pencere boyunca sabit, tekrar `GetAsync` ile önce okur. İşlenmiş-ama-hata-dönmüş yazımı yalnız `FaultInjection.ThrowAfter` üretir.
- **🚨 Onay isteğiyle ya da istemci tool çağrısıyla biten tur metin taşımaz; yapılandırılmış doğrulayıcı onu "boş" sayıyordu** (2026-10-14, F-350, K-980): `WaitsForCaller` bekleme turunu atlar; MEAI 10.9 son turda fonksiyon tool'larını modelden alır, sonucu olmayan çağrı istemci tool'udur.

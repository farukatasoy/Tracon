# Ertelenen ve Kapatılan Adaylar

> `ADAYLAR.md`'den taşındı (2026-08-16, Faz 58.1): bunlar artık
> **aday değildir**. Ölçüm kanıtı korunmuştur; yalnız grep'lenir.

---

### F-72 · Agent Control Specification (ACS) uyumu — ÖLÇÜLDÜ, ERTELENDİ (2026-08-06)

> **Dalga 3'e seçildi, plana dönüşmedi.** Kullanıcı kararı: ertelensin.
> Aşağıdaki kanıt **ölçülmüştür**; sonraki oturum ölçümü tekrarlamak zorunda
> değildir, yalnız tarihini denetler.

**Sorun:** AgentPrism bir kontrol düzlemidir ama kontrol kuralları **kendi
biçiminde** yaşar: onay kuralları `tool_approval_rules`, kota `quotas`, rol
politikaları kodda. Microsoft 2026-06-02'de bunun için açık bir standart
yayımladı.
**Kapsam:** ACS bildirimini okuyan bir politika değerlendirici; kesişim
noktalarının AgentPrism dekoratör zincirine eşlenmesi (kayıt 0 → telemetri 10
→ onay 20 zinciri hazır yuvadır).
**Değer:** Kurumsal alıcı "hangi standarda uyuyorsunuz" diye sorar. Bugün
cevap "kendi modelimiz"dir.
**Mercek:** 3, 6.

**Hazırlık — 🚨 ÖLÇÜLDÜ (2026-08-06):**

| Ölçüm | Sonuç |
|---|---|
| Spesifikasyon sürümü | **0.3.1-beta**, durum **Draft**. Belge kendisi yazıyor: *"the contract MAY change in breaking ways between minor versions"* |
| Sekiz kesişim noktası | ✅ Doğrulandı: `agent_startup`, `input`, `pre_model_call`, `post_model_call`, `pre_tool_call`, `post_tool_call`, `output`, `agent_shutdown` |
| Beş karar | ✅ `allow`, `warn`, `deny`, `escalate`, `transform` |
| .NET paketi | ✅ **Var:** `AgentControlSpecification` `0.3.1-beta.1`, yazar **Microsoft**, MIT, imzalı, `projectUrl = github.com/microsoft/agent-governance-toolkit` |
| Geçişli yönetilen bağımlılık | ✅ **0** (sıfır) — restore ile ölçüldü |
| Public tip sayısı | 66. `AgentControlAgentFrameworkRunMiddleware<,>`, `AgentControlDelegatingChatClient<,>`, `AgentControlMcpToolProvider<,>`, `ApprovalResolver`, `NativeAgentControlRuntime` dahil |
| 🚨 **Uygulama biçimi** | **Native P/Invoke.** `lib/net8.0/AgentControlSpecification.dll` yalnız ince bir sarmalayıcıdır; iş `libagent_control_specification_core` (Rust) içindedir |
| 🚨 **Taşınan RID'ler** | **Beş:** `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`, `win-x64`. **`win-arm64` YOK. `linux-musl` (Alpine) YOK** |
| 🚨 NuGet arama indeksi | Paket `azuresearch` sorgusunda **görünmüyor** (unlisted veya indekslenmemiş) |
| İkinci paket | `Microsoft.AgentGovernance` 5.0.0 — GA görünümlü, 33 318 indirme, 2 geçişli paket (`YamlDotNet`). ACS'nin **üst çerçevesi**, spesifikasyon paketi değil |

🚨 **"MAF adaptörü hazır" iddiası yarım doğrudur.**
`AgentControlAgentFrameworkRunMiddleware<TInput,TOutput>` **MAF tipi almaz** —
ACS'nin kendi `IAgentControlAgentInvocationContext<TInput,TOutput>` arayüzünü
alır. Adaptör bir **şekildir**, hazır bir köprü değil; AgentPrism yine de
`AIAgent` → o arayüz dönüşümünü yazmak zorundadır.

**Maliyet:** Uygulama **düşük-orta** (adaptör şekilleri hazır). **Bağımlılık
riski yüksek** — asıl maliyet buradadır.

**Risk:** 🚨 Native bir bağımlılık bir NuGet **kütüphanesi** için ağır bir
taahhüttür: Alpine tabanlı bir konteynerde veya Windows ARM64'te tüketicinin
uygulaması **çalışmaz**. Standart beta ve kırıcı değişebileceğini kendisi
yazıyor.
**Erteleme gerekçesi (kullanıcı kararı, 2026-08-06):** K-212'nin (Foundry)
deseni — ağırlık değil, **olgunluk ve doğrulanabilirlik**.
**Yeniden açılma koşulu:** ACS **GA** olduğunda; ya da spesifikasyon yönetilen
bir uygulamaya kavuştuğunda. Alınırsa **ayrı bir paket** olmalıdır
(`AgentPrism.AgentControl`), K-185/K-209/K-212 deseniyle — native ağırlık
yalnız isteyen tüketiciye bulaşmalıdır.
**Bağımlılık:** [Faz 48](fazlar/48-GUARDRAILS.md)'in `IContentGuard`'ı ACS'nin
`input`/`output` kesişim noktalarına eşlenir. `pre_tool_call`/`post_tool_call`
Faz 48'de **kapsanmadı** ve F-61 ile birlikte düşünülmelidir.
**Ekosistem:** Microsoft'un kendi standardı; Apache 2.0, topluluk yönetimli.
Kaynak: [spesifikasyon](https://microsoft.github.io/agent-governance-toolkit/packages/agent-control-specification/) ·
[normatif metin](https://github.com/microsoft/agent-governance-toolkit/blob/main/policy-engine/spec/SPECIFICATION.md)

---

### F-76 · Paylaşılan SQL kaynağının XML doküman çakışması — ✅ KAPATILDI (2026-08-08)

> **Faza dönüşmedi; bir kusur olarak düzeltildi.** Kapsam ölçülüp **daraltıldı**:
> hata yalnız `ProjectReference` ile derleyen tüketiciyi etkiliyordu, NuGet
> paketiyle tüketen bir uygulamayı **etkilemiyordu** (uçtan uca doğrulandı:
> HTTP 200). Düzeltme `samples/AgentPrism.Api.csproj` içindedir ve K-185'i
> yeniden açmadı. Tam gerekçe, ölçümler ve koruma testi: **K-352**.

---

## ADAYLAR.md'den taşınan kapanmış kayıtlar (2026-09-07)

> Dokuz kapanmış gövde buraya taşındı; `ADAYLAR.md` 80 KB bütçesini doldurmuştu
> ve dosyanın kendi kuralı kusurların orada yaşamamasıdır. Kapanış kanıtı
> korunmuştur — hiçbiri silinmedi. **F-180** ayrıca `ADAYLAR.md`'de özetiyle
> kalır: vakası kapandı ama **sınıfı açıktır**.

### F-197 · Üretilen istemcinin koleksiyonları `null` başlıyordu — ✅ KAPANDI (2026-09-06)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Kayıt tek vakayı anlatıyordu;
**sınıf taraması ikinci ve daha ağır vakayı buldu** — sunucu.

| Yarı | Ölçüm (düzeltme öncesi) | Düzeltme |
|---|---|---|
| İstemci | 50 non-nullable koleksiyon property'si `= default!`; yalnız `Message` ile çağrı `500` | `nswag-postprocess-client.py` **beşinci geçişi**; tam yeniden üretim, delta 100 satır (50 çift), başka kayma yok |
| **Sunucu** (kayıtta yoktu) | Ham `{"approvals":null}` → `500` NRE; `{"documents":null}` → `200` ama SSE gövdesinde NRE | `RequestBodyBinding` gövde okumasına `RespectNullableAnnotations` → `400` `ProblemDetails` |

**Ayırt edici iki tarafta da `nullable` annotation'ıdır.** 56 nullable koleksiyon
property'si `default!` KALIR — `?` işareti "verilmedi" ile "boş verildi"yi
ayırdığını söyleyen sözleşmedir ve silinmesi gerçek bir ayrımı siler.

**Kapı:** `GeneratedClientCollectionDefaultTests` (YENİ) — düzeltmeden önce iki
testi de kırmızıydı (`AgentEndpoints.cs:698` NRE, ölçüldü), sonra yeşil. Dört
ham gövde `400`, atlanan koleksiyon hâlâ `200`, nullable property'ye açık `null`
hâlâ `200`. `nswag_postprocess_client_test.py`'a beş birim testi eklendi
(nullable'ın korunması ve iç içe generic'in REDDİ dahil).

**Karar:** K-702. **Tuzak:** `docs/hafiza/nswag-istemci-uretimi.md` ·
`docs/hafiza/aspnetcore-json.md`.

**Kapılar:** fonksiyonel paket 922/922 · `python3 -m unittest discover -s scripts`
232/232.


### F-190 · MCP Tasks testlerinin tam çözüm koşumunda yalıtımı — ✅ KAPANDI (2026-09-04)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. **K-656 sınıfı DEĞİLMİŞ** —
ilk teşhis yanlış daralmıştı (bkz. aşağıdaki kayıt). Gerçek kök neden:
`ModelContextProtocol.Core`'un istemcisi (`McpClient.CreateAsync`,
`ProtocolVersion` verilmemişse) önce `server/discover` probesini dener; bu
probe `McpClientOptions.DiscoverProbeTimeout` ile sınırlıdır ve **üretim
varsayımı 5 saniyedir**. Süre aşılırsa istemci SESSİZCE eski `initialize`
handshake'ine düşer ve `2025-11-25` negotiate eder — Tasks eklentisi bunu
reddeder, düşen testin mesajı bunu birebir söylüyordu. Tam paket koşumu CPU
baskısı altında bu 5 saniyeyi ara sıra aşıyordu. SDK'nın kendi XML dokümanı
bunu zaten belgeliyor: varsayılan "gerçek ağ eşleri" için kasıtlı kısa,
"yüksek gecikmeli ortamlar için artırın" diyor — in-memory `TestServer` +
onlarca paralel host tam olarak o ortam.

**Düzeltme:** `Infrastructure/McpTaskTestClient.cs`'e
`DiscoverProbeTimeout = TimeSpan.FromSeconds(30)` eklendi (varsayılan
`InitializationTimeout` 60 sn'nin altında kalır — SDK'nın kendi bağlanma
bütçesi böyle bozulmaz).

**Kapı:** `McpTasksEndpointTests.Discover_probe_negotiates_2026_07_28_even_when_the_first_response_is_slow` —
`configureApp`'ten geçirilen bir middleware ilk `/agentprism/mcp` isteğini
6 saniye geciktirir (SDK'nın 5 sn varsayılanının üstü, düzeltmenin 30 sn'sinin
altı). Düzeltmeden ÖNCE kırmızıydı (`2025-11-25` negotiate edildi, ölçüldü),
sonra yeşil. Gerçek CI çekişmesini beklemeden mekanizmayı deterministik
kanıtlıyor.

**Sınıf taraması:** Bu SDK istemcisini (`McpClient.CreateAsync`) kuran tek yer
`McpTaskTestClient.cs`'ti — başka vaka yok. Üretim tarafında
(`McpOAuthAuthorizationCoordinator.cs:233`) `clientOptions: null` **kasıtlı**:
o kod gerçek ağ eşlerine bağlanıyor, SDK'nın üretim varsayımı orada doğru —
kapsam dışı.

**Kapılar:** `ic-dongu` ✅ (767/767, yeni test dahil) · `tarama` ✅ temiz ·
`kapanis --taban dd0ad27e` çalıştırıldı.

**Tuzak:** [`docs/hafiza/test-altyapisi.md`](../hafiza/test-altyapisi.md).

<details>
<summary>Kapanış öncesi teşhis anlatısı (yanlış sınıflandırma, kayıt)</summary>

**Sorun:** `dotnet test AgentPrism.slnx` (tüm çözüm birlikte) koşumunda üç MCP
Tasks testi düşüyor: `McpTasksEndpointTests.Unknown_task_id_is_reported_as_a_protocol_error_not_a_500`,
`McpTaskCrossInstanceTests.Second_instance_reconstructs_a_completed_task_from_the_shared_database`,
`McpTaskCrossInstanceTests.Second_instance_reconstructs_an_approval_rejection_generically_not_with_todays_exact_wording`.

**Ölçüm (2026-09-04):** Yalıtım sorunudur, regresyon değildir.

| Koşum | Sonuç |
|---|---|
| Tüm çözüm (`AgentPrism.slnx`) | ❌ 3 düştü / 766 |
| Yalnız `AgentPrism.AspNetCore.FunctionalTests` derlemesi | ✅ 766/766 |
| Yalnız `*McpTask*` filtresi (HEAD) | ✅ 12/12 |
| Yalnız `*McpTask*` filtresi (temel `f2147a27`, Faz 139 öncesi) | ✅ 12/12 |

Faz 139–143 **hiçbir MCP koduna dokunmadı** (`git diff --name-only f2147a27..HEAD`
yalnız `docs-site/public/screenshots/mcp.png` veriyor).

Düşüşlerden birinin mesajı nedeni işaret ediyor: *"'GetTaskAsync' requires a
newer protocol revision that supports tasks (the '2026-07-28' revision or
later). The negotiated protocol version is '2025-11-25'."* Yani paralel koşumda
istemci **yanlış protokol sürümüyle** anlaşıyor; ayrı koşumda doğru sürümü
alıyor. **Bu ilk izlenim yanlış çıktı:** mesaj `MeterListener` vakasının
(K-656) sınıfına — process-wide bir durumun başka bir testin kurduğu duruma
bağlanması — benziyordu, ama SDK'yı decompile edip gerçek mekanizmayı
(`DiscoverProbeTimeout` + timeout-tetiklemeli fallback) bulunca sınıfın
FARKLI olduğu ortaya çıktı: paylaşılan durum değil, üretim için ayarlanmış kısa
bir zaman aşımı.

**Değer:** Kapanış kapısı tam çözüm koşumunda kırmızı çıkabiliyordu; bu,
gerçek bir regresyonu gizleyebilirdi.

**Risk:** Düşük — yalnız test altyapısı.

</details>


### F-170 · Store audit kapsamının tamamlanması — ✅ KAPANDI (2026-09-06)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Kaydın istediği ölçüm yapıldı:
28 store arayüzü, 8 `Auditing*` dekoratörü, 12 endpoint dosyası `AuditRecorder`'ı
doğrudan çağırıyor. Gerçek boşluk **birdi** ve kaydın işaret ettiği yerdeydi:
`IAgentSkillStore`. `PUT`/`DELETE /api/skills/{name}` denetim izine hiçbir şey
yazmıyordu — oysa kardeşi `ISkillScriptGrantStore` yazıyordu, yani iz bir
script'i kimin **çalıştırmaya izin verdiğini** kaydediyor, kimin **yazdığını**
kaydetmiyordu.

**Düzeltme:** `AuditingAgentSkillStore` (`skill.create` · `skill.update` ·
`skill.delete`). Kiracı, skill'in **kendisinden** alınır, `ITenantContext`'ten
değil — bu arayüz kiracıyı açık parametre alır. `AuditRecorder`'ın yut-ve-logla
yolunu kullanır: skill sürümlü ve geri alınabilir yapılandırmadır, script
**izni** gibi geri dönüşsüz değildir.

**Dört kayıt yeri:** Core + PostgreSql + SqlServer + Sqlite. Bir saplayıcının
`services.Replace`'i dekoratörü sessizce düşürür.

**Kapı:** `AuditCoverageTests` (YENİ) — gerçek container'dan çözüp
`IAuditDecorated` arar. Düzeltmeden **önce** kırmızıydı (ölçüldü: *"do not
resolve to an IAuditDecorated decorator: IAgentSkillStore"*). Asıl değeri
üçüncü testidir: her `I*Store` ya denetlenen ya da **gerekçesiyle** hariç
listesinde olmalı — yazıldığı gün sınıflandırılmamış iki store buldu
(`IConversationBranchStore` → endpoint-audited, `IVoiceSessionStore` →
machine-written).

**Tuzak:** `docs/hafiza/aspnetcore-di.md`.


### F-180 · Tam paket koşumunda E2E zaman aşımı — ⚠️ VAKA KAPANDI, SINIF AÇIK (2026-09-04)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. **Yalıtım kusuru değildi —
sevk edilen bir ürün kusuruydu** (K-660).

> 🚨 **2026-09-04 · YENİDEN DÜŞTÜ.** Süreç denetiminin taban koşumunda aynı
> test aynı imzayla düştü (`Timeout 30000ms exceeded ... voice-transcript`);
> izole koşum **1/1, 2,5 sn**. K-660'ın düzeltmesi HEAD'dedir ve kapattığı iki
> ürün yolu gerçektir — ama kaydı AÇAN repro (yük altındaki tam paket koşumu)
> kapanışta tekrar koşulmadı. Bu, `test-yalitimi.md`'nin YEDİNCİ vakasıdır.
> Ölçüm ve ders: [`kesif/2026-09-04-surec-denetimi.md`](../kesif/2026-09-04-surec-denetimi.md).
>
> ✅ **DÜZELTME SONRASI İLK YÜK ALTI KOŞUM YEŞİL (2026-09-05).** Kaydın eksik
> dediği kanıt budur. MAF 1.20.0 yükseltmesinin kapanış kapısında tam paket
> koşuldu (`dotnet test AgentPrism.slnx -c Release --no-build -maxcpucount:1`,
> 21 proje, 6161 test, 0 düşen) ve
> `Playground_voice_mode_opens_microphone_and_shows_transcript` **geçti —
> TRX süresi 00:00:01.49**, E2E 58/58. Karşılaştırma: kaydın kendi yük altı
> geçme tabanı **2,58 sn**, düşme imzası ise 30 sn'lik `voice-transcript`
> beklemesiydi. Süre tabanın da altına indi; bu, K-660'ın `idle` çerçevesinin
> gerçekten geldiğiyle tutarlıdır.
>
> 🚨 **Sınıf yine de KAPANMADI.** Kayıt kusuru "gerçek ama seyrek" diye
> niteliyor ve düzeltme ÖNCESİNDE de üç yük altı koşum yeşil gelmişti. Bir
> koşum bir düzeltmeyi doğrular, seyrek bir sınıfı kapatmaz. Fark şudur:
> önceki üç yeşil koşum K-660'tan ÖNCEYDİ, bu ilk sonrasıdır. **Kapanış
> ölçütü:** düzeltme sonrası ard arda üç yük altı tam paket koşumunun üçünde de
> yeşil. Sayaç bugün **1/3**'tür.

Aşağıdaki teşhis anlatısı doğru daralmıştı ("kayıp olay commit sonrasındadır");
eksik olan tek şey sunucunun o yolda hiçbir çerçeve göndermediğiydi.

**Kök neden:** `VoiceConversationDriver.Commit`, ses gelmeden ulaşan bir
`commit`'te `FinishTurn(counted:false)` çağırıp **hiçbir çerçeve göndermeden**
dönüyordu. İstemci ise gönder'e basıldığı anda kendini `'thinking'`e alıp
kaydediciyi durdurur — panel kalıcı asılır, mikrofon bir daha açılmaz. Kaydedicinin
ilk 250 ms'lik dilimi içinde commit etmek bu yola girer; tam paket yükü o pencereyi
genişletiyordu, sebebi o değildi.

**Sınıf taraması ikinci vakayı buldu** (üretimde daha olası): transcriber boş metin
döndüğünde `ProcessTurnAsync` aynı sessiz dönüşü yapıyordu ve istemci boş
`transcript` çerçevesini zaten yok sayıyor. İkisi de yeni `idle` sunucu
çerçevesiyle kapatıldı, ikisi de red→green kanıtlandı
(`VoiceConversationTests`). Mevcut `Commit_without_audio_does_NOT_produce_a_turn`
testi bunu göremiyordu: yalnız **sunucunun** dinlemeye döndüğünü ölçüyordu.

Tuzak `docs/hafiza/ses-ve-konusma.md`'de; sözleşme `docs-site/guides/voice.md`'de.

<details>
<summary>Kapanış öncesi teşhis anlatısı (kayıt)</summary>

**Sorun:** `AgentPrism.Ui.E2ETests.UiTests.Playground_voice_mode_opens_microphone_and_shows_transcript`
**yalnız** tam çözüm koşumunda (`dotnet test AgentPrism.slnx -c Release
--no-build -maxcpucount:1`) düşüyor: Playwright'ın `voice-transcript`
test id'sini beklerken 30 sn'lik varsayılan zaman aşımına takılıyor.

**Ölçüldü (2026-09-02, Faz 133 kapanışı):** izole koşumda **3/3 yeşil**; tüm
E2E paketi tek başına **57/57 yeşil**. Yalnız makineyi onlarca paketle
paylaşırken düşüyor. Faz 133 arayüze, ses yoluna veya playground'a
**dokunmadı** — kusur o fazın ürünü değil.

**Sınıf:** `kusur-giderme` Adım 2'nin *"tek başına geçip pakette düşen test bir
yalıtım veya kilit çakışmasıdır"* kalemi. Sessiz bırakılmaz; testi uzatmak
(zaman aşımını büyütmek) semptomu susturur, sebebi değil.

**Ek ölçüm (2026-09-02, F-181 turunda):** Ağustos'tan kalan bir tam-paket
koşum kaydı (`tests/AgentPrism.Ui.E2ETests/artifacts/repro/full-suite-before/`,
izlenmiyor) bu testi **geçerken** yakalamış. TRX'teki gerçek süre
**00:00:02.58**. Koşum logundaki `(36s)` testin süresi değil, koşumun o andaki
geçen süresidir — karıştırılmamalı.

Bu, teşhisi daraltıyor: yük altında geçtiğinde test 2,6 saniye sürüyor, ama
düştüğünde 30 saniyelik `voice-transcript` beklemesine takılıyor. Aradaki fark
~12 kat. Yani sorun **yavaş ama ilerleyen** bir yol değil; bir olay **hiç
gelmiyor**. Bu, "zaman aşımını büyüt" refleksini kanıtla eler.

**Sonraki adım:** kayıp olayı ara, gecikmeyi değil — WebSocket el sıkışmasının
tamamlanıp `ready`'nin gelmemesi, ya da sahte cihazın ürettiği tondan VAD'ın
commit edilebilir bir tampon üretememesi. `UiTests.cs:259` (`voice-meter`,
20 sn) ve `:266` (`voice-commit`, 20 sn) beklemeleri düşmüyor; düşen yalnız
`:270`. Bu, el sıkışmasının tamamlandığını ve sorunun **commit sonrası
sunucu turunda** olduğunu söylüyor — bir sonraki repro turu oraya bakmalı.

**🚨 TEKRARLADI (2026-09-03, K-658 kapanış kapısı) — teşhis doğrulandı.**
`Timeout 30000ms exceeded ... waiting for GetByTestId("voice-transcript") to be
visible`, `UiTests.cs:270`. E2E 56/57. Kritik olan **hangi adımın düşmediği**:
`:259` (`voice-meter`, 20 sn) ve `:266` (`voice-commit`, 20 sn) geçti, yani
WebSocket el sıkışması tamamlandı, `ready` geldi, VAD commit edilebilir bir
tampon üretti ve düğme tıklandı. Düşen yalnız commit **sonrası** sunucu turu.
Bu, 2026-09-02'de bu kayda yazılan hipotezi doğruluyor: kayıp olay commit
sonrasındadır, el sıkışmasında değil. Kanıt:
`tests/AgentPrism.Ui.E2ETests/artifacts/repro/2026-09-03-kapanis/` (izlenmiyor).

**Sonraki adım daraldı:** commit'ten sonra sunucunun transcript üretimine kadar
olan yolu ölç — `voice-commit` tıklaması sunucuya ulaştı mı, ulaştıysa yanıt
neden gelmedi. Zaman aşımını büyütmek hâlâ yasak.

**Önceki üç koşumda tekrarlanmamıştı:** Faz 134 · Faz 135 · ve
2026-09-02'nin F-181 kapanış kapısı (`dotnet test AgentPrism.slnx -c Release
--no-build -maxcpucount:1`, E2E **57/57** yeşil, 506 sn). Kusur gerçek ama
seyrek; bir sonraki düşüşte **koşumun kendi TRX'i saklanmalıdır** — asıl eksik
kanıt, düşen koşumdaki adım sürelerinin dağılımıdır.

</details>


### F-181 · `MeterListener` yalıtımı — ✅ KAPANDI (2026-09-02)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Sınıf taraması **dört** vaka
buldu (kayıtta bir tane vardı):

| Dosya | Durum |
|---|---|
| `AspNetCore.FunctionalTests/JobMetricEndToEndTests.cs` | Faz 134'te etiket süzgeciyle düzeltilmişti; **instance bağlamasına** yükseltildi |
| `AspNetCore.FunctionalTests/RunCostMetricEndToEndTests.cs` | F-181'in kendisi; düzeltildi |
| `Core.UnitTests/Quotas/QuotaUsageObserverTests.cs` | Kayıtta yoktu; düzeltildi |
| `Core.UnitTests/Diagnostics/JobQueueDepthGaugeTests.cs` | Kayıtta yoktu (`TestMeterFactory`'si vardı ama süzgeci isme bakıyordu); düzeltildi |

**Çözüm:** Etiketle süzmek yerine `Meter` **instance**'ına bağlanma. Etiket
süzgeci, hangi testlerin çakışabileceğine dair düzyazı bir akıl yürütmeye
dayanıyordu; yeni bir test onu sessizce geçersiz kılabilirdi. Dinlenen her tip
`IMeterFactory` alıyor ve `AgentPrismTestHost.StartAsync` servis kaydına izin
veriyor — izolasyon dört vakanın dördünde de mümkündü.

**Kapı:** `MeterListenerIsolationTests` (sıfır tolerans, taban çizgisi yok).
Düzeltmeden **önce** kırmızıydı (dört vaka), sonra yeşil. Tarayıcı yorumları
ayıklar: tuzağı XML dokümanında ANLATAN `ObservabilityTests` cezalandırılmaz.

**Karar:** K-656. **Tuzak:** `docs/hafiza/test-altyapisi.md`.


### F-211 · `kind` eşleşmesinin büyük/küçük harf asimetrisi — ✅ KAPANDI (2026-09-07)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Kayıt asimetriyi doğru tarif ediyordu ama **eval yarısını eksik ölçmüştü**: `EvalCheckRegistry`'de yerleşik gölgeleme guard'ı hiç yoktu, yani `"nonempty"` kaydedilip **kullanılabiliyordu** ve `"nonEmpty"` ile aynı suite'te iki ayrı şey demekti — loop defterinin prose'unda açıkça reddettiği durum. Düzeltme iki defterde de `Canonical(kind)` + eval defterine loop defterinin guard'ı. Aynı hata mesajında ikinci kusur çıktı: ikinci yarısı interpolasyonsuzdu ve tüketiciye düz `{kind}` basıyordu. **Kapı:** `LoopEvaluatorRegistryTests` + `EvalCheckRegistryTests`, iki yönlü (harf varyantı ÇÖZÜLÜR · bilinmeyen ad HÂLÂ reddedilir). Düzeltmeden önce 8 test kırmızıydı. **Karar:** K-703. **Tuzak:** `docs/hafiza/aspnetcore-di.md`.


### F-212 · Olay payload'ının `RecordToolPayloads` şartı sevk edilen metinde eksik — ✅ KAPANDI (2026-09-07)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Ölçüm: 32 üyenin **~18'i** payload iddiası taşıyor, şartı yalnız **3'ü** anıyordu. 🚨 Kayıtta olmayan **ters yön** daha ağırdı: `WorkflowRequest`'in payload'ı yazıcıda bilerek **her zaman** yazılır (bekleyen insan isteği yalnız oradan okunur) ama üye dokümanı bu muafiyeti hiç söylemiyordu — genel kuralı uygulayan okuyucu yanlış sonuca varırdı. Kaydın iki seçeneğinden **birincisi** seçildi (kullanıcı kararı): şart bir kez tip düzeyi `<remarks>`'ta, üç üyedeki tekrar kaldırıldı, `WorkflowRequest` ve reserved-prefix `Custom` muafiyet olarak adlandırıldı. **Kapı:** `RunEventPayloadSuppressionTests` (YENİ, üç test). Asıl değeri ikincisidir: yazıcının muafiyet kümesi ile dokümandaki küme **eşit** olmalı. Yazıcıya `RunCompleted` muafiyeti eklenerek kırmızı olduğu ölçüldü. Üçüncü test tekrarı yasaklar ve yazıldığı gün üç ihlal buldu. **Tuzak:** `docs/hafiza/dokumantasyon.md`.


### F-215 · SQL Server'ın `run_scores` upsert'ü yarışıyor ve `NULL`/`''` semantiği kardeşlerinden sapıyor — ✅ KAPANDI (2026-09-07)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Önerilen çözüm (`message_key AS ISNULL(message_id, N'') PERSISTED`
+ index taşıma) [`0037_run_score_message_key.sql`](../../src/AgentPrism.SqlServer/Migrations/0037_run_score_message_key.sql)
ile uygulandı; migration mevcut `NULL`/`''` çiftlerini `0025_provider_name_case.sql`
emsaliyle dedupe eder (en yeni `created_at` kalır). **Boşluk kaydın önerdiğinden
büyüktü:** sargable predicate tek başına YETMEDİ — tam paket koşumunda (izole
koşumda değil) `Concurrent_writes_with_null_and_empty_MessageId_leave_exactly_one_row`
canlı bir `SqlException: Cannot insert duplicate key row` fırlattı, çünkü
`UPDATE`-sonra-`INSERT` deseninin (K-177) dar bir yarış penceresi index
sargable olsa bile kapanmıyor — repodaki her diğer iki dallı upsert
(`SqlEvalStore.AddCaseAsync`, `SqlAuditLog.WriteAsync`) bu yüzden
`Dialect.IsUniqueViolation` ile yeniden dener, `SqlRunScoreStore.UpsertAsync`
bu deseni hiç taşımıyordu. Beşe kadar yeniden deneme eklendi. **Sınıf
taraması** üç kardeş bulguyu (`quotas.agent_name`, `skill_script_grants.script_name`,
`tool_approval_rules.{agent_name,arguments_hash,conditions_hash}`) aynı
ISNULL-sarmalı/ham-sütun deseniyle işaretledi, ama 32 eşzamanlı doğrudan-analog
denemesiyle DOĞRULANDI ki hiçbiri canlı yarışı üretmiyor — ayırt edici etken
`run_scores`'un indeksinin **filtreli** olması (`WHERE author IS NOT NULL`);
üç kardeşin indeksi filtresiz düz `UNIQUE` ve SQL Server o durumda kilidi doğru
alıyor görünüyor. Üçü de düzeltilmedi — kanıtlanmış bir kusur değil, ölçülüp
kapatılmış bir sanı. **Kapı:** `RunScoreStoreContract`'a **iki** yeni test —
sıralı çağrı zaten `ISNULL` eşleşmesiyle toplandığı için (`Score_with_no_message_id_belongs_to_the_whole_run`
gibi testler bunu kanıtlamaz) ikisi de **eşzamanlı**: orijinal repro
(`Concurrent_writes_of_the_SAME_name_leave_exactly_one_row`, 3/3 izole koşumda
düşüyordu, kayıt 2/3 diyordu) ve yeni `null`/`''` varyantı (3/3 düşüyordu).
Düzeltmeden sonra SQL Server tam paketi (703/703) iki ayrı koşumda, Postgres
(768/768) ve SQLite (712/712) yeşil. Karar: **K-724**. **Tuzak:**
`docs/hafiza/sql-server-tuzaklari.md`.

---


### F-214 · Deneylerde boş-string `MessageId` run seviyesi ortalamayı bozabilir — ✅ KAPANDI (2026-09-07)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Boşluk kaydın söylediğinden **bir yer büyüktü**: kayıt üç SQL sorgusu + `FileRunStore.cs` diyordu, ölçüm beşinci yeri buldu — [`InMemoryRunStore.Analytics.cs:57`](../../src/AgentPrism.Core/Storage/InMemoryRunStore.Analytics.cs#L57) de `score.MessageId is null` kullanıyordu, yani bellek içi deney sonucu da aynı skoru dışlıyordu. Beşi birden `is not { Length: > 0 }` / `(message_id IS NULL OR message_id = '')` oldu (SQL Server'da `N''`). **Kapı:** `RunStoreContract`'a **iki yönlü** bir çift test — bir tanesi yetmezdi, çünkü "her skoru run seviyesi say" diyen aşırı düzeltme de tek testi yeşil geçerdi. Sözleşme skor store'unu yeni `CreateScoreStoreAsync()` kancasıyla ister (`virtual`, `abstract` değil: sevk edilen sözleşmeyi türeten üçüncü tarafı kırmamak için) ve beş fixture'ın hepsi override eder. Düzeltmeden önce bellek içi ve SQLite'ta kırmızı olduğu ölçüldü (*"should be 80d but was null"*). Karar açılmadı: sevk edilen `RunScore.MessageId` sözleşmesi **zaten** *"if empty, the score belongs to the whole run"* diyordu; kod sözleşmeye hizalandı. **Tuzak:** `docs/hafiza/sql-saglayicilari.md`.



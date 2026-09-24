# Ertelenen ve Kapatılan Adaylar

> `ADAYLAR.md`'den taşındı (2026-08-16, Faz 58.1): bunlar artık
> **aday değildir**. Ölçüm kanıtı korunmuştur; yalnız grep'lenir.

---

### F-72 · Agent Control Specification (ACS) uyumu — ÖLÇÜLDÜ, ERTELENDİ (2026-08-06)

> **Dalga 3'e seçildi, plana dönüşmedi.** Kullanıcı kararı: ertelensin.
> Aşağıdaki kanıt **ölçülmüştür**; sonraki oturum ölçümü tekrarlamak zorunda
> değildir, yalnız tarihini denetler.

**Sorun:** Tracon bir kontrol düzlemidir ama kontrol kuralları **kendi
biçiminde** yaşar: onay kuralları `tool_approval_rules`, kota `quotas`, rol
politikaları kodda. Microsoft 2026-06-02'de bunun için açık bir standart
yayımladı.
**Kapsam:** ACS bildirimini okuyan bir politika değerlendirici; kesişim
noktalarının Tracon dekoratör zincirine eşlenmesi (kayıt 0 → telemetri 10
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
alır. Adaptör bir **şekildir**, hazır bir köprü değil; Tracon yine de
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
(`Tracon.AgentControl`), K-185/K-209/K-212 deseniyle — native ağırlık
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
> HTTP 200). Düzeltme `samples/Tracon.Api.csproj` içindedir ve K-185'i
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
`configureApp`'ten geçirilen bir middleware ilk `/tracon/mcp` isteğini
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

**Sorun:** `dotnet test Tracon.slnx` (tüm çözüm birlikte) koşumunda üç MCP
Tasks testi düşüyor: `McpTasksEndpointTests.Unknown_task_id_is_reported_as_a_protocol_error_not_a_500`,
`McpTaskCrossInstanceTests.Second_instance_reconstructs_a_completed_task_from_the_shared_database`,
`McpTaskCrossInstanceTests.Second_instance_reconstructs_an_approval_rejection_generically_not_with_todays_exact_wording`.

**Ölçüm (2026-09-04):** Yalıtım sorunudur, regresyon değildir.

| Koşum | Sonuç |
|---|---|
| Tüm çözüm (`Tracon.slnx`) | ❌ 3 düştü / 766 |
| Yalnız `Tracon.AspNetCore.FunctionalTests` derlemesi | ✅ 766/766 |
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
> koşuldu (`dotnet test Tracon.slnx -c Release --no-build -maxcpucount:1`,
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

**Sorun:** `Tracon.Ui.E2ETests.UiTests.Playground_voice_mode_opens_microphone_and_shows_transcript`
**yalnız** tam çözüm koşumunda (`dotnet test Tracon.slnx -c Release
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
koşum kaydı (`tests/Tracon.Ui.E2ETests/artifacts/repro/full-suite-before/`,
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
`tests/Tracon.Ui.E2ETests/artifacts/repro/2026-09-03-kapanis/` (izlenmiyor).

**Sonraki adım daraldı:** commit'ten sonra sunucunun transcript üretimine kadar
olan yolu ölç — `voice-commit` tıklaması sunucuya ulaştı mı, ulaştıysa yanıt
neden gelmedi. Zaman aşımını büyütmek hâlâ yasak.

**Önceki üç koşumda tekrarlanmamıştı:** Faz 134 · Faz 135 · ve
2026-09-02'nin F-181 kapanış kapısı (`dotnet test Tracon.slnx -c Release
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
`IMeterFactory` alıyor ve `TraconTestHost.StartAsync` servis kaydına izin
veriyor — izolasyon dört vakanın dördünde de mümkündü.

**Kapı:** `MeterListenerIsolationTests` (sıfır tolerans, taban çizgisi yok).
Düzeltmeden **önce** kırmızıydı (dört vaka), sonra yeşil. Tarayıcı yorumları
ayıklar: tuzağı XML dokümanında ANLATAN `ObservabilityTests` cezalandırılmaz.

**Karar:** K-656. **Tuzak:** `docs/hafiza/test-altyapisi.md`.


### F-211 · `kind` eşleşmesinin büyük/küçük harf asimetrisi — ✅ KAPANDI (2026-09-07)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Kayıt asimetriyi doğru tarif ediyordu ama **eval yarısını eksik ölçmüştü**: `EvalCheckRegistry`'de yerleşik gölgeleme guard'ı hiç yoktu, yani `"nonempty"` kaydedilip **kullanılabiliyordu** ve `"nonEmpty"` ile aynı suite'te iki ayrı şey demekti — loop defterinin prose'unda açıkça reddettiği durum. Düzeltme iki defterde de `Canonical(kind)` + eval defterine loop defterinin guard'ı. Aynı hata mesajında ikinci kusur çıktı: ikinci yarısı interpolasyonsuzdu ve tüketiciye düz `{kind}` basıyordu. **Kapı:** `LoopEvaluatorRegistryTests` + `EvalCheckRegistryTests`, iki yönlü (harf varyantı ÇÖZÜLÜR · bilinmeyen ad HÂLÂ reddedilir). Düzeltmeden önce 8 test kırmızıydı. **Karar:** K-783. **Tuzak:** `docs/hafiza/aspnetcore-di.md`.


### F-212 · Olay payload'ının `RecordToolPayloads` şartı sevk edilen metinde eksik — ✅ KAPANDI (2026-09-07)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Ölçüm: 32 üyenin **~18'i** payload iddiası taşıyor, şartı yalnız **3'ü** anıyordu. 🚨 Kayıtta olmayan **ters yön** daha ağırdı: `WorkflowRequest`'in payload'ı yazıcıda bilerek **her zaman** yazılır (bekleyen insan isteği yalnız oradan okunur) ama üye dokümanı bu muafiyeti hiç söylemiyordu — genel kuralı uygulayan okuyucu yanlış sonuca varırdı. Kaydın iki seçeneğinden **birincisi** seçildi (kullanıcı kararı): şart bir kez tip düzeyi `<remarks>`'ta, üç üyedeki tekrar kaldırıldı, `WorkflowRequest` ve reserved-prefix `Custom` muafiyet olarak adlandırıldı. **Kapı:** `RunEventPayloadSuppressionTests` (YENİ, üç test). Asıl değeri ikincisidir: yazıcının muafiyet kümesi ile dokümandaki küme **eşit** olmalı. Yazıcıya `RunCompleted` muafiyeti eklenerek kırmızı olduğu ölçüldü. Üçüncü test tekrarı yasaklar ve yazıldığı gün üç ihlal buldu. **Tuzak:** `docs/hafiza/dokumantasyon.md`.


### F-215 · SQL Server'ın `run_scores` upsert'ü yarışıyor ve `NULL`/`''` semantiği kardeşlerinden sapıyor — ✅ KAPANDI (2026-09-07)

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Önerilen çözüm (`message_key AS ISNULL(message_id, N'') PERSISTED`
+ index taşıma) [`0037_run_score_message_key.sql`](../../src/Tracon.SqlServer/Migrations/0037_run_score_message_key.sql)
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

**Kapanış:** `kusur-giderme` faz dışı koşuldu. Boşluk kaydın söylediğinden **bir yer büyüktü**: kayıt üç SQL sorgusu + `FileRunStore.cs` diyordu, ölçüm beşinci yeri buldu — [`InMemoryRunStore.Analytics.cs:57`](../../src/Tracon.Core/Storage/InMemoryRunStore.Analytics.cs#L57) de `score.MessageId is null` kullanıyordu, yani bellek içi deney sonucu da aynı skoru dışlıyordu. Beşi birden `is not { Length: > 0 }` / `(message_id IS NULL OR message_id = '')` oldu (SQL Server'da `N''`). **Kapı:** `RunStoreContract`'a **iki yönlü** bir çift test — bir tanesi yetmezdi, çünkü "her skoru run seviyesi say" diyen aşırı düzeltme de tek testi yeşil geçerdi. Sözleşme skor store'unu yeni `CreateScoreStoreAsync()` kancasıyla ister (`virtual`, `abstract` değil: sevk edilen sözleşmeyi türeten üçüncü tarafı kırmamak için) ve beş fixture'ın hepsi override eder. Düzeltmeden önce bellek içi ve SQLite'ta kırmızı olduğu ölçüldü (*"should be 80d but was null"*). Karar açılmadı: sevk edilen `RunScore.MessageId` sözleşmesi **zaten** *"if empty, the score belongs to the whole run"* diyordu; kod sözleşmeye hizalandı. **Tuzak:** `docs/hafiza/sql-saglayicilari.md`.

---

## ADAYLAR.md'den taşınan kapanmış kayıtlar (2026-09-13)

> Normalizasyon turu. Bu kalemler `ADAYLAR.md` § *Bekleyen Kalemler*
> tablosunda ✅ işaretiyle duruyordu; kapanmış bir kayıt aday dosyasında
> yaşamaz.

### F-203 · F-204 · F-206 — ✅ KAPANDI (2026-09-06), `kusur-giderme` faz dışı

Üçü de tablo satırı olarak taşındı; sütunlar: kalem · kapanış kaydı.

| Kalem | Kapanış |
|---|---|
| **F-203** | ✅ **KAPANDI (2026-09-06)** — `kusur-giderme` faz dışı. 🚨 Kaydın öncülü YANLIŞTI: hedef `docs-site/src/content/docs/http-api.md` **izleniyor** ve `.gitignore`'da değil; gitignore'lu olan `http-api/` **dizinidir**. Gerçek kusur farklıydı ve tarihe karşı ölçüldü: o sayfa API'nin elle yazılmış **şeklidir** (kimlik doğrulama, akış, sayfalama, hata gövdesi) ve bir uç eklenmesi onu değiştirmez, yani kural son 40 commit'te **7 kez tetiklendi, 5'i kırmızı** döndü ve hepsi `--site-gerekce-yazildi` ile geçildi — sürekli kırmızı bir kapı insanları onu susturmaya eğitir. Kaydın önerdiği **çözüm** yine de doğruydu: `docs/openapi/tracon.json` (üretilen ama izlenen ve commit edilen) alternatif hedef olarak eklendi ve `docs/` ile başlayan hedef artık depo köküne göre çözülür. Aynı tarihte yeniden ölçüldü: **5 kırmızı → 3**. Kalan üçü uç dosyasının değişip HTTP yüzeyinin değişmediği commit'lerdir (XML yorum düzeltmesi, MAF yükseltmesi) — gerekçe yazma yolu tam olarak onlar içindir. Kapı: `dokuman_bakim_test.py`'a dört test (depo kökü hedefinin site kökü altında ARANMADIĞI dahil). Tuzak: `docs/hafiza/dokumantasyon.md` |
| **F-204** | ✅ **KAPANDI (2026-09-06)** — `kusur-giderme` faz dışı. Boşluk kaydın söylediğinden **büyüktü**: kayıt yalnız `OpenAIConversationsEndpoints.cs`'in üç çağrısını anıyordu, ölçüm `RunEndpoints.cs`'in **12** `CheckRunResourceAsync` çağrısı taşıdığını buldu — on birinin silinmesi kapıyı yeşil bırakırdı. Kapı varlıktan (`IsMatch`) **tam sayı eşitliğine** çevrildi (kullanıcı kararı); taban değil, çünkü taban bir eklemenin bir silmeyi ödemesine ve net sıfırda sessiz geçmesine izin verirdi. 22 (dosya, marker) çiftinin sayısı ölçülüp yazıldı. Düzeltmeden **önce** kırmızı olduğu kanıtlandı: bir `CheckSessionAsync` çağrısı silinince *"expected 3, found 2"* — eski kapı bunu göremiyordu. Taramanın kendi regresyon testi de sayma davranışını kanıtlar. Tuzak: `docs/hafiza/test-altyapisi.md` |
| **F-206** | ✅ **KAPANDI (2026-09-06)** — `kusur-giderme` faz dışı. Kapanış turunun kendi kapı koşumunda bulundu: `denetim-paketi.py:17`'nin test tiyatrosu tarayıcısı `\bShould\b` arıyordu ve bu depodaki **7488** Shouldly iddiasının **hiçbirini** eşleştirmiyordu (`Should`'dan sonra kelime karakteri gelir, `\b` sınır oluşturmaz); `Assert.` yalnız **6** yerde geçiyor. Yani tarayıcı pratikte her yeni testi aday sayıyordu — bu turda 5 yanlış pozitif, düzeltmeden sonra **0**. Çıkış kodunu kırmadığı için gürültü olarak yaşamıştı; F-203'ün sınıfı. Kök sebep testtedir: var olan tek test yalnız POZİTİF yönü ("iddiasız test yakalanır") kanıtlıyordu. Düzeltme `Should\w*` + **iki yönlü** üç test (Shouldly tanınır · altı biçim ayrı ayrı · gerçekten iddiasız test HÂLÂ aday). Eski regex'e karşı kırmızı olduğu ölçüldü. Tuzak: `docs/hafiza/test-altyapisi.md` |

### F-219 — ✅ KAPANDI (2026-09-12)

### F-219 · ✅ KAPANDI (2026-09-12) — `ChildAgentInvoker` sağlayıcı zaman aşımını KENDİ deadline'ı sanıyor

**Kapanış:** `kusur-giderme` faz dışı. Kayıt doğruydu ve **dar** çıktı: ölçüm
kaydın anmadığı bir ikinci zararı buldu — yanlış metin yalnız yanlış SEBEBİ
söylemiyordu, bir sağlayıcı kesintisini `ChildRunTimedOut` **metriğine** de
yazıyordu. Repro: 30 sn'lik `ChildDeadline` için **17 ms**'de
*"did not respond in time (limit: 00:00:30)"*. Ayırma `deadline.IsCancellationRequested`
ile **tam** yapılabiliyor; ayrılamayan dal yoktur. Kullanıcı kararı: sağlayıcı
arızası dalı istisna aksıtmaz, **ayrı bir reddetme metni** döner (ağaç yaşar,
`ChildRunTimedOut` yazılmaz). Kırmızı iki seviyede ayrı ayrı kanıtlandı — metin
iddiası birimde, metrik iddiası **yalnız** fonksiyonel seviyede (birimde
`scope.Writer` yoktur, olay hiç yazılmaz, yani orada iddia tiyatrodur).

**Sınıf taraması** (`grep -rn "catch (OperationCanceledException) when (!" src/`,
13 yer) iki vaka daha buldu ve ikisi de kapatıldı:
**(2)** `ToolApprovalPresenterRunner` — `IToolApprovalPresenter` sevk edilen bir
genişleme noktasıdır; tüketicinin kendi HTTP çağrısı kendi zaman aşımını attığında
log *"timed out after 00:10:00"* diyordu (çağrı 56 ms'de dönmüştü). Fail-open
davranışı değişmedi, yalnız sebep düzeldi; genel `catch` de OCE'yi kabul edecek
şekilde genişletildi, çağıranın iptali hâlâ akıyor. **(3)** CLI — altında ayrı
bir kusur yatıyordu: `AddTraconClient`'ın `HttpClient`'ı .NET varsayılanı
**100 sn** taşıyordu, yani `tracon eval --timeout 1800` fiilen imkânsızdı ve tavan
dolduğunda CLI *"Timed out after 1800 s"* yazıyordu. Ölçüldü: **100,03 sn → 0,57 sn**.
Kullanıcı kararı: hem atfetme ayrıldı hem `TraconClientOptions.Timeout` eklendi
(additive; CLI onu `Timeout.InfiniteTimeSpan` yapar).

**Vaka DEĞİL** (tarandı, yazıldı): beş sağlayıcı sağlık kontrolü +
`ElevenLabsSpeechClient` (iki dalda da sonuç ve sebep aynı: `Unhealthy`,
*"Timed out."*) · `AgentDefinitionValidator` (iki dal da `false` döner, hiçbir
yerde sebep söylenmez) · `VoiceConversationDriver` (`socket.ReceiveAsync` OCE'yi
yalnız verilen token'dan atar; iç bir istemci yok).

Kararlar: **K-759** (sebep söyleyen her filtre kendi kaynağını sınar) ·
**K-760** (`TraconClientOptions.Timeout`). Tuzak:
[`hafiza/cekirdek-calistirma.md`](../hafiza/cekirdek-calistirma.md).

**Kaynak:** [Faz 157](fazlar/157-SINIRLI-YUK-VE-IKI-PROCESS-ARIZA-KANITI.md) denetimi — 🟢 bulgu.

**Gözlem:** `ChildAgentInvoker.cs:208,329` bir `OperationCanceledException`'ı
alt-agent'ın `ChildDeadline`'ının dolduğu varsayımıyla `TimeoutRefusal`'a
çeviriyor. K-737'den sonra biliyoruz ki bu istisna sağlayıcının KENDİ istek
zaman aşımından da gelebilir; o zaman çağırana "alt-agent süresi doldu" denir,
oysa doğru cümle "sağlayıcı yanıt vermedi"dir.

**Kapsam:** İki `catch`'i `deadline.IsCancellationRequested` ile ayırmak;
ayrılmayan durumda mevcut sınıflandırmaya düşürmek.

**Değer:** Alt-agent bekleme sınırının kendi metriği ile sağlayıcı kesintisi
karışmaz; operatör hangi kadranı büyüteceğini bilir.

**Mercek:** 3.

**Hazırlık:** K-737 kuralı ve emsal filtreler hazır.

**Maliyet:** Küçük — iki filtre, iki test.

**Risk:** Düşük. Sonuç bugün de bir zaman aşımı mesajıdır; sessiz başarı
üretmiyor, yalnız yanlış SEBEBİ söylüyor.

### F-220 — ✅ KAPANDI (2026-09-12)

### F-220 · ✅ KAPANDI (2026-09-12) — Yük raporundaki CPU/RAM alanları makineyi değil süreci anlatıyor

**Kapanış:** `kusur-giderme` faz dışı. Kayıt doğruydu ve kapsamı aynen uygulandı:
rapor artık `Processor` ve `Physical memory` satırlarını gerçek donanımdan okur
(macOS `sysctl -n machdep.cpu.brand_string`/`hw.memsize`, Linux `/proc/cpuinfo`
`model name` + `/proc/meminfo` `MemTotal`), okuyamadığı platformda satır
*"not available on this platform"* der. Süreç değerleri silinmedi — ne oldukları
adlarına yazıldı: `Logical processors (process-visible)` ·
`Available memory (process-visible)`. Okuma yolu yumuşak düşer; rapor bir kapı
değildir (K-738). Repro kendi koşullarında tekrar koşuldu (`TRACON_LOAD=1`,
gerçek Postgres container'ı) ve çıktı doğrulandı: *Apple M1 Pro · 16384 MiB*.

**Sınıf taraması:** `grep -rn "Environment.ProcessorCount\|GetGCMemoryInfo" src/ tests/`
— başka vaka yok. `Environment.MachineName`'in `SingletonGuard` ve
`JobWorkerBackgroundService`'teki kullanımları **sahip kimliğidir**, makine
kapasitesi iddiası değil. Tuzak:
[`hafiza/olcum-kota-ve-secenekler.md`](../hafiza/olcum-kota-ve-secenekler.md).

**Kaynak:** [Faz 157](fazlar/157-SINIRLI-YUK-VE-IKI-PROCESS-ARIZA-KANITI.md) denetimi — 🟢 bulgu.

**Gözlem:** Rapor "Available memory" olarak
`GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` (GC/container limiti),
"Logical processors" olarak `Environment.ProcessorCount` yazıyor. İkisi de
sürecin gördüğü değerdir; makinenin gerçek belleği ve işlemci modeli raporda
yok. İki koşumu karşılaştıran biri farkı donanıma bağlayamaz.

**Kapsam:** Platforma göre gerçek donanım bilgisini okumak (`sysctl -n
hw.model`, `/proc/cpuinfo`), bulunamazsa bugünkü değerlerle "process-visible"
etiketiyle yetinmek.

**Değer:** Rapor K-738'in vaat ettiği "ortamıyla birlikte" sözünü tam karşılar.

**Mercek:** 6.

**Hazırlık:** Rapor iskeleti hazır; yalnız alan eklenir.

**Maliyet:** Küçük.

**Risk:** Düşük — rapor bir kapı değildir (K-738), yanlış bir alan hiçbir
koşumu kırmızıya çevirmez.

### F-222 (ikinci tahsis) · `nav.*` ekran kapısı — ✅ KAPANDI (2026-09-12, Faz 164)

> 🚨 **Bu F-222, Faz 159'un F-222'si DEĞİLDİR.** Numara iki kez tahsis edildi;
> ayrıntı `ADAYLAR.md` § *Aday Olmayan Açık Kayıtlar* → ID çakışması satırı.

### F-222 · ✅ Kapandı — Faz 164

**Kaynak:** [Faz 162](fazlar/162-TRACON-YENIDEN-ADLANDIRMA.md) keşfi — faz
kapsamı dışında bırakıldı (kullanıcı kararı 2026-09-12).

**Kapanış (2026-09-12, Faz 164):** Aşağıdaki gözlem kaydedildiğinde zaten
bayattı — kapı `check-content.mjs`'ten `check-console-screens.mjs`'e taşınmış ve
`locales/en/common.ts`'i okuyordu, yani döngü dönüyordu. Ama adlandırdığı SINIF
gerçekti ve Faz 164 onun ikinci örneğini üretti: `nav.*` ad alanındaki her
anahtar bir ekran sayılıyordu, dolayısıyla ekran OLMAYAN bir anahtar
(`nav.skipToContent`) kapıyı kırmızı yapıp kendi ekran görüntüsünü istedi.
Okuma yolu artık `components/navigation.ts`'tir — kenar çubuğu ile komut
paletinin paylaştığı tek envanter. Regresyon testi:
`check-console-screens.test.mjs` içindeki "a shell affordance in the message
catalogue is not mistaken for a screen".

**Gözlem:** `docs-site/scripts/check-content.mjs:399` `nav.*` anahtarlarını
`locales/en.ts` içinde arar. O anahtarlar `locales/en/common.ts`'e taşındı;
`en.ts` bugün yalnız bir toplayıcıdır ve tek bir `'nav.…'` literal'i
taşımaz. Regex sıfır eşleşme bulur, döngü hiç dönmez ve **19 ekran
görüntüsünün varlığını hiçbir şey doğrulamaz**. Kapı yeşil görünür.

**Kapsam:** Okuma yolunu `locales/en/common.ts`'e çevirmek, sonra kapının
gerçekten döndüğünü kanıtlayan bir kayıt eklemek. `kusur-giderme` SINIF
TARAMASI adımı burada zorunludur: aynı sınıf (fragment'e taşınan bir sabiti
eski toplayıcı dosyada arayan kapı) başka kapılarda da olabilir —
`check-content.mjs` içindeki her `readFileSync(...locales...)` ve her
`nav\.`/`'[a-z]+\.` regex'i taranır.

**Değer:** Sevk edilen sayfalarda eksik veya bayat ekran görüntüsü yakalanır.

**Mercek:** 6.

**Hazırlık:** Kusur yeri ve kök nedeni ölçüldü; düzeltme tek satır, kanıt
testi ek iş.

**Maliyet:** Küçük.

**Risk:** Düşük — ama kapı canlanınca bugün eksik olan varlıklar ortaya
çıkabilir; o hâlde düzeltme kapsamı ekran görüntüsü üretimini de kapsar.

### F-221 (logo dilimi) — rutin bakıma indirildi (2026-09-13, kullanıcı kararı)

**Görsel yarı 2026-09-12'de Faz 163'te kapandı** ve aşağıdaki gövde o tarihte
bayatladı: `assets/tracon-mark.svg` tek kaynak oldu, `assets/icon.png` ile
`docs-site/public/favicon.svg` ondan üretiliyor ve ikisi de artık prizma değil
radar işareti çiziyor. Faz 164 console tarafını kapattı.

**Ölçüm (2026-09-13):** `--ap-*` CSS ad alanı `src/` ve `docs-site/` içinde
**sıfır** eşleşme veriyor; `prismMark`/`PrismMark`/"prism spectrum" de sıfır.
Kalan tek iz `src/` içinde dört dosyada **27 yerel değişken adıdır**
(`RunRecordingAgent.Lifecycle.cs` `prismOptions` ×17 ·
`RunRecordingAgent.Completion.cs` `prismException` ×2 ·
`WorkflowRunner.cs` `_prismOptions` ×6 · `WorkflowNodeRetry.cs`
`prismException` ×2). Hepsi `internal` yerel değişkendir; hiçbiri tüketici
sözleşmesi değildir.

**Karar:** Kalan iş bir faz değildir — o dosyalara dokunan ilk oturumun
düzelteceği bir yeniden adlandırmadır. Kalem adaylıktan düştü.

**Taşınan gövde (2026-09-12 tarihli, bayat):**

### F-221 · Logo ve favicon adı anlatmıyor

**Kaynak:** [Faz 162](fazlar/162-TRACON-YENIDEN-ADLANDIRMA.md) — kullanıcı kararı
(D5): görseller o fazda bilerek değiştirilmedi.

**Gözlem:** `assets/icon.png` ve `docs-site/public/favicon.svg` bir prizma
çiziyor. Prizma önceki adın görsel karşılığıydı; yeni adla hiçbir ilişkisi
yok. `assets/icon.png` NuGet'e `PackageIcon` olarak **sevk edilir**, favicon
her site sayfasında görünür.

**Kapsam:** Yeni bir işaret tasarlamak; `docs-site/scripts/build-package-icon.mjs`
ve `build-social-images.mjs` ile türevleri yeniden üretmek. Üretim hattı hazır
— eksik olan tasarımın kendisidir.

Kapsam görselle bitmiyor: eski adın METAFORU kodda da yaşıyor ve o dosyalara
zaten dokunulacak — `prismMark`/`PrismMark` (`build-social-images.mjs:59`,
`icons.tsx:30`), `_prismOptions`/`prismOptions` (`WorkflowRunner.cs`),
`prismException` (`RunRecordingAgent.Completion.cs:338`), `--ap-*` CSS ad alanı
(17 dosya) ve `styles.css` + `layout.tsx` içindeki "prism spectrum" anlatısı.
**Kısmen kapandı (2026-09-12, Faz 164):** console tarafı bitti — `--ap-*` →
`--tracon-*`, prizma şeridi ve ekran başına gökkuşağı tonu kaldırıldı,
`icons.tsx` işareti zaten `TraconMark`'tı, console favicon'u yeni işaretle
değiştirildi. Kalan: `assets/icon.png`, `docs-site/public/favicon.svg`,
`build-social-images.mjs` ve `src/` içindeki `_prismOptions`/`prismException`
adları.
Hiçbiri tüketici sözleşmesi değil (tipler `internal`), bu yüzden Faz 162'de
bırakıldı.

**Değer:** İlk yayında paket listelemesi ve site aynı markayı gösterir.

**Mercek:** 8.

**Hazırlık:** Üretim script'leri çalışır durumda; girdi dosyası değişince
türevler tek komutla çıkar.

**Maliyet:** Küçük (kod), tasarım kararı kullanıcıya ait.

**Risk:** Düşük — hiçbir kapı ikonun içeriğine bakmaz.

---

## Kapanan kalemler

> 2026-09-24'ten itibaren [`../ADAYLAR.md`](../ADAYLAR.md) yalnız yapılacak
> işi taşır (kullanıcı kararı). Kapanan her kalem o dosyadan silinir ve buraya
> **tek satırla** (tarih + kanıt) taşınır. Numara burada yaşamaya devam eder:
> `dokuman-bakim.py`'nin F-ID sayacı bu dosyayı da sayar, bu yüzden kapanan
> bir numara yeniden tahsis edilemez. Gövdeli bir kalem kapanırsa gövdesi
> tablonun altına eklenir.

| Kalem | Kapanış | Kanıt |
|---|---|---|
| **F-262** · `Tracon.Google`'ın görsel yolu deprecated Imagen `:predict` yüzeyini hedefliyordu | 2026-09-19 (kayıt 2026-09-24'te kapatıldı) | Kod `GenerateContentAsync` kullanıyor — [`GoogleImageGenerator.cs:83`](../../src/Tracon.Google/Internal/GoogleImageGenerator.cs#L83), commit `d1f539fb` (Faz 179). Satır aday dosyasına 2026-09-22'de, düzeltmeden **sonra** ve bayat olarak girmişti (F-251 çift tahsisi yeniden numaralanırken). Canlı kanıt **F-255** ile 2026-09-24'te alındı; K-835'e kapanış notu eklendi |
| **F-221** · kalan iz: `src/` içindeki `prismOptions`/`prismException` yerel değişken adları | 2026-09-24 ölçümü | `grep -rnoi "prism" src --include='*.cs'` → **0**. Görsel yarısı Faz 163–165'te kapanmıştı |
| **F-278** · `PeriodicTimer`'a giden aralıklar doğrulanmıyordu; geçersiz değer host'u açılıştan SONRA durduruyordu | 2026-09-24, kusur-giderme | Repro örnek uygulamada canlı: `Tracon__Approvals__ScanInterval=00:00:00` → "Application started" ardından `StopHost`; düzeltmeden sonra aynı komut açılışta `OptionsValidationException` ile adı veriyor. Ortak `TimerPeriod` (`src/Tracon.Core/Hosting/TimerPeriod.cs`); yeni `TraconApprovalOptionsValidator`, `CanaryOptionsValidator`, `TraconMcpOptionsValidator`; zamanlama, singleton, uzlaştırma, sağlık ve saklama doğrulayıcıları sıkılaştırıldı (özellik açıkken). Kanıt: `TimerPeriodOptionValidationTests` (önce 20 kırmızı), `McpRefreshIntervalValidationTests` (önce 4 kırmızı), kapı `PeriodicTimerSiteTests`. Timeout alt sınıfı **F-280**'e ayrıldı |
| **F-275** · `quota_usage` her run'da tüm geçmişi okuyordu; `AsOf` yok sayılıyordu | 2026-09-24, kusur-giderme | K-857: `QuotaUsageQuery.PeriodStarts` (store süzer, verilmezse tüm geçmiş) · `AsOf` `[Obsolete]`. Dört iç çağıran dönem başlarını geçer. `QuotaStoreContract`'a dört case (in-memory önce 3 kırmızı; SQLite/PostgreSQL/SQL Server yeşil); `QuotaEnforcerTests` ve `QuotaUsageObserverTests` önce kırmızı. Migration yok |
| **F-244** · `guides/coding-agents.md` diagnostic tablosu eksik sayıyordu | 2026-09-24, doğrudan | `TRC0501`/`TRC0502` satırları eklendi; sayfa artık sayı yazmıyor. Kapı: `check-content.mjs` tabloyu `UsageDiagnostics.cs` tanımlayıcılarıyla iki yönlü karşılaştırır |
| **F-255** · `Tracon.Google` görsel yolu için canlı kanıt yoktu | 2026-09-24, doğrudan | `MT-MM-121` canlı Google anahtarıyla yeniden koşuldu: `POST /api/images/generate` → `200`, `image/png` 2 063 511 B ek. `size: "1024x1024"` açık hatayla reddedildi (`502`, *"does not accept WIDTHxHEIGHT"*). `OutputMimeType` sorusu ölçüldü ve yeni kusura dönüştü: **F-281** |
| **F-251** · Damıtma, yeniden koşulan bir case'in eski bloğunu işaretsiz bırakıyordu | 2026-09-24, doğrudan | `_kosum_damit_metni` son bloğun işaretini önceki blokların boş şablon `Durum` satırına taşır; ters yön (ilk deneme geçti, yeniden koşum kaldı) artık `☑` satırına inmez; serbest metinli `Durum` satırına dokunulmaz. Arşivde 19 blok düzeltildi (`12-GOZLEMLENEBILIRLIK-MALIYET.md`). Kanıt: `KosumDamitmaTestleri` (önce 2 kırmızı, 6 yeni test) |

### ADAYLAR.md'den taşınan kanal satırları (2026-09-24)

Aday dosyasının § *Aday Olmayan Açık Kayıtlar* tablosu bu iki satırı
taşıyordu. Her ID'nin gövdesi bu dosyada veya
[`PLANA-DONUSEN-ADAYLAR.md`](PLANA-DONUSEN-ADAYLAR.md)'dedir.

| Kanal | ID'ler | Kural |
|---|---|---|
| **Kapatılan kusur kayıtları** | F-106 · F-130 · F-137 · F-138 · F-139 · F-170 · F-180 · F-181 · F-190 · F-197 · F-203 · F-204 · F-206 · F-211 · F-212 · F-214 · F-215 · F-219 · F-220 · F-222 | Yeniden görülürse **yeni** kusur kaydı açılır. **F-180**'in vakası kapandı, sınıfı açık — aday dosyasında tetik bekleyen olarak durur |
| **Arşivlendi / birleştirildi / rutin bakım** | F-48 · F-88 · F-89 · F-98 · F-144 · F-145 · F-146 · F-147 · F-148 · F-155 · F-158 · F-163 · F-221 | Plan değeri yok; rutin bakım olarak kaldı veya aktif adayla aynı tasarım işiydi. F-221'in kalan izi 2026-09-24'te sıfır ölçüldü (yukarıdaki tablo) |

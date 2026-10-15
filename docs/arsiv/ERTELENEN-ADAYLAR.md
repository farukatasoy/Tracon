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

## Tetik bekleyen gövdeler

> 2026-10-11'de `ADAYLAR.md`'den taşındı (aday dosyası 80 KB bütçesi; video tüketicisi turu).
> Kalemler **açıktır**; satırları `ADAYLAR.md` § *Tetik bekleyenler* tablosundadır.

> 2026-10-13: F-240, F-254, F-374, F-376, F-381, F-412, F-414, F-415, F-422 taşındı (Faz 197 ile Faz 198 birleştirmesi; aday dosyası bütçesi). Metin aynen.

> 2026-10-14: F-242, F-230, F-413, F-375, F-205, F-277, F-377 taşındı (Faz 208 kapanışı; aday dosyası bütçesi). Metin aynen; `ADAYLAR.md` satırı kısaldı.

### F-242 · `ResolveVersion`'ın `catch` dalı kanıtlanmadı

**Engel:** Sürüm çözümünde attribute okuması **hata verirse** alan `null` kalır ve skor yine yazılır (K-790). Yalnız "attribute yok" yolu testle kanıtlı; `catch` dalını koşturmak `GetCustomAttribute`'u attıran düşmanca bir tip ister ve kazanç maliyeti karşılamıyor

**Koşul:** Assembly attribute okumasının gerçekten attığı bir kurulum (kısıtlı host, bozuk assembly) raporlanırsa ([Faz 176](fazlar/176-EVALUATOR-SURUM-DAMGASI.md) denetim bulgusu 🟢 2)

### F-230 · `kurtarma.md` ↔ `.claude/settings.json` senkron kapısı

**Engel:** `KR-11` rampası `deny` listesinin bugünkü içeriğini **sayarak** tekrarlıyor (`git rebase`, `git clean -fd`, `rm -rf` listede yok). Yasağın sınırını yazmayan bir rampa yanlış güven üretir (K-761). Ama `settings.json` genişlerse cümle sessizce yalan olur ve bunu sayan kapı yok

**Koşul:** `.claude/settings.json` `deny` bloğu ilk kez değiştiğinde ([Faz 168](fazlar/168-KURTARMA-RAMPASI-KATALOGU.md) denetim bulgusu 🟢 6)

### F-413 · Konsol yapısal değişiklik uyarısını bekleyen run olmadan da gösteriyor

**Engel:** Faz 195 (K-901, Açık Soru 3-A): uyarı desen, adım veya sıra her değiştiğinde çıkar; hepsi ölçüldü ve eski checkpoint'i bozar (`WorkflowStructuralChangeResumeTests`). Yalnız bekleyen `AwaitingInput` run varken göstermek bir sayım çağrısı ister (ek uç ya da run listesinde workflow süzgeci)

**Koşul:** Operatörler uyarıyı gürültü sayarsa ya da run listesi workflow adına göre süzülür hâle gelirse

### F-375 · Batch öğesinde düz metin ile `{"message":…}` biçimli nesne ayırt edilemiyor

**Engel:** Faz 194 (K-899): `JobPayload.ExtractItems` string ve nesne öğeyi aynı metne indirir; metin tür taşımadığı için `message` string üyeli bir JSON metni yapılı öğe sayılır (site belgeler). Tür sütunu migration ister ve gerçek çakışma ölçülmedi

**Koşul:** Bir tüketici düz metin öğesinin yanlışlıkla yapılı sayıldığını bildirirse — o zaman `job_items`'a tür işareti eklenir

### F-205 · `/v1/conversations/{id}` varlık asimetrisi

**Engel:** Kullanılmamış kimlik `200`, reddedilen kimlik `404`. Katı modda çağıran hangi id'lerin sahipsiz SATIR olduğunu sayabilir — erişim kapalı, yalnız varlık görünür. Davranış ucun rezervasyon semantiğinden gelir; kapatmak OpenAI uyumluluğunu bozar. `/api/sessions/{id}` bu sızıntıyı taşımaz

**Koşul:** Tüketici varlık gizliliği talep ederse ([Faz 149](fazlar/149-SAHIPSIZ-OTURUMUN-KATI-REDDI.md) denetim bulgusu)

### F-277 · Dış katkıcı için İngilizce mimari karar özeti yok

**Engel:** `ARCHITECTURE.md:59`, `:137-146` ve `CONTRIBUTING.md:114-116` dört Türkçe dokümana işaret eder; iki dosya da makine çevirisinin yeterli olduğunu söyler. K-408 (`docs/` Türkçe kalır, kullanıcı kararı) korunur

**Koşul:** İlk dış PR veya issue mimari kararı Türkçe dokümandan okumak zorunda kalırsa ya da GA freeze turu (UR-003)

### F-377 · `ReplayTools` kipiyle parametreli tanımın replay'i için test yok

**Engel:** Faz 194 denetimi (🟢): yeni iç `CompileParameterizedAsync(…, toolTransform, …)` aşırı yüklemesi yalnız `NoTools` ve playback'li devam testinde dolaylı sınanıyor; gövde parametresiz yolla aynıdır

**Koşul:** `AgentDefinitionCompiler` araç dönüşümü ya da replay tool kipleri değişirse

### F-422 · `openai-responses` harness turu cevabını akışta iki kez yayabilir — ölçüm bekliyor

**Engel:** [Faz 201](fazlar/201-LOOP-DUZENLEME-VE-OLCUT-FABRIKASI.md) örnek uygulama koşumu (2026-10-13, `gpt-5.4-mini`): `aiJudge` + kod ölçütlü loop'lu agent'ın üç koşumunun ikisinde ilk turun metni `MessageDelta` dizisinde token token iki kopya; arada tool çağrısı ya da başka olay yok. Loop'suz harness 0/3. Modelin turda iki mesaj yazması mı, MAF/MEAI'nin aynı içeriği iki kez yayması mı ayırt edilmedi. Tracon tarafı: ölçüt deseni cevabı son mesajdan okur (`hafiza/maf-harness-ve-loop.md`)

**Koşul:** Akışta yinelenen metin bildirilirse ya da `openai-responses` istemcisi güncellenirse — o zaman aynı tanımla N koşum: ham Responses olayları (sağlayıcı istemcisi düzeyi) ile `AgentResponse.Messages` sayısı karşılaştırılır; tekrar MEAI/MAF'ta ise kusur kaydına döner, modeldeyse kapanır

### F-414 · Konsol 2^53 − 1'den geniş bir tamsayı ayarını kayıpsız taşıyamıyor

**Engel:** Faz 195 denetimi (🟡, gerekçelendi): konsol JSON sayısını `double` okur; `9007199254740993` açılıp kaydedilince `…992` olur, ayar tipi `NumberHandling.Strict` olduğundan `long` üyeye metin de yazılamaz. Sınır XML'de ve sitede yazılı: bu genişlikteki kimlik `string` bildirilir. Kayıpsız taşıma özel bir JSON ayrıştırıcısı ve yazıcısı ister (`JSON.parse` kaynak metni her tarayıcıda yok)

**Koşul:** Bir tüketici 53 bitten geniş sayısal ayar bildirirse — o zaman ya ayarı ham metin olarak taşıyan bir editör ya da tarayıcı `JSON.parse` kaynak desteği oturunca onun kullanımı

### F-415 · Tüketicinin dönüştürücüsünün (`JsonConverter`) fırlattığı `JsonException` mesajı `400`'e ve `runs.error`'a girer

**Engel:** Faz 195 denetimi (🟢): setter ve kurucu istisnasının mesajı yanıta girmez (K-900), ama tüketicinin kendi `JsonConverter`'ı `JsonException` ya da `NotSupportedException` fırlatırsa mesaj serileştiricinin cümlesi gibi aktarılır ve değeri taşıyabilir. Kayıt yolunda uyarı logu da yazılmaz (`WorkflowFunctionSettings.Deserialize`, `IsRaisedByConsumerCode`)

**Koşul:** Bir tüketici ayar tipinde özel dönüştürücü kullanır ve mesajının değer taşıdığı görülürse — o zaman iki istisna türü de setter istisnası gibi ele alınır

### F-412 · Fonksiyon düğümünün ayar alanı şemadan üretilen bir forma dönüşmedi

**Engel:** [Faz 195](fazlar/195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md) (K-900, kullanıcı kararı): konsol bir JSON metin alanı ve şemanın üye listesini verir. Tür başına bir form (enum seçici, sayı alanı, iç içe nesne) şema yorumlayıcısı ister; bundle payı (199,9 KB, bütçe 250) ölçülmedi. Doğrulama sunucudadır, form bir kopya taşımaz

**Koşul:** Bir operatör JSON'u elle yazarken tekrarlanan hata bildirirse ya da ayar tipleri iç içe nesne ve dizi üyeleri taşımaya başlarsa — o zaman `settingsSchema`'dan üretilen form ve bundle payı birlikte ölçülür

### F-374 · `jobs.payload` ve `job_items.input` korumalı sütun değil; kuyruklu run'ın parametre değeri ve belge metni orada düz durur

**Engel:** [Faz 194](fazlar/194-WORKFLOW-VE-KUYRUK-PARAMETRELERI.md) (K-899, kullanıcı kararı): kuyruk yükünde mesaj zaten düz duruyordu; sütunu koruma kapsamına almak ayrı bir migration ve anahtar kararıdır (`jobs.payload` serbest metin, `ProtectedColumn` üyesi yok). Site bunu açıkça yazar

**Koşul:** Düzenlemeye tabi bir kurulum kuyruk yükünün şifrelenmesini isterse — o zaman `ProtectedColumn` üyesi, iki sütun ve geriye dönük okuma (zarf kendini tanıtır) birlikte tasarlanır

### F-240 · Kapasite kapısının kalan üç dar açığı

**Engel:** [Faz 174](fazlar/174-KAPASITE-DAMGASI-KAPISI.md) denetiminin 🟢 bulguları; (3) ve (4) 2026-09-24'te kapandı. Kalan: (1) `SCHEMAS.storage` ölçülmüş tekrar sayısını başlık dizesinde sabitliyor (`'Rows per run (3 repeats)'`); (2) `P95_SAMPLE_FLOOR = 100` ile `LatencyStatistics.P95SampleFloor` elle senkron, uyumu hiçbir şey ölçmüyor; (5) 19 işaret `docs-site/public/llms-full.txt`'e düz metin olarak sızıyor (emsal `claim:` zaten 11 tane sızdırıyor)

**Koşul:** Kapasite ölçümü yenilendiğinde — o koşum (1) ve (2)'yi zaten elden geçirtir

### F-376 · Paylaşılan boş parametre sözlükleri `Dictionary` tabanlı ve `GetRunParameters()` onları ya da kapsamın canlı sözlüğünü döner

**Engel:** [Faz 194](fazlar/194-WORKFLOW-VE-KUYRUK-PARAMETRELERI.md) denetimi (🟢): `ParameterValueSet.Empty` ve `TraconWorkflowContextExtensions.NoValues` salt okunur görünür; `(Dictionary<,>)` dönüşümüyle yazan kod run'lar ve kiracılar arasında değeri bozar. Barındıran kod güvenilirdir; `ReadOnlyDictionary` sarmalı ucuzdur

**Koşul:** Bir tüketici sözlüğü dönüştürerek yazdığını bildirirse ya da `GetRunParameters()` yüzeyi genişlerse

### F-254 · Kiracı egress politikası satırı YOKKEN hiçbir kısıt uygulanmıyor (fail-open)

**Engel:** [`ModelProviderRegistry.cs:287`](../../src/Tracon.Core/Models/ModelProviderRegistry.cs#L287) `if (policy is not null)`. `policy is null ⇒ kısıt yok` ürünün belgelenmiş varsayılanıdır — politika kurmayan kurulum her sağlayıcıyı çağırabilir. Fail-closed yapmak `TraconTenantProviderOptions`'a varsayılanı kapalı bir anahtar ister (K1 sıfır-sürpriz; KG-034)

**Koşul:** Kullanıcı fail-closed seçeneğine karar verirse — **kullanıcı kararı gerekir** (güvenlik varsayılanı)

### F-381 · Workflow `run_inputs` yazımı düşerse bekleyen run kalıcı olarak cevaplanamaz

**Engel:** Faz 194 denetimi (🟡, gerekçelendi): yazım best-effort kalır (K-898); ret mesajı artık depo hatasını adlandırır. `AwaitingInput`'a geçerken kayıt yoksa idempotent yeniden yazmak (`ON CONFLICT DO NOTHING` buna izin verir) run'ı kurtarırdı; kurtarma yolu yoktur ve satırı yaşa göre silen kural da yoktur

**Koşul:** Depo hatası nedeniyle cevaplanamayan bir workflow run'ı görülürse (`tracon.run_recording.failures{stage=input}` artar)

### F-95 · Agent düzeyinde kesinti/devam kancası

**Engel:** MAF yolu **kapalı** (aşağıda ölçüldü). Kalem yalnız F-141 üzerinden
ilerler.

**Sorun:** Kesintiye uğramış bir agent turunu devam ettirmek için Tracon'in
agent yürütmesinin **içine** girebilmesi gerekir.

**Hazırlık — 🚨 İmza doğrulandı (2026-09-05), MAF yolu KAPALI.**
`maf-api-kesfi` koşuldu; 1.18.0 → 1.20.0 tam yüzey dump'ı diff'lendi. Kanca
çekirdek paketlerde **yoktur**: ayrı ve **alpha** bir pakettedir —
`Microsoft.Agents.AI.AgentHooks` 1.20.0-alpha.260831.1, public yüzeyi iki tip
(`AgentHooksChatClientExtensions.AsAIAgentWithAgentHooks(...)` ·
`AgentHooksOptions`). Üç ölçüm bu yolu kapatır:

1. **Sözleşme yanlış ihtiyacı karşılıyor.** Kanca bir *enforcement/interception*
   sözleşmesidir (AGENT-HOOKS-0.1): allow/deny/transform verdict, approval
   seam, `InterceptionRecord`. Bu kalemin ihtiyacı olan **kesinti/devam**
   (turu checkpoint'leyip sonra sürdürme) yüzeyde **yoktur**.
2. **Bölünemez ve pipeline'ımızla uyumsuz.** Seam decorator'ları `internal`;
   PR gövdesi "partial installs are impossible by construction" diyor.
   `AsAIAgentWithAgentHooks`, içinde `FunctionInvokingChatClient` bulunan bir
   client'ı **reddeder** ("tools would execute below the verdicts") — Tracon'in
   her provider pipeline'ı `UseFunctionInvocation()` kurar
   ([`AnthropicChatClientFactory.cs:14`](../../src/Tracon.Anthropic/AnthropicChatClientFactory.cs#L14) ·
   [`AzureOpenAIChatClientFactory.cs:16`](../../src/Tracon.Azure/AzureOpenAIChatClientFactory.cs#L16) ·
   [`GoogleChatClientFactory.cs:15`](../../src/Tracon.Google/GoogleChatClientFactory.cs#L15)).
   Kancayı almak, agent kurulum yolunun tamamını MAF'a devretmek demektir.
3. **Bağımlılık grafiği kabul edilemez.** `ResponsibleAI.AgentHooks`
   0.1.0-alpha.4 bir **native FFI** taşır (`libagent_hooks_ffi`, 1,7 MB) ve
   yalnız dört RID kapsar: linux-x64 · osx-arm64 · osx-x64 · win-x64 —
   **linux-arm64 yoktur**. Yanında `Microsoft.ML.Tokenizers`,
   `Microsoft.Extensions.AI.Evaluation`, `VectorData.Abstractions`,
   `Compliance.Abstractions`, `FileSystemGlobbing` gelir. `Tracon.Core` bugün
   AOT-uyumludur; bu graf hem onu hem "tüketicinin bağımlılık grafiğini
   kirletme" kuralını bozar.

**Kapsam:** Kalan kapsam F-141'in kapsamıdır: kesinti/devam'ı MAF'a kanca
takmadan çözmek. Tracon paralel bir kanca hiyerarşisi kurmaz (K3) — bu kural
MAF kancası alınmadığı için de geçerli kalır.

**Değer:** Kesintiye uğramış tur, MAF'ı sarmalamadan devam ettirilebilir.

**Mercek:** 2, 5, 6.

**Maliyet:** Ölçüldü, MAF yoluyla **karşılanamaz** (2. ve 3. madde).

**Risk:** Yüzeyin tamamı `[Experimental("MAAI001")]` ve paket alpha kanalında.
K-008 ön sürüm paketlerini yalnız `Tracon.AspNetCore` içinde tutar; bu kanca
`Tracon.Core`'un agent kurulum yoluna girer.

**Bağımlılık:** F-141 (MAF-kancasız alternatif tasarım) — rakip değil, **tek
yol**.

**Ekosistem:** 2026-09-05 — [`dotnet-1.19.0` sürüm notları](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.19.0) ·
[PR #7564](https://github.com/microsoft/agent-framework/pull/7564). MAF pin
2026-09-13 itibarıyla hâlâ **1.20.0**; ölçüm güncel.

**Karşı görüş:** Kalmadı. F-141 aynı ihtiyacı MAF'a hiç kanca takmadan
karşılıyor ve 2026-08-21'de bu tasarımın **doğru** olduğu kaydedilmişti.

---

### F-178 · Model deneme (attempt) telemetrisi

**Engel:** Gerçek bir üretim fallback gecikmesi olayı ölçülmedi. Tüketici
bunu **bilerek** erteledi.

**Sorun:** Yedek zincirinde hangi linkte ne kadar süre harcandığı ölçülmüyor.
`FallbackChatClient` döngü indeksini tutuyor ve `ModelFallbackUsed`'ı yazıyor,
ama `ModelFallbackUsedEventPayload` süre veya indeks taşımıyor. Birincil model
28 sn'de timeout olup yedek 2 sn'de yanıtladığında, 30 sn'lik `run`'ın
gecikmesinin hangi linkten geldiği ayrıştırılamaz.

**Kapsam:** Deneme başına süre (monotonik saat), deneme indeksi, sağlayıcı,
model ve sonuç kategorisi taşıyan bir `run` olayı veya `span`. Prompt ve yanıt
içeriği telemetriye **girmez**; sağlayıcıya özel request ID **çıkarılmaz**
(tüketici bu maliyeti kabul etti). Etiket kardinalitesi sınırlanır.

**Değer:** "Birincil timeout değeri düşürülmeli mi?", "Yedek ilk model olmalı
mı?" soruları kanıta dayanır.

**Mercek:** 2, 7.

**Hazırlık — ölçüldü (2026-09-13):** Boşluk hâlâ tam.
`grep -c "Stopwatch\|GetTimestamp\|Elapsed" src/Tracon.Core/Models/FallbackChatClient.cs`
→ **0**. Döngü indeksi zaten tutuluyor; ekleme tamamen additive'dir.
[Faz 133](fazlar/133-IS-KUYRUGU-METRIKLERI.md) metrik adı ve etiket
kurallarını kurdu.

**Maliyet:** Ölçülmedi. Yeni tablo ve migration gerekmez.

**Risk:** Etiket kardinalitesi kontrolsüz büyürse metrik altyapısını boğar.

**Bağımlılık:** Faz 133 (kapandı).

**Ekosistem:** 2026-09-02 — tüketici raporu.

**Karşı görüş:** Tüketici bunu bilerek erteledi — henüz üretimde olmadığı için incident verisi sunamıyor; ilk olay ölçülünce talebi yeniden açacak. Tek istediği,
API tasarımında bunu engelleyecek bir karar alınmamasıdır; bu koşul bugün
sağlanıyor.

---

### F-179 · Çalışma anı model yönlendirme policy'si

**Engel:** F-178 attempt süresini ölçmeye başlamalı **ve** gerçek üretim
trafiği oluşmalı.

**Sorun:** Model seçimi bugün statik binding ve hata sonrası yedek zinciriyle
sınırlı. Çağrı **öncesi** maliyet, gecikme ve capability'ye göre seçim
yapılamaz; yapılsa bile seçimin nedeni `run` kanıtına girmez.

**Kapsam:** Aday binding kümesinden seçim yapan opt-in bir policy seam'i ve
seçim kararının `run` kanıtına yazılması (seçilen binding, kararlı reason code,
değerlendirilen adaylar, policy adı).

**Değer:** Yönlendirme kararı ile `run` kanıtı aynı yerde durur.

**Mercek:** 2, 7, 8.

**Hazırlık — ölçüldü (2026-09-13), ön koşulların BİRİ karşılandı:**

| Ön koşul | Durum |
|---|---|
| `run` satırı sağlayıcıyı saklamalı | ✅ **karşılandı** — [`RunRecord.ModelProvider`](../../src/Tracon.Abstractions/Runs/RunRecord.cs#L73) |
| Kayan latency penceresi ölçülmeli | ❌ eksik — F-178 kapatır |
| Gerçek üretim trafiği | ❌ yok |

`ModelProviderHealthCache` yalnız sağlık durumu verir, gecikme vermez.

**Maliyet:** Ölçülmedi.

**Risk:** Ölçüm olmadan "en ucuzu seç" kararı yanlış olur.

**Bağımlılık:** F-178.

**Ekosistem:** 2026-09-02 — tüketici raporu.

**Karşı görüş:** Tüketici de *"önce doğru telemetry, sonra dinamik policy"*
diyor.


### Kısaltılan tetik satırlarının tam metni

2026-10-11'de aday dosyası bütçesi için `ADAYLAR.md` § *Tetik bekleyenler* tablosundaki on altı satır kısaltıldı. Satırların özgün metni:

| Kalem | Engel | Koşul ne zaman oluşur |
|---|---|---|
| **F-226** · SSE yanıtının şeması JSON şekli ilan ediyor | ASP.NET Core'un üstveri modeli aynı statü kodu için iki şema ifade edemiyor ve K-039 gereği kütüphane `Microsoft.AspNetCore.OpenApi`'ye bağımlı değil — bir `OpenApiOperationTransformer` kütüphanede yaşayamaz. Ölçüldü (2026-09-13): `tracon.json`'da 7 `text/event-stream` yanıtının **2'si** JSON şekli ilan ediyor (`/tracon/v1/responses` → `JsonElement`, `/tracon/v1/chat/completions` → `ChatCompletion`). Üretilen istemci etkilenmiyor — altıncı geçiş içerik tipinin VARLIĞINA bakar | Belgeden kod üreten üçüncü taraf bir üreteç bu yüzden kırılırsa ([Faz 159](fazlar/159-TIPLI-ISTEMCIDE-AKISLI-OPENAI-CAGRISI.md) denetim bulgusu 🟢 3) |
| **F-237** · Etkiden önceki dar fail-closed `run` kaydı | Genel `RecordingMode.Required` **reddedildi** (A10): model çağrısı ve tool yan etkileri olduktan sonra `run`'ı düşürmek hiçbir şeyi geri almaz. Savunulabilir kalan tek biçim dar bir `seam`'dir — `run` açılışı yazılamazsa `run` başlamaz, yan etkili tool çağrısının kaydı yazılamazsa tool koşmaz. Bugün böyle bir `seam` **yok**: `RunEventWriter` her hatayı yutar ([`RunEventWriter.cs:452`](../../src/Tracon.Core/Recording/RunEventWriter.cs#L452) — `Disable`) ve tüketicinin kendi `IRunStore`'u bunu değiştiremez. Kayıp **görünür** (K-782), fail-closed değil | Düzenlemeye tabi bir kurulum kanıtla talep ederse. `tracon.run.recording_failures` sayacı önce kaybın gerçek sıklığını ölçer — kanıtsız inşa edilen altyapı yanlış şekli alır |
| **F-238** · Metrik dinleyici test yardımcısının ÜÇ kopyası | Bir metriği ölçmek isteyen her test projesi kendi `MeterListener` sarmalayıcısını yazıyor: `MetricCollector` ([`tests/Tracon.Core.UnitTests/Fakes/MetricTestHelpers.cs`](../../tests/Tracon.Core.UnitTests/Fakes/MetricTestHelpers.cs)), `MetricProbe` ([`tests/Tracon.PostgreSql.IntegrationTests/Infrastructure/MetricProbe.cs`](../../tests/Tracon.PostgreSql.IntegrationTests/Infrastructure/MetricProbe.cs)) ve `WorkflowMetricProbe` ([`tests/Tracon.Workflows.UnitTests/WorkflowRecordingFailureTests.cs`](../../tests/Tracon.Workflows.UnitTests/WorkflowRecordingFailureTests.cs)). Üçü de meter'ı **referansla** eşleyip `TagList`'i kopyalıyor; üçü de `internal`, paylaşılamıyor. Kopya sayısı K-483'ün "elle tekrarlanan ifade bir kusur SINIFI üretir" eşiğindedir | Dördüncü kopya gerektiğinde, ya da `Tracon.Testing` yüzeyine bir metrik doğrulama yardımcısı eklemek ayrıca istendiğinde. Bu bir **test altyapısı** kararıdır; sevk edilen yüzeyi büyütmek (public bir `MeterProbe`) ayrı bir tartışmadır ([Faz 173](fazlar/173-CALISTIRMA-KAYDI-GORUNURLUGU.md) denetim bulgusu 🟢 7) |
| **F-243** · Analitik store yüzeylerinde metot bazında iptal kapsamı yok | `IEvalStore.DiffRunsAsync` ve `IRunScoreStore.SummarizeAsync` iptal sözleşmesinin **metot bazında** kapsamı dışında: hook'lar `ListSuitesAsync`/`ListAsync`'i hedefliyor. "Store başına bir okuma + bir yazma" sınırı bilinçlidir ve vaadi kanıtlamaya yeter — ama iptali en pahalı olan yüzeyler bu toplayıcı sorgulardır (store'un İÇİNDE hesaplanırlar, K-483 sınıfı). Aynısı `IRunStore.GetStatisticsAsync` ve `GetTimeSeriesAsync` için de geçerli | Bir toplayıcı sorgunun iptal edilmemesi gerçek bir kaynak tüketimi raporlarsa, ya da sözleşme "store başına bir okuma" sınırını gevşetmeye karar verirse ([Faz 177](fazlar/177-STORE-IPTAL-SOZLESMESI.md) denetim bulgusu 🟢 6) |
| **F-245** · Sessiz bir `run` ile ölü bir bağlantı ekranda AYNI görünüyor | Sunucu 250 ms'de bir `: waiting` gönderiyor ve bu canlılığı kanıtlıyor, ama `SseDecoder` yorumları düşürüyor ve konsol onları hiç görmüyor: kullanıcı "düşünüyor" ile "öldü"yü ayırt edemiyor. Ölü bağlantı 30 sn bayt eşiğiyle kapanır; bu kalem **canlılık göstergesi** sorunudur ve bir arayüz tasarımı kararıdır. Ölçüm (2026-09-18): `setOffline(true)` açık bir `chunked` SSE gövdesini kesmiyor — `HATA-S3-005`'in gördüğü donukluk sağlıklı bağlantıda sessiz bir run'dı | Bir kullanıcı sessiz bir run'ı öldü sanıp elle yeniden yüklerse, ya da keep-alive'ı yüzeye çıkarmanın (frame tipi ya da "son sinyal: 2 sn önce" göstergesi) bedeli tartışılırken — **kullanıcı kararı gerekir** |
| **F-246** · Sekiz kararlı hata kimliğinin karşılığı olan bir `RunErrorClass` YOK | Kaynakta 17 kararlı hata kimliği (`const string *ErrorType`) tanımlı; `DefaultRunErrorClassifier.StableIdentities` **9**'unu tanıyor. Kalan 8'i (`session_conflict` · `session_owner_required` · `external_call_rejected` · `agent_source_contract` · `agent_source_failed` · `replay_tool_mismatch` · `job_retry` · `eval_run_diff_unavailable`) bilerek eşlenmedi: **hiçbirinin karşılığı olan bir `RunErrorClass` üyesi yok** ve var olan üyelerin doküman anlamları dar. Yanlış kovaya koymak `Unknown`'dan **kötüdür** — `Unknown` kendi dokümanında dürüst bir ölçüm aracıdır. Yeni enum üyesi public sözleşme işidir: OpenAPI belgesi, TypeScript şeması, iki arayüz sözlüğü ve `RunErrorClassContractTests` (emekli 9 numaralı boşluk disiplini, K-603) birlikte değişir | Bir operatör bu kimliklerden birini panoda ayırt etmek istediğinde, ya da taksonomi bir sonraki kez elden geçirilirken — o zaman sekizi tek turda ölçülür ve kaç yeni üye gerektiği birlikte kararlaştırılır — **kullanıcı kararı gerekir** |
| **F-233** · SQLite'ın yük altındaki `SQLITE_BUSY` davranışı ölçülmedi | `SqliteDialectTests.WAL_and_busy_timeout_are_set_when_the_connection_opens` yalnız ayarların **kurulduğunu** doğrular, çekişme altındaki **davranışı** değil; `BoundedSqlLoadTests` yalnız `PostgresFixture` ile koşar (`tests/Tracon.PostgreSql.IntegrationTests/Load/`). SQLite tek yazarlıdır ve `README.md` onu çok örnekli dağıtım için önermez — ölçülmemiş olması yayımlanan bir vaadi yalanlamıyor | SQLite'ı çok yazarlı ya da yük altındaki bir kurulumda desteklemek istenirse, ya da `BoundedSqlLoadTests` deseni ikinci bir sağlayıcıya genişletilirken |
| **F-236** · Audit yazma politikası ratchet'i değişken adına bağlı | `AuditWritePolicyTests`'in deseni `[Aa]udit[Ll]og\.WriteAsync\s*\(`'dir; `IAuditLog log = …; log.WriteAsync(entry, ct);` şeklini **görmez**. Kaynak taraması "ucuz ama kırılgan" diye bilerek seçildi; Roslyn analyzer'a geçmek bir analyzer paketi maliyetidir. Ölçüldü: `src/` altında bu şekli kullanan tek yer `Tracon.Testing.Contracts.Xunit/Contracts/AuditLogContract.cs:25` ve orada **meşru** | Politikayı atlayan bir çağrı yeri gerçekten kaçarsa, ya da başka bir ratchet de analyzer isterse ([Faz 171](fazlar/171-DENETIM-IZI-YAZMA-POLITIKASI.md) denetim bulgusu 🟢 7) |
| **F-252** · Örnek uygulamanın demo kancaları bir kimlik sağlayıcısıyla değişmeli | K-834 dokuz demo kancası ekledi (`Tracon:Demo:*` + `whoami`/`refund_order` tool'ları); 60'tan fazla manuel case geçici `Program.cs` düzenlemesi olmadan koşulabiliyor. Hepsi `samples/` altındadır. Örnek uygulama gerçek bir kimlik sağlayıcısına (OIDC/JWT) bağlanırsa `DemoRoleAuthenticationHandler` · `DemoRunAuthorization` · `DemoDenyAllToolAuthorization` üçü birlikte kaldırılmalı ve bu case'lerin ön koşulları yeniden yazılmalıdır | Örnek uygulama gerçek bir kimlik sağlayıcısına bağlandığında |
| **F-253** · Konuşma paneli için sentetik ses sürücüsü | Canlı ses yolu WebRTC'ye parça verdiği için sentetik bir `MediaStream` ile **sürülebiliyor**, ama konuşma paneli `MediaRecorder` kullanıyor ve aynı akıştan **hiç veri üretmiyor** (giden çerçeve 2, ses parçası 0; `MT-MM-086`/`087`). ∴ panelin konuşma **içeriği** isteyen iki case'i yalnız gerçek bir insanla koşulabiliyor. Chrome'un `--use-file-for-fake-audio-capture` bayrağı çözerdi ama tarayıcıyı MCP sunucusu başlatıyor ve bayrak geçirilemiyor | E2E paketine ses kapsamı eklenmek istenirse, ya da tarayıcı başlatma bayrakları yapılandırılabilir hale gelirse |
| **F-264** · Azure kimlik hatası için manuel kabul case'i yok | `Credential error (...)` davranışı yalnız fonksiyonel testle (`Azure_credential_that_throws_is_Unhealthy_and_does_not_fail_the_whole_list`) kanıtlı. Kimlik gerektirmeyen bir case mümkün (`az login` yok → `DefaultAzureCredential` → `Credential error (CredentialUnavailableException)`, diğer sağlayıcılar etkilenmez) ama örnek uygulama Azure'u yalnız kimlik varken kaydediyor | Örnek uygulamaya bir Azure demo kancası eklenirse (F-252 ile aynı iş) case `06-SAGLAYICI-DIGER`'e yazılır ([Faz 181](fazlar/181-PROVIDER-ORTAK-KATMANI.md) denetimi 🟢-2) |
| **F-250** · Kiracıya duyarlı örnek bir `IAgentSource` yok | `MT-CORE-095` iki turdur koşulamıyor: case bir kiracının kendi agent kaynağını getirdiğini ölçmek istiyor, ama depoda kiracıya duyarlı bir `IAgentSource` **örneği** yok — sevk edilen tek uygulama yapılandırmadan okur. K-834 sınıfının **dışındadır**: bir bayrağı çevirmek değil, bir örnek yazmak demektir. Aynı boşluk `samples/`'ın seam envanterinde de bir delik: genişleme noktası sevk ediliyor, örneği yok | Bir tüketici kiracı başına agent kümesi sorarsa, ya da `MT-CORE-095` üçüncü turda da koşulamazsa — o zaman örnek `samples/Tracon.Api`'ye kalıcı olarak eklenir ve case onunla koşulur |
| **F-256** · Harf-kayması sözleşme senaryosu yalnız iki store'da | Senaryo paylaşılan `TenantIsolationContract<T>` tabanında değil: taban bellek içi store'lar üzerinde de koşuyor ve K-839 gereği onların 30'u kiracı kimliğini normalleştirmiyor. Senaryo yalnız iki fail-open store'dadır; kalan depolama sınırının güvencesi `TenantParameterChokePointTests` metin kapısı + SQL Server/SQLite guard testleridir | Bellek içi store ailesi kiracı kimliğini normalleştirirse (K-839 genişlerse) — o zaman senaryo tabana taşınır |
| **F-263** · `ic-dongu`, paylaşımlı provider kaynağının HTTP kanıtını seçmiyor | `kapi.py` `src/Tracon.Providers.Shared` değişikliğini dört provider birim projesine bağlıyor (`PROVIDER_TEST_PROJECTS`), ama paylaşılan `authorize` yolunun HTTP seviyesindeki tek kanıtı `ModelHealthEndpointsTests` (`Tracon.AspNetCore.FunctionalTests`, ~200 sn). `ProviderHealthCheckCore` değişirse iç döngü onu koşmaz; `kapanis` tam koşum yaptığı için kaçış kapanışta yakalanır | İç döngüde bir provider kaçışı ölçülürse `Tracon.AspNetCore.FunctionalTests` eşlemeye eklenir ([Faz 181](fazlar/181-PROVIDER-ORTAK-KATMANI.md) denetimi 🟢-1) |
| **F-274** · Üç SQL `Use*` metodundaki ~107 aynı kayıt satırı ortak bir iç yardımcıya taşınmadı | `SqlProviderRegistrationParityTests` unutulan veya dekoratörsüz kaydı kırmızı yapar; kopya kalır. `git log` ölçümü üç dosyaya birlikte dokunan 35 commit gösterdi. Hedef: `src/Tracon.Sql.Shared/Internal/` altında internal bir yardımcı; `Use*` sağlayıcıda kalır | Üç sağlayıcıda satır satır tekrarlanan ikinci bir yatay değişiklik — sayım 2026-09-23'ten başlar (kullanıcı kararı) — ya da dördüncü bir SQL sağlayıcısı (K-176) |
| **F-276** · npm kanalı test edilen derlemeyi değil kendi `npm run build` çıktısını yayınlıyor | `ci.yml` `npm-publish` işinin `Paketle` adımı (`npm run build`) kendi derlemesini yayınlar; `npm test` başka bir derlemede, `release-dryrun`'daki `npm publish --dry-run` üçüncü bir derlemede koşar. NuGet kanalındaki aynı sınıf Faz 191'de kapandı (K-871; yeniden açılma koşulu bu adaydır); `tsc` çıktısı deterministik ve npm'in 72 saatlik geri alma penceresi riski düşürür | `tsc` çıktısında ilk sapma ya da ilk npm yayın kusuru |

## Kapanan kalemler

> 2026-09-24'ten itibaren [`../ADAYLAR.md`](../ADAYLAR.md) yalnız yapılacak
> işi taşır (kullanıcı kararı). Kapanan her kalem o dosyadan silinir ve buraya
> **tek satırla** (tarih + kanıt) taşınır. Numara burada yaşamaya devam eder:
> `dokuman-bakim.py`'nin F-ID sayacı bu dosyayı da sayar, bu yüzden kapanan
> bir numara yeniden tahsis edilemez. Gövdeli bir kalem kapanırsa gövdesi
> tablonun altına eklenir.

**Kusur giderme turlarının geçmişi** ([`../ADAYLAR.md`](../ADAYLAR.md) § *Kusur giderme*'den taşındı, 2026-10-14, Faz 206 kapanışı — bütçe):

2026-10-12'de üç tur kapandı: önce 🟠 kalemler (F-325 · F-326 · F-328 · F-327 · F-329 · F-330 · F-331, F-317 ve F-314 ile), sonra 🟡 kalemler (F-315, F-332…F-340, F-348, F-349, F-351, F-352; K-892…K-896), sonra tüketici turunun kusurları (F-341…F-344, F-354, F-360, F-364…F-367, F-369…F-372; K-903…K-911). F-382 bu turun kendi ölçümüdür; F-386…F-404 kapanan kusurların sınıf taramalarından gelir. 2026-10-13'te F-383, F-384, F-385 ve F-388 kapandı (K-912, K-913); sınıf taramaları ve bağımsız inceleme F-416…F-419'u açtı. Faz 201 kapanışında F-423 (kapıyı kıran test yarışı) açıldı ve kapandı. Faz 198 kapanışı (konuşma kapısının sınıf taraması ve denetimi) F-441…F-446'yı açtı. Aynı gün ölçümlü aciliyet turu F-431, F-432, F-401, F-387 ve F-416'yı kapattı (K-933…K-936), F-400'ü daralttı, F-418'i faz planlamaya taşıdı, F-291'i kusur kanalına aldı ve sınıf taramasından F-447'yi açtı. İkinci tur F-382, F-386, F-389, F-396, F-400, F-361, F-291 ve F-447'yi kapattı (K-937…K-941); F-389'un sınıf taraması F-448'i açtı. 2026-10-14'te F-398, F-419, F-448, F-433, F-391, F-350 ve F-481 kapandı (K-975…K-981); sınıf taramaları F-485…F-487'yi açtı. Aynı gün ikinci tur F-429, F-353, F-442, F-444, F-426, F-430, F-356, F-393, F-484 ve F-443'ü kapattı (K-982…K-986); sınıf taramaları F-488…F-490'ı, bağımsız inceleme F-491'i açtı. F-418 ve F-482 Faz 205'e dönüştü (planlandı). Kapanan kalemlerin kaydı bu bölümdedir.

| Kalem | Kapanış | Kanıt |
|---|---|---|
| **F-547** · İş kuyruğu, yeniden denemeye bıraktığı işi aynı worker'da yeniden kiralayınca kirayı bırakıyordu; ikinci deneme hiç koşmadan iş düşüyordu | 2026-10-15, kusur-giderme | Windows CI (`main` #259, #261) `JobWorkerDeadlineTests.With_attempts_left…` `Attempt = 2`, `Leased` ile 30 sn bekledi; macOS'ta 25 koşumda 1. `JobWorkerReleaseRaceTests.A_job_leased_again_while_its_previous_attempt_is_still_finishing_runs_that_attempt` düzeltmeden önce her koşumda kırmızı, sonra yeşil; deadline testi 0/25; zamanlama ad alanı 268/268. Sınıf taraması: tek kira tüketicisi `JobWorkerBackgroundService` |
| **F-548** · İki skill script FIFO testi Windows runner'ında düşüyordu (`mkfifo` MSYS emülasyonu) | 2026-10-15, kusur-giderme | Windows CI: `A_missing_empty_or_special_file_script_is_denied_without_opening_it(fifo)` "does not exist", `A_file_that_blocks_after_the_gate…` 30 sn. Ürün davranışı doğru; testin ortam varsayımı yanlıştı. `TryMakeFifo` Windows'ta `false`; macOS'ta 47/47 |
| **F-546** · Workflow editörü Vitest'i kayıtlı tanım gelmeden zinciri okuyordu; Ubuntu CI'da kırmızı | 2026-10-15, kusur-giderme | `main` #259 Ubuntu `Derle` adımında `opens a function-node chain…` `workflow-step` bulamadı (anotasyon DOM'u boş form). Yerelde dosya 41/41, yük altında üç tam koşum 418/418 — kırılgan test, ürün kusuru değil. Sınıf taraması: dosyada dokuz aynı bekleme, hepsi `loaded()`'a çevrildi; başka ekran testinde `findByTestId('workflow-participants')` yok |
| **F-528** · `reference/configuration.md`'de `AgentGraph:ChildDeadline` ve `AgentGraph:WaitTimeout` satırı yoktu | 2026-10-15, Faz 211 kapanışı | İki satır `Tracon:AgentGraph` tablosuna eklendi (`TraconAgentGraphOptions` XML'inden: varsayılan, hata aralığı, agent adımının `timeout` ile ezilmesi); `npm run check` yeşil |
| **F-536** · Loop'lu agent akışlı yüzeylerde taslaklarını cevap olarak veriyordu (workflow adımı, OpenAI uyumlu ve A2A akışları, iki ses yolu) | 2026-10-15, kusur-giderme (K-1006) | `WorkflowLoopStepOutputTests` (2) ve `LoopAnswerSurfaceTests` (5 kırmızı yüzey) düzeltmeden önce kırmızı, sonra yeşil; akışsız A2A `SendMessage` öncesinde de yeşildi. Sınıf taraması: `grep -rn "RunStreamingAsync(" src` — cevabı okuyan altı çağıran `AgentAnswerStream`'e bağlandı; native agent SSE belgelenmiş davranış olarak kaldı |
| **F-524** · `Microsoft.Data.SqlClient` açık uyarılı `System.Security.Cryptography.Pkcs` çekiyordu; kapanış kapısı kırmızıydı | 2026-10-14, kusur-giderme | `TemplateInstantiationTests.Most_full_combination_compiles_with_zero_warnings` düzeltmeden önce kırmızı (`NU1902`/`NU1903`), sonra yeşil. SqlClient'ı yükseltmek kapatmaz: 7.0.2…7.1.1 her yayın aynı Pkcs'yi çeker (net8.0 8.0.1, net9.0/net10.0 9.0.13 ve 9.0.18). `Tracon.SqlServer` hedef çatıya göre yamalı sürümü (8.0.2 · 9.0.21 · 10.0.13) doğrudan `PackageReference` ile zorlar; eşleşen sürüm tüketici grafiğine paket eklemez. Ölçüm: `dotnet restore Tracon.slnx --force` temiz, `dotnet list … --vulnerable --include-transitive` 0 proje, paketlenen nuspec üç çatıda pini taşır. Soğuk restore `NU1902/NU1903`'ü hata yapıyordu (`--force` ile yamasız ağaçta doğrulandı) — hafıza: `hafiza/tedarik-zinciri.md` |
| **F-502** · `Tracon.Samples.FileRunStore` karışık para birimi sözleşme case'inde düşüyordu; yayın provası kırmızıydı | 2026-10-14, kusur-giderme | `RunStoreContract.No_cost_total_adds_amounts_in_two_currencies` örnekte 128/129 kırmızı, düzeltmeden sonra 129/129. Örnek `internal` `CurrencyTotals`'ı göremez; ağaç, istatistik penceresi, zaman kovası ve deney kolu için kendi `CurrencySet` sayımını yazar, tamamlanan run'ın kendi token dışı kalemi para birimi sayısını taşır. Sınıf taraması: başka toplama yapan `IRunStore` uygulaması yok (kalanlar test sahteleri ve dekoratörler). Kanıt: `kapi.py yayin --kuru` çıkış 0 — altı örnek test projesi, Native AOT smoke ve net8.0 tüketici smoke yeşil (önceden prova ilk düşende duruyordu) |
| **F-268** · `ExperimentTests` yük altında kırılgandı: sürüm alanı `<select>` olmadan seçiliyordu | 2026-09-24, Faz 189 kapanışı | Tam kapanış koşumunda yeniden düştü ("Element is not a <select> element"). Test artık `ToHaveJSPropertyAsync("tagName", "SELECT")` bekler (`tests/Tracon.Ui.E2ETests/Ui/ExperimentTests.cs`); ikinci tam koşum (17.846 test) yeşil. Tuzak: `hafiza/frontend-test-altyapisi.md` |
| **F-262** · `Tracon.Google`'ın görsel yolu deprecated Imagen `:predict` yüzeyini hedefliyordu | 2026-09-19 (kayıt 2026-09-24'te kapatıldı) | Kod `GenerateContentAsync` kullanıyor — [`GoogleImageGenerator.cs:83`](../../src/Tracon.Google/Internal/GoogleImageGenerator.cs#L83), commit `d1f539fb` (Faz 179). Satır aday dosyasına 2026-09-22'de, düzeltmeden **sonra** ve bayat olarak girmişti (F-251 çift tahsisi yeniden numaralanırken). Canlı kanıt **F-255** ile 2026-09-24'te alındı; K-835'e kapanış notu eklendi |
| **F-221** · kalan iz: `src/` içindeki `prismOptions`/`prismException` yerel değişken adları | 2026-09-24 ölçümü | `grep -rnoi "prism" src --include='*.cs'` → **0**. Görsel yarısı Faz 163–165'te kapanmıştı |
| **F-278** · `PeriodicTimer`'a giden aralıklar doğrulanmıyordu; geçersiz değer host'u açılıştan SONRA durduruyordu | 2026-09-24, kusur-giderme | Repro örnek uygulamada canlı: `Tracon__Approvals__ScanInterval=00:00:00` → "Application started" ardından `StopHost`; düzeltmeden sonra aynı komut açılışta `OptionsValidationException` ile adı veriyor. Ortak `TimerPeriod` (`src/Tracon.Core/Hosting/TimerPeriod.cs`); yeni `TraconApprovalOptionsValidator`, `CanaryOptionsValidator`, `TraconMcpOptionsValidator`; zamanlama, singleton, uzlaştırma, sağlık ve saklama doğrulayıcıları sıkılaştırıldı (özellik açıkken). Kanıt: `TimerPeriodOptionValidationTests` (önce 20 kırmızı), `McpRefreshIntervalValidationTests` (önce 4 kırmızı), kapı `PeriodicTimerSiteTests`. Timeout alt sınıfı **F-280**'e ayrıldı |
| **F-275** · `quota_usage` her run'da tüm geçmişi okuyordu; `AsOf` yok sayılıyordu | 2026-09-24, kusur-giderme | K-857: `QuotaUsageQuery.PeriodStarts` (store süzer, verilmezse tüm geçmiş) · `AsOf` `[Obsolete]`. Dört iç çağıran dönem başlarını geçer. `QuotaStoreContract`'a dört case (in-memory önce 3 kırmızı; SQLite/PostgreSQL/SQL Server yeşil); `QuotaEnforcerTests` ve `QuotaUsageObserverTests` önce kırmızı. Migration yok |
| **F-244** · `guides/coding-agents.md` diagnostic tablosu eksik sayıyordu | 2026-09-24, doğrudan | `TRC0501`/`TRC0502` satırları eklendi; sayfa artık sayı yazmıyor. Kapı: `check-content.mjs` tabloyu `UsageDiagnostics.cs` tanımlayıcılarıyla iki yönlü karşılaştırır |
| **F-255** · `Tracon.Google` görsel yolu için canlı kanıt yoktu | 2026-09-24, doğrudan | `MT-MM-121` canlı Google anahtarıyla yeniden koşuldu: `POST /api/images/generate` → `200`, `image/png` 2 063 511 B ek. `size: "1024x1024"` açık hatayla reddedildi (`502`, *"does not accept WIDTHxHEIGHT"*). `OutputMimeType` sorusu ölçüldü ve yeni kusura dönüştü: **F-281** |
| **F-251** · Damıtma, yeniden koşulan bir case'in eski bloğunu işaretsiz bırakıyordu | 2026-09-24, doğrudan | `_kosum_damit_metni` son bloğun işaretini önceki blokların boş şablon `Durum` satırına taşır; ters yön (ilk deneme geçti, yeniden koşum kaldı) artık `☑` satırına inmez; serbest metinli `Durum` satırına dokunulmaz. Arşivde 19 blok düzeltildi (`12-GOZLEMLENEBILIRLIK-MALIYET.md`). Kanıt: `KosumDamitmaTestleri` (önce 2 kırmızı, 6 yeni test) |
| **F-304** · Önbellekteki derlenmiş agent sonradan kaydedilen egress politikasını görmüyordu | 2026-10-11, kusur-giderme | `EgressPolicyCacheTests` düzeltmeden önce iki kez kırmızı (`200`, sağlayıcı çağrıldı), sonra yeşil. K-875: politika içeriği bağımlılık parmak izinde. Kapı: `CompiledAgentCacheKeyCoverageTests`. Sınıf taramasının açık bulguları F-305 ve F-306 |
| **F-283** · Onay kuralı MCP kaynağına bağlı değildi: `AgentsAdmin` endpoint'i değiştirince `SecurityAdmin`'in kuralı yeni sunucunun tool'unu onaylıyordu | 2026-10-11, kusur-giderme | K-876 (seçenek A, kullanıcı kararı): `ToolApprovalRule.SourceFingerprint` + üç migration (PG 0055 · SQL Server 0043 · SQLite 0042). `McpApprovalRuleSourceTests` ölçüm testleri ters çevrildi; üç case (endpoint değişimi · sil+aynı adla ekle · "bir daha sorma") HEAD'de kırmızı, düzeltmeyle yeşil. Sözleşme: iki yeni `ToolApprovalRuleStoreContract` case'i (bellek içi + SQLite koşuldu). Bağımsız inceleme sonrası: tool'ları keşfedilmemiş sunucunun tool'u `Unresolved` raporlar (hiçbir kural eşleşmez), kural yazımı `409`. Manuel: `MT-MCP-078` ➜ CI |
| **F-284** · `AgentsAdmin`, MCP sunucusunun `RequiresApproval`'ını kapatarak onayı bütünüyle atlatıyordu | 2026-10-11, kusur-giderme | K-877 (seçenek A, kullanıcı kararı): `requiresApproval: false` yeni kayıtta, `true`→`false` geçişinde ve endpoint değişiminde `SecurityAdmin` ister. `McpApprovalRuleSourceTests.Switching_approval_off_for_a_server_requires_the_security_admin_scope` HEAD'de kırmızı, düzeltmeyle yeşil. Manuel: `MT-MCP-079` ➜ CI |
| **F-306** · Canlı ses (OpenAI Realtime) kiracının egress politikasını ve kendi anahtarını uygulamıyordu | 2026-10-11, kusur-giderme | K-878 (seçenek A, kullanıcı kararı): launcher'da politika + BYOK; `SupportsTenantCredentials` · `AttachSessionAsync` · `LiveVoiceCreateRequest.Credential`; kural `TenantProviderAccess`'te tek yerde (registry'nin iki kopyası ve bağlama ucunun kopyası oraya indi). `LiveVoiceTenantPolicyTests` beş case HEAD'de kırmızı (politika, kiracı anahtarı iki çağrıda, değersiz bağlama, endpoint override, opt-in etmeyen sağlayıcı), kontrol case'i yeşil. Bağımsız inceleme sonrası endpoint override launcher'da `403` (kayıt yazılmadan). Manuel: `MT-MM-123` ➜ CI |
| **F-279** · K-840 kalıntısı: `is not OperationCanceledException` filtresi — dokuz site + SINIF (toplam 58 filtre, 7 `catch (OperationCanceledException) { throw; }` bloğu, 7 tekil "her OCE iptaldir" yakalaması) | 2026-10-11, kusur-giderme | Sınıf taraması 59 site sınıfladı (55 aynı sınıf, 4 değil — onlar da tek yazıma alındı). Kural tek yazım `OperationCancellation.IsFailure(ex, token)`; token'sız siteler her istisnayı yakalar ve yorumla söyler (kullanıcı kararı). Kapı (üçüncü tekrar): `CancellationFilterSiteTests` — HEAD'de kırmızı. Davranış testleri HEAD'de kırmızı: `ForeignCancellationEndpointTests` (4: attribution · hatırlanan kural yazımı · trigger yeniden denemesi · dış yüzey kataloğu), `ImageEndpointTests` (502), `ForeignCancellationTests` (3), `AgentSourceFaultIsolationTests` (3), `AuditRecorderTests`, `ResponseCacheLifetimeTests`, `RunRecordingFailureMetricTests`. Eski semantiği kodlayan 8 test düzeltildi (7 birim: "gerçek iptal = token iptal"; 1 fonksiyonel: yabancı iptal artık doğrulayıcı hatası gibi fail-closed reddedilir) |
| **F-257** · Bilinen yarış testleri (`WorkflowEventSinkTests` sıra · `LiveVoiceLifecycleTests` transkript zaman aşımı · sağlayıcı kapanışında `ObjectDisposedException`) | 2026-10-11, kusur-giderme | (1) ÜRÜN kusuru: olay numarası, store yazımı ve sink dağıtımı ayrıktı — K-879, deterministik düşen test `RunEventSinkTests.A_second_writer_waits_…` (HEAD'de kırmızı); bellek içi store sıralı okur. (2) Kök neden 2026-09-19/20'de kapanmıştı (kapanış sırası + sideband boşaltma); test yorumu ve hafıza notu yanlış teşhisi yazıyordu, düzeltildi. (3) TEST kusuru: sahte sunucunun gönderimi ile bağlantı sökümü yarışıyordu — `FakeGptLiveServer` gönderim kapısı. Repro kendi koşullarında: CPU stresi + 8 paralel şerit, 120 `LiveVoiceLifecycleTests` sınıf koşumu (1 200 test) · 150 `WorkflowEventSinkTests` koşumu → 0 düşüş; tam çözüm koşumu kapanış kapısında |
| **F-305** · Önbellekteki agent'ın MCP tool'ları, sunucu değişince kapatılmış bağlantıyı çağırıyordu | 2026-10-11, kusur-giderme | `McpToolCacheTests` düzeltmeden önce beş case kırmızı (HTTP `200`, `Completed`, `ToolFailed`/`TaskCanceledException`; onay yeniden açılınca onay sorulmadı; `read_resource` `InvalidOperationException`; refresh hatasında durmuş sunucuya çağrı), sonra iki ardışık koşumda yeşil. K-880 (seçenek A, yalnız bağlantı kimliği; kullanıcı kararı): bağlantı örneği kimliği (`McpConnection.Generation`) bağımlılık parmak izinde; `absent` silme, devre dışı ve refresh hatasını kapsar. Kapı: `CompiledAgentCacheKeyCoverageTests` (`IToolRegistry` → key). Manuel: `MT-MCP-080` ➜ CI. Sınıf taramasının açık bulguları: F-308 (şema kayması), F-309 (refresh'te son iyi liste), F-310 (canlı ses oturumu), F-311 (yanıltıcı hata açıklaması) |
| **F-307** · SQLite entegrasyon testleri tam çözüm yükünde rastgele `SQLITE_BUSY` ile düşüyordu | 2026-10-11, kusur-giderme | Kök sebep tek bir tutucu değil, açlık: bütün sınıflar tek `.db` dosyasını, yani tek yazar kilidini paylaşıyordu; busy handler kuyruk olmadığı için yük altında bir ifade Microsoft.Data.Sqlite'ın 30 sn'lik `CommandTimeout`'unu aştı (iz: en uzun işlem ≤ 1,6 sn, ifade bekleyişi 30–61 sn). Her tablo öneki artık kendi dosyası (`SqliteFixture.ConnectionStringFor`). Kanıt: `TestDatabaseIsolationTests` (önce kırmızı, 1,3 sn'de `SQLite Error 5`); yük repro'su (3 TFM eşzamanlı + `--max-threads 3x` + 10 CPU yakıcı, `52674c96`) önce 9/9 bacak kırmızı (146 kilit düşüşü + 352 sınıf temizliği satırı), sonra 27/27 bacak yeşil. K-193 kapsamı `Two_prefixes_in_one_file_each_get_the_full_index_set` ile korunur. Tuzak: `hafiza/test-yalitimi-vakalari.md`, `hafiza/sqlite.md`. Ürün yarısı (K-827 retry hiç koşmuyor) → **F-312** |
| **F-312** · K-827'nin `SqliteRetryingCommand`'ı hiç yeniden denemiyordu | 2026-10-11, kusur-giderme | Seçenek (c), kullanıcı kararı: sarmalayıcı ve `SqliteDataSource.CreateDbCommand` override'ı silindi; davranış değişmedi, çünkü filtre (`_inner.Transaction` → `NotSupportedException` → `false`) retry'ı hiç koşturmuyordu. Reddedilen yazmayı Microsoft.Data.Sqlite `CommandTimeout`'a kadar kendisi yeniden gönderir. Kanıt: `SqliteWriteContentionTests` gerçek data source + gerçek kilitle — 7 sn tutulan kilit (`busy_timeout`'tan uzun) altında yazma başarılı, `CommandTimeout = 1` ile `SQLite Error 5` sınırlı sürede; eski dört mekanizma/kablolama testi silindi. Tahsis tabanı `QueryRuns` 45 040 → 44 832 B (sarmalayıcıdan önceki değer, `bf319755`). K-827'ye düzeltme notu; `hafiza/sqlite.md` |
| **F-313** · SQL Server'da iki dallı upsert'ler işlemsiz koşuyordu: aynı anahtarın eşzamanlı ilk yazımı `UNIQUE KEY` ihlali atıyordu (20 store) | 2026-10-11, kusur-giderme | `UpsertRaceTests` (taze anahtar, 12 yazar × 20 tur) düzeltmeden önce 10/10 kırmızı, sonra üç koşumda 10/10 yeşil. Kök sebep autocommit: range lock UPDATE ile bitiyordu. Çözüm `SqlStoreContext.ExecuteUpsertAsync` (yalnız SQL Server'da ADO.NET `DbTransaction`); işlem açılınca `ISNULL` sarmalı sütun deadlock üretti (ölçüldü) → üç tablo `EXISTS (SELECT c INTERSECT SELECT @p)`. F-215'in yeniden denemesi kaldırıldı. Kapı: `SqlServerUpsertAtomicityTests`. SQL Server 865 · PostgreSQL 935 · SQLite 881 test yeşil |
| **F-289** · Tool kütüphanesini referanslayan host `AddGeneratedTools()` çağıramıyordu (`CS0121` + `TRC0005`) | 2026-10-11, kusur-giderme | K-881 (seçenek A, kullanıcı kararı): üretilen `TraconGeneratedToolsBuilderExtensions` artık `internal`; kütüphane kendi public uzantısını sunar, `TRC0005` mesajı bunu söyler. `CrossAssemblyGeneratedToolsTests` (iki derleme, ayrı üreteç koşumları) düzeltmeden önce `CS0121` ile kırmızı, sonra yeşil; 301 üreteç testi yeşil. Sınıf taraması: üreteç başka `public` tip yazmıyor. Regresyon case'i `MT-PKG-164`. Site: `getting-started/tools.md` (kütüphane bölümü), `troubleshooting.md` (TRC0005) |
| **F-280** · Yapılandırmadan gelen timeout'lar zamanlayıcı aralığında doğrulanmıyordu — F-278'in (b) alt sınıfı | 2026-10-11, kusur-giderme | Sınıf taraması 18 girdi (listedeki 13 + `VoiceConversationOptions` iki süre, MCP'nin iki ek kullanımı, istemci, CLI, retry politikası). Hepsi `TimerPeriod.IsValid` (HTTP'ye gidenler `IsValidHttpTimeout`, ≤ `int.MaxValue` ms); `Workflows`, canlı ses ve konuşma için ilk doğrulayıcılar + `ValidateOnStart`; `SubAgentSettings` derleyicide, kod değerleri guard'da. Yeni testler düzeltmeden önce kırmızı (Core 19 · Workflows 9 · Mcp 4 · Google 1 · CLI 2 · istemci 1), sonra yeşil. Kapı: `TimeoutSiteTests` (yeni `CancelAfter(` sitesi). `OnlineEval:JudgeTimeout` zaten 5 dk ile sınırlıydı |
| **F-308** · Önbellekteki agent uzak MCP tool'unun değişen açıklamasını ve şemasını görmüyordu | 2026-10-11, kusur-giderme | K-882 (seçenek A, kullanıcı kararı): `CompiledAgentCache` `(kiracı, ad, tanım, kültür)` başına tek girdi; MCP bağlaması `{generation}:{ProtocolTool SHA-256}`. `McpToolCacheTests.A_cached_agent_sees_a_tool_description_the_server_changed` ve `CompiledAgentCacheTests` yeni vakaları düzeltmeden önce kırmızı, sonra yeşil. Sınıf taraması: `IToolBindingResolver` yalnız `McpToolRegistry`'de; kod tool'ları süreç ömrü boyunca sabit. Case `MT-MCP-082` (➜ CI) |
| **F-309** · Geçici `tools/list` hatası sunucunun tool'larını hemen düşürüyor, agent'lar bir sonraki başarılı refresh'e kadar reddediliyordu | 2026-10-11, kusur-giderme | K-883 (süre, varsayılan 15 dk, kullanıcı kararı): `TraconMcpOptions.StaleToolRetention`; bağlantı kayıtlı ve tanımı aynıyken son iyi liste son başarılı listelemeden itibaren tutulur, `Zero` eski davranış. `McpToolCacheTests.A_cached_agent_keeps_working_through_a_transient_listing_failure` düzeltmeden önce kırmızı, sonra yeşil; eski red davranışı süre geçince (`00:00:00` ve `00:00:00.001`) korunur. Sınıf taraması: periyodik uzak liste yalnız MCP kataloğunda; kaynak listesi aynı dalda. Case `MT-MCP-081` (➜ CI), `MT-MCP-080` adım 5 güncellendi |
| **F-310** · Canlı ses oturumu agent'ı oturum boyunca tutuyordu; oturum ortasındaki MCP değişikliği ona ulaşmıyordu | 2026-10-11, kusur-giderme | Repro doğrulandı: `LiveVoiceMcpTests` ikinci delegasyonda "Error: Function failed." (iki kez), düzeltmeden sonra 6/6 yeşil. Kullanıcı kararı: her delegasyon/tur/öğe başında `IAgentCatalog.ResolveAsync` (pompanın `AmbientTenantScope`'u altında; `AgentSession` korunur). Sınıf taraması iki vaka daha buldu ve aynı düzeltmeyi aldı: WebSocket konuşması (`VoiceConversationMcpTests`, düzeltmeden önce kırmızı) ve toplu iş (`AgentBatchJobHandlerTests`, iki vaka kırmızı → yeşil); `ChildAgentInvoker`/workflow ve diğer iş işleyicileri zaten çağrı başına çözüyor. Case `MT-MM-124` (➜ CI) |
| **F-325** · Workflow SSE akışı keep-alive yazmıyordu; konsol 30 sn sessiz run'ı kesiyor, run `Canceled` oluyordu; slot beklerken `RunTimeout` dolan run satırsız ve kapanış çerçevesiz kalıyordu | 2026-10-12, kusur-giderme | K-885 (kullanıcı kararı): `SseWriter` her SSE ucunda kendi keep-alive'ını yazar (`TraconEndpointOptions.StreamKeepAliveInterval`, varsayılan 15 sn; ajan, workflow, iki OpenAI-uyumlu uç); workflow akışı her istisnayı `error` çerçevesine çevirir (K-296 sınıfı); slot beklemesi run satırı açıldıktan sonradır. `StreamKeepAliveTests` (boşta 2 sn sınırlı okuyucu) ve `WorkflowRunTimeoutReasonTests.A_run_that_times_out_waiting_for_a_slot…` düzeltmeden önce kırmızı |
| **F-326** · Job worker'ın kira, deneme ve iptal sözleşmesi tutmuyordu | 2026-10-12, kusur-giderme | K-886 (kullanıcı kararı): yenileme tick başına yakalar ve kiranın üçte birinde koşar; kirası dolup sınırı aşan job koşmadan `Failed` (`JobErrorCodes.AttemptsExhausted`); paylaşılan SQL ve bellek içi depo terminal job'u değiştirmez; `MarkRunningAsync` reddi job'u koşturmaz. `JobWorkerLeaseContractTests` (5) ve iki `JobStoreContract` case'i önce kırmızı; repro süreç düzeyinde: `TwoProcessLeaseTakeoverTests` SIGKILL + `MaxAttempts = 1` yeşil. Sınıf taramasının açık bulgusu F-348 |
| **F-328** · Workflow agent adımında süre, bütçe, sağlayıcı ve onay reddi metin olarak sonraki düğüme akıyor, run `Completed` bitiyordu | 2026-10-12, kusur-giderme | K-887 (kullanıcı kararı): `ChildRefusalMode.Fail` — workflow katılımcısı reddi atar (yeni public `TraconChildRunTimeoutException`, sınıf `Timeout`; bütçe `TraconRunBudgetExceededException`; sağlayıcı hatası olduğu gibi); delegasyon metin dönmeye devam eder. `WorkflowStepRefusalTests` Sequential, Concurrent, GroupChat, Handoff ile önce kırmızı (6/6); Magentic ölçülmedi |
| **F-314** · Workflow run satırı hata türünü CLR adıyla yazıyor, sınıflamıyordu | 2026-10-12, kusur-giderme (F-328 ile) | K-887: `WorkflowRunner.ToRunError` `TraconException.ErrorType` yazar, satır kapanırken `IRunErrorClassifier` ile `Class`/`Fingerprint` dolar. `HarnessLoopTests.A_workflow_stops_at_an_agent_whose_loop_fails_on_exhaustion` iddiası çevrildi (`loop_unsatisfied` / `LoopUnsatisfied`) |
| **F-327** · Bekleyen insan isteği kalıcı olarak yanıtlanamaz hâle geliyordu (8 KB kırpma, retention, `KeepCheckpointsAfterCompletion=false`) | 2026-10-12, kusur-giderme | K-888 (kullanıcı kararı): istek kimliği olay `Text`'inden, yük toleranslı okunur, `form` `prompt`'tan önce yazılır; retention `AwaitingInput` run'ın istek olayını ve checkpoint'lerini tutar; oturumda bekleyen veya koşan run varken checkpoint silinmez. Üç HITL testi ve SQLite retention testi önce kırmızı; PostgreSQL retention testi yeşil |
| **F-317** · Çok turlu harness döngüsünün run'ı yalnız son turun `usage`'ını kaydediyordu | 2026-10-12, kusur-giderme | K-889 (kullanıcı kararı): `AggregatedResponseGuard` MAF `OpenTelemetryAgent`'ın bayat `RawRepresentation`'dan yeniden kurmasını keser; kaydedici akıştaki `UsageContent`'leri toplar (OpenAI, Anthropic, Google adaptörlerinin çağrı başına tek `UsageContent` yazdığı ölçüldü); akışsız döngü son turu döner. `HarnessLoopTests` iki usage case'i önce kırmızı. `aiJudge` usage'ı F-349 |
| **F-329** · Loop + yapılandırılmış doğrulama birleşik metni doğruluyordu | 2026-10-12, kusur-giderme (F-317 ile) | K-889: doğrulayıcı son asistan mesajını doğrular (`FinalAnswerText`); tool öncesi ara metin de aynı sınıftı. `HarnessLoopTests` iki structured case'i önce kırmızı; üç birim testi. Doğrulanmamış komşu bulgu F-350 |
| **F-330** · Parametreli agent kuyruk, batch, replay, OpenAI, MCP, A2A, workflow, ses ve kod tanımında bağlanmadan koşuyordu | 2026-10-12, kusur-giderme | K-890 (kullanıcı kararı): `BuildAgent` varsayılanları bağlar, varsayılansız zorunlu parametrede `UnboundParameterGuard` sağlayıcıdan önce düşürür; giriş uçları 400/hata döner; kapı kod tanımını da okur. `AgentParameterPathTests` önce kırmızı (6/6). İnceleme ekleri: replay ve devam parametreli tanımı reddeder (kullanıcı kararı), eval kod tanımını okur — iki test daha, önce kırmızı. Değer taşıma F-294'te, kuyrukta düşen culture/atama F-351 |
| **F-331** · Sabit sürümlü çözümleme güncel sürümün descriptor'ını kullanıyordu | 2026-10-12, kusur-giderme | K-891 (kullanıcı kararı): `IDescribingAgentSource` iç seam'i (tek okuma, sabit sürümün kendi descriptor'ı; sürümsüz yoldaki çift okuma yarışı da kapandı), beş elle kopyalanan descriptor tek `AgentDescriptorProjection`; eval case run'ı sabit sürümü yazar, eval satırı sabit sürümün modelini. `PinnedVersionDescriptorTests` önce kırmızı. Sınıf taramasının açık bulgusu F-352 |
| **F-339** · Harness agent'ı skill adı vermeden çalışma dizininden skill keşfediyor, dosya belleği açılmadan `./agent-file-memory`'ye yazıyordu | 2026-10-12, kusur-giderme | K-892 (kullanıcı kararı): skill yalnız `SkillNames` doluyken, dosya belleği yalnız `Memory.EnableFileMemory` ile kayıtlı `AgentFileStore`'a; `SkillNames` + `DisableAgentSkillsProvider` derleme hatası. Dosya belleği bulgusu inceleme turunun sınıf taramasından (yeni, aynı kök). `HarnessDefaultsTests` iki CWD testi (gerçek çalışma dizini, paralelsiz koleksiyon) önce kırmızı; MT-CORE-139 |
| **F-315** · Harness modu OpenAI Chat Completions sağlayıcısında `400` ile düşüyordu | 2026-10-12, kusur-giderme (F-339 ile) | K-892 (kullanıcı kararı): web araması opt-in (yeni public `HarnessSettings.EnableWebSearch`); `messages.[1].role` kök sebebi tel üstünde ölçüldü — MAF'ın geçmiş katmanı akış güncellemesine yerinde `ConversationId` yazıyor, alttaki Tracon FICC'si onu görüp geçmişi düşürüyordu; `HarnessLoopBoundaryChatClient` kopya verir. Aynı dizilimin ikinci belirtisi `MaximumIterationsPerRequest`'in ölü olması (`2` ile 7 çağrı): sınır iç `ModelBinding.ToolLoopIterationLimit` ile iç döngüye ve fallback halkasına gider; halka birincilin `AllowConcurrentToolCalls`'unu da alır. `ScriptedChatCompletionsServer` (OpenAI'nin iki kuralını taklit eder) + `HarnessDefaultsTests` beş testi ve `FallbackChatClientTests` bir testi önce kırmızı; MT-OAI-094 |
| **F-338** · Workflow düğüm retry'ı, fallback zinciri ve online eval yargıcı kalıcı `4xx`'i geçici sayıyordu | 2026-10-12, kusur-giderme | Kullanıcı kararı (veto, enum değişmez, fallback dahil): `ProviderHttpStatus` (`HttpRequestException.StatusCode`, sonra `HTTP nnn` metni; 408/429 hariç her `4xx` kalıcı) düğüm retry'ında sınıflandırıcıdan önce, `FallbackRetryClassifier`'da ve online eval yargıcında okunur. Önce kırmızı: `WorkflowNodeRetryTests` (10), `FallbackRetryRegressionTests` + `FallbackChatClientTests` (16), `WorkflowNodeRetryEndpointTests` (veto kapalıyken 2), `OnlineEvalJobHandlerTests` (2). Açık sınıf bulguları F-353, F-354; MT-WF-124, MT-MYU-018 |
| **F-335** · Konsol başka yerden başlayan run'ın bekleyen isteğini açamıyordu; K-130 yorumu ve test adı K-130'a aykırıydı | 2026-10-12, kusur-giderme | Kullanıcı kararı (genel metin, adres doğrulaması): workflow ekranı run'ı adresten okur (`?runId=`); run sayfasının iki bağlantısı onu taşır; ekran kendi run'ını `replace` ile adrese yazar; başka workflow'un veya beklemeyen run'ın `runId`'si kart yerine not gösterir; rota `key={name}`. Üç E2E (`WorkflowTests.Pending_request_of_a_run_started_elsewhere_opens_from_its_run_page`, `…opens_from_a_link_that_names_its_run`, `A_run_this_screen_cannot_answer…`) ve `run-detail.test.tsx` vakası önce kırmızı. `WorkflowRunner` yorumu K-130'a çevrildi; `An_answered_run_still_lists_its_request` K-130'u sabitler (baştan yeşil); MT-UIRUN-067 |
| **F-340** · OpenAI sağlayıcısı `ProviderSettings` anahtarlarını sessizce yok sayıyordu | 2026-10-12, kusur-giderme | Kullanıcı kararı (tek `openai` öneki, hemen hata, isteğe bağlı `SupportedSetting`): `OpenAIChatClientFactory.CreateChatClient` → `ModelProviderSettings.Validate(binding, "openai", [])`; `OpenAIProviderNames.SettingsPrefix`/`SupportedSettings`; `ModelProviderSettingsContract.SupportedSetting` `virtual` ve nullable, OpenAI/Azure muafiyetleri kalktı. Önce kırmızı: `OpenAIChatClientFactoryTests` (4), `OpenAIModelProviderCredentialTests` (1), `OpenAIModelProviderSettingsContractTests` (1), `MultiProviderTests.Validate_endpoint_rejects_a_setting_on_every_OpenAI_registration` (4 kayıt). K-208 notu; MT-OAI-095; açık bulgular F-355, F-356 |
| **F-336** · Ses: özel sentezleyici ElevenLabs ayarına bağlıydı; başlangıç doğrulaması yoktu; dosya adı `.mp3` sabitti | 2026-10-12, kusur-giderme | Kullanıcı kararı (koleksiyon taraması, kendi/ListVoices sağlık, `ValidateOnStart`): `UseVoice` `TryAdd`'den önce tüketici kaydını okur (`VoiceProviderBinding`); ElevenLabs kuralları yalnız yerleşik istemci bağlıyken. `UseVoice` ve `UseOpenAILive` `ValidateOnStart` alır. Fiyat, `speak` limiti ve sağlık bağlı uygulamanın `ProviderName`/limitini okur; tek `AttachmentFileNames` yardımcısı (HTTP, `speak`, konuşma, görsel). Önce kırmızı: `CustomVoiceProviderTests` (7), `OpenAILiveStartupValidationTests` (2), `VoiceEndpointTests` OGG adı, `VoiceConversationTests` `.bin` adı. Açık bulgular F-357…F-359; MT-MM-125 |
| **F-349** · `aiJudge` yargıcının model çağrısı run `usage`'ına, maliyetine ve kotasına girmiyordu; özetleyici agent fiyatıyla fiyatlanıyordu | 2026-10-12, kusur-giderme | K-893 (kullanıcı kararı): yan kanal payı `(provider, model)` ile birikir (`SideChannelUsageTrackingChatClient`), `RunCostCombiner` her payı kendi modeliyle fiyatlar, karma run'da birim fiyat alanları `null`; kota aynı toplamı tüketir; `RunBudgetChatClient` `UsageContent`'leri toplar. Önce kırmızı: `HarnessLoopTests.An_aiJudge_loop_records_the_judge_tokens_and_prices_them_at_the_judge_model` (akışlı/akışsız; 82 yerine 12), `RunRecordingAgentTests.Summarization_tokens_are_priced_at_the_summarization_model` (0,042 yerine 0,0028), `RunBudgetAccountingTests.A_stream_with_two_usage_contents_charges_the_budget_with_their_sum` (500 yerine 200). Açık bulgular F-360…F-363; MT-OBS-067 |
| **F-334** · `ExecutorFailed` olayı ham istisna mesajını kalıcı yazıyordu | 2026-10-12, kusur-giderme | K-640 deseni: eşleyici istisnanın mesajını okumaz, runner'ın logladığı güvenli metni (`SafeErrorText`, tek `ref`) yazar; aynı hata olayda ve run satırında aynı `ref`'i taşır, günlüğe bir kez yazılır. K-640 cırcırı `RunEventDraft` başlatıcılarını da tarar (taban: `RunEventWriter.CompleteAsync`, `RunError.Message` zaten güvenli). Önce kırmızı: `WorkflowFunctionNodeTests.A_failed_node_event_carries_the_safe_text_and_never_the_raw_message` ("connect to media-worker:8080 failed"), `WorkflowFailureTextTests` (SSE gövdesi). MT-WF-114/118 bayat test adı ve "gerçek mesaj" iddiası düzeltildi; MT-WF-125 |
| **F-337** · Workflow kökü atıf taşımıyordu (`Labels`/`UserId`, kota `RunId`, `RunAttributionGate`) | 2026-10-12, kusur-giderme | Kullanıcı kararı (her run başlatan uç): atıf kapsamdan ÖNCE okunur, kapsam `UserId`/`Labels` taşır; kota kaydı `RunId`/`UserId` taşır; `RunAttributionGate` workflow run/resume/respond, replay, trigger kabulü ve iki OpenAI ucunda (OpenAI hata şekliyle `Validate`); `RunAuthorizationCoverageTests` sayıyı zorlar; workflow run/resume `400` beyan eder. `WorkflowAttributionTests` 8 case önce kırmızı. Açık bulgu F-364; MT-SEC-213 |
| **F-348** · Başarılı handler'dan sonraki `store` hatası handler hatası sayılıyordu | 2026-10-12, kusur-giderme | Varsayılan kararlar (kira sınırlı tekrar, owner fencing ertelendi — K-886): yalnız `handler.ExecuteAsync` handler `try`'ındadır; sonuç yazımı `RetryFinalizationAsync` ile artan beklemeyle, kanıtlanan kira penceresi (son kira/yenileme + `LeaseDuration` − yenileme aralığı) ve kapanış sınırında tekrarlanır; pencere biterse ref'li Error ve kira dolumu yolu. Kardeş `AgentBatchJobHandler` raporu `try` dışına alındı. `JobWorkerLeaseContractTests` 10 case ve `AgentBatchJobHandlerTests` bir case önce kırmızı. Açık bulgular F-365…F-367; MT-JOB-134 |
| **F-351** · Kuyruklu run isteğin `culture`'ını ve deney kolunu düşürüyordu | 2026-10-12, kusur-giderme | K-894 (kullanıcı kararı): kol enqueue anında atanır, payload kültür ve kolu taşır; worker sürüm+kültürle çözer ve kolu `IExperimentStore`'a karşı doğrular, uymazsa kapalı düşer; kültür run kaydına yazılmaz. Önce kırmızı: `CultureInstructionEndpointTests.A_queued_run_uses_the_requested_cultures_instructions` ("Reply briefly."), `ExperimentEndpointTests` iki case (AgentVersion 2; sahte iş `Completed`). Açık bulgular F-368, F-369; MT-CORE-140 · MT-EVAL-104 |
| **F-352** · Onay devamı güncel sürümle koşuyordu (varsayılan replay ve alt çağrılar ölçüldü) | 2026-10-12, kusur-giderme | K-895 (kullanıcı kararı): (a) kaynak run'ın sürümü ve kolu taşınır (`SourceRunAgentResolver`), payload agent adı kaynakla eşleşmezse ya da kaynak yoksa iş kapalı düşer; sınıf taraması: `RunContinuationJobHandler` kolu düşürüyordu, düzeltildi. (b) varsayılan replay ve (c) alt agent/workflow adımının güncel sürümü belgelenmiş tasarım (K-309), site metni netleşti; alt sürüm bağlama F-300'e eklendi. Önce kırmızı: `ApprovalResumeVersionTests` üç case (AgentVersion 2; sahte agent `Completed`), `RunContinuationTests.Continuation_keeps_the_interrupted_runs_experiment_arm`. Ölçülen komşu kusur F-370; MT-JOB-135 |
| **F-332** · Retention ve erasure harici ek baytını hiç silmiyordu; workflow eki 7 günde yetim sayılıyordu | 2026-10-12, kusur-giderme | K-896 (kullanıcı kararı): önce bayt sonra satır (migration yok); retention hatada satırı tutar, erasure geri alınır; ek deposu da önce baytı siler; workflow run'ı oturumunun eklerine sahiptir. `AttachmentByteLifecycleTests` üç SQL sağlayıcısında 11/12 önce kırmızı; `InMemoryAttachmentStoreTests.A_failed_byte_delete_keeps_the_attachment`. Açık bulgular F-371…F-373; MT-RET-078 · MT-DVR-011 · MT-WF-126 |
| **F-333** · Belgelenen `workflow.request.pending`, `job.*`, `eval.completed` webhook'ları yayınlanmıyordu; workflow kökü `run.*` üretmiyordu | 2026-10-12, kusur-giderme | Kullanıcı kararı (hepsini bağla): workflow kökü `run.*` (`WebhookRunSummary.Kind`, ağaç usage/maliyeti) ve istek başına `workflow.request.pending` (istem yok); job worker üç terminal noktada `job.*` (iptal ve teslim job'u hariç); eval işleyicisi `eval.completed` (başarısızlıkta son deneme; yeni public `WebhookEvalSummary`, `WebhookEventPayload.Eval`). Konsol olay listesine eksik `run.score.low` eklendi. `WebhookEventPublishingTests` altı case (beşi önce kırmızı, teslim job'u koruması baştan yeşil); MT-OBS-068 |
| **F-341** · `respond` dönüştürülemeyen yanıtta `200` + `Failed` dönüyordu; kayıt komşu düğüm tip uyumunu denetlemiyordu | 2026-10-12, kusur-giderme | K-908 (ana oturum varsayılanı): yanıt akıştan önce `IWorkflowRunner.ValidateResponseAsync` ile kayıtlı istek olayından (`form` + yeni `responseAssembly`) denetlenir, `400` alır ve run açmaz; runner akış içi otoritedir. Kayıt ve derleyici zinciri atanabilirlikle denetler (MAF 1.20.0 ölçüldü: uyumsuz mesaj sessiz düşer, run çıktısız `Completed` olur). F-324'ün tip zinciri maddesi kapandı. Önce kırmızı: `WorkflowRespondValidationTests` (4), `WorkflowNodeTypeChainTests` (3). Açık: F-396, F-398; MT-WF-138 |
| **F-342** · Belge-kod kaymaları (video tüketicisi turu demeti) | 2026-10-12, kusur-giderme | Belge koda göre düzeltildi: düğüm hatası maskeleme kuralı ve `Canceled` nedenleri (`concepts/workflows.md`), fallback `ChatOptions` devri (`guides/reliability.md`; K-444 satırına düzeltme notu), beş kapı ve bekleme kotası (`concepts/tools.md`), editörün `loop`'u koruması (`ui.md`), iptalin handler'a yalnız `JobContext.IsCancelledAsync` ile ulaşması (`guides/background-work.md`); XML: `JobHandlerKeys.Workflow`, `SkillRoots`, `MaxOutputBytes` (akış başına), `ToolCallUsage`, `ModelFallback`, `ModelBinding`. Resume hatası grafiğin yapısını adlandırır (`WorkflowRunner`). Konsolda `ToolCallUsage` gösterimi Faz 199'a girdi |
| **F-343** · Eval yolunda bağlam kayboluyordu: `EvalCase.Context` agent'a gitmiyor, terfi belgeyi, 8 KB üstünü ve parametreleri düşürüyordu | 2026-10-12, kusur-giderme | K-905: Context belge kanalıyla sorgudan önce gider; terfi `run_inputs`'u önce okur (tam sorgu, belge blokları `Context`'e aynen, değerler yeni `EvalCaseDraft.Parameters`'a); kayıt yoksa `…[truncated]` işaretli olay metni; taşınamayan girdi `422`; `TryGetSummary` `JsonElement` okur (SQL replay de düzeldi). Önce kırmızı: `EvalJobHandlerTests` (2), `EvalCaseInputFidelityTests` (6), `RunToCasePromotionSqlTests`, `RunToCasePromoterTests`, Vitest `eval-detail`. Açık: F-386, F-403; MT-EVAL-106 |
| **F-344** · Reconciliation yetim workflow kökünü ve alt run'ları agent turu gibi sürdürüyordu | 2026-10-12, kusur-giderme | K-909: yalnız kök agent run'ı sürdürülür (`RunContinuationEligibility`; uzlaştırıcı ve işleyici); workflow kökü ve alt run (`ParentRunId` ya da `Depth > 0`) `Failed` kalır, `RunContinuationBlocked` workflow için resume'u adlandırır. Aynı yerde kiracı sınıfı kapandı: ret olayı başka kiracıda düşüyordu, zincir yürüyüşü ataları göremiyordu. F-321'in "çocuk run'ları hariç" maddesi kapandı. Önce kırmızı: `RunContinuationTests` (6), `RunStoreContract` dört depoda. Açık: F-399; MT-JOB-143 |
| **F-354** · Devre kesici kalıcı `4xx`'i ve devrenin içindeki Tracon kararlarını sağlayıcı arızası sayıyordu | 2026-10-12, kusur-giderme | K-906: devre yalnız sağlayıcı sınırında etiketli hatayı ve istenmemiş iptali sayar; `401`/`403`, `408`, `429`, `5xx`, durumsuz hata sayılır, diğer kalıcı `4xx` sayılmaz; sınıf taramasıyla run bütçesi, eksik ek ve yeniden fırlatılan tool hatası da sayılmaz; sayılmayan hata nötr. Önce kırmızı: `CircuitBreakerFailureClassificationTests` (12), `SharedCredentialCircuitTests` (kiracı B `502`). Açık: F-387, F-404; MT-OAI-096 |
| **F-360** · Yan kanal çağrısının fallback'i run'ın modelini, olayını ve agent fiyatını yazıyordu | 2026-10-12, kusur-giderme | K-907: fallback atfı yalnız agent'ın kendi bağlamasında; `SideChannelUsageTrackingChatClient` çağrı süresince kapsam kopyasını `SideChannelFallback` ile açar, pay cevap veren modelle fiyatlanır (K-893), geçiş loglanır. Önce kırmızı: `HarnessLoopTests.An_aiJudge_fallback_does_not_rewrite_the_runs_model_or_its_price` (akışlı ve akışsız; maliyet 0,978 yerine 0,765), `SideChannelFallbackAttributionTests` (2). Açık: F-395; MT-MYU-019 |
| **F-364** · Kuyruklu ve zamanlanmış workflow run'ı atıf taşımıyordu | 2026-10-12, kusur-giderme | K-904: workflow ve agent batch yükünde atıf zarfı `{items,userId,labels}`; `IJobDispatcher` çağrı anının, inbound trigger kabul isteğinin atfını yazar (agent dalı dahil); zamanlama zarfını Admin yazar, sınır aşımı `400`; handler kapsamı kendi gövdesinde açar; kardeş `AgentBatchJobHandler`. Önce kırmızı: `QueuedWorkflowAttributionTests` (6). Kapı: `JobAttributionScopeSiteTests` (sınıf dördüncü kez). Açık: F-397, F-402; MT-JOB-142 |
| **F-365** · `EvalJobHandler` son yazımı dış `catch`'e düşüyordu | 2026-10-12, kusur-giderme | F-348 deseni (K-886): yalnız vakalar `try`'da; kapanış yazımı `JobOutcomeWriteRetry` ile kiranın üçte biri boyunca tekrarlanır, olmazsa hata worker'a gider ve sonraki deneme yalnız yazımı tekrarlar. Önce kırmızı: `EvalJobHandlerFinalWriteTests` (2). Kapı: `JobHandlerOutcomeWriteSiteTests`. Açık: F-392, F-394; MT-EVAL-105 |
| **F-366** · `WebhookDeliveryJobHandler` başarılı gönderimden sonraki `store` hatasında yeniden teslim ediyordu | 2026-10-12, kusur-giderme | F-348 deseni: `2xx` sonrası üç yazım ortak pencerede tekrarlanır, olmazsa ref'li Error ve deneme normal biter; `Pending` dışı teslim gönderilmez; site `X-Tracon-Delivery` ile tekilleştirmeyi yazar. Önce kırmızı: `WebhookDeliveryFinalWriteTests` (3; gerçek worker "1 beklendi, 2 geldi"). Açık: F-388, F-393; MT-OBS-069 |
| **F-367** · Kuyruklu turun oturum kaydı hatası başarılı run'ı handler hatası yapıyordu | 2026-10-12, kusur-giderme | K-903: kayıt turun parçası kaldı (F-133); ortak `QueuedTurn` kaydı worker'ın kanıtlanan kira penceresinde artan beklemeyle tekrarlar; pencere biterse iç `NonRetryableJobException` → iş `tracon.job.session-save-failed` ile o denemede `Failed`, handler tekrar koşmaz; onay yolunda run `Failed`. Önce kırmızı: `QueuedTurnSessionSaveTests` (6), `RunContinuationTests` (1). Açık: F-391; MT-JOB-140 |
| **F-369** · Kuyruklu run'ın çocukları etiketi, onay devamı ve sürdürmenin çocukları kullanıcıyı görmüyordu | 2026-10-12, kusur-giderme | K-904: yük `labels` (string→string) taşır, worker kapsamı kullanıcı ve etiketle açar; devam ve sürdürme kaynak run kaydının atfıyla açar (`QueuedRunAttribution`); bozuk değer düşer; örnek `DemoRunAttributionContext` istek yokken ortam kapsamını okur. Önce kırmızı: `QueuedRunAttributionTests` (2), `RunContinuationTests` (1), `AgentRunJobHandlerTests` (1). Kapı: `JobAttributionScopeSiteTests`. Açık: F-390; MT-SEC-214 |
| **F-370** · Onay devamının ikinci onayı posta kutusuna yazılmıyordu | 2026-10-12, kusur-giderme | Kanca ve `RecordPendingApprovalsAsync` `QueuedTurn`'e taşındı (oturum önce, onay sonra); sınıf taramasıyla `RunContinuationJobHandler` da kullanır. Önce kırmızı: `ApprovalChainTests`, `RunContinuationTests.A_continuation_that_asks_for_approval_lists_it_and_the_decision_resumes_it`. Açık: F-389; MT-JOB-141 |
| **F-371** · Veri sahibi dışa aktarımı ve silmesi `workflow_checkpoints`'i atlıyordu | 2026-10-12, kusur-giderme | K-911: `workflow_checkpoints` hedefi (oturum ya da run kimliği); `AwaitingInput` run'ınki de silinir (K-888 yalnız retention); `state` saklandığı JSON metni olarak dışa aktarılır. Sınıf: `run_scores` oturumla bulunan run'lar için de hedef, silme sırası `Runs`'tan önce. `DataSubjectStoreTests` üç SQL sağlayıcısında 3 test önce kırmızı; kapı `DataSubjectCoverageTests`. Açık: F-400, F-401; MT-DVR-012 |
| **F-372** · Retention arşiv modu arşivlemediği satırı siliyordu | 2026-10-12, kusur-giderme | K-910: `IRetentionStore.ArchiveBatchAsync` (varsayılan gövdeli); silme yalnız arşivlenen anahtarlarla, yüklem yeniden denetlenir, sink transaction dışında. Ölçüldü: 7 satır ve 3'lük batch'te üç sağlayıcıda 3-4 satır arşivsiz silindi. Önce kırmızı: `RetentionStoreContract.Archive_batches_delete_only_the_rows_they_archived` (×3), `RetentionExecutorTests.Archive_mode_deletes_only_the_rows_the_sink_received`; `RetentionArchiveKeyTests`. MT-RET-079 |
| **F-385** · `Scheduling` lane ve handler-key koleksiyonları yapılandırmadan bağlanmıyordu | 2026-10-13, kusur-giderme | `BindSchedulingCollections`: `Lanes` ve `HttpSchedulableHandlerKeys` liste olarak (varsa varsayılanın yerine), `MaxConcurrentJobsPerLane` ve `LaneByHandlerKey` anahtar yazıldığı gibi; sayı olmayan lane bütçesi ve boş lane eşlemesi atlanmaz; doğrulayıcı açılışta adlandırır. `TraconSchedulingOptionsBindingTests` (5) önce 3 kırmızı; `TraconSchedulingOptions` kapsam tabanından çıktı (F-247 ratchet). Karar açılmadı (belgelenen davranış). MT-JOB-144 |
| **F-388** · Webhook teslim ön denetimi kurulumun egress ayarını almıyordu | 2026-10-13, kusur-giderme | Teslim handler fabrikası `IOptionsMonitor<TraconEgressOptions>` geçirir. `WebhookDeliveryEgressOptionTests` DI'dan kurulan handler'la önce kırmızı (`Dropped`, mesaj zaten ayarlanmış ayarı öneriyordu). Sınıf taraması iki desenle: `?? new …Options()` yedeği olan 11 yer temiz; bağımsız inceleme ikinci deseni buldu — `UseOpenAICompatible()` fabrikası `OpenAIModelProvider`'a `egressGuard` geçirmiyordu (diğer dört sağlayıcı geçiriyor), kiracı bağlamasının uç noktası denetimsiz aranıyordu. Aynı düzeltme; `OpenAICompatibleTenantEndpointGuardTests` önce kırmızı (`127.0.0.1:9`'a dört deneme). MT-SEC-215 · MT-SEC-217 |
| **F-384** · Görsel üretimi kiracı egress politikasını ve kiracı anahtarını uygulamıyordu | 2026-10-13, kusur-giderme | K-912: `ImageGeneratorResolver.ResolveForTenantAsync` iki yolun ortak noktası; politika dışı sağlayıcı ve bağlamalı kiracı `TenantProviderRefusedException` → uç `403`, tool hatası; kurulum anahtarına düşülmez. `ImageTenantPolicyTests` (4) önce 3 kırmızı; OpenAPI `403` ve üretilen iki istemci tazelendi. Sınıf: embedding yolu F-416, konuşma Faz 198; kiracı anahtarını kullanan görsel sağlayıcı F-417. MT-SEC-216 |
| **F-383** · Alt agent harcaması kiracı kotasına ve kökün webhook'una girmiyordu | 2026-10-13, kusur-giderme | K-913: kök, kotaya ve `run.*` webhook'una ağacın toplamını yazar; alt run toplamı kök satırı kapanmadan depodan okunur, yalnız ağaç alt run rezerve ettiyse. `AgentTreeQuotaTests` (4, akışlı ve akışsız) önce 3 kırmızı. Sınıf taraması: webhook yükü aynı açık (aynı düzeltme); metrikler run başına doğru. Bağımsız inceleme sonrası: depo okuması iptal token'ı almaz; satırın önceki denemeden kalan usage'ı ağaçtan düşülür. Açık kalan: kökten uzun yaşayan arka plan alt run'ı (F-418), ağaç içinde karışık para birimi (F-419). MT-RET-080 |
| **F-423** · `QueuedRunParameterTests` model isteğini bekleyip run durumunu tek okumayla `Completed` sayıyordu | 2026-10-13, kusur-giderme (Faz 201 kapanışında kapıyı kırdı) | Test yarışı, ürün kusuru değil: run model cevabından sonra kapanır. Önce kırmızı: tek test koşumunda temel commit `643b9eee` 8'de 7, faz dalı 8'de 7 kırmızı; tam `ic-dongu`'da bir kez. `WaitUntil` kapanmış durumu bekler (sınıfın `:181` deseni); sonra sınıf 8/8 yeşil. Sınıf taraması: aynı sınıfta ve `AgentParameterPathTests`, `CultureInstructionEndpointTests`'te model isteğini bekleyen diğer beş test yalnız isteği okur, durum okumaz — temiz |
| **F-431** · Workflow uçları drain sırasında yeni run ve kuyruk işi kabul ediyordu — sınıf: süreç içinde run başlatan on yüzeyin dokuzu kapısızdı | 2026-10-13, kusur-giderme | Ölçüldü: drain sırasında `resume` `200` ile yeni run açtı. `DrainGate` artık workflow run/resume/respond ve replay'de (`503`, OpenAPI + iki istemci), `/v1/chat/completions` ve `/v1/responses`'ta (OpenAI zarfı, `type: service_unavailable`), MCP `tools/call`'da (`isError`), A2A'da (çağrı hatası) ve ses soketinde (upgrade öncesi `503`). Job yazan yüzey (trigger, eval, schedule, onay) Faz 87 kuralıyla kapsam dışı (bakımcı onayı). Önce kırmızı: 10 yeni test (`DrainTests` 8, `A2AEndpointTests`, `McpServerEndpointTests`). Kapı: `RunAuthorizationCoverageTests.Every_known_in_process_run_starting_surface_refuses_while_draining`. Site: `guides/reliability.md` |
| **F-432** · Ajan kuyruklu run'ında enqueue düşerse satır `Queued` kalıyordu — sınıf: trigger'ın ajan hedefi de | 2026-10-13, kusur-giderme | Üç yol (ajan kuyruk ucu, trigger ajan hedefi, workflow kuyruk ucu) `QueuedRunPlaceholder.EnqueueAsync`'ten geçer: satır `Failed` + güvenli hata metni, kapatma düşerse log, istisna yeniden atılır. Önce kırmızı: `AsyncRunTests` ve `TriggerEndpointTests` (`RunStatus.Queued`). Onay devamı bilinçli istisna: kimliği onaydan türetilir, kararı tekrarlamak eksik job'u yazar. `RefusingJobStore` ortak test yardımcısı oldu |
| **F-401** · Dışa aktarım korumalı sütunları şifreli metin olarak veriyordu; checkpoint durumu korunmuyordu | 2026-10-13, kusur-giderme | K-934: `SqlDataSubjectStore.ExportAsync` korumalı sütunu `DataSubjectTargetRegistry.ProtectedExportColumns` ile düz metin okur; `ProtectedColumn.WorkflowCheckpointState = 11` (checkpoint deposu yazar/okur, preflight çözer, migration yok). Önce kırmızı: SQLite `Data_subject_export_returns_protected_columns_as_plaintext` (`$apEnc`) ve checkpoint testi; üç sağlayıcıda yeşil. Kapı: `DataSubjectCoverageTests.Every_protected_column_of_an_export_target_is_exported_as_plaintext` (mutasyonla kırmızı ölçüldü). Sınıf: retention arşivi → F-447 |
| **F-387** · Durum okuyucusu System.ClientModel'in `Status: nnn` mesajını görmüyordu — sınıf: Anthropic ve Google biçimleri, istenmemiş iptal | 2026-10-13, kusur-giderme | Ölçüldü (`ProviderErrorStatusTests`, gerçek SDK + boş gövdeli yerel sunucu, 4 sağlayıcı × 400/401/403/404, 16'sı önce kırmızı): System.ClientModel `Status: 404 (Not Found)`, Anthropic `Status Code: NotFound`, Google.GenAI `status code 404: Not Found`. Tek okuyucu `ProviderFailureNormalizer.ReadFrameStatus`; `FallbackRetryClassifier` iki kendi desenini bırakıp `ProviderHttpStatus.ReadFrame` okur. İptal yalnız sağlayıcı istemcisinin kendisi attıysa devreye sayılır (`ProviderFailureTaggingChatClient` `Exception.Data` işareti; `CircuitBreakerFailureCountingTests`). K-817 parmak izi bu hatalarda bir kez değişir (`(HTTP nnn)` eklenir). `DefaultRunErrorClassifier` hâlâ yalnız normalleştirilmiş mesajı okur (F-353) |
| **F-416** · Bilgi tabanı embedding çağrısı kiracı egress politikasından geçmiyordu — sınıf: `Tracon:Knowledge` bölümü hiç bağlanmıyordu | 2026-10-13, kusur-giderme | K-936: `EmbeddingProviderAccess` üç embedding yolunu (yükleme, arama ucu, `search_knowledge`) politika + kiracı anahtarı kapısından geçirir; ad `TraconKnowledgeOptions.EmbeddingProvider`, yoksa üretecin bildirdiği; ad yoksa politikalı kiracı reddedilir. Uç `403` (OpenAPI + iki istemci). `KnowledgeTenantPolicyTests` (7; kapı kapatılınca 5'i kırmızı). Ölçüldü: MEAI metadata adı OpenAI/Azure `openai`, Google `gcp.gen_ai`. `BindKnowledge` eklendi, `TraconKnowledgeOptionsBindingTests` ratchet'ten çıkardı |
| **F-441** · Manuel kabul spesifikasyon ağacının bütçesi doldu | 2026-10-13, doğrudan | Kalan 46 tarihli/üstü çizili blok (14 spesifikasyon dosyası: `Düzeltildi`, `Ölçüldü`, `Doküman düzeltmesi`, `Kimlik düzeltmesi`, `Not`, `~~Eski beklenti~~`) `arsiv/manuel-test-kosum-2026-09/`'daki eşlerine case başlığı altında aynen taşındı. Beklentiyi taşıyan dört notun (MT-PKG-011, MT-PG-001, MT-SQL-001, MT-SQL-010) yerine kısa beklenti maddesi kaldı; MT-PG-001'in beklentisi Faz 197 taşımasından beri boştu, o da doldu. Ölçüm: 2 550 692 → 2 535 799 / 2 556 000 B (14 893 B boşaldı, 20 201 B boş). Spesifikasyonda bilerek kalan üç not test edene yön verir: MT-PG-062 log gürültüsü, MT-SQL-020 bayat migration sayısı, MT-SQL-042 Docker sürümü. Ağaç hâlâ DAR (%1); taşınacak tarihli içerik kalmadı, sonraki doluşta yol kalibrasyon kararıdır |
| **F-291** · Anahtar alanına değer yazan operatörün değeri `400`'de ve teslim `error`'ında yankılanıyordu | 2026-10-13, kusur-giderme | K-059 ihlali: `ConfigurationKeyGuard` reddedilen adı mesaja yazıyordu; mesaj yanıt gövdesine ve `webhook_deliveries.error`'a gitti. Mesaj artık yalnız alanı ve izinli anahtar uzayını adlandırır. Muhafız tek yerdir, dört yüzey (MCP, BYOK, webhook, trigger) ve teslim yolu onu çağırır. Önce kırmızı: `ConfigurationKeyGuardTests.A_refused_name_is_never_echoed`; eski test yankıyı artık beklemez. Sınıf taraması: muhafız geçtikten sonraki log'lar adı yazar, o ad izinli uzaydadır |
| **F-382** · `GET /api/workflows/functions` ve `GET /api/schedules/handler-keys` aynı adlı kaydı gölgeliyordu | 2026-10-13, kusur-giderme | K-937: iki ad ayrılmıştır, kayıt `400` (harf ayrımsız), `AddWorkflow` `ArgumentException` atar. Önce kırmızı: `WorkflowEndpointTests.The_name_the_function_list_uses_is_refused`, `SchedulingEndpointTests.The_name_the_handler_key_list_uses_is_refused`, `WorkflowCatalogTests.A_code_workflow_cannot_take_the_name_the_function_list_uses`. Sınıf taraması: kullanıcı adlı koleksiyonun yanında sabit `GET` segmenti yalnız bu ikisinde var. OpenAPI açıklamaları ve iki istemci yenilendi |
| **F-386** · İçerik koruyucu maskelenen belgeyi işaretsiz metin yapıyordu | 2026-10-13, kusur-giderme | `ContentGuardMessageMasker.WriteText` belge içeriğini `DocumentChannelMessageBuilder.BuildMasked` ile yeniden kurar: işaret maskeli metinden gelir, okunamazsa ad `[masked document]`; ham ad ve ham içerik hash'i kayda girmez. Önce kırmızı: `DocumentChannelTests.A_masked_document_stays_a_document_in_the_run_record_with_the_masked_values` (`DocumentAttached` yoktu, `RunStarted.Text` belge metniydi) |
| **F-389** · Batch ve eval kök run'ı onay isterse `AwaitingApproval`'da kalıyordu — sınıf: `/v1/chat/completions` | 2026-10-13, kusur-giderme | `SessionlessApprovalRun`: batch öğesi ve eval vakası onayda düşer (hata metni onayı adlandırır), run `Failed` kapanır; `AgentRunJobHandler` kuralı. Önce kırmızı: `SessionlessApprovalTests` (2, gerçek işçi; batch öğe `Completed`, run `AwaitingApproval` idi). Sınıf taraması: `/v1/chat/completions` oturumsuz onayda `200` + boş içerik döner, run `AwaitingApproval` kalır → F-448 (yanıt biçimi kararı ister) |
| **F-396** · Workflow SSE uçları girdi ve durum hatasını akış başladıktan sonra bildiriyordu | 2026-10-13, kusur-giderme | K-938: denetimler dalın üstünde (`CheckSourceAsync`, `CheckCheckpointAsync`), kodlar kuyruklu yolunkiler: olmayan/başka kiracının run'ı `404`, geçersiz `sessionId`, olmayan checkpoint, beklemeyen run'a yanıt `400`; run satırı açılmaz. Önce kırmızı: `WorkflowEndpointTests` 4 test (olmayan checkpoint `Failed` satır da açıyordu); iki eski test akış içi hatayı bekliyordu, güncellendi (`WorkflowParameterTenantIsolationTests` artık `404`). OpenAPI açıklamaları yenilendi |
| **F-400** · Silme commit edildikten sonra koşan run oturumu ve checkpoint'i geri yazabiliyordu | 2026-10-13, kusur-giderme | Tablo kapsamı K-935 ile kapanmıştı. K-939: gerçek silme öznenin `Running`/`Queued` run'ı varken `409` (`activeRunIds`), ön izleme listeyi `DataSubjectErasureResult.ActiveRunIds`'de verir, `force=true` siler. Önce kırmızı: `DataSubjectEndpointTests` (`409` iki durum için, ön izleme listesi, `force`, bitmiş run engellemez). OpenAPI `409` + `force`, iki istemci yenilendi (C# imzası: `force` iptal token'ından önce) |
| **F-361** · `EvaluatorRunJudge` harcaması hiçbir yere yazılmıyordu | 2026-10-13, kusur-giderme | K-940: her yargı `judge:{name}` `RunKind.Eval` run'ı açar; kullanım istemci sarmalayıcısında sayılır, maliyet ve `RecordJudgeCost` yazılır; atan evaluator run'ı `Failed` kapatır. XML düzeltildi. Önce kırmızı: `EvaluatorJudgeCostTests` (2, gerçek `AddEvaluatorJudge` kaydıyla; satır yoktu) |
| **F-447** · Retention arşivi korumalı sütunu zarf hâliyle `IArchiveSink`'e veriyordu | 2026-10-13, kusur-giderme | K-941: `SqlRetentionStore` okuma ve toplu arşiv yolu `ProtectedValue.RowReader` ile çözer (`RetentionTargetRegistry.ProtectedArchiveColumns`: olay metni/yükü, tool argüman/sonucu, checkpoint, ek içeriği, oturum durumu, run girdisi). Önce kırmızı: SQLite `ContentProtectionTests.A_retention_archive_hands_protected_columns_to_the_sink_as_plaintext` (`$apEnc`). Kapı: `DataSubjectCoverageTests.Every_protected_column_of_a_retention_target_is_archived_as_plaintext` (üç sağlayıcı). Site: `governance.md`, `security.md` |
| **F-398** · `object` girdili ilk fonksiyon düğümü iki kez koşuyordu | 2026-10-14, kusur-giderme | K-975: ilk düğüm `object` alamaz (kayıt `400`, saklı tanım düğümden önce `Failed`); token resume'da gerekli (tokensız resume 10 dk asılı, ölçüldü). Önce kırmızı: `WorkflowEntryFunctionRunsOnceTests` (2/3). `TurnToken`'ın yalnız `object`'e atanabildiği `WorkflowNodeTypeRuleTests`'te sabit. Sınıf taraması: token yalnız `WorkflowRunner.StartAsync`'ten gider; kod grafiği `AddWorkflow` XML'inde belgeli |
| **F-419** · Ağaç maliyeti farklı para birimindeki run'ları tek etiketle topluyordu — sınıf: istatistik, zaman serisi, deney kolu, yargıç maliyeti | 2026-10-14, kusur-giderme | K-976: `CurrencyTotals` + public `CurrencyCount`; karışık toplam tutarsız. Önce kırmızı: `RunStoreContract.No_cost_total_adds_amounts_in_two_currencies` (bellek, SQLite; dört depoda yeşil). Kök agent kotası ve webhook'u kendi maliyetini yazar (`AgentTreeQuotaTests`); workflow birleşimi aynı kural (`WorkflowTreeSpendTests`); konsol *mixed currencies* |
| **F-448** · `/v1/chat/completions` onayda `200` + boş içerik dönüyordu, run `AwaitingApproval` kalıyordu — sınıf: oturumsuz yerel run, MCP, A2A | 2026-10-14, kusur-giderme | K-977: `409` (`approval_required` / `approval_unanswerable`), akışta error frame; dört yüzeyde run `Failed` (`approval_unanswerable`); MCP görev eşlemesi reddi korur. Önce kırmızı: `SessionlessApprovalTests` (6). Açık: F-485 (A2A metni) |
| **F-433** · `KeepCheckpointsAfterCompletion=false` iken zarif durdurulan workflow'un devamı baştan başlıyordu | 2026-10-14, kusur-giderme | K-978: işçi durunca `Canceled` kapanan run checkpoint'ini tutar, devam bitince temizler. Önce kırmızı: `WorkflowRecoveryTests.A_run_stopped_by_a_graceful_shutdown_is_continued_by_the_next_worker(false)`. Sınıf taraması: checkpoint silen tek yer `WorkflowRunner.CompleteAsync` |
| **F-391** · Onay satırı yazımı tekrar edilemiyordu, hata modeli yeniden çağırıyordu | 2026-10-14, kusur-giderme | K-979: kira penceresinde tekrar, kimlikle önce okuma, `tracon.job.approval-save-failed`. Önce kırmızı: `QueuedTurnSessionSaveTests` (4; `ThrowAfter` fault injection). Sınıf taraması: onay satırı yazan tek yer `QueuedTurn` |
| **F-350** · Onay bekleyen yanıt yapılandırılmış doğrulamada "boş" diye reddediliyordu — sınıf: istemci tarafı tool çağrısı | 2026-10-14, kusur-giderme | K-980: bekleme turu doğrulanmaz. Doğrulandı ve önce kırmızı: `StructuredResponseEndpointTests` (4, akışlı/akışsız) |
| **F-481** · Yalnız diskte duran skill'i agent adlandıramıyordu | 2026-10-14, kusur-giderme | K-981: katalog ve doğrulayıcı `SkillRoots`'u tarar, disk içeriği parmak izine girer. Önce kırmızı: `SkillScriptExecutorRoutingTests` (3; HTTP → derleme → MAF → executor). Açık: F-486, F-487 |
| **F-429** · İş işçisi tekrarı aynı cevabı alacak model hatasını da yeniden deniyordu — sınıf: düğüm yeniden denemesi, çevrimiçi değerlendirme, eval'in başarısız yazımı | 2026-10-14, kusur-giderme | K-982: tek karar `FinalFailure` (kararlı kimlik: `output_truncated`, `tenant_provider_refused`, kalıcı 4xx `upstream_error`; mesaj okunmaz; `compilation_failed` geçici olabilir, bağımsız inceleme listeden çıkardı); işçi geneli, tüketicinin ham hatası tekrar dener, `JobRetryException` ve süre sınırı karar dışı. Önce kırmızı: `FinalFailureJobRetryTests` (eval yargıcı kesildi, kuyruklu run `400`; ikisi 3. denemedeydi), `EvalJobHandlerFinalWriteTests` (başarısız yazımın `store` hatası asıl hatayı gizliyordu). Muhafız: `429` ve tüketicinin `404`'ü tekrar dener |
| **F-353** · Normalleştirilmiş `429` `ProviderError` sayılıyordu; yalnız `HttpRequestException.StatusCode`'da taşınan durum kayda girmiyordu | 2026-10-14, kusur-giderme (F-429 sınıf taraması) | K-984: ikinci daraltma `(HTTP 429)` → `RateLimited`; `UpstreamFailure` durumu `ProviderHttpStatus.Read`'den okur ve `StatusCode` özelliği taşır. Önce kırmızı: `DefaultRunErrorClassifierTests.A_normalized_provider_429_is_rate_limited`, `ProviderErrorClassificationTests.A_status_carried_only_by_the_property…`. `transport_error` yarısını F-387 kapatmıştı |
| **F-442** · Kiracı sağlayıcı reddi CLR adıyla kaydediliyordu; adında `timeout` geçen ret düğümde yeniden deneniyordu — sınıf: model yolu, görsel, ses, embedding, canlı ses | 2026-10-14, kusur-giderme (F-429 sınıf taraması) | K-983: public `TraconTenantProviderRefusedException` (`tenant_provider_refused`) + `RunErrorClass.TenantProviderRefused` (17); `TenantProviderAccess` tek tipi atar, okunamaz anahtar da ret (`403`; bağımsız inceleme), derleme tipi ajan adıyla korur, model yolunun kimlik uyumsuzluğu da bu tiptir. Önce kırmızı: `SpeechDialogueTenantTests.A_refusal_whose_text_reads_like_a_transient_fault…`, `DefaultRunErrorClassifierTests.A_tenant_refusal_is_classified_by_its_type…`, üç `…key_the_tenant_may_not_read…` (`500` idi). Açık: F-490, F-491 |
| **F-444** · Görsel ve canlı ses reddi sağlayıcıyı "model provider" diye adlandırıyordu | 2026-10-14, kusur-giderme (F-442 ile) | `TenantProviderCallKind.Image`/`LiveVoice`. Önce kırmızı: `ImageTenantPolicyTests`, `LiveVoiceTenantPolicyTests` (ret metni). Sınıf taraması: `TenantProviderEndpoints` bağlama kaydı model sağlayıcısıdır, doğru |
| **F-426** · Anthropic'in `model_context_window_exceeded` ve `pause_turn` bitişleri `Stop` sayılıyordu; run yarım cevapla `Completed` kapanıyordu | 2026-10-14, kusur-giderme | K-985: `TraconFinishReasons.ContextWindowExceeded` (kesilme, mesaj ve olay `cause: context_window`); `pause_turn` blokları API'nin JSON'uyla geri gönderilip en çok 5 kez sürdürülür, sonra `AnthropicTurnPausedException` (`upstream_error`). Önce kırmızı: `AnthropicStopReasonTests` (5/5; yapılandırılmış yanıt bile `Completed`'dı). SDK'nın `MessageContentAggregator`'ı sunucu tool bloğunda atar (decompile); akışta bloklar olaylardan kurulur. Açık: F-489 |
| **F-430** · SQL Server upsert'i deadlock kurbanı olunca hatayı ham veriyordu — sınıf: dokuz deponun elle açtığı işlem | 2026-10-14, kusur-giderme | K-986: `SqlStoreContext.ExecuteInTransactionAsync` işlemin tamamını en çok 5 kez, rastgele beklemeyle yeniden koşar; upsert ve sekiz depo oradan geçer. Önce kırmızı: `UpsertDeadlockRetryTests` (deterministik döngü, `DEADLOCK_PRIORITY LOW`; 1205 aynı imza). Kapı: `SqlTransactionSiteTests`. Üç veritabanında sözleşme yeşil. Açık: F-488 |
| **F-356** · `kapi.py ic-dongu` tam koşuma genişleyince UI'sız derliyordu; commit'ten sonra değişiklik görmüyordu | 2026-10-14, kusur-giderme | E2E'yi seçen her yol (tam koşum, E2E projesi) arayüzlü derler; `--taban` aralığı verir, boş aralık uyarır. Önce kırmızı: `kapi_test` (4 case) |
| **F-393** · Başarısız webhook gönderiminde `store` hatası beklemeyi atlıyor, son denemede teslim `Pending` kalıyordu | 2026-10-14, kusur-giderme | Başarısız yolun yazımları `JobOutcomeWriteRetry`'den geçer, atmaz; deneme `JobRetryException` ile beklemesini korur. Önce kırmızı: `WebhookDeliveryFinalWriteTests` (2). Sınıf taraması: eval'in başarısız yazımı aynı sınıftı (F-429 satırı) |
| **F-484** · Tool ölçümünün alan uzunlukları `Report`'ta denetlenmiyordu | 2026-10-14, kusur-giderme | `TraconToolUsage.Report` `NonTokenCostAccumulator.Validate`'i koşar, sığmayan ölçüm `false` döner ve hiçbir yere iliştirilmez (K-974'ün kabul kuralı). Önce kırmızı: `NonTokenSpendTests.A_tool_measurement_its_columns_cannot_hold…`. Sınıf taraması: düğüm kalemi zaten doğrulanıyordu |
| **F-443** · `GET /api/voice/voices` sağlayıcı hatasını `500` veriyordu | 2026-10-14, kusur-giderme | `/speak`'in deseni: `502` + `.ProducesProblem(502)`; OpenAPI ve iki istemci yenilendi. Önce kırmızı: `VoiceEndpointTests.When_the_provider_fails_to_list_its_voices…`. Sınıf taraması: sağlayıcı çağıran öteki uçlar (`/speak`, görsel, bilgi tabanı) zaten eşliyor |

### ADAYLAR.md'den taşınan kanal satırları (2026-09-24)

Aday dosyasının § *Aday Olmayan Açık Kayıtlar* tablosu bu iki satırı
taşıyordu. Her ID'nin gövdesi bu dosyada veya
[`PLANA-DONUSEN-ADAYLAR.md`](PLANA-DONUSEN-ADAYLAR.md)'dedir.

| Kanal | ID'ler | Kural |
|---|---|---|
| **Kapatılan kusur kayıtları** | F-106 · F-130 · F-137 · F-138 · F-139 · F-170 · F-180 · F-181 · F-190 · F-197 · F-203 · F-204 · F-206 · F-211 · F-212 · F-214 · F-215 · F-219 · F-220 · F-222 | Yeniden görülürse **yeni** kusur kaydı açılır. **F-180**'in vakası kapandı, sınıfı açık — aday dosyasında tetik bekleyen olarak durur |
| **Arşivlendi / birleştirildi / rutin bakım** | F-48 · F-88 · F-89 · F-98 · F-144 · F-145 · F-146 · F-147 · F-148 · F-155 · F-158 · F-163 · F-221 | Plan değeri yok; rutin bakım olarak kaldı veya aktif adayla aynı tasarım işiydi. F-221'in kalan izi 2026-09-24'te sıfır ölçüldü (yukarıdaki tablo) |

---

## Açık kalem gövdeleri

> 2026-10-14: F-281, F-285, F-311, F-290, F-282 `ADAYLAR.md`'den taşındı (Faz 208 kapanışı; aday dosyası bütçesi). Kalemler **açıktır**; satırları kanal tablolarındadır. Metin aynen.

### F-281 · `Tracon.Google` görsel yolunda `MediaType` verilirse SDK `NotSupportedException` atar

**Aciliyet:** 🟢

**Sorun:** Canlı ölçüldü (2026-09-24, Google.GenAI 1.16.0, API anahtarı): `ImageConfig.OutputMimeType` her değer için *"outputMimeType parameter is only supported in Gemini Enterprise Agent Platform mode, not in Gemini Developer API mode"* ile reddedilir. `GoogleImageGenerator` (`src/Tracon.Google/Internal/GoogleImageGenerator.cs:77`) `ImageGenerationOptions.MediaType` verilince onu gönderir; Tracon'un iki sevk edilen yolu (`/api/images/generate`, `generate_image`) `MediaType` vermediği için bugün yalnız `IImageGenerator`'ı doğrudan çağıran tüketiciyi etkiler

**Ne yapılır:** Paket yalnız API anahtarı modunu desteklediği sürece `MediaType` istemde reddedilir ya da yok sayılır — hangisi olacağı küçük bir davranış kararıdır; karar sonrası düşen test + site cümlesi

### F-285 · Kırıcı kapının iki yanlış-mesaj yolu (Faz 187 denetimi)

**Aciliyet:** 🟢

**Sorun:** (a) `PKV007` gibi taban kaydı gerçekte `IsBaselineSuppression` taşımıyorsa (ölçülen `PKV006` taşımıyor) `read_breaking_changes` onu "strict uyumsuzluk" diye etiketler, "tanınmayan kayıt" değil (`scripts/breaking_changes.py`, kural sırası); test `test_pkv007_kirmizi` ölçülmemiş biçimi kullanır. (b) Düşen TFM iç içe listeyle yazılırsa (üst madde TFM, alt maddeler paket) `_list_items` girintili `-` satırını yeni madde sayar ve eşleşme kaçar. İkisi de güvenli yön (kırmızı), yalnız mesaj yanlış

**Ne yapılır:** Gerçek bir `PKV007` raporu ölç ve kuralı ona göre sırala; iç içe maddeyi üst maddenin span'leriyle birleştir. F-266'nın `### Removed` maddesi tek madde biçimindeyse acil değil

### F-311 · Kapalı MCP oturumuna giden çağrı "TaskCanceledException" diye raporlanır

**Aciliyet:** 🟢

**Sorun:** Ölçüldü (2026-10-11, F-305 repro): kapalı oturumdan giden `tools/call` için `ToolFailed` olay metni "Tool failed with TaskCanceledException." idi. Model "Error: Function failed." gördü, run `Completed` döndü; neden iptal değil kapalı bağlantıydı ve operatör onu iptal sanır. `{server}_read_resource` aynı durumda yanıltıcı "not among the resources declared" hatası verir. K-880'den sonra ana yol, F-310'dan sonra uzun ömürlü oturumlar bu duruma girmez; kalan yol derleme ile çağrı arasındaki yarış

**Ne yapılır:** MCP tool sarmalı (ya da `ExplainedFailureAIFunction`), çağıranın token'ı iptal değilken dispose edilmiş oturumdan gelen `OperationCanceledException`'ı "MCP connection to server '<ad>' is closed" diye açıklar; `read_resource` gövdesi kapalı bağlantıyı ayrıca söyler

### F-290 · `AuthorizationConfigurationKey`'ın `1.0.0` GA'da kaldırılması: veri taşıma + form geçişi

**Aciliyet:** 🟡

**Sorun:** Faz 190 alanı `[Obsolete]` yaptı ve `1.0.0`'da kalkacağını `CHANGELOG`, XML ve `production.md`'de duyurdu (K-869). Üç iş kaldı: (1) `authorization_configuration_key` dolu satırları `header_configuration_keys["Authorization"]`'a taşıyan migration (üç sağlayıcı; iki alan birlikte doluysa ne olacağı karar ister); (2) MCP formu (`mcp.tsx`) eski alanı yazıyor — yeni alanı yazmalı, yoksa kaldırma formu kırar; (3) sütunu ve public üyeyi kaldırma, `McpTransportFactory`/`McpHeaderBuilder` eski yol, `#pragma warning disable CS0618` blokları (kapanışta 11 adet: `grep -rn "pragma warning disable CS0618" src tests`)

**Ne yapılır:** GA tarihine bağlı; GA öncesi son preview fazı olarak planlanır. Form geçişi (2) GA'dan bağımsız erken yapılabilir

### F-282 · `troubleshooting` sayfası ağırlık tavanında; yeni semptom girdisi eklenemiyor

**Aciliyet:** 🟡

**İş:** Ölçüldü (2026-09-24, Faz 185): sayfa 58 994 B gzip, tavan 59 000 B (`docs-site/scripts/check-weight.mjs`) — 6 B boşluk. Faz 185'in iki kısa girdisi (NU1107/NU1608 ve "Tracon packages from more than one release") 59 850 B, tek başlığa indirilmiş hâli 59 335 B ölçüldü; ikisi de geri alındı, içerik `reference/versioning.md`'de. Kapının kendi yorumu "bir sonraki yükseltme yerine sayfayı böl" der. İş: sayfayı böl (ör. `## Build diagnostics and the agent map` bölümü ayrı sayfaya; semptom dizini, `sidebar.mjs`, TRC tanı kapısı birlikte), sonra Faz 185'in girdisini ekle. Tavanı yükseltmek ölçümle bile kapının kendi kuralına aykırıdır

## Kısaltılan kalem ayrıntıları

> `ADAYLAR.md` bütçesi için Faz 211 kapanışında satırlardan taşındı (2026-10-15); satırlar anlamını korur, ayrıntı aşağıdadır.

- **F-241** — Aynı kırpma deseni `TextValue` kırpmasında da var ([`EvaluatorRunJudge.cs`](../../src/Tracon.Core/Evaluation/EvaluatorRunJudge.cs)); hata yazma döngüsünde `retryable` sayılır. Yanlış kırpılan alan `varchar`/`nvarchar` yazımında kodlama hatası verir.
- **F-491** — Yedi yer `Type = exception.GetType().Name` yazar: `AgentRunJobHandler.cs:248`, `RunContinuationJobHandler.cs:296`, `QueuedRunPlaceholder.cs:73`, `WorkflowRecovery.cs:294`, `RunReconciliationService.cs:352`, `EvaluatorRunJudge.cs:220`, `ApprovalResumeJobHandler.cs:333` (2026-10-14 satır numaraları).
- **F-466** — Betik `kapanis`'te koşmaz; GA freeze turu (UR-003) onu iş listesi olarak okur. `TraconChildRunTimeoutException` ve `TraconLoopUnsatisfiedException` yalnız Core içinde atılır; site sayfası, `<example>` veya kalan bir tipin `<exception cref>`'i onları anmaz.

# Agent Derleme ve Derlenmiş Agent Önbelleği Tuzakları

> Bir `AgentDefinition`'in çalıştırılabilir bir `AIAgent`'a dönüşmesi, o
> dönüşümün KİMLİĞİ ve `CompiledAgentCache`.
>
> `RunRecording` zinciri, `scope`/span ve iptal AYRI dosyadadır:
> [`cekirdek-calistirma.md`](cekirdek-calistirma.md). Katalog dekoratör
> zinciri ve tool onayı da AYRI dosyadadır:
> [`tool-onay-ve-yetkilendirme.md`](tool-onay-ve-yetkilendirme.md).

- Compaction sarmalama sınırı (Faz 13) ve `IAgentSource` sürüm marker deseni (Faz 19): `docs/arsiv/FAZ-GECMISI.md` "Faz 13/19".
- **🚨 `AgentDefinitionCompiler.Compile` TAMAMEN senkron; kiraci kimlik bilgisi cozumlemesi async `store` gerektirir — ikisi celisince YENI paralel async yol acildi, mevcut sync yol DEGISTIRILMEDI** (Faz 65). Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `ConcurrentDictionary.GetOrAdd(key, valueFactory)` tahsis eder — HER cagrida, CACHE ISABETINDE bile** (2026-08-27, Faz 116, olculdu): C# argumani metottan ONCE degerlendirir, yani `_ => factory()` kapanisi anahtar zaten varken de HEAP'e yazilir. Cozum `TryGetValue`-once deseni; isabet basina 88 B → 24 B (BenchmarkDotNet). Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Bir sürüm numarası KİMLİK değildir; store numaralandırmayı yeniden
  başlatabiliyorsa önbellek anahtarı olamaz** (2026-09-18, `HATA-S4-004`).
  `CompiledAgentCache` anahtarı `(kiracı, ad, **sürüm**, bağımlılık, kültür)
  idi ve sınıfın kendi doküman yorumu bunu bir ilke diye anlatıyordu: "sürüm
  artar, önbellek DOĞAL olarak bayatlar, bu yüzden açık geçersiz kılma
  mantığı YOKTUR". İlke yalnız **UPDATE** için doğruydu. **DELETE + CREATE**
  sürümü `1`'e sıfırlar; silinmiş tanım ile yeni tanım aynı anahtara düşer ve
  silinmiş (çoğu zaman bozuk) tanım her `run`'ı cevaplamaya devam eder.
  Belirti operatörü **yanlış yöne yönlendirir**: `GET /api/agents/{name}` YENİ
  tanımı gösterir, veritabanında tek satır ve o satır doğrudur, yalnız
  çalıştırma yanlıştır. **Kural:** bir önbellek anahtarındaki her bileşen için
  "bu değer bir kaydın ömrü boyunca tekil mi, yoksa bir vekil mi?" sorusunu
  sor. Vekilse ölçüyü vekille değil **gömülen içeriğin kendisiyle** yap.
  K-811.
- **🚨 Aynı vekil hatası BAĞIMLILIK parmak izlerinde de vardı ve tek vaka
  aramak onu gizliyordu** (2026-09-18, aynı kapanışın sınıf taraması). Kusur
  kaydı tek yüzey biliyordu; ölçüm **üç** buldu: agent'ın kendisi, shared
  instructions bloğu (`"{blok}:{sürüm}"`) ve callable sub-agent
  (`(ad, sürüm)`). Üçü de silinip yeniden yaratılan bir adı eskisinden ayırt
  edemiyordu; son ikisinde bayat kalan şey **çağıranın** derlenmiş agent'ıydı
  — bloğun silinmiş metni ve alt agent'ın silinmiş açıklaması modele gitmeye
  devam ediyordu. **Kural:** bir parmak izi, gömülen şeyi ölçmelidir. Blok
  parmak izi metnin hash'i, callable parmak izi `CallableAgentInfo`'nun
  tamamıdır. K-812.
- **Bir içerik parmak izi ALAN ALAN değil, SERİLEŞTİRİLEREK hash'lenir**
  (2026-09-18, aynı kapanış). Elle yazılan bir alan listesi ucuzdur ama
  `AgentDefinition`'a sonradan eklenen alanı kaçırır ve kaçırmanın bedeli
  **sessizdir**: önbellek o alanın ESKİ değeriyle derlenmiş agent'ı sunar —
  Aile G'nin `TraconImageOptions.Timeout` vakasıyla aynı sınıf ("imza
  değiştirmek ile gövdeyi kullanmak iki ayrı adımdır"). `AgentDefinition`
  zaten `TraconCoreJsonContext`'te tanımlı olduğu için serileştirme AOT
  temizdir. Kapı: `DefinitionFingerprintTests
  .Every_field_of_the_record_reaches_the_fingerprint` — record'un kendi
  alanlarını gezer ve her birinin parmak izini değiştirdiğini zorlar.
- **🚨 Paralel async yolu açmak, o yolda kurulan HER model istemcisini
  taşımak demektir — yalnız ana istemciyi değil** (2026-10-11,
  kusur-giderme). Faz 65 ana modeli `CreateChatClientAsync`'e taşıdı;
  sıkıştırma özetleyicisi (Faz 13) ve Faz 151'in `aiJudge` yargıcı
  `BuildAgent`'ın İÇİNDE senkron `CreateChatClient` ile kurulmaya devam etti.
  Kiracının egress politikası özet ve yargıç modelini hiç görmedi, kiracının
  kendi anahtarı özetleyiciye ulaşmadı, önbellek atlama kararı yalnız
  `definition.Model`'i sordu. Ana modele bakan her test yeşildi — K-467'nin
  "tek nokta" ölçümü de yalnız ana istemciyi saymıştı. **Kural:** async yol
  agent'ın çağıracağı her istemciyi agent'tan ÖNCE kurar
  (`CreateSecondaryChatClientsAsync`); `BuildAgent` ve altı istemci KURMAZ,
  yalnız alır. Yeni bir ikincil model oraya ve `HoldsTenantCredentialAsync`'e
  girer. Kapı: `CompilePathTenantAwarenessTests` — registry'nin senkron üyesi
  atar ve `AgentDefinition`'a eklenen her `ModelBinding` bir proba bağlanmadan
  test geçmez. Davranış: `SecondaryModelTenantBoundaryTests`. K-874.
- **🚨 Derlenmiş agent, derlenirken okunan HER kiracı durumunu taşır; anahtar
  o durumu ölçmezse önbellek atlanmalıdır** (2026-10-11, F-304). Egress
  politikası yalnız istemci kurulurken denetlenir; anahtar politikayı
  taşımıyordu. İlk run agent'ı önbelleğe aldıktan sonra kaydedilen yasak
  politika ikinci run'ı durdurmadı: HTTP `200`, yasak sağlayıcı çağrıldı.
  Beşinci vaka: kiracı (K-380), kiracı anahtarı (K-471, K-874), sürüm vekili
  (K-811), shared/callable (K-812). **Kural:** derleyiciye veya registry'ye
  giren her bağımlılığın durumu ya anahtara girer, ya önbelleği atlatır, ya da
  yalnız çalışma anında okunur — üçünden biri, testle. Kapı:
  `CompiledAgentCacheKeyCoverageTests` (iki kurucunun her parametresi
  sınıflı; yeni parametre sınıflanmadan test geçmez). Altıncı vaka aşağıda
  (F-305). K-875.
- **🚨 Anahtar yalnız gömülen DEĞERİ değil, gömülen NESNENİN bağlı olduğu canlı
  durumu da ölçmelidir** (2026-10-11, F-305). Derleyici MCP tool nesnesini
  gömer; o nesne keşfedildiği `McpClient` üzerinden çağırır. Sunucu
  düzenlenince veya silinince katalog bağlantıyı kapatır, ama anahtar
  değişmiyordu: önbellekteki agent'ın her sonraki run'ı kapalı oturuma gitti
  (HTTP `200`, `Completed`, `ToolFailed`/`TaskCanceledException`, model
  "Error: Function failed." gördü). Onay yeniden açılınca agent onay
  istemedi; tool'u yalnız kapalı bağlantı durdurdu. Ölçü bağlantı ÖRNEĞİNİN
  kimliğidir (`McpConnection.Generation`), tanım parmak izi değil: sil +
  aynı tanımla yeniden ekle aynı parmak izini ama yeni bir bağlantı üretir
  (K-811 deseni). Anlık görüntüde olmayan ad `absent` yazar; refresh hatası
  da bunu üretir ve önbellekli yol önbelleksiz derlemeyle aynı reddi verir.
  **Test tuzağı:** `McpApprovalRuleSourceTests` her run'a ayrı agent veriyor
  ya da tanımı değiştiriyordu, bu yüzden önbellek isabetini hiç görmedi.
  Önbellek davranışını kanıtlayan test AYNI agent'ı AYNI tanımla iki kez
  koşmalıdır; `FakeModelProvider` kuyruğu provider'da yaşar, ikinci run'dan
  önce `CallsTool` yeniden kuyruğa konur. Kapı: `McpToolCacheTests` (beş case,
  `{server}_read_resource` dahil). K-880.
- **🚨 Uzaktan değişen bir durumu anahtara koymadan ÖNCE önbelleğe sınır koy**
  (2026-10-11, F-308). Uzak tool tanımı aynı bağlantıda değişebilir; anahtara
  girmeyince model eski açıklamayı görüyordu. Ama `CompiledAgentCache` hiç girdi
  atmıyordu: her refresh'te değişen bir açıklama agent başına günde 288 girdi
  demekti — K-880 bu yüzden hash'i dışarıda bırakmıştı. Çözüm sıralıdır: önce
  `(kiracı, ad, tanım, kültür)` başına tek yuva (yeni bağımlılık parmak izi
  değiştirir; tanım parmak izi yuvada kalır, yoksa A/B sürümleri her run'da
  birbirini atar), sonra bağlamaya `ProtocolTool` hash'i. **Kural:** operatör
  dışı bir kaynağın değiştirebildiği her anahtar bileşeni, eskisini **silen**
  bir yuva ister. K-882. Geçici `tools/list` hatası artık listeyi düşürmez
  (`StaleToolRetention`, K-883): aynı nesneler → aynı bağlama → isabet.
- **🚨 Anahtar doğru olsa da agent'ı UZUN SÜRE tutan çağıran önbelleği atlar**
  (2026-10-11, F-310). K-880/K-882 çözümde doğru agent'ı verir; ama canlı ses
  oturumu, WebSocket konuşması ve toplu iş agent'ı BİR KEZ çözüp oturum/iş boyunca
  tuttu — sunucu değişince sonraki her delegasyon kapalı bağlantıya gitti (HTTP
  testleri bunu göremez: her istek yeniden çözer). **Kural:** bir istekten uzun
  yaşayan tutucu adı tutar, agent'ı her tur/delegasyon/öğe başında çözer (önbellek
  isabeti ucuz); oturumun `AgentSession` nesnesi korunur. Tarandı: `ChildAgentInvoker`
  (workflow dahil) ve iş işleyicileri zaten çağrı başına çözüyor. Kapı:
  `LiveVoiceMcpTests` · `VoiceConversationMcpTests` · `AgentBatchJobHandlerTests`.

- **🚨 Parametreleri yalnız `CompileParameterizedAsync` bağlar ve kopyada `Parameters`'ı temizler** (2026-10-12, F-330, K-890): `BuildAgent`'ta şema görmek "bu derleme değer taşımadı" demektir. Orada varsayılanlar bağlanır ve `UnboundParameterGuard` HER böyle derlemeyi işaretler; yalnız varsayılansız zorunlu parametrede (`RequiresValues`) run'ı düşürür (derlemede ATMAZ — kayıt, doğrulama ve oturum okuması da derler). Giriş uçları `RequiresValues`'a bakar. Replay ve devam kayıtlı değeri okur, kayıt yoksa reddeder (F-294, K-898; işaret yalnız kayıtsız kaynağı yakalar). Tanım arama tek yerde: `AgentDefinitionLookup` (kod önce).
- **🚨 Parametreli çalıştırma TEK fabrikadan geçer: `ParameterizedAgentFactory`** (2026-10-12, F-294, K-897/K-898): parametreli derleme `CompiledAgentCache`'i ve katalog dekoratör zincirini atlar; derle + `AgentDecoratorPipeline.Apply` sırası (K-581) yalnız burada durur. Beş çağıran var: `/run` hattı, replay, devam, onay-devamı, workflow adımı (`ChildAgentInvoker`). Yeni bir çağıran kendi derlemesini yazarsa dekoratörler (bütçe, onay, kayıt) SESSİZCE düşer; kapı `A_bound_step_still_leaves_its_run_row…`. Değer akışı: `TraconRunOptions.ParameterValues` → `RunRecordingAgent` → `run_inputs.parameters` NİHAİ değerleri (varsayılanlar dahil) yazar. Workflow'da değer `AgentRunScope.ParameterValues`'ta (`AsyncLocal`) yaşar; adım yalnız KENDİ şemasındaki adları alır. Executor kimliği sarmalayıcıya bağlı kalır, bu yüzden checkpoint uyumu bozulmaz. Ölçüm: derleme + dekorasyon 0,064 ms/çağrı, önbellek isabeti 0,077 ms; run başına önbellek gerekmedi.
- **🚨 Descriptor, derlenen tanımın KENDİ sürümünü tanımlamalı** (2026-10-12, F-331, K-891): katalog sabit sürümü derleyip güncel sürümün descriptor'ını veriyordu; dekoratörler biçimi, modeli ve sürümü oradan okur. `IDescribingAgentSource` tek okumadan ikisini verir; descriptor üretimi yalnız `AgentDescriptorProjection`'dadır — elle kopyalama yeniden kayar.
- **🚨 Run kaydındaki `AgentVersion`, sabitlenebilir sürüm demek değildir** (2026-10-12, F-352, K-895): bildirimsel kod agent'ı `1` olarak kaydedilir; sürüm geçmişi tutmayan kaynakta sabit resolve fırlatır. Önceki run'ı sürdüren yol `SourceRunAgentResolver` kullanır: kaynak `IVersionedAgentSource` ise sabitler, değilse güncel tanımı çözer. Yerleşik job anahtarları (`AgentRun`, `ApprovalResume`) elle payload alır; payload'daki her güven iddiası (kol, agent adı) worker'da depoya karşı doğrulanır.
- **🚨 Katalog adı yalnız kod ve `store`'da arıyordu; çalışma anı ise MAF dosya kaynağından da sunuyordu — iki kaynak kümesi ayrıştı** (2026-10-14, F-481, K-981): disk skill'i HTTP'den adlandırılamadı, test MAF kaynağından başladığı için görmedi. Katalog artık `SkillScriptSupport.ReadDiskSkillsAsync` ile aynı MAF kaynağını okur (context'i okumaz, `null!`); parmak izine `GetContentAsync`'in SHA-256'sı girer (`SKILL.md` + script/kaynak adları). `FindWithOriginAsync` bilerek diske bakmaz: grant pini diskteki skill'i pinsiz verir. Kanıt HTTP → derleme → MAF → executor zinciridir.
- **🚨 Skill zinciri TEK yerde kurulur: `CreateSkillsProvider` → MAF `AgentSkillsProviderBuilder`** (2026-10-19, F-578): düz agent provider'ı `AIContextProviders`'a, harness agent da aynı provider'ı `AIContextProviders` başına alır. Harness'ta MAF'ın yerleşik skill provider'ı `DisableAgentSkillsProvider = true` ile **her zaman** kapalıdır; çalışma dizini taraması yapısal olarak imkânsızdır ve `load_skill` bir kez görünür. Builder sırası kaynaklar → Caching → Filtering → Dedup: önbellek süzülmemiş listeyi tutar. `CacheIsolationKeySelector` olmazsa iki kiracı tek listeyi paylaşır; kapı `SkillChainTests.One_compiled_agent_lists_each_tenants_own_skill` (satır çıkarılınca kırmızı, ölçüldü). Harness provider sırası: … BackgroundAgents → **Skills** → TextSearch → McpResources. Yeni bir skill kaynağı (`UseMcpSkills`, C-14) `CreateSkillsProvider`'a `UseSource` olarak girer; harness'a elle eklenmez.
- **🚨 Agent uzantısı `CompileChatAgent`/`CompileHarnessAgent` içinde, seçenek nesnesi tamamen kurulduktan sonra ve `AsAIAgent`/`AsHarnessAgent`'tan önce koşar** (2026-10-19, Faz 222, K-1029): kayıt `Core/Extensions/AgentExtensionRegistry.cs` (ad, sıra, çift ad, hata sarmalama), korunan üye tablosu `AgentOptionsGuard.cs` — TEK liste, her uzantıdan önce ve sonra okunur. Kanca derleme önbelleğinin İÇİNDE koşar (anahtar `ExtensionNames`'i tanım parmak iziyle taşır); parametreli run her seferinde koşar. 🚨 `POST /api/agents/validate` ve kayıt tanımı derler, yani adlandırdığı uzantıyı **koşar** (liste ucu koşmaz). Replay `NoTools`/`ReplayTools`'ta `ExtensionNames` boşaltılır (K-1033). Sarmalayıcılar `context.Use` ile en içte, ilk eklenen en içte. Koruma iç içe seçenek nesnelerini üye üye karşılaştırır (`BackgroundAgentsProviderOptions`, `LoopAgentOptions`, `FileAccessProviderOptions`): referans eşitliği yerinde değişikliği kaçırırdı. 🚨 MAF'ın döngü içi katmanları (mesaj enjeksiyonu, servis çağrısı başına geçmiş) Tracon'da tool döngüsünün ÜSTÜNE düşer: Tracon'un FIC'i model istemcisinin içindedir, MAF kendi FIC'ini eklemez. Mesaj çalışan tura girmez, geçmiş tur başına yazılır (gerçek host probu + düz MAF kontrolü). İki üye ve `ChatOptions.RawRepresentationFactory` korunur (K-1038); sohbet agent'ının seam'i kalktı.
- **🚨 SQL payload'ında `required` üyeli tipte kaynak üreteçli okuyucu `= []` başlatıcısını ATLAR** (2026-10-19, Faz 222, K-1031, ölçüldü): anahtarı olmayan eski satırda `IReadOnlyList<T>` alanı `null` döner. `ExtensionNames` bunu katalog listelemesini çökerterek gösterdi (`ArgumentNullException`, `CompositeAgentCatalog.Freeze`); `McpResourceUris` ve `Parameters` aynı kusuru taşıyordu. Payload'a sonradan eklenen her liste `?` taşır, `ToDefinition` `?? []` yazar; kapı `A_payload_written_before_a_list_member_existed_reads_back_with_an_empty_list`. Elle koşumda tek örnek bunu yakaladı: testlerin hepsi yeni satır yazıyordu.

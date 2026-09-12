# 02 — Çekirdek ve Katalog (`CORE`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../02-CEKIRDEK-VE-KATALOG.md`](../../02-CEKIRDEK-VE-KATALOG.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show efd5247:docs/manuel-test/kosumlar/2026-08-13/02-CEKIRDEK-VE-KATALOG.md
> ```

---

## Temiz geçen case'ler (29)

| Case | Durum | Başlık |
|---|---|---|
| MT-CORE-001 | ☑ | Geçerli tanım hatasız doğrulanır |
| MT-CORE-002 | ☑ | `unknown_tool`: kayıtlı olmayan tool adı |
| MT-CORE-003 | ☑ | `unknown_model`: kayıtlı olmayan sağlayıcı |
| MT-CORE-005 | ☑ | `unknown_skill`: bağlı olmayan skill adı |
| MT-CORE-007 | ☑ | Kendi kendini çağıran agent reddedilir |
| MT-CORE-008 | ☑ | Doğrulama hiçbir zaman istisna sızdırmaz |
| MT-CORE-020 | ☑ | Tanınmayan `reasoningEffort` değeri reddedilir |
| MT-CORE-021 | ☑ | `responseFormat` kombinasyonları |
| MT-CORE-030 | ☑ | Kod kaynaklı agent veritabanı tanımını yener |
| MT-CORE-031 | ☑ | Katalog ada göre sıralı döner |
| MT-CORE-032 | ☑ | Bulunmayan agent `null` döner, istisna atmaz |
| MT-CORE-033 | ☑ | Sürüm artışı derlenmiş agent önbelleğini geçersiz kılar |
| MT-CORE-034 | ☑ | Kod kaynaklı agent'ta sürümlü çözümleme reddedilir |
| MT-CORE-035 | ☑ | Tanım hiçbir zaman kimlik bilgisi taşımaz |
| MT-CORE-040 | ☑ | Tool listesi ad, açıklama, şema ve kaynak taşır |
| MT-CORE-042 | ☑ | Onay gerektiren tool sarmalanır ama şeması değişmez |
| MT-CORE-043 | ☑ | Tanım yalnız kayıtlı tool'a işaret edebilir |
| MT-CORE-045 | ☑ | Tool gerekmeyen istek tool çağırmaz |
| MT-CORE-051 | ☑ | Oturum silinir ve geçmiş gider |
| MT-CORE-052 | ☑ | Var olmayan oturumun silinmesi hata vermez |
| MT-CORE-053 | ☑ | İki oturum birbirini görmez |
| MT-CORE-060 | ☑ | Geçersiz `MaxPayloadLength` açılışı durdurur |
| MT-CORE-061 | ☑ | Script çalıştırma onaysız açılamaz |
| MT-CORE-062 | ☑ | Agent grafiği sınırlarının varsayılanı vardır |
| MT-CORE-063 | ☑ | Hassas veri varsayılan olarak kaydedilmez |
| MT-CORE-070 | ☑ | Bellek içi depolar veritabanı olmadan çalışır |
| MT-CORE-071 | ☑ | `FakeModelProvider` kuyruğu bir kez tüketilir |
| MT-CORE-073 | ☑ | Enum'lar JSON'da ad olarak yazılır |
| MT-CORE-074 | ☑ | Uygulama yeniden başlatıldığında kod agent'ları geri gelir |

## Ayrıntı taşıyan case'ler (13)

## MT-CORE-004 — `invalid_setting`: tanınmayan sağlayıcı ayarı

**Gerçek sonuç**
`provider:"anthropic"` ile koşuldu: `valid:false`, `code:invalid_setting`,
mesaj "su anahtarlar taninmiyor: anthropic.boyle.bir.ayar.yok. Desteklenen
anahtarlar: anthropic.promptCaching, anthropic.thinking.budgetTokens." —
düzeltilmiş beklentiyle **tam örtüşüyor**. Ürün kusuru yok; eski `echo`
senaryosu yanlış sağlayıcı seçmişti (echo ağa çıkmadığı için ayar okumasını
hiç tetiklemez).

---

**Doküman düzeltmesi (2026-08-15):** Girilecek veri koda göre düzeltildi
(`echo` → `anthropic`). Ürün kusuru yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-006 — `Inconclusive`: erişilemeyen MCP sunucusu `Valid`'i düşürmez

**Gerçek sonuç**
Doküman scriptinde İKİ ayrı hata bulundu: (1) endpoint yolu yanlış — `POST $APU/api/mcp/servers` 405 döner, doğrusu `PUT $APU/api/mcp-servers/{name}`; (2) gövde alanı yanlış — `"url"` değil `"endpoint"` olmalı (`McpServerRequest.Endpoint` `required`). Doğru endpoint+gövdeyle kayıt başarılı. Ardından doğrulama: doküman scriptinin adresi (`127.0.0.1:59999`, dinleyen yok) "connection refused" ile HIZLI döner; `McpToolCatalog.RefreshAsync` (`src/Tracon.Mcp/Internal/McpToolCatalog.cs:187`) sunucu bazlı istisnaları içeride yutuyor (log: "MCP sunucusu 'olu-mcp' baglanamadi"), bu yüzden `TryRefreshMcpAsync`'e istisna hiç ulaşmıyor, refresh "başarılı" sayılıyor → sonuç sade `unknown_tool` (`mcp_unreachable` DEĞİL). Yanıt vermeyen bir adresle (`192.0.2.1`, TEST-NET black-hole) TEKRARLANDI: 5.02 saniyede TAM beklenen sonuç alındı — `valid:true`, `inconclusive:true`, `mcp_unreachable`/`Warning`. SONUÇ: `mcp_unreachable` mekanizması doğru çalışıyor ama yalnız GERÇEK zaman aşımında (`OperationCanceledException`) tetikleniyor; aktif red ("connection refused") sessizce `unknown_tool`'a düşüyor — kullanıcı için iki "erişilemez" alt durumu farklı davranıyor. Hem doküman adresi yanlış hem de bu ince sözleşme boşluğu ayrı bir HATA adayı olarak not edildi.

---
Kapanış oturumu (Aile T, `docs/manuel-test/KAPANIS-PLANI.md`): kök neden
doğrulandığı gibi çıktı — `McpConnection.ConnectAsync`/`RefreshCatalogAsync`
HER türlü (zaman aşımı VEYA aktif red) bağlantı hatasını kendi içinde
yutuyordu, `McpToolCatalog.RefreshAsync` da bu iki alt durumu ayırt eden
hiçbir sinyal üretmiyordu — `AgentDefinitionValidator.TryRefreshMcpAsync`
yalnız DIŞARI FIRLAYAN bir istisnayı (yalnız zaman aşımının, belirli bir
zamanlama yarışında, ürettiği) görebiliyordu. `IMcpToolRefresher.RefreshAsync`
artık `int` değil yeni `McpRefreshOutcome` (`ToolCount` + `HadUnreachableServers`)
döner — `McpConnection`/`McpToolCatalog` (ikisi de `internal`, herkese açık
API kırılmadı) artık bağlantı/katalog-okuma başarısızlığının GERÇEK bir
ağlayıcı hatasından mı (yeniden denenince düzelebilir) yoksa kasıtlı bir
atlamadan mı (kalıcı yapılandırma sorunu — ad/adres geçersiz, OAuth geri
dönüş adresi eksik) kaynaklandığını ayırt edip yukarı taşıyor.
`TryRefreshMcpAsync` artık zaman aşımı istisnasına ek olarak bu yeni alanı
da kontrol ediyor — iki alt durum artık AYNI şekilde `mcp_unreachable`
üretiyor. Canlı `mt_fin` şemasına karşı doğrulandı: `127.0.0.1:59999`
(connection refused) artık **33 ms**'de `mcp_unreachable`/`Inconclusive`
veriyor (önceden sessizce `unknown_tool`); `192.0.2.1` (black-hole, gerçek
zaman aşımı) hâlâ **~5.02 sn**'de aynı sonucu veriyor — regresyon yok.

**Değişen dosyalar:** `IMcpToolRefresher.cs` (+`McpRefreshOutcome`),
`McpConnection.cs` (`ConnectAsync`/`RefreshCatalogAsync` artık `Unreachable`
bayrağı da döner), `McpToolCatalog.cs` (`RefreshAsync`/`EnsureConnectionAsync`/
`McpToolRefresher` bayrağı yukarı taşır), `McpDiscoveryService.cs`,
`GovernanceEndpoints.cs` (çağrı yerleri yeni dönüş tipine uyarlandı),
`AgentDefinitionValidator.cs` (`TryRefreshMcpAsync` yeni bayrağı okur).

**Regresyon testleri:** `tests/Tracon.Mcp.UnitTests/McpToolCatalogReachabilityTests.cs`
(gerçek "connection refused" ile `McpToolCatalog.RefreshAsync` seviyesinde,
kayıtlı sunucu yokken negatif kontrol) ·
`tests/Tracon.Core.UnitTests/Compilation/AgentDefinitionValidatorTests.cs`
`Aktif_red_ile_erisilemeyen_MCP_sunucusu_da_inconclusive_uretir`.

**Case:** `MT-CORE-006` ✅. Doküman düzeltmesi de yapıldı: yukarıdaki
"Girilecek veri" scripti artık doğru uç (`PUT /api/mcp-servers/{name}`) ve
doğru alan adını (`endpoint`) kullanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-009 — Boş ve aşırı uzun alanlar

**Gerçek sonuç**
1) `name:""` → 400, "'name' alani zorunludur" — DOĞRU. 2) `name` alanı JSON'da HİÇ YOK → **HTTP 500** (beklenen: 400 veya `valid=false`; "hiçbir adımda 500 dönmez" ilkesi ihlal edildi). Kök neden (sunucu logundan doğrulandı): `AgentDefinitionRequest.Name` bir C# `required` üye; JSON'da alan hiç yoksa `System.Text.Json` "missing required properties" ile `JsonException` atıyor → `BadHttpRequestException` → uygulamanın genel exception handler'ı bunu 400 yerine 500 ProblemDetails'e çeviriyor. 3) 50.000 karakterlik `instructions` → `valid:true`, çökme/zaman aşımı yok — DOĞRU. GENEL BULGU: bu SİSTEMİK bir örüntü — bkz. MT-CORE-006 (missing `endpoint`) ve MT-CORE-022 (geçersiz enum) notları; gövdesinde `required` alan veya enum tipi olan HERHANGİ bir endpoint'e eksik/geçersiz veri gönderildiğinde muhtemelen 500 dönüyor, 400 değil. Ayrı bir HATA kaydı gerekir; kapsamı bu dosyayı aşıyor, 07-HTTP-YONETIM-API.md'de de doğrulanmalı.

---

**Yeniden koşum (Aile G, 2026-08-14).** `/api/agents/validate` artık `AgentEndpoints.BindAgentDefinitionRequestAsync` ile govdeyi elle okuyor (önceki dalgada kapanmış, HATA-S1-007) — bu case zaten kapalıydı, yalnız yeniden doğrulandı: 2) `name` alanı JSON'da HİÇ YOK → **HTTP 400**, `{"title":"Gecersiz istek govdesi","detail":"JSON deserialization for type 'Tracon.AgentDefinitionRequest' was missing required properties including: 'name'."}`. Sistemik bulgunun geri kalanı (Aile G, `KAPANIS-PLANI.md` §6) `Tracon.AspNetCore`'da genel bir `RequestBodyBinding.ReadAsync<T>` yardımcı metoduyla kapatıldı — kütüphanenin tüm govde-baglayan uçları artık aynı elle-okuma desenini kullanıyor, ortamdan (Development/Production) bağımsız.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Derleyici

Derleyici, doğrulamanın aksine **istisna atar**. Bu case'ler derleme yolunu
tanım kaydederek ve agent'ı çözdürerek tetikler.

---

## MT-CORE-022 — Sıkıştırma ayarları tetikleyicisiz olamaz

**Gerçek sonuç**
c1 (`strategy:"Summarize"`, TriggerTokens yok) → **HTTP 500**. c2 (`strategy:"BoyleBirSeyYok"`) → **HTTP 500**. c3 (ContextWindow, MaxContextWindowTokens yok) → DOĞRU: `compilation_error`, beklenen mesaj. Kök neden (log doğrulandı): c1'de "Summarize" GEÇERLİ bir `CompactionStrategyKind` değeri DEĞİL — doğru ad `Summarization` (bu bir DOKÜMAN TİPOSU); c2'de zaten kasıtlı geçersiz değer. İkisinde de `System.Text.Json`'ın enum dönüştürücüsü tanımadığı string'i `JsonException` ile reddediyor → aynı sistemik 500 örüntüsü (bkz. MT-CORE-009). ÖNEMLİ: doküman'ın c2 için beklediği "mesaj bilinmeyen strateji adını AYNEN taşır" davranışı HTTP API üzerinden HİÇBİR ZAMAN gerçekleşemez — validator/compiler mantığına hiç ulaşılmıyor, JSON ayrıştırma katmanında daha erken patlıyor. "Hiçbir istekte 500 dönmez" ilkesi bu koşumda en az üç ayrı case'de (006 kayıt denemesi, 009-2, 022 c1/c2) ihlal edildi — sistemik, tek endpoint'e özgü değil.

---

**Yeniden koşum (Aile G, 2026-08-14).** Aile G'nin `RequestBodyBinding.ReadAsync<T>` düzeltmesi sonrası: c1 → **HTTP 400** (`"The JSON value could not be converted to Tracon.CompactionStrategyKind..."`). c2 → **HTTP 400**, aynı mesaj şekli. Beklenen sonuç düzeltmesi (doküman koda göre): c1'in "Summarize" değeri zaten DOKÜMAN TİPOSU (doğrusu `Summarization`) — bu adımda gerçek bir hata YOKTUR, düzeltilmiş adla test edilmeli. c2'nin "mesaj bilinmeyen strateji adını AYNEN taşır" beklentisi HTTP API üzerinden hiçbir zaman gerçekleşemez (JSON ayrıştırma katmanı validator'a hiç ulaşmadan patlar); doğru beklenti `HTTP 400` + JSON dönüştürme hatası mesajıdır — bu artık karşılanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-023 — Skill kataloğu kayıtlı değilken skill isteyen tanım

**Gerçek sonuç**
İstisna atıldı (`TraconCompilationException`, `AgentName="skill-isteyen"`
doğru), mesaj metni düzeltilmiş beklentiyle **tam örtüşüyor**: `"'skill-isteyen'
agent'i 'olmayan-skill' skill'ine isaret ediyor ancak skill bulunamadi."`
Davranış tutarlı (eksik skill'e işaret eden tanım her koşulda reddediliyor);
`AgentDefinitionCompiler.cs:307`/`:1174`'teki ikinci hata dalı pratikte hiç
görülmüyor (ölü kod, ayrı bir temizlik adayı — bu case'in kapsamı dışı).

---

**Doküman düzeltmesi (2026-08-15):** Beklenti koda göre düzeltildi. Ürün
kusuru yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-024 — Derleme hatası hangi agent'ta olduğunu söyler

**Gerçek sonuç**
Doküman'ın kendi scripti `provider:"echo"` kullanıyor — bu paket İÇİNDE HİÇ
YOK (echo yalnız örnek uygulamaya özel, bkz. `MT-CORE-023`). Bare consumer
projesinde basit bir yerel `IModelProvider` uygulamasıyla telafi edilip
koşuldu. Mekanizma KANITLANDI çalışıyor: istisna doğru atıldı, `tool-eksik`
agent adı mesajda var, eksik tool adı (`hayali_tool`) mesajda var, kayıtlı
tool `"Var"` olarak listelendi (düzeltilmiş beklentiyle örtüşüyor),
`builder.AddTracon().AddTool(...)` yönlendirmesi mesajda var. Ürün
kusuru yok — API tasarımı belgelenmiş şekilde çalışıyor.

---

**Doküman düzeltmesi (2026-08-15):** Beklenti koda göre düzeltildi. Ürün
kusuru yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Katalog

---

## MT-CORE-041 — Aynı adda iki tool açılışta hata verir

**Gerçek sonuç**
Mesaj "beklenen istisna:" ile başlıyor, "'ayni_ad' adinda birden cok tool kaydedilmis..." tam eşleşti. "🚨 istisna ATILMADI" satırı görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-044 — Tool gerçekten çağrılır ve sonucu kayda geçer

**Gerçek sonuç**
Gerçek OpenAI çağrısı (gpt-5.4-mini) yapıldı. Yanıt metni tam olarak "ORD-1001 siparişiniz kargoya verilmiş. Tahmini teslimat: 2 gün." — `ORD-1001` dizgisini içeriyor. `get_order_status` tool'u tam bir kez çağrıldı (DB'den doğrulandı). `runs.status` tamamlanmış. İlk denemede `input_cost`/`output_cost` NULL çıktı ama bu ürün kusuru değil: örnek uygulamanın kendi `appsettings.json`'ı `openai`/`gpt-5.4-mini` için hiç fiyat tanımlamıyor (K-032: "fiyat uydurulmaz, deger verilmezse maliyet NULL kalir" — kasıtlı/belgelenmiş, yalnız örnek uygulamanın eksik yapılandırması). Kendi izole sürecime fiyat eklenip tekrar koşuldu: `input_cost=0.0000695`, `output_cost=0.0000560` — pozitif. Mekanizma tam doğrulandı. Doküman notu: doküman örnek uygulamada `Pricing` tanımlı olduğunu varsayıyor; şu an değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-050 — Oturum yoksa oluşturulur, varsa yüklenir

**Gerçek sonuç**
Oturum oluştu, ikinci çalıştırmada geçmiş GERÇEKTEN yüklendi/kullanıldı (persist edilen mesaj listesi API'den doğrulandı: 4 mesaj, kronolojik sıra: 2 kullanıcı + 2 asistan). Yalnız "yanıt 'Faruk' içerir" iddiası doğrulanamadı — `echo` sağlayıcısının kendi tasarımı gereği (`EchoChatClient.BuildReply` yalnız SON kullanıcı mesajını yankılar, tam geçmişi değil); bu bir ürün kusuru değil, echo'nun bilinçli sınırlaması (MT-CORE-004/006 ile aynı sınıf).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-054 — Aynı oturuma eşzamanlı iki çalıştırma

**Gerçek sonuç**
**GERÇEK VE KRİTİK ürün kusuru — doğrulandı, kanıtlandı.** Aynı YENİ oturum kimliğine ("manuel-yaris") iki eşzamanlı İLK istek gönderildi. İkisi de başarıyla tamamlandı (SSE `event: done`), hiçbiri hata dönmedi. Beklenen: 4 mesaj (sessiz kayıp kabul edilemez). Gerçekleşen: oturumda yalnız **2 mesaj**. KÖK NEDEN (veritabanından birebir kanıtlandı): İKİ AYRI `conversation` satırı oluştu (aynı saniyede, mikrosaniye farkla: `...6880-7137` ve `...6880-700e`) — "bu oturum için conversation var mı" kontrolü ile "yoksa oluştur" arasında klasik check-then-create yarışı var. Oturumun `state->stateBag` alanındaki `conversationId` işaretçisi SON YAZAN istek tarafından ezildi (last-write-wins); kaybeden isteğin conversation'ı ("Ikinci istek." + yanıtı — DOĞRULANDI: veri fiziksel olarak kayıp değil, `seq 0-1` orada) oturumun `state`'inden artık erişilemez durumda — `GET /api/sessions/manuel-yaris` bu mesajları ASLA göstermez, sessizce orphan kaldı. Etki: aynı oturuma HENÜZ hiç mesaj gönderilmemişken eşzamanlı iki istek gelirse (çift tıkla gönder, ağ retry'i, iki sekme) ikinci konuşmanın tamamı SESSİZCE kaybolur. Ayrı bir HATA-NNN kaydı olarak raporlanmalı (Kritik).

---
**2026-08-14 yeniden koşum (düzeltme sonrası — HATA-004, `AgentSessionManager` + `ISessionStore.TryCreateAsync`):** Aynı senaryo canlı Postgres'e (`mt_fin`, temiz şema) karşı yeniden koşuldu. Birinci istek `event: done` ile normal tamamlandı. İkinci istek TAM modelini çalıştırdı (gerçek maliyet — bu kabul edilen taviz) ama kaydetme anında açık bir `event: error` çerçevesi aldı: `{"type":"TraconSessionConflictException","message":"'manuel-yaris' oturumunu ayni anda baska bir istek de acti ve bizden once kaydetti. Kisa bir sure sonra yeniden deneyin."}` — **hiçbiri 500 dönmedi**. DB doğrulaması: `sessions.state->stateBag->Tracon.ChatHistory->conversationId` kazanan `conversation_id`'yi taşıyor; o `conversation_id` altında tam 2 mesaj var (kaybeden istek fiziksel olarak yazdığı 2 mesajla birlikte ayrı, artık hiçbir sessiondan referanslanmayan bir `conversation_id`'de kalıyor — orphan, ama SESSİZCE DEĞİL: istemci açıkça bilgilendirildi). Yeniden deneme (`Ikinci istek (yeniden deneme).`) normal yoldan geçti ve kazanan konuşmaya doğru şekilde eklendi (toplam 4 mesaj). Beklenen sonucun 3. maddesi ("Bir çakışma denetimi varsa isteklerden biri açık bir çakışma hatası döner; bu da kabul edilebilir") tam olarak gerçekleşti — sessiz kayıp yok. Kök neden ve düzeltme karar defterine kapanışta yazılacak (KAPANIS-PLANI.md §11 sırası).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Ayarlar ve açılış doğrulaması

Geçersiz bir ayar **açılışta** hata vermelidir. Çalışma anında ortaya çıkan bir
ayar hatası üretimde bulunur.

---

## MT-CORE-064 — Yapılandırmadan gelen negatif fiyat reddedilir

**Gerçek sonuç**
Mesaj "beklenen red:" ile başlıyor: "TraconPricingOptions: 'echo:echo-1' icin fiyat negatif olamaz." — hangi sağlayıcı/model için sorun olduğu açık. "🚨 negatif fiyat KABUL EDILDI" satırı görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-065 — `Pricing` altındaki rezerve anahtarlar sağlayıcı sayılmaz

**Gerçek sonuç**
Doküman'ın scripti aynen koşulduğunda `Saglayici sayisi: 0` döndü (beklenen 1). İKİ AYRI, ÜST ÜSTE binen doküman hatası bulundu: (1) `services.AddTracon()` ARGÜMANSIZ çağrılıp AYRICA `services.Configure<TraconOptions>(config.GetSection(...))` çağrılmış — bu standart/reflection-tabanlı binder'ı kullanır. `TraconOptions` ise AOT gerekçesiyle KENDİ elle yazılmış `Bind()` metoduyla bağlanıyor (doğru kullanım: `AddTracon(configSection)`, K-021). Düzeltilip tekrar koşuldu, YİNE 0 döndü. (2) BAĞIMSIZ ikinci kök neden: manuel `BindPricing` (`TraconServiceCollectionExtensions.cs:841-842`) her model için `ReadDecimal(modelSection, "Input")`/`"Output"` KISA anahtarlarını okuyor; doküman'ın JSON'ı ise `"InputCostPerMillionTokens"`/`"OutputCostPerMillionTokens"` (C# özellik adlı UZUN anahtarlar) kullanıyor — hiç eşleşmiyor, `ReadDecimal` sessizce `null` dönüyor, provider HİÇ eklenmiyor, ne hata ne log. İki kök neden de düzeltilip (doğru API + kısa anahtar adları) tekrar koşuldu: TAM beklenen sonuç alındı (Currency=USD, sağlayıcı sayısı=1, yalnız "echo", Ses sağlayıcıları="elevenlabs"). AYRICA ürün-düzeyi bulgu (Orta): yanlış anahtar adıyla yazılan bir `Pricing` girdisi TAMAMEN SESSİZCE düşüyor (istisna/log/uyarı yok) — bu, projenin "bilinmeyen ayar sessizce yok sayılmaz" ilkesiyle (K-034) çelişiyor. Ayrı bir HATA adayı (Orta) + doküman düzeltmesi önerilir.

---
**2026-08-14 yeniden koşum (Aile P).** Kalan Orta bulgu ("sessizce düşme") düzeltildi. `BindPricing`/`BindVoicePricing` artık `Input`/`Output` (veya `PerMillionCharacters`/`PerMinute`) hiç eşleşmese bile modeli `continue` ile atlamıyor — ikisi de boş bir `ModelPriceOverride`/`VoicePriceOverride` kaydı olarak `Providers`/`Voice`'a giriyor. `TraconOptionsValidator.ValidatePricing` bu "ikisi de boş" durumunu artık açıkça reddediyor. Canlı doğrulama (gerçek PostgreSQL'e karşı, `mt_fin_p` şeması): `Tracon__Pricing__echo__echo-1__InputCostPerMillionTokens=0.25` (yanlış/uzun anahtar) ile uygulama **başlamayı reddetti** — `Microsoft.Extensions.Options.OptionsValidationException: TraconPricingOptions: 'echo:echo-1' ne 'Input' ne 'Output' tasiyor — anahtar adini kontrol edin.` Doğru kısa anahtarla (`Input`/`Output`) aynı uygulama sorunsuz başladı. Regresyon testleri: `tests/Tracon.Core.UnitTests/Configuration/TraconPricingBindingTests.cs` (Bind() çıktısını dogrulayıcıdan izole reflection ile inceleyen `BindOnly` testleri + tam DI üzerinden acilis reddini dogrulayan testler).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Bellek içi izlek ve kimlik

---

## MT-CORE-072 — Kimlikler zaman sıralı UUIDv7'dir

**Gerçek sonuç**
`sirali: True`, damga farkı 0,0sn (<1s), v4 (`Guid.NewGuid()`) reddedildi: "Kimlik bir UUID surum 7 degeri degil." "🚨 v4 KABUL EDILDI" satırı görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

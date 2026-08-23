# Faz 86 — Talimatın Girdi Yüzeyi

> **Durum:** ✅ Tamamlandı (2026-08-22)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-34** — Dalga 14 Küme P (2026-08-21'de yeniden yargılandı; belge kanalını devraldı)
> **Önkoşul:** [Faz 72](72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) — `InstructionsByCulture` ve `InstructionCultureResolver` oradan gelir; parametre yerleştirme **onun çıktısına** uygulanır · [Faz 19](19-SURUM-KARSILASTIRMA-VE-AB.md) (sürümleme) — kalemin değeri sürüm geçmişidir · [Faz 18](18-DEGERLENDIRME.md) · [Faz 45](45-URETIMDEN-EVAL-KUMESI.md) (eval vakası şeması)
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.Sql.Shared` (üç SQL paketine linked-source, K-176), `AgentPrism.UI`
> **Yeni paket:** Yok — yerleştirme saf bir fonksiyondur, şablon motoru **alınmaz** · **Migration:** 🚨 **Gerekli — ama yalnız eval tarafı için.** Agent tanımı `jsonb`'dir (ölçüldü: `agent_definitions.definition jsonb`), parametre şeması oraya migration'sız girer. `eval_cases` **sütun tabanlıdır** (ölçüldü) → parametre seti için üç migration seti. Numaralar uygulama anında alınır (K-178)
> **Public API:** Büyüyor — parametre tipi, `AgentDefinition` alanı, `AgentRunRequest` alanı, belge tipi, `EvalCase` alanı. `PublicAPI.Shipped.txt` toplamı **16 satır** (yalnız başlıklar; ölçüldü 2026-08-21) → Faz 7'den önce eklemek **bedava**, sonra bir sürüm kararıdır
> **Tüketici yüzeyi:** `docs-site/` → `concepts/agents.md` (parametreli tanım), `concepts/governance.md` (belge kanalının **ne olmadığı**), `concepts/evaluation.md` (parametreli vaka), `reference/configuration.md`, `capabilities.md`
> · sevk edilen: yeni tiplerin XML dokümanı ve `<example>`'ları, `src/AgentPrism.Abstractions/README.md`. `api/` ve `http-api/` **üretilir**
> **Manuel test alanı:** [`docs/manuel-test/02-CEKIRDEK-VE-KATALOG.md`](../../manuel-test/02-CEKIRDEK-VE-KATALOG.md) (parametre) · [`docs/manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md`](../../manuel-test/22-GUARDRAIL-VE-YAPISAL-CIKTI.md) (belge kanalı)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/86-TALIMATIN-GIRDI-YUZEYI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'in agent tanımı bugün **parametresizdir**. `Instructions` düz metindir; `InstructionsByCulture` yalnız **dile** göre varyant verir. Koşu isteği de bir parametre taşımaz. Sonuç: parametreli bir agent AgentPrism tanımına **taşınamaz**. Bu bir ergonomi eksiği değildir.

## Bitiş Ölçütleri (DoD)

- [x] `POST /api/agents/{name}/run` eksik zorunlu parametrede `400` döner ve eksik parametrenin **adını** söyler — `samples/AgentPrism.Api`'ye karşı ölçüldü: `{"detail":"Missing required parameter 'musteri'.","missingParameters":["musteri"]}`
- [x] `/estimate` **aynı** hatayı döner (tek doğrulayıcı — `AgentParameterValidator.ValidateValues`, iki çağıran: `/run` ve `/estimate`). 🚨 **Plan düzeltmesi**: `/validate` **değil** — bkz. "Plandan Sapmalar" #1; `/validate` tam bir `AgentDefinitionRequest` gövdesi alır ve şema kendi tutarlılığını kontrol eder (`ValidateSchema`), bir agent'ın var olan şemasına karşı DEĞER kontrolü yapmaz. Ölçüldü: `POST /api/agents/parametreli/validate` (plan taslağının varsaydığı yol) `404` döner — böyle bir uç yok
- [x] JSON taşıyan bir talimat, tırnak içeren bir değerle yerleştirildiğinde **geçerli JSON** üretir — `InstructionParameterBinderTests`
- [x] Yerleştirme **tek geçişlidir**: değerin içindeki `{{ad}}` yeniden yerleştirilmez — `InstructionParameterBinderTests`
- [x] Her kültür varyantı aynı parametre kümesiyle doğrulanır; fazlası derleme hatasıdır — `AgentParameterValidatorTests.Every_culture_variant_is_checked_against_the_same_schema`
- [x] Belge kanalı kayıtta talimattan **ayrı** görünür — canlı ölçüldü (aşağıdaki doğrulama komutu çıktısına bak): `RunStarted.text` gerçek soruyu ("selam") taşır, belge içeriği hiçbir olayda görünmez, `DocumentAttached` yalnız ad+boyut+karma taşır
- [x] Belge içeriğindeki sınırlayıcı dizisi kaçırılır — canlı ölçüldü: sahte `-----END AGENTPRISM DOCUMENT-----` içeren bir belge gönderildi, model talimatı bozulmadı, kayıt yalnız gerçek belge adını/boyutunu gösterdi (bkz. denetim 🔴 bulgusu — Name kaçışı da eklendi)
- [x] Parametreli agent bir eval setinde koşar — `EvalJobHandlerTests.Parameterized_agent_case_binds_its_values_before_running`
- [x] Üç migration seti (PostgreSQL · SQL Server · SQLite) yazıldı ve `EvalStoreContract` üçünde geçti — `dotnet test` üç entegrasyon projesinde de yeşil (1134/572/590 test)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `dotnet build`/`test`/`pack`/`format`, tam çözüm, arayüz DAHİL, tamamı yeşil (2026-08-22)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. "Doğrulama komutları" altındaki gerçek çıktı
- [x] `secret` taraması boş döndü — bu fazın dokunduğu dosyalarda (repodaki önceden var olan yerel test `Password=` literalleri bu fazdan bağımsızdır, Faz 79/80/81 emsaliyle aynı kapsam)
- [x] Manuel kabul case'leri `docs/manuel-test/02-*` ve `22-*` içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — iki tur denetim, üç 🔴 bulgu (bkz. "Denetim Bulguları"), tamamı düzeltildi ve gates yeniden koşuldu
- [x] `docs-site/` güncellendi; `npm run check` temiz. Belge kanalı sayfası **güvenlik garantisi vaat etmiyor**
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı **ölçüldü ve yazıldı** — 175.0 KB gzip (bütçe: 250 KB gzip)

### Doğrulama komutları — gerçekleşen çıktı (2026-08-22, `samples/AgentPrism.Api`, gerçek OpenAI çağrısı)

🚨 **Plan düzeltmesi**: aşağıdaki komutlar planın taslağından farklıdır — `/validate` `/run`/`/estimate` ile
**aynı** yol biçimini almaz (bkz. "Plandan Sapmalar" #1). Gerçek kanıt:

```bash
# Eksik parametre — /run ve /estimate AYNI hatayi verir
curl -s -X POST http://localhost:5081/agentprism/api/agents/parametreli/run \
  -H 'content-type: application/json' -d '{"message":"selam"}'
# → 400 {"title":"Invalid run parameters","detail":"Missing required parameter 'musteri'.",
#        "missingParameters":["musteri"],"unknownParameters":[],"tooLongParameters":[]}

curl -s -X POST http://localhost:5081/agentprism/api/agents/parametreli/estimate \
  -H 'content-type: application/json' -d '{"message":"selam"}'
# → aynı gövde, aynı 400

# /validate FARKLI bir sozlesmedir: tam bir tanim govdesi alir, sema tutarliligini kontrol eder
curl -s -X POST http://localhost:5081/agentprism/api/agents/validate \
  -H 'content-type: application/json' --data-binary @param_agent.json
# → 200 {"valid":true,"inconclusive":false,"messages":[]}

# Basarili parametreli run + belge kanali
curl -s -X POST http://localhost:5081/agentprism/api/agents/parametreli/run \
  -H 'content-type: application/json' -H 'Idempotency-Key: ...' \
  -d '{"message":"selam","parameters":{"musteri":"Acme"},
       "documents":[{"name":"policy.txt","content":"Refunds within 30 days."}]}'
# → 200 {"runId":"01a02ade-...","response":{"messages":[{"role":"assistant",
#        "contents":[{"$type":"text","text":"Hello Acme, how can I help?"}]}], ...}}

# Kayit: sorgu ile belge AYRI mi? (RunStarted.text asla belge icerigini tasimaz)
curl -s http://localhost:5081/agentprism/api/runs/01a02ade-.../events
# → id:0 event:run.started      data:{"type":"RunStarted","text":"selam", ...}
#   id:1 event:unknown          data:{"type":"DocumentAttached","text":"policy.txt",
#                                      "payload":"{\"sizeBytes\":23,\"sha256\":\"7294ff...\"}"}
#   id:2 event:message.delta    data:{"type":"MessageDelta","text":"Hello Acme, how can I help?"}
#   id:3 event:message.completed data:{"type":"MessageCompleted", ...}
#   id:4 event:run.completed    data:{"type":"RunCompleted", ...}
# Belge ICERIGI ("Refunds within 30 days.") hicbir olayda gorunmuyor - yalnizca ad/boyut/karma.

# Parametre degeri boyut siniri (varsayilan 4096 bayt UTF-8)
curl -s -X POST http://localhost:5081/agentprism/api/agents/parametreli/run \
  -H 'content-type: application/json' -d '{"message":"hi","parameters":{"musteri":"'"$(python3 -c "print('a'*5000)")"'"}}'
# → 400 {"detail":"Parameter 'musteri' exceeds the maximum value length.","tooLongParameters":["musteri"]}

# Bundle payi (frontend derlemesi, arayuz DAHIL build)
ls -la src/AgentPrism.UI/wwwroot/assets/*.js | awk '{s+=$5} END {print s/1024" KB (raw)"}'
# → 175.0 KB gzip (bütçe: 250 KB gzip)
```

**İlk denemede** (fix'ten önce) `parameters` alanı `PUT /api/agents/{name}` ile **sessizce boşta
kaldı** — bu iki 🔴 denetim bulgusundan biriydi, bkz. "Denetim Bulguları".

---

## Plandan Sapmalar

1. **`/validate` `/run`/`/estimate` ile AYNI sözleşmeyi paylaşmaz.** Plan §86.3'ün
   tablosu ve DoD taslağı üçünü aynı yol biçiminde ("eksik parametrede aynı hata")
   tarif ediyordu. Gerçek: `POST /api/agents/validate` **tam bir tanım gövdesi**
   alır ve `AgentDefinitionCompiler`'ın çalıştırdığı `AgentParameterValidator.ValidateSchema`
   ile şemanın **kendi tutarlılığını** kontrol eder (talimatta tanımsız `{{ad}}`,
   yinelenen ad, geçersiz tanımlayıcı) — bir agent'ın var olan şemasına karşı bir
   koşu isteğinin DEĞERLERİNİ kontrol etmez. `/run` ve `/estimate` ise
   `AgentParameterGate` üzerinden **aynı** `ValidateValues` metodunu çağırır ve
   gerçekten aynı hatayı üretir (canlı ölçüldü). `AgentParameterValidator`'ın kendi
   XML dokümanı bu ayrımı zaten doğru tarif ediyordu; sapan yalnızca plandaki DoD
   satırı ve doğrulama komutuydu — ikisi de düzeltildi.
2. **Parametre değeri boyut sınırı eklendi** (Açık Soru 2, öneri A benimsendi):
   `AgentPrismOptions.MaxParameterValueLength` (varsayılan 4096 bayt UTF-8,
   `MaxInstructionsLength` emsaliyle aynı desen). `AgentParameterValidator.ValidateValues`
   yeni bir `maxValueLength` parametresi alır (varsayılan `null` = sınırsız, geriye
   dönük uyumluluk); `AgentParameterGate` (HTTP) ve `EvalJobHandler` (eval) aynı
   sınırı uygular. Yeni hata kodu: `ValueTooLongCode = "value_too_long"`.
3. **Paylaşılan blok yalnız `Origin.Database` bir satır olabilir** (Açık Soru 4,
   öneri A'nın doğal sonucu). `SharedInstructionsName` bir ada göre
   `IAgentDefinitionStore.GetAsync` ile çözülür; kod tanımlı bir agent hiçbir zaman
   bu depoya yazılmaz, dolayısıyla kodda tanımlı bir agent'ı blok olarak
   referans vermek mümkün değildir — yalnız arayüzden/API'den oluşturulan bir
   tanım blok olabilir. Bu bir kısıtlama değil, seçilen tasarımın (var olan
   `agent_definitions` tablosunu paylaşılan blok için de kullanmak) doğrudan
   sonucudur; plan bunu açıkça yazmıyordu.
4. **`CompiledAgentCache` atlaması, `IAgentCatalog`'un TAMAMINI atlar — yalnız
   önbelleği değil.** Plan §86.5 ve ilk taslak yorumlar bunu BYOK (kiracıya özel
   sağlayıcı kimlik bilgisi) ile aynı desen sanıyordu. Ölçüldü: BYOK yalnız
   önbelleği `CodeAgentSource`/`DefinitionStoreAgentSource`'un İÇİNDE atlar —
   dış çağıran hâlâ `CompositeAgentCatalog.ResolveAsync`'i çağırır ve bu metot
   HER ZAMAN `IAgentDecorator` zincirini (run kaydı, telemetri, tool onayı)
   uygular. Parametreli koşu ise `AgentDefinitionCompiler.CompileParameterizedAsync`'i
   **doğrudan** çağırır — katalog metoduna hiç girmez. Bu, denetimde bulunan 🔴
   bulgulardan biriydi (bkz. "Denetim Bulguları"); düzeltme yeni bir
   `AgentDecoratorPipeline.Apply` yardımcı metoduyla, her iki çağıran (HTTP
   `/run`, `EvalJobHandler`) tarafından elle uygulanır.
5. **SQL-tabanlı `AgentDefinitionPayload` jsonb izdüşümü yeni alanları
   taşımıyordu** — ikinci 🔴 bulgu, yalnız gerçek bir `samples/AgentPrism.Api`
   koşusuyla (PostgreSQL'e karşı) ortaya çıktı: `AgentDefinition.Parameters` ve
   `SharedInstructionsName` `AgentContracts.cs`'e, `AgentDefinition.cs`'e ve
   bellek-içi depoya doğru eklenmişti ama SQL-tabanlı depoların jsonb sütununa
   yazılan ARA tip (`AgentDefinitionPayload`, `AgentPrism.Sql.Shared`) unutulmuştu
   — kod derlendi, testten geçti (bellek-içi depo doğrudan `AgentDefinition`'ı
   sakladığı için sorunu hiç görmedi), ve PostgreSQL/SQL Server/SQLite'ta bu iki
   alan **sessizce kayboluyordu**. Düzeltme: `AgentDefinitionPayload`'a iki alan
   eklendi; `AgentDefinitionStoreContract.SaveAsync_round_trips_all_definition_fields`
   artık üçünü de doğruluyor (üç SQL sağlayıcısında + bellek-içi depoda çalışır).

## Bu Fazda Verilen Kararlar

| Karar | Tarih | Gerekçe | Yeniden açılma koşulu |
|---|---|---|---|
| **K-580 — SQL-tabanlı `jsonb`/`json` yükü taşıyan her ARA tip (`AgentDefinitionPayload` gibi), kaynak tipe (`AgentDefinition`) yeni alan eklendiğinde ELLE senkronize edilmelidir; derleyici bunu zorlamaz (Faz 86, ölçülen kusur)** | 2026-08-22 | `AgentDefinition.Parameters`/`SharedInstructionsName` eklendi, `AgentContracts.cs` ve bellek-içi depo doğru güncellendi, ama `AgentPrism.Sql.Shared/Internal/AgentDefinitionPayload.cs` — jsonb'ye yazılan gerçek tip — unutuldu. Derleme geçti (payload tipi bağımsız bir record'dur, `AgentDefinition`'a bağlı değildir), 1233 test geçti (hepsi bellek-içi depoyu kullanıyordu), ve kusur yalnız `samples/AgentPrism.Api`'nin GERÇEK PostgreSQL'ine karşı elle koşulan bir `run` ile ortaya çıktı: `PUT /api/agents/{name}` sonrası `parameters: []` dönüyordu. Bu AGENTS.md'nin "imza değiştirmek ile gövdeyi kullanmak iki ayrı adımdır" kuralının ÜÇÜNCÜ somut örneğidir (Faz 20'nin `Cost = cost` ve Faz 48'in yapısal konum kusurundan sonra) — ama bu sefer İKİNCİ bir tip (payload projeksiyonu) üzerinden, ilk ikisinden farklı bir yüzeyde. `AgentDefinitionStoreContract.SaveAsync_round_trips_all_definition_fields` artık `Parameters`/`SharedInstructionsName`'i de doğruluyor. | Yeni bir alan `AgentDefinition`'a eklenirken bu kontrol listesine `AgentDefinitionPayload.cs`'i de ekleyecek bir kalıcı hatırlatma (`docs/hafiza/`) yoksa tekrar yaşanır — bkz. Adım 7 notu |
| **K-581 — `CompiledAgentCache`'i atlayan bir çağıran, `IAgentCatalog.ResolveAsync`'in UYGULADIĞI `IAgentDecorator` zincirini de ELLE uygulamak zorundadır; yeni `AgentDecoratorPipeline.Apply` bunu tek bir yerde toplar (Faz 86, ölçülen kusur)** | 2026-08-22 | `AgentDefinitionCompiler.CompileParameterizedAsync` doğrudan çağrıldığında (parametreli `/run` ve eval vakası) `CompositeAgentCatalog.ResolveAsync`'in normalde uyguladığı dekoratör zinciri (`RunRecordingAgentDecorator`, `OpenTelemetryAgentDecorator`, `ToolApprovalAgentDecorator`) HİÇ ÇALIŞMIYORDU — kod derlendi, 632 fonksiyonel test geçti (hepsi bellek-içi/test host'ta koşuyordu ve kayıt davranışını doğrudan test etmiyordu), ve kusur yalnız `samples/AgentPrism.Api`'ye karşı gerçek bir `run` sonrası `GET /api/runs?agentName=parametreli`'nin BOŞ dönmesiyle ortaya çıktı. Kök neden: ilk tasarım (BYOK'un tenant-kimlik-bilgisi baypası) yalnız `CompiledAgentCache`'i atlar ve katalog metodunun İÇİNDE kalır (dekorasyon hâlâ uygulanır); bu fazın parametreli-koşu baypası ise katalog metoduna HİÇ GİRMEZ. `AgentDecoratorPipeline.Apply(agent, descriptor, decorators)` yeni bir genel yardımcı (`AgentPrism.Core`), iki çağıran (HTTP `/run`, `EvalJobHandler`) tarafından `CompileParameterizedAsync` sonrası elle çağrılır. | Gelecekte üçüncü bir "katalog dışı compile" çağıranı eklenirse (bugün yok) o da bu yardımcıyı çağırmak zorundadır — derleyici bunu zorlamaz, yalnız kod incelemesi/bu not yakalar |
| **K-582 — `AgentPrismOptions.MaxParameterValueLength` tek, üst-düzey bir sınırdır; parametre başına ayrı bir sınır YOKTUR (Faz 86, Açık Soru 2 kapatıldı, öneri A)** | 2026-08-22 | `MaxInstructionsLength`in (`SkillEndpoints.cs`) izlediği aynı desen: tek bir `int` (varsayılan 4096 bayt UTF-8), `AgentPrismOptionsValidator`'da pozitiflik kontrolüyle. Parametre başına bir sınır şemayı (`AgentParameter`) şişirirdi ve bu kalemin gerçek ihtiyacı — bir isteğin toplam yükünü sınırlamak, tek bir alanın anlamsal boyutunu değil. `AgentParameterValidator.ValidateValues`'un yeni `maxValueLength` parametresi varsayılan `null`dır (sınırsız) — bu, parametreyi geçirmeyen HERHANGİ bir çağıranın (örn. birim testleri) davranışını DEĞİŞTİRMEZ. | — |

## Denetim Bulguları

İki bağımsız denetim turu koştu (`faz-denetim`, taze bağlamlı ayrı agent).

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | `DocumentChannelMessageBuilder.Build` yalnız `document.Content`'i kaçırıyordu, `document.Name`'i DEĞİL — belge adına gömülü bir sahte sınırlayıcı gerçek içerikten ÖNCE sınırı sahteleyebilirdi | 🔴 | **Düzeltildi** — `Escape()` artık Name'e de uygulanıyor; regresyon testi: `DocumentChannelMessageBuilderTests.A_literal_delimiter_inside_the_document_name_cannot_forge_the_boundary` |
| 2 | `AgentPrismOptions.MaxParameterValueLength` hiç yoktu; planın kendi "Beş soru" tablosu bir sınırın "gerekir" dediği hâlde ilk uygulamada eklenmemişti | 🟡 | **Düzeltildi** — K-582, bkz. "Bu Fazda Verilen Kararlar" |
| 3 | Paylaşılan bloklar için kiracı-yalıtımı bir sözleşme testiyle sabitlenmemişti | 🟡 | **Düzeltildi** — `SharedInstructionsTests.Shared_block_resolves_to_the_calling_tenants_own_content_not_another_tenants` (aynı store örneği, iki kiracı arasında `MutableTenantContext` ile geçiş) |
| 4 | DoD'nin "/validate ve /estimate aynı hatayı döner" satırı yanlıştı — `/validate` `ValidateValues`'ı hiç çağırmaz | 🟡 | **Düzeltildi** — DoD ve doğrulama komutları düzeltildi, plandan sapma #1 olarak yazıldı |
| 5 | Plan dışı public API büyümesi (`MaxParameterValueLength`, `ValueTooLongCode`, `AgentDecoratorPipeline`) fazın "Planlanan Public API" bölümünde yoktu | 🟡 | **Gerekçelendi** — "Gerçekleşen Public API" bölümünde taslaktan farkı açıkça işaretlendi |
| 6 | Paylaşılan blok kapsamının `Origin.Database`'e özgü olduğu (kod tanımlı bir agent blok OLAMAZ) plana yazılmamıştı | 🟡 | **Gerekçelendi** — "Plandan Sapmalar" #3 |
| 7 (ilk turdan sonra, uygulayan oturumun kendi canlı koşusunda bulundu) | `AgentDefinitionPayload` (SQL jsonb izdüşümü) yeni alanları taşımıyordu — SQL-tabanlı her deploymentta `Parameters`/`SharedInstructionsName` sessizce kayboluyordu | 🔴 | **Düzeltildi** — K-580, `AgentDefinitionStoreContract` genişletildi |
| 8 (aynı canlı koşuda bulundu) | Parametreli koşu `IAgentCatalog`'u atlıyordu ve dolayısıyla HİÇ kayıt/telemetri/tool-onayı almıyordu | 🔴 | **Düzeltildi** — K-581, `AgentDecoratorPipeline` eklendi |

**7 ve 8 denetimin İKİNCİ turunda değil, uygulayan oturumun kendi kapanış
doğrulama koşusunda bulundu** — `samples/AgentPrism.Api`'ye karşı gerçek bir
`run` yapmanın tam olarak neden zorunlu olduğunun kanıtıdır (`faz-tamamlama`
Adım 2): 1233+632 test hiçbirini yakalamadı, ikisi de yalnız gerçek bir SQL
deposuna karşı gerçek bir HTTP isteğiyle ortaya çıktı.

## `docs-site` senkron denetimi — gerekçeli geçişler

`python3 scripts/dokuman-bakim.py --site-denetle --taban 293112f` beş kuraldan
üçünü otomatik karşıladı (`ui.md` — playground'daki parametre formu için yeni
paragraf eklendi; `capabilities.md`/`llms-full.txt` — `build-agent-map.mjs` ile
yeniden üretildi; `concepts/`). İki kural elle gerekçelendirilir:

- **`http-api.md` değişmedi.** Bu sayfa **üretilir** ve yalnız toplam
  `operation`/`path` sayısını özetler ("161 operation / 124 path"). Bu faz
  **hiçbir yeni uç eklemedi** ("Yeni uç yoktur" — Planlanan Public API); üç
  var olan ucun (`/run`, `/estimate`, `validate`) gövdesi büyüdü. Sayılar
  değişmediği için özet metin de değişmiyor — bu bir eksiklik değil, doğru
  davranış. Asıl değişiklik `http-api/schema-agentparameter.md`,
  `schema-agentparameterkind.md`, `schema-agentdefinitionrequest.md` gibi
  **şema sayfalarındadır** — `npm run build` ile üretildi, doğrulandı.
- **`getting-started/persistence.md` değişmedi.** `PostgresQueries.cs`'teki
  değişiklik `eval_cases` tablosuna bir sütun (`parameters`) ekliyor — bu
  sayfanın konusu (sağlayıcı seçimi, tablo yalıtımı, migration mekaniği,
  saklama) hiçbiri değişmedi, yalnız var olan bir tablo bir sütun kazandı.
  Kullanıcıya dönük karşılığı zaten `concepts/evaluation.md`'de ("a case's
  own `parameters` field...") — doğru sayfa, sayfa değişikliği tekrarlamaz.

## Sonraki Faza Devir Notu

**Sıradaki faz:** [`87-KESILEN-ISIN-DEVAMI.md`](87-KESILEN-ISIN-DEVAMI.md) (F-141,
dayanıklı çalıştırma/devam) — **konu bakımından bağımsızdır**, bu fazın hiçbir
sözleşmesine dayanmaz; doğrulandı (`grep -in "86-TALIMAT\|SharedInstructions"` boş
döndü). O dokümanın kendi "Bu Faza Başlarken" listesi olduğu gibi geçerlidir.

**Devralınan sözleşmeler** (bu fazın parametreli-koşu altyapısına dokunacak
gelecek bir faz için):

- `AgentDefinitionCompiler.CompileParameterizedAsync(definition, culture, values, ct)`
  — `CompiledAgentCache`'i **ve** `IAgentCatalog`'u atlar. Sonucu HER ZAMAN
  `AgentDecoratorPipeline.Apply(agent, descriptor, decorators)` ile dekore et —
  atlarsan run kaydı/telemetri/tool-onayı sessizce kaybolur (K-581).
- `AgentDefinitionPayload` (`AgentPrism.Sql.Shared/Internal/`), `AgentDefinition`'ın
  jsonb'ye yazılan İKİNCİ bir izdüşümüdür. `AgentDefinition`'a yeni bir alan
  eklerken bu dosyayı da güncelle — derleyici zorlamaz (K-580).
  `AgentDefinitionStoreContract.SaveAsync_round_trips_all_definition_fields`
  yeni alanı da kapsıyor mu diye kontrol et.
- `AgentParameterValidator.ValidateValues(schema, values, maxValueLength)` —
  `AgentParameterGate` (HTTP) ve `EvalJobHandler` (eval) aynı metodu çağırır.
  `/validate` bunu ÇAĞIRMAZ (yalnız `ValidateSchema`); ikisini karıştırma.

**Bilinen tuzaklar (🚨) bu fazda keşfedildi:**

- Bir SQL-tabanlı jsonb izdüşüm tipi (`AgentDefinitionPayload` gibi) kaynak
  tipin (`AgentDefinition`) alan listesini OTOMATİK takip etmez — bellek-içi
  depo bunu maskeler (doğrudan kaynak tipi saklar), yalnız gerçek bir SQL
  koşusu ortaya çıkarır.
- `CompiledAgentCache`'i atlayan HER YENİ "katalog dışı compile" yolu,
  `AgentDecoratorPipeline.Apply`'ı da elle çağırmak zorundadır — aksi hâlde run
  sessizce kayıtsız/telemetrisiz çalışır ve hiçbir test bunu yakalamaz (bellek-içi
  test host'ları dekorasyonu doğrudan doğrulamıyor).
- `faz-tamamlama` Adım 2'nin ("örnek uygulamayı GERÇEKTEN çalıştır") gerekliliği
  bu fazda tam ikinci kez kanıtlandı: 1233+632 yeşil testin YAKALAYAMADIĞI iki 🔴
  kusur, yalnız `samples/AgentPrism.Api`'nin gerçek PostgreSQL'ine karşı elle
  koşulan bir `run` ile bulundu.

**Yarım kalan iş yok** — 🔴 ve 🟡 bulguların tamamı bu fazda kapandı (bkz.
"Denetim Bulguları"). `docs/hafiza/postgresql.md`/`sql-saglayicilari.md`'ye
K-580'in tuzağını ekleyecek bir not `faz-tamamlama` Adım 7'de yazılmalı — bu
faz onu **yaptı** (bkz. commit'teki `docs/hafiza/*` değişikliği).

# Faz 249 — Kod Grafında Agent Sürümünü Run Parametresi Seçer

> **Durum:** ✅ Tamamlandı (2026-10-26)
> **Kademe:** hafif — migration yok · public API'den satır silinmez (yalnız `WorkflowAgentStepOptions`'a bir özellik eklenir; `Unshipped` büyümesi) · hedef framework değişmez · güvenlik/kiracı sınırı yok · geri dönüşü pahalı karar yok
> **Plan onayı:** Bakımcı, 2026-10-26 (sohbette alınan kararlar: F-409 bu kalemin ikinci kanıtıdır — sabitlenen sürüm çağırdığını da sabitlemelidir; sürüm faz zinciri üç faza bölündü: 248 numara kimliği, 249 bu faz, 250 alt agent sabiti)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-545**
> **Genellik:** Workflow'u kodda kuran (`AddWorkflow` + `GetWorkflowAgentStep`) ve agent'ı veritabanında sürümleyen her tüketici. İş tüketici kodunda kalamaz: fabrika yalnız `IServiceProvider` alır ve grafı run parametreleri bağlanmadan **önce** kurar (`WorkflowRunner.cs:1082` derleme, `:1090` `BindParametersAsync`); tüketici bir run'ın hangi sürümle koşacağını seçemez. İkinci kanıt ilk ilkedir (F-409, bakımcı kararı): sabitlenebilen bir sürüm modeli run başına seçimi de taşımalıdır; saklanan workflow bunu `definitionVersion` ve K-989 ile zaten verir
> **Karşı görüş:** "Tüketici her sürüm için ayrı bir workflow kaydedebilir ya da `AgentVersion`'a sabit yazabilir." Cevap: fabrika her run'da koşar ama değerleri göremez; sabit sayı her yayında kod değişikliği ister ve bir template yayınından sonraki revizyon, yayını yapan run'ı yeni sürümle koşturur (video tüketicisi raporu, 2026-10-15). Run parametresi bugün zaten vardır (K-1010) ve devam/cevapta kayıtlı değerler yeniden kullanılır
> **Önkoşul:** Faz 248 — sabit, silmede yeniden kullanılmayan bir numaraya dayanmalıdır
> **Paketler:** `Tracon.Workflows` (`WorkflowAgentStepOptions`, `WorkflowAgentBinding`, `WorkflowAgentCache`), `Tracon.Core` (`ChildAgentInvoker`)
> **Tüketici yüzeyi:** site `concepts/workflows.md` (kod grafında agent adımı), `concepts/agents.md:452-460` (sabitsiz adım güncel sürümü çözer), `guides/review-and-revise-workflow.md` (varsa adım seçenekleri) · sevk edilen: `WorkflowAgentStepOptions` XML + `<example>`, `src/Tracon.Workflows/PublicAPI.Unshipped.txt`, `CHANGELOG.md` `[Unreleased]` → *Added*

## Önceki Fazdan Devir

Faz 248 kapandı (satırı `FAZ-DEFTERI.md`'de). Bu fazın dayandığı kural: bir agent adı için sürüm numarası
**hiç yeniden verilmez**. Silinen ve aynı adla yeniden oluşturulan agent eski dönemin numaralarını geri almaz;
`IAgentDefinitionStore.GetVersionAsync(ad, eskiNumara)` `null` döner. Run parametresinden gelen sürüm bu yüzden
"yok" olabilir ve kapalı düşmelidir (workflow sabitiyle aynı: çalıştırmadan önce, adıyla, hiçbir model çağrısı
yapılmadan). Emsal ileti: workflow düğümü `pins agent 'x' to version N, but the agent has no version N`; onay devamı
`Version N of agent 'x' was not found.` K-1100.

## Amaç

Kod grafındaki bir agent adımı bugün ya güncel sürümü ya da kodda sabit yazılmış
bir sürümü koşar (`WorkflowAgentStepOptions.cs:42`). Sürüm grafı kurarken
verilir (`WorkflowAgentBinding.cs:211-212`) ve önbellek anahtarına girer
(`WorkflowAgentCache.cs:126-134`). Run parametreleri kod kaydında vardır
(`WorkflowRegistrationOptions.cs:90`, K-1010) ve adım onları çalışma anında
okuyabilir (`ChildAgentInvoker.cs:662`), ama sürümü seçmek için kullanılamaz.

Faz bitince `WorkflowAgentStepOptions.AgentVersionParameter` (ad uygulamada
kesinleşir) kayıtta bildirilmiş bir run parametresinin adını taşır. Adım her
çağrıda o parametrenin değerini okur:

- değer yok ya da boş → güncel sürüm (bugünkü davranış);
- `1` ya da üstü bir tam sayı → o sürüm; kayıt `ChildRunReport.AgentVersion`'a o sayıyı yazar;
- başka her değer, bildirilmemiş parametre adı ya da tutulamayan sürüm → adım
  model çağrısından önce **adıyla** düşer (K-1007: sabit adımda denetlenir, grafta değil).

`AgentVersion` ile `AgentVersionParameter` birlikte verilemez; ikisi birden
grafı kurarken `ArgumentException` ile düşer (bugünkü `< 1` denetimi gibi,
`WorkflowAgentBinding.cs:202-209`). Önbellek anahtarı parametrenin **adını**
taşır, değerini değil — anahtar uzayı sınırlı kalır; sürüm çağrı anında çözülür.
Devam ve cevap run'ı başlangıçtaki kayıtlı değerleri yeniden kullanır
(`TraconWorkflowContextExtensions.cs:31-33`), yani sabit devamda da tutulur.

## Bitiş Ölçütleri (DoD)

- [x] `AgentVersionParameter = "agentVersion"` ve `POST /tracon/api/workflows/{name}/run` gövdesinde `parameters: { agentVersion: "2" }` → çocuk run satırı `agentVersion = 2`; parametre yoksa güncel sürüm — `CodeWorkflowAgentStepTests.A_run_parameter_picks_the_version_the_step_runs_and_its_row_records_it`, `One_graph_serves_every_version_…`, `A_run_that_leaves_the_version_parameter_empty_or_out_…`; gerçek `run`: aşağıdaki çıktı
- [x] Geçersiz değer (`"abc"`, `"0"`, `"-1"`, `"1.5"`, `"+2"`, `" 2"`, taşan sayı), bildirilmemiş ad (hiç bildirilmemiş ve başka ad bildirilmiş) ve tutulamayan sürüm (olmayan sürüm, geçmişi olmayan kod agent'ı) adımı adıyla, model çağrısından önce düşürür — aynı sınıfın `Theory` case'leri; her biri `provider.Requests` boş ve çocuk satırı yok der
- [x] `AgentVersion` + `AgentVersionParameter` birlikte grafı kurarken düşer — `WorkflowAgentStepVersionParameterTests.A_fixed_version_and_a_version_parameter_together_are_refused_when_the_graph_is_built` (düz `ArgumentException`); boş ad da düşer
- [x] Devam ve cevap run'ı aynı sürümle sürer — `CodeWorkflowParameterTests.The_run_that_answers_runs_the_agent_version_the_run_started_with` (cevaptan önce agent v3'e düzenlenir; adım v1'i koşar)
- [x] `WorkflowAgentStepOptions` sınıf açıklaması saklanan düğümle eşlik iddiasını düzeltir (Açık Soru 1, seçenek A)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı aşağıda (*Örnek Uygulama Koşumu*)
- [x] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban 7af62952` (29 146 test, 39 modül; denetim düzeltmelerinden sonra koştu)

## Örnek Uygulama Koşumu

2026-10-26, `samples/Tracon.Api` (Release, `Development`, dört boş ortam değişkeni; günlükte `Npgsql` satırı: **0**).
**Geçici yama, geri alındı:** örnekte `episode-writer` kodda tanımlıdır ve kod agent'ı sürüm geçmişi tutmaz; sürüm seçimini
göstermek için yalnız bu koşumda yazar veritabanına taşındı (API ile oluşturuldu, üç sürüm), kayıt `agentVersion` (Number)
bildirdi ve adım `AgentVersionParameter = "agentVersion"` aldı. Koşum sonrası `git checkout` ile geri alındı; oluşan
`agentprism-manuel.db` silindi.

| Çağrı (`POST /tracon/api/workflows/review-and-revise/run`) | Kök run | Çocuk satırı (`GET /runs/{id}/tree`) |
|---|---|---|
| `parameters: {productionId: p-1, agentVersion: "2"}` | `AwaitingInput` | `episode-writer`, `agentVersion=2`, `Completed` |
| `agentVersion: "1"` | `AwaitingInput` | `agentVersion=1` |
| `agentVersion` yok (güncel = 3) | `AwaitingInput` | `agentVersion=3` |
| `agentVersion: "abc"` | `Failed`: `Workflow step 'episode-writer' cannot run: run parameter 'agentVersion' is not a version number. A version is a whole number from 1; leave the parameter empty to run the agent's current version.` | çocuk satırı yok |
| `agentVersion: "9"` | `Failed`: `Version 9 of agent 'episode-writer' was not found.` | çocuk satırı yok |
| agent v4'e düzenlendi; run 1 `data: {approved: false, note: …}` ile cevaplandı | cevap run'ı `AwaitingInput` | revizyon adımı **`agentVersion=2`** (güncel 4) |
| `Tracon__Observability__SuccessSampleRatio=1` ile yeniden başlatma (veritabanı kaldı), `agentVersion: "3"` | `AwaitingInput` | `agentVersion=3` |

## Testler

| Davranış | Seviye ([test-seviyeleri](../.agents/ortak/test-seviyeleri.md)) | Test |
|---|---|---|
| Parametre sürümü seçer; çocuk satırı sürümü taşır | Fonksiyonel (HTTP + depo) | `CodeWorkflowAgentStepTests` (yeni case'ler; `ChildRowsAsync(...).AgentVersion` emsali `:86`) |
| Parametre yok → güncel sürüm | Fonksiyonel | aynı sınıf |
| Geçersiz değer / bildirilmemiş ad / tutulamayan sürüm adımı adıyla düşürür | Fonksiyonel | aynı sınıf (`WorkflowAgentVersionPinTests.cs:209` emsali) |
| İki seçenek birlikte grafı düşürür | Birim | `Tracon.Workflows.UnitTests` (bağlama testi) |
| Önbellek anahtarı değeri taşımaz; iki run farklı sürümle aynı sarmalayıcıyı paylaşır | Birim | `WorkflowAgentCache` testi (`CodeWorkflowRevisionTests` emsali) |
| Devam/cevap aynı sürümü koşar | Fonksiyonel | `CodeWorkflowParameterTests` (`:119` emsali) |

Beş soru: **iptal** — değişmez · **eşzamanlılık** — sürüm çağrı anında kapsamdan
okunur, paylaşılan durum yok · **boş/aşırı girdi** — boş = güncel; taşan sayı
geçersizdir · **başka kiracı** — çözümleme kiracının kataloğundan (bugünkü yol) ·
**alt sistem hatası** — katalog okuması düşerse adım düşer (bugünkü yol). Kabul
case'i yok: davranışın tamamı fonksiyonel testte tekrarlanır.

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Sınıf açıklaması "A stored workflow carries the same values in a `WorkflowNodeReference`" der (`WorkflowAgentStepOptions.cs:8`); yeni seçeneğin saklanan düğüm eşi var mı | A: açıklama "yalnız kod grafı" diye düzelir; saklanan düğüm eşi ayrı aday olur · B: `WorkflowNodeReference.AgentVersionParameter` bu faza girer | A — saklanan workflow `definitionVersion` ile zaten run başına sabitlenir; eşlik ancak saklanan düğüm run parametresi taşıyorsa anlamlıdır, uygulamada ölçülür |
| 2 | Özelliğin adı | A: `AgentVersionParameter` · B: `AgentVersionFromParameter` | A |

## Plandan Sapmalar

> Çalışırken dolar; kapanışta commit mesajına taşınır.

- **Bildirilmemiş ad `ParameterValues`'tan ayırt edilemez → scope'a `ParameterNames` eklendi.** Plan "bildirilmemiş parametre
  adı adımı düşürür" diyordu ama son değer kümesi, değeri ve varsayılanı olmayan *bildirilmiş* parametreyi içermez
  (`ParameterValueSet.Resolve`); yalnız ona bakan adım yazım hatasını "verilmedi" sanıp güncel sürümü koşardı. `AgentRunScope`'a
  internal `ParameterNames` (bildirilen adlar) eklendi, `BindParametersAsync` yazar, `ChildAgentInvoker.VersionFor` okur. Public
  yüzey değişmedi. Test: `A_name_the_workflow_does_not_declare_…(declaresOtherName: true)` (başka ad bildirilmiş) bu sınıfı kapatır.
- **Açık Soru 1 → A, Açık Soru 2 → A** (planın önerisi; "direkt başla" talimatıyla sorulmadı). Saklanan düğümün run parametresi eşi
  yoktur ve bu faza girmedi.
- **Örnek uygulama değişmedi.** Yazarı kodda tanımlı (`episode-writer`) ve kod agent'ı sürüm geçmişi tutmaz; kalıcı bir
  `agentVersion` parametresi örnekte yalnız hata yolunu gösterirdi. Canlı koşum geçici yamayla yapıldı (yukarıda).
- **İki seçenek birlikte → düz `ArgumentException`** (`ArgumentOutOfRangeException` değil: değer aralığı değil, birlikte verilemezlik).
  `AgentVersionParameter` boş ya da boşluksa da `ArgumentException`.
- **Yalnız boşluk içeren değer "boş" sayılmaz, geçersizdir** (`" 2"`, `"  "`): yalnız `""` güncel sürümdür. Ayrıştırma
  `NumberStyles.None` + değişmez kültür: işaret, boşluk ve ondalık nokta reddedilir; `"02"` 2 okunur.
- **Plandaki dosya yolları** `WorkflowAgentCache.cs` ve `ChildAgentInvoker.cs` için bayattı: gerçek yollar
  `src/Tracon.Workflows/Internal/WorkflowAgentCache.cs` ve `src/Tracon.Core/Graph/ChildAgentInvoker.cs`.
- **`docs/hafiza/workflows.md` bütçeyi aştı** (16 536 B / 16 000): üç eski not kısaltıldı, tam metni `arsiv/HAFIZA-GECMISI.md`'ye taşındı.

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.

- **K-1101** (`public-api`, `docs/KARARLAR.md`): seçenek adı taşır, değer her çağrıda kapsamdan okunur, `AgentVersion` ile birlikte
  verilemez. Reddedilen: parametre alan fabrika aşırı yüklemesi ve temsilci.
- Kategori dışı (kapanış commit mesajına): önbellek anahtarı adı taşır, değeri değil; geçersiz değerin iletisi yalnız adı yazar,
  değeri hiç yazmaz (run hatası ve log kullanıcı içeriği taşımasın).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan yeniden yazılmadı; 7 sapma kaydedildi) |
| Düzeltme turu sayısı | 1 (denetim sonrası) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (tam test kümesi yeşil) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı; 🟢 kapanışta `ADAYLAR.md`'ye gider.

Bağımsız denetçi (`faz-denetcisi`, 2026-10-26): koddan çıkan kusur yok; 🔴 1, 🟡 3, 🟢 2. Hepsi kapandı.

- 🔴 **Metin kodla çelişiyordu:** "hata değeri hiç tekrarlamaz" iddiası olmayan sürümde (`Version 7 of agent … was not found.`) ve geçmişi
  olmayan kod agent'ında (`does not keep version history`) yanlıştı. XML, site ve CHANGELOG düzeltildi: değer yalnız "sayı değil" ve
  "bildirilmemiş ad" hatalarında hiç yazılmaz; olmayan sürüm numarasıyla anılır.
- 🟡 **Sürümü taşıyan iki dal kanıtsızdı** (`FindAsync(…, sürüm)` ve `stepValues.Count == 0`): iki fonksiyonel case eklendi. Mutasyon
  (`FindAsync` ya da ikinci dal sürümü yok sayar) her biri için tam bir case'i kırdı.
- 🟡 **Boş değer, cevap run'ında düzenlemeyi izler** (kayıtlı değer boştur, sürüm çağrı anında "güncel" çözülür); site cümlesi yalnız
  sayı için doğruydu. Metin düzeltildi; `The_run_that_answers_…` bir `Theory` oldu ve iki davranışı da sabitler.
- 🟡 **Çocuk satırı yokluğu üç testte iddia edilmiyordu:** eklendi.
- 🟢 **Oturum metotları `_pinnedVersion`'da kalır:** Faz 250 devrine yazıldı. 🟢 **`Required = true` parametre ve sürümü seçmenin yetkisi:**
  doğrudan XML ve site metnine yazıldı, aday açılmadı.

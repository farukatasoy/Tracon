# Faz 249 — Kod Grafında Agent Sürümünü Run Parametresi Seçer

> **Durum:** 📋 Planlandı (2026-10-26)
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

- [ ] `AgentVersionParameter = "agentVersion"` ve `POST /tracon/api/workflows/{name}/run` gövdesinde `parameters: { agentVersion: "2" }` → çocuk run satırı `agentVersion = 2`; parametre yoksa güncel sürüm
- [ ] Geçersiz değer (`"abc"`, `"0"`), bildirilmemiş ad ve tutulamayan sürüm adımı adıyla, model çağrısından önce düşürür
- [ ] `AgentVersion` + `AgentVersionParameter` birlikte grafı kurarken düşer
- [ ] Devam ve cevap run'ı aynı sürümle sürer
- [ ] `WorkflowAgentStepOptions` sınıf açıklaması saklanan düğümle eşlik iddiasını düzeltir (Açık Soru 1)
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] Kapanış kapısı yeşil: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`

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

## Bu Fazda Verilen Kararlar

> Dört kategoriden biri `docs/KARARLAR.md`'ye; gerisi kapanış commit mesajına.
> Beklenen: `public-api` — seçenek adın, değer çağrı anında okunur; reddedilen
> alternatifler: parametre alan fabrika aşırı yüklemesi (derleme bağlamadan önce
> koşar, graf şekli parametreye bağlanırdı) ve temsilci (`Func<…, int?>`; her run
> yeni temsilci → önbellek sınırsız büyürdü, `WorkflowAgentCache.cs:238`).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı; 🟢 kapanışta `ADAYLAR.md`'ye gider.

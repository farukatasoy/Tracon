# UCUNCU-FAZ-ADAYLARI.md — Üçüncü Tur Aday Yetenekleri

> **Durum (2026-08-08): FAZ 31–56 PLANLANDI; KALAN 15 KALEM SEÇİLMEDİ.**
> İkinci tur (Faz 8–30) [Faz 30](30-ARAYUZ-CILASI.md) ile kapandı. Dalga 1, 2
> ve 3'ün toplam **yirmi altı** kalemi [Faz 31–52](UCUNCU-FAZ-YOL-HARITASI.md)
> olarak plana dönüştü ve bölümleri **bu dosyadan silindi**. Kalan kalemler
> için seçim yapılmadan faz dokümanı yazılmaz.
>
> 🚨 **2026-08-08 denetimi dört kalemi daha plana çevirdi ve birini kapattı:**
> F-56 → [Faz 53](53-KIRACI-API-ANAHTARLARI.md), F-36 →
> [Faz 54](54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md), F-69 →
> [Faz 55](55-ASENKRON-ONAY-KUTUSU.md), F-74 →
> [Faz 56](56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md). **F-76 faza dönüşmedi;
> bir kusur olarak düzeltildi** (K-352). Aynı denetim numarasız kalan on üç işi
> **F-90…F-102** olarak listeye aldı.
>
> 🚨 **F-72 (ACS uyumu) Dalga 3'e seçildi ama ölçüm sonucu ERTELENDİ** ve bu
> listede kaldı. Bölümü artık **ölçülmüş kanıt** taşıyor; sonraki oturum
> ölçümü tekrarlamak zorunda değildir.
>
> 🚨 **2026-08-10: manuel kabul testi senaryo yazımının düşürdüğü notların
> taranması F-103–F-105'i ekledi** (`docs/manuel-test/00-INDEKS.md` §8).
> Aynı tarama sırasında bulunan gerçek kusurlar (kiracı yalıtımı, `--no-build`
> paketleme, SSE hata çerçevesi vb.) doğrudan kodlandı — burada yalnız var
> olmayan bir **yetenek** gerektiren adaylar durur.
>
> 🚨 **2026-08-14: Ortak kuyruk manuel kabul testi kapanışı F-106'yı ekledi**
> (`HATA-K-003`/K-401) — Magentic round-limit sonrası zarif durdurma, MAF'ın
> kapalı-kutu orkestrasyon durumuna bağımlı bir yetenek adayıdır. Aynı
> koşumda bulunan diğer sekiz kusur (`HATA-K-001`..`008`) doğrudan kodlandı.
>
> 🚨 **2026-08-15: Manuel kabul testi kapanışı (KAPANIS-PLANI §5 Karar 4)
> F-107'yi ekledi** (`HATA-S2-010`/`MT-RES-005`) — bir `WorkflowRunner`
> çalıştırmasının gerçekten iptal edilip edilemediği MAF'ın kendi
> `AgentWorkflowBuilder.BuildSequential` grafiğinin iç iptal davranışına
> bağımlı bir yetenek adayıdır. Kullanıcı kararıyla, yetenek isteyen diğer
> tüm bulgular doğrudan kodlandı; yalnız bu istisna faza döndü.
>
> Bu belge 2026-08-05 tarihli ilk aday listesinin **yerini alır**. Ayrı bir
> aday listesi dosyası açılmaz; iki yerde tutmak kayma üretir. Eski sürümün
> tarihsel değeri "hangi iddia yanlış çıktı" bilgisidir ve o bilgi aşağıdaki
> [Yeniden Yargı](#yeniden-yargı-2026-08-06) bölümünde durur. Eski metnin
> tamamı git geçmişindedir; arşive kopya alınmadı.
>
> **Okuma notu — bu dosya baştan sona okunmaz.** Seçim yaparken önce
> [Bu Turda Neyin Değiştiği](#bu-turda-neyin-değiştiği), sonra
> [Önerilen Sıralama](#önerilen-sıralama--üç-dalga) okunur. Tek bir kalemin
> ayrıntısı için `grep -n "F-68" docs/UCUNCU-FAZ-ADAYLARI.md` yeterlidir.
> Bir kalem faz dokümanına dönüştürüldüğünde ilgili bölüm buradan **silinir**
> ve faz dokümanına taşınır.

---

## Plana Dönüşenler (2026-08-06)

Aşağıdaki **yirmi altı** kalemin bölümü bu dosyadan **silindi**. Ayrıntı artık
faz dokümanındadır; bu tablo yalnız yönlendirmedir.

### Dalga 1 → Faz 31–37

| Kalem | Faz |
|---|---|
| **F-35** Çalıştırma iptali | [Faz 32](32-CALISTIRMA-IPTALI.md) |
| **F-38** ASP.NET Core `IHealthCheck` | [Faz 33](33-SAGLIK-DENETIMI-VE-TESHIS.md) |
| **F-49** `dotnet new` şablon paketi | [Faz 37](37-PROJE-SABLONU.md) |
| **F-52** Geri bildirim ve puanlama | [Faz 31](31-GERI-BILDIRIM-VE-PUANLAMA.md) |
| **F-60** Tanım doğrulama ucu | [Faz 34](34-TANIM-DOGRULAMA-UCU.md) |
| **F-62** Yapılandırma teşhisi | [Faz 33](33-SAGLIK-DENETIMI-VE-TESHIS.md) |
| **F-70** Maliyet ve kota OTel metrikleri | [Faz 35](35-MALIYET-VE-KOTA-METRIKLERI.md) |
| **F-73** Saklama `MaxRows` uygulaması | [Faz 36](36-SAKLAMA-HACIM-SINIRI.md) |

### Dalga 2 → Faz 38–45

| Kalem | Faz |
|---|---|
| **F-37** `Idempotency-Key` desteği | [Faz 43](43-IDEMPOTENCY-KEY.md) |
| **F-42** Yapılandırılmış çıktı (JSON şeması) | [Faz 38](38-YAPILANDIRILMIS-CIKTI.md) |
| **F-46** `AgentPrism.Testing` paketi | [Faz 39](39-TEST-PAKETI.md) |
| **F-53** Üretimden eval kümesi toplama | [Faz 45](45-URETIMDEN-EVAL-KUMESI.md) |
| **F-55** Hata sınıflandırma ve arıza kümeleme | [Faz 44](44-HATA-SINIFLANDIRMA.md) |
| **F-57** Tek yürütücü seçimi | [Faz 42](42-TEK-YURUTUCU-SECIMI.md) |
| **F-63** OpenAPI yayını | [Faz 40](40-OPENAPI-YAYINI.md) |
| **F-76** Kiracı yalıtımının zorlanması | [Faz 41](41-KIRACI-YALITIMININ-ZORLANMASI.md) |

### Dalga 3 → Faz 46–52

| Kalem | Faz |
|---|---|
| **F-30** Vektör bellek ve RAG | [Faz 51](51-VEKTOR-BELLEK-VE-RAG.md) |
| **F-31** AgentPrism'in MCP sunucusu olması | [Faz 50](50-DISA-ACILAN-AGENT-YUZEYI.md) |
| **F-32** Guardrails | [Faz 48](48-GUARDRAILS.md) |
| **F-33** A2A protokolü | [Faz 50](50-DISA-ACILAN-AGENT-YUZEYI.md) |
| **F-47** Kaynak üreteci | [Faz 52](52-KAYNAK-URETECI.md) |
| **F-54** Yeniden oynatma | [Faz 47](47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md) |
| **F-66** Konuşma dallandırma | [Faz 47](47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md) |
| **F-68** Dayanıklı çalıştırma (F-39 içinde) | [Faz 46](46-DAYANIKLI-CALISTIRMA.md) |
| **F-71** Çevrimiçi değerlendirme | [Faz 49](49-CEVRIMICI-DEGERLENDIRME.md) |

🚨 **F-63'ün kapsamı daraldı.** Kalem "TypeScript istemci paketi **ve** OpenAPI
yayını" idi; [Faz 40](40-OPENAPI-YAYINI.md) yalnız **belgeyi** kapsar.
TypeScript/npm yayını ayrı bir dağıtım kanalıdır ve yeni bir aday kalemidir.

🚨 **F-72 seçildi ama plana dönüşmedi.** Bölümü aşağıda duruyor ve artık
ölçülmüş kanıt taşıyor.

Planlama sırasında **on beş kanıt düzeltildi** (Dalga 1–2'de yedi, Dalga 3'te
sekiz); ayrıntı [`UCUNCU-FAZ-YOL-HARITASI.md`](UCUNCU-FAZ-YOL-HARITASI.md)
içindedir.

---

## Bu Turda Neyin Değiştiği

| Ne | Sonuç |
|---|---|
| Bu listede kalan kalem | **20** — üç dalganın yirmi altı ID'si plana dönüştü ve bölümleri silindi |
| Plana dönüşen | **26** — Dalga 1: F-35, F-38, F-49, F-52, F-60, F-62, F-70, F-73 → Faz 31–37 · Dalga 2: F-37, F-42, F-46, F-53, F-55, F-57, F-63, F-76 → Faz 38–45 · Dalga 3: F-30, F-31, F-32, F-33, F-47, F-54, F-66, F-68, F-71 → Faz 46–52 (F-39 F-68'in içinde) |
| İptal edilen | **1** — F-43, çünkü tamamlandı |
| Seçildi ama **ertelendi** | **1** — F-72; ölçüm erteleme getirdi ve kanıt bölümüne yazıldı |
| Kanıtı düzeltilen | **19** — Dalga 1–2'de 11, Dalga 3'te 8. Kalemler ayakta, gerekçeler değişti |
| Yükseltilen | **7** — F-30, F-32, F-33, F-44, F-52, F-53, F-54 |
| Tek faza birleşen | **3 çift** — F-31+F-33, F-54+F-66, F-68 F-39'u yutar |
| Kapsamı daraltılan | **2** — F-63 (TypeScript/npm çıkarıldı) · F-30 (yalnız PostgreSQL) |
| Aciliyeti **artan** | **4** — F-36, F-56, F-69, F-74; hepsi Dalga 3'ün çıktısına bağlı |

**ID'ler sabittir.** F-35 her zaman "çalıştırma iptali"dir — kalem plana
dönüşse bile ID yeniden kullanılmaz. Yeni kalemler F-77'den devam eder. Sabit
ID olmadan sonraki oturumun referansları kaybolur.

**Kod kanıtları 2026-08-06'da bu depo üzerinde `grep` ile yeniden
doğrulandı.** Depo ilerledikçe satır numaraları kayar. Bir kanıtı
kullanmadan önce yeniden ölç.

---

## Değerlendirme Ölçütleri

Dört temel soru korunur:

| Ölçüt | Soru |
|-------|------|
| **Değer** | Bu olmadan AgentPrism'i kim kullanamaz? |
| **Maliyet** | Kaç paket, kaç yeni public tip, kaç migration? |
| **Risk** | Bir tasarım kuralını (K1–K4) zorluyor mu? Bundle bütçesini? |
| **Hazırlık** | MAF veya .NET ekosisteminde hazır mı, sıfırdan mı? |

### Sekiz mercek

Bir fikir yalnız bir mercekten iyi görünüyorsa zayıftır. Her kalemin
**Mercek** satırı destekleyen mercekleri numarayla sayar.

| # | Mercek | Sorusu |
|---|--------|--------|
| 1 | **Benimseme** | İlk agent'a kadar geçen süreyi kısaltır mı? |
| 2 | **Üretim işletimi** | Gece 03:00'te nöbetçi mühendisin işine yarar mı? |
| 3 | **Kurumsal satın alma** | Hangi kurumsal kapıyı açar? |
| 4 | **Performans ve AOT** | Sıcak yolda tahsis üretir mi? AOT duruşunu bozar mı? |
| 5 | **API ergonomisi** | Yanlış kullanım derlemede yakalanır mı? Sonradan eklemek kırıcı mı? |
| 6 | **Ekosistem yerleşimi** | Aspire, OTel, MCP, A2A, DI ile doğal mı oturuyor? |
| 7 | **Ölçme–iyileştirme** | Üretim verisini geliştirmeye geri besler mi? |
| 8 | **Maliyet (FinOps)** | Tüketicinin model faturasını düşürür mü? |

---

## Yeniden Yargı (2026-08-06)

### İptal — tamamlandı

| Kalem | Kanıt |
|---|---|
| **F-43** Sağlayıcıya özgü ayar torbası | `ModelBinding.ProviderSettings` sözleşmede yaşıyor: [`ModelBinding.cs:70`](../src/AgentPrism.Abstractions/Agents/ModelBinding.cs). Karar K-208. Bilinmeyen anahtar derleme hatasıdır — istenen davranış birebir uygulanmış. Eski liste bunu "Faz 26 isteyecek" diye yazmıştı; Faz 26 bitti ve isteği karşıladı |

### Kanıtı yanlışlanan — kalem ayakta, gerekçe değişti

| Kalem | Eski iddia | 2026-08-06 ölçümü |
|---|---|---|
| **F-35** Çalıştırma iptali | "`RunStatus.Canceled` tanımlı ama hiçbir kod yazmıyor" | **Yanlış.** Üç yer yazıyor: [`RunRecordingAgent.cs:186`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs), aynı dosya `:251` ve [`WorkflowRunner.cs:463`](../src/AgentPrism.Workflows/Internal/WorkflowRunner.cs). Gerçek delik başkadır ve aşağıda yazılıdır |
| **F-57** Çok örnekli koordinasyon | "Faz 17 bu olmadan yapılırsa her cron N kez tetiklenir" | **Yanlış.** [`0008_scheduling.sql:46`](../src/AgentPrism.PostgreSql/Migrations/0008_scheduling.sql) `jobs_schedule_scheduled_uq UNIQUE (schedule_id, scheduled_for)` kısıtını taşıyor (K-138). Cron çift tetiklemesi zaten kapalı. Kalemin aciliyeti düştü, kapsamı daraldı |

### Yükseltilenler

| Kalem | Neden yükseldi |
|---|---|
| **F-30** Vektör bellek | Yeni kanıt: kalıcı depo geldi ama arama hâlâ regex **ve** O(n). `SqlAgentFileStore.SearchAsync` her çağrıda tüm dosyaları belleğe alıyor |
| **F-32** Guardrails | Microsoft 2026-06-02'de **Agent Control Specification**'ı yayımladı. İş artık "kendi filtremizi yaz" değil, "bir standarda otur" |
| **F-33** A2A | `Microsoft.Agents.AI.A2A` ve `Microsoft.Agents.AI.Hosting.A2A` paketleri **var**. "Elle uygulamak pahalı" gerekçesi düştü |
| **F-44** Model yedek zinciri | LiteLLM ve Portkey'de temel yetenek. .NET'te karşılığı yok |
| **F-52** Geri bildirim | Önkoşulsuz, tek tablo, ölçme döngüsünün ilk halkası |
| **F-53** Üretimden eval kümesi | Faz 18 bitti. Bu artık "önce yapılmalı" değil, **eksik yarısı** |
| **F-54** Yeniden oynatma | LangGraph 1.2'nin "time travel"i fiilî standart oldu |

### Birleşmeler

| Birleşen | Nasıl |
|---|---|
| **F-31 + F-33** | Tek faz: "dışa açılan agent yüzeyi". İkisi de aynı altyapıyı ister — kimlik doğrulama, kiracı çözümleme, derinlik ve bütçe sınırı, onay sınırı. Protokoller iki ince adaptördür. İki ID korunur |
| **F-54 + F-66** | Tek faz. İkisi de "kayıtlı bir noktadan dallanma"dır; `conversation_items` append-only olduğu için ikisi de aynı dal işaretçisini ister |
| **F-39 → F-68** | F-39 (`202 Accepted`) dayanıklı çalıştırmanın **HTTP yüzüdür**. Ayrı kalem tutmak sözleşmeyi motordan koparır. F-39 ID'si F-68'in içinde yaşar |

---

## A. Kontrol düzlemi çekirdeği — işletim

> **Bu bölümün her iki kalemi de plana dönüştü (2026-08-08):** F-36 →
> [Faz 54](54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md), F-69 →
> [Faz 55](55-ASENKRON-ONAY-KUTUSU.md). Bölümleri buradan silindi.

## B. Model yüzeyi ve yönlendirme

### F-44 · Model yedek zinciri ve yönlendirme 🔥

**Sorun:** Faz 8 devre kesiciyi verdi — yarısı. Eksik yarı: "sağlayıcı A
kesikse B'ye geç". Bugün devre açılınca çalıştırma yalnız **hata veriyor**
([`ModelProviderRegistry.cs:100-109`](../src/AgentPrism.Core/Models/ModelProviderRegistry.cs)).
Giden trafikte eşzamanlılık sınırı da yok; 429 fırtınasına karşı savunma
yalnız devre kesicidir.
**Kapsam:** `ModelBinding.Fallbacks` (sıralı liste) + sağlayıcı başına giden
eşzamanlılık sınırı. Entegrasyon noktası tektir:
`ModelProviderRegistry.CreateChatClient` — Faz 8 zaten oraya dokundu.
**Değer:** Sağlayıcı kesintisi tüketicinin hizmetini durdurmaz.
**Mercek:** 2, 3, 8.
**Hazırlık:** Tek entegrasyon noktası hazır. `VoiceConnectionLimiter` (Faz 29)
eşzamanlılık sayacı için kullanılabilir bir desen taşıyor.
**Maliyet:** Düşük-orta.
**Risk:** Yedek model **farklı fiyatlıdır**. Faz 20'nin maliyet raporu hangi
modelin çalıştığını yazmalıdır — `RunStatistics.ByModel` zaten var. Yedek
varsayılan **kapalı** gelir (K1). Sessiz model değişimi bir sürprizdir;
çalıştırma kaydına açık bir olay yazılmalıdır.
**Bağımlılık:** F-42 ile aynı sözleşmeye (`ModelBinding`) dokunur.
[Faz 38](38-YAPILANDIRILMIS-CIKTI.md) planlandı; devir notu bu kalemin
ekleyeceği `Fallbacks` alanının nasıl oturacağını yazacaktır.
**Ekosistem:** LiteLLM ve Portkey'de temel yetenek: model başına yedek, bütçe
aşımında yedek, otomatik devir. **.NET'te açık kaynak bir karşılığı yok.**

### F-59 · Ön uçuş bütçe denetimi ve bağlam penceresi koruması 🔥

**Sorun:** `ModelDescriptor.ContextWindowTokens`
([`ModelDescriptor.cs:13`](../src/AgentPrism.Abstractions/Models/ModelDescriptor.cs))
**tanımlı ama hiç okunmuyor.** Dört sağlayıcı paketi onu yapılandırmadan
*yazıyor* (`OpenAIProviderExtensions.cs:204`, `AnthropicProviderExtensions.cs:176`,
`GoogleProviderExtensions.cs:172`, `AzureOpenAIProviderExtensions.cs:197`), hiçbir
kod *okumuyor*. Kullanıcı aynı sayıyı `HarnessSettings.MaxContextWindowTokens`
ve `CompactionSettings.MaxContextWindowTokens` alanlarına elle yazıyor. Yanlış
yazarsa hata sağlayıcıdan gelir.
**Kapsam:** Model üstverisinden varsayılan türetme, çağrı öncesi token sayımı,
aşımda erken ve anlaşılır red. `POST /api/agents/{name}/estimate` ucu.
**Değer:** Kota (Faz 21) ve ağaç bütçesi (Faz 12) bu sayıyı **zaten** istiyor;
ikisi de çağrıdan sonra ölçüyor. Ön uçuş denetimi para harcanmadan reddeder.
**Mercek:** 2, 5, 8.
**Hazırlık:** Tokenizer `Microsoft.ML.Tokenizers` üzerinden geçişli
bağımlılıkta hazır (K-104) — Faz 13'te gerçek çalıştırmayla doğrulandı.
**Maliyet:** Düşük.
**Risk:** Token sayımı yaklaşıktır. Erken red yanlış olursa çalışan bir agent
durur; eşik gevşek tutulmalı ve varsayılan **kapalı** gelmelidir.
**Bağımlılık:** Yok. Bugün yapılabilir.
**Ekosistem:** LiteLLM `max_input_tokens` denetimi yapar.

### F-40 · Kiracı başına sağlayıcı anahtarı (BYOK)

**Sorun:** Sağlayıcı anahtarı global yapılandırmadadır. Tüm kiracılar aynı
faturayı paylaşır. Çok kiracılı SaaS için kabul edilemez.
**Kapsam:** Kiracı kaydında **yapılandırma anahtarının adı** durur, değer
çalışma anında `IConfiguration`'dan çözülür. Sır veritabanına yazılmaz.
**Değer:** Kiracı kendi faturasını taşır.
**Mercek:** 3, 8.
**Hazırlık:** K-059 deseni birebir uygulanabilir — MCP kimlik doğrulamasında
zaten kullanılıyor.
**Maliyet:** Orta. `ModelProviderRegistry` kiracı farkındalığı kazanır;
`CreateChatClient` bugün kiracıyı bilmiyor.
**Risk:** `CompiledAgentCache` anahtarı kiracı içermelidir, yoksa bir kiracının
istemcisi diğerine sızar. Bu **gerçek bir güvenlik riskidir** ve testle
kapatılmalıdır.
**Bağımlılık:** F-56 ile aynı kiracı modeline dokunur.
**Ekosistem:** LiteLLM'in "virtual keys" kavramının giden yarısı.

### F-45 · Yanıt önbelleği

**Sorun:** Aynı soru iki kez sorulursa iki kez ödenir. Önbellek yok.
**Kapsam:** `DistributedCachingChatClient` boru hattına takılır.
**Değer:** Deterministik iş yüklerinde fatura düşer.
**Mercek:** 8.
**Hazırlık:** `Microsoft.Extensions.AI` içinde `DistributedCachingChatClient`
hazır **görünüyor** — **doğrulanmadı, `maf-api-kesfi` ile ölçülmeli.**
**Maliyet:** Düşük.
**Risk:** Varsayılan **kapalı**. Agent'ın aynı soruya farklı yanıt vermesi
beklenen davranıştır; önbellek bunu bozar. Kiracı yalıtımı önbellek
anahtarında olmalıdır.
**Bağımlılık:** Yok.
**Ekosistem:** LiteLLM ve Portkey'de standart. Anthropic'in prompt caching'i
ayrı bir kavramdır ve Faz 26'da zaten var.

---

## C. Güvenlik, yönetişim ve uyum

### F-72 · Agent Control Specification (ACS) uyumu — ERTELENDİ (2026-08-06)

> **Aday değildir.** Kullanıcı kararıyla ertelendi. Ölçülmüş kanıt (ACS
> şeması, kesişim noktaları, eşleme tablosu) arşivdedir:
> [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md).

### F-75 · Denetim izi değişmezliği (hash zinciri) **YENİ**

**Sorun:** `audit_log` append-only **ruhla** yazılır ama **teknik olarak
değişmez değildir.** Migration'larda hash zinciri veya imza yok. Veritabanına
yazma yetkisi olan biri geçmişi sessizce düzenleyebilir. Bir denetçi bunu
sorar.
**Kapsam:** Satır başına `prev_hash` + `hash` sütunu, açılışta ve istek
üzerine zincir doğrulaması, `GET /api/audit/verify`. Bir migration.
**Değer:** Denetlenebilirlik iddiası kanıtlanabilir olur.
**Mercek:** 3.
**Hazırlık:** SHA-256 hazır. `TraceSpanIdentity` deposunda benzer bir türetme
deseni zaten var.
**Maliyet:** Düşük.
**Risk:** Zincir **yazma sırasına** bağlıdır; eşzamanlı yazımda sıralama
gerekir. Denetim kaydı yazımı bugün hata yutuyor (`AuditRecorder`) — hash
zinciri bunu zorlaştırır. Yavaşlama **ölçülmeli**.
**Bağımlılık:** F-58 ile çatışır — silme hakkı ile değişmezlik aynı tabloda
buluşur. İkisi **birlikte** tasarlanmalıdır.
**Ekosistem:** AWS CloudTrail ve GCP Cloud Audit Logs bunu yapar.

### F-76 · Paylaşılan SQL kaynağının XML doküman çakışması — KAPATILDI (2026-08-08)

> **Aday değildir.** Faza dönüşmeden bir kusur olarak düzeltildi;
> tam gerekçe ve koruma testi **K-352**'dedir.

### F-58 · Veri konusu silme ve ihracı (GDPR)

**Sorun:** Faz 25 **yaşa göre** temizliyor; **kişiye göre** silme yolu yok.
K-107 "özetlenen mesajlar silinmez" diyor — depo tüm hassas içeriği bilerek
biriktiriyor. Saklama uçları yalnız hedef bazlıdır (`/api/retention/{target}`).
**Kapsam:** Konu kimliğine göre arama, dışa aktarım, silme veya maskeleme.
**Değer:** AB'de kurumsal kapı.
**Mercek:** 3.
**Hazırlık:** Sıfırdan. Konu kimliğinin **nerede** yaşadığı bugün belirsiz —
`sessions` tablosunda kullanıcı kimliği yok.
**Maliyet:** Orta-yüksek.
**Risk:** 🚨 **Gerçek bir tasarım çatışması:** denetim izi değişmez olmalıdır
(F-75), veri konusu ise silme hakkına sahiptir. Silme işaretlemesi mi, alan
bazlı maskeleme mi? `KARARLAR.md`'ye yazılacak cinsten bir karardır ve
atlanamaz.
**Bağımlılık:** F-75 ile birlikte tasarlanır.
**Ekosistem:** Langfuse ve Braintrust'ta veri saklama ve silme birinci sınıf
yetenektir.

### F-41 · İçerik şifreleme (at-rest)

**Sorun:** `conversation_items` tam sohbet geçmişini açık saklıyor. Ekler
`attachments.content` sütununda `bytea` olarak açık duruyor
([`0006_attachments.sql:22`](../src/AgentPrism.PostgreSql/Migrations/0006_attachments.sql)).
**Kapsam:** `IContentProtector` genişleme noktası; varsayılan uygulama yok
(K4).
**Değer:** Regüle sektörlerde zorunlu.
**Mercek:** 3.
**Hazırlık:** .NET Data Protection API kullanılabilir.
**Maliyet:** Orta.
**Risk:** Şifreli sütun **aranamaz**. Konuşma araması ve saklama sorguları
etkilenir. Anahtar döndürme bir tasarım kararıdır.
**Bağımlılık:** F-58 ile aynı veriye dokunur.
**Ekosistem:** Genel veritabanı deseni; agent'a özgü değil.

### F-61 · Argüman düzeyinde tool politikası

**Sorun:** Onay kuralları **tool düzeyindedir**.
[`ToolApprovalRule.cs:41`](../src/AgentPrism.Abstractions/Approvals/ToolApprovalRule.cs)
yalnız `ArgumentsHash` taşır — bu **birebir aynı argüman** demektir, koşul
değil. `refund_order` ya hep onay ister ya hiç. Gerçek ihtiyaç "100 TL altı
otomatik, üstü onay" biçimindedir.
**Kapsam:** `ToolApprovalRule`'a argüman koşulu; izin verilen değer listesi ve
sayısal eşik.
**Değer:** Bugünkü kabalık onay yorgunluğu üretir. Kullanıcı her şeyi
onaylamayı öğrenir ve onay akışı değerini kaybeder.
**Mercek:** 2, 3.
**Hazırlık:** `ApprovalRequiredAIFunction` sarmalaması `ToolRegistry` içinde
tek kapıdır — yer hazır.
**Maliyet:** Düşük.
**Risk:** Koşul dili bir güvenlik yüzeyidir. **İfade değil**, yalnız
karşılaştırma desteklenmelidir. `ToolApprovalRule` public API'dir; alan
eklemek `record` olduğu için ek kurucu ister.
**Bağımlılık:** F-69 ile birlikte değerli.
🚨 [Faz 48](48-GUARDRAILS.md) içerik denetimini **model sınırına** koydu ve
tool **argümanı** denetimini bilerek kapsam dışı bıraktı. O boşluğun sahibi bu
kalemdir; Faz 48'in devir notu bunu yazıyor.
**Ekosistem:** ACS'nin (F-72) `pre_tool_call` kesişim noktası tam olarak bunu
tanımlar.

---

### F-103 · API anahtarı kapsam (`scope`) taksonomisinin genişletilmesi

**Sorun:** `RequireApiKeyScope` yalnız DoD'nin adlandırdığı yüzeye uygulandı
(K-360, Faz 53) — `AgentEndpoints`, `RunEndpoints`'in bir kısmı, MCP/A2A dış
yüzey grupları. Tam taksonomi **bilinçli olarak ertelendi**; K-360'ın kendi
metni bunu "ayrı bir aday kalemi" olarak öngörmüştü. O zamandan beri kapanan
her yeni endpoint ailesi aynı boşluğu miras aldı — manuel kabul testi
senaryoları yazılırken **dokuz bağımsız oturumda** aynı örüntü yeniden
keşfedildi (`docs/manuel-test/00-INDEKS.md` §8): `WorkflowEndpoints`,
`SchedulingEndpoints`, `EvalEndpoints`/`ExperimentEndpoints`,
`GovernanceEndpoints` (`/api/mcp-servers/*`), `KnowledgeEndpoints`,
`ApprovalEndpoints`, `RetentionEndpoints`, `QuotaEndpoints`. Bu oturumun kendi
taraması (`grep -L RequireApiKeyScope src/AgentPrism.AspNetCore/Endpoints/*.cs`)
listeyi genişletti: `AttachmentEndpoints`, `AuditEndpoints`, `CatalogEndpoints`,
`ModelHealthEndpoints`, `ObservabilityEndpoints`, `SessionEndpoints`,
`SkillEndpoints`, `SkillScriptGrantEndpoints`, `VoiceEndpoints`,
`WebhookEndpoints` de dahil — bazıları kasıtlı olarak muaf olabilir
(`MetaEndpoints`/`UiEndpoints` tasarım gereği kimlik doğrulamasız;
`ApiKeyEndpoints`'in kendisi `docs/53-KIRACI-API-ANAHTARLARI.md`'ye göre
bilerek "kapsamsız uç, yalnız Admin rolü") ama geri kalanı **doğrulanmadı** —
sonraki planlama oturumu dosya dosya karar vermeli. Sonuç: `EvalEndpoints`/
`ExperimentEndpoints` için `ApiKeyScope` hiç uygun bir üye taşımıyor (beş üye:
`RunsRead/RunsWrite/AgentsRead/AgentsAdmin/ExternalInvoke`); diğerleri için üye
var ama uç onu hiç çağırmıyor. Salt-okunur (`RunsRead`) bir otomasyon anahtarı
bugün bu uçlarda pratikte TAM yetki taşıyabilir.
**Kapsam:** (a) yeni `ApiKeyScope` üyeleri mi yoksa var olanların yeniden
kullanımı mı — tasarım kararı; (b) 10-19 `Endpoints.cs` dosyasına
`RequireApiKeyScope(...)` eklenmesi; (c) `docs/53-KIRACI-API-ANAHTARLARI.md`'nin
kapsam-kapsama tablosunun güncellenmesi.
**Değer:** Bugün salt-okunur amaçla verilmiş bir otomasyon anahtarı, kapsam
dışı hiçbir uyarı almadan idari eylem (workflow silme, kota değiştirme, MCP
sunucusu kaydetme) yapabilir — en az ayrıcalık ilkesinin sessizce delinmesi.
**Mercek:** 2, 3.
**Hazırlık:** Mekanizma zaten var (`RequireApiKeyScope` uzantısı,
`ApiKeyScopeRequirement`) — yalnız kapsam genişletilir, yeni bir mekanizma
icat edilmez.
**Maliyet:** Orta. Enum'a üye eklemek kırıcı değildir (K-360'ın kendi notu) ama
her uç için DOĞRU granülerliği seçmek (aşırı-ince taksonomi ile birkaç geniş
katman arasında) bir tasarım kararıdır ve 10-19 dosyada mekanik uygulama
gerektirir.
**Risk:** Var olan API anahtarları, bugün "kapsamsız" olan bir uca erişimi
varken, yeni bir zorunlu kapsam eklendiğinde o erişimi KAYBEDER — geriye dönük
uyumluluk/geçiş notu (`docs/KARARLAR.md`'ye) gerekir. `EnablePublicApiTracking=false`
(Faz 7 beklemede) olduğu için bugün derleme-zamanı bir kırılma değildir ama
çalışma-zamanı davranış değişikliğidir.
**Bağımlılık:** Rol politikalarının örnek uygulamada kayıtlı olmaması (bkz.
F-104) ile birlikte ele alınmalı — ikisi birlikte "API anahtarı kapsamı ∩ rol"
yetkilendirme modelinin BÜTÜNÜNÜ oluşturur; biri düzeltilip diğeri
düzeltilmezse örnek uygulamada gözlemlenebilir fark küçük kalır.
**Ekosistem:** —

---

### F-104 · Örnek uygulama rol politikalarını hiç kaydetmiyor — `RequireRole` her yerde no-op

**Sorun:** `RoleEndpointConventionBuilderExtensions.RequireRole`, policy adı
`null` çözülürse HİÇBİR yetkilendirme eklemeyen bilinçli bir tasarımdır (bir
policy kayıtlı değilse uç yalnız üç katmanlı korumadan geçer — eski davranış
korunur, K1). `samples/AgentPrism.Api/Program.cs` `AgentPrismPolicies.Reader`/
`.Operator`/`.Admin`'i **hiçbir zaman** `AuthorizationOptions`'a kaydetmiyor
(`grep -rn "AgentPrismPolicies\." samples/AgentPrism.Api/Program.cs` boş
döner) — bu üç adı kullanan HER `RequireRole(...)` çağrısı (Skill, Approval,
Retention, Quota, Workflow'un `respond` ucu, …) örnek uygulamada sessizce
etkisizdir. Statik bearer token her role açık uçlara eşit erişir. Sonuç: Faz
55'in kendi "Reader karar veremez" DoD iddiası (izole fonksiyonel test
host'unda doğrulanmıştı) referans dağıtımda GÖZLEMLENEMEZ — manuel kabul testi
bu yüzden birden çok dosyada (`13-KIRACI-VE-GUVENLIK.md`, `14-SKILL-VE-SCRIPT.md`,
`21-DAYANIKLILIK-VE-IPTAL.md`) aynı ortam kısıtını ayrı ayrı kaydetmek zorunda
kaldı.
**Kapsam:** Örnek uygulamaya gerçek bir rol kaynağı bağlamak — en basit yol:
`AuthorizationBuilder.AddPolicy(AgentPrismPolicies.Reader, ...)` + statik
bearer token'ın yanına (yalnız gösterim amaçlı) bir rol claim'i üreten basit
bir test/örnek kimlik doğrulama şeması, VEYA API anahtarı kayıtlarına bir rol
alanı eklenip API-anahtarı doğrulamasının bunu bir claim'e çevirmesi (ikincisi
F-103 ile kesişir — bkz. Bağımlılık).
**Değer:** Rol tabanlı yetkilendirmenin gerçek bir dağıtımda NASIL
görüneceğini gösteren tek referans örnek uygulamadır; bugün bu hikayenin en
kritik parçası (rolün gerçekten bir şey engellediği) hiç gösterilmiyor.
**Mercek:** 1, 3.
**Hazırlık:** Mekanizma zaten var (`AgentPrismPolicies`, `RequireRole`); yalnız
örnek uygulamanın DI kaydı eksik.
**Maliyet:** Düşük — tek dosya (`samples/AgentPrism.Api/Program.cs`) + belki
küçük bir test kimlik doğrulama şeması.
**Risk:** Örnek uygulamaya statik/sahte bir rol şeması eklemek, gerçek bir
kimlik sağlayıcısı (OIDC vb.) entegrasyonu gerektiren üretim kurulumuyla
karıştırılabilir — yorum ile net ayrılmalı ("bu yalnız gösterim amaçlıdır").
**Bağımlılık:** F-103 (API anahtarı kapsamı) ile birlikte ele alınmalı.
**Ekosistem:** —

---

### F-105 · Dosya belleği/metin araması aynı kiracı içinde ajan/oturum sınırını gözetmiyor

**Sorun:** `20-BELLEK-RAG-BAGLAM.md` üretilirken bulunan bir kiracı-yalıtımı
şüphesi bu oturumda kod okumasıyla doğrulandı ve **kiracı boyutu** düzeltildi
(`TenantPrefixingAgentFileStore` — bkz. `docs/KARARLAR.md`). Ama düzeltme
kasıtlı olarak **kiracı sınırıyla sınırlı** bırakıldı: `TextSearchProvider`'ın
arama callback'i (`Func<string, CancellationToken, Task<...>>`) tek başına
`query` alır — hangi ajan/oturumun aramayı tetiklediğini bilmez. Callback
`AgentDefinitionCompiler.Compile(...)` anında (bir kez, `CompiledAgentCache`'e
GİREN paylaşılan `AIAgent`'a bağlı) kurulur ve o compiled agent AYNI kiracının
TÜM oturumlarınca yeniden kullanılır. Sonuç: aynı kiracı içinde
`EnableFileMemory` açık bir ajanın/oturumun yazdığı dosya, `EnableTextSearch`
açık BAŞKA bir ajan/oturum tarafından hâlâ bulunabilir — yalnız kiracılar
arası sızıntı kapandı, kiracı-İÇİ sızıntı kapanmadı.
**Kapsam:** Arama callback'inin RUN ZAMANINDA "hangi oturum/ajan çağırıyor"
sorusuna cevap verebilmesi gerekir — bugünkü derleme-zamanı closure yaklaşımı
bunu yapısal olarak veremez. Olası yön: `AmbientTenantScope`'a benzer bir
ambient/`AsyncLocal` "güncel oturum" kapsamı açıp `TenantPrefixingAgentFileStore`'u
oturum bazında ikinci bir alt önekle (`/{tenantId}/{sessionId}`) sarmalamak —
YA DA `FileMemoryProvider`'ın kendi (MAF varsayılanı) oturum bazlı çalışma
klasörü şemasını decompile ederek aynı şemayı arama tarafında yeniden
üretmek.
**Değer:** `EnableFileMemory` + `EnableTextSearch` birlikte kullanan HERHANGİ
bir dağıtımda gerçek bir veri sızıntısı riski — Kritik olmasa da (kiracı
sınırı değil) Yüksek önemde bir kusur adayı.
**Mercek:** 2, 3.
**Hazırlık:** Yok — `AsyncLocal` tabanlı ambient kapsam bu kod tabanında DÖRT
kez yanlış açılıp düzeltildi (Faz 6, 11, 12, 15 — `docs/hafiza/cekirdek-calistirma.md`);
bu kalem BEŞİNCİ bir deneme olacak, dikkatli tasarım ister.
**Maliyet:** Orta-Yüksek. MAF'ın `FileMemoryProvider`'ının kapalı-kutu
davranışına bağımlı (reflection/decompile ile keşif gerekir — `maf-api-kesfi`
skill'i kullanılmalı) veya MAF'ın kendi API'sinde bir genişleme noktası
istenebilir.
**Risk:** Yanlış uygulanan bir ambient kapsam, sessizce YANLIŞ oturumun
bağlamını sızdırabilir — tam da düzeltmeye çalıştığı sınıf hatayı üretme
riski taşır. Kapsamlı testle (farklı ajan/oturum, ardışık ve eşzamanlı
çalıştırma) doğrulanmalı.
**Bağımlılık:** Yok.
**Ekosistem:** —

---

### F-106 · Magentic orkestrasyonu round-limit'e ulaştıktan sonra zarif durmuyor — `WorkflowRunner` bunu önceden kestiremiyor

**Sorun:** `HATA-K-003` (manuel kabul testi, K-401) bir Magentic +
`requirePlanApproval` iş akışında `maxIterations` plan+onay-sonrası-devam+
katılımcı döngüsü için yetersiz kalınca şunu ölçtü: MAF'ın Magentic
orkestratörü round-limit'e ulaşıp kendi `WorkflowOutput`'unu ("Task
execution stopped due to hitting the maximum round count limit.")
ürettikten SONRA, `WorkflowRunner`'ın süper-adım pompası orkestratörü BİR
KEZ DAHA çağırıyor — MAF bunu "orkestrasyon zaten sonlandı" istisnasıyla
reddediyor, çalıştırma `RunFailed` ile bitiyor. K-401 bu istisnanın
mesajını ANLAMLI hale getirdi (artık gerçek nedeni gösteriyor) ama
çalıştırmanın KENDİSİ hâlâ hatayla bitiyor — plan aslında MAF'ın kendi
tanımına göre "tamamlandı" (round-limit'e vararak durdu) sayılabilecekken,
AgentPrism bunu temiz bir `Completed` yerine bir `RunFailed` olarak
kaydediyor.
**Kapsam:** `WorkflowRunner`'ın MAF'tan gelen `WorkflowOutputEvent`'i
(round-limit metnini taşıyan) GÖRDÜKTEN sonra, aynı orkestratöre yönelik
sonraki bir süper-adım çağrısının "zaten sonlandı" istisnasıyla
başarısız olacağını ÖNCEDEN bilip akışı orada temiz bir `Completed`
olarak kapatması gerekir — bugünkü kod bu iki olayı (round-limit çıktısı
ile sonraki başarısız çağrı) ilişkilendirmiyor, MAF'ın ne üreteceğini
sırayla pompalayıp olduğu gibi yansıtıyor.
**Değer:** `requirePlanApproval: true` + Magentic KULLANAN her tüketici,
`maxIterations`'ı plan+onay-sonrası-devam+katılımcı döngüsü için yeterince
yüksek tutmazsa aynı "opak olmayan ama yine de yanlış" `RunFailed`'i
görür — ergonomik bir kusur, veri kaybı riski taşımaz (K-401 sonrası
mesaj zaten doğru nedeni söylüyor).
**Mercek:** 16 (Workflow yürütme).
**Hazırlık:** Yok — MAF'ın Magentic durum makinesinin "sonlandı mı"
sorusuna yanıt veren herkese açık bir API'si var mı, `maf-api-kesfi`
skill'iyle doğrulanmalı.
**Maliyet:** Orta. `WorkflowRunner`'ın süper-adım pompasına "önceki
adımda round-limit çıktısı görüldüyse sonraki çağrıyı deneme, doğrudan
`Completed`'e geç" mantığı eklenmesi gerekir — MAF'ın kapalı-kutu
orkestrasyon durumuna bağımlı olabilir.
**Risk:** Yanlış sezilen bir "zaten sonlandı" durumu, GERÇEKTEN başarısız
olması gereken bir çalıştırmayı sessizce `Completed` gösterebilir —
round-limit metninin TAM eşleşmesi yerine MAF'ın kendi tip/durum
bilgisine dayanmalı, metin eşleştirme kırılgandır.
**Bağımlılık:** K-401 (mesaj netleştirmesi) zaten main'de.
**Ekosistem:** —

---

### F-107 · `WorkflowRunner` iptali MAF'ın sıralı grafiğini gerçekten kesmiyor — çalıştırma sessizce `Completed` ile bitiyor

**Sorun:** `MT-RES-005` (manuel kabul testi, `HATA-S2-010`), Faz 32'nin
kendi kapanışında KANITLANAMAMIŞ bıraktığı açık soruyu (`docs/32-CALISTIRMA-IPTALI.md`
"Plandan Sapmalar") gerçek bir kayıtlı workflow'la (`ozetle-ve-cevir`)
kapattı: uzun bir mesajla başlatılan çalıştırma akış sürerken
`POST /api/runs/{id}/cancel` ile iptal edildi (`202` döndü, iptal öncesi
durumun gerçekten `Running` olduğu ayrı bir `GET` ile doğrulandı — yarış
koşulu değil), ama 3 saniye sonra durum `Canceled` DEĞİL `Completed` oldu,
`error: null`. Kod okuması `WorkflowRunner.cs:356-368`'in AgentPrism
seviyesinde DOĞRU çalıştığını gösterdi: tek bir `linked`
`CancellationTokenSource` hem `IRunCancellationRegistry.Register`'a
(satır 362) hem `run.WatchStreamAsync`'e (satır 589, `linked.Token`)
besleniyor, `OperationCanceledException` doğru yakalanıyor (satır 612).
**Kök neden AgentPrism dışı bir sınırda:** MAF'ın `AgentWorkflowBuilder
.BuildSequential` grafiğinin (`StreamingRun.WatchStreamAsync` iç
uygulaması) dışarıdan gelen iptal token'ını çalışan bir adım ortasında
GERÇEKTEN honor etmiyor gibi görünüyor — ama kullanıcıya göre sonuç aynı:
bir workflow çalıştırması iptal edilemiyor, sessizce tamamlanıyor
(maliyet/zaman israfı + kullanıcı yanıltılması).
**Kapsam:** MAF'ın `BuildSequential` (ve muhtemelen diğer orkestrasyon
tipleri) grafiğinin, kendisine geçirilen `CancellationToken`'ı çalışan bir
adımın ORTASINDA da honor ettiğini doğrulamak; honor etmiyorsa AgentPrism
tarafında bir üst düzey zorlama (her süper-adım sınırında token'ı elle
denetleyip akışı kesme) eklemek gerekebilir — bu, MAF'ın kapalı-kutu
yürütme modeline bağımlı, `maf-api-kesfi` ile araştırılmadan kapsam
netleşmez.
**Değer:** Uzun süren herhangi bir workflow çalıştırmasını iptal etmeye
çalışan her tüketici sessizce yanıltılıyor — çalıştırma görünürde
`Canceled` olması beklenirken gerçekte tam maliyetle `Completed` oluyor.
**Mercek:** 16 (Workflow yürütme), 32 (Çalıştırma iptali).
**Hazırlık:** MAF'ın `AgentWorkflowBuilder`/`StreamingRun` iç yürütme
modelinin bir adım ortasında iptali nasıl (ve honor edip etmediğini)
işlediği `maf-api-kesfi` skill'iyle doğrulanmalı.
**Maliyet:** Belirsiz — MAF kaynağına bakılmadan tahmin edilemez; MAF
gerçekten honor etmiyorsa çözüm MAF'a bağımlı olabilir (üstündeki bir
sarmalama yetmeyebilir).
**Risk:** Üst düzey bir zorlama eklenirse, adım ortasında kesilen bir
workflow'un kısmi durumunun (bellek/araç yan etkileri) tutarsız kalması
riski taşır — MAF'ın kendi iptal semantiğini atlamak yeni bir sınıf hata
üretebilir.
**Bağımlılık:** K-245 (birleşik `CancellationTokenSource`) zaten main'de.
**Ekosistem:** —

---

## D. Yetenek derinliği

### F-34 · Talimat şablonlama ve paylaşılan prompt kütüphanesi

**Sorun:** Faz 19 sürümlemeyi ve A/B'yi verdi; **içerik yeniden kullanımı**
eksik. On agent aynı "kurum kuralları" bloğunu kopyalıyorsa tek yerden
değiştirmenin yolu yok.
**Kapsam:** Değişkenli talimat (`{{tenant_name}}`), kısmi bloklar, agent
tanımında referans.
**Değer:** Talimat bakımı ölçeklenir.
**Mercek:** 1, 7.
**Hazırlık:** Sıfırdan; şablon motoru yazılmalı veya seçilmeli.
**Maliyet:** Orta.
**Risk:** 🚨 **Şablon dili bir güvenlik yüzeyidir.** İfade değil, yalnız
**değer yerleştirme** desteklenmelidir. Tam bir şablon motoru (Scriban gibi)
K2'nin ruhunu zorlar — arayüzden çalıştırılabilir ifade yazılamamalıdır.
**Bağımlılık:** Yok.
**Ekosistem:** Langfuse'un prompt yönetimi tam olarak budur: arayüzden
sürümle, kod aktif sürümü çeker. Braintrust ve Portkey'de de var.

---

## E. Ölçme–iyileştirme döngüsü

Bu grup birlikte "agent'ı ölçerek iyileştirme" döngüsünü kurar. Bugün döngü
**tek yönlüdür**: üretim veri üretir, hiçbiri geri beslenmez.

> **Döngünün beş halkası plana dönüştü:** F-52 geri bildirim
> ([Faz 31](31-GERI-BILDIRIM-VE-PUANLAMA.md)), F-55 hata sınıflandırma
> ([Faz 44](44-HATA-SINIFLANDIRMA.md)), F-53 üretimden eval kümesi
> ([Faz 45](45-URETIMDEN-EVAL-KUMESI.md)), F-54+F-66 yeniden oynatma
> ([Faz 47](47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md)) ve F-71 çevrimiçi
> değerlendirme ([Faz 49](49-CEVRIMICI-DEGERLENDIRME.md)).
> 🚨 **Döngü kapandı (2026-08-08):** son halka F-74 da plana dönüştü →
> [Faz 56](56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md). Bu bölümde kalem
> kalmadı; bölümü buradan silindi.

## F. Paket ailesi ve geliştirici deneyimi

### F-48 · GitOps: tanım dışa ve içe aktarımı

**Sorun:** Agent tanımı ya koddadır ya veritabanında. Bir ekip tanımlarını
git'te sürümlemek ve dev→prod terfi etmek isterse **yolu yok**.
**Kapsam:** JSON/YAML dışa aktarım, `--dry-run` ile fark gösterimi, içe
aktarımda sürüm geçmişi ve denetim izi korunur (Faz 9 hazır).
**Değer:** Dağıtım hattına girer. Faz 19'un arayüz diff'inden farklı iştir.
**Mercek:** 1, 3.
**Hazırlık:** `agent_definition_versions` ve `audit_log` hazır.
**Maliyet:** Orta.
**Risk:** İçe aktarım **üzerine yazar**. Çakışma çözümü bir karardır.
**Bağımlılık:** F-60 (doğrulama ucu) bunun CI adımıdır;
[Faz 34](34-TANIM-DOGRULAMA-UCU.md) tamamlandı (2026-08-06) — önkoşul hazır.
**Ekosistem:** Dify ve n8n dışa aktarımı verir. Langfuse prompt'ları API'den
yönetir.

### F-50 · Tipli yönetim istemcisi ve CLI

**Sorun:** Yönetim API'sini kod içinden çağırmanın tipli yolu yok. Migration
bugün açılışta uygulanıyor; CI/CD hattı ayrı bir migration adımı ister.
**Kapsam:** `AgentPrism.Client` (AOT uyumlu, kaynak üretilmiş JSON) +
`dotnet agentprism` global aracı: migration uygula, tanım dışa/içe aktar
(F-48), sağlık denetimi. CLI istemcinin ilk tüketicisidir.
**Değer:** Dağıtım hattı olgunlaşır.
**Mercek:** 1, 3.
**Hazırlık:** F-63'ün OpenAPI belgesi üretim kaynağıdır ve
[Faz 40](40-OPENAPI-YAYINI.md) olarak planlandı. 🚨 O faz **yalnız belgeyi**
kapsar; TypeScript istemcisi bu kalemin veya yeni bir kalemin işidir.
**Maliyet:** Orta.
**Risk:** İki yeni paket, K-007 gerekçesi ister. İstemci sözleşmesi sunucuyla
birlikte sürümlenmelidir.
**Bağımlılık:** F-63'ten sonra — [Faz 40](40-OPENAPI-YAYINI.md).
[Faz 52](52-KAYNAK-URETECI.md) bir üreteç altyapısı kurar; `AgentPrism.Client`'ın
kaynak üretilmiş JSON'u aynı projeyi kullanabilir ve **ikinci bir üreteç
projesi açılmamalıdır**.
**Ekosistem:** LiteLLM ve Langfuse CLI verir.

### F-51 · .NET Aspire entegrasyonu

**Sorun:** Aspire kurumsal .NET'in yeni varsayılan besteleme yoludur.
AgentPrism'in Aspire kaynağı yok.
**Kapsam:** PostgreSQL kaynağı, OTel bağlantısı ve panoya bağlantı hazır gelir.
**Değer:** Yerel geliştirme kurulumu tek komuta iner.
**Mercek:** 1, 6.
**Hazırlık:** Faz 6'nın telemetrisi zaten OTel;
`AgentPrismDiagnostics.ActivitySourceName` public.
**Maliyet:** Düşük.
**Risk:** Yeni paket (K-007). Aspire sürüm hızı yüksektir; bakım borcu üretir.
**Bağımlılık:** F-38 (health check) [Faz 33](33-SAGLIK-DENETIMI-VE-TESHIS.md) olarak
planlandı; o faz önce biterse Aspire panosu doğal çalışır.
**Ekosistem:** .NET'e özgü. Karşılığı Docker Compose'dur.

### F-67 · Performans regresyon kapısı

**Sorun:** Dört doğrulama kapısı **doğruluğu** koruyor; performans
korunmuyor. Depoda `BenchmarkDotNet` projesi yok (slnx'te 14 kaynak + 13 test
projesi var, benchmark yok).
**Kapsam:** BenchmarkDotNet + tahsis eşiği. İlk hedefler: `run_events` yazma
yolu, `AgentDefinitionCompiler` önbelleği ve `SqlAgentFileStore.SearchAsync`
yolu.
🚨 [Faz 51](51-VEKTOR-BELLEK-VE-RAG.md) o son yolu **düzeltiyor** ve
düzeltmenin "okunan satır sayısı" ölçümünü devir notuna yazıyor. O sayı bu
kalemin **başlangıç eşiğidir**.
**Değer:** Bir kütüphanede sıcak yolun tahsis bütçesi olmalıdır.
**Mercek:** 4.
**Hazırlık:** BenchmarkDotNet hazır.
**Maliyet:** Orta.
**Risk:** Benchmark CI'da gürültülüdür. Eşik geniş tutulmalı veya yalnız elle
koşulmalıdır. Beşinci bir kapı her fazı yavaşlatır — bu bir karardır.
**Bağımlılık:** Yok.
**Ekosistem:** Bu depoya **kültürel olarak uygundur**; dört kapı disiplini
zaten var.

---

## G. Dış tüketici yüzeyi

### F-64 · Gömülebilir sohbet bileşeni

**Sorun:** Arayüz **tam bir kontrol düzlemidir**. Tüketicinin kendi
uygulamasına koyacağı küçük bir sohbet kutusu yok.
**Kapsam:** Ayrı, küçük bir bundle. Hedef **<30 KB gzip**.
**Değer:** Tüketici son kullanıcıya agent açabilir.
**Mercek:** 1.
**Hazırlık:** Playground'un akış mantığı yeniden kullanılabilir.
**Maliyet:** Orta.
**Risk:** 🚨 **Bundle bütçesi ayrı tutulmalıdır.** Bugün kontrol düzlemi
151,3 KB / 250 KB kullanıyor, kalan pay ~99 KB. Sohbet bileşeni bu paya
**girmemelidir**; ayrı bir çıktı ve ayrı bir kapı ister. Kontrol düzlemi
bileşenleri bu pakete sızmamalıdır — sızarsa 30 KB hedefi tutmaz.
**Bağımlılık:** F-56 (API anahtarı) olmadan güvenli gömülemez — son
kullanıcının tarayıcısına yönetim token'ı konulamaz.
[Faz 46](46-DAYANIKLI-CALISTIRMA.md)'nın `202 Accepted` sözleşmesi uzun süren
bir sohbet için ikinci bir okuma yolu verir.
**Ekosistem:** Vercel AI SDK'nın `useChat` kancası ve Dify'ın gömülebilir
widget'ı.

### F-65 · Gelen tetikleyiciler

**Sorun:** Faz 21 **giden** webhook'u verdi. Tersi yok: dış bir olay (Slack
mesajı, e-posta, kuyruk) bir çalıştırma başlatamıyor.
**Kapsam:** İmzalı gelen webhook ucu, olay → agent eşlemesi, Faz 17'nin
kuyruğuna düşürme.
**Değer:** Agent olaya tepki verir; yalnız sorulunca konuşmaz.
**Mercek:** 1, 3.
**Hazırlık:** Faz 17'nin kuyruğu ve Faz 21'in imza doğrulayıcısı hazır —
`WebhookSigner` ters yönde de kullanılır.
**Maliyet:** Düşük.
**Risk:** Gelen uç **kimlik doğrulaması olmadan** açılamaz. Her kaynak için
ayrı sır gerekir ve K-059 deseni uygulanır.
**Bağımlılık:** F-56'dan sonra doğal.
**Ekosistem:** n8n ve Dify'ın ana ekseni. Inngest olay tabanlı tetiklemeyi
altyapı olarak satar.

---

## Ekosistem Boşluk Tablosu

"X'te standart, .NET'te yok." AgentPrism'in yankı uyandırma ihtimali en çok
buradadır.

Kalın yazılan kalemler **hâlâ bu listededir**; 📋 işaretliler plana dönüştü.

| Yetenek | Nerede standart | .NET durumu | Karşılık gelen kalem |
|---|---|---|---|
| Dayanıklı agent çalıştırması (crash-resume) | LangGraph 1.2 · Mastra `createDurableAgent` · Temporal · Inngest · Restate | **Yok** | F-68 → [Faz 46](46-DAYANIKLI-CALISTIRMA.md) 📋 |
| Kontrol noktasından geri sarma (time travel) | LangGraph · Arize playground | **Yok** | F-54 → [Faz 47](47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md) 📋 |
| Üretim izinden tek tıkla eval vakası | Langfuse · Braintrust | **Var** | F-53 → [Faz 45](45-URETIMDEN-EVAL-KUMESI.md) ✅ |
| Üretim trafiğinde LLM-yargıç puanlama | Braintrust · Arize Phoenix · Langfuse | **Yok** | F-71 → [Faz 49](49-CEVRIMICI-DEGERLENDIRME.md) 📋 |
| Guardrail eklenti noktası | LiteLLM · Portkey · NeMo Guardrails · Guardrails AI | **Yok** | F-32 → [Faz 48](48-GUARDRAILS.md) 📋 |
| Agent'ı MCP tool'u olarak yayımlama | Dify · n8n · OpenAI AgentKit | **Yok** | F-31 → [Faz 50](50-DISA-ACILAN-AGENT-YUZEYI.md) ✅ |
| A2A ile satıcılar arası çağrı | Google A2A · sekiz satıcı kurulu | MAF paketi **var** (ön sürüm), kontrol düzlemi yok | F-33 → [Faz 50](50-DISA-ACILAN-AGENT-YUZEYI.md) ✅ |
| Vektör bellek ve RAG | LlamaIndex · LangChain | Semantic Kernel connector'ları var ama **yalnız ön sürüm** ve `Npgsql` 8'e bağlı | F-30 → [Faz 51](51-VEKTOR-BELLEK-VE-RAG.md) 📋 |
| Derleme anında tool doğrulama | — | **Yalnız .NET'te mümkün** | F-47 → [Faz 52](52-KAYNAK-URETECI.md) ✅ |
| Tipli yapılandırılmış çıktı | Pydantic AI · OpenAI · Instructor | **Yok** | F-42 → [Faz 38](38-YAPILANDIRILMIS-CIKTI.md) 📋 |
| Maliyet metriğinin Prometheus'a akması | LiteLLM | **Yok** | F-70 → [Faz 35](35-MALIYET-VE-KOTA-METRIKLERI.md) 📋 |
| **Model yedek zinciri ve yönlendirme** | LiteLLM · Portkey · Kong AI Gateway | **Yok** | **F-44** |
| **Sanal anahtar + anahtar başına bütçe** | LiteLLM · Portkey | **Yok** | **F-56** + F-40 (zemin: [Faz 41](41-KIRACI-YALITIMININ-ZORLANMASI.md) 📋) |
| **Prompt kütüphanesi ve şablon** | Langfuse · Braintrust · Portkey | Kısmen — sürümleme var (Faz 19), şablon yok | **F-34** |
| **Olay tabanlı agent tetikleme** | n8n · Dify · Inngest | **Yok** | **F-65** |
| **Taşınabilir çalışma anı politikası** | Microsoft ACS | .NET paketi **var** ama **beta ve native** (beş RID) | **F-72** ⏸ ertelendi |

🚨 **Dokuz boşluğun dokuzu plana girdi.** Kalan beş satır bu listenin
stratejik çekirdeğidir; ikisi kimlik (F-56, F-40), biri maliyet (F-44), biri
ergonomi (F-34), biri tetikleme (F-65).

**Neden kimse yapmamış?** Üç yanıt vardır ve hepsi AgentPrism'in lehinedir:

1. **Python ekosisteminde kontrol düzlemi ayrı bir üründür** (Langfuse,
   Braintrust). .NET'te tek bir NuGet ailesi hem çerçeveyi hem düzlemi
   verebilir.
2. **Dayanıklı çalıştırma Python'da ayrı altyapı ister** (Temporal, Inngest).
   .NET'te `IHostedService` + PostgreSQL kuyruğu **zaten kurulmuş** durumda.
3. **Derleme anı doğrulama Python'da imkânsızdır.** F-47 ve F-42 .NET'in
   ayrıcalığıdır.

**Kaynaklar:**
[LangGraph durable execution](https://docs.langchain.com/oss/python/langgraph/durable-execution) ·
[Mastra workflow runners](https://mastra.ai/docs/deployment/workflow-runners) ·
[Langfuse observability](https://langfuse.com/docs/observability/overview) ·
[Braintrust eval tools 2026](https://www.braintrust.dev/articles/best-ai-evaluation-tools-2026) ·
[LiteLLM AI Gateway](https://docs.litellm.ai/docs/simple_proxy) ·
[Agent Control Specification](https://microsoft.github.io/agent-governance-toolkit/packages/agent-control-specification/) ·
[A2A v1 in Microsoft Agent Framework](https://devblogs.microsoft.com/agent-framework/a2a-v1-is-here-cross-platform-agent-communication-in-microsoft-agent-framework-for-net/)

---

## Bilerek Önerilmeyenler

Değerlendirildi ve **alınmaması** önerildi. Reddin gerekçesi kabulün gerekçesi
kadar değerlidir.

| Kalem | Neden hayır |
|---|---|
| Arayüzden tool kodu yazma / no-code tool oluşturucu | K2'nin doğrudan ihlali. Güvenlik sınırıdır, gevşetilmez |
| OpenAI Assistants API uyumluluğu | OpenAI kendisi Responses API'ye taşıdı; ölü bir yüzeye maliyet |
| gRPC yönetim yüzeyi | HTTP + OpenAPI yeterli; ikinci yüzey iki kat bakım |
| Çoklu model konsensüs / oylama | Niş; tüketici bunu kendi agent'ında kurar |
| Agent/skill pazar yeri | Barındırma ve moderasyon işi; kütüphane sınırının dışında |
| **Kendi vektör veritabanımızı yazmak** | F-30 `pgvector` ile çözülür. Depolama motoru yazmak kütüphane sınırının dışındadır |
| **S3/Azure Blob ek deposu uygulaması** | Genişleme noktası **zaten var**: `IAttachmentStorage` kayıtlıysa içerik orada yaşar ([`IAttachmentStore.cs`](../src/AgentPrism.Abstractions/Attachments/IAttachmentStore.cs)). Somut uygulama tüketicinin işidir; yazmak iki bulut SDK'sı bağımlılığı getirir |
| **Kendi eval çerçevemizi yazmak** | Faz 18 MAF'ın `LocalEvaluator`'ını kullanıyor (K-139). İkinci bir çerçeve bakım borcudur |
| **MAF tiplerinin üzerine soyutlama** | K3'ün doğrudan ihlali |
| **Dağıtık hız sınırı (Redis)** | K-158 hız sınırını bilerek bellekte tuttu. Redis bağımlılığı K1'i (sıfır sürpriz) zorlar. Gerçek ihtiyaç kotadır ve o **zaten veritabanındadır** |
| **Kendi OTel toplayıcımız** | K-055 `ActivityListener` ile topluyor. Toplayıcı yazmak ekosistemle çakışır |
| **Arayüzde Mermaid.js ile graf çizimi** | K-132 ölçtü: mermaid.js ~100 KB gzip eder. Kalan bundle payının tamamıdır |
| **Yerleşik model listesi** | K-032 kararı. Model adları NuGet yayın hızından hızlı değişir |
| **Declarative workflow (MAF)** | K-129 ölçtü: +19 paket ve Responses API şartı |
| **Azure AI Foundry** | K-212 ölçtü: 37 geçişli paket ve doğrulanamazlık. Karar değişmedi |

---

## Bağımlılık Grafiği

Oklar **gerçek önkoşulları** gösterir. Ok yoksa kalemler bağımsızdır.
Yuvarlak köşeli düğümler **plana dönüşmüş** kalemlerdir; bu listede yoktur ve
yalnız önkoşul zincirini göstermek için durur.

```mermaid
flowchart LR
    F31p(["F-52 - Faz 31<br/>Geri bildirim"]) --> F49p(["F-71 - Faz 49<br/>Cevrimici eval"])
    F44p(["F-55 - Faz 44<br/>Hata sinifi"]) --> F74["F-74<br/>Kanarya"]
    F31p --> F74
    F49p --> F74

    F42p(["F-57 - Faz 42<br/>Tek yurutucu"]) --> F36["F-36<br/>Oksuz uzlastirma"]
    F46p(["F-68 - Faz 46<br/>Dayanikli calistirma"]) --> F36
    F46p --> F69["F-69<br/>Onay kutusu"]
    F61["F-61<br/>Arguman politikasi"] --> F69
    F48p(["F-32 - Faz 48<br/>Guardrails"]) --> F61
    F48p --> F72["F-72 - ERTELENDI<br/>ACS uyumu"]

    F38p(["F-42 - Faz 38<br/>Yapilandirilmis cikti"]) --> F44["F-44<br/>Yedek zinciri"]
    F41p(["F-76 - Faz 41<br/>Kiraci yalitimi"]) --> F56["F-56<br/>API anahtarlari"]
    F41p --> F40["F-40<br/>BYOK"]
    F50p(["F-31+F-33 - Faz 50<br/>Disa acilan yuzey"]) -->|"acil kilar"| F56
    F56 --> F64["F-64<br/>Gomulebilir sohbet"]
    F56 --> F65["F-65<br/>Gelen tetikleyici"]
    F75["F-75<br/>Denetim zinciri"] --> F58["F-58<br/>GDPR silme"]

    F34p(["F-60 - Faz 34<br/>Dogrulama ucu"]) --> F48["F-48<br/>GitOps"]
    F40p(["F-63 - Faz 40<br/>OpenAPI"]) --> F50["F-50<br/>Istemci + CLI"]
    F52p(["F-47 - Faz 52<br/>Kaynak ureteci"]) --> F50
    F33p(["F-38 - Faz 33<br/>Health check"]) --> F51["F-51<br/>Aspire"]
    F51p(["F-30 - Faz 51<br/>Vektor bellek"]) --> F67["F-67<br/>Performans kapisi"]

    classDef planlandi fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    classDef ertelendi fill:#5a5a5a,stroke:#2c2c2c,color:#ffffff
    class F31p,F44p,F49p,F42p,F46p,F48p,F38p,F41p,F50p,F34p,F40p,F52p,F33p,F51p planlandi
    class F72 ertelendi
```

> **Yuvarlak köşeli yeşil düğümler plana dönüşmüştür** ve bu listede
> **yoktur**; yalnız önkoşul zincirini göstermek için dururlar. On dört düğüm
> bu durumdadır — Faz 31–52.

> 🚨 **Üç ok yön değiştirdi.** Dalga 3 planlandıktan sonra bazı bağımlılıklar
> tersine döndü: Faz 46 artık F-36'nın **sebebi**, Faz 50 F-56'nın **sebebi**,
> Faz 49 F-74'ün **son eksik önkoşulu**. Bunlar önkoşul değil, **aciliyet**
> oklarıdır ve etiketleriyle ayrılmıştır.

> **F-35 kısmen kapandı.** [Faz 32](32-CALISTIRMA-IPTALI.md) iptali **tek
> örnek** için çözer; çok örnekli yarısı
> [Faz 42](42-TEK-YURUTUCU-SECIMI.md)'nin `ISingletonLeaseStore`'unu bekler.

> **F-63'ün oku daraldı.** [Faz 40](40-OPENAPI-YAYINI.md) yalnız belgeyi
> yayımlar; F-50'nin istemci üretimi için gereken kaynak budur, ama TypeScript
> tarafı ayrı bir kalemdir.

> Grafikte yalnız **önkoşulu veya bağımlısı olan** kalemler görünür. Tam
> bağımsız kalemler (F-34, F-41, F-45, F-59) grafikte yoktur ve istenen sırada
> yapılabilir.

---

## Önerilen Sıralama — Üç Dalga

### Dalga 1 — ✅ planlandı (2026-08-06), bu listeden çıktı

Sekiz kalemin tamamı [Faz 31–37](UCUNCU-FAZ-YOL-HARITASI.md) olarak plana
dönüştü. Bölümleri bu dosyadan silindi; yönlendirme için
[Plana Dönüşenler](#plana-dönüşenler-2026-08-06) tablosuna bakın.

**Kod yazılmadı.** Fazlar `📋 Planlandı` durumundadır.

### Dalga 2 — ✅ planlandı (2026-08-06), bu listeden çıktı

Sekiz kalemin tamamı [Faz 38–45](UCUNCU-FAZ-YOL-HARITASI.md) olarak plana
dönüştü. Bölümleri bu dosyadan silindi; yönlendirme için
[Plana Dönüşenler](#plana-dönüşenler-2026-08-06) tablosuna bakın.

**Kod yazılmadı.** Fazlar `📋 Planlandı` durumundadır.

Dalganın ortak gerekçesi korunur: **her kalem başka bir işten önce yapılmazsa
iki kat pahalıya gelir** — biri kırıcı bir sürüm kararı, biri yeniden yazım,
biri güvenlik düzeltmesi olarak geri döner.

### Dalga 2'den doğan yeni aday kalemler

Planlama dokuz işi **bilinçli olarak kapsam dışına** çıkardı. Bunlar yeni kalem
olarak buraya yazılmalıdır; ID'ler **F-77'den** devam eder.

| Kapsam dışı iş | Hangi fazdan | Neden ayrı bir kalem |
|---|---|---|
| PostgreSQL RLS ile derinlemesine savunma | [Faz 41](41-KIRACI-YALITIMININ-ZORLANMASI.md) | SQLite'ta karşılığı **yok**; üç sağlayıcıda davranış ayrışır. Faz 41 sözleşme testi kapısını seçti, RLS'i **iptal etmedi** |
| 🚨 Çalıştırmanın alt yazmalarında **açık kiracı** | [Faz 41](41-KIRACI-YALITIMININ-ZORLANMASI.md) | `IRunStore.AppendEventAsync` · `CompleteRunAsync` · `UpdateRunCostAsync` · `RecordToolInvocationAsync` kiracı süzgeci taşımaz (K-280). Ambient ile süzmek denendi ve geri alındı: `RunStartInfo.TenantId` ambient kiracıyı bilerek ezer ve süzgeç meşru yazmaları düşürüyordu. Gerçek denetim, çağrının **beklenen** kiracıyı taşımasını ister — yani `RunEvent`/`RunCompletion`/`ToolInvocationRecord`'a birer alan. Bugün ulaşılabilir sızıntı **yok** (uuid v7 kimlikler, okuma tarafı süzülü); public API büyüteceği için ayrı kalem |
| MCP OAuth token'ının örnekler arasında paylaşılması | [Faz 42](42-TEK-YURUTUCU-SECIMI.md) | 🚨 **K-059 ile çatışır** — `secret` veritabanına yazılmaz. Kendi kararını ister |
| Paylaşılan (dağıtık) hız sınırı | [Faz 42](42-TEK-YURUTUCU-SECIMI.md) | K-158 bunu bilerek bellekte tuttu; tek yürütücü seçimi bu sorunu **çözmez** |
| Akışlı yanıtta idempotency | [Faz 43](43-IDEMPOTENCY-KEY.md) | Doğru evi F-68'in `202 Accepted` + `Location` sözleşmesidir |
| TypeScript istemci paketi ve npm yayını | [Faz 40](40-OPENAPI-YAYINI.md) | İkinci bir dağıtım kanalı; ayrı yayın hattı, kimlik bilgisi ve sürümleme ister |
| Çok turlu eval vakası terfisi | [Faz 45](45-URETIMDEN-EVAL-KUMESI.md) | `EvalCase` sözleşmesini değiştirir; Faz 7'den **önce** karara bağlanması ucuzdur |
| `AgentPrismMcpOptions`'ı `IConfiguration`'a bağlamak | [Faz 42](42-TEK-YURUTUCU-SECIMI.md) | Ölçüldü: `.UseMcp()` yalnız kod-taraflı `configure` delegesi kabul eder, `IConfiguration.Bind` hiç çağrılmaz — `AgentPrism:Mcp:RefreshInterval` gibi bir ortam değişkeni **sessizce hiçbir şey yapmaz**. Faz 42'den önce de böyleydi; ilk kez orada gerçek bir dağıtım denemesinde ortaya çıktı |
| 🚨 `BackgroundService` başlatma sırası migration'la yarışır | [Faz 42](42-TEK-YURUTUCU-SECIMI.md) | Ölçüldü: `MigrationHostedService.StartAsync` migration'ları TAM bekler ama `BackgroundService.StartAsync` (taban sınıf) `ExecuteAsync`'i beklemeden döner; kayıt sırası `.UseMcp()` `.UseSqlite()`'tan önceyse `McpDiscoveryService`'in ilk SQL denemesi migration bitmeden çalışabilir ("no such table"). Kendiliğinden iyileşir (bir sonraki turda) ama gözlemlenebilir bir uyarı üretir. Kalıcı çözüm hosted service sırasını garanti etmek veya ilk turu geciktirmek — ikisi de kendi kararını ister |

### Dalga 3 — ✅ planlandı (2026-08-06), bu listeden çıktı

Dokuz kalem [Faz 46–52](UCUNCU-FAZ-YOL-HARITASI.md) olarak plana dönüştü.
Bölümleri bu dosyadan silindi; yönlendirme için
[Plana Dönüşenler](#plana-dönüşenler-2026-08-06) tablosuna bakın.

🚨 **F-72 seçildi ama plana dönüşmedi.** Ölçüm erteleme getirdi ve kalem
[C bölümünde](#f-72--agent-control-specification-acs-uyumu--ölçüldü-ertelendi-2026-08-06)
ölçülmüş kanıtıyla duruyor. Dalga bu yüzden sekiz değil **yedi** fazdır.

**Kod yazılmadı.** Fazlar `📋 Planlandı` durumundadır.

Dalganın ortak gerekçesi korunur: **her kalem kendi başına bir tur
büyüklüğündedir** ve hiçbiri eksik bir yarıyı tamamlamaz; her biri .NET'te
karşılığı **hiç bulunmayan** bir yetenek ekler.

### Dalga 3'ten doğan yeni aday kalemler

Planlama **on** işi bilinçli olarak kapsam dışına çıkardı. Tam liste ve
gerekçeleri [`UCUNCU-FAZ-YOL-HARITASI.md`](UCUNCU-FAZ-YOL-HARITASI.md)'nin
"Dalga 3'ün Açtığı Yeni Aday Kalemler" bölümündedir; burada tekrarlanmaz.
ID'ler **F-77'den** devam eder.

Öne çıkan üçü:

| Kapsam dışı iş | Hangi fazdan | Neden ayrı bir kalem |
|---|---|---|
| Tur bazlı kontrol noktası (F-68 Okuma B) | [Faz 46](46-DAYANIKLI-CALISTIRMA.md) | 🚨 MAF agent düzeyinde kanca **vermiyor** — ölçüldü. Kancayı AgentPrism yazmak K3'ü zorlar |
| Azure AI Content Safety adaptörü | [Faz 48](48-GUARDRAILS.md) | Ağırlık **4 paket** (ölçüldü) — sorun değil. Erteleme gerekçesi doğrulanamazlıktır (K-212 emsali) |
| `IVectorSearchStore`'un SQL Server / SQLite uygulaması | [Faz 51](51-VEKTOR-BELLEK-VE-RAG.md) | SQL Server'ın yerel `VECTOR` tipi ve SQLite'ın `sqlite-vec` uzantısı **ölçülmedi** |

### Faz 48'in uygulanmasından doğan yeni aday kalemler (2026-08-07)

Bunlar plan anında değil, **kod yazılırken** ortaya çıktı. ID'ler **F-87'den**
devam eder; tam gerekçeleri [`48-GUARDRAILS.md`](48-GUARDRAILS.md)'nin devir
notundadır.

| ID | Kalem | Neden ayrı |
|---|---|---|
| **F-87** | 🚨 Kayıtlardaki hassas verinin redaksiyonu | Guard **model sınırındadır**; `run_events.RunStarted` kullanıcının ham istemini (Faz 45) ve `run_inputs` ham mesajları (Faz 47) saklar. Maskeleme bunları geriye dönük temizlemez. Ayrı bir sözleşme: hangi kayıt, hangi anda, geri alınamaz mı? **Faz 45'in eval terfisi ve Faz 47'nin yeniden oynatması ham girdiye BAĞIMLIDIR** — redaksiyon ikisini de bozar ve o çatışma önce karara bağlanmalıdır |
| **F-88** | Guard kararının transcript'te gösterilmesi | Faz 48 iki olay tipini **ham olay akışına** ekledi; katlanmış transcript görünümü (`transcript.ts`) onları göstermiyor. `compaction` için var olan "sistem konuşmayı değiştirdi" öğesinin kardeşi gerekir: yeni öğe tipi + bileşen + sözlük anahtarları |
| **F-89** | Kiracı bazlı guard kuralları | `ContentGuardContext.TenantId` **bugün taşınıyor** ve özel bir guard onu kullanabilir; ama yerleşik `PatternContentGuard` tek bir kural kümesi taşır. Kiracı başına kural, kuralların **nerede yaşadığı** sorusunu açar (yapılandırma mı, veritabanı mı) ve K2'ye benzer bir sınır kararı ister |

### Numaralandırılan kapsam-dışı işler (2026-08-08 denetimi)

Aşağıdaki kalemler daha önce **numarasızdı** ve yalnız devir notlarında yaşıyordu.
2026-08-08 denetimi bunları resmî F-numarasıyla listeye aldı; böylece sonraki bir
planlama turu onları yeniden **keşfetmek** zorunda kalmaz. ID'ler **F-90**'dan
devam eder ve sabittir.

| ID | Kalem | Kaynak | Neden ayrı bir kalem |
|---|---|---|---|
| **F-90** | PostgreSQL RLS ile derinlemesine savunma | [Faz 41](41-KIRACI-YALITIMININ-ZORLANMASI.md) | SQLite'ta karşılığı **yok**; üç sağlayıcıda davranış ayrışır. Faz 41 sözleşme testi kapısını seçti, RLS'i **iptal etmedi** |
| **F-91** | MCP OAuth token'ının örnekler arasında paylaşılması | [Faz 42](42-TEK-YURUTUCU-SECIMI.md) | 🚨 **K-059 ile çatışır** — `secret` veritabanına yazılmaz. Kendi kararını ister |
| **F-92** | Paylaşılan (dağıtık) hız sınırı | [Faz 42](42-TEK-YURUTUCU-SECIMI.md) | K-158 bunu bilerek bellekte tuttu; tek yürütücü seçimi bu sorunu **çözmez**. 🚨 "Bilerek Önerilmeyenler" tablosundaki Redis maddesiyle **çakışır**; alınırsa o karar yeniden açılır |
| **F-93** | TypeScript istemci paketi ve npm yayını | [Faz 40](40-OPENAPI-YAYINI.md) | İkinci bir dağıtım kanalı; ayrı yayın hattı, kimlik bilgisi ve sürümleme ister. F-63'ten ayrıldı |
| **F-94** | Çok turlu eval vakası terfisi | [Faz 45](45-URETIMDEN-EVAL-KUMESI.md) | `EvalCase` sözleşmesini değiştirir; Faz 7'den **önce** karara bağlanması ucuzdur |
| **F-95** | Tur bazlı kontrol noktası (F-68 Okuma B) | [Faz 46](46-DAYANIKLI-CALISTIRMA.md) | 🚨 MAF agent düzeyinde kanca **vermiyor** — ölçüldü. Kancayı AgentPrism yazmak K3'ü zorlar. Kanca yalnız `Microsoft.Agents.AI.Workflows` içinde var |
| **F-96** | Kuyruğa alınan çalıştırmalarda ek (attachment) desteği | [Faz 46](46-DAYANIKLI-CALISTIRMA.md) | `AttachmentUriReference` bir HTTP yol öneki ister; bu değer yalnız `MapAgentPrism` çağrısı anında bilinir, `AgentRunJobHandler`'ın DI kayıt anında değil |
| **F-97** | OpenAI uyumlu uçların asenkron sözleşmesi (`background: true`) | [Faz 46](46-DAYANIKLI-CALISTIRMA.md) | Faz 46 `202 Accepted` + `Location` sözleşmesini **yönetim API'sinde** verdi; OpenAI uyumlu yüzeyin kendi sözleşmesi (`response.id` ile yoklama) ayrı bir iştir |
| **F-98** | Azure AI Content Safety adaptörü | [Faz 48](48-GUARDRAILS.md) | Ağırlık **4 paket** (ölçüldü) — sorun değil. Erteleme gerekçesi doğrulanamazlıktır (K-212 emsali) |
| **F-99** | `IVectorSearchStore`'un SQL Server / SQLite uygulaması | [Faz 51](51-VEKTOR-BELLEK-VE-RAG.md) | SQL Server'ın yerel `VECTOR` tipi ve SQLite'ın `sqlite-vec` uzantısı **ölçülmedi** (K-343) |
| **F-100** | Bütçe eşiği uyarısı (proaktif kota alarmı) | 2026-08-08 denetimi | Faz 35 kota ölçerlerini (OTel gauge), Faz 21 giden webhook'u verdi; ikisini bağlayan "eşik aşılınca webhook tetikle" mantığı **yok**. Operatör kota aşımını bugün yalnız gösterge panelinde **görerek** fark ediyor. Var olan iki altyapıyı birleştirir |
| **F-101** | RAG belge tazeliği takibi | 2026-08-08 denetimi | Faz 51 vektör aramayı getirdi ama gömülerin ne zaman bayatladığını izleyen bir mekanizma yok. `document_embeddings`'e `source_updated_at`/`last_indexed_at` karşılaştırması ve isteğe bağlı bir "yeniden indeksle" ucu. **Doğrulanmadı** — planlanmadan önce şema okunmalı |
| **F-102** | `EvalStoreContract` eşzamanlılık testi yük altında kırılgan | 2026-08-08 denetimi | 🚨 **Ölçüldü:** `AddCaseAsync_es_zamanli_terfiler_farkli_seq_uretir` PostgreSQL paketinin tamamı koşarken düştü (`SqlEvalStore.AddCaseAsync:221` — "5 denemede sira numarasi atanamadi"), **tek başına ve ikinci tam koşumda geçti** (870/870). Testin kendisi mi yoksa `AddCaseAsync`'in 5 denemelik yeniden deneme sınırı mı yetersiz — karara bağlanmalı. Bir kusur değil, **kırılgan bir test** olarak sınıflandırıldı ama sessiz bırakılmadı |

> **F-100, F-101 ve F-102 dışındakiler** daha önce devir notlarında yazılıydı;
> bu denetim yalnız numara verdi ve gerekçeleri buraya taşıdı. F-101 **kod
> tabanında doğrulanmamıştır**; plana dönüşmeden önce ölçülmelidir.

---

## Bundan Sonra Ne Kaldı

Üç dalga bittiğinde ekosistem boşluk tablosunun **dokuz satırı** kapanmış olur.
2026-08-08 denetiminden sonra **seçilmemiş 15 kalem** kalır (F-90…F-102 hariç):

| Küme | Kalemler | Ortak yanı |
|---|---|---|
| **Kimlik ve çok kiracılılık** | F-40, F-64, F-65 | 🚨 Zinciri F-56 açıyordu; o artık [Faz 53](53-KIRACI-API-ANAHTARLARI.md) — bu üçünün **önkoşulu planlandı** |
| **İşletim boşlukları** | F-44, F-59, F-45 | Üçü de planlanmış bir fazın **doğrudan devamıdır**. F-36, F-69 ve F-74 [Faz 54–56](54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md) oldu |
| **Uyum ve veri hakları** | F-72 ⏸, F-75, F-58, F-41, F-61 | Kurumsal kapı. F-75 ve F-58 **birlikte** tasarlanmalıdır (silme hakkı ↔ değişmezlik) |

Geriye kalanlar bağımsızdır: F-34 (şablon), F-48 (GitOps), F-50 (istemci+CLI),
F-51 (Aspire), F-67 (performans kapısı).

**Bunu yapmazsak ne olur:** Üç dalga AgentPrism'i Python ve TypeScript
ekosisteminin bugün verdiği yeteneklere ulaştırır. Kalan 20 kalem onu
**çok kiracılı bir SaaS ürününün** altına koyabilecek düzleme taşır; en zayıf
halka bugün **kimliktir** ve dış yüzey açıldıktan sonra en görünür eksik odur.

---

## Faz 7 Hatırlatması

`EnablePublicApiTracking` bugün **`false`**. **Bu listede kalan** ve public
yüzeyi genişleten kalemler:

| Kalem | Yeni public yüzey | Yayından sonra maliyeti |
|---|---|---|
| **F-56** kiracı API anahtarları | Anahtar tipi, kapsam enum'u, depo arayüzü | Yeni tip — ucuz |
| **F-44** model yedek zinciri | 🚨 `ModelBinding`'e bir alan | `sealed record` — sürüm kararı |
| **F-40** BYOK | `ModelBinding` veya kiracı kaydına bir alan | `sealed record` — sürüm kararı |
| **F-50** istemci + CLI | 🚨 **İki yeni paketin tamamı** | En geniş yüzey |
| **F-59** ön uçuş bütçe denetimi | Bir uç, bir ayar | Ucuz |
| **F-61** argüman düzeyinde tool politikası | 🚨 `ToolApprovalRule`'a alan — `record` olduğu için ek kurucu ister | `sealed record` — sürüm kararı |
| **F-75** denetim hash zinciri | Denetim kaydına iki alan | `sealed record` — sürüm kararı |
| **F-30/F-32/F-68 vb.** | — | ✅ Artık **plana dönüştü**; yüzeyleri yol haritasındadır |

Yayından **önce** yapılırlarsa bedavadır. Sonra yapılırlarsa her biri bir
sürüm kararıdır ve `PublicAPI.Shipped.txt` disiplinine girer.

Plana dönüşen yirmi altı kalemin public yüzey listesi
[`UCUNCU-FAZ-YOL-HARITASI.md`](UCUNCU-FAZ-YOL-HARITASI.md)'nin "Faz 7 (Yayın)
Etkisi" bölümündedir; burada tekrarlanmaz.

🚨 **Yayından sonra en pahalı üç değişiklik zaten plana alındı** — üçü de var
olan bir **arayüze metot** ekliyor: [Faz 36](36-SAKLAMA-HACIM-SINIRI.md)
(`IRetentionStore`), [Faz 45](45-URETIMDEN-EVAL-KUMESI.md) (`IEvalStore`) ve
[Faz 52](52-KAYNAK-URETECI.md) (`IAgentPrismBuilder`).

🚨 **Bu listede kalan en pahalı kalem F-61'dir**: `ToolApprovalRule` public bir
`record`'tur ve alan eklemek ek kurucu ister. Faz 7'den önce yapmak bedavadır.

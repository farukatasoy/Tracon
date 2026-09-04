# Keşif Turu — 2026-08-21 · Faza dönüşmesi gereken fikirlerin tespiti

> ## 📦 ARŞİV — tükenmiş tur
>
> 2026-09-04'te taşındı: `docs/kesif/` bütçesi aşıldı (376.792/370.000 B) ve
> **içerik silinmez, taşınır**. Turun ürettiği on altı kalemin tamamı
> yerine ulaştı — hiçbiri öksüz değil (2026-09-04'te tek tek `grep`'lendi):
> F-101 · F-106 · F-109 · F-123 · F-128 · F-130 · F-132 · F-137 · F-138
> [`ADAYLAR.md`](../../ADAYLAR.md)'de · F-122 · F-125 · F-129 · F-133 ·
> F-134 · F-135 · F-136 [`../PLANA-DONUSEN-ADAYLAR.md`](../PLANA-DONUSEN-ADAYLAR.md)'de.
>
> Bundan sonra **yalnız `grep` hedefidir**; durum alanları bayattır.


> Bu bir **koşum kaydıdır**, spec değildir. Sıcak yolda değildir ve baştan sona
> okunmaz. Onaylanan kalemlerin tam metni [`ADAYLAR.md`](../../ADAYLAR.md) içinde
> yaşar; bu dosya yalnız oraya işaret eder.

**Tetikleyen:** Kullanıcı — "faza dönüştürülmesi gereken fikirleri tespit & analiz et".
**Zemin:** Faz 78 kapalı · aday dosyasında 31 seçilmemiş kalem · en büyük numara F-137.
**Taban:** `e45278d` (phase 78).
**Ekosistem taraması:** 2026-08-20 turu (bir gün önce) devralındı; MAF/MEAI/MCP
yüzeyi orada tarandı. Rakip ürün iddiaları (LiteLLM · Portkey · Langfuse) önceki
turların **kendi tarih damgalarıyla** taşındı, bu turda yeniden doğrulanmadı.

---

## 1. Ölçülen zemin (Aşama 0)

| Kaynak | Bulgu |
|---|---|
| `docs/YOL-HARITASI.md` | 78 faz kapandı; Faz 7 (yayın) ⏸ **2026-08-02**'den beri (K-068) |
| `find src -name "PublicAPI.*.txt"` | `Unshipped` **7 772 satır**; 16 paketin `Shipped.txt`'i boş (1 satır) |
| Son dört fazın devir notu (75–78) | F-136 ve F-137 devredildi; F-132 gerekçesiyle ertelendi; iki bütçe kararı kullanıcıya bırakıldı |
| `docs/manuel-test/00-INDEKS.md` §7 | Son tam tur **2026-08-13**; 7 dosya · **114 case** hiç koşulmadı; Faz 61–78 hiçbir tam turda görülmedi |
| Ekosistem boşluk tablosu | 15 satırın **14'ü** plana girdi; kalan iki: F-34 (şablon), F-72 ⏸ (ACS) |
| `ls .github/` | Yalnız `ci.yml`. Bağımlılık sürüklenmesini fark eden **hiçbir mekanizma yok** |
| `docs/guvenlik-tarama/BULGULAR.md` | Egress kalemleri Faz 77'de kapandı; açık kalan tek koruma boşluğu **F-41**'dir |

### 1.1 Numarasız kalemler ölçüldü — üçü de KAPALI

`ADAYLAR.md` § *Dalga 2'den doğan yeni aday kalemler* tablosunda F-numarası
verilmemiş üç iş vardı. Sonraki her planlama turu bunları yeniden keşfetmek
zorunda kalıyordu. Üçü de bu turda ölçüldü ve **hiçbiri açık değil**:

| Numarasız kalem | Ölçüm | Sonuç |
|---|---|---|
| `AgentPrismMcpOptions`'ı `IConfiguration`'a bağlamak | `AgentPrismMcpBuilderExtensions.cs:183` `Bind(IConfiguration, AgentPrismMcpOptions)`; `UseMcp(section, configure)` aşırı yüklemesi var | ✅ Kapalı |
| `BackgroundService` başlatma sırası migration'la yarışır | `src/AgentPrism.Abstractions/Diagnostics/SchemaReadyGate.cs` — kayıt sırasından **bağımsız** hazır sinyali; SQL sağlayıcı yoksa kendiliğinden açılıyor | ✅ Kapalı |
| Çalıştırmanın alt yazmalarında açık kiracı (K-280) | `RunEvent.cs:85` `public string? TenantId { get; init; }`; XML dokümanı "NOT filled from the ambient tenant" diyor (K-355) | ✅ Kapalı |

---

## 2. Ham fikir listesi (Aşama 2)

Bu tur yeni fikir üretmedi; **var olan 31 açık kalemi** kümeledi ve yargıladı.
Gerekçe: ekosistem boşluk tablosunun 14/15 satırı kapandığı için "X'te standart,
.NET'te yok" damarı tükenmiş durumda. Kalan kalemler derinleşme ve kusurdur.

| # | Küme / kalem | Kim için | Neden şimdi | Sonuç |
|---|---|---|---|---|
| 1 | **A** · F-125 + F-129 + F-136 — tüketici yüzeyi kapıları | MAF'ı kullanan ekip, tüketicinin kod agent'ı | Üçü de son üç fazın **kendi denetiminde** bulundu | ✅ derinleşti |
| 2 | **B** · F-45 + F-134 — model boru hattı ergonomisi | İlk agent'ını kuran geliştirici · FinOps | İkisi de pinlenmiş sürümde **ölçüldü**, tek ekleme noktası | ✅ derinleşti |
| 3 | **C** · F-41 + F-87 — kayıt içeriğinin korunması | Kurumsal platform ekibi | Güvenlik taramasının bıraktığı **tek açık koruma boşluğu** | ✅ derinleşti |
| 4 | **E** · F-50 + F-93 — dağıtım kanalı | Dağıtım hattı kuran ekip | Önkoşullar (Faz 40, 52) tamamlandı | ✅ derinleşti |
| 5 | **D** · F-88 + F-89 + F-98 — guardrail devamı | — | — | ❌ elendi (aşağıda) |
| 6 | F-34 şablon · F-72 ACS · F-48 · F-51 · F-67 | — | — | ⏸ bu turda değil |
| 7 | F-90 · F-91 · F-92 | — | Üçü de kapatılmış bir kararla çatışır | ⏸ önce karar |
| 8 | F-94…F-99 · F-101 · F-106 · F-109 · F-123 · F-128 | — | Ölçülmüş tüketici acısına bağlanmadı | ⏸ bu turda değil |

**Önerilen üç kalem ve gerekçesi:** A (ölçülmüş, şekli belli, yeni public tip
yok) · C (kalan tek kurumsal kapı) · B (iki bayrak, tek ekleme noktası).
Faz 7 ayrıca **kanal 3** olarak masaya kondu.

**Kullanıcının elemesi:** dört küme de seçildi (A, B, C, E); sıra **A → B → C → E**.
Faz 7 **ertelenmeye devam** eder. Küme D önerildiği gibi düşürüldü.

---

## 3. Ekosistem taraması (Aşama 3.2)

| Kaynak | Bakılan tarih | Ne değişti | AgentPrism'e etkisi |
|---|---|---|---|
| `Microsoft.Extensions.AI` 10.8.3 (pinli) | **2026-08-21** (reflection) | `DistributedCachingChatClient`, `CachingChatClient`, `FunctionInvokingChatClient` imzaları çıkarıldı | Küme B'nin tamamı pinlenmiş sürümde **var**; sürüm yükseltmesi gerekmiyor |
| MAF 1.16.0 (pinli) · 1.18.0 yayında | 2026-08-20 (devralındı) | 1.18.0 agent seviyesinde eşzamanlı tool çağrısını öne çıkardı | F-134'ün MEAI yarısı bugün mümkün; **agent-seviyesi harness entegrasyonu doğrulanmadı** |
| `ModelContextProtocol.Core` 2.0.0 (pinli) · 2.2.0 yayında | 2026-08-20 (devralındı) | `IdentityAssertionGrantProvider` iki sürümde de aynı | Kanal 2 (sürüm yükseltmesi) |
| LiteLLM · Portkey (yanıt önbelleği) | önceki turların damgası, **bu turda doğrulanmadı** | — | F-45'in ekosistem gerekçesi taşındı, yenilenmedi |
| LiteLLM · Langfuse (CLI) | önceki turların damgası, **bu turda doğrulanmadı** | — | F-50'nin ekosistem gerekçesi taşındı, yenilenmedi |

---

## 4. Derinleşen kalemler (Aşama 3)

Tam gövdeler [`ADAYLAR.md`](../../ADAYLAR.md)'dedir. Burada yalnız bu turda
**yeni ölçülen** kanıt ve her kalemin sonucu durur.

### Küme A — F-125 · `<example>` derleme kapısı

**Kanıt seviyesi:** Ölçüldü. 49 `<example>` bloğu; **2**'si elipsis (`...`)
taşıyor (`AgentPrismToolAttribute.cs`, `AgentPrismMcpServerBuilderExtensions.cs`);
prelüd yer tutucusu **iki**: `app` (6 blok) ve `agentPrism` (1 blok). `options`,
`o`, `context`, `services` blok içinde **lambda parametresi olarak bağlanıyor**,
prelüd istemiyor. `capability-example-baseline.txt` **boş** (0 muafiyet).
**Mercek:** 1, 5. **Eleyici sınır:** yok — test projesi, public yüzey büyümüyor.
**Karşı görüş:** bir kapı işidir, tüketiciye yeni yetenek vermez.
**Sonuç:** aday dosyasında güncellendi; adayın "ölçülmedi" satırı kapandı.

### Küme A — F-129 · 🚨 İddiası ölçülüp YANLIŞ çıktı, kalem daraldı

**Kanıt seviyesi:** Ölçüldü. Kaydın iddiası şuydu: *"`capabilities.md`'yi kodla
karşılaştıran hiçbir kapı yoktur."* **Yanlış.**
`tests/AgentPrism.Core.UnitTests/Architecture/CapabilityCoverageTests.cs`
`CapabilityEntryPoints.Names`'i `PublicAPI` dosyalarından okuyup her giriş
noktasının haritada **adlandırıldığını** kanıtlıyor; `CapabilityExampleTests` de
her giriş noktasının bir örnek gösterdiğini. **53** giriş noktası, **iki taban
çizgisi de boş** — yani sıfır muafiyet.
Ayakta kalan iki dar boşluk: (1) `scripts/dokuman-bakim.py` içinde
`capabilities.md` **sıfır kez** geçiyor, yani `src/AgentPrism.Core/buildTransitive/`
değişimi hiçbir sayfaya eşlenmiyor (giriş noktası değil, bu yüzden var olan kapı
da görmüyor); (2) kalan dokuz `SITE_KURALLARI` deseni "değişti"yi "doğru" sayıyor.
**Mercek:** 1. **Karşı görüş:** kalan boşluk, var olan kapının yanında **küçük**;
tek başına faz değildir.
**Sonuç:** kalem **daraltılarak** aday dosyasında kaldı; yanlış iddia düzeltildi.

### Küme A — F-136 · Ölçüldü, sayı çıktı

**Kanıt seviyesi:** Ölçüldü. `ToolDiagnostics.cs` **7** descriptor
(`APG0001`…`APG0007`), `UsageDiagnostics.cs` **7** descriptor (`APG0101`,
`APG0102`, `APG0201`, `APG0301`, `APG0302`, `APG0401`, `APG0402`) — toplam **14**.
`guides/coding-agents.md` ve `troubleshooting.md` birlikte **9** kod taşıyor.
**Beş kod belgesiz:** `APG0002` · `APG0003` · `APG0004` · `APG0005` · `APG0006`.
Kapının yokluğu **zaten bedel ödetmiş**.
**Mercek:** 1, 5. **Eleyici sınır:** yok.
**Karşı görüş:** Ciddi bir karşı gerekçe bulunamadı — boşluk ölçüldü ve kapı ucuz.
**Sonuç:** aday dosyasında ölçülmüş sayıyla güncellendi; Küme A'nın **en güçlü** kalemi.

### Küme B — F-45 · Yanıt önbelleği

**Kanıt seviyesi:** Ölçüldü (reflection, 2026-08-21).
`DistributedCachingChatClient(IChatClient innerClient, IDistributedCache storage)`
— 🚨 **`IDistributedCache` bugün AgentPrism'de hiç kullanılmıyor**
(`grep -rn "IDistributedCache" src` → boş). Tüketici bir cache uygulaması seçmek
zorunda kalır; bellek içi olan çok örnekli kurulumda **sessizce işe yaramaz**.
`CacheKeyAdditionalValues { get; set; }` (`IReadOnlyList<Object>`) var — kiracı
anahtara **yapısal** olarak karışır, elle string birleştirme gerekmez (K-525 sağlanır).
`GetCacheKey` ve `EnableCaching` `protected virtual` — AgentPrism kiracı
anahtarlamasını yapılandırmaya güvenmek yerine **zorlayabilir**.
**Mercek:** 8. **Eleyici sınır:** yeni geçişli bağımlılık kararı (K1 — sıfır sürpriz).
**Karşı görüş:** `CacheKeyAdditionalValues` **örnek başına** bir özelliktir; kiracı
başına anahtarlama kiracı başına istemci örneği demektir.
**Sonuç:** aday dosyasında güncellendi.

### Küme B — F-134 · 🚨 Gerçek risk ölçüldü: eşzamanlılık + ambient bağlam

**Kanıt seviyesi:** Ölçüldü. `FunctionInvokingChatClient.AllowConcurrentInvocation`
`{ get; set; }` doğrulandı. Asıl bulgu şudur: AgentPrism'in tool katmanı
**ambient** `FunctionInvokingChatClient.CurrentContext`'e bağlıdır —
`AuthorizingAIFunction.cs:107` ve `AgentPrismToolUsage.cs:56`. Bayrak açılınca
tool gövdeleri eşzamanlı koşar ve bu ambient'ın çağrı başına doğru çözülüp
çözülmediği **ölçülmemiştir**. Bu depo aynı sınıf tuzağı **beş kez** yaşadı
(`MEMORY.md`).
Ekleme noktası doğrulandı: `ModelProviderRegistry.cs:317-321` —
`.AsBuilder().UseFunctionInvocation(...).UseOpenTelemetry(...)`.
**Mercek:** 1, 4.
**Karşı görüş:** kendi kaydının karşı görüşü ayakta — hiçbir manuel test veya
kullanıcı bu gecikmeyi bildirmedi; ilk adım benchmark'tır, bayrak değil.
**Sonuç:** aday dosyasında **ambient riski** eklenerek güncellendi.

### Küme C — F-41 + F-87 · Çatışma haritası çıkarıldı

**Kanıt seviyesi:** Ölçüldü. Dokuz sütunu tüketen **üç** okuma yolu adlandırıldı:

| Okuma yolu | Kanıt | Şifreleme / redaksiyon etkisi |
|---|---|---|
| `RunReplayService` | `Replay/RunReplayService.cs:30` `IRunInputStore _inputs` | Replay **ham** girdiyi ister; redakte edilirse yeniden oynatma sadık değildir |
| `RunToCasePromoter` | `Evaluation/RunToCasePromoter.cs:175` `_runs.ReadEventsAsync`, `:97` `ListToolInvocationsAsync` | Eval vakası `run_events` ve `tool_invocations` ham metninden üretilir |
| Dosya araması | `Sql.Shared/Stores/SqlAgentFileStore.cs:212` `CollectMatches(Regex regex, string content)` | Regex **düz metin** üzerinde koşar; şifreleme dosya aramasını tamamen bitirir |

Ayrıca `AgentPrismServiceCollectionExtensions.cs:870` açıkça yazıyor: *"RunStarted
event and IRunInputStore never passes through the guards."* — F-87'nin öncülü
koddan doğrulandı.
**Mercek:** 3. **Eleyici sınır:** public yüzey (`IContentProtector`, yeni tip — ucuz).
**Karşı görüş:** genişleme noktası varsayılansız gelirse (K4) bugün hiçbir tüketici
korunmaz; Faz 73'ün "opt-in kararının bedeli benimseme oranıdır" cümlesi geçerli.
**Sonuç:** aday dosyasında **tek küme** olarak birleştirildi ve çatışma haritası eklendi.

### Küme E — F-50 + F-93 · İki iddia düzeltildi

**Kanıt seviyesi:** Ölçüldü.
1. **OpenAPI belgesi üretim kaynağı olarak yeterli:** `docs/openapi/agentprism.json`
   — OpenAPI **3.1.1**, **123** yol, **160** operasyon, **250** şema,
   `operationId` eksik operasyon **0**.
2. 🚨 **F-50'nin "ikinci bir üreteç projesi açılmamalıdır" kısıtı KONUSUZ.**
   `AgentPrism.Client`'ın kaynak üretilmiş JSON'u `JsonSerializerContext`'tir —
   **derleyicinin kendi** üreteci. Depo bunu zaten on'dan fazla yerde kullanıyor
   (`AgentPrismCoreJsonContext.cs`, `AgentPrismJsonContext.cs`, …).
   `AgentPrism.Generators` bir Roslyn analyzer projesidir (`netstandard2.0`,
   `EnforceExtendedAnalyzerRules`) ve bu işe hiç karışmaz.
3. **npm gerçekten yeni bir kanaldır:** `ci.yml:154` `publish` işi yalnız
   **NuGet.org**'a yayınlar, `v*` etiketiyle tetiklenir, `nuget` environment'ı
   kullanır. npm registry veya kimlik bilgisi yok.
**Mercek:** 1, 3. **Eleyici sınır:** üç yeni dağıtım yüzeyi → **K-007 gerekçesi**; AOT.
**Karşı görüş:** yayın ertelendiği için yüzey büyütmek bugün **ucuzdur**, ama bu
kaleme *değer* kazandırmaz. Bugün hiçbir tüketici yönetim API'sini kod içinden
çağıramadığı için engellenmiş değildir; F-135'in aksine bu kalem **ölçülmüş bir
tüketici acısına** bağlı değil.
**Sonuç:** aday dosyasında iki düzeltmeyle güncellendi.

---

## 5. Üç kanalın çıktısı (Aşama 1)

### Kanal 1 — yeni aday

Bu tur **yeni F-numarası üretmedi.** En büyük numara F-137'de kaldı. Var olan
sekiz kalem dört kümeye toplandı ve gövdeleri ölçümle güncellendi.

| Küme | Kalemler | Aday dosyasına yazıldı mı |
|---|---|---|
| A | F-125 · F-129 · F-136 | ✅ Evet (F-129 **daraltıldı**) |
| B | F-45 · F-134 | ✅ Evet |
| C | F-41 · F-87 | ✅ Evet (tek küme olarak birleştirildi) |
| E | F-50 · F-93 | ✅ Evet |

### Kanal 2 — kusur

| Bulgu | Kanıt | Kullanıcıya söylendi mi | `kusur-giderme` koşuldu mu |
|---|---|---|---|
| Dört bozuk test (F-122, F-130, F-133, F-137) | Faz 77/78 kapanış koşumları; F-133 izolasyonda 3/3 düşüyor, F-137 bu çalışma kopyasında 5/5 | ✅ Evet | ✅ **Koşuldu (2026-08-21).** 🚨 **Öncül tutmadı:** dört tam koşum dört FARKLI düşüş kümesi verdi ve kayıtlı dördü hiçbirinde düşmedi. F-133'ün kök sebebi bulundu ve **kapandı** (K-541); koşum 4'ün SQL Server düşüşü ayrı bir ürün kusuru çıktı ve **kapandı** (K-540). Yeni kırılgan: **F-138** |
| **🆕 Yinelenen karar numarası:** K-535 ve K-536 **ikişer kez**, farklı içerikle | `KARARLAR-INDEKS.md:123-126`; `KARARLAR.md` gövdesinde de yineleniyor (538 karar satırı). Faz 77 ve Faz 78 aynı tabandan yazıldı ve çarpıştı | ✅ Evet | ✅ **Kapandı (2026-08-21, K-539).** Sınıf taraması iki vaka daha buldu: tabloyu kesen **iki** boş satır ve **iki** sıra dışı numara. Faz 78'inkiler K-537/K-538'e taşındı; kapı `dokuman-bakim.py --denetle`'ye eklendi |
| **🆕 Sürüklenmeyi fark eden mekanizma yok** | `.github/` yalnız `ci.yml` taşır; dependabot/renovate yapılandırması yok | ✅ Evet | ✅ **Kapandı (2026-08-21, K-543).** `.github/dependabot.yml` kuruldu: NuGet (CPM) + iki npm dizini + GitHub Actions, haftalık. Gruplar sürüm hattı kısıtını korur (MAF GA/preview/alpha tek PR — K-008; MCP `.Core`+`.AspNetCore` tek PR — K-334). Belgelenmiş **beş** sabit `ignore` listesinde, her biri yalnız kendi kırılgan sürüm aralığıyla. Renovate reddedildi (GitHub App + repo dışı servis), CI içi kendi kapımız reddedildi (PR açmaz, taban çizgisi elle güncellenir) |
| MAF 1.16→1.18 · MEAI 10.8.3→10.9.0 · MCP.Core 2.0.0→2.2.0 | `Directory.Packages.props:19,156`; keşif notu 2026-08-20 | ✅ Evet | ✅ **Yapıldı (2026-08-21).** Üç aile yükseltildi (preview/alpha hattı da 1.18.0'a); **MEAI 10.9.0 `Microsoft.Extensions.*` tabanını 10.0.11'e ZORLADI** — 10.0.10'da restore NU1605 verdi (K-544). Dört kapı sıfır uyarı: 4 592 test, 0 düşük, 0 atlanan · 68 paket. Tam yüzey diff'i alındı: **kaldırma sıfır**, altı üye ve yedi tip eklendi; ikisi plan etkiledi — `ChatClientAgentOptions.AllowConcurrentInvocation` (Faz 81 §81.5) ve MEAI'nin `RoutingChatClient` ailesi. Kalan **30+** paketin sürüklenmesi yükseltilmedi; kapı onları haftalık bildirecek |

### Kanal 3 — yeniden açılması önerilen karar

| K-NNN | Kararın gerekçesi | Neyin değiştiği | Kullanıcının kararı |
|---|---|---|---|
| **K-068** | Yayın zamanı belirlenmedi (2026-08-02) | 76 faz geçti; `Unshipped` **7 772** satıra çıktı ve her faz büyütüyor | 🚫 **Ertelenmeye devam** (2026-08-21) |
| **K-053** | Harness+streaming tool çağrısı kırıktı, ön sürümde | Harness GA hattında (1.16.0, sonek yok); koşulun yarısı sağlandı, **davranış ölçülmedi** | ⏳ Bekleniyor |
| **F-132** | Egress muhafızı adres bazlıdır, şema kısıtı yok | Değişmedi — varsayılan kararı hâlâ ölçüm ister | ⏳ Bekleniyor |

### Skill koşumu (kanal değil)

Manuel kabul setinin son tam turu **2026-08-13**'tür; o günden sonra 18 faz
kapandı. 7 dosya · 114 case hiç koşulmadı. Kullanıcı tam turu onayladı —
`manuel-test-kosumu` kendi oturum dizisidir.

---

## 6. Reddedilenler

| Fikir | Ret gerekçesi | Kalıcı mı | Nereye yazıldı |
|---|---|---|---|
| **Küme D** — F-88 + F-89 + F-98 guardrail devamı | F-88 tek başına faz değil; F-89 yayından sonra pahalı bir **arayüz** değişimidir ve kuralların nerede yaşadığı sorusunu açar; F-98'in erteleme gerekçesi (doğrulanamazlık, K-212 emsali) değişmedi | Bu turda — kalemler `ADAYLAR.md`'de **kalır** | Yalnız bu keşif notunda |
| Üç numarasız Dalga-2 kalemini aday listesine almak | Üçü de ölçüldü ve **kapalı** çıktı (§1.1) | Kalıcı — konu kapalı | `ADAYLAR.md` (tablodan düşüldü) |
| F-90 RLS · F-91 MCP OAuth · F-92 dağıtık hız sınırı | Üçü de kapatılmış bir kararla çatışır (sağlayıcı ayrışması · K-059 · K-158 + Redis reddi). Önce karar açılmalı | Bu turda | Yalnız bu keşif notunda |
| F-34 şablon · F-48 GitOps · F-51 Aspire · F-67 performans kapısı | Hiçbiri bugün ölçülmüş bir tüketici acısına bağlı değil. F-34 ayrıca bir **güvenlik yüzeyidir** (K2'yi zorlar) | Bu turda | Yalnız bu keşif notunda |
| F-72 ACS uyumu | .NET paketi hâlâ beta ve native (beş RID); erteleme koşulu değişmedi | Bu turda | Yalnız bu keşif notunda |
| Faz 7'yi bu turda açmak | Kullanıcı kararı: ertelenmeye devam | Bu turda | Kanal 3 tablosu |

---

## 7. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap |
|---|---|
| Faz 7 (yayın) bu turda masaya gelsin mi? | **Ertelenmeye devam etsin** |
| Hangi kümeler Aşama 3'e girsin? | **A, B, C, E** — dördü de |
| Küme sırası ne olsun? | **A → B → C → E** |
| Bu oturumun sınırı nerede? | **ADAYLAR.md'de bitsin** — faz dokümanı `faz-planlama`'nın işi |
| Kanal 2 bulguları için ne yapılsın? | **Dördü de** — bozuk testler · yinelenen K numarası · sürüm yükseltmeleri · tam manuel tur |

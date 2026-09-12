# Keşif Turu — 2026-08-20 · MAF / Semantic Kernel / Microsoft.Extensions.AI ekosistem taraması

> ## 📦 ARŞİV — tükenmiş tur
>
> 2026-09-04'te taşındı: `docs/kesif/` bütçesi aşıldı (376.792/370.000 B) ve
> **içerik silinmez, taşınır**. Bu tur bir **ekosistem taramasıdır** ve
> ekosistem taramaları tarih damgalıdır: 2026-08-26'nın iki turu
> ([`../../kesif/2026-08-26-yeni-feature-fikirleri.md`](../../kesif/2026-08-26-yeni-feature-fikirleri.md))
> MAF 1.19.0 dahil daha yeni bir yüzey taradı ve bunu geçersizleştirdi.
> Ürettiği kalemler: F-45 · F-133 · F-134 — durumları
> [`ADAYLAR.md`](../../ADAYLAR.md) § *Aday Olmayan Açık Kayıtlar*'dadır.
>
> Bundan sonra **yalnız `grep` hedefidir**; durum alanları bayattır.


> Bu bir **koşum kaydıdır**, spec değildir. Sıcak yolda değildir ve baştan sona
> okunmaz. Onaylanan kalemlerin tam metni
> [`ADAYLAR.md`](../../ADAYLAR.md) içinde yaşar; bu dosya
> yalnız oraya işaret eder.

**Tetikleyen:** Kullanıcı, projenin geliştirilmesinden bu yana MAF, Semantic
Kernel ve Microsoft.Extensions.AI'da çıkan yeni sürümlere göre optimize
edilebilecek işlev veya geliştirilmesi gereken yeni özellik olup olmadığının
kapsamlı analizini istedi.
**Zemin:** Faz 77 kapalı · aday dosyasında 2026-08-18 turundan kalan az sayıda
seçilmemiş kalem · en büyük numara F-133 (kusur kalemi, bu turdan önce).
**Ekosistem taraması:** 2026-08-20 · web erişimi var.

---

## 1. Ölçülen zemin (Aşama 0)

| Kaynak | Bulgu |
|---|---|
| Son üç fazın devir notu (75, 76, 77) | Dokümantasyon/güvenlik odaklı, MAF sürümüyle ilgisiz. Faz 77'nin "zorlama noktasını sür" dersi bu turda da uygulandı — bkz. §4 K-053 |
| `docs/ADAYLAR.md` § Ekosistem Boşluk Tablosu | On beş boşluğun on dördü zaten plana dönüşmüş; MAF/SK sürüm takibi bu tabloda ayrı bir satır olarak yok |
| `docs/hafiza/maf-api.md` | En zengin damar: K-053 (Harness+streaming tool kırık), K-129 (Declarative reddi), Foundry sürüm sürüklenmesi, MCP OAuth kısıtı — hepsi bu turda yeniden ölçüldü |
| `docs/manuel-test/00-INDEKS.md` | Bu tur taranmadı — konu (paket sürümleri) manuel test kapsamının dışında |

---

## 2. Ham fikir listesi (Aşama 2)

| # | Fikir | Kim için | Neden şimdi | Sonuç |
|---|---|---|---|---|
| 1 | MAF sürüm yükseltmesi 1.16.0→1.18.0 | Herkes | İki minor sürüm gerisinde | ✅ derinleşti → Kanal 2 |
| 2 | Microsoft.Extensions.AI 10.8.3→10.9.0 | Herkes | Rutin bakım | ✅ derinleşti → Kanal 2 |
| 3 | ModelContextProtocol.Core 2.0.0→2.2.0 / MCP OAuth boşluğu | MCP kullanan ekip | Faz 22 kısıtı kapanmış olabilir | ✅ derinleşti → belge düzeltmesi |
| 4 | K-053 yeniden test | Nöbetçi mühendis | Harness artık GA hattında | ✅ derinleşti → Kanal 3 |
| 5 | `Microsoft.Agents.AI.Foundry` sürüm hizalama | Foundry kullanan kurumsal ekip | Sürüm sürüklenmesi notu var | ❌ elendi — hâlâ ön sürüm, K-212 değişmedi |
| 6 | F-45 Yanıt önbelleği doğrulama | FinOps | Aday dosyasında asılı "doğrulanmadı" notu | ✅ derinleşti → F-45 güncellendi |
| 7 | AgentSkillsProvider/BackgroundAgentsProvider vs. resmi Harness middleware | Kod azaltma | Blog bunları "yeni" diye tanıtıyor | ❌ elendi — zaten kullanılıyor, blog iddiası yanlış çıktı |
| 8 | MAF eşzamanlı tool çağrısı | Performans | 1.18 notu bunu öne çıkardı | ✅ derinleşti → F-134 |
| 9 | Semantic Kernel bakım modu teyidi | Dokümantasyon | MAF/SK birleşmesi resmî | ⏸ ertelendi — yalnız teyit, aksiyon yok |
| 10 | CodeAct (alpha) | Düşük öncelik | Python/Hyperlight odaklı | ❌ elendi — .NET'e gelmedi, izlemeye bile gerek yok |
| 11 | GitHub Copilot SDK entegrasyonu | Kapsam belirsiz | BUILD 2026 duyurusu | ❌ elendi — Tracon'in kapsamı dışı (kod-odaklı agent, kütüphane değil) |
| 12 | Handoff orchestration resmi MAF deseni | Teyit | Zaten `CreateHandoffBuilderWith` var mı | ⏸ ertelendi — kullanıcı elemedi, ayrı tur gerektirir |
| 13 | `Workflows.Declarative` sürüm kontrolü | K-129 teyidi | Sürüm hizalanmış olabilir | ✅ derinleşti — K-129 teyit edildi, kalem değil |
| 14 | Cosmos vektör bellek eklentileri | RAG ekibi | 1.18 notu | ⏸ ertelendi — kullanıcı elemedi |
| 15 | `LocalEvaluator`/`EvalItem` gelişmeleri | Eval ekibi | — | ⏸ ertelendi — kullanıcı elemedi |
| 16 | MEAI reasoning-token yetenekleri | Token kırılımı | 10.3+ notu | ⏸ ertelendi — kullanıcı elemedi |

**Önerilen üç kalem ve gerekçesi:** #4 (K-053 — somut, ölçülebilir, gece-03:00
acısı), #3 (MCP OAuth — bilinen kısıt ucuz doğrulanır), #6 (F-45 — tek eksiği
doğrulama olan asılı kalem).

**Kullanıcının elemesi:** Dört grup seçildi: sürüm yükseltmeleri (#1, #2, #5,
#13), F-45 (#6), MCP OAuth (#3), K-053 (#4).

---

## 3. Ekosistem taraması (Aşama 3.2)

| Kaynak | Bakılan tarih | Ne değişti | Tracon'e etkisi |
|---|---|---|---|
| NuGet `microsoft.agents.ai` | 2026-08-20 | Son stabil 1.18.0 (repo 1.16.0) | İki minor sürüm gerisinde — kusur kanalı |
| NuGet `microsoft.extensions.ai` | 2026-08-20 | Son stabil 10.9.0 (repo 10.8.3) | Küçük fark — kusur kanalı |
| NuGet `modelcontextprotocol.core` | 2026-08-20 | Son stabil 2.2.0 (repo 2.0.0) | `IdentityAssertionGrantProvider` HER İKİ sürümde de var — yükseltme bu boşluğu kapatmıyor |
| NuGet `microsoft.agents.ai.foundry` | 2026-08-20 | En yüksek sürüm hâlâ `1.18.0-preview.260818.1` — asla stabile çıkmamış | K-212'nin ret gerekçesi (doğrulanamazlık) değişmedi |
| NuGet `microsoft.agents.ai.workflows.declarative` 1.18.0 nuspec | 2026-08-20 | Artık tam olarak `Microsoft.Agents.AI.Workflows 1.18.0`'a bağımlı (önceden 1.13.x'te kalmıştı) — sürüm skewü kapandı | K-129'un İKİNCİ gerekçesi (+19 geçişli paket: `Microsoft.Agents.ObjectModel*`, `PowerFx.Interpreter`, `System.CodeDom`, `VectorData.Abstractions` vb.) somut listeyle DOĞRULANDI — karar değişmiyor |
| `github.com/microsoft/agent-framework` sürüm 1.0 GA duyurusu | 2026-08-20 | MAF 2026-04-02'de 1.0 GA'ya çıktı; AutoGen + Semantic Kernel'i birleştirdi | K-341 (SK connector reddi) doğrulandı, değişmiyor |
| `github.com/microsoft/agent-framework` — Semantic Kernel destek durumu | 2026-08-20 | SK bakım moduna alındı; yeni özellik yatırımı yalnız MAF'a gidiyor, SK en az GA'dan bir yıl kritik yama alacak | Aksiyon gerektirmiyor — mevcut duruşu güçlendiriyor |
| `dotnet-1.17.0`/`dotnet-1.18.0` release notes | 2026-08-20 | "Allow agents to opt into concurrent tool invocation" (1.18.0); Harness+streaming tool kırıklığına dair AÇIK bir "fixed" notu YOK | F-134'ün kaynağı; K-053'ün davranışsal düzelmesi kanıtlanmadı |
| Reflection: `Microsoft.Agents.AI.Harness` 1.16.0 (pinlenmiş, GA) | 2026-08-20 | `OpenTelemetryAgent`, `ToolApprovalAgentOptions`/`UseToolApproval` tipleri zaten pinlenmiş sürümde mevcut | Tracon'in `OpenTelemetryAgentDecorator`/`ToolApprovalAgentDecorator`'ı bunları ZATEN SARIYOR (K-055, kaynak dosya doğrulandı) — "yinelenen kod" hipotezi ÇÜRÜTÜLDÜ |
| Reflection: `ModelContextProtocol.Core` 2.0.0 vs 2.2.0 (ikisi de indirilip karşılaştırıldı) | 2026-08-20 | `IdentityAssertionGrantProvider` iki sürümde de birebir aynı sembollerle var | `docs/hafiza/maf-api.md:51`'in "etkileşimsiz akış YOKTUR" notu netleştirilmeli — IAG "var olan bir IdP kimliğinin token değişimi"dir, saf `client_credentials` (kimliksiz servis-servis) DEĞİL. Not yanlış değil ama eksik |
| Reflection: `Microsoft.Extensions.AI` 10.8.3 (pinlenmiş) | 2026-08-20 | `DistributedCachingChatClient`, `AllowConcurrentInvocation` ikisi de mevcut | F-45'i kapattı, F-134'ü doğdurdu |

**Kaynaklar:**
[Microsoft Agent Framework 1.0 GA (VS Magazine)](https://visualstudiomagazine.com/articles/2026/04/06/microsoft-ships-production-ready-agent-framework-1-0-for-net-and-python.aspx) ·
[Agent Framework at Build 2026](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-at-build-2026-announce/) ·
[Agent Framework Harness/Hosted Agents GA (InfoQ)](https://www.infoq.com/news/2026/08/agent-framework-harness-ga/) ·
[dotnet-1.17.0 release](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.17.0) ·
[dotnet-1.18.0 release](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.18.0) ·
[MCP OAuth docs (DeepWiki)](https://deepwiki.com/modelcontextprotocol/csharp-sdk/3.8-oauth-authentication-and-authorization)

---

## 4. Derinleşen kalemler (Aşama 3)

### F-45 doğrulaması (aday dosyasında zaten vardı)

**Kanıt seviyesi:** Ölçüldü — `strings -a ~/.nuget/packages/microsoft.extensions.ai/10.8.3/lib/net10.0/Microsoft.Extensions.AI.dll` çıktısında `Microsoft.Extensions.AI.DistributedCachingChatClient` ve `DistributedCachingChatClientBuilderExtensions` sembolleri.
**Mercek:** 8 (zaten aday dosyasında).
**Eleyici sınır kontrolü:** Yeni paket yok (mevcut bağımlılığın içinde). AOT: doğrulanmadı, ayrı adım.
**Karşı görüş:** Aday dosyasındaki mevcut satır korunuyor (varsayılan kapalı olmalı).
**Sonuç:** ADAYLAR.md'deki mevcut F-45 bölümü güncellendi, yeni numara açılmadı.

### F-134 · Eşzamanlı tool çağrısını açığa çıkar

**Kanıt seviyesi:** Ölçüldü — reflection (`AllowConcurrentInvocation` sembolü MEAI 10.8.3'te) + kod (`ToolUsageAccumulator.cs:18` yorumu, ayar hiçbir yerde `true` değil) + tarih damgalı ekosistem kaynağı (dotnet-1.18.0 release notes, 2026-08-18).
**Mercek:** 1, 4.
**Eleyici sınır kontrolü:** K2/K3 ihlali yok — mevcut `ChatClientBuilder` boru hattına bir bayrak. Yeni paket yok. AOT: `FunctionInvokingChatClient` zaten AOT-temiz yolda (`docs/hafiza/maf-api.md` satır 18); doğrulama gerekir ama risk düşük.
**Karşı görüş:** Ölçülmüş bir talep yok — hiçbir manuel test veya kullanıcı bu gecikmeyi şikayet etmedi. İlk adım özellik eklemek değil, gerçek bir çok-tool senaryosunda ölçüm almak.
**Sonuç:** F-134 olarak aday dosyasına yazıldı.

### AgentSkillsProvider/BackgroundAgentsProvider "yinelenmesi" hipotezi

**Kanıt seviyesi:** Ölçüldü — `grep -rn "class AgentSkillsProvider\|class BackgroundAgentsProvider" src` boş döndü (özel sınıf yok); ama `grep -rn "AgentSkillsProvider\|BackgroundAgentsProvider" src` MAF'ın KENDİ tiplerinin `AgentDefinitionCompiler.cs` içinde doğrudan kullanıldığını gösterdi (satır 1149, 1314-1315).
**Mercek:** —
**Eleyici sınır kontrolü:** —
**Karşı görüş:** —
**Sonuç:** Aday dosyasına yazılmadı — hipotez yanlıştı, MAF'ın resmi tipleri zaten K-097 kararıyla kullanılıyor.

### OpenTelemetryAgent/ToolApprovalAgent "yinelenmesi" hipotezi

**Kanıt seviyesi:** Ölçüldü — Harness 1.16.0 reflection'ı `OpenTelemetryAgent` ve `ToolApprovalAgentOptions`/`UseToolApproval` sembollerini gösterdi; ama `docs/KARARLAR.md` K-055 ve `ToolApprovalAgentDecorator.cs` kaynağı bu tiplerin **zaten** Tracon'in `IAgentDecorator` sarmalayıcıları (K-024) İÇİNDE kullanıldığını gösterdi — `OpenTelemetryAgentDecorator` MAF'ın `OpenTelemetryAgent`'ını tek dosyada `MAAI001` bastırarak sarıyor.
**Mercek:** —
**Eleyici sınır kontrolü:** —
**Karşı görüş:** —
**Sonuç:** Aday dosyasına yazılmadı — hipotez yanlıştı. Blog'un "yeni" diye tanıttığı tipler zaten 1.16.0'da vardı ve Tracon zaten sarıyordu.

### K-053 yeniden açma önerisi

**Kanıt seviyesi:** Ölçüldü (kısmi) — `Directory.Packages.props` satır 26'da `Microsoft.Agents.AI.Harness`'ın artık GA hattında (`$(MicrosoftAgentsAIVersion)=1.16.0`, `-preview` soneksiz) olduğu doğrulandı; K-053'ün yeniden açılma koşulu ("Harness GA olduğunda VEYA senaryo yeniden test edilip düzeldiği doğrulandığında") birinci yarısı sağlandı. **Ölçülmedi:** asıl davranış (tool çağrısının Harness+streaming'de gerçekten çalışıp çalışmadığı) — 1.17.0 ve 1.18.0 değişiklik günlükleri bu spesifik kırıklığa dair açık bir "fixed" ibaresi taşımıyor; GitHub arama da doğrudan eşleşme bulamadı.
**Mercek:** 2 (üretim işletimi).
**Eleyici sınır kontrolü:** —
**Karşı görüş:** GA olmak otomatik olarak "düzeldi" anlamına gelmez — sürüm numarası bir davranış kanıtı değildir. Gerçek repro (Playground'da `arastirmaci` örneği, Harness + tool) koşulmadan karar kapatılmamalı.
**Sonuç:** ADAYLAR.md'ye YAZILMADI (bu bir aday değil, karar kanalı). Kullanıcıya bildirildi; kullanıcı isterse `kusur-giderme` ile gerçek bir repro koşulup K-053 güncellenebilir.

### MCP OAuth notu netleştirme

**Kanıt seviyesi:** Ölçüldü — `ModelContextProtocol.Core` 2.0.0 VE 2.2.0 ikisi de indirilip `strings` ile karşılaştırıldı; `IdentityAssertionGrantProvider` iki sürümde de birebir aynı sembollerle mevcut (yeni değil). DeepWiki dokümantasyonu IAG'ın "var olan bir IdP kimlik token'ının değişimi" olduğunu, saf `client_credentials`'ın hâlâ desteklenmediğini teyit ediyor.
**Mercek:** —
**Eleyici sınır kontrolü:** —
**Karşı görüş:** Faz 22'nin notu teknik olarak YANLIŞ değil — aradığı şey saf client_credentials'dı ve o hâlâ yok. Ama not IAG'ın varlığından hiç bahsetmiyor; bir sonraki oturum "hiçbir etkileşimsiz seçenek yok" okuyup IAG'ı gözden kaçırabilir.
**Sonuç:** ADAYLAR.md'ye yazılmadı (yetenek adayı değil, belge doğruluğu). Kullanıcıya bildirildi.

---

## 5. Üç kanalın çıktısı (Aşama 1)

### Kanal 1 — yeni aday

| F-NN | Başlık | Aday dosyasına yazıldı mı |
|---|---|---|
| F-134 | Eşzamanlı tool çağrısını açığa çıkar | ✅ Evet |

### Kanal 2 — kusur

| Bulgu | Kanıt | Kullanıcıya söylendi mi | `kusur-giderme` koşuldu mu |
|---|---|---|---|
| MAF paket ailesi 1.16.0→1.18.0 gerisinde | NuGet sürüm karşılaştırması, 2026-08-20 | ✅ Evet | ⏳ Kullanıcı onayı bekliyor |
| Microsoft.Extensions.AI 10.8.3→10.9.0 gerisinde | NuGet sürüm karşılaştırması, 2026-08-20 | ✅ Evet | ⏳ Kullanıcı onayı bekliyor |

### Kanal 3 — yeniden açılması önerilen karar

| K-NNN | Kararın gerekçesi | Neyin değiştiği | Kullanıcının kararı |
|---|---|---|---|
| K-053 | Harness+streaming tool çağrısı kırıktı, ön sürümde | Harness artık GA (1.16.0, sonek yok) — koşulun yarısı sağlandı, davranış ölçülmedi | ⏳ Bekleniyor |

---

## 6. Reddedilenler

| Fikir | Ret gerekçesi | Kalıcı mı | Nereye yazıldı |
|---|---|---|---|
| `Microsoft.Agents.AI.Foundry` sürüm hizalama | Paket asla stabile çıkmamış (`1.18.0-preview.260818.1`); K-212'nin ret gerekçesi (doğrulanamazlık) hâlâ geçerli | Bu turda — kalıcı değil, paket GA olursa yeniden bakılır | Yalnız bu keşif notunda |
| `Workflows.Declarative` sürüm skewü giderildi diye alım önerisi | Sürüm skewü kapandı ama +19 geçişli paket gerekçesi somut listeyle doğrulandı; K-129 değişmiyor | Bu turda — kalıcı değil | Yalnız bu keşif notunda |
| AgentSkillsProvider/BackgroundAgentsProvider'ı Harness'a taşıma | Zaten `AIContextProvider` yoluyla (K-097) kullanılıyor; taşımanın hiçbir gerekçesi yok | Kalıcı değil — konu kapalı | Yalnız bu keşif notunda |
| OpenTelemetryAgent/ToolApprovalAgent "yinelenmiş kod" | Hipotez çürütüldü — zaten sarılıyor (K-055) | Kalıcı değil — konu kapalı | Yalnız bu keşif notunda |
| CodeAct (alpha) izlemeye alma | Python/Hyperlight odaklı, .NET yüzeyi yok, izlemeye bile değmez | Bu turda | Yalnız bu keşif notunda |
| GitHub Copilot SDK entegrasyonu | Kod-odaklı agent senaryosu; Tracon bir kütüphane kontrol düzlemi, IDE/kod ajanı değil | Bu turda | Yalnız bu keşif notunda |

---

## 7. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap |
|---|---|
| Bu analiz için nasıl ilerleyelim? | `aday-kesfi` skill'i (önerilen seçenek) |
| Ham fikir turundan hangileri derinleştirilsin? | Sürüm yükseltmeleri (#1,#2,#5,#13) · F-45 (#6) · MCP OAuth (#3) · K-053 (#4) — dördü de seçildi |

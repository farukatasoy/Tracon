# Keşif Turu — 2026-10-28 · Stratejik raporun ölçümü ve RC kalite dalgası

> Bu bir **koşum kaydıdır**, spec değildir. Sıcak yolda değildir ve baştan sona
> okunmaz. Turun ürettiği kalemlerin tam metni
> [`RC-KALITE-DALGALARI.md`](../RC-KALITE-DALGALARI.md) içinde yaşar (yerel
> `Q-NN` kimliği); bu dosya yalnız oraya işaret eder.

**Tetikleyen:** Bakımcı: "rc'ye doğru giderken ürünümü daha kaliteleştirmek için
yeni bir faz dalgası başlatmak istiyorum" — girdi, başka bir oturumun yazdığı
"Tracon Stratejik Değerlendirme Raporu"dur (2026-10-26, `af816fc6`). İstek:
raporun ayrıntılı ölçümü ve dalga dokümanı.
**Raporun hedef cümlesi:** "MAF'ın her özelliği Tracon altında çalışsın,
yönetilsin, kaydedilsin, karşılık bulsun, iş kolaylaştırsın, basitleştirsin,
esnekliğiyle sistemlere entegre edilebilsin."
**Zemin:** HEAD `8295ca9c` · Faz 254 kapalı · açık faz yalnız 193 · sıradaki faz
255 · en büyük F-ID F-743 (sayaç değişmedi) · rapordan sonra 109 commit girdi.
**Ekosistem taraması:** 2026-10-28 · web erişimi var.
**Yöntem:** üç bağımsız salt okuma taraması (sayılar ve ilk temas · çalışma anı
ve güvenlik · karar defteri ve MAF matrisi) + NuGet flat container ve
registration API, GitHub API, MAF `dotnet-1.26.0` sürüm notu, MCP 2026-07-28
changelog, MEAI 10.8–10.11 derlemelerinde tip adı taraması. Kod koşulmadı;
çalışma anı probu yok. Eski revizyon sayıları `git show af816fc6:<yol>` ile
ölçüldü.

---

## 1. Ölçülen zemin

| Kaynak | Bulgu (2026-10-28) |
|---|---|
| nuget.org `Tracon` | `preview.1`…`preview.6`; `preview.5` ve `preview.6` 2026-10-27'de yayımlandı (registration API `published`). Toplam indirme 516 (`preview.4`: 69) |
| nuget.org `Tracon.AGUI` | `1.0.0-preview.6` yayında (rapor anında yoktu) |
| `Microsoft.Agents.AI` | 30 kararlı sürüm; `1.0.0` 2026-04-02, `1.16.0` 2026-07-30, `1.25.0` 2026-10-14, `1.26.0` 2026-10-23. Tracon pin'i `1.25.0` (`Directory.Packages.props:19`) |
| `Microsoft.Agents.AI.Hosting` | 63 sürüm, **0 kararlı**; son `1.26.0-preview.261023.1` |
| `A2A.AspNetCore` · `A2A` | 9 sürüm, 0 kararlı; son `1.0.0-preview3` |
| `Microsoft.Agents.AI.DevUI` | 53 sürüm, 0 kararlı |
| MEAI · MCP SDK | `10.11.0` ve `2.3.0` en yeni; Tracon aynı sürümde |
| GitHub `farukatasoy/Tracon` | public; 0 yıldız; 1 fork; 6 açık issue (insan kaynaklı mı ölçülmedi) |
| `main` CI | 2026-10-19'dan 2026-10-27'ye 25 koşum `Paketle`'de `NU1102` ile durdu; Faz 219–250 tam kapıları CI'da koşmadı (BL-059, kapandı; `cfdae68e`) |

---

## 2. Raporun ölçümü

**Hüküm anahtarı:** **Doğru** — ölçüm iddiayı doğrular · **Kısmen** — iddianın
bir parçası doğru ya da sayı yakın · **Yanlış** — ölçüm iddiayı çürütür ·
**Bayat** — rapor gününde doğruydu, 2026-10-27 yayınıyla değişti · **İddia** —
bu turda doğrulanmadı.

**Sayım (60 satır, §2.1–§2.4):** Doğru 29 · Kısmen 20 · Yanlış 6 · Bayat 3 · İddia 2. §2.5 karar satırları ayrıca yargılanır; orada bir Yanlış daha vardır (K-1037).

**Özet yargı.** Raporun yönü doğrudur: ilk temas çelişkilidir, MAF-yerli giriş
kayıplıdır, güvenlik varsayılanları fail-open'dır, benimseme hiç ölçülmemiştir.
Ama dört taşıyıcı iddiası yanlıştır ya da abartılıdır: K-1037 ikinci hattı
kurmadı (UR-004 önerisinin dayanağı düşer); "tek süreç varsayımı" çok replika
altyapısının çoğunu görmez; "OTLP örneği yok / ikinci silo" sitedeki örneği ve
MEAI'nin `gen_ai.*` span'ini görmez; "yayın yerelde biter" bir gün sonra bayatladı.

### 2.1 Sayılar (rapor §2, §4 tablosu)

| # | İddia | Hüküm | Ölçülen | Kanıt |
|---|---|---|---|---|
| 1 | 20 yayınlanan paket | Bayat | 21 paketlenen proje; nuget.org 21/21 `preview.6`. Rapor gününde nuget.org 20 paket, `preview.4` | `src/Directory.Build.props:37`; `YAYIN-HAZIRLIK.md:26` |
| 2 | `net8.0`, `net9.0`, `net10.0` | Doğru | Faz 193 (`net8.0`/`net9.0` düşer) planlı, en erken 2026-11-10 | `src/Directory.Build.props:13` |
| 3 | ~224.000 C# satırı; Core 84.949, AspNetCore 32.041, Abstractions 28.307 | Doğru | `af816fc6`: 224.254 (üretilen hariç); HEAD 226.672 | `git ls-tree` + `wc -l` |
| 4 | Konsol 42.626 TS/TSX, React 19, 250 KB gzip | Doğru | `frontend/src` sayımı | `package.json:18`; `scripts/postbuild.mjs:26` |
| 5 | 8.046 test özniteliği, 718 sözleşme, ~450 vitest, 105 Playwright | Kısmen | 8.041 öznitelik satırı; 718 · 451 · 105 doğru. HEAD 8.261 · 726 · 472 · 108 | `git grep -P '^\s*\[(Fact\|Theory)\b'` |
| 6 | Abstractions'ta 85 arayüz (38'i `store`) | Yanlış | Abstractions 82; 85 tüm `src/` sayısı; `I*Store` 35 | PublicAPI tip satırları |
| 7 | 73 `*Options` sınıfı | Kısmen | 72 sınıf + 2 record = 74 | `src/` |
| 8 | 625 public tip | Kısmen | PublicAPI 626 (README 625 diyordu, `ad2fd5ec` düzeltti); HEAD 628 | PublicAPI |
| 9 | Migration PG 64 · SQL Server 53 · SQLite 52; 53 tablo | Doğru | HEAD 65 · 54 · 53 | `Migrations/*.sql` |
| 10 | 181 uç, 139 OpenAPI yolu | Doğru | 180 AspNetCore + 1 AGUI; 177 operasyon | `docs/openapi/tracon.json` |
| 11 | 34 ekran | Yanlış | 30 ekran bileşeni, 36 rota | `app.tsx:45-104`; `README.md:49` |
| 12 | MAF temas oranları 63/125 · 17/28 · 38/132 · 49/230 · 45/218 | Doğru | 2026-10-18 anlık görüntüsü, yeniden hesaplanmadı | `MAF-KAPSAMA.md:93` |
| 13 | `✗` cırcırı 50 | Yanlış | `CARPI_TABANI = 49` (Faz 236). `MAF-KAPSAMA.md` §10 P-03 hâlâ 50 der → kayma | `scripts/maf-kapsama.py:48` |
| 14 | `#pragma` 65 satır / 53 dosya; MAAI001 ×40, MEAI001 ×21 | Kısmen | 65/53 yalnız OPENAI001 ve MCPEXP002 eklenince; MAAI001 41 | `git grep` |
| 15 | `PublicAPI.Unshipped` 8.521; Abstractions 5.787; `Shipped` boş | Doğru | HEAD 8.579 / 5.830 | `wc -l` − `#nullable` başlıkları |
| 16 | 87 günde 1.741 commit, 250 kapanmış faz | Kısmen | 1.741 doğru; 249 ✅ + Faz 7 ⏸ | `git rev-list --count af816fc6` |
| 17 | `Tracon` 465 indirme, `preview.4` 62 | Doğru | Rapor gününe uygun; bugün 516 / 69 | NuGet arama API'si |
| 18 | Hosting ailesi ve `A2A.AspNetCore` 26 minor sürümdür ön sürüm | Doğru | Hosting 63 sürüm, 0 kararlı | flat container |

### 2.2 Engeller (rapor §3)

| # | İddia | Hüküm | Ölçülen | Kanıt |
|---|---|---|---|---|
| 19 | Tek süreç varsayımı; çok örnekli davranış hiç ölçülmedi | Kısmen | K-739 metni doğru. Ama SQL iş kirası (`SKIP LOCKED` / `UPDLOCK, READPAST`), kira yenileme, kiracı başına kira tavanı, singleton kira ile lider seçimi, kalp atışı uzlaştırması, API/işçi ayrımı (`RunWorker=false`) var ve gerçek Postgres'te iki `IJobStore` ile test edilmiş. Açık olanlar doğru: hız sınırı bellekte, MCP OAuth bekleyen akış ve token süreç içinde, kuyruksuz run iptali süreç içinde (yanlış örnekte `409`), `MaxConcurrentJobs = 2` | `JobStoreConcurrencyTests`; `SingletonLeaseTakeoverTests`; `TraconRateLimitFilter.cs`; `McpOAuthAuthorizationCoordinator.cs:52`; `McpOAuthTokenCacheRegistry.cs:17`; `RunCancellationRegistry.cs:8`; `RunEndpoints.cs:298`; `TraconSchedulingOptions.cs:25` |
| 20 | Fabrika agent'ı 12 model halkasını, BYOK'u, egress'i, maliyeti kaybeder | Kısmen | Bugün 13 halka + 2 tüketici yuvası (`HostedToolUsageChatClient` eklendi). Halkalar derleyicide değil `ModelProviderRegistry`'dedir; `IModelProviderRegistry.CreateChatClient` ve özel `IAgentSource` geri kazanır. Kayıp varsayılan davranıştır | `ModelProviderRegistry.cs:444-634`; `MAF-KAPSAMA.md` §7.1 |
| 21 | `AddAIAgent` kayıtları kataloğa görünmez (D-06) | Doğru | `GetKeyedServices<AIAgent>` okuyan kod yok | `MAF-KAPSAMA.md:750` |
| 22 | `WorkflowKind` kapalı; fonksiyon düğümü yalnız `Sequential` | Doğru | | `WorkflowDefinitionValidator.cs:116-128` |
| 23 | Dallanma ve fan-out veriyle tanımlanamaz | Kısmen | `Concurrent` veriyle fan-out/fan-in'dir; `Handoff` model kararıyla dallanır. Eksik: koşullu kenar ve alt workflow (F-659) | `WorkflowKind.cs` |
| 24 | Tanımda taslak/yayın, dışa/içe aktarma, ortam terfisi yok | Kısmen | Aktarma ve terfi yok (doğru). Sürüm, fark, geri alma ve canary var | `AgentEndpoints.cs:146-172` |
| 25 | `CHANGELOG` 29 "Changed" kaydı | Yanlış | `af816fc6` `[Unreleased] ### Changed` 31 madde | `CHANGELOG.md` |
| 26 | Yüzey `rc.1`'de tek commit'le donacak (K-603) | Kısmen | Yüzey `rc.1`'de donar (UR-009 B), `Shipped` dolumu `1.0.0` GA'dadır (K-603, `breaking_changes.py`, `ga-olcutleri.md`). İki ayrı adımdır; yayın defterinin cümlesi ikisini yan yana koyup karıştırıyordu (Dalga 0'da netleşti) | `KARARLAR.md:648`; `YAYIN-HAZIRLIK.md:43` |
| 27 | `preview.5` yayınlanmadı; `Tracon.AGUI` NuGet'te yok | Bayat | İkisi de 2026-10-27'de yayımlandı | registration API |
| 28 | Faz 212–217 CI'da koşmadı | Kısmen | O kayıt 2026-10-16'da itilmemiş commit'leri anlatıyordu. Gerçek açık: `main` 25 koşum `Paketle`'de kırmızıydı, Faz 219–250 tam kapıları atlandı | `cfdae68e`; BL-059 |
| 29 | "README sürümü = NuGet sürümü" kapısı yok | Doğru | A-71 ve F-709 açık | `YAYIN-HAZIRLIK.md:77,90` |
| 30 | Kurumsal güven paketi eksik (lisans, Gmail, SLA yok, son sürüm yaması, A-61, BL-057) | Doğru | | `LICENSE.md:3,16-17`; `SECURITY.md:6-7` |
| 31 | `NPM_TOKEN` 2026-11-26'da biter | Kısmen | Tarih bir tahmindir; gerçek bitiş okunmadı (A-66) | `YAYIN-HAZIRLIK.md:76` |
| 32 | Dış katkı: K-228 / K-408 Türkçe aparat | Kısmen | K-228 i18n kütüphanesi kararıdır; doğru kimlik K-408. F-277 doğru | `KARARLAR.md:273,453` |
| 33 | Kaynak yorumlarında 959 iç kayıt referansı | Kısmen | Yeniden üretilemedi: `.cs` 880, `.cs+.ts+.tsx` 969, tüm `src` 1.175 | `git grep -P '\b(K\|F\|HATA)-\d{2,4}\b'` |
| 34 | "Tetik bekleyenler" 60+, çoğu "bir tüketici bildirirse" | Kısmen | 69 satır; 18'i "tüketici", 24'ü "tüketici/talep" der | `ADAYLAR.md` § *Tetik bekleyenler* |
| 35 | Şablon `WRITE_MODEL_NAME_HERE`, rehberin `DefaultModel` secret'ını okumaz; rehber "Program.cs'i değiştir" der | Doğru | Ek: değiştirilen `Program.cs` tool ve kalıcılık kaydını düşürür, "Run it" yine tool kartı vaat eder; şablon README'si başka yol söyler | `Program.cs:96`; `first-agent.md:45,52,103-105`; şablon `README.md:37` |
| 36 | İki yapılandırma anahtarı biçimi | Doğru | `OpenAI:ApiKey` ↔ `Tracon:Providers:OpenAI:ApiKey` | `src/Tracon/README.md:16`; `guides/voice.md:358`; `first-agent.md:44` |
| 37 | Meta paket SQL Server kullanana Npgsql taşır | Doğru | | `src/Tracon/Tracon.csproj:29` |
| 38 | Site `main`'i belgeler, sürümlü belge yok | Doğru | | `reference/versioning.md:213`; `astro.config.mjs:148` |
| 39 | Sayılar kayar: 20/21 paket, altı/yedi nokta, yedi/sekiz işlem | Doğru | README düzeldi; `SECURITY.md:5` ve `src/Directory.Build.props` hâlâ 20 der (Kanal 2) | |

### 2.3 Tehditler (rapor §4)

| # | İddia | Hüküm | Ölçülen | Kanıt |
|---|---|---|---|---|
| 40 | Agent 365 GA 1 Mayıs 2026 | Doğru | | Microsoft 365 Message Center MC1296867 |
| 41 | Foundry Hosted Agents GA Ağustos 2026 | Kısmen | Azure Updates AZ563596 2026-06-02 der; Foundry özeti Temmuz–Ağustos der | §3 |
| 42 | Foundry Control Plane public preview (Ignite 2025), Entra Agent ID | İddia | Bu turda açılmadı | — |
| 43 | `docs/` içinde "Foundry Control Plane", "Agent 365" 0 isabet | Doğru | Entra Agent ID de 0 | `grep -rc` |
| 44 | MEAI 10.9 `FailoverChatClient`, `OrderedFailoverChatClient`, `RoutingChatClient` | Kısmen | Üçüncüsünün adı `SemanticRoutingChatClient`; ikisi 10.8'de yok, 10.9'da var | MEAI derlemesinde tip adı taraması |
| 45 | 13 paralel uygulama: 1 hata, 1 borç, 10 haklı, 1 ürün; 7'sinde gerekçe yok | Bayat | 2026-10-18 anlık görüntüsü; Dalga 1 ölçümü (2026-10-19) iki "Hata"yı düşürdü ve gerekçe tablosu yazdı | `MAF-KAPSAMA.md` §1.4, §6 |
| 46 | MAF 1.26.0: `DeleteSessionAsync`, AG-UI istek başına agent, yinelenen onay kimliği reddi | Doğru | #8801, #9382, #9542. Ek: `TraconAgentSessionStore : AgentSessionStore` doğrudan etkilenir | sürüm notu; `TraconAgentSessionStore.cs:39` |
| 47 | README "MAF reached GA with 1.16.0" | Doğru (README yanlış) | GA `1.0.0`, 2026-04-02 (Kanal 2) | `README.md:153` |
| 48 | README DevUI satırı ".NET documentation: coming soon" | İddia | Satır var (`README.md:166`); Learn'deki .NET DevUI belgesi bu turda açılmadı | — |
| 49 | Aspire için `src/`, `samples/`, `docs-site/` içinde 0 isabet | Doğru | | `git grep -i aspire` |
| 50 | MCP 2026-07-28 DCR'yi kaldırdı, sampling/roots/logging'i düşürdü | Kısmen | **Kullanımdan kaldırıldı** (en az 12 ay pencere; SEP-2577, PR #2858), silinmedi. Raporun anmadığı büyük değişiklik: protokol oturumu kaldırıldı (durumsuz). Repo bunu zaten izler | MCP changelog; `MAF-KAPSAMA.md` §0 #40; F-657 |

### 2.4 Eleştiriler (rapor §5)

| # | İddia | Hüküm | Ölçülen | Kanıt |
|---|---|---|---|---|
| 51 | Kiracılık varsayılan kapalı; bozuk kiracı başlığı varsayılan kiracıya düşer | Doğru | Belgelenmiş bir davranıştır: "reject it in front of Tracon" | `HttpTenantContext.cs:74-75,122-127`; `governance.md:48-53` |
| 52 | `AmbientTenantScope` düşünce "boş kiracı" okunur | Yanlış | Boş kiracı oluşamaz (`Normalize` atar); kapsamsız arka plan kodu varsayılan kiracıya düşer, sorgu boş döner | `AmbientTenantScope.Normalize`; `TraconOptionsValidator.cs:33` |
| 53 | `RequireRolePolicies` varsayılan `false`; kayıtsız rol politikası üst katmana düşer | Doğru | `Tracon.PlatformAdmin` ise kayıtsızken reddeder | `TraconEndpointOptions.cs:187`; `RoleEndpointConventionBuilderExtensions.cs:25-33` |
| 54 | Politikasız egress kısıtsız; tool yetkisi hepsine izin; `tenant_id` FK'sız, RLS yok | Doğru | FK/RLS yokluğu bilinçli (K-623) | `TenantProviderAccess.cs:53-54`; `AllowAllToolAuthorizationHandler.cs:20`; `MIMARI-GUVENLIK.md:137-139` |
| 55 | Denetim zinciri anahtarsız SHA-256; HMAC ve dış çapa yok, "A10 ertelendi" | Kısmen | SHA-256 doğru. A10 dış çapa/outbox kalemidir, HMAC değil | `AuditChainHasher.cs:63` |
| 56 | 8 işlem dışında best-effort; doğrulama yalnız API'de; "seven"/"eight" çelişir | Doğru | 8 işlem, 11 `WriteOrThrowAsync` noktası; `threat-model.md` listesi `tool_input.answer`'ı atlar | `IAuditLog.cs:11-24`; `AuditEndpoints.cs:90`; `audit.tsx` |
| 57 | OTLP örneği yok; GenAI uyumu reddedildi; izler "ikinci siloda" | Yanlış | Sitede `AddSource` + `AddOtlpExporter` örneği var; `chat` halkası MEAI `UseOpenTelemetry` ile `gen_ai.*` span'i üretir. 2026-08-26'da elenen bir uyum **modu**ydu, K kararı değil. Gerçek açık F-595'tir | `guides/observability.md:39-51`; `ModelProviderRegistry.cs:533`; `kesif/2026-08-26-yeni-feature-fikirleri.md:68` |
| 58 | Son 25 fazın "kaçan kusur" sütunu ölçülmedi; kuyruk ~6–7 run/s'de doyar | Doğru | Art arda 32 faz (223–254) ölçülmedi | `FAZ-OLCUMLERI.md`; `guides/production.md:528,581` |
| 59 | `tools.md:110` örneği `Build()` sonrası `AddTool` çağırır; `Func<IServiceProvider, AIFunction>` aşırı yüklemesi yok | Doğru | `AddTool` → `Services.AddSingleton` | `getting-started/tools.md:110-111`; `TraconBuilderExtensions.Tools.cs:33` |
| 60 | Son 30 fazın 27'si kapsama ve sertleştirme; mercek 1'e faz yok; "ilk agent süresi" sayı değil | Kısmen | ~19–20/30 (MAF dalgası 12, denetim 6–7). Son benimseme fazı Faz 37 (2026-08-06). İlk agent süresi hiçbir yerde ölçülmedi | `FAZ-DEFTERI.md`; `ADAYLAR.md:61` |

### 2.5 Yeniden açma önerileri (rapor §8)

| Karar | Raporun iddiası | Ölçüm |
|---|---|---|
| K-740 | Lisans hedefle çelişir | Doğru (`KARARLAR.md:785`) |
| UR-004 A | "K-1037 zaten ikinci bir ön sürüm hattı kurdu" | **Yanlış:** K-1037 yalnız kuralı yazdı; "hat önceden kurulmaz" (`KARARLAR.md:1072`; `MAF-KAPSAMA.md` X-1). Önerilen ayrık hat K-602'de 2026-08-24'te reddedilmişti |
| K-739 | Çok replika vaadi gerekir | Gerekçe doğru, ölçek abartılı (#19) |
| F-638 / K-212 | Foundry; "37 paket" | Ağırlık gerekçesi P-05 ile düştü; engel 2026-10-21 👤 öncelik kararıdır |
| F-51 | Aspire talep bekliyor | Doğru |
| D-06 | Katalog `AddAIAgent` okumaz | Doğru; yeniden açılma koşulu pratikte kapalı ("Hosting GA olsa bile") |
| K-494 | Fonksiyon düğümü yalnız `Sequential` | Doğru |
| K-055 | GenAI uyum modu reddi | Kısmen (#57) |
| K-058 · K-645 | stdio MCP · adlandırılmış `Add*` | Doğru; ikisi de 2026-10'da korundu |
| F-171 | "Sayıları tek kaynaktan üretmek F-171'in kapsamı" | Kısmen: F-171 Faz 158'de kapandı; kayma yeniden oluştu |

---

## 3. Raporun kaçırdıkları

1. **Üretim profili var.** `RequireProductionProfile()` altı riski açılışta
   denetler (`TraconProductionRisk.cs`: SingleTenant · UnownedSessions ·
   UnencryptedContentAtRest · UninspectedContent · UnlimitedRequestRate ·
   UnboundedRetention). Rol politikası, tool yetkisi, egress ve başlık çözümünü
   **denetlemez** (`ProductionProfileValidator.cs:66-150`). Fail-closed işin
   doğal yeri budur → Q-20.
2. **MAF 1.26.0 Tracon'un kendi türevine dokunur.** `TraconAgentSessionStore :
   AgentSessionStore` (`TraconAgentSessionStore.cs:39`); `DeleteSessionAsync`
   soyut mu sanal mı, kiracı sınırını nasıl taşır — ölçülmeli → Q-10.
3. **Freeze ölçütü 2 bayatladı.** "Her bağımlılık en yeni sürümde" ✅ yazar; MAF
   `1.26.0` 2026-10-23'te çıktı.
4. **Freeze ile `Shipped` dolumu yayın defterinde karışır** (#26). Dalga 0 ölçümü: çelişki değil, belirsizlik.
5. **İlk temas rehberi kendi içinde çelişir** (#35) ve README "iki satır"
   örneği (`README.md:32-40`) agent kaydetmez ama "a working agent" der
   (`README.md:42`).
6. ~~AOT bayrağı ile belge çelişir.~~ **Dalga 0 ölçümüyle düştü:** K-1019 ve
   `docs/hafiza/tedarik-zinciri.md:115` bayrağı (paketin kendi IL analizi) ile
   vaadi (`ProviderAotSmoke` duman testi) bilerek ayırır. Kusur değildir.
7. **Kiracı başlığı ile API anahtarı karşılaştırması harfe duyarlıdır.**
   `TraconEndpointFilter.cs:342` ham başlığı `StringComparison.Ordinal` ile
   karşılaştırır; site "letter case does not distinguish two tenants" der.
   Sonuç fail-closed'dır (`403`), sızıntı yoktur.
8. **`governance.md:303` kurcalama tespitini abartır:** "altered after it was
   written" der; anahtarsız hash veritabanına yazabilen birine karşı korumaz.
9. **Workflow eşzamanlılığı varsayılan olarak kiracılar arası paylaşılır**
   (süreç başına 4, `MaxConcurrentRunsPerTenant` verilene kadar;
   `guides/production.md:344`).
10. **OAuth token'ını veritabanına yazmak K-059 ile çatışır.** Rapor "K-059 ile
    uyumlu: değer `IContentProtector` altında" der; K-059 MCP kimlik doğrulama
    değerinin veritabanında saklanmadığını söyler (`KARARLAR.md:104`) → Q-31'de 👤.
11. **MCP 2026-07-28 durumsuzdur.** Protokol oturumunun kalkması MCP sunucusunu
    çok replikaya yaklaştırır; SDK'nın bu sürümü ne zaman konuşacağı ölçülmedi.

---

## 4. Ekosistem taraması (Aşama 3.2)

| Kaynak | Bakılan tarih | Ne değişti | Tracon'a etkisi |
|---|---|---|---|
| NuGet flat container + registration (`microsoft.agents.ai`, `.hosting`, `.devui`, `a2a.aspnetcore`, `tracon*`) | 2026-10-28 | MAF `1.26.0` (2026-10-23); Hosting ailesi hâlâ ön sürüm | Q-10; ER-001 sürer |
| [MAF `dotnet-1.26.0` sürüm notu](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.26.0) | 2026-10-28 | `DeleteSessionAsync` (#8801), AG-UI istek anında agent seçimi (#9382), yinelenen onay kimliği reddi (#9542), MCP çağrı başlığı doğrulaması (#9539), fan-out hedefi tekilleştirme (#9432) | Q-10 |
| [MCP 2026-07-28 changelog](https://modelcontextprotocol.io/specification/2026-07-28/changelog) | 2026-10-28 | Oturum ve `initialize` kalktı; MRTR; Tasks uzantıya taşındı; Roots, Sampling, Logging ve DCR kullanımdan kalktı | F-657, F-658 zaten izler; Q-30…Q-33 için not |
| [Agent 365 GA (MC1296867)](https://www.itelio.com/en/microsoft-message-center/MC1296867) | 2026-10-28 | GA 2026-05-01 | Q-41 konum sayfası |
| [Hosted agents GA (AZ563596)](https://mc.merill.net/message/AZ563596) · [Foundry Temmuz–Ağustos özeti](https://devblogs.microsoft.com/foundry/whats-new-in-microsoft-foundry-july-august-2026/) | 2026-10-28 | Tarihler çelişir (2026-06-02 / Temmuz–Ağustos) | Q-41 |
| MEAI 10.8.0–10.11.0 derlemeleri (yerel NuGet önbelleği) | 2026-10-28 | `FailoverChatClient`, `OrderedFailoverChatClient`, `SemanticRoutingChatClient` 10.9'da geldi | R-02 hükmü (MAF-KAPSAMA §6) sürer |

---

## 5. Üç kanalın çıktısı (Aşama 1)

### Kanal 1 — yeni aday

Kalemler [`RC-KALITE-DALGALARI.md`](../RC-KALITE-DALGALARI.md)'de yerel kimlikle
yaşar; F-numarası `faz-planlama`'da verilir (gerekçe o dosyanın §11'inde).

| Kimlik | Başlık | Dalga |
|---|---|---|
| Q-10 | MAF 1.26 yükseltmesi ve seam ölçümü | 1 |
| Q-11 | İlk agent'a kadar geçen süre: kendi başına çalışan şablon + ölçülen sayı | 1 |
| Q-12 | G-02: async fabrika, model beyanı, `AddTool` fabrikası, `AddAIAgent` geçiş rehberi | 1 |
| Q-13 | `AddJobHandler` ve `UseScheduling` → `ITraconBuilder` | 1 |
| Q-14 | Meta paketin yeniden kurulumu: depo ayrı seçilir | 1 |
| Q-20 | Üretim profili genişler + kiracılık açıkken bozuk başlık `400` | 2 |
| Q-21 | Anahtarlı denetim zinciri + konsolda doğrulama | 2 |
| Q-22 | Kiracı başına tool izin listesi | 2 |
| Q-30 | Dağıtık hız sınırı seam'i (F-92'yi devralır) | 3 |
| Q-31 | MCP OAuth'un çok replikada çalışması | 3 |
| Q-32 | Kuyruksuz run'ın örnekler arası iptali | 3 |
| Q-33 | İki düğümlü kabul seti ve destek matrisi | 3 |
| Q-40 | Aspire AppHost örneği ve rehberi | 4 |
| Q-41 | "Tracon ve Microsoft kontrol düzlemleri" konum sayfası | 4 |
| Q-42 | MAF sürüm takibi CI'da | 4 |
| Q-50 | Tanım dışa/içe aktarma ve ortam terfisi | 5 |
| Q-60 | Sürümlü belge | 6 |
| Q-61 | Sevk edilen sayı kapısı | 6 |

### Kanal 2 — kusur

Kullanıcıya söylendi (2026-10-28) ve onayıyla `kusur-giderme` aynı gün Dalga 0
olarak koştu: sekizi kapandı, Q-04 kusur değil çıktı, kapı turu F-744'ü (F-180
sınıfı kırılgan test) açtı. Sonuç `RC-KALITE-DALGALARI.md` §4'tedir.

| Kimlik | Bulgu | Kanıt |
|---|---|---|
| Q-01 | README: "GA with 1.16.0"; DevUI satırı ölçülmeli; `Tracon.AGUI` ön sürüm zinciri listesinde yok; "iki satır" örneği agent kaydetmez | `README.md:42,153,166,309` |
| Q-02 | `tools.md` örneği `Build()` sonrası `AddTool` çağırır | `getting-started/tools.md:110-111` |
| Q-03 | Sevk edilen sayı kayması | `SECURITY.md:5`; `src/Directory.Build.props:130,182`; `guides/embedding.md:3`; `capabilities.md:142,282`; `getting-started/security.md:390`; `reference/threat-model.md:77,108`; `README.md:212`; `compatibility.md:89` |
| Q-04 | ~~AOT bayrağı ile belge çelişir~~ — kusur değil (Dalga 0, K-1019) | `Directory.Build.targets:16-17`; `compatibility.md:83-84` |
| Q-05 | `governance.md:303` kurcalama tespitini abartır | `governance.md:303` |
| Q-06 | Freeze ile `Shipped` dolumu yayın defterinde karışır (belirsizlik, çelişki değil) | `KARARLAR.md:648`; `YAYIN-HAZIRLIK.md:43` |
| Q-07 | MAF-KAPSAMA §10 P-03 "taban 50" ↔ 49 | `scripts/maf-kapsama.py:48` |
| Q-08 | Kiracı başlığı ↔ API anahtarı karşılaştırması harfe duyarlı | `TraconEndpointFilter.cs:342` |
| Q-09 | İlk temas rehberi kendi içinde ve şablonla çelişir | `first-agent.md:52,103-105`; şablon `README.md:37` |

### Kanal 3 — yeniden açılan karar

| Kimlik | Kararın gerekçesi | Neyin değiştiği | Kullanıcının kararı (2026-10-28) |
|---|---|---|---|
| K-740 | Tek bakımcının emeğini korumak | Hedef orta/büyük ölçek; PolyForm SB büyük şirkette denemeye izin vermez | **QD-1: bu dalganın dışında** |
| UR-004 A | Upstream GA beklenir | Raporun dayanağı (K-1037) yanlış; Hosting 63 sürüm ön sürüm | **QD-2: A kalır + zaman tetiği** |
| K-739 | Çok node hedefi belirsizdi | Hedef segment çok replikadır; altyapının çoğu var, dört açık ölçüldü | **QD-3: dar vaat açılır → K-1113** |
| D-06 | Köprülenen agent fabrika agent'ıdır | "Karşılık bulsun" fiili | **QD-4: D-06 korunur; G-02 + geçiş rehberi (Q-12)** |

---

## 6. Benimsenmeyen öneriler

| Raporun önerisi | Neden benimsenmedi | Kalıcı mı | Nereye yazıldı |
|---|---|---|---|
| Açık çekirdek lisans | QD-1: lisans bu dalganın dışında | Hayır (👤 açık) | `RC-KALITE-DALGALARI.md` §2 |
| Kararlı aile ayrı `rc.1`/GA (ayrık hat) | Dayanağı yanlış (K-1037); K-602'nin reddettiği yol; QD-2 | Hayır | §2, YAYIN-HAZIRLIK UR-004 |
| `DiscoverMafAgents()` köprüsü | QD-4: D-06 korunur | Hayır | §2 |
| GenAI semantik kural uyum modu | Ölçüldü: `gen_ai.*` span'i MEAI'den zaten gelir (#57) | Evet (yeni kanıt olmadan) | bu dosya |
| `Tracon.Foundry` uydu paketi | F-638 öncelik dışı (👤 2026-10-21); yalnız konum sayfası (Q-41) | Hayır | §2 |
| `Tracon.Aspire.Hosting` paketi | Örnek + rehber önce (Q-40); paket F-51'in tetiğinde kalır | Hayır | §2 |
| stdio MCP (K-058) · 60 seam için `Add*` (K-645) | İkisi de 2026-10'da korundu; F-656 tarifi var | Evet | §2 |
| Veriyle dallanma (K-494, F-659) | Ekleyicidir, `rc.1`'i bekletmez; `WorkflowKind` kararı 👤 | Hayır | §2 |
| Python istemcisi | Genellik ölçülmedi; tüketici sinyali yok | Hayır | bu dosya |
| "İlk ufuk iki haftada biter" | Ölçülmemiş süre (`aday-kesfi` § *Yazılmayacaklar*) | — | — |

---

## 7. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap |
|---|---|
| Lisans (K-740) bu dalgada hangi yola göre yazılsın? | Bu dalganın dışında |
| `rc.1` yolu (UR-004 A) ne olsun? | A kalsın + zaman tetiği |
| Çok replika (K-739) ne olsun? | Dar vaat aç |
| MAF-yerli giriş için ne yapılsın? | G-02 + geçiş rehberi; D-06 korunur |

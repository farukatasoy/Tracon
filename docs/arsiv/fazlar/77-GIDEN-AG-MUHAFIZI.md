# Faz 77 — Giden Ağ Muhafızı

> **Durum:** ✅ Tamamlandı (2026-08-20)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-131** · bulgular
> [`guvenlik-tarama/BULGULAR.md`](../../guvenlik-tarama/BULGULAR.md) B05-2 · B05-3 · B05-4 · B05-5 · B05-6 · B05-7
> **Önkoşul:** Yok — K-164'ün `ConnectCallback` deseni bugün webhook yolunda çalışıyor ve bu faz onu genelleştirir
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Mcp`, `AgentPrism.AspNetCore`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — `PublicAPI.Shipped.txt` dosyaları **API kaydı taşımaz** (`wc -l src/*/PublicAPI.Shipped.txt` → her biri tek satır, yalnız `#nullable enable`), yani bugün eklemek ucuzdur; Faz 7'den sonra bir sürüm kararı olur
> **Tüketici yüzeyi:** `docs-site/src/content/docs/getting-started/security.md` (giden ağ bölümü) · `docs-site/src/content/docs/guides/production.md` (yeni ayar) · sevk edilen: `AgentPrismEgressOptions` XML dokümanı, `capabilities.md` satırı
> **Manuel test alanı:** [`manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md) ve [`manuel-test/18-MCP-VE-A2A.md`](../../manuel-test/18-MCP-VE-A2A.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/77-GIDEN-AG-MUHAFIZI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism bugün **üç** yerden dışarı ağ isteği atar: webhook teslimi, MCP sunucu bağlantısı ve model sağlayıcı çağrısı. SSRF koruması yalnız **birincide** vardır. Bu faz korumayı üçüne de taşır ve `secret`'ın yanlış hedefe gitmesini engelleyen yapılandırma anahtarı kısıtını eksik iki yüzeye yayar.

## Bitiş Ölçütleri (DoD)

- [x] `PUT /api/mcp-servers/x` ile `http://169.254.169.254/` → `400`, mesaj ayar adını içerir
- [x] Aynı istek `AllowPrivateNetworkTargets=true` iken → `200`
- [x] `PUT /api/tenants/{id}/providers/anthropic` ile özel ağ `Endpoint`'i → `400`
- [x] MCP ve webhook `secret` anahtar adları önek dışındaysa → `400`
- [x] `WebhookSocketGuard`'ın (yeni adıyla `EgressSocketGuard`) testi vardır ve NAT64'ü kapsar
- [x] `WebhookUrlValidator` içindeki adres mantığı **tek** yerde kalır; kopya yoktur
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` ve `18-MCP-VE-A2A.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [x] `docs-site/` güncellendi (`security.md` giden ağ bölümü + `production.md` yükseltme notu); `npm run build` + `check-links.mjs` temiz
- [x] [`BULGULAR.md`](../../guvenlik-tarama/BULGULAR.md) içindeki B05-2/3/4/5/6/7 satırları **KAPANDI** olarak işaretlendi

### Doğrulama komutları

```bash
# Özel ağ hedefi reddedilir
curl -s -X PUT http://localhost:5081/agentprism/api/mcp-servers/probe \
  -H 'Content-Type: application/json' \
  -d '{"endpoint":"http://169.254.169.254/","transport":"StreamableHttp"}' | jq .detail

# Önek dışı yapılandırma anahtarı reddedilir
curl -s -X PUT http://localhost:5081/agentprism/api/mcp-servers/probe \
  -H 'Content-Type: application/json' \
  -d '{"endpoint":"https://mcp.example.com/","authorizationConfigurationKey":"ConnectionStrings:Default"}' | jq .detail

# Adres mantığının tek yerde kaldığı
grep -rn "IsPrivate\|ConnectCallback" src/ --include="*.cs"
```

---

## Plandan Sapmalar

| # | Plan ne diyordu | Gerçekte ne oldu | Neden |
|---|---|---|---|
| 1 | Dört sağlayıcı da `HttpClient` enjeksiyonuna izin verir; kanca `ClientOptions.HttpClient` | **Üçte ikisi yanlıştı.** Yalnız Anthropic öyle. OpenAI/Azure `ClientPipelineOptions.Transport` ister (`new HttpClientPipelineTransport(httpClient)`); Google `ClientOptions.HttpClientFactory` (`Func<HttpClient>`) ister — "zaten kendi `HttpClient`'ı var" notu da yanlıştı | Plan bunu 🚨 ile işaretleyip "uygulama anında ölçülmeli" demişti; ölçüldü. Sonuç değişmedi (yeni paket yok, `IHttpClientFactory` yok, K-164 korundu). K-532 |
| 2 | `EgressSocketGuard(IOptionsMonitor<AgentPrismEgressOptions>)` tek kurucu | İkinci kurucu eklendi: `EgressSocketGuard(Func<EgressAddressPolicy>)`, ve yeni bir public tip doğdu: `EgressAddressPolicy` | Webhook'un politikası **iki boyutludur** — `AllowInsecureHttp` yalnız loopback açar, `AllowPrivateNetworkTargets` tüm özel ağı. Tek `bool` bunu modelleyemezdi; iki `bool` parametresi ise çağrı yerinde okunmuyordu |
| 3 | MCP önek ayarı adı geçmiyordu; doğal yer `AgentPrismMcpOptions` | Ayar `AgentPrism.Abstractions`'ta yeni bir tipe kondu: `AgentPrismMcpSecurityOptions` | `AgentPrism.AspNetCore` `AgentPrism.Mcp`'yi **görmez** (yalnız `Core`'a bağlıdır). Kural iki pakette birden zorlanır; ayarı Mcp'de bırakmak iki kaynak üretirdi. `SectionName` aynı (`AgentPrism:Mcp`). K-534 |
| 4 | Kaydetme ucunda "adres denetimi" | Kaydetme ucunda **yalnız IP literal** denetlenir; ad çözülmez | DNS çözümü kaydetmeyi yavaşlatır ve **henüz çözülmeyen** bir adı reddederdi (`https://mcp.example.com/` gibi). Gerçek koruma zaten `ConnectCallback`'tedir ve kaçınılamaz. Webhook'un var olan iki katmanlı deseniyle aynı |
| 5 | `WebhookUrlValidator.ValidateResolvedAsync(url, settings, ct)` imzası korunur (ima) | İmza değişti: `(url, settings, egress, ct)` | Global egress ayarının webhook yolunda da okunması gerekiyordu. `PublicAPI.Shipped.txt` boş olduğu için kırıcı değişiklik bugün ücretsizdir |
| 6 | B05-7 kapsamı: NAT64 + IPv4-uyumlu | Dört biçim birden: NAT64 (`64:ff9b::/96`), IPv4-uyumlu (`::a.b.c.d`), IPv4-çevrilmiş (`::ffff:0:a.b.c.d`) ve **6to4** (`2002::/16`); ayrıca yerel-kullanım NAT64 öneki (`64:ff9b:1::/48`) bütün olarak reddedilir | Aynı kusur **sınıfı** — `kusur-giderme` "tek vakayı değil sınıfı kapat" der. Dördü de aynı üç satırlık çözümü paylaşıyor. Teredo bilinçle dışarıda: istemci adresi maskelidir ve sunucu adresi hedef değildir |
| 7 | Yalnız `WebhookSocketGuard` testsizdi (B05-6) | `EgressAddressValidator.ResolveAndValidateAsync` **aşırı uzun host'ta çöküyordu**: `Dns.GetHostAddressesAsync` `ArgumentOutOfRangeException` atar, `SocketException` değil — `catch` onu kapsamıyordu | Yeni yazılan `Over_long_host_is_rejected` testi buldu. Etkisi gerçek: `ConnectCallback` içinde yakalanmamış bir istisna, temiz bir "hedef reddedildi" yerine beklenmedik bir çökme olurdu. `catch` `ArgumentException`'ı da kapsıyor |
| 8 | Sevk edilen dokümanda tuzak notu yok sayıldı | XML dokümanlarından 🚨 ve K-NNN referansları **çıkarıldı** | `ShippedDocumentationSelfContainmentTests` bunları reddediyor ve taban çizgisi yalnız küçülebilir. İçerik korundu, yalnız günlük sesi tüketici sesine çevrildi |

**Açık soruların sonucu (kullanıcı kararı, 2026-08-20):** üçü de planın önerisiyle
kapandı — soru 1 → **B** (muhafız yalnız kiracı override'ında, K-531) · soru 2 →
**B** (`RequireHttps` ertelendi, `ADAYLAR.md`'ye F-132 olarak yazıldı) · soru 3 →
**A** (`Core`) · soru 4 → **B** (taşıma yardımcısı yok). Ayrıca Faz 76'nın devir
notu 9(a)'daki `docs/**.md` bütçe sorusu kendiliğinden çözüldü: ölçüldü, **%37 boş**
(3.171.468 / 5.000.000) — arşivleme aradan sonra yeri geri kazandırmış.

## Bu Fazda Verilen Kararlar

| # | Karar |
|---|---|
| **K-529** | Giden ağ hedefi TEK bir muhafızdan geçer; üç yüzey de kendi kopyasını taşımaz |
| **K-530** | `AgentPrism:Egress:AllowPrivateNetworkTargets` varsayılanı KAPALI; üç yüzeyde de özel ağ reddedilir 👤 |
| **K-531** | Sağlayıcı istemcisine muhafız YALNIZ kiracı `Endpoint` override'ı varken takılır 👤 |
| **K-532** | Sağlayıcı SDK'larının muhafız kancası ÖLÇÜLDÜ; plan tahmini üçte ikisinde yanlıştı |
| **K-533** | `secret` çözen HER yapılandırma anahtarı bir önek allow-list'ine bağlıdır |
| **K-534** | MCP önek ayarı `Abstractions`'ta yaşar (`AgentPrismMcpSecurityOptions`) |
| **K-535** | Webhook ek başlıkları AgentPrism'in kendi başlık adlarını taşıyamaz |

## Doğrulama

Dört kapı da sıfır uyarı: `build` · `test` **4574/4574** · `pack` · `format`.

> 🚨 **Kapanış koşumunda bir ara sürüm 4573/4574 verdi ve düşen test bu fazın
> değil.** `ApprovalEndpointTests.Second_decision_on_the_same_approval_gets_409`
> **izolasyonda 3/3 düşüyor**, tam sette geçiyor (son iki tam koşum 4574/4574).
> **Ölçüldü:** `git stash -u` ile fazın tüm değişiklikleri geri alınıp temiz
> `HEAD` derlendiğinde **aynı şekilde düşüyor** — var olan bir kusurdur,
> regresyon değil. [`ADAYLAR.md`](../../ADAYLAR.md) **F-133** olarak kaydedildi.
> Onaylar bu fazın dokunduğu hiçbir yüzeyle kesişmiyor (giden ağ · webhook ·
> MCP · sağlayıcı).
Site kapıları temiz: `npm run build` (1010 sayfa) · `check-content` · `check-links`
(130.937 bağlantı, 0 kırık) · `check-weight` (en ağır 49.734 B / 57.000 B).
`secret` taraması: yalnız önceden var olan bilinçli sahte test anahtarları.

**`samples/AgentPrism.Api` ile gerçek koşum** (2026-08-20, DoD gereği):

| Komut | Sonuç |
|---|---|
| `PUT /api/mcp-servers/probe` ← `http://169.254.169.254/` | `400` · `The target resolves to a private network address (169.254.169.254); set 'AgentPrism:Egress:AllowPrivateNetworkTargets' to true to allow it.` |
| Aynısı, `AgentPrism__Egress__AllowPrivateNetworkTargets=true` ile | `200` — **ayarın gerçekten bağlandığını** kanıtlar (K-406 sınıfı) |
| `PUT /api/mcp-servers/probe` ← `authorizationConfigurationKey: ConnectionStrings:Default` | `400` · `... may only reference a configuration key under 'AgentPrism:McpSecrets:'.` |
| Aynısı, `AgentPrism:McpSecrets:Token` ile | `200` |
| `PUT /api/tenants/acme/providers/anthropic` ← `http://10.0.0.5/` | `400` · ayar adını içerir |
| `PUT /api/webhooks/orders` ← `secretConfigurationKey: ConnectionStrings:Default` | `400` · `... under 'AgentPrism:WebhookSecrets:'.` |
| `PUT /api/mcp-servers/probe2` ← `http://[64:ff9b::a9fe:a9fe]/mcp` (NAT64) | `400` — B05-7 gerçek uygulamada kapandı |

Adres mantığının tek yerde kaldığı (DoD):
`grep -rn "IsPrivate\|ConnectCallback" src/ --include="*.cs"` → uygulama yalnız
`EgressAddressValidator.IsPrivate` ve `EgressSocketGuard.ConnectAsync`'tedir;
`WebhookUrlValidator.IsPrivate` tek satırlık bir yönlendiricidir.

## Denetim Bulguları

`faz-denetim` bağımsız denetçisi (taze bağlam, yalnız DoD + `git diff`) **bir 🔴,
altı 🟡 ve iki 🟢** buldu. Hepsi doğrulandı; 🔴 ve 🟡'lerin tamamı **bu fazda
kapatıldı**.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | **K-164'ün zorlama noktası hâlâ testsizdi.** `EgressGuardTests`'in iki "guard" testi `ValidateAsync`'i çağırıyordu — kaydetme uçlarının bile kullanmadığı ayrı bir metot. `ConnectAsync`/`CreateHandler`/`CreateHttpClient` hiç koşmuyordu; gövdesi düz `Socket.ConnectAsync`'e indirgense 4483 testin hiçbiri kırmızıya dönmezdi. **B05-6'nın kapandığı iddiası yanlıştı** | **Düzeltildi.** Yeni `EgressSocketGuardTests` (9 case) gerçek `HttpClient`'ı sürer: özel adrese istek `HttpRequestException` ile düşer ve muhafızın gerekçesi `InnerException` zincirinde bulunur. Ayrıca "çözülen adreslerden biri bile reddedilirse hedef reddedilir" döngüsü `ValidateAddresses` seam'i ile iki sırada da test edildi |
| 2 | 🟡 | **MCP `prompt`/`resource` yolları 100 sn'lik yanıt sınırını KAYBETTİ.** `HttpClientTransport` kendi `HttpClient`'ını `Timeout = 100s` ile kurar (ölçüldü, ModelContextProtocol.Core 2.0.0); `CreateHttpClient()` sonsuz veriyordu. `McpShortLivedConnection` yalnız **connect**'i sınırlar, sonraki liste çağrısını değil — bağlantıyı kabul edip yanıtı askıda bırakan bir sunucuda uç süresiz asılırdı | **Düzeltildi.** `CreateHttpClient(TimeSpan timeout)` artık zorunlu parametre alır; MCP `ResponseTimeout = 100 sn` geçer, dört sağlayıcı gerekçesiyle `InfiniteTimeSpan` (SDK'ları kendi istek sınırlarını uygular). Sonsuzu **kazara miras almak** artık mümkün değil |
| 3 | 🟡 | **Webhook teslim yolundaki önek denetimi yakalanmamış istisna üretiyordu.** `SendAsync`'in `catch`'i yalnız `TaskCanceledException`/`HttpRequestException` idi; `AgentPrismException` kaçıyor, `WebhookDeliveryResult` **hiç yazılmıyor** (kayıt `Pending` kalıyor) ve "N hatadan sonra devre dışı bırak" sayacı hiç ilerlemiyordu | **Düzeltildi.** Denetim `ExecuteAsync`'e, adres reddinin **yanına** taşındı ve `DropAsync` kullanıyor — önek verdikti bir yeniden denemede değişmez. Test: `Out_of_prefix_secret_key_drops_the_delivery_instead_of_throwing` (`Dropped` + hiçbir istek gönderilmedi) |
| 4 | 🟡 | **Muhafız `UseProxy`'yi kapatmıyordu.** Ortamda `HTTPS_PROXY` varsa `ConnectCallback` **proxy'nin** adresini görür; gerçek hedef `CONNECT` isteğinin içinde gider ve hiç yargılanmaz. Sevk edilen `security.md` "the check that cannot be evaded" diyor | **Düzeltildi.** `CreateHandler()` artık `UseProxy = false` yazar; gerekçe koda ve K-536'ya yazıldı. Test: `Handler_does_not_use_an_ambient_proxy` |
| 5 | 🟡 | `TenantProviderEndpoints.SaveEgressPolicyAsync` `egressOptions` parametresini alıyor ama kullanmıyordu | **Düzeltildi.** Uygulayan oturumun `str.replace` hatasıydı — parametre iki handler'a birden eklenmişti. Kaldırıldı; adres denetimini gerçekten yapan `SaveBindingAsync`'te duruyor |
| 6 | 🟡 | `BindEgress`'in üstüne `BindTenantProviders`'ın XML dokümanı düşmüştü | **Düzeltildi.** Aynı sınıf hata: ekleme, var olan doküman ile metot arasına girmişti |
| 7 | 🟡 | Alan hafızası bölünmesi `openai-saglayici.md`'de girişsiz bir kod bloğu ve tamamen boş bir bölüm bıraktı | **Düzeltildi.** "Halka sirasi (Faz 48)" bloğu maddesiyle birlikte `model-boru-hatti.md`'ye taşındı; boş "Faz 62" başlığı kaldırıldı |
| 8 | 🟢 | `KARARLAR.md`'de K-535 ile K-524 arasında boş satır; tablo orada kesiliyor | **Düzeltildi** (kozmetik, tek satır) |
| 9 | 🟢 | İki test AgentPrism hakkında hiçbir şey iddia etmiyordu (`Non_ip_families_are_not_private` ölçüldü: `new IPAddress(new byte[]{1,2,3,4})` zaten `InterNetwork`; `Address_family_of_a_parsed_literal_is_preserved` BCL'i doğruluyordu) | **Silindi.** Kapsam iddiasını şişiriyorlardı |

**Denetçinin temiz bulduğu ve kayda geçirdiği ölçümler:** Google SDK'sının
`HttpClientFactory` kancası sızıntı üretmiyor (kurucuda 0, üç istekte 1 çağrı) ·
`IPAddress.TryParse` köşeli parantezli IPv6'yı kabul ediyor, yani
`ValidateLiteral(new Uri("http://[64:ff9b::a9fe:a9fe]/"))` gerçekten çalışıyor ·
`IPAddress.IsLoopback("::ffff:127.0.0.1")` `true` · muafiyet listeleri ve taban
çizgileri (`SourceLanguageTests`, `DIAGRAM_EXEMPT`, `CLOSING_EXEMPT`, ağırlık,
kontrast) **hiç büyümedi**.

**Denetim sonrası ek karar:** K-536 (aşağıda) — muhafız ortam proxy'sini kullanmaz.

## Sonraki Faza Devir Notu

1. 🚨 **Bir muhafızı test ederken "doğrulama metodunu" değil, ZORLAMA NOKTASINI
   sür.** Bu fazın 🔴'ı tam buydu: `ValidateAsync` yeşildi, `ConnectAsync` hiç
   koşmamıştı ve gövdesi silinse 4483 test yeşil kalırdı. Soru şudur: *bu kodu
   bozarsam hangi test kırmızıya döner?* Cevap "hiçbiri" ise o test kapsamı
   ölçmüyor, kapsam **iddia ediyor**. Aynı soruyu bir sonraki güvenlik kapısında
   da sor.
2. 🚨 **Bir SDK'nın `HttpClient`/`Transport`'unu değiştirmek, o SDK'nın
   TIMEOUT'unu da değiştirir.** `HttpClientTransport` kendi istemcisini 100 sn
   ile kurar; muhafızlı istemci sonsuz verince MCP `prompt`/`resource` uçları
   sınırsız asılabilir hâle geldi. `EgressSocketGuard.CreateHttpClient` bu yüzden
   `TimeSpan`'i **zorunlu** parametre yapar. Yeni bir SDK'ya muhafız takarken
   önce o SDK'nın istemcisinin taşıdığı `Timeout`'u ölç.
3. 🚨 **`str.replace` ile imza düzenleme iki ayrı handler'ı birden vurur.**
   Denetimin iki 🟡'si (5 ve 6) bu sınıftandır: biri parametreyi yanlış metoda
   koydu, diğeri XML dokümanı ile metot arasına girdi. Çok satırlı bir deseni
   `replace` ederken **kaç yere uyduğunu** önce say (`grep -c`), sonra uygula.
4. **`docs/hafiza/aspnetcore-di.md` %4 boş (15.297 / 16.000).** Bütçe içindedir
   ama bir sonraki faz büyük olasılıkla aşacaktır. Emsal Faz 77'de kuruldu:
   `openai-saglayici.md` aşınca **konuya göre ikiye bölündü** (SDK tuzakları /
   boru hattı tuzakları → `model-boru-hatti.md`), arşive taşınmadı. Aynısı burada
   da uygulanabilir; doğal ayrım çizgisi "uç kaydı + DI" ile "yetkilendirme +
   filtre sırası" gibi görünüyor. **Ölç, sonra kullanıcıya sor** — bu bir
   kullanıcı kararıdır.
5. **`AgentPrism:Egress` bugün tek ayar taşıyor.** İkinci ayar adayı hazır ve
   gerekçelendirilmiş: `RequireHttps` (`ADAYLAR.md` **F-132**). Ertelenme sebebi
   ölçüm eksikliğidir, tasarım belirsizliği değil — kaç kurulumun `http` MCP
   sunucusu olduğu bilinmiyor ve açık gelen bir varsayılan K-165'in önlediği
   sessiz kırılmayı üretir. Eklemek üç satırdır (`EgressAddressPolicy`'ye üçüncü
   alan); pahalı olan varsayılan kararıdır.
6. **Devralınan sözleşme: her giden yol istemcisini `EgressSocketGuard`'dan
   kurar.** Yeni bir dış çağrı yüzeyi eklerken `new SocketsHttpHandler` veya çıplak
   `new HttpClient` yazma — `guard.CreateHandler()` / `CreateHttpClient(timeout)`
   kullan. MCP tarafında ek kural: `new HttpClientTransport(...)` yerine
   `McpTransportFactory.CreateTransport(...)`; fabrika, guard `null` gelirse
   fail-closed `StrictGuard`'a düşer, yani atlanan bir çağrı yeri sessizce
   korumasız kalmaz, gürültülü şekilde fazla reddeder.
7. **`secret` çözen yeni bir alan eklersen önek kısıtı ZORUNLUDUR** (K-533) ve
   **iki katmanda** uygulanır: kaydetme ucunda + çözüm anında. Desen
   `ConfigurationKeyGuard.RequirePrefix(key, prefix, fieldName)`. Ayar iki paket
   tarafından görülmesi gerekiyorsa `Abstractions`'a koy (K-534, MCP emsali).
8. **Yarım kalan iş yok.** DoD'nin tamamı işaretlendi; B05-2/3/4/5/6/7 kapandı.
   Manuel kabul case'leri yazıldı (MT-SEC-128…130, MT-MCP-054…058); MT-MCP-058
   👤 insan gerektirir (gerçek iç ağ MCP sunucusu) ve bir sonraki tam manuel
   koşumda beklemektedir.

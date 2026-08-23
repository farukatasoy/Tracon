# Faz 33 — Sağlık Denetimi ve Yapılandırma Teşhisi

> **Durum:** ✅ Tamamlandı (2026-08-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-38** · **F-62**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok — `Microsoft.Extensions.Diagnostics.HealthChecks` paylaşılan çerçevededir · **Migration:** Yok
> **Public API:** büyüyor — Faz 7'den önce ucuz

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/33-SAGLIK-DENETIMI-VE-TESHIS.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

`MEMORY.md`'deki tuzakların çoğu **sessiz yanlış yapılandırmadır**. `TryAdd` sırası bozulursa kalıcılık sessizce devre dışı kalır. İki kalıcılık sağlayıcısı birlikte kaydedilirse yalnız bir uyarı loglanır (K-183) ve kullanıcı onu görmez. Bugün "kurulumum doğru mu?" sorusunun cevaplanacağı **tek bir yer** yoktur.

## Bitiş Ölçütleri (DoD)

- [x] `AddAgentPrismHealthChecks()` + `MapHealthChecks("/health")` kurulumunda
      `GET /health` `200 Healthy` döner — model sağlığı ısıtıldıktan sonra
      gerçek `samples/AgentPrism.Api` ile doğrulandı (aşağıda çıktı)
- [x] Veritabanı durdurulduğunda `GET /health` `503 Unhealthy` döner —
      `HealthCheckTests.Baglanti_kurulamayan_SQL_saglayicisi_503_Unhealthy_doner`
      ile doğrulandı (fonksiyonel test; gerçek Postgres kapatma manuel
      doğrulanmadı — bellek içi/SQL geçişi `ISqlPersistenceDiagnostics` üzerinden
      soyutlandığı için sahte sağlayıcı ile eşdeğerdir)
- [x] `UsePostgreSql()` ve `UseSqlite()`/`UseSqlServer()` birlikte kaydedildiğinde
      `/health` `Degraded` ve `/api/diagnostics` çift kaydı **açıkça** bildirir
      (K-183) — `HealthCheckTests.Cift_SQL_kaydi_Degraded_doner` +
      `DiagnosticsCollectorTests.Cift_SQL_kaydi_K183_sayaci_ikiyi_gosterir`
- [x] `GET /api/diagnostics` Admin ile `200`, Admin policy başarısızsa `403`
      döner — `DiagnosticsEndpointTests.Admin_policy_basarisizsa_403_alir` /
      `_basariliysa_200_alir`
- [x] 🚨 `secret` sızıntı testi: bilinen bir API anahtarı yapılandırmaya konur;
      teşhis yanıtının tamamında **hiçbir yerde** geçmez —
      `DiagnosticsEndpointTests.Bilinen_API_anahtari_yanitin_hicbir_yerinde_gecmez`
      VE gerçek `samples/AgentPrism.Api`'de `dotnet user-secrets`'taki gerçek
      OpenAI anahtarıyla elle doğrulandı (aşağıda)
- [x] Sağlık denetimi çağrısı hiçbir model isteği üretmez (sahte sağlayıcı
      sayacı sıfır) — `ModelHealthEndpointsTests` zaten bunu `ModelProviderHealthCache`
      üzerinde kanıtlıyordu; `AgentPrismDiagnosticsCollector` aynı önbellekten
      `TryPeek` ile okur, `HealthCheckTests`'in tamamı hiçbir `FakeOpenAiCompatibleServer`
      çağrı sayacını artırmadan geçti
- [x] Dört doğrulama kapısı sıfır uyarı verir — bu kapanışta tekrar çalıştırıldı
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, `/health` ve
      `/api/diagnostics` çıktısı bu belgeye yazıldı (aşağıda)
- [x] `secret` taraması boş döndü
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — **+3,3 KB gzip**
      (151,3 KB → 154,6 KB; plan tahmini 2–4 KB idi)

### Doğrulama komutları ve gerçek çıktı (2026-08-06, port 5080 — `launchSettings.json`)

```bash
$ curl -s -i http://localhost:5080/health | tail -1
Degraded
```

Model sağlığı hiç yoklanmamışken (uygulama yeni açılmış) beklenen durum budur —
bkz. bölüm 33.3. `GET /agentprism/api/models/health?refresh=true` ile bir
sağlayıcı ısıtıldıktan sonra:

```bash
$ curl -s -i http://localhost:5080/health | tail -1
Healthy
```

```bash
$ curl -s http://localhost:5080/agentprism/api/diagnostics | jq
{
  "persistenceProvider": "InMemory",
  "registeredPersistenceProviders": 0,
  "canConnect": true,
  "migrationsUpToDate": true,
  "pendingMigrations": [],
  "modelProviders": [
    { "name": "openai", "status": "Unknown", "circuitOpen": false },
    { "name": "openai-responses", "status": "Unknown", "circuitOpen": false },
    { "name": "openrouter", "status": "Unknown", "circuitOpen": false },
    { "name": "anthropic", "status": "Unknown", "circuitOpen": false },
    { "name": "google", "status": "Unknown", "circuitOpen": false }
  ],
  "configuration": [
    { "key": "AgentPrism:Providers:OpenAI:ApiKey", "resolved": true, "hint": null },
    { "key": "AgentPrism:Providers:Anthropic:ApiKey", "resolved": true, "hint": null },
    { "key": "AgentPrism:Providers:Google:ApiKey", "resolved": true, "hint": null }
  ],
  "uiEmbedded": true,
  "toolCount": 6,
  "agentCount": 11
}
```

```bash
# 🚨 Sizinti denetimi — gercek OpenAI anahtariyla, dev makinesindeki user-secrets'tan
$ KEY=$(dotnet user-secrets list --project samples/AgentPrism.Api \
      | grep -i 'OpenAI:ApiKey' | cut -d= -f2- | tr -d ' ')
$ curl -s http://localhost:5080/agentprism/api/diagnostics | grep -F "$KEY" && echo "SIZINTI VAR" || echo "temiz"
temiz
```

`UseOpenAICompatible("openrouter", ...)` sağlayıcısı `configuration` listesinde
**hiç görünmez** — bkz. K-249: sabit bir bölüm yolu yoktur, yanlış anahtar adı
raporlamak yerine hiç raporlanmaz.

---

## Plandan Sapmalar

1. **K-183 işareti Sql.Shared'dan Abstractions'a taşındı (K-247).** Plan bunu
   öngörmüyordu; uygulama sırasında keşfedildi. `internal SqlPersistenceRegistration`
   linked-source (K-176) yüzünden her sağlayıcı derlemesinde AYRI bir CLR tipiydi —
   `UsePostgreSql()` + `UseSqlServer()` birlikte çağrıldığında hiçbir
   `MigrationHostedService` diğerinin işaretini göremiyordu ve K-183'ün kendi
   uyarısı hiç tetiklenmiyordu (test kapsamı da yoktu). Yeni public
   `SqlPersistenceRegistrationMarker` (Abstractions) hem teşhisi hem eski uyarıyı
   aynı, tek derlenmiş tipten besler.
2. **`MigrationRunner` `ISqlPersistenceDiagnostics`'i doğrudan uygular** (K-248);
   planın taslağı ayrı bir sarmalayıcı ima ediyordu ama gereksizdi — `ProviderName`
   ve migration keşif/okuma mantığı zaten oradaydı.
3. **`OpenAIModelProvider`'a `configurationSectionKey` parametresi eklendi (K-249).**
   Plan `Configuration` alanını "yalnız kayıtlı sağlayıcıların beklediği anahtarlar"
   diye tarif ediyordu ama `UseOpenAICompatible()`'ın sabit bir bölüm yolu
   OLMADIĞINI (kod içinde serbestçe yapılandırılır) hesaba katmıyordu. İlk taslak
   hep `AgentPrism:Providers:OpenAI` raporlardı — yanlış olurdu. Çözüm: parametre
   `null` ise hiç raporlanmaz.
4. **`AgentPrismDiagnosticsReport` genel bir `Status` alanı taşımaz (K-250).**
   Taslak API zaten böyleydi (sapma değil) ama gerekçesi kapanışta netleşti:
   `HealthStatus` yalnız `AgentPrism.AspNetCore`'da görünür (paylaşılan çerçeve),
   `AgentPrism.Core` bu tipi hiç göremez. Üç durumlu karar tamamen
   `AgentPrismHealthCheck` içindedir.
5. **`AddAgentPrismHealthChecks()` `AddAgentPrism()`'in önceden çağrıldığını
   denetlemez (K-251).** İlk taslak `MapAgentPrism`'in `IAgentCatalog` kontrolünü
   taklit ediyordu; kayıt anında (Build() öncesi) bu kontrol sıraya bağımlı yanlış
   sonuç üretirdi (`docs/hafiza/aspnetcore-di.md`).
6. **`samples/AgentPrism.Api`'nin Faz 3'ten kalma elle yazılmış `GET /health`'i
   söküldü.** Yeni standart `AddAgentPrismHealthChecks()` + `MapHealthChecks("/health")`
   onun yerini aldı; `persistenceEnabled` yerel değişkeni de kaldırıldı (artık
   `/api/diagnostics` bu bilgiyi taşıyor). Plan bunu açıkça söylemiyordu ama
   iki paralel "kurulum sağlıklı mı" yüzeyi tutmak DoD'un "tek yer" amacına aykırıydı.
7. **`AgentPrismTestHost`'a (`AspNetCore.FunctionalTests`) `configureApp` kancası
   eklendi.** Health check testleri `app.MapHealthChecks("/health")`'i
   `MapAgentPrism`'den önce çağırmak zorundaydı; mevcut test altyapısında bu yol
   yoktu.
8. **Açık Soru 1 (SQL sağlık denetimi gerçek sorgu mu atsın) — A seçildi, plandaki
   gibi.** `GetSnapshotAsync` bağlantı açar + (varsa) `__migrations` okur; hiçbir
   önbellekleme eklenmedi (DoD'da istenmemişti, `/health` yoklama sıklığı
   tüketicinin `HealthCheckOptions.Period` ayarına kalır — .NET'in kendi
   önbellekleme mekanizması).
9. **Açık Soru 4 (teşhis ucu varsayılan) — B seçildi, plandaki gibi.**
   `EnableDiagnosticsEndpoint` varsayılan `false`.

## Bu Fazda Verilen Kararlar

K-247 — K-251. Tam gerekçe: `docs/KARARLAR.md`.

| Karar | Özet |
|---|---|
| K-247 | K-183 işareti Abstractions'a taşındı; linked-source cross-assembly kimlik hatasını da düzeltti |
| K-248 | `MigrationRunner` `ISqlPersistenceDiagnostics`'i doğrudan uygular |
| K-249 | `UseOpenAICompatible()` hiçbir `ConfigurationDiagnostic` bildirmez |
| K-250 | Genel sağlık kararı yalnız `AgentPrismHealthCheck`'te (AspNetCore), raporda değil |
| K-251 | `AddAgentPrismHealthChecks()` kayıt anında `AddAgentPrism()` kontrolü yapmaz |

## Sonraki Faza Devir Notu

- **Devralınan sözleşmeler**: `ISqlPersistenceDiagnostics` (Abstractions) — yeni
  bir SQL sağlayıcısı eklenirse `MigrationRunner`'ın zaten uyguladığı bu arayüzü
  otomatik alır; ekstra kod gerekmez. `IModelProviderConfigurationDiagnostics` —
  yeni bir model sağlayıcısı paketi (`GetConfigurationDiagnostic()`) uygularsa
  `/api/diagnostics` onu otomatik toplar; uygulamazsa sessizce atlanır (K4 uyumlu,
  hata değil).
- **🚨 `IServiceCollection` sırası kayıt-anı kontrollerini bozar (K-251)**: yeni
  bir `Add*()` uzantısı yazarken `MapAgentPrism`'in `app.Build()` sonrası kontrol
  desenini kayıt anında TEKRARLAMA — `docs/hafiza/aspnetcore-di.md`.
- **🚨 Linked-source (K-176) `internal` bir tipi `IEnumerable<T>` ile SAYMAK
  istiyorsan T Abstractions'da olmalı** — aksi halde her sağlayıcı derlemesi
  kendi ayrı CLR tipini görür ve sayım sessizce yanlış çalışır (K-247).
- **Yarım kalan/ölçülmedi**: gerçek bir Postgres container'ı durdurup
  `/health`'in `503`'e döndüğü **manuel olarak** (curl ile) doğrulanmadı — yalnız
  fonksiyonel test (sahte `ISqlPersistenceDiagnostics`) ve gerçek DB'ye karşı
  `GetSnapshotAsync` ayrı ayrı kanıtlandı, ikisinin birleşimi (gerçek DB kapalıyken
  gerçek `/health` isteği) elle denenmedi. SQL Server bu ortamda hiç çalıştırılamadı
  (Rosetta); `docs/hafiza/sql-saglayicilari.md`'deki bilinen kısıt.
- **Sıradaki faz**: `docs/arsiv/fazlar/34-TANIM-DOGRULAMA-UCU.md` (henüz yazılmadı — üçüncü tur
  yol haritasından seçilecek).

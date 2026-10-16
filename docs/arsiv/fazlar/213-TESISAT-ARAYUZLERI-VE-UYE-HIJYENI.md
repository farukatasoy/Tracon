# Faz 213 — Tesisat Arayüzleri ve Üye Hijyeni

> **Durum:** ✅ Tamamlandı (2026-10-16)
> **Plan onayı:** onaylandı — farukatasoy, 2026-10-16, "planı uygula". Kapsam kararları: **UR-011 B** ve **UR-009 B** ([`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) §7). **Sapma:** UR-011 14 arayüz saydı; ölçüm ikisini public tutmayı gerektiriyor (§213.1); onay bu sapmayı kapsar. Dört Açık Soru ayrı ayrı cevaplanmadı; uygulama dördünde de **önerilen A** seçeneğini aldı (Plandan Sapmalar §1).
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-555**, **F-556**
> **Genellik:** Her .NET tüketicisi. Donacak yüzeyde hiçbir tüketicinin uygulamadığı 12 arayüz ve DI'ın kurduğu 5 tipin public kurucusu var; 1.0'dan sonra her biri major sürümdür. İş tüketici kodunda kalamaz. İki kalem bir fazda, çünkü aynı altyapıyı paylaşır: aynı `PublicAPI.Unshipped.txt`, aynı envanter kapısı, aynı `InternalsVisibleTo` ve aynı CHANGELOG girdisi.
> **Karşı görüş:** (1) MIMARI K4 "her genişleme noktası değiştirilebilir" der; XML'i "tüketicinin kendi kaydı kazanır" diyen 8 arayüzü `internal` yapmak bu sözü daraltır. (2) K-614 / K-866'nın `IToolRegistry` için yeniden açılma koşulu doğdu ve kullanıcı yine de public tuttu (Faz 188). **Cevap:** K4 "belgelenmiş seam" anlamına gelecek şekilde daraltılır (yeni `K-*`, kapanışta). Seam'i rehber, sample ya da contract suite belgeler; bu 12 arayüzün hiçbirini hiçbiri belgelemez, uygulayanların hepsi `internal` veya test sınıfıdır (ölçüldü). `IToolRegistry` farklıdır: rehber (`write-your-own-tool.md:160`) onu anar ve değiştirmek 4 güvenlik sarmalayıcısını düşürür; bu yüzden "çağrılır, uygulanmaz" listesinde kalır, `internal` olmaz.
> **Önkoşul:** [Faz 212](212-HTTP-DTO-DARALTMA.md) — ikisi de `PublicAPI.Unshipped.txt` ve `scripts/public-yuzey-envanteri.py` dosyalarını değiştirir; paralel koşmaz. **Tamamlandı (2026-10-16).** Başlangıç sayıları: `PublicAPI.Unshipped.txt` toplamı `8530`; `public-surface-baseline.txt` Abstractions `423`, AspNetCore `23`, Core `79`; `public-yuzey-envanteri.py --liste kanıtsız` boş ve OpenAPI şeması artık kanıt değil. Yeni HTTP DTO'su `internal` + `[Description]` doğar (devir notu: Faz 212).
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.AspNetCore`, `.Mcp`, `.Workflows`, `.Voice`, `.OpenAI`, `.UI`, `.Cli` (yalnız `InternalsVisibleTo`)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** **Küçülüyor.** `PublicAPI.Shipped.txt` bugün **0** satırdır (`find src -name PublicAPI.Shipped.txt -exec cat {} + | grep -vcE '^\s*$|^#'` → `0`); daraltma `1.0.0`'dan önce bedavadır.
> **Tüketici yüzeyi:** site: `guides/voice.md` ve `reference/configuration.md` (kalkan `BackendModel` anahtarı), `guides/write-your-own-store.md:48` (`UpdateRunCostAsync` varsayılanı), `concepts/workflows.md` ve `capabilities.md` hiç değişmez · sevk edilen: `src/Tracon.Abstractions/README.md:20` (tip sayıları), `UseUI()` ve `IWebhookStore` XML'i, `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** [`manuel-test/01-KURULUM-VE-PAKETLEME.md`](../../manuel-test/01-KURULUM-VE-PAKETLEME.md) — packed-consumer case'leri; MCP (`18`), ses (`19`) ve arayüz (`09`) alanlarına birer case

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 03cdbc93:docs/arsiv/fazlar/213-TESISAT-ARAYUZLERI-VE-UYE-HIJYENI.md
> ```
>
> Damıtıldı 2026-10-16 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Donmadan önce yüzeyden iki tür fazlalık çıkar. **Birincisi** tesisat arayüzleri: Tracon paketlerinin kendi aralarında kullandığı, hiçbir tüketicinin uygulamadığı 12 arayüz ve yalnız onların imzasında yaşayan tipler. **İkincisi** üye hijyeni: DI'ın kurduğu 5 tipin public kurucusu, hiçbir yerde okunmayan 3 public üye, çağırana derlenen bir opsiyonel `tenantId`.

## Bitiş Ölçütleri (DoD)

- [x] `python3 scripts/public-yuzey-envanteri.py --denetle` çıkış `0`; 12 arayüz ve envanterin bulduğu tüm yetim tipler `internal` — 591 tip, kanıtsız 0; 23 yetim tip
- [x] `grep -rn "IMcpToolRefresher\|IAuditDecorated\|IWebhookPublisher" src/*/PublicAPI.Unshipped.txt` boş (12 arayüzün hiçbiri public değil) — sayım `0` (12 arayüz de, 35 tip adı da)
- [x] `SingleTenantContext`, `HttpTenantContext`, `TimeoutAIFunction`, `CompiledAgentCache`, `DefaultRunAttributionContext` kurucuları `internal`; `PublicAPI.Unshipped.txt`'te kurucu satırı yok; hepsi fabrikayla kayıtlı — `PublicAPI.Unshipped.txt`'te kurucu satırı 0; kayıtlar fabrika (`ServiceRegistrationSnapshotTests` 3 satır `Factory`); `DiConstructedServiceResolutionTests` ve iki negatif kontrol (aşağı)
- [x] `OpenAILiveOptions.BackendModel`, `LiveVoiceSessionOptions.BackendModel`, `QuotaUsageQuery.AsOf` kodda, `PublicAPI`'de ve belgede yok — `grep -rn "BackendModel\|\bAsOf\b" src docs-site` boş (CHANGELOG hariç)
- [x] `UpdateRunCostAsync` imzasında `= null` yok; `RunStoreContract` açık `tenantId` geçer; `write-your-own-store.md:48` güncel — `RunStoreContract.cs` ve iki dialect testi `AmbientTenant.TenantId` / `context.TenantContext.TenantId` geçer; bench ve örnek de güncellendi
- [x] Açık Soru 1'in seçimi uygulandı ve `VoiceLiveOptionsBindingTests`'te kanıtlandı — dört ret vakası (`responses`, `Responses`, `1`, `not-a-mode`) + `CLIENT` kabulü; örnek uygulamada canlı ses kayıtlıyken `DelegationMode=Responses` host'u açılışta düşürür (çıkış 134), `BackendModel` anahtarı host'u düşürmez
- [x] Kurucu tabanı kapısı yeşil; negatif kontrol (geçici public kurucu → kırmızı) kayda geçti — negatif kontrol: `PublicAPI.Unshipped.txt`'e geçici `SingleTenantContext` kurucu satırı → `+ Tracon.Core:Tracon.SingleTenantContext: new public constructor on a concrete class` kırmızı; satır geri alınınca yeşil. Denetçi kapıyı Python'da yeniden kurdu: ölçülen 17 = taban 17
- [x] `seam-contract-baseline.txt` ve `public-surface-baseline.txt` yenilendi; `SeamContractDocumentationTests` yeşil — seam −23 satır; Abstractions 423→389, AspNetCore 23→22
- [x] K4 geçen her metin güncellendi (`grep -rn "her genişleme noktası\|every extension point" docs docs-site/src ARCHITECTURE.md README.md AGENTS.md`) — README, ARCHITECTURE, `MIMARI.md` K4, `MIMARI-GUVENLIK.md`, site `concepts/index.md` + `getting-started/index.md`; kalan tek eşleşme ("full picture, including … every extension point") başka bir anlamda
- [x] `CHANGELOG.md` `[Unreleased]`: **Removed** (her tip adıyla, `BackendModel` ×2, `AsOf`), **Changed** (`UpdateRunCostAsync`, kurucular); `kapi.py yayin --kuru` yeşil — `kapi.py yayin --kuru --surum 1.0.0-preview.5.213` yeşil: "Kırıcı liste: 127 tip, 0 TFM düşüşü, 5 paket — hepsi 'Unreleased' notunda", 20 paket, 6 exact-version sample + Native AOT + net8.0 tüketici smoke
- [x] Dört doğrulama kapısı sıfır uyarı: `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` — `kapanis --taban f3b266c3` çıkış 0, 10 adım (tarama 8,8 sn · bakım 2,0 · Python 9,9 · harita 0,1 · denetim paketi 0,3 · build 109 sn 0 uyarı · test 734 sn, 38 koşumda 23.338 test, 0 kırmızı, 19 atlanan · pack 9 · format 149 · performans 158 (`AppendEvent` 112 B) · site 48 sn, 996 sayfa); denetim düzeltmesinden SONRA koşuldu
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — Production, bellek içi depo: `POST /tracon/api/agents/summarizer/run` → SSE `200`, iki run `Completed` (`echo-1`, kiracı `default`); `GET /api/diagnostics`: `ITenantContext` → `SingleTenantContext` `isBuiltInDefault: true` (fabrika kaydı tanıyı bozmadı), `IRunAttributionContext` → örneğin `DemoRunAttributionContext` `isBuiltInDefault: false` (tüketici kaydı kazandı); `GET /tracon/api/tenants/current` → `{"tenantId":"default"}`; `POST /tracon/api/mcp-servers/refresh` → `200 {"toolCount":0}`; `GET /tracon/` → `200 text/html`, `./assets/index-*.js` → `200`
- [x] `secret` taraması boş döndü — `kapi.py tarama` temiz (17 işaretli sentetik credential atlandı)
- [x] Manuel kabul case'leri `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md`, `18-MCP-VE-A2A.md`, `19-COK-MODLULUK-VE-SES.md`, `09-ARAYUZ-GENEL.md` içine eklendi; otomatikleştirilebilenler koşuldu — `MT-PKG-165` (gerçek paketlenmiş tüketici ölçümü) · `MT-MCP-084` · `MT-MM-140` · `MT-SEC-224` · `MT-UI-058`; plan case 3'ün adresi düzeltildi (Plandan Sapmalar §11)
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — taze bağlamlı `faz-denetcisi`: 🔴 1 (gerçek, kapandı) · 🟡 2 (kapandı) · 🟢 3 (üçü işlendi) — bkz. "Denetim Bulguları"
- [x] Yeni `K-*` kayıtları açıldı: K4 daraltması (seam = belgelenmiş), K-850 seam tanımı, K-614/K-866 notu, `Responses` kararı — K-1013, K-1014, K-1015; K-850, K-614 ve K-866 satırlarına not

### Doğrulama komutları

```bash
python3 scripts/public-yuzey-envanteri.py --liste kanıtsız          # 0
python3 scripts/kapi.py tarama                                      # temiz
python3 scripts/kapi.py test --proje Tracon.Core.UnitTests --sinif "*SeamContract*" --tfm net10.0
curl -s -XPOST http://localhost:5081/tracon/api/mcp-servers/refresh # 200 (UseMcp) / 501
```

---

## Plandan Sapmalar

1. **Onay ve Açık Sorular.** Plan "onay bekler" idi; kullanıcı "planı uygula, commit atabilirsin" dedi. Dört Açık Soru ayrı ayrı sorulmadı: planın önerisi **A** dördünde de uygulandı (1: `Responses` silindi, enum `Client`; 2: kurucu tabanı kapısı bu fazda; 3: dokuz arayüze yalnız XML cümlesi; 4: `IVoiceHealthCheck` ve `IWorkflowRunner` public). Biri geri alınmak istenirse yeri bellidir: kapı ve `Responses` bağımsız commit'lerdir.
2. **3 arayüzlük prob yapılmadı; 12 arayüz birlikte `internal` yapıldı.** Probun sorusu (minimal API `[FromServices]` ile `internal` bir arayüzü çalışma anında bağlar mı, vekil sınıf kurulur mu) şöyle ölçüldü: ilk derleme sıfır hata verdi, `GovernanceEndpointTests` + MCP + Drain 88 fonksiyonel test geçti (`IMcpOAuthCoordinator?`, `IMcpToolRefresher?`, `IMcpPromptClient?`, `IMcpResourceClient?` hepsi nullable `[FromServices]`; kayıtsızken `501`). Vekil sınıf riski ayrıca ölçüldü: `StorePersistenceTests` `Substitute.For<IAgentDefinitionStore, IAuditDecorated>()` ile `internal` bir arayüzde üç vekil kurar (ilk grep tek tipli biçimi aradığı için bunu kaçırmıştı; denetim buldu) ve `DynamicProxyGenAssembly2` IVT'si (`src/Directory.Build.props`) sayesinde geçer. Risk gerçekleşmedi.
3. **Yetim tip: planda 19 benzetim, gerçek 23** (iki tur): planın saydığına `McpOperationStatus`, `McpPromptArgumentSummary`, `McpPromptContent`, `McpPromptSummary`, `McpResourceContent`, `McpResourceSummary` ve `WorkflowFunctionDescriptor` eklendi. Liste elle tutulmadı; `public-yuzey-envanteri.py --liste kanıtsız` boşalana kadar döngü koşuldu.
4. **Beş MCP özet tipi OpenAPI şemasıdır ve `[Description]` istedi.** `McpPromptArgumentSummary`, `McpPromptSummary`, `McpPromptContent`, `McpResourceSummary`, `McpResourceContent` HTTP yanıtında şema olarak yaşıyor; `internal` olunca `OpenApiDescriptionCoverageTests` 24 açıklamanın kaybını yakaladı. Faz 212 kuralı uygulandı: `<summary>` yerine `[Description]`, **aynı metin**. `docs/openapi/tracon.json` byte-aynı kaldı (`OpenApiSnapshotTests` yenilenmeden geçti). Plan "belge değişmez" diyordu — doğru; ama bu iş planın dosya listesinde yoktu. `WorkflowFunctionDescriptor` etkilenmedi: HTTP tarafı `WorkflowFunctionResponse` ile projekte eder.
5. **Seam tabanından 26 değil 23 satır düştü**, ve bir kapı ek olarak taşındı: `SeamContractDocumentationTests.A_guarantee_limit_contract_states_what_is_NOT_guaranteed` `ITraconDrainState` XML'ini arıyordu. Tüketiciye dönük cümle public ayarın (`TraconDrainOptions.Enabled`) metnine taşındı ve kapı oraya bağlandı (`"does not, by itself"`, `"backup mechanism"`).
6. **Public XML'de planın listesinden fazla yer vardı:** `TraconMcpOptions` (`McpOAuthOperationStatus.NotConfigured`), `TraconWorkflowFunctionExtensions` (`WorkflowFunctionDescriptor.SettingsSchema`), `IWorkflowRunner` (`IMcpToolRefresher`). Plandaki `IWorkflowCheckpointStore.cs:25` gerekmedi: atıf `IWorkflowRunner.ResumeStreamingAsync`'e ve o public kaldı. Tüketici-dışı `internal` arayüzlerin XML'indeki "tüketicinin kaydı kazanır" cümleleri de yeniden yazıldı (sekiz arayüz) — derleyici bunu istemez, ama yalan söyleyen bir yorum bırakmak sonraki oturumu yanıltır.
7. **Kurucu tabanı 17 giriş, planın "~14"ü değil.** Kapsam kuralı plandaki gibi (public, soyut olmayan, statik olmayan, `record` olmayan sınıf; `*Options`/`*Settings`/`*Exception` hariç). Tür (sınıf/yapı/soyut) `PublicAPI.Unshipped.txt`'ten okunamadığı için kapı paketin kendi kaynağındaki bildirimi tarar (`record struct`lar — `RunEventDraft`, `CallableAgentInfo`, `ResolvedCallableAgents`, `ConversationBranch` — ve 53 soyut sözleşme sınıfı kapsam dışı kalır); bildirimi bulunamayan tip kapsamda kalır. Üç fiyat geçersiz kılma tipi, `TraconToolAttribute` ve `JobContext`/`AttachmentContent`/`SpeechAudio`/`SpeechDialogueAudio`/`ToolApprovalContext` planın sayımında yoktu. `PublicSurfaceBaselineTests.OptionalConstructors.cs`'in değerlendirici ve bayat-silme yardımcıları paylaşıma açıldı (`EvaluateConstructorBaseline`, `RemoveStaleConstructorEntries`).
8. **`Responses` ek işleri.** `OpenAILiveProvider`'ın `switch`'i `Client => "client"` ve tanımsız değerde `ArgumentOutOfRangeException` oldu (eski `_ => "client"` sessiz düşüşü kalktı). `OptionsKeyCoverage` "test değeri varsayılana eşit" kuralı tek üyeli enum'da geçilemez: `DelegationMode` `exempt` oldu, anahtarın okunduğunu bağlayıcının reddi kanıtlar (`VoiceLiveOptionsBindingTests`). Ret `Bind` içinde, `InvalidOperationException` ile ve başlangıçta olur; `UseLiveVoice(Action<VoiceLiveOptions>)` yolunda enum'a sayı atamak hâlâ mümkündür ve o yolda ret yoktur (provider ilk oturumda `ArgumentOutOfRangeException` verir) — plan bunu istemedi, YAGNI.
9. **`bench/Tracon.Benchmarks/NoOpRunStore.cs`** `IRunStore`'u uyguluyordu ve `tenantId = null` taşıyordu (`MA0061`); planın dosya listesinde yoktu. Örnek (`samples/Tracon.Samples.FileRunStore`) ve rehber (`write-your-own-store.md:48`) planlıydı.
10. **`AspNetCore`'un hiç `InternalsVisibleTo`'su yoktu.** `Tracon.AspNetCore.csproj`'a yeni `ItemGroup` (Tracon.UI, Tracon.Ui.E2ETests) eklendi; `Tracon.Abstractions.csproj`'a `Tracon.Cli`.
11. **Manuel case 3'ün adresi yanlıştı.** Plan `GET /tracon/ui` dedi; UI önek köküne eşlenir (`GET /tracon/`, `UiEndpoints.Map` `"/"` ve `"/{**path}"`). Case buna göre yazıldı ve örnek uygulamada ölçüldü (`/tracon/` `200 text/html`, gömülü varlık `200`); `/tracon/ui` de `200` verir ama bu SPA yedek rotasıdır. Arayüzsüz derlemede (`-p:TraconFrontendEnabled=false`) uç `404` verir. Case 2'nin `501` yarısı ve case 6 örnek uygulamada ölçülemez (`UseMcp()` ve özel `ITenantContext` için `Program.cs` değişikliği ister; K-834) — fonksiyonel testlere devredildi (`Refresh_returns_501_when_Mcp_not_registered`, `A_tenant_context_the_consumer_registers_before_AddTracon_is_the_one_that_resolves`).
12. **Dokuz "çağrılır, uygulanmaz" arayüzünün XML cümlesi ilk geçişte unutuldu.** Plan bunu Amaç'ta ve §213.1'de açıkça istiyordu; uygulama yalnız sekiz `internal` arayüzün "kaydı kazanır" cümlelerini yeniden yazdı. Bağımsız denetim buldu (🔴 1, gerçek): `IAgentCatalog`, `IModelProviderRegistry`, `IJobDispatcher`, `ISpeechProviderResolver`, `IVoicePricingReader`, `IMigrationApplier`, `ISqlPersistenceDiagnostics`, `ITraconBuilder` ve `IWorkflowRunner` "Call this interface; do not implement or replace it" cümlesi aldı (sebep arayüze göre yazıldı: `ISpeechProviderResolver` için egress ve kimlik bilgisi denetimi atlanırdı); `IToolRegistry` satırı `Tracon.Abstractions/README.md`'nin "Extension points" tablosundan çıkıp "çağrılır, uygulanmaz" paragrafına taşındı. Zorlama (başlangıç reddi) eklenmedi (Açık Soru 3 A).
13. **`docs-site/.../reference/configuration.md`'de `BackendModel` anahtarı yoktu** (planın tüketici yüzeyi bunu öngörmüştü); anahtar yalnız `guides/voice.md` tablosundaydı.
14. **Tüketici yüzeyi envanteri ve site senkron gerekçesi.** Değişen yüzeyler: `docs-site/` (elle yazılan) — `concepts/index.md`, `getting-started/index.md` (K4 daraldı), `guides/voice.md` (`BackendModel` satırı kalktı, `DelegationMode` satırı), `guides/write-your-own-store.md` (`UpdateRunCostAsync` imzası); sevk edilen metin — `///` blokları (`IRunStore`, `IWebhookStore`, `WorkflowDefinition`, `TraconDrainOptions`, `UseUI`, `TraconMcpOptions`, `TraconWorkflowFunctionExtensions`, dokuz "çağrılır, uygulanmaz" arayüzü), `src/Tracon.Abstractions/README.md`, kök `README.md`, `CHANGELOG.md`; `llms-full.txt` yeniden üretildi. `capabilities.md` ve `Tracon.AgentMap.md` değişmedi (yeni yetenek, paket veya giriş noktası yok). `dokuman-bakim.py --site-denetle` altı kuralı karşılanmamış saydı ve `--site-gerekce-yazildi` ile geçildi, çünkü hedef sayfalarda değişecek bir cümle yoktur (hedefler `grep` ile tarandı): `guvenlik-kiraci` (`HttpTenantContext` kurucusu `internal`; `getting-started/security.md` ve `concepts/governance.md` tipi anmaz) · `workflow` (`TraconWorkflowFunctionExtensions` yalnız XML cümlesi) · `kalicilik` (`SqlRunStore.UpdateRunCostAsync` imzası; kalıcılık sayfası anmaz, store rehberi güncellendi) · `model-saglayici` (`OpenAILiveOptions.BackendModel` kalktı; `first-agent.md` ve `model-providers.md` anmaz, ses rehberi güncellendi) · `paket-tanimi` (`Tracon.Abstractions.csproj` yalnız `InternalsVisibleTo`) · `paket-readme` (`README.md` yalnız sayı ve çağrılır-uygulanmaz paragrafı; `packages.md` sayı vermez). Site kapıları: `npm run check` 996 sayfa, kırık bağlantı 0; `build-agent-map.mjs --check` temiz. Site **yayınlanmadı** (Adım 10 bakımcı eylemi).

## Bu Fazda Verilen Kararlar

**`docs/KARARLAR.md`'ye girenler (kategori: `public-api`):**

- **K-1013** — Seam = bir rehberin, sample'ın veya contract suite'in tüketiciye uygulatmayı anlattığı public arayüz; 12 tesisat arayüzü ve 23 yetim tip `internal`; MIMARI K4 "belgelenmiş genişleme noktası" olarak daraldı; K-850'ye seam tanımı notu, K-614'e `IToolRegistry` "çağrılır, uygulanmaz" notu (UR-011 B, kullanıcı kararı).
- **K-1014** — DI'ın ve araç boru hattının kurduğu beş tipin kurucusu `internal`, kaydı fabrika; somut sınıfın her public kurucusu `public-constructor-baseline.txt`'e gerekçeyle bağlı; `UpdateRunCostAsync` `tenantId` varsayılanı yok; `BackendModel` ×2 ve `QuotaUsageQuery.AsOf` silindi (K-866 ve K-867'nin devamı; UR-009 B, kullanıcı kararı).
- **K-1015** — `LiveVoiceDelegationMode.Responses` silindi, `Tracon:Voice:Live:DelegationMode` bilinmeyen adı başlangıçta reddeder (Açık Soru 1 A, planın önerisi).

**Faz içinde kalan yerel tercihler (`K-*` açılmadı):** kurucu tabanı anahtarı `<paket>:<tip>` (parametre sayısı yok; tek amaç tipin varlığı); ret `Bind` içinde, doğrulayıcıda değil (ham dize yalnız orada görünür); `OptionsKeyCoverage` istisnası; `ITraconDrainState` garanti-sınırı kapısının `TraconDrainOptions.Enabled`'a taşınması; `[Description]` metinleri XML özetinin birebir kopyası.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan metni uygulama sırasında değişmedi; sapmalar bu dokümanda) |
| Düzeltme turu sayısı | 5 — `MA0061` (bench `NoOpRunStore`) · yeni kapının iki derleme hatası (`MA0006`, `CS8604` statik başlatma sırası) · Core'da 5 kırmızı (iki taban, kayıt anlık görüntüsü, `OptionsKeyCoverage`, drain garanti kapısı) · `OpenApiDescriptionCoverageTests` 24 kayıp açıklama · denetim 🔴 1 |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 0 commit edilmiş (kırılmalar commit öncesi kapıda yakalandı: açıklama kaybı OpenAPI testinde, XML sözü denetimde) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz bu commit'le kapanıyor; sonraki `kusur-giderme` koşumu doldurur) |

## Denetim Bulguları

Taze bağlamlı `faz-denetcisi` (2026-10-16, taban `f3b266c3`, çalışma ağacı); kod yazmadı, derleme/test koşmadı. Triyajı kullanıcı seçti (🔴 için "gerçek").

| # | Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🔴 | Dokuz "çağrılır, uygulanmaz" arayüzü XML cümlesi almadı; `ISpeechProviderResolver` ve `IVoicePricingReader` hâlâ "a consumer's own registration wins" diyordu; `Abstractions/README.md` `IToolRegistry`'yi "Extension points" tablosuna koyuyordu | **gerçek** | Düzeltildi: dokuz arayüze cümle, iki "kaydı kazanır" cümlesi yeniden yazıldı, README tablosu düzeltildi. Dokuz dosya okundu; `ShippedDocumentationSelfContainmentTests`, `SeamContractDocumentationTests`, `StorePersistenceTests` yeşil. Bu sözü zorlayan bir kapı yoktur (yalnız okuma kapatır): **F-567** |
| 2 | 🟡 | `01` ve `09` manuel case'leri eklenmemişti | — | Düzeltildi: `MT-PKG-165` (gerçek paketlenmiş tüketici, hata kodları ölçüldü) ve `MT-UI-058` |
| 3 | 🟡 | "NSubstitute bu arayüzlerde hiç kullanılmıyor" yanlış (`StorePersistenceTests` `IAuditDecorated` ile üç vekil kurar) | — | Düzeltildi: Plandan Sapmalar §2; `StorePersistenceTests` kapanış koşumunda yeşil |
| 4 | 🟢 | `UseLiveVoice(Action<VoiceLiveOptions>)` yolunda tanımsız `DelegationMode` reddedilmiyor (`500`) | — | Devredildi: **F-565** |
| 5 | 🟢 | Kurucu tabanı gerekçeleri yalnız uzunluk denetler, doğruluk denetlemez | — | Devredildi: **F-566** |
| 6 | 🟢 | `ADAYLAR.md` F-508 `DelegationMode` sessiz düşmesini hâlâ sayıyordu | — | Düzeltildi: F-508 metni güncellendi |

**Denetçinin temiz saydığı başlıklar:** PublicAPI satırları (silinen 454, eklenen 1; kalıntı yok) · fabrika kayıtları (tanı `GetType()` ile örneğe bakar, `ImplementationType` okuyan kod yok) · kurucu kapısı (Python'da yeniden kuruldu: 17 = taban 17, test tiyatrosu değil) · `DelegationMode` reddi (tek bağlama yolu `UseLiveVoice(IConfiguration)`) · HTTP/OpenAPI (24 `[Description]` metni belgeyle birebir) · public XML (kalıntı yok) · site/README/ARCHITECTURE taraması · CHANGELOG kapsamı · imza-gövde.

**Uygulayanın ek negatif kontrolleri (denetimden bağımsız):** (a) `ITenantContext` kaydı tip tabanlı (`TryAddSingleton<ITenantContext, SingleTenantContext>()`) → `DiConstructedServiceResolutionTests` 6 vaka kırmızı (`A suitable constructor for type 'Tracon.SingleTenantContext' could not be located`); (b) fabrika `Options.Create(new TraconOptions())` kurarsa → yalnız `The_single_tenant_context_reads_the_configured_default_tenant` kırmızı; ikisi de geri alındı. (c) Kurucu kapısı: geçici satırla kırmızı, geri alınca yeşil.

## Sonraki Faza Devir Notu

**Sıradaki iş:** [`ADAYLAR.md`](../../ADAYLAR.md) **F-557** (bağımlılıkları en yeni sürüme yükselt, freeze sırası 2). Faz dokümanı henüz yok; `faz-planlama` açar. `YAYIN-HAZIRLIK.md` §4 "Freeze giriş ölçütleri" 1. madde (küçültme) **bitti**: A-74, A-75, A-79, A-78, A-80.

**Başlangıç sayıları (F-557 planı bunları ön koşul olarak yazar):** `PublicAPI.Unshipped.txt` toplamı `8077` · `public-surface-baseline.txt` Abstractions `389`, AspNetCore `22`, Core `79` · envanter toplam `591`, kanıtsız `0` · `seam-contract-baseline.txt` −23 satır · `public-constructor-baseline.txt` 17 giriş · kapanış kapısı 38 koşumda 23.338 test.

**Devralınan sözleşmeler:**

- **K-1013** — seam, uygulatmayı bir rehberin/sample'ın/contract suite'in anlattığı public arayüzdür; yeni bir tesisat arayüzü `internal` doğar. Kanıt `scripts/public-yuzey-envanteri.py --liste kanıtsız`. `IToolRegistry` ve dokuz arayüz "çağrılır, uygulanmaz"dır; XML cümlesi onları işaretler.
- **K-1014** — DI'ın veya araç boru hattının kurduğu bir sınıfın kurucusu `internal`, kaydı fabrikadır. Kapı `PublicSurfaceBaselineTests.Public_constructors_of_concrete_classes_match_the_checked_in_baseline`; yeni giriş **elle** ve gerekçeyle eklenir, `TRACON_PUBLIC_CTOR_REFRESH=1` yalnız bayat satırı siler. `UpdateRunCostAsync(…, string? tenantId, …)` varsayılansızdır.
- **K-1015** — `LiveVoiceDelegationMode` tek üyelidir (`Client`); yeni mod eklemek enum büyümesidir (A-76'nın kuralına bağlanacak). `Tracon:Voice:Live:DelegationMode` tanımsız adı açılışta reddeder.

**🚨 Tuzaklar:**

- `internal` yapılan bir HTTP gövde/yanıt tipi OpenAPI açıklamasını kaybeder: `OpenApiDescriptionCoverageTests` yakalar. Çözüm `<summary>` yerine `[Description]` (aynı metin). Beş MCP özet tipi bu fazda böyle düzeltildi.
- Arayüzsüz derlemede (`-p:TraconFrontendEnabled=false`) `UseUI()` uçları hiç bağlanmaz (`404`); arayüz kontrolü için tam derleme ister.
- `kapi.py yayin --kuru` aynı sürümün **farklı içerikli** eski adayını reddeder ve silmez. Eski aday elle silinmedi; bu faz `--surum 1.0.0-preview.5.213` kullandı (`artifacts/package/yayin/1.0.0-preview.5` 2026-10-14'ten kalmadır).
- `KARARLAR.md` bütçesi `586_000 → 692_000` yeniden kalibre edildi (Faz 212'den sonra %0 boşlukta idi; `karar-damit` yalnız 492 B taşıdı, dört satır 👤/🔁 kaynağı taşıdığı için elle bakılmalı). Bir sonraki karar da bu alarmı çalıştırırsa taşınacak içerik bulmak yerine **önce o dört satırı** (K-594, K-750, K-827, K-837) elle kısalt.
- `OptionsKeyCoverage` "test değeri varsayılana eşit" kuralı tek üyeli enum'da geçilemez; `exempt` kullan ve anahtarın okunduğunu başka bir testle kanıtla.

**Açık iş:** F-565 (`UseLiveVoice(Action)` yolu), F-566 (kurucu tabanı gerekçelerinin doğruluğu), F-567 ("çağrılır, uygulanmaz" cümlesi kapısız), **F-560** (`TraconAgentSessionStore` — public kurucu tabanda gerekçeli duruyor; ön koşul F-557). Site yayını (`faz-tamamlama` Adım 10) bakımcı eylemidir: `./scripts/site-deploy.sh` koşulmadı.

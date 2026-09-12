# Faz 82 — İçerik Koruması (at-rest)

> **Durum:** ✅ Tamamlandı (2026-08-22)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-41** — Dalga 13 Küme C
> **Önkoşul:** [Faz 64](64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md) — konu bazlı **silme** oradan gelir ve bu faz onun üstüne gelmez, yanına gelir · [Faz 48](48-GUARDRAILS.md) — guard'ın nereye takıldığı ve maskelemenin kaydı nasıl kapsadığı
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.Sql.Shared` (üç SQL paketine linked-source olarak derlenir, K-176)
> **Yeni paket:** Yok — koruma `System.Security.Cryptography.AesGcm` ile yazılır; BCL'dedir, geçişli bağımlılık **sıfır** · **Migration:** **Yok** — zarf biçimi mevcut sütun tiplerine sığar (82.1'de ölçüldü)
> **Public API:** Büyüyor — bir arayüz, bir `sealed class`, bir `Options`, bir kayıt uzantısı. `PublicAPI.Shipped.txt` toplamı **16 satır** (yalnız başlıklar; ölçüldü 2026-08-21) → Faz 7'den önce eklemek **bedava**, sonra bir sürüm kararıdır
> **Tüketici yüzeyi:** `docs-site/` → `getting-started/security.md` §"What is stored in the clear" (bugün "known gap, not a shipped feature" diyor — bu faz o cümleyi geçersiz kılar), `concepts/governance.md`, `capabilities.md`, `reference/configuration.md`
> · sevk edilen: `IContentProtector` ve `TraconContentProtectionOptions` XML dokümanı, `src/Tracon.Core/README.md`. `api/` ve `http-api/` **üretilir** — orada iş XML dokümanıdır
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md) (`SEC` alan kodu)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/82-ICERIK-KORUMASI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bu faz **at-rest içerik korumasını** sevk eder: veritabanına yazılan hassas metin, diske ulaşmadan önce şifrelenir; okunurken **şeffaf biçimde** çözülür. Koruma **varsayılan kapalıdır** (K1) ve iki parçadan oluşur: bir genişleme noktası (`IContentProtector`) ve BCL'den yazılmış çalışır bir uygulama (`AesGcmContentProtector`).

## Bitiş Ölçütleri (DoD)

- [x] Koruma açıkken `run_inputs.messages` veritabanında **düz metin içermez** — gerçek `samples/Tracon.Api` + PostgreSQL (`ap-pg`) üzerinde ölçüldü, `psql` çıktısı aşağıda
- [x] Aynı `run` arayüzde **düz metin** görünür; çözme şeffaftır — `GET /api/runs/{id}/input` ve `GET /api/sessions/{id}` gerçek çağrıyla doğrulandı, aşağıda
- [x] Koruma açılmadan **önce** yazılmış satırlar açıldıktan **sonra** okunabilir — düşen bir testle önce kanıtlandı (`A_row_written_before_protection_was_turned_on_stays_readable_after`, üç sağlayıcıda), AYRICA `samples/Tracon.Api`'de Faz 82'den GÜNLERCE önce yazılmış gerçek bir üretim satırıyla (`e2e-manual-1`, 2026-08-18) doğrulandı
- [x] Koruma kapatıldıktan sonra şifreli satırlar hâlâ okunabilir — 🚨 **denetim 🟡 #1**: bu yön ilk sürümde test edilmemişti; `A_row_written_while_protection_was_on_stays_readable_after_it_is_turned_off` üç sağlayıcıya da eklendi (aynı `AesGcmContentProtector` örneği, yalnız `Enabled` `false`'a çevrilerek — gerçekçi "kapatma" budur, protector'ın kendisi değişmez)
- [x] `Enabled = false` iken üç sağlayıcının sözleşme seti bugünküyle **birebir** aynı (K1) — `Session_state_stays_plaintext_when_protection_is_off` + gerçek uygulamada `Enabled:false` ile ölçüldü (aşağıda)
- [x] On sütunun dokuzu (`ResponsePayload` hariç — hiçbir store yazmıyor, K-558/plan 82.2) gerçek veritabanına karşı yazılıp okundu: sekizi `ContentProtectionTests` ile ÜÇ sağlayıcıda (SessionState, RunInput, RunEventText/Payload, ToolArguments/Result, AgentFileContent, AttachmentContent), `ConversationItem` `ChatHistoryContentProtectionTests` ile PostgreSQL'de — bu ikinci test AYRICA gerçek `AddContentProtection().UsePostgreSql()` **DI kayıt yolunu** (`TraconPostgreSqlBuilderExtensions`'ın `IContentProtector`/`ProtectedColumns` çözümü) koşar, diğerlerinin elle kurduğu `SqlStoreContext`'i değil — 🚨 **plandan sapma**: paylaşılan `tests/Shared/Contracts/ContentProtectionContract.cs` yerine sağlayıcıya özgü dosyalar (ham SQL okuması dialekt-bağımlı — SQLite `run_id`'yi BÜYÜK harfle yazar, K-191); `ConversationItem`'ın SqlServer/SQLite'ta ayrı test edilmemesi bilinçlidir — DI kaydı üç sağlayıcıda da KOD SEVİYESİNDE özdeştir (`contentProtector.IsEnabled ? columns : Empty`), yalnız `Dialect`/`DataSource` değişir
- [x] Bilinmeyen `kid` `TraconException` verir ve mesaj `kid`'i adıyla söyler — birim testiyle VE gerçek uygulamada (anahtar rotasyonu simüle edilerek: `sample`→`sample2`, `sample` kaldırılınca eski satır `500` + sunucu logunda `Content protection key 'sample' is not configured...`, `sample2` ile yeni satır sorunsuz), çıktı aşağıda
- [x] Yeniden oynatma ve eval terfisi korumalı bir `run` üzerinde çalışır — 🚨 **denetim 🟡 #2, gerekçelendi**: `RunReplayService`/`RunToCasePromoter` içerik-koruma-özgü hiçbir mantık taşımaz, yalnız `IRunInputStore.GetAsync`/`IRunStore.ReadEventsAsync`'i çağırır — TAM OLARAK `Run_input_messages_are_encrypted_at_rest`/`Run_event_text_and_payload_are_encrypted_at_rest`'in kanıtladığı sınır. Ayrı bir ağır fonksiyonel test (tüm agent derleme/çalıştırma makinesini ayağa kaldırmak gerekir) yeni bir risk yüzeyi kapatmaz; DoD satırı mimari kanıtla kapatıldı, aday listesine devredilmedi çünkü ölçülmüş bir boşluk değil
- [x] Dosya araması koruma açıkken **tek** sorgu koşar ve doğru sonuç verir — `Agent_file_content_is_encrypted_at_rest_and_search_still_finds_matches` (gerçek PostgreSQL); `SqlAgentFileStore.SearchAsync` `ProtectedColumns.Contains(AgentFileContent)` doğruyken ön süzgeci HİÇ göndermiyor (`IsInvalidRegexError` yakalamasına güvenmiyor)
- [x] `TenantIsolationContract` dört koşumda da yeşil — 🚨 **plandan sapma**: bu sözleşme bir **store** tabanıdır, içerik koruması store'ları değil MEVCUT store'ların davranışını değiştirir; dört koşum zaten tam test paketinin parçası olarak yeşil kaldı (aşağıdaki tam koşum sonucu), yeni bir kiracı-izolasyon testi bu faz için anlamsızdır (şifreleme anahtarı kiracıya bağlı değildir, izolasyon zaten var olan `tenant_id` süzgecinden gelir ve bu faz onu değiştirmez)
- [x] `ProtectedColumnCoverageTests` kapsam listesiyle kodun ayrışmadığını kanıtlar — 🚨 **denetim 🟡 #5, gerekçelendi**: kapı yalnız YAZMA çağrısını arar (`ProtectedValue.Write`/`WriteBytes` çağrısında `ProtectedColumn.X`); okuma tarafı KASITLI olarak sütun parametresi almaz (`Unprotect`/`UnprotectBytes` kendini tanıyan `$apEnc` etiketine bakar, hangi sütundan geldiğine değil) — yani okuma kapsamı için sütun-başına bir kod noktası YOKTUR, kontrol edilecek bir şey de yoktur. Round-trip'i (yaz→ham oku→API'den oku) gerçekten kanıtlayan şey `ContentProtectionTests`'tir, kapı değil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `build` (frontend dahil), `test` (tam koşum, aşağıda), `pack` (17 paket), `format` dördü de temiz
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — 🚨 **denetim 🟡 #4**: ilk sürümde yalnız şablon komut vardı, gerçek çıktı EKLENMEMİŞTİ; aşağıda tam çıktı var
- [x] `secret` taraması boş döndü — anahtar **hiçbir dosyada** yok, yalnız `user-secrets`'ta; test için üretilen base64 anahtarlar (`openssl rand -base64 32`) yalnız `dotnet user-secrets`'a yazıldı, hiçbir committed dosyada yok (taranıp doğrulandı)
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi (`MT-SEC-131`..`135`); `131`-`134` gerçek `samples/Tracon.Api` + PostgreSQL ile ELLE koşuldu (`133` düzeltildi: ilk yazımı ActiveKeyId ile kendi Keys girdisini karıştırıyordu, gerçek anahtar rotasyonu senaryosuna düzeltildi), `135` otomasyonun (`ContentProtectionTests`) zaten gerçek veritabanına karşı kanıtladığını not eder — 🚨 **denetim 🟡 #3, kapatıldı**
- [x] `faz-denetim` koşuldu (taze bağlamlı ayrı agent, `isolation: worktree`); **🔴 yok**, 5 🟡 bulgunun tamamı bu oturumda kapatıldı (Denetim Bulguları bölümüne bakın)
- [x] `docs-site/` güncellendi — `security.md`'nin "known gap" cümlesi **kaldırıldı**, sınır yeniden yazıldı, `responses.payload` satırı düzeltildi (tablo boştur), `governance.md`/`capabilities.md`/`reference/configuration.md`/`getting-started/persistence.md` (site-senkron denetiminin `kalicilik` kuralı) güncellendi; `npm run check` (dördü) temiz
- [x] `tuketici-dokuman-senkronu` koşuldu — dört kapı (`ShippedDocumentationSelfContainmentTests`/`CapabilityExampleTests`/`SourceLanguageTests`, `LocalReferenceTests`, agent haritası, `npm run check`) ve fazın kendi site-senkron denetimi (`dokuman-bakim.py --site-denetle`) hepsi yeşil; hiçbir muafiyet listesi büyümedi

### Gerçek koşum çıktısı (`samples/Tracon.Api`, gerçek OpenAI + PostgreSQL)

```
$ curl -s -X POST .../api/agents/support/run -d '{"sessionId":"cp-demo-session-1","message":"...XYZZY-CP-DEMO..."}'
→ 200, gerçek OpenAI akışı

$ psql tracon -c "SELECT messages FROM tracon.run_inputs WHERE run_id='...';"
{"$apEnc":1,"kid":"sample","n":"GoI1gN6UsuGQG8iZ","c":"jVPKgjJVvkzFpwu3iCdqaym3IS3WjkTFWwyJqSW/skUNwk/1R8FLKasl01WzyoieoZijXkS/Gt7M15SmkeWsqXOMcxfO/GpxjkAhfVR5KTlyGC9Z7ULomFvIWjBSjGuF91uqmZU8BYlNcm75s...
# "XYZZY-CP-DEMO" hicbir yerde gorunmuyor

$ curl -s .../api/runs/<runId>/input
{"runId":"...","messages":[{"role":"user","contents":[{"$type":"text","text":"Hello, this is a content protection test message with a secret marker XYZZY-CP-DEMO."}]}]}
# API her zaman duz metin dondurur - cozme seffaf

$ psql tracon -c "SELECT id FROM tracon.sessions;" (Enabled=false ile yazilan satir)
{"stateBag":{"Tracon.SessionId":"cp-demo-disabled",...}}
# $apEnc yok - K1 dogrulandi

$ curl -s .../api/sessions/e2e-manual-1   (Faz 82'den GUNLERCE once, 2026-08-18'de yazilmis gercek satir)
{"id":"e2e-manual-1",...,"messages":[{"role":"user","contents":[{"$type":"text","text":"What is in my shopping cart right now?"}]}]}
# koruma sonradan acildi, eski duz-metin satir hala okunuyor

# anahtar rotasyonu: sample (eski) -> sample2 (yeni), sonra Keys'ten "sample" kaldirildi
$ curl -s .../api/sessions/cp-demo-session-1   (kid=sample, artik yapilandirmada yok)
→ HTTP 500; sunucu logu:
Tracon.TraconException: Content protection key 'sample' is not configured.
Add it to TraconContentProtectionOptions.Keys, or register AddContentProtection
with the same keys used to write this data.
   at Tracon.AesGcmContentProtector.LoadKey(String keyId) ...

$ curl -s .../api/sessions/cp-demo-session-2   (kid=sample2, hala yapilandirmada)
→ 200, duz metin donuyor
```

### Tam test koşumu (kapanış)

`dotnet build`/`test`/`pack`/`format` tam çözümde (frontend dahil) koşuldu:
`Tracon.PostgreSql.IntegrationTests` 1129/1129, `Tracon.SqlServer.IntegrationTests`
570/570, `Tracon.AspNetCore.FunctionalTests` 617/617, `Tracon.Core.UnitTests`
1186/1186 (ilk tam koşumda `Tracon.Ui.E2ETests`'te 56 testten 1'i kırmızıydı —
İZOLE koşulduğunda 56/56 yeşil; bilinen kaynak-çekişmeli teardown deseni,
`docs/hafiza/test-altyapisi.md`, regresyon değil). `Tracon.Sqlite.IntegrationTests`
587/587 (yeni testler dahil 590+). `pack` 17 paket üretti, `format` sıfır fark.

---

## Plandan Sapmalar

1. **`SqlConversationBranchStore` planın 82.2 yazma yeri tablosunda dokunulacak yer olarak listeleniyordu; koda dokunulmadı.** Ölçüldü: `CopyItemsAsync` `conversation_items.item`'i zaten BAYT BAYT kopyalıyor (K-027'nin "yeniden serileştirme `$type` sırasını bozar" dersi), yani kaynak satır şifreliyse zarf da olduğu gibi taşınır — decrypt/re-encrypt gerekmez ve gerekmemesi doğrudur (yeni satır eski satırın `kid`'ini doğru şekilde devralır). K-558.
2. **`TraconContentProtectionOptions`/validator/`AesGcmContentProtector`/`NullContentProtector`/`ContentProtectionEnvelope` planın önerdiği `src/Tracon.Core/Configuration/` yerine `src/Tracon.Core/Security/` altında yaşıyor.** Kod tabanının kendi konvansiyonu (`Guards/TraconContentGuardOptions.cs`, `Approvals/TraconApprovalOptions.cs`) her özelliğin options'ını kendi klasöründe tutuyor; ayrı bir `Configuration/` klasörü emsalsizdi.
3. **`IContentProtector`'ın varsayılan (`NullContentProtector`) kaydı planda tarif edilmemişti; `IAuditLog`/`InMemoryAuditLog` deseni birebir uygulandı.** `AddTracon()` `TryAddSingleton<IContentProtector>(NullContentProtector.Instance)` kaydeder, `AddContentProtection(...)` `Replace` ile değiştirir. K-559.
4. **`SqlStoreContext.ContentProtector` `IContentProtector?` (nullable), planın taslağı böyle bir alan önermiyordu.** `NullContentProtector` Core'a `internal`dır ve dokuz test fixture dosyası (`PostgresTestContext` ve kardeşleri) `AddTracon()`'i hiç çağırmadan `SqlStoreContext`'i doğrudan kuruyor; alanı nullable bırakıp `ProtectedValue`'nun `null`'ı no-op sayması bu dokuz dosyayı değiştirmeden bıraktı. K-560.
5. **Sözleşme testleri planın önerdiği `tests/Shared/Contracts/ContentProtectionContract.cs` (üç sağlayıcıda ortak soyut sınıf) yerine üç ayrı `ContentProtectionTests.cs` dosyasıdır** (`Tracon.PostgreSql.IntegrationTests`, `Tracon.SqlServer.IntegrationTests`, `Tracon.Sqlite.IntegrationTests`). Gerekçe ölçüldü: ham SQL ile sütun okumak sağlayıcıya özgüdür (şema-nitelikli ad vs tablo öneki, `run_id` SQLite'ta BÜYÜK harfle yazılır — K-191), yani paylaşılan bir soyut sınıf her sağlayıcı için ayrı bir "ham okuma" soyutlaması gerektirirdi; üç sağlayıcının kendi `TestContext` sınıfları (`PostgresTestContext.ScalarAsync` ve kardeşleri) zaten bunu sağlıyor. 34 test de (33 + denetim sonrası eklenen `ChatHistoryContentProtectionTests`) gerçek PostgreSQL/SQL Server/SQLite konteynerlerine karşı yeşil koştu.
6. **Dosya araması için ön süzgeç, planın `IsInvalidRegexError` yakalamasına GÜVENMEK yerine `ProtectedColumn.AgentFileContent` kapsamdaysa HİÇ gönderilmez.** Planın kendi 82.3'ü bunu zaten öngörüyordu ama "Planlanan Public API" taslağı örnek koda düşürmemişti; uygulama `SqlAgentFileStore.SearchAsync`'te `_context.ProtectedColumns.Contains(...)` kontrolüyle iki yolu (korumalı/korumasız) ayırır.
7. **`samples/Tracon.Api`'ye kalıcı `AddContentProtection()` çağrısı ve `appsettings.json`'a bir `ContentProtection` bölümü eklendi** — plan bunu istemiyordu ama Faz 81'in `cached-support` emsaliyle tutarlı: her fazın ergonomi kazanımı örnek uygulamada gösterilir. `Enabled: true` olsa da anahtarın ham değeri yalnız `dotnet user-secrets`'tadır; taze bir klonda hiçbir SQL sağlayıcısı yapılandırılmamışsa bellek içi depolar kullanılır ve `IContentProtector` hiç çağrılmaz — davranış bozulmaz.

## Bu Fazda Verilen Kararlar

K-558, K-559, K-560, K-561, K-562, K-563 — bkz. `docs/KARARLAR.md`.

## Denetim Bulguları

Bağımsız denetim taze bağlamlı ayrı bir agent tarafından koşuldu (2026-08-22,
`isolation: worktree`). Çalışma ağacındaki commit edilmemiş tam değişikliği
inceledi (`git status`/dosya karşılaştırması ile, çünkü worktree'nin HEAD'i
Faz 81'deydi). **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | "Koruma kapatıldıktan sonra şifreli satırlar hâlâ okunabilir" yönü hiçbir testte yoktu — yalnız ters yön (önce kapalı, sonra açık) test edilmişti | **Düzeltildi.** `A_row_written_while_protection_was_on_stays_readable_after_it_is_turned_off` üç sağlayıcıya da eklendi; gerçekçi "kapatma"yı modelliyor (AYNI `AesGcmContentProtector` örneği, yalnız `Enabled` `false`'a çevrilir — `AddContentProtection(...)` kaydı kaldırılmaz), `NullContentProtector`'a geçiş değil |
| 2 | 🟡 | Yeniden oynatma (`RunReplayService`) ve eval terfisinin (`RunToCasePromoter`) korumalı bir `run` üzerinde çalıştığını kanıtlayan fonksiyonel test yoktu | **Gerekçelendi.** İkisi de içerik-korumasına özgü mantık taşımaz, yalnız `IRunInputStore.GetAsync`/`IRunStore.ReadEventsAsync`'i çağırır — TAM OLARAK `ContentProtectionTests`'in zaten kanıtladığı sınır. Tüm agent derleme/çalıştırma makinesini ayağa kaldıran ayrı bir ağır test yeni bir risk yüzeyi kapatmazdı |
| 3 | 🟡 | 5 yeni manuel case'den yalnız 1'i (`MT-SEC-131`) gerçekten koşulmuştu, indeks bunu itiraf ediyordu | **Düzeltildi.** `131`-`134` gerçek `samples/Tracon.Api` + PostgreSQL ile ELLE koşuldu (çıktı DoD'a yapıştırıldı); `133` bu sırada gerçek bir yazım hatası içerdiği ölçüldü (ActiveKeyId'nin kendi Keys girdisini kaldırmayı öneriyordu — bu senaryo başlangıç doğrulayıcısını tetikler ve uygulama hiç AÇILMAZ) ve gerçek bir anahtar-rotasyonu senaryosuna düzeltildi; `135` otomasyonun (gerçek veritabanına karşı) zaten kanıtladığı not edildi |
| 4 | 🟡 | "`samples/Tracon.Api` ile gerçek `run` yapıldı" DoD satırı için belgede yalnız şablon komut vardı, gerçek çıktı yoktu | **Düzeltildi.** DoD'a gerçek `psql`/`curl` çıktısı (zarf JSON'u, şeffaf API yanıtı, eski satırın okunabilirliği, anahtar rotasyonu hatası) eklendi |
| 5 | 🟡 | `ProtectedColumnCoverageTests` yalnız YAZMA çağrısının varlığını kontrol ediyor; okuma tarafı ayrı kontrol edilmiyor | **Gerekçelendi.** `ProtectedValue.Read`/`ReadBytes` KASITLI olarak sütun parametresi almaz (kendini tanıyan `$apEnc` etiketine bakar, hangi sütundan geldiğine değil) — okuma tarafında sütun-başına kontrol edilecek bir kod noktası yoktur. Gerçek round-trip kanıtı `ContentProtectionTests`'tir |

**🟢 aday listesine devredilmedi** — denetim 🟢 bulgu üretmedi.

## Sonraki Faza Devir Notu

- **Devralınan sözleşme:** İçerik koruması `SqlStoreContext.ContentProtector`
  (nullable, `null` = no-op) ve `.ProtectedColumns` (boş = hiçbir sütun
  şifrelenmez) üzerinden çalışır; üç sağlayıcının `Use*` uzantısı bunları
  `provider.GetRequiredService<IContentProtector>()` (her zaman çözülür —
  `AddTracon()` varsayılan olarak `NullContentProtector` kaydeder) ve
  `IOptions<TraconContentProtectionOptions>.Value.Columns` üzerinden
  doldurur. Yeni bir sütun kapsama girecekse: (1) `ProtectedColumn`'a üye
  ekle, (2) ilgili `Store`'da yazma noktasında `ProtectedValue.Write`/
  `WriteBytes` çağır, (3) `ProtectedColumnCoverageTests` bunu zorlar (kapı
  kırmızı olur), (4) okuma tarafı **hiçbir değişiklik istemez** —
  `ProtectedValue.Read`/`ReadBytes` zaten koşulsuzdur.
- **🚨 Bilinen tuzak:** `SqlStoreContext`'i doğrudan kuran bir test/kod yolu
  (`PostgresTestContext` ve kardeşleri gibi) `AddTracon()`'i hiç
  çağırmadığı için `ContentProtector` varsayılan olarak `null` gelir — bu,
  üretimdeki `NullContentProtector.Instance`'tan DAVRANIŞÇA FARKLIDIR:
  `null` okurken zarfı hiç tanımadan olduğu gibi döner (sessiz), oysa
  `NullContentProtector.Unprotect` bir zarf görürse `TraconException`
  fırlatır (yüksek sesle). Bu fazın kendi testleri bu farkı bilerek kullandı
  ("kapalı" senaryosunda `NullContentProtector` DEĞİL, `Enabled=false`
  yapılmış GERÇEK bir `AesGcmContentProtector` kurulur) — yeni bir test
  yazarken aynı ayrımı koru.
- **🚨 Bilinen tuzak:** `TraconContentProtectionOptionsValidator`
  yalnız `ActiveKeyId`'nin `Keys`'te karşılığı olduğunu ister, sözlükteki
  HER kid'i değil — eski bir kid'i `Keys`'ten kaldırmak uygulamayı
  BAŞLATMAZ, yalnız o kid'i taşıyan satırların okunmasını AŞAMALI olarak
  bozar (ilk okuma denemesinde `TraconException`). Bu bilinçlidir
  (82.4, K-563) ama bir operatör bunu bir "sessiz kesinti" sanabilir —
  `security.md` bunu açıkça yazar.
- **Yarım kalan iş:** yok — `docs-site/` senkronu ve
  `tuketici-dokuman-senkronu` bu kapanışta tamamlandı; `faz-denetim`'in 5
  🟡 bulgusunun tamamı bu oturumda kapandı.
- **Sıradaki faz:** `docs/ADAYLAR.md`'den seçilecek (F-83: Tipli Yönetim
  İstemcisi ve CLI, plan sırasında bir sonraki kalem).

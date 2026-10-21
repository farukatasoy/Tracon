# Faz 229 — A2A Çok Tur ve Çağıran İzolasyonu

> **Durum:** ✅ Tamamlandı (2026-10-21)
> **Kademe:** tam — güvenlik/kiracı sınırı (kiracı içinde çağıranlar arası konuşma izolasyonu) · geri dönüşü pahalı karar (kalıcı oturum kimliği biçimi)
> **Plan onayı:** farukatasoy, 2026-10-21 (seçim: izolasyon anahtarı çağıran kimliği; çok tur varsayılan açık)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-614** (yargı alanları: [`kesif/2026-10-21-maf-dalga-4.md`](kesif/2026-10-21-maf-dalga-4.md) §4)
> **Genellik:** A2A ile Tracon agent'ı çağıran her dış agent. İkinci kanıt ekosistem standardıdır: A2A'da `contextId` konuşma kimliğidir ve geçmişi sunucu saklar (MAF tartışması microsoft/agent-framework#7252, 2026-10-21'de bakıldı). Tüketici kodunda kalamaz: A2A barındırması ve oturum deposu Tracon'undur.
> **Karşı görüş:** "A2A çağıranları çoğunlukla tek atımlık görev gönderir; çok turun değeri ölçülmedi." Cevap: Tracon yanıtta `contextId`'yi geri yazar, ama geçmişi tutmaz; çağıran bir konuşma sürdürdüğünü sanır. Tek atımlık çağıran etkilenmez: yeni `contextId` yeni oturumdur. Bedel: aynı `contextId`'yi kullanan çağıran artık geçmiş token'ını da öder (belgelenir).
> **Önkoşul:** Yok
> **Paketler:** `Tracon.AspNetCore` (yalnız)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor (yeni tipler `internal`; `TraconA2AOptions`'a üye eklenmez). `wc -l src/*/PublicAPI.Shipped.txt` toplamı 17 satır (2026-10-21)
> **Tüketici yüzeyi:** site: `guides/external-agents.md` (2026-10-21'de eklenen "single turn" paragrafı ve kontrol listesi maddesi **geri alınır**, çok tur ve izolasyon anlatılır), `capabilities.md` A2A satırı ("each message is a single turn" kalkar) · sevk edilen: `UseA2A` ve `MapTraconA2A` XML belgesi, `src/Tracon.AspNetCore/README.md` A2A bölümü varsa
> **Manuel test alanı:** `docs/manuel-test/18-MCP-VE-A2A.md` — `🤖`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — yalnız grep:
   ```bash
   grep -n "K-008 —\|K-283 —\|K-337 —\|K-688 —\|K-691 —\|K-1016 —" docs/KARARLAR.md
   ```
   **K-008** (ön sürüm MAF yalnız `Tracon.AspNetCore`), **K-283** (görünmeyen oturum yok sayılır), **K-337** (dış çağrı her zaman yeni kök run açar), **K-688** ve **K-691** (oturum sahipliği; run başlatan yüzeyde `403`), **K-1016** (`TraconAgentSessionStore` köprüsü ve `isolation` bölümü)
3. Önceki fazdan devir: yok. Ölçüm [`MAF-KAPSAMA.md`](MAF-KAPSAMA.md) §0 #35 ve §8.2 C-15 (a)'dadır
4. Alan hafızası: [`hafiza/mcp-a2a-sunucu.md`](hafiza/mcp-a2a-sunucu.md) (A2A kaydı ve uç tuzakları), [`hafiza/maf-oturum.md`](hafiza/maf-oturum.md) (oturum durumu)
5. Gerektiğinde: [`MIMARI-GUVENLIK.md`](MIMARI-GUVENLIK.md) kiracı ve dış yüzey bölümü

---

## Amaç

Bir A2A çağıranı aynı `contextId` ile ikinci mesaj gönderdiğinde agent önceki
turu görür. Konuşma çağıranın kimliğine bağlıdır: aynı kiracıdaki başka bir
çağıran aynı `contextId`'yi gönderse bile o konuşmaya erişemez.

- **F-614** — A2A için anahtarlı oturum deposu, çağıran kimliğinden izolasyon anahtarı, oturum sahipliği eşliği, belge.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| Probe (gerçek host, `TraconTestHost` + `FakeModelProvider`, 2026-10-21) | Aynı `contextId` ile ikinci mesajda modelin gördüğü tek mesaj `user:second turn`; yanıt `contextId`'yi geri yazar |
| MAF `Microsoft.Agents.AI.Hosting.A2A` 1.25 decompile, `A2AServerServiceCollectionExtensions` | Oturum deposu `GetKeyedService<AgentSessionStore>(agent.Name)`; yoksa `NoopAgentSessionStore`. İzolasyon sağlayıcısı `GetService<AgentIsolationKeyProvider>()`; sağlayıcı varsa sarmalayıcı `Strict = true` |
| MAF `A2AAgentHandler` decompile | Oturum `GetOrCreateSessionAsync(contextId)`; `contextId` yoksa yeni GUID |
| [`TraconA2ABuilderExtensions.cs:56-72`](../src/Tracon.AspNetCore/A2A/TraconA2ABuilderExtensions.cs) | Her ad için yalnız `ExternalAgentProxy` anahtarlı kaydedilir (`:60`); `AgentSessionStore` kaydı yok |
| `grep -rn "AgentIsolationKeyProvider" src/` | 0 |
| [`TraconAgentSessionStore.cs:97-112`](../src/Tracon.AspNetCore/Sessions/TraconAgentSessionStore.cs) | `isolation` bölümünü `kaçışlı-anahtar::kimlik` biçimine katlar; başka bölümü reddeder |
| [`TraconEndpointRouteBuilderExtensions.cs:449-451`](../src/Tracon.AspNetCore/TraconEndpointRouteBuilderExtensions.cs) | Tüketicinin `AgentSessionStore` kaydı kazanır, yoksa `TraconAgentSessionStore` |
| [`TraconA2AExtensions.cs:140`](../src/Tracon.AspNetCore/A2A/TraconA2AExtensions.cs) | A2A grubu `ExternalInvoke` anahtarını **zorunlu** ister |
| [`TraconEndpointFilter.cs:226-247`](../src/Tracon.AspNetCore/Security/TraconEndpointFilter.cs) | Denetim aktörü: kimliği doğrulanmış kullanıcı, yoksa API anahtarı (`apikey:{id}`) |
| [`AgentSessionManager.cs:495-519`](../src/Tracon.Core/Sessions/AgentSessionManager.cs) | Sahiplik açık ve `RequireAuthenticatedOwner` iken atıf kimliği yoksa oturum açılmaz (`TraconSessionOwnerRequiredException`) |
| SQL şeması: SQL Server `0001_initial.sql:103` `id nvarchar(200)` birincil anahtar; PostgreSQL `:73` ve SQLite `:88` `text` birincil anahtar | Oturum kimliği **kiracıdan bağımsız** birincil anahtardır; SQL Server 200 karakterle sınırlıdır |
| `grep` (oturum kimliği uzunluk doğrulaması) | Tracon'da yok |

> Kanıtlar 2026-10-21 tarihinde doğrulandı.

---

## 229.1 — Akış

```mermaid
sequenceDiagram
    accTitle: A2A multi-turn session flow
    accDescr: The A2A handler asks MAF's isolation-scoped store for the session of a context. Tracon's isolation provider returns the caller identity, and Tracon's A2A store derives a fixed-length session id from tenant, agent, caller and context before it reads or writes the shared session store.
    participant C as A2A çağıranı
    participant F as TraconEndpointFilter
    participant H as MAF A2AAgentHandler
    participant I as IsolationKeyScopedAgentSessionStore (MAF)
    participant P as TraconCallerIsolationKeyProvider
    participant S as A2ASessionStore (Tracon, ajan başına)
    participant M as AgentSessionManager

    C->>F: SendMessage(contextId)
    F->>F: aktör = kullanıcı ya da apikey:{id}
    F->>H: istek
    H->>I: GetOrCreateSessionAsync(contextId)
    I->>P: GetIsolationKeyAsync()
    P-->>I: aktör
    I->>S: anahtar (contextId, isolation = aktör)
    S->>M: GetOrCreateSessionAsync(agent, "a2a:" + özet)
    M-->>H: oturum (geçmişiyle)
    H->>H: run (ExternalAgentProxy)
    H->>I: SaveSessionAsync
```

## 229.2 — İzolasyon anahtarı (karar: çağıran kimliği)

`TraconCallerIsolationKeyProvider : AgentIsolationKeyProvider` (`internal`,
`Tracon.AspNetCore`) şu sırayla okur:

1. `IAuditActorResolver.Resolve()` — kimliği doğrulanmış kullanıcı ya da API
   anahtarı aktörü (`TraconEndpointFilter` ayarlar).
2. Boşsa `ApiKeyRequestContext.Get(httpContext)` → `apikey:{id}`. Gerekçe:
   tüketici `Tracon:Audit:ActorClaimType` ayarladıysa API anahtarı aktöründe o
   claim yoktur ve 1. adım `null` döner.
3. İkisi de yoksa `null` → MAF'ın sıkı sarmalayıcısı isteği reddeder. A2A
   anahtarı zorunlu olduğu için bu dal bugün ulaşılmazdır; kapı olarak kalır.

Kayıt `TryAddSingleton<AgentIsolationKeyProvider, …>` (K4: tüketicinin kaydı
kazanır). Sağlayıcı `IHttpContextAccessor` ister; `UseA2A` onu `TryAdd` ile
kaydeder. **Doğrulanmadı — `maf-api-kesfi` ile ölçülmeli:**
`AgentIsolationKeyProvider` üzerinde `[Experimental]` olmadığı decompile'da
görüldü; sınıf `internal` kalacağı için public imzaya girmez.

## 229.3 — Oturum kimliği ve ajan başına depo

`A2ASessionStore : AgentSessionStore` (`internal`), her açık ajan adı için
`AddKeyedSingleton<AgentSessionStore>(agentName, …)` ile kaydedilir. Tüketici
aynı ad için kendi anahtarlı deposunu kaydettiyse o kazanır (`TryAdd` eşi:
anahtarlı kayıt önceden varsa eklenmez). Tüketicinin anahtarsız deposu için
Açık Soru 4.

Kimlik biçimi:

```
a2a:<SHA-256 hex( tenantId \u001F agentName \u001F isolationKey \u001F contextId )>
```

68 karakter, sabit. Gerekçeler:

| Sorun | Okunur birleşik kimlik (`kaçışlı-aktör::contextId`) | Özet |
|---|---|---|
| SQL Server `nvarchar(200)` | `contextId` ve aktör uzunluğuna bağlı; aşınca yazma hatası | Sabit 68 |
| Aynı `contextId` iki farklı açık ajanda | Aynı kimlik → `RestoreSessionAsync` başka ajanın durumunu okumaya çalışır | Ajan özetin içinde |
| Oturum kimliği kiracıdan bağımsız birincil anahtardır | Başka kiracıda aynı kullanıcı ve `contextId` → çakışma hatası | Kiracı özetin içinde |
| Yerel oturum kimliğiyle çakışma | `::` içeren yerel kimlik aynı satıra düşebilir | `a2a:` öneki + özet |
| Okunabilirlik (konsol oturum listesi) | Okunur | Okunmaz; liste ajan adını ve sahibi gösterir |

Ayırıcı `U+001F`'tir; MEMORY "görünmez karakter" dersi gereği test anahtarın
hex dökümünü doğrular. Depo `AgentSessionStoreKey`'in `isolation` dışındaki
bölümünü reddeder (K-1016 kuralı). Okuma ve yazma `AgentSessionManager`'a gider:
kiracı sınırı, durum sürümü ve sahiplik oradaki kuralla aynıdır.

🚨 Bu biçim **kalıcı veridir**: değişirse eski A2A konuşmaları yetim kalır. Biçim
faz içinde bir K kaydı alır (`kalıcı-veri`, alternatif: okunur birleşik kimlik).

## 229.4 — Oturum sahipliği eşliği

Bugün A2A oturum yazmadığı için sahiplik kuralı A2A'da hiç çalışmaz. Bu fazla
yazar. Kural öbür run yüzeyleriyle aynıdır (K-691):

- Sahiplik kapalı: davranış 229.1.
- Sahiplik açık, atıf kimliği var: oturum o kimliğe ait olur; başka kimlik aynı
  oturuma ulaşamaz (izolasyon zaten ayırır; sahiplik ikinci kapıdır).
- Sahiplik açık, `RequireAuthenticatedOwner = true`, atıf kimliği yok: çağrı
  **reddedilir** — bkz. Açık Soru 1.

## 229.5 — Belge

- `guides/external-agents.md`: 2026-10-21 "single turn" paragrafı kalkar. Yerine:
  `contextId` konuşmayı sürdürür; konuşma çağıranın kimliğine (kullanıcı ya da
  API anahtarı) bağlıdır; başka bir anahtar aynı `contextId` ile yeni konuşma
  açar; geçmiş token'ı ödenir; sahiplik açıkken atıf kimliği gerekir.
- `capabilities.md` A2A satırı düzeltilir.
- `MAF-KAPSAMA.md` §5.9 A2A satırı ve §8.2 C-15 (a) "✅ Faz 229" olur.

---

## Planlanan Public API

Büyümüyor. Yeni tipler `internal`:

```csharp
// Tracon.AspNetCore (internal)
internal sealed class TraconCallerIsolationKeyProvider : AgentIsolationKeyProvider
{
    public override ValueTask<string?> GetIsolationKeyAsync(CancellationToken cancellationToken = default);
}

internal sealed class A2ASessionStore : AgentSessionStore
{
    // GetSessionAsync / GetOrCreateSessionAsync / SaveSessionAsync → AgentSessionManager
}
```

### HTTP `endpoint`'leri

Yeni uç yok. `MapTraconA2A` uçlarının davranışı değişir (çok tur). Uç açıklaması
(`WithDescription`) "single message" yerine konuşmayı anlatır.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.AspNetCore/A2A/
├── TraconA2ABuilderExtensions.cs        (değişir: anahtarlı depo + sağlayıcı kaydı)
├── TraconA2AExtensions.cs               (değişir: uç açıklaması)
├── A2ASessionStore.cs                   (yeni)
└── TraconCallerIsolationKeyProvider.cs  (yeni)
tests/Tracon.AspNetCore.FunctionalTests/
└── A2AConversationTests.cs              (yeni)
docs-site/src/content/docs/guides/external-agents.md
docs-site/src/content/docs/capabilities.md
docs/manuel-test/18-MCP-VE-A2A.md
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| İkinci mesaj ilk turu görmez (bugünkü kusur) | Fonksiyonel (önce kırmızı) | `A2AConversationTests` — `FakeModelProvider.Requests[^1]` ilk turu içerir |
| Başka API anahtarı aynı `contextId` ile konuşmayı görür | Fonksiyonel | iki anahtar, aynı kiracı: ikinci anahtarın model isteği ilk turu içermez; hata yok, varlık sızmaz |
| Aynı `contextId` iki açık ajanda birbirinin durumunu okur | Fonksiyonel | iki ajan, aynı anahtar ve `contextId`: iki ayrı oturum, hata yok |
| Kimlik uzun aktör/`contextId` ile SQL Server sınırını aşar | Birim + Fonksiyonel | birim: kimlik 68 karakter, ayırıcı `U+001F` (hex dökümü); fonksiyonel: 4 KB `contextId` ile run tamamlanır |
| Kiracı B aynı kullanıcı ve `contextId` ile çakışma alır | Fonksiyonel | iki kiracı, aynı kullanıcı kimliği ve `contextId`: iki oturum |
| `Tracon:Audit:ActorClaimType` ayarlıyken API anahtarı aktörü boş kalır ve çağrı reddedilir | Fonksiyonel | ayar açık, yalnız API anahtarı: çağrı tamamlanır (2. adım) |
| Tüketicinin `AgentSessionStore` ya da `AgentIsolationKeyProvider` kaydı ezilir | Fonksiyonel | tüketici kaydı kazanır (K4) |
| Sahiplik açık, atıf yok, `RequireAuthenticatedOwner`: model çağrıldıktan sonra yazma düşer | Fonksiyonel | ret model çağrısından **önce**; `FakeModelProvider.Requests` boş; run satırı yok ya da `Failed` (Açık Soru 1) |
| Depo yazamazsa A2A yanıtı kaybolur | Fonksiyonel | depo `SaveSessionAsync` atar: MAF davranışı ölçülür ve belgelenir (`executionFailed` dalı) |
| İptal: çağıran bağlantıyı keser | Fonksiyonel | oturum yarım tur yazmaz; sonraki mesaj tutarlı geçmiş görür |
| Eşzamanlılık: aynı `contextId` ile iki eşzamanlı mesaj | Fonksiyonel | biri `TraconSessionConflictException` sınıfına düşer ya da sıraya girer — ölçülür, belgelenir |
| Boşaltma (drain) ve parametreli agent reddi bozulur | Fonksiyonel (mevcut) | `A2AEndpointTests` yeşil kalır |

Beş soru: iptal ✓ · eşzamanlılık ✓ · boş/aşırı girdi (`contextId` yok → MAF yeni GUID; 4 KB `contextId`) ✓ · başka kiracı ✓ · alt sistem hatası (depo) ✓.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 `🤖` | `samples/Tracon.Api`, SQLite, gerçek OpenAI, `summarizer` A2A'ya açık, iki `ExternalInvoke` anahtarı | Anahtar 1: "Benim adım Ada" (`contextId` X) → "Adım ne?" (`contextId` X) | İkinci yanıt "Ada" der |
| 2 `🤖` | aynı | Anahtar 2: "Adım ne?" (`contextId` X) | Yanıt adı bilmez; hata yok |
| 3 `🤖` | aynı | Konsol → Oturumlar | A2A oturumları ajan adıyla listelenir; kimlik `a2a:` önekli |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Sahiplik açık, `RequireAuthenticatedOwner = true` ve A2A çağrısında atıf kimliği yokken ne olur? Bugün A2A çalışır (oturum yazılmadığı için) | A: reddet (`session_owner_required`, model çağrısından önce); sürüm notu · B: o çağrı için oturumu yazma (tek tur) | **A.** B sessiz davranış farkıdır; ret öbür run yüzeyleriyle aynıdır (K-691). Sürüm notu kırılmayı adıyla anar |
| 2 | Yerel run uçlarında oturum kimliği uzunluğu doğrulanıyor mu? (SQL Server `nvarchar(200)`) | Ölç: 201 karakterlik `sessionId` ile `POST /api/agents/{name}/run` SQL Server'da | Faz içinde ölç; `500` ise `kusur-giderme` kaydı (sınıf taraması), bu fazın kapsamı değil |
| 3 | A2A oturumu saklama hedefine (`retention`) girer mi? | A: evet, öbür oturumlar gibi · B: ayrı hedef | **A**; ölç ve belgele |
| 4 | Tüketici **anahtarsız** bir `AgentSessionStore` kaydettiyse (bugün `/v1/responses` için kazanır) A2A onu kullanır mı? | A: evet; tüketici deposu `isolation` bölümünü ve uzunluğu kendisi işler · B: hayır; A2A her zaman `A2ASessionStore` | **A** (K4: tüketicinin kaydı kazanır). Sıra: adla anahtarlı tüketici deposu → anahtarsız tüketici deposu → `A2ASessionStore`. Belgede yazılır |

---

## Bitiş Ölçütleri (DoD)

- [x] Aynı `contextId` ile ikinci A2A mesajında model ilk turu görür — `A2AConversationTests.A_second_message_with_the_same_context_id_sees_the_first_turn` (önce kırmızıydı: model `["What is my name?"]` gördü)
- [x] Başka API anahtarı aynı `contextId` ile ilk turu görmez; iki agent ve iki kiracı ayrı oturum alır — `Another_api_key_…`, `Two_agents_…`, `One_user_in_two_tenants_…`
- [x] Oturum kimliği 68 karakter, `a2a:` önekli; biçim bilinen değerlerle sabit — `A2ASessionStoreTests` (ayırıcı hex dökümü yerine uzunluk önekli özet, sapma 2)
- [x] Tüketicinin `AgentSessionStore` (adla ya da anahtarsız) ve `AgentIsolationKeyProvider` kaydı kazanır — `A_session_store_the_consumer_registered_…` (üç test), `An_isolation_key_provider_the_consumer_registered_is_used`
- [x] Açık Soru 1 kararı uygulandı ve testte — `Session_ownership_refuses_…`, `Session_ownership_gives_…`, `Session_ownership_keeps_one_end_users_conversation_…`
- [x] Site: "single turn" notu kalktı; çok tur ve izolasyon anlatıldı — `guides/external-agents.md`, `concepts/sessions.md`, `capabilities.md`; `npm run check` çıkış 0 (1019 sayfa, kırık bağlantı yok, ağırlık tavanı içinde)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `kapi.py kapanis --taban b56994db` çıkış 0 (2026-10-21): restore, build 0 uyarı / 0 hata, pack, `npm run check`, `dotnet format --verify-no-changes`, 25689 test 0 başarısız (19 atlanan önceden vardı). Kapı, tamamlanmış faz dokümanı kökte kalamadığı için Durum satırı geçici olarak 🚧 iken koştu; `faz-kapat` sonrası `dokuman-bakim.py --denetle` yeniden koştu
- [x] `samples/Tracon.Api` ile gerçek A2A konuşması (case 1–2) yapıldı — SQLite, `gpt-5.4-mini`, 2026-10-21: anahtar 1 ikinci turda `- Your name is Ada.`, anahtar 2 `- I don’t have access to your name unless you tell me.`; `GET /api/sessions` iki satır, 68 karakter, `agentName: summarizer`; günlükte `Npgsql` satırı 0
- [x] `secret` taraması boş döndü — `kapi.py kapanis` içinde `tarama`
- [x] `🤖` case `docs/manuel-test/18-MCP-VE-A2A.md` içinde (`MT-MCP-090`) ve koşuldu; gerisi otomatik testte
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. *Denetim Bulguları*

### Doğrulama komutları

```bash
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif A2AConversationTests A2AEndpointTests A2ARunModeTests
python3 scripts/kapi.py kapanis --taban <faz öncesi commit>
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| İzolasyonsuz depo eklenir (yalnız 229.3 uygulanır, 229.2 unutulur) | Sağlayıcı ve depo aynı `UseA2A` dalında kaydedilir; "başka anahtar" testi ilk yazılan testlerdendir |
| Özet biçimi sonradan değişir, konuşmalar yetim kalır | K kaydı (`kalıcı-veri`); biçim tek yardımcıda, birim testi sabitler |
| `AsyncLocal` aktör MAF handler'ına akmaz | `TraconEndpointFilter` `next` çağrısından önce ayarlar; fonksiyonel test aktörü doğrular (birim testi kanıtlamaz) |
| MAF anahtarlı depoyu `IsolationKeyScopedAgentSessionStore` ile sarmaz (`GetService` dalı) | Fonksiyonel test anahtarın `isolation` bölümüyle geldiğini doğrular |

---

<!-- ============================================================
     AŞAĞISI ÇALIŞIRKEN DOLAR — doküman kapanışta silinir (`faz-kapat`).
     Kalıcı kısım `faz-tamamlama` Adım 5'te yerine taşınır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **Çağıran kimliği (229.2): plan "A2A anahtarı zorunlu, `null` dalı ulaşılmaz" diyordu; yanlıştı.** `RequireApiKeyScope(ExternalInvoke, mandatory: true)` yalnız `AllowRemoteAccess` iken zorlanır. Loopback'te anahtarsız, token'sız çağrı geçer ve mevcut `A2AEndpointTests`'in tamamı böyle çalışır. Plandaki sağlayıcı o çağrıya `null` dönerdi, MAF'ın strict deposu reddederdi. Çözüm: tür önekli kimlik — `user:` · `apikey:` · `static-token` · `anonymous` (K-1053). Sıra da değişti: plan "denetim aktörü, sonra API anahtarı" diyordu; `ActorClaimType` ayarlıyken anahtar aktörü boş okunur ve `apikey:x` değerli bir kullanıcı claim'i anahtarla çakışırdı. Kimliği doğrulanmış ama okunamayan kullanıcı `null` alır (reddedilir), `anonymous`'a düşmez.
2. **Oturum kimliği (229.3): ayırıcı `U+001F` yerine uzunluk önekli parçalar** (K-1052). `contextId` çağıran girdisidir; ayırıcıyı taşıyıp parça sınırını kaydırabilir. Test, hex dökümü yerine bilinen değerleri sabitler (Python ile bağımsız hesaplandı).
3. **Plan "oturum kimliği kiracıdan bağımsız birincil anahtardır" diyordu; üç dialect'te de yanlıştı.** `sessions` anahtarı (kiracı, kimlik)'tir: SQLite 0006, PostgreSQL 0018, SQL Server 0006. Kiracı özetin içinde kaldı ama nedeni değişti: kimlikle tek kiracı arayan yollar (`GetOwnerTenantIdAsync`). "Başka kiracıda aynı kullanıcı çakışma alır" hata modu yoktu; test kimliklerin farklı olduğunu sınar (özetten kiracıyı çıkaran mutasyon kırmızı verdi).
4. **`A2ASessionStore` `DelegatingAgentSessionStore` türevidir ve iç depo `TraconAgentSessionStore`'dur** (plan: doğrudan `AgentSessionManager`). Kimliği yeniden damgalama ve sahiplik kuralları tek yerde kalır.
5. **`ExternalAgentProxy.SerializeSessionCoreAsync` boşaltma kapısından çıkarıldı** (planda yoktu). Oturum turdan sonra yazıldığı için kapı, boşaltma başlarken biten turun yanıtını ve konuşmasını kaybettirirdi. Test: `A_turn_in_flight_when_the_process_starts_draining_is_answered_and_kept`.
6. **Açık Soru 1 (229.4): açma yolu için ek kod gerekmedi, sürdürme yolu için gerekti.** `AgentSessionManager.GetOrCreateSessionAsync` sahipliği zaten model çağrısından önce çözer (`ClaimOwnerId`); A2A işleyicisi oturumu koşudan önce açtığı için ret doğal olarak öndedir. Planın "sahiplik ikinci kapıdır" cümlesi ise kendiliğinden gerçekleşmedi: bağımsız denetim (🔴1) şunu gösterdi — `AgentSessionManager` mevcut oturumu sahibini doğrulamadan geri yükler, kapı yalnız HTTP uçlarındadır (`SessionOwnershipGate`). Tek bir `ExternalInvoke` anahtarı ağ geçidi olarak U1 ve U2 adına konuşursa izolasyon anahtarı aynıdır ve U2, U1'in geçmişini modele taşırdı. `A2ASessionStore`, mevcut bir oturumu açmadan önce aynı kapıyı çalıştırır (ret model çağrılmadan; neden günlükte, çağıran opak `-32603`). A2A SDK hatayı opak `-32603`'e çevirir (K-1054).
7. **Sağlayıcı kaydı genel** (`TryAddSingleton<AgentIsolationKeyProvider>`): MAF'ın `AddA2AServer` ile kurulan her sunucusu, sağlayıcı varsa deposunu ve görev deposunu `Strict` sarar; MAF'ın bellek içi Responses servisi (`AddOpenAIResponses`) de onu okur. Tracon o servisi kullanmaz (`src/`'de 0), bu yüzden `/v1/responses` değişmedi. Uygulama kendi MAF A2A ucunu `UseA2A` ile yan yana barındırıyorsa o uç da sağlayıcıyı kullanır: Tracon süzgeci koşmadığı için ambient denetim aktörü boştur, bu yüzden sağlayıcı kimliği doğrulanmış kullanıcıyı aynı kurallarla isteğin kendi `User`'ından da okur (denetim 🟡4; `AmbientAuditActorResolver.ReadActor`).
8. **Açık Soru 2 (201 karakterlik `sessionId`) ölçülemedi:** ortamda SQL Server yok. `ADAYLAR.md`'ye F-622 olarak yazıldı.
9. **Manuel case 3 (Konsol → Oturumlar) ayrı case olmadı:** davranış otomatik testte (`A_conversation_is_stored_under_an_a2a_identity_that_names_its_agent`); gerçek örnek uygulamada `GET /api/sessions` ile de görüldü (iki satır, 68 karakter, `a2a:` önekli). Konsol sayfasının kendisi açılmadı (örnek uygulama arayüzsüz derlendi).

10. **Onay bekleyen tur oturuma yazılır** (denetim 🟡3, ölçüldü). Başlangıç denetiminden sonra kaydedilen bir tanım onay isteyen tool taşıyabilir. MAF işleyicisi oturumu tur hata verse de kaydeder; çağıran opak `-32603` alır, reddedilen tur konuşmada kalır. Sonraki mesaj sorunsuz geçer: MAF cevapsız onayı kendisi reddeder ve geçmişte aracın bir sonucu vardır (`tool` mesajı). Davranış testle sabitlendi, kod değişmedi.
11. **`SendStreamingMessage` kenarı ölçülmedi** (denetim 🟢7): kopan bağlantıda yürütme ayrık sürerse `HttpContext` yoktur ve oturum yazımı sağlayıcı `null` döndüğü için düşebilir. `ADAYLAR.md` F-623.
12. **Kapanış notu — site senkron kapısı:** `dokuman-bakim.py --site-denetle` `http-api` kuralını `A2ASessionStore.cs` tetikledi diye kırmızı yazar. A2A uçları OpenAPI belgesinde değildir (`docs-site/.../external-agents.md` son paragraf), bu yüzden `http-api/` sayfaları ve `docs/openapi/tracon.json` değişmez; kapı `--site-gerekce-yazildi` ile geçilir. `cekirdek-kavram` kuralı `concepts/sessions.md` (A2A bölümü) ile karşılandı. Davranış değişimi `guides/external-agents.md` ve `capabilities.md` ile de yazıldı.

## Bu Fazda Verilen Kararlar

- **K-1052** (`kalıcı-veri`): oturum kimliği `a2a:` + SHA-256 özeti, uzunluk önekli parçalar.
- **K-1053** (`güvenlik`): çağıran tür önekli kimlik; kimliksizler `anonymous` alanını paylaşır; okunamayan kullanıcı reddedilir.
- **K-1054** (`güvenlik`): Açık Soru 1 → A (reddet, model çağrılmadan); sürdürmede de başkasının konuşması reddedilir.
- Açık Soru 3 → A: A2A oturumu öbür oturumlar gibi `retention` hedefine girer. Kodla okundu (`RetentionTargets.Sessions` tabloyu kimlik önekine bakmadan temizler); `a2a:` satırıyla ayrıca koşulmadı.
- Açık Soru 4 → A: sıra adla anahtarlı tüketici deposu → anahtarsız tüketici deposu → `A2ASessionStore`; üç yol da testte.

Açık Soru 1, 3 ve 4 planın önerisiyle kapandı; kullanıcı ayrıca yanıtlamadı.

## Süreç Ölçümü

> Kapanışta doldurulur.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 12 (Plandan Sapmalar; plan metni ayrıca yeniden yazılmadı) |
| Düzeltme turu sayısı | 1 (denetim sonrası) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 0 (tam functional proje: 2493 testin 2493'ü geçti, denetimden önceki koşum) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

Bağımsız denetçi (`faz-denetcisi`, taze bağlam) 1 🔴, 5 🟡, 4 🟢 buldu; sapma yargıları: 1, 2, 3, 4, 5, 8, 9 kabul; 6 ve 7 kısmen.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | Mevcut oturum sürdürülürken sahiplik kapısı çalışmıyor; tek `ExternalInvoke` anahtarı ağ geçidi olarak iki kullanıcı adına konuşursa U2, U1'in geçmişini alır | **Kapandı:** `A2ASessionStore` mevcut oturumu açmadan önce `SessionOwnershipGate.CheckRunSessionAsync` çalıştırır; test `Session_ownership_keeps_one_end_users_conversation_…`; kapıyı kaldıran mutasyon kırmızı verdi. Sapma 6, K-1054, site, CHANGELOG, XML belgesi güncellendi |
| 2 | 🟡 | Tür öneki ve `static-token` testsiz | **Kapandı:** `The_caller_is_named_by_kind_…` (kimliği `apikey:<id>` olan kullanıcı), `The_static_auth_token_is_its_own_caller_…`; önek ve statik token mutasyonları kırmızı verdi |
| 3 | 🟡 | Onay bekleyen tur oturuma yazılır | **Ölçüldü, kapandı:** tur konuşmada kalır, MAF onayı reddeder, sonraki mesaj geçer. Test `A_turn_that_ends_for_a_pending_approval_…`, sapma 10, site notu |
| 4 | 🟡 | Genel sağlayıcı MAF'ın kendi A2A uçlarında ambient aktöre bağlı | **Kapandı:** sağlayıcı kullanıcıyı isteğin `User`'ından da okur (`AmbientAuditActorResolver.ReadActor`); `TraconCallerIsolationKeyProviderTests`; sapma 7 |
| 5 | 🟡 | `A2ASessionStore` XML belgesi yanlış kiracı gerekçesini taşıyor | **Kapandı:** belge koda göre yazıldı |
| 6 | 🟡 | Sapma 8 `ADAYLAR.md`'ye yazıldı diyor, yazılmamıştı | **Kapandı:** F-622 |
| 7 | 🟢 | `SendStreamingMessage` kopan bağlantı kenarı | `ADAYLAR.md` F-623 |
| 8 | 🟢 | CHANGELOG her mesajın oturum bıraktığını söylemiyor | **Kapandı:** CHANGELOG cümlesi eklendi |
| 9 | 🟢 | Ayırıcı testi uzunluk önekini ayırt etmiyor | **Kapandı:** `Parts_that_concatenate_to_the_same_text_stay_apart` |
| 10 | 🟢 | A2A/MCP `IRunAuthorizationHandler` çağırmaz | `ADAYLAR.md` F-624 |

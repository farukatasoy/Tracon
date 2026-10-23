# Keşif Turu — 2026-10-22 · MAF yetim dilimleri

> Bu bir **koşum kaydıdır**, spec değildir. Sıcak yolda değildir ve baştan sona
> okunmaz. Onaylanan kalemlerin tek satırı [`ADAYLAR.md`](../ADAYLAR.md)
> içinde yaşar; bu dosya yalnız ölçümü ve eleme gerekçesini taşır.

**Tetikleyen:** Bakımcı: "Aday numarası olmayan dilimleri ölçerek adaylara alabiliriz gerekirse" (2026-10-22).
**Zemin:** Faz 234 kapalı · açık faz yalnız 193 · sıradaki numara F-656 · MAF Dalga 0–4'ün planlı işi kapalı, Dalga 5'in sekiz kararı uygulandı (Faz 231–234).
**Ekosistem taraması:** 2026-10-22 · web erişimi var · NuGet: MAF 1.25.0, MEAI 10.11.0, MCP 2.3.0, OpenAI 2.14.0, VectorData 10.11.0, `AGUI.Server` 1.0.1 en yeni (tetik yok). dotnet/extensions#7863 açık.

---

## 1. Ölçülen zemin (Aşama 0)

| Kaynak | Bulgu |
|---|---|
| `maf-kapsama.py denetle` | Yeşil: temas taze, `✗` 50 (taban), özet = §5 sayımı |
| `MAF-KAPSAMA.md` §8–§9 | Dokuz dilimin F-ID'si yoktu: D-10, C-04, C-14 (d), C-14 (e), C-14 sunucu prompts/resources, C-18 (b), C-15 (c), C-19 (c), X-1 |
| Reddedilenler indeksi | Dilimlerin hiçbiri reddedilenlerde yok. K-058 (stdio yok) karar defterindedir; yeniden açılma koşulu "Tracon izole servis olursa" |
| Kayma ölçümü (aynı gün) | §5.1 mesaj kaynağı satırı ❌ diyordu; Faz 233 değeri okur (`ConversationMemoryScopeProvider.cs:79`). `temas` göremedi: okuyan üye (`GetAgentRequestMessageSourceType`) satırda yoktu → satır 🟡, üye satıra eklendi. Paket envanterinin 16 `degerlendirilmedi` satırı `karar` oldu |

## 2. Ham liste ve eleme (Aşama 2)

Ham liste matrisin yetim dilimleridir. Bakımcının elemesi yok: istek "ölç, gerekirse al" idi. Tek karar sorusu D-10'du (§7).

| # | Dilim | Sonuç |
|---|---|---|
| 1 | D-10 stdio MCP, yalnız kod kaydı | ✅ F-656 (Doğrudan: rehber tarifi; K-058 korunur 👤) |
| 2 | C-14 (d) CIMD OAuth | ✅ F-657 (tetik bekler) |
| 3 | C-14 (e) MCP task istemcisi | ✅ F-658 (tetik bekler) |
| 4 | C-18 (b) bildirimsel topoloji | ✅ F-659 (tetik bekler; ürün ilkesi 👤) |
| 5 | C-15 (c) A2A HTTP+JSON | ✅ F-660 (tetik bekler) |
| 6 | C-04 `AIAgent.AsIChatClient` | ⏸ tüketici kodunda kalır (§4) |
| 7 | C-14 sunucuda prompts/resources | ⏸ sunulacak Tracon nesnesi yok (§4) |
| 8 | C-19 (c) uzak agent sınıfları | ⏸ Foundry kısmı F-638'de; kalanı fabrika kapısıyla K1 (§4) |
| 9 | X-1 ön sürüm uydu hattı | ⏸ K-1037 zaten kurar: ilk uydu paketin fazı (§4) |

## 3. Ekosistem taraması (Aşama 3.2)

| Kaynak | Bakılan tarih | Ne değişti | Tracon'e etkisi |
|---|---|---|---|
| CIMD yazıları ([den.dev](https://den.dev/blog/mcp-november-authorization-spec/), [WorkOS](https://workos.com/blog/client-id-metadata-documents-cimd-oauth-client-registration-mcp), [SSOJet](https://ssojet.com/blog/mcp-authorization-spec-2026-07-28-what-changed.md)) | 2026-10-22 | MCP 2025-11-25 CIMD'yi tercih eder (SEP-991), DCR MAY'e iner; 2026-07-28 DCR'yi kullanımdan kaldırır. İkincil kaynaklar; birincil spec açılmadı | F-657: elle kayıt sunmayan sunucuya Tracon bağlanamaz |
| MCP tasks ([C# SDK belgesi](https://csharp.sdk.modelcontextprotocol.io/v2/concepts/tasks/tasks.html), [2026-07-28 RC](https://blog.modelcontextprotocol.io/posts/2026-07-28-release-candidate/), [fastmcp-tasks](https://simple-repository.app.cern.ch/project/fastmcp-tasks)) | 2026-10-22 | Tasks uzantıdır (SEP-2663), istek başına katılım ister; sunucu uygulamaları erken | F-658 tetik bekler |
| A2A v1.0 ([Tyk](https://tyk.io/learning-center/a2a-protocol-architecture-and-technical-specification/), [chatforest](https://chatforest.com/guides/a2a-protocol-v1-production-ready/)) | 2026-10-22 | Üç bağlama: JSON-RPC (en yaygın), gRPC, HTTP+JSON. İkincil kaynaklar | F-660 tetik bekler |
| stdio ve uzak taşıma ([TrueFoundry](https://truefoundry.com/blog/mcp-stdio-vs-streamable-http-enterprise), [WATI](https://www.wati.io/en/blog/mcp-stdio-vs-streamable-http/)) | 2026-10-22 | Yerel istemciler stdio kullanır; ekip ve uzak kullanım Streamable HTTP. Sayısal pay bulunamadı | F-656: MAF örneklerinden gelen ekip stdio bekler |
| MCP C# SDK 2.3.0 decompile | 2026-10-22 | `ClientOAuthOptions.ClientId` boşsa SDK DCR dener; `ClientMetadataDocumentUri` var. MRTR döngüsü `McpClientImpl.SendRequestAsync` içindedir (en çok 10 tur); `requestState` dışarı verilmez; elicitation handler'ı yeteneği kendiliğinden bildirir | F-657 kanıtı; F-617 planının girdisi |

## 4. Derinleşen kalemler (Aşama 3)

### D-10 · stdio MCP

**Kanıt seviyesi:** Ölçüldü — `McpServerDefinition.cs:47-53` (kiracı zorunlu, kayıt veridir); `TraconMcpBuilderExtensions` kodla sunucu kaydı sunmaz; `dump-api.sh`: `McpClientTool : AIFunction`, `StdioClientTransport` `ModelContextProtocol.Core`'da.
**Mercek:** 1, 6.
**Eleyici sınır kontrolü:** K2 korunur (süreci tüketici kodu başlatır). Yeni paket yok.
**Karşı görüş:** Ortak süreç kiracılar arası durum paylaşır; tarif bunu açıkça uyarmazsa tüketiciye risk taşır.
**Sonuç:** 👤 K-058 korunur, rehber tarifi → F-656 (Doğrudan). Birinci sınıf `AddMcpServer` açılmadı.

### C-04 · `AIAgent.AsIChatClient`

**Kanıt seviyesi:** Ölçüldü — MAF 1.25 decompile: `AIAgentChatClient` `ChatOptions`'ı `ChatClientAgentRunOptions` ile geçirir (K-1039 kuralı uygulanır); oturumsuz modda geçmişi çağıran verir. Tüketici `IAgentCatalog.ResolveAsync` ile Tracon agent'ını alır ve uzantıyı kendisi çağırır. Davranış probu koşulmadı.
**Eleyici sınır kontrolü:** MAAI001 → çekirdek public imzaya giremez (K-1036).
**Karşı görüş:** —
**Sonuç:** Yazılmadı: tüketici kodunda kalır; Tracon'un yapacağı iş yok. Rehber satırı ancak probe ile.

### C-14 · sunucuda prompts/resources

**Kanıt seviyesi:** Ölçüldü — `src/Tracon.AspNetCore/McpServer/` yalnız tool listeler ve çağırır; Tracon'da prompt kataloğu yok (`Abstractions`'ta prompt deposu yok).
**Sonuç:** Yazılmadı: sunulacak nesne yok. Skill'i MCP kaynağı olarak sunmak F-618'in (istemci) tersidir; talep gelirse onunla birlikte düşünülür.

### C-19 (c) · Copilot Studio, GitHub Copilot

**Kanıt seviyesi:** İddia — ikisi de `AIAgent`'tır (MAF-KAPSAMA §8.3, okuma); A2A tarifi (rehber, P3 probu) aynı kapıyı kullanır. GitHub Copilot yerel süreç başlatır.
**Sonuç:** Yazılmadı: Foundry kısmı F-638'dedir; kalanı fabrika kapısıyla K1. Envanter notu güncellendi.

### X-1 · ön sürüm uydu hattı

**Sonuç:** Yazılmadı: K-1037 hattı ilk uydu paketin fazına bağlar (F-630, F-633, F-636, F-637 açılırsa). Ayrı kalem iki yerde tutmak olur.

## 5. Üç kanalın çıktısı (Aşama 1)

### Kanal 1 — yeni aday

| F-NN | Başlık | Aday dosyasına yazıldı mı |
|---|---|---|
| F-656 | stdio MCP sunucusunu kodla bağlama tarifi | ✅ Doğrudan |
| F-657 | MCP OAuth'ta istemci kimliği belgesi (CIMD) | ✅ Tetik bekleyenler |
| F-658 | MCP task istemcisi | ✅ Tetik bekleyenler |
| F-659 | Bildirimsel workflow topolojisi | ✅ Tetik bekleyenler |
| F-660 | A2A HTTP+JSON bağlaması | ✅ Tetik bekleyenler |

### Kanal 2 — kusur

| Bulgu | Kanıt | Kullanıcıya söylendi mi | `kusur-giderme` koşuldu mu |
|---|---|---|---|
| — | Kusur bulunmadı. Not: HTTP doğrulaması `oauthClientId`'yi zorunlu tutar; `IMcpServerStore`'a kodla yazılan kimliksiz kayıtta SDK DCR dener (okuma, ölçülmedi). Dönen `secret` yalnız bellekte kalır; K-059 ihlali değildir | — | — |

### Kanal 3 — yeniden açılması önerilen karar

| K-NNN | Kararın gerekçesi | Neyin değiştiği | Kullanıcının kararı |
|---|---|---|---|
| K-058 | Arayüzden süreç başlatmak K2'yi bozar | Kodla kayıt K2'yi bozmaz (D-10) | 👤 2026-10-22: korunur; rehber tarifi yeter |

## 6. Reddedilenler

| Fikir | Ret gerekçesi | Kalıcı mı | Nereye yazıldı |
|---|---|---|---|
| Birinci sınıf kodla stdio MCP kaydı (`AddMcpServer`) | 👤 K-058 korunur | Hayır (talep ve kiracı başına süreç tasarımı gelirse) | Yalnız burada |
| C-04, C-14 prompts/resources, C-19 (c), X-1 | §4 | Hayır | Yalnız burada |

## 7. Kullanıcıya sorulanlar ve cevapları

| Soru | Cevap |
|---|---|
| D-10: K-058'e ne olsun? | K-058 korunur + rehber (öneri kabul, 2026-10-22) |

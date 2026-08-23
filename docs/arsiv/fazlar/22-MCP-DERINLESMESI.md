# Faz 22 — MCP Derinleşmesi: Prompts, Resources ve OAuth

> **Durum:** ✅ Tamamlandı (2026-08-04)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-28**, **F-29**
> **Önkoşul:** Yok (Faz 9 önerilir — uzak içerik almak Admin yetkisidir)
> **Paketler:** `AgentPrism.Mcp`, `.Abstractions`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** `0013_mcp_oauth.sql` · **Yeni test projesi:** `AgentPrism.Mcp.UnitTests`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/22-MCP-DERINLESMESI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz 6 MCP'nin yalnız **tool'larını** kullanıyordu. Bu faz protokolün diğer yarısını ekledi — prompts (22.1) ve resources (22.2) — ve kimlik doğrulamayı statik bir `Authorization` başlığının ötesine, OAuth'a taşıdı (22.3). ---

## 🚨 Plandan Sapmalar

### 1. "Mod 0" (OAuth istemci kimlik bilgileri) teknik olarak imkânsız çıktı

Faz dokümanının ilk taslağı iki OAuth modu varsayıyordu: Mod 0 (istemci
kimlik bilgileri, kullanıcı etkileşimi yok) ve Mod 1 (yetkilendirme kodu,
etkileşimli). Kullanıcı önce "yalnız Mod 0" seçti — Mod 1'in karmaşıklığından
kaçınmak için.

Uygulama sırasında `ModelContextProtocol.Core` 2.0.0'ın `ClientOAuthOptions`
tipini derlerken **`RedirectUri` zorunlu bir alan** olduğu ve SDK'nın XML
belgesinin `AuthorizationCallbackHandler` verilmezse *"varsayılan uygulama
kullanıcıdan tam yönlendirme URL'sini elle girmesini ister"* dediği ortaya
çıktı. **Bu SDK sürümü yalnızca Authorization Code (+PKCE) akışını
destekler — client_credentials tarzı, kullanıcı etkileşimsiz bir mod
SDK'da hiç yoktur.** Doğrulanmış API bölümündeki "Mod 0" tanımı bu yüzden
yanlış bir varsayımdı.

Kullanıcıya bulgu sunuldu; üç seçenek arasından **"Mod 1'i şimdi yap"**
seçildi (önceki kararın tersine çevrilmesi). `McpOAuthAuthorizationMode`
enum'u bu yüzden **tek üyelidir**: `AuthorizationCode = 0`.

### 2. Bir JSON serileştirme hatası DoD doğrulamasında yakalandı

`OAuthEnabled`, `OAuthClientId` vb. alanlar ilk yazımda `[JsonPropertyName]`
taşımıyordu. System.Text.Json'ın camelCase politikası yalnız **ilk** harfi
küçültür; "OAuth" iki büyük harfle başladığı için varsayılan çıktı
`oAuthEnabled` oluyordu (beklenen `oauthEnabled` değil). Birim ve işlevsel
testler bunu **yakalamadı** çünkü ASP.NET Core'un istek gövdesi bağlama
varsayılanı büyük/küçük harfe duyarsızdır — yalnızca **yanıt** tarafı
etkileniyordu. Hata, örnek uygulamaya gerçek `curl` isteği atılıp yanıt JSON'ı
gözle incelenince ortaya çıktı (bu protokolün Adım 2'sinin tam amacı budur).
Düzeltme: her OAuth alanına `[JsonPropertyName("oauthXxx")]` eklendi. Ders
`docs/hafiza/aspnetcore-di.md`'ye yazıldı.

### 3. Prompt "aktarma" düğmesi panoya kopyalama olarak uygulandı

Doküman "Prompt'tan agent'a aktarma düğmesi" ve "sunucudaki prompt
değişince arayüz rozet gösteriyor" davranışını öngörüyordu. Bunun tam
sürümü, Agents ekranının düzenleyicisine `AgentDefinition.Metadata` içine
`mcp.prompt.server` / `.name` / `.hash` yazan ve kayıtlı özeti sunucudaki
güncel özetle karşılaştırıp rozet gösteren bir entegrasyon ister — ayrı bir
ekranın (agent-editor.tsx) değiştirilmesini gerektirir.

Kapsam bu fazda **panoya kopyalamaya** daraltıldı: "Copy" düğmesi, kaynak
sunucu/prompt adı ve SHA-256 özetini yorum olarak taşıyan hazır bir metni
panoya yazar; yönetici bunu agent talimatına elle yapıştırır. Anlık görüntü
ilkesi korunur (uzak sunucu agent'ı hiçbir zaman doğrudan etkilemez), ama
**rozet otomatik görünmez** — bu, sonraki faza devreden bir açık uçtur (bkz.
altta).

---

## Bu Fazda Verilen Kararlar

1. **OAuth yalnız Mod 1 (Authorization Code)** — SDK client_credentials
   sunmuyor; `McpOAuthAuthorizationMode` tek üyeli.
2. **Prompt içeriği anlık görüntü olarak alınır, çalışma anında
   çekilmez** — bu fazda panoya kopyalama ile, otomatik yazma değil.
3. **Kaynak okuma yalnız bildirilen URI'lerle** — serbest URI SSRF'dir.
4. **OAuth token'ları kalıcılaştırılmaz** — `(kiracı, sunucu)` başına tek
   bellek içi önbellek, hem etkileşimli akış hem arka plan tazeleme
   tarafından paylaşılır.
5. **`OAuthCallbackBaseUri` sabit bir ayardır**, istekten türetilmez —
   sağlayıcıda önceden kayıtlı bir yönlendirme adresi gerektirir.
6. **Abonelik yalnız önbellek geçersizleştirir**, içerik çekmez.
7. **Kaynaklar `attachments`'a kopyalanmaz** (kullanıcı kararı).
8. **JSON alan adları `[JsonPropertyName]` ile açıkça sabitlenir** — iki
   büyük harfle başlayan C# adları (`OAuth...`) için camelCase
   politikasının varsayılanına güvenilmez.

---

## Bitiş Ölçütleri (DoD)

- [x] Prompt listelenip **panoya kopyalanıyor** (kaynak+hash yorumuyla) — agent talimatına otomatik aktarma değil, bkz. "Plandan Sapmalar #3"
- [ ] Sunucudaki prompt değişince arayüz rozet gösteriyor — **yapılmadı**, sonraki faza devredildi (agent editör entegrasyonu gerektirir)
- [x] Mod A kaynakları çalıştırma bağlamına giriyor; boyut sınırı çalışıyor — `McpResourceContextProvider` + `McpResourceTrimmingTests`
- [x] `{sunucu}_read_resource` tool'u onay isteyerek çalışıyor — `McpConnection.CreateReadResourceTool`, sunucunun `RequiresApproval` ayarını miras alır
- [x] Yetenek bildirmeyen sunucuya istek gönderilmiyor — Tools/Prompts/Resources üçü de `ServerCapabilities` denetiminden geçer
- [x] OAuth **Mod 1** ile korumalı bir sunucuya bağlanılabiliyor (yapı doğrulandı: `curl` ile 400/409/200 davranışları) — **gerçek bir OAuth sağlayıcısına karşı uçtan uca doğrulanmadı** (bkz. Sonraki Faza Devir Notu)
- [x] Hiçbir uçta ve kayıtta sır yok; token veritabanında yok — `Mcp_sunucusu_oauth_alanlariyla_yazilir_ve_listelenir` + kod incelemesi (`ITokenCache` yalnız bellekte)
- [x] Dört doğrulama kapısı sıfır uyarı; sır taraması boş — `dotnet build/test/pack/format` dördü de temiz, 1235 test (E2E hariç) geçti

---

## Sonraki Faza Devir Notu

- **Prompt rozet/otomatik-güncelleme entegrasyonu yapılmadı.** Agents
  ekranının düzenleyicisi (`agent-editor.tsx`) şu an `Metadata` içine
  `mcp.prompt.*` yazmıyor ve sunucudaki değişikliği rozetle göstermiyor.
  Panoya kopyalanan metin kaynak+hash yorumunu taşır; yönetici isterse bunu
  elle `Metadata`'ya da ekleyebilir ama arayüz bunu otomatik yapmaz. Bu,
  ayrı bir küçük fazda (agent editor'e dokunan) tamamlanabilir.
- **OAuth Mod 1 gerçek bir sağlayıcıya karşı doğrulanmadı.** Yapı
  (state/CSRF, token cache, non-interactive fail-fast) kod incelemesi ve
  `curl` ile doğrulandı; gerçek bir OAuth korumalı MCP referans
  sunucusuyla uçtan uca test edilmedi.
- **`AgentPrism.Ui.E2ETests` bu oturumun sonunda yeniden doğrulanmalıdır.**
  Bu fazın en başındaki tam paket çalıştırmasında 29/29 geçti (mevcut MCP
  ekranı testi dahil). Fazın sonunda, makinede eşzamanlı çalışan başka
  Claude Code oturumlarının CPU çekişmesi yüzünden iki bağımsız yeniden
  çalıştırma da genel zaman aşımlarıyla başarısız oldu — hiçbiri MCP/OAuth
  ile ilgili değildi (`Evals`, `Settings`, `Playground` ekranlarında
  "eleman görünmedi"). Makine boşken `dotnet test
  tests/AgentPrism.Ui.E2ETests` tek başına çalıştırılıp temiz geçtiği
  teyit edilmelidir.
- Faz 13'ün `TextSearchProvider`'ı MCP kaynaklarını arama kaynağı olarak
  kullanabilir; bu, ayrı bir değerlendirme ister.
- Faz 25 (saklama) MCP kaynak önbelleklerini etkilemez — önbellek bellektedir.

---

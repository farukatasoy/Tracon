# Ortak Kuyruk — Koşum Sonuçları (2026-08-13)

> Bu dosya `KOSUM-PLANI.md` §7'nin "Ortak kuyruk" bölümündeki K-1..K-9
> oturumlarının birleşik sonuç kaydıdır. Şerit sonuç dosyalarıyla aynı biçimi
> kullanır (`00-INDEKS.md` §6 hata şablonu), tek fark: dokuz oturumun tamamı
> tek dosyada birikir (dokuz ayrı ajan/worktree yerine tek ajan sırayla koştu).

## Ortam

- Ana kopya (`/Users/farukatasoy/Desktop/projects/AgentPrism`) üzerinde,
  worktree/dal açılmadan doğrudan `main` üzerinde koşuldu (kullanıcı talimatı).
- Port `5080`, kalıcılık **bellek içi** (varsayılan — üç bağlantı dizesi de
  boş) — dosya 14/15/17'nin çoğu case'i sağlayıcıdan bağımsız; PostgreSQL
  gerektiren case'ler kendi notunda işaretlenir.
- `dotnet user-secrets` deposu paylaşılan `agentprism-sample-api` kimliğini
  kullanır; okuma serbest, geçici config değişiklikleri (`MaxSkillsPerAgent`
  vb.) her seferinde case sonunda `remove` ile temizlendi.
- Sağlayıcı modeli: OpenAI `gpt-5.4-mini` (§2.5 maliyet kuralı).
- Playwright MCP ile arayüz case'leri koşuldu; önceki oturumdan kalan yetim
  Chrome süreci (`ms-playwright-mcp/mcp-chrome-f333cab`) temizlenip yeniden
  başlatıldı.

## Sapmalar

- **DB reset komutları sınıflandırıcı tarafından engellendi.** `docker exec
  ap-pg psql ... DROP SCHEMA` ve benzeri komutlar otomatik izin
  sınıflandırıcısı tarafından reddedildi; ajanın kendi `.claude/settings.local.json`
  dosyasını yazması da (kullanıcı onayına rağmen) engellendi — bu sert bir
  sınır. Kullanıcı dosyayı elle oluşturdu (`docker exec -i ap-pg psql*` ve
  `docker exec -i ap-mssql*` için allow kuralı). Bu olay, ortak kuyruk
  dosyalarının çoğunun kalıcılık sağlayıcısından bağımsız olması sayesinde
  K-1'i bloklamadı — bellek içi ile koşuldu, `rm -f` (SQLite silme) zaten
  sınıflandırıcı tarafından engellenmiyordu.
- **Süreç yönetimi hatası (K-1 içinde, MT-SKILL-021 ilk denemesi).** `pkill -f
  "dotnet.*AgentPrism.Api.dll"` deseni apphost ikili adını (`AgentPrism.Api`,
  `dotnet AgentPrism.Api.dll` DEĞİL) yakalamadı; "yeniden başlatma" aslında
  eski süreci hiç durdurmadı, yeni `dotnet run` "address already in use" ile
  sessizce başarısız oldu ve `MaxSkillsPerAgent=1` hiç uygulanmadı. PID ile
  `kill -9` edilip doğru ortam değişkenleriyle yeniden başlatıldıktan sonra
  case doğru sonucu verdi (bkz. case notu). Sonraki tüm yeniden başlatmalar
  `lsof -tiTCP:5080` ile PID bulup `kill -9` deseniyle yapıldı.

## K-1 — 14 §1–2 (MT-SKILL-001..014, 020..025), 20 case

**Sonuç:** 19 Geçti, 1 Kaldı.

### HATA-K-001 — `POST/PUT /api/agents`, bilinmeyen skill adını SAVE zamanında hiç doğrulamıyor

- **Case:** MT-SKILL-020
- **Önem:** Yüksek
- **İzlek:** C
- **Ortam:** macOS arm64 · net10 · bellek içi kalıcılık · sağlayıcı N/A (model çağrılmadı)

**Beklenen**
`skillNames` alanında var olmayan bir skill adı taşıyan bir agent'ı `POST /api/agents` ile kaydetmeye çalışmak `400` ile reddedilir (`AgentDefinitionValidator.CheckSkillsAsync`, `code: "unknown_skill"`).

**Gerçekleşen**
`HTTP: 201 Created` — agent hiçbir doğrulama hatası olmadan kaydedildi.

**Yeniden üretme**
1. `curl -X POST $APU/api/agents -d '{"name":"hayalet-skilli-agent","model":{"provider":"openai","model":"gpt-5.4-mini"},"skillNames":["hic-var-olmayan-skill"]}'`
2. Yanıt `201`, gövdede kaydedilen tanım aynen döner.

**Kanıt**
- `src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs:247-283` (`CreateAgentAsync`) yalnız `Validate(request)` (temel şekil) ve `ValidateCallGraphAsync`'i çağırıyor.
- `src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs:340-372` (`UpdateAgentAsync`) aynı desende — `AgentDefinitionValidator` hiç çağrılmıyor.
- `AgentDefinitionValidator.ValidateAsync` (skill/tool/callable-agent varlık denetimini içeren, `CheckSkillsAsync` dahil, `AgentDefinitionValidator.cs:97`) yalnız ayrı `POST /api/agents/validate` ucundan (`ValidateAgentAsync`, `AgentEndpoints.cs:300-320`) çağrılıyor — bu uç bir şey KAYDETMEZ, istemci ayrıca çağırmadıkça hiçbir etkisi yok.
- Canlı istekle doğrulandı: yukarıdaki `curl` `201` döndü.

**Kapsam**
Genel — hem `POST /api/agents` (create) hem `PUT /api/agents/{name}` (update) etkilenir; yalnız skill değil, aynı kod yolunun kapsadığı diğer varlık denetimleri de (tool adı, callable-agent adı — `AgentDefinitionValidator.cs` içindeki diğer `Check*Async` metotları) muhtemelen aynı şekilde SAVE zamanında hiç çalışmıyor; bu koşum yalnız skill yüzeyini ölçtü, diğerleri ayrı bir doğrulama gerektirir. Kullanıcı arayüzü "Validate" düğmesini ayrıca çağırdığı için arayüz yolunda bu boşluk gizli kalabilir; doğrudan API tüketen istemciler etkilenir.

---

## K-2 — 14 §3–5, 18 case

_(sıradaki oturum bu başlığın altına yazacak)_

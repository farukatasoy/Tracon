# Faz 9 — Yönetişim: Rol Tabanlı Yetkilendirme ve Denetim İzi

> **Durum:** ✅ Tamamlandı (2026-08-02)
> **Kaynak:** [BEYIN-FIRTINASI.md](../BEYIN-FIRTINASI.md) · **F-21**, **F-20**
> **Önkoşul:** Yok
> **Sonrasında mümkün olan:** [Faz 11](11-SKILL-SCRIPT-CALISTIRMA.md) — script çalıştırma bu fazsız yapılamaz
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.PostgreSql`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** Yok (`audit_log` tablosu 0001'de kuruldu, hiç değişmedi)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Bugün erişim **ikilidir**: kapıdan giren her şeyi yapar. Aynı token'a sahip biri hem bir çalıştırmayı okuyabilir hem de bir MCP sunucusu ekleyip onay verebilir. `audit_log` tablosu Faz 0'da kuruldu ve **hâlâ boştur** — kimin ne değiştirdiği hiçbir yerde yazmıyor.

## Plandan Sapmalar

Plan ile gerçekleşen arasındaki fark, sonraki oturumun en değerli bilgisidir.
Aşağıdakiler **gizlenmemiş sapmalardır**.

### S1 — Aktör ambient bir `AsyncLocal` köprüsüyle okunur, `IHttpContextAccessor` ile değil

Plan `IAuditActorResolver`'ın "varsayılan uygulaması `HttpContext.User`'dan okur"
diyordu ama bunu Core'un nasıl yapacağını belirtmiyordu — `HttpTenantContext`
deseni (`IHttpContextAccessor` + Replace ile açık bir `UseXxx()` çağrısı) burada
uygulanamazdı çünkü audit her zaman, açık bir çağrı olmadan, otomatik çalışmalıydı.
Çözüm: `AgentPrism.Core`'da `AuditActorContext` adlı bir `AsyncLocal<ClaimsPrincipal?>`
tutucu; `AgentPrism.AspNetCore`'daki `AgentPrismEndpointFilter`, güvenlik denetimleri
geçtikten sonra her istekte oraya `HttpContext.User` yazar. `ClaimsPrincipal` temel
.NET kütüphanesindedir, bu yüzden Core'un ASP.NET Core'a bağımlılık eklemesi
gerekmedi. Karar K-076.

### S2 — Rol policy fallback'i `IAuthorizationPolicyProvider.GetPolicyAsync` ile, `MapAgentPrism()` çağrısında bir kez çözülür

Plan "policy yoksa eski davranış" diyordu ama mekanizmayı tanımlamıyordu. Uç
gruplarının çoğu tek bir `IEndpointRouteBuilder` grubunu paylaştığı için
(`AgentPrismEndpointFilter` ile korunan grup), rol kısıtını **uç bazında**
uygulamak gerekti: her `*Endpoints.Map(...)` çağrısı artık `AgentPrismRolePolicies`
alır ve `RequireRole(policyName)` uzantısı `policyName` `null` ise hiçbir şey
eklemez. Karar K-075.

### S3 — Denetim izi dekoratörleri her paketin kendi kaydında sarılır, genel bir `Decorate<T>` yardımcısı yazılmadı

`AddAgentPrism()` bellek içi depoları doğrudan `Auditing*Store` ile sarılı
kaydeder; `UsePostgreSql()` aynı dekoratörleri Postgres depolarıyla sarar
(`ActivatorUtilities.CreateInstance`). Scrutor benzeri genel bir dekorasyon
yardımcısı, `UsePostgreSql()`'in `AddAgentPrism()`'den **sonra** çalışıp mevcut
kaydı `Replace` ettiği gerçeğine (K-025) karşı kırılgan olurdu. Karar K-077.

### S4 — `mcp.refresh` istisna: uç katmanında yazılır

"Denetim izi depo dekoratöründe yazılır" kuralının **tek** istisnası budur: elle
tazeleme bir depo yazması değil, `AgentPrism.Mcp` paketindeki
`IMcpToolRefresher.RefreshAsync` çağrısıdır ve `AgentPrism.Core` o pakete bağımlı
olamaz. Yazma `GovernanceEndpoints` içinde yapılır. Karar K-079.

### S5 — `/api/meta` yanıtına `roles` alanı eklendi

Plan bunu "Arayüz" bölümünde zaten istiyordu ("Kullanıcının rolü `/api/meta`
yanıtından okunur") ama sözleşmeye eklenmemişti. `AgentPrismRoleMeta { canRead,
canOperate, canAdminister }` eklendi; her alan `IAuthorizationService.AuthorizeAsync`
ile hesaplanır, policy kayıtlı değilse `true` döner. Karar K-078.

### 🚨 S6 — Sır süzgeci ilk sürümde `maxOutputTokens`'i yanlışlıkla gizliyordu

Örnek uygulamayı gerçekten çalıştırınca (bu protokolün Adım 2'si) ortaya çıktı:
"token" alt dizesi "maxOutputTokens" içindeki "Tokens"ı da eşliyordu ve gerçek bir
`agent.create` denetim kaydında sayısal bir alan `"***"` olarak görünüyordu. Birim
testleri bunu yakalamadı çünkü sentetik veriler gerçek `AgentDefinition` şeklini
taşımıyordu. Düzeltme: "token" fragmanı, anahtar adı çoğul (`tokens`) içeriyorsa
eşleşmeyi iptal eder. Karar K-081; regresyon testi
`AuditSecretFilterTests.Cogul_token_alanlari_sir_sayilmaz`.

### Açık soruların cevapları

1. **Operator onay verebilir mi?** Evet — uygulandı (`Operator` policy'si
   `POST /api/agents/{name}/run` üzerinde, onay kararları da aynı uçtan geçer).
2. **`RequireRolePolicies` varsayılanı?** `false` — geriye uyumlu.
3. **`before`/`after` tam tanım mı?** Tam tanım — `AgentDefinition`,
   `McpServerDefinition`, `TenantDescriptor`, `ToolApprovalRule` doğrudan
   `AgentPrismCoreJsonContext` ile serileştirilir. Tek istisna `agent.rollback`:
   yalnızca sürüm numaraları taşır (`{"rolledBackToVersion":N,"newVersion":M}`).

---

## Bugün Ne Var

```
audit_log (0001_initial.sql, satir 229)
    id         uuid        PK
    tenant_id  text        NOT NULL
    actor      text                     ← NULL, hic yazilmiyor
    action     text        NOT NULL
    entity     text        NOT NULL
    before     jsonb
    after      jsonb
    created_at timestamptz NOT NULL
INDEX audit_log_tenant_created_idx (tenant_id, created_at DESC)
```

Şema **yeterlidir**. Migration gerekmez. Eksik olan tek şey yazan koddur.

Erişim katmanları (`AgentPrismEndpointFilter`): loopback → bearer token →
authorization policy. Üçü de **tüm** korumalı uçlara aynı şekilde uygulanır.

---

## Arayüz

- Yeni ekran: **Audit** (`frontend/src/screens/audit.tsx`) — tablo, filtre,
  `before`/`after` farkı için basit bir JSON gösterimi
- Agent detay ekranına "Bu agent'ın değişiklik geçmişi" bölümü
- Kullanıcının rolü `/api/meta` yanıtından okunur; yetkisi olmayan düğmeler
  **gizlenir** (gösterip 403 almak kötü deneyimdir)
- Sunucu tarafı yetkilendirme yine de tek gerçektir; arayüz gizlemesi bir
  güvenlik önlemi **değildir**

Bundle hedefi: **+6 KB gzip'ten az**. Diff için kütüphane alınmaz; Faz 19 gerçek
bir diff görünümü getirecek.

---

## Bu Fazda Verilecek Kararlar

1. **Üç rol, daha fazlası değil** — dört ve üzeri rol, kullanıcıdan gelen somut
   bir gereksinim olmadan yapılandırma yükü üretir.
2. **AgentPrism rol saklamaz** — kimlik tüketicinin sistemindedir; rol tablosu
   eklemek AgentPrism'i bir kimlik sağlayıcısına dönüştürürdü.
3. **Policy yoksa eski davranış** — aksi hâlde sürüm yükseltmesi çalışan
   kurulumları kırardı.
4. **Denetim izi depo dekoratöründe yazılır, uçta değil** — tek kapı kuralı.
5. **Çalıştırmalar denetim izine yazılmaz** — `runs` tablosu zaten kayıttır.

---

## Bitiş Ölçütleri (DoD)

- [x] Üç policy tanımlı; uç → rol haritası uygulanmış ve testli —
      `AgentPrismPolicies.{Reader,Operator,Admin}`, tüm `Endpoints.Map(...)`
      çağrılarına `RequireRole(roles.X)` eklendi, `RoleAndAuditTests` doğruluyor
- [x] Policy kaydedilmemiş bir uygulamada Faz 8 davranışı **birebir** korunuyor —
      `Rol_policy_kayitli_degilse_tum_uclar_calisir` testi ve mevcut 130
      fonksiyonel testin hiçbiri policy kaydetmeden hâlâ geçiyor
- [x] `audit_log` gerçekten doluyor — agent güncelleme, MCP ekleme ve onay
      kararının gerçek satırları dokümana yazılır (aşağıda, gerçek örnek
      uygulama çıktısı)
- [x] Denetim kaydında hiçbir sır yok (test + gerçek çıktı) — `AuditSecretFilterTests`
      + örnek uygulamada `authorizationConfigurationKey` ve olası `token`/`secret`
      alanlarının `"***"` göründüğü doğrulandı
- [x] `GET /api/audit` filtreleri çalışıyor, kiracılar arası sızıntı yok —
      `AuditLogContract.Kiracilar_arasi_sizinti_yok` (bellek içi + Postgres)
- [x] Audit ekranı çalışıyor; rol tabanlı düğme gizleme çalışıyor —
      `Audit_ekrani_denetim_kaydini_listeler` ve `Reader_rolunde_yazma_dugmeleri_gizlenir`
      (gerçek Playwright taramaları, gerçek Kestrel)
- [x] `MIMARI.md` bölüm 7'deki "⚠️ `audit_log` hâlâ yazılmıyor" uyarısı **kalktı**
- [x] Dört doğrulama kapısı sıfır uyarı; sır taraması boş

### Gerçek çıktı — `samples/AgentPrism.Api` (gerçek OpenAI, bellek içi depo)

```bash
$ curl -s localhost:5091/agentprism/api/meta | python3 -m json.tool
{
    "version": "0.0.0-preview.0.9",
    "storage": {"persistent": false, "agentDefinitionStore": "InMemoryAgentDefinitionStore", ...},
    "roles": {"canRead": true, "canOperate": true, "canAdminister": true}
}

$ curl -s -X POST localhost:5091/agentprism/api/agents -d '{"name":"faz9-demo3", ...
                                                              "model":{"provider":"openai","model":"gpt-5.4-mini","maxOutputTokens":256}, ...}'
{"name":"faz9-demo3", "version":1, "model":{"maxOutputTokens":256, ...}, ...}

$ curl -s localhost:5091/agentprism/api/audit/agent:faz9-demo3 | python3 -m json.tool
[{
    "actor": null,
    "action": "agent.create",
    "entity": "agent:faz9-demo3",
    "before": null,
    "after": "{\"name\":\"faz9-demo3\",...,\"model\":{...,\"maxOutputTokens\":256,...},...}"
}]
```

`maxOutputTokens":256` (sır süzgecinden **geçmedi**, sayısaldır) — sapma S6'nın
düzeltmesinin kanıtı; ilk sürümde bu alan `"***"` dönüyordu.

```bash
$ curl -s -X PUT localhost:5091/agentprism/api/mcp-servers/demo-mcp \
    -d '{"endpoint":"https://example.com/mcp","transport":"StreamableHttp","requiresApproval":true}'
$ curl -s localhost:5091/agentprism/api/audit/mcp:demo-mcp | python3 -m json.tool
[{
    "action": "mcp.create",
    "entity": "mcp:demo-mcp",
    "after": "{...,\"authorizationConfigurationKey\":\"***\",...}"
}]
```

`authorizationConfigurationKey` (o çağrıda `null` olsa bile) `"***"` ile
gizlendi — süzgeç anahtar **adına** göre çalışır, değere değil; bilerek
tutucu davranıştır.

---

## Sonraki Faza Devir Notu

- **Faz 11 (script) bu fazın çıktısına bağlıdır.** Script çalıştırma yetkisi
  Admin'dir ve her çalıştırma denetim izine yazılır. `IAuditLog` orada
  "yazılamazsa reddet" modunda kullanılacaktır.
- Faz 19 (diff) Audit ekranındaki basit JSON gösterimini gerçek bir diff ile
  değiştirecektir; bu fazda diff kütüphanesi **alınmaz**.
- Faz 21 (kota) rol modelini genişletmez; kota kiracı bazlıdır, rol bazlı değil.
- **Faz 10 (agent skill'leri) rol haritasını zaten doğru tahmin etmişti**
  (`10-AGENT-SKILLERI.md` bölüm 10.4 — `GET /api/skills` Reader, `PUT`/`DELETE`
  Admin). Yeni skill uçları eklenirken yalnızca `AgentEndpoints.cs` gibi
  dosyalardaki `.RequireRole(roles.X)` deseni tekrarlanır; `Endpoints.Map(...)`
  imzasına `AgentPrismRolePolicies roles` parametresi eklemeyi unutmayın.
- **Yeni bir yazma yapan depo eklenirse** (Faz 10'un `IAgentSkillStore`'u gibi)
  denetim izine dahil edilmek isteniyorsa aynı desen izlenir: `Auditing*Store`
  dekoratörü yazılır, `AddAgentPrism()`/`UsePostgreSql()` içinde ilgili depo
  bu dekoratörle sarılarak kaydedilir (bkz. K-077, "Gerçekleşen Public API").
- **Aktör köprüsü (`AuditActorContext`) ve rol policy adları (`AgentPrismPolicies`)
  kararlıdır** — sonraki fazlar bunları değiştirmeden kullanabilir.
- **E2E testlerinde rol senaryosu kurmak için** `tests/AgentPrism.Ui.E2ETests/Infrastructure/TestAuthenticationHandler.cs`
  ve `UiHost.StartAsync(configureServices: ...)` kullanılabilir — bu fazda
  eklendi, önceden yoktu.

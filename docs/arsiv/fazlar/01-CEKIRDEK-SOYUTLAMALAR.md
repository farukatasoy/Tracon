# Faz 1 — Çekirdek Soyutlamalar ve Runtime

> **Durum:** Tamamlandı (2026-08-02)
> **Önkoşul:** [00-ALTYAPI.md](00-ALTYAPI.md)
> **Sonraki:** [02-POSTGRESQL-KALICILIK.md](02-POSTGRESQL-KALICILIK.md)
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/01-CEKIRDEK-SOYUTLAMALAR.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism'in sözleşmelerini ve MAF'a bağlanma noktalarını kurmak. **Bu faz veritabanı olmadan tam çalışır.** Bir geliştirici `AddAgentPrism()` yazar, agent tanımlar, çalıştırır ve çalıştırma kaydını okur — hiçbir altyapı kurmadan. Bu, tasarım kuralı K1'in ("sıfır sürpriz") somut karşılığıdır.

## Uygulama Sırasında Alınan Kararlar

Bu kararlar plan yazılırken bilinmiyordu; gerçek MAF API yüzeyi incelenince ortaya çıktı.

### `IAgentSource` — plandaki "MAF `AddAIAgent` kayıtlarını oku" yerine

Plan, katalogun MAF'ın `AddAIAgent` kayıtlarını okumasını öngörüyordu. Ancak `AddAIAgent`, ön sürüm durumundaki `Microsoft.Agents.AI.Hosting` paketindedir ve **K-008 gereği `AgentPrism.Core` o pakete bağımlı olamaz**.

Çözüm: `IAgentSource` soyutlaması. Katalog, önceliğe göre sıralanmış kaynaklardan agent toplar.

| Kaynak | Öncelik | Paket | Faz |
|--------|---------|-------|-----|
| `CodeAgentSource` | 0 | `AgentPrism.Core` | 1 |
| MAF barındırma köprüsü | 10 | `AgentPrism.AspNetCore` | 4 |
| `DefinitionStoreAgentSource` | 100 | `AgentPrism.Core` | 1 |

Sonuç: Core yalnız GA paketlere bağlı kaldı ve mimari genişletilebilir hale geldi. Karar: **K-019**.

### `IAgentDecorator` — çalıştırma kaydı için genişleme noktası

Çalıştırma kaydı doğrudan katalogun içine gömülmedi. `IAgentDecorator` arayüzü tanımlandı; `RunRecordingAgentDecorator` bunu uygular. Tüketici kendi sarmalayıcısını aynı şekilde ekleyebilir.

Bu, tasarım kuralı K4'ün ("her genişleme noktası değiştirilebilir") uygulanmasıdır.

### `AgentPrismId` — kendi UUIDv7 üretecimiz

`Guid.CreateVersion7()` yalnızca .NET 9+ içindedir; paket `net8.0` da hedefliyor. Depolama anahtarlarının tüm hedeflerde aynı üretilmesi gerektiği için RFC 9562 uygulamasını kendimiz yazdık (`AgentPrismId.NewId()`).

Ek kazanç: `AgentPrismId.GetTimestamp(id)` ile kimlikten zaman damgası okunabiliyor.

### AOT uyumluluğu üç yerde ödün istedi

`AgentPrism.Core` AOT uyumlu olarak işaretli (K-006). Üç nokta buna uyarlandı:

| Sorun | Çözüm |
|-------|-------|
| `ValidateDataAnnotations()` → `IL2026` | Elle yazılmış `AgentPrismOptionsValidator` |
| `optionsBuilder.Bind()` → `IL2026` + `IL3050` | `EnableConfigurationBindingGenerator=true` |
| Tool argümanlarını JSON'a çevirme | Elle biçimlendirme (`ad=deger`), yansıma yok |

### `AddToolsFrom<T>()` Faz 3'e ertelendi

Attribute taramalı tool kaydı yansıma gerektirir. Faz 1'de iki aşırı yükleme var:

- `AddTool(AIFunction tool, ...)` — AOT temiz
- `AddTool(Delegate method, ...)` — `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` ile işaretli

İkinci aşırı yükleme, uyarıyı çağırana **dürüstçe iletir**; bastırmaz.

### MAF harness seçenekleri `MAAI001` ile işaretli

`HarnessAgentOptions` üyeleri **"for evaluation purposes only"** tanısı üretiyor. Bastırma tek bir dosyada (`AgentDefinitionCompiler.CompileHarnessAgent`) yapıldı ve gerekçesi koda yazıldı. MAF bu API'yi değiştirirse yalnız orası güncellenecek. Karar: **K-020**.

---

## Bitiş Ölçütleri (DoD)

| Ölçüt | Durum |
|-------|-------|
| `AddAgentPrism()` tek başına, yapılandırmasız çalışır | ✅ |
| Bellek içi store'larla agent tanımlanır → çalıştırılır → kayıt okunur | ✅ |
| Bilinmeyen tool adı anlaşılır hata verir (kayıtlı tool'ları listeler) | ✅ |
| Mimari testi bağımlılık yönünü zorlar | ✅ |
| `dotnet build -c Release` — 0 uyarı | ✅ |
| `dotnet test` — 42/42 | ✅ |
| Örnek API uçtan uca çalışıyor | ✅ |

Örnek API doğrulaması (`samples/AgentPrism.Api`, port 5081):

```
GET  /agents            → support agent'ı, kaynak "code", 2 tool
GET  /tools             → get_order_status, list_recent_orders
POST /agents/support/run → {"text":"Echo: siparisim nerede"}
GET  /runs              → status Completed, usage, eventCount 4, id 019fbf66-… (UUIDv7)
GET  /runs/{id}/events  → #0 RunStarted, #1 MessageDelta, #2 MessageCompleted, #3 RunCompleted
```

---

## Faz 2'ye Devreden Notlar

1. **`ISessionStore` tanımlı ama hiç kullanılmıyor.** Faz 1'de oturum kalıcılığı yok. Faz 2 bunu MAF'ın `AgentSessionStore` yapısına bağlayacak.
2. **`RunRecordingAgent.GetSessionId`** şu an yer tutucu bir değer üretiyor. Faz 2'de gerçek oturum kimliği bağlanmalı.
3. **`CompiledAgentCache.Evict`** çağıran yok. Faz 4'te agent tanımı güncellenince çağrılmalı.
4. **`ToolDescriptor.RequiresApproval`** yalnız bilgi amaçlı; onay akışı Faz 6'da.
5. **`IModelProviderRegistry` boş.** Örnek API kendi `EchoModelProvider` sınıfını kaydediyor; Faz 3'te `UseOpenAI()` gelecek.

---

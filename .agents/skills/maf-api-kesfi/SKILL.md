---
name: maf-api-kesfi
description: Microsoft Agent Framework (Microsoft.Agents.AI) tiplerinin gerçek imzalarını reflection ile çıkarır. MAF'ın .NET dokümantasyonu eksik olduğu için tip ve metot adlarını tahmin etmek hataya yol açar; yeni bir MAF tipi kullanmadan önce bu skill ile imzayı doğrulayın.
---

# MAF API Keşfi

## Neden gerekli

Microsoft Agent Framework'un .NET dokümantasyonu eksiktir. Learn sayfası birçok konuda C# pivotunda **"Coming Soon"** diyor. Blog yazıları ve örnekler eski sürümlere ait olabiliyor.

Bu, gerçek bir maliyete yol açtı: Faz 1 planı `AgentRunResponse` ve `AgentRunResponseUpdate` tiplerini varsayıyordu. Gerçek adlar **`AgentResponse`** ve **`AgentResponseUpdate`**. Plan ayrıca `AddToolsFrom<T>` gibi var olmayan bir API'ye dayanıyordu.

**Kural: MAF'ın bir tipini ilk kez kullanmadan önce imzasını bu yöntemle doğrula.**

---

## Kullanım

```bash
.agents/skills/maf-api-kesfi/scripts/dump-api.sh AIAgent DelegatingAIAgent AgentSession
```

Argüman vermezsen paketlerdeki tüm public tiplerin adlarını listeler:

```bash
.agents/skills/maf-api-kesfi/scripts/dump-api.sh
```

Çıktı: her tip için ctor'lar, public/protected metotlar (virtual işaretli) ve property'ler.

---

## Ne aramalı

| Soru | Nereye bak |
|------|-----------|
| Bir agent'ı nasıl sararım? | `DelegatingAIAgent` → `RunCoreAsync`, `RunCoreStreamingAsync` (protected virtual) |
| Yanıt tipi ne? | `AgentResponse` (Messages, Text, Usage) / `AgentResponseUpdate` (Contents, Text, Role) |
| Oturum durumu nerede? | `AgentSession.StateBag`, `ProviderSessionState<T>` |
| Özel geçmiş deposu nasıl yazılır? | `ChatHistoryProvider` → `ProvideChatHistoryAsync`, `StoreChatHistoryAsync` |
| Agent nasıl kurulur? | `ChatClientExtensions.AsAIAgent`, `ChatClientHarnessExtensions.AsHarnessAgent` |
| Tool çağrılarını nasıl görürüm? | `FunctionCallContent` / `FunctionResultContent` (Microsoft.Extensions.AI) |

---

## Dikkat edilecekler

**1. `MAAI001` tanısı.** MAF'ın bazı üyeleri *"for evaluation purposes only"* işaretlidir ve derlemeyi kırar (`TreatWarningsAsErrors` açık). Bunlar ileride değişebilir. Bastırırken:

- Kullanımı **tek bir dosyada** topla
- `#pragma warning disable MAAI001` üstüne gerekçe yaz
- `docs/KARARLAR.md`'ye kaydet

Bilinen örnek: `HarnessAgentOptions` üyeleri.

**2. Ön sürüm paketleri.** `Microsoft.Agents.AI.Hosting` (preview) ve `.Hosting.OpenAI` (alpha) yalnızca `AgentPrism.AspNetCore` içinde kullanılabilir — karar K-008. Diğer paketler yalnız GA MAF paketlerine bağlanır.

**3. Nullability reflection'da görünmez.** Script `AgentSession session` gösterir ama gerçek imza `AgentSession? session = null` olabilir. Override yazarken derleyici uyarısına güven; `MA0061` varsayılan değer farkını yakalar.

**4. Sürümü sabitle.** Script `Directory.Packages.props` içindeki sürümü kullanır. Farklı bir sürümü incelemek için script'i düzenle.

---

## Alternatif: NuGet paketinin içini açma

Hangi TFM'lerin desteklendiğini görmek için:

```bash
curl -sL "https://api.nuget.org/v3-flatcontainer/microsoft.agents.ai/1.16.0/microsoft.agents.ai.1.16.0.nupkg" -o /tmp/p.zip
unzip -l /tmp/p.zip | grep -oE 'lib/[a-z0-9.]+/' | sort -u
```

Bir paketin bağımlılıklarını görmek için `.nuspec` dosyasını okuyun.

---
name: maf-api-kesfi
description: Microsoft Agent Framework (Microsoft.Agents.AI), Microsoft.Extensions.AI ve ModelContextProtocol tiplerinin gerçek imzalarını reflection ile çıkarır. Bu kütüphanelerin .NET dokümantasyonu eksik olduğu için tip ve metot adlarını tahmin etmek hataya yol açar; yeni bir tipi kullanmadan önce bu skill ile imzayı doğrulayın.
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

**Joker destekli** — bir konuyu tarayacaksan ad bilmene gerek yok:

```bash
.agents/skills/maf-api-kesfi/scripts/dump-api.sh '*Approval*'
.agents/skills/maf-api-kesfi/scripts/dump-api.sh '*Skill*'
```

Çıktı: her tip için uyguladığı arayüzler, ctor'lar, public/protected metotlar
(virtual işaretli), property'ler; enum'larda üye adları ve sayısal değerleri.

### Hangi derlemeler taranıyor

Varsayılan olarak üçü birden — sürümler `Directory.Packages.props`'tan okunur:

| Önek | Ne için |
|------|---------|
| `Microsoft.Agents.AI*` | MAF çekirdeği ve Harness |
| `Microsoft.Extensions.AI*` | `AIContent` türevleri (`ToolApprovalRequestContent`, `ApprovalRequiredAIFunction`) |
| `ModelContextProtocol*` | MCP istemcisi (`McpClient`, `McpClientTool`) |

Başka bir paketi incelemek için script'i **değiştirmeyin**, ortam değişkeni verin:

```bash
APIDUMP_PACKAGES="Azure.AI.OpenAI@2.6.0" APIDUMP_PREFIXES="Azure.AI" \
  .agents/skills/maf-api-kesfi/scripts/dump-api.sh '*Client*'
```

---

## Ne aramalı

| Soru | Nereye bak |
|------|-----------|
| Bir agent'ı nasıl sararım? | `DelegatingAIAgent` → `RunCoreAsync`, `RunCoreStreamingAsync` (protected virtual) |
| Yanıt tipi ne? | `AgentResponse` (Messages, Text, Usage) / `AgentResponseUpdate` (Contents, Text, Role) |
| Oturum durumu nerede? | `AgentSession.StateBag`, `ProviderSessionState<T>` |
| Özel geçmiş `store`'u nasıl yazılır? | `ChatHistoryProvider` → `ProvideChatHistoryAsync`, `StoreChatHistoryAsync` |
| Agent nasıl kurulur? | `ChatClientExtensions.AsAIAgent`, `ChatClientHarnessExtensions.AsHarnessAgent` |
| Tool çağrılarını nasıl görürüm? | `FunctionCallContent` / `FunctionResultContent` (Microsoft.Extensions.AI) |
| Bir tool'u nasıl onaya bağlarım? | `ApprovalRequiredAIFunction` ile sar → MAF `ToolApprovalRequestContent` üretir |
| Onay yanıtını nasıl üretirim? | `ToolApprovalRequestContent.CreateResponse(approved, reason)` |
| Span'leri nasıl toplarım? | `OpenTelemetryAgent` (`MAAI001`) + `ActivityListener` |
| Uzak MCP tool'u nasıl bağlarım? | `McpClient.CreateAsync` → `ListToolsAsync` → `McpClientTool : AIFunction` |

---

## Dikkat edilecekler

**1. `MAAI001` tanısı.** MAF'ın bazı üyeleri *"for evaluation purposes only"* işaretlidir ve derlemeyi kırar (`TreatWarningsAsErrors` açık). Bunlar ileride değişebilir. Bastırırken:

- Kullanımı **tek bir dosyada** topla
- `#pragma warning disable MAAI001` üstüne gerekçe yaz
- `docs/KARARLAR.md`'ye kaydet

Bilinen örnekler: `HarnessAgentOptions` üyeleri, `OpenTelemetryAgent` kurucusu,
`ChatHistoryProvider.InvokingContext` kurucusu.

**2. Ön sürüm paketleri.** `Microsoft.Agents.AI.Hosting` (preview) ve `.Hosting.OpenAI` (alpha) yalnızca `AgentPrism.AspNetCore` içinde kullanılabilir — karar K-008. Diğer paketler yalnız GA MAF paketlerine bağlanır.

**3. Nullability reflection'da görünmez.** Script `AgentSession session` gösterir ama gerçek imza `AgentSession? session = null` olabilir. Override yazarken derleyici uyarısına güven; `MA0061` varsayılan değer farkını yakalar.

**4. Sürümü sabitle.** Script sürümleri `Directory.Packages.props`'tan okur. Başka bir sürümü incelemek için `APIDUMP_PACKAGES` ortam değişkenini kullan.

**5. Paket var sanma, doğrula.** Faz 6 planı `Microsoft.Agents.AI.Mcp` paketini varsayıyordu; böyle bir paket **yok** ve planın merkezi varsayımı çöktü. Bir paketi ilk kez kullanmadan önce:

```bash
dotnet package search "<PaketAdi>" --exact-match --format json
```

Boş `packages` dizisi paketin var olmadığı anlamına gelir.

---

## Sürüm yükseltirken — iki dump al, diff'le

Bir MAF/MEAI/MCP sürümünü yükseltirken "derleme geçti" **yeterli kanıt değildir**:
derleme yalnız BUGÜN çağırdığımız üyeleri kanıtlar. Kaldırılan bir üye, henüz
kullanmadığımız bir genişleme noktasını sessizce kapatabilir; eklenen bir üye ise
planlanmış bir fazın tasarımını değiştirebilir.

Script sürümleri `Directory.Packages.props`'tan okur; bu yüzden **eski sürüm için
ikinci bir kök** kurulur:

```bash
SP=/tmp/apidiff && mkdir -p $SP/eski/.agents/skills/maf-api-kesfi/scripts
cp .agents/skills/maf-api-kesfi/scripts/dump-api.sh $SP/eski/.agents/skills/maf-api-kesfi/scripts/
sed 's|<MicrosoftAgentsAIVersion>YENI|<MicrosoftAgentsAIVersion>ESKI|' \
  Directory.Packages.props > $SP/eski/Directory.Packages.props

TMPDIR=$SP/t1 bash $SP/eski/.agents/skills/maf-api-kesfi/scripts/dump-api.sh '*' > $SP/eski.txt
TMPDIR=$SP/t2 .agents/skills/maf-api-kesfi/scripts/dump-api.sh '*'              > $SP/yeni.txt
```

Düz `diff` **yanıltır**: aynı üye satırı başka bir tipe eklendiğinde "değişmedi"
görünür. Karşılaştırma **tip kapsamlı** yapılır — her tipin üye kümesi ayrı ayrı
(`>>> ` satırı tip, girintili satırlar üyedir; iki dosyayı bu yapıya göre ayrıştır
ve tip tip küme farkı al).

**Kaldırma varsa yükseltme bir kusur işidir**, bir bakım işi değil. Ekleme varsa
sonucu [`docs/MAF-GENISLEME-NOKTALARI.md`](../../../docs/MAF-GENISLEME-NOKTALARI.md)
§ *Sürüm damgası* tablosuna yaz — hangi eklemenin **kullanılmadığını** da yaz, yoksa
sonraki tur onu yeniden keşfeder.

🚨 **Reflection davranışı görmez.** İmza aynı kalıp davranış değişebilir: 2026-08-21
yükseltmesinde `OpenAIResponses.ToAgentRunRequest` aynı imzayla `JsonException`
yerine `ArgumentException` atmaya başladı. Sevk edilen bir metin bir istisna tipini,
bir varsayılan değeri veya bir hata mesajını adıyla anıyorsa onu **küçük bir probe
projesiyle** yeniden ölç; dump yetmez.

---

## Alternatif: NuGet paketinin içini açma

Hangi TFM'lerin desteklendiğini görmek için:

```bash
curl -sL "https://api.nuget.org/v3-flatcontainer/microsoft.agents.ai/1.18.0/microsoft.agents.ai.1.18.0.nupkg" -o /tmp/p.zip
unzip -l /tmp/p.zip | grep -oE 'lib/[a-z0-9.]+/' | sort -u
```

Bir paketin bağımlılıklarını görmek için `.nuspec` dosyasını okuyun.

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
- `docs/KARARLAR.md`'ye `*(kategori: public-api)*` etiketiyle kaydet — bastırma
  ön sürüm bir API'ye bağımlılıktır, yani bir uyumluluk kararıdır (`faz-tamamlama` Adım 8)

Bilinen örnekler: `HarnessAgentOptions` üyeleri, `OpenTelemetryAgent` kurucusu,
`ChatHistoryProvider.InvokingContext` kurucusu.

**2. Ön sürüm paketleri.** `Microsoft.Agents.AI.Hosting` (preview) ve `.Hosting.OpenAI` (alpha) yalnızca `Tracon.AspNetCore` içinde kullanılabilir — karar K-008. Diğer paketler yalnız GA MAF paketlerine bağlanır.

**3. Nullability reflection'da görünmez.** Script `AgentSession session` gösterir ama gerçek imza `AgentSession? session = null` olabilir. Override yazarken derleyici uyarısına güven; `MA0061` varsayılan değer farkını yakalar.

**4. Sürümü sabitle.** Script sürümleri `Directory.Packages.props`'tan okur. Başka bir sürümü incelemek için `APIDUMP_PACKAGES` ortam değişkenini kullan.

**5. Paket var sanma, doğrula — `--prerelease` ile.** MAF ailesi yeni paketi önce `alpha` veya `preview` olarak yayınlar. `--prerelease` olmadan `dotnet package search` bu paketleri **göstermez**. Ölçüm (2026-10-17): `Microsoft.Agents.AI.Mcp` için varsayılan arama boş `packages` döndürdü; `--prerelease` ile aynı arama 19 sürüm döndürdü (ilk sürüm 2026-05-28). Faz 6 notu ve 2026-08-26 keşif turu ("paket yok") bu yüzden yanlış sonuca vardı. Bir paketi ilk kez kullanmadan önce:

```bash
dotnet package search "<PaketAdi>" --exact-match --prerelease --format json
```

Boş `packages` dizisi **yalnız bu komutla** paketin var olmadığı anlamına gelir. `--prerelease` yazmayan komutun boş sonucu hiçbir şey kanıtlamaz. İkinci kaynak (ön sürümleri de listeler): `https://api.nuget.org/v3-flatcontainer/<paket-kimliği-küçük-harf>/index.json`.

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
görünür. Karşılaştırma **tip kapsamlı** yapılır — her tipin üye kümesi ayrı ayrı.
Bunu betik yapar (Faz 224). Kaldırma ya da tip başlığı değişimi (taban sınıf, `[abstract]`,
`[sealed]`) varsa çıkış `1` olur; `!! yuklenemedi` satırı taşıyan eksik bir dump hata verir:

```bash
python3 scripts/maf-kapsama.py fark $SP/eski.txt $SP/yeni.txt
```

Ölçüm (2026-10-20): 1.24.0 → 1.25.0 çekirdek paketlerde **sıfır** fark verdi; 1.20.0 →
1.25.0 sürüm damgası tablosundaki dört tipi ve sekiz üyeyi birebir verdi.

**Kaldırma varsa yükseltme bir kusur işidir**, bir bakım işi değil. Ekleme varsa
sonucu [`docs/MAF-GENISLEME-NOKTALARI.md`](../../../docs/MAF-GENISLEME-NOKTALARI.md)
§ *Sürüm damgası* tablosuna yaz — hangi eklemenin **kullanılmadığını** da yaz, yoksa
sonraki tur onu yeniden keşfeder.

### Üçüncü adım — paket ailesi envanteri (ön sürümler dahil)

İki dump yalnız Tracon'un **referans verdiği** paketleri diff'ler. MAF yeni bir yeteneği
çoğu zaman **yeni bir pakette**, önce `alpha`/`preview` olarak yayınlar; o paket ne
referanslıdır ne dump'ta görünür. Ölçüm (2026-10-17): 40 paketlik ailenin 31'i referanslı
değildi ve hiçbir yükseltme turu onları görmüyordu. Her yükseltmede dump'tan sonra:

```bash
python3 scripts/maf-paket-envanteri.py            # tabanla fark var mı? (çıkış 1 = var)
python3 scripts/maf-paket-envanteri.py --liste    # ailenin tamamı + her birinin durumu
```

Betik NuGet aramasını `prerelease=true` ile okur ve `scripts/maf-paket-envanteri.tsv`
tabanıyla karşılaştırır: `YENİ` (hiç değerlendirilmedi) · `OLGUNLAŞTI` (ör. preview → GA,
bir kararın yeniden okunma zamanı) · `GERİLEDİ` · `GÖRÜNMÜYOR` · `TUTARSIZ` (taban ile
`Directory.Packages.props` ayrıştı). Farkı kapatmak için:

1. `--guncelle` tabanı canlı duruma getirir ve her `YENİ` paketi `degerlendirilmedi` olarak ekler.
2. **Her `YENİ` ve `OLGUNLAŞTI` satırın `durum` ve `not` alanını ELLE yaz.** `karar` satırı
   karar (`K-…`) veya aday (`F-…`) numarası ve gerekçe taşır; gerekçesiz `karar` taban
   okunurken reddedilir. `OLGUNLAŞTI` bir `karar` satırının gerekçesini bayatlatabilir
   (K-129: paket GA oldu) — gerekçeyi yeniden ölç, satırı sessizce bırakma.
3. `degerlendirilmedi` bir sonuç değil bir **borçtur**: `aday-kesfi` turunun girdisidir.

Ağ gerekir ve CI'da koşmaz. Tabanın kendi biçimi ve `Directory.Packages.props` ile
tutarlılığı ise CI'da `maf_paket_envanteri_test.py` ile denetlenir: yeni bir MAF paketi
referansı ekleyip tabanı güncellemeyen bir değişiklik orada kırmızı olur. macOS python.org
kurulumunda `urllib` CA bulamaz; betik sertifika doğrulamasını kapatmaz, `curl` komutunu
basar ve `--dosya` ile verilir (`docs/hafiza/tedarik-zinciri.md`).

### Dördüncü adım — niyet: release notu ve örnek dizini

Dump ve envanter bir tipin **var** olduğunu gösterir. Neden geldiğini ve bir davranışın
değiştiğini göstermez. Bunu upstream'in kendi metni söyler. Her yükseltmede, eski pin ile
yeni pin arasındaki **her** .NET sürümü için oku:

```bash
gh release list --repo microsoft/agent-framework --limit 40 | grep -E '^dotnet-'
gh release view dotnet-<sürüm> --repo microsoft/agent-framework --json body --jq .body

# Değişen örnekler yeni yeteneği adıyla gösterir. GitHub compare API'si 300 dosyada keser
# (1.24.0 → 1.25.0: 435 dosya); yalnız ağaç çeken bir klon kesmez ve ~3 sn sürer:
git clone --filter=blob:none --no-checkout --quiet https://github.com/microsoft/agent-framework "$SP/maf"
git -C "$SP/maf" diff --name-only dotnet-<eski> dotnet-<yeni> -- dotnet/samples
```

Her satırı bir sınıfa koy:

| Satır | Ne yapılır |
|---|---|
| `[BREAKING]` ya da davranış değişikliği (ör. 1.25.0 #9152: yanıtsız tool onay isteği otomatik reddedilir) | Tracon'un kullandığı bir tipe dokunuyorsa aşağıdaki 🚨 kuralıyla probe |
| Yeni yetenek ya da yeni örnek | [`docs/MAF-KAPSAMA.md`](../../../docs/MAF-KAPSAMA.md) §5'e `✗⁺` satırı; `aday-kesfi` turunun girdisidir |
| Tracon'a dokunmayan (Python, yalnız Foundry, Cosmos …) | Yok say |

Sonucu `MAF-GENISLEME-NOKTALARI.md` § *Sürüm damgası*'ndaki tur notuna tek satır yaz:
okunan sürümler ve her sınıftaki satır sayısı. Release notu yoksa ya da yalnız commit
listesi varsa bunu da yaz — "okunmadı" ile "boştu" ayrı sonuçlardır.

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

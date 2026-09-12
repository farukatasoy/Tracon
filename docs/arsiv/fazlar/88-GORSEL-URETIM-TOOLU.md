# Faz 88 — Görsel Üretim Tool'u

> **Durum:** ✅ Tamamlandı (2026-08-23)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-142** — Dalga 14 Küme Ö
> **Önkoşul:** [Faz 28](28-SES-TOOLLARI.md) (ses tool'ları — yapı emsali) · [Faz 14](14-COK-MODLULUK.md) (ekler ve `IAttachmentStorage`) · [Faz 68](68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) (`ToolCallUsage` ve token kırılımı)
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.OpenAI`, `Tracon.Azure`, `Tracon.Google`
> **Yeni paket:** 🚨 **Yok — ÖLÇÜLDÜ (2026-08-21).** Aday listesinin "ÖLÇÜLMEDİ" satırı kapandı: hiçbir yeni NuGet paketi gerekmiyor (88.1) · **Migration:** Yok — ölçüm `tool_invocations` satırına `ToolCallUsage` olarak yazılır ve o yol Faz 68'de açıldı
> **Public API:** Büyüyor — 1 ayar tipi, 2 `ToolUsageUnits` sabiti, sağlayıcı başına 1 kayıt uzantısı. `PublicAPI.Shipped.txt` toplamı **16 satır** (yalnız başlıklar; ölçüldü 2026-08-21) → Faz 7'den önce eklemek **bedava**
> **Tüketici yüzeyi:** `docs-site/` → `guides/multimodal.md`, `guides/model-providers.md`, `reference/configuration.md`, `capabilities.md`, `concepts/tools.md`
> · sevk edilen: tool ve fiyat ayarının XML dokümanı, `src/Tracon.Core/README.md`, sağlayıcı paketlerinin `README.md`'leri. `api/` ve `http-api/` **üretilir**
> **Manuel test alanı:** [`docs/manuel-test/19-COK-MODLULUK-VE-SES.md`](../../manuel-test/19-COK-MODLULUK-VE-SES.md) · [`docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`](../../manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/88-GORSEL-URETIM-TOOLU.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon görsel **üretemiyor**. Faz 14 çok modluluğu **girdi** tarafında çözdü (görsel, ses, dosya girdisi); çıktı tarafında karşılığı yoktur. Ölçüldü (2026-08-21): `src/` içinde görsel üretimi için **0 eşleşme**. Bu bir **ölçüm bütünlüğü** kalemidir.

## Bitiş Ölçütleri (DoD)

- [x] Ayar **kapalıyken** tool kayıtlı değildir ve uç bağlı değildir (K1).
- [x] `generate_image` OpenAI · Azure · Google adapter kayıtları ve provider çözümüyle çalışır; üç provider extension testi geçer.
- [x] Tool sonucu **yalnız ek kimliği** taşır; base64 taşımaz.
- [x] Üretilen ek doğru `TenantId` **ve** `SessionId` ile yazılır.
- [x] `Data` ve `Uri` biçimleri ek deposuna doğru girer; `Uri` indirmesi giden ağ muhafızından geçer ve chunked gövde sınırlandırılır.
- [x] `tool_invocations` satırı `ToolCallUsage` taşır; birim `images` **veya** token olur.
- [x] 🚨 Fiyat kaydı yoksa `cost` **`null`** döner — hiçbir koşulda uydurulmaz.
- [x] Hem `PerImage` hem token fiyatı doluysa başlangıçta yapılandırma hatası verilir.
- [x] **Yeni NuGet paketi alınmadı** — `dotnet list package --include-transitive` farkı sıfır.
- [x] `Tracon.OpenAI` AOT uyumlu kaldı.
- [x] Dört doğrulama kapısı sıfır uyarı ile geçti: build, test, pack ve format.
- [x] `samples/Tracon.Api` ile gerçek istek yapıldı. İlk turda yetkisiz `gpt-image-1` çağrısı kontrollü `502` ve alt `403 model_not_found` döndü; yetki tanındıktan sonra (2026-08-23) hem operatör ucu hem `generate_image` tool yolu **gerçek** `gpt-image-1` görseli üretti ve doğru PNG olarak ek deposuna yazıldı — bkz. üçüncü denetim turu.
- [x] Bu fazın değiştirdiği ve eklediği dosyalarda `secret` taraması boş döndü. Repo genelindeki eski manuel-test örnekleri ve Astro cache'i bu kapsam dışındadır.
- [x] Manuel kabul case'leri `docs/manuel-test/19-*` ve `12-*` içine eklendi; otomatikleştirilebilenler koşuldu.
- [x] `faz-denetim` üç kez koşuldu (üçüncüsü gerçek `gpt-image-1` ile canlı manuel koşum); 🔴 bulgu kalmadı.
- [x] `docs-site/` güncellendi; `npm run check`, link ve agent-map kapıları temiz.
- [x] Arayüze dokunulmadı; sözlük veya bundle değişimi yok.

### Doğrulama komutları

```bash
# Yeni gecisli paket alinmadi mi (fazdan ONCE ve SONRA ayni cikti)
dotnet list Tracon.slnx package --include-transitive | sort > /tmp/paketler.txt

# Tool sonucu ek kimligi mi tasiyor
curl -s http://localhost:5081/tracon/api/runs/$RUN_ID/tools \
  | jq '.[] | select(.toolName=="generate_image") | .result'

# Olcum ve maliyet
curl -s http://localhost:5081/tracon/api/runs/$RUN_ID/tools \
  | jq '.[] | select(.toolName=="generate_image") | .usage'
```

---

## Plandan Sapmalar

1. Plan MEAI 10.9.0 görsel yüzeyini GA sayıyordu. Reflection ölçümü
   `IImageGenerator` ve ilişkili tiplerde `MEAI001` verdi. Native MEAI tipi
   doğrudan korunuyor; bastırma yalnız dört çağrı/kayıt sınırında dar tutuldu
   (K-588).
2. Plan `HostedFileContent` kimliğinin eki temsil edebileceğini varsayıyordu.
   Gerçek imza yalnız provider'a özgü `FileId` ve isteğe bağlı metadata verir;
   indirme işlemi yoktur. Byte-only `IAttachmentStore` içine bu kimliği yazmak
   bozuk bir görsel üretirdi. `DataContent` ve guard'dan indirilmiş `UriContent`
   desteklenir; hosted yanıt açık hatayla reddedilir (K-590).
3. Tek isimsiz `IImageGenerator` kaydı çoklu sağlayıcı hostunda DI kayıt sırasına
   bağlı olurdu. Sağlayıcı kayıtları provider adıyla keyed yapıldı; isimsiz özel
   consumer kaydı yalnız fallback kaldı (K-589).
4. Google'ın image API'si `WIDTHxHEIGHT` değil ayrı aspect-ratio/size-tier
   değerleri taşır. Yaklaşık eşleme maliyeti ve sonucu değiştirirdi; adapter
   `ImageSize` isteğini tahmin etmeden reddeder (K-591).
5. Denetim sonrası attachment yazıcısı `ITenantContext` okumayı bıraktı; çağıran
   run scope'un sabit tenant kimliğini verir. Çoklu içerik yazısında hata veya
   iptal olursa önceki eklerin silinmesi denenir; cleanup hatası loglanır ve asıl
   hata korunur. Chunked URI gövdesi de limitten büyük belleğe alınmadan durur.
6. Operatör HTTP ucu planın public API sayımında yoktu. Uç için istek/yanıt
   sözleşmeleri `Abstractions`a eklendi ve OpenAPI/client yeniden üretildi.

## Bu Fazda Verilen Kararlar

- **K-588** — Deneysel MEAI görsel yüzeyi dar `MEAI001` sınırlarında kullanılır.
- **K-589** — Image generator provider adına göre keyed çözülür; isimsiz kayıt
  özel consumer fallback'idir.
- **K-590** — Attachment deposu yalnız doğrulanabilir baytı kalıcılaştırır;
  hosted provider referansı fail-closed reddedilir.
- **K-591** — Google adapter `WIDTHxHEIGHT`yi tahminle eşleştirmez, reddeder.

## Denetim Bulguları


| Bulgu | Seviye | Sonuç |
|---|---|---|
| Hosted response kalıcı ek olamıyordu | 🔴 | Gerçek MEAI imzası yeniden ölçüldü. Byte indirme veya kalıcı referans sözleşmesi olmadığı için fail-closed davranış gerekçelendi, doküman eşitlendi (K-590). |
| Ek çağrı anındaki tenant context'e yazılıyordu | 🔴 | `ImageAttachmentWriter` artık tenantı çağırandan alır; tool run scope tenantını, endpoint güncel request tenantını verir. |
| Çoklu yazıda yarım ek kalıyordu | 🔴 | Hata/iptalde kaydedilen ekler `CancellationToken.None` ile geri silinir; test kapsar. |
| Chunked URI sınırsız belleğe alınabiliyordu | 🔴 | Bounded stream okuyucu limite erişir erişmez durur; test kapsar. |
| Sevk edilen XML içinde `K1` vardı | 🔴 | "default-off rule" ile değiştirildi; self-containment kapısı yeşil. |
| Üç yeni giriş noktasının XML örneği yoktu | 🔴 | Derlenebilir `Use*Images` örnekleri eklendi; capability kapısı yeşil. |
| Başarılı URI indirme kanıtı yoktu | 🟡 | Chunked yerel HTTP sunucusundan guard üzerinden indirme ve saklama testi eklendi. |
| Operatör request/response tipleri plan API'sinde yoktu | 🟡 | Gerçekleşen API bölümüne eklendi. |
| Rollback cleanup hatası için garanti ve test belirsizdi | 🟡 | Cleanup best-effort olarak ürün dokümanına yazıldı; delete hatası asıl hatayı koruyan test eklendi. |
| Shipped agent map Google image giriş noktasını içermiyordu | 🟡 | `capabilities.md` kaynağından map yeniden üretildi; `UseGoogleImages()` artık sevk edilen map'te. |
| 🚨 `.UseMcp(...)` etkinken `generate_image` derlemeye hiç girmiyordu | 🔴 | Üçüncü denetim turu — gerçek `gpt-image-1` çağrısıyla canlı koşumda bulundu. `TraconMcpBuilderExtensions.UseMcpCore` `IToolRegistry`'yi `McpToolRegistry` ile REPLACE ederken iç registry'yi `ToolRegistry.Create(provider)` üzerinden değil, kayıtları elle yeniden toplayan ikinci bir inşa yoluyla kuruyordu; bu ikinci yol 88.1'in `images.Enabled` kapısını hiç çalıştırmıyordu. Ayar açık, sağlayıcı kayıtlı olsa bile `GET /api/tools` ve agent derlemesi tool'u hiç görmüyordu — hata da vermiyordu. Düzeltme: `Tracon.Core`'un `InternalsVisibleTo`'suna `Tracon.Mcp` eklendi, `UseMcpCore` artık `ToolRegistry.Create(provider)`'ı çağırıyor (K3/K1 ile aynı kapıyı paylaşıyor). `McpToolRegistryImageGateTests` (`tests/Tracon.Mcp.UnitTests/`) eski koda karşı doğrulanmış: fix'siz kırmızı, fix'li yeşil. |
| `GenerateImageTool`'un kendi `MaxImagesPerRequest` reddi yalnız HTTP operatör ucunun kopya kontrolüyle test ediliyordu, tool yolu hiç değil | 🟡 | `GenerateImageToolTests` (`tests/Tracon.Core.UnitTests/Images/`) eklendi: sayım sınırı, yalnız-ek-kimliği sonucu, sağlayıcı hatasının yutulmadığı, eksik run scope/tenant durumları, `size` ayrıştırması — tool'un kendi gövdesi üzerinden, HTTP'siz. |

🔴 ve 🟡 açık bulgu yoktur.

## Sonraki Faza Devir Notu


- Faz 89 `ToolRegistry` sarmalayıcı zincirine yeni halka eklerken, images açıkken
  factory'nin `generate_image`ı `ToolEffect.External` ile eklediğini korumalıdır.
  Bu tool devam koşusunda otomatik tekrar edilmez.
- 🚨 **Ölçüldü ve düzeltildi (2026-08-23, üçüncü denetim turu):** `.UseMcp(...)`
  `IToolRegistry`'yi `McpToolRegistry` ile REPLACE ederken iç registry'yi
  `ToolRegistry.Create(provider)` üzerinden değil, kayıtları elle yeniden
  toplayan bir kopya inşa yoluyla kuruyordu. Bu kopya 88.1'in `images.Enabled`
  kapısını atlıyordu: ayar açık ve sağlayıcı kayıtlı olsa bile `generate_image`
  MCP açıkken (sample'da her zaman) derlemeye hiç girmiyordu — hatasız, sessizce.
  Düzeltme `Tracon.Core` → `InternalsVisibleTo("Tracon.Mcp")` ekleyip
  `UseMcpCore`'u `ToolRegistry.Create(provider)`'ı çağıracak şekilde değiştirdi.
  **Ders:** `IToolRegistry`'yi REPLACE/sarmalayan her yeni yer `ToolRegistry.Create`
  üzerinden inşa etmelidir — kayıtları elle yeniden toplamak, `Create`'e sonradan
  eklenen her gate'i (bugünkü images, yarın başka biri) sessizce atlar.
- 🚨 Image attachment yazısı `AgentRunScope.TenantId` ile yapılır; async akışta
  yeniden `ITenantContext` çözmek tenant/run ayrışması üretir.
- 🚨 URI gövdesi `Content-Length`e güvenmez. `ImageAttachmentWriter`ın bounded
  okuma ve best-effort rollback yolu korunmalıdır; cleanup hatası asıl image
  hatasını maskelemez ve loglanır.
- `HostedFileContent` provider `FileId`si dışında okunabilir içerik vermez.
  Depo sözleşmesi değişmeden destek eklenmez.
- 2026-08-23 güncellemesi: `gpt-image-1` erişimi tanındı. Hem
  `POST /api/images/generate` hem agent üzerinden `generate_image` tool çağrısı
  gerçek bir görsel üretti (1024×1024 PNG, ek deposuna doğru yazıldı, `usage.unit
  = tokens` ve fiyat kaydı yokken `cost = null`). Faz 89 artık bu modelle canlı
  deneme yapabilir; provider erişimi kaybolursa yine `HTTP 502` / alt
  `403 model_not_found` beklenir.

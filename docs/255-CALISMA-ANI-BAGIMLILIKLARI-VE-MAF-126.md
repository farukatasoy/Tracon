# Faz 255 — Çalışma Anı Bağımlılıkları ve MAF 1.26

> **Durum:** 🚧 Uygulanıyor (2026-10-28)
> **Kademe:** tam — güvenlik ve kiracı sınırı (oturum silme yolu; BYOK isteğinin kimlik başlığı değişir) · public API davranışı (Anthropic örnekleme beyanı daralır, alanı taşıyan tanım reddedilir; `SamplingFields` büyüyebilir) · geri dönüşü pahalı (Anthropic SDK major sürümü)
> **Plan onayı:** 👤 2026-10-28 (farukatasoy) — "fazı geliştir commit atabilirsin". Kapsam kararları 👤 aynı gün: çalışma anı seti · ortak silme yolu · Anthropic 13'e geç ve beyanı daralt. Açık Sorular 1–4 plandaki önerilen seçenekle uygulandı (bkz. Plandan Sapmalar 1).
> **Kaynak:** [RC-KALITE-DALGALARI.md](RC-KALITE-DALGALARI.md) Q-10 · **F-745** (çalışma anı bağımlılık seti ve MAF 1.26), **F-746** (`DELETE /v1/conversations/{id}` oturumun ek dosyalarını silmez)
> **Genellik:** Her tüketici. Sevk edilen 21 paketin grafiği MAF'ın bir minor sürümü (1.25 → 1.26) ve platformun bir yamasının (10.0.13 → 10.0.14) gerisindedir. Tüketici MAF 1.26'yı kendi projesine alırsa Tracon'un ikilisi `TraconAgentSessionStore`'da `TypeLoadException` ile kırılabilir: 1.26 `AgentSessionStore`'a soyut bir üye ekledi (ölçüldü, derleme `CS0534`). İş tüketici kodunda kalamaz. İkinci kanıt: ilk ilke (`rc.1` bu grafiği dondurur) ve freeze ölçütü 2'nin bayatlaması (`YAYIN-HAZIRLIK.md` §2).
> **Karşı görüş:** "Anthropic 13 bir major sürüm; tüketicinin `Temperature`/`TopK` kullanan Anthropic tanımı reddedilmeye başlar." **Cevap:** SDK 13 bu alanları istekten kaldırdı; yükseltmeden sonra onları göndermenin yolu yoktur. Seçenek reddetmek ya da sessizce düşürmektir; Tracon'un kuralı sessiz düşürmeyi yasaklar (K-1103, Faz 252). `rc.1`'den önce kırmak ucuzdur (UR-012 B). 12.x'te kalmak 👤 kararla seçilmedi.
> **Önkoşul:** RC kalite Dalga 0 (2026-10-28, kapandı). Faz 193 (`net8.0`/`net9.0` düşer) bu fazdan bağımsızdır; ikisi `Directory.Packages.props` ve `CHANGELOG.md`'yi paylaşır, **paralel koşmaz**.
> **Paketler:** `Tracon.AspNetCore`, `.Core`, `.Anthropic`, `.Google`, `.Azure`, `.OpenAI`, `.Sqlite`, `.Abstractions` (yalnız Açık Soru 1 A seçilirse) (+ `Directory.Packages.props`, `.github/dependabot.yml`)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyebilir (Açık Soru 1: `SamplingFields`'e iki üye). Silinen satır beklenmez. `PublicAPI.Shipped.txt` bugün **0** satırdır (`find src -name PublicAPI.Shipped.txt -exec cat {} + | grep -vcE '^\s*$|^#'` → `0`); değişiklik `1.0.0` GA'dan (K-603) önce ucuzdur.
> **Tüketici yüzeyi:** site: `reference/compatibility.md:135-162` (test edilen sürümler) · `guides/model-providers.md:197,225` (Anthropic `TopK` sütunu) · `capabilities.md:53` · `guides/ef-core.md:44` (Sqlite sürüm damgası) · `concepts/governance.md` ya da oturum sayfası (silmenin ek dosyalarını da sildiği) — sevk edilen: `src/Tracon.Anthropic/README.md` (§ *Known behavior differences*, `:44`, `:141`), `src/Tracon.Google/README.md:107`, `///` sürüm damgaları, `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** [`01-KURULUM-VE-PAKETLEME.md`](manuel-test/01-KURULUM-VE-PAKETLEME.md) (paketlenmiş tüketici) · [`06-SAGLAYICI-DIGER.md`](manuel-test/06-SAGLAYICI-DIGER.md) (Anthropic gerçek anahtar) · [`08-OPENAI-UYUMLU-UCLAR.md`](manuel-test/08-OPENAI-UYUMLU-UCLAR.md) (conversation silme)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman.
2. Kararlar — dosyanın tamamını **okuma**:
   ```bash
   grep -n "K-1016 —\|K-1017 —\|K-1103 —\|K-1104 —\|K-543 —\|K-283 —" docs/KARARLAR.md
   ```
   **K-1016** (`TraconAgentSessionStore` `internal`; köprü oturum açmaz; `isolation` bölümü katlanır, başka bölüm reddedilir) · **K-1017** (yükseltme istek davranışını değiştirmez; Anthropic adaptörü `Extended` ile kurulur) · **K-1103** (model reddi `RejectedSamplingFields`; denetim = yüzey desteği − model reddi) · **K-1104** (değer sınırı `SamplingLimits`) · **K-543** (Dependabot grubu) · **K-283** (bir davranışı düzeltmek çağıranı sessizce değiştirir).
3. Emsal faz — MAF 1.25 yükseltmesi, aynı iş türü:
   ```bash
   git show 2a8149ef:docs/arsiv/fazlar/214-CALISMA-ANI-BAGIMLILIKLARI-VE-MAF-125.md | sed -n '/## Plandan Sapmalar

> Çalışırken dolar. Plan ile gerçek arasındaki fark **gizlenmez**. Kapanışta
> kapanış commit mesajına taşınır.

1. **Açık Sorular cevaplandı (öneriyle).** (1) = A: `SamplingFields` `Temperature` ve `TopP` taşır; `ISamplingSupport`
   uygulamayan sağlayıcı ikisini gönderir sayılır (`ModelProviderRegistry.List`), uygulayan onları adıyla bildirir.
   (2) = A: gerçek API `x-api-key` + `2023-06-01`, `Bearer` + `2023-06-01` ve `Bearer` + `2026-10-21` çiftlerinin
   hepsine `200` verdi (2026-10-28, `GET /v1/models`); sağlık denetimi SDK'yı izler. (3) = A: `SessionDeletion`
   `Tracon.Core`'dadır; `IAttachmentStore` Core'da `TryAddTraconDefault` ile her kurulumda kayıtlıdır. (4) = C:
   `Pkcs` sabitleri değişmedi; SqlClient 7.1.1 hâlâ en yenisi ve boş projede net8.0 `8.0.1`, net9/net10 `9.0.18` ile
   `NU1902` + `NU1903` veriyor.
2. **Plan `A2ASessionStore`'u saymıyordu.** `DelegatingAgentSessionStore` (1.26) `DeleteSessionAsync`'i anahtarı olduğu
   gibi iletir; `A2ASessionStore` türevi geçersiz kılmazsa MAF'ın `isolation` bölümlü anahtarı köprüde `caller::contextId`
   kimliğine katlanır ve `a2a:<özet>` altındaki konuşma yerinde kalırdı. Derleme bunu göstermez (taban artık somut).
   Geçersiz kılma anahtarı diğer üç çağrıdaki gibi `Rewrite` eder ve silmeden önce `SessionOwnershipGate.DeniesAsync`'i
   sorar (silme uçlarının sorusu; `CheckRunSessionAsync` değil: olmayan oturum no-op'tur). Mutasyon kontrolü: anahtarı
   yeniden yazmayan sürüm iki teste düşürüyor.
3. **Silme sırası değişti.** Plan "oturum, sonra ek dosyaları" varsayıyordu (bugünkü `/api/sessions` sırası). Uygulanan
   sıra kopya, ek dosyaları, kayıttır (`AgentSessionManager.DeleteSessionAsync`'in `internal` aşırı yükü
   `afterContentErased` alır; genel imza ve davranış aynı kaldı). Gerekçe: ek dosyası silinemezse kayıt yerinde kalır ve
   tekrar çağrı bitirir; kayıt önce gidince ek dosyasını adlandıracak oturum kalmaz. K-283 taraması: `git grep
   DeleteSessionAsync|DeleteBySessionAsync -- src` üç etkileşimli yol + iki depo uygulaması.
4. **Plan Responses yüzeyinin "hiçbir örnekleme alanını göndermediğini" söylüyordu** (`OpenAIModelProvider` remark'ı).
   Telde ölçüldü (`ModelSamplingWireTests`): `openai-responses` `temperature` ve `top_p` gönderir. Tablo ve remark
   düzeltildi; iki alan eklenmeden bu yanlış görünmüyordu.
5. **İki bulgu aynı yolda.** Katalogda `SupportsSampling = false` olan bir Anthropic modeli ve `Temperature` taşıyan
   tanım artık iki hata verir (`sampling_unsupported` ve `sampling_field_unsupported`, ikisi de `model.temperature`).
   İkisi de doğru; tek bulguya indirmek `SamplingCapabilityCheck`'i sağlayıcı beyanına bağlardı ve kapsamı büyütürdü.
   `AgentValidationWarningTests` ikisini de bekler. README artık `SupportsSampling = false` önermez.
6. **`1024` tanımsız bit oldu.** `ProviderRegistrationCoreTests` tanımsız bit örneği olarak `"256"` kullanıyordu; `256`
   artık `Temperature`. Örnek `"1024"`'e taşındı (aynı kural).
7. **Sürüm damgaları decompile diff'iyle taşındı.** Her damgayı elle yeniden ölçmek yerine iki sürümün tam decompile'ı
   diff'lendi (`ilspycmd -p`, MAF beş derleme; Anthropic adaptör sınıfı; Google adaptör sınıfı). Yalnız değişen kod yeniden
   ölçüldü. Yöntem `hafiza/tedarik-zinciri.md`'de. `dokuman-bakim.py` `SURUM_DAMGASI_KAYDI`'na
   `AnthropicProviderHealthCheck.cs` eklendi (yeni damga).
8. **MAF'ın dosya araması artık 10 sn'de iptal olur** (#9536; `AgentFileStore.SearchTimeout`). Plan bunu saymıyordu;
   dosya belleği ve çalışma alanı `InMemoryAgentFileStore` yolunu kullanır. Matrise yazıldı; gerçek büyük depoda süre
   ölçülmedi.
9. **Dependabot grubu değişmedi.** `Anthropic`, `Google.GenAI` ve `Azure.Core` `agent-stack` grubunda değildir ve
   ayrı PR alır; zincir kopmadı, bu yüzden K-543 satırı ve `dependabot.yml` aynı kaldı.

## Bu Fazda Verilen Kararlar

> Çalışırken dolar. Dört kategoriden birine giren karar (AGENTS.md)
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer; girmeyen karar
> kapanışta commit mesajına taşınır (`faz-tamamlama` Adım 8). Plan numara rezerve
> etmez.

- **K-1114** *(güvenlik, kalıcı-veri)* — oturum silme tek iç yoldur (`SessionDeletion`): kopya, ek dosyaları, kayıt.
- **K-1115** *(public-api)* — `SamplingFields` `Temperature` ve `TopP` taşır; Anthropic SDK 13 üçünü göndermez, beyan
  daralır, tanım reddedilir.
- **Commit mesajına** (kategori dışı): sağlık denetimi SDK'nın kimlik biçimini ve `anthropic-version` değerini yazar
  (gerçek API ikisini de kabul eder, Açık Soru 2 = A); `Pkcs` CVE sabitleri kalır (Açık Soru 4 = C); sürüm damgaları
  decompile diff'iyle taşındı; `ProviderRegistrationCoreTests` tanımsız bit örneği `1024`.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL: `faz-kapat`
> işaretsiz kutuda durur. `faz-kapat` bu tabloyu `docs/FAZ-OLCUMLERI.md`'ye taşır;
> eşik **167**'den itibaren boş bir değer hücresinde kapanışı durdurur.
> `ölçülmedi` **geçerli bir değerdir** — kapı bir sayı değil, bir **karar** arar.
> Kapı bölümün VARLIĞINI denetler, doğruluğunu denetlemez (K-766).

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

> `faz-denetim` çıktısı. Her satır: bulgu · seviye (🔴/🟡/🟢) · sonuç
> (düzeltildi / gerekçelendi / F-NN olarak devredildi). 🟢 kapanışta
> `ADAYLAR.md`'ye gider.

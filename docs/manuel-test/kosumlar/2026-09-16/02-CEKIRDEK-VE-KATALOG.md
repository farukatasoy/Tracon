# 02 — Çekirdek ve Katalog (`CORE`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../02-CEKIRDEK-VE-KATALOG.md`](../../02-CEKIRDEK-VE-KATALOG.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz A zinciri, tek şerit) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `7e3a4de7` donuk |
| **Case sayısı** | 97 (MT-CORE-001..129) |
| **Port** | 5081 (spec 5080 yazar — şerit sapması) |
| **Şema** | `mt_s1` (spec `tracon` yazar — şerit sapması, skill §1.3) |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): sağlayıcı anahtarları şeridin
kendi **ortam değişkenine** okunur. Depo makine genelinde tektir.

**Açılış ölçümü (reset sonrası):**

```
DROP SCHEMA mt_s1 CASCADE -> yeniden kuruldu
Tracon applied 51 migration(s). Schema: mt_s1.
mt_s1.__migrations satir sayisi: 51
GET /api/agents -> 14 agent (azure-support YOK — kimlik yok, beklenen)
/health -> 200 (Degraded: "No model provider has been confirmed healthy yet")
```

⚠️ **`00-INDEKS.md` §4 bayat: "33 migration" diyor, bugün 51.** Bu dosyanın
case'lerini etkilemiyor; kalıcılık ailesinin (`03-KALICILIK-POSTGRESQL.md`)
konusu. Oraya geçen oturum doğrulamalı.

---

## Devir notu

**DOSYA 02 KAPANDI.** `MT-CORE-001..129` — **97 case: 92 ☑ Geçti · 4 ☑ Kaldı ·
1 ☐ Beklemede.** Dört oturumluk iş tek turda koşuldu.

- **Sonraki aile:** zincirin 3.'sü `03-KALICILIK-POSTGRESQL.md` (50 case).
- **Bozuk ön koşul:** yok.

### Bu dosyanın bulguları

| Bulgu | Önem | Kısaca |
|---|---|---|
| `HATA-S1-008` | Düşük | Bilinmeyen `compaction.strategy` reddediliyor ama mesaj reddedilen değeri de geçerli listeyi de söylemiyor (güçlü tiplenmiş enum, JSON okuyucusunda düşüyor) |
| `HATA-S1-009` | Orta | Kod kaynaklı agent'ın `versions` ucu "böyle bir agent yok" diyor; agent var. `IAgentDefinitionStore`'da yokluk, her yerde yokluk sanılıyor |
| `HATA-S1-010` | Orta | Katalogdaki **13 modelin hiçbirinde fiyat yok** → her `run` maliyetsiz kaydediliyor. Mekanizma dürüst (`pricing_source=NotDefined`), eksik olan veri |
| `HATA-S1-011` | Orta | Çakışmayla düşen `run` kayıtta `Completed` görünüyor; çakışmanın izi yalnız akışta kalıyor |
| `HATA-S1-012` | Orta | Sevk edilen iki hata mesajında yarım kalmış Türkçe (`ne 'Input' ne 'Output' contains neither value`). `SourceLanguageTests` iki harfli kelimeleri bilinçli dışladığı için kaçmış |
| `HATA-S1-013` | Düşük | `Custom` kaynaklı agent'a "bu agent kodda tanımlı" deniyor; yönlendirme de yanlış |
| `HATA-S1-014` | Düşük | Analyzer'ın `TRC0007` metni `AddScopedTool`'u anmıyor; runtime metni anıyor. Pratikte görülen analyzer'ınki |

`HATA-S1-009` ve `HATA-S1-013` **aynı sınıftır** ("veritabanında yok" →
"kodda"); kapanışta birlikte değerlendirilmeli.

### Ortam — sonraki oturumun bilmesi gerekenler

🚨 **`echo` sağlayıcısı ile gerçek sağlayıcı aynı anda olamaz.**
`samples/Tracon.Api/Program.cs:277-280` `EchoModelProvider`'ı **yalnız** OpenAI
anahtarı yokken kaydeder. Bu dosyanın 4 case'i `echo`, 73'ü gerçek model
istedi; ortam ikisi arasında bilinçli olarak değiştirildi (kullanıcı kararı,
2026-09-16: gerçek çağrılarla koşulur).

🚨 **`/run` yanıtı SSE'dir ve metin token token gelir.** Ham `grep "Kelime"`
boş dönebilir (`" Far"` + `"uk"`). Birleştirici: `<scratch>/sse.py`.

🚨 **Kod donmuş turda `samples/` değiştirilemez.** Sekiz case bunu istiyordu
(`087 · 088 · 089 · 090 · 091 · 092 · 093 · 094` ve `129`). Hepsi repo
**dışında** kurulan bir tüketici host'uyla koşuldu: `~/tracon-manuel/ohost`
(port 5085), yayınlanmış paketler + örnek kaynak dosyalarının kopyası.
`git status` repo'da boş kaldı.

**Bırakılan aparat** (tur sonunda silinecek): `~/tracon-manuel/ohost` ·
`~/tracon-manuel/skillsiz` · `<scratch>/sse.py`. Çalışan süreç
bırakılmadı.

⚠️ **`00-INDEKS.md` §4 bayat:** "33 migration" diyor, bugün **51**. §3.2 tool
tablosu **4** tool listeliyor, bugün **10**. İkisi de bu dosyanın kapsamı
değil; açık kaleme yazıldı.

### Spec düzeltmeleri (skill §1.1, hepsi doküman kusuru)

| Case | Ne düzeltildi |
|---|---|
| 006 | SSRF kapısı: `Tracon__Egress__AllowPrivateNetworkTargets=true` ön koşulu + kayıt temizliği uyarısı |
| 022 | `Summarize` → `Summarization`; `ContextWindow` değeri katalogdan **türetilir** |
| 023 · 024 | `ResolveAsync` üç parametre alır; Türkçe mesaj alıntısı → İngilizce; 024'e sağlayıcı kaydı ön koşulu |
| 030 | `display_name` sütunu yok — görünen ad `definition` jsonb'sinde |
| 040 · 041 | Tool sayısı sabit değil; `source` kodda tanımlı tool'da `null`'dır (`"generated"` diye bir değer yok) |
| 044 · 045 | `cost_usd` sütunu yok (`input_cost`/`output_cost`); fiyat verisi olmadan maliyet `null` |
| 050 · 051 · 053 · 054 | Oturum geçmişi SQL'den doğrulanamaz — şema farklı **ve** `state` şifreli; doğrulama API'den |
| 060 · 072 · 041 | Türkçe mesaj alıntıları → bugünkü İngilizce metinler (K-228) |
| 062 | `CreateBudget()` artık `TimeProvider` alıyor |
| 065 | Bölüm `AddTracon`'a verilmeli; fiyat anahtarları `Input`/`Output` |
| 082 | `parameters` öğesi `kind` zorunlu alanını taşır |
| 084 · 085 | Talimat metni yalnız trace'ten görülebilir (span'e yazılır, `run_events`'e değil) |
| 087 · 089 | `isEditable` detay ucunda; alan adları `modelId`/`modelProvider` |
| 090 | Var olmayan dizin kaynağı **bozmaz** (`yield break`); bozmak için hatalı JSON gerekir |
| 092 · 093 | `json-file` önceliği 101; aynı tipi iki kez kaydetmek çakışma üretmez (`TryAddEnumerable`) |
| 099 | İki yüzey **farklı** metin taşır; aranacak şey sızıntının olmamasıdır |
| 087-094 · 129 | Donmuş `samples/` yerine repo dışı tüketici host'u tarifi |

---


---

## Ortam sapması — `echo` sağlayıcısı ve OpenAI anahtarı

Oturum 1 açılışta anahtarların **hepsini** export etti ve `MT-CORE-001` düştü:

```
"No model provider named 'echo' is registered.
 Registered providers: anthropic, google, openai, openai-responses, openrouter."
```

Kök neden koddadır, kusur değildir — `samples/Tracon.Api/Program.cs:277-280`:

```csharp
else { tracon.AddModelProvider(new EchoModelProvider()); }
```

`echo` **yalnız** OpenAI anahtarı yokken kayıtlanır ("zero surprises" kuralı,
`Program.cs:7-9`). Bu dosyadaki 23 case `echo` istiyor, yalnız `MT-CORE-004`
gerçek sağlayıcı istiyor (o da ağa çıkmadan, yerel `Validate`).

∴ **Şerit yapılandırması:** `Tracon__Providers__OpenAI__ApiKey=""` bırakıldı.
Kayıtlı sağlayıcılar: `anthropic, echo, google, openrouter`.

**Bedeli:** katalog 14 yerine **12** agent çözüyor — `knowledge-assistant`
(OpenAI embedding ister) ve `voice-assistant` (ElevenLabs ister) düşüyor.
`azure-support` zaten yok. Bu iki agent'a dokunan case çıkarsa ortam bekleyen
olarak işaretlenir.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 89f44ab3:docs/manuel-test/kosumlar/2026-09-16/02-CEKIRDEK-VE-KATALOG.md
> ```

---

## Temiz geçen case'ler (63)

| Case | Durum | Başlık |
|---|---|---|
| MT-CORE-001 | ☑ | Geçerli tanım hatasız doğrulanır |
| MT-CORE-002 | ☑ | `unknown_tool`: kayıtlı olmayan tool adı |
| MT-CORE-003 | ☑ | `unknown_model`: kayıtlı olmayan sağlayıcı |
| MT-CORE-004 | ☑ | `invalid_setting`: tanınmayan sağlayıcı ayarı |
| MT-CORE-005 | ☑ | `unknown_skill`: bağlı olmayan skill adı |
| MT-CORE-007 | ☑ | Kendi kendini çağıran agent reddedilir |
| MT-CORE-008 | ☑ | Doğrulama hiçbir zaman istisna sızdırmaz |
| MT-CORE-009 | ☑ | Boş ve aşırı uzun alanlar |
| MT-CORE-020 | ☑ | Tanınmayan `reasoningEffort` değeri reddedilir |
| MT-CORE-021 | ☑ | `responseFormat` kombinasyonları |
| MT-CORE-032 | ☑ | Bulunmayan agent `null` döner, istisna atmaz |
| MT-CORE-033 | ☑ | Sürüm artışı derlenmiş agent önbelleğini geçersiz kılar |
| MT-CORE-035 | ☑ | Tanım hiçbir zaman kimlik bilgisi taşımaz |
| MT-CORE-042 | ☑ | Onay gerektiren tool sarmalanır ama şeması değişmez |
| MT-CORE-043 | ☑ | Tanım yalnız kayıtlı tool'a işaret edebilir |
| MT-CORE-051 | ☑ | Oturum silinir ve geçmiş gider |
| MT-CORE-052 | ☑ | Var olmayan oturumun silinmesi hata vermez |
| MT-CORE-053 | ☑ | İki oturum birbirini görmez |
| MT-CORE-061 | ☑ | Script çalıştırma onaysız açılamaz |
| MT-CORE-063 | ☑ | Hassas veri varsayılan olarak kaydedilmez |
| MT-CORE-070 | ☑ | Bellek içi depolar veritabanı olmadan çalışır |
| MT-CORE-071 | ☑ | `FakeModelProvider` kuyruğu bir kez tüketilir |
| MT-CORE-073 | ☑ | Enum'lar JSON'da ad olarak yazılır |
| MT-CORE-074 | ☑ | Uygulama yeniden başlatıldığında kod agent'ları geri gelir |
| MT-CORE-075 | ☑ | Kültür sözlüğü boş agent'ta `culture` verilse de davranış değişmez |
| MT-CORE-076 | ☑ | Eşleşen kültür kendi talimatını seçer |
| MT-CORE-077 | ☑ | Bölge alt etiketi ebeveynine düşer: `tr-TR` → `tr` |
| MT-CORE-078 | ☑ | Eşleşmeyen kültür varsayılana düşer, hata VERMEZ |
| MT-CORE-080 | ☑ | Arka arkaya farklı kültürlerle `run` — önbellek yanlış dili TUTMAZ |
| MT-CORE-082 | ☑ | Zorunlu parametre eksikken `run` başlamaz; ad hatada geçer |
| MT-CORE-083 | ☑ | Fazladan parametre sessizce yutulmaz |
| MT-CORE-084 | ☑ | Değer JSON yapısını bozmaz |
| MT-CORE-085 | ☑ | Paylaşılan talimat bloğu prepend edilir; bloğa referans veren blok reddedilir |
| MT-CORE-091 | ☑ | Bozuk kaynağa ait adla `run` denemesi ham hata metnini sızdırmaz |
| MT-CORE-096 | ☑ | `AddAgentSource` kayıt yüzeyi: generic ve factory aşırı yüklemeleri tek instance üretir |
| MT-CORE-097 | ☑ | Complex tool sonucu canonical JSON ve output limiti taşır |
| MT-CORE-098 | ☑ | Doğrulanmamış tool registry startup'ta reddedilir |
| MT-CORE-100 | ☑ | BYOK desteklemeyen provider'da tenant credential fail-closed'tır |
| MT-CORE-101 | ☑ | `AddRunJudge` üç overload'ı AgentSource deseniyle aynı idempotency'i taşır |
| MT-CORE-103 | ☑ | `AgentDefinitionCompiler` ayrıştırmasından sonra `support` agent aynı şekilde çalışır |
| MT-CORE-104 | ☑ | Culture taşıyan tanım en yakın culture talimatını çözer |
| MT-CORE-105 | ☑ | Shared instructions sync yolda açık hata verir, async yolda çözülür |
| MT-CORE-106 | ☑ | `InMemoryRunStore` ayrıştırması sonrası ağaç toplamları ve kiracı yalıtımı doğru kalır |
| MT-CORE-107 | ☑ | `AddScopedTool` ile kaydedilmiş bir tool her çağrıda taze DI kapsamı alır |
| MT-CORE-109 | ☑ | `[Range]` üretilen şemaya `minimum`/`maximum` yazar |
| MT-CORE-110 | ☑ | `[MinLength]` `string`'de `minLength`, dizide `minItems` yazar |
| MT-CORE-111 | ☑ | Uyumsuz kısıt `TRC0010` üretir, derleme başarılı kalır |
| MT-CORE-113 | ☑ | `tr-TR` yerelinde ondalık ayırıcı `.` kalır |
| MT-CORE-114 | ☑ | Aynı girdi iki derlemede bit düzeyinde aynı şema üretir |
| MT-CORE-112 | ☑ | Kısıtlı bir tool ile gerçek `run` başarıyla biter |
| MT-CORE-115 | ☑ | Nesne parametresi nested JSON Schema düğümü üretir |
| MT-CORE-116 | ☑ | Nesne dizisi parametresi `items` düğümünde nesne üretir |
| MT-CORE-117 | ☑ | Nesne üyesindeki `[Range]` üye düğümüne `minimum`/`maximum` yazar |
| MT-CORE-118 | ☑ | Context'te bildirilmeyen nesne tipi `TRC0011` ile derlemeyi durdurur |
| MT-CORE-119 | ☑ | 3'ü aşan nesne derinliği `TRC0012` ile derlemeyi durdurur |
| MT-CORE-120 | ☑ | Bir cycle `TRC0012` üretir, generator asılmaz |
| MT-CORE-122 | ☑ | Nesne parametreli tool paketlenmiş dış tüketicide `PublishAot` altında çalışır |
| MT-CORE-123 | ☑ | `harness.loop` verilmeyen agent bugünkü davranışını korur |
| MT-CORE-124 | ☑ | `completionMarker` ölçütü marker gelene kadar döner |
| MT-CORE-125 | ☑ | Ulaşılamayan bir ölçüt Tracon'in kendi tavanında durur |
| MT-CORE-126 | ☑ | Bilinmeyen bir ölçüt `kind`'i kayıtta `400` ile reddedilir |
| MT-CORE-127 | ☑ | Döngü iterasyonları kayda geçer |
| MT-CORE-128 | ☑ | Bütçe tavanı dolunca döngü yeni iterasyon açmaz |

## Ayrıntı taşıyan case'ler (34)

## MT-CORE-006 — `Inconclusive`: erişilemeyen MCP sunucusu `Valid`'i düşürmez

**Gerçek sonuç**

⚠️ **Ön koşul ilk denemede tutmadı — spec bayat.** `PUT /api/mcp-servers/olu-mcp`
**400** döndü:

```
"title": "Address not allowed",
"detail": "The target resolves to a private network address (127.0.0.1);
           set 'Tracon:Egress:AllowPrivateNetworkTargets' to true to allow it."
```

Bu bir kusur değil, **SSRF kapısı** — case 127.0.0.1 kullanıyor ve kapı o
adresi varsayılanda engelliyor. Uygulama `Tracon__Egress__AllowPrivateNetworkTargets=true`
ile yeniden başlatıldı (kapının kendi mesajının söylediği ayar); kayıt 200 döndü.
`Ön koşul` bu ayarı taşıyacak şekilde düzeltildi (skill §1.1 istisnası).

Sonra:
```json
{ "valid": true, "inconclusive": true, "messages": [
  { "severity": "Warning", "code": "mcp_unreachable", "path": null,
    "message": "A fresh list could not be fetched from MCP servers for missing
                tool names (timeout or connection failure). The result is inconclusive." }]}
SURE: 0.16 sn   ·  /health sonrasi 200
```

Dört beklentinin dördü de tuttu: 8 sn'nin **çok** altında · `inconclusive:true`
· `mcp_unreachable` + `Warning` · uygulama ayakta. **`valid` düşmedi** —
belirsizlik ile geçersizlik ayrı tutuluyor, case'in asıl iddiası bu.

📋 **Not (kapsam sınırı):** kapalı bir port **anında** `connection refused`
veriyor, yani 5 sn'lik zaman aşımı bu case'te hiç çalışmadı. Zaman aşımı yolunu
ölçmek **yavaş** (asılı kalan) bir sunucu ister; bu senaryo `18-MCP-VE-A2A.md`
kapsamındadır.

**Temizlik:** `DELETE /api/mcp-servers/olu-mcp` → 204. Silinmeseydi sonraki tüm
doğrulamalar `inconclusive` dönerdi; silindikten sonra `unknown_tool` davranışı
geri geldi (doğrulandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-022 — Sıkıştırma ayarları tetikleyicisiz olamaz

**Gerçek sonuç**

⚠️ **Spec'in girdisi iki yerde yanlıştı; davranış doğru ve spec'in
varsaydığından daha iyi.**

```
c1 '"strategy":"Summarize"'        -> HTTP 400 deserialization hatasi
c2 '"strategy":"BoyleBirSeyYok"'   -> HTTP 400 deserialization hatasi
c3 ContextWindow, maxContextWindowTokens YOK -> valid:TRUE   (spec hata bekliyordu)
```

**1) `Summarize` diye bir strateji yok.** Enum
(`src/Tracon.Abstractions/Agents/CompactionStrategyKind.cs`):
`None · SlidingWindow · Truncation · ToolResult · Summarization · ContextWindow`.
Doğru adla tekrar koşuldu:

```
c1b '"strategy":"Summarization"' (tetikleyicisiz) -> valid:false
  "Agent 'c1b' selected the 'Summarization' compaction strategy but gave no
   trigger (at least one of TriggerTokens/TriggerMessages/TriggerTurns is required)."
```
∴ **c1 beklentisi tutuyor** — yalnız spec'teki strateji adı yanlışmış.

**2) `c3` katalogdan TÜRETİYOR, bu bilinçli.**
`AgentDefinitionCompiler.Compaction.cs:145-168` bunu belgeliyor: değer
verilmezse `ModelDescriptor.ContextWindowTokens`'tan türetilir, çünkü
"kullanıcının elle kopyalayacağı değer zaten model bağlamasında duruyor".
Derleme **yalnız iki kaynağın da boş olduğu** durumda düşer. Kanıtlandı:

```
c3b: model "katalogda-yok" -> valid:false
  "Agent 'c3b' selected the ContextWindow compaction strategy but did not
   supply MaxContextWindowTokens, and its model ('echo/katalogda-yok') has no
   context window size in the catalog either. Set MaxContextWindowTokens explicitly."
```
∴ `c3` beklentisi **bayat**; mesaj ortaya çıktığında spec'in istediği alanı
adlandırıyor, üstelik **ikinci** kaynağı da söylüyor.

`Beklenen sonuç` ve girdiler bu iki bulguya göre düzeltildi (skill §1.1).

🚨 **c2 GERÇEK BİR BULGU — `HATA-S1-008`.** Bilinmeyen strateji adı reddediliyor
ama mesaj **reddedilen değeri taşımıyor**:

```
"The JSON value could not be converted to Tracon.CompactionStrategyKind.
 Path: $.compaction.strategy | LineNumber: 0 | BytePositionInLine: 99."
```

`BoyleBirSeyYok` metinde **geçmiyor**; geçerli değerler de listelenmiyor.
Karşılaştır: `MT-CORE-020` (`reasoningEffort`) aynı sınıf hatada hem reddedilen
değeri hem geçerli listeyi veriyor. Aradaki fark, enum'un `CompactionStrategyKind`
olarak **güçlü tiplenmiş** olması: istek Tracon'un doğrulayıcısına hiç
ulaşmıyor, ASP.NET Core'un JSON okuyucusunda düşüyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

**Gerçek sonuç — yeniden koşum 2026-09-19 (Aile L kapanışı sonrası)**

Canlı host (`artifacts/bin/Tracon.Api/release`, `--urls :5087`,
`ASPNETCORE_ENVIRONMENT=Development`, OpenAI anahtarı komut satırından boşaltıldı
ki `EchoModelProvider` kaydolsun — case'in kendi girdisi `echo/echo-1`).
Dört alt kontrolün dördü de:

```
c1b '"strategy":"Summarization"' (tetikleyicisiz)
  -> valid:false · "Agent 'c1b' selected the 'Summarization' compaction strategy
                    but gave no trigger (at least one of TriggerTokens/
                    TriggerMessages/TriggerTurns is required)."
c2  '"strategy":"BoyleBirSeyYok"'
  -> HTTP 400 · "'BoyleBirSeyYok' is not a valid value. The valid values are:
                 None, SlidingWindow, Truncation, ToolResult, Summarization,
                 ContextWindow, Pipeline. Path: $.compaction.strategy"
c3  ContextWindow, maxContextWindowTokens YOK -> valid:true   (katalogdan türetildi)
c3b ContextWindow, model "katalogda-yok"      -> valid:false · iki kaynağı da söylüyor
```

`c2` artık reddedilen değeri **ve** geçerli listeyi taşıyor; `BytePositionInLine`
gibi tüketiciye hiçbir şey anlatmayan ayrıntı gitti, yararlı olan tek parça
(`$.compaction.strategy`) kaldı. Case'in `Kaldı` sebebi buydu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-008 — Sıkıştırma stratejisi adı yanlışsa hata reddedilen değeri de geçerli listeyi de söylemiyor

| | |
|---|---|
| **Önem** | Düşük |
| **Bulunduğu case** | MT-CORE-022 (c2) |
| **Sınıf** | Tanı kalitesi · enum deserialization |

**Repro**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" \
  -d '{"name":"c2","model":{"provider":"echo","model":"echo-1"},
       "compaction":{"strategy":"BoyleBirSeyYok","triggerTokens":1000}}'
```

**Gözlenen:** HTTP 400 · `"The JSON value could not be converted to
Tracon.CompactionStrategyKind. Path: $.compaction.strategy | LineNumber: 0 |
BytePositionInLine: 99."`

**Beklenen sınıf davranışı:** `reasoningEffort` ile aynı — reddedilen değeri
aynen taşı ve geçerli değerleri listele.

**Kök neden:** `strategy` alanı `CompactionStrategyKind` enum'una güçlü
tiplenmiş; `System.Text.Json` dönüşümü Tracon'un doğrulama katmanından **önce**
yapıyor. `reasoningEffort` ise string olarak alınıp Tracon içinde çözümlendiği
için iyi mesaj üretebiliyor.

**Sınıf taraması gerekir (kapanışta):** aynı desendeki her enum alanı. İlk
bakışta `responseFormat.kind`, `compaction.strategy` ve OpenAPI'den türeyen
diğer enum'lar aynı sınıfta. `BytePositionInLine` gibi bir ayrıntı tüketiciye
hiçbir şey anlatmıyor; alan adı (`$.compaction.strategy`) tek yararlı kısım.

**Etki:** düşük — istek yine reddediliyor, sessiz kabul yok. Bedeli teşhis
süresi: kullanıcı geçerli adı dokümanda aramak zorunda.

**✅ KAPANDI 2026-09-18 (Aile L).** 👤 Karar: **tek converter, bütün enum
alanları** (K-821). Mesaj artık şu:

```
'BoyleBirSeyYok' is not a valid value. The valid values are: None,
SlidingWindow, Truncation, ToolResult, Summarization, ContextWindow,
Pipeline. Path: $.compaction.strategy
```

🚨 **İlk tasarım ÖLÇÜMLE reddedildi.** Converter'ı enum tiplerinin kendisine
`[JsonConverter]` ile takmak akla yatkındı ve **yayınlanan sözleşmeyi
bozuyordu**: `JsonSchemaExporter` yalnız framework'ün kendi enum converter'ını
tanır, bu yüzden `docs/openapi/tracon.json` **42 enum'un `enum` listesini birden
kaybetti** (287 satır silindi; `CompactionStrategyKind` şeması yalnız
`description` olarak kaldı) ve üretilen her istemci union'larını yitirirdi. Bu
ancak belgeyi tazeleyip `git diff`'e bakınca görüldü — derleme ve testler
yeşildi.

Çözüm, aynı sonucu sözleşmeye hiç dokunmadan verir: converter **istek gövdesi
options'ına** takılır. Bir options converter'ı tip düzeyindeki attribute'u
geçer (çözüm sırası: property attribute → options listesi → tip attribute'u),
yani gövdeler için kazanır, şema üretiminde hiç görünmez, ve `Tracon.Abstractions`
public yüzeyi büyümez.

🚨 **İkinci katman: `AgentEndpoints` gövdeyi KENDİ okuyordu.** Converter
takıldıktan sonra test hâlâ kırmızı kaldı. Sebep: `BindAgentDefinitionRequestAsync`
`RequestBodyBinding`'in **elle yazılmış ikinci bir kopyasıydı** ve
`ReadFromJsonAsync`'i **hiç options vermeden** çağırıyordu. Üç agent tanımı ucu
bu yüzden paylaşılan okuyucunun eklediği her şeyi sessizce kaçırıyordu —
yalnız bu converter'ı değil, `RespectNullableAnnotations`'ı da. İki okuyucunun
başlığı ve durum kodu **aynı** olduğu için kopya fark edilmeden durabilmişti.
Artık delege ediyor.

🚨 **Üçüncü katman: yolu System.Text.Json eklemiyor.** STJ `JsonException.Path`
alanını bir converter'ın attığı istisna için de doldurur, ama **mesaja** yalnız
kendi ürettiği istisnalarda ekler. Daha iyi bir cümle yazan converter bu yüzden
stok mesajın tek yararlı parçasını kaybediyordu. `RequestBodyBinding.Describe`
yolu bir kez, her gövde hatası için ekliyor.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ üç fonksiyonel testin ikisi düzeltmeden önce kırmızı |
| Sınıf taraması | ☑ `grep -rn "ReadFromJsonAsync" src/` → gövdeyi elle okuyan başka **iki** yer var (OpenAI uyumluluk uçları) ve ikisi de `JsonElement` okuyor, yani enum bağlama yok. Kaydın saydığı iki alan yerine **tüm** enum alanları kapsandı |
| Testler | 3 fonksiyonel test; ikisi ayrı sözleşme/uç, biri geçerli değerin hâlâ kabul edildiğini kilitliyor |

---

## MT-CORE-023 — Skill kataloğu kayıtlı değilken skill isteyen tanım

**Gerçek sonuç**
```
dotnet run -c Release -> cikis 0
beklenen istisna: Agent 'skill-isteyen' refers to skill 'olmayan-skill',
                  but the skill was not found.
AgentName: skill-isteyen
```

Dört beklentinin dördü de tuttu: çıktı `beklenen istisna:` ile başlıyor ·
mesaj eksik skill'i adıyla söylüyor · `AgentName` doğru · `🚨 istisna ATILMADI`
**görünmedi**. 2026-08-15'in düzeltilmiş öncülü (erişilebilir dal, tanımın var
olmayan skill'e işaret ettiği daldır) bugün de geçerli.

⚠️ **Spec'e iki düzeltme** (skill §1.1 istisnası):

1. **Script derlenmiyordu.** `catalog.ResolveAsync("skill-isteyen")` →
   `error CS1501: No overload for method 'ResolveAsync' takes 1 arguments`.
   Gerçek imza `IAgentCatalog.cs:32`:
   `ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)`.
   Çağrı `ResolveAsync("skill-isteyen", null, CancellationToken.None)` yapıldı.
2. **Beklenen mesaj metni Türkçe yazılmıştı** ("...isaret ediyor ancak skill
   bulunamadi."). Sevk edilen metin K-228'den beri **İngilizce**. Bugünkü
   metne göre güncellendi.

**Sapma:** proje `<scratch>` yerine `~/tracon-manuel/skillsiz` altında kuruldu
(spec böyle diyor); yerel feed oturum 2'den hazır (`0.0.0-preview.0.789`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-024 — Derleme hatası hangi agent'ta olduğunu söyler

**Gerçek sonuç**

⚠️ **İlk koşumda case'in ölçmek istediği dala VARILAMADI.** Spec'in script'i
hiçbir model sağlayıcısı kaydetmiyor; derleme tool denetiminden **önce**
sağlayıcıda düşüyor:

```
Agent 'tool-eksik' could not be compiled: No model provider named 'echo' is
registered. Registered providers: no provider is registered. For OpenAI, call
`builder.AddTracon().UseOpenAI(apiKey)`.
```

(`echo` yalnız örnek uygulamanın kendi `EchoModelProvider`'ıdır — yalın bir
tüketici projesinde yoktur.) `Tracon.Testing` eklenip
`.AddModelProvider(new FakeModelProvider("echo"))` ile sağlayıcı kaydedildi;
`Beklenen sonuç` bu ön koşulu taşıyacak şekilde düzeltildi (skill §1.1).

Sonra, **dört beklentinin dördü de tuttu:**

```
Agent 'tool-eksik' references the following tools, but they are not registered
in code: hayali_tool. Registered tools: Var. Tools are defined only in code;
register them with `builder.AddTracon().AddTool(...)`.
```

| Beklenti | Gözlenen |
|---|---|
| agent adını taşır | `Agent 'tool-eksik'` ✅ |
| eksik tool adını taşır | `hayali_tool` ✅ |
| kayıtlı tool'ları listeler, `Var` görünür | `Registered tools: Var` ✅ |
| `builder.AddTracon().AddTool(...)` yönlendirmesi | aynen var ✅ |

🚨 **2026-08-15'in düzeltmesi bugün de doğrulandı:** kayıtlı ad `Var`
(**metot adı**), `var_olan_tool` (öznitelik adı) **değil**. `.AddTool(delegate)`
`[TraconTool]` özniteliğini hiç okumuyor. Yirmi agent'lı bir kurulumda mesaj
hangi agent, hangi tool ve hangi alternatifler olduğunu tek satırda veriyor.

**Yan gözlem:** derleme `warning TRC0009` üretti (`x` parametresinin
açıklaması yok) — `MT-PKG-040`/`049`'da ölçülen aynı analyzer, tüketici
projesinde de çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-030 — Kod kaynaklı agent veritabanı tanımını yener

**Gerçek sonuç**
```
1) GET /api/agents -> support: origin="Code" (AD olarak, sayi degil)
                      displayName="Support Assistant"

2) POST /api/agents {"name":"support",...} -> HTTP 409
   "title": "Agent name in use"
   "detail": "'support' is an agent defined in code and cannot be changed from
              the management API. Code wins name conflicts, so a definition
              written with the same name would never resolve."

3) GET /api/agents -> support TEK kayit · origin="Code"
                      displayName hala "Support Assistant" (SAHTE Destek DEGIL)
```

**Doğrulama sorgusu** (`mt_s1`): `SELECT name, version FROM agent_definitions
WHERE name='support'` → **0 satır**.

Spec iki sonucu da kabul ediyordu ("ya reddedilir ya kaydedilir ama katalogda
görünmez"); gerçekleşen **daha güçlü** olanı: kayıt hiç oluşmadı, yani veritabanında
asla çözülmeyecek ölü bir satır birikmiyor. Güvenlik özelliği ayakta — arayüzden
gelen tanım kod tanımını ezemiyor ve 409 gerekçesini kullanıcıya açıklıyor.

⚠️ **Spec'e düzeltme** (skill §1.1): doğrulama sorgusu `display_name` sütununu
okuyordu, öyle bir sütun **yok**. Şema:
`id · tenant_id · name · version · definition (jsonb) · created_at · updated_at`
— görünen ad `definition` içindedir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-031 — Katalog ada göre sıralı döner

**Gerçek sonuç**
```
['cached-support', 'claude-support', 'claude-thinking', 'gemini-strict-filter',
 'gemini-support', 'manuel-a', 'manuel-b', 'openrouter-support', 'order-summary',
 'researcher', 'router', 'summarizer', 'support', 'translator']
SIRALI
```
(`manuel-a`/`manuel-b` `MT-CORE-007`'den kalan kayıtlardır.)

📋 **İkinci beklenti bu veriyle ölçülemez:** "sıralama ordinal'dir: büyük
harfler küçük harflerden önce gelir". Katalogdaki 14 adın **hepsi küçük harf**;
ordinal ile harf-duyarsız sıralama aynı sonucu veriyor. Ayrımı görmek büyük
harfle başlayan bir agent adı ister. Bu bir kusur değil, kapsam sınırı —
`MT-CORE-033`+ veri yazan case'lerden sonra tekrar bakılabilir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-034 — Kod kaynaklı agent'ta sürümlü çözümleme reddedilir

**Gerçek sonuç**
```
GET /api/agents/support/versions      -> HTTP 404
  { "title": "Agent not found",
    "detail": "There is no agent named 'support'." }        🚨 YANLIS IFADE

GET /api/agents/manuel-surum/versions -> HTTP 200 · IKI surum
  version 2 | IKINCI SURUM TALIMATI.
  version 1 | BIRINCI SURUM TALIMATI.

GET /api/agents/support               -> HTTP 200   (yani agent VAR)
```

**Tutan:** `manuel-surum` için iki sürüm listelendi · hiçbir istekte 500 yok ·
`support` için sürümlü çözümleme reddedildi (4xx).

🚨 **Tutmayan — `HATA-S1-009`.** Case'in gerekçesi "bu **açıkça** söylenmelidir"
diyor; 404 gövdesi gerekçeyi söylemiyor, üstelik **yanlış** bir şey söylüyor:
`support` vardır (`GET /api/agents/support` → 200 ve katalogda görünür), yalnız
sürüm geçmişi yoktur. Kullanıcı agent'ı ekranda görürken "böyle bir agent yok"
cevabı alıyor.

Spec'in operasyonel testi ("gövde `kod kaynagi` taşır **veya** HTTP 4xx") lafzen
sağlanıyor; ama case'in adı ve gerekçesi açık bir sebep istiyor. Bu ikiliği
kaldırmak için `Beklenen sonuç` netleştirildi (skill §1.1) ve case `Kaldı`
işaretlendi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

**Gerçek sonuç — yeniden koşum 2026-09-19 (Aile L kapanışı sonrası)**

Canlı host (`:5087`). `manuel-surum` bu veritabanında yoktu; case'in kendi
adımlarıyla yeniden kuruldu (`POST` → 201, `PUT` → 200) ve koşum sonunda
silindi (`DELETE` → 204).

```
GET /api/agents/support/versions      -> HTTP 404
  { "title": "Agent has no version history",
    "detail": "'support' is defined in code. A code definition is not stored by
               Tracon, so it has no version history; its history is the
               application's source history." }

GET /api/agents/support               -> HTTP 200   (agent VAR; iki cevap artik celiskisiz)

GET /api/agents/manuel-surum/versions -> HTTP 200 · IKI surum
  version 2 | IKINCI SURUM TALIMATI.
  version 1 | BIRINCI SURUM TALIMATI.

GET /api/agents/support/versions/1/diff/2 -> HTTP 404 · AYNI gerekce
  (sinif taramasinin bulduğu ikinci yuzey; onceden "Agent 'support' has no
   version 1." diyordu, yani agent'in burada surumlendigini iddia ediyordu)

GET /api/agents/hicbir-yerde-olmayan/versions -> HTTP 404
  { "title": "Agent not found", "detail": "There is no agent named
    'hicbir-yerde-olmayan'." }   (ters yon korundu)
```

Case'in gerekçesinin istediği şey — var olan bir agent için "yok" dememek ve
sebebi açıkça söylemek — sağlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-009 — Kod kaynaklı agent'ın sürüm listesi "böyle bir agent yok" diyor

| | |
|---|---|
| **Önem** | Orta |
| **Bulunduğu case** | MT-CORE-034 |
| **Sınıf** | Tanı kalitesi · kod/DB kaynak ayrımı |

**Repro**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/support/versions" -H "$APB"
# -> 404 "There is no agent named 'support'."
curl -s -o /dev/null -w "%{http_code}\n" "$APU/api/agents/support" -H "$APB"
# -> 200        (ayni agent, ayni an)
```

**Kök neden** — `src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs:664-672`:

```csharp
if (await definitions.GetAsync(name, cancellationToken).ConfigureAwait(false) is null)
{
    return NotFound(name);
}
```

Denetim `IAgentDefinitionStore`'a, yani **veritabanı** deposuna bakıyor. Kod
kaynaklı agent orada yoktur; dolayısıyla "yok" sonucuna varılıyor. Oysa
"veritabanı deposunda yok" ile "böyle bir agent yok" aynı şey değildir.

**Aynı durumda doğru davranan uç var** — `MT-CORE-030`, `POST /api/agents`:

```
409 "Agent name in use"
"'support' is an agent defined in code and cannot be changed from the
 management API. Code wins name conflicts, ..."
```

Yani proje bu ayrımı nasıl anlatacağını **biliyor**; `versions` ucu o bilgiyi
kullanmıyor.

**Önerilen davranış (kapanışta karar):** `support/versions` ya 200 + boş liste
(sürüm geçmişi yok) ya da 409/404 + kod kaynaklı olduğunu söyleyen bir gövde
dönmeli. Hangisi seçilirse seçilsin, mesaj var olan bir agent için "yok"
dememeli.

**Sınıf taraması gerekir (kapanışta):** `IAgentDefinitionStore.GetAsync ... is
null → NotFound` deseni. `AgentEndpoints.cs`'te `rollback` ve
`versions/{a}/diff/{b}` uçları aynı şekilde kuruluyor gibi görünüyor; kod
kaynaklı agent'la çağrıldıklarında aynı yanıltıcı metni üretme ihtimalleri
**kapanışta ölçülmeli**.

**Etki:** orta — veri kaybı yok, güvenlik sorunu yok; bedeli tüketicinin
teşhis süresi ve dokümana duyduğu güven.

**✅ KAPANDI 2026-09-18 (Aile L).** 👤 Karar: **durum kodu `404` kalır, gerekçe
doğruyu söyler** (K-820). Sürüm geçmişi kaynağı gerçekten yoktur; değişen tek
şey çağıranın üzerine iş yaptığı cümledir. `409` reddedildi — o kod bir
**değişiklik** çakışması içindir ve bu bir `GET`'tir. `200 + boş liste` de
reddedildi: "hiç sürüm yok" ile "burada sürümlenmiyor" ayrımını siler.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ altı fonksiyonel testin beşi düzeltmeden önce kırmızı |
| Sınıf taraması | ☑ kaydın şüphesi **yarı** doğru çıktı. `rollback` **zaten** doğruydu — `GuardCodeAgentAsync`'ten geçiyor. `versions/{a}/diff/{b}` ise kaydın öngörmediği bir **ikinci** biçimde yanlıştı: agent varlığını hiç kontrol etmiyor, doğrudan `"Agent 'x' has no version N."` diyordu — yani "bu agent burada sürümleniyor, yalnız o numara yok" iddiası; iki yarısı da yanlış |
| Düzeltme | Ortak `NoVersionHistoryAsync`: `Code` · `Custom` · bilinmeyen ad için üç ayrı cevap, tek yerde. `diff` artık varlık kontrolünü **iki sürüm okumasından önce** yapıyor |
| Testler | 6 fonksiyonel test; ikisi ters yönü kilitliyor (bilinmeyen ad **hâlâ** "There is no agent named 'x'." alır) |
| Tüketici yüzeyi | İki ucun OpenAPI `description` metni; yayınlanan belge tazelendi |

---

## MT-CORE-040 — Tool listesi ad, açıklama, şema ve kaynak taşır

**Gerçek sonuç**
```
GET /api/tools -> 10 tool (spec UC bekliyordu)

cancel_order            onay=True   effect=Destructive  izin=orders.cancel
estimate_shipping_cost  onay=False
get_order_status        onay=False
get_slow_report         onay=False
list_recent_orders      onay=False
list_voices             onay=False
mark_preview_ready      onay=False
read_shopping_cart      onay=False
speak                   onay=False
transcribe              onay=False

SIRALI ✅ · her tool'un description'i dolu ✅ · her tool'un jsonSchema'si dolu
ve parametrelerini tanimliyor ✅
```

**Tutan:** sıralama · `cancel_order` tek onay isteyen · açıklamalar ve şemalar
dolu.

⚠️ **İki beklenti bayat, ikisi de doküman kusuru (skill §1.1, düzeltildi):**

1. **"Üç tool görünür" → 10.** Örnek uygulama Faz 52'den beri büyümüş.
   `00-INDEKS.md` §3.2 de yalnız **4** tool listeliyor; o tablo da bayat
   (bu turda düzeltilmedi, dosya 02'nin kapsamı değil — açık kaleme yazıldı).
2. **"`source` alanı `generated`'dır" → `null`, ve bu DOĞRU.**
   `src/Tracon.Abstractions/Tools/ToolDescriptor.cs:30-38`:

   > *"The tool's source. **null** for tools defined in code; the server name
   > for tools coming from a remote MCP server."*

   `"generated"` değeri kod tabanında **hiç geçmiyor**. Alan kod/uzak-MCP
   ayrımı için var ve arayüzde ayrı bir rozet olarak gösteriliyor. Kodda
   tanımlı 10 tool'un onunda da `null` olması beklenen davranıştır.

**Yan gözlem — case'in yazıldığı zamandan beri eklenen alanlar:**
`effect` · `requiredPermission` · `runsOnClient` · `safeToRepeat` ·
`timeout` · `maxOutputBytes`. `cancel_order` için `effect=Destructive` ve
`requiredPermission=orders.cancel` dolu; bunlar bu dosyanın değil ilgili
ailelerin konusu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-041 — Aynı adda iki tool açılışta hata verir

**Gerçek sonuç**
```
beklenen istisna: More than one tool is registered with name 'ayni_ad'.
                  Tool names must be unique.
```
`🚨 istisna ATILMADI` satırı **görünmedi**. "Son kayıt kazanır" davranışı yok;
belirsizlik açılışta patlıyor.

⚠️ **Spec'e düzeltme:** beklenen metin Türkçe yazılmıştı
(`'ayni_ad' adinda birden cok tool kaydedilmis`). Sevk edilen metin K-228'den
beri İngilizce; bugünkü metne göre güncellendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-044 — Tool gerçekten çağrılır ve sonucu kayda geçer

**Gerçek sonuç** (gerçek OpenAI çağrısı · model `gpt-5.4-mini`)
```
POST /api/agents/support/run  {"message":"ORD-1001 siparisim nerede?",
                               "sessionId":"musteri-42"}
-> SSE akisi, finishReason "stop", "ORD-1001" yanitta 2 kez gecti ✅

 agent_name | status | model_id     | input_tokens | output_tokens | tool_cagrisi
------------+--------+--------------+--------------+---------------+--------------
 support    |      1 | gpt-5.4-mini |          402 |            25 |            1
```

`status = 1` = `RunStatus.Completed` (`src/Tracon.Abstractions/Runs/RunStatus.cs:22`).

**Tutan (dört beklentinin üçü):** yanıt `ORD-1001` taşıyor · `get_order_status`
**tam bir kez** çağrıldı · `runs.status` tamamlandı · `tool_invocations`'ta o
tool için tam bir satır var.

🚨 **Tutmayan: maliyet.** Spec `runs.cost_usd`'nin dolu ve pozitif olmasını
bekliyor; alan **boş**. İki ayrı sebep var ve ikisi de kayda geçti:

1. ⚠️ **`cost_usd` diye bir sütun yok** (doküman kusuru, düzeltildi). Gerçek
   şema: `input_cost · output_cost · cost_currency · pricing_source`.
2. 🚨 **Fiyat verisi hiçbir modelde yok** → `HATA-S1-010`. `pricing_source = 2`
   dönüyor; enum'a göre bu "fiyat hiçbir kaynakta tanımlı değil, bu yüzden
   maliyet alanları `null` — **sıfır değil**, sıfır yalan olurdu"
   (`PricingSource.cs:19-21`). Yani **mekanizma doğru davranıyor**; eksik olan
   veridir.

`Doğrulama sorgusu` gerçek sütun adlarına göre düzeltildi.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

**Yeniden koşum — 2026-09-18 (Aile G kapanışı) · ☑ GEÇTİ**

Gerçek OpenAI `gpt-5.4-mini`, gerçek PostgreSQL (`mt_g` şeması). Token sayıları
**turdakiyle birebir aynı** çıktı (402 / 25), yani ölçülen tek fark fiyattır:

```
 agent_name | status | model_id     | input_tokens | output_tokens | input_cost   | output_cost  | cur | pricing_source
 support    |      1 | gpt-5.4-mini |          402 |            25 | 0.0001005000 | 0.0000500000 | USD | 0
```

`pricing_source` **2 (`Unknown`) → 0 (`Catalog`)**. Hesap elle doğrulandı:
402 × 0,25 / 1e6 = 0,0001005 ✓ · 25 × 2,00 / 1e6 = 0,00005 ✓. Dört beklentinin
dördü de karşılandı; `tool_invocations` satır sayısı yine **1**.

Örnek uygulamanın katalogu artık fiyat taşıyor (K-809) ve mekanizma
değişmedi — eksik olan yalnız veriydi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-045 — Tool gerekmeyen istek tool çağırmaz

**Gerçek sonuç**
```
POST /api/agents/support/run {"message":"Merhaba","sessionId":"musteri-99"}
-> finishReason "stop", run tamamlandi

 status | input_tokens | output_tokens | input_cost | pricing_source | tool_sayisi
--------+--------------+---------------+------------+----------------+-------------
      1 |          343 |            13 |            |              2 |           0   <- "Merhaba"
      1 |          402 |            25 |            |              2 |           1   <- "ORD-1001..."
```

**Case'in asıl iddiası TUTTU:** selam isteği için `tool_invocations` satır
sayısı **0**; bir önceki sipariş isteği için **1**. Her istekte tool çağıran
bir kurulum değil — gereksiz maliyet üretilmiyor. `run` tamamlandı.

🚨 **`runs.cost_usd` yine boş** — aynı kök neden, `HATA-S1-010`. Bu case'in
kendi davranışsal iddiası doğru olduğu için bulgu 044'e bağlandı; case maliyet
beklentisi yüzünden `Kaldı`.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

**Yeniden koşum — 2026-09-18 (Aile G kapanışı) · ☑ GEÇTİ**

Aynı oturum, aynı kurulum. Token sayıları yine **turdakiyle birebir** (343 / 13):

```
 agent_name | input_tokens | output_tokens | input_cost   | output_cost  | pricing_source | tool_calls
 support    |          343 |            13 | 0.0000857500 | 0.0000260000 |              0 |          0
```

343 × 0,25 / 1e6 = 0,00008575 ✓ · 13 × 2,00 / 1e6 = 0,000026 ✓. Case'in asıl
iddiası (selam isteği tool çağırmaz) yine tuttu: **0** tool çağrısı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-010 — Hiçbir modelde fiyat verisi yok; her `run` maliyetsiz kaydediliyor

| | |
|---|---|
| **Önem** | Orta (yayın öncesi karar gerektirir) |
| **Bulunduğu case** | MT-CORE-044 · MT-CORE-045 |
| **Sınıf** | Katalog verisi · maliyet takibi |

**Repro**
```bash
curl -s "$APU/api/models" -H "$APB"   # her modelde:
#   "inputCostPerMillionTokens": null
#   "outputCostPerMillionTokens": null
#   "cachedInputCostPerMillionTokens": null
```

**Ölçüm — sistemik, tek bir modele özgü değil:**

| Sağlayıcı | Model sayısı | Fiyat taşıyan |
|---|---|---|
| anthropic | 3 | 0 |
| google | 3 | 0 |
| openai | 3 | 0 |
| openai-responses | 3 | 0 |
| openrouter | 1 | 0 |
| **toplam** | **13** | **0** |

∴ Örnek uygulamada **her** `run` `input_cost`/`output_cost` `null` ve
`pricing_source = NotDefined` ile kaydediliyor.

**Mekanizma doğru, veri eksik.** `PricingSource.cs:19-21` sıfır yazmayı açıkça
reddediyor ("zero would be a lie") ve `NotDefined` ile durumu dürüstçe
bildiriyor. Yani bu bir hesaplama kusuru değil; fiyat kaynağının boş olması.

**Karar gerektiren (kapanış / `nuget-danismani`):** maliyet takibi tüketiciye
dönük bir yetenek olarak duyuruluyor mu? Duyuruluyorsa ya katalog fiyatlarla
gelmeli ya da `Tracon:Pricing` yapılandırmasının **zorunlu** olduğu
dokümantasyonda ve örnek uygulamada açıkça görünmeli. Bugün sessizce boş
geçiyor: tüketici `cost` sütunlarını görüyor, doldurmuyor ve sebebini ancak
`pricing_source` enum'unu okuyarak anlıyor.

**Etki:** orta — veri kaybı yok, yanlış sayı **üretilmiyor** (en önemlisi bu);
bedeli, maliyet raporlamasının kutudan çıkmamasıdır.

---

**✅ KAPANDI 2026-09-18 (Aile G).** Kaydın teşhisi doğruydu ve iki yarısı da
kapatıldı (K-808, K-809):

1. **Mekanizma artık susmuyor.** `UnpricedModelWarningService` açılışta her
   fiyatsız `provider/model` çiftini `Warning` ile adlandırır ve
   `GET /api/diagnostics` aynı listeyi `pricing` altında bildirir. İki okuyucu
   **tek** `UnpricedModels.Find` metodunu çağırır — iki kopya kaçınılmaz olarak
   kayar ve raporu uyarıyı doğrulamak için açan operatör farklı bir liste
   görürdü.
2. **Örnek uygulama artık dolu.** 13 modelin 13'ü fiyat taşıyor; rakamlar
   `//Models` yorumunda **açıkça ÖRNEK** olarak işaretlendi (bakımı yapılan bir
   fiyat listesi değil — K-032). Paket içine gömülü fiyat tablosu yine yok.

Canlı ölçüm (gerçek kurulum, `mt_g` şeması):

```
GET /api/models            -> 13 model, 13'ü fiyatlı  (turda: 13 / 0)
GET /api/diagnostics       -> "pricing": { "pricedModels": 13,
                                           "unpricedModels": [], "currency": "USD" }
acilis kaydi (echo saglayicisi acikken):
  warn: Tracon.UnpricedModelWarningService[1]
        1 catalog model(s) carry no price ... Unpriced: echo/echo-1
```

Veri kaybı olmadığı kayıtta zaten doğruydu: `POST /api/stats/recalculate-costs`
fiyat sonradan girilince geçmişi geri hesaplar. `HATA-S1-025`'ten (tool
harcamasının **kalıcı** kaybı) bu ailedeki ikizini ayıran şey budur.

---

## MT-CORE-050 — Oturum yoksa oluşturulur, varsa yüklenir

**Gerçek sonuç**
```
1) run "Benim adim Faruk." (sessionId manuel-oturum-01) -> tamam
2) GET /api/sessions -> manuel-oturum-01 VAR
3) run "Adim neydi?" (ayni sessionId) -> "Adınız Faruk."     ✅ gecmis yuklendi
4) GET /api/sessions/manuel-oturum-01 -> 4 mesaj, kronolojik:
     user      | Benim adim Faruk.
     assistant | Merhaba Faruk! Size nasıl yardımcı olabilirim?
     user      | Adim neydi?
     assistant | Adınız Faruk.
```

Dört beklentinin dördü de tuttu.

🚨 **Ölçüm tuzağı — sonraki oturumlar için:** `/run` yanıtı **SSE akışıdır** ve
metin token token gelir. Ham `grep "Faruk"` **boş döner**, çünkü kelime
`" Far"` + `"uk"` diye bölünür. Bu bir kusur değil, akışın doğasıdır. Parçaları
birleştiren yardımcı `<scratch>/sse.py` olarak yazıldı ve bu dosyanın kalan
tüm akış case'lerinde kullanıldı.

⚠️ **Doğrulama sorgusu çalışmıyor, düzeltildi (skill §1.1):** spec
`conversation_items ci JOIN sessions s ON s.id = ci.session_id` ve
`s.external_id` kullanıyor; **üçü de yok**. Gerçek şema:

| Tablo | Gerçek |
|---|---|
| `sessions` | `id` (text, dış kimliğin **kendisi**), `external_id` sütunu yok |
| `conversation_items` | `conversation_id`'ye bağlı, `session_id` sütunu yok |
| `conversations` | `session_id` sütunu **yok** |

🚨 **Daha önemlisi: oturum geçmişi SQL'den okunamaz — şifreli.**
`sessions.state` bir zarf taşıyor: `{"$apEnc":…, "kid":…, "n":…, "c":…}`.
Örnek uygulama `AddContentProtection()` çağırıyor
(`samples/Tracon.Api/Program.cs:227`) ve AES-256 anahtarı `user-secrets`'tan
geliyor (`appsettings.json:31-36`). Yani at-rest koruması **çalışıyor** ve bu
case'in doğrulaması API üzerinden yapılmalıdır.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-054 — Aynı oturuma eşzamanlı iki çalıştırma

**Gerçek sonuç**

🚨 **İlk bakışta kayıp güncelleme gibi görünüyor, DEĞİL.** Oturumda 4 yerine 2
mesaj kalıyor ve oturum `version` 1'de duruyor — ama kaybeden istek **sessizce**
düşmüyor, SSE akışında açık bir hata olayı alıyor:

```
event: error
data: {"type":"TraconSessionConflictException",
       "message":"Another request also opened session 'manuel-yaris' at the
                  same time and saved it before us. Retry again shortly."}
```

**Dört tekrarda dördü de aynı** (tekrarlanabilirlik ölçüldü):

| Oturum | mesaj | version | çakışma hatası alan |
|---|---|---|---|
| `manuel-yaris` | 2 | 1 | 1. istek |
| `manuel-yaris-2` | 2 | 1 | 2. istek |
| `manuel-yaris-3` | 2 | 1 | 2. istek |
| `manuel-yaris-4` | 2 | 1 | 1. istek |

Her seferinde **tam bir** istek kazanıyor, **tam bir** istek açık çakışma
hatası alıyor. Hangisinin kazandığı yarışa bağlı, ki beklenen budur.

∴ Spec'in üçüncü maddesi gerçekleşti: *"Bir çakışma denetimi varsa isteklerden
biri açık bir çakışma hatası döner; bu da **kabul edilebilir**. Sessiz kayıp
kabul edilemez."* Sessiz kayıp **yok**; optimistic concurrency çalışıyor ve
mesaj çağırana ne yapacağını (`Retry again shortly`) söylüyor. Hiçbir istek
500 dönmedi.

🚨 **Yan bulgu — `HATA-S1-011`:** çakışmayla biten `run` veritabanında
**`Completed`** olarak kaydediliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-011 — Çakışmayla düşen `run`, kayıtta `Completed` görünüyor

| | |
|---|---|
| **Önem** | Orta |
| **Bulunduğu case** | MT-CORE-054 |
| **Sınıf** | Gözlemlenebilirlik · `run` durum doğruluğu |

**Repro** — aynı `sessionId` ile iki eşzamanlı `run`, sonra:
```sql
SELECT session_id, status, error_type, error_message
FROM runs WHERE session_id LIKE 'manuel-yaris%';
```

**Gözlenen** (8 satır, 4 çakışmalı koşumun tamamı):
```
 session_id     | status | error_type | error_message
----------------+--------+------------+---------------
 manuel-yaris   |      1 |            |
 manuel-yaris   |      1 |            |      <- bu run CAKISMAYLA dustu
 ...
```

İkisi de `status = 1` (`RunStatus.Completed`), `error_type` ve `error_message`
**boş**. Oysa isteklerden biri çağırana `TraconSessionConflictException`
döndürdü ve oturumuna hiçbir şey yazamadı.

**Neden önemli:** `runs` tablosu kontrol düzleminin kendi kaydıdır. Operatör
tabloya baktığında iki başarılı koşum görüyor; çakışmanın izi **yalnız**
o an akışı dinleyen istemcide kalıyor. `RunStatus.Failed` (2) tam da bunun
için var. Oturum sahibi "mesajım kayboldu" dediğinde kayıtta hiçbir kanıt yok.

`AGENTS.md`: *"Gözlemlenebilirlik işlevselliği bozmaz"* — burada tersi
geçerli: işlev doğru, **gözlemlenebilirlik** eksik.

**Sınıf taraması gerekir (kapanışta):** akış başladıktan **sonra** atılan her
istisna. Akış açıldıktan sonra `run` kaydı erkenden `Completed`'a çekiliyorsa
aynı sınıfın başka örnekleri olabilir (sağlayıcı kesintisi, guard engellemesi,
iptal). Ölçüm noktası: `run` kaydının `status` yazıldığı an ile SSE `error`
olayının üretildiği an.

**Etki:** orta — veri kaybı yok (kaybeden taraf zaten yazamadı, çağıran
bilgilendirildi); bedeli olay sonrası teşhisin imkânsızlaşması.

---

### ✅ KAPANDI — 2026-09-18 (Aşama 2, Aile B)

**Kök neden ölçüldü.** `AgentEndpoints` `SaveSessionAsync`'i `agent.RunAsync`'ten
**sonra** çağırıyor, yani `CompleteAsync(Completed)` çoktan yazılmış oluyor.
Run gerçekten tamamlandı: model çağrıldı, token harcandı, yanıt üretildi.
Düşen şey run'ın **oturum yazımı**.

**Kullanıcı kararı 👤 (2026-09-18):** durum `Completed` **kalır**, çakışma yeni
bir olay olarak kaydedilir. `Failed` demek run'ın maliyetini ve ürettiği yanıtı
da başarısız gösterirdi, ve hata sınıflandırma/uyarı hatları bunu gerçek bir
kesinti sanabilirdi.

**Düzeltme.** `RunEventType.SessionWriteConflicted = 32` eklendi (32, çünkü
`run_events.type` bir `smallint` sütunu ve var olan bir üyenin sayısal değeri
asla kaydırılamaz). Her iki yol da — akışlı ve akışsız — `SaveSessionAsync`'i
`SaveSessionRecordingConflictAsync` üzerinden çağırıyor; çakışmada olay run'a
yazılır, `Text` oturum kimliğini taşır, sonra istisna olduğu gibi devam eder ve
çağıran yine `409` alır.

Olay yazımı **best effort**'tur ve `409`'un yerine geçmez: gözlemlenebilirlik
işlevselliği bozmaz. Olayı alamayan bir store çağıranın yanıtını değiştirmez,
yalnız bir uyarı loglanır.

**Sınıf taraması.** Kayıt "akış başladıktan sonra atılan her istisna" taramasını
istiyordu. Aynı oturumda `HATA-S4-003` ile birlikte kapandı: o kusur run
satırının yazılmasından **önceki** pencereyi, bu kusur **sonraki** pencereyi
kapsıyor. Aradaki pencere (`MoveNextAsync` döngüsü) zaten `Failed` yazıyordu ve
ölçümle doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-060 — Geçersiz `MaxPayloadLength` açılışı durdurur

**Gerçek sonuç**
```
1048576: KABUL
1048577: RED - TraconRunRecordingOptions.MaxPayloadLength must be between
                0 and 1048576. Actual value: 1048577.
     -1: RED - ... Actual value: -1.
```
Dört beklentinin dördü de tuttu: sınırın **tam üstü** geçerli, bir üstü ve
negatif reddedildi, red mesajı **gelen değeri** yazıyor (`Actual value`).

⚠️ **Spec'e düzeltme:** beklenen metin Türkçe alıntılanmıştı
(`0 ile 1048576 arasinda olmalidir`); sevk edilen metin İngilizce (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-062 — Agent grafiği sınırlarının varsayılanı vardır

**Gerçek sonuç**
```
MaxDepth      : 3          ✅
MaxTotalTokens: 200000     ✅
MaxTotalRuns  : 25         ✅
sifirla butce token siniri: SINIRSIZ   ✅
```
Dört beklentinin dördü de birebir tuttu. Sınırsız bırakılan bir kurulumun ilk
yanlış tanımı faturayla öğrenmesi engellenmiş.

⚠️ **Spec'e düzeltme:** `CreateBudget()` artık parametresiz değil —
`CreateBudget(TimeProvider)`. Script `error CS7036` ile derlenmiyordu;
`TimeProvider.System` verildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-064 — Yapılandırmadan gelen negatif fiyat reddedilir

**Gerçek sonuç**
```
beklenen red: TraconPricingOptions: price for 'echo:echo-1' cannot be negative.
```
Üç beklentinin üçü de tuttu: çıktı `beklenen red:` ile başladı · mesaj
sağlayıcı **ve** modeli (`echo:echo-1`) adlandırıyor ·
`🚨 negatif fiyat KABUL EDILDI` görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-065 — `Pricing` altındaki rezerve anahtarlar sağlayıcı sayılmaz

**Gerçek sonuç**

⚠️ **Spec'in script'i iki yerde yanlıştı; ikisi de düzeltildi.**

**1) Bölüm `AddTracon`'a verilmiyordu.** Elle (AOT) bağlayıcı
`AddTracon(services, IConfiguration section)` içinden koşuyor
(`TraconServiceCollectionExtensions.cs:64 → Bind → BindPricing:198`).
Spec `AddTracon()`'u **argümansız** çağırıp ayrıca
`services.Configure<TraconOptions>(section)` yapıyordu; o yol yansıma tabanlı
binder'ı kullanır ve `Providers` sözlüğünü **dolduramaz**:

```
A) spec'teki hali : Currency=USD · Saglayici sayisi=0 · Ses=elevenlabs
B) AddTracon(section): Saglayici sayisi=1 · Saglayicilar=echo
```

**2) Fiyat anahtarları C# özellik adıyla yazılmıştı.** Yapılandırmada geçerli
adlar `Input` · `Output` · `CachedInput`'tur
(`TraconServiceCollectionExtensions.Binding.Models.cs:51-53`), C# özellik adı
`InputCostPerMillionTokens` **değil**.

Doğru script ile üç beklentinin üçü de tuttu:
```
Currency          : USD          ✅
Saglayici sayisi  : 1            ✅
Saglayicilar      : echo         ✅ Currency ve Voice listede YOK
Ses saglayicilari : elevenlabs   ✅ Voice kendi sozlugune bagli
echo/echo-1 Input : 0,25         ✅ deger gercekten baglandi
```

💡 **Kodun kendisi bu case'i adıyla anıyor** (`Binding.Models.cs:62-68`):
yanlış anahtar adıyla yazılan bir fiyat kaydı **sessizce düşürülmüyor**, boş
bir `ModelPriceOverride` olarak sözlüğe giriyor ve doğrulayıcı açılışta
reddediyor. Yanlış anahtarla denendiğinde tam olarak bu gözlendi (aşağıda) —
K-034'ün "sessizce yok sayma" yasağı çalışıyor.

🚨 **O red mesajı bir kusur ortaya çıkardı → `HATA-S1-012`.**

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-012 — Sevk edilen üç hata mesajında yarım kalmış Türkçe çeviri var

| | |
|---|---|
| **Önem** | Orta (K-228 dil sınırı ihlali · tüketiciye görünür) |
| **Bulunduğu case** | MT-CORE-065 |
| **Sınıf** | Dil sınırı · `SourceLanguageTests` kör noktası |

**Repro** — `Tracon:Pricing` altına anahtar adı yanlış bir fiyat yaz:
```json
{ "Tracon": { "Pricing": { "echo": { "echo-1": { "InputCostPerMillionTokens": 0.25 } } } } }
```
Açılışta:
```
OptionsValidationException: TraconPricingOptions: 'echo:echo-1'
  ne 'Input' ne 'Output' contains neither value. Check the key name.
                  ^^^^^^^^^^^^^^^^^^^^ Turkce "ne ... ne ..." yapisi
```

Cümle iki dilin ortasında kalmış: Türkçe *"ne X ne Y"* kalıbı İngilizce
*"contains neither value"* ile birleştirilmiş. Tüketici bozuk İngilizce
okuyor.

**Kaynak — `src/Tracon.Core/TraconOptionsValidator.cs`, iki satır:**

| Satır | Metin |
|---|---|
| 292 | `'{providerName}:{modelName}' ne 'Input' ne 'Output' ` |
| 312 | `'Voice:{providerName}:{modelName}' ne '{PerMillionCharacters}' ne '{PerMinute}' ` |

(`:342` aynı bloktaki üçüncü mesajdır ve **doğru** İngilizce yazılmış — düzeltme
sırasında atlanan iki satır bunlar.)

🚨 **Kapı bunu neden yakalamadı:** `SourceLanguageTests` iki kaynakla tarıyor —
Türkçe harfler (`çğıöşü`) ve Türkçe kelime listesi. `ne` ikisine de girmiyor:
Türkçe harf taşımıyor ve kelime listesi **"every two-letter word"**'ü
*bilinçli olarak* dışlıyor (`SourceLanguageTests.cs:138-141`), çünkü iki
harfli Türkçe kelimeler İngilizce kelimelerle ve tanımlayıcılarla çakışıyor.
`TraconOptionsValidator.cs` taban çizgisinde (`source-language-baseline.txt`)
**yok** — yani bu borç kayıtlı değil, kapıdan **kaçmış**.

**Kapanışta yapılacak:** iki satır düzeltilir (ör. `neither 'Input' nor
'Output' is set`). Kapının kör noktası **ayrı** bir karardır: iki harfli
kelimeleri listeye almak yanlış pozitif üretir; alternatif, `ne X ne Y` gibi
Türkçe **kalıpları** (tek kelimeleri değil) arayan bir desen eklemektir.

**Sınıf taraması gerekir:** aynı kalıbın başka örnekleri. `grep -rn " ne '" src/`
bugün **2** satır buluyor, ikisi de bu dosyada. Diğer iki-harfli Türkçe
kelimeler (`ve`, `bu`, `da`, `de`, `o`, `mi`) ayrıca taranmalı.

**Etki:** orta — işlev doğru, doğrulama doğru reddediyor; bedeli paketin
profesyonel görünümü ve K-228'in ihlali.

---

**✅ KAPANDI 2026-09-18 (Aile K).** Üç satır düzeltildi ve kapının kör noktası
kapatıldı.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ iki yeni test düzeltmeden önce kırmızı; ölçülen metin: `TraconPricingOptions: 'echo:echo-1' ne 'Input' ne 'Output' contains neither value.` |
| Düzeltme | Üç satır `Images` dalının **zaten doğru** olan cümlesine hizalandı: `'{anahtar}' contains neither 'X' nor 'Y'. Check the key name.` Çifte olumsuzlama da kalktı |
| Kapı | `SourceLanguageTests` iki harfli kelimeleri artık toptan atlamıyor; `ne · ya · ki · mi · mu · da · ve` listeye girdi |
| Sınıf taraması | ☑ taranan ağacın tamamında 16 iki-harfli aday ölçüldü; listeye giren yedisinin **sıfır** çakışması var. Çakışan üçü dışarıda kaldı: `de` ve `en` BCP-47 etiketi olarak literal geçiyor, `bu` çevrilmiş arayüz metnini doğrulayan E2E testinde |
| Taban çizgisi | **boş kaldı** — yeni kural repoda başka hiçbir satır bulmuyor |

---

## MT-CORE-072 — Kimlikler zaman sıralı UUIDv7'dir

**Gerçek sonuç**
```
sirali: True                    ✅
damga farki (sn): 0,0           ✅ 1 sn'den kucuk
beklenen red: The identifier is not a UUID version 7 value. (Parameter 'id')
🚨 v4 KABUL EDILDI satiri: gorunmedi   ✅
```
Dört beklentinin dördü de tuttu. Beş kimlik ordinal sıralamada artan —
veritabanı indeksi için önemli olan özellik.

⚠️ **Spec'e düzeltme:** beklenen metin Türkçe alıntılanmıştı
(`surum 7 degeri degil`); sevk edilen metin İngilizce (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-079 — `Accept-Language` başlığı talimatı DEĞİŞTİRMEZ

**Gerçek sonuç**
```
-H "Accept-Language: tr", govdede culture YOK -> HTTP 200
  "I’m good, thanks. How are you?"
```
Başlık **yok sayıldı**; yanıt varsayılan İngilizce talimata göre üretildi.
K-232'nin çizgisi korunuyor: sunucu içeriği ambient bir tarayıcı başlığından
beslenmiyor.

📋 **Koşum notu (ürün değil, ölçüm):** ilk denemede HTTP 400 göründü. Sebep
koşum tarafındaki shell yardımcı fonksiyonunun ek başlığı bölmesiydi
(`-H Accept-Language:` + `tr` iki ayrı argüman oluyordu). Düz `curl` ile
tekrarlandığında 200 döndü. Ürün kusuru **değildir**.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-081 — 👤 Agent editöründe dil sekmesi; sürüm diff'i iki dili de gösterir

**Gerçek sonuç** (Playwright · `http://localhost:5081/tracon`)

**1) Panel** — `/agents/manuel-kultur/edit`, "Instructions" altında
"Instructions by culture":
```
[textbox placeholder="tr" değer="tr"]  [Remove culture]
[textbox: "Kisa cevap ver ve TAMAMEN TURKCE yaz."]
[Add culture]
yardim metni: "A culture tag such as \"en\" or \"tr\". A run whose requested
               culture matches none of these uses the instructions above."
```
Her kültür satırı için **bir dil kodu alanı ve bir metin alanı** var ✅

**2) Satır eklendi ve kaydedildi:** `de` / `Kurz antworten.` →
"Save new version" → agent detayına dönüldü, rozet **`db · v2`**.
Tanım JSON'u: `"instructionsByCulture": { "de": "Kurz antworten.",
"tr": "Kisa cevap ver ve TAMAMEN TURKCE yaz." }` ✅ yeni sürüm oluştu

**3) Sürüm karşılaştırma** — v1 ve v2 seçildi:
```
Instructions        1 | 1 | Answer briefly and ENTIRELY IN ENGLISH.   (degismedi)
Instructions (de)   1 | + | Kurz antworten.                           ← EKLENEN
Instructions (tr)   1 | 1 | ...                                       (degismedi)
```
`de` için **ayrı bir "Instructions (de)" bölümü** belirdi ve eklenen metin
`+` ile vurgulandı ✅

**4) Dil değişimi (EN → TR)** — başlıktaki `en` düğmesi:

| İngilizce | Türkçe |
|---|---|
| Summary | Özet |
| Instructions | Talimatlar |
| **Instructions (de)** | **Talimatlar (de)** |
| Version history | Sürüm geçmişi |
| Definition / Copy | Tanım / Kopyala |
| Edit / Delete / Roll back | Düzenle / Sil / Geri al |
| "Comparing v1 → v2" | "v1 → v2 karşılaştırılıyor." |
| tooltip: "Stored definition, version 2." | "Saklanan tanım, sürüm 2." |

Panel etiketleri, tablo başlıkları **ve tooltip'ler** çevrildi ✅

🚨 **Doğru olan ayrım:** agent'ın **içeriği** çevrilmedi —
`"Answer briefly and ENTIRELY IN ENGLISH."` her iki dilde de aynen duruyor.
K-228'in çizgisi tam burada: çeviri konsol yüzeyine ait, kullanıcının verisine
değil.

**Konsol:** her sayfada `browser_console_messages` kontrol edildi. Tek tekrar
eden hata `HATA-S1-004`'ün CSP ihlali (oturum 2'de kaydedilmişti) —
**bağımsız olarak doğrulandı**:
```
Executing inline script violates the following Content Security Policy
directive 'script-src 'self''. ... The action has been blocked.
@ http://localhost:5081/tracon:41
```
📋 **Yeni bilgi:** bu hata **ölümcül değil** — konsol tümüyle çalışıyor
(token girişi, gezinme, düzenleme, kaydetme, diff, dil değişimi hepsi çalıştı).
`HATA-S1-004`'ün etkisi bu turda ilk kez sınırlandırılmış oldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Parametre bloğu (082–086) — ortak kurulum

`PARAM_AGENT` = `manuel-param` (201):
```json
{ "name": "manuel-param",
  "instructions": "Musteri: {{musteri}}. Kisa cevap ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "parameters": [ { "name": "musteri", "kind": "Text", "required": true } ] }
```

⚠️ **Spec'te eksik alan:** `parameters` öğesi `kind` **zorunlu** alanını
taşımalıdır (`AgentParameter.cs:28`, `AgentParameterKind`: `Text · Number ·
Boolean`). `kind` olmadan istek `400 "missing required properties including:
'kind'"` döner. Ön koşul düzeltildi.

---

## MT-CORE-086 — Aşırı uzun parametre değeri koşuyu düşürür

**Gerçek sonuç**
```
musteri = 5000 karakter -> HTTP 400
  "detail": "Parameter 'musteri' exceeds the maximum value length.",
  "tooLongParameters": ["musteri"]
```
Beklenen `detail` metni ve `tooLongParameters` dizisi **birebir** tuttu.
Koşu başlamadı, model çağrılmadı. 2026-08-22'nin canlı doğrulaması bugün de
geçerli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Özel agent kaynağı bloğu (087–089) — ortak kurulum

🚨 **Spec'in ön koşulu kod donmasını çiğniyor:** `samples/Tracon.Api/Program.cs`'e
geçici kayıt eklemeyi istiyor. Bu turda `samples/` **donuktur** (kural 1).

**Çözüm — repo'ya dokunmadan kendi tüketici host'um kuruldu**
(`~/tracon-manuel/ohost`, port **5085**). İzlek A'nın yaptığı şeyin aynısı:

- `Tracon.AspNetCore` · `Tracon.UI` · `Tracon.Testing` (yerel feed,
  `0.0.0-preview.0.789`)
- `samples/Tracon.Samples.CustomAgentSource`'un **üç `.cs` dosyası kopyalandı**
  (paketlenmiş bir sürümü yok; örnek kod zaten kopyalanmak için var)
- `agents/greeter.json` = spec'in verdiği içerik
- `.AddModelProvider(new FakeModelProvider("echo").EchoesUserMessage())` —
  `echo` yalın bir host'ta yok, sağlayıcı kaydı şart
- `app.MapTracon("/tracon", o => o.AuthToken = "...")` — 🚨 `AuthToken`
  yapılandırmadan **otomatik okunmaz**, host'un kendisi verir
  (`TraconEndpointOptions.cs:65`); örnek uygulama bunu `Program.cs:944`'te
  elle yapıyor

`git status` repo'da boş kaldı; donma bozulmadı.

---

## MT-CORE-087 — Özel `IAgentSource` ajanı `Custom` origin ile listelenir

**Gerçek sonuç**

**Adım 1 — API:**
```
GET /api/agents        -> greeter | origin: Custom | sourceName: json-file
GET /api/agents/greeter -> isEditable: false
                           descriptor.origin: Custom
                           descriptor.sourceName: json-file
```
Üç beklenti de tuttu.

⚠️ **Spec'e düzeltme:** `isEditable` **liste** ucunda yoktur, **detay** ucunda
vardır. Liste öğesinin alanları: `name · origin · sourceName · model ·
version · displayName · description · toolNames · skillNames ·
callableAgentNames · usesHarness · updatedAt`. Case'in `grep -A3 '"name":
"greeter"'` komutu `isEditable`'ı hiçbir zaman göremez.

**Adım 2 — konsol** (`http://localhost:5085/tracon/agents/greeter`):
```
rozet     : "json-file"  · tooltip: "Provided by source \"json-file\"."   ✅
Edit baglantisi: YOK (manuel-kultur'da vardi)                              ✅
salt-okunur gorunum                                                        ✅
```
İki beklenti de tuttu.

🚨 **Ama açıklama metni yanlış → `HATA-S1-013`:** sayfadaki bilgi kutusu
*"This agent is declared in code..."* diyor. `greeter` **kodda tanımlı
değildir**; bir JSON dosyasından gelir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-013 — `Custom` kaynaklı agent'a "bu agent kodda tanımlı" deniyor

| | |
|---|---|
| **Önem** | Düşük |
| **Bulunduğu case** | MT-CORE-087 (adım 2) |
| **Sınıf** | Arayüz metni · kod/Custom kaynak ayrımı |

**Gözlenen:** `greeter` (origin `Custom`, source `json-file`) detay sayfasında:

> *"This agent is declared in code. A code definition is validated at compile
> time and cannot be changed from the console — **edit the application source
> instead**."*

**Kök neden** — `src/Tracon.UI/frontend/src/screens/agent-detail.tsx:139`:
```tsx
{!isEditable && (
  <div ...>{t('agentDetail.codeNotice')}</div>
)}
```
Koşul yalnız `!isEditable`'a bakıyor; `origin` **hiç okunmuyor**. Sözlükte de
tek metin var (`locales/en/agents.ts:51`) — `Custom` kaynak için karşılığı yok.

**Neden önemli:** yönlendirme de yanlış. Kullanıcı "uygulama kaynağını düzenle"
diyor; doğru eylem `agents/greeter.json` dosyasını düzenlemektir. Sayfa
gerekli bilgiyi **zaten gösteriyor** (rozet `json-file`, tooltip "Provided by
source"), yalnız bilgi kutusu onu kullanmıyor.

**`HATA-S1-009` ile aynı sınıf:** "veritabanında yok" → "kodda" varsayımı.
Kapanışta ikisi birlikte değerlendirilmeli.

**✅ KAPANDI 2026-09-18 (Aile L).** Bilgi kutusu artık `descriptor.origin`'i
okuyor; `Custom` için yeni bir metin (`agentDetail.sourceNotice`, en + tr)
kaynağı **adıyla** söylüyor ve doğru eylemi gösteriyor. Sayfa bu bilgiyi zaten
rozette gösteriyordu; eksik olan kutunun onu kullanmasıydı.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ iki vitest testi düzeltmeden önce kırmızı |
| Sınıf taraması | ☑ `grep -rn "isEditable" src/Tracon.UI/frontend/src` → beş kullanım; kalan dördü düğme görünürlüğüdür ve `Code`/`Custom` ayrımı onlar için anlamsızdır (ikisi de düzenlenemez). Rozet (`OriginBadge`) ayrımı zaten yapıyordu |
| Testler | `agent-detail.test.tsx`; ikinci test kod kaynaklı agent'ın metnini kilitliyor |

---

## MT-CORE-088 — `Custom` kaynağa ait ada `PUT`/`POST` çakışması `409` döner

**Gerçek sonuç**
```
PUT  /api/agents/greeter -> HTTP 409 · title "Custom-source agent cannot be modified"
POST /api/agents         -> HTTP 409 · title "Agent name in use"

ikisinin de detail'i AYNI:
 "'greeter' belongs to the 'json-file' agent source and cannot be changed
  from the management API."
```
İki beklenti de tuttu: ikisi de 409 · `detail` **kaynağın adını** taşıyor ·
404 değil, doğru çakışma anlatılıyor. Başlıklar iki uç için ayrı ayrı
anlamlandırılmış, gerekçe ortak.

📋 **Karşıtlık:** `MT-CORE-034`'te (`HATA-S1-009`) aynı proje, var olan bir
agent için "böyle bir agent yok" diyordu. Burada doğrusu yapılıyor — yani
desen biliniyor, `versions` ucunda uygulanmamış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-089 — `Custom` kaynaktaki agent gerçek bir `run` tamamlar

**Gerçek sonuç**
```
POST /api/agents/greeter/run {"message":"selam"} -> yanit "Echo: selam"

run kaydi:
  status       : Completed
  modelId      : "echo-1"     ✅ bos degil
  modelProvider: "echo"       ✅ bos degil
  agentVersion : 0
```
Koşu tamamlandı ve kayıt **doğru atıf** taşıyor: kaynağın kendi `ListAsync`'inden
gelen dondurulmuş descriptor kullanılmış. Uydurma `Origin=Code` / `Model=null`
**yok**.

⚠️ **Spec'e düzeltme:** alan adları `modelId` ve `modelProvider`'dır
(`model`/`provider` değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-090 — Bozuk bir kaynak varken `GET /api/agents` sağlıklı kalır

**Gerçek sonuç**

🚨 **Spec'in "bozuk kaynak" tarifi bu örnek için YANLIŞ.** Case var olmayan bir
dizin öneriyor ("`ListAsync`'i her çağrıda istisna fırlatsın diye"), ama
`JsonFileAgentSource.ReadAllAsync` (`:142-145`) bunu **açıkça** ele alıyor:

```csharp
if (!Directory.Exists(_directory)) { yield break; }
```

Var olmayan dizin **boş** kaynak üretir, bozuk kaynak değil. Ölçüldü: log
tertemiz, hiçbir hata yok.

**Gerçekten bozmak için** dizine ayrıştırılamayan bir JSON kondu
(`agents/aaa-broken.json` = `{ bu gecerli JSON degil`); ad sıralamada
`greeter.json`'dan **önce** gelsin diye `aaa-` öneki verildi.

Sonra, host'a bir de kod agent'ı eklenerek (yerleşik kaynağın görünürlüğünü
kanıtlamak için) ölçüldü:

```
GET /api/agents -> HTTP 200                                  ✅ fail-open
agent sayisi: 1 | [('kod-agent', 'Code')]                    ✅ yerlesik kaynak GORUNUYOR
greeter (bozuk kaynak) listede YOK                           ✅

sunucu logu:
  fail: Tracon.CompositeAgentCatalog[0]
        Agent source 'json-file' failed during list.          ✅ kaynak ADIYLA
```

Üç beklentinin üçü de tuttu. Bozuk bir uzantı bütün katalogu düşürmüyor;
hata yutulmuyor da — loga kaynağın adıyla yazılıyor.

`Ön koşul` gerçek bozma yöntemine göre düzeltildi (skill §1.1).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-092 — `GET /api/diagnostics` kayıtlı kaynakları önceliğe göre listeler

**Gerçek sonuç**
```json
"agentSources": [
  { "name": "code",      "priority": 0,   "implementation": "Tracon.CodeAgentSource" },
  { "name": "database",  "priority": 100, "implementation": "Tracon.DefinitionStoreAgentSource" },
  { "name": "json-file", "priority": 101, "implementation": "Tracon.Samples.CustomAgentSource.JsonFileAgentSource" }
]
```
Üç girdi, **doğru öncelik sırasıyla** — case'in asıl iddiası bu ve tuttu.
`implementation` alanı da dolu, yani hangi tipin hangi adı sağladığı görünüyor.

⚠️ **Spec'te sayı yanlış:** `json-file` önceliği **101**'dir, 200 değil.
Örnek kaynağın kendi beyanı: `Priority => AgentSourcePriority.Database + 1`
(`JsonFileAgentSource.cs:91`). Düzeltildi.

**Sapma:** `EnableDiagnosticsEndpoint = true` host'un `MapTracon` seçeneğinden
verildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-093 — Aynı `Name`'e sahip iki kaynak host'u başlatmaz

**Gerçek sonuç**

📋 **İlk deneme yanlış kurulumdu:** `AddAgentSource<JsonFileAgentSource>()` **iki
kez** çağrıldı ve host sorunsuz açıldı (HTTP 200). Bu bir kusur değil —
`TraconBuilder.cs:142` `TryAddEnumerable` kullanıyor ve aynı **tipi**
tekilleştiriyor. Yani iki kayıt tek kaynağa iniyor, ad çakışması hiç oluşmuyor.

Gerçek koşul için aynı adı döndüren **ikinci bir tip** yazıldı
(`IkinciJsonFileSource`, `Name => "json-file"`):

```
host acilmadi: curl -> HTTP 000 (baglanti reddedildi)          ✅

Unhandled exception. Tracon.TraconAgentSourceException:
  Agent sources 'Tracon.Samples.CustomAgentSource.JsonFileAgentSource' and
  'IkinciJsonFileSource' share the name 'json-file'.            ✅
```

İki beklenti de tuttu: uygulama **başlamadı** · mesaj **iki kaynağın da
tipini** ve paylaşılan adı taşıyor. Hata açılışta, ilk istekte değil.

`Ön koşul`a "iki farklı TİP gerekir" notu eklendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-094 — Yavaş bir kaynak başlangıcı geciktirmez

**Gerçek sonuç**

`ListAsync`'inde `Task.Delay(3 sn)` olan bir `YavasSource` kaydedildi.

```
host baslangici -> ilk saglikli yanit :  1,51 sn     ✅ GECIKMEDI
ilk GET /api/agents cagrisi           :  3,15 sn     <- 3 sn'lik gecikme BURADA
```

🚨 **Kanıt tam olarak bu iki sayının farkıdır.** Startup doğrulaması
`ListAsync`'i çağırsaydı açılış ≥3 sn sürerdi; 1,51 sn sürdü (tipik soğuk
başlangıç). Gecikme **ilk listeleme isteğinde**, yani talep anında ödendi.
∴ Açılışta yalnız `Name`/`Priority` okunuyor, I/O yapılmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-095 — Kiracıya duyarlı özel kaynak

**Gerçek sonuç**
Koşulmadı. Spec'in kendisi bunu **👤 insan gerekir** olarak işaretliyor: bu
depoda kiracıya duyarlı bir örnek `IAgentSource` **yok**, dolayısıyla kiracı
A/B listelerinin ayrıştığını gösterecek bir kurulum kurulamıyor.

Bu bir kusur değil, **kapsam boşluğu**dur. Otomatik karşılığı mevcut ve
kapsıyor: `TenantAwareAgentSourceContract`
(`tests/Tracon.Core.UnitTests/Catalog/BuiltInAgentSourceContractTests.cs`,
`DefinitionStoreTenantAwareAgentSourceContractTests`).

Bu turda yazılan `ohost` kurulumuna kiracıya duyarlı bir kaynak **eklenebilirdi**,
ama o zaman case kendi yazdığım bir kaynağı ölçerdi, ürünü değil — sözleşme
testinin zaten yaptığı şeyi tekrarlardı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-099 — Tool hatası foreign exception ayrıntısını sızdırmaz

**Gerçek sonuç**

`ohost`'a bağlantı dizesi taşıyan yabancı bir istisna atan tool eklendi:
```csharp
throw new SahteVeritabaniException(
  "Host=gizli-sunucu.local;Port=5432;Database=musteri;Username=admin;Password=COK-GIZLI-PAROLA");
```
Model sağlayıcısı `CallsTool("Patla", ...)` ile tool'u gerçekten çağırttı.

**🚨 Sızıntı taraması — iki yüzeyde de temiz:**
```
SSE akisi        : "COK-GIZLI|gizli-sunucu|Password=" -> 0 eslesme ✅
run olaylari     : gizli dizge var mi -> False                      ✅
```

**İki yüzeydeki metinler farklı, ikisi de güvenli:**

| Yüzey | Metin |
|---|---|
| `GET /api/runs/{id}/tools` → `error` | **`Tool failed with SahteVeritabaniException.`** |
| SSE `functionResult.result` | `Error: Function failed.` |

Kayıt yüzeyi spec'in beklediği metni **birebir** veriyor
(`src/Tracon.Core/Tools/ToolFailureText.cs:10`:
`$"Tool failed with {exception.GetType().Name}."`, çağıranı
`ToolInvocationTracker.cs:130`).

⚠️ **Spec'e düzeltme:** "İki yüzeyde de **yalnız** `Tool failed with
<ExceptionType>.` görünür" ifadesi akış yüzeyi için doğru değil. SSE'deki
`functionResult` metni Microsoft.Extensions.AI'ın kendi genel metnidir
(`Error: Function failed.`) ve Tracon'un `ToolFailureText`'inden geçmez.
**Güvenlik iddiası ikisinde de tutuyor** — ne bağlantı dizesi ne özgün mesaj
görünüyor; yalnız metin ikisinde farklı. `Beklenen sonuç` buna göre
netleştirildi.

`tool_invocations` satırı ayrıca `succeeded: false`, `authorizationDenied:
false`, `timedOut: false` taşıyor — başarısızlığın **türü** de kayıtta ayrık.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-102 — `JudgeTimeout` token'ı yok sayan judge için gerçek wait cutoff'tur

**Gerçek sonuç**

`TembelJudge` `Task.Delay(8 sn)` yapıyor ve cancellation token'ı **bilinçli
olarak** iletmiyor. `Tracon__OnlineEvaluation__JudgeTimeout = 00:00:02`.

```
POST /api/runs/{id}/judge -> HTTP 502 · 2,06 sn
  { "title": "Manual scoring failed", "detail": "tembel (judge_timeout)" }
```

🚨 **Kanıt süredir:** judge gövdesi 8 sn sürüyor ve token'ı dinlemiyor; çağrı
yine de **2,06 sn**'de döndü. Yani `JudgeTimeout` judge'ın işbirliğine bağlı
değil, çağıran tarafta **gerçek bir bekleme kesintisi**. Hata metni judge'ı
adıyla ve `judge_timeout` koduyla veriyor.

**Gövde bittikten sonra (12 sn beklendi):**
```
run kaydinda scores/judgments  -> None       ✅ skor YAZILMADI
UnobservedTaskException/Unhandled -> 0        ✅ uretilmedi
```
İki beklentinin ikisi de tuttu: geç biten gövde skoru geri yazmıyor ve arkada
kalan Task sessizce yutulmuyor da, patlamıyor da.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-108 — Örnek metot tool taraması, `AddScopedTool`'u adıyla önerir

**Gerçek sonuç**

**Case'in kendi iddiası TUTTU** — runtime tarayıcı yolunda:
```
beklenen red: Method 'OrnekMetotTool.Calistir' is an instance method and
cannot be a tool. MAF supplies an empty provider as
AIFunctionArguments.Services (K-218). Make the method `static`, create the
target during registration and use `AddTool(AIFunctionFactory.Create(...))`,
or use `AddScopedTool(...)` if the dependency must be resolved per call.

AddScopedTool aniliyor mu: True   ✅
```
Başlangıç `TraconException` ile durdu ve metin `AddScopedTool`'u **adıyla ve
ne zaman kullanılacağıyla** anıyor (`ToolMethodScanner.cs:101-105`).

🚨 **Ama ikinci bir ret yolu var ve o güncellenmemiş → `HATA-S1-014`.**

📋 **Koşum notu — skill §6'nın tuzağına düşülüp çıkıldı:** ilk denemede
derleme `error TRC0007` ile kırıldı, ama `dotnet run --no-build` **eski
ikiliyi** koşup bir önceki case'in çıktısını bastı. Çıktı bir an doğru
görünüyordu. Derleme logu okunarak yakalandı; `--no-build` öncesi derleme
sonucunun okunması bu yüzden zorunlu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### HATA-S1-014 — Analyzer'ın `TRC0007` metni `AddScopedTool`'u anmıyor; runtime metni anıyor

| | |
|---|---|
| **Önem** | Düşük |
| **Bulunduğu case** | MT-CORE-108 |
| **Sınıf** | Tanı kalitesi · iki yüzeyin ayrışması |

Aynı kusur için **iki** ret metni var ve Faz 127 yalnız birini güncellemiş:

| Yüzey | Kaynak | `AddScopedTool` anılıyor mu |
|---|---|---|
| Runtime tarayıcı (`TraconException`) | `src/Tracon.Core/Tools/ToolMethodScanner.cs:101-105` | ✅ evet |
| Analyzer (`error TRC0007`) | `src/Tracon.Generators/ToolDiagnostics.cs:74` | ❌ **hayır** |

Analyzer metni yalnız iki yol öneriyor: *"Make the method 'static', or
instantiate the tool at setup time and register it with
'AddTool(AIFunctionFactory.Create(...))'."*

🚨 **Pratikte görülen metin analyzer'ınkidir.** Analyzer **derleme zamanında**
hata verir, yani runtime tarayıcıya sıra **hiç gelmez** — bu turda runtime
yolunu ölçmek için `TRC0007`'yi `NoWarn` ile bastırmak gerekti. Yani
geliştiricinin gördüğü tek metin, güncellenmemiş olanıdır.

`grep -rc "AddScopedTool" src/Tracon.Generators/ToolDiagnostics.cs` → **0**.

**Kapanışta:** `ToolDiagnostics.cs:74`'e üçüncü seçenek eklenir. Ölçüt basit —
iki metin aynı seçenek kümesini saymalı.

**Etki:** düşük — kusur her iki yolda da yakalanıyor; bedeli, kalıcı bir
bağımlılığı olan tüketicinin doğru API'yi (kendisi için var olan `AddScopedTool`)
bulamaması.

**✅ KAPANDI 2026-09-18 (Aile L).** Analyzer metni üçüncü seçeneği kazandı.

| Adım | Sonuç |
|---|---|
| Ampirik yeniden üretim | ☑ `TRC0007` testi genişletildi ve düzeltmeden önce kırmızı |
| Sınıf taraması | ☑ kayıt **iki** yüzey sayıyordu; ölçüm **dört** buldu. `AddScopedTool`'u anmayan diğer ikisi: `ToolMethodScanner`'ın kendi XML dokümanı (sevk edilen metin) ve `docs-site/troubleshooting.md`'nin `TRC0007` bölümü — ikincisi `AddScopedTool`'un yaptığı işi **tarif ediyor** ama adını vermiyordu, yani okuyan kişi API'yi yine bulamıyordu. İkisi de düzeltildi; siteye çalışan bir örnek eklendi |
| Diğer ikizler | `TRC0004` ve `TRC0005`'in çalışma-anı karşılıkları da karşılaştırıldı: `TRC0004` birebir aynı, `TRC0005`'in iki metni **bilerek** farklı (biri `AddGeneratedTools()` çağrısını, diğeri `AddToolsFrom(type)` çağrısını anlatır) — kayma değil |
| Testler | `DiagnosticTests`; üç seçeneğin üçü de adıyla zorlanıyor |

---

## MT-CORE-121 — Nesne parametreli bir tool gerçek `run`'da modelden nesne argümanı alır

**Gerçek sonuç** (gerçek OpenAI · `support` · `gpt-5.4-mini`)

`support`'un tool listesi ön koşulu **zaten** sağlıyor:
`['get_order_status', 'list_recent_orders', 'cancel_order',
'read_shopping_cart', 'estimate_shipping_cost', 'mark_preview_ready']`

```
istek: "I need a shipping estimate for an order going to 42 Rose Ave,
        Springfield, postal code 62704."

functionCall   | {"name": "estimate_shipping_cost",
                  "arguments": {"address": {"Street": "42 Rose Ave",
                                            "City": "Springfield",
                                            "PostalCode": "62704"}}}
functionResult | {"result": "Estimated shipping to Springfield, 62704:
                             $12.50 (3-5 business days)."}
```

🚨 **Model düz `string` değil, gerçek bir NESNE argümanı gönderdi** — üç alanı
da ayrı ayrı doldurdu. 2026-09-02 koşumunun sonucu bugün **birebir**
tekrarlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-CORE-129 — `AddAgentDecorator()` tüketicinin kendi decorator'ını sıraya sokar

**Gerçek sonuç**

🚨 **Spec'in ön koşulu `samples/Tracon.Api/Program.cs`'i değiştirmeyi istiyor —
donuk.** Bunun yerine `ohost` tüketici host'una `Order => 5` taşıyan bir
`TuketiciDecorator` kaydedildi (`AddAgentDecorator<T>()`). Decorator sardığı
agent'ın **tipini** kaydediyor; sıralamanın kanıtı o tip.

```
GET /decorator-kayit ->
[ "kod-agent <- OpenTelemetryAgent",
  "greeter  <- OpenTelemetryAgent" ]
```

**1) Her agent'a uygulanıyor** ✅ — `kod-agent` (kod kaynağı) **ve** `greeter`
(özel `json-file` kaynağı) ikisi de sarıldı. Özel kaynaktan gelen bir agent
bile atlanmadı.

**2) Sıralama kanıtlandı** ✅ — `Order 5` decorator'ına gelen agent zaten
`OpenTelemetryAgent`. Yani `Order 10` (telemetri) **daha önce** sarmış; benim
`Order 5` çıktım da sonra `Order 0` (kayıt) tarafından sarılacak. Belgelenen
kural birebir: *"düşük `Order` DIŞTA sarar"* —

```
RunRecording (0)  →  TUKETICI (5)  →  OpenTelemetry (10)  →  ToolApproval (20)  →  StructuredResponse (30)
   en distaki                                                                          en icteki
```
Yani tüketicinin logladığı iş `run` kaydının **içinde**, telemetri span'inin
**dışında** görünür — spec'in tarif ettiği yer.

**3) Yerleşik decorator'lar kaybolmadı** ✅ — telemetri sarmalayıcısı gözle
görünür durumda; `run`'lar tamamlandı ve kaydedildi.

📋 **Ölçülmeyen:** "aynı davranış üç kayıt biçiminin üçünde de aynıdır" —
yalnız `AddAgentDecorator<T>()` biçimi koşuldu. Örnek alan ve `IServiceProvider`
alan aşırı yüklemeler ölçülmedi; `MT-CORE-101`/`MT-CORE-093` aynı desenin
(TryAddEnumerable + instance/factory) tutarlı olduğunu gösteriyor ama bu case
için doğrudan kanıt değildir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

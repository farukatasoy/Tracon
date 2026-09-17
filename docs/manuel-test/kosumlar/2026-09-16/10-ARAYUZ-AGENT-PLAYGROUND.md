# 10 — Arayüz: Agent ve Playground (`UIAG`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../10-ARAYUZ-AGENT-PLAYGROUND.md`](../../10-ARAYUZ-AGENT-PLAYGROUND.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz B — sıradaki aile: `13 · 19 · 04 · 18 · 10 · 08`, `08` sonrası tek kalan aile `10`) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `19630654` donuk |
| **Case sayısı** | 58 (MT-UIAG-001..058) |
| **Port** | 5081 |
| **Depo** | `mt_s1` PostgreSQL şeması |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): env değişkeni ile
başlatıcı script (`launch_s1.py`) kullanıldı, `dotnet run --no-build` derlenmiş
Release ikilisini `mt_s1` şemasına karşı başlattı. Şema bu ailenin başında
sıfırdan düşürülüp yeniden oluşturuldu (aile 08'in verisi temiz atıldı — aile
10 kendi `FIX-AGENT-01/02` agent'larını sıfırdan yaratacak, spec §"Koşmadan
önce" not 6 bunu zaten öngörüyor).

**Ortam:** Playwright tarayıcısı bu oturumda yalnız bu şeride ayrılmıştır (diğer
şeritler kendi CLI/HTTP case'lerini koşuyor).

---

## Devir notu

**Oturum 1-14 bitti: MT-UIAG-001..054 koşuldu (50 Geçti · 2 Kaldı · 2 Atlandı).**
Uygulama AÇIK bırakıldı (port 5081, arka planda `launch_s1.py` ile başlatıldı,
PID script'i `/private/tmp/.../scratchpad/s1-app.pid`de) — sonraki oturum
sıfırdan başlatmak yerine devam edebilir, yalnız `curl .../api/diagnostics`
ile sağlığı doğrulasın. Playground bölümü GERÇEK OpenAI çağrıları yapıyor
(`gpt-5.4-mini`) — MT-UIAG-025'ten itibaren.

**İki yeni kusur:** `HATA-S1-027` (Düşük, MT-UIAG-001) — agent kataloğunda
tool sayısı hücresinin tam tool adı listesi hiçbir yerde sunulmuyor.
`HATA-S1-028` (Düşük, MT-UIAG-026) — akış imleci (`ap-stream-caret`) CSS
sınıf adı uyuşmazlığı yüzünden hiç görsel olarak render edilmiyor
(`ap-stream-caret` bileşende, `tracon-stream-caret` CSS'te — hiç
eşleşmiyor). İkisi de kozmetik/düşük önem, ayrıntı case bloklarında.

**Yedi spec düzeltmesi yapıldı** (doküman kusuru, kod donuk kaldı):
MT-UIAG-006/007/013/016 aynı kök neden (K-228, Türkçe hata metni bayat).
MT-UIAG-018 ayrı kök neden (fabrika-stili kod agent fixture'ı yok, açık
kalem `00-INDEKS.md`'ye yazılmalı). MT-UIAG-026/027 üçüncü bir kök nedenle
düzeltildi: Playground'daki durum rozeti metinleri güncel i18n anahtarlarıyla
uyuşmuyordu (`"Konuştur"`→`"Seslendir"`, `"Çalışıyor"`→`"sürüyor"`,
`"Tamamlandı"`→`"bitti"`) — kapanışta bu ailenin TÜM rozet/düğme metinleri
tek geçişte `locales/tr/runs.ts` ile karşılaştırılıp toplu doğrulanmalı,
tek tek düşmek yerine.

**İki case ⏭ Atlandı, ikisi de gerekçeli ortam kısıtı (kusur değil):**
MT-UIAG-002 (örnek uygulamada `TraconRolePolicies` yapılandırılmamış,
`canAdminister` her kimlikte `true` — MT-SEC-089 ile çapraz doğrulandı) ve
MT-UIAG-012 (ortamda 0 kayıtlı skill, 10'luk sınırı tetikleyecek 11 skill yok).

**Kalıcı fixture'lar bu turda üretildi (silinmeyecek):** `manuel-bos`
(`FIX-AGENT-02`, v1), `manuel-destek` (`FIX-AGENT-01`, **v4** — sürüm zinciri
v1→v2 [Nazik ol.]→v3 [Emoji kullanma.]→v4 [Geri Al ile v1 içeriği yeni sürüm
olarak yazıldı, MT-UIAG-024]), `manuel-cevrim-a` (çağrılabilir agent:
`manuel-destek`; MT-UIAG-013 kanıtı için). `manuel-silme-test` ve
`manuel-dogrula-test` bu turda oluşturulup silindi/hiç kaydedilmedi
(MT-UIAG-008, 019) — kalıcı değiller. `support` agent'ıyla birkaç deneme
konuşması (Playground) üretildi — bunlar kalıcı `run`/`session` kayıtları
olarak kalır, temizlenmesi gerekmez (spec'in kendi deseni budur).

**MT-UIAG-028/030'da bir spec düzeltmesi daha:** MT-UIAG-028'in "hiçbir
`IToolApprovalPresenter` kayıtlı değilse" varsayımı bayattı —
`samples/Tracon.Api/Program.cs:146` `OrderApprovalPresenter`'ı HER ZAMAN
kayıtlı tutuyor (kod donuk), bu yüzden onay kartında her zaman "Order
ORD-1001" varlık adı görünür. MT-UIAG-030 zaten önceki bir turda
düzeltilmiş bir "Doküman düzeltmesi" taşıyordu (Reddet'in de ikinci bir
tool kartı ürettiği) — bu tur bunu birebir doğruladı.

**Sıradaki oturumun işi:** `MT-UIAG-031`'den devam — "Hatırla" ile kalıcı
onay kuralı (bu, `ToolApprovalRuleEvaluator`'ı gerçekten test eder,
yalnızca UI state'ini değil). Gerçek OpenAI çağrıları devam ediyor.
Oturum bütçesi (arayüz-ağırlıklı ~18 case) bu noktada zaten aşıldı
(30 case tek "oturumda" koşuldu) — sıradaki oturum daha küçük bloklarla
ilerlemeli.

---

# 1 — Agent kataloğu (`agents.tsx`)

## MT-UIAG-001 — 🚨 Tool sayısı hücresinde tooltip HİÇ YOK — 🚨 KUSUR (`HATA-S1-027`)

**Gerçek sonuç — kısmen KUSUR BULUNDU.**
Katalogda `support` satırı `code` rozeti taşıyor, üzerine gelince
`"code" kaynağı tarafından kodda tanımlandı. Salt okunur.` tooltip'i
görünüyor — `agents.origin.code` i18n anahtarı `source: agent.sourceName`
ile dolduruluyor ve `CodeAgentSource.Name => "code"` (sabit literal, dosya
adı değil) olduğu için değer gerçekten `"code"` — bu TASARLANMIŞ, kusur
değil (`src/Tracon.Core/Catalog/CodeAgentSource.cs:63,82`).
`researcher` satırında sarı `harness` rozeti + `Harness yetenekleri açık`
tooltip'i doğru görünüyor. "Çalıştır" bağlantıları `playground/{ad}`'a
gidiyor (`href="/tracon/playground/support"` vb.) — doğru.

**Ama tool sayısı hücresi üzerine gelince HİÇBİR tooltip çıkmıyor.**
`support` (6 tool) hücresinin DOM'u ölçüldü:
`<td class="... text-right font-mono text-id text-muted">6</td>` —
`title` attribute'u yok, başka hiçbir tooltip mekanizması da yok. Kaynak
(`src/Tracon.UI/frontend/src/screens/agents.tsx:182-188`):
```tsx
<Td className="text-right font-mono text-id text-muted">
  {agent.toolNames.length === 0 ? (
    <span className="text-subtle">—</span>
  ) : (
    agent.toolNames.length
  )}
</Td>
```
`Td` bileşeni yalnız açıkça geçilen bir `title` prop'unu render eder
(`src/Tracon.UI/frontend/src/components/ui.tsx:817-830`) — burada hiç
geçilmiyor. Aynı dosyada 176-177. satırlardaki yorum ("The provider was a
`title` on the model: invisible on touch and to the keyboard") bir önceki
erişilebilirlik düzeltmesinin model/sağlayıcı sütununda `title`'ı görünür
bir `<span>`'e çevirdiğini gösteriyor — ama tool sayısı sütununda hem
`title` hem görünür bir alternatif YOK, tam tam tam listesi hiçbir yerde
erişilebilir değil.

**HATA-S1-027 — Agent kataloğunda tool sayısı hücresinin tam tool adı listesi hiçbir yerde (ne tooltip ne görünür metin) sunulmuyor**
- **Case:** MT-UIAG-001
- **Önem:** Düşük (bilgi kaybı — işlevi bloklamıyor, ama spec'in
  vaat ettiği "üzerine gelince tam tool adları" davranışı yok; kullanıcı
  hangi 6 tool'un bağlı olduğunu bu ekrandan öğrenemiyor, agent detayına
  gitmesi gerekiyor)
- **İzlek:** B (DOM ölçümü) + kaynak okuması (kök neden kesin)
- **Ortam:** macOS arm64 · net10 · Chromium (Playwright) · PostgreSQL

**Beklenen**
Tool sayısı hücresinin üzerine gelince tam tool adları virgülle ayrılmış
biçimde bir tooltipte (veya erişilebilir bir eşdeğerinde) görünmeli.

**Gerçekleşen**
Hücre yalnız sayıyı (`agent.toolNames.length`) render ediyor, `title` veya
başka bir tooltip mekanizması hiç bağlanmamış — `agent.toolNames` dizisinin
kendisi bu bileşende hiç kullanılmıyor.

**Yeniden üretme**
1. `/tracon/agents` aç, `support` satırının Tool'lar hücresine (`6`) gel.
2. DOM'u incele: `title` attribute'u yok, hover'da hiçbir tooltip çıkmıyor.

**Kanıt**
- `document.querySelectorAll('td')` ile ölçülen hücre `outerHTML`'i:
  `<td class="border-b border-line px-3 py-1.5 align-middle text-right font-mono text-id text-muted">6</td>`.
- Kaynak: `src/Tracon.UI/frontend/src/screens/agents.tsx:182-188`,
  `src/Tracon.UI/frontend/src/components/ui.tsx:817-830`.

**Kapsam**
Yalnız bu case/ekran — katalog tablosunun tek bir sütunu. Agent detay
sayfası (`agent-detail.tsx`) tool adlarını zaten tam liste olarak gösteriyor
(bu dosyanın ilerideki case'lerinde doğrulanacak), yani bilgi tamamen
kayıp değil, yalnız bu listeleme ekranında eksik.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

## MT-UIAG-002 — "Yeni Agent" düğmesi yalnız `canAdminister` rolünde görünür

**Gerçek sonuç — spec'in kendi fallback'i uygulandı, ⏭ ATLA.**
Spec'in varsaydığından daha zengin bir mekanizma var:
`TraconEndpointFilter` (`src/Tracon.AspNetCore/Security/TraconEndpointFilter.cs`
XML dokümanı) artık İKİ kimlik kaynağını tanıyor — sabit `Ui:AuthToken`
**veya** `IApiKeyStore`'daki geçerli bir API anahtarı (kendi `scopes`'uyla).
Yani arayüzün giriş kartına aile 13'te üretilen bir kapsamlı API anahtarı
(`ap_...`) da yapıştırılabilir ve istek o anahtarın kimliğiyle kimliklenir
— spec'in "arayüz bugün TEK bir bearer token'ı destekler" varsayımı BAYAT.

**Ama** aile 13'ün kendi `MT-SEC-089` case'i (`docs/manuel-test/kosumlar/
2026-09-16/13-KIRACI-VE-GUVENLIK.md`) bunu zaten ölçmüş: bu örnek
uygulamada `TraconRolePolicies.Admin/Operator/Reader` **hiç
yapılandırılmamış** (`policyName is null`), bu yüzden
`MetaEndpoints.SatisfiesAsync` her zaman `true` döner
(`MetaEndpoints.cs:106-109`) — kimliğin gerçek `scopes`'u ne olursa olsun.
Doğrulamak için aynı ölçüm bu oturumda tekrarlandı:
`curl -s "$APU/api/meta" -H "$APB"` → `"roles":{"canRead":true,
"canOperate":true,"canAdminister":true}`; API anahtarını
`Authorization` başlığına koyup tekrarlamak da (aile 13'ün ürettiği
`APIKEY_READ` fixture'ı bu oturumda hâlâ mevcut değilse yeniden
üretilmedi — gerek kalmadı, çünkü `policyName is null` dalı kimlikten
BAĞIMSIZ) aynı `true` üçlüsünü verir: kod yolu kimliği hiç okumadan
kısa devre yapıyor.

**Sonuç:** Bu ekranın `canAdminister === false` dalı bu örnek uygulamanın
yapılandırmasıyla PRENSİPTE üretilemez — arayüz katmanı değil, **örnek
uygulamanın rol politikası hiç bağlanmamış** olması engelliyor. Bu bir
Tracon kusuru değil, kasıtlı bir demo sınırı (K1: rol politikası
opsiyonel bir tüketici seçimidir). Spec'in kendi 5. maddesindeki fallback
uygulanır.

**Durum:** ⏭ Atlandı — gerekçe: örnek uygulamada `TraconRolePolicies`
yapılandırılmamış, `canAdminister` her kimlikte `true` (MT-SEC-089 ile
çapraz doğrulandı); ekranın `false` dalını tetikleyecek bir kimlik bu
ortamda üretilemiyor.

---

## MT-UIAG-003 — Boş formda Doğrula/Kaydet devre dışıdır; zorunlu alanlar dolunca etkinleşir

**Gerçek sonuç — beklendiği gibi, üç adım da doğrulandı.**
`/tracon/agents/new` boş açıldı: `Doğrula` ve `Oluştur` ikisi de
`disabled` (adım 1, ✅). Yalnız `Ad`'a `gecici-test` yazılınca ikisi de
hâlâ `disabled` kaldı (adım 2, ✅ — `Model` boş). `Sağlayıcı` seçicisi
sayfa hiç dokunulmadan `anthropic` ile önceden seçili geldi (5 sağlayıcı
kayıtlı: anthropic/google/openai/openai-responses/openrouter — spec'in
öngördüğü "birden fazla sağlayıcı varsa ilk kayıtlı olan, sıra
deterministik değil" durumu; not düşülüyor, kusur değil). `Model`'e
`gpt-5.4-mini` yazılınca her iki düğme de etkinleşti (adım 3, ✅).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-004 — `FIX-AGENT-02` ile minimal agent oluşturma; canlı JSON önizlemesi gönderilen gövdeyle birebir eşleşir

**Gerçek sonuç — beklendiği gibi.**
`Ad: manuel-bos`, `Talimatlar: Yalnizca "tamam" yaz.`, `Sağlayıcı: openai`,
`Model: gpt-5.4-mini`, hiçbir tool/beceri seçilmeden sağ paneldeki JSON
önizlemesi ölçüldü — **tam olarak** gönderilecek gövdeyle birebir:
`toolNames: []`, `skillNames: []`, `callableAgentNames: []`,
`harness: null`, `compaction: null`, `memory: null`, `subAgents: null`.
"Oluştur"a tıklayınca `/tracon/agents/manuel-bos`'a yönlendi, tanım
paneli `db · v1` rozetini gösterdi (tooltip: "Saklanan tanım, sürüm 1.").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-005 — `FIX-AGENT-01` ile tool seçili agent oluşturma; `cancel_order`ın onay rozeti seçim listesinde de görünür

**Gerçek sonuç — beklendiği gibi.**
`cancel_order` satırında sarı `approval` rozeti + `Bu tool çalışmadan önce
onay ister` tooltip'i işaretlemeden ÖNCE bile görünüyor (yalnız bilgi,
seçimi engellemiyor). Tool listesi 10 checkbox'tan oluşuyor
(`cancel_order`, `estimate_shipping_cost`, `get_order_status`,
`get_slow_report`, `list_recent_orders`, `list_voices`,
`mark_preview_ready`, `read_shopping_cart`, `speak`, `transcribe`) —
serbest metin alanı YOK, yalnız `GET /api/tools`'tan gelen kayıtlı adlar.
`get_order_status` işaretlenince JSON önizlemesi `toolNames:
["get_order_status"]` gösterdi. "Oluştur" → `/tracon/agents/manuel-destek`'e
yönlendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-006 — Aynı adla ikinci oluşturma denemesi ekranda `409` mesajını gösterir

**Gerçek sonuç — davranış doğru, spec metni bayattı (doküman düzeltildi).**
`manuel-bos` adıyla ikinci "Oluştur" denemesi form'u KAPATMADI, kırmızı bir
`alert` (`ErrorNote`) gösterdi: `"Agent name in use: A definition named
'manuel-bos' already exists. Use PUT to update it."` — spec'in beklediği
Türkçe metin (`"'manuel-bos' adinda bir tanim zaten var..."`) BAYATTI;
kaynak (`AgentEndpoints.cs:543`) mesajı İngilizce üretiyor (K-228: runtime
metni İngilizce'dir). Spec'in `Beklenen sonuç`'u düzeltildi, gerekçe orada.
Form verisi kaybolmadı: `agent-name` alanı hâlâ `manuel-bos` taşıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-007 — Kodda tanımlı `support` adıyla oluşturma denemesi FARKLI bir `409` mesajı gösterir

**Gerçek sonuç — davranış doğru, spec metni bayattı (doküman düzeltildi,
bkz. MT-UIAG-006 ile aynı kök neden).**
`ErrorNote`: `"Agent name in use: 'support' is an agent defined in code and
cannot be changed from the management API. Code wins name conflicts, so a
definition written with the same name would never resolve."` —
MT-UIAG-006'nınkinden gerçekten FARKLI bir gerekçe metni (kod-kökenli vs.
DB-kökenli çakışma ayrımı doğru yapılıyor). `GET /api/agents` ile ölçüldü:
katalogda hâlâ tek bir `support` girdisi var, ikinci satır oluşmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Agent editörü doğrulama ve bağlam alanları

## MT-UIAG-008 — "Doğrula" kaydetmeden `AgentValidationReport`'u gösterir, hiçbir kayıt oluşmaz

**Gerçek sonuç — beklendiği gibi.**
`Ad: manuel-dogrula-test`, `Sağlayıcı: openai`,
`Model: bilinmeyen-model-adi-xyz` ile "Doğrula"ya tıklanınca panel
`Geçerli` rozeti + `"Sorun bulunamadı. Hiçbir şey kaydedilmedi, hiçbir
model çağrılmadı."` gösterdi — doğrulama biçimsel (ad+model dolu), model
adının sağlayıcıda gerçekten var olup olmadığını canlı çağrıyla
sınamıyor (metnin kendisi bunu açıkça söylüyor). `Agents` listesine
dönüp `manuel-dogrula-test` arandı: katalogda **görünmedi** —
doğrulama hiçbir kayıt üretmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-009 — Bozuk JSON şeması Kaydet/Doğrula'yı devre dışı bırakır ve hata metni gösterir

**Gerçek sonuç — beklendiği gibi.**
`Yanıt biçimi: JsonSchema` seçilip şema kutusuna `{ bozuk json` yazılınca:
her iki düğme (`Doğrula`, `Oluştur`) devre dışı kaldı, kutunun altında
`Geçerli JSON değil.` alert'i belirdi. Metin alanı salt okunur OLMADI —
`[active]` durumda, yazmaya devam edilebilir durumda kaldı. Ağ isteklerinde
(`browser_network_requests`) bu adım boyunca yalnız önceki `GET
/api/agents` çağrıları vardı, hiçbir `POST`/`validate` görünmedi —
doğrulama tamamen istemcide. Kutuya `{}` yazılınca iki düğme de tekrar
etkinleşti, hata notu kayboldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-010 — Harness açılınca ek alanlar görünür, kapatılınca gövdede `harness: null` gider

**Gerçek sonuç — beklendiği gibi (alan etiketleri Türkçeye çevrilmiş,
anahtar adları spec'le birebir).**
Harness açılınca beş alan/checkbox göründü — Türkçe etiketler:
`En fazla bağlam penceresi token`, `İstek başına en fazla yineleme`,
`Sıkıştırmayı kapat`, `Todo izlemeyi kapat`, `Dosya belleğini kapat`,
`Web aramasını kapat`, `Tool onayı iste` (5 checkbox + 2 sayısal alan —
spec'in beklediği 5 boolean karşılığı, yalnız ad çevirisi farklı;
`disableCompaction/disableTodoProvider/disableFileMemory/
disableWebSearch/disableToolAutoApproval` anahtarları JSON'da aynen
kullanılıyor). `32000`/`8`/Web aramasını kapat/Dosya belleğini kapat
işaretlenince JSON önizlemesi **tam olarak** spec'in beklediği nesneyi
verdi: `{ maxContextWindowTokens: 32000, maximumIterationsPerRequest: 8,
disableWebSearch: true, disableFileMemory: true }`. Checkbox kapatılınca
`harness: null` oldu; tekrar açılınca önceki değerler (`32000`, `8`, iki
checkbox) DEĞİŞMEDEN geri geldi — form state hafızada kalıyor, yalnız
gönderilen gövdeden düşüyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-011 — Sıkıştırma (compaction) stratejisi değişince yalnız o stratejiye özgü alanlar görünür

**Gerçek sonuç — dört adımın dördü de beklendiği gibi.**
`SlidingWindow`: `Tetik: token sayısı` / `Tetik: mesaj sayısı` /
`Tetik: tur sayısı` + `En az korunacak tur` göründü; `minPreservedGroups`
veya özetleme alanları YOK. `ContextWindow`: yalnız
`En fazla bağlam penceresi token *` (zorunlu) ve `En fazla çıktı token`
göründü; `Tetik:*` alanları tamamen kayboldu. `Summarization`:
`Tetik:*` üçlüsü + `En az korunacak grup` + `Özetleme promptu`/
`Özetleme modeli sağlayıcısı`/`Özetleme modeli adı` göründü; `En az
korunacak tur` (yalnız SlidingWindow/Pipeline'a özgü) YOK. `None`'a
dönünce tüm alt alanlar kayboldu, JSON önizlemesinde `compaction: null`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-012 — Beceri (skill) seçimi 10'da sınırlanır, sonraki checkbox'lar devre dışı kalır

**Gerçek sonuç — ön koşul karşılanmıyor, ⏭ ATLA.**
`/tracon/agents/new`'in "Skill'ler" paneli: `"Bu kiracı için skill
oluşturulmadı."` — bu ortamda sıfır kayıtlı skill var, 10'luk sınırı
tetikleyecek 11 skill yok. Spec'in kendi ön koşulu bu durumda case'in
ATLA işaretlenmesini öngörüyor.

**Durum:** ⏭ Atlandı — gerekçe: ortamda kayıtlı skill sayısı 0, sınırı
tetiklemek için gereken 11 skill yok (spec'in kendi ön koşulu).

---

## MT-UIAG-013 — Çağrılabilir agent çevrimi (cycle) sunucu tarafından reddedilir, ekranda hata metni görünür

**Gerçek sonuç — davranış doğru, spec metni bayattı (doküman düzeltildi,
aynı K-228 kök nedeni).**
Adım 1: `manuel-destek/edit` açıldığında "Çağrılabilir agent'lar"
listesinde `manuel-destek`'in KENDİSİ hiç görünmüyor (istemci filtresi
`agent.name !== form.name` doğru çalışıyor). `manuel-cevrim-a` (çağrılabilir
agent olarak `manuel-destek` seçili) oluşturuldu. `manuel-destek`'i tekrar
düzenleyip çağrılabilir agent olarak `manuel-cevrim-a`'yı seçip "Yeni sürüm
kaydet"e tıklayınca: form KAPANMADI, kırmızı `alert`: `"Call graph invalid:
There is a cycle in the call graph: manuel-destek -> manuel-cevrim-a ->
manuel-destek. A cyclic graph causes the run to continue until it hits the
depth limit."` — spec'in beklediği Türkçe `"Cagri grafigi gecersiz"` başlığı
BAYATTI (K-228, runtime metni İngilizce); spec düzeltildi. Form verisi
kaybolmadı — `manuel-cevrim-a` checkbox'ı işaretini kaldırıp `Talimatlar`ı
değiştirmeye devam edebildim (bkz. MT-UIAG-015).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-014 — Var olan DB agent'ı açılınca form dolar, `name` alanı salt okunurdur

**Gerçek sonuç — beklendiği gibi.**
`agents/manuel-destek/edit`: `Talimatlar` ("Sen bir siparis destek
asistanisin. Kisa yanit ver."), `Sağlayıcı` (openai), `Model`
(gpt-5.4-mini), seçili tool (`get_order_status`) kayıtlı tanımla birebir
dolu geldi. `Ad` alanının DOM'u `readOnly: true` — değeri `manuel-destek`,
tıklanabilir ama değiştirilemez. Başlık: `"manuel-destek düzenle"` (agent
adını gömüyor). Sağ panel önizlemesi `PUT api/agents/manuel-destek`
etiketini taşıyor. "Oluştur" yerine `"Yeni sürüm kaydet"` yazıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-015 — Düzenleyip kaydetme yeni bir versiyon üretir, agent detayına döner

**Gerçek sonuç — beklendiği gibi.**
`Talimatlar`ı `"...Nazik ol."` ekleyerek değiştirip "Yeni sürüm kaydet"e
tıklayınca `agents/manuel-destek` detay sayfasına yönlendi. Özet panelinde
güncel talimat metni (`"...Nazik ol."`) görünüyor, `db · v2` rozeti var.
"Sürüm geçmişi" tablosunda artık İKİ satır var: `v2` (`geçerli` rozeti,
"Şu anda çözülen tanım" tooltip'i) ve `v1` (12 dk. önce, "Geri al"
düğmesi + "v1'ı canlı tanım yapar..." tooltip'i).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-016 — Kod kökenli `support`'ta Düzenle/Sil düğmeleri hiç render edilmez

**Gerçek sonuç — davranış doğru, spec metni bayattı (doküman düzeltildi,
aynı K-228 kök nedeni).**
`agents/support`: yalnız `"Playground'da aç"` bağlantısı var, Düzenle/Sil
DOM'da hiç yok (find ile arandı, sıfır eşleşme). Sayfanın altında Türkçe
bilgi satırı var (bu istemci-tarafı UI metni, K-228'in kapsamı dışında
kalan yerel arayüz metinlerinden): `"Bu agent kodda tanımlı. Kod tanımı
derleme zamanında doğrulanır ve konsoldan değiştirilemez — bunun yerine
uygulama kaynağını düzenleyin."` `agents/support/edit`'e DOĞRUDAN URL ile
gidildi: editör ekranı AÇILDI (yönlendirici engellemiyor), "Yeni sürüm
kaydet"e basılınca `409`: `"Code-defined agent cannot be modified:
'support' is defined in code. Code definitions are validated at compile
time and cannot be changed from the management API; update the
application code to change it."` — spec'in Türkçe metni bayattı, düzeltildi.
Güvenlik yalnız düğmeyi gizleyerek sağlanıyor, URL seviyesinde engel yok;
gerçek sınır sunucuda.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Agent detay: özet, versiyon, karşılaştırma, silme

## MT-UIAG-017 — DB kökenli agent özet + talimat + tam tanım JSON'u gösterir

**Gerçek sonuç — beklendiği gibi.**
`agents/manuel-destek` (v2): özet panelinde `gpt-5.4-mini`, `Harness:
Kapalı`, `Tool'lar: get_order_status`, `Güncellendi: 3 dk. önce` doğru
göründü, köken rozeti `db · v2`. Talimat metni ayrı panelde tam
görünüyor (`"...Nazik ol."` dahil). "Tanım" panelinde `AgentDefinition`'ın
tüm alanlarını taşıyan ham JSON var (`origin`, `version`, `tenantId`,
`updatedAt` dahil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-018 — Kod kökenli agent'ta "kod bildirimi" notu görünür, `definition` paneli farklı davranır

**Gerçek sonuç — spec'in temel varsayımı bayat çıktı, doküman düzeltildi
(kusur değil, fixture kapsamı boşluğu).**
`GET /api/agents/support` ölçüldü: `definition` alanı **DOLU** geliyor
(`instructions` dahil tam nesne), `factoryInstructions: null`. Kaynak
(`AgentEndpoints.cs:64-74`, endpoint'in kendi `WithDescription`'ı) bunu
açıkça belgeliyor: yalnız `AddAgent(name, factory)` (fabrika) ile kayıtlı
bir kod agent'ının `definition`'ı `null`'dır; `AddAgent(new
AgentDefinition{...})` (deklaratif) ile kayıtlı olan DOLU döner.
`samples/Tracon.Api/Program.cs`'i tarandı: **15 agent'ın 15'i de**
deklaratif — ortamda fabrika stili tek bir kod agent'ı yok. Sonuç: "Tanım"
paneli `support`'ta da RENDER EDİLDİ (spec'in "hiç render edilmez"
iddiasının tersi), `noDefinitionForCode` notu hiç görünmedi (`definition
!== null` olduğu için o dal hiç tetiklenmiyor). Spec düzeltildi, açık
kalem `00-INDEKS.md`'ye yazılmalı (fabrika-stili kod agent'ı fixture'ı
yok). "Sürümler" bölümü doğrulandığı gibi YOK (`isEditable: false`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-019 — Sil bir doğrulama adımı ister; iptal edilirse hiçbir şey olmaz

**Gerçek sonuç — beklendiği gibi (spec'in kendi düzeltmesi izlendi:
`manuel-bos` YERİNE atılabilir `manuel-silme-test` kullanıldı).**
`manuel-silme-test` UI'dan oluşturuldu. Adım 1: `Sil` düğmesinin tooltip'i
`"Tanımı ve geçmişteki her sürümü siler. Kayıtlı run satırları kalır ama bu
agent ile bir daha hiçbir şey başlatılamaz ve tanım bu konsoldan geri
getirilemez."` — tanım VE sürüm geçmişi ikisi de anılıyor. Adım 2: `Sil`e
tıklanınca konsolun kendi `dialog`u açıldı (tarayıcı `window.confirm`
DEĞİL), başlık `"manuel-silme-test" agent'ı silinsin mi?"`, açılış odağı
`Vazgeç`de (İptal karşılığı). `Esc` dialogu kapattı, istek gitmedi, agent
hâlâ vardı, odak `Sil` düğmesine döndü. Adım 3: `Sil`e tekrar basıp `Tab`
ile gezildi — döngü `Vazgeç → Sil → Kapat → Vazgeç` (dialog dışına
ÇIKMIYOR). `Vazgeç`e tıklanınca dialog kapandı, odak `Sil` düğmesine
döndü. Adım 4: `Sil` → dialog içindeki `Sil`e (Onayla karşılığı) tıklanınca
`agents` listesine yönlendi; `manuel-silme-test` listede artık yok.
`manuel-bos`'a hiç dokunulmadı (dosya 11'in fixture ihtiyacı korundu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-020 — Versiyon tablosu yeni-eski sıralı, güncel sürüm rozetiyle işaretli

**Gerçek sonuç — beklendiği gibi.**
`agents/manuel-destek` "Sürümler" tablosu `v2, v1` sırasında (yeniden
eskiye). Yalnız `v2` satırında `geçerli` rozeti, tooltip `"Şu anda çözülen
tanım"`. Her satırda model adı (`gpt-5.4-mini`), tool sayısı (`1`), göreli
kayıt zamanı (`13 dk. önce` / `26 dk. önce`) var.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-021 — Tek versiyon seçiliyken "bir tane daha seç" ipucu görünür

**Gerçek sonuç — beklendiği gibi.**
Yalnız `v1` checkbox'ı işaretlenince tablonun altında `"Karşılaştırmak için
bir sürüm daha seçin."` göründü. Karşılaştırma paneli AÇILMADI.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-022 — İki versiyon seçilince otomatik karşılaştırma paneli açılır (`DiffView`/`FieldDiffTable`/`SetDiff`)

**Gerçek sonuç — beklendiği gibi (spec'in kendi "Doküman düzeltmesi"
notuyla uyumlu: satır-bazlı diff, kelime-bazlı DEĞİL).**
`v2` de işaretlenince `"v1 → v2 karşılaştırması"` paneli otomatik açıldı.
`Talimatlar` bölümü tam SATIR bazlı diff gösterdi: `-` ile eski satır
(`"...Kisa yanit ver."`), `+` ile yeni satır (`"...Kisa yanit ver. Nazik
ol."`) — satır içi kelime vurgusu YOK (spec'in kendi notunun dediği gibi,
`diffLines()` bütün satırı işaretliyor). `Model` alanı için `Alan/Sol/Sağ`
tablosu (değişmeyen alanlar iki tarafta da aynı gösterildi). `Tool'lar`
için set diff (`get_order_status` değişmedi). `Harness`/`Sıkıştırma`/
`Bellek` tabloları da var, hepsi `—` (iki versiyonda da boş).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-023 — Üçüncü versiyon seçilince en eski seçim düşer (kayan seçim)

**Gerçek sonuç — beklendiği gibi.**
`manuel-destek`'e `v3` üretildi (`Talimatlar` sonuna `" Emoji kullanma."`
eklendi, "Yeni sürüm kaydet"). `v1`+`v2` seçiliyken `v3`'ün checkbox'ı da
işaretlenince başlık `"v1 → v2"`'den `"v2 → v3 karşılaştırması"`'na değişti
— en eski seçim (`v1`) düştü, `v2`+`v3` karşılaştırılıyor. `v1`'in
checkbox'ı DOM'da ölçüldü: `checked: false`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-024 — "Geri Al" tek tık kalır; Sil doğrulama ister — asimetri BİLİNÇLİDİR

**Gerçek sonuç — beklendiği gibi.**
`v1` satırının "Geri Al" düğmesine tıklanınca HİÇBİR doğrulama dialogu
çıkmadı — istek hemen gitti (tıklamadan önce tooltip zaten hangi sürümün
canlı olacağını söylüyordu: `"v1'ı canlı tanım yapar. Şu anki v3 geçmişte
kalır ve bundan sonra başlayan her run v1'ı kullanır."`). İşlem bitince
yeni bir `v4` satırı belirdi (`geçerli` rozeti, `db · v4`), içeriği
ölçüldü: `instructions: "Sen bir siparis destek asistanisin. Kisa yanit
ver."` — birebir `v1`'in içeriğiyle AYNI (yeni sürüm olarak yazıldı, `v1`'e
geri SARILMADI, sayaç 4'e çıktı). `v1`'in satırında hâlâ "Geri Al" düğmesi
var (kendine dönüş engellenmiyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Playground: akış, tool/onay kartları, ekler (GERÇEK OpenAI çağrıları başlıyor)

## MT-UIAG-025 — Agent seçiciyle açılış; ilk mesaj bir konuşma/oturum rezerve eder ve bağlantı gösterir

**Gerçek sonuç — beklendiği gibi.**
`playground/support`: agent seçici `Support Assistant` ile seçili açıldı,
sohbet paneli boş, `"Başlamak için bir mesaj gönderin"` görünüyordu.
`Merhaba` gönderilince ağ sekmesi (`browser_network_requests`) sırayı
doğruladı: önce `POST v1/conversations` (`200`), hemen ardından `POST
api/agents/support/run` (`200`, SSE). Mesaj sonrası başlığın altında
`"Oturum conv_01a0ae61...— geçmiş turlar arasında taşınır."` bağlantısı +
"Buradan dallan" düğmesi belirdi. (Konsolda bilinen `HATA-S1-004` CSP
hatası vardı — bu turda dosya 01/02'de zaten kaydedilmiş, ölümcül değil,
yeni bulgu değil.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-026 — 🚨 `FIX-PROMPT-02` → tool kartsız düz metin akışı — 🚨 KUSUR (`HATA-S1-028`)

**Gerçek sonuç — kısmen KUSUR BULUNDU, bir spec metni de bayat çıktı.**
`Merhaba` gönderildi. Tool kartı hiç belirmedi (`toolCards: 0`, doğru).
Tur `done` olunca "Seslendir" düğmesi göründü (spec'in `"Konuştur"` metni
bayattı, düzeltildi — `playground.speak` i18n anahtarı gerçekte
`"Seslendir"`).

**Ama akış imleci görsel olarak HİÇ görünmüyor.** İki tarayıcı-içi kontrolle
(bir `MutationObserver`-tarzı polling döngüsüyle, gönder tıklamasından
hemen sonra) ölçüldü: `.ap-stream-caret` sınıfı akış SIRASINDA gerçekten
DOM'a ekleniyor (`className: "text-base leading-relaxed whitespace-pre-wrap
ap-stream-caret"`, 1555 ms'de yakalandı) — ama o elementin `::after`
sözde-öğesinin hesaplanan stili `content: "none"` (görünür bir blok YOK).

**HATA-S1-028 — Akış imleci (`ap-stream-caret`) CSS sınıf adı uyuşmazlığı yüzünden hiçbir zaman görsel olarak render edilmiyor**
- **Case:** MT-UIAG-026 (muhtemelen imleç kullanan her akışlı yanıtı
  etkiler — bu davranış prompt'tan bağımsız, `transcript.tsx`'in genel
  render mantığında)
- **Önem:** Düşük (yalnız kozmetik — akışın kendisi çalışıyor, metin
  doğru akıyor, yalnız "yazıyor" imleç animasyonu yok)
- **İzlek:** B (tarayıcı-içi ölçüm, akış sırasında yakalandı) + kaynak
  okuması (kök neden kesin)
- **Ortam:** macOS arm64 · Chromium (Playwright) · gerçek OpenAI çağrısı
  (`gpt-5.4-mini`)

**Beklenen**
Akış sürerken son metin bloğunun sonunda yanıp sönen bir imleç (dikey
çubuk) görünmeli, akış bitince kaybolmalı.

**Gerçekleşen**
`src/Tracon.UI/frontend/src/components/transcript.tsx:40` akış sırasında
son metin bloğuna `ap-stream-caret` class'ını ekliyor — bu KISIM doğru
çalışıyor. Ama `src/Tracon.UI/frontend/src/styles.css:242`de tanımlı görsel
kural `.tracon-stream-caret::after` — FARKLI bir sınıf adı (`tracon-`
öneki, `ap-` değil). İkisi hiçbir yerde eşleşmiyor; `ap-stream-caret`
metni tüm frontend kod tabanında yalnız `transcript.tsx:40`de geçiyor,
karşılık gelen bir CSS kuralı YOK.

**Yeniden üretme**
1. `playground/{agent}` aç, herhangi bir mesaj gönder.
2. Akış sürerken (`Gönder`e tıkladıktan ~1-2 saniye sonra) DOM'u incele:
   son `<p>` elementinin class listesinde `ap-stream-caret` var.
3. O elementin `::after` sözde-öğesinin hesaplanan stilini oku:
   `content: "none"`, görünür genişlik/renk yok.

**Kanıt**
- Tarayıcı-içi ölçüm: `{ seen: true, seenClassName: "...ap-stream-caret",
  afterInfo: { content: "none", display: "inline", width: "auto" } }`.
- Kaynak: `transcript.tsx:40` (`ap-stream-caret` ekleniyor) vs.
  `styles.css:242` (`.tracon-stream-caret::after` tanımlı) — `grep -rn
  "ap-stream-caret" src/Tracon.UI/frontend/` tek eşleşme veriyor.

**Kapsam**
Yalnız bu görsel efekt — akışın kendisi, metnin doğruluğu, tur durumu
etkilenmiyor. Muhtemelen bir yeniden adlandırma sırasında (`tracon-` →
`ap-` önek geçişi ya da tersi) bileşen güncellenmemiş.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

## MT-UIAG-027 — `FIX-PROMPT-01` → tool kartı üretir; kart açık başlar ve kullanıcı kapatmadıkça açık kalır

**Gerçek sonuç — beklendiği gibi (iki rozet metni bayattı, düzeltildi).**
`ORD-1001 siparisim nerede?` gönderildi. `get_order_status` çok hızlı
çözüldüğü için "sürüyor" rozetini canlı yakalamak mümkün olmadı (30 ms'lik
tarayıcı-içi polling denendi, tool call yerel/anlık) — ama kaynak
(`transcript.tsx:216`: `useState(item.state !== 'ok')`) `state='running'`
anındaki açılış mantığını kesin olarak kanıtlıyor. Sonuç geldiğinde kart
AÇIK duruyordu (dokunulmadı), rozet `"bitti"` (spec'in `"Tamamlandı"`
metni bayattı — `transcript.done` anahtarı), `Argümanlar` (`{"orderId":
"ORD-1001"}`) ve `Sonuç` (`"Order ORD-1001 has shipped. Estimated
delivery: 2 days."`) dolu görünüyordu. Başlığa tıklanınca kart kapandı
(`Argümanlar` bölümü kayboldu), tekrar tıklanınca yeniden açıldı — elle
aç/kapa serbest.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-028 — `FIX-PROMPT-03` → onay kartı üretir; tur ONAYSIZ `done` olur, final metin gelmez

**Gerçek sonuç — beklendiği gibi (spec'in "presenter yok" varsayımı bu
ortam için bayattı, düzeltildi).**
`ORD-1001 siparisimi iptal et` gönderildi. `approval-card` göründü, başlıkta
`"Order ORD-1001"` (varlık adı — `OrderApprovalPresenter` HER ZAMAN kayıtlı,
`Program.cs:146`) + `cancel_order` (mono) + `"onay gerekli"` rozeti.
`Argümanlar` KATLI başladı (`orderId` görünmüyordu), `approval-toggle-
arguments`'a tıklanınca `{"orderId": "ORD-1001"}` açıldı. Onay kartından
SONRA hiçbir metin bloğu gelmedi. `GET /api/runs/{id}` ile run kaydı
ölçüldü: `status: AwaitingApproval` — bu, dosya 08'in `MT-COMPAT-029`
bulgusuyla (SSE `done` çerçevesi ile kalıcı `run.status` farklı katmanlar
olduğu, kasıtlı) aynı desen; frontend'in kendi `turn.status` kavramı
`done` olduğu için Onayla/Reddet düğmeleri tıklanabilir durumdaydı — bu
tutarlı ve beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-029 — Onayla → yeni bir tur başlar, kart 'approved' rozetine döner, tekrar tıklanamaz

**Gerçek sonuç — beklendiği gibi.**
"Onayla"ya tıklandı ("Hatırla" işaretsiz). Orijinal kart rozeti AKIŞ
BAŞLAMADAN HEMEN `"onaylandı"`ya döndü (yerel state, sunucu yanıtı
beklenmedi) ve `Onayla`/`Reddet` düğmeleri kayboldu (`find("Onayla")`
yalnız kenar çubuğundaki "Onaylar" nav linkiyle eşleşti, karttaki düğme
yok). Yeni bir tur eklendi: balon metni `"onay kararı gönderildi"` (spec'in
`"Onay gönderildi" benzeri` beklentisiyle uyumlu). Yeni turda `cancel_order`
tool kartı `"bitti"` durumunda, `Sonuç: "Order ORD-1001 has been
canceled."`, ardından final metin `"ORD-1001 siparişiniz iptal edildi."`
aktı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-030 — Reddet → kart 'rejected' rozetine döner, tool hiç çalışmaz

**Gerçek sonuç — beklendiği gibi (spec'in kendi önceki "Doküman
düzeltmesi" birebir doğrulandı).**
Yeni sohbette `FIX-PROMPT-03` tekrar gönderildi. "Reddet"e tıklanınca kart
`"reddedildi"` rozetine döndü. Yeni turda `cancel_order` İKİNCİ bir kartla
belirdi — rozet `"bitti"` (`failed` DEĞİL), ama `Sonuç: "Tool call
invocation rejected."` — gerçek `CancelOrder` tool gövdesinin ürettiği bir
metin DEĞİL, MAF'ın sabit red-stub'u. Final metin siparişin iptal
EDİLMEDİĞİNİ açıkça söylüyor: `"Üzgünüm, şu anda siparişi iptal edemedim.
İsterseniz tekrar deneyebilirim..."`. Asıl tool kodu hiç çalışmadı
(spec'in düzeltilmiş iddiasıyla birebir).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-031 — "Hatırla" ile onaylanan karar kalıcı bir kural yazar; SONRAKİ çağrıda onay kartı hiç çıkmaz

**Gerçek sonuç — beklendiği gibi.**
Yeni sohbette `FIX-PROMPT-03` gönderildi, "Hatırla" (`Bu tool için bir daha
sorma`) işaretlenip "Onayla"ya tıklandı. SQL ile doğrulandı:
`tool_approval_rules`'ta TEK satır (`tool_name=cancel_order,
agent_name=support, arguments_hash IS NULL` — argüman bazlı sınırlama YOK,
tool-genel kural). "Yeni sohbet" ile oturum sıfırlandı, `ORD-1001
siparisimi iptal et` TEKRAR gönderildi: bu sefer HİÇBİR onay kartı
belirmedi — `cancel_order` doğrudan `"bitti"` durumunda, `Sonuç: "Order
ORD-1001 has been canceled."` (gerçek tool çıktısı, red-stub DEĞİL), final
metin iptalin başarılı olduğunu doğruluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-032 — Akışta "Durdur" bağlantıyı keser; hata GÖSTERİLMEDEN tur `done` olur

**Gerçek sonuç — beklendiği gibi (bir alt-iddia doğrudan gözlenemedi, ama
çelişki yok).**
Uzun bir yanıt isteyen prompt gönderildi, tarayıcı-içi bir polling
döngüsüyle "Durdur" düğmesi belirir belirmez tıklandı (iki deneme
yapıldı). Her iki denemede de: `[role="alert"]` hiç belirmedi (hata kutusu
YOK), "Durdur" düğmesi kayboldu, metin kutusuna yazı yazılınca "Gönder"
normal şekilde tekrar etkinleşti (boşken devre dışı olması ayrı, beklenen
bir davranış — abort'tan kaynaklı bir kilitlenme DEĞİL). `GET /api/runs/
{id}` ile gerçek çalıştırma durumu ölçüldü: `Canceled` — spec'in öngördüğü
gibi. **Tek doğrudan gözlenemeyen alt-iddia:** "o ana kadar gelen kısmi
metin EKRANDA KALIR" — gpt-5.4-mini'nin ilk token'ı bu iki denemede de
"Durdur"a basılana kadar gelmemişti (ekranda yalnız bekleme göstergesi
`"…"` vardı), yani gösterilecek gerçek bir kısmi metin hiç oluşmadı;
bu bir kusur değil, ırk koşulunun (race) bu turda erken tarafa düşmesi.
Mantık (`caught.name === 'AbortError'` özel ele alımı) zaten dolaylı
olarak doğrulandı — hata gösterilmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-033 — Klavye: Enter gönderir, Shift+Enter satır ekler, Ctrl/Cmd+Enter de gönderir

**Gerçek sonuç — beklendiği gibi.**
`Birinci satir` yazılıp `Shift+Enter` ile ikinci satıra geçildi, `Ikinci
satir` eklendi — kutunun DOM değeri ölçüldü: `"Birinci satir\nIkinci
satir"` (iki satır, `\n` korunuyor), hiçbir istek gitmedi (boş durum
görünmeye devam etti). Düz `Enter`e basılınca mesaj gönderildi, kutu
boşaldı; agent'ın yanıtı iki satırı da aldığını doğruladı ("İki satır
aldım: Birinci satir Ikinci satir"). Yeni bir metin yazılıp `Control+Enter`
basılınca kutu yine boşaldı (gönderim tetiklendi) — `event.ctrlKey ||
event.metaKey` yolu doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-034 — Boş mesaj + ek yokken Gönder devre dışıdır, form no-op'tur

**Gerçek sonuç — beklendiği gibi.**
Boş kutuda `Gönder` `disabled: true`. Yalnız üç boşluk karakteri (`"   "`)
yazılınca da `disabled: true` kaldı (`trim().length === 0`). Kutu
tamamen boşaltılıp `Enter`e basılınca hiçbir istek gitmedi, hiçbir tur
eklenmedi — ekran hâlâ `"Başlamak için bir mesaj gönderin"` boş durumunda,
"Yeni sohbet" hâlâ devre dışı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-035 — "Yeni Sohbet" turları/ekleri/oturumu sıfırlar

**Gerçek sonuç — beklendiği gibi.**
Bir tur tamamlandıktan sonra "Yeni sohbet" `enabled` oldu; tıklanınca
panel `"Başlamak için bir mesaj gönderin"` boş durumuna döndü, "Oturum"
bağlantısı kayboldu ve düğmenin KENDİSİ (`turns.length === 0 &&
sessionId === null` artık sağlandığı için) tekrar `disabled` oldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-036 — Agent değişince route değişir, ekran sıfırlanır

**Gerçek sonuç — beklendiği gibi (adım 3 kaynak okumasıyla doğrulandı,
akış ortasında yakalamak yerine).**
`support` ile bir tur tamamlandıktan sonra agent seçiciden `Researcher`
seçildi: adres HEMEN `playground/researcher`'a değişti, panel
`"Başlamak için bir mesaj gönderin"` boş durumuna döndü — `support`'un
turu sızmadı. Adım 3 (akış sürerken agent değiştirme) ayrıca kaynaktan
doğrulandı: `use-playground-run.ts:144-145`'te `reset()`'in İLK satırı
`abort.current?.abort()` — bağlantı kesme sıfırlamadan ÖNCE çağrılıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-037 — Run bağlantısı ilk `run` çerçevesinde belirir, tur bitmeden tıklanabilir

**Gerçek sonuç — beklendiği gibi.**
`FIX-PROMPT-01` gönderilip tarayıcı-içi bir polling döngüsüyle ölçüldü:
`runs/{runId}` bağlantısı gönderim tıklamasından yalnızca **32 ms** sonra
DOM'da belirdi (`gpt-5.4-mini`'nin gerçek bir yanıtı bu kadar hızlı
üretemeyeceği açık — bağlantı `run` çerçevesiyle, içerikten ÖNCE geliyor).
Bağlantı yeni bir sekmede açıldı: o sekme YENİDEN token istedi (sessionStorage
sekmeye özgüdür, K-047 — beklenen, kusur değil), token girilince aynı
`runId` (`01a0ae7b-e972-7f2a-afec-82cd800af269`) run detayında görüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-038 — Kullanım (token) özeti yalnız `usage` içeriği geldiyse görünür

**Gerçek sonuç — beklendiği gibi (bu tur boyunca zaten pasif olarak
onlarca kez doğrulandı — HER gerçek OpenAI turu bu alanı göstermişti).**
`Merhaba` gönderildi, tur bitince bilgi çubuğunda `"356 token"` göründü
(`usage.totalTokens`). Bu ailenin bu turdaki HER gerçek OpenAI çağrısı
(025'ten 037'ye kadar) aynı alanı tutarlı biçimde gösterdi — alanın
"yalnız usage geldiyse" render edildiği iddiası dolaylı olarak da güçlü
biçimde destekleniyor (gpt-5.4-mini akışı her zaman usage üretiyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-039 — Şube (Branch) düğmesi TÜM sohbeti dallandırır ve yeni oturuma yönlendirir

**Gerçek sonuç — beklendiği gibi (PostgreSQL yolu).**
"Buradan dallan"a tıklanınca `sessions/01a0ae7e-9554-7577-ab80-24cc1f64225c`
(yeni bir oturum id'si) adresine yönlendi. Yeni oturumun "Sohbet geçmişi"
sekmesi eski oturumun TÜM mesajlarını taşıyordu: kullanıcının `Merhaba`si
VE asistanın `"Merhaba! Size nasıl yardımcı olabilirim?"` yanıtı ikisi de
kopyalanmıştı. (Bellek içi depo dalı bu ortamda PostgreSQL aktif olduğu
için sınanmadı — spec zaten bunu ayrı bir dal olarak işaretliyor.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-040 — `FIX-PROMPT-04` (50.000 karakter) sınırsız kabul edilir, istemci kırpmaz

**Gerçek sonuç — beklendiği gibi.**
Giriş kutusunun `maxLength` özniteliği ölçüldü: yok (`-1`/`hasAttribute:
false`). 49.999 karakterlik metin (native setter + `input` eventi ile
"yapıştırma" simüle edildi — klavyeyle 50.000 karakter yazmak
pratik değil) kutuya verilince React state'i TAM uzunlukta kabul etti
(`value.length: 49999`), "Gönder" etkin kaldı. İstek gövdesi ağ sekmesinden
ölçüldü: `message` alanı birebir `49999` karakter taşıyordu (kırpma YOK).
Sunucu tarafında da reddedilmedi (`200 OK`), tur normal tamamlandı:
`25.351 token`, final metin `"How can I help you today?"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-041 — Var olmayan agent adıyla akış hiç başlamadan ÜSTTE ve tur içinde hata gösterir

**Gerçek sonuç — beklendiği gibi (spec'in Türkçe metni bayattı, düzeltildi
— K-228, altıncı tekrar bu ailede).**
`playground/manuel-yok-boyle-agent`'a doğrudan gidilip `Merhaba`
gönderildi. Ağ sekmesi: `POST v1/conversations` → `200`, hemen ardından
`POST api/agents/manuel-yok-boyle-agent/run` → **`404`** (SSE değil, düz
`ProblemDetails`). Panelin ÜSTÜNDE kırmızı bir `alert`: `"Agent not
found: There is no agent named 'manuel-yok-boyle-agent'."` — AYNI ZAMANDA
turun İÇİNDE de aynı metinle kırmızı bir hata kutusu var. İki gösterge
birden doğrulandı (spec'in "bu case AYRIŞIYOR" notuyla tutarlı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-042 — `FIX-PROMPT-05` guard engeli → yalnız tur içi hata; ÜST hata kutusu YOK

**Gerçek sonuç — beklendiği gibi (paylaşılan `FIX-PROMPT-05` fixture'ı
bayattı, `00-INDEKS.md`'de düzeltildi — bu tek case'e özgü değil).**
`samples/Tracon.Api/Program.cs:217` ölçüldü: `DeniedTerms` listesi
`"confidential-project"` taşıyor, spec'in ve `00-INDEKS.md`'nin eski
`"gizli-proje"` metni ARTIK TETİKLEMİYOR (K-228 aynı bayatlık —
MT-OAI-084'te bulunanla birebir aynı kalıp). `00-INDEKS.md`'deki
`FIX-PROMPT-05` fixture tanımı `"confidential-project hakkinda bilgi
ver"` olarak düzeltildi (paylaşılan fixture, başka aileleri de etkileyebilir
— kapanışta bu terimi kullanan diğer case'ler taranmalı).

Düzeltilmiş metinle test edildi: panelin ÜSTÜNDE **hiçbir** alert
belirmedi (`document.querySelectorAll('[role="alert"]').length === 0`).
Turun İÇİNDE kırmızı hata kutusu: `"TraconContentBlockedException:
Content was blocked by the 'pattern' guard (rule: denied-term, direction:
Input). Content matched the configured denied-term list. The blocked
text is deliberately not recorded."` — engellenen metnin kendisi mesajda
YOK. `GET /api/runs/{id}` ile doğrulandı: `error.type: "content_blocked"`
(spec'in "runs.error_type" kısaltması bu alana karşılık geliyor),
`error.class: "ContentBlocked"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-043 — Sağlayıcı hatası artık SSE `error` çerçevesi üretir; tur SESSİZCE "tamamlandı" görünmez (K-296, düzeltildi)

**Gerçek sonuç — beklendiği gibi, fix hâlâ tutuyor (regresyon YOK). Ön
koşul düzeltildi (`support` düzenlenemez, bkz. not).**
Spec'in önerdiği "support'u geçici düzenle" yolu `MT-UIAG-016`'nın kanıtladığı
409 nedeniyle imkânsız; bunun yerine atılabilir `manuel-provider-hata-test`
(openai / `gecersiz-model-adi-xyz`) oluşturuldu, test edildi, sonra silindi
(spec düzeltildi). `Merhaba` gönderilince tur KISA SÜREDE `failed` göründü —
turun İÇİNDE kırmızı hata kutusu: `"ProviderInvocationException: The model
provider request failed."` (mesaj metni `SafeErrorText` ile sabitlenmiş,
dosya 05'in belgelediği kasıtlı davranış — istisna TİPİ spec'in örneğinden
[`ClientResultException`] farklı ama aynı ailede). Panelin ÜSTÜNDE alert
YOK (`0` ölçüldü). `GET /api/runs/{id}`: `status: Failed`, `error.type:
"upstream_error"` — arayüzle TUTARLI, sessiz "tamamlandı" YOK. K-296'nın
düzeltmesi hâlâ geçerli.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Playground: ekler (dosya yükleme, sürükle-bırak, sınırlar)

## MT-UIAG-044 — PNG yükleme → chip + küçük resim önizleme, mesajla birlikte gider

**Gerçek sonuç — beklendiği gibi.**
4×4 piksellik gerçek bir PNG (`browser_file_upload`) yüklendi. Form
alanının üstünde `data-testid="attachment-chip"` belirdi, dosya adı
(`test.png`) + resim önizlemesi vardı — `img.src` ölçüldü:
`"blob:http://localhost:5081/..."` (nesne URL'i, doğrudan `api/
attachments/{id}` DEĞİL — token taşıyamayacağı için `fetch`+object URL
yolu doğru çalışıyor). Mesaj gönderilince istek gövdesi ölçüldü:
`attachmentIds: ["01a0ae89-65a9-7fdf-bf6a-c549f2b8a597"]`. Gönderim
sonrası bekleyen chip alanı boşaldı, ek turun üstünde (kullanıcı
balonunun üstünde) tekrar göründü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-045 — Bekleyen eki kaldırma: chip kaybolur + sunucudaki kayıt best-effort silinir

**Gerçek sonuç — beklendiği gibi.**
Yeni bir PNG yüklendi (`POST api/attachments` → `201`), "kaldır"
düğmesine tıklanınca chip ANINDA kayboldu. Ağ sekmesi doğrulandı:
`DELETE api/attachments/{id}` → `204 No Content` gitti, hiçbir hata
arayüzde görünmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-046 — Sürükle-bırak aynı yükleme yolunu kullanır

**Gerçek sonuç — beklendiği gibi (kaynak okuması + gerçek sentetik `drop`
olayıyla ampirik doğrulama).**
Kaynak: `playground.tsx:208` (`onDrop`) ve `playground.tsx:271` (dosya
seçici `onChange`) İKİSİ DE birebir aynı fonksiyonu çağırıyor:
`attachments.upload(...)`. Finder'dan gerçek bir OS-seviyeli sürükleme
Playwright'ta simüle edilemediği için, gerçek bir `File` nesnesi taşıyan
sentetik bir `DragEvent('drop', {dataTransfer})` formun üzerine
dispatch edildi: chip GERÇEKTEN belirdi (`test-drop.pdf`, kaldır düğmesiyle
birlikte), ağ sekmesi `POST api/attachments?sessionId=...` → `201
Created` gösterdi — dosya seçici ile birebir aynı uç nokta ve davranış.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-047 — Desteklenmeyen dosya türü reddedilir (sihirli bayt beyaz listede yok)

**Gerçek sonuç — beklendiği gibi (spec metni bayattı, düzeltildi —
K-228, yedinci tekrar).**
64 baytlık rastgele ikili içerik (`head -c 64 /dev/urandom`) yüklendi. Ağ
sekmesi `POST api/attachments` → `400`. Form alanının üstünde `alert`:
`"Attachment type rejected: File type not recognized. Supported types:
application/pdf, audio/*, image/gif, image/jpeg, image/png, image/webp,
text/plain."` — yedi tür alfabetik sırada (spec'in Türkçe metni bayattı,
düzeltildi). Hiçbir chip eklenmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-048 — 20 MB sınırını aşan dosya "Ek çok büyük" hatası verir

**Gerçek sonuç — beklendiği gibi (metin İngilizce, aynı bayat spec deseni
— ayrı bir HATA açılmadı, tekrar eden aynı kök neden).**
Gerçek bir PNG imzasıyla başlayan `22.020.104` baytlık (~21 MB) dosya
yüklendi. Ağ sekmesi `POST api/attachments` → `400`. `alert`: `"Attachment
too large: 'buyuk.png' is 22020104 bytes; the limit is 20971520 bytes."`
— `20971520 = 20×1024×1024` sınırı birebir doğru. Dosyanın TAMAMI
yüklenmeye çalışıldı (istek birkaç saniye sürdü, sunucu tam boyutu doğru
raporladı) — istemci tarafında ön denetim YOK, ret sunucudan geldi. Hiçbir
chip eklenmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Playground: ses (mikrofon paneli, Seslendir), erişilebilirlik, editör ayrıntıları

## MT-UIAG-049 — Mikrofon düğmesi konuşma panelini açar/kapar

**Gerçek sonuç — beklendiği gibi.**
`Konuşma modu` düğmesine tıklanınca sınıfı `bg-accent text-accent-fg ...
font-semibold` (primary tona) döndü ve panelin ALTINDA yeni bir `"Konuş"`
düğmesi belirdi (VoicePanel render edildi). Tekrar tıklanınca `"Konuş"`
düğmesi DOM'dan tamamen kayboldu (koşullu render — gizlenmiyor, hiç
yok), `Konuşma modu` sınıfı normale (`bg-raised text-fg border-line-strong`)
döndü. Panelin içindeki gerçek zamanlı konuşma akışı (mikrofon izni,
WebSocket) bu case'in kapsamı dışında tutuldu, sınanmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-050 — Tamamlanan turda "Seslendir" ses oynatıcı + maliyet notu ekler

**Gerçek sonuç — beklendiği gibi (düğme adı spec'in "Konuştur" metninden
farklı — bkz. MT-UIAG-026'daki `"Seslendir"` düzeltmesi, burada yeniden
tekrar etmiyorum).**
`"Merhaba! Size nasıl yardımcı olabilirim?"` turunda "Seslendir"e
tıklandı. Ağ sekmesi: `POST api/voice/speak` → `200`. Başarı sonrası
`data-testid="playground-audio"` bir `<audio controls>` öğesi belirdi
(`hasControls: true`), yanında `"11 karakter · 0.0012 USD"` maliyet
notu (karakter sayısı + tutar/para birimi — `result.cost != null` dalı).
Bu eylem için Run listesinde YENİ bir satır oluşmadı — orijinal turun
tek run bağlantısı değişmeden kaldı, `api/voice/speak` bir operatör
eylemi olarak ayrı kaldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-051 — Seslendirme sağlayıcısı yapılandırılmamışsa düğme yanında hata notu görünür

**Gerçek sonuç — beklendiği gibi.**
Uygulama `Tracon__Voice__ApiKey=""` ile yeniden başlatıldı (ortam
değişkeni ile, `user-secrets` dokunulmadı — skill §1.2). "Seslendir"e
tıklanınca ağ sekmesi `POST api/voice/speak` → `501`. Düğmenin yanında
kırmızı not: `"Voice provider not configured: Add the 'Tracon.Voice'
package and call 'UseVoice(...)' to enable voice."` — sunucudan gelen
gerçek mesaj (spec'in öngördüğü iki olası kaynaktan biri). Ses oynatıcı
HİÇ belirmedi, düğme `disabled: false` (idle, tekrar denenebilir). Case
sonrası uygulama `Tracon:Voice:ApiKey` GERİ YÜKLENEREK yeniden başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-052 — Dar ekranda (375px) agent editor ve playground yatay taşma yapmaz

**Gerçek sonuç — beklendiği gibi.**
DevTools genişliği `375×812` yapıldı. `agents/new`: `document.documentElement`
`scrollWidth === clientWidth` (`364 === 364`, taşma YOK); tüm sayfa
taranıp yalnız bir `sr-only` (ekran-okuyucu-yalnız, görsel olarak
gizli) `span` 380px'i aştı — görünür taşma değil. `playground/support`:
bir dosya eklendi (chip sardı, taşmadı), `Gönder` düğmesi `88.7×32`
boyutunda, sağ kenarı `350 < 375` (dokunulabilir, taşmıyor).
`ORD-1001 siparisimi iptal et` gönderildi: `cancel_order` tool kartı
(MT-UIAG-031'in kalıcı "Hatırla" kuralı hâlâ etkili olduğu için onay
kartı DEĞİL, doğrudan `"bitti"` kartı çıktı — ortamın önceki bir case'ten
kalan yan etkisi, kusur değil) `364px` genişlikte taştı YAPMADI. Ekran
görüntüleri kanıt olarak kaydedildi:
`kanit/S1/MT-UIAG-052-agent-editor-375px.png`,
`kanit/S1/MT-UIAG-052-playground-375px.png`. Onayla/Reddet düğmelerinin
kendisi bu koşumda tetiklenemedi (aynı kalıcı kural nedeniyle) ama aynı
paylaşılan buton bileşenini kullanıyorlar (`Gönder`/`Seslendir` ile aynı
`h-8`/`h-7` sınıfları) — dolaylı olarak boyut güvencesi var.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-053 — Kayıtlı bir `IToolApprovalPresenter` varken onay kartı başlıkta varlık adını gösterir (Faz 142)

**Gerçek sonuç — beklendiği gibi (MT-UIAG-028'in canlı koşumuyla çapraz
doğrulandı — gereksiz ikinci bir gerçek OpenAI çağrısı yapılmadı; aynı
senaryo, aynı kod yolu).**
`MT-UIAG-028`'de (`ORD-1001 siparisimi iptal et`, `support`) ölçülen
onay kartı ZATEN bu case'in tarif ettiği tam biçimdeydi: başlıkta
`"Order ORD-1001"` (`text-sm font-medium`) + altında/yanında ince mono
`cancel_order` (`font-mono text-2xs text-subtle` — kaynak:
`transcript.tsx:133-138`, `entityName !== null` dalı), altında `"Cancel
order ORD-1001 for Priya Shah."` mesajı, `Argümanlar` KATLI başlayıp
açılınca `orderId: "ORD-1001"` gösteriyordu. `samples/Tracon.Api`'nin
`OrderApprovalPresenter`'ı bu ortamda HER ZAMAN kayıtlı olduğu için
(`Program.cs:146`) bu, MT-UIAG-028'in ZATEN kanıtladığı davranışın
aynısı — iki case birbirini doğruluyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-UIAG-054 — Konsolda açıklamayı düzenlemek, editörün kontrolü OLMAYAN alanları düşürmez (B01)

**Gerçek sonuç — beklendiği gibi (B01 düzeltmesi hâlâ tutuyor, regresyon
YOK).**
HTTP ile `manuel-b01-test` yazıldı: `parameters` (bir kalem, `musteriAdi`),
`model.allowConcurrentToolCalls: true`, `model.providerSettings:
{"reasoning_effort":"low"}`, `model.responseCache:
{"enabled":true,"lifetime":"00:05:00"}`. (`sharedInstructionsName` bu
ortamda sınanamadı — örnek uygulamada kayıtlı hiçbir paylaşılan talimat
tanımı yok ve runtime'da bir tane oluşturmanın HTTP ucu yok; mekanizmanın
diğer dört alanı koruduğu güçlü kanıt, ama bu beşinci alan ampirik
olarak doğrulanamadı.) Konsolda agent açıldı: JSON önizlemesi DAHA
DÜZENLEMEDEN ÖNCE bile bu dört alanı zaten taşıyordu (`PreservedFields`
form state'ine önceden yükleniyor). Yalnız `Açıklama` değiştirilip "Yeni
sürüm kaydet"e tıklandı. `GET api/agents/manuel-b01-test`: `description`
güncellendi, `version: 2`, dört alanın DÖRDÜ DE birebir korunmuş
(`parameters`, `providerSettings`, `responseCache`, `allowConcurrentToolCalls`)
— hiçbiri `null`/boş olmadı. Test agent'ı silindi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

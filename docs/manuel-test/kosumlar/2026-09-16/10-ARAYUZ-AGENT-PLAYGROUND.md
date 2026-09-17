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

**Oturum 1 bitti: MT-UIAG-001..016 koşuldu (13 Geçti · 1 Kaldı · 2 Atlandı).**
Uygulama AÇIK bırakıldı (port 5081, arka planda `launch_s1.py` ile başlatıldı,
PID script'i `/private/tmp/.../scratchpad/s1-app.pid`de) — sonraki oturum
sıfırdan başlatmak yerine devam edebilir, yalnız `curl .../api/diagnostics`
ile sağlığı doğrulasın.

**Yeni kusur:** `HATA-S1-027` (Düşük, MT-UIAG-001) — agent kataloğunda tool
sayısı hücresinin tam tool adı listesi hiçbir yerde (ne tooltip ne görünür
metin) sunulmuyor. Ayrıntı case'in kendi bloğunda.

**Dört spec düzeltmesi yapıldı** (doküman kusuru, kod donuk kaldı — hepsi
aynı kök neden, K-228): MT-UIAG-006, 007, 013, 016 — dördü de Türkçe
`ErrorNote` metni bekliyordu, gerçek sunucu mesajları İngilizce. Aile 05/18
oturumlarında görülen aynı sistematik bayatlığın bu ailedeki dördüncü
tekrarı; kapanışta sınıf taraması önerilir (`grep -rn '"Cagri\|adinda\|gecersiz'
docs/manuel-test/*.md` gibi Türkçe hata metni bekleyen kalan case var mı diye).

**İki case ⏭ Atlandı, ikisi de gerekçeli ortam kısıtı (kusur değil):**
MT-UIAG-002 (örnek uygulamada `TraconRolePolicies` yapılandırılmamış,
`canAdminister` her kimlikte `true` — MT-SEC-089 ile çapraz doğrulandı) ve
MT-UIAG-012 (ortamda 0 kayıtlı skill, 10'luk sınırı tetikleyecek 11 skill yok).

**Kalıcı fixture'lar bu oturumda üretildi (silinmeyecek):** `manuel-bos`
(`FIX-AGENT-02`, v1), `manuel-destek` (`FIX-AGENT-01`, v2 — `Nazik ol.` eklendi,
v1 geçmişte), `manuel-cevrim-a` (çağrılabilir agent: `manuel-destek`; MT-UIAG-013
kanıtı için — MT-UIAG-024'ün "Geri Al" case'i v1/v2 geçmişini zaten kullanacak).

**Sıradaki oturumun işi:** `MT-UIAG-017`'den devam. Şu ana kadar HİÇBİR case
gerçek bir OpenAI çağrısı yapmadı (017'den itibaren playground/akış case'leri
gerçek çağrı yapacak — `Tracon:Providers:OpenAI:ApiKey` ortamda tanımlı).

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

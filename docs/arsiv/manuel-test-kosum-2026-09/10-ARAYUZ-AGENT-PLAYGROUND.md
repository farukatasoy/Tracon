# 10 — Arayüz: Agent ve Playground (`UIAG`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../10-ARAYUZ-AGENT-PLAYGROUND.md`](../../manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md)
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

**🎉 AİLE KAPANDI — MT-UIAG-001..058 koşuldu (54 Geçti · 2 Kaldı · 2 Atlandı,
58/58).** Sayım betiği (skill §7) ile doğrulandı: `{'Kaldı': 2, 'Atlandı': 2,
'Geçti': 54} toplam: 58`, açık kalem YOK. **Bununla `ap-s1`'in Faz B'de
üstlendiği ALTI ailenin ALTISI DA kapandı** (`13 · 19 · 04 · 18 · 10 · 08`
— sırayla SEC/MM/SQL/MCP/UIAG/COMPAT). **Bu şeridin bu tur için işi bitti.**
Uygulama DURDURULDU (port 5081 serbest bırakıldı).

**İki yeni kusur:** `HATA-S1-027` (Düşük, MT-UIAG-001) — agent kataloğunda
tool sayısı hücresinin tam tool adı listesi hiçbir yerde sunulmuyor.
`HATA-S1-028` (Düşük, MT-UIAG-026) — akış imleci (`ap-stream-caret`) CSS
sınıf adı uyuşmazlığı yüzünden hiç görsel olarak render edilmiyor
(`ap-stream-caret` bileşende, `tracon-stream-caret` CSS'te — hiç
eşleşmiyor). İkisi de kozmetik/düşük önem, ayrıntı case bloklarında.

**On spec düzeltmesi yapıldı** (doküman kusuru, kod donuk kaldı — hiçbiri
kod değişikliği gerektirmedi): MT-UIAG-006/007/013/016/041/047/048 aynı kök
neden (K-228, Türkçe hata metni bayat — bu ailede YEDİ tekrar, kapanışta
sınıf taraması şart). MT-UIAG-018 ayrı kök neden (fabrika-stili kod agent
fixture'ı yok — açık kalem, aşağıda). MT-UIAG-026/027 üçüncü kök neden
(Playground rozet metinleri i18n'le uyuşmuyordu: `"Konuştur"`→`"Seslendir"`,
`"Çalışıyor"`→`"sürüyor"`, `"Tamamlandı"`→`"bitti"`). MT-UIAG-028 dördüncü
kök neden (`IToolApprovalPresenter` bu ortamda HER ZAMAN kayıtlı,
"kayıtlı değilse" dalı hiç sınanamaz). MT-UIAG-043 beşinci kök neden
(`support` düzenlenemediği için spec'in "geçici düzenle" ön koşulu
imkânsızdı, atılabilir agent'a çevrildi). **Kapanışta önerilen tek geçiş:**
bu ailenin TÜM rozet/düğme/hata metinleri `locales/tr/runs.ts` ile toplu
karşılaştırılmalı — tek tek düşmek yerine.

**Açık kalem (00-INDEKS.md'ye taşınmalı, kapanışta):** MT-UIAG-018'in
"fabrika-stili kod agent'ı" dalı (`definition: null`, `noDefinitionForCode`
notu) bu örnek uygulamada hiç sınanamıyor — 15 agent'ın 15'i de deklaratif
(`AddAgent(new AgentDefinition{...})`), fabrika stili (`AddAgent(name,
factory)`) örneği yok.

**İki case ⏭ Atlandı, ikisi de gerekçeli ortam kısıtı (kusur değil):**
MT-UIAG-002 (`TraconRolePolicies` yapılandırılmamış, `canAdminister` her
kimlikte `true`) ve MT-UIAG-012 (ortamda 0 kayıtlı skill).

**Kalıcı fixture'lar bu turda üretildi (silinmeyecek, sonraki dosyalar
kullanabilir):** `manuel-bos` (`FIX-AGENT-02`, v1), `manuel-destek`
(`FIX-AGENT-01`, **v4** — sürüm zinciri v1→v2→v3→v4), `manuel-cevrim-a`
(çağrılabilir agent: `manuel-destek`). `support` agent'ıyla çok sayıda
deneme konuşması (gerçek OpenAI çağrısı) — kalıcı `run`/`session`
kayıtları, temizlenmesi gerekmez. `tool_approval_rules`'ta KALICI bir
kural var: `support` + `cancel_order` artık her zaman ONAYSIZ çalışır
(MT-UIAG-031'in kalıcı yan etkisi — sonraki bir dosya `cancel_order`'ın
onay davranışını test etmek isterse bu kuralı hesaba katmalı ya da
`DELETE`lemeli).

**Kapanış oturumunun işi:** `docs/manuel-test/kosumlar/2026-09-16/DEVIR.md`
(ana depoda) `ap-s1` satırını "6/6 aile kapandı" olarak güncellemeli.

---

# 1 — Agent kataloğu (`agents.tsx`)

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show fca6f3a1:docs/manuel-test/kosumlar/2026-09-16/10-ARAYUZ-AGENT-PLAYGROUND.md
> ```

---

## Temiz geçen case'ler (32)

| Case | Durum | Başlık |
|---|---|---|
| MT-UIAG-004 | ☑ | `FIX-AGENT-02` ile minimal agent oluşturma; canlı JSON önizlemesi gönderilen gövdeyle birebir eşleşir |
| MT-UIAG-005 | ☑ | `FIX-AGENT-01` ile tool seçili agent oluşturma; `cancel_order`ın onay rozeti seçim listesinde de görünür |
| MT-UIAG-008 | ☑ | "Doğrula" kaydetmeden `AgentValidationReport`'u gösterir, hiçbir kayıt oluşmaz |
| MT-UIAG-009 | ☑ | Bozuk JSON şeması Kaydet/Doğrula'yı devre dışı bırakır ve hata metni gösterir |
| MT-UIAG-010 | ☑ | Harness açılınca ek alanlar görünür, kapatılınca gövdede `harness: null` gider |
| MT-UIAG-011 | ☑ | Sıkıştırma (compaction) stratejisi değişince yalnız o stratejiye özgü alanlar görünür |
| MT-UIAG-014 | ☑ | Var olan DB agent'ı açılınca form dolar, `name` alanı salt okunurdur |
| MT-UIAG-015 | ☑ | Düzenleyip kaydetme yeni bir versiyon üretir, agent detayına döner |
| MT-UIAG-017 | ☑ | DB kökenli agent özet + talimat + tam tanım JSON'u gösterir |
| MT-UIAG-020 | ☑ | Versiyon tablosu yeni-eski sıralı, güncel sürüm rozetiyle işaretli |
| MT-UIAG-021 | ☑ | Tek versiyon seçiliyken "bir tane daha seç" ipucu görünür |
| MT-UIAG-023 | ☑ | Üçüncü versiyon seçilince en eski seçim düşer (kayan seçim) |
| MT-UIAG-024 | ☑ | "Geri Al" tek tık kalır; Sil doğrulama ister — asimetri BİLİNÇLİDİR |
| MT-UIAG-029 | ☑ | Onayla → yeni bir tur başlar, kart 'approved' rozetine döner, tekrar tıklanamaz |
| MT-UIAG-031 | ☑ | "Hatırla" ile onaylanan karar kalıcı bir kural yazar; SONRAKİ çağrıda onay kartı hiç çıkmaz |
| MT-UIAG-033 | ☑ | Klavye: Enter gönderir, Shift+Enter satır ekler, Ctrl/Cmd+Enter de gönderir |
| MT-UIAG-034 | ☑ | Boş mesaj + ek yokken Gönder devre dışıdır, form no-op'tur |
| MT-UIAG-035 | ☑ | "Yeni Sohbet" turları/ekleri/oturumu sıfırlar |
| MT-UIAG-036 | ☑ | Agent değişince route değişir, ekran sıfırlanır |
| MT-UIAG-038 | ☑ | Kullanım (token) özeti yalnız `usage` içeriği geldiyse görünür |
| MT-UIAG-039 | ☑ | Şube (Branch) düğmesi TÜM sohbeti dallandırır ve yeni oturuma yönlendirir |
| MT-UIAG-040 | ☑ | `FIX-PROMPT-04` (50.000 karakter) sınırsız kabul edilir, istemci kırpmaz |
| MT-UIAG-044 | ☑ | PNG yükleme → chip + küçük resim önizleme, mesajla birlikte gider |
| MT-UIAG-045 | ☑ | Bekleyen eki kaldırma: chip kaybolur + sunucudaki kayıt best-effort silinir |
| MT-UIAG-046 | ☑ | Sürükle-bırak aynı yükleme yolunu kullanır |
| MT-UIAG-048 | ☑ | 20 MB sınırını aşan dosya "Ek çok büyük" hatası verir |
| MT-UIAG-049 | ☑ | Mikrofon düğmesi konuşma panelini açar/kapar |
| MT-UIAG-051 | ☑ | Seslendirme sağlayıcısı yapılandırılmamışsa düğme yanında hata notu görünür |
| MT-UIAG-053 | ☑ | Kayıtlı bir `IToolApprovalPresenter` varken onay kartı başlıkta varlık adını gösterir (Faz 142) |
| MT-UIAG-055 | ☑ | Yalnız vector search açık olan `memory` bloğu kaydetmede kaybolmaz (B01) |
| MT-UIAG-057 | ☑ | Bir düğmenin açıklaması hem işaretçiyle hem klavyeyle görünür (Faz 165) |
| MT-UIAG-058 | ☑ | Yükleme hatası boş bir form göstermez (Faz 165) |

## Ayrıntı taşıyan case'ler (26)

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

**Gerçek sonuç — kapanış yeniden koşumu (2026-09-19, gerçek tarayıcı)**
`HATA-S1-027` kapandı. `/tracon/agents` açıldı, tool sayısı hücresi ölçüldü:

```
{ text: "2", describedBy: "_r_4_",
  names: "get_order_status, list_recent_orders",
  underline: "underline", tabIndex: 0 }
```

Üzerine gelindiğinde balon **görünür** oldu ve aynı listeyi taşıdı
(`visible: true`, `sr-only` değil). `title` DEĞİL: hücre `aria-describedby`
ile listeye bağlı, odaklanabilir ve noktalı alt çizgiyle işaretli — ekranın
kendi `Th` deseni. Koşumda hücre yalnız sayıyı taşıyordu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — gerekçe: örnek
uygulamada `TraconRolePolicies` yapılandırılmamış, `canAdminister` her
kimlikte `true` (MT-SEC-089 ile
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

## MT-UIAG-012 — Beceri (skill) seçimi 10'da sınırlanır, sonraki checkbox'lar devre dışı kalır

**Gerçek sonuç — ön koşul karşılanmıyor, ⏭ ATLA.**
`/tracon/agents/new`'in "Skill'ler" paneli: `"Bu kiracı için skill
oluşturulmadı."` — bu ortamda sıfır kayıtlı skill var, 10'luk sınırı
tetikleyecek 11 skill yok. Spec'in kendi ön koşulu bu durumda case'in
ATLA işaretlenmesini öngörüyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — gerekçe: ortamda
kayıtlı skill sayısı 0, sınırı tetiklemek için gereken 11 skill yok
(spec'in kendi ön koşulu).

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

**Gerçek sonuç — kapanış yeniden koşumu (2026-09-19, gerçek tarayıcı)**
`HATA-S1-028` kapandı. Sunulan konsolda bileşenin uyguladığı sınıf adıyla
(`tracon-stream-caret`) bir eleman oluşturulup `::after` hesaplanan stili
okundu:

```
{ content: "\"\"", width: "8px",
  background: "rgb(115, 217, 194)", animation: "tracon-blink" }
```

Koşumda aynı ölçüm `content: "none"` veriyordu, çünkü bileşen
`ap-stream-caret` uyguluyordu ve o ada karşılık gelen kural yoktu. 🚨 Ölçüm
akışlı bir turla değil sunulan stil sayfasıyla yapıldı; bileşenin **hangi
sınıfı uyguladığı** ayrı bir kapıyla kilitli
(`scripts/check-custom-classes.mjs`, iki yönlü) ve o kapı düzeltme öncesi
kırmızı olduğu ölçülerek doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-UIAG-056 — Boş bir kiracıda her liste ekranı ilkini nasıl oluşturacağını söyler (Faz 165)

**Gerçek sonuç — mekanik olarak GEÇTİ, wording'in nihai öznel kalite
denetimi 👤'ye kalır (case'in kendi işareti).**
`mt_s1` şeması tamamen "temiz" değil (bu tur boyunca `agents`/`workflows`
kullanıldı) ama YEDİ ekranın BEŞİ hâlâ gerçekten boş: `skills` (0),
`triggers` (0), `experiments` (0), `evals` (0), `mcp` (0) — bunlar
gerçek boş durumlarıyla ölçüldü:
- **Skills:** `"Henüz skill yok"` + açıklama + `"İlk skill'i oluştur"`.
- **Triggers:** `"Henüz trigger yok"` + açıklama + `"İlk tetikleyiciyi oluştur"`.
- **Experiments:** `"Henüz deney yok"` + açıklama + `"İlk deneyi oluştur"`.
- **Evals:** `"Henüz değerlendirme kümesi yok"` + açıklama + `"İlk seti oluştur"`.
- **MCP:** `"MCP sunucusu yok"` — "Tracon bunlar olmadan çalışır" notuyla
  + `"İlk server'ı ekle"` (spec'in kendi örneğiyle birebir eşleşti).

Beşinde de CTA metni başlıktaki düğmeyi ("Yeni skill" vb.) TEKRARLAMIYOR.

**İkinci grup** (sahte aksiyon YOK, yalnız durumu söylüyor) da doğrulandı:
`Onaylar`: `"Bekleyen bir şey yok"` + açıklama. `Ayarlar` → Kotalar:
`"Tanımlı kota yok"` + `"Kural olmadan hiçbir şey reddedilmez; Tracon
varsayılan kota getirmez."`. `Skills` sayfasındaki "Script çalıştırma
izinleri" paneli: `"Script izni yok"` + `"Etkin izin yok."`.

**Ölçülemeyen ekranlar (ortam kısıtı, kusur değil):** `agents` (15 kod
agent'ı HER ZAMAN var), `workflows` (2 kod workflow'u HER ZAMAN var),
`audit` (bu turun kendi CRUD aktivitesi 22+ kayıt üretti) — üçü de bu
örnek uygulamada asla gerçekten boş olamaz.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

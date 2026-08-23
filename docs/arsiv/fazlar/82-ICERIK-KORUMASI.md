# Faz 82 — İçerik Koruması (at-rest)

> **Durum:** ✅ Tamamlandı (2026-08-22)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-41** — Dalga 13 Küme C
> **Önkoşul:** [Faz 64](64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md) — konu bazlı **silme** oradan gelir ve bu faz onun üstüne gelmez, yanına gelir · [Faz 48](48-GUARDRAILS.md) — guard'ın nereye takıldığı ve maskelemenin kaydı nasıl kapsadığı
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Sql.Shared` (üç SQL paketine linked-source olarak derlenir, K-176)
> **Yeni paket:** Yok — koruma `System.Security.Cryptography.AesGcm` ile yazılır; BCL'dedir, geçişli bağımlılık **sıfır** · **Migration:** **Yok** — zarf biçimi mevcut sütun tiplerine sığar (82.1'de ölçüldü)
> **Public API:** Büyüyor — bir arayüz, bir `sealed class`, bir `Options`, bir kayıt uzantısı. `PublicAPI.Shipped.txt` toplamı **16 satır** (yalnız başlıklar; ölçüldü 2026-08-21) → Faz 7'den önce eklemek **bedava**, sonra bir sürüm kararıdır
> **Tüketici yüzeyi:** `docs-site/` → `getting-started/security.md` §"What is stored in the clear" (bugün "known gap, not a shipped feature" diyor — bu faz o cümleyi geçersiz kılar), `concepts/governance.md`, `capabilities.md`, `reference/configuration.md`
> · sevk edilen: `IContentProtector` ve `AgentPrismContentProtectionOptions` XML dokümanı, `src/AgentPrism.Core/README.md`. `api/` ve `http-api/` **üretilir** — orada iş XML dokümanıdır
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md) (`SEC` alan kodu)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-059\|K-176\|K-182\|K-370\|K-479\|K-007" docs/KARARLAR.md
   ```
   **K-059** (`secret` veritabanına yazılmaz; kayıtta yalnız yapılandırma
   anahtarının **adı** durur — bu fazın anahtar çözümü onun aynısıdır),
   **K-176** (linked-source: `Sql.Shared` bir paket değildir, üç pakete ayrı
   derlenir), **K-182** (SQL Server/SQLite'ta JSON bir **metindir**),
   **K-370** (uyum kaydı yutan sarmalayıcıdan geçmez), **K-479** (`jsonb`
   süzgeç biçimi — bu faz o sütunlara **dokunmaz**, karıştırma),
   **K-007** (yeni paket gerekçesi — bu fazda yeni paket **yok**).
3. [`64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md`](64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md) — yalnız §64.4 ve devir notu:
   ```bash
   sed -n '/^## 64.4/,/^## 64.5/p' docs/arsiv/fazlar/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md
   ```
   Silme **sevk edildi**; bu faz onu tekrarlamaz.
   🚨 **Faz 79, 80 ve 81 planlandı ama uygulanmadı** — devir notları boştur, okuma.
4. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/sql-saglayicilari.md`](../../hafiza/sql-saglayicilari.md) (paylaşılan katman
   kuralları — özellikle "sağlayıcıya özgü ADO.NET tipine başvurma") ·
   [`hafiza/postgresql.md`](../../hafiza/postgresql.md) (🚨 **ordinal okuma**: `runs`
   ve kardeşleri sabit sütun konumundan okunur) ·
   [`hafiza/cekirdek-calistirma.md`](../../hafiza/cekirdek-calistirma.md)
   (`RunRecording` zinciri ve `secret` filtresi)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI-GUVENLIK.md`](../../MIMARI-GUVENLIK.md)

---

## Amaç

Bu faz **at-rest içerik korumasını** sevk eder: veritabanına yazılan hassas
metin, diske ulaşmadan önce şifrelenir; okunurken **şeffaf biçimde** çözülür.

Koruma **varsayılan kapalıdır** (K1) ve iki parçadan oluşur: bir genişleme
noktası (`IContentProtector`) ve BCL'den yazılmış çalışır bir uygulama
(`AesGcmContentProtector`). İkincisi bilerek vardır — Faz 73'ün "opt-in
kararının bedeli benimseme oranıdır" cümlesi F-135'te ilk faturasını kesti;
varsayılansız bir genişleme noktası hiçbir tüketiciyi korumaz.

**Bu faz bir tehdit modelini kapatır, hepsini değil.** Korunan şey diskteki
ve yedekteki veridir: çalınan bir yedek, atılmış bir disk, yanlış izinli bir
tablo. Korunmayan şey **çalışan uygulamadır** — anahtarı olan bir süreç düz
metni görür. 82.5 bunu açıkça yazar ve site metnine de aynen geçer.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `grep -rn "IContentProtector" src` → **0 sonuç** | Genişleme noktası **yok**. Yazılacak |
| [`0001_initial.sql:75`](../../../src/AgentPrism.PostgreSql/Migrations/0001_initial.sql) `state json NOT NULL` | Oturum durumu açık |
| [`0023_replay_and_branching.sql:33`](../../../src/AgentPrism.PostgreSql/Migrations/0023_replay_and_branching.sql) `messages json NOT NULL` | Kullanıcının ham istemi açık |
| [`0006_attachments.sql:22`](../../../src/AgentPrism.PostgreSql/Migrations/0006_attachments.sql) `content bytea` | Yüklenen dosya açık |
| [`security.md:196`](../../../docs-site/src/content/docs/getting-started/security.md) | Site **on sütunu adıyla** listeliyor ve "Column-level encryption inside AgentPrism is a **known gap, not a shipped feature**" diyor |

> Kanıtlar 2026-08-21 tarihinde doğrulandı.

### 🚨 Planlama sırasında ölçülen beş şey — üçü kaydı çürüttü

| Kayıttaki iddia | Ölçüm | Sonuç |
|---|---|---|
| **F-87:** "Guard model sınırındadır; `RunStarted` ve `run_inputs` ham saklar" | [`RunRecordingAgent.cs:661`](../../../src/AgentPrism.Core/Recording/RunRecordingAgent.cs#L661) kaydı **guard'dan geçiriyor** (`ContentGuardMessageMasker.PreviewAsync`). Aday, `AgentPrismServiceCollectionExtensions.cs:869`'daki yorumu ters okumuş: yorum "bu satır **olmasaydı**" diyor ve satır **var** | 🚫 **YANLIŞ.** F-87 kapatıldı (👤 kullanıcı kararı, 2026-08-21). Faz 64 ayrıca konu bazlı **silme** sevk ediyor |
| **F-41:** "Bu sınır hiçbir tüketiciye dönük belgede yazmıyor" (B06-1) | `security.md` §"What is stored in the clear" sınırı **ve on sütunun tamamını** yazıyor; kontrol listesinde de satırı var | 🚫 **YANLIŞ.** Dokümantasyon boşluğu kapanmış; kalan iş **yalnız koddur** |
| "Şifreleme dosya aramasını **tamamen bitirir**" | [`SqlAgentFileStore.cs:167-190`](../../../src/AgentPrism.Sql.Shared/Stores/SqlAgentFileStore.cs#L167) — sunucu regex'i yalnız bir **ön süzgeçtir**; nihai eşleşme **her zaman** .NET `Regex` ile yapılır ve kod zaten ön süzgeçsiz bir yedek yola sahiptir (`IsInvalidRegexError` yakalayınca) | ⚠️ **Fazla güçlü.** Arama bitmez, **ön süzgeci** kaybeder — davranış aynı, maliyet artar (82.3) |
| "Dokuz sütun" | Site ve tarama kaydı **dokuz kalem** sayıyor ama `tool_invocations.arguments/result` iki sütundur → **on sütun**. Ayrıca tarama **iki sütun kaçırmış**: [`0027_pending_approvals.sql:14`](../../../src/AgentPrism.PostgreSql/Migrations/0027_pending_approvals.sql) `arguments text` (onay bekleyen tool çağrısının argümanları — `tool_invocations.arguments` ile **aynı** veri) ve `agent_skills` kaynak/script içeriği | ✅ Sayı düzeltildi: **on sütun** + iki aday (82.5) |
| **`responses.payload` içerik tutuyor** (site böyle diyor) | `INSERT INTO ... responses` **hiçbir yerde yok** (ölçüldü). Tablo `0001_initial.sql`'de kuruldu ("phase 4 fills them") ama hiçbir `store` ona yazmıyor; yalnız [`DataSubjectTargetRegistry.cs:121`](../../../src/AgentPrism.Sql.Shared/Internal/DataSubjectTargetRegistry.cs#L121) onu silme hedefi olarak tanıyor | 🚫 **YANLIŞ.** Tablo bugün **boştur**. Sevk edilen `security.md` onu "session state and provider responses" diye sayıyor — bu faz o satırı da düzeltir (82.5) |

---

## 82.1 — 🚨 Zarf biçimi: ham şifre metni sütuna SIĞMAZ

Bu fazın en önemli yapısal kararıdır ve ölçümle verilmiştir.

On sütunun **altısı** JSON geçerliliği zorlar. PostgreSQL bunu **tiple**,
SQL Server ve SQLite **`CHECK` kısıtıyla** yapar:

| Sütun | PostgreSQL | SQL Server | SQLite |
|---|---|---|---|
| `sessions.state` | `json` | `nvarchar(max)` + `CHECK (ISJSON(state) = 1)` | `TEXT CHECK (json_valid(state))` |
| `conversation_items.item` | `json` | `nvarchar(max)` + `CHECK (ISJSON(item) = 1)` | `TEXT CHECK (json_valid(item))` |
| `responses.payload` | `jsonb` | `nvarchar(max)` + `CHECK (ISJSON(payload) = 1)` | `TEXT CHECK (json_valid(payload))` |
| `run_inputs.messages` | `json` | `nvarchar(max)` | `TEXT` |
| `tool_invocations.arguments` | `jsonb` | `nvarchar(max)` | `TEXT` |
| `tool_invocations.result` | `jsonb` | `nvarchar(max)` | `TEXT` |
| `run_events.text` | `text` | `nvarchar(max)` | `TEXT` |
| `run_events.payload` | `text` (**kısıt yok**, bilerek) | `nvarchar(max)` (kısıt yok) | `TEXT` (kısıt yok) |
| `agent_files.content` | `text` | `nvarchar(max)` | `TEXT` |
| `attachments.content` | `bytea` | `varbinary(max)` | `BLOB` |

Base64 bir şifre metni geçerli JSON **değildir**. En sıkı sağlayıcı
PostgreSQL'dir ve altı sütunu birden reddeder. İki yol vardı:

| Yol | Bedeli |
|---|---|
| Sütun tipini `text`'e çevir | Üç sağlayıcıda **veri migration'ı**; var olan kurulumda tüm satırlar yeniden yazılır; `jsonb` sütunlarında tip değişimi kilit ister |
| **Zarf** — şifre metnini geçerli bir JSON nesnesine sar | Migration **yok**; kısıtlar sağlanır; düz metin ve şifreli satırlar **aynı tabloda yan yana** yaşar |

**Karar: zarf.** Biçim tek bir yerde tanımlanır ve üç sağlayıcıda aynıdır:

```json
{ "$apEnc": 1, "kid": "2026-08", "n": "<base64 nonce>", "c": "<base64 ciphertext+tag>" }
```

Zarfın üç işi vardır ve üçü de zorunludur:

1. **Geçerli JSON'dur** — altı kısıtlı sütun sağlanır, migration gerekmez.
2. **Kendini tanıtır** — `$apEnc` alanı okuma yolunda "bu değer şifreli mi"
   sorusunu **veriye bakarak** cevaplar. Yapılandırmaya bakmaz: koruma sonradan
   kapatılsa bile eski satırlar okunabilir kalır.
3. **Anahtarı adlandırır** — `kid` (82.4).

İkili sütun (`attachments.content`) zarfı **taşımaz**; orada aynı bilgi bir
ikili başlıkta durur (sihirli sayı + sürüm + `kid` uzunluğu + `kid` + nonce).
Gerekçe: `bytea` bir JSON kısıtı taşımaz ve içeriği base64'lemek boyutu
üçte bir büyütür.

**Düz metin satırlar bozulmaz.** `$apEnc` taşımayan her değer olduğu gibi
döner. Bu yüzden koruma **var olan bir veritabanında açılabilir** ve eski
satırlar okunmaya devam eder — geriye dönük şifreleme bu fazın işi **değildir**
(82.4).

---

## 82.2 — Kanca noktası: küresel değil, ADI VERİLMİŞ çağrı yeridir

Ölçüldü: `Sql.Shared/Stores/` içinde **29** `AddJson`/`AddJsonb` çağrısı var,
ama bunların yalnız bir kısmı bu fazın kapsamındadır. `labels`, `checks`,
`scores`, `variants`, `attributes`, `headers`, iş kuyruğu `payload`'ı ve
webhook `payload`'ı **kontrol düzlemi verisidir**, kullanıcı içeriği değil.

Sütun **adı da ayırt edici değildir**: `payload` dört ayrı tabloda geçer
(`run_events`, `jobs`, `job_schedules`, `webhook_deliveries`) ve yalnız
birincisi kapsamdadır.

**Bu yüzden koruma, adı verilmiş her yazma/okuma yerine ELLE takılır.**
Küresel bir `AddJson` kancası yanlış olurdu: bugün kapsam dışı olan bir sütunu
sessizce şifreler ve `labels` gibi **süzgeçlenen** bir sütunu bozardı (K-479).

Kapsamdaki yazma yerleri (ölçüldü, 2026-08-21):

| Sütun | Yazma yeri |
|---|---|
| `sessions.state` | [`SqlSessionStore.cs:72`](../../../src/AgentPrism.Sql.Shared/Stores/SqlSessionStore.cs#L72) ve `:98` |
| `conversation_items.item` | [`SqlChatHistoryProvider.cs:166`](../../../src/AgentPrism.Sql.Shared/Stores/SqlChatHistoryProvider.cs#L166) · [`SqlConversationBranchStore.cs:155`](../../../src/AgentPrism.Sql.Shared/Stores/SqlConversationBranchStore.cs#L155) |
| `run_inputs.messages` | [`SqlRunInputStore.cs:47`](../../../src/AgentPrism.Sql.Shared/Stores/SqlRunInputStore.cs#L47) |
| `run_events.text` · `.payload` | [`SqlRunStore.cs:121`](../../../src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs#L121) ve `:124`; ayrıca `:291` (hata metni) |
| `tool_invocations.arguments` · `.result` | [`SqlRunStore.cs:647`](../../../src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs#L647) ve `:648` |
| `agent_files.content` | [`SqlAgentFileStore.cs:74`](../../../src/AgentPrism.Sql.Shared/Stores/SqlAgentFileStore.cs#L74) |
| `attachments.content` | [`SqlAttachmentStore.cs:71`](../../../src/AgentPrism.Sql.Shared/Stores/SqlAttachmentStore.cs#L71) |
| `responses.payload` | **Yazma yolu YOK** (ölçüldü) — tabloya hiçbir `store` yazmıyor, okuma yolu da yok. Kapsam listesinde **kalır** ki tablo bir gün dolarsa korumalı doğsun; bugün kod değişikliği doğurmaz |

🚨 **İmza değiştirmek ile gövdeyi kullanmak iki ayrı adımdır.** Okuma tarafı
bu repoda **ordinal** okur (`docs/hafiza/postgresql.md`): `ReadRun` sabit sütun
konumundan alır. Çözme, okunan **değerin** üzerine uygulanır; sütun sırası
değişmez ve değiştirilmemelidir.

**Kapı:** yeni bir sütun kapsama girdiğinde unutulmasın diye bir mimari test
yazılır — kapsam listesi **tek bir yerde** (`ProtectedColumns`) durur ve test
o listedeki her sütunun hem yazma hem okuma yolunda korumadan geçtiğini
kanıtlar. Liste ile kod ayrışırsa test kızarır.

---

## 82.3 — Üç okuma yolu: ikisi hiç etkilenmez, biri ön süzgecini kaybeder

Aday kaydının "çatışma haritası" bu fazın en çok korkulan kısmıydı. Ölçüm
korkuyu küçülttü, çünkü koruma **şeffaftır**: depo katmanı okurken çözer.

| Okuma yolu | Kanıt | Şifrelemeden sonra |
|---|---|---|
| Yeniden oynatma | [`RunReplayService.cs:30`](../../../src/AgentPrism.Core/Replay/RunReplayService.cs#L30) `IRunInputStore _inputs` | **Etkilenmez.** `IRunInputStore` uygulaması çözülmüş metni döndürür; replay sadıktır |
| Eval terfisi | [`RunToCasePromoter.cs:97`](../../../src/AgentPrism.Core/Evaluation/RunToCasePromoter.cs#L97) `ListToolInvocationsAsync`, `:175` `ReadEventsAsync` | **Etkilenmez.** İkisi de depo üzerinden okur |
| Dosya araması | [`SqlAgentFileStore.cs:167`](../../../src/AgentPrism.Sql.Shared/Stores/SqlAgentFileStore.cs#L167) | **Ön süzgeç ölür, arama yaşar.** Sunucu tarafı `~` regex'i şifreli metinle eşleşmez; kod bu durumda dizin kapsamındaki dosyaları çeker, çözer ve .NET `Regex` ile eşler — **zaten var olan yedek yol** |

🚨 **Ön süzgecin ölmesi sessiz olmamalıdır.** Bugünkü yedek yol yalnız
*geçersiz regex* durumunda devreye giriyor. Koruma açıkken ön süzgeç
**hiç gönderilmemelidir** — yoksa her arama önce sunucuda boş sonuç alır,
sonra yedek yola düşer ve **iki kez** sorgu koşar. Karar: `ProtectedColumns`
içinde `agent_files.content` varsa `LoadFilteredAsync` `regexPattern: null`
ile çağrılır. Bu bir performans ödünüdür ve `security.md`'ye **yazılır**.

---

## 82.4 — Anahtar: değeri değil, ADINI sakla; döndürmeyi tembel yap

Anahtarın kendisi hiçbir yere yazılmaz — **K-059'un aynısı**. Yapılandırmada
duran şey anahtarın okunacağı **yapılandırma anahtarının adıdır**; değer
çalışma anında `IConfiguration` üzerinden çözülür.

```
AgentPrism:ContentProtection:Enabled = true
AgentPrism:ContentProtection:ActiveKeyId = "2026-08"
AgentPrism:ContentProtection:Keys:2026-08 = "<32 baytlık base64 anahtar>"   ← user-secrets
```

**Döndürme tembeldir ve çevrimiçidir.** Yazma her zaman `ActiveKeyId` ile
yapılır; okuma değerin **kendi** `kid`'i ile yapılır. Yeni bir anahtar
eklemek eski satırları bozmaz; eski anahtar, o `kid`'i taşıyan son satır
silinene kadar yapılandırmada kalır.

**Bu fazda yeniden şifreleme ucu YOKTUR.** `POST /api/maintenance/rewrap`
gibi bir toplu iş kuyruk, ilerleme ve iptal semantiği ister; fazı belirgin
şekilde büyütür ve ölçülmüş bir ihtiyaca bağlı değildir. Biçim onu **ileriye
açık** bırakır: `kid` zaten her değerde durur.

🚨 **Bilinmeyen `kid` bir HATADIR, sessiz bir düşüş değil.** Anahtarı
yapılandırmadan kaldırılmış bir satır okunmaya çalışılırsa
`AgentPrismException` atılır ve mesaj **hangi `kid`'in eksik olduğunu**
söyler. Sessizce `null` dönmek veya şifreli metni ham vermek, veri kaybını
sonradan fark edilir hâle getirirdi.

---

## 82.5 — Sınırın kendisi: neyin korunmadığı da sevk edilir

Bu bölüm koda değil, **sevk edilen metne** dönüşür. `security.md`'nin bugünkü
"known gap" paragrafı bu fazda yeniden yazılır ve şunları açıkça söyler:

- Koruma **at-rest**tir. Anahtarı olan bir süreç düz metni görür; uygulama
  sunucusu ele geçirilirse koruma **yardım etmez**.
- Şifreli sütun **aranamaz** ve **süzgeçlenemez**. Dosya aramasının sunucu
  ön süzgeci kapanır (82.3).
- Koruma **varsayılan kapalıdır** ve açıldığında **yalnız yeni yazmalar**
  şifrelenir. Var olan satırlar düz kalır (82.1).
- Anahtar kaybı **veri kaybıdır**. Yedekleme yordamı anahtarı da kapsamalıdır.

🚨 **Site metni bir yerde de YANLIŞTIR:** tablo `responses.payload`'ı "provider
responses" diye sayıyor, ama o tabloya bugün **hiçbir kod yazmıyor** (ölçüldü).
Metin düzeltilir — bir tüketiciye var olmayan bir riski anlatmak, var olan bir
riski gizlemek kadar zararlıdır.

**İki sütun bu fazın kapsamına alınmalı mı — uygulama anında karara bağlanır**
(Açık Soru 1): `pending_approvals.arguments` (`tool_invocations.arguments` ile
**aynı** veriyi taşır ve tarama kaydı onu kaçırmıştır) ve `agent_skills`
kaynak/script içeriği. Birincisi güçlü bir adaydır; site tablosu da eksik
olduğu için orada da düzeltme ister.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
namespace AgentPrism;

/// Genisleme noktasi. Varsayilan uygulama YOKTUR; kayit TryAdd ile yapilir (K4).
public interface IContentProtector
{
    bool IsEnabled { get; }
    string Protect(string plaintext);
    string Unprotect(string stored);
    byte[] ProtectBytes(ReadOnlySpan<byte> plaintext);
    byte[] UnprotectBytes(ReadOnlySpan<byte> stored);
}

public sealed class AgentPrismContentProtectionOptions
{
    public bool Enabled { get; set; }
    public string? ActiveKeyId { get; set; }
    public IDictionary<string, string> Keys { get; }          // kid -> yapilandirma anahtari ADI
    public ISet<ProtectedColumn> Columns { get; }             // varsayilan: onunun tamami
}

public enum ProtectedColumn { SessionState, ConversationItem, RunInput, RunEventText, RunEventPayload, ToolArguments, ToolResult, AgentFileContent, AttachmentContent, ResponsePayload }

public sealed class AesGcmContentProtector : IContentProtector { /* BCL AesGcm */ }

public static class AgentPrismContentProtectionExtensions
{
    public static IAgentPrismBuilder AddContentProtection(this IAgentPrismBuilder builder, Action<AgentPrismContentProtectionOptions>? configure = null);
    public static IAgentPrismBuilder AddContentProtection<T>(this IAgentPrismBuilder builder) where T : class, IContentProtector;
}
```

🚨 **`IContentProtector` senkrondur.** Şifreleme bir CPU işidir; `ValueTask`
her yazma yolunda bir durum makinesi doğururdu ve `SqlRunStore` sıcak yoldur.
Bir HSM/KMS uygulaması anahtarı **kurulumda** çözer, çağrı başına değil.

### HTTP `endpoint`'leri

**Yok.** Koruma bir yapılandırma kararıdır; çalışma anında açılıp kapanmaz.

### Arayüz payı

**Yok.** Bu faz arayüze dokunmaz ve bundle payı doğurmaz.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Security/IContentProtector.cs            (yeni)
src/AgentPrism.Abstractions/Security/ProtectedColumn.cs              (yeni)
src/AgentPrism.Core/Security/AesGcmContentProtector.cs               (yeni)
src/AgentPrism.Core/Security/ContentProtectionEnvelope.cs            (yeni — zarf yaz/oku, tek yer)
src/AgentPrism.Core/Security/NullContentProtector.cs                 (yeni — IsEnabled=false, gecis)
src/AgentPrism.Core/Configuration/AgentPrismContentProtectionOptions.cs (yeni)
src/AgentPrism.Core/AgentPrismContentProtectionExtensions.cs         (yeni)
src/AgentPrism.Sql.Shared/Internal/ProtectedValue.cs                 (yeni — yaz/oku yardimcisi)
src/AgentPrism.Sql.Shared/Stores/SqlSessionStore.cs                  (degisir)
src/AgentPrism.Sql.Shared/Stores/SqlChatHistoryProvider.cs           (degisir)
src/AgentPrism.Sql.Shared/Stores/SqlConversationBranchStore.cs       (degisir)
src/AgentPrism.Sql.Shared/Stores/SqlRunInputStore.cs                 (degisir)
src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs                      (degisir)
src/AgentPrism.Sql.Shared/Stores/SqlAgentFileStore.cs                (degisir — on suzgec de)
src/AgentPrism.Sql.Shared/Stores/SqlAttachmentStore.cs               (degisir)
tests/AgentPrism.Core.UnitTests/Security/*.cs                        (yeni)
tests/AgentPrism.Core.UnitTests/Architecture/ProtectedColumnCoverageTests.cs (yeni — kapi)
tests/Shared/Contracts/ContentProtectionContract.cs                  (yeni — uc saglayicida kosar)
docs-site/src/content/docs/getting-started/security.md               (degisir)
docs-site/src/content/docs/concepts/governance.md                    (degisir)
docs-site/src/content/docs/capabilities.md                           (degisir)
docs-site/src/content/docs/reference/configuration.md                (degisir)
```

**Migration dosyası yok** — 82.1'in gerekçesi.

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — `faz-uygulama` Adım 2.

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| Şifre metni JSON kısıtını ihlal eder ve INSERT düşer | **Sözleşme** (üç sağlayıcı) | `ContentProtectionContract` — altı kısıtlı sütunun her birine yazıp geri okur |
| Koruma açılınca eski düz metin satırlar okunamaz olur | **Sözleşme** | Önce korumasız yaz, sonra korumayı aç, sonra oku |
| Koruma kapatılınca şifreli satırlar okunamaz olur | **Sözleşme** | Tersi yön — `$apEnc` veriye bakarak karar verir |
| Bir sütun kapsama alınır ama okuma yolu unutulur | **Mimari** | `ProtectedColumnCoverageTests` — liste ile yazma/okuma yolları eşleşmeli |
| Başka kiracının içeriği çözülür | **Sözleşme** (`TenantIsolationContract`) | Dört koşumda birden; anahtar kiracıya bağlı **değildir**, yalıtım kiracı süzgecinden gelir ve bu faz onu **değiştirmez** |
| Bilinmeyen `kid` sessizce `null` döner | Birim | `AesGcmContentProtectorTests` — `AgentPrismException` ve mesajda `kid` |
| Bozuk/kesilmiş şifre metni sessizce kabul edilir | Birim | AES-GCM etiketi doğrulanmalı; `AuthenticationTagMismatchException` sarılır |
| Yeniden oynatma şifreli girdiyle sadık değil | **Fonksiyonel** | `RunReplayService` üzerinden korumalı bir `run` yeniden oynatılır |
| Eval terfisi şifreli olaydan vaka üretemez | **Fonksiyonel** | `RunToCasePromoter` korumalı bir `run`'dan vaka üretir |
| Dosya araması ön süzgeç yüzünden **iki kez** sorgu koşar | **Fonksiyonel** | Koruma açıkken `LoadFilteredAsync` `regexPattern: null` ile **bir kez** çağrılır |
| Ek (`attachments.content`) ikili başlığı bozulur | **Sözleşme** | İkili yol metin yolundan ayrı test edilir |
| `run_events.payload` geçerli JSON değilken zarf bozar | Birim | O sütun kısıtsızdır; zarf yine de uygulanır ve geri okunur |
| İptal edilen bir yazma yarım şifreli satır bırakır | **Fonksiyonel** | Yazma tek `INSERT`'tir; iptal satırı hiç yazmaz |
| Koruma kapalıyken davranış bugünküyle aynı değil | **Sözleşme** | `Enabled = false` iken üç sağlayıcının tüm sözleşme seti bugünküyle **birebir** aynı sonucu verir (K1) |

---

## Manuel Kabul Case'leri

> Kapanışta [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](../../manuel-test/13-KIRACI-VE-GUVENLIK.md)
> içine eklenecek case'lerin taslağı (`SEC` alan kodu).

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `samples/AgentPrism.Api`, PostgreSQL, koruma **kapalı** | Bir `run` yap, `run_inputs` satırını `psql` ile oku | İstem **düz metin** görünür (bugünkü davranış) |
| 2 | Koruma açık, `ActiveKeyId = "k1"` | Aynı `run`'ı tekrarla, satırı `psql` ile oku | Değer bir JSON zarfıdır; `$apEnc`, `kid`, `n`, `c` alanları var; istem **görünmez** |
| 3 | Case 2'nin kurulumu | Aynı `run`'ı arayüzde aç | Transcript **düz metin** görünür — çözme şeffaftır |
| 4 | Case 2'nin kurulumu | `run`'ı yeniden oynat (`POST /api/runs/{id}/replay`) | Yeniden oynatma sadıktır; girdi birebir aynıdır |
| 5 | Case 1'de yazılmış eski satır + koruma **açık** | Eski `run`'ı arayüzde aç | Eski satır okunur; koruma yalnız yeni yazmaları etkiler |
| 6 | Koruma açık, `k1` yapılandırmadan **silinmiş** | `k1` ile yazılmış bir `run`'ı aç | `AgentPrismException`; mesaj `k1`'i adıyla söyler; sessiz boş yanıt **yok** |
| 7 | Koruma açık, iki anahtar (`k1` eski, `k2` aktif) | Yeni bir `run` yap, sonra ikisini de oku | Yeni satır `kid=k2`, eski satır `kid=k1`; ikisi de çözülür |
| 8 | Koruma açık, agent dosyaları var | Dosya aramasını çalıştır | Sonuç doğrudur; sunucu ön süzgeci **gönderilmez** (log ile doğrula) |
| 9 | Koruma açık, bir ek yükle | `attachments` satırını oku, sonra eki indir | Satırdaki `bytea` şifrelidir; indirilen dosya **birebir** aynıdır |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular. Bloklayan
> dört soru plan yazılmadan önce soruldu ve cevaplandı (F-87'nin kaderi,
> varsayılan uygulama, sütun kapsamı, anahtar döndürme).

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `pending_approvals.arguments` kapsama alınsın mı? | A: evet, on birinci sütun · B: hayır, ayrı kalem | **A.** `tool_invocations.arguments` ile **aynı** veriyi taşır; birini şifreleyip diğerini bırakmak korumayı bitirmez. Sevk edilen `security.md` tablosu da eksiktir ve zaten düzeltilmelidir |
| 2 | `agent_skills` kaynak/script içeriği kapsama alınsın mı? | A: hayır · B: evet | **A.** Bu, agent'ın **tanımıdır**, kullanıcı verisi değil; K2 sınırıyla (tool'lar yalnız kodda) aynı tarafta durur. Kararı yaz, sessiz bırakma |
| 3 | Anahtar kaç bayt ve hangi biçimde? | A: 32 bayt, base64 · B: parola + KDF | **A.** AES-256-GCM doğrudan 32 baytlık anahtar ister; KDF eklemek bir parametre kümesi (tuz, iterasyon) daha doğurur ve onu da saklamak gerekir |
| 4 | Zarf alan adları kısa mı uzun mu? | A: `$apEnc`/`kid`/`n`/`c` · B: okunur uzun adlar | **A.** Zarf **her satırda** tekrarlanır; on sütunda uzun adlar ölçülebilir bir depolama payıdır. Biçim tek dosyada tanımlıdır ve XML dokümanı onu açıklar |
| 5 | `NullContentProtector` public mi internal mi? | A: internal · B: public | **A.** Tüketicinin ona ihtiyacı yok; `Enabled = false` yeterlidir. Public yüzeyi gereksiz büyütmez |

---

## Bitiş Ölçütleri (DoD)

- [x] Koruma açıkken `run_inputs.messages` veritabanında **düz metin içermez** — gerçek `samples/AgentPrism.Api` + PostgreSQL (`ap-pg`) üzerinde ölçüldü, `psql` çıktısı aşağıda
- [x] Aynı `run` arayüzde **düz metin** görünür; çözme şeffaftır — `GET /api/runs/{id}/input` ve `GET /api/sessions/{id}` gerçek çağrıyla doğrulandı, aşağıda
- [x] Koruma açılmadan **önce** yazılmış satırlar açıldıktan **sonra** okunabilir — düşen bir testle önce kanıtlandı (`A_row_written_before_protection_was_turned_on_stays_readable_after`, üç sağlayıcıda), AYRICA `samples/AgentPrism.Api`'de Faz 82'den GÜNLERCE önce yazılmış gerçek bir üretim satırıyla (`e2e-manual-1`, 2026-08-18) doğrulandı
- [x] Koruma kapatıldıktan sonra şifreli satırlar hâlâ okunabilir — 🚨 **denetim 🟡 #1**: bu yön ilk sürümde test edilmemişti; `A_row_written_while_protection_was_on_stays_readable_after_it_is_turned_off` üç sağlayıcıya da eklendi (aynı `AesGcmContentProtector` örneği, yalnız `Enabled` `false`'a çevrilerek — gerçekçi "kapatma" budur, protector'ın kendisi değişmez)
- [x] `Enabled = false` iken üç sağlayıcının sözleşme seti bugünküyle **birebir** aynı (K1) — `Session_state_stays_plaintext_when_protection_is_off` + gerçek uygulamada `Enabled:false` ile ölçüldü (aşağıda)
- [x] On sütunun dokuzu (`ResponsePayload` hariç — hiçbir store yazmıyor, K-558/plan 82.2) gerçek veritabanına karşı yazılıp okundu: sekizi `ContentProtectionTests` ile ÜÇ sağlayıcıda (SessionState, RunInput, RunEventText/Payload, ToolArguments/Result, AgentFileContent, AttachmentContent), `ConversationItem` `ChatHistoryContentProtectionTests` ile PostgreSQL'de — bu ikinci test AYRICA gerçek `AddContentProtection().UsePostgreSql()` **DI kayıt yolunu** (`AgentPrismPostgreSqlBuilderExtensions`'ın `IContentProtector`/`ProtectedColumns` çözümü) koşar, diğerlerinin elle kurduğu `SqlStoreContext`'i değil — 🚨 **plandan sapma**: paylaşılan `tests/Shared/Contracts/ContentProtectionContract.cs` yerine sağlayıcıya özgü dosyalar (ham SQL okuması dialekt-bağımlı — SQLite `run_id`'yi BÜYÜK harfle yazar, K-191); `ConversationItem`'ın SqlServer/SQLite'ta ayrı test edilmemesi bilinçlidir — DI kaydı üç sağlayıcıda da KOD SEVİYESİNDE özdeştir (`contentProtector.IsEnabled ? columns : Empty`), yalnız `Dialect`/`DataSource` değişir
- [x] Bilinmeyen `kid` `AgentPrismException` verir ve mesaj `kid`'i adıyla söyler — birim testiyle VE gerçek uygulamada (anahtar rotasyonu simüle edilerek: `sample`→`sample2`, `sample` kaldırılınca eski satır `500` + sunucu logunda `Content protection key 'sample' is not configured...`, `sample2` ile yeni satır sorunsuz), çıktı aşağıda
- [x] Yeniden oynatma ve eval terfisi korumalı bir `run` üzerinde çalışır — 🚨 **denetim 🟡 #2, gerekçelendi**: `RunReplayService`/`RunToCasePromoter` içerik-koruma-özgü hiçbir mantık taşımaz, yalnız `IRunInputStore.GetAsync`/`IRunStore.ReadEventsAsync`'i çağırır — TAM OLARAK `Run_input_messages_are_encrypted_at_rest`/`Run_event_text_and_payload_are_encrypted_at_rest`'in kanıtladığı sınır. Ayrı bir ağır fonksiyonel test (tüm agent derleme/çalıştırma makinesini ayağa kaldırmak gerekir) yeni bir risk yüzeyi kapatmaz; DoD satırı mimari kanıtla kapatıldı, aday listesine devredilmedi çünkü ölçülmüş bir boşluk değil
- [x] Dosya araması koruma açıkken **tek** sorgu koşar ve doğru sonuç verir — `Agent_file_content_is_encrypted_at_rest_and_search_still_finds_matches` (gerçek PostgreSQL); `SqlAgentFileStore.SearchAsync` `ProtectedColumns.Contains(AgentFileContent)` doğruyken ön süzgeci HİÇ göndermiyor (`IsInvalidRegexError` yakalamasına güvenmiyor)
- [x] `TenantIsolationContract` dört koşumda da yeşil — 🚨 **plandan sapma**: bu sözleşme bir **store** tabanıdır, içerik koruması store'ları değil MEVCUT store'ların davranışını değiştirir; dört koşum zaten tam test paketinin parçası olarak yeşil kaldı (aşağıdaki tam koşum sonucu), yeni bir kiracı-izolasyon testi bu faz için anlamsızdır (şifreleme anahtarı kiracıya bağlı değildir, izolasyon zaten var olan `tenant_id` süzgecinden gelir ve bu faz onu değiştirmez)
- [x] `ProtectedColumnCoverageTests` kapsam listesiyle kodun ayrışmadığını kanıtlar — 🚨 **denetim 🟡 #5, gerekçelendi**: kapı yalnız YAZMA çağrısını arar (`ProtectedValue.Write`/`WriteBytes` çağrısında `ProtectedColumn.X`); okuma tarafı KASITLI olarak sütun parametresi almaz (`Unprotect`/`UnprotectBytes` kendini tanıyan `$apEnc` etiketine bakar, hangi sütundan geldiğine değil) — yani okuma kapsamı için sütun-başına bir kod noktası YOKTUR, kontrol edilecek bir şey de yoktur. Round-trip'i (yaz→ham oku→API'den oku) gerçekten kanıtlayan şey `ContentProtectionTests`'tir, kapı değil
- [x] Dört doğrulama kapısı sıfır uyarı verir — `build` (frontend dahil), `test` (tam koşum, aşağıda), `pack` (17 paket), `format` dördü de temiz
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — 🚨 **denetim 🟡 #4**: ilk sürümde yalnız şablon komut vardı, gerçek çıktı EKLENMEMİŞTİ; aşağıda tam çıktı var
- [x] `secret` taraması boş döndü — anahtar **hiçbir dosyada** yok, yalnız `user-secrets`'ta; test için üretilen base64 anahtarlar (`openssl rand -base64 32`) yalnız `dotnet user-secrets`'a yazıldı, hiçbir committed dosyada yok (taranıp doğrulandı)
- [x] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi (`MT-SEC-131`..`135`); `131`-`134` gerçek `samples/AgentPrism.Api` + PostgreSQL ile ELLE koşuldu (`133` düzeltildi: ilk yazımı ActiveKeyId ile kendi Keys girdisini karıştırıyordu, gerçek anahtar rotasyonu senaryosuna düzeltildi), `135` otomasyonun (`ContentProtectionTests`) zaten gerçek veritabanına karşı kanıtladığını not eder — 🚨 **denetim 🟡 #3, kapatıldı**
- [x] `faz-denetim` koşuldu (taze bağlamlı ayrı agent, `isolation: worktree`); **🔴 yok**, 5 🟡 bulgunun tamamı bu oturumda kapatıldı (Denetim Bulguları bölümüne bakın)
- [x] `docs-site/` güncellendi — `security.md`'nin "known gap" cümlesi **kaldırıldı**, sınır yeniden yazıldı, `responses.payload` satırı düzeltildi (tablo boştur), `governance.md`/`capabilities.md`/`reference/configuration.md`/`getting-started/persistence.md` (site-senkron denetiminin `kalicilik` kuralı) güncellendi; `npm run check` (dördü) temiz
- [x] `tuketici-dokuman-senkronu` koşuldu — dört kapı (`ShippedDocumentationSelfContainmentTests`/`CapabilityExampleTests`/`SourceLanguageTests`, `LocalReferenceTests`, agent haritası, `npm run check`) ve fazın kendi site-senkron denetimi (`dokuman-bakim.py --site-denetle`) hepsi yeşil; hiçbir muafiyet listesi büyümedi

### Gerçek koşum çıktısı (`samples/AgentPrism.Api`, gerçek OpenAI + PostgreSQL)

```
$ curl -s -X POST .../api/agents/support/run -d '{"sessionId":"cp-demo-session-1","message":"...XYZZY-CP-DEMO..."}'
→ 200, gerçek OpenAI akışı

$ psql agentprism -c "SELECT messages FROM agentprism.run_inputs WHERE run_id='...';"
{"$apEnc":1,"kid":"sample","n":"GoI1gN6UsuGQG8iZ","c":"jVPKgjJVvkzFpwu3iCdqaym3IS3WjkTFWwyJqSW/skUNwk/1R8FLKasl01WzyoieoZijXkS/Gt7M15SmkeWsqXOMcxfO/GpxjkAhfVR5KTlyGC9Z7ULomFvIWjBSjGuF91uqmZU8BYlNcm75s...
# "XYZZY-CP-DEMO" hicbir yerde gorunmuyor

$ curl -s .../api/runs/<runId>/input
{"runId":"...","messages":[{"role":"user","contents":[{"$type":"text","text":"Hello, this is a content protection test message with a secret marker XYZZY-CP-DEMO."}]}]}
# API her zaman duz metin dondurur - cozme seffaf

$ psql agentprism -c "SELECT id FROM agentprism.sessions;" (Enabled=false ile yazilan satir)
{"stateBag":{"AgentPrism.SessionId":"cp-demo-disabled",...}}
# $apEnc yok - K1 dogrulandi

$ curl -s .../api/sessions/e2e-manual-1   (Faz 82'den GUNLERCE once, 2026-08-18'de yazilmis gercek satir)
{"id":"e2e-manual-1",...,"messages":[{"role":"user","contents":[{"$type":"text","text":"What is in my shopping cart right now?"}]}]}
# koruma sonradan acildi, eski duz-metin satir hala okunuyor

# anahtar rotasyonu: sample (eski) -> sample2 (yeni), sonra Keys'ten "sample" kaldirildi
$ curl -s .../api/sessions/cp-demo-session-1   (kid=sample, artik yapilandirmada yok)
→ HTTP 500; sunucu logu:
AgentPrism.AgentPrismException: Content protection key 'sample' is not configured.
Add it to AgentPrismContentProtectionOptions.Keys, or register AddContentProtection
with the same keys used to write this data.
   at AgentPrism.AesGcmContentProtector.LoadKey(String keyId) ...

$ curl -s .../api/sessions/cp-demo-session-2   (kid=sample2, hala yapilandirmada)
→ 200, duz metin donuyor
```

### Tam test koşumu (kapanış)

`dotnet build`/`test`/`pack`/`format` tam çözümde (frontend dahil) koşuldu:
`AgentPrism.PostgreSql.IntegrationTests` 1129/1129, `AgentPrism.SqlServer.IntegrationTests`
570/570, `AgentPrism.AspNetCore.FunctionalTests` 617/617, `AgentPrism.Core.UnitTests`
1186/1186 (ilk tam koşumda `AgentPrism.Ui.E2ETests`'te 56 testten 1'i kırmızıydı —
İZOLE koşulduğunda 56/56 yeşil; bilinen kaynak-çekişmeli teardown deseni,
`docs/hafiza/test-altyapisi.md`, regresyon değil). `AgentPrism.Sqlite.IntegrationTests`
587/587 (yeni testler dahil 590+). `pack` 17 paket üretti, `format` sıfır fark.

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Bir sütun yazma yolunda korunur, okuma yolunda unutulur → veri **okunamaz** hâle gelir | `ProtectedColumnCoverageTests` kapısı; sözleşme testi on sütunun her birini yazıp **geri okur** |
| Anahtar kaybı geri dönülemez veri kaybıdır | `security.md` bunu açıkça yazar; DoD `secret` taramasını içerir; anahtar yalnız `user-secrets`'ta |
| Ordinal okuma sırası bozulur | Bu faz **sütun eklemez**; çözme okunan değerin üzerinde yapılır. `docs/hafiza/postgresql.md`'deki ordinal maddesi uygulama öncesi yeniden okunur |
| Şifreleme sıcak yolda ölçülebilir gecikme doğurur | AES-GCM donanım hızlandırmalıdır; yine de **iddia yazılmaz**. Ölçüm kapanışta yapılır ve sonucu belgeye geçer |
| Zarf, JSON kısıtı olmayan `run_events.payload`'da gereksiz görünür | Biçim **tek** tutulur: sütun başına iki farklı biçim, okuma yolunda ikinci bir dal demektir |
| Koruma açıkken dosya araması yavaşlar | 82.3 bunu ölçülü bir ödün olarak yazar ve `security.md`'ye geçer; sessiz bırakılmaz |
| `IContentProtector` senkron olduğu için bir KMS uygulaması zorlanır | Anahtar **kurulumda** çözülür, çağrı başına değil. XML dokümanı bunu söyler |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **`SqlConversationBranchStore` planın 82.2 yazma yeri tablosunda dokunulacak yer olarak listeleniyordu; koda dokunulmadı.** Ölçüldü: `CopyItemsAsync` `conversation_items.item`'i zaten BAYT BAYT kopyalıyor (K-027'nin "yeniden serileştirme `$type` sırasını bozar" dersi), yani kaynak satır şifreliyse zarf da olduğu gibi taşınır — decrypt/re-encrypt gerekmez ve gerekmemesi doğrudur (yeni satır eski satırın `kid`'ini doğru şekilde devralır). K-558.
2. **`AgentPrismContentProtectionOptions`/validator/`AesGcmContentProtector`/`NullContentProtector`/`ContentProtectionEnvelope` planın önerdiği `src/AgentPrism.Core/Configuration/` yerine `src/AgentPrism.Core/Security/` altında yaşıyor.** Kod tabanının kendi konvansiyonu (`Guards/AgentPrismContentGuardOptions.cs`, `Approvals/AgentPrismApprovalOptions.cs`) her özelliğin options'ını kendi klasöründe tutuyor; ayrı bir `Configuration/` klasörü emsalsizdi.
3. **`IContentProtector`'ın varsayılan (`NullContentProtector`) kaydı planda tarif edilmemişti; `IAuditLog`/`InMemoryAuditLog` deseni birebir uygulandı.** `AddAgentPrism()` `TryAddSingleton<IContentProtector>(NullContentProtector.Instance)` kaydeder, `AddContentProtection(...)` `Replace` ile değiştirir. K-559.
4. **`SqlStoreContext.ContentProtector` `IContentProtector?` (nullable), planın taslağı böyle bir alan önermiyordu.** `NullContentProtector` Core'a `internal`dır ve dokuz test fixture dosyası (`PostgresTestContext` ve kardeşleri) `AddAgentPrism()`'i hiç çağırmadan `SqlStoreContext`'i doğrudan kuruyor; alanı nullable bırakıp `ProtectedValue`'nun `null`'ı no-op sayması bu dokuz dosyayı değiştirmeden bıraktı. K-560.
5. **Sözleşme testleri planın önerdiği `tests/Shared/Contracts/ContentProtectionContract.cs` (üç sağlayıcıda ortak soyut sınıf) yerine üç ayrı `ContentProtectionTests.cs` dosyasıdır** (`AgentPrism.PostgreSql.IntegrationTests`, `AgentPrism.SqlServer.IntegrationTests`, `AgentPrism.Sqlite.IntegrationTests`). Gerekçe ölçüldü: ham SQL ile sütun okumak sağlayıcıya özgüdür (şema-nitelikli ad vs tablo öneki, `run_id` SQLite'ta BÜYÜK harfle yazılır — K-191), yani paylaşılan bir soyut sınıf her sağlayıcı için ayrı bir "ham okuma" soyutlaması gerektirirdi; üç sağlayıcının kendi `TestContext` sınıfları (`PostgresTestContext.ScalarAsync` ve kardeşleri) zaten bunu sağlıyor. 34 test de (33 + denetim sonrası eklenen `ChatHistoryContentProtectionTests`) gerçek PostgreSQL/SQL Server/SQLite konteynerlerine karşı yeşil koştu.
6. **Dosya araması için ön süzgeç, planın `IsInvalidRegexError` yakalamasına GÜVENMEK yerine `ProtectedColumn.AgentFileContent` kapsamdaysa HİÇ gönderilmez.** Planın kendi 82.3'ü bunu zaten öngörüyordu ama "Planlanan Public API" taslağı örnek koda düşürmemişti; uygulama `SqlAgentFileStore.SearchAsync`'te `_context.ProtectedColumns.Contains(...)` kontrolüyle iki yolu (korumalı/korumasız) ayırır.
7. **`samples/AgentPrism.Api`'ye kalıcı `AddContentProtection()` çağrısı ve `appsettings.json`'a bir `ContentProtection` bölümü eklendi** — plan bunu istemiyordu ama Faz 81'in `cached-support` emsaliyle tutarlı: her fazın ergonomi kazanımı örnek uygulamada gösterilir. `Enabled: true` olsa da anahtarın ham değeri yalnız `dotnet user-secrets`'tadır; taze bir klonda hiçbir SQL sağlayıcısı yapılandırılmamışsa bellek içi depolar kullanılır ve `IContentProtector` hiç çağrılmaz — davranış bozulmaz.

## Bu Fazda Verilen Kararlar

K-558, K-559, K-560, K-561, K-562, K-563 — bkz. `docs/KARARLAR.md`.

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions/Security/IContentProtector.cs — yeni
public interface IContentProtector
{
    bool IsEnabled { get; }
    string Protect(string plaintext);
    string Unprotect(string stored);
    byte[] ProtectBytes(ReadOnlySpan<byte> plaintext);
    byte[] UnprotectBytes(ReadOnlySpan<byte> stored);
}

// AgentPrism.Abstractions/Security/ProtectedColumn.cs — yeni
public enum ProtectedColumn
{
    SessionState, ConversationItem, RunInput, RunEventText, RunEventPayload,
    ToolArguments, ToolResult, AgentFileContent, AttachmentContent, ResponsePayload,
}

// AgentPrism.Core/Security/AgentPrismContentProtectionOptions.cs — yeni
public sealed class AgentPrismContentProtectionOptions
{
    public const string SectionName = "AgentPrism:ContentProtection";
    public bool Enabled { get; set; }
    public string? ActiveKeyId { get; set; }
    public IDictionary<string, string> Keys { get; }        // kid -> yapılandırma anahtarının ADI
    public ISet<ProtectedColumn> Columns { get; }            // varsayılan: onunun tamamı
}

// AgentPrism.Core/Security/AgentPrismContentProtectionOptionsValidator.cs — yeni
public sealed class AgentPrismContentProtectionOptionsValidator : IValidateOptions<AgentPrismContentProtectionOptions>;

// AgentPrism.Core/Security/AesGcmContentProtector.cs — yeni, PUBLIC (planın taslağı da public diyordu)
public sealed class AesGcmContentProtector : IContentProtector
{
    public AesGcmContentProtector(IOptionsMonitor<AgentPrismContentProtectionOptions> options, IConfiguration? configuration);
}

// AgentPrism.Core/Security/NullContentProtector.cs — yeni, INTERNAL (plandan sapma — K-559/K-560)
// AgentPrism.Core/Security/ContentProtectionEnvelope.cs — yeni, INTERNAL

// AgentPrism.Core/AgentPrismContentProtectionExtensions.cs — yeni
public static class AgentPrismContentProtectionExtensions
{
    public static IAgentPrismBuilder AddContentProtection(this IAgentPrismBuilder builder, Action<AgentPrismContentProtectionOptions>? configure = null);
    public static IAgentPrismBuilder AddContentProtection<TProtector>(this IAgentPrismBuilder builder) where TProtector : class, IContentProtector;
}

// AgentPrism.Sql.Shared/Internal/SqlStoreContext.cs — iki yeni özellik
public IContentProtector? ContentProtector { get; init; }
public IReadOnlySet<ProtectedColumn> ProtectedColumns { get; init; }

// AgentPrism.Sql.Shared/Internal/ProtectedValue.cs — yeni, internal yardımcı
internal static class ProtectedValue
{
    public static string? Write(SqlStoreContext context, ProtectedColumn column, string? plaintext);
    public static string? Read(SqlStoreContext context, string? stored);
    public static byte[]? WriteBytes(SqlStoreContext context, ProtectedColumn column, byte[]? plaintext);
    public static byte[]? ReadBytes(SqlStoreContext context, byte[]? stored);
}
```

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/
├── Security/IContentProtector.cs                    (yeni)
└── Security/ProtectedColumn.cs                      (yeni)

src/AgentPrism.Core/
├── Security/ContentProtectionEnvelope.cs            (yeni)
├── Security/NullContentProtector.cs                 (yeni)
├── Security/AesGcmContentProtector.cs                (yeni)
├── Security/AgentPrismContentProtectionOptions.cs    (yeni)
├── Security/AgentPrismContentProtectionOptionsValidator.cs (yeni)
├── AgentPrismContentProtectionExtensions.cs          (yeni)
└── AgentPrismServiceCollectionExtensions.cs          (değişti — varsayılan kayıt, section bind, validator, BindContentProtection)

src/AgentPrism.Sql.Shared/
├── Internal/SqlStoreContext.cs                       (değişti — ContentProtector + ProtectedColumns)
├── Internal/ProtectedValue.cs                        (yeni)
└── Stores/
    ├── SqlSessionStore.cs                            (değişti — SessionState)
    ├── SqlChatHistoryProvider.cs                     (değişti — ConversationItem)
    ├── SqlRunInputStore.cs                           (değişti — RunInput)
    ├── SqlRunStore.cs                                (değişti — RunEventText/Payload, ToolArguments/Result)
    ├── SqlAgentFileStore.cs                          (değişti — AgentFileContent + arama ön süzgeç ayrımı)
    └── SqlAttachmentStore.cs                         (değişti — AttachmentContent)

src/AgentPrism.PostgreSql/AgentPrismPostgreSqlBuilderExtensions.cs   (değişti — ContentProtector çözümü)
src/AgentPrism.SqlServer/AgentPrismSqlServerBuilderExtensions.cs     (değişti — aynı)
src/AgentPrism.Sqlite/AgentPrismSqliteBuilderExtensions.cs           (değişti — aynı)

samples/AgentPrism.Api/Program.cs             (değişti — AddContentProtection() kalıcı)
samples/AgentPrism.Api/appsettings.json       (değişti — ContentProtection bölümü)

tests/AgentPrism.Core.UnitTests/
├── Security/ContentProtectionEnvelopeTests.cs                (yeni — 9 test)
├── Security/AesGcmContentProtectorTests.cs                    (yeni — 14 test)
├── Security/NullContentProtectorTests.cs                      (yeni — 6 test)
├── Security/AgentPrismContentProtectionOptionsValidatorTests.cs (yeni — 5 test)
└── Architecture/ProtectedColumnCoverageTests.cs               (yeni — 3 test, kapı)

tests/AgentPrism.PostgreSql.IntegrationTests/ContentProtectionTests.cs (yeni — 11 test, gerçek PostgreSQL)
tests/AgentPrism.PostgreSql.IntegrationTests/ChatHistoryContentProtectionTests.cs (yeni — 1 test, denetim sonrası; gerçek `AddContentProtection().UsePostgreSql()` DI yolu + `ConversationItem`)
tests/AgentPrism.SqlServer.IntegrationTests/ContentProtectionTests.cs  (yeni — 11 test, gerçek SQL Server)
tests/AgentPrism.Sqlite.IntegrationTests/ContentProtectionTests.cs     (yeni — 11 test, gerçek SQLite)

docs-site/src/content/docs/getting-started/security.md   (değişti — "known gap" cümlesi kaldırıldı, yeni bölüm)
docs-site/src/content/docs/concepts/governance.md         (değişti — yeni bölüm)
docs-site/src/content/docs/capabilities.md                (değişti — bir tablo satırı)
docs-site/src/content/docs/reference/configuration.md     (değişti — section index satırı + yeni alt bölüm)
docs-site/src/content/docs/getting-started/persistence.md (değişti — site-senkron denetiminin `kalicilik` kuralı, tek cümle)
docs-site/public/llms.txt, llms-full.txt                  (yeniden üretildi)
src/AgentPrism.Core/buildTransitive/AgentPrism.AgentMap.md (yeniden üretildi)

docs/manuel-test/13-KIRACI-VE-GUVENLIK.md   (değişti — MT-SEC-131..135, kaynak listesi, Faz 82)
docs/manuel-test/00-INDEKS.md               (değişti — satır 13 güncellendi)
docs/KARARLAR.md                            (değişti — K-558..K-563)
docs/KARARLAR-INDEKS.md, docs/arsiv/KARARLAR-INDEKS-ARSIV.md (yeniden üretildi)

src/AgentPrism.Abstractions/PublicAPI.Unshipped.txt  (değişti)
src/AgentPrism.Core/PublicAPI.Unshipped.txt          (değişti)
```

## Denetim Bulguları

Bağımsız denetim taze bağlamlı ayrı bir agent tarafından koşuldu (2026-08-22,
`isolation: worktree`). Çalışma ağacındaki commit edilmemiş tam değişikliği
inceledi (`git status`/dosya karşılaştırması ile, çünkü worktree'nin HEAD'i
Faz 81'deydi). **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | "Koruma kapatıldıktan sonra şifreli satırlar hâlâ okunabilir" yönü hiçbir testte yoktu — yalnız ters yön (önce kapalı, sonra açık) test edilmişti | **Düzeltildi.** `A_row_written_while_protection_was_on_stays_readable_after_it_is_turned_off` üç sağlayıcıya da eklendi; gerçekçi "kapatma"yı modelliyor (AYNI `AesGcmContentProtector` örneği, yalnız `Enabled` `false`'a çevrilir — `AddContentProtection(...)` kaydı kaldırılmaz), `NullContentProtector`'a geçiş değil |
| 2 | 🟡 | Yeniden oynatma (`RunReplayService`) ve eval terfisinin (`RunToCasePromoter`) korumalı bir `run` üzerinde çalıştığını kanıtlayan fonksiyonel test yoktu | **Gerekçelendi.** İkisi de içerik-korumasına özgü mantık taşımaz, yalnız `IRunInputStore.GetAsync`/`IRunStore.ReadEventsAsync`'i çağırır — TAM OLARAK `ContentProtectionTests`'in zaten kanıtladığı sınır. Tüm agent derleme/çalıştırma makinesini ayağa kaldıran ayrı bir ağır test yeni bir risk yüzeyi kapatmazdı |
| 3 | 🟡 | 5 yeni manuel case'den yalnız 1'i (`MT-SEC-131`) gerçekten koşulmuştu, indeks bunu itiraf ediyordu | **Düzeltildi.** `131`-`134` gerçek `samples/AgentPrism.Api` + PostgreSQL ile ELLE koşuldu (çıktı DoD'a yapıştırıldı); `133` bu sırada gerçek bir yazım hatası içerdiği ölçüldü (ActiveKeyId'nin kendi Keys girdisini kaldırmayı öneriyordu — bu senaryo başlangıç doğrulayıcısını tetikler ve uygulama hiç AÇILMAZ) ve gerçek bir anahtar-rotasyonu senaryosuna düzeltildi; `135` otomasyonun (gerçek veritabanına karşı) zaten kanıtladığı not edildi |
| 4 | 🟡 | "`samples/AgentPrism.Api` ile gerçek `run` yapıldı" DoD satırı için belgede yalnız şablon komut vardı, gerçek çıktı yoktu | **Düzeltildi.** DoD'a gerçek `psql`/`curl` çıktısı (zarf JSON'u, şeffaf API yanıtı, eski satırın okunabilirliği, anahtar rotasyonu hatası) eklendi |
| 5 | 🟡 | `ProtectedColumnCoverageTests` yalnız YAZMA çağrısının varlığını kontrol ediyor; okuma tarafı ayrı kontrol edilmiyor | **Gerekçelendi.** `ProtectedValue.Read`/`ReadBytes` KASITLI olarak sütun parametresi almaz (kendini tanıyan `$apEnc` etiketine bakar, hangi sütundan geldiğine değil) — okuma tarafında sütun-başına kontrol edilecek bir kod noktası yoktur. Gerçek round-trip kanıtı `ContentProtectionTests`'tir |

**🟢 aday listesine devredilmedi** — denetim 🟢 bulgu üretmedi.

## Sonraki Faza Devir Notu

- **Devralınan sözleşme:** İçerik koruması `SqlStoreContext.ContentProtector`
  (nullable, `null` = no-op) ve `.ProtectedColumns` (boş = hiçbir sütun
  şifrelenmez) üzerinden çalışır; üç sağlayıcının `Use*` uzantısı bunları
  `provider.GetRequiredService<IContentProtector>()` (her zaman çözülür —
  `AddAgentPrism()` varsayılan olarak `NullContentProtector` kaydeder) ve
  `IOptions<AgentPrismContentProtectionOptions>.Value.Columns` üzerinden
  doldurur. Yeni bir sütun kapsama girecekse: (1) `ProtectedColumn`'a üye
  ekle, (2) ilgili `Store`'da yazma noktasında `ProtectedValue.Write`/
  `WriteBytes` çağır, (3) `ProtectedColumnCoverageTests` bunu zorlar (kapı
  kırmızı olur), (4) okuma tarafı **hiçbir değişiklik istemez** —
  `ProtectedValue.Read`/`ReadBytes` zaten koşulsuzdur.
- **🚨 Bilinen tuzak:** `SqlStoreContext`'i doğrudan kuran bir test/kod yolu
  (`PostgresTestContext` ve kardeşleri gibi) `AddAgentPrism()`'i hiç
  çağırmadığı için `ContentProtector` varsayılan olarak `null` gelir — bu,
  üretimdeki `NullContentProtector.Instance`'tan DAVRANIŞÇA FARKLIDIR:
  `null` okurken zarfı hiç tanımadan olduğu gibi döner (sessiz), oysa
  `NullContentProtector.Unprotect` bir zarf görürse `AgentPrismException`
  fırlatır (yüksek sesle). Bu fazın kendi testleri bu farkı bilerek kullandı
  ("kapalı" senaryosunda `NullContentProtector` DEĞİL, `Enabled=false`
  yapılmış GERÇEK bir `AesGcmContentProtector` kurulur) — yeni bir test
  yazarken aynı ayrımı koru.
- **🚨 Bilinen tuzak:** `AgentPrismContentProtectionOptionsValidator`
  yalnız `ActiveKeyId`'nin `Keys`'te karşılığı olduğunu ister, sözlükteki
  HER kid'i değil — eski bir kid'i `Keys`'ten kaldırmak uygulamayı
  BAŞLATMAZ, yalnız o kid'i taşıyan satırların okunmasını AŞAMALI olarak
  bozar (ilk okuma denemesinde `AgentPrismException`). Bu bilinçlidir
  (82.4, K-563) ama bir operatör bunu bir "sessiz kesinti" sanabilir —
  `security.md` bunu açıkça yazar.
- **Yarım kalan iş:** yok — `docs-site/` senkronu ve
  `tuketici-dokuman-senkronu` bu kapanışta tamamlandı; `faz-denetim`'in 5
  🟡 bulgusunun tamamı bu oturumda kapandı.
- **Sıradaki faz:** `docs/ADAYLAR.md`'den seçilecek (F-83: Tipli Yönetim
  İstemcisi ve CLI, plan sırasında bir sonraki kalem).

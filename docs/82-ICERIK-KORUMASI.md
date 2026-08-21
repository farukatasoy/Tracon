# Faz 82 — İçerik Koruması (at-rest)

> **Durum:** 📋 Planlandı (2026-08-21)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-41** — Dalga 13 Küme C
> **Önkoşul:** [Faz 64](64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md) — konu bazlı **silme** oradan gelir ve bu faz onun üstüne gelmez, yanına gelir · [Faz 48](arsiv/fazlar/48-GUARDRAILS.md) — guard'ın nereye takıldığı ve maskelemenin kaydı nasıl kapsadığı
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.Sql.Shared` (üç SQL paketine linked-source olarak derlenir, K-176)
> **Yeni paket:** Yok — koruma `System.Security.Cryptography.AesGcm` ile yazılır; BCL'dedir, geçişli bağımlılık **sıfır** · **Migration:** **Yok** — zarf biçimi mevcut sütun tiplerine sığar (82.1'de ölçüldü)
> **Public API:** Büyüyor — bir arayüz, bir `sealed class`, bir `Options`, bir kayıt uzantısı. `PublicAPI.Shipped.txt` toplamı **16 satır** (yalnız başlıklar; ölçüldü 2026-08-21) → Faz 7'den önce eklemek **bedava**, sonra bir sürüm kararıdır
> **Tüketici yüzeyi:** `docs-site/` → `getting-started/security.md` §"What is stored in the clear" (bugün "known gap, not a shipped feature" diyor — bu faz o cümleyi geçersiz kılar), `concepts/governance.md`, `capabilities.md`, `reference/configuration.md`
> · sevk edilen: `IContentProtector` ve `AgentPrismContentProtectionOptions` XML dokümanı, `src/AgentPrism.Core/README.md`. `api/` ve `http-api/` **üretilir** — orada iş XML dokümanıdır
> **Manuel test alanı:** [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](manuel-test/13-KIRACI-VE-GUVENLIK.md) (`SEC` alan kodu)

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
   sed -n '/^## 64.4/,/^## 64.5/p' docs/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md
   ```
   Silme **sevk edildi**; bu faz onu tekrarlamaz.
   🚨 **Faz 79, 80 ve 81 planlandı ama uygulanmadı** — devir notları boştur, okuma.
4. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md) (paylaşılan katman
   kuralları — özellikle "sağlayıcıya özgü ADO.NET tipine başvurma") ·
   [`hafiza/postgresql.md`](hafiza/postgresql.md) (🚨 **ordinal okuma**: `runs`
   ve kardeşleri sabit sütun konumundan okunur) ·
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md)
   (`RunRecording` zinciri ve `secret` filtresi)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI-GUVENLIK.md`](MIMARI-GUVENLIK.md)

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
| [`0001_initial.sql:75`](../src/AgentPrism.PostgreSql/Migrations/0001_initial.sql) `state json NOT NULL` | Oturum durumu açık |
| [`0023_replay_and_branching.sql:33`](../src/AgentPrism.PostgreSql/Migrations/0023_replay_and_branching.sql) `messages json NOT NULL` | Kullanıcının ham istemi açık |
| [`0006_attachments.sql:22`](../src/AgentPrism.PostgreSql/Migrations/0006_attachments.sql) `content bytea` | Yüklenen dosya açık |
| [`security.md:196`](../docs-site/src/content/docs/getting-started/security.md) | Site **on sütunu adıyla** listeliyor ve "Column-level encryption inside AgentPrism is a **known gap, not a shipped feature**" diyor |

> Kanıtlar 2026-08-21 tarihinde doğrulandı.

### 🚨 Planlama sırasında ölçülen beş şey — üçü kaydı çürüttü

| Kayıttaki iddia | Ölçüm | Sonuç |
|---|---|---|
| **F-87:** "Guard model sınırındadır; `RunStarted` ve `run_inputs` ham saklar" | [`RunRecordingAgent.cs:661`](../src/AgentPrism.Core/Recording/RunRecordingAgent.cs#L661) kaydı **guard'dan geçiriyor** (`ContentGuardMessageMasker.PreviewAsync`). Aday, `AgentPrismServiceCollectionExtensions.cs:869`'daki yorumu ters okumuş: yorum "bu satır **olmasaydı**" diyor ve satır **var** | 🚫 **YANLIŞ.** F-87 kapatıldı (👤 kullanıcı kararı, 2026-08-21). Faz 64 ayrıca konu bazlı **silme** sevk ediyor |
| **F-41:** "Bu sınır hiçbir tüketiciye dönük belgede yazmıyor" (B06-1) | `security.md` §"What is stored in the clear" sınırı **ve on sütunun tamamını** yazıyor; kontrol listesinde de satırı var | 🚫 **YANLIŞ.** Dokümantasyon boşluğu kapanmış; kalan iş **yalnız koddur** |
| "Şifreleme dosya aramasını **tamamen bitirir**" | [`SqlAgentFileStore.cs:167-190`](../src/AgentPrism.Sql.Shared/Stores/SqlAgentFileStore.cs#L167) — sunucu regex'i yalnız bir **ön süzgeçtir**; nihai eşleşme **her zaman** .NET `Regex` ile yapılır ve kod zaten ön süzgeçsiz bir yedek yola sahiptir (`IsInvalidRegexError` yakalayınca) | ⚠️ **Fazla güçlü.** Arama bitmez, **ön süzgeci** kaybeder — davranış aynı, maliyet artar (82.3) |
| "Dokuz sütun" | Site ve tarama kaydı **dokuz kalem** sayıyor ama `tool_invocations.arguments/result` iki sütundur → **on sütun**. Ayrıca tarama **iki sütun kaçırmış**: [`0027_pending_approvals.sql:14`](../src/AgentPrism.PostgreSql/Migrations/0027_pending_approvals.sql) `arguments text` (onay bekleyen tool çağrısının argümanları — `tool_invocations.arguments` ile **aynı** veri) ve `agent_skills` kaynak/script içeriği | ✅ Sayı düzeltildi: **on sütun** + iki aday (82.5) |
| **`responses.payload` içerik tutuyor** (site böyle diyor) | `INSERT INTO ... responses` **hiçbir yerde yok** (ölçüldü). Tablo `0001_initial.sql`'de kuruldu ("phase 4 fills them") ama hiçbir `store` ona yazmıyor; yalnız [`DataSubjectTargetRegistry.cs:121`](../src/AgentPrism.Sql.Shared/Internal/DataSubjectTargetRegistry.cs#L121) onu silme hedefi olarak tanıyor | 🚫 **YANLIŞ.** Tablo bugün **boştur**. Sevk edilen `security.md` onu "session state and provider responses" diye sayıyor — bu faz o satırı da düzeltir (82.5) |

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
| `sessions.state` | [`SqlSessionStore.cs:72`](../src/AgentPrism.Sql.Shared/Stores/SqlSessionStore.cs#L72) ve `:98` |
| `conversation_items.item` | [`SqlChatHistoryProvider.cs:166`](../src/AgentPrism.Sql.Shared/Stores/SqlChatHistoryProvider.cs#L166) · [`SqlConversationBranchStore.cs:155`](../src/AgentPrism.Sql.Shared/Stores/SqlConversationBranchStore.cs#L155) |
| `run_inputs.messages` | [`SqlRunInputStore.cs:47`](../src/AgentPrism.Sql.Shared/Stores/SqlRunInputStore.cs#L47) |
| `run_events.text` · `.payload` | [`SqlRunStore.cs:121`](../src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs#L121) ve `:124`; ayrıca `:291` (hata metni) |
| `tool_invocations.arguments` · `.result` | [`SqlRunStore.cs:647`](../src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs#L647) ve `:648` |
| `agent_files.content` | [`SqlAgentFileStore.cs:74`](../src/AgentPrism.Sql.Shared/Stores/SqlAgentFileStore.cs#L74) |
| `attachments.content` | [`SqlAttachmentStore.cs:71`](../src/AgentPrism.Sql.Shared/Stores/SqlAttachmentStore.cs#L71) |
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
| Yeniden oynatma | [`RunReplayService.cs:30`](../src/AgentPrism.Core/Replay/RunReplayService.cs#L30) `IRunInputStore _inputs` | **Etkilenmez.** `IRunInputStore` uygulaması çözülmüş metni döndürür; replay sadıktır |
| Eval terfisi | [`RunToCasePromoter.cs:97`](../src/AgentPrism.Core/Evaluation/RunToCasePromoter.cs#L97) `ListToolInvocationsAsync`, `:175` `ReadEventsAsync` | **Etkilenmez.** İkisi de depo üzerinden okur |
| Dosya araması | [`SqlAgentFileStore.cs:167`](../src/AgentPrism.Sql.Shared/Stores/SqlAgentFileStore.cs#L167) | **Ön süzgeç ölür, arama yaşar.** Sunucu tarafı `~` regex'i şifreli metinle eşleşmez; kod bu durumda dizin kapsamındaki dosyaları çeker, çözer ve .NET `Regex` ile eşler — **zaten var olan yedek yol** |

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

> Kapanışta [`docs/manuel-test/13-KIRACI-VE-GUVENLIK.md`](manuel-test/13-KIRACI-VE-GUVENLIK.md)
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

- [ ] Koruma açıkken `run_inputs.messages` veritabanında **düz metin içermez** — `psql` çıktısı belgeye yazıldı
- [ ] Aynı `run` arayüzde **düz metin** görünür; çözme şeffaftır
- [ ] Koruma açılmadan **önce** yazılmış satırlar açıldıktan **sonra** okunabilir — düşen bir testle önce kanıtlandı
- [ ] Koruma kapatıldıktan sonra şifreli satırlar hâlâ okunabilir
- [ ] `Enabled = false` iken üç sağlayıcının sözleşme seti bugünküyle **birebir** aynı (K1)
- [ ] On sütunun her biri `ContentProtectionContract` ile üç sağlayıcıda yazılıp okundu
- [ ] Bilinmeyen `kid` `AgentPrismException` verir ve mesaj `kid`'i adıyla söyler
- [ ] Yeniden oynatma ve eval terfisi korumalı bir `run` üzerinde çalışır
- [ ] Dosya araması koruma açıkken **tek** sorgu koşar ve doğru sonuç verir
- [ ] `TenantIsolationContract` dört koşumda da yeşil
- [ ] `ProtectedColumnCoverageTests` kapsam listesiyle kodun ayrışmadığını kanıtlar
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü — anahtar **hiçbir dosyada** yok, yalnız `user-secrets`'ta
- [ ] Manuel kabul case'leri `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi — `security.md`'nin "known gap" cümlesi **kaldırıldı**, sınır yeniden yazıldı ve `responses.payload` satırı düzeltildi (tablo boştur); `npm run check` temiz
- [ ] `tuketici-dokuman-senkronu` koşuldu — bu faz sevk edilen bir yüzeye dokunuyor

### Doğrulama komutları

```bash
# Korumasiz yaz, sonra korumayi ac, sonra oku: eski satir okunabilir kalmali.
curl -s -X POST http://localhost:5081/agentprism/api/agents/demo/run \
  -H 'Content-Type: application/json' -d '{"message":"my card is 4111 1111 1111 1111"}'

# Diskte ne duruyor?
psql "$AGENTPRISM_PG" -c "SELECT messages FROM agentprism.run_inputs ORDER BY created_at DESC LIMIT 1;"

# Bilinmeyen kid net hata dondurmeli.
curl -s http://localhost:5081/agentprism/api/runs/<id> | jq '.detail'
```

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

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Her satır: bulgu · seviye
> (🔴/🟡/🟢) · sonuç (düzeltildi / gerekçelendi / F-NN olarak devredildi).
> Bulgu yoksa "🔴 ve 🟡 yok" yazılır; boş bırakılmaz.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

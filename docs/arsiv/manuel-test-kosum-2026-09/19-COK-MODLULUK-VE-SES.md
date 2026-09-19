# 19 — Çok Modluluk: Ek, Ses Tool'ları ve Gerçek Zamanlı Konuşma (`MM`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../19-COK-MODLULUK-VE-SES.md`](../../manuel-test/19-COK-MODLULUK-VE-SES.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz B — sıradaki aile: `13 · 19 · 04 · 18 · 10 · 08`) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `f721b229` donuk |
| **Case sayısı** | 93 (MT-MM-001..122, seyrek numaralı) |
| **Port** | 5081 |
| **Depo** | `mt_s1` PostgreSQL şeması (aile 13'ten devreden test verisi temizlenmedi — bu aile ona dokunmuyor) |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): tüm kimlikler (OpenAI, Google,
Voice/ElevenLabs) tek seferde ortam değişkenine aktarıldı, hiçbir dosyaya/loga
yazılmadı.

**Açılış ölçümü:**

```
GET /api/diagnostics -> persistenceProvider=PostgreSQL, canConnect=true, migrationsUpToDate=true
GET /api/meta -> version 0.0.0-preview.0.789, storage.persistent=true,
  agentDefinitionStore=SqlAgentDefinitionStore, runStore=SqlRunStore
```

---

## Devir notu

**🚨 Kaza — bir komut ElevenLabs anahtarını düz metin gösterdi.** Fiyatlandırma
anahtarının nereden geldiğini araştırırken `dotnet user-secrets list` (JSON
DEĞİL, düz biçim) çalıştırıldı ve çıktı bu ajanın kendi araç transkriptine
gerçek `Tracon:Voice:ApiKey` değerini yazdı (hiçbir dosyaya/proje logına
YAZILMADI, yalnız bu oturumun kendi tool-call kaydında göründü). 00-INDEKS
§2.4 zaten "sağlayıcı anahtarları düz metne çıktı, tur bitince DÖNDÜRÜLMELİ"
notu taşıyor — bu kaza aynı öneriyi bir kez daha doğruluyor, ek bir aksiyon
GEREKTİRMİYOR (anahtar zaten rotasyon listesinde) ama şeffaflık için
kaydediliyor. Bundan sonra bu oturumda yalnız `--json` + Python filtreleme
kullanıldı (değerler asla doğrudan `echo`/`print` edilmedi).

**Oturum 1 — MT-MM-001..055 koşuldu (46 case; 043/046-050/053-055 dahil,
051/052 numarası spesifikasyonda yok).** Ayrıntı case bloklarında.
**Bir yeni kusur açıldı: `HATA-S1-024`** (aşağıda, MT-MM-047 ve MT-MM-049) —
`FunctionInvokingChatClient` tool'lardan fırlatılan `TraconException`'ı
modele ulaştırmadan genel bir mesaja çeviriyor; kapsamı muhtemelen ses
tool'larının ötesine geçiyor, kapanışta SINIF TARAMASI önerildi.

**🚨 Ölçülen ortam tuzağı — DLL doğrudan koşumu `appsettings.json`'ı bulamıyor.**
`dotnet artifacts/bin/Tracon.Api/release/Tracon.Api.dll` çalışma dizini
`ap-s1` kökündeyken başlatılırsa `ContentRootPath` = çağrının yapıldığı dizin
olur (DLL'in bulunduğu dizin DEĞİL), `appsettings.json` hiç yüklenmez ve
`OpenAICompatible:openrouter:Endpoint` gibi yalnız o dosyada tanımlı
varsayılanlar kaybolur — açılış `OptionsValidationException` ile çöker.
Çözüm: `cd artifacts/bin/Tracon.Api/release && dotnet Tracon.Api.dll --urls ...`
(DLL'in KENDİ dizininden çalıştır) YA DA `dotnet run --project samples/Tracon.Api
-c Release --no-build` kullan (bu doğru `ContentRootPath`'i otomatik ayarlar).//
Bu oturum ikinci yöntemi seçti.

**🚨 Ölçülen ortam tuzağı — `dotnet run` Development ortamında paylaşılan
`user-secrets`'ı SESSİZCE okuyor.** `Tracon:ContentProtection:RawKeys:sample`
env değişkeni olarak VERİLMEDİĞİ hâlde `dotnet run` ile başlatılan uygulama
ekleri (`attachments.content`) şifrelerken bu anahtarı bulup kullandı — çünkü
`dotnet run` varsayılan olarak `ASPNETCORE_ENVIRONMENT=Development` seçer ve
.NET, Development ortamında `AddUserSecrets` çağrısını OTOMATİK ekler; bu da
ap-s1 worktree'sinin `samples/Tracon.Api.csproj`'ının ANA repoyla AYNI
`UserSecretsId` taşıdığı için paylaşılan `~/.microsoft/usersecrets/<id>/`
deposunu okur. Skill §1.2 yalnız YAZMAYI yasaklıyor; bu okuma bir ihlal
DEĞİLDİR (yalnız okundu, hiçbir şey yazılmadı) ama gelecekteki bir şeridin
env değişkeniyle EZMEDİĞİ her anahtarın sessizce paylaşılan gerçek değerini
alacağını unutmayın — yalnız açıkça override edilen anahtarlar (OpenAI,
Anthropic, Google, OpenRouter, Voice) izole edilmiştir; `ContentProtection`,
`Pricing`, vb. override edilmeyen her şey paylaşılan depodan sızar. Bu turda
zararsızdı (yalnız okuma, şifreleme anahtarı sızmadı — HTTP yanıtında hiçbir
yerde görünmedi) ama not düşülüyor.

**Oturum 2 — aile KAPANDI: MT-MM-059..122 koşuldu (66 Geçti · 3 Kaldı · 23
Beklemede · 1 Atlandı, toplam 93/93 case hesaba katıldı).** Devralınan
durum stale idi — `DEVIR.md` "19 henüz açılmadı" diyordu ama aslında 3
önceki commit (`aedeac02`, `3c9da77f`, `906cc71b`) MT-MM-001..055'i zaten
bitirmişti; ayrıca yarıda kalmış bir önceki oturumdan DB'de leftover state
bulundu (bkz. MT-MM-067, MT-MM-082) — dosyaya hiçbir kayıp yazılmamıştı,
yalnız uygulama süreci ve birkaç test satırı temizlenip yeniden koşuldu.

**İki yeni kusur açıldı:**
- `HATA-S1-025` (Yüksek) — 30s zaman aşımından SONRA arka planda başarıyla
  biten bir tool çağrısının (`generate_image`, gerçek `gpt-image-1`) gerçek
  çıktısı (ek) doğru kaydediliyor ama `tool_invocations.usage`/`succeeded`
  kalıcı olarak `null`/`false` kalıyor — gerçek sağlayıcı harcaması
  gözlemlenebilirlikten düşüyor. `HATA-S1-024` ailesinden (aynı
  `TraconException` yutulması) ama sonucu daha ağır. Ayrıntı MT-MM-095'te.
  **✅ KAPANDI 2026-09-18 (Aile G)** — teşhis doğruydu ama eksikti: kök neden
  yutulan istisna değil, zaman aşımının **hiçbir şeyi iptal etmemesiydi**
  (K-805). Ayrıca sevk edilen kayıt genel 30 sn varsayılanını miras alıyordu
  (K-807) ve geç biten çağrının harcamasını alacak kimse yoktu (K-806).

**İki spec düzeltmesi yapıldı (doküman kusuru, kod donuk kaldı):**
MT-MM-067 (`X-Tenant-Id` → `X-Tracon-Tenant`, ayrıca tenancy bayrakları)
ve MT-MM-073 (`--no-commit`'in kendi notuyla çelişmesi). Ayrıntı ilgili
case bloklarında.

**Ortam notu:** `Tracon:Tenancy:Enabled`/`AllowHeaderResolution` bu
oturumdan itibaren `ap-s1`'de AÇIK bırakıldı (MT-MM-067'den beri) — sonraki
family'ler (04, 18, 10, 08) bunu bilerek devralmalı; varsayılan davranışı
değiştirmedi (header yoksa yine `default` tenant), yalnız header
resolution'ı etkinleştiriyor.

**Playwright bu oturum boyunca başka bir şeritçe meşguldü** — MT-MM-088,
090, 099 (Playground UI, gerçek mikrofon gerekmez) bu yüzden ertelendi;
tarayıcı boşalınca ayrıca koşulmalı.

**Sıradaki ailenin işi:** `04-COK-AJAN-VE-DEVIR.md` (ya da plandaki
sıradaki dosya) açılmalı — bu dosyanın kalanı (§Fiziksel eylem tablosu) ve
`HATA-S1-025` kapanışta ele alınacak.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 45cfed58:docs/manuel-test/kosumlar/2026-09-16/19-COK-MODLULUK-VE-SES.md
> ```

---

## Temiz geçen case'ler (60)

| Case | Durum | Başlık |
|---|---|---|
| MT-MM-001 | ☑ | Geçerli bir PNG yüklenir |
| MT-MM-002 | ☑ | Yanlış `Content-Type` sihirli bayt tarafından geçersiz kılınır |
| MT-MM-003 | ☑ | Yürütülebilir/tanınmayan içerik reddedilir |
| MT-MM-004 | ☑ | Boş dosya reddedilir |
| MT-MM-005 | ☑ | `MaxBytes` (20 MB) aşımı reddedilir |
| MT-MM-006 | ☑ | MP3 çerçeve senkronu bit maskesiyle tanınır |
| MT-MM-010 | ☑ | İndirme doğru başlıklarla ve bayt-bayt eşleşmeyle döner |
| MT-MM-011 | ☑ | Listeleme `sessionId` ile filtreler; `skip`/`take` sınırlanır |
| MT-MM-012 | ☑ | Silme sonrası indirme VE ikinci silme `404` döner |
| MT-MM-013 | ☑ | Başka kiracının ekine erişilemez |
| MT-MM-020 | ☑ | Ek + mesajla çalıştırma; modele giden içerik `DataContent`'tir |
| MT-MM-021 | ☑ | Var olmayan bir ekle çalıştırma → `400`, akış başlamaz |
| MT-MM-022 | ☑ | Başka kiracının eki çalıştırmada kullanılamaz |
| MT-MM-023 | ☑ | `message` boş ama `attachmentIds` doluysa istek geçerlidir |
| MT-MM-026 | ☑ | `/v1/responses` gömülü `data:` URI'yi eğe çevirir |
| MT-MM-027 | ☑ | `/v1/chat/completions` görsel girdiyi kabul etmez |
| MT-MM-028 | ☑ | Beyaz listede olmayan bir `data:` türü reddedilir |
| MT-MM-031 | ☑ | `Tracon:Voice:ApiKey` yoksa `/api/voice/*` `501`, konuşma ucu `404` döner |
| MT-MM-038 | ☑ | `GET /api/voice/voices` gerçek ses listesini döner |
| MT-MM-039 | ☑ | `GET /api/voice/health` ücret üretmeden sağlığı ölçer |
| MT-MM-040 | ☑ | `POST /api/voice/speak` metni seslendirir, ek üretir, maliyet döner |
| MT-MM-041 | ☑ | `POST /api/voice/speak` boş metinle `400` döner |
| MT-MM-043 | ☑ | `GET /api/voice/sessions` konuşma katmanı kapalıyken boş liste döner, `501` DEĞİL |
| MT-MM-046 | ☑ | `speak` gerçek bir çalıştırmada çağrılır, ek `session_id`'si doludur (G1) |
| MT-MM-048 | ☑ | `transcribe` kayıtlı bir ses ekini metne çevirir |
| MT-MM-053 | ☑ | `speak` tool çağrısı `tool_invocations`'a `usage_unit=characters` ile yazılır |
| MT-MM-054 | ☑ | `transcribe` tool çağrısı `usage_unit=seconds` ile yazılır |
| MT-MM-055 | ☑ | `POST /api/voice/speak` (operatör yolu) `tool_invocations`'a hiç satır yazmaz |
| MT-MM-059 | ☑ | Python WebSocket istemcisini kur |
| MT-MM-062 | ☑ | `UseVoiceConversation()` açıksa ama sağlayıcı yoksa `501`; hiç çağrılmadıysa `404` |
| MT-MM-063 | ☑ | WebSocket olmayan bir isteğe `400` döner |
| MT-MM-064 | ☑ | Token doğruysa alt protokolde KABUL edilir, `ready` çerçevesi gelir |
| MT-MM-065 | ☑ | Token YANLIŞSA el sıkışma REDDEDİLİR |
| MT-MM-071 | ☑ | Her tur normal bir `runs` satırı üretir |
| MT-MM-072 | ☑ | İkinci `start` REDDEDİLİR |
| MT-MM-074 | ☑ | Bilinmeyen `inputFormat` |
| MT-MM-075 | ☑ | Olmayan bir agent adıyla `start` |
| MT-MM-079 | ☑ | `MaxConcurrentConnectionsPerTenant` sınırı |
| MT-MM-080 | ☑ | Ses VARSAYILAN olarak SAKLANMAZ |
| MT-MM-081 | ☑ | `PersistAudio` açıkken YALNIZ agent'ın sesi eke yazılır |
| MT-MM-082 | ☑ | `voice_sessions` kaydı yazılır |
| MT-MM-090 | ☑ | i18n/tema hızlı geçiş kontrolü |
| MT-MM-092 | ☑ | `includeTimestamps: true` karakter hizalaması döner |
| MT-MM-093 | ☑ | Akışlı sentezde `includeTimestamps: true` açıkça reddedilir |
| MT-MM-100 | ☑ | `list_voices` ve `GET /api/voice/voices` attribute taşır |
| MT-MM-101 | ☑ | Sağlayıcı üstverisi `preview_url`/anahtar sızdırmaz |
| MT-MM-102 | ☑ | Model `list_voices` çıktısında gender bilgisini görür |
| MT-MM-103 | ☑ | Ses sağlayıcısı yokken `list_voices` İngilizce mesaj döner |
| MT-MM-105 | ☑ | Kaynak dili kapısı yeşildir |
| MT-MM-106 | ☑ | Ses yolu Native AOT'ta çalışır |
| MT-MM-107 | ☑ | Var olan özel `ISpeechSynthesizer` uygulaması değişmeden derlenir |
| MT-MM-108 | ☑ | `UseOpenAILive()` çağrılmadan canlı oturum ucu 501 döner |
| MT-MM-111 | ☑ | Devredilen iş gerçek bir `runs` satırı üretir |
| MT-MM-112 | ☑ | Konuşma dökümü oturum geçmişinde görünür |
| MT-MM-113 | ☑ | `PersistTranscript=false` iken geçmişe yazılmaz |
| MT-MM-114 | ☑ | Kapanan oturumun kaydı süreyi ve maliyeti taşır |
| MT-MM-116 | ☑ | Hiç bağlanılmayan oturum TTL'de `Abandoned` ile kapanır |
| MT-MM-118 | ☑ | Eşzamanlılık limiti sağlayıcıya gitmeden reddeder |
| MT-MM-119 | ☑ | Seçenek A regresyon çiti |
| MT-MM-120 | ☑ | Görsel üretimi açıkken sağlayıcı kayıtlı değilse uygulama BAŞLAMAZ |

## Ayrıntı taşıyan case'ler (33)

## MT-MM-014 — Oturum silinince ekleri de gider (kaskad)

**Doküman notu:** Ön koşul metni "bir ek yüklenmiş" diyor ama bu tek başına
YETERSİZ — `POST /api/attachments?sessionId=...` bir `Session` KAYDI
OLUŞTURMAZ (yalnız ek tablosuna `session_id` sütunu yazar). İlk denemede
gerçek bir session hiç var olmadığı için `DELETE /api/sessions/musteri-42`
`HTTP: 404` döndü (kusur DEĞİL — `SessionEndpoints.DeleteSessionAsync`
XML dokümanının kendisi "an attachment may be uploaded before any session
exists" diyor, yani bu 404 TASARLANMIŞ davranış). Case'i anlamlı koşmak için
önce `POST /api/agents/support/run` ile gerçek bir oturum açıldı
(`sessionId: musteri-42`, gerçek OpenAI çağrısı), SONRA ek yüklendi.

**Gerçek sonuç**
Session gerçekten var olduktan sonra: ek yükleme `201`, silme `204`, silme
sonrası `GET /api/attachments?sessionId=musteri-42` `0` döndürdü — kaskad
uygulama katmanında çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-032 — `pcm_*`/`ulaw_*`/`alaw_*` çıktı biçimleri açılışta reddedilir

**Doküman düzeltmesi:** Bkz. spesifikasyondaki not — beklenen mesaj metni
Türkçe yazılmıştı, gerçek metin İngilizce'dir (K-228); spesifikasyon
düzeltildi.

**Gerçek sonuç**
`Tracon__Voice__OutputFormat=pcm_16000` ile başlatma denemesi
`OptionsValidationException` ile çöktü: `"VoiceOptions: format 'pcm_16000'
cannot be stored as an attachment. ..."` — beklenen davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-033 — Tanınmayan sağlayıcı adı açılışta reddedilir

**Doküman düzeltmesi:** Bkz. spesifikasyondaki not — mesaj metni İngilizce
olarak düzeltildi (K-228).

**Gerçek sonuç**
`Tracon__Voice__Provider=azure-cognitive-speech` ile başlatma denemesi çöktü:
`"VoiceOptions: provider 'azure-cognitive-speech' is not recognized.
Built-in provider: 'elevenlabs'. ..."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-034 — `MaxConcurrentRequests` sıfır veya negatif açılışta reddedilir

**Doküman düzeltmesi:** Bkz. spesifikasyondaki not — mesaj metni İngilizce
olarak düzeltildi (K-228).

**Gerçek sonuç**
`Tracon__Voice__MaxConcurrentRequests=0` ile başlatma denemesi çöktü:
`"VoiceOptions: concurrent request limit must be greater than zero."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-035 — Hatalı bir API anahtarıyla hata mesajı anahtarı sızdırmaz

**Doküman düzeltmesi:** Bkz. spesifikasyondaki not — ipucu metni İngilizce
olarak düzeltildi (K-228).

**Gerçek sonuç**
`Tracon__Voice__ApiKey="SAHTE-GECERSIZ-ANAHTAR-xyz789"` ile açılış BAŞARILI
oldu (yalnız boşluk denetleniyor). `GET /api/voice/health`:
`isHealthy:false`, `detail:"Voice list could not be retrieved: HTTP 401. The
API key is invalid."`. Sahte anahtar metni (`SAHTE-GECERSIZ-ANAHTAR-xyz789`)
ne yanıtta ne uygulama log dosyasında (`grep -c` → `0`) göründü. Gerçek
anahtar geri yüklenip yeniden başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-042 — `POST /api/voice/speak` `MaxCharactersPerRequest` sınırını yerel olarak denetler

**Gerçek sonuç**
6000 karakterlik metin `HTTP: 400`, başlık "Text too long", detay
`"The text is 6000 characters; the limit is 5000. ..."` — istek ElevenLabs'e
GİTMEDEN reddedildi (400, 502 değil). Düzeltilen kusur hâlâ düzeltilmiş
durumda, regresyon yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-047 — `speak` — `MaxCharactersPerRequest` aşımı tool içinde hata döner

**Gerçek sonuç — 🚨 KUSUR (`HATA-S1-024`, ayrıntı aşağıda).** Model, 6000
karakterlik istemi HER SEFERİNDE (üç farklı deneme, üç farklı istem
biçimiyle) kendiliğinden ~1100 karaktere kısaltarak `speak` tool'unu çağırdı
— sınırı hiç aşmadı, bu yüzden case'in ASIL senaryosu (tool'un limit hatası
DÖNMESİ) doğrudan gözlenemedi. Kaynak okumasıyla (`SpeakTool.cs`,
`TranscribeTool.cs`) ve MT-MM-049'un ampirik sonucuyla doğrulandı ki: tool
`TraconException` FIRLATTIĞINDA, MAF'ın `FunctionInvokingChatClient`'ı bu
istisnayı YUTAR ve modele yalnız genel `"Error: Function failed."` metnini
döndürür — spec'in beklediği `"Metin 6000 karakter; sinir 5000..."` açıklayıcı
metni MODELE HİÇBİR ZAMAN ULAŞMAZ. Bu, Tracon'in kendi kod tabanında ZATEN
ÖLÇÜLMÜŞ bir davranıştır (`src/Tracon.Core/Replay/RecordedToolPlayback.cs`
satır ~133: *"`FunctionInvokingChatClient` CATCHES an exception coming out
of a tool body, turns the error into a tool result, ... this was
measured."*) — ekip bunu farklı bir özellik (`ReplayMismatchGuard`) için
zaten telafi ediyor ama `SpeakTool`/`TranscribeTool` bu telafiyi
kullanmıyor. 3 gerçek ElevenLabs çağrısı bu araştırma sırasında yapıldı
(~1100 karakterlik sesler; kredi harcandı, geri alınamaz).

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

### HATA-S1-024 — Tool içi `TraconException` mesajı modele hiç ulaşmıyor (`FunctionInvokingChatClient` genel hataya çeviriyor)

- **Case:** MT-MM-047, MT-MM-049 (aynı kök neden; sınıf muhtemelen `throw new
  TraconException(...)` kullanan HER Tracon tool'unu etkiler — yalnız ses
  tool'larıyla sınırlı değil, bkz. Kapsam)
- **Önem:** Yüksek (genişlik nedeniyle — tek bir davranış değil, muhtemelen
  düzinelerce tool'un hata yolunu etkileyen bir ÖRÜNTÜ)
- **İzlek:** B (gerçek OpenAI + gerçek ElevenLabs çağrısıyla ampirik olarak
  gözlendi)
- **Ortam:** macOS arm64 · net10 · PostgreSQL · OpenAI (`gpt-5.4-mini`) +
  ElevenLabs

**Beklenen**
`SpeakTool`/`TranscribeTool` içinde fırlatılan `TraconException`'ın
`.Message`'ı, tool çağrısının SONUCU olarak modele (ve dolayısıyla son
kullanıcıya) ulaşmalı — spec bunu MT-MM-047/049'da açıkça vaat ediyor
(`"Metin X karakter; sinir Y..."`, `"'...' bir ses dosyasi degil..."`).

**Gerçekleşen**
Model, tool sonucu olarak yalnız `"Error: Function failed."` genel metnini
görüyor — açıklayıcı mesajın hiçbir parçası ulaşmıyor. `FunctionInvokingChatClient`
varsayılan olarak `IncludeDetailedErrors=false` davranışıyla çalışıyor ve
Tracon bunu HİÇBİR yerde açıkça `true` yapmıyor (`grep -rn
"IncludeDetailedErrors" src/` boş döner). Bu davranış Tracon'in kendi
`RecordedToolPlayback.cs` yorumunda "bu ölçüldü" diye zaten belgeleniyor —
ama yalnız `ReplayMismatchGuard` bunu telafi ediyor, sıradan tool'lar
etmiyor.

**Yeniden üretme**
1. `voice-assistant` agent'ına ses eki OLMAYAN bir attachmentId ile
   `transcribe` tool'unu tetikleyen bir istem gönder (MT-MM-049 adımları).
2. Akıştaki `functionResult` içeriğini oku.

**Kanıt**
- MT-MM-049 çalıştırması: `"result": "Error: Function failed."` (spec'in
  beklediği `"...is not an audio file (type: image/png)."` yerine).
- Kaynak: `src/Tracon.Voice/Tools/TranscribeTool.cs:75-76`,
  `src/Tracon.Voice/Tools/SpeakTool.cs:69-71` (her ikisi de `throw new
  TraconException`).
- Kaynak: `src/Tracon.Core/Replay/RecordedToolPlayback.cs:133-137` (davranışın
  ekip tarafından önceden ölçüldüğüne dair yorum).

**Kapsam**
Yalnız bu iki case değil — kod tabanında `throw new TraconException` deseni
KULLANAN her `AIFunction`/Tracon tool'u (grep: onlarca sonuç, ör. güvenlik/
onay/skill tool'ları) muhtemelen aynı sessiz yutmaya maruz. Kapanış
oturumunda bir SINIF TARAMASI (`grep -rn "throw new TraconException" src/**/Tools`
+ her birinin `FunctionInvokingChatClient` üzerinden gerçekten çağrıldığı
doğrulanarak) önerilir. Olası düzeltme yönleri: (a) tool taban sınıfında
(`VoiceToolBase` ve benzerleri) `TraconException`'ı YAKALAYIP açıklayıcı bir
STRING dönmek (throw etmemek), (b) `AIAgent`/`ChatClientAgentOptions`
üzerinden `IncludeDetailedErrors=true` açmak (ama bu TÜM istisna türlerinin
detayını sızdırır — güvenlik açısından daha riskli), ya da (c) her tool'un
kendi `catch (TraconException ex) { return ex.Message; }` desenini
benimsemesi. Karar kapanış oturumuna bırakıldı.

---

### ✅ KAPANDI — 2026-09-18 (Aşama 2, Aile C)

**Sınıf taraması ölçüldü: 17 çalışma-anı `throw`, beş tipte** —
`TranscribeTool` (4) · `SpeakTool` (2) · `VoiceToolBase` (1) ·
`GenerateImageTool` (9) · `ValidatingAIFunction` (1). Hepsi modele aynı altı
kelimeyle gidiyordu. En ağırı `ValidatingAIFunction`: argüman reddinin
gerekçesi modele ulaşmadığı için model kendini düzeltemiyor, yani aynı hatalı
çağrıyı tekrarlıyor.

**Kullanıcı kararı 👤 (2026-09-18): seçenek (a), genelleştirilmiş.**
`ToolWrapperChain.Compose` **her tool kaynağına ulaşan tek birleştirme
noktasıdır** (kod tool'ları ve MCP kiracı tool'ları), bu yüzden düzeltme tek
bir yerde duruyor ve yeni tool'lar otomatik kapsanıyor.

**Düzeltme.** `ExplainedFailureAIFunction` zincire **en dış katman** olarak
eklendi. Yalnız `TraconException` yakalanır ve `.Message` tool sonucu olarak
döndürülür. En dışta olması altındaki her katmanı kapsar: argüman doğrulama,
yetkilendirme, zaman aşımı ve tool'un kendi gövdesi.

**Seçenek (b) reddedildi.** `IncludeDetailedErrors=true` **her** istisna
türünün detayını modele sızdırır — sağlayıcı SDK mesajları, yığın izleri
dahil. K-059'un ruhuna aykırı. Katmanın dar olması bilinçlidir ve testle
kilitli: `InvalidOperationException` ve `OperationCanceledException` dokunulmadan
geçmeye devam ediyor.

🚨 **İlk düzeltme bir gerileme üretti ve kapılar onu yakaladı.** Katmanın ilk
hâlinin doküman yorumu "run kaydı çağrıyı yine başarısız gösterir, alttaki
katmanlar kendi olaylarını zaten yazdı" diyordu. **Bu iddia yanlıştı.** Kayıt
ve olay tipi `FunctionResultContent.Exception`'ı okuyor; istisnayı yutunca:

- `ToolInvocationRecord.TimedOut` **false** oldu (zaman aşımına uğrayan çağrı
  başarılı göründü),
- `Error` **null** oldu, yani `Succeeded` true,
- olay `ToolFailed` yerine `ToolInvoked` yazıldı.

Üç işlevsel test bunu yakaladı (`ConcurrentToolInvocationTests`,
`ToolGovernanceEndpointTests` ×2). **Testler zayıflatılmadı** — kod düzeltildi.

**Çözüm kod tabanının kendi emsalini izliyor.** `AuthorizingAIFunction` da bir
reddi normal bir sonuç olarak döndürüyor ve tam bu sorunu
`ToolAuthorizationAccumulator` ile çözmüş: işaret, sonucun yanında çağrı
kimliğine göre taşınıyor. `ToolExplainedFailureAccumulator` aynı deseni
uyguluyor, ama bayrak değil **istisnanın kendisini** taşıyor — kayıt hem
metnini istiyor hem de bir zaman aşımının zaman aşımı olarak tanınabilir
kalması gerekiyor.

Ek olarak olay tipi artık `record.Succeeded`'dan okunuyor, `result.Exception`'dan
değil: tek kaynak.

**Tip `internal` yapıldı.** Zincirdeki public kardeşlerinden farklı olarak
tüketici onu ne kurar ne yapılandırır. `public-surface-baseline.txt` "sayı
yalnız küçülebilir; büyüme bilinçli bir eklemedir" diyor — kimsenin
adresleyemediği bir katman için paket yüzeyini büyütmek karşılıksız bir
maliyet olurdu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-049 — `transcribe` — ses OLMAYAN bir ekle çağrılırsa hata döner

**Gerçek sonuç — aynı kök neden, `HATA-S1-024`'e bağlı.** Tool DOĞRU şekilde
işlemi durdurdu (PNG'yi transcribe ETMEDİ, `usage_unit` boş kaldı — yan etki
yok) ama modele dönen mesaj spec'in beklediği `"'...' bir ses dosyasi
degil..."` yerine yalnız genel `"Error: Function failed."` oldu. Fonksiyonel
güvenlik korunuyor (yanlış türde dosya işlenmiyor), yalnız hata mesajının
açıklayıcılığı kayboluyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı (bkz. `HATA-S1-024`)

---

**Yeniden koşum — 2026-09-19 (kapanış, Aile C sonrası) · ☑ GEÇTİ**

Gerçek OpenAI, `mt_z` şeması, yeni yüklenen PNG
(`01a0b70c-1adf-7be3-b4a7-61c2a2e9e7b3`, `image/png`, 69 B).

Model `transcribe`'ı çağırdı ve tool sonucu artık **özgül** hatayı taşıyor —
turun ölçtüğü jenerik `Error: Function failed.` gitti:

```json
{ "$type": "functionResult",
  "result": "The attachment with id '01a0b70c-1adf-7be3-b4a7-61c2a2e9e7b3'
             is not an audio file (type: image/png).",
  "callId": "call_KWYNPJIGFZ8ADXl3a6YwWR25" }
```

Mesaj modele ulaştığı için model de kullanıcıya **nedeni** söyleyebildi:

```
Bu ek bir ses dosyası değil; `image/png` olarak görünüyor. Bu yüzden
`transcribe` ile metne çevrilemez.
```

Turun ölçtüğü fonksiyonel güvenlik korundu: PNG transcribe **edilmedi**.

⚠️ **Spec'in beklenen metni bayattı ve düzeltildi** (skill §1.1 istisnası):
satır Türkçe yazılmıştı, sevk edilen metin İngilizce'dir (K-228).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-050 — `list_voices` ücret üretmeden sesleri listeler

**Gerçek sonuç**
Yanıt `list_voices` tool çağrısı içerdi; sonuç `"Ad (kimlik) — kategori —
cinsiyet"` biçiminde 21 satırdır (`Ad (kimlik)` çekirdeği spec ile eşleşiyor;
ek `— premade — male` gibi bir son ek Faz 138'in `attributes` zenginleştirmesi
— bkz. MT-MM-100/102, kusur değil, daha sonraki bir geliştirme). 21 ses
`MaxListedVoices=50` altında olduğu için kısaltma satırı görünmedi (bu hesapta
50'den fazla ses yok — MT-MM-104 bu senaryoyu gerçek sağlayıcıyla test
edemez, otomatik teste bırakılmalı). ElevenLabs panelinde bu çağrı ayrı bir
karakter/dakika tüketimi YARATMADI (`GET /v2/voices`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-066 — 🚨 Token SORGU DİZESİNDE gönderilirse KABUL EDİLMEZ

**Gerçek sonuç**
`--send-token-in-query --no-token` ile token yalnız `?token=...` sorgu
dizesindeyken alt protokol listesi yalnız `tracon.voice.v1` kaldı. Bağlantı
`HTTP 401` ile REDDEDİLDİ — sunucu sorgu dizesini okumuyor, güvenlik sınırı
doğru.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-067 — Başka kiracının oturumuna bağlanmak o kaydı GÖRMEZ; kendi kiracısında taze bir oturum açılır

**Gerçek sonuç — spec'te iki bayat ayrıntı düzeltildi (gerekçe spec dosyasında).**
İlk deneme `X-Tenant-Id` başlığıyla (spec'in yazdığı gibi) çalıştırıldı ve
kiracı ayrımı gözlenmedi — `POST .../support/run` bile `X-Tenant-Id:
kiraci-alfa` ile çağrılınca satır `tenant_id='default'` olarak yazıldı.
Kaynağa bakıldı: `TraconTenancyOptions.HeaderName` varsayılanı
`X-Tracon-Tenant`'tır (`src/Tracon.AspNetCore/.../HttpTenantContext.cs`),
`X-Tenant-Id` DEĞİL — dosya 13'ün 21 case'i zaten doğru başlığı kullanıyor,
yalnız bu case'in metni bayat kalmış. Ayrıca çalışan `ap-s1` süreci
`Tracon:Tenancy:Enabled`/`AllowHeaderResolution` OLMADAN başlatılmıştı
(varsayılan kapalı); uygulama bu iki bayrakla yeniden başlatıldı (env
değişkeni, `user-secrets`'a yazılmadı — skill §1.2). Düzeltilen başlıkla
YENİDEN koşuldu:
- Ön koşul: `X-Tracon-Tenant: kiraci-alfa` ile `POST .../support/run` →
  DB'de `tenant_id='kiraci-alfa', agent_name='support'` (doğrulandı,
  `psql`).
- WS bağlantısı (varsayılan kiracı, başlık YOK) → `ready` çerçevesi hemen
  geldi, tam beklendiği gibi.
- `GET /api/sessions/paylasilan-oturum-id` `X-Tracon-Tenant: kiraci-alfa`
  ile → `tenantId=kiraci-alfa, agentName=support, msgCount=2` (yalnız
  "merhaba" turu — WS bağlantısı bu kayda HİÇ dokunmadı).
- DB'de iki AYRI satır doğrulandı: `(id, tenant='kiraci-alfa',
  agent='support')` ve `(id, tenant='default', agent='voice-assistant')` —
  WS bağlantısı kendi kiracısında TAZE bir oturum açtı, spec'in vaat ettiği
  tam olarak bu.
- 🚨 Bu case'in kendi ölçümü sırasında ayrıca `~1 saat 20 dakika önceden
  kalma bir DB satırı` bulundu (`tenant_id='kiraci-alfa'`, `created_at`
  bu oturumdan çok önce) — önceki bir oturumun bu case'in ön koşulunu
  koşup yarıda kaldığının kanıtı; iş kaybı yok (yalnız DB state, dosyaya
  hiçbir şey yazılmamıştı), temizlenip yeniden koşuldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-070 — Uçtan uca bir tur

**Gerçek sonuç**
Sıra: `ready` → `transcript` (`final:true`) → `runStarted` → `text` deltaları
→ `audioStart` → ikili çerçeveler → `audioEnd` → (ikinci tur `text` +
`audioStart`/`audioEnd` — model yanıtı iki cümleye bölündüğü için `speak`
iki kez tetiklendi, sıra bozulmadı) → `done` (`cancelled:false, turn:1`).
`transcript.text` beklendiği gibi anlamsız (`"[tone]"`) — kusur değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-073 — Ses göndermeden `commit`

**Gerçek sonuç — spec'in örnek komutu düzeltildi (gerekçe spec dosyasında).**
Spec'in verdiği `--no-commit` bayrağıyla istemci commit'i HİÇ göndermiyordu
(notla çelişiyordu); bayraksız yeniden koşuldu. `ready` sonrası `{"type":
"idle"}` geldi (Listening durumu), ardından hiçbir `transcript`/
`runStarted`/`done` gelmeden 8 saniyede zaman aşımına uğradı — beklenen
davranış tam olarak bu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-078 — Kesinti (`cancel`) çalıştırmayı `Canceled` yapar

**Gerçek sonuç**
İki deneme yapıldı çünkü sonuç yanıt UZUNLUĞUNA bağlı: `--cancel-on-audio-start`
cancel'ı İLK `audioStart`ta gönderiyor, ve kısa bir tek-cümlelik yanıtta metin
akışı `audioStart`tan ÖNCE zaten bitiyor — bu durumda çalıştırma zaten
`Completed` olarak bitmiş oluyor (cancel'ın iptal edecek bir şeyi kalmıyor).
- **1. deneme** (`manuel-ws-kesinti-1`, kısa yanıt "How can I help?"): metin
  akışı `audioStart`tan önce tamamlanmış, `runs.status=1` (Completed) kaldı —
  bu bir KUSUR DEĞİL, `RespondAsync`'in `RunStreamingAsync` döngüsü zaten
  bitmiş demek (kaynak: `VoiceConversationDriver.cs:645-671`). Geçmişe yine
  de kesinti notu eklendi (WS katmanı doğru davrandı), yalnız SQL beklentisi
  bu senaryoda karşılanamaz.
- **2. deneme** (`manuel-ws-kesinti-2`, metin akışı `audioStart` sırasında
  HÂLÂ sürüyordu — son delta "Please" idi, cümle yarım kaldı): `runs.status=3`
  (Canceled) — TAM beklenen. `done` çerçevesi `cancelled:true`. Geçmişteki
  son mesaj: `"I couldn't transcribe that audio. Please\n\n[The response was
  interrupted by the user.]"` — spec'in beklediği dizgi TÜRKÇE
  (`[Yanit kullanici tarafindan kesildi.]`), gerçek İNGİLİZCE; bu K-228'in
  bilinen bayat-Türkçe-beklenti örüntüsü, yeni bir kusur değil.
- **Sonuç:** ürün davranışı doğru; case'in `--cancel-on-audio-start`
  tetikleyicisi kısa yanıtlarda flaky olabilir, spec'e bir not eklenebilir
  (kapanışta değerlendirilebilir, kod donuk olduğu için burada düzeltilmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-083 — Ham PCM çözüme WAV başlığıyla gider

**Gerçek sonuç (izlek C)**
`VoiceUtteranceBufferTests` sınıfı `tests/Tracon.Core.UnitTests/Voice/`
altında mevcut. `dotnet test ... --filter "FullyQualifiedName~..."` çalıştı
ama 🚨 **filtre bu ortamda YOK SAYILDI** (`Microsoft.Testing.Platform`
uyarısı: "VSTest-specific properties are set but will be ignored" —
`VSTestTestCaseFilter` artık desteklenmiyor) — bu yüzden TÜM
`Tracon.Core.UnitTests` paketi koştu: **2805/2805 geçti, 0 başarısız**
(5.6s). Hedef sınıf bu kümenin içinde ve set genelinde hiçbir başarısızlık
yok, dolayısıyla case'in bar'ı karşılanıyor; filtre sözdiziminin bu test
runner'ında değiştiği ayrı bir tooling notu (kapanışta spec'e eklenebilir).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-086 — Canlı transkript/altyazı arayüzde akar

**Gerçek sonuç**
Gerçek mikrofon/konuşma gerektiriyor — bkz. §4.3 fiziksel eylem tablosu.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 9) · ☐ AÇIK KALIYOR (§5c)**
🚨 **ÖLÇÜLDÜ: konuşma paneli sentetik ses AKIŞINI KABUL ETMİYOR.**
MT-MM-110…118'i açan yordam (mikrofonu kendi
`MediaStreamAudioDestinationNode`'umuza bağlayıp sunucunun TTS'ini içine
çalmak) burada **çalışmıyor**, ve sebebi kesin ölçüldü. WebSocket'in
`send` çağrısı sarıldı:

```
gonderilen cerceve: 2      (yalniz 'start' + 'commit' kontrol mesaji)
gonderilen bayt:   85
gelen:             ready, idle
ses parcasi:       0        ← HIC SES GONDERILMEDI
```

Fark şu: canlı ses yolu parçayı **WebRTC'ye doğrudan** veriyor ve orada
sentetik akış sorunsuz taşındı (sağlayıcı *"order four four two"* diye
transkript etti). Konuşma paneli ise `MediaRecorder` kullanıyor ve bu
tarayıcıda `MediaRecorder` sentetik bir `MediaStream`'den **veri üretmiyor**.
Bu bir Tracon kusuru değil, tarayıcı sınırıdır.

∴ case gerçek bir insanın mikrofona konuşmasını ister — §5(c). `00-INDEKS.md`
açık kalem tablosuna taşınır.

💡 **Aynı panelin üç case'i bu turda ÖLÇÜLEBİLDİ** (`MT-MM-088` · `090` · `099`)
— çünkü hiçbiri konuşma **içeriği** gerektirmiyor. Sınır yalnız
"gerçek cümle söyle" diyen case'leri vuruyor.

Panel açıldı, `listening` durumuna geçti ve `Send now` çalışıyor — ama
transkript alanı doldurulamadığı için "canlı transkript dolar, ardından yanıt
altyazı olarak akar" iddiası ölçülemedi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-087 — "Interrupt" düğmesi

**Gerçek sonuç**
Gerçek mikrofon/konuşma gerektiriyor — bkz. §4.3 fiziksel eylem tablosu.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 9) · ☐ AÇIK KALIYOR (§5c)**
🚨 **ÖLÇÜLDÜ: konuşma paneli sentetik ses AKIŞINI KABUL ETMİYOR.**
MT-MM-110…118'i açan yordam (mikrofonu kendi
`MediaStreamAudioDestinationNode`'umuza bağlayıp sunucunun TTS'ini içine
çalmak) burada **çalışmıyor**, ve sebebi kesin ölçüldü. WebSocket'in
`send` çağrısı sarıldı:

```
gonderilen cerceve: 2      (yalniz 'start' + 'commit' kontrol mesaji)
gonderilen bayt:   85
gelen:             ready, idle
ses parcasi:       0        ← HIC SES GONDERILMEDI
```

Fark şu: canlı ses yolu parçayı **WebRTC'ye doğrudan** veriyor ve orada
sentetik akış sorunsuz taşındı (sağlayıcı *"order four four two"* diye
transkript etti). Konuşma paneli ise `MediaRecorder` kullanıyor ve bu
tarayıcıda `MediaRecorder` sentetik bir `MediaStream`'den **veri üretmiyor**.
Bu bir Tracon kusuru değil, tarayıcı sınırıdır.

∴ case gerçek bir insanın mikrofona konuşmasını ister — §5(c). `00-INDEKS.md`
açık kalem tablosuna taşınır.

💡 **Aynı panelin üç case'i bu turda ÖLÇÜLEBİLDİ** (`MT-MM-088` · `090` · `099`)
— çünkü hiçbiri konuşma **içeriği** gerektirmiyor. Sınır yalnız
"gerçek cümle söyle" diyen case'leri vuruyor.

Ön koşulu MT-MM-086'dır (*"agent şu anda sesli yanıt veriyor"*). O tur
üretilemediği için "Interrupt" düğmesi hiç görünmedi ve kesinti ölçülemedi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-088 — `persistAudio` açıkken görünür bir rozet belirir

**Gerçek sonuç**
Playwright tarayıcısı bu oturum sırasında BAŞKA BİR ŞERİDİN kullanımındaydı
(`Browser is already in use for .../mcp-chrome-3eca5a9`) — paylaşılan
kaynağa dokunulmadı, case ertelendi. Not: koşulacaksa önce uygulama
`Tracon:Voice:Conversation:PersistAudio=true` ile başlatılmalı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 2) · ☑ GEÇTİ**

🚨 **§5(b)'nin "LAN arayüzünde dinlenmiyordu" teşhisi YANLIŞTI.** Kaydın
kendisi doğru sebebi yazıyor: tarayıcı kilidi. Kilit kalkınca case
koştu; hiçbir ağ yapılandırması değişmedi.

Uygulama `--Tracon:Voice:Conversation:PersistAudio=true` ile başlatıldı,
Playground → `Voice Assistant` → Conversation mode → Talk. Rozet belirdi:

```
[data-testid="voice-recording-notice"]  →  "Audio of the reply is being stored"
```

**Negatif kontrol de koşuldu** — case'in asıl iddiası budur. Aynı adımlar,
bayrak **kapalı**: oturum yine canlı (`listening` · `End conversation`
görünüyor) ama rozet **yok** (`count: 0`, metin hiç geçmiyor). Yani rozet
oturumun kendisine değil ayara bağlı.

💡 **Rozet sunucu olayından besleniyor, istemci ayarından değil**
(`voice-panel.tsx:147` — `setPersistAudio(event.persistAudio === true)`),
∴ canlı bir oturum açılmadan görülemez. Panel de `play.conversation &&
play.sessionId !== null` koşuluna bağlıdır: yalnız "Conversation mode"
düğmesine basmak yetmez, `voice-toggle` ("Talk") ile oturum **başlatılmalıdır**.

⚠️ Bu makinede gerçek mikrofon erişilebilir çıktı
(`getUserMedia` → *"Default - MacBook Pro Microphone (Built-in)"*, 4 giriş
cihazı). §5(c)'nin ses case'leri (`MT-MM-086` · `087` · `110` · `111`) bu
yüzden **yeniden değerlendirilmelidir** — "mikrofon yok" varsayımı bu ortamda
doğru değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-089 — Güvenli bağlam yoksa panel açılmaz

**Gerçek sonuç**
`curl --max-time 5 http://192.168.1.103:5081/tracon/api/diagnostics` →
bağlantı tamamen REDDEDİLDİ (exit 7, `http_code=000`) — HTTP 403 bile
gelmedi, çünkü şerit Kestrel'i yalnız `http://localhost:5081`'e bağlanacak
şekilde başlatıldı (LAN arayüzünde HİÇ dinlemiyor). Bu, case'in kendi
kaçış kapısını tetikliyor: "AllowRemoteAccess kapalıysa..." — burada
"kapalı"nın ötesinde, arayüz hiç bağlı değil. Gerçek bir tarayıcı denemesi
zaten aynı sonuca (ulaşılamama) varır.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı — LAN arayüzünde dinlenmiyor, backend hiç ulaşılamıyor (aynı sonuç: erişim yok)

## MT-MM-091 — `includeTimestamps` verilmeden bugünkü yanıtla birebir aynıdır

**Gerçek sonuç**
`HTTP: 200`. `alignment: null` (dolu liste değil). `attachment.mediaType:
"audio/mpeg"`, `characters:6`, `isEstimated:false`, `cost:0.00066` — MT-MM-040
ile aynı şekilde davranıyor. 🚨 Gözlem (kusur DEĞİL, kaynakla doğrulandı):
`characters` alanı metnin GERÇEK uzunluğuyla eşleşmiyor ("Zaman damgasiz
sentez." = 22 karakter → `characters:6`; MT-MM-040'ta da aynı örüntü, 34
karakterlik metin → `characters:9`). Kaynak: `ElevenLabsSpeechClient.cs:110`
`ReadBilledCharacters(response)` — değer ElevenLabs'in kendi
`character-cost`/`x-character-cost` yanıt başlığından okunuyor (yalnız
başlık YOKSA `request.Text.Length`'e düşüyor); Tracon burada sağlayıcının
GERÇEK faturaladığı değeri aynen yansıtıyor (K-032'nin "hesap uydurulmaz"
ilkesiyle tutarlı) — düşük görünen sayı ElevenLabs'in kendi ücretlendirme
biriminden kaynaklanıyor, Tracon kusuru değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-094 — Hizalama istemek ayrı bir fatura birimi DEĞİLDİR

**Gerçek sonuç — spec'in ön koşulu düzeltilerek koşuldu.** MT-MM-091'in kendi
örnek metni ("Zaman damgasiz sentez.") MT-MM-092'nin metniyle ("Merhaba")
AYNI DEĞİLDİ — bu case'in ön koşulu "aynı metinle koşuldu" diyor ama iki
case'in kendi örnek komutları farklı metin taşıyor (spec'in kendi içindeki
tutarsızlığı). MT-MM-091 "Merhaba" ile TEKRAR koşuldu
(`manuel-mm-timestamps-off-2`): `characters:2, cost:0.00022` —
MT-MM-092'nin (`characters:2, cost:0.00022`) ile BİREBİR AYNI. Hizalama
istemek maliyeti/karakter sayısını değiştirmiyor, tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-095 — `generate_image` yalnız ek kimliği döndürür — 🚨 KUSUR (`HATA-S1-025`)

**Gerçek sonuç — spec'in kendi bar'ı karşılandı AMA yeni bir kusur bulundu.**
Uygulama `Tracon:Images:Enabled=true`, `:Provider=openai`,
`:Model=gpt-image-1` ile yeniden başlatıldı. `generate_image` taşıyan bir
agent oluşturuldu, istem gönderildi. Model tool'u İKİ KEZ çağırdı, ikisi de
modele `"Error: Function failed."` olarak döndü — AMA `GET
/api/attachments?sessionId=...` TAM 2 görsel ek listeledi (`image/png`,
1024x1024, indirilip `file` ile doğrulandı — gerçek, açılabilir görsel).
Kök neden günlükte: `Tracon.TraconToolTimeoutException: Tool
'generate_image' did not complete within 30s.` — `gpt-image-1` üretimi
30 saniyeden uzun sürüyor (gerçek süre ~30-35s), sabit tool timeout'u
aşılıyor. `TimeoutAIFunction.cs`'in kendi yorumu bunu BİLİNÇLİ bir sınır
olarak tanımlıyor ("the call keeps running in the background... This is a
documented limit, not a bug") — arka plan çağrısı gerçekten TAMAMLANDI ve
ek DOĞRU şekilde `SessionId`/`RunId` ile kaydedildi. Ancak `GET
/api/runs/.../tools` sorgulandığında HER İKİ `generate_image` satırı da
`succeeded:false, timedOut:true, usage:null, result:null` — arka plan
görevi başarıyla bitmesine rağmen `tool_invocations` satırı GÜNCELLENMİYOR.
**Sonuç: gerçek sağlayıcı harcaması (görsel üretildi, muhtemelen faturalı)
kalıcı olarak GÖZLEMLENEBİLİRLİKTEN düşüyor** — K-032'nin "hesap asla
uydurulmaz" ilkesi burada tersten ihlal ediliyor: hesap uydurulmuyor ama
GERÇEK bir hesap sessizce kayboluyor. Bu, `HATA-S1-024` ile aynı ailede
(`TraconException` yutulması) ama SONUCU farklı ve daha ağır (mesaj
netliği değil, maliyet muhasebesi kaybı) — ayrı bir kayıt açıldı.

**HATA-S1-025 — Zaman aşımından SONRA başarıyla biten tool çağrısının kullanım/maliyeti kalıcı olarak kayboluyor**
- **Case:** MT-MM-095 (muhtemelen 30s+ süren HER tool — yalnız
  `generate_image`'e özgü değil, `TimeoutAIFunction` her tool'a sarılıyor)
- **Önem:** Yüksek (finansal gözlemlenebilirlik — gerçek harcama izlenemez
  hâle geliyor)
- **İzlek:** B (gerçek OpenAI `gpt-image-1` çağrısıyla ampirik olarak
  gözlendi, 2/2 çağrıda tekrarlandı)
- **Ortam:** macOS arm64 · net10 · PostgreSQL · OpenAI (`gpt-image-1`,
  `Tracon:Images:Enabled=true`)

**Beklenen**
Tool zaman aşımından sonra arka planda başarıyla biterse (ek kaydedilir),
bu başarı `tool_invocations` kaydına da yansımalı — en azından `usage`/
`result` alanları arka plandaki gerçek sonuçla GÜNCELLENMELİ, ya da en
azından "geç tamamlandı" durumu ayrı bir alanla işaretlenmeli. Sessiz kayıp
kabul edilemez.

**Gerçekleşen**
`ToolInvocationTracker.cs:143` satırı `TimedOut = result.Exception is
TraconToolTimeoutException` yazıyor — kayıt YALNIZ ilk (zaman aşımı) sonucu
temel alıyor, `TimeoutAIFunction.cs`'in arka planda gözlemlediği geç
tamamlanma (`logger.LogInformation("...finished after its timeout had
already been reported...")`) hiçbir yerde `tool_invocations`'a geri
yazılmıyor.

**Yeniden üretme**
1. `Tracon:Images:Enabled=true`, `Provider=openai`, `Model=gpt-image-1` ile
   başlat (gpt-image-1 üretimi rutin olarak 30s'yi geçiyor).
2. `generate_image` tool'u taşıyan bir agent'a görsel isteği gönder.
3. `GET /api/runs/{runId}/tools` → `succeeded:false, usage:null` görülür.
4. `GET /api/attachments?sessionId=...` → gerçek bir görsel ekin VAR
   olduğu görülür — çelişki budur.

**Kanıt**
- `docker exec ap-pg psql ...`: 2 ek satırı (`image/png`, 1024x1024,
  `runId` dolu) + 2 `tool_invocations` satırı (`usage=NULL,
  succeeded=false`) AYNI `run_id` altında.
- Kaynak: `src/Tracon.Core/Tools/TimeoutAIFunction.cs:79-99` (arka plan
  gözlemi, geri yazma yok), `src/Tracon.Core/Recording/
  ToolInvocationTracker.cs:143`.

**Kapsam**
Yalnız görsel üretimi değil — 30s+ süren HER tool (uzun `speak` metni,
yavaş MCP çağrısı vb.) aynı sessiz maliyet kaybına maruz. Kapanışta sınıf
taraması önerilir (`TimeoutAIFunction` kullanan her tool + gerçek harcama
yapan tool'ların timeout süresi).

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

**Yeniden koşum — 2026-09-18 (Aile G kapanışı) · ☑ GEÇTİ**

Gerçek OpenAI `gpt-image-1`, gerçek PostgreSQL (`mt_g` şeması), sevk edilen
varsayılanlarla. **İki ayrı ölçüm yapıldı; ikisi de kaydın anlattığı çelişkiyi
ortadan kaldırıyor.**

**Ölçüm 1 — zaman aşımı ZORLANDI (`Tracon:Images:Timeout=00:00:10`).**
Model tool'u iki kez çağırdı, ikisi de 10 sn'de düştü
(`timed_out=t`, `duration_ms` 10036 / 10008). Turdan farkı:

```
SELECT count(*) FROM mt_g.attachments;  ->  0
GET /api/attachments?sessionId=aile-g-095  ->  []
```

**Turun bulduğu çelişki kökünden kalktı.** Turda aynı yordam `tool_invocations`
satırları `succeeded:false` iken **2 gerçek, faturalanmış görsel** üretmişti;
bugün zaman aşımı gövdeyi gerçekten **iptal ediyor** (K-805) ve
`GenerateImageTool` token'ını `GenerateAsync`'e geçirdiği için sağlayıcı
çağrısı durur — üretilmemiş bir görselin bedeli de yoktur. `late_completed_at`
bu yüzden boş: iptal edilen gövde hiçbir şey üretmedi, uydurulacak bir hesap
yok.

**Ölçüm 2 — sevk edilen varsayılan (`00:02:00`, K-807).** Zaman aşımı **hiç
olmadı**; çağrı 21,8 sn'de başarıyla bitti:

```
 tool_name      | timed_out | succeeded | usage_unit | usage_quantity | duration_ms | late
 generate_image | f         | t         | tokens     | 4160           | 21796       | f

 attachments: 1 × image/png, 1.264.473 bayt, run_id dolu
 SSE yanıtı: attachmentIds=01a0b4f1-356a-7a36-934d-29d5574f1885
```

Spec'in kendi barı (tool yalnız ek kimliği döndürür) karşılandı **ve** kaydın
açtığı kusur kapandı: satır artık çağrının gerçekten ne yaptığını söylüyor.
30 sn'lik genel varsayılan bu çağrıyı turdaki gibi kesecekti.

⚠️ `cost` bu satırda hâlâ boş — `usage_quantity` (4160 token) kayıtlı ama
`Tracon:Pricing:Images:openai:gpt-image-1` örnek uygulamada **yapılandırılmamış**
(appsettings'teki `//Images` yorumu yolu gösteriyor). Bu, `HATA-S1-010`'un
model fiyatlarından ayrı bir kalemdir; görsel fiyatı asla tahmin edilmez ve
`UnpricedModelWarningService` yalnız sohbet katalogunu tarar. Kapanışta
kullanıcıya sunulan açık kalem listesine yazıldı.

🚨 **Bu yeniden koşum ikinci bir kusur buldu:** `TraconImageOptions.Timeout`
eklenmiş ve `docs-site`'ta yapılandırılabilir diye belgelenmiş, ama
yansımasız bağlayıcıya (`BindImages`) **hiç yazılmamıştı** — `Enabled`
bağlanıyordu, `Timeout` sessizce yok sayılıyordu. Canlı koşum yakaladı, hiçbir
test yakalamadı: `TraconOptionsBindingCoverageTests` bu sınıfı yapısal olarak
kilitler ama `TraconOptions` AĞACINI gezer ve `TraconImageOptions` o ağacın
düğümü değil, **kardeş** bir section'dır. Bağlayıcı düzeltildi;
`TraconImageOptionsBindingTests` eklendi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-096 — Kapalı ayar tool'u ve operator ucunu açmaz

**Gerçek sonuç**
İki ayrı gözlem: (1) `Tracon:Images:Enabled=false` (bu turun varsayılanı,
095'ten önceki hâl) iken `POST /api/images/generate` → `HTTP 405` (spec
`404` bekliyordu), `Allow: GET, HEAD` başlığı taşıyor. Aynı path'e `GET` →
`HTTP 404` ("There is no endpoint or console asset at path..."). Kök neden:
bu path'e GET/HEAD için eşleşen bir GENEL fallback/console-asset rotası
var (Tracon'a özgü değil — herhangi bir eşleşmeyen API path'i için aynı
davranış beklenir), POST bu rotanın metod kümesinde olmadığı için ASP.NET
Core standart 405 üretiyor. Uç işlevsel olarak BAĞLI DEĞİL (spec'in asıl
iddiası), yalnız literal durum kodu POST için 404 değil 405 — küçük bir
spec netliği notu, ürün kusuru değil. (2) `POST /api/agents` ile
`toolNames:["generate_image"]` taşıyan bir tanım → `HTTP 400 "Definition
invalid"`, `"Agent 'mt-mm-096-probe2' refers to tool 'generate_image', but
it is not registered in this code."` — TAM beklendiği gibi reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-097 — URI görsel çıktısı giden ağ muhafızını atlayamaz

**Gerçek sonuç — canlı senaryo koşulamadı, otomatik test kanıtına dayanıldı.**
Bu case özel bir `UriContent` dönen test adaptörü gerektiriyor; donuk
`samples/` bunu taşımıyor ve kod donukken eklenemez. `Tracon.Core.UnitTests`
(MT-MM-083'te TAM koştu: 2805/2805 geçti) bu paketin içinde
`ImageAttachmentWriterTests` sınıfını taşıyor. Canlı akış doğrulanmadı —
bu bir sınırlama olarak not düşülüyor, geçti sayılmıyor.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 9) · ☐ AÇIK KALIYOR**

Ön koşul *"`UriContent` dönen bir görsel sağlayıcı veya test adapter'ı"*
istiyor. Sevk edilen üç adapter'ın (`UseOpenAIImages` · `UseGoogleImages` ·
`UseAzureOpenAIImages`) **hiçbiri** `UriContent` döndürmüyor — üçü de
`DataContent` (bayt) üretiyor (ölçüldü: `GoogleImageGenerator.cs`
`new DataContent(bytes, …)`).

∴ giden ağ muhafızının görsel **indirme** yolu bu kurulumda hiç
tetiklenemiyor; case yeni bir test adapter'ı ister. K-834'ün deseniyle
eklenebilir ama bu, sevk edilen örneğe **sahte bir görsel sağlayıcısı**
koymak demektir — diğer demo kancalarından farklı bir karar. Kullanıcıya
bırakıldı; `00-INDEKS.md` açık kalem tablosuna yazılır.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-098 — Google adapter'ı boyut tahmin etmez

**Gerçek sonuç — ilk yarı kaynakla doğrulandı, ikinci yarı canlı koşulmadı.**
`src/Tracon.Google/Internal/GoogleImageGenerator.cs:40-47`: `options.ImageSize
is not null` iken KOŞULSUZ `"Google image generation does not accept
WIDTHxHEIGHT. Omit 'size' and use the provider default."` fırlatıyor —
sağlayıcıya hiç istek gitmeden, girdi doğrulama aşamasında. Bu ilk iddiayı
(açık hata, boyut tahmini yok) kaynak düzeyinde KANITLIYOR.
`Tracon.Google.UnitTests` TAM koştu: 84/84 geçti (bu davranışa özel bir
test bulunamadı, ama kayıt testleri geçti). İkinci iddia (boyutsuz çağrının
sağlayıcı varsayılanıyla ÇALIŞTIĞI) canlı bir Google görsel modeli
gerektiriyor; geçerli bir model adı bu turda doğrulanamadı — koşulmadı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 9) · ☐ AÇIK KALIYOR (K-835 engelliyor)**

**Adım 1 GEÇTİ — case'in asıl konusu.** `--Tracon:Images:Provider=google` ile
`size: "1024x1024"` gönderildi:

```
POST /api/images/generate  {"prompt":"a red bicycle","size":"1024x1024"}
→ 502  "Google image generation does not accept WIDTHxHEIGHT.
         Omit 'size' and use the provider default."
```

☑ Açık hata. ☑ Adapter `WIDTHxHEIGHT`'ı Google'ın aspect-ratio/size-tier
sözleşmesine **tahmin ederek çevirmiyor**. Kontrol adapter'ın **içinde**,
sağlayıcıya gitmeden önce (`GoogleImageGenerator.cs`, `options.ImageSize is
not null` dalı) — yani hatalı bir istek para harcamıyor.

**Adım 2 ÖLÇÜLEMEDİ.** *"İkinci çağrı sağlayıcının varsayılan boyutuyla
çalışır"* iddiası **K-835** yüzünden imkânsız: Google görsel yolu artık
sunulmayan Imagen `:predict` ucunu hedefliyor ve `size` verilmeden de `502`
dönüyor. Kontrol koşulamadığı için case açık kalıyor; K-835 düzeltilince
yalnız adım 2 tekrarlanmalıdır.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-099 — Tur üretmeyen bir `commit` paneli asmaz

**Gerçek sonuç**
Playground UI (Playwright) gerektiriyor, tarayıcı başka şeritçe kullanımda —
ertelendi (bkz. MT-MM-088). Not: bu case'in SUNUCU tarafı zaten MT-MM-073
ile dolaylı doğrulandı (`idle` çerçevesi, `done` YOK); yalnız panelin görsel
"asılı kalmama" davranışı gözlenemedi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 9) · ☑ GEÇTİ**

WebSocket çerçeveleri yakalanarak ölçüldü. Panel açıldı, **hiç konuşmadan**
hemen `Send now`'a basıldı:

```
gelen cerceveler: ["ready", "idle"]
panel durumu:     listening
```

☑ Sunucu **tek bir `idle`** çerçevesi gönderdi. ☑ **`done` GÖNDERMEDİ.**
☑ Panel dinleme durumuna döndü, **asılmadı**, ve yeni bir tur başlatılabilir
durumda kaldı (`Send now` hâlâ etkin).

💡 Bu case'in bir kusuru görmesi için `done` beklemek yetmez — **hiçbir şey**
gelmemesi de geçerli bir arızadır ve panel sonsuza kadar "gönderiliyor"da
kalırdı. Çerçeve listesini okumak ikisini birden ayırt ediyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-104 — 50'den fazla ses varken kalan sayı satırı İngilizce'dir

**Gerçek sonuç — canlı koşulamadı (ortam sınırı, MT-MM-050'de zaten not
düşüldü).** Bu hesapta yalnız 21 gerçek ElevenLabs sesi var,
`MaxListedVoices=50` altında — kısaltma satırı gerçek sağlayıcıyla asla
tetiklenmez. `ListVoicesToolTests.cs` aynı dosyada "... and N more voices."
iddiasını taşıyor ve MT-MM-093'ün tam koştuğu pakette (68/68 geçti).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 9) · ☑ GEÇTİ**

⚠️ **Ön koşul canlı hesapla sağlanamıyor ve sayı ölçüldü:** taşma satırının
eşiği `MaxListedVoices = 50` (`ListVoicesTool.cs:21`), hesapta ise **21** ses
var. ∴ canlı çağrıda satır **doğru olarak** görünmüyor (ölçüldü: `"more
voices"` yok) — bu, case'in karşıt kontrolüdür.

Ön koşulun ikinci seçeneği (*sahte `ISpeechSynthesizer`*) tam bu sınırı
zorlayan testle karşılanıyor ve koşuldu:

```
ListVoicesToolTests.Overflow_line_is_in_English
  result.ShouldContain("… and 3 more voices.")
→ 4 test · 0 düşen
```

Biçim case'in dediğiyle birebir: `"… and N more voices."`, İngilizce, Türkçe
metin yok. Test dosyasının kendi dokümanı da bunu söylüyor: *"Verifies the
list_voices tool's output text: no Turkish…"*

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-109 — `UseLiveVoice()` da çağrılmadan adres hiç yoktur

**Gerçek sonuç — otomatik test kanıtı (aynı gerekçe, MT-MM-108).**
`LiveVoiceRegistrationTests.Without_UseLiveVoice_the_route_does_not_exist`
bu case'i birebir kapsıyor, aynı 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 7) · ☑ GEÇTİ**
Ön koşulun istediği geçici yorum satırı yerine kalıcı bayrak kullanıldı
(K-834): `Tracon:Demo:SuppressRegistrations`. Varsayılan değişmedi.

`--Tracon:Demo:SuppressRegistrations=OpenAILive,LiveVoice` (ikisi de yok):

☑ **Rota hiç açılmamış** — OpenAPI belgesinde `voice/live` geçen **hiçbir
yol yok**. Yetenek "var ama kapalı" görünmüyor.

⚠️ **Spec `404` diyor; ölçülen `POST`ta `405`, `GET`te `404`** — ve bu
bir ürün kusuru değil. Kanıt: **tamamen uydurma** bir yol da birebir aynı
davranıyor:

| Yol | POST | GET |
|---|---|---|
| `api/voice/live/sessions` | `405` | `404` |
| `api/tamamen-uydurma-yol` | `405` | `404` |
| `api/voice/live/sessions/alt/yol` | `405` | `404` |

∴ canlı ses rotası **hiç tanımlanmamış bir yoldan ayirt edilemiyor** —
case'in asıl iddiası budur ve tutuyor. `405`, konsol varlıklarının
`{prefix}/{**path}` yakala-hepsi rotasının GET-only olmasından geliyor.
Spec bu ölçümle keskinleştirildi (skill §1.1).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-110 — Gerçek GPT-Live oturumu

**Gerçek sonuç**
👤 Gerçek insan/mikrofon veya tarayıcı gerektiriyor — §4.3 fiziksel eylem
tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 8) · ☑ GEÇTİ**
🚨 **Türün çözümü — gerçek konuşma sentezle üretildi.** `live-test.html`
mikrofondan besleniyor ve sessiz bir odada anlamlı bir soru oluşmuyor. Yordam:
`navigator.mediaDevices.getUserMedia` **oturum açılmadan önce** kendi
`MediaStreamAudioDestinationNode`'umuza bağlandı (parça oturum boyunca **aynı**
kalır), sonra sorunun sesi sunucunun **kendi** TTS'iyle üretilip
(`POST /api/voice/speak` → ek) o düğüme çalındı. Ses gerçekten WebRTC
üzerinden sağlayıcıya gitti — kanıtı dökümde: sağlayıcı *"order four four
two"* diye transkript etti.

⚠️ İlk denemede `replaceTrack` kullanıldı ve oturum `Abandoned` oldu; parçayı
**değiştirmek** yerine **içine çalmak** gerekiyor.

Gerçek GPT-Live oturumu açıldı:

```
microphone granted
offer ready, asking Tracon to create the session
session 01a0b7c4-9bc9-780f-8d20-db9b205ea5f1 on gpt-live-1
transcript persisted: true
ice: checking → remote audio track → answer applied — speak now
connection: connecting → ice: connected → connection: connected
```

☑ Oturum açıldı (`gpt-live-1`). ☑ **Sesli yanıt geldi** — kulakla değil
`getStats()` ile ölçüldü, çift yönlü RTP akıyor:

| Yön | Bayt | Paket |
|---|---|---|
| gelen (sağlayıcı → tarayıcı) | **70.225** | **958** |
| giden (tarayıcı → sağlayıcı) | 55.000 | 1.000 |

`remote audio track` sayfanın `#speaker` öğesine bağlı. Yanıtın **içeriği**
MT-MM-112'nin dökümünde okunabiliyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-115 — Fiyat yapılandırması yokken `cost` `null` döner

**Gerçek sonuç — otomatik test kanıtı (MT-MM-110'a bağımlı).**
`LiveVoiceTests.With_no_price_configured_the_cost_is_null_NOT_zero` kapsıyor
(K-032'nin canlı katmandaki karşılığı), 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 8) · ☑ GEÇTİ**

`Tracon:Pricing:Voice:openai:gpt-live-1` **olmadan** koşuldu:

```json
{"turns": 1, "liveSeconds": 15.0, "cost": null, "endReason": "Client"}
```

☑ `cost` **`null`** — **`0` değil**. ☑ `liveSeconds` yine dolu (`15.0`).
Sıfır, konuşmanın ücretsiz olduğunu iddia ederdi; `null` "fiyatı bilmiyorum"
diyor.

🚨 **Fiyatı kaldırmak düşünülduğünden zordu ve yol kayda değer.** Fiyat
`user-secrets`'tadır ve skill §1.2 `user-secrets` yazmayı yasaklar. Komut
satırından **boş** vermek de çalışmıyor — geçersiz bir kayıt üretiyor ve
kapı haklı olarak durduruyor:
`OptionsValidationException: 'Voice:openai:gpt-live-1' contains neither
'PerMillionCharacters' nor 'PerMinute'`. Çözüm `ASPNETCORE_ENVIRONMENT=Production`
ile koşmaktı (`user-secrets` yalnız Development'ta yüklenir — §3.4) ve gereken
yedi ayarı **çift alt çizgili ortam değişkeni** olarak vermek; komut satırına
`secret` **konmadı** (§6'nın `ps eww` sızıntı dersi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-117 — Başka kiracının canlı oturumu erişilemez oturumla aynı 404'ü alır

**Gerçek sonuç — otomatik test kanıtı (MT-MM-110'a bağımlı).**
`LiveVoiceAuthorizationTests.Another_tenants_session_cannot_be_closed_or_read`
ve `A_denied_caller_is_told_the_session_does_not_exist` bu iddiayı (aynı
404, ayırt edici bayt yok) kapsıyor, 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 8) · ☑ GEÇTİ**

Kiracı modu açık. `tenant-a` bir canlı oturum açtı; üç istek `tenant-b` ile:

| Adım | İstek | Sonuç |
|---|---|---|
| 2 | `DELETE .../{tenant-a'nin id'si}` | ☑ `404` |
| 2 | `GET .../{tenant-a'nin id'si}` | ☑ `404` |
| 3 | `GET .../{rastgele id}` | ☑ `404` |

Gövdeler **birebir aynı kalıpta**:

```json
{"type":"…rfc9110#section-15.5.5","title":"Session not found","status":404,
 "detail":"There is no session with id '<ID>', or it does not belong to this tenant."}
```

⚠️ **Spec "kimlik dışında bayt bayt aynı" diyor; ölçümde İKİ alan değişiyor:**
`id` **ve** `traceId`. `traceId` her istekte değişen bir korelasyon
değeridir, sızıntı değil — `type`, `title`, `status` ve `detail` dördü de
bayt bayt aynı. Spec bu ölçümle keskinleştirildi (skill §1.1).

💡 `detail` cümlesinin kendisi de tasarımın parçası: *"or it does not belong
to this tenant"* — iki durumu **tek** cümlede birleştiriyor, ∴ hangisi
olduğu anlaşılmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-121 — `UseGoogleImages()` Google üreticisini kaydeder

**Gerçek sonuç**
Aynı gerekçeyle (`Program.cs` geçici düzenleme gerektiriyor) kapanışa
ertelendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 7) · ☒ KALDI — ÜRÜN KUSURU (K-835)**
Ön koşulun istediği geçici yorum satırı yerine kalıcı bayrak kullanıldı
(K-834): `Tracon:Demo:SuppressRegistrations`. Varsayılan değişmedi.

**Adım 1 GEÇTİ.** `--Tracon:Images:Provider=google` ile uygulama sorunsuz
başladı (`health=200`, `Application started: 1`) — MT-MM-120'nin kapısı Google
sağlayıcısıyla da tatmin oluyor, ve kayıt çağrısı ile `Provider` seçimi
gerçekten **ayrı iki karar**.

🚨 **Adım 2 KALDI — ve sebep ortam sınırı DEĞİL.** Her üretim isteği
`502 "Image could not be generated"` veriyor.

```
POST /api/images/generate  {"prompt":"a red bicycle"}  → 502
```

**Üç adımda kanıtlandı:**

1. `GoogleImageGenerator.cs:56` `Client.Models.GenerateImagesAsync(...)`
   çağırıyor — bu Imagen `:predict` ucudur.
2. Bu anahtarda `ListModels` **58 model** döndürüyor; altı görsel modelinin
   **hiçbiri** `predict` desteklemiyor, altısı da yalnız `generateContent`:
   ```
   gemini-2.5-flash-image        -> ['generateContent', 'countTokens', ...]
   gemini-3-pro-image            -> ['generateContent', ...]
   gemini-3.1-flash-image        -> ['generateContent', ...]
   ```
   `imagen-3.0-generate-002:predict` doğrudan çağrıldığında Google
   **`404 NOT_FOUND`** diyor.
3. **Yetenek var**: **aynı anahtarla**
   `gemini-2.5-flash-image:generateContent` çağrısı **3.2 MB'lık bir PNG**
   üretti (`inlineData`, `image/png`).

Google SDK'nın kendisi de koşum günlüğünde söylüyor:
*"The GenerateImagesAsync method is deprecated … Please use the
GenerateContentAsync method with image models instead."*

∴ paket tüketicinin **kullanamayacağı** bir yol sevk ediyor. Kusur kodlandı:
**K-835**. Düzeltme bu oturumda **yapılmadı** — sevk edilen bir paketin
davranışını değiştirir ve seçenek eşlemesi birebir değildir
(`Count` → `candidateCount` ama görsel modelleri genelde 1 döndürür;
`MediaType` hiç kontrol edilemez). Karar kullanıcıya bırakıldı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

## MT-MM-122 — `UseAzureOpenAIImages()` Azure üreticisini kaydeder

**Gerçek sonuç**
Aynı gerekçeyle kapanışa ertelendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Fiziksel eylem / kod donması / paylaşılan kaynak nedeniyle koşulamayan case'ler

| Case | Neden | Kullanıcıdan istenen / kapanışta yapılacak |
|---|---|---|
| MT-MM-086, 087 | Gerçek mikrofon/konuşma | Chrome'da playground açıp gerçekten konuşarak koşulmalı |
| MT-MM-088, 090, 099 | Playwright tarayıcısı bu oturumda başka şeritçe kullanımdaydı | Tarayıcı boşalınca (Playground UI, gerçek mikrofon gerekmez) koşulabilir |
| MT-MM-097 | `UriContent` dönen özel test adaptörü gerekiyor, kod donuk | Kapanışta ya da izole bir test ortamında koşulmalı |
| MT-MM-098 (2. yarı) | Geçerli bir Google görsel modeli adı doğrulanamadı | Google Imagen model adı netleşince canlı koşulmalı |
| MT-MM-103, 104 | `list_voices` sağlayıcısız hiç kayıtlı değil / hesapta 50+ ses yok | Otomatik test kanıtı kullanıldı (bkz. case metni), canlı yol ortam sınırı |
| MT-MM-108, 109, 111-118 | `samples/` her zaman `UseOpenAILive()` çağırıyor; MT-MM-110 fiziksel mikrofon/tarayıcı istiyor | Otomatik test kanıtı kullanıldı (`LiveVoice*Tests`, 1077/1077 geçti); canlı yol MT-MM-110'a bağımlı |
| MT-MM-110 | Gerçek insan sesi veya sentetik mikrofon (WebRTC) gerekiyor | `http://localhost:5080/live-test.html` üzerinden gerçek bir konuşma yapılmalı |
| MT-MM-120, 121, 122 | `Program.cs`'te geçici satır değişikliği istiyor, kod donuk (kural 1) | `MT-PG-067` ile aynı yordamla kapanışta koşulmalı |

**Yeniden koşum — 2026-09-19 (kapanış, §5 turu 7) · ⏭ ATLANDI**

Azure kimliği bu ortamda **yok** — `00-INDEKS.md` §3.1 ve §3.4'ün kuralı:
*"Azure | Kimlik **yoktur**; Azure isteyen case `⏭ Atlandı` kalır, kusur
değildir."* Doğrulandı: `Tracon:Providers:AzureOpenAI:Endpoint` ve `ApiKey`
tanımlı değil, ∴ `UseAzureOpenAIImages()` zaten hiç koşmuyor.

💡 Case'in asıl iddiası (*"üç görsel sağlayıcısının kayıt yüzeyi tek
biçimdir"*) diğer ikisiyle **kısmen** ölçüldü: `UseOpenAIImages()` ve
`UseGoogleImages()` aynı kalıbı izliyor ve ikisi de MT-MM-120'nin kapısını
tatmin ediyor (MT-MM-121 adım 1). Azure kimliği geldiğinde yalnız üçüncü
satır doğrulanacaktır.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

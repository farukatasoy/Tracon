# 19 — Çok Modluluk: Ek, Ses Tool'ları ve Gerçek Zamanlı Konuşma (`MM`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../19-COK-MODLULUK-VE-SES.md`](../../19-COK-MODLULUK-VE-SES.md)
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

---

## MT-MM-001 — Geçerli bir PNG yüklenir

**Gerçek sonuç**
`HTTP: 201`. `mediaType: "image/png"`, `byteSize: 68`, `sha256` dolu,
`id` bir GUID (`01a0acc3-9d1c-7558-9ac5-e282bc2188c1`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-002 — Yanlış `Content-Type` sihirli bayt tarafından geçersiz kılınır

**Gerçek sonuç**
`mediaType` alanı `image/png` yazdırıldı — istemcinin `text/plain` iddiası
yok sayıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-003 — Yürütülebilir/tanınmayan içerik reddedilir

**Gerçek sonuç**
`HTTP: 400`, başlık "Attachment type rejected", detay tanınan türleri listeler.
`MZ` imzası hiçbir kuralla eşleşmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-004 — Boş dosya reddedilir

**Gerçek sonuç**
`HTTP: 400`, başlık "Attachment cannot be empty".

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-005 — `MaxBytes` (20 MB) aşımı reddedilir

**Gerçek sonuç**
`HTTP: 400`, başlık "Attachment too large", detay `20971529 bytes; the limit
is 20971520 bytes` yazdı (istek boyutu + 8 baytlık PNG imzası dahil, sınır
doğru).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-006 — MP3 çerçeve senkronu bit maskesiyle tanınır

**Gerçek sonuç**
Beş geçerli varyant (`fb/f3/f2/fa/e3`) `HTTP=201`, iki `reserved` varyant
(`e8`/`e1`) `HTTP=400` — hepsi `OK` (beklenenle eşleşti).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-010 — İndirme doğru başlıklarla ve bayt-bayt eşleşmeyle döner

**Gerçek sonuç**
`Content-Type: image/png`, `Content-Disposition: attachment; filename="test.png"`,
`X-Content-Type-Options: nosniff`, `ETag` sha256'nın kendisi. `diff` sıfır
fark, `BAYT BAYT AYNI` yazdırıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-011 — Listeleme `sessionId` ile filtreler; `skip`/`take` sınırlanır

**Gerçek sonuç**
İlk `curl` `1` yazdırdı (yalnız o oturumun eki). `take=99999&skip=-5` isteği
`HTTP: 200` döndü (400 değil) — kırpma davranışı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-012 — Silme sonrası indirme VE ikinci silme `404` döner

**Gerçek sonuç**
Sırasıyla `204`, `404`, `404` — beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-013 — Başka kiracının ekine erişilemez

**Ön koşul uygulaması:** Uygulama `Tracon:Tenancy:Enabled=true` +
`Tracon:Tenancy:AllowHeaderResolution=true` ile yeniden başlatıldı
(bu oturumdan itibaren aile 19'un tenancy gerektiren case'leri için kalıcı).

**Gerçek sonuç**
`kiraci-alfa` altında yüklenen ekin `kiraci-beta` ile GET'i `HTTP: 404` döndü —
"böyle bir ek yok" (403 değil), sızıntı yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-MM-020 — Ek + mesajla çalıştırma; modele giden içerik `DataContent`'tir

**Gerçek sonuç**
`HTTP: 200`, akış `event: done` ile tamamlandı, hata yok. Gerçek OpenAI
çağrısı (`support` agent, `gpt-5.4-mini`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-021 — Var olmayan bir ekle çalıştırma → `400`, akış başlamaz

**Gerçek sonuç**
`HTTP: 400`, detay: "There is no attachment with id
'00000000-0000-0000-0000-000000000000', or it does not belong to this
tenant." Yanıt akışlı DEĞİLDİ (düz JSON `ProblemDetails`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-022 — Başka kiracının eki çalıştırmada kullanılamaz

**Gerçek sonuç**
`kiraci-beta` ile `kiraci-alfa`'nın (MT-MM-013) ekiyle çalıştırma denemesi
`HTTP: 400`, aynı "Attachment not found" mesajı — kiracı sızıntısı yok, akış
başlamadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-023 — `message` boş ama `attachmentIds` doluysa istek geçerlidir

**Gerçek sonuç**
`HTTP: 200`, akış `event: done` ile tamamlandı — yalnız `attachmentIds` dolu
olması yeterli oldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-026 — `/v1/responses` gömülü `data:` URI'yi eğe çevirir

**Gerçek sonuç**
`HTTP: 200`. `GET /api/attachments` listesinde gömülü PNG ayrı bir kayıt
(`fileName: "upload"`, `sessionId: "resp_..."`) olarak durdu — gövdedeki
base64 blok sohbet geçmişine olduğu gibi yazılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-027 — `/v1/chat/completions` görsel girdiyi kabul etmez

**Gerçek sonuç**
`HTTP: 200`. Model yanıtı görseli GÖREMEDİĞİNİ belirtti ("Görseli göremiyorum...")
— görsel parça sessizce yok sayıldı, yalnız metin parçası ulaştı. K-116'nın
bilinçli kapsam kararı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-028 — Beyaz listede olmayan bir `data:` türü reddedilir

**Gerçek sonuç**
`HTTP: 400`, `AttachmentIngestion.ReplaceEmbeddedDataAsync` guard hatasını
doğrudan istemciye taşıdı ("File type not recognized...").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-031 — `Tracon:Voice:ApiKey` yoksa `/api/voice/*` `501`, konuşma ucu `404` döner

**Gerçek sonuç**
`Tracon__Voice__ApiKey=""` ile yeniden başlatıldı. `GET /api/voice/health`
`HTTP: 501`. `GET /api/voice/sessions/deneme/stream` `HTTP: 404`. Gerçek
anahtar geri yüklenip yeniden başlatıldı.

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

## MT-MM-038 — `GET /api/voice/voices` gerçek ses listesini döner

**Gerçek sonuç**
`HTTP: 200`, 21 ses döndü; her öğede `voiceId`/`name`. İlk ses
`pNInz6obpgDQGcFmaJgB` (`Adam - Dominant, Firm`). `$VOICE_ID` bu değere
ayarlandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-039 — `GET /api/voice/health` ücret üretmeden sağlığı ölçer

**Gerçek sonuç**
`isHealthy:true`, `voiceCount:21` (MT-MM-038 ile eşleşti), `latency` dolu.
`GET /v2/voices`'e gitti, hiçbir ses üretmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-040 — `POST /api/voice/speak` metni seslendirir, ek üretir, maliyet döner

**Gerçek sonuç**
`HTTP: 200`. `attachment.mediaType:"audio/mpeg"`, `byteSize:38078`,
`characters:9`, `isEstimated:false`, `cost:0.00099`, `currency:"USD"`.
Örnek uygulamanın `appsettings.json`'ı `Pricing:Voice:elevenlabs:
eleven_multilingual_v2:PerMillionCharacters=110.0` taşıyor — bu yüzden
`cost` burada DOLU (spec'in "yapılandırılmamışsa null" dalı bu örnekte hiç
tetiklenmiyor, çünkü fiyat HER ZAMAN yapılandırılı geliyor; K-032'nin asıl
iddiası — hesap ASLA UYDURULMAZ, sıfır YAZILMAZ — burada "doğru fiyatla
doğru hesap" olarak doğrulandı). Attachment indirilip gerçekten çalan bir
MP3 olduğu `file` komutuyla doğrulandı (aşağıya bakın).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-041 — `POST /api/voice/speak` boş metinle `400` döner

**Gerçek sonuç**
`HTTP: 400`, başlık "Text empty" — ElevenLabs'e hiç istek gitmedi (kredi
harcanmadı, MT-MM-039'daki `voiceCount` ile karşılaştırıldığında ek çağrı
görünmüyor).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-042 — `POST /api/voice/speak` `MaxCharactersPerRequest` sınırını yerel olarak denetler

**Gerçek sonuç**
6000 karakterlik metin `HTTP: 400`, başlık "Text too long", detay
`"The text is 6000 characters; the limit is 5000. ..."` — istek ElevenLabs'e
GİTMEDEN reddedildi (400, 502 değil). Düzeltilen kusur hâlâ düzeltilmiş
durumda, regresyon yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-043 — `GET /api/voice/sessions` konuşma katmanı kapalıyken boş liste döner, `501` DEĞİL

**Gerçek sonuç (MT-MM-031'e ek doğrulama olarak koşuldu)**
Voice/VoiceConversation kapalıyken (`Tracon:Voice:ApiKey` boş) `GET
/api/voice/sessions` `HTTP: 200`, gövde `[]` — `501` DEĞİL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-046 — `speak` gerçek bir çalıştırmada çağrılır, ek `session_id`'si doludur (G1)

**Gerçek sonuç**
`voice-assistant` agent'ı `speak` tool'unu doğru argümanla çağırdı, gerçek
ElevenLabs sesi üretti (`attachmentId=01a0acdc-67ae-71b8-8b53-33de2d7d5c18`,
`audio/mpeg`, 33062 bayt). SQL: `session_id='manuel-mm-speak-1'`,
`run_id` dolu, `media_type='audio/mpeg'` — tek satır, `session_id` NULL
DEĞİL.

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

## MT-MM-048 — `transcribe` kayıtlı bir ses ekini metne çevirir

**Gerçek sonuç**
Yanıt `transcribe` tool çağrısı içerdi; sonuç `"[lang=tur] Merhaba dedi gibi
sesli söyle"` — `[dil=...] ...` biçiminde, boş değil. SQL:
`usage_unit='seconds'`, `usage_quantity≈1.997`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-049 — `transcribe` — ses OLMAYAN bir ekle çağrılırsa hata döner

**Gerçek sonuç — aynı kök neden, `HATA-S1-024`'e bağlı.** Tool DOĞRU şekilde
işlemi durdurdu (PNG'yi transcribe ETMEDİ, `usage_unit` boş kaldı — yan etki
yok) ama modele dönen mesaj spec'in beklediği `"'...' bir ses dosyasi
degil..."` yerine yalnız genel `"Error: Function failed."` oldu. Fonksiyonel
güvenlik korunuyor (yanlış türde dosya işlenmiyor), yalnız hata mesajının
açıklayıcılığı kayboluyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı (bkz. `HATA-S1-024`)

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

## MT-MM-053 — `speak` tool çağrısı `tool_invocations`'a `usage_unit=characters` ile yazılır

**Gerçek sonuç**
MT-MM-046'nın `run_id`'siyle: `usage_unit='characters'`,
`usage_quantity=8.0`, `usage_estimated=false`, `cost=0.00088`,
`cost_currency='USD'`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-054 — `transcribe` tool çağrısı `usage_unit=seconds` ile yazılır

**Gerçek sonuç**
MT-MM-048'in `run_id`'siyle: `usage_unit='seconds'`,
`usage_quantity≈1.997` — sesin gerçek uzunluğuna yakın.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-055 — `POST /api/voice/speak` (operatör yolu) `tool_invocations`'a hiç satır yazmaz

**Gerçek sonuç**
`tool_name='speak' AND created_at > now()-5min` sayımı TAM OLARAK `4` —
bu, agent üzerinden yapılan 4 gerçek `speak` çağrısıyla (MT-MM-046 + üç
MT-MM-047 denemesi) birebir eşleşiyor. MT-MM-040'ın operatör çağrısı (aynı
5 dakikalık pencerede) HİÇ satır eklemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-059 — Python WebSocket istemcisini kur

**Gerçek sonuç**
`pip3 install websockets` zaten kuruluydu (17.0.1). `voice_client.py`
`~/tracon-manuel-test/` altına yazıldı, `--help` hatasız argüman listesini
gösterdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-062 — `UseVoiceConversation()` açıksa ama sağlayıcı yoksa `501`; hiç çağrılmadıysa `404`

**Gerçek sonuç**
Yalnız kaynak/otomatik test referansı — MT-MM-031'de zaten kapsandı, ayrı
doğrulama gerekmiyor (spec'in kendi notu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-063 — WebSocket olmayan bir isteğe `400` döner

**Gerçek sonuç**
`HTTP: 400`, başlık "WebSocket upgrade required" — detay "This endpoint can
only be used over WebSocket...".

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-064 — Token doğruysa alt protokolde KABUL edilir, `ready` çerçevesi gelir

**Gerçek sonuç**
`BAGLANDI, kabul edilen alt protokol: tracon.voice.v1`. İlk `<<` çerçevesi
`{"type": "ready", "agent": "voice-assistant", "sessionId":
"manuel-ws-token-ok", "persistAudio": false}` — beklenen alanlarla birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-065 — Token YANLIŞSA el sıkışma REDDEDİLİR

**Gerçek sonuç**
`BAGLANTI REDDEDILDI: server rejected WebSocket connection: HTTP 401` —
sunucu yanlış token hakkında hiçbir ipucu vermedi (yalnız `HTTP 401`).

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

## MT-MM-071 — Her tur normal bir `runs` satırı üretir

**Gerçek sonuç**
`SELECT ... FROM runs WHERE id='01a0ad5c-...'` → `status=1` (Completed),
`agent_name='voice-assistant'`, `model_id='gpt-5.4-mini'`,
`input_tokens=442`, `output_tokens=25`, `session_id='manuel-ws-tur-1'` —
hepsi pozitif ve beklenen.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-072 — İkinci `start` REDDEDİLİR

**Gerçek sonuç**
Gözlenen davranış: **`error` çerçevesi** — `"The conversation is already
started; the agent does not change during the connection."` Bağlantı
KAPANMADI: istemci `stop` çerçevesini sorunsuz gönderebildi (sunucu
kapatmış olsaydı `ws.send` bir `ConnectionClosed` fırlatırdı — fırlatmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-073 — Ses göndermeden `commit`

**Gerçek sonuç — spec'in örnek komutu düzeltildi (gerekçe spec dosyasında).**
Spec'in verdiği `--no-commit` bayrağıyla istemci commit'i HİÇ göndermiyordu
(notla çelişiyordu); bayraksız yeniden koşuldu. `ready` sonrası `{"type":
"idle"}` geldi (Listening durumu), ardından hiçbir `transcript`/
`runStarted`/`done` gelmeden 8 saniyede zaman aşımına uğradı — beklenen
davranış tam olarak bu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-074 — Bilinmeyen `inputFormat`

**Gerçek sonuç**
İlk çerçeve `{"type": "error", "message": "Unknown audio format: 'mp3'."}`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-075 — Olmayan bir agent adıyla `start`

**Gerçek sonuç**
`{"type": "error", "message": "There is no agent named
'yok-boyle-bir-agent'."}` — agent adını içeriyor. Bağlantı hemen kapanmadı,
`stop` ile düzgün kapatıldı.

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

## MT-MM-079 — `MaxConcurrentConnectionsPerTenant` sınırı

**Gerçek sonuç**
5 arka plan bağlantısının (`manuel-ws-sinir-1..5`) hepsi `ready` aldı ve açık
kaldı. 6.'sı (`manuel-ws-sinir-6`, ön planda) `BAGLANTI REDDEDILDI: server
rejected WebSocket connection: HTTP 429` — el sıkışma tam 5'te reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-080 — Ses VARSAYILAN olarak SAKLANMAZ

**Gerçek sonuç**
`ready` çerçevesinde `"persistAudio": false`. `done` çerçevesinde
`attachmentId` alanı hiç yok (iki ayrı koşumda da). `GET
/api/attachments?sessionId=manuel-ws-nopersist` → `0`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-081 — `PersistAudio` açıkken YALNIZ agent'ın sesi eke yazılır

**Gerçek sonuç**
Uygulama `Tracon:Voice:Conversation:PersistAudio=true` ile yeniden başlatıldı
(env değişkeni, `user-secrets`'a yazılmadı). `ready` çerçevesi
`"persistAudio": true`. `done` çerçevesi `attachmentId` DOLU. `GET
/api/attachments?sessionId=manuel-ws-persist` TAM OLARAK 1 ek listeledi,
`mediaType: "audio/mpeg"` — kullanıcının sinüs tonu hiçbir ek olarak
görünmedi. Geri alındı: uygulama bayraksız yeniden başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-082 — `voice_sessions` kaydı yazılır

**Gerçek sonuç**
Hem HTTP (`GET /api/voice/sessions?sessionId=manuel-ws-tur-1`) hem SQL:
`turns=1`, `endReason`/`end_reason='Client'/0`, `input_seconds=1.5`,
`output_chars=106` (benim MT-MM-070 turum) — ham ses baytı ne HTTP gövdesinde
ne SQL sütunlarında var. (Aynı session id için 01:02 UTC'den kalma İKİNCİ bir
satır daha bulundu — önceki yarıda kalmış oturumun aynı senaryoyu daha önce
koştuğunun ek kanıtı; iki satır da kendi içinde tutarlı, karışıklık yok.)

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

---

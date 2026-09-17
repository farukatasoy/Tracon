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

## MT-MM-086 — Canlı transkript/altyazı arayüzde akar

**Gerçek sonuç**
Gerçek mikrofon/konuşma gerektiriyor — bkz. §4.3 fiziksel eylem tablosu.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-087 — "Interrupt" düğmesi

**Gerçek sonuç**
Gerçek mikrofon/konuşma gerektiriyor — bkz. §4.3 fiziksel eylem tablosu.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-088 — `persistAudio` açıkken görünür bir rozet belirir

**Gerçek sonuç**
Playwright tarayıcısı bu oturum sırasında BAŞKA BİR ŞERİDİN kullanımındaydı
(`Browser is already in use for .../mcp-chrome-3eca5a9`) — paylaşılan
kaynağa dokunulmadı, case ertelendi. Not: koşulacaksa önce uygulama
`Tracon:Voice:Conversation:PersistAudio=true` ile başlatılmalı.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-MM-090 — i18n/tema hızlı geçiş kontrolü

**Gerçek sonuç**
Playwright tarayıcısı bu oturum sırasında başka bir şeridin kullanımındaydı,
ertelendi (bkz. MT-MM-088).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

## MT-MM-092 — `includeTimestamps: true` karakter hizalaması döner

**Gerçek sonuç**
`HTTP: 200`. `alignment` TAM 7 öge taşıyor — "Merhaba" metninin karakter
sayısıyla birebir (kelime değil, karakter). Her öge `character`/`start`/`end`
taşıyor; `start`/`end` artan sırada (`00:00:00` → `00:00:00.929`), son
`end` (0.929s) sesin gerçek süresine yakın.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-093 — Akışlı sentezde `includeTimestamps: true` açıkça reddedilir

**Gerçek sonuç (izlek C)**
`dotnet test tests/Tracon.Voice.UnitTests --filter "..."` — filtre yine
`Microsoft.Testing.Platform` tarafından yok sayıldı (aynı MT-MM-083 tuzağı),
TÜM `Tracon.Voice.UnitTests` koştu: **68/68 geçti, 0 başarısız** (223ms).
`Streaming_synthesis_rejects_IncludeTimestamps_explicitly` testi
`ElevenLabsSpeechClientTests.cs`'te mevcut ve set genelinde başarısızlık
yok — case'in bar'ı karşılanıyor.

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

## MT-MM-099 — Tur üretmeyen bir `commit` paneli asmaz

**Gerçek sonuç**
Playground UI (Playwright) gerektiriyor, tarayıcı başka şeritçe kullanımda —
ertelendi (bkz. MT-MM-088). Not: bu case'in SUNUCU tarafı zaten MT-MM-073
ile dolaylı doğrulandı (`idle` çerçevesi, `done` YOK); yalnız panelin görsel
"asılı kalmama" davranışı gözlenemedi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-100 — `list_voices` ve `GET /api/voice/voices` attribute taşır

**Gerçek sonuç**
21 sesin 21'i de `attributes` nesnesi taşıyor (hiçbiri eksik değil), 21'inde
de `gender` anahtarı var, boş `attributes` nesnesi yok (hepsi dolu — bu
hesapta `labels`'ı boş bir ses yok, o dal gözlenemedi ama alan sözleşmesi
doğru).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-101 — Sağlayıcı üstverisi `preview_url`/anahtar sızdırmaz

**Gerçek sonuç**
Tam yanıt gövdesi tarandı: `preview_url` alanı yok. Gerçek `Tracon:Voice:ApiKey`
değeri (51 karakter) yanıtın hiçbir yerinde geçmiyor (Python'da string
`in` testiyle doğrulandı, anahtar hiçbir yere yazdırılmadı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-102 — Model `list_voices` çıktısında gender bilgisini görür

**Gerçek sonuç**
"Hangi kadın sesler var?" istemine model YALNIZ `female` etiketli 7 sesi
listeledi (Alice, Bella, Jessica, Laura, Lily, Matilda, Sarah) — gender
bilgisini doğru kullandı. Spec'in örnek biçimi (`— female` son eki) yerine
model kendi doğal dil özetini üretti, ama ALTTA YATAN veriyi doğru filtreledi
— bu case'in asıl iddiası (gender bilgisinin modele ULAŞTIĞI ve
KULLANILDIĞI) karşılandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-103 — Ses sağlayıcısı yokken `list_voices` İngilizce mesaj döner

**Gerçek sonuç — canlı yol koşulamadı, otomatik test kanıtı kullanıldı.**
Uygulama `Tracon:Voice:ApiKey=""` (açık boş override — Development modunda
paylaşılan `user-secrets`'ı sessizce okuma tuzağına düşülmeden, skill §1.2)
ile yeniden başlatıldı; `GET /api/voice/voices` doğru şekilde `501` verdi ve
`GET /api/tools` listesinde `list_voices` HİÇ YOKTU (Voice kayıtlı değilken
tool da kayıtlı değil). Kod tabanının kendi `voice-assistant` agent'ı da bu
durumda `404 Agent not found` verdi (tool'ları çözülemiyor). Sonuç: bu
case'in "tool'u çağır" adımı canlı olarak imkânsız — `list_voices` hiç
YOKKEN çağrılamaz, yalnız "sıfır ses döndüren SAHTE `ISpeechSynthesizer`"
alternatif ön koşulu bu case'i test edilebilir kılar ve o kod donukken
enjekte edilemez. Otomatik kanıt kullanıldı: `ListVoicesToolTests.cs`
`"No voices available."` iddiasını taşıyor ve MT-MM-093'te TAM koşulan
`Tracon.Voice.UnitTests` paketinin içinde (68/68 geçti).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-104 — 50'den fazla ses varken kalan sayı satırı İngilizce'dir

**Gerçek sonuç — canlı koşulamadı (ortam sınırı, MT-MM-050'de zaten not
düşüldü).** Bu hesapta yalnız 21 gerçek ElevenLabs sesi var,
`MaxListedVoices=50` altında — kısaltma satırı gerçek sağlayıcıyla asla
tetiklenmez. `ListVoicesToolTests.cs` aynı dosyada "... and N more voices."
iddiasını taşıyor ve MT-MM-093'ün tam koştuğu pakette (68/68 geçti).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-105 — Kaynak dili kapısı yeşildir

**Gerçek sonuç**
`dotnet test tests/Tracon.Core.UnitTests --filter "..."` — filtre yine yok
sayıldı (MT-MM-083/093 tuzağı), TÜM paket koştu: 2805/2805 geçti.
`source-language-baseline.txt` yalnız 4 satır (üstbilgi yorumları), hiçbir
dosya girdisi yok — taban çizgisi boş, büyümedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-106 — Ses yolu Native AOT'ta çalışır

**Gerçek sonuç — ortam temizliği gerekti.** İlk koşum
`❌ Extension sample contract ihlal edildi: release feed contains stale
Tracon packages ...0.789.nupkg` ile `exit=1` verdi — kök neden AOT/ses ile
İLGİSİZ: `ap-s1`'in kendi `artifacts/package/release/` dizininde ÖNCEKİ bir
oturumdan (16 Eylül 20:07, muhtemelen dosya 01 paketleme turu) kalma
`0.0.0-preview.0.789` paketleri, bu koşumun ürettiği `.827` paketleriyle
`release_extension_samples.verify()`'ın taze/bayat kontrolünü tetikledi.
Yalnız kendi worktree'imin üretilen `artifacts/` dizini (git-izlenmeyen
derleme çıktısı) temizlendi, hiçbir kaynak dosyasına dokunulmadı. Temiz
feed ile yeniden koşuldu: `python3 scripts/kapi.py yayin --kuru` **başarıyla
bitti** (exit=0) — AOT smoke adımı `release_extension_samples.verify()`
içinde çalıştı ve ses yolunu (yeni `Dictionary<string,string>` serileştirmesi
dahil) hatasız geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-107 — Var olan özel `ISpeechSynthesizer` uygulaması değişmeden derlenir

**Gerçek sonuç — ayrı bir harici proje kurulmadan, mevcut bir tüketiciyle
doğrulandı.** `tests/Tracon.Ui.E2ETests/Infrastructure/StubSpeechSynthesizer.cs`
`ISpeechSynthesizer`'ı uyguluyor ve `ListVoicesAsync`'te `new
VoiceDescriptor { VoiceId = ..., Name = ..., Category = ... }` yazıyor —
`Attributes` alanını HİÇ ayarlamıyor (bu dosya `Attributes` eklenmeden ÖNCE
yazılmış, hiç güncellenmemiş). Kaynak: `VoiceDescriptor.Attributes` `{ get;
init; } = ReadOnlyDictionary<string,string>.Empty` — `required` DEĞİL,
varsayılanlı. Tüm çözüm (`dotnet build`, bu turun onlarca `dotnet test`
koşumu) sıfır uyarıyla derleniyor — bu eski nesne başlatıcı hâlâ hatasız
derleniyor, tam case'in iddiası.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-108 — `UseOpenAILive()` çağrılmadan canlı oturum ucu 501 döner

**Gerçek sonuç — canlı deneme örnek uygulamanın gerçek yapılandırmasıyla
ÇELİŞTİ, otomatik test kanıtı kullanıldı.** `samples/Tracon.Api/Program.cs`
`UseOpenAILive()`'ı OpenAI anahtarı varken KOŞULSUZ çağırıyor — bu turda
her zaman öyle. Canlı deneme (`POST .../voice/live/sessions` sahte SDP ile)
beklenen `501` yerine `502 "provider status 400"` verdi (gerçek sağlayıcıya
gitti, sahte SDP'yi reddetti) — bu case'in ön koşulu (`UseOpenAILive`
çağrılMAmış) donuk `samples/`'ta yeniden üretilemez. `LiveVoiceRegistrationTests.
With_UseLiveVoice_but_no_provider_the_answer_is_501` bu TAM senaryoyu
kapsıyor ve `Tracon.AspNetCore.FunctionalTests`'in bu oturumda TAM koştuğu
pakette (1077/1077 geçti).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-109 — `UseLiveVoice()` da çağrılmadan adres hiç yoktur

**Gerçek sonuç — otomatik test kanıtı (aynı gerekçe, MT-MM-108).**
`LiveVoiceRegistrationTests.Without_UseLiveVoice_the_route_does_not_exist`
bu case'i birebir kapsıyor, aynı 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-110 — Gerçek GPT-Live oturumu

**Gerçek sonuç**
👤 Gerçek insan/mikrofon veya tarayıcı gerektiriyor — §4.3 fiziksel eylem
tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-111 — Devredilen iş gerçek bir `runs` satırı üretir

**Gerçek sonuç — otomatik test kanıtı.** MT-MM-110 (canlı önkoşulu) fiziksel
eylem gerektirdiği için bu case de canlı koşulamadı.
`LiveVoiceTests.A_delegation_becomes_a_real_run_and_its_answer_is_spoken_back`
aynı iddiayı (gerçek `runs` satırı, `usage` dolu, tool çağrısı kayıtta,
sesli yanıt) kapsıyor, 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-112 — Konuşma dökümü oturum geçmişinde görünür

**Gerçek sonuç — otomatik test kanıtı (MT-MM-110'a bağımlı, canlı koşulamadı).**
`LiveVoiceLifecycleTests.The_transcript_is_written_to_the_session_history_when_persistence_is_on`
aynı iddiayı kapsıyor, 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-113 — `PersistTranscript=false` iken geçmişe yazılmaz

**Gerçek sonuç — otomatik test kanıtı (MT-MM-110'a bağımlı).**
`LiveVoiceLifecycleTests.The_transcript_is_NOT_written_when_persistence_is_off`
ve `LiveVoicePrivacyTests.With_PersistTranscript_off_delegation_STILL_works`
ikisi birden bu case'in tam iddiasını (geçmiş yok, delegation yine çalışır)
kapsıyor, 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-114 — Kapanan oturumun kaydı süreyi ve maliyeti taşır

**Gerçek sonuç — otomatik test kanıtı (MT-MM-110'a bağımlı).**
`LiveVoiceTests.The_session_record_carries_the_provider_the_model_and_the_duration_the_provider_reported`
kapsıyor, 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-115 — Fiyat yapılandırması yokken `cost` `null` döner

**Gerçek sonuç — otomatik test kanıtı (MT-MM-110'a bağımlı).**
`LiveVoiceTests.With_no_price_configured_the_cost_is_null_NOT_zero` kapsıyor
(K-032'nin canlı katmandaki karşılığı), 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-116 — Hiç bağlanılmayan oturum TTL'de `Abandoned` ile kapanır

**Gerçek sonuç — otomatik test kanıtı (MT-MM-110'a bağımlı).**
`LiveVoiceLifecycleTests.A_session_nothing_ever_connected_to_stays_pending`
ve `A_provider_close_on_a_session_that_never_carried_media_is_ABANDONED`
bu iddiayı kapsıyor, 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-117 — Başka kiracının canlı oturumu erişilemez oturumla aynı 404'ü alır

**Gerçek sonuç — otomatik test kanıtı (MT-MM-110'a bağımlı).**
`LiveVoiceAuthorizationTests.Another_tenants_session_cannot_be_closed_or_read`
ve `A_denied_caller_is_told_the_session_does_not_exist` bu iddiayı (aynı
404, ayırt edici bayt yok) kapsıyor, 1077/1077 geçen pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-118 — Eşzamanlılık limiti sağlayıcıya gitmeden reddeder

**Gerçek sonuç — otomatik test kanıtı (MT-MM-110'a bağımlı).**
`LiveVoiceAuthorizationTests.The_concurrency_limit_answers_BEFORE_the_provider_is_called`
ve `The_limit_is_per_tenant` bu iddiayı birebir kapsıyor, 1077/1077 geçen
pakette.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-119 — Seçenek A regresyon çiti

**Gerçek sonuç**
Sunucu tarafı davranışı (`ready → ses → transcript → done`) MT-MM-070'te
CANLI koşuldu ve tam bu sırayla geçti — bu case'in asıl iddiası (Faz 29
davranışı canlı katmandan bağımsız DEĞİŞMEDİ) zaten doğrulandı. Ek olarak
`LiveVoiceRegistrationTests.The_live_layer_is_independent_of_the_conversation_layer`
iki katmanın birbirini gerektirmediğini kod düzeyinde kanıtlıyor, 1077/1077
geçen pakette.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-120 — Görsel üretimi açıkken sağlayıcı kayıtlı değilse uygulama BAŞLAMAZ

**Gerçek sonuç**
Bu case `samples/Tracon.Api/Program.cs`'te GEÇİCİ bir satır değişikliği
istiyor (`UseOpenAIImages()`'ı yorum satırı yapmak) — kural 1 koşum
sırasında `src/`/`samples/`/`tests/` altında HİÇBİR dosyanın
değişmemesini şart koşuyor. Spec'in kendi notu da otomatik bir karşılığın
OLMADIĞINI söylüyor ("yalnız gerçek bir host başlatmasıyla ölçülür").
Kapanışa ertelendi (`MT-PG-067` ile aynı yordam).

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-121 — `UseGoogleImages()` Google üreticisini kaydeder

**Gerçek sonuç**
Aynı gerekçeyle (`Program.cs` geçici düzenleme gerektiriyor) kapanışa
ertelendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

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

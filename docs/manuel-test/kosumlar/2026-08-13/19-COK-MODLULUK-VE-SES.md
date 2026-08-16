# 19 — Çok Modluluk: Ek, Ses Tool'ları ve Gerçek Zamanlı Konuşma (`MM`) — Koşum Kaydı (2026-08-13)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir** (Faz 58.3 ayrımı).
> Spesifikasyon: [`../../19-COK-MODLULUK-VE-SES.md`](../../19-COK-MODLULUK-VE-SES.md) — `Ön koşul`, `Adımlar`,
> `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-08-13** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır. İkinci bir koşum bu dosyayı **ezmez**; kardeş bir
> `kosumlar/<tarih>/` dizini açar.

---

## MT-MM-001 — Geçerli bir PNG yüklenir, sihirli bayttan doğru tür çıkarılır

**Gerçek sonuç**
`HTTP: 201`. `mediaType:"image/png"`, `byteSize:68`, `sha256` 64 hex karakter,
`id:019ff9fb-fc46-7f0b-b198-47053ad21ee6` bir GUID.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-002 — İstemcinin bildirdiği yanlış `Content-Type` sihirli bayt tarafından GEÇERSİZ kılınır

**Gerçek sonuç**
`image/png` yazdırıldı — istemcinin `text/plain` iddiası yok sayıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-003 — Yürütülebilir/tanınmayan içerik reddedilir

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek turu reddedildi"`, detay desteklenen türleri listeledi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-004 — Boş dosya reddedilir

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek bos olamaz"`, detay `'file' alani bos.`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-005 — `AgentPrismAttachmentOptions.MaxBytes` (varsayılan 20 MB) aşımı reddedilir

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek cok buyuk"`, detay `20971529 bayt; sinir 20971520 bayt.`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-006 — MP3 çerçeve senkronu BİT MASKESİYLE tanınır — beş geçerli varyant kabul, iki `reserved` varyant ret

**Gerçek sonuç**
Yedi satırın tamamı beklenen sonuçla `OK` eşleşti: `fb/f3/f2/fa/e3` → `HTTP=201`,
`e8-ayrilmis-surum`/`e1-ayrilmis-katman` → `HTTP=400`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Ek İndirme, Listeleme, Silme, Kiracı Yalıtımı (Faz 14)

---

## MT-MM-010 — İndirme doğru başlıklarla ve bayt-bayt eşleşmeyle döner

**Gerçek sonuç**
Tüm başlıklar tam beklendiği gibi geldi (`Content-Type: image/png`,
`Content-Disposition: attachment; filename="test.png"`, `X-Content-Type-Options: nosniff`,
`ETag` sha256 değeriyle aynı). `diff` sıfır fark bildirdi, `BAYT BAYT AYNI` yazdırıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-011 — Listeleme `sessionId` ile filtreler; `skip`/`take` sınırlanır

**Gerçek sonuç**
İlk `curl` `1` yazdırdı. İkinci istek `HTTP: 200` döndü — kırpma çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-012 — Silme sonrası indirme VE ikinci silme `404` döner

**Gerçek sonuç**
Sırasıyla `204`, `404`, `404` geldi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-013 — Başka kiracının ekine erişilemez — "yok" gibi yanıtlanır

**Gerçek sonuç**
İlk deneme (yanlış `X-Tenant-Id` başlığıyla, çok kiracılık kapalı) `HTTP: 200`
döndü — sapma değil, doküman kusuruydu: gerçek başlık adı `X-AgentPrism-Tenant`
(`HttpTenantContext.cs:50`), `X-Tenant-Id` sunucu tarafından hiç okunmuyor ve
sessizce yok sayılıyor. Girilecek veri düzeltildi (yukarıda not edildi),
çok kiracılık `AgentPrism:Tenancy:Enabled`/`AllowHeaderResolution` ile açılıp
doğru başlıkla tekrar koşuldu: `HTTP: 404` — beklenen davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-014 — Oturum silinince ekleri de gider (kaskad, yabancı anahtar OLMADAN)

**Gerçek sonuç**
İlk denemede yalnız bir ek yüklemek (`POST /api/attachments?sessionId=...`)
gerçek bir `sessions` kaydı OLUŞTURMUYOR — `DeleteSessionAsync` `sessions.DeleteSessionAsync`
`false` dönünce `404` veriyor (`SessionEndpoints.cs:188-194`). Bu, ön koşulun eksik
tarifiydi: bir oturumun var sayılması için önce gerçek bir agent çalıştırması
gerekiyor. `POST /api/agents/support/run` ile `sessionId=musteri-42` üzerinden
bir tur çalıştırılıp SONRA ek eklendi; bu sırayla silme `204`, listeleme `0`
döndü — beklenen davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Çalıştırmaya Ek Bağlama — Uçtan Uca (Faz 14)

---

## MT-MM-020 — Bir ek + mesajla çalıştırma; modele giden GERÇEK içerik `DataContent`'tir

**Gerçek sonuç**
`HTTP: 200`, akış hatasız `done` ile bitti, model boş olmayan bir metin üretti.
`GET /api/runs/{runId}` → `status:"Completed"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-021 — Var olmayan bir ekle çalıştırma → `400`, akış hiç BAŞLAMAZ

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek bulunamadi"`, düz JSON `ProblemDetails` (SSE değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-022 — Başka kiracının eki çalıştırmada kullanılamaz

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek bulunamadi"` (doğru başlık `X-AgentPrism-Tenant` ile,
bkz. MT-MM-013 doküman düzeltmesi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-023 — `message` BOŞ ama `attachmentIds` doluysa istek GEÇERLİDİR

**Gerçek sonuç**
`HTTP: 200`, akış hatasız `done` ile bitti — `message` boş olsa da yalnız
`attachmentIds` dolu olması yeterliydi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — OpenAI Uyumlu Uçlarda Gömülü `data:` URI (Faz 14, K-116)

---

## MT-MM-026 — `/v1/responses` gömülü `data:` URI'yi eğe çevirir

**Gerçek sonuç**
`HTTP: 200`. `GET /api/attachments` listesinde `sessionId` yanıtın `resp_...`
kimliğiyle eşleşen, `mediaType:"image/png"`, `byteSize:68` yeni bir kayıt
oluştu — gömülü base64 ayrı bir `attachments` satırına çözüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-027 — `/v1/chat/completions` görsel girdiyi KABUL ETMEZ

**Gerçek sonuç**
`HTTP: 200`. Model "Resmi göremiyorum, lütfen görseli yükle" yanıtı verdi —
görsel parça modele hiç ulaşmadı, istek reddedilmedi. Beklenen kapsam dışı
davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-028 — Beyaz listede olmayan bir `data:` türü reddedilir

**Gerçek sonuç**
`HTTP: 400`, `"Dosya turu taninmadi. ..."` — beklenen davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Ses Sağlayıcı Yapılandırması: Açılış Doğrulaması (Faz 28)

---

## MT-MM-031 — `AgentPrism:Voice:ApiKey` yoksa `/api/voice/*` `501`, konuşma ucu `404` döner

**Gerçek sonuç**
Birinci istek beklendiği gibi `HTTP: 501`. İkinci istek (`$APB` ile, dokümanın
kendi komutuyla) `HTTP: 404` DEĞİL, `HTTP: 401` `{"title":"Kimlik dogrulanamadi",
"detail":"Gecerli bir 'Authorization: Bearer <token>' basligi gerekiyor."}`
döndü — GEÇERLİ bir bearer token verilmesine rağmen.

Kök neden: `UseVoiceConversation()` çağrılmadığı için `/api/voice/sessions/{id}/stream`
gerçekten kayıtlı değil (kod beklendiği gibi çalışıyor), istek
`api/`-önekli yollar için `UiEndpoints.ServeAsync`'in yakalayıcı (`{**path}`)
rotasına düşüyor (`UiEndpoints.cs:44-46,66-74`) ve orada `NotFound` (404)
üretiliyor — AMA yalnız `Authorization` başlığı BOŞSA. Bu grup
`AgentPrismEndpointFilter(options, requireBearerToken: false)` ile korunuyor
(`AgentPrismEndpointRouteBuilderExtensions.cs:294`); `requireBearerToken: false`
olunca `_authToken` `null` olarak ayarlanıyor (`AgentPrismEndpointFilter.cs:57`).
Başlık BOŞ değilse filtre statik `AuthToken`'ı HİÇ karşılaştırmıyor
(`_authToken is {Length: >0}` `false` olduğu için `93. satır` atlanıyor),
doğrudan `IApiKeyStore` üzerinden bir API anahtarı arıyor
(`AgentPrismEndpointFilter.cs:100-131`); statik bearer token kayıtlı bir API
anahtarı OLMADIĞI için arama boş dönüyor ve `134. satır`daki genel
`Unauthorized()` tetikleniyor — mesaj "gecerli bir token gerekiyor" der ama
tam olarak geçerli olan statik token zaten sağlanmıştı. Doğrulama: aynı
başlıkla kayıtlı bir rotaya (`/api/voice/health`) istek atıldığında `501`
düzgün dönüyor (bearer token orada normal şekilde denetleniyor); sorun yalnız
eşlenmemiş `api/*` yollarında ortaya çıkıyor — rastgele bir yol da
(`/api/totally-made-up-path-xyz`) aynı `401`i veriyor, yalnız bu uca özgü
değil. **Kusur — HATA-S1-014, Önem: Orta** (bkz. şerit sonuç dosyası).
`/api/voice/sessions` (§8 doğrulaması, aynı ön koşulda) beklendiği gibi
`HTTP: 200`, `[]` döndü — MT-MM-043 bu adımla birleştirildi ve GEÇTİ.

**Ön koşulu geri aldım:** `AgentPrism__Voice__ApiKey` gerçek ElevenLabs
anahtarıyla ayarlanıp uygulama yeniden başlatıldı (bkz. koşum notu).

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-014 düzeltmesiyle (K-395) yeniden koşuldu: doğru statik token artık 404, yanlış token 401. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MM-032 — `pcm_*`/`ulaw_*`/`alaw_*` çıktı biçimleri AÇILIŞTA reddedilir

**Gerçek sonuç**
Açılış `OptionsValidationException` ile çöktü (exit code 134), mesaj:
`'pcm_16000' bicimi ek olarak saklanamaz. ...`. Şerit izolasyonu gereği
`AgentPrism__Voice__OutputFormat` ortam değişkeni kaldırılıp normal
konfigürasyonla yeniden başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-033 — Tanınmayan sağlayıcı adı AÇILIŞTA reddedilir

**Gerçek sonuç**
Açılış çöktü (exit code 134), mesaj: `'azure-cognitive-speech' saglayicisi
taninmiyor. Yerlesik saglayici: 'elevenlabs'. ...`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-034 — `MaxCharactersPerRequest`/`MaxConcurrentRequests` sıfır veya negatif AÇILIŞTA reddedilir

**Gerçek sonuç**
Açılış çöktü (exit code 134), mesaj: `eszamanli istek siniri sifirdan buyuk
olmalidir.`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-035 — Hatalı bir API anahtarıyla hata mesajı anahtarı SIZDIRMAZ

**Gerçek sonuç**
`isHealthy:false`, `detail:"Ses listesi alinamadi: HTTP 401. API anahtari
gecersiz."` — sahte anahtar metni yanıtta hiç görünmedi. Sunucu logu
(`grep -c "SAHTE-GECERSIZ-ANAHTAR-xyz789"`) `0` sonuç verdi — anahtar loglara
da sızmadı. Gerçek ElevenLabs anahtarı geri ayarlanıp uygulama yeniden
başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Ses HTTP Uçları (Faz 28)

---

## MT-MM-038 — `GET /api/voice/voices` gerçek ses listesini döner

**Gerçek sonuç**
İlk denemede `HTTP: 500` (`AgentPrismException: Ses listesi alinamadi: HTTP 401.
API anahtari gecersiz.`) — koşum hatası: sunucu, MT-MM-035'in sahte anahtarıyla
başlatılmış eski bir işlemdi (yeniden başlatma komutu `pgrep -f
"AgentPrism.Api.dll"` ile eşleşmedi, `dotnet run` apphost'u macOS'ta farklı bir
süreç adıyla listeleniyor; eski süreç asla ölmedi). PID'yi doğrudan `kill -9`
ile sonlandırıp gerçek anahtarla yeniden başlatıldı, `ps eww <pid>` ile ortam
değişkeninin gerçekten değiştiği doğrulandı. Sonrasında `HTTP: 200`, 10 ses
döndü, her öğede `voiceId`/`name` doluydu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-039 — `GET /api/voice/health` ücret ÜRETMEDEN sağlığı ölçer

**Gerçek sonuç**
`isHealthy:true`, `voiceCount:10` (MT-MM-038 ile eşleşiyor), `latency` dolu
(`00:00:00.2277458`), `detail:null`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-040 — `POST /api/voice/speak` metni seslendirir, ek üretir, yanıtta karakter/maliyet döner

**Gerçek sonuç**
`HTTP: 200`. `attachment.mediaType:"audio/mpeg"`, `byteSize:42675`,
`characters:10`, `isEstimated:false`. Bu örnek uygulamada
`AgentPrism:Pricing:Voice` YAPILANDIRILMIŞ (`appsettings.json:193-200`,
elevenlabs/eleven_multilingual_v2 = 110 USD/milyon karakter) — bu yüzden
`cost:0.0011`, `currency:"USD"` doğru hesaplandı (10 × 110e-6 = 0.0011,
eşleşiyor). İndirilen ek gerçek bir MP3: `file` komutu
`MPEG ADTS, layer III, v1, 128 kbps, 44.1 kHz` doğruladı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-041 — `POST /api/voice/speak` boş metinle `400` döner

**Gerçek sonuç**
`HTTP: 400`, `title:"Metin bos"`, detay `'text' alani zorunludur.`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-042 — `POST /api/voice/speak`, `MaxCharactersPerRequest` sınırını artık YEREL OLARAK denetler (düzeltildi)

**Gerçek sonuç**
`HTTP: 400`, `title:"Metin cok uzun"`, detay `Metin 6000 karakter; sinir 5000. ...`
— fix bekleneni yaptı, istek sağlayıcıya gitmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-043 — `GET /api/voice/sessions` konuşma katmanı kapalıyken BOŞ liste döner, `501` DEĞİL

**Gerçek sonuç**
MT-MM-031 ile aynı koşumda (voice kapalı, `UseVoiceConversation()` hiç
çağrılmamışken) `GET /api/voice/sessions` çağrıldı: `HTTP: 200`, gövde `[]`
— `501` DEĞİL, boş liste. Beklenen davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Ses Tool'ları: `speak` / `transcribe` / `list_voices` (Faz 28, gerçek çalıştırma)

---

## MT-MM-046 — `speak` gerçek bir çalıştırmada çağrılır, ek `session_id`'si DOLUDUR (G1)

**Gerçek sonuç**
İlk denemede `speak` tool çağrısı `Error: Function failed.` ile başarısız
oldu — sunucu logunda kök neden: `AgentPrismException: Ses uretilemedi:
HTTP 400.` Sebep bu ortama özgüydü: paylaşılan makine-geneli `user-secrets`
deposundaki `AgentPrism:Voice:DefaultVoiceId` değeri (başka/eski bir
ElevenLabs anahtarına ait, bu şeridin env değişkeni bunu hiç override
etmemişti) bu anahtarın hesabında GEÇERSİZ bir ses kimliği taşıyordu.
`AgentPrism__Voice__DefaultVoiceId` ortam değişkeni MT-MM-038'de doğrulanmış
gerçek bir kimlikle (`hpp4J3VqNfWAUOO0d1Us`) override edilip yeniden
başlatıldıktan sonra: akış tamamlandı, `speak` çağrıldı, sonuç
`"Ses uretildi. attachmentId=019ffa0e-cebd-7274-9547-8cdb2a2ede54, ..."`
(ham ses yok). SQL sorgusu (düzeltilmiş, `tool_name` sütunu olmadan — bkz.
not) tek satır döndü: `session_id='manuel-mm-speak-2'` DOLU, `run_id` DOLU.

> **Doküman düzeltmesi:** Doğrulama sorgusundaki `tool_name` sütunu
> `attachments` tablosunda YOK (`\d attachments` doğrulandı — sütunlar:
> id/tenant_id/session_id/run_id/file_name/media_type/byte_size/sha256/
> content/external_uri/created_by/created_at). Sorgudan çıkarıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-047 — `speak` — `MaxCharactersPerRequest` aşımı tool İÇİNDE hata döner, metin KIRPILMAZ

**Gerçek sonuç**
İki denemede de model `speak` tool'unu çağırdı AMA metni 6000 karaktere
TAMAMLAMADI (1096, sonra daha direktif bir istemle 1207 karakterde kesti) —
tool başarıyla ses üretti, sınır hiç tetiklenmedi. Kök neden: `sesli-asistan`
fixture'ının model ayarı `maxOutputTokens:1024` (`GET /api/agents` çıktısı).
6000 karakterlik bir fonksiyon çağrısı argümanı tek bir tamamlamada
1024 çıktı token'ına asla sığmaz — model kaç kez denenirse denensin bu
sınıra token bütçesinden ÖNCE ulaşamaz. Bu, doğrulanabilir bir yapısal
kısıt (fixture ayarı), model isteksizliği değil.

`⏭ ATLA — model tool'u istenen uzunlukta (6000 kr) hiçbir zaman tetikleyemez;
sebep `sesli-asistan` fixture'ının `maxOutputTokens=1024` sınırı`. HTTP ucu
tarafında AYNI sınır MT-MM-042'de doğrudan (model araya girmeden) zaten
doğrulandı — kapsanan davranış orada kanıtlandı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-MM-048 — `transcribe` kayıtlı bir ses ekini metne çevirir

**Gerçek sonuç**
`transcribe` çağrıldı, sonuç: `"[dil=tur] Agent Prism manuel test seslendirmesi"`
— beklenen biçimde, boş olmayan bir metin.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-049 — `transcribe` — ses OLMAYAN bir ekle çağrılırsa hata döner

**Gerçek sonuç**
Model sonucu `"Error: Function failed."` gördü (Microsoft.Extensions.AI'nin
genel sarmalayıcı mesajı); sunucu logunda gerçek istisna tam beklenen metni
taşıyordu: `AgentPrismException: '019ffa11-1c59-7b24-853d-ad453065b03e'
kimlikli ek bir ses dosyasi degil (tur: image/png).`
(`TranscribeTool.cs:74`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-050 — `list_voices` ücret ÜRETMEDEN sesleri listeler

**Gerçek sonuç**
`list_voices` çağrıldı, sonuç `Ad (kimlik) — kategori` biçiminde 10 satır
(hesapta 10 ses var, 50 sınırı tetiklenmedi): `"Bella - Professional, Bright,
Warm (hpp4J3VqNfWAUOO0d1Us) — premade\n..."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Maliyet ve Ölçüm: İki Seslendirme Yolunun Farkı (Faz 28, K-220)

---

## MT-MM-053 — `speak` tool çağrısı `tool_invocations`'a `usage_unit=characters` ile yazılır

**Gerçek sonuç**
Bir satır: `usage_unit='characters'`, `usage_quantity=2` (pozitif),
`usage_estimated=false`. `AgentPrism:Pricing:Voice` bu ortamda yapılandırılmış
olduğundan `cost=0.00022`, `cost_currency='USD'` doldu (2 × 110e-6, doğru
hesaplandı) — sıfır DEĞİL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-054 — `transcribe` tool çağrısı `usage_unit=seconds` ile yazılır

**Gerçek sonuç**
`usage_unit='seconds'`, `usage_quantity=2.6006250000` — küçük test sesiyle
tutarlı bir ondalık.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-055 — `POST /api/voice/speak` (operatör yolu) `tool_invocations`'a HİÇ satır YAZMAZ

**Gerçek sonuç**
Son 30 dakikada `tool_name='speak'` için `5` satır — MT-MM-046'nın 2 başarısız
+ 1 başarılı denemesi, MT-MM-047'nin 2 başarılı denemesi: TOPLAM 5 agent
çağrısıyla BİREBİR eşleşti. Her satırın `run_id` DOLU (FK zorunluluğu ile
tutarlı). MT-MM-040'ın operatör çağrısı (07:34:01, `POST /api/voice/speak`)
bu listede HİÇ YOK — beklendiği gibi hiç eklemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Yerel WebSocket Test Aracı (Faz 29 hazırlığı)

> **Önemli not.** Bu depo hiçbir hazır WebSocket komut satırı istemcisi
> içermez ve manuel testin bir insan tarafından koşulması gerektiği için
> `websockets` Python paketiyle küçük, tekrar kullanılabilir bir istemci
> yazılır. Bu **tester-tedarikli altyapıdır** — AgentPrism deposunun bir
> parçası veya onaylı bir fixture DEĞİLDİR. Beklenen sonuçlar §4.1 kuralına
> uyar: gönderilen ses gerçek konuşma DEĞİL, 440 Hz sinüs tonudur — hiçbir
> case gerçek transkript METNİNE bağlanmaz, yalnızca protokol OLAYLARININ
> doğru sırayla geldiği doğrulanır.

---

## MT-MM-059 — Python WebSocket istemcisini kur

**Gerçek sonuç**
`pip3 install --quiet websockets` hatasız bitti (paket zaten kuruluydu).
İstemci `~/agentprism-manuel-test/voice_client.py` olarak yazıldı — tek
sapma: `--host` varsayılanı bu şeridin portuna göre `localhost:5081`
(dokümandaki `localhost:5080` şerit izolasyonu gereği). `--help` beklenen
argüman listesini eksiksiz gösterdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 10 — Gerçek Zamanlı Konuşma: El Sıkışma ve Güvenlik (Faz 29)

---

## MT-MM-062 — `UseVoiceConversation()` açıksa ama sağlayıcı yoksa `501`; hiç çağrılmadıysa `404`

**Gerçek sonuç**
Dokümandaki `--filter` sözdizimi (VSTest tarzı) bu MTP tabanlı test
çalıştırıcısında hiçbir şeyi filtrelemedi — komut sessizce TÜM 447 testi
koştu (1 kaldı, `ApprovalEndpointTests.Kuyruga_alinan_calistirma_...` —
bu turla ilgisiz, önceden var olan ayrı bir bulgu). Doğru sözdizimi
`-- --filter-query "/*/*/VoiceConversationTests/*"`: 17 test (tüm sınıf)
koştu, `UseVoiceConversation_cagrilmadiysa_HICBIR_uc_acilmaz` VE
`Ses_saglayicisi_yoksa_501_doner` dahil **hepsi Geçti** (17/17).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-063 — WebSocket olmayan bir isteğe `400` döner

**Gerçek sonuç**
Dokümandaki komut (statik `Authorization: Bearer` başlığıyla, `-H "$APB"`)
`HTTP 401` ("Kimlik dogrulanamadi") döndürdü, `400` DEĞİL — `HATA-S1-014`
ile AYNI kök nedene çarpıyor: `voiceGroup` de `requireBearerToken: false`
ile kurulu (`AgentPrismEndpointRouteBuilderExtensions.cs:253` — WebSocket
el sıkışması sırasında tarayıcı `Authorization` başlığı ekleyemediği için
bilinçli tasarım, token yerine WS alt protokolüyle taşınır), bu yüzden
BOŞ OLMAYAN bir `Authorization` başlığı statik token ile hiç
karşılaştırılmadan doğrudan `IApiKeyStore`'da aranıyor, bulunamayınca genel
`401` dönüyor — endpoint'in kendi "WebSocket yukseltmesi gerekiyor" `400`
mantığına hiç ulaşılamıyor. Başlıksız istekte (`Authorization` hiç
verilmeden) beklenen `400` DOĞRU şekilde alındı — ölçüldü ayrıca kanıt
olarak. Bu, önceki oturumun `HATA-S1-014`'ünün (eşlenmemiş `api/*` yolları)
kapsamının MAPLI uçları da (voice conversation grubu) kapsadığını gösteriyor
— aynı kusur, ikinci bir yüzey. Yeni numara açılmadı, `HATA-S1-014`'ün
notuna eklendi.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-014 düzeltmesiyle (K-395) yeniden koşuldu: 400 (WebSocket yukseltmesi gerekiyor) artık dogru token ile de aliniyor. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MM-064 — Token doğruysa alt protokolde KABUL edilir, `ready` çerçevesi gelir

**Gerçek sonuç**
Tam olarak beklendiği gibi: `BAGLANDI, kabul edilen alt protokol:
agentprism.voice.v1`, ilk `<<` çerçevesi
`{"type": "ready", "agent": "sesli-asistan", "sessionId": "manuel-ws-token-ok", "persistAudio": false}`.
Ardından gerçek OpenAI + ElevenLabs uçtan uca çalıştı (`transcript` →
`runStarted` → `text` deltaları → `audioStart` → ikili ses çerçeveleri →
`audioEnd` → `done`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-065 — Token YANLIŞSA el sıkışma REDDEDİLİR

**Gerçek sonuç**
`BAGLANTI REDDEDILDI: server rejected WebSocket connection: HTTP 401` —
tam beklendiği gibi, mesajda beklenen token hakkında hiçbir ipucu yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-066 — 🚨 Token SORGU DİZESİNDE gönderilirse KABUL EDİLMEZ

**Gerçek sonuç**
`BAGLANTI REDDEDILDI: server rejected WebSocket connection: HTTP 401` —
sorgu dizesindeki token tamamen yok sayıldı, alt protokolde
`agentprism.token.*` girdisi olmayınca bağlantı reddedildi. Beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-067 — Başka kiracının oturumuna bağlanmak o kaydı GÖRMEZ; kendi kiracısında taze bir oturum açılır

**Gerçek sonuç**
İlk denemede `AgentPrism__Tenancy__AllowHeaderResolution` bu şeridin
ortamında AÇIK DEĞİLDİ — `X-AgentPrism-Tenant: kiraci-alfa` başlığı hiç
okunmadı, hem ön koşul POST'u hem WebSocket bağlantısı aynı `default`
kiracısına, aynı oturum kimliğine yazdı (kirlenme: 6 mesaj tek oturumda
karıştı, `paylasilan-oturum-id` artık `default` kiracısında bu kirli
durumda duruyor — zararsız, başka case ona bağlı değil). Düzeltme: sunucu
`AgentPrism__Tenancy__Enabled=true` + `AgentPrism__Tenancy__AllowHeaderResolution=true`
ile yeniden başlatıldı (S1-3'ün `23` dosyasında uyguladığı aynı desen) ve
case TEMİZ bir oturum kimliğiyle (`paylasilan-oturum-id-2`) tekrarlandı.
İkinci denemede: `kiraci-alfa` oturumu doğru tenant'ta oluştu
(`tenantId:"kiraci-alfa"`, 2 mesaj). WebSocket `default` kiracısıyla
BAŞARIYLA bağlandı (`ready` çerçevesi geldi) — kendi TAZE oturumunu açtı.
`kiraci-alfa` oturumu WebSocket turu SONRASINDA da `2` mesajda sabit kaldı
— hiç değişmedi. Doküman iddiası doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 11 — Gerçek Zamanlı Konuşma: Protokol Akışı ve Durum Makinesi (Faz 29)

---

## MT-MM-070 — Uçtan uca bir tur: `start → ready → commit → transcript → runStarted → text → audioStart → audioEnd → done`

**Gerçek sonuç**
Sıra tam beklendiği gibi: `ready` → `transcript` (`final:true`,
`text:"[tone]"`) → `runStarted` (`runId: 019ffa21-f876-7f7a-8960-cc93840f40a4`)
→ `text` deltaları → `audioStart` (`audio/mpeg`) → ikili ses çerçeveleri →
`audioEnd` → `done` (`cancelled:false, turn:1`). Sapma: model burada TEK
değil İKİ ayrı `text`/`audioStart`/`audioEnd` döngüsü üretti (agent iki
ayrı `speak` tool çağrısı yaptı — "Seslendirme ister misin? İ" ve
"steren metni gönder." biçiminde bölünmüş bir yanıt). Bu, doğrulanması
istenen SIRAYI bozmuyor (döngü kendi içinde ready→...→done akışına uyuyor,
yalnız `text`/`audioStart`/`audioEnd` üçlüsü tekrarlanıyor) — kusur değil,
gerçek modelin serbest kararı (sinüs tonu anlamsız girdi olduğu için model
davranışı öngörülemez, MT-MM-070/071'in amacı protokol sırasını doğrulamak,
model içeriğini değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-071 — Her tur normal bir `runs` satırı üretir — çalıştırma yolu DEĞİŞMEZ

**Gerçek sonuç**
Bir satır: `status: Completed` (API), `agent_name='sesli-asistan'`,
`model_id='gpt-5.4-mini'`, `input_tokens=392`, `output_tokens=17`,
`session_id='manuel-ws-tur-1'` — hepsi pozitif ve doğru. Not: SQL sorgusu
`status` sütununu ham tamsayı (`1`) döndürüyor, doğrudan enum metni değil
— `GET /api/runs/{id}` üzerinden okundu (`Completed`), doküman sapması
değil, yalnızca ölçüm kolaylığı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-072 — İkinci `start` REDDEDİLİR — agent/oturum/kiracı bağlantı boyunca sabittir

**Gerçek sonuç**
İlk `start` `ready` üretti. İkinci `start` AÇIK bir `error` çerçevesiyle
reddedildi: `{"type": "error", "message": "Konusma zaten baslatildi; agent baglanti boyunca degismez."}`.
Bağlantı kapanmadı — ardından gönderilen `stop` normal şekilde işlendi
(bağlantı kapalı olsaydı istisna fırlardı). Davranış: **açık `error`
çerçevesi**, sessiz yok sayma DEĞİL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-073 — Ses göndermeden `commit` — tur ÜRETİLMEZ, dinlemeye geri döner

**Gerçek sonuç**
Doküman sapması: dokümandaki komut `--no-commit` bayrağı taşıyor ama
kendi yorumu ("aracin kendisi commit gonderir") bununla ÇELİŞİYOR —
`--no-commit` istemcinin commit'i HİÇ GÖNDERMEMESİNİ sağlıyor, yorum
metniyle ters. Dokümanın niyetine (boş ses + commit gönderilip sunucunun
onu sessizce attığını doğrulamak) uymak için `--no-commit` OLMADAN
tekrarlandı (`manuel-ws-bos-commit-2`, `--max-wait-seconds 10`): `ready`
geldi, `commit` gönderildi, ardından 10 saniye boyunca HİÇBİR çerçeve
gelmedi (`ZAMAN ASIMI`) — `transcript`/`runStarted`/`done` YOK. Tam
beklendiği gibi. (`--no-commit` İLE orijinal deneme de aynı sonucu verdi
ama commit hiç gönderilmediği için o deneme geçersizdi — boş bir turun
"süresi dolduğunu" değil, hiç başlamadığını kanıtlıyordu.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-074 — Bilinmeyen `inputFormat` → `error` çerçevesi

**Gerçek sonuç**
`{"type": "error", "message": "Bilinmeyen ses bicimi: 'mp3'."}` — tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-075 — Olmayan bir agent adıyla `start` → `error` çerçevesi, agent adını içerir

**Gerçek sonuç**
`{"type": "error", "message": "'yok-boyle-bir-agent' adinda bir agent yok."}`
— agent adını içeriyor, tam beklendiği gibi. `stop` düzgün gönderildi,
bağlantı zaten kapalı değildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 12 — Gerçek Zamanlı Konuşma: Kesinti, Sınırlar, Kayıt (Faz 29)

---

## MT-MM-078 — Kesinti (`cancel`) çalıştırmayı `Canceled` yapar; yarım yanıt geçmişe "kesildi" notuyla yazılır

**Gerçek sonuç**
İlk yarı doğrulandı: `audioStart` gelir gelmez `cancel` gönderildi, `done`
çerçevesi `cancelled: true, turn: 1` taşıdı, `GET /api/sessions/...`'in son
asistan mesajı `[Yanit kullanici tarafindan kesildi.]` dizgisini içeriyordu.
**SQL/API doğrulaması BAŞARISIZ oldu** → `HATA-S1-015` (Yüksek, yeni). `GET
/api/runs/<runId>` 15+ saniye sonra bile `status: "Running"`,
`completedAt: null`, `eventCount: 0`, `usage: null` döndürdü — run KALICI
OLARAK "Running" durumunda asılı kaldı, `Canceled`'a HİÇ geçmedi. Kök neden
kod okumasıyla bulundu: `RunRecordingAgent.RunCoreStreamingAsync`
(`src/AgentPrism.Core/Recording/RunRecordingAgent.cs:255-370`) `CompleteAsync`
çağrısını (hem `Completed` yolu satır 366 hem `Canceled` yakalayıcısı satır
323-327) yalnız İKİ yerde tetikler: (a) `enumerator.MoveNextAsync()`
`OperationCanceledException` fırlatırsa (satır 304-333'teki iç try/catch),
(b) döngü doğal olarak biterse (satır 353'ten SONRA, 355-358'deki
`finally`'nin dışında, satır 360-370). Ses turunda kesinti tam bu ikisinin
ARASINDA oluyor: `VoiceConversationDriver.RespondAsync`
(`src/AgentPrism.Core/Voice/VoiceConversationDriver.cs:606-632`) her
`update` alındıktan SONRA (RunRecordingAgent `yield return` ile kontrolü
DRIVER'a devrettikten sonra) `SpeakAsync` (ElevenLabs TTS ağ çağrısı,
satır 629) çağırıyor — kesinti tam bu TTS çağrısı SÜRERKEN geliyor
(`audioStart` zaten gönderilmiş, ses parçaları akıyor). `_turnCancellation.Cancel()`
bu noktada `SpeakAsync`'i (driver kodu, RunRecordingAgent'ın DIŞINDA) iptal
ediyor; `RespondAsync`'in `await foreach` döngüsü bir istisnayla çıkıyor,
bu da RunRecordingAgent'ın `updates` numaralandırıcısını ERKEN
`DisposeAsync()` ile kapatıyor — C#'ın async-iterator kuralına göre bu yalnız
298-358 arasındaki `finally` bloğunu (numaralandırıcının kendi
`DisposeAsync`'i) çalıştırır, 360+ satırındaki (döngüden SONRAKİ) `CompleteAsync`
çağrısına HİÇ ULAŞILMAZ — ne `Completed` ne `Canceled` yazılır, run
sonsuza dek `Running` kalır. Etki: yalnız durum yanlış değil — gerçek
OpenAI (kısmi metin akışı) ve ElevenLabs (üretilen ses parçaları) maliyeti
GERÇEKTEN oluştu ama `usage`/`cost`/`quota_usage` HİÇ kaydedilmedi (sessiz
veri kaybı). Bu, kesintiyi TETİKLEYEN her akan (`streaming`) çalıştırma
için genel bir risktir (yalnız ses'e özgü olmayabilir) — döngü gövdesinde
bir `yield return` SONRASI, bir sonraki `MoveNextAsync`'ten ÖNCE herhangi
bir istisna/iptal tüketiciyi (`consumer`) erken `DisposeAsync`'e
zorlarsa aynı sessiz kayıp oluşur.

 **Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı — S1-8'de HATA-S1-015 düzeltmesiyle (K-398) yeniden koşuldu: run artık status:Canceled ile tamamlaniyor. Bkz. SONUCLAR-S1-2026-08-13.md.

---

## MT-MM-079 — `MaxConcurrentConnectionsPerTenant` sınırı — sınır soket YÜKSELTİLMEDEN önce ayrılır

**Gerçek sonuç**
Tam beklendiği gibi: ilk 5 bağlantının hepsi `ready` aldı, `commit`
gönderilmediği için 20 saniyelik zaman aşımıyla kapandı. 6. bağlantı
`BAGLANTI REDDEDILDI: server rejected WebSocket connection: HTTP 429`
ile anında reddedildi — sınır tam **5**'te uygulanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-080 — Ses VARSAYILAN olarak SAKLANMAZ

**Gerçek sonuç**
Tam beklendiği gibi: `ready` içinde `"persistAudio": false`, `done`
çerçevesinde `attachmentId` alanı yoktu, ikinci komut `0` döndü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-081 — `PersistAudio` açıkken YALNIZ agent'ın sesi eke yazılır — kullanıcının sesi HİÇ saklanmaz

**Gerçek sonuç**
Tam beklendiği gibi: `done` çerçevesinde `attachmentId:
"019ffa29-5c25-7001-9755-d7785927c50b"` doluydu. `GET /api/attachments?...`
TAM OLARAK 1 ek listeledi, `mediaType: "audio/mpeg"`, `byteSize: 46020` —
kullanıcının gönderdiği sinüs tonu hiçbir ek olarak görünmedi. (§2.2 sapması:
`user-secrets` yerine `AgentPrism__Voice__Conversation__PersistAudio=true`
ortam değişkeni kullanıldı.) `PersistAudio` MT-MM-088 için geçici olarak
AÇIK bırakıldı — §13'te tekrar kullanılacak, o bölüm bitince kaldırılacak.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-082 — `voice_sessions` kaydı yazılır — ses İÇERMEZ, `turns`/`endReason` doğrudur

**Gerçek sonuç**
HTTP: `turns:1`, `endReason:"Client"`, `inputSeconds:1.5`, `outputChars:46`.
SQL: aynı değerler doğrulandı (`end_reason` sütunu ham tamsayı `0` — API'nin
`"Client"` metnine karşılık gelen enum değeri). Ne HTTP'de ne SQL'de ham ses
baytı yok — yalnız özet ölçüm. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-083 — Ham PCM çözüme WAV başlığıyla gider — 44 baytlık RIFF başlığı eklenir

**Gerçek sonuç**
Dokümandaki `--filter` sözdizimi burada da (MT-MM-062 ile aynı sebepten)
filtrelemedi ama koşulan 8 test zaten TAMAMI `VoiceUtteranceBufferTests`
sınıfına aitti (`dotnet test ... -- --filter-query "/*/*/VoiceUtteranceBufferTests/*"`
ile teyit edildi) — 8/8 Geçti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 13 — Arayüz: Konuşma Paneli İçi Gerçek Zamanlı Davranış (Faz 29)

> Panelin açılıp kapanma mekaniği ve "Konuştur" düğmesinin dış görünümü
> `10-ARAYUZ-AGENT-PLAYGROUND.md` (`MT-UIAG-049`–`051`) tarafından zaten
> test edildi ve burada TEKRAR EDİLMEZ. Bu bölüm yalnız panelin İÇİNDEKİ
> gerçek zamanlı akışı kapsar.

---

## MT-MM-086 — Canlı transkript ve altyazı, gerçek bir turda arayüzde akar

**Gerçek sonuç**
Ajan Playwright ile panele kadar ulaştı (token girişi, `Sesli Asistan`
seçimi, "Conversation mode" → "Talk" tıklamaları başarılı; bir `sessions`
kaydı oluştu: `conv_019ffa2b7e4a7ab38c9f6080fd3a8a0a`). Ancak panel
`"connecting"` durumunda SONSUZA DEK asılı kaldı — sunucu loglarında bu
oturum için HİÇBİR WebSocket bağlantı denemesi görünmedi (`grep -i voice
/tmp/ap-s1-server.log` boş): tarayıcı `getUserMedia()` sonucunu bekliyor,
ama bu headless Playwright oturumunda GERÇEK bir mikrofon cihazı yok ve
izin istemi hiç görünmedi (sessizce askıda kaldı). Bu case GERÇEK insan
konuşması gerektirdiği için (KOSUM-PLANI §2.4.2, fiziksel eylem) koşulamadı
— §5.3 tablosuna eklendi.

**Gerçek sonuç (S1-9 güncellemesi, 2026-08-13)**
Kök neden "gerçek mikrofon" değil, sahte bir `AgentPrism:Voice:ApiKey`
(`SAHTE-SES-ANAHTARI-xyz789`) idi — gerçek bir anahtarla panel zaten
`ready`'ye ulaşabiliyordu, önceki denemeler mikrofon izni adımında
tıkandığı için bu hiç görülmedi. Kullanıcı gerçek bir ElevenLabs anahtarı
sağladı (doğrulandı: `GET /v1/voices` → `200`, 10 ses); `DefaultVoiceId`
de sahte olduğundan (`ses-tr-1`) gerçek bir ID'ye (`JBFqnCBsd6RMkjVDRZzb`)
güncellendi. "Gerçek insan konuşması" gereksinimi bağımsız bir
Playwright/Node betiğiyle (paylaşılan MCP kaydına dokunulmadı) karşılandı:
Chromium `--use-fake-device-for-media-stream --use-fake-ui-for-media-stream
--use-file-for-fake-audio-capture=<wav>` ile başlatıldı, `wav` macOS
`say -v Yelda -o merhaba.aiff "Merhaba, nasılsın?"` ile üretilip 16 kHz
mono PCM'e çevrildi. Panel `listening`e ulaştı, "Send now"a basıldı, gerçek
STT gerçek kelimeleri transkribe etti: canlı transkript alanında
`"You: Merhaba, nasılsın? ..."` belirdi, ardından model yanıtı
(`"Merhaba! İyiyim, teşekkürler. Sen..."`) altyazı olarak aktı ve ses
otomatik çaldı (`speaking` durumu gözlendi). Konsolda hata yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-087 — Agent konuşurken "Interrupt" düğmesi görünür; basılınca kesinti gerçekleşir

**Gerçek sonuç**
MT-MM-086 ön koşulu koşulamadığı için bu case de koşulamadı — aynı fiziksel
eylem engeli (gerçek mikrofon + gerçek konuşma gerekir). §5.3 tablosuna
eklendi.

**Gerçek sonuç (S1-9 güncellemesi, 2026-08-13)**
MT-MM-086 ile aynı oturumda, `speaking` durumuna geçer geçmez (agent sesli
yanıt vermeye başladığı an) "Interrupt" düğmesi (`voice-interrupt`) görünür
oldu ve tıklanınca panel ANINDA `listening` durumuna döndü. Transcript'teki
yanıt `"Merhaba! İyiyim, teşekkürler. Sen (interrupted)"` şeklinde kesilme
etiketiyle güncellendi (dokümanın beklediği notla eşdeğer görsel işaret —
`t('voice.interrupted')`). Ekran görüntüsüyle doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-088 — `persistAudio` açıkken görünür bir rozet belirir

**Gerçek sonuç**
`PersistAudio=true` zaten açıktı (MT-MM-081'den). Panel açıldı ama
`getUserMedia()` mikrofon iznini gerektirdiği için "connecting" durumunda
takıldı (MT-MM-086 ile AYNI engel) — rozetin göründüğü/görünmediği durum
gözlemlenemedi, çünkü panel `ready` durumuna hiç ulaşmadı. Gerçek mikrofon
izni GEREKTİREN bir fiziksel eylem — §5.3 tablosuna eklendi.

**Gerçek sonuç (S1-9 güncellemesi, 2026-08-13)**
`AgentPrism:Voice:Conversation:PersistAudio` `user-secrets` ile `true`
yapılıp uygulama yeniden başlatıldı (gerçek ElevenLabs anahtarıyla,
bkz. MT-MM-086). Playwright/sahte-mikrofon betiğiyle panel açılıp `listening`
durumuna ulaşıldı: rozet (`voice-recording-notice`) göründü, metni tam
olarak `"Audio of the reply is being stored"` — beklenen temayla birebir
eşleşiyor. Ekran görüntüsüyle doğrulandı. **Geri alındı:** `PersistAudio`
`false`'a döndürülüp uygulama tekrar başlatıldı; `MT-MM-086/087/090`
denemelerinde (aynı oturum, `PersistAudio=false`) rozet hiç görünmedi —
karşılaştırma da doğrulanmış oldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-MM-089 — Güvenli bağlam yoksa panel açılmaz, açık bir mesaj gösterilir

**Gerçek sonuç**
Doğrulandı: `samples/AgentPrism.Api/appsettings.json:31` içinde
`"AllowRemoteAccess": false` — bu şeritte hiç açılmadı. Doküman kendi
belirttiği ⏭ ATLA yoluna göre işaretlendi; geçici olarak açıp tekrar
denemek §2.1'in "kod değiştirilmez" kapsamı DIŞINDA (yalnız config, kod
değil) ama zaman bütçesi + fiziksel eylem gerektiren diğer §13 case'leri
(086-088, 090) zaten Beklemede olduğundan bu oturumda AllowRemoteAccess
açılıp tekrar denenmedi — sonraki fiziksel eylem turunda diğerleriyle
birlikte ele alınmalı.

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☑ Atlandı

---

## MT-MM-090 — i18n/tema hızlı geçiş kontrolü — konuşma paneli metinleri

**Gerçek sonuç**
MT-MM-086 ile AYNI engel: panel `getUserMedia()` mikrofon izni bekliyor,
gerçek mikrofon olmadan "connecting"te takılı kalıyor — panel içi metinler
gözlemlenemedi. Gerçek mikrofon izni GEREKTİREN bir fiziksel eylem —
§5.3 tablosuna eklendi.

**Gerçek sonuç (S1-9 güncellemesi, 2026-08-13)**
Panel `listening` durumundayken (MT-MM-086 akışının başı) dil anahtarı
(`language-toggle`, EN→TR) ve tema anahtarı (`theme-toggle`, açık→koyu)
art arda değiştirildi. Panel KAPANMADI/bozulmadı boyunca: `Talk`/`End
conversation` düğmesi, durum etiketi (`listening`→`dinliyor`), "Send
now"/"Şimdi gönder" düğmesi ve `<html lang>` (`en`→`tr`) hepsi doğru
çevrildi; karışık dil metni yok. Tema koyuya geçince kontrast bozulmadı,
ses seviyesi çubukları okunur kaldı (ekran görüntüsüyle doğrulandı — tüm
kenar çubuğu + gövde koyu temaya geçti, panel açık kaldı). İkisi de eski
hâline döndürüldü, panel sorunsuz kapandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

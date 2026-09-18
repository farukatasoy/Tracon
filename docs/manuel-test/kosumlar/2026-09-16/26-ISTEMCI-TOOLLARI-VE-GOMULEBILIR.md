# 26 — İstemci Tool'ları ve Gömülebilir Sohbet (`IST`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md`](../../26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum`
> kayıtlarıdır; spec `### MT-IST-NNN` (h3) kullanır, burada skill
> §4.1/§7 konvansiyonuna uymak için `## MT-IST-NNN` (h2) kullanılır.

| | |
|---|---|
| **Şerit** | `ap-s4` (Faz B, yedinci aile) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s4` · dal `test/kosum-s4` |
| **Kod** | `5ef711d6` donuk (doğrulandı: `git status --short` boş, `git diff --stat 7e3a4de7..HEAD -- src samples tests` boş) |
| **Case sayısı** | 16 (MT-IST-001..016) |
| **Ana uygulama** | port 5084, şema `mt_s4`, gerçek `openai` Healthy |

**Sapma — `echo` sağlayıcısı bu ortamda YOK:** MT-IST-014'ün kendi Ön koşulu
`{"provider":"echo","model":"echo-1"}` öneriyor; `samples/Tracon.Api/Program.cs`
`echo` sağlayıcısını yalnız `openAiEnabled == false` iken kaydediyor (satır
390-396) — bu şeritte gerçek `openai` açık olduğu için `echo` HİÇ kayıtlı
değil (`POST /api/agents` `"No model provider named 'echo' is registered"`
ile `400` döndü). `manuel-ist-replay` bunun yerine gerçek
`openai`/`gpt-5.4-mini` ile kaydedildi — case'in test ettiği şey (istemci
tool'unun replay'de 409 vermesi) modelden bağımsızdır, küçük bir gerçek
çağrı maliyeti kabul edildi.

**Sapma — MT-IST-003/006'nın kendi curl gövdesi eksik:**
- MT-IST-003 "MT-IST-002'nin isteğini aynen tekrar gönder" diyor; AYNI
  `Idempotency-Key` ile tekrarlamak **idempotency replay**'e düşüyor (`200`,
  `Idempotency-Replayed: true` başlığı) — bu doğru idempotency davranışı,
  ama case'in iddia ettiği "ikinci kez yanıtlama" `409`'unu hiç sınamıyor.
  **YENİ** bir `Idempotency-Key` ile (aynı `callId`, aynı `result`) tekrar
  gönderildiğinde gerçek `409 Tool call already answered` alındı — ürün
  kusuru değil, case'in kendi tekrar tarifi idempotency anahtarını da
  değiştirmesi gerektiğini söylemiyor.
- MT-IST-006 spec gövdesinde `message` alanı YOK; gerçek kod
  (`AgentEndpoints.RunQueuedAsync`, satır 986-1002) önce `message` boşluğunu
  kontrol ediyor, SONRA `toolResults`/kuyruk kısıtını — `message` olmadan
  case'in beklediği "kuyruk kısıtı" mesajı yerine daha genel "'message' is
  required for a queued run" `400`'ü alınır. `message` eklenince beklenen
  kısıt mesajı (`"...does not support...client-side tool results..."`)
  birebir çıktı.

**Sapma — CORS placeholder origin gerçek testte kullanılamadı:** MT-IST-012
tarayıcı testi `http://localhost:8099`'dan sunuldu (gerçek bir üçüncü taraf
alan adı yerine yerel statik sunucu — spec'in kendi önerdiği kurulum);
`AllowedOrigins`'e hem spec'in placeholder'ı (`https://baska-site.example.com`,
MT-IST-011 bunu kullandı) HEM `http://localhost:8099` (MT-IST-012 bunu
kullandı) birlikte eklendi.

**Sapma — `POST /api/agents/{id:guid}`'dan `RunsWrite` kapsamlı bir API
anahtarı üretildi** (`13-KIRACI-VE-GUVENLIK.md`'nin öngördüğü gibi), koşum
sonunda `DELETE /api/api-keys/{id}` ile iptal edildi.

Ana uygulama MT-IST-009/011/012 için `Tenancy:Enabled` +
`Tenancy:AllowHeaderResolution` + `Ui:AllowedOrigins` env değişkenleriyle
yeniden başlatıldı, sonra **temel duruma** (üçü de kapalı) döndürüldü —
doğrulandı: `GET /api/meta` öncesi/sonrası birebir aynı.

## Gerçek para uyarısı

MT-IST-001/002/007/008/009/012/014 gerçek `openai`'ye küçük çağrılar yaptı
(spec'in öngördüğü gibi, `support` üzerinden `gpt-5.4-mini`). MT-IST-
003/004/005/006/010/011/013/015/016 sağlayıcıya gitmedi ya da yalnız daha
önceki bir case'in kaydını okudu.

---

## MT-IST-001 — İstemci tool'u çağrılır ama sunucuda ÇALIŞMAZ

**Gerçek sonuç**
`support`'a `sessionId: mt-ist-001` ile "sepetimde ne var?" gönderildi:
yanıt tek bir `functionCall` (`name: "read_shopping_cart"`) taşıyor,
`functionResult` YOK, `finishReason: "tool_calls"`. `read_shopping_cart`
istemci tool'u olduğu için (`AddClientTool`) mimari olarak sunucu tarafında
hiçbir gövde çalıştırılmıyor — koşacak kod yolu yok, dolayısıyla log izi de
yok (K2).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-002 — `toolResults` ile sonuç gönderilince tur tamamlanır

**Gerçek sonuç**
Aynı `sessionId` ile `toolResults: [{callId, result: "2x Kablosuz Fare"}]`
gönderildi: `200`, yanıt metni "Sepetinizde: 2 adet Kablosuz Fare var."
(sepet içeriğinden bahsediyor), `finishReason: "stop"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-003 — Aynı `callId` ikinci kez yanıtlanır → `409`

**Gerçek sonuç**
Aynı `Idempotency-Key` ile tekrar: `200` + `Idempotency-Replayed: true`
(idempotency katmanı devreye girdi, gerçek iş mantığına hiç uğramadı —
yukarıdaki sapma notuna bakın). **Yeni** bir `Idempotency-Key` ile aynı
`callId`/`result` tekrar gönderildiğinde: `409`,
`{"title":"Tool call already answered","detail":"The client-side tool
call with id '...' already has a result."}` — case'in iddia ettiği davranış
doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-004 — Bilinmeyen `callId` ile sonuç gönderilir → `400`

**Gerçek sonuç**
`400`, `{"title":"Unknown tool call","detail":"There is no pending
client-side tool call with id 'hic-var-olmayan' in this session."}`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-005 — `sessionId` olmadan `toolResults` gönderilir → `400`

**Gerçek sonuç**
`400`, `{"title":"Session required for tool result","detail":"A pending
client-side tool call lives in session history; 'sessionId' is
required."}`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-006 — Kuyruk yolunda (`Prefer: respond-async`) `toolResults` reddedilir

**Gerçek sonuç**
`message` alanı EKLENEREK (yukarıdaki sapma notu) tekrar denendi: `400`,
`{"title":"Not supported","detail":"A queued run ('Prefer: respond-async')
does not support approval decisions, client-side tool results,
attachments, parameters, or documents in this version."}` — kuyruk kısıtını
açıkça anlatıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-007 — `errorMessage` model'e ulaşır, `result` yerine kullanılır

**Gerçek sonuç**
Yeni oturum (`mt-ist-007`), `toolResults: [{callId, errorMessage:
"kullanici izin vermedi"}]`: `200`, model yanıtı "Sepeti göremedim çünkü
erişim izni verilmedi. İsterseniz izni açıp tekrar deneyebilirim." —
`errorMessage` temelinde, `finishReason: "stop"`, tur tamamlandı — `400`
DEĞİL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-008 — İstemciden gelen sonuç guard'dan geçer

**Gerçek sonuç**
`support`'un kalıcı `AddPatternContentGuard` kaydı (`samples/Tracon.Api/
Program.cs:214-218`, yasaklı terim `confidential-project`) kullanıldı.
Yeni oturum, `toolResults.result` yasaklı terimi taşıyor: `422`,
`errorType: "content_blocked"`, `detail: "...The blocked text is
deliberately not recorded."` — yanıt gövdesi yasaklı terimi taşımıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-009 — Başka kiracının oturumuna sonuç yazılamaz

**Gerçek sonuç**
Ana uygulama `Tenancy:Enabled`+`AllowHeaderResolution` ile yeniden
başlatıldı. `X-Tracon-Tenant: tenant-a` ile MT-IST-001 tekrarlandı
(`callId` alındı), sonra AYNI `sessionId`+`callId` ile `X-Tracon-Tenant:
tenant-b` başlığıyla `toolResults` gönderildi: `400`, `"Unknown tool
call"` — `tenant-b`'nin bakış açısından çağrı/oturum hiç yok (aynı
bilinmeyen-`callId` yoluna düşüyor, `tenant-a`'nın bekleyen çağrısına
asla erişilmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-010 — `AllowedOrigins` boşken CORS başlığı yollanmaz

**Gerçek sonuç**
Temel durumda (`AllowedOrigins: []`), `Origin: https://baska-site.example.com`
başlığıyla `GET /api/meta`: `grep -i access-control` çıktısı **boş** —
`Access-Control-Allow-Origin` yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-011 — `AllowedOrigins` açıldığında izin verilen origin geçer

**Gerçek sonuç**
`Tracon__Ui__AllowedOrigins__0=https://baska-site.example.com` ile yeniden
başlatılınca AYNI istek: `Access-Control-Allow-Origin:
https://baska-site.example.com` başlığı döndü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-012 — Gömülebilir bileşen tarayıcıda çalışır ve turu tamamlar

**Gerçek sonuç**
`RunsWrite` kapsamlı bir API anahtarı üretildi (`POST /api/api-keys`),
statik sayfa `http://localhost:8099`'dan sunuldu (spec'in placeholder
alan adı yerine — sapma notuna bakın), `AllowedOrigins`'e bu origin de
eklendi. Playwright ile sayfa açıldı, 💬 balonuna tıklandı, widget "Chat"
paneli açıldı, "sepetimde ne var?" yazılıp gönderildi. **İlk denemede**
`AllowedOrigins` yalnız placeholder alan adını taşırken tarayıcı konsolunda
gerçek bir CORS engeli görüldü (`Access to fetch...has been blocked by CORS
policy`) — bu BEKLENEN bir yapılandırma eksikliğiydi (test sayfasının kendi
origin'i allow-list'te değildi), ürün kusuru değil; `localhost:8099`
eklenip uygulama yeniden başlatılınca ikinci denemede: "Running…" durumu
göründü, ardından nihai yanıt **"Sepetinde: 3x Klavye var."** —
`window.TraconEmbed.registerTool('read_shopping_cart', () => '3x Klavye')`
kayıtlı işlevinin döndürdüğü değerden bahsediyor. Konsolda **sıfır** hata
(CORS dahil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-013 — Konsolun Tools ekranı `runsOnClient` rozetini gösterir

**Gerçek sonuç**
`$APU/tools` Playwright ile açıldı. `read_shopping_cart` kartında
`"client-side"` rozeti VE bir tooltip ("This tool's body runs on the
caller (typically a browser), not on the server...") bulundu.
`get_order_status` kartının yapısı karşılaştırıldı: açıklama paragrafından
doğrudan agent-bağlantı listesine geçiyor, rozet YOK.

**Not (bu dosyanın kapsamı DIŞINDA, kayda değer):** Bu sayfada VE
MT-IST-016'nın run detay sayfasında (aynı satır 41) tekrarlanan bir CSP
konsol hatası gözlendi: `"Executing inline script violates...script-src
'self'..."`. Rozet/uyarı gösterimi buna rağmen doğru çalıştı (işlevsellik
etkilenmedi), bu dosyanın sınır tablosu "konsolun genel mekaniği"ni `09-
ARAYUZ-GENEL.md`'ye bırakıyor (o dosya 2026-09-16'da 56/57 kusursuz
kapandı) — yeni bir `HATA-S4-*` AÇILMADI, ama tur kapanışında `09` ailesinin
kendi konsol taramasının bu iki rotayı (`/tools`, `/runs/{id}`) gerçekten
ziyaret edip etmediği doğrulanması ÖNERİLİR.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-014 — Persistent bir agent'ın istemci tool'u REPLAY'İ HER ÜÇ MOD'DA reddeder (Faz 112)

**Gerçek sonuç**
`manuel-ist-replay` (gerçek `openai`/`gpt-5.4-mini` — `echo` yok, sapma
notuna bakın; yalnız `read_shopping_cart` taşıyor) kaydedildi ve bir run
tamamlandı. Üç replay isteği:
- `ReplayTools`: `409`.
- `LiveTools`: `409`.
- `NoTools`: `409`.

Üçü de AYNI `detail`i taşıyor: `"Agent 'manuel-ist-replay' carries the
client-side tool 'read_shopping_cart' (AddClientTool)...This run cannot be
replayed in any tool mode."` — `read_shopping_cart` adı ve "cannot be
replayed in any tool mode" ibaresi her üçünde birebir var; `NoTools`
DAHİL reddedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-015 — Kod tanımlı (`support`) agent'ta da replay 409 döner (katalog kolu, Faz 112)

**Gerçek sonuç**
MT-IST-001'in `runId`'siyle `LiveTools` replay denendi: `409`,
`{"title":"A tool requiring approval cannot run live","detail":"Agent
'support' carries the tool 'cancel_order', which requires approval, and
cannot run in 'LiveTools' mode...This agent has no persistent definition
(code-defined or deleted), so 'ReplayTools'/'NoTools' are not available
either — this run cannot be replayed."}` — `cancel_order` (onay
gerektiren guard önce tetiklendi) adını taşıyor; case'in kendi notu
gereği `read_shopping_cart` yerine bu ad çıkması da kabul edilen bir
sonuç.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-IST-016 — Konsolun run detayı "Replay" düğmesi 409'u okunabilir gösterir

**Gerçek sonuç**
MT-IST-014'ün run detay sayfası Playwright ile açıldı, varsayılan mod
(`ReplayTools`) ile "Replay" düğmesine tıklandı. Sunucudan gerçek bir
`409` döndü (konsolda görülen ağ isteği kaydı), ekranda bir `alert` rolü
taşıyan kutu belirdi: **"A client-side tool cannot be replayed: Agent
'manuel-ist-replay' carries the client-side tool 'read_shopping_cart'
(AddClientTool)...This run cannot be replayed in any tool mode."** + bir
"Try again" düğmesi. Ekran boş kalmadı, sonsuz yüklenme durumuna
takılmadı, hata metni tam ve okunabilir biçimde göründü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Sayım

16/16 case koşuldu ve Geçti. Sıfır Kaldı, sıfır Beklemede, sıfır fiziksel
eylem gerektiren case (012 ve 016 spec'te 👤 işaretli, ama ikisi de
Playwright ile CORS dahil gerçek bir tarayıcı/gerçek sunucu akışıyla
faithful biçimde koşulabildi — mikrofon/hoparlör/ekran-okuyucu gibi
otomatikleştirilemez bir duyu gerektirmiyorlardı). Yeni ürün kusuru
(`HATA-S4-*`) bulunmadı; MT-IST-013'ün notundaki CSP gözlemi kapanışa
taşındı (aşağıya bakın).

## Devir notu

- Ana uygulama (port 5084) koşum sonunda **temel durumuna** döndürüldü:
  `Tenancy`/`AllowedOrigins` env değişkenleri OLMADAN yeniden başlatıldı
  (aynı şema `mt_s4`, aynı `Ui:AuthToken`), `GET /api/meta` öncesi/sonrası
  birebir aynı.
- Koşum sırasında üretilen geçici kaynaklar temizlendi: `manuel-ist-replay`
  agent'ı silindi (`DELETE /api/agents/manuel-ist-replay` → `204`),
  `mt-ist-012-embed-key` API anahtarı iptal edildi (`DELETE
  /api/api-keys/{id}` → `204`), `python3 -m http.server 8099` durduruldu,
  `/tmp/ist012-embed-site/` ve anahtar dosyası silindi.
- **Kapanışa not:** MT-IST-013'ün gözlemlediği CSP `script-src 'self'`
  konsol hatası (`/tools` ve `/runs/{id}`, ikisinde de satır 41) `09-
  ARAYUZ-GENEL.md`'nin kendi kapsamına giriyor — o dosya zaten kapandı
  (56/57, kusursuz); bu iki rotanın o oturumda ziyaret edilip
  edilmediğinin doğrulanması, edilmediyse yeni bir kusur kaydı açılması
  önerilir.
- **Sıradaki:** `28-DENETIM-ZINCIRI-VE-VERI-HAKLARI.md` (10 case), henüz
  açılmadı.

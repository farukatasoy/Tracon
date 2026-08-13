# 19 — Çok Modluluk: Ek, Ses Tool'ları ve Gerçek Zamanlı Konuşma (`MM`)

> **Alan kodu:** `MM` · **Faz:** 14, 28, 29
> **Kaynak:** `src/AgentPrism.Abstractions/Attachments/` (tümü) ·
> `src/AgentPrism.Core/Attachments/` (tümü) ·
> `src/AgentPrism.AspNetCore/Endpoints/AttachmentEndpoints.cs` ·
> `src/AgentPrism.AspNetCore/OpenAICompat/AttachmentIngestion.cs` ·
> `src/AgentPrism.Voice/` (tümü — `AgentPrism.Voice` paketi) ·
> `src/AgentPrism.Abstractions/Voice/` (tümü — `SpeechContracts.cs`,
> `SpeechModels.cs`, `ConversationContracts.cs`) ·
> `src/AgentPrism.Core/Voice/` (tümü — gerçek zamanlı konuşma sürücüsü) ·
> `src/AgentPrism.AspNetCore/Endpoints/VoiceEndpoints.cs` ·
> `src/AgentPrism.AspNetCore/Voice/VoiceConversationEndpoint.cs` ·
> `src/AgentPrism.AspNetCore/AgentPrismEndpointRouteBuilderExtensions.cs`
> (yalnız `MapVoiceConversation` — `UseWebSockets()` koşullu kurulumu) ·
> Migration'lar: `attachments`/`agent_files` (`0006`), `tool_invocations` beş
> ölçüm sütunu (`0015`), `voice_sessions` (`0016`).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

Üç faz, tek bir omurga üzerinde birikir. Faz 14 ikili içeriğin (görsel, ses,
belge) sohbet geçmişinde **küçük bir referans** olarak yaşamasını sağlar —
gerçek bayt `attachments` tablosundadır. Faz 28 bu depoyu **üretilen sesin**
hedefi yapar: `speak`/`transcribe`/`list_voices` kodda tanımlı üç tool'dur ve
ElevenLabs'e ham `HttpClient` ile bağlanır. Faz 29 ikisini **gerçek zamanlı**
bir WebSocket borusunda birleştirir — ama çalıştırma kaydı, span, maliyet ve
onay vaatlerini askıya almadan: her konuşma turu sıradan bir `runs` satırı
üretir.

```mermaid
flowchart TD
    subgraph F14["Faz 14 — Ek"]
        UP["POST api/attachments<br/>veya govdeye gomulu data: URI"] --> AT["attachments tablosu<br/>bytea"]
        AT --> REF["UriContent referansi<br/>conversation_items'ta KUCUK"]
        REF -.->|"model cagrisi oncesi"| RESOLVE["AttachmentResolvingChatClient<br/>DataContent'e cozer"]
    end

    subgraph F28["Faz 28 — Ses Tool'lari"]
        SPEAK["speak tool'u"] --> ELV["ElevenLabsSpeechClient<br/>xi-api-key"]
        ELV --> GUARD["AttachmentTypeGuard<br/>MP3 bit maskesi"]
        GUARD --> AT
        SPEAK --> USAGE["tool_invocations<br/>usage_unit/quantity/cost"]
    end

    subgraph F29["Faz 29 — Gercek Zamanli Konusma"]
        WS["WebSocket<br/>api/voice/sessions/id/stream"] --> BUF["VoiceUtteranceBuffer<br/>PCM ise 44 bayt WAV baslar"]
        BUF --> ELV
        ELV --> RUN["AIAgent.RunStreamingAsync<br/>MEVCUT calistirma yolu"]
        RUN --> SPEAK
        RUN --> RUNSTORE["runs / run_events / traces<br/>TEKRAR EDILMEZ"]
        WS --> VS["voice_sessions<br/>ozet kayit, ses ICERMEZ"]
    end

    style AT fill:#5a3a7a,stroke:#2c1c3d,color:#ffffff
    style RUNSTORE fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    style USAGE fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Genel HTTP zarfı, idempotency-key deseni, `curl` başlıkları | `07-HTTP-YONETIM-API.md` (zaten üretildi) |
| Genel üç katmanlı erişim koruması (loopback, bearer token, authorization policy) ve rol matrisinin GENEL davranışı | `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) — bu dosya yalnız bu alana **özgü** olan alt protokol token mekanizmasını (§10) test eder |
| Playground'daki ek çipi, sürükle-bırak, mikrofon düğmesinin panel açma/kapama mekaniği, "Konuştur" düğmesinin çalar+maliyet-notu görünümü | `10-ARAYUZ-AGENT-PLAYGROUND.md` (zaten üretildi, `MT-UIAG-044`–`051`) — burada yalnız konuşma panelinin **içindeki** gerçek zamanlı akış (§13) test edilir |
| Genel çalıştırma kaydı, span, maliyet gösterimi, SSE zarfı | `11-ARAYUZ-RUN-SESSION-SSE.md`, `12-GOZLEMLENEBILIRLIK-MALIYET.md` (zaten üretildi) — burada yalnız ses turunun de AYNI yolu ürettiği doğrulanır, mekanizma tekrar edilmez |
| Sahipsiz eklerin/kapanmış konuşma kayıtlarının otomatik temizlenmesi (saklama işi) | `23-SAKLAMA-ARSIV-KOTA.md` (henüz üretilmedi) — burada yalnız `RetentionTargets.Attachments`/`VoiceSessions` hedeflerinin VARLIĞI doğrulanır, işin kendisi değil |

> **Rol matrisi burada da örnek uygulamada NO-OP'tur.** `samples/AgentPrism.Api/Program.cs`
> hiçbir `AgentPrismRolePolicies` rol policy'si kaydetmez (`grep -n "AddAuthorization\|RolePolic" samples/AgentPrism.Api/Program.cs`
> boş döner) — bu genel davranış `13-KIRACI-VE-GUVENLIK.md`'de test edildi.
> Bu dosyadaki tüm uçlar (`RequireRole(roles.Operator)` dahil) bu yüzden yalnız
> loopback + bearer token katmanlarından geçer; ayrıca rol reddi test edilmez.

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080/agentprism`.
3. **§1–4 (ek) hiçbir ek yapılandırma istemez** — `AgentPrism.Core` her zaman
   kurulur. **§5'ten itibaren (ses)** `AgentPrism:Voice:ApiKey` (gerçek
   ElevenLabs anahtarı) gerekir; `00-INDEKS.md` §2.4'e göre zaten ayarlıdır.
   Anahtar tanımlıysa örnek uygulama `UseVoice()` **ve** `UseVoiceConversation()`'ı
   birlikte açar (`Program.cs:212-226`) — ikisi ayrı ayrı açılıp kapatılamaz;
   §5'in ilk case'i (MT-MM-031) bunu bilerek bu çiftin geçici olarak
   KAPATILMASIYLA test eder.
4. `sesli-asistan` agent'ı fixture'dır (`00-INDEKS.md` §3.1): tool'ları
   `speak`, `transcribe`, `list_voices`, `get_order_status`; modeli gerçek
   OpenAI (`gpt-5.4-mini`).

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
export PG="docker exec -i ap-pg psql -U postgres -d agentprism"
```

> 🚨 **Gerçek para uyarısı.** Bu dosyanın §5'ten sonraki HER case'i gerçek
> ElevenLabs kredisini ve (§7, §11'den itibaren) gerçek OpenAI kredisini
> harcar — sahte bir uç yoktur, ölçüm gerçektir. §1–4 (yalnız ek) hiçbir
> sağlayıcıya gitmez.

---

## Bu dosyanın yerel fixture'ları

| Kimlik | Değer |
|---|---|
| `FIX-MM-PNG-01` | 1×1 saydam PNG (68 bayt) — aşağıdaki komutla oluşturulur |
| `FIX-MM-VOICE-01` | Gerçek bir ElevenLabs ses kimliği — sabit değildir, MT-MM-038'in çıktısından `$VOICE_ID` olarak alınır |
| `FIX-MM-WS-CLIENT` | `~/agentprism-manuel-test/voice_client.py` — §9'da kurulan tester-tedarikli WebSocket istemcisi |

```bash
mkdir -p ~/agentprism-manuel-test
python3 -c "
import base64
open('/tmp/fix-mm.png', 'wb').write(base64.b64decode(
    'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII='))
"
```

---

# 1 — Ek Yükleme ve Tür Doğrulama (Faz 14)

### MT-MM-001 — Geçerli bir PNG yüklenir, sihirli bayttan doğru tür çıkarılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | K-113 |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/attachments" -H "$APB" \
     -F "file=@/tmp/fix-mm.png;type=image/png;filename=test.png" | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 201`. Gövdede `mediaType: "image/png"`, `byteSize: 68`,
  `sha256` dolu bir hex dizgisi, `id` bir GUID. `$id`'yi sonraki case'ler
  için `export ATT_ID=<id>` ile sakla.

**Gerçek sonuç**
`HTTP: 201`. `mediaType:"image/png"`, `byteSize:68`, `sha256` 64 hex karakter,
`id:019ff9fb-fc46-7f0b-b198-47053ad21ee6` bir GUID.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-002 — İstemcinin bildirdiği yanlış `Content-Type` sihirli bayt tarafından GEÇERSİZ kılınır

Sınır senaryosu — K-113'ün asıl kanıtı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | K-113 |

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/attachments" -H "$APB" \
     -F "file=@/tmp/fix-mm.png;type=text/plain;filename=yalan.txt" \
     | python3 -c "import json,sys; print(json.load(sys.stdin)['mediaType'])"
```

**Beklenen sonuç**
- `image/png` yazdırılır — istemcinin `type=text/plain` iddiası tamamen
  yok sayılır; kayıtlı tür sihirli bayttan gelir.

**Gerçek sonuç**
`image/png` yazdırıldı — istemcinin `text/plain` iddiası yok sayıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-003 — Yürütülebilir/tanınmayan içerik reddedilir

Negatif senaryo — beyaz listede yürütülebilir tür BİLEREK yoktur.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | K-113 |

**Girilecek veri**
```bash
printf 'MZ\x90\x00\x03\x00\x00\x00\x04\x00\x00\x00\xff\xff' > /tmp/fix-mm.exe
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/attachments" -H "$APB" \
     -F "file=@/tmp/fix-mm.exe;type=application/octet-stream" | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 400`, başlık "Ek turu reddedildi" (dosya türü tanınmadı — `MZ`
  imzası hiçbir sihirli bayt kuralıyla eşleşmez ve rastgele ikili bayt
  taşıdığı için düz metin de sayılmaz).

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek turu reddedildi"`, detay desteklenen türleri listeledi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-004 — Boş dosya reddedilir

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
: > /tmp/fix-mm-empty.png
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/attachments" -H "$APB" \
     -F "file=@/tmp/fix-mm-empty.png;type=image/png"
```

**Beklenen sonuç**
- `HTTP: 400`, başlık "Ek bos olamaz".

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek bos olamaz"`, detay `'file' alani bos.`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-005 — `AgentPrismAttachmentOptions.MaxBytes` (varsayılan 20 MB) aşımı reddedilir

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
head -c 20971521 /dev/urandom > /tmp/fix-mm-big.bin
# Gecerli bir PNG imzasi ile basla, boyut siniri yine de tetiklenmeli
python3 -c "
data = open('/tmp/fix-mm-big.bin','rb').read()
open('/tmp/fix-mm-big.png','wb').write(b'\x89PNG\r\n\x1a\n' + data)
"
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/attachments" -H "$APB" \
     -F "file=@/tmp/fix-mm-big.png;type=image/png"
```

**Beklenen sonuç**
- `HTTP: 400`, başlık "Ek cok buyuk", detay 20 MB sınırını (`20971520` bayt)
  yazar. `rm /tmp/fix-mm-big.bin /tmp/fix-mm-big.png` ile temizle.

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek cok buyuk"`, detay `20971529 bayt; sinir 20971520 bayt.`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-006 — MP3 çerçeve senkronu BİT MASKESİYLE tanınır — beş geçerli varyant kabul, iki `reserved` varyant ret

Sınır senaryosu — `AttachmentTypeGuard.IsMpegFrameSync`, G3 (docs/28, §28.0).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 -c "
import subprocess
cases = {
    'fb-gecerli': (0xFB, True), 'f3-gecerli': (0xF3, True), 'f2-gecerli': (0xF2, True),
    'fa-gecerli': (0xFA, True), 'e3-gecerli': (0xE3, True),
    'e8-ayrilmis-surum': (0xE8, False), 'e1-ayrilmis-katman': (0xE1, False),
}
for name, (b2, expect_ok) in cases.items():
    path = f'/tmp/fix-mm-mp3-{name}.mp3'
    open(path, 'wb').write(bytes([0xFF, b2]) + b'\x00' * 64)
    result = subprocess.run(
        ['curl', '-s', '-o', '/dev/null', '-w', '%{http_code}', '-X', 'POST',
         'http://localhost:5080/agentprism/api/attachments',
         '-H', 'Authorization: Bearer manuel-test-token-2026',
         '-F', f'file=@{path};type=audio/mpeg'],
        capture_output=True, text=True)
    status = result.stdout
    ok = status == '201'
    print(f'{name}: HTTP={status} beklenen_kabul={expect_ok} sonuc_kabul={ok} {\"OK\" if ok==expect_ok else \"BEKLENMEDIK\"}')
"
```

**Beklenen sonuç**
- `fb/f3/f2/fa/e3` satırları `HTTP=201 ... OK` — bunlar gerçek sağlayıcı
  çıktısında görülen geçerli sürüm/katman kombinasyonlarıdır.
- `e8-ayrilmis-surum` ve `e1-ayrilmis-katman` satırları `HTTP=400 ... OK` —
  `version=01` ve `layer=00` `reserved` değerlerdir; sabit bir bayt listesi
  bunları yanlışlıkla kabul ederdi, bit maskesi etmez.

**Gerçek sonuç**
Yedi satırın tamamı beklenen sonuçla `OK` eşleşti: `fb/f3/f2/fa/e3` → `HTTP=201`,
`e8-ayrilmis-surum`/`e1-ayrilmis-katman` → `HTTP=400`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Ek İndirme, Listeleme, Silme, Kiracı Yalıtımı (Faz 14)

### MT-MM-010 — İndirme doğru başlıklarla ve bayt-bayt eşleşmeyle döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | K-113 |

**Ön koşul**
- MT-MM-001'den `$ATT_ID` dolu.

**Girilecek veri**
```bash
curl -s -D - -o /tmp/fix-mm-indirilen.png "$APU/api/attachments/$ATT_ID" -H "$APB" | head -20
diff /tmp/fix-mm.png /tmp/fix-mm-indirilen.png && echo "BAYT BAYT AYNI"
```

**Beklenen sonuç**
- Başlıklarda `Content-Type: image/png`, `Content-Disposition: attachment;
  filename="test.png"`, `X-Content-Type-Options: nosniff`, `ETag` (sha256'nın
  kendisi). `diff` sıfır fark bildirir, `BAYT BAYT AYNI` yazdırılır.

**Gerçek sonuç**
Tüm başlıklar tam beklendiği gibi geldi (`Content-Type: image/png`,
`Content-Disposition: attachment; filename="test.png"`, `X-Content-Type-Options: nosniff`,
`ETag` sha256 değeriyle aynı). `diff` sıfır fark bildirdi, `BAYT BAYT AYNI` yazdırıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-011 — Listeleme `sessionId` ile filtreler; `skip`/`take` sınırlanır

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/attachments?sessionId=filtre-test-42" -H "$APB" \
     -F "file=@/tmp/fix-mm.png;type=image/png"
curl -s "$APU/api/attachments?sessionId=filtre-test-42" -H "$APB" \
     | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/attachments?take=99999&skip=-5" -H "$APB" -o /dev/null
```

**Beklenen sonuç**
- İlk `curl` sonucu `1` yazdırır — yalnız o oturumdaki ek listelenir.
- İkinci istek `HTTP: 200` döner (400 değil): `take` `200`'e, `skip` `0`'a
  **kırpılır**, hata verilmez (`Math.Clamp(take ?? 50, 1, 200)`,
  `Math.Max(skip ?? 0, 0)`).

**Gerçek sonuç**
İlk `curl` `1` yazdırdı. İkinci istek `HTTP: 200` döndü — kırpma çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-012 — Silme sonrası indirme VE ikinci silme `404` döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MM-001'den `$ATT_ID` dolu.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/attachments/$ATT_ID" -H "$APB" -o /dev/null
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/attachments/$ATT_ID" -H "$APB" -o /dev/null
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/attachments/$ATT_ID" -H "$APB" -o /dev/null
```

**Beklenen sonuç**
- Sırasıyla `204`, `404`, `404`. İkinci silme çağrısı da `404` döner —
  silme idempotent bir "başarı" değil, "artık yok" anlamındadır.

**Gerçek sonuç**
Sırasıyla `204`, `404`, `404` geldi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-013 — Başka kiracının ekine erişilemez — "yok" gibi yanıtlanır

Kritik negatif senaryo — kiracı yalıtımı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-TENANT-01` (`kiraci-alfa`) altında bir ek yüklenmiş (`X-AgentPrism-Tenant: kiraci-alfa` başlığıyla).
- Çok kiracılık açık: `AgentPrism:Tenancy:Enabled=true`, `AgentPrism:Tenancy:AllowHeaderResolution=true`
  (varsayılan kapalı — `13-KIRACI-VE-GUVENLIK.md` §5 deseniyle aynı).

**Girilecek veri**

> **Doküman düzeltmesi:** Örnek başlık adı `X-Tenant-Id` idi; gerçek başlık
> `HttpTenantContext.cs:50`'de `X-AgentPrism-Tenant`dır (bkz. `13-KIRACI-VE-GUVENLIK.md`
> MT-SEC-021, aynı başlığı doğru kullanıyor). Aşağıda düzeltildi.

```bash
curl -s -X POST "$APU/api/attachments" -H "$APB" -H "X-AgentPrism-Tenant: kiraci-alfa" \
     -F "file=@/tmp/fix-mm.png;type=image/png" | python3 -c "import json,sys; print(json.load(sys.stdin)['id'])"
# Yukaridaki id'yi ALFA_ID olarak sakla, sonra BETA kiracisiyla dene:
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/attachments/$ALFA_ID" -H "$APB" -H "X-AgentPrism-Tenant: kiraci-beta" -o /dev/null
```

**Beklenen sonuç**
- İkinci istek `HTTP: 404` — "böyle bir ek yok" mesajı; ekin `kiraci-alfa`'ya
  ait olduğu bilgisi hiçbir şekilde sızmaz (403 değil, 404).

**Gerçek sonuç**
İlk deneme (yanlış `X-Tenant-Id` başlığıyla, çok kiracılık kapalı) `HTTP: 200`
döndü — sapma değil, doküman kusuruydu: gerçek başlık adı `X-AgentPrism-Tenant`
(`HttpTenantContext.cs:50`), `X-Tenant-Id` sunucu tarafından hiç okunmuyor ve
sessizce yok sayılıyor. Girilecek veri düzeltildi (yukarıda not edildi),
çok kiracılık `AgentPrism:Tenancy:Enabled`/`AllowHeaderResolution` ile açılıp
doğru başlıkla tekrar koşuldu: `HTTP: 404` — beklenen davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-014 — Oturum silinince ekleri de gider (kaskad, yabancı anahtar OLMADAN)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | K-112 |

**Ön koşul**
- `musteri-42` (`FIX-SESSION-01`) oturumuna bağlı en az bir ek yüklenmiş
  (`POST /api/attachments?sessionId=musteri-42`).

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/sessions/musteri-42" -H "$APB" -o /dev/null
curl -s "$APU/api/attachments?sessionId=musteri-42" -H "$APB" \
     | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- Silme `204`. Listeleme `0` döner — `attachments.session_id` bir yabancı
  anahtar DEĞİLDİR (K-112, gerçek akışta INSERT hatası verdiği için geri
  alındı); kaskad `SessionEndpoints.DeleteSessionAsync` içinde **uygulama
  katmanında** yapılır.

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

### MT-MM-020 — Bir ek + mesajla çalıştırma; modele giden GERÇEK içerik `DataContent`'tir

Gerçek entegrasyon — mutlu yol.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | K-111 |

**Ön koşul**
- `support` agent'ına yeni bir ek yüklenmiş, `$ATT_ID2` dolu.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -d "{
  \"message\": \"Bu goreseli aciklar misin?\",
  \"attachmentIds\": [\"$ATT_ID2\"]
}"
```

**Beklenen sonuç**
- `HTTP: 200`/akış başarıyla tamamlanır; model bir açıklama üretir (metne
  bağlı bir iddia yazılmaz — yalnız yanıtın BOŞ olmadığı ve akışın hatasız
  bittiği doğrulanır). `GET /api/runs/{id}` çalıştırmanın `Completed`
  olduğunu gösterir.

**Gerçek sonuç**
`HTTP: 200`, akış hatasız `done` ile bitti, model boş olmayan bir metin üretti.
`GET /api/runs/{runId}` → `status:"Completed"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-021 — Var olmayan bir ekle çalıştırma → `400`, akış hiç BAŞLAMAZ

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -d '{
  "message": "merhaba",
  "attachmentIds": ["00000000-0000-0000-0000-000000000000"]
}'
```

**Beklenen sonuç**
- `HTTP: 400`, başlık "Ek bulunamadi". Yanıt akışlı (SSE) DEĞİLDİR — ek
  sahipliği akış başlamadan ÖNCE doğrulanır; yarım kalan bir akışta düzgün
  bir `ProblemDetails` dönemeyeceği için bu sıra kasıtlıdır.

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek bulunamadi"`, düz JSON `ProblemDetails` (SSE değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-022 — Başka kiracının eki çalıştırmada kullanılamaz

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MM-013'teki `$ALFA_ID` hâlâ `kiraci-alfa`'da kayıtlı.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" -H "X-AgentPrism-Tenant: kiraci-beta" \
     -H "content-type: application/json" -d "{
  \"message\": \"bu ek nedir\",
  \"attachmentIds\": [\"$ALFA_ID\"]
}"
```

**Beklenen sonuç**
- `HTTP: 400`, "Ek bulunamadi" — `kiraci-beta` `kiraci-alfa`'nın ekini
  göremez, akış başlamaz.

**Gerçek sonuç**
`HTTP: 400`, `title:"Ek bulunamadi"` (doğru başlık `X-AgentPrism-Tenant` ile,
bkz. MT-MM-013 doküman düzeltmesi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-023 — `message` BOŞ ama `attachmentIds` doluysa istek GEÇERLİDİR

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Ön koşul**
- `$ATT_ID2` geçerli bir ek kimliği.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" -d "{ \"attachmentIds\": [\"$ATT_ID2\"] }"
```

**Beklenen sonuç**
- `HTTP: 200` — üç alandan (`message`, `attachmentIds`, `approvals`) yalnız
  biri dolu olması yeterlidir; boş istek reddi yalnız üçü de boşsa tetiklenir.

**Gerçek sonuç**
`HTTP: 200`, akış hatasız `done` ile bitti — `message` boş olsa da yalnız
`attachmentIds` dolu olması yeterliydi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — OpenAI Uyumlu Uçlarda Gömülü `data:` URI (Faz 14, K-116)

### MT-MM-026 — `/v1/responses` gömülü `data:` URI'yi eğe çevirir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | K-116 |

**Girilecek veri**
```bash
python3 -c "
import base64, json
png = base64.b64encode(open('/tmp/fix-mm.png','rb').read()).decode()
body = {
    'model': 'support',
    'input': [{'role': 'user', 'content': [
        {'type': 'input_text', 'text': 'Bu resimde ne var?'},
        {'type': 'input_image', 'image_url': f'data:image/png;base64,{png}'}
    ]}]
}
print(json.dumps(body))
" > /tmp/fix-mm-responses.json

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" -H "$APB" \
     -H "content-type: application/json" -d @/tmp/fix-mm-responses.json | tail -3
```

**Beklenen sonuç**
- `HTTP: 200`. Ardından `$APU/api/attachments?sessionId=...` ile bakıldığında
  gömülü PNG artık `attachments` tablosunda ayrı bir kayıt olarak durur —
  gövdedeki base64 blok sohbet geçmişine OLDUĞU GİBİ yazılmaz.

**Gerçek sonuç**
`HTTP: 200`. `GET /api/attachments` listesinde `sessionId` yanıtın `resp_...`
kimliğiyle eşleşen, `mediaType:"image/png"`, `byteSize:68` yeni bir kayıt
oluştu — gömülü base64 ayrı bir `attachments` satırına çözüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-027 — `/v1/chat/completions` görsel girdiyi KABUL ETMEZ

Sınır senaryosu — kasıtlı kapsam dışı (K-116), kusur DEĞİL.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | K-116 |

**Girilecek veri**
```bash
python3 -c "
import base64, json
png = base64.b64encode(open('/tmp/fix-mm.png','rb').read()).decode()
body = {
    'model': 'support',
    'messages': [{'role': 'user', 'content': [
        {'type': 'text', 'text': 'Bu resimde ne var?'},
        {'type': 'image_url', 'image_url': {'url': f'data:image/png;base64,{png}'}}
    ]}]
}
print(json.dumps(body))
" > /tmp/fix-mm-chatcompletions.json

curl -s "$APU/v1/chat/completions" -H "$APB" -H "content-type: application/json" \
     -d @/tmp/fix-mm-chatcompletions.json | head -5
```

**Beklenen sonuç**
- İstek reddedilmez ama görsel parça sessizce YOK sayılır (yalnız `text`
  parçası modele ulaşır) — `OpenAIChatCompletionsEndpoints.ReadContent`
  `image_url`/`input_file` okumaz. Bu, `docs/14-COK-MODLULUK.md`'nin
  bilinçli kapsam kararıdır; koşum bu davranışı doğrular/çürütür.

**Gerçek sonuç**
`HTTP: 200`. Model "Resmi göremiyorum, lütfen görseli yükle" yanıtı verdi —
görsel parça modele hiç ulaşmadı, istek reddedilmedi. Beklenen kapsam dışı
davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-028 — Beyaz listede olmayan bir `data:` türü reddedilir

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 14 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 -c "
import base64, json
exe = base64.b64encode(bytes([0x4D,0x5A,0x90,0x00,0x03,0x00,0x00,0x00])).decode()
body = {
    'model': 'support',
    'input': [{'role': 'user', 'content': [
        {'type': 'input_file', 'file_data': f'data:application/octet-stream;base64,{exe}', 'filename': 'x.exe'}
    ]}]
}
print(json.dumps(body))
" > /tmp/fix-mm-badtype.json

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" -H "$APB" \
     -H "content-type: application/json" -d @/tmp/fix-mm-badtype.json
```

**Beklenen sonuç**
- `HTTP: 400` — `AttachmentIngestion.ReplaceEmbeddedDataAsync` `guard.Validate`
  hatasını doğrudan istemciye taşır.

**Gerçek sonuç**
`HTTP: 400`, `"Dosya turu taninmadi. ..."` — beklenen davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Ses Sağlayıcı Yapılandırması: Açılış Doğrulaması (Faz 28)

### MT-MM-031 — `AgentPrism:Voice:ApiKey` yoksa `/api/voice/*` `501`, konuşma ucu `404` döner

Negatif/sınır senaryosu — kritik. **Geçici yapılandırma değişikliği ister.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 28, 29 |
| **İlgili karar** | — |

**Adımlar**
1. `cd samples/AgentPrism.Api && dotnet user-secrets remove "AgentPrism:Voice:ApiKey"`.
2. Uygulamayı yeniden başlat (`Program.cs:210`'daki `voiceEnabled` artık
   `false`; hem `UseVoice()` hem `UseVoiceConversation()` HİÇ çağrılmaz).

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/voice/health" -H "$APB" -o /dev/null
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/voice/sessions/deneme/stream" -H "$APB" -o /dev/null
```

**Beklenen sonuç**
- Birinci istek `HTTP: 501` (uç kayıtlı ama sağlayıcı yok — `NotConfigured()`).
- İkinci istek `HTTP: 404` (uç HİÇ kayıtlı değil — `UseVoiceConversation()`
  çağrılmadığı için rota gerçekten yok, "var ama kapalı" değil).
- **Ön koşulu geri al:**
  `dotnet user-secrets set "AgentPrism:Voice:ApiKey" "<ELEVENLABS_ANAHTARINIZ>"`,
  uygulamayı yeniden başlat — sonraki tüm case'ler bu anahtara ihtiyaç duyar.

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

### MT-MM-032 — `pcm_*`/`ulaw_*`/`alaw_*` çıktı biçimleri AÇILIŞTA reddedilir

Negatif senaryo — saklanamayan biçim.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:Voice:OutputFormat" "pcm_16000"
dotnet run
```

**Beklenen sonuç**
- Uygulama açılışta `OptionsValidationException` ile ÇÖKER; mesaj
  `'pcm_16000' bicimi ek olarak saklanamaz` metnini içerir (`VoiceOptionsValidator.IsStorableFormat`).
- **Geri al:** `dotnet user-secrets remove "AgentPrism:Voice:OutputFormat"`,
  yeniden başlat.

**Gerçek sonuç**
Açılış `OptionsValidationException` ile çöktü (exit code 134), mesaj:
`'pcm_16000' bicimi ek olarak saklanamaz. ...`. Şerit izolasyonu gereği
`AgentPrism__Voice__OutputFormat` ortam değişkeni kaldırılıp normal
konfigürasyonla yeniden başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-033 — Tanınmayan sağlayıcı adı AÇILIŞTA reddedilir

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:Voice:Provider" "azure-cognitive-speech"
dotnet run
```

**Beklenen sonuç**
- Açılış çöker; mesaj `'azure-cognitive-speech' saglayicisi taninmiyor.
  Yerlesik saglayici: 'elevenlabs'` metnini içerir.
- **Geri al:** `dotnet user-secrets remove "AgentPrism:Voice:Provider"`,
  yeniden başlat.

**Gerçek sonuç**
Açılış çöktü (exit code 134), mesaj: `'azure-cognitive-speech' saglayicisi
taninmiyor. Yerlesik saglayici: 'elevenlabs'. ...`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-034 — `MaxCharactersPerRequest`/`MaxConcurrentRequests` sıfır veya negatif AÇILIŞTA reddedilir

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:Voice:MaxConcurrentRequests" "0"
dotnet run
```

**Beklenen sonuç**
- Açılış çöker; mesaj `eszamanli istek siniri sifirdan buyuk olmalidir`
  içerir.
- **Geri al:** `dotnet user-secrets remove "AgentPrism:Voice:MaxConcurrentRequests"`,
  yeniden başlat.

**Gerçek sonuç**
Açılış çöktü (exit code 134), mesaj: `eszamanli istek siniri sifirdan buyuk
olmalidir.`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-035 — Hatalı bir API anahtarıyla hata mesajı anahtarı SIZDIRMAZ

Kritik güvenlik doğrulaması.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Adımlar**
1. Geçici olarak GEÇERSİZ ama BOŞ-OLMAYAN bir anahtar ayarla:
   `dotnet user-secrets set "AgentPrism:Voice:ApiKey" "SAHTE-GECERSIZ-ANAHTAR-xyz789"`,
   uygulamayı yeniden başlat (bu geçerli bir açılış — doğrulama yalnız
   BOŞLUĞU kontrol eder, gerçekliği değil).
2. Gerçek bir çağrı yap.

**Girilecek veri**
```bash
curl -s "$APU/api/voice/health" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `isHealthy: false`. `detail` alanı yalnız HTTP durum kodunu ve genel bir
  ipucu içerir (`" API anahtari gecersiz."`) — `SAHTE-GECERSIZ-ANAHTAR-xyz789`
  metni yanıtın HİÇBİR YERİNDE görünmez. Uygulama loglarını da tara: anahtar
  orada da görünmemelidir.
- **Geri al:** gerçek anahtarı tekrar ayarla, yeniden başlat.

**Gerçek sonuç**
`isHealthy:false`, `detail:"Ses listesi alinamadi: HTTP 401. API anahtari
gecersiz."` — sahte anahtar metni yanıtta hiç görünmedi. Sunucu logu
(`grep -c "SAHTE-GECERSIZ-ANAHTAR-xyz789"`) `0` sonuç verdi — anahtar loglara
da sızmadı. Gerçek ElevenLabs anahtarı geri ayarlanıp uygulama yeniden
başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Ses HTTP Uçları (Faz 28)

### MT-MM-038 — `GET /api/voice/voices` gerçek ses listesini döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/api/voice/voices" -H "$APB" | python3 -m json.tool | head -20
```

**Beklenen sonuç**
- `HTTP: 200`, en az bir ses; her öğede `voiceId`, `name`. Bir sesin
  `voiceId`'sini `export VOICE_ID=<id>` ile sakla — sonraki case'ler (§7,
  §9'daki WebSocket istemcisi) bunu kullanır.

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

### MT-MM-039 — `GET /api/voice/health` ücret ÜRETMEDEN sağlığı ölçer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s "$APU/api/voice/health" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `isHealthy: true`, `voiceCount` MT-MM-038'deki liste uzunluğuyla eşleşir,
  `latency` dolu. Bu çağrı `GET /v2/voices`'e gider — hiçbir ses ÜRETMEZ,
  ElevenLabs panelinde karakter tüketimi görünmez.

**Gerçek sonuç**
`isHealthy:true`, `voiceCount:10` (MT-MM-038 ile eşleşiyor), `latency` dolu
(`00:00:00.2277458`), `detail:null`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-040 — `POST /api/voice/speak` metni seslendirir, ek üretir, yanıtta karakter/maliyet döner

Gerçek entegrasyon — mutlu yol, kritik.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Ön koşul**
- `$VOICE_ID` MT-MM-038'den dolu.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/voice/speak" -H "$APB" -H "content-type: application/json" -d "{
  \"text\": \"AgentPrism manuel test seslendirmesi.\",
  \"sessionId\": \"manuel-mm-1\",
  \"voiceId\": \"$VOICE_ID\"
}" | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200`. Gövdede `attachment.mediaType: "audio/mpeg"`, `characters`
  pozitif bir tam sayı, `isEstimated` bir `bool`. `AgentPrism:Pricing:Voice`
  yapılandırılmamışsa `cost`/`currency` `null` — SIFIR değil (K-032).
  `attachment.id`'yi `GET {APU}/api/attachments/{id}` ile indirip gerçekten
  çalan bir MP3 olduğunu doğrula.

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

### MT-MM-041 — `POST /api/voice/speak` boş metinle `400` döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/voice/speak" -H "$APB" \
     -H "content-type: application/json" -d '{ "text": "   " }'
```

**Beklenen sonuç**
- `HTTP: 400`, başlık "Metin bos" — ElevenLabs'e HİÇ istek gitmez (kredi
  harcanmaz).

**Gerçek sonuç**
`HTTP: 400`, `title:"Metin bos"`, detay `'text' alani zorunludur.`

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-042 — `POST /api/voice/speak`, `MaxCharactersPerRequest` sınırını artık YEREL OLARAK denetler (düzeltildi)

Sınır senaryosu — düzeltilmiş kusur.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Düzeltilmiş kusur (2026-08-10).** `VoiceEndpoints.cs`'in kendi XML belgesi
"uc ... tool ile ayni karakter sinirina uyar" diyordu ama `SpeakAsync` gövdesi
böyle bir kontrol taşımıyordu — `SpeakTool.InvokeCoreAsync` bu kontrolü
yaparken HTTP ucu `ElevenLabsSpeechClient.SynthesizeAsync`'i DOĞRUDAN
çağırıyordu, doküman ile kod çelişiyordu. `ISpeechSynthesizer`'a
`MaxCharactersPerRequest` özelliği eklendi (`AgentPrism.Abstractions` —
`AgentPrism.AspNetCore`, `AgentPrism.Voice`'a referans VEREMEZ, bu yüzden
soyutlama katmanına eklendi); `SpeakAsync` artık bu sınırı `speak` tool'uyla
AYNI şekilde uygular.

**Girilecek veri**
```bash
python3 -c "print('a' * 6000)" > /tmp/fix-mm-uzun.txt
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/voice/speak" -H "$APB" \
     -H "content-type: application/json" \
     -d "{ \"text\": \"$(cat /tmp/fix-mm-uzun.txt)\", \"sessionId\": \"manuel-mm-1\" }"
```

**Beklenen sonuç**
- Varsayılan `MaxCharactersPerRequest` **5000**'dir ve gönderilen metin
  **6000** karakterdir. İstek ElevenLabs'e HİÇ GİTMEDEN `HTTP: 400` döner
  (`title: "Metin cok uzun"`, `detail` sınırı ve gerçek uzunluğu içerir).
  İstek sağlayıcıya giderse (400 yerine 502/başarı) fix'in regresyonudur —
  **Kusur, Önem: Orta**.

**Gerçek sonuç**
`HTTP: 400`, `title:"Metin cok uzun"`, detay `Metin 6000 karakter; sinir 5000. ...`
— fix bekleneni yaptı, istek sağlayıcıya gitmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-043 — `GET /api/voice/sessions` konuşma katmanı kapalıyken BOŞ liste döner, `501` DEĞİL

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Beklenen sonuç (koşum, izlek C — kaynak okuması)**
- `VoiceEndpoints.ListSessionsAsync`, `IVoiceSessionStore? store` `null`
  ise `TypedResults.Ok<IReadOnlyList<VoiceSessionRecord>>([])` döner — liste
  ucu bir yeteneğin YOKLUĞUNU değil, verinin YOKLUĞUNU bildirir; arayüz
  paneli hatasız çizilir. Bu örnek uygulamada `UseVoiceConversation()` her
  zaman `UseVoice()` ile birlikte açıldığı için gerçek 501/boş-liste ayrımı
  yalnız MT-MM-031'in geçici kapatma adımıyla gözlemlenebilir — orada
  `/api/voice/sessions` de ayrıca çağrılıp `[]` döndüğü doğrulanabilir
  (bu case'i MT-MM-031'e ek bir doğrulama satırı olarak koşum sırasında
  birleştir).

**Gerçek sonuç**
MT-MM-031 ile aynı koşumda (voice kapalı, `UseVoiceConversation()` hiç
çağrılmamışken) `GET /api/voice/sessions` çağrıldı: `HTTP: 200`, gövde `[]`
— `501` DEĞİL, boş liste. Beklenen davranış doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Ses Tool'ları: `speak` / `transcribe` / `list_voices` (Faz 28, gerçek çalıştırma)

### MT-MM-046 — `speak` gerçek bir çalıştırmada çağrılır, ek `session_id`'si DOLUDUR (G1)

Gerçek entegrasyon — kritik, `docs/28` G1'in canlı doğrulaması.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/sesli-asistan/run" -H "$APB" \
     -H "content-type: application/json" -d '{
  "sessionId": "manuel-mm-speak-1",
  "message": "Merhaba dedigimi sesli soyle."
}'
```

**Doğrulama sorgusu**

> **Doküman düzeltmesi:** `tool_name` sütunu `attachments` tablosunda yok
> (bkz. Gerçek sonuç). Aşağıda çıkarıldı.

```sql
SELECT session_id, run_id, media_type, byte_size
FROM agentprism.attachments
WHERE session_id = 'manuel-mm-speak-1'
ORDER BY created_at DESC LIMIT 1;
```

**Beklenen sonuç**
- Akış tamamlanır, yanıtta bir tool çağrısı görünür (`speak`), sonuç metni
  `attachmentId=...` biçimindedir (ham ses DEĞİL — bağlam penceresine base64
  konmaz). SQL sorgusu **tek bir satır** döner ve `session_id` alanı
  **DOLUDUR** (`NULL` değil) — G1'in düzeltmesi: tool `AgentRunScope.SessionId`
  üzerinden oturum kimliğini görür, ek sahipsiz sayılıp silinmez.

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

### MT-MM-047 — `speak` — `MaxCharactersPerRequest` aşımı tool İÇİNDE hata döner, metin KIRPILMAZ

Negatif senaryo. Sınır artık HTTP ucuyla (MT-MM-042) AYNI değeri uygular —
yalnız hatanın ŞEKLİ farklıdır (burada tool sonucu içinde metin, orada
`400 ProblemDetails`).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 -c "print('Lutfen şunu oldugu gibi tekrarla ve speak toolunu 6000 karakterlik bu metinle cagir: ' + 'a'*6000)" \
  > /tmp/fix-mm-tool-uzun.txt
curl -s -X POST "$APU/api/agents/sesli-asistan/run" -H "$APB" \
     -H "content-type: application/json" \
     -d "{ \"sessionId\": \"manuel-mm-speak-2\", \"message\": $(python3 -c "import json; print(json.dumps(open('/tmp/fix-mm-tool-uzun.txt').read()))") }"
```

**Beklenen sonuç**
- Model `speak` tool'unu 6000 karakterlik metinle çağırırsa tool bir HATA
  metni döner (`Metin 6000 karakter; sinir 5000. ...`) — metin sessizce
  KIRPILMAZ; model hatayı görüp kullanıcıya açıklayabilir. (Model tool'u
  hiç çağırmamayı seçerse case `⏭ ATLA — model tool'u tetiklemedi` notuyla
  işaretlenir ve tekrar denenir.)

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

### MT-MM-048 — `transcribe` kayıtlı bir ses ekini metne çevirir

Gerçek entegrasyon.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MM-040'tan bir ses ekinin `id`'si (`$SPEECH_ATT_ID`).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/sesli-asistan/run" -H "$APB" \
     -H "content-type: application/json" -d "{
  \"sessionId\": \"manuel-mm-transcribe-1\",
  \"message\": \"attachmentId=$SPEECH_ATT_ID olan ses ekini transcribe tool'uyla metne cevir ve sonucu aynen yaz.\"
}"
```

**Beklenen sonuç**
- Yanıt `transcribe` tool çağrısı içerir; sonuç `[dil=...] ...` biçiminde
  bir metindir (metnin İÇERİĞİ değişmez sayılmaz — yalnız tool'un
  çağrıldığı ve boş olmayan bir metin döndürdüğü doğrulanır).

**Gerçek sonuç**
`transcribe` çağrıldı, sonuç: `"[dil=tur] Agent Prism manuel test seslendirmesi"`
— beklenen biçimde, boş olmayan bir metin.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-049 — `transcribe` — ses OLMAYAN bir ekle çağrılırsa hata döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MM-001'deki PNG ekinin `id`'si (`$ATT_ID`, henüz silinmemiş bir kopyası
  gerekiyorsa yeniden yükle).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/sesli-asistan/run" -H "$APB" \
     -H "content-type: application/json" -d "{
  \"sessionId\": \"manuel-mm-transcribe-2\",
  \"message\": \"attachmentId=$ATT_ID olan eki transcribe tool'uyla metne cevirmeyi dene.\"
}"
```

**Beklenen sonuç**
- Tool hatası: `'...' kimlikli ek bir ses dosyasi degil (tur: image/png).`
  — `descriptor.MediaType.StartsWith("audio/")` kontrolü.

**Gerçek sonuç**
Model sonucu `"Error: Function failed."` gördü (Microsoft.Extensions.AI'nin
genel sarmalayıcı mesajı); sunucu logunda gerçek istisna tam beklenen metni
taşıyordu: `AgentPrismException: '019ffa11-1c59-7b24-853d-ad453065b03e'
kimlikli ek bir ses dosyasi degil (tur: image/png).`
(`TranscribeTool.cs:74`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-050 — `list_voices` ücret ÜRETMEDEN sesleri listeler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/sesli-asistan/run" -H "$APB" \
     -H "content-type: application/json" -d '{
  "sessionId": "manuel-mm-listvoices-1",
  "message": "Hangi sesler var? list_voices toolunu kullan."
}'
```

**Beklenen sonuç**
- Yanıt `list_voices` tool çağrısı içerir; sonuç `Ad (kimlik)` biçiminde
  satırlardır ve `MaxListedVoices=50` üstünde bir hesapta "... ve N ses
  daha." ile kısaltılır. ElevenLabs panelinde bu çağrı karakter/dakika
  TÜKETMEZ.

**Gerçek sonuç**
`list_voices` çağrıldı, sonuç `Ad (kimlik) — kategori` biçiminde 10 satır
(hesapta 10 ses var, 50 sınırı tetiklenmedi): `"Bella - Professional, Bright,
Warm (hpp4J3VqNfWAUOO0d1Us) — premade\n..."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Maliyet ve Ölçüm: İki Seslendirme Yolunun Farkı (Faz 28, K-220)

### MT-MM-053 — `speak` tool çağrısı `tool_invocations`'a `usage_unit=characters` ile yazılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MM-046'daki çalıştırmanın `runId`'si (`$RUN_ID`).

**Doğrulama sorgusu**
```sql
SELECT tool_name, usage_unit, usage_quantity, usage_estimated, cost, cost_currency
FROM agentprism.tool_invocations
WHERE run_id = '<RUN_ID>' AND tool_name = 'speak';
```

**Beklenen sonuç**
- Bir satır: `usage_unit='characters'`, `usage_quantity` pozitif,
  `usage_estimated` `true`/`false` (ElevenLabs `character-cost` başlığı
  döndürüp döndürmediğine bağlı), `cost_currency` fiyat yapılandırılmışsa
  dolu, değilse `cost` **`NULL`** — sıfır DEĞİL.

**Gerçek sonuç**
Bir satır: `usage_unit='characters'`, `usage_quantity=2` (pozitif),
`usage_estimated=false`. `AgentPrism:Pricing:Voice` bu ortamda yapılandırılmış
olduğundan `cost=0.00022`, `cost_currency='USD'` doldu (2 × 110e-6, doğru
hesaplandı) — sıfır DEĞİL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-054 — `transcribe` tool çağrısı `usage_unit=seconds` ile yazılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MM-048'deki çalıştırmanın `runId`'si.

**Doğrulama sorgusu**
```sql
SELECT usage_unit, usage_quantity FROM agentprism.tool_invocations
WHERE run_id = '<RUN_ID>' AND tool_name = 'transcribe';
```

**Beklenen sonuç**
- `usage_unit='seconds'`, `usage_quantity` sesin uzunluğuna yakın bir
  ondalık (küçük bir test sesi için birkaç saniye).

**Gerçek sonuç**
`usage_unit='seconds'`, `usage_quantity=2.6006250000` — küçük test sesiyle
tutarlı bir ondalık.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-055 — `POST /api/voice/speak` (operatör yolu) `tool_invocations`'a HİÇ satır YAZMAZ

Sınır senaryosu — K-220.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 28 |
| **İlgili karar** | K-220 |

**Ön koşul**
- MT-MM-040 çalıştırılmış (operatör yolu üzerinden bir ses üretilmiş).

**Doğrulama sorgusu**
```sql
SELECT count(*) FROM agentprism.tool_invocations WHERE tool_name = 'speak'
  AND created_at > now() - interval '5 minutes';
```

**Beklenen sonuç**
- Bu sayı, MT-MM-046/047'de agent'ın `speak` tool'unu kaç kez çağırdığıyla
  BİREBİR eşleşir — MT-MM-040'ın operatör çağrısı HİÇ eklemez.
  `tool_invocations.run_id` zorunlu bir yabancı anahtardır ve operatör
  eyleminin bağlı olduğu bir `run` yoktur; ölçüm yalnız HTTP yanıtında
  görünür kalır (kalıcı değildir).

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

### MT-MM-059 — Python WebSocket istemcisini kur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Adımlar**
1. Paketi kur ve istemciyi yaz.

**Girilecek veri**
```bash
pip3 install --quiet websockets

cat > ~/agentprism-manuel-test/voice_client.py << 'PYEOF'
#!/usr/bin/env python3
"""AgentPrism gercek zamanli konusma WebSocket istemcisi (tester-tedarikli)."""
import argparse
import asyncio
import json
import math
import struct
import sys

import websockets

SUBPROTOCOL = "agentprism.voice.v1"
TOKEN_PREFIX = "agentprism.token."


def make_tone(seconds: float, sample_rate: int, freq: float = 440.0) -> bytes:
    n = int(sample_rate * seconds)
    samples = [int(3000 * math.sin(2 * math.pi * freq * i / sample_rate)) for i in range(n)]
    return struct.pack(f"<{n}h", *samples)


async def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--host", default="localhost:5080")
    p.add_argument("--prefix", default="/agentprism")
    p.add_argument("--session", default="manuel-ws-1")
    p.add_argument("--agent", default="sesli-asistan")
    p.add_argument("--token", default="manuel-test-token-2026")
    p.add_argument("--format", default="pcm16")
    p.add_argument("--voice-id", default=None)
    p.add_argument("--sample-rate", type=int, default=16000)
    p.add_argument("--audio-seconds", type=float, default=1.5)
    p.add_argument("--send-token-in-query", action="store_true")
    p.add_argument("--no-token", action="store_true")
    p.add_argument("--no-commit", action="store_true")
    p.add_argument("--repeat-start", action="store_true")
    p.add_argument("--cancel-on-audio-start", action="store_true")
    p.add_argument("--send-audio-before-start", action="store_true")
    p.add_argument("--max-wait-seconds", type=float, default=30.0)
    args = p.parse_args()

    url = f"ws://{args.host}{args.prefix}/api/voice/sessions/{args.session}/stream"
    if args.send_token_in_query:
        url += f"?token={args.token}"

    subprotocols = [SUBPROTOCOL]
    if not args.send_token_in_query and not args.no_token and args.token:
        subprotocols.append(TOKEN_PREFIX + args.token)

    print(f">> baglaniliyor: {url}")
    print(f">> alt protokoller: {subprotocols}")

    try:
        async with websockets.connect(url, subprotocols=subprotocols, open_timeout=10) as ws:
            print(">> BAGLANDI, kabul edilen alt protokol:", ws.subprotocol)

            tone = make_tone(args.audio_seconds, args.sample_rate)

            if args.send_audio_before_start:
                await ws.send(tone)

            start = {"type": "start", "agent": args.agent, "inputFormat": args.format}
            if args.voice_id:
                start["voiceId"] = args.voice_id
            await ws.send(json.dumps(start))
            print(">> gonderildi:", start)

            if args.repeat_start:
                await asyncio.sleep(0.2)
                await ws.send(json.dumps(start))
                print(">> TEKRAR gonderildi (ikinci start):", start)

            if not args.send_audio_before_start:
                await ws.send(tone)
                print(f">> {len(tone)} bayt ses gonderildi")

            if not args.no_commit:
                await ws.send(json.dumps({"type": "commit"}))
                print(">> gonderildi: commit")

            cancelled = False
            deadline = asyncio.get_event_loop().time() + args.max_wait_seconds

            while asyncio.get_event_loop().time() < deadline:
                remaining = deadline - asyncio.get_event_loop().time()
                try:
                    message = await asyncio.wait_for(ws.recv(), timeout=max(remaining, 0.1))
                except asyncio.TimeoutError:
                    print(">> ZAMAN ASIMI: beklenen cerceve gelmedi")
                    break

                if isinstance(message, bytes):
                    print(f"<< [ikili ses cercevesi] {len(message)} bayt")
                    continue

                data = json.loads(message)
                print("<<", json.dumps(data, ensure_ascii=False))

                if args.cancel_on_audio_start and data.get("type") == "audioStart" and not cancelled:
                    cancelled = True
                    await ws.send(json.dumps({"type": "cancel"}))
                    print(">> gonderildi: cancel (kesinti)")

                if data.get("type") in ("done", "error"):
                    break

            await ws.send(json.dumps({"type": "stop"}))
            print(">> gonderildi: stop")

    except websockets.exceptions.InvalidStatus as exc:
        print(f"BAGLANTI REDDEDILDI: {exc}")
        sys.exit(1)
    except websockets.exceptions.ConnectionClosed as exc:
        print(f"BAGLANTI KAPANDI: code={exc.code} reason={exc.reason}")


if __name__ == "__main__":
    asyncio.run(main())
PYEOF

python3 ~/agentprism-manuel-test/voice_client.py --help
```

**Beklenen sonuç**
- `pip3 install` hatasız biter. `--help` çıktısı yukarıdaki argüman
  listesini gösterir.

**Gerçek sonuç**
`pip3 install --quiet websockets` hatasız bitti (paket zaten kuruluydu).
İstemci `~/agentprism-manuel-test/voice_client.py` olarak yazıldı — tek
sapma: `--host` varsayılanı bu şeridin portuna göre `localhost:5081`
(dokümandaki `localhost:5080` şerit izolasyonu gereği). `--help` beklenen
argüman listesini eksiksiz gösterdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 10 — Gerçek Zamanlı Konuşma: El Sıkışma ve Güvenlik (Faz 29)

### MT-MM-062 — `UseVoiceConversation()` açıksa ama sağlayıcı yoksa `501`; hiç çağrılmadıysa `404`

Bu ayrım MT-MM-031'de `sesli-asistan` çiftinin (Voice+VoiceConversation
birlikte açılıp kapanan) ikisini de kapsayacak şekilde zaten koşuldu; burada
yalnız izlek C referansı tekrar edilir.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Beklenen sonuç (izlek C — kaynak/otomatik test referansı)**
- Repo'nun kendi `VoiceConversationTests.UseVoiceConversation_cagrilmadiysa_HICBIR_uc_acilmaz`
  ve `Ses_saglayicisi_yoksa_501_doner` testleri bu iki durumu ayrı ayrı
  doğrular (biri `UseVoiceConversation()` hiç çağrılmadan `404`, diğeri
  `UseVoiceConversation()` çağrılıp `UseVoice()` çağrılmadan `501`). Örnek
  uygulamada ikinci durum ayrı test edilemez (ikisi birlikte açılıp
  kapanıyor, bkz. MT-MM-031); bu case yalnız otomatik test kanıtının GÜNCEL
  olduğunu (test dosyasının varlığını ve geçtiğini) doğrular:
  `dotnet test tests/AgentPrism.AspNetCore.FunctionalTests -c Release --no-build --filter "FullyQualifiedName~VoiceConversationTests.UseVoiceConversation_cagrilmadiysa|FullyQualifiedName~VoiceConversationTests.Ses_saglayicisi_yoksa"`.

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

### MT-MM-063 — WebSocket olmayan bir isteğe `400` döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/voice/sessions/duz-http-deneme/stream" -H "$APB" -o /dev/null
```

**Beklenen sonuç**
- `HTTP: 400`, "WebSocket yukseltmesi gerekiyor" — düz bir GET isteği bir
  yükseltme talebi taşımaz.

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

### MT-MM-064 — Token doğruysa alt protokolde KABUL edilir, `ready` çerçevesi gelir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | K-224 |

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-token-ok --token manuel-test-token-2026
```

**Beklenen sonuç**
- `BAGLANDI, kabul edilen alt protokol: agentprism.voice.v1` yazdırılır.
  İlk `<<` çerçevesi `{"type": "ready", "agent": "sesli-asistan", ...,
  "persistAudio": false}` olur.

**Gerçek sonuç**
Tam olarak beklendiği gibi: `BAGLANDI, kabul edilen alt protokol:
agentprism.voice.v1`, ilk `<<` çerçevesi
`{"type": "ready", "agent": "sesli-asistan", "sessionId": "manuel-ws-token-ok", "persistAudio": false}`.
Ardından gerçek OpenAI + ElevenLabs uçtan uca çalıştı (`transcript` →
`runStarted` → `text` deltaları → `audioStart` → ikili ses çerçeveleri →
`audioEnd` → `done`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-065 — Token YANLIŞSA el sıkışma REDDEDİLİR

Negatif senaryo — kritik.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | K-224 |

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-token-bad --token yanlis-token-123
```

**Beklenen sonuç**
- `BAGLANTI REDDEDILDI: ...` — el sıkışma `401` ile düşer (WebSocket
  yükseltmesi hiç tamamlanmaz), sunucu beklenen token hakkında hiçbir
  ipucu vermez.

**Gerçek sonuç**
`BAGLANTI REDDEDILDI: server rejected WebSocket connection: HTTP 401` —
tam beklendiği gibi, mesajda beklenen token hakkında hiçbir ipucu yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-066 — 🚨 Token SORGU DİZESİNDE gönderilirse KABUL EDİLMEZ

Kritik negatif senaryo — güvenlik sınırı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | K-224 |

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-query-token \
  --token manuel-test-token-2026 --send-token-in-query --no-token
```

**Beklenen sonuç**
- Komut `--send-token-in-query` ile token'ı `?token=...` sorgu dizesine
  koyar VE `--no-token` ile alt protokol listesinden `agentprism.token.*`
  girdisini ÇIKARIR (yalnız `agentprism.voice.v1` kalır). Bağlantı
  REDDEDİLİR (`401`) — sunucu yalnız `Sec-WebSocket-Protocol` alt
  protokolüne bakar, sorgu dizesini hiç okumaz. Adres bu şekilde sunucu
  günlüklerine ve tarayıcı geçmişine yazılsa bile token orada geçerli
  sayılmaz.

**Gerçek sonuç**
`BAGLANTI REDDEDILDI: server rejected WebSocket connection: HTTP 401` —
sorgu dizesindeki token tamamen yok sayıldı, alt protokolde
`agentprism.token.*` girdisi olmayınca bağlantı reddedildi. Beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-067 — Başka kiracının oturumuna bağlanmak o kaydı GÖRMEZ; kendi kiracısında taze bir oturum açılır

Kritik negatif senaryo — K-277 davranışı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | K-277 |

**Ön koşul**
- `kiraci-alfa` altında `paylasilan-oturum-id` adlı bir oturum zaten var
  (`X-Tenant-Id: kiraci-alfa` ile `POST {APU}/api/agents/support/run -d
  '{"sessionId":"paylasilan-oturum-id","message":"merhaba"}'`).

**Adımlar**
1. VARSAYILAN kiracı (`X-Tenant-Id` başlığı YOK) ile aynı oturum kimliğine
   bağlan.

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session paylasilan-oturum-id --token manuel-test-token-2026
curl -s "$APU/api/sessions/paylasilan-oturum-id" -H "$APB" -H "X-Tenant-Id: kiraci-alfa" \
     | python3 -c "import json,sys; print(len(json.load(sys.stdin).get('items', [])))"
```

**Beklenen sonuç**
- WebSocket bağlantısı BAŞARIYLA kurulur (`ready` çerçevesi gelir) — depo
  kiracı sınırlıdır, `kiraci-alfa`'nın oturumu varsayılan kiracıya
  GÖRÜNMEZ ve "yok" sayılır; bağlantı kendi kiracısında TAZE bir oturum
  açar. İkinci komut `kiraci-alfa`'nın orijinal oturumunun mesaj sayısının
  DEĞİŞMEDİĞİNİ (yalnızca ilk "merhaba" turu) gösterir — WebSocket
  bağlantısı o kayda hiç dokunmamıştır.

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

### MT-MM-070 — Uçtan uca bir tur: `start → ready → commit → transcript → runStarted → text → audioStart → audioEnd → done`

Gerçek entegrasyon — mutlu yol, kritik.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-tur-1 --voice-id "$VOICE_ID"
```

**Beklenen sonuç**
- Çerçeveler bu SIRAYLA görünür: `ready` → `transcript` (`final: true`) →
  `runStarted` (bir `runId`) → bir veya daha fazla `text` (`delta` alanı) →
  `audioStart` (`mediaType: "audio/mpeg"`) → en az bir `[ikili ses
  cercevesi]` → `audioEnd` → `done` (`cancelled: false`, `turn: 1`).
  `transcript.text` hiçbir zaman gerçek metne bağlanmaz (sinüs tonu
  konuşma değildir; boş veya anlamsız bir dize gelebilir — bu KUSUR
  DEĞİLDİR).

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

### MT-MM-071 — Her tur normal bir `runs` satırı üretir — çalıştırma yolu DEĞİŞMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MM-070'in `runStarted` çerçevesinden `runId`.

**Doğrulama sorgusu**
```sql
SELECT status, agent_name, model_id, input_tokens, output_tokens, session_id
FROM agentprism.runs WHERE id = '<RUN_ID>';
```

**Beklenen sonuç**
- Bir satır: `status='Completed'`, `agent_name='sesli-asistan'`,
  `input_tokens`/`output_tokens` pozitif — ses tur yolu OpenAI'a normal
  bir çalıştırma gibi gider; token sayımı, span'ler ve maliyet MEVCUT
  yoldan gelir, TEKRAR EDİLMEZ.

**Gerçek sonuç**
Bir satır: `status: Completed` (API), `agent_name='sesli-asistan'`,
`model_id='gpt-5.4-mini'`, `input_tokens=392`, `output_tokens=17`,
`session_id='manuel-ws-tur-1'` — hepsi pozitif ve doğru. Not: SQL sorgusu
`status` sütununu ham tamsayı (`1`) döndürüyor, doğrudan enum metni değil
— `GET /api/runs/{id}` üzerinden okundu (`Completed`), doküman sapması
değil, yalnızca ölçüm kolaylığı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-072 — İkinci `start` REDDEDİLİR — agent/oturum/kiracı bağlantı boyunca sabittir

Sınır senaryosu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-ikinci-start --repeat-start
```

**Beklenen sonuç**
- İlk `start` normal şekilde `ready` üretir. İkinci `start` (aynı
  bağlantı üzerinden) bir `error` çerçevesiyle reddedilir VEYA sessizce
  yok sayılır (durum makinesi `Rejected` döner) — bağlantı KAPANMAZ, ilk
  `start`'ın açtığı tur yoluna devam eder. Hangi davranışın gerçekleştiği
  (`error` çerçevesi mi, sessiz yok sayma mı) koşum notuna yazılır.

**Gerçek sonuç**
İlk `start` `ready` üretti. İkinci `start` AÇIK bir `error` çerçevesiyle
reddedildi: `{"type": "error", "message": "Konusma zaten baslatildi; agent baglanti boyunca degismez."}`.
Bağlantı kapanmadı — ardından gönderilen `stop` normal şekilde işlendi
(bağlantı kapalı olsaydı istisna fırlardı). Davranış: **açık `error`
çerçevesi**, sessiz yok sayma DEĞİL.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-073 — Ses göndermeden `commit` — tur ÜRETİLMEZ, dinlemeye geri döner

Sınır senaryosu — kısa bir öksürük VAD'i yanlışlıkla tetiklerse bağlantı
kopmamalıdır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-bos-commit \
  --send-audio-before-start --audio-seconds 0.0 --no-commit
# Not: bu komut once "commit"i ELLE gondermeden BOS bir ses gonderir; ardindan
# aracin kendisi commit gonderir. Boş parca (0 saniye) VoiceUtteranceBuffer'in
# HasAudio=false durumunu tetikler.
```

**Beklenen sonuç**
- `commit` sonrası hiçbir `transcript`/`runStarted`/`done` gelmez; sunucu
  boş parçayı sessizce atar ve `Listening` durumuna döner. Bağlantı
  30 saniyelik `--max-wait-seconds` sonunda "ZAMAN ASIMI" ile biter — bu
  BEKLENEN sonuçtur (hata değil): boş bir konuşma turu SAYILMAZ.

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

### MT-MM-074 — Bilinmeyen `inputFormat` → `error` çerçevesi

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-bad-format --format mp3
```

**Beklenen sonuç**
- İlk gelen çerçeve `{"type": "error", "message": "...mp3..."}` içerir —
  `VoiceAudioFormats.IsKnown` yalnız `webm-opus`/`pcm16`/boş kabul eder.

**Gerçek sonuç**
`{"type": "error", "message": "Bilinmeyen ses bicimi: 'mp3'."}` — tam
beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-075 — Olmayan bir agent adıyla `start` → `error` çerçevesi, agent adını içerir

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-bad-agent --agent yok-boyle-bir-agent
```

**Beklenen sonuç**
- `{"type": "error", "message": "...yok-boyle-bir-agent..."}`. Bağlantı
  hemen kapanmaz; `stop` göndererek düzgün kapatılabilir (araç bunu zaten
  yapar).

**Gerçek sonuç**
`{"type": "error", "message": "'yok-boyle-bir-agent' adinda bir agent yok."}`
— agent adını içeriyor, tam beklendiği gibi. `stop` düzgün gönderildi,
bağlantı zaten kapalı değildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 12 — Gerçek Zamanlı Konuşma: Kesinti, Sınırlar, Kayıt (Faz 29)

### MT-MM-078 — Kesinti (`cancel`) çalıştırmayı `Canceled` yapar; yarım yanıt geçmişe "kesildi" notuyla yazılır

Gerçek entegrasyon — kritik.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-kesinti-1 --cancel-on-audio-start
curl -s "$APU/api/sessions/manuel-ws-kesinti-1" -H "$APB" | python3 -m json.tool | tail -20
```

**Beklenen sonuç**
- Araç `audioStart` çerçevesi gelir gelmez `cancel` gönderir; `done`
  çerçevesi `cancelled: true` taşır. `GET /api/sessions/...` çıktısındaki
  son asistan mesajı `[Yanit kullanici tarafindan kesildi.]` dizgisini
  içerir (metnin tamamı DEĞİL, yalnız bu dizginin varlığı doğrulanır).

**Doğrulama sorgusu**
```sql
SELECT status FROM agentprism.runs WHERE id = '<runStarted cerçevesindeki runId>';
```

**Beklenen sonuç (SQL)**
- `status = 'Canceled'`.

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

### MT-MM-079 — `MaxConcurrentConnectionsPerTenant` sınırı — sınır soket YÜKSELTİLMEDEN önce ayrılır

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Adımlar**
1. Varsayılan sınır **5**'tir. Aynı anda 6 bağlantı açmayı dene (5 arka
   planda açık kalır, 6.'sı reddedilmelidir).

**Girilecek veri**
```bash
for i in 1 2 3 4 5; do
  python3 ~/agentprism-manuel-test/voice_client.py --session "manuel-ws-sinir-$i" \
    --no-commit --max-wait-seconds 20 &
done
sleep 2
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-sinir-6 --no-commit --max-wait-seconds 5
wait
```

**Beklenen sonuç**
- İlk beş komut arka planda `ready` alır ve açık kalır (20 saniye
  boyunca `commit` gönderilmediği için zaman aşımıyla kapanır). Altıncı
  (ön plandaki) komut `BAGLANTI REDDEDILDI` yazdırır — el sıkışma `429`
  ile düşer, sınır tam **5**'te uygulanır.

**Gerçek sonuç**
Tam beklendiği gibi: ilk 5 bağlantının hepsi `ready` aldı, `commit`
gönderilmediği için 20 saniyelik zaman aşımıyla kapandı. 6. bağlantı
`BAGLANTI REDDEDILDI: server rejected WebSocket connection: HTTP 429`
ile anında reddedildi — sınır tam **5**'te uygulanıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-080 — Ses VARSAYILAN olarak SAKLANMAZ

Kritik güvenlik doğrulaması (kişisel veri).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | K-225 |

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-nopersist
curl -s "$APU/api/attachments?sessionId=manuel-ws-nopersist" -H "$APB" \
     | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- İlk komutun `ready` çerçevesinde `"persistAudio": false`. `done`
  çerçevesinde `attachmentId` alanı YOK (veya `null`). İkinci komut `0`
  yazdırır — o oturuma bağlı hiçbir ek YOKTUR.

**Gerçek sonuç**
Tam beklendiği gibi: `ready` içinde `"persistAudio": false`, `done`
çerçevesinde `attachmentId` alanı yoktu, ikinci komut `0` döndü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-081 — `PersistAudio` açıkken YALNIZ agent'ın sesi eke yazılır — kullanıcının sesi HİÇ saklanmaz

Kritik senaryo — S3 (docs/29).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | K-225 |

**Adımlar**
1. `cd samples/AgentPrism.Api && dotnet user-secrets set "AgentPrism:Voice:Conversation:PersistAudio" "true"`,
   uygulamayı yeniden başlat.

**Girilecek veri**
```bash
python3 ~/agentprism-manuel-test/voice_client.py --session manuel-ws-persist
curl -s "$APU/api/attachments?sessionId=manuel-ws-persist" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `ready` çerçevesinde `"persistAudio": true`. `done` çerçevesinde
  `attachmentId` DOLUDUR. İkinci komut TAM OLARAK `1` ek listeler,
  `mediaType: "audio/mpeg"` (agent'ın konuştuğu ses) — kullanıcının
  gönderdiği sinüs tonu HİÇBİR ek olarak görünmez, çünkü kullanıcının sesi
  hiçbir zaman diske/veritabanına yazılmaz.
- **Geri al:** `dotnet user-secrets remove "AgentPrism:Voice:Conversation:PersistAudio"`,
  yeniden başlat.

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

### MT-MM-082 — `voice_sessions` kaydı yazılır — ses İÇERMEZ, `turns`/`endReason` doğrudur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MM-070 (veya benzeri bir tur) tamamlanmış ve bağlantı `stop` ile
  kapanmış olmalı (araç zaten `stop` gönderir).

**Girilecek veri**
```bash
curl -s "$APU/api/voice/sessions?sessionId=manuel-ws-tur-1" -H "$APB" | python3 -m json.tool
```

**Doğrulama sorgusu**
```sql
SELECT session_id, turns, end_reason, input_seconds, output_chars
FROM agentprism.voice_sessions WHERE session_id = 'manuel-ws-tur-1';
```

**Beklenen sonuç**
- Hem HTTP yanıtı hem SQL satırı: `turns >= 1`, `endReason: "Client"`
  (araç `stop` gönderir). Ne HTTP gövdesinde ne SQL sütunlarında ham ses
  baytı yer alır — tablo yalnız özet ölçümdür.

**Gerçek sonuç**
HTTP: `turns:1`, `endReason:"Client"`, `inputSeconds:1.5`, `outputChars:46`.
SQL: aynı değerler doğrulandı (`end_reason` sütunu ham tamsayı `0` — API'nin
`"Client"` metnine karşılık gelen enum değeri). Ne HTTP'de ne SQL'de ham ses
baytı yok — yalnız özet ölçüm. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-083 — Ham PCM çözüme WAV başlığıyla gider — 44 baytlık RIFF başlığı eklenir

Sınır senaryosu — teknik doğrulama.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Beklenen sonuç (izlek C — kaynak okuması + repo'nun testi)**
- `VoiceUtteranceBuffer.WriteWaveFile` her PCM parçasının önüne sabit
  **44** baytlık bir `RIFF`/`WAVE`/`fmt `/`data` başlığı yazar; `dTOra
  sample rate` alanı `InputSampleRate` (varsayılan **16000**) ile doldurulur.
  Repo'nun kendi testi (`VoiceConversationTests.Uctan_uca_bir_tur_konusma`)
  bunu `voice.ReceivedBytes.ShouldBe(3200 + 44)` ile ölçer — 3200 bayt PCM
  gönderilince çözüm ucuna giden bayt sayısı tam 3244'tür. Bu case koşum
  sırasında bu testin GEÇTİĞİNİ doğrulamakla yetinir (gerçek ElevenLabs'e
  giden multipart gövdenin bayt sayısını manuel ölçmek pratik değildir):
  `dotnet test tests/AgentPrism.Core.UnitTests -c Release --no-build --filter "FullyQualifiedName~VoiceUtteranceBufferTests"`.

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

### MT-MM-086 — Canlı transkript ve altyazı, gerçek bir turda arayüzde akar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Ön koşul**
- Chrome'da `http://localhost:5080/agentprism/playground/sesli-asistan`
  açık; tarayıcı mikrofon iznini kabul etmiş.

**Adımlar**
1. Mikrofon düğmesine tıkla, gerçekten bir cümle söyle (ör. "Merhaba, nasılsın?").
2. Konuşmayı bitir ve "Send now" düğmesine bas (veya 900 ms sessizliği bekle).

**Beklenen sonuç**
- Ses seviyesi göstergesi (12 çubuk) konuşurken hareket eder. "Send now"a
  basılınca (veya VAD tetiklenince) canlı transkript alanı dolar (metin
  gerçek konuşmaya bağlı olduğu için TAM metin değişmez sayılmaz — yalnız
  alanın BOŞ kalmadığı doğrulanır). Ardından model yanıtı altyazı olarak
  akar (`text.delta` çerçeveleri arayüzde birikir) ve ses otomatik çalar.

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

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-087 — Agent konuşurken "Interrupt" düğmesi görünür; basılınca kesinti gerçekleşir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MM-086 gibi bir tur başlatılmış, agent şu anda sesli yanıt veriyor
  (ses çalıyor).

**Adımlar**
1. Ses çalarken "Interrupt" düğmesine bas.

**Beklenen sonuç**
- Ses ANINDA durur. Transcript'e `[Yanit kullanici tarafindan kesildi.]`
  notu eklenir (veya buna karşılık gelen bir görsel işaret). Panel
  dinleme durumuna geri döner; yeni bir tur hemen başlatılabilir.

**Gerçek sonuç**
MT-MM-086 ön koşulu koşulamadığı için bu case de koşulamadı — aynı fiziksel
eylem engeli (gerçek mikrofon + gerçek konuşma gerekir). §5.3 tablosuna
eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-088 — `persistAudio` açıkken görünür bir rozet belirir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | K-225 |

**Ön koşul**
- MT-MM-081'deki gibi `PersistAudio=true` geçici olarak açık.

**Adımlar**
1. Konuşma panelini aç.

**Beklenen sonuç**
- Panelde açıkça okunur bir rozet/uyarı belirir (metni "Audio of the
  reply is being stored" temalıdır — i18n sözlüğüne göre tam metin
  değişebilir, yalnız BİR uyarının göründüğü doğrulanır). `PersistAudio`
  kapalıyken bu rozet HİÇ görünmemelidir (MT-MM-086 ile karşılaştır).
- **Geri al:** `PersistAudio` ayarını kaldır, uygulamayı yeniden başlat.

**Gerçek sonuç**
`PersistAudio=true` zaten açıktı (MT-MM-081'den). Panel açıldı ama
`getUserMedia()` mikrofon iznini gerektirdiği için "connecting" durumunda
takıldı (MT-MM-086 ile AYNI engel) — rozetin göründüğü/görünmediği durum
gözlemlenemedi, çünkü panel `ready` durumuna hiç ulaşmadı. Gerçek mikrofon
izni GEREKTİREN bir fiziksel eylem — §5.3 tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-MM-089 — Güvenli bağlam yoksa panel açılmaz, açık bir mesaj gösterilir

Negatif senaryo — bu makinede yalnız DOLAYLI doğrulanabilir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

`getUserMedia` yalnız güvenli bağlamda (HTTPS veya `localhost`) çalışır.
Bu makinede uygulama her zaman `localhost` üzerinden test edildiği için
GERÇEK bir güvensiz-bağlam denemesi bu ortamda pratik değildir.

**Adımlar**
1. Mac'in yerel ağ IP adresini bul (`ipconfig getifaddr en0`).
2. Chrome'da `http://<yerel-ip>:5080/agentprism/playground/sesli-asistan`
   adresini aç (`localhost` DEĞİL, IP adresiyle — HTTP üzerinden).
3. Mikrofon düğmesine tıkla.

**Beklenen sonuç**
- Panel ya hiç açılmaz ya da açılıp içinde "mikrofon erişimi güvenli bir
  bağlam gerektirir" temalı açık bir mesaj gösterir; sessiz bir başarısızlık
  (konsol hatası dışında hiçbir görünür belirti olmaması) KABUL EDİLEMEZ.
  IP adresi üzerinden AgentPrism'e erişim `AllowRemoteAccess` ayarı
  KAPALIYSA zaten backend'de `403` ile reddedilir — bu durumda case
  `⏭ ATLA — AllowRemoteAccess kapalı, tarayıcı güvenli-bağlam denemesine hiç
  ulaşamıyor` notuyla işaretlenir ve `AllowRemoteAccess` GEÇİCİ olarak
  açılarak tekrar denenebilir.

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

### MT-MM-090 — i18n/tema hızlı geçiş kontrolü — konuşma paneli metinleri

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 29, 30 |
| **İlgili karar** | K-228 |

**Adımlar**
1. Konuşma panelini aç (mikrofon düğmesi).
2. Dil anahtarını TR↔EN değiştir (command palette veya ayarlar).
3. Tema anahtarını açık↔koyu değiştir.

**Beklenen sonuç**
- Panel içindeki tüm sabit metinler (düğme etiketleri, rozet, boş durum
  mesajı) dil değişince çevrilir; hiçbir İngilizce/Türkçe karışık metin
  kalmaz (`en.ts`/`tr.ts` anahtar kümesi K-228 gereği derleme zamanında
  eşleşir, bu yalnız GÖRSEL bir gözle kontrol). Tema değişince kontrast
  bozulmaz, ses seviyesi çubukları her iki temada da okunur kalır.

**Gerçek sonuç**
MT-MM-086 ile AYNI engel: panel `getUserMedia()` mikrofon izni bekliyor,
gerçek mikrofon olmadan "connecting"te takılı kalıyor — panel içi metinler
gözlemlenemedi. Gerçek mikrofon izni GEREKTİREN bir fiziksel eylem —
§5.3 tablosuna eklendi.

**Durum:** ☑ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

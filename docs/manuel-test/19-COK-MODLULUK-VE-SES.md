# 19 — Çok Modluluk: Ek, Ses Tool'ları ve Gerçek Zamanlı Konuşma (`MM`)

> **Alan kodu:** `MM` · **Faz:** 14, 28, 29, 72, 88
> **Kaynak:** `src/Tracon.Abstractions/Attachments/` (tümü) ·
> `src/Tracon.Core/Attachments/` (tümü) ·
> `src/Tracon.AspNetCore/Endpoints/AttachmentEndpoints.cs` ·
> `src/Tracon.AspNetCore/OpenAICompat/AttachmentIngestion.cs` ·
> `src/Tracon.Voice/` (tümü — `Tracon.Voice` paketi) ·
> `src/Tracon.Abstractions/Voice/` (tümü — `SpeechContracts.cs`,
> `SpeechModels.cs`, `ConversationContracts.cs`) ·
> `src/Tracon.Core/Voice/` (tümü — gerçek zamanlı konuşma sürücüsü) ·
> `src/Tracon.AspNetCore/Endpoints/VoiceEndpoints.cs` ·
> `src/Tracon.AspNetCore/Voice/VoiceConversationEndpoint.cs` ·
> `src/Tracon.AspNetCore/TraconEndpointRouteBuilderExtensions.cs`
> (yalnız `MapVoiceConversation` — `UseWebSockets()` koşullu kurulumu) ·
> `src/Tracon.Core/Images/` (tümü) ·
> `src/Tracon.AspNetCore/Endpoints/ImageEndpoints.cs` ·
> `src/Tracon.{OpenAI,Azure,Google}/*Image*` ·
> Migration'lar: `attachments`/`agent_files` (`0006`), `tool_invocations` beş
> ölçüm sütunu (`0015`), `voice_sessions` (`0016`).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** son tur (2026-09-16):
> [`../arsiv/manuel-test-kosum-2026-09/19-COK-MODLULUK-VE-SES.md`](../arsiv/manuel-test-kosum-2026-09/19-COK-MODLULUK-VE-SES.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır. 2026-08-13 turunun kaydı silindi (K-847);
> tam metin: git show 64c8a103:docs/manuel-test/kosumlar/2026-08-13/19-COK-MODLULUK-VE-SES.md

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

> **Rol matrisi burada da örnek uygulamada NO-OP'tur.** `samples/Tracon.Api/Program.cs`
> hiçbir `TraconRolePolicies` rol policy'si kaydetmez (`grep -n "AddAuthorization\|RolePolic" samples/Tracon.Api/Program.cs`
> boş döner) — bu genel davranış `13-KIRACI-VE-GUVENLIK.md`'de test edildi.
> Bu dosyadaki tüm uçlar (`RequireRole(roles.Operator)` dahil) bu yüzden yalnız
> loopback + bearer token katmanlarından geçer; ayrıca rol reddi test edilmez.

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080/tracon`.
3. **§1–4 (ek) hiçbir ek yapılandırma istemez** — `Tracon.Core` her zaman
   kurulur. **§5'ten itibaren (ses)** `Tracon:Voice:ApiKey` (gerçek
   ElevenLabs anahtarı) gerekir; `00-INDEKS.md` §2.4'e göre zaten ayarlıdır.
   Anahtar tanımlıysa örnek uygulama `UseVoice()` **ve** `UseVoiceConversation()`'ı
   birlikte açar (`Program.cs:212-226`) — ikisi ayrı ayrı açılıp kapatılamaz;
   §5'in ilk case'i (MT-MM-031) bunu bilerek bu çiftin geçici olarak
   KAPATILMASIYLA test eder.
4. `voice-assistant` agent'ı fixture'dır (`00-INDEKS.md` §3.1): tool'ları
   `speak`, `transcribe`, `list_voices`, `get_order_status`; modeli gerçek
   OpenAI (`gpt-5.4-mini`).

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
export PG="docker exec -i ap-pg psql -U postgres -d tracon"
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
| `FIX-MM-WS-CLIENT` | `~/tracon-manuel-test/voice_client.py` — §9'da kurulan tester-tedarikli WebSocket istemcisi |

```bash
mkdir -p ~/tracon-manuel-test
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

---

### MT-MM-005 — `TraconAttachmentOptions.MaxBytes` (varsayılan 20 MB) aşımı reddedilir

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
         'http://localhost:5080/tracon/api/attachments',
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
- `FIX-TENANT-01` (`kiraci-alfa`) altında bir ek yüklenmiş (`X-Tracon-Tenant: kiraci-alfa` başlığıyla).
- Çok kiracılık açık: `Tracon:Tenancy:Enabled=true`, `Tracon:Tenancy:AllowHeaderResolution=true`
  (varsayılan kapalı — `13-KIRACI-VE-GUVENLIK.md` §5 deseniyle aynı).

**Girilecek veri**

> **Doküman düzeltmesi:** Örnek başlık adı `X-Tenant-Id` idi; gerçek başlık
> `HttpTenantContext.cs:50`'de `X-Tracon-Tenant`dır (bkz. `13-KIRACI-VE-GUVENLIK.md`
> MT-SEC-021, aynı başlığı doğru kullanıyor). Aşağıda düzeltildi.

```bash
curl -s -X POST "$APU/api/attachments" -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa" \
     -F "file=@/tmp/fix-mm.png;type=image/png" | python3 -c "import json,sys; print(json.load(sys.stdin)['id'])"
# Yukaridaki id'yi ALFA_ID olarak sakla, sonra BETA kiracisiyla dene:
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/attachments/$ALFA_ID" -H "$APB" -H "X-Tracon-Tenant: kiraci-beta" -o /dev/null
```

**Beklenen sonuç**
- İkinci istek `HTTP: 404` — "böyle bir ek yok" mesajı; ekin `kiraci-alfa`'ya
  ait olduğu bilgisi hiçbir şekilde sızmaz (403 değil, 404).

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
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" -H "X-Tracon-Tenant: kiraci-beta" \
     -H "content-type: application/json" -d "{
  \"message\": \"bu ek nedir\",
  \"attachmentIds\": [\"$ALFA_ID\"]
}"
```

**Beklenen sonuç**
- `HTTP: 400`, "Ek bulunamadi" — `kiraci-beta` `kiraci-alfa`'nın ekini
  göremez, akış başlamaz.

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
  `image_url`/`input_file` okumaz. Bu, `docs/arsiv/fazlar/14-COK-MODLULUK.md`'nin
  bilinçli kapsam kararıdır; koşum bu davranışı doğrular/çürütür.

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

### MT-MM-031 — `Tracon:Voice:ApiKey` yoksa `/api/voice/*` `501`, konuşma ucu `404` döner

Negatif/sınır senaryosu — kritik. **Geçici yapılandırma değişikliği ister.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 28, 29 |
| **İlgili karar** | — |

**Adımlar**
1. `cd samples/Tracon.Api && dotnet user-secrets remove "Tracon:Voice:ApiKey"`.
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
  `dotnet user-secrets set "Tracon:Voice:ApiKey" "<ELEVENLABS_ANAHTARINIZ>"`,
  uygulamayı yeniden başlat — sonraki tüm case'ler bu anahtara ihtiyaç duyar.

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
cd samples/Tracon.Api
dotnet user-secrets set "Tracon:Voice:OutputFormat" "pcm_16000"
dotnet run
```

**Beklenen sonuç**
- Uygulama açılışta `OptionsValidationException` ile ÇÖKER; mesaj
  `format 'pcm_16000' cannot be stored as an attachment` metnini içerir
  (`VoiceOptionsValidator.IsStorableFormat`).
- **Geri al:** `dotnet user-secrets remove "Tracon:Voice:OutputFormat"`,
  yeniden başlat.

> **Doküman düzeltmesi (2026-09-16 koşumu):** Beklenen mesaj metni Türkçe
> yazılmıştı; `VoiceOptionsValidator.cs`'in gerçek metni İngilizce'dir
> (K-228 — paket koduna giren her şey İngilizce, yalnız `docs/`/`.agents/`
> Türkçe kalır). Yukarıda düzeltildi.

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
cd samples/Tracon.Api
dotnet user-secrets set "Tracon:Voice:Provider" "azure-cognitive-speech"
dotnet run
```

**Beklenen sonuç**
- Açılış çöker; mesaj `provider 'azure-cognitive-speech' is not recognized.
  Built-in provider: 'elevenlabs'` metnini içerir.
- **Geri al:** `dotnet user-secrets remove "Tracon:Voice:Provider"`,
  yeniden başlat.

> **Doküman düzeltmesi (2026-09-16 koşumu):** Beklenen mesaj metni Türkçe
> yazılmıştı; gerçek metin İngilizce'dir (K-228). Yukarıda düzeltildi.

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
cd samples/Tracon.Api
dotnet user-secrets set "Tracon:Voice:MaxConcurrentRequests" "0"
dotnet run
```

**Beklenen sonuç**
- Açılış çöker; mesaj `concurrent request limit must be greater than zero`
  içerir.
- **Geri al:** `dotnet user-secrets remove "Tracon:Voice:MaxConcurrentRequests"`,
  yeniden başlat.

> **Doküman düzeltmesi (2026-09-16 koşumu):** Beklenen mesaj metni Türkçe
> yazılmıştı; gerçek metin İngilizce'dir (K-228). Yukarıda düzeltildi.

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
   `dotnet user-secrets set "Tracon:Voice:ApiKey" "SAHTE-GECERSIZ-ANAHTAR-xyz789"`,
   uygulamayı yeniden başlat (bu geçerli bir açılış — doğrulama yalnız
   BOŞLUĞU kontrol eder, gerçekliği değil).
2. Gerçek bir çağrı yap.

**Girilecek veri**
```bash
curl -s "$APU/api/voice/health" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `isHealthy: false`. `detail` alanı yalnız HTTP durum kodunu ve genel bir
  ipucu içerir (`" The API key is invalid."`) — `SAHTE-GECERSIZ-ANAHTAR-xyz789`
  metni yanıtın HİÇBİR YERİNDE görünmez. Uygulama loglarını da tara: anahtar
  orada da görünmemelidir.
- **Geri al:** gerçek anahtarı tekrar ayarla, yeniden başlat.

> **Doküman düzeltmesi (2026-09-16 koşumu):** İpucu metni Türkçe yazılmıştı;
> `ElevenLabsSpeechClient.cs`'in gerçek metni İngilizce'dir (K-228).
> Yukarıda düzeltildi.

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
  \"text\": \"Tracon manuel test seslendirmesi.\",
  \"sessionId\": \"manuel-mm-1\",
  \"voiceId\": \"$VOICE_ID\"
}" | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200`. Gövdede `attachment.mediaType: "audio/mpeg"`, `characters`
  pozitif bir tam sayı, `isEstimated` bir `bool`. `Tracon:Pricing:Voice`
  yapılandırılmamışsa `cost`/`currency` `null` — SIFIR değil (K-032).
  `attachment.id`'yi `GET {APU}/api/attachments/{id}` ile indirip gerçekten
  çalan bir MP3 olduğunu doğrula.

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
`MaxCharactersPerRequest` özelliği eklendi (`Tracon.Abstractions` —
`Tracon.AspNetCore`, `Tracon.Voice`'a referans VEREMEZ, bu yüzden
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
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/voice-assistant/run" -H "$APB" \
     -H "content-type: application/json" -d '{
  "sessionId": "manuel-mm-speak-1",
  "message": "Merhaba dedigimi sesli soyle."
}'
```

**Doğrulama sorgusu**

> **Doküman düzeltmesi:** `tool_name` sütunu `attachments` tablosunda yok
> (bkz. `kosumlar/2026-08-13/`). Aşağıda çıkarıldı.

```sql
SELECT session_id, run_id, media_type, byte_size
FROM tracon.attachments
WHERE session_id = 'manuel-mm-speak-1'
ORDER BY created_at DESC LIMIT 1;
```

**Beklenen sonuç**
- Akış tamamlanır, yanıtta bir tool çağrısı görünür (`speak`), sonuç metni
  `attachmentId=...` biçimindedir (ham ses DEĞİL — bağlam penceresine base64
  konmaz). SQL sorgusu **tek bir satır** döner ve `session_id` alanı
  **DOLUDUR** (`NULL` değil) — G1'in düzeltmesi: tool `AgentRunScope.SessionId`
  üzerinden oturum kimliğini görür, ek sahipsiz sayılıp silinmez.

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
curl -s -X POST "$APU/api/agents/voice-assistant/run" -H "$APB" \
     -H "content-type: application/json" \
     -d "{ \"sessionId\": \"manuel-mm-speak-2\", \"message\": $(python3 -c "import json; print(json.dumps(open('/tmp/fix-mm-tool-uzun.txt').read()))") }"
```

**Beklenen sonuç**
- Model `speak` tool'unu 6000 karakterlik metinle çağırırsa tool bir HATA
  metni döner (`Metin 6000 karakter; sinir 5000. ...`) — metin sessizce
  KIRPILMAZ; model hatayı görüp kullanıcıya açıklayabilir. (Model tool'u
  hiç çağırmamayı seçerse case `⏭ ATLA — model tool'u tetiklemedi` notuyla
  işaretlenir ve tekrar denenir.)

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
curl -s -X POST "$APU/api/agents/voice-assistant/run" -H "$APB" \
     -H "content-type: application/json" -d "{
  \"sessionId\": \"manuel-mm-transcribe-1\",
  \"message\": \"attachmentId=$SPEECH_ATT_ID olan ses ekini transcribe tool'uyla metne cevir ve sonucu aynen yaz.\"
}"
```

**Beklenen sonuç**
- Yanıt `transcribe` tool çağrısı içerir; sonuç `[dil=...] ...` biçiminde
  bir metindir (metnin İÇERİĞİ değişmez sayılmaz — yalnız tool'un
  çağrıldığı ve boş olmayan bir metin döndürdüğü doğrulanır).

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
curl -s -X POST "$APU/api/agents/voice-assistant/run" -H "$APB" \
     -H "content-type: application/json" -d "{
  \"sessionId\": \"manuel-mm-transcribe-2\",
  \"message\": \"attachmentId=$ATT_ID olan eki transcribe tool'uyla metne cevirmeyi dene.\"
}"
```

**Beklenen sonuç**
- Tool sonucu modele **özgül** hatayı taşır (jenerik `Error: Function failed.`
  değil): `The attachment with id '<id>' is not an audio file (type: image/png).`
  — `descriptor.MediaType.StartsWith("audio/")` kontrolü. Metin İngilizce'dir
  (K-228); bu satır 2026-09-19'a kadar Türkçe yazılmıştı ve bayattı.
- Tool işlemi **durdurur** — PNG transcribe edilmez, yan etki yoktur.

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
curl -s -X POST "$APU/api/agents/voice-assistant/run" -H "$APB" \
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
FROM tracon.tool_invocations
WHERE run_id = '<RUN_ID>' AND tool_name = 'speak';
```

**Beklenen sonuç**
- Bir satır: `usage_unit='characters'`, `usage_quantity` pozitif,
  `usage_estimated` `true`/`false` (ElevenLabs `character-cost` başlığı
  döndürüp döndürmediğine bağlı), `cost_currency` fiyat yapılandırılmışsa
  dolu, değilse `cost` **`NULL`** — sıfır DEĞİL.

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
SELECT usage_unit, usage_quantity FROM tracon.tool_invocations
WHERE run_id = '<RUN_ID>' AND tool_name = 'transcribe';
```

**Beklenen sonuç**
- `usage_unit='seconds'`, `usage_quantity` sesin uzunluğuna yakın bir
  ondalık (küçük bir test sesi için birkaç saniye).

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
SELECT count(*) FROM tracon.tool_invocations WHERE tool_name = 'speak'
  AND created_at > now() - interval '5 minutes';
```

**Beklenen sonuç**
- Bu sayı, MT-MM-046/047'de agent'ın `speak` tool'unu kaç kez çağırdığıyla
  BİREBİR eşleşir — MT-MM-040'ın operatör çağrısı HİÇ eklemez.
  `tool_invocations.run_id` zorunlu bir yabancı anahtardır ve operatör
  eyleminin bağlı olduğu bir `run` yoktur; ölçüm yalnız HTTP yanıtında
  görünür kalır (kalıcı değildir).

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

cat > ~/tracon-manuel-test/voice_client.py << 'PYEOF'
#!/usr/bin/env python3
"""Tracon gercek zamanli konusma WebSocket istemcisi (tester-tedarikli)."""
import argparse
import asyncio
import json
import math
import struct
import sys

import websockets

SUBPROTOCOL = "tracon.voice.v1"
TOKEN_PREFIX = "tracon.token."

def make_tone(seconds: float, sample_rate: int, freq: float = 440.0) -> bytes:
    n = int(sample_rate * seconds)
    samples = [int(3000 * math.sin(2 * math.pi * freq * i / sample_rate)) for i in range(n)]
    return struct.pack(f"<{n}h", *samples)

async def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--host", default="localhost:5080")
    p.add_argument("--prefix", default="/tracon")
    p.add_argument("--session", default="manuel-ws-1")
    p.add_argument("--agent", default="voice-assistant")
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

python3 ~/tracon-manuel-test/voice_client.py --help
```

**Beklenen sonuç**
- `pip3 install` hatasız biter. `--help` çıktısı yukarıdaki argüman
  listesini gösterir.

### MT-MM-062 — `UseVoiceConversation()` açıksa ama sağlayıcı yoksa `501`; hiç çağrılmadıysa `404`

Bu ayrım MT-MM-031'de `voice-assistant` çiftinin (Voice+VoiceConversation
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
  `dotnet test tests/Tracon.AspNetCore.FunctionalTests -c Release --no-build --filter "FullyQualifiedName~VoiceConversationTests.UseVoiceConversation_cagrilmadiysa|FullyQualifiedName~VoiceConversationTests.Ses_saglayicisi_yoksa"`.

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
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-token-ok --token manuel-test-token-2026
```

**Beklenen sonuç**
- `BAGLANDI, kabul edilen alt protokol: tracon.voice.v1` yazdırılır.
  İlk `<<` çerçevesi `{"type": "ready", "agent": "voice-assistant", ...,
  "persistAudio": false}` olur.

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
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-token-bad --token yanlis-token-123
```

**Beklenen sonuç**
- `BAGLANTI REDDEDILDI: ...` — el sıkışma `401` ile düşer (WebSocket
  yükseltmesi hiç tamamlanmaz), sunucu beklenen token hakkında hiçbir
  ipucu vermez.

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
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-query-token \
  --token manuel-test-token-2026 --send-token-in-query --no-token
```

**Beklenen sonuç**
- Komut `--send-token-in-query` ile token'ı `?token=...` sorgu dizesine
  koyar VE `--no-token` ile alt protokol listesinden `tracon.token.*`
  girdisini ÇIKARIR (yalnız `tracon.voice.v1` kalır). Bağlantı
  REDDEDİLİR (`401`) — sunucu yalnız `Sec-WebSocket-Protocol` alt
  protokolüne bakar, sorgu dizesini hiç okumaz. Adres bu şekilde sunucu
  günlüklerine ve tarayıcı geçmişine yazılsa bile token orada geçerli
  sayılmaz.

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
  (`X-Tracon-Tenant: kiraci-alfa` ile `POST {APU}/api/agents/support/run -d
  '{"sessionId":"paylasilan-oturum-id","message":"merhaba"}'`).
  🚨 **Düzeltildi (2026-09-17):** spec `X-Tenant-Id` yazıyordu —
  `TraconTenancyOptions.HeaderName` varsayılanı `X-Tracon-Tenant`'tır
  (dosya 13'ün 21 case'i zaten bunu kullanıyor). Ayrıca bu case'in
  çalışması için sunucunun `Tracon:Tenancy:Enabled=true` VE
  `Tracon:Tenancy:AllowHeaderResolution=true` ile başlatılması gerekir
  (ikisi de varsayılan kapalı — skill'in bilinen tuzağı).

**Adımlar**
1. VARSAYILAN kiracı (`X-Tracon-Tenant` başlığı YOK) ile aynı oturum
   kimliğine bağlan.

**Girilecek veri**
```bash
python3 ~/tracon-manuel-test/voice_client.py --session paylasilan-oturum-id --token manuel-test-token-2026
curl -s "$APU/api/sessions/paylasilan-oturum-id" -H "$APB" -H "X-Tracon-Tenant: kiraci-alfa" \
     | python3 -c "import json,sys; print(len(json.load(sys.stdin).get('messages') or []))"
```

**Beklenen sonuç**
- WebSocket bağlantısı BAŞARIYLA kurulur (`ready` çerçevesi gelir) — depo
  kiracı sınırlıdır, `kiraci-alfa`'nın oturumu varsayılan kiracıya
  GÖRÜNMEZ ve "yok" sayılır; bağlantı kendi kiracısında TAZE bir oturum
  açar. İkinci komut `kiraci-alfa`'nın orijinal oturumunun mesaj sayısının
  DEĞİŞMEDİĞİNİ (yalnızca ilk "merhaba" turu) gösterir — WebSocket
  bağlantısı o kayda hiç dokunmamıştır.

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
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-tur-1 --voice-id "$VOICE_ID"
```

**Beklenen sonuç**
- Çerçeveler bu SIRAYLA görünür: `ready` → `transcript` (`final: true`) →
  `runStarted` (bir `runId`) → bir veya daha fazla `text` (`delta` alanı) →
  `audioStart` (`mediaType: "audio/mpeg"`) → en az bir `[ikili ses
  cercevesi]` → `audioEnd` → `done` (`cancelled: false`, `turn: 1`).
  `transcript.text` hiçbir zaman gerçek metne bağlanmaz (sinüs tonu
  konuşma değildir; boş veya anlamsız bir dize gelebilir — bu KUSUR
  DEĞİLDİR).

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
FROM tracon.runs WHERE id = '<RUN_ID>';
```

**Beklenen sonuç**
- Bir satır: `status='Completed'`, `agent_name='voice-assistant'`,
  `input_tokens`/`output_tokens` pozitif — ses tur yolu OpenAI'a normal
  bir çalıştırma gibi gider; token sayımı, span'ler ve maliyet MEVCUT
  yoldan gelir, TEKRAR EDİLMEZ.

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
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-ikinci-start --repeat-start
```

**Beklenen sonuç**
- İlk `start` normal şekilde `ready` üretir. İkinci `start` (aynı
  bağlantı üzerinden) bir `error` çerçevesiyle reddedilir VEYA sessizce
  yok sayılır (durum makinesi `Rejected` döner) — bağlantı KAPANMAZ, ilk
  `start`'ın açtığı tur yoluna devam eder. Hangi davranışın gerçekleştiği
  (`error` çerçevesi mi, sessiz yok sayma mı) koşum notuna yazılır.

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
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-bos-commit \
  --send-audio-before-start --audio-seconds 0.0
# Not: bu komut BOS bir ses gonderir, ardindan aracin kendisi commit gonderir.
# Boş parca (0 saniye) VoiceUtteranceBuffer'in HasAudio=false durumunu tetikler.
# 🚨 Düzeltildi (2026-09-17): spec ayrıca `--no-commit` taşıyordu — bu bayrak
# istemcinin commit'i HİÇ göndermemesine yol açar (kod: `if not
# args.no_commit`), notun "araç commit gönderir" iddiasıyla ÇELİŞİYORDU.
# `--no-commit` olmadan koşulunca gerçekten commit gidiyor ve senaryo (boş
# parça + commit → tur üretilmez) doğru gözlenebiliyor.
```

**Beklenen sonuç**
- `commit` sonrası hiçbir `transcript`/`runStarted`/`done` gelmez; sunucu
  boş parçayı sessizce atar ve `Listening` durumuna döner. Bağlantı
  30 saniyelik `--max-wait-seconds` sonunda "ZAMAN ASIMI" ile biter — bu
  BEKLENEN sonuçtur (hata değil): boş bir konuşma turu SAYILMAZ.

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
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-bad-format --format mp3
```

**Beklenen sonuç**
- İlk gelen çerçeve `{"type": "error", "message": "...mp3..."}` içerir —
  `VoiceAudioFormats.IsKnown` yalnız `webm-opus`/`pcm16`/boş kabul eder.

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
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-bad-agent --agent yok-boyle-bir-agent
```

**Beklenen sonuç**
- `{"type": "error", "message": "...yok-boyle-bir-agent..."}`. Bağlantı
  hemen kapanmaz; `stop` göndererek düzgün kapatılabilir (araç bunu zaten
  yapar).

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
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-kesinti-1 --cancel-on-audio-start
curl -s "$APU/api/sessions/manuel-ws-kesinti-1" -H "$APB" | python3 -m json.tool | tail -20
```

**Beklenen sonuç**
- Araç `audioStart` çerçevesi gelir gelmez `cancel` gönderir; `done`
  çerçevesi `cancelled: true` taşır. `GET /api/sessions/...` çıktısındaki
  son asistan mesajı `[Yanit kullanici tarafindan kesildi.]` dizgisini
  içerir (metnin tamamı DEĞİL, yalnız bu dizginin varlığı doğrulanır).

**Doğrulama sorgusu**
```sql
SELECT status FROM tracon.runs WHERE id = '<runStarted cerçevesindeki runId>';
```

**Beklenen sonuç (SQL)**
- `status = 'Canceled'`.

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
  python3 ~/tracon-manuel-test/voice_client.py --session "manuel-ws-sinir-$i" \
    --no-commit --max-wait-seconds 20 &
done
sleep 2
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-sinir-6 --no-commit --max-wait-seconds 5
wait
```

**Beklenen sonuç**
- İlk beş komut arka planda `ready` alır ve açık kalır (20 saniye
  boyunca `commit` gönderilmediği için zaman aşımıyla kapanır). Altıncı
  (ön plandaki) komut `BAGLANTI REDDEDILDI` yazdırır — el sıkışma `429`
  ile düşer, sınır tam **5**'te uygulanır.

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
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-nopersist
curl -s "$APU/api/attachments?sessionId=manuel-ws-nopersist" -H "$APB" \
     | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- İlk komutun `ready` çerçevesinde `"persistAudio": false`. `done`
  çerçevesinde `attachmentId` alanı YOK (veya `null`). İkinci komut `0`
  yazdırır — o oturuma bağlı hiçbir ek YOKTUR.

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
1. `cd samples/Tracon.Api && dotnet user-secrets set "Tracon:Voice:Conversation:PersistAudio" "true"`,
   uygulamayı yeniden başlat.

**Girilecek veri**
```bash
python3 ~/tracon-manuel-test/voice_client.py --session manuel-ws-persist
curl -s "$APU/api/attachments?sessionId=manuel-ws-persist" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `ready` çerçevesinde `"persistAudio": true`. `done` çerçevesinde
  `attachmentId` DOLUDUR. İkinci komut TAM OLARAK `1` ek listeler,
  `mediaType: "audio/mpeg"` (agent'ın konuştuğu ses) — kullanıcının
  gönderdiği sinüs tonu HİÇBİR ek olarak görünmez, çünkü kullanıcının sesi
  hiçbir zaman diske/veritabanına yazılmaz.
- **Geri al:** `dotnet user-secrets remove "Tracon:Voice:Conversation:PersistAudio"`,
  yeniden başlat.

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
FROM tracon.voice_sessions WHERE session_id = 'manuel-ws-tur-1';
```

**Beklenen sonuç**
- Hem HTTP yanıtı hem SQL satırı: `turns >= 1`, `endReason: "Client"`
  (araç `stop` gönderir). Ne HTTP gövdesinde ne SQL sütunlarında ham ses
  baytı yer alır — tablo yalnız özet ölçümdür.

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
  `dotnet test tests/Tracon.Core.UnitTests -c Release --no-build --filter "FullyQualifiedName~VoiceUtteranceBufferTests"`.

### MT-MM-086 — Canlı transkript ve altyazı, gerçek bir turda arayüzde akar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 29 |
| **İlgili karar** | — |

**Ön koşul**
- Chrome'da `http://localhost:5080/tracon/playground/voice-assistant`
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
2. Chrome'da `http://<yerel-ip>:5080/tracon/playground/voice-assistant`
   adresini aç (`localhost` DEĞİL, IP adresiyle — HTTP üzerinden).
3. Mikrofon düğmesine tıkla.

**Beklenen sonuç**
- Panel ya hiç açılmaz ya da açılıp içinde "mikrofon erişimi güvenli bir
  bağlam gerektirir" temalı açık bir mesaj gösterir; sessiz bir başarısızlık
  (konsol hatası dışında hiçbir görünür belirti olmaması) KABUL EDİLEMEZ.
  IP adresi üzerinden Tracon'e erişim `AllowRemoteAccess` ayarı
  KAPALIYSA zaten backend'de `403` ile reddedilir — bu durumda case
  `⏭ ATLA — AllowRemoteAccess kapalı, tarayıcı güvenli-bağlam denemesine hiç
  ulaşamıyor` notuyla işaretlenir ve `AllowRemoteAccess` GEÇİCİ olarak
  açılarak tekrar denenebilir.

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

---

### MT-MM-091 — `includeTimestamps` verilmeden `POST /api/voice/speak` bugünkü yanıtla birebir aynıdır (F-118)

Gerçek entegrasyon — mutlu yol, kritik. Regresyon: F-118 öncesi davranışın
değişmediğini kanıtlar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | — |

**Ön koşul**
- `$VOICE_ID` MT-MM-038'den dolu.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/voice/speak" -H "$APB" -H "content-type: application/json" -d "{
  \"text\": \"Zaman damgasiz sentez.\",
  \"sessionId\": \"manuel-mm-timestamps-off\",
  \"voiceId\": \"$VOICE_ID\"
}" | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200`. Gövdede `alignment` alanı ya hiç yok ya da `null` — dolu bir
  liste DEĞİL. `attachment`/`characters`/`isEstimated`/`cost` MT-MM-040 ile
  birebir aynı şekilde davranır.

---

### MT-MM-092 — `includeTimestamps: true` karakter hizalaması döner, süreler ses uzunluğuyla tutarlıdır (F-118)

Gerçek entegrasyon — mutlu yol, kritik.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | — |

**Ön koşul**
- `$VOICE_ID` MT-MM-038'den dolu.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/voice/speak" -H "$APB" -H "content-type: application/json" -d "{
  \"text\": \"Merhaba\",
  \"sessionId\": \"manuel-mm-timestamps-on\",
  \"voiceId\": \"$VOICE_ID\",
  \"includeTimestamps\": true
}" | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200`. `alignment` alanı `"Merhaba"` metnindeki karakter sayısı kadar
  öge taşır (kelime değil — sağlayıcının verdiği granülerlik budur, bkz.
  `docs-site/src/content/docs/guides/voice.md`). Her ögede `character`,
  `start`, `end`; `start`/`end` artan sıradadır ve son ögenin `end` değeri
  indirilen sesin gerçek süresine yakındır (kulakla veya ses dosyasının
  meta verisiyle karşılaştır). 🚨 `alignment[i].character` "orijinal metin"
  hizalamasıdır (`normalized_alignment` DEĞİL) — sağlayıcının sayı/kısaltma
  normalizasyonu yaptığı bir metinle tekrarlanırsa karakterler girdiyle
  birebir eşleşmeye devam eder.

---

### MT-MM-093 — Akışlı sentezde `includeTimestamps: true` açıkça reddedilir, sessizce yok sayılmaz (F-118)

Sınır senaryosu — `ISpeechSynthesizer.SynthesizeStreamingAsync` ham ses
parçası dışında hiçbir şey döndürmez; hizalama verisini taşıyacak bir kanal
yoktur. Kombinasyon istisna ile reddedilir (K1).

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | — |

**Adımlar**
1. `Tracon.Voice.UnitTests.ElevenLabsSpeechClientTests.Streaming_synthesis_rejects_IncludeTimestamps_explicitly`
   testini çalıştır (otomatik koşum; bu case elle tekrar üretmeye gerek
   bırakmaz, davranışın belgesidir):
   ```bash
   dotnet test tests/Tracon.Voice.UnitTests -c Release \
     --filter "FullyQualifiedName~Streaming_synthesis_rejects_IncludeTimestamps_explicitly"
   ```

**Beklenen sonuç**
- Test yeşil. `ISpeechSynthesizer.SynthesizeStreamingAsync` çağrısı
  `request.IncludeTimestamps == true` iken `TraconException` fırlatır;
  hiçbir HTTP isteği ElevenLabs'e gitmez (`handler.Requests` boştur —
  kredi harcanmaz). Akışlı sentez zaten `POST /api/voice/speak`'ten
  ULAŞILAMAZ (o uç her zaman `SynthesizeAsync`'i çağırır); bu case yalnız
  `ISpeechSynthesizer`'ı doğrudan kullanan bir tüketicinin göreceği
  davranışı kanıtlar.

---

### MT-MM-094 — Hizalama istemek ayrı bir fatura birimi DEĞİLDİR (F-118)

Negatif/maliyet senaryosu. MT-MM-091 ve MT-MM-092'nin karşılaştırması.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 72 |
| **İlgili karar** | — |

**Ön koşul**
- MT-MM-091 ve MT-MM-092 aynı metinle (`"Merhaba"`) koşuldu. 🚨 Düzeltildi
  (2026-09-17): MT-MM-091'in kendi örnek komutu farklı bir metin
  ("Zaman damgasiz sentez.") taşıyor — bu case için MT-MM-091,
  MT-MM-092'nin metniyle (`"Merhaba"`) AYRICA koşulmalı.

**Beklenen sonuç**
- İki yanıttaki `characters` ve `cost` (yapılandırılmışsa) değerleri
  BİREBİR AYNIDIR. Hizalama listesinin uzunluğu maliyeti etkilemez —
  `VoicePricingTests` zaten karakter tabanlı hesaba dokunulmadığını
  birim seviyesinde kanıtlıyor; bu case aynı iddiayı gerçek sağlayıcıya
  karşı doğrular.

---

# 15 — Görsel Üretim Tool'u (Faz 88)

> **Ek ön koşul:** Bir görsel sağlayıcı anahtarı ve modeli tanımlıdır. OpenAI
> örneği için `Tracon:Images:Enabled=true`, `:Provider=openai`,
> `:Model=<etkin görsel model>` ayarlanır. Bu bölümdeki canlı çağrılar gerçek
> sağlayıcı kredisi harcar. Fiyat case'leri için `Pricing:Images` altında açık
> bir tüketici fiyatı girilir; Tracon yerleşik fiyat kullanmaz.

### MT-MM-095 — `generate_image` yalnız ek kimliği döndürür ve ek oturuma bağlıdır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 88 |
| **İlgili karar** | K-032 |

**Adımlar**
1. `generate_image` tool'unu taşıyan bir agent tanımı oluştur veya seç.
2. Agent'a kısa bir görsel istemi gönder: `Gün doğumunda turkuaz bir deniz feneri çiz.`
3. Koşu bitince tool sonucunu ve session eklerini oku:
```bash
curl -s -H "$APB" "$APU/api/runs/$RUN_ID/tools" \
  | jq '.[] | select(.toolName=="generate_image") | {result,usage}'
curl -s -H "$APB" "$APU/api/attachments?sessionId=$SESSION_ID" | jq .
```

**Beklenen sonuç**
- Tool sonucu `attachmentIds=` ile bir veya daha çok GUID taşır; `iVBOR`, uzun
  base64 veya ham görsel baytı taşımaz.
- Her ek `image/*` medya türündedir, aynı `SessionId` ve koşunun `RunId` değeriyle
  kayıtlıdır; indirilen bayt açılabilir bir görseldir.
- `usage.unit` `images` veya sağlayıcının bildirdiği `tokens` değeridir.

### MT-MM-096 — Kapalı ayar tool'u ve operator ucunu açmaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 88 |
| **İlgili karar** | — |

**Ön koşul**
- `Tracon:Images:Enabled=false` ile uygulama yeniden başlatılmış.

**Adımlar**
```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$APU/api/images/generate" \
  -H "$APB" -H 'content-type: application/json' -d '{"prompt":"bir kedi"}'
```

**Beklenen sonuç**
- `404` döner; uç bağlı değildir.
- `generate_image` isteyen yeni agent tanımı doğrulamada reddedilir; tool
  kaydı kapalıyken bir ücretli yetenek sessizce açılamaz.

### MT-MM-097 — URI görsel çıktısı giden ağ muhafızını atlayamaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 88 |
| **İlgili karar** | — |

**Ön koşul**
- `UriContent` dönen bir görsel sağlayıcı veya test adapter'ı yapılandırılmış;
  dönen adres loopback/private ağa çözülür.

**Adımlar**
1. Agent üzerinden `generate_image` çağrısını başlat.
2. Koşu ve ek kayıtlarını incele.

**Beklenen sonuç**
- İndirme giden ağ muhafızında reddedilir; private/loopback adrese bağlantı
  kurulmaz.
- Koşu tipli tool hatası taşır ve başarısız çağrı için indirilebilir/belirsiz bir
  ek oluşturulmaz.

### MT-MM-098 — Google adapter'ı boyut tahmin etmez

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 88 |
| **İlgili karar** | K-032 |

**Ön koşul**
- Google görsel adapter'ı `UseGoogleImages()` ile kayıtlı (MT-MM-121'in
  kurulumu) ve geçerli bir görsel modeli yapılandırılmış.

**Adımlar**
1. `generate_image` tool'una `size: "1024x1024"` ile istek gönder.
2. Aynı istemi `size` olmadan gönder.

**Beklenen sonuç**
- İlk çağrı açık bir hata döndürür; adapter `WIDTHxHEIGHT` değerini Google'ın
  aspect-ratio/size-tier sözleşmesine tahmin ederek dönüştürmez.
- İkinci çağrı sağlayıcının varsayılan boyutuyla çalışır; fiyat kaydı yoksa
  maliyet `null` kalır.

### MT-MM-099 — Tur üretmeyen bir `commit` paneli asmaz: `idle` çerçevesi dinlemeye döndürür

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | F-180 kapanışı |
| **İlgili karar** | K-660 |

**Ön koşul**
- `UseVoiceConversation()` etkin, transcriber ve synthesizer yapılandırılmış.
- Playground'da bir agent'ın konuşma paneli açık ve `ready` alınmış.

**Adımlar**
1. Ses modunu aç, mikrofonu başlat ve **hiç konuşmadan** "şimdi gönder"e
   (`voice-commit`) hemen bas — kaydedicinin ilk dilimi dolmadan.
2. Panelin durumunu ve mikrofonun yeniden açılıp açılmadığını gözle.
3. Gürültülü bir ortamda (ya da anlaşılmayan bir ses çıkararak) konuş ve gönder;
   çözümlemenin boş metin döndüğü turu üret.

**Beklenen sonuç**
- Her iki adımda da sunucu tek bir `idle` çerçevesi gönderir; `done` GÖNDERMEZ.
- Panel `thinking` durumunda ASILI KALMAZ: dinlemeye döner ve kaydedici yeniden
  başlar, yani kullanıcı yeniden konuşabilir.
- Transkript listesine boş bir tur eklenmez ve **önceki** turun kaydı
  (özellikle "kesildi" notu) değişmez.
- Tur sayacı artmaz: sonraki gerçek tur `done` çerçevesinde `turn: 1` taşır.

### MT-MM-100 — `list_voices` ve `GET /api/voice/voices` sağlayıcı attribute'larını taşır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 138 |
| **İlgili karar** | K-669 |

**Ön koşul**
- Gerçek bir ElevenLabs anahtarı yapılandırılmış (`UseVoice`).

**Adımlar**
1. `GET /api/voice/voices` çağır.
2. Yanıttaki her sesin `attributes` alanını incele.

**Beklenen sonuç**
- Her ses bir `attributes` nesnesi taşır (boş olabilir, `null` değil).
- En az bir seste `gender` anahtarı var.
- `labels` taşımayan bir ses varsa o sesin `attributes`'ı **boş nesnedir**,
  `null` değildir.

### MT-MM-101 — Sağlayıcı üstverisi `preview_url` veya API key sızdırmaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 138 |
| **İlgili karar** | K-669 |

**Ön koşul**
- MT-MM-100'ün yanıtı elde edilmiş.

**Adımlar**
1. Tam yanıt gövdesini `preview_url` ve yapılandırılan API key değeri için
   tara.

**Beklenen sonuç**
- Hiçbir `preview_url` alanı yanıtta yer almaz.
- API key değeri yanıtın hiçbir yerinde geçmez.

### MT-MM-102 — Model `list_voices` çıktısında gender bilgisini görür

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 138 |
| **İlgili karar** | K-669 |

**Ön koşul**
- Bir agent'a `list_voices` tool'u bağlı ve gerçek bir ElevenLabs anahtarı
  yapılandırılmış.

**Adımlar**
1. Agent'a "hangi kadın sesler var?" gibi bir soru sor.

**Beklenen sonuç**
- Model, `list_voices` çıktısındaki gender bilgisini kullanarak yanıt verir
  (çıktı satırlarında `— female` gibi bir ek görünür).

### MT-MM-103 — Ses sağlayıcısı yokken `list_voices` İngilizce mesaj döner

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 138 |
| **İlgili karar** | — |

**Ön koşul**
- Ses sağlayıcısı hiç yapılandırılmamış (`UseVoice` çağrılmamış) VEYA
  `ISpeechSynthesizer` sıfır ses döndüren bir sahte uygulama.

**Adımlar**
1. `list_voices` tool'unu çağır.

**Beklenen sonuç**
- Dönen metin **İngilizce**'dir ("No voices available."); Türkçe metin yoktur.

### MT-MM-104 — 50'den fazla ses varken kalan sayı satırı İngilizce'dir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 138 |
| **İlgili karar** | — |

**Ön koşul**
- Sağlayıcı 50'den fazla ses döndürüyor (gerçek ElevenLabs hesabı veya sahte
  `ISpeechSynthesizer`).

**Adımlar**
1. `list_voices` tool'unu çağır.

**Beklenen sonuç**
- Çıktının son satırı "… and N more voices." biçimindedir; Türkçe metin
  yoktur.

### MT-MM-105 — Kaynak dili kapısı yeşildir ve taban çizgisi büyümedi

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 138 |
| **İlgili karar** | — |

**Ön koşul**
- Depo kök dizini.

**Adımlar**
1. `dotnet test tests/Tracon.Core.UnitTests -c Release --filter-method
   "*Source_tree_carries_no_Turkish*"` koştur.

**Beklenen sonuç**
- Test yeşildir.
- `tests/Tracon.Core.UnitTests/Architecture/source-language-baseline.txt`
  boş kalır (hiçbir dosya için izin verilen satır sayısı büyümedi).

### MT-MM-106 — Ses yolu Native AOT'ta çalışır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 138 |
| **İlgili karar** | K-006 |

**Ön koşul**
- Depo kök dizini.

**Adımlar**
1. `python3 scripts/kapi.py yayin --kuru` koştur.

**Beklenen sonuç**
- Native AOT smoke koşumu ses yolunu (yeni `Dictionary<string,string>`
  serileştirmesi dahil) hatasız geçer.

### MT-MM-107 — Var olan özel `ISpeechSynthesizer` uygulaması değişmeden derlenir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 138 |
| **İlgili karar** | K-669 |

**Ön koşul**
- `ISpeechSynthesizer`'ı uygulayan, `VoiceDescriptor` döndüren özel bir tüketici
  projesi (`samples/` veya harici).

**Adımlar**
1. Projeyi yeni `Tracon.Abstractions` sürümüne karşı derle.

**Beklenen sonuç**
- Proje **hiçbir kod değişikliği olmadan** derlenir: `Attributes` varsayılanlı
  bir alan olduğu için mevcut nesne başlatıcılar etkilenmez.

### MT-MM-108 — `UseOpenAILive()` çağrılmadan canlı oturum ucu 501 döner

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 161 |
| **İlgili karar** | K-705 |

**Ön koşul**
- `UseLiveVoice()` çağrılmış, hiçbir `ILiveVoiceProvider` kayıtlı değil.

**Adımlar**
1. `POST /tracon/api/voice/live/sessions` gövde `{"sessionId":"s1","agent":"support","sdp":"v=0\r\n"}`.

**Beklenen sonuç**
- `501`; `detail` metni eksik olan çağrının adını (`UseOpenAILive`) söyler.

### MT-MM-109 — `UseLiveVoice()` da çağrılmadan adres hiç yoktur

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 161 |

**Ön koşul**
- Ne `UseLiveVoice()` ne de bir canlı sağlayıcı kayıtlı.

**Adımlar**
1. Aynı adrese `POST` at.

**Beklenen sonuç**
- `404` — rota hiç açılmamıştır. Barındırma modelini değiştiren bir yetenek
  "var ama kapalı" görünmez.

### MT-MM-110 — Gerçek GPT-Live oturumu açılır ve sesli yanıt duyulur

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 161 |

**Ön koşul**
- Gerçek `Tracon:Providers:OpenAI:ApiKey`, `support` agent'ı tanımlı,
  `samples/Tracon.Api` ayakta.

**Adımlar**
1. `http://localhost:5080/live-test.html` aç.
2. Bearer token'ı gir, **Start**'a bas, mikrofona izin ver.
3. Bir sipariş durumu sor.

**Beklenen sonuç**
- Sayfa `ice=connected` ve `remote track` yazar; sesli yanıt duyulur.
- 👤 İnsan gerekir — ya da sentetik mikrofon: `say` ile üretilen WAV'ın
  **sonuna sessizlik eklenmeli**, yoksa sağlayıcının VAD'i tur sonunu hiç
  görmez ve model sıra alamaz.

### MT-MM-111 — Devredilen iş gerçek bir `runs` satırı üretir

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 161 |
| **İlgili karar** | K-704 |

**Ön koşul**
- MT-MM-110 sürüyor.

**Adımlar**
1. Agent'ın bir tool'unu gerektiren bir soru sor ("442 numaralı siparişin durumu ne?").
2. `GET /tracon/api/runs?take=5`.

**Beklenen sonuç**
- Gerçek bir satır: `agentName` oturumu açan agent, `sessionId` istekteki değer,
  `usage` token sayıları dolu, tool çağrısı kayıtta.
- Yanıt sesli döner.

### MT-MM-112 — Konuşma dökümü oturum geçmişinde görünür

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 161 |
| **İlgili karar** | K-706 |

**Ön koşul**
- MT-MM-111 tamamlandı, `PersistTranscript` açık (varsayılan).

**Adımlar**
1. Oturumu kapat (`DELETE .../voice/live/sessions/{id}`).
2. `GET /tracon/api/sessions/{sessionId}`.

**Beklenen sonuç**
- Konuşmanın dökümü geçmişte durur; devredilen run'ın girdisi etiketli
  transcript kesimidir.

### MT-MM-113 — `PersistTranscript=false` iken geçmişe yazılmaz ama delegation çalışır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 161 |
| **İlgili karar** | K-706 |

**Ön koşul**
- `Tracon:Voice:Live:PersistTranscript=false`.

**Adımlar**
1. MT-MM-111'i tekrarla.

**Beklenen sonuç**
- Oturum yaratma yanıtı `"persistTranscript": false` bildirir.
- Delegation yine çalışır ve sesli yanıt gelir.
- Geçmişte transcript **yoktur**.

### MT-MM-114 — Kapanan oturumun kaydı süreyi ve maliyeti taşır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 161 |
| **İlgili karar** | K-701 · K-702 |

**Ön koşul**
- `Tracon:Pricing:Voice:openai:gpt-live-1:PerMinute` tanımlı,
  `Tracon:Pricing:Currency` tanımlı.

**Adımlar**
1. Bir canlı oturum aç, kapat.
2. `GET /tracon/api/voice/sessions`.

**Beklenen sonuç**
- `provider`, `model`, `liveSeconds` ve `cost.durationCost` dolu.
- `liveSeconds` sağlayıcının bildirdiği sayıdır, duvar saati değil.
- `cost.durationCost` = `liveSeconds / 60 × PerMinute`.

### MT-MM-115 — Fiyat yapılandırması yokken `cost` `null` döner

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 161 |
| **İlgili karar** | K-702 |

**Ön koşul**
- `Tracon:Pricing:Voice` altında `gpt-live-1` **yok**.

**Adımlar**
1. MT-MM-114'ü tekrarla.

**Beklenen sonuç**
- `cost` **`null`** — `0` değil. Sıfır, konuşmanın ücretsiz olduğunu iddia ederdi.
- `liveSeconds` yine doludur.

### MT-MM-116 — Hiç bağlanılmayan oturum TTL'de `Abandoned` ile kapanır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 161 |

**Ön koşul**
- `Tracon:Voice:Live:PendingSessionTimeout` kısa bir değere çekilmiş
  (ör. `00:00:30`).

**Adımlar**
1. Geçerli bir SDP ile oturum yarat, tarayıcıyı **hiç bağlama**.
2. TTL kadar bekle, `GET /tracon/api/voice/sessions`.

**Beklenen sonuç**
- Kayıt `endReason: "Abandoned"` ile kapanmıştır — `Error` değil. Terk edilmiş
  bir oturum hata değildir.

### MT-MM-117 — Başka kiracının canlı oturumu erişilemez oturumla aynı 404'ü alır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 161 |
| **İlgili karar** | K-708 · K-687 |

**Ön koşul**
- Çok kiracılı kurulum (`UseTenancy`, `AllowHeaderResolution`).

**Adımlar**
1. `tenant-a` ile bir canlı oturum aç, `voiceSessionId`'yi al.
2. `X-Tracon-Tenant: tenant-b` ile aynı kimliğe `DELETE` ve `GET` at.
3. `tenant-b` ile **rastgele** bir kimliğe `GET` at.

**Beklenen sonuç**
- Üçü de `404`.
- 2. ve 3. adımın gövdeleri kimlik dışında **bayt bayt aynıdır**.

### MT-MM-118 — Eşzamanlılık limiti sağlayıcıya gitmeden reddeder

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 161 |
| **İlgili karar** | K-705 |

**Ön koşul**
- `Tracon:Voice:Live:MaxConcurrentSessionsPerTenant=1`.

**Adımlar**
1. Bir oturum aç (açık kalsın).
2. İkinci bir oturum açmayı dene.
3. Sağlayıcının kullanım panelinden oturum sayısına bak.

**Beklenen sonuç**
- İkinci istek `429`.
- Sağlayıcıda **tek** oturum yaratılmıştır: reddedilen istek faturalanan bir
  oturum bırakmaz.

### MT-MM-119 — Seçenek A regresyon çiti

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 161 |
| **İlgili karar** | K-222 |

**Ön koşul**
- `UseVoiceConversation()` açık (canlı katmandan bağımsız).

**Adımlar**
1. `/tracon/api/voice/sessions/{id}/stream` üzerinde tam bir tur yap.

**Beklenen sonuç**
- Faz 29 davranışı **değişmemiştir**: `ready` → ses → `transcript` → `done`.
- İki katman birbirini gerektirmez; ayrı ayrı da açılabilirler.

### MT-MM-120 — Görsel üretimi açıkken sağlayıcı kayıtlı değilse uygulama BAŞLAMAZ

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Kritik |
| **İlgili faz** | Faz 88 |
| **İlgili karar** | — |

**Ön koşul**
- `Tracon:Images:Enabled=true`.
- `samples/Tracon.Api/Program.cs`'teki `tracon.UseOpenAIImages();` satırı
  GEÇİCİ olarak yorum satırı yapılır; başka hiçbir görsel sağlayıcısı
  (`UseAzureOpenAIImages()` · `UseGoogleImages()`) kayıtlı değildir. Değişiklik
  case sonunda GERİ ALINIR.

**Adımlar**
1. `cd samples/Tracon.Api && dotnet run`

**Beklenen sonuç**
- Uygulama **başlamaz**. `app.MapTracon(...)` çağrısı bir `TraconException` ile
  düşer; hata metni eksik sağlayıcı adını ve üç kayıt çağrısını birden yazar:
  `UseOpenAIImages()`, `UseAzureOpenAIImages()` veya `UseGoogleImages()`.
- Arıza **HTTP yüzeyi kurulurken** çıkar, ilk ücretli çağrıda değil. Açık ama
  sağlayıcısız bir görsel yapılandırması çalışan bir uca dönüşemez.

> **Otomatik karşılığı yoktur** — bu bir kompozisyon anı kapısıdır ve yalnız
> gerçek bir host başlatmasıyla ölçülür.

### MT-MM-121 — `UseGoogleImages()` Google üreticisini kaydeder ve `Provider` onu seçer

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 88 |
| **İlgili karar** | — |

**Ön koşul**
- `Tracon:Providers:Google:ApiKey` tanımlı.
- `Program.cs`'te `tracon.UseOpenAIImages();` yerine GEÇİCİ olarak
  `tracon.UseGoogleImages();` yazılır ve `Tracon:Images:Provider` `google`
  yapılır. Değişiklik case sonunda GERİ ALINIR.

**Adımlar**
1. Uygulamayı başlat; `MapTracon` hatasız geçmelidir.
2. ```bash
   curl -s -X POST "$APU/api/images/generate" \
     -H "$APB" -H 'content-type: application/json' \
     -d '{"prompt":"a red bicycle"}' | jq
   ```

**Beklenen sonuç**
- Uygulama başlar — MT-MM-120'nin kapısı bu sağlayıcıyla de tatmin olur.
- Çağrı bir ek kimliği döndürür ve ek oturuma bağlanır (MT-MM-095 ile aynı
  sözleşme); üretici Google'dır.
- Sağlayıcı seçimi `Tracon:Images:Provider` anahtarıyla yapılır; kayıt çağrısı
  ile seçim anahtarı **ayrı** iki karardır.

### MT-MM-122 — `UseAzureOpenAIImages()` Azure üreticisini kaydeder

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 88 |
| **İlgili karar** | — |

**Ön koşul**
- ⏭ **Azure kimliği bu ortamda yoktur** (`00-INDEKS.md` §3.1). Kimlik
  sağlanana kadar bu case atlanır.
- Kimlik varsa: `Program.cs`'te `tracon.UseAzureOpenAIImages();` ve
  `Tracon:Images:Provider` `azure` yapılır.

**Adımlar**
1. MT-MM-121'in iki adımını Azure sağlayıcısıyla tekrarla.

**Beklenen sonuç**
- MT-MM-121 ile **aynı** sözleşme: uygulama başlar, çağrı ek kimliği döndürür.
- Üç görsel sağlayıcısının kayıt yüzeyi tek biçimlidir; yalnız kayıt çağrısının
  adı ve `Provider` değeri değişir.

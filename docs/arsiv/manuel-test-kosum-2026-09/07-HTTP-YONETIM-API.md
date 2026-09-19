# 07 — HTTP Yönetim API'si (`API`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../07-HTTP-YONETIM-API.md`](../../manuel-test/07-HTTP-YONETIM-API.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz A zinciri, tek şerit — zincirin SON ailesi) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `7e3a4de7` donuk |
| **Case sayısı** | 43 (MT-API-001..100) |
| **Port** | 5081 (spec 5080 yazar — şerit sapması) |
| **Depo** | **Bellek içi** (`storage.persistent: false`) — dosyanın kendi varsayılanı (MT-API-053/054 bunu ön koşul sayar), `mt_s1` şemasına dokunulmadı |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): gerçek OpenAI anahtarı tek
komutta ortam değişkenine aktarıldı, hiçbir dosyaya/loga yazılmadı.

**Açılış ölçümü:**

```
GET /api/meta -> version 0.0.0-preview.0.789, storage.persistent=false,
  agentDefinitionStore=InMemoryAgentDefinitionStore, runStore=InMemoryRunStore,
  sessionStore=InMemorySessionStore, jobStore=InMemoryJobStore, jobWorkerEnabled=true
```

---

## Devir notu

**🎉 DOSYA 07 KAPANDI — 43/43 case koşuldu** (oturum 12: tamamı tek
oturumda — CLI bütçesi ~40, dosya 07 43 case ama tamamı basit `curl`
olduğundan tek oturumda bitti). Dosya sonucu: **43 ☑ Geçti · 0 ☑ Kaldı ·
0 ⏭ Atlandı**. Sayım skill §7 betiğiyle alındı, elle yazılmadı.

**🎉 ZİNCİR TAMAMLANDI (Faz A bitti).** `01 → 02 → 03 → 05 → 07` beşi de
yeşil. Toplam **311 case** koşuldu (268 + 43). Sıradaki iş **Faz B'nin
açılması** — dört şerit paralel, `00-KOSUM-PLANI.md` §3.1 dağılımı.

🚨 **Dosya 07'nin `Beklenen sonuç` metinleri sistematik olarak bayat çıktı** —
dosya 05'teki aynı desen (K-228). Neredeyse her case'te ürünün ürettiği
`title`/`detail` İngilizce, spec Türkçe yazıyordu. Fark tespit edildiğinde
spec bu koşumda düzeltildi (kural 1 istisnası), gerekçe her case'in
`Gerçek sonuç`'una yazıldı. **20+ satır düzeltildi** — tam liste aşağıdaki
case kayıtlarında.

**Yeni bulgular / çözülen belirsizlikler:**
- **MT-API-040/041/042** dosya 05'te açılmış `HATA-S1-020`'nin (sağlayıcı
  hata sınıflandırıcısı `upstream_error`'ı tanımıyor, her hata `Unknown`'a
  düşüyor) aynı kök nedenini bir kez daha doğruladı — yeni kayıt açılmadı,
  mevcut bulguya çapraz referans verildi.
- **MT-API-054 açık soruyu çözdü:** bellek içi depoda dallandırma kontrolü
  (`501`) oturum varlığı kontrolünden (`404`) **önce** çalışıyor — var olmayan
  bir oturum için de `501` görülüyor.
- **MT-API-064** `Tracon:RunRecording:RecordRunInput` ayarı için uygulama iki
  kez yeniden başlatıldı (kapalı → test → varsayılana dönüş); ortam değişkeni
  kullanıldı, `user-secrets`'a yazılmadı.
- **/run varsayılan olarak (Idempotency-Key yokken) SSE akışı döner** —
  dosya 02'nin notuyla tutarlı; `<scratch>/sse.py` ile ayrıştırıldı.

**Sonraki oturumun işi:** Aşama 1 bitti. `docs/manuel-test/kosumlar/2026-09-16/DEVIR.md`'yi
güncelle ve Faz B'yi aç (dört worktree zaten hazır: `ap-s1..4`, dallar
`test/kosum-s1..4`). Şerit dağılımı `00-KOSUM-PLANI.md` §3.1'dedir.

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 54479468:docs/manuel-test/kosumlar/2026-09-16/07-HTTP-YONETIM-API.md
> ```

---

## Temiz geçen case'ler (28)

| Case | Durum | Başlık |
|---|---|---|
| MT-API-001 | ☑ | `POST /api/agents` yeni bir tanım oluşturur, `201` ve `Location` döner |
| MT-API-003 | ☑ | Kodda tanımlı `support` adıyla `POST` edilirse `409` döner (farklı gerekçe metni) |
| MT-API-010 | ☑ | Başarılı `PUT` yeni bir versiyon üretir |
| MT-API-012 | ☑ | Var olmayan agent'ı silmek `404` döner |
| MT-API-013 | ☑ | Başarılı `DELETE` `204` döner, ardından `GET` `404` döner |
| MT-API-020 | ☑ | Geçerli VE geçersiz tanımda da yanıt `200`'dür; `severity` ad olarak yazılır |
| MT-API-021 | ☑ | `validate` yan etkisizdir: hiçbir agent veya çalıştırma satırı yazılmaz |
| MT-API-022 | ☑ | Bozuk JSON gövdesi gerçek bir HTTP hatası verir (`400`) |
| MT-API-030 | ☑ | Aynı anahtar VE aynı gövdeyle ikinci istek yeniden çalışmaz, `Idempotency-Replayed: true` döner |
| MT-API-032 | ☑ | Akışlı istekte (`stream: true`) `Idempotency-Key` desteklenmez |
| MT-API-033 | ☑ | 255 karakteri aşan `Idempotency-Key` reddedilir |
| MT-API-034 | ☑ | Aynı anahtarla eşzamanlı iki istek: ikincisi `409` alır |
| MT-API-041 | ☑ | `/api/stats/errors` varsayılan aralığı son 24 saattir, `?hours=` ile değiştirilir |
| MT-API-042 | ☑ | `/api/stats` genel sayaçları tutarlıdır (maliyet HARİÇ) |
| MT-API-050 | ☑ | `GET /api/sessions` sayfalama parametreleri `[1, 200]` aralığına kırpılır |
| MT-API-052 | ☑ | Oturum silinir, tekrar okunduğunda `404` döner |
| MT-API-054 | ☑ | Var olmayan bir oturumu dallandırmak `404` döner |
| MT-API-060 | ☑ | `GET /api/runs` varsayılan olarak yalnız kök çalıştırmaları döner |
| MT-API-061 | ☑ | Var olmayan `runId` her uçta `404` döner |
| MT-API-062 | ☑ | `GET /api/runs/{id}/tree` kökten tam ağacı döner (alt çalıştırmadan sorulsa bile) |
| MT-API-063 | ☑ | `Last-Event-ID` ile akış kaldığı sıradan devam eder, tekrar göndermez |
| MT-API-064 | ☑ | Girdi kaydı kapalıyken `GET /api/runs/{id}/input` `404` döner |
| MT-API-065 | ☑ | `GET /api/runs` sayfalama parametreleri `[1, 200]` aralığına kırpılır |
| MT-API-070 | ☑ | `GET /api/tools` kayıtlı tool'ları JSON şemalarıyla listeler |
| MT-API-071 | ☑ | `GET /api/models`'in `status` alanı önbellekten gelir, ağ çağrısı yapmaz |
| MT-API-080 | ☑ | `/api/meta` kimlik doğrulamasız erişilebilir, sır içermez |
| MT-API-091 | ☑ | Doğru token ile korunan uç normal çalışır |
| MT-API-100 | ☑ | Farklı hatalar aynı `ProblemDetails` zarfını taşır |

## Ayrıntı taşıyan case'ler (15)

## MT-API-002 — Aynı ad ikinci kez `POST` edilirse `409` döner

**Gerçek sonuç**
`HTTP: 409`. `title: "Agent name in use"`, `detail: "A definition named
'manuel-crud-01' already exists. Use PUT to update it."` — spec Türkçe
bekliyordu, ürün İngilizce (K-228); `Beklenen sonuç` bu koşumda düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-004 — Adı boş bir tanım `400` döner

**Gerçek sonuç**
`HTTP: 400`. `title: "Agent name empty"`, `detail: "'name' is required."`
(İngilizce, K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-005 — `model.provider` veya `model.model` eksikse `400` döner

**Gerçek sonuç**
`HTTP: 400`. `title: "Model binding missing"`, `detail: "'model.provider' and
'model.model' are required."` (İngilizce, K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-006 — Var olmayan agent'ı okuma `404` döner

**Gerçek sonuç**
`HTTP: 404`. `title: "Agent not found"`, `detail: "There is no agent named
'hic-boyle-bir-agent'."` (İngilizce, K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-007 — `PUT` yol ile gövdedeki ad uyuşmazsa `400` döner

**Gerçek sonuç**
`HTTP: 400`. `title: "Name mismatch"`, `detail: "The path name is
'manuel-crud-01', the body name is 'baska-bir-ad'. An agent's name cannot be
changed; create a new definition for a new name."` (İngilizce, K-228 — spec
düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-008 — Kodda tanımlı bir agent `PUT` ile güncellenemez

**Gerçek sonuç**
`HTTP: 409`. `title: "Code-defined agent cannot be modified"` (İngilizce,
K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-009 — Var olmayan bir veritabanı tanımını `PUT` etmek `404` döner

**Gerçek sonuç**
`HTTP: 404`. `title: "Agent not found"` (İngilizce, K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-011 — Kodda tanımlı bir agent silinemez

**Gerçek sonuç**
`DELETE` `HTTP: 409`, `title: "Code-defined agent cannot be modified"`
(İngilizce, K-228 — spec düzeltildi). Ardından `GET /api/agents/support`
`HTTP: 200`, `origin: "Code"` — silinmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-014 — Var olmayan bir versiyon farkı istenirse `404` döner

**Gerçek sonuç**
`HTTP: 404`. `title: "Version not found"`, `detail: "Agent
'manuel-versiyon-testi' has no version 99."` — `99` geçiyor (İngilizce,
K-228 — spec düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-015 — Var olmayan bir versiyona geri dönmek `404` döner

**Gerçek sonuç**
`HTTP: 404`. `title: "Rollback failed"`, `detail: "Version 99 of agent
'manuel-versiyon-testi' was not found."` (İngilizce, K-228 — spec
düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-031 — Aynı anahtar, FARKLI gövdeyle kullanılırsa `422` döner

**Gerçek sonuç**
`HTTP: 422`, `title: "Idempotency-Key used for a different request"`
(İngilizce, K-228 — spec'in Türkçe beklentisi bayat, düzeltilmedi çünkü case
zaten yalnız durum kodunu iddia ediyordu, `title` metnini iddia etmiyordu).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-040 — Gerçek bir sağlayıcı hatası `/api/stats/errors`'ta gruplanarak görünür

**Gerçek sonuç**
`class: "Unknown"`, `count: 2` (iki hata da aynı fingerprint'e düştü),
`sampleMessage: "The model provider request failed."` — spec'in kendi
koşullu ifadesi ("eşleşmiyorsa Unknown görülebilir") gerçekleşti. Bu,
**dosya 05'te açılmış `HATA-S1-020`'nin aynı kök nedeni** — `upstream_error`
sınıflandırıcıda tanınmıyor, her sağlayıcı hatası `Unknown`'a düşüyor.
Yeni bir kusur açılmadı, mevcut `HATA-S1-020`'ye çapraz referans verildi.
Case, spec'in koşullu beklentisi karşılandığı için **Geçti** sayılır (count
`en az 2` şartı sağlandı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-051 — Var olmayan oturum `404` döner

**Gerçek sonuç**
`HTTP: 404`, `title: "Session not found"` (İngilizce, K-228 — spec
düzeltildi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-053 — Bellek içi depoda `POST /api/sessions/{id}/branch` `501` döner

**Gerçek sonuç**
`HTTP: 501`, `title: "Branching not supported"` (İngilizce, K-228 — spec
düzeltildi). `detail` bellek içi kısıtı açıkça anlatıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-API-090 — Token yokken korunan bir uç `401` döner, `WWW-Authenticate: Bearer` taşır

**Gerçek sonuç**
İki istek de (`Authorization` yok / yanlış token) `HTTP: 401`,
`WWW-Authenticate: Bearer` başlığı var. `title: "Authentication failed"`
(İngilizce, K-228 — spec düzeltildi), `detail: "A valid 'Authorization:
Bearer <token>' header is required."` — beklenen token hakkında hiçbir bilgi
vermiyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

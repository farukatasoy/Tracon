# AgentPrism — Manuel Kabul Testi Koşum Planı

> **Bu dosya bir ajan talimatıdır.** Koşum oturumunu açan ajan önce bu dosyayı
> baştan sona okur, sonra kendi oturum satırındaki dosyayı açar. Senaryo
> **üretimi** bitti; bu dosya **koşumu** yönetir. Üretim protokolü
> [`PROMPT.md`](../arsiv/manuel-test-kosum-2026-08/PROMPT.md)'dedir ve artık kullanılmaz.
>
> Ortam kurulumu, fixture verisi, önem dereceleri ve hata şablonu
> [`00-INDEKS.md`](00-INDEKS.md)'dedir. Bu dosya onu tekrarlamaz; **sapmaları**
> yazar.

---

## 1. Durum

**Koşum bitti (2026-08-14): 1094 case koşuldu, 2 açık kalem var.** Bulunan
kusurların tamamı kodlandı, doğrulandı ve ayrı commit'lerle `main`'e işlendi.
Açık kalanlar (Faz 58.3'te ölçüldü, bkz. `KAPANIS-PLANI.md` §12):

| Case | Durum | Neden |
|---|---|---|
| `MT-UIRUN-019` | ☐ Beklemede | Playwright/CDP çevrimdışı emülasyonu açık SSE akışını kesmiyor; fiziksel ağ kesintisi ister |
| `MT-SKILL-057` | ⬜ Hiç koşulmadı | Koşum kaydı boş bırakılmış |

| Nereye bakmalı | Dosya |
|---|---|
| Kapanış protokolü ve kusur ailelerinin kapanış kaydı | [`KAPANIS-PLANI.md`](KAPANIS-PLANI.md) |
| Şerit sonuç kayıtları (arşiv, yalnız grep'lenir) | [`../arsiv/manuel-test-kosum-2026-08/`](../arsiv/manuel-test-kosum-2026-08/) |
| Kusurların karar gerekçeleri | `docs/KARARLAR.md` → K-392..K-407 |

İkinci bir koşum bu dosyanın §2–§9 bölümlerini olduğu gibi kullanır; §1 yalnız
son koşumun kapandığını söyler. Koşum kayıtları case dosyalarında **değil**,
[`kosumlar/`](kosumlar/) altındadır.

---

## 2. Ajanın uyacağı kurallar

Bu bölüm pazarlığa açık değildir. Her oturum bu kurallarla açılır.

### 2.1 Kod değiştirilmez

Koşum ajanı `src/`, `samples/`, `tests/` altında **hiçbir dosyayı
değiştirmez**. Bir kusur bulduğunda:

1. Kaynağı **okur** ve kök nedeni bulur (`grep`, `Read` serbest).
2. Bulguyu case'in `Gerçek sonuç` alanına yazar — log alıntısı, dosya:satır.
3. `Durum` satırını `☑ Kaldı` işaretler.
4. Şeridin sonuç dosyasına `HATA-NNN` kaydı ekler (§6).
5. **Koşmaya devam eder.**

Gerekçe: dört şerit paralel çalışır. Bir şeridin kod düzeltmesi diğerinin
koştuğu ikiliyi değiştirir ve sonuçlar karşılaştırılamaz hâle gelir. Düzeltmeler
tüm koşum bittikten sonra tek bir toplama oturumunda yapılır (§8).

**Tek istisna:** Bir case'in **beklenen sonucu** koda göre yanlışsa (doküman
kusuru, ürün kusuru değil), senaryo dosyasındaki `Beklenen sonuç` düzeltilir ve
düzeltmenin gerekçesi `Gerçek sonuç` alanına yazılır. AGENTS.md kuralı: doküman
ile kod çelişirse doküman yanlıştır.

### 2.2 `user-secrets` kullanılmaz — ortam değişkeni kullanılır

`dotnet user-secrets` deposu `UserSecretsId=agentprism-sample-api` ile
**makine genelinde tektir**. Dört şerit onu paylaşır; biri yazarken diğeri okur.

Senaryolarda geçen her

```bash
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "..."
dotnet user-secrets remove "AgentPrism:Sqlite:ConnectionString"
```

adımı, ajan tarafından şeridin kendi ortam değişkenine çevrilir:

```bash
export AgentPrism__PostgreSql__ConnectionString="..."
export AgentPrism__Sqlite__ConnectionString=""      # remove = bos deger
```

Ortam değişkeni `user-secrets`'ı **ezer** (ASP.NET Core yapılandırma sırası).
Boş değer "kayıtlı değil" demektir: `samples/AgentPrism.Api/Program.cs:635-646`
üç sağlayıcıyı `string.IsNullOrWhiteSpace` ile ayırır.

> Bu bir **sapmadır** ve her oturumun sonuç dosyasına bir kez yazılır:
> "`user-secrets` yerine ortam değişkeni kullanıldı — şerit izolasyonu (KOSUM-PLANI §2.2)."

### 2.3 Yalnız kendi şeridinin kaynağına dokunulur

| Kaynak | Kural |
|---|---|
| Senaryo dosyaları | Bir dosya **tek** şeride aittir. Başka şeridin dosyasına yazma. |
| PostgreSQL | Yalnız kendi şemanı düşür: `DROP SCHEMA IF EXISTS mt_s<N> CASCADE;` — **asla** `agentprism` şemasını değil. |
| SQLite | Yalnız kendi worktree'ndeki `.db` dosyası. |
| SQL Server | Yalnız kendi veritabanın (`AgentPrism_S<N>`). |
| Docker container | **Durdurma, silme, yeniden başlatma yok.** Container'lar paylaşılır. Bir case container'ı durdurmayı istiyorsa §5'e bak. |
| `~/agentprism-local-feed`, `dotnet new install` | Küresel. Yalnız `24` dosyasını koşan ajan dokunur. |
| Port | Yalnız kendi portun. |

### 2.4 Ne zaman kullanıcıya sorulur

Ajan şu durumlarda **durur ve `AskUserQuestion` ile sorar** — varsayım yapmaz:

1. Bir kimlik bilgisi yok ya da çalışmıyor (401/403 sağlayıcıdan).
2. Bir case fiziksel eylem ister: mikrofon, hoparlör, Docker Desktop ayarı,
   göz denetimi. → Önce §5'teki listeye ekle; oturum sonunda topluca sor.
3. Bir case paylaşılan bir kaynağı bozacak: container durdurma, küresel şablon
   kaydı, `agentprism` şeması, repo'nun `NuGet.config` dosyası.
4. Beklenen sonuç iki farklı biçimde okunabiliyor ve hangisinin doğru olduğu
   koddan çıkmıyor.
5. **Kritik** önemde bir kusur bulundu ve aynı kök neden sonraki 5+ case'i
   bloklayacak. (Kusuru kaydet, sonra sor: "bu şeridin kalanını atlayayım mı?")
6. Gerçek sağlayıcı çağrısı beklenenden çok tüketiyor (bir case 20+ çağrı).

Sormak ucuzdur. Yanlış varsayımla 40 case koşmak pahalıdır.

### 2.5 Maliyet kuralı

Gerçek sağlayıcı çağrısı yapan case'ler **en ucuz modelle** koşulur. Örnek
uygulamanın varsayılanları zaten budur:

| Sağlayıcı | Kullanılacak model |
|---|---|
| OpenAI | `gpt-5.4-mini` |
| Anthropic | `claude-haiku-4-5-20251001` |
| Google | `gemini-3.6-flash` (gerekirse `gemini-3.1-flash-lite`) |
| OpenRouter | `openai/gpt-5.4-mini` |

Bir case açıkça büyük model ister (yapılandırılmış çıktı yeteneği, uzun bağlam,
akıl yürütme) ancak o zaman büyük model kullanılır ve gerekçesi `Gerçek sonuç`
alanına yazılır. Model çağrısı gerekmeyen her yerde `echo` sağlayıcısı
kullanılır.

---

## 3. Şerit izolasyonu — bir kerelik kurulum

Dört şerit dört ayrı `git worktree` içinde çalışır. Aynı repo, ayrı çalışma
kopyası, ayrı dal. Böylece dosya yazımı çakışmaz ve birleştirme önemsizdir.

```mermaid
flowchart TD
    R["AgentPrism (ana kopya)<br/>dal: docs/manuel-test"] --> W1["ap-s1<br/>dal: test/kosum-s1<br/>port 5081 · sema mt_s1"]
    R --> W2["ap-s2<br/>dal: test/kosum-s2<br/>port 5082 · sema mt_s2"]
    R --> W3["ap-s3<br/>dal: test/kosum-s3<br/>port 5083 · sema mt_s3"]
    R --> W4["ap-s4<br/>dal: test/kosum-s4<br/>port 5084 · sema mt_s4"]
    W1 --> D["ap-pg · ap-mssql<br/>paylasilan container"]
    W2 --> D
    W3 --> D
    W4 --> D
```

### 3.1 Kurulum (bir kez, tek kişi/ajan yapar)

```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism

# 1. Aktif oturumun isi bitti mi? Bitmediyse BEKLE.
git status --short

# 2. Yarim kalan koşum sonuclarini kaydet.
git add docs/manuel-test && git commit -m "manuel test: 03 kosumu ve ara sonuclar"

# 3. Dort worktree.
for n in 1 2 3 4; do
  git worktree add ../ap-s$n -b test/kosum-s$n docs/manuel-test
done

# 4. Her worktree'yi bir kez tam derle (sonraki kosumlar --no-build ile hizli olur).
for n in 1 2 3 4; do
  (cd ../ap-s$n && dotnet build AgentPrism.slnx -c Release)
done
```

`ap-pg` (55432), `ap-mssql` (51433) ayakta olmalıdır. `ap-pg-mt-core` (55434)
önceki oturumdan kalmıştır; **dokunma**, kullanma.

### 3.2 Şeridin ortam dosyası

Her oturum bu bloğu çalıştırarak açılır. `<N>` şerit numarasıdır.

```bash
export SERIT=1                      # kendi serit numaran
export APORT=508$SERIT
export APU="http://localhost:$APORT/agentprism"
export APB="Authorization: Bearer manuel-test-token-2026"
export PG="docker exec -i ap-pg psql -U postgres -d agentprism"

cd /Users/farukatasoy/Desktop/projects/AgentPrism/../ap-s$SERIT

# Kimlik — hepsi ortam degiskeni, user-secrets DEGIL.
export AgentPrism__Ui__AuthToken="manuel-test-token-2026"
export AgentPrism__Providers__OpenAI__ApiKey="$(cd /Users/farukatasoy/Desktop/projects/AgentPrism/samples/AgentPrism.Api && dotnet user-secrets list --json 2>/dev/null | python3 -c 'import sys,json;d=sys.stdin.read();d=d[d.index("{"):d.rindex("}")+1];print(json.loads(d).get("AgentPrism:Providers:OpenAI:ApiKey",""))')"
# Ayni deseni Anthropic, Google, OpenAICompatible:openrouter, Voice icin tekrarla.

# Kalicilik — HER SERIT KENDI IZOLASYONU. Ucunden yalniz biri dolu olur.
export AgentPrism__PostgreSql__ConnectionString="Host=localhost;Port=55432;Database=agentprism;Username=postgres;Password=agentprism"
export AgentPrism__PostgreSql__SchemaName="mt_s$SERIT"
export AgentPrism__Sqlite__ConnectionString=""
export AgentPrism__SqlServer__ConnectionString=""

dotnet run --project samples/AgentPrism.Api -c Release --no-build --urls "http://localhost:$APORT"
```

> Anahtarları okumak için `dotnet user-secrets list` **yalnız okuma** yapar;
> yazma yasağı (§2.2) sürer. Anahtar hiçbir dosyaya, hiçbir loga yazılmaz.

### 3.3 Şeridin reset yordamı

[`00-INDEKS.md`](00-INDEKS.md) §4 yerine bu kullanılır — şerit kapsamlıdır:

```bash
# 1. Uygulamayi durdur.
# 2. Yalniz KENDI semani dusur.
docker exec -i ap-pg psql -U postgres -d agentprism -c "DROP SCHEMA IF EXISTS mt_s$SERIT CASCADE;"
# 3. Yalniz KENDI sqlite dosyani sil.
rm -f samples/AgentPrism.Api/agentprism-manuel.db*
# 4. Yalniz KENDI SQL Server veritabanini dusur (varsa).
docker exec -i ap-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa \
  -P 'AgentPrism!2026' -Q "DROP DATABASE IF EXISTS AgentPrism_S$SERIT;"
# 5. Uygulamayi yeniden baslat.
```

Arayüz şeridi ek olarak Playwright'ta site verisini temizler (§7).

---

## 4. Oturum protokolü

Her oturum bu yedi adımı uygular. Adım atlanmaz.

```mermaid
flowchart TD
    A["1. KOSUM-PLANI.md oku (bu dosya)"] --> B["2. Oturum satirini bul: dosya + bolum araligi"]
    B --> C["3. Serit ortamini kur, uygulamayi baslat, /health dogrula"]
    C --> D["4. Reset yordamini uygula"]
    D --> E["5. Case'leri SIRAYLA kos, her birini bitirince YAZ"]
    E --> F["6. Kalan case'ler icin HATA kaydi + fiziksel eylem listesi"]
    F --> G["7. Commit + devir notu, dur"]
```

**Adım 5 kuralı:** Bir case bitince sonucu **hemen** dosyaya yaz. Oturumun
sonunda toplu yazma yok — oturum bütçesi biterse yazılmamış her şey kaybolur.

**Adım 7 kuralı:** Oturum kendi dalında commit eder:

```bash
git add docs/manuel-test && git commit -m "manuel test S$SERIT: <dosya> <bolum araligi> kosuldu"
```

Devir notu, sonuç dosyasının başına yazılır: nerede kalındı, sonraki oturum
neyle başlamalı, hangi ön koşul bozuk kaldı.

### Bütçe

| Oturum türü | Case sayısı |
|---|---|
| Saf CLI (`curl`, `psql`, `dotnet`) | ~40 |
| Karışık (birkaç arayüz case'i) | ~28 |
| Arayüz ağırlıklı (Playwright) | ~18 |

Bütçe aşılırsa oturum **durur**, kaldığı yeri devir notuna yazar. Yarım okunan
bir case'in sonucu yazılmaz.

---

## 5. Sonuç kaydı

### 5.1 Case içine (birincil kayıt)

```markdown
**Gerçek sonuç**
`valid:true`, `messages:[]`. `GET /api/agents` sonrasında kayıt yok — beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
```

`Gerçek sonuç` **gözlenen** şeyi yazar, beklentiyi tekrarlamaz. Kaldıysa log
alıntısı ve `dosya.cs:satır` referansı taşır.

### 5.2 Şerit sonuç dosyası

`docs/manuel-test/SONUCLAR-S<N>-<YYYY-AA-GG>.md`. Şerit başına ayrı dosya —
birleştirmede çakışma olmaz. Biçim, koşulmuş
[`SONUCLAR-2026-08-12.md`](../arsiv/manuel-test-kosum-2026-08/SONUCLAR-2026-08-12.md) dosyasıyla aynıdır:
başlık bloğu (koşulan dosya, ortam, sayım, devir notu) + yalnız `Kaldı`
case'lerin `HATA-NNN` kayıtları ([`00-INDEKS.md`](00-INDEKS.md) §6 şablonu).

Hata numarası şerit önekiyle verilir: `HATA-S2-001`. Böylece dört şerit aynı
numarayı üretmez.

### 5.3 Fiziksel eylem listesi

Ajanın koşamadığı her case, şerit sonuç dosyasının sonuna bu tabloyla yazılır:

```markdown
## Kullanıcı eylemi bekleyen case'ler

| Case | Neden | Kullanıcıdan istenen |
|---|---|---|
| MT-MM-091 | Hoparlör çıktısı | Üretilen `.mp3` dinlenir, ses anlaşılır mı? |
```

Bu case'ler `☑ Atlandı` değil, `☐ Beklemede` kalır — koşulmadılar.

---

## 6. Playwright kuralları (arayüz oturumları)

Playwright MCP proje ayarında kayıtlıdır (`npx @playwright/mcp@latest`).

| Kural | Neden |
|---|---|
| Önce `browser_snapshot`, sonra tıkla | Erişilebilirlik ağacı ekran görüntüsünden ucuz ve kararlıdır. |
| Ekran görüntüsü **yalnız** kanıt gerektiğinde | Her adımda görüntü almak oturum bütçesini bitirir. |
| Kanıt yolu: `docs/manuel-test/kanit/S4/<case>.png` | Sonuç dosyası bu yolu referans verir. |
| Konsol hatası her case'te kontrol edilir | `browser_console_messages` — sessiz JS hatası "Geçti" gibi görünür. |
| Dar ekran testi `browser_resize` ile | Gerçek cihaz gerekmez. |
| Tema/dil geçişi arayüzden yapılır | Depoyu elle değiştirme; kullanıcı yolunu test ediyorsun. |
| Site verisi temizleme | Yeni bağlam aç (`browser_tabs`) ya da `localStorage.clear()` + yenile. |

Ekran okuyucu (VoiceOver) case'leri **kullanıcıya** gider — §5.3 tablosuna yazılır.

---

## 7. Şeritler ve oturumlar

Kalan 981 case, 40 oturuma bölündü. Bölme noktaları dosyaların kendi `#`
bölüm başlıklarıdır — bir bölümün ortasında oturum bitmez.

### Şerit 1 — Kalıcılık ve ses · port 5081 · şema `mt_s1`

Kalıcılık sağlayıcısı case'e göre değişir; bu şerit `AgentPrism__Sqlite__*` ve
`AgentPrism__SqlServer__*` değişkenlerini en çok o kullanır.

| Oturum | Dosya | Bölüm | Case | Not |
|---|---|---|---|---|
| S1-1 | [`04`](04-KALICILIK-DIGER.md) | §1–§3 | 19 | SQLite + SQL Server bağlantı, migration. `MT-SQL-001/021` koşuldu, atla. |
| S1-2 | [`04`](04-KALICILIK-DIGER.md) | §4–§8 | 18 | Sağlayıcıya özgü davranış, bellek içi izlek, taşınabilirlik, yük. |
| S1-3 | [`23`](23-SAKLAMA-ARSIV-KOTA.md) | tümü | 26 | SQLite ile koş — saklama silme yolları en hızlı orada görünür. |
| S1-4 ✅ | [`20`](20-BELLEK-RAG-BAGLAM.md) | tümü | 31 | **Bitti** (2026-08-13): 13 Geçti, 10 Kaldı, 8 Beklemede (embedding erişimi yok — bkz. `SONUCLAR-S1-2026-08-13.md`). |
| S1-5 | [`25`](25-SAGLIK-TESHIS-OPENAPI.md) | tümü | 28 | Üç sağlayıcıyı da sırayla dener; her geçişte reset. |
| S1-6 ✅ | [`19`](19-COK-MODLULUK-VE-SES.md) | §1–§8 | 37 | **Bitti** (2026-08-13): 35 Geçti, 1 Kaldı (`HATA-S1-014`), 1 Atlandı (`MT-MM-047`, yapısal `maxOutputTokens` sınırı). Ayrıntı `SONUCLAR-S1-2026-08-13.md`. |
| S1-7 ✅ | [`19`](19-COK-MODLULUK-VE-SES.md) | §9–§13 | 24 | **Bitti** (2026-08-13): 17 Geçti, 2 Kaldı (`HATA-S1-014` kapsam genişlemesi + yeni `HATA-S1-015`), 1 Atlandı (`MT-MM-089`, `AllowRemoteAccess` kapalı), 4 Beklemede (`MT-MM-086/087/088/090`). S1-8'de 2 Kaldı → Geçti; **S1-9'da 4 Beklemede → Geçti** (sahte `Voice:ApiKey`/`DefaultVoiceId` kök nedendi, gerçek mikrofon değil). **Dosya `19` TAMAMLANDI (61/61), 0 açık kalem.** Ayrıntı `SONUCLAR-S1-2026-08-13.md`. |

### Şerit 2 — HTTP ve güvenlik · port 5082 · şema `mt_s2`

Bu şerit çoğunlukla bellek içi kalıcılıkla koşar (üç bağlantı dizesi de boş) —
en hızlı şerittir.

| Oturum | Dosya | Bölüm | Case | Not |
|---|---|---|---|---|
| S2-1 ✅ | [`07`](07-HTTP-YONETIM-API.md) | tümü | 43 | **Bitti** (2026-08-13): 41 Geçti, 2 Kaldı. |
| S2-2 ✅ | [`08`](08-OPENAI-UYUMLU-UCLAR.md) | §1–§2 | 30 | **Bitti**, S2-3 ile birlikte dosya `08` TAMAMLANDI (49/49, 43 Geçti, 6 Kaldı). |
| S2-3 ✅ | [`08`](08-OPENAI-UYUMLU-UCLAR.md) | §3–§5 | 19 | **Bitti** (yukarıdaki S2-2 toplamına dahil). |
| S2-4 ✅ | [`13`](13-KIRACI-VE-GUVENLIK.md) | §1–§5 | 27 | **Bitti**: 27/27 tümü Geçti. |
| S2-5 ✅ | [`13`](13-KIRACI-VE-GUVENLIK.md) | §6–§9 | 27 | **Bitti**, S2-4 ile birlikte dosya `13` TAMAMLANDI (54/54, 53 Geçti, 1 Kaldı). |
| S2-6 ✅ | [`18`](18-MCP-VE-A2A.md) | tümü | 43 | **Bitti**: 38 Geçti, 5 Kaldı. |
| S2-7 ✅ | [`21`](21-DAYANIKLILIK-VE-IPTAL.md) | tümü | 28 | **Bitti — ŞERİT 2 TAMAMEN BİTTİ**: 24 Geçti, 2 Kaldı, 2 Atlandı. Ayrıntı: `SONUCLAR-S2-2026-08-13.md`. |

### Şerit 3 — Çekirdek ve sağlayıcı · port 5083 · şema `mt_s3`

Gerçek sağlayıcı çağrılarının çoğu buradadır. §2.5 maliyet kuralı geçerlidir.

| Oturum | Dosya | Bölüm | Case | Not |
|---|---|---|---|---|
| S3-1 ✅ | [`05`](05-SAGLAYICI-OPENAI.md) | §1–§4 | 13 | **Bitti** (2026-08-13): `02` dosyası zaten 42/42 tam (bkz. §1 düzeltme notu); S3-1 kapsamı yalnız `05` §1–§4'e daraldı. 11 Geçti, 2 Kaldı (`HATA-S3-001` Endpoint doğrulaması, `HATA-S3-002` boş model adı doğrulaması — ikisi de `Bind()`'ın doğrulayıcıya ulaşmadan geçersiz değeri sessizce elemesi). Ayrıntı `SONUCLAR-S3-2026-08-13.md`. |
| S3-2 ✅ | [`05`](05-SAGLAYICI-OPENAI.md) | §5–§9 | 27 | **Bitti — dosya `05` TAMAMEN BİTTİ (40/40)** (2026-08-13): 26 Geçti, 1 Atlandı (`MT-OAI-053`, Ollama kurulu değil), 0 Kaldı. Yeni kusur yok. Ayrıntı `SONUCLAR-S3-2026-08-13.md`. |
| S3-3 ✅ | [`06`](06-SAGLAYICI-DIGER.md) | §1–§8 | 30 | **Bitti** (2026-08-13): 28 Geçti, 2 Kaldı (`HATA-S3-003`, `HATA-S3-004` — `HATA-S3-001`/`002`'nin Anthropic+Google genellemesi). Ayrıntı `SONUCLAR-S3-2026-08-13.md`. |
| S3-4 ✅ | [`06`](06-SAGLAYICI-DIGER.md) + [`22`](22-GUARDRAIL-VE-YAPISAL-CIKTI.md) | 06 §9 + 22 §1–§4 | 26 | **Bitti — dosya `06` TAMAMEN BİTTİ (39/39)** (2026-08-13): 06 §9 Azure 9 case ⏭ Atlandı (kimlik yok); 22 §1–4 17/17 Geçti. `HATA-S3-005` (akışsız `run` ucu K-296 düzeltmesini almamış). |
| S3-5 ✅ | [`22`](22-GUARDRAIL-VE-YAPISAL-CIKTI.md) | §5–§8 | 18 | **Bitti — dosya `22` TAMAMEN BİTTİ (35/35)** (2026-08-13): 15 Geçti, 3 Kaldı. **`HATA-S3-006` (Kritik)** — guard'ın maskelediği/engellediği girdi `RunStarted` olayında kalıcı, ham hâliyle. `HATA-S3-007` — `errorType` sorgu filtresi sessizce yok sayılıyor. |
| S3-6 ✅ | [`16`](16-IS-KUYRUGU-VE-ZAMANLAMA.md) | §1–§5 | 37 | **Bitti** (2026-08-13): 37/37 Geçti, 0 Kaldı. Yeni kusur yok — iki doküman düzeltmesi (`PollInterval<=0` uygulamayı başlatmaz; örnek `curl` eksik başlık). |
| S3-7 ✅ | [`16`](16-IS-KUYRUGU-VE-ZAMANLAMA.md) | §6–§8 | 24 | **Bitti — dosya `16` TAMAMEN BİTTİ (61/61), ŞERİT 3 TAMAMEN BİTTİ** (2026-08-13): 22 Geçti, 2 Kaldı. `HATA-S3-008` (idempotency replay `Location` başlığını kaybediyor), `HATA-S3-009` (`RunsRead` anahtarı zamanlama silebiliyor — `MT-WF-100`'ün aynı kalıbı). Ayrıntı `SONUCLAR-S3-2026-08-13.md`. |

### Şerit 4 — Arayüz · port 5084 · şema `mt_s4` · Playwright

| Oturum | Dosya | Bölüm | Case | Not |
|---|---|---|---|---|
| S4-1 | [`09`](09-ARAYUZ-GENEL.md) | §1–§2 | 17 | Erişim kapısı, gezinme kabuğu, rol görünürlüğü. |
| S4-2 | [`09`](09-ARAYUZ-GENEL.md) | §3–§5 | 17 | Komut paleti, kısayol, tema, i18n derin metin denetimi. |
| S4-3 | [`09`](09-ARAYUZ-GENEL.md) + [`10`](10-ARAYUZ-AGENT-PLAYGROUND.md) | 09 §6–§8 + 10 §1–§2 | 22 | 09 §8 Safari → kullanıcıya (Playwright'ta WebKit varsa dene). |
| S4-4 | [`10`](10-ARAYUZ-AGENT-PLAYGROUND.md) | §3–§5 | 11 | Agent düzenleme, detay, versiyon karşılaştırma. |
| S4-5 | [`10`](10-ARAYUZ-AGENT-PLAYGROUND.md) | §6 | 16 | Playground temel akış — en yoğun tek bölüm. |
| S4-6 | [`10`](10-ARAYUZ-AGENT-PLAYGROUND.md) + [`11`](11-ARAYUZ-RUN-SESSION-SSE.md) | 10 §7–§9 + 11 §1 | 17 | Ek ve ses UI katmanı; çalıştırma listesi. |
| S4-7 | [`11`](11-ARAYUZ-RUN-SESSION-SSE.md) | §2–§4 | 19 | Çalıştırma detayı, SSE dayanıklılığı, iptal. |
| S4-8 | [`11`](11-ARAYUZ-RUN-SESSION-SSE.md) | §5–§9 | 21 | Yeniden oynatma, karşılaştırma, oturum, dallandırma. |
| S4-9 | [`12`](12-GOZLEMLENEBILIRLIK-MALIYET.md) | §1–§7 | 20 | Dashboard, uyarılar, iz/waterfall. Veri gerekir → önce birkaç run üret. |
| S4-10 | [`12`](12-GOZLEMLENEBILIRLIK-MALIYET.md) | §8–§12 | 16 | Maliyet hesabı, zaman serisi ucu, `dotnet-counters`. |

### Ortak kuyruk — şeridi biten ajan buradan alır

Şerit 1, 2, 3 yedi oturumda biter; şerit 4 onda. Boşa düşen ajan **kuyruğun en
üstündeki** oturumu alır ve kendi şerit numarasında koşar. Alınan oturumun
yanına ajan adını yazar — iki ajan aynı oturumu almaz.

| Oturum | Dosya | Bölüm | Case | Kilit |
|---|---|---|---|---|
| K-1 ✅ | [`14`](14-SKILL-VE-SCRIPT.md) | §1–§2 | 20 | **Bitti** (2026-08-13): 19 Geçti, 1 Kaldı (`HATA-K-001`). Ayrıntı `SONUCLAR-K-2026-08-13.md`. |
| K-2 ✅ | [`14`](14-SKILL-VE-SCRIPT.md) | §3–§5 | 18 | **Bitti** (2026-08-13): 18/18 Geçti, kusur yok. Ayrıntı `SONUCLAR-K-2026-08-13.md`. |
| K-3 ✅ | [`14`](14-SKILL-VE-SCRIPT.md) + [`15`](15-WORKFLOWS.md) | 14 §6–§7 + 15 §1 | 28 | **Bitti** (2026-08-13): 21 Geçti, 7 Kaldı — **dosya `14` TAMAMEN BİTTİ (47/47)**. `HATA-K-002` (🚨 Kritik: script çalıştırma özelliği `JsonSerializerOptions` çökmesiyle tamamen işlevsiz). Ayrıntı `SONUCLAR-K-2026-08-13.md`. |
| K-4 ✅ | [`15`](15-WORKFLOWS.md) | §2–§6 | 26 | **Bitti** (2026-08-13): 23 Geçti, 3 Kaldı. `HATA-K-003` (Yüksek: Magentic plan onayı sonrası devam `ExecutorFailed`/`RunFailed` ile çöküyor). Ayrıntı `SONUCLAR-K-2026-08-13.md`. |
| K-5 ✅ | [`15`](15-WORKFLOWS.md) + [`17`](17-EVAL-VE-DENEYLER.md) | 15 §7–§9 + 17 §1–§2 | 27 | **Bitti** (2026-08-14): 21 Geçti, 6 Kaldı — **dosya `15` TAMAMEN BİTTİ (60/60)**. `HATA-K-004/005/006`. Ayrıntı `SONUCLAR-K-2026-08-13.md`. |
| K-6 ✅ | [`17`](17-EVAL-VE-DENEYLER.md) | §3–§6 | 30 | **Bitti** (2026-08-14): 30/30 Geçti, kusur yok. Ayrıntı `SONUCLAR-K-2026-08-13.md`. |
| K-7 ✅ | [`17`](17-EVAL-VE-DENEYLER.md) | §7–§11 | 26 | **Bitti** (2026-08-14): 20 Geçti, 4 Kaldı — **dosya `17` TAMAMEN BİTTİ (69/69)**. `HATA-K-007/008`. Ayrıntı `SONUCLAR-K-2026-08-13.md`. |
| K-8 ✅ | [`24`](24-TEST-PAKETI-VE-SABLON.md) | §1–§2 | 24 | **Bitti** (2026-08-14): 24/24 Geçti, kusur yok, 2 doküman düzeltmesi. Ayrıntı `SONUCLAR-K-2026-08-13.md`. |
| K-9 ✅ | [`24`](24-TEST-PAKETI-VE-SABLON.md) | §3–§5 | 17 | **Bitti** (2026-08-14): 16 Geçti, 1 Kaldı — **dosya `24` TAMAMEN BİTTİ (41/41)**. Doküman düzeltmesi (MT-TEST-044). Ayrıntı `SONUCLAR-K-2026-08-13.md`. |

`14` dosyasının `MT-SKILL-071` case'i koşuldu; atla.

### Ortak kuyruk'un bulduğu hatalar

**Hepsi kapandı (2026-08-14, kapanış oturumu) — kullanıcı kararıyla "8 kusuru
sırayla kodla" seçildi: Kritik önce, sonra Yüksek, her düzeltmeden sonra dört
doğrulama kapısı, her satır canlı sunucuda yeniden doğrulandı.** Ayrıntı ve
karar gerekçeleri: `docs/KARARLAR.md` K-400..K-407,
[`SONUCLAR-K-2026-08-13.md`](../arsiv/manuel-test-kosum-2026-08/SONUCLAR-K-2026-08-13.md).

| Hata | Önem | Durum |
|---|---|---|
| `HATA-K-002` — Skill script çalıştırma iki ayrı kök nedenle tamamen çalışmıyordu | Kritik | ✅ Düzeltildi (K-400) |
| `HATA-K-003` — Magentic plan onayı sonrası devam `ExecutorFailed`/`RunFailed` ile opak hata veriyor | Kritik | ✅ Düzeltildi (K-401, kısmi — "zarif durdurma" F-106'ya yazıldı) |
| `HATA-K-004` — `AgentPrismWorkflowOptions` hiçbir konfigürasyon kaynağına bağlı değil | Kritik | ✅ Düzeltildi (K-402) |
| `HATA-K-005` — Workflow `run` ucunda `sessionId` doğrulama hatası düz `HTTP 500`'e düşüyor | Kritik | ✅ Düzeltildi (K-403) |
| `HATA-K-001` — `POST/PUT /api/agents` bilinmeyen skill/tool/callable-agent adını SAVE zamanında hiç doğrulamıyor | Yüksek | ✅ Düzeltildi (K-404) |
| `HATA-K-006` — `WorkflowEndpoints` API anahtarı kapsam denetimi hiç uygulamıyor | Yüksek | ✅ Düzeltildi (K-405) |
| `HATA-K-007` — `AgentPrism:RunRecording:RecordRunInput` config'ten hiçbir zaman okunmuyor | Yüksek | ✅ Düzeltildi (K-406) |
| `HATA-K-008` — `ApiKeyScope`'ta Eval/Experiment için kapsam yok, `RunEndpoints`'te `feedback`/`compare`/`input` kapsamsız | Yüksek | ✅ Düzeltildi (K-407) |

Ortak kuyruk'ta kod/kusur açığı **sıfır**. `SchedulingEndpoints`
(`MT-JOB-090`) ve `GovernanceEndpoints`'in aynı kapsam-eksikliği kalıbını
taşıması bu koşumun konfirme ettiği listeye dahil değildi — bu düzeltmenin
kapsamı dışında bırakıldı, ayrı bir bulgu/düzeltme gerektirir.

---

## 8. Kapanış

> **Bu bölüm [`KAPANIS-PLANI.md`](KAPANIS-PLANI.md) tarafından yürütülür.**
> Orada 75 açık case, 21 kusur ailesine bölünmüştür; her aile ayrı bir oturumda
> kodlanır. Aşağıdaki merge adımı **gereksizdir** — dört şeridin dalı zaten
> `main`'e alınmış ve worktree'ler kaldırılmıştır.

Tüm şeritler bitince tek bir toplama oturumu:

```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
for n in 1 2 3 4; do git merge --no-ff test/kosum-s$n -m "manuel test: serit $n sonuclari"; done
```

Senaryo dosyaları şeritlere göre bölündüğü için çakışma beklenmez. Çakışma
çıkarsa iki şerit aynı dosyaya yazmış demektir — bu bir plan ihlalidir, kaydet.

Sonra:

1. Dört şeridin `SONUCLAR-S*.md` dosyaları tek bir `SONUCLAR.md` içinde
   birleştirilir; hatalar önem sırasına dizilir.
2. `00-INDEKS.md` §7 tablosunun `Koşum` sütunu güncellenir.
3. Kusurlar **kodlanır** — Kritik ve Yüksek olanlar önce. Her düzeltme sonrası
   dört doğrulama kapısı ([`AGENTS.md`](../../AGENTS.md)) koşar.
4. Yeni yetenek isteyen bulgular kodlanmaz; faz adayı olarak
   [`docs/UCUNCU-FAZ-ADAYLARI.md`](../UCUNCU-FAZ-ADAYLARI.md)'ya yazılır.
5. `git worktree remove ../ap-s1 …` ile çalışma kopyaları silinir.

---

## 9. Şerit özeti

| Şerit | Port | Şema | Dosyalar | Case | Oturum |
|---|---|---|---|---|---|
| 1 — Kalıcılık ve ses | 5081 | `mt_s1` | 04, 23, 20, 25, 19 | 183 | 7 |
| 2 — HTTP ve güvenlik | 5082 | `mt_s2` | 07, 08, 13, 18, 21 | 217 | 7 |
| 3 — Çekirdek ve sağlayıcı | 5083 | `mt_s3` | 02, 05, 06, 22, 16 | 189 | 7 |
| 4 — Arayüz | 5084 | `mt_s4` | 09, 10, 11, 12 | 176 | 10 |
| Ortak kuyruk | — | — | 14, 15, 17, 24 | 216 | 9 |
| **Toplam** | | | **23 dosya** | **981** | **40** |

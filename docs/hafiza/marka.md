# Marka: Hikaye, Metafor, Ses

> Tracon'un marka karakteri. **Kullanıcıya dönük metne dokunan her iş burayı
> okur** — `docs-site/`, paket `Description` alanları, `README.md`, sosyal kart,
> ekran görüntüsü, hero metni.
>
> Sınır: **pakete giren metnin DOĞRULUĞU** [`dokumantasyon.md`](dokumantasyon.md)'dedir,
> **o metnin SESİ** buradadır. Site yayın hattı [`site-yayin-ve-tema.md`](site-yayin-ve-tema.md),
> üretim betikleri [`site-uretim-kapilari.md`](site-uretim-kapilari.md).
>
> Bu dosya Türkçe'dir ve **siteye kopyalanmaz**. İçindeki İngilizce bloklar
> sevk edilebilir dizelerdir; gerisi talimattır.

## 1. Tek cümlede

Tracon, Microsoft Agent Framework üzerine kurulan bir .NET paket ailesidir ve
üretimde çalışan bir **kontrol düzlemi** sağlar. Agent'ı o çalıştırmaz — MAF
çalıştırır. Tracon çalışanı kaydeder, sınırlar, yetkilendirir, ayırır ve
görünür kılar.

## 2. İsmin hikayesi

**TRACON** havacılıkta gerçek bir tesis sınıfının adıdır: *Terminal Radar
Approach Control*. Kulenin bir üst katmanıdır. Yoğun hava sahasındaki trafiği
**sıraya sokar**, uçakları **ayırır**, **clearance** verir, radar üzerinden
sürekli **izler** ve kuleye **handoff** eder.

**TRACON uçağı uçurmaz.** Pilot uçurur. TRACON izin verir, nereye gideceğini
söyler, çakışmayı engeller, her teması kaydeder. Ürünün MAF karşısındaki konumu
budur: isim benzetme değil, mimarinin tarifidir.

**İkinci okuma.** Havacılığı bilmeyen bir geliştirici `Tracon` gördüğünde
**TRAC(e) + CON(trol)** okur. Ürün OpenTelemetry `span`'leri üzerine kurulu bir
kontrol düzlemidir. İki okuma da aynı yere çıkar.

🚨 **Dürüstlük sınırı.** İkinci okuma bir **tasarım tercihi** olarak sunulabilir.
Akronimin "trace" kelimesinden türediği **iddia edilemez**; açılımı *Terminal
Radar Approach Control*'dür. Uydurulmuş etimoloji yasaktır.

**Yazım.** Marka metninde **`Tracon`** — baş harf büyük, gerisi küçük.
**`TRACON`** biçimi yalnız havacılık tesisinden söz ederken kullanılır. Marka
bağırmaz.

## 3. Metafor haritası

Sitenin omurgası. Her satırın kodda karşılığı vardır; hiçbiri uydurma değildir.

| Kulede | Üründe |
|---|---|
| **Clearance** — izinsiz kalkamaz | Authorization, API key `scope`'u, approval, egress policy, content guard |
| **Radar contact** — sürekli izlenir | `run` kaydı, `span`, metric, cost, canlı SSE |
| **Separation** — trafik karışmaz | Multi-tenancy; her sorguda `tenant_id` |
| **The strip** — oynanamaz fiziksel kayıt | `audit_log`, hash zinciri, `GET /api/audit/verify` |
| **Sequencing** | Workflow, job, kuyruğa alınan `run` |
| **Handoff** | Agent-to-agent, A2A, MCP |
| **Squawk** — kim, hangi kimlikle | API key kimliği, kiracı, çağıran |
| **Ground stop** | Circuit breaker, canary rollback, kill switch |
| **Say intentions** | Agent definition, versiyon, rollback |
| **Go around** | Retry, replay, checkpoint'ten devam |

**"The strip" neden birebir:** havacılıkta *flight progress strip*, bir uçağın her
temasının yazıldığı, oynanamayan fiziksel kayıttır — hash zincirli `audit_log`'un
karşılığı odur. Zorlanmış benzetme değildir.

## 4. Metaforun sınırı

| Metafor **serbest** | Metafor **yasak** |
|---|---|
| Hero, landing, sosyal kart, `README` | XML dokümanı |
| Bölüm ve sayfa başlıkları | `api/` (üretilen referans) |
| Kavram sayfasının giriş paragrafı | `reference/` sözleşme sayfaları |
| Paket `Description` alanları | `http-api/`, hata mesajı ve kodu |
| | `troubleshooting/` adımları |

**Gerekçe:** `clearance` kelimesi `authorization`'ın yerine geçerse tüketici yanlış
tip arar. **Metafor pazarlar, doküman tarif eder.**

## 5. Ses ve ton

**Karakter: kontrolör.** Satıcı değil, amigo değil. Sakin, kısa ve kesin — acil
durumda bile. Otorite ses tonundan değil yetkinlikten gelir; telsiz frazeolojisi
minimaldir çünkü **belirsizlik öldürür**.

1. **Sakin otorite.** Ünlem yok, bağırma yok, "devrim" yok. Bildirir.
2. **Ekonomi.** Her kelime yük taşır; uzun cümle güvensizliğin işaretidir.
3. **Kanıt.** Her iddianın yanında bir sayı, bir `endpoint` veya bir komut var.

**Ses testi:** *"Bir kontrolör bunu telsizde söyler miydi?"*

Bizim sesimiz:

```
Recording is on by default for catalog-resolved agents. Stored events can be replayed over SSE.
Recording is best-effort: a store failure is logged while the agent continues.
Without a persistence provider configured, the default stores use memory.
The console can create an agent. It can never write tool code.
```

Bizim sesimiz değil:

```
Supercharge your AI agents with the ultimate control plane!
Effortlessly manage agents at scale with our powerful, enterprise-grade platform.
The only solution you will ever need for production AI.
```

## 6. Yasaklar

**Kelime:** `blazingly fast`, `100% secure`, `effortless`, `seamless`, `magic`,
`revolutionary`, `game-changing`, `ultimate`, `best-in-class`, `enterprise-grade`,
`unlimited`, `zero-config`, `just works`.

**Yapı:**

- Kanıtsız yüzde, süre veya "X kat daha hızlı" iddiası
- Ürünün sunmadığı bir yeteneğin ima edilmesi
- Uydurulmuş etimoloji (§2)
- Emoji ile vurgulama (tablo durum işaretleri hariç)
- DevUI karşılaştırması bir **konumlandırmadır**, saldırı değil. DevUI'nin kendi
  dokümanı "production için değildir" der; alıntı odur, yorum değil.

**Sevk edilen metin kuralı ayrıca geçerlidir** ([`dokumantasyon.md`](dokumantasyon.md)):
faz numarası, `K-NNN`, `F-NN`, `MT-*`, `docs/NN-*.md` referansı ve 🚨/⚠️ sesi
tüketiciye giden hiçbir metinde yer alamaz. Kapı:
`ShippedDocumentationSelfContainmentTests`.

## 7. İddia edilebilecek gerçekler

**Tek kaynak [`README.md`](../../README.md)'dir** — hero listesi, paket tablosu
ve HTTP yüzeyi örnekleri. Site metni oradan beslenir; bu dosyaya kopyalanmaz.
README ile site çelişirse **README doğrudur**.

## 8. Söylenmeyecekler

🚨 **Yayın durumu.** Bugün hiçbir paket NuGet'te veya npm'de **değildir** ve
release etiketi yoktur. Site "install from NuGet" adımını gerçekleşmiş gibi
sunamaz. README'nin dili doğrudur ve korunur: *in development, not yet published
— build from this repository.* Kurulum sayfaları bu uyarıyı taşır.

Ayrıca gizlenemeyecek sınırlar:

- `Skill script` çalıştırma ve content guard **varsayılan kapalıdır**; kodda
  açıkça açılır. Skill script'leri sunucuda, Tracon'dan işletim sistemi düzeyinde
  **yalıtılmadan** çalışır — o sınır barındırma ortamının işidir.
- `Sqlite` tek yazarlıdır; çok örnekli dağıtıma uygun değildir.
- Ön sürüm MAF bağımlılığı yalnız `Tracon.AspNetCore` içindedir (K-008).
- `Tracon.AspNetCore` ve `Tracon.UI` AOT uyumlu **değildir**.

## 9. Onaylanmış metin blokları

```
Tagline
  Tracon — approach control for your agents.

Hero  (H1 tanimdir, metafor degil — bkz. asagidaki not)
  The .NET control plane for Microsoft Agent Framework.

  A package family that runs inside your own ASP.NET Core host. MAF runs your
  agents. Tracon records them, scopes them to a tenant, and governs what they
  may call.

Hero kicker  (metafor burada yasar)
  Tracon / Approach control for your agents.

Düz konumlandırma (NuGet Description, GitHub About, meta description)
  The production-oriented control plane for Microsoft Agent Framework — recorded
  runs, tenant isolation, tamper-evident audit trail, embedded console.

Dörtlü
  Separate. Sequence. Clear. Record.

Kısa vuruşlar
  MAF flies the aircraft. Tracon runs the airspace.
  No clearance, no takeoff.
  Every run traced. Every call cleared.
  Autonomy, under clearance.
  The tower ships in the assembly.
  Two lines to the tower.

Paket Description kalıbı
  Tracon.Abstractions  Contracts only. Enough on its own if you write your own implementations.
  Tracon.Core          Runtime, catalog, definition compiler and tool registry. No database required.
  Tracon.AspNetCore    The frequency: management API, OpenAI-compatible endpoints, multi-tenancy.
  Tracon.PostgreSql    Persistence in its own tracon schema, with embedded migrations.
  Tracon.UI            The tower. An embedded React console — 30 screens, zero JavaScript dependencies.
  Tracon.Mcp           Remote MCP tools. Approval by default.
  Tracon.Workflows     Sequencing and handoff: five patterns, checkpoints, resume, human-in-the-loop.
  Tracon.Testing       Fly the whole approach without calling a model.
  Tracon.Cli           Ground operations: migrate, migrate status, health.
```

> 🚨 **H1 metafor DEGILDIR** — H1 tanimdir. Metafor tagline'da, hero kicker'inda,
> footer'da ve "Kisa vuruslar"da yasar ama **tanimin yerini almaz**. Bunu geri alma.
>
> 🚨 **Paketler yayinlanana kadar `production-oriented`** — `production control
> plane` / `production-grade` degil. Uc yuzey birlikte tasinir: site meta
> description, kok `README.md` ve `src/Tracon/Tracon.csproj` `<Description>`.
> 1.0 yayinlandiktan sonra donulebilir.
>
> DevUI karsilastirmasinin tam hali `/getting-started/devui/` sayfasindadir;
> iddialar yalniz Microsoft'un yayinlanmis dokumanindan alinir.
> Geri alma gerekceleri: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
>
> 🚨 **Uc iddia kelimesi olculerek secildi; geri alma.**
> - **`queries`, `stores` degil.** Kiraci yalitimi *uygulama katmanindadir*:
>   her sorgu cozulmus tenant'i tasir, veritabani RLS'i **yoktur ve bilincli
>   yoktur**. "Tenant-scoped stores" depolama seviyesinde ayrim ima ediyordu.
>   Dogru: `Tenant-scoped queries keep each tenant's records logically isolated`.
>   Kaynak: `concepts/governance.md` "Isolation lives in the application layer".
> - **`persisted`, `durable` degil.** Kayit *best-effort*'tur: store hatasi
>   loglanir ve agent devam eder. "Durable" dagitik sistem sozlugunde teslim
>   garantisi cagristirir. Dogru: `persisted run records`.
> - **`one implementation of that production-facing layer`, `that interface`
>   degil.** Microsoft "kendi arayuzunu yaz" diyor; "Tracon o arayuzdur" demek
>   tavsiyenin resmi cevabi Tracon'mus gibi okunuyordu.
>
> 🚨 **Anasayfa envanter satiri "20 modular packages" der, "20 NuGet packages"
> DEMEZ** — ayni ekranda paketlerin henuz yayinlanmadigi yaziyor. Kapi bu
> etiketi zorlar: `docs-site/scripts/check-content.mjs` metrik kontrolu.
> `reference/versioning.md` ve `SECURITY.md` yayin surecini anlattigi icin
> orada "NuGet packages" dogru ifadedir.

## 10. Terminoloji

| Kullan | Kullanma | Neden |
|---|---|---|
| control plane | framework, platform, wrapper | Ürün MAF'ı sarmalamaz |
| package family | library, SDK | Meta paket dahil 20 NuGet paketi |
| agent | bot, assistant, AI worker | MAF'ın terimi |
| `run`·`session`·`tenant`·`tool`·`span`·`store` | çevrilmiş karşılıklar | Tip adlarıyla birebir |
| the console | dashboard, admin panel | Ürünün kendi adı |
| Tracon | TRACON (marka olarak) | §2 |

## 11. Görsel yön

Metafor **alet diliyle** çalışır, uçak resmiyle değil.

- **Evet:** radar süpürme çizgisi, ayrım halkası, sıralama kuyruğu, ilerleme
  şeridi (`strip`), monospace `callsign` etiketi, koordinat ve saat damgası
  estetiği, karanlık enstrüman paleti üzerinde tek vurgu rengi
- **Hayır:** uçak clipart'ı, bulut illüstrasyonu, pilot/kule fotoğrafı,
  skeuomorfik kokpit, havacılık kostümü
- **Renk:** durum renkleri **anlam taşır** (cleared / holding / denied),
  dekorasyon değildir
- **Hareket:** varsa ölçülü ve periyodik (süpürme gibi), dikkat çekmek için değil

**Kural:** siteye bakan kişi "havacılık teması" değil, **bir operasyon ekranı**
görmelidir.

## 12. Teslim öncesi kontrol listesi

- [ ] Hiçbir sayfa paketlerin yayımlandığını ima etmiyor (§8)
- [ ] Metafor `reference/`, `api/`, `http-api/`, `troubleshooting/` içinde yok (§4)
- [ ] Akronimin "trace"ten türediği hiçbir yerde iddia edilmiyor (§2)
- [ ] Yasak kelimelerin hiçbiri geçmiyor (§6)
- [ ] Her iddianın arkasında sayı, `endpoint` veya komut var (§5)
- [ ] Ekran görüntüleri yeni marka ve `/tracon` route'u ile yeniden üretildi
- [ ] `locales/en.ts` ↔ `tr.ts` anahtar kümesi eşit (K-228)
- [ ] `tuketici-dokuman-senkronu` kalite sözleşmesi koşuldu

## 13. Yeniden adlandırmanın bıraktığı iz (Faz 163)

🚨 **Bul-değiştir artikeli göremez.** Ürün `AgentPrism`'den `Tracon`'a döndü ve
artikel eski ada göre seçilmişti: geriye **51** yerde `an Tracon` kaldı, biri her
tüketicinin build çıktısında görünen derleyici tanısıydı. **Yanlış olan kelime,
değiştirilmeyen kelimedir.** Dört varyant vardır ve her tarama yalnız bir
sonrakini açığa çıkarır; dördüncüsünde artikel satır sonunda kalır ve ad sonraki
satırın `/// ` önekinden **sonra** başlar. Bir sonraki yeniden adlandırmada
`samples/` ve `tests/` dahil **dört varyantı da** tara. Tarama deseni ve `\b`
tuzağı: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md). Kapı:
`check-content.mjs` bunu **üretilen sayfalarda da** zorlar.

🚨 **Alarm emojisi el yazısı sayfaya sızar.** `build-api-reference.mjs` üretilen
sayfalarda `🚨|⚠️` → `**Important:**` dönüşümü yapar ve paket XML'i
`ShippedDocumentationSelfContainmentTests`'e tabidir. `src/content/docs/` altına
elle yazılan bir sayfa **ikisinden de geçmez**; üç paragraf bu yüzden sevk edildi.
Uyarıyı koru, işareti at: kalın metin veya `:::caution` aside. Kapı eklendi.

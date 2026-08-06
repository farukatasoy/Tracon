# Faz 37 — `dotnet new` Proje Şablonu

> **Durum:** 📋 Planlandı (2026-08-06)
> **Kaynak:** [UCUNCU-FAZ-ADAYLARI.md](UCUNCU-FAZ-ADAYLARI.md) · **F-49**
> **Önkoşul:** Yok. Faz 33 (sağlık denetimi) önce biterse şablon onu da taşır
> **Paketler:** yeni — `AgentPrism.Templates`
> **Yeni paket:** **Evet** — gerekçe aşağıda · **Migration:** Yok
> **Public API:** büyümüyor — şablon paketi kod yüzeyi taşımaz

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-007\|K-032\|K-059\|K-185" docs/KARARLAR.md
   ```
   **K-007** (geçişli sabitleme kapalı — yeni paket gerekçesi ister),
   **K-032** (model kataloğu yapılandırmadan gelir — şablon yerleşik model adı
   **yazamaz**), **K-059** (🚨 `secret` dosyaya yazılmaz — şablonun ürettiği
   `appsettings.json` boş placeholder taşır), **K-185** (meta paket her
   sağlayıcıyı içermez — şablon hangi paketleri referans verecek?).
3. [`00-ALTYAPI.md`](00-ALTYAPI.md) — paketleme kuralları ve `Directory.Build.props`
   düzeni. Şablon paketi bu kuralların **istisnasıdır**; farkı bilmen gerekir.
4. Alan hafızası (bu faz bir alana dokunuyor):
   [`hafiza/build-ve-analyzer.md`](hafiza/build-ve-analyzer.md)
   (MSBuild, csproj, NuGet paketleme, analyzer)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`README.md`](../README.md) — kurulum bölümü. Şablon bu bölümün **çalıştırılabilir
   hâlidir**; ikisi ayrışmamalıdır

---

## Amaç

`samples/AgentPrism.Api` çalışan bir kontrol düzlemidir ama bir **şablon
değildir**. Yeni bir kullanıcı onu kopyalamak, adları değiştirmek ve gereksiz
parçaları silmek zorundadır.

- **F-49** — `dotnet new agentprism-api` → çalışan bir kontrol düzlemi.

Bu, dalganın **ilk on dakikaya** dokunan tek kalemidir. Değeri benimseme
merceğindedir: ilk agent'a kadar geçen süreyi kısaltır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `ls samples/AgentPrism.Api/` | `Program.cs`, `OrderTools.cs`, `EchoModelProvider.cs`, `FileSystemArchiveSink.cs`, `appsettings.json`, `Properties/launchSettings.json` — çalışan bir uygulama |
| `find . -name ".template.config" -not -path "*/node_modules/*"` | **Hiç sonuç yok.** Şablon altyapısı yoktur |
| [`AgentPrism.slnx:20-22`](../AgentPrism.slnx) | `samples/` klasöründe tek proje var; şablon projesi yoktur |
| `ls src/` | On beş kaynak paketi; hiçbiri şablon paketi değildir |

> Kanıtlar 2026-08-06 tarihinde doğrulandı.

---

## 37.1 — Yeni paket gerekçesi (K-007)

`AgentPrism.Templates` bir NuGet paketidir ama **bir kütüphane değildir**.
Ayrımı bilmek gerekir:

| Normal AgentPrism paketi | Şablon paketi |
|---|---|
| `PackageType` yok (varsayılan kütüphane) | `<PackageType>Template</PackageType>` |
| Derlenir, `dll` üretir | **Derlenmez**; yalnız içerik taşır |
| Bağımlılık grafiğine girer | 🚨 **Hiçbir bağımlılığı yoktur** ve tüketicinin grafiğine **girmez** |
| Meta pakete girebilir | **Meta pakete asla girmez** |
| Public API takibine tabidir | Public API taşımaz |

**Gerekçe:** Şablon paketi tüketicinin bağımlılık grafiğini kirletmez, çünkü
grafiğe hiç girmez. `dotnet new install` ile makineye kurulur, `dotnet new` ile
kullanılır ve üretilen proje ona **referans vermez**. K-007'nin endişesi
(geçişli ağırlık) burada sıfırdır.

Bu, `faz-tamamlama`'daki paket kontrol listesinin de bir kısmını **atlar**:
`DependencyDirectionTests` şablon paketini kapsamaz, çünkü bağımlılık yönü
yoktur. Kontrol listesinin geri kalanı (README, slnx, sürümleme) geçerlidir.

## 37.2 — Şablon neyi üretir

```mermaid
flowchart TD
    A["dotnet new agentprism-api -n Benim.Agent"] --> B["Program.cs<br/>AddAgentPrism + MapAgentPrism"]
    A --> C["Tools/OrderTools.cs<br/>ornek tool - K2: tool YALNIZ kodda"]
    A --> D["appsettings.json<br/>BOS placeholder - K-059"]
    A --> E["README.md<br/>user-secrets adimlari"]
    A --> F[".gitignore"]

    B --> G{"secenekler"}
    G --> H["--persistence postgres|sqlite|sqlserver|memory"]
    G --> I["--provider openai|anthropic|google|azure"]
    G --> J["--ui true|false"]
```

### 🚨 Şablon `secret` yazmaz

Üretilen `appsettings.json` **boş placeholder** taşır. Gerçek değer yalnız
`dotnet user-secrets` içinde yaşar ve üretilen `README.md` bunu ilk adım olarak
anlatır.

Bir şablonun ürettiği dosyaya örnek bir API anahtarı koymak, o anahtarın
binlerce depoda commit edilmesine yol açar. Bu, K-059'un ruhunun doğrudan
ihlalidir.

### 🚨 Şablon model adı yazmaz

K-032 model kataloğunun yapılandırmadan geldiğini, kodda yerleşik liste
olmadığını kararlaştırdı. Şablon bir model adı **sabitlemez**; yapılandırmada
adlandırılmış bir placeholder ve README'de "bugünkü model adını sağlayıcının
belgesinden alın" yönergesi taşır.

Model adları NuGet yayın hızından hızlı değişir. Şablona gömülen bir ad,
şablonun bayatlamasının **en hızlı** yoludur.

## 37.3 — 🚨 Şablon bayatlar — CI kapısı zorunludur

Bu kalemin tek gerçek riski budur. Şablon, kütüphane değiştiğinde sessizce
kırılır: derleme kapıları onu kapsamaz, çünkü üretilen proje depoda yoktur.

Kapı şudur:

```bash
dotnet new install ./src/AgentPrism.Templates
dotnet new agentprism-api -n Sablon.Denemesi -o "$TMP/sablon"
dotnet build "$TMP/sablon" -c Release      # sifir uyari
```

Bu adım **her seçenek birleşimi için** değil, en az iki birleşim için koşar:
en yalın (`memory` + arayüzsüz) ve en dolu (`postgres` + arayüz). Tüm
birleşimleri koşmak faz kapanışını yavaşlatır; iki uç nokta kırılmayı yakalar.

> **Beşinci bir kapı mı?** Hayır. Bu, dört kapının **içine** girmez; şablon
> projesinin kendi testidir ve `dotnet test` tarafından bir test olarak
> koşulur. Dört kapı disiplini değişmez.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

Kod yüzeyi **yoktur**. Şablonun sözleşmesi `template.json` dosyasıdır:

```jsonc
{
  "identity": "AgentPrism.Api.CSharp",
  "shortName": "agentprism-api",
  "name": "AgentPrism control plane (ASP.NET Core)",
  "sourceName": "AgentPrism.Starter",
  "tags": { "language": "C#", "type": "project" },
  "symbols": {
    "persistence": {
      "type": "parameter",
      "datatype": "choice",
      "choices": [
        { "choice": "memory",    "description": "Veritabani yok" },
        { "choice": "postgres",  "description": "PostgreSQL" },
        { "choice": "sqlite",    "description": "SQLite (tek dosya)" },
        { "choice": "sqlserver", "description": "SQL Server / Azure SQL" }
      ],
      "defaultValue": "memory"
    },
    "provider": {
      "type": "parameter",
      "datatype": "choice",
      "choices": [
        { "choice": "openai",    "description": "OpenAI" },
        { "choice": "anthropic", "description": "Anthropic (Claude)" },
        { "choice": "google",    "description": "Google Gemini" },
        { "choice": "azure",     "description": "Azure OpenAI" }
      ],
      "defaultValue": "openai"
    },
    "ui": {
      "type": "parameter",
      "datatype": "bool",
      "defaultValue": "true",
      "description": "Gomulu kontrol duzlemi arayuzunu dahil et"
    }
  }
}
```

**Varsayılan `memory` seçilmesinin gerekçesi:** `dotnet new` sonrası
`dotnet run` **hiçbir kurulum olmadan** çalışmalıdır. Varsayılan PostgreSQL
olsaydı ilk deneyim bir bağlantı hatası olurdu.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

**Yok.** Şablon mevcut arayüz paketini referans verir; yeni arayüz kodu
üretmez.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Templates/
├── AgentPrism.Templates.csproj
└── content/
    └── AgentPrism.Starter/
        ├── .template.config/
        │   ├── template.json
        │   └── dotnetcli.host.json
        ├── AgentPrism.Starter.csproj
        ├── Program.cs
        ├── Tools/OrderTools.cs
        ├── appsettings.json
        ├── appsettings.Development.json
        ├── .gitignore
        └── README.md

tests/AgentPrism.Templates.Tests/
├── AgentPrism.Templates.Tests.csproj
└── TemplateInstantiationTests.cs
```

Ek olarak güncellenecekler: [`AgentPrism.slnx`](../AgentPrism.slnx) (`src/` ve
`tests/` klasörlerine birer satır), [`README.md`](../README.md) (kurulum
bölümüne şablon komutu).

---

## Testler

| Test sınıfı | Neyi doğrular |
|---|---|
| `TemplateInstantiationTests` | 🚨 Şablon kurulur, en yalın ve en dolu birleşim üretilir, ikisi de **sıfır uyarıyla derlenir** |
| `TemplateSecretTests` | 🚨 Üretilen hiçbir dosyada gerçek görünümlü bir anahtar yoktur; `appsettings.json` yalnız boş placeholder taşır |
| `TemplateModelNameTests` | Üretilen kodda sabitlenmiş bir model adı yoktur (K-032) |
| `TemplateRunTests` | `memory` birleşimi `dotnet run` ile ayağa kalkar ve `GET /agentprism/api/agents` `200` döner |
| `TemplateRenameTests` | `-n Benim.Agent` ile üretilen projede `AgentPrism.Starter` dizesi hiçbir dosyada kalmaz |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular. Bloklayan
> sorular plan yazılmadan **önce** sorulur.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `samples/AgentPrism.Api` şablonla birleşsin mi? | A: ayrı kalsın · B: şablon örneğin yerini alsın | **A.** Örnek uygulama dört kapının ve her fazın gerçek doğrulama zeminidir (`MEMORY.md`: hatalar **yalnız** orada çıktı). Şablon sadeleştirilmiş bir başlangıçtır; ikisi farklı işlerdir |
| 2 | Şablon sürümü kütüphaneyle birlikte mi artar? | A: evet, aynı sürüm · B: bağımsız | **A.** Şablon `x.y.z` sürümlü paketleri referans verir; sürümleri ayırmak hangi şablonun hangi kütüphaneyle çalıştığını belirsizleştirir |
| 3 | Örnek tool şablona girsin mi? | A: evet, tek basit tool · B: hayır, boş başlasın | **A.** K2 gereği tool yalnız kodda tanımlanır; bunu **gösteren** bir örnek, kuralın en iyi belgesidir |
| 4 | Kaç seçenek birleşimi CI'da derlenir? | A: iki uç nokta · B: on altısı da | **A.** On altı birleşim faz kapanışını yavaşlatır; iki uç nokta kırılmayı yakalar |

---

## Bitiş Ölçütleri (DoD)

- [ ] `dotnet new install ./src/AgentPrism.Templates` başarılı
- [ ] `dotnet new agentprism-api -n Benim.Agent` çalışır ve üretilen projede
      `AgentPrism.Starter` dizesi **hiçbir dosyada kalmaz**
- [ ] Varsayılan (`memory`) birleşim **hiçbir kurulum olmadan** `dotnet run`
      ile ayağa kalkar; `GET /agentprism/api/agents` `200` döner
- [ ] `--persistence postgres --ui true` birleşimi sıfır uyarıyla derlenir
- [ ] 🚨 Üretilen `appsettings.json` yalnız boş placeholder taşır; **üretilen
      projede** `secret` taraması boş döner
- [ ] Üretilen kodda sabitlenmiş model adı yoktur (K-032)
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `secret` taraması boş döndü (depo geneli — `faz-tamamlama` komutu)
- [ ] `README.md` kurulum bölümü şablon komutunu içerir
- [ ] `AgentPrism.slnx` iki yeni projeyi taşır

### Doğrulama komutları

```bash
# Sablonu kur
dotnet new install ./src/AgentPrism.Templates

# En yalin birlesim
TMP=$(mktemp -d)
dotnet new agentprism-api -n Benim.Agent -o "$TMP/yalin" --persistence memory --ui false
dotnet build "$TMP/yalin" -c Release

# Yeniden adlandirma tam mi — cikti BOS olmali
grep -rn "AgentPrism.Starter" "$TMP/yalin" && echo "ADLANDIRMA EKSIK" || echo "temiz"

# 🚨 secret taramasi — cikti BOS olmali
grep -rniE "sk-[a-z0-9]{20}|api[_-]?key\"\s*:\s*\"[^\"]+\"" "$TMP/yalin" \
  && echo "SECRET VAR" || echo "temiz"

# Calisiyor mu
(cd "$TMP/yalin" && dotnet run &) && sleep 8
curl -s -i http://localhost:5081/agentprism/api/agents | head -1

# En dolu birlesim
dotnet new agentprism-api -n Benim.Agent2 -o "$TMP/dolu" --persistence postgres --ui true
dotnet build "$TMP/dolu" -c Release
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 Şablon bayatlar; kütüphane değişince sessizce kırılır | `TemplateInstantiationTests` her `dotnet test` koşusunda iki birleşimi gerçekten derler |
| 🚨 Şablon `secret` içeren bir dosya üretir | Yalnız boş placeholder; `TemplateSecretTests` ve DoD taraması |
| Gömülü model adı bayatlar | K-032 gereği ad sabitlenmez; test bunu doğrular |
| Şablon paketi meta pakete sızar | `PackageType=Template`; meta paket referansı **eklenmez**, kontrol listesinde açık madde |
| Yeni paket bakım yükü üretir | Şablon paketi bağımlılık taşımaz ve public API'si yoktur; yük yalnız içerik tazeliğidir ve testle kapatılır |
| Varsayılan seçenek ilk deneyimi kırar | Varsayılan `memory`; kurulum gerektirmez |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır; plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

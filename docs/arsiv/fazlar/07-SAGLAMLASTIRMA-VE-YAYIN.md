# Faz 7 — Sağlamlaştırma ve Yayın

> **Durum:** ⏸ **Beklemede — sıradan çıkarıldı.** Kullanıcı yayın zamanını henüz
> belirlemedi (karar **K-068**, 2026-08-02). Bu faz **her an** araya girebilir;
> diğer fazlar onu beklemez. Sıradaki faz
> [08-SAGLAYICI-GENISLEMESI.md](08-SAGLAYICI-GENISLEMESI.md)'dir.
> **Önkoşul:** [06-GOZLEMLENEBILIRLIK.md](06-GOZLEMLENEBILIRLIK.md) — tamamlandı
> **Sonraki:** Yok — bu faz 1.0 yayınını kapatır
>
> ⚠️ 🚨 **Bu not 2026-08-18'de koda göre düzeltildi.**
> `EnablePublicApiTracking` artık **`true`**'dur
> ([`Directory.Build.props:58`](../../../Directory.Build.props), K-421, Faz 60):
> takip yayın kararından **bağımsız** olarak açıldı ve kayıtsız bir yüzey
> değişikliği derlemeyi kırar. Ama `PublicAPI.Shipped.txt` dosyalarının tamamı
> hâlâ **boştur** (ölçüldü: 1 satır); tüm yüzey `Unshipped` içindedir. Bu faz
> o dosyaları dolduracak ve **o andan sonra** her kırıcı değişiklik bir sürüm
> kararı olacaktır. Yayın geciktikçe ilk dolum büyür; her faz dokümanının
> "Gerçekleşen Public API" bölümü o dolumun kaynağıdır.
> İkinci faz planı: [arsiv/IKINCI-FAZ-YOL-HARITASI.md](../IKINCI-FAZ-YOL-HARITASI.md).

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 7f1833e:docs/arsiv/fazlar/07-SAGLAMLASTIRMA-VE-YAYIN.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Devraldığınız Durum

> ⚠️ Aşağıdaki tablo **Faz 6 sonundaki** durumdur. Bu faz beklemeye alındığı için
> araya Faz 8+ girebilir; her yeni faz paket sayısını, test sayısını, migration
> sayısını ve bundle ölçüsünü değiştirir. Faz 7'ye başlarken güncel değerleri
> son tamamlanan fazın dokümanından okuyun.

| Ne | Durum |
|----|-------|
| Paket sayısı | **8** (`Abstractions`, `Core`, `PostgreSql`, `OpenAI`, **`Mcp`**, `AspNetCore`, `UI`, meta) |
| Test | 383 .NET + 40 Vitest, tamamı yeşil |
| Migration | `0001_initial`, `0002_observability` |
| Bundle | 92,4 / 250 KB gzip |
| Doğrulama kapıları | Dördü de sıfır uyarı |

**Faz 6'da eklenen ve Faz 7'yi doğrudan etkileyen şeyler:**

- `AgentPrism.Mcp` yeni bir **yayınlanabilir pakettir**; ikon, README, sürüm
  politikası ve yayın zinciri onu da kapsamalıdır. `AgentPrismAotCompatible` **false**.
- Public API yüzeyi ciddi büyüdü — `PublicAPI.Shipped.txt` dosyaları faz 5 sonuna
  göre belirgin biçimde uzun olacaktır. Tam liste faz 6 dokümanının
  "Gerçekleşen Public API" bölümündedir.
- `run_events` partition kararı **bu fazın yük testine bağlandı** (K-063).

---

## Amaç

Paketi gerçekten yayınlanabilir hâle getirmek. Faz 6 sonunda AgentPrism çalışır ve işletilebilir; bu faz sonunda **başkalarının güvenle bağımlı olabileceği** bir paket olur. ---

## Bitiş Ölçütleri (DoD)

- [ ] `EnablePublicApiTracking=true`, tüm `PublicAPI.Shipped.txt` dolu
- [ ] Paket doğrulama açık, TFM ve geriye uyum denetimleri geçiyor
- [ ] Paket ikonu tüm paketlerde
- [ ] Depo adresi gerçek değeriyle
- [ ] Tüm test katmanları CI'da geçiyor
- [ ] Benchmark sonuçları kayıtlı
- [ ] XML doküman kapsamı tam
- [ ] `v1.0.0-preview.1` etiketi NuGet.org'a yayınlanıyor
- [ ] Temiz bir makinede `dotnet add package AgentPrism` → örnek çalışıyor

### Son doğrulama

```bash
# Temiz makine simülasyonu
dotnet new web -o /tmp/agentprism-smoke
cd /tmp/agentprism-smoke
dotnet add package AgentPrism --prerelease
# Program.cs'e iki satır eklenir, uygulama çalıştırılır
# http://localhost:5xxx/agentprism açılır
```

---

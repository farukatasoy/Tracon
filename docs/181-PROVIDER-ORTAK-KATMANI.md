# Faz 181 — Provider Ortak Katmanı

> **Durum:** 📋 Planlandı (2026-09-22)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı; mimari seçim: shared-source, paket yok)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-258**
> **Önkoşul:** Yok
> **Paketler:** `Tracon.OpenAI`, `.Anthropic`, `.Google`, `.Azure` (+ yeni shared-source ağacı)
> **Yeni paket:** **Yok** — `src/Tracon.Providers.Shared/` bir paket değildir; `Tracon.Sql.Shared` emsali ([Tracon.SqlServer.csproj:52](../src/Tracon.SqlServer/Tracon.SqlServer.csproj)) gibi `<Compile Include>` ile derlemeye kopyalanır
> **Migration:** Yok
> **Public API:** Büyümüyor — kural: ortak kod `internal` yardımcı + kompozisyon; public hiyerarşi değişmez
> **Tüketici yüzeyi:** Yok (davranış birebir korunur) · sevk edilen: Yok
> **Manuel test alanı:** `docs/manuel-test/05-SAGLAYICI-OPENAI.md` · `06-SAGLAYICI-DIGER.md` — mevcut case'ler regresyon görevi görür

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır.

1. Bu doküman
2. Kararlar — yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-007\|K-008\|K-483\|K-646" docs/KARARLAR.md
   ```
   **K-007** (yeni bağımlılık gerekçe ister — bu faz sıfır bağımlılık ekler),
   **K-008** (ön sürüm MAF yalnız AspNetCore'da — provider paketlerine sızmamalı),
   **K-483** (elle tekrarlanan ifade sessiz kusur sınıfı üretir — bu fazın varlık sebebi),
   **K-646** (dört adapter'da birden düzeltilen BYOK-cache kusuru — kopyanın ölçülmüş bedeli)
3. Alan hafızası: [`hafiza/openai-saglayici.md`](hafiza/openai-saglayici.md) ·
   [`hafiza/icerik-koruma-ve-saglayici-kayit.md`](hafiza/icerik-koruma-ve-saglayici-kayit.md)
4. Emsal: [`src/Tracon.SqlServer/Tracon.SqlServer.csproj`](../src/Tracon.SqlServer/Tracon.SqlServer.csproj)
   satır 50–60 — shared-source mekanizması ve gerekçesi

---

## Amaç

Dört sağlayıcı paketi aynı gövdeyi elle kopyalıyor. K-646 bu kopyanın bedelini
ölçtü: bir BYOK-cache kusuru **dört yerde ayrı ayrı** düzeltildi. K-483 sınıfı
(elle tekrarlanan ifadeye terim eklemek) burada dört kat geçerli. Bu faz ortak
gövdeyi `Tracon.Sql.Shared` emsalindeki gibi tek shared-source ağacına indirir;
davranış birebir korunur, public yüzey değişmez.

- **F-258** — provider ortak katmanı: health check gövdesi, model provider
  iskeleti, kayıt (extensions) kalıbı tek kaynağa iner.

### Bugün ne çalışmıyor — doğrulanmış kanıt

Ad normalizasyonu sonrası ölçülen fark (2026-09-22, `sed 's/Anthropic/PROV/'`
+ `diff`, Anthropic ↔ Google çifti):

| Dosya çifti | Toplam satır | Farklı satır |
|---|---|---|
| `*ModelProvider.cs` | 199 | **40** (~%80 aynı) |
| `*ProviderExtensions.cs` | 216 | **60** (~%72 aynı) |
| `*ProviderHealthCheck.cs` | 186 | **82** (~%56 aynı; fark auth başlığı + yanıt ayrıştırma) |

Dosya sayıları: OpenAI 19 · Anthropic 9 · Google 12 · Azure 9. Dört pakette de
`GuardFor(Uri?)` (tenant endpoint → guard), `SecretLeakTests`, health check
zaman aşımı/hata eşleme gövdesi satır satır aynı desendir.
[`AnthropicProviderHealthCheck.cs:12`](../src/Tracon.Anthropic/AnthropicProviderHealthCheck.cs)
kopyayı kendisi itiraf eder: "The pattern is identical to
`OpenAIProviderHealthCheck`".

> Kanıtlar 2026-09-22 tarihinde doğrulandı.

---

## 181.1 — Mekanizma: shared-source, paket yok

`src/Tracon.Providers.Shared/` açılır; dört provider csproj'u
`<Compile Include="../Tracon.Providers.Shared/**/*.cs" LinkBase="Shared"/>`
ekler. NuGet'e yeni kimlik **çıkmaz**, tüketicinin bağımlılık grafiği
değişmez, K-007 tartışması doğmaz. `Tracon.Sql.Shared`'ın csproj yorumundaki
gerekçe buraya da kopyalanmaz — oraya bağlanır.

## 181.2 — Kompozisyon kuralı (CS0060 sınırı)

`AnthropicModelProvider` gibi public sınıflar **internal taban sınıftan
türeyemez** (CS0060). Bu yüzden ortak gövde kalıtımla değil kompozisyonla
girer: `internal sealed class ProviderHealthCheckCore` (HTTP çağrısı, zaman
aşımı, hata eşleme; sağlayıcıya özgü kısımlar delege: endpoint kurucu, auth
başlık yazıcı, yanıt ayrıştırıcı) ve `internal static class ModelProviderCore`
(known-model kümesi, `GuardFor` kuralı, configuration diagnostic). Public
sınıflar ince kabuk kalır; adları, tabanları ve üyeleri değişmez.

## 181.3 — Kapsam sınırı

OpenAI'nin fazlası (live/sideband, compatible-provider yolu) ortak katmana
**girmez**; yalnız dört pakette ortak olan gövde taşınır. İlk taşıma health
check + model provider iskeleti + extensions kayıt kalıbıdır; katalog dosyaları
model VERİSİ taşıdığı için (39 satır fark ölçüldü) veri ayrı kalır, yükleme
kalıbı ortaklaşır.

---

## Planlanan Public API

Yok — tüm yeni tipler `internal` ve shared-source'tur. `PublicAPI.Unshipped.txt`
dosyalarında satır oynamaz; oynarsa plan ihlal edilmiştir (kapı: public API
takibi zaten açık, K-421).

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
src/Tracon.Providers.Shared/
├── ProviderHealthCheckCore.cs
├── ModelProviderCore.cs
└── ProviderRegistrationCore.cs
src/Tracon.OpenAI|Anthropic|Google|Azure/
└── (mevcut dosyalar inceltilir; ad değişmez)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Taşıma bir sağlayıcının davranışını değiştirir | Sözleşme | mevcut `*ModelProviderContractTests` + `*ModelProviderCredentialTests` dört pakette — değişmeden yeşil kalmalı |
| Hata metnine secret/adres sızar | Birim | mevcut `SecretLeakTests` ×4 — değişmeden yeşil |
| Auth başlığı yanlış sağlayıcıya gider (delege karışması) | Birim | mevcut health check testleri + `ProviderHealthCheckCore` için yeni birim testleri |
| Tenant endpoint guard kuralı (`GuardFor`) taşınırken gevşer | Birim | `ModelProviderCore` guard kuralı testi — `null` endpoint → guard yok, tenant endpoint → guard var |
| Shared-source iki pakette farklı derleniyor (koşullu sembol) | Derleme | dört paketin `dotnet build`'i; koşullu sembol kullanmak yasak (plan kuralı) |

İptal · eşzamanlılık · boş girdi · başka kiracı · alt sistem hatası: mevcut
sözleşme ve birim testleri bu soruları sağlayıcı başına zaten kapsıyor; faz
davranış eklemediği için yeni soru doğmaz — kapsam "aynı testler yeşil kalır".

---

## Manuel Kabul Case'leri

Yeni case yok; `05-SAGLAYICI-OPENAI` ve `06-SAGLAYICI-DIGER` ailelerinin
mevcut case'leri regresyon görevi görür. Kapanışta bu ailelerden sağlayıcı
başına en az bir smoke (gerçek anahtar gerektirmeyenler) koşulur.

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `ProviderRegistrationCore` kayıt kalıbının kapsamı | A: yalnız options doğrulama + health check kaydı · B: chat factory kurulumu dahil | **A** ile başla — B, OpenAI compatible yolu yüzünden dört pakette simetrik değil; ölçüp karar ver |
| 2 | Katalog yükleme kalıbı bu fazda mı? | A: evet · B: veri/kalıp ayrımı sonraki dilime | **B** — katalog dosyaları en az farklı olanlardır; kazanç düşük, dokunma riski var |

---

## Bitiş Ölçütleri (DoD)

- [ ] `sed`+`diff` ölçümü tekrarlanır ve üç dosya çiftinde farklı-satır sayısı yalnız gerçek sağlayıcı farkını içerir (auth · endpoint · ayrıştırma); ölçüm kapanışa yazılır
- [ ] Dört paketin `PublicAPI.Unshipped.txt` dosyalarında net satır değişimi 0
- [ ] Mevcut sözleşme, credential ve secret-leak testleri **değiştirilmeden** yeşil
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul: sağlayıcı smoke case'leri koşuldu, sonuç belgede
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

### Doğrulama komutları

```bash
for p in OpenAI Anthropic Google Azure; do git diff --stat src/Tracon.$p/PublicAPI.Unshipped.txt; done
python3 scripts/kapi.py test --proje Tracon.Anthropic.UnitTests --sinif "*Contract*"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| "Birebir davranış" iddiası sessizce bozulur | Mevcut testler değiştirilemez (plan kuralı); değiştirme ihtiyacı çıkarsa o bir plandan sapmadır ve gerekçesiyle yazılır |
| Shared-source ağacı zamanla ikinci bir "çöp ortak" olur | Kapsam sınırı 181.3'te; her taşınan dosyanın dört pakette de kullanıcısı olmalı |
| OpenAI'nin fazlası ortak katmanı çarpıtır | OpenAI fazlası kapsam dışı; ortak katman "dördünde ortak" tanımıyla sınırlı |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.

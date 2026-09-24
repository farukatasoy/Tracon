# On Bir Mercek

> `nuget-danismani` Adım 5'in kaynak dosyasıdır. Skill **hangi merceğin ne
> zaman koştuğunu** söyler; bu dosya her merceğin **neyi aradığını** söyler.
> Komutlar burada değil, [`kanit-komutlari.md`](kanit-komutlari.md)'dedir.
>
> Her mercek bağımsız koşar. Nokta danışmanlığında yalnız ilgili olanı aç.
> Merceklerin emsalleri bu depoda ölçülmüş vakalardır; kural değil, **hipotez**
> kaynağıdır — her turda yeniden ölç.

| # | Mercek | Tek soru |
|---|---|---|
| 1 | Public API freeze | Bu yüzey 1.x boyunca taşınmaya değer mi? |
| 2 | SemVer ve compatibility | Bu değişiklik tüketiciye neye mal olur? |
| 3 | Güvenlik sınırı | Ham veri hangi çıkışa kadar ulaşıyor? |
| 4 | Capability tasarımı | Desteklenmeyen yetenek sessizce mi düşüyor? |
| 5 | Timeout ≠ cancellation | `Timeout` adı sonsuz beklemeyi gerçekten kesiyor mu? |
| 6 | Exception taxonomy | Public hata sözleşmesi diagnostics'ten ayrı mı? |
| 7 | Serialization | Guard, persistence ve wire aynı temsili mi görüyor? |
| 8 | Upstream bağımlılık riski | Tracon'un sözü bağımlılıklarının sözünden güçlü mü? |
| 9 | Tedarik zinciri ve güven | Tüketici artifact'in kaynağına ve güvenliğine nasıl güvenir? |
| 10 | Yaşam döngüsü ve destek | Söz ne kadar sürer, nasıl biter? |
| 11 | Benimsenme ve ilk deneyim | Hiçbir şey bilmeyen biri ilk `run`'a ulaşıyor mu? |

---

## 1 — Public API freeze

`Shipped` baseline'a girmemiş bir API'yi **sırf yazılmış olduğu için koruma.**
`PublicAPI.Shipped.txt` preview hattı boyunca **boştur** (K-603) — bugün
yüzeyi küçültmek ucuzdur, GA'dan sonra kırıcıdır. YAGNI uygula.

Ara: kullanılmayan public tip · yalnız implementation detayı olan public
helper · gelecekte büyüyecek public `enum` · yanlış arayüze konmuş capability ·
`null` parametresiyle iki anlam taşıyan API · convenience için açılmış
implementation wrapper · concrete Core tipine gereksiz bağımlılık · registration
API'si olmayan extension point · anlamı belirsiz duplicate registration ·
uygulanmayan public options alanı · XML sözü ile çelişen runtime · DI'ın
kurduğu tipte public kurucu (K-866) · opsiyonel parametre varsayılanı (çağıranın
ikilisine derlenir, K-867).

GA'da ek soru: **Her public tipin bir dış kanıtı var mı?** Kanıtsız kalan
tipin gerekçesi `scripts/public-yuzey-gerekceleri.tsv`'de yazılı mı (K-850)?
Yüzeyin tamamını dondurmak zorunlu değildir — olgun olmayan kısım
Mercek 10'daki olgunluk katmanına
alınabilir.

Emsal: Faz 96 yaprak olup başka public imzada geçmeyen **96 tipi** `internal`
yaptı (K-601); Faz 182 dış kanıt ölçütüyle 91 tipi daha (K-850). Faz 103
kullanılmayan `TraconJudgeException`'ı kaldırdı.

## 2 — SemVer ve compatibility

Her değişikliği sınıflandır: `patch-safe` · `minor/additive` · `source-breaking`
· `binary-breaking` · `behavioral-breaking` · `wire/protocol-breaking` ·
`serialization-breaking` · `database/migration-breaking` · `telemetry-breaking`
· `configuration-breaking` · `AOT/trimming-breaking`.

Sınıf **yüzeye** göre değişir; on dört yüzeyin her biri kendi kuralını taşır:
[`sozlesme-yuzeyleri.md`](sozlesme-yuzeyleri.md).

Preview'da bile **tüketici maliyetini** yaz. Stable sonrası yapılamayacakları
ayrıca işaretle. Migration'lar immutable'dır — uygulanmış bir migration
düzenlenmez, yenisi eklenir.

Kapı: `kapi.py yayin` son `v*` etiketine karşı ApiCompat taban doğrulaması
koşar ve her kırılmanın sürüm notunda **adıyla** geçmesini ister (K-864). Bu
preview kuralıdır — 1.x minor'unda kırılma **notla geçmez, durur**. GA'da
kapının bu moda geçtiğini ölç ([`ga-olcutleri.md`](ga-olcutleri.md)).

## 3 — Güvenlik sınırı — uçtan uca izle

"Endpoint'te maskeleniyor" bir kanıt **değildir**. Ham veriyi kaynağından
başlayıp **her** ara katmandan geçir: runtime boru hattı · fallback · retry ·
circuit breaker · `ILogger` · `store` · HTTP · SSE · MCP · OpenAI-uyumlu uçlar
· kalıcı `run`/`tool`/`error` kayıtları · audit izi · telemetri etiketi.

Ara: `exception` mesajı sızıntısı · `secret`/API anahtarı · connection string ·
kiracı yalıtımı · BYOK credential semantiği · global credential'a sessiz düşme ·
egress policy · content guard · tool sonucu normalizasyonu · yetkilendirme ·
platform yetkisi (K-853) · onay · timeout · çıktı sınırı · script grant'ı
(K-860) · kimlik başlığı saklama (K-868).

Normalizasyon **public boundary'ye yakın** ama classifier/fallback bilgisini
kaybetmeyecek yerde olmalıdır. `secret` veritabanına da yazılmaz (K-059).

Emsal: ham exception metni 26 sitede sızıyordu; sınıf `SafeErrorText` ve
mimari cırcır kapısıyla kapandı (Faz 119, K-640).

## 4 — Capability tasarımı

Bir implementasyon her capability'yi desteklemiyorsa seçenekleri **karşılaştır**:
nullable parametre · boolean property · marker interface · opt-in interface ·
capability object · registration metadata.

**Optional bir capability sessiz fallback ile geçilmez.** Kiracı BYOK
credential'ı verilmişken provider bunu desteklemiyorsa setup/global
credential'a düşmek bir güvenlik kusurudur; `fail-closed` + stable bir hata
kodu doğru davranıştır (Faz 103, `provider_credential_unsupported`).

Capability sözleşmesi dört şeyi birden olmalıdır: **discoverable · testable ·
documented · runtime-enforced.**

## 5 — Timeout ≠ cancellation

Her async sınırda sor: caller cancellation nedir? · internal timeout var mı? ·
cooperative mi, gerçek **wait cutoff** mı? · gövde token'ı yok sayarsa ne olur?
· timeout sonrası task yaşamaya devam ediyor mu? · geç tamamlanma/fault
**observe** ediliyor mu? · `OperationCanceledException`'ın kaynağı (caller mı
host mu timeout mu) ayırt ediliyor mu? · retry edilebilir mi?

Bir options alanının adı `Timeout` ise **sonsuz beklemeyi gerçekten kestiğini
ölç.** Linked `CancellationToken` üretmek timeout garantisi değildir.

Emsal: tool zaman aşımı K-805'e kadar hiçbir şeyi iptal etmiyordu;
`OperationCancellation.IsFailure` kuralı tek yerde yazıldı (K-840).

## 6 — Exception taxonomy ve public hata sözleşmesi

Her seam için: hangi exception tipi public olabilir? · yabancı exception ne
olur? · ham `Exception.Message` dışarı çıkar mı? · stable `ErrorType`/kod var
mı? · `InnerException` korunuyor mu? · log tam detayı taşıyor mu? ·
HTTP/SSE/MCP kullanıcıya **generic güvenli** mesaj mı veriyor? · framework'ün
kendi bilinen exception alt tipleri korunuyor mu? · kullanılmayan public
exception tipi var mı?

Public hata sözleşmesi ile diagnostics birbirinden ayrıdır. İkisini aynı
string'ten beslemek sızıntı üretir. **Hata kodu kümesi de bir sözleşme
yüzeyidir:** 1.0'dan sonra bir kodun anlamı değişmez, yalnız yeni kod eklenir.

## 7 — Serialization / canonical representation

Tool, plugin veya sonuç taşıyan sistemlerde her tipin **çalışma anı temsilini
ölç**: `string` · `JsonElement` · primitive · `enum` · collection · record/class
· `null` · binary/`AIContent`/protokole özgü değerler.

Content guard, truncation, persistence ve wire çıktısı **aynı canonical
representation**'dan beslenmelidir; farklı beslenirlerse guard'ın gördüğü ile
tüketicinin gördüğü ayrışır (K-798, K-799).

AOT'u bozan reflection serializer **varsayılan çözüm değildir**: kaynak üretimli
`JsonSerializerContext`/`JsonTypeInfo` yollarını değerlendir. Normalize
edilemeyen hassas veri için `fail-open` değil **`fail-closed`** seç.

Struct alanı atanmazsa `default` kalır ve seri hâle getirme **liste ucunun
tamamını** çökertir (MEMORY.md) — yeni kayıt üreten her yolu ölç.

## 8 — Upstream bağımlılık riski

**Tracon'un 1.x sözü, bağımlı olduğu paketlerin sözünden güçlü olamaz.**
Tracon MAF tiplerini doğrudan taşır (AGENTS.md: "MAF tiplerini sarmalama");
bu bir tasarım kararıdır ve bedeli şudur: upstream'in kırıcı değişikliği
Tracon'un kırıcı değişikliğidir. Bedeli ölç, varsayma.

Ara:

- **Ön sürüm doğrudan bağımlılık.** Kararlı pack'i `NU5104` durdurur (K-008,
  K-602). Beş paketin hangisi GA oldu? Canlı registry'den oku, ezberden değil.
- **Ön sürüm bağımlılığın alt sınır aralığı.** Tracon'un nuspec'i
  `>= 1.20.0-preview.X` yazar; tüketici upstream'in daha yeni preview'ını
  alırsa NuGet onu sessizce birleştirir. Upstream preview'lar birbirini kırar
  → kırılma **restore'da değil çalışma anında** görünür. Seçenekler: tam aralık
  (`[x]`, kırılma restore'a taşınır) · üst sınırlı aralık · kabul + doküman.
  Ölçüm: `uyum-probu.cs ileri`.
  *Ölçüldü (2026-09-24):* `Tracon.AspNetCore 1.0.0-preview.2` + MAF Hosting
  `1.22.0-preview.260918.1` → `Microsoft.Agents.AI.Hosting.AgentSessionStore`
  yok (Abstractions'a taşındı, forwarder yok) ve
  `AgentRunMode.DisallowBackground` yok. GA paketleri (MAF `1.22.0`, MEAI
  `10.10.0`) temiz: 1.170 üye referansında 0 eksik.
- **Upstream `[Experimental]` tipi public imzada.** Deneysel tip minor
  sürümde değişebilir; onu taşıyan Tracon üyesi kararlı olamaz. Seçenekler:
  Tracon üyesini kendi `[Experimental]` kimliğiyle işaretle · yüzeyden çıkar
  (upstream GA olana kadar) · kabul et ve riski yaz. Ölçüm:
  `uyum-probu.cs deneysel`. *Ölçüldü (2026-09-24):* tek maruziyet
  `TraconBuilderExtensions.AddLoopEvaluator(… LoopEvaluator)` (`MAAI001`).
- **Deneysel upstream kullanımı içeride.** `#pragma warning disable MAAI001`
  gibi her bastırma, yayınlanmış ikilinin upstream minor'ıyla kırılabileceği
  bir noktadır. Sayı tek başına bulgu değildir; **eğri** risktir. İleri probu
  periyodik koş.
- **Birlikte yükselmesi gereken küme.** MAF ↔ MEAI ↔ `Microsoft.Extensions.*`
  birbirine alt sınırla bağlıdır. Bağımlılık botu bu kümeyi ayrı PR'lara
  bölerse her PR tek başına `restore`'da kırmızı olur ve drift kapısı fiilen
  kapanır. *Ölçüldü (2026-09-21):* MAF, MEAI ve platform PR'ları ayrı ayrı
  "Bagimliliklari geri yukle" adımında kırmızı; MEAI `10.10.0`
  `Microsoft.Extensions.* 10.0.12` ister, pin `10.0.11`.
- **Upstream major sınırı.** Public imzada geçen upstream tip sayısı
  (`AIAgent` 33 kez, `AIFunction` 12, `IChatClient` 11) Tracon'un upstream
  major'ına bağlanma yüzeyidir. MAF 2.0 geldiğinde Tracon 2.0 mı? Politika
  GA'dan **önce** yazılır.
- **Test/contract paketinin framework bağı.** `Tracon.Testing.Contracts.Xunit`
  `xunit.v3 3.2.2` alt sınırı taşır; `xunit.v3 4.x` kararlı yayında. Tüketici
  4.x'e geçerse ne olur? İkili probu metadata uyumunu ölçer; çalışma anı
  (test platformu v2) ayrıca ölçülür.
- **CVE zorlamaları.** `K-007` deseniyle zorlanan geçişli sürümler GA'da hâlâ
  gerekli mi, yoksa upstream düzeltti mi?

## 9 — Tedarik zinciri ve güven

Kurumsal bir tüketici paketi kurmadan önce dört şeyi sorar: **kim yayınladı,
neyden üretildi, içinde ne var, açık bulunursa nasıl haber alırım?** Her soru
için ölçülmüş bir cevap olmalıdır.

Ara:

- **Yayın kimliği.** NuGet: OIDC trusted publishing (KN-022) — uzun ömürlü
  anahtar yok. npm: kalıcı `NPM_TOKEN` secret'ı. npm granular write token'ın
  ömrü en fazla 90 gündür; süre dolunca yayın işi kırmızı olur. Hedef: npm
  trusted publishing (OIDC) — token'sız ve provenance'lı.
- **Provenance / attestation.** `.nupkg` için GitHub build provenance
  attestation'ı; npm için `--provenance` (önkoşul: `package.json`
  `repository` alanı, A-43). Tüketici `gh attestation verify` ile hangi
  commit'ten ve workflow'dan üretildiğini doğrular.
- **SBOM.** Paketin içindeki ve arkasındaki bileşen listesi (SPDX veya
  CycloneDX). Kurumsal satın alma bunu sorar; yokluğu bir 🟡 satın alma
  engelidir, runtime kusuru değildir.
- **İmza.** nuget.org her pakete depo imzası koyar. Yazar imzası sertifika
  maliyeti getirir — ⚪ gerçek talep çıkana kadar.
- **Tekrar üretilebilir derleme.** `Deterministic` + `ContinuousIntegrationBuild`
  bir iddiadır; bağımsız yeniden üretim kanıttır (GA'ya ertelendi, §12 ER).
- **CI sertleştirme.** Action'lar SHA-pinli mi? İş başına en az yetki mi?
  Yayın environment'ı **tag kuralı ve onaylayıcı** ile korunuyor mu? Public
  repo'da bu koruma ücretsiz gelir (OP-011'in yeniden açılma ölçütü).
  Runner/action çalışma zamanı kaldırılmaları (ör. Node 20) izleniyor mu?
- **Zafiyet bildirimi → tüketici uyarısı.** NuGet'in restore anı uyarısı
  (NuGetAudit) **GitHub Advisory Database**'den beslenir. Bir güvenlik
  düzeltmesi yalnız nuget.org deprecation'ı ile duyurulursa `dotnet restore`
  tüketiciyi **uyarmaz**. Public repo'da private vulnerability reporting ve
  yayınlanan GHSA bu hattı kurar. Ölç: `SECURITY.md` kanalı, repo ayarı,
  bir tatbikat advisory'si.
- **Lisans uyumu.** Pakete gömülen üçüncü taraf kod ve varlığın lisans
  bildirimi pakette mi? *Ölçüldü (2026-09-24):* `Tracon.UI` derlemesine gömülü
  748 KB JS paketi (React, React DOM, TanStack Query — MIT) lisans başlığı
  taşımıyor ve pakette `THIRD-PARTY-NOTICES` yok. MIT bildirimin kopyalarla
  birlikte gitmesini ister.
- **Sır hijyeni.** Çalışma ağacı taraması kapıdadır; git geçmişi taraması
  public öncesi yapıldı (RK-014). Push protection açık mı?
- **Süreklilik.** Yayın organizasyonunda ikinci yetkili var mı (BL-057)?
  Tek kişinin erişemediği gün güvenlik yaması çıkabiliyor mu?
- **Dış güven sinyali.** CodeQL / OpenSSF Scorecard gibi ölçümler kurumsal
  incelemede sorulur. Kendi başına blocker değildir; eksikliği 🟢/🟡.

## 10 — Yaşam döngüsü ve destek politikası

1.0 bir sürüm numarası değil, **süresi olan bir sözdür.** Sözün başı (ne
donar), süresi (ne kadar yama) ve sonu (nasıl biter) GA'dan önce yazılır.

Ara:

- **TFM ↔ .NET desteği.** `net8.0` ve `net9.0` 2026-11-10'dan sonraki ilk
  sürümde düşer (K-855). 1.0 hangi TFM kümesiyle çıkar? Yeni bir .NET sürümü
  (ör. `net11.0`) eklemek additive'dir; düşürmek compatibility sayfasına göre
  minor'dır. Tarihleri Microsoft'un politika sayfasından canlı oku.
- **Tracon destek penceresi.** Preview'da "yalnız son sürüm" doğrudur. 1.x
  için: hangi minor yama alır, yeni major çıkınca önceki major kaç ay güvenlik
  yaması alır? `SECURITY.md` tablosu bunu söyler.
- **Deprecation politikası.** `[Obsolete]` → en az bir minor → major'da
  kaldırma. `DiagnosticId` ve `UrlFormat` STJ kaynak üretecinin bastırmasını
  deliyor (K-869) — kural bu sınırla yazılır. Preview'da `1.0.0`'da kalkacağı
  duyurulan üyeler GA'dan önce gerçekten kalkar (F-290).
- **Olgunluk katmanları.** 1.0 her yüzeyi aynı sıkılıkta dondurmak zorunda
  değildir. Tracon'un kendi `[Experimental("…")]` kimliği olgunlaşmamış
  yüzeyi 1.x içinde değiştirilebilir bırakır; tüketici bunu derleyici
  uyarısıyla görür. K-869 emsali yüzünden STJ kaynak üretecine etkisi
  **önce ölçülür**.
- **Doküman sürümleme.** Site `main`'den derlenir ve yayınlanmamış API'yi
  anlatabilir (reference/versioning). 1.x'te tüketici hangi sürümün
  dokümanını okuduğunu bilmelidir: sürümlü site veya "eklendiği sürüm"
  işareti.
- **Servis hattı.** `main` 1.1'e ilerlerken 1.0.x yaması nereden kesilir?
  MinVer etiketten türetir; bakım dalı ve geri taşıma kuralı yazılı mı?

## 11 — Benimsenme ve ilk deneyim

"Milyonlara hitap eden" bir paket, ilk beş dakikada kaybedilir. Doküman
doğru olsa da giriş yolu kırıksa ürün kırıktır.

Ara:

- **Temiz makine yolu.** Boş cache'te `dotnet new install` → `dotnet new
  tracon-api` → `dotnet run` → ilk agent cevabı. Adım sayısı ve süre ölçülür.
  Emsal: A-34 — sevk edilen şablon `NU1102` ile restore olmuyordu.
- **Paket sayfası.** README nuget.org'da nasıl render oluyor (Mermaid
  basılmaz — A-55)? Açıklama bağımlılık gerçeğiyle uyumlu mu (A-52)? Ön ek
  doğrulaması (✅ verified) duruyor mu?
- **Repo yüzü.** Açıklama, konu etiketleri, Discussions, "Latest" release
  rozeti doğru sürümde mi (A-40)?
- **Keşfedilebilir örnek.** Sample'lar giriş noktasından görünüyor mu (A-48)?
- **Sayı taşıyan iddia.** Paket, operasyon, tip sayısı kapıya bağlı mı, elle
  mi tutuluyor (A-45 · A-47 · A-53 · A-54 sınıfı)?
- **Lisans netliği.** Gelir eşiği paket sayfasında görünür mü (A-58)?
- **Destek kanalı.** Hata, soru ve güvenlik bildirimi için üç ayrı ve
  çalışan kanal var mı?

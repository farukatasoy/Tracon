# Faz 39 — `AgentPrism.Testing` Paketi

> **Durum:** ✅ Tamamlandı (2026-08-06)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-46**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Testing` (**yeni**), `AgentPrism.Abstractions`, `.Core`, `.AspNetCore`
> **Yeni paket:** **`AgentPrism.Testing`** — K-007 gerekçesi [39.1](#391--k-007-gerekçesi-neden-ayrı-bir-paket) · **Migration:** Yok
> **Public API:** büyüyor — 🚨 **bir kez doğru yapılmalıdır**; kırılırsa tüketicinin **tüm test paketi** kırılır

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show d20ed6f:docs/arsiv/fazlar/39-TEST-PAKETI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

AgentPrism üzerine agent yazan biri, bugün kendi agent'ını **gerçek model çağırmadan test edemez.** Test etmek için gereken her şey bu depoda **zaten yazılmıştır** — ama test projelerinin içine kilitlidir ve `internal`'dır. Bu faz o kodu birleştirir, tasarlar ve yayımlar. Yeni yetenek yazılmaz; var olan yetenek **kullanılabilir hâle** getirilir.

## Bitiş Ölçütleri (DoD)

- [x] `AgentPrism.Testing` paketlenir; `nuspec` **hiçbir test çerçevesi**
      bağımlılığı taşımaz (`dotnet pack` çıktısı okunarak doğrulanır)
- [x] Meta paket `AgentPrism` bu pakete referans **vermez**
- [x] Beş sahte sağlayıcı kopyasının **hepsi silindi** — 🚨 **bir istisna
      dışında**: `tests/AgentPrism.Core.UnitTests/Fakes/FakeModelProvider.cs`
      bilinçli olarak KORUNDU (K-271, gerekçe "Plandan Sapmalar"da)
- [x] Mevcut test paketlerinin tamamı yeni paketle koşar ve geçer —
      `SqlServer.IntegrationTests` **hariç** (bu ortamda ARM64 Docker'da
      `mssql/server` imajı zaten çalışmıyor, README'de önceden belgeli,
      Faz 39 ile ilgisiz)
- [x] Depo dışından bir tüketici senaryosu çalıştı: `samples/` altında veya
      geçici bir projede, AgentPrism'e bağlı bir agent **gerçek model
      çağırmadan** test edildi; komut ve çıktı bu belgeye yazıldı
- [x] Her iddia hem geçen hem düşen yolda test edildi; düşen yol mesajı
      **beklenen ve bulunan** değeri içeriyor
- [x] `DependencyDirectionTests` yeni paketi tanıyor ve README denetimi geçiyor
- [x] 🚨 K-218 tuzağı paketin README'sinde ve fixture XML dokümanında yazılı
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı bu belgeye yazıldı
- [x] `secret` taraması boş döndü

### Doğrulama komutları — gerçek çıktı

```bash
$ dotnet pack src/AgentPrism.Testing -c Release --no-build
$ unzip -p artifacts/package/release/AgentPrism.Testing.0.0.0-preview.0.41.nupkg \
  AgentPrism.Testing.nuspec | grep -A5 "<dependencies>"
    <dependencies>
      <group targetFramework="net10.0">
        <dependency id="AgentPrism.AspNetCore" version="0.0.0-preview.0.41" exclude="Build,Analyzers" />
        <dependency id="AgentPrism.Core" version="0.0.0-preview.0.41" exclude="Build,Analyzers" />
        <dependency id="Microsoft.AspNetCore.TestHost" version="10.0.10" exclude="Build,Analyzers" />
      </group>
    </dependencies>
# Hicbir test cercevesi (xunit/NUnit/MSTest/Shouldly/FluentAssertions) YOK.

$ grep -c "AgentPrism.Testing" src/AgentPrism/AgentPrism.csproj
0

$ find tests -name "EchoModelProvider.cs" -o -name "ScriptedModelProvider.cs" \
  -o -name "RoutingModelProvider.cs" -o -name "FakeModelProvider.cs"
tests/AgentPrism.Core.UnitTests/Fakes/FakeModelProvider.cs
# Tek sonuc — K-271'in bilincli istisnasi. EchoModelProvider (x2),
# ScriptedModelProvider, RoutingModelProvider SILINDI (boş dönüyorlardı).

$ find src/AgentPrism.Testing -name "* 2.*"
# (bos)

$ grep -rIn -E "sk-[a-z]+-[A-Za-z0-9_-]{24,}|AVNS_[A-Za-z0-9]{12,}|(Password|pwd)=[^ \";']{6,}" . \
  --exclude-dir=.git --exclude-dir=artifacts --exclude-dir=node_modules
# (bos)
```

### Test sayıları (gerçek çalıştırma)

| Proje | Sonuç | Not |
|---|---|---|
| `AgentPrism.Testing.UnitTests` (**yeni**) | 32/32 ✅ | Paketin kendi testleri |
| `AgentPrism.Core.UnitTests` | 576/576 ✅ | `DependencyDirectionTests` dahil |
| `AgentPrism.AspNetCore.FunctionalTests` | 319/319 ✅ | `EchoModelProvider`+`RoutingModelProvider` göçü |
| `AgentPrism.PostgreSql.IntegrationTests` | 505/505 ✅ | Gerçek Postgres container (Testcontainers) |
| `AgentPrism.Sqlite.IntegrationTests` | 255/255 ✅ | |
| `AgentPrism.Ui.E2ETests` (Playwright) | 41/41 ✅ | `ScriptedModelProvider` göçü |
| `AgentPrism.SqlServer.IntegrationTests` | ⚠️ atlandı | Bu makinede `mssql/server` ARM64 Docker'da hazır olmuyor (önceden bilinen ortam kısıtı, bu fazla ilgisiz) |
| `Anthropic`/`Google`/`Azure`/`OpenAI`/`Voice`/`Workflows`/`Mcp`.UnitTests | 331/331 ✅ | Faz 39'a dokunulmadı, regresyon sıfır |
| `AgentPrism.Templates.Tests` | 10/10 ✅ | Bu oturumun ayrı bir düzeltmesiyle ilgili (bkz. altta) |

**Depo dışı tüketici senaryosu** (gerçek çıktı):

```bash
$ mkdir /tmp/faz39-consumer-check && cd /tmp/faz39-consumer-check
# NuGet.config: agentprism-local -> artifacts/package/release
$ dotnet test
Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 848ms - Consumer.dll (net10.0|arm64)
```

Test: `FakeModelProvider().CallsTool("get_order_status", ...).EchoesUserMessage()`
+ `AgentPrismTestHost.RunAsync("support", "ORD-7 nerede?")` →
`ShouldHaveCompleted().ShouldHaveCalledTool(...).ShouldHaveOutputContaining("Echo:")`
— **hiçbir gerçek model çağrılmadı.** Bu adım gerçek bir hata yakaladı: bkz.
"Plandan Sapmalar".

**`samples/AgentPrism.Api` gerçek çalıştırma:**

```bash
$ curl -s http://localhost:5081/agentprism/api/meta
{"version":"0.0.0-preview.0.41","prefix":"/agentprism", ...}
$ curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5081/agentprism/api/agents
200
```

---

## Plandan Sapmalar

**Açık Soru 1 çözüldü — İki `EchoModelProvider` kopyası arasındaki fark:**
`diff` tek bir satır fark gösterdi — **yalnızca namespace bildirimi**
(`AgentPrism.AspNetCore.FunctionalTests.Infrastructure` vs
`AgentPrism.PostgreSql.IntegrationTests.Infrastructure`). Davranış birebir
aynıydı; Seçenek A (diff okunup bilinçli seçim yapılır) mekanik olarak
uygulandı, davranış farkı yoktu.

**Açık Soru 2, 3, 5 plandaki önerilerle aynen kapandı:** `AgentPrismTestHost`
kendi `IHost`'unu kurar (B değil A); `RunAssertions` akışlı çalıştırmaları da
kapsar (`run_events` aynı okunur); paket AOT uyumlu **değildir** (ölçüldü —
`FakeModelProvider.CallsTool` anonim tip özelliklerini yansıma ile okur,
`AgentPrismTestHost` JSON (de)serileştirmesi kaynak üreteci bağlamı taşımaz).
Açık Soru 4 (ses sağlayıcısı) plandaki gibi bu fazın dışında bırakıldı.

**Tasarım sapması — `FakeModelProvider` mesaj-geçmişi tarayan bir "akıllı"
saglayici DEĞİL, saglayicinin KENDİSİNDE tutulan bir kuyruktur:**
`RoutingModelProvider`/`ScriptedModelProvider` hangi tool'un zaten
çağrıldığını mesaj geçmişindeki `FunctionCallContent`/`FunctionResultContent`
çiftlerini korele ederek çıkarıyordu. Bu mantığı genellemek yerine, her model
kendi `Queue<FakeStep>`'ini taşır (`ForModel(id, cfg => ...)`); bir çağrı
sıradaki adımı çöker, kuyruk tükendiğinde `EchoesUserMessage()`/
`EchoesLastToolResult(prefix)` devreye girer. Bu, planın "Planlanan Public
API" taslağında YOKTU (`EchoesLastToolResult` ve `RespondsWith(text, tokens,
tokens)` yeni eklendi) — gerekçe K-269.

**Kaldırılan kopyalar listesi bir istisnayla uygulandı — `tests/AgentPrism.Core.UnitTests/Fakes/FakeModelProvider.cs`
SİLİNMEDİ:** plan bu dosyayı da listeye yazıyordu ama inceleme, 27 çağrı
yerinin 20'den fazlasının **argümanlı bir `IChatClient`** (derleyici/kayıt
zincirinin internallerini beyaz-kutu test eden) geçirdiğini gösterdi — yeni
paketin sıralı-kuyruk tasarımıyla karşılanamayan bir kullanım biçimi.
Gerekçe ve kullanıcıyla görüşme K-271'dedir.

**`RunAssertions.ShouldHaveOutputContaining` planın öngörmediği bir hata
içeriyordu, depo dışı tüketici senaryosu tarafından yakalandı:** ilk
uygulama yalnız `RunEventType.MessageCompleted` olaylarına bakıyordu; bu
olay tipi **yalnız akışsız `agent.RunAsync()` yolunda** yazılır.
`AgentPrismTestHost.RunAsync` HTTP `/run` (SSE, akışlı) kullandığı için
gerçek dünyadaki HER çağrı boş çıktı görüyordu — depo İÇİ testler bunu
kaçırdı çünkü hiçbiri bu iddiayı akışlı bir HTTP çağrısına karşı
çalıştırmıyordu. Düzeltme ve tam ders `docs/hafiza/cekirdek-calistirma.md`
ve `docs/hafiza/test-altyapisi.md`'dedir.

**Templates.Tests hız sorunu (bu fazın kapsamı DIŞINDA ama aynı oturumda
düzeltildi):** kullanıcı "Templates.Tests çok geç bitiyor (~1 saat)" diye
başladı; kök neden `TemplateFixture`'ın tam çözümü paketlemesiydi (13
gereksiz test projesi dahil). Düzeltme (`AgentPrism.src.slnf`) K-268'dedir;
Faz 39'un kendisiyle ilgisi yoktur ama aynı oturumda, Faz 39'a başlamadan
önce yapıldı.

## Bu Fazda Verilen Kararlar

- **K-268** — `TemplateFixture` tam çözüm yerine `AgentPrism.src.slnf` paketler (Templates.Tests hızı, Faz 39 kapsamı dışı)
- **K-269** — `FakeModelProvider` modele özel, bir kez tüketilen kuyruk tutar; `EchoesLastToolResult` eklendi
- **K-270** — `AgentPrism.Testing` yalnız `net10.0` hedefler (`Microsoft.AspNetCore.TestHost` sürüm kısıtı)
- **K-271** — Core.UnitTests'in kendi `FakeModelProvider`'ı silinmedi (27 çağrının çoğu argümanlı `IChatClient` gerektiriyor)

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `AgentPrism.Testing.FakeModelProvider`/`AgentPrismTestHost`/`RunAssertions`
  public API'si Faz 7'den önce yayımlandı; kırılması tüketicinin **tüm test
  paketini** kırır (K-016 aciliyeti burada da geçerli).
- `AgentPrism.Testing`, `AgentPrism.AspNetCore` üzerinden K-008'in ön sürüm
  MAF paketlerini (`Microsoft.Agents.AI.Hosting` preview,
  `.Hosting.OpenAI` alpha) **geçişli olarak** devralır. Test paketi üretim
  bağımlılık grafiğinde olmadığı için kabul edilebilir.
- `AgentPrism.Testing` yalnız `net10.0` hedefler (K-270) — `net8.0`/`net9.0`
  hedefleyen bir tüketici bu paketi kullanamaz.

**Bilinen tuzaklar (🚨):**
- `RunEventType.MessageCompleted` yalnız akışsız `agent.RunAsync()` yolunda
  yazılır; HTTP `/run` (SSE) yalnız `MessageDelta` üretir.
  `RunAssertions.ShouldHaveOutputContaining` ikisini doğru sırayla okur
  (`MessageCompleted` varsa o, yoksa `MessageDelta` birleşimi) — yeni bir
  olay-okuyan özellik eklerken bu ayrım tekrar unutulabilir.
- `AgentPrism.Testing.AgentPrismTestHost` ile
  `AgentPrism.AspNetCore.FunctionalTests.Infrastructure.AgentPrismTestHost`
  (TestServer tabanlı, iç) **aynı adı taşır**; `using AgentPrism.Testing;`
  FunctionalTests dosyalarında `CS0104` verir — `using FakeModelProvider =
  AgentPrism.Testing.FakeModelProvider;` takma adıyla alınmalıdır.
- `Microsoft.AspNetCore.TestHost` merkezi sürümü barındırma framework'üyle
  birebir eşlenir; SDK'nın hedef .NET sürümü değiştiğinde (bugün 10.0.100)
  bu paketin sürümü de güncellenmeli, aksi hâlde `AgentPrism.Testing` eski
  bir TFM'e kilitli kalır.

**Yarım kalan işler / açık uçlar:**
- `FakeModelProvider` "custom responder" kancası **eklenmedi** (K-269'un açık
  notu). Bir tüketici tool-tamamlanma-durumuna göre dinamik dallanma
  isterse (bugün karşılaşılmadı) bu eklenir; o noktada
  `Core.UnitTests/Fakes/FakeModelProvider.cs`'in (K-271) gerçekten
  gereksizleşip gereksizleşmediği yeniden değerlendirilir.
- `AgentPrism.src.slnf` (K-268) yalnız `src/` projelerini listeler; yeni bir
  paket eklendiğinde bu dosyaya da satır eklenmesi gerekir — `dotnet pack
  AgentPrism.slnx` DoD kontrolü paket SAYISINI doğrular ama `.slnf`'in
  güncel olduğunu doğrulamaz (otomatik bir denetim yok).

**Sıradaki faz:** `docs/arsiv/UCUNCU-FAZ-YOL-HARITASI.md`'deki sıraya göre seçilir —
Faz 39 dördüncü öncelikli yeni-pakete-genişleme fazıydı; kalan adaylar
`docs/ADAYLAR.md`'dedir.

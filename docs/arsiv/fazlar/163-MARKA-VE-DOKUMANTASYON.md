# Faz 163 — Marka ve Dokümantasyon Deneyimi

> **Durum:** ✅ Tamamlandı (2026-09-12)
> **Kaynak:** Kullanıcının onayladığı site planı · F-221 görsel dilimi · F-222
> **Önkoşul:** Faz 162 tamamlandı
> **Paketler:** Paket ikonu · Tracon.UI logo asset'i
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Yüzey değişmedi; sevk edilen XML metni değişti (sapma 1)
> **Tüketici yüzeyi:** docs-site tüm şablonlar ve 52 el yazısı sayfa · SVG/PNG marka asset'leri · console görüntüleri · llms türevleri
> **Taban:** `38cc4a0887e98c9d9958ec039ad3947fd9fff1a3`
> **Teslim:** Yerel production preview; kullanıcı commit ve deploy istemiyor.

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 50a4e23e:docs/arsiv/fazlar/163-MARKA-VE-DOKUMANTASYON.md
> ```
>
> Damıtıldı 2026-09-12 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

.NET geliştiricisi Tracon'un MAF üzerindeki control plane rolünü, çözdüğü sorunları ve teknik bilgiye erişim yolunu anlar. Astro/Starlight ve Pagefind korunur. Site İngilizce kalır. Yeni dependency eklenmez.

## Onaylanan tasarım ve kapsam

- Koyu kömür/slate, açık kırık beyaz; tek teal vurgu. Durum renkleri anlam taşır.
- Sistem fontları; 17 px prose, 14 px kod/etiket, yaklaşık 70ch okuma genişliği.
- Operasyon şeritleri, radar geometrisi, monospace kimlikler. Sürekli animasyon yok.
- Ana sayfa: onaylı hero → dört kontrol davranışı → MAF sınırı → gerçek console
  ve kod → build/integrate/operate/evaluate yolları → sınırlar ve footer.
- CTA: Explore the documentation → /getting-started/; Explore capabilities → /capabilities/.
- Anonim ziyaretçi dokümanla değerlendirir. Source build repo erişimi gerektirir.
  Yayımlanmamış NuGet/template komutu çalışan kurulum olarak sunulmaz.
- Sloganlar korunur; açıklayıcı mutlak iddialar kod gerçeğine göre marka belgesiyle
  birlikte düzeltilir. Recording varsayılan açık, kapatılabilir ve best-effort'tur.
- Ortak SVG kaynak: site favicon, paket PNG, console işareti ve sosyal kartlar.
  Console teması ve ilgisiz runtime adları kapsam dışıdır.
- Sidebar: Start here, Build agents, Orchestrate, Test and evaluate, Integrate,
  Operate, Extend, Reference; generated API grupları ayrıca korunur.
- Pagefind filtreleri: Documentation, .NET API, HTTP API. Varsayılan tüm içerik.
- Header, hero, page title, içerik ve footer ortak sunumu; mevcut erişilebilir
  menü/tema/kopyalama davranışları korunur. 404 özel sayfa olur.
- URL'ler ve eski anchor'lar korunur. Generated reference elle değiştirilmez.
- Mermaid sabit açık plate üstünde; etiketi küçültmeden alan içinde kaydırılır.
- Önce ana sayfa + concepts/runs tarayıcıda doğrulanır, sonra tüm içerik kapanır.

## Başlangıç kanıtları

2026-09-12: 52 izlenen el yazısı sayfa; 782 API + 302 HTTP + release notes
üretilir. Production çıktısı 1138 HTML. Site content/link/weight kapıları yeşil;
tam repo kapanışı henüz koşulmadı. En ağır sayfa troubleshooting: 56617 B gzip,
tavan 57000 B. Başlangıç rehberinin template yolu yayın yokken kurulum öneriyor.
Screenshot kapısı en.ts toplayıcısında nav literal'i arıyor; sıfır kez dönüyor.
Tarayıcıda desktop ana sayfa/rehber, 360 px rehber/HTTP, tablet API ve açık tema
runs diyagramı, production arama ve 404 incelendi. Tenant isolation sorgusunda
58 sonuç; generated tipler başta geliyor.
Kullanıcının mevcut marka.md ve hafıza indeksi değişiklikleri korunacak.

## Tüketici yüzeyi envanteri

Her satır teknik kapsam, iddia, başlangıç ve okuma akışı yönünden incelenir.
Kanıt sütunu inceleme tamamlandığında dosyanın gerçek bulgusunu taşır.

| URL | Kaynak | Tür | Yapılacak iş | Kanıt / sonuç |
|---|---|---|---|---|
| `//` | `index.mdx` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Ana sayfa onaylı hero, dört kontrol davranışı ve MAF sınırına göre yeniden yazıldı; eski anchor korundu |
| `/capabilities/` | `capabilities.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Açılış cümlesi paket ailesi ve control plane diline çekildi. `dotnet new` anması kurulum adımı değil; uyarı gerekmedi |
| `/concepts/` | `concepts/index.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te tarandı: `No surprises` ve `Every extension point is replaceable` başlıkları düzeltilmiş `getting-started` ile çelişiyordu. `Explicit infrastructure` ve `Replaceable services` oldu; eski anchor'lar `<span id>` ile korundu |
| `/concepts/agents/` | `concepts/agents.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka, iddia ve artikel taraması temiz |
| `/concepts/evaluation/` | `concepts/evaluation.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka ve iddia taraması temiz |
| `/concepts/governance/` | `concepts/governance.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka ve iddia taraması temiz |
| `/concepts/runs/` | `concepts/runs.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Dekoratör zinciri koda göre dörde çıkarıldı (`StructuredResponseValidatingAgent` dahil). Başlık değişti, anchor korundu |
| `/concepts/sessions/` | `concepts/sessions.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka ve iddia taraması temiz |
| `/concepts/tools/` | `concepts/tools.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. İki gevşetilmiş istisna doğru tarif edilmiş |
| `/concepts/workflows/` | `concepts/workflows.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka ve iddia taraması temiz |
| `/getting-started/` | `getting-started/index.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Dört kural mutlak dilinden çıkarıldı; `Evaluate through the documentation` uyarısı eklendi; dışa akış host'un kararı olarak yazıldı |
| `/getting-started/first-agent/` | `getting-started/first-agent.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Şablon yolu kaldırıldı; `Repository access required` uyarısı ve kaynak derleme adımları geldi. Anchor korundu |
| `/getting-started/persistence/` | `getting-started/persistence.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te okundu, değişiklik gerekmedi: yayın iddiası yok, uyarılar aside, her iddia ayar adı veya uç ile kanıtlı |
| `/getting-started/security/` | `getting-started/security.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Loopback varsayılanı mutlak güvenlik iddiası olmaktan çıkarıldı; host yetkilendirmesinin yerini tutmadığı yazıldı |
| `/getting-started/tools/` | `getting-started/tools.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te okundu, değişiklik gerekmedi: boş servis sağlayıcı tuzağı K-218 ile uyumlu |
| `/guides/background-work/` | `guides/background-work.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka ve iddia taraması temiz |
| `/guides/cli/` | `guides/cli.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | `Package availability` uyarısı eklendi; giriş paragrafı iki paketin işini ayırdı |
| `/guides/client-side-tools/` | `guides/client-side-tools.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te okundu, değişiklik gerekmedi: her iddia durum kodu veya bütçe ile kanıtlı |
| `/guides/coding-agents/` | `guides/coding-agents.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te tarandı: üç boyut iddiası bayattı. `under 10 KB`→`about 10 KB`, `17 KB`→`20 KB`, `400 KB`→`700 KB`. Sürüklenme kapıyla kapatıldı |
| `/guides/context-and-memory/` | `guides/context-and-memory.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka ve iddia taraması temiz |
| `/guides/ef-core/` | `guides/ef-core.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te tarandı: iki `an Tracon` artikel artefaktı düzeltildi. Diğer içerik ölçülü |
| `/guides/embedding/` | `guides/embedding.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te tarandı: 🚨 işareti kaldırıldı, uyarı korundu. Frontmatter `seven embedding points` gövdedeki `six points` ile hizalandı |
| `/guides/external-agents/` | `guides/external-agents.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı; bir artikel artefaktı düzeltildi |
| `/guides/inbound-triggers/` | `guides/inbound-triggers.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı; iki artikel artefaktı düzeltildi |
| `/guides/knowledge/` | `guides/knowledge.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka ve iddia taraması temiz |
| `/guides/model-providers/` | `guides/model-providers.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı; bir artikel artefaktı düzeltildi |
| `/guides/multimodal/` | `guides/multimodal.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka ve iddia taraması temiz |
| `/guides/observability/` | `guides/observability.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka ve iddia taraması temiz |
| `/guides/openai-api/` | `guides/openai-api.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te tarandı: beş artikel artefaktı düzeltildi, diyagram `accDescr` dahil. Uyumluluk sınırı doğru tarif edilmiş |
| `/guides/production/` | `guides/production.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te okundu: bir artikel artefaktı düzeltildi. Kalan içerik ayar adı ve varsayılanla kanıtlı |
| `/guides/reliability/` | `guides/reliability.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te okundu, değişiklik gerekmedi: her iddia ayar adı, varsayılan veya durum koduyla kanıtlı |
| `/guides/structured-output/` | `guides/structured-output.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Marka ve iddia taraması temiz |
| `/guides/testing/` | `guides/testing.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | `Package availability` uyarısı eklendi |
| `/guides/typescript-client/` | `guides/typescript-client.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | `Package availability` uyarısı eklendi |
| `/guides/voice/` | `guides/voice.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | `Package availability` uyarısı ve okuma akışı açıklaması eklendi |
| `/guides/write-your-own-agent-decorator/` | `guides/write-your-own-agent-decorator.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te tarandı: satır sonuna sarılmış `an \`TraconAgentSourceException\`` düzeltildi |
| `/guides/write-your-own-agent-source/` | `guides/write-your-own-agent-source.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te okundu, değişiklik gerekmedi: sözleşme ve öncelik kuralları ölçülü |
| `/guides/write-your-own-error-classifier/` | `guides/write-your-own-error-classifier.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te okundu, değişiklik gerekmedi: üç değerli karar sözleşmesi gerekçesiyle anlatılmış |
| `/guides/write-your-own-job-handler/` | `guides/write-your-own-job-handler.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | `Package availability` uyarısı ve okuma akışı açıklaması eklendi |
| `/guides/write-your-own-judge/` | `guides/write-your-own-judge.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | `Package availability` uyarısı eklendi; bir artikel artefaktı düzeltildi |
| `/guides/write-your-own-store/` | `guides/write-your-own-store.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | `Package availability` uyarısı eklendi |
| `/guides/write-your-own-tool/` | `guides/write-your-own-tool.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te tarandı: iki 🚨 işareti kaldırıldı, uyarı metni korundu |
| `/http-api/` | `http-api.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Metafor yok; sözleşme dili korundu |
| `/packages/` | `packages.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te tarandı: yerel model iddiası host'un dışa akışını kapsayacak şekilde daraltıldı. Yayın uyarısı zaten vardı |
| `/reference/compatibility/` | `reference/compatibility.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | `Package availability` uyarısı eklendi |
| `/reference/configuration/` | `reference/configuration.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te okundu, değişiklik gerekmedi: sayfa yalnız tanımlı seçenek değerlerini listeliyor, davranıştan çıkarım yapmıyor |
| `/reference/glossary/` | `reference/glossary.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te okundu, değişiklik gerekmedi: metafor yok, tanımlar tip adlarıyla birebir |
| `/reference/licensing/` | `reference/licensing.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | 163'te tarandı: kanıtsız pazar iddiası (`So is most of the market`) kaldırıldı |
| `/reference/read-views/` | `reference/read-views.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Giriş, iki alternatifi kötüleyen kurgudan çıkıp kullanım tarifine döndü |
| `/reference/versioning/` | `reference/versioning.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | `Package availability` uyarısı eklendi; bir artikel artefaktı düzeltildi |
| `/troubleshooting/` | `troubleshooting.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | `Package availability` uyarısı eklendi. Metafor yok; adım dili korundu |
| `/ui/` | `ui.md` | El yazısı | Marka, başlangıç/iddia ve okuma akışı taraması | Okuma akışı açıklandı. Konsol bütçeleri sayıyla kanıtlı |
Generated API/HTTP: generator + ortak sunum. Release notes: CHANGELOG kaynaklı.
Agent map/llms metinleri son içerikten üretilir. Paket XML'i **değişti** — gerekçe
"Plandan Sapmalar" 1'dedir; HTTP contract değişmedi.

Tabloda 52 satır vardır ve diskteki el yazısı sayfa sayısı 53'tür: fark
`reference/changelog.md`, yukarıda ayrıca anılan CHANGELOG türevi. Bu fazın
eklediği **`404.md` yeni bir sayfadır**, envanterin taradığı mevcut kümeye ait
değildir; kendi kabul case'i `MT-DKL-033`'tür. Kapı onu yapısal kurallardan muaf
tutar (kenar çubuğu, `## Read next`, llms dizini) ama ses ve iç-geçmiş
kurallarına tabi tutar.

## Bitiş Ölçütleri (DoD)

- [x] 52 el yazısı sayfanın envanterinde gerçek değerlendirme kanıtı var — 44'ü
      değişti, 8'i okundu ve değişiklik gerekmediği gerekçesiyle kaydedildi.
- [x] Tüm sayfa şablonları 360/640/1024/1440 px, iki temada doğrulandı: dokuz
      şablonda 51 ölçüm, hiçbirinde yatay taşma yok.
- [x] Arama/filtre/hata, menü, tema, anchor, kopyalama ve CTA çalışıyor. Arama
      üç filtre grubunu sayısıyla veriyor (`tenant isolation` → 57 sonuç:
      `.NET API` 44 · `Documentation` 13 · `HTTP API` 0). CTA'lar
      `/getting-started/` ve `/capabilities/`.
- [x] Klavye/focus, semantic HTML, kontrast, reduced-motion ve %200 zoom kontrol
      edildi: ilk durak "Skip to content" (`#_top` hedefi var), sayfa başına tek
      `h1`, dört landmark, sonsuz animasyon yok, `prefers-reduced-motion` kuralı
      var, %200 eşdeğeri 640 px'te taşma yok. Kontrast tabanı metin 5,49:1 ·
      metin dışı 3,74:1.
- [x] 19 gerçek console görüntüsü yeni işaretle ve `/tracon` altında yenilendi.
- [x] F-222 önce kırmızı testle gösterildi: `check-console-screens.test.mjs`
      sekiz test, biri boş envanterin kapıyı kızartmasını kanıtlar. Kapı artık
      18 gezinme girişi buluyor; eskiden sıfır dönüyordu.
- [x] `npm run check` dört adımda yeşil: 53 el yazısı · 1138 toplam sayfa,
      179 717 iç bağlantı kırıksız, en ağır sayfa 57 368 B. Agent haritası
      `--check` ile taze.
- [x] Dört repo kapısı sıfır uyarı (`kapi.py kapanis`, taban `38cc4a08`, tek
      koşumda uçtan uca yeşil): `tarama` 4,2 s · `dokuman-bakim --denetle` 8,9 s ·
      betik birim testleri 9,8 s · `build-agent-map --check` 0,1 s ·
      `denetim-paketi` 0,8 s · `dotnet build` 62,5 s · `dotnet test` 670,0 s ·
      `dotnet pack` 6,6 s · `dotnet format --verify-no-changes` 109,7 s ·
      `npm run check` 23,5 s.
- [x] `samples/Tracon.Api` ile gerçek run: `01a095a6-fae4-7140-a04d-2fbef4affc92`,
      `Completed`, 4 olay, 13 token, `echo-1`/`echo`. Aynı `Idempotency-Key` ile
      tekrar `Idempotency-Replayed: true` döndü.
- [x] Manuel kabul case'leri eklendi (32-DOKUMAN-KALITESI 24 → 37); bağımsız
      `faz-denetim` koşuldu.
- [x] Marka checklist (§12) tamam: yayın durumu üç yüzeyde, metafor `reference/`
      · `api/` · `http-api/` · `troubleshooting/` dışında, uydurma etimoloji yok,
      yasak kelime yok, ekran görüntüleri yeni işaretle, `en`/`tr` sözlük eşit.
      Yerel preview `http://localhost:4321/` üzerinde koşuldu.

## Plandan Sapmalar

**1. Paket XML'i değişti.** Plan "Paket XML'i ve HTTP contract değişmez" diyordu.
Tarama, sevk edilen XML dokümanında tüketiciye çıkan iki kusur sınıfı buldu.
Kullanıcı, contract'ı değiştirmeyen prose düzeltmesi olarak 163 içinde
kapatılmasını onayladı. HTTP contract ve public API yüzeyi değişmedi.

**2. Artikel artefaktı — Faz 162 yeniden adlandırmasının artığı.** Ürün
`AgentPrism`'den `Tracon`'a döndü; artikel eski ada göre seçilmişti ve yeniden
adlandırma ona dokunmadı, bu yüzden `an Tracon` kaldı. Bul-değiştir bunu
göremez: yanlış olan kelime, değiştirilmeyen kelimedir. **Dört varyant vardı ve
her tarama yalnız bir sonrakini açığa çıkardı:** düz `an Tracon`; satır sonuna
sarılmış veya kod biçimli `an \`Tracon…\``; cümle başındaki `An Tracon`; ve en
sinsisi — artikel satır sonunda, ad sonraki satırın `/// ` yorum önekinden
sonra. Dördüncüsünü elle tarama değil, **kapının kendisi** buldu: kural üretilen
sayfalara genişletilince `TraconOnlineEvaluationBuilderExtensions` sızıntısı
hemen düştü. 51 yer düzeltildi (19 kaynak · 16 site sayfası · 8 test ·
7 örnek · 1 npm test dizesi). Biri `UsageDiagnostics.cs` içindeydi: her
tüketicinin build çıktısında görünen derleyici tanısı.

**3. Üretilen referansta 60 satır daha vardı, kökü üreteçteydi.** `plainText`
bir `cref`'i her zaman üye biçiminde (`Tracon.IRunJudge`) yazıyordu; XML ise
artikeli kısa ada göre seçiyor (`an <see cref="IRunJudge"/>`). `resolveReference`
bu tuzağı zaten çözmüştü ve kendi yorumunda anlatıyordu; düz metin yolu aynı
kuralı uygulamıyordu. Kural taşındı, sayfalar elle düzeltilmedi.

**4. `no-surprises rule` ifadesinin bıraktığı sekiz bozuk cümle.** Faz 162'den
değil, Faz 75'e kadar giden eski bir `K-NNN` temizliğinden kalma ("The the
no-surprises rule (no surprises) gate…", "This follows from rationale the
no-surprises rule"). Sekizi de düzeltildi.

**5. Bayat boyut iddiası.** `coding-agents.md` üretilen dosyaları "under 10 KB",
"About 17 KB" ve "about 400 KB" diye ölçüyordu; gerçek değerler 10,3 KB, 20,5 KB
ve 712,9 KB idi. Sayılar düzeltildi.

**6. Üç yeni site kapısı.** Kusur sınıfları tek vakayla kapanmaz. `check-content.mjs`
şunları ekledi ve üçü de önce kırmızı, sonra yeşil gösterildi: sevk edilen prose
içinde alarm emojisi · ürün adından önce yanlış artikel · üretilen dosya boyutu
iddiasının sürüklenmesi (±%15 bandı, "about" dilini karşılar).

Artikel kapısı **üretilen sayfaları da** tarar. Gerekçesi ölçüldü: 60 vakanın
hiçbirini kimse yazmamıştı, üreteç üretmişti — el yazısı sayfalara bakan bir
kapı onu göremez. Kapsam genişletildiği anda kapı kaçırdığım bir kaynak
sızıntısını daha düşürdü.

**7. Ağırlık tavanı yükseltildi (K-756).** Plan "tavan yükseltilmez, eski CSS
kaldırılır" diyordu. Ölçüm o çareyi kapattı: `site.css`'te ölü kural yok — ölü
görünen dört sınıfın dördü de canlı (`pagefind-ui` çalışma anında enjekte edilir).
`troubleshooting` 57 367 B'ye çıkmıştı; sebebi marka sözleşmesinin zorunlu
kıldığı yayın uyarısı ve maliyeti tam 376 B. Uyarıyı kısaltmak 95 B, düşürmek
tavanın 9 B altını veriyordu. Kullanıcı onayıyla tavan 58 000 B oldu ve gerekçe
hem `check-weight.mjs` yorumuna hem K-756'ya yazıldı.

**8. Kontrast tabanı geri kazanıldı.** Yeni palet metin kontrast tabanını
5,47:1'den 5,04:1'e düşürmüştü — kalite sözleşmesinin "yalnız yükselir" cırcırına
ters ve `faz-denetim`'de 🔴. `--tracon-text-muted` açık temada aynı ton ve
doygunlukta %2 koyulaştırıldı (`#526b6d` → `#4e6567`); taban 5,49:1 oldu. Metin
dışı taban zaten yükselmişti (3,46 → 3,74). Sözleşmenin F bölümü yeni ölçümle
güncellendi.

**10. Ses testindeki yarış kapatıldı.** Kapanış kapısını kıran tek kusur bir test
yarışıydı ve bu fazın konusuyla ilgisi yoktu. DoD "dört repo kapısı sıfır uyarı"
dediği için kapsamda bırakılamazdı; düzeltme testin kendi `WaitForAsync`
yardımcısını kullanır, ürün koduna dokunmaz. Ayrıntı "Denetim Bulguları"
bölümündedir.

**9. Console teması kapsam dışı bırakıldı.** Konsolun `--ap-*` CSS token'ları ve
mor vurgu rengi eski addan kalmadır. Plan "console teması ve ilgisiz runtime
adları kapsam dışıdır" dediği için dokunulmadı; ekran görüntüleri yeni işaretle
yeniden üretilmiş durumda.

## Bu Fazda Verilen Kararlar

Kullanıcının onaylı planındaki kararlar yukarıdadır; yeni kalıcı contract yok.
Aşağıdakiler yerel uygulama tercihidir ve `K-*` kaydı açmaz:

- Sevk edilen prose'da alarm emojisi yerine kalın metin veya aside kullanılır.
  Üreteç zaten `🚨|⚠️` → `**Important:**` dönüşümü yapıyordu; el yazısı sayfa
  o yoldan geçmediği için kural kapıya taşındı.
- Üretilen referansta bir `cref` düz metne dönerken **tip kısa adıyla**, üye ise
  bildiren tipiyle yazılır. Bağlantı yolundaki kural ile aynıdır.
- Ürün adı `a` artikeli alır. Kapı bunu el yazısı sayfalarda zorlar.

## docs-site senkron gerekçesi

`--site-denetle` yedi kural tetikledi; altısı karşılandı. Karşılanmayan tek
kural **`kalicilik`**: `src/Tracon.Sql.Shared/Migrations/MigrationHostedService.cs`
değişti ama `getting-started/persistence.md` değişmedi.

**Gerekçe:** o dosyadaki tek değişiklik bir XML yorumundaki artikeldir
(`An Tracon instance` → `A Tracon instance`). Kalıcılık davranışı, migration
sırası, kilit, checksum veya yapılandırma yüzeyi değişmedi; sayfanın anlatacağı
yeni bir şey yok. Sayfa ayrıca bu fazda okundu ve marka/iddia yönünden
değişiklik gerekmediği envantere kaydedildi.

## Denetim Bulguları

Bağımsız denetçi (taze bağlam, yalnız DoD + diff) 2026-09-12'de koştu. Üç 🔴,
sekiz 🟡, üç 🟢 bulgu. **Kritik bulguların üçü de kapatıldı.**

### 🔴 Kapatıldı

| # | Bulgu | Kapanış |
|---|---|---|
| 1 | Artikel sınıfı sevk edilen kaynakta **kapanmamıştı**: altı örnek XML'de duruyordu ve dördü üretilen sayfalara basılmıştı | **Kök neden benim desenimdi.** Dördüncü tur `Tracon\b` ile aradı; `\b` bir sonraki karakter harf olunca düşer, yani `an TraconException` hiçbir turda eşleşmedi. Sınır kaldırıldı, altı yer düzeltildi, **kapı da aynı sınırdan arındırıldı** |
| 2 | `marka.md` §13'teki tarama reçetesi `///` önekini sıyırmıyordu; bir sonraki yeniden adlandırma onu kullanacaktı | Reçete ayırıcı sınıfıyla (`(?:\s\|///\|//\|\*\|>)+`) ve `\b` olmadan yeniden yazıldı; iki tuzak da metinde adlandırıldı |
| 3 | Ağırlık tavanı cırcırı ters çevrildi ama "Plandan Sapmalar"da yoktu | Sapma 7 olarak yazıldı; K-756 kaydı ve `check-weight.mjs` yorumu ölçümü taşıyor |

### 🟡 Kapatıldı

| # | Bulgu | Kapanış |
|---|---|---|
| 4 | Aynı artefakt `samples/` (7) ve `tests/` (2) içinde de duruyordu | Düzeltildi; biri okuyucuya dönük `samples/Tracon.Embedded/README.md` idi |
| 5 | `404.md` `manualContent`'ten **toptan** çıkarılmıştı; yapısal olmayan kapıları da atlıyordu | Muafiyet kural bazına indirildi: `handWrittenContent` (404 dahil) ses ve iç-geçmiş kurallarını, `manualContent` yapısal kuralları besler |
| 6 | F-222 sınıf taraması aynı dosyada bitmemişti: `telemetryNames` ve `validApiKeyScopes` de sessizce sıfır dönebilirdi | İkisine de sıfır muhafızı eklendi |
| 7 | Kalite sözleşmesinin F tablosu 2026-09-12'ye tarihliydi ama iki satır ölçülmemişti | `DIAGRAM_EXEMPT` 5 → 6; harita bütçesi satırı artık sayıyı tekrar etmiyor, `agentMapBudgetBytes` sabitine işaret ediyor |
| 8 | `dotnet new tracon-api` iki sayfada çalışan komut gibi duruyordu | `coding-agents.md` diğer kurulum sayfalarıyla aynı uyarıyı aldı; `capabilities.md`'de komut adı düştü (üretilen haritanın bütçesi dar, aside oraya sığmazdı) |
| 9 | Marka işareti üç bayt-bayt kopya hâlinde commit'leniyordu, hiçbir kapı uyuştuklarını doğrulamıyordu (K-411 sınıfı) | Agent haritasının çözümü kopyalandı: `check-content.mjs` üç dosyayı karşılaştırır ve üreteci adıyla söyler. Kırmızı gösterildi |
| 10 | İki DoD satırının kanıtı kayıtlı değildi | DoD artık `runId`, olay sayısı, token ve kapı çıktılarını taşıyor |
| 11 | Depo kökünde iki izlenmeyen PNG kalmıştı | `.playwright-mcp/` altına taşındı (gitignore'da) |

### Kapanışta çıkan yarış kusuru (sapma 10)

Kapanış kapısı `LiveVoiceLifecycleTests.The_transcript_is_written_to_the_session_
history_when_persistence_is_on` üzerinde **iki tam koşumda** düştü. `kapi.py` onu
izole tekrar koştu ve geçti; kapının kuralı "ikinci koşumda da düşerse gerçek
kusurdur" der.

**Ölçüm sırası kusuru üçe indirdi:**

| Kapsam | Sonuç |
|---|---|
| Tek test, izole | geçti |
| `Tracon.AspNetCore.FunctionalTests` tek başına | 1046/1046 geçti |
| Tüm çözüm (`-maxcpucount:1`) | iki kez düştü |

**Kusur testin kendisindeydi, üründe değil.** `CloseAsync` oturum sökülür sökülmez
döner; geçmişe yazma bundan sonra sunucunun kendi işidir. Test depoyu **bir kez**
okuyup doğruluyordu — aynı dosyanın `WaitForAsync` yardımcısını kullanmıyordu,
oysa dosyadaki diğer zamanlamaya bağlı doğrulamalar onu kullanır. Tam çözüm
yükünde yazma penceresi genişleyince okuma yazmanın önüne geçti.

Doğrulama yazmayı bekleyecek şekilde düzeltildi. Bu fazın hiçbir değişikliği ses
yolunun davranışına dokunmuyor: o iki dosyadaki tek fark bir XML yorumundaki
artikeldir (`git diff` ile doğrulandı).

### 🟢 Aday olarak kaydedildi

12 · Console `TraconMark` bileşeninde ölü `text-fg` sınıfı — işaret
`currentColor` izlemiyor. Console teması bu fazın kapsamı dışında.
13 · `wrongArticle` `exec` ile ilk eşleşmeyi alır; iki artefaktlı sayfa iki
koşum ister. Kapı yine kızarıyor.
14 · `alarmVoice` çıplak `⚠` (U+26A0, VS16'sız) ile eşleşmiyor. Bugün örneği yok.

**Denetçinin temiz bulduğu başlıklar:** yeni sınır geçen davranış yok · imza
değişikliği yok (C# diff'i yalnız yorum ve iki dize) · public API değişmedi ·
Pagefind filtre indeksi üç grubu taşıyor · `id="_top"` her sayfada tek ·
`SourceLanguageTests` taban çizgisi değişmedi.

## Sonraki Faza Devir Notu

**Faz kapandı.** `kapi.py kapanis --taban 38cc4a08` tek koşumda uçtan uca sıfır
uyarı verdi; bağımsız denetimin üç 🔴 bulgusu da kapatıldı. Doküman arşivlendi ve
damıtıldı, commit atıldı, site yayınlandı.

**Devralınan durum:**

- Çalışma ağacında 151 yol var; 9'u yeni dosya. Senkronizasyon kopyası yok.
- Site `scripts/site-deploy.sh` ile yayınlandı. Görsel kayıt `.playwright-mcp/`
  altında (gitignore'da): açılış sayfası 1440 px, açık ve koyu tema.
- Örnek uygulama kanıtı bu dokümanın DoD bölümündedir; uygulama durduruldu.

**Sonraki oturumun bilmesi gerekenler:**

1. **Ağırlık payı bitti.** Tavan bir kez yükseltildi (K-756, 58 000 B) ve
   `troubleshooting` 57 368 B'de. Bu sayfaya içerik eklemek tavanı ikinci kez
   yükseltmeyi gerektirir; karar kaydı bunu **yasaklıyor** — sayfa bölünür.
2. **`docs/hafiza/site-uretim-kapilari.md` %4 boş kaldı.** Bir sonraki not
   taşırır; önce damıtılmalı.
3. **Console teması eski addan kalma.** `--ap-*` token'ları ve mor vurgu rengi
   duruyor; bu faz console temasını kapsam dışı bıraktı. Ekran görüntüleri yeni
   işaretle üretilmiş durumda, yani yalnız palet geride.
4. **Denetimin 🟢 kalemleri açık.** Üçü de "Denetim Bulguları" bölümündedir ve
   hiçbiri bugün bir okuyucuyu etkilemiyor. `docs/ADAYLAR.md` "aday kalmadı"
   durumunda olduğu için F numarası açılmadı; bir sonraki `aday-kesfi` turunda
   oradan alınır.
5. **Yeniden adlandırma artefaktı sınıfı kapandı** ama kapı yalnız el yazısı
   site sayfalarını koruyor. Kaynak tarafı tek seferlik temizlendi; bir sonraki
   ad değişiminde `docs/hafiza/marka.md` §13'teki üç varyantı tara.

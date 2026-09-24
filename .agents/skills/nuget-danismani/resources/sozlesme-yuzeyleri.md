# Sözleşme Yüzeyleri

> `nuget-danismani` Adım 3'ün kaynak dosyasıdır. 1.0 yalnız .NET API'sini
> dondurmaz: tüketicinin **bağımlı olduğu her şey** bir sözleşmedir. Bir
> yüzeyin SemVer kuralı yazılı değilse, tüketici onu kendisi tahmin eder —
> ve kırıldığında hatayı Tracon'a yazar.
>
> Tabloyu her GA/olgunluk turunda **yeniden** doldur. "Kapı" sütunu bugünkü
> ölçümdür; bir önceki turun tablosu kanıt değildir.

## Nasıl okunur

Her yüzey için üç soru sorulur:

1. **Söz** — 1.x boyunca bu yüzeyde ne değişebilir, ne değişemez? Yazılı mı,
   nerede?
2. **Kapı** — Sözü bozan bir değişikliği yayından **önce** durduran makine
   kontrolü var mı? Hangi komut, hangi test?
3. **Boşluk** — Söz yazılı ama kapı yoksa, ya da kapı var ama söz yoksa, bu
   bir bulgudur. İlki sessiz kırılma, ikincisi keyfi bir kilittir.

Önerilen varsayılan kural (kullanıcı başka karar vermedikçe): **minor
yalnız ekler; kaldırma ve anlam değişikliği yalnız major'da ve önceki bir
minor'da duyurulduktan sonra olur.** Aşağıdaki "1.x kuralı" sütunu bu
varsayılanın yüzeye özgü hâlidir.

## On dört yüzey

| # | Yüzey | Örnek | 1.x kuralı (önerilen) | Bugünkü kapı | Ölçülecek boşluk |
|---|---|---|---|---|---|
| 1 | .NET public API | `PublicAPI.*.txt`, ~650 tip | Kaldırma/imza değişikliği yalnız major | Public API analyzer; ApiCompat taban doğrulaması son `v*`'a karşı (K-864); TFM'ler arası strict mode (K-865) | GA'da kapının "notta adıyla geçsin" modundan "dur" moduna geçmesi; `Shipped` dolumu (K-603) |
| 2 | Paket grafiği | Kimlik kümesi, TFM kümesi, bağımlılık aralıkları | Paket kimliği kalkmaz; TFM yalnız Microsoft desteği bitince düşer; kardeş aralığı tam (`[x]`, K-858) | `kapi.py yayin` kimlik/metaveri/K-008 kontrolü; `ReleaseArtifactTests` | Ön sürüm upstream'in açık alt sınırı (Mercek 8); upstream aralık politikası yazılı mı |
| 3 | HTTP yönetim API'si | `/api/*` — 168 operasyon, OpenAPI `buildTransitive/tracon.json` | Uç, alan, durum kodu kalkmaz; yeni alan eklenebilir; zorunlu yeni girdi yok | `OpenApiSnapshotTests` — **kod ↔ anlık görüntü** kayması | Sürümden sürüme **kırılma** kapısı yok: anlık görüntü her değişiklikte yeniden yazılır. Rota sürümleme politikası (`/api/v1`?) yazılı değil |
| 4 | Olay ve akış biçimleri | SSE `run` olayları, webhook gövdesi | Olay tipi ve alan anlamı değişmez; yeni tip eklenebilir, tüketici bilinmeyeni yok sayar | `RunEventFrameNameContractTests`, `RunEventPayloadContractTests` | Webhook gövdesinin sürüm/imza sözü; "bilinmeyen olayı yok say" kuralı dokümanda mı |
| 5 | Protokol uçları | OpenAI-uyumlu `/v1/responses`, `/v1/chat/completions`; MCP; A2A | Tel biçimi upstream spec'indir; Tracon'un sözü **eşleme**dir | Uç testleri; A2A/Hosting ön sürüm (K-008) | Upstream spec değişince Tracon major mı? Yazılı değil |
| 6 | Yapılandırma | `Tracon:*` bölümleri, options adları, ortam değişkenleri | Anahtar kalkmaz; yeniden adlandırma eski adı en az bir minor boyunca okur ve uyarır | Açılış doğrulaması (options validation) | Anahtar kümesinin sürümden sürüme taban kapısı ölçülmedi |
| 7 | Kalıcı veri | Şema, migration'lar (PostgreSQL 55 · SQL Server 43 · SQLite 42), `runs_v1` görünümü, durum zarfı | Migration immutable ve yalnız ileri; her 1.x her önceki 1.x veritabanını yükseltir; zarf nesli (`StateSchemaVersion`) yalnız major'da ilerler | `applied-migrations.json`; yakalanmış durumdan okuma testi (reference/versioning); `tracon state-check` | Preview veritabanından 1.0'a yükseltme yolu **paketten** ölçüldü mü? |
| 8 | Telemetri | Kaynak/metre `Tracon`, `tracon.*` metrik ve etiket adları | Ad ve etiket anlamı değişmez; yeni metrik eklenebilir | Adlar testlerde kullanılıyor; guides/observability "stable" diyor | Adların sürümden sürüme taban kapısı ölçülmedi. **Kardinalite:** `tracon.tenant.id` çok kiracılı kurulumda her run metriğinde; kapatma seçeneği yok (`IncludeAgentVersionTag` var, kiracı için yok) |
| 9 | Hata sözleşmesi | Stable hata kodları (`provider_credential_unsupported`, `judge_*`), ProblemDetails, HTTP durumları | Kodun anlamı değişmez; yeni kod eklenebilir | `RawExceptionTextSiteTests` (sızıntı), `ProblemDetailsLanguageTests` | Hata kodu **kümesinin** envanteri ve taban kapısı |
| 10 | Yetki modeli | Roller, API anahtarı kapsamları, `TraconPolicies.*` | Bir kapsam genişlemez; daraltma güvenlik sürümünde olabilir ve duyurulur | `RunAuthorizationCoverageTests` ve uç testleri | Kapsam ↔ uç matrisinin yayınlanmış bir tablosu var mı |
| 11 | CLI | `tracon` komutları, bayraklar, çıkış kodları (`state-check` → `3`), çıktı biçimi | Komut/bayrak kalkmaz; çıkış kodu anlamı değişmez; makine okunur çıktı biçimi sabit | `Tracon.Cli.FunctionalTests` | `--version` yok (A-57); makine okunur çıktı sözü yazılı mı |
| 12 | Şablon çıktısı | `dotnet new tracon-api` | Üretilen kod tüketicinindir; şablon değişikliği yalnız yeni projeyi etkiler; paket sürümü şablonun kendi sürümüne damgalanır (K-843) | `TemplateFixture` | Temiz makinede restore → run (A-34 sınıfı) |
| 13 | İstemciler | `Tracon.Client` (NuGet), `@tracon/client` (npm) | Aynı OpenAPI'den, aynı sürüm hattıyla üretilir; yüzey #3'ün kuralını miras alır | Şema kayması + npm testleri | ApiCompat `Tracon.Client`'ı karşılaştırmaz (reference/versioning) — kırılma kapısı #3'e bağlı |
| 14 | Gömülü arayüz | `Tracon.UI` rotaları, `embed.js`, CSP | Gömme API'si ve rota öneki sabit; ekran içeriği sözleşme değildir | Konsol kapıları, E2E | Gömme API'sinin sözü yazılı mı; gömülü üçüncü taraf lisans bildirimi (Mercek 9) |

## GA'da her yüzey için yazılacak tek satır

`docs-site` "Versions and upgrades" sayfası bugün yalnız preview sözünü
anlatır. 1.0'dan önce her yüzey için **tek satırlık** kural yazılır: "Bu
yüzeyde 1.x içinde X değişebilir, Y değişmez, Z değişmeden önce N minor
duyurulur." Satırı olmayan yüzey, tüketici için tanımsızdır.

Kapatma işi `tuketici-dokuman-senkronu`'nundur; bu dosya boşluğu **bulur**.

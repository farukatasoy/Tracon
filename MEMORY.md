# MEMORY.md — Yönlendirme

> **Yalnız yönlendirme + her oturumda geçerli tuzaklar.** Alan notları
> `docs/hafiza/` altındadır ve **yalnız o alana dokunurken** okunur.
> Bütçelidir; denetim `python3 scripts/dokuman-bakim.py --denetle`.

---

## Nereye Bakmalı

**Yalnız ilgili satırın dosyasını** oku. Hepsini okuma. Belirli bir şey
arıyorsan dosyayı açmak yerine grep et: `grep -rn "AsyncLocal" docs/hafiza/`.

| Dokunduğun iş | Oku |
|---|---|
| Bir şeyin nerede yaşadığını arıyorsun | [`docs/hafiza/kod-haritasi.md`](docs/hafiza/kod-haritasi.md) |
| Ses tool'ları, gerçek zamanlı konuşma katmanı | [`docs/hafiza/ses-ve-konusma.md`](docs/hafiza/ses-ve-konusma.md) |
| MAF tipi, harness, `AIContextProvider`, skill | [`docs/hafiza/maf-api.md`](docs/hafiza/maf-api.md) |
| Workflow yürütme, executor kimliği, HITL | [`docs/hafiza/workflows.md`](docs/hafiza/workflows.md) |
| SQL, migration, `jsonb`, sütun indeksi (PostgreSQL) | [`docs/hafiza/postgresql.md`](docs/hafiza/postgresql.md) |
| Paylaşılan SQL katmanı, SQL Server, yeni sağlayıcı | [`docs/hafiza/sql-saglayicilari.md`](docs/hafiza/sql-saglayicilari.md) |
| SQLite'a özgü (indeks ad alanı, upsert, `ExecuteScalarAsync` CLR tipi) | [`docs/hafiza/sqlite.md`](docs/hafiza/sqlite.md) |
| Minimal API ucu, DI kaydı, HTTP davranışı | [`docs/hafiza/aspnetcore-di.md`](docs/hafiza/aspnetcore-di.md) |
| Enum/alan JSON serileştirme, `.WithTags`/`.Produces` | [`aspnetcore-json.md`](docs/hafiza/aspnetcore-json.md) |
| MSBuild, csproj, NuGet, AOT, analyzer tanısı | [`docs/hafiza/build-ve-analyzer.md`](docs/hafiza/build-ve-analyzer.md) |
| Test yazımı (xunit, Shouldly, Testcontainers, Playwright) | [`docs/hafiza/test-altyapisi.md`](docs/hafiza/test-altyapisi.md) |
| Arayüz (Vite, SPA rota, TS) | [`docs/hafiza/frontend.md`](docs/hafiza/frontend.md) |
| Model sağlayıcısı (OpenAI, Anthropic, Google, uyumlu uçlar) | [`docs/hafiza/openai-saglayici.md`](docs/hafiza/openai-saglayici.md) |
| `RunRecording` zinciri, `secret` filtresi, metrik, sürüm | [`docs/hafiza/cekirdek-calistirma.md`](docs/hafiza/cekirdek-calistirma.md) |
| Dışa açılan MCP/A2A sunucusu (`McpServer/`, `A2A/`) | [`docs/hafiza/mcp-a2a-sunucu.md`](docs/hafiza/mcp-a2a-sunucu.md) |

---

## Her Oturumda Geçerli (bunları oku)

Alana bağlı değildir; her fazda tekrar bedel ödettiler.

- **🚨 `AsyncLocal` yazımı çağırana geri akmaz — dört kez yaşandı** (Faz 6, 11,
  12, 15). Kural: `scope`/`span` **çağıran metodun kendi gövdesinde** yazılır;
  akışlı yolda **her `MoveNextAsync` öncesi** tekrarlanır. Dört vaka:
  `docs/hafiza/cekirdek-calistirma.md`.
- **🚨 Bir davranışı düzeltmek, o davranışa dayanan çağıranı sessizce değiştirir.**
  Oturum deposu kiracıyla sınırlanınca ses ucunun "başkasının oturumu" reddi
  etkisiz kaldı (K-283); birim değil **fonksiyonel** testler yakaladı. Depo/servis
  davranışını değiştirdiğinde `grep -rn "<metot>" src/` ile çağıranları tara.
- **🚨 İmza değiştirmek ile gövdeyi kullanmak İKİ AYRI ADIMDIR.** Yeni bir
  alan/parametre eklerken çağrı zincirindeki **her katmanın gövdesini** elle izle.
  Yaşandı (Faz 20): `RunEventWriter.CompleteAsync`'e `cost` parametresi eklendi,
  nesne başlatıcıya `Cost = cost` yazılmadı — 1068 test yakalamadı.
- **🚨 Dört kapının dördünü de çalıştır.** `dotnet build` yeşilken `dotnet format`
  276 `IDE0055` hatası verdi (Faz 11 bu yüzden eksik kapandı). Kaynak üreteci
  build'in analyzer geçişinde tanıyı gizleyebilir.
- **Birim testi yetmez — örnek uygulamayı gerçekten çalıştır.** Sekiz fazda gerçek
  hatalar **yalnız** orada çıktı; hepsi testlerden geçmişti (K-166, K-167).
- **🚨 Struct alanını atamamak `default` bırakır ve seri hâle getirme çöker.**
  Atanmayan `JsonElement` `Undefined` olur; etki tek kayıtla kalmaz, o kaydı
  içeren **liste ucunun tamamı** çöker. Yeni kayıt üreten her kod yolunda
  zorunlu olmayan alanları da doldur (`docs/hafiza/cekirdek-calistirma.md`).
- **🚨 Senkronizasyon kopyaları (`<ad> 2.<uzantı>`) — BEŞ kez.** `.cs` → CS0101,
  `.ts` → TS2741; varlık kopyası `build`'i yeşil bırakır ama arayüz yüklenmez.
  Faz 57 kopyaları **commit etti**, `main` derlenmedi (K-411). İki tuzak:
  `git status` **temiz** görünür (kopya izleniyordur) ve `src` taraması
  **yetmez** (`tests/` altındaydılar). Kapı: `faz-tamamlama` Adım 1. Silmek
  yetmez — `wwwroot` + `agentprism-frontend.stamp` damgasını da sil.
- **🚨 `dotnet test` dakikalarca ASILI kalıyorsa alt süreç boru hatlarına bak.**
  Öksüz MSBuild düğümleri (`nodeReuse:true`) boruyu açık tutar ve
  `WaitForExitAsync` ~15 dk bloke kalır; çözüm `MSBUILDDISABLENODEREUSE=1`
  (8 dk+ → 18,5 sn). İkinci sebep: `-p:AgentPrismFrontendEnabled=false` ile
  derleyip **E2E** koşmak. Ayrıntı: `docs/hafiza/test-altyapisi.md`.
- **🚨 Bir toplama/hesaplama ifadesi kodun BEŞ farklı yerinde elle tekrarlanıyorsa, ona bir terim eklemek sessiz bir kusur SINIFI üretir.** Faz 68'de `InputCost + OutputCost` yedi yerde elle yazılıydı; üçüncü bir maliyet terimi (cache ücreti) eklenince SQL tarafı düzeltildi, çalışma anı (kota · metrik · webhook · workflow kotası · judge · online eval · arayüz) düzeltilmedi — **maliyet tavanı olan bir kiracı tavanı aşabilirdi** ve 4241 test yakalamadı. Bağımsız denetim buldu. Kural: bir `record`'a toplama girecek bir alan eklerken ona bir `Total()` metodu ver ve `grep` ile sınıfı tara.
- **Bash'te `cd` kalıcıdır**; doğrulama komutlarında **mutlak yol** kullan.
- **`dotnet test` MTP'dir, VSTest değil.** `--filter-query` yoktur (`MSB1001`).
  Tek test: `./artifacts/bin/<Proje>/release/<Proje> --filter-method "*Ad*"`.
  Bir testin **bayat mı kusurlu mu** olduğunu ayırmanın yolu budur — aynı testi
  `git worktree add <dizin> HEAD` ile temel sürümde de izole koş.
- **🚨 Tool'un gördüğü servis sağlayıcı BOŞTUR.** MAF, `AIFunctionArguments.Services`
  olarak `EmptyServiceProvider` geçirir; bir tool bağımlılığını **kurulum anında**
  almalıdır (`new BenimTool(provider)` + fabrika kaydı). Aynı sebeple `AddToolsFrom`
  ile kaydedilen **örnek metot** tool'ları da çalışmaz (K-218,
  `docs/hafiza/cekirdek-calistirma.md`). **🚨 İzole ölçüm entegre davranışı
  kanıtlamaz** — ayrı bir konsol probunda aynı çağrı çalışıyordu.
- **🚨 Planın YAPISAL iddiasını (katman, sıra, konum) kabul etmeden GREP'le ölç.**
  Faz 48'in planı guard'ı "boru hattının en dışına" koyuyordu; tek bir grep o
  konumun tool çağrı turlarını göremediğini gösterdi ve fazın yarısı taşımaya
  dönüştü (K-320). Yanlış konum derlenir, testten geçer, yalnız gerçek
  senaryoda çöker.
- **MAF ve OpenAI tip adlarını tahmin etme.** `AgentResponse` (`AgentRunResponse`
  değil), `ResponsesClient` (`OpenAIResponseClient` değil). Yeni tip kullanmadan
  önce `maf-api-kesfi` skill'ini çalıştır.

---

## Not Ekleme Kuralı

Not **alan dosyasına** yazılır (`docs/hafiza/<alan>.md`), buraya değil. Buraya
yalnız **alandan bağımsız** ve **tekrar bedel ödeten** bir ders girer.
`AGENTS.md` / `KARARLAR.md` / skill'lerde yazılı olanı kopyalama. Bayatlayan
notu güncelle veya sil.

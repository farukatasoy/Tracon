# MEMORY.md — Kurumsal Bilgi Defteri (Yönlendirme)

> Oturumlar arası biriken keşif notları. Bu dosya **yalnız yönlendirme + her
> oturumda geçerli tuzaklar** taşır. Alan notları `docs/hafiza/` altındadır ve
> **yalnız o alana dokunurken** okunur.
>
> **Bütçe: bu dosya 8 KB'yi aşamaz.** Aşarsa not alana taşınır.
> `scripts/dokuman-butcesi.sh` bunu denetler.

---

## Nereye Bakmalı

Bir alana dokunmadan önce **yalnız ilgili satırın dosyasını** oku. Hepsini okuma.

| Dokunduğun iş | Oku |
|---|---|
| Bir şeyin nerede yaşadığını arıyorsun | [`docs/hafiza/kod-haritasi.md`](docs/hafiza/kod-haritasi.md) |
| MAF tipi, harness, `AIContextProvider`, skill | [`docs/hafiza/maf-api.md`](docs/hafiza/maf-api.md) |
| Workflow yürütme, executor kimliği, HITL | [`docs/hafiza/workflows.md`](docs/hafiza/workflows.md) |
| SQL, migration, `jsonb`, sütun indeksi (PostgreSQL) | [`docs/hafiza/postgresql.md`](docs/hafiza/postgresql.md) |
| Paylaşılan SQL katmanı, SQL Server, yeni sağlayıcı | [`docs/hafiza/sql-saglayicilari.md`](docs/hafiza/sql-saglayicilari.md) |
| SQLite'a özgü (indeks ad alanı, upsert, `ExecuteScalarAsync` CLR tipi) | [`docs/hafiza/sqlite.md`](docs/hafiza/sqlite.md) |
| Minimal API ucu, DI kaydı, HTTP davranışı | [`docs/hafiza/aspnetcore-di.md`](docs/hafiza/aspnetcore-di.md) |
| MSBuild, csproj, NuGet, AOT, analyzer tanısı | [`docs/hafiza/build-ve-analyzer.md`](docs/hafiza/build-ve-analyzer.md) |
| Test yazımı (xunit, Shouldly, Testcontainers, Playwright) | [`docs/hafiza/test-altyapisi.md`](docs/hafiza/test-altyapisi.md) |
| Arayüz (Vite, SPA rota, TS) | [`docs/hafiza/frontend.md`](docs/hafiza/frontend.md) |
| Model sağlayıcısı (OpenAI, Anthropic, Google, uyumlu uçlar) | [`docs/hafiza/openai-saglayici.md`](docs/hafiza/openai-saglayici.md) |
| `RunRecording` zinciri, `secret` filtresi, metrik, sürüm | [`docs/hafiza/cekirdek-calistirma.md`](docs/hafiza/cekirdek-calistirma.md) |
| Dışa açılan MCP/A2A sunucusu (`McpServer/`, `A2A/`) | [`docs/hafiza/mcp-a2a-sunucu.md`](docs/hafiza/mcp-a2a-sunucu.md) |

Aradığın belirli bir şeyse dosyayı açmak yerine **grep** et:

```bash
grep -rn "AsyncLocal" docs/hafiza/
```

---

## Her Oturumda Geçerli (bunları oku)

Bunlar alana bağlı değildir; her fazda tekrar tekrar bedel ödettiler.

- **🚨 `AsyncLocal` yazımı çağırana geri akmaz — dört kez yaşandı.** Span (Faz 6),
  `run scope` (Faz 11), `async IAsyncEnumerable` gövdesi (Faz 12),
  `RunStreamingAsync`'in arka plan görevi (Faz 15). Kural: `scope`/`span` **çağıran
  metodun kendi gövdesinde** yazılır; akışlı yolda **her `MoveNextAsync` öncesi**
  tekrarlanır. Ayrıntı ve dört vaka: `docs/hafiza/cekirdek-calistirma.md`.
- **🚨 Bir davranışı düzeltmek, o davranışa dayanan çağıranı sessizce değiştirir.**
  Faz 41'de bellek içi oturum deposu kiracıyla sınırlandırıldı; ses ucunun
  "başkasının oturumu" reddi bunun üzerine kuruluydu ve etkisiz kaldı (K-283).
  Birim testleri değil, **fonksiyonel testler** yakaladı. Bir depo/servis
  davranışını değiştirdiğinde `grep -rn "<metot>" src/` ile çağıranları tara.
- **🚨 İmza değiştirmek ile gövdeyi kullanmak İKİ AYRI ADIMDIR.** Yeni bir
  alan/parametre eklerken çağrı zincirindeki **her katmanın gövdesini** elle izle.
  Yaşandı (Faz 20): `RunEventWriter.CompleteAsync`'e `cost` parametresi eklendi,
  nesne başlatıcıya `Cost = cost` yazılmadı — 1068 test yakalamadı.
- **🚨 Dört kapının dördünü de çalıştır.** `dotnet build` tek başına yeşil
  görünürken `dotnet format` 276 `IDE0055` hatası verdi (Faz 11 bu yüzden eksik
  kapandı). Kaynak üreteci build'in analyzer geçişinde tanıyı gizleyebilir.
- **Birim testi yetmez — örnek uygulamayı gerçekten çalıştır.** Faz 6, 12, 15, 16,
  18, 20, 21 ve 48'de gerçek hatalar **yalnız** orada ortaya çıktı; hepsi
  testlerden geçmişti. Faz 21'de 1231 test yeşilken iki hata çıktı (K-166, K-167).
- **🚨 Struct alanını atamamak `default` bırakır ve seri hâle getirme çöker.**
  Atanmayan `JsonElement` `Undefined` olur ve dönüştürücü fırlatır; etki tek
  kayıtla kalmaz, o kaydı içeren **liste ucunun tamamı** çöker. Yeni bir kayıt
  üreten her kod yolunda zorunlu olmayan alanları da doldurun. Ayrıntı:
  `docs/hafiza/cekirdek-calistirma.md`.
- **🚨 Senkronizasyon kopyaları (`<ad> 2.<uzantı>`) — DÖRT kez yaşandı (Faz 29, 30,
  41, 48).** Kopya gömülü varlık listesine karışır; `dotnet build` **yeşildir** ama
  arayüz hiç yüklenmez (Faz 41: 41 E2E testi 19 dk zaman aşımı) veya derleme
  gürültülü kırılır (Faz 48: `locales/tr 2.ts` → TS2741, K-228). `.cs` kopyaları
  CS0101 yağmuru üretir. Denetim (faz kapanışında zorunlu):
  `find src -name "* 2.*" -not -path "*/node_modules/*"` — çıktı boş olmalıdır.
  Silmek yetmez: `wwwroot`'u kaldırıp `agentprism-frontend.stamp` damgasını da sil.
- **🚨 `dotnet test` dakikalarca ASILI kalıyorsa alt süreç boru hatlarına bak.**
  Faz 47'de ölçüldü: yönlendirilmiş stdout ile çalışan `dotnet pack`'in öksüz
  MSBuild düğümleri (`nodeReuse:true`) boruyu açık tutuyor ve `WaitForExitAsync`
  **~15 dakika** bloke kalıyordu. Belirti: `ps`'te `dotnet pack` yok, yalnız
  öksüz `MSBuild.dll … /nodeReuse:true`. Çözüm `MSBUILDDISABLENODEREUSE=1`
  (8 dk+ → 18,5 sn). İkinci sebep: `-p:AgentPrismFrontendEnabled=false` ile
  derleyip **E2E** koşmak. Ayrıntı: `docs/hafiza/test-altyapisi.md`.
- **Bash'te `cd` kalıcıdır**; doğrulama komutlarında **mutlak yol** kullan.
- **`dotnet test` MTP'dir, VSTest değil.** `--filter-query` bir MSBuild anahtarı
  değildir (`MSB1001`). Tek test için derlenmiş çalıştırılabilir doğrudan koşulur:
  `./artifacts/bin/<Proje>/release/<Proje> --filter-method "*AdParcasi*"`
  (`--filter-class`/`--filter-namespace` de vardır). Bir testin gerçekten
  **bayat mı yoksa kusurlu mu** olduğunu ayırmanın yolu budur: aynı testi
  `git worktree add <dizin> HEAD` ile temel sürümde de izole koş.
- **🚨 Tool'un gördüğü servis sağlayıcı BOŞTUR.** MAF, `AIFunctionArguments.Services`
  olarak `EmptyServiceProvider` geçirir; bir tool bağımlılığını **kurulum anında**
  almalıdır (`new BenimTool(provider)` + fabrika kaydı). Aynı sebeple `AddToolsFrom`
  ile kaydedilen **örnek metot** tool'ları da çalışmaz. Faz 28'de ölçüldü; ayrıntı
  `docs/hafiza/cekirdek-calistirma.md`, karar K-218.
- **🚨 Bir prob programı gerçek boru hattını kanıtlamaz.** Faz 28'de ayrı bir konsol
  projesinde `AIFunctionArguments.Services` çalışıyordu (orada
  `FunctionInvokingChatClient` elle kurulmuştu); gerçek yolda çalışmıyordu.
  **İzole ölçüm, entegre davranışı kanıtlamaz.**
- **🚨 Planın YAPISAL iddiasını (katman, sıra, konum) kabul etmeden GREP'le ölç.**
  Faz 48'in planı guard'ı "boru hattının en dışına" koyuyordu; tek bir
  `grep -rn "UseFunctionInvocation" src/` o konumun tool çağrı turlarını
  göremediğini gösterdi ve fazın yarısı bir taşımaya dönüştü (K-320). Yanlış
  konum derlenir, testten geçer ve yalnız gerçek senaryoda çöker.
- **MAF ve OpenAI tip adlarını tahmin etme.** `AgentResponse` (`AgentRunResponse`
  değil), `ResponsesClient` (`OpenAIResponseClient` değil). Yeni tip kullanmadan
  önce `maf-api-kesfi` skill'ini çalıştır.

---

## Not Ekleme Kuralı

1. Not **alan dosyasına** yazılır (`docs/hafiza/<alan>.md`), buraya değil.
2. Buraya yalnız **alandan bağımsız** ve **tekrar bedel ödeten** bir ders eklenir.
3. `AGENTS.md` / `KARARLAR.md` / skill'lerde zaten yazılı olanı kopyalama.
4. Her not: tek satır–birkaç satır, dosya yolu, tarih (`YYYY-AA-GG`), faz numarası.
5. Bayatlayan notu güncelle veya sil.

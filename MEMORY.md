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
  18, 20 ve 21'de gerçek hatalar **yalnız** orada ortaya çıktı; hepsi testlerden
  geçmişti. Faz 21'de 1231 test yeşilken iki hata çıktı: `JobRecord.Payload`
  atanmadığı için `/api/jobs` tüm listeyi 500 ile döndürüyordu (K-166) ve
  `AllowInsecureHttp` loopback adresini açmadığı için yerel webhook teslimi
  imkânsızdı (K-167).
- **🚨 Struct alanını atamamak `default` bırakır ve seri hâle getirme çöker.**
  `JsonElement` atanmazsa `ValueKind = Undefined` olur ve
  `JsonElementConverter` istisna fırlatır — etki tek kayıtla sınırlı kalmaz,
  o kaydı içeren **liste ucunun tamamı** çöker. Yeni bir kayıt üreten her kod
  yolunda zorunlu olmayan alanları da doldurun. Ayrıntı:
  `docs/hafiza/cekirdek-calistirma.md`.
- **🚨 Senkronizasyon kopyaları (`<ad> 2.<uzantı>`) sessizce zehirler — üç kez
  yaşandı (Faz 29, 30, 41).** Kopya, gömülü varlık listesine karışır; `dotnet build`
  **yeşildir** ama arayüz hiç yüklenmez. Faz 41'de bedel en ağırdı: E2E'nin
  **41 testinin tamamı** 19 dakika boyunca zaman aşımına uğradı ("waiting for
  heading Dashboard"). Kopyalar silinip `wwwroot` temiz üretilince aynı koşum
  **37 saniyede** yeşile döndü. `.cs` kopyaları ayrıca CS0101 yağmuru üretir.
  Denetim (faz kapanışında zorunlu): `find src -name "* 2.*" -not -path "*/node_modules/*"`
  — çıktı boş olmalıdır. Silmek yetmez: `wwwroot`'u kaldırıp
  `agentprism-frontend.stamp` damgasını da silmeden build arayüzü yeniden üretmez.
- **Bash'te `cd` kalıcıdır.** Bir komutta dizin değiştirdiysen sonraki komut orada
  başlar. Doğrulama komutlarında **mutlak yol** kullan.
- **`dotnet test` MTP'dir, VSTest değil.** `--filter-query` bir MSBuild anahtarı
  değildir (`MSB1001`). Tek test koşmak için projeyi çalıştırıp çıktıyı grep'le.
- **🚨 Tool'un gördüğü servis sağlayıcı BOŞTUR.** MAF, `AIFunctionArguments.Services`
  olarak `EmptyServiceProvider` geçirir; bir tool bağımlılığını **kurulum anında**
  almalıdır (`new BenimTool(provider)` + fabrika kaydı). Aynı sebeple `AddToolsFrom`
  ile kaydedilen **örnek metot** tool'ları da çalışmaz. Faz 28'de ölçüldü; ayrıntı
  `docs/hafiza/cekirdek-calistirma.md`, karar K-218.
- **🚨 Bir prob programı gerçek boru hattını kanıtlamaz.** Faz 28'de ayrı bir konsol
  projesinde `AIFunctionArguments.Services` çalışıyordu — çünkü orada
  `FunctionInvokingChatClient` elle kurulmuştu. Gerçek yolda çalışmıyordu. Faz 27'nin
  "derleme yeşilliği hiçbir şey kanıtlamaz" dersinin kardeşi: **izole ölçüm, entegre
  davranışı kanıtlamaz.**
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

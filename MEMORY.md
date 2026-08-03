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
| SQL, migration, `jsonb`, sütun indeksi | [`docs/hafiza/postgresql.md`](docs/hafiza/postgresql.md) |
| Minimal API ucu, DI kaydı, HTTP davranışı | [`docs/hafiza/aspnetcore-di.md`](docs/hafiza/aspnetcore-di.md) |
| MSBuild, csproj, NuGet, AOT, analyzer tanısı | [`docs/hafiza/build-ve-analyzer.md`](docs/hafiza/build-ve-analyzer.md) |
| Test yazımı (xunit, Shouldly, Testcontainers, Playwright) | [`docs/hafiza/test-altyapisi.md`](docs/hafiza/test-altyapisi.md) |
| Arayüz (Vite, SPA rota, TS) | [`docs/hafiza/frontend.md`](docs/hafiza/frontend.md) |
| OpenAI / uyumlu sağlayıcı | [`docs/hafiza/openai-saglayici.md`](docs/hafiza/openai-saglayici.md) |
| `RunRecording` zinciri, sır süzgeci, metrik, sürüm | [`docs/hafiza/cekirdek-calistirma.md`](docs/hafiza/cekirdek-calistirma.md) |

Aradığın belirli bir şeyse dosyayı açmak yerine **grep** et:

```bash
grep -rn "AsyncLocal" docs/hafiza/
```

---

## Her Oturumda Geçerli (bunları oku)

Bunlar alana bağlı değildir; her fazda tekrar tekrar bedel ödettiler.

- **🚨 `AsyncLocal` yazımı çağırana geri akmaz — dört kez yaşandı.** Span (Faz 6),
  çalıştırma kapsamı (Faz 11), `async IAsyncEnumerable` gövdesi (Faz 12),
  `RunStreamingAsync`'in arka plan görevi (Faz 15). Kural: kapsam/span **çağıran
  metodun kendi gövdesinde** yazılır; akışlı yolda **her `MoveNextAsync` öncesi**
  tekrarlanır. Ayrıntı ve dört vaka: `docs/hafiza/cekirdek-calistirma.md`.
- **🚨 İmza değiştirmek ile gövdeyi kullanmak İKİ AYRI ADIMDIR.** Yeni bir
  alan/parametre eklerken çağrı zincirindeki **her katmanın gövdesini** elle izle.
  Yaşandı (Faz 20): `RunEventWriter.CompleteAsync`'e `cost` parametresi eklendi,
  nesne başlatıcıya `Cost = cost` yazılmadı — 1068 test yakalamadı.
- **🚨 Dört kapının dördünü de çalıştır.** `dotnet build` tek başına yeşil
  görünürken `dotnet format` 276 `IDE0055` hatası verdi (Faz 11 bu yüzden eksik
  kapandı). Kaynak üreteci build'in analyzer geçişinde tanıyı gizleyebilir.
- **Birim testi yetmez — örnek uygulamayı gerçekten çalıştır.** Faz 6, 12, 15, 16,
  18 ve 20'de gerçek hatalar **yalnız** orada ortaya çıktı; hepsi testlerden
  geçmişti.
- **Bash'te `cd` kalıcıdır.** Bir komutta dizin değiştirdiysen sonraki komut orada
  başlar. Doğrulama komutlarında **mutlak yol** kullan.
- **`dotnet test` MTP'dir, VSTest değil.** `--filter-query` bir MSBuild anahtarı
  değildir (`MSB1001`). Tek test koşmak için projeyi çalıştırıp çıktıyı grep'le.
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

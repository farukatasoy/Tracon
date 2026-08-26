# 34 — Tipli Yönetim İstemcisi ve CLI (`CLI`)

> **Alan kodu:** `CLI` · **Faz:** 83 · `eval` komutu Faz 115
> **Kaynak:** `src/AgentPrism.Client` · `src/AgentPrism.Cli` · `nswag.json` ·
> `scripts/nswag-*.py`
>
> `eval` komutunun ölçtüğü takım/vaka/`check` kurulumu bu dosyanın kapsamı
> **dışındadır** — o [`17-EVAL-VE-DENEYLER.md`](17-EVAL-VE-DENEYLER.md)'dedir.
> Buradaki case'ler yalnız komutun kendi mekaniğini (eşik, çıkış kodu,
> yoklama, scope) kanıtlar.
>
> Ortam kurulumu ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.
> Dosya numarası **34**'tür — planın yazıldığı anda "34" boştu ama kapanışta
> `33` [`33-DOKUMAN-KAPILARI.md`](33-DOKUMAN-KAPILARI.md) tarafından
> alınmıştı (Faz 80); bkz. faz dokümanının Plandan Sapmalar bölümü.

---

## Bu dosya neyi kanıtlar

`agentprism` global tool'unun `migrate`/`migrate status`/`health`
komutlarının **gerçek** bir veritabanına ve **gerçek** bir HTTP sunucusuna
karşı çalıştığını, `secret`'ın hiçbir çıktıda görünmediğini ve
`AgentPrism.Client`'ın `MapAgentPrism`'in özel önekiyle de doğru çalıştığını
kanıtlar.

## Koşmadan önce

```bash
dotnet tool restore   # nswag.json'daki üreteç için, yalnız istemci yeniden üretilecekse
dotnet build AgentPrism.slnx -c Release
dotnet tool install -g AgentPrism.Cli --add-source ./artifacts/package/release
```

Sağlık case'leri gerçek bir dinleyici ister:

```bash
cd samples/AgentPrism.Api
dotnet run -c Release --urls http://localhost:5081
```

---

## Case'ler

| # | Kod | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|---|
| 1 | `MT-CLI-001` | Boş bir SQLite dosyası | `agentprism migrate --provider sqlite --connection "Data Source=<dosya>"` | Uygulanan migration sayısı yazılır (`N applied`, `N > 0`); çıkış kodu `0` |
| 2 | `MT-CLI-002` | 1 numaralı case koştu | Aynı komut tekrar | `0 applied` yazılır; çıkış kodu `0` (idempotent) |
| 3 | `MT-CLI-003` | 1 numaralı case koştu | `agentprism migrate status --provider sqlite --connection ...` | `0 pending` yazılır; dosyanın değişim zamanı **değişmez** (yazma yok) |
| 4 | `MT-CLI-004` | Boş bir SQLite dosyası | `agentprism migrate status --provider sqlite --connection ...` (`migrate` hiç koşulmadan) | Bekleyen migration sayısı ve adları listelenir; `0 pending` **değildir** |
| 5 | `MT-CLI-005` | `samples/AgentPrism.Api` ayakta, kimlik doğrulama yapılandırılmamış | `agentprism health --url http://localhost:5081/agentprism` | Model sağlayıcı sağlık durumu satır satır yazılır (`ad: durum`); çıkış kodu `0` |
| 6 | `MT-CLI-006` | Aynı, ayrıca `--json` | Aynı komut + `--json` | Çıktı geçerli JSON'dur (`jq .` hata vermez) |
| 7 | `MT-CLI-007` | Aynı sunucu, geçersiz `--token FIX-TOKEN-02` | `agentprism health --url ... --token FIX-TOKEN-02` | `HTTP 401` okunur bir hataya çevrilir; çıkış kodu `0` **değil**; `FIX-TOKEN-02` çıktıda **geçmez** |
| 8 | `MT-CLI-008` | Sunucu **kapalı** | `agentprism health --url http://localhost:1/agentprism` | Bağlantı hatası okunur hataya çevrilir; komut **asılı kalmaz** (birkaç saniye içinde döner) |
| 9 | `MT-CLI-009` | `samples/AgentPrism.Api`, `app.MapAgentPrism("control")` ile başlatılmış | `agentprism health --url http://localhost:5081/control` | Sağlık durumu yazılır — önek soyma tasarımı (§83.3) kanıtlanır |
| 10 | `MT-CLI-010` | Herhangi bir komut, uydurma bir bağlantı dizesi/token ile | Çıktı ve varsa log dosyası okunur | Bağlantı dizesi ve token çıktıda **hiç geçmez** |
| 11 | `MT-CLI-011` | Temiz makine | `dotnet tool install -g AgentPrism.Cli` sonra `agentprism --help` | Dört komut listelenir; kurulum ek adım istemez |
| 12 | `MT-CLI-012` | 👤 insan gerekir | Yeni bir konsol uygulamasında `AgentPrism.Client` referanslanır, `AddAgentPrismClient` ile bir `AgentPrismApiClient` çözülür ve bir metot çağrılır | IntelliSense metot ve parametre adlarını gösterir; çağrı gerçek sunucudan yanıt döner |
| 13 | `MT-CLI-013` | Çalışan sunucu, hepsi geçen bir takım | `agentprism eval --url … --suite ok --min-pass-rate 1.0` | Çıkış `0`; çıktı `Passed/Total` yazar |
| 14 | `MT-CLI-014` | Bir vaka'sı düşen takım | Aynı komut | Çıkış **`3`**; çıktı **düşen vaka'nın kimliğini** yazar |
| 15 | `MT-CLI-015` | Aynı takım | `--max-failures 1` | Çıkış `0` — bir başarısızlığa tolerans var |
| 16 | `MT-CLI-016` | Aynı takım | `--min-pass-rate 1.0 --max-failures 5` | Çıkış `3` — ikisi birden sağlanmalı (VE, VEYA değil) |
| 17 | `MT-CLI-017` | Olmayan takım adı | `--suite yok` | Çıkış `2`; mesaj takım adını söyler, sunucu gövdesini yazmaz |
| 18 | `MT-CLI-018` | Sunucu kapalı | Herhangi bir `eval` komutu | Çıkış `2`; mesaj bağlantı hatasını söyler |
| 19 | `MT-CLI-019` | Uzun koşan (asla bitmeyen) bir takım | `--timeout 1` | Çıkış `2`; mesaj timeout süresini söyler; komut asılı kalmaz |
| 20 | `MT-CLI-020` | Yalnız `EvalsRead` scope'lu API anahtarı | Herhangi bir `eval` komutu | Çıkış `2`; mesaj **`RunsWrite`** scope'unu adıyla söyler |
| 21 | `MT-CLI-021` | Herhangi bir takım | `--json` | Ayrıştırılabilir JSON; çıkış kodu eşikten bağımsız doğru |
| 22 | `MT-CLI-022` | 👤 insan gerekir | Uzun koşan bir takımda koşum sırasında `Ctrl+C` | Komut hemen çıkar, `2` döner; asılı kalmaz |

## Otomasyon karşılığı

Case 1–4 ve 10, `tests/AgentPrism.Cli.FunctionalTests/MigrateCommandTests.cs`
ve `CliSecretRedactionTests.cs` içinde gerçek bir SQLite dosyasına karşı
otomatikleştirilmiştir. Case 5–9, `HealthCommandTests.cs` içinde gerçek bir
Kestrel dinleyicisine karşı otomatikleştirilmiştir (case 9'un karşılığı
`Reads_health_through_a_custom_MapAgentPrism_prefix`). Case 11 kapanışta elle
koşuldu: paketlenen tool gerçekten kuruldu, `agentprism --help` doğrulandı,
sonra kaldırıldı. Case 12 ve 22 tek 👤 case'leridir.

Case 13–21, `EvalCommandTests.cs` içinde gerçek bir Kestrel dinleyicisine ve
gerçek bir arka plan iş kuyruğuna karşı otomatikleştirilmiştir — takımın
kendisi bir `FakeModelProvider` (`AgentPrism.Testing`) ile çalışan bir kod
agent'ı ölçer, gerçek bir LLM gerekmez. `External_cancellation_breaks_the_poll_loop_immediately_instead_of_waiting_out_the_timeout`
case 22'nin dış iptal (Ctrl+C ile aynı token yolu) kısmını otomatik kanıtlar;
gerçek bir `Ctrl+C` tuş vuruşu yalnız 👤 ile doğrulanır.

# 34 — Tipli Yönetim İstemcisi ve CLI (`CLI`)

> **Alan kodu:** `CLI` · **Faz:** 83 · `eval` komutu Faz 115 · göreli kapı Faz 153 ·
> `state-check` komutu Faz 156
> **Kaynak:** `src/Tracon.Client` · `src/Tracon.Cli` · `nswag.json` ·
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

`tracon` global tool'unun `migrate`/`migrate status`/`state-check`/`health`
komutlarının **gerçek** bir veritabanına ve **gerçek** bir HTTP sunucusuna
karşı çalıştığını, `secret`'ın hiçbir çıktıda görünmediğini ve
`Tracon.Client`'ın `MapTracon`'in özel önekiyle de doğru çalıştığını
kanıtlar.

`state-check` case'leri (32–37) ayrıca **yazmadığını** kanıtlar: tablo anlık
görüntüsü öncesi ve sonrası birebir aynı olmalıdır. Bu, komutun tek sert
sözüdür — canlı bir veritabanına karşı koşulabilmesi buna dayanır.

## Koşmadan önce

```bash
dotnet tool restore   # nswag.json'daki üreteç için, yalnız istemci yeniden üretilecekse
dotnet build Tracon.slnx -c Release
dotnet tool install -g Tracon.Cli --add-source ./artifacts/package/release
```

Sağlık case'leri gerçek bir dinleyici ister:

```bash
cd samples/Tracon.Api
dotnet run -c Release --urls http://localhost:5081
```

---

## Case'ler

| # | Kod | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|---|
| 1 | `MT-CLI-001` | Boş bir SQLite dosyası | `tracon migrate --provider sqlite --connection "Data Source=<dosya>"` | Uygulanan migration sayısı yazılır (`N applied`, `N > 0`); çıkış kodu `0` |
| 2 | `MT-CLI-002` | 1 numaralı case koştu | Aynı komut tekrar | `0 applied` yazılır; çıkış kodu `0` (idempotent) |
| 3 | `MT-CLI-003` | 1 numaralı case koştu | `tracon migrate status --provider sqlite --connection ...` | `0 pending` yazılır; dosyanın değişim zamanı **değişmez** (yazma yok) |
| 4 | `MT-CLI-004` | Boş bir SQLite dosyası | `tracon migrate status --provider sqlite --connection ...` (`migrate` hiç koşulmadan) | Bekleyen migration sayısı ve adları listelenir; `0 pending` **değildir** |
| 5 | `MT-CLI-005` | `samples/Tracon.Api` ayakta, kimlik doğrulama yapılandırılmamış | `tracon health --url http://localhost:5081/tracon` | Model sağlayıcı sağlık durumu satır satır yazılır (`ad: durum`); çıkış kodu `0` |
| 6 | `MT-CLI-006` | Aynı, ayrıca `--json` | Aynı komut + `--json` | Çıktı geçerli JSON'dur (`jq .` hata vermez) |
| 7 | `MT-CLI-007` | Aynı sunucu, geçersiz `--token FIX-TOKEN-02` | `tracon health --url ... --token FIX-TOKEN-02` | `HTTP 401` okunur bir hataya çevrilir; çıkış kodu `0` **değil**; `FIX-TOKEN-02` çıktıda **geçmez** |
| 8 | `MT-CLI-008` | Sunucu **kapalı** | `tracon health --url http://localhost:1/tracon` | Bağlantı hatası okunur hataya çevrilir; komut **asılı kalmaz** (birkaç saniye içinde döner) |
| 9 | `MT-CLI-009` | `samples/Tracon.Api`, `app.MapTracon("control")` ile başlatılmış | `tracon health --url http://localhost:5081/control` | Sağlık durumu yazılır — önek soyma tasarımı (§83.3) kanıtlanır |
| 10 | `MT-CLI-010` | Herhangi bir komut, uydurma bir bağlantı dizesi/token ile | Çıktı ve varsa log dosyası okunur | Bağlantı dizesi ve token çıktıda **hiç geçmez** |
| 11 | `MT-CLI-011` | Temiz makine | `dotnet tool install -g Tracon.Cli` sonra `tracon --help` | Altı komut listelenir (`migrate`, `migrate status`, `state-check`, `health`, `agent-skill`, `eval`); kurulum ek adım istemez |
| 12 | `MT-CLI-012` | 👤 insan gerekir | Yeni bir konsol uygulamasında `Tracon.Client` referanslanır, `AddTraconClient` ile bir `TraconApiClient` çözülür ve bir metot çağrılır | IntelliSense metot ve parametre adlarını gösterir; çağrı gerçek sunucudan yanıt döner |
| 13 | `MT-CLI-013` | Çalışan sunucu, hepsi geçen bir takım | `tracon eval --url … --suite ok --min-pass-rate 1.0` | Çıkış `0`; çıktı `Passed/Total` yazar |
| 14 | `MT-CLI-014` | Bir vaka'sı düşen takım | Aynı komut | Çıkış **`3`**; çıktı **düşen vaka'nın kimliğini** yazar |
| 15 | `MT-CLI-015` | Aynı takım | `--max-failures 1` | Çıkış `0` — bir başarısızlığa tolerans var |
| 16 | `MT-CLI-016` | Aynı takım | `--min-pass-rate 1.0 --max-failures 5` | Çıkış `3` — ikisi birden sağlanmalı (VE, VEYA değil) |
| 17 | `MT-CLI-017` | Olmayan takım adı | `--suite yok` | Çıkış `2`; mesaj takım adını söyler, sunucu gövdesini yazmaz |
| 18 | `MT-CLI-018` | Sunucu kapalı | Herhangi bir `eval` komutu | Çıkış `2`; mesaj bağlantı hatasını söyler |
| 19 | `MT-CLI-019` | Uzun koşan (asla bitmeyen) bir takım | `--timeout 1` | Çıkış `2`; mesaj timeout süresini söyler; komut asılı kalmaz |
| 20 | `MT-CLI-020` | Yalnız `EvalsRead` scope'lu API anahtarı | Herhangi bir `eval` komutu | Çıkış `2`; mesaj **`RunsWrite`** scope'unu adıyla söyler |
| 21 | `MT-CLI-021` | Herhangi bir takım | `--json` | Ayrıştırılabilir JSON; çıkış kodu eşikten bağımsız doğru |
| 22 | `MT-CLI-022` | 👤 insan gerekir | Uzun koşan bir takımda koşum sırasında `Ctrl+C` | Komut hemen çıkar, `2` döner; asılı kalmaz |
| 23 | `MT-CLI-023` | Aynı takımın iki koşumu; ikincide bir vaka bozulmuş | `--baseline previous --max-regressions 0` | Çıkış **`3`**; `stdout` `N regressed` sayar, `stderr` bozulan vaka'yı kimliğiyle sayar |
| 24 | `MT-CLI-024` | Aynı takım, regresyon **yok** | Aynı komut | Çıkış `0`; `0 regressed` yazar |
| 25 | `MT-CLI-025` | Takım ilk kez koşuluyor (önceki koşum yok) | Aynı komut | Çıkış **`0`**; `stderr` "No earlier completed run" der — kapı **atlanır**, kırmızı yanmaz |
| 26 | `MT-CLI-026` | Taban çizgisi koşumunun `eval_case_results` satırları silinmiş (kısmen de olsa) | `--baseline <o runId> --max-regressions 0` | Çıkış **`4`** — `3` DEĞİL; `stderr` `409` der |
| 27 | `MT-CLI-027` | Herhangi bir takım | `--max-regressions 0` (`--baseline` **verilmeden**) | Çıkış **`1`**; mesaj `--baseline` ister. Sessiz no-op DEĞİL |
| 28 | `MT-CLI-028` | Herhangi bir takım | `--baseline yesterday` | Çıkış `1` — `runId` ya da `previous` olmalı |
| 29 | `MT-CLI-029` | İkinci koşumdan önce takıma yeni vaka eklenmiş ve o vaka düşüyor | `--baseline previous --max-regressions 0` | Çıkış `0`; `1 added`, `0 regressed` — takımı büyütmek kapıyı kırmızı yakmaz |
| 30 | `MT-CLI-030` | Regresyonlu iki koşum | `--json --baseline previous --max-regressions 0` | Çıkış `3`; `stdout` **tek başına ayrıştırılabilir JSON** (`\| jq .` çalışır); özet satırı `stderr`'dedir |
| 31 | `MT-CLI-031` | Başka bir takımın koşum id'si taban çizgisi verilir | `--baseline <o runId>` | Çıkış `4`; `stderr` `400` der |
| 32 | `MT-CLI-032` | Case 1 koştu; `sessions` tablosuna güncel kuşaklı iki satır elle eklenmiş | `tracon state-check --provider sqlite --connection "Data Source=<dosya>"` | Çıkış `0`; `generation 1: 2 row(s), readable by this build` yazılır |
| 33 | `MT-CLI-033` | Aynı veritabanında bir satırın `state_schema_version` değeri elle `99` yapılmış | Aynı komut | Çıkış **`3`**; `NOT readable by this build` ve okunamaz satır sayısı yazılır; satırın `state_schema_version` değeri **hâlâ `99`** |
| 34 | `MT-CLI-034` | Yanlış bağlantı dizesi (`Data Source=/no/such/dir/x.db`) | Aynı komut | Çıkış `2`; tek satırlık hata; bağlantı dizesi **yazdırılmaz**; yığın izi **yok** |
| 35 | `MT-CLI-035` | Dolu veritabanı, sekiz oturum satırı | `... --sample 5` | Çıktı `Sampled 5 row(s), at most 5 per generation` ve `This is a sample, not a survey` der; `all readable` / `every row` **demez** |
| 36 | `MT-CLI-036` | Dolu veritabanı | `state-check` koşumu **öncesi** ve **sonrası** tam tablo anlık görüntüsü alınır | İki anlık görüntü **birebir aynı** — hiçbir sütun, `updated_at` ve `version` dahil, değişmemiş |
| 37 | `MT-CLI-037` | 👤 insan gerekir | `docs-site` `reference/versioning` sayfasının "The supported upgrade window" bölümü okunur | Desteklenen atlama aralığı (aynı ana sürüm içinde her sürüm), dayanağı (gerçek koşumdan yakalanmış fixture'lar) ve **MAF sınırı** (gövde Tracon'in vaadi değildir) açıkça yazılıdır |
| 38 | `MT-CLI-038` | Örnek uygulama ayakta, bir agent kayıtlı | `TraconOpenAIResponsesStreamAsync` gövdesi `stream: true` ile çağrılır | Çerçeveler **sırayla** gelir (`response.created` → `response.completed`); her eleman TEK bir ham SSE çerçevesidir, tüm gövde değil; çökme yok |
| 39 | `MT-CLI-039` | Aynı | `TraconOpenAIChatCompletionsStreamAsync` gövdesi `stream: true` ile çağrılır | İlk çerçeve `chat.completion.chunk` taşır; **son** çerçeve `data: [DONE]`'dur |
| 40 | `MT-CLI-040` | Aynı | `TraconOpenAIResponsesAsync` gövdesi `stream: false` ile çağrılır | JSON belge döner (`object=response`, `status=completed`) — Faz 159 öncesi davranış **birebir** korunur |
| 41 | `MT-CLI-041` | Aynı | `TraconOpenAIResponsesAsync` gövdesi **`stream: true`** ile çağrılır | `TraconApiException`; mesaj `text/event-stream` aldığını söyler ve **`TraconOpenAIResponsesStreamAsync`**'i adıyla önerir. Opak "could not deserialize" **değil** |
| 42 | `MT-CLI-042` | Aynı | `TraconOpenAIResponsesStreamAsync` gövdesi **`stream: false`** ile çağrılır | `TraconApiException`; mesaj `application/json` aldığını söyler ve **`TraconOpenAIResponsesAsync`**'i adıyla önerir. **Sessiz boş akış değil** |
| 43 | `MT-CLI-043` | Aynı | `TraconOpenAIResponsesAsync(default)` — atanmamış `JsonElement` gövdesi | `ArgumentException`; `ParamName` = `body`; mesaj "uninitialized JsonElement" der. Serilestirici içindeki opak `InvalidOperationException` **değil** |
| 44 | `MT-CLI-044` | Aynı | `TraconRunAgentStreamAsync` çağrılır, **iki çerçeve sonra `break`** edilir | Akış durur; sonraki çerçeve gelmez; istisna yok; süreç asılı kalmaz (bağlantı serbest bırakılır) |
| 45 | `MT-CLI-045` | Aynı | `TraconRunAgentStreamAsync(...).WithCancellation(iptalEdilmişToken)` | `OperationCanceledException`; istek hiç gönderilmez — `[EnumeratorCancellation]` bağı çalışıyor |
| 46 | `MT-CLI-046` | Node.js veya tarayıcı; `@tracon/client` kurulu | `client.POST('/api/agents/{name}/run', { parseAs: 'stream' })` sonucu `readSse(response)` ile okunur | Çerçeveler `{ id, event, data }` olarak gelir; `: keep-alive` yorum blokları görünmez; çok satırlı `data` satır sonlarıyla birleşiktir |

### `state-check` case'leri için hazırlık (32–36)

Satırlar elle eklenir; komutun kendisi hiçbir satır yazmaz, bu yüzden veriyi
başka bir şey koymalıdır. SQLite tablo öneki varsayılan `tracon_`'dir.

```bash
DB=/tmp/mt-cli-state-check.db
tracon migrate --provider sqlite --connection "Data Source=$DB"

NOW=$(date -u +%Y-%m-%dT%H:%M:%S.0000000+00:00)
for ID in a b; do
  sqlite3 "$DB" "INSERT INTO tracon_sessions
    (id, tenant_id, agent_name, state, state_schema_version, state_maf_version, created_at, updated_at, version)
    VALUES ('$ID', 'default', 'test-agent', '{}', 1, '1.18.0', '$NOW', '$NOW', 1);"
done

# Case 36'nın anlık görüntüsü
snapshot() {
  sqlite3 "$DB" "SELECT id||'|'||tenant_id||'|'||agent_name||'|'||state||'|'||state_schema_version
                        ||'|'||COALESCE(state_maf_version,'')||'|'||created_at||'|'||updated_at
                        ||'|'||version||'|'||COALESCE(owner_id,'')
                 FROM tracon_sessions ORDER BY id;"
}
snapshot > /tmp/before.txt
tracon state-check --provider sqlite --connection "Data Source=$DB"
snapshot > /tmp/after.txt
diff /tmp/before.txt /tmp/after.txt    # boş olmalı

# Case 33
sqlite3 "$DB" "UPDATE tracon_sessions SET state_schema_version = 99 WHERE id = 'b';"
```

---

## Otomasyon karşılığı

Case 1–4 ve 10, `tests/Tracon.Cli.FunctionalTests/MigrateCommandTests.cs`
ve `CliSecretRedactionTests.cs` içinde gerçek bir SQLite dosyasına karşı
otomatikleştirilmiştir. Case 5–9, `HealthCommandTests.cs` içinde gerçek bir
Kestrel dinleyicisine karşı otomatikleştirilmiştir (case 9'un karşılığı
`Reads_health_through_a_custom_MapTracon_prefix`). Case 11 kapanışta elle
koşuldu: paketlenen tool gerçekten kuruldu, `tracon --help` doğrulandı,
sonra kaldırıldı. Case 12 ve 22 tek 👤 case'leridir.

Case 23–31, `EvalBaselineGateTests.cs` içinde aynı gerçek dinleyiciye karşı
otomatikleştirilmiştir; taban çizgisi koşumları `IEvalStore` üzerinden doğrudan
yazılır, çünkü sevk edilen `PUT /cases` her düzenlemede YENİ vaka kimliği atar
ve düzenlenmiş bir vaka regresyon değil `Added`+`Removed` olur. Case 26'nın
kısmi budama hâli sözleşme seviyesinde ölçülür
(`EvalStoreContract.DiffRunsAsync_throws_when_retention_removed_only_SOME_of_the_results`).

Case 13–21, `EvalCommandTests.cs` içinde gerçek bir Kestrel dinleyicisine ve
gerçek bir arka plan iş kuyruğuna karşı otomatikleştirilmiştir — takımın
kendisi bir `FakeModelProvider` (`Tracon.Testing`) ile çalışan bir kod
agent'ı ölçer, gerçek bir LLM gerekmez. `External_cancellation_breaks_the_poll_loop_immediately_instead_of_waiting_out_the_timeout`
case 22'nin dış iptal (Ctrl+C ile aynı token yolu) kısmını otomatik kanıtlar;
gerçek bir `Ctrl+C` tuş vuruşu yalnız 👤 ile doğrulanır.

Case 32–36, `tests/Tracon.Cli.FunctionalTests/StateCheckCommandTests.cs`
içinde gerçek bir SQLite dosyasına karşı otomatikleştirilmiştir; case 36'nın
"hiçbir şey yazmadı" iddiası orada da **tam tablo anlık görüntüsü**
karşılaştırmasıdır, satır sayısı karşılaştırması değil — `updated_at` veya
`version` sütununa dokunan bir ön kontrol satır sayısını yine korurdu.
Sayımın kiracıdan bağımsız olduğu ve örneklemin kuşak başına sınırlandığı
`tests/Tracon.Sqlite.IntegrationTests/StatePreflightTests.cs` içinde,
gerçek SQL'e karşı ölçülür. Case 37 tek 👤 case'idir.

Case 38–45, `tests/Tracon.AspNetCore.FunctionalTests/GeneratedClientSseTests.cs`
içinde gerçek bir sunucuya karşı otomatikleştirilmiştir; çerçeveleme kurallarının
kendisi (parçalanmış okuma, `\r\n` sınırı, keep-alive, sonlandırıcısız son çerçeve,
akış ortasında iptal) `tests/Tracon.Client.UnitTests/TraconApiClientSseTests.cs`
içindedir — bir `TestServer` düşmanca parçalama üretemez, o yüzden iki seviye de
gereklidir. Case 46, `packages/tracon-client/test/sse.test.ts` içinde çözücü
seviyesinde otomatiktir; **gerçek bir tarayıcıda** koşumu tek 👤 case'idir.

# Alan Hafizasi — Yonlendirme Indeksi

> **Bastan sona okunmaz.** `faz-baslangic` Adim 3'te, DOKUNACAGIN alani bulmak
> icin bakilir. Yalniz eslesen satirin dosyasini ac.
>
> Belirli bir sey ariyorsan bu tabloyu hic acma, dogrudan grep'le:
> `grep -rn "AsyncLocal" docs/hafiza/`
>
> Faz 90'da `MEMORY.md`'den ayrildi: tablo ALAN SAYISIYLA buyur (20 -> 27),
> `MEMORY.md`'nin tuzak listesi ise OGRENILEN DERSLE. Iki farkli buyume egrisi
> tek butcede sikisiyordu ve dosya %1 bosluga dusmustu -- `MIMARI.md` §7 ile
> ayni sekil (K-524). Bu dosya baslangic degil SORGU baglamidir: her oturumda
> degil, alan aranirken okunur.

| Dokunduğun iş | Oku |
|---|---|
| Bir şeyin nerede yaşadığı | [kod-haritasi](kod-haritasi.md) |
| MAF tipi, harness, `AIContextProvider`, skill | [maf-api](maf-api.md) |
| MAF oturumu, `ChatHistoryProvider`, Responses depolama | [maf-oturum](maf-oturum.md) |
| Workflow yürütme, executor kimliği, HITL | [workflows](workflows.md) |
| `RunRecording` zinciri, `scope`/span, olay, iptal | [cekirdek-calistirma](cekirdek-calistirma.md) |
| Tool onayı, yetkilendirme sırası, sunum genişleme noktası | [tool-onay-ve-yetkilendirme](tool-onay-ve-yetkilendirme.md) |
| Metrik, maliyet, kota, `secret` süzgeci, `Bind()` | [olcum-kota-ve-secenekler](olcum-kota-ve-secenekler.md) |
| Paylaşılan SQL katmanı, yeni sağlayıcı | [sql-saglayicilari](sql-saglayicilari.md) |
| Migration, `__migrations` defteri, göç kilidi | [sql-migration](sql-migration.md) |
| SQL Server tuzağı (parametre, sorgu, şema, upsert) | [sql-server-tuzaklari](sql-server-tuzaklari.md) |
| SQL Server'ı yerelde ayağa kaldırma | [sql-server-yerel-test](sql-server-yerel-test.md) |
| PostgreSQL (`jsonb`, sütun indeksi) | [postgresql](postgresql.md) |
| SQLite (indeks ad alanı, upsert, CLR tipi) | [sqlite](sqlite.md) |
| DI kaydı, `TryAdd` sırası, yaşam döngüsü | [aspnetcore-di](aspnetcore-di.md) |
| HTTP ucu, model bağlama, filtre sırası, WebSocket | [http-uc-tuzaklari](http-uc-tuzaklari.md) |
| Enum/alan JSON serileştirme, `.WithTags` | [aspnetcore-json](aspnetcore-json.md) |
| MSBuild, csproj, AOT, analyzer tanısı | [build-ve-analyzer](build-ve-analyzer.md) |
| Analyzer/üreteç **yazımı** (`APG*`) | [analyzer-yazimi](analyzer-yazimi.md) |
| `dotnet pack`, `buildTransitive/`, şablon, yerel feed tüketicisi | [paketleme-ve-dagitim](paketleme-ve-dagitim.md) |
| NSwag ile üretilen istemci (`AgentPrism.Client`/`@agentprism/client`) | [nswag-istemci-uretimi](nswag-istemci-uretimi.md) |
| Test **yazımı** (xunit, Shouldly, sözleşme, Playwright) | [test-altyapisi](test-altyapisi.md) |
| Test **koşumu** asılı/eksik (`dotnet test`, MSBuild) | [test-kosum-tuzaklari](test-kosum-tuzaklari.md) |
| Test tek başına geçip **tam koşumda** düşüyor | [test-yalitimi](test-yalitimi.md) |
| Kapanış kapısı taban ölçümü (wall-clock, proje sonucu) | [test-kosum-olcumleri](test-kosum-olcumleri.md) |
| Arayüz (Vite, SPA rota, TS, ekran) | [frontend](frontend.md) |
| Arayüz yerelleştirme (`useT`, `Messages`, `Intl`) | [frontend-yerellestirme](frontend-yerellestirme.md) |
| Model sağlayıcısı (OpenAI, Anthropic, Google) | [openai-saglayici](openai-saglayici.md) |
| `IChatClient` dekoratörü, devre kesici | [model-boru-hatti](model-boru-hatti.md) |
| Ses tool'ları, konuşma katmanı | [ses-ve-konusma](ses-ve-konusma.md) |
| Dışa açılan MCP/A2A sunucusu | [mcp-a2a-sunucu](mcp-a2a-sunucu.md) |
| Sevk edilen doküman metni, metin kapısı yazımı, ekran görüntüsü | [dokumantasyon](dokumantasyon.md) |
| Site üretim betikleri (`build-agent-map.mjs`, `docfx`) ve kapıları | [site-uretim-kapilari](site-uretim-kapilari.md) |
| Site yayını (`site-deploy.sh`), Starlight teması | [site-yayin-ve-tema](site-yayin-ve-tema.md) |

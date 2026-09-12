# Alan Hafizasi — Yonlendirme Indeksi

> **Bastan sona okunmaz.** `faz-baslangic` Adim 3'te, DOKUNACAGIN alani bulmak
> icin bakilir. Yalniz eslesen satirin dosyasini ac.
>
> Belirli bir sey ariyorsan bu tabloyu hic acma, dogrudan grep'le:
> `grep -rn "AsyncLocal" docs/hafiza/`
>
> Faz 90'da `MEMORY.md`'den ayrildi (K-524, gerekce: `MIMARI.md` §7 ile ayni
> "iki buyume egrisi tek butcede sikisiyordu" deseni). Bu dosya baslangic degil
> SORGU baglamidir: her oturumda degil, alan aranirken okunur.

| Dokunduğun iş | Oku |
|---|---|
| Bir şeyin nerede yaşadığı | [kod-haritasi](kod-haritasi.md) |
| Graph/Compaction/Scheduling/Eval/Quota/Webhook/MCP dosya haritası | [altyapi-haritasi](altyapi-haritasi.md) |
| MAF tipi, harness, `AIContextProvider`, skill | [maf-api](maf-api.md) |
| MAF Harness/`LoopAgent`/`BackgroundAgents` bekleme tuzağı | [maf-harness-ve-loop](maf-harness-ve-loop.md) |
| MAF oturumu, `ChatHistoryProvider`, Responses depolama | [maf-oturum](maf-oturum.md) |
| Workflow yürütme, executor kimliği, HITL | [workflows](workflows.md) |
| `RunRecording` zinciri, `scope`/span, olay, iptal | [cekirdek-calistirma](cekirdek-calistirma.md) |
| `RunEventType`/`RunStatus` enum ve terminal durum sözleşmesi | [run-olay-sozlesmesi](run-olay-sozlesmesi.md) |
| Tool onayı, yetkilendirme sırası, sunum genişleme noktası | [tool-onay-ve-yetkilendirme](tool-onay-ve-yetkilendirme.md) |
| Metrik, maliyet, kota, `secret` süzgeci, `Bind()` | [olcum-kota-ve-secenekler](olcum-kota-ve-secenekler.md) |
| Genişleme noktası kaydı, denetim izi kapsamı | [genisleme-noktalari-ve-denetim](genisleme-noktalari-ve-denetim.md) |
| Paylaşılan SQL katmanı, yeni sağlayıcı | [sql-saglayicilari](sql-saglayicilari.md) |
| Paylaşılan sorgu ÜRETİM mekaniği (`BuildSharedQueries`, `RunOrdinals`) | [sql-paylasilan-sorgu-uretimi](sql-paylasilan-sorgu-uretimi.md) |
| Migration, `__migrations` defteri, göç kilidi | [sql-migration](sql-migration.md) |
| SQL Server tuzağı (parametre, sorgu, şema, upsert) | [sql-server-tuzaklari](sql-server-tuzaklari.md) |
| SQL Server'ı yerelde ayağa kaldırma | [sql-server-yerel-test](sql-server-yerel-test.md) |
| PostgreSQL (`jsonb`, sütun indeksi) | [postgresql](postgresql.md) |
| SQLite (indeks ad alanı, upsert, CLR tipi) | [sqlite](sqlite.md) |
| DI kaydı, `TryAdd` sırası, yaşam döngüsü | [aspnetcore-di](aspnetcore-di.md) |
| HTTP ucu, model bağlama, filtre sırası, WebSocket | [http-uc-tuzaklari](http-uc-tuzaklari.md) |
| HTTP kiracı önceliği, CORS, egress DNS, OpenAPI/enum sözleşmesi | [http-uc-guvenlik-ve-sozlesme](http-uc-guvenlik-ve-sozlesme.md) |
| Enum/alan JSON serileştirme, `.WithTags` | [aspnetcore-json](aspnetcore-json.md) |
| MSBuild, csproj, AOT | [build-ve-analyzer](build-ve-analyzer.md) |
| Analyzer/lint TANI kodu (RS/MA/CA) ile karşılaşma | [analyzer-tanilari](analyzer-tanilari.md) |
| Analyzer/üreteç **yazımı** (`APG*`) | [analyzer-yazimi](analyzer-yazimi.md) |
| `dotnet pack`, `buildTransitive/`, şablon, yerel feed tüketicisi | [paketleme-ve-dagitim](paketleme-ve-dagitim.md) |
| MinVer sürümleme, repo dışı tüketiciyi yerel feed'e bağlama | [yayin-ve-surumleme](yayin-ve-surumleme.md) |
| NSwag ile üretilen istemci (`Tracon.Client`/`@tracon/client`) | [nswag-istemci-uretimi](nswag-istemci-uretimi.md) |
| Test **yazımı** (xunit, Shouldly, sözleşme, Playwright) | [test-altyapisi](test-altyapisi.md) |
| Test **koşumu** asılı/eksik (`dotnet test`, MSBuild) | [test-kosum-tuzaklari](test-kosum-tuzaklari.md) |
| Test tek başına geçip **tam koşumda** düşüyor | [test-yalitimi](test-yalitimi.md) |
| Bir testin tek başına geçip tam koşumda düştüğü **ölçülmüş vakalar** | [test-yalitimi-vakalari](test-yalitimi-vakalari.md) |
| Kapanış kapısı taban ölçümü (wall-clock, proje sonucu) | [test-kosum-olcumleri](test-kosum-olcumleri.md) |
| Arayüz (Vite, SPA rota, TS, ekran) | [frontend](frontend.md) |
| Arayüz tasarım katmanı (token, tema, yoğunluk, primitif, erişilebilirlik) | [frontend-tasarim-katmani](frontend-tasarim-katmani.md) |
| Arayüz yerelleştirme (`useT`, `Messages`, `Intl`) | [frontend-yerellestirme](frontend-yerellestirme.md) |
| Arayüz test altyapısı (Vitest component, `openapi-fetch` stub) | [frontend-test-altyapisi](frontend-test-altyapisi.md) |
| Model sağlayıcısı (OpenAI, Anthropic, Google) | [openai-saglayici](openai-saglayici.md) |
| `IChatClient` dekoratörü, devre kesici | [model-boru-hatti](model-boru-hatti.md) |
| `ContentGuard` fail-closed, DI döngüsü, sağlayıcı adı kaydı | [icerik-koruma-ve-saglayici-kayit](icerik-koruma-ve-saglayici-kayit.md) |
| Ses tool'ları, konuşma katmanı | [ses-ve-konusma](ses-ve-konusma.md) |
| Dışa açılan MCP/A2A sunucusu | [mcp-a2a-sunucu](mcp-a2a-sunucu.md) |
| MCP Tasks uzantısı (`ModelContextProtocol.Extensions.Tasks`) | [mcp-tasks](mcp-tasks.md) |
| Marka hikayesi, metafor, ses ve tonu; kullanıcıya dönük metin | [marka](marka.md) |
| Sevk edilen doküman metni, metin kapısı yazımı, ekran görüntüsü | [dokumantasyon](dokumantasyon.md) |
| Site üretim betikleri (`build-agent-map.mjs`, `docfx`) ve kapıları | [site-uretim-kapilari](site-uretim-kapilari.md) |
| Site yayını (`site-deploy.sh`), Starlight teması | [site-yayin-ve-tema](site-yayin-ve-tema.md) |

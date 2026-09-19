# Alan Hafizasi — Yonlendirme Indeksi

> **Bastan sona okunmaz.** `faz-baslangic` Adim 3'te, DOKUNACAGIN alani bulmak
> icin bakilir; yalniz eslesen satirin dosyasini ac. Belirli bir sey ariyorsan
> tabloyu hic acma, dogrudan grep'le: `grep -rn "AsyncLocal" docs/hafiza/`.
> Bu dosya baslangic degil SORGU baglamidir (K-524, Faz 90).

| Dokunduğun iş | Oku |
|---|---|
| Bir şeyin nerede yaşadığı | [kod-haritasi](kod-haritasi.md) |
| Graph/Compaction/Eval/Quota/Webhook/MCP haritası | [altyapi-haritasi](altyapi-haritasi.md) |
| MAF tipi, harness, `AIContextProvider` | [maf-api](maf-api.md) |
| MAF Harness/`LoopAgent` bekleme tuzağı | [maf-harness-ve-loop](maf-harness-ve-loop.md) |
| MAF oturumu, `ChatHistoryProvider`, Responses | [maf-oturum](maf-oturum.md) |
| Workflow yürütme, executor kimliği, HITL | [workflows](workflows.md) |
| `RunRecording` zinciri, `scope`/span, iptal | [cekirdek-calistirma](cekirdek-calistirma.md) |
| Agent derleme, derleme kimliği, derlenmiş agent önbelleği | [agent-derleme-ve-onbellek](agent-derleme-ve-onbellek.md) |
| `RunEventType`/`RunStatus` terminal durum sözleşmesi | [run-olay-sozlesmesi](run-olay-sozlesmesi.md) |
| Tool onayı, yetkilendirme sırası, sunum seam'i | [tool-onay-ve-yetkilendirme](tool-onay-ve-yetkilendirme.md) |
| Metrik, maliyet, kota, rapor dürüstlüğü | [olcum-kota-ve-secenekler](olcum-kota-ve-secenekler.md) |
| `Bind()` seçenek bağlama, `secret` süzgeci | [secenek-baglama-ve-gizlilik](secenek-baglama-ve-gizlilik.md) |
| Genişleme noktası kaydı, denetim izi kapsamı | [genisleme-noktalari-ve-denetim](genisleme-noktalari-ve-denetim.md) |
| Paylaşılan SQL katmanı, yeni sağlayıcı | [sql-saglayicilari](sql-saglayicilari.md) |
| Paylaşılan sorgu ÜRETİM mekaniği | [sql-paylasilan-sorgu-uretimi](sql-paylasilan-sorgu-uretimi.md) |
| Migration, `__migrations`, göç kilidi | [sql-migration](sql-migration.md) |
| SQL Server tuzağı (parametre, sorgu, şema) | [sql-server-tuzaklari](sql-server-tuzaklari.md) |
| SQL Server'ı yerelde ayağa kaldırma | [sql-server-yerel-test](sql-server-yerel-test.md) |
| PostgreSQL (`jsonb`, sütun indeksi) | [postgresql](postgresql.md) |
| SQLite (indeks ad alanı, upsert, CLR tipi) | [sqlite](sqlite.md) |
| DI kaydı, `TryAdd` sırası, ömür | [aspnetcore-di](aspnetcore-di.md) |
| HTTP ucu, model bağlama, filtre sırası | [http-uc-tuzaklari](http-uc-tuzaklari.md) |
| HTTP kiracı önceliği, CORS, egress, OpenAPI | [http-uc-guvenlik-ve-sozlesme](http-uc-guvenlik-ve-sozlesme.md) |
| Enum/alan JSON serileştirme | [aspnetcore-json](aspnetcore-json.md) |
| MSBuild, csproj, AOT | [build-ve-analyzer](build-ve-analyzer.md) |
| Analyzer/lint TANI kodu (RS/MA/CA) | [analyzer-tanilari](analyzer-tanilari.md) |
| Analyzer/üreteç **yazımı** (`APG*`) | [analyzer-yazimi](analyzer-yazimi.md) |
| `dotnet pack`, `buildTransitive/`, şablon, feed | [paketleme-ve-dagitim](paketleme-ve-dagitim.md) |
| MinVer sürümleme, repo dışı tüketiciyi bağlama | [yayin-ve-surumleme](yayin-ve-surumleme.md) |
| NSwag ile üretilen istemci (`Tracon.Client`/`@tracon/client`) | [nswag-istemci-uretimi](nswag-istemci-uretimi.md) |
| Test **yazımı** (xunit, sözleşme, Playwright) | [test-altyapisi](test-altyapisi.md) |
| Test **koşumu** asılı/eksik (`dotnet test`) | [komut/MSBuild](test-kosum-tuzaklari.md) · [paralellik/zamanlama](test-paralellik-ve-zamanlama.md) |
| Örnek uygulamayı **elle** ayağa kaldırma (manuel tur, repro) | [elle-kosum-ortami](elle-kosum-ortami.md) |
| Test tek başına geçip **tam koşumda** düşüyor | [test-yalitimi](test-yalitimi.md) |
| Tam koşumda düşen testin **ölçülmüş vakaları** | [test-yalitimi-vakalari](test-yalitimi-vakalari.md) |
| Kapanış kapısı taban ölçümü (wall-clock) | [test-kosum-olcumleri](test-kosum-olcumleri.md) |
| Kapasite ölçümü aparatı (`bench/capacity/`) | [kapasite-olcumu](kapasite-olcumu.md) |
| Arayüz (Vite, SPA rota, TS, ekran) | [frontend](frontend.md) |
| Arayüz tasarım katmanı (token, tema, primitif) | [frontend-tasarim-katmani](frontend-tasarim-katmani.md) |
| Arayüz modal katmanı (`Dialog`, doğrulama adımı, `window.confirm` yasağı) | [frontend-modal-katmani](frontend-modal-katmani.md) |
| Arayüz yerelleştirme (`useT`, `Messages`) | [frontend-yerellestirme](frontend-yerellestirme.md) |
| Arayüz test altyapısı (Vitest, `openapi-fetch` stub) | [frontend-test-altyapisi](frontend-test-altyapisi.md) |
| Model sağlayıcısı (OpenAI/Anthropic/Google) | [openai-saglayici](openai-saglayici.md) |
| `IChatClient` dekoratörü, devre kesici | [model-boru-hatti](model-boru-hatti.md) |
| `ContentGuard` fail-closed, DI döngüsü, sağlayıcı kaydı | [icerik-koruma-ve-saglayici-kayit](icerik-koruma-ve-saglayici-kayit.md) |
| Ses tool'ları, konuşma katmanı | [ses-ve-konusma](ses-ve-konusma.md) |
| Dışa açılan MCP/A2A sunucusu | [mcp-a2a-sunucu](mcp-a2a-sunucu.md) |
| MCP Tasks uzantısı | [mcp-tasks](mcp-tasks.md) |
| Marka hikayesi, metafor, ses ve ton; kullanıcı metni | [marka](marka.md) |
| Sevk edilen doküman metni, metin kapısı, ekran | [dokumantasyon](dokumantasyon.md) |
| Karar defteri bakımı, indeks üretimi, faz arşivleme, kapı KAPSAMI | [defter-bakimi](defter-bakimi.md) |
| Site üretim betikleri (`build-agent-map.mjs`, `docfx`) | [site-uretim-kapilari](site-uretim-kapilari.md) |
| Site yayını (`site-deploy.sh`), Starlight | [site-yayin-ve-tema](site-yayin-ve-tema.md) |
| Site metni yazımı (markdown, makine okuyucusu) | [site-icerik-yazimi](site-icerik-yazimi.md) |

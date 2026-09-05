# Keşif Turu — 2026-09-05 · ProdigyEnabler tüketici turu 4

> Bu bir **koşum kaydıdır**, spec değildir. Sıcak yolda değildir ve baştan sona
> okunmaz. Plana dönen kalemlerin tam metni faz dokümanlarındadır; bu dosya
> yalnız oraya işaret eder.

**Tetikleyen:** Kullanıcı, ProdigyEnabler'ın 2026-09-05 tarihli iki belgesini
(feature talepleri: A1 · A2 · F1–F8; uygulanabilirlik raporu: 28 karar,
R1–R23 risk) koda karşı ölçmeyi, sonra çıkan kalemleri `faz-planlama` ile
plana çevirmeyi istedi.
**Zemin:** Faz 144 kapalı · sıralamada tek aday (F-192) · en büyük numara F-192
**İncelenen sürüm:** `0.0.0-preview.0.589` · source commit `234d4081` — **bu
depo HEAD'iyle aynı**, çalışma ağacı temiz
**Ekosistem taraması:** yapılmadı — hiçbir kalem "MAF/ekosistem bunu artık
veriyor" iddiası taşımıyor; hepsi AgentPrism'in kendi yüzeyine dair.

---

## 1. Ölçülen zemin

| Kaynak | Bulgu |
|---|---|
| `arsiv/KARARLAR-INDEKS-REDDEDILEN.md` | A1 · A2 · F1–F8 konularında **eşleşme yok**. Bu turda daha önce reddedilmiş bir iş yeniden önerilmiyor |
| Faz 139 (turu 3, A1 + A8) | Kapıyı sevk etti. Devir notu iki şeyi **açıkça devretti**: yeni run yüzeyinin kapıyı kendi gövdesinde çağırması, ve sesin kapsam dışı bırakılması |
| Faz 139 · Plandan Sapmalar 5 | *"Bu fazda kalıcı bir session-sahiplik veri modeli yok"* — A1'in ikinci yarısı orada bilerek açık bırakılmış |
| Faz 141 | `RunEventType.Custom` + `CustomType` sevk edildi; F1 ve A2 bunun üstüne oturuyor |
| K-273 (Faz 40) | F1'in MIME yarısını **zaten ölçmüş**: `.Produces(...)` `responseType` verilmeden içerik tipini sessizce düşürür |
| `RunEventTypeFrontendParityTests` | C# enum ↔ TS union kapısı zaten var; F1'in kapısı bunun üçüncü halkasıdır |
| `RunAuthorizationCoverageTests` | Dört run başlatan dosyayı sayıyor; kendi dokümanı beşinciyi bulamayacağını söylüyor |

---

## 2. Raporun güvenilirliği

Tüketici kaynak gösterdi ve "yok" yerine "bulunamadı" dedi. Ölçüm sonucu:

| Ölçülen iddia | Sonuç |
|---|---|
| `SessionRecord` owner alanı taşımaz | ✅ Doğru (`ISessionStore.cs:179`) |
| `SessionQuery` user filtresi taşımaz | ✅ Doğru (`ISessionStore.cs:254`) |
| `RunAccess` yalnız `Start` | ✅ Doğru (`RunAuthorizationTypes.cs`) |
| Run okuma grafiğinde auth hook yok | ✅ Doğru — `RunEndpoints.cs` (992 satır) içinde kapı çağrısı **sıfır**; approval ve attachment da sıfır |
| Voice yalnız kiracı denetler | ✅ Doğru (`VoiceConversationEndpoint.cs:262`) |
| `WebhookQuotaSummary` run/user korelasyonu taşımaz | ✅ Doğru (`WebhookEventPayload.cs:138`) |
| `QuotaConsumption` de taşımaz | ✅ Doğru (`RunRecordingAgent.Notifications.cs:32`) |
| Terminal, kota kaydından önce yazılıyor | ✅ Doğru (`Completion.cs:132` → `:162`) |
| `RunEventType`'ta kota eşiği yok | ✅ Doğru (31 üyenin hiçbiri) |
| `QuotaEnforcer` eşik hafızası süreç içi | ✅ Doğru (`QuotaEnforcer.cs:39`, `ConcurrentDictionary`) |
| `AgentPrismStreamRunEvents` MIME bildirmiyor | ✅ Doğru — `{"200": {"description": "OK"}}`; ailedeki **tek** bildirmeyen SSE ucu |
| `RunEventType.Custom` `unknown` adıyla taşınıyor | ✅ Doğru — 31 üyenin **21'i** `unknown` (`RunEndpoints.cs:973`) |
| `ExtensionPointDiagnostic` kayıt bilgisini gösterir (F2) | ✅ Doğru; eksik olan yalnız **zorlama** anahtarı |
| `SqlPersistenceDiagnosticsSnapshot` yalnız ad taşır (F7) | ✅ Doğru — `CanConnect` + `PendingMigrations` |
| `T:AgentPrism.MigrationDescriptor` public bir tiptir (R13) | ❌ **Yanlış** — `internal sealed record` (`MigrationDescriptor.cs:13`). Hash'ler var ama dışarıdan görünmüyor |

Bir iddia yanlış çıktı ve o da bir feature talebini değil bir risk satırını
etkiliyor. Rapor bu turda da **yüksek doğrulukta**.

---

## 3. Ölçümün raporda olmayan iki bulgusu

Tüketici yalnız **okuma** grafiğini ölçtü. Planlama ölçümü iki **run başlatan**
yüzeyin de kapısız olduğunu buldu:

| Yüzey | Kanıt |
|---|---|
| `POST /api/runs/{id}/replay` | Kendi özeti *"Starts a new run with recorded input."*; `RunReplayService` katalogdan agent çözüyor; `RunEndpoints.cs` içinde kapı çağrısı **sıfır** |
| `/v1/chat/completions` | `OpenAIChatCompletionsEndpoints.cs:107` katalogdan agent çözüyor, `:138`/`:301` `RunAsync`/`RunStreamingAsync` çağırıyor; kapı çağrısı **sıfır** |

Faz 139'un "dört run başlatan yüzey" iddiası (K-670) bu yüzden **eksiktir**;
gerçek sayı **altıdır**. Bu, o fazın kendi devir notunun uyardığı sınıftır ve
Faz 147'nin kapsamına alındı.

---

## 4. Kalemlerin varış yeri

| Kalem | Karar | Faz |
|---|---|---|
| **F1** → F-193 | Plana | [Faz 145 — Kayıtlı Olay Akışının Çerçeve Sözleşmesi](../arsiv/fazlar/145-OLAY-AKISININ-CERCEVE-SOZLESMESI.md) |
| **A2 + F3** → F-194 | Plana (birleşti — aynı kota altyapısı, Faz 21 emsali) | [Faz 146 — Çalıştırmaya Bağlı Kota Eşiği](../arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md) |
| **A1 · kapsam** → F-195 | Plana | [Faz 147 — Yetkilendirme Kapısının Kaynak Kapsamı](../arsiv/fazlar/147-YETKI-KAPISININ-KAYNAK-KAPSAMI.md) |
| **A1 · sahiplik** → F-196 | Plana | [Faz 148 — Oturum Sahipliğinin Kalıcılığı](../148-OTURUM-SAHIPLIGININ-KALICILIGI.md) |
| **F2** · zorunlu binding profili | ⏸ Sıralanmadı | Bilgi zaten `ExtensionPointDiagnostic`'te; emsal `RequireRolePolicies`. Küçük ve gerçek, ama talep kanıtı yok — tüketici bunu bir "fikir" diye sundu |
| **F7** · migration plan artifact'i | ⏸ Sıralanmadı | Boşluk gerçek ama değeri düşük; raporun kendi kanıtı (`MigrationDescriptor` public) yanlış çıktı |
| **F4** · yazma hacmi planlayıcısı | ❌ Elendi | Tüketicinin kendisi "GB/ay vaat edilmemeli" diyor; ölçülmüş talep yok |
| **F5** · çok dilli guard corpus'u | ❌ Elendi | Faz 140 `ContentGuardContext.Source` ayrımını sevk etti; kalan iş test verisi bakımıdır, faz değil |
| **F6** · ses/WebSocket harness'i | ⏸ Sıralanmadı | Boşluk gerçek (`AgentPrism.Testing` içinde ses fixture'ı yok) ama tüketici VAD/WebSocket'i kendi üstlendi; talep kanıtı yok |
| **F8** · generator kontrat paketi | ❌ Elendi | Asıl şikâyet beklenti/doküman; Faz 127 kısmen kapattı |

**Faz sırası ve gerekçesi:** 145 → 146 → 147 → 148.
145 hiçbir şeyi beklemiyor ve sevk edilmiş bir sözleşmenin yarısını kapatıyor;
146 notice'ı `Custom` çerçevesi olarak yazdığı için 145'i bekliyor;
148 daralan liste ile açık kalan `run` okuması bir arada yanlış güvenlik hissi
üreteceği için 147'yi bekliyor.

---

## 5. Plan anında sorulan bloklayıcı kararlar

Dördü de kullanıcıya soruldu ve dördünde de öneri seçildi (2026-09-05):

| Karar | Seçim |
|---|---|
| SSE ad kuralı | Açık tablo + tamlık kapısı; `Custom` sabit `custom` çerçevesi alır. Enum adından mekanik türetme **reddedildi** — bir C# rename wire'ı sessizce değiştirirdi |
| Kota sırası | Muhasebe terminalden öne alınır; `RecordAsync` geçilen eşiği döner |
| Yetki biçimi | Var olan `RunAccess`/`SessionAccess` enum'ları büyür; ayrı sözleşme **reddedildi** |
| Sahiplik deposu | `sessions`'a `owner_id` sütunu, mod opt-in; owner'sız eski satırlar son kullanıcıya görünmez |

---

## 6. Bu turda yapılmayanlar

- Kod yazılmadı. `faz-planlama` yalnız doküman üretir.
- Doğrulama kapıları koşulmadı — bu fazların uygulama anına aittir.
- Tüketiciye yanıt belgesi yazılmadı. Önceki turlarda yanıt ayrı bir dosyaydı
  (`2026-09-04-tuketici-turu-3-yaniti.md`); bu tur için henüz istenmedi.
- F2 · F6 · F7 için aday gövdesi yazılmadı; koşulları oluşursa `aday-kesfi`
  ile yeniden yargılanırlar.

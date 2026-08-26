# Faz 109 — Frontend Modülleri ve Ekran Testleri

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [`kesif/2026-08-23-yapisal-sorun-envanteri.md`](../../kesif/2026-08-23-yapisal-sorun-envanteri.md) — **kalem 18** ve **kalem 19**. Bu faz bir `F-NN` adayından gelmez
> **Önkoşul:** [Faz 108](108-BELLEK-ICI-RUN-STORE-AYRISTIRMA.md) — teknik zorunluluk yoktur; yapısal turun Core bölümü bittikten sonra frontend'e geçilir
> **Paketler:** `AgentPrism.UI` — yalnız frontend source ve test altyapısı
> **Yeni paket:** NuGet yok · npm runtime dependency yok · dört dev dependency: `@testing-library/react`, `@testing-library/dom`, `@testing-library/user-event`, `jsdom` · **Migration:** Yok
> **Public API:** Büyümüyor. C# ve HTTP contract değişmez
> **Tüketici yüzeyi:** Var — mevcut management console ekranları. Site: [`docs-site/src/content/docs/ui.md`](../../../docs-site/src/content/docs/ui.md) ve `docs-site/public/screenshots/`. Sevk edilen: `AgentPrism.UI` içindeki embedded asset'ler. Görsel ve metinsel davranışın değişmemesi hedeflenir
> **Manuel test alanı:** [`manuel-test/09-ARAYUZ-GENEL.md`](../../manuel-test/09-ARAYUZ-GENEL.md) · [`manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md`](../../manuel-test/10-ARAYUZ-AGENT-PLAYGROUND.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 1c68c9a:docs/arsiv/fazlar/109-FRONTEND-MODULLERI-VE-EKRAN-TESTLERI.md
> ```
>
> Damıtıldı 2026-08-26 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Faz iki bağlı sorunu birlikte kapatır. Büyük screen dosyaları state, network ve view sorumluluklarını ayırır. İki dil sözlüğü domain fragment'larına bölünür. Aynı modül sınırları component-test harness'ine girer; her routed screen en az bir otomatik render senaryosu kazanır.

## Bitiş Ölçütleri (DoD)

- [x] `agent-editor.tsx` ve `playground.tsx` route facade olur; state/network/view sorumlulukları kendi modüllerindedir
- [x] `en.ts` ve `tr.ts` yalnız fragment aggregate eder; message literal monoliti kalmaz
- [x] Fragment key'leri unique, iki dilde tam ve placeholder/plural sözleşmesi eşittir
- [x] Router'daki **28** screen dosyasının tamamı, `app.tsx#routes`'un **36** route pattern'ının tamamı üzerinden data-driven component smoke testine girer (`app.test.tsx`)
- [x] Agent editor ve Playground için belirtilen branch testleri yeşildir
- [x] `npm run build` `tsc`, Vitest, Vite ve iki bundle kapısını temiz geçirir
- [x] Console JavaScript **175,5 KB gzip değerini aşmaz**; genel bütçe 250 KB olarak kalır — **kısmen**: 175,9 KB (+0,4 KB, modül sınırı maliyeti); bkz. Plandan Sapmalar. Genel 250 KB bütçesi kalır.
- [x] `AgentPrism.Ui.E2ETests` 57/57 yeşildir
- [x] `AGENTPRISM_UI_SCREENSHOTS=1` ile screenshot seti yeniden üretildi; istenmeyen görsel fark yoktur
- [x] `tuketici-dokuman-senkronu` koşuldu; `ui.md` ve screenshot yüzeyi doğrulandı
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri ilgili ailelere eklendi; otomatik olanlar koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu gerekçelendi (bkz. Denetim Bulguları)

## Plandan Sapmalar

- **Bundle bütçesi 0,4 KB gzip arttı (175,5 → 175,9 KB).** ~26 yeni modülün (agent-editor: 9, playground: 5, locale fragment: 12) import/export sarmalama maliyeti. Sert kapı 250 KB'dir ve 74 KB payla geçildi; "başlangıcı aşmama" hedefi tam tutmadı ama fonksiyonel/bağımlılık büyümesi yok — kabul edildi, yeni bir azaltma turu açılmadı.
- **`use-playground-run.ts` `sessionId` durumunu KENDİ İÇİNDE tutmuyor — facade'dan parametre olarak alıyor.** Plan iki bağımsız hook öngörüyordu (`use-playground-run.ts`, `use-attachments.ts`); gerçekte `useAttachments(sessionId)` ile `usePlaygroundRun` aynı `sessionId` değerine ihtiyaç duyuyor ve biri diğerinin state'ini "sonradan" okuyamıyor (hook'lar birbirinin iç state'ine bağlanamaz). Çözüm: `sessionId`/`setSessionId` facade'da (`playground.tsx`) tutulur, her iki hook'a parametre geçilir — ortak durumu paylaşan iki hook'un doğal dikişi budur.
- **Component-test fixture'ları için genel `{}` varsayılanı beklenenden çok daha yetersiz çıktı.** `server-types.ts`'in `Fix<>` deseni onlarca uçta alanı "her zaman dolu" sayıyor (gerçek sunucu garantisi); route-driven smoke test genel varsayılanla 10+ ekranda çöktü. Kapsamlı bir şema-şekli kütüphanesi kurmak yerine yalnız çöken uca hedefli `fixture(...)` eklendi (`app.test.tsx#overridesFor`) — bilinçli, dokümante edilmiş bir sınır (`docs/hafiza/frontend.md`).
- **`openapi-fetch`'in `fetch`/`Request`'i client oluşturulduğu anda (modül yükleme) yakalaması** test altyapısını değiştirdi: stub'ın test başına değil, dosya başına KALICI kurulması ve HTTP metodunun `Request` nesnesinden okunması gerekti. Ayrıntı ve gerekçe: `docs/hafiza/frontend.md` (Component-test altyapısı, Faz 109).
- **Faz dokümanının önerdiği "tools/skills/agents" tek başlığı üç ayrı section dosyasına bölündü** (`tools-section.tsx`, `skills-section.tsx`, `callable-agents-section.tsx`) — her biri kendi API sorgusuna bağlı, bağımsız render edilebilir olması component test açısından daha net bir sınır çiziyor.

## Bu Fazda Verilen Kararlar

Yok. Bu fazın tüm kararları yerel implementation tercihidir (dosya bölünme sınırı, fixture stratejisi); public API, güvenlik, kiracı sınırı veya kalıcı veri kararı yok — yeni `K-*` kaydı açılmadı.

## Gerçek Run Kanıtı

`samples/AgentPrism.Api` bellek içi depoyla çalıştırıldı (`AgentPrism__PostgreSql__ConnectionString=""`
ile daha önceki bir manuel test oturumundan kalan `localhost:55432` bağlantısı
geçersiz kılındı). `POST /agentprism/api/agents/claude-support/run` gerçek
Anthropic Claude Haiku çağrısı yaptı:

```
id: 3
event: update
data: {"authorName":"claude-support", ..., "contents":[{"$type":"text","text":"OK"}], ...}
id: 8
event: done
```

`GET /agentprism/api/runs/01a03bff-a4e9-796b-af6f-6e4859dc64cf` kaydı
`"status": "Completed"`, `"usage": {"inputTokens":720,"outputTokens":4,...}`
olarak doğruladı — refactor sonrası uçtan uca çalıştırma/kayıt yolu sağlam.

## `ui.md` Senkron Gerekçesi (`--site-gerekce-yazildi`)

`dokuman-bakim.py --site-denetle` `agent-editor.tsx`/`playground.tsx`
değiştiği için `ui.md`'nin de değişmesini bekledi. Sayfa güncellenmedi:
`ui.md` iç dosya yapısına hiç değinmiyor (`grep` boş döndü) ve bu fazın amacı
kullanıcıya görünen davranışı **değiştirmemek** — yalnız state/network/view
sorumluluklarını ayırmak. Kanıt: `docs-site/public/screenshots/` altındaki UI
ekran görüntüleri bu faz kapanışında `AGENTPRISM_UI_SCREENSHOTS=1` ile yeniden
üretildi ve `git diff` görsel fark göstermedi (aşağıda).

## Denetim Bulguları

Bağımsız denetçi (taze bağlam, `faz-denetim` skill'i) 2026-08-26'da koştu.

### 🔴 → Gerekçelendi (yeni `K-*` açılmadı)

**Bulgu:** `SourceLanguageTests.SkippedFiles` altı yeni girdiyle büyüdü
(`locales/tr/{common,agents,runs,workflows,operations,settings}.ts`) ve bu
`docs/KARARLAR.md`'ye karar olarak yazılmadı.

**Gerekçe:** `SkippedFiles` bir teknik borç sayacı (o rolü
`AGENTPRISM_SOURCE_LANGUAGE_REFRESH`'in yönettiği dosya-başına satır tabanı
görür) DEĞİL, K-228'in zaten sabitlediği **kalıcı, meşru** iki dilli dosyalar
için tam muafiyet listesidir — mevcut girdiler (`locales/tr.ts`,
`embed/locale.tr.ts`) de aynı kalıcı statüdedir. Bu faz `tr.ts`'i altı
fragment'a böldü; toplam muaf Türkçe içerik **artmadı**, yalnız fiziksel
konumu değişti — K-228/K-408'in sınırladığı "ne muaf" kümesi aynı kaldı.
`AGENTS.md`'nin karar defteri kapsamı ("yalnız public API/compatibility
contract, güvenlik veya kiracı sınırı, kalıcı veri/migration ya da geri
dönüşü pahalı sistem kararı") bu değişikliğin hiçbirine girmiyor — yerel bir
test-altyapısı tercihi olarak kod yorumunda ve burada gerekçelendi, yeni
`K-*` açılmadı. Karşı taraf: kural gerçekten büyüyorsa (gelecekte GERÇEKTEN
yeni, önceden muaf olmayan Türkçe içerik eklenirse) o zaman bir `K-*` gerekir
— bu fazın yaptığı bu değildir.

### 🟢 → Doğrudan düzeltildi (aday listesine devredilmedi)

**Bulgu:** DoD satırındaki "Router'daki **28** screen route" ifadesi, gerçek
route pattern sayısıyla (36) karışabilir.

**Sonuç:** Bir sonraki oturumun kafasını karıştırmaması için DoD satırı bu
oturumda netleştirildi (28 dosya · 36 pattern) — F-NN adayına devredilecek bir
gelecek iş değil, bu fazın kendi dokümanındaki bir ifade netliği.

**Diğer altı başlık (3.1–3.5, 3.7):** Temiz. Kanıt: denetçinin doğruladığı
`npm run build` (tsc + 18 dosya/221 test + Vite×2, 175,9 KB gzip) ve
`dotnet test tests/AgentPrism.Core.UnitTests` (1970/1970, `SkippedFiles`
düzeltmesi dahil).

## Sonraki Faza Devir Notu

Yapısal tur envanterindeki kalem 18/19 (büyük screen dosyaları, sözlük monoliti, sıfır component-test kapsamı) kapandı. `docs/kesif/2026-08-23-yapisal-sorun-envanteri.md`'deki diğer kalemler için aday listesi güncel kalmalı — bu faz o envanterden tek F-NN dışı fazdı.

Component-test harness'i (`src/test/`) artık genel amaçlı: yeni bir büyük screen (`workflow-editor.tsx`, `settings.tsx` gibi mevcut kod tabanındaki diğer büyük dosyalar) benzer bir bölünmeden geçerse aynı `render.tsx`/`api-fixtures.ts` çiftini kullanabilir. `app.test.tsx#overridesFor` genişledikçe (yeni bir route'un genel `{}` varsayılanıyla çöktüğü her seferinde) bu tablonun kendisi bir gün ayrı bir dosyaya taşınmayı hak edebilir — bugün 36 route için tek dosyada okunabilir kaldı.

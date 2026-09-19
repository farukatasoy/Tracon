# Arayüz Test Altyapısı Tuzakları

> Vitest component testleri, `openapi-fetch` stub'ları ve arayüz E2E'sinin
> kendine özgü tuzakları.
>
> Kardeş dosyalar: [`frontend.md`](frontend.md) (Vite, SPA rota, ekran
> mantığı) · [`frontend-tasarim-katmani.md`](frontend-tasarim-katmani.md)
> (token, primitif, erişilebilirlik) ·
> [`frontend-yerellestirme.md`](frontend-yerellestirme.md).
> Genel test koşum tuzakları AYRI:
> [`test-kosum-tuzaklari.md`](test-kosum-tuzaklari.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasıdır. Yalnızca bu alana dokunurken
> okunur. Yeni not buraya eklenir, `MEMORY.md`'ye değil.
>
> 🚨 Faz 165'te `frontend.md` bütçesini aştı; bu bölüm oradan **TAŞINDI**,
> içerik silinmedi.

## Component-test altyapısı (Faz 109)

- **🚨 `openapi-fetch`, `globalThis.fetch` VE `globalThis.Request`'i client OLUŞTURULDUĞU anda yakalar — her çağrıda değil.** `createClient()`'ın `fetch: baseFetch = globalThis.fetch` ve `Request: CustomRequest = globalThis.Request` varsayılan parametreleri MODÜL YÜKLENİRKEN (tek seferlik `lib/api.ts` `client` singleton'ı kurulurken) değerlenir. Bir testin `beforeEach`'inde `vi.stubGlobal('fetch', ...)` çağırmak `client.GET/...` çağrılarını **etkilemez** — hepsi ilk import anındaki (gerçek) `fetch`'i çağırmaya devam eder ve her ekran sessizce kendi hata durumunu çizer. Çözüm: stub'ı `test/setup.ts` içinde, dosyanın İLK importundan önce, KALICI kur; test başına değişen şey yalnız stub'ın okuduğu mutable route tablosu olmalı (`installApiMock` deseni, `test/api-fixtures.ts`).
- **🚨 `openapi-fetch`'in `fetch(request, requestInitExt)` çağrısında HTTP metodu `Request` nesnesinin ÜZERİNDEDİR, ikinci argümanda DEĞİL.** Bir stub `init?.method`'a bakarsa (Request-nesnesi çağrılarında `init` yalnız undici'ye özel uzantılar taşır) HER İSTEK sessizce GET olarak okunur — POST fixture'ları hiç eşleşmez ve mutasyon testleri (`validate`, `save`) genel `{}` varsayılanını alır. Metodu `input instanceof Request ? input.method : init?.method` ile oku.
- **Node'un gerçek `fetch`/`Request`'i (undici; jsdom hiçbirini uygulamaz) GÖRECELİ URL kabul etmez** — `new Request('/tracon/api/agents')` "Failed to parse URL from …" atar. Uygulama `apiBase`'i bilerek göreceli tutar (tarayıcı sayfaya göre çözer); test ortamında `test/setup.ts` `globalThis.Request`'i göreceli girdiyi `http://localhost` tabanına göre çözen bir sarmalayıcıyla değiştirir. `window.matchMedia` ve `Element.prototype.scrollIntoView` de aynı dosyada aynı sebeple (jsdom'da yok) dolduruluyor.
- **`server-types.ts`'in `Fix<>` ile zorunlu kıldığı alan (ör. `RunStatistics.byAgent`, `AgentSkillDefinition.enabled`) gerçek sunucuda HER ZAMAN dolu gelir — ama genel `{}` fixture varsayılanı bunu MODELLEMEZ.** Route-driven smoke testte (`app.test.tsx`) 90+ uçtan onlarcası bu yüzden çöktü (`.length`/`.map` on `undefined`); hiçbiri gerçek uygulama kusuru değildi. Düzeltme genel varsayılanı zenginleştirmek değil, çöken rotaya hedefli bir `fixture(...)` eklemektir — bkz. `app.test.tsx`'teki `overridesFor()`.
- **`userEvent.type` `{`/`}` karakterini özel tuş sözdizimi sanır** — sözdizimsel olarak bozuk JSON gibi ham metin yazarken `fireEvent.change(el, { target: { value } })` kullan, `user.type` değil.
- **🚨 Arayüz varlıklarını (`wwwroot/assets`) silip TAM derleme koşmak iki TFM'i
  yarıştırır** (2026-09-03, Faz 137): `Tracon.UI` `net9.0` ve `net10.0` için
  derlenir, ikisi de AYNI `wwwroot`'a `npm run build` koşar ve postbuild adımı
  brotli'ledikten sonra ham `.js`'i siler — ikincisi `ENOENT: unlink` ile düşer
  ve derleme `MSB3073` verir. Sıcak derlemede damga (`artifacts/obj/Tracon.UI/
  tracon-frontend*.stamp`) adımı atlattığı için görünmez; yalnız SOĞUK
  frontend derlemesinde çıkar. Çözüm `-m:1` ile derlemek. Damgayı silip
  varlıkları silmemek de yetmez: derleme "güncel" sanır ve `wwwroot/assets` BOŞ
  kalır, sonra E2E testleri "UI assets are not embedded" ile toplu düşer.
- **🚨 Form state'i kayıttan kurup request'e geri yazan ekranda, formun KONTROLÜ OLMAYAN alan sessizce düşer** (2026-09-07, B01/K-725): `agent-editor` `parameters`, `sharedInstructionsName` ve üç `model` alt alanını (`providerSettings`, `responseCache`, `allowConcurrentToolCalls`) hiç taşımıyordu; PUT tam değiştirme olduğu için kodla veya HTTP ile yazılmış bir definition, konsolda açıklaması düzeltilince bunları KAYBEDİYORDU. Çözüm `PreservedFields`: korumak ile düzenlemek ayrı işlerdir — veriyi korumak için her alana UI kontrolü GEREKMEZ. 🚨 İkinci ders `memoryHasAnything`'dedir: flag SAYAN predicate (`enableFileMemory || enableTodo || enableTextSearch`) `enableVectorSearch` eklenince bayatladı ve yalnız vector search açık olan `memory` bloğu `null` olarak gitti. Alan sayan predicate her yeni alanda bayatlar; genel yaz. Ayrıca yükleme eşlemesi `use-agent-editor.ts` içindeydi, yani `toRequest` ile ÇİFTİ test edilemiyordu — `fromDefinition` `model.ts`'e taşındı, round-trip artık tek testte ölçülüyor.
- **🚨 Formun görünmesi, asenkron varsayılanların hazır olduğunu KANITLAMAZ** (2026-09-19, Ubuntu CI): agent editor hemen render olur, provider kataloğu sonra gelir ve ilk provider'ı form state'ine yazar. E2E testi name/model alanlarını doldurup doğrudan disabled Save'e tıklarsa Playwright gerçek eksik koşulu gizleyip 30 saniyelik click timeout'u verir. Agent oluşturan test önce `agent-provider == scripted` koşulunu bekler; böylece hata provider handshake'inde ve doğru adıyla görünür.

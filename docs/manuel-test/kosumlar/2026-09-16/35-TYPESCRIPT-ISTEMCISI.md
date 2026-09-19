# 35 — TypeScript İstemcisi ve npm Kanalı — koşum kaydı (2026-09-16, ap-s2)

> **Devir notu (oturum 15, ap-s2):** Dosya 35 TAM BİTTİ — MT-TSC-001..011,
> 9/9 koşulabilir case Geçti, 0 Kaldı. İki case (008, 009) 👤 fiziksel eylem
> (`@tracon` npm kapsamı henüz rezerve edilmedi — kullanıcı kararı, spec'in
> kendi notu), fiziksel eylem listesine düştü. **Bir yeni bulgu: HATA-S2-002**
> (aşağıda) — TSC ailesinin kendi konusu değil (client migration'la ilgisiz),
> MT-TSC-002 sırasında konsol hatası taraması sırasında tesadüfen bulundu.
> Ortam: ap-s2'nin kendi çalışan uygulaması (port 5082) + iki geçici repo-dışı
> tüketici (`/control` önek testi ve tanı-ucu-kapalı testi için, ikisi de iş
> bitince durduruldu ve silindi).

---

> ### ⚗️ Damıtılmış koşum kaydı
> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin
> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum
> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**
> her case'in bloğu AYNEN durur. Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 596f24c3:docs/manuel-test/kosumlar/2026-09-16/35-TYPESCRIPT-ISTEMCISI.md
> ```

---

## Temiz geçen case'ler (4)

| Case | Durum | Başlık |
|---|---|---|
| MT-TSC-001 | ☑ | Client paketi arayüzden önce derlenir, sıfır uyarı |
| MT-TSC-003 | ☑ | Playground'da token'lar akarak gelir (SSE) |
| MT-TSC-004 | ☑ | Workflow `resume`/`respond` akışı çalışır |
| MT-TSC-006 | ☑ | Farklı önek (`/control`) altında tüm çağrılar çalışır |

## Ayrıntı taşıyan case'ler (5)

## MT-TSC-002 — Konsol eskisi gibi yüklenir (agent/run listesi, run ayrıntısı)

**Gerçek sonuç**
Playwright ile `http://localhost:5082/tracon/` açıldı, token girildi (`GET
/api/agents` önce `401`, token girişinden sonra başarılı). Dashboard, Agents
(14 agent, tam tablo), Runs (`GET /api/runs?skip=0&take=50` → 200) ve bir run
ayrıntısı (`events`/`feedback`/`input`/`trace`/`tools` uçlarının hepsi → 200)
sorunsuz yüklendi. 🚨 Konsol taramasında iki gözlem yapıldı, ikisi de
davranışı bozmuyor:
- `GET /api/agents/support/versions` → `404` — **kusur değil**, kaynakta
  belgelenmiş kasıtlı davranış (`replay-panel.tsx:33,49-51`: "A 404 here is
  a normal outcome... Code agents keep no version history"), `retry`
  fonksiyonu 404'te tekrar denemiyor, `versions.data ?? []` ile zarifçe boş
  listeye düşüyor.
- CSP ihlali (`script-src 'self'`) her sayfa yüklemesinde — **HATA-S2-002**,
  aşağıda ayrıca kaydedildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TSC-005 — Yanlış token → 401 → token istemine döner, `onUnauthorized` çalışır

**Gerçek sonuç**
🚨 **Bu case spec'in kendisinde "otomatikleştirilmemiştir" diye işaretli
(E2E boşluğu F-145) — elle koşulması özellikle değerliydi.**
`sessionStorage.tracon.token` geçersiz bir değerle ezildi (`tracon.token`
anahtarı bu şekilde tespit edildi — token disk'e değil, yalnız bu depoya
yazılıyor, prompt metniyle tutarlı), bir ekran yenilendi. Token istemi
**birebir** beklenen şekilde geri döndü: `alert: "The server rejected that
token. Check the value configured in TraconEndpointOptions.AuthToken."` —
`onUnauthorized` doğru bağlı, "reddedildi" anlamı net. Doğru token tekrar
girilerek oturum kurtarıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TSC-007 — 🚨 OpenAPI sürüklenmesi bir kapıyı kırar; `dotnet build` tek başına yakalamaz

**Gerçek sonuç**
`docs/openapi/tracon.json` (kod donuk kapsamının **dışında** — `src/`,
`samples/`, `tests/` değil) yedeklendi, `/tracon/api/diagnostics` yolu
silindi. `dotnet build src/Tracon.UI -c Release`: **`0 Warning(s), 0
Error(s)`** — sürüklenmeyi yakalamadı (beklenen, `schema.ts` commit'li ve
etkilenmiyor). `Tracon.AspNetCore.FunctionalTests`: 1077 case'ten **tam 1**
kırıldı — `OpenApiSnapshotTests.Document_matches_the_committed_snapshot`,
mesaj: `"The OpenAPI document differs from 'docs/openapi/tracon.json'..."`.
Dosya geri yüklendi (`cp` + `touch`), `git status` temiz, snapshot testi
tekrar **2/2 Geçti**. (Eski beklenti "619 toplam" diyordu, şimdi 1077 —
suite büyümüş, kusur değil, aynı desen diğer ailelerde de görüldü.)

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TSC-010 — Tanı ucu açıkken `GET /tracon/api/diagnostics` rapor döner

**Gerçek sonuç**
ap-s2'nin kendi çalışan örneği tanı ucunu zaten açık tutuyor (önceki bir
case'in kalıntısı olabilir, ürün kusuru değil). `GET /tracon/api/diagnostics`
→ `200`, gerçek bir rapor: `persistenceProvider: "PostgreSQL"`,
`canConnect: true`, `migrationsUpToDate: true`, beş sağlayıcının hepsi
`Healthy`, `toolCount: 10`, `agentCount: 16`, `agentSources`,
`extensionPoints` dahil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## MT-TSC-011 — Tanı ucu kapalıyken (varsayılan) `404` döner

**Gerçek sonuç**
İki bağımsız doğrulama: (1) mevcut otomasyon
(`DiagnosticsEndpointTests.Endpoint_does_not_map_at_all_while_off_by_default`)
→ 1/1 Geçti. (2) **Elle, gerçek bir HTTP çağrısıyla**: MT-TSC-006'nın
`/control` tüketicisi (varsayılan yapılandırma, tanı ucu bayrağı hiç
verilmedi) yeniden başlatılıp `GET /control/api/diagnostics` çağrıldı →
`404 Not Found: "There is no endpoint or console asset at path
'api/diagnostics'."`. İki yöntem de aynı sonucu doğruladı. İş bitince
süreç durduruldu ve geçici proje silindi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

## 🚨 HATA-S2-002 — Gömülü konsolun kendi CSP başlığı, kendi HTML kabuğundaki tema-erken-boyama script'ini engelliyor

**Önem:** Düşük-Orta (görsel bir FOUC/tema yanıp sönmesi, işlevsel kırılma
yok) · **Kapsam:** Bu ailenin (TSC/npm istemcisi) konusu DEĞİL — tesadüfen
MT-TSC-002 sırasında konsol hatası taraması sırasında bulundu.

**Bulgu:** `src/Tracon.UI/frontend/index.html:39-52`, sayfa gövdesine
literal bir `<script>...</script>` bloğu (nonce/hash YOK) gömüyor — amacı
`localStorage`'daki `tracon.theme` tercihini stylesheet yüklenmeden ÖNCE
uygulamak (yorum: "Runs BEFORE the stylesheet, so a stored preference
paints its own ground rather than the default one"). Ama aynı yanıtın
`Content-Security-Policy` başlığı (`src/Tracon.UI/Internal/
EmbeddedUiProvider.cs:50`) `script-src 'self'` taşıyor — `'unsafe-inline'`,
hash veya nonce YOK. Sonuç: bu inline script **HER sayfa yüklemesinde**
tarayıcı tarafından engelleniyor (ölçüldü: `/tracon/`, `/tracon/agents`,
`/tracon/runs/<id>`, `/tracon/playground/support`, hepsinde aynı hata):
```
Executing inline script violates the following Content Security Policy
directive 'script-src 'self''. Either the 'unsafe-inline' keyword, a hash
('sha256-qXVpEEsBZN0tkb/4NNCKKYCX/3nqPrSN9lCJAU0JadE='), or a nonce
('nonce-...') is required to enable inline execution. The action has been
blocked.
```
Gözlenen etki: sayfa yine de doğru temayla açılıyor (`theme.ts`, "source of
truth", modül yüklenince aynı `data-theme` özniteliğini sonradan doğru
yazıyor — `document.documentElement.dataset.theme` sonunda `"dark"` olarak
doğrulandı), yani işlevsel bir kırılma yok. Ama erken-boyama optimizasyonu
**hiçbir zaman çalışmıyor** — yorum tam tersini iddia ediyor ("this only
moves that write earlier"), oysa hiç çalışmadığı için hiçbir şeyi
erkene taşımıyor; kalıcı bir saklanmış tema tercihi olan bir kullanıcı
her sayfa yüklemesinde kısa bir varsayılan-tema yanıp sönmesi (FOUC)
yaşayabilir (görsel olarak doğrulanmadı, yalnız mekanizma üzerinden
çıkarıldı).

**Kök neden:** `EmbeddedUiProvider.cs`'in CSP yorumu `script-src 'self'`in
kasıtlı sıkı tutulduğunu söylüyor (`style-src`e bilerek `'unsafe-inline'`
eklenmiş, `script-src`e eklenmemiş) — ama `index.html`'in inline script'i bu
politikayla hiç test edilmemiş/senkronize edilmemiş görünüyor.

**Düzeltme yönü (uygulanmadı, kural 1 gereği):** Ya (a) script içeriğinin
sabit hash'i CSP'ye eklenir (`sha256-qXVpEEsBZN0tkb/4NNCKKYCX/3nqPrSN9lCJAU0JadE='`
— tarayıcının kendi hata mesajı zaten doğru hash'i veriyor), ya da (b)
script harici bir dosyaya taşınıp `script-src 'self'` altında zaten izinli
hale getirilir, ya da (c) script tamamen kaldırılıp yalnız `theme.ts`e
güvenilir (erken-boyama faydası kaybedilir).

**Durum:** Kayıt, düzeltilmedi (kural 1) — kapanışta değerlendirilmeli.

---

## Fiziksel eylem listesi

| Case | Neden | Kullanıcıdan istenen |
|---|---|---|
| MT-TSC-008 | 👤 gerçek `npm install` + IntelliSense gözlemi | Temiz bir Node projesinde `npm i @tracon/client` çalıştır, bir agent listele; paketin kurulduğunu, IntelliSense'in yol/alan adlarını gösterdiğini, çağrı sonucunun döndüğünü doğrula. **Ön koşul karşılanmıyor:** `@tracon` npm kapsamı henüz rezerve edilmedi (kullanıcı kararı, spec'in kendi notu) — bu koşulmadan önce kapsamın ayrılması gerekiyor. |
| MT-TSC-009 | 👤 gerçek CI koşumu izleme | Bir `v*` etiketi at, CI koşumunu izle; NuGet ve npm'in aynı sürüm numarasıyla yayınlandığını, ikinci koşumun var olan sürümü atlayıp kırılmadığını doğrula. Aynı ön koşul engeli (npm kapsamı) geçerli. |

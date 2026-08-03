# Frontend Tuzaklari

> Vite, SPA yonlendirme, TypeScript, mermaid.
>
> Bu dosya `MEMORY.md`'nin alan dosyasidir. Yalnizca bu alana
> dokunurken okunur. Yeni not buraya eklenir, `MEMORY.md`'ye degil.

- **Mermaid'i jsdom olmadan doğrulayamazsın** (2026-08-02): `mermaid.parse` DOM ister, aksi hâlde `DOMPurify.addHook is not a function` verir — bu bir sözdizimi hatası değildir. Doğrulayıcı: `jsdom` ile `window`/`document`/`DOMParser` global'lerini kur, sonra `mermaid.initialize({startOnLoad:false})`.
- **Vite `base: './'` + çalışma anında `<base href>`** (2026-08-02): prefix çalışma anında bilindiği için tek yol budur. `<base>` etiketini Vite'a yazdırma — HTML boru hattı `href` niteliklerini yeniden yazar. `postbuild.mjs` `<head>` içine yer tutucuyu kendisi koyar.
- **🚨 SPA rota tabanında sondaki eğik çizgi** (2026-08-02): `<base href="/agentprism/">` iken `document.baseURI` sonda `/` taşır ama `location.pathname` taşımaz (`/agentprism`). Yalnız `startsWith(base)` ile karşılaştırmak en sık kullanılan giriş adresinde "sayfa bulunamadı" üretir. `toRelativePath` bu iki biçimi de kök kabul eder; regresyon testi `format.test.ts` içinde.
- **`npm ci` bozuk kullanıcı önbelleğinde de çalışabilir** (2026-08-02): `npm install` `~/.npm/_cacache` içindeki root sahipli dosyalarda `EACCES` verirken `npm ci` geçti. Yine de kalıcı çözüm `sudo chown -R $(id -u):$(id -g) ~/.npm`.
- **`Select` bilesenin `onChange`'i cig string deger verir, DOM olayi degil** (2026-08-03, Faz 19): `components/ui.tsx`'teki `Select` `(value: string) => void` alir; `TextInput`/`TextArea` gibi native `event.target.value` OKUNMEZ. `onChange={(value) => ...}` yazilmali, `onChange={(event) => ... event.target.value}` derleme hatasi verir.

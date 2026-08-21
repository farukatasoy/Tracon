# 33 — Doküman Kapılarının Doğruluğu (`DKP`)

> **Alan kodu:** `DKP` · **Faz:** 80
> **Kaynak:** `scripts/dokuman-bakim.py` · `scripts/dokuman_bakim_test.py` ·
> `.github/workflows/ci.yml`
>
> Ortam kurulumu ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.
> Bu alan [`31-DOKUMAN-DOGRULUGU.md`](31-DOKUMAN-DOGRULUGU.md)'nün üstüne
> kurulmaz — o alan sevk edilen **metnin** doğru olduğunu kanıtlar, bu alan
> onu kanıtlayan **kapının kendisinin** doğru çalıştığını kanıtlar.

---

## Bu dosya neyi kanıtlar

`scripts/dokuman-bakim.py --site-denetle` eskiden **herhangi** bir site
sayfasının değişmesini tetiklenen **tüm** kuralların karşılığı sayıyordu;
`Workflows/` değiştirip yalnız `packages.md`yi düzenlemek de ✅ dönüyordu. Faz 80
eşlemeyi kural başına yaptı, `capabilities.md`'yi bir hedef ekledi,
`kirik_baglantilar()`'ı site-mutlak (`/...`) bağlantıları çözecek ve `.mdx`
okuyacak şekilde genişletti, kapının kendi testini yazdı ve `--denetle`'yi
CI'nın `build` işine bağladı.

---

## Koşmadan önce

```bash
cd /Users/farukatasoy/Desktop/projects/AgentPrism
python3 --version   # 3.10+ gerekir (X | None tip birleşimi)
```

Case 1-4 doğrudan `python3 scripts/dokuman-bakim.py` çağrısıdır; .NET veya
Node derlemesi istemez. Case 5 yalnız `.github/workflows/ci.yml` dosyasını
okur.

---

## Case'ler

| # | Kod | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|---|
| 1 | `MT-DKP-001` | Temiz ağaç | `src/AgentPrism.Workflows/` altında bir dosyaya boşluk ekle, `docs-site/src/content/docs/packages.md`'ye bir satır ekle, `python3 scripts/dokuman-bakim.py --site-denetle --taban HEAD` | ❌ **kırmızı**, çıkış kodu 1 — `workflow` kuralı `concepts/workflows.md` ister, `packages.md` onu karşılamaz. Rapor kural adını, hedefi ve tetikleyen dosyayı yazar |
| 2 | `MT-DKP-002` | Case 1'in ağacı (değişiklikleri geri alma) | `concepts/workflows.md`'ye de bir satır ekle, aynı komutu tekrar koş | ✅ **yeşil**, çıkış kodu 0 — tetiklenen kuralın hedefi de değişenler arasında |
| 3 | `MT-DKP-003` | Temiz ağaç | `src/AgentPrism.Core/buildTransitive/AgentPrism.Core.targets`'a yorum satırı ekle, `python3 scripts/dokuman-bakim.py --site-denetle --taban HEAD` | ❌ **kırmızı** — rapor **`capabilities.md`**'yi ister (`buildtransitive` kuralı) **ve** ayrıca `concepts/` dizinini ister (`cekirdek-kavram` kuralı); ikisi ayrı satır |
| 4 | `MT-DKP-004` | Temiz ağaç | Elle yazılan bir sayfaya (ör. `docs-site/src/content/docs/troubleshooting.md`) bir Markdown bağlantısı ekle — görünen metin "kırık", hedef yol `/yok-boyle-sayfa/` — sonra `python3 scripts/dokuman-bakim.py --denetle` | ❌ **kırmızı** — "Kırık bağlantı" bölümünde dosya adı ve hedef yol görünür |
| 5 | `MT-DKP-005` | 👤 insan gerekir | `.github/workflows/ci.yml`'yi aç | `Dokuman kapilari` ve `Dokuman kapilari testleri` adımları **`build`** işinde; `site` işinde **değil**. `Python kur` adımı (`actions/setup-python`) bu adımlardan önce çalışır |

Her case sonunda çalışma ağacı `git checkout -- <değiştirilen dosyalar>` ile
temizlenir; hiçbir case commit oluşturmaz.

---

## Doğrulama komutları

```bash
# Kapının kendi testi
python3 -m unittest discover -s scripts -p "*_test.py" -v

# Bugünkü ağaçta kırık site-mutlak bağlantı sayısı (taban ölçüm: 0)
python3 scripts/dokuman-bakim.py --denetle | grep "Kırık bağlantı"

# CI satırı DOĞRU işte mi (build, site/pages değil)
awk '/^  build:/{j="build"} /^  site:/{j="site"} /^  pages:/{j="pages"}
     /dokuman-bakim|Dokuman kapilari/{print j": "$0}' .github/workflows/ci.yml
```

## Bilinen sınırlar

- **Case 5 göz gerektirir.** Kapı YAML'ın **hangi işte** olduğunu ölçmez;
  `python3 -m unittest` çıktısı yalnız testlerin geçtiğini kanıtlar, CI'ya
  bağlı olduğunu değil.
- **`## Read next` bağlantısının hedefinin DOĞRU sayfa olduğu** (yalnız var
  olduğu değil) bu kapıyla denetlenmez — bilerek: çözülebilirlik makine işidir,
  doğruluk semantiktir (`tuketici-dokuman-senkronu` skill Adım 7).

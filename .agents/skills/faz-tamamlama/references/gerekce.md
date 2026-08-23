# Faz Tamamlama — Gerekçe ve Vaka Kaydı

> `SKILL.md`'den bağlanan ayrıntı. Bu dosya yalnız **kapı kazanmış** tuzakların
> anlatısını taşır (Faz 91 devir notu). Kapısı olmayan bir tuzak burada değil,
> `SKILL.md` prose'unda kalır — o hâlâ agent'ın uyma iradesine ihtiyaç duyar.

## Senkronizasyon kopyası (`<ad> 2.<uzantı>`)

Bulut senkronizasyon istemcisi (Faz 57'de yaşandığı üzere) çakışan bir dosyayı
sessizce `<ad> 2.<uzantı>` olarak kopyalar. `.cs` kopyası CS0101 yağmuru,
`.ts` kopyası TS2741 verir. **Beş kez yaşandı**; Faz 57'de bu kopya commit
edildi ve `main`'i derlenmez bıraktı.

Üç tuzak, `kapi.py tarama`'nın kapattığı:

- **`git status` bu kopyaları göstermeyebilir** — bir kez `git add` edildiyse
  izlenen dosyadır ve "temiz" görünür.
- **`src` yetmez.** Faz 57'de kopyalar `tests/` altındaydı; yalnız `src`'ye
  bakan eski komut onları görmedi. `docs` ve `.agents` de taranır.
- **Kopya bir `.cs` dosyası olmak zorunda değil.** `-name "* 2.*"` tek başına
  **dizin** kopyasını kaçırır: noktası yoktur. Üç boş `resources 2/` ve
  `2026-08-13 2/` dizini tam bu yüzden aylarca durdu. Komut ikisini de arar.

Kopyaları sil (`git rm` gerekebilir), sonra `wwwroot`'u ve
`agentprism-frontend.stamp` damgasını da kaldır — damga durursa arayüz yeniden
gömülmez.

## `secret` taraması

Desen, ön ekten sonra en az 24 karakter arar. `docs/manuel-test/`,
`docs/arsiv/` ve `manuel-test-kosumu` skill kaynakları hariç tutulur —
bunlarda yerel Testcontainers/Docker parola varsayılanı ve sahte
`sk-...-test-anahtari` değerleri **bilerek** vardır (Faz 79/80/81/87 emsali);
hariç tutulmadan koşarsan bu satırlar taramayı boğar. Çıktı boş olmalıdır —
`secret`'lar yalnızca `dotnet user-secrets` içinde yaşar.

Testlerde sahte `secret` literali kullanırken **tarama desenine uymayan** bir
değer seç. Yaşandı: `"sk-cok-gizli-..."` biçimindeki bir test sabiti taramayı
kirletti ve sonraki oturum için gürültü üretecekti.

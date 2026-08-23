# MEMORY.md — Yönlendirme

> **Yalnız yönlendirme + her oturumda geçerli tuzaklar.** Alan notları
> `docs/hafiza/` altındadır ve **yalnız o alana dokunurken** okunur.
> Bütçelidir; denetim `python3 scripts/dokuman-bakim.py --denetle`.

---

## Nereye Bakmalı

Alan notları `docs/hafiza/` altındadır. Hangi dosya olduğunu
[`docs/hafiza/00-INDEKS.md`](docs/hafiza/00-INDEKS.md) söyler — **yalnız
dokunduğun alanın** dosyasını aç. Belirli bir şey arıyorsan indeksi hiç açma,
doğrudan grep'le: `grep -rn "AsyncLocal" docs/hafiza/`.

---

## Her Oturumda Geçerli (bunları oku)

Alana bağlı değildir; her fazda tekrar bedel ödettiler.

- **🚨 Bir davranışı düzeltmek, o davranışa dayanan çağıranı sessizce değiştirir.**
  Oturum deposu kiracıyla sınırlanınca ses ucunun "başkasının oturumu" reddi
  etkisiz kaldı (K-283); birim değil **fonksiyonel** testler yakaladı. Depo/servis
  davranışını değiştirdiğinde `grep -rn "<metot>" src/` ile çağıranları tara.
- **Birim testi yetmez — örnek uygulamayı gerçekten çalıştır.** Sekiz fazda gerçek
  hatalar **yalnız** orada çıktı; hepsi testlerden geçmişti (K-166, K-167).
- **🚨 Struct alanını atamamak `default` bırakır ve seri hâle getirme çöker.**
  Atanmayan `JsonElement` `Undefined` olur; etki tek kayıtla kalmaz, o kaydı
  içeren **liste ucunun tamamı** çöker. Yeni kayıt üreten her kod yolunda
  zorunlu olmayan alanları da doldur (`docs/hafiza/cekirdek-calistirma.md`).
- **🚨 Senkronizasyon kopyaları (`<ad> 2.<uzantı>`) — BEŞ kez.** `.cs` → CS0101,
  `.ts` → TS2741; varlık kopyası `build`'i yeşil bırakır ama arayüz yüklenmez.
  Faz 57 kopyaları **commit etti**, `main` derlenmedi (K-411). İki tuzak:
  `git status` **temiz** görünür (kopya izleniyordur) ve `src` taraması
  **yetmez** (`tests/` altındaydılar). Kapı: `faz-tamamlama` Adım 1. Silmek
  yetmez — `wwwroot` + `agentprism-frontend.stamp` damgasını da sil.
- **🚨 `dotnet test` dakikalarca ASILI kalıyorsa alt süreç boru hatlarına bak.**
  Öksüz MSBuild düğümleri (`nodeReuse:true`) boruyu açık tutar ve
  `WaitForExitAsync` ~15 dk bloke kalır; çözüm `MSBUILDDISABLENODEREUSE=1`
  (8 dk+ → 18,5 sn). İkinci sebep: `-p:AgentPrismFrontendEnabled=false` ile
  derleyip **E2E** koşmak. Ayrıntı: `docs/hafiza/test-kosum-tuzaklari.md`.
- **🚨 Elle tekrarlanan bir toplama ifadesine terim eklemek sessiz bir kusur
  SINIFI üretir.** Faz 68'de `InputCost + OutputCost` yedi yerde elle yazılıydı;
  üçüncü terim (cache ücreti) eklenince yalnız SQL düzeltildi ve **maliyet tavanı
  olan bir kiracı tavanı aşabilirdi** — 4241 test yakalamadı, bağımsız denetim buldu.
  Toplama alan bir `record`'a alan eklerken ona `Total()` ver ve `grep` ile sınıfı
  tara (K-483, `docs/hafiza/olcum-kota-ve-secenekler.md`).
- **🚨 Kaynak okuması GÖRÜNMEZ karakteri doğrulayamaz.** MCP cache anahtarındaki
  ayırıcı `U+001F` idi; `cat` onu göstermez. Hem güvenlik denetçisi hem kapanış
  oturumu kodu okuyup "ayırıcı yok" dedi ve **yanlış bir 🔴 bulgu** üretildi;
  gerçeği çalışma anı probu (anahtarın hex dökümü) verdi. Bir string'in TAM
  içeriğine dayanan iddiayı `cat -v` veya `grep -P '[\x00-\x1F]'` ile doğrula.
  Aynı kusur elle tekrarlanan anahtar ifadesinden doğdu: okuma yolu ayırıcıyı
  taşımıyordu, yazma yolu taşıyordu (K-525).
- **Bash'te `cd` kalıcıdır**; doğrulama komutlarında **mutlak yol** kullan.
- **`dotnet test` MTP'dir, VSTest değil.** `--filter-query` yoktur (`MSB1001`).
  Tek test: `./artifacts/bin/<Proje>/release/<Proje> --filter-method "*Ad*"`.
  Bir testin **bayat mı kusurlu mu** olduğunu ayırmanın yolu budur — aynı testi
  `git worktree add <dizin> HEAD` ile temel sürümde de izole koş.
- **🚨 Tool'un gördüğü servis sağlayıcı BOŞTUR.** MAF, `AIFunctionArguments.Services`
  olarak `EmptyServiceProvider` geçirir; bir tool bağımlılığını **kurulum anında**
  almalıdır (`new BenimTool(provider)` + fabrika kaydı). Aynı sebeple `AddToolsFrom`
  ile kaydedilen **örnek metot** tool'ları da çalışmaz (K-218,
  `docs/hafiza/cekirdek-calistirma.md`). **🚨 İzole ölçüm entegre davranışı
  kanıtlamaz** — ayrı bir konsol probunda aynı çağrı çalışıyordu.
- **🚨 Planın YAPISAL iddiasını (katman, sıra, konum) kabul etmeden GREP'le ölç.**
  Faz 48'in planı guard'ı "boru hattının en dışına" koyuyordu; tek bir grep o
  konumun tool çağrı turlarını göremediğini gösterdi ve fazın yarısı taşımaya
  dönüştü (K-320). Yanlış konum derlenir, testten geçer, yalnız gerçek
  senaryoda çöker.
- **MAF ve OpenAI tip adlarını tahmin etme.** Yeni tip kullanmadan önce
  `maf-api-kesfi` skill'ini çalıştır (`AgentResponse` ≠ `AgentRunResponse`,
  `ResponsesClient` ≠ `OpenAIResponseClient`).

---

## Not Ekleme Kuralı

Not **alan dosyasına** yazılır (`docs/hafiza/<alan>.md`), buraya değil. Buraya
yalnız **alandan bağımsız** ve **tekrar bedel ödeten** bir ders girer.
`AGENTS.md` / `KARARLAR.md` / skill'lerde yazılı olanı kopyalama. Bayatlayan
notu güncelle veya sil.

# Kurtarma Rampaları — Ortak Sözleşme

> `.agents/skills/` **dışındadır** — skill keşfi bu dosyayı bir skill sanmaz.
> Bağlayan dosyalar: `AGENTS.md` · `.agents/skills/README.md` ·
> `kusur-giderme/SKILL.md` · `faz-uygulama/SKILL.md` · `faz-denetim/SKILL.md` ·
> `faz-baslangic/SKILL.md` (Faz 168). Bir rampanın gövdesi ya **burada** ya
> **bağlandığı protokolde** yaşar; ikisinde birden değil.

Bir rampanın asıl işi protokolü hatırlatmak değil, **doğaçlamayı
yasaklamaktır**. Bir şey ters gittiğinde ilk soru "ne yapsak?" değildir; "bu
hangi rampa?"dır. Rampanın kodu söylendiği an tartışma biter.

Adlandırmanın bu repoda ölçülmüş bir bedeli var: üç kusur sınıfı (`AsyncLocal`
4 kez, senkronizasyon kopyası 5 kez, Playwright locator 3 kez) ancak
**adlandırıldıktan sonra** kapı kazandı.

👤 işareti: rampa **kullanıcı onayı** gerektirir. Onaysız ilerlenmez.

## Katalog

| Kod | Durum | Gövde |
|---|---|---|
| `KR-01` | Derleme hatası | → [`kusur-giderme` Adım 1](../skills/kusur-giderme/SKILL.md#adım-1--reproyu-sabitle) |
| `KR-02` | Kırmızı test | → [`kusur-giderme` Adım 1–4](../skills/kusur-giderme/SKILL.md#adım-1--reproyu-sabitle) |
| `KR-03` | Kırılgan test | → [`kusur-giderme` Adım 2](../skills/kusur-giderme/SKILL.md#adım-2---kusur-mu-kırılgan-test-mi) |
| `KR-04` | Regresyon | → [`kusur-giderme` Adım 5, 7](../skills/kusur-giderme/SKILL.md#adım-5---sinif-taramasi-bu-adım-atlanmaz) |
| `KR-05` | Araştırılacak bulgu — repro yok | **Burada** ↓ |
| `KR-06` | Düzeltme turu limiti | **Burada** ↓ |
| `KR-07` | Plan sapması | → [`faz-uygulama` Adım 1](../skills/faz-uygulama/SKILL.md#adım-1---planın-yapisal-iddiasını-kabul-etmeden-ölç) |
| `KR-08` | Faz ortasında kapsam değişimi 👤 | **Burada** ↓ |
| `KR-09` | Faz içi bağlam sisi / devir | **Burada** ↓ |
| `KR-10` | Belirsizlik · doküman-kod çelişkisi | → [`AGENTS.md`](../../AGENTS.md) § "Temel İletişim Kuralları" → "Her belirsizliği sor" |
| `KR-11` | Güvenli geri alma 👤 | **Burada** ↓ |
| `KR-12` | Performans hedefi kaçtı | → [`kapilar.md`](kapilar.md) — `performans` alt komutu |

Yedi rampanın gövdesi **bağlantıdadır ve burada tekrarlanmaz**. Bir protokol
adımı değişirse tek yerde değişir; katalog onu kopyalasaydı iki metin sessizce
çelişirdi.

---

## `KR-05` — araştırılacak bulgu

**Tetikleyici:** bir bulgu geçerli **görünüyor** ama repro yok. Denetim
raporundan, bir okumadan veya bir sezgiden gelmiş olabilir.

1. Minimal repro yaz — bulguyu istediğin an üreten en küçük komut ya da test
2. Süre kutusu **bir tur**. Tur dolduğunda uzatma yok
3. Repro çıktıysa → `KR-02`. Artık bir kusurdur, araştırma bitti
4. Repro çıkmadıysa → **gerekçeli kapanış**: bulgu, ne denendi ve neden
   kapatıldığı faz dokümanının `## Denetim Bulguları` bölümüne yazılır

🚨 **Düzeltme yazmak yasaktır.** Repro'suz bir düzeltme, düzelttiğini
kanıtlayamaz; yalnız kodu değiştirdiğini kanıtlar. Sessiz kapanış da yasaktır —
kapatılmış bir bulgu, hiç bakılmamış bir bulgudan ayırt edilebilmelidir.

## `KR-06` — düzeltme turu limiti

**Tetikleyici:** aynı davranış için **üçüncü** düzeltme turu.

1. **Kod yazmayı durdur.** Dördüncü turu denemek limiti kaldırmaktır
2. Tek soruyu sor: kök sebep **kodda mı planda mı?**
3. Plandaysa → `KR-08`. Koddaysa repro yeterince dar değildir → `KR-01`/`KR-02`

Bu rampa bir sayı taşır ve sayı **üçtür**. "Birkaç tur" bir limit değildir;
sayısı olmayan bir limit hiç tetiklenmez.

## `KR-08` — faz ortasında kapsam değişimi 👤

**Tetikleyici:** fazın DoD'si artık doğru işi tarif etmiyor.

Sıra **bağlayıcıdır**:

1. Önce **faz dokümanı** güncellenir — `## Plandan Sapmalar` gerekçeyi alır
2. Sonra delta plan yazılır — neyin eklendiği, neyin düştüğü
3. Sonra kod

Ters sıra `AGENTS.md`'nin "plandan sapma **gizlenmez**" kuralını sessizce
çiğner: kod önce yazılırsa doküman onu **açıklamaya** değil **haklı
çıkarmaya** başlar. 👤 Kullanıcı onayı gerekir — kapsam kullanıcınındır.

## `KR-09` — faz içi bağlam sisi / devir

**Tetikleyici:** oturumun bağlamı doluyor ama faz bitmedi.

Devretmeden önce faz dokümanının sonuna `## Faz İçi Devir Notu` başlığı açılır
ve **dört başlık** doldurulur:

- **Ne bitti** — hangi DoD satırları kapandı
- **Ne yarım** — hangi dosya hangi hâlde bırakıldı
- **Sıradaki tek adım** — bir sonraki oturumun ilk komutu
- **Bilinen tuzak** — bu fazda bedel ödeten ne varsa

Not faz dokümanına yazılır çünkü sonraki oturumun **sabit okuma kümesinde**
zaten o doküman vardır; başka bir dosya okunmayabilir. Kapanışta bu bölüm
`## Plandan Sapmalar` ile birleşir ve silinir.

🚨 `faz-tamamlama` Adım 6 **buna alternatif değildir**: o yalnız faz
**sonunda** koşar. Bu rampa fazın **ortası** içindir.

## `KR-11` — güvenli geri alma 👤

**Araç `git revert`'tir.** `git reset --hard` Faz 167'de yasaklandı
(`.claude/settings.json` `deny`).

🚨 **O yasak bir korkuluktur, kilit değildir (K-761).** Yalnız Claude Code'da
ve yalnız eşleşen komut biçiminde geçerlidir; `/bin/git`, `sh -c 'git …'` ve
`git -C . reset --hard` biçimlerini **durdurmaz**. `git rebase`, `git clean -fd`
ve `rm -rf` listede **hiç yoktur**. Bu rampayı "harness beni durdurur" diye
okuma — durdurmaz.

Geri almadan önce **migration risk raporu** yazılır: geri alınacak commit'ler
kalıcı veriye dokundu mu, dokunduysa geri alma veriyi ne hâle getirir.

Tek kural: **"bilinmiyor" bir cevaptır ve geri almayı durdurur.** 👤 Kullanıcı
onayı gerekir.

---

## Bilinen sınır — fragment çapası

Yukarıdaki bağlantıların beşi `#adım-N-…` çapası taşır. Kapı
(`kirik_baglantilar()`) **dosyayı** doğrular, fragment'ı **doğrulamaz**:
bağlanan adım yeniden numaralanırsa çapa sessizce ölür, bağlantı yeşil kalır.
Bu yüzden her satır adımı **adıyla da** yazar — çapa ölse bile hedef okunabilir
kalır. Fragment doğrulayan bir kapı bilerek kurulmadı (Faz 168).

🚨 **Başlığı `İ` ile başlayan bir bölüme çapa YAZILMAZ.** `İ`'nin küçük harfi
`i` + `U+0307`'dir (birleşen nokta); slug o görünmez karakteri taşır ve elle
yazılan `…-iletişim-…` çapası ekranda **birebir aynı görünürken** hedefe
atlamaz. `KR-10` bu yüzden çapasız bağlanır ve bölüm adını düz metin yazar.
Ölçüldü (Faz 168 denetimi, `github-slugger`): `Temel İletişim Kuralları` →
`temel-i̇letişim-kuralları`.

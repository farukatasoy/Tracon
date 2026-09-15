# ADAYLAR — Planlama Kuyruğu

> **Bu dosya ne taşır:** henüz faza dönüşmemiş yetenek adaylarını ve onları
> bekleten koşulları. Başka hiçbir şeyi.
>
> **Ne taşımaz:** faz durumunu (üretilen [`YOL-HARITASI.md`](YOL-HARITASI.md)) ·
> kusurları (`kusur-giderme` kanalı) · kapanmış kararları
> ([`KARARLAR-INDEKS.md`](KARARLAR-INDEKS.md)) · tur anlatılarını
> ([`kesif/`](kesif/)) · plana dönüşmüş kalemlerin gövdelerini
> ([`arsiv/PLANA-DONUSEN-ADAYLAR.md`](arsiv/PLANA-DONUSEN-ADAYLAR.md)).

**Durum (2026-09-15):** 4 sıralanabilir aday · 11 bekleyen kalem.
Son plana dönüşen: **F-227 · F-228 · F-229 → [Faz 167](arsiv/fazlar/167-AGENT-ZORLAMA-KATMANI.md) ·
[168](arsiv/fazlar/168-KURTARMA-RAMPASI-KATALOGU.md) · [169](arsiv/fazlar/169-FAZ-PLANI-SOZLESMESI.md)**
(📋 Planlandı). Yeni aday üretmek için `aday-kesfi` koşulur.

---

## Okuma Sırası

| İhtiyacın | Nereye bak |
|---|---|
| Sıradaki fazı seçmek | § *Sıralanabilir Adaylar* — kanıtı ölçülmüş, bugün plana dönüşebilir |
| Hangi fazın nerede olduğu | **Buraya değil** — üretilen [`YOL-HARITASI.md`](YOL-HARITASI.md). Planlanmış faz dokümanları `docs/` kökündedir |
| Bir kalem neden faz değil | § *Bekleyen Kalemler* — her satır engeli ve koşulu söyler |
| Bir F-ID nereye gitti | § *Aday Olmayan Açık Kayıtlar* |
| Yeni aday üretmek | `aday-kesfi` skill'i; çıktısı bu dosyaya yazılır |
| Bir kalem neden reddedildi | § *Bilerek Önerilmeyenler* → [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md) |
| Geçmiş turda ne olmuştu | [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](arsiv/PLANA-DONUSEN-ADAYLAR.md) — tur anlatıları, çürütülmüş iddialar, taşınan gövdeler |

Bu dosya **baştan sona okunmaz.** İhtiyacın olan bölüme git.

---

## Değerlendirme Ölçütleri

| Ölçüt | Soru |
|---|---|
| **Değer** | Bu olmadan Tracon'i kim kullanamaz? |
| **Maliyet** | Kaç paket, kaç yeni public tip, kaç migration? |
| **Risk** | Bir tasarım kuralını, AOT veya bundle bütçesini zorluyor mu? |
| **Hazırlık** | MAF veya .NET ekosisteminde hazır mı, sıfırdan mı? |

Bir adayın `Mercek` satırı aşağıdaki destekleyen mercekleri numarayla sayar.

| # | Mercek | Sorusu |
|---|---|---|
| 1 | **Benimseme** | İlk agent'a kadar geçen süreyi kısaltır mı? |
| 2 | **Üretim işletimi** | Gece 03:00'te nöbetçi mühendisin işine yarar mı? |
| 3 | **Kurumsal satın alma** | Hangi kurumsal kapıyı açar? |
| 4 | **Performans ve AOT** | Sıcak yol ve tahsis bütçesi korunur mu? |
| 5 | **API ergonomisi** | Yanlış kullanım derlemede yakalanır mı? |
| 6 | **Ekosistem yerleşimi** | Aspire, OTel, MCP, A2A ve DI ile doğal mı oturur? |
| 7 | **Ölçme–iyileştirme** | Üretim verisini geliştirmeye geri besler mi? |
| 8 | **Maliyet (FinOps)** | Tüketicinin model faturasını düşürür mü? |

Her aday gövdesi şu alanları taşır: **Sorun · Kapsam · Değer · Mercek ·
Hazırlık · Maliyet · Risk · Bağımlılık · Ekosistem · Karşı görüş.** Bir alan
ölçülmediyse öyle yazılır; boş bırakılmaz.

---

## Faz 166'dan Devreden

| Aday | Ne açar | Kanıt |
|---|---|---|
| **F-230 — Kapasite sürüm damgası kapısı** | `docs-site/guides/production.md`'de yayımlanan her kapasite sayısının `bench/capacity/measurements/*/summary.json` içinde bir karşılığı olduğunu ve sayfanın yazdığı commit'in manifest'le eşleştiğini doğrulayan bir `dokuman-bakim.py` kontrolü | Faz 166'nın denetimi **beş 🔴** buldu ve beşi de tam olarak bu sınıftı: yanlış commit damgası, yanlış birleştirilmiş percentile, tek tekrarın ortalama gibi sunulması, elle kopyalarken bozulan yüzde, kanıtın izlenmeyen dizinde kalması. K-775 bunu bir **sözleşme** olarak kurdu; kapısı yok |

---

## Sıralanabilir Adaylar

Dördünün de **boşluğu** ölçülmüştür ve dördü de bugün `faz-planlama`'ya
girebilir. Sıra bir öneridir, zorunluluk değil. F-232 bir **ölçüm adımını**
planının içinde taşır ([Faz 167](arsiv/fazlar/167-AGENT-ZORLAMA-KATMANI.md)
§ 167.3 emsali): ölçüm negatifse kapsam o adımda daralır.

| # | Aday | Ne açar | Maliyet | Kanıtın gücü |
|---|---|---|---|---|
| 1 | [F-224](#f-224--geri-alınamaz-kararlar-için-doğrulama-adımı) | Mis-click geri alınamaz bir kararı vermez | Küçük–orta | Primitif hazır; envanter ölçüldü |
| 2 | [F-218](#f-218--skor-satırı-evaluator-paket-sürümünü-damgalamıyor) | "Yargıç mı değişti, model mi bozuldu" sorusunun cevabı olur | Orta (migration) | Boşluk ölçüldü; talep kanıtı tek risk satırı |
| 3 | [F-213](#f-213--store-sözleşmelerinin-iptal-davranışı-yazılı-değil) | İptal davranışı uygulama detayı olmaktan çıkar | Küçük ama yaygın | Boşluk ölçüldü; talep kanıtı yok |
| 4 | [F-232](#f-232--tüketici-reposuna-kurulan-tracon-kapı-skilli) | Agent Tracon kodunu yazmadan **önce** yönlendirilir | Orta | Boşluk ölçüldü; **yükleme/çağrılma kanıtı yok** — fazın ilk adımı ölçümdür |

---

### F-171 · Sevk edilen davranış iddiaları için kapı — ✅ KAPANDI

Üç yarısı da kapandı (sayı · davranış · sürüm damgası). Kaydın kendi
ölçümünün neden yanlış olduğu ve kapının 2026-09-14 genişletmesi:
[`arsiv/PLANA-DONUSEN-ADAYLAR.md`](arsiv/PLANA-DONUSEN-ADAYLAR.md).

---

### F-224 · Geri alınamaz kararlar için doğrulama adımı

**Kaynak:** [Faz 164](arsiv/fazlar/164-CONSOLE-ENSTRUMAN-KATMANI.md) — `Dialog`
yazıldı, ölçüldü ve DoD gereği geri alındı (o fazın "Plandan Sapmalar" §3).

**Sorun:** Onaylar ekranında bir tool çağrısını onaylamak veya reddetmek tek
tıktır ve geri alınamaz (K-014: karar yeni bir run açar, soran run sonsuza dek
`AwaitingApproval` kalır). İki düğme yan yanadır. Aynı desen başka yerlerde de
yaşıyor: session silme, agent sürümü geri alma, kiracı anahtarı iptali.

**Kapsam:** `Dialog` primitifi hazırdır — odak tuzağı, `Esc`, odak dönüşü,
kaydırma kilidi, `UiTests` ile kanıtlı
([`components/dialog.tsx`](../src/Tracon.UI/frontend/src/components/dialog.tsx)).
İş üç adımdır: geri alınamaz aksiyonların **envanterini** çıkarmak,
hangilerinin doğrulama hak ettiğine karar vermek, etkilenen E2E olgularını
**birlikte** güncellemek. Faz 164 bunu yapamazdı: DoD'si mevcut olguların
hiçbirinin değişmemesini şart koşuyordu.

**Değer:** Bir mis-click geri alınamaz bir kararı vermez.

**Mercek:** 4.

**Hazırlık — ölçüldü (2026-09-13):** Primitif hazır ve bugün üç bileşen
kullanıyor (`dialog.tsx` · `command-palette.tsx` · `menu.tsx`) — **hiçbir ekran
kullanmıyor**. Ekranlarda geri alınamaz aksiyon çağrı yeri **10**
(`onApprove`/`onReject`/`handleDelete`/`handleRevoke`/`rollback` taraması).
E2E olgu sayısı bugün **70**'tir (Faz 164'ün DoD'si 58 diyordu — o sayı bayat).
Hangi 10'unun doğrulama hak ettiği **ölçülmedi**; fazın ilk adımı odur.

**Maliyet:** Küçük–orta. Envanterin kaçının seçildiğine bağlı.

**Risk:** Orta — her doğrulama adımı bir etkileşim sözleşmesini değiştirir ve
ilgili E2E olgusunu birlikte taşımayı gerektirir. Onay yorgunluğu da gerçek bir
maliyettir: her şeyi doğrulatmak hiçbirini doğrulatmamakla aynı yere çıkar.

**Bağımlılık:** Yok.

**Ekosistem:** 2026-09-13 — iç arayüz kalitesi; dış ekosistem iddiası yok.

**Karşı görüş:** 10 çağrı yerinin hepsi doğrulama hak etmez. Kapsam seçici
olmazsa aday kendi riskini üretir.

---

### F-218 · Skor satırı evaluator paket sürümünü damgalamıyor

**Kaynak:** [Faz 155](arsiv/fazlar/155-KALIBRE-EDILMIS-EVALUATOR-KATALOGU.md) — plan
risk tablosundaydı, kapsam dışı bırakıldı.

**Sorun:** `AddEvaluatorJudge` ile bağlanan bir `IEvaluator`'ın puanı
`Microsoft.Extensions.AI.Evaluation.Quality`'nin **prompt'una** bağlıdır ve o
prompt paket sürümüyle değişir. Skor satırı hangi sürümün ürettiğini
kaydetmiyor; bir yükseltme
[Faz 153](arsiv/fazlar/153-EVAL-KOSUMLARI-ARASINDA-REGRESYON-FARKI.md)'ün regresyon
taban çizgisini **sessizce** kaydırabilir — fark "model bozuldu" gibi görünür,
oysa yargıcın kendisi değişmiştir.

**Kapsam:** `RunScore`'a bir kaynak/sürüm damgası eklemek — `Comment`'e
sıkıştırmadan, ayrı bir alan olarak.

**Değer:** Skor trendini yorumlanabilir kılar; "yargıç değişti mi" sorusunun
koşulan bir cevabı olur.

**Mercek:** 3, 6.

**Hazırlık — ölçüldü (2026-09-13):** Faz 155 köprüyü kurdu; damga onun üstüne
biner. [`RunScore`](../src/Tracon.Abstractions/Runs/RunScore.cs) bugün on iki alan
taşıyor ve ikisi kaynağa bakıyor (`Source`, `Author`) — **sürüm alanı yok**.

**Maliyet:** Orta — `RunScore` kalıcı bir tiptir, üç SQL sağlayıcısında
migration ister (K-711'in emsali).

**Risk:** Kalıcı şemaya alan eklemek geri dönüşü pahalıdır.

**Bağımlılık:** Faz 155 (kapandı).

**Ekosistem:** 2026-09-07 — `Microsoft.Extensions.AI.Evaluation` 10.9.0.

**Karşı görüş:** Talep kanıtı şimdilik tek bir risk satırıdır, ölçülmüş bir
tüketici şikâyeti değil. `Author` alanı `judge:{ad}` taşıyor; sürümü oraya
sıkıştırmak migration'sız bir ara çözüm olurdu — ama K-059 sınıfı bir hata
olur, ad alanı sürüm alanı değildir.

---

### F-213 · Store sözleşmelerinin iptal davranışı yazılı değil

**Kaynak:** [Faz 152](arsiv/fazlar/152-SKORUN-ADI-VE-SEKLI.md) — case bilerek
dışarıda bırakıldı, gerekçe o fazın "Plandan Sapmalar" tablosundadır (sapma 3).

**Sorun:** Hiçbir `*StoreContract` iptal case'i taşımıyor ve uygulamalar farklı
davranıyor: SQL store'lar token'ı ADO.NET üzerinden doğal olarak gözlüyor,
bellek içi store'lar token'ı çoğunlukla hiç okumuyor. Bir tüketici
`UpsertAsync(score, alreadyCancelledToken)` çağırdığında ne olacağını
sözleşmeden öğrenemiyor.

**Kapsam:** Tek bir store değil, **store ailesi** için bir karar: sevk edilen
sözleşmelere iptal case'i girsin mi, girecekse bellek içi uygulamalar da
`ThrowIfCancellationRequested` ile hizalansın mı.

**Değer:** İptal davranışı bugün uygulama detayı; sözleşmeye girerse
tüketicinin varsayımı ölçülebilir hâle gelir.

**Mercek:** 3, 7.

**Hazırlık — ölçüldü (2026-09-13):** `Tracon.Testing.Contracts.Xunit/Contracts/`
altında **29 sözleşme sınıfı** var ve **hiçbiri** iptal case'i taşımıyor.

**Maliyet:** Küçük ama YAYGIN — 29 sözleşme ve bellek içi karşılıkları.

**Risk:** Sözleşmeyi genişletmek sevk edilen bir söz vermektir; üçüncü taraf
`IRunScoreStore`/`IRunStore` uygulamalarına yeni bir zorunluluk yükler ve
ölçülmüş bir tüketici talebi olmadan yapılırsa geri alması pahalıdır.

**Bağımlılık:** Yok.

**Ekosistem:** 2026-09-13 — iç sözleşme kalitesi; dış ekosistem iddiası yok.

**Karşı görüş:** Talep kanıtı **yok**. Sıralamada sonuncu olmasının sebebi
budur; 1.0 öncesi bedava olması ise tersini söylüyor — yayından sonra bu
genişletme kırıcıdır.

---

### F-232 · Tüketici reposuna kurulan Tracon kapı skill'i

**Kaynak:** Kullanıcı fikri (2026-09-15). [Faz 73](arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md)
ve [Faz 167](arsiv/fazlar/167-AGENT-ZORLAMA-KATMANI.md) hattının devamı.

**Sorun:** Tracon bugün bir coding agent'a iki kanal veriyor: **bilgi**
(`Tracon.AgentMap.md` → `AGENTS.md`, `Tracon.LocalReference.md`, `llms.txt`)
ve **zorlama** (dokuz `TRC0*` usage diagnostic). İkisi de geç konuşur. Map
okunmayı bekler; diagnostic ise kod **yazıldıktan sonra** konuşur — agent
retry loop'unu yazar, derler, uyarıyı görür, siler. Eksik olan üçüncü kanal
**prosedürdür**: "Tracon yüzeyine dokunan kod yazmadan önce şunu şu sırayla
yap." Skill formatı tam olarak bunu taşır; tetikleyicisi olan bir iş akışıdır.

**Kapsam** (kullanıcı kararları, 2026-09-15):

- **Yazıcı CLI'dır, build değil.** `tracon` global tool'una dosya yazan bir
  komut girer. Build **yazmaz**, yalnız bayatlığı raporlar — `TRC0401`
  emsalinde yeni bir usage diagnostic. Gerekçe: skill dizini çok dosyalıdır,
  commit edilir ve tüketicinin sahibi olduğu içeriktir; `AGENTS.md`'nin
  "yalnız yokken yaz" sözleşmesi oraya genişletilemez.
- **Dört format:** `.claude/skills/<ad>/SKILL.md` · vendor-nötr
  (`AGENTS.md` eki veya `.agents/`) · `.github/` (Copilot) ·
  `.cursor/rules/*.mdc`. Tek kanonik metin, dört ince emitter.
- **İçerik tek kapı skill'idir.** "Tracon yüzeyine dokunan kod yazmadan önce
  yetenek haritasını ve **kurulu sürümün** XML dokümanını oku." Görev
  prosedürü seti (agent ekle · tool ekle · run teşhisi · sürüm yükseltme)
  kapsam **dışıdır**.
- **Bayatlama mekanizması hazırdır.** Map bugün `revision:` damgası taşıyor
  (`9039142d`); üretilen skill aynı damgayı taşır ve diagnostic karşılaştırır.

**Değer:** Diagnostic yazılmış kodu yakalar, skill yazılmadan önce yönlendirir.
Tracon'un "agent senin yerine yeniden icat etmesin" iddiası ancak önleyici
kanalla tamamlanır.

**Mercek:** 1 · 5.

**Hazırlık — kısmen ölçüldü (2026-09-15):** Hazır olan: yetenek haritası
üretiliyor, damgalı ve bütçeli (`docs-site/scripts/build-agent-map.mjs`,
10.583 B); `AGENTS.md` ve `Tracon.LocalReference.md` yazma yolu
(`src/Tracon.Core/buildTransitive/Tracon.Core.targets`); dokuz diagnostic
(`src/Tracon.Generators/UsageDiagnostics.cs`); beş komutlu bir CLI.
🚨 **Ölçülmemiş ve fazın ilk adımı olan:** üretilen skill dört harness'ın
her birinde gerçekten **yükleniyor ve çağrılıyor mu**. Faz 167 § 167.3
emsali aynen geçerlidir — izole geçici projede ölç ve **ayırt edici** bir
kontrol koşumu taşı; tanınmayan bir anahtar sessizce yok sayıldığı için
"hata vermedi" tek başına kanıt değildir. Bir format ölçümü geçemezse
kapsamdan **düşer**. Ayrıca ölçülmemiş: tüketicinin vendor dizinine yazılmayı
kabul edip etmediği (talep kanıtı yok).

**Maliyet:** Orta. Bir CLI komutu · bir kanonik metin · N emitter · bir
diagnostic · bir ölçüm turu. `src/` çekirdeğine dokunmaz; yeni paket yok,
migration yok. Public API yalnız CLI yüzeyinde büyür.

**Risk:**

- Skill formatları vendor'a özgü ve hareketlidir. Dördü birden bugün bir
  sözleşme, yarın dört bayat dosyadır.
- Çağrılmayan skill ölü ağırlıktır ve tüketicinin agent bağlam bütçesini yer —
  Tracon'un kendi `AGENTS.md` bütçe disiplininin aynısı.
- 🚨 Dosya yazan bir komut, tüketicinin çalışma ağacını **ilk kez** değiştirir.
  Bugünkü beş komuttan yalnız `migrate` bir şey değiştirir ve o da veritabanını.
  Üzerine yazma, birleştirme ve elle düzenlenmiş dosya davranışı bir
  **sözleşme kararıdır** (karar defteri adayı).
- CLI ayrı kurulum ister (`dotnet tool install -g Tracon.Cli`); benimseme
  sürtünmesi build hattından yüksektir.

**Bağımlılık:** Yok. Faz 73 ve Faz 167'nin çıktısı üzerine oturur.

**Ekosistem:** 2026-09-15 — `AGENTS.md` çok-vendor bir konvansiyondur ve
Tracon onu zaten yazıyor. Skill dizini konvansiyonunun ortak standardı
**yoktur**; her vendor kendi yolunu tanır.

**Karşı görüş:** Diagnostic zaten yönlendiriyor ve yazılmış kodu **kesin**
olarak yakalıyor; skill yalnız okunduğunda işe yarar. Ek değeri ölçülmeden
dört format sevk etmek, kanıtsız bir bakım yüzeyi satın almaktır.

---

## Bekleyen Kalemler

Hiçbiri **bugün faz değildir.** Gövdeleri, koşulları oluştuğunda plana
dönüşebilmeleri için duruyor. Bir kalemi buradan çıkarmanın tek yolu
**koşulunun gerçekleştiğini ölçmektir**.

### Tek satırlık bekleyenler

| Kalem | Engel | Koşul ne zaman oluşur |
|---|---|---|
| **F-180** · Tam paket koşumunda E2E zaman aşımı | ⚠️ **Vaka kapandı (2026-09-04), SINIF açık.** Repro "yük altındaki tam paket koşumu"ydu; kapanış onu kendi koşullarında tekrar koşmadı ve test bir gün sonra aynı imzayla düştü | Ders `kusur-giderme` kapanış kontrolüne yazıldı. Sınıf yeniden görülürse **yeni** kusur kaydı açılır. Gövde: [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md) |
| **F-199** · Kota eşiği bildiriminin kaybı | Eşik claim edildikten SONRA webhook/akış yayını başarısız olursa o eşik dönem sonuna kadar kalıcı kaybolur — düşük risk | Kota webhook/notice teslimi için retry/backoff istenirse ([Faz 146](arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md) denetim bulgusu) |
| **F-200** · Çok kullanıcılı kota izolasyonu regresyon testi | Garanti **yapısaldır** (`RunEventWriter`'ın run başına özel `Guid`'i); eksik olan yalnız ona adanmış test | `RunEventWriter`/`RunRecordingAgent`'ın run izolasyonu yeniden düzenlenirse ([Faz 146](arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md) denetim bulgusu) |
| **F-205** · `/v1/conversations/{id}` varlık asimetrisi | Kullanılmamış kimlik `200`, reddedilen kimlik `404`. Katı modda çağıran hangi id'lerin sahipsiz SATIR olduğunu sayabilir — erişim kapalı, yalnız varlık görünür. Davranış ucun rezervasyon semantiğinden miras (Faz 4); kapatmak OpenAI uyumluluğunu bozar. `/api/sessions/{id}` bu sızıntıyı taşımaz | Tüketici varlık gizliliği talep ederse ([Faz 149](arsiv/fazlar/149-SAHIPSIZ-OTURUMUN-KATI-REDDI.md) denetim bulgusu) |
| **F-226** · SSE yanıtının şeması JSON şekli ilan ediyor | ASP.NET Core'un üstveri modeli aynı statü kodu için iki şema ifade edemiyor ve K-039 gereği kütüphane `Microsoft.AspNetCore.OpenApi`'ye bağımlı değil — bir `OpenApiOperationTransformer` kütüphanede yaşayamaz. **Ölçüldü (2026-09-13):** `tracon.json`'da 7 `text/event-stream` yanıtı var, **2'si** JSON şekli ilan ediyor (`/tracon/v1/responses` → `JsonElement`, `/tracon/v1/chat/completions` → `ChatCompletion`); kalan 5'i doğru biçimde `type: string`. Üretilen istemci etkilenmiyor — altıncı geçiş içerik tipinin VARLIĞINA bakar | Belgeden kod üreten üçüncü taraf bir üreteç bu yüzden kırılırsa ([Faz 159](arsiv/fazlar/159-TIPLI-ISTEMCIDE-AKISLI-OPENAI-CAGRISI.md) denetim bulgusu 🟢 3) |
| **F-230** · `kurtarma.md` ↔ `.claude/settings.json` senkron kapısı | `KR-11` rampası `deny` listesinin bugünkü içeriğini **sayarak** tekrarlıyor (`git rebase`, `git clean -fd`, `rm -rf` listede yok). Tekrar Faz 167 devir notunun **açık isteğidir** — yasağın sınırını yazmayan bir rampa yanlış güven üretir (K-761). Ama `settings.json` genişlerse cümle sessizce yalan olur ve bunu sayan kapı yok | `.claude/settings.json` `deny` bloğu ilk kez değiştiğinde ([Faz 168](arsiv/fazlar/168-KURTARMA-RAMPASI-KATALOGU.md) denetim bulgusu 🟢 6) |
| **F-236** · Audit yazma politikası ratchet'i değişken adına bağlı | `AuditWritePolicyTests`'in deseni `[Aa]udit[Ll]og\.WriteAsync\s*\(`'dir; `IAuditLog log = …; log.WriteAsync(entry, ct);` şeklini **görmez**. Açık Soru §3'te kaynak taraması "ucuz ama kırılgan" diye bilerek seçildi (emsal `AuditCoverageTests` de kaptan doğrular, analyzer değildir); Roslyn analyzer'a geçmek bir analyzer paketi maliyetidir. Ölçüldü: `src/` altında bu şekli kullanan tek yer `Tracon.Testing.Contracts.Xunit/Contracts/AuditLogContract.cs:25` ve orada **meşru** | Politikayı atlayan bir çağrı yeri gerçekten kaçarsa, ya da başka bir ratchet de analyzer isterse ([Faz 171](arsiv/fazlar/171-DENETIM-IZI-YAZMA-POLITIKASI.md) denetim bulgusu 🟢 7) |
| **F-237** · Etkiden önceki dar fail-closed `run` kaydı | Genel `RecordingMode.Required` **reddedildi** (2026-09-07 A10; 2026-09-15 turu yeniden ölçtü): model çağrısı ve tool yan etkileri olduktan sonra `run`'ı düşürmek hiçbir şeyi geri almaz. Savunulabilir kalan tek biçim dar bir `seam`'dir — `run` açılışı yazılamazsa `run` başlamaz, yan etkili tool çağrısının kaydı yazılamazsa tool koşmaz. Bugün böyle bir `seam` **yok**: `RunEventWriter` her hatayı yutar ([`RunEventWriter.cs:452`](../src/Tracon.Core/Recording/RunEventWriter.cs#L452) — `Disable`) ve tüketicinin kendi `IRunStore`'u da bunu değiştiremez. Faz 173 kaybı **görünür** kıldı (K-782), fail-closed **yapmadı** | Düzenlemeye tabi bir kurulum kanıtla talep ederse. Faz 173'ün `tracon.run.recording_failures` sayacı önce kaybın gerçek sıklığını ölçer — kanıtsız inşa edilen altyapı yanlış şekli alır |
| **F-238** · Metrik dinleyici test yardımcısının ÜÇ kopyası | Bir metriği ölçmek isteyen her test projesi kendi `MeterListener` sarmalayıcısını yazıyor: `MetricCollector` ([`tests/Tracon.Core.UnitTests/Fakes/MetricTestHelpers.cs`](../tests/Tracon.Core.UnitTests/Fakes/MetricTestHelpers.cs)), `MetricProbe` ([`tests/Tracon.PostgreSql.IntegrationTests/Infrastructure/MetricProbe.cs`](../tests/Tracon.PostgreSql.IntegrationTests/Infrastructure/MetricProbe.cs)) ve `WorkflowMetricProbe` ([`tests/Tracon.Workflows.UnitTests/WorkflowRecordingFailureTests.cs`](../tests/Tracon.Workflows.UnitTests/WorkflowRecordingFailureTests.cs)). Üçü de meter'ı **referansla** eşleyip `TagList`'i kopyalıyor; üçü de `internal`, dolayısıyla paylaşılamıyor. İlk ikisi Faz 173 denetiminde 🟢 7 olarak görüldü, üçüncüsü aynı fazda eklendi — kopya sayısı K-483'ün "elle tekrarlanan ifade bir kusur SINIFI üretir" eşiğindedir | Dördüncü kopya gerektiğinde, ya da `Tracon.Testing` yüzeyine bir metrik doğrulama yardımcısı eklemek ayrıca istendiğinde. Not: bu bir **test altyapısı** kararıdır ve sevk edilen yüzeyi büyütmek (public bir `MeterProbe`) ayrı bir tartışmadır ([Faz 173](173-CALISTIRMA-KAYDI-GORUNURLUGU.md) denetim bulgusu 🟢 7) |

### F-95 · Agent düzeyinde kesinti/devam kancası

**Engel:** MAF yolu **kapalı** (aşağıda ölçüldü). Kalem yalnız F-141 üzerinden
ilerler.

**Sorun:** Kesintiye uğramış bir agent turunu devam ettirmek için Tracon'in
agent yürütmesinin **içine** girebilmesi gerekir.

**Hazırlık — 🚨 İmza doğrulandı (2026-09-05), MAF yolu KAPALI.**
`maf-api-kesfi` koşuldu; 1.18.0 → 1.20.0 tam yüzey dump'ı diff'lendi. Kanca
çekirdek paketlerde **yoktur**: ayrı ve **alpha** bir pakettedir —
`Microsoft.Agents.AI.AgentHooks` 1.20.0-alpha.260831.1, public yüzeyi iki tip
(`AgentHooksChatClientExtensions.AsAIAgentWithAgentHooks(...)` ·
`AgentHooksOptions`). Üç ölçüm bu yolu kapatır:

1. **Sözleşme yanlış ihtiyacı karşılıyor.** Kanca bir *enforcement/interception*
   sözleşmesidir (AGENT-HOOKS-0.1): allow/deny/transform verdict, approval
   seam, `InterceptionRecord`. Bu kalemin ihtiyacı olan **kesinti/devam**
   (turu checkpoint'leyip sonra sürdürme) yüzeyde **yoktur**.
2. **Bölünemez ve pipeline'ımızla uyumsuz.** Seam decorator'ları `internal`;
   PR gövdesi "partial installs are impossible by construction" diyor.
   `AsAIAgentWithAgentHooks`, içinde `FunctionInvokingChatClient` bulunan bir
   client'ı **reddeder** ("tools would execute below the verdicts") — Tracon'in
   her provider pipeline'ı `UseFunctionInvocation()` kurar
   ([`AnthropicChatClientFactory.cs:14`](../src/Tracon.Anthropic/AnthropicChatClientFactory.cs#L14) ·
   [`AzureOpenAIChatClientFactory.cs:16`](../src/Tracon.Azure/AzureOpenAIChatClientFactory.cs#L16) ·
   [`GoogleChatClientFactory.cs:15`](../src/Tracon.Google/GoogleChatClientFactory.cs#L15)).
   Kancayı almak, agent kurulum yolunun tamamını MAF'a devretmek demektir.
3. **Bağımlılık grafiği kabul edilemez.** `ResponsibleAI.AgentHooks`
   0.1.0-alpha.4 bir **native FFI** taşır (`libagent_hooks_ffi`, 1,7 MB) ve
   yalnız dört RID kapsar: linux-x64 · osx-arm64 · osx-x64 · win-x64 —
   **linux-arm64 yoktur**. Yanında `Microsoft.ML.Tokenizers`,
   `Microsoft.Extensions.AI.Evaluation`, `VectorData.Abstractions`,
   `Compliance.Abstractions`, `FileSystemGlobbing` gelir. `Tracon.Core` bugün
   AOT-uyumludur; bu graf hem onu hem "tüketicinin bağımlılık grafiğini
   kirletme" kuralını bozar.

**Kapsam:** Kalan kapsam F-141'in kapsamıdır: kesinti/devam'ı MAF'a kanca
takmadan çözmek. Tracon paralel bir kanca hiyerarşisi kurmaz (K3) — bu kural
MAF kancası alınmadığı için de geçerli kalır.

**Değer:** Kesintiye uğramış tur, MAF'ı sarmalamadan devam ettirilebilir.

**Mercek:** 2, 5, 6.

**Maliyet:** Ölçüldü, MAF yoluyla **karşılanamaz** (2. ve 3. madde).

**Risk:** Yüzeyin tamamı `[Experimental("MAAI001")]` ve paket alpha kanalında.
K-008 ön sürüm paketlerini yalnız `Tracon.AspNetCore` içinde tutar; bu kanca
`Tracon.Core`'un agent kurulum yoluna girer.

**Bağımlılık:** F-141 (MAF-kancasız alternatif tasarım) — rakip değil, **tek
yol**.

**Ekosistem:** 2026-09-05 — [`dotnet-1.19.0` sürüm notları](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.19.0) ·
[PR #7564](https://github.com/microsoft/agent-framework/pull/7564). MAF pin
2026-09-13 itibarıyla hâlâ **1.20.0**; ölçüm güncel.

**Karşı görüş:** Kalmadı. F-141 aynı ihtiyacı MAF'a hiç kanca takmadan
karşılıyor ve 2026-08-21'de bu tasarımın **doğru** olduğu kaydedilmişti.

---

### F-165 · Manuel kabul setinin CI'a kademeli taşınması

**Engel:** Bağımsız faz olarak **hiç** planlanmaz — kuyruğu bitmez. Her fazın
dokunduğu alanın manuel ailesi **o fazda** otomatikleştirilir
(`faz-tamamlama` Adım 3).

**Sorun:** Manuel set CI'da koşmaz; regresyon güvencesi bir kişinin koşum
zamanına bağlıdır.

**Kapsam:** Tek fazda tüm seti taşımak değil, bir aileyi
[test seviyeleri tablosuna](../.agents/ortak/test-seviyeleri.md) göre
otomatikleştiren tekrar edilebilir devir şablonu kurmak. Manuel kalması
gereken model-yanıtı ve insan-yargısı case'leri açıkça ayrılır.

**Değer:** En yüksek riskli kabul davranışları insan zamanı beklemeden
regresyon kapısına girer; iki ayrı spec/test kaynağı oluşmaz.

**Mercek:** 2, 3, 4, 6.

**Hazırlık — ölçüldü (2026-09-13):** `docs/manuel-test/` kökünde **37 dosya**,
**1 632** benzersiz `MT-*` case'i (kayıt 1 650 diyordu — bayat).
`Tracon.Testing` ve Testcontainers altyapısı hazır.

**Maliyet:** Yüksek, fakat ilk dilim kontrollüdür.

**Risk:** Case'leri kör biçimde birim teste çevirmek test tiyatrosu üretir.
Sınır davranışı functional/integration seviyesinde kalmalıdır.

**Bağımlılık:** Yok.

**Ekosistem:** 2026-08-26 — depo kalite disiplini; dış ekosistem iddiası yok.

**Karşı görüş:** Model kalitesi ve görsel değerlendirme otomasyona uygun
değildir. Bu aday o case'leri silmeyi değil, otomatikleştirilebilir kısmı
ayırmayı önerir.

---

### F-178 · Model deneme (attempt) telemetrisi

**Engel:** Gerçek bir üretim fallback gecikmesi olayı ölçülmedi. Tüketici
bunu **bilerek** erteledi.

> **Yarısı plana dönüştü.** Job/kuyruk metrikleri
> [Faz 133](arsiv/fazlar/133-IS-KUYRUGU-METRIKLERI.md)'e gitti. Aşağıdaki gövde
> yalnız **kalan yarıyı** anlatır.

**Sorun:** Yedek zincirinde hangi linkte ne kadar süre harcandığı ölçülmüyor.
`FallbackChatClient` döngü indeksini tutuyor ve `ModelFallbackUsed`'ı yazıyor,
ama `ModelFallbackUsedEventPayload` süre veya indeks taşımıyor. Birincil model
28 sn'de timeout olup yedek 2 sn'de yanıtladığında, 30 sn'lik `run`'ın
gecikmesinin hangi linkten geldiği ayrıştırılamaz.

**Kapsam:** Deneme başına süre (monotonik saat), deneme indeksi, sağlayıcı,
model ve sonuç kategorisi taşıyan bir `run` olayı veya `span`. Prompt ve yanıt
içeriği telemetriye **girmez**; sağlayıcıya özel request ID **çıkarılmaz**
(tüketici bu maliyeti kabul etti). Etiket kardinalitesi sınırlanır.

**Değer:** "Birincil timeout değeri düşürülmeli mi?", "Yedek ilk model olmalı
mı?" soruları kanıta dayanır.

**Mercek:** 2, 7.

**Hazırlık — ölçüldü (2026-09-13):** Boşluk hâlâ tam.
`grep -c "Stopwatch\|GetTimestamp\|Elapsed" src/Tracon.Core/Models/FallbackChatClient.cs`
→ **0**. Döngü indeksi zaten tutuluyor; ekleme tamamen additive'dir. Faz 133
metrik adı ve etiket kurallarını kurdu.

**Maliyet:** Ölçülmedi. Yeni tablo ve migration gerekmez.

**Risk:** Etiket kardinalitesi kontrolsüz büyürse metrik altyapısını boğar.

**Bağımlılık:** Faz 133 (kapandı).

**Ekosistem:** 2026-09-02 — tüketici raporu.

**Karşı görüş:** Tüketici bunu bilerek erteledi — *"Prodigy henüz production
olmadığı için gerçek incident kaydı sunamıyoruz … İlk fallback latency olayı
ölçüldüğünde bu talebi incident verisiyle yeniden açacağız."* Tek istediği,
API tasarımında bunu engelleyecek bir karar alınmamasıdır; bu koşul bugün
sağlanıyor.

---

### F-179 · Çalışma anı model yönlendirme policy'si

**Engel:** F-178 attempt süresini ölçmeye başlamalı **ve** gerçek üretim
trafiği oluşmalı.

**Sorun:** Model seçimi bugün statik binding ve hata sonrası yedek zinciriyle
sınırlı. Çağrı **öncesi** maliyet, gecikme ve capability'ye göre seçim
yapılamaz; yapılsa bile seçimin nedeni `run` kanıtına girmez.

**Kapsam:** Aday binding kümesinden seçim yapan opt-in bir policy seam'i ve
seçim kararının `run` kanıtına yazılması (seçilen binding, kararlı reason code,
değerlendirilen adaylar, policy adı).

**Değer:** Yönlendirme kararı ile `run` kanıtı aynı yerde durur.

**Mercek:** 2, 7, 8.

**Hazırlık — ölçüldü (2026-09-13), ön koşulların BİRİ karşılandı:**

| Ön koşul | Durum |
|---|---|
| `run` satırı sağlayıcıyı saklamalı | ✅ **karşılandı** — [`RunRecord.ModelProvider`](../src/Tracon.Abstractions/Runs/RunRecord.cs#L73) eklendi ([Faz 132](arsiv/fazlar/132-UYGULANAN-FIYAT-SNAPSHOTU.md)) |
| Kayan latency penceresi ölçülmeli | ❌ eksik — F-178 kapatır |
| Gerçek üretim trafiği | ❌ yok |

`ModelProviderHealthCache` yalnız sağlık durumu verir, gecikme vermez.

**Maliyet:** Ölçülmedi.

**Risk:** Ölçüm olmadan "en ucuzu seç" kararı yanlış olur.

**Bağımlılık:** F-178.

**Ekosistem:** 2026-09-02 — tüketici raporu.

**Karşı görüş:** Tüketici de *"önce doğru telemetry, sonra dinamik policy"*
diyor.

---

## Aday Olmayan Açık Kayıtlar

Bu kalemler faz sıralamasına **girmez**. Tam kanıt, geçmiş ve sonraki adım
keşif kaydındadır; burada yalnız hangi kanala düştükleri yazar.

| Kanal | ID'ler | Kural |
|---|---|---|
| **Plana dönüştü** | 40+ kalem · son: **F-234** → [Faz 171](arsiv/fazlar/171-DENETIM-IZI-YAZMA-POLITIKASI.md), **F-235** → [Faz 172](arsiv/fazlar/172-TEHDIT-MODELI.md) (2026-09-15) | Bölümleri bu dosyadan silindi; kanıt ve tasarım **fazın kendi dokümanındadır**. Aday listesine geri dönmezler. Eşleme tabloları ve aday gövdeleri: [`arsiv/PLANA-DONUSEN-ADAYLAR.md`](arsiv/PLANA-DONUSEN-ADAYLAR.md) |
| **Kapatılan kusur kayıtları** | F-106 · F-130 · F-137 · F-138 · F-139 · F-170 · F-180 · F-181 · F-190 · F-197 · F-203 · F-204 · F-206 · F-211 · F-212 · F-214 · F-215 · F-219 · F-220 · F-222 | Gövdeleri [`arsiv/ERTELENEN-ADAYLAR.md`](arsiv/ERTELENEN-ADAYLAR.md)'dedir. Yeniden görülürse **yeni** kusur kaydı açılır. **F-180** özetiyle § *Bekleyen Kalemler*'de kalır: vakası kapandı, **sınıfı açık** |
| **Karar / uyumluluk eşiği** | F-72 · F-90 · F-91 · F-92 · F-132 · F-169 | Mevcut karar veya dış bağımlılık değişmeden planlanmaz. **F-169** (MAF CodeAct / Hyperlight sandbox) F-72 ile **aynı eşiktedir**: paket GA ve taşınabilir olana kadar planlanmaz — ölçüm [`kesif/2026-08-26-yeni-feature-fikirleri.md`](kesif/2026-08-26-yeni-feature-fikirleri.md) § 9 |
| **Ölçüm bekliyor — F-ID'leri** | F-51 · F-94 · F-96 · F-97 · F-99 · F-101 · F-123 · F-128 · F-154 · F-156 · F-157 · F-159 · F-160 · F-161 · F-162 | Her biri için gereken somut kanıt keşif kaydında yazılıdır. Kanıt üretmeden aday olmaz |
| **Ölçüm bekliyor — A-ID'leri** | A01 (kalan manifest bağlama) · A02 · A03 · A05'in ETag ve provenance dilimleri · A06 · A07 · A08 · A09 · A10 · A11 · A12 · A13 · A14 · A15 · A17 · A18 | 🚨 Bunlar **F-NN değildir** — 2026-09-07 tüketici analizi turunun rapor içi izleme kimliğidir ve F numarası ayrılmadı; biri seçilirse o zaman aday numarası alır. Değer/maliyet/risk yargısı ve gereken kanıt [tam raporun](arsiv/incelemeler/2026-09-07-tuketici-analizi-codebase-olcumu.md) §5'indedir. **A17/A18 mimari ret DEĞİLDİR** — mevcut yetenekle çözülemeyen somut bir vaka çıkarsa yeniden değerlendirilirler |
| **Arşivlendi / birleştirildi / rutin bakım** | F-48 · F-88 · F-89 · F-98 · F-144 · F-145 · F-146 · F-147 · F-148 · F-155 · F-158 · F-163 · **F-221** | Plan değeri yok; rutin bakım olarak kalır veya aktif adayla aynı tasarım işidir. **F-221** 2026-09-13'te indirildi: görsel yarısı Faz 163'te kapandı, kalan iz `src/` içinde 4 dosyada 27 `internal` yerel değişken adıdır (`prismOptions`/`prismException`) — o dosyalara dokunan ilk oturum düzeltir |

### F-ID tahsis kuralı

Numara **geri dönüştürülmez** ve bir numara **tek kaleme** aittir. Sıradaki
numara: **F-233**.

**F-232** 2026-09-15'te kullanıcının tüketici skill'i fikrine tahsis edildi ve
§ *Sıralanabilir Adaylar* içine yazıldı.

**F-231** 2026-09-14'te `nuget-danismani` turunun 4. bulgusuna (options
düzeyinde production doğrulayıcısı yok) tahsis edildi ve **aynı gün plana
dönüştü** — [Faz 170](arsiv/fazlar/170-PRODUCTION-PROFIL-KAPISI.md). Gövdesi doğrudan faz
dokümanına yazıldı; aday listesinde hiç durmadı.

**F-230** 2026-09-13'te Faz 168 denetiminin 🟢 6 bulgusuna tahsis edildi.

**F-227 · F-228 · F-229** 2026-09-13'te tahsis edildi ve **aynı gün plana
dönüştü** — Faz 167 · 168 · 169. Gövdeleri
[`arsiv/PLANA-DONUSEN-ADAYLAR.md`](arsiv/PLANA-DONUSEN-ADAYLAR.md) üzerinden
faz dokümanlarına taşındı.

🚨 **F-221 ve F-222 bu kurala 2026-09-08 ile 2026-09-12 arasında uymadı** —
ikisi de iki kez tahsis edildi. 2026-09-13'te çözüldü (kullanıcı kararı):

| Numara | Kime ait | Yanlış tahsis nereye gitti |
|---|---|---|
| **F-221** | Logo/favicon ve prizma metaforu (Faz 162 · 164 · 165) — bugün rutin bakım | [Faz 159](arsiv/fazlar/159-TIPLI-ISTEMCIDE-AKISLI-OPENAI-CAGRISI.md) denetim bulgusu 🟢 3 (SSE şeması) → **F-226** |
| **F-222** | `nav.*` ekran kapısı (Faz 163 · 164) — ✅ kapandı | [Faz 159](arsiv/fazlar/159-TIPLI-ISTEMCIDE-AKISLI-OPENAI-CAGRISI.md) denetim bulgusu 🟢 2 (site ağırlık marjı) → **kalem kapatıldı**, [K-756](KARARLAR-INDEKS.md) kapsıyor |

**Site ağırlık kalemi neden kapatıldı:** kayıt bayattı (tavan 57 000 B, sayfa
56 651 B, taban 49 376 B diyordu). 2026-09-13 ölçümü: tavan **58 000 B**,
en ağır sayfa **57 367 B**, marj **%1,1**. K-756 bir sonraki adımı zaten
yazıyor — *"Bir daha dolarsa sayfa BÖLÜNÜR veya içerik kısalır; tavan İKİNCİ
kez yükseltilmez."* Aynı kuralı ikinci bir yerde bayatlatarak tutmanın değeri
yok.

---

## Bilerek Önerilmeyenler

Reddedilmiş mimari işler için tek kaynak
[`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md)'dir.
Özellikle **F-91** (`secret` saklama sınırı) ve **F-92** (dağıtık hız
sınırı / Redis) kararı değiştirmeden yeniden aday olmaz.

**F-95 bu listede değildir** — onu bekleten bir tasarım kararı değil, MAF'ın
sözleşmesidir; bkz. § *Bekleyen Kalemler* → F-95.

### Zaten var — bir daha "eksik" diye önerilmez

2026-09-07 Langfuse turunda üçü de kod ölçümüyle çürütüldü. Bunlar mimari ret
değildir; **mevcut yeteneklerdir.**

| Önerilen | Nerede zaten var |
|---|---|
| Eval suite'i için CI kapısı | `tracon eval --min-pass-rate --max-failures`, regresyonda çıkış kodu 3 ([`EvalCommand.cs:168`](../src/Tracon.Cli/Commands/EvalCommand.cs#L168)) |
| Skor düşüşünde alarm | `WebhookEvents.RunScoreLow`, `MinSampleSize` gürültü eşiğiyle ([`OnlineEvalSummaryService.cs`](../src/Tracon.Core/Evaluation/OnlineEvalSummaryService.cs)) |
| Agent sürümüne `production`/`staging` label'ı | `Experiment` sürüm başına ağırlıklı varyant veriyor, `IAgentDefinitionStore.RollbackAsync` geri alıyor; ortam ayrımını kiracı sınırı çözüyor |

Aynı turun bulgusu şuydu: Langfuse'un **beş sütununun beşi de** bu repo'da
zaten vardı (prompt sürümleme → `IAgentDefinitionStore` · LLM-as-judge →
`IRunJudge` · gold dataset → `RunToCasePromoter` · maliyet-gecikme panosu →
`RunStatistics` · deney → `Experiment` + canary). Tur başlıklara değil
**kenarlara** yöneldiği için işe yaradı.

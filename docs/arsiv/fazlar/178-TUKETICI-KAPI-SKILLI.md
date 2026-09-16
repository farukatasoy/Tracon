# Faz 178 — Tüketici Kapı Skill'i

> **Durum:** ✅ Tamamlandı (2026-09-16)
> **Plan onayı:** onaylandı (kullanıcı, 2026-09-16) — Açık Soru 1 → A, 3 → B (4 KB)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-232**
> **Önkoşul:** [Faz 73](73-TUKETICI-AGENT-DESTEGI.md) (bilgi kanalı: harita, `AGENTS.md`, yerel referans) · [Faz 167](167-AGENT-ZORLAMA-KATMANI.md) (zorlama kanalı: `TRC0*` diagnostic'leri **ve** § 167.3 ölçüm emsali)
> **Paketler:** `Tracon.Cli`, `Tracon.Generators`
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** büyüyor — yalnız **CLI yüzeyinde**; `src/` çekirdeğine dokunulmaz
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/coding-agent-support.md` · sevk edilen: yeni CLI komutu, yeni `TRC04xx` diagnostic metni
> **Manuel test alanı:** `docs/manuel-test/29-AGENT-DESTEGI.md`

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 74650dda:docs/arsiv/fazlar/178-TUKETICI-KAPI-SKILLI.md
> ```
>
> Damıtıldı 2026-09-16 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon bugün bir coding agent'a iki kanal veriyor: | Kanal | Nasıl | Ne zaman konuşur | |---|---|---| | **Bilgi** | `Tracon.AgentMap.md` → `AGENTS.md`, `Tracon.LocalReference.md`, `llms.txt` | Okunmayı **bekler** | | **Zorlama** | Dokuz `TRC0*` usage diagnostic | Kod **yazıldıktan sonra** | İkisi de geç konuşur.

## Bitiş Ölçütleri (DoD)

- [x] 🚨 §178.1 ölçümü koşuldu; **her formatın** sonucu (yüklendi / yüklenmedi / ölçülemedi) bu dokümana yazıldı
- [x] Ölçümü geçemeyen formatlar kapsamdan **düştü** ve gerekçesi yazıldı (K-796)
- [x] `tracon agent-skill` var olan bir dosyaya `--force` olmadan **hiç** dokunmuyor (`Existing_file_is_never_touched`, hash karşılaştırması)
- [x] Üretilen dosya harita revizyon damgasını taşıyor; damga analyzer'ın aradığı dizeye **testle bağlandı**
- [x] Bayat skill `TRC0403` uyarısı üretiyor; güncel skill **üretmiyor** — üreteç biriminde (6 olgu) **ve gerçek pakette** (3 olgu, `TemplateAgentsFileTests`)
- [x] Tek emitter tek kanonik metinden türüyor (ölçüm üçünü düşürdü — Sapma 1); `The_shell_carries_the_canonical_text_verbatim`
- [x] Komut hedef dizinin dışına yazamıyor (`Output_stays_within_the_target_directory`, tüm ağaç taranıyor)
- [x] İptal yarım dosya **ve boş dizin** bırakmıyor; mekanizmayı ısıran test `A_failed_write_leaves_no_temporary_file_behind` (Sapma 8)
- [x] Üretilen metin bayt bütçesinin altında (**1 540 B** / 4 096 B)
- [x] Doğrulama kapıları yeşil. (Kapanış sırasında `ObjectToolAotPackageTests` kırmızıydı ve sebebi repo değildi: Xcode 27 kurulmuş ama **lisansı kabul edilmemişti**, `/usr/bin/cc` her derlemeyi reddediyordu. Kullanıcı `sudo xcodebuild -license accept` koştu; test 1/1 geçti, yayın provası Native AOT smoke dahil tam yeşil oldu.)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı (`status = Completed`)
- [x] `secret` taraması boş döndü (`kapi.py tarama` ✅)
- [x] Manuel kabul case'leri `docs/manuel-test/29-AGENT-DESTEGI.md` içine eklendi (`MT-AGD-022…024`); 022 ve 024 koşuldu, 023 üreteç+paket testleriyle kapsanıyor
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (2 gerçek kapandı, 1 bayattı)
- [x] `docs-site/` güncellendi; `npm run check` (dört alt kapı) temiz

### Doğrulama komutları

```bash
# İzole ölçüm (1. adım) — geçici dizinde
mkdir -p /tmp/tracon-skill-olcum && cd /tmp/tracon-skill-olcum
dotnet new tracon-api && tracon agent-skill --format all
# sonra her harness'ta AYIRT EDİCİ kontrol koşumunu çalıştır

# Var olan dosyaya dokunulmuyor mu
tracon agent-skill --format claude
sha256sum .claude/skills/tracon/SKILL.md > /tmp/before
tracon agent-skill --format claude
sha256sum -c /tmp/before      # eşleşmeli

# Bayatlık uyarısı
sed -i.bak 's/revision: [0-9a-f]*/revision: deadbeef/' .claude/skills/tracon/SKILL.md
dotnet build      # TRC0403 vermeli
```

---

## Plandan Sapmalar

### 1. Dört emitter yerine BİR — ölçümün amacı buydu

Plan "tek kanonik metin, N ince emitter" diyordu. §178.1 üçünü düşürdü, geriye
bir kabuk kaldı. Ayrım yine de korundu: `GateSkillText.Body` formatsız
prosedürdür, `ClaudeCodeSkill(revision)` onu sarar. DoD'nin "dört emitter tek
metinden türüyor" satırı bu yüzden **tek emitter** için yazıldı ve testi
(`The_shell_carries_the_canonical_text_verbatim`) kabuğun gövdeyi birebir
taşıdığını kanıtlar — ikinci harness geldiğinde metni çatallamak yerine sarması
gerektiğini sabitleyen şey budur.

### 2. Marker ilk satırda DEĞİL — plan öyle varsayıyordu

Plan damgayı haritanınki gibi ilk satıra koyuyordu. Bir skill dosyası front
matter ile başlar; ilk satıra HTML yorumu koymak YAML'ı bozar ve harness
skill'i **hiç yüklemez**. Damga front matter'ın altına indi ve analyzer ilk
satır yerine **ilk 16 satırı** tarıyor (`ReadGateSkillRevision`). Varsayım
tahmin edilmedi, üçüncü bir ölçüm turuyla doğrulandı (yukarıda).

### 3. Revizyonu CLI'ın kendisi taşıyor — plan kaynağı söylemiyordu

`tracon` ayrı kurulur; yanında okunacak paket yoktur. Harita
`Tracon.Cli.csproj` içinde `EmbeddedResource` olarak gömüldü. Sonucu bir
sözleşme farkıdır ve tanının metnine girdi: damgayı **tool** taşır, bu yüzden
bayat skill'in düzeltmesi "dosyayı sil, tekrar koş" değil, **önce tool'u
güncelle**'dir. Aksi hâlde eski tool aynı bayat değeri geri yazar ve tanı
sonsuz döngüye girerdi (`analyzer-yazimi.md`'deki Faz 78 🔴 sınıfı).

### 4. Tüketici sayfası `coding-agent-support.md` değil `coding-agents.md`

Plan başlığı yanlış yazmıştı; site sayfasının gerçek adı
`docs-site/src/content/docs/guides/coding-agents.md`. Tanının help link
anchor'ı (`#coding-agent-support`) ise `capabilities.md` başlığıdır ve
doğrudur — ikisi farklı şeylerdir.

### 5. `--format all` yazılmadı

Plan `all`'ı varsayılan yapıyordu. Tek format kaldığı için "hepsi" bir üyelik
kümedir ve gürültüdür. Varsayılan doğrudan `claude`'dur; tanınmayan bir değer
**argüman hatasıdır** (Açık Soru 4 → A'nın ruhu: sessiz daraltma yok).

### 6. Sayfa ağırlığı tavanı yükseltildi — bu fazın maliyeti

`troubleshooting.md` `TRC0403` bölümüyle 58 391 B'ye çıktı; tavan 58 000'di ve
faz öncesi sayfada **15 B** boşluk kalmıştı. Alternatifler ölçüldü (bölüm 450 B,
tablo satırı 36 B, metni kısaltmak 80 B geri kazandırdı), tavan ölçümle birlikte
59 000'e çıkarıldı ve `check-weight.mjs` yorumuna bir sonraki adımın sayfayı
**bölmek** olduğu yazıldı.

### 7. 🚨 Faz ortasında dış bir süreç commit attı ve iş karıştı

04:56'da bu oturumun dışından bir commit (`c0959952`) yarım kalmış Faz 178
işini Faz 177 başlıklı bir mesajın altına süpürdü. Ayrıca bu oturum, taban
ölçümü için denediği tek dosyalık `git stash push` hiçbir şey stash'lemediği
hâlde ardından `git stash pop` koştu ve **ilgisiz, önceden duran** bir stash'i
ağaca boşalttı (üç dosyada çatışma, 21 AgentPrism artığı). İkisi de kullanıcıya
bildirildi ve kullanıcının kararıyla düzeltildi: commit `--soft` reset ile
ikiye ayrıldı, artıklar silinmek yerine taşındı, stash kaydı korundu.

**Ders:** `git stash push -- <yol>` hiçbir şey stash'lemezse (çıktı: `No local
changes to save`) ardından gelen `git stash pop` **senin kaydını değil,
listedeki ilk kaydı** açar. Taban ölçümü için stash yerine `git worktree add`
ya da `git show HEAD:<yol> > <geçici>` kullan.

### 8. Ölçüm kusuru: `Cancellation_leaves_no_partial_file` TİYATROYDU

İlk yazılan test yeşildi ve geçici dosya mekanizmasını hiç kapsamıyordu:
mutasyon (geçici dosya + move yerine doğrudan yazma) testi **kırmızıya
çevirmedi**, çünkü `File.WriteAllTextAsync` zaten iptal edilmiş bir token'da
dosyayı açmadan düşer. İkinci mutasyon (temizliğin kaldırılması) da yeşil
geçti. Test, kanıtladığı şeye göre yeniden adlandırıldı
(`Cancellation_before_the_write_creates_nothing`) ve mekanizmayı gerçekten
ısıran bir test eklendi: hedef yolu bir DİZİN yaparak `File.Move`'u düşüren
`A_failed_write_leaves_no_temporary_file_behind` — bu test temizlik kaldırılınca
kırmızı yanar (ölçüldü).

## Bu Fazda Verilen Kararlar

| # | Karar | Nerede |
|---|---|---|
| **K-795** | `tracon` komutu tüketicinin çalışma ağacına yazar; sözleşme "yalnız yokken yaz"dır ve varsayılan hedef **repo köküdür** | `KARARLAR.md` |
| **K-796** | Bir agent skill formatı **ölçülmeden** sevk edilmez; ölçüm ayırt edici olmak zorundadır | `KARARLAR.md` |
| **K-797** | Damgayı **paket değil tool** taşır; bayat skill'in düzeltmesi "önce tool'u güncelle"dir | `KARARLAR.md` |

## Tüketici Yüzey Envanteri

`tuketici-dokuman-senkronu` koşuldu. Dokunulan yüzeyler:

| Kova | Yüzey | Elle / üretilen |
|---|---|---|
| `docs-site/` | `guides/coding-agents.md` (yeni bölüm) · `guides/cli.md` · `troubleshooting.md` · `capabilities.md` · `packages.md` | elle |
| `docs-site/` | `llms.txt` · `llms-full.txt` · `Tracon.AgentMap.md` | üretilen, commit edilir |
| Sevk edilen metin | `src/Tracon.Cli/README.md` · `Tracon.Cli.csproj` `Description` · `Program.cs` yardım metni · `GateSkillText` gövdesi | elle |
| Yerel referans | değişmedi — kapı skill'i `Tracon.LocalReference.md`'yi **adıyla işaret eder**, o dosyanın kendisi değişmez | üretilen |

Kapılar (skill Adım 5, dördü de yeşil): sevk edilen metin **6/6** ·
`LocalReferenceTests` **10/10** · `build-agent-map --check` *up to date and
within budget* · `npm run check` (içerik · derleme · bağlantı · ağırlık)
`Links: 188 739 · SEO 0 hata · Weight heaviest 58 393 B / 59 000 B`.

### 🚨 `cekirdek-kavram` kuralı karşılanmadı — gerekçe

`--site-denetle` dört kural tetikledi; üçü karşılandı. `cekirdek-kavram`
(`src/Tracon.Core/buildTransitive/` → `concepts/`) **karşılanmadı** ve bu
bilinçlidir:

- O dizinde değişen iki dosya `Tracon.Core.targets` (yeni `AdditionalFiles` +
  `NoWarn`) ve **üretilen** `Tracon.AgentMap.md`'dir. Birincisinin tüketiciye
  dönük yüzü `capabilities.md`'dir ve o kural ✅ karşılandı; ikincisi zaten
  `capabilities.md`'den üretilir.
- `concepts/` sekiz sayfadır (`agents` · `runs` · `sessions` · `tools` ·
  `workflows` · `evaluation` · `governance` · `index`) ve **hiçbiri** kod
  agent'ı desteğini anlatmaz; o konu `guides/coding-agents.md`'dedir ve bu
  fazda genişletildi. `concepts/agents.md` Tracon agent'larını anlatır, kod
  agent'larını değil — oraya kapı skill'i yazmak sayfayı konusundan saptırırdı.

∴ `--site-gerekce-yazildi` ile geçildi.

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — plan onaylandı, üç açık soru kullanıcıya soruldu (1 → A, 3 → B, ölçüm kapsamı → yalnız Claude) |
| Düzeltme turu sayısı | 3 — (1) test tiyatrosunun kendi tespiti, (2) denetim 🔴/🟡 kapanışı, (3) kapı kızarması (`Every_usage_diagnostic_reaches_a_real_consumer_project`) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | **2 / 0 / 0** + 1 bayat (denetçi ağacı okuduğunda düzeltme henüz yoktu) |
| Fazın ürettiği regresyon | **1** — `DiagnosticIds`'e `TRC0403` eklemek `Every_usage_diagnostic_reaches_a_real_consumer_project`'i kırdı (o tüketicide kapı skill'i yoktu). Kapanış kapısı yakaladı; düzeltme: teste bayat kapı skill'i de yazılıyor |
| Faz kapandıktan sonra bulunan kusur | — |

**Kendi kendini yakalayan ölçüm.** Bu fazın en pahalı dersi kapanışta değil
uygulama sırasında çıktı: ilk yazılan iptal testi yeşildi ve **hiçbir şeyi
kapsamıyordu**. İki ayrı mutasyon (geçici dosyayı kaldır · temizliği kaldır)
testi kırmızıya çeviremedi. Bu fazda yazılan **her** yeni kapı testi ondan
sonra mutasyonla doğrulandı; beşi de kırmızı yandı (ölçüldü).

## Örnek Uygulama Koşumu

Bu faz `src/` çekirdeğinin çalışma anı yolunu değiştirmez; koşum bir
**regresyon kanıtıdır** (Faz 167 emsali).

```bash
dotnet run --project samples/Tracon.Api --no-build -c Release --urls http://localhost:5081
curl -s -X POST http://localhost:5081/tracon/api/agents/cached-support/run \
  -H "Content-Type: application/json" -d '{"message":"Phase 178 regression proof"}'
```

Açılış temiz: `MCP discovery completed: 0 tools available.` ·
`Now listening on: http://localhost:5081`. `fail:`/`crit:`/`Unhandled`
satırı **sıfır** (sayıldı: 0).

Kayıt gerçekten yazıldı (`GET /tracon/api/runs/{id}`):

| Alan | Değer |
|---|---|
| `id` | `01a0a898-b9a2-7fbe-8d86-068128fa478d` |
| `agentName` | `cached-support` |
| `status` | **`Completed`** |
| `startedAt` → `completedAt` | `05:02:56.464` → `05:02:56.756` (292 ms) |

Ayrıca **gerçek repo içinde** komutun kök çözümü ölçüldü:
`src/Tracon.Cli/` altından koşulan `tracon agent-skill --json`
`/Users/.../Tracon/.claude/skills/tracon/SKILL.md` yazdı — yani alt dizinden
koşulsa bile repo kökü. (Ölçüm artığı ağaçtan kaldırıldı.)

🚨 Ölçümün yan bulgusu: bu depoda `.claude/skills` bir **symlink**'tir
(`.agents/skills`), ve komut symlink'i izleyip oraya yazdı. Tüketici tarafında
sorun değildir, ama bu depoda komutu denemek kendi skill dizinini kirletir.

## Denetim Bulguları

`faz-denetim` taze bağlamlı `faz-denetcisi` ile koşuldu (2026-09-16). Denetçi
ağaca yazmadı. **🔴 3 · 🟡 4 · 🟢 3.** Triyaj kullanıcıya soruldu (Adım 5.1).

### 🔴 — üçü de kapandı

| # | Bulgu | Triyaj | Sonuç |
|---|---|---|---|
| 1 | Sevk edilen kurulum komutu paket kimliği yerine tool komut adını veriyordu (`dotnet tool install -g tracon`); paket `Tracon.Cli` | **gerçek** (kullanıcı) | `coding-agents.md`, `troubleshooting.md` düzeltildi; `llms*` yeniden üretildi. `packages.md` satırına `agent-skill` eklendi |
| 2 | Komut **çalışma dizinine**, build **repo köküne** bakıyordu: alt dizinden koşan tüketici kökteki dosyayı siler, yenisi alt dizine düşer, uyarı kaybolur ve düzeldi sanılır | **gerçek** (kullanıcı) | Varsayılan çıktı **repo kökü** oldu (`RepositoryRootAbove`, build'in `.git` sondasının aynısı). Testi: `Without_an_output_the_file_lands_at_the_repository_root` — varsayılanı geri çevirince **kırmızı yanıyor** (ölçüldü). `TRC0403` mesajı da artık kökü adlandırıyor |
| 3 | Teslim yolu (MSBuild → `AdditionalFiles` → analyzer) hiçbir seviyede test edilmiyor | **bayat** | Denetçi ağacı okuduğunda henüz yoktu: `TemplateAgentsFileTests`'e üç gerçek-paket olgusu eklenmişti. Mutasyonla doğrulandı — `AdditionalFiles` `ItemGroup`'u kaldırılınca `A_gate_skill_from_an_older_map_is_reported_as_stale` **kırmızı yanıyor** |

### 🟡 — dördü de kapandı

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | Damga dizesi üç yerde elle kopya; üretici ↔ tüketici bağlı değil | `The_marker_the_command_writes_is_the_one_the_analyzer_looks_for` analyzer **kaynağını okuyup** sabiti karşılaştırıyor. Analyzer sabitini bozunca kırmızı yanıyor (ölçüldü) |
| 2 | İptal edilen koşum boş `.claude/skills/tracon/` dizini bırakıyordu | `ThrowIfCancellationRequested` dizin yaratmanın **önüne** alındı; iddia `GetFileSystemEntries`'e yükseltildi |
| 3 | DoD'deki "örnek uygulama ile gerçek run" satırının kanıtı yoktu | Koşuldu; çıktı yukarıdaki bölümde |
| 4 | Değersiz `--format` sessizce `claude`'a düşüyordu | Artık argüman hatası; `A_format_flag_with_no_value_is_an_argument_error` |

### 🟢 — adaya

| # | Bulgu | Neden şimdi değil |
|---|---|---|
| 1 | `coding-agents.md` "Eight diagnostics" diyor; kategoride **on** var — `TRC0501`/`TRC0502` o sayfanın tablosunda hiç yok | Faz öncesinden gelen kusur ("Seven" iken de dokuz vardı); düzeltmek iki satır daha eklemek demek, kapsam dışı. `ADAYLAR.md`'ye girdi |
| 2 | `ResolveTarget`'ın kaçış koruması bugün ulaşılamaz | Göreli yol sabit; ikinci format gelince ısırır. `<remarks>` bunu zaten yazıyor |
| 3 | `MaximumBytes = 4096`, üretilen dosya 1 540 B | Tavan bilinçli sözleşme; ölçüldü, uydurulmadı |

## Sonraki Faza Devir Notu

**Kapı skill'ine dokunacak faz için:**

- 🚨 Skill formatı **ölçülmeden** eklenmez. `.agents/` ölçüldü ve **düştü**;
  Cursor ve Copilot bu makinede ölçülemedi. Yeni bir format eklemek, önce
  §178.1'deki ayırt edici koşumu o harness'ta tekrarlamaktır — kanıt `Skill`
  tool çağrısının kaydıdır, modelin "yükledim" demesi değil.
- Marker **ilk satırda değildir** ve olamaz: front matter'ı bozar, harness
  skill'i hiç yüklemez. Analyzer ilk **16 satırı** tarar.
- Damgayı **tool** taşır (gömülü harita), paket değil. Bayat skill'in düzeltmesi
  bu yüzden "önce tool'u güncelle"dir; mesajı gevşetmek tanıyı sonsuz döngüye
  sokar.
- Komutun varsayılan çıktısı **repo köküdür** ve bu, build'in baktığı yerle
  eşleşmek zorundadır. İkisinden birini değiştiren, diğerini de değiştirmelidir
  (`AgentSkillCommand.RepositoryRootAbove` ↔ `Tracon.Core.targets`
  `_TraconAgentsFileRoot`).

**Bu depoda komutu denerken:**

- `.claude/skills` burada `.agents/skills`'e **symlink**'tir. `tracon
  agent-skill`'i repo kökünde koşmak kendi skill dizinini kirletir; geçici bir
  dizinde `--output` ile dene.

**Sonraki faz için açık kalan:**

- 🟢 `coding-agents.md` diagnostic tablosu `TRC0501`/`TRC0502`'yi hiç
  saymıyor ve sayfa "Eight diagnostics" diyor; gerçek sayı **on**.
  `ADAYLAR.md`'ye girdi.
- ✅ **AOT kapısı çözüldü — ama teşhis ilk seferde YANLIŞ kondu.** Belirti
  `ld: tapi error … unknown architecture` idi ve bu oturum bunu "CLT kurulumu
  bozuk" diye yazdı. Gerçek sebep Xcode 27'nin **kabul edilmemiş lisansıydı**:
  `/usr/bin/cc` hiçbir derlemeyi başlatmıyordu. Ayırt eden ölçüm, shim'i atlayıp
  `XcodeDefault.xctoolchain/usr/bin/clang`'i doğrudan çağırmaktı — o üç SDK ile
  de sorunsuz linkledi, yani toolchain sağlamdı. Ders: iki satırlık bir C
  programı linklenmiyorsa sebep repo değildir, ama "hangi" sistem sorunu
  olduğunu belirtinin kendisi söylemez. Ayrıntı ve tek satırlık ayırt etme
  komutu: [`hafiza/test-kosum-tuzaklari.md`](../../hafiza/test-kosum-tuzaklari.md).

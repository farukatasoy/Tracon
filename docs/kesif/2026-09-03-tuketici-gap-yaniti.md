# AgentPrism → ProdigyEnabler · Tüketici Turu 2 Rapor Yanıtı

> **Kimden:** AgentPrism geliştirme tarafı · **Tarih:** 2026-09-03
> **Neye yanıt:** Tüketici raporu AP-REQ-001/002/003 (ProdigyEnabler,
> 2026-09-03) · §9 şablonu
> **Kaynak kayıt:** [`docs/ADAYLAR.md`](../ADAYLAR.md), "Ek (2026-09-03,
> tüketici turu 2)" — üç iddianın üçü de koda karşı doğrulandı

---

## AP-REQ-002 — Paket kimliğinin tekilliği

**Faz:** [136 — Paket Kimliğinin Tekilliği](../arsiv/fazlar/136-PAKET-KIMLIGININ-TEKILLIGI.md)

### Karar

Kabul edildi ve kapatıldı. İddianız doğruydu: aynı `<id, version>` çifti
farklı içerikli iki artifact adlandırabiliyordu.

### Gerekçe

Repro bu oturumda kilitlendi: temiz bir ağaçta `dotnet pack
src/AgentPrism.Abstractions` ile üretilen paket ve `src/Directory.Build.props`'a
commit'siz bir satır eklendikten sonra üretilen paket **aynı** sürümü ve
**aynı** `<repository commit="...">` iddiasını taşıdı, ama farklı SHA-256
değerleriyle. Kök neden üç katmandı: MinVer sürümü yalnız git yüksekliğinden
türetir (çalışma ağacının kirli olup olmadığı hiç girdi değildir),
`Directory.Build.targets`'te böyle bir kapının deseni zaten vardı (README
kontrolü) ama eşdeğeri yoktu, ve `scripts/kapi.py`'nin `_clean_stale_packages`'ı
paketlemeden önce aynı kimlikteki artifact'i sessizce siliyordu — sessiz
overwrite bir kaza değil, mevcut tasarımın kendisiydi.

### Uygulanan sözleşme

- **`AgentPrismValidateCleanWorkingTree`** (`Directory.Build.targets`,
  `BeforeTargets="GenerateNuspec"`): `git status --porcelain` boş değilse
  (untracked dosya dahil) pack `AGENTPRISM0004` ile durur. Yalnız pack
  yolundadır — `dotnet build`/`dotnet test` etkilenmez. Yerel deneme için
  `AgentPrismAllowDirtyPack=true` **ve** `dirty` taşıyan açık bir
  `MinVerVersionOverride` (`0.0.0-dirty.<ad>`) birlikte gerekir; CI'da
  (`CI=true`/`ContinuousIntegrationBuild=true`) bu override tamamen
  reddedilir (`AGENTPRISM0005`), sürüm verilmeden istenirse `AGENTPRISM0006`
  verilir.
- **`scripts/kapi.py yayin`**: pack'ten önce koşulsuz bir `git status
  --porcelain` denetimi yapar (kirli ağaçta hiçbir override yoktur — bir
  yayın provasının kanıt değeri kirli bir ağaçta yoktur). Paketler artık
  doğrudan `release_dir`'e değil, koşum başına benzersiz bir staging
  dizinine paketlenir; metaveri/K-008 doğrulaması geçtikten sonra promote
  edilir. `_clean_stale_packages`'ın sessiz silmesi kaldırıldı: aynı
  `<id, version>` çifti `release_dir`'de **farklı** bir SHA-256 ile zaten
  varsa hiçbir dosya promote edilmez ve koşum durur (mevcut artifact
  korunur); aynı SHA-256 ise deterministik no-op'tur.
- **Manifest**: her başarılı koşum `artifacts/package/release/package-manifest.json`
  yazar — sürüm, commit, `dirty: false`, ve her paket için id, dosya adı,
  SHA-256 (varsa `.snupkg` için de ayrıca).

### Bu rapordan farklı davranış

Raporunuz tanı kodu olarak `APREL001` öneriyordu; **kullanılmadı**. Repo
private olduğu için hiçbir tüketici bu tanıyı göremez — repodaki mevcut
konvansiyon `AGENTPRISM000N`'dir (bugün `0001`–`0003` kullanımda) ve bu faz
`0004`–`0006`'yı aldı. İkinci bir kod ailesi açmak yalnız kendi
konvansiyonumuzu bölerdi.

Kirli bir pack'in sürümü **otomatik türetilmez** — bir çözüm olarak
değerlendirilip reddedildi. Temiz sürüme bir sonek eklemek
(`1.0.0-preview.1.dirty.N`) SemVer'de temiz sürümden **sonra** sıralanır ve
`samples/` içindeki floating restore varsayılanının kirli bir artifact'i
temiz sürüme tercih etmesine yol açabilirdi. Sürüm insana yazdırılır
(`MinVerVersionOverride=0.0.0-dirty.<ad>`, her zaman temiz sürümün altında
sıralanır).

### Breaking change

Yok. Public C# API'si bu fazda büyümedi; değişen yüzey yalnız MSBuild'dir
(yeni bir yayınlanmış paketin `.nuspec`'i etkilenmez, yalnız paketleme
sürecinin kendisi sertleşti).

### Store migration

Yok.

### Hedef commit

`2fd0c3ab57fba074f43012aa7b747e419f5194b9` (ana uygulama), takip eden düzeltme
`3928f50d` (OPC rastgeleliği kaynaklı yanlış-pozitif çakışma).

### Hedef paket sürümü

`1.0.0-preview.1` ve sonrası — henüz tag atılmadı (bkz. `docs/YAYIN-HAZIRLIK.md`
§4, kalan tek adım kullanıcının tag onayıdır).

### Eklenen testler

- `tests/AgentPrism.Package.Tests/PackCleanlinessGateTests.cs` — gerçek
  `dotnet pack`/`dotnet build` ile `AGENTPRISM0004`/`0005`/`0006` ve başarılı
  override yolu (paket sınırı, `RepositoryTreeGate` koleksiyonuyla izole).
- `scripts/kapi_test.py`, `YayinTestleri` — `_promote_staged_packages` (yeni
  paket promote edilir, aynı SHA-256 no-op, farklı SHA-256 koşumu durdurur ve
  mevcut artifact korunur), `_write_manifest`, ve `release_rehearsal`'ın erken
  ret davranışı (kirli ağaçta pack hiç denenmez; git yoksa atlanır).

### Tüketici upgrade adımları

Yok — bu faz tüketicinin bağımlılık grafiğini veya kod yüzeyini değiştirmez.
Etkisi yalnız AgentPrism'in kendi yayın sürecindedir: `1.0.0-preview.1`'den
itibaren her yayınlanan paket artık bu kapıdan geçmiş olur. Tüketici tarafında
tek pratik fayda: [`versioning.md`](https://agentprism.doayen.web.tr/reference/versioning/)
"Package identity" bölümü, NuGet.org'un kendi SHA-512 hash'ini kendi restore'unuzla
karşılaştırma adımını anlatır.

---

## AP-REQ-003 — Ses tanımının sağlayıcı üstverisi

**Faz:** [138 — Ses Tanımının Sağlayıcı Üstverisi](../arsiv/fazlar/138-SES-TANIMININ-SAGLAYICI-USTVERISI.md)

### Karar

Kabul edildi ve kapatıldı. İddianız doğruydu: `VoiceDescriptor` yalnız üç alan
taşıyordu (`VoiceId`, `Name`, `Category`) ve ElevenLabs'in `labels` alanı hiç
parse edilmiyordu — kendi konsolumuz bile bu boşluk yüzünden elle bir dil
eşlemesi taşıyordu (`settings.tsx`/`voice.ts`).

### Gerekçe

Kanıt bu oturumda ölçüldü: `ElevenLabsVoice` (`ElevenLabsJson.cs`) yalnız
`voice_id`/`name`/`category` okuyordu, `ReadVoicesAsync` yalnız bu üçünü
eşliyordu. §5'in istediği minimum kümenin (`gender`/`language`/`accent`) hangi
alandan geldiği ölçüm gerektiriyordu: ElevenLabs'in yayınladığı OpenAPI
belgesi (`api.elevenlabs.io/openapi.json`) doğrudan indirilip
`components.schemas.VoiceResponseModel` incelendi. Sonuç raporun varsayımından
farklı çıktı — `labels` (`additionalProperties: string`) `gender`/`accent`/
`age`/`use_case`/`description` taşıyor ama **`language` taşımıyor**; dil ayrı
bir alanda, `verified_languages` (bir voice birden çok model için doğrulanmış
olabileceğinden dizi, her öge `VerifiedVoiceLanguageResponseModel.language`
zorunlu alanı) durur. Sorgu dokümantasyon prosasının ("filtering, based on the
voice's 'language' label") şemanın kendisiyle çeliştiği de bu turda görüldü —
gerçek karar örnek JSON ve şema tanımından alındı, prosadan değil.

İkinci bir ölçüm daha aynı dosyada çıktı: `ListVoicesAsync` `/v2/voices`'i hiç
sorgu dizesi eklemeden çağırıyordu; o uç `page_size` verilmezse **varsayılan
10** ses döndürür. Kod `MaxReportedVoices = 500` sınırını varsayıyordu ama
gerçekte hiçbir hesap 10'dan fazla ses hiç görmüyordu — raporunuzun kapsamı
dışında ama aynı dosyada karşılaşılan bir kusurdu, bu fazda birlikte kapatıldı.

### Uygulanan sözleşme

- `VoiceDescriptor.Attributes` — `IReadOnlyDictionary<string, string>`,
  varsayılan boş (mevcut kod değişmeden derlenir). `VoiceAttributeNames`
  sabitleri (`Gender`, `Language`, `Accent`, `Age`, `UseCase`) yazım hatasını
  önler; küme kapalı değildir, bilinmeyen güvenli bir etiket de kendi anahtarı
  altında taşınır (raporun §5'i bunu açıkça istedi).
- **Sınırlar** (`VoiceAttributeMapper`): en fazla 32 attribute, 64 karakter
  key, 256 karakter value; case-insensitive duplicate key tek kanonik
  (küçük harf, `_`→`-`) değere iner; yalnız `JsonValueKind.String` değer
  taşınır — sayı/nesne/dizi/null güvenle atlanır (`VoiceAttributeMapperTests`,
  15 birim testi).
- **`preview_url` ve API key hiçbir koşulda taşınmaz** — ilki mevcut, bilinçli
  bir karar (`SpeechModels.cs`); ikincisi `ElevenLabsVoice`'ta hiç alan olarak
  yok. `SecretLeakTests` tam alan taraması yapar.
- **`language` normalizasyonu**: `verified_languages` dizisindeki dağınık
  dil kodları küçük harfe indirgenip tekilleştirilir, sıralanır ve tek bir
  `Attributes["language"]` değerine virgülle birleştirilir (`"en,fr"`).
- **Sayfalama düzeltmesi**: `ListVoicesAsync` artık `page_size=100` ile
  başlar ve `has_more`/`next_page_token` bitene veya 500 sınırına ulaşana
  kadar sayfaları takip eder.
- **`list_voices` çıktısı** artık bilinen bir `gender` varsa gösterir; aracın
  iki Türkçe dizgesi ("Kullanilabilir ses yok." / "… ve … ses daha.")
  İngilizce'ye çevrildi ve `SourceLanguageTests`'in kelime listesi bu sınıfı
  yakalayacak biçimde genişletildi (taban çizgisi büyümedi — aynı turda
  ortaya çıkan beş test dosyasındaki benzer Türkçe test verisi de temizlendi).
- **Arayüz**: `settings.tsx`'teki ses seçici artık `language`/`gender`
  varsa `"Amy (en, female)"` biçiminde gösterir; elle dil eşlemesi bu fazda
  KALDIRILMADI (bkz. aşağı, kapsam bilinçli dar tutuldu).

### Bu rapordan farklı davranış

Raporun 9. maddesi `language`'ın `labels` altında olacağını varsayıyordu;
ölçüm bunun yanlış olduğunu gösterdi (`verified_languages` ayrı bir alan).
Sözleşme aynı kaldı (`Attributes["language"]`), yalnız eşleme kaynağı farklı.

Raporun önerdiği gibi typed `Gender`/`Language`/`Accent` özellikleri yerine
sınırlı bir sözlük seçildi — raporun kendisi de §5'te bunu tercih etmişti;
her yeni sağlayıcı etiketi (`use_case`, `age`, ileride başkaları) aksi hâlde
yeni bir public sözleşme değişikliği isterdi (K-669).

Konsolun (`settings.tsx`) elle dil eşlemesi bu fazda **kaldırılmadı**. Ölçüm
dilin gerçekten geldiğini gösterdi, ama yalnız ElevenLabs için ve yalnız
sağlayıcı bunu doğrularsa (`verified_languages` boş dönebilir); tüm
sağlayıcılar için garanti değildir, bu yüzden operatörün elle seçimi hâlâ tek
güvenilir yoldur. Seçici artık bu üstveriyle zenginleşir ama seçimin yerini
almaz — kapsam bilinçli dar tutuldu, kaldırma kararı ayrı bir tur gerektirir.

### Breaking change

Yok. `VoiceDescriptor.Attributes` varsayılanlı bir alan; var olan her
`ISpeechSynthesizer` uygulaması değişmeden derlenir (`Attributes` yalnız
`VoiceDescriptor`'ı üreten kodun doldurabileceği bir alan, uygulamanın
kendisinin değil).

### Store migration

Yok — `VoiceDescriptor` hiç kalıcılaştırılmaz.

### Hedef commit

`28ca187f` (ana uygulama), takip eden arşivleme/damıtma `3982ce8e`.

### Hedef paket sürümü

`1.0.0-preview.1` ve sonrası — henüz tag atılmadı (AP-REQ-002 ile aynı durum).

### Eklenen testler

- `tests/AgentPrism.Voice.UnitTests/VoiceAttributeMapperTests.cs` — 15 birim
  testi: bilinen etiketler, `null`/nesne/dizi/sayısal değer güvenli atlama,
  32/64/256 sınırları, case-insensitive + `_`→`-` kanonikleştirme,
  `verified_languages` birleştirme.
- `tests/AgentPrism.Voice.UnitTests/ElevenLabsSpeechClientTests.cs` — yeni:
  `labels`+`verified_languages` uçtan uca eşleme, boş `labels` → boş
  koleksiyon, `has_more`/`next_page_token` sayfalama takibi, 500 sınırında
  durma.
- `tests/AgentPrism.Voice.UnitTests/SecretLeakTests.cs` — yeni:
  `preview_url`'in `VoiceDescriptor`'a hiçbir koşulda ulaşmadığının tam alan
  taraması.
- `tests/AgentPrism.Voice.UnitTests/ListVoicesToolTests.cs` — yeni: İngilizce
  mesajlar, gender gösterimi.
- `tests/AgentPrism.Core.UnitTests/Architecture/SourceLanguageTests.cs` —
  kelime listesi genişletildi (`yok`, `ses`, `kullanilabilir`, `daha`); aynı
  turda beş `AgentPrism.AspNetCore.FunctionalTests` dosyasındaki benzer
  Türkçe test verisi (`"yok-boyle"`, `"merhaba"`, ...) İngilizce'ye çevrildi.
- `src/AgentPrism.UI/frontend/src/lib/voice.test.ts` — `voiceOptionMeta` için
  4 yeni test.

### Tüketici upgrade adımları

Yok — kaynak uyumlu bir büyüme. `AgentPrism.Voice`/`AgentPrism.Abstractions`'ı
güncelleyen bir tüketici `VoiceDescriptor.Attributes`'a hemen erişebilir;
erişmeyen kod değişmeden çalışmaya devam eder.

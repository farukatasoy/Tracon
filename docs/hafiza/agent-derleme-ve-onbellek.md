# Agent Derleme ve Derlenmiş Agent Önbelleği Tuzakları

> Bir `AgentDefinition`'in çalıştırılabilir bir `AIAgent`'a dönüşmesi, o
> dönüşümün KİMLİĞİ ve `CompiledAgentCache`.
>
> `RunRecording` zinciri, `scope`/span ve iptal AYRI dosyadadır:
> [`cekirdek-calistirma.md`](cekirdek-calistirma.md). Katalog dekoratör
> zinciri ve tool onayı da AYRI dosyadadır:
> [`tool-onay-ve-yetkilendirme.md`](tool-onay-ve-yetkilendirme.md).

- Compaction sarmalama sınırı (Faz 13) ve `IAgentSource` sürüm marker deseni (Faz 19): `docs/arsiv/FAZ-GECMISI.md` "Faz 13/19".
- **🚨 `AgentDefinitionCompiler.Compile` TAMAMEN senkron; kiraci kimlik bilgisi cozumlemesi async `store` gerektirir — ikisi celisince YENI paralel async yol acildi, mevcut sync yol DEGISTIRILMEDI** (Faz 65). Vaka: [`arsiv/HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 `ConcurrentDictionary.GetOrAdd(key, valueFactory)` tahsis eder — HER cagrida, CACHE ISABETINDE bile** (2026-08-27, Faz 116, olculdu): C# argumani metottan ONCE degerlendirir, yani `_ => factory()` kapanisi anahtar zaten varken de HEAP'e yazilir. Cozum `TryGetValue`-once deseni; isabet basina 88 B → 24 B (BenchmarkDotNet). Olcum: [`HAFIZA-GECMISI.md`](../arsiv/HAFIZA-GECMISI.md).
- **🚨 Bir sürüm numarası KİMLİK değildir; store numaralandırmayı yeniden
  başlatabiliyorsa önbellek anahtarı olamaz** (2026-09-18, `HATA-S4-004`).
  `CompiledAgentCache` anahtarı `(kiracı, ad, **sürüm**, bağımlılık, kültür)
  idi ve sınıfın kendi doküman yorumu bunu bir ilke diye anlatıyordu: "sürüm
  artar, önbellek DOĞAL olarak bayatlar, bu yüzden açık geçersiz kılma
  mantığı YOKTUR". İlke yalnız **UPDATE** için doğruydu. **DELETE + CREATE**
  sürümü `1`'e sıfırlar; silinmiş tanım ile yeni tanım aynı anahtara düşer ve
  silinmiş (çoğu zaman bozuk) tanım her `run`'ı cevaplamaya devam eder.
  Belirti operatörü **yanlış yöne yönlendirir**: `GET /api/agents/{name}` YENİ
  tanımı gösterir, veritabanında tek satır ve o satır doğrudur, yalnız
  çalıştırma yanlıştır. **Kural:** bir önbellek anahtarındaki her bileşen için
  "bu değer bir kaydın ömrü boyunca tekil mi, yoksa bir vekil mi?" sorusunu
  sor. Vekilse ölçüyü vekille değil **gömülen içeriğin kendisiyle** yap.
  K-811.
- **🚨 Aynı vekil hatası BAĞIMLILIK parmak izlerinde de vardı ve tek vaka
  aramak onu gizliyordu** (2026-09-18, aynı kapanışın sınıf taraması). Kusur
  kaydı tek yüzey biliyordu; ölçüm **üç** buldu: agent'ın kendisi, shared
  instructions bloğu (`"{blok}:{sürüm}"`) ve callable sub-agent
  (`(ad, sürüm)`). Üçü de silinip yeniden yaratılan bir adı eskisinden ayırt
  edemiyordu; son ikisinde bayat kalan şey **çağıranın** derlenmiş agent'ıydı
  — bloğun silinmiş metni ve alt agent'ın silinmiş açıklaması modele gitmeye
  devam ediyordu. **Kural:** bir parmak izi, gömülen şeyi ölçmelidir. Blok
  parmak izi metnin hash'i, callable parmak izi `CallableAgentInfo`'nun
  tamamıdır. K-812.
- **Bir içerik parmak izi ALAN ALAN değil, SERİLEŞTİRİLEREK hash'lenir**
  (2026-09-18, aynı kapanış). Elle yazılan bir alan listesi ucuzdur ama
  `AgentDefinition`'a sonradan eklenen alanı kaçırır ve kaçırmanın bedeli
  **sessizdir**: önbellek o alanın ESKİ değeriyle derlenmiş agent'ı sunar —
  Aile G'nin `TraconImageOptions.Timeout` vakasıyla aynı sınıf ("imza
  değiştirmek ile gövdeyi kullanmak iki ayrı adımdır"). `AgentDefinition`
  zaten `TraconCoreJsonContext`'te tanımlı olduğu için serileştirme AOT
  temizdir. Kapı: `DefinitionFingerprintTests
  .Every_field_of_the_record_reaches_the_fingerprint` — record'un kendi
  alanlarını gezer ve her birinin parmak izini değiştirdiğini zorlar.

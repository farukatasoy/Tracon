# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul: `grep -n 'K-059\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 783 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (88 / 871 kalem)

| K | Satır | Karar |
|---|---|---|
| K-784 | 835 | Bir kapı, iddianın makine okunur bir kaynağı VARSA doğruluğu denetler; yoksa yalnız varlığı (K-766'nın diğer yüzü) |
| K-785 | 836 | Konsolda `window.confirm` / `alert` / `prompt` KULLANILMAZ; tek bir modal katmanı vardır (`components/dialog.tsx`) ve `frontend/scripts/check-modal-layer.mjs` bunu zorlar |
| K-786 | 837 | Bir aksiyon doğrulama adımı alır ancak ve ancak (a) arayüzden aynı girdilerle geri getirilemeyen bir durumu yok ediyorsa VEYA (b) tekrarlanamayan bir kararı kesinleştiriyorsa; ölçüt şema kanıtıyla ÖLÇÜLÜR |
| K-787 | 838 | Doğrulama dialogu aksiyon uçarken AÇIK KALIR ve sunucu reddini kendi içinde gösterir; `consequence` metni katman 1'in tooltip'iyle AYNI anahtardan gelir |
| K-788 | 839 | Evaluator sürümü `RunJudgment.EvaluatorVersion` ile taşınır; `IRunJudge` seam'i DEĞİŞMEZ 👤 |
| K-789 | 840 | `RunScore.EvaluatorVersion` sınırı `RunScoreRules.MaxEvaluatorVersionLength` (128); sütun `varchar(128)` 👤 |
| K-790 | 841 | `EvaluatorVersion` `null` = SÜRÜM ÇÖZÜLMEDİ, "sürüm sıfır" değil; çözülemezse skor YİNE yazılır |
| K-791 | 842 | `ModelRunJudge` sürüm damgalamaz |
| K-792 | 843 | İptal, sevk edilen store sözleşmesinin bir parçasıdır: zaten iptal edilmiş bir token ile çağrılan store metodu `OperationCanceledException` fırlatır ve HİÇBİR satır yazmaz 👤 |
| K-793 | 844 | `StoreCancellationContract<TStore>` her store sözleşmesinin KÖKÜDÜR; `TenantIsolationContract<TStore>` ondan türer 👤 |
| K-794 | 845 | Akış dönen bir store okuma metodunun İLK `MoveNextAsync`'i iptal edilmiş token'da fırlatır 👤 |
| K-795 | 846 | `tracon` komutu tüketicinin ÇALIŞMA AĞACINA yazar; sözleşme "yalnız yokken yaz"dır ve varsayılan hedef REPO KÖKÜDÜR 👤 |
| K-796 | 847 | Bir agent skill formatı ÖLÇÜLMEDEN sevk edilmez; ölçüm ayırt edici olmak zorundadır 👤 |
| K-797 | 848 | Kapı skill'inin revizyon damgasını PAKET değil TOOL taşır; bayat skill'in düzeltmesi "önce tool'u güncelle"dir |
| K-798 | 849 | Bir tool sonucu, sağlayıcı adaptörünün TELE YAZDIĞI metinle ölçülür; `AIContent` protokol sonucu bu ölçümün dışında değildir |
| K-799 | 850 | `ContentGuard`'ın "incelenemeyen sonuç" fail-closed dalı, okunamayan sonuç içindir; `string` OLMAYAN her sonuç için değil |
| K-800 | 851 | Uzak bir tool'un davranışını sınayan fake, uzak tool'un DÖNÜŞ ŞEKLİNİ taklit etmelidir |
| K-801 | 852 | Bir eval case'inin kimliğini istemci AÇIKÇA taşır; sunucu içerikten tahmin etmez 👤 |
| K-802 | 853 | Bir case'in promosyon kaydı SUNUCUNUN kendi verisidir ve KİMLİKLE taşınır |
| K-803 | 854 | Bir SSE tüketicisi canlılığı FRAME değil BAYT sayarak ölçer 👤 |
| K-804 | 855 | `/api/diagnostics` yürürlükteki `RunRecording` ayarlarını bildirir 👤 |
| K-805 | 856 | Tool zaman aşımı artık gövdeyi İPTAL EDER; bugüne kadar hiçbir şeyi iptal etmiyordu 👤 |
| K-806 | 857 | Zaman aşımından SONRA başarıyla biten bir tool çağrısının sonucu ve harcaması AYNI `tool_invocations` satırına yazılır; ikinci satır AÇILMAZ 👤 |
| K-807 | 858 | Sevk edilen `generate_image` kaydı kendi timeout'unu taşır (varsayılan 2 dk), genel 30 sn'yi miras almaz 👤 |
| K-808 | 859 | Fiyatsız katalog modeli AÇILIŞTA adıyla bildirilir ve `/api/diagnostics` aynı listeyi taşır 👤 |
| K-809 | 860 | Örnek uygulamanın katalog fiyatları AÇIKÇA örnektir, bakımı yapılan bir fiyat listesi DEĞİLDİR 👤 |
| K-810 | 861 | Modele giden zaman aşımı cümlesi saniyenin altını milisaniye olarak söyler |
| K-811 | 862 | Derlenmiş agent önbelleğinin anahtarı SÜRÜMÜ değil İÇERİĞİ ölçer; `CompiledAgentCache.Evict` kaldırıldı 👤 |
| K-812 | 863 | Bağımlılık parmak izi de sürümü değil, ÇAĞIRANA GÖMÜLEN içeriği ölçer |
| K-813 | 864 | Cevap veremeyen bir store `503` döner ve HANGİ tür erişilemezlik olduğunu söyler; şema adı ve SQL metni yine sızmaz 👤 |
| K-814 | 865 | Anahtar deposunu okuyamayan açılış kapısı KENDİ sonucuna varır: "anahtar kanıtlanamadı" |
| K-815 | 866 | Kriptografik doğrulama hatası da anahtarı ADIYLA söyler; beşinci dal diğer dördüyle aynı hizaya getirildi |
| K-816 | 867 | Normalleştirilen sağlayıcı hatası `StableIdentities` ile sınıflandırılır; desen yolu o trafiği hiç görmüyordu |
| K-817 | 868 | Maskelenen sağlayıcı hatasının kalıcı mesajı ÜÇ OLGU taşır: sağlayıcı adı, istisnanın tip adı, HTTP durum kodu 👤 |
| K-818 | 869 | HTTP durum kodu parmak izi normalleştirmesinde GÜRÜLTÜ DEĞİLDİR ve korunur |
| K-819 | 870 | Dil kapısının iki-harfli kelime dışlaması VARSAYIM değil ÖLÇÜMDÜR; yedi bağlaç listeye girdi |
| K-820 | 871 | Sürüm geçmişi okuması, bir agent'ın tanımının NEREDE yaşadığını söyler; `404` kalır, gerekçe değişir 👤 |
| K-821 | 872 | Reddedilen bir enum değeri KENDİNİ ve alternatiflerini söyler; converter TİPE DEĞİL istek gövdesi options'ına takılır 👤 |
| K-822 | 873 | Proplanmamış bir sağlayıcı BOZULMA DEĞİLDİR; `/health` yalnız bilinen arızayı sarıya boyar 👤 |
| K-823 | 874 | Cevabın yalnız hata olabileceği anda soru sorulmaz; A2A kurulumu şemayı beklemeden kataloğu LİSTELEMEZ |
| K-824 | 875 | Sevk edilen inline script'e CSP izni HASH ile verilir ve hash SEVK EDİLEN METİNDEN HESAPLANIR, yazılmaz |
| K-825 | 876 | Yayın provası sürüm bölümünü bulamazsa `## [Unreleased]`'i okur; bölüm ETİKET anında yeniden adlandırmayla doğar 👤 |
| K-826 | 877 | Serbest biçimli bir `JsonElement` alanının BOŞ değeri JSON `null` değil BOŞ DİZİDİR |
| K-827 | 878 | SQLite sağlayıcısı reddedilen bir yazmayı YENİDEN GÖNDERİR; `busy_timeout` yetmez 👤 |
| K-828 | 879 | Şablon yer tutucusu açılışta durdurur, ama YALNIZ bir sağlayıcı yapılandırıldığında 👤 |
| K-829 | 880 | Denetim filtresi ADI kimlik bilgisi taşıyan alanı değil, DEĞERİ taşıyanı redakte eder |
| K-830 | 881 | Kapanan bir kapı span'in durumunu KENDİ ADIYLA kapatır |
| K-831 | 882 | Normalleştirilmiş bir sağlayıcı hatasının `fault` ADI zaman aşımıysa sınıf `ProviderError` değil `Timeout`'tur 👤 |
| K-832 | 883 | Sınıf taraması İKİ gölgeleme daha ölçtü; ikisi de bilerek KODLANMADI |
| K-833 | 884 | Rolün reddettiği bir panel isteği HİÇ GÖNDERMEZ; 403'ü çizmek "konsol bozuk" diye okunur |
| K-834 | 885 | Referans örneğin gösteremediği bir seam için KALICI demo kancası eklenir; "geçici `Program.cs` düzenlemesi" bir mekanizma değildir 👤 |
| K-835 | 886 | `Tracon.Google`'ın görsel yolu ARTIK SUNULMAYAN Imagen `:predict` yüzeyini hedefliyor; kusur açık, düzeltme kullanıcı kararına bırakıldı 👤 |
| K-836 | 887 | Kiracı kimliği, karşılaştırıcı değiştirilerek değil DEĞER NORMALLEŞTİRİLEREK case-duyarsız yapılır; kural `AmbientTenantScope.Normalize` olarak public'tir ve hem giriş sınırı hem SQL depolama sınırı uygular (Faz 179, A-1) |
| K-837 | 888 | Kanonik olmayan `tenant_id` taşıyan veritabanında migration DURUR; satırlar KATLANMAZ (Faz 179) |
| K-838 | 889 | `tenant_id` case guard'ı üç `.sql` migration'ı olarak değil, `SqlDialect` üzerinden tek bir C# adımı (`TenantIdCaseGuard`) olarak yazılır ve tablo listesi KATALOGDAN okunur (Faz 179, plandan sapma) |
| K-839 | 890 | Bellek içi store'ların yalnız İKİSİ (`InMemoryTenantEgressPolicyStore`, `InMemoryTenantProviderBindingStore`) kiracı kimliğini normalleştirir; kalan 30'u kanonik girdi BEKLER (Faz 179) 👤 |
| K-840 | 891 | `exception is not OperationCanceledException` filtresi TEK BAŞINA yanlıştır; kural `OperationCancellation.IsFailure(exception, token)` olarak tek yerde yazılır (A-3 · A-24 sınıf taraması) |
| K-841 | 892 | Onay parmak izi UZUNLUK-ÖNEKLİ formata geçer; `U+001F` ayırıcı varsayımı terk edilir (A-5) |
| K-842 | 893 | `TenantChatClientCacheKey` `internal` yapılır (A-4) 👤 |
| K-843 | 894 | `dotnet new tracon-api`'nin ürettiği projenin paket sürümü, ŞABLON PAKETİNİN KENDİ SÜRÜMÜNE paketleme anında damgalanır (A-9) 👤 |
| K-844 | 895 | Deadline'la duran bir workflow `run`'ı `Canceled` KALIR ve sebebi `RunRecord.Error`'da taşır; sebep her kapıda değil, `run`'ın kapandığı TEK yerde eklenir (A-26) 👤 |
| K-845 | 896 | Bir workflow `run`'ı çağıran iptal ettiğinde VEYA tüketici akışı okumayı bıraktığında `Canceled` olarak kapanır; `RunGuardedAsync`'in `finally`'si bunu garanti eder (A-32) 👤 |
| K-846 | 897 | `DbDataSource` adapter'larının `ConnectionString` özelliği parolayı düşürür |
| K-847 | 898 | "Silinmez, taşınır" kuralına tanımlı istisna: yenilenen kanıt eskisinin yerine geçince ve sıfır canlı referans ölçülünce eski kayıt silinebilir 👤 |
| K-848 | 899 | İstisna atan bir `IModelProviderHealthCheck` listeyi düşürmez: o sağlayıcı `Unhealthy` raporlanır, `detail` YALNIZ istisna tipini taşır, istisnanın kendisi Warning log'a gider (Faz 181, plan dışı kusur) |
| K-849 | 900 | Dört provider adaptörünün ortak gövdesi `src/Tracon.Providers.Shared/` shared-source ağacındadır; paket değildir, her tipi `internal` kalır (Faz 181, F-258) 👤 |
| K-850 | 901 | Public yüzey dış kanıt ölçütüyle daraltıldı: 91 tip adı (93 paket×tip — `MigrationRunner` üç pakette) `internal` oldu; kalan her tipin kanıtı `scripts/public-yuzey-envanteri.py` ile ölçülür ve kanıtsız kalan tipin gerekçesi `scripts/public-yuzey-gerekceleri.tsv`'ye yazılır (Faz 182, F-259) |
| K-851 | 902 | Onay kuralı koşul kümesinin parmak izi tek iç fonksiyondadır (`ToolArgumentConditionFingerprint`); ayırıcı taşıyan bir yol kümeyi uzunluk önekli biçime geçirir, diğer her küme eski biçimi korur — migration yok (Faz 182, plan dışı kusur) |
| K-852 | 903 | Bir kaydın adını taşıdığı `secret` anahtarı KİRACININ anahtar alanında olmalıdır: varsayılan olmayan kiracı `{prefix}{tenant}:...` kullanır, önek altındaki düz ad varsayılan kiracınındır; kural dört yüzeyde (MCP, BYOK, webhook, trigger) hem kaydetmede hem çözmede uygulanır (kusur-giderme, 2026-09-23) 👤 |
| K-853 | 904 | Kiracıyı İSTEKTEN alan uçlar (`/api/tenants/{tenantId}/providers`, `/egress`, kiracı kayıtları `GET/PUT/DELETE /api/tenants[/{slug}]`) başka kiracıya yalnız PLATFORM YETKİSİYLE dokunur: API anahtarı `PlatformAdmin` kapsamı · statik `AuthToken` · claim tabanlı çağıran için yeni `TraconPolicies.PlatformAdmin` policy'si (çok kiracılık açıkken; kayıtsızsa REDDEDER) · anonim yerel operatör (K1) (kusur-giderme, 2026-09-23; K-469'u daraltır) 👤 |
| K-854 | 905 | Kimlik bilgisi taşımayan istek yalnız GERÇEKTEN yerelse ve başka bir sitenin tarayıcı sayfası değilse kabul edilir: `AllowRemoteAccess` açık + `AuthToken` ve policy yok → uzak anonim `401`; `X-Forwarded-For`/`Forwarded`/`X-Real-IP` taşıyan loopback bağlantısı UZAK sayılır; anonim yerel istekte `Host` loopback adı, varsa `Origin` loopback olmalı (`403`) (kusur-giderme, 2026-09-23) 👤 |
| K-855 | 906 | `net8.0` ve `net9.0`, Microsoft desteğinin bittiği 2026-11-10'dan sonraki ilk Tracon sürümünde düşer; acil bir güvenlik sürümü onları hâlâ taşıyabilir (kusur-giderme turu, 2026-09-23) 👤 |
| K-856 | 907 | MCP sunucusu ve webhook aboneliğinin ek `headers` DEĞERLERİ hiçbir HTTP yanıtında ve MCP audit'inde yer almaz: her değer `***` olur, başlık adı kalır; `***` değerli kaydetme `400` alır; değer `store`'da düz kalır ve hedefe aynen gider (kusur-giderme, 2026-09-23) 👤 |
| K-857 | 908 | `QuotaUsageQuery.PeriodStarts` store'un uyguladığı dönem süzgecidir; verilmezse tüm geçmiş döner; `AsOf` `[Obsolete]` olur (kusur-giderme, F-275, 2026-09-24) 👤 |
| K-858 | 909 | Her Tracon→Tracon nuspec bağımlılığı tam aralıktır (`[x]`) ve paketin kendi sürümüne eşittir; yalnız IVT kenarları değil, 22 kardeş kenarının hepsi (Faz 185, F-265) (kullanıcı kararı) 👤 |
| K-859 | 910 | Yüklü Tracon aile derlemeleri farklı sürümdeyse host başlamaz; kontrolün opt-out'u yoktur; preview hattında silinen üyeye ikili uyum shim'i yazılmaz (Faz 185, F-265) (kullanıcı kararı) 👤 |
| K-860 | 911 | Script grant'ı içeriği pinler: stored ve kodda tanımlı script yalnız içeriğinin hash'ini taşıyan grant'la çalışır; hash'siz eski grant onları yetkilendirmez; diskteki script pinlenmez (Faz 186, F-269) (kullanıcı kararı) 👤 |
| K-861 | 912 | Çok kiracılı host'ta stored script grant'ı platform yetkisi ister (`PlatformAdmin` kapsamlı anahtar, statik token veya `Tracon.PlatformAdmin` policy'si); tek kiracılı host'ta ve kodda tanımlı skill'de gerekmez (Faz 186) (kullanıcı kararı) 👤 |
| K-862 | 913 | B7'nin adı "Script execution gates"tir ("Script sandboxing" değil); R8 kabul edilen risktir (script sunucunun OS kimliğiyle çalışır); `SECURITY.md` ve site politikasının kapsamı "Script execution gates (grant, content pin, interpreter allowlist, audit)" der (Faz 186) (kullanıcı kararı) 👤 |
| K-863 | 914 | Kodda kayıtlı bir skill adı yönetim API'sinden yazılamaz: `PUT /api/skills/{name}` `409`; `DELETE` yalnız gölgelenen kayıtlı kopyayı siler, kopya yoksa `409`; konsol kod skill'ini salt okunur açar (Faz 186, denetim 🔴1) (kullanıcı kararı) 👤 |
| K-864 | 915 | Yayın provası son yayınlanmış sürüme karşı kırıcı değişiklik kapısı koşar: `kapi.py yayin` (her push'ta `release-dryrun`) son `v*` etiketinin library paketlerini izole bir cache'e nuget.org'dan restore eder, ApiCompat taban doğrulamasıyla paketler, doğrulamanın bu koşumda koştuğunu semaphore ile kanıtlar; kırılan her public tip ve düşen her TFM sürüm notunda tam adıyla (code span, joker değil) geçmezse prova kırmızıdır ve `publish` koşmaz (Faz 187, F-270) (kullanıcı kararı) 👤 |
| K-865 | 916 | Her library paketinin public yüzeyi TFM'ler arasında aynıdır: `EnableStrictModeForCompatibleTfms` ve `EnableStrictModeForCompatibleFrameworksInPackage` `src/Directory.Build.props`'ta koşulsuz açık; yalnız bir TFM'de derlenen public üye pack'i kırar (Faz 187) (kullanıcı kararı) 👤 |
| K-866 | 917 | DI'ın veya Tracon boru hattının kurduğu public servis tipinin kurucusu `internal`'dır; tip public kalabilir. Tip tabanlı DI kaydı olan böyle bir tip fabrika kaydı kullanır. Opsiyonel parametreli public kurucu yalnız tüketicinin kurduğu tipte olur ve ratchet tabanına gerekçeyle girer; taban yalnız küçülür (Faz 188, F-271 A) (kullanıcı kararı) 👤 |
| K-867 | 918 | `ITraconBuilder` yalnız `Services` taşır; Tracon'un bütün kayıt yetenekleri statik uzantı metodudur (`TraconBuilderExtensions`, Core) ve arayüze üye eklenmez. `TraconToolRegistration` tek zorunlu kurucu parametresi (`function`) + yedi `init` ayarı kullanır; yeni tool ayarı `init` özelliğidir. Kaldırılan imzalar `[Obsolete]` geçişi olmadan kalktı, önceki preview ikilisi yeniden derlenir (Faz 189, F-271 B) (kullanıcı kararı) 👤 |
| K-868 | 919 | Kimlik başlığı yalnız anahtar ADIYLA saklanır (`HeaderConfigurationKeys`, MCP + webhook); her ad kaydetmede ve çözmede K-852'den geçer; düz `headers`'ta kimlik benzeri ad `400`; eski satır gönderilir ve uyarı loglanır (Faz 190, K-059'u genişletir) (kullanıcı kararı) 👤 |
| K-869 | 920 | `AuthorizationConfigurationKey` `[Obsolete]` (yalnız mesaj; `DiagnosticId` ve `UrlFormat` YOK), `1.0.0`'da kalkar; iki alandan veya OAuth ile `Authorization` `400` (Faz 190) (kullanıcı kararı) 👤 |
| K-870 | 921 | MCP/webhook `PUT`: `headers` veya `headerConfigurationKeys` yok ya da `null` → saklı değer; `{}` temizler (Faz 190) (kullanıcı kararı) 👤 |
| K-871 | 922 | Bir `v*` etiketinin nuget.org'a ittiği her `.nupkg`/`.snupkg`, aynı workflow koşumunda build işinin ubuntu bacağının derleyip test ettiği derlemeden `--no-build` ile üretilen dosyadır; `release-dryrun` onu paketlemeden doğrular, `publish` SHA-256 manifest'ini yeniden sınayıp yalnız onu iter; CI'da ikinci bir yayın `dotnet pack`'i yoktur (Faz 191, F-273) (kullanıcı kararı) 👤 |

# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul: `grep -n 'K-059\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 768 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (88 / 856 kalem)

| K | Satır | Karar |
|---|---|---|
| K-769 | 816 | Bir üretim kararı İKİ kontrol taşıyabilir ve risk başına EN KATI cevap kazanır 👤 |
| K-770 | 817 | `NotApplicable`, bir riski taşıyan HİÇ kontrol kayıtlı olmadığında üretilir; sevk edilen altı kontrolün hiçbiri bunu DÖNMEZ 👤 |
| K-771 | 818 | Toplu kabul yolu (`AcceptAll()`) YOKTUR ve eklenmeyecektir |
| K-772 | 819 | `ProductionProfileResult` bir `record` DEĞİLDİR ve üç fabrikayla kurulur |
| K-773 | 820 | Profil kümesi bir SÜRÜM SÖZLEŞMESİDİR; kümeye anahtar eklemek DAVRANIŞSAL KIRICI değişikliktir 👤 |
| K-774 | 821 | Kapasite ölçümü bir KAPI DEĞİLDİR; hiçbir profili standart kapanışa, PR yoluna veya release hattına girmez ve CI'da yalnız `smoke` koşar |
| K-775 | 822 | Yayımlanan kapasite sayısı sürüm ve commit taşır; ölçüm yenilenmeden sürüm satırı güncellenmez 👤 |
| K-776 | 823 | Denetim izinin garanti AYRIMI yayımlanmış bir sözleşmedir: ALTI işlem fail-closed'dır, kalan her audit yazımı best-effort'tur; kümeye ekleme veya çıkarma yayımlanmış bir güvenlik garantisini değiştirir |
| K-777 | 824 | SBOM üretimi ve NuGet paket imzalama preview hattında YAPILMAZ; GA turuna ertelenir 👤 |
| K-778 | 825 | İş kuyruğu MAF'ın durability uzantısının YERİNE GEÇMEZ; sevk ve zamanlama Tracon'un, workflow içi dayanıklılık MAF'ındır 👤 |
| K-779 | 826 | Denetim izinin `before`/`after` içeriği at-rest content protection kapsamı DIŞINDADIR; bu adlandırılmış bir kabul edilen risktir, sessiz bir kusur değil |
| K-780 | 827 | `Tracon.Testing` çalışma paketleriyle AYNI matrisi hedefler (`net8.0;net9.0;net10.0`); K-270'in tek-TFM daralması KALDIRILDI |
| K-781 | 828 | `docs/KARARLAR.md` bütçesi 420.000 → 450.000; sınır yine damıtma SONRASI ölçülen değere ~%7 boşluk eklenerek kondu |
| K-782 | 829 | Best-effort `run` kaydı YAYIMLANMIŞ bir sözleşmedir (K-776'nın kardeşi) ve ihlali SAYILIR; `tracon.recording.stage` KAPALI bir kümedir |
| K-783 | 830 | Bildirimsel `kind` adları HER İKİ defterde de büyük/küçük harf DUYARSIZ çözülür; yerleşik bir `kind`'in harf varyantını kaydetmek başlangıçta atar 👤 |
| K-784 | 831 | Bir kapı, iddianın makine okunur bir kaynağı VARSA doğruluğu denetler; yoksa yalnız varlığı (K-766'nın diğer yüzü) |
| K-785 | 832 | Konsolda `window.confirm` / `alert` / `prompt` KULLANILMAZ; tek bir modal katmanı vardır (`components/dialog.tsx`) ve `frontend/scripts/check-modal-layer.mjs` bunu zorlar |
| K-786 | 833 | Bir aksiyon doğrulama adımı alır ancak ve ancak (a) arayüzden aynı girdilerle geri getirilemeyen bir durumu yok ediyorsa VEYA (b) tekrarlanamayan bir kararı kesinleştiriyorsa; ölçüt şema kanıtıyla ÖLÇÜLÜR |
| K-787 | 834 | Doğrulama dialogu aksiyon uçarken AÇIK KALIR ve sunucu reddini kendi içinde gösterir; `consequence` metni katman 1'in tooltip'iyle AYNI anahtardan gelir |
| K-788 | 835 | Evaluator sürümü `RunJudgment.EvaluatorVersion` ile taşınır; `IRunJudge` seam'i DEĞİŞMEZ 👤 |
| K-789 | 836 | `RunScore.EvaluatorVersion` sınırı `RunScoreRules.MaxEvaluatorVersionLength` (128); sütun `varchar(128)` 👤 |
| K-790 | 837 | `EvaluatorVersion` `null` = SÜRÜM ÇÖZÜLMEDİ, "sürüm sıfır" değil; çözülemezse skor YİNE yazılır |
| K-791 | 838 | `ModelRunJudge` sürüm damgalamaz |
| K-792 | 839 | İptal, sevk edilen store sözleşmesinin bir parçasıdır: zaten iptal edilmiş bir token ile çağrılan store metodu `OperationCanceledException` fırlatır ve HİÇBİR satır yazmaz 👤 |
| K-793 | 840 | `StoreCancellationContract<TStore>` her store sözleşmesinin KÖKÜDÜR; `TenantIsolationContract<TStore>` ondan türer 👤 |
| K-794 | 841 | Akış dönen bir store okuma metodunun İLK `MoveNextAsync`'i iptal edilmiş token'da fırlatır 👤 |
| K-795 | 842 | `tracon` komutu tüketicinin ÇALIŞMA AĞACINA yazar; sözleşme "yalnız yokken yaz"dır ve varsayılan hedef REPO KÖKÜDÜR 👤 |
| K-796 | 843 | Bir agent skill formatı ÖLÇÜLMEDEN sevk edilmez; ölçüm ayırt edici olmak zorundadır 👤 |
| K-797 | 844 | Kapı skill'inin revizyon damgasını PAKET değil TOOL taşır; bayat skill'in düzeltmesi "önce tool'u güncelle"dir |
| K-798 | 845 | Bir tool sonucu, sağlayıcı adaptörünün TELE YAZDIĞI metinle ölçülür; `AIContent` protokol sonucu bu ölçümün dışında değildir |
| K-799 | 846 | `ContentGuard`'ın "incelenemeyen sonuç" fail-closed dalı, okunamayan sonuç içindir; `string` OLMAYAN her sonuç için değil |
| K-800 | 847 | Uzak bir tool'un davranışını sınayan fake, uzak tool'un DÖNÜŞ ŞEKLİNİ taklit etmelidir |
| K-801 | 848 | Bir eval case'inin kimliğini istemci AÇIKÇA taşır; sunucu içerikten tahmin etmez 👤 |
| K-802 | 849 | Bir case'in promosyon kaydı SUNUCUNUN kendi verisidir ve KİMLİKLE taşınır |
| K-803 | 850 | Bir SSE tüketicisi canlılığı FRAME değil BAYT sayarak ölçer 👤 |
| K-804 | 851 | `/api/diagnostics` yürürlükteki `RunRecording` ayarlarını bildirir 👤 |
| K-805 | 852 | Tool zaman aşımı artık gövdeyi İPTAL EDER; bugüne kadar hiçbir şeyi iptal etmiyordu 👤 |
| K-806 | 853 | Zaman aşımından SONRA başarıyla biten bir tool çağrısının sonucu ve harcaması AYNI `tool_invocations` satırına yazılır; ikinci satır AÇILMAZ 👤 |
| K-807 | 854 | Sevk edilen `generate_image` kaydı kendi timeout'unu taşır (varsayılan 2 dk), genel 30 sn'yi miras almaz 👤 |
| K-808 | 855 | Fiyatsız katalog modeli AÇILIŞTA adıyla bildirilir ve `/api/diagnostics` aynı listeyi taşır 👤 |
| K-809 | 856 | Örnek uygulamanın katalog fiyatları AÇIKÇA örnektir, bakımı yapılan bir fiyat listesi DEĞİLDİR 👤 |
| K-810 | 857 | Modele giden zaman aşımı cümlesi saniyenin altını milisaniye olarak söyler |
| K-811 | 858 | Derlenmiş agent önbelleğinin anahtarı SÜRÜMÜ değil İÇERİĞİ ölçer; `CompiledAgentCache.Evict` kaldırıldı 👤 |
| K-812 | 859 | Bağımlılık parmak izi de sürümü değil, ÇAĞIRANA GÖMÜLEN içeriği ölçer |
| K-813 | 860 | Cevap veremeyen bir store `503` döner ve HANGİ tür erişilemezlik olduğunu söyler; şema adı ve SQL metni yine sızmaz 👤 |
| K-814 | 861 | Anahtar deposunu okuyamayan açılış kapısı KENDİ sonucuna varır: "anahtar kanıtlanamadı" |
| K-815 | 862 | Kriptografik doğrulama hatası da anahtarı ADIYLA söyler; beşinci dal diğer dördüyle aynı hizaya getirildi |
| K-816 | 863 | Normalleştirilen sağlayıcı hatası `StableIdentities` ile sınıflandırılır; desen yolu o trafiği hiç görmüyordu |
| K-817 | 864 | Maskelenen sağlayıcı hatasının kalıcı mesajı ÜÇ OLGU taşır: sağlayıcı adı, istisnanın tip adı, HTTP durum kodu 👤 |
| K-818 | 865 | HTTP durum kodu parmak izi normalleştirmesinde GÜRÜLTÜ DEĞİLDİR ve korunur |
| K-819 | 866 | Dil kapısının iki-harfli kelime dışlaması VARSAYIM değil ÖLÇÜMDÜR; yedi bağlaç listeye girdi |
| K-820 | 867 | Sürüm geçmişi okuması, bir agent'ın tanımının NEREDE yaşadığını söyler; `404` kalır, gerekçe değişir 👤 |
| K-821 | 868 | Reddedilen bir enum değeri KENDİNİ ve alternatiflerini söyler; converter TİPE DEĞİL istek gövdesi options'ına takılır 👤 |
| K-822 | 869 | Proplanmamış bir sağlayıcı BOZULMA DEĞİLDİR; `/health` yalnız bilinen arızayı sarıya boyar 👤 |
| K-823 | 870 | Cevabın yalnız hata olabileceği anda soru sorulmaz; A2A kurulumu şemayı beklemeden kataloğu LİSTELEMEZ |
| K-824 | 871 | Sevk edilen inline script'e CSP izni HASH ile verilir ve hash SEVK EDİLEN METİNDEN HESAPLANIR, yazılmaz |
| K-825 | 872 | Yayın provası sürüm bölümünü bulamazsa `## [Unreleased]`'i okur; bölüm ETİKET anında yeniden adlandırmayla doğar 👤 |
| K-826 | 873 | Serbest biçimli bir `JsonElement` alanının BOŞ değeri JSON `null` değil BOŞ DİZİDİR |
| K-827 | 874 | SQLite sağlayıcısı reddedilen bir yazmayı YENİDEN GÖNDERİR; `busy_timeout` yetmez 👤 |
| K-828 | 875 | Şablon yer tutucusu açılışta durdurur, ama YALNIZ bir sağlayıcı yapılandırıldığında 👤 |
| K-829 | 876 | Denetim filtresi ADI kimlik bilgisi taşıyan alanı değil, DEĞERİ taşıyanı redakte eder |
| K-830 | 877 | Kapanan bir kapı span'in durumunu KENDİ ADIYLA kapatır |
| K-831 | 878 | Normalleştirilmiş bir sağlayıcı hatasının `fault` ADI zaman aşımıysa sınıf `ProviderError` değil `Timeout`'tur 👤 |
| K-832 | 879 | Sınıf taraması İKİ gölgeleme daha ölçtü; ikisi de bilerek KODLANMADI |
| K-833 | 880 | Rolün reddettiği bir panel isteği HİÇ GÖNDERMEZ; 403'ü çizmek "konsol bozuk" diye okunur |
| K-834 | 881 | Referans örneğin gösteremediği bir seam için KALICI demo kancası eklenir; "geçici `Program.cs` düzenlemesi" bir mekanizma değildir 👤 |
| K-835 | 882 | `Tracon.Google`'ın görsel yolu ARTIK SUNULMAYAN Imagen `:predict` yüzeyini hedefliyor; kusur açık, düzeltme kullanıcı kararına bırakıldı 👤 |
| K-836 | 883 | Kiracı kimliği, karşılaştırıcı değiştirilerek değil DEĞER NORMALLEŞTİRİLEREK case-duyarsız yapılır; kural `AmbientTenantScope.Normalize` olarak public'tir ve hem giriş sınırı hem SQL depolama sınırı uygular (Faz 179, A-1) |
| K-837 | 884 | Kanonik olmayan `tenant_id` taşıyan veritabanında migration DURUR; satırlar KATLANMAZ (Faz 179) |
| K-838 | 885 | `tenant_id` case guard'ı üç `.sql` migration'ı olarak değil, `SqlDialect` üzerinden tek bir C# adımı (`TenantIdCaseGuard`) olarak yazılır ve tablo listesi KATALOGDAN okunur (Faz 179, plandan sapma) |
| K-839 | 886 | Bellek içi store'ların yalnız İKİSİ (`InMemoryTenantEgressPolicyStore`, `InMemoryTenantProviderBindingStore`) kiracı kimliğini normalleştirir; kalan 30'u kanonik girdi BEKLER (Faz 179) 👤 |
| K-840 | 887 | `exception is not OperationCanceledException` filtresi TEK BAŞINA yanlıştır; kural `OperationCancellation.IsFailure(exception, token)` olarak tek yerde yazılır (A-3 · A-24 sınıf taraması) |
| K-841 | 888 | Onay parmak izi UZUNLUK-ÖNEKLİ formata geçer; `U+001F` ayırıcı varsayımı terk edilir (A-5) |
| K-842 | 889 | `TenantChatClientCacheKey` `internal` yapılır (A-4) 👤 |
| K-843 | 890 | `dotnet new tracon-api`'nin ürettiği projenin paket sürümü, ŞABLON PAKETİNİN KENDİ SÜRÜMÜNE paketleme anında damgalanır (A-9) 👤 |
| K-844 | 891 | Deadline'la duran bir workflow `run`'ı `Canceled` KALIR ve sebebi `RunRecord.Error`'da taşır; sebep her kapıda değil, `run`'ın kapandığı TEK yerde eklenir (A-26) 👤 |
| K-845 | 892 | Bir workflow `run`'ı çağıran iptal ettiğinde VEYA tüketici akışı okumayı bıraktığında `Canceled` olarak kapanır; `RunGuardedAsync`'in `finally`'si bunu garanti eder (A-32) 👤 |
| K-846 | 893 | `DbDataSource` adapter'larının `ConnectionString` özelliği parolayı düşürür |
| K-847 | 894 | "Silinmez, taşınır" kuralına tanımlı istisna: yenilenen kanıt eskisinin yerine geçince ve sıfır canlı referans ölçülünce eski kayıt silinebilir 👤 |
| K-848 | 895 | İstisna atan bir `IModelProviderHealthCheck` listeyi düşürmez: o sağlayıcı `Unhealthy` raporlanır, `detail` YALNIZ istisna tipini taşır, istisnanın kendisi Warning log'a gider (Faz 181, plan dışı kusur) |
| K-849 | 896 | Dört provider adaptörünün ortak gövdesi `src/Tracon.Providers.Shared/` shared-source ağacındadır; paket değildir, her tipi `internal` kalır (Faz 181, F-258) 👤 |
| K-850 | 897 | Public yüzey dış kanıt ölçütüyle daraltıldı: 91 tip adı (93 paket×tip — `MigrationRunner` üç pakette) `internal` oldu; kalan her tipin kanıtı `scripts/public-yuzey-envanteri.py` ile ölçülür ve kanıtsız kalan tipin gerekçesi `scripts/public-yuzey-gerekceleri.tsv`'ye yazılır (Faz 182, F-259) |
| K-851 | 898 | Onay kuralı koşul kümesinin parmak izi tek iç fonksiyondadır (`ToolArgumentConditionFingerprint`); ayırıcı taşıyan bir yol kümeyi uzunluk önekli biçime geçirir, diğer her küme eski biçimi korur — migration yok (Faz 182, plan dışı kusur) |
| K-852 | 899 | Bir kaydın adını taşıdığı `secret` anahtarı KİRACININ anahtar alanında olmalıdır: varsayılan olmayan kiracı `{prefix}{tenant}:...` kullanır, önek altındaki düz ad varsayılan kiracınındır; kural dört yüzeyde (MCP, BYOK, webhook, trigger) hem kaydetmede hem çözmede uygulanır (kusur-giderme, 2026-09-23) 👤 |
| K-853 | 900 | Kiracıyı İSTEKTEN alan uçlar (`/api/tenants/{tenantId}/providers`, `/egress`, kiracı kayıtları `GET/PUT/DELETE /api/tenants[/{slug}]`) başka kiracıya yalnız PLATFORM YETKİSİYLE dokunur: API anahtarı `PlatformAdmin` kapsamı · statik `AuthToken` · claim tabanlı çağıran için yeni `TraconPolicies.PlatformAdmin` policy'si (çok kiracılık açıkken; kayıtsızsa REDDEDER) · anonim yerel operatör (K1) (kusur-giderme, 2026-09-23; K-469'u daraltır) 👤 |
| K-854 | 901 | Kimlik bilgisi taşımayan istek yalnız GERÇEKTEN yerelse ve başka bir sitenin tarayıcı sayfası değilse kabul edilir: `AllowRemoteAccess` açık + `AuthToken` ve policy yok → uzak anonim `401`; `X-Forwarded-For`/`Forwarded`/`X-Real-IP` taşıyan loopback bağlantısı UZAK sayılır; anonim yerel istekte `Host` loopback adı, varsa `Origin` loopback olmalı (`403`) (kusur-giderme, 2026-09-23) 👤 |
| K-855 | 902 | `net8.0` ve `net9.0`, Microsoft desteğinin bittiği 2026-11-10'dan sonraki ilk Tracon sürümünde düşer; acil bir güvenlik sürümü onları hâlâ taşıyabilir (kusur-giderme turu, 2026-09-23) 👤 |
| K-856 | 903 | MCP sunucusu ve webhook aboneliğinin ek `headers` DEĞERLERİ hiçbir HTTP yanıtında ve MCP audit'inde yer almaz: her değer `***` olur, başlık adı kalır; `***` değerli kaydetme `400` alır; değer `store`'da düz kalır ve hedefe aynen gider (kusur-giderme, 2026-09-23) 👤 |

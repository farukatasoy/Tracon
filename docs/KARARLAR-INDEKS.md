# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul: `grep -n 'K-059\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 742 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (88 / 830 kalem)

| K | Satır | Karar |
|---|---|---|
| K-743 | 790 | `SingletonExecution.LeaseDuration` `Enabled` iken en az 3 sn'dir; bir yenileme aralığı yenilediği pencerenin İÇİNDE kalmalıdır 👤 |
| K-744 | 791 | Paylaşılan bir dizine yazan MSBuild adımı, damgayla değil TEK BİR PROJE ÖRNEĞİNE devredilerek teklenir 👤 |
| K-745 | 792 | Canlı ses oturumunun faturalanan süresi SAĞLAYICININ bildirdiği sayıdır, duvar saati DEĞİL |
| K-746 | 793 | `VoiceSessionCost` iki terimli bir `record`'dur ve toplamı YALNIZ `Total()` yapar |
| K-747 | 794 | Canlı ses append'i HER kanalda bir `delegation_id` taşır; oturum geneli append YOKTUR |
| K-748 | 795 | Delegation olayı AGENT SEÇEMEZ; agent oturum yaratılırken bir kez çözülür |
| K-749 | 796 | Eşzamanlılık limiti SAĞLAYICI ÇAĞRISINDAN ÖNCE uygulanır ve sıra tek yerde durur |
| K-750 | 797 | Canlı yolda konuşmanın METNİ varsayılan olarak kalıcıdır; SES hiç saklanmaz 👤 |
| K-751 | 798 | Giden WebSocket egress politikasını `ValidateAsync` ile ELDE çağırır; çağrı bir TEST MADDESİDİR |
| K-752 | 799 | Ses yüzeyinin 404 gövdesini TEK bir yazar üretir (`VoiceEndpointGates`) |
| K-753 | 800 | Bir yeniden adlandırma migration YOLLARINI taşıdığında `applied-migrations.json` YENİDEN TEMELLENDİRİLİR; bu iş İKİ commit'tir |
| K-754 | 801 | Analyzer tanı öneki `APG` değil `TRC`'dir; kısaltmalar ad aramasıyla BULUNAMAZ, elle aranır 👤 |
| K-755 | 802 | NuGet paketlerinin sahibi `Tracon` ORGANİZASYONUDUR, kişisel hesap değil 👤 |
| K-756 | 803 | Doküman sayfasının gzip tavanı 57 000 B → 58 000 B 👤 |
| K-757 | 804 | Console'un varsayılan teması SAKLANAN TERCİHTİR (`dark`), medya sorgusu DEĞİL |
| K-758 | 805 | Console'un runtime bağımlılık kümesi DÖRT isimle kapıya bağlandı 👤 |
| K-759 | 806 | Bir `OperationCanceledException`'ın SEBEBİNİ söyleyen her yol, o sebebin KENDİ kaynağını sınar; çağıranın token'ını dışlamak yetmez |
| K-760 | 807 | `TraconClientOptions` bir `Timeout` alanı taşır; kendi bütçesini kuran çağıran onu SONSUZA çeker |
| K-761 | 808 | `.claude/settings.json`'daki `deny` bloğu bir KORKULUKTUR, güvenlik sınırı DEĞİLDİR |
| K-762 | 809 | Denetçinin Bash ile yazması ENGELLENMEZ; `tools` allowlist'i korkuluktur 👤 |
| K-763 | 810 | `permissions.ask` bir KİLİT değildir; oturumun izin moduna tabidir ve auto mode onu SESSİZCE onaylayabilir |
| K-764 | 811 | Bir kapı bir girdi BİÇİMİNİ tanımıyorsa "kapsam dışı" demek onu SESSİZ yapar; kapı her biçimi sayar ya da saymadığını BİLDİRİR |
| K-765 | 812 | Kurtarma rampalarının öneki `KR-`'dir ve bir rampanın GÖVDESİ TEK YERDE yaşar; katalog on ikiden yedisini yalnız BAĞLAR, kopyalamaz |
| K-766 | 813 | Süreç ölçümü kapısı bölümün VARLIĞINI denetler, DOĞRULUĞUNU denetlemez |
| K-767 | 814 | Süreç ölçümü eşiği sabit sayı `167`'dir (kullanıcı kararı); geriye dönük 166 faz DOLDURULMAZ 👤 |
| K-768 | 815 | 🔴 denetim bulgusunun triyajını KULLANICI yapar; denetçi yalnız ÖNERİR |
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

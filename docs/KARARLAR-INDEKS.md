# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul: `grep -n 'K-059\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 709 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (88 / 797 kalem)

| K | Satır | Karar |
|---|---|---|
| K-710 | 757 | `RunScore.Name` tekillik anahtarına girer, `author` `COALESCE` EDİLMEZ 👤 |
| K-711 | 758 | `RunScore.Value` `required int` → `double?` (`null` = ölçüm yok); `TextValue` ve `Categorical` eklendi 👤 |
| K-712 | 759 | `RunScoreRules` PUBLIC'tir; invariant tek kaynaktan zorlanır |
| K-713 | 760 | `EvalCaseResult.Scores` değer/derece/tanı yazar; `Metadata` ve `Context` YAZILMAZ 👤 |
| K-714 | 761 | `IEvalStore` `DiffRunsAsync` üyesini kazanır; hizalama politikası `Tracon.Abstractions` içindeki PUBLIC `EvalRunDiffBuilder`'dadır, `Core`'da değil |
| K-715 | 762 | Karşılaştırılamayan iki koşum İSTİSNA atar (`EvalRunDiffUnavailableException`); uç `409`/`400` döner, BOŞ FARK asla dönmez |
| K-716 | 763 | `ContentChanged` bayrağı KAPSAM DIŞI; case içeriği koşum başına saklanmaz ve sınır sözleşmeye yazılır 👤 |
| K-717 | 764 | Yalnız `Completed` koşumlar karşılaştırılır; diğer her durum `400` döner 👤 |
| K-718 | 765 | `tracon eval` DÖRDÜNCÜ bir çıkış kodu kazanır: `4` = karşılaştırılamadı; `3` (kapı düştü) anlamı değişmez |
| K-719 | 766 | Bir koşumda bir case'in BİRDEN ÇOK sonuç satırı varsa case yalnız HEPSİ geçtiyse geçmiş sayılır |
| K-720 | 767 | Süreçten ÇIKAN metin sayılarını `CultureInfo.InvariantCulture` ile biçimler; risk yalnız ONDALIK ve YÜZDE belirteçlerindedir, analyzer bu sınıfı GÖRMEZ |
| K-721 | 768 | `KARARLAR.md` bütçesi 390.000 → 420.000; önce TAŞIMA denendi ve `karar-damit`'in "işaretçisi var, atla" kuralı kaldırıldı |
| K-722 | 769 | Sevk edilen agent haritasının tavanı 10 KiB → 11 KiB 👤 |
| K-723 | 770 | `capabilities.md` tablosunda KAÇIRILMIŞ boru (`\|`) hücreyi bölmez |
| K-724 | 771 | SQL Server `run_scores` upsert'i artık ham `message_id`'yi değil, `message_key AS ISNULL(message_id, N'') PERSISTED` computed column'unu indeksler ve eşler; migration mevcut `NULL`/`''` çiftlerini en yeni `created_at` kalacak şekilde dedupe eder |
| K-725 | 772 | `AgentDefinitionRequest` tüketiciye ait HER `AgentDefinition` alanını taşımak ZORUNDADIR; `PUT /api/agents/{name}` tam değiştirme (full-replace) semantiğinde KALIR |
| K-726 | 773 | Approval kararında AYNI cevapla gelen tekrar `409` DEĞİL, handoff'u tamamlayan `200` döner; yalnız TERS cevap `409`'dur. Resume run kimliği approval'dan TÜRETİLİR (`TraconId.DeriveId`) |
| K-727 | 774 | `Microsoft.Extensions.AI.Evaluation.Quality` katalogu sevk edilen HİÇBİR pakete girmez; `Tracon.Core` yalnız `Microsoft.Extensions.AI.Evaluation`'ı AÇIK referanslar 👤 |
| K-728 | 775 | `RunJudgment.Score`/`Reason` KALDIRILDI; bir yargıç `IReadOnlyList<JudgeScore> Scores` döndürür 👤 |
| K-729 | 776 | Köprü metrik adını `{judge}.{metrik}` olarak önekler; ayırıcı `:` KULLANILAMAZ 👤 |
| K-730 | 777 | Yalnız MANŞET skor (adı yargıcın adına EŞİT olan) online değerlendirme penceresine ve `tracon.judge.score` histogramına girer |
| K-731 | 778 | `IEvalEvaluatorFactory` public'tir; eval suite seam'i çıplak bir `IAgentEvaluator` DEĞİL bir FABRİKADIR |
| K-732 | 779 | Sözleşmeyi ihlal eden bir yargıç FIRLATMAZ; `judge_contract` `JudgeFailure` olarak raporlanır ve doğrulama İLK YAZMADAN ÖNCE toplu yapılır |
| K-733 | 780 | Durum ön kontrolü AYRI bir salt okunur SQL yüzeyinden okur (`IStatePreflightReader`); `ISessionStore.QueryAsync` bu iş için YETMEZ 👤 |
| K-734 | 781 | Desteklenen upgrade penceresi: aynı ana sürüm içinde HER sürümden HER sürüme; söz yalnız Tracon'in KENDİ envelope'u içindir 👤 |
| K-735 | 782 | Ön kontrol ÇÖZEMEDİĞİ şifreli satırı hata SAYMAZ; "yapı kontrolü" olarak raporlar |
| K-736 | 783 | Ön kontrol "örneklem temiz" der, "hepsi okunabilir" DEMEZ; ayrım çıktıda kelimeyle kurulur |
| K-737 | 784 | Bir `OperationCanceledException` ancak İLGİLİ TOKEN gerçekten iptal edildiyse iptaldir; aksi hâlde ARIZADIR |
| K-738 | 785 | Yük ve arıza ölçümleri RAPORDUR, kapı değildir; süre hiçbir eşiğe bağlanmaz |
| K-739 | 786 | İki process senaryosu bir ÖLÇÜMDÜR, çok node DESTEK BEYANI değildir |
| K-740 | 787 | Tracon MIT DEĞİL, `PolyForm-Small-Business-1.0.0` ile sevk edilir; üç paket MIT kalır 👤 |
| K-741 | 788 | Pakette lisans anahtarı, aktivasyon çağrısı veya özellik kapısı YOKTUR; uyum tüketicinin kendi lisans taramasına bırakılır 👤 |
| K-742 | 789 | Lisans matrisi İKİ yetkili kaynakta yazılır (`src/Directory.Build.props` + `scripts/kapi.py`) ve bir test onları kilitler; üçüncü kopya türetilir |
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

# KARARLAR — İndeks

> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` · üretim: `scripts/dokuman-bakim.py`

Bul: `grep -n 'K-059\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`. Tarih yok (K-214). Reddedilenler: [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md). En eski 671 karar: [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md). 👤 kullanıcı kararı · 🔁 yeniden açılmış.

---

## En Yeni Kalıcı Kararlar (88 / 759 kalem)

| K | Satır | Karar |
|---|---|---|
| K-713 | 722 | `EvalCaseResult.Scores` değer/derece/tanı yazar; `Metadata` ve `Context` YAZILMAZ 👤 |
| K-714 | 723 | `IEvalStore` `DiffRunsAsync` üyesini kazanır; hizalama politikası `Tracon.Abstractions` içindeki PUBLIC `EvalRunDiffBuilder`'dadır, `Core`'da değil |
| K-715 | 724 | Karşılaştırılamayan iki koşum İSTİSNA atar (`EvalRunDiffUnavailableException`); uç `409`/`400` döner, BOŞ FARK asla dönmez |
| K-716 | 725 | `ContentChanged` bayrağı KAPSAM DIŞI; case içeriği koşum başına saklanmaz ve sınır sözleşmeye yazılır 👤 |
| K-717 | 726 | Yalnız `Completed` koşumlar karşılaştırılır; diğer her durum `400` döner 👤 |
| K-718 | 727 | `tracon eval` DÖRDÜNCÜ bir çıkış kodu kazanır: `4` = karşılaştırılamadı; `3` (kapı düştü) anlamı değişmez |
| K-719 | 728 | Bir koşumda bir case'in BİRDEN ÇOK sonuç satırı varsa case yalnız HEPSİ geçtiyse geçmiş sayılır |
| K-720 | 729 | Süreçten ÇIKAN metin sayılarını `CultureInfo.InvariantCulture` ile biçimler; risk yalnız ONDALIK ve YÜZDE belirteçlerindedir, analyzer bu sınıfı GÖRMEZ |
| K-721 | 730 | `KARARLAR.md` bütçesi 390.000 → 420.000; önce TAŞIMA denendi ve `karar-damit`'in "işaretçisi var, atla" kuralı kaldırıldı |
| K-722 | 731 | Sevk edilen agent haritasının tavanı 10 KiB → 11 KiB 👤 |
| K-723 | 732 | `capabilities.md` tablosunda KAÇIRILMIŞ boru (`\|`) hücreyi bölmez |
| K-662 | 740 | `JobKind` KALDIRILDI; işin kimliği tek bir dizge alandır (`JobRecord.HandlerKey` / `JobSchedule.HandlerKey`), ikinci bir alan tutulmaz 👤 |
| K-663 | 741 | Handler anahtarı KAYITTA verilir (`AddJobHandler<T>(key)`), handler SCOPED kaydedilir ve execution başına yeni bir DI scope'undan çözülür; dispatch tam anahtar eşleşmesidir, kayıt sırası sonucu DEĞİŞTİRMEZ 👤 |
| K-664 | 742 | Kayıtsız handler anahtarı FAIL-CLOSED'dır: iş `Failed` kapanır, `ErrorMessage` kararlı `JobErrorCodes.UnknownHandlerKey` kodunu taşır ve HAM ANAHTARI TAŞIMAZ |
| K-665 | 743 | `PUT /api/schedules/{name}` yalnız `TraconSchedulingOptions.HttpSchedulableHandlerKeys` içindeki anahtarı kabul eder (boş liste = yalnız yerleşik dokuz anahtar); izinli anahtar listesi `GET /api/schedules/handler-keys` ile ADMIN ardında yayınlanır, kimlik doğrulamasız `/api/meta` ile DEĞİL 👤 |
| K-666 | 744 | SQLite'ta `kind` sütunu YERİNDE düşürülür (`ALTER TABLE ... DROP COLUMN`), repo'nun tablo-yeniden-kurma emsali (`0006_sessions_tenant_key.sql`) İZLENMEZ |
| K-667 | 746 | Aynı tipin aynı handler anahtarıyla ikinci kaydı NO-OP'tur; çakışma yalnız İKİ FARKLI tip aynı anahtarı paylaştığında vardır |
| K-668 | 747 | Handler'ın kurucusu çözülemezse iş `JobErrorCodes.HandlerActivationFailed` ile `Failed` kapanır; YENİDEN DENENMEZ |
| K-669 | 748 | `VoiceDescriptor.Attributes` sağlayıcı üstverisini typed alanlar değil, sınırlı bir `Dictionary<string,string>` olarak taşır |
| K-670 | 749 | `IRunAuthorizationHandler` dört run başlatan yüzeyin (agent run, workflow run, inbound trigger, OpenAI uyumlu `/v1/responses`) DÖRDÜNÜ de kapsar; kapı `QuotaGate` ile AYNI çağrı şeklini taşır (elle çağrı, ortak `IEndpointFilter` DEĞİL) 👤 |
| K-671 | 750 | Reddedilen session `List` erişimi `403` döner (asla filtrelenmez); reddedilen `Read`/`Delete`/`Branch` `404` döner ve gövdesi gerçekten var olmayan bir session'la BİREBİR AYNIDIR 👤 |
| K-672 | 751 | `ContentGuardContext.Source` içerik TİPİNE göre sınıflanır (`FunctionResultContent` → `ToolResult`, rolden BAĞIMSIZ), sonra mesajın ROLÜNE göre; `Direction`'a hiç bakılmaz — geçmiş bir turun yeniden gönderilen model metni `Input` yönünde de `ModelOutput` kalır. `PatternContentGuard` `Source`'u KASITLI okumaz |
| K-673 | 752 | `run_events.custom_type` AYRI bir nullable sütundur (üç dialect'e birer migration), `payload` metni içine gömülmez 👤 |
| K-674 | 753 | Geçersiz `CustomType` (`Custom` iken boş/yanlış biçim, ya da `Custom` DIŞINDA doluyken) `RunEventWriter.AppendAsync`'i `ArgumentException` ile REDDEDER, sessizce atlamaz veya loglamaz 👤 |
| K-675 | 754 | `IToolApprovalPresenter`'ın çözdüğü sunum `pending_approvals`'a KALICI bir sütun olarak yazılır, karar anında yeniden çözülmez 👤 |
| K-676 | 755 | `IToolApprovalPresenter` fail-OPEN'dır: kayıtlı değil, `null` döner, `throw` eder veya zaman aşımına uğrar — dördü de onay isteğinin yayımını ENGELLEMEZ |
| K-677 | 756 | Alt-agent çağrısının iki katmanlı bekleme sınırında katman 2'yi (sert kesme) `ChildAgentInvoker`'ın KENDİSİ uygular; MAF'ın `BackgroundAgentsProviderOptions.WaitTimeout`'una GÜVENİLMEZ |
| K-678 | 757 | Kayıtlı olay akışının (`GET /api/runs/{id}/events`) SSE çerçeve adları AÇIK bir eşleme tablosuyla verilir; `RunEventType` üyesinin adından MEKANİK türetilmez, ve tamlık bir kapıya (`RunEventFrameNameContractTests`) bağlanır 👤 |
| K-679 | 758 | `RunEventType.Custom`'ın kayıtlı olay akışındaki SSE çerçeve adı HER ZAMAN sabit `"custom"`dır; tüketicinin kendi `CustomType` dizgesi asla çerçeve adı OLMAZ 👤 |
| K-680 | 759 | Kota muhasebesi (`RecordQuotaAsync`) artık `run`'ın terminal olay yazımından ÖNCE çalışır; kota bir hata sonrası GERİ ALINMAZ 👤 |
| K-681 | 760 | `QuotaEnforcer.RecordAsync` geçilen eşikleri döner (`ValueTask` → `ValueTask<IReadOnlyList<QuotaThresholdCrossing>>`); `IQuotaStore`'a `TryClaimThresholdNotificationAsync` eklenir — ikisi de kırıcı |
| K-682 | 761 | Kota eşiği tekilliği `quota_usage.notified_thresholds` (`text`, `,metrik:yüzde,` sınırlayıcılı CSV) sütununda kalıcı hâle gelir; atomiklik koşullu `UPDATE`'in etkilenen satır sayısıyla sağlanır |
| K-683 | 762 | Kaynak yetkilendirmesi AYRI bir sözleşme açmaz: var olan `RunAccess`/`SessionAccess` enum'ları büyür, `RunAuthorizationRequest` `RunId` kazanır ve `AgentName` `required` olmaktan çıkar; `IRunAuthorizationHandler`'ın metot sayısı DEĞİŞMEZ. Enum'ların sayısal değeri bir persistence sözleşmesi DEĞİLDİR ve bu XML'e açıkça yazılır 👤 |
| K-684 | 763 | Reddedilen TEKİL kaynak `404` döner ve gövdesi gerçekten var olmayan kaynakla BİREBİR aynıdır; reddedilen LİSTE `403` döner. Kapı, kaynak bulunduktan ve kiracısı doğrulandıktan SONRA ve durum okumasından ÖNCE sorulur; ret yanıtını çağıran verir, kapı üretmez |
| K-685 | 764 | `GET /api/runs/{id}/tools` var olmayan bir `run` için artık `200 []` değil `404` döner; bu, handler kayıtlı olmasa bile geçerli bilinçli bir davranış değişikliğidir 👤 |
| K-686 | 765 | `POST /api/attachments` reddi `403` döner, `404` değil 👤 |
| K-687 | 766 | Ses WebSocket'inin yetkilendirme reddi `404` döner ve gövdesi erişilemeyen oturumunkiyle BİREBİR aynıdır (`401` veya `403` DEĞİL); var olmayan oturum reddedilmez, handler sorulur ve varsayılan cevap soketi açar (K-283 korunur) 👤 |
| K-688 | 767 | Oturum sahipliği KALICI bir sütundur (`sessions.owner_id`, üç migration), ayrı bir `session_owners` tablosu değil; `SessionQuery.OwnerId` süzgeci SQL `WHERE` yan tümcesinde, `Skip`/`Take`'ten ÖNCE yaşar 👤 |
| K-689 | 768 | Sahiplik BİR KEZ atanır: ilk yazımda çözülür, sonraki her yazımda KAYNAKTAN taşınır ve üç SQL `store` ile bellek içi `store` sütunu `COALESCE` eder — "set → unset" meşru bir geçiş DEĞİLDİR |
| K-690 | 769 | Mod açıkken `IRunAttributionContext` bir MUHASEBE değil bir YETKİLENDİRME girdisidir: çözülemeyen kimlik oturumu açtırmaz (`403`, `errorType` `session_owner_required`), `NULL` sütun bırakmaz 👤 |
| K-691 | 770 | Sahiplik sınırı `run` BAŞLATAN yüzeylerde de zorlanır (`403`), yalnız oturum uçlarında değil; ama sahipli LİSTE muafiyeti (`ManagementPolicy`) yalnız listeye uygulanır — tekil oturuk okumasında yönetim muafiyeti YOKTUR |
| K-692 | 771 | Sahip çözümünde AÇIK bir `AmbientRunAttributionScope` kayıtlı `IRunAttributionContext`'i EZER; bu öncelik yalnız SAHİPLİK içindir, attribution'ın kendi okuyucusu değişmez |
| K-693 | 772 | Sahipsiz eski satır tekil erişimde REDDEDİLMEZ (sahipli listede ise HİÇ görünmez); sahiplik geriye dönük DEĞİLDİR |
| K-694 | 773 | Katı modun yönetim muafiyeti yalnız OKUMA kapısındadır (`SessionOwnershipGate.DeniesAsync`), `run` BAŞLATMADA yoktur 👤 |
| K-695 | 774 | Sahipsiz satır reddi ile BAŞKASININ oturumu reddi aynı metni taşır; ayrı bir `errorType` icat edilmez 👤 |
| K-696 | 775 | `/v1/conversations`'ın üç OKUMA/SİLME ucu `IRunAuthorizationHandler`'a bağlandı; `POST` bağlanmadı |
| K-697 | 776 | `TraconEndpointOptions.MapOpenAIConversations` yalnız conversations ailesini yönetir; varsayılan `true` 👤 |
| K-698 | 777 | `RequireCustomBinding<T>()` serbest generic'tir; yedi sözleşmenin kapalı kümesi ÇALIŞMA ANINDA zorlanır, derlemede değil 👤 |
| K-699 | 778 | Zorunlu binding ihlali `InvalidOperationException` atar; `TraconException` ailesine yeni tip eklenmez 👤 |
| K-700 | 779 | Zorunluluk `/api/diagnostics`'te GÖRÜNMEZ; `ExtensionPointDiagnostic` bir `IsRequired` alanı almaz 👤 |
| K-701 | 780 | Kapı yalnız "yerleşik varsayılan mı" sorusunu yanıtlar; lifetime iddiası kapsam dışıdır 👤 |
| K-702 | 781 | Sözleşmenin non-nullable ilan ettiği bir koleksiyona AÇIK `null` `400`'dür, `500` değil; üretilen istemcinin non-nullable koleksiyonları da boş başlar 👤 |
| K-703 | 782 | Bildirimsel `kind` adları HER İKİ defterde de büyük/küçük harf DUYARSIZ çözülür; yerleşik bir `kind`'in harf varyantını kaydetmek başlangıçta atar 👤 |
| K-724 | 783 | SQL Server `run_scores` upsert'i artık ham `message_id`'yi değil, `message_key AS ISNULL(message_id, N'') PERSISTED` computed column'unu indeksler ve eşler; migration mevcut `NULL`/`''` çiftlerini en yeni `created_at` kalacak şekilde dedupe eder |
| K-725 | 784 | `AgentDefinitionRequest` tüketiciye ait HER `AgentDefinition` alanını taşımak ZORUNDADIR; `PUT /api/agents/{name}` tam değiştirme (full-replace) semantiğinde KALIR |
| K-726 | 785 | Approval kararında AYNI cevapla gelen tekrar `409` DEĞİL, handoff'u tamamlayan `200` döner; yalnız TERS cevap `409`'dur. Resume run kimliği approval'dan TÜRETİLİR (`TraconId.DeriveId`) |
| K-727 | 786 | `Microsoft.Extensions.AI.Evaluation.Quality` katalogu sevk edilen HİÇBİR pakete girmez; `Tracon.Core` yalnız `Microsoft.Extensions.AI.Evaluation`'ı AÇIK referanslar 👤 |
| K-728 | 787 | `RunJudgment.Score`/`Reason` KALDIRILDI; bir yargıç `IReadOnlyList<JudgeScore> Scores` döndürür 👤 |
| K-729 | 788 | Köprü metrik adını `{judge}.{metrik}` olarak önekler; ayırıcı `:` KULLANILAMAZ 👤 |
| K-730 | 789 | Yalnız MANŞET skor (adı yargıcın adına EŞİT olan) online değerlendirme penceresine ve `tracon.judge.score` histogramına girer |
| K-731 | 790 | `IEvalEvaluatorFactory` public'tir; eval suite seam'i çıplak bir `IAgentEvaluator` DEĞİL bir FABRİKADIR |
| K-732 | 791 | Sözleşmeyi ihlal eden bir yargıç FIRLATMAZ; `judge_contract` `JudgeFailure` olarak raporlanır ve doğrulama İLK YAZMADAN ÖNCE toplu yapılır |
| K-733 | 792 | Durum ön kontrolü AYRI bir salt okunur SQL yüzeyinden okur (`IStatePreflightReader`); `ISessionStore.QueryAsync` bu iş için YETMEZ 👤 |
| K-734 | 793 | Desteklenen upgrade penceresi: aynı ana sürüm içinde HER sürümden HER sürüme; söz yalnız Tracon'in KENDİ envelope'u içindir 👤 |
| K-735 | 794 | Ön kontrol ÇÖZEMEDİĞİ şifreli satırı hata SAYMAZ; "yapı kontrolü" olarak raporlar |
| K-736 | 795 | Ön kontrol "örneklem temiz" der, "hepsi okunabilir" DEMEZ; ayrım çıktıda kelimeyle kurulur |
| K-737 | 796 | Bir `OperationCanceledException` ancak İLGİLİ TOKEN gerçekten iptal edildiyse iptaldir; aksi hâlde ARIZADIR |
| K-738 | 797 | Yük ve arıza ölçümleri RAPORDUR, kapı değildir; süre hiçbir eşiğe bağlanmaz |
| K-739 | 798 | İki process senaryosu bir ÖLÇÜMDÜR, çok node DESTEK BEYANI değildir |
| K-740 | 799 | Tracon MIT DEĞİL, `PolyForm-Small-Business-1.0.0` ile sevk edilir; üç paket MIT kalır 👤 |
| K-741 | 800 | Pakette lisans anahtarı, aktivasyon çağrısı veya özellik kapısı YOKTUR; uyum tüketicinin kendi lisans taramasına bırakılır 👤 |
| K-742 | 801 | Lisans matrisi İKİ yetkili kaynakta yazılır (`src/Directory.Build.props` + `scripts/kapi.py`) ve bir test onları kilitler; üçüncü kopya türetilir |
| K-743 | 802 | `SingletonExecution.LeaseDuration` `Enabled` iken en az 3 sn'dir; bir yenileme aralığı yenilediği pencerenin İÇİNDE kalmalıdır 👤 |
| K-744 | 803 | Paylaşılan bir dizine yazan MSBuild adımı, damgayla değil TEK BİR PROJE ÖRNEĞİNE devredilerek teklenir 👤 |
| K-745 | 804 | Canlı ses oturumunun faturalanan süresi SAĞLAYICININ bildirdiği sayıdır, duvar saati DEĞİL |
| K-746 | 805 | `VoiceSessionCost` iki terimli bir `record`'dur ve toplamı YALNIZ `Total()` yapar |
| K-747 | 806 | Canlı ses append'i HER kanalda bir `delegation_id` taşır; oturum geneli append YOKTUR |
| K-748 | 807 | Delegation olayı AGENT SEÇEMEZ; agent oturum yaratılırken bir kez çözülür |
| K-749 | 808 | Eşzamanlılık limiti SAĞLAYICI ÇAĞRISINDAN ÖNCE uygulanır ve sıra tek yerde durur |
| K-750 | 809 | Canlı yolda konuşmanın METNİ varsayılan olarak kalıcıdır; SES hiç saklanmaz 👤 |
| K-751 | 810 | Giden WebSocket egress politikasını `ValidateAsync` ile ELDE çağırır; çağrı bir TEST MADDESİDİR |
| K-752 | 811 | Ses yüzeyinin 404 gövdesini TEK bir yazar üretir (`VoiceEndpointGates`) |
| K-753 | 812 | Bir yeniden adlandırma migration YOLLARINI taşıdığında `applied-migrations.json` YENİDEN TEMELLENDİRİLİR; bu iş İKİ commit'tir |
| K-754 | 813 | Analyzer tanı öneki `APG` değil `TRC`'dir; kısaltmalar ad aramasıyla BULUNAMAZ, elle aranır 👤 |
| K-755 | 814 | NuGet paketlerinin sahibi `Tracon` ORGANİZASYONUDUR, kişisel hesap değil 👤 |
| K-756 | 815 | Doküman sayfasının gzip tavanı 57 000 B → 58 000 B 👤 |
| K-757 | 816 | Console'un varsayılan teması SAKLANAN TERCİHTİR (`dark`), medya sorgusu DEĞİL |
| K-758 | 817 | Console'un runtime bağımlılık kümesi DÖRT isimle kapıya bağlandı 👤 |

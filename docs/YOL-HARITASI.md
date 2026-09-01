# Faz Yol Haritası

> **Üretilen dosya. Elle düzenleme.** Kaynak: her fazın kendi
> dokümanındaki `> **Durum:**` satırı. Açık fazlar `docs/` kökünde,
> kapanmış fazlar `docs/arsiv/fazlar/` altında yaşar.
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

Bir fazın durumu yanlış görünüyorsa **o fazın dokümanını** düzelt;
bu dosyayı düzeltmek bir sonraki üretimde geri alınır.

## Fazlar (129 kalem)

| Faz | Konu | Durum |
|-----|------|-------|
| [0](arsiv/fazlar/00-ALTYAPI.md) | Repository ve Build Altyapısı | ✅ Tamamlandı |
| [1](arsiv/fazlar/01-CEKIRDEK-SOYUTLAMALAR.md) | Çekirdek Soyutlamalar ve Runtime | ✅ Tamamlandı |
| [2](arsiv/fazlar/02-POSTGRESQL-KALICILIK.md) | PostgreSQL Kalıcılık Katmanı | ✅ Tamamlandı |
| [3](arsiv/fazlar/03-SAGLAYICI-VE-DERLEYICI.md) | Sağlayıcı Katmanı ve Agent Derleyici | ✅ Tamamlandı |
| [4](arsiv/fazlar/04-HTTP-API.md) | HTTP API Katmanı | ✅ Tamamlandı |
| [5](arsiv/fazlar/05-AGENTPRISM-UI.md) | AgentPrism.UI | ✅ Tamamlandı |
| [6](arsiv/fazlar/06-GOZLEMLENEBILIRLIK.md) | Gözlemlenebilirlik, Tool Onayı, MCP ve Çok Kiracılılık | ✅ Tamamlandı |
| [7](arsiv/fazlar/07-SAGLAMLASTIRMA-VE-YAYIN.md) | Sağlamlaştırma ve Yayın | ⏸ Beklemede |
| [8](arsiv/fazlar/08-SAGLAYICI-GENISLEMESI.md) | Sağlayıcı Genişlemesi ve Sağlık Denetimi | ✅ Tamamlandı |
| [9](arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md) | Yönetişim: Rol Tabanlı Yetkilendirme ve Denetim İzi | ✅ Tamamlandı |
| [10](arsiv/fazlar/10-AGENT-SKILLERI.md) | Agent Skill'leri (script'siz) | ✅ Tamamlandı |
| [11](arsiv/fazlar/11-SKILL-SCRIPT-CALISTIRMA.md) | Skill Script Çalıştırma | ✅ Tamamlandı |
| [12](arsiv/fazlar/12-AGENT-CAGRI-GRAFIGI.md) | Agent'ın Agent'ı Çağırması | ✅ Tamamlandı |
| [13](arsiv/fazlar/13-BAGLAM-SIKISTIRMA-VE-BELLEK.md) | Bağlam Sıkıştırma ve Bellek Sağlayıcıları | ✅ Tamamlandı |
| [14](arsiv/fazlar/14-COK-MODLULUK.md) | Çok Modluluk: Görsel, Ses ve Dosya Girdisi | ✅ Tamamlandı |
| [15](arsiv/fazlar/15-WORKFLOWS-YURUTME.md) | Workflows: Yürütme ve Kalıcılık | ✅ Tamamlandı |
| [16](arsiv/fazlar/16-WORKFLOWS-ARAYUZ.md) | Workflows: Graf, Arayüz ve Human-in-the-Loop | ✅ Tamamlandı |
| [17](arsiv/fazlar/17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md) | Toplu ve Zamanlanmış Çalıştırma | ✅ Tamamlandı |
| [18](arsiv/fazlar/18-DEGERLENDIRME.md) | Değerlendirme (Eval) Altyapısı | ✅ Tamamlandı |
| [19](arsiv/fazlar/19-SURUM-KARSILASTIRMA-VE-AB.md) | Sürüm Karşılaştırma, Diff ve A/B | ✅ Tamamlandı |
| [20](arsiv/fazlar/20-MALIYET-VE-GOSTERGE-PANELI.md) | Maliyet Raporlaması ve Gösterge Paneli | ✅ Tamamlandı |
| [21](arsiv/fazlar/21-KOTA-VE-OLAY-YAYINI.md) | Hız Sınırı, Kota ve Olay Yayını | ✅ Tamamlandı |
| [22](arsiv/fazlar/22-MCP-DERINLESMESI.md) | MCP Derinleşmesi: Prompts, Resources ve OAuth | ✅ Tamamlandı |
| [23](arsiv/fazlar/23-SQL-SERVER.md) | SQL Server Desteği | ✅ Tamamlandı |
| [24](arsiv/fazlar/24-SQLITE.md) | SQLite Desteği | Kod tamam · 205/205 sözleşme+diyalekt testi yeşil · AOT ölçülmedi (bkz. "Açık Kalan") |
| [25](arsiv/fazlar/25-VERI-SAKLAMA-VE-ARSIVLEME.md) | Veri Saklama Politikası ve Arşivleme | ✅ Tamamlandı |
| [26](arsiv/fazlar/26-ANTHROPIC-VE-GEMINI.md) | Anthropic (Claude) ve Google Gemini Sağlayıcıları | ✅ Tamamlandı |
| [27](arsiv/fazlar/27-AZURE-FOUNDRY.md) | Azure OpenAI (ve ertelenen Azure AI Foundry) | ✅ Tamamlandı |
| [28](arsiv/fazlar/28-SES-TOOLLARI.md) | Ses Tool'ları (ElevenLabs) | ✅ Tamamlandı |
| [29](arsiv/fazlar/29-KONUSMA-KATMANI.md) | Konuşma Katmanı (Gerçek Zamanlı Ses) | ✅ Tamamlandı |
| [30](arsiv/fazlar/30-ARAYUZ-CILASI.md) | Arayüz Cilası: Yerelleştirme, Komut Paleti ve Kısayollar | ✅ Tamamlandı |
| [31](arsiv/fazlar/31-GERI-BILDIRIM-VE-PUANLAMA.md) | Geri Bildirim ve Puanlama | ✅ Tamamlandı |
| [32](arsiv/fazlar/32-CALISTIRMA-IPTALI.md) | Çalıştırma İptali | ✅ Tamamlandı |
| [33](arsiv/fazlar/33-SAGLIK-DENETIMI-VE-TESHIS.md) | Sağlık Denetimi ve Yapılandırma Teşhisi | ✅ Tamamlandı |
| [34](arsiv/fazlar/34-TANIM-DOGRULAMA-UCU.md) | Tanım Doğrulama Ucu | ✅ Tamamlandı |
| [35](arsiv/fazlar/35-MALIYET-VE-KOTA-METRIKLERI.md) | Maliyet ve Kota Metrikleri | ✅ Tamamlandı |
| [36](arsiv/fazlar/36-SAKLAMA-HACIM-SINIRI.md) | Saklama Hacim Sınırı (`MaxRows`) | ✅ Tamamlandı |
| [37](arsiv/fazlar/37-PROJE-SABLONU.md) | `dotnet new` Proje Şablonu | ✅ Tamamlandı |
| [38](arsiv/fazlar/38-YAPILANDIRILMIS-CIKTI.md) | Yapılandırılmış Çıktı (JSON Şeması) | ✅ Tamamlandı |
| [39](arsiv/fazlar/39-TEST-PAKETI.md) | `AgentPrism.Testing` Paketi | ✅ Tamamlandı |
| [40](arsiv/fazlar/40-OPENAPI-YAYINI.md) | OpenAPI Belgesinin Yayımlanması | ✅ Tamamlandı |
| [41](arsiv/fazlar/41-KIRACI-YALITIMININ-ZORLANMASI.md) | Kiracı Yalıtımının Zorlanması | ✅ Tamamlandı |
| [42](arsiv/fazlar/42-TEK-YURUTUCU-SECIMI.md) | Tek Yürütücü Seçimi (Çok Örnekli Koordinasyon) | ✅ Tamamlandı |
| [43](arsiv/fazlar/43-IDEMPOTENCY-KEY.md) | `Idempotency-Key` Desteği | ✅ Tamamlandı |
| [44](arsiv/fazlar/44-HATA-SINIFLANDIRMA.md) | Hata Sınıflandırma ve Arıza Kümeleme | ✅ Tamamlandı |
| [45](arsiv/fazlar/45-URETIMDEN-EVAL-KUMESI.md) | Üretimden Değerlendirme Veri Kümesi Toplama | ✅ Tamamlandı |
| [46](arsiv/fazlar/46-DAYANIKLI-CALISTIRMA.md) | Dayanıklı Çalıştırma (`202 Accepted`) | ✅ Tamamlandı |
| [47](arsiv/fazlar/47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md) | Yeniden Oynatma ve Konuşma Dallandırma | ✅ Tamamlandı |
| [48](arsiv/fazlar/48-GUARDRAILS.md) | Guardrails ve İçerik Güvenliği Genişleme Noktası | ✅ Tamamlandı |
| [49](arsiv/fazlar/49-CEVRIMICI-DEGERLENDIRME.md) | Çevrimiçi Değerlendirme (üretim trafiğinde yargıç) | ✅ Tamamlandı |
| [50](arsiv/fazlar/50-DISA-ACILAN-AGENT-YUZEYI.md) | Dışa Açılan Agent Yüzeyi (MCP sunucusu ve A2A) | ✅ Tamamlandı |
| [51](arsiv/fazlar/51-VEKTOR-BELLEK-VE-RAG.md) | Vektör Bellek ve RAG (`pgvector`) | ✅ Tamamlandı |
| [52](arsiv/fazlar/52-KAYNAK-URETECI.md) | Tool Kaynak Üreteci ve Derleme Anı Doğrulama | ✅ Tamamlandı |
| [53](arsiv/fazlar/53-KIRACI-API-ANAHTARLARI.md) | Kiracı Bazlı API Anahtarları ve Kapsamlar | ✅ Tamamlandı |
| [54](arsiv/fazlar/54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md) | Öksüz Çalıştırma Uzlaştırması | ✅ Tamamlandı |
| [55](arsiv/fazlar/55-ASENKRON-ONAY-KUTUSU.md) | Asenkron Onay Kutusu | ✅ Tamamlandı |
| [56](arsiv/fazlar/56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md) | Kanarya Yayını ve Otomatik Geri Alma | ✅ Tamamlandı |
| [57](arsiv/fazlar/57-KOD-DILI-BIRLESTIRME.md) | Kod Dili Birleştirme (İngilizce) | ✅ Tamamlandı |
| [58](arsiv/fazlar/58-DOKUMAN-DUZENI.md) | Doküman Düzeni | ✅ Tamamlandı |
| [59](arsiv/fazlar/59-URUN-DOKUMANTASYONU.md) | Ürün Dokümantasyonu (Doküman Sitesi) | ✅ Tamamlandı |
| [60](arsiv/fazlar/60-PUBLIC-API-KAPISI.md) | Public API Kapısı | ✅ Tamamlandı |
| [61](arsiv/fazlar/61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md) | İstemci Tool'ları ve Gömülebilir Sohbet | ✅ Tamamlandı |
| [62](arsiv/fazlar/62-MODEL-YEDEK-ZINCIRI-VE-ON-UCUS-DENETIMI.md) | Model Yedek Zinciri ve Ön Uçuş Denetimi | ✅ Tamamlandı |
| [63](arsiv/fazlar/63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md) | Argüman Düzeyinde Onay Politikası | ✅ Tamamlandı |
| [64](arsiv/fazlar/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md) | Denetim Zinciri ve Veri Konusu Hakları | ✅ Tamamlandı |
| [65](arsiv/fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md) | Kiracı Sağlayıcı Anahtarları (BYOK) | ✅ Tamamlandı |
| [66](arsiv/fazlar/66-GELEN-TETIKLEYICILER.md) | Gelen Tetikleyiciler | ✅ Tamamlandı |
| [67](arsiv/fazlar/67-ISTEGE-BAGLI-MIGRATION-SETI.md) | İsteğe Bağlı Migration Seti (`pgvector` opt-in) | ✅ Tamamlandı |
| [68](arsiv/fazlar/68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) | Çalıştırma Kimliği ve Token Kırılımı | ✅ Tamamlandı |
| [69](arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) | Tool Yetkilendirmesi ve Yürütme Timeout'u | ✅ Tamamlandı |
| [70](arsiv/fazlar/70-CALISTIRMA-OLAYI-HEDEFI.md) | Çalıştırma Olayı Hedefi ve Düşünme Akışı | ✅ Tamamlandı |
| [71](arsiv/fazlar/71-WORKFLOW-KOD-DUGUMU.md) | Workflow Kod Düğümü | ✅ Tamamlandı |
| [72](arsiv/fazlar/72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) | Çok Dilli Talimat ve Zaman Damgalı Sentez | ✅ Tamamlandı |
| [73](arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md) | Tüketici Agent Desteği | ✅ Tamamlandı |
| [74](arsiv/fazlar/74-YEREL-REFERANS-YUZEYI.md) | Yerel Referans Yüzeyi | ✅ Tamamlandı |
| [75](arsiv/fazlar/75-TUKETICI-DOKUMAN-DOGRULUGU.md) | Tüketici Dokümanının Doğruluğu | ✅ Tamamlandı |
| [76](arsiv/fazlar/76-DOKUMAN-KALITESI-VE-GORSEL-KIMLIK.md) | Doküman Kalitesi ve Görsel Kimlik | ✅ Tamamlandı |
| [77](arsiv/fazlar/77-GIDEN-AG-MUHAFIZI.md) | Giden Ağ Muhafızı | ✅ Tamamlandı |
| [78](arsiv/fazlar/78-YETENEK-HARITASI-ERISIMI.md) | Yetenek Haritası Erişimi | ✅ Tamamlandı |
| [79](arsiv/fazlar/79-SEVK-EDILEN-YUZEY-KAPILARI.md) | Sevk Edilen Yüzey Kapıları | ✅ Tamamlandı |
| [80](arsiv/fazlar/80-DOKUMAN-KAPILARININ-DOGRULUGU.md) | Doküman Kapılarının Doğruluğu | ✅ Tamamlandı |
| [81](arsiv/fazlar/81-YANIT-ONBELLEGI-VE-ESZAMANLI-TOOL.md) | Yanıt Önbelleği ve Eşzamanlı Tool Çağrısı | ✅ Tamamlandı |
| [82](arsiv/fazlar/82-ICERIK-KORUMASI.md) | İçerik Koruması (at-rest) | ✅ Tamamlandı |
| [83](arsiv/fazlar/83-TIPLI-ISTEMCI-VE-CLI.md) | Tipli Yönetim İstemcisi ve CLI | ✅ Tamamlandı |
| [84](arsiv/fazlar/84-TYPESCRIPT-ISTEMCISI-VE-NPM.md) | TypeScript İstemcisi ve npm Kanalı | ✅ Tamamlandı |
| [85](arsiv/fazlar/85-GOMME-EKSENI.md) | Gömme Ekseni | ✅ Tamamlandı |
| [86](arsiv/fazlar/86-TALIMATIN-GIRDI-YUZEYI.md) | Talimatın Girdi Yüzeyi | ✅ Tamamlandı |
| [87](arsiv/fazlar/87-KESILEN-ISIN-DEVAMI.md) | Kesilen İşin Devamı | ✅ Tamamlandı |
| [88](arsiv/fazlar/88-GORSEL-URETIM-TOOLU.md) | Görsel Üretim Tool'u | ✅ Tamamlandı |
| [89](arsiv/fazlar/89-TOOL-CIKTISI-BOYUT-SINIRI.md) | Tool Çıktısı Boyut Sınırı | ✅ Tamamlandı |
| [90](arsiv/fazlar/90-DOKUMAN-DAMITMA-POLITIKASI.md) | Doküman Damıtma Politikası | ✅ Tamamlandı |
| [91](arsiv/fazlar/91-GELISTIRME-DONGUSU-KAPILARI.md) | Geliştirme Döngüsü Kapıları | ✅ Tamamlandı |
| [92](arsiv/fazlar/92-ZINCIR-KONSOLIDASYONU.md) | Zincir Konsolidasyonu | ✅ Tamamlandı |
| [93](arsiv/fazlar/93-KUSUR-SINIFI-KAPILARI.md) | Kusur Sınıfı Kapıları | ✅ Tamamlandı |
| [94](arsiv/fazlar/94-SQL-TEK-KAYNAK.md) | SQL Tek Kaynak | ✅ Tamamlandı |
| [95](arsiv/fazlar/95-GERCEK-TUKETICI-KAPISI.md) | Gerçek Tüketici Kapısı | ✅ Tamamlandı |
| [96](arsiv/fazlar/96-PUBLIC-YUZEY-KUCULTME.md) | Public Yüzey Küçültme | ✅ Tamamlandı |
| [97](arsiv/fazlar/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md) | Sürüm Politikası ve Yayın Provası | ✅ Tamamlandı |
| [98](arsiv/fazlar/98-DEPOLAMA-SOZLESMESININ-YAYINI.md) | Depolama Sözleşmesinin Yayını | ✅ Tamamlandı |
| [99](arsiv/fazlar/99-SAGLAYICI-SOZLESMESININ-YAYINI.md) | Sağlayıcı Sözleşmesinin Yayını | ✅ Tamamlandı |
| [100](arsiv/fazlar/100-YARGIC-SOZLESMESININ-YAYINI.md) | Yargıç Sözleşmesinin Yayını | ✅ Tamamlandı |
| [101](arsiv/fazlar/101-KAYNAK-SOZLESMESININ-YAYINI.md) | Kaynak Sözleşmesinin Yayını | ✅ Tamamlandı |
| [102](arsiv/fazlar/102-TOOL-SOZLESMESI-VE-SONUC-SINIRI.md) | Tool Sözleşmesi ve Sonuç Sınırı | ✅ Tamamlandı |
| [103](arsiv/fazlar/103-EXTENSION-SOZLESMELERININ-YAYIN-ONCESI-SERTLESTIRILMESI.md) | Extension Sözleşmelerinin Yayın Öncesi Sertleştirilmesi | ✅ Tamamlandı |
| [104](arsiv/fazlar/104-BEYAN-DOGRULUGU-VE-GIRIS-RAMPASI.md) | Beyan Doğruluğu ve Giriş Rampası | ✅ Tamamlandı |
| [105](arsiv/fazlar/105-DI-BILESEN-KOKU-AYRISTIRMA.md) | DI Bileşen Kökü Ayrıştırma | ✅ Tamamlandı |
| [106](arsiv/fazlar/106-AGENT-DERLEYICI-AYRISTIRMA.md) | Agent Derleyici Ayrıştırma | ✅ Tamamlandı |
| [107](arsiv/fazlar/107-RUN-KAYIT-AKISI-AYRISTIRMA.md) | Run Kayıt Akışı Ayrıştırma | ✅ Tamamlandı |
| [108](arsiv/fazlar/108-BELLEK-ICI-RUN-STORE-AYRISTIRMA.md) | Bellek İçi Run Store Ayrıştırma | ✅ Tamamlandı |
| [109](arsiv/fazlar/109-FRONTEND-MODULLERI-VE-EKRAN-TESTLERI.md) | Frontend Modülleri ve Ekran Testleri | ✅ Tamamlandı |
| [110](arsiv/fazlar/110-TUKETICI-BAGLANTI-DUZLEMI.md) | Tüketici Bağlantı Düzlemi | ✅ Tamamlandı |
| [111](arsiv/fazlar/111-OKUMA-SOZLESMESI-GORUNUMLERI.md) | Okuma Sözleşmesi Görünümleri | ✅ Tamamlandı |
| [112](arsiv/fazlar/112-REPLAY-ISTEMCI-TOOL-SOZLESMESI.md) | Replay'in İstemci Tool Sözleşmesi | ✅ Tamamlandı |
| [113](arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md) | Sağlayıcı Arıza Sınıflandırmasının Genişleme Noktası | ✅ Tamamlandı |
| [114](arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md) | Çalıştırma-İçi Bütçe Tavanı | ✅ Tamamlandı |
| [115](arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md) | Eval'in Başsız Koşucusu | ✅ Tamamlandı |
| [116](arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md) | Performans Tahsis Kapısı | ✅ Tamamlandı |
| [117](arsiv/fazlar/117-MCP-TASKS-UZANTISI.md) | MCP Tasks Uzantısı | ✅ Tamamlandı |
| [118](arsiv/fazlar/118-YARGIC-BASINA-CHECKPOINT.md) | Yargıç Başına Checkpoint | ✅ Tamamlandı |
| [119](arsiv/fazlar/119-HATA-METNI-SIZINTISI.md) | Hata Metni Sızıntısının Kapatılması | ✅ Tamamlandı |
| [120](arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md) | `IJobHandler` Sözleşmesi: At-Least-Once Yazılı Hale Gelir | ✅ Tamamlandı |
| [121](arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md) | Seam Sözleşme Dokümanı ve Küçülen Taban Çizgisi | ✅ Tamamlandı |
| [122](arsiv/fazlar/122-KAYIT-API-SI-VE-SESSIZ-BOSLUKLAR.md) | Kayıt API'si ve Sessiz Boşluklar | ✅ Tamamlandı |
| [123](arsiv/fazlar/123-YAYIN-KRITIK-YOLU.md) | Yayın Kritik Yolu: Kapı Kapsamı, Adaptör Sözleşmesi ve Sürüm Notları | ✅ Tamamlandı |
| [124](arsiv/fazlar/124-YEDEKLEMENIN-TOOL-DEFTERI.md) | Yedeklemenin Tool Defteri | ✅ Tamamlandı |
| [125](arsiv/fazlar/125-URETILEN-TOOL-SEMASININ-IFADE-GUCU.md) | Üretilen Tool Şemasının İfade Gücü | ✅ Tamamlandı |
| [126](arsiv/fazlar/126-KALICI-PAYLOAD-SURUM-SOZLESMESI.md) | Kalıcı Payload Sürüm Sözleşmesi | ✅ Tamamlandı |
| [127](arsiv/fazlar/127-TOOL-KAYIT-YUZEYI.md) | Tool Kayıt Yüzeyi: Tek Kompozisyon, Argüman Kapısı ve Kapsamlı Tool | ✅ Tamamlandı |
| [128](128-RUN-AGACI-SURE-BUTCESI.md) | Run Ağacı Süre Bütçesi | 📋 Planlandı |

Seçilmemiş adaylar: [`ADAYLAR.md`](ADAYLAR.md). Fazların hangi dalgada, hangi gerekçeyle sıralandığı (Faz 8–56, kapandı): [`arsiv/IKINCI-FAZ-YOL-HARITASI.md`](arsiv/IKINCI-FAZ-YOL-HARITASI.md) · [`arsiv/UCUNCU-FAZ-YOL-HARITASI.md`](arsiv/UCUNCU-FAZ-YOL-HARITASI.md).

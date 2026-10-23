# Faz Defteri

> **Kapanan fazların tek satırlık izi.** Satırı
> `python3 scripts/dokuman-bakim.py faz-kapat <NN>` yazar ve faz dokümanını
> aynı anda siler. Elle yazılan tek hücre `Faz kapandıktan sonra bulunan
> kusur`dur (`kusur-giderme` Adım 6).
>
> Tam metin: `git show <Kayıt>`. Faz 0–218 satırları 2026-10-17'de
> `2a8149ef`'teki yol haritasından ve kayıtlardan dolduruldu; kademeleri `tam`
> çünkü hepsi tam zincirle koştu. Açık ve planlı fazlar:
> [`YOL-HARITASI.md`](YOL-HARITASI.md).

## Kapanan fazlar

| Faz | Konu | Kademe | Durum | Kapanış | Kaynak | Kayıt |
|---|---|---|---|---|---|---|
| 0 | Repository ve Build Altyapısı | tam | ✅ | — | — | `2a8149ef:docs/arsiv/fazlar/00-ALTYAPI.md` |
| 1 | Çekirdek Soyutlamalar ve Runtime | tam | ✅ | 2026-08-02 | — | `2a8149ef:docs/arsiv/fazlar/01-CEKIRDEK-SOYUTLAMALAR.md` |
| 2 | PostgreSQL Kalıcılık Katmanı | tam | ✅ | 2026-08-02 | — | `2a8149ef:docs/arsiv/fazlar/02-POSTGRESQL-KALICILIK.md` |
| 3 | Sağlayıcı Katmanı ve Agent Derleyici | tam | ✅ | 2026-08-02 | — | `2a8149ef:docs/arsiv/fazlar/03-SAGLAYICI-VE-DERLEYICI.md` |
| 4 | HTTP API Katmanı | tam | ✅ | 2026-08-02 | — | `2a8149ef:docs/arsiv/fazlar/04-HTTP-API.md` |
| 5 | Tracon.UI | tam | ✅ | 2026-08-02 | — | `2a8149ef:docs/arsiv/fazlar/05-TRACON-UI.md` |
| 6 | Gözlemlenebilirlik, Tool Onayı, MCP ve Çok Kiracılılık | tam | ✅ | 2026-08-02 | — | `2a8149ef:docs/arsiv/fazlar/06-GOZLEMLENEBILIRLIK.md` |
| 7 | Sağlamlaştırma ve Yayın | tam | ⏸ | — | — | `2a8149ef:docs/arsiv/fazlar/07-SAGLAMLASTIRMA-VE-YAYIN.md` |
| 8 | Sağlayıcı Genişlemesi ve Sağlık Denetimi | tam | ✅ | 2026-08-02 | F-03 · F-05 · F-16 | `2a8149ef:docs/arsiv/fazlar/08-SAGLAYICI-GENISLEMESI.md` |
| 9 | Yönetişim: Rol Tabanlı Yetkilendirme ve Denetim İzi | tam | ✅ | 2026-08-02 | F-21 · F-20 | `2a8149ef:docs/arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md` |
| 10 | Agent Skill'leri (script'siz) | tam | ✅ | 2026-08-02 | F-09 | `2a8149ef:docs/arsiv/fazlar/10-AGENT-SKILLERI.md` |
| 11 | Skill Script Çalıştırma | tam | ✅ | 2026-08-02 | F-09 | `2a8149ef:docs/arsiv/fazlar/11-SKILL-SCRIPT-CALISTIRMA.md` |
| 12 | Agent'ın Agent'ı Çağırması | tam | ✅ | 2026-08-02 | F-10 | `2a8149ef:docs/arsiv/fazlar/12-AGENT-CAGRI-GRAFIGI.md` |
| 13 | Bağlam Sıkıştırma ve Bellek Sağlayıcıları | tam | ✅ | 2026-08-02 | F-11 | `2a8149ef:docs/arsiv/fazlar/13-BAGLAM-SIKISTIRMA-VE-BELLEK.md` |
| 14 | Çok Modluluk: Görsel, Ses ve Dosya Girdisi | tam | ✅ | 2026-08-02 | F-12 | `2a8149ef:docs/arsiv/fazlar/14-COK-MODLULUK.md` |
| 15 | Workflows: Yürütme ve Kalıcılık | tam | ✅ | 2026-08-03 | F-27 | `2a8149ef:docs/arsiv/fazlar/15-WORKFLOWS-YURUTME.md` |
| 16 | Workflows: Graf, Arayüz ve Human-in-the-Loop | tam | ✅ | 2026-08-03 | F-27 | `2a8149ef:docs/arsiv/fazlar/16-WORKFLOWS-ARAYUZ.md` |
| 17 | Toplu ve Zamanlanmış Çalıştırma | tam | ✅ | 2026-08-03 | F-22 | `2a8149ef:docs/arsiv/fazlar/17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md` |
| 18 | Değerlendirme (Eval) Altyapısı | tam | ✅ | 2026-08-03 | F-14 | `2a8149ef:docs/arsiv/fazlar/18-DEGERLENDIRME.md` |
| 19 | Sürüm Karşılaştırma, Diff ve A/B | tam | ✅ | 2026-08-03 | F-15 · F-24 | `2a8149ef:docs/arsiv/fazlar/19-SURUM-KARSILASTIRMA-VE-AB.md` |
| 20 | Maliyet Raporlaması ve Gösterge Paneli | tam | ✅ | 2026-08-03 | F-17 · F-23 | `2a8149ef:docs/arsiv/fazlar/20-MALIYET-VE-GOSTERGE-PANELI.md` |
| 21 | Hız Sınırı, Kota ve Olay Yayını | tam | ✅ | 2026-08-03 | F-18 · F-19 | `2a8149ef:docs/arsiv/fazlar/21-KOTA-VE-OLAY-YAYINI.md` |
| 22 | MCP Derinleşmesi: Prompts, Resources ve OAuth | tam | ✅ | 2026-08-04 | F-28 · F-29 | `2a8149ef:docs/arsiv/fazlar/22-MCP-DERINLESMESI.md` |
| 23 | SQL Server Desteği | tam | ✅ | — | F-06 | `2a8149ef:docs/arsiv/fazlar/23-SQL-SERVER.md` |
| 24 | SQLite Desteği | tam | ✅ | — | F-07 | `2a8149ef:docs/arsiv/fazlar/24-SQLITE.md` |
| 25 | Veri Saklama Politikası ve Arşivleme | tam | ✅ | 2026-08-05 | F-08 | `2a8149ef:docs/arsiv/fazlar/25-VERI-SAKLAMA-VE-ARSIVLEME.md` |
| 26 | Anthropic (Claude) ve Google Gemini Sağlayıcıları | tam | ✅ | 2026-08-05 | F-01 · F-02 | `2a8149ef:docs/arsiv/fazlar/26-ANTHROPIC-VE-GEMINI.md` |
| 27 | Azure OpenAI (ve ertelenen Azure AI Foundry) | tam | ✅ | 2026-08-05 | F-04 | `2a8149ef:docs/arsiv/fazlar/27-AZURE-FOUNDRY.md` |
| 28 | Ses Tool'ları (ElevenLabs) | tam | ✅ | 2026-08-05 | F-13 | `2a8149ef:docs/arsiv/fazlar/28-SES-TOOLLARI.md` |
| 29 | Konuşma Katmanı (Gerçek Zamanlı Ses) | tam | ✅ | 2026-08-05 | F-13 | `2a8149ef:docs/arsiv/fazlar/29-KONUSMA-KATMANI.md` |
| 30 | Arayüz Cilası: Yerelleştirme, Komut Paleti ve Kısayollar | tam | ✅ | 2026-08-05 | F-25 · F-26 | `2a8149ef:docs/arsiv/fazlar/30-ARAYUZ-CILASI.md` |
| 31 | Geri Bildirim ve Puanlama | tam | ✅ | 2026-08-06 | F-52 | `2a8149ef:docs/arsiv/fazlar/31-GERI-BILDIRIM-VE-PUANLAMA.md` |
| 32 | Çalıştırma İptali | tam | ✅ | 2026-08-06 | F-35 | `2a8149ef:docs/arsiv/fazlar/32-CALISTIRMA-IPTALI.md` |
| 33 | Sağlık Denetimi ve Yapılandırma Teşhisi | tam | ✅ | 2026-08-06 | F-38 · F-62 | `2a8149ef:docs/arsiv/fazlar/33-SAGLIK-DENETIMI-VE-TESHIS.md` |
| 34 | Tanım Doğrulama Ucu | tam | ✅ | 2026-08-06 | F-60 | `2a8149ef:docs/arsiv/fazlar/34-TANIM-DOGRULAMA-UCU.md` |
| 35 | Maliyet ve Kota Metrikleri | tam | ✅ | 2026-08-06 | F-70 | `2a8149ef:docs/arsiv/fazlar/35-MALIYET-VE-KOTA-METRIKLERI.md` |
| 36 | Saklama Hacim Sınırı (`MaxRows`) | tam | ✅ | 2026-08-06 | F-73 | `2a8149ef:docs/arsiv/fazlar/36-SAKLAMA-HACIM-SINIRI.md` |
| 37 | `dotnet new` Proje Şablonu | tam | ✅ | 2026-08-06 | F-49 | `2a8149ef:docs/arsiv/fazlar/37-PROJE-SABLONU.md` |
| 38 | Yapılandırılmış Çıktı (JSON Şeması) | tam | ✅ | 2026-08-06 | F-42 | `2a8149ef:docs/arsiv/fazlar/38-YAPILANDIRILMIS-CIKTI.md` |
| 39 | `Tracon.Testing` Paketi | tam | ✅ | 2026-08-06 | F-46 | `2a8149ef:docs/arsiv/fazlar/39-TEST-PAKETI.md` |
| 40 | OpenAPI Belgesinin Yayımlanması | tam | ✅ | 2026-08-07 | F-63 | `2a8149ef:docs/arsiv/fazlar/40-OPENAPI-YAYINI.md` |
| 41 | Kiracı Yalıtımının Zorlanması | tam | ✅ | 2026-08-07 | F-76 | `2a8149ef:docs/arsiv/fazlar/41-KIRACI-YALITIMININ-ZORLANMASI.md` |
| 42 | Tek Yürütücü Seçimi (Çok Örnekli Koordinasyon) | tam | ✅ | 2026-08-07 | F-57 | `2a8149ef:docs/arsiv/fazlar/42-TEK-YURUTUCU-SECIMI.md` |
| 43 | `Idempotency-Key` Desteği | tam | ✅ | 2026-08-07 | F-37 | `2a8149ef:docs/arsiv/fazlar/43-IDEMPOTENCY-KEY.md` |
| 44 | Hata Sınıflandırma ve Arıza Kümeleme | tam | ✅ | 2026-08-07 | F-55 | `2a8149ef:docs/arsiv/fazlar/44-HATA-SINIFLANDIRMA.md` |
| 45 | Üretimden Değerlendirme Veri Kümesi Toplama | tam | ✅ | 2026-08-07 | F-53 | `2a8149ef:docs/arsiv/fazlar/45-URETIMDEN-EVAL-KUMESI.md` |
| 46 | Dayanıklı Çalıştırma (`202 Accepted`) | tam | ✅ | 2026-08-07 | F-68 · F-39 | `2a8149ef:docs/arsiv/fazlar/46-DAYANIKLI-CALISTIRMA.md` |
| 47 | Yeniden Oynatma ve Konuşma Dallandırma | tam | ✅ | 2026-08-07 | F-54 · F-66 | `2a8149ef:docs/arsiv/fazlar/47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md` |
| 48 | Guardrails ve İçerik Güvenliği Genişleme Noktası | tam | ✅ | 2026-08-07 | F-32 | `2a8149ef:docs/arsiv/fazlar/48-GUARDRAILS.md` |
| 49 | Çevrimiçi Değerlendirme (üretim trafiğinde yargıç) | tam | ✅ | 2026-08-07 | F-71 | `2a8149ef:docs/arsiv/fazlar/49-CEVRIMICI-DEGERLENDIRME.md` |
| 50 | Dışa Açılan Agent Yüzeyi (MCP sunucusu ve A2A) | tam | ✅ | 2026-08-08 | F-31 · F-33 | `2a8149ef:docs/arsiv/fazlar/50-DISA-ACILAN-AGENT-YUZEYI.md` |
| 51 | Vektör Bellek ve RAG (`pgvector`) | tam | ✅ | 2026-08-08 | F-30 | `2a8149ef:docs/arsiv/fazlar/51-VEKTOR-BELLEK-VE-RAG.md` |
| 52 | Tool Kaynak Üreteci ve Derleme Anı Doğrulama | tam | ✅ | 2026-08-08 | F-47 | `2a8149ef:docs/arsiv/fazlar/52-KAYNAK-URETECI.md` |
| 53 | Kiracı Bazlı API Anahtarları ve Kapsamlar | tam | ✅ | 2026-08-08 | F-56 | `2a8149ef:docs/arsiv/fazlar/53-KIRACI-API-ANAHTARLARI.md` |
| 54 | Öksüz Çalıştırma Uzlaştırması | tam | ✅ | 2026-08-09 | F-36 | `2a8149ef:docs/arsiv/fazlar/54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md` |
| 55 | Asenkron Onay Kutusu | tam | ✅ | 2026-08-09 | F-69 | `2a8149ef:docs/arsiv/fazlar/55-ASENKRON-ONAY-KUTUSU.md` |
| 56 | Kanarya Yayını ve Otomatik Geri Alma | tam | ✅ | 2026-08-09 | F-74 | `2a8149ef:docs/arsiv/fazlar/56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md` |
| 57 | Kod Dili Birleştirme (İngilizce) | tam | ✅ | 2026-08-16 | — | `2a8149ef:docs/arsiv/fazlar/57-KOD-DILI-BIRLESTIRME.md` |
| 58 | Doküman Düzeni | tam | ✅ | 2026-08-16 | — | `2a8149ef:docs/arsiv/fazlar/58-DOKUMAN-DUZENI.md` |
| 59 | Ürün Dokümantasyonu (Doküman Sitesi) | tam | ✅ | 2026-08-16 | — | `2a8149ef:docs/arsiv/fazlar/59-URUN-DOKUMANTASYONU.md` |
| 60 | Public API Kapısı | tam | ✅ | 2026-08-16 | — | `2a8149ef:docs/arsiv/fazlar/60-PUBLIC-API-KAPISI.md` |
| 61 | İstemci Tool'ları ve Gömülebilir Sohbet | tam | ✅ | 2026-08-18 | F-108 · F-64 | `2a8149ef:docs/arsiv/fazlar/61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md` |
| 62 | Model Yedek Zinciri ve Ön Uçuş Denetimi | tam | ✅ | 2026-08-18 | F-44 · F-59 | `2a8149ef:docs/arsiv/fazlar/62-MODEL-YEDEK-ZINCIRI-VE-ON-UCUS-DENETIMI.md` |
| 63 | Argüman Düzeyinde Onay Politikası | tam | ✅ | 2026-08-18 | F-61 | `2a8149ef:docs/arsiv/fazlar/63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md` |
| 64 | Denetim Zinciri ve Veri Konusu Hakları | tam | ✅ | 2026-08-18 | F-75 · F-58 | `2a8149ef:docs/arsiv/fazlar/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md` |
| 65 | Kiracı Sağlayıcı Anahtarları (BYOK) | tam | ✅ | 2026-08-19 | F-40 · F-119 | `2a8149ef:docs/arsiv/fazlar/65-KIRACI-SAGLAYICI-ANAHTARLARI.md` |
| 66 | Gelen Tetikleyiciler | tam | ✅ | 2026-08-19 | F-65 | `2a8149ef:docs/arsiv/fazlar/66-GELEN-TETIKLEYICILER.md` |
| 67 | İsteğe Bağlı Migration Seti (`pgvector` opt-in) | tam | ✅ | 2026-08-19 | F-110 | `2a8149ef:docs/arsiv/fazlar/67-ISTEGE-BAGLI-MIGRATION-SETI.md` |
| 68 | Çalıştırma Kimliği ve Token Kırılımı | tam | ✅ | 2026-08-19 | F-111 · F-112 | `2a8149ef:docs/arsiv/fazlar/68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md` |
| 69 | Tool Yetkilendirmesi ve Yürütme Timeout'u | tam | ✅ | 2026-08-19 | F-113 · F-114 | `2a8149ef:docs/arsiv/fazlar/69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md` |
| 70 | Çalıştırma Olayı Hedefi ve Düşünme Akışı | tam | ✅ | 2026-08-19 | F-115 | `2a8149ef:docs/arsiv/fazlar/70-CALISTIRMA-OLAYI-HEDEFI.md` |
| 71 | Workflow Kod Düğümü | tam | ✅ | 2026-08-19 | F-116 | `2a8149ef:docs/arsiv/fazlar/71-WORKFLOW-KOD-DUGUMU.md` |
| 72 | Çok Dilli Talimat ve Zaman Damgalı Sentez | tam | ✅ | 2026-08-19 | F-117 · F-118 | `2a8149ef:docs/arsiv/fazlar/72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md` |
| 73 | Tüketici Agent Desteği | tam | ✅ | 2026-08-19 | F-120 | `2a8149ef:docs/arsiv/fazlar/73-TUKETICI-AGENT-DESTEGI.md` |
| 74 | Yerel Referans Yüzeyi | tam | ✅ | 2026-08-20 | F-121 | `2a8149ef:docs/arsiv/fazlar/74-YEREL-REFERANS-YUZEYI.md` |
| 75 | Tüketici Dokümanının Doğruluğu | tam | ✅ | 2026-08-20 | F-126 · F-124 | `2a8149ef:docs/arsiv/fazlar/75-TUKETICI-DOKUMAN-DOGRULUGU.md` |
| 76 | Doküman Kalitesi ve Görsel Kimlik | tam | ✅ | 2026-08-20 | F-127 | `2a8149ef:docs/arsiv/fazlar/76-DOKUMAN-KALITESI-VE-GORSEL-KIMLIK.md` |
| 77 | Giden Ağ Muhafızı | tam | ✅ | 2026-08-20 | F-131 | `2a8149ef:docs/arsiv/fazlar/77-GIDEN-AG-MUHAFIZI.md` |
| 78 | Yetenek Haritası Erişimi | tam | ✅ | 2026-08-21 | F-135 | `2a8149ef:docs/arsiv/fazlar/78-YETENEK-HARITASI-ERISIMI.md` |
| 79 | Sevk Edilen Yüzey Kapıları | tam | ✅ | 2026-08-21 | F-125 · F-136 | `2a8149ef:docs/arsiv/fazlar/79-SEVK-EDILEN-YUZEY-KAPILARI.md` |
| 80 | Doküman Kapılarının Doğruluğu | tam | ✅ | 2026-08-21 | F-129 | `2a8149ef:docs/arsiv/fazlar/80-DOKUMAN-KAPILARININ-DOGRULUGU.md` |
| 81 | Yanıt Önbelleği ve Eşzamanlı Tool Çağrısı | tam | ✅ | 2026-08-22 | F-45 · F-134 | `2a8149ef:docs/arsiv/fazlar/81-YANIT-ONBELLEGI-VE-ESZAMANLI-TOOL.md` |
| 82 | İçerik Koruması (at-rest) | tam | ✅ | 2026-08-22 | F-41 | `2a8149ef:docs/arsiv/fazlar/82-ICERIK-KORUMASI.md` |
| 83 | Tipli Yönetim İstemcisi ve CLI | tam | ✅ | 2026-08-22 | F-50 | `2a8149ef:docs/arsiv/fazlar/83-TIPLI-ISTEMCI-VE-CLI.md` |
| 84 | TypeScript İstemcisi ve npm Kanalı | tam | ✅ | 2026-08-22 | F-93 · F-50 | `2a8149ef:docs/arsiv/fazlar/84-TYPESCRIPT-ISTEMCISI-VE-NPM.md` |
| 85 | Gömme Ekseni | tam | ✅ | 2026-08-22 | F-140 | `2a8149ef:docs/arsiv/fazlar/85-GOMME-EKSENI.md` |
| 86 | Talimatın Girdi Yüzeyi | tam | ✅ | 2026-08-22 | F-34 | `2a8149ef:docs/arsiv/fazlar/86-TALIMATIN-GIRDI-YUZEYI.md` |
| 87 | Kesilen İşin Devamı | tam | ✅ | 2026-08-23 | F-141 | `2a8149ef:docs/arsiv/fazlar/87-KESILEN-ISIN-DEVAMI.md` |
| 88 | Görsel Üretim Tool'u | tam | ✅ | 2026-08-23 | F-142 | `2a8149ef:docs/arsiv/fazlar/88-GORSEL-URETIM-TOOLU.md` |
| 89 | Tool Çıktısı Boyut Sınırı | tam | ✅ | 2026-08-23 | F-143 | `2a8149ef:docs/arsiv/fazlar/89-TOOL-CIKTISI-BOYUT-SINIRI.md` |
| 90 | Doküman Damıtma Politikası | tam | ✅ | 2026-08-23 | — | `2a8149ef:docs/arsiv/fazlar/90-DOKUMAN-DAMITMA-POLITIKASI.md` |
| 91 | Geliştirme Döngüsü Kapıları | tam | ✅ | 2026-08-23 | — | `2a8149ef:docs/arsiv/fazlar/91-GELISTIRME-DONGUSU-KAPILARI.md` |
| 92 | Zincir Konsolidasyonu | tam | ✅ | 2026-08-23 | — | `2a8149ef:docs/arsiv/fazlar/92-ZINCIR-KONSOLIDASYONU.md` |
| 93 | Kusur Sınıfı Kapıları | tam | ✅ | 2026-08-24 | — | `2a8149ef:docs/arsiv/fazlar/93-KUSUR-SINIFI-KAPILARI.md` |
| 94 | SQL Tek Kaynak | tam | ✅ | 2026-08-24 | — | `2a8149ef:docs/arsiv/fazlar/94-SQL-TEK-KAYNAK.md` |
| 95 | Gerçek Tüketici Kapısı | tam | ✅ | 2026-08-24 | — | `2a8149ef:docs/arsiv/fazlar/95-GERCEK-TUKETICI-KAPISI.md` |
| 96 | Public Yüzey Küçültme | tam | ✅ | 2026-08-24 | — | `2a8149ef:docs/arsiv/fazlar/96-PUBLIC-YUZEY-KUCULTME.md` |
| 97 | Sürüm Politikası ve Yayın Provası | tam | ✅ | 2026-08-24 | — | `2a8149ef:docs/arsiv/fazlar/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md` |
| 98 | Depolama Sözleşmesinin Yayını | tam | ✅ | 2026-08-24 | — | `2a8149ef:docs/arsiv/fazlar/98-DEPOLAMA-SOZLESMESININ-YAYINI.md` |
| 99 | Sağlayıcı Sözleşmesinin Yayını | tam | ✅ | 2026-08-25 | — | `2a8149ef:docs/arsiv/fazlar/99-SAGLAYICI-SOZLESMESININ-YAYINI.md` |
| 100 | Yargıç Sözleşmesinin Yayını | tam | ✅ | 2026-08-25 | — | `2a8149ef:docs/arsiv/fazlar/100-YARGIC-SOZLESMESININ-YAYINI.md` |
| 101 | Kaynak Sözleşmesinin Yayını | tam | ✅ | 2026-08-25 | — | `2a8149ef:docs/arsiv/fazlar/101-KAYNAK-SOZLESMESININ-YAYINI.md` |
| 102 | Tool Sözleşmesi ve Sonuç Sınırı | tam | ✅ | 2026-08-25 | — | `2a8149ef:docs/arsiv/fazlar/102-TOOL-SOZLESMESI-VE-SONUC-SINIRI.md` |
| 103 | Extension Sözleşmelerinin Yayın Öncesi Sertleştirilmesi | tam | ✅ | 2026-08-25 | F-153 | `2a8149ef:docs/arsiv/fazlar/103-EXTENSION-SOZLESMELERININ-YAYIN-ONCESI-SERTLESTIRILMESI.md` |
| 104 | Beyan Doğruluğu ve Giriş Rampası | tam | ✅ | 2026-08-25 | — | `2a8149ef:docs/arsiv/fazlar/104-BEYAN-DOGRULUGU-VE-GIRIS-RAMPASI.md` |
| 105 | DI Bileşen Kökü Ayrıştırma | tam | ✅ | 2026-08-26 | — | `2a8149ef:docs/arsiv/fazlar/105-DI-BILESEN-KOKU-AYRISTIRMA.md` |
| 106 | Agent Derleyici Ayrıştırma | tam | ✅ | 2026-08-26 | — | `2a8149ef:docs/arsiv/fazlar/106-AGENT-DERLEYICI-AYRISTIRMA.md` |
| 107 | Run Kayıt Akışı Ayrıştırma | tam | ✅ | 2026-08-26 | — | `2a8149ef:docs/arsiv/fazlar/107-RUN-KAYIT-AKISI-AYRISTIRMA.md` |
| 108 | Bellek İçi Run Store Ayrıştırma | tam | ✅ | 2026-08-26 | — | `2a8149ef:docs/arsiv/fazlar/108-BELLEK-ICI-RUN-STORE-AYRISTIRMA.md` |
| 109 | Frontend Modülleri ve Ekran Testleri | tam | ✅ | 2026-08-26 | — | `2a8149ef:docs/arsiv/fazlar/109-FRONTEND-MODULLERI-VE-EKRAN-TESTLERI.md` |
| 110 | Tüketici Bağlantı Düzlemi | tam | ✅ | 2026-08-26 | — | `2a8149ef:docs/arsiv/fazlar/110-TUKETICI-BAGLANTI-DUZLEMI.md` |
| 111 | Okuma Sözleşmesi Görünümleri | tam | ✅ | 2026-08-26 | — | `2a8149ef:docs/arsiv/fazlar/111-OKUMA-SOZLESMESI-GORUNUMLERI.md` |
| 112 | Replay'in İstemci Tool Sözleşmesi | tam | ✅ | 2026-08-26 | F-109 | `2a8149ef:docs/arsiv/fazlar/112-REPLAY-ISTEMCI-TOOL-SOZLESMESI.md` |
| 113 | Sağlayıcı Arıza Sınıflandırmasının Genişleme Noktası | tam | ✅ | 2026-08-26 | F-149 | `2a8149ef:docs/arsiv/fazlar/113-ARIZA-SINIFLANDIRMA-SEAMI.md` |
| 114 | Çalıştırma-İçi Bütçe Tavanı | tam | ✅ | 2026-08-26 | F-166 | `2a8149ef:docs/arsiv/fazlar/114-CALISTIRMA-ICI-BUTCE-TAVANI.md` |
| 115 | Eval'in Başsız Koşucusu | tam | ✅ | 2026-08-27 | F-168 | `2a8149ef:docs/arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md` |
| 116 | Performans Tahsis Kapısı | tam | ✅ | 2026-08-27 | F-67 · F-163 | `2a8149ef:docs/arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md` |
| 117 | MCP Tasks Uzantısı | tam | ✅ | 2026-08-27 | F-167 | `2a8149ef:docs/arsiv/fazlar/117-MCP-TASKS-UZANTISI.md` |
| 118 | Yargıç Başına Checkpoint | tam | ✅ | 2026-08-27 | F-152 | `2a8149ef:docs/arsiv/fazlar/118-YARGIC-BASINA-CHECKPOINT.md` |
| 119 | Hata Metni Sızıntısının Kapatılması | tam | ✅ | 2026-08-27 | — | `2a8149ef:docs/arsiv/fazlar/119-HATA-METNI-SIZINTISI.md` |
| 120 | `IJobHandler` Sözleşmesi: At-Least-Once Yazılı Hale Gelir | tam | ✅ | 2026-08-27 | — | `2a8149ef:docs/arsiv/fazlar/120-JOB-SOZLESMESI-AT-LEAST-ONCE.md` |
| 121 | Seam Sözleşme Dokümanı ve Küçülen Taban Çizgisi | tam | ✅ | 2026-08-28 | — | `2a8149ef:docs/arsiv/fazlar/121-SEAM-SOZLESME-DOKUMANI.md` |
| 122 | Kayıt API'si ve Sessiz Boşluklar | tam | ✅ | 2026-08-28 | — | `2a8149ef:docs/arsiv/fazlar/122-KAYIT-API-SI-VE-SESSIZ-BOSLUKLAR.md` |
| 123 | Yayın Kritik Yolu: Kapı Kapsamı, Adaptör Sözleşmesi ve Sürüm Notları | tam | ✅ | 2026-08-28 | — | `2a8149ef:docs/arsiv/fazlar/123-YAYIN-KRITIK-YOLU.md` |
| 124 | Yedeklemenin Tool Defteri | tam | ✅ | 2026-08-31 | — | `2a8149ef:docs/arsiv/fazlar/124-YEDEKLEMENIN-TOOL-DEFTERI.md` |
| 125 | Üretilen Tool Şemasının İfade Gücü | tam | ✅ | 2026-09-01 | — | `2a8149ef:docs/arsiv/fazlar/125-URETILEN-TOOL-SEMASININ-IFADE-GUCU.md` |
| 126 | Kalıcı Payload Sürüm Sözleşmesi | tam | ✅ | 2026-09-01 | — | `2a8149ef:docs/arsiv/fazlar/126-KALICI-PAYLOAD-SURUM-SOZLESMESI.md` |
| 127 | Tool Kayıt Yüzeyi: Tek Kompozisyon, Argüman Kapısı ve Kapsamlı Tool | tam | ✅ | 2026-09-01 | — | `2a8149ef:docs/arsiv/fazlar/127-TOOL-KAYIT-YUZEYI.md` |
| 128 | Run Ağacı Süre Bütçesi | tam | ✅ | 2026-09-01 | — | `2a8149ef:docs/arsiv/fazlar/128-RUN-AGACI-SURE-BUTCESI.md` |
| 129 | İş Kuyruğu `lane`'leri | tam | ✅ | 2026-09-01 | F-172 | `2a8149ef:docs/arsiv/fazlar/129-IS-KUYRUGU-LANELERI.md` |
| 130 | Üretilen Şemanın Kısıtları | tam | ✅ | 2026-09-01 | F-173 | `2a8149ef:docs/arsiv/fazlar/130-URETILEN-SEMANIN-KISITLARI.md` |
| 131 | Yapısal Yanıt Doğrulama Seam'i | tam | ✅ | 2026-09-01 | F-174 | `2a8149ef:docs/arsiv/fazlar/131-YAPISAL-YANIT-DOGRULAMA-SEAMI.md` |
| 132 | Uygulanan Fiyat Snapshot'ı ve Sağlayıcı Kimliği | tam | ✅ | 2026-09-02 | F-175 | `2a8149ef:docs/arsiv/fazlar/132-UYGULANAN-FIYAT-SNAPSHOTU.md` |
| 133 | İş Kuyruğu Metrikleri | tam | ✅ | 2026-09-02 | F-178 | `2a8149ef:docs/arsiv/fazlar/133-IS-KUYRUGU-METRIKLERI.md` |
| 134 | Sınırlı Yapısal Yanıt Onarımı | tam | ✅ | 2026-09-02 | F-177 | `2a8149ef:docs/arsiv/fazlar/134-SINIRLI-YANIT-ONARIMI.md` |
| 135 | Üretilen Şemanın Nesne Grafı | tam | ✅ | 2026-09-02 | F-176 | `2a8149ef:docs/arsiv/fazlar/135-URETILEN-SEMANIN-NESNE-GRAFI.md` |
| 136 | Paket Kimliğinin Tekilliği | tam | ✅ | 2026-09-03 | F-182 | `2a8149ef:docs/arsiv/fazlar/136-PAKET-KIMLIGININ-TEKILLIGI.md` |
| 137 | İş Türünün Açık Anahtarı | tam | ✅ | 2026-09-03 | F-183 | `2a8149ef:docs/arsiv/fazlar/137-IS-TURUNUN-ACIK-ANAHTARI.md` |
| 138 | Ses Tanımının Sağlayıcı Üstverisi | tam | ✅ | 2026-09-03 | F-184 | `2a8149ef:docs/arsiv/fazlar/138-SES-TANIMININ-SAGLAYICI-USTVERISI.md` |
| 139 | Çalıştırma ve Oturum Yetkilendirmesi | tam | ✅ | 2026-09-03 | F-185 | `2a8149ef:docs/arsiv/fazlar/139-CALISTIRMA-VE-OTURUM-YETKILENDIRMESI.md` |
| 140 | İçerik Guard'ının Kaynağı | tam | ✅ | 2026-09-04 | F-186 | `2a8149ef:docs/arsiv/fazlar/140-ICERIK-GUARDININ-KAYNAGI.md` |
| 141 | Genişletilebilir Çalıştırma Olayı | tam | ✅ | 2026-09-04 | F-187 | `2a8149ef:docs/arsiv/fazlar/141-GENISLETILEBILIR-CALISTIRMA-OLAYI.md` |
| 142 | Onay İsteğinin Sunumu | tam | ✅ | 2026-09-04 | F-188 | `2a8149ef:docs/arsiv/fazlar/142-ONAY-ISTEGININ-SUNUMU.md` |
| 143 | Tool Argümanının Sözleşme Testleri | tam | ✅ | 2026-09-04 | F-189 | `2a8149ef:docs/arsiv/fazlar/143-TOOL-ARGUMANININ-SOZLESME-TESTLERI.md` |
| 144 | Alt-Agent Bekleme Sınırı | tam | ✅ | 2026-09-05 | F-191 | `2a8149ef:docs/arsiv/fazlar/144-ALT-AGENT-BEKLEME-SINIRI.md` |
| 145 | Kayıtlı Olay Akışının Çerçeve Sözleşmesi | tam | ✅ | 2026-09-05 | F-193 | `2a8149ef:docs/arsiv/fazlar/145-OLAY-AKISININ-CERCEVE-SOZLESMESI.md` |
| 146 | Çalıştırmaya Bağlı Kota Eşiği | tam | ✅ | 2026-09-05 | F-194 | `2a8149ef:docs/arsiv/fazlar/146-CALISTIRMAYA-BAGLI-KOTA-ESIGI.md` |
| 147 | Yetkilendirme Kapısının Kaynak Kapsamı | tam | ✅ | 2026-09-05 | F-195 | `2a8149ef:docs/arsiv/fazlar/147-YETKI-KAPISININ-KAYNAK-KAPSAMI.md` |
| 148 | Oturum Sahipliğinin Kalıcılığı | tam | ✅ | 2026-09-06 | F-196 | `2a8149ef:docs/arsiv/fazlar/148-OTURUM-SAHIPLIGININ-KALICILIGI.md` |
| 149 | Sahipsiz Oturumun Katı Reddi | tam | ✅ | 2026-09-06 | F-201 | `2a8149ef:docs/arsiv/fazlar/149-SAHIPSIZ-OTURUMUN-KATI-REDDI.md` |
| 150 | Zorunlu Binding Profili | tam | ✅ | 2026-09-06 | F-202 | `2a8149ef:docs/arsiv/fazlar/150-ZORUNLU-BINDING-PROFILI.md` |
| 151 | Harness'in Döngü Yeteneği | tam | ✅ | 2026-09-07 | F-192 | `2a8149ef:docs/arsiv/fazlar/151-HARNESSIN-DONGU-YETENEGI.md` |
| 152 | Skorun Adı ve Şekli | tam | ✅ | 2026-09-07 | F-208 | `2a8149ef:docs/arsiv/fazlar/152-SKORUN-ADI-VE-SEKLI.md` |
| 153 | Eval Koşumları Arasında Regresyon Farkı | tam | ✅ | 2026-09-07 | F-207 | `2a8149ef:docs/arsiv/fazlar/153-EVAL-KOSUMLARI-ARASINDA-REGRESYON-FARKI.md` |
| 154 | Skor Trendinin Kalıcı Sorgusu | tam | ✅ | 2026-09-07 | F-209 | `2a8149ef:docs/arsiv/fazlar/154-SKOR-TRENDININ-KALICI-SORGUSU.md` |
| 155 | Kalibre Edilmiş Evaluator Kataloğu | tam | ✅ | 2026-09-08 | F-210 | `2a8149ef:docs/arsiv/fazlar/155-KALIBRE-EDILMIS-EVALUATOR-KATALOGU.md` |
| 156 | Durum Ön Kontrolü ve Upgrade Penceresi | tam | ✅ | 2026-09-08 | F-216 | `2a8149ef:docs/arsiv/fazlar/156-DURUM-ON-KONTROLU-VE-UPGRADE-PENCERESI.md` |
| 157 | Sınırlı Yük ve İki Process Arıza Kanıtı | tam | ✅ | 2026-09-08 | F-217 | `2a8149ef:docs/arsiv/fazlar/157-SINIRLI-YUK-VE-IKI-PROCESS-ARIZA-KANITI.md` |
| 158 | Davranış İddialarının Kapısı | tam | ✅ | 2026-09-08 | F-171 | `2a8149ef:docs/arsiv/fazlar/158-DAVRANIS-IDDIALARININ-KAPISI.md` |
| 159 | Tipli İstemcide Akışlı OpenAI Çağrısı | tam | ✅ | 2026-09-08 | F-198 | `2a8149ef:docs/arsiv/fazlar/159-TIPLI-ISTEMCIDE-AKISLI-OPENAI-CAGRISI.md` |
| 160 | Lisans Modeli ve Paket Metaverisi | tam | ✅ | 2026-09-08 | — | `2a8149ef:docs/arsiv/fazlar/160-LISANS-MODELI-VE-PAKET-METAVERISI.md` |
| 161 | GPT-Live Sideband Denetimi | tam | ✅ | 2026-09-11 | — | `2a8149ef:docs/arsiv/fazlar/161-GPT-LIVE-SIDEBAND-DENETIMI.md` |
| 162 | Tracon Yeniden Adlandırma | tam | ✅ | 2026-09-12 | — | `2a8149ef:docs/arsiv/fazlar/162-TRACON-YENIDEN-ADLANDIRMA.md` |
| 163 | Marka ve Dokümantasyon Deneyimi | tam | ✅ | 2026-09-12 | F-221 · F-222 | `2a8149ef:docs/arsiv/fazlar/163-MARKA-VE-DOKUMANTASYON.md` |
| 164 | Console'un Enstrüman Katmanı | tam | ✅ | 2026-09-12 | F-223 | `2a8149ef:docs/arsiv/fazlar/164-CONSOLE-ENSTRUMAN-KATMANI.md` |
| 165 | Konsolun Kalan Ekranları | tam | ✅ | 2026-09-12 | F-223 | `2a8149ef:docs/arsiv/fazlar/165-KONSOLUN-KALAN-EKRANLARI.md` |
| 166 | HTTP Kapasite Ölçümü | tam | ✅ | 2026-09-14 | F-225 | `2a8149ef:docs/arsiv/fazlar/166-HTTP-KAPASITE-OLCUMU.md` |
| 167 | Agent Zorlama Katmanı | tam | ✅ | 2026-09-13 | F-227 | `2a8149ef:docs/arsiv/fazlar/167-AGENT-ZORLAMA-KATMANI.md` |
| 168 | Kurtarma Rampası Kataloğu | tam | ✅ | 2026-09-13 | F-228 | `2a8149ef:docs/arsiv/fazlar/168-KURTARMA-RAMPASI-KATALOGU.md` |
| 169 | Faz Planı Sözleşmesi: Süreç Ölçümü ve Triyaj | tam | ✅ | 2026-09-13 | F-229 | `2a8149ef:docs/arsiv/fazlar/169-FAZ-PLANI-SOZLESMESI.md` |
| 170 | Production Profil Kapısı | tam | ✅ | 2026-09-14 | F-231 | `2a8149ef:docs/arsiv/fazlar/170-PRODUCTION-PROFIL-KAPISI.md` |
| 171 | Denetim İzi Yazma Politikası | tam | ✅ | 2026-09-15 | F-234 | `2a8149ef:docs/arsiv/fazlar/171-DENETIM-IZI-YAZMA-POLITIKASI.md` |
| 172 | Tehdit Modeli | tam | ✅ | 2026-09-15 | F-235 | `2a8149ef:docs/arsiv/fazlar/172-TEHDIT-MODELI.md` |
| 173 | Çalıştırma Kaydının Kaybı Ölçülür | tam | ✅ | 2026-09-15 | — | `2a8149ef:docs/arsiv/fazlar/173-CALISTIRMA-KAYDI-GORUNURLUGU.md` |
| 174 | Kapasite Damgası Kapısı | tam | ✅ | 2026-09-15 | F-239 | `2a8149ef:docs/arsiv/fazlar/174-KAPASITE-DAMGASI-KAPISI.md` |
| 175 | Geri Alınamaz Karar Doğrulaması | tam | ✅ | 2026-09-16 | F-224 | `2a8149ef:docs/arsiv/fazlar/175-GERI-ALINAMAZ-KARAR-DOGRULAMASI.md` |
| 176 | Evaluator Sürüm Damgası | tam | ✅ | 2026-09-16 | F-218 | `2a8149ef:docs/arsiv/fazlar/176-EVALUATOR-SURUM-DAMGASI.md` |
| 177 | Store İptal Sözleşmesi | tam | ✅ | 2026-09-16 | F-213 | `2a8149ef:docs/arsiv/fazlar/177-STORE-IPTAL-SOZLESMESI.md` |
| 178 | Tüketici Kapı Skill'i | tam | ✅ | 2026-09-16 | F-232 | `2a8149ef:docs/arsiv/fazlar/178-TUKETICI-KAPI-SKILLI.md` |
| 179 | Kiracı Kimliğinin Normalleştirilmesi | tam | ✅ | 2026-09-19 | — | `2a8149ef:docs/arsiv/fazlar/179-KIRACI-KIMLIGI-NORMALLESTIRME.md` |
| 180 | Manuel Set Devir Şablonu | tam | ✅ | 2026-09-22 | F-165 | `2a8149ef:docs/arsiv/fazlar/180-MANUEL-SET-DEVIR-SABLONU.md` |
| 181 | Provider Ortak Katmanı | tam | ✅ | 2026-09-22 | F-258 | `2a8149ef:docs/arsiv/fazlar/181-PROVIDER-ORTAK-KATMANI.md` |
| 182 | Public API Yüzey Daraltma | tam | ✅ | 2026-09-22 | F-259 | `2a8149ef:docs/arsiv/fazlar/182-PUBLIC-API-YUZEY-DARALTMA.md` |
| 183 | Çoklu TFM Test Matrisi | tam | ✅ | 2026-09-23 | F-260 | `2a8149ef:docs/arsiv/fazlar/183-COKLU-TFM-TEST-MATRISI.md` |
| 184 | Test Bekleme ve E2E Yapısı | tam | ✅ | 2026-09-23 | F-261 | `2a8149ef:docs/arsiv/fazlar/184-TEST-BEKLEME-VE-E2E-YAPISI.md` |
| 185 | Kardeş Paket Sürüm Sabitleme ve Karışık Graf Koruması | tam | ✅ | 2026-09-24 | F-265 | `2a8149ef:docs/arsiv/fazlar/185-KARDES-PAKET-SURUM-SABITLEME.md` |
| 186 | Script İzninin İçeriğe Bağlanması ve Tehdit Modeli Düzeltmesi | tam | ✅ | 2026-09-24 | F-269 | `2a8149ef:docs/arsiv/fazlar/186-SCRIPT-IZNI-ICERIK-PINI.md` |
| 187 | Yayınlanmış Sürüme Karşı Kırıcı Değişiklik Kapısı | tam | ✅ | 2026-09-24 | F-270 | `2a8149ef:docs/arsiv/fazlar/187-KIRICI-DEGISIKLIK-KAPISI.md` |
| 188 | DI ile Kurulan Servis Tiplerinde Kurucu Daraltması | tam | ✅ | 2026-09-24 | F-271 | `2a8149ef:docs/arsiv/fazlar/188-DI-KURUCU-DARALTMA.md` |
| 189 | Tüketici Yüzeyi: TraconToolRegistration ve ITraconBuilder | tam | ✅ | 2026-09-24 | F-271 | `2a8149ef:docs/arsiv/fazlar/189-TUKETICI-YUZEYI-VE-BUILDER.md` |
| 190 | Kimlik Taşıyan Başlıklar İçin Yapılandırma Anahtarı Referansı | tam | ✅ | 2026-09-24 | F-272 | `2a8149ef:docs/arsiv/fazlar/190-KIMLIK-BASLIKLARI-ANAHTAR-REFERANSI.md` |
| 191 | Tek Derleme Zinciri | tam | ✅ | 2026-09-24 | F-273 | `2a8149ef:docs/arsiv/fazlar/191-TEK-DERLEME-ZINCIRI.md` |
| 192 | Döngü Tükenme Politikası | tam | ✅ | 2026-10-11 | F-293 | `2a8149ef:docs/arsiv/fazlar/192-DONGU-TUKENME-POLITIKASI.md` |
| 194 | Workflow ve Kuyruk Yollarında Parametre Değerleri | tam | ✅ | 2026-10-12 | F-294 | `2a8149ef:docs/arsiv/fazlar/194-WORKFLOW-VE-KUYRUK-PARAMETRELERI.md` |
| 195 | Workflow Düğüm Düzenleme ve Fonksiyon Düğümü Ayarı | tam | ✅ | 2026-10-12 | F-292 · F-295 | `2a8149ef:docs/arsiv/fazlar/195-WORKFLOW-DUGUM-DUZENLEME-VE-AYAR.md` |
| 196 | Uzun Adımların Süre Sınırı ve İptali | tam | ✅ | 2026-10-13 | F-303 · F-323 · F-405 | `2a8149ef:docs/arsiv/fazlar/196-UZUN-ADIM-SURE-VE-IPTAL.md` |
| 197 | Dayanıklı Workflow Yürütmesi: Bağlantısız Run ve Otomatik Sürdürme | tam | ✅ | 2026-10-13 | F-318 · F-321 | `2a8149ef:docs/arsiv/fazlar/197-DAYANIKLI-WORKFLOW-YURUTMESI.md` |
| 198 | Adlı Konuşma Sağlayıcısı ve Kiracı Bağlaması | tam | ✅ | 2026-10-13 | F-298 | `2a8149ef:docs/arsiv/fazlar/198-ADLI-KONUSMA-SAGLAYICISI.md` |
| 199 | Fonksiyon Düğümünün Token Dışı Maliyeti | tam | ✅ | 2026-10-14 | F-299 | `2a8149ef:docs/arsiv/fazlar/199-FONKSIYON-DUGUMU-MALIYETI.md` |
| 200 | Workflow Sürüm Geçmişi ve Sürüm Sabitleme | tam | ✅ | 2026-10-14 | F-300 | `2a8149ef:docs/arsiv/fazlar/200-WORKFLOW-SURUM-GECMISI-VE-SABITLEME.md` |
| 201 | Konsolda Loop Düzenleme ve DI'lı Ölçüt Fabrikası | tam | ✅ | 2026-10-13 | F-302 · F-301 | `2a8149ef:docs/arsiv/fazlar/201-LOOP-DUZENLEME-VE-OLCUT-FABRIKASI.md` |
| 202 | Skill Script Yürütmesini Harici Sandbox'a Yönlendiren Seam | tam | ✅ | 2026-10-13 | F-345 | `2a8149ef:docs/arsiv/fazlar/202-SKILL-SCRIPT-YURUTUCU-SEAM.md` |
| 203 | Çok Konuşmacılı Diyalog Sentezi ve Sağlayıcı İstek Ayarları | tam | ✅ | 2026-10-13 | F-296 | `2a8149ef:docs/arsiv/fazlar/203-COK-KONUSMACILI-DIYALOG-SENTEZI.md` |
| 204 | Kesilen Model Çıktısı ve Anthropic Güncel İstek Ayarları | tam | ✅ | 2026-10-13 | F-320 · F-346 | `2a8149ef:docs/arsiv/fazlar/204-KESILEN-CIKTI-VE-ANTHROPIC-AYARLARI.md` |
| 205 | Kök Kapandıktan Sonra Biten Harcamanın Kotası | tam | ✅ | 2026-10-14 | F-418 · F-482 | `2a8149ef:docs/arsiv/fazlar/205-GEC-BITEN-HARCAMA-KOTASI.md` |
| 206 | Yapılandırma Bölümlerinin Bağlama Kanıtı | tam | ✅ | 2026-10-14 | F-247 | `2a8149ef:docs/arsiv/fazlar/206-YAPILANDIRMA-BAGLAMA-KANITI.md` |
| 207 | Akışta Kesilen Çıktı Bildirimi | tam | ✅ | 2026-10-14 | F-427 | `2a8149ef:docs/arsiv/fazlar/207-AKISTA-KESILEN-CIKTI-BILDIRIMI.md` |
| 208 | Workflow Karar Sözleşmesi: Tek Yanıt, Karar Denetimi ve Kaynak Bağı | tam | ✅ | 2026-10-14 | F-319 | `2a8149ef:docs/arsiv/fazlar/208-WORKFLOW-KARAR-SOZLESMESI.md` |
| 209 | Workflow Gelen Kutusu ve İstek Süresi | tam | ✅ | 2026-10-14 | F-319 | `2a8149ef:docs/arsiv/fazlar/209-WORKFLOW-GELEN-KUTUSU-VE-SURE.md` |
| 210 | Bütçe Tükenince Fonksiyon Düğümü Başlamaz | tam | ✅ | 2026-10-14 | F-407 | `2a8149ef:docs/arsiv/fazlar/210-BUTCE-DUGUM-KAPISI.md` |
| 211 | Kod Grafında Düğüm Yetenekleri ve Revizyon Döngüsü | tam | ✅ | 2026-10-15 | F-537 · F-538 · F-539 | `2a8149ef:docs/arsiv/fazlar/211-KOD-GRAFI-DUGUM-YETENEKLERI.md` |
| 212 | HTTP DTO'larını Daralt: Yalnız OpenAPI İçin Public Olan 81 Tip | tam | ✅ | 2026-10-16 | F-554 | `2a8149ef:docs/arsiv/fazlar/212-HTTP-DTO-DARALTMA.md` |
| 213 | Tesisat Arayüzleri ve Üye Hijyeni | tam | ✅ | 2026-10-16 | F-555 · F-556 | `2a8149ef:docs/arsiv/fazlar/213-TESISAT-ARAYUZLERI-VE-UYE-HIJYENI.md` |
| 214 | Çalışma Anı Bağımlılıkları ve MAF 1.25 | tam | ✅ | 2026-10-16 | F-557 | `2a8149ef:docs/arsiv/fazlar/214-CALISMA-ANI-BAGIMLILIKLARI-VE-MAF-125.md` |
| 215 | Test ve Derleme Araçları | tam | ✅ | 2026-10-16 | F-557 · F-568 | `2a8149ef:docs/arsiv/fazlar/215-TEST-VE-DERLEME-ARACLARI.md` |
| 216 | npm Bağımlılıkları: Arayüz, TypeScript İstemcisi ve Site | tam | ✅ | 2026-10-16 | F-557 | `2a8149ef:docs/arsiv/fazlar/216-NPM-BAGIMLILIKLARI.md` |
| 217 | Sağlayıcı Modernleşmesi ve AOT Vaadinin Kanıtı | tam | ✅ | 2026-10-17 | F-558 | `2a8149ef:docs/arsiv/fazlar/217-SAGLAYICI-MODERNLESME-VE-AOT-VAADI.md` |
| 218 | `AuthorizationConfigurationKey` Alanını Kaldırma | tam | ✅ | 2026-10-17 | F-290 | `2a8149ef:docs/arsiv/fazlar/218-YETKI-ANAHTARI-ALANINI-KALDIRMA.md` |
| 219 | Tek Skill Zinciri (`AgentSkillsProviderBuilder`) | tam | ✅ | 2026-10-19 | F-578 | `c3b454d0:docs/219-TEK-SKILL-ZINCIRI.md` |
| 220 | MEAI Ses İstemcisi Köprüsü ve OpenAI Sesi | tam | ✅ | 2026-10-19 | F-579 | `41295388:docs/220-MEAI-SES-KOPRUSU.md` |
| 221 | MAF Kaçış Kapısı Rehberi ve Fabrika Agent Tanılaması | hafif | ✅ | 2026-10-19 | F-587 | `cee22aca:docs/221-MAF-KACIS-KAPISI-REHBERI.md` |
| 222 | Adlı Agent Uzantıları (MAF Seçenek Kancası) | tam | ✅ | 2026-10-19 | F-588 | `b55ede4e:docs/222-ADLI-AGENT-UZANTILARI.md` |
| 223 | Tanınmayan MAF İçeriği ve Workflow Olayının Kaydı | tam | ✅ | 2026-10-20 | F-589 | `e1055f5d:docs/223-TANINMAYAN-ICERIK-KAYDI.md` |
| 224 | MAF Kapsama Ölçüm Aracı | hafif | ✅ | 2026-10-20 | F-596 | `941da3b0:docs/224-MAF-KAPSAMA-OLCUM-ARACI.md` |
| 225 | Model Örneklemesi ve Tool Döngüsü Tanımda | tam | ✅ | 2026-10-20 | F-598 | `44866805:docs/225-MODEL-ORNEKLEME-VE-TOOL-DONGUSU.md` |
| 226 | Adlı Hosted Tool'lar: Kayıt, Yüzey Doğrulaması, Ücret | tam | ✅ | 2026-10-21 | F-603 | `18f8d6a2:docs/226-ADLI-HOSTED-TOOLLAR.md` |
| 227 | Eval Vakasında Beklenen Tool Çağrısı ve Argümanı | tam | ✅ | 2026-10-21 | F-605 | `11db7056:docs/227-EVAL-BEKLENEN-TOOL-CAGRISI.md` |
| 228 | Alt Agent Tek Çağrı Modu | hafif | ✅ | 2026-10-20 | F-599 | `d26a33ca:docs/228-ALT-AGENT-TOOL-MODU.md` |
| 229 | A2A Çok Tur ve Çağıran İzolasyonu | tam | ✅ | 2026-10-21 | F-614 | `d76bbfa8:docs/229-A2A-COK-TUR.md` |
| 230 | Workflow Adımında Tool Onayı | tam | ✅ | 2026-10-21 | F-615 | `a176a386:docs/230-WORKFLOW-ADIMINDA-TOOL-ONAYI.md` |
| 231 | Skill Okuma Onayı Tanımda ve Dış Yüzey Kapısı | tam | ✅ | 2026-10-21 | F-626 | `73a6f249:docs/231-SKILL-ONAYI-VE-DIS-YUZEY.md` |
| 232 | Adlı Model Halkası Yuvası | tam | ✅ | 2026-10-22 | F-594 | `487c811d:docs/232-ADLI-MODEL-HALKASI-YUVASI.md` |
| 233 | Konuşma Hafızası | tam | ✅ | 2026-10-22 | F-627 | `0bc5fb98:docs/233-KONUSMA-HAFIZASI.md` |
| 234 | Oturum Çalışma Alanı (Dosya Erişimi) | tam | ✅ | 2026-10-22 | F-628 | `25b72146:docs/234-OTURUM-CALISMA-ALANI.md` |
| 235 | MCP Elicitation: Askıya Alınan Tool Çağrısı | tam | ✅ | 2026-10-23 | F-617 | `e7cb8372:docs/235-MCP-ELICITATION.md` |

## Süreç Ölçümü

Eşik Faz 167'dir (K-767); öncesi ölçülmedi ve doldurulmaz. Kapı
satırın VARLIĞINI denetler, doğruluğunu değil (K-766). Açıklama fazın tam metnindedir.

| Faz | Plan revizyonu sayısı | Düzeltme turu sayısı | 🔴 bulgu: gerçek / gürültü / araştırılacak | Fazın ürettiği regresyon | Faz kapandıktan sonra bulunan kusur |
|---|---|---|---|---|---|
| 167 | 4 | 0 kod turu | 0 / 0 / 0 | 0. Denetçi iki kez ağaca/sürece müdahale etti | ölçülmedi |
| 168 | 0 | 2 | 2 / 0 / 0 | 0 | ölçülmedi |
| 169 | 5 | 4 | 1 / 0 / 0 | 0 | ölçülmedi |
| 170 | 0 | 2 | 2 / 0 / 0 | 0 | — |
| 171 | 0 | 9 | 1 / 0 / 0 | 0 | — |
| 172 | 0 | 1 | 0 / 0 / 0 | 0 | ölçülmedi |
| 173 | 0 | 9 | 2 / 0 / 0 | 0 | — |
| 174 | 5 | 5 | 7 / 0 / 0 | 0 | (kapanışta boş) |
| 175 | 0 | 3 | faz-denetim bölümüne bakınız | 1 | — |
| 176 | 0 | 4 | faz-denetim bölümüne bak | 0 | — |
| 177 | 0 | 4 | 2 / 0 / 0 | 0 | — |
| 178 | 0 | 3 | 2 / 0 / 0 + 1 bayat | 1 | — |
| 179 | 0 | 2 | 2 / 0 / 0 | 0 ölçülen. Kapılar üç kez kızardı ve üçü de fazın kendi eksiğiydi | 1 |
| 180 | 0 | 1 | 1 / 0 / 0 | 0 | — |
| 181 | 0 | 1 | 0 / 0 / 0 | 0 | — |
| 182 | 0 | 1 | 0 / 0 / 0 | 0 | 0 |
| 183 | 0 | 3 | 2 / 0 / 0 | 2 | ölçülmedi |
| 184 | 0 | 3 | 1 / 0 / 0 | 0 | ölçülmedi |
| 185 | 0 | 5 | 0 / 0 / 0 | 0 | ölçülmedi |
| 186 | 0 | 2 | 2 / 0 / 0 | 2 | ölçülmedi |
| 187 | 0 | 4 | 0 / 0 / 0 | 0 | ölçülmedi |
| 188 | 0 | 1 | 0 / 0 / 0 | 1 | ölçülmedi |
| 189 | 0 | 5 | 0 / 0 / 0 | 0 | ölçülmedi |
| 190 | 1 | 5 | 1 / 0 / 0 | 1 | ölçülmedi |
| 191 | 0 | 3 | 0 / 1 / 0 | 0 | ölçülmedi |
| 192 | 0 | 4 | 1 / 0 / 0 | 0 | 0 |
| 194 | 0 | 6 | 1 / 0 / 0 | 2 | 0 |
| 195 | 1 | 12 + kapanış kapıları | 1 / 1 / 0 | 2 | 0 |
| 196 | 2 | 1 | 0 / 0 / 0 | 1 | ölçülmedi |
| 197 | 1 | 12 | 1 / 0 / 0 | 3 | ölçülmedi |
| 198 | 1 | 3 | 0 / 0 / 0 | 1 | ölçülmedi |
| 199 | 0 | 2 | Denetçi | Davranış | Henüz ölçülmedi |
| 200 | 0 | 3 | 0 / 0 / 0 | 0 davranışta. Kapı turunda 2 bayat sayım | ölçülmedi |
| 201 | 0 | 10 | 0 / 0 / 0 | 0 | 0 |
| 202 | 1 | 1 | 0 / 0 / 0 | 1 | 0 |
| 203 | 1 | 1 | 0 / 0 / 0 | Merge'e giden | 0 |
| 204 | 0 | 6 | 0 / 0 / 0 | 0 | ölçülmedi |
| 205 | 0 | 9 | 1 / 0 / 0 | 0 | ölçülmedi |
| 206 | 0 | 3 | 0 / 0 / 0 | 0 | ölçülmedi |
| 207 | 0 | 3 | 0 / 0 / 0 | 0 | ölçülmedi |
| 208 | 2 | 2 | 2 / 0 / 0 | 0 | 0 |
| 209 | 0 | 1 | 0 / 0 / 0 | 0 | 0 |
| 210 | 0 | 1 | 0 / 0 / 0 | 0 | 0 |
| 211 | 0 | 3 | bkz. Denetim Bulguları | 0 | ölçülmedi |
| 212 | 0 | 4 | 0 / 0 / 0 | 1 | ölçülmedi |
| 213 | 0 | 5 | 1 / 0 / 0 | 0 commit edilmiş | ölçülmedi |
| 214 | 0 | 4 kapı kırmızısı turu | 0 / 0 / 0 | 0 bilinen | ölçülmedi |
| 215 | 0 | 2 | 1 / 1 / 0 | 1 | 0 |
| 216 | 0 | 1 | 0 / 0 / 0 | 0 | 0 |
| 217 | 1 | 3 | 2 / 0 / 0 | 2 | ölçülmedi |
| 218 | 0 | 3 | 0 / 0 / 0 | 2 | ölçülmedi |
| 219 | 0 | 2 | 0 / 0 / 0 | 0 commit edilmiş | ölçülmedi |
| 220 | 16 sapma | 2 | 0 / 0 / 0 | 0 bilinen | ölçülmedi |
| 221 | 8 | 1 | 2 / 0 / 0 | 0 | ölçülmedi |
| 222 | 0 | 2 | 1 / 0 / 1 | 1 | 6 |
| 223 | 9 | 1 | 0 / 1 / 0 | 0 | ölçülmedi |
| 224 | 1 | 1 | 0 / 0 / 0 | 0 | ölçülmedi |
| 225 | 0 | 3 | 2 / 0 / 0 | 1 | ölçülmedi |
| 226 | 12 | 3 denetim sonrası | 1 / 0 / 0 | 0. Hiçbir şey denetimden önce commit edilmedi | ölçülmedi |
| 227 | 0 | 3 | 0 / 0 / 0 | 1 | ölçülmedi |
| 228 | 8 sapma | 2 | 0 / 0 / 0 | 0 bilinen | ölçülmedi |
| 229 | 12 | 1 | 1 / 0 / 0 | 0 | ölçülmedi |
| 230 | 10 | 3 | 2 / 0 / 0 | 12 test | ölçülmedi |
| 231 | 0 yeniden yazım | 3 | 0 / 0 / 0 | 0 kullanıcıya ulaşan. Kapanıştan önce 3 ratchet düştü | ölçülmedi |
| 232 | 0 | 1 | 2 / 0 / 0 | 3 | ölçülmedi |
| 233 | 4 | 2 | 1 / 0 / 0 | 0 | ölçülmedi |
| 234 | 5 | 2 | 0 / 0 / 0 | 2 | ölçülmedi |
| 235 | 3 | 1 | 1 / 0 / 0 | 1 | ölçülmedi |

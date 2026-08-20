# Faz Yol Haritası

> **Üretilen dosya. Elle düzenleme.** Kaynak: her fazın kendi
> `docs/NN-*.md` dosyasındaki `> **Durum:**` satırı.
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

Bir fazın durumu yanlış görünüyorsa **o fazın dokümanını** düzelt;
bu dosyayı düzeltmek bir sonraki üretimde geri alınır.

## Fazlar (78 kalem)

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
| [24](arsiv/fazlar/24-SQLITE.md) | SQLite Desteği | Kod tamam · 205/205 sözleşme+diyalekt te |
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
| [60](60-PUBLIC-API-KAPISI.md) | Public API Kapısı | ✅ Tamamlandı |
| [61](61-ISTEMCI-TOOLLARI-VE-GOMULEBILIR-SOHBET.md) | İstemci Tool'ları ve Gömülebilir Sohbet | ✅ Tamamlandı |
| [62](62-MODEL-YEDEK-ZINCIRI-VE-ON-UCUS-DENETIMI.md) | Model Yedek Zinciri ve Ön Uçuş Denetimi | ✅ Tamamlandı |
| [63](63-ARGUMAN-DUZEYINDE-ONAY-POLITIKASI.md) | Argüman Düzeyinde Onay Politikası | ✅ Tamamlandı |
| [64](64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md) | Denetim Zinciri ve Veri Konusu Hakları | ✅ Tamamlandı |
| [65](65-KIRACI-SAGLAYICI-ANAHTARLARI.md) | Kiracı Sağlayıcı Anahtarları (BYOK) | ✅ Tamamlandı |
| [66](66-GELEN-TETIKLEYICILER.md) | Gelen Tetikleyiciler | ✅ Tamamlandı |
| [67](67-ISTEGE-BAGLI-MIGRATION-SETI.md) | İsteğe Bağlı Migration Seti (`pgvector` opt-in) | ✅ Tamamlandı |
| [68](68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md) | Çalıştırma Kimliği ve Token Kırılımı | ✅ Tamamlandı |
| [69](69-TOOL-YETKILENDIRMESI-VE-TIMEOUT.md) | Tool Yetkilendirmesi ve Yürütme Timeout'u | ✅ Tamamlandı |
| [70](70-CALISTIRMA-OLAYI-HEDEFI.md) | Çalıştırma Olayı Hedefi ve Düşünme Akışı | ✅ Tamamlandı |
| [71](71-WORKFLOW-KOD-DUGUMU.md) | Workflow Kod Düğümü | ✅ Tamamlandı |
| [72](72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) | Çok Dilli Talimat ve Zaman Damgalı Sentez | ✅ Tamamlandı |
| [73](73-TUKETICI-AGENT-DESTEGI.md) | Tüketici Agent Desteği | ✅ Tamamlandı |
| [74](74-YEREL-REFERANS-YUZEYI.md) | Yerel Referans Yüzeyi | ✅ Tamamlandı |
| [75](75-TUKETICI-DOKUMAN-DOGRULUGU.md) | Tüketici Dokümanının Doğruluğu | ✅ Tamamlandı |
| [76](76-DOKUMAN-KALITESI-VE-GORSEL-KIMLIK.md) | Doküman Kalitesi ve Görsel Kimlik | ✅ Tamamlandı |
| [77](77-GIDEN-AG-MUHAFIZI.md) | Giden Ağ Muhafızı | 📋 Planlandı |

Seçilmemiş adaylar: [`ADAYLAR.md`](ADAYLAR.md). Fazların hangi dalgada, hangi gerekçeyle sıralandığı (Faz 8–56, kapandı): [`arsiv/IKINCI-FAZ-YOL-HARITASI.md`](arsiv/IKINCI-FAZ-YOL-HARITASI.md) · [`arsiv/UCUNCU-FAZ-YOL-HARITASI.md`](arsiv/UCUNCU-FAZ-YOL-HARITASI.md).

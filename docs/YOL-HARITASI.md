# Faz Yol Haritası

> **Üretilen dosya. Elle düzenleme.** Kaynak: her fazın kendi
> `docs/NN-*.md` dosyasındaki `> **Durum:**` satırı.
> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`

Bir fazın durumu yanlış görünüyorsa **o fazın dokümanını** düzelt;
bu dosyayı düzeltmek bir sonraki üretimde geri alınır.

## Fazlar (74 kalem)

| Faz | Konu | Durum |
|-----|------|-------|
| [0](00-ALTYAPI.md) | Repository ve Build Altyapısı | ✅ Tamamlandı |
| [1](01-CEKIRDEK-SOYUTLAMALAR.md) | Çekirdek Soyutlamalar ve Runtime | ✅ Tamamlandı |
| [2](02-POSTGRESQL-KALICILIK.md) | PostgreSQL Kalıcılık Katmanı | ✅ Tamamlandı |
| [3](03-SAGLAYICI-VE-DERLEYICI.md) | Sağlayıcı Katmanı ve Agent Derleyici | ✅ Tamamlandı |
| [4](04-HTTP-API.md) | HTTP API Katmanı | ✅ Tamamlandı |
| [5](05-AGENTPRISM-UI.md) | AgentPrism.UI | ✅ Tamamlandı |
| [6](06-GOZLEMLENEBILIRLIK.md) | Gözlemlenebilirlik, Tool Onayı, MCP ve Çok Kiracılılık | ✅ Tamamlandı |
| [7](07-SAGLAMLASTIRMA-VE-YAYIN.md) | Sağlamlaştırma ve Yayın | ⏸ Beklemede |
| [8](08-SAGLAYICI-GENISLEMESI.md) | Sağlayıcı Genişlemesi ve Sağlık Denetimi | ✅ Tamamlandı |
| [9](09-YONETISIM-VE-DENETIM-IZI.md) | Yönetişim: Rol Tabanlı Yetkilendirme ve Denetim İzi | ✅ Tamamlandı |
| [10](10-AGENT-SKILLERI.md) | Agent Skill'leri (script'siz) | ✅ Tamamlandı |
| [11](11-SKILL-SCRIPT-CALISTIRMA.md) | Skill Script Çalıştırma | ✅ Tamamlandı |
| [12](12-AGENT-CAGRI-GRAFIGI.md) | Agent'ın Agent'ı Çağırması | ✅ Tamamlandı |
| [13](13-BAGLAM-SIKISTIRMA-VE-BELLEK.md) | Bağlam Sıkıştırma ve Bellek Sağlayıcıları | ✅ Tamamlandı |
| [14](14-COK-MODLULUK.md) | Çok Modluluk: Görsel, Ses ve Dosya Girdisi | ✅ Tamamlandı |
| [15](15-WORKFLOWS-YURUTME.md) | Workflows: Yürütme ve Kalıcılık | ✅ Tamamlandı |
| [16](16-WORKFLOWS-ARAYUZ.md) | Workflows: Graf, Arayüz ve Human-in-the-Loop | ✅ Tamamlandı |
| [17](17-TOPLU-VE-ZAMANLANMIS-CALISTIRMA.md) | Toplu ve Zamanlanmış Çalıştırma | ✅ Tamamlandı |
| [18](18-DEGERLENDIRME.md) | Değerlendirme (Eval) Altyapısı | ✅ Tamamlandı |
| [19](19-SURUM-KARSILASTIRMA-VE-AB.md) | Sürüm Karşılaştırma, Diff ve A/B | ✅ Tamamlandı |
| [20](20-MALIYET-VE-GOSTERGE-PANELI.md) | Maliyet Raporlaması ve Gösterge Paneli | ✅ Tamamlandı |
| [21](21-KOTA-VE-OLAY-YAYINI.md) | Hız Sınırı, Kota ve Olay Yayını | ✅ Tamamlandı |
| [22](22-MCP-DERINLESMESI.md) | MCP Derinleşmesi: Prompts, Resources ve OAuth | ✅ Tamamlandı |
| [23](23-SQL-SERVER.md) | SQL Server Desteği | ✅ Tamamlandı |
| [24](24-SQLITE.md) | SQLite Desteği | Kod tamam · 205/205 sözleşme+diyalekt te |
| [25](25-VERI-SAKLAMA-VE-ARSIVLEME.md) | Veri Saklama Politikası ve Arşivleme | ✅ Tamamlandı |
| [26](26-ANTHROPIC-VE-GEMINI.md) | Anthropic (Claude) ve Google Gemini Sağlayıcıları | ✅ Tamamlandı |
| [27](27-AZURE-FOUNDRY.md) | Azure OpenAI (ve ertelenen Azure AI Foundry) | ✅ Tamamlandı |
| [28](28-SES-TOOLLARI.md) | Ses Tool'ları (ElevenLabs) | ✅ Tamamlandı |
| [29](29-KONUSMA-KATMANI.md) | Konuşma Katmanı (Gerçek Zamanlı Ses) | ✅ Tamamlandı |
| [30](30-ARAYUZ-CILASI.md) | Arayüz Cilası: Yerelleştirme, Komut Paleti ve Kısayollar | ✅ Tamamlandı |
| [31](31-GERI-BILDIRIM-VE-PUANLAMA.md) | Geri Bildirim ve Puanlama | ✅ Tamamlandı |
| [32](32-CALISTIRMA-IPTALI.md) | Çalıştırma İptali | ✅ Tamamlandı |
| [33](33-SAGLIK-DENETIMI-VE-TESHIS.md) | Sağlık Denetimi ve Yapılandırma Teşhisi | ✅ Tamamlandı |
| [34](34-TANIM-DOGRULAMA-UCU.md) | Tanım Doğrulama Ucu | ✅ Tamamlandı |
| [35](35-MALIYET-VE-KOTA-METRIKLERI.md) | Maliyet ve Kota Metrikleri | ✅ Tamamlandı |
| [36](36-SAKLAMA-HACIM-SINIRI.md) | Saklama Hacim Sınırı (`MaxRows`) | ✅ Tamamlandı |
| [37](37-PROJE-SABLONU.md) | `dotnet new` Proje Şablonu | ✅ Tamamlandı |
| [38](38-YAPILANDIRILMIS-CIKTI.md) | Yapılandırılmış Çıktı (JSON Şeması) | ✅ Tamamlandı |
| [39](39-TEST-PAKETI.md) | `AgentPrism.Testing` Paketi | ✅ Tamamlandı |
| [40](40-OPENAPI-YAYINI.md) | OpenAPI Belgesinin Yayımlanması | ✅ Tamamlandı |
| [41](41-KIRACI-YALITIMININ-ZORLANMASI.md) | Kiracı Yalıtımının Zorlanması | ✅ Tamamlandı |
| [42](42-TEK-YURUTUCU-SECIMI.md) | Tek Yürütücü Seçimi (Çok Örnekli Koordinasyon) | ✅ Tamamlandı |
| [43](43-IDEMPOTENCY-KEY.md) | `Idempotency-Key` Desteği | ✅ Tamamlandı |
| [44](44-HATA-SINIFLANDIRMA.md) | Hata Sınıflandırma ve Arıza Kümeleme | ✅ Tamamlandı |
| [45](45-URETIMDEN-EVAL-KUMESI.md) | Üretimden Değerlendirme Veri Kümesi Toplama | ✅ Tamamlandı |
| [46](46-DAYANIKLI-CALISTIRMA.md) | Dayanıklı Çalıştırma (`202 Accepted`) | ✅ Tamamlandı |
| [47](47-YENIDEN-OYNATMA-VE-DALLANDIRMA.md) | Yeniden Oynatma ve Konuşma Dallandırma | ✅ Tamamlandı |
| [48](48-GUARDRAILS.md) | Guardrails ve İçerik Güvenliği Genişleme Noktası | ✅ Tamamlandı |
| [49](49-CEVRIMICI-DEGERLENDIRME.md) | Çevrimiçi Değerlendirme (üretim trafiğinde yargıç) | ✅ Tamamlandı |
| [50](50-DISA-ACILAN-AGENT-YUZEYI.md) | Dışa Açılan Agent Yüzeyi (MCP sunucusu ve A2A) | ✅ Tamamlandı |
| [51](51-VEKTOR-BELLEK-VE-RAG.md) | Vektör Bellek ve RAG (`pgvector`) | ✅ Tamamlandı |
| [52](52-KAYNAK-URETECI.md) | Tool Kaynak Üreteci ve Derleme Anı Doğrulama | ✅ Tamamlandı |
| [53](53-KIRACI-API-ANAHTARLARI.md) | Kiracı Bazlı API Anahtarları ve Kapsamlar | ✅ Tamamlandı |
| [54](54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md) | Öksüz Çalıştırma Uzlaştırması | ✅ Tamamlandı |
| [55](55-ASENKRON-ONAY-KUTUSU.md) | Asenkron Onay Kutusu | ✅ Tamamlandı |
| [56](56-KANARYA-YAYINI-VE-OTOMATIK-GERI-ALMA.md) | Kanarya Yayını ve Otomatik Geri Alma | ✅ Tamamlandı |
| [57](57-KOD-DILI-BIRLESTIRME.md) | Kod Dili Birleştirme (İngilizce) | ✅ Tamamlandı |
| [58](58-DOKUMAN-DUZENI.md) | Doküman Düzeni | ✅ Tamamlandı |
| [59](59-URUN-DOKUMANTASYONU.md) | Ürün Dokümantasyonu (Doküman Sitesi) | ✅ Tamamlandı |
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
| [71](71-WORKFLOW-KOD-DUGUMU.md) | Workflow Kod Düğümü | 📋 Planlandı |
| [72](72-COK-DILLI-TALIMAT-VE-ZAMAN-DAMGALI-SENTEZ.md) | Çok Dilli Talimat ve Zaman Damgalı Sentez | 📋 Planlandı |
| [73](73-TUKETICI-AGENT-DESTEGI.md) | Tüketici Agent Desteği | 📋 Planlandı |

Seçilmemiş adaylar: [`ADAYLAR.md`](ADAYLAR.md). Fazların hangi dalgada, hangi gerekçeyle sıralandığı (Faz 8–56, kapandı): [`arsiv/IKINCI-FAZ-YOL-HARITASI.md`](arsiv/IKINCI-FAZ-YOL-HARITASI.md) · [`arsiv/UCUNCU-FAZ-YOL-HARITASI.md`](arsiv/UCUNCU-FAZ-YOL-HARITASI.md).

# Faz 217 — Sağlayıcı Modernleşmesi ve AOT Vaadinin Kanıtı

> **Durum:** ✅ Tamamlandı (2026-10-17)
> **Plan onayı:** onaylandı — farukatasoy, 2026-10-17, "onaylıyorum". Açık Sorular 1–5 ayrıca yanıtlanmadı; uygulama planın önerisini izler (Açık Soru 1 canlı ölçümden sonra kapanır). Planlamada verilen kararlar (farukatasoy, 2026-10-17): Anthropic AOT vaadi **geri çekilir** · AOT vaadinin ölçütü **smoke + sıfır uyarı** · Azure **düz `OpenAI` istemcisi + `/openai/v1/`** · canlı doğrulama **yalnız Anthropic** (Azure canlı case'i 👤 bekler).
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-558**
> **Genellik:** Sağlayıcı paketini Native AOT ile yayımlayan **her** tüketici (Anthropic bugün ilk istekte çöker — ölçüldü) ve Azure OpenAI kullanan **her** tüketici (kararlı `Azure.AI.OpenAI` 2024-12'den beri sürüm almadı; 1.0 bu bağımlılığı dondurur). İş tüketici kodunda kalamaz: vaat ve bağımlılık paketin kendisindedir. İkinci kanıt: freeze yolu ölçüt 3 ([`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) §2) ve 2026-10-16 denetimi A-83.
> **Karşı görüş:** "Çalışan bir adaptörü yeniden yazmak risktir; Azure kullanıcısı v1 yolunu desteklemeyen bir kaynakta kalabilir." Cevap: değişiklik yalnız iç istemciyi değiştirir, public API aynı kalır. Ölçülen kırık (`AddDataSource`, K-211) ve 62 AOT uyarısı eski adaptördedir. Bedel (istek yolu değişir) sürüm notunda adıyla yazılır. 1.0'dan sonra aynı değişiklik daha pahalıdır.
> **Önkoşul:** [Faz 214](214-CALISMA-ANI-BAGIMLILIKLARI-VE-MAF-125.md) · [215](215-TEST-VE-DERLEME-ARACLARI.md) · [216](216-NPM-BAGIMLILIKLARI.md) — bağımlılıklar en yeni sürümde (kapandı)
> **Paketler:** `Tracon.Azure`, `Tracon.Anthropic`, `Tracon.Google`, `Tracon.Voice` (+ `Directory.Packages.props`, `scripts/release_extension_samples.py`, yeni örnek `samples/Tracon.Samples.ProviderAotSmoke`)
> **Yeni paket:** Yok — bir paket **kalkar** (`Azure.AI.OpenAI`) · **Migration:** Yok
> **Public API:** Büyümüyor. `wc -l src/Tracon.{Azure,Anthropic,Google,Voice}/PublicAPI.Shipped.txt` → toplam 4 satır (yalnız `#nullable enable` başlıkları); `AzureOpenAIProviderOptions` aynı kalır
> **Tüketici yüzeyi:** site: `reference/compatibility.md` (AOT sütunu, `:83-86`), `packages.md` (§ *Trimming and native AOT*, `:159-160`), `guides/model-providers.md` (§ *Azure OpenAI uses deployments*, `:298`), `reference/changelog` (üretilir)
> · sevk edilen: `src/Tracon.Azure/README.md` (`:55` istek yolu, `:109-119` SDK anlatısı), `src/Tracon.Anthropic/README.md` (`:39` AOT cümlesi, `:107-109` `ReasoningEffort` satırı — Açık Soru 1), `src/Tracon.Google/README.md` (`:64`), `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/06-SAGLAYICI-DIGER.md` (`MT-PROV-091…`) · `docs/manuel-test/19-COK-MODLULUK-VE-SES.md` (`MT-MM-141…`) · `docs/manuel-test/01-KURULUM-VE-PAKETLEME.md` (`MT-PKG-167…`)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 51f08f2c:docs/arsiv/fazlar/217-SAGLAYICI-MODERNLESME-VE-AOT-VAADI.md
> ```
>
> Damıtıldı 2026-10-17 · `scripts/dokuman-bakim.py faz-damit`

---

## Bitiş Ölçütleri (DoD)

- [x] `Directory.Packages.props` ve `src/` içinde `Azure.AI.OpenAI` yalnız tarihsel yorum olarak geçer (paket başvurusu 0) — ✅
- [x] `AzureOpenAIChatRequestWireTests`: `POST /openai/v1/chat/completions`, `Authorization: Bearer`, gövdede `model`; akışlı, Entra (kapsam + HTTPS reddi), uç biçimleri, iptal, görsel yolu yeşil — ✅ (Azure.UnitTests 157/157, üç çerçeve)
- [x] `ProviderAotSmoke` `kapi.py yayin --kuru --surum 1.0.0-preview.5.217` içinde (temiz worktree) yayımlandı ve koştu; sıfır IL uyarısı; `provider AOT smoke passed: openai azure-openai voice`; `✅ 6 exact-version packed sample, 2 Native AOT smoke ve net8.0 tüketici smoke` — ✅
- [x] Vaat kapısı: site "Yes" kümesi = smoke referansları ∪ gerekçeli muaflar; `release_extension_samples_test.py` 24 test, negatifler kırmızı; gerçek ağaç yeşil (`MT-PKG-168`) — ✅. Negatif kontrol 2: Anthropic smoke'a eklenince yayın 3.388 hatayla düştü
- [x] `compatibility.md`, `packages.md`, kök README, Anthropic/Google/Azure/Voice README'leri ve MIMARI §9 ölçülen sonucu söyler; Anthropic "No", Google "No" — ✅
- [x] `ElevenLabsOperationDriftTests` yeşil (3 test; Voice.UnitTests 198/198); beyanda bir alan adı değişince kırmızı (negatif kontrol koşuldu) — ✅
- [x] `elevenlabs-kayma.py` canlı belgeye karşı fark yok: `✅ 7 operasyon, 105 beyan canlı belgede geçerli (OpenAPI 3.1.0, 320 yol)`; negatif kontrol `voice_settings.stability_renamed` → çıkış 1 — ✅
- [x] Açık Soru 1 217.4 ölçümüyle kapandı (B); sonuç tablosu *Plandan Sapmalar* 7 ve `MT-PROV-092` — ✅
- [x] K-216, K-006, K-211, K-1017 notları; yeni K-1019 (AOT vaadi), K-1020 (Azure v1), K-1021 (Anthropic `ReasoningEffort`) — ✅
- [x] `CHANGELOG.md` `[Unreleased]`: Azure v1 (+ sağlık, görsel, Entra HTTPS), Anthropic thinking biçimi, Anthropic/Google AOT vaadi — ✅
- ➜ Anthropic SDK deposuna issue açılır 👤; bağlantı karara yazılır — **devredildi** (yapılmadı) (`YAYIN-HAZIRLIK.md` § 3.2)
- [x] Dört doğrulama kapısı sıfır uyarı verir: `kapi.py kapanis --taban ac5ab062` — build ✅ · tam test 23.472 (38 modül, 0 başarısız, 19 atlandı — sağlayıcı sözleşmelerinin "ayar yok" atlamaları) ✅ · pack ✅ · format ✅ · site (`npm run check`) ✅. Dördüncü koşum; önceki üçünün kırmızıları: shipped-doc kapısı (`K-211` XML'de), `llms-full.txt` bayatlığı, fonksiyonel sağlık testi (http→https), faz durumu/arşiv kuralı
- [x] `samples/Tracon.Api` ile gerçek `run` — Anthropic canlı (`claude-haiku-5-5`): sağlık `Healthy`; `claude-support` akışlı run `Completed`, `functionCall → functionResult → text` ("Order ORD-1001 has shipped…"); önbellekli uzun talimat agent'ı iki turda `cachedInputTokens` 24.321/24.332 — ✅ (Azure: kaynak yok, `MT-PROV-091` 👤)
- [x] `secret` taraması boş döndü (`kapi.py tarama` ✅); Anthropic anahtarı yalnız süreç ortam değişkeniyle verildi, hiçbir dosyada ve günlükte yok (`grep -rn "sk-ant-usr"` 0) — ✅
- [x] Manuel kabul case'leri eklendi (`MT-PKG-167/168`, `MT-PROV-091…093`, `MT-MM-141`); koşulanlar: 092 ✅ canlı, 093 ✅ örnek uygulama, `MT-MM-141` ✅ betik; 167/168 ➜ CI (yayın provası + betik testleri koştu); 091 ⏭ Azure kimliği yok — ✅
- [x] `faz-denetim` koşuldu; 2 🔴 bulundu, ikisi kapandı — ✅
- [x] `docs-site/` güncellendi; `npm run check` (build + içerik + bağlantı + SEO + ağırlık) temiz — ✅

### Doğrulama komutları

```bash
# Azure SDK'sı tamamen kalktı mı
grep -rn "Azure.AI.OpenAI" src Directory.Packages.props

# AOT smoke ve vaat kapısı
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.5

# ElevenLabs canlı kayma
python3 scripts/elevenlabs-kayma.py
```

---

## Plandan Sapmalar

1. **Entra yolu deneysel bir kurucuya bağlıdır** (planda yoktu). `OpenAIClient(AuthenticationPolicy, OpenAIClientOptions)` OpenAI 2.14'te `[Experimental("OPENAI001")]` işaretli; Microsoft'un v1 rehberinin yolu odur. Kullanıcıya soruldu, **gerekçeli pragma** seçildi (K-1020). Bastırma `AzureOpenAIChatClientFactory.CreateClient`'ta tek `return`'dedir; anahtar yolu kararlı kurucuyu kullanır. Risk: tüketici `OpenAI`'ı test edilen sürümün üstüne çekerse yalnız Entra yolu çalışma anında kırılabilir.
2. **`BearerTokenPolicy` düz HTTP'ye token göndermez** (planda yoktu). Sonuçları: (a) Entra wire testi loopback sunucuyla yazılamadı; `CreateClient`'a `transport` parametresi (test dikişi, `egressGuard`'ı ezer) eklendi ve test HTTPS adresli kayıt tutan `HttpMessageHandler` kullanır; (b) bu bir **güvenlik iyileştirmesi** olarak CHANGELOG'a yazıldı; (c) denetim 🔴2: sağlık denetimi aynı token'ı düz HTTP'ye gönderiyordu — düzeltildi (denetim "Unhealthy" döner, token istemez).
3. **Sağlık denetimi anahtar başlığı:** plan `GET /openai/v1/models`'i yazıyordu ve başlığı değiştirmiyordu. Denetim 🟡6: sohbet `Authorization: Bearer`, sağlık `api-key` gönderiyordu. Sağlık sohbetle **aynı başlığa** çekildi (`Bearer`); Açık Soru 5 (A) böylece iki çağrıda da uygulandı. Canlı doğrulama `MT-PROV-091` 👤.
4. **Uç birleştirme:** `V1Endpoint` `AbsoluteUri` yerine `GetLeftPart(Path)` kullanır (sorgu/parça taşıyan adres, denetim 🟢8) ve sağlık denetimine de uygulanır (denetim 🔴1: `…/openai/v1` adresi `…/openai/v1/openai/v1/models` oluyordu).
5. **ElevenLabs: "anlık görüntü" yerine "beyan".** Plan, canlı belgeden çıkarılan bir anlık görüntü ve `--yaz` bayrağı öngörüyordu. Uygulanan: Tracon'un bağımlılığını beyan eden `Fixtures/elevenlabs-operations.json` (yedi operasyon, sorgu parametreleri, alan yolları); birim testi beyanı **kodla** eşitler (istemci sürülür, DTO metadata'sı yürünür, kaynak `v1/…` sabitleri taranır); betik beyanı **canlı belgeyle** karşılaştırır, belge indirmez/yazmaz. Beyan alan **adlarını** tutar, tipleri değil (F-576). Betik macOS python.org kurulumunda `CERTIFICATE_VERIFY_FAILED` verdi; doğrulama kapatılmadı, `--dosya` + `curl` yolu eklendi.
6. **Google:** plan "geçmez (103 uyarı)" diyordu; ölçüldü: 102 farklı uyarı (99 `Newtonsoft.Json` ← `Google.Apis.Auth`, 2 `Google.GenAI`, 1 `Google.Apis`). Vaat geri çekildi (Açık Soru 3 = A). Akışlı Gemini yolu sahte sunucuda `Incomplete JSON segment` verdi; ölçüt zaten uyarıdan düştüğü için yorumlanmadı.
7. **Anthropic `ReasoningEffort` (Açık Soru 1 = B)** — canlı ölçüm (SDK 12.58.0, 2026-10-17; HTTP durumu):

   | Model | Bütçeli (`enabled`) | Uyarlanabilir `medium` | Uyarlanabilir `xhigh` |
   |---|---|---|---|
   | `claude-opus-5-5` · `sonnet-5-5` · `haiku-5-5` · `fable-5-1` · `sonnet-5` · `opus-5` | `400` | `200` | `200` |
   | `claude-haiku-4-5-20251001` | `200` | `400` | `400` |
   | `claude-sonnet-4-5` | `404` (kimlik yok) | `404` | `404` |

   🚨 İlk prob yanlıştı: `CreateChatClient(binding)` ham istemcidir ve `ReasoningEffort`'u `ChatOptions`'a koymaz; ilk tablo "bütçeli biçim kabul edilir" gösterdi çünkü ayar hiç gitmedi. İstek gövdesi yakalanınca (`max_tokens` 4096, `thinking` yok) fark edildi, `ChatOptions.Reasoning` ile yeniden ölçüldü. Sonuç: Faz 214'ün `Extended` sabiti `ReasoningEffort`'u her güncel modelde kullanılamaz kılıyordu (K-1021).
8. **AOT kapısı planın ötesine geçti:** plan yalnız "Yes diyen sağlayıcı smoke'ta mı" diyordu. Uygulama ayrıca tablodaki **her** "Yes" satırını ya `PROVIDER_AOT_PACKAGES` ya da gerekçeli `AOT_PROOF_ELSEWHERE`'de ister (denetim 🟡4b). `packages.md`/README düz yazı sayımı kapısız kaldı (F-575).
9. **Smoke kapsamı:** Anthropic ve Google dışında; Azure için yalnız API anahtarı yolu, Voice için TTS + ses listesi koşar. Entra, görsel, STT ve diyalog çağrılmaz (F-574); Azure ve Voice README'leri vaadi bu kapsamla yazar. Google rotası (kullanılmayan) örnekten silindi.
10. **Kapsam dışı iş aynı çalışma ağacında:** `docs/YAYIN-HAZIRLIK.md` sadeleştirmesi (838 → ~190 satır), `nuget-danismani` skill bölüm atıfları ve `docs/193-…` satırı bu fazın değil, oturumun ilk kullanıcı isteğidir (denetim 🟡7). Commit edilirken ayrı olmalıdır.
11. **Eklenen testler planda sayılmayanlar:** `V1Endpoint` kenar durumları, BYOK + egress guard (`AzureOpenAIModelProviderCredentialTests`, provider DI'dan gelir), görsel v1 wire testi, sağlık Bearer/HTTPS testleri. Silinenler: `Sovereign_cloud_audience_can_be_given_to_the_client` ve `A_failed_call_does_not_put_the_key_in_the_error` (iddia etmiyorlardı).
12. **Gerçek Anthropic anahtarı sohbette verildi.** Yalnız süreç ortam değişkeniyle kullanıldı, hiçbir dosyaya yazılmadı; kullanıcıya **döndürülmesi** (rotate) söylendi.

## Bu Fazda Verilen Kararlar

- **K-1019** — Sağlayıcı/ses paketi "Native AOT: Yes" vaadini yalnız tüketici yayınında kanıtlayarak verir; `Anthropic` ve `Google` vaadi geri çekildi *(kullanıcı kararı)*.
- **K-1020** — `Tracon.Azure` v1 ucuna düz `OpenAI` istemcisiyle gider; `Azure.AI.OpenAI` kalktı; Entra yalnız HTTPS ve deneysel kurucu tek çağrıda pragma'lı *(kullanıcı kararı)*.
- **K-1021** — Anthropic `ReasoningEffort` uyarlanabilir düşünmedir; K-1017'nin `Extended` sabiti kalktı.
- Kategori dışı (yalnız burada): ElevenLabs sözleşmesi bir **beyan** dosyasıdır ve kayma denetimi CI'da değil elle koşar (ağ bağımlılığı CI'ı sağlayıcı kesintisiyle kırardı); sağlık denetimi sohbetle aynı anahtar başlığını kullanır; `transport` parametresi yalnız test içindir.
- K-006, K-211, K-216, K-1017 satırlarına not düşüldü (yeniden açılma sütunu).

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 (onaydan sonra plan yeniden yazılmadı; sapmalar *Plandan Sapmalar*'da) |
| Düzeltme turu sayısı | 3 (iç döngü: shipped-doc kapısı; denetim sonrası kod turu; kapı yeniden koşumu) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 2 / 0 / 0 |
| Fazın ürettiği regresyon | 2, ikisi de faz içinde yakalandı ve kapandı: sağlık denetimi çift `/openai/v1` ve sağlık denetimi Entra token'ını düz HTTP'ye gönderiyordu (denetim 🔴1, 🔴2) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (kapanış anı) |

## Denetim Bulguları

Bağımsız denetçi (`faz-denetcisi`, salt-okunur) iki 🔴, beş 🟡 ve altı 🟢 üretti.

| # | Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | Sağlık denetimi `…/openai/v1` adresini ikiye katlıyor | 🔴 | gerçek | düzeltildi (`BuildModelsEndpoint` `V1Endpoint` kullanır; 2 yeni test) |
| 2 | Sağlık denetimi Entra token'ını düz HTTP'ye gönderiyor; "yalnız HTTPS" iddiası yalnız sohbet için doğruydu | 🔴 | gerçek | düzeltildi (Unhealthy, token istenmez; 3 yeni test; eski test bu davranışı sabitliyordu) |
| 3 | BYOK + v1 + egress guard ve görsel yolu testsiz | 🟡 | gerçek | düzeltildi (`A_tenant_endpoint_is_checked_by_the_egress_guard`, `An_image_call_goes_to_the_v1_images_path…`) |
| 4 | AOT vaadi iki katmanda iddia: smoke Entra/STT/diyalog/görseli koşmaz; kapı yalnız tabloyu okur, liste elle | 🟡 | gerçek | kısmen düzeltildi (her "Yes" satırı listede ya da gerekçeli muaf; Azure README'si vaadi "API anahtarı" ile daraltır); kalan F-574, F-575 |
| 5 | `hafiza/openai-saglayici.md` bayat `Extended` cümlesi; `K-*` kayıtları ve kapanış bölümleri eksik | 🟡 | gerçek | düzeltildi (bu kapanış) |
| 6 | Sağlık `api-key`, sohbet `Bearer` — aynı anahtar için iki biçim | 🟡 | gerçek | düzeltildi (sağlık `Bearer`); canlı doğrulama `MT-PROV-091` 👤 |
| 7 | `YAYIN-HAZIRLIK.md` yeniden yazımı fazın diff'ine karışmış | 🟡 | gürültü (kapsam dışı iş) | gerekçelendi (*Plandan Sapmalar* 10); commit ayrı olmalı |
| 8 | `V1Endpoint` sorgu/parça taşıyan adres | 🟢 | gerçek | düzeltildi (`GetLeftPart(Path)`, test) |
| 9 | `transport` parametresi sessizce `egressGuard`'ı ezer | 🟢 | gerçek | belgelendi (XML `<param>`, *Plandan Sapmalar* 2) |
| 10 | Beyan yalnız alan adı tutar; `elevenlabs-kayma.py` testsiz | 🟢 | gerçek | F-576 |
| 11 | Zayıf testler (`Sovereign_cloud…`, `A_failed_call…`) | 🟢 | gerçek | silindi / wire testi kapsamı |
| 12 | `FakeProviderServer`'da kullanılmayan Gemini rotası | 🟢 | gerçek | silindi |
| 13 | `OPENAI001` pragma'sı tek çağrıda; tüketici OpenAI'ı yükseltirse Entra yolu kırılabilir | 🟢 | gerçek | kabul edildi, belgelendi (K-1020) |

Denetçi derleme ve test koşmadı (salt-okunur); C# sonuçlarını kapı doğruladı.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.

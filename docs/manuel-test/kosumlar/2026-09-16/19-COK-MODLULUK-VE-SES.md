# 19 — Çok Modluluk: Ek, Ses Tool'ları ve Gerçek Zamanlı Konuşma (`MM`) — Koşum Kaydı (2026-09-16)

> **Bu dosya bir koşum kaydıdır, spesifikasyon değildir.**
> Spesifikasyon: [`../../19-COK-MODLULUK-VE-SES.md`](../../19-COK-MODLULUK-VE-SES.md)
> — `Ön koşul`, `Adımlar`, `Beklenen sonuç` oradadır ve yeniden koşulabilir.
>
> Aşağısı yalnız **2026-09-16** koşumunun `Gerçek sonuç` ve `Durum` kayıtlarıdır.

| | |
|---|---|
| **Şerit** | `ap-s1` (Faz B — sıradaki aile: `13 · 19 · 04 · 18 · 10 · 08`) |
| **Çalışma kopyası** | `/Users/farukatasoy/Desktop/projects/ap-s1` · dal `test/kosum-s1` |
| **Kod** | `f721b229` donuk |
| **Case sayısı** | 93 (MT-MM-001..122, seyrek numaralı) |
| **Port** | 5081 |
| **Depo** | `mt_s1` PostgreSQL şeması (aile 13'ten devreden test verisi temizlenmedi — bu aile ona dokunmuyor) |

**Sapma — `user-secrets` yazılmaz** (skill §1.2): tüm kimlikler (OpenAI, Google,
Voice/ElevenLabs) tek seferde ortam değişkenine aktarıldı, hiçbir dosyaya/loga
yazılmadı.

**Açılış ölçümü:**

```
GET /api/diagnostics -> persistenceProvider=PostgreSQL, canConnect=true, migrationsUpToDate=true
GET /api/meta -> version 0.0.0-preview.0.789, storage.persistent=true,
  agentDefinitionStore=SqlAgentDefinitionStore, runStore=SqlRunStore
```

---

## Devir notu

**Oturum 1 — MT-MM-001..028 koşuldu (28 case).** Ayrıntı case bloklarında.

**🚨 Ölçülen ortam tuzağı — DLL doğrudan koşumu `appsettings.json`'ı bulamıyor.**
`dotnet artifacts/bin/Tracon.Api/release/Tracon.Api.dll` çalışma dizini
`ap-s1` kökündeyken başlatılırsa `ContentRootPath` = çağrının yapıldığı dizin
olur (DLL'in bulunduğu dizin DEĞİL), `appsettings.json` hiç yüklenmez ve
`OpenAICompatible:openrouter:Endpoint` gibi yalnız o dosyada tanımlı
varsayılanlar kaybolur — açılış `OptionsValidationException` ile çöker.
Çözüm: `cd artifacts/bin/Tracon.Api/release && dotnet Tracon.Api.dll --urls ...`
(DLL'in KENDİ dizininden çalıştır) YA DA `dotnet run --project samples/Tracon.Api
-c Release --no-build` kullan (bu doğru `ContentRootPath`'i otomatik ayarlar).//
Bu oturum ikinci yöntemi seçti.

**🚨 Ölçülen ortam tuzağı — `dotnet run` Development ortamında paylaşılan
`user-secrets`'ı SESSİZCE okuyor.** `Tracon:ContentProtection:RawKeys:sample`
env değişkeni olarak VERİLMEDİĞİ hâlde `dotnet run` ile başlatılan uygulama
ekleri (`attachments.content`) şifrelerken bu anahtarı bulup kullandı — çünkü
`dotnet run` varsayılan olarak `ASPNETCORE_ENVIRONMENT=Development` seçer ve
.NET, Development ortamında `AddUserSecrets` çağrısını OTOMATİK ekler; bu da
ap-s1 worktree'sinin `samples/Tracon.Api.csproj`'ının ANA repoyla AYNI
`UserSecretsId` taşıdığı için paylaşılan `~/.microsoft/usersecrets/<id>/`
deposunu okur. Skill §1.2 yalnız YAZMAYI yasaklıyor; bu okuma bir ihlal
DEĞİLDİR (yalnız okundu, hiçbir şey yazılmadı) ama gelecekteki bir şeridin
env değişkeniyle EZMEDİĞİ her anahtarın sessizce paylaşılan gerçek değerini
alacağını unutmayın — yalnız açıkça override edilen anahtarlar (OpenAI,
Anthropic, Google, OpenRouter, Voice) izole edilmiştir; `ContentProtection`,
`Pricing`, vb. override edilmeyen her şey paylaşılan depodan sızar. Bu turda
zararsızdı (yalnız okuma, şifreleme anahtarı sızmadı — HTTP yanıtında hiçbir
yerde görünmedi) ama not düşülüyor.

---

## MT-MM-001 — Geçerli bir PNG yüklenir

**Gerçek sonuç**
`HTTP: 201`. `mediaType: "image/png"`, `byteSize: 68`, `sha256` dolu,
`id` bir GUID (`01a0acc3-9d1c-7558-9ac5-e282bc2188c1`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-002 — Yanlış `Content-Type` sihirli bayt tarafından geçersiz kılınır

**Gerçek sonuç**
`mediaType` alanı `image/png` yazdırıldı — istemcinin `text/plain` iddiası
yok sayıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-003 — Yürütülebilir/tanınmayan içerik reddedilir

**Gerçek sonuç**
`HTTP: 400`, başlık "Attachment type rejected", detay tanınan türleri listeler.
`MZ` imzası hiçbir kuralla eşleşmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-004 — Boş dosya reddedilir

**Gerçek sonuç**
`HTTP: 400`, başlık "Attachment cannot be empty".

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-005 — `MaxBytes` (20 MB) aşımı reddedilir

**Gerçek sonuç**
`HTTP: 400`, başlık "Attachment too large", detay `20971529 bytes; the limit
is 20971520 bytes` yazdı (istek boyutu + 8 baytlık PNG imzası dahil, sınır
doğru).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-006 — MP3 çerçeve senkronu bit maskesiyle tanınır

**Gerçek sonuç**
Beş geçerli varyant (`fb/f3/f2/fa/e3`) `HTTP=201`, iki `reserved` varyant
(`e8`/`e1`) `HTTP=400` — hepsi `OK` (beklenenle eşleşti).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-010 — İndirme doğru başlıklarla ve bayt-bayt eşleşmeyle döner

**Gerçek sonuç**
`Content-Type: image/png`, `Content-Disposition: attachment; filename="test.png"`,
`X-Content-Type-Options: nosniff`, `ETag` sha256'nın kendisi. `diff` sıfır
fark, `BAYT BAYT AYNI` yazdırıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-011 — Listeleme `sessionId` ile filtreler; `skip`/`take` sınırlanır

**Gerçek sonuç**
İlk `curl` `1` yazdırdı (yalnız o oturumun eki). `take=99999&skip=-5` isteği
`HTTP: 200` döndü (400 değil) — kırpma davranışı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-012 — Silme sonrası indirme VE ikinci silme `404` döner

**Gerçek sonuç**
Sırasıyla `204`, `404`, `404` — beklenenle birebir.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-013 — Başka kiracının ekine erişilemez

**Ön koşul uygulaması:** Uygulama `Tracon:Tenancy:Enabled=true` +
`Tracon:Tenancy:AllowHeaderResolution=true` ile yeniden başlatıldı
(bu oturumdan itibaren aile 19'un tenancy gerektiren case'leri için kalıcı).

**Gerçek sonuç**
`kiraci-alfa` altında yüklenen ekin `kiraci-beta` ile GET'i `HTTP: 404` döndü —
"böyle bir ek yok" (403 değil), sızıntı yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-014 — Oturum silinince ekleri de gider (kaskad)

**Doküman notu:** Ön koşul metni "bir ek yüklenmiş" diyor ama bu tek başına
YETERSİZ — `POST /api/attachments?sessionId=...` bir `Session` KAYDI
OLUŞTURMAZ (yalnız ek tablosuna `session_id` sütunu yazar). İlk denemede
gerçek bir session hiç var olmadığı için `DELETE /api/sessions/musteri-42`
`HTTP: 404` döndü (kusur DEĞİL — `SessionEndpoints.DeleteSessionAsync`
XML dokümanının kendisi "an attachment may be uploaded before any session
exists" diyor, yani bu 404 TASARLANMIŞ davranış). Case'i anlamlı koşmak için
önce `POST /api/agents/support/run` ile gerçek bir oturum açıldı
(`sessionId: musteri-42`, gerçek OpenAI çağrısı), SONRA ek yüklendi.

**Gerçek sonuç**
Session gerçekten var olduktan sonra: ek yükleme `201`, silme `204`, silme
sonrası `GET /api/attachments?sessionId=musteri-42` `0` döndürdü — kaskad
uygulama katmanında çalışıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-020 — Ek + mesajla çalıştırma; modele giden içerik `DataContent`'tir

**Gerçek sonuç**
`HTTP: 200`, akış `event: done` ile tamamlandı, hata yok. Gerçek OpenAI
çağrısı (`support` agent, `gpt-5.4-mini`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-021 — Var olmayan bir ekle çalıştırma → `400`, akış başlamaz

**Gerçek sonuç**
`HTTP: 400`, detay: "There is no attachment with id
'00000000-0000-0000-0000-000000000000', or it does not belong to this
tenant." Yanıt akışlı DEĞİLDİ (düz JSON `ProblemDetails`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-022 — Başka kiracının eki çalıştırmada kullanılamaz

**Gerçek sonuç**
`kiraci-beta` ile `kiraci-alfa`'nın (MT-MM-013) ekiyle çalıştırma denemesi
`HTTP: 400`, aynı "Attachment not found" mesajı — kiracı sızıntısı yok, akış
başlamadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-023 — `message` boş ama `attachmentIds` doluysa istek geçerlidir

**Gerçek sonuç**
`HTTP: 200`, akış `event: done` ile tamamlandı — yalnız `attachmentIds` dolu
olması yeterli oldu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-026 — `/v1/responses` gömülü `data:` URI'yi eğe çevirir

**Gerçek sonuç**
`HTTP: 200`. `GET /api/attachments` listesinde gömülü PNG ayrı bir kayıt
(`fileName: "upload"`, `sessionId: "resp_..."`) olarak durdu — gövdedeki
base64 blok sohbet geçmişine olduğu gibi yazılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-027 — `/v1/chat/completions` görsel girdiyi kabul etmez

**Gerçek sonuç**
`HTTP: 200`. Model yanıtı görseli GÖREMEDİĞİNİ belirtti ("Görseli göremiyorum...")
— görsel parça sessizce yok sayıldı, yalnız metin parçası ulaştı. K-116'nın
bilinçli kapsam kararı doğrulandı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-MM-028 — Beyaz listede olmayan bir `data:` türü reddedilir

**Gerçek sonuç**
`HTTP: 400`, `AttachmentIngestion.ReplaceEmbeddedDataAsync` guard hatasını
doğrudan istemciye taşıdı ("File type not recognized...").

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# OpenAI Yanıt Şeması ve Strict Mod (Faz 245)

> `openai.strictJsonSchema` / `azure-openai.strictJsonSchema`, sağlayıcıya giden
> etkin şema, `IResponseSchemaSupport` ve doğrulama raporunun `responseSchemas`
> alanı. Kaynak: `src/Tracon.OpenAI.Shared/` (iki pakete derlenir). Sağlayıcı SDK
> tuzakları için [`openai-saglayici.md`](openai-saglayici.md), dekoratör sırası için
> [`model-boru-hatti.md`](model-boru-hatti.md).
>
> Bu dosya `MEMORY.md`'nin alan dosyasıdır. Yalnızca bu alana dokunurken okunur.

- **🚨 MEAI.OpenAI yanıt şemasını strict KAPALIYKEN de dönüştürür** (2026-10-25, 10.11.0
  decompile + canlı istek). `StrictSchemaTransformCache` her istekte koşar; `strict` bayrağı
  yalnız `CreateJsonSchemaFormat(…, strict)` parametresini belirler. Dönüşüm: dört seçenek
  (`DisallowAdditionalProperties`, `ConvertBooleanSchemas`, `MoveDefaultKeywordToDescription`,
  `RequireAllProperties`) + 22 anahtar `description` metnine taşınır. İsteğe bağlı bir alan
  `required`'a girer ve tipine `null` **eklenmez**: model onu uydurur (`"nickname": ""`, canlı).
  `oneOf` korunur. Dönüşüm `internal`; `OpenAIResponseSchema` kopyadır.
- **🚨 Kopya MEAI yükseltmesinde kayar; kapı tel testidir.** `StrictResponseSchemaWireTests`
  gerçek istek gövdesindeki şemayı kopyanın çıktısıyla bayt bayt karşılaştırır (iki yüzey ×
  iki mod), ayrıca 22 anahtarın hepsini taşıyan tek bir şemayla anahtar listesini sınar. Anahtar
  silme, `RequireAllProperties` kapatma, tool sarmayı atlama ve `options` yerinde yazma
  mutasyonlarının hepsi kırmızı oldu. Yükseltmede ilk bu sınıfı koş.
- **`strict` bayrağı tool'larla paylaşılır.** `HasStrict(tool.AdditionalProperties) ??
  HasStrict(options.AdditionalProperties)`; yalnız `true`/`false` bool okunur. Seçenekte `true`
  yazmak her tool'u strict yapar ve parametre şemasını yeniden yazar. Bu yüzden dekoratör her
  `AIFunctionDeclaration`'ı `strict = false` taşıyan bir sarmalayıcıyla değiştirir; tool kendi
  `strict` anahtarını yazdıysa dokunmaz. `DelegatingAIFunctionDeclaration` `internal`, sarmalayıcı
  elle yazıldı. Sarmalayıcı yalnız sağlayıcıya giden bildirimi değiştirir: dekoratör
  `FunctionInvokingChatClient`'ın ALTINDADIR ve tool çağrısını FIC kendi haritasından çözer.
- **Dekoratör `ChatOptions`'ı kopyalar.** Derlenmiş agent tek örneği paylaşır ve yedek zincir
  aynı örneği sonraki sağlayıcıya verir; yerinde yazmak strict'i yedeğe sızdırır. Yedek bağlantı
  zaten sağlayıcı ayarı almaz (`ModelFallback` yalnız sağlayıcı + model).
- **🚨 OpenAI strict modda 400 verir** (2026-10-25, `gpt-5.4-mini`, `chat/completions`,
  `strict: true`): `oneOf`, `allOf`, `if`, `then`, `dependentRequired`, `dependentSchemas`
  (`'…' is not permitted`); `additionalProperties` yok/`true`/şema ve serbest `{"type":"object"}`
  (`…is required to be supplied and to be false`); `$defs` içindeki açık nesne de. Responses yüzeyinde `oneOf` aynı mesajla reddedildi (ölçüldü). Kabul: `anyOf`,
  `enum`, `["string","null"]`, `$ref` + `$defs` (kapalı), boş `properties`. Sondaj betiği
  repoda yok; `manuel-test/22` `MT-GUARD-117` sonucu taşır. Azure ve uyumlu sunucu ÖLÇÜLMEDİ:
  `OpenAIModelProvider` yalnız `api.openai.com` ya da adres verilmemişse listeyi doldurur. Cevap kiracıdan bağımsızdır (BYOK uç adresi görünmez); bu yüzden kod UYARIdır, hata değil: kayıt yolu da doğrular ve hata kaydı engeller (denetim 🔴, 2026-10-25).
- **Responses'ta `strict` yazılmayan tool açık bir şemayı kabul eder** (ölçüldü, 2026-10-25): ayar açıkken tool'a `strict: false` yazmak sağlayıcı varsayılanını değiştirmez.
- **Strict + `UseOpenAICompatible()`**: aynı `openai.` öneki ve aynı dekoratör. Strict'i
  bilmeyen sunucu isteği reddedebilir; ayar isteğe bağlı olduğu için tüketici seçer.
- **Doğrulama `IResponseSchemaSupport`'u `ModelProviderDescriptor.ResponseSchemaSupport`
  (internal) üzerinden okur**, `IModelProviderRegistry`'ye üye eklenmedi. Rapor yedek bağlantıyı
  ayarsız bağlamayla sorar. İsteğe bağlı alan `null` kabul ediyorsa (`type` dizisi, `enum`, `const`,
  `anyOf`/`oneOf` kolu, boş şema) uyarıya girmez; `$ref` izlenmez, uyarıya girer.
- **Kabul edilen sınır:** geçersiz şema (ör. `properties.x` bir dizgi) `ArgumentException`
  verir; doğrulama o sağlayıcı için giriş yazmaz, çalıştırma ilk istekte düşer.
- **Python `urllib` bu makinede `CERTIFICATE_VERIFY_FAILED` verir** (python.org kurulumu,
  sertifika yok): canlı sondajı `curl` ile koş.

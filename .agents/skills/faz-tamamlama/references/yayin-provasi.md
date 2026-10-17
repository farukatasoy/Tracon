# Ek — Sevk edilen bir contract'a dokunulduysa: yayın provası

> `faz-tamamlama` Adım 1'den açılır. Tetik: `src/Tracon.Testing.Contracts.Xunit/`
> altındaki bir sözleşmeye case eklendi ya da davranışı değişti.

Kapanış kapısı yetmez:

```bash
python3 scripts/kapi.py yayin --kuru --surum <bir sonraki preview sürümü>
```

**Neden zorunlu:** `samples/` projeleri **hiçbir çözüm dosyasında değildir**
(`grep -c Samples Tracon.slnx` → `0`) ve bu bilinçlidir — yalnız
`PackageReference` ile, izole `NUGET_PACKAGES` içinde koşarak gerçek bir
tüketiciyi taklit ederler. Bunun bedeli şudur: `dotnet test Tracon.slnx`
onları **çalıştıramaz**. Sözleşmeyi genişleten faz, sevk ettiği sözleşmenin
kendi referans implementation'ında geçtiğini kapanış kapısıyla **kanıtlayamaz**.

**Ölçülen vaka (2026-09-02, `nuget-danismani`):** Faz 132 `RunStoreContract`'a
iki `ModelProvider` case'i ekledi, SQL ve bellek içi store'ları güncelledi,
kapanış kapısını 10/10 yeşil geçti. Yayın provası `EXIT=1` döndü:
`samples/Tracon.Samples.FileRunStore` `ModelProvider`'ı hiç işlemiyordu ve
sevk edilen suite dış tüketicide **kırmızıydı** (BL-053).

Prova ilk düşen sample'da durur; yeşil görene kadar kalan sample'lar ve AOT
smoke **ölçülmemiş** sayılır.

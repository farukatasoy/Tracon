# Kanıt Komutları

> `nuget-danismani` Adım 1 ve 2'nin komut yüzeyidir. Skill **hangi
> seviyede kanıt gerektiğini** söyler; bu dosya o kanıtın **nasıl ölçüleceğini**
> söyler.
>
> Kapı komutları burada tekrarlanmaz — dört doğrulama kapısı ve `secret`
> taraması [`.agents/ortak/kapilar.md`](../../../ortak/kapilar.md)'dedir.
> Buradaki komutlar kapı değil **ölçüm**dür: hiçbiri bir şeyi kırmaz, hepsi bir
> soruyu cevaplar.

Her komuttan önce ortam kuralı geçerlidir: `MSBUILDDISABLENODEREUSE=1`.
`kapi.py` bunu kendisi ekler; elle koşuyorsan sen eklersin.

---

## 1 — Yayın provası (5.–7. seviye kanıt, tek komut)

```bash
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.N
```

Kapsadığı yedi iş ve **kapsamadıkları**:

| Yapar | Yapmaz |
|---|---|
| Sürümü zorlar (`MinVerVersionOverride`), `git tag` atmaz | Etiketi **atmaz** — o kullanıcının kararıdır |
| Bayat `.nupkg`/`.snupkg` siler, sonra paketler | Kaynak ağacındaki davranışı ölçmez |
| Paket kimlik kümesini, sürüm hattını, ikonu, metaveriyi, K-008 ön sürüm sınırını doğrular | Paket **içeriğinin** doğruluğunu ölçmez (bkz. §3) |
| TFM başına XML doküman varlığını doğrular | XML dokümanının **doğruluğunu** ölçmez |
| `npm publish --dry-run` koşar (npm yoksa **atlar**) | npm yoksa sessizce geçer — çıktıyı oku |
| Beş extension sample'ını exact sürüm + izole `NUGET_PACKAGES` ile koşar | Sample'ın **iddia ettiği** davranışı yargılamaz |
| Native AOT smoke publish eder ve **çalıştırır** | Diğer paketlerin AOT davranışını ölçmez |

> ⚠️ `--surum` verilmezse MinVer'in bugünkü değeri kullanılır ve
> `1.0.0-preview.N` desenine uymayan sürüm **hata değil uyarı**dır. Yayın kararı
> için her zaman `--surum` ver.

---

## 2 — Paket kimliği, sürüm hattı, bağımlılık grafiği

```bash
ls artifacts/package/release/*.nupkg | wc -l          # paket sayısı
grep -L "<IsPackable>false" src/*/*.csproj | wc -l    # beklenen sayı, elle yazılmaz

# Bir paketin BEYAN ETTIGI bagimliliklar (tuketicinin gordugu tek gercek)
unzip -p artifacts/package/release/<Id>.<Sürüm>.nupkg <Id>.nuspec \
  | grep -E '<dependency|<group targetFramework'

# On surum bagimliligi yalniz AspNetCore'da olmali (K-008)
for f in artifacts/package/release/*.nupkg; do
  id=$(basename "$f" | sed -E 's/\.[0-9]+\.[0-9]+\.[0-9]+.*//')
  unzip -p "$f" "$id.nuspec" \
    | grep -oE 'id="[^"]*" version="[0-9]+\.[0-9]+\.[0-9]+-[^"]*"' \
    | grep -v 'id="Tracon' | sed "s|^|$id: |"
done
```

Paket **içeriği** yalnız `unzip -l` ile doğrulanır. `<None Update=...>` çapraz
hedefli projede **hiçbir uyarı vermeden** hiçbir şey yapmaz; dosyanın pakete
girdiğini kaynaktan okuyarak bilemezsin
([`docs/hafiza/paketleme-ve-dagitim.md`](../../../../docs/hafiza/paketleme-ve-dagitim.md)).

```bash
unzip -l artifacts/package/release/<Id>.<Sürüm>.nupkg \
  | grep -E "icon.png|README.md|lib/.*\.xml|buildTransitive/"
```

---

## 3 — Public API yüzeyi ve freeze durumu

```bash
# Shipped baseline BOS mu (preview hatti boyunca oyle olmali - K-603)
find src -name PublicAPI.Shipped.txt -exec cat {} + | grep -vcE '^\s*$|^#'

# Unshipped yuzeyin tip sayisi - bugun degistirmek ucuz olan yuzey
find src -name PublicAPI.Unshipped.txt -exec cat {} + \
  | grep -vE '^\s*$|^#' | grep -vE ' -> |\(' | sort -u | wc -l

# Iki surum arasi yuzey farki
git diff <önceki-etiket>..HEAD -- 'src/**/PublicAPI.Unshipped.txt'
```

Kullanılmayan public yüzeyi bulmak için: bir tipin adını `src/` içinde ara; tek
geçtiği yer kendi tanımıysa ve başka public imzada geçmiyorsa **yaprak**tır ve
`internal` adayıdır (Faz 96 deseni, K-601).

```bash
grep -rn "\b<TipAdı>\b" src/ --include=*.cs | grep -v "/<TipAdı>.cs:"
```

---

## 4 — Depo dışında, sıfırdan tüketici (6.–7. seviye kanıt)

Hazır kapı yalnız beş extension sample'ını kapsar. **Yeni** bir senaryoyu
ölçmek için aynı yalıtımı elle kur — üç şart birlikte sağlanmalıdır, biri
eksikse ölçüm geçersizdir:

```bash
TMP=$(mktemp -d)
export NUGET_PACKAGES="$TMP/cache"        # 1: IZOLE cache - global cache kanit degil
mkdir -p "$TMP/cache" "$TMP/app"

cat > "$TMP/NuGet.config" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="tracon-release" value="ARTIFACT_DIR" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="tracon-release"><package pattern="Tracon*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
XML
sed -i '' "s|ARTIFACT_DIR|$PWD/artifacts/package/release|" "$TMP/NuGet.config"

cd "$TMP/app" && dotnet new console
dotnet add package Tracon --version 1.0.0-preview.N   # 2: EXACT surum, floating YOK
# 3: ProjectReference YOK - csproj'da src/ yolu gecmemeli
dotnet build && dotnet run
```

Ölçümün gerçekten izole koştuğunu **doğrula** — `dotnet restore` sessizce global
cache'e düşebilir:

```bash
grep -q "$TMP/cache" artifacts/obj/<Proje>/project.assets.json && echo "izole ✅"
```

Test/contracts paketinin production graph'ına sızmadığını da aynı dosyadan oku:

```bash
grep -c '"Tracon.Testing' artifacts/obj/<Proje>/project.assets.json   # 0 olmali
```

---

## 5 — Native AOT ve trimming

`AotCompatible` property'si bir **iddia**dır, kanıt değil.

```bash
grep -l "AotCompatible>false" src/*/*.csproj      # AOT OLMAYAN paketler
```

Kanıt, packed tüketicinin `PublishAot` ile publish edilip **çalıştırılmasıdır**.
`kapi.py yayin` bunu `Tracon.Samples.ExtensionAotSmoke` üzerinden yapar.
Yeni bir yüzey için aynısını kur ve şu üçünü ara: trim uyarısı ·
`RequiresUnreferencedCode` · `RequiresDynamicCode`. Native AOT publish **somut
bir RID** ister; `--use-current-runtime` izole bir cache'e ILCompiler runtime
paketini güvenilir biçimde getirmez (ölçüldü, Faz 103).

---

## 6 — Doküman drift

```bash
# Paket sayfasinin metni - tuketicinin NuGet.org'da gordugu
unzip -p artifacts/package/release/<Id>.<Sürüm>.nupkg <Id>.nuspec | grep -E '<description|<tags'
unzip -p artifacts/package/release/<Id>.<Sürüm>.nupkg README.md | head -40

# Site kapilari (dordunu birden kosar)
cd docs-site && npm run check

# Sevk edilen metinde ic referans kaldi mi
grep -rnE "K-[0-9]{3}|F-[0-9]+|MT-[A-Z]+-|docs/[0-9]{2}-" src/ --include=*.cs | grep "///"
```

Sayı taşıyan iddialar (paket sayısı, aile sayısı) **elle güncellenir ve elle
bayatlar**. `docs-site/check-content.mjs` bunlardan bir kısmını kapıya bağlar;
kapıya bağlanmamış her sayı bir drift adayıdır.

---

## 7 — Güvenlik sınırının uçtan uca izlenmesi

Kaynak okuması bu soruyu **cevaplayamaz**. Görünmez karakter, ara katman
dönüşümü ve `ILogger` sızıntısı ancak çalışma anında görülür (K-525).

Yöntem: fırlatan/sızdıran bir **fake implementation** kur, sonra ham dizeyi
**her** çıkış yüzeyinde ara.

```bash
# Yuzey envanteri - hangi uclarin okunmasi gerektigi
grep -rn "MapPost\|MapGet\|WriteAsync\|ProblemDetails" src/Tracon.AspNetCore/ | wc -l
```

Okunacak yüzeyler — biri bile atlanırsa iddia geçersizdir: agent SSE · buffered
HTTP yanıtı · kalıcı `RunError` kaydı · OpenAI Responses ucu · Chat Completions
ucu · MCP · A2A · `ILogger` çıktısı.

Bir dizenin **tam** içeriğine dayanan iddiayı görünür karakterle doğrulama:

```bash
cat -v <dosya> | grep -n "<desen>"
grep -P '[\x00-\x1F]' <dosya>
```

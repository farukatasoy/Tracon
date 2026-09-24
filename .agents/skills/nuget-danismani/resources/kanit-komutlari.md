# Kanıt Komutları

> `nuget-danismani`'nin komut yüzeyidir. Skill **hangi seviyede kanıt
> gerektiğini** söyler; bu dosya o kanıtın **nasıl ölçüleceğini** söyler.
>
> Kapı komutları burada tekrarlanmaz — dört doğrulama kapısı ve `secret`
> taraması [`.agents/ortak/kapilar.md`](../../../ortak/kapilar.md)'dedir.
> Buradaki komutlar kapı değil **ölçüm**dür: hiçbiri bir şeyi kırmaz, hepsi bir
> soruyu cevaplar. Ağa yazan tek bir komut yoktur.

Her `dotnet` komutundan önce ortam kuralı geçerlidir: `MSBUILDDISABLENODEREUSE=1`.
`kapi.py` bunu kendisi ekler; elle koşuyorsan sen eklersin.

| § | Soru | Mercek / Adım |
|---|---|---|
| 1 | Paketlenen artifact doğru mu? | Adım 2 |
| 2 | Paket kimliği, sürüm hattı, bağımlılık grafiği | Adım 2 · Mercek 8 |
| 3 | Public API yüzeyi ve freeze durumu | Mercek 1 · 2 |
| 4 | Depo dışında, sıfırdan tüketici | Adım 1 seviye 6–7 |
| 5 | Native AOT ve trimming | Mercek 7 |
| 6 | Doküman drift | Adım 7 |
| 7 | Güvenlik sınırının uçtan uca izlenmesi | Mercek 3 |
| 8 | Upstream: ön sürüm, deneysel API, ileri uyum | Mercek 8 |
| 9 | Tedarik zinciri, lisans, zafiyet hattı | Mercek 9 |
| 10 | Canlı durum: CI, registry, bağımlılık botu | Adım 0 |
| 11 | Tetiklenmiş yeniden açılma ölçütleri | Adım 0 |
| 12 | Dış olgu tazeliği | Adım 0 |

---

## 1 — Yayın provası (5.–7. seviye kanıt)

Yerel prova — paketler ve doğrular:

```bash
python3 scripts/kapi.py yayin --kuru --surum 1.0.0-preview.N
```

CI zinciri (Faz 191, K-871) — tek derleme, tek pack:

```text
build (ubuntu)   : dotnet build → kapi.py paketle --cikti artifacts/ci-paket → testler → kapi.py paket-dogrula
release-dryrun   : kapi.py yayin --kuru --paket-dizini artifacts/ci-paket   (paketlemez, doğrular)
publish (v* tag) : kapi.py paket-dogrula → dotnet nuget push   (yalnız doğrulanan baytlar)
```

| Yapar | Yapmaz |
|---|---|
| Sürümü zorlar (`MinVerVersionOverride`); `git tag` atmaz | Etiketi **atmaz** — o kullanıcının kararıdır |
| Koşuma özgü **staging** dizinine paketler, `artifacts/package/yayin/<sürüm>/`'e promote eder; aynı kimlikte FARKLI içerik varsa hiçbirini taşımaz ve kırmızı döner (Faz 136, `_promote_staged_packages`) | Kaynak ağacındaki davranışı ölçmez |
| Paket kimlik kümesini, sürüm hattını, ikonu, metaveriyi, K-008 ön sürüm sınırını, ön sürüm bağımlılığın tam aralığını (K-872) ve `Tracon.UI` üçüncü taraf bildirimini (BL-058) doğrular | Paket **içeriğinin** doğruluğunu ölçmez (bkz. §2) |
| Son `v*` etiketinin paketlerini izole cache'e restore eder ve ApiCompat taban doğrulaması koşar; kırılan her tip sürüm notunda adıyla geçmezse kırmızıdır (K-864). **Ağ ister** | HTTP/OpenAPI, yapılandırma, telemetri ve hata kodu yüzeylerinin kırılmasını ölçmez (`sozlesme-yuzeyleri.md`) |
| TFM başına XML doküman varlığını doğrular | XML dokümanının **doğruluğunu** ölçmez |
| `npm publish --dry-run` koşar (npm yoksa **atlar**) | npm yoksa sessizce geçer — çıktıyı oku |
| **Altı** extension sample'ını exact sürüm + izole `NUGET_PACKAGES` ile koşar (BL-052, Faz 123; `scripts/release_extension_samples.py`, `kapi.py` içinden) | Sample'ın **iddia ettiği** davranışı yargılamaz |
| Native AOT smoke publish eder ve **çalıştırır** | Diğer paketlerin AOT davranışını ölçmez |

> ⚠️ `--surum` verilmezse MinVer'in bugünkü değeri kullanılır ve
> `1.0.0-preview.N` desenine uymayan sürüm **hata değil uyarı**dır. Yayın kararı
> için her zaman `--surum` ver. Kararlı hat provası `--surum 1.0.0`'dır;
> `NU5104` orada görünür (§8).

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

# Paket basina API satiri - donacak yuzeyin buyuklugu
for f in src/*/PublicAPI.Unshipped.txt; do
  printf '%-40s %s\n' "$(basename "$(dirname "$f")")" "$(grep -vcE '^\s*$|^#' "$f")"
done | sort -k2 -rn

# Dis kanit envanteri ve kanitsiz tiplerin gerekcesi (Faz 182, K-850)
python3 scripts/public-yuzey-envanteri.py --help

# GA'da kalkacak uyeler
grep -rn "\[Obsolete" src --include='*.cs' | grep -v /Generated/

# Iki surum arasi yuzey farki
git diff <önceki-etiket>..HEAD -- 'src/**/PublicAPI.Unshipped.txt'
```

Kullanılmayan public yüzeyi bulmak için dış kanıt ölçütü (K-850) ad
aramasından güçlüdür; ad araması uzantı sınıfında, öznitelikte ve `<see cref>`
yorumunda yanlış pozitif verir (K-601).

---

## 4 — Depo dışında, sıfırdan tüketici (6.–7. seviye kanıt)

Hazır kapı **altı** extension sample'ını kapsar. **Yeni** bir senaryoyu ölçmek
için aynı yalıtımı elle kur — üç şart birlikte sağlanmalıdır, biri eksikse
ölçüm geçersizdir:

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
grep -q "$TMP/cache" "$TMP/app/obj/project.assets.json" && echo "izole ✅"
grep -c '"Tracon.Testing' "$TMP/app/obj/project.assets.json"   # 0 olmali
```

Yayınlanmış bir sürümü ölçüyorsan `tracon-release` kaynağını çıkar: tüketici
nuget.org'dan ne alıyorsa onu al.

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

# Bayat oncul: artik dogru olmayan bir durumu anlatan yorum/dokuman
# (ornek: repo public oldugu halde "repository is private" diyen metin)
grep -rnE "repository is private|repo(sitory)? private|ozel repo" \
  src/ docs-site/scripts/ .github/ docs/hafiza/ --include='*.props' --include='*.mjs' \
  --include='*.yml' --include='*.md'
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
ucu · MCP · A2A · `ILogger` çıktısı · audit izi · telemetri etiketi.

Bir dizenin **tam** içeriğine dayanan iddiayı görünür karakterle doğrula:

```bash
cat -v <dosya> | grep -n "<desen>"
grep -P '[\x00-\x1F]' <dosya>
```

---

## 8 — Upstream: ön sürüm, deneysel API, ileri uyum

**Kararlı pack'i ne durdurur?** (`NU5104`, K-008)

```bash
# Yalniz olcum: cikti gecici dizine gider, yayin adayi degildir.
MSBUILDDISABLENODEREUSE=1 dotnet pack src/Tracon.AspNetCore -c Release \
  -p:MinVerVersionOverride=1.0.0 -p:TraconSkipCleanWorkingTreeCheck=true -o "$(mktemp -d)" 2>&1 \
  | grep -oE "NU5104[^\n]{0,200}" | sort -u
```

`NU5104` yalnız **doğrudan** bağımlılığa bakar; temiz pack grafiğin ön sürümsüz
olduğunu kanıtlamaz (`docs/hafiza/paketleme-ve-dagitim.md`).

**Upstream'in bugünkü GA durumu** — ezberden değil, registry'den:

```bash
for p in microsoft.agents.ai microsoft.agents.ai.hosting microsoft.agents.ai.hosting.openai \
         microsoft.agents.ai.hosting.a2a microsoft.agents.ai.hosting.aspnetcore a2a.aspnetcore \
         microsoft.extensions.ai modelcontextprotocol.core; do
  curl -s "https://api.nuget.org/v3-flatcontainer/$p/index.json" | python3 -c "
import json,sys; v=json.load(sys.stdin)['versions']; s=[x for x in v if '-' not in x]
print('$p', 'son:', v[-1], '| son kararli:', s[-1] if s else 'YOK')"
done
```

**İleri uyum ve deneysel maruziyet** — metadata seviyesinde, derleme gerekmez:

```bash
# Yayinlanmis Tracon ikilileri + en yeni upstream: eksik tip/uye var mi?
dotnet run .agents/skills/nuget-danismani/scripts/uyum-probu.cs -- ileri --tracon 1.0.0-preview.N

# Yalniz GA upstream'e karsi (on surum Hosting'i disarida birak)
dotnet run .agents/skills/nuget-danismani/scripts/uyum-probu.cs -- ileri --tracon 1.0.0-preview.N \
  --ust Microsoft.Agents.AI@latest Microsoft.Agents.AI.Abstractions@latest \
        Microsoft.Agents.AI.Workflows@latest Microsoft.Extensions.AI@latest \
        Microsoft.Extensions.AI.Abstractions@latest

# Upstream [Experimental] tipi Tracon'un public imzasinda mi? Kac bastirma var?
dotnet run .agents/skills/nuget-danismani/scripts/uyum-probu.cs -- deneysel
```

Aracın sınırı başlık yorumundadır: temiz sonuç **ikili** uyumdur, davranış
uyumu değildir. Bir bulguya dayanmadan önce **negatif kontrol** koş: `--ust`
ile eski bir upstream sürümü ver; araç eksik üye raporlamalıdır. Rapor
etmiyorsa ölçüm bozuktur.

**Birlikte yükselmesi gereken küme** — bot PR'ları neden kırmızı?

```bash
grep -A6 'targetFramework="net10.0"' \
  ~/.nuget/packages/microsoft.extensions.ai/<sürüm>/microsoft.extensions.ai.nuspec   # MEAI'nin istedigi Microsoft.Extensions.*
grep -n "MicrosoftExtensionsVersion" Directory.Packages.props
```

---

## 9 — Tedarik zinciri, lisans, zafiyet hattı

```bash
# Yayin kimligi: kalici secret kullanan is var mi?
grep -nE 'secrets\.[A-Z_]+' .github/workflows/*.yml
grep -nE 'id-token: write|environment:|attest|provenance|sbom' .github/workflows/*.yml

# npm paketi provenance tasiyor mu? (bos cikti = yok)
npm view @tracon/client@<sürüm> dist.attestations --json

# Gomulu ucuncu taraf varliginin lisans bildirimi (UI paketi)
unzip -l artifacts/package/release/Tracon.UI.<Sürüm>.nupkg | grep -iE "notice|third|licen"
brotli -d -c src/Tracon.UI/wwwroot/assets/index-*.js.br \
  | grep -ciE "@license|copyright|MIT License"          # 0 = bildirim yok
node -e "const l=require('./src/Tracon.UI/frontend/package-lock.json');
  const p=Object.entries(l.packages).filter(([k,v])=>k&&!v.dev);
  console.log(p.map(([k,v])=>k.replace(/.*node_modules\//,'')+' '+v.license).join('\n'))"

# nuget.org: on ek dogrulamasi ve sahiplik (tuketicinin gordugu)
curl -s "https://azuresearch-usnc.nuget.org/query?q=owner:Tracon&prerelease=true&semVerLevel=2.0.0&take=50" \
  | python3 -c "import json,sys;[print(x['id'],x['version'],'verified=',x['verified'],x['owners']) for x in json.load(sys.stdin)['data']]"

# Repo gorunurlugu - Source Link, GHSA ve environment korumasi buna bagli
curl -s https://api.github.com/repos/farukatasoy/Tracon | python3 -c \
  "import json,sys;d=json.load(sys.stdin);print(d.get('visibility'), d.get('has_discussions'), d.get('topics'))"
```

Zafiyet hattını bir **tatbikatla** ölç: GHSA taslağı açılabiliyor mu,
`SECURITY.md` kanalı ona yönlendiriyor mu? NuGetAudit tüketiciyi yalnız GitHub
Advisory Database'deki kayıtla uyarır; nuget.org deprecation'ı restore'da
uyarı üretmez.

---

## 10 — Canlı durum: CI, registry, bağımlılık botu

```bash
# CI'in hic gormedigi commit - yerelde yesil, CI'da kosmamis
git fetch --quiet origin && git rev-list --count origin/main..HEAD

# Son CI kosumlari ve dal/PR sonucu (anonim, public repo)
curl -s "https://api.github.com/repos/farukatasoy/Tracon/actions/runs?per_page=15" | python3 -c "
import json,sys
for r in json.load(sys.stdin)['workflow_runs']:
    print(r['created_at'], r['event'], r['head_branch'][:45], r['conclusion'])"

# Kirmizi bir kosumun hangi adimda kirildigi
curl -s "https://api.github.com/repos/farukatasoy/Tracon/actions/runs/<id>/jobs" | python3 -c "
import json,sys
for j in json.load(sys.stdin)['jobs']:
    print(j['name'], j['conclusion'], [s['name'] for s in j['steps'] if s.get('conclusion')=='failure'])"

# Tuketicinin bugun ne aldigi
curl -s https://api.nuget.org/v3-flatcontainer/tracon.core/index.json
npm view @tracon/client dist-tags --json
```

Yerelde `origin`'in önünde olan commit'ler için "CI yeşil" denmez. Faz 191
gibi CI zincirini değiştiren bir fazın **ilk gerçek koşumu** kendi başına bir
kanıt kalemidir (A-29 emsali: yalnız etikette koşan kapı ilk etikette düştü).

---

## 11 — Tetiklenmiş yeniden açılma ölçütleri

Karar ve defter kayıtları "şu olursa yeniden aç" der. Olay gerçekleşir ama
kayda kimse dönmez; metin doğru kalır, **öncül** bayatlar.

```bash
# Karar defterinin "yeniden acilma" sutunu - kosul bugun dogru mu?
grep -nE "private|GA oldu|GA olduğunda|2026-[0-9]{2}-[0-9]{2}'?(den|dan) sonra|public yapılırsa" docs/KARARLAR.md \
  | cut -c1-220
# Defterin kendi ertelenen/operasyon kalemleri
grep -nE "yeniden açıl|Yeniden açılma|repo public" docs/YAYIN-HAZIRLIK.md | cut -c1-220
```

Her eşleşme için tek soru: **koşul bugün sağlanıyor mu?** Sağlanıyorsa kalem
yeniden açılır ya da "koşul sağlandı, karar aynı kaldı çünkü …" diye kapanır.
Emsal: repo 2026-09-19'da public oldu. OP-011'in eylemi (environment
koruması) A-12 ile yapıldı ama satırı güncellenmedi; K-659'un koşulu sağlandı
ve karar yeniden değerlendirilmedi (A-65).

---

## 12 — Dış olgu tazeliği

Şu olgular ayda bir değişir ve ezberden yazılamaz: .NET destek tarihleri ·
upstream paketlerin GA durumu · registry token ve yayın politikaları (npm,
nuget.org) · CI runner ve action çalışma zamanı kaldırılmaları · test
framework major'ları.

Kural: olguyu **canlı** kaynaktan oku (registry API, resmî changelog, resmî
politika sayfası) ve deftere **tarih + kaynak** ile yaz: "ölçüldü 2026-09-24,
kaynak: …". Tarihsiz bir dış olgu bir sonraki turda yanlış bir kanıttır.

# Şerit İzolasyonu — Kurulum Tarifi

`manuel-test-kosumu` skill'inin §2 adımıdır. Bir koşum turu açılırken **bir
kez** uygulanır.

Şeritler ayrı `git worktree` içinde çalışır: aynı repo, ayrı çalışma kopyası,
ayrı dal. Dosya yazımı çakışmaz ve birleştirme önemsizdir.

```mermaid
flowchart TD
    R["Tracon (ana kopya)"] --> W1["ap-s1 · port 5081 · sema mt_s1"]
    R --> W2["ap-s2 · port 5082 · sema mt_s2"]
    R --> W3["ap-s3 · port 5083 · sema mt_s3"]
    R --> W4["ap-s4 · port 5084 · sema mt_s4"]
    W1 --> D["ap-pg · ap-mssql<br/>paylasilan container"]
    W2 --> D
    W3 --> D
    W4 --> D
```

Şerit sayısı tura göre değişir. Kural sabittir: **şerit başına bir worktree,
bir port, bir şema.**

---

## 1. Kurulum (bir kez, tek ajan yapar)

```bash
cd /Users/farukatasoy/Desktop/projects/Tracon

# 1. Aktif oturumun isi bitti mi? Bitmediyse BEKLE.
git status --short

# 2. Yarim kalan kosum sonuclarini kaydet.
git add docs/manuel-test && git commit -m "manual test: save partial run results"

# 3. Serit basina bir worktree.
for n in 1 2 3 4; do
  git worktree add ../ap-s$n -b test/kosum-s$n
done

# 4. Her worktree'yi bir kez TAM derle (sonraki kosumlar --no-build ile hizli olur).
for n in 1 2 3 4; do
  (cd ../ap-s$n && dotnet build Tracon.slnx -c Release)
done
```

Paylaşılan container'lar ayakta olmalıdır: `ap-pg` (55432), `ap-mssql` (51433).

> 🚨 Container'lar **paylaşılır**. Durdurma, silme, yeniden başlatma yok.
> Eski bir oturumdan kalmış container'a (`ap-pg-mt-core` gibi) dokunma.

---

## 2. Şeridin ortam bloğu

Her oturum bu bloğu çalıştırarak açılır. `<N>` şerit numarasıdır.

```bash
export SERIT=1                      # kendi serit numaran
export APORT=508$SERIT
export APU="http://localhost:$APORT/tracon"
export APB="Authorization: Bearer manuel-test-token-2026"
export PG="docker exec -i ap-pg psql -U postgres -d tracon"

cd /Users/farukatasoy/Desktop/projects/Tracon/../ap-s$SERIT

# Kimlik — hepsi ortam degiskeni, user-secrets DEGIL (SKILL.md §1.2).
export Tracon__Ui__AuthToken="manuel-test-token-2026"
export Tracon__Providers__OpenAI__ApiKey="$(cd /Users/farukatasoy/Desktop/projects/Tracon/samples/Tracon.Api && dotnet user-secrets list --json 2>/dev/null | python3 -c 'import sys,json;d=sys.stdin.read();d=d[d.index("{"):d.rindex("}")+1];print(json.loads(d).get("Tracon:Providers:OpenAI:ApiKey",""))')"
# Ayni deseni Anthropic, Google, OpenAICompatible:openrouter, Voice icin tekrarla.

# Kalicilik — HER SERIT KENDI IZOLASYONU. Ucunden yalniz biri dolu olur.
export Tracon__PostgreSql__ConnectionString="Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon"
export Tracon__PostgreSql__SchemaName="mt_s$SERIT"
export Tracon__Sqlite__ConnectionString=""
export Tracon__SqlServer__ConnectionString=""

dotnet run --project samples/Tracon.Api -c Release --no-build --urls "http://localhost:$APORT"
```

`dotnet user-secrets list` **yalnız okur**; yazma yasağı sürer. Anahtar hiçbir
dosyaya, hiçbir loga yazılmaz.

---

## 3. Şeridin reset yordamı

Şerit kapsamlıdır — `00-INDEKS.md` §4'ün genel yordamı yerine bu kullanılır:

```bash
# 1. Uygulamayi durdur.
# 2. Yalniz KENDI semani dusur.
docker exec -i ap-pg psql -U postgres -d tracon -c "DROP SCHEMA IF EXISTS mt_s$SERIT CASCADE;"
# 3. Yalniz KENDI sqlite dosyani sil.
rm -f samples/Tracon.Api/tracon-manuel.db*
# 4. Yalniz KENDI SQL Server veritabanini dusur (varsa).
docker exec -i ap-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa \
  -P 'Tracon!2026' -Q "DROP DATABASE IF EXISTS Tracon_S$SERIT;"
# 5. Uygulamayi yeniden baslat.
```

Arayüz şeridi ek olarak Playwright'ta site verisini temizler.

---

## 4. Doğrulama sunucusu (kapanış modu)

Kapanış modunda şerit izolasyonu gerekmez; tek bir doğrulama sunucusu yeter:

```bash
cd /Users/farukatasoy/Desktop/projects/Tracon

export Tracon__Ui__AuthToken="manuel-test-token-2026"
export Tracon__PostgreSql__ConnectionString="Host=localhost;Port=55432;Database=tracon;Username=postgres;Password=tracon"
export Tracon__PostgreSql__SchemaName="mt_fin"
export Tracon__Sqlite__ConnectionString=""
export Tracon__SqlServer__ConnectionString=""

dotnet run --project samples/Tracon.Api -c Release --no-build --urls "http://localhost:5090"
```

```bash
curl -s -H "Authorization: Bearer manuel-test-token-2026" \
  "http://localhost:5090/tracon/api/diagnostics" | python3 -m json.tool

docker exec -i ap-pg psql -U postgres -d tracon -c "DROP SCHEMA IF EXISTS mt_fin CASCADE;"
```

---

## 5. Ölçülmüş tuzaklar

- **`Bash` aracının çalışma dizini çağrılar arasında korunur.** Bir `cd`'den
  sonra göreli yol kırılır — **mutlak yol** kullan ya da her komutu
  `cd <repo kökü> &&` ile başlat.
- **`provider:"echo"` gerçek anahtarlar kayıtlıyken kayıtlı DEĞİLDİR** —
  `400 Tanim gecersiz` alırsın. Veritabanına ulaşan yolu görmek için gerçek bir
  sağlayıcı (`openai`) kullan.
- **Arayüz düzeltmelerinde `-p:TraconFrontendEnabled=false` KULLANMA.**
  `Tracon.UI` yeniden derlenmezse E2E testi eski bundle'ı koşar. Yalnız
  arayüze dokunmayan iç döngüde serbesttir.
- **`dotnet test --no-build` kırık build'de eski ikiliyi koşar** ve yanlış
  yeşil verir. Önce build'in başarılı olduğunu doğrula.

// uyum-probu.cs - nuget-danismani'nin upstream kanit araci (.NET 10 dosya tabanli uygulama).
//
// Iki soruyu METADATA seviyesinde olcer; kaynak okumasi bu sorulari cevaplayamaz:
//
//   ileri     Yayinlanmis Tracon ikilileri, upstream'in DAHA YENI surumuyle ayni surecte
//             yuklenirse referans verdikleri her tip ve uye hala var mi? Tracon'un
//             bagimlilik araligi alt sinirdir (>= x); tuketici MAF/MEAI'yi yukselttiginde
//             NuGet onu sessizce birlestirir ve eksik uye ancak calisma aninda
//             MissingMethodException / TypeLoadException olarak gorunur.
//   deneysel  Upstream'in [Experimental] isaretli tiplerinden hangileri Tracon'un PUBLIC
//             imzasinda geciyor, hangi tani kimligi kac kez bastiriliyor? Deneysel bir
//             upstream tipi minor surumde degisebilir; Tracon'un kararli sozu onu tasiyamaz.
//
// Kullanim (repo kokunden):
//   dotnet run .agents/skills/nuget-danismani/scripts/uyum-probu.cs -- ileri --tracon 1.0.0-preview.2
//   dotnet run .agents/skills/nuget-danismani/scripts/uyum-probu.cs -- ileri --tracon 1.0.0-preview.2 \
//       --ust Microsoft.Agents.AI@1.22.0 Microsoft.Extensions.AI.Abstractions@latest
//   dotnet run .agents/skills/nuget-danismani/scripts/uyum-probu.cs -- deneysel
//
// Girdi bicimleri: Id@Surum · Id@latest (en yeni kararli) · Id@latest-pre (on surum dahil)
// · yerel .nupkg yolu · .dll iceren dizin (ust seviye). --ust verilmezse upstream kumesi
// Directory.Packages.props'tan okunur: Microsoft.Agents.AI*, Microsoft.Extensions.AI*,
// ModelContextProtocol*. `ileri` onlari en yeni surumde (pin on surumse latest-pre),
// `deneysel` pinlenen surumde alir.
//
// Sinir: bu olcum IKILI uyumdur, DAVRANIS uyumu degildir. Temiz sonuc "yuklenir ve
// baglanir" der; "ayni davranir" demez. Deneysel API'nin anlami degisebilir.
// Negatif kontrol: --ust ile ESKI bir surum ver; arac eksik uye raporlamalidir.
//
// Repo'nun Directory.Build.props'u bu dosyaya da uygulanir (analyzer'lar, uyari = hata);
// arac o kurallarla temiz derlenir. Derleme ciktisi artifacts/{bin,obj}/uyum-probu/
// altina gider (bu dizindeki Directory.Build.props); paket onbellegi $TMPDIR altindadir.
//
// Cikis kodu: 0 temiz · 1 bulgu var · 2 kullanim/ag hatasi.

using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tracon.Agents.UyumProbu;

internal static class Program
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static async Task<int> Main(string[] args)
    {
        var komut = args.Length > 0 ? args[0] : "";
        if (!string.Equals(komut, "ileri", StringComparison.Ordinal) && !string.Equals(komut, "deneysel", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Kullanim: uyum-probu.cs -- ileri|deneysel [--tracon <surum>] [--tuketici <girdi>...] [--ust <girdi>...] [--tfm net10.0]");
            return 2;
        }

        var ileri = string.Equals(komut, "ileri", StringComparison.Ordinal);
        try
        {
            var secenekler = Secenekler.Oku(args.AsSpan(1));
            var kok = RepoKoku();
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var indirici = new Indirici(http, Path.Combine(Path.GetTempPath(), "tracon-uyum-probu"), secenekler.Tfm);
            var ustGirdiler = secenekler.Ust.Count > 0 ? secenekler.Ust : VarsayilanUpstream(kok, enYeni: ileri);
            var ust = new Derlemeler();
            foreach (var girdi in ustGirdiler)
            {
                foreach (var dll in await indirici.CozAsync(girdi).ConfigureAwait(false))
                {
                    ust.Ekle(dll);
                }
            }

            Console.WriteLine("upstream: " + string.Join(", ", indirici.Cozulenler));
            return ileri
                ? await IleriAsync(secenekler, indirici, ust, kok).ConfigureAwait(false)
                : Deneysel(ust, kok);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine("HATA: ag istegi basarisiz: " + ex.Message);
            return 2;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine("HATA: " + ex.Message);
            return 2;
        }
    }

    private static async Task<int> IleriAsync(Secenekler secenekler, Indirici indirici, Derlemeler ust, string kok)
    {
        var tuketiciGirdiler = new List<string>(secenekler.Tuketici);
        if (secenekler.Tracon is { } surum)
        {
            tuketiciGirdiler.AddRange(TraconKutuphaneleri(kok).Select(id => id + "@" + surum));
        }

        if (tuketiciGirdiler.Count == 0)
        {
            throw new InvalidOperationException("ileri: --tracon <surum> veya --tuketici <girdi> gerekli");
        }

        var bulgular = new SortedSet<string>(StringComparer.Ordinal);
        int denetlenen = 0, eksikTip = 0, eksikUye = 0, cozulemeyen = 0;
        var saglayici = new ImzaSaglayici();
        foreach (var girdi in tuketiciGirdiler)
        {
            foreach (var dll in await indirici.CozAsync(girdi, eksikseAtla: true).ConfigureAwait(false))
            {
                using var pe = new PEReader(File.OpenRead(dll));
                var md = pe.GetMetadataReader();
                var ben = md.GetString(md.GetAssemblyDefinition().Name);
                foreach (var trh in md.TypeReferences)
                {
                    var (asm, ad) = Meta.ReferansAdi(md, trh);
                    if (asm is not null && ust.Var(asm) && ust.TipBul(asm, ad) is null)
                    {
                        eksikTip++;
                        bulgular.Add($"{ben}: EKSIK TIP [{asm}] {ad}");
                    }
                }

                foreach (var mrh in md.MemberReferences)
                {
                    var mr = md.GetMemberReference(mrh);
                    if (Meta.UstTipReferansi(md, mr.Parent) is not { } tref)
                    {
                        continue;
                    }

                    var (asm, ad) = Meta.ReferansAdi(md, tref);
                    if (asm is null || !ust.Var(asm) || ust.TipBul(asm, ad) is not { } hedef)
                    {
                        continue;
                    }

                    var uyeAdi = md.GetString(mr.Name);
                    var tur = mr.GetKind();
                    string imza;
                    try
                    {
                        imza = tur == MemberReferenceKind.Method
                            ? Meta.MetotImzasi(mr.DecodeMethodSignature(saglayici, genericContext: null))
                            : "F:" + mr.DecodeFieldSignature(saglayici, genericContext: null);
                    }
                    catch (BadImageFormatException)
                    {
                        continue;
                    }

                    denetlenen++;
                    var sonuc = ust.UyeBul(hedef, uyeAdi, imza, tur);
                    if (sonuc == UyeSonucu.Yok)
                    {
                        eksikUye++;
                        bulgular.Add($"{ben}: EKSIK UYE [{asm}] {ad}::{uyeAdi} {imza}");
                    }
                    else if (sonuc == UyeSonucu.Cozulemedi)
                    {
                        cozulemeyen++;
                        bulgular.Add($"{ben}: COZULEMEDI (taban tip kumenin disinda) [{asm}] {ad}::{uyeAdi}");
                    }
                }
            }
        }

        foreach (var bulgu in bulgular)
        {
            Console.WriteLine(bulgu);
        }

        Console.WriteLine(string.Create(Inv, $"OZET denetlenen_uye={denetlenen} eksik_tip={eksikTip} eksik_uye={eksikUye} cozulemeyen={cozulemeyen}"));
        return eksikTip + eksikUye > 0 ? 1 : 0;
    }

    private static int Deneysel(Derlemeler ust, string kok)
    {
        var deneysel = ust.DeneyselTipler();
        foreach (var grup in deneysel.GroupBy(p => p.Value, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            Console.WriteLine(string.Create(Inv, $"deneysel upstream tip [{grup.Key}]: {grup.Count()}"));
        }

        var maruz = 0;
        if (deneysel.Count > 0)
        {
            var desen = new Regex(
                "(?<![A-Za-z0-9_.])(?<tip>" + string.Join("|", deneysel.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + ")(?![A-Za-z0-9_])",
                RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromSeconds(5));
            foreach (var dosya in Directory.EnumerateFiles(Path.Combine(kok, "src"), "PublicAPI.*.txt", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var goreli = Path.GetRelativePath(kok, dosya).Replace('\\', '/');
                var satirlar = File.ReadAllLines(dosya);
                for (var i = 0; i < satirlar.Length; i++)
                {
                    foreach (Match m in desen.Matches(satirlar[i]))
                    {
                        maruz++;
                        Console.WriteLine(string.Create(Inv, $"PUBLIC MARUZIYET [{deneysel[m.Groups["tip"].Value]}] {goreli}:{i + 1}: {satirlar[i]}"));
                    }
                }
            }
        }

        // Bastirma sayimi: kaynakta her `#pragma warning disable <ID>` bir upstream deneysel
        // API kullanimidir. Sayi tek basina bulgu degil; buyume egrisi bir risk olcusudur.
        var kimlikler = new SortedSet<string>(deneysel.Values, StringComparer.Ordinal);
        var pragma = new Regex(@"#pragma\s+warning\s+disable\s+(?<id>[A-Z]+[0-9]{3,4})", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(5));
        var sayim = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var objParcasi = Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar;
        foreach (var dosya in Directory.EnumerateFiles(Path.Combine(kok, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (dosya.Contains(objParcasi, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match m in pragma.Matches(File.ReadAllText(dosya)))
            {
                var id = m.Groups["id"].Value;
                if (kimlikler.Contains(id))
                {
                    sayim[id] = sayim.GetValueOrDefault(id) + 1;
                }
            }
        }

        foreach (var (id, n) in sayim)
        {
            Console.WriteLine(string.Create(Inv, $"src bastirma [{id}]: {n}"));
        }

        Console.WriteLine(string.Create(Inv, $"OZET deneysel_tip={deneysel.Count} public_maruziyet={maruz}"));
        return maruz > 0 ? 1 : 0;
    }

    private static string RepoKoku()
    {
        var dizin = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dizin is not null && !File.Exists(Path.Combine(dizin.FullName, "Directory.Packages.props")))
        {
            dizin = dizin.Parent;
        }

        return dizin?.FullName ?? throw new InvalidOperationException("repo koku bulunamadi (Directory.Packages.props yok); repo icinden kos");
    }

    // Tracon'un ikili tasiyan kutuphane paketleri: public API takibi olan projeler.
    private static IEnumerable<string> TraconKutuphaneleri(string kok) =>
        Directory.EnumerateFiles(Path.Combine(kok, "src"), "PublicAPI.Unshipped.txt", SearchOption.AllDirectories)
            .Select(p => Path.GetFileName(Path.GetDirectoryName(p)!))
            .Where(id => id.StartsWith("Tracon.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static List<string> VarsayilanUpstream(string kok, bool enYeni)
    {
        var props = XDocument.Load(Path.Combine(kok, "Directory.Packages.props"));
        var ozellikler = props.Descendants("PropertyGroup").Elements()
            .GroupBy(e => e.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value.Trim(), StringComparer.Ordinal);
        string[] onekler = ["Microsoft.Agents.AI", "Microsoft.Extensions.AI", "ModelContextProtocol"];
        var sonuc = new List<string>();
        foreach (var pv in props.Descendants("PackageVersion"))
        {
            var id = (string?)pv.Attribute("Include") ?? "";
            if (!onekler.Any(o => id.StartsWith(o, StringComparison.Ordinal)))
            {
                continue;
            }

            var surum = Regex.Replace(
                (string?)pv.Attribute("Version") ?? "",
                @"\$\((?<ad>\w+)\)",
                m => ozellikler.GetValueOrDefault(m.Groups["ad"].Value, m.Value),
                RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
                TimeSpan.FromSeconds(1));
            var hedef = surum.Contains('-', StringComparison.Ordinal) ? "latest-pre" : "latest";
            sonuc.Add(id + "@" + (enYeni ? hedef : surum));
        }

        return sonuc;
    }
}

internal enum UyeSonucu
{
    Var,
    Yok,
    Cozulemedi,
}

internal sealed class Secenekler
{
    public string? Tracon { get; private set; }

    public List<string> Tuketici { get; } = [];

    public List<string> Ust { get; } = [];

    public string Tfm { get; private set; } = "net10.0";

    public static Secenekler Oku(ReadOnlySpan<string> args)
    {
        var s = new Secenekler();
        List<string>? hedef = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--tracon":
                    s.Tracon = Deger(args, ++i, "--tracon");
                    hedef = null;
                    break;
                case "--tfm":
                    s.Tfm = Deger(args, ++i, "--tfm");
                    hedef = null;
                    break;
                case "--tuketici":
                    hedef = s.Tuketici;
                    break;
                case "--ust":
                    hedef = s.Ust;
                    break;
                default:
                    if (hedef is null || args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("bilinmeyen arguman: " + args[i]);
                    }

                    hedef.Add(args[i]);
                    break;
            }
        }

        return s;
    }

    private static string Deger(ReadOnlySpan<string> args, int i, string ad) =>
        i < args.Length ? args[i] : throw new InvalidOperationException(ad + " bir deger ister");
}

// Paket girdisini .dll yollarina cozer; nuget.org flatcontainer'dan indirir ve onbellekler.
internal sealed class Indirici(HttpClient http, string onbellek, string tfm)
{
    private static readonly string[] YedekTfmler = ["net9.0", "net8.0", "netstandard2.1", "netstandard2.0"];

    public List<string> Cozulenler { get; } = [];

    public async Task<IReadOnlyList<string>> CozAsync(string girdi, bool eksikseAtla = false)
    {
        if (Directory.Exists(girdi))
        {
            Cozulenler.Add(girdi);
            return Directory.GetFiles(girdi, "*.dll");
        }

        if (girdi.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && File.Exists(girdi))
        {
            Cozulenler.Add(Path.GetFileName(girdi));
            return Ac(girdi, Path.Combine(onbellek, "yerel", Path.GetFileNameWithoutExtension(girdi)));
        }

        var at = girdi.LastIndexOf('@');
        if (at <= 0)
        {
            throw new InvalidOperationException("girdi Id@Surum, .nupkg veya dizin olmali: " + girdi);
        }

        var id = girdi[..at].ToLowerInvariant();
        var surum = girdi[(at + 1)..];
        if (surum is "latest" or "latest-pre")
        {
            surum = await EnYeniAsync(id, onSurumDahil: string.Equals(surum, "latest-pre", StringComparison.Ordinal)).ConfigureAwait(false);
        }

        surum = surum.ToLowerInvariant();
        var hedef = Path.Combine(onbellek, id, surum);
        var nupkg = Path.Combine(hedef, id + "." + surum + ".nupkg");
        if (!File.Exists(nupkg))
        {
            Directory.CreateDirectory(hedef);
            var adres = new Uri($"https://api.nuget.org/v3-flatcontainer/{id}/{surum}/{id}.{surum}.nupkg");
            using var yanit = await http.GetAsync(adres).ConfigureAwait(false);
            if (!yanit.IsSuccessStatusCode)
            {
                if (eksikseAtla)
                {
                    Console.WriteLine($"atlandi (nuget.org'da yok): {id}@{surum}");
                    return [];
                }

                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"nuget.org {(int)yanit.StatusCode} dondu: {id}@{surum}"));
            }

            var gecici = nupkg + ".indiriliyor";
            var dosya = File.Create(gecici);
            await using (dosya.ConfigureAwait(false))
            {
                await yanit.Content.CopyToAsync(dosya).ConfigureAwait(false);
            }

            File.Move(gecici, nupkg, overwrite: true);
        }

        Cozulenler.Add(id + "@" + surum);
        return Ac(nupkg, Path.Combine(hedef, "lib"));
    }

    private async Task<string> EnYeniAsync(string id, bool onSurumDahil)
    {
        var akis = await http.GetStreamAsync(new Uri($"https://api.nuget.org/v3-flatcontainer/{id}/index.json")).ConfigureAwait(false);
        await using (akis.ConfigureAwait(false))
        {
            using var belge = await JsonDocument.ParseAsync(akis).ConfigureAwait(false);
            var surumler = belge.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!).ToList();
            return surumler.LastOrDefault(v => onSurumDahil || !v.Contains('-', StringComparison.Ordinal))
                ?? throw new InvalidOperationException(id + " icin surum bulunamadi");
        }
    }

    private List<string> Ac(string nupkg, string cikti)
    {
        using var zip = ZipFile.OpenRead(nupkg);
        var tfmler = zip.Entries
            .Select(e => e.FullName.Split('/'))
            .Where(p => p.Length == 3 && string.Equals(p[0], "lib", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[1])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var secilen = new[] { tfm }.Concat(YedekTfmler).FirstOrDefault(tfmler.Contains);
        if (secilen is null)
        {
            return [];
        }

        var dizin = Path.Combine(cikti, secilen);
        Directory.CreateDirectory(dizin);
        var onek = "lib/" + secilen + "/";
        var sonuc = new List<string>();
        foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith(onek, StringComparison.OrdinalIgnoreCase)
                                                  && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            var yol = Path.Combine(dizin, e.Name);
            if (!File.Exists(yol))
            {
                e.ExtractToFile(yol);
            }

            sonuc.Add(yol);
        }

        return sonuc;
    }
}

// Upstream derleme kumesi: tip dizini, yonlendirmeler ve uye arama.
internal sealed class Derlemeler
{
    private readonly Dictionary<string, MetadataReader> _okuyucular = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, TypeDefinitionHandle>> _tipler = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _yonlendirmeler = new(StringComparer.OrdinalIgnoreCase);
    private readonly ImzaSaglayici _saglayici = new();

    public void Ekle(string dll)
    {
        // PEReader bilerek dispose edilmez: MetadataReader onun bellegini kullanir ve
        // surec boyunca yasar. Arac kisa omurludur.
        var pe = new PEReader(File.OpenRead(dll));
        var md = pe.GetMetadataReader();
        var ad = md.GetString(md.GetAssemblyDefinition().Name);
        _okuyucular[ad] = md;
        var tipler = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        foreach (var th in md.TypeDefinitions)
        {
            tipler[Meta.TanimAdi(md, th)] = th;
        }

        _tipler[ad] = tipler;
        var yon = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var eh in md.ExportedTypes)
        {
            var et = md.GetExportedType(eh);
            if (et.Implementation.Kind == HandleKind.AssemblyReference)
            {
                var ns = md.GetString(et.Namespace);
                yon[(ns.Length > 0 ? ns + "." : "") + md.GetString(et.Name)] =
                    md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)et.Implementation).Name);
            }
        }

        _yonlendirmeler[ad] = yon;
    }

    public bool Var(string asm) => _okuyucular.ContainsKey(asm);

    public (string Asm, TypeDefinitionHandle Tip)? TipBul(string asm, string ad)
    {
        for (var adim = 0; adim < 5; adim++)
        {
            if (_tipler.TryGetValue(asm, out var d) && d.TryGetValue(ad, out var h))
            {
                return (asm, h);
            }

            if (_yonlendirmeler.TryGetValue(asm, out var y) && y.TryGetValue(ad, out var hedef) && Var(hedef))
            {
                asm = hedef;
                continue;
            }

            return null;
        }

        return null;
    }

    public UyeSonucu UyeBul((string Asm, TypeDefinitionHandle Tip) t, string ad, string imza, MemberReferenceKind tur)
    {
        for (var derinlik = 0; derinlik < 16; derinlik++)
        {
            var md = _okuyucular[t.Asm];
            var td = md.GetTypeDefinition(t.Tip);
            if (TipteVar(md, td, ad, imza, tur))
            {
                return UyeSonucu.Var;
            }

            // CLR uye referansini taban tiplerde de arar; upstream bir uyeyi tabana
            // tasidiysa ikili uyum korunur.
            if (Meta.TabanTip(md, td.BaseType) is not { } taban)
            {
                return UyeSonucu.Yok;
            }

            if (taban.Kind == HandleKind.TypeDefinition)
            {
                t = (t.Asm, (TypeDefinitionHandle)taban);
                continue;
            }

            if (taban.Kind != HandleKind.TypeReference)
            {
                return UyeSonucu.Cozulemedi;
            }

            var (asm, tipAdi) = Meta.ReferansAdi(md, (TypeReferenceHandle)taban);
            if (asm is null || !Var(asm) || TipBul(asm, tipAdi) is not { } sonraki)
            {
                // Taban System.* ise uyenin orada tanimli olmasi beklenmez: eksik sayilir.
                // Baska bir derlemede ise olcum karar veremez.
                return asm is not null && asm.StartsWith("System", StringComparison.Ordinal) ? UyeSonucu.Yok : UyeSonucu.Cozulemedi;
            }

            t = sonraki;
        }

        return UyeSonucu.Cozulemedi;
    }

    public Dictionary<string, string> DeneyselTipler()
    {
        var sonuc = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var md in _okuyucular.Values)
        {
            foreach (var th in md.TypeDefinitions)
            {
                if (Meta.DeneyselKimlik(md, md.GetTypeDefinition(th).GetCustomAttributes()) is { } id)
                {
                    sonuc[Meta.TanimAdi(md, th).Replace('/', '.')] = id;
                }
            }
        }

        return sonuc;
    }

    private bool TipteVar(MetadataReader md, TypeDefinition td, string ad, string imza, MemberReferenceKind tur)
    {
        if (tur == MemberReferenceKind.Method)
        {
            foreach (var mh in td.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                if (string.Equals(md.GetString(m.Name), ad, StringComparison.Ordinal)
                    && string.Equals(Meta.MetotImzasi(m.DecodeSignature(_saglayici, genericContext: null)), imza, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var fh in td.GetFields())
        {
            var f = md.GetFieldDefinition(fh);
            if (string.Equals(md.GetString(f.Name), ad, StringComparison.Ordinal)
                && string.Equals("F:" + f.DecodeSignature(_saglayici, genericContext: null), imza, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

internal static class Meta
{
    public static string MetotImzasi(MethodSignature<string> s) =>
        string.Create(CultureInfo.InvariantCulture, $"{(s.Header.IsInstance ? "inst " : "")}{s.ReturnType} ({string.Join(",", s.ParameterTypes)})`{s.GenericParameterCount}");

    public static string TanimAdi(MetadataReader md, TypeDefinitionHandle th)
    {
        var td = md.GetTypeDefinition(th);
        var ad = md.GetString(td.Name);
        if (td.IsNested)
        {
            return TanimAdi(md, td.GetDeclaringType()) + "/" + ad;
        }

        var ns = md.GetString(td.Namespace);
        return ns.Length > 0 ? ns + "." + ad : ad;
    }

    public static (string? Asm, string Ad) ReferansAdi(MetadataReader md, TypeReferenceHandle h)
    {
        var tr = md.GetTypeReference(h);
        var ad = md.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            var (asm, dis) = ReferansAdi(md, (TypeReferenceHandle)tr.ResolutionScope);
            return (asm, dis + "/" + ad);
        }

        var ns = md.GetString(tr.Namespace);
        var tam = ns.Length > 0 ? ns + "." + ad : ad;
        return tr.ResolutionScope.Kind == HandleKind.AssemblyReference
            ? (md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name), tam)
            : (null, tam);
    }

    // Uye referansinin sahibi: duz tip referansi veya generic ornegin tanim tipi.
    public static TypeReferenceHandle? UstTipReferansi(MetadataReader md, EntityHandle ebeveyn)
    {
        if (ebeveyn.Kind == HandleKind.TypeReference)
        {
            return (TypeReferenceHandle)ebeveyn;
        }

        if (ebeveyn.Kind != HandleKind.TypeSpecification || GenericTanim(md, (TypeSpecificationHandle)ebeveyn) is not { } tanim)
        {
            return null;
        }

        return tanim.Kind == HandleKind.TypeReference ? (TypeReferenceHandle)tanim : null;
    }

    public static EntityHandle? TabanTip(MetadataReader md, EntityHandle taban)
    {
        if (taban.IsNil)
        {
            return null;
        }

        return taban.Kind == HandleKind.TypeSpecification ? GenericTanim(md, (TypeSpecificationHandle)taban) : taban;
    }

    public static string? DeneyselKimlik(MetadataReader md, CustomAttributeHandleCollection oznitelikler)
    {
        foreach (var ah in oznitelikler)
        {
            var a = md.GetCustomAttribute(ah);
            var tip = a.Constructor.Kind switch
            {
                HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent is { Kind: HandleKind.TypeReference } p
                    ? md.GetString(md.GetTypeReference((TypeReferenceHandle)p).Name)
                    : "",
                HandleKind.MethodDefinition => md.GetString(md.GetTypeDefinition(md.GetMethodDefinition((MethodDefinitionHandle)a.Constructor).GetDeclaringType()).Name),
                _ => "",
            };
            if (string.Equals(tip, "ExperimentalAttribute", StringComparison.Ordinal))
            {
                var blob = md.GetBlobReader(a.Value);
                _ = blob.ReadUInt16();
                return blob.ReadSerializedString();
            }
        }

        return null;
    }

    private static EntityHandle? GenericTanim(MetadataReader md, TypeSpecificationHandle h)
    {
        var br = md.GetBlobReader(md.GetTypeSpecification(h).Signature);
        if (br.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
        {
            return null;
        }

        _ = br.ReadCompressedInteger(); // CLASS / VALUETYPE
        return br.ReadTypeHandle();
    }
}

// Imzalari derlemeden bagimsiz bir metne cevirir; iki taraf ayni saglayiciyla okunur.
internal sealed class ImzaSaglayici : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr(" + Meta.MetotImzasi(signature) + ")";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";

    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index.ToString(CultureInfo.InvariantCulture);

    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index.ToString(CultureInfo.InvariantCulture);

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => Meta.TanimAdi(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Meta.ReferansAdi(reader, handle).Ad;

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}

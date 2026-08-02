#!/usr/bin/env bash
# Microsoft Agent Framework ve komsu paketlerin gercek imzalarini reflection ile cikarir.
#
# Kullanim:
#   dump-api.sh                            -> tum public tiplerin adlarini listeler
#   dump-api.sh AIAgent AgentResponse      -> verilen tiplerin ayrintili imzalarini yazar
#   dump-api.sh '*Approval*'               -> joker; ad icinde gecen her tipi yazar
#
# Kapsanan derlemeler (varsayilan):
#   Microsoft.Agents.AI*      MAF cekirdegi + Harness
#   Microsoft.Extensions.AI*  AIContent turevleri (ToolApprovalRequestContent vb.)
#   ModelContextProtocol*     MCP istemcisi (McpClient, McpClientTool)
#
# Ortam degiskenleri:
#   APIDUMP_PACKAGES  Ek NuGet paketleri: "Id@Surum;Id@Surum"
#   APIDUMP_PREFIXES  Yansitilacak derleme adi onekleri (virgulle ayrilir)
#
# Ornek — baska bir paketi incelemek:
#   APIDUMP_PACKAGES="Azure.AI.OpenAI@2.6.0" APIDUMP_PREFIXES="Azure.AI" dump-api.sh
#
# Gecici proje $TMPDIR altinda olusturulur ve tekrar kullanilir. Paket listesi
# degistiginde proje yeniden yazilir; restore kendiliginden tetiklenir.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
WORK_DIR="${TMPDIR:-/tmp}/agentprism-maf-apidump"
PROPS="$REPO_ROOT/Directory.Packages.props"

# Surumleri tek kaynaktan al: Directory.Packages.props.
read_version() {
  # $1 = property adi veya PackageVersion Include degeri
  grep -oE "<$1>[^<]+" "$PROPS" | head -1 | cut -d'>' -f2
}

read_package_version() {
  grep -oE "<PackageVersion Include=\"$1\" Version=\"[^\"]+" "$PROPS" \
    | head -1 | sed 's/.*Version="//'
}

MAF_VERSION="$(read_version MicrosoftAgentsAIVersion)"

if [[ -z "$MAF_VERSION" ]]; then
  echo "HATA: Directory.Packages.props icinde MicrosoftAgentsAIVersion bulunamadi." >&2
  exit 1
fi

MEAI_VERSION="$(read_package_version 'Microsoft\.Extensions\.AI')"
MCP_VERSION="$(read_package_version 'ModelContextProtocol\.Core')"

PREFIXES="${APIDUMP_PREFIXES:-Microsoft.Agents.AI,Microsoft.Extensions.AI,ModelContextProtocol}"

mkdir -p "$WORK_DIR"
cd "$WORK_DIR"

{
  echo '<Project Sdk="Microsoft.NET.Sdk">'
  echo '  <PropertyGroup>'
  echo '    <OutputType>Exe</OutputType>'
  echo '    <TargetFramework>net10.0</TargetFramework>'
  echo '    <Nullable>disable</Nullable>'
  echo '    <ImplicitUsings>enable</ImplicitUsings>'
  echo '    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>'
  echo '    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>'
  echo '    <NoWarn>$(NoWarn);MAAI001;MEAI001;CS8981</NoWarn>'
  echo '  </PropertyGroup>'
  echo '  <ItemGroup>'
  echo "    <PackageReference Include=\"Microsoft.Agents.AI\" Version=\"$MAF_VERSION\" />"
  echo "    <PackageReference Include=\"Microsoft.Agents.AI.Harness\" Version=\"$MAF_VERSION\" />"
  [[ -n "$MEAI_VERSION" ]] && \
    echo "    <PackageReference Include=\"Microsoft.Extensions.AI\" Version=\"$MEAI_VERSION\" />"
  [[ -n "$MCP_VERSION" ]] && \
    echo "    <PackageReference Include=\"ModelContextProtocol.Core\" Version=\"$MCP_VERSION\" />"
  # APIDUMP_PACKAGES="Id@Surum;Id@Surum"
  if [[ -n "${APIDUMP_PACKAGES:-}" ]]; then
    IFS=';' read -ra extra <<< "$APIDUMP_PACKAGES"
    for entry in "${extra[@]}"; do
      [[ -z "$entry" ]] && continue
      echo "    <PackageReference Include=\"${entry%@*}\" Version=\"${entry#*@}\" />"
    done
  fi
  echo '  </ItemGroup>'
  echo '</Project>'
} > apidump.csproj.new

# Proje degistiyse yaz; degismediyse dokunma (gereksiz restore olmasin).
if ! cmp -s apidump.csproj.new apidump.csproj 2>/dev/null; then
  mv apidump.csproj.new apidump.csproj
else
  rm -f apidump.csproj.new
fi

cat > Program.cs <<'EOF'
using System.Reflection;
using System.Text;

var wanted = args;
var prefixes = (Environment.GetEnvironmentVariable("APIDUMP_PREFIXES")
        ?? "Microsoft.Agents.AI,Microsoft.Extensions.AI,ModelContextProtocol")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

// Cikti dizinindeki her derleme taranir: paket eklemek icin script'i degil,
// yalnizca APIDUMP_PACKAGES degiskenini degistirmek yeter.
var files = Directory.GetFiles(AppContext.BaseDirectory, "*.dll")
    .Select(Path.GetFileNameWithoutExtension)
    .Where(name => prefixes.Any(prefix => name!.StartsWith(prefix, StringComparison.Ordinal)))
    .OrderBy(name => name, StringComparer.Ordinal)
    .ToList();

if (files.Count == 0)
{
    Console.WriteLine($"!! '{string.Join(", ", prefixes)}' onekleriyle eslesen derleme yok.");
    return;
}

foreach (var assemblyName in files)
{
    Assembly assembly;
    try { assembly = Assembly.Load(assemblyName!); }
    catch { Console.WriteLine($"!! yuklenemedi: {assemblyName}"); continue; }

    Type[] types;
    try { types = assembly.GetExportedTypes(); }
    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

    var ordered = types.OrderBy(t => t.FullName, StringComparer.Ordinal).ToList();

    var matches = wanted.Length == 0
        ? []
        : ordered.Where(t => wanted.Any(w => Matches(t.Name, w))).ToList();

    if (wanted.Length > 0 && matches.Count == 0)
    {
        continue;
    }

    Console.WriteLine($"\n########## {assemblyName}  ({ordered.Count} public tip) ##########");

    if (wanted.Length == 0)
    {
        foreach (var chunk in ordered.Select(t => t.Name).Chunk(6))
        {
            Console.WriteLine("  " + string.Join(", ", chunk));
        }
        continue;
    }

    foreach (var type in matches)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\n>>> {type.FullName}   base: {type.BaseType?.Name}   {(type.IsAbstract ? "[abstract]" : "")}{(type.IsSealed ? "[sealed]" : "")}");

        if (type.IsEnum)
        {
            foreach (var name in Enum.GetNames(type))
            {
                sb.AppendLine($"    {name} = {Convert.ToInt64(Enum.Parse(type, name))}");
            }

            Console.WriteLine(sb.ToString());
            continue;
        }

        foreach (var iface in type.GetInterfaces().OrderBy(i => i.Name, StringComparer.Ordinal))
        {
            sb.AppendLine($"    : {Short(iface)}");
        }

        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 .Where(c => c.IsPublic || c.IsFamily))
        {
            sb.AppendLine($"    {(ctor.IsFamily ? "protected " : "")}ctor({Params(ctor.GetParameters())})");
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                               BindingFlags.Static | BindingFlags.DeclaredOnly)
                                   .Where(m => (m.IsPublic || m.IsFamily) && !m.IsSpecialName)
                                   .OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var modifiers = new StringBuilder();
            if (method.IsStatic) modifiers.Append("static ");
            if (method.IsFamily) modifiers.Append("protected ");
            if (method.IsVirtual && !method.IsFinal) modifiers.Append("virtual ");

            sb.AppendLine($"    {modifiers}{Short(method.ReturnType)} {method.Name}({Params(method.GetParameters())})");
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                     .OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            sb.AppendLine($"    prop {Short(property.PropertyType)} {property.Name} {{{(property.CanRead ? " get;" : "")}{(property.CanWrite ? " set;" : "")} }}");
        }

        Console.WriteLine(sb.ToString());
    }
}

// Joker destegi: '*Approval*' ad icinde arar, 'AIAgent' tam eslesir.
static bool Matches(string typeName, string pattern)
    => pattern.Contains('*', StringComparison.Ordinal)
        ? typeName.Contains(pattern.Trim('*'), StringComparison.OrdinalIgnoreCase)
        : string.Equals(typeName, pattern, StringComparison.OrdinalIgnoreCase);

static string Params(ParameterInfo[] parameters)
    => string.Join(", ", parameters.Select(p => $"{Short(p.ParameterType)} {p.Name}"));

static string Short(Type type)
{
    if (type is null) return "?";
    if (type.IsGenericType)
        return type.Name.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(Short)) + ">";
    return type.Name;
}
EOF

dotnet run --verbosity quiet -- "$@"

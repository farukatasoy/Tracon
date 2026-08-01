#!/usr/bin/env bash
# Microsoft Agent Framework tiplerinin gercek imzalarini reflection ile cikarir.
#
# Kullanim:
#   dump-api.sh                          -> tum public tiplerin adlarini listeler
#   dump-api.sh AIAgent AgentResponse    -> verilen tiplerin ayrintili imzalarini yazar
#
# Gecici proje $TMPDIR altinda olusturulur ve tekrar kullanilir.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
WORK_DIR="${TMPDIR:-/tmp}/agentprism-maf-apidump"

# Surumu tek kaynaktan al: Directory.Packages.props
MAF_VERSION="$(
  grep -oE '<MicrosoftAgentsAIVersion>[^<]+' "$REPO_ROOT/Directory.Packages.props" \
  | head -1 | cut -d'>' -f2
)"

if [[ -z "$MAF_VERSION" ]]; then
  echo "HATA: Directory.Packages.props icinde MicrosoftAgentsAIVersion bulunamadi." >&2
  exit 1
fi

mkdir -p "$WORK_DIR"
cd "$WORK_DIR"

cat > apidump.csproj <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NoWarn>\$(NoWarn);MAAI001;CS8981</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="$MAF_VERSION" />
    <PackageReference Include="Microsoft.Agents.AI.Harness" Version="$MAF_VERSION" />
  </ItemGroup>
</Project>
EOF

cat > Program.cs <<'EOF'
using System.Reflection;
using System.Text;

var wanted = args;
string[] assemblies =
[
    "Microsoft.Agents.AI.Abstractions",
    "Microsoft.Agents.AI",
    "Microsoft.Agents.AI.Harness",
];

foreach (var assemblyName in assemblies)
{
    Assembly assembly;
    try { assembly = Assembly.Load(assemblyName); }
    catch { Console.WriteLine($"!! yuklenemedi: {assemblyName}"); continue; }

    var types = assembly.GetExportedTypes().OrderBy(t => t.FullName).ToList();

    Console.WriteLine($"\n########## {assemblyName}  ({types.Count} public tip) ##########");

    if (wanted.Length == 0)
    {
        foreach (var chunk in types.Select(t => t.Name).Chunk(6))
        {
            Console.WriteLine("  " + string.Join(", ", chunk));
        }
        continue;
    }

    foreach (var type in types.Where(t => wanted.Contains(t.Name, StringComparer.OrdinalIgnoreCase)))
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\n>>> {type.FullName}   base: {type.BaseType?.Name}   {(type.IsAbstract ? "[abstract]" : "")}{(type.IsSealed ? "[sealed]" : "")}");

        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 .Where(c => c.IsPublic || c.IsFamily))
        {
            sb.AppendLine($"    {(ctor.IsFamily ? "protected " : "")}ctor({Params(ctor.GetParameters())})");
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                               BindingFlags.Static | BindingFlags.DeclaredOnly)
                                   .Where(m => (m.IsPublic || m.IsFamily) && !m.IsSpecialName)
                                   .OrderBy(m => m.Name))
        {
            var modifiers = new StringBuilder();
            if (method.IsStatic) modifiers.Append("static ");
            if (method.IsFamily) modifiers.Append("protected ");
            if (method.IsVirtual && !method.IsFinal) modifiers.Append("virtual ");

            sb.AppendLine($"    {modifiers}{Short(method.ReturnType)} {method.Name}({Params(method.GetParameters())})");
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                                     .OrderBy(p => p.Name))
        {
            sb.AppendLine($"    prop {Short(property.PropertyType)} {property.Name} {{{(property.CanRead ? " get;" : "")}{(property.CanWrite ? " set;" : "")} }}");
        }

        Console.WriteLine(sb.ToString());
    }
}

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

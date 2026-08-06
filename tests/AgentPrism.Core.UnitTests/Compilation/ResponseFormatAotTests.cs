namespace AgentPrism.Core.UnitTests.Compilation;

/// <summary>
/// <c>ChatResponseFormat.ForJsonSchema(Type, ...)</c> ve
/// <c>ForJsonSchema(JsonSerializerOptions, ...)</c> asiri yuklemeleri yansimaya
/// dayanir (<c>AIJsonUtilities.CreateJsonSchema</c>) ve <c>AgentPrism.Abstractions</c>
/// ile <c>.Core</c>'un AOT duruşunu bozar. Bu test kaynak agacini tarayarak bu
/// asiri yuklemelerin hic cagrilmadigini doğrular. Bkz. docs/38-YAPILANDIRILMIS-CIKTI.md, 38.4.
/// </summary>
public sealed class ResponseFormatAotTests
{
    [Fact]
    public void ForJsonSchema_Type_asiri_yuklemesi_kaynak_agacinda_yoktur()
        => AssertNoMatch("ForJsonSchema(typeof(", "ForJsonSchema<");

    [Fact]
    public void ForJsonSchema_JsonSerializerOptions_asiri_yuklemesi_yoktur()
        => AssertNoMatch("ForJsonSchema(serializerOptions", "ForJsonSchema(options", "ForJsonSchema(jsonOptions");

    private static void AssertNoMatch(params string[] forbiddenSnippets)
    {
        var srcRoot = Path.Combine(RepositoryRoot, "src");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var content = File.ReadAllText(file);

            foreach (var snippet in forbiddenSnippets)
            {
                if (content.Contains(snippet, StringComparison.Ordinal))
                {
                    offenders.Add($"{file}: '{snippet}'");
                }
            }
        }

        offenders.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, offenders));
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Depo koku bulunamadi. '{AppContext.BaseDirectory}' konumundan yukari dogru AgentPrism.slnx arandi.");
    }
}

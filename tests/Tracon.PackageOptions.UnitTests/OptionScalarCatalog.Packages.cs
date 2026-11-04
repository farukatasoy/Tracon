using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// The scope of this project: the sections that live in a package of their own (a model provider, MCP, voice,
/// workflows, a SQL store), each composed the way a host composes it.
/// </summary>
internal static partial class OptionScalarCatalog
{
    /// <summary>A value no provider will accept as a credential and no scan takes for one.</summary>
    private const string Credential = "placeholder-not-a-credential";

    private static readonly Dictionary<Type, (string Section, Action<ITraconBuilder, IConfigurationSection> Compose, string[] BaseKeys)> Sections = new()
    {
        [typeof(AnthropicProviderOptions)] = (
            "Providers:Anthropic",
            static (builder, section) => builder.UseAnthropic(section),
            ["ApiKey"]),
        [typeof(AzureOpenAIProviderOptions)] = (
            "Providers:AzureOpenAI",
            static (builder, section) => builder.UseAzureOpenAI(section),
            ["Endpoint", "ApiKey"]),
        [typeof(GoogleProviderOptions)] = (
            "Providers:Google",
            static (builder, section) => builder.UseGoogle(section),
            ["ApiKey"]),
        [typeof(OpenAIProviderOptions)] = (
            "Providers:OpenAI",
            static (builder, section) => builder.UseOpenAI(section),
            ["ApiKey"]),
        [typeof(OpenAILiveOptions)] = (
            "Providers:OpenAI:Live",
            static (builder, section) => builder.UseOpenAI(Credential).UseOpenAILive(section),
            []),
        [typeof(TraconMcpOptions)] = (
            "Mcp",
            static (builder, section) => builder.UseMcp(section, configure: null),
            []),
        [typeof(VoiceOptions)] = (
            "Voice",
            static (builder, section) => builder.UseVoice(section),
            []),
        [typeof(TraconWorkflowOptions)] = (
            "Workflows",
            static (builder, _) => builder.UseWorkflows(),
            []),
        [typeof(TraconPostgreSqlOptions)] = (
            "PostgreSql",
            static (builder, section) => builder.UsePostgreSql(section),
            ["ConnectionString"]),
        [typeof(TraconSqlServerOptions)] = (
            "SqlServer",
            static (builder, section) => builder.UseSqlServer(section),
            ["ConnectionString"]),
        [typeof(TraconSqliteOptions)] = (
            "Sqlite",
            static (builder, section) => builder.UseSqlite(section),
            ["ConnectionString"]),
    };

    private static partial IEnumerable<Type> SectionTypes()
        => Sections.Keys
            .Where(static type => type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)
                is { IsLiteral: true } field
                && field.GetRawConstantValue() is string name
                && name.StartsWith("Tracon", StringComparison.Ordinal))
            .Where(static type => type.GetConstructor(Type.EmptyTypes) is not null);

    private static partial Action<ITraconBuilder, IConfigurationSection>? ComposeFor(Type type)
    {
        var (section, compose, _) = Sections[type];

        // The probe hands over the Tracon section; each package call takes its own.
        return (builder, tracon) => compose(builder, tracon.GetSection(section));
    }

    private static partial IReadOnlyDictionary<string, string?> BaseKeysFor(Type type)
    {
        var keys = Sections[type].BaseKeys;
        var sectionPath = (string)type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static)!.GetRawConstantValue()!;
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            values[$"{sectionPath}:{key}"] = key switch
            {
                "Endpoint" => "https://probe.example.net/",
                "ConnectionString" => ConnectionStringFor(type),
                _ => Credential,
            };
        }

        return values;
    }

    private static string ConnectionStringFor(Type type)
        => type == typeof(TraconPostgreSqlOptions)
            ? "Host=probe.invalid;Database=probe;Username=probe;Password=probe"
            : type == typeof(TraconSqlServerOptions)
                ? "Server=probe.invalid;Database=probe;User Id=probe;Password=probe;TrustServerCertificate=true"
                : "Data Source=probe.db";
}

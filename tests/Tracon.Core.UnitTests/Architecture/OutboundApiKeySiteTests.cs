namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every place in <c>src/</c> that puts a key set up at start into a header must pass the key through
/// <c>OutboundApiKey</c> first, and every options validator of such a key must ask the same rule.
/// </summary>
/// <remarks>
/// <para>
/// A key read from a file or a mounted secret usually ends in a line break. A client that validates the header it builds
/// refuses such a key with a sentence that quotes the whole value, and a client that does not validate writes the line
/// break to the wire. The first form leaked the key into responses, run records, logs and spans; it appeared in one
/// provider while the other three, which build the same header another way, showed no leak and were taken for safe.
/// </para>
/// <para>
/// This gate does not read the code: it makes a NEW sender fail until someone either routes its key through the rule or
/// names the reason it is exempt. A site that disappears fails too, so the list cannot go stale. The key a tenant
/// supplies is not a setup key: it is masked where the text of its failure is shown, and the factories build its client
/// through a separate entry that does not trim.
/// </para>
/// </remarks>
public sealed class OutboundApiKeySiteTests
{
    /// <summary>The text that marks a line that hands a key to a header or to an SDK that builds one.</summary>
    private static readonly string[] SenderMarkers =
    [
        "new ApiKeyCredential(",
        "AuthenticationHeaderValue(\"Bearer\"",
        "\"x-goog-api-key\"",
        "new Client(apiKey:",
        "Anthropic.Core.ClientOptions",
        "TryAddWithoutValidation(ApiKeyHeader",
        "SetRequestHeader(\"Authorization\"",
    ];

    /// <summary>The sites that send a setup key. Each must call the rule.</summary>
    private static readonly string[] NormalizedSites =
    [
        "src/Tracon.Anthropic/AnthropicChatClientFactory.cs",
        "src/Tracon.Anthropic/AnthropicProviderHealthCheck.cs",
        "src/Tracon.Azure/AzureOpenAIChatClientFactory.cs",
        "src/Tracon.Azure/AzureOpenAIProviderHealthCheck.cs",
        "src/Tracon.Google/GoogleChatClientFactory.cs",
        "src/Tracon.Google/GoogleProviderHealthCheck.cs",
        "src/Tracon.OpenAI/OpenAIChatClientFactory.cs",
        "src/Tracon.OpenAI/OpenAIProviderHealthCheck.cs",
        "src/Tracon.OpenAI/Live/OpenAILiveProvider.cs",
        "src/Tracon.Voice/Internal/ElevenLabsSpeechClient.cs",
    ];

    /// <summary>The validators of those keys. Each must ask the rule, so a key that cannot be sent stops the host at start.</summary>
    private static readonly string[] ValidatorSites =
    [
        "src/Tracon.Anthropic/AnthropicProviderOptionsValidator.cs",
        "src/Tracon.Azure/AzureOpenAIProviderOptionsValidator.cs",
        "src/Tracon.Google/GoogleProviderOptionsValidator.cs",
        "src/Tracon.OpenAI/OpenAIProviderOptionsValidator.cs",
        "src/Tracon.Voice/VoiceOptionsValidator.cs",
    ];

    /// <summary>Senders that carry a credential which is not a provider key set up at start.</summary>
    private static readonly Dictionary<string, string> ExemptSites = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Client/TraconClientServiceCollectionExtensions.cs"] =
            "a token for Tracon's own API, given by the application that uses the client; the validating header constructor does not quote the value in its failure",
        ["src/Tracon.Cli/Commands/DefinitionTransferHttp.cs"] =
            "a token for Tracon's own API, given on the command line or in the environment; the validating header constructor does not quote the value in its failure",
    };

    [Fact]
    public void Every_sender_of_a_setup_key_passes_it_through_the_rule_or_is_exempt_for_a_stated_reason()
    {
        var root = FindRepositoryRoot();

        var senders = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Relative: Path.GetRelativePath(root, path).Replace('\\', '/'), Text: File.ReadAllText(path)))
            .Where(file => SenderMarkers.Any(marker => file.Text.Contains(marker, StringComparison.Ordinal)))
            .ToDictionary(file => file.Relative, file => file.Text, StringComparer.Ordinal);

        var unknown = senders.Keys
            .Where(path => !NormalizedSites.Contains(path, StringComparer.Ordinal) && !ExemptSites.ContainsKey(path))
            .Order(StringComparer.Ordinal)
            .ToList();

        var gone = NormalizedSites.Concat(ExemptSites.Keys)
            .Where(path => !senders.ContainsKey(path))
            .Order(StringComparer.Ordinal)
            .ToList();

        var withoutRule = NormalizedSites
            .Where(path => senders.TryGetValue(path, out var text) && !text.Contains("OutboundApiKey.Normalize(", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        unknown.ShouldBeEmpty(
            "A new place sends a key in a header. If the key is set up at start, pass it through OutboundApiKey.Normalize " +
            "and add the file to NormalizedSites; if it is another credential, name the reason in ExemptSites.");
        gone.ShouldBeEmpty("A listed sender no longer builds a key header; remove it from the list.");
        withoutRule.ShouldBeEmpty("A sender of a setup key does not call OutboundApiKey.Normalize.");
    }

    [Fact]
    public void Every_validator_of_a_setup_key_asks_the_same_rule()
    {
        var root = FindRepositoryRoot();

        var missing = ValidatorSites
            .Where(path => !File.ReadAllText(Path.Combine(root, path)).Contains("OutboundApiKey.Refusal(", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        missing.ShouldBeEmpty("An options validator of a setup key does not ask OutboundApiKey.Refusal, so a key that cannot be sent would not stop the host at start.");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}

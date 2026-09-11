using System.Text.Json;
using System.Text.Json.Nodes;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies that the committed <c>docs/openapi/agentprism.json</c> file stays
/// identical to the document produced by the running host (Phase 40, section 40.4).
/// </summary>
/// <remarks>
/// This pattern turns the document drifting from the code into a build gate.
/// To refresh the file:
/// <c>AGENTPRISM_OPENAPI_REFRESH=1 dotnet test tests/AgentPrism.AspNetCore.FunctionalTests
/// -c Release --filter FullyQualifiedName~OpenApiSnapshotTests</c>.
/// </remarks>
public sealed class OpenApiSnapshotTests
{
    private const string RefreshEnvVar = "AGENTPRISM_OPENAPI_REFRESH";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    [Fact]
    public async Task Document_matches_the_committed_snapshot()
    {
        var current = await GenerateAsync();

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            await File.WriteAllTextAsync(SnapshotPath, current);
        }

        File.Exists(SnapshotPath).ShouldBeTrue(
            $"'{SnapshotPath}' does not exist. Generate it first with '{RefreshEnvVar}=1' (see the class description).");

        var committed = await File.ReadAllTextAsync(SnapshotPath);

        // Plain ShouldBe() dumps BOTH multi-KB strings in full on failure. The
        // document is near-entirely one giant pretty-printed JSON value per
        // property, so that dump is a handful of extremely long lines -- CI log
        // viewers (and anyone pasting the failure) truncate well before the
        // actual point of divergence. Report just the first differing character
        // and a short window around it instead.
        if (!string.Equals(current, committed, StringComparison.Ordinal))
        {
            throw new ShouldAssertException(DescribeDifference(current, committed));
        }
    }

    /// <summary>
    /// Guards the Windows leg: a multi-line XML doc comment reaches the document as
    /// CRLF there, because the compiler writes the documentation file with
    /// <c>Environment.NewLine</c>. The escaped '\r' sits inside the JSON string value,
    /// so the outer <c>ReplaceLineEndings()</c> call never sees it and the committed
    /// LF snapshot failed on windows-latest while Ubuntu stayed green.
    /// </summary>
    [Fact]
    public void Normalization_rewrites_carriage_returns_inside_string_values()
    {
        var document = JsonNode.Parse(
            """
            {
              "paths": {
                "/api/agents": {
                  "get": {
                    "description": "First line.\r\nSecond line.\rThird line.",
                    "tags": [ "Agents\r\nsecond" ],
                    "deprecated": false
                  }
                }
              }
            }
            """)!;

        NormalizeStringLineEndings(document);

        document["paths"]!["/api/agents"]!["get"]!["description"]!.GetValue<string>()
            .ShouldBe("First line.\nSecond line.\nThird line.");
        document["paths"]!["/api/agents"]!["get"]!["tags"]![0]!.GetValue<string>()
            .ShouldBe("Agents\nsecond");
        document["paths"]!["/api/agents"]!["get"]!["deprecated"]!.GetValue<bool>().ShouldBeFalse();
    }

    private static string DescribeDifference(string current, string committed)
    {
        var length = Math.Min(current.Length, committed.Length);
        var index = 0;

        while (index < length && current[index] == committed[index])
        {
            index++;
        }

        var line = 1;

        for (var i = 0; i < index; i++)
        {
            if (committed[i] == '\n')
            {
                line++;
            }
        }

        const int radius = 120;
        var start = Math.Max(0, index - radius);

        static string Snippet(string text, int start, int index)
            => start >= text.Length
                ? "<string ends here>"
                : text[start..Math.Min(text.Length, index + 120)];

        return "The OpenAPI document differs from 'docs/openapi/agentprism.json' at character " +
               $"{index} (line {line}). Lengths: current={current.Length}, committed={committed.Length}." +
               Environment.NewLine +
               $"committed: ...{Snippet(committed, start, index)}..." + Environment.NewLine +
               $"current:   ...{Snippet(current, start, index)}..." + Environment.NewLine +
               $"To refresh: {RefreshEnvVar}=1 dotnet test tests/AgentPrism.AspNetCore.FunctionalTests " +
               "-c Release --filter FullyQualifiedName~OpenApiSnapshotTests";
    }

#pragma warning disable MEAI001
    private static async Task<string> GenerateAsync()
    {
        // Section 84.3: the document is generated with every optional endpoint turned
        // on. A default-off endpoint (like /api/diagnostics) is still part of the
        // surface a consumer sees once they enable it, and every generated artifact
        // (this document, the .NET and TypeScript clients, the http-api/ site pages)
        // is built from this one file.
        await using var host = await AgentPrismTestHost.StartAsync(
            withOpenApi: true,
            configureEndpoints: options => options.EnableDiagnosticsEndpoint = true,
            // 🚨 The live voice routes are mapped only when UseLiveVoice() registered
            // the layer, so the document would silently omit three endpoints — and
            // with them the .NET client, the TypeScript client and the http-api/
            // site pages, all of which are generated from this one file.
            configureAgentPrism: static builder => builder
                .UseOpenAI(options => options.ApiKey = "openapi-placeholder-key")
                .UseOpenAILive()
                .UseLiveVoice(),
            configureServices: static services =>
            {
                services.Configure<AgentPrismImageOptions>(options =>
                {
                    options.Enabled = true;
                    options.Provider = "openapi";
                    options.Model = "image-1";
                });
                services.AddSingleton<IImageGenerator, OpenApiImageGenerator>();
            });

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.EnsureSuccessStatusCode();

        var document = JsonNode.Parse(await response.Content.ReadAsStringAsync())
            ?? throw new InvalidOperationException("The OpenAPI endpoint returned a JSON null.");

        // Descriptions come from XML doc comments, and the compiler writes the
        // documentation file with Environment.NewLine. A multi-line <summary> on
        // Windows therefore reaches the document as CRLF and is escaped as '\r\n'
        // INSIDE the JSON string value -- which the ReplaceLineEndings() call below
        // cannot reach, because it only sees the escape characters. Canonicalize the
        // values themselves before serializing.
        NormalizeStringLineEndings(document);

        // LF, NOT Environment.NewLine: the committed file is LF on every platform
        // ('.gitattributes' pins '* text=auto eol=lf'), and System.Text.Json's
        // indented writer defaults its line break to Environment.NewLine -- on
        // Windows that made EVERY line differ (4278 differences, measured on the
        // windows-latest CI leg) for a document that had not changed at all.
        return document.ToJsonString(WriteOptions).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>
    /// Rewrites CRLF and lone CR to LF in every string value of <paramref name="node"/>.
    /// </summary>
    /// <param name="node">The node to normalize in place.</param>
    private static void NormalizeStringLineEndings(JsonNode node)
    {
        switch (node)
        {
            case JsonObject document:
                foreach (var (name, child) in document.ToArray())
                {
                    if (child is null)
                    {
                        continue;
                    }

                    if (Normalized(child) is { } value)
                    {
                        document[name] = value;
                    }
                    else
                    {
                        NormalizeStringLineEndings(child);
                    }
                }

                break;

            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    if (array[index] is not { } child)
                    {
                        continue;
                    }

                    if (Normalized(child) is { } value)
                    {
                        array[index] = value;
                    }
                    else
                    {
                        NormalizeStringLineEndings(child);
                    }
                }

                break;
        }

        // Returns the LF form when the node is a string that carries a CR, null otherwise.
        // Only CR is touched: ReplaceLineEndings() would also rewrite form feed, vertical
        // tab and the Unicode separators, which are content rather than a platform artifact.
        static string? Normalized(JsonNode node)
        {
            if (node is not JsonValue value ||
                !value.TryGetValue<string>(out var text) ||
                !text.Contains('\r', StringComparison.Ordinal))
            {
                return null;
            }

            return text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string SnapshotPath { get; } =
        Path.Combine(RepositoryRoot, "docs", "openapi", "agentprism.json");

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgentPrism.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("AgentPrism.slnx not found.");
    }

    private sealed class OpenApiImageGenerator : IImageGenerator
    {
        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageGenerationResponse());

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
#pragma warning restore MEAI001
}

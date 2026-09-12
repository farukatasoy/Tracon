using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Closes the loop between a documented behavior claim and the code it
/// describes (F-171, phase 158). A marked claim names either an option's
/// default or an endpoint's required API-key scope, and this file is the ONLY
/// place either kind is actually evaluated: <c>check-content.mjs</c> can only
/// check that a marker's shape is well-formed, because that gate runs BEFORE
/// the solution is built and has no compiled assembly to reflect on and no
/// running host to send a request to (158.1).
/// </summary>
/// <remarks>
/// <para>
/// A marker is an HTML comment right next to the sentence it measures:
/// <c>&lt;!-- claim:option TypeName.PropertyName=value --&gt;</c> for an
/// option default, or <c>&lt;!-- claim:policy METHOD /path scope=ScopeName
/// --&gt;</c> for an endpoint's required scope (Open Question 1). Every marked
/// claim in this first round is a <see langword="bool"/> (158.3); a future
/// non-bool claim needs a small change to the value comparison below, not a
/// new mechanism.
/// </para>
/// </remarks>
public sealed class DocumentedPolicyTests
{
    /// <summary>
    /// Every option type a claim is allowed to name. Explicit rather than
    /// assembly-scanned: instantiating an arbitrary type by reflection should
    /// not be something a doc marker can opt into just by spelling a name
    /// right. Adding a type here is the deliberate step 158.3 describes.
    /// </summary>
    private static readonly Dictionary<string, Type> ClaimableOptionTypes = new(StringComparer.Ordinal)
    {
        [nameof(TraconSessionOwnershipOptions)] = typeof(TraconSessionOwnershipOptions),
        [nameof(TraconRetentionOptions)] = typeof(TraconRetentionOptions),
        [nameof(TraconSqliteOptions)] = typeof(TraconSqliteOptions),
        [nameof(TraconSchedulingOptions)] = typeof(TraconSchedulingOptions),
        [nameof(TraconMcpServerOptions)] = typeof(TraconMcpServerOptions),
        [nameof(TraconEndpointOptions)] = typeof(TraconEndpointOptions),
        [nameof(TraconImageOptions)] = typeof(TraconImageOptions),
        [nameof(TraconEgressOptions)] = typeof(TraconEgressOptions),
    };

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex ClaimPattern = new(
        @"<!--\s*claim:(?<kind>option|policy)\s+(?<payload>.+?)\s*-->",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex OptionPayloadPattern = new(
        @"^(?<type>[A-Za-z0-9_]+)\.(?<property>[A-Za-z0-9_]+)=(?<value>\S+)$",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex PolicyPayloadPattern = new(
        @"^(?<method>[A-Z]+)\s+(?<path>\S+)\s+scope=(?<scope>\S+)$",
        RegexOptions.Compiled,
        RegexTimeout);

    [Fact]
    public void Every_marked_option_default_matches_the_real_type()
    {
        var claims = FindClaims("option").ToList();

        claims.ShouldNotBeEmpty("no marked option claim was found; did every marker move or get removed?");

        var failures = new List<string>();

        foreach (var (file, payload) in claims)
        {
            var match = OptionPayloadPattern.Match(payload);

            if (!match.Success)
            {
                // check-content.mjs already fails a marker with the wrong shape;
                // this test does not need to fail a second time for one defect.
                continue;
            }

            var typeName = match.Groups["type"].Value;
            var propertyName = match.Groups["property"].Value;
            var expected = match.Groups["value"].Value;

            if (!ClaimableOptionTypes.TryGetValue(typeName, out var type))
            {
                failures.Add(
                    $"{file}: '{typeName}' is not registered in {nameof(ClaimableOptionTypes)} in " +
                    $"{nameof(DocumentedPolicyTests)} - add it there to make this claim checkable.");
                continue;
            }

            var property = type.GetProperty(propertyName)
                ?? throw new InvalidOperationException($"{type.Name} has no property named '{propertyName}'.");

            var instance = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"{type.Name} has no public parameterless constructor.");

            var actual = property.GetValue(instance);
            var actualText = actual switch
            {
                bool boolValue => boolValue ? "true" : "false",
                null => "null",
                _ => actual.ToString(),
            };

            if (!string.Equals(actualText, expected, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"{file}: claims {typeName}.{propertyName}={expected}, but the real default is {actualText}.");
            }
        }

        failures.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_marked_endpoint_policy_claim_is_actually_enforced()
    {
        var claims = FindClaims("policy").ToList();

        claims.ShouldNotBeEmpty("no marked policy claim was found; did the marker move or get removed?");

        foreach (var (file, payload) in claims)
        {
            var match = PolicyPayloadPattern.Match(payload);

            if (!match.Success)
            {
                continue;
            }

            var method = match.Groups["method"].Value;
            var path = match.Groups["path"].Value;
            var scope = Enum.Parse<ApiKeyScope>(match.Groups["scope"].Value);
            var otherScope = scope == ApiKeyScope.RunsWrite ? ApiKeyScope.RunsRead : ApiKeyScope.RunsWrite;

            await using var host = await TraconTestHost.StartAsync(
                static builder => builder.AddAgent(TestData.Definition()));

            var wrongScopeKey = await ApiKeyEndpointTests.CreateKeyAsync(host, "wrong-scope", otherScope.ToString());
            using var deniedResponse = await SendAsync(host, method, path, wrongScopeKey.PlaintextKey);

            deniedResponse.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                $"{file} claims {method} {path} requires {scope}, but a key scoped only to {otherScope} was not refused.");

            var rightScopeKey = await ApiKeyEndpointTests.CreateKeyAsync(host, "right-scope", scope.ToString());
            using var allowedResponse = await SendAsync(host, method, path, rightScopeKey.PlaintextKey);

            allowedResponse.StatusCode.ShouldNotBe(
                HttpStatusCode.Forbidden,
                $"{file} claims {method} {path} requires {scope}, but a key carrying exactly that scope was refused.");
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(TraconTestHost host, string method, string path, string apiKey)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { model = "kod-agent", messages = new[] { new { role = "user", content = "hello" } } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        return await host.Client.SendAsync(request);
    }

    private static IEnumerable<(string File, string Payload)> FindClaims(string kind)
    {
        foreach (var file in Directory.EnumerateFiles(DocsRoot, "*.md", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(DocsRoot, "*.mdx", SearchOption.AllDirectories)))
        {
            // The generated API and HTTP reference trees are not hand-written and
            // do not exist before the solution is built; skip them the same way
            // check-content.mjs does.
            var relative = Path.GetRelativePath(DocsRoot, file);

            if (relative.StartsWith("api" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || relative.StartsWith("http-api" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (Match match in ClaimPattern.Matches(text))
            {
                if (string.Equals(match.Groups["kind"].Value, kind, StringComparison.Ordinal))
                {
                    yield return (relative, match.Groups["payload"].Value);
                }
            }
        }
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string DocsRoot { get; } = Path.Combine(RepositoryRoot, "docs-site", "src", "content", "docs");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}'.");
    }
}

using System.Text.Json;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every write operation either declares its request body or is registered as
/// deliberately body-less.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The trap this closes is already recorded in
/// <c>docs/hafiza/aspnetcore-json.md</c>: turning a typed body parameter
/// (<c>T request</c>) into <c>HttpContext</c> plus a hand-read SILENTLY drops
/// the operation's <c>requestBody</c> metadata. .NET derives the body schema
/// from the endpoint's PARAMETER TYPE; remove the parameter and there is
/// nothing left to derive it from. Nothing warns: the build stays green, the
/// endpoint keeps working over raw HTTP, and only the generated typed client
/// loses the ability to send a body at all.
/// </para>
/// <para>
/// It happened twice more after that note was written. <c>/v1/responses</c> and
/// <c>/v1/chat/completions</c> read a JSON body by hand and declared none, so
/// both generated clients produced methods with no body parameter — the two
/// endpoints were unreachable from the typed client entirely, not merely for
/// their streaming shape.
/// </para>
/// <para>
/// The burden is deliberately inverted. Rather than trying to detect which
/// handler reads a body — a source-parsing problem with no reliable answer —
/// every body-less write operation must be REGISTERED with a reason. A new
/// endpoint that forgets <c>.Accepts&lt;T&gt;</c> is therefore red by default,
/// and an endpoint that genuinely takes no body costs one line to declare.
/// </para>
/// </remarks>
public sealed class DeclaredRequestBodyTests
{
    /// <summary>
    /// Write operations that legitimately declare no request body, and why.
    /// </summary>
    private static readonly Dictionary<string, string> BodyLessByDesign = new(StringComparer.Ordinal)
    {
        ["POST /tracon/api/runs/{runId}/cancel"] = "Action route: the path names the target; no body.",
        ["POST /tracon/api/jobs/{id}/cancel"] = "Action route: the path names the target; no body.",
        ["POST /tracon/api/runs/{runId}/judge"] = "Action route: the judges are registered; no body.",
        ["POST /tracon/api/experiments/{name}/start"] = "Action route: a state transition; no body.",
        ["POST /tracon/api/experiments/{name}/stop"] = "Action route: a state transition; no body.",
        ["POST /tracon/api/stats/recalculate-costs"] = "Action route: recomputes for the whole tenant; no body.",
        ["POST /tracon/api/webhooks/{name}/test"] = "Action route: sends a probe to the registered target; no body.",
        ["POST /tracon/api/retention/run"] = "Action route: runs the configured policy; no body.",
        ["POST /tracon/api/mcp-servers/{name}/oauth/start"] = "Action route: starts authorization; no body.",
        ["POST /tracon/v1/conversations"] = "Reserves an identifier only; writes nothing and reads no body.",

        // 🚨 This route DOES read a body, and having no schema for it is
        // deliberate: the payload belongs to a third party (Slack, GitHub, …),
        // is not required to be JSON at all, and the HMAC is computed over the
        // RAW bytes. Declaring a schema would be a promise we cannot keep. Its
        // caller is an external system; the typed client never calls it.
        ["POST /tracon/api/triggers/{tenantId}/{name}"] = "Schemaless raw payload; HMAC over the raw body.",
    };

    [Fact]
    public void Every_write_operation_declares_its_body_or_is_registered_as_body_less()
    {
        var document = JsonDocument.Parse(File.ReadAllText(OpenApiPath));
        var undeclared = new List<string>();

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!WriteMethods.Contains(operation.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (operation.Value.TryGetProperty("requestBody", out _))
                {
                    continue;
                }

                var key = $"{operation.Name.ToUpperInvariant()} {path.Name}";

                if (!BodyLessByDesign.ContainsKey(key))
                {
                    undeclared.Add(key);
                }
            }
        }

        undeclared.ShouldBeEmpty(
            $"These write operations declare no request body: {string.Join(", ", undeclared)}. " +
            "If the handler reads a body, add `.Accepts<T>(\"application/json\")` to the route — " +
            "removing a typed body parameter drops the metadata silently and the generated " +
            "clients lose the body parameter entirely. If it genuinely takes no body, add it to " +
            "BodyLessByDesign here with the reason.");
    }

    /// <summary>
    /// The registry may not outlive its entries: a stale key hides the next
    /// endpoint that reuses that route.
    /// </summary>
    [Fact]
    public void The_body_less_registry_names_no_operation_that_stopped_existing()
    {
        var document = JsonDocument.Parse(File.ReadAllText(OpenApiPath));
        var live = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (WriteMethods.Contains(operation.Name, StringComparer.OrdinalIgnoreCase))
                {
                    live.Add($"{operation.Name.ToUpperInvariant()} {path.Name}");
                }
            }
        }

        var stale = BodyLessByDesign.Keys.Where(key => !live.Contains(key)).ToList();

        stale.ShouldBeEmpty(
            $"BodyLessByDesign names operations the document no longer has: {string.Join(", ", stale)}. " +
            "Remove the entries, or fix the route if it was renamed.");
    }

    private static readonly string[] WriteMethods = ["post", "put", "patch"];

    // 🚨 Declaration order matters: static field initialisers run in order, so
    // `RepositoryRoot` must come BEFORE `OpenApiPath`. The other way round
    // throws `TypeInitializationException` — the same trap is recorded in
    // `ShippedDocumentationSelfContainmentTests`.
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string OpenApiPath { get; } = Path.Combine(
        RepositoryRoot, "docs", "openapi", "tracon.json");

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

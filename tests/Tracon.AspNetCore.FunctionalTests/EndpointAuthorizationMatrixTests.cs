using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Every endpoint that declares an API key scope is called with a caller that lacks what it declares, and refuses it.
/// </summary>
/// <remarks>
/// <para>
/// The operations come from the endpoint data source, not from a list, so an endpoint added later is probed without
/// anyone naming it. The expectation is one-directional on purpose: a caller short of the declared scope, role,
/// credential or tenant must get exactly the refusal, and the caller who has what is declared must not be refused.
/// An endpoint that checks its caller by hand and skips the shared filter answers the short caller with something
/// else and shows up here; that was the shape of the voice socket that accepted any scope.
/// </para>
/// <para>
/// The scope and tenant checks live in an endpoint filter, which ASP.NET Core runs AFTER parameter binding. A body the
/// binder rejects (a missing <c>required</c> member, a wrong content type) answers 400 or 415 before the filter looks
/// at the caller, so such a request proves nothing. Those answers fail the test instead of passing quietly: add the
/// route to <see cref="BodyOverrides"/> with a body that binds.
/// </para>
/// </remarks>
public sealed partial class EndpointAuthorizationMatrixTests
{
    /// <summary>The tenant every request belongs to unless a test says otherwise.</summary>
    private const string OwnTenant = "default";

    internal const string RoleHeader = "X-Test-Role";

    /// <summary>Makes the test scheme leave the caller unauthenticated, so a policy answers 401 instead of 403.</summary>
    internal const string AnonymousHeader = "X-Test-Anonymous";

    private const string Json = "application/json";

    /// <summary>
    /// The only two endpoints that change nothing yet use POST, so a Reader policy is right for them. Any other
    /// mutating endpoint that carries only the Reader policy is a role gap.
    /// </summary>
    private static readonly HashSet<string> ReadingPosts = new(StringComparer.Ordinal)
    {
        // Prices a run without starting one.
        "POST /tracon/api/agents/{name}/estimate",
        // The query is too long and too structured for a query string; the search reads and writes nothing.
        "POST /tracon/api/knowledge/{collection}/search",
    };

    /// <summary>
    /// Routes whose body must bind before the filter can judge the caller. Every other mutating route gets <c>{}</c>.
    /// </summary>
    private static readonly Dictionary<string, Func<HttpContent>> BodyOverrides = new(StringComparer.Ordinal)
    {
        ["POST /tracon/api/attachments"] = static () => new MultipartFormDataContent
        {
            { new ByteArrayContent([0x89, 0x50, 0x4E, 0x47]), "file", "probe.png" },
        },
        ["POST /tracon/api/voice/live/sessions"] = static () => new StringContent(
            """{"sessionId":"probe","agent":"probe","sdp":"v=0"}""",
            Encoding.UTF8,
            Json),
    };

    /// <summary>The role a caller must carry to satisfy each policy. One role per policy, so a wrong policy shows.</summary>
    private static readonly Dictionary<string, string> RoleOfPolicy = new(StringComparer.Ordinal)
    {
        [TraconPolicies.Reader] = "reader",
        [TraconPolicies.Operator] = "operator",
        [TraconPolicies.Admin] = "admin",
        [TraconPolicies.PlatformAdmin] = "platform-admin",
    };

    [Fact]
    public async Task Every_scoped_endpoint_declares_a_role_policy()
    {
        await using var host = await StartRoleHostAsync();

        var operations = ScopedOperations(host);

        operations.Count.ShouldBeGreaterThan(150, "the surface lists too few scoped operations for the matrix to mean anything");
        operations
            .Where(static operation => operation.Policy is null)
            .Select(static operation => operation.Label)
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task A_mutating_endpoint_is_not_reader_only_unless_it_only_reads()
    {
        await using var host = await StartRoleHostAsync();

        var operations = ScopedOperations(host);

        operations
            .Where(static operation => !string.Equals(operation.Method, "GET", StringComparison.Ordinal)
                && string.Equals(operation.Policy, TraconPolicies.Reader, StringComparison.Ordinal)
                && !ReadingPosts.Contains(operation.Label))
            .Select(static operation => operation.Label)
            .ShouldBeEmpty();

        // An exemption whose route moved or lost its Reader policy no longer exempts anything; say so.
        ReadingPosts
            .Where(label => !operations.Any(operation =>
                string.Equals(operation.Label, label, StringComparison.Ordinal)
                && string.Equals(operation.Policy, TraconPolicies.Reader, StringComparison.Ordinal)))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task A_key_missing_the_declared_scope_is_refused_by_every_endpoint()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync();

        var scopes = Enum.GetValues<ApiKeyScope>();
        var onlyKeys = new Dictionary<ApiKeyScope, string>();
        var allButKeys = new Dictionary<ApiKeyScope, string>();

        foreach (var scope in scopes)
        {
            onlyKeys[scope] = (await ApiKeyEndpointTests.CreateKeyAsync(host, "only-" + scope, scope.ToString())).PlaintextKey;

            // The strongest wrong key: every authority except the one the endpoint asks for.
            allButKeys[scope] = (await ApiKeyEndpointTests.CreateKeyAsync(
                host,
                "all-but-" + scope,
                [.. scopes.Where(candidate => candidate != scope).Select(static candidate => candidate.ToString())])).PlaintextKey;
        }

        var operations = ScopedOperations(host);
        var notRefused = new List<string>();
        var refusedTheRightKey = new List<string>();

        foreach (var operation in operations)
        {
            var wrong = await SendAsync(host, operation, OwnTenant, bearer: allButKeys[operation.Scope]);

            if (wrong != HttpStatusCode.Forbidden)
            {
                notRefused.Add($"{operation.Label} -> {(int)wrong} for a key holding every scope but {operation.Scope}");
            }

            var right = await SendAsync(host, operation, OwnTenant, bearer: onlyKeys[operation.Scope]);

            if (right is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                refusedTheRightKey.Add($"{operation.Label} -> {(int)right} for a key holding {operation.Scope}");
            }
        }

        operations.Count.ShouldBeGreaterThan(150);
        notRefused.ShouldBeEmpty();
        refusedTheRightKey.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_key_cannot_act_for_another_tenant_through_the_header_on_any_endpoint()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync(
            configureMoreTracon: static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var key = (await host.Services.GetRequiredService<IApiKeyStore>().CreateAsync(
            new ApiKeyDraft { TenantId = "tenant-a", Name = "all-scopes", Scopes = Enum.GetValues<ApiKeyScope>() },
            TestContext.Current.CancellationToken)).PlaintextKey;

        var operations = ScopedOperations(host);
        var notRefused = new List<string>();
        var refusedOwnTenant = new List<string>();

        foreach (var operation in operations)
        {
            var foreign = await SendAsync(host, operation, "tenant-a", bearer: key, tenantHeader: "tenant-b");

            if (foreign != HttpStatusCode.Forbidden)
            {
                notRefused.Add($"{operation.Label} -> {(int)foreign} for a tenant-a key naming tenant-b");
            }

            var own = await SendAsync(host, operation, "tenant-a", bearer: key, tenantHeader: "tenant-a");

            if (own is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                refusedOwnTenant.Add($"{operation.Label} -> {(int)own} for a tenant-a key naming tenant-a");
            }
        }

        operations.Count.ShouldBeGreaterThan(150);
        notRefused.ShouldBeEmpty();
        refusedOwnTenant.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_caller_without_the_declared_role_is_refused_by_every_endpoint()
    {
        await using var host = await StartRoleHostAsync();

        var operations = ScopedOperations(host).Where(static operation => operation.Policy is not null).ToList();
        var notRefused = new List<string>();
        var refusedTheRole = new List<string>();

        foreach (var operation in operations)
        {
            var declared = RoleOfPolicy[operation.Policy!];

            foreach (var role in (string?[])[null, .. RoleOfPolicy.Values])
            {
                var status = await SendAsync(host, operation, OwnTenant, role: role);

                if (string.Equals(role, declared, StringComparison.Ordinal))
                {
                    if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        refusedTheRole.Add($"{operation.Label} -> {(int)status} for role {role}");
                    }
                }
                else if (status != HttpStatusCode.Forbidden)
                {
                    notRefused.Add($"{operation.Label} ({operation.Policy}) -> {(int)status} for role {role ?? "none"}");
                }
            }
        }

        operations.Count.ShouldBeGreaterThan(150);
        notRefused.ShouldBeEmpty();
        refusedTheRole.ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_role_refusal_carries_a_body_in_the_shape_its_endpoint_speaks()
    {
        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync(
            configureMoreServices: AddRoleScheme,
            configureMoreTracon: static builder => builder.UseProblemDetailsOnDenial());

        var operations = ScopedOperations(host).Where(static operation => operation.Policy is not null).ToList();
        var wrong = new List<string>();

        foreach (var operation in operations)
        {
            var forbidden = await SendForRefusalAsync(host, operation, role: "nobody");
            var unauthorized = await SendForRefusalAsync(host, operation, anonymous: true);

            Judge(wrong, operation, "a caller with the wrong role", forbidden, HttpStatusCode.Forbidden, ProblemDetailsOnDenialHandler.ForbiddenTitle);
            Judge(wrong, operation, "an unauthenticated caller", unauthorized, HttpStatusCode.Unauthorized, ProblemDetailsOnDenialHandler.UnauthorizedTitle);
        }

        operations.Count.ShouldBeGreaterThan(150);

        // The mark decides the shape, so a mark that goes missing would move its route to the other set quietly.
        operations.Count(static operation => operation.AnswersInOpenAiShape).ShouldBe(6);
        operations.Count(static operation => !operation.AnswersInOpenAiShape).ShouldBeGreaterThan(140);
        wrong.ShouldBeEmpty();

        static void Judge(List<string> wrong, Operation operation, string caller, Refusal reply, HttpStatusCode status, string title)
        {
            var openAi = operation.AnswersInOpenAiShape;

            if (reply.Status != status)
            {
                wrong.Add($"{operation.Label} -> {(int)reply.Status} for {caller}, expected {(int)status}");
                return;
            }

            var expectedType = openAi ? "application/json" : "application/problem+json";

            if (!string.Equals(reply.ContentType, expectedType, StringComparison.Ordinal))
            {
                wrong.Add($"{operation.Label} -> {(int)status} for {caller} has content type '{reply.ContentType}', expected '{expectedType}'");
                return;
            }

            var body = System.Text.Json.Nodes.JsonNode.Parse(reply.Body);
            var good = openAi
                ? string.Equals(
                      (string?)body?["error"]?["type"],
                      status == HttpStatusCode.Unauthorized
                          ? ProblemDetailsOnDenialHandler.OpenAIUnauthorizedType
                          : ProblemDetailsOnDenialHandler.OpenAIForbiddenType,
                      StringComparison.Ordinal)
                : string.Equals((string?)body?["title"], title, StringComparison.Ordinal) && (int?)body?["status"] == (int)status;

            if (!good)
            {
                wrong.Add($"{operation.Label} -> {(int)status} for {caller} has the body '{reply.Body}'");
            }
        }
    }

    [Fact]
    public async Task No_scoped_endpoint_answers_a_caller_without_the_configured_token()
    {
        const string Token = "matrix-static-token";

        await using var host = await OpenApiSnapshotTests.StartFullSurfaceHostAsync(
            configureMoreEndpoints: static options => options.AuthToken = Token);

        var operations = ScopedOperations(host);
        var notRefused = new List<string>();
        var refusedTheToken = new List<string>();

        foreach (var operation in operations)
        {
            var anonymous = await SendAsync(host, operation, OwnTenant);
            var wrong = await SendAsync(host, operation, OwnTenant, bearer: "not-the-token");
            var right = await SendAsync(host, operation, OwnTenant, bearer: Token);

            if (anonymous != HttpStatusCode.Unauthorized)
            {
                notRefused.Add($"{operation.Label} -> {(int)anonymous} without a credential");
            }

            if (wrong != HttpStatusCode.Unauthorized)
            {
                notRefused.Add($"{operation.Label} -> {(int)wrong} with a wrong token");
            }

            if (right is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                refusedTheToken.Add($"{operation.Label} -> {(int)right} with the configured token");
            }
        }

        operations.Count.ShouldBeGreaterThan(150);
        notRefused.ShouldBeEmpty();
        refusedTheToken.ShouldBeEmpty();
    }

    private static Task<TraconTestHost> StartRoleHostAsync()
        => OpenApiSnapshotTests.StartFullSurfaceHostAsync(AddRoleScheme);

    private static void AddRoleScheme(IServiceCollection services)
    {
        services
            .AddAuthentication(RoleHeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, RoleHeaderAuthenticationHandler>(
                RoleHeaderAuthenticationHandler.SchemeName,
                configureOptions: null);

        var authorization = services.AddAuthorizationBuilder();

        foreach (var (policy, role) in RoleOfPolicy)
        {
            authorization.AddPolicy(policy, builder => builder.RequireRole(role));
        }
    }

    /// <summary>Lists every method and route that declares an API key scope.</summary>
    private static List<Operation> ScopedOperations(TraconTestHost host)
    {
        var operations = new List<Operation>();

        foreach (var endpoint in host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<ApiKeyScopeRequirement>() is not { } requirement)
            {
                continue;
            }

            var policy = endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Select(static data => data.Policy)
                .FirstOrDefault(static name => name is not null && RoleOfPolicy.ContainsKey(name));

            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
            {
                if (method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE")
                {
                    operations.Add(new Operation(
                        method,
                        endpoint.RoutePattern.RawText ?? string.Empty,
                        requirement.Scope,
                        policy,
                        endpoint.Metadata.GetMetadata<OpenAIDenialShapeMarker>() is not null));
                }
            }
        }

        return operations;
    }

    private static async Task<HttpStatusCode> SendAsync(
        TraconTestHost host,
        Operation operation,
        string tenantParameter,
        string? bearer = null,
        string? role = null,
        string? tenantHeader = null)
    {
        using var request = new HttpRequestMessage(
            new HttpMethod(operation.Method),
            new Uri(Fill(operation.Template, tenantParameter), UriKind.Relative));

        if (operation.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = BodyOverrides.TryGetValue(operation.Label, out var body)
                ? body()
                : new StringContent("{}", Encoding.UTF8, Json);
        }

        if (bearer is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        if (role is not null)
        {
            request.Headers.Add(RoleHeader, role);
        }

        if (tenantHeader is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenantHeader);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        try
        {
            // Headers are enough: a stream that never ends must not hold the matrix.
            using var reply = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            return reply.StatusCode;
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            // Never answered: the handler ran. Reported as a timeout so a refusal check does not mistake it for a 403.
            return HttpStatusCode.RequestTimeout;
        }
    }

    /// <summary>Sends the request of <paramref name="operation"/> and reads the body of the refusal it gets.</summary>
    private static async Task<Refusal> SendForRefusalAsync(TraconTestHost host, Operation operation, string? role = null, bool anonymous = false)
    {
        using var request = new HttpRequestMessage(
            new HttpMethod(operation.Method),
            new Uri(Fill(operation.Template, OwnTenant), UriKind.Relative));

        if (operation.Method is "POST" or "PUT" or "PATCH")
        {
            request.Content = BodyOverrides.TryGetValue(operation.Label, out var body)
                ? body()
                : new StringContent("{}", Encoding.UTF8, Json);
        }

        if (role is not null)
        {
            request.Headers.Add(RoleHeader, role);
        }

        if (anonymous)
        {
            request.Headers.Add(AnonymousHeader, "1");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        using var reply = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

        // Only a refusal is read to its end: any other answer may be a stream that never finishes.
        var text = reply.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? await reply.Content.ReadAsStringAsync(timeout.Token)
            : string.Empty;

        return new Refusal(reply.StatusCode, reply.Content.Headers.ContentType?.MediaType, text);
    }

    /// <summary>Replaces each route parameter with a value its constraint accepts.</summary>
    internal static string Fill(string template, string tenantParameter)
        => ParameterPattern().Replace(
            template,
            match =>
            {
                if (string.Equals(match.Groups["name"].Value, "tenantId", StringComparison.Ordinal))
                {
                    return tenantParameter;
                }

                var constraint = match.Groups["constraint"].Value;

                if (constraint.Contains("guid", StringComparison.OrdinalIgnoreCase))
                {
                    return Guid.NewGuid().ToString();
                }

                return constraint.Contains("int", StringComparison.OrdinalIgnoreCase)
                    || constraint.Contains("long", StringComparison.OrdinalIgnoreCase)
                    ? 1.ToString(CultureInfo.InvariantCulture)
                    : "probe";
            });

    [GeneratedRegex(@"\{\*{0,2}(?<name>\w+)(?<constraint>:[^}?]+)?\??\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ParameterPattern();

    /// <summary>What a refusal looked like.</summary>
    /// <param name="Status">The status code.</param>
    /// <param name="ContentType">The media type of the body, or <see langword="null"/>.</param>
    /// <param name="Body">The body text.</param>
    private sealed record Refusal(HttpStatusCode Status, string? ContentType, string Body);

    /// <summary>One method on one route, with what it declares.</summary>
    /// <param name="Method">The HTTP method.</param>
    /// <param name="Template">The route template.</param>
    /// <param name="Scope">The API key scope the route declares.</param>
    /// <param name="Policy">The Tracon role policy the route declares, or <see langword="null"/> when it declares none.</param>
    /// <param name="AnswersInOpenAiShape">Whether the route is marked to answer a refused role in the OpenAI error shape.</param>
    private sealed record Operation(string Method, string Template, ApiKeyScope Scope, string? Policy, bool AnswersInOpenAiShape = false)
    {
        public string Label => $"{Method} {Template}";
    }

    /// <summary>Authenticates every request as a user whose only role is the one the test header names.</summary>
    internal sealed class RoleHeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "RoleHeader";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.ContainsKey(AnonymousHeader))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim> { new(ClaimTypes.Name, "matrix-user") };

            if (Request.Headers.TryGetValue(RoleHeader, out var role) && role.ToString() is { Length: > 0 } name)
            {
                claims.Add(new Claim(ClaimTypes.Role, name));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}

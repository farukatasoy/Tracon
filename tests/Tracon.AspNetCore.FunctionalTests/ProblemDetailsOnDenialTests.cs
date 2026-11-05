using System.IO.Pipelines;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A refused role or policy check answers with a <c>ProblemDetails</c> body once the host asked for it, and with
/// exactly what it answered before when it did not.
/// </summary>
/// <remarks>
/// The answer to a refusal is written by the ASP.NET Core authorization middleware, which sits between the
/// route match and Tracon's own endpoint filter. Every claim here crosses that boundary (DI order, the HTTP
/// pipeline, an authentication scheme, a tenant), so none is proven by a unit test of the handler alone.
/// </remarks>
public sealed class ProblemDetailsOnDenialTests
{
    private const string RoleHeader = "X-Test-Role";

    private const string ChallengeHeader = "X-Test-Challenge";

    /// <summary>The one role the test policies accept. Distinctive, so a body that echoes it is found.</summary>
    private const string AcceptedRole = "canary-role-7f3a";

    private const string UserName = "canary-user-9c1d";

    private const string CredentialValue = "canary-token-5e2b";

    private const string ReadPath = "/tracon/api/agents";

    private const string ProblemJson = "application/problem+json";

    [Fact]
    public async Task A_refused_role_answers_403_with_a_problem_body()
    {
        await using var host = await StartAsync(optIn: true);

        using var reply = await SendAsync(host, ReadPath, role: "some-other-role");

        reply.Status.ShouldBe(HttpStatusCode.Forbidden);
        reply.ContentType.ShouldBe(ProblemJson);

        var body = JsonNode.Parse(reply.Body)!;
        ((int)body["status"]!).ShouldBe(403);
        ((string)body["title"]!).ShouldBe(ProblemDetailsOnDenialHandler.ForbiddenTitle);
        ((string)body["detail"]!).ShouldBe(ProblemDetailsOnDenialHandler.ForbiddenDetail);
    }

    [Fact]
    public async Task An_unauthenticated_caller_answers_401_with_a_problem_body_and_keeps_the_scheme_challenge()
    {
        await using var host = await StartAsync(optIn: true);

        using var reply = await SendAsync(host, ReadPath);

        reply.Status.ShouldBe(HttpStatusCode.Unauthorized);
        reply.ContentType.ShouldBe(ProblemJson);
        reply.WwwAuthenticate.ShouldBe("Bearer realm=\"test\"");

        var body = JsonNode.Parse(reply.Body)!;
        ((int)body["status"]!).ShouldBe(401);
        ((string)body["title"]!).ShouldBe(ProblemDetailsOnDenialHandler.UnauthorizedTitle);
    }

    [Fact]
    public async Task Without_the_call_a_refusal_stays_empty()
    {
        await using var host = await StartAsync(optIn: false);

        using var forbidden = await SendAsync(host, ReadPath, role: "some-other-role");
        using var unauthorized = await SendAsync(host, ReadPath);

        forbidden.Status.ShouldBe(HttpStatusCode.Forbidden);
        forbidden.ContentType.ShouldBeNull();
        forbidden.Body.ShouldBeEmpty();

        unauthorized.Status.ShouldBe(HttpStatusCode.Unauthorized);
        unauthorized.ContentType.ShouldBeNull();
        unauthorized.Body.ShouldBeEmpty();
        unauthorized.WwwAuthenticate.ShouldBe("Bearer realm=\"test\"");
    }

    [Fact]
    public async Task The_caller_who_has_the_role_is_not_refused()
    {
        await using var host = await StartAsync(optIn: true);

        using var reply = await SendAsync(host, ReadPath, role: AcceptedRole);

        reply.Status.ShouldBe(HttpStatusCode.OK);
        string.Equals(reply.ContentType, ProblemJson, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task A_cookie_scheme_answers_what_it_answered_before()
    {
        static void AddCookie(IServiceCollection services)
            => services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();

        await using var plain = await StartAsync(optIn: false, configureServices: AddCookie, useTestScheme: false);
        await using var wrapped = await StartAsync(optIn: true, configureServices: AddCookie, useTestScheme: false);

        using var before = await SendAsync(plain, ReadPath);
        using var after = await SendAsync(wrapped, ReadPath);

        // Whatever the cookie scheme does for this endpoint (a redirect, or a bare 401 for an API endpoint) it still does.
        after.Status.ShouldBe(before.Status);
        after.Location.ShouldBe(before.Location);

        if (after.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            after.ContentType.ShouldBe(ProblemJson);
        }
        else
        {
            after.Body.ShouldBeEmpty();
            after.ContentType.ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_redirecting_challenge_keeps_its_redirect_and_gets_no_body()
    {
        await using var host = await StartAsync(optIn: true);

        using var request = new HttpRequestMessage(HttpMethod.Get, ReadPath);
        request.Headers.Add(ChallengeHeader, "redirect");

        using var reply = await Read(await host.Client.SendAsync(request, TestContext.Current.CancellationToken));

        reply.Status.ShouldBe(HttpStatusCode.Redirect);
        reply.Location.ShouldBe("https://idp.example/login");
        reply.Body.ShouldBeEmpty();
        reply.ContentType.ShouldBeNull();
    }

    [Fact]
    public async Task The_body_is_the_same_for_every_tenant_and_names_nothing_of_the_caller()
    {
        await using var host = await StartAsync(
            optIn: true,
            configureTracon: static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        using var first = await SendAsync(host, ReadPath, role: "some-other-role", tenant: "tenant-alpha-31", credential: CredentialValue);
        using var second = await SendAsync(host, ReadPath, role: "some-other-role", tenant: "tenant-beta-77", credential: CredentialValue);
        using var anonymousFirst = await SendAsync(host, ReadPath, tenant: "tenant-alpha-31");
        using var anonymousSecond = await SendAsync(host, ReadPath, tenant: "tenant-beta-77");

        first.Status.ShouldBe(HttpStatusCode.Forbidden);
        second.Status.ShouldBe(HttpStatusCode.Forbidden);
        WithoutTraceId(first.Body).ShouldBe(WithoutTraceId(second.Body));
        WithoutTraceId(anonymousFirst.Body).ShouldBe(WithoutTraceId(anonymousSecond.Body));

        foreach (var body in new[] { first.Body, second.Body, anonymousFirst.Body, anonymousSecond.Body })
        {
            body.ShouldNotContain("tenant-alpha-31");
            body.ShouldNotContain("tenant-beta-77");
            body.ShouldNotContain(AcceptedRole);
            body.ShouldNotContain("some-other-role");
            body.ShouldNotContain(UserName);
            body.ShouldNotContain(CredentialValue);
            body.ShouldNotContain(TraconPolicies.Reader);
        }
    }

    [Fact]
    public async Task A_consumer_endpoint_keeps_its_empty_refusal()
    {
        await using var host = await StartAsync(
            optIn: true,
            configureApp: static app => app.MapGet("/own", static () => "mine").RequireAuthorization(TraconPolicies.Reader));

        using var forbidden = await SendAsync(host, "/own", role: "some-other-role");
        using var unauthorized = await SendAsync(host, "/own");

        forbidden.Status.ShouldBe(HttpStatusCode.Forbidden);
        forbidden.Body.ShouldBeEmpty();
        forbidden.ContentType.ShouldBeNull();
        unauthorized.Status.ShouldBe(HttpStatusCode.Unauthorized);
        unauthorized.Body.ShouldBeEmpty();
        unauthorized.ContentType.ShouldBeNull();
    }

    [Fact]
    public async Task An_openai_compatible_endpoint_refuses_in_the_openai_error_shape()
    {
        await using var host = await StartAsync(optIn: true);

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Post, "/tracon/v1/chat/completions"),
            (HttpMethod.Post, "/tracon/v1/responses"),
            (HttpMethod.Get, "/tracon/v1/conversations/conv_probe"),
        })
        {
            using var forbidden = await SendAsync(host, path, role: "some-other-role", method: method);
            using var unauthorized = await SendAsync(host, path, method: method);

            forbidden.Status.ShouldBe(HttpStatusCode.Forbidden, path);
            forbidden.ContentType.ShouldBe("application/json", path);
            var error = JsonNode.Parse(forbidden.Body)!["error"]!;
            ((string)error["type"]!).ShouldBe(ProblemDetailsOnDenialHandler.OpenAIForbiddenType, path);
            ((string)error["message"]!).ShouldBe(ProblemDetailsOnDenialHandler.ForbiddenDetail, path);

            unauthorized.Status.ShouldBe(HttpStatusCode.Unauthorized, path);
            ((string)JsonNode.Parse(unauthorized.Body)!["error"]!["type"]!).ShouldBe(ProblemDetailsOnDenialHandler.OpenAIUnauthorizedType, path);
        }
    }

    [Fact]
    public async Task The_openai_sdk_raises_a_refused_role_with_the_server_message()
    {
        await using var host = await StartAsync(optIn: true);

        var chat = OpenAiSdkClients.Chat(host, "probe");

        var failure = await Should.ThrowAsync<System.ClientModel.ClientResultException>(
            async () => await chat.CompleteChatAsync([new OpenAI.Chat.UserChatMessage("hi")], cancellationToken: TestContext.Current.CancellationToken));

        failure.Status.ShouldBe((int)HttpStatusCode.Unauthorized);
        failure.Message.ShouldContain(ProblemDetailsOnDenialHandler.UnauthorizedDetail, Case.Sensitive);
    }

    [Fact]
    public async Task An_event_stream_request_is_refused_with_a_problem_and_never_opens_the_stream()
    {
        await using var host = await StartAsync(optIn: true);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/tracon/api/agents/probe/run")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add(RoleHeader, "some-other-role");

        using var reply = await Read(await host.Client.SendAsync(request, TestContext.Current.CancellationToken));

        reply.Status.ShouldBe(HttpStatusCode.Forbidden);
        reply.ContentType.ShouldBe(ProblemJson);
    }

    [Fact]
    public async Task A_cors_preflight_is_not_refused()
    {
        await using var host = await StartAsync(
            optIn: true,
            configureEndpoints: static options => options.AllowedOrigins.Add("https://console.example"));

        using var request = new HttpRequestMessage(HttpMethod.Options, ReadPath);
        request.Headers.Add("Origin", "https://console.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var reply = await Read(await host.Client.SendAsync(request, TestContext.Current.CancellationToken));

        reply.Status.ShouldNotBe(HttpStatusCode.Forbidden);
        reply.Status.ShouldNotBe(HttpStatusCode.Unauthorized);
        string.Equals(reply.ContentType, ProblemJson, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task A_voice_handshake_is_refused_with_a_problem_and_the_socket_client_sees_the_status()
    {
        await using var host = await StartAsync(
            optIn: true,
            configureTracon: static builder => builder.UseVoiceConversation(static options => options.OutputMediaType = "audio/mpeg"),
            configureServices: static services =>
            {
                var provider = new StubVoiceProvider();
                services.AddSingleton<ISpeechTranscriber>(provider);
                services.AddSingleton<ISpeechSynthesizer>(provider);
            });

        const string Path = "/tracon/api/voice/sessions/session-1/stream";

        using var plain = await SendAsync(host, Path, role: "some-other-role");

        plain.Status.ShouldBe(HttpStatusCode.Forbidden);
        plain.ContentType.ShouldBe(ProblemJson);

        var socket = host.CreateWebSocketClient();
        socket.SubProtocols.Add(VoiceConversationProtocol.SubProtocol);
        socket.ConfigureRequest = static request => request.Headers[RoleHeader] = "some-other-role";

        var connect = () => socket.ConnectAsync(new Uri("http://localhost" + Path), TestContext.Current.CancellationToken);

        var failure = await connect.ShouldThrowAsync<InvalidOperationException>();
        failure.Message.ShouldContain("403");
    }

    [Fact]
    public async Task A_body_that_cannot_be_written_leaves_the_refusal_standing_and_is_logged()
    {
        await using var host = await StartAsync(optIn: true);

        using var request = new HttpRequestMessage(HttpMethod.Get, ReadPath);
        request.Headers.Add(ChallengeHeader, "failing-body");

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        host.Logs.Entries
            .Count(static entry => entry.StartsWith("Warning Tracon.ProblemDetailsOnDenial", StringComparison.Ordinal)
                && entry.Contains("could not get a problem body", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_default_handler_that_throws_is_not_swallowed()
    {
        // No authentication service at all: the default handler's challenge throws, with or without the call.
        await using var plain = await StartAsync(optIn: false, useTestScheme: false);
        await using var wrapped = await StartAsync(optIn: true, useTestScheme: false);

        var plainFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => plain.Client.GetAsync(ReadPath, TestContext.Current.CancellationToken));
        var wrappedFailure = await Should.ThrowAsync<InvalidOperationException>(
            () => wrapped.Client.GetAsync(ReadPath, TestContext.Current.CancellationToken));

        wrappedFailure.Message.ShouldBe(plainFailure.Message);
    }

    [Fact]
    public async Task Many_concurrent_refusals_all_carry_the_same_problem_body()
    {
        await using var host = await StartAsync(optIn: true);

        var replies = await Task.WhenAll(Enumerable.Range(0, 100).Select(async _ =>
        {
            using var reply = await SendAsync(host, ReadPath, role: "some-other-role");

            return (reply.Status, reply.ContentType, Body: WithoutTraceId(reply.Body));
        }));

        replies.Select(static reply => reply.Status).Distinct().ShouldBe([HttpStatusCode.Forbidden]);
        replies.Select(static reply => reply.ContentType).Distinct(StringComparer.Ordinal).ShouldBe([ProblemJson]);
        replies.Select(static reply => reply.Body).Distinct(StringComparer.Ordinal).Count().ShouldBe(1);
    }

    [Fact]
    public async Task The_mcp_and_a2a_endpoints_carry_the_mark()
    {
        await using var host = await StartAsync(
            optIn: true,
            configureTracon: static builder => builder.UseMcpServer().UseA2A(static options => options.ExposedAgents.Add("probe")),
            configureEndpoints: static options => options.RequireAuthorization(TraconPolicies.Reader),
            configureAfterMap: static app =>
            {
                app.MapTraconMcpServer();
                app.MapTraconA2A();
            });

        foreach (var (method, path) in new[]
        {
            (HttpMethod.Post, "/tracon/mcp"),
            (HttpMethod.Get, "/tracon/a2a/probe/.well-known/agent-card.json"),
        })
        {
            using var reply = await SendAsync(host, path, role: "some-other-role", method: method);

            reply.Status.ShouldBe(HttpStatusCode.Forbidden, path);
            reply.ContentType.ShouldBe(ProblemJson, path);
        }
    }

    // --- registration order -------------------------------------------------------------------------------------

    [Fact]
    public void The_call_before_AddAuthorization_installs_the_wrapper()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseProblemDetailsOnDenial();
        services.AddAuthorization();

        ResolveHandler(services).ShouldBeOfType<ProblemDetailsOnDenialHandler>();
        services.Count(static descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler)).ShouldBe(1);
    }

    [Fact]
    public void The_call_after_AddAuthorization_replaces_the_default_with_the_wrapper()
    {
        var services = new ServiceCollection();
        services.AddAuthorization();
        services.AddTracon().UseProblemDetailsOnDenial();

        ResolveHandler(services).ShouldBeOfType<ProblemDetailsOnDenialHandler>();
        services.Count(static descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler)).ShouldBe(1);
    }

    [Fact]
    public void A_handler_the_consumer_registered_before_the_call_stays()
    {
        var services = new ServiceCollection();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ConsumerHandler>();
        services.AddTracon().UseProblemDetailsOnDenial();

        ResolveHandler(services).ShouldBeOfType<ConsumerHandler>();
    }

    [Fact]
    public void A_handler_the_consumer_registers_after_the_call_wins()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseProblemDetailsOnDenial();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ConsumerHandler>();

        ResolveHandler(services).ShouldBeOfType<ConsumerHandler>();
    }

    [Fact]
    public void Without_the_call_nothing_changes()
    {
        var services = new ServiceCollection();
        services.AddAuthorization();
        services.AddTracon();

        ResolveHandler(services).ShouldBeOfType<AuthorizationMiddlewareResultHandler>();
    }

    [Fact]
    public void Calling_it_twice_is_the_same_as_once()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseProblemDetailsOnDenial().UseProblemDetailsOnDenial();
        services.AddAuthorization();

        ResolveHandler(services).ShouldBeOfType<ProblemDetailsOnDenialHandler>();
        services.Count(static descriptor => descriptor.ServiceType == typeof(IAuthorizationMiddlewareResultHandler)).ShouldBe(1);
    }

    // --- the handler on its own: states a pipeline cannot reach on demand ----------------------------------------

    [Fact]
    public async Task A_cancelled_request_gets_no_body_and_no_log()
    {
        var logs = new RecordingLoggerProvider();
        var (context, handler) = Direct(logs, marked: true, innerStatus: 403, cancelled: true);

        await handler.HandleAsync(static _ => Task.CompletedTask, context, Policy(), PolicyAuthorizationResult.Forbid());

        context.Response.Body.Length.ShouldBe(0);
        context.Response.ContentType.ShouldBeNull();
        logs.AllText.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unmarked_endpoint_gets_no_body()
    {
        var logs = new RecordingLoggerProvider();
        var (context, handler) = Direct(logs, marked: false, innerStatus: 403);

        await handler.HandleAsync(static _ => Task.CompletedTask, context, Policy(), PolicyAuthorizationResult.Forbid());

        context.Response.Body.Length.ShouldBe(0);
        context.Response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task A_status_other_than_401_and_403_is_left_alone()
    {
        var logs = new RecordingLoggerProvider();
        var (context, handler) = Direct(logs, marked: true, innerStatus: 302);

        await handler.HandleAsync(static _ => Task.CompletedTask, context, Policy(), PolicyAuthorizationResult.Challenge());

        context.Response.Body.Length.ShouldBe(0);
        context.Response.StatusCode.ShouldBe(302);
    }

    [Fact]
    public async Task A_success_passes_to_the_next_step_and_writes_nothing()
    {
        var logs = new RecordingLoggerProvider();
        var (context, handler) = Direct(logs, marked: true, innerStatus: 200);
        var reached = false;

        await handler.HandleAsync(
            _ =>
            {
                reached = true;

                return Task.CompletedTask;
            },
            context,
            Policy(),
            PolicyAuthorizationResult.Success());

        reached.ShouldBeTrue();
        context.Response.Body.Length.ShouldBe(0);
    }

    private static (DefaultHttpContext Context, ProblemDetailsOnDenialHandler Handler) Direct(
        RecordingLoggerProvider logs,
        bool marked,
        int innerStatus,
        bool cancelled = false)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs));
        var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Response.Body = new MemoryStream();

        if (cancelled)
        {
            context.RequestAborted = new CancellationToken(canceled: true);
        }

        context.SetEndpoint(new Endpoint(
            requestDelegate: null,
            marked ? new EndpointMetadataCollection(ProblemDetailsOnDenialMarker.Instance) : EndpointMetadataCollection.Empty,
            displayName: "probe"));

        var handler = new ProblemDetailsOnDenialHandler(
            new StatusHandler(innerStatus),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger("probe"));

        return (context, handler);
    }

    private static AuthorizationPolicy Policy() => new AuthorizationPolicyBuilder().RequireAssertion(static _ => true).Build();

    private static IAuthorizationMiddlewareResultHandler ResolveHandler(ServiceCollection services)
    {
        services.AddLogging();

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationMiddlewareResultHandler>();
    }

    // --- hosting ------------------------------------------------------------------------------------------------

    private static Task<TraconTestHost> StartAsync(
        bool optIn,
        Action<WebApplication>? configureApp = null,
        Action<WebApplication>? configureAfterMap = null,
        Action<TraconEndpointOptions>? configureEndpoints = null,
        Action<ITraconBuilder>? configureTracon = null,
        Action<IServiceCollection>? configureServices = null,
        bool useTestScheme = true)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                configureTracon?.Invoke(builder);

                if (optIn)
                {
                    builder.UseProblemDetailsOnDenial();
                }
            },
            configureEndpoints: configureEndpoints,
            configureApp: configureApp,
            configureAfterMap: configureAfterMap,
            configureServices: services =>
            {
                // Without the test scheme the host has policies and no authentication service at all: the
                // challenge has nowhere to go.
                if (useTestScheme)
                {
                    services
                        .AddAuthentication(RoleScheme.Name)
                        .AddScheme<AuthenticationSchemeOptions, RoleScheme>(RoleScheme.Name, configureOptions: null);
                }

                configureServices?.Invoke(services);

                var authorization = services.AddAuthorizationBuilder();

                foreach (var policy in new[] { TraconPolicies.Reader, TraconPolicies.Operator, TraconPolicies.Admin, TraconPolicies.PlatformAdmin })
                {
                    authorization.AddPolicy(policy, builder => builder.RequireRole(AcceptedRole));
                }
            });

    private static async Task<Reply> SendAsync(
        TraconTestHost host,
        string path,
        string? role = null,
        string? tenant = null,
        string? credential = null,
        HttpMethod? method = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);

        if (role is not null)
        {
            request.Headers.Add(RoleHeader, role);
        }

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        if (credential is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential);
        }

        return await Read(await host.Client.SendAsync(request, TestContext.Current.CancellationToken));
    }

    private static async Task<Reply> Read(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return new Reply(
            response,
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            response.Headers.WwwAuthenticate.ToString() is { Length: > 0 } challenge ? challenge : null,
            response.Headers.Location?.OriginalString,
            body);
    }

    private static string WithoutTraceId(string body)
    {
        var node = JsonNode.Parse(body)!.AsObject();
        node.Remove("traceId");

        return node.ToJsonString();
    }

    private sealed record Reply(
        HttpResponseMessage Response,
        HttpStatusCode Status,
        string? ContentType,
        string? WwwAuthenticate,
        string? Location,
        string Body) : IDisposable
    {
        public void Dispose() => Response.Dispose();
    }

    /// <summary>Authenticates the caller whose role the test header names; answers a challenge like a bearer scheme.</summary>
    private sealed class RoleScheme(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "ProblemDetailsOnDenialRole";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role) || role.ToString() is not { Length: > 0 } name)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, UserName), new Claim(ClaimTypes.Role, name)], Name);

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            if (Request.Headers.TryGetValue(ChallengeHeader, out var kind) && kind == "redirect")
            {
                // What an OpenID Connect challenge does: send the browser to the identity provider.
                Response.Redirect("https://idp.example/login");

                return Task.CompletedTask;
            }

            if (Request.Headers.TryGetValue(ChallengeHeader, out var failing) && failing == "failing-body")
            {
                // Every write to the response fails from here on, as a connection that dropped mid-answer would.
                Context.Features.Set<IHttpResponseBodyFeature>(new FailingBodyFeature());
            }

            Response.Headers.WWWAuthenticate = "Bearer realm=\"test\"";

            return base.HandleChallengeAsync(properties);
        }
    }

    /// <summary>A result handler a consumer would register in place of ASP.NET Core's.</summary>
    private sealed class ConsumerHandler : IAuthorizationMiddlewareResultHandler
    {
        public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
            => next(context);
    }

    /// <summary>An inner handler that only sets a status, as a scheme's challenge or forbid does.</summary>
    private sealed class StatusHandler(int status) : IAuthorizationMiddlewareResultHandler
    {
        public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Succeeded)
            {
                return next(context);
            }

            context.Response.StatusCode = status;

            return Task.CompletedTask;
        }
    }

    /// <summary>A response body feature whose every write fails.</summary>
    private sealed class FailingBodyFeature : IHttpResponseBodyFeature
    {
        public FailingBodyFeature()
        {
            Stream = new FailingStream();
            Writer = PipeWriter.Create(Stream);
        }

        public Stream Stream { get; }

        public PipeWriter Writer { get; }

        public void DisableBuffering()
        {
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task CompleteAsync() => Task.CompletedTask;
    }

    /// <summary>A response body whose every write fails.</summary>
    private sealed class FailingStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("The connection dropped.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new IOException("The connection dropped.");

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => throw new IOException("The connection dropped.");

        public override Task FlushAsync(CancellationToken cancellationToken) => throw new IOException("The connection dropped.");
    }
}

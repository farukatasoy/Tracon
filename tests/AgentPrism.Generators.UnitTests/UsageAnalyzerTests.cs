using System.Globalization;
using Microsoft.CodeAnalysis;

namespace AgentPrism.Generators.UnitTests;

/// <summary>
/// Behaviour of <see cref="AgentPrismUsageAnalyzer"/>. Every diagnostic is
/// covered from both sides: the code it must report, and the correct code next
/// to it that it must leave alone.
/// </summary>
/// <remarks>
/// The credential-shaped literals below are analyzer INPUT. APG0201 reports a
/// string LITERAL, so a placeholder short enough to be safe would prove the
/// opposite of what the test claims. None of them is a real credential, and
/// none matches a service's issued format beyond its prefix and length.
/// </remarks>
public sealed class UsageAnalyzerTests
{
    private const string Wiring = """
        using AgentPrism;
        using Microsoft.AspNetCore.Routing;
        using Microsoft.Extensions.DependencyInjection;
        """;

    [Fact]
    public async Task APG0101_reports_a_mapped_but_unregistered_control_plane()
    {
        var diagnostics = await AnalyzerTestHelper.RunAsync($$"""
            {{Wiring}}

            public static class Startup
            {
                public static void Configure(IEndpointRouteBuilder endpoints) => endpoints.MapAgentPrism();
            }
            """);

        var reported = diagnostics.ShouldHaveSingleItem();
        reported.Id.ShouldBe("APG0101");
        reported.Severity.ShouldBe(DiagnosticSeverity.Warning);
        reported.GetMessage(CultureInfo.InvariantCulture).ShouldContain("AddAgentPrism()");
    }

    [Fact]
    public async Task APG0101_is_silent_when_the_registration_is_in_another_file()
    {
        var registration = $$"""
            {{Wiring}}

            public static class Registration
            {
                public static void Register(IServiceCollection services) => services.AddAgentPrism();
            }
            """;

        var mapping = $$"""
            {{Wiring}}

            public static class Mapping
            {
                public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapAgentPrism();
            }
            """;

        var diagnostics = await AnalyzerTestHelper.RunAsync([registration, mapping]);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task APG0102_reports_a_provider_that_is_never_registered()
    {
        var diagnostics = await AnalyzerTestHelper.RunAsync($$"""
            {{Wiring}}

            public static class Agents
            {
                public static AgentDefinition Support() => new()
                {
                    Name = "support",
                    Model = new ModelBinding { Provider = "anthropic", Model = "claude-sonnet-4-5" },
                };
            }
            """);

        var reported = diagnostics.ShouldHaveSingleItem();
        reported.Id.ShouldBe("APG0102");
        reported.GetMessage(CultureInfo.InvariantCulture).ShouldContain("anthropic");
        reported.GetMessage(CultureInfo.InvariantCulture).ShouldContain("UseAnthropic()");
    }

    [Fact]
    public async Task APG0102_is_silent_when_the_provider_is_registered()
    {
        var source = $$"""
            {{Wiring}}

            public static class Agents
            {
                public static void Register(IServiceCollection services)
                    => services.AddAgentPrism().UseOpenAI("configured-elsewhere");

                public static AgentDefinition Support() => new()
                {
                    Name = "support",
                    Model = new ModelBinding { Provider = "openai", Model = "gpt-4o" },
                };
            }
            """;

        AnalyzerTestHelper.ShouldCompileCleanly(source);

        var diagnostics = await AnalyzerTestHelper.RunAsync(source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task APG0102_is_silent_when_a_custom_provider_answers_for_the_name()
    {
        var diagnostics = await AnalyzerTestHelper.RunAsync($$"""
            {{Wiring}}

            public static class Agents
            {
                public static void Register(IServiceCollection services, IModelProvider provider)
                    => services.AddAgentPrism().AddModelProvider(provider);

                public static AgentDefinition Support() => new()
                {
                    Name = "support",
                    Model = new ModelBinding { Provider = "google", Model = "gemini-2.5-pro" },
                };
            }
            """);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task APG0201_reports_a_literal_secret_inside_a_definition()
    {
        var diagnostics = await AnalyzerTestHelper.RunAsync($$"""
            {{Wiring}}
            using System;

            public static class Servers
            {
                public static McpServerDefinition Github() => new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "default",
                    Name = "github",
                    Endpoint = new Uri("https://example.test/mcp"),
                    AuthorizationConfigurationKey = "ghp_0123456789abcdef0123456789abcdef0123",
                };
            }
            """);

        var reported = diagnostics.ShouldHaveSingleItem();
        reported.Id.ShouldBe("APG0201");
        reported.Severity.ShouldBe(DiagnosticSeverity.Warning);
        reported.GetMessage(CultureInfo.InvariantCulture).ShouldContain("McpServerDefinition.AuthorizationConfigurationKey");
    }

    [Fact]
    public async Task APG0201_reports_a_literal_secret_hidden_in_a_header_dictionary()
    {
        var diagnostics = await AnalyzerTestHelper.RunAsync($$"""
            {{Wiring}}
            using System;
            using System.Collections.Generic;

            public static class Servers
            {
                public static McpServerDefinition Github() => new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "default",
                    Name = "github",
                    Endpoint = new Uri("https://example.test/mcp"),
                    Headers = new Dictionary<string, string>
                    {
                        ["X-Api-Key"] = "sk-abcdefghijklmnopqrstuvwxyz0123456789",
                    },
                };
            }
            """);

        diagnostics.ShouldHaveSingleItem().Id.ShouldBe("APG0201");
    }

    [Fact]
    public async Task APG0201_is_silent_for_a_configuration_key_name_and_a_short_placeholder()
    {
        var source = $$"""
            {{Wiring}}
            using System;

            public static class Servers
            {
                public static McpServerDefinition Github() => new()
                {
                    Id = Guid.NewGuid(),
                    TenantId = "default",
                    Name = "sk-test",
                    Endpoint = new Uri("https://example.test/mcp"),
                    AuthorizationConfigurationKey = "AgentPrism:Mcp:GithubToken",
                };
            }
            """;

        AnalyzerTestHelper.ShouldCompileCleanly(source);

        var diagnostics = await AnalyzerTestHelper.RunAsync(source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task APG0301_reports_a_retry_loop_written_around_a_chat_client()
    {
        var diagnostics = await AnalyzerTestHelper.RunAsync("""
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.AI;

            public sealed class RetryingChatClient(IChatClient inner) : DelegatingChatClient(inner)
            {
                public override async Task<ChatResponse> GetResponseAsync(
                    IEnumerable<ChatMessage> messages,
                    ChatOptions? options = null,
                    CancellationToken cancellationToken = default)
                {
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        try
                        {
                            return await base.GetResponseAsync(messages, options, cancellationToken);
                        }
                        catch (InvalidOperationException)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                        }
                    }

                    throw new InvalidOperationException("exhausted");
                }
            }
            """);

        var reported = diagnostics.ShouldHaveSingleItem();
        reported.Id.ShouldBe("APG0301");
        reported.GetMessage(CultureInfo.InvariantCulture).ShouldContain("RetryingChatClient");
    }

    [Fact]
    public async Task APG0301_is_silent_for_a_chat_client_that_does_not_retry()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.AI;

            public sealed class CountingChatClient(IChatClient inner) : DelegatingChatClient(inner)
            {
                public int Calls { get; private set; }

                public override Task<ChatResponse> GetResponseAsync(
                    IEnumerable<ChatMessage> messages,
                    ChatOptions? options = null,
                    CancellationToken cancellationToken = default)
                {
                    Calls++;
                    return base.GetResponseAsync(messages, options, cancellationToken);
                }
            }
            """;

        AnalyzerTestHelper.ShouldCompileCleanly(source);

        var diagnostics = await AnalyzerTestHelper.RunAsync(source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task APG0301_is_silent_for_a_loop_that_paces_instead_of_retrying()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.AI;

            public sealed class ThrottlingChatClient(IChatClient inner) : DelegatingChatClient(inner)
            {
                private DateTimeOffset _next;

                public override async Task<ChatResponse> GetResponseAsync(
                    IEnumerable<ChatMessage> messages,
                    ChatOptions? options = null,
                    CancellationToken cancellationToken = default)
                {
                    while (DateTimeOffset.UtcNow < _next)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
                    }

                    _next = DateTimeOffset.UtcNow.AddSeconds(1);

                    return await base.GetResponseAsync(messages, options, cancellationToken);
                }
            }
            """;

        AnalyzerTestHelper.ShouldCompileCleanly(source);

        var diagnostics = await AnalyzerTestHelper.RunAsync(source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task APG0302_is_silent_when_the_wrapper_serves_a_factory_agent()
    {
        var diagnostics = await AnalyzerTestHelper.RunAsync($$"""
            {{Wiring}}
            using Microsoft.Agents.AI;

            public sealed class LoggingAgent(AIAgent inner) : DelegatingAIAgent(inner);

            public static class Startup
            {
                public static void Register(IServiceCollection services)
                    => services.AddAgentPrism().AddAgent("logged", provider => new LoggingAgent(null!));
            }
            """);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task APG0302_is_silent_when_the_wrapper_belongs_to_a_decorator()
    {
        var source = """
            using AgentPrism;
            using Microsoft.Agents.AI;

            public sealed class LoggingAgent(AIAgent inner) : DelegatingAIAgent(inner);

            public sealed class LoggingDecorator : IAgentDecorator
            {
                public int Order => 10;

                public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor) => new LoggingAgent(agent);
            }
            """;

        AnalyzerTestHelper.ShouldCompileCleanly(source);

        var diagnostics = await AnalyzerTestHelper.RunAsync(source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task APG0302_reports_an_agent_wrapped_by_hand()
    {
        var source = """
            using Microsoft.Agents.AI;

            public sealed class LoggingAgent(AIAgent inner) : DelegatingAIAgent(inner);
            """;

        AnalyzerTestHelper.ShouldCompileCleanly(source);

        var diagnostics = await AnalyzerTestHelper.RunAsync(source);

        var reported = diagnostics.ShouldHaveSingleItem();
        reported.Id.ShouldBe("APG0302");
        reported.GetMessage(CultureInfo.InvariantCulture).ShouldContain("IAgentDecorator");
    }

    [Fact]
    public async Task APG0302_is_silent_for_the_decorator_it_recommends()
    {
        var source = """
            using AgentPrism;
            using Microsoft.Agents.AI;

            public sealed class LoggingDecorator : IAgentDecorator
            {
                public int Order => 10;

                public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor) => agent;
            }
            """;

        AnalyzerTestHelper.ShouldCompileCleanly(source);

        var diagnostics = await AnalyzerTestHelper.RunAsync(source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task APG0401_reports_an_agents_file_written_from_an_older_map()
    {
        var diagnostics = await AnalyzerTestHelper.RunAsync(
            "public static class Empty;",
            ("/repo/AGENTS.md", Marker("aaaaaaaa")),
            ("/packages/buildTransitive/AgentPrism.AgentMap.md", Marker("bbbbbbbb")));

        var reported = diagnostics.ShouldHaveSingleItem();
        reported.Id.ShouldBe("APG0401");
        reported.GetMessage(CultureInfo.InvariantCulture).ShouldContain("aaaaaaaa");
        reported.GetMessage(CultureInfo.InvariantCulture).ShouldContain("bbbbbbbb");
    }

    [Fact]
    public async Task APG0401_is_silent_when_the_revisions_match()
    {
        var diagnostics = await AnalyzerTestHelper.RunAsync(
            "public static class Empty;",
            ("/repo/AGENTS.md", Marker("aaaaaaaa")),
            ("/packages/buildTransitive/AgentPrism.AgentMap.md", Marker("aaaaaaaa")));

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task APG0401_is_silent_for_an_agents_file_this_package_did_not_write()
    {
        var diagnostics = await AnalyzerTestHelper.RunAsync(
            "public static class Empty;",
            ("/repo/AGENTS.md", "# House rules\n\nRun the tests before you commit.\n"),
            ("/packages/buildTransitive/AgentPrism.AgentMap.md", Marker("bbbbbbbb")));

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task Correct_wiring_reports_nothing()
    {
        var source = $$"""
            {{Wiring}}

            public static class Startup
            {
                public static void Register(IServiceCollection services)
                    => services.AddAgentPrism().UseOpenAI("configured-elsewhere").AddAgent(Support());

                public static void Map(IEndpointRouteBuilder endpoints) => endpoints.MapAgentPrism();

                private static AgentDefinition Support() => new()
                {
                    Name = "support",
                    Instructions = "Answer support questions.",
                    Model = new ModelBinding { Provider = "openai", Model = "gpt-4o" },
                };
            }
            """;

        AnalyzerTestHelper.ShouldCompileCleanly(source);

        var diagnostics = await AnalyzerTestHelper.RunAsync(source);

        diagnostics.ShouldBeEmpty();
    }

    private static string Marker(string revision)
        => $"<!-- AgentPrism agent map · revision: {revision} · generated by docs-site/scripts/build-agent-map.mjs -->\n# AgentPrism\n";
}

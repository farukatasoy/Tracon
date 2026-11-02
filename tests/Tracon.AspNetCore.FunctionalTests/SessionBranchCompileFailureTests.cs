using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Branching a session whose agent cannot be compiled is a 400, as running that agent is.
/// </summary>
/// <remarks>
/// Branching restores the session through the agent, so it resolves the agent from the catalog. A tenant provider
/// binding whose configuration key has no value, or an egress policy that no longer allows the provider, makes that
/// resolution throw; the run endpoints, the OpenAI-compatible ones and the replay answer 400 "Agent compilation failed"
/// for it, and the branch let it out as a 500.
/// </remarks>
public sealed class SessionBranchCompileFailureTests
{
    [Fact]
    public async Task Branching_a_session_of_an_agent_that_cannot_be_compiled_is_a_400_not_a_500()
    {
        // Registering a branch store lifts the "not supported" answer; the store is never reached, because the agent
        // is resolved first.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services => services.AddSingleton<IConversationBranchStore, UnreachedBranchStore>());

        var tenant = host.Services.GetRequiredService<ITenantContext>().TenantId;
        var model = TestData.Model();

        await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(new AgentDefinition
        {
            Name = "branchy",
            Instructions = "Answer.",
            Model = model,
        });

        await host.Services.GetRequiredService<ISessionStore>().SaveAsync(new SessionRecord
        {
            Id = "branch-me",
            AgentName = "branchy",
            State = JsonDocument.Parse("{}").RootElement.Clone(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
        });

        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = tenant,
                ProviderName = model.Provider,
                ApiKeyConfigurationName = "Tracon:ProviderKeys:branch-missing",
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/sessions/branch-me/branch", UriKind.Relative),
            new { newSessionId = "branched" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var problem = await TraconTestHost.ReadJsonAsync(response);
        problem.GetProperty("title").GetString().ShouldBe("Agent compilation failed");
        (problem.GetProperty("detail").GetString() ?? string.Empty).ShouldContain("Tracon:ProviderKeys:branch-missing");
    }

    private sealed class UnreachedBranchStore : IConversationBranchStore
    {
        public ValueTask<ConversationBranch?> BranchAsync(
            string tenantId,
            Guid parentConversationId,
            long? upToSequence,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The agent is resolved before the store is asked.");
    }
}

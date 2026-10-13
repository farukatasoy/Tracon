using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Which <see cref="ISkillScriptExecutor"/> a host ends up with, decided only
/// once every module has registered: a host's own executor wins in both
/// registration orders, and <c>RequireCustomBinding&lt;ISkillScriptExecutor&gt;()</c>
/// refuses to start while the built-in one is what resolves.
/// </summary>
/// <remarks>
/// What a container resolves is a composition fact, so the cases start real hosts;
/// the ordering case also runs a script, because a resolved service that the
/// runner does not use would prove nothing.
/// </remarks>
public sealed class SkillScriptExecutorRegistrationTests
{
    private const string SkillName = "registration-skill";

    [Fact]
    public async Task Without_a_host_executor_the_built_in_one_resolves()
    {
        await using var host = await TraconTestHost.StartAsync(configureTracon: static builder => EnableScripts(builder));

        host.Services.GetRequiredService<ISkillScriptExecutor>().ShouldBeOfType<SkillScriptProcessRunner>();
        host.Services.GetRequiredService<SandboxedSkillScriptRunner>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_host_executor_wins_in_either_registration_order_and_runs_the_script(bool beforeAddTracon)
    {
        var executor = new AnsweringExecutor();

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                if (beforeAddTracon)
                {
                    services.AddSingleton<ISkillScriptExecutor>(executor);
                }
            },
            configureTracon: builder =>
            {
                EnableScripts(builder);
                AddScriptAgent(builder);

                if (!beforeAddTracon)
                {
                    builder.Services.AddSingleton<ISkillScriptExecutor>(executor);
                }

                builder.RequireCustomBinding<ISkillScriptExecutor>();
            });

        host.Services.GetRequiredService<ISkillScriptExecutor>().ShouldBeSameAs(executor);

        await AddStandingApprovalRuleAsync(host);
        var saved = await host.Services.GetRequiredService<IAgentSkillStore>().SaveAsync(new AgentSkillDefinition
        {
            TenantId = "default",
            Name = SkillName,
            Description = "Skill for the registration tests.",
            Instructions = "Run the script when asked.",
            Scripts = [new AgentSkillScriptDefinition { Name = "hello", Extension = "sh", Content = "echo registration" }],
        });
        await host.Services.GetRequiredService<ISkillScriptGrantStore>().GrantAsync(new SkillScriptGrant
        {
            TenantId = "default",
            SkillName = SkillName,
            ContentHash = saved.ScriptSetHash,
            GrantedAt = DateTimeOffset.UtcNow,
        });

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/registration-agent/run", UriKind.Relative),
            new AgentRunRequest { Message = "run the script" });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = new List<string>();
        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync()))
        {
            frames.Add(frame.Data);
        }

        string.Join('\n', frames).ShouldContain("answered-by-host-executor");
        executor.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task RequireCustomBinding_stops_the_host_while_the_built_in_executor_resolves()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder => EnableScripts(builder).RequireCustomBinding<ISkillScriptExecutor>()));

        exception.Message.ShouldContain(nameof(ISkillScriptExecutor));
    }

    [Fact]
    public async Task A_TryAdd_registration_after_UseSkillScripts_is_dropped_and_RequireCustomBinding_catches_it()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder =>
                {
                    EnableScripts(builder);
                    builder.Services.TryAddSingleton<ISkillScriptExecutor>(new AnsweringExecutor());
                    builder.RequireCustomBinding<ISkillScriptExecutor>();
                }));

        exception.Message.ShouldContain(nameof(ISkillScriptExecutor));
    }

    [Fact]
    public async Task RequireCustomBinding_without_UseSkillScripts_names_the_contract()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await TraconTestHost.StartAsync(
                configureTracon: static builder => builder.RequireCustomBinding<ISkillScriptExecutor>()));

        exception.Message.ShouldContain(nameof(ISkillScriptExecutor));
    }

    private static ITraconBuilder EnableScripts(ITraconBuilder builder)
        => builder.UseSkillScripts(static options =>
        {
            options.PlatformIsolationAcknowledged = true;
            options.AllowStoredScripts = true;
            options.Interpreters["sh"] = "/bin/sh";
        });

    private static void AddScriptAgent(ITraconBuilder builder)
        => builder
            .AddModelProvider(new Tracon.Testing.FakeModelProvider("registration-model")
                .CallsTool("run_skill_script", new { skillName = SkillName, scriptName = "hello" })
                .EchoesLastToolResult())
            .AddAgent(new AgentDefinition
            {
                Name = "registration-agent",
                Instructions = "Run the script.",
                Model = new ModelBinding { Provider = "registration-model", Model = "registration" },
                SkillNames = [SkillName],
            });

    private static async Task AddStandingApprovalRuleAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/approvals/rules", UriKind.Relative),
            new { toolName = "run_skill_script" });

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private sealed class AnsweringExecutor : ISkillScriptExecutor
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<SkillScriptExecutionResult> ExecuteAsync(
            SkillScriptExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.FromResult(new SkillScriptExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "answered-by-host-executor",
            });
        }
    }
}

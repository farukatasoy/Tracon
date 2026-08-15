using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AgentPrism;

/// <summary>Configuration extensions that enable skill script execution.</summary>
public static class AgentPrismSkillScriptBuilderExtensions
{
    /// <summary>
    /// Enables skill script execution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <strong>This call changes a security boundary.</strong> Once
    /// enabled, AgentPrism can run scripts from permitted skills <em>on its
    /// own machine</em>.
    /// </para>
    /// <para>
    /// AgentPrism does not provide operating-system-level isolation: cutting
    /// off network access, restricting the file system, applying CPU/memory
    /// quotas, and dropping privileges are the hosting environment's job. For
    /// this reason, the application <strong>fails at startup</strong> if
    /// <see cref="AgentPrismSkillScriptOptions.PlatformIsolationAcknowledged"/>
    /// is not set.
    /// </para>
    /// <para>
    /// Even with the feature enabled, a script also requires a
    /// <see cref="SkillScriptGrant"/> record and a user approval that has gone
    /// through MAF's approval flow before it can run.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddAgentPrism()
    ///        .UseSkillScripts(o =>
    ///        {
    ///            o.PlatformIsolationAcknowledged = true;   // running inside a container
    ///            o.SkillRoots.Add("/opt/agentprism/skills");
    ///            o.Interpreters["py"] = "/usr/bin/python3";
    ///            o.Timeout = TimeSpan.FromSeconds(30);
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="builder">The configuration chain.</param>
    /// <param name="configure">Modifies the script settings.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public static IAgentPrismBuilder UseSkillScripts(
        this IAgentPrismBuilder builder,
        Action<AgentPrismSkillScriptOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Configure(options =>
        {
            options.Skills.Scripts.Enabled = true;
            configure(options.Skills.Scripts);
        });

        // An explicit factory is required: the built-in DI container does not
        // fill in constructor parameters that carry a default value, and
        // TimeProvider may not be registered.
        builder.Services.TryAddSingleton(static provider => new SandboxedSkillScriptRunner(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentPrismOptions>>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<ISkillScriptGrantStore>(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SandboxedSkillScriptRunner>>(),
            provider.GetService<IRunStore>(),
            provider.GetService<AgentPrismMetrics>(),
            provider.GetService<TimeProvider>()));

        builder.Services.TryAddSingleton(static provider => new SkillScriptSupport(
            provider.GetRequiredService<SandboxedSkillScriptRunner>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentPrismOptions>>(),
            provider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()));

        return builder;
    }
}

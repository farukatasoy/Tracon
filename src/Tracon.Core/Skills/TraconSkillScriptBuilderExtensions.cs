using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Tracon;

/// <summary>Configuration extensions that enable skill script execution.</summary>
public static class TraconSkillScriptBuilderExtensions
{
    /// <summary>
    /// Enables skill script execution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This call changes a security boundary.</strong> Once
    /// enabled, Tracon can run scripts from permitted skills <em>on its
    /// own machine</em>.
    /// </para>
    /// <para>
    /// Tracon does not provide operating-system-level isolation: cutting
    /// off network access, restricting the file system, applying CPU/memory
    /// quotas, and dropping privileges are the hosting environment's job. For
    /// this reason, the application <strong>fails at startup</strong> if
    /// <see cref="TraconSkillScriptOptions.PlatformIsolationAcknowledged"/>
    /// is not set.
    /// </para>
    /// <para>
    /// Even with the feature enabled, a script also requires a
    /// <see cref="SkillScriptGrant"/> record and a user approval that has gone
    /// through MAF's approval flow before it can run.
    /// </para>
    /// <para>
    /// An approved script goes to the registered <see cref="ISkillScriptExecutor"/>.
    /// This call registers the built-in one, which starts a local process under
    /// the host's operating-system identity. Register your own executor with
    /// <c>AddSingleton</c>, before or after this call, to run scripts somewhere
    /// else; add <c>RequireCustomBinding&lt;ISkillScriptExecutor&gt;()</c> to stop
    /// the host when the built-in one is still what resolves. The acknowledgment
    /// above is required with any executor: Tracon cannot verify what isolation an
    /// executor provides.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseSkillScripts(o =>
    ///        {
    ///            o.PlatformIsolationAcknowledged = true;   // running inside a container
    ///            o.SkillRoots.Add("/opt/tracon/skills");
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
    public static ITraconBuilder UseSkillScripts(
        this ITraconBuilder builder,
        Action<TraconSkillScriptOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Configure(options =>
        {
            options.Skills.Scripts.Enabled = true;
            configure(options.Skills.Scripts);
        });

        // The built-in executor is a MARKED default: a host's own executor wins in
        // either registration order, and RequireCustomBinding<ISkillScriptExecutor>()
        // can tell the two apart. A factory, because the constructor is internal
        // and type-based activation only finds public constructors.
        builder.Services.TryAddTraconDefault<ISkillScriptExecutor>(static provider => new SkillScriptProcessRunner(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>()));

        // An explicit factory is required: the built-in DI container does not
        // fill in constructor parameters that carry a default value, and
        // TimeProvider may not be registered.
        builder.Services.TryAddSingleton(static provider => new SandboxedSkillScriptRunner(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<ISkillScriptGrantStore>(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<ISkillScriptExecutor>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SandboxedSkillScriptRunner>>(),
            provider.GetService<IRunStore>(),
            provider.GetService<TraconMetrics>(),
            provider.GetService<TimeProvider>()));

        builder.Services.TryAddSingleton(static provider => new SkillScriptSupport(
            provider.GetRequiredService<SandboxedSkillScriptRunner>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TraconOptions>>(),
            provider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()));

        return builder;
    }
}

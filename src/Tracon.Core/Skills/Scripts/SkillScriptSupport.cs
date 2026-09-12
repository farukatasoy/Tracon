using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The compile-path handle for skill script execution: sets up MAF's
/// file-based skill source and produces the execution delegate for stored
/// scripts.
/// </summary>
/// <remarks>
/// <para>
/// This type is registered only when <c>UseSkillScripts</c> is called. If not
/// registered, the compiler runs without script support; this is the concrete
/// expression of the feature being <strong>disabled by default</strong>.
/// </para>
/// <para>
/// Two sources are kept separate:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <strong>Skills on disk</strong> — scanned by MAF's <c>AgentFileSkillsSource</c>
///     type. Whoever deploys the application writes the content.
///   </description></item>
///   <item><description>
///     <strong>Skills in the database</strong> — go through Tracon's own
///     source. Using MAF's file source for database data would bypass tenant
///     isolation and cache fingerprinting.
///   </description></item>
/// </list>
/// </remarks>
public sealed class SkillScriptSupport
{
    private readonly SandboxedSkillScriptRunner _runner;
    private readonly IOptions<TraconOptions> _options;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Creates a new script support instance.</summary>
    /// <param name="runner">The isolated runner.</param>
    /// <param name="options">The Tracon settings.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public SkillScriptSupport(
        SandboxedSkillScriptRunner runner,
        IOptions<TraconOptions> options,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(options);

        _runner = runner;
        _options = options;
        _loggerFactory = loggerFactory;
    }

    private TraconSkillScriptOptions Options => _options.Value.Skills.Scripts;

    /// <summary>Whether stored scripts are visible to the model.</summary>
    internal bool StoredScriptsEnabled => Options.Enabled && Options.AllowStoredScripts;

    /// <summary>Produces the MAF source that scans skill roots on disk.</summary>
    /// <returns><see langword="null"/> if no root is defined.</returns>
    internal AgentFileSkillsSource? CreateFileSource()
    {
        var options = Options;

        if (!options.Enabled || options.SkillRoots.Count == 0)
        {
            return null;
        }

        var fileOptions = new AgentFileSkillsSourceOptions
        {
            SearchDepth = options.SearchDepth,

            // The extension allow-list is also enforced on MAF's side. An
            // extension with no counterpart in the interpreter dictionary
            // cannot be run anyway; filtering it here too ensures a script
            // that could never run is never even shown to the model.
            AllowedScriptExtensions = options.Interpreters.Keys
                .Select(static extension => "." + extension.TrimStart('.'))
                .ToArray(),
        };

        return new AgentFileSkillsSource(
            options.SkillRoots.ToArray(),
            RunFileScriptAsync,
            fileOptions,
            _loggerFactory);
    }

    /// <summary>Produces the delegate that runs a stored script.</summary>
    /// <param name="skillName">The skill's name.</param>
    /// <param name="script">The script definition.</param>
    /// <returns>The delegate MAF will call.</returns>
    /// <remarks>
    /// The parameter's default value is deliberate: MAF's generator
    /// marks this field "required" in the argument schema (even though it is
    /// nullable) and rejects the model sending JSON `null` as "value missing"
    /// (<c>Microsoft.Agents.AI.AgentSkillsProvider</c>, <c>Throw.ArgumentException</c>)
    /// — for a script with no arguments, this would COMPLETELY block real
    /// execution. The default empty string lets MAF publish the field as
    /// "not required"; the body already handles empty/null the same way.
    /// </remarks>
    internal Func<string, CancellationToken, Task<object?>> CreateStoredScriptDelegate(
        string skillName,
        AgentSkillScriptDefinition script)
    {
        return RunStoredScript;

        Task<object?> RunStoredScript(string arguments = "", CancellationToken cancellationToken = default)
        {
            JsonElement? parsed = null;

            if (arguments is { Length: > 0 })
            {
                try
                {
                    using var document = JsonDocument.Parse(arguments);
                    parsed = document.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    throw new TraconException(
                        $"Script '{skillName}/{script.Name}' was given an invalid JSON argument.",
                        ex);
                }
            }

            return _runner.RunStoredScriptAsync(skillName, script, parsed, cancellationToken);
        }
    }

    private Task<object?> RunFileScriptAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
        => _runner.RunFileScriptAsync(skill, script, arguments, serviceProvider, cancellationToken);
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
internal sealed class SkillScriptSupport
{
    private readonly SandboxedSkillScriptRunner _runner;
    private readonly IOptions<TraconOptions> _options;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Creates a new script support instance.</summary>
    /// <param name="runner">The isolated runner.</param>
    /// <param name="options">The Tracon settings.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    internal SkillScriptSupport(
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
    internal AgentFileSkillsSource? CreateFileSource() => CreateFileSource(_loggerFactory);

    /// <summary>
    /// Reads the skills under the skill roots: each skill's name and a hash of what the
    /// model is shown for it.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Name to hash; empty when no root is defined.</returns>
    /// <remarks>
    /// <para>
    /// The skill catalog asks this when a name is neither in code nor in the
    /// store, so an agent can name a skill that exists only on disk. The read is
    /// Microsoft Agent Framework's own file source, the one a run is served from: a
    /// skill MAF would not load (a bad frontmatter, a link) is not found here either.
    /// </para>
    /// <para>
    /// The hash covers the <c>SKILL.md</c> text and the names of the skill's scripts and
    /// resources (<c>AgentFileSkill.GetContentAsync</c>), so an edit compiles the agent
    /// again. A script's own text is read when it runs and is not part of the hash. The
    /// first root wins on a name that two roots carry, as it does in a run. The read logs
    /// nothing: it happens on every resolution, and the run's own source logs the scan.
    /// </para>
    /// </remarks>
    internal async ValueTask<IReadOnlyDictionary<string, string>> ReadDiskSkillsAsync(CancellationToken cancellationToken)
    {
        using var source = CreateFileSource(NullLoggerFactory.Instance);

        if (source is null)
        {
            return EmptyDiskSkills;
        }

        // The file source does not read its context (Microsoft Agent Framework 1.20.0);
        // the catalog asks outside any run, where there is no agent or session to give.
        var skills = await source.GetSkillsAsync(null!, cancellationToken).ConfigureAwait(false);
        var hashes = new Dictionary<string, string>(skills.Count, StringComparer.Ordinal);

        foreach (var skill in skills)
        {
            if (hashes.ContainsKey(skill.Frontmatter.Name))
            {
                continue;
            }

            var content = await skill.GetContentAsync(cancellationToken).ConfigureAwait(false);

            hashes.Add(skill.Frontmatter.Name, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))));
        }

        return hashes;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyDiskSkills =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private AgentFileSkillsSource? CreateFileSource(ILoggerFactory? loggerFactory)
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
            loggerFactory);
    }

    /// <summary>Produces the delegate that runs a stored or code-defined script.</summary>
    /// <param name="skill">The skill definition the agent was compiled with.</param>
    /// <param name="scriptName">The name of the script in <paramref name="skill"/>.</param>
    /// <returns>The delegate MAF will call.</returns>
    /// <remarks>
    /// The parameter's default value is deliberate: MAF's generator
    /// marks this field "required" in the argument schema (even though it is
    /// nullable) and rejects the model sending JSON `null` as "value missing"
    /// (<c>Microsoft.Agents.AI.AgentSkillsProvider</c>, <c>Throw.ArgumentException</c>)
    /// — for a script with no arguments, this would COMPLETELY block real
    /// execution. The default empty string lets MAF publish the field as
    /// "not required"; the body already handles empty/null the same way.
    /// <para>
    /// The delegate captures the whole skill definition: the runner computes the
    /// hashes the grant must pin from the definition that will actually run, not
    /// from a fresh store read that could return other content.
    /// </para>
    /// </remarks>
    internal Func<string, CancellationToken, Task<object?>> CreateStoredScriptDelegate(
        AgentSkillDefinition skill,
        string scriptName)
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
                        $"Script '{skill.Name}/{scriptName}' was given an invalid JSON argument.",
                        ex);
                }
            }

            return _runner.RunStoredScriptAsync(skill, scriptName, parsed, cancellationToken);
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

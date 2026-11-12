using System.Globalization;
using System.Reflection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Retention</c> really reaches
/// <see cref="TraconRetentionOptions"/> — the three scalars and every target.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TraconRetentionOptions.IdempotencyKeys"/> was a target the
/// options defined and the purge resolved, but the binder read fifteen of the
/// sixteen targets and not that one: <c>Tracon:Retention:IdempotencyKeys:*</c> was
/// silently ignored. An earlier defect had already left four targets missing from the
/// binder the same way.
/// </para>
/// <para>
/// The configuration keys are written out by hand in <see cref="TargetNames"/>, not
/// derived from the options type. A key built from a property name makes the check
/// that every member is named by a key pass by construction, so a target added to the
/// options and to neither the binder nor this test would stay silent. With the
/// literal list, <see cref="OptionsKeyCoverage"/> fails on the new member, and
/// <see cref="The_literal_target_names_are_the_targets_of_the_options_type"/> fails
/// on a rename. The list is tied to <see cref="RetentionTargets.All"/> too, so a
/// target the purge knows cannot hide from either test.
/// </para>
/// </remarks>
public sealed class TraconRetentionOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Retention:Enabled"] = "true",
            ["Tracon:Retention:BatchSize"] = "250",
            ["Tracon:Retention:BatchDelay"] = "00:00:02",
        };

        foreach (var (key, value) in TargetValues())
        {
            values[key] = value;
        }

        var options = SectionBinding.Bind<TraconRetentionOptions>(values);

        options.Enabled.ShouldBeTrue();
        options.BatchSize.ShouldBe(250);
        options.BatchDelay.ShouldBe(TimeSpan.FromSeconds(2));
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:Retention", options);
    }

    [Fact]
    public void Every_retention_target_is_bound()
    {
        var options = SectionBinding.Bind<TraconRetentionOptions>(TargetValues());

        var failures = new List<string>();

        for (var index = 0; index < TargetNames.Length; index++)
        {
            var name = TargetNames[index];
            var bound = TargetOf(options, name);

            if (bound is null)
            {
                failures.Add($"Tracon:Retention:{name}: the options type has no target of this name.");
            }
            else if (bound.MaxAgeDays != MaxAgeDaysFor(index) || !bound.Archive)
            {
                failures.Add(
                    $"Tracon:Retention:{name}: expected MaxAgeDays={MaxAgeDaysFor(index)} and Archive=true, " +
                    $"got MaxAgeDays={bound.MaxAgeDays?.ToString(CultureInfo.InvariantCulture) ?? "null"} and Archive={bound.Archive}. " +
                    "The binder does not read this target.");
            }
        }

        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void The_literal_target_names_are_the_targets_of_the_options_type()
    {
        // The keys above are literal, so the options type is the thing to compare
        // them with: a renamed or added target fails here, naming both sides.
        var declared = Targets().Select(static target => target.Name).Order(StringComparer.Ordinal).ToArray();

        declared.ShouldBe(
            TargetNames.Order(StringComparer.Ordinal).ToArray(),
            customMessage: "TargetNames and the RetentionTargetOptions properties of TraconRetentionOptions differ.");
    }

    [Fact]
    public void The_target_count_matches_the_purge_targets()
    {
        // Tie the literal list to the purge's own list, so a target the purge knows
        // cannot hide from the binding test.
        TargetNames.Length.ShouldBe(RetentionTargets.All.Count);
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconRetentionOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.BatchSize.ShouldBe(5000);
        options.BatchDelay.ShouldBe(TimeSpan.FromMilliseconds(100));
        options.RunEvents.MaxAgeDays.ShouldBe(30);
        options.IdempotencyKeys.MaxAgeDays.ShouldBe(1);
        options.Sessions.MaxAgeDays.ShouldBeNull();
        options.DocumentEmbeddings.MaxAgeDays.ShouldBeNull();
        TargetNames.ShouldAllBe(name => !TargetOf(options, name)!.Archive);
    }

    /// <summary>
    /// The seventeen targets of <c>Tracon:Retention</c>, as they are written in a configuration file.
    /// </summary>
    private static readonly string[] TargetNames =
    [
        "RunEvents",
        "ToolInvocations",
        "Spans",
        "Jobs",
        "WebhookDeliveries",
        "EvalCaseResults",
        "WorkflowCheckpoints",
        "SkillScriptGrants",
        "Attachments",
        "Sessions",
        "Conversations",
        "IdempotencyKeys",
        "RunInputs",
        "VoiceSessions",
        "RunScores",
        "DocumentEmbeddings",
        "WorkflowRequests",
    ];

    /// <summary>The target a configuration key names, or <see langword="null"/> when the options type has none.</summary>
    private static RetentionTargetOptions? TargetOf(TraconRetentionOptions options, string name)
        => typeof(TraconRetentionOptions).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(options)
            as RetentionTargetOptions;

    /// <summary>Every public <see cref="RetentionTargetOptions"/> property, in declaration order.</summary>
    private static List<PropertyInfo> Targets()
        => typeof(TraconRetentionOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.PropertyType == typeof(RetentionTargetOptions))
            .ToList();

    /// <summary>
    /// A distinct age per target, above every shipped default (the largest is 180),
    /// so a target that kept its default cannot pass for one that was bound.
    /// </summary>
    private static int MaxAgeDaysFor(int index) => 200 + index;

    private static Dictionary<string, string?> TargetValues()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);

        for (var index = 0; index < TargetNames.Length; index++)
        {
            values[$"Tracon:Retention:{TargetNames[index]}:MaxAgeDays"] = MaxAgeDaysFor(index).ToString(CultureInfo.InvariantCulture);
            values[$"Tracon:Retention:{TargetNames[index]}:Archive"] = "true";
        }

        return values;
    }
}

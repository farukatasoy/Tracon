using System.Globalization;
using System.Reflection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Retention</c> really reaches
/// <see cref="TraconRetentionOptions"/> — the three scalars and every target.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-247: <see cref="TraconRetentionOptions.IdempotencyKeys"/> was a target the
/// options defined and the purge resolved, but the binder read fifteen of the
/// sixteen targets and not that one: <c>Tracon:Retention:IdempotencyKeys:*</c> was
/// silently ignored. HATA-S1-005 had already found four targets missing from the
/// binder the same way.
/// </para>
/// <para>
/// So the targets are not listed here by hand. They are counted by reflection from
/// the options type, and the count is tied to <see cref="RetentionTargets.All"/>: a
/// seventeenth target that is added to the options but not to the binder fails
/// <see cref="Every_retention_target_is_bound"/>.
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

        foreach (var (target, index) in Targets().Select(static (target, index) => (target, index)))
        {
            var bound = (RetentionTargetOptions)target.GetValue(options)!;

            if (bound.MaxAgeDays != MaxAgeDaysFor(index) || !bound.Archive)
            {
                failures.Add(
                    $"Tracon:Retention:{target.Name}: expected MaxAgeDays={MaxAgeDaysFor(index)} and Archive=true, " +
                    $"got MaxAgeDays={bound.MaxAgeDays?.ToString(CultureInfo.InvariantCulture) ?? "null"} and Archive={bound.Archive}. " +
                    "The binder does not read this target.");
            }
        }

        failures.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void The_target_count_matches_the_purge_targets()
    {
        // The reflective count above is only as good as what it counts: tie it to
        // the purge's own list, so a target the purge knows cannot hide from it.
        Targets().Count.ShouldBe(RetentionTargets.All.Count);
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
        Targets().ShouldAllBe(target => !((RetentionTargetOptions)target.GetValue(options)!).Archive);
    }

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

        foreach (var (target, index) in Targets().Select(static (target, index) => (target, index)))
        {
            values[$"Tracon:Retention:{target.Name}:MaxAgeDays"] = MaxAgeDaysFor(index).ToString(CultureInfo.InvariantCulture);
            values[$"Tracon:Retention:{target.Name}:Archive"] = "true";
        }

        return values;
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:ContentProtection</c> really reaches
/// <see cref="TraconContentProtectionOptions"/>, and an entry that cannot be read
/// stops the application at startup — whether protection is on or off.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 F-247: the binder skipped a column name it did not know and a key entry with
/// an empty name. <c>Columns = ["RunInput", "RunInputt"]</c> started and protected
/// only <c>RunInput</c>: the column the operator meant stayed in plain text, and
/// nothing said so.
/// </para>
/// <para>
/// The checks run while protection is off too. Resolving a key does not look at
/// <see cref="TraconContentProtectionOptions.Enabled"/>: an old encrypted row is
/// still read through <see cref="TraconContentProtectionOptions.Keys"/> after
/// protection is turned off.
/// </para>
/// </remarks>
public sealed class TraconContentProtectionOptionsBindingTests
{
    [Fact]
    public void Every_scalar_of_the_section_is_bound()
    {
        // Enabled=true needs an active key that Keys names, or the validator stops
        // the container before the values can be read.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentProtection:Enabled"] = "true",
            ["Tracon:ContentProtection:ActiveKeyId"] = "2026-10",
            ["Tracon:ContentProtection:Keys:2026-10"] = "Tracon:ContentProtection:RawKeys:2026-10",
        };

        var options = SectionBinding.Bind<TraconContentProtectionOptions>(values);

        options.Enabled.ShouldBeTrue();
        string.Equals(options.ActiveKeyId, "2026-10", StringComparison.Ordinal).ShouldBeTrue();
        OptionsKeyCoverage.ShouldCoverEveryMember(values, "Tracon:ContentProtection", options, "Columns");
    }

    [Fact]
    public void The_keys_are_bound()
    {
        var options = SectionBinding.Bind<TraconContentProtectionOptions>(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentProtection:Keys:2026-08"] = "Tracon:ContentProtection:RawKeys:2026-08",
            ["Tracon:ContentProtection:Keys:2026-10"] = "Tracon:ContentProtection:RawKeys:2026-10",
        });

        // The value is the NAME of the configuration key the raw material is read
        // from (K-561), kept as written.
        options.Keys.Count.ShouldBe(2);
        string.Equals(options.Keys["2026-08"], "Tracon:ContentProtection:RawKeys:2026-08", StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(options.Keys["2026-10"], "Tracon:ContentProtection:RawKeys:2026-10", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public void The_columns_are_bound()
    {
        var options = SectionBinding.Bind<TraconContentProtectionOptions>(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentProtection:Columns:0"] = "RunInput",
            ["Tracon:ContentProtection:Columns:1"] = "sessionstate",
        });

        // A list that is present REPLACES the default (every column); a name
        // matches case-insensitively.
        options.Columns.Count.ShouldBe(2);
        options.Columns.ShouldContain(ProtectedColumn.RunInput);
        options.Columns.ShouldContain(ProtectedColumn.SessionState);
    }

    [Theory]
    [InlineData("true", "RunInputt")]
    [InlineData("false", "RunInputt")]
    [InlineData("false", "99")]
    [InlineData("false", "")]
    [InlineData("false", "RunInput,ToolArguments")]
    public void An_unknown_column_fails_at_startup(string enabled, string column)
    {
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentProtection:Enabled"] = enabled,
            ["Tracon:ContentProtection:ActiveKeyId"] = "2026-10",
            ["Tracon:ContentProtection:Keys:2026-10"] = "Tracon:ContentProtection:RawKeys:2026-10",
            ["Tracon:ContentProtection:Columns:0"] = "RunInput",
            ["Tracon:ContentProtection:Columns:1"] = column,
        });

        // The message names the key and the text as written, so the operator can find the entry to fix.
        failure.Message.ShouldContain($"Tracon:ContentProtection:Columns:1 is '{column}'");
    }

    [Fact]
    public void Every_unknown_column_is_named()
    {
        // Each unreadable entry is recorded under its own key; two typos are two failures.
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentProtection:Columns:0"] = "RunInputt",
            ["Tracon:ContentProtection:Columns:1"] = "ToolResultt",
        });

        failure.Message.ShouldContain("Tracon:ContentProtection:Columns:0 is 'RunInputt'");
        failure.Message.ShouldContain("Tracon:ContentProtection:Columns:1 is 'ToolResultt'");
    }

    [Fact]
    public void An_empty_key_name_fails_at_startup()
    {
        var failure = SectionBinding.FailsAtStartup(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentProtection:Enabled"] = "false",
            ["Tracon:ContentProtection:Keys:old"] = "",
        });

        failure.Message.ShouldContain("TraconContentProtectionOptions.Keys['old']");
    }

    [Fact]
    public void Code_that_rewrites_the_members_wins_over_unreadable_entries()
    {
        // The failure comes from the bound value (K4, the F-385 precedent): code
        // that configures the same member after binding clears it.
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:ContentProtection:Keys:old"] = "",
            ["Tracon:ContentProtection:Columns:0"] = "RunInputt",
        };

        SectionBinding.StartsCleanly(values, static (builder, _) => builder.Services.Configure<TraconContentProtectionOptions>(static options =>
        {
            options.Keys["old"] = "Tracon:ContentProtection:RawKeys:old";
            options.Columns.Clear();
            options.Columns.Add(ProtectedColumn.RunInput);
        }));
    }

    [Fact]
    public void An_undefined_column_from_code_fails_at_startup_too()
    {
        // The check rejects the undefined value whatever its source: code is not
        // a way around it.
        var failure = SectionBinding.FailsAtStartup(SectionBinding.NoKeys, static (builder, _) =>
            builder.Services.Configure<TraconContentProtectionOptions>(static options => options.Columns.Add((ProtectedColumn)99)));

        failure.Message.ShouldContain("TraconContentProtectionOptions.Columns contains 99");
    }

    [Fact]
    public void An_absent_section_leaves_the_shipped_defaults()
    {
        var options = SectionBinding.Bind<TraconContentProtectionOptions>(SectionBinding.NoKeys);

        options.Enabled.ShouldBeFalse();
        options.ActiveKeyId.ShouldBeNull();
        options.Keys.ShouldBeEmpty();

        // Every member of ProtectedColumn - twelve today (K-934).
        options.Columns.Count.ShouldBe(12);
        options.Columns.ShouldBe(Enum.GetValues<ProtectedColumn>(), ignoreOrder: true);
    }
}

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:Sqlite</c> reaches <see cref="TraconSqliteOptions"/>.
/// </summary>
/// <remarks>
/// <see cref="TraconSqliteOptions.DataSource"/> is a live data source object that a host passes in code; no key names it
/// and it is exempt, with the assertion that a configuration leaves it unset.
/// </remarks>
public sealed class TraconSqliteOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:Sqlite:ConnectionString"] = "Data Source=binding-probe.db",
            ["Tracon:Sqlite:TablePrefix"] = "probe_",
            ["Tracon:Sqlite:AutoApplyMigrations"] = "false",
            ["Tracon:Sqlite:CommandTimeoutSeconds"] = "45",
            ["Tracon:Sqlite:EnableReadViews"] = "true",
        };

        var options = SectionBinding.Bind<TraconSqliteOptions>(
            values, static (builder, tracon) => builder.UseSqlite(tracon.GetSection("Sqlite")));

        options.ConnectionString.ShouldBe("Data Source=binding-probe.db");
        options.TablePrefix.ShouldBe("probe_");
        options.AutoApplyMigrations.ShouldBeFalse();
        options.CommandTimeoutSeconds.ShouldBe(45);
        options.EnableReadViews.ShouldBeTrue();
        options.DataSource.ShouldBeNull();
        OptionsKeyCoverage.ShouldCoverEveryMember(
            values, "Tracon:Sqlite", options, nameof(TraconSqliteOptions.DataSource));
    }
}

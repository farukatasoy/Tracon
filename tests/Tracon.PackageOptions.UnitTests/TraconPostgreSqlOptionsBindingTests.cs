namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:PostgreSql</c> reaches <see cref="TraconPostgreSqlOptions"/>.
/// </summary>
/// <remarks>
/// <see cref="TraconPostgreSqlOptions.DataSource"/> is a live data source object that a host passes in code; no key names it
/// and it is exempt, with the assertion that a configuration leaves it unset.
/// </remarks>
public sealed class TraconPostgreSqlOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:PostgreSql:ConnectionString"] = "Host=probe.invalid;Database=probe;Username=probe;Password=probe",
            ["Tracon:PostgreSql:SchemaName"] = "binding_probe",
            ["Tracon:PostgreSql:AutoApplyMigrations"] = "false",
            ["Tracon:PostgreSql:CommandTimeoutSeconds"] = "45",
            ["Tracon:PostgreSql:EnableKnowledge"] = "true",
            ["Tracon:PostgreSql:EnableReadViews"] = "true",
        };

        var options = SectionBinding.Bind<TraconPostgreSqlOptions>(
            values, static (builder, tracon) => builder.UsePostgreSql(tracon.GetSection("PostgreSql")));

        options.ConnectionString.ShouldBe("Host=probe.invalid;Database=probe;Username=probe;Password=probe");
        options.SchemaName.ShouldBe("binding_probe");
        options.AutoApplyMigrations.ShouldBeFalse();
        options.CommandTimeoutSeconds.ShouldBe(45);
        options.EnableKnowledge.ShouldBeTrue();
        options.EnableReadViews.ShouldBeTrue();
        options.DataSource.ShouldBeNull();
        OptionsKeyCoverage.ShouldCoverEveryMember(
            values, "Tracon:PostgreSql", options, nameof(TraconPostgreSqlOptions.DataSource));
    }
}

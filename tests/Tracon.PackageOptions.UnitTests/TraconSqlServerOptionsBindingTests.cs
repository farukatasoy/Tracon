namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Every key of <c>Tracon:SqlServer</c> reaches <see cref="TraconSqlServerOptions"/>.
/// </summary>
/// <remarks>
/// <see cref="TraconSqlServerOptions.DataSource"/> is a live data source object that a host passes in code; no key names it
/// and it is exempt, with the assertion that a configuration leaves it unset.
/// </remarks>
public sealed class TraconSqlServerOptionsBindingTests
{
    [Fact]
    public void Every_member_of_the_section_is_bound()
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Tracon:SqlServer:ConnectionString"] = "Server=probe.invalid;Database=probe;User Id=probe;Password=probe;TrustServerCertificate=true",
            ["Tracon:SqlServer:SchemaName"] = "binding_probe",
            ["Tracon:SqlServer:AutoApplyMigrations"] = "false",
            ["Tracon:SqlServer:CommandTimeoutSeconds"] = "45",
            ["Tracon:SqlServer:EnableReadViews"] = "true",
        };

        var options = SectionBinding.Bind<TraconSqlServerOptions>(
            values, static (builder, tracon) => builder.UseSqlServer(tracon.GetSection("SqlServer")));

        options.ConnectionString.ShouldBe("Server=probe.invalid;Database=probe;User Id=probe;Password=probe;TrustServerCertificate=true");
        options.SchemaName.ShouldBe("binding_probe");
        options.AutoApplyMigrations.ShouldBeFalse();
        options.CommandTimeoutSeconds.ShouldBe(45);
        options.EnableReadViews.ShouldBeTrue();
        options.DataSource.ShouldBeNull();
        OptionsKeyCoverage.ShouldCoverEveryMember(
            values, "Tracon:SqlServer", options, nameof(TraconSqlServerOptions.DataSource));
    }
}

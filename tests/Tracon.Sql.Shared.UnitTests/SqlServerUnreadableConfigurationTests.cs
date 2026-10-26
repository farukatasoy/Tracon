using Microsoft.Extensions.Configuration;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// The <c>Tracon:SqlServer</c> binder refuses text it cannot read (F-572). The cases live in
/// <see cref="SqlUnreadableConfigurationTests{TOptions}"/>.
/// </summary>
public sealed class SqlServerUnreadableConfigurationTests : SqlUnreadableConfigurationTests<TraconSqlServerOptions>
{
    /// <inheritdoc />
    protected override string SectionName => TraconSqlServerOptions.SectionName;

    /// <inheritdoc />
    protected override string PlaceholderConnectionString
        => "Server=localhost,1;Database=unused;Integrated Security=true;TrustServerCertificate=true";

    /// <inheritdoc />
    protected override void Use(ITraconBuilder builder, IConfiguration section) => builder.UseSqlServer(section);
}

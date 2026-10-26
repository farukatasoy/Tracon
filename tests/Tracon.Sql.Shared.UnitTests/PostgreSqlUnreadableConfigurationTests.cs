using Microsoft.Extensions.Configuration;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// The <c>Tracon:PostgreSql</c> binder refuses text it cannot read (F-572). The cases live in
/// <see cref="SqlUnreadableConfigurationTests{TOptions}"/>.
/// </summary>
public sealed class PostgreSqlUnreadableConfigurationTests : SqlUnreadableConfigurationTests<TraconPostgreSqlOptions>
{
    /// <inheritdoc />
    protected override string SectionName => TraconPostgreSqlOptions.SectionName;

    /// <inheritdoc />
    protected override string PlaceholderConnectionString => "Host=localhost;Port=1;Database=unused;Username=unused";

    /// <inheritdoc />
    protected override void Use(ITraconBuilder builder, IConfiguration section) => builder.UsePostgreSql(section);
}

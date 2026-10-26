using Microsoft.Extensions.Configuration;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// The <c>Tracon:Sqlite</c> binder refuses text it cannot read (F-572). The cases live in
/// <see cref="SqlUnreadableConfigurationTests{TOptions}"/>.
/// </summary>
public sealed class SqliteUnreadableConfigurationTests : SqlUnreadableConfigurationTests<TraconSqliteOptions>
{
    /// <inheritdoc />
    protected override string SectionName => TraconSqliteOptions.SectionName;

    // The options validator rejects a bare ':memory:'; a named in-memory URI is the accepted form.
    /// <inheritdoc />
    protected override string PlaceholderConnectionString => "Data Source=file:unreadable-configuration?mode=memory&cache=shared";

    /// <inheritdoc />
    protected override void Use(ITraconBuilder builder, IConfiguration section) => builder.UseSqlite(section);
}

using Tracon.SqlServer.IntegrationTests.Infrastructure;

// The SQL Server container starts once for the whole assembly. The schema is
// shared per class; data is reset for every test. The image needs
// ~2 GB of memory; watch the CI resource limit.
[assembly: AssemblyFixture(typeof(SqlServerFixture))]

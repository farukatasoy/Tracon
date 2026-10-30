using Tracon.PostgreSql.IntegrationTests.Infrastructure;

// The PostgreSQL container starts once for the whole assembly. The schema is
// shared per class; data is reset for every test.
[assembly: AssemblyFixture(typeof(PostgresFixture))]

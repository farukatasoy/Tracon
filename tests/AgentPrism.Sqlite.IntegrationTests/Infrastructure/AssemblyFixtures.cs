using AgentPrism.Sqlite.IntegrationTests.Infrastructure;

// A single SQLite file created once for the whole assembly. The table prefix
// is shared per class (K-390); data is reset for every test.
[assembly: AssemblyFixture(typeof(SqliteFixture))]

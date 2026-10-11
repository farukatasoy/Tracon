using Tracon.Sqlite.IntegrationTests.Infrastructure;

// One temporary directory for the whole assembly; every table prefix gets its
// own database file inside it (F-307). The table prefix is shared per class
// (K-390); data is reset for every test.
[assembly: AssemblyFixture(typeof(SqliteFixture))]

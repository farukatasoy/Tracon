using Tracon.Sqlite.IntegrationTests.Infrastructure;

// One temporary directory for the whole assembly; every table prefix gets its
// own database file inside it. The table prefix is shared per class
// Data is reset for every test.
[assembly: AssemblyFixture(typeof(SqliteFixture))]

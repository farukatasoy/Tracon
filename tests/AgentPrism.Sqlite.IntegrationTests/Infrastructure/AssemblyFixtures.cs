using AgentPrism.Sqlite.IntegrationTests.Infrastructure;

// Tum derleme icin bir kez olusan tek bir SQLite dosyasi. Testler ayri tablo
// onekleriyle yalitilir.
[assembly: AssemblyFixture(typeof(SqliteFixture))]

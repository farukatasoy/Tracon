using AgentPrism.Sqlite.IntegrationTests.Infrastructure;

// Tum derleme icin bir kez olusan tek bir SQLite dosyasi. Tablo oneki sinif
// basina paylasilir (K-390), veri her testte sifirlanir.
[assembly: AssemblyFixture(typeof(SqliteFixture))]

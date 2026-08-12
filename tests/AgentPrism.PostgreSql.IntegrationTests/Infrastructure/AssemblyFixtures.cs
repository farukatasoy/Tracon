using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

// PostgreSQL container'i tum derleme icin bir kez baslar. Sema sinif basina
// paylasilir (K-390), veri her testte sifirlanir.
[assembly: AssemblyFixture(typeof(PostgresFixture))]

using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

// PostgreSQL container'i tum derleme icin bir kez baslar. Testler ayri semalarla yalitilir.
[assembly: AssemblyFixture(typeof(PostgresFixture))]

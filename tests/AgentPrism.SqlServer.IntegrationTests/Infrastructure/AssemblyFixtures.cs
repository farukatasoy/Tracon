using AgentPrism.SqlServer.IntegrationTests.Infrastructure;

// SQL Server container'i tum derleme icin bir kez baslar. Testler ayri semalarla
// yalitilir. Imaj ~2 GB bellek ister; CI kaynak sinirina dikkat.
[assembly: AssemblyFixture(typeof(SqlServerFixture))]

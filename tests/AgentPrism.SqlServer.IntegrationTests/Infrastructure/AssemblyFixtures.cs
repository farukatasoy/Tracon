using AgentPrism.SqlServer.IntegrationTests.Infrastructure;

// SQL Server container'i tum derleme icin bir kez baslar. Sema sinif basina
// paylasilir (K-390), veri her testte sifirlanir. Imaj ~2 GB bellek ister;
// CI kaynak sinirina dikkat.
[assembly: AssemblyFixture(typeof(SqlServerFixture))]

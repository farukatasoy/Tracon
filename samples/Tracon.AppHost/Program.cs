// Starts the Tracon sample API on a pgvector-enabled PostgreSQL and opens the Aspire dashboard.
//
//   dotnet run --project samples/Tracon.AppHost
//
// The dashboard lists the API and the database. Its Traces page shows `tracon.run` and `chat` spans once an agent
// runs; its Metrics page lists the `Tracon` meter. Docker (or another container runtime Aspire supports) must be running.
//
// The model provider keys are NOT set here. Put them in the API's own user-secrets, as without the AppHost:
//   dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "sk-..." --project samples/Tracon.Api

var builder = DistributedApplication.CreateBuilder(args);

// 🚨 The image MUST carry the `vector` extension. Aspire's default PostgreSQL image does not, and Tracon's PostgreSQL
// migration set runs `CREATE EXTENSION IF NOT EXISTS vector;`, so the sample API would never start (K-346). `pg18` is the
// tag the integration tests and the capacity runs use.
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg18");

var database = postgres.AddDatabase("tracon");

// 🚨 Tracon reads `Tracon:PostgreSql:ConnectionString`; `WithReference` would write `ConnectionStrings__tracon`, which
// the API never reads. The environment variable below is the same key in the `__` form, and it wins over the user-secrets
// value the API may carry for a shared local database.
builder.AddProject<Projects.Tracon_Api>("tracon-api")
    .WithEnvironment("Tracon__PostgreSql__ConnectionString", database)
    .WaitFor(database);

builder.Build().Run();

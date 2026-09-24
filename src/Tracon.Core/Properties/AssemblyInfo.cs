using System.Runtime.CompilerServices;

// The optional ASP.NET Core endpoint uses Core's single image attachment and
// pricing path. Keeping that path internal prevents it from becoming a second
// consumer-facing image abstraction while avoiding duplicate security code.
[assembly: InternalsVisibleTo("Tracon.AspNetCore")]

// Tracon.Mcp wraps the code-defined tool registry (McpToolRegistry) and
// must build its inner registry through ToolRegistry.Create, not duplicate
// the construction. A duplicated build site silently missed the phase-88
// image-tool gate: `.UseMcp(...)` replaced IToolRegistry with a registry
// built from the raw registrations, so an enabled `generate_image` tool
// never appeared once MCP was also configured (measured 2026-08-23).
[assembly: InternalsVisibleTo("Tracon.Mcp")]

// Auditing* stores and InMemoryRunStore are used by the three SQL provider
// packages in real code (Sql.Shared is a linked-source, not its own
// assembly - K-176), so visibility is granted per provider package.
[assembly: InternalsVisibleTo("Tracon.PostgreSql")]
[assembly: InternalsVisibleTo("Tracon.SqlServer")]
[assembly: InternalsVisibleTo("Tracon.Sqlite")]

// Tracon.Voice validates generated audio with the same attachment guard the
// upload path uses, Tracon.Workflows validates a definition with the same
// validator the HTTP layer uses, and the `tracon` tool prints the state
// preflight report. None of these types is a consumer seam; they were public
// only so that a sibling package could reach them.
[assembly: InternalsVisibleTo("Tracon.Voice")]
[assembly: InternalsVisibleTo("Tracon.Workflows")]
[assembly: InternalsVisibleTo("Tracon.Cli")]

// Phase 96: the default $(MSBuildProjectName).UnitTests/.IntegrationTests
// pattern only covers a test project that shares Core's own project name
// prefix. These seven test projects exercise Core in-memory stores through a
// sibling package's wiring (MCP discovery/tenant tools, workflow test
// fixtures, an ASP.NET Core functional test, SQL content-protection
// integration) and were left with real dependencies on now-internal types
// when those types were narrowed.
[assembly: InternalsVisibleTo("Tracon.Mcp.UnitTests")]
[assembly: InternalsVisibleTo("Tracon.Workflows.UnitTests")]
[assembly: InternalsVisibleTo("Tracon.AspNetCore.FunctionalTests")]
[assembly: InternalsVisibleTo("Tracon.PostgreSql.IntegrationTests")]
[assembly: InternalsVisibleTo("Tracon.SqlServer.IntegrationTests")]
[assembly: InternalsVisibleTo("Tracon.Sqlite.IntegrationTests")]
[assembly: InternalsVisibleTo("Tracon.Core.UnitTests")]

// The voice tests register the attachment guard Tracon.Voice resolves, and the
// shared provider tests (tests/Shared/Providers) build an egress guard for each
// of the four adapters - both became internal with the evidence pass over the
// public surface.
[assembly: InternalsVisibleTo("Tracon.Voice.UnitTests")]
[assembly: InternalsVisibleTo("Tracon.Anthropic.UnitTests")]
[assembly: InternalsVisibleTo("Tracon.Azure.UnitTests")]
[assembly: InternalsVisibleTo("Tracon.Google.UnitTests")]
[assembly: InternalsVisibleTo("Tracon.OpenAI.UnitTests")]

// The RunEventWriter benchmark measures the writer directly. Building it
// through the run pipeline would change the measured path and move the
// allocation baseline (bench/baseline.json).
[assembly: InternalsVisibleTo("Tracon.Benchmarks")]

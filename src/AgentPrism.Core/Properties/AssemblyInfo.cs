using System.Runtime.CompilerServices;

// The optional ASP.NET Core endpoint uses Core's single image attachment and
// pricing path. Keeping that path internal prevents it from becoming a second
// consumer-facing image abstraction while avoiding duplicate security code.
[assembly: InternalsVisibleTo("AgentPrism.AspNetCore")]

// AgentPrism.Mcp wraps the code-defined tool registry (McpToolRegistry) and
// must build its inner registry through ToolRegistry.Create, not duplicate
// the construction. A duplicated build site silently missed the phase-88
// image-tool gate: `.UseMcp(...)` replaced IToolRegistry with a registry
// built from the raw registrations, so an enabled `generate_image` tool
// never appeared once MCP was also configured (measured 2026-08-23).
[assembly: InternalsVisibleTo("AgentPrism.Mcp")]

// Auditing* stores and InMemoryRunStore are used by the three SQL provider
// packages in real code (Sql.Shared is a linked-source, not its own
// assembly - K-176), so visibility is granted per provider package.
[assembly: InternalsVisibleTo("AgentPrism.PostgreSql")]
[assembly: InternalsVisibleTo("AgentPrism.SqlServer")]
[assembly: InternalsVisibleTo("AgentPrism.Sqlite")]

// Phase 96: the default $(MSBuildProjectName).UnitTests/.IntegrationTests
// pattern only covers a test project that shares Core's own project name
// prefix. These seven test projects exercise Core in-memory stores through a
// sibling package's wiring (MCP discovery/tenant tools, workflow test
// fixtures, an ASP.NET Core functional test, SQL content-protection
// integration) and were left with real dependencies on now-internal types
// when those types were narrowed.
[assembly: InternalsVisibleTo("AgentPrism.Mcp.UnitTests")]
[assembly: InternalsVisibleTo("AgentPrism.Workflows.UnitTests")]
[assembly: InternalsVisibleTo("AgentPrism.AspNetCore.FunctionalTests")]
[assembly: InternalsVisibleTo("AgentPrism.PostgreSql.IntegrationTests")]
[assembly: InternalsVisibleTo("AgentPrism.SqlServer.IntegrationTests")]
[assembly: InternalsVisibleTo("AgentPrism.Sqlite.IntegrationTests")]
[assembly: InternalsVisibleTo("AgentPrism.Core.UnitTests")]

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

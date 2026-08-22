using System.Runtime.CompilerServices;

// The optional ASP.NET Core endpoint uses Core's single image attachment and
// pricing path. Keeping that path internal prevents it from becoming a second
// consumer-facing image abstraction while avoiding duplicate security code.
[assembly: InternalsVisibleTo("AgentPrism.AspNetCore")]

using System.Runtime.CompilerServices;

// The default $(MSBuildProjectName).FunctionalTests pattern covers only a test project that shares
// this package's name. The two-instance OAuth tests live in Tracon.AspNetCore.FunctionalTests, next
// to the two-host fixture, and need the distributed OAuth state (the token cache, its timing and the
// key builder) to seed an expired token and to shorten the polling intervals; neither is a
// consumer seam.
[assembly: InternalsVisibleTo("Tracon.AspNetCore.FunctionalTests")]

// Phase 265: the two-node acceptance set runs the same OAuth scenarios on PostgreSQL and SQL Server, with the same
// shortened polling intervals.
[assembly: InternalsVisibleTo("Tracon.TwoNode.IntegrationTests")]

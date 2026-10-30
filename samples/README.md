# Tracon samples

Use `Tracon.Api` for a complete host and the extension samples when you implement
a public Tracon seam.

| Sample | Contract it demonstrates |
|---|---|
| [`Tracon.Samples.FileRunStore`](Tracon.Samples.FileRunStore/) | `IRunStore` and `IRunScoreStore` |
| [`Tracon.Samples.CustomModelProvider`](Tracon.Samples.CustomModelProvider/) | `IModelProvider` |
| [`Tracon.Samples.CustomRunJudge`](Tracon.Samples.CustomRunJudge/) | `IRunJudge` |
| [`Tracon.Samples.CustomAgentSource`](Tracon.Samples.CustomAgentSource/) | `IAgentSource` |
| [`Tracon.Samples.CustomTool`](Tracon.Samples.CustomTool/) | generated and hand-built tools with scoped dependencies |
| [`Tracon.Samples.CustomJobHandler`](Tracon.Samples.CustomJobHandler/) | keyed `IJobHandler` registration |
| [`Tracon.Samples.MafFeatures`](Tracon.Samples.MafFeatures/) | four doors to a Microsoft Agent Framework feature: an `IAgentDecorator`, an agent extension, a factory agent, and an `IAgentSource` that builds its agent |

Each extension sample has a sibling `.Tests` project. These projects use exact
`PackageReference` versions against packed artifacts. They do not use
`ProjectReference` and do not belong to `Tracon.slnx`: that isolation proves a
third-party consumer can compile and pass the same public contract suites without
access to Tracon source projects.

The release gate packs Tracon, restores every sample into an isolated NuGet cache,
runs all seven test projects, publishes the AOT smoke sample, and runs the `net8.0`
consumer on the .NET 8 runtime. See
[`CONTRIBUTING.md`](../CONTRIBUTING.md) for the current release-gate command.

## Other hosts

- [`Tracon.Api`](Tracon.Api/) is the main ASP.NET Core sample.
- [`Tracon.Embedded`](Tracon.Embedded/) shows the embedded deployment shape.
- [`Tracon.AppHost`](Tracon.AppHost/) is an Aspire AppHost that starts `Tracon.Api` on a pgvector PostgreSQL and opens the
  Aspire dashboard. Run it with `dotnet run --project samples/Tracon.AppHost`; Docker must be running.
- [`Tracon.Samples.Net8Consumer`](Tracon.Samples.Net8Consumer/) is a `net8.0` console
  application that takes `Tracon.Core` from the packed feed and runs one agent.

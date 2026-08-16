# AgentPrism.Sql.Shared — shared source

> This is **not** a NuGet package and has **no** `.csproj` file of its own.
> The `.cs` files here are compiled directly into each SQL persistence package
> via `<Compile Include="..." />`.

## Why not a package

Publishing a third package would produce a dependency the consumer would never use
directly, and would add a release burden on every version. Sharing source avoids
code duplication without adding to the package count.

Rationale: `docs/KARARLAR.md`, decision K-176.

## Who compiles it

| Package | How |
|-------|-------|
| `AgentPrism.PostgreSql` | `<Compile Include="../AgentPrism.Sql.Shared/**/*.cs" />` |
| `AgentPrism.SqlServer` | same |

In both assemblies the types live in the `AgentPrism` namespace and are `internal`;
since a type with the same name lives in two **separate** assemblies, there is no
conflict.

## What belongs here, what doesn't

| Belongs | Doesn't belong |
|-------|--------|
| Provider-independent store implementations (`Stores/Sql*Store.cs`) | SQL text (`SqlQueriesBase` subclasses) |
| Helpers built on `DbCommand` / `DbDataReader` | Embedded `.sql` migration files |
| Migration runner scaffolding and checksum computation | Connection string / data source setup |
| Definition payload DTOs, JSON source generator context | `Options` classes and `Use*` extensions |

**Rule:** no file here may reference the `Npgsql` or `Microsoft.Data.SqlClient`
namespace. Anything provider-specific goes through `SqlDialect`.

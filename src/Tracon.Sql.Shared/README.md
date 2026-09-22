# Tracon.Sql.Shared — shared source

> This is **not** a NuGet package and has **no** `.csproj` file of its own.
> The `.cs` files here are compiled directly into each SQL persistence package
> via `<Compile Include="..." />`.

## Why not a package

Publishing a third package would produce a dependency the consumer would never use
directly, and would add a release burden on every version. Sharing source avoids
code duplication without adding to the package count.

The dialect is the single gateway for all SQL text.

## Who compiles it

| Package | How |
|-------|-------|
| `Tracon.PostgreSql` | `<Compile Include="../Tracon.Sql.Shared/**/*.cs" />` |
| `Tracon.SqlServer` | same |
| `Tracon.Sqlite` | same |

In all three assemblies the types live in the `Tracon` namespace and are
`internal`. **Every type here stays `internal`.** A public type would exist
three times under the same full name, one per provider package: a consumer that
references two providers (the `tracon` tool references all three) cannot name it
without `CS0433`, and each package carries its own copy in `PublicAPI.Unshipped.txt`.
A consumer reaches what these types do through the interfaces in
`Tracon.Abstractions` instead — `IMigrationApplier` and
`ISqlPersistenceDiagnostics` resolve to the active provider's migration runner.

## What belongs here, what doesn't

| Belongs | Doesn't belong |
|-------|--------|
| Provider-independent store implementations (`Stores/Sql*Store.cs`) | SQL text (`SqlQueriesBase` subclasses) |
| Helpers built on `DbCommand` / `DbDataReader` | Embedded `.sql` migration files |
| Migration runner scaffolding and checksum computation | Connection string / data source setup |
| Definition payload DTOs, JSON source generator context | `Options` classes and `Use*` extensions |

**Rule:** no file here may reference the `Npgsql` or `Microsoft.Data.SqlClient`
namespace. Anything provider-specific goes through `SqlDialect`.

## Links

- Guide: <https://tracon.dev/getting-started/persistence/>
- Capability map: <https://tracon.dev/capabilities/>
- API reference: <https://tracon.dev/api/>

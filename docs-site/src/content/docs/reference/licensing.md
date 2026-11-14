---
title: Licensing
description: Every Tracon package is MIT. What the package declares, what it carries, and which terms earlier previews shipped with.
---

Every Tracon package is **MIT**, and so is `@tracon/client` on npm. Nothing in any
package checks, enforces or reports your licence.

## What a package carries

| Where | What you find |
|-------|---------------|
| `.nuspec` | `<license type="expression">MIT</license>` — nuget.org and a licence scanner read this |
| Package root | `LICENSE.md`, the MIT text with the copyright notice |
| npm `package.json` | `"license": "MIT"`, and `LICENSE.md` in the published files |

No package asks you to accept a licence when you restore it.

## There is no licence key

No Tracon package contains an activation call, a licence check, a phone-home
request, a trial timer or a feature gate. Nothing degrades, expires or refuses to
start.

## Earlier versions

`1.0.0-preview.10` and every earlier preview were published under the **PolyForm
Small Business License 1.0.0**, with three packages (`Tracon.Abstractions`,
`Tracon.Testing.Contracts.Xunit`, `Tracon.Templates`) under MIT. The licence of a
version that is already on NuGet or npm never changes: a version you restored keeps
the terms it shipped with, and the text inside that package is the one that applies.
To use the MIT terms, take a later version.

## Questions a procurement review usually asks

**Is this an OSI approved open source licence?** Yes. The SPDX identifier is `MIT`.

**May we modify and redistribute it?** Yes, including in a commercial product. Keep
the copyright notice and the permission notice in every copy.

**Is there a patent grant?** The MIT licence does not contain an explicit patent
grant.

**Does a package contain third-party code?** Two do. `Tracon.UI` embeds a console that is a
JavaScript and CSS bundle containing React, React DOM, `scheduler`, TanStack Query,
`openapi-fetch`, and Tailwind CSS, all under the MIT licence. `Tracon.Cli`, the `tracon` tool,
ships the NuGet packages it runs on (the database drivers Npgsql, `Microsoft.Data.SqlClient` and
SQLitePCLRaw, Google.Protobuf, and the Microsoft libraries underneath them) as assemblies and
native libraries; their licences are MIT, BSD-3-Clause, Apache-2.0, the PostgreSQL licence, and the
licence of the SQL Server network library. For both, the notices ship as `THIRD-PARTY-NOTICES.txt`
at the package root. `Tracon.UI` also embeds its copy in `Tracon.UI.dll`, next to the bundle. The
release build checks each file against what the package really contains: the modules of the bundle,
and the packages in the tool's dependency manifest.

## The full terms

`LICENSE.md` at the root of the repository and at the root of every package.

## Read next

- [Choosing packages](/packages/) — which packages you actually take a version of
- [Versions and upgrades](/reference/versioning/) — the version line these terms attach to
- [Write your own store](/guides/write-your-own-store/) — the extension path through `Tracon.Abstractions`

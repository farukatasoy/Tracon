---
title: Licensing
description: Free for individuals, open source projects and small companies; larger companies buy a commercial licence. Which package carries which terms, and why.
---

Tracon ships under the **PolyForm Small Business License 1.0.0**. Three
packages are MIT instead. Nothing in any package checks, enforces or reports your
licence.

## Are you free to use it?

Yes, at no cost, if your company has **fewer than 100 total individuals** working as
employees and independent contractors, **and less than 1,000,000 USD** (2019,
adjusted for inflation) total revenue in the prior tax year.

An individual, a student, a hobby project and an open source project are all under
that threshold.

Above the threshold, using Tracon needs a commercial licence. Write to
<hfarukatasoy@gmail.com>.

The clause that governs is the **Small Business** section of the licence text
itself. The summary here is a reading aid, not the terms.

## Which package is which

| Licence | Packages |
|---------|----------|
| MIT | `Tracon.Abstractions` · `Tracon.Testing.Contracts.Xunit` · `Tracon.Templates` |
| PolyForm Small Business 1.0.0 | Every other package, and `@tracon/client` on npm |

Each package carries its own licence text inside the `.nupkg`, and states it in the
package metadata. If a package's metadata and the file inside it ever disagree, the
file inside the package governs.

### Why those three are MIT

The split follows one rule: **everything you need in order to extend Tracon, or
to own the code it generates for you, is free of the threshold. Everything that
actually runs an agent is not.**

- **`Tracon.Abstractions`** carries interfaces and record types, nothing that
  runs. You implement `IRunStore`, `IModelProvider`, `IRunJudge`, `IAgentSource` or
  `IJobHandler` against it.
- **`Tracon.Testing.Contracts.Xunit`** carries the behaviour contract suites
  your implementation has to pass — the same scenarios the shipped implementations
  run. It depends on `Tracon.Abstractions` and nothing else, so the whole path
  from writing an extension to proving it correct stays MIT end to end.
- **`Tracon.Templates`** generates source into your project. That code is yours,
  under no obligation to these terms.

A licence scanner resolves transitive dependencies, so an MIT package that depended
on a PolyForm one would stop your build anyway and the MIT label would buy you
nothing. A test in the repository enforces that this never happens.

## There is no licence key

No Tracon package contains an activation call, a licence check, a phone-home
request, a trial timer or a feature gate. Nothing degrades, expires or refuses to
start. Compliance is yours to determine, the same way it is for every other
dependency your build already carries.

This is a deliberate product decision, not an omission, and it will not change for
a capability that already shipped.

## A published version keeps its licence

The licence of a version that is already on NuGet or npm never changes. If the
terms are ever revised, the revision applies to versions published after that
point; what you already restored stays under the terms it shipped with.

## Questions a procurement review usually asks

**Is this an OSI approved open source licence?** No, and deliberately so. PolyForm
Small Business is a source-available licence with a size threshold. Its SPDX
identifier is `PolyForm-Small-Business-1.0.0`.

**May we modify it?** Yes. The licence grants a copyright licence to make changes
and new works, and to distribute copies, within a permitted purpose.

**Is there a patent grant?** Yes, with a defensive termination clause: asserting a
patent claim against the software ends your patent licence.

**What happens if we cross the threshold mid-term?** Use for the benefit of your
company stops being a permitted purpose, and you need a commercial licence from
that point. Get in touch and it will be handled without drama.

**What if we are already in breach?** The licence gives you 32 days from written
notice to come into compliance, after which the licences end.

## The full terms

The canonical text is reproduced without modification in `LICENSE.md`, which ships
inside every PolyForm-licensed package, and in `LICENSE-MIT.md` for the three MIT
ones. Both are also at the root of the repository.

## Read next

- [Choosing packages](/packages/) — which packages you actually take a version of
- [Versions and upgrades](/reference/versioning/) — the version line these terms attach to
- [Write your own store](/guides/write-your-own-store/) — the extension path the MIT packages exist for

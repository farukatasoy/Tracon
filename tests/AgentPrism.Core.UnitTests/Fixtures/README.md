# Fixtures

Real Microsoft Agent Framework output, captured from a genuine run — not
hand-written. Read by `Sessions/PersistedPayloadUpgradeTests.cs` (Phase 126).

## `session-state-1.18.0.json`

Captured 2026-09-01 against `Microsoft.Agents.AI` 1.18.0 (the version pinned
in `Directory.Packages.props` as `$(MicrosoftAgentsAIVersion)` at the time).
Produced by a throwaway test that compiled an agent through
`AgentDefinitionCompiler`, ran two turns against a `FakeChatClient`, and
called `agent.SerializeSessionAsync(session, jsonSerializerOptions: null, ct)`
on the result — the exact call `AgentSessionManager.SaveSessionAsync` makes
in production. The generator was deleted after the file was captured; only
the fixture remains.

## Renewal rule

**Do not regenerate this file because the test using it turned red.** A red
`PersistedPayloadUpgradeTests` test after bumping
`$(MicrosoftAgentsAIVersion)` means: the new Microsoft Agent Framework
version cannot read a session written by the old one. That is the finding —
replacing the fixture with one captured under the new version would erase
the very thing the test exists to catch. Treat a break as a real
compatibility question and take it to the `nuget-danismani` skill, not as a
fixture maintenance chore.

A new fixture is added — never replacing this one — only when AgentPrism's
own envelope (`SessionRecord.StateSchemaVersion`) advances, so both
generations stay covered.

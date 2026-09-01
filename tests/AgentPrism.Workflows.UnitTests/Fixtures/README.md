# Fixtures

Real Microsoft Agent Framework Workflows output, captured from a genuine run
— not hand-written. Read by `PersistedPayloadUpgradeTests.cs` (Phase 126).

## `workflow-checkpoint-1.18.0.json`

Captured 2026-09-01 against `Microsoft.Agents.AI.Workflows` 1.18.0 (the
version pinned in `Directory.Packages.props` as
`$(MicrosoftAgentsAIVersion)` at the time). Produced by a throwaway test
that ran a two-agent Sequential workflow through `WorkflowTestHost`,
`ListByRunAsync`'d the checkpoints it wrote, and read the LAST one's full
state back with `IWorkflowCheckpointStore.ReadAsync` — the same opaque
payload `AgentPrismCheckpointStore.CreateCheckpointAsync` writes in
production. The generator was deleted after the file was captured; only the
fixture remains.

## Renewal rule

**Do not regenerate this file because the test using it turned red.** A red
`PersistedPayloadUpgradeTests` test after bumping
`$(MicrosoftAgentsAIVersion)` means: the new Microsoft Agent Framework
Workflows version changed the checkpoint state shape enough to break the
`$type`-discriminator-first round-trip this test checks (see the test's own
remarks for why a full `ResumeStreamingAsync` proof is not possible here).
That is the finding — replacing the fixture with one captured under the new
version would erase the very thing the test exists to catch. Treat a break
as a real compatibility question and take it to the `nuget-danismani` skill,
not as a fixture maintenance chore.

A new fixture is added — never replacing this one — only when AgentPrism's
own envelope (`WorkflowCheckpointRecord.StateSchemaVersion`) advances, so
both generations stay covered.

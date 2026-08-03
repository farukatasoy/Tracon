import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { useNavigate } from '../lib/router';
import {
  Badge,
  Button,
  ErrorNote,
  Field,
  Loading,
  PageHeader,
  Panel,
  Select,
  TextArea,
  TextInput,
  cx,
} from '../components/ui';
import { KIND_HINT } from './workflows';
import type { WorkflowKind, WorkflowSaveRequest } from '../lib/types';

const KINDS: WorkflowKind[] = ['Sequential', 'Concurrent', 'Handoff', 'GroupChat', 'Magentic'];

/** Patterns that need at least two participants to mean anything. */
const NEEDS_TWO: WorkflowKind[] = ['Concurrent', 'Handoff', 'GroupChat'];

interface Draft {
  name: string;
  displayName: string;
  description: string;
  kind: WorkflowKind;
  agentNames: string[];
  managerAgentName: string;
  maxIterations: string;
  handoffInstructions: string;
  requirePlanApproval: boolean;
}

const EMPTY: Draft = {
  name: '',
  displayName: '',
  description: '',
  kind: 'Sequential',
  agentNames: [],
  managerAgentName: '',
  maxIterations: '',
  handoffInstructions: '',
  requirePlanApproval: false,
};

/**
 * Create or edit a workflow definition.
 *
 * The form only wires together agents that already exist — it never defines
 * behaviour. That is the security boundary from decision K2: a workflow built
 * here arranges the catalogue, it cannot add to it.
 *
 * Validation is deliberately *not* duplicated from the server. The rules live
 * in `WorkflowDefinitionValidator` and both the save endpoint and the compiler
 * use them; a second copy here would drift. The form only disables the button
 * for the obvious cases and shows the server's own message otherwise.
 */
export function WorkflowEditorScreen({ name }: { name?: string }): ReactNode {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const editing = name !== undefined;

  const agents = useQuery({ queryKey: ['agents'], queryFn: api.agents });

  const existing = useQuery({
    queryKey: ['workflow', name],
    queryFn: () => api.workflow(name as string),
    enabled: editing,
  });

  const [draft, setDraft] = useState<Draft>(EMPTY);

  useEffect(() => {
    const loaded = existing.data;

    if (loaded === undefined) {
      return;
    }

    setDraft({
      name: loaded.name,
      displayName: loaded.displayName ?? '',
      description: loaded.description ?? '',
      kind: loaded.kind,
      agentNames: loaded.agentNames,
      managerAgentName: loaded.managerAgentName ?? '',
      maxIterations: loaded.maxIterations == null ? '' : String(loaded.maxIterations),
      handoffInstructions: loaded.handoffInstructions ?? '',
      requirePlanApproval: loaded.requirePlanApproval,
    });
  }, [existing.data]);

  const save = useMutation({
    mutationFn: async () => {
      const body: WorkflowSaveRequest = {
        displayName: blank(draft.displayName),
        description: blank(draft.description),
        kind: draft.kind,
        agentNames: draft.agentNames,

        // Fields that belong to another pattern are sent as null, not as an
        // empty string: the server rejects a value that the chosen pattern does
        // not use (decision K-125), and "" is a value.
        managerAgentName: draft.kind === 'Magentic' ? blank(draft.managerAgentName) : null,
        maxIterations: draft.maxIterations.length === 0 ? null : Number(draft.maxIterations),
        handoffInstructions: draft.kind === 'Handoff' ? blank(draft.handoffInstructions) : null,
        requirePlanApproval: draft.kind === 'Magentic' && draft.requirePlanApproval,
      };

      return api.saveWorkflow(draft.name.trim(), body);
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['workflows'] });
      await queryClient.invalidateQueries({ queryKey: ['workflow', saved.name] });
      navigate(`workflows/${encodeURIComponent(saved.name)}`);
    },
  });

  if (editing && existing.isPending) {
    return <Loading />;
  }

  if (editing && existing.isError) {
    return <ErrorNote error={existing.error} />;
  }

  const available = agents.data ?? [];
  const tooFew = NEEDS_TWO.includes(draft.kind) && draft.agentNames.length < 2;

  const blocked =
    draft.name.trim().length === 0 ||
    draft.agentNames.length === 0 ||
    tooFew ||
    (draft.kind === 'Magentic' && draft.managerAgentName.trim().length === 0);

  return (
    <>
      <PageHeader
        title={editing ? `Edit ${name}` : 'New workflow'}
        description="Pick a pattern and the agents that take part. The graph is compiled from this on every run."
        actions={
          <>
            <Button onClick={() => navigate('workflows')}>Cancel</Button>
            <Button
              tone="primary"
              testId="workflow-save"
              busy={save.isPending}
              disabled={blocked}
              onClick={() => save.mutate()}
            >
              Save
            </Button>
          </>
        }
      />

      {save.isError && (
        <div className="mb-3">
          <ErrorNote error={save.error} />
        </div>
      )}

      <div className="flex flex-col gap-4">
        <Panel title="Identity">
          <div className="grid gap-4 p-4 sm:grid-cols-2">
            <Field label="Name" required hint="Used in the API path. Cannot be changed later.">
              <TextInput
                value={draft.name}
                data-testid="workflow-name"
                disabled={editing}
                placeholder="review-chain"
                onChange={(event) => setDraft({ ...draft, name: event.target.value })}
              />
            </Field>

            <Field label="Display name">
              <TextInput
                value={draft.displayName}
                onChange={(event) => setDraft({ ...draft, displayName: event.target.value })}
              />
            </Field>

            <div className="sm:col-span-2">
              <Field label="Description">
                <TextInput
                  value={draft.description}
                  onChange={(event) => setDraft({ ...draft, description: event.target.value })}
                />
              </Field>
            </div>
          </div>
        </Panel>

        <Panel title="Pattern">
          <div className="flex flex-col gap-4 p-4">
            <Field label="Kind" required hint={KIND_HINT[draft.kind]}>
              <Select
                value={draft.kind}
                onChange={(value) => setDraft({ ...draft, kind: value as WorkflowKind })}
              >
                {KINDS.map((kind) => (
                  <option key={kind} value={kind}>
                    {kind}
                  </option>
                ))}
              </Select>
            </Field>

            <Field
              label="Participants"
              required
              hint={
                draft.kind === 'Sequential'
                  ? 'Order matters: each agent receives the previous one’s output.'
                  : 'Order does not affect this pattern; it only sets the participant list.'
              }
            >
              <AgentPicker
                available={available.map((agent) => agent.name)}
                selected={draft.agentNames}
                onChange={(agentNames) => setDraft({ ...draft, agentNames })}
              />
            </Field>

            {tooFew && (
              <p className="text-[12px] text-warn">
                {draft.kind} needs at least two participants.
              </p>
            )}

            {draft.kind === 'Magentic' && (
              <Field
                label="Manager agent"
                required
                hint="Builds the plan, watches progress and re-plans. Cannot also be a participant."
              >
                <Select
                  value={draft.managerAgentName}
                  onChange={(value) => setDraft({ ...draft, managerAgentName: value })}
                >
                  <option value="">Choose an agent…</option>
                  {available
                    .filter((agent) => !draft.agentNames.includes(agent.name))
                    .map((agent) => (
                      <option key={agent.name} value={agent.name}>
                        {agent.name}
                      </option>
                    ))}
                </Select>
              </Field>
            )}

            {draft.kind === 'Handoff' && (
              <Field
                label="Handoff instructions"
                hint="Extra guidance for deciding when to hand over."
              >
                <TextArea
                  rows={3}
                  value={draft.handoffInstructions}
                  onChange={(event) =>
                    setDraft({ ...draft, handoffInstructions: event.target.value })
                  }
                />
              </Field>
            )}

            {draft.kind !== 'Sequential' && draft.kind !== 'Concurrent' && (
              <Field
                label="Max iterations"
                hint="The only guard against a loop that never ends. Left empty, a built-in default applies."
              >
                <TextInput
                  type="number"
                  min={1}
                  value={draft.maxIterations}
                  onChange={(event) => setDraft({ ...draft, maxIterations: event.target.value })}
                />
              </Field>
            )}

            {draft.kind === 'Magentic' && <PlanApproval draft={draft} onChange={setDraft} />}
          </div>
        </Panel>
      </div>
    </>
  );
}

/**
 * Plan approval toggle, with its cost stated plainly.
 *
 * The manager agent runs on every round and a rejected plan makes it plan
 * again, so this is a real spend — the screen says so rather than leaving the
 * user to discover it on the bill.
 */
function PlanApproval({
  draft,
  onChange,
}: {
  draft: Draft;
  onChange: (draft: Draft) => void;
}): ReactNode {
  return (
    <div className="rounded-md border border-line bg-raised p-3">
      <label className="flex items-start gap-2.5">
        <input
          type="checkbox"
          className="mt-0.5"
          data-testid="workflow-plan-approval"
          checked={draft.requirePlanApproval}
          onChange={(event) => onChange({ ...draft, requirePlanApproval: event.target.checked })}
        />
        <span>
          <span className="flex items-center gap-1.5 text-[13px] font-medium">
            Ask a human to approve the plan
            <Badge tone="warn">costs a manager turn</Badge>
          </span>
          <span className="mt-0.5 block text-[12px] text-muted">
            The run stops after the manager writes its plan and waits for an answer. Its state is
            written to a checkpoint, so answering later continues from exactly that point — in a
            new run. Rejecting the plan makes the manager plan again, which costs another model
            call.
          </span>
        </span>
      </label>
    </div>
  );
}

/** Ordered multi-select. Order is what makes a `Sequential` chain a chain. */
function AgentPicker({
  available,
  selected,
  onChange,
}: {
  available: readonly string[];
  selected: readonly string[];
  onChange: (names: string[]) => void;
}): ReactNode {
  const unselected = available.filter((agent) => !selected.includes(agent));

  return (
    <div className="flex flex-col gap-2">
      <ol className="flex flex-col gap-1.5" data-testid="workflow-participants">
        {selected.map((agent, index) => (
          <li
            key={agent}
            className="flex items-center gap-2 rounded-md border border-line bg-panel px-2.5 py-1.5 text-[13px]"
          >
            <span className="w-5 text-[11px] text-subtle">{index + 1}</span>
            <span className="flex-1">{agent}</span>

            <button
              type="button"
              title="Move up"
              aria-label={`Move ${agent} up`}
              disabled={index === 0}
              className={cx('px-1 text-muted hover:text-fg', index === 0 && 'opacity-30')}
              onClick={() => onChange(swap([...selected], index, index - 1))}
            >
              ↑
            </button>
            <button
              type="button"
              title="Move down"
              aria-label={`Move ${agent} down`}
              disabled={index === selected.length - 1}
              className={cx(
                'px-1 text-muted hover:text-fg',
                index === selected.length - 1 && 'opacity-30',
              )}
              onClick={() => onChange(swap([...selected], index, index + 1))}
            >
              ↓
            </button>
            <button
              type="button"
              aria-label={`Remove ${agent}`}
              className="px-1 text-muted hover:text-danger"
              onClick={() => onChange(selected.filter((name) => name !== agent))}
            >
              ✕
            </button>
          </li>
        ))}
      </ol>

      {unselected.length > 0 && (
        <Select
          value=""
          onChange={(value) => {
            if (value.length > 0) {
              onChange([...selected, value]);
            }
          }}
        >
          <option value="">Add an agent…</option>
          {unselected.map((agent) => (
            <option key={agent} value={agent}>
              {agent}
            </option>
          ))}
        </Select>
      )}
    </div>
  );
}

function swap(items: string[], from: number, to: number): string[] {
  const moved = items[from];
  const replaced = items[to];

  if (moved === undefined || replaced === undefined) {
    return items;
  }

  items[from] = replaced;
  items[to] = moved;

  return items;
}

function blank(value: string): string | null {
  return value.trim().length === 0 ? null : value.trim();
}

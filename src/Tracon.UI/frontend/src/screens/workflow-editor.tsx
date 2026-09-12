import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
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
  type FieldIds,
} from '../components/ui';
import { Tooltip } from '../components/tooltip';
import { KIND_HINT } from './workflows';
import type { WorkflowSaveRequest } from '@tracon/client';
import type { AgentDescriptor, WorkflowDefinition, WorkflowKind } from '../lib/server-types';

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
  const t = useT();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const editing = name !== undefined;

  const agents = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')) as Promise<AgentDescriptor[]>,
  });

  const existing = useQuery({
    queryKey: ['workflow', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/workflows/{name}', { params: { path: { name: name as string } } }),
      ) as Promise<WorkflowDefinition>,
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

      return unwrap(
        client.PUT('/api/workflows/{name}', {
          params: { path: { name: draft.name.trim() } },
          body,
        }),
      ) as Promise<WorkflowDefinition>;
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['workflows'] });
      await queryClient.invalidateQueries({ queryKey: ['workflow', saved.name] });
      navigate(`workflows/${encodeURIComponent(saved.name)}`);
    },
  });

  const title = editing
    ? t('workflowEditor.editTitle', { name: name ?? '' })
    : t('workflows.new');

  if (editing && existing.isPending) {
    return (
      <>
        <PageHeader title={title} />
        <Panel>
          <Loading rows={8} />
        </Panel>
      </>
    );
  }

  // 🚨 A separate path from the loading one, and it has to render something. On
  // failure `isPending` goes false and, without this, the editor fell through to
  // an empty form that looked like a workflow which had loaded blank — saving it
  // would have replaced the real definition with the default one.
  if (editing && existing.isError) {
    return (
      <>
        <PageHeader title={title} />
        <Panel>
          <div className="p-4">
            <ErrorNote error={existing.error} onRetry={() => void existing.refetch()} />
          </div>
        </Panel>
      </>
    );
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
        title={title}
        description={t('workflowEditor.description')}
        actions={
          <>
            <Button onClick={() => navigate('workflows')}>{t('common.cancel')}</Button>
            <Button
              tone="primary"
              testId="workflow-save"
              busy={save.isPending}
              disabled={blocked}
              onClick={() => save.mutate()}
            >
              {t('common.save')}
            </Button>
          </>
        }
      />

      {save.isError && (
        <div className="mb-3">
          <ErrorNote error={save.error} onRetry={() => save.mutate()} />
        </div>
      )}

      <div className="flex flex-col gap-4">
        <Panel title={t('agentEditor.identity')}>
          <div className="grid gap-4 p-4 sm:grid-cols-2">
            <Field label={t('common.name')} required hint={t('workflowEditor.nameHint')}>
              <TextInput
                value={draft.name}
                data-testid="workflow-name"
                disabled={editing}
                placeholder="review-chain"
                onChange={(event) => setDraft({ ...draft, name: event.target.value })}
              />
            </Field>

            <Field label={t('agentEditor.displayName')}>
              <TextInput
                value={draft.displayName}
                onChange={(event) => setDraft({ ...draft, displayName: event.target.value })}
              />
            </Field>

            <div className="sm:col-span-2">
              <Field label={t('common.description')}>
                <TextInput
                  value={draft.description}
                  onChange={(event) => setDraft({ ...draft, description: event.target.value })}
                />
              </Field>
            </div>
          </div>
        </Panel>

        <Panel title={t('workflows.column.pattern')}>
          <div className="flex flex-col gap-4 p-4">
            <Field label={t('workflowEditor.kind')} required hint={t(KIND_HINT[draft.kind])}>
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

            {/* The render-prop form: "needs two agents" used to be a loose
                paragraph under the picker, which no screen reader ties to the
                control and no keyboard user is told about. */}
            <Field
              label={t('workflowEditor.participants')}
              required
              hint={
                draft.kind === 'Sequential'
                  ? t('workflowEditor.orderMatters')
                  : t('workflowEditor.orderIgnored')
              }
              error={tooFew ? t('workflowEditor.needsTwo', { kind: draft.kind }) : undefined}
            >
              {(ids) => (
                <AgentPicker
                  ids={ids}
                  available={available.map((agent) => agent.name)}
                  selected={draft.agentNames}
                  onChange={(agentNames) => setDraft({ ...draft, agentNames })}
                />
              )}
            </Field>

            {draft.kind === 'Magentic' && (
              <Field
                label={t('workflowEditor.managerAgent')}
                required
                hint={t('workflowEditor.managerHint')}
              >
                <Select
                  value={draft.managerAgentName}
                  onChange={(value) => setDraft({ ...draft, managerAgentName: value })}
                >
                  <option value="">{t('workflowEditor.chooseAgent')}</option>
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
                label={t('workflowEditor.handoffInstructions')}
                hint={t('workflowEditor.handoffHint')}
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
                label={t('workflowEditor.maxIterations')}
                hint={t('workflowEditor.maxIterationsHint')}
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
  const t = useT();

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
          <span className="flex items-center gap-1.5 text-base font-medium">
            {t('workflowEditor.planApproval')}
            <Badge tone="warn">{t('workflowEditor.planApprovalCost')}</Badge>
          </span>
          <span className="mt-0.5 block text-sm text-muted">
            {t('workflowEditor.planApprovalNotice')}
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
  ids,
}: {
  available: readonly string[];
  selected: readonly string[];
  onChange: (names: string[]) => void;
  /**
   * The field bindings, put on the one real control this composite has — the
   * "add an agent" select. A `<div>` cannot carry `aria-invalid`, and the
   * ordered list of chosen agents is output rather than an input.
   */
  ids?: FieldIds;
}): ReactNode {
  const t = useT();
  const unselected = available.filter((agent) => !selected.includes(agent));

  return (
    <div className="flex flex-col gap-2">
      <ol className="flex flex-col gap-1.5" data-testid="workflow-participants">
        {selected.map((agent, index) => (
          <li
            key={agent}
            className="flex items-center gap-2 rounded-md border border-line bg-panel px-2.5 py-1.5 text-base"
          >
            <span className="w-5 text-xs text-subtle">{index + 1}</span>
            <span className="flex-1">{agent}</span>

            <Tooltip text={t('workflowEditor.moveUp')}>
              <button
                type="button"
                aria-label={t('workflowEditor.moveUpAgent', { agent })}
                disabled={index === 0}
                className={cx('px-1 text-muted hover:text-fg', index === 0 && 'opacity-30')}
                onClick={() => onChange(swap([...selected], index, index - 1))}
              >
                ↑
              </button>
            </Tooltip>
            <Tooltip text={t('workflowEditor.moveDown')}>
              <button
                type="button"
                aria-label={t('workflowEditor.moveDownAgent', { agent })}
                disabled={index === selected.length - 1}
                className={cx(
                  'px-1 text-muted hover:text-fg',
                  index === selected.length - 1 && 'opacity-30',
                )}
                onClick={() => onChange(swap([...selected], index, index + 1))}
              >
                ↓
              </button>
            </Tooltip>
            <button
              type="button"
              aria-label={t('workflowEditor.removeAgent', { agent })}
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
          {...(ids ?? {})}
          value=""
          onChange={(value) => {
            if (value.length > 0) {
              onChange([...selected, value]);
            }
          }}
        >
          <option value="">{t('workflowEditor.addAgent')}</option>
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

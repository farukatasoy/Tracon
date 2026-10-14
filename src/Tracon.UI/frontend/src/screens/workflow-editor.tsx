import { useEffect, useMemo, useState, type ReactNode } from 'react';
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
} from '../components/ui';
import { KIND_HINT } from './workflows';
import { StepList } from './workflow-editor/steps';
import {
  canonicalForm,
  chainBody,
  hasFunction,
  hasInvalidSettings,
  hasInvalidTimeouts,
  hasPins,
  hasStepTimeouts,
  hasStrandedTimeouts,
  parseTimeout,
  shapeOf,
  stepsOf,
  storedFormOf,
  timeoutText,
  timeoutValue,
  type Step,
} from '../lib/workflow-steps';
import type { AgentParameter, WorkflowFunctionResponse, WorkflowSaveRequest } from '@tracon/client';
import type { AgentDescriptor, WorkflowDefinition, WorkflowKind, WorkflowResponseMode } from '../lib/server-types';

const KINDS: WorkflowKind[] = ['Sequential', 'Concurrent', 'Handoff', 'GroupChat', 'Magentic'];

/** Patterns that need at least two participants to mean anything. */
const NEEDS_TWO: WorkflowKind[] = ['Concurrent', 'Handoff', 'GroupChat'];

interface Draft {
  name: string;
  displayName: string;
  description: string;
  kind: WorkflowKind;
  /**
   * The chain: agents, and for a `Sequential` workflow function nodes with their settings.
   *
   * 🚨 `PUT /api/workflows/{name}` is a full replace (K-725). The steps are written back in
   * the form the chain needs (`chainBody`) - `nodes` when it holds a function or an agent pinned
   * to a version, `agentNames` otherwise - and the other field is always sent empty. Before phase 195 the editor did not
   * edit `nodes` at all, and adding an agent to a function-node chain replaced the chain with
   * that one agent: the function nodes were gone, with no warning.
   */
  steps: Step[];
  /**
   * The run parameters the workflow declares - carried, never edited here
   * (F-294). The console has no schema editor yet; the agent editor treats its
   * own `parameters` the same way.
   *
   * 🚨 `PUT /api/workflows/{name}` is a full replace (K-725): a save that did
   * not send them back would erase the schema, and the next run would be
   * refused for values the workflow no longer declares.
   */
  parameters: AgentParameter[];
  managerAgentName: string;
  maxIterations: string;
  handoffInstructions: string;
  requirePlanApproval: boolean;
  /**
   * The run timeout this workflow sets for itself, as typed: seconds or `hh:mm:ss`. Empty
   * means the installation's. Sent back on every save - the save is a full replace (K-725).
   */
  runTimeout: string;
  /** The run timeout the server sent, so an untouched field goes back exactly as stored. */
  storedRunTimeout: string | null;
  /**
   * How long a human request of this workflow may wait for an answer (phase 209), as typed:
   * seconds or `d.hh:mm:ss`. Empty means no limit. Sent back on every save - the save is a full
   * replace (K-725), and a dropped value would remove the limit of a workflow whose requests
   * must not wait for ever.
   */
  requestTimeout: string;
  /** The request timeout the server sent, so an untouched field goes back exactly as stored. */
  storedRequestTimeout: string | null;
  /**
   * How a repeated answer to one request is treated (phase 208). Sent back on every save: the
   * save is a full replace (K-725), and a dropped value would turn a one-answer workflow back
   * into Branch.
   */
  responseMode: WorkflowResponseMode;
}

const EMPTY: Draft = {
  name: '',
  displayName: '',
  description: '',
  kind: 'Sequential',
  steps: [],
  parameters: [],
  managerAgentName: '',
  maxIterations: '',
  handoffInstructions: '',
  requirePlanApproval: false,
  runTimeout: '',
  storedRunTimeout: null,
  requestTimeout: '',
  storedRequestTimeout: null,
  responseMode: 'Branch',
};

/**
 * Create or edit a workflow definition.
 *
 * The form only wires together agents that already exist, and the functions the code
 * registered - it never defines behaviour. That is the security boundary from decision K2: a
 * workflow built here arranges the catalogue, it cannot add to it. A function node's settings
 * are values for a shape the function's code declares, not code and not an expression.
 *
 * The chain is one list of steps (`StepList`). A `Sequential` workflow may mix function nodes
 * in, and may pin a stored agent to one of its versions; every other pattern takes agents only.
 * The form writes the canonical shape for what the chain holds - `nodes` with a function or a
 * pin in it, `agentNames` without - and says so before the save when that is a change
 * (`ChainNotices`).
 *
 * Validation is deliberately *not* duplicated from the server. The rules live
 * in `WorkflowDefinitionValidator` and the function registry, and both the save endpoint and
 * the compiler use them; a second copy here would drift. The form only disables the button
 * for the obvious cases (an empty chain, text that is not JSON) and shows the server's own
 * message otherwise.
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

  // The functions a Sequential chain may add. Asked for on every editor, not only when a function is
  // already in the chain: adding the first one is the main thing this list is for.
  const functions = useQuery({
    queryKey: ['workflow-functions'],
    queryFn: () =>
      unwrap(client.GET('/api/workflows/functions')) as Promise<WorkflowFunctionResponse[]>,
  });

  const [draft, setDraft] = useState<Draft>(EMPTY);

  // What the server holds now, so the screen can say when a save changes it.
  const loaded = useMemo(
    () =>
      existing.data === undefined
        ? undefined
        : {
            shape: shapeOf(existing.data.kind, stepsOf(existing.data)),
            form: storedFormOf(existing.data),
          },
    [existing.data],
  );

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
      steps: stepsOf(loaded),
      parameters: loaded.parameters ?? [],
      managerAgentName: loaded.managerAgentName ?? '',
      maxIterations: loaded.maxIterations == null ? '' : String(loaded.maxIterations),
      handoffInstructions: loaded.handoffInstructions ?? '',
      requirePlanApproval: loaded.requirePlanApproval,
      runTimeout: timeoutText(loaded.runTimeout),
      storedRunTimeout: loaded.runTimeout ?? null,
      requestTimeout: timeoutText(loaded.requestTimeout),
      storedRequestTimeout: loaded.requestTimeout ?? null,
      responseMode: loaded.responseMode ?? 'Branch',
    });
  }, [existing.data]);

  const save = useMutation({
    mutationFn: async () => {
      const body: WorkflowSaveRequest = {
        displayName: blank(draft.displayName),
        description: blank(draft.description),
        kind: draft.kind,
        ...chainBody(draft.kind, draft.steps),
        parameters: draft.parameters,

        // Fields that belong to another pattern are sent as null, not as an
        // empty string: the server rejects a value that the chosen pattern does
        // not use (decision K-125), and "" is a value.
        managerAgentName: draft.kind === 'Magentic' ? blank(draft.managerAgentName) : null,
        maxIterations: draft.maxIterations.length === 0 ? null : Number(draft.maxIterations),
        handoffInstructions: draft.kind === 'Handoff' ? blank(draft.handoffInstructions) : null,
        requirePlanApproval: draft.kind === 'Magentic' && draft.requirePlanApproval,
        runTimeout: timeoutValue(draft.runTimeout, draft.storedRunTimeout),
        requestTimeout: timeoutValue(draft.requestTimeout, draft.storedRequestTimeout),
        responseMode: draft.responseMode,
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
      // A save appends a version, and the workflow screen lists them.
      await queryClient.invalidateQueries({ queryKey: ['workflow-versions', saved.name] });
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
  // A function node, a step's own timeout and an agent pinned to a version live on a node, which
  // only a Sequential chain has: the pattern is locked while the chain holds any of them.
  const mixed = hasFunction(draft.steps) || hasStepTimeouts(draft.steps) || hasPins(draft.steps);
  // A step timeout in a chain saved as a plain agent list: held back, never dropped.
  const stranded = hasStrandedTimeouts(draft.kind, draft.steps);
  const tooFew = NEEDS_TWO.includes(draft.kind) && draft.steps.length < 2;
  const sequential = draft.kind === 'Sequential';
  const participantNames = draft.steps.map((step) => step.name);
  // Only a stored agent keeps versions to pin to; the selector is offered in a Sequential chain only.
  const storedAgents = new Set(available.filter((agent) => agent.origin === 'Database').map((agent) => agent.name));

  const blocked =
    draft.name.trim().length === 0 ||
    draft.steps.length === 0 ||
    tooFew ||
    hasInvalidSettings(draft.steps) ||
    hasInvalidTimeouts(draft.steps) ||
    stranded ||
    !parseTimeout(draft.runTimeout).ok ||
    !parseTimeout(draft.requestTimeout).ok ||
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
          {/* Retry is Save again, so it holds back whenever Save does: it used to send a draft
              whose settings text had stopped being JSON (full replace would have cleared them). */}
          <ErrorNote error={save.error} onRetry={blocked ? undefined : () => save.mutate()} />
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
            <Field
              label={t('workflowEditor.kind')}
              required
              hint={mixed ? t('workflowEditor.kindLocked') : t(KIND_HINT[draft.kind])}
            >
              <Select
                value={draft.kind}
                disabled={mixed}
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
                <StepList
                  ids={ids}
                  agents={available.map((agent) => agent.name)}
                  storedAgents={sequential ? storedAgents : undefined}
                  steps={draft.steps}
                  onChange={(steps) => setDraft({ ...draft, steps })}
                  functions={
                    sequential
                      ? {
                          items: functions.data,
                          error: functions.isError ? functions.error : null,
                          retry: () => void functions.refetch(),
                        }
                      : undefined
                  }
                />
              )}
            </Field>

            {loaded !== undefined && (
              <ChainNotices
                kind={draft.kind}
                steps={draft.steps}
                loaded={loaded}
              />
            )}

            {stranded && (
              <p
                className="flex items-start gap-2 rounded-md border border-line bg-raised p-3 text-sm text-muted"
                data-testid="workflow-timeouts-notice"
              >
                <Badge tone="warn">!</Badge>
                <span>{t('workflowEditor.timeoutsNeedFunction')}</span>
              </p>
            )}

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
                    .filter((agent) => !participantNames.includes(agent.name))
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

            <SingleAnswer draft={draft} onChange={setDraft} />

            <Field
              label={t('workflowEditor.runTimeout')}
              hint={t('workflowEditor.runTimeoutHint')}
              error={parseTimeout(draft.runTimeout).ok ? undefined : t('workflowEditor.timeoutInvalid')}
            >
              {(ids) => (
                <TextInput
                  {...ids}
                  className="max-w-xs"
                  data-testid="workflow-run-timeout"
                  inputMode="decimal"
                  placeholder={t('workflowEditor.runTimeoutDefault')}
                  value={draft.runTimeout}
                  onChange={(event) => setDraft({ ...draft, runTimeout: event.target.value })}
                />
              )}
            </Field>

            <Field
              label={t('workflowEditor.requestTimeout')}
              hint={t('workflowEditor.requestTimeoutHint')}
              error={parseTimeout(draft.requestTimeout).ok ? undefined : t('workflowEditor.requestTimeoutInvalid')}
            >
              {(ids) => (
                <TextInput
                  {...ids}
                  className="max-w-xs"
                  data-testid="workflow-request-timeout"
                  inputMode="decimal"
                  placeholder={t('workflowEditor.requestTimeoutDefault')}
                  value={draft.requestTimeout}
                  onChange={(event) => setDraft({ ...draft, requestTimeout: event.target.value })}
                />
              )}
            </Field>
          </div>
        </Panel>
      </div>
    </>
  );
}

/**
 * The one-answer switch (phase 208): with it on, the same answer given twice follows the run the
 * first one started and a different answer is refused. Shown for every kind - the server takes
 * it for every kind - and carried on every save.
 */
function SingleAnswer({
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
          data-testid="workflow-single-answer"
          checked={draft.responseMode === 'SingleAnswer'}
          onChange={(event) =>
            onChange({ ...draft, responseMode: event.target.checked ? 'SingleAnswer' : 'Branch' })
          }
        />
        <span>
          <span className="block text-base font-medium">{t('workflowEditor.singleAnswer')}</span>
          <span className="mt-0.5 block text-sm text-muted">{t('workflowEditor.singleAnswerNotice')}</span>
        </span>
      </label>
    </div>
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

/**
 * What a save is about to do to the stored workflow, said before it happens.
 *
 * Two separate things, both only for a workflow that already exists:
 *
 * - **The stored form changes.** The console writes `nodes` when the chain holds a function or an
 *   agent pinned to a version, and `agentNames` when it does not (one canonical form), whatever
 *   form it loaded. The agent step of a node list runs on the hand-built graph, which does not
 *   stream its tokens live; an agent-only list runs on the built-in sequential pattern. The first
 *   pin on an agent-only chain is such a change (different executors, so a different graph); a
 *   change of the pinned version alone is not.
 * - **The graph changes.** A new pattern, step or order is a different graph. A run that is
 *   waiting keeps resuming the definition version it recorded; resuming it WITH THE NEW version
 *   is refused, because its saved state belongs to the old graph (measured, phase 195; phase 200
 *   for the recorded version). A settings, description or pin change is not such a change.
 */
function ChainNotices({
  kind,
  steps,
  loaded,
}: {
  kind: WorkflowKind;
  steps: readonly Step[];
  loaded: { shape: string; form: 'agentNames' | 'nodes' };
}): ReactNode {
  const t = useT();
  const form = canonicalForm(kind, steps);
  const formChanged = form !== loaded.form;
  const shapeChanged = shapeOf(kind, steps) !== loaded.shape;

  if (!formChanged && !shapeChanged) {
    return null;
  }

  return (
    <div className="flex flex-col gap-2">
      {formChanged && (
        <p
          className="rounded-md border border-line bg-raised p-3 text-sm text-muted"
          data-testid="workflow-form-notice"
        >
          {form === 'nodes' ? t('workflowEditor.formNodes') : t('workflowEditor.formAgents')}
        </p>
      )}
      {(shapeChanged || formChanged) && (
        <p
          className="flex items-start gap-2 rounded-md border border-line bg-raised p-3 text-sm text-muted"
          data-testid="workflow-structure-notice"
        >
          <Badge tone="warn">!</Badge>
          <span>{t('workflowEditor.structureChanged')}</span>
        </p>
      )}
    </div>
  );
}

function blank(value: string): string | null {
  return value.trim().length === 0 ? null : value.trim();
}

import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { useT } from '../lib/i18n';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Field,
  JsonView,
  LinkButton,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  TextArea,
  TextInput,
  Th,
} from '../components/ui';
import { Tooltip } from '../components/tooltip';
import { PassRateBar } from './evals';
import type {
  EvalCaseInput,
  EvalRunStatus,
  TraconMetaResponse as Meta,
} from '@tracon/client';
import type { EvalCase, EvalRun, EvalSuite } from '../lib/server-types';

/**
 * One expected tool call as the operator edits it. `arguments` is the JSON text
 * of an object, kept as text so a half-typed value is not thrown away; empty
 * means "check only that the tool was called".
 */
interface ToolCallDraft {
  name: string;
  arguments: string;
}

/**
 * A case as the screen edits it. It is a case input with the expected tool
 * calls held as text; `toInput` turns it back for the save.
 */
type CaseDraft = Omit<EvalCaseInput, 'expectedToolCalls'> & { toolCalls: ToolCallDraft[] };

function emptyCase(): CaseDraft {
  return { query: '', expectedOutput: null, toolCalls: [], context: null };
}

function argumentsText(value: unknown): string {
  return value === null || value === undefined ? '' : JSON.stringify(value, null, 2);
}

/**
 * What an arguments text stands for. `unsafe` is a JSON object that holds a
 * number JavaScript cannot keep exact: an integer past 2^53, or one too large
 * to be finite. `JSON.parse` rounds it (`1234567890123456789` becomes
 * `1234567890123456800`) and `JSON.stringify` writes the rounded value back, so
 * a save would change what the case expects without a word.
 */
type ParsedArguments =
  | { state: 'empty' }
  | { state: 'object'; value: Record<string, unknown> }
  | { state: 'invalid' }
  | { state: 'unsafe' };

function holdsUnsafeNumber(value: unknown): boolean {
  if (typeof value === 'number') return !Number.isFinite(value) || (Number.isInteger(value) && !Number.isSafeInteger(value));
  if (Array.isArray(value)) return value.some(holdsUnsafeNumber);
  if (typeof value === 'object' && value !== null) return Object.values(value).some(holdsUnsafeNumber);

  return false;
}

function parseArguments(text: string): ParsedArguments {
  if (text.trim().length === 0) return { state: 'empty' };

  try {
    const parsed: unknown = JSON.parse(text);

    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) {
      return { state: 'invalid' };
    }

    return holdsUnsafeNumber(parsed)
      ? { state: 'unsafe' }
      : { state: 'object', value: parsed as Record<string, unknown> };
  } catch {
    return { state: 'invalid' };
  }
}

function isSavable(item: CaseDraft): boolean {
  return (
    item.query.trim().length > 0 &&
    item.toolCalls.every((call) => {
      const { state } = parseArguments(call.arguments);

      return call.name.trim().length > 0 && (state === 'empty' || state === 'object');
    })
  );
}

function toDraft(item: EvalCase): CaseDraft {
  return {
    // The id and the parameter values travel back with every case that is
    // being kept. Dropping them here is not a cosmetic loss: the save
    // would read as "every case removed, every case added" in the
    // run-to-run diff, and a parameterized agent's cases would come back
    // with no values to run against.
    id: item.id,
    query: item.query,
    expectedOutput: item.expectedOutput ?? null,
    toolCalls: (item.expectedToolCalls ?? []).map((call) => ({
      name: call.name,
      arguments: argumentsText(call.arguments),
    })),
    context: item.context ?? null,
    parameters: item.parameters ?? null,
  };
}

function toInput({ toolCalls, ...rest }: CaseDraft): EvalCaseInput {
  return {
    ...rest,
    expectedToolCalls: toolCalls.map((call) => {
      const parsed = parseArguments(call.arguments);

      // The generated type calls the arguments `Record<string, never>`: the
      // schema says "an object" without saying what is in it.
      return {
        name: call.name.trim(),
        ...(parsed.state === 'object' ? { arguments: parsed.value as Record<string, never> } : {}),
      };
    }),
  };
}

function StatusBadge({ status }: { status: EvalRunStatus }): ReactNode {
  const t = useT();

  switch (status) {
    case 'Completed':
      return <Badge tone="success">{t('runs.status.completed')}</Badge>;
    case 'Failed':
      return <Badge tone="danger">{t('runs.status.failed')}</Badge>;
    case 'Cancelled':
      return <Badge tone="warn">{t('runs.status.canceled')}</Badge>;
    case 'Running':
      return <Badge tone="info">{t('runs.status.running')}</Badge>;
    default:
      return <Badge>{t('jobs.status.pending')}</Badge>;
  }
}

function CaseEditor({
  evalCase,
  onChange,
  onRemove,
}: {
  evalCase: CaseDraft;
  onChange: (value: CaseDraft) => void;
  onRemove: () => void;
}): ReactNode {
  const t = useT();

  const argumentsError = (text: string): string | undefined => {
    switch (parseArguments(text).state) {
      case 'invalid':
        return t('evals.toolCall.argumentsInvalid');
      case 'unsafe':
        return t('evals.toolCall.argumentsUnsafe');
      default:
        return undefined;
    }
  };

  const setToolCall = (index: number, value: ToolCallDraft): void =>
    onChange({
      ...evalCase,
      toolCalls: evalCase.toolCalls.map((call, i) => (i === index ? value : call)),
    });

  return (
    <div className="grid gap-3 border-t border-line pt-3 sm:grid-cols-2">
      <div className="sm:col-span-2">
        <Field label={t('evals.query')} required>
          <TextArea
            rows={2}
            value={evalCase.query}
            placeholder={t('evals.queryPlaceholder')}
            onChange={(event) => onChange({ ...evalCase, query: event.target.value })}
          />
        </Field>
      </div>
      <Field label={t('evals.expectedOutput')} hint={t('evals.expectedOutputHint')}>
        <TextInput
          value={evalCase.expectedOutput ?? ''}
          onChange={(event) => onChange({ ...evalCase, expectedOutput: event.target.value || null })}
        />
      </Field>
      <fieldset className="grid gap-2 sm:col-span-2">
        <legend className="text-xs font-medium text-muted">{t('evals.expectedToolCalls')}</legend>
        <p className="text-xs text-subtle">{t('evals.expectedToolCallsHint')}</p>
        {evalCase.toolCalls.map((call, index) => (
          <div key={index} className="grid gap-2 sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)_auto]">
            <Field label={t('evals.toolCall.name')} required>
              <TextInput
                value={call.name}
                onChange={(event) => setToolCall(index, { ...call, name: event.target.value })}
              />
            </Field>
            <Field
              label={t('evals.toolCall.arguments')}
              hint={t('evals.toolCall.argumentsHint')}
              error={argumentsError(call.arguments)}
            >
              <TextArea
                // Tall enough for the pretty-printed object a stored call loads as.
                rows={Math.min(8, Math.max(2, call.arguments.split('\n').length))}
                spellCheck={false}
                value={call.arguments}
                placeholder={'{"orderId": "ORD-1001"}'}
                onChange={(event) => setToolCall(index, { ...call, arguments: event.target.value })}
              />
            </Field>
            <div className="sm:self-end">
              <Button
                tone="danger"
                onClick={() =>
                  onChange({
                    ...evalCase,
                    toolCalls: evalCase.toolCalls.filter((_, i) => i !== index),
                  })
                }
              >
                {t('evals.toolCall.remove')}
              </Button>
            </div>
          </div>
        ))}
        <div>
          <Button
            onClick={() =>
              onChange({ ...evalCase, toolCalls: [...evalCase.toolCalls, { name: '', arguments: '' }] })
            }
          >
            {t('evals.toolCall.add')}
          </Button>
        </div>
      </fieldset>
      <div className="sm:col-span-2">
        <Field label={t('agentEditor.context')} hint={t('evals.contextHint')}>
          {/* Multi-line on purpose: a promoted case keeps the run's documents
              here as line-based blocks, and a single-line input drops the line
              breaks on the first edit (F-343). */}
          <TextArea
            rows={3}
            value={evalCase.context ?? ''}
            onChange={(event) => onChange({ ...evalCase, context: event.target.value || null })}
          />
        </Field>
      </div>
      <div>
        <Button tone="danger" onClick={onRemove}>
          {t('evals.removeCase')}
        </Button>
      </div>
    </div>
  );
}

export function EvalSuiteDetailScreen({ name, meta }: { name: string; meta: Meta }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();
  const [cases, setCases] = useState<CaseDraft[]>([]);

  const suite = useQuery({
    queryKey: ['evalSuite', name],
    queryFn: () =>
      unwrap(client.GET('/api/evals/{name}', { params: { path: { name } } })) as Promise<EvalSuite>,
  });
  const existingCases = useQuery({
    queryKey: ['evalCases', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/evals/{name}/cases', { params: { path: { name } } }),
      ) as Promise<EvalCase[]>,
  });
  const runs = useQuery({
    queryKey: ['evalRuns', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/evals/{name}/runs', { params: { path: { name }, query: { take: 20 } } }),
      ) as Promise<EvalRun[]>,
    refetchInterval: 5_000,
  });

  useEffect(() => {
    if (!existingCases.isSuccess) return;

    setCases(existingCases.data.map(toDraft));
  }, [existingCases.isSuccess, existingCases.data]);

  const saveCases = useMutation({
    mutationFn: () =>
      unwrap(
        client.PUT('/api/evals/{name}/cases', {
          params: { path: { name } },
          body: cases.map(toInput),
        }),
      ),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['evalCases', name] }),
  });

  const trigger = useMutation({
    mutationFn: () =>
      unwrap(client.POST('/api/evals/{name}/run', { params: { path: { name } }, body: {} })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['evalRuns', name] }),
  });

  if (suite.isPending) {
    return (
      <>
        <PageHeader title={name} />
        <Panel>
          <Loading rows={8} />
        </Panel>
      </>
    );
  }

  if (suite.isError) {
    return (
      <>
        <PageHeader title={name} />
        <Panel>
          <div className="p-4">
            <ErrorNote error={suite.error} onRetry={() => void suite.refetch()} />
          </div>
        </Panel>
      </>
    );
  }

  return (
    <>
      <PageHeader
        title={suite.data.name}
        description={suite.data.description ?? t('evals.measures', { agent: suite.data.agentName })}
        actions={
          meta.roles.canOperate && (
            <>
              <LinkButton to="evals" tone="ghost">
                {t('nav.evals')}
              </LinkButton>
              {/* 🚨 A decision surface: pressing this spends model calls. How
                  many, against which agent, is what the tooltip says. */}
              <Tooltip
                text={t('evals.runNowEffect', {
                  count: cases.length,
                  agent: suite.data.agentName,
                })}
              >
                <Button
                  tone="primary"
                  busy={trigger.isPending}
                  disabled={cases.length === 0}
                  onClick={() => trigger.mutate()}
                >
                  {t('evals.runNow')}
                </Button>
              </Tooltip>
            </>
          )
        }
      />

      {trigger.isError && (
        <div className="mb-4">
          <ErrorNote error={trigger.error} onRetry={() => trigger.mutate()} />
        </div>
      )}

      <Panel title={t('evals.checks')} className="mb-4">
        <div className="p-4">
          <JsonView value={suite.data.checks} maxHeight="10rem" />
        </div>
      </Panel>

      <Panel title={t('evals.cases')} className="mb-4">
        <div className="flex flex-col gap-3 p-4">
          {existingCases.isPending && <Loading rows={4} />}
          {/* 🚨 A separate path from the loading one: on failure `isPending`
              goes false and the case list used to render as EMPTY, which reads
              as "this suite has no cases" — and saving from that state would
              have replaced the stored cases with nothing. */}
          {existingCases.isError && (
            <ErrorNote
              error={existingCases.error}
              onRetry={() => void existingCases.refetch()}
            />
          )}
          {cases.length === 0 && !existingCases.isPending && !existingCases.isError && (
            <Empty
              title={t('evals.noCasesYet')}
              action={
                meta.roles.canAdminister ? (
                  <Button
                    tone="primary"
                    onClick={() => setCases((current) => [...current, emptyCase()])}
                  >
                    {t('evals.cases.empty.action')}
                  </Button>
                ) : undefined
              }
            >
              {t('evals.noCasesBody')}
            </Empty>
          )}
          {cases.map((item, index) => (
            <CaseEditor
              key={index}
              evalCase={item}
              onChange={(value) => setCases((current) => current.map((item, i) => (i === index ? value : item)))}
              onRemove={() => setCases((current) => current.filter((_, i) => i !== index))}
            />
          ))}

          {meta.roles.canAdminister && (
            <div className="flex items-center gap-2 border-t border-line pt-3">
              <Button
                disabled={existingCases.isPending}
                onClick={() => setCases((current) => [...current, emptyCase()])}
              >
                {t('evals.addCase')}
              </Button>
              <Button
                tone="primary"
                busy={saveCases.isPending}
                disabled={existingCases.isPending || !cases.every(isSavable)}
                onClick={() => saveCases.mutate()}
              >
                {t('evals.saveCases')}
              </Button>
              {saveCases.isError && (
                <ErrorNote error={saveCases.error} onRetry={() => saveCases.mutate()} />
              )}
            </div>
          )}
        </div>
      </Panel>

      <Panel title={t('nav.runs')}>
        {runs.isPending && <Loading rows={5} />}
        {runs.isError && (
          <div className="p-4">
            <ErrorNote error={runs.error} onRetry={() => void runs.refetch()} />
          </div>
        )}

        {runs.isSuccess &&
          (runs.data.length === 0 ? (
            <Empty
              title={t('evals.noRuns.title')}
              action={
                meta.roles.canOperate && cases.length > 0 ? (
                  <Button tone="primary" busy={trigger.isPending} onClick={() => trigger.mutate()}>
                    {t('evals.noRuns.action')}
                  </Button>
                ) : undefined
              }
            >
              {t('evals.noRuns.body')}
            </Empty>
          ) : (
            <Table label={t('nav.runs')}>
              <thead>
                <tr>
                  <Th>{t('runs.column.run')}</Th>
                  <Th>{t('common.status')}</Th>
                  <Th>{t('evals.passRate')}</Th>
                  <Th className="text-right">{t('agentDetail.version')}</Th>
                  <Th>{t('common.model')}</Th>
                  <Th>{t('common.started')}</Th>
                </tr>
              </thead>
              <tbody>
                {runs.data.map((run) => (
                  <tr key={run.id} className="focus-within:bg-raised hover:bg-raised">
                    <Td>
                      <Link to={`evals/runs/${encodeURIComponent(run.id)}`}>
                        <Mono title={run.id}>{shortId(run.id, 13, 6)}</Mono>
                      </Link>
                    </Td>
                    <Td>
                      <StatusBadge status={run.status} />
                    </Td>
                    <Td>
                      <PassRateBar passed={run.passed} failed={run.failed} total={run.total} />
                    </Td>
                    <Td className="text-right font-mono text-id text-muted">
                      {run.agentVersion ?? '—'}
                    </Td>
                    <Td className="text-muted">{run.modelId ?? '—'}</Td>
                    <Td className="text-muted" title={absoluteTime(run.startedAt)}>
                      {relativeTime(run.startedAt)}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>
          ))}
      </Panel>
    </>
  );
}

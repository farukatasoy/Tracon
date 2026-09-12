import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import type { EvalCaseDiff, EvalCaseDiffKind, EvalRun, EvalRunDetailResponse, EvalRunDiff, EvalSuite } from '../lib/server-types';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { useT, type MessageKey } from '../lib/i18n';
import {
  Badge,
  Empty,
  ErrorNote,
  LinkButton,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Stat,
  Table,
  Td,
  Th,
} from '../components/ui';
import { Select } from '../components/ui';
import { Toolbar, ToolbarField } from '../components/toolbar';
import { PassRateBar } from './evals';

export function EvalRunDetailScreen({ id }: { id: string }): ReactNode {
  const t = useT();
  const detail = useQuery({
    queryKey: ['evalRun', id],
    queryFn: () =>
      unwrap(
        client.GET('/api/evals/runs/{id}', { params: { path: { id } } }),
      ) as Promise<EvalRunDetailResponse>,
    refetchInterval: 5_000,
  });

  const title = t('evals.runTitle', { id: shortId(id, 13, 6) });

  if (detail.isPending) {
    return (
      <>
        <PageHeader title={title} />
        <Panel>
          <Loading rows={8} />
        </Panel>
      </>
    );
  }

  if (detail.isError) {
    return (
      <>
        <PageHeader title={title} />
        <Panel>
          <div className="p-4">
            <ErrorNote error={detail.error} onRetry={() => void detail.refetch()} />
          </div>
        </Panel>
      </>
    );
  }

  const { run, results } = detail.data;

  return (
    <>
      <PageHeader
        title={title}
        description={
          <>
            <Mono copy={run.id}>{run.id}</Mono> —{' '}
            {t('evals.runSubtitle', { status: run.status, when: relativeTime(run.startedAt) })}
          </>
        }
        actions={
          <LinkButton to="evals" tone="ghost">
            {t('nav.evals')}
          </LinkButton>
        }
      />

      <Panel className="mb-4">
        <div className="grid grid-cols-2 gap-4 p-4 sm:grid-cols-4">
          <div>
            <span className="block text-2xs tracking-wider text-subtle uppercase">
              {t('evals.passRate')}
            </span>
            <span className="mt-0.5 block">
              <PassRateBar passed={run.passed} failed={run.failed} total={run.total} />
            </span>
          </div>
          <Stat label={t('evals.agentVersion')} value={run.agentVersion?.toString() ?? '—'} />
          <Stat label={t('common.model')} value={run.modelId ?? '—'} />
          <Stat
            label={t('evals.completed')}
            value={run.completedAt == null ? '—' : relativeTime(run.completedAt)}
            hint={run.completedAt == null ? undefined : absoluteTime(run.completedAt)}
          />
        </div>
      </Panel>

      <Panel title={t('evals.caseResults')}>
        {results.length === 0 ? (
          /* No action: a case result is written by the runner, and the body
             below already distinguishes "still running" from "produced none". */
          <Empty title={t('evals.noResults.title')}>
            {run.status === 'Pending' || run.status === 'Running'
              ? t('evals.noResults.running')
              : t('evals.noResults.none')}
          </Empty>
        ) : (
          <Table label={t('evals.caseResults')}>
            <thead>
              <tr>
                <Th>{t('evals.case')}</Th>
                <Th>{t('common.status')}</Th>
                <Th>{t('evals.output')}</Th>
                <Th>{t('evals.failureReason')}</Th>
                <Th>{t('runs.column.run')}</Th>
              </tr>
            </thead>
            <tbody>
              {results.map((result) => (
                <tr key={result.id} className="focus-within:bg-raised hover:bg-raised">
                  <Td>
                    <Mono title={result.caseId}>{shortId(result.caseId, 8, 4)}</Mono>
                  </Td>
                  <Td>
                    {result.passed ? (
                      <Badge tone="success">{t('evals.passed')}</Badge>
                    ) : (
                      <Badge tone="danger">{t('runs.status.failed')}</Badge>
                    )}
                  </Td>
                  <Td className="max-w-sm truncate" title={result.output ?? undefined}>
                    {result.output ?? ''}
                  </Td>
                  <Td className="max-w-xs truncate text-danger" title={result.failureReason ?? undefined}>
                    {result.failureReason ?? ''}
                  </Td>
                  <Td>
                    {result.runId != null ? (
                      <Link to={`runs/${encodeURIComponent(result.runId)}`}>
                        <Mono>{shortId(result.runId, 13, 6)}</Mono>
                      </Link>
                    ) : (
                      <span className="text-subtle">—</span>
                    )}
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Panel>

      <EvalRunDiffPanel run={run} />
    </>
  );
}

/**
 * The comparison against an earlier run of the same suite.
 *
 * A single pass rate hides which case broke; this aligns two runs case by case
 * so the on-call engineer reads the break itself. `Regressed` comes first and
 * open by default because it is the only group anyone opens the page for.
 */
function EvalRunDiffPanel({ run }: { run: EvalRun }): ReactNode {
  const t = useT();
  const [baselineId, setBaselineId] = useState('');

  // The run-list endpoint is addressed by suite NAME while a run record only
  // carries its suite's id, so the name is resolved from the suite list first.
  const suites = useQuery({
    queryKey: ['evalSuites'],
    queryFn: () => unwrap(client.GET('/api/evals')) as Promise<EvalSuite[]>,
  });

  const suiteName = suites.data?.find((suite) => suite.id === run.suiteId)?.name;

  const runs = useQuery({
    queryKey: ['evalRunsForDiff', suiteName],
    enabled: suiteName != null,
    queryFn: () =>
      unwrap(
        client.GET('/api/evals/{name}/runs', {
          params: { path: { name: suiteName as string }, query: { take: 50 } },
        }),
      ) as Promise<EvalRun[]>,
  });

  const diff = useQuery({
    queryKey: ['evalRunDiff', run.id, baselineId],
    enabled: baselineId !== '',
    retry: false,
    queryFn: () =>
      unwrap(
        client.GET('/api/evals/runs/{id}/diff', {
          // The server caps a page at 500. Asking for the cap keeps the groups
          // below consistent with the counters beside them for every suite that
          // fits; a larger one is reported rather than silently truncated.
          params: { path: { id: run.id }, query: { baseline: baselineId, take: 500 } },
        }),
      ) as Promise<EvalRunDiff>,
  });

  const candidates = (runs.data ?? []).filter(
    (candidate) => candidate.id !== run.id && candidate.status === 'Completed',
  );

  return (
    <Panel className="mt-4" title={t('evals.diff.title')}>
      <div className="border-line border-b p-4">
        <p className="text-subtle mb-2 text-sm">{t('evals.diff.hint')}</p>
        {/* 🚨 A failed suite or run lookup used to be indistinguishable from
            "this suite has only one run": both produced the same sentence. */}
        {suites.isError && (
          <div className="mb-2">
            <ErrorNote error={suites.error} onRetry={() => void suites.refetch()} />
          </div>
        )}
        {runs.isError && (
          <div className="mb-2">
            <ErrorNote error={runs.error} onRetry={() => void runs.refetch()} />
          </div>
        )}
        {candidates.length === 0 ? (
          <p className="text-subtle text-sm">{t('evals.diff.noOtherRuns')}</p>
        ) : (
          <Toolbar>
            <ToolbarField label={t('evals.diff.baseline')}>
              {(id) => (
                <Select
                  id={id}
                  value={baselineId}
                  onChange={setBaselineId}
                  testId="eval-diff-baseline"
                >
                  <option value="">{t('evals.diff.pick')}</option>
                  {candidates.map((candidate) => (
                    <option key={candidate.id} value={candidate.id}>
                      {shortId(candidate.id, 13, 6)} · {candidate.passed}/{candidate.total} ·{' '}
                      {absoluteTime(candidate.startedAt)}
                    </option>
                  ))}
                </Select>
              )}
            </ToolbarField>
          </Toolbar>
        )}
      </div>

      {baselineId === '' ? null : diff.isPending ? (
        <Loading rows={6} />
      ) : diff.isError ? (
        <div className="p-4">
          <ErrorNote error={diff.error} onRetry={() => void diff.refetch()} />
        </div>
      ) : (
        <EvalRunDiffGroups diff={diff.data} />
      )}
    </Panel>
  );
}

const diffGroups: readonly { kind: EvalCaseDiffKind; label: MessageKey; count: (diff: EvalRunDiff) => number; tone: 'danger' | 'success' | 'warn' | 'neutral' }[] = [
  { kind: 'Regressed', label: 'evals.diff.regressed', count: (diff) => diff.regressedCount, tone: 'danger' },
  { kind: 'StillFailing', label: 'evals.diff.stillFailing', count: (diff) => diff.stillFailingCount, tone: 'warn' },
  { kind: 'Added', label: 'evals.diff.added', count: (diff) => diff.addedCount, tone: 'neutral' },
  { kind: 'Fixed', label: 'evals.diff.fixed', count: (diff) => diff.fixedCount, tone: 'success' },
  { kind: 'Removed', label: 'evals.diff.removed', count: (diff) => diff.removedCount, tone: 'neutral' },
  { kind: 'Unchanged', label: 'evals.diff.unchanged', count: (diff) => diff.unchangedCount, tone: 'neutral' },
];

function EvalRunDiffGroups({ diff }: { diff: EvalRunDiff }): ReactNode {
  const t = useT();

  return (
    <div>
      {diffGroups.map((group) => (
        <details
          key={group.kind}
          open={group.kind === 'Regressed'}
          data-testid={`eval-diff-group-${group.kind}`}
          className="border-line border-b last:border-b-0"
        >
          <summary className="hover:bg-raised cursor-pointer p-3 text-base">
            <Badge tone={group.tone}>{t(group.label)}</Badge>
            <span className="text-subtle ml-2">{group.count(diff)}</span>
          </summary>
          <EvalRunDiffTable
            cases={diff.cases.filter((entry) => entry.kind === group.kind)}
            total={group.count(diff)}
          />
        </details>
      ))}
    </div>
  );
}

function EvalRunDiffTable({ cases, total }: { cases: EvalCaseDiff[]; total: number }): ReactNode {
  const t = useT();

  if (cases.length === 0) {
    // 🚨 An empty table under a non-zero count would read as "nothing here"
    // while cases sit beyond the page - the same misreading this whole screen
    // exists to prevent, one level down.
    return (
      <p className="text-subtle p-3 text-sm">
        {total === 0 ? t('evals.diff.empty') : t('evals.diff.beyondPage', { count: total })}
      </p>
    );
  }

  return (
    <>
    {cases.length < total ? (
      <p className="text-subtle px-3 pt-3 text-sm">
        {t('evals.diff.partialPage', { shown: cases.length, count: total })}
      </p>
    ) : null}
    <Table label={t('evals.diff.title')}>
      <thead>
        <tr>
          <Th>{t('evals.case')}</Th>
          <Th>{t('evals.diff.baselineColumn')}</Th>
          <Th>{t('evals.diff.candidateColumn')}</Th>
          <Th>{t('evals.failureReason')}</Th>
        </tr>
      </thead>
      <tbody>
        {cases.map((entry) => (
          <tr key={entry.caseId} className="focus-within:bg-raised hover:bg-raised">
            <Td>
              <Mono title={entry.caseId}>{shortId(entry.caseId, 8, 4)}</Mono>
            </Td>
            <Td>
              <DiffSide passed={entry.baselinePassed} runId={entry.baselineRunId} />
            </Td>
            <Td>
              <DiffSide passed={entry.candidatePassed} runId={entry.candidateRunId} />
            </Td>
            <Td
              className="text-danger max-w-sm truncate"
              title={entry.candidateFailureReason ?? entry.baselineFailureReason ?? undefined}
            >
              {entry.candidateFailureReason ?? entry.baselineFailureReason ?? ''}
            </Td>
          </tr>
        ))}
      </tbody>
    </Table>
    </>
  );
}

/** One side of a compared case: its outcome, linked to the conversation that produced it. */
function DiffSide({ passed, runId }: { passed?: boolean | null; runId?: string | null }): ReactNode {
  const t = useT();

  if (passed == null) {
    return <span className="text-subtle">—</span>;
  }

  const badge = passed ? (
    <Badge tone="success">{t('evals.passed')}</Badge>
  ) : (
    <Badge tone="danger">{t('runs.status.failed')}</Badge>
  );

  return runId == null ? (
    badge
  ) : (
    <Link to={`runs/${encodeURIComponent(runId)}`}>{badge}</Link>
  );
}

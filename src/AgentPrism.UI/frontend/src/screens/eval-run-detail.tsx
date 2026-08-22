import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import type { EvalRunDetailResponse } from '../lib/server-types';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { useT } from '../lib/i18n';
import { Badge, Empty, ErrorNote, Loading, Mono, PageHeader, Panel, Table, Td, Th } from '../components/ui';
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

  if (detail.isPending) {
    return <Loading />;
  }

  if (detail.isError) {
    return <ErrorNote error={detail.error} />;
  }

  const { run, results } = detail.data;

  return (
    <>
      <PageHeader
        title={t('evals.runTitle', { id: shortId(run.id, 13, 6) })}
        description={t('evals.runSubtitle', {
          status: run.status,
          when: relativeTime(run.startedAt),
        })}
      />

      <Panel className="mb-4">
        <div className="grid grid-cols-2 gap-4 p-4 sm:grid-cols-4">
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">{t('evals.passRate')}</span>
            <span className="mt-0.5 block">
              <PassRateBar passed={run.passed} failed={run.failed} total={run.total} />
            </span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">{t('evals.agentVersion')}</span>
            <span className="mt-0.5 block text-[13px]">{run.agentVersion ?? '—'}</span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">{t('common.model')}</span>
            <span className="mt-0.5 block text-[13px]">{run.modelId ?? '—'}</span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">{t('evals.completed')}</span>
            <span className="mt-0.5 block text-[13px]" title={absoluteTime(run.completedAt)}>
              {run.completedAt == null ? '—' : relativeTime(run.completedAt)}
            </span>
          </div>
        </div>
      </Panel>

      <Panel title={t('evals.caseResults')}>
        {results.length === 0 ? (
          <Empty title={t('evals.noResults.title')}>
            {run.status === 'Pending' || run.status === 'Running'
              ? t('evals.noResults.running')
              : t('evals.noResults.none')}
          </Empty>
        ) : (
          <Table>
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
                <tr key={result.id} className="hover:bg-raised">
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
    </>
  );
}

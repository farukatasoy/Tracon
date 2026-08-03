import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { Badge, Empty, ErrorNote, Loading, Mono, PageHeader, Panel, Table, Td, Th } from '../components/ui';
import { PassRateBar } from './evals';

export function EvalRunDetailScreen({ id }: { id: string }): ReactNode {
  const detail = useQuery({
    queryKey: ['evalRun', id],
    queryFn: () => api.evalRun(id),
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
        title={`Eval run ${shortId(run.id, 13, 6)}`}
        description={`${run.status} · started ${relativeTime(run.startedAt)}`}
      />

      <Panel className="mb-4">
        <div className="grid grid-cols-2 gap-4 p-4 sm:grid-cols-4">
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">Pass rate</span>
            <span className="mt-0.5 block">
              <PassRateBar passed={run.passed} failed={run.failed} total={run.total} />
            </span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">Agent version</span>
            <span className="mt-0.5 block text-[13px]">{run.agentVersion ?? '—'}</span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">Model</span>
            <span className="mt-0.5 block text-[13px]">{run.modelId ?? '—'}</span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">Completed</span>
            <span className="mt-0.5 block text-[13px]" title={absoluteTime(run.completedAt)}>
              {run.completedAt == null ? '—' : relativeTime(run.completedAt)}
            </span>
          </div>
        </div>
      </Panel>

      <Panel title="Case results">
        {results.length === 0 ? (
          <Empty title="No results yet">
            {run.status === 'Pending' || run.status === 'Running'
              ? 'The run is still in progress.'
              : 'This run produced no case results.'}
          </Empty>
        ) : (
          <Table>
            <thead>
              <tr>
                <Th>Case</Th>
                <Th>Status</Th>
                <Th>Output</Th>
                <Th>Failure reason</Th>
                <Th>Run</Th>
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
                      <Badge tone="success">passed</Badge>
                    ) : (
                      <Badge tone="danger">failed</Badge>
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

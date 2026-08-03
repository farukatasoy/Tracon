import type { ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { absoluteTime, relativeTime, percent } from '../lib/format';
import { Badge, Button, Empty, ErrorNote, Loading, Mono, PageHeader, Panel, Table, Td, Th } from '../components/ui';
import { StatusBadge } from './experiments';
import type { Meta } from '../lib/types';

export function ExperimentDetailScreen({ name, meta }: { name: string; meta: Meta }): ReactNode {
  const client = useQueryClient();

  const experiment = useQuery({ queryKey: ['experiment', name], queryFn: () => api.experiment(name) });

  const results = useQuery({
    queryKey: ['experimentResults', name],
    queryFn: () => api.experimentResults(name),
    refetchInterval: 5_000,
  });

  const invalidate = async (): Promise<void> => {
    await client.invalidateQueries({ queryKey: ['experiment', name] });
    await client.invalidateQueries({ queryKey: ['experiments'] });
  };

  const start = useMutation({
    mutationFn: () => api.startExperiment(name),
    onSuccess: invalidate,
  });

  const stop = useMutation({
    mutationFn: () => api.stopExperiment(name),
    onSuccess: invalidate,
  });

  if (experiment.isPending) {
    return <Loading />;
  }

  if (experiment.isError) {
    return <ErrorNote error={experiment.error} />;
  }

  const data = experiment.data;

  return (
    <>
      <PageHeader
        title={data.name}
        description={`Splits ${data.agentName}'s traffic between ${data.variants.length} definition versions.`}
        actions={
          meta.roles.canAdminister && (
            <>
              {data.status === 'Draft' && (
                <Button tone="primary" testId="experiment-start" busy={start.isPending} onClick={() => start.mutate()}>
                  Start
                </Button>
              )}
              {data.status === 'Running' && (
                <Button tone="danger" testId="experiment-stop" busy={stop.isPending} onClick={() => stop.mutate()}>
                  Stop
                </Button>
              )}
            </>
          )
        }
      />

      {(start.isError || stop.isError) && (
        <div className="mb-4">
          <ErrorNote error={start.error ?? stop.error} />
        </div>
      )}

      <Panel title="Configuration" className="mb-4">
        <div className="p-4">
          <dl className="mb-4 flex flex-wrap gap-6 text-[13px]">
            <div>
              <dt className="text-[11px] text-subtle uppercase">Status</dt>
              <dd className="mt-0.5"><StatusBadge status={data.status} /></dd>
            </div>
            <div>
              <dt className="text-[11px] text-subtle uppercase">Started</dt>
              <dd className="mt-0.5 text-muted" title={absoluteTime(data.startedAt)}>{relativeTime(data.startedAt)}</dd>
            </div>
            <div>
              <dt className="text-[11px] text-subtle uppercase">Ended</dt>
              <dd className="mt-0.5 text-muted" title={absoluteTime(data.endedAt)}>{relativeTime(data.endedAt)}</dd>
            </div>
          </dl>

          <Table>
            <thead>
              <tr>
                <Th>Variant</Th>
                <Th>Version</Th>
                <Th>Weight</Th>
              </tr>
            </thead>
            <tbody>
              {data.variants.map((variant) => (
                <tr key={variant.name}>
                  <Td><Badge tone="accent">{variant.name}</Badge></Td>
                  <Td><Mono>v{variant.version}</Mono></Td>
                  <Td className="text-muted">{variant.weight}%</Td>
                </tr>
              ))}
            </tbody>
          </Table>
        </div>
      </Panel>

      <Panel
        title="Results"
        actions={
          <span className="text-[11px] text-subtle">
            Raw counts only — no statistical &ldquo;winner&rdquo; is claimed.
          </span>
        }
      >
        {results.isPending && <Loading />}
        {results.isError && (
          <div className="p-4">
            <ErrorNote error={results.error} />
          </div>
        )}

        {results.isSuccess &&
          (results.data.results.length === 0 ? (
            <Empty title="No traffic yet">
              Results appear here once the experiment is running and requests start arriving.
            </Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>Variant</Th>
                  <Th>Version</Th>
                  <Th>Runs</Th>
                  <Th>Completed</Th>
                  <Th>Failed</Th>
                  <Th>Error rate</Th>
                  <Th>Total tokens</Th>
                  <Th>Avg duration</Th>
                </tr>
              </thead>
              <tbody>
                {results.data.results.map((result) => (
                  <tr key={result.variant} className="hover:bg-raised">
                    <Td><Badge tone="accent">{result.variant}</Badge></Td>
                    <Td><Mono>v{result.version}</Mono></Td>
                    <Td className="text-muted">{result.totalRuns}</Td>
                    <Td className="text-muted">{result.completedRuns}</Td>
                    <Td className="text-muted">{result.failedRuns}</Td>
                    <Td className="text-muted">{percent(result.errorRate)}</Td>
                    <Td className="text-muted">{result.totalTokens.toLocaleString()}</Td>
                    <Td className="text-muted">
                      {result.averageDurationMs != null ? `${Math.round(result.averageDurationMs)}ms` : '—'}
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

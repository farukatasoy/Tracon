import type { ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { absoluteTime, count, relativeTime, percent } from '../lib/format';
import { useT } from '../lib/i18n';
import { Badge, Button, Empty, ErrorNote, Loading, Mono, PageHeader, Panel, Table, Td, Th } from '../components/ui';
import { StatusBadge } from './experiments';
import type { Meta } from '../lib/types';

export function ExperimentDetailScreen({ name, meta }: { name: string; meta: Meta }): ReactNode {
  const t = useT();
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
        description={t('experiments.splits', {
          agent: data.agentName,
          count: data.variants.length,
        })}
        actions={
          meta.roles.canAdminister && (
            <>
              {data.status === 'Draft' && (
                <Button tone="primary" testId="experiment-start" busy={start.isPending} onClick={() => start.mutate()}>
                  {t('experiments.start')}
                </Button>
              )}
              {data.status === 'Running' && (
                <Button tone="danger" testId="experiment-stop" busy={stop.isPending} onClick={() => stop.mutate()}>
                  {t('workflowDetail.stop')}
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

      <Panel title={t('experiments.configuration')} className="mb-4">
        <div className="p-4">
          <dl className="mb-4 flex flex-wrap gap-6 text-[13px]">
            <div>
              <dt className="text-[11px] text-subtle uppercase">{t('common.status')}</dt>
              <dd className="mt-0.5"><StatusBadge status={data.status} /></dd>
            </div>
            <div>
              <dt className="text-[11px] text-subtle uppercase">{t('common.started')}</dt>
              <dd className="mt-0.5 text-muted" title={absoluteTime(data.startedAt)}>{relativeTime(data.startedAt)}</dd>
            </div>
            <div>
              <dt className="text-[11px] text-subtle uppercase">{t('experiments.ended')}</dt>
              <dd className="mt-0.5 text-muted" title={absoluteTime(data.endedAt)}>{relativeTime(data.endedAt)}</dd>
            </div>
          </dl>

          <Table>
            <thead>
              <tr>
                <Th>{t('experiments.variant')}</Th>
                <Th>{t('agentDetail.version')}</Th>
                <Th>{t('experiments.weightColumn')}</Th>
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
        title={t('experiments.results')}
        actions={
          <span className="text-[11px] text-subtle">
            {t('experiments.resultsNote')}
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
            <Empty title={t('experiments.noTraffic.title')}>{t('experiments.noTraffic.body')}</Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>{t('experiments.variant')}</Th>
                  <Th>{t('agentDetail.version')}</Th>
                  <Th>{t('nav.runs')}</Th>
                  <Th>{t('runs.filter.completed')}</Th>
                  <Th>{t('runs.stat.failed')}</Th>
                  <Th>{t('runs.stat.errorRate')}</Th>
                  <Th>{t('experiments.totalTokens')}</Th>
                  <Th>{t('experiments.avgDuration')}</Th>
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
                    <Td className="text-muted">{count(result.totalTokens)}</Td>
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

import type { ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { useT } from '../lib/i18n';
import { Badge, Button, Empty, ErrorNote, Loading, Mono, PageHeader, Panel, Table, Td, Th } from '../components/ui';
import { JobProgressBar, JobStatusBadge } from './jobs';
import type { JobItemStatus, AgentPrismMetaResponse as Meta } from '@agentprism/client';
import type { JobDetailResponse } from '../lib/server-types';

function ItemStatusBadge({ status }: { status: JobItemStatus }): ReactNode {
  const t = useT();

  switch (status) {
    case 'Completed':
      return <Badge tone="success">{t('runs.status.completed')}</Badge>;
    case 'Failed':
      return <Badge tone="danger">{t('runs.status.failed')}</Badge>;
    default:
      return <Badge>{t('jobs.status.pending')}</Badge>;
  }
}

export function JobDetailScreen({ id, meta }: { id: string; meta: Meta }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();

  const detail = useQuery({
    queryKey: ['job', id],
    queryFn: () =>
      unwrap(client.GET('/api/jobs/{id}', { params: { path: { id } } })) as Promise<JobDetailResponse>,
    refetchInterval: 5_000,
  });

  const cancel = useMutation({
    mutationFn: () => unwrap(client.POST('/api/jobs/{id}/cancel', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['job', id] }),
  });

  if (detail.isPending) {
    return <Loading />;
  }

  if (detail.isError) {
    return <ErrorNote error={detail.error} />;
  }

  const { job, items } = detail.data;
  const cancellable = job.status === 'Pending' || job.status === 'Leased' || job.status === 'Running';

  return (
    <>
      <PageHeader
        title={t('jobs.jobTitle', { id: shortId(job.id, 13, 6) })}
        description={`${job.kind} · ${job.targetName}`}
        actions={
          meta.roles.canOperate &&
          cancellable && (
            <Button tone="danger" busy={cancel.isPending} onClick={() => cancel.mutate()}>
              {t('common.cancel')}
            </Button>
          )
        }
      />

      <Panel className="mb-4">
        <div className="grid grid-cols-2 gap-4 p-4 sm:grid-cols-4">
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">{t('common.status')}</span>
            <span className="mt-0.5 block"><JobStatusBadge status={job.status} /></span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">{t('jobs.progress')}</span>
            <span className="mt-0.5 block">
              <JobProgressBar done={job.doneItems} failed={job.failedItems} total={job.totalItems} />
            </span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">{t('jobs.attempt')}</span>
            <span className="mt-0.5 block text-[13px]">{job.attempt}</span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">{t('jobs.scheduledFor')}</span>
            <span className="mt-0.5 block text-[13px]" title={absoluteTime(job.scheduledFor)}>
              {relativeTime(job.scheduledFor)}
            </span>
          </div>
        </div>

        {job.errorMessage != null && job.errorMessage.length > 0 && (
          <div className="border-t border-line px-4 py-2.5">
            <ErrorNote error={new Error(job.errorMessage)} />
          </div>
        )}
      </Panel>

      <Panel title={t('jobs.items')}>
        {items.length === 0 ? (
          <Empty title={t('jobs.noItems')}>{t('jobs.noItemsBody')}</Empty>
        ) : (
          <Table>
            <thead>
              <tr>
                <Th>{t('jobs.seq')}</Th>
                <Th>{t('jobs.input')}</Th>
                <Th>{t('common.status')}</Th>
                <Th>{t('runs.column.run')}</Th>
                <Th>{t('common.error')}</Th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id} className="hover:bg-raised">
                  <Td className="text-muted">{item.seq}</Td>
                  <Td className="max-w-sm truncate" title={item.input}>
                    {item.input}
                  </Td>
                  <Td><ItemStatusBadge status={item.status} /></Td>
                  <Td>
                    {item.runId != null ? (
                      <Link to={`runs/${encodeURIComponent(item.runId)}`}>
                        <Mono>{shortId(item.runId, 13, 6)}</Mono>
                      </Link>
                    ) : (
                      <span className="text-subtle">—</span>
                    )}
                  </Td>
                  <Td className="max-w-xs truncate text-danger" title={item.error ?? undefined}>
                    {item.error ?? ''}
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

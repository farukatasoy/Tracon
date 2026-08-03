import type { ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { Badge, Button, Empty, ErrorNote, Loading, Mono, PageHeader, Panel, Table, Td, Th } from '../components/ui';
import { JobProgressBar, JobStatusBadge } from './jobs';
import type { JobItemStatus, Meta } from '../lib/types';

function ItemStatusBadge({ status }: { status: JobItemStatus }): ReactNode {
  switch (status) {
    case 'Completed':
      return <Badge tone="success">completed</Badge>;
    case 'Failed':
      return <Badge tone="danger">failed</Badge>;
    default:
      return <Badge>pending</Badge>;
  }
}

export function JobDetailScreen({ id, meta }: { id: string; meta: Meta }): ReactNode {
  const client = useQueryClient();

  const detail = useQuery({
    queryKey: ['job', id],
    queryFn: () => api.job(id),
    refetchInterval: 5_000,
  });

  const cancel = useMutation({
    mutationFn: () => api.cancelJob(id),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['job', id] }),
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
        title={`Job ${shortId(job.id, 13, 6)}`}
        description={`${job.kind} · ${job.targetName}`}
        actions={
          meta.roles.canOperate &&
          cancellable && (
            <Button tone="danger" busy={cancel.isPending} onClick={() => cancel.mutate()}>
              Cancel
            </Button>
          )
        }
      />

      <Panel className="mb-4">
        <div className="grid grid-cols-2 gap-4 p-4 sm:grid-cols-4">
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">Status</span>
            <span className="mt-0.5 block"><JobStatusBadge status={job.status} /></span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">Progress</span>
            <span className="mt-0.5 block">
              <JobProgressBar done={job.doneItems} failed={job.failedItems} total={job.totalItems} />
            </span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">Attempt</span>
            <span className="mt-0.5 block text-[13px]">{job.attempt}</span>
          </div>
          <div>
            <span className="block text-[11px] tracking-wide text-subtle uppercase">Scheduled for</span>
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

      <Panel title="Items">
        {items.length === 0 ? (
          <Empty title="No items">This job has no input items.</Empty>
        ) : (
          <Table>
            <thead>
              <tr>
                <Th>Seq</Th>
                <Th>Input</Th>
                <Th>Status</Th>
                <Th>Run</Th>
                <Th>Error</Th>
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

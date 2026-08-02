import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, count, duration, percent, relativeTime, shortId } from '../lib/format';
import {
  Badge,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Select,
  Table,
  Td,
  Th,
} from '../components/ui';
import { Pager } from './sessions';
import type { RunStatus } from '../lib/types';

const PAGE_SIZE = 50;

export function StatusBadge({ status }: { status: RunStatus }): ReactNode {
  switch (status) {
    case 'Completed':
      return <Badge tone="success">completed</Badge>;
    case 'Failed':
      return <Badge tone="danger">failed</Badge>;
    case 'Canceled':
      return <Badge tone="warn">canceled</Badge>;
    default:
      return <Badge tone="info">running</Badge>;
  }
}

export function RunsScreen(): ReactNode {
  const [agentName, setAgentName] = useState('');
  const [status, setStatus] = useState('');
  const [page, setPage] = useState(0);

  const agents = useQuery({ queryKey: ['agents'], queryFn: api.agents });
  const stats = useQuery({
    queryKey: ['stats', agentName],
    queryFn: () => api.stats({ agentName: agentName.length > 0 ? agentName : undefined }),
  });

  const runs = useQuery({
    queryKey: ['runs', agentName, status, page],
    queryFn: () =>
      api.runs({
        agentName: agentName.length > 0 ? agentName : undefined,
        status: status.length > 0 ? (status as RunStatus) : undefined,
        skip: page * PAGE_SIZE,
        take: PAGE_SIZE,
      }),
    // A run in flight changes on its own; the list should follow without a reload.
    refetchInterval: 5_000,
  });

  return (
    <>
      <PageHeader
        title="Runs"
        description="Every agent execution, with its event stream. Events are append-only, so a finished run replays exactly as it happened."
        actions={
          <>
            <Select
              value={agentName}
              onChange={(value) => {
                setAgentName(value);
                setPage(0);
              }}
            >
              <option value="">All agents</option>
              {(agents.data ?? []).map((agent) => (
                <option key={agent.name} value={agent.name}>
                  {agent.displayName ?? agent.name}
                </option>
              ))}
            </Select>
            <Select
              value={status}
              onChange={(value) => {
                setStatus(value);
                setPage(0);
              }}
            >
              <option value="">Any status</option>
              <option value="Running">Running</option>
              <option value="Completed">Completed</option>
              <option value="Failed">Failed</option>
              <option value="Canceled">Canceled</option>
            </Select>
          </>
        }
      />

      {stats.isSuccess && (
        <div className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Stat label="Runs" value={count(stats.data.totalRuns)} />
          <Stat label="Failed" value={count(stats.data.failedRuns)} tone={stats.data.failedRuns > 0 ? 'danger' : undefined} />
          <Stat label="Error rate" value={percent(stats.data.errorRate)} hint="Of finished runs only." />
          <Stat label="Tokens" value={count(stats.data.totalTokens)} />
        </div>
      )}

      <Panel>
        {runs.isPending && <Loading />}
        {runs.isError && <div className="p-4"><ErrorNote error={runs.error} /></div>}

        {runs.isSuccess && runs.data.length === 0 && (
          <Empty title="No runs recorded">
            Send a message in the{' '}
            <Link to="playground" className="text-accent underline">Playground</Link> or call the
            agent through the API.
          </Empty>
        )}

        {runs.isSuccess && runs.data.length > 0 && (
          <>
            <Table>
              <thead>
                <tr>
                  <Th>Run</Th>
                  <Th>Agent</Th>
                  <Th>Status</Th>
                  <Th>Duration</Th>
                  <Th>Tokens</Th>
                  <Th>Events</Th>
                  <Th>Started</Th>
                </tr>
              </thead>
              <tbody>
                {runs.data.map((run) => (
                  <tr key={run.id} className="hover:bg-raised">
                    <Td>
                      <Link to={`runs/${encodeURIComponent(run.id)}`}>
                        <Mono title={run.id}>{shortId(run.id, 13, 6)}</Mono>
                      </Link>
                    </Td>
                    <Td>
                      <Link
                        to={`agents/${encodeURIComponent(run.agentName)}`}
                        className="text-muted hover:text-fg"
                      >
                        {run.agentName}
                      </Link>
                    </Td>
                    <Td><StatusBadge status={run.status} /></Td>
                    <Td className="text-muted">{duration(run.startedAt, run.completedAt)}</Td>
                    <Td className="text-muted">{count(run.usage?.totalTokens)}</Td>
                    <Td className="text-muted">{count(run.eventCount)}</Td>
                    <Td className="text-muted" title={absoluteTime(run.startedAt)}>
                      {relativeTime(run.startedAt)}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>

            <Pager page={page} size={runs.data.length} pageSize={PAGE_SIZE} onChange={setPage} />
          </>
        )}
      </Panel>
    </>
  );
}

export function Stat({
  label,
  value,
  hint,
  tone,
}: {
  label: string;
  value: string;
  hint?: string;
  tone?: 'danger';
}): ReactNode {
  return (
    <div className="rounded-lg border border-line bg-panel px-3 py-2.5" title={hint}>
      <span className="block text-[11px] tracking-wide text-subtle uppercase">{label}</span>
      <span className={`mt-0.5 block text-lg font-semibold ${tone === 'danger' ? 'text-danger' : ''}`}>
        {value}
      </span>
    </div>
  );
}

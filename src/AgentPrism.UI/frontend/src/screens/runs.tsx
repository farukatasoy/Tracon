import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, count, duration, percent, relativeTime, shortId } from '../lib/format';
import { usePlural, useT } from '../lib/i18n';
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
  const t = useT();

  switch (status) {
    case 'Completed':
      return <Badge tone="success">{t('runs.status.completed')}</Badge>;
    case 'Failed':
      return <Badge tone="danger">{t('runs.status.failed')}</Badge>;
    case 'Canceled':
      return <Badge tone="warn">{t('runs.status.canceled')}</Badge>;
    case 'AwaitingInput':
      // Neither finished nor running: a workflow stopped on a human decision.
      return <Badge tone="warn">{t('runs.status.awaitingInput')}</Badge>;
    default:
      return <Badge tone="info">{t('runs.status.running')}</Badge>;
  }
}

export function RunsScreen(): ReactNode {
  const t = useT();
  const plural = usePlural();
  const [agentName, setAgentName] = useState('');
  const [status, setStatus] = useState('');
  const [includeChildren, setIncludeChildren] = useState(false);
  const [page, setPage] = useState(0);

  const agents = useQuery({ queryKey: ['agents'], queryFn: api.agents });
  const stats = useQuery({
    queryKey: ['stats', agentName],
    queryFn: () => api.stats({ agentName: agentName.length > 0 ? agentName : undefined }),
  });

  const runs = useQuery({
    queryKey: ['runs', agentName, status, includeChildren, page],
    queryFn: () =>
      api.runs({
        agentName: agentName.length > 0 ? agentName : undefined,
        status: status.length > 0 ? (status as RunStatus) : undefined,
        includeChildren: includeChildren ? true : undefined,
        skip: page * PAGE_SIZE,
        take: PAGE_SIZE,
      }),
    // A run in flight changes on its own; the list should follow without a reload.
    refetchInterval: 5_000,
  });

  return (
    <>
      <PageHeader
        title={t('nav.runs')}
        description={t('runs.description')}
        actions={
          <>
            <Select
              value={agentName}
              onChange={(value) => {
                setAgentName(value);
                setPage(0);
              }}
            >
              <option value="">{t('runs.allAgents')}</option>
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
              <option value="">{t('runs.anyStatus')}</option>
              <option value="Running">{t('runs.filter.running')}</option>
              <option value="Completed">{t('runs.filter.completed')}</option>
              <option value="Failed">{t('runs.filter.failed')}</option>
              <option value="Canceled">{t('runs.filter.canceled')}</option>
              <option value="AwaitingInput">{t('runs.filter.awaitingInput')}</option>
            </Select>
            <Select
              value={includeChildren ? 'all' : 'roots'}
              onChange={(value) => {
                setIncludeChildren(value === 'all');
                setPage(0);
              }}
            >
              <option value="roots">{t('runs.rootOnly')}</option>
              <option value="all">{t('runs.includeChildren')}</option>
            </Select>
          </>
        }
      />

      {stats.isSuccess && (
        <div className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Stat label={t('nav.runs')} value={count(stats.data.totalRuns)} />
          <Stat label={t('runs.stat.failed')} value={count(stats.data.failedRuns)} tone={stats.data.failedRuns > 0 ? 'danger' : undefined} />
          {stats.data.awaitingInputRuns > 0 ? (
            <Stat
              label={t('runs.filter.awaitingInput')}
              value={count(stats.data.awaitingInputRuns)}
              hint={t('runs.stat.awaitingHint')}
            />
          ) : (
            <Stat
              label={t('runs.stat.errorRate')}
              value={percent(stats.data.errorRate)}
              hint={t('runs.stat.errorRateHint')}
            />
          )}
          <Stat label={t('common.tokens')} value={count(stats.data.totalTokens)} />
        </div>
      )}

      <Panel>
        {runs.isPending && <Loading />}
        {runs.isError && <div className="p-4"><ErrorNote error={runs.error} /></div>}

        {runs.isSuccess && runs.data.length === 0 && (
          <Empty title={t('runs.empty.title')}>
            {t('runs.empty.before')}{' '}
            <Link to="playground" className="text-accent underline">
              {t('nav.playground')}
            </Link>{' '}
            {t('runs.empty.after')}
          </Empty>
        )}

        {runs.isSuccess && runs.data.length > 0 && (
          <>
            <Table>
              <thead>
                <tr>
                  <Th>{t('runs.column.run')}</Th>
                  <Th>{t('common.agent')}</Th>
                  <Th>{t('common.status')}</Th>
                  <Th>{t('common.duration')}</Th>
                  <Th>{t('common.tokens')}</Th>
                  <Th>{t('runs.column.treeTokens')}</Th>
                  <Th>{t('runs.column.events')}</Th>
                  <Th>{t('common.started')}</Th>
                </tr>
              </thead>
              <tbody>
                {runs.data.map((run) => (
                  <tr key={run.id} className="hover:bg-raised">
                    <Td>
                      <span className="flex items-center gap-2">
                        <Link to={`runs/${encodeURIComponent(run.id)}`}>
                          <Mono title={run.id}>{shortId(run.id, 13, 6)}</Mono>
                        </Link>
                        {run.childRunCount > 0 && (
                          <Badge tone="info">
                            {plural('runs.childRuns', run.childRunCount)}
                          </Badge>
                        )}
                        {run.depth > 0 && <Badge tone="warn">{t('runs.depth', { depth: run.depth })}</Badge>}
                      </span>
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
                    <Td
                      className="text-muted"
                      title={t('runs.treeTokensTitle')}
                    >
                      {count(run.treeUsage?.totalTokens)}
                    </Td>
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

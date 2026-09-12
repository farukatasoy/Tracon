import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link, useSearchParams } from '../lib/router';
import { absoluteTime, count, duration, percent, relativeTime, shortId } from '../lib/format';
import { usePlural, useT } from '../lib/i18n';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  LinkButton,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Pager,
  Select,
  Stat,
  Table,
  Td,
  TextInput,
  Th,
} from '../components/ui';
import { Toolbar, ToolbarField } from '../components/toolbar';
import { StatusDot, runStatusTone } from '../components/status-dot';
import type { RunStatus } from '@tracon/client';
import type { AgentDescriptor, RunRecord, RunStatistics } from '../lib/server-types';

const PAGE_SIZE = 50;

/**
 * A run's status, everywhere it appears.
 *
 * 🚨 The colour comes from `runStatusTone`, not from a choice made here. Before
 * that mapping existed, `Queued` was info on one screen and neutral on another.
 */
export function StatusBadge({ status }: { status: RunStatus }): ReactNode {
  const t = useT();
  const tone = runStatusTone(status);

  const label: Record<RunStatus, string> = {
    Completed: t('runs.status.completed'),
    Failed: t('runs.status.failed'),
    Canceled: t('runs.status.canceled'),
    // Neither finished nor running: a workflow stopped on a human decision.
    AwaitingInput: t('runs.status.awaitingInput'),
    // Started with 'Prefer: respond-async'; the worker has not picked it up yet.
    Queued: t('runs.status.queued'),
    // A queued run hit a tool call needing approval; see /approvals.
    AwaitingApproval: t('runs.status.awaitingApproval'),
    Running: t('runs.status.running'),
  };

  return (
    <Badge tone={tone}>
      <StatusDot tone={tone} live={status === 'Running'} />
      {label[status] ?? status}
    </Badge>
  );
}

export function RunsScreen(): ReactNode {
  const t = useT();
  const plural = usePlural();
  const [agentName, setAgentName] = useState('');
  const [status, setStatus] = useState('');
  const [includeChildren, setIncludeChildren] = useState(false);
  const [userId, setUserId] = useState('');
  const [label, setLabel] = useState('');
  const [page, setPage] = useState(0);
  // `?sessionId=...` (session-detail.tsx's "N runs" button) — a link-driven
  // filter, not a control on this screen, so it has no `<Select>` of its own.
  const sessionId = useSearchParams().get('sessionId');

  // Two different questions, and they have different answers when the list was
  // reached from a session link. `resettable` is what THIS screen's controls
  // can undo; `sessionId` is in the URL and the reset button cannot clear it.
  const resettable =
    agentName.length > 0 || status.length > 0 || includeChildren || userId.length > 0 || label.length > 0;
  // `narrowed` is what the empty state should say: arriving from a session with
  // no runs is a narrowed list, not an empty console.
  const narrowed = resettable || sessionId !== null;

  const reset = (): void => {
    setAgentName('');
    setStatus('');
    setIncludeChildren(false);
    setUserId('');
    setLabel('');
    setPage(0);
  };

  const agents = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')) as Promise<AgentDescriptor[]>,
  });
  const stats = useQuery({
    queryKey: ['stats', agentName, userId, label],
    queryFn: () =>
      unwrap(
        client.GET('/api/stats', {
          params: {
            query: {
              agentName: agentName.length > 0 ? agentName : undefined,
              userId: userId.length > 0 ? userId : undefined,
              label: label.length > 0 ? label : undefined,
            },
          },
        }),
      ) as Promise<RunStatistics>,
  });

  const runs = useQuery({
    queryKey: ['runs', agentName, status, includeChildren, page, sessionId, userId, label],
    queryFn: () =>
      unwrap(
        client.GET('/api/runs', {
          params: {
            query: {
              agentName: agentName.length > 0 ? agentName : undefined,
              status: status.length > 0 ? (status as RunStatus) : undefined,
              includeChildren: includeChildren ? true : undefined,
              sessionId: sessionId ?? undefined,
              userId: userId.length > 0 ? userId : undefined,
              label: label.length > 0 ? label : undefined,
              skip: page * PAGE_SIZE,
              take: PAGE_SIZE,
            },
          },
        }),
      ) as Promise<RunRecord[]>,
    // A run in flight changes on its own; the list should follow without a reload.
    refetchInterval: 5_000,
  });

  return (
    <>
      <PageHeader title={t('nav.runs')} description={t('runs.description')} />

      <Toolbar onReset={resettable ? reset : undefined}>
        <ToolbarField label={t('common.agent')}>
          {(id) => (
            <Select
              id={id}
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
          )}
        </ToolbarField>

        <ToolbarField label={t('common.status')}>
          {(id) => (
            <Select
              id={id}
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
              <option value="Queued">{t('runs.filter.queued')}</option>
              <option value="AwaitingApproval">{t('runs.filter.awaitingApproval')}</option>
            </Select>
          )}
        </ToolbarField>

        <ToolbarField label={t('runs.filter.scope')}>
          {(id) => (
            <Select
              id={id}
              value={includeChildren ? 'all' : 'roots'}
              onChange={(value) => {
                setIncludeChildren(value === 'all');
                setPage(0);
              }}
            >
              <option value="roots">{t('runs.rootOnly')}</option>
              <option value="all">{t('runs.includeChildren')}</option>
            </Select>
          )}
        </ToolbarField>

        {/*
          Free text rather than a <Select>: the server does not expose a user
          list, and building one from the visible page would silently offer
          only the users on THIS page.
        */}
        <ToolbarField label={t('runs.filter.user')}>
          {(id) => (
            <TextInput
              id={id}
              value={userId}
              placeholder={t('runs.filter.userPlaceholder')}
              className="h-8 w-36 py-0"
              onChange={(event) => {
                setUserId(event.target.value);
                setPage(0);
              }}
            />
          )}
        </ToolbarField>

        <ToolbarField label={t('runs.filter.label')}>
          {(id) => (
            <TextInput
              id={id}
              value={label}
              placeholder={t('runs.filter.labelPlaceholder')}
              className="h-8 w-36 py-0"
              onChange={(event) => {
                setLabel(event.target.value);
                setPage(0);
              }}
            />
          )}
        </ToolbarField>
      </Toolbar>

      {stats.isSuccess && (
        <div className="mb-3 grid grid-cols-2 gap-2 sm:grid-cols-4">
          <Stat label={t('nav.runs')} value={count(stats.data.totalRuns)} />
          <Stat
            label={t('runs.stat.failed')}
            value={count(stats.data.failedRuns)}
            tone={stats.data.failedRuns > 0 ? 'danger' : undefined}
          />
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
        {runs.isPending && <Loading rows={8} />}
        {runs.isError && (
          <div className="p-3">
            <ErrorNote error={runs.error} onRetry={() => void runs.refetch()} />
          </div>
        )}

        {runs.isSuccess && runs.data.length === 0 && (
          <Empty
            title={t('runs.empty.title')}
            action={
              resettable ? (
                <Button onClick={reset}>{t('toolbar.reset')}</Button>
              ) : (
                <LinkButton to="playground" tone="primary">
                  {t('runs.empty.action')}
                </LinkButton>
              )
            }
          >
            {narrowed ? t('runs.empty.filtered') : t('runs.empty.body')}
          </Empty>
        )}

        {runs.isSuccess && runs.data.length > 0 && (
          <>
            <Table label={t('nav.runs')}>
              <thead>
                <tr>
                  <Th>{t('runs.column.run')}</Th>
                  <Th>{t('common.agent')}</Th>
                  <Th>{t('common.status')}</Th>
                  <Th className="text-right">{t('common.duration')}</Th>
                  <Th className="text-right">{t('common.tokens')}</Th>
                  <Th className="text-right" description={t('runs.treeTokensTitle')}>
                    {t('runs.column.treeTokens')}
                  </Th>
                  <Th className="text-right">{t('runs.column.events')}</Th>
                  <Th>{t('common.started')}</Th>
                </tr>
              </thead>
              <tbody>
                {runs.data.map((run) => (
                  // `focus-within` is the keyboard half of the hover highlight:
                  // tabbing to the run link marks the whole row, and Enter on
                  // that link opens it — which is what "the row opens" means
                  // without inventing a second, fake control on the <tr>.
                  <tr key={run.id} className="focus-within:bg-raised hover:bg-raised">
                    <Td>
                      <span className="flex items-center gap-1.5">
                        <Link to={`runs/${encodeURIComponent(run.id)}`}>
                          <Mono title={run.id}>{shortId(run.id, 13, 6)}</Mono>
                        </Link>
                        {run.childRunCount > 0 && (
                          <Badge tone="info">{plural('runs.childRuns', run.childRunCount)}</Badge>
                        )}
                        {run.depth > 0 && <Badge tone="neutral">{t('runs.depth', { depth: run.depth })}</Badge>}
                      </span>
                    </Td>
                    <Td>
                      <Link to={`agents/${encodeURIComponent(run.agentName)}`} className="text-muted hover:text-fg">
                        {run.agentName}
                      </Link>
                    </Td>
                    <Td>
                      <StatusBadge status={run.status} />
                    </Td>
                    <Td className="text-right font-mono text-id text-muted">
                      {duration(run.startedAt, run.completedAt)}
                    </Td>
                    <Td className="text-right font-mono text-id text-muted">{count(run.usage?.totalTokens)}</Td>
                    <Td className="text-right font-mono text-id text-muted">
                      {count(run.treeUsage?.totalTokens)}
                    </Td>
                    <Td className="text-right font-mono text-id text-muted">{count(run.eventCount)}</Td>
                    <Td className="text-sm text-muted" title={absoluteTime(run.startedAt)}>
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

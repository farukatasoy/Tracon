import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { useT } from '../lib/i18n';
import {
  Button,
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
import { TrashIcon } from '../components/icons';
import type { TraconMetaResponse as Meta, SessionRecord } from '@tracon/client';
import type { AgentDescriptor } from '../lib/server-types';

const PAGE_SIZE = 50;

export function SessionsScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();
  const [agentName, setAgentName] = useState('');
  const [page, setPage] = useState(0);

  const agents = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')) as Promise<AgentDescriptor[]>,
  });

  const sessions = useQuery({
    queryKey: ['sessions', agentName, page],
    queryFn: () =>
      unwrap(
        client.GET('/api/sessions', {
          params: {
            query: {
              agentName: agentName.length > 0 ? agentName : undefined,
              skip: page * PAGE_SIZE,
              take: PAGE_SIZE,
            },
          },
        }),
      ) as Promise<SessionRecord[]>,
  });

  const remove = useMutation({
    mutationFn: (id: string) =>
      unwrap(client.DELETE('/api/sessions/{sessionId}', { params: { path: { sessionId: id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sessions'] }),
  });

  return (
    <>
      <PageHeader
        title={t('nav.sessions')}
        description={t('sessions.description')}
        actions={
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
        }
      />

      {remove.isError && <div className="mb-4"><ErrorNote error={remove.error} /></div>}

      <Panel>
        {sessions.isPending && <Loading />}
        {sessions.isError && <div className="p-4"><ErrorNote error={sessions.error} /></div>}

        {sessions.isSuccess && sessions.data.length === 0 && (
          <Empty title={t('sessions.empty.title')}>
            {t('sessions.empty.body')}{' '}
            <Link to="playground" className="text-accent underline">
              {t('nav.playground')}
            </Link>
            .
          </Empty>
        )}

        {sessions.isSuccess && sessions.data.length > 0 && (
          <>
            <Table>
              <thead>
                <tr>
                  <Th>{t('common.session')}</Th>
                  <Th>{t('common.agent')}</Th>
                  <Th>{t('common.created')}</Th>
                  <Th>{t('common.updated')}</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {sessions.data.map((session) => (
                  <tr key={session.id} className="hover:bg-raised">
                    <Td>
                      <Link to={`sessions/${encodeURIComponent(session.id)}`}>
                        <Mono title={session.id}>{shortId(session.id, 18, 6)}</Mono>
                      </Link>
                    </Td>
                    <Td>
                      <Link
                        to={`agents/${encodeURIComponent(session.agentName)}`}
                        className="text-muted hover:text-fg"
                      >
                        {session.agentName}
                      </Link>
                    </Td>
                    <Td className="text-muted" title={absoluteTime(session.createdAt)}>
                      {relativeTime(session.createdAt)}
                    </Td>
                    <Td className="text-muted" title={absoluteTime(session.updatedAt)}>
                      {relativeTime(session.updatedAt)}
                    </Td>
                    <Td className="text-right">
                      {meta.roles.canOperate && (
                        <Button
                          tone="ghost"
                          title={t('sessions.delete')}
                          busy={remove.isPending && remove.variables === session.id}
                          onClick={() => {
                            if (window.confirm(t('sessions.confirmDelete'))) {
                              remove.mutate(session.id);
                            }
                          }}
                        >
                          <TrashIcon className="size-3.5" />
                        </Button>
                      )}
                    </Td>
                  </tr>
                ))}
              </tbody>
            </Table>

            <Pager
              page={page}
              size={sessions.data.length}
              pageSize={PAGE_SIZE}
              onChange={setPage}
            />
          </>
        )}
      </Panel>
    </>
  );
}

export function Pager({
  page,
  size,
  pageSize,
  onChange,
}: {
  page: number;
  size: number;
  pageSize: number;
  onChange: (page: number) => void;
}): ReactNode {
  const t = useT();

  if (page === 0 && size < pageSize) {
    return null;
  }

  return (
    <div className="flex items-center justify-between border-t border-line px-4 py-2 text-sm text-muted">
      <span>{t('common.page', { page: page + 1 })}</span>
      <div className="flex gap-2">
        <Button tone="ghost" disabled={page === 0} onClick={() => onChange(page - 1)}>
          {t('common.previous')}
        </Button>
        <Button tone="ghost" disabled={size < pageSize} onClick={() => onChange(page + 1)}>
          {t('common.next')}
        </Button>
      </div>
    </div>
  );
}

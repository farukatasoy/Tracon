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
  LinkButton,
  Loading,
  Mono,
  PageHeader,
  Pager,
  Panel,
  Select,
  Table,
  Td,
  Th,
} from '../components/ui';
import { Toolbar, ToolbarField } from '../components/toolbar';
import { Tooltip } from '../components/tooltip';
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
      <PageHeader title={t('nav.sessions')} description={t('sessions.description')} />

      <Toolbar
        onReset={
          agentName.length > 0
            ? () => {
                setAgentName('');
                setPage(0);
              }
            : undefined
        }
      >
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
      </Toolbar>

      {/* A delete that failed has to be retryable where it was attempted; the
          row is gone from the mutation's point of view but not from the list. */}
      {remove.isError && (
        <div className="mb-4">
          <ErrorNote
            error={remove.error}
            onRetry={remove.variables === undefined ? undefined : () => remove.mutate(remove.variables)}
          />
        </div>
      )}

      <Panel>
        {sessions.isPending && <Loading rows={8} />}
        {sessions.isError && (
          <div className="p-4">
            <ErrorNote error={sessions.error} onRetry={() => void sessions.refetch()} />
          </div>
        )}

        {sessions.isSuccess && sessions.data.length === 0 && (
          <Empty
            title={agentName.length > 0 ? t('common.noResults') : t('sessions.empty.title')}
            action={
              agentName.length > 0 ? (
                <Button
                  onClick={() => {
                    setAgentName('');
                    setPage(0);
                  }}
                >
                  {t('toolbar.reset')}
                </Button>
              ) : (
                <LinkButton to="playground" tone="primary">
                  {t('sessions.empty.action')}
                </LinkButton>
              )
            }
          >
            {agentName.length > 0 ? t('sessions.empty.filtered') : t('sessions.empty.body')}
          </Empty>
        )}

        {sessions.isSuccess && sessions.data.length > 0 && (
          <>
            <Table label={t('nav.sessions')}>
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
                  <tr key={session.id} className="focus-within:bg-raised hover:bg-raised">
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
                        <Tooltip text={t('sessions.delete')}>
                          <Button
                            tone="ghost"
                            ariaLabel={t('sessions.delete')}
                            busy={remove.isPending && remove.variables === session.id}
                            onClick={() => {
                              if (window.confirm(t('sessions.confirmDelete'))) {
                                remove.mutate(session.id);
                              }
                            }}
                          >
                            <TrashIcon className="size-3.5" />
                          </Button>
                        </Tooltip>
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

import { useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
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

const PAGE_SIZE = 50;

export function SessionsScreen(): ReactNode {
  const queryClient = useQueryClient();
  const [agentName, setAgentName] = useState('');
  const [page, setPage] = useState(0);

  const agents = useQuery({ queryKey: ['agents'], queryFn: api.agents });

  const sessions = useQuery({
    queryKey: ['sessions', agentName, page],
    queryFn: () =>
      api.sessions({
        agentName: agentName.length > 0 ? agentName : undefined,
        skip: page * PAGE_SIZE,
        take: PAGE_SIZE,
      }),
  });

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteSession(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['sessions'] }),
  });

  return (
    <>
      <PageHeader
        title="Sessions"
        description="Persisted conversations. A session and an OpenAI conversation are the same identity — the id shown here is what a client passes as conversation or previous_response_id."
        actions={
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
        }
      />

      {remove.isError && <div className="mb-4"><ErrorNote error={remove.error} /></div>}

      <Panel>
        {sessions.isPending && <Loading />}
        {sessions.isError && <div className="p-4"><ErrorNote error={sessions.error} /></div>}

        {sessions.isSuccess && sessions.data.length === 0 && (
          <Empty title="No sessions">
            A session is created the first time an agent runs with a session id. Start one in the{' '}
            <Link to="playground" className="text-accent underline">Playground</Link>.
          </Empty>
        )}

        {sessions.isSuccess && sessions.data.length > 0 && (
          <>
            <Table>
              <thead>
                <tr>
                  <Th>Session</Th>
                  <Th>Agent</Th>
                  <Th>Created</Th>
                  <Th>Updated</Th>
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
                      <Button
                        tone="ghost"
                        title="Delete session"
                        busy={remove.isPending && remove.variables === session.id}
                        onClick={() => {
                          if (window.confirm('Delete this session and its history?')) {
                            remove.mutate(session.id);
                          }
                        }}
                      >
                        <TrashIcon className="size-3.5" />
                      </Button>
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
  if (page === 0 && size < pageSize) {
    return null;
  }

  return (
    <div className="flex items-center justify-between border-t border-line px-4 py-2 text-[12px] text-muted">
      <span>Page {page + 1}</span>
      <div className="flex gap-2">
        <Button tone="ghost" disabled={page === 0} onClick={() => onChange(page - 1)}>
          Previous
        </Button>
        <Button tone="ghost" disabled={size < pageSize} onClick={() => onChange(page + 1)}>
          Next
        </Button>
      </div>
    </div>
  );
}

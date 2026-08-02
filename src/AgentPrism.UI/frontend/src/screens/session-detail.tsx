import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime } from '../lib/format';
import { foldMessage } from '../lib/transcript';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  JsonView,
  Loading,
  Mono,
  PageHeader,
  Panel,
  cx,
} from '../components/ui';
import { TranscriptView } from '../components/transcript';

type Tab = 'history' | 'state';

export function SessionDetailScreen({ id }: { id: string }): ReactNode {
  const [tab, setTab] = useState<Tab>('history');

  const session = useQuery({ queryKey: ['session', id], queryFn: () => api.session(id) });
  const runs = useQuery({ queryKey: ['runs', 'session', id], queryFn: () => api.runs({ sessionId: id }) });

  if (session.isPending) {
    return <Loading />;
  }

  if (session.isError) {
    return <ErrorNote error={session.error} />;
  }

  const detail = session.data;

  return (
    <>
      <PageHeader
        title="Session"
        description={
          <>
            <Mono>{detail.id}</Mono> — agent{' '}
            <Link to={`agents/${encodeURIComponent(detail.agentName)}`} className="text-accent underline">
              {detail.agentName}
            </Link>
            , updated <span title={absoluteTime(detail.updatedAt)}>{relativeTime(detail.updatedAt)}</span>
          </>
        }
        actions={
          <Link to={`runs?sessionId=${encodeURIComponent(detail.id)}`}>
            <Button>
              {runs.data === undefined ? 'Runs' : `${runs.data.length} run${runs.data.length === 1 ? '' : 's'}`}
            </Button>
          </Link>
        }
      />

      <div className="mb-4 flex gap-1">
        <TabButton active={tab === 'history'} onClick={() => setTab('history')}>
          Chat history
        </TabButton>
        <TabButton active={tab === 'state'} onClick={() => setTab('state')}>
          Raw state
        </TabButton>
      </div>

      {tab === 'history' && (
        <Panel>
          {detail.messages === null ? (
            <Empty title="History is not available">
              The stored state could not be read as a chat history. This happens when the agent has
              left the catalogue or the framework changed its serialisation format. The session
              metadata above is still valid.
            </Empty>
          ) : detail.messages.length === 0 ? (
            <Empty title="No messages yet">
              The identifier is reserved but no run has used it. A conversation created through{' '}
              <Mono>POST /v1/conversations</Mono> stays empty until the first response.
            </Empty>
          ) : (
            <div className="flex flex-col divide-y divide-line">
              {detail.messages.map((message, index) => {
                const folded = foldMessage(message);
                const role = (message.role ?? 'unknown').toLowerCase();

                return (
                  <div key={message.messageId ?? index} className="px-4 py-3">
                    <div className="mb-1.5 flex items-center gap-2">
                      <Badge tone={role === 'user' ? 'accent' : role === 'system' ? 'warn' : 'neutral'}>
                        {role}
                      </Badge>
                      {message.authorName != null && (
                        <span className="text-[11px] text-subtle">{message.authorName}</span>
                      )}
                    </div>
                    {folded.items.length === 0 ? (
                      <p className="text-[12px] text-subtle">No renderable content.</p>
                    ) : (
                      <TranscriptView items={folded.items} />
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </Panel>
      )}

      {tab === 'state' && (
        <Panel title="Serialised session state">
          <div className="p-4">
            <p className="mb-3 text-[12px] text-muted">
              This is the framework's own session state. AgentPrism stores it and never interprets
              it, so its shape belongs to Microsoft Agent Framework.
            </p>
            <JsonView value={detail.state} maxHeight="max-h-[40rem]" />
          </div>
        </Panel>
      )}
    </>
  );
}

function TabButton({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: ReactNode;
}): ReactNode {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cx(
        'rounded-md px-3 py-1.5 text-[13px] font-medium transition-colors',
        active ? 'bg-raised text-fg' : 'text-muted hover:text-fg',
      )}
    >
      {children}
    </button>
  );
}

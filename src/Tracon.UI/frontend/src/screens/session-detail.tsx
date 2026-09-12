import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import type { SessionDetailResponse } from '@tracon/client';
import type { ChatMessage, RunRecord } from '../lib/server-types';
import { absoluteTime, relativeTime } from '../lib/format';
import { usePlural, useT } from '../lib/i18n';
import { foldMessages } from '../lib/transcript';
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
import { BranchButton } from '../components/branch-button';

type Tab = 'history' | 'state';

export function SessionDetailScreen({ id }: { id: string }): ReactNode {
  const t = useT();
  const plural = usePlural();
  const [tab, setTab] = useState<Tab>('history');

  const session = useQuery({
    queryKey: ['session', id],
    queryFn: () =>
      unwrap(
        client.GET('/api/sessions/{sessionId}', { params: { path: { sessionId: id } } }),
      ) as Promise<SessionDetailResponse>,
  });
  const runs = useQuery({
    queryKey: ['runs', 'session', id],
    queryFn: () =>
      unwrap(client.GET('/api/runs', { params: { query: { sessionId: id } } })) as Promise<RunRecord[]>,
  });

  if (session.isPending) {
    return <Loading />;
  }

  if (session.isError) {
    return <ErrorNote error={session.error} />;
  }

  const detail = session.data;
  // `messages` is documented as opaque JSON (the chat history provider does not
  // guarantee a `ChatMessage[]` shape) but the built-in provider always returns
  // one; the old hand-written type trusted the same assumption.
  const messages = detail.messages as ChatMessage[] | null;
  const folds = messages === null ? [] : foldMessages(messages);

  return (
    <>
      <PageHeader
        title={t('common.session')}
        description={
          <>
            <Mono>{detail.id}</Mono> — agent{' '}
            <Link to={`agents/${encodeURIComponent(detail.agentName)}`} className="text-accent underline">
              {detail.agentName}
            </Link>
            , {t('common.updated').toLocaleLowerCase()}{' '}
            <span title={absoluteTime(detail.updatedAt)}>{relativeTime(detail.updatedAt)}</span>
          </>
        }
        actions={
          <>
            <Link to={`playground/${encodeURIComponent(detail.agentName)}?sessionId=${encodeURIComponent(detail.id)}`}>
              <Button>{t('sessionDetail.continueInPlayground')}</Button>
            </Link>
            <Link to={`runs?sessionId=${encodeURIComponent(detail.id)}`}>
              <Button>
                {runs.data === undefined ? t('nav.runs') : plural('sessionDetail.runs', runs.data.length)}
              </Button>
            </Link>
          </>
        }
      />

      <div className="mb-4 flex gap-1">
        <TabButton active={tab === 'history'} onClick={() => setTab('history')}>
          {t('sessionDetail.history')}
        </TabButton>
        <TabButton active={tab === 'state'} onClick={() => setTab('state')}>
          {t('sessionDetail.rawState')}
        </TabButton>
      </div>

      {tab === 'history' && (
        <Panel>
          {messages === null ? (
            <Empty title={t('sessionDetail.noHistory.title')}>{t('sessionDetail.noHistory.body')}</Empty>
          ) : messages.length === 0 ? (
            <Empty title={t('sessionDetail.noMessages.title')}>
              {t('sessionDetail.noMessages.body')} <Mono>POST /v1/conversations</Mono>.
            </Empty>
          ) : (
            <div className="flex flex-col divide-y divide-line">
              {messages.map((message, index) => {
                const folded = folds[index] ?? { items: [], usage: null };
                const role = ((message.role as string | undefined) ?? 'unknown').toLowerCase();

                return (
                  <div key={message.messageId ?? index} className="px-4 py-3">
                    <div className="mb-1.5 flex items-center gap-2">
                      <Badge tone={role === 'user' ? 'accent' : role === 'system' ? 'warn' : 'neutral'}>
                        {role}
                      </Badge>
                      {message.authorName != null && (
                        <span className="text-[11px] text-subtle">{message.authorName}</span>
                      )}
                      {/*
                        The index is the item's own `seq`: this list comes from
                        the chat history provider in sequence order.
                      */}
                      <span className="ml-auto">
                        <BranchButton sessionId={detail.id} upToSequence={index} />
                      </span>
                    </div>
                    {folded.items.length === 0 ? (
                      <p className="text-[12px] text-subtle">{t('sessionDetail.noContent')}</p>
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
        <Panel title={t('sessionDetail.stateTitle')}>
          <div className="p-4">
            <p className="mb-3 text-[12px] text-muted">
              {t('sessionDetail.stateNotice')}
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

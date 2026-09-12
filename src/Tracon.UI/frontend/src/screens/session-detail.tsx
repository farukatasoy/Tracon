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
  Empty,
  ErrorNote,
  JsonView,
  LinkButton,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Stat,
  Tabs,
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

  // 🚨 The header stays put through both. A screen that replaces itself with
  // a bare skeleton loses its own identity while loading, and one that replaces
  // itself with a bare sentence on failure offers nowhere to go next.
  if (session.isPending) {
    return (
      <>
        <PageHeader title={t('common.session')} />
        <Panel>
          <Loading rows={8} />
        </Panel>
      </>
    );
  }

  if (session.isError) {
    return (
      <>
        <PageHeader title={t('common.session')} />
        <Panel>
          <div className="p-4">
            <ErrorNote error={session.error} onRetry={() => void session.refetch()} />
          </div>
        </Panel>
      </>
    );
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
            {/* The identity line, the way `run-detail.tsx` writes it: the full id
                is copyable, because an id that has to be retyped by hand is an
                id that will be retyped wrong. */}
            <Mono copy={detail.id}>{detail.id}</Mono> — {t('common.agent').toLocaleLowerCase()}{' '}
            <Link to={`agents/${encodeURIComponent(detail.agentName)}`}>{detail.agentName}</Link>
          </>
        }
        actions={
          <>
            <LinkButton
              to={`playground/${encodeURIComponent(detail.agentName)}?sessionId=${encodeURIComponent(detail.id)}`}
            >
              {t('sessionDetail.continueInPlayground')}
            </LinkButton>
            <LinkButton to={`runs?sessionId=${encodeURIComponent(detail.id)}`}>
              {runs.data === undefined
                ? t('nav.runs')
                : plural('sessionDetail.runs', runs.data.length)}
            </LinkButton>
          </>
        }
      />

      <div className="mb-4 grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Stat label={t('common.agent')} value={detail.agentName} />
        <Stat
          label={t('sessionDetail.messages')}
          value={messages === null ? '—' : String(messages.length)}
        />
        <Stat
          label={t('nav.runs')}
          value={runs.data === undefined ? '—' : String(runs.data.length)}
        />
        <Stat
          label={t('common.updated')}
          value={relativeTime(detail.updatedAt)}
          hint={absoluteTime(detail.updatedAt)}
        />
      </div>

      <Tabs
        label={t('common.session')}
        value={tab}
        onChange={setTab}
        panels={[
          {
            id: 'history',
            label: t('sessionDetail.history'),
            render: () => (
              <Panel>
                {/* Neither of these carries an action: a session's history is
                    written by runs, and the way to add to it — continuing the
                    conversation — is the header button above. */}
                {messages === null ? (
                  <Empty title={t('sessionDetail.noHistory.title')}>
                    {t('sessionDetail.noHistory.body')}
                  </Empty>
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
                              <span className="text-xs text-subtle">{message.authorName}</span>
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
                            <p className="text-sm text-subtle">{t('sessionDetail.noContent')}</p>
                          ) : (
                            <TranscriptView items={folded.items} />
                          )}
                        </div>
                      );
                    })}
                  </div>
                )}
              </Panel>
            ),
          },
          {
            id: 'state',
            label: t('sessionDetail.rawState'),
            render: () => (
              <Panel title={t('sessionDetail.stateTitle')}>
                <div className="p-4">
                  <p className="mb-3 text-sm text-muted">{t('sessionDetail.stateNotice')}</p>
                  <JsonView value={detail.state} maxHeight="max-h-[40rem]" />
                </div>
              </Panel>
            ),
          },
        ]}
      />
    </>
  );
}

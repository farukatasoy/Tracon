import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, openStream } from '../lib/api';
import { readSse } from '../lib/sse';
import { foldRunEvents } from '../lib/transcript';
import { Link } from '../lib/router';
import { absoluteTime, count, duration, prettyJson, relativeTime, shortId } from '../lib/format';
import {
  Badge,
  CodeBlock,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  cx,
} from '../components/ui';
import { SpinnerIcon } from '../components/icons';
import { TranscriptView } from '../components/transcript';
import { StatusBadge, Stat } from './runs';
import type { RunEvent, RunEventType } from '../lib/types';

/** Event name and hue per event type. Shapes and labels carry the meaning too. */
const EVENT_STYLE: Record<RunEventType, { label: string; hue: string }> = {
  RunStarted: { label: 'run.started', hue: 'var(--ap-indigo)' },
  MessageDelta: { label: 'message.delta', hue: 'var(--ap-cyan)' },
  MessageCompleted: { label: 'message.completed', hue: 'var(--ap-cyan)' },
  ToolInvoking: { label: 'tool.invoking', hue: 'var(--ap-rose)' },
  ToolInvoked: { label: 'tool.invoked', hue: 'var(--ap-rose)' },
  ToolFailed: { label: 'tool.failed', hue: 'var(--ap-danger)' },
  RunCompleted: { label: 'run.completed', hue: 'var(--ap-emerald)' },
  RunFailed: { label: 'run.failed', hue: 'var(--ap-danger)' },
};

/**
 * A single run: summary, folded transcript and the raw event timeline.
 *
 * The event stream is read with Server-Sent Events. Live tailing and historical
 * replay are the same endpoint and the same code path, because run events are
 * append-only (decision K-014) — a finished run simply ends its stream.
 */
export function RunDetailScreen({ id }: { id: string }): ReactNode {
  const [events, setEvents] = useState<RunEvent[]>([]);
  const [streaming, setStreaming] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const abort = useRef<AbortController | null>(null);

  const run = useQuery({
    queryKey: ['run', id],
    queryFn: () => api.run(id),
    refetchInterval: (query) => (query.state.data?.status === 'Running' ? 2_000 : false),
  });

  useEffect(() => {
    const controller = new AbortController();

    abort.current = controller;
    setEvents([]);
    setStreaming(true);
    setError(null);

    void (async () => {
      try {
        const response = await openStream(`api/runs/${encodeURIComponent(id)}/events`, {
          signal: controller.signal,
        });

        for await (const frame of readSse(response)) {
          const event = JSON.parse(frame.data) as RunEvent;

          setEvents((current) => [...current, event]);
        }
      } catch (caught) {
        if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
          setError(caught);
        }
      } finally {
        setStreaming(false);
      }
    })();

    return () => controller.abort();
  }, [id]);

  if (run.isPending) {
    return <Loading />;
  }

  if (run.isError) {
    return <ErrorNote error={run.error} />;
  }

  const record = run.data;
  const transcript = foldRunEvents(events);

  return (
    <>
      <PageHeader
        title="Run"
        description={
          <>
            <Mono>{record.id}</Mono> — agent{' '}
            <Link to={`agents/${encodeURIComponent(record.agentName)}`} className="text-accent underline">
              {record.agentName}
            </Link>
            {record.sessionId != null && (
              <>
                , session{' '}
                <Link
                  to={`sessions/${encodeURIComponent(record.sessionId)}`}
                  className="text-accent underline"
                >
                  <Mono>{shortId(record.sessionId, 12, 5)}</Mono>
                </Link>
              </>
            )}
          </>
        }
        actions={<StatusBadge status={record.status} />}
      />

      <div className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
        <Stat label="Duration" value={duration(record.startedAt, record.completedAt)} />
        <Stat label="Input tokens" value={count(record.usage?.inputTokens)} />
        <Stat label="Output tokens" value={count(record.usage?.outputTokens)} />
        <Stat label="Events" value={count(record.eventCount)} />
      </div>

      {record.error != null && (
        <div className="mb-4">
          <Panel title="Failure">
            <div className="p-4">
              <Badge tone="danger">{record.error.type}</Badge>
              <p className="mt-2 text-[13px] text-danger">{record.error.message}</p>
            </div>
          </Panel>
        </div>
      )}

      {error !== null && <div className="mb-4"><ErrorNote error={error} /></div>}

      <div className="grid gap-4 lg:grid-cols-2">
        <Panel
          title={
            <span className="flex items-center gap-1.5">
              Transcript
              {streaming && <SpinnerIcon className="size-3 text-muted" />}
            </span>
          }
        >
          <div className="p-4">
            {transcript.items.length === 0 ? (
              <p className="text-[13px] text-subtle">
                {streaming ? 'Waiting for events…' : 'This run produced no renderable content.'}
              </p>
            ) : (
              <TranscriptView items={transcript.items} streaming={streaming} />
            )}
          </div>
        </Panel>

        <Panel title={`Event timeline (${events.length})`}>
          {events.length === 0 ? (
            <Empty title={streaming ? 'Waiting for events…' : 'No events'} />
          ) : (
            <ol className="max-h-[40rem] overflow-y-auto p-4">
              {events.map((event) => (
                <EventRow key={event.sequence} event={event} />
              ))}
            </ol>
          )}
        </Panel>
      </div>

      <p className="mt-3 text-[11px] text-subtle">
        Started <span title={absoluteTime(record.startedAt)}>{relativeTime(record.startedAt)}</span>
        {record.isStreaming ? ' · streamed' : ' · non-streaming'}
      </p>
    </>
  );
}

function EventRow({ event }: { event: RunEvent }): ReactNode {
  const style = EVENT_STYLE[event.type] ?? { label: event.type, hue: 'var(--ap-muted)' };
  const body = event.payload ?? event.text ?? null;

  return (
    <li className="relative flex gap-3 pb-3 pl-1 last:pb-0">
      <div className="flex flex-col items-center">
        <span
          aria-hidden="true"
          className="mt-1 size-2 shrink-0 rounded-full"
          style={{ background: style.hue }}
        />
        <span aria-hidden="true" className="mt-1 w-px flex-1 bg-line" />
      </div>

      <div className="min-w-0 flex-1">
        <div className="flex items-baseline gap-2">
          <Mono className="text-subtle">{event.sequence}</Mono>
          <span className="font-mono text-[12px] font-medium" style={{ color: style.hue }}>
            {style.label}
          </span>
          {event.toolName != null && <Badge>{event.toolName}</Badge>}
          <span className="ml-auto text-[11px] text-subtle" title={absoluteTime(event.timestamp)}>
            {new Date(event.timestamp).toLocaleTimeString()}
          </span>
        </div>

        {body !== null && body.length > 0 && (
          <div className={cx('mt-1', event.type === 'MessageDelta' && 'text-[12px]')}>
            {event.type === 'MessageDelta' ? (
              <span className="font-mono break-all text-muted">{body}</span>
            ) : (
              <CodeBlock code={prettyJson(body)} maxHeight="max-h-40" />
            )}
          </div>
        )}
      </div>
    </li>
  );
}

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
import { Waterfall, formatMs } from '../components/waterfall';
import { StatusBadge, Stat } from './runs';
import { ApiError } from '../lib/api';
import type { RunEvent, RunEventType, RunRecord, ToolInvocationRecord } from '../lib/types';

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
  ChildRunStarted: { label: 'child.started', hue: 'var(--ap-amber)' },
  ChildRunCompleted: { label: 'child.completed', hue: 'var(--ap-amber)' },
  HistoryCompacted: { label: 'history.compacted', hue: 'var(--ap-violet)' },
  WorkflowStarted: { label: 'workflow.started', hue: 'var(--ap-emerald)' },
  SuperStepStarted: { label: 'superstep.started', hue: 'var(--ap-muted)' },
  SuperStepCompleted: { label: 'superstep.completed', hue: 'var(--ap-muted)' },
  ExecutorInvoked: { label: 'executor.invoked', hue: 'var(--ap-indigo)' },
  ExecutorCompleted: { label: 'executor.completed', hue: 'var(--ap-indigo)' },
  ExecutorFailed: { label: 'executor.failed', hue: 'var(--ap-danger)' },
  WorkflowOutput: { label: 'workflow.output', hue: 'var(--ap-emerald)' },
  WorkflowRequest: { label: 'workflow.request', hue: 'var(--ap-amber)' },
  RunAwaitingInput: { label: 'run.awaiting-input', hue: 'var(--ap-amber)' },
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

  const finished = run.data != null && run.data.status !== 'Running';

  // Spans and tool rows are written when the run closes, so both are fetched
  // only after it has settled. A 404 on the trace is expected: successful runs
  // are sampled, so most of them carry no spans at all.
  // Alt calistirmalar icin trace HIC istenmez: agactaki her calistirma ayni
  // trace'i paylasir ve tamponun sahibi koktur, dolayisiyla cevap her zaman
  // 404 olurdu.
  const trace = useQuery({
    queryKey: ['run-trace', id],
    queryFn: () => api.runTrace(id),
    enabled: finished && run.data?.parentRunId == null,
    retry: (_, error) => !(error instanceof ApiError && error.status === 404),
  });

  const toolCalls = useQuery({
    queryKey: ['run-tools', id],
    queryFn: () => api.runToolInvocations(id),
    enabled: finished,
  });

  // The tree is fetched whenever this run is part of one — either it has
  // children of its own, or it is itself a child. A run with neither has no
  // tree to draw and the request is skipped.
  const partOfTree = run.data != null && (run.data.childRunCount > 0 || run.data.parentRunId != null);

  const tree = useQuery({
    queryKey: ['run-tree', id],
    queryFn: () => api.runTree(id),
    enabled: partOfTree,
    refetchInterval: (query) =>
      (query.state.data ?? []).some((entry) => entry.status === 'Running') ? 2_000 : false,
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
            <Mono>{record.id}</Mono> —{' '}
            {record.kind === 'Workflow' ? (
              <>
                workflow{' '}
                <Link
                  to={`workflows/${encodeURIComponent(record.workflowName ?? record.agentName)}`}
                  className="text-accent underline"
                >
                  {record.workflowName ?? record.agentName}
                </Link>
              </>
            ) : (
              <>
                agent{' '}
                <Link
                  to={`agents/${encodeURIComponent(record.agentName)}`}
                  className="text-accent underline"
                >
                  {record.agentName}
                </Link>
              </>
            )}
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
            {record.parentRunId != null && (
              <>
                , called by{' '}
                <Link
                  to={`runs/${encodeURIComponent(record.parentRunId)}`}
                  className="text-accent underline"
                >
                  <Mono>{shortId(record.parentRunId, 12, 5)}</Mono>
                </Link>
              </>
            )}
          </>
        }
        actions={<StatusBadge status={record.status} />}
      />

      <div className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-6">
        <Stat label="Duration" value={duration(record.startedAt, record.completedAt)} />
        <Stat label="Model" value={record.modelId ?? '—'} />
        <Stat label="Input tokens" value={count(record.usage?.inputTokens)} />
        <Stat label="Output tokens" value={count(record.usage?.outputTokens)} />
        <Stat
          label="Tree tokens"
          value={count(record.treeUsage?.totalTokens)}
          hint="This run plus every run under it. Already includes this run's own tokens — the two columns are not meant to be added."
        />
        <Stat label="Events" value={count(record.eventCount)} />
      </div>

      {record.status === 'AwaitingInput' && (
        <div className="mb-4">
          <Panel title="Waiting on a human">
            <div className="p-4 text-[13px]">
              This run stopped at a request port and its state is saved in a checkpoint.
              Answer it on the{' '}
              <Link
                to={`workflows/${encodeURIComponent(record.workflowName ?? record.agentName)}`}
                className="text-accent underline"
              >
                workflow screen
              </Link>
              . Answering opens a new run — this row keeps its history exactly as it happened.
            </div>
          </Panel>
        </div>
      )}

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

      {partOfTree && (
        <div className="mt-4">
          <Panel title="Call tree">
            {tree.isPending ? (
              <Loading />
            ) : tree.isError ? (
              <div className="p-4"><ErrorNote error={tree.error} /></div>
            ) : (
              <RunTree runs={tree.data ?? []} current={record.id} />
            )}
          </Panel>
        </div>
      )}

      {finished && (
        <div className="mt-4 grid gap-4 lg:grid-cols-2">
          <Panel title="Trace">
            {trace.isPending ? (
              <Loading />
            ) : trace.isSuccess ? (
              <Waterfall trace={trace.data} />
            ) : record.parentRunId != null ? (
              <Empty title="Spans live on the root run">
                Every run in a call tree shares one trace, and the root run owns it. This run's
                spans are nested inside the{' '}
                <Link
                  to={`runs/${encodeURIComponent(record.rootRunId ?? record.parentRunId)}`}
                  className="text-accent underline"
                >
                  root run's
                </Link>{' '}
                waterfall.
              </Empty>
            ) : (
              <Empty title="No spans recorded">
                Span writing is sampled. Failed runs are kept by default; successful ones are
                kept at the ratio in <Mono>AgentPrism:Observability:SuccessSampleRatio</Mono>.
              </Empty>
            )}
          </Panel>

          <Panel title={`Tool calls (${toolCalls.data?.length ?? 0})`}>
            {toolCalls.isPending ? (
              <Loading />
            ) : (toolCalls.data ?? []).length === 0 ? (
              <Empty title="No tool calls">This run did not invoke a tool.</Empty>
            ) : (
              <ul className="divide-y divide-line">
                {(toolCalls.data ?? []).map((call) => (
                  <ToolCallRow key={call.id} call={call} />
                ))}
              </ul>
            )}
          </Panel>
        </div>
      )}

      <p className="mt-3 text-[11px] text-subtle">
        Started <span title={absoluteTime(record.startedAt)}>{relativeTime(record.startedAt)}</span>
        {record.isStreaming ? ' · streamed' : ' · non-streaming'}
      </p>
    </>
  );
}

/**
 * One recorded tool call.
 *
 * Duration is only measured for streaming runs: in a non-streaming run every
 * message arrives at once, so the real time between the call and its result
 * cannot be read. Writing a near-zero number would be worse than writing none.
 */
function ToolCallRow({ call }: { call: ToolInvocationRecord }): ReactNode {
  return (
    <li className="px-4 py-2.5">
      <div className="flex flex-wrap items-center gap-2">
        <Mono className="text-[12px] font-semibold">{call.toolName}</Mono>
        {call.source != null && (
          <Badge tone="warn" title={`Discovered on the remote MCP server "${call.source}".`}>
            mcp: {call.source}
          </Badge>
        )}
        {call.succeeded ? <Badge tone="accent">ok</Badge> : <Badge tone="danger">failed</Badge>}
        <span className="ml-auto text-[11px] text-subtle">
          {call.duration != null ? formatMs(parseDuration(call.duration)) : 'not measured'}
        </span>
      </div>

      {call.arguments != null && call.arguments.length > 0 && (
        <div className="mt-1.5">
          <CodeBlock code={call.arguments} maxHeight="max-h-28" />
        </div>
      )}

      {call.error != null && <p className="mt-1.5 text-[12px] text-danger">{call.error}</p>}
    </li>
  );
}

/** Parses a .NET TimeSpan ("hh:mm:ss.fffffff") into milliseconds. */
export function parseDuration(value: string): number {
  const match = /^(?:(\d+)\.)?(\d{2}):(\d{2}):(\d{2}(?:\.\d+)?)$/.exec(value);

  if (match === null) {
    return 0;
  }

  const days = Number(match[1] ?? '0');
  const hours = Number(match[2]);
  const minutes = Number(match[3]);
  const seconds = Number(match[4]);

  return ((days * 24 + hours) * 60 + minutes) * 60_000 + seconds * 1_000;
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

/**
 * The whole call tree this run belongs to, nested by parent.
 *
 * Rows are laid out from `parentRunId`, not from `depth`: depth alone cannot say
 * *which* parent a run hangs off when a branch fans out. Orphans — a child whose
 * parent row is missing, which the schema deliberately allows since there is no
 * foreign key — are rendered at the top level rather than dropped.
 */
function RunTree({ runs, current }: { runs: RunRecord[]; current: string }): ReactNode {
  if (runs.length === 0) {
    return <Empty title="No tree" />;
  }

  const present = new Set(runs.map((run) => run.id));
  const children = new Map<string, RunRecord[]>();
  const roots: RunRecord[] = [];

  for (const run of runs) {
    const parent = run.parentRunId;

    if (parent == null || !present.has(parent)) {
      roots.push(run);
      continue;
    }

    const bucket = children.get(parent);

    if (bucket === undefined) {
      children.set(parent, [run]);
    } else {
      bucket.push(run);
    }
  }

  const rows: ReactNode[] = [];

  const push = (run: RunRecord, indent: number): void => {
    rows.push(<RunTreeRow key={run.id} run={run} indent={indent} isCurrent={run.id === current} />);

    for (const child of children.get(run.id) ?? []) {
      push(child, indent + 1);
    }
  };

  for (const root of roots) {
    push(root, 0);
  }

  return <ul className="divide-y divide-line">{rows}</ul>;
}

function RunTreeRow({
  run,
  indent,
  isCurrent,
}: {
  run: RunRecord;
  indent: number;
  isCurrent: boolean;
}): ReactNode {
  return (
    <li className={cx('flex flex-wrap items-center gap-2 px-4 py-2', isCurrent && 'bg-raised')}>
      <span style={{ paddingLeft: `${indent * 1.25}rem` }} className="flex items-center gap-2">
        {indent > 0 && <span aria-hidden="true" className="text-subtle">└</span>}
        {isCurrent ? (
          <Mono className="text-[12px] font-semibold">{shortId(run.id, 12, 5)}</Mono>
        ) : (
          <Link to={`runs/${encodeURIComponent(run.id)}`}>
            <Mono className="text-[12px]" title={run.id}>{shortId(run.id, 12, 5)}</Mono>
          </Link>
        )}
      </span>

      <Link to={`agents/${encodeURIComponent(run.agentName)}`} className="text-[12px] text-muted hover:text-fg">
        {run.agentName}
      </Link>

      <StatusBadge status={run.status} />
      {isCurrent && <Badge tone="accent">this run</Badge>}

      <span className="ml-auto flex items-center gap-3 text-[11px] text-subtle">
        <span>{count(run.usage?.totalTokens)} tokens</span>
        <span>{duration(run.startedAt, run.completedAt)}</span>
      </span>
    </li>
  );
}

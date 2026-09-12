import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap, openStream, TraconError } from '../lib/api';
import { formatDateTime, useT } from '../lib/i18n';
import { readSse } from '@tracon/client';
import { foldRunEvents } from '../lib/transcript';
import { Link, useNavigate } from '../lib/router';
import { absoluteTime, count, duration, money, prettyJson, relativeTime, shortId } from '../lib/format';
import {
  Badge,
  CodeBlock,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Stat,
  cx,
} from '../components/ui';
import { StatusDot, STATUS_TEXT, type StatusTone } from '../components/status-dot';
import { Menu, type MenuItem } from '../components/menu';
import { CancelRunButton } from '../components/cancel-run-button';
import { FeedbackControl } from '../components/feedback-control';
import { PromoteToEvalCase } from '../components/promote-to-eval-case';
import { ReplayPanel } from '../components/replay-panel';
import { RunComparison } from '../components/run-comparison';
import { TranscriptView } from '../components/transcript';
import { Waterfall, formatMs } from '../components/waterfall';
import { StatusBadge } from './runs';
import type { RunEvent, RunEventType } from '../lib/run-event';
import type { RunRecord, RunTrace, ToolInvocationRecord } from '../lib/server-types';

/**
 * Formats an applied unit price, or an em dash when it was never priced —
 * never `0`, which would read as "free" (same rule as `money`).
 */
function unitPrice(
  t: ReturnType<typeof useT>,
  value: number | null | undefined,
  currency: string | null | undefined,
): string {
  return value == null ? '—' : t('runDetail.unitPrice.perMillionTokens', { price: money(value, currency) });
}

/**
 * Event name and status tone per event type.
 *
 * The console has ONE accent and a fixed status vocabulary (styles.css); an
 * event row is coloured by what KIND of thing happened, not by which subsystem
 * emitted it:
 *
 *   accent   the model is producing — a delta on the wire
 *   info     lifecycle — something started, attached, or was invoked
 *   success  cleared — something finished the way it was supposed to
 *   warn     holding — degraded, waiting, truncated, retried
 *   danger   denied — the run is ending Failed because of this
 *   neutral  structural — a marker that reports progress, not an outcome
 *
 * The label and the row's own shape carry the meaning too, so the timeline
 * still reads with no colour at all.
 */
const EVENT_STYLE: Record<RunEventType, { label: string; tone: StatusTone }> = {
  RunStarted: { label: 'run.started', tone: 'info' },
  MessageDelta: { label: 'message.delta', tone: 'accent' },
  MessageCompleted: { label: 'message.completed', tone: 'success' },
  ToolInvoking: { label: 'tool.invoking', tone: 'info' },
  ToolInvoked: { label: 'tool.invoked', tone: 'success' },
  ToolFailed: { label: 'tool.failed', tone: 'danger' },
  RunCompleted: { label: 'run.completed', tone: 'success' },
  RunFailed: { label: 'run.failed', tone: 'danger' },
  ChildRunStarted: { label: 'child.started', tone: 'info' },
  ChildRunCompleted: { label: 'child.completed', tone: 'success' },
  HistoryCompacted: { label: 'history.compacted', tone: 'neutral' },
  WorkflowStarted: { label: 'workflow.started', tone: 'info' },
  SuperStepStarted: { label: 'superstep.started', tone: 'neutral' },
  SuperStepCompleted: { label: 'superstep.completed', tone: 'neutral' },
  ExecutorInvoked: { label: 'executor.invoked', tone: 'info' },
  ExecutorCompleted: { label: 'executor.completed', tone: 'success' },
  ExecutorFailed: { label: 'executor.failed', tone: 'danger' },
  WorkflowOutput: { label: 'workflow.output', tone: 'success' },
  WorkflowRequest: { label: 'workflow.request', tone: 'warn' },
  RunAwaitingInput: { label: 'run.awaiting-input', tone: 'warn' },
  // Phase 48. A guard decision is a policy event: masking rewrote the prompt
  // and the run carried on (warn), a block ended it (danger).
  ContentMasked: { label: 'content.masked', tone: 'warn' },
  ContentBlocked: { label: 'content.blocked', tone: 'danger' },
  // Phase 62. A fallback is a mitigation the operator should notice.
  ModelFallbackUsed: { label: 'model.fallback-used', tone: 'warn' },
  // Phase 70. The model's reasoning, not its answer — but still the model
  // producing, so it shares the live accent with MessageDelta.
  ReasoningDelta: { label: 'reasoning.delta', tone: 'accent' },
  // Phase 14. A document joining the run is part of its setup, not an outcome.
  DocumentAttached: { label: 'document.attached', tone: 'info' },
  // Phase 89. A tool that keeps getting truncated is a sign its own output
  // bound is missing.
  ToolOutputTruncated: { label: 'tool.output-truncated', tone: 'warn' },
  // Phase 87. Written on a run that is ALREADY Failed, to explain why
  // orphaned-run reconciliation declined to continue it automatically — not
  // a new failure of its own.
  RunContinuationBlocked: { label: 'run.continuation-blocked', tone: 'warn' },
  // Phase 131. The run is ending Failed, same as ContentBlocked/RunFailed.
  StructuredResponseRejected: { label: 'structured-response.rejected', tone: 'danger' },
  // Phase 134. The run is not over yet, a repair turn is about to try again.
  StructuredResponseRepairAttempted: { label: 'structured-response.repair-attempted', tone: 'warn' },
  // Phase 141. The label here is only a fallback: EventRow shows the event's
  // OWN CustomType instead whenever one is present, which it always is by
  // the time it reaches the wire (RunEventWriter rejects a Custom event
  // without one).
  Custom: { label: 'custom', tone: 'neutral' },
  // Phase 144. A sub-agent call ran past its wait limit — the tree kept going,
  // but an operator should notice which layer cut it (payload's hardCutoff).
  ChildRunTimedOut: { label: 'child.timed-out', tone: 'warn' },
  // Phase 151. One harness loop iteration finished. It reports progress, not a
  // problem — the payload's continuedBy names the criterion that asked for
  // another turn.
  LoopIterationCompleted: { label: 'loop.iteration-completed', tone: 'neutral' },
};

/**
 * A single run: summary, folded transcript and the raw event timeline.
 *
 * The event stream is read with Server-Sent Events. Live tailing and historical
 * replay are the same endpoint and the same code path, because run events are
 * append-only (decision K-014) — a finished run simply ends its stream.
 */
export function RunDetailScreen({ id }: { id: string }): ReactNode {
  const t = useT();
  const navigate = useNavigate();
  const [events, setEvents] = useState<RunEvent[]>([]);
  const [streaming, setStreaming] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const abort = useRef<AbortController | null>(null);

  const run = useQuery({
    queryKey: ['run', id],
    queryFn: () => unwrap(client.GET('/api/runs/{runId}', { params: { path: { runId: id } } })) as Promise<RunRecord>,
    // 'Queued' polls too (phase 46): the worker has not picked the job up yet,
    // and the row transitions on its own once it does.
    refetchInterval: (query) =>
      query.state.data?.status === 'Running' || query.state.data?.status === 'Queued' ? 2_000 : false,
  });

  const finished =
    run.data != null && run.data.status !== 'Running' && run.data.status !== 'Queued';

  // Spans and tool rows are written when the run closes, so both are fetched
  // only after it has settled. A 404 on the trace is expected: successful runs
  // are sampled, so most of them carry no spans at all.
  // Child runs NEVER request a trace: every run in the tree shares the same
  // trace, and the buffer's owner is the root run, so the response would
  // always be 404.
  const trace = useQuery({
    queryKey: ['run-trace', id],
    queryFn: () =>
      unwrap(client.GET('/api/runs/{runId}/trace', { params: { path: { runId: id } } })) as Promise<RunTrace>,
    enabled: finished && run.data?.parentRunId == null,
    retry: (_, error) => !(error instanceof TraconError && error.status === 404),
  });

  const toolCalls = useQuery({
    queryKey: ['run-tools', id],
    queryFn: () =>
      unwrap(
        client.GET('/api/runs/{runId}/tools', { params: { path: { runId: id } } }),
      ) as Promise<ToolInvocationRecord[]>,
    enabled: finished,
  });

  // The tree is fetched whenever this run is part of one — either it has
  // children of its own, or it is itself a child. A run with neither has no
  // tree to draw and the request is skipped.
  const partOfTree = run.data != null && (run.data.childRunCount > 0 || run.data.parentRunId != null);

  const tree = useQuery({
    queryKey: ['run-tree', id],
    queryFn: () =>
      unwrap(client.GET('/api/runs/{runId}/tree', { params: { path: { runId: id } } })) as Promise<RunRecord[]>,
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
    return <Loading rows={6} />;
  }

  if (run.isError) {
    return <ErrorNote error={run.error} onRetry={() => void run.refetch()} />;
  }

  const record = run.data;
  const transcript = foldRunEvents(events);

  // Where this run came from and what it belongs to. Each entry is a real
  // record; an absent relation is simply not offered rather than shown disabled.
  const related: (MenuItem | null)[] = [
    record.sessionId == null
      ? null
      : {
          id: 'session',
          label: `${t('runDetail.forSession')} ${shortId(record.sessionId, 12, 5)}`,
          onSelect: () => navigate(`sessions/${encodeURIComponent(record.sessionId ?? '')}`),
        },
    record.parentRunId == null
      ? null
      : {
          id: 'parent',
          label: `${t('runDetail.calledBy')} ${shortId(record.parentRunId, 12, 5)}`,
          onSelect: () => navigate(`runs/${encodeURIComponent(record.parentRunId ?? '')}`),
        },
    record.replayOfRunId == null
      ? null
      : {
          id: 'replay-source',
          label: `${t('replay.sourceLink')} ${shortId(record.replayOfRunId, 12, 5)}`,
          onSelect: () => navigate(`runs/${encodeURIComponent(record.replayOfRunId ?? '')}`),
        },
    record.continuedFromRunId == null
      ? null
      : {
          id: 'continued-from',
          label: `${t('runDetail.continuationOf')} ${shortId(record.continuedFromRunId, 12, 5)}`,
          onSelect: () => navigate(`runs/${encodeURIComponent(record.continuedFromRunId ?? '')}`),
        },
  ];
  const relatedItems = related.filter((item): item is MenuItem => item !== null);

  return (
    <>
      <PageHeader
        title={t('runs.column.run')}
        description={
          // The identity line. A run is identified by its id, which is a thing
          // an operator copies into a query or a ticket — so it is monospace and
          // copyable — and by the agent or workflow that owns it. Everything
          // else it is RELATED to moved into the menu below: seven inline links
          // in one sentence read as a paragraph, not as an instrument.
          <span className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <Mono copy={record.id}>{record.id}</Mono>
            <span className="text-subtle">·</span>
            {record.kind === 'Workflow' ? (
              <>
                {t('runDetail.forWorkflow')}{' '}
                <Link
                  to={`workflows/${encodeURIComponent(record.workflowName ?? record.agentName)}`}
                  className="text-accent underline"
                >
                  {record.workflowName ?? record.agentName}
                </Link>
              </>
            ) : (
              <>
                {t('runDetail.forAgent')}{' '}
                <Link to={`agents/${encodeURIComponent(record.agentName)}`} className="text-accent underline">
                  {record.agentName}
                </Link>
              </>
            )}
            {record.userId != null && (
              <>
                <span className="text-subtle">·</span>
                {t('runDetail.forUser')} <Mono>{record.userId}</Mono>
              </>
            )}
            {record.labels != null &&
              Object.entries(record.labels).map(([key, value]) => (
                <Badge key={key} tone="neutral">
                  {key}: {value}
                </Badge>
              ))}
          </span>
        }
        actions={
          <div className="flex items-center gap-2">
            {relatedItems.length > 0 && (
              <Menu label={t('runDetail.related')} items={relatedItems} testId="run-related" />
            )}
            {(record.status === 'Running' || record.status === 'Queued') && (
              <CancelRunButton runId={record.id} />
            )}
            <StatusBadge status={record.status} />
          </div>
        }
      />

      {/* Seven figures: two columns on a phone, four on a tablet, one row on a
          desktop. A six-column grid left `Events` alone on a second row. */}
      <div className="mb-3 grid grid-cols-2 gap-2 sm:grid-cols-4 xl:grid-cols-7">
        <Stat label={t('common.duration')} value={duration(record.startedAt, record.completedAt)} />
        <Stat label={t('common.model')} value={record.modelId ?? '—'} />
        <Stat label={t('common.provider')} value={record.modelProvider ?? '—'} />
        <Stat label={t('runDetail.inputTokens')} value={count(record.usage?.inputTokens)} />
        <Stat label={t('runDetail.outputTokens')} value={count(record.usage?.outputTokens)} />
        <Stat
          label={t('runs.column.treeTokens')}
          value={count(record.treeUsage?.totalTokens)}
          hint={t('runs.treeTokensTitle')}
        />
        <Stat label={t('runs.column.events')} value={count(record.eventCount)} />
      </div>

      {record.cost != null && (
        <div className="mb-3 grid grid-cols-2 gap-2 sm:grid-cols-3">
          <Stat
            label={t('runDetail.unitPrice.input')}
            value={unitPrice(t, record.cost.inputPricePerMillionTokens, record.cost.currency)}
            hint={t('runDetail.unitPrice.hint')}
          />
          <Stat
            label={t('runDetail.unitPrice.output')}
            value={unitPrice(t, record.cost.outputPricePerMillionTokens, record.cost.currency)}
            hint={t('runDetail.unitPrice.hint')}
          />
          <Stat
            label={t('runDetail.unitPrice.cached')}
            value={unitPrice(t, record.cost.cachedInputPricePerMillionTokens, record.cost.currency)}
            hint={t('runDetail.unitPrice.hint')}
          />
        </div>
      )}

      {record.status === 'AwaitingInput' && (
        <div className="mb-4">
          <Panel title={t('runDetail.awaiting.title')}>
            <div className="p-4 text-base">
              {t('runDetail.awaiting.before')}{' '}
              <Link
                to={`workflows/${encodeURIComponent(record.workflowName ?? record.agentName)}`}
                className="text-accent underline"
              >
                {t('runDetail.awaiting.link')}
              </Link>
              . {t('runDetail.awaiting.after')}
            </div>
          </Panel>
        </div>
      )}

      {record.error != null && (
        <div className="mb-4">
          <Panel title={t('runDetail.failure')}>
            <div className="p-4">
              <Badge tone="danger">{record.error.type}</Badge>
              <p className="mt-2 text-base text-danger">{record.error.message}</p>
            </div>
          </Panel>
        </div>
      )}

      {/* This screen's own action failures — a cancel, a branch. No retry: the
          action is the button that produced it, and re-running a cancel from an
          error note would hide which run it applies to. */}
      {error !== null && (
        <div className="mb-4">
          <ErrorNote error={error} />
        </div>
      )}

      {finished && (
        <div className="mb-4">
          <FeedbackControl runId={record.id} />
        </div>
      )}

      {(record.status === 'Completed' || record.status === 'Failed') && (
        <div className="mb-4">
          <PromoteToEvalCase runId={record.id} />
        </div>
      )}

      {/*
        A replay is offered only for a settled agent run: a workflow keeps its
        own resume path (checkpoints), and a run still in flight has no result
        worth reproducing yet.
      */}
      {finished && record.kind === 'Agent' && (
        <div className="mb-4">
          <ReplayPanel runId={record.id} agentName={record.agentName} />
        </div>
      )}

      {record.replayOfRunId != null && (
        <div className="mb-4">
          <RunComparison left={record.replayOfRunId} right={record.id} />
        </div>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <Panel
          title={
            <span className="flex items-center gap-1.5">
              {t('runDetail.transcript')}
              {streaming && <StatusDot tone="accent" live label={t('runDetail.waiting')} />}
            </span>
          }
        >
          <div className="p-4" aria-live="polite" aria-busy={streaming}>
            {transcript.items.length === 0 ? (
              <p className="text-base text-subtle">
                {streaming ? t('runDetail.waiting') : t('runDetail.noContent')}
              </p>
            ) : (
              <TranscriptView items={transcript.items} streaming={streaming} />
            )}
          </div>
        </Panel>

        <Panel title={t('runDetail.timeline', { count: events.length })}>
          {events.length === 0 ? (
            /* None of this screen's empty states carries an action, and the
               reason is what a run IS: a record. Events, spans, tool calls and
               the child tree are written while it executes; nothing pressed here
               afterwards can add one. */
            <Empty title={streaming ? t('runDetail.waiting') : t('runDetail.noEvents')} />
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
          <Panel title={t('runDetail.callTree')}>
            {tree.isPending ? (
              <Loading rows={3} />
            ) : tree.isError ? (
              <div className="p-4"><ErrorNote error={tree.error} onRetry={() => void tree.refetch()} /></div>
            ) : (
              <RunTree runs={tree.data ?? []} current={record.id} />
            )}
          </Panel>
        </div>
      )}

      {finished && (
        <div className="mt-4 grid gap-4 lg:grid-cols-2">
          <Panel title={t('runDetail.trace')}>
            {record.parentRunId != null ? (
              // `trace` is `enabled: finished && parentRunId == null` (a
              // sub-run shares its root's trace, so its own request would
              // always 404) — checked first because a disabled query's
              // `isPending` never leaves `true` (React Query v5), which would
              // otherwise show a permanent spinner instead of this link.
              /* Recorded structure: spans live on the root run, and the link
                 below goes there rather than offering to create anything. */
              <Empty title={t('runDetail.spansOnRoot.title')}>
                {t('runDetail.spansOnRoot.before')}{' '}
                <Link
                  to={`runs/${encodeURIComponent(record.rootRunId ?? record.parentRunId)}`}
                >
                  {t('runDetail.spansOnRoot.link')}
                </Link>{' '}
                {t('runDetail.spansOnRoot.after')}
              </Empty>
            ) : trace.isPending ? (
              <Loading rows={5} />
            ) : trace.isSuccess ? (
              <Waterfall trace={trace.data} />
            ) : (
              /* Sampling decided this, not the operator; the body names the
                 setting that changes it. */
              <Empty title={t('runDetail.noSpans.title')}>
                {t('runDetail.noSpans.body')}{' '}
                <Mono>Tracon:Observability:SuccessSampleRatio</Mono>.
              </Empty>
            )}
          </Panel>

          <Panel title={t('runDetail.toolCalls', { count: toolCalls.data?.length ?? 0 })}>
            {toolCalls.isPending ? (
              <Loading rows={3} />
            ) : (toolCalls.data ?? []).length === 0 ? (
              /* Recorded: the agent either called a tool or it did not. */
              <Empty title={t('runDetail.noToolCalls.title')}>{t('runDetail.noToolCalls.body')}</Empty>
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

      <p className="mt-3 text-xs text-subtle">
        {t('common.started')}{' '}
        <span title={absoluteTime(record.startedAt)}>{relativeTime(record.startedAt)}</span>
        {record.isStreaming ? ` · ${t('runDetail.streamed')}` : ` · ${t('runDetail.nonStreaming')}`}
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
  const t = useT();

  return (
    <li className="px-4 py-2.5">
      <div className="flex flex-wrap items-center gap-2">
        <Mono className="text-sm font-semibold">{call.toolName}</Mono>
        {call.source != null && (
          <Badge tone="warn" description={t('runDetail.mcpSource', { server: call.source })}>
            mcp: {call.source}
          </Badge>
        )}
        {call.succeeded ? (
          <Badge tone="accent">{t('runDetail.ok')}</Badge>
        ) : (
          <Badge tone="danger">{t('runs.status.failed')}</Badge>
        )}
        <span className="ml-auto text-xs text-subtle">
          {call.duration != null ? formatMs(parseDuration(call.duration)) : t('runDetail.notMeasured')}
        </span>
      </div>

      {call.arguments != null && call.arguments.length > 0 && (
        <div className="mt-1.5">
          <CodeBlock code={call.arguments} maxHeight="max-h-28" />
        </div>
      )}

      {call.error != null && <p className="mt-1.5 text-sm text-danger">{call.error}</p>}
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
  const style: { label: string; tone: StatusTone } =
    EVENT_STYLE[event.type] ?? { label: event.type, tone: 'neutral' };
  // 141.2: a Custom event's own name IS its CustomType, not the generic
  // fallback label -- a consumer's contoso.preview-ready must read as
  // that, not as an unstyled "Custom" row.
  const label = event.type === 'Custom' && event.customType != null ? event.customType : style.label;
  const body = event.payload ?? event.text ?? null;

  return (
    <li className="relative flex gap-3 pb-3 pl-1 last:pb-0">
      <div className="flex flex-col items-center">
        <StatusDot tone={style.tone} className="mt-1" />
        <span aria-hidden="true" className="mt-1 w-px flex-1 bg-line" />
      </div>

      <div className="min-w-0 flex-1">
        <div className="flex items-baseline gap-2">
          <Mono className="text-subtle">{event.sequence}</Mono>
          <span className={cx('font-mono text-id font-medium', STATUS_TEXT[style.tone])}>{label}</span>
          {event.toolName != null && <Badge>{event.toolName}</Badge>}
          <span className="ml-auto text-xs text-subtle" title={absoluteTime(event.timestamp)}>
            {formatDateTime(new Date(event.timestamp), { timeStyle: 'medium' })}
          </span>
        </div>

        {body !== null && body.length > 0 && (
          <div className={cx('mt-1', event.type === 'MessageDelta' && 'text-sm')}>
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
  const t = useT();

  if (runs.length === 0) {
    // Recorded structure, so no action — see the timeline above.
    return <Empty title={t('runDetail.noTree')} />;
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
  const t = useT();

  return (
    <li className={cx('flex flex-wrap items-center gap-2 px-4 py-2', isCurrent && 'bg-raised')}>
      <span style={{ paddingLeft: `${indent * 1.25}rem` }} className="flex items-center gap-2">
        {indent > 0 && <span aria-hidden="true" className="text-subtle">└</span>}
        {isCurrent ? (
          <Mono className="text-sm font-semibold">{shortId(run.id, 12, 5)}</Mono>
        ) : (
          <Link to={`runs/${encodeURIComponent(run.id)}`}>
            <Mono className="text-sm" title={run.id}>{shortId(run.id, 12, 5)}</Mono>
          </Link>
        )}
      </span>

      <Link to={`agents/${encodeURIComponent(run.agentName)}`} className="text-sm text-muted hover:text-fg">
        {run.agentName}
      </Link>

      <StatusBadge status={run.status} />
      {isCurrent && <Badge tone="accent">{t('runDetail.thisRun')}</Badge>}

      <span className="ml-auto flex items-center gap-3 text-xs text-subtle">
        <span>
          {count(run.usage?.totalTokens)} {t('common.tokens').toLocaleLowerCase()}
        </span>
        <span>{duration(run.startedAt, run.completedAt)}</span>
      </span>
    </li>
  );
}

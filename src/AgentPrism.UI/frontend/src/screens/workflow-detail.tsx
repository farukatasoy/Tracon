import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, openStream } from '../lib/api';
import { readSse } from '../lib/sse';
import { Link, useNavigate } from '../lib/router';
import { shortId } from '../lib/format';
import { useT, type MessageKey } from '../lib/i18n';
import { foldNodeStates } from '../lib/workflow-graph';
import {
  Badge,
  Button,
  CopyButton,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  TextArea,
  TextInput,
} from '../components/ui';
import { SpinnerIcon } from '../components/icons';
import { WorkflowGraphLegend, WorkflowGraphView } from '../components/workflow-graph';
import { KIND_HINT } from './workflows';
import type { Meta, RunEvent, WorkflowPendingRequest } from '../lib/types';

/**
 * One workflow: its graph, a place to run it, and the human decisions it is
 * waiting on.
 *
 * The graph is fetched compiled, so its node ids are exactly the executor ids
 * the run events carry. Colouring nodes while a run streams is therefore a map
 * lookup, not guesswork.
 */
export function WorkflowDetailScreen({ name, meta }: { name: string; meta: Meta }): ReactNode {
  const t = useT();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const workflow = useQuery({
    queryKey: ['workflow-descriptor', name],
    queryFn: async () => (await api.workflows()).find((entry) => entry.name === name) ?? null,
  });

  const graph = useQuery({
    queryKey: ['workflow-graph', name],
    queryFn: () => api.workflowGraph(name),
  });

  const [message, setMessage] = useState('');
  const [runId, setRunId] = useState<string | null>(null);
  const [events, setEvents] = useState<RunEvent[]>([]);
  const [streaming, setStreaming] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const abort = useRef<AbortController | null>(null);

  useEffect(() => () => abort.current?.abort(), []);

  const states = useMemo(() => foldNodeStates(events), [events]);
  const finished = runId !== null && !streaming;

  // Pending requests are only meaningful once the stream has closed: while it
  // is open the run may still move past the port on its own.
  const pending = useQuery({
    queryKey: ['workflow-requests', runId],
    queryFn: () => api.workflowRequests(runId as string),
    enabled: finished,
  });

  const consume = useCallback(
    async (response: Response) => {
      for await (const frame of readSse(response)) {
        if (frame.event === 'run') {
          setRunId((JSON.parse(frame.data) as { runId: string }).runId);

          continue;
        }

        if (frame.event === 'event') {
          setEvents((current) => [...current, JSON.parse(frame.data) as RunEvent]);

          continue;
        }

        if (frame.event === 'error') {
          const payload = JSON.parse(frame.data) as { type: string; message: string };

          setError(new Error(`${payload.type}: ${payload.message}`));
        }
      }

      await queryClient.invalidateQueries({ queryKey: ['runs'] });
    },
    [queryClient],
  );

  const start = useCallback(async () => {
    abort.current?.abort();

    const controller = new AbortController();

    abort.current = controller;
    setRunId(null);
    setEvents([]);
    setError(null);
    setStreaming(true);

    try {
      const response = await openStream(`api/workflows/${encodeURIComponent(name)}/run`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message: message.trim().length === 0 ? null : message }),
        signal: controller.signal,
      });

      await consume(response);
    } catch (caught) {
      if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
        setError(caught);
      }
    } finally {
      setStreaming(false);
    }
  }, [consume, message, name]);

  /**
   * Answers a pending request and follows the run that continues from it.
   *
   * The answer opens a *new* run: history is append-only, so the original row
   * keeps its `AwaitingInput` status and this screen switches to the new one.
   */
  const respond = useCallback(
    async (request: WorkflowPendingRequest, body: Record<string, unknown>) => {
      const controller = new AbortController();

      abort.current = controller;
      setEvents([]);
      setError(null);
      setStreaming(true);

      try {
        const response = await openStream(
          `api/workflows/runs/${encodeURIComponent(request.runId)}/respond`,
          {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ requestId: request.requestId, ...body }),
            signal: controller.signal,
          },
        );

        await consume(response);
      } catch (caught) {
        if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
          setError(caught);
        }
      } finally {
        setStreaming(false);
      }
    },
    [consume],
  );

  if (workflow.isPending) {
    return <Loading />;
  }

  if (workflow.isError) {
    return <ErrorNote error={workflow.error} />;
  }

  if (workflow.data === null) {
    return (
      <Panel>
        <Empty title={t('workflowDetail.notFound')}>
          {t('workflowDetail.notFoundBody')}{' '}
          <Link to="workflows" className="text-accent underline">
            {t('nav.workflows')}
          </Link>
          .
        </Empty>
      </Panel>
    );
  }

  const descriptor = workflow.data;
  const editable = descriptor.origin !== 'Code' && meta.roles.canAdminister;

  // A code-defined workflow is a free graph: no pattern, and nothing to edit
  // here. Its shape is only visible in the compiled graph below.
  const kindHintKey: MessageKey =
    descriptor.kind == null ? 'workflowDetail.codeGraphHint' : KIND_HINT[descriptor.kind];
  const kindHint = t(kindHintKey);

  return (
    <>
      <PageHeader
        title={descriptor.displayName ?? descriptor.name}
        description={descriptor.description ?? kindHint}
        actions={
          <>
            <Badge tone={descriptor.kind == null ? 'neutral' : 'accent'} title={kindHint}>
              {descriptor.kind ?? t('workflows.codeGraph')}
            </Badge>
            {editable && (
              <Button onClick={() => navigate(`workflows/${encodeURIComponent(name)}/edit`)}>
                {t('common.edit')}
              </Button>
            )}
          </>
        }
      />

      {error !== null && (
        <div className="mb-3">
          <ErrorNote error={error} />
        </div>
      )}

      <div className="flex flex-col gap-4">
        <Panel
          title={t('workflowDetail.graph')}
          actions={
            graph.isSuccess && (
              <span className="relative">
                {/* The console draws the graph itself; this hands out Microsoft
                    Agent Framework's Mermaid text so it can be pasted into a
                    document, where a real layout engine can place it. */}
                <span className="inline-flex h-8 items-center rounded-md border border-line bg-raised pr-8 pl-3 text-[13px]">
                  {t('workflowDetail.copyMermaid')}
                </span>
                <CopyButton value={graph.data.mermaid} />
              </span>
            )
          }
        >
          {graph.isPending && <Loading />}
          {graph.isError && (
            <div className="p-4">
              <ErrorNote error={graph.error} />
            </div>
          )}

          {graph.isSuccess && (
            <div className="flex flex-col gap-3 p-4">
              <WorkflowGraphView graph={graph.data} states={states} />
              <WorkflowGraphLegend />
            </div>
          )}
        </Panel>

        {meta.roles.canOperate && (
          <Panel title={t('common.run')}>
            <div className="flex flex-col gap-3 p-4">
              <div className="flex items-end gap-2">
                <TextInput
                  value={message}
                  data-testid="workflow-message"
                  placeholder={t('workflowDetail.messagePlaceholder')}
                  disabled={streaming}
                  onChange={(event) => setMessage(event.target.value)}
                />
                {streaming ? (
                  <Button onClick={() => abort.current?.abort()}>{t('workflowDetail.stop')}</Button>
                ) : (
                  <Button tone="primary" testId="workflow-run" onClick={() => void start()}>
                    {t('common.run')}
                  </Button>
                )}
              </div>

              {runId !== null && (
                <p className="flex items-center gap-2 text-[12px] text-subtle">
                  {streaming && <SpinnerIcon className="size-3" />}
                  <Link
                    to={`runs/${encodeURIComponent(runId)}`}
                    className="text-accent underline"
                    title={t('workflowDetail.inspectRun')}
                  >
                    <Mono>{t('workflowDetail.runId', { id: shortId(runId, 8, 4) })}</Mono>
                  </Link>
                  <span>{t('workflowDetail.eventCount', { count: events.length })}</span>
                </p>
              )}

              <Output events={events} />
            </div>
          </Panel>
        )}

        {finished && pending.isSuccess && pending.data.length > 0 && (
          <Panel title={t('workflowDetail.waitingOnYou')}>
            <div className="flex flex-col gap-3 p-4">
              {pending.data.map((request) => (
                <PendingRequestCard
                  key={request.requestId}
                  request={request}
                  disabled={streaming || !meta.roles.canOperate}
                  onRespond={(body) => void respond(request, body)}
                />
              ))}
            </div>
          </Panel>
        )}
      </div>
    </>
  );
}

/** Whatever the graph produced, plus any failure it reported. */
function Output({ events }: { events: readonly RunEvent[] }): ReactNode {
  const outputs = events.filter((event) => event.type === 'WorkflowOutput');
  const failures = events.filter((event) => event.type === 'ExecutorFailed');

  if (outputs.length === 0 && failures.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-2" data-testid="workflow-output" aria-live="polite">
      {outputs.map((event) => (
        <p
          key={event.sequence}
          className="rounded-md border border-line bg-raised px-3 py-2 text-[13px] whitespace-pre-wrap"
        >
          {event.text}
        </p>
      ))}

      {failures.map((event) => (
        <p
          key={event.sequence}
          className="rounded-md border border-line bg-danger-soft px-3 py-2 text-[12px] text-danger"
        >
          <Mono>{event.text}</Mono> {event.payload}
        </p>
      ))}
    </div>
  );
}

/**
 * One decision the run is blocked on.
 *
 * The input shown comes from the port's response type, which the server
 * resolves and reports as `form`. Guessing it on the client would mean putting
 * .NET type names on the wire.
 */
function PendingRequestCard({
  request,
  disabled,
  onRespond,
}: {
  request: WorkflowPendingRequest;
  disabled: boolean;
  onRespond: (body: Record<string, unknown>) => void;
}): ReactNode {
  const t = useT();
  const [text, setText] = useState('');

  return (
    <div
      className="rounded-md border border-line bg-warn-soft p-3"
      data-testid="workflow-pending-request"
    >
      <div className="mb-2 flex flex-wrap items-center gap-1.5">
        <Badge tone="warn">
          {t(request.form === 'PlanReview' ? 'workflowDetail.planApproval' : 'workflowDetail.waitingForInput')}
        </Badge>
        <Mono className="text-[11px] text-subtle" title={request.requestId}>
          {request.portId}
        </Mono>
      </div>

      {request.prompt != null && request.prompt.length > 0 && (
        <p className="mb-2.5 text-[13px] whitespace-pre-wrap">{request.prompt}</p>
      )}

      {(request.form === 'Text' || request.form === 'Json' || request.form === 'PlanReview') && (
        <TextArea
          rows={3}
          value={text}
          disabled={disabled}
          data-testid="workflow-answer"
          placeholder={
            request.form === 'PlanReview'
              ? t('workflowDetail.planRevisionPlaceholder')
              : request.form === 'Json'
                ? t('workflowDetail.jsonPlaceholder', {
                    type: request.responseType ?? t('workflowDetail.responseType'),
                  })
                : t('workflowDetail.answerPlaceholder')
          }
          className="mb-2.5"
          onChange={(event) => setText(event.target.value)}
        />
      )}

      <div className="flex flex-wrap gap-2">
        {request.form === 'PlanReview' && (
          <>
            <Button
              tone="primary"
              testId="workflow-approve"
              disabled={disabled}
              onClick={() => onRespond({ approved: true })}
            >
              {t('workflowDetail.approvePlan')}
            </Button>
            <Button
              disabled={disabled || text.trim().length === 0}
              title={text.trim().length === 0 ? t('workflowDetail.revisionRequired') : undefined}
              onClick={() => onRespond({ approved: false, text })}
            >
              {t('workflowDetail.sendBack')}
            </Button>
          </>
        )}

        {request.form === 'Boolean' && (
          <>
            <Button
              tone="primary"
              testId="workflow-approve"
              disabled={disabled}
              onClick={() => onRespond({ approved: true })}
            >
              {t('workflowDetail.yes')}
            </Button>
            <Button disabled={disabled} onClick={() => onRespond({ approved: false })}>
              {t('workflowDetail.no')}
            </Button>
          </>
        )}

        {request.form === 'Text' && (
          <Button
            tone="primary"
            testId="workflow-answer-send"
            disabled={disabled || text.trim().length === 0}
            onClick={() => onRespond({ text })}
          >
            {t('workflowDetail.sendAnswer')}
          </Button>
        )}

        {request.form === 'Json' && (
          <Button
            tone="primary"
            testId="workflow-answer-send"
            disabled={disabled || text.trim().length === 0}
            onClick={() => onRespond({ data: parseJson(text) })}
          >
            {t('workflowDetail.sendAnswer')}
          </Button>
        )}
      </div>
    </div>
  );
}

/**
 * Parses the JSON box, falling back to the raw string.
 *
 * The server decides whether the value fits the port's response type and
 * answers `400` with its own message when it does not; rejecting it here would
 * only duplicate that rule in a second place.
 */
function parseJson(text: string): unknown {
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

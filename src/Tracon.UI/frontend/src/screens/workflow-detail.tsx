import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { client, openStream, STREAM_IDLE_TIMEOUT_MS, unwrap } from '../lib/api';
import { readSse, SseIdleTimeoutError } from '@tracon/client';
import { Link, useNavigate, useSearchParams } from '../lib/router';
import { shortId } from '../lib/format';
import { useT, type MessageKey } from '../lib/i18n';
import { foldNodeStates } from '../lib/workflow-graph';
import {
  Badge,
  Button,
  CopyButton,
  Empty,
  ErrorNote,
  LinkButton,
  Loading,
  Mono,
  PageHeader,
  Panel,
  TextArea,
  TextInput,
} from '../components/ui';
import { Tooltip } from '../components/tooltip';
import { hasMissingRequiredParameter, ParameterInputs } from '../components/parameter-inputs';
import { SpinnerIcon } from '../components/icons';
import { WorkflowGraphLegend, WorkflowGraphView } from '../components/workflow-graph';
import { KIND_HINT } from './workflows';
import { WorkflowVersions } from './workflow-detail/versions';
import type { TraconMetaResponse as Meta } from '@tracon/client';
import type { RunEvent } from '../lib/run-event';
import type { RunRecord, WorkflowDescriptor, WorkflowGraph, WorkflowPendingRequest } from '../lib/server-types';

/**
 * One workflow: its graph, a place to run it, and the human decisions it is
 * waiting on.
 *
 * The graph is fetched compiled, so its node ids are exactly the executor ids
 * the run events carry. Colouring nodes while a run streams is therefore a map
 * lookup, not guesswork.
 *
 * The address names the run the screen is about (`?runId=`). A run page, a
 * bookmark or a reload can therefore open a run this screen did not start —
 * F-335: the request card used to show only for a run started right here, so
 * a run started over HTTP, by a job or in another tab could not be answered
 * from the console at all.
 */
export function WorkflowDetailScreen({ name, meta }: { name: string; meta: Meta }): ReactNode {
  const t = useT();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const workflow = useQuery({
    queryKey: ['workflow-descriptor', name],
    queryFn: async () => {
      const list = (await unwrap(client.GET('/api/workflows'))) as WorkflowDescriptor[];

      return list.find((entry) => entry.name === name) ?? null;
    },
  });

  const graph = useQuery({
    queryKey: ['workflow-graph', name],
    queryFn: () =>
      unwrap(
        client.GET('/api/workflows/{name}/graph', { params: { path: { name } } }),
      ) as Promise<WorkflowGraph>,
  });

  const linkedRunId = useSearchParams().get('runId');
  const [message, setMessage] = useState('');
  // The values for the workflow's own parameters (F-294); empty for a workflow
  // that declares none, in which case no `parameters` member is sent at all.
  const [paramValues, setParamValues] = useState<Record<string, string>>({});
  const [runId, setRunId] = useState<string | null>(linkedRunId);
  // True once this screen streamed `runId` itself. A run that came only from
  // the address has no events here and is not yet known to belong here.
  const [streamed, setStreamed] = useState(false);
  const [events, setEvents] = useState<RunEvent[]>([]);
  const [streaming, setStreaming] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const abort = useRef<AbortController | null>(null);

  useEffect(() => () => abort.current?.abort(), []);

  // Follows the address when something else changes it: a run page link, Back
  // or Forward. Only the address re-runs this; `runId` is read, not watched,
  // because the screen's own stream writes the same value there (`consume`)
  // and must not be pulled back by it.
  useEffect(() => {
    if (linkedRunId === runId) {
      return;
    }

    abort.current?.abort();
    setRunId(linkedRunId);
    setStreamed(false);
    setEvents([]);
    setError(null);
  }, [linkedRunId]);

  const states = useMemo(() => foldNodeStates(events), [events]);
  const finished = runId !== null && !streaming;
  const fromAddress = finished && !streamed;

  // A run that came only from the address is checked before its card shows:
  // the request list answers for ANY workflow run, and a card for another
  // workflow's run would send the answer to a graph the operator is not
  // looking at.
  const linkedRun = useQuery({
    queryKey: ['run', runId],
    queryFn: () =>
      unwrap(
        client.GET('/api/runs/{runId}', { params: { path: { runId: runId as string } } }),
      ) as Promise<RunRecord>,
    enabled: fromAddress,
  });
  const ownsRun =
    !fromAddress ||
    (linkedRun.isSuccess &&
      linkedRun.data.kind === 'Workflow' &&
      (linkedRun.data.workflowName ?? linkedRun.data.agentName) === name);

  // Pending requests are only meaningful once the stream has closed: while it
  // is open the run may still move past the port on its own.
  const pending = useQuery({
    queryKey: ['workflow-requests', runId],
    queryFn: () =>
      unwrap(
        client.GET('/api/workflows/runs/{runId}/requests', {
          params: { path: { runId: runId as string } },
        }),
      ) as Promise<WorkflowPendingRequest[]>,
    enabled: finished,
  });

  const consume = useCallback(
    async (response: Response) => {
      for await (const frame of readSse(response, { idleTimeoutMs: STREAM_IDLE_TIMEOUT_MS })) {
        if (frame.event === 'run') {
          const next = (JSON.parse(frame.data) as { runId: string }).runId;

          setRunId(next);
          setStreamed(true);
          // Written back so a reload, a copied link or Back keeps this run.
          // Replace, not push: the page is the same; only its run changed.
          navigate(`workflows/${encodeURIComponent(name)}?runId=${encodeURIComponent(next)}`, {
            replace: true,
            scroll: false,
          });

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
    [name, navigate, queryClient],
  );

  const parameterSchema = workflow.data?.parameters ?? [];
  const missingRequiredParameter = hasMissingRequiredParameter(parameterSchema, paramValues);

  const start = useCallback(async () => {
    // A run with a required value missing never starts on the server either
    // (the endpoint answers 400 before the stream opens); blocked here first.
    if (missingRequiredParameter) {
      return;
    }

    abort.current?.abort();

    const controller = new AbortController();

    abort.current = controller;
    setRunId(null);
    setStreamed(false);
    setEvents([]);
    setError(null);
    setStreaming(true);

    // The address stops naming the previous run; the new run's first frame
    // names the new one.
    if (linkedRunId !== null) {
      navigate(`workflows/${encodeURIComponent(name)}`, { replace: true, scroll: false });
    }

    try {
      const response = await openStream(`api/workflows/${encodeURIComponent(name)}/run`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          message: message.trim().length === 0 ? null : message,
          parameters: parameterSchema.length > 0 ? paramValues : undefined,
        }),
        signal: controller.signal,
      });

      await consume(response);
    } catch (caught) {
      if (caught instanceof SseIdleTimeoutError) {
        setError(new Error(t('common.streamLost')));
      } else if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
        setError(caught);
      }
    } finally {
      setStreaming(false);
    }
  }, [consume, linkedRunId, message, missingRequiredParameter, name, navigate, paramValues, parameterSchema.length]);

  /**
   * Answers a pending request and follows the run that continues from it.
   *
   * The answer opens a *new* run: history is append-only, so the original row
   * keeps its `AwaitingInput` status — and its request (K-130) — and this
   * screen, address included, switches to the new one.
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
        if (caught instanceof SseIdleTimeoutError) {
          // The console recognises this one, so it says it in the reader's
          // language — unlike a server message, which ErrorNote shows as-is.
          setError(new Error(t('common.streamLost')));
        } else if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
          setError(caught);
        }
      } finally {
        setStreaming(false);
      }
    },
    [consume],
  );

  if (workflow.isPending) {
    return (
      <>
        <PageHeader title={name} />
        <Panel>
          <Loading rows={8} />
        </Panel>
      </>
    );
  }

  if (workflow.isError) {
    return (
      <>
        <PageHeader title={name} />
        <Panel>
          <div className="p-4">
            <ErrorNote error={workflow.error} onRetry={() => void workflow.refetch()} />
          </div>
        </Panel>
      </>
    );
  }

  if (workflow.data === null) {
    return (
      <>
        <PageHeader title={name} />
        <Panel>
          <Empty
            title={t('workflowDetail.notFound')}
            action={
              <LinkButton to="workflows" tone="primary">
                {t('nav.workflows')}
              </LinkButton>
            }
          >
            {t('workflowDetail.notFoundBody')}
          </Empty>
        </Panel>
      </>
    );
  }

  const descriptor = workflow.data;
  const editable = descriptor.origin !== 'Code' && meta.roles.canAdminister;

  // A code-defined workflow is a free graph: no pattern, and nothing to edit
  // here. Its shape is only visible in the compiled graph below.
  const kindHintKey: MessageKey =
    descriptor.kind == null ? 'workflowDetail.codeGraphHint' : KIND_HINT[descriptor.kind];
  const kindHint = t(kindHintKey);

  // Why a run named by the address shows no card. A run this screen streamed
  // needs no note: its output is on the screen.
  const notice =
    !fromAddress || runId === null
      ? null
      : linkedRun.isSuccess && !ownsRun
        ? t('workflowDetail.linkedRun.otherWorkflow', { id: shortId(runId, 8, 4) })
        : ownsRun && pending.isSuccess && pending.data.length === 0
          ? t('workflowDetail.linkedRun.notWaiting', { id: shortId(runId, 8, 4) })
          : null;

  return (
    <>
      <PageHeader
        title={descriptor.displayName ?? descriptor.name}
        description={descriptor.description ?? kindHint}
        actions={
          <>
            <Badge tone={descriptor.kind == null ? 'neutral' : 'accent'} description={kindHint}>
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
          {/* The stream's own failure. Retrying means starting the run again,
              which is what the Run button below does — not a refetch. */}
          <ErrorNote error={error} onRetry={() => void start()} />
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
                <span className="inline-flex h-8 items-center rounded-md border border-line bg-raised pr-8 pl-3 text-base">
                  {t('workflowDetail.copyMermaid')}
                </span>
                <CopyButton value={graph.data.mermaid} />
              </span>
            )
          }
        >
          {graph.isPending && <Loading rows={6} />}
          {graph.isError && (
            <div className="p-4">
              <ErrorNote error={graph.error} onRetry={() => void graph.refetch()} />
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
              <ParameterInputs
                schema={parameterSchema}
                values={paramValues}
                onChange={setParamValues}
                title={t('workflowDetail.parameters')}
                testId="workflow-parameters"
                disabled={streaming}
              />

              <div className="flex items-end gap-2">
                <TextInput
                  value={message}
                  data-testid="workflow-message"
                  aria-label={t('workflowDetail.messagePlaceholder')}
                  placeholder={t('workflowDetail.messagePlaceholder')}
                  disabled={streaming}
                  onChange={(event) => setMessage(event.target.value)}
                />
                {streaming ? (
                  <Button onClick={() => abort.current?.abort()}>{t('workflowDetail.stop')}</Button>
                ) : (
                  <Button
                    tone="primary"
                    testId="workflow-run"
                    disabled={missingRequiredParameter}
                    onClick={() => void start()}
                  >
                    {t('common.run')}
                  </Button>
                )}
              </div>

              {runId !== null && (
                <p className="flex items-center gap-2 text-sm text-subtle">
                  {streaming && <SpinnerIcon className="size-3" />}
                  <Tooltip text={t('workflowDetail.inspectRun')}>
                    <Link to={`runs/${encodeURIComponent(runId)}`}>
                      <Mono>{t('workflowDetail.runId', { id: shortId(runId, 8, 4) })}</Mono>
                    </Link>
                  </Tooltip>
                  {/* A run that came from the address was not streamed here, so
                      a count of the events seen here would read as zero. */}
                  {streamed && <span>{t('workflowDetail.eventCount', { count: events.length })}</span>}
                </p>
              )}

              <Output events={events} />
            </div>
          </Panel>
        )}

        {fromAddress && linkedRun.isError && (
          <ErrorNote error={linkedRun.error} onRetry={() => void linkedRun.refetch()} />
        )}

        {notice !== null && runId !== null && (
          <div
            className="rounded-md border border-line bg-info-soft px-3 py-2 text-sm text-info"
            data-testid="workflow-run-notice"
          >
            {notice} <Link to={`runs/${encodeURIComponent(runId)}`}>{t('workflowDetail.linkedRun.openRun')}</Link>
          </div>
        )}

        {finished && ownsRun && pending.isError && (
          <ErrorNote error={pending.error} onRetry={() => void pending.refetch()} />
        )}

        {finished && ownsRun && pending.isSuccess && pending.data.length > 0 && (
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

      {/* Only a stored workflow keeps versions. One defined in code has no history, and the
          endpoint answers 404 for it - so the section is not offered at all. */}
      {descriptor.origin === 'Database' && (
        <WorkflowVersions
          name={name}
          currentVersion={descriptor.version}
          canRollback={meta.roles.canAdminister}
        />
      )}
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
          className="rounded-md border border-line bg-raised px-3 py-2 text-base whitespace-pre-wrap"
        >
          {event.text}
        </p>
      ))}

      {failures.map((event) => (
        <p
          key={event.sequence}
          className="rounded-md border border-line bg-danger-soft px-3 py-2 text-sm text-danger"
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
        <Mono className="text-xs text-subtle" copy={request.requestId}>
          {request.portId}
        </Mono>
      </div>

      {request.prompt != null && request.prompt.length > 0 && (
        <p className="mb-2.5 text-base whitespace-pre-wrap">{request.prompt}</p>
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
            {/* 🚨 `title` on a DISABLED control is never shown by any browser, so
                the one explanation that mattered — why this button cannot be
                pressed — was unreachable by construction. The tooltip wraps the
                control rather than living on it, so it shows either way. */}
            <Tooltip
              text={
                text.trim().length === 0
                  ? t('workflowDetail.revisionRequired')
                  : t('workflowDetail.sendBackHint')
              }
            >
              <Button
                disabled={disabled || text.trim().length === 0}
                onClick={() => onRespond({ approved: false, text })}
              >
                {t('workflowDetail.sendBack')}
              </Button>
            </Tooltip>
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

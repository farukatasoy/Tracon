import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { api, openStream } from '../lib/api';
import { readSse } from '../lib/sse';
import { emptyTranscript, foldUpdate, type TranscriptState } from '../lib/transcript';
import { Link, useNavigate } from '../lib/router';
import { count, shortId } from '../lib/format';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Select,
  cx,
} from '../components/ui';
import { PlusIcon, SendIcon, SpinnerIcon } from '../components/icons';
import { TranscriptView } from '../components/transcript';

interface Turn {
  id: string;
  /** Null for a turn that only carries an approval decision. */
  prompt: string | null;
  runId: string | null;
  transcript: TranscriptState;
  status: 'streaming' | 'done' | 'failed';
  error: string | null;
}

interface Decision {
  requestId: string;
  approved: boolean;
  remember: boolean;
}

/**
 * Streaming chat against a single agent.
 *
 * The stream comes from `POST api/agents/{name}/run` as Server-Sent Events. The
 * first frame is `run` and carries the run id, so the turn can link straight to
 * its recorded run while the answer is still arriving.
 */
export function PlaygroundScreen({ name }: { name?: string }): ReactNode {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const agents = useQuery({ queryKey: ['agents'], queryFn: api.agents });

  const [sessionId, setSessionId] = useState<string | null>(null);
  const [turns, setTurns] = useState<Turn[]>([]);
  const [prompt, setPrompt] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);

  const bottom = useRef<HTMLDivElement>(null);
  const abort = useRef<AbortController | null>(null);

  const selected = name ?? agents.data?.[0]?.name ?? '';

  useEffect(() => {
    bottom.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [turns]);

  useEffect(() => () => abort.current?.abort(), []);

  const reset = useCallback(() => {
    abort.current?.abort();
    setSessionId(null);
    setTurns([]);
    setError(null);
    setBusy(false);
  }, []);

  const run = useCallback(
    async (message: string | null, decision: Decision | null) => {
    if ((message === null && decision === null) || selected.length === 0 || busy) {
      return;
    }

    setBusy(true);
    setError(null);

    const turnId = `turn-${Date.now()}`;

    setTurns((current) => [
      ...current,
      { id: turnId, prompt: message, runId: null, transcript: emptyTranscript, status: 'streaming', error: null },
    ]);

    const update = (change: (turn: Turn) => Turn): void =>
      setTurns((current) => current.map((turn) => (turn.id === turnId ? change(turn) : turn)));

    try {
      // A conversation identifier is reserved on the server; conversation and
      // session are the same identity space (decision K-043), so this is also
      // the session the run will be recorded against.
      let conversation = sessionId;

      if (conversation === null) {
        conversation = (await api.createConversation()).id;
        setSessionId(conversation);
      }

      const controller = new AbortController();

      abort.current = controller;

      const response = await openStream(`api/agents/${encodeURIComponent(selected)}/run`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          message,
          sessionId: conversation,
          // An approval is the input of the next turn, not a resume signal:
          // Microsoft Agent Framework ends the run when a tool needs a decision
          // and expects the answer in the following request.
          approvals:
            decision === null
              ? []
              : [
                  {
                    requestId: decision.requestId,
                    approved: decision.approved,
                    remember: decision.remember,
                  },
                ],
        }),
        signal: controller.signal,
      });

      for await (const frame of readSse(response)) {
        if (frame.event === 'run') {
          const payload = JSON.parse(frame.data) as { runId: string };

          update((turn) => ({ ...turn, runId: payload.runId }));

          continue;
        }

        if (frame.event === 'update') {
          const payload = JSON.parse(frame.data) as { contents?: [] };

          update((turn) => ({ ...turn, transcript: foldUpdate(turn.transcript, payload) }));

          continue;
        }

        if (frame.event === 'error') {
          const payload = JSON.parse(frame.data) as { type: string; message: string };

          update((turn) => ({ ...turn, status: 'failed', error: `${payload.type}: ${payload.message}` }));

          continue;
        }

        if (frame.event === 'done') {
          update((turn) => (turn.status === 'failed' ? turn : { ...turn, status: 'done' }));
        }
      }

      update((turn) => (turn.status === 'streaming' ? { ...turn, status: 'done' } : turn));

      await queryClient.invalidateQueries({ queryKey: ['runs'] });
      await queryClient.invalidateQueries({ queryKey: ['sessions'] });

      if (decision?.remember === true) {
        await queryClient.invalidateQueries({ queryKey: ['approval-rules'] });
      }
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === 'AbortError') {
        update((turn) => ({ ...turn, status: 'done' }));
      } else {
        setError(caught);
        update((turn) => ({
          ...turn,
          status: 'failed',
          error: caught instanceof Error ? caught.message : String(caught),
        }));
      }
    } finally {
      abort.current = null;
      setBusy(false);
    }
  },
    [selected, busy, sessionId, queryClient],
  );

  const send = useCallback(() => {
    const message = prompt.trim();

    if (message.length === 0) {
      return;
    }

    setPrompt('');
    void run(message, null);
  }, [prompt, run]);

  /**
   * Answers a pending approval.
   *
   * The card is marked immediately so the same request cannot be answered
   * twice, then a new turn is started carrying the decision.
   */
  const decide = useCallback(
    (requestId: string, approved: boolean, remember: boolean) => {
      setTurns((current) =>
        current.map((turn) => ({
          ...turn,
          transcript: {
            ...turn.transcript,
            items: turn.transcript.items.map((item) =>
              item.kind === 'approval' && item.requestId === requestId
                ? { ...item, decided: approved ? ('approved' as const) : ('rejected' as const) }
                : item,
            ),
          },
        })),
      );

      void run(null, { requestId, approved, remember });
    },
    [run],
  );

  if (agents.isPending) {
    return <Loading />;
  }

  if (agents.isError) {
    return <ErrorNote error={agents.error} />;
  }

  if (agents.data.length === 0) {
    return (
      <>
        <PageHeader title="Playground" />
        <Panel>
          <Empty title="No agents to run">
            Create one on the <Link to="agents" className="text-accent underline">Agents</Link> screen
            first.
          </Empty>
        </Panel>
      </>
    );
  }

  return (
    <>
      <PageHeader
        title="Playground"
        description="Streamed test runs. Every turn is recorded and can be inspected event by event on the Runs screen."
        actions={
          <>
            <Select
              value={selected}
              onChange={(value) => {
                reset();
                navigate(`playground/${encodeURIComponent(value)}`);
              }}
            >
              {agents.data.map((agent) => (
                <option key={agent.name} value={agent.name}>
                  {agent.displayName ?? agent.name}
                </option>
              ))}
            </Select>
            <Button onClick={reset} disabled={turns.length === 0 && sessionId === null}>
              <PlusIcon className="size-3.5" />
              New chat
            </Button>
          </>
        }
      />

      {sessionId !== null && (
        <p className="mb-3 text-[12px] text-subtle">
          Session{' '}
          <Link to={`sessions/${encodeURIComponent(sessionId)}`} className="text-accent underline">
            <Mono>{shortId(sessionId, 14, 6)}</Mono>
          </Link>{' '}
          — history is carried across turns.
        </p>
      )}

      {error !== null && <div className="mb-3"><ErrorNote error={error} /></div>}

      <Panel className="flex min-h-[26rem] flex-col">
        <div className="flex-1 overflow-y-auto p-4">
          {turns.length === 0 ? (
            <Empty title="Send a message to start">
              The reply streams in token by token. Tool calls appear as cards with their
              arguments and results.
            </Empty>
          ) : (
            <div className="flex flex-col gap-6">
              {turns.map((turn) => (
                <TurnView key={turn.id} turn={turn} onDecide={decide} />
              ))}
            </div>
          )}
          <div ref={bottom} />
        </div>

        <form
          className="flex items-end gap-2 border-t border-line p-3"
          onSubmit={(event) => {
            event.preventDefault();
            send();
          }}
        >
          <textarea
            rows={1}
            value={prompt}
            disabled={busy}
            data-testid="playground-input"
            placeholder="Send a message…"
            className="max-h-40 min-h-9 flex-1 resize-y rounded-md border border-line bg-panel px-3 py-1.5 text-[13px] placeholder:text-subtle focus:border-accent focus:outline-none disabled:opacity-60"
            onChange={(event) => setPrompt(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && !event.shiftKey) {
                event.preventDefault();
                send();
              }
            }}
          />
          {busy ? (
            <Button tone="default" onClick={() => abort.current?.abort()}>
              Stop
            </Button>
          ) : (
            <Button
              type="submit"
              tone="primary"
              testId="playground-send"
              disabled={prompt.trim().length === 0}
            >
              <SendIcon className="size-3.5" />
              Send
            </Button>
          )}
        </form>
      </Panel>
    </>
  );
}

function TurnView({
  turn,
  onDecide,
}: {
  turn: Turn;
  onDecide: (requestId: string, approved: boolean, remember: boolean) => void;
}): ReactNode {
  const usage = turn.transcript.usage;

  return (
    <div data-testid="playground-turn">
      {turn.prompt !== null ? (
        <div className="mb-2.5 flex justify-end">
          <p className="max-w-[80%] rounded-lg rounded-br-sm bg-accent-soft px-3 py-2 text-[13px] whitespace-pre-wrap text-fg">
            {turn.prompt}
          </p>
        </div>
      ) : (
        <p className="mb-2.5 text-right text-[11px] text-subtle">approval decision sent</p>
      )}

      <div className="flex items-center gap-2 pb-1.5 text-[11px] text-subtle">
        {turn.status === 'streaming' && <SpinnerIcon className="size-3" />}
        <span>Assistant</span>
        {turn.runId !== null && (
          <Link
            to={`runs/${encodeURIComponent(turn.runId)}`}
            className="text-accent underline"
            title="Inspect this run event by event"
          >
            run {shortId(turn.runId, 8, 4)}
          </Link>
        )}
        {usage?.totalTokens != null && <span>{count(usage.totalTokens)} tokens</span>}
      </div>

      <div className={cx(turn.status === 'failed' && 'opacity-90')}>
        <TranscriptView
          items={turn.transcript.items}
          streaming={turn.status === 'streaming'}
          onDecide={turn.status === 'done' ? onDecide : undefined}
        />

        {turn.transcript.items.length === 0 && turn.status === 'streaming' && (
          <p className="text-[13px] text-subtle">…</p>
        )}

        {turn.error !== null && (
          <div className="mt-2 rounded-md border border-line bg-danger-soft px-3 py-2 text-[12px] text-danger">
            {turn.error}
          </div>
        )}

        {turn.status === 'failed' && turn.error === null && <Badge tone="danger">failed</Badge>}
      </div>
    </div>
  );
}

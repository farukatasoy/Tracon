import { useCallback, useEffect, useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { client, openStream, unwrap } from '../../lib/api';
import { readSse } from '../../lib/sse';
import {
  applyApprovalPresentations,
  emptyTranscript,
  foldMessages,
  foldUpdate,
  type ApprovalPresentationAnnouncement,
  type TranscriptState,
} from '../../lib/transcript';
import { useNavigate, useSearchParams } from '../../lib/router';
import type { ChatMessage, SessionDetailResponse } from '@agentprism/client';
import type { AgentDescriptor, AgentDetailResponse, AttachmentDescriptor, ConversationResource } from '../../lib/server-types';

export interface Turn {
  id: string;
  /** Null for a turn that only carries an approval decision. */
  prompt: string | null;
  attachments: AttachmentDescriptor[];
  runId: string | null;
  transcript: TranscriptState;
  status: 'streaming' | 'done' | 'failed';
  error: string | null;
}

export interface Decision {
  requestId: string;
  approved: boolean;
  remember: boolean;
}

/**
 * Owns a playground turn's whole life: the agent selection, the resumed or
 * fresh session, the SSE-driven turns, and the pending-attachment interplay
 * needed to send one.
 *
 * `attachments` is a narrow, injected seam — this hook never manages
 * attachment state itself, it only calls `take()` to grab what is pending
 * when a message is sent, and `reset()` when starting a new chat.
 *
 * `sessionId`/`setSessionId` are owned by the caller, not by this hook: the
 * session identifier is the one piece of state `useAttachments` also needs
 * (an upload is tagged with the session it belongs to), and hooks cannot
 * depend on each other's internal state without something above both of
 * them holding it.
 */
export function usePlaygroundRun(
  name: string | undefined,
  sessionId: string | null,
  setSessionId: (next: string | null) => void,
  attachments: { pending: AttachmentDescriptor[]; take: () => AttachmentDescriptor[]; reset: () => void },
) {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const agents = useQuery({
    queryKey: ['agents'],
    queryFn: () => unwrap(client.GET('/api/agents')) as Promise<AgentDescriptor[]>,
  });
  const urlSessionId = useSearchParams().get('sessionId');

  const [turns, setTurns] = useState<Turn[]>([]);
  const [history, setHistory] = useState<{ message: ChatMessage; folded: TranscriptState }[] | null>(null);
  const [prompt, setPrompt] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [conversation, setConversation] = useState(false);

  const bottomRef = useRef<HTMLDivElement>(null);
  const abort = useRef<AbortController | null>(null);

  const selected = name ?? agents.data?.[0]?.name ?? '';

  // The list endpoint (AgentDescriptor) carries no parameter schema; only the
  // detail endpoint's AgentDefinition does. Fetched separately so agents
  // without a schema (the overwhelming majority) pay no extra cost beyond
  // this one cheap, per-name-cached lookup.
  const agentDetail = useQuery({
    queryKey: ['agent-detail', selected],
    queryFn: () =>
      unwrap(client.GET('/api/agents/{name}', { params: { path: { name: selected } } })) as Promise<AgentDetailResponse>,
    enabled: selected.length > 0,
  });
  const parameterSchema = agentDetail.data?.definition?.parameters ?? [];

  const [paramValues, setParamValues] = useState<Record<string, string>>({});

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth', block: 'end' });
  }, [turns]);

  useEffect(() => () => abort.current?.abort(), []);

  /**
   * Loads an existing session named by `?sessionId=` in the address bar.
   *
   * HATA-S4-018: the screen used to ignore this parameter entirely and always
   * reserved a brand-new conversation on the first send, so there was no way
   * to resume a session from the UI (not from a link, not by pasting the URL
   * back). `sessionId` is set to the SAME identity the server already knows —
   * the next `run()` call appends to it instead of branching a new one. Prior
   * turns are not replayable as live `Turn`s (no `runId` per historical turn,
   * no clean way to regroup messages into turns without guessing) — they are
   * rendered as a read-only preface instead, the same fold `session-detail.tsx`
   * already uses for the same messages.
   */
  useEffect(() => {
    if (urlSessionId === null || urlSessionId === sessionId) {
      return;
    }

    let cancelled = false;

    void (
      unwrap(
        client.GET('/api/sessions/{sessionId}', { params: { path: { sessionId: urlSessionId } } }),
      ) as Promise<SessionDetailResponse>
    )
      .then((detail) => {
        if (cancelled) {
          return;
        }

        setSessionId(detail.id);

        const messages = (detail.messages as ChatMessage[] | null) ?? [];
        const folds = foldMessages(messages);

        setHistory(messages.map((message, index) => ({ message, folded: folds[index] ?? emptyTranscript })));
      })
      .catch((caught) => {
        if (!cancelled) {
          setError(caught);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [urlSessionId, sessionId]);

  const reset = useCallback(() => {
    abort.current?.abort();
    setSessionId(null);
    setTurns([]);
    setHistory(null);
    setError(null);
    setBusy(false);
    attachments.reset();
    setConversation(false);
    setParamValues({});

    // Otherwise a stale '?sessionId=' still in the address bar would re-hydrate
    // the very session 'new chat' just left, right back through the effect above.
    if (urlSessionId !== null) {
      navigate(`playground/${encodeURIComponent(selected)}`, { replace: true });
    }
  }, [urlSessionId, navigate, selected, attachments, setSessionId]);

  const run = useCallback(
    async (message: string | null, decision: Decision | null, runAttachments: AttachmentDescriptor[]) => {
      if ((message === null && decision === null) || selected.length === 0 || busy) {
        return;
      }

      setBusy(true);
      setError(null);

      const turnId = `turn-${Date.now()}`;

      setTurns((current) => [
        ...current,
        {
          id: turnId,
          prompt: message,
          attachments: runAttachments,
          runId: null,
          transcript: emptyTranscript,
          status: 'streaming',
          error: null,
        },
      ]);

      const update = (change: (turn: Turn) => Turn): void =>
        setTurns((current) => current.map((turn) => (turn.id === turnId ? change(turn) : turn)));

      try {
        // A conversation identifier is reserved on the server; conversation and
        // session are the same identity space (decision K-043), so this is also
        // the session the run will be recorded against.
        let conversationId = sessionId;

        if (conversationId === null) {
          const created = (await unwrap(client.POST('/v1/conversations'))) as ConversationResource;

          conversationId = created.id;
          setSessionId(conversationId);
        }

        const controller = new AbortController();

        abort.current = controller;

        const response = await openStream(`api/agents/${encodeURIComponent(selected)}/run`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            message,
            sessionId: conversationId,
            attachmentIds: runAttachments.map((attachment) => attachment.id),
            // Sent only when the agent declares a schema at all — omitting the
            // field entirely for the overwhelming majority of agents is the
            // exact same request shape as before this feature existed.
            parameters: parameterSchema.length > 0 ? paramValues : undefined,
            // An approval is the input of the next turn, not a resume signal:
            // Microsoft Agent Framework ends the run when a tool needs a decision
            // and expects the answer in the following request.
            approvals:
              decision === null
                ? []
                : [{ requestId: decision.requestId, approved: decision.approved, remember: decision.remember }],
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

          if (frame.event === 'approvals') {
            const payload = JSON.parse(frame.data) as ApprovalPresentationAnnouncement[];

            update((turn) => ({ ...turn, transcript: applyApprovalPresentations(turn.transcript, payload) }));

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
    [selected, busy, sessionId, setSessionId, queryClient, parameterSchema, paramValues],
  );

  // A run with a value missing for a required AgentParameter never starts on
  // the server either (AgentParameterGate); blocked here too so the person
  // running the agent sees why before spending a round trip on it.
  const missingRequiredParameter = parameterSchema.some(
    (parameter) =>
      parameter.required &&
      (paramValues[parameter.name] ?? '').trim().length === 0 &&
      (parameter.defaultValue ?? '').length === 0,
  );

  const send = useCallback(() => {
    const message = prompt.trim();

    if ((message.length === 0 && attachments.pending.length === 0) || missingRequiredParameter) {
      return;
    }

    setPrompt('');

    const taken = attachments.take();

    void run(message.length > 0 ? message : null, null, taken);
  }, [prompt, run, missingRequiredParameter, attachments]);

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

      void run(null, { requestId, approved, remember }, []);
    },
    [run],
  );

  /**
   * Turns conversation mode on.
   *
   * A session identifier is reserved first: the conversation socket runs
   * against ONE session for its whole life (the tenant and the session are
   * fixed at handshake time), so it cannot be opened before one exists.
   */
  const openConversation = useCallback(async () => {
    if (conversation) {
      setConversation(false);
      return;
    }

    try {
      if (sessionId === null) {
        const created = (await unwrap(client.POST('/v1/conversations'))) as ConversationResource;

        setSessionId(created.id);
      }

      setConversation(true);
    } catch (caught) {
      setError(caught);
    }
  }, [conversation, sessionId, setSessionId]);

  return {
    agents,
    selected,
    agentDetail,
    parameterSchema,
    paramValues,
    setParamValues,
    missingRequiredParameter,
    sessionId,
    turns,
    history,
    prompt,
    setPrompt,
    busy,
    error,
    conversation,
    bottomRef,
    abortRun: () => abort.current?.abort(),
    reset,
    send,
    decide,
    openConversation,
  };
}

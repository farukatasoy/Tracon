import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client as apiClient, unwrap } from '../lib/api';
import { useT } from '../lib/i18n';
import { Button, ErrorNote, Panel, TextArea } from './ui';
import { ThumbsDownIcon, ThumbsUpIcon } from './icons';
import type { JudgeRunResponse, RunScore } from '../lib/server-types';
import { Tooltip } from './tooltip';

/** The score name this control owns. Every other name is read-only here. */
const OVERALL = 'overall';

/**
 * Run-level "was this helpful" control: thumbs up/down plus an optional
 * comment (F-52, Phase 31).
 *
 * Since phase 152 a run can carry MORE THAN ONE score per author, told apart
 * by `name`. This control still owns exactly one of them — the run-level
 * `overall` score written by whoever is looking at the screen. Every other row
 * (a second human name, a judge score, a categorical label) is listed
 * read-only underneath.
 *
 * Message-level scoring and star ratings are part of the store contract
 * (`IRunScoreStore`, `RunScoreKind.Stars`) but are not exposed in this
 * control yet — see the phase's handoff notes for the open follow-up.
 */
export function FeedbackControl({ runId }: { runId: string }): ReactNode {
  const t = useT();
  const client = useQueryClient();
  const queryKey = ['run-feedback', runId];

  const feedback = useQuery({
    queryKey,
    queryFn: () =>
      unwrap(
        apiClient.GET('/api/runs/{runId}/feedback', { params: { path: { runId } } }),
      ) as Promise<RunScore[]>,
  });

  // Multiple authenticated reviewers each get their own row (one per
  // author); this control shows and edits only the run-level (no
  // `messageId`) score belonging to whoever is looking at the screen right
  // now, which the server resolves — the client never has to know who that is.
  //
  // 🚨 `source`, `name` AND `kind` are all required in the match. A judge score
  // also carries no `messageId`, so matching on that alone picked up the
  // judge's row as "mine" — the thumbs then rendered the judge's number and
  // Remove deleted the judge's score. Since phase 152 a human can hold several
  // names as well, so this control names the one it owns. `kind` joined the
  // match last: a Stars score may carry the SAME name as this control's Binary
  // one (the API does not forbid it, and `IRunScoreStore.ListAsync` documents
  // no order), and `mine` then bound to a value that can never equal 0 or 1 —
  // every click created a duplicate row instead of removing the existing one.
  const mine = feedback.data?.find((score) =>
    score.messageId == null &&
    score.source === 'human' &&
    score.name === OVERALL &&
    score.kind === 'Binary');

  // Everything the control does not own: a second human name, a categorical
  // label, a judge score. Read-only, so a name this screen cannot write is
  // still visible.
  const otherScores = (feedback.data ?? []).filter(
    (score) => score.messageId == null && !score.source.startsWith('judge:') && score !== mine,
  );

  const [comment, setComment] = useState(mine?.comment ?? '');

  // Re-syncs only when the underlying score identity or its saved comment
  // changes, not on every render.
  useEffect(() => {
    setComment(mine?.comment ?? '');
  }, [mine?.id, mine?.comment]);

  const rate = useMutation({
    mutationFn: (value: 0 | 1) =>
      unwrap(
        apiClient.POST('/api/runs/{runId}/feedback', {
          params: { path: { runId } },
          body: { name: OVERALL, kind: 'Binary', value, comment: comment.trim() || undefined },
        }),
      ) as Promise<RunScore>,
    onMutate: async (value) => {
      await client.cancelQueries({ queryKey });
      const previous = client.getQueryData<RunScore[]>(queryKey);
      const optimistic: RunScore = {
        id: mine?.id ?? 'optimistic',
        tenantId: mine?.tenantId ?? '',
        runId,
        messageId: null,
        name: OVERALL,
        kind: 'Binary',
        value,
        textValue: null,
        comment: comment.trim() || null,
        source: 'human',
        author: mine?.author ?? null,
        createdAt: new Date().toISOString(),
      };

      client.setQueryData<RunScore[]>(queryKey, (current) => [
        optimistic,
        ...(current ?? []).filter((score) => score.id !== optimistic.id),
      ]);

      return { previous };
    },
    onError: (_error, _value, context) => {
      if (context !== undefined) {
        client.setQueryData(queryKey, context.previous);
      }
    },
    onSettled: () => void client.invalidateQueries({ queryKey }),
  });

  const remove = useMutation({
    mutationFn: (scoreId: string) =>
      unwrap(
        apiClient.DELETE('/api/runs/{runId}/feedback/{scoreId}', {
          params: { path: { runId, scoreId } },
        }),
      ),
    onMutate: async (scoreId) => {
      await client.cancelQueries({ queryKey });
      const previous = client.getQueryData<RunScore[]>(queryKey);

      client.setQueryData<RunScore[]>(queryKey, (current) =>
        (current ?? []).filter((score) => score.id !== scoreId));

      return { previous };
    },
    onError: (_error, _scoreId, context) => {
      if (context !== undefined) {
        client.setQueryData(queryKey, context.previous);
      }
    },
    onSettled: () => void client.invalidateQueries({ queryKey }),
  });

  // Judge scores (Phase 49) share the same table and query as human feedback;
  // they are told apart by their `source` prefix (`judge:{name}`).
  const judgeScores = feedback.data?.filter((score) => score.source.startsWith('judge:')) ?? [];

  // 🚨 The asserted type has to be the one the endpoint answers with. This
  // said `RunScore[]` while the endpoint answers `{scores,failures}`, so
  // `data.length` was always `undefined`: the "no judge is configured" line
  // could never render and the button reported success by changing nothing on
  // screen. An assertion here NARROWS the generated shape (whose fields are
  // all optional); it must never name a different shape.
  const judgeNow = useMutation({
    mutationFn: () =>
      unwrap(
        apiClient.POST('/api/runs/{runId}/judge', { params: { path: { runId } } }),
      ) as Promise<JudgeRunResponse>,
    onSuccess: () => void client.invalidateQueries({ queryKey }),
  });

  const busy = rate.isPending || remove.isPending;

  const toggle = (value: 0 | 1): void => {
    if (mine?.value === value) {
      remove.mutate(mine.id);
    } else {
      rate.mutate(value);
    }
  };

  const saveComment = (): void => {
    if (mine != null && mine.value != null && comment !== (mine.comment ?? '')) {
      rate.mutate(mine.value as 0 | 1);
    }
  };

  return (
    <Panel title={t('feedback.title')}>
      <div className="flex flex-col gap-2 p-4">
        <div className="flex items-center gap-2">
          {/* 🚨 Icon-only, so the tooltip text is the ONLY name these carry —
              as a `title` it was the accessible name too, and `title` is the
              weakest possible source for one. `ariaLabel` names them and the
              tooltip shows the same words on hover and on focus. */}
          <Tooltip text={t('feedback.helpful')}>
            <Button
              tone={mine?.value === 1 ? 'primary' : 'default'}
              onClick={() => toggle(1)}
              disabled={busy}
              testId="feedback-up"
              ariaLabel={t('feedback.helpful')}
            >
              <ThumbsUpIcon className="size-3.5" />
            </Button>
          </Tooltip>
          <Tooltip text={t('feedback.notHelpful')}>
            <Button
              tone={mine?.value === 0 ? 'primary' : 'default'}
              onClick={() => toggle(0)}
              disabled={busy}
              testId="feedback-down"
              ariaLabel={t('feedback.notHelpful')}
            >
              <ThumbsDownIcon className="size-3.5" />
            </Button>
          </Tooltip>
          {mine != null && (
            <span className="text-xs text-subtle">
              {t('feedback.by', { author: mine.author ?? t('feedback.anonymous') })}
            </span>
          )}
        </div>

        <TextArea
          rows={2}
          placeholder={t('feedback.commentPlaceholder')}
          value={comment}
          onChange={(event) => setComment(event.target.value)}
          onBlur={saveComment}
          disabled={busy}
          data-testid="feedback-comment"
        />

        {rate.isError && (
          <ErrorNote
            error={rate.error}
            onRetry={rate.variables === undefined ? undefined : () => rate.mutate(rate.variables)}
          />
        )}

        {otherScores.length > 0 && (
          <div className="mt-2 flex flex-col gap-1 border-t border-line pt-2">
            <span className="text-xs font-semibold text-body">{t('feedback.otherScores')}</span>
            {otherScores.map((score) => (
              <div key={score.id} className="flex items-center gap-2 text-xs text-subtle">
                <span className="font-semibold text-body">{score.name}</span>
                <span>{score.kind === 'Categorical' ? score.textValue : formatScoreValue(score.value)}</span>
                {score.comment != null && <span>· {score.comment}</span>}
              </div>
            ))}
          </div>
        )}

        <div className="mt-2 flex flex-col gap-1 border-t border-line pt-2">
          {judgeScores.map((score) => (
            <div key={score.id} className="flex items-center gap-2 text-xs text-subtle">
              <span className="font-semibold text-body">{t('onlineEval.judgeScoreLabel')}</span>
              <span>{score.source.replace('judge:', '')}</span>
              <span className="font-semibold text-body">{formatScoreValue(score.value)}/100</span>
              {score.comment != null && <span>· {score.comment}</span>}
            </div>
          ))}

          <div className="flex items-center gap-2">
            <Button tone="default" onClick={() => judgeNow.mutate()} disabled={judgeNow.isPending} testId="judge-now">
              {t('onlineEval.judgeButton')}
            </Button>
            {judgeNow.isSuccess && judgeNow.data.scores.length === 0 && (
              <span className="text-xs text-subtle">{t('onlineEval.judgeNoJudges')}</span>
            )}
          </div>

          {judgeNow.isError && <ErrorNote error={judgeNow.error} onRetry={() => judgeNow.mutate()} />}
        </div>
      </div>
    </Panel>
  );
}

/**
 * Renders a numeric score. `null` means NO MEASUREMENT was made, not zero
 * (phase 152), so it renders as a dash rather than a 0 nobody wrote. A whole
 * number keeps its short form; a decimal keeps at most two places.
 */
function formatScoreValue(value: number | null | undefined): string {
  if (value == null) {
    return '—';
  }

  return Number.isInteger(value) ? String(value) : value.toFixed(2);
}

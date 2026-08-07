import { useEffect, useState, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { useT } from '../lib/i18n';
import { Button, ErrorNote, Panel, TextArea } from './ui';
import { ThumbsDownIcon, ThumbsUpIcon } from './icons';
import type { RunScore } from '../lib/types';

/**
 * Run-level "was this helpful" control: thumbs up/down plus an optional
 * comment (F-52, Faz 31).
 *
 * Message-level scoring and star ratings are part of the store contract
 * (`IRunScoreStore`, `RunScoreKind.Stars`) but are not exposed in this
 * control yet — see the phase's handoff notes for the open follow-up.
 */
export function FeedbackControl({ runId }: { runId: string }): ReactNode {
  const t = useT();
  const client = useQueryClient();
  const queryKey = ['run-feedback', runId];

  const feedback = useQuery({ queryKey, queryFn: () => api.runFeedback(runId) });

  // Multiple authenticated reviewers each get their own row (one per
  // author); this control shows and edits only the run-level (no
  // `messageId`) score belonging to whoever is looking at the screen right
  // now, which the server resolves — the client never has to know who that is.
  const mine = feedback.data?.find((score) => score.messageId == null);

  const [comment, setComment] = useState(mine?.comment ?? '');

  // Re-syncs only when the underlying score identity or its saved comment
  // changes, not on every render.
  useEffect(() => {
    setComment(mine?.comment ?? '');
  }, [mine?.id, mine?.comment]);

  const rate = useMutation({
    mutationFn: (value: 0 | 1) =>
      api.saveRunFeedback(runId, { kind: 'Binary', value, comment: comment.trim() || undefined }),
    onMutate: async (value) => {
      await client.cancelQueries({ queryKey });
      const previous = client.getQueryData<RunScore[]>(queryKey);
      const optimistic: RunScore = {
        id: mine?.id ?? 'optimistic',
        tenantId: mine?.tenantId ?? '',
        runId,
        messageId: null,
        kind: 'Binary',
        value,
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
    mutationFn: (scoreId: string) => api.deleteRunFeedback(runId, scoreId),
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

  // Judge scores (Faz 49) share the same table and query as human feedback;
  // they are told apart by their `source` prefix (`judge:{name}`).
  const judgeScores = feedback.data?.filter((score) => score.source.startsWith('judge:')) ?? [];

  const judgeNow = useMutation({
    mutationFn: () => api.judgeRun(runId),
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
    if (mine != null && comment !== (mine.comment ?? '')) {
      rate.mutate(mine.value as 0 | 1);
    }
  };

  return (
    <Panel title={t('feedback.title')}>
      <div className="flex flex-col gap-2 p-4">
        <div className="flex items-center gap-2">
          <Button
            tone={mine?.value === 1 ? 'primary' : 'default'}
            onClick={() => toggle(1)}
            disabled={busy}
            testId="feedback-up"
            title={t('feedback.helpful')}
          >
            <ThumbsUpIcon className="size-3.5" />
          </Button>
          <Button
            tone={mine?.value === 0 ? 'primary' : 'default'}
            onClick={() => toggle(0)}
            disabled={busy}
            testId="feedback-down"
            title={t('feedback.notHelpful')}
          >
            <ThumbsDownIcon className="size-3.5" />
          </Button>
          {mine != null && (
            <span className="text-[11px] text-subtle">
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

        {rate.isError && <ErrorNote error={rate.error} />}

        <div className="mt-2 flex flex-col gap-1 border-t border-line pt-2">
          {judgeScores.map((score) => (
            <div key={score.id} className="flex items-center gap-2 text-[11px] text-subtle">
              <span className="font-semibold text-body">{t('onlineEval.judgeScoreLabel')}</span>
              <span>{score.source.replace('judge:', '')}</span>
              <span className="font-semibold text-body">{score.value}/100</span>
              {score.comment != null && <span>· {score.comment}</span>}
            </div>
          ))}

          <div className="flex items-center gap-2">
            <Button tone="default" onClick={() => judgeNow.mutate()} disabled={judgeNow.isPending} testId="judge-now">
              {t('onlineEval.judgeButton')}
            </Button>
            {judgeNow.isSuccess && judgeNow.data.length === 0 && (
              <span className="text-[11px] text-subtle">{t('onlineEval.judgeNoJudges')}</span>
            )}
          </div>

          {judgeNow.isError && <ErrorNote error={judgeNow.error} />}
        </div>
      </div>
    </Panel>
  );
}

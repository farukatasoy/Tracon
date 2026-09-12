import type { ReactNode } from 'react';
import { useT } from '../../lib/i18n';
import { count, shortId } from '../../lib/format';
import { Link } from '../../lib/router';
import { Badge, cx } from '../../components/ui';
import { SpinnerIcon } from '../../components/icons';
import { TranscriptView } from '../../components/transcript';
import { AttachmentChip } from './attachment-chip';
import { SpeakButton } from './speak-button';
import type { Turn } from './use-playground-run';
import { Tooltip } from '../../components/tooltip';

export function TurnView({
  turn,
  onDecide,
  sessionId,
}: {
  turn: Turn;
  onDecide: (requestId: string, approved: boolean, remember: boolean) => void;
  sessionId: string | null;
}): ReactNode {
  const t = useT();
  const usage = turn.transcript.usage;

  /** The assistant's plain text, which is what "speak" would read out. */
  const spokenText = turn.transcript.items
    .filter((item) => item.kind === 'text')
    .map((item) => item.text)
    .join('\n')
    .trim();

  return (
    <div data-testid="playground-turn">
      {turn.attachments.length > 0 && (
        <div className="mb-2 flex flex-wrap justify-end gap-1.5">
          {turn.attachments.map((attachment) => (
            <AttachmentChip key={attachment.id} attachment={attachment} />
          ))}
        </div>
      )}

      {turn.prompt !== null ? (
        <div className="mb-2.5 flex justify-end">
          <p className="max-w-[80%] rounded-lg rounded-br-sm bg-accent-soft px-3 py-2 text-base whitespace-pre-wrap text-fg">
            {turn.prompt}
          </p>
        </div>
      ) : (
        <p className="mb-2.5 text-right text-xs text-subtle">{t('playground.approvalSent')}</p>
      )}

      <div className="flex items-center gap-2 pb-1.5 text-xs text-subtle">
        {turn.status === 'streaming' && <SpinnerIcon className="size-3" />}
        <span>{t('playground.assistant')}</span>
        {turn.runId !== null && (
          <Tooltip text={t('workflowDetail.inspectRun')}>
            <Link to={`runs/${encodeURIComponent(turn.runId)}`}>
              {t('workflowDetail.runId', { id: shortId(turn.runId, 8, 4) })}
            </Link>
          </Tooltip>
        )}
        {usage?.totalTokens != null && <span>{t('settings.modelTokens', { tokens: count(usage.totalTokens) })}</span>}
      </div>

      {/* The reply arrives token by token over SSE. `polite` lets a screen
          reader finish the current sentence before announcing the update. */}
      <div aria-live="polite" aria-busy={turn.status === 'streaming'} className={cx(turn.status === 'failed' && 'opacity-90')}>
        <TranscriptView
          items={turn.transcript.items}
          streaming={turn.status === 'streaming'}
          onDecide={turn.status === 'done' ? onDecide : undefined}
        />

        {turn.transcript.items.length === 0 && turn.status === 'streaming' && (
          <p className="text-base text-subtle">…</p>
        )}

        {turn.error !== null && (
          <div className="mt-2 rounded-md border border-line bg-danger-soft px-3 py-2 text-sm text-danger">
            {turn.error}
          </div>
        )}

        {turn.status === 'failed' && turn.error === null && <Badge tone="danger">{t('runs.status.failed')}</Badge>}

        {turn.status === 'done' && spokenText.length > 0 && <SpeakButton text={spokenText} sessionId={sessionId} />}
      </div>
    </div>
  );
}

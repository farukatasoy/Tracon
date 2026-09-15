import { type ReactNode } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { client, unwrap, TraconError } from '../lib/api';
import { useT } from '../lib/i18n';
import { Button } from './ui';
import { Tooltip } from './tooltip';
import type { RunRecord } from '../lib/server-types';

/**
 * "Cancel run" action for the run detail screen (F-35, Phase 32).
 *
 * Only rendered while the run is `Running`. A 202 means cancellation was only
 * REQUESTED — `cts.Cancel()` on the server is not a guarantee. The eventual
 * `Canceled` status is picked up by the run screen's own polling
 * (`refetchInterval` on the `run` query); this button does not assert it.
 */
export function CancelRunButton({ runId }: { runId: string }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();

  const cancel = useMutation({
    mutationFn: () =>
      unwrap(
        client.POST('/api/runs/{runId}/cancel', { params: { path: { runId } } }),
      ) as Promise<RunRecord>,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['run', runId] }),
  });

  if (cancel.isSuccess) {
    return <span className="text-sm text-subtle">{t('runDetail.cancel.requested')}</span>;
  }

  return (
    <div className="flex flex-col items-end gap-1">
      {/* No confirmation step: §175.3 fails on both counts. Cancelling cannot
          be undone, but it destroys nothing — the recorded work stays and the
          agent runs again. Layer 1 states that at the moment of deciding. */}
      <Tooltip text={t('runDetail.cancel.effect')}>
        <Button
          tone="danger"
          busy={cancel.isPending}
          testId="cancel-run"
          onClick={() => cancel.mutate()}
        >
          {t('runDetail.cancel.button')}
        </Button>
      </Tooltip>

      {cancel.isError && (
        <span className="text-xs text-danger" role="alert">
          {cancel.error instanceof TraconError && cancel.error.status === 409
            ? t('runDetail.cancel.conflict')
            : cancel.error.message}
        </span>
      )}
    </div>
  );
}

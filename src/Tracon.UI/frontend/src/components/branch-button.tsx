import type { ReactNode } from 'react';
import { useMutation } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import type { SessionBranchResult } from '../lib/server-types';
import { useT } from '../lib/i18n';
import { useNavigate } from '../lib/router';
import { Button, ErrorNote } from './ui';

/**
 * Branches a session's conversation at one message and opens the branch
 * (F-66, phase 47).
 *
 * `upToSequence` is the message's own index. That index is exact here and only
 * here: `GET /api/sessions/{id}` reads the history through the registered
 * `ChatHistoryProvider`, which returns `conversation_items` ordered by `seq`,
 * so the i-th rendered message is `seq = i`. The playground's transcript is
 * folded from a live SSE stream instead and carries no sequence numbers, which
 * is why per-message branching lives on this screen.
 *
 * A 501 means the deployment has no SQL provider — history then lives inside
 * the opaque session state and cannot be copied up to a point. The server's own
 * explanation is shown verbatim (K-232).
 */
export function BranchButton({
  sessionId,
  upToSequence,
}: {
  sessionId: string;
  upToSequence?: number;
}): ReactNode {
  const t = useT();
  const navigate = useNavigate();

  const branch = useMutation({
    mutationFn: () =>
      unwrap(
        client.POST('/api/sessions/{sessionId}/branch', {
          params: { path: { sessionId } },
          body: { upToSequence: upToSequence ?? null },
        }),
      ) as Promise<SessionBranchResult>,
    onSuccess: (result) => navigate(`sessions/${encodeURIComponent(result.sessionId)}`),
  });

  return (
    <>
      <Button
        tone="ghost"
        onClick={() => branch.mutate()}
        busy={branch.isPending}
        title={t('branch.hint')}
        testId={upToSequence === undefined ? 'branch-session' : `branch-at-${upToSequence}`}
      >
        {branch.isPending ? t('branch.running') : t('branch.title')}
      </Button>

      {branch.isError && <ErrorNote error={branch.error} />}
    </>
  );
}

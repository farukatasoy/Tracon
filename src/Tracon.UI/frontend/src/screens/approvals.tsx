import type { ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { useT } from '../lib/i18n';
import { Link } from '../lib/router';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
  Table,
  Td,
  Th,
  Unauthorized,
} from '../components/ui';
import { Tooltip } from '../components/tooltip';
import { StatusDot } from '../components/status-dot';
import { ThumbsDownIcon, ThumbsUpIcon } from '../components/icons';
import type { TraconMetaResponse as Meta, PendingApproval } from '@tracon/client';

/**
 * Pending tool-approval requests for queued agent runs (phase 55).
 *
 * A request here is a projection, not the source of truth: the run's own
 * session state owns the pending question. Deciding it does not resume the
 * *same* run — the run row that asked stays `AwaitingApproval` forever
 * (append-only, decision K-014); a new run opens with the answer.
 *
 * 🚨 This is the console's decision surface and a decision here cannot be taken
 * back, so the row states the consequence before it is taken: the arguments the
 * tool was called with are on the row itself, and each button carries a real
 * tooltip — reachable by keyboard and on touch, unlike the `title` attribute it
 * replaced — naming what the answer sets in motion.
 */
export function ApprovalsScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const queryClient = useQueryClient();

  const approvals = useQuery({
    queryKey: ['approvals-pending'],
    queryFn: () => unwrap(client.GET('/api/approvals/pending')) as Promise<PendingApproval[]>,
    refetchInterval: 5_000,
  });

  const decide = useMutation({
    mutationFn: ({ id, approved }: { id: string; approved: boolean }) =>
      unwrap(
        client.POST('/api/approvals/{id}/decide', { params: { path: { id } }, body: { approved } }),
      ) as Promise<PendingApproval>,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['approvals-pending'] }),
  });

  const waiting = approvals.data?.length ?? 0;

  return (
    <>
      <PageHeader
        title={t('approvals.title')}
        description={t('approvals.description')}
        actions={
          approvals.isSuccess && waiting > 0 ? (
            <Badge tone="warn">
              <StatusDot tone="warn" live />
              {t('approvals.waiting', { count: waiting })}
            </Badge>
          ) : undefined
        }
      />

      <Panel>
        {approvals.isPending && <Loading rows={3} />}
        {approvals.isError && (
          <div className="p-3">
            <ErrorNote error={approvals.error} onRetry={() => void approvals.refetch()} />
          </div>
        )}

        {approvals.isSuccess &&
          (waiting === 0 ? (
            // An empty approval queue is the DESIRED state, so this one has no
            // "create the first" action: there is nothing an operator should do
            // to make a tool call ask for permission.
            <Empty title={t('approvals.empty.title')}>{t('approvals.empty.body')}</Empty>
          ) : (
            <Table label={t('approvals.title')}>
              <thead>
                <tr>
                  <Th>{t('approvals.tool')}</Th>
                  <Th>{t('approvals.run')}</Th>
                  <Th>{t('common.created')}</Th>
                  <Th>{t('approvals.expiresAt')}</Th>
                  <Th className="text-right">{t('common.actions')}</Th>
                </tr>
              </thead>
              <tbody>
                {(approvals.data ?? []).map((approval) => {
                  const approving =
                    decide.isPending && decide.variables?.id === approval.id && decide.variables.approved;
                  const rejecting =
                    decide.isPending && decide.variables?.id === approval.id && !decide.variables.approved;

                  return (
                  <tr key={approval.id} className="focus-within:bg-raised hover:bg-raised">
                    <Td>
                      {approval.presentation?.entityName == null ? (
                        <Mono className="font-semibold">{approval.toolName}</Mono>
                      ) : (
                        <>
                          <p className="font-semibold">{approval.presentation.entityName}</p>
                          <Mono className="text-subtle">{approval.toolName}</Mono>
                        </>
                      )}
                      {approval.presentation?.message != null && (
                        <p className="mt-0.5 max-w-sm text-sm text-subtle">{approval.presentation.message}</p>
                      )}
                      {approval.arguments != null && approval.arguments.length > 0 && (
                        <details className="mt-1">
                          <summary className="cursor-pointer text-xs text-subtle">
                            {t('approvals.rawArguments')}
                          </summary>
                          <p className="mt-1 max-w-sm rounded border border-line bg-raised p-2 font-mono text-id break-all text-muted">
                            {approval.arguments}
                          </p>
                        </details>
                      )}
                    </Td>
                    <Td>
                      <Link to={`runs/${encodeURIComponent(approval.runId)}`}>
                        <Mono title={approval.runId}>{shortId(approval.runId, 13, 6)}</Mono>
                      </Link>
                      <p className="mt-0.5">
                        <Mono className="text-subtle">{shortId(approval.sessionId, 13, 6)}</Mono>
                      </p>
                    </Td>
                    <Td className="text-sm text-muted" title={absoluteTime(approval.createdAt)}>
                      {relativeTime(approval.createdAt)}
                    </Td>
                    <Td className="font-mono text-sm text-warn">
                      {/* relativeTime() is "ago"-only (it treats any future
                          instant as clock skew and prints "just now"); an
                          expiry is always in the future, so this shows the
                          absolute time directly instead of a misleading one. */}
                      {absoluteTime(approval.expiresAt)}
                    </Td>
                    <Td className="text-right">
                      {meta.roles.canOperate ? (
                        <div className="flex justify-end gap-1.5">
                          <Tooltip text={t('approvals.approveTitle')}>
                            <Button
                              tone="primary"
                              busy={approving}
                              disabled={decide.isPending && !approving}
                              testId={`approve-${approval.id}`}
                              onClick={() => decide.mutate({ id: approval.id, approved: true })}
                            >
                              <ThumbsUpIcon className="size-3.5" />
                              {t('approvals.approve')}
                            </Button>
                          </Tooltip>
                          <Tooltip text={t('approvals.rejectTitle')}>
                            <Button
                              tone="danger"
                              busy={rejecting}
                              disabled={decide.isPending && !rejecting}
                              testId={`reject-${approval.id}`}
                              onClick={() => decide.mutate({ id: approval.id, approved: false })}
                            >
                              <ThumbsDownIcon className="size-3.5" />
                              {t('approvals.reject')}
                            </Button>
                          </Tooltip>
                        </div>
                      ) : (
                        <Badge tone="warn">{t('approvals.status.pending')}</Badge>
                      )}
                    </Td>
                  </tr>
                  );
                })}
              </tbody>
            </Table>
          ))}

        {approvals.isSuccess && waiting > 0 && !meta.roles.canOperate && (
          <div className="border-t border-line">
            <Unauthorized requires="operator" />
          </div>
        )}
      </Panel>

      {decide.isError && (
        <div className="mt-3">
          {/* No retry. Approving or rejecting a tool call is a decision, and a
              generic "try again" next to a failed one would re-submit a verdict
              the operator can no longer see the arguments for. The row is still
              in the list with both buttons on it. */}
          <ErrorNote error={decide.error} />
        </div>
      )}
    </>
  );
}

import type { ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../lib/api';
import { absoluteTime, relativeTime, shortId } from '../lib/format';
import { useT } from '../lib/i18n';
import { Link } from '../lib/router';
import { Badge, Button, Empty, ErrorNote, Loading, Mono, PageHeader, Panel, Table, Td, Th } from '../components/ui';
import { ThumbsDownIcon, ThumbsUpIcon } from '../components/icons';
import type { Meta } from '../lib/types';

/**
 * Pending tool-approval requests for queued agent runs (phase 55).
 *
 * A request here is a projection, not the source of truth: the run's own
 * session state owns the pending question. Deciding it does not resume the
 * *same* run — the run row that asked stays `AwaitingApproval` forever
 * (append-only, decision K-014); a new run opens with the answer.
 */
export function ApprovalsScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const client = useQueryClient();

  const approvals = useQuery({
    queryKey: ['approvals-pending'],
    queryFn: api.pendingApprovals,
    refetchInterval: 5_000,
  });

  const decide = useMutation({
    mutationFn: ({ id, approved }: { id: string; approved: boolean }) => api.decideApproval(id, approved),
    onSuccess: () => void client.invalidateQueries({ queryKey: ['approvals-pending'] }),
  });

  return (
    <>
      <PageHeader title={t('approvals.title')} description={t('approvals.description')} />

      <Panel>
        {approvals.isPending && <Loading />}
        {approvals.isError && <ErrorNote error={approvals.error} />}

        {approvals.isSuccess &&
          (approvals.data.length === 0 ? (
            <Empty title={t('approvals.empty.title')}>{t('approvals.empty.body')}</Empty>
          ) : (
            <Table>
              <thead>
                <tr>
                  <Th>{t('approvals.tool')}</Th>
                  <Th>{t('approvals.run')}</Th>
                  <Th>{t('common.created')}</Th>
                  <Th>{t('approvals.expiresAt')}</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {approvals.data.map((approval) => {
                  const approving =
                    decide.isPending && decide.variables?.id === approval.id && decide.variables.approved;
                  const rejecting =
                    decide.isPending && decide.variables?.id === approval.id && !decide.variables.approved;

                  return (
                    <tr key={approval.id}>
                      <Td>
                        <Mono className="font-semibold">{approval.toolName}</Mono>
                        {approval.arguments != null && approval.arguments.length > 0 && (
                          <p className="mt-0.5 max-w-sm truncate text-[11px] text-subtle" title={approval.arguments}>
                            {approval.arguments}
                          </p>
                        )}
                      </Td>
                      <Td>
                        <Link to={`runs/${encodeURIComponent(approval.runId)}`}>
                          <Mono title={approval.runId}>{shortId(approval.runId, 13, 6)}</Mono>
                        </Link>
                        <p className="mt-0.5 text-[11px] text-subtle">{approval.sessionId}</p>
                      </Td>
                      <Td className="text-[11px] text-muted" title={absoluteTime(approval.createdAt)}>
                        {relativeTime(approval.createdAt)}
                      </Td>
                      <Td className="text-[11px] text-muted">
                        {/* relativeTime() is "ago"-only (it treats any future
                            instant as clock skew and prints "just now"); an
                            expiry is always in the future, so this shows the
                            absolute time directly instead of a misleading one. */}
                        {absoluteTime(approval.expiresAt)}
                      </Td>
                      <Td className="text-right">
                        {meta.roles.canOperate ? (
                          <div className="flex justify-end gap-1.5">
                            <Button
                              tone="primary"
                              busy={approving}
                              disabled={decide.isPending && !approving}
                              onClick={() => decide.mutate({ id: approval.id, approved: true })}
                              title={t('approvals.approveTitle')}
                            >
                              <ThumbsUpIcon className="size-3.5" />
                              {t('approvals.approve')}
                            </Button>
                            <Button
                              tone="danger"
                              busy={rejecting}
                              disabled={decide.isPending && !rejecting}
                              onClick={() => decide.mutate({ id: approval.id, approved: false })}
                              title={t('approvals.rejectTitle')}
                            >
                              <ThumbsDownIcon className="size-3.5" />
                              {t('approvals.reject')}
                            </Button>
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

        {decide.isError && (
          <div className="border-t border-line p-3">
            <ErrorNote error={decide.error} />
          </div>
        )}
      </Panel>
    </>
  );
}

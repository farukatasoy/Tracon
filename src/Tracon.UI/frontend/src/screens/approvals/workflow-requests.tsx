import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../../lib/api';
import { absoluteTime, relativeTime, shortId } from '../../lib/format';
import { useT } from '../../lib/i18n';
import { Link } from '../../lib/router';
import {
  Badge,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  Pager,
  Panel,
  Select,
  Table,
  Td,
  Th,
} from '../../components/ui';
import { Toolbar, ToolbarField } from '../../components/toolbar';
import { StatusDot } from '../../components/status-dot';
import { Tooltip } from '../../components/tooltip';
import type { WorkflowDescriptor, WorkflowPendingRequest } from '../../lib/server-types';

const PAGE_SIZE = 25;

/** The longest prompt the row shows; the run's card shows all of it. */
const PROMPT_PREVIEW = 140;

type RequestState = 'pending' | 'answered' | 'all';

/**
 * The human requests the tenant's workflow runs wait on, across every workflow.
 *
 * A row is a pointer, not a form: it names the workflow, shows the start of the request and when
 * it runs out, and opens the run's card, where the whole request is shown and answered. The
 * answering controls live on that one card (`WorkflowDetailScreen`), so a form that must stay in
 * step with a request type exists once.
 *
 * 🚨 An answered run stays `AwaitingInput`, so an answered request is still a row here,
 * marked answered: the run it was answered on is named, and the run that waits is the one whose
 * rows retention keeps until the request expires. The default filter hides the answered ones.
 */
export function WorkflowRequestsPanel(): ReactNode {
  const t = useT();
  const [state, setState] = useState<RequestState>('pending');
  const [workflowName, setWorkflowName] = useState('');
  const [page, setPage] = useState(0);

  const workflows = useQuery({
    queryKey: ['workflows'],
    queryFn: () => unwrap(client.GET('/api/workflows')) as Promise<WorkflowDescriptor[]>,
  });

  const requests = useQuery({
    queryKey: ['workflow-inbox', state, workflowName, page],
    queryFn: () =>
      unwrap(
        client.GET('/api/workflows/requests', {
          params: {
            query: {
              state,
              workflowName: workflowName.length > 0 ? workflowName : undefined,
              skip: page * PAGE_SIZE,
              take: PAGE_SIZE,
            },
          },
        }),
      ) as Promise<WorkflowPendingRequest[]>,
    // A request is answered, or runs out, on its own schedule; the list follows without a reload.
    refetchInterval: 5_000,
  });

  const filtered = state !== 'pending' || workflowName.length > 0;

  return (
    <div data-testid="workflow-requests">
      <Panel title={t('approvals.workflowRequests.title')}>
        <p className="px-4 pt-3 text-sm text-muted">{t('approvals.workflowRequests.description')}</p>

        <div className="px-4 pt-3">
          <Toolbar
            onReset={
              filtered
                ? () => {
                    setState('pending');
                    setWorkflowName('');
                    setPage(0);
                  }
                : undefined
            }
          >
            <ToolbarField label={t('approvals.workflowRequests.show')}>
              {(id) => (
                <Select
                  id={id}
                  testId="workflow-requests-state"
                  value={state}
                  onChange={(value) => {
                    setState(value as RequestState);
                    setPage(0);
                  }}
                >
                  <option value="pending">{t('approvals.workflowRequests.showPending')}</option>
                  <option value="answered">{t('approvals.workflowRequests.showAnswered')}</option>
                  <option value="all">{t('approvals.workflowRequests.showAll')}</option>
                </Select>
              )}
            </ToolbarField>

            <ToolbarField label={t('workflows.column.workflow')}>
              {(id) => (
                <Select
                  id={id}
                  testId="workflow-requests-workflow"
                  value={workflowName}
                  onChange={(value) => {
                    setWorkflowName(value);
                    setPage(0);
                  }}
                >
                  <option value="">{t('approvals.workflowRequests.allWorkflows')}</option>
                  {(workflows.data ?? []).map((workflow) => (
                    <option key={workflow.name} value={workflow.name}>
                      {workflow.displayName ?? workflow.name}
                    </option>
                  ))}
                </Select>
              )}
            </ToolbarField>
          </Toolbar>
        </div>

        {requests.isPending && <Loading rows={3} />}
        {requests.isError && (
          <div className="p-3">
            <ErrorNote error={requests.error} onRetry={() => void requests.refetch()} />
          </div>
        )}

        {requests.isSuccess &&
          ((requests.data ?? []).length === 0 ? (
            <Empty title={t('approvals.workflowRequests.empty.title')}>
              {t(filtered ? 'approvals.workflowRequests.empty.filtered' : 'approvals.workflowRequests.empty.body')}
            </Empty>
          ) : (
            <Table label={t('approvals.workflowRequests.title')}>
              <thead>
                <tr>
                  <Th>{t('workflows.column.workflow')}</Th>
                  <Th>{t('approvals.workflowRequests.request')}</Th>
                  <Th>{t('approvals.workflowRequests.requested')}</Th>
                  <Th>{t('approvals.workflowRequests.expires')}</Th>
                  <Th>{t('approvals.workflowRequests.state')}</Th>
                </tr>
              </thead>
              <tbody>
                {(requests.data ?? []).map((request) => (
                  <RequestRow key={`${request.runId}:${request.requestId}`} request={request} />
                ))}
              </tbody>
            </Table>
          ))}

        {requests.isSuccess && (
          <Pager page={page} size={(requests.data ?? []).length} pageSize={PAGE_SIZE} onChange={setPage} />
        )}
      </Panel>
    </div>
  );
}

/** One request. The workflow name is a link to the run's card; a request with no name links to the run. */
function RequestRow({ request }: { request: WorkflowPendingRequest }): ReactNode {
  const t = useT();
  const to =
    request.workflowName == null
      ? `runs/${encodeURIComponent(request.runId)}`
      : `workflows/${encodeURIComponent(request.workflowName)}?runId=${encodeURIComponent(request.runId)}`;
  const prompt = request.prompt ?? '';

  return (
    <tr className="focus-within:bg-raised hover:bg-raised" data-testid={`workflow-request-${request.requestId}`}>
      <Td>
        <Tooltip text={t('approvals.workflowRequests.openTitle')}>
          <Link to={to}>
            <span className="font-semibold">{request.workflowName ?? shortId(request.runId, 13, 6)}</span>
          </Link>
        </Tooltip>
        <p className="mt-0.5">
          <Mono className="text-subtle" title={request.runId}>
            {shortId(request.runId, 13, 6)}
          </Mono>
        </p>
      </Td>
      <Td>
        <p className="max-w-md text-sm break-words">
          {prompt.length === 0
            ? t('approvals.workflowRequests.noPrompt')
            : prompt.length > PROMPT_PREVIEW
              ? `${prompt.slice(0, PROMPT_PREVIEW)}…`
              : prompt}
        </p>
        <Mono className="text-subtle">{request.form}</Mono>
      </Td>
      <Td className="text-sm text-muted" title={absoluteTime(request.requestedAt)}>
        {relativeTime(request.requestedAt)}
      </Td>
      <Td className="font-mono text-sm">
        {request.expiresAt == null ? (
          <span className="text-subtle">{t('approvals.workflowRequests.noLimit')}</span>
        ) : (
          // relativeTime() is "ago"-only (a future instant reads as clock skew), and an expiry is
          // always ahead: the absolute time is the honest one.
          <span className="text-warn">{absoluteTime(request.expiresAt)}</span>
        )}
      </Td>
      <Td>
        {request.answered ? (
          <Badge tone="info">
            {t('approvals.workflowRequests.answered')}
          </Badge>
        ) : (
          <Badge tone="warn">
            <StatusDot tone="warn" live />
            {t('approvals.workflowRequests.waiting')}
          </Badge>
        )}
        {request.answered && request.lastResponseRunId != null && (
          <p className="mt-0.5 text-xs text-subtle">
            {t('approvals.workflowRequests.answeredOn', { id: shortId(request.lastResponseRunId, 8, 4) })}
          </p>
        )}
      </Td>
    </tr>
  );
}

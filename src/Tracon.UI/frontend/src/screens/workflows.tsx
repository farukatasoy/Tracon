import { useState, type ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { absoluteTime, relativeTime } from '../lib/format';
import { useT, type MessageKey } from '../lib/i18n';
import {
  Badge,
  Button,
  Empty,
  ErrorNote,
  LinkButton,
  Loading,
  PageHeader,
  Panel,
  Select,
  Table,
  Td,
  Th,
} from '../components/ui';
import { Link } from '../lib/router';
import { Toolbar, ToolbarField } from '../components/toolbar';
import { PlusIcon } from '../components/icons';
import type { TraconMetaResponse as Meta } from '@tracon/client';
import type { WorkflowDescriptor, WorkflowKind } from '../lib/server-types';

/** One-line description of what each pattern does, shown wherever a kind is picked. */
export const KIND_HINT: Record<WorkflowKind, MessageKey> = {
  Sequential: 'workflows.kind.sequential',
  Concurrent: 'workflows.kind.concurrent',
  Handoff: 'workflows.kind.handoff',
  GroupChat: 'workflows.kind.groupChat',
  Magentic: 'workflows.kind.magentic',
};

export function WorkflowsScreen({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const [query, setQuery] = useState('');
  const [origin, setOrigin] = useState('');

  const workflows = useQuery({
    queryKey: ['workflows'],
    queryFn: () => unwrap(client.GET('/api/workflows')) as Promise<WorkflowDescriptor[]>,
  });

  const filtered = useFilteredWorkflows(workflows.data, query, origin);
  const filtering = query.trim().length > 0 || origin.length > 0;

  const reset = (): void => {
    setQuery('');
    setOrigin('');
  };

  return (
    <>
      <PageHeader
        title={t('nav.workflows')}
        description={t('workflows.description')}
        actions={
          meta.roles.canAdminister && (
            <LinkButton to="workflows/new" tone="primary">
              <PlusIcon className="size-3.5" />
              {t('workflows.new')}
            </LinkButton>
          )
        }
      />

      <Toolbar
        search={{ value: query, onChange: setQuery, label: t('workflows.search') }}
        onReset={filtering ? reset : undefined}
      >
        <ToolbarField label={t('common.source')}>
          {(id) => (
            <Select id={id} value={origin} onChange={setOrigin}>
              <option value="">{t('workflows.anyOrigin')}</option>
              <option value="Code">{t('workflows.originCode')}</option>
              <option value="Database">{t('workflows.originDatabase')}</option>
            </Select>
          )}
        </ToolbarField>
      </Toolbar>

      <Panel>
        {workflows.isPending && <Loading rows={6} />}
        {workflows.isError && (
          <div className="p-4">
            <ErrorNote error={workflows.error} onRetry={() => void workflows.refetch()} />
          </div>
        )}

        {workflows.isSuccess && filtered.length === 0 && (
          <Empty
            title={filtering ? t('common.noResults') : t('workflows.empty.title')}
            action={
              filtering ? (
                <Button onClick={reset}>{t('toolbar.reset')}</Button>
              ) : (
                meta.roles.canAdminister && (
                  <LinkButton to="workflows/new" tone="primary">
                    {t('workflows.empty.action')}
                  </LinkButton>
                )
              )
            }
          >
            {filtering ? (
              t('workflows.empty.filtered')
            ) : (
              <>
                {t('workflows.empty.body')} <code>AddWorkflow(...)</code>.
              </>
            )}
          </Empty>
        )}

        {workflows.isSuccess && filtered.length > 0 && (
          <Table label={t('nav.workflows')}>
            <thead>
              <tr>
                <Th>{t('workflows.column.workflow')}</Th>
                <Th>{t('workflows.column.pattern')}</Th>
                <Th>{t('nav.agents')}</Th>
                <Th>{t('common.source')}</Th>
                <Th>{t('common.updated')}</Th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((workflow) => (
                <tr key={workflow.name} className="focus-within:bg-raised hover:bg-raised">
                  <Td>
                    <Link to={`workflows/${encodeURIComponent(workflow.name)}`}>
                      {workflow.displayName ?? workflow.name}
                    </Link>
                    {workflow.description != null && workflow.description.length > 0 && (
                      <span className="block text-sm text-subtle">{workflow.description}</span>
                    )}
                  </Td>
                  <Td>
                    {/* A code-defined workflow is a free graph, not one of the
                        five patterns, so it has no kind to show. */}
                    {workflow.kind == null ? (
                      <Badge description={t('workflows.codeGraphTitle')}>{t('workflows.codeGraph')}</Badge>
                    ) : (
                      <Badge tone="accent" description={t(KIND_HINT[workflow.kind])}>
                        {workflow.kind}
                      </Badge>
                    )}
                  </Td>
                  <Td className="text-muted">
                    {workflow.agentNames.length === 0 ? '—' : workflow.agentNames.join(' → ')}
                  </Td>
                  <Td>
                    {/* A code-defined workflow cannot be edited here: it ships with the
                        deployment and wins over any stored definition of the same name. */}
                    <Badge
                      tone={workflow.origin === 'Code' ? 'info' : 'neutral'}
                      description={
                        workflow.origin === 'Code'
                          ? t('workflows.originCodeTitle')
                          : t('workflows.originDatabaseTitle')
                      }
                    >
                      {workflow.origin === 'Code' ? t('workflows.originCode') : t('workflows.originDatabase')}
                    </Badge>
                  </Td>
                  <Td className="text-muted" title={absoluteTime(workflow.updatedAt)}>
                    {workflow.updatedAt == null ? '—' : relativeTime(workflow.updatedAt)}
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Panel>

      {workflows.isSuccess && filtered.length > 0 && !meta.roles.canOperate && (
        <p className="mt-3 text-sm text-subtle">{t('workflows.needsOperator')}</p>
      )}
    </>
  );
}

/**
 * The list this screen shows.
 *
 * Filtering happens here rather than on the server: `/api/workflows` returns
 * the whole catalogue in one response — a deployment has tens of workflows, not
 * thousands — so a round trip per keystroke would buy nothing.
 */
function useFilteredWorkflows(
  workflows: WorkflowDescriptor[] | undefined,
  query: string,
  origin: string,
): WorkflowDescriptor[] {
  const needle = query.trim().toLowerCase();

  return (workflows ?? []).filter((workflow) => {
    if (origin.length > 0 && workflow.origin !== origin) {
      return false;
    }

    if (needle.length === 0) {
      return true;
    }

    return (
      workflow.name.toLowerCase().includes(needle) ||
      (workflow.displayName?.toLowerCase().includes(needle) ?? false) ||
      (workflow.description?.toLowerCase().includes(needle) ?? false)
    );
  });
}

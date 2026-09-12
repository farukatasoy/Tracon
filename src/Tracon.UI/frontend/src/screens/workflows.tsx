import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { client, unwrap } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime } from '../lib/format';
import { useT, type MessageKey } from '../lib/i18n';
import {
  Badge,
  Empty,
  ErrorNote,
  Loading,
  PageHeader,
  Panel,
  Table,
  Td,
  Th,
} from '../components/ui';
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
  const workflows = useQuery({
    queryKey: ['workflows'],
    queryFn: () => unwrap(client.GET('/api/workflows')) as Promise<WorkflowDescriptor[]>,
  });

  return (
    <>
      <PageHeader
        title={t('nav.workflows')}
        description={t('workflows.description')}
        actions={
          meta.roles.canAdminister && (
            <Link
              to="workflows/new"
              className="inline-flex h-8 items-center gap-1.5 rounded-md border border-transparent bg-accent px-3 text-[13px] font-medium text-accent-fg"
            >
              <PlusIcon className="size-3.5" />
              {t('workflows.new')}
            </Link>
          )
        }
      />

      <Panel>
        {workflows.isPending && <Loading />}
        {workflows.isError && (
          <div className="p-4">
            <ErrorNote error={workflows.error} />
          </div>
        )}

        {workflows.isSuccess && workflows.data.length === 0 && (
          <Empty title={t('workflows.empty.title')}>
            {t('workflows.empty.body')} <code>AddWorkflow(...)</code>.
          </Empty>
        )}

        {workflows.isSuccess && workflows.data.length > 0 && (
          <Table>
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
              {workflows.data.map((workflow) => (
                <tr key={workflow.name} className="hover:bg-raised">
                  <Td>
                    <Link to={`workflows/${encodeURIComponent(workflow.name)}`}>
                      {workflow.displayName ?? workflow.name}
                    </Link>
                    {workflow.description != null && workflow.description.length > 0 && (
                      <span className="block text-[12px] text-subtle">{workflow.description}</span>
                    )}
                  </Td>
                  <Td>
                    {/* A code-defined workflow is a free graph, not one of the
                        five patterns, so it has no kind to show. */}
                    {workflow.kind == null ? (
                      <Badge title={t('workflows.codeGraphTitle')}>{t('workflows.codeGraph')}</Badge>
                    ) : (
                      <Badge tone="accent" title={t(KIND_HINT[workflow.kind])}>
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
                    <Badge tone={workflow.origin === 'Code' ? 'info' : 'neutral'}>
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

      {workflows.isSuccess && workflows.data.length > 0 && !meta.roles.canOperate && (
        <p className="mt-3 text-[12px] text-subtle">
          {t('workflows.needsOperator')}
        </p>
      )}
    </>
  );
}

import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { absoluteTime, relativeTime } from '../lib/format';
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
import type { Meta, WorkflowKind } from '../lib/types';

/** One-line description of what each pattern does, shown wherever a kind is picked. */
export const KIND_HINT: Record<WorkflowKind, string> = {
  Sequential: 'Runs in order; each output is the next input.',
  Concurrent: 'Runs at the same time; results are merged.',
  Handoff: 'The first agent hands over when it needs to.',
  GroupChat: 'A round-robin manager passes the turn around.',
  Magentic: 'A manager agent plans, watches and re-plans.',
};

export function WorkflowsScreen({ meta }: { meta: Meta }): ReactNode {
  const workflows = useQuery({ queryKey: ['workflows'], queryFn: api.workflows });

  return (
    <>
      <PageHeader
        title="Workflows"
        description="Agents from the catalogue wired together with one of five ready-made patterns. A workflow run is one row in Runs, and every agent it calls is a child of that row."
        actions={
          meta.roles.canAdminister && (
            <Link
              to="workflows/new"
              className="inline-flex h-8 items-center gap-1.5 rounded-md border border-transparent bg-accent px-3 text-[13px] font-medium text-accent-fg"
            >
              <PlusIcon className="size-3.5" />
              New workflow
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
          <Empty title="No workflows yet">
            A workflow connects agents you already have. Define one here, or register it in code
            with <code>AddWorkflow(...)</code>.
          </Empty>
        )}

        {workflows.isSuccess && workflows.data.length > 0 && (
          <Table>
            <thead>
              <tr>
                <Th>Workflow</Th>
                <Th>Pattern</Th>
                <Th>Agents</Th>
                <Th>Origin</Th>
                <Th>Updated</Th>
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
                      <Badge title="Built by a factory in code; see its graph.">code graph</Badge>
                    ) : (
                      <Badge tone="accent" title={KIND_HINT[workflow.kind]}>
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
                      {workflow.origin === 'Code' ? 'code' : 'database'}
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
          Running a workflow needs the operator role.
        </p>
      )}
    </>
  );
}

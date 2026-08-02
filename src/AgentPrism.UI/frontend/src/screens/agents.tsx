import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { relativeTime } from '../lib/format';
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
} from '../components/ui';
import { PlusIcon } from '../components/icons';
import type { AgentDescriptor } from '../lib/types';

export function OriginBadge({ agent }: { agent: AgentDescriptor }): ReactNode {
  if (agent.origin === 'Code') {
    return (
      <Badge tone="info" title={`Declared in code by source "${agent.sourceName}". Read only.`}>
        code
      </Badge>
    );
  }

  if (agent.origin === 'Database') {
    return (
      <Badge tone="accent" title={`Stored definition, version ${agent.version}.`}>
        db · v{agent.version}
      </Badge>
    );
  }

  return <Badge title={`Provided by source "${agent.sourceName}".`}>{agent.sourceName}</Badge>;
}

export function AgentsScreen(): ReactNode {
  const agents = useQuery({ queryKey: ['agents'], queryFn: api.agents });

  return (
    <>
      <PageHeader
        title="Agents"
        description="Every agent the catalogue resolves, from code and from the database. On a name collision code wins, so agents declared in code cannot be edited here."
        actions={
          <Link to="agents/new">
            <Button tone="primary">
              <PlusIcon className="size-3.5" />
              New agent
            </Button>
          </Link>
        }
      />

      <Panel>
        {agents.isPending && <Loading />}
        {agents.isError && <div className="p-4"><ErrorNote error={agents.error} /></div>}

        {agents.isSuccess && agents.data.length === 0 && (
          <Empty title="No agents yet">
            Declare one in code with <Mono>AddAgent(...)</Mono>, or create one here. Agents
            created here are stored in the database and compiled at run time.
          </Empty>
        )}

        {agents.isSuccess && agents.data.length > 0 && (
          <Table>
            <thead>
              <tr>
                <Th>Name</Th>
                <Th>Source</Th>
                <Th>Model</Th>
                <Th>Tools</Th>
                <Th>Updated</Th>
                <Th />
              </tr>
            </thead>
            <tbody>
              {agents.data.map((agent) => (
                <tr key={agent.name} className="hover:bg-raised">
                  <Td>
                    <Link to={`agents/${encodeURIComponent(agent.name)}`} className="block">
                      <span className="font-medium">{agent.displayName ?? agent.name}</span>
                      {agent.displayName !== null && agent.displayName !== undefined && (
                        <Mono className="ml-2 text-subtle">{agent.name}</Mono>
                      )}
                      {agent.description !== null && agent.description !== undefined && (
                        <span className="block text-[12px] text-muted">{agent.description}</span>
                      )}
                    </Link>
                  </Td>
                  <Td>
                    <div className="flex items-center gap-1.5">
                      <OriginBadge agent={agent} />
                      {agent.usesHarness && <Badge tone="warn" title="Harness features enabled">harness</Badge>}
                    </div>
                  </Td>
                  <Td>
                    {agent.model === null || agent.model === undefined ? (
                      <span className="text-subtle">—</span>
                    ) : (
                      <Mono title={`Provider: ${agent.model.provider}`}>{agent.model.model}</Mono>
                    )}
                  </Td>
                  <Td>
                    {agent.toolNames.length === 0 ? (
                      <span className="text-subtle">—</span>
                    ) : (
                      <span title={agent.toolNames.join(', ')}>{agent.toolNames.length}</span>
                    )}
                  </Td>
                  <Td className="text-muted">{relativeTime(agent.updatedAt)}</Td>
                  <Td className="text-right">
                    <Link to={`playground/${encodeURIComponent(agent.name)}`}>
                      <Button tone="ghost">Run</Button>
                    </Link>
                  </Td>
                </tr>
              ))}
            </tbody>
          </Table>
        )}
      </Panel>
    </>
  );
}

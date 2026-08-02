import type { ReactNode } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api } from '../lib/api';
import { Link } from '../lib/router';
import { prettyJson } from '../lib/format';
import {
  Badge,
  CodeBlock,
  Empty,
  ErrorNote,
  Loading,
  Mono,
  PageHeader,
  Panel,
} from '../components/ui';

/**
 * Registered tools.
 *
 * This screen is read only by design. Tools are defined in code and nowhere
 * else: a console that could write tool code would let anyone who reaches the
 * console run code on the server (rule K2, decision K-012).
 */
export function ToolsScreen(): ReactNode {
  const tools = useQuery({ queryKey: ['tools'], queryFn: api.tools });
  const agents = useQuery({ queryKey: ['agents'], queryFn: api.agents });

  const usedBy = (tool: string): string[] =>
    (agents.data ?? []).filter((agent) => agent.toolNames.includes(tool)).map((agent) => agent.name);

  return (
    <>
      <PageHeader
        title="Tools"
        description="Tools registered in the host application. This list is what agent definitions may reference — the console never accepts free text here."
      />

      {tools.isPending && <Loading />}
      {tools.isError && <ErrorNote error={tools.error} />}

      {tools.isSuccess && tools.data.length === 0 && (
        <Panel>
          <Empty title="No tools registered">
            Register them in code with <Mono>AddTool(...)</Mono> or{' '}
            <Mono>AddToolsFrom(typeof(OrderTools))</Mono>. Only methods marked with{' '}
            <Mono>[AgentPrismTool]</Mono> are scanned.
          </Empty>
        </Panel>
      )}

      <div className="flex flex-col gap-3">
        {(tools.data ?? []).map((tool) => {
          const agentNames = usedBy(tool.name);

          return (
            <Panel key={tool.name}>
              <div className="flex flex-wrap items-start justify-between gap-3 border-b border-line px-4 py-2.5">
                <div>
                  <Mono className="text-[13px] font-semibold">{tool.name}</Mono>
                  {tool.description != null && (
                    <p className="mt-0.5 text-[12px] text-muted">{tool.description}</p>
                  )}
                </div>
                <div className="flex items-center gap-1.5">
                  {tool.requiresApproval && (
                    <Badge tone="warn" title="The approval flow arrives in phase 6; today this flag is informational.">
                      approval required
                    </Badge>
                  )}
                  {agentNames.length === 0 ? (
                    <Badge title="No agent references this tool.">unused</Badge>
                  ) : (
                    agentNames.map((name) => (
                      <Link key={name} to={`agents/${encodeURIComponent(name)}`}>
                        <Badge tone="accent">{name}</Badge>
                      </Link>
                    ))
                  )}
                </div>
              </div>

              <div className="p-4">
                {tool.jsonSchema == null || tool.jsonSchema.length === 0 ? (
                  <p className="text-[12px] text-subtle">This tool takes no arguments.</p>
                ) : (
                  <CodeBlock code={prettyJson(tool.jsonSchema)} maxHeight="max-h-72" />
                )}
              </div>
            </Panel>
          );
        })}
      </div>

      <p className="mt-4 text-[11px] text-subtle">
        Per-tool call counts and durations arrive with the observability phase; the invocation
        table exists but is not written yet.
      </p>
    </>
  );
}

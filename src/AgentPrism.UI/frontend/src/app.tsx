import type { ReactNode } from 'react';
import { AccessGate } from './components/access-gate';
import { Layout } from './components/layout';
import { Empty, Panel } from './components/ui';
import { useT } from './lib/i18n';
import { useRoute, type RouteDefinition } from './lib/router';
import { DashboardScreen } from './screens/dashboard';
import { AgentsScreen } from './screens/agents';
import { AgentDetailScreen } from './screens/agent-detail';
import { AgentEditorScreen } from './screens/agent-editor';
import { SkillEditorScreen, SkillsScreen } from './screens/skills';
import { PlaygroundScreen } from './screens/playground';
import { SessionsScreen } from './screens/sessions';
import { SessionDetailScreen } from './screens/session-detail';
import { RunsScreen } from './screens/runs';
import { WorkflowsScreen } from './screens/workflows';
import { WorkflowEditorScreen } from './screens/workflow-editor';
import { WorkflowDetailScreen } from './screens/workflow-detail';
import { JobsScreen } from './screens/jobs';
import { JobDetailScreen } from './screens/job-detail';
import { EvalsScreen } from './screens/evals';
import { EvalSuiteDetailScreen } from './screens/eval-detail';
import { EvalRunDetailScreen } from './screens/eval-run-detail';
import { ExperimentsScreen } from './screens/experiments';
import { ExperimentDetailScreen } from './screens/experiment-detail';
import { RunDetailScreen } from './screens/run-detail';
import { ToolsScreen } from './screens/tools';
import { ModelsScreen } from './screens/models';
import { McpScreen } from './screens/mcp';
import { SettingsScreen } from './screens/settings';
import { AuditScreen } from './screens/audit';
import { DiagnosticsScreen } from './screens/diagnostics';
import type { Meta } from './lib/types';

/**
 * Route table.
 *
 * Order matters: a literal segment must come before the dynamic pattern that
 * would also match it, so `agents/new` is registered above `agents/:name`.
 */
const routes = (meta: Meta): RouteDefinition[] => [
  { pattern: '', render: () => <DashboardScreen meta={meta} /> },
  { pattern: 'dashboard', render: () => <DashboardScreen meta={meta} /> },
  { pattern: 'agents', render: () => <AgentsScreen meta={meta} /> },
  { pattern: 'agents/new', render: () => <AgentEditorScreen /> },
  {
    pattern: 'agents/:name',
    render: (params) => <AgentDetailScreen name={params['name'] ?? ''} meta={meta} />,
  },
  { pattern: 'agents/:name/edit', render: (params) => <AgentEditorScreen name={params['name'] ?? ''} /> },
  { pattern: 'skills', render: () => <SkillsScreen meta={meta} /> },
  { pattern: 'skills/new', render: () => <SkillEditorScreen /> },
  { pattern: 'skills/:name/edit', render: (params) => <SkillEditorScreen name={params['name'] ?? ''} /> },
  { pattern: 'playground', render: () => <PlaygroundScreen /> },
  { pattern: 'playground/:name', render: (params) => <PlaygroundScreen name={params['name'] ?? ''} /> },
  { pattern: 'sessions', render: () => <SessionsScreen meta={meta} /> },
  { pattern: 'sessions/:id', render: (params) => <SessionDetailScreen id={params['id'] ?? ''} /> },
  { pattern: 'workflows', render: () => <WorkflowsScreen meta={meta} /> },
  { pattern: 'workflows/new', render: () => <WorkflowEditorScreen /> },
  {
    pattern: 'workflows/:name',
    render: (params) => <WorkflowDetailScreen name={params['name'] ?? ''} meta={meta} />,
  },
  {
    pattern: 'workflows/:name/edit',
    render: (params) => <WorkflowEditorScreen name={params['name'] ?? ''} />,
  },
  { pattern: 'jobs', render: () => <JobsScreen meta={meta} /> },
  { pattern: 'jobs/:id', render: (params) => <JobDetailScreen id={params['id'] ?? ''} meta={meta} /> },
  { pattern: 'evals', render: () => <EvalsScreen meta={meta} /> },
  { pattern: 'evals/runs/:id', render: (params) => <EvalRunDetailScreen id={params['id'] ?? ''} /> },
  {
    pattern: 'evals/:name',
    render: (params) => <EvalSuiteDetailScreen name={params['name'] ?? ''} meta={meta} />,
  },
  { pattern: 'experiments', render: () => <ExperimentsScreen meta={meta} /> },
  {
    pattern: 'experiments/:name',
    render: (params) => <ExperimentDetailScreen name={params['name'] ?? ''} meta={meta} />,
  },
  { pattern: 'runs', render: () => <RunsScreen /> },
  { pattern: 'runs/:id', render: (params) => <RunDetailScreen id={params['id'] ?? ''} /> },
  { pattern: 'tools', render: () => <ToolsScreen /> },
  { pattern: 'models', render: () => <ModelsScreen /> },
  { pattern: 'mcp', render: () => <McpScreen meta={meta} /> },
  { pattern: 'audit', render: () => <AuditScreen /> },
  { pattern: 'diagnostics', render: () => <DiagnosticsScreen /> },
  { pattern: 'settings', render: () => <SettingsScreen meta={meta} /> },
];

export function App(): ReactNode {
  return <AccessGate>{(meta) => <Shell meta={meta} />}</AccessGate>;
}

function Shell({ meta }: { meta: Meta }): ReactNode {
  const t = useT();
  const screen = useRoute(routes(meta));

  return (
    <Layout meta={meta}>
      {screen ?? (
        <Panel>
          <Empty title={t('shell.notFound.title')}>{t('shell.notFound.body')}</Empty>
        </Panel>
      )}
    </Layout>
  );
}

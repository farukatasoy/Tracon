import type { ReactNode } from 'react';
import type { MessageKey } from '../lib/i18n';
import {
  AgentsIcon,
  ApprovalsIcon,
  AuditIcon,
  DashboardIcon,
  DiagnosticsIcon,
  EvalsIcon,
  ExperimentsIcon,
  JobsIcon,
  McpIcon,
  ModelsIcon,
  PlaygroundIcon,
  RunsIcon,
  SessionsIcon,
  SettingsIcon,
  ToolsIcon,
  TriggersIcon,
  WorkflowIcon,
} from './icons';

/**
 * Every screen in the console, in the order the shell shows them.
 *
 * 🚨 ONE table, read by both the sidebar and the command palette. They were two
 * separate lists and they had already drifted: `triggers` and `diagnostics`
 * were in the sidebar and missing from the palette, so two screens could not be
 * reached from the keyboard at all.
 *
 * Two groups, because an operator does two different jobs here: WATCHING what
 * the airspace is doing, and CONFIGURING what is allowed to fly in it.
 *
 * The `nav.*` message keys are unchanged on purpose — the documentation gate
 * (`docs-site/scripts/check-console-screens.mjs`) reads them to decide which
 * screenshots and which guide sections must exist. Group labels are therefore
 * `nav.group.*`, which that gate's pattern deliberately does not match.
 *
 * `english` is what the palette matches against regardless of the interface
 * language: an operator who learned the product in English should still find
 * "runs" while the console is in Turkish.
 */
export interface NavItem {
  path: string;
  label: MessageKey;
  english: string;
  icon: (props: { className?: string }) => ReactNode;
  adminOnly?: boolean;
}

export interface NavGroup {
  group: MessageKey;
  items: readonly NavItem[];
}

export const NAV_GROUPS: readonly NavGroup[] = [
  {
    group: 'nav.group.operate',
    items: [
      { path: 'dashboard', label: 'nav.dashboard', english: 'Dashboard', icon: DashboardIcon },
      { path: 'playground', label: 'nav.playground', english: 'Playground', icon: PlaygroundIcon },
      { path: 'runs', label: 'nav.runs', english: 'Runs', icon: RunsIcon },
      { path: 'sessions', label: 'nav.sessions', english: 'Sessions', icon: SessionsIcon },
      { path: 'approvals', label: 'nav.approvals', english: 'Approvals', icon: ApprovalsIcon },
      { path: 'jobs', label: 'nav.jobs', english: 'Jobs', icon: JobsIcon },
      { path: 'evals', label: 'nav.evals', english: 'Evals', icon: EvalsIcon },
      { path: 'experiments', label: 'nav.experiments', english: 'Experiments', icon: ExperimentsIcon },
      { path: 'audit', label: 'nav.audit', english: 'Audit', icon: AuditIcon, adminOnly: true },
      {
        path: 'diagnostics',
        label: 'nav.diagnostics',
        english: 'Diagnostics',
        icon: DiagnosticsIcon,
        adminOnly: true,
      },
    ],
  },
  {
    group: 'nav.group.configure',
    items: [
      { path: 'agents', label: 'nav.agents', english: 'Agents', icon: AgentsIcon },
      { path: 'workflows', label: 'nav.workflows', english: 'Workflows', icon: WorkflowIcon },
      { path: 'tools', label: 'nav.tools', english: 'Tools', icon: ToolsIcon },
      { path: 'skills', label: 'nav.skills', english: 'Skills', icon: ToolsIcon },
      { path: 'models', label: 'nav.models', english: 'Models', icon: ModelsIcon },
      { path: 'mcp', label: 'nav.mcp', english: 'MCP', icon: McpIcon },
      { path: 'triggers', label: 'nav.triggers', english: 'Triggers', icon: TriggersIcon, adminOnly: true },
      { path: 'settings', label: 'nav.settings', english: 'Settings', icon: SettingsIcon },
    ],
  },
];

/** The groups a role may see. Hiding is a courtesy; the server enforces. */
export function visibleNavigation(canAdminister: boolean): NavGroup[] {
  return NAV_GROUPS.map((group) => ({
    group: group.group,
    items: group.items.filter((item) => item.adminOnly !== true || canAdminister),
  }));
}

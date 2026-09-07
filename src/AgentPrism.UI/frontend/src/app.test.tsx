import { afterEach, describe, expect, it } from 'vitest';
import { routes } from './app';
import { fixture, installApiMock, testMeta, type FixtureRoute } from './test/api-fixtures';
import { renderScreen, waitFor } from './test/render';

/**
 * `RunStatistics` (`/api/stats`) narrows several OpenAPI-optional fields to
 * always-present (`lib/server-types.ts`'s `Fix<>`) because the real server's
 * C# record always initialises them — `byAgent: []`, never an omitted key.
 * The generic `{}` fallback below breaks that assumption on `.byAgent.length`
 * and similar reads; this is what the real server always sends when there is
 * nothing to report.
 */
const emptyRunStatistics = {
  totalRuns: 0,
  completedRuns: 0,
  failedRuns: 0,
  canceledRuns: 0,
  runningRuns: 0,
  awaitingInputRuns: 0,
  inputTokens: 0,
  outputTokens: 0,
  totalTokens: 0,
  cachedInputTokens: 0,
  reasoningTokens: 0,
  audioInputTokens: 0,
  audioOutputTokens: 0,
  byAgent: [],
  byModel: [],
  byVersion: [],
  byUser: [],
  byLabel: [],
  byErrorClass: [],
  totalCost: null,
  currency: null,
  runsWithUnknownPricing: 0,
  errorRate: null,
  scoredRuns: 0,
  positiveRate: null,
};

/**
 * `RunScoreSummary` (`/api/evaluation/scores/summary`) narrows `byName` and
 * `series` to always-present the same way `emptyRunStatistics` does above —
 * the generic `{}` fallback breaks `PersistentScoreTrendPanel`'s
 * `.series.length` read.
 */
const emptyRunScoreSummary = {
  byName: [],
  byAuthor: [],
  bySource: [],
  byAgent: [],
  series: [],
};

/** A code-defined agent with no persisted definition — a real, named path (`AgentEditorScreen`'s HATA-S4-010 branch), not a fixture shortcut. */
const codeAgentDetail = {
  descriptor: {
    name: 'sample',
    origin: 'Code',
    sourceName: 'code',
    version: 1,
    model: null,
    toolNames: [],
    skillNames: [],
    callableAgentNames: [],
    usesHarness: false,
  },
  definition: null,
};

const now = new Date().toISOString();

const emptySkillDetail = {
  id: 'sample',
  tenantId: 'default',
  name: 'sample',
  description: '',
  instructions: '',
  compatibility: null,
  license: null,
  allowedTools: null,
  metadata: {},
  enabled: true,
  version: 1,
  resources: [],
  scripts: [],
  createdAt: now,
  updatedAt: now,
};

const emptyTriggerDetail = {
  name: 'sample',
  targetKind: 'Agent',
  targetName: 'sample',
  signingSecretConfigurationName: 'AgentPrism:Triggers:sample',
  resolved: true,
  payloadMode: 'WholeBody',
  payloadPath: null,
  enabled: true,
  createdAt: now,
};

const emptyWorkflowDetail = {
  name: 'sample',
  displayName: null,
  description: null,
  kind: 'Sequential',
  agentNames: [],
  managerAgentName: null,
  nodes: [],
  maxIterations: null,
  handoffInstructions: null,
  requirePlanApproval: false,
  version: 1,
};

const emptyDiagnosticsReport = {
  persistenceProvider: 'InMemory',
  registeredPersistenceProviders: 0,
  canConnect: true,
  migrationsUpToDate: true,
  pendingMigrations: [],
  modelProviders: [],
  configuration: [],
  uiEmbedded: true,
  toolCount: 0,
  agentCount: 0,
  extensionPoints: [],
};

const emptyQuotaUsage = { definitions: [], usage: [] };

const emptySessionDetail = {
  id: 'sample',
  agentName: 'sample',
  createdAt: now,
  updatedAt: now,
  messages: null,
  state: null,
};

const emptyJobDetail = {
  job: {
    id: 'sample',
    tenantId: 'default',
    scheduleId: null,
    kind: 'AgentBatch',
    targetName: 'sample',
    status: 'Completed',
    payload: null,
    totalItems: 0,
    doneItems: 0,
    failedItems: 0,
    attempt: 0,
    maxAttempts: null,
    leaseOwner: null,
    leaseUntil: null,
    scheduledFor: now,
    startedAt: null,
    completedAt: null,
    errorMessage: null,
    createdAt: now,
  },
  items: [],
};

const emptyEvalRunDetail = {
  run: {
    id: 'sample',
    tenantId: 'default',
    suiteId: 'sample',
    jobId: null,
    agentVersion: null,
    modelId: null,
    status: 'Completed',
    total: 0,
    passed: 0,
    failed: 0,
    inputTokens: null,
    outputTokens: null,
    startedAt: now,
    completedAt: null,
  },
  results: [],
};

const emptyExperimentDetail = {
  id: 'sample',
  tenantId: 'default',
  name: 'sample',
  agentName: 'sample',
  variants: [],
  status: 'Draft',
  assignmentKey: null,
  startedAt: null,
};

const emptyExperimentResults = { experiment: emptyExperimentDetail, results: [] };

/**
 * Per-route overrides for the handful of endpoints whose response type
 * `server-types.ts` narrows to always-present fields (its `Fix<>` pattern) or
 * whose fields are simply required outright — the generic default in
 * `api-fixtures.ts` (`{}` for a non-collection GET) is deliberately shallow
 * and models neither. Add an entry here, not a broader default, when a new
 * route's screen dereferences a field the generic default cannot supply (see
 * the module comment on `emptyRunStatistics`).
 */
function overridesFor(pattern: string): FixtureRoute[] {
  switch (pattern) {
    case '':
    case 'dashboard':
      return [
        fixture('GET', 'api/stats', emptyRunStatistics),
        fixture('GET', 'api/evaluation/scores/summary', emptyRunScoreSummary),
      ];
    case 'agents/:name':
    case 'agents/:name/edit':
      return [fixture('GET', 'api/agents/:name', codeAgentDetail)];
    case 'skills/:name/edit':
      return [fixture('GET', 'api/skills/:name', emptySkillDetail)];
    case 'triggers/:name/edit':
      return [fixture('GET', 'api/triggers/:name', emptyTriggerDetail)];
    case 'workflows/:name/edit':
      return [fixture('GET', 'api/workflows/:name', emptyWorkflowDetail)];
    case 'diagnostics':
      return [fixture('GET', 'api/diagnostics', emptyDiagnosticsReport)];
    case 'settings':
      return [
        fixture('GET', 'api/quotas/usage', emptyQuotaUsage),
        fixture('GET', 'api/stats', emptyRunStatistics),
      ];
    case 'sessions/:id':
      return [fixture('GET', 'api/sessions/:sessionId', emptySessionDetail)];
    case 'jobs/:id':
      return [fixture('GET', 'api/jobs/:id', emptyJobDetail)];
    case 'evals/runs/:id':
      return [fixture('GET', 'api/evals/runs/:id', emptyEvalRunDetail)];
    case 'experiments/:name':
      return [
        fixture('GET', 'api/experiments/:name', emptyExperimentDetail),
        fixture('GET', 'api/experiments/:name/results', emptyExperimentResults),
      ];
    case 'runs/:id':
      return [fixture('GET', 'api/runs/:runId/trace', { id: 'sample', traceId: 'sample', tenantId: 'default', spans: [] })];
    default:
      return [];
  }
}

/**
 * Every route in `app.tsx`'s table, rendered once with a mostly-generic mock
 * API.
 *
 * This does not assert what a screen SHOWS — the branch-specific tests next
 * to `agent-editor` and `playground` do that — only that mounting it with a
 * plausible response for whatever it queries does not throw, and that it
 * settles out of its loading state instead of hanging on it. A route added to
 * the table without a matching entry here still fails: the table is read
 * directly, so nothing can be "forgotten".
 */
describe('every route in the table renders to a settled state', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  const sampleParams = (pattern: string): Record<string, string> => {
    const params: Record<string, string> = {};

    for (const segment of pattern.split('/')) {
      if (segment.startsWith(':')) {
        params[segment.slice(1)] = 'sample';
      }
    }

    return params;
  };

  for (const route of routes(testMeta)) {
    it(`renders "${route.pattern === '' ? '(root)' : route.pattern}"`, async () => {
      restoreFetch = installApiMock(overridesFor(route.pattern));

      const { container } = renderScreen(<>{route.render(sampleParams(route.pattern))}</>);

      expect(container.childElementCount).toBeGreaterThan(0);

      // A screen with nothing to fetch never shows `role="status"` at all —
      // this then passes on the very first check, which is exactly right.
      await waitFor(
        () => {
          expect(container.querySelector('[role="status"]')).toBeNull();
        },
        { timeout: 2000 },
      );
    });
  }
});

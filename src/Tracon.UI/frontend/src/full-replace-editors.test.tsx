import userEvent, { type UserEvent } from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { afterEach, describe, expect, it } from 'vitest';
import type {
  AgentDefinitionRequest,
  AgentSkillRequest,
  EvalCaseInput,
  EvalSuiteSaveRequest,
  ExperimentSaveRequest,
  InboundTriggerSaveRequest,
  JobScheduleSaveRequest,
  QuotaSaveRequest,
  RetentionPolicySaveRequest,
  TenantEgressPolicyRequest,
  WorkflowSaveRequest,
} from '@tracon/client';
import { fixture, installApiMock, testMeta, type FixtureRoute } from './test/api-fixtures';
import { renderScreen, screen, waitFor } from './test/render';
import { QuotaPanel } from './components/quota-panel';
import { RetentionPanel } from './components/retention-panel';
import { TenantProviderPanel } from './components/tenant-provider-panel';
import { AgentEditorScreen } from './screens/agent-editor';
import { EvalSuiteDetailScreen } from './screens/eval-detail';
import { EvalsScreen } from './screens/evals';
import { ExperimentsScreen } from './screens/experiments';
import { JobsScreen } from './screens/jobs';
import { SkillEditorScreen } from './screens/skills/skill-editor';
import { TriggerEditorScreen } from './screens/triggers';
import { WorkflowEditorScreen } from './screens/workflow-editor';

/*
  Gate for the full-replace class (B01/K-725, HATA-S3-003/K-802, and the
  2026-10-10 workflow `nodes` and quota `enabled` losses).

  Every management PUT is a FULL REPLACE: the server rebuilds the record from
  the body. A field a screen loaded but did not send back is erased; a field it
  sent as a constant is overwritten. Both kinds passed every earlier test,
  because each screen was only ever tested on the fields it had a control for.

  Two checks close the class:

  1. Round trip. Each screen that loads a record and saves it back is opened on
     a record with EVERY field set to a value its default cannot produce, then
     saved with no change. The body must equal the record. The expected body is
     typed `Complete<…Request>`, so a field added to a request type later fails
     `tsc` here until its case states the value it must carry.
  2. Inventory. Every `.PUT(` in the console source is either a round-trip case
     below or a create-only form with a stated reason. A new PUT fails here until
     someone classifies it.
*/

/** Every key required, all the way down — a request field nobody states fails `tsc`. */
type Complete<T> = T extends readonly (infer U)[]
  ? Complete<U>[]
  : T extends object
    ? { [K in keyof T]-?: Complete<T[K]> }
    : T;

interface RoundTrip {
  /** The file that makes the call, relative to `src/`. */
  source: string;
  /** The path literal exactly as the call writes it: `client.PUT('<path>', …)`. */
  path: string;
  title: string;
  routes: FixtureRoute[];
  render: () => ReactElement;
  /** From the first render to the click that sends the PUT. */
  save: (user: UserEvent) => Promise<void>;
  expected: unknown;
}

function roundTrip<TBody>(definition: Omit<RoundTrip, 'expected'> & { expected: Complete<TBody> }): RoundTrip {
  return definition;
}

/** Clicks a button once it is enabled — a button that never enables names the defect in the failure. */
async function clickWhenEnabled(user: UserEvent, button: HTMLElement): Promise<void> {
  await waitFor(() => expect(button).toHaveProperty('disabled', false));
  await user.click(button);
}

const stamp = '2026-10-10T09:00:00Z';

const providers = [
  { name: 'anthropic', displayName: 'Anthropic', status: 'Unknown', models: [{ name: 'claude', contextWindowTokens: null }] },
  { name: 'openai', displayName: 'OpenAI', status: 'Unknown', models: [{ name: 'gpt-5.4-mini', contextWindowTokens: null }] },
];

const agentRequest: Complete<AgentDefinitionRequest> = {
  name: 'support',
  displayName: 'Support',
  description: 'Answers support questions.',
  instructions: 'Be brief.',
  instructionsByCulture: { 'en-GB': 'Keep it short.' },
  model: {
    provider: 'anthropic',
    model: 'claude',
    temperature: 0.2,
    maxOutputTokens: 900,
    topP: 0.8,
    reasoningEffort: 'Low',
    providerSettings: { 'anthropic.promptCaching': true } as never,
    responseFormat: { kind: 'JsonSchema', schema: { type: 'object' }, schemaName: 'answer', schemaDescription: 'The answer.' },
    fallbacks: [{ provider: 'openai', model: 'gpt-5.4-mini' }],
    responseCache: { enabled: true, lifetime: '00:05:00' },
    seed: 7,
    topK: 40,
    stopSequences: ['END'],
    frequencyPenalty: 0.25,
    presencePenalty: 0.5,
    toolMode: 'required:get_order_status',
    allowMultipleToolCalls: false,
    reasoningOutput: 'Summary',
    toolLoop: { allowConcurrentToolCalls: true, maximumConsecutiveErrors: 2, terminateOnUnknownCalls: true },
  },
  toolNames: ['get_order_status'],
  skillNames: ['returns-policy'],
  // Phase 222: 'retired' is a name the host no longer registers. The form keeps it, so an edit that
  // does not touch it sends it back unchanged instead of dropping it.
  extensionNames: ['audit', 'retired'],
  callableAgentNames: ['billing'],
  subAgents: { childDeadline: '00:00:10', waitTimeout: '00:00:30' },
  mcpResourceUris: ['docs:file:///handbook.md'],
  harness: {
    maxContextWindowTokens: 64000,
    maxOutputTokens: 2000,
    maximumIterationsPerRequest: 8,
    harnessInstructions: 'Plan first.',
    disableCompaction: true,
    disableTodoProvider: true,
    disableFileMemory: true,
    disableWebSearch: true,
    enableWebSearch: false,
    disableToolAutoApproval: true,
    disableAgentSkillsProvider: true,
    disableAgentModeProvider: true,
    loop: {
      // Phase 201: the console now EDITS the loop, and editing must not cost what
      // it does not touch — a field the kind ignores (marker on aiJudge), the
      // todoCompletion modes it offers no control for, and a kind registered in
      // code all come back exactly as loaded.
      criteria: [
        { kind: 'aiJudge', marker: 'IGNORED BY AIJUDGE', judgeCriteria: ['Covers the source text'], judgeInstructions: 'Be strict.', modes: [] },
        { kind: 'todoCompletion', marker: null, judgeCriteria: [], judgeInstructions: null, modes: ['plan', 'execute'] },
        { kind: 'hasCitations', marker: null, judgeCriteria: [], judgeInstructions: null, modes: [] },
      ],
      maxIterations: 3,
      freshContextPerIteration: true,
      onExhausted: 'FailRun',
      onCriterionError: 'ReturnLastResponse',
    },
  },
  compaction: {
    strategy: 'Summarization',
    triggerTokens: 50000,
    triggerMessages: 40,
    triggerTurns: 10,
    minimumPreservedTurns: 2,
    minimumPreservedGroups: 1,
    maxContextWindowTokens: 64000,
    maxOutputTokens: 1000,
    summarizationPrompt: 'Summarize the older turns.',
    summarizationModel: {
      provider: 'openai',
      model: 'gpt-5.4-mini',
      temperature: null,
      maxOutputTokens: null,
      topP: null,
      reasoningEffort: null,
      providerSettings: {},
      responseFormat: null,
      fallbacks: [],
      responseCache: null,
      seed: null,
      topK: null,
      stopSequences: null,
      frequencyPenalty: null,
      presencePenalty: null,
      toolMode: null,
      allowMultipleToolCalls: null,
      reasoningOutput: null,
      toolLoop: null,
    },
  },
  memory: {
    enableFileMemory: true,
    enableTodo: true,
    enableTextSearch: true,
    enableVectorSearch: true,
    vectorCollection: 'handbook',
  },
  metadata: { owner: 'platform-team' } as never,
  parameters: [{ name: 'tone', kind: 'Text', required: true, defaultValue: 'warm', description: 'Voice of the answer.' }],
  sharedInstructionsName: 'shared-block',
};

/** The functions the code registered: one with settings, one without. */
const workflowFunctions = [
  {
    name: 'word-count',
    inputType: 'System.Collections.Generic.List`1[Microsoft.Extensions.AI.ChatMessage]',
    outputType: 'System.Collections.Generic.List`1[Microsoft.Extensions.AI.ChatMessage]',
    settingsSchema: {
      type: 'object',
      properties: { language: { type: 'string' }, limit: { type: 'integer' } },
      required: ['language'],
      additionalProperties: false,
    },
  },
  {
    name: 'pass-through',
    inputType: 'System.String',
    outputType: 'System.String',
    settingsSchema: null,
  },
];

function workflowCase(title: string, expected: Complete<WorkflowSaveRequest>): RoundTrip {
  return roundTrip<WorkflowSaveRequest>({
    source: 'screens/workflow-editor.tsx',
    path: '/api/workflows/{name}',
    title,
    routes: [
      fixture('GET', 'api/agents', [
        { name: 'drafter', origin: 'Database' },
        { name: 'reviewer', origin: 'Database' },
        { name: 'planner', origin: 'Database' },
        { name: 'summarizer', origin: 'Code' },
      ]),
      fixture('GET', 'api/workflows/functions', workflowFunctions),
      fixture('GET', 'api/workflows/:name', { name: 'chain', ...expected, tenantId: 'default', version: 4, updatedAt: stamp }),
    ],
    render: () => <WorkflowEditorScreen name="chain" />,
    save: async (user) => clickWhenEnabled(user, await screen.findByTestId('workflow-save')),
    expected,
  });
}

const ROUND_TRIPS: RoundTrip[] = [
  roundTrip<AgentDefinitionRequest>({
    source: 'screens/agent-editor/use-agent-editor.ts',
    path: '/api/agents/{name}',
    title: 'agent definition',
    routes: [
      fixture('GET', 'api/tools', []),
      fixture('GET', 'api/skills', []),
      fixture('GET', 'api/agent-extensions', [{ name: 'audit', description: 'Records every model call.' }]),
      fixture('GET', 'api/agents', []),
      fixture('GET', 'api/models', providers),
      fixture('GET', 'api/loop-criteria', {
        kinds: [
          { kind: 'completionMarker', builtIn: true, description: null },
          { kind: 'todoCompletion', builtIn: true, description: null },
          { kind: 'aiJudge', builtIn: true, description: null },
          { kind: 'backgroundTaskCompletion', builtIn: true, description: null },
          { kind: 'hasCitations', builtIn: false, description: 'Every claim carries a citation.' },
        ],
        judgeModel: { provider: 'openai', model: 'gpt-5.4-mini' },
      }),
      fixture('GET', 'api/agents/:name', {
        descriptor: { name: 'support', origin: 'Database', sourceName: 'database' },
        definition: { ...agentRequest, origin: 'Database', version: 3, tenantId: 'default', updatedAt: stamp },
      }),
    ],
    render: () => <AgentEditorScreen name="support" />,
    save: async (user) => {
      await waitFor(() => expect((screen.getByTestId('agent-name') as HTMLInputElement).value).toBe('support'));
      await clickWhenEnabled(user, screen.getByTestId('agent-save'));
    },
    expected: agentRequest,
  }),
  workflowCase('workflow with a manager agent', {
    displayName: 'Review chain',
    description: 'Drafts, then reviews.',
    kind: 'Magentic',
    agentNames: ['drafter', 'reviewer'],
    nodes: [],
    parameters: [
      { name: 'tone', kind: 'Text', required: true, defaultValue: 'formal', description: 'Voice of the review.' },
      { name: 'depth', kind: 'Number', required: false, defaultValue: null, description: null },
    ],
    managerAgentName: 'planner',
    maxIterations: 6,
    handoffInstructions: null,
    requirePlanApproval: true,
    // Phase 196 (F-405): written back exactly as stored, never re-formatted.
    runTimeout: '00:42:00',
    // Phase 209: written back exactly as stored; a save that dropped it would remove the limit.
    requestTimeout: '2.00:00:00',
    // Phase 208: a save that dropped it would turn a one-answer workflow back into Branch.
    responseMode: 'SingleAnswer',
  }),
  workflowCase('workflow with handoff instructions', {
    displayName: 'Triage',
    description: 'Routes a request.',
    kind: 'Handoff',
    agentNames: ['drafter', 'reviewer'],
    nodes: [],
    parameters: [],
    managerAgentName: null,
    maxIterations: 4,
    handoffInstructions: 'Hand billing questions over.',
    requirePlanApproval: false,
    runTimeout: null,
    requestTimeout: null,
    responseMode: 'Branch',
  }),
  workflowCase('workflow that mixes function nodes in', {
    displayName: 'Mixed chain',
    description: 'Summarizes, then counts words.',
    kind: 'Sequential',
    agentNames: [],
    nodes: [
      // The field shows seconds; an untouched value goes back in the server's own spelling.
      { name: 'summarizer', kind: 'Agent', settings: null, timeout: '00:06:00', agentVersion: null },
      { name: 'word-count', kind: 'Function', settings: { language: 'en', limit: 3 }, timeout: '00:00:02.5000000', agentVersion: null },
    ],
    parameters: [],
    managerAgentName: null,
    maxIterations: null,
    handoffInstructions: null,
    requirePlanApproval: false,
    runTimeout: '1.00:00:00',
    requestTimeout: null,
    responseMode: 'SingleAnswer',
  }),
  // Settings go back to the node they belong to: one function with them, one without, and a
  // value of every JSON kind inside (a number must stay a number, null must stay null).
  workflowCase('workflow whose function nodes carry different settings', {
    displayName: 'Two functions',
    description: 'Counts words, then passes the text on.',
    kind: 'Sequential',
    agentNames: [],
    nodes: [
      { name: 'word-count', kind: 'Function', settings: { language: 'tr', limit: 10, tags: ['a', 'b'], nested: { enabled: true }, empty: null }, timeout: null, agentVersion: null },
      { name: 'summarizer', kind: 'Agent', settings: null, timeout: null, agentVersion: null },
      // Below a millisecond the seconds field cannot say it: shown and sent back as stored.
      { name: 'pass-through', kind: 'Function', settings: null, timeout: '00:00:00.0000001', agentVersion: null },
    ],
    parameters: [{ name: 'tone', kind: 'Text', required: false, defaultValue: null, description: null }],
    managerAgentName: null,
    maxIterations: null,
    handoffInstructions: null,
    requirePlanApproval: false,
    runTimeout: null,
    requestTimeout: null,
    responseMode: 'Branch',
  }),
  // Phase 200: a pinned agent version goes back to the node it belongs to. Version 3 is not in the
  // agent's list here (the fixture has none), which must not cost the stored pin; the step beside
  // it stays on the current version, and a timeout rides along on a node list made by the pin.
  workflowCase('workflow that pins a stored agent to a version', {
    displayName: 'Pinned chain',
    description: 'Drafts on a fixed version, then reviews on the current one.',
    kind: 'Sequential',
    agentNames: [],
    nodes: [
      { name: 'drafter', kind: 'Agent', settings: null, timeout: null, agentVersion: 3 },
      { name: 'reviewer', kind: 'Agent', settings: null, timeout: '00:01:00', agentVersion: null },
    ],
    parameters: [],
    managerAgentName: null,
    maxIterations: null,
    handoffInstructions: null,
    requirePlanApproval: false,
    runTimeout: null,
    requestTimeout: null,
    responseMode: 'Branch',
  }),
  roundTrip<InboundTriggerSaveRequest>({
    source: 'screens/triggers.tsx',
    path: '/api/triggers/{name}',
    title: 'inbound trigger',
    routes: [
      fixture('GET', 'api/tenants/current', { tenantId: 'default' }),
      fixture('GET', 'api/triggers/:name', {
        name: 'slack',
        targetKind: 'Workflow',
        targetName: 'summarize-and-translate',
        signingSecretConfigurationName: 'Tracon:TriggerSecrets:slack',
        resolved: true,
        payloadMode: 'Path',
        payloadPath: 'event.text',
        enabled: false,
        createdAt: stamp,
        updatedAt: stamp,
      }),
    ],
    render: () => <TriggerEditorScreen name="slack" meta={testMeta} />,
    save: async (user) => clickWhenEnabled(user, await screen.findByRole('button', { name: 'Save' })),
    expected: {
      targetKind: 'Workflow',
      targetName: 'summarize-and-translate',
      signingSecretConfigurationName: 'Tracon:TriggerSecrets:slack',
      payloadMode: 'Path',
      payloadPath: 'event.text',
      enabled: false,
    },
  }),
  roundTrip<JobScheduleSaveRequest>({
    source: 'screens/jobs.tsx',
    path: '/api/schedules/{name}',
    title: 'job schedule',
    routes: [
      fixture('GET', 'api/schedules/handler-keys', ['tracon.workflow', 'contoso.report']),
      fixture('GET', 'api/schedules', [
        {
          id: '01a12790-0000-7000-8000-000000000001',
          tenantId: 'default',
          name: 'nightly',
          handlerKey: 'tracon.workflow',
          lane: 'reports',
          targetName: 'summarize-and-translate',
          cron: '0 2 * * *',
          timeZone: 'Europe/Istanbul',
          payload: ['first input', 'second input'],
          enabled: false,
          nextRunAt: null,
          lastRunAt: null,
          createdBy: null,
          createdAt: stamp,
          updatedAt: stamp,
        },
      ]),
    ],
    render: () => <JobsScreen meta={testMeta} />,
    save: async (user) => {
      await user.click(await screen.findByRole('button', { name: 'Edit' }));
      await clickWhenEnabled(user, screen.getByRole('button', { name: 'Save' }));
    },
    expected: {
      handlerKey: 'tracon.workflow',
      targetName: 'summarize-and-translate',
      cron: '0 2 * * *',
      timeZone: 'Europe/Istanbul',
      lane: 'reports',
      payload: ['first input', 'second input'],
      enabled: false,
    },
  }),
  roundTrip<EvalSuiteSaveRequest>({
    source: 'screens/evals.tsx',
    path: '/api/evals/{name}',
    title: 'eval suite',
    routes: [
      fixture('GET', 'api/evals', [
        {
          id: '01a12790-0000-7000-8000-000000000002',
          tenantId: 'default',
          name: 'support-eval',
          description: 'Measures support.',
          agentName: 'support',
          checks: [{ kind: 'keywords', values: ['refund'] }],
          createdAt: stamp,
          updatedAt: stamp,
        },
      ]),
    ],
    render: () => <EvalsScreen meta={testMeta} />,
    save: async (user) => {
      await user.click(await screen.findByRole('button', { name: 'Edit' }));
      await clickWhenEnabled(user, screen.getByRole('button', { name: 'Save' }));
    },
    expected: {
      description: 'Measures support.',
      agentName: 'support',
      checks: [{ kind: 'keywords', values: ['refund'] }],
    },
  }),
  roundTrip<EvalCaseInput[]>({
    source: 'screens/eval-detail.tsx',
    path: '/api/evals/{name}/cases',
    title: 'eval cases',
    routes: [
      fixture('GET', 'api/evals/:name', { name: 'support-eval', description: null, agentName: 'support', checks: [] }),
      fixture('GET', 'api/evals/:name/cases', [
        {
          id: '11111111-1111-1111-1111-111111111111',
          suiteId: '22222222-2222-2222-2222-222222222222',
          seq: 0,
          query: 'Where is my order?',
          expectedOutput: 'shipped',
          expectedTools: ['get_order_status'],
          context: 'Order 4182 shipped yesterday.',
          parameters: { customer: 'Acme' },
          sourceRunId: null,
          sourceKind: null,
          promotedAt: null,
        },
      ]),
      fixture('GET', 'api/evals/:name/runs', []),
    ],
    render: () => <EvalSuiteDetailScreen name="support-eval" meta={testMeta} />,
    save: async (user) => clickWhenEnabled(user, await screen.findByRole('button', { name: 'Save cases' })),
    expected: [
      {
        id: '11111111-1111-1111-1111-111111111111',
        query: 'Where is my order?',
        expectedOutput: 'shipped',
        expectedTools: ['get_order_status'],
        context: 'Order 4182 shipped yesterday.',
        parameters: { customer: 'Acme' },
      },
    ],
  }),
  roundTrip<ExperimentSaveRequest>({
    source: 'screens/experiments.tsx',
    path: '/api/experiments/{name}',
    title: 'draft experiment',
    routes: [
      fixture('GET', 'api/experiments', [
        {
          id: '01a12790-0000-7000-8000-000000000003',
          tenantId: 'default',
          name: 'version-comparison',
          agentName: 'support',
          variants: [
            { name: 'control', version: 1, weight: 70 },
            { name: 'candidate', version: 2, weight: 30 },
          ],
          status: 'Draft',
          assignmentKey: null,
          startedAt: null,
          endedAt: null,
          updatedAt: stamp,
          canary: null,
          rollbackReason: null,
        },
      ]),
      fixture('GET', 'api/agents/:name/versions', [
        { name: 'support', version: 1, model: { provider: 'anthropic', model: 'claude' }, origin: 'Database' },
        { name: 'support', version: 2, model: { provider: 'anthropic', model: 'claude' }, origin: 'Database' },
      ]),
    ],
    render: () => <ExperimentsScreen meta={testMeta} />,
    save: async (user) => {
      await user.click(await screen.findByRole('button', { name: 'Edit' }));
      await clickWhenEnabled(user, screen.getByTestId('experiment-save'));
    },
    expected: {
      agentName: 'support',
      variants: [
        { name: 'control', version: 1, weight: 70 },
        { name: 'candidate', version: 2, weight: 30 },
      ],
    },
  }),
  roundTrip<AgentSkillRequest>({
    source: 'screens/skills/skill-editor.tsx',
    path: '/api/skills/{name}',
    title: 'stored skill',
    routes: [
      fixture('GET', 'api/skills/:name', {
        id: '01a12790-0000-7000-8000-000000000004',
        tenantId: 'default',
        name: 'returns-policy',
        description: 'Explains the returns policy.',
        instructions: 'Quote the policy, then answer.',
        compatibility: 'tracon>=1.0',
        license: 'MIT',
        allowedTools: 'get_order_status',
        metadata: { owner: 'support-team' },
        enabled: false,
        version: 2,
        resources: [{ name: 'policy.md', description: 'The policy text.', mediaType: 'text/markdown', content: '# Returns' }],
        scripts: [
          {
            name: 'refund',
            description: 'Computes a refund.',
            extension: 'py',
            content: 'print(1)',
            parametersSchema: '{"type":"object"}',
            contentHash: 'ABC123',
          },
        ],
        origin: 'Database',
        createdAt: stamp,
        updatedAt: stamp,
      }),
    ],
    render: () => <SkillEditorScreen name="returns-policy" />,
    save: async (user) => clickWhenEnabled(user, await screen.findByRole('button', { name: 'Save' })),
    expected: {
      name: 'returns-policy',
      description: 'Explains the returns policy.',
      instructions: 'Quote the policy, then answer.',
      compatibility: 'tracon>=1.0',
      license: 'MIT',
      allowedTools: 'get_order_status',
      metadata: { owner: 'support-team' } as never,
      enabled: false,
      resources: [{ name: 'policy.md', description: 'The policy text.', mediaType: 'text/markdown', content: '# Returns' }],
      scripts: [
        {
          name: 'refund',
          description: 'Computes a refund.',
          extension: 'py',
          content: 'print(1)',
          parametersSchema: '{"type":"object"}',
          contentHash: 'ABC123',
        },
      ],
    },
  }),
  roundTrip<QuotaSaveRequest>({
    source: 'components/quota-panel.tsx',
    path: '/api/quotas',
    title: 'disabled quota',
    routes: [
      fixture('GET', 'api/quotas/usage', {
        tenantId: 'default',
        timeZone: 'UTC',
        usage: [],
        definitions: [
          {
            id: '01a12790-0000-7000-8000-000000000005',
            tenantId: 'default',
            agentName: 'support',
            period: 'Monthly',
            maxRuns: 100,
            maxTokens: 50000,
            maxCost: 12.5,
            enabled: false,
            createdAt: stamp,
            updatedAt: stamp,
          },
        ],
        dailyResetsAt: stamp,
        monthlyResetsAt: stamp,
      }),
    ],
    render: () => <QuotaPanel />,
    save: async (user) => {
      await user.click(await screen.findByRole('button', { name: 'Edit' }));
      await clickWhenEnabled(user, screen.getByTestId('quota-save'));
    },
    expected: {
      agentName: 'support',
      period: 'Monthly',
      maxRuns: 100,
      maxTokens: 50000,
      maxCost: 12.5,
      enabled: false,
    },
  }),
  roundTrip<RetentionPolicySaveRequest>({
    source: 'components/retention-panel.tsx',
    path: '/api/retention/{target}',
    title: 'retention policy',
    routes: [
      fixture('GET', 'api/retention', [
        {
          id: '01a12790-0000-7000-8000-000000000006',
          tenantId: 'default',
          target: 'run_events',
          maxAgeDays: 30,
          maxRows: 100000,
          archive: true,
          enabled: false,
          createdAt: stamp,
          updatedAt: stamp,
        },
      ]),
    ],
    render: () => <RetentionPanel canAdminister />,
    save: async (user) => {
      // The first row is `run_events`, the target the policy above belongs to.
      const [edit] = await screen.findAllByRole('button', { name: 'Edit' });

      await user.click(edit as HTMLElement);
      await clickWhenEnabled(user, screen.getByRole('button', { name: 'Save policy' }));
    },
    expected: { maxAgeDays: 30, maxRows: 100000, archive: true, enabled: false },
  }),
  roundTrip<TenantEgressPolicyRequest>({
    source: 'components/tenant-provider-panel.tsx',
    path: '/api/tenants/{tenantId}/egress',
    title: 'tenant egress policy',
    routes: [
      fixture('GET', 'api/tenants/current', { tenantId: 'acme' }),
      fixture('GET', 'api/models', providers),
      fixture('GET', 'api/tenants/:tenantId/egress', { tenantId: 'acme', allowedProviders: ['openai', 'anthropic'], updatedAt: stamp }),
    ],
    render: () => <TenantProviderPanel canAdminister />,
    save: async (user) => clickWhenEnabled(user, await screen.findByTestId('tenant-egress-save')),
    expected: { allowedProviders: ['openai', 'anthropic'] },
  }),
];

/**
 * PUTs whose form never loads a stored record, so there is nothing for it to
 * drop. Each entry says why; "it is fine" is not a reason.
 */
const CREATE_ONLY: Record<string, string> = {
  'screens/mcp.tsx::/api/mcp-servers/{name}':
    'The form only adds a server and starts empty; an absent `headers` keeps the stored value (K-870), and a filled ' +
    '`headerConfigurationKeys` carries the stored key names of a server saved under the same name (mcp.test.tsx).',
  'components/webhook-panel.tsx::/api/webhooks/{name}':
    'The form only adds a subscription and starts empty; an absent `headers` keeps the stored value (K-870).',
  'components/tenant-provider-panel.tsx::/api/tenants/{tenantId}/providers/{provider}':
    'The form only adds a binding and starts empty.',
  'screens/experiment-detail.tsx::/api/experiments/{name}/canary':
    'The form only appears while no policy exists; a stored policy is removed, never edited.',
};

/** `client.PUT('/api/x/{name}', …)` → the fixture pattern `api/x/:name`. */
function toPattern(path: string): string {
  return path.replace(/^\//, '').replace(/\{(\w+)\}/g, ':$1');
}

describe('a full-replace editor saved without a change sends back what it loaded', () => {
  let restoreFetch: () => void;

  afterEach(() => {
    restoreFetch();
  });

  it.each(ROUND_TRIPS.map((entry) => [entry.title, entry] as const))('%s', async (_, entry) => {
    let sent: unknown;

    restoreFetch = installApiMock([
      {
        method: 'PUT',
        pattern: toPattern(entry.path),
        handler: (_params, _url, body) => {
          sent = body;
          return {};
        },
      },
      ...entry.routes,
    ]);

    const user = userEvent.setup();

    renderScreen(entry.render());

    await entry.save(user);
    await expect.poll(() => sent).toBeDefined();

    expect(sent).toStrictEqual(entry.expected);
  });
});

describe('the PUT inventory', () => {
  // Raw source of every non-test module, keyed `./screens/x.tsx`.
  const sources = import.meta.glob(['./**/*.ts', './**/*.tsx', '!./**/*.test.ts', '!./**/*.test.tsx', '!./test/**'], {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>;

  const calls = Object.entries(sources).flatMap(([file, text]) =>
    [...text.matchAll(/\.PUT\(\s*'([^']+)'/g)].map((match) => `${file.replace(/^\.\//, '')}::${match[1]}`),
  );

  it('reads the source it scans', () => {
    // An empty glob would turn the check below into a pass that proves nothing.
    expect(Object.keys(sources).length).toBeGreaterThan(50);
    expect(calls.length).toBeGreaterThan(0);
  });

  it('writes every PUT path as a literal, so the inventory can see it', () => {
    const literal = calls.length;
    const total = Object.values(sources).reduce((count, text) => count + (text.match(/\.PUT\(/g)?.length ?? 0), 0);

    expect(total, 'a PUT path is built at run time — write it as a string literal').toBe(literal);
  });

  it('classifies every PUT as a round-trip case or a create-only form', () => {
    const known = new Set([...ROUND_TRIPS.map((entry) => `${entry.source}::${entry.path}`), ...Object.keys(CREATE_ONLY)]);
    const found = new Set(calls);

    expect(
      [...found].filter((call) => !known.has(call)),
      'a new PUT: add a round-trip case above, or a create-only entry with its reason',
    ).toEqual([]);
    expect(
      [...known].filter((call) => !found.has(call)),
      'a classified PUT no longer exists: remove its entry',
    ).toEqual([]);
  });
});

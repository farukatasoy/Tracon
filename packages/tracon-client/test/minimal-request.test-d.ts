import { expectTypeOf, test } from 'vitest';
import type { components } from '../src/schema.js';

type Schemas = components['schemas'];

/**
 * A request that sets only the members the document marks required has to compile, and a member the
 * document calls free-form has to take a value. These are compile-time assertions: a regression is a
 * type error, not a failed expectation, so the type check of this package is the gate.
 *
 * `schema-drift.test.ts` proves the committed schema equals what the generator writes from the
 * committed document. It cannot say the schema is usable; that is what this file says.
 */

test('an agent needs only a name and a model, and its free-form members take values', () => {
  const minimal: Schemas['AgentDefinitionRequest'] = { name: 'minimal', model: { provider: 'echo', model: 'echo-1' } };

  const withValues: Schemas['AgentDefinitionRequest'] = {
    name: 'configured',
    metadata: { owner: 'platform', tier: 2 },
    model: {
      provider: 'anthropic',
      model: 'claude',
      providerSettings: { 'anthropic.promptCaching': true, 'anthropic.thinking.budgetTokens': 8000 },
    },
  };

  const withNulls: Schemas['AgentDefinitionRequest'] = {
    name: 'nulls',
    metadata: null,
    model: { provider: 'echo', model: 'echo-1', providerSettings: null },
  };

  expectTypeOf(minimal).toExtend<Schemas['AgentDefinitionRequest']>();
  expectTypeOf(withValues.model.providerSettings).toEqualTypeOf<Schemas['ModelBinding']['providerSettings']>();
  expectTypeOf(withNulls.metadata).toEqualTypeOf<Schemas['AgentDefinitionRequest']['metadata']>();
});

test('a free-form dictionary is a string-keyed bag of unknown values, null or absent', () => {
  expectTypeOf<Schemas['ModelBinding']['providerSettings']>().toEqualTypeOf<{ [key: string]: unknown } | null | undefined>();
  expectTypeOf<Schemas['AgentDefinitionRequest']['metadata']>().toEqualTypeOf<{ [key: string]: unknown } | null | undefined>();
  expectTypeOf<Schemas['AgentSkillRequest']['metadata']>().toEqualTypeOf<{ [key: string]: unknown } | null | undefined>();
  expectTypeOf<Schemas['EvalExpectedToolCall']['arguments']>().toEqualTypeOf<{ [key: string]: unknown } | null | undefined>();
});

test('a skill needs only a name, a description and its instructions', () => {
  const minimal: Schemas['AgentSkillRequest'] = { name: 'minimal', description: 'A skill.', instructions: 'Do the thing.' };

  expectTypeOf(minimal).toExtend<Schemas['AgentSkillRequest']>();
});

test('an eval suite needs only an agent name, and checks may be left out or null', () => {
  const minimal: Schemas['EvalSuiteSaveRequest'] = { agentName: 'kod-agent' };
  const nulled: Schemas['EvalSuiteSaveRequest'] = { agentName: 'kod-agent', checks: null };
  const checked: Schemas['EvalSuiteSaveRequest'] = { agentName: 'kod-agent', checks: [{ kind: 'nonEmpty' }] };

  expectTypeOf(minimal).toExtend<Schemas['EvalSuiteSaveRequest']>();
  expectTypeOf(nulled.checks).toEqualTypeOf<Schemas['EvalSuiteSaveRequest']['checks']>();
  expectTypeOf(checked.checks).toEqualTypeOf<Schemas['EvalSuiteSaveRequest']['checks']>();
});

test('a schedule needs only a handler and a target; payload, time zone and enabled may be left out', () => {
  const minimal: Schemas['JobScheduleSaveRequest'] = { handlerKey: 'tracon.agent-batch', targetName: 'kod-agent' };

  expectTypeOf(minimal).toExtend<Schemas['JobScheduleSaveRequest']>();
  expectTypeOf<Schemas['JobScheduleSaveRequest']['timeZone']>().toEqualTypeOf<string | undefined>();
  expectTypeOf<Schemas['JobScheduleSaveRequest']['enabled']>().toEqualTypeOf<boolean | undefined>();
});

test('an MCP server needs only an endpoint; its approval gate and enabled flag are optional members with a documented default', () => {
  const minimal: Schemas['McpServerRequest'] = { endpoint: 'https://mcp.example.com/' };

  expectTypeOf(minimal).toExtend<Schemas['McpServerRequest']>();
  expectTypeOf<Schemas['McpServerRequest']['requiresApproval']>().toEqualTypeOf<boolean | undefined>();
  expectTypeOf<Schemas['McpServerRequest']['enabled']>().toEqualTypeOf<boolean | undefined>();
});

test('an enum alias does not carry null: the member that is nullable says so on its own wrapper', () => {
  expectTypeOf<null>().not.toExtend<Schemas['RunErrorClass']>();
  expectTypeOf<null>().not.toExtend<Schemas['WorkflowKind']>();
  expectTypeOf<Schemas['RunError']['class']>().toEqualTypeOf<Schemas['RunErrorClass'] | null | undefined>();
});

import { describe, expect, it } from 'vitest';
import type { AgentDefinition } from '../../lib/server-types';
import { emptyForm, fromDefinition, toRequest, withDefaultProvider } from './model';

describe('withDefaultProvider', () => {
  it('defaults to the first catalog provider when the form has none', () => {
    const next = withDefaultProvider(emptyForm, ['anthropic', 'openai']);

    expect(next.provider).toBe('anthropic');
  });

  it('leaves an empty form unchanged when the catalog is empty', () => {
    const next = withDefaultProvider(emptyForm, []);

    expect(next).toBe(emptyForm);
  });

  // HATA-S4-009 / MT-UIAG-014: the effect that loads an existing definition
  // and the effect that defaults the provider can both settle in the same
  // React commit. When that happens this function receives `current` as the
  // *already-loaded* form (the definition's real provider applied first),
  // not the stale pre-load form its own effect closed over. It must not
  // overwrite a provider that is already set, even though it differs from
  // the catalog's first entry.
  it('does not overwrite a provider already set by a concurrent update', () => {
    const loadedFromDefinition: typeof emptyForm = { ...emptyForm, provider: 'openai' };

    const next = withDefaultProvider(loadedFromDefinition, ['anthropic', 'google']);

    expect(next.provider).toBe('openai');
  });
});

// B01: the editor rebuilds the request from form state, and `PUT /api/agents`
// is a full replace — so a definition field the form never carries is not
// "left alone" by an edit, it is erased. Measured loss: `parameters`,
// `sharedInstructionsName`, the three `model` sub-fields, and a `memory` that
// only had vector search turned on.
describe('definition round trip', () => {
  const definition: AgentDefinition = {
    name: 'review-agent',
    displayName: 'Review Agent',
    description: 'Reviews pull requests.',
    instructions: 'Be brief.',
    model: {
      provider: 'openai',
      model: 'configured-model',
      temperature: null,
      maxOutputTokens: null,
      topP: null,
      fallbacks: [],
      providerSettings: { 'anthropic.promptCaching': true } as never,
      responseCache: { enabled: true, lifetime: '00:05:00' },
      allowConcurrentToolCalls: true,
    },
    toolNames: ['diff-tool'],
    skillNames: [],
    callableAgentNames: [],
    subAgents: { waitTimeout: '00:00:30' },
    mcpResourceUris: ['docs:file:///handbook.md'],
    memory: {
      enableFileMemory: false,
      enableTodo: false,
      enableTextSearch: false,
      enableVectorSearch: true,
      vectorCollection: 'internal-documents',
    },
    metadata: { owner: 'platform-team' } as never,
    parameters: [{ name: 'tone', kind: 'Text', required: false }],
    sharedInstructionsName: 'shared-block',
    compaction: null,
    harness: null,
    origin: 'Database',
    version: 3,
  };

  const request = toRequest(fromDefinition(definition));

  it('keeps the fields the editor has no control for', () => {
    expect(request.parameters).toEqual(definition.parameters);
    expect(request.sharedInstructionsName).toBe('shared-block');
    expect(request.subAgents).toEqual(definition.subAgents);
    expect(request.mcpResourceUris).toEqual(definition.mcpResourceUris);
    expect(request.metadata).toEqual(definition.metadata);
  });

  it('keeps the model sub-fields the editor rebuilds around', () => {
    expect(request.model.providerSettings).toEqual(definition.model.providerSettings);
    expect(request.model.responseCache).toEqual(definition.model.responseCache);
    expect(request.model.allowConcurrentToolCalls).toBe(true);
  });

  // The old predicate asked only about file/todo/text search, so a definition
  // whose sole memory feature was vector search serialized as `memory: null`.
  it('keeps a memory block that only has vector search turned on', () => {
    expect(request.memory).toEqual(definition.memory);
  });

  it('still drops a memory block in which nothing is turned on', () => {
    const nothingOn = {
      enableFileMemory: false,
      enableTodo: false,
      enableTextSearch: false,
      enableVectorSearch: false,
    };

    const bare = toRequest(fromDefinition({ ...definition, memory: nothingOn }));

    expect(bare.memory).toBeNull();
  });
});
